using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ReflectHandle dispatch tests: prove the reflection-handle domain
// (MethodMap.ReflectHandle.cs -> TryMapReflectHandle) maps
// RuntimeTypeHandle / RuntimeMethodHandle / RuntimeFieldHandle value ops to
// the reflecthandle.cpp icalls (reflecthandle.h, namespace il2cpp):
//
//   get_Value   -> il2cpp_{rthandle,rmethodhandle,rfieldhandle}_get_value_intptr
//                 (the handle VALUE rides in the intptr_t carrier; the address
//                 receiver is dereferenced by the "deref:l" cast)
//   Equals      -> il2cpp_rthandle_equals / ... (instance, one handle arg)
//   GetHashCode -> il2cpp_rthandle_get_hash_code / ...
//   GetRuntimeType -> il2cpp_rthandle_get_runtime_type (type handle only)
//
// VALUE MODEL: a handle value rides in the pointer carrier (intptr_t slot);
// instance receivers follow the IntPtr precedent (ldloca -> "deref:l").
public class MethodMapReflectHandleTests
{
    [Fact]
    public void Resolve_RuntimeTypeHandle_GetValue_AddressReceiver_MapsToIcall()
    {
        // instance intptr get_Value(): ret IntPtr (0x11 VALUETYPE + TypeHandle
        // byte 0x05), 0 params.
        var m = ResolveMember(Synthesize("System", "RuntimeTypeHandle", "get_Value", Sig(0x11, 0x05)),
            "get_Value", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rthandle_get_value_intptr", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(0, m.ParamCount);
        Assert.Equal("ptr", m.ResultKind);
        Assert.Equal(new[] { "deref:l" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_RuntimeTypeHandle_Equals_AddressReceiver_MapsToIcall()
    {
        // instance bool Equals(RuntimeTypeHandle): one handle param.
        var m = ResolveMember(Synthesize("System", "RuntimeTypeHandle", "Equals", Sig(0x02, 0x11, 0x05)),
            "Equals", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rthandle_equals", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "deref:l", "deref:l" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_RuntimeTypeHandle_GetHashCode_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "RuntimeTypeHandle", "GetHashCode", Sig(0x08)),
            "GetHashCode", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rthandle_get_hash_code", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(new[] { "deref:l" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_RuntimeTypeHandle_GetRuntimeType_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "RuntimeTypeHandle", "GetRuntimeType", Sig(0x12, 0x05)),
            "GetRuntimeType", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rthandle_get_runtime_type", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("System.Type", m.ReturnTypeName);
        Assert.Equal(new[] { "deref:l" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_RuntimeMethodHandle_GetValue_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "RuntimeMethodHandle", "get_Value", Sig(0x11, 0x05)),
            "get_Value", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rmethodhandle_get_value_intptr", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_RuntimeMethodHandle_Equals_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "RuntimeMethodHandle", "Equals", Sig(0x02, 0x11, 0x05)),
            "Equals", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rmethodhandle_equals", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(new[] { "deref:l", "deref:l" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_RuntimeFieldHandle_GetHashCode_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "RuntimeFieldHandle", "GetHashCode", Sig(0x08)),
            "GetHashCode", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rfieldhandle_get_hash_code", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(new[] { "deref:l" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_RuntimeFieldHandle_GetValue_AddressReceiver_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "RuntimeFieldHandle", "get_Value", Sig(0x11, 0x05)),
            "get_Value", receiverIsAddress: true);

        Assert.Equal("il2cpp::il2cpp_rfieldhandle_get_value_intptr", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal("ptr", m.ResultKind);
    }

    [Fact]
    public void Resolve_RuntimeMethodHandle_GetRuntimeType_ThrowsLoudNotWired()
    {
        // GetRuntimeType exists only on the TYPE handle.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "RuntimeMethodHandle", "GetRuntimeType", Sig(0x12, 0x05)),
                "GetRuntimeType", receiverIsAddress: true));

        Assert.Contains("System.RuntimeMethodHandle::GetRuntimeType", ex.Message);
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
        // Build a REAL MemberRef signature blob (the walking-Sig contract of
        // MethodMapTests.Marshal/Net): CLASS/VALUETYPE returns (0x11/0x12/0x40)
        // carry a trailing TypeHandle byte; params with trailing data
        // (0x11/0x12/0x40/0x1D/0x1F/0x10) consume their element/coded-index
        // byte. The param count derives from the walk, not ps.Length.
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
