using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Marshal dispatch tests: prove the System.Runtime.InteropServices
// .Marshal domain (TryMapMarshal in MethodMap.Marshal.cs) maps the M4/M5
// marshal static surface to the runtime icalls -- AND pins the
// static/instance classification (P0 sprint-worklist #28/#29: the fallback
// name heuristic classified every Marshal STATIC as INSTANCE, so StackFold
// popped `this` off an empty stack):
//
//   Marshal.GetLastWin32Error()  -> il2cpp::il2cpp_pinvoke_get_last_error
//   Marshal.PtrToStringAnsi(IntPtr) -> il2cpp::il2cpp_pinvoke_string_from_utf8
//   Marshal.AllocHGlobal(int/IntPtr) -> icall_marshal_alloc_hglobal
//   Marshal.FreeHGlobal(IntPtr)  -> icall_marshal_free_hglobal
//   Marshal.Copy(Array,int,IntPtr,int) -> icall_marshal_copy_to_unmanaged
//   Marshal.Copy(IntPtr,Array,int,int) -> icall_marshal_copy_from_unmanaged
//   SetLastWin32Error/SizeOf/OffsetOf/PtrToStringUni -> LOUD NotSupported
//     (precise wiring guidance; marshalext2.h is not in generated.h)
//   GetFunctionPointerForDelegate -> UNMAPPED (null): the delegatecb work
//     item pins the MethodSpec-path error at MethodRefResolver.cs:224
//
// Signature blobs are the raw bytes RawSignature consumes: header 0x00
// (DEFAULT), param count, return byte, then one byte per parameter. A
// valuetype (0x11) return/param carries the TypeDefOrRef coded index of the
// owner TypeRef (row 1 -> 0x05) after the 0x11 byte; an SZARRAY (0x1D) param
// carries its element byte after (consumed by RawSignature -- ps reads 0x1D).
public class MethodMapMarshalTests
{
    //--------------------------------------------------------------------------
    // GetLastWin32Error() -> il2cpp_pinvoke_get_last_error (STATIC -- the
    // classification fix #28)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetLastWin32Error_IsStaticMappedToCaptureSlot()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetLastWin32Error", Sig(Ret(0x08))), "GetLastWin32Error");

        Assert.Equal("il2cpp::il2cpp_pinvoke_get_last_error", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(0, m.ParamCount);
    }

    //--------------------------------------------------------------------------
    // PtrToStringAnsi(IntPtr) -> il2cpp_pinvoke_string_from_utf8 (STATIC --
    // the classification fix #29)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_PtrToStringAnsi_IsStaticMappedToUtf8Decode()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "PtrToStringAnsi", Sig(Ret(0x0E), 0x18)), "PtrToStringAnsi");

        Assert.Equal("il2cpp::il2cpp_pinvoke_string_from_utf8", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        // M4.5: "p" -- the IntPtr carrier reaches the const char* param via an
        // explicit cast (CodeGenerator EmitIcallCallArgs "p"); the old "" cast
        // passed an int32_t straight into const char* (C2440 on MSVC).
        Assert.Equal("p", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // PtrToStringAnsi(IntPtr) as VALUETYPE (0x11 + TypeHandle) -- the Roslyn
    // user-signature form; the map accepts it alongside the 0x18 native-int
    // form above (root cause #29).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_PtrToStringAnsi_IntPtrValuetypeForm_IsStaticMappedToUtf8Decode()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "PtrToStringAnsi", Sig(Ret(0x0E), 0x11, 0x05)), "PtrToStringAnsi");

        Assert.Equal("il2cpp::il2cpp_pinvoke_string_from_utf8", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
    }

    //--------------------------------------------------------------------------
    // AllocHGlobal(int) / AllocHGlobal(IntPtr) -> icall_marshal_alloc_hglobal
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_AllocHGlobal_Int_IsStaticMappedToAllocIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "AllocHGlobal", Sig(Ret(0x11, 0x05), 0x08)), "AllocHGlobal");

        Assert.Equal("il2cpp::runtime::icall_marshal_alloc_hglobal", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("i", m.ParamTypeNames![0]);
        // R2b (strictalias): the helper returns intptr_t; the 0x11 VALUETYPE
        // signature here is NOT rescued by the raw-0x18 signature fallback
        // (StackFold.UserCallResultKind), so the mapping must carry "ptr".
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_Marshal_AllocHGlobal_IntPtr_IsStaticMappedToAllocIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "AllocHGlobal", Sig(Ret(0x11, 0x05), 0x18)), "AllocHGlobal");

        Assert.Equal("il2cpp::runtime::icall_marshal_alloc_hglobal", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
    }

    //--------------------------------------------------------------------------
    // FreeHGlobal(IntPtr) -> icall_marshal_free_hglobal (void, static)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_FreeHGlobal_IsStaticMappedToFreeIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "FreeHGlobal", Sig(Ret(0x01), 0x18)), "FreeHGlobal");

        Assert.Equal("il2cpp::runtime::icall_marshal_free_hglobal", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        // M4.5: "g" -- the IntPtr carrier reaches the void* param via an
        // explicit (uint8_t*) cast (CodeGenerator EmitIcallCallArgs "g"); the
        // old "" cast passed an intptr_t straight into void* (C2440).
        Assert.Equal("g", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // Copy(Array, int, IntPtr, int) -> icall_marshal_copy_to_unmanaged_elem
    // Copy(IntPtr, Array, int, int) -> icall_marshal_copy_from_unmanaged_elem
    // (r127: elem-width variants dispatch on arr->elemSize, handling all
    // primitive element kinds -- the old int32-stride icalls only handle
    // elemSize==4).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_Copy_ArrayToNative_IsStaticMappedToCopyToUnmanaged()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "Copy",
            Sig(Ret(0x01), 0x1D, 0x08, 0x08, 0x18, 0x08)), "Copy");

        Assert.Equal("il2cpp::runtime::icall_marshal_copy_to_unmanaged_elem", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(4, m.ParamCount);
        // pos2 = "g": the IntPtr dest reaches the byte-pointer param via an
        // explicit (uint8_t*) cast (M4.5 C2440 fix -- same carrier rule as
        // FreeHGlobal's void* arg; the old "" expectation predates it).
        Assert.Equal(new[] { "a", "i", "g", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_Copy_NativeToArray_IsStaticMappedToCopyFromUnmanaged()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "Copy",
            Sig(Ret(0x01), 0x18, 0x1D, 0x08, 0x08, 0x08)), "Copy");

        Assert.Equal("il2cpp::runtime::icall_marshal_copy_from_unmanaged_elem", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(4, m.ParamCount);
        // pos0 = "g": the IntPtr src reaches the byte-pointer param via an
        // explicit (uint8_t*) cast (M4.5 C2440 fix -- the old "" expectation
        // predates it).
        Assert.Equal(new[] { "g", "a", "i", "i" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Known-but-unwireable members -> LOUD NotSupportedException with precise
    // guidance (the BclResolve wrapper prefixes "System...::name: ").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_SetLastWin32Error_MapsToExt2Icall()
    {
        // Wired: il2cpp_marshal_ext2.h joined the generated include chain,
        // so the LOUD include-gap rejection became a direct mapping.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "SetLastWin32Error", Sig(Ret(0x01), 0x08)), "SetLastWin32Error");

        Assert.Equal("il2cpp::runtime::il2cpp_marshal_set_last_win32_error", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_SizeOf_ThrowsLoudTypeCodeGap()
    {
        // D-MAR-1 (batch 111): SizeOf<T>() / SizeOf(Type) / SizeOf(object) are
        // wired for the REGISTERED STRUCT FAMILY; a bare parameterless
        // SizeOf with no generic/Type/object argument is still a LOUD
        // rejection (no type to resolve).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "SizeOf", Sig(Ret(0x08))), "SizeOf"));

        Assert.StartsWith("System.Runtime.InteropServices.Marshal::SizeOf: ", ex.Message);
        Assert.Contains("registered-struct family", ex.Message);
    }

    //--------------------------------------------------------------------------
    // PtrToStringUni(IntPtr) -> il2cpp_pinvoke_string_from_utf16 (WF-M1,
    // 2026-08-07): the UTF-16 decode helper now exists (il2cpp_pinvoke_ext.h).
    // The IntPtr carrier reaches the const char16_t* param via the "p16" cast
    // code (the "p" cast is const char* -- "p16" is its char16_t twin, a
    // CG-FIX EmitIcallCallArgs addition).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_PtrToStringUni_MapsToUtf16Decode()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "PtrToStringUni", Sig(Ret(0x0E), 0x18)), "PtrToStringUni");

        Assert.Equal("il2cpp::il2cpp_pinvoke_string_from_utf16", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("p16", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // StringToHGlobalUni(string) -> il2cpp_pinvoke_string_to_utf16_hglobal
    // (WF-M1, 2026-08-07): the hglobal icall form returns intptr_t (IntPtr.Zero
    // on null) -- "ptr" result kind, so the result temp is intptr_t with no
    // emitter return cast. The string reaches the Il2CppString* param via "s".
    // StringToHGlobalAnsi wired via il2cpp_marshal_ext2.h (in the chain).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_StringToHGlobalUni_MapsToUtf16HglobalIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "StringToHGlobalUni",
            Sig(Ret(0x11, 0x05), 0x0E)), "StringToHGlobalUni");

        Assert.Equal("il2cpp::il2cpp_pinvoke_string_to_utf16_hglobal", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
        Assert.Equal(new[] { "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_StringToHGlobalAnsi_MapsToAnsiHglobalIcall()
    {
        // Wired: il2cpp_marshal_ext2.h joined the generated include chain --
        // mirrors the Uni form (string via "s", "ptr" result, freed by
        // FreeHGlobal).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "StringToHGlobalAnsi",
            Sig(Ret(0x11, 0x05), 0x0E)), "StringToHGlobalAnsi");

        Assert.Equal("il2cpp::runtime::il2cpp_marshal_string_to_hglobal_ansi", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
        Assert.Equal(new[] { "s" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // GetFunctionPointerForDelegate (delegatecb work item): the generic
    // MethodSpec form (Marshal.GetFunctionPointerForDelegate<T>(T delegate))
    // now maps to icall_delegate_get_fn_ptr (the runtime single-cast
    // static-method delegate marshals to its raw methodPtr). The void* result
    // is the IntPtr carrier ("ptr" ResultKind -> intptr_t temp). The
    // NON-generic MemberRef form (an object param -- not a real .NET
    // overload) still falls through to the generated-name path.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetFunctionPointerForDelegate_MethodSpec_MapsToGetFnPtrIcall()
    {
        var m = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.InteropServices", "Marshal", "GetFunctionPointerForDelegate",
            MemberRefSig(Ret(0x08), VarParam()), GenericSig(0x12)));

        Assert.Equal("il2cpp::runtime::icall_delegate_get_fn_ptr", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
        Assert.Equal("", m.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_Marshal_GetFunctionPointerForDelegate_FallsThroughToGeneratedName()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetFunctionPointerForDelegate", Sig(Ret(0x11, 0x05), 0x1C)), "GetFunctionPointerForDelegate");

        Assert.StartsWith("System_Runtime_InteropServices_Marshal_GetFunctionPointerForDelegate_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // P-D2 #2: GetFunctionPointerForDelegate<T>(T) -- the INSTANCE-binding
    // signature. The delegate type is resolved from the tracked argument type
    // (arg0Type) and its Invoke MethodDef signature decoded into the full-width
    // leaf-ABI C++ types (SAME mapping as the #1 multicast helper cast table).
    // The resolver registers a per-signature GfpS module (thunks + strong-root
    // slots + dispatcher -- see GfpModuleDefs) and returns the dispatcher as
    // the call target: the emitter emits `il2cpp_delegate_gfp_<hash>(d)` and
    // its void* result rides the IntPtr "ptr" carrier. The dispatcher keeps the
    // net48 byte-for-byte contract for the static single-cast case (returns the
    // raw methodPtr -- the icall_delegate_get_fn_ptr path was byte-identical).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetFunctionPointerForDelegate_MethodSpec_MapsToGfpDispatch()
    {
        var m = ResolveDelegateGfpMethodSpec(SynthesizeDelegateGfp());

        var hash = SigHash.Hash("int32_t(int32_t)");
        Assert.Equal("il2cpp_delegate_gfp_" + hash, m.CppName);
        Assert.True(m.ReturnsValue);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
        Assert.Equal("ptr", m.ResultKind);
    }

    //--------------------------------------------------------------------------
    // P-D2 #2 (guard): a delegate signature that CANNOT be resolved from the
    // input assembly metadata (a BCL generic delegate like Func`N -- absent
    // from the metadata) falls through to the existing
    // icall_delegate_get_fn_ptr mapping (the conservative baseline: static
    // single-cast works, instance-bound stays LOUD at runtime).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetFunctionPointerForDelegate_MethodSpec_UnresolvableSignature_FallsThrough()
    {
        using var reader = new PEReader(SynthesizeDelegateGfp());
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        var m = MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, "System.Func`2_System_Int32_System_Int32");

        Assert.Equal("il2cpp::runtime::icall_delegate_get_fn_ptr", m.CppName);
        Assert.Equal("ptr", m.ResultKind);
    }

    //--------------------------------------------------------------------------
    // P2-HARDEN #2: a NON-delegate class that happens to declare an Invoke
    // method must NOT be classified as a GFPD delegate (the old
    // ResolveDelegateGfpSignature matched on "full name + has Invoke" only).
    // A non-MulticastDelegate-derived class used as the GFPD generic argument
    // would otherwise resolve to a GfpS dispatcher and the runtime would
    // flatten a non-delegate object's garbage as invocation-list leaves.
    // The base-chain check (System.MulticastDelegate / System.Delegate) makes
    // it fall through to the conservative icall_delegate_get_fn_ptr baseline
    // instead (defensive hardening; zero corpus hits).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetFunctionPointerForDelegate_NonDelegateWithInvoke_FallsThroughToGetFnPtr()
    {
        using var reader = new PEReader(SynthesizeDelegateGfpNonDelegate());
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        var m = MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, "Cases.Fake");

        Assert.Equal("il2cpp::runtime::icall_delegate_get_fn_ptr", m.CppName);
        Assert.Equal("ptr", m.ResultKind);
        Assert.DoesNotContain("il2cpp_delegate_gfp_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // P-D2 #3: GetDelegateForFunctionPointer<T>(IntPtr) -- the generic
    // MethodSpec form wraps a native function pointer into a single-cast
    // delegate of type T (net48: the returned delegate.Invoke() is a direct
    // native call). The delegate-type ID -- FNV-1a of T's full name, the SAME
    // SigHash.UInt64Literal stamp DelegateNewExpr applies (StackFold.cs:3541)
    // -- rides as a trailing literal (AppendLiterals) so Combine/Equals type
    // checks on GDFFP-created delegates work. The IntPtr arg rides the
    // intptr_t carrier ("n" cast); the result is the delegate object ("ref"
    // ResultKind -> Il2CppObject*). The non-generic Type form is NOT wired
    // (falls through to the generated-name path -- no typeId resolvable).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetDelegateForFunctionPointer_MethodSpec_MapsToFromFnPtrIcall()
    {
        // MemberRef signature: T GetDelegateForFunctionPointer<T>(native int).
        // The MethodSpec generic arg is a CLASS TypeHandle (coded index 0x05 =
        // TypeRef row 1, the only TypeRef the synthetic PE adds -- the Marshal
        // owner itself; the resolved name feeds the typeId literal).
        var m = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.InteropServices", "Marshal", "GetDelegateForFunctionPointer",
            MemberRefSig(Ret(0x13, 0x00), new byte[] { 0x18 }), new byte[] { 0x0A, 0x01, 0x12, 0x05 }));

        Assert.Equal("il2cpp::runtime::icall_delegate_from_fn_ptr", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("ref", m.ResultKind);
        Assert.Equal(new[] { "n" }, m.ParamTypeNames);
        Assert.Equal(SigHash.UInt64Literal("System.Runtime.InteropServices.Marshal"), m.AppendLiterals![0]);
    }

    //--------------------------------------------------------------------------
    // GN-9 (a): GetDelegateForFunctionPointer<Func<int[]>>(IntPtr) -- a
    // GENERIC-INST generic argument whose nested SZARRAY arg (0x1D) must be
    // ELEMENT-aware. The pre-fix GenericInstArgName encoded the SZARRAY arg as
    // the constant token "array" WITHOUT consuming the element-type byte (the
    // stream then misaligns on a multi-arg instantiation like
    // Func<int[],string[]>), so the GDFFP delegate typeId
    // (SigHash.UInt64Literal of the full name) diverged from a delegate
    // newobj's GenericInstantiationName stamp "System.Func`1_[int]" -- the
    // Combine/Equals type check then rejected a GDFFP-created delegate against
    // a normally-created one. The fix routes 0x1D through ReadM3ArrayArg (the
    // same element-aware helper GenericInstantiationName already uses).
    // MethodSpec blob: GENERICINST(0x0A) + argc=1 + GenericInst arg(0x15) +
    // CLASS(0x12) Func`1 TypeRef + count=1 + SZARRAY(0x1D) + int(0x08).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetDelegateForFunctionPointer_Func1Szarray_MethodSpec_MapsToFromFnPtrIcall()
    {
        var m = ResolveMethodSpec(SynthesizeGdfpFunc1Array());

        Assert.Equal("il2cpp::runtime::icall_delegate_from_fn_ptr", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("ref", m.ResultKind);
        Assert.Equal(new[] { "n" }, m.ParamTypeNames);
        Assert.Equal(SigHash.UInt64Literal("System.Func`1_[int]"), m.AppendLiterals![0]);
    }

    //--------------------------------------------------------------------------
    // GN-10 (i): GenericArgFullNames consumption contract. A bare multidim
    // ARRAY (0x14) generic argument has NO arm in the name table -- the element
    // + rank + size/lobound bytes are NOT consumed, so the stream misaligns
    // and every following argument decodes from the wrong byte. With a
    // VALUETYPE struct as the SECOND argument the name table must still reach
    // it and return "Cases.Pt" (the contract: GAFN's consumption rules equal
    // ReadParamByte's).
    // MethodSpec blob: GENERICINST(0x0A) + argc=2 + arg0 multidim int[,]
    // (0x14 08 02 00 00) + arg1 VALUETYPE Cases.Pt (0x11 08 -- TypeDef row 2).
    // Pre-fix: the 0x14 payload leaks, arg1 reads a stray byte, names is null.
    //--------------------------------------------------------------------------
    [Fact]
    public void GenericArgFullNames_MultidimThenStruct_SecondArgIsStruct()
    {
        using var reader = new PEReader(SynthesizeGdfpMultidimStruct());
        var md = reader.GetMetadataReader();
        var ms = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(1));
        var names = InvokeGenericArgFullNames(md, ms.Signature, new byte[] { 0x14, 0x11 });

        Assert.NotNull(names);
        Assert.Equal("Cases.Pt", names![1]);
    }

    //--------------------------------------------------------------------------
    // GN-10 (i) correction 1(d): the genArgs PRODUCER (ReadParamByte) must consume a
    // multidim (0x14) payload EMBEDDED inside a nested GenericInst (0x15) too.
    // GAFN already consumes it (via GenericInstArgName's 0x14 arm) but
    // ReadParamByte's 0x15 recursion does not -- an index>0 latent misalignment.
    // MethodSpec blob: GENERICINST(0x0A) + argc=2 + arg0 GenericInst(0x15)
    // CLASS Cases.Dummy (0x12 04 -- TypeDef row 1) with one multidim int[,]
    // arg (0x14 08 02 00 00) + arg1 VALUETYPE Cases.Pt (0x11 08).
    // Pre-fix: genArgs == [0x15, 0x08] (the leaked int element byte).
    // Post-fix: genArgs == [0x15, 0x11] (payload consumed, arg1 aligned).
    //--------------------------------------------------------------------------
    [Fact]
    public void ReadParamByte_NestedGenericEmbeddedMultidim_NextArgStaysAligned()
    {
        using var reader = new PEReader(SynthesizeGdfpNestedMultidimStruct());
        var md = reader.GetMetadataReader();
        var ms = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(1));
        var genArgs = InvokeReadMethodSpecGenArgs(md, ms.Signature);

        Assert.Equal(new byte[] { 0x15, 0x11 }, genArgs);
    }

    //--------------------------------------------------------------------------
    // P-D2 #3 (guard): the NON-generic GetDelegateForFunctionPointer(IntPtr,
    // Type) form -- the runtime-Type overload left to a later milestone -- must
    // NOT map (a type object argument is not resolvable to a typeId at
    // transpile time). The fallthrough to the generated-name path keeps the
    // existing LOUD contract.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetDelegateForFunctionPointer_NonGenericTypeForm_FallsThrough()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetDelegateForFunctionPointer",
            Sig(Ret(0x11, 0x05), 0x18, 0x12, 0x05)), "GetDelegateForFunctionPointer");

        Assert.StartsWith("System_Runtime_InteropServices_Marshal_GetDelegateForFunctionPointer_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // UnsafeAddrOfPinnedArrayElement(Array, int) ->
    // icall_marshal_unsafe_addr_of_pinned_array_element ("ptr" IntPtr carrier,
    // "a" array / "i" index casts). Three ps[0] shapes are accepted: 0x12
    // (CLASS System.Array -- the net48 public overload), 0x1D (SZARRAY -- the
    // generic UnsafeAddrOfPinnedArrayElement<T>(T[], int) overload) and 0x13
    // (VAR -- a bare generic element slot).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_UnsafeAddrOfPinnedArrayElement_ArrayClassForm_MapsToAddrIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "UnsafeAddrOfPinnedArrayElement",
            Sig(Ret(0x11, 0x05), 0x12, 0x05, 0x08)), "UnsafeAddrOfPinnedArrayElement");

        Assert.Equal("il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
        Assert.Equal(new[] { "a", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_UnsafeAddrOfPinnedArrayElement_SzarrayForm_MapsToAddrIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "UnsafeAddrOfPinnedArrayElement",
            Sig(Ret(0x11, 0x05), 0x1D, 0x08, 0x08)), "UnsafeAddrOfPinnedArrayElement");

        Assert.Equal("il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_Marshal_UnsafeAddrOfPinnedArrayElement_VarForm_MapsToAddrIcall()
    {
        // Hand-built GENERIC signature: [0x10 header, gen-param count 1, param
        // count 2, ret 0x11+0x05, param1 VAR 0x13 + compressed index 0x00,
        // param2 int32 0x08]. A bare VAR param only appears inside a GENERIC
        // (0x10) signature -- MethodSignatureCoder requires the header so it
        // skips the generic-param count and ConsumeGenericParamIndex eats the
        // index byte (a DEFAULT 0x00 header would leak the index into the
        // param loop -> "byte 0x00 not supported").
        var varSig = new byte[] { 0x10, 0x01, 0x02, 0x11, 0x05, 0x13, 0x00, 0x08 };
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "UnsafeAddrOfPinnedArrayElement", varSig),
            "UnsafeAddrOfPinnedArrayElement");

        Assert.Equal("il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_Marshal_UnsafeAddrOfPinnedArrayElement_WrongSecondParam_FallsThrough()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "UnsafeAddrOfPinnedArrayElement",
            Sig(Ret(0x11, 0x05), 0x12, 0x05, 0x0E)), "UnsafeAddrOfPinnedArrayElement");

        Assert.StartsWith("System_Runtime_InteropServices_Marshal_UnsafeAddrOfPinnedArrayElement_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // GetHRForException(Exception) -> icall_marshal_get_hr_for_exception
    // (int32 result; the exception object rides the "" passthrough slot).
    // ps[0] is 0x12 (CLASS System.Exception) or 0x1C (object -- the internal
    // GetHRForException_WinRT variant shares the shape).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetHRForException_ClassForm_MapsToHrIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetHRForException",
            Sig(Ret(0x08), 0x12, 0x05)), "GetHRForException");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_hr_for_exception", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal("", m.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_Marshal_GetHRForException_ObjectForm_MapsToHrIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetHRForException",
            Sig(Ret(0x08), 0x1C)), "GetHRForException");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_hr_for_exception", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
    }

    [Fact]
    public void Resolve_Marshal_GetHRForException_TwoParams_FallsThrough()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetHRForException",
            Sig(Ret(0x08), 0x1C, 0x08)), "GetHRForException");

        Assert.StartsWith("System_Runtime_InteropServices_Marshal_GetHRForException_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // GetExceptionCode() / GetExceptionPointers() -- zero-arg SEH queries.
    // GetExceptionCode -> icall_marshal_get_exception_code (int32);
    // GetExceptionPointers -> icall_marshal_get_exception_pointers ("ptr"
    // IntPtr carrier -- IntPtr.Zero semantics).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetExceptionCode_IsStaticMappedToSehIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetExceptionCode",
            Sig(Ret(0x08))), "GetExceptionCode");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_exception_code", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(0, m.ParamCount);
    }

    [Fact]
    public void Resolve_Marshal_GetExceptionPointers_IsStaticMappedToSehIcallPtr()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetExceptionPointers",
            Sig(Ret(0x11, 0x05))), "GetExceptionPointers");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_exception_pointers", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
    }

    //--------------------------------------------------------------------------
    // VARIANT family + IsComObject (marshal-b23, marshalext5.cpp) -- the 4
    // members REMOVED from kComMarshalMembers now map to real runtime icalls.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetObjectForNativeVariant_MapsToVariantIcall()
    {
        // object GetObjectForNativeVariant(IntPtr): ret object (0x12 CLASS +
        // TypeHandle byte 0x05), param IntPtr (0x18 native-int).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetObjectForNativeVariant",
            Sig(Ret(0x12, 0x05), 0x18)), "GetObjectForNativeVariant");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_object_for_native_variant", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "g" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_GetNativeVariantForObject_MapsToVariantIcall()
    {
        // void GetNativeVariantForObject(object, IntPtr).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetNativeVariantForObject",
            Sig(Ret(0x01), 0x1C, 0x18)), "GetNativeVariantForObject");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_native_variant_for_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "g" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_GetObjectsForNativeVariants_MapsToVariantIcall()
    {
        // object[] GetObjectsForNativeVariants(IntPtr, int).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetObjectsForNativeVariants",
            Sig(Ret(0x12, 0x05), 0x18, 0x08)), "GetObjectsForNativeVariants");

        Assert.Equal("il2cpp::runtime::icall_marshal_get_objects_for_native_variants", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "g", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_IsComObject_MapsToComObjectIcall()
    {
        // bool IsComObject(object).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "IsComObject",
            Sig(Ret(0x02), 0x1C)), "IsComObject");

        Assert.Equal("il2cpp::runtime::icall_marshal_is_com_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_IsComObject_NullThrowsArgNull()
    {
        // The runtime icall throws ArgumentNullException on null; the mapping
        // itself is unchanged. This test pins the ICALL surface (not the
        // throw, which is runtime-side).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "IsComObject",
            Sig(Ret(0x02), 0x12, 0x05)), "IsComObject");

        Assert.Equal("il2cpp::runtime::icall_marshal_is_com_object", m.CppName);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Marshal COM RCW surface (com3, com.cpp) -- the 4 members REMOVED from
    // kComMarshalMembers now map to real runtime icalls. Windows = full
    // implementation; POSIX = the documented stub (never a crash).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetActiveObject_MapsToComIcall()
    {
        // object GetActiveObject(string progID): ret object (0x12 CLASS +
        // TypeHandle byte 0x05), param string (0x0E).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetActiveObject",
            Sig(Ret(0x12, 0x05), 0x0E)), "GetActiveObject");

        Assert.Equal("il2cpp::runtime::icall_com_get_active_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_GetIUnknownForObject_MapsToComIcall()
    {
        // IntPtr GetIUnknownForObject(object): ret native-int (0x18), param
        // object (0x1C). The intptr_t result rides the "ptr" carrier.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetIUnknownForObject",
            Sig(Ret(0x18), 0x1C)), "GetIUnknownForObject");

        Assert.Equal("il2cpp::runtime::icall_com_get_iunknown_for_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
        Assert.Equal("ptr", m.ResultKind);
    }

    //--------------------------------------------------------------------------
    // C2 (A2): GetIDispatchForObject moved OUT of the COM LOUD set -- the C2
    // split wires it to the real runtime icall (same "ptr" IntPtr-carrier
    // shape as GetIUnknownForObject).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetIDispatchForObject_MapsToComIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetIDispatchForObject",
            Sig(Ret(0x18), 0x1C)), "GetIDispatchForObject");

        Assert.Equal("il2cpp::runtime::icall_com_get_idispatch_for_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_Marshal_GetIUnknownForObject_ClassParam_MapsToComIcall()
    {
        // CLASS-typed param variant (0x12 + TypeHandle) -- same mapping.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetIUnknownForObject",
            Sig(Ret(0x18), 0x12, 0x05)), "GetIUnknownForObject");

        Assert.Equal("il2cpp::runtime::icall_com_get_iunknown_for_object", m.CppName);
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_Marshal_GetObjectForIUnknown_MapsToComIcall()
    {
        // object GetObjectForIUnknown(IntPtr): ret object (0x12 CLASS +
        // TypeHandle byte 0x05), param IntPtr (0x18 native-int). The IntPtr
        // carrier reaches the intptr_t param via the "n" cast.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetObjectForIUnknown",
            Sig(Ret(0x12, 0x05), 0x18)), "GetObjectForIUnknown");

        Assert.Equal("il2cpp::runtime::icall_com_get_object_for_iunknown", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "n" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_ReleaseComObject_MapsToComIcall()
    {
        // int ReleaseComObject(object): ret int32 (0x08), param object (0x1C).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "ReleaseComObject",
            Sig(Ret(0x08), 0x1C)), "ReleaseComObject");

        Assert.Equal("il2cpp::runtime::icall_com_release_com_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // COM interop surface -> LOUD NotSupportedException (the kComMarshalMembers
    // set at the top of TryMapMarshal). Every name in the set rejects -- a
    // sample of the public wrappers and the internal *Native variants.
    //--------------------------------------------------------------------------
    [Theory]
    // batch 186: GetComInterfaceForObject moved from the LOUD set to the wired
    // path (icall_com_get_com_interface_for_object) -- removed from this list.
    [InlineData("FinalReleaseComObject")]
    [InlineData("GetIUnknownForObjectNative")]
    [InlineData("CreateWrapperOfType")]
    public void Resolve_Marshal_ComMember_ThrowsLoudComDomain(string memberName)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", memberName, Sig(Ret(0x11, 0x05), 0x1C)), memberName));

        Assert.StartsWith("System.Runtime.InteropServices.Marshal::", ex.Message);
        Assert.Contains("COM interop is not supported", ex.Message);
        Assert.Contains(memberName, ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.IntPtr.cs / MethodMapTests.VersionUri.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static byte[] Ret(byte ret, params byte[] trailing) =>
        new[] { ret }.Concat(trailing).ToArray();

    // Builds a REAL MemberRef signature blob: [0x00 DEFAULT, compressed param
    // count, return bytes, param bytes]. rawParams is the full parameter
    // SECTION (each logical param carries its trailing bytes: 0x11/0x12/0x40 +
    // TypeHandle coded index, 0x1D/0x1F + element byte); the count is derived
    // by walking -- the old helper counted raw bytes as params, which produced
    // invalid blobs (MethodSignatureCoder Decode crashed OutOfBounds).
    private static byte[] Sig(byte[] retBytes, params byte[] rawParams)
    {
        var count = 0;
        var i = 0;
        while (i < rawParams.Length)
        {
            count++;
            var b = rawParams[i];
            if (b is 0x11 or 0x12 or 0x40) i += 2; // + TypeHandle byte (TypeRef row 1 -> 0x05)
            else if (b is 0x1D or 0x1F) i += 2;    // + element byte (int32 in these tests)
            else i += 1;
        }
        var bytes = new byte[3 + retBytes.Length + rawParams.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        Array.Copy(retBytes, 0, bytes, 2, retBytes.Length);
        Array.Copy(rawParams, 0, bytes, 2 + retBytes.Length, rawParams.Length);
        return bytes;
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    // The synthesized PE holds exactly one MethodSpec row (row 1) whose
    // underlying MemberReference is the Marshal member.
    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
    }

    // Marshal::<member> MemberReference + one MethodSpecification with the
    // GENERICINST blob (0x0A header, 1 generic argument byte). memberRefSig
    // is the MemberRef signature blob; genericSig is the MethodSpec blob.
    private static MemoryStream SynthesizeGeneric(string ns, string typeName, string memberName,
        byte[] memberRefSig, byte[] genericSig)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(memberRefSig));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSig));
        return Serialize(mdb);
    }

    // A GENERIC method MemberRef signature: header 0x10 (GENERIC) + 1 generic
    // param count + 1 param count + return bytes + VAR(0x13) param index 0.
    private static byte[] MemberRefSig(byte[] retBytes, byte[] varParam)
    {
        var bytes = new byte[3 + retBytes.Length + varParam.Length];
        bytes[0] = 0x10; // GENERIC calling convention
        bytes[1] = 0x01; // generic parameter count
        bytes[2] = 0x01; // parameter count
        Array.Copy(retBytes, 0, bytes, 3, retBytes.Length);
        Array.Copy(varParam, 0, bytes, 3 + retBytes.Length, varParam.Length);
        return bytes;
    }

    private static byte[] VarParam() => new byte[] { 0x13, 0x00 }; // VAR index 0

    private static byte[] GenericSig(byte genericArg) => new byte[] { 0x0A, 0x01, genericArg };

    // P-D2 #2: a synthetic PE with a REAL delegate TypeDef (Cases.Cb --
    // System.MulticastDelegate base, runtime .ctor(object, native int) +
    // int32 Invoke(int32)) and one MethodSpec row: the generic
    // Marshal.GetFunctionPointerForDelegate<T>(T) MemberRef instantiated at
    // Cases.Cb (TypeDefOrRef coded index = rowId << 2).
    private static MemoryStream SynthesizeDelegateGfp()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var mcDelRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("MulticastDelegate"));
        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x02, 0x01, 0x12, 0x05, 0x1C }),
            0, default(ParameterHandle));
        var invoke = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            MethodImplAttributes.Runtime,
            mdb.GetOrAddString("Invoke"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x08, 0x08 }),
            0, default(ParameterHandle));
        var cb = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Cb"), mcDelRef, default, ctor);
        var marshalRef = mdb.AddTypeReference(asmRef,
            mdb.GetOrAddString("System.Runtime.InteropServices"), mdb.GetOrAddString("Marshal"));
        var memberRef = mdb.AddMemberReference(marshalRef, mdb.GetOrAddString("GetFunctionPointerForDelegate"),
            mdb.GetOrAddBlob(MemberRefSig(Ret(0x13, 0x00), VarParam())));
        mdb.AddMethodSpecification(memberRef,
            mdb.GetOrAddBlob(new byte[] { 0x0A, 0x01, 0x12, (byte)(MetadataTokens.GetRowNumber(cb) << 2) }));
        return Serialize(mdb);
    }

    // GN-9 (a): a synthetic PE with a System.Func`1 TypeRef (the nested
    // GenericInst definition) + one MethodSpec row: the generic
    // Marshal.GetDelegateForFunctionPointer<T>(IntPtr) MemberRef instantiated
    // at Func`1<int[]> -- GENERICINST(0x0A) + argc=1 + GenericInst arg(0x15) +
    // CLASS(0x12) Func`1 TypeRef + count=1 + SZARRAY(0x1D) + int(0x08).
    private static MemoryStream SynthesizeGdfpFunc1Array()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var funcRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`1"));
        var marshalRef = mdb.AddTypeReference(asmRef,
            mdb.GetOrAddString("System.Runtime.InteropServices"), mdb.GetOrAddString("Marshal"));
        var memberRef = mdb.AddMemberReference(marshalRef, mdb.GetOrAddString("GetDelegateForFunctionPointer"),
            mdb.GetOrAddBlob(MemberRefSig(Ret(0x13, 0x00), new byte[] { 0x18 })));
        var funcCoded = (byte)((MetadataTokens.GetRowNumber(funcRef) << 2) | 1); // TypeRef tag
        mdb.AddMethodSpecification(memberRef,
            mdb.GetOrAddBlob(new byte[] { 0x0A, 0x01, 0x15, 0x12, funcCoded, 0x01, 0x1D, 0x08 }));
        return Serialize(mdb);
    }

    // GN-10 (i): a synthetic PE with two TypeDefs (row 1 = Cases.Dummy, row 2 =
    // Cases.Pt) so a VALUETYPE coded index (0x08 = row 2 << 2) can name
    // "Cases.Pt", plus one MethodSpec row: GENERICINST(0x0A) + argc=2 +
    // arg0 multidim int[,] (0x14 08 02 00 00) + arg1 VALUETYPE Cases.Pt
    // (0x11 08). The Marshal member itself is irrelevant (only the MethodSpec
    // blob is decoded).
    private static MemoryStream SynthesizeGdfpMultidimStruct() =>
        SynthesizeWithTwoStructs(new byte[] { 0x0A, 0x02, 0x14, 0x08, 0x02, 0x00, 0x00, 0x11, 0x08 });

    // GN-10 (i) correction 1(d): GENERICINST(0x0A) + argc=2 + arg0 GenericInst(0x15)
    // CLASS Cases.Dummy (0x12 04 = row 1 << 2) with one multidim int[,] arg
    // (0x14 08 02 00 00) + arg1 VALUETYPE Cases.Pt (0x11 08).
    private static MemoryStream SynthesizeGdfpNestedMultidimStruct() =>
        SynthesizeWithTwoStructs(new byte[]
        {
            0x0A, 0x02, 0x15, 0x12, 0x04, 0x01, 0x14, 0x08, 0x02, 0x00, 0x00, 0x11, 0x08,
        });

    private static MemoryStream SynthesizeWithTwoStructs(byte[] methodSpecBlob)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        // Row 1 = Cases.Dummy (the nested GenericInst definition), row 2 =
        // Cases.Pt (the VALUETYPE second argument).
        var dummy = mdb.AddTypeDefinition(TypeAttributes.Public, mdb.GetOrAddString("Cases"),
            mdb.GetOrAddString("Dummy"), objRef, default(FieldDefinitionHandle), default(MethodDefinitionHandle));
        var pt = mdb.AddTypeDefinition(TypeAttributes.Public, mdb.GetOrAddString("Cases"),
            mdb.GetOrAddString("Pt"), objRef, default(FieldDefinitionHandle), default(MethodDefinitionHandle));
        var marshalRef = mdb.AddTypeReference(asmRef,
            mdb.GetOrAddString("System.Runtime.InteropServices"), mdb.GetOrAddString("Marshal"));
        var memberRef = mdb.AddMemberReference(marshalRef, mdb.GetOrAddString("GetDelegateForFunctionPointer"),
            mdb.GetOrAddBlob(MemberRefSig(Ret(0x13, 0x00), new byte[] { 0x18 })));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(methodSpecBlob));
        return Serialize(mdb);
    }

    // GN-10 (i): reflection into the private name-table helper (the resolver's
    // internal consumption contract; no public surface exposes it).
    private static string[]? InvokeGenericArgFullNames(MetadataReader md, BlobHandle sigHandle, byte[] genArgs)
    {
        var mi = typeof(MethodRefResolver).GetMethod("GenericArgFullNames",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string[]?)mi!.Invoke(null, new object[] { md, sigHandle, genArgs });
    }

    // GN-10 (i) correction 1(d): replay the MethodSpec genArg PRODUCER (ReadParamByte)
    // to observe the dispatch byte stream the resolver actually sees.
    private static byte[] InvokeReadMethodSpecGenArgs(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        if (sig.ReadByte() != 0x0A) throw new InvalidOperationException("not GENERICINST");
        var argc = sig.ReadCompressedInteger();
        var rp = typeof(MethodRefResolver).GetMethod("ReadParamByte",
            BindingFlags.NonPublic | BindingFlags.Static);
        var genArgs = new byte[argc];
        for (var i = 0; i < argc; i++)
        {
            var args = new object[] { md, sig };
            genArgs[i] = (byte)rp!.Invoke(null, args)!;
            sig = (BlobReader)args[1];
        }
        return genArgs;
    }

    // P2-HARDEN #2: a synthetic PE with a NON-delegate TypeDef (Cases.Fake --
    // System.Object base, but with an Invoke MethodDef the OLD name+Invoke
    // heuristic would match) + the GetFunctionPointerForDelegate<T> MethodSpec
    // instantiated at Cases.Fake.
    private static MemoryStream SynthesizeDelegateGfpNonDelegate()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        // Cases.Fake : System.Object with a long Invoke(long) -- NOT a
        // MulticastDelegate subclass.
        var invoke = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Invoke"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x0A, 0x0A }),
            0, default(ParameterHandle));
        var fake = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Fake"), objRef, default, invoke);
        var marshalRef = mdb.AddTypeReference(asmRef,
            mdb.GetOrAddString("System.Runtime.InteropServices"), mdb.GetOrAddString("Marshal"));
        var memberRef = mdb.AddMemberReference(marshalRef, mdb.GetOrAddString("GetFunctionPointerForDelegate"),
            mdb.GetOrAddBlob(MemberRefSig(Ret(0x13, 0x00), VarParam())));
        mdb.AddMethodSpecification(memberRef,
            mdb.GetOrAddBlob(new byte[] { 0x0A, 0x01, 0x12, (byte)(MetadataTokens.GetRowNumber(fake) << 2) }));
        return Serialize(mdb);
    }

    private static ResolvedMethod ResolveDelegateGfpMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, "Cases.Cb");
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
