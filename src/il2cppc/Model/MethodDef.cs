namespace Il2Cpp.Transpiler.Model;

using System.Reflection.Metadata;

//------------------------------------------------------------------------------
// MethodDef -- one metadata method of a translated type.
//
// M2 additions for virtual dispatch (Task 7):
//   IsVirtual  : the method is declared virtual (a vtable slot candidate);
//                includes 'virtual' keyword methods AND interface impls
//   IsOverride : the method overrides a base-class virtual (slot replacement
//                instead of new slot)
//   VTableSlot : the virtual slot index assigned by the vtable layout pass
//                (-1 for non-virtual methods). Slots are inherited from the
//                base type; overrides replace the inherited slot; new
//                virtuals append. Slot 0 of every vtable is the base
//                (System.Object) slot count anchor -- see VTablePlanner.
//------------------------------------------------------------------------------
public sealed class MethodDef
{
    public required string Name { get; init; }
    public required TypeDef Owner { get; init; }
    public bool IsStatic { get; init; }
    public bool IsPublic { get; init; }
    // Signature info (decoded and filled by MetadataLoader via MethodSignatureCoder):
    // the param count is used for overloaded method-body signature matching; the
    // encoded param-type string ("i,i") is part of the method-name hash input
    // {owner}.{name}({paramTypes}), guaranteeing overloads get distinct C++ function names.
    public int ParamCount { get; init; }
    public string ParamTypes { get; init; } = "";
    // Return type (decoded by MethodSignatureCoder.Decode): the M4 Task 7 C API export
    // needs exact signature matching (int32(int32,int32,int32,int32)); a signature-mismatched
    // function-pointer cast is forbidden (C++ standard UB).
    public SignatureTypeCode ReturnType { get; init; }
    // Return-type full name (refl2 S1/D1, decoded by MethodSignatureCoder.Decode's
    // EncodeReturn): the FULL encoded return-type name for nameful returns
    // (0x11 VALUETYPE -> "V:System.Decimal", 0x12 CLASS -> "r:Calculator",
    // 0x15 GENERICINST -> "r:System.Func`2<i,i>", SZARRAY/BYREF/PTR/ARRAY/FNPTR
    // analogues), "" for primitive/void/leaf returns. TypeEmitter.EncodeReturnType
    // (the typeinfo method-table signature encoder) consumes this to name every
    // return -- removing the G2 reflection guard's "return not representable"
    // exclusion (the MethodDef model previously carried no full name for
    // reference/value/generic-inst returns).
    public string ReturnTypeName { get; init; } = "";

    // ReturnsVoid (computed): C# convenience for "this method produces no result".
    // Mirrors the codebase's repeated `ReturnType == SignatureTypeCode.Void` checks
    // (e.g. MethodBodyReader.BodyInfo.ReturnsVoid) on one semantic property.
    public bool ReturnsVoid => ReturnType == SignatureTypeCode.Void;

    // IsInstance (computed): an instance method takes the hidden `this` argument
    // (HAS_THIS in the signature) and is emitted as a C++ member function; a
    // static method is a free function. Convenience mirror of IsStatic.
    public bool IsInstance => !IsStatic;

    // ResultKind (computed): semantic classification of the return type, aligned
    // with the C++ slot model the emitter uses (MethodSignatureCoder's byte
    // encodings + TypeEmitter.EncodeReturnType's arms). Consumers decide
    // reference-ness (GC tracking), slot width and C++ return type from ONE
    // stable taxonomy instead of repeating raw SignatureTypeCode switches.
    public MethodResultKind ResultKind => ReturnType switch
    {
        SignatureTypeCode.Void => MethodResultKind.Void,
        SignatureTypeCode.Boolean or SignatureTypeCode.Char or SignatureTypeCode.SByte or
        SignatureTypeCode.Byte or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or
        SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or
        SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr => MethodResultKind.Int32,
        SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 => MethodResultKind.Int64,
        SignatureTypeCode.Single or SignatureTypeCode.Double => MethodResultKind.Floating,
        // Raw byte casts: MetadataLoader stores the RAW signature byte, so a
        // CLASS (0x12) / VALUETYPE (0x11) return is the unnamed enum value 18/17
        // (SignatureTypeCode has no Class/ValueType members -- the codebase's
        // raw-byte convention, see MethodSignatureCoder).
        (SignatureTypeCode)0x12 or SignatureTypeCode.String or SignatureTypeCode.Object or
        SignatureTypeCode.SZArray or SignatureTypeCode.Array or
        SignatureTypeCode.GenericTypeInstance or SignatureTypeCode.TypeHandle => MethodResultKind.Reference,
        (SignatureTypeCode)0x11 => MethodResultKind.ValueType,
        SignatureTypeCode.GenericTypeParameter or SignatureTypeCode.GenericMethodParameter => MethodResultKind.Generic,
        SignatureTypeCode.FunctionPointer => MethodResultKind.FunctionPointer,
        _ => MethodResultKind.Other,
    };

    public bool IsVirtual { get; init; }
    public bool IsOverride { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsFinal { get; init; }
    public int VTableSlot { get; set; } = -1;

    // P/Invoke (M4 Task 1): a method imported from a native library via the
    // ImplMap table ([DllImport] in source). Such methods have NO IL body
    // (RVA == 0); the transpiler emits a native import declaration instead.
    public bool IsPInvoke { get; init; }
    public string? PinvokeModule { get; init; }   // native library name ("mylib")
    public string? PinvokeEntryName { get; init; } // imported symbol name ("add_ints")
    public int PinvokeFlags { get; init; }          // MethodImportAttributes (CharSet/CallingConvention)
    // M4 Task 4: [DllImport(SetLastError=true)] -- the OS error code is
    // captured after the call (MethodImportAttributes.SetLastError = 0x40).
    public bool PinvokeSetLastError { get; init; }

    // 泛型信息 (M3 generics): the method's OWN generic arity -- the number of
    // METHOD-level type parameters (MVAR, `T Id<T>(T x)` has arity 1). Distinct
    // from the owner type's arity (TypeDef.GenericArity, class-level VARs).
    // MethodSignatureCoder.Decode currently skips the generic parameter count
    // of generic signatures (md1 audit: the model gap is that arity is not
    // surfaced); the default instantiation maps every T to int32_t (iron rule 2g).
    public int GenericArity { get; init; }
    public bool IsGenericMethod => GenericArity > 0;
}

//------------------------------------------------------------------------------
// MethodResultKind -- semantic return-type classification (MethodDef.ResultKind).
//
// Aligned with the C++ slot model (MethodSignatureCoder byte encodings +
// TypeEmitter.EncodeReturnType arms):
//   Int32      : 32-bit value slot (bool/char/i1/u1/i2/u2/i4/u4/native int)
//   Int64      : 64-bit value slot (i8/u8)
//   Floating   : r4/r8 (stored in double slots)
//   Reference  : managed GC reference (string/object/class/array/generic-inst)
//   ValueType  : user struct (inline value, no object header)
//   Generic    : VAR/MVAR type parameter (default instantiation -> int32_t)
//   FunctionPointer : unmanaged function pointer (C-style pointer)
//   Other      : any raw byte the current pipeline cannot classify
//------------------------------------------------------------------------------
public enum MethodResultKind
{
    Void,
    Int32,
    Int64,
    Floating,
    Reference,
    ValueType,
    Generic,
    FunctionPointer,
    Other,
}

