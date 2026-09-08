using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Net Socket dispatch tests: prove the seven wired System.Net.Sockets.Socket
// members (TryMapSocket in MethodMap.Net.cs) resolve to their runtime icalls:
//
//   .ctor(AddressFamily, SocketType, ProtocolType) -> icall_socket_ctor (int32)
//   Connect(string, int)                          -> icall_socket_connect_host (void)
//   Send(byte[])                                  -> icall_socket_send (int32)
//   Send(byte[], int, int)                        -> icall_socket_send_offset (int32)
//   Receive(byte[])                               -> icall_socket_receive (int32)
//   Receive(byte[], int, int)                     -> icall_socket_receive_offset (int32)
//   Close()                                       -> icall_socket_close (void)
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void, 0x08=int32,
// 0x0E=string, 0x1D=SZARRAY). Param count + element bytes drive TryMapSocket.
public class MethodMapSocketTests
{
    //------------------------------------------------------------------
    // .ctor(AddressFamily, SocketType, ProtocolType) -> icall_socket_ctor
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_Ctor_MapsToIcall()
    {
        var m = ResolveMember(Synthesize(".ctor", Sig(0x01, 0x08, 0x08, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_socket_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(3, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "i", "i", "i" }, m.ParamTypeNames);
    }

    //------------------------------------------------------------------
    // Connect(string, int) -> icall_socket_connect_host
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_Connect_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Connect", Sig(0x01, 0x0E, 0x08)), "Connect");

        Assert.Equal("il2cpp::runtime::icall_socket_connect_host", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.Equal(new[] { "", "s", "i" }, m.ParamTypeNames);
    }

    //------------------------------------------------------------------
    // Send(byte[]) -> icall_socket_send
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_SendByteArray_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Send", Sig(0x08, 0x1D, 0x05)), "Send");

        Assert.Equal("il2cpp::runtime::icall_socket_send", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "a" }, m.ParamTypeNames);
    }

    //------------------------------------------------------------------
    // Send(byte[], int, int) -> icall_socket_send_offset
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_SendOffset_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Send", Sig(0x08, 0x1D, 0x05, 0x08, 0x08)), "Send");

        Assert.Equal("il2cpp::runtime::icall_socket_send_offset", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(3, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "a", "i", "i" }, m.ParamTypeNames);
    }

    //------------------------------------------------------------------
    // Receive(byte[]) -> icall_socket_receive
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_ReceiveByteArray_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Receive", Sig(0x08, 0x1D, 0x05)), "Receive");

        Assert.Equal("il2cpp::runtime::icall_socket_receive", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "a" }, m.ParamTypeNames);
    }

    //------------------------------------------------------------------
    // Receive(byte[], int, int) -> icall_socket_receive_offset
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_ReceiveOffset_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Receive", Sig(0x08, 0x1D, 0x05, 0x08, 0x08)), "Receive");

        Assert.Equal("il2cpp::runtime::icall_socket_receive_offset", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(3, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
        Assert.Equal(new[] { "", "a", "i", "i" }, m.ParamTypeNames);
    }

    //------------------------------------------------------------------
    // Close() -> icall_socket_close
    //------------------------------------------------------------------
    [Fact]
    public void Resolve_Socket_Close_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Close", Sig(0x01)), "Close");

        Assert.Equal("il2cpp::runtime::icall_socket_close", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    //------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Timers.cs, owner System.Net.Sockets.Socket).
    //------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var retIndex = ret is 0x11 or 0x12 or 0x40;
        var body = new System.Collections.Generic.List<byte>();
        var count = 0;
        var idx = 0;
        if (retIndex) idx = 1;
        while (idx < ps.Length)
        {
            count++;
            var b = ps[idx++];
            body.Add(b);
            if (b is 0x11 or 0x12 or 0x40 or 0x1D or 0x1F) body.Add(ps[idx++]);
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

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Net.Sockets"), mdb.GetOrAddString("Socket"));
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
