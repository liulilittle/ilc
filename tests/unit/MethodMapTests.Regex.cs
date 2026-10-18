using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Regex dispatch tests: prove the System.Text.RegularExpressions
// Regex domain (TryMapRegex in MethodMap.Regex.cs) routes each STATIC member
// to the matching regex.h entry point positionally (the runtime mirrors the
// .NET static signatures exactly -- input first, pattern second):
//
//   Regex.IsMatch(input, pattern[, options[, timeout]]) -> il2cpp_regex_is_match
//   Regex.Match(input, pattern[, options[, timeout]])   -> il2cpp_regex_match
//   Regex.Replace(input, pattern, replacement[...])     -> il2cpp_regex_replace
//   Regex.Split(input, pattern[, options[, timeout]])   -> il2cpp_regex_split
//
// String slots carry the "s" cast (Il2CppString* -- the IR holds strings as
// Il2CppObject*), the RegexOptions (i4) slot the "i" cast, and the trailing
// TimeSpan matchTimeout is DROPPED (the engine's step budget replaces it).
// Match/Split return string[] references (ReturnsRef + the managed
// ReturnTypeName), Replace returns a string reference.
//
// Known-unwireable overloads reject LOUDLY (NotSupportedException with the
// "{owner}::{name}" prefix -- see BclResolve's wrap):
//   * Instance forms (new Regex(pattern).IsMatch(input)) -- no Regex
//     ctor/handle object graph on the runtime surface (v2).
//   * Replace(string, string, MatchEvaluator) -- no delegate evaluator.
//
// The PEs are synthesized with System.Reflection.Metadata's own writer
// (MetadataBuilder + ManagedPEBuilder -- net8.0 shared framework, no new
// packages), the same reader pipeline the transpiler uses. Signature blobs
// are the raw bytes RawSignature consumes (0x08=i4, 0x0E=string, 0x02=bool,
// 0x01=void, 0x11=valuetype, 0x12=class, 0x1D=SZARRAY); CLASS (0x12) and
// VALUETYPE (0x11) bytes carry the TypeDefOrRef coded index of the owner
// TypeRef (row 1 -> 0x05) after them, exactly as RawSignature reads them;
// SZARRAY (0x1D) returns carry the element byte (0x0E = string).
public class MethodMapRegexTests
{
    //--------------------------------------------------------------------------
    // IsMatch -- static, int32 result, string[]-free
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_IsMatch_MapsToIsMatchIcall()
    {
        // static bool IsMatch(string, string): bool ret, 2 string params.
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "IsMatch",
            new byte[] { 0x00, 0x02, 0x02, 0x0E, 0x0E }), "IsMatch");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_is_match", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Regex_IsMatchOptions_MapsWithOptionsCast()
    {
        // static bool IsMatch(string, string, RegexOptions): RegexOptions is
        // an ENUM -- the real Roslyn (csc) signature encodes it as a
        // VALUETYPE (0x11 + the coded index of RegexOptions, 0x05), NOT a
        // bare i4. The resolver accepts both shapes (unit-synthesized 0x08
        // kept for back-compat); the cast stays "i" (the enum value travels
        // as an int32 carrier).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "IsMatch",
            new byte[] { 0x00, 0x03, 0x02, 0x0E, 0x0E, 0x11, 0x05 }), "IsMatch");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_is_match", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Regex_IsMatchOptions_SynthI4FormStillResolves()
    {
        // The unit-synthesized i4 (0x08) blob remains accepted (back-compat
        // with older synthetic-PE tests).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "IsMatch",
            new byte[] { 0x00, 0x03, 0x02, 0x0E, 0x0E, 0x08 }), "IsMatch");

        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Regex_IsMatchTimeout_DropsTheTimeoutArg()
    {
        // static bool IsMatch(string, string, RegexOptions, TimeSpan):
        // RegexOptions is a VALUETYPE (0x11 + coded index), the trailing
        // TimeSpan is also a valuetype (dropped -- the step budget replaces
        // the timeout).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "IsMatch",
            new byte[] { 0x00, 0x04, 0x02, 0x0E, 0x0E, 0x11, 0x05, 0x11, 0x05 }), "IsMatch");

        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s", "i", "drop" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Match -- static, string[] reference result
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_Match_MapsToMatchIcall()
    {
        // static Match Match(string, string): ret CLASS (0x12 + coded index).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Match",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x0E }), "Match");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_match", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
        Assert.Equal("System.Text.RegularExpressions.Match", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Match object model (nb5 OBJ-1) -- property + indexer arms
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_RegexObj_GroupSuccess_MapsToGroupIcall()
    {
        // bool Group.Success (declared on Group).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Group", "get_Success",
            new byte[] { 0x00, 0x00, 0x02 }), "get_Success");

        Assert.Equal("il2cpp::runtime::icall_regex_group_get_success", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_RegexObj_MatchGroups_MapsToGroupsIcall()
    {
        // GroupCollection Match.Groups (declared on Match).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Match", "get_Groups",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_Groups");

        Assert.Equal("il2cpp::runtime::icall_regex_match_get_groups", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Text.RegularExpressions.GroupCollection", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_RegexObj_GroupCollectionCount_MapsToCountIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "GroupCollection", "get_Count",
            new byte[] { 0x00, 0x00, 0x08 }), "get_Count");

        Assert.Equal("il2cpp::runtime::icall_regex_gc_get_count", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_RegexObj_GroupCollectionIndexer_MapsToItemIcall()
    {
        // Group GroupCollection[int index] (get_Item, i4).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "GroupCollection", "get_Item",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x08 }), "get_Item");

        Assert.Equal("il2cpp::runtime::icall_regex_gc_get_item_int", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Text.RegularExpressions.Group", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_RegexObj_CaptureIndex_RoutesByReceiverType()
    {
        // Index is declared on Capture; the tracked receiver (Match vs Group)
        // selects the object arm (regex_obj.h ABI differs per type).
        var asMatch = ResolveMemberWithReceiver(Synthesize("System.Text.RegularExpressions", "Capture", "get_Index",
            new byte[] { 0x00, 0x00, 0x08 }), "get_Index", "System.Text.RegularExpressions.Match");
        Assert.Equal("il2cpp::runtime::icall_regex_match_get_index", asMatch.CppName);

        var asGroup = ResolveMemberWithReceiver(Synthesize("System.Text.RegularExpressions", "Capture", "get_Index",
            new byte[] { 0x00, 0x00, 0x08 }), "get_Index", "System.Text.RegularExpressions.Group");
        Assert.Equal("il2cpp::runtime::icall_regex_group_get_index", asGroup.CppName);
    }

    //--------------------------------------------------------------------------
    // MatchCollection (nb6 OBJ-2) -- Regex.Matches result
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_Matches_MapsToMatchesIcall()
    {
        // static MatchCollection Matches(string, string): ret CLASS (0x12).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Matches",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x0E }), "Matches");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_matches", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
        Assert.Equal("System.Text.RegularExpressions.MatchCollection", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_RegexObj_MatchCollectionCount_MapsToCountIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "MatchCollection", "get_Count",
            new byte[] { 0x00, 0x00, 0x08 }), "get_Count");

        Assert.Equal("il2cpp::runtime::icall_regex_mc_get_count", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_RegexObj_MatchCollectionIndexer_MapsToItemIcall()
    {
        // Match MatchCollection[int index] (get_Item, i4) -> Match.
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "MatchCollection", "get_Item",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x08 }), "get_Item");

        Assert.Equal("il2cpp::runtime::icall_regex_mc_get_item_int", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.Text.RegularExpressions.Match", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_RegexObj_MatchCollectionGetEnumerator_MapsToEnumeratorIcall()
    {
        // IEnumerator MatchCollection.GetEnumerator() -> the snapshot
        // enumerator pseudo-type (foreach routing).
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "MatchCollection", "GetEnumerator",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetEnumerator");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_mc_get_enumerator", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Collections.SortedList.Enumerator", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_RegexObj_GroupCollectionStringIndexer_MapsToItemStrIcall()
    {
        // Group GroupCollection[string name] (get_Item, string) -> Group.
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "GroupCollection", "get_Item",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "get_Item");

        Assert.Equal("il2cpp::runtime::icall_regex_gc_get_item_str", resolved.CppName);
        Assert.Equal(new[] { "", "s" }, resolved.ParamTypeNames);
        Assert.Equal("System.Text.RegularExpressions.Group", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Replace -- static, string reference result
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_Replace_MapsToReplaceIcall()
    {
        // static string Replace(string, string, string): string ret, 3 params.
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Replace",
            new byte[] { 0x00, 0x03, 0x0E, 0x0E, 0x0E, 0x0E }), "Replace");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_replace", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s", "s" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Regex_ReplaceOptionsTimeout_MapsWithDrop()
    {
        // static string Replace(string, string, string, RegexOptions,
        // TimeSpan): RegexOptions (0x11 + coded index) carries the "i" cast;
        // the trailing TimeSpan (0x11 + coded index) is dropped.
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Replace",
            new byte[] { 0x00, 0x05, 0x0E, 0x0E, 0x0E, 0x0E, 0x11, 0x05, 0x11, 0x05 }), "Replace");

        Assert.Equal(5, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s", "s", "i", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Regex_ReplaceMatchEvaluator_ThrowsLoud()
    {
        // static string Replace(string, string, MatchEvaluator): the delegate
        // is a CLASS param (0x12 + coded index) -- no evaluator support.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Replace",
                new byte[] { 0x00, 0x03, 0x0E, 0x0E, 0x0E, 0x12, 0x05 }), "Replace"));

        Assert.Contains("System.Text.RegularExpressions.Regex::Replace", ex.Message);
        Assert.Contains("MatchEvaluator", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Split -- static, string[] reference result
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_Split_MapsToSplitIcall()
    {
        // static string[] Split(string, string): ret SZARRAY (0x1D) + string
        // element byte (0x0E), 2 string params -- multi-byte hand-built blob.
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Split",
            new byte[] { 0x00, 0x02, 0x1D, 0x0E, 0x0E, 0x0E }), "Split");

        Assert.Equal("il2cpp::runtime::il2cpp_regex_split", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Instance forms -- no Regex object graph on the runtime surface
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_InstanceIsMatch_ThrowsLoud()
    {
        // instance bool IsMatch(string): 1 param.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "IsMatch",
                new byte[] { 0x00, 0x01, 0x02, 0x0E }), "IsMatch"));

        Assert.Contains("System.Text.RegularExpressions.Regex::IsMatch", ex.Message);
        Assert.Contains("instance", ex.Message);
    }

    [Fact]
    public void Resolve_Regex_InstanceReplace_ThrowsLoud()
    {
        // instance string Replace(string, string): string ret, 2 string params.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Replace",
                new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x0E }), "Replace"));

        Assert.Contains("System.Text.RegularExpressions.Regex::Replace", ex.Message);
        Assert.Contains("instance", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Out-of-scope members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Regex_UnknownMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text.RegularExpressions", "Regex", "Frobnicate", Sig(0x02, 0x0E)), "Frobnicate");

        Assert.StartsWith("System_Text_RegularExpressions_Regex_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodRefResolverDispatchTests)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static ResolvedMethod ResolveMemberWithReceiver(MemoryStream pe, string memberName, string receiverType)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
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
