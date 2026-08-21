using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.IntPtr/UIntPtr dispatch tests: prove the System.IntPtr and
// System.UIntPtr domains (MethodMap.IntPtr.cs / MethodMap.UIntPtr.cs ->
// TryMapIntPtr / TryMapUIntPtr) map the pointer-arithmetic surface to the
// runtime helpers declared in src/runtime/include/internal/intptr.h:
//
//   IntPtr.Add/Subtract(IntPtr, int)     -> il2cpp_intptr_add/subtract
//   IntPtr.ToInt64()/ToInt32()           -> il2cpp_intptr_to_int64/int32
//   IntPtr.op_Equality(IntPtr, IntPtr)   -> il2cpp_intptr_op_equality
//   IntPtr.CompareTo(IntPtr)             -> il2cpp_intptr_compare
//   UIntPtr.Add/Subtract/op_Equality/    -> the SAME helpers (the runtime
//     CompareTo                          ->  reuses them for UIntPtr)
//
// Receiver handling for the INSTANCE methods: an IntPtr value rides in a
// FULL-WIDTH carrier (an intptr_t slot -- batch 155b widened the IntPtr
// value model and batch 177's "ptr" ResultKind declares intptr_t temps,
// CodeGenerator.cs:5213), and the helpers take intptr_t. The
// address-receiver cast width matches the value the method produces:
//   * ToInt64 (full 64-bit value) -> "deref:l" (read the int64 slot -- a
//     "deref:i" would truncate x64 pointers; batch 155b's "ToInt64 deref
//     width fix (int64)")
//   * ToInt32 / CompareTo (int32 result / int32 ordering) -> "deref:i"
//   * plain VALUE receiver (e.g. the result of Add -- a pointer-arithmetic
//     chain like IntPtr.Add(p, 1).ToInt64()) -> "" pass-through (the
//     intptr_t expression passes to the intptr_t parameter directly)
//   * boxed receivers are UNREACHABLE: the runtime has no IntPtr box class
//     (the emitter's box table rejects it LOUDLY), so no unbox cast exists.
//
// Like MethodRefResolverDispatchTests, the PEs are synthesized with
// System.Reflection.Metadata's writer. Signature blobs with valuetype (0x11)
// parameters carry the TypeDefOrRef coded index of the owner TypeRef (row 1
// -> 0x05) after the 0x11 byte, exactly as RawSignature reads them.
public class MethodMapTestsIntPtr
{
    //--------------------------------------------------------------------------
    // IntPtr domain -- static arithmetic (MethodMap.IntPtr.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IntPtr_Add_MapsToPointerAddIcall()
    {
        // static IntPtr Add(IntPtr, int): ret valuetype, param0 valuetype, param1 i4
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "Add",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x08 }), "Add");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_add", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_IntPtr_Subtract_MapsToPointerSubtractIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "Subtract",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x08 }), "Subtract");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_subtract", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_IntPtr_OpEquality_MapsToPointerEqualityIcall()
    {
        // static bool op_Equality(IntPtr, IntPtr)
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "op_Equality",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Equality");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_op_equality", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // IntPtr domain -- instance conversions; the receiver cast depends on the
    // receiver shape (ldloca address -> deref, plain value -> pass-through).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IntPtr_ToInt64_AddressReceiver_DerefsFullWidthSlot()
    {
        // instance int64 ToInt64(): receiver = ldloca address. An IntPtr
        // value rides in a FULL-WIDTH carrier (intptr_t slot, batch 155b),
        // and ToInt64 must produce the whole 64-bit value -- so the address
        // receiver derefs the INT64 slot ("deref:l"). A "deref:i" (read the
        // low int32 slot) would truncate the pointer value on x64; batch
        // 155b's "ToInt64 deref width fix (int64)" made "deref:l" the
        // correct expectation (this test pre-dated that fix and asserted the
        // old int32 model).
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "ToInt64", Sig(0x0A)),
            "ToInt64", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_to_int64", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal("deref:l", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_IntPtr_ToInt32_AddressReceiver_DerefsInt32Slot()
    {
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "ToInt32", Sig(0x08)),
            "ToInt32", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_to_int32", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal("deref:i", resolved.ParamTypeNames![0]);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_IntPtr_ToInt64_ValueReceiver_PassesThrough()
    {
        // The pointer-arithmetic chain: IntPtr.Add(p, 1).ToInt64() -- the
        // receiver is the Add RESULT (a plain int32_t temp, not an address
        // and not a box). The receiver cast must be the "" pass-through (the
        // value sign-extends to intptr_t on the call).
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "ToInt64", Sig(0x0A)),
            "ToInt64", receiverIsAddress: false);

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_to_int64", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal("", resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Resolve_IntPtr_ToInt32_ValueReceiver_PassesThrough()
    {
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "ToInt32", Sig(0x08)),
            "ToInt32", receiverIsAddress: false);

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_to_int32", resolved.CppName);
        Assert.Equal("", resolved.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // IntPtr domain -- CompareTo (instance, one IntPtr argument)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IntPtr_CompareTo_AddressReceiver_MapsToPointerCompare()
    {
        // instance int CompareTo(IntPtr)
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "CompareTo",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "CompareTo", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_compare", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("deref:i", resolved.ParamTypeNames![0]);
        Assert.Equal("", resolved.ParamTypeNames![1]); // the IntPtr argument passes through
    }

    [Fact]
    public void Resolve_IntPtr_CompareToObject_ThrowsLoud()
    {
        // instance int CompareTo(object): the icall takes an intptr_t
        // argument -- a boxed pointer cannot reach it in the M-stage.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "IntPtr", "CompareTo", Sig(0x08, 0x1C)), "CompareTo"));

        Assert.Contains("System.IntPtr::CompareTo", ex.Message);
    }

    //--------------------------------------------------------------------------
    // UIntPtr domain -- the same helpers serve UIntPtr values (intptr.h)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_UIntPtr_Add_MapsToSamePointerAddIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "UIntPtr", "Add",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x08 }), "Add");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_add", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_UIntPtr_Subtract_MapsToSamePointerSubtractIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "UIntPtr", "Subtract",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x08 }), "Subtract");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_subtract", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_UIntPtr_OpEquality_MapsToSamePointerEqualityIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "UIntPtr", "op_Equality",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Equality");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_op_equality", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_UIntPtr_CompareTo_AddressReceiver_MapsToPointerCompare()
    {
        var resolved = ResolveMember(Synthesize("System", "UIntPtr", "CompareTo",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "CompareTo", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_compare", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("deref:i", resolved.ParamTypeNames![0]);
        Assert.Equal("", resolved.ParamTypeNames![1]);
    }

    [Fact]
    public void Resolve_UIntPtr_CompareToObject_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "UIntPtr", "CompareTo", Sig(0x08, 0x1C)), "CompareTo"));

        Assert.Contains("System.UIntPtr::CompareTo", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Negative: a signature the domain does not map falls back to the
    // generated-name path (no blind mapping of any Add-shaped member).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IntPtr_Add_WrongSignature_FallsBackToGeneratedName()
    {
        // Add(IntPtr, int) is the only overload; a one-parameter Add(int)
        // shape must NOT hit the pointer-arithmetic mapping.
        var resolved = ResolveMember(Synthesize("System", "IntPtr", "Add",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x08 }), "Add");

        Assert.StartsWith("System_IntPtr_Add_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        bool receiverIsAddress = false)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, receiverIsAddress, null, null);
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
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
