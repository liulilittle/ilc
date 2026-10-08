using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Legacy dispatch tests (MethodMap.Legacy.cs -> TryMapLegacy,
// reached through the Remoting/AppDomain/ArgIterator/ReaderWriterLock case
// labels): the net48 audit §3/§6 LOUD-rejection families. Every member of
// these owners is rejected as a family -- without the cases they silently
// fell to the generated-name path (undefined C++ symbol at link).
//
// Same synthetic-PE pipeline as MethodMapTests.Interlocked.
public class MethodMapTestsLegacy
{
    //--------------------------------------------------------------------------
    // System.Runtime.Remoting.* -- the 6 public-face RemotingServices gaps
    // (net48 audit §3 row 979: "legacy technique, LOUD rejection recommended"). The family has no
    // runtime infrastructure (proxies/ObjRef/channels/contexts).
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "IsTransparentProxy")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "GetRealProxy")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "CreateTransparentProxy")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "AllocateUninitializedObject")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "AllocateInitializedObject")]
    [InlineData("System.Runtime.Remoting", "RemotingServices", "CallDefaultCtor")]
    [InlineData("System.Runtime.Remoting.Proxies", "RealProxy", "GetStubData")]
    public void Resolve_RemotingFamily_ThrowsLoud(string ns, string type, string memberName)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize(ns, type, memberName, Sig(0x1C, 0x1C)), memberName));

        Assert.Contains($"System.Runtime.Remoting", ex.Message);
    }

    //--------------------------------------------------------------------------
    // AppDomain -- legacy/low-frequency (net48 audit §3): no AppDomain object
    // in the runtime; the AOT process IS the default domain.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppDomain_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "AppDomain", "get_FriendlyName", Sig(0x0E)), "get_FriendlyName"));

        Assert.Contains("System.AppDomain", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ArgIterator -- legacy varargs-reflection primitive.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ArgIterator_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "ArgIterator", "GetRemainingCount", Sig(0x08)), "GetRemainingCount"));

        Assert.Contains("System.ArgIterator", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ReaderWriterLock -- legacy low-frequency lock.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ReaderWriterLock_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Threading", "ReaderWriterLock", "AcquireReaderLock", Sig(0x01, 0x08)),
                "AcquireReaderLock"));

        Assert.Contains("System.Threading.ReaderWriterLock", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the Interlocked-test pipeline).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

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
