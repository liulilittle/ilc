using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.DateTime FileTime dispatch tests (B3 diffdt001b): the Utc and
// Local forms are DISTINCT icalls -- FromFileTime/ToFileTime convert through
// the machine zone's per-date offset (net48 semantics), FromFileTimeUtc/
// ToFileTimeUtc only shift the 1601 epoch.
public class MethodMapTestsDateTimeFileTime
{
    [Fact]
    public void Resolve_DateTime_ToFileTime_MapsToLocalIcall()
    {
        // instance long ToFileTime(): header + 0 params + ret int64 (0x0A).
        var resolved = ResolveMember(Synthesize("System", "DateTime", "ToFileTime", Sig(0x0A)), "ToFileTime");

        Assert.Equal("il2cpp::runtime::icall_datetime_to_file_time", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_ToFileTimeUtc_MapsToUtcIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTime", "ToFileTimeUtc", Sig(0x0A)), "ToFileTimeUtc");

        Assert.Equal("il2cpp::runtime::icall_datetime_to_file_time_utc", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_FromFileTime_MapsToLocalIcall()
    {
        // static DateTime FromFileTime(long): ret VALUETYPE (0x11) +
        // TypeDefOrRef coded index (DateTime TypeRef row 1 -> 0x05).
        var resolved = ResolveMember(SynthesizeWithDateTimeRef("System", "DateTime", "FromFileTime",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0A }), "FromFileTime");

        Assert.Equal("il2cpp::runtime::icall_datetime_from_file_time", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_FromFileTimeUtc_MapsToUtcIcall()
    {
        var resolved = ResolveMember(SynthesizeWithDateTimeRef("System", "DateTime", "FromFileTimeUtc",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0A }), "FromFileTimeUtc");

        Assert.Equal("il2cpp::runtime::icall_datetime_from_file_time_utc", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "l" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_DateTime_GetHashCode_MapsToHashIcall()
    {
        // D1 (diffdt001 dt.hash): instance int GetHashCode() -- net48
        // ticks-fold (icall_datetime_get_hash_code, time.cpp). Oracle:
        // D6 ticks 637185764960000000 -> -1061690093.
        var resolved = ResolveMember(Synthesize("System", "DateTime", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_datetime_get_hash_code", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Subtract(TimeSpan) vs Subtract(DateTime) -- both ride a single
    // VALUETYPE (0x11) parameter byte; the decoded parameter-type text
    // disambiguates (the DateTimeOffset precedent).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_SubtractTimeSpan_MapsToSubtractTicks()
    {
        // DateTime.Subtract(TimeSpan) -> DateTime: the TimeSpan TypeRef is
        // row 2 (coded 0x09), DateTime row 1 (coded 0x05).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeTimeSpanRefs("System", "DateTime", "Subtract",
                new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x09 }), "Subtract");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_subtract_ticks", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt", "" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_SubtractDateTime_MapsToSubtractIcall()
    {
        // DateTime.Subtract(DateTime) -> TimeSpan: the ret is TimeSpan (row 2),
        // the param DateTime (row 1).
        var resolved = ResolveMember(
            SynthesizeWithDateTimeTimeSpanRefs("System", "DateTime", "Subtract",
                new byte[] { 0x00, 0x01, 0x11, 0x09, 0x11, 0x05 }), "Subtract");

        Assert.Equal("il2cpp::runtime::icall_datetime_subtract", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt", "" }, resolved.ParamTypeNames);
        Assert.Equal("ts", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToLocalTime / ToUniversalTime -- the instance converters (timezone.h
    // il2cpp_datetime_to_local / from_local).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_ToLocalTime_MapsToLocalIcall()
    {
        // instance DateTime ToLocalTime(): ret VALUETYPE (0x11) + DateTime
        // TypeRef coded index (0x05), 0 params.
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "ToLocalTime",
                new byte[] { 0x00, 0x00, 0x11, 0x05 }), "ToLocalTime");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_to_local", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_DateTime_ToUniversalTime_MapsToFromLocalIcall()
    {
        var resolved = ResolveMember(
            SynthesizeWithDateTimeRef("System", "DateTime", "ToUniversalTime",
                new byte[] { 0x00, 0x00, 0x11, 0x05 }), "ToUniversalTime");

        Assert.Equal("il2cpp::runtime::il2cpp_datetime_from_local", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "deref:dt" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
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

    private static MemoryStream SynthesizeWithDateTimeRef(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        // DateTime TypeRef FIRST (row 1 -> coded 0x05, matching the blob).
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    // DateTime + TimeSpan TypeRefs: row 1 = DateTime (coded 0x05), row 2 =
    // TimeSpan (coded 0x09), matching the Subtract blob's coded indices.
    private static MemoryStream SynthesizeWithDateTimeTimeSpanRefs(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("TimeSpan"));
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
