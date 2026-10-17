using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.CultureInfo dispatch tests for the nb2 CULT-GET surface
// (System.Globalization.CultureInfo):
//   * GetCultureInfo(string) -> the runtime name-carrier icall, stamped
//     ReturnTypeName=System.Globalization.CultureInfo so the chained
//     get_Name / get_TwoLetterISOLanguageName resolve on the tracked type
//   * get_TwoLetterISOLanguageName on a CultureInfo (or string) carrier ->
//     the primary-segment ISO icall (receiver-first instance call)
//   * get_DisplayName -> LOUD (no culture display-name table)
// The PEs are synthesized with System.Reflection.Metadata's own writer (the
// pipeline shared with MethodMapTests.Regex/IO). Raw signature bytes:
// header 0x00, param count, return byte (+ coded index for CLASS 0x12
// returns / params), then one byte per parameter (0x0E=string).
public class MethodMapCultureInfoTests
{
    [Fact]
    public void Resolve_CultureInfo_GetCultureInfo_MapsToNameCarrierIcall()
    {
        // static CultureInfo GetCultureInfo(string): CLASS return (0x12 +
        // coded index), 1 string param.
        var resolved = ResolveMember(Synthesize("System.Globalization", "CultureInfo", "GetCultureInfo",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetCultureInfo");

        Assert.Equal("il2cpp::runtime::il2cpp_culture_get_culture_info", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.False(resolved.IsInstance);
        Assert.Equal("System.Globalization.CultureInfo", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_CultureInfo_GetCultureInfo_2Args_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Globalization", "CultureInfo", "GetCultureInfo",
                new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x0E }), "GetCultureInfo"));

        Assert.Contains("CultureInfo.GetCultureInfo with 2 param(s)", ex.Message);
    }

    [Fact]
    public void Resolve_CultureInfo_GetTwoLetter_OnCultureInfoReceiver_MapsToIcall()
    {
        // instance string TwoLetterISOLanguageName -- receiver-first on the
        // CultureInfo carrier (the receiver hint routes it; a hint-less
        // first pass stays a deferral).
        var resolved = ResolveMember(Synthesize("System.Globalization", "CultureInfo",
            "get_TwoLetterISOLanguageName", new byte[] { 0x00, 0x00, 0x0E }),
            "get_TwoLetterISOLanguageName", receiverType: "System.Globalization.CultureInfo");

        Assert.Equal("il2cpp::runtime::il2cpp_culture_get_two_letter_from_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.IsInstance);
    }

    [Fact]
    public void Resolve_CultureInfo_GetDisplayName_ThrowsLoud()
    {
        // No culture display-name table on the runtime surface -- explicit
        // LOUD instead of a phantom.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Globalization", "CultureInfo", "get_DisplayName",
                new byte[] { 0x00, 0x00, 0x0E }), "get_DisplayName"));

        Assert.Contains("DisplayName", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as MethodMapTests.IO)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null);
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