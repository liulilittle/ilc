using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Il;

/// <summary>The decode result of one method body: IL bytes, local slot names, arg count, return type, method token/RVA.</summary>
public sealed class MethodBodyInfo
{
    public required byte[] IlBytes { get; init; }
    public required string[] LocNames { get; init; }
    // Coarse IL-local kinds (index-aligned with LocNames): "i" int32-slot,
    // "l" int64/uint64, "d" double/float, "r" managed reference (string/object/
    // class/array), "v" other value. The emitter declares locals from these
    // (int64_t/double/Il2CppObject*/int32_t) so IL-typed locals keep their
    // width even when the fold only sees an int32-typed constant (M5 wiring:
    // `long v = 5;` must declare int64_t, and Double::ToString receivers via
    // ldloca must dereference a 64-bit slot).
    public required string[] LocalKinds { get; init; }
    public required int ArgCount { get; init; }
    public required SignatureTypeCode ReturnType { get; init; }
    public bool ReturnsVoid => ReturnType == SignatureTypeCode.Void;
    // Method header MaxStack (ECMA-335 II.15.4.3.2): the declared maximum operand
    // stack depth for the body. Previously unread (audit mbr1); surfaced so
    // downstream validation can compare it against the IL the fold actually uses.
    public int MaxStack { get; init; }
    public int MethodToken { get; init; } // 0x06 MethodDef token (for the generated-code English header comment)
    public int Rva { get; init; }          // method body RVA (for the generated-code English header comment)
    // Exception handling regions (M3 Task 6): try/catch/finally blocks (IL ExceptionRegion table).
    public IReadOnlyList<IlExceptionRegion> ExceptionRegions { get; init; } = Array.Empty<IlExceptionRegion>();
}

/// <summary>An IL exception handling region (an ECMA-335 II.16.4 exception table entry).</summary>
public sealed class IlExceptionRegion
{
    public int TryOffset { get; init; }        // try block start IL offset
    public int TryLength { get; init; }        // try block length (bytes)
    public int HandlerOffset { get; init; }    // handler start IL offset
    public int HandlerLength { get; init; }    // handler length (bytes)
    public int CatchTypeToken { get; init; }   // catch type token (0 = catch-all/filter/finally)
    public ExceptionRegionKind Kind { get; init; } // Catch/Finally/Filter/Fault
    // Filter clauses (Kind == Filter, C# `catch when`): the offset of the filter
    // code, which precedes the catch HANDLER (HandlerOffset) and ends in
    // endfilter. -1 for every non-filter kind. Surfaced so a parse of a filter
    // region is complete (the fold rejects the filter body's endfilter loudly).
    public int FilterOffset { get; init; } = -1;
}

/// <summary>
/// Given a MetadataReader + method body RVA, produces (ilBytes, locNames, argCount, returnType).
/// Method location: matched in metadata by the TypeDef/MethodDef (Name, ParamCount, ParamTypes) --
/// overloads share a name but differ in parameter signature; the signature (param count +
/// encoded param type string) pinpoints the method body.
/// </summary>
public static class MethodBodyReader
{
    public static MethodBodyInfo Read(PEReader pe, MetadataReader md, TypeDef typeDef, MethodDef methodDef)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            if (md.GetString(td.Namespace) != typeDef.Namespace || md.GetString(td.Name) != typeDef.Name) continue;
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (md.GetString(m.Name) != methodDef.Name) continue;
                var (paramCount, _, paramTypes, _) = MethodSignatureCoder.Decode(md, m.Signature);
                if (paramCount != methodDef.ParamCount || paramTypes != methodDef.ParamTypes) continue;
                return Read(pe, md, m, MetadataTokens.GetToken(mh));
            }
        }
        throw new InvalidOperationException($"method {typeDef.FullName}.{methodDef.Name} not found in metadata");
    }

    // Bodyless probe for diagnostic passes (Inline/Licm/TempSlot ScanAssembly):
    // bodyless methods (abstract/PInvoke/runtime/delegate ctors, RVA=0) are
    // by-contract skips, not anomalies -- callers `continue` silently instead
    // of tripping Read's throw and printing a spurious "pre-scan skipped"
    // warning (zero-warning rule). Unmatched methods still throw, exactly
    // like Read (model/metadata skew stays loud).
    public static bool HasBody(MetadataReader md, TypeDef typeDef, MethodDef methodDef)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            if (md.GetString(td.Namespace) != typeDef.Namespace || md.GetString(td.Name) != typeDef.Name) continue;
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (md.GetString(m.Name) != methodDef.Name) continue;
                var (paramCount, _, paramTypes, _) = MethodSignatureCoder.Decode(md, m.Signature);
                if (paramCount != methodDef.ParamCount || paramTypes != methodDef.ParamTypes) continue;
                return m.RelativeVirtualAddress != 0;
            }
        }
        throw new InvalidOperationException($"method {typeDef.FullName}.{methodDef.Name} not found in metadata");
    }

    private static MethodBodyInfo Read(PEReader pe, MetadataReader md, MethodDefinition m, int token)
    {
        if (m.RelativeVirtualAddress == 0)
            throw new InvalidOperationException($"method {md.GetString(m.Name)} has no body (RVA=0)");
        var body = pe.GetMethodBody(m.RelativeVirtualAddress);
        var (paramCount, returnType, _, _) = MethodSignatureCoder.Decode(md, m.Signature);
        var ilBytes = body.GetILBytes() ?? Array.Empty<byte>();
        // CLI instance method signatures do not include this: IL arg0 is the this
        // reference, so the generated code's parameter slot count = explicit param
        // count + 1 (this). M2 rule: a non-static method is an instance method
        // (incl. .ctor).
        var isInstance = (m.Attributes & System.Reflection.MethodAttributes.Static) == 0;
        return new MethodBodyInfo
        {
            IlBytes = ilBytes,
            LocNames = ReadLocalNames(md, body.LocalSignature, out var localKinds),
            LocalKinds = localKinds,
            ArgCount = paramCount + (isInstance ? 1 : 0),
            ReturnType = returnType,
            MaxStack = body.MaxStack,
            MethodToken = token,
            Rva = m.RelativeVirtualAddress,
            ExceptionRegions = ReadExceptionRegions(md, body),
        };
    }

    // Exception handling region table (ECMA-335 II.16.4.1): try blocks + handler
    // blocks, one entry per clause -- typed catch (0x00), filter (0x01), finally
    // (0x02), fault (0x04). All four are valid ECMA-335 table entries; the parser
    // decodes catch/finally/filter. Fault (0x04) is deliberately kept LOUD here:
    // a fault handler runs cleanup on the EXCEPTIONAL path only, and the emission
    // layer has no fault model (CodeGenerator.CloseTryBlock has no Fault arm -- a
    // decoded fault region would silently mis-emit as a swallowing catch-all).
    // Filter (0x01, C# `catch when`): the filter body is part of the handler
    // range and ends in `endfilter`; the fold rejects Endfilter LOUDLY
    // (StackFold.cs Endfilter case) so decode keeps the parser complete for valid
    // IL and the pipeline fails at the semantic gate.
    private static IReadOnlyList<IlExceptionRegion> ReadExceptionRegions(MetadataReader md, MethodBodyBlock body)
    {
        var regions = new List<IlExceptionRegion>();
        foreach (var region in body.ExceptionRegions)
        {
            if (region.Kind == ExceptionRegionKind.Fault)
                throw new NotSupportedException(
                    $"exception fault region (try=[0x{region.TryOffset:X4},0x{region.TryOffset + region.TryLength:X4})) not supported: the generated code has no fault model (a fault handler = exceptional-path-only cleanup; decoding it would mis-emit it as a swallowing catch-all)");
            regions.Add(new IlExceptionRegion
            {
                TryOffset = region.TryOffset,
                TryLength = region.TryLength,
                HandlerOffset = region.HandlerOffset,
                HandlerLength = region.HandlerLength,
                CatchTypeToken = CatchToken(md, region),
                Kind = region.Kind,
                // A filter entry's table fields are try / filter-offset / handler;
                // the BCL surfaces the filter code offset via FilterOffset.
                FilterOffset = region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : -1,
            });
        }
        // Ordering contract (outer-first / inner-last / siblings in table order):
        // Roslyn's EH table is sorted per ECMA-335 II.16.4.1 = inner tries first
        // (exdeep f1 table order evidenced: the inner catch [0,0xB) precedes the
        // outer [0,0x1A)). The emitter selects the active try by "last match =
        // innermost" (CodeGenerator.EmitBody) and needs outer-first order -- without
        // sorting, a nested construct's inner catch is treated as the outer try's,
        // and inner handler statements fall into the try body (exdeep f1's inner
        // catch was dropped). Stable sort: sibling catches with the same
        // (TryOffset, TryLength) keep table order = dispatch order.
        return regions.OrderBy(r => r.TryOffset).ThenByDescending(r => r.TryLength).ToList();
    }

    // catch type token: a nil CatchType (ECMA catch-all) or a resolution to
    // System.Object (C# `catch { }` / `catch (object)` has a System.Object TypeRef
    // CatchType, not nil -- evidenced by the exdeep f19/CatchRet EH table) is
    // normalized to the catch-all marker 0; other types keep the original token
    // (the emitter's il2cpp_exception_matches type check depends on it).
    private static int CatchToken(MetadataReader md, ExceptionRegion region)
    {
        if (region.CatchType.IsNil) return 0;
        var token = MetadataTokens.GetToken(region.CatchType);
        var name = region.CatchType.Kind switch
        {
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)region.CatchType),
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)region.CatchType),
            _ => null, // TypeSpec: unresolvable without a generic context, keep the original token (the emitter rejects loudly)
        };
        return name == "System.Object" ? 0 : token;
    }

    // Local variable signature: LOCAL_SIG(0x07) + compressed count + per-slot type.
    // M1 only counts slots → loc0..locN-1.
    // M5 wiring: int64/uint64 (0x0A/0x0B) and float/double (0x0C/0x0D) locals are
    // now legal (int64_t/double declaration + ldloca dereference on the 64-bit slot
    // -- see MethodBodyInfo.LocalKinds); other types with trailing data
    // (VALUETYPE/CLASS/TypeHandle → TypeDefOrRef compressed index, VAR/MVAR →
    // compressed index, SZARRAY(0x1D/0x1F) → element type) must be skipped
    // correctly to keep the slot alignment.
    private static string[] ReadLocalNames(MetadataReader md, StandaloneSignatureHandle locSig, out string[] kinds)
    {
        if (locSig.IsNil)
        {
            kinds = Array.Empty<string>();
            return Array.Empty<string>();
        }
        var ss = md.GetStandaloneSignature(locSig);
        var sig = md.GetBlobReader(ss.Signature);
        if (sig.ReadByte() != 0x07)
            throw new NotSupportedException("local signature must start with LOCAL_SIG(0x07)");
        var count = sig.ReadCompressedInteger();
        var names = new string[count];
        var localKinds = new string[count];
        for (var i = 0; i < count; i++)
        {
            localKinds[i] = LocalTypeKind(md, ref sig, $"loc{i}");
            names[i] = $"loc{i}";
        }
        kinds = localKinds;
        return names;
    }

    // Consume one local type byte plus all its trailing data; return the coarse slot
    // kind ("i"/"l"/"d"/"r"/"v", or "v:<FullName>" for a user VALUETYPE struct).
    // NOTE: BlobReader must travel BY REF -- it is a struct; a by-value copy
    // would consume trailing data (VALUETYPE index/SZARRAY element) only in
    // the copy and leave the caller's position stale (every later local
    // decoded from the same byte -- the M5 wiring kinds misalignment).
    private static string LocalTypeKind(MetadataReader md, ref BlobReader sig, string name)
    {
        var b = sig.ReadByte();
        if (b is 0x0A or 0x0B) return "l";          // I8/U8: int64_t slot
        if (b is 0x0C or 0x0D) return "d";          // R4/R8: double slot
        if (b is 0x0E or 0x1C) return "r";          // STRING/OBJECT: managed reference
        if (b is 0x11 or 0x12 or 0x40)
        {
            // wf2 (winforms1): a VALUETYPE local (0x11) carries the struct's
            // TypeDefOrRef coded index -- resolve it to the full name so the
            // emitter can declare the local as the generated struct type
            // (`MSG msg;` -- the P/Invoke GetMessageW writes 48 bytes through
            // `&msg`; declaring it int32_t would overrun the slot). The
            // emitter maps "v:<name>" -> TypeCppName(name). CLASS (0x12) and
            // TypeHandle (0x40) locals are references ("r").
            var th = sig.ReadTypeHandle();
            if (b == 0x11)
            {
                // batch 156c (asynccase): a VALUETYPE TypeSpecification local
                // (TaskAwaiter`1<int32> / AsyncTaskMethodBuilder`1<int32> in
                // the async state machine) decodes via the generic
                // instantiation name -- the awaiter rides as the task
                // reference ("r": the awaiter object IS the task in the
                // minimal surface, task.cpp), so the state machine's
                // `stloc.3; ldloca.s V_3; call get_IsCompleted()` shape gets
                // an Il2CppObject* slot instead of an int32 truncation.
                if (th.Kind == HandleKind.TypeSpecification)
                {
                    var tsName = TypeSpecFullName(md, (TypeSpecificationHandle)th);
                    if (tsName != null
                        && (tsName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`1", StringComparison.Ordinal)
                            || tsName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1", StringComparison.Ordinal)))
                        return "r";
                }
                var fullName = th.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
                    // batch 156c (asynccase): a NESTED VALUETYPE local (the
                    // async state machine struct <F>d__1) -- nested types emit
                    // under their SHORT metadata name (TypeEmitter.ExpandNestedTypes),
                    // so the local's "v:" name must be the short name too,
                    // or the declared C++ type (_F_d__1) never matches the
                    // emitted struct (Cases_AsyncCase___F_d__1 -> compile fail).
                    HandleKind.TypeDefinition when !md.GetTypeDefinition((TypeDefinitionHandle)th).GetDeclaringType().IsNil
                        => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)th).Name),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
                    _ => null,
                };
                return fullName == null ? "v" : "v:" + fullName;
            }
            return b == 0x12 ? "r" : "v";
        }
        // batch 156c (asynccase): GENERICINST (0x15) LOCAL -- the async state
        // machine's TaskAwaiter`1<int32> local is encoded INLINE as
        // `15 11 <TypeRef TaskAwaiter`1> 01 08` (NOT 0x11 + TypeSpec -- the
        // net48 local signature uses the generic-inst form directly). The
        // awaiter rides as the task reference ("r": the awaiter object IS
        // the task in the minimal surface). Consume the full trailing
        // payload (marker + generic-def TypeDefOrRef + arg count + args) or
        // the next local's type byte is parsed mid-instantiation and the
        // kinds misalign (the old default fell through to "i" and desynced).
        if (b == 0x15)
        {
            sig.ReadByte(); // 0x11 VALUETYPE / 0x12 CLASS marker
            var th15 = sig.ReadTypeHandle();
            var defName = th15.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th15),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th15),
                _ => null,
            };
            // D2 (valuetuple-local design): a GENERICINST System.ValueTuple`N
            // with FLAT PRIMITIVE args decodes to the canonical "tuple:<codes>"
            // kind -- CodeGenerator's declaration arm turns it into the
            // matching System_ValueTuple_N_<types> struct (instead of the old
            // int32_t truncation that broke Math.SinCos locals). Non-primitive
            // or non-ValueTuple instantiations keep consuming + "v".
            if (defName != null && defName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
            {
                var argcVt = sig.ReadCompressedInteger();
                var codes = new List<string>();
                var flat = true;
                for (var i = 0; i < argcVt; i++)
                {
                    var ab = sig.ReadByte();
                    switch (ab)
                    {
                        case 0x0A: codes.Add("l"); break;   // I8
                        case 0x0C: codes.Add("d"); break;   // R4 (float-in-double slot)
                        case 0x0D: codes.Add("d"); break;   // R8
                        case 0x11 or 0x12: _ = sig.ReadTypeHandle(); flat = false; break;
                        case 0x1D or 0x1F: _ = sig.ReadByte(); flat = false; break;
                        case 0x13 or 0x1E: _ = sig.ReadCompressedInteger(); flat = false; break;
                        case 0x15: flat = false; break;     // nested GenericInst: not decoded here
                        default: codes.Add("i"); break;     // I4/U4/I2/U2/I1/U1/Bool/Char...
                    }
                }
                if (flat)
                    return "tuple:" + string.Join(",", codes);
            }
            var argc = sig.ReadCompressedInteger();
            for (var i = 0; i < argc; i++)
            {
                _ = LocalTypeKind(md, ref sig, name);
            }
            return "v";
        }
        if (b is 0x13 or 0x1E)
        {
            sig.ReadCompressedInteger(); // VAR/MVAR: parameter index
            return "v";
        }
        if (b is 0x1D or 0x1F)
        {
            LocalTypeKind(md, ref sig, name); // SZARRAY: element type follows; the array object is a reference
            return "r";
        }
        // multidim ARRAY (0x14, int[,] etc. -- matrixcase probe E wall, formerly
        // rejected at this site): element type + rank + sizes/lobounds compressed
        // lists. The array object is a managed reference ("r" slot); the trailing
        // lists must be consumed or every later local misaligns (multidim-probe.md
        // fix direction: "0x14 按引用槽 'r' + 完整消费").
        if (b == 0x14)
        {
            LocalTypeKind(md, ref sig, name); // element type: any shape, recursively consumed
            var rank = sig.ReadCompressedInteger();
            var numSizes = sig.ReadCompressedInteger();
            for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
            var numLoBounds = sig.ReadCompressedInteger();
            for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
            return "r";
        }
        // PTR (0x0F, unsafe `int*`) / BYREF (0x10, `ref` locals): pointer-shaped
        // slots -- the element type follows and must be consumed (int* = 0F 08,
        // ref int = 10 08), else the element byte leaks into the next local. Coarse
        // kind "i" (a pointer stored in a native-int slot).
        if (b is 0x0F or 0x10)
        {
            LocalTypeKind(md, ref sig, name);
            return "i";
        }
        // PINNED (0x45, `fixed` buffers): a modifier wrapping the next type -- the
        // slot keeps the element's kind (fixed byte* p = 45 0F 05).
        if (b == 0x45) return LocalTypeKind(md, ref sig, name);
        // FNPTR (0x1B, C# 9.0 `delegate*` locals): a full method signature
        // follows (calling convention byte + param count + return + one type per
        // param). The slot is a raw function pointer held in a native-int slot
        // ("i"; the emitter declares the local void* from the FuncPtrExpr store).
        // The whole trailing signature MUST be consumed (fnptr-b23 residual) or
        // the next local's type byte is parsed mid-signature and misaligns.
        if (b == 0x1B)
        {
            sig.ReadByte(); // calling convention (0x00 default / 0x01 C / ...)
            var fnptrCount = sig.ReadCompressedInteger();
            LocalTypeKind(md, ref sig, name); // return type (recursively consumed)
            for (var i = 0; i < fnptrCount; i++) LocalTypeKind(md, ref sig, name);
            return "i";
        }
        // Remaining single-byte types (0x02 bool / 0x03 char / 0x04 i1 / 0x08 i4 /
        // 0x09 u4 / 0x17 native int / 0x18-0x19 native uint etc.) carry no trailing data.
        return "i";
    }

    // R77: decode one generic-argument byte to its instantiation-suffix
    // name ("int", "object", "byte", "long", ...). Consumes trailing
    // payload (CLASS/VALUETYPE handle, SZARRAY element, VAR/MVAR index,
    // nested GenericInst args) so the next arg byte stays aligned.
    private static string GenericArgSuffix(MetadataReader md, ref BlobReader sig, string name)
    {
        var b = sig.ReadByte();
        switch (b)
        {
            case 0x02: return "bool";
            case 0x03: return "char";
            case 0x04: return "sbyte";
            case 0x05: return "byte";
            case 0x06: return "short";
            case 0x07: return "ushort";
            case 0x08: return "int";
            case 0x09: return "uint";
            case 0x0A: return "long";
            case 0x0B: return "ulong";
            case 0x0C: return "float";
            case 0x0D: return "double";
            case 0x0E: return "string";
            case 0x1C: return "object";
            case 0x11: // VALUETYPE
            case 0x12: // CLASS
            case 0x40: // TypeHandle
            {
                var th = sig.ReadTypeHandle();
                var fullName = th.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
                    _ => null,
                };
                // For reference types, the suffix is "object" (the slot is
                // Il2CppObject*). For value types, use the short name.
                if (fullName == null) return "object";
                var dotIdx = fullName.LastIndexOf('.');
                return dotIdx >= 0 ? fullName[(dotIdx + 1)..] : fullName;
            }
            case 0x13: // VAR
            case 0x1E: // MVAR
                sig.ReadCompressedInteger();
                return "int"; // default instantiation T -> int
            case 0x1D: // SZARRAY
            case 0x1F:
                _ = sig.ReadByte();
                return "object"; // arrays are references
            case 0x15: // GENERICINST (nested)
                sig.ReadByte(); // CLASS/VALUETYPE marker
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                for (var i = 0; i < n; i++) _ = GenericArgSuffix(md, ref sig, name);
                return "object"; // nested generic instances are references
            case 0x14: // ARRAY (multidim)
                _ = GenericArgSuffix(md, ref sig, name);
                var rank = sig.ReadCompressedInteger();
                for (var i = 0; i < rank; i++) _ = sig.ReadCompressedInteger();
                for (var i = 0; i < rank; i++) _ = sig.ReadCompressedInteger();
                return "object";
            default:
                return "int"; // fallback: treat as int32
        }
    }

    // batch 156c (asynccase): the full name of a TypeSpecification -- a
    // generic instantiation ("System.Runtime.CompilerServices.TaskAwaiter`1<System.Int32>").
    // The GENERICINST (0x15) blob: CLASS/VALUETYPE marker + generic-def
    // TypeDefOrRef + arg count + one type per arg. Returns null when the blob
    // is not the expected shape (the caller falls back to the plain "v" slot).
    private static string? TypeSpecFullName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
        if (sig.ReadByte() != 0x15) return null; // GENERICINST
        sig.ReadByte(); // 0x11 VALUETYPE / 0x12 CLASS marker
        var th = sig.ReadTypeHandle();
        var defName = th.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
            _ => null,
        };
        if (defName == null) return null;
        var argc = sig.ReadCompressedInteger();
        for (var i = 0; i < argc; i++)
        {
            var b = sig.ReadByte();
            if (b is 0x11 or 0x12) _ = sig.ReadTypeHandle();
            else if (b is 0x1D or 0x1F) _ = sig.ReadByte();
            else if (b is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();
        }
        return defName;
    }
}

