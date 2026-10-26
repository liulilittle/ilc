using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Net dispatch tests: prove the System.Net minimal domain
// (MethodMap.Net.cs -- socket.h / ipaddr.h / http.h, the flat M-stage
// runtime surface).
//
//   IPAddress.Parse(string)   -> il2cpp_ipaddress_parse   (static, "ip4"
//                                value-slot kind -- writes through the
//                                output buffer like Guid.Parse)
//   IPAddress.ToString()      -> il2cpp_ipaddress_to_string (instance,
//                                returns string; the receiver IS the ip4
//                                slot, pass-through "")
//   Socket ctor/Connect/Send/Receive/Close -> LOUD NotSupportedException
//                                (handle gap: socket.h is an opaque-handle
//                                API with no object-typed icalls and no
//                                descriptor; closure = icall_socket_* +
//                                System_Net_Sockets_Socket_class per
//                                net-domain.md sections 2.3 + 4.2)
//   WebClient ctor/DownloadString -> LOUD NotSupportedException (no
//                                descriptor; the http.h engine needs a
//                                single-call managed wrapper)
//
// DEPLOYMENT NOTE: these tests resolve through the owner dispatch in
// MethodRefResolver.cs. The System.Net case lines are PENDING the parallel
// decimal-domain session that currently owns that file (2026-08-05 ~18:1x)
// -- until they land, Resolve returns null and these tests are RED.
public class MethodMapNetTests
{
    //--------------------------------------------------------------------------
    // IPAddress.Parse(string) -> il2cpp_ipaddress_parse (static, ip4 kind)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IPAddress_Parse_MapsToParseLayer()
    {
        var m = ResolveMember(Synthesize("System.Net", "IPAddress", "Parse", Sig(0x12, 0x05, 0x0E)), "Parse");

        Assert.Equal("il2cpp::runtime::il2cpp_ipaddress_parse", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal("ip4", m.ResultKind);
        // The parse input is a managed string.
        Assert.Equal("s", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // IPAddress.ToString() -> il2cpp_ipaddress_to_string (instance, string)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IPAddress_ToString_MapsToToString()
    {
        var m = ResolveMember(Synthesize("System.Net", "IPAddress", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::il2cpp_ipaddress_to_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        // The receiver (the ip4 value slot) is a byte-slot param -- the "g"
        // cast code (rendered (uint8_t*)(...); the Guid contract,
        // MethodMapTests.Guid.cs:18-35 -- valid for both the plain-array
        // value and the ldloca address shape).
        Assert.Equal("g", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // IPAddress.GetAddressBytes() -> il2cpp_ipaddress_get_address_bytes
    // (instance, returns byte[]; the receiver IS the ip4 value slot, the "g"
    // byte-slot cast).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IPAddress_GetAddressBytes_MapsToAddressBytesIcall()
    {
        // instance byte[] GetAddressBytes(): ret SZARRAY (0x1D) + element byte
        // (0x05), no params.
        var m = ResolveMember(Synthesize("System.Net", "IPAddress", "GetAddressBytes",
            new byte[] { 0x00, 0x00, 0x1D, 0x05 }), "GetAddressBytes");

        Assert.Equal("il2cpp::runtime::il2cpp_ipaddress_get_address_bytes", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal("g", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // IPAddress.GetHashCode() -> il2cpp_ipaddress_get_hash_code (instance,
    // returns int32; net48 IPv4: unchecked((int)m_Address) -- the little-endian
    // fold of the four address bytes).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IPAddress_GetHashCode_MapsToHashIcall()
    {
        var m = ResolveMember(Synthesize("System.Net", "IPAddress", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::il2cpp_ipaddress_get_hash_code", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal("g", m.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // IPAddress.Equals(object) (net48) -- Roslyn emits `callvirt
    // Object::Equals(object)` for the override (the constrained callvirt
    // passes the ip4 value ADDRESS), so the receiver-typed Object arm is the
    // live path: the value-vs-object helper il2cpp_ipaddress_equals_object
    // (the generic icall_boxed_equals_ext2 takes Il2CppObject*).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_IPAddress_EqualsObject_MapsToEqualsObjectIcall()
    {
        var m = ResolveMemberWithReceiver(Synthesize("System", "Object", "Equals", Sig(0x02, 0x1C)),
            "Equals", "System.Net.IPAddress", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::il2cpp_ipaddress_equals_object", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "g", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Socket -- the handle gap: every requested member is a LOUD rejection
    // with the exact runtime closure spelled out
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_Ctor_ThrowsHandleGap()
    {
        // Socket object layer is WIRED (il2cpp_netsocket.h): the ctor maps to
        // the object icall, not a LOUD handle-gap rejection.
        var m = ResolveMember(Synthesize("System.Net.Sockets", "Socket", ".ctor",
                Sig(0x01, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_socket_ctor", m.CppName);
        Assert.True(m.IsInstance);
    }

    [Fact]
    public void Resolve_Socket_Connect_ThrowsHandleGap()
    {
        var m = ResolveMember(Synthesize("System.Net.Sockets", "Socket", "Connect",
                Sig(0x01, 0x0E, 0x08)), "Connect");

        Assert.Equal("il2cpp::runtime::icall_socket_connect_host", m.CppName);
        Assert.True(m.IsInstance);
    }

    [Theory]
    [InlineData("Send", "il2cpp::runtime::icall_socket_send")]
    [InlineData("Receive", "il2cpp::runtime::icall_socket_receive")]
    public void Resolve_Socket_SendReceive_ThrowsHandleGap(string name, string expected)
    {
        var m = ResolveMember(Synthesize("System.Net.Sockets", "Socket", name,
                Sig(0x08, 0x1D, 0x07)), name);

        Assert.Equal(expected, m.CppName);
        Assert.True(m.IsInstance);
    }

    [Fact]
    public void Resolve_Socket_Close_ThrowsHandleGap()
    {
        var m = ResolveMember(Synthesize("System.Net.Sockets", "Socket", "Close", Sig(0x01)), "Close");

        Assert.Equal("il2cpp::runtime::icall_socket_close", m.CppName);
        Assert.True(m.IsInstance);
    }

    //--------------------------------------------------------------------------
    // WebClient -- wired (web-b23 closure): the parameterless ctor is a no-op
    // object icall; DownloadString(string) is the single-call HTTP closure
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WebClient_DownloadString_MapsToDownloadIcall()
    {
        // instance string DownloadString(string url): ret string (0x0E), one
        // string param.
        var m = ResolveMember(Synthesize("System.Net", "WebClient", "DownloadString", Sig(0x0E, 0x0E)), "DownloadString");

        Assert.Equal("il2cpp::runtime::icall_web_client_download_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        // Receiver "" passthrough + url "s" (Il2CppString*).
        Assert.Equal(new[] { "", "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_WebClient_Ctor_MapsToCtorIcall()
    {
        var m = ResolveMember(Synthesize("System.Net", "WebClient", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_web_client_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.VersionUri.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Receiver-aware resolution (the Object::Equals(object) override shape --
    // the tracked value-slot receiver type selects the domain arm).
    private static ResolvedMethod ResolveMemberWithReceiver(MemoryStream pe, string memberName,
        string? receiverType, bool receiverIsAddress)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, receiverIsAddress, null);
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then the parameter bytes (the raw bytes RawSignature decodes).
    // Multi-byte types: CLASS (0x12)/VALUETYPE (0x11) carry their TypeDefOrRef
    // coded index byte (0x05 = the owner TypeRef at row 1) immediately after
    // the marker; SZARRAY (0x1D) carries its element byte. The helper walks
    // the parameter list with those consumption rules so the blob's param
    // count matches reality (one blob byte per type, NOT one per raw byte) --
    // the IPAddress.Parse CLASS return therefore reads 0x12 0x05 + the
    // string param (0x0E), a 5-byte blob like the real netcase.exe MemberRef.
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var retIndex = ret is 0x11 or 0x12 or 0x40;
        var body = new System.Collections.Generic.List<byte>();
        var count = 0;
        var idx = 0;
        if (retIndex) idx = 1; // first ps byte = the return type's coded index
        while (idx < ps.Length)
        {
            count++;
            var b = ps[idx++];
            body.Add(b);
            if (b is 0x11 or 0x12 or 0x40 or 0x1D or 0x1F) body.Add(ps[idx++]); // trailing byte
        }
        var bytes = new byte[3 + (retIndex ? 1 : 0) + body.Count];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        bytes[2] = ret;
        var offset = 3;
        if (retIndex) bytes[offset++] = ps[0];
        body.CopyTo(bytes, offset);
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
