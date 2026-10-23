using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.TimeSpan ToString overload dispatch tests (dtfmt): the 0-arg form
// keeps the constant "c" icall; ToString(string) and ToString(string,
// IFormatProvider) route to the format icall with the value passed BY VALUE
// ("deref:ts") and the provider DROPPED (every TimeSpan layout is
// culture-invariant).
public class MethodMapTestsTimeSpanToString
{
    [Fact]
    public void Resolve_TimeSpan_ToString_NoArgs_MapsToPlainIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeSpan", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_timespan_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:ts" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_TimeSpan_ToString_StringArg_MapsToFormatIcall()
    {
        // instance string ToString(string format): ret 0x0E, one 0x0E param.
        var resolved = ResolveMember(Synthesize("System", "TimeSpan", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_timespan_to_string_format", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:ts", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_TimeSpan_ToString_StringAndProvider_DropsProvider()
    {
        // instance string ToString(string, IFormatProvider): two 0x0E params.
        var resolved = ResolveMember(Synthesize("System", "TimeSpan", "ToString", Sig(0x0E, 0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_timespan_to_string_format", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "deref:ts", "", "drop" }, resolved.ParamTypeNames);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);
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
