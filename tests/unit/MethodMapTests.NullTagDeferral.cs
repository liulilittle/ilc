using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// RISK5 (T2 §9 R1-R5): a null instantiation tag must DEFER instead of silently
// taking the historical default arm (List<int>/dictsi/HashSet<string>/
// non-generic Queue/Stack). Observable contract: hint-less Resolve returns the
// fictional-BCL fallback (IsFictionalBclFallback -- the fold re-resolves with
// the tracked receiver), and the FINAL RequireResolvable gate throws LOUD
// (P26/charconv G2 deferral contract).
public class MethodMapTestsNullTagDeferral
{
    [Fact]
    public void Resolve_List_NullTag_DefersThenLoud()
    {
        AssertDeferThenLoud("System.Collections.Generic", "List`1", "Add",
            new byte[] { 0x20, 0x01, 0x02, 0x13, 0x00 },
            "System.Collections.Generic.List`1::Add");
    }

    [Fact]
    public void Resolve_Dictionary_NullTag_DefersThenLoud()
    {
        AssertDeferThenLoud("System.Collections.Generic", "Dictionary`2", "Add",
            new byte[] { 0x20, 0x02, 0x02, 0x0E, 0x08 },
            "System.Collections.Generic.Dictionary`2::Add");
    }

    [Fact]
    public void Resolve_HashSet_NullTag_DefersThenLoud()
    {
        AssertDeferThenLoud("System.Collections.Generic", "HashSet`1", "Add",
            new byte[] { 0x20, 0x01, 0x02, 0x0E },
            "System.Collections.Generic.HashSet`1::Add");
    }

    [Fact]
    public void Resolve_Queue_NullTag_DefersThenLoud()
    {
        AssertDeferThenLoud("System.Collections.Generic", "Queue`1", "Enqueue",
            new byte[] { 0x20, 0x01, 0x02, 0x13, 0x00 },
            "System.Collections.Generic.Queue`1::Enqueue");
    }

    [Fact]
    public void Resolve_Stack_NullTag_DefersThenLoud()
    {
        AssertDeferThenLoud("System.Collections.Generic", "Stack`1", "Push",
            new byte[] { 0x20, 0x01, 0x02, 0x13, 0x00 },
            "System.Collections.Generic.Stack`1::Push");
    }

    private static void AssertDeferThenLoud(string ns, string typeName, string memberName, byte[] signature, string expectedFragment)
    {
        using var pe = Synthesize(ns, typeName, memberName, signature);
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        var token = MetadataTokens.GetToken(handle);
        var resolved = MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, null, null);
        // Deferral: no silent default arm -- the fictional-BCL fallback marker.
        Assert.True(resolved.IsFictionalBclFallback);
        // Final gate: LOUD with the owner::member identity.
        var ex = Assert.Throws<NotSupportedException>(() =>
            MethodRefResolver.RequireResolvable(md, token, resolved));
        Assert.Contains(expectedFragment, ex.Message);
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
