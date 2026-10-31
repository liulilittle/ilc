using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

/// <summary>
/// Shared method-signature decoding used by MetadataLoader (populates MethodDef.ParamCount /
/// ParamTypes), MethodRefResolver (signature-aware method-name hashing) and MethodBodyReader
/// (overload body matching by signature).
/// The encoded parameter-type string is the compact, stable hash input for the method name
/// ({owner}.{name}({paramTypes})): overloads differ in param types, so overloads must not
/// collide even though they share a metadata name.
/// Encoding: i=int32, v=void, r:{FullName}=reference type (string/class/object),
///           V:{FullName}=value type, T:{FullName}=ECMA type-handle encoding (nested types),
///           P:{...}=unmanaged pointer, tr=TypedReference, ...=vararg sentinel marker.
/// Native int/uint ride the raw ELEMENT_TYPE_I/U bytes (0x18/0x19) and encode
/// as "n"/"u" (intptr_t/uintptr_t pointer-width slots) -- the ECMA-335 PRINTED
/// table (I=0x17, U=0x18) is an erratum; Roslyn/net48 csc emit 0x18/0x19
/// (pinvokeext 00-01-18-0E probe).
/// </summary>
public static class MethodSignatureCoder
{
    /// <summary>
    /// Decodes a method signature blob into
    /// (paramCount, returnType, encoded param types, returnTypeName).
    /// The 4th element is the FULL encoded return-type name for nameful
    /// returns (0x11 VALUETYPE / 0x12 CLASS / 0x15 GENERICINST / 0x40
    /// type-handle / SZARRAY / BYREF / PTR / multidim ARRAY / FNPTR), "" for
    /// primitive/void/leaf returns (refl2 S1/D1: the MethodDef return-type
    /// full-name decode that lets EncodeReturnType name reference/value/
    /// generic-inst returns -- the G2 guard's removal condition).
    /// </summary>
    public static (int ParamCount, SignatureTypeCode ReturnType, string ParamTypes, string ReturnTypeName) Decode(
        MetadataReader md, BlobHandle sigHandle)
    {
        // varargs (ECMA-335 II.23.2.2): a call-site (MemberRef) signature is
        //   [callconv VARARG][ParamCount][ret][fixed types...][SENTINEL 0x41][vararg types...]
        // where ParamCount counts the fixed AND vararg parameters (the sentinel
        // is NOT a parameter). The method-name hash channel must see the FIXED
        // parameters ONLY -- the callee's MethodDef signature carries exactly
        // those (a definition has no sentinel), so hashing the call-site vararg
        // types here would never match the generated symbol. DecodeVarArgs
        // returns the split; Decode stays the historical fixed-only view.
        var v = DecodeVarArgs(md, sigHandle);
        return (v.FixedCount, v.ReturnType, v.FixedParamTypes, v.ReturnTypeName);
    }

    /// <summary>
    /// varargs-aware signature decode (ECMA-335 II.23.2.2 / III.3.9).
    /// Returns the calling-convention varargs flag, the FIXED parameter count
    /// and encoded types (the hash/ABI channel), and the optional (vararg)
    /// parameter types in call-site order. The sentinel (ELEMENT_TYPE_SENTINEL,
    /// 0x41) is a separator, not a parameter: it is consumed without taking a
    /// slot. For a method DEFINITION the signature is
    /// `[callconv VARARG][count][ret][fixed types...]` (no sentinel), so
    /// IsVarArgs is true while VarArgTypes is empty.
    /// </summary>
    public static (bool IsVarArgs, int FixedCount, SignatureTypeCode ReturnType,
        string FixedParamTypes, string ReturnTypeName, string[] VarArgTypes) DecodeVarArgs(
        MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        var header = sig.ReadSignatureHeader();
        if (header.IsGeneric) sig.ReadCompressedInteger(); // M2 has no method-level generics, just skip
        var paramCount = sig.ReadCompressedInteger();
        // The return type is also read as a raw byte, consuming its trailing TypeDefOrRef
        // (class/value-type returns); otherwise the param loop would start parsing at the
        // TypeDefOrRef coded index and produce a wrong hash input.
        var returnByte = SkipCustomModifiers(ref sig, sig.ReadByte());
        var returnType = (SignatureTypeCode)returnByte;
        var returnTypeName = EncodeReturn(md, ref sig, returnByte);
        var fixedParts = new List<string>();
        var varParts = new List<string>();
        var inVarArgs = false;
        while (fixedParts.Count + varParts.Count < paramCount)
        {
            if (sig.RemainingBytes == 0)
                throw new NotSupportedException(
                    $"signature ended after {fixedParts.Count + varParts.Count} of {paramCount} parameters (malformed signature blob)");
            var first = sig.ReadByte();
            if (first == 0x41)
            {
                // ELEMENT_TYPE_SENTINEL: the fixed/optional boundary. Legal at
                // most once and only for a VARARG signature; a repeat/late
                // sentinel is malformed metadata.
                if (inVarArgs)
                    throw new NotSupportedException("signature carries more than one ELEMENT_TYPE_SENTINEL (malformed vararg signature)");
                inVarArgs = true;
                continue;
            }
            var encoded = EncodeTypeFromByte(md, ref sig, first);
            (inVarArgs ? varParts : fixedParts).Add(encoded);
        }
        return (header.CallingConvention == SignatureCallingConvention.VarArgs,
            fixedParts.Count, returnType, string.Join(",", fixedParts), returnTypeName, varParts.ToArray());
    }

    /// <summary>
    /// ECMA-335 II.23.1.16 custom modifiers: CMOD_REQD (0x1F) / CMOD_OPT (0x20)
    /// each carry a TypeDefOrRefOrSpec coded index followed by the type they
    /// modify. Every type-reading entry point must consume the modifier(s) and
    /// continue with the REAL type byte (volatile fields, `in` parameters and
    /// modreq'd returns would otherwise misparse -- e.g. `static volatile int`
    /// has field sig 06 1F 09 08 where 0x1F is IsVolatile's modreq).
    /// </summary>
    public static byte SkipCustomModifiers(ref BlobReader sig, byte b)
    {
        while (b is 0x1F or 0x20)
        {
            // A well-formed modifier carries a TypeDefOrRefOrSpec coded index
            // AND the type it modifies. A blob that ends early is malformed
            // (e.g. the reserved-byte matrix probes) -- reject loudly with the
            // offending byte, never misparse.
            if (sig.RemainingBytes == 0)
                throw new NotSupportedException($"signature custom modifier 0x{b:X2} has no coded index (malformed signature blob)");
            _ = sig.ReadCompressedInteger(); // TypeDefOrRefOrSpec coded index
            if (sig.RemainingBytes == 0)
                throw new NotSupportedException($"signature custom modifier 0x{b:X2} has no underlying type (malformed signature blob)");
            b = sig.ReadByte();
        }
        return b;
    }

    /// <summary>
    /// R2b P2 (parallel VAR channel): decode ONLY which signature slots are
    /// generic parameters (0x13 VAR / 0x1E MVAR). The main Decode keeps the raw
    /// "i"/"" encodings (the hash channel -- A3 C3: the C++ name hash must not
    /// drift); a closed generic clone consumes THESE flags to render T as its
    /// concrete C++ type (Il2CppObject* for reference instantiations) without
    /// touching the hash input.
    /// P-G3 (ParamIsByrefVar): a BYREF-wrapped generic parameter (0x10 BYREF +
    /// 0x13/0x1E -- `ref T` / `out T`) is the byref object slot (Il2CppObject**
    /// in the shared object ABI) -- the third channel the definition-side
    /// emitter consumes to declare the byref T parameters. A by-value VAR/MVAR
    /// is NOT byref-VAR.
    /// </summary>
    public static (bool ReturnIsVar, bool[] ParamIsVar, bool[] ParamIsByrefVar, int ReturnVarIndex, int[] ParamVarIndex) DecodeVarFlags(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        var header = sig.ReadSignatureHeader();
        if (header.IsGeneric) sig.ReadCompressedInteger();
        var paramCount = sig.ReadCompressedInteger();
        var returnByte = SkipCustomModifiers(ref sig, sig.ReadByte());
        var returnIsVar = returnByte is 0x13 or 0x1E;
        // GN-14 (c): carry the VAR (0x13) parameter index (MVAR 0x1E is
        // method-level -- no class parts -> -1, preserving the legacy behavior).
        var returnVarIndex = -1;
        if (returnByte == 0x13) returnVarIndex = sig.ReadCompressedInteger();
        else ConsumeVarSigType(md, ref sig, returnByte);
        var flags = new bool[paramCount];
        var byrefFlags = new bool[paramCount];
        var varIndex = new int[paramCount];
        for (var i = 0; i < paramCount; i++)
        {
            var b = SkipCustomModifiers(ref sig, sig.ReadByte());
            varIndex[i] = -1;
            if (b == 0x10) // BYREF wrapper: the POINTEE decides the slot
            {
                var pointee = SkipCustomModifiers(ref sig, sig.ReadByte());
                flags[i] = pointee is 0x13 or 0x1E;
                byrefFlags[i] = pointee is 0x13 or 0x1E;
                if (pointee == 0x13) varIndex[i] = sig.ReadCompressedInteger();
                else ConsumeVarSigType(md, ref sig, pointee);
            }
            else
            {
                flags[i] = b is 0x13 or 0x1E;
                if (b == 0x13) varIndex[i] = sig.ReadCompressedInteger();
                else ConsumeVarSigType(md, ref sig, b);
            }
        }
        return (returnIsVar, flags, byrefFlags, returnVarIndex, varIndex);
    }

    // Consume one signature type byte plus its trailing payload (the minimal
    // alignment walk DecodeVarFlags needs -- it never materializes the names).
    private static void ConsumeVarSigType(MetadataReader md, ref BlobReader sig, byte b)
    {
        switch (b)
        {
            case 0x11 or 0x12 or 0x40:
                sig.ReadTypeHandle();
                break;
            case 0x13 or 0x1E:
                sig.ReadCompressedInteger();
                break;
            // ECMA-335 custom modifier (0x1F/0x20): coded index then the
            // modified type -- consume the real type recursively.
            case 0x1F or 0x20:
                _ = sig.ReadCompressedInteger();
                ConsumeVarSigType(md, ref sig, sig.ReadByte());
                break;
            case 0x0F or 0x10 or 0x1D:
                ConsumeVarSigType(md, ref sig, sig.ReadByte());
                break;
            case 0x14:
                ConsumeVarSigType(md, ref sig, sig.ReadByte());
                var rank = sig.ReadCompressedInteger();
                var numSizes = sig.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
                var numLo = sig.ReadCompressedInteger();
                for (var i = 0; i < numLo; i++) _ = sig.ReadCompressedInteger();
                break;
            case 0x15:
                _ = sig.ReadByte(); // CLASS/VALUETYPE marker
                _ = sig.ReadTypeHandle();
                var argc = sig.ReadCompressedInteger();
                for (var i = 0; i < argc; i++)
                    ConsumeVarSigType(md, ref sig, sig.ReadByte());
                break;
            case 0x1B:
                _ = sig.ReadByte(); // calling convention
                var fc = sig.ReadCompressedInteger();
                ConsumeVarSigType(md, ref sig, sig.ReadByte());
                for (var i = 0; i < fc; i++)
                    ConsumeVarSigType(md, ref sig, sig.ReadByte());
                break;
            default:
                break; // leaf bytes carry no trailing data
        }
    }

    /// <summary>
    /// Decodes a PROPERTY signature blob (ECMA-335 II.23.2.4) into
    /// (indexerParamCount, propertyTypeName). The property signature is the
    /// method-signature shape with the PROPERTY calling-convention byte
    /// (0x08 | HAS_THIS): [paramCount][property type][indexer params...].
    /// refl3 (m7d): the property TYPE occupies the method signature's "return"
    /// slot. Its full name decodes via EncodePropertyType -- the FULL type name
    /// ("System.Int32", "Calc.Node", "System.String"), NOT the EncodeReturn
    /// method-return encoding (which is "" for primitive/leaf types): the
    /// typeinfo.h Il2CppPropertyInfo.typeName contract is the property type
    /// full name, mirroring the field-table typeName convention. The indexer
    /// params are consumed (their types are not surfaced in the typeinfo
    /// property table, only the count).
    /// </summary>
    public static (int IndexParamCount, string PropertyTypeName) DecodeProperty(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        // The property signature's first byte is 0x08 (PROPERTY) or 0x28
        // (PROPERTY | HAS_THIS). Read it as a RAW byte -- the compiler's
        // SignatureCallingConvention enum has no Property member, so the
        // low-nibble check is the portable form.
        var header = sig.ReadByte();
        if ((header & 0x0F) != 0x08)
            throw new NotSupportedException($"property signature with calling convention 0x{header:X2} not supported in refl3 (expected PROPERTY 0x08)");
        var paramCount = sig.ReadCompressedInteger();
        var typeName = EncodePropertyType(md, ref sig);
        for (var i = 0; i < paramCount; i++)
            EncodeType(md, ref sig); // indexer parameter types: consumed, not surfaced
        return (paramCount, typeName);
    }

    // Property-type full-name decode (refl3/D8): the FULL ECMA type set a
    // property type can carry, decoded into the field-table typeName
    // convention (typeinfo.h Il2CppPropertyInfo.typeName -- "System.Int32" for
    // primitives, the type's full name for class/value types, "[]" suffixes
    // for arrays, "{def}<{arg},...>" for generic instantiations). Mirrors
    // MetadataLoader.ReadTypeName (the field-table decoder) EXCEPT generic
    // args keep the compact EncodeType form (the MethodSignatureCoder
    // encoding language this class already owns -- reflops probe: Func`2
    // property arg bytes 08 08 -> "i,i", matching the method-table
    // signature convention).
    private static string EncodePropertyType(MetadataReader md, ref BlobReader sig)
    {
        var b = SkipCustomModifiers(ref sig, sig.ReadByte());
        return b switch
        {
            0x02 => "System.Boolean",
            0x03 => "System.Char",
            0x04 => "System.SByte",
            0x05 => "System.Byte",
            0x06 => "System.Int16",
            0x07 => "System.UInt16",
            0x08 => "System.Int32",
            0x09 => "System.UInt32",
            0x0A => "System.Int64",
            0x0B => "System.UInt64",
            0x0C => "System.Single",
            0x0D => "System.Double",
            0x0E => "System.String",
            0x1C => "System.Object",
            0x11 => ResolveTypeName(md, sig.ReadTypeHandle()),
            0x12 => ResolveTypeName(md, sig.ReadTypeHandle()),
            0x40 => ResolveTypeName(md, sig.ReadTypeHandle()),
            0x18 => "System.IntPtr",
            0x19 => "System.UIntPtr",
            0x13 or 0x1E => ConsumeGenericParamIndex(ref sig), // VAR/MVAR: index consumed, "i" slot (default instantiation)
            0x1D or 0x1F => EncodePropertyType(md, ref sig) + "[]",
            0x15 => PropertyGenericInst(md, ref sig),
            0x14 => PropertyMultidim(md, ref sig),
            // Tolerant fallback: exotic property types map to Object.
            var other => "System.Object",
        };
    }

    // ELEMENT_TYPE_GENERICINST property type: marker byte + TypeDefOrRef +
    // compressed arg count + one type per argument (same shape as the method
    // signature GenericInst, reusing its arg encoding).
    private static string PropertyGenericInst(MetadataReader md, ref BlobReader sig)
    {
        sig.ReadByte(); // marker byte (0x11/0x12)
        var defHandle = sig.ReadTypeHandle();
        var n = sig.ReadCompressedInteger();
        var args = new string[n];
        for (var i = 0; i < n; i++) args[i] = EncodeType(md, ref sig);
        return ResolveTypeName(md, defHandle) + "<" + string.Join(",", args) + ">";
    }

    // ELEMENT_TYPE_ARRAY property type: element type + rank + sizes/lobounds
    // (bounds consumed for alignment; the name carries the rank only).
    private static string PropertyMultidim(MetadataReader md, ref BlobReader sig)
    {
        var elem = EncodePropertyType(md, ref sig);
        var rank = sig.ReadCompressedInteger();
        var numSizes = sig.ReadCompressedInteger();
        for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
        var numLoBounds = sig.ReadCompressedInteger();
        for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
        return elem + "[" + rank + "]";
    }

    /// <summary>
    /// Decodes a calli StandAloneSig (ECMA-335 I.12.3.3.3 indirect call): the
    /// blob is a method signature whose CALLING CONVENTION byte is the fnptr's
    /// unmanaged convention (0x00 default / 0x01 C / 0x02 StdCall / 0x03
    /// ThisCall / 0x04 FastCall) and whose params describe the target
    /// function. Used by StackFold (calli fold) and CodeGenerator (emission).
    /// Returns (callingConvention, hasThis, paramCount, returnType, encoded
    /// params). callingConvention is the low nibble (0x00..0x04); hasThis is
    /// the HAS_THIS flag (the IL pushes the receiver as the first arg, and the
    /// emitted C++ function pointer must include it).
    /// </summary>
    public static (int CallingConvention, bool HasThis, int ParamCount, SignatureTypeCode ReturnType, string ParamTypes) DecodeStandalone(
        MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        var conv = sig.ReadByte();
        // fnptr-b23: a calli StandAloneSig carries the fnptr's calling
        // convention in the LOW NIBBLE (0x00/0x01/0x02/0x03/0x04); the high
        // nibble holds flags (0x10 GENERIC, 0x20 HAS_THIS, 0x40 EXPLICIT_THIS).
        // A GENERIC fnptr signature cannot be emitted (the generated C++ has no
        // generic instantiation context); reject loudly.
        var callingConvention = conv & 0x0F;
        if ((conv & 0x10) != 0)
            throw new NotSupportedException($"calli signature with GENERIC calling convention (0x{conv:X2}) not supported in fnptr-b23");
        // HAS_THIS (0x20): the target takes an implicit this. The IL pushes the
        // receiver like the first explicit arg; the emitted C++ function
        // pointer must include it. EXPLICIT_THIS (0x40) is rejected (it has no
        // stack representation).
        var hasThis = (conv & 0x20) != 0;
        if ((conv & 0x40) != 0)
            throw new NotSupportedException($"calli signature with EXPLICIT_THIS calling convention (0x{conv:X2}) not supported in fnptr-b23");
        var paramCount = sig.ReadCompressedInteger();
        var returnByte = SkipCustomModifiers(ref sig, sig.ReadByte());
        var returnType = (SignatureTypeCode)returnByte;
        ConsumeReturnTrailing(md, ref sig, returnByte);
        var parts = new string[paramCount];
        for (var i = 0; i < paramCount; i++)
            parts[i] = EncodeType(md, ref sig);
        return (callingConvention, hasThis, paramCount, returnType, string.Join(",", parts));
    }

    // Raw-byte type decoding: BlobReader.ReadSignatureTypeCode() remaps the ECMA VALUETYPE (0x11)
    // and CLASS (0x12) bytes to the enum value TypeHandle (0x40), so it cannot distinguish a value
    // type from a reference type -- the distinction the compact encoding needs. Reading the raw byte
    // preserves it. VALUETYPE/CLASS/TypeHandle are each followed by a TypeDefOrRef coded index.
    // M5 BCL wiring: the BCL member signatures reach this encoder for EVERY MemberRef the
    // transpiler touches (Decode runs before the resolver's owner/name dispatch), so every
    // primitive code a BCL method can carry must decode here. Slots: bool/char/sbyte/byte/short/
    // ushort/native-int live in int32_t slots ("b"/"c"/"i1"/"u1"/"i2"/"u2"/"n"); int64/uint64 in
    // int64_t slots ("l"/"q"); float/double in double slots ("f"/"d"); arrays are managed
    // references ("r:...[]").
    private static string EncodeType(MetadataReader md, ref BlobReader sig)
        => EncodeTypeFromByte(md, ref sig, sig.ReadByte());

    // Encode one type whose FIRST byte was already read by the caller (the
    // varargs decoder needs to peek for the 0x41 separator without consuming a
    // parameter slot; the blob reader has no un-read).
    private static string EncodeTypeFromByte(MetadataReader md, ref BlobReader sig, byte firstByte)
    {
        var tb = SkipCustomModifiers(ref sig, firstByte);
        return tb switch
    {
        0x01 => "v",                                    // ELEMENT_TYPE_VOID
        0x02 => "b",                                    // ELEMENT_TYPE_BOOLEAN (int32 slot)
        0x03 => "c",                                    // ELEMENT_TYPE_CHAR (int32 slot)
        0x04 => "i1",                                   // ELEMENT_TYPE_I1 (int32 slot)
        0x05 => "u1",                                   // ELEMENT_TYPE_U1 (int32 slot)
        0x06 => "i2",                                   // ELEMENT_TYPE_I2 (int32 slot)
        0x07 => "u2",                                   // ELEMENT_TYPE_U2 (int32 slot)
        0x08 => "i",                                    // ELEMENT_TYPE_I4 (int32)
        // Audit H1: Roslyn emits 'uint' parameters as the RAW U4 byte (0x09),
        // not as VALUETYPE System.UInt32. The model stores uint values in
        // int32_t slots (same ABI); the unsigned semantics live at the
        // comparison site (Cgt_un/Clt_un emit uint32_t casts). The encoding
        // stays "i" so method hashing/ABI stay uniform with int32.
        0x09 => "i",                                    // ELEMENT_TYPE_U4 (uint32 -- stored in int32_t slots)
        0x0A => "l",                                    // ELEMENT_TYPE_I8 (int64 slot)
        0x0B => "q",                                    // ELEMENT_TYPE_U8 (uint64 slot)
        0x0C => "f",                                    // ELEMENT_TYPE_R4 (float -- stored in double slots)
        0x0D => "d",                                    // ELEMENT_TYPE_R8 (double slot)
        0x0E => "r:System.String",                      // ELEMENT_TYPE_STRING
        0x0F => "P:" + EncodeType(md, ref sig),         // ELEMENT_TYPE_PTR: unmanaged pointer -- pointee type follows
        0x10 => "B:" + EncodeType(md, ref sig),         // ELEMENT_TYPE_BYREF (out/ref params -- TryParse etc.)
        0x11 => "V:" + ResolveTypeName(md, sig.ReadTypeHandle()), // ELEMENT_TYPE_VALUETYPE
        0x12 => "r:" + ResolveTypeName(md, sig.ReadTypeHandle()), // ELEMENT_TYPE_CLASS
        0x13 or 0x1E => ConsumeGenericParamIndex(ref sig),        // VAR/MVAR: generic parameter -- index byte follows
        0x14 => "r:" + MultidimArray(md, ref sig),      // ELEMENT_TYPE_ARRAY (int[,] etc.)
        0x15 => "r:" + GenericInst(md, ref sig),        // ELEMENT_TYPE_GENERICINST
        0x16 => "tr",                                   // ELEMENT_TYPE_TYPEDBYREF (no trailing data)
        // Native int/uint -- RAW wire bytes 0x18/0x19 (verified: nint probe
        // sig 00 02 18 18 19; pinvokeext reverse_static return 00-01-18-0E).
        // 0x17 is reserved in real metadata (ECMA-335's printed I=0x17 is an
        // erratum -- Roslyn/net48 csc emit 0x18 for nint).
        // "n" = native int (intptr_t), "u" = native UNSIGNED int (uintptr_t).
        // The old 0x19 -> "i" encoding declared a UIntPtr parameter as
        // int32_t and truncated every x64 value above 2^31 on the way in
        // (the same width defect as the "i" local default); the unsigned
        // carrier must stay distinct from signed int32.
        0x18 => "n",                                    // ELEMENT_TYPE_I (native int -> intptr_t)
        0x19 => "u",                                    // ELEMENT_TYPE_U (native uint -> uintptr_t)
        0x1B => "F:" + FnPtrSig(md, ref sig),           // ELEMENT_TYPE_FNPTR
        0x1C => "r:System.Object",                      // ELEMENT_TYPE_OBJECT
        0x1D => "r:" + EncodeType(md, ref sig).TrimStart('r', ':') + "[]", // ELEMENT_TYPE_SZARRAY
        0x40 => "T:" + ResolveTypeName(md, sig.ReadTypeHandle()), // ELEMENT_TYPE type-handle (nested)
        0x41 => "...",                                  // ELEMENT_TYPE_SENTINEL (vararg separator -- no trailing data)
        var b => throw new NotSupportedException($"signature parameter type byte 0x{b:X2} not supported in M2"),
    };
    }

    // ELEMENT_TYPE_FNPTR (0x1B) trailing payload: a full method signature --
    // calling-convention byte (0x00 default / 0x01 C / 0x02 StdCall / 0x03
    // ThisCall / 0x04 FastCall for unmanaged fnptrs), compressed param count,
    // return type, then one type per parameter (ECMA-335 II.23.2.16). Each
    // nested type is recursively decoded. Encoded "F:ret(params)".
    // NOTE: the inner param list uses ';' (NOT ',') -- the whole encoding is
    // embedded in MethodDef.ParamTypes, a COMMA-joined string consumed by
    // MethodBodyReader (read-only signature matching) and ParamTypesCpp
    // (split on ','). A comma inside the fnptr sig would fragment the outer
    // split ("F:i(i,i)" -> "F:i(i", "i)") and misalign every later param.
    private static string FnPtrSig(MetadataReader md, ref BlobReader sig)
    {
        sig.ReadByte(); // calling convention
        var count = sig.ReadCompressedInteger();
        var ret = EncodeType(md, ref sig);
        var parts = new string[count];
        for (var i = 0; i < count; i++) parts[i] = EncodeType(md, ref sig);
        return ret + "(" + string.Join(";", parts) + ")";
    }

    // Return-type trailing data consumption shared by Decode and
    // DecodeStandalone: the return byte's own trailing payload (TypeDefOrRef /
    // generic index / GenericInst / multidim array / fnptr signature / SZARRAY
    // element / BYREF element / PTR pointee) must be consumed so the param loop
    // starts at the FIRST param byte. (The old inline chain is kept behavior-
    // identical; fnptr-b23 adds the 0x1B arm, b23-audit adds 0x1D/0x1F/0x10/0x0F.)
    private static void ConsumeReturnTrailing(MetadataReader md, ref BlobReader sig, byte returnByte)
        => EncodeReturn(md, ref sig, returnByte);

    // refl2 S1/D1: return-type full-name decode. Mirrors ConsumeReturnTrailing's
    // consumption EXACTLY (the single trailing-consumption implementation -- DRY)
    // and additionally produces the FULL encoded return-type name for the nameful
    // bytes. The name uses the SAME encoding language as EncodeType ("V:" value
    // type, "r:" reference, "T:" type-handle, "B:" byref, "P:" pointer, "F:" fnptr),
    // so a 0x12 CLASS return of Calculator encodes "r:Calculator" and a 0x11
    // VALUETYPE return of System.Decimal encodes "V:System.Decimal"; 0x15
    // GENERICINST returns encode the resolved generic-inst name ("r:...<args>").
    // Leaf/primitive/void returns carry no name (""). Decode stores the result on
    // MethodDef.ReturnTypeName; TypeEmitter.EncodeReturnType uses it to name
    // reference/value/generic-inst returns (no more LOUD -- G2 guard removed).
    private static string EncodeReturn(MetadataReader md, ref BlobReader sig, byte returnByte)
    {
        // ECMA-335 II.23.1.16 custom modifiers (volatile / in / init modreq):
        // strip them and decode the underlying return type (the caller may
        // have passed the raw modifier byte -- Decode/DecodeStandalone strip
        // first, this keeps every entry consistent).
        returnByte = SkipCustomModifiers(ref sig, returnByte);
        if (returnByte is 0x11 or 0x12 or 0x40)
        {
            var prefix = returnByte == 0x11 ? "V:" : returnByte == 0x12 ? "r:" : "T:";
            return prefix + ResolveTypeName(md, sig.ReadTypeHandle());
        }
        // Generic-parameter return types (VAR/MVAR): a compressed integer parameter index
        // follows -- not consuming it leaks the index byte into the param loop (genlist
        // evidenced: Id<T> returns 0x1E 0x00 / get_Item returns 0x13 0x00).
        if (returnByte is 0x13 or 0x1E) { sig.ReadCompressedInteger(); return ""; }
        // GenericInst return types (user methods returning List<int> etc.): followed by
        // TypeDefOrRef + arg count + one type per argument -- consumed in the arg loop for
        // alignment.
        if (returnByte == 0x15) return "r:" + GenericInst(md, ref sig);
        // multidim ARRAY (0x14) return types (int[,] etc.): the element + rank +
        // sizes/lobounds lists must be consumed, otherwise the param loop misaligns from
        // the element byte (multidim-probe evidenced: Make returns [14 08 02 00 02 00 00];
        // param 0x0E would decode as "r:System.String" instead of element 0x08's "i").
        if (returnByte == 0x14) return "r:" + MultidimArray(md, ref sig);
        // fnptr-b23: a function-pointer RETURN type carries the embedded method signature.
        if (returnByte == 0x1B) return "F:" + FnPtrSig(md, ref sig);
        // b23-audit: SZARRAY return types (int[] Get(int x) -> 00 01 1D 08 08) carry the
        // ELEMENT byte after the marker -- not consuming it makes the param loop read the
        // element as the first param (string[] Get(int x) decodes "r:System.String" instead
        // of "i", and a following int param is silently dropped).
        if (returnByte is 0x1D) return "r:" + EncodeType(md, ref sig).TrimStart('r', ':') + "[]";
        // b23-audit: BYREF return types (ref int Get(int x) -> 00 01 10 08 08) carry the
        // element type after the marker -- same misalignment without the consume.
        if (returnByte == 0x10) return "B:" + EncodeType(md, ref sig);
        // b23-audit: PTR return types (int* Get(int x) -> 00 01 0F 08 08) carry the
        // pointee type after the marker.
        if (returnByte == 0x0F) return "P:" + EncodeType(md, ref sig);
        // 0x16 TYPEDBYREF / 0x41 SENTINEL / leaf bytes carry no trailing data -- no-op.
        return "";
    }

    private static string ConsumeGenericParamIndex(ref BlobReader sig)
    {
        sig.ReadCompressedInteger();
        return "i";
    }

    // ELEMENT_TYPE_ARRAY (0x14): element type (recursively encoded), rank,
    // then the sizes and lower-bound lists (each a compressed count + values).
    // Bounds are consumed for alignment but NOT part of the hash encoding
    // (the rank distinguishes array shapes; bounds are a construction-time
    // property invisible in the method signature).
    private static string MultidimArray(MetadataReader md, ref BlobReader sig)
    {
        var elem = EncodeType(md, ref sig).TrimStart('r', ':');
        var rank = sig.ReadCompressedInteger();
        var numSizes = sig.ReadCompressedInteger();
        for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
        var numLoBounds = sig.ReadCompressedInteger();
        for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
        return elem + "[" + rank + "]";
    }

    // GenericInst type: ELEMENT_TYPE_CLASS(0x12)/VALUETYPE(0x11) marker byte
    // + TypeDefOrRef (generic definition) + compressed arg count + one type
    // per argument (delechain evidenced add_Changed parameter: 15 12 35 02 08 08 --
    // Roslyn prefixes the marker byte inside method signatures just like in the
    // TypeSpec table blob).
    private static string GenericInst(MetadataReader md, ref BlobReader sig)
    {
        sig.ReadByte(); // marker byte
        var defHandle = sig.ReadTypeHandle();
        var n = sig.ReadCompressedInteger();
        var args = new string[n];
        for (var i = 0; i < n; i++) args[i] = EncodeType(md, ref sig);
        return ResolveTypeName(md, defHandle) + "<" + string.Join(",", args) + ">";
    }

    private static string ResolveTypeName(MetadataReader md, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
            _ => throw new NotSupportedException($"signature type handle kind {handle.Kind} not supported in M2"),
        };

    // M3 generic argument: consume a TypeDefOrRef coded index and return the
    // concrete full name. Shared by MethodRefResolver / CodeGenerator /
    // StackFold / MethodBodyReader (the "four-place rule").
    public static string ResolveM3ArgTypeName(MetadataReader md, ref BlobReader sig)
    {
        EntityHandle h = sig.ReadTypeHandle();
        return h.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)h),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)h),
            _ => throw new NotSupportedException($"M3 arg handle kind {h.Kind}"),
        };
    }
}
