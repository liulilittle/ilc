using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.GCHandle dispatch tests: prove the
// System.Runtime.InteropServices.GCHandle domain (MethodMap.GCHandle.cs ->
// TryMapGCHandle) maps to the gchandle.cpp handle-table icalls
// (il2cpp_gchandle.h):
//
//   Alloc(object)                   -> icall_gchandle_alloc_normal (static)
//   Alloc(object, GCHandleType)     -> icall_gchandle_alloc (static, type "i")
//   Free()                          -> icall_gchandle_free (instance)
//   get_Target / set_Target         -> icall_gchandle_get/set_target (instance)
//   AddrOfPinnedObject()            -> icall_gchandle_addr_of_pinned ("ptr")
//   get_IsAllocated                 -> icall_gchandle_is_allocated (instance)
//   ToIntPtr / FromIntPtr           -> LOUD (the runtime icalls do NOT exist)
//   op_Equality/op_Inequality       -> il2cpp_intptr_op_equality/inequality
//
// VALUE MODEL: a GCHandle value rides in an int32_t slot (the CodeGenerator
// TypeCppName entry "System.Runtime.InteropServices.GCHandle" => "int32_t",
// the IntPtr carrier precedent). Instance receivers:
//   * ldloca receiver (receiverIsAddress) -> "deref:i" (read the int32 slot)
//   * plain VALUE receiver -> "" pass-through
public class MethodMapGCHandleTests
{
    [Fact]
    public void Resolve_GCHandle_AllocObject_MapsToAllocNormal()
    {
        // static int32 Alloc(object): ret int32, one object param.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "Alloc", Sig(0x08, 0x1C)), "Alloc");

        Assert.Equal("il2cpp::runtime::icall_gchandle_alloc_normal", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_AllocObjectType_MapsToAlloc()
    {
        // static int32 Alloc(object, GCHandleType): the enum param decodes as
        // VALUETYPE (0x11 + TypeHandle byte 0x05), cast "i".
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "Alloc",
            Sig(0x08, 0x1C, 0x11, 0x05)), "Alloc");

        Assert.Equal("il2cpp::runtime::icall_gchandle_alloc", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_Free_AddressReceiver_DerefsSlot()
    {
        // instance void Free(): the ldloca receiver is the int32 handle slot.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "Free", Sig(0x01)),
            "Free", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_gchandle_free", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal(new[] { "deref:i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_GetTarget_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "get_Target", Sig(0x1C)),
            "get_Target", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_gchandle_get_target", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsRef);
        Assert.Equal(new[] { "deref:i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_SetTarget_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "set_Target", Sig(0x01, 0x1C)),
            "set_Target", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_gchandle_set_target", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "deref:i", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_AddrOfPinned_AddressReceiver_MapsToIcallPtr()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "AddrOfPinnedObject", Sig(0x11, 0x05)),
            "AddrOfPinnedObject", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_gchandle_addr_of_pinned", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal("ptr", m.ResultKind);
        Assert.Equal(new[] { "deref:i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_GetIsAllocated_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "get_IsAllocated", Sig(0x02)),
            "get_IsAllocated", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_gchandle_is_allocated", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(new[] { "deref:i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_ToIntPtr_FoldsToIdentity()
    {
        // GCHandle.ToIntPtr(GCHandle value): STATIC in net48 (verified against
        // the net48 reference assembly). The handle value IS the int32 slot and
        // an IntPtr rides the SAME slot -- the result is the argument verbatim
        // (the identity fold).
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "ToIntPtr", Sig(0x11, 0x05, 0x11, 0x05)), "ToIntPtr");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
    }

    [Fact]
    public void Resolve_GCHandle_FromIntPtr_FoldsToIdentity()
    {
        // GCHandle.FromIntPtr(IntPtr): the reverse identity -- the IntPtr
        // value IS the handle (both ride the int32 slot). The result is the
        // argument.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "FromIntPtr", Sig(0x11, 0x05, 0x11, 0x05)), "FromIntPtr");

        Assert.Equal(ResolvedMethod.IdentityMarker, m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
    }

    [Fact]
    public void Resolve_GCHandle_OpEquality_MapsToIntPtrEquality()
    {
        // static bool op_Equality(GCHandle, GCHandle): both operands are
        // VALUETYPE (0x11 + TypeHandle byte 0x05); the raw handle values
        // compare via the IntPtr equality helper.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "op_Equality",
            Sig(0x02, 0x11, 0x05, 0x11, 0x05)), "op_Equality");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_op_equality", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_GCHandle_OpInequality_MapsToIntPtrInequality()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "GCHandle", "op_Inequality",
            Sig(0x02, 0x11, 0x05, 0x11, 0x05)), "op_Inequality");

        Assert.Equal("il2cpp::runtime::il2cpp_intptr_op_inequality", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (MethodMapTests.IntPtr.cs pipeline)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        bool receiverIsAddress = false)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, receiverIsAddress, null);
    }

    private static byte[] Sig(byte ret, params byte[] ps)
    {
        // Build a REAL MemberRef signature blob. ret may carry trailing bytes
        // for CLASS/VALUETYPE returns (0x11/0x12/0x40 + the TypeDefOrRef coded
        // index byte); each param byte that carries trailing data (0x11/0x12/
        // 0x40/0x1D/0x1F/0x10) consumes its element/coded-index byte. The
        // param count is derived by walking, NOT from ps.Length (which counts
        // raw bytes) -- the MethodSignatureCoder/RawSignature contract.
        var retBytes = new List<byte> { ret };
        var body = new List<byte>();
        var count = 0;
        var idx = 0;
        if (ret is 0x11 or 0x12 or 0x40) { retBytes.Add(ps[0]); idx = 1; }
        while (idx < ps.Length)
        {
            count++;
            var b = ps[idx++];
            body.Add(b);
            if (b is 0x11 or 0x12 or 0x40 or 0x1D or 0x1F or 0x10) body.Add(ps[idx++]);
        }
        var bytes = new byte[3 + retBytes.Count + body.Count];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        bytes[2] = ret;
        var off = 3;
        if (ret is 0x11 or 0x12 or 0x40) bytes[off++] = ps[0];
        body.CopyTo(bytes, off);
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
