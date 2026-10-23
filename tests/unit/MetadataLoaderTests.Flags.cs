using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// flagsenum: the REAL [System.Flags] attribute bit.
//
// The runtime used to GUESS flags-capability from the member shape (zero
// member + every non-zero member an exact power of two). That guess silently
// printed numeric for the extremely common `[Flags] enum { None=0, A=1, B=2,
// C=4, AB=3 }` shape (the composite member disqualified the whole enum) and
// wrongly decomposed a plain `{Zero=0, One=1, Two=2}` enum.
//
// The fix: MetadataLoader decodes System.FlagsAttribute from the TypeDef's
// CustomAttribute rows (HasFlagsAttribute), TypeDef.IsFlags carries it, and
// the enum register block emits it as Il2CppEnumInfo::isFlags.
//
// Fixtures (real csc/net48 exes, same CaseExePath convention as the rest of
// the unit suite):
//   flagsenum1 -- [Flags] WITH composite members (None/A/B/C/AB=3/All=7)
//   flagsenum2 -- [Flags] canonical power-of-two members only
//   flagsenum3 -- PLAIN enum with exactly the flags SHAPE (Zero/One/Two)
//------------------------------------------------------------------------------
public class MetadataLoaderFlagsTests
{
    [Fact]
    public void FlagsEnum_WithCompositeMembers_IsFlagsTrue()
    {
        var types = MetadataLoader.LoadAssembly(CaseExePath("flagsenum1"));
        var f = types.Single(t => t.Name == "F" && t.BaseTypeName == "System.Enum");

        Assert.True(f.IsFlags);
        // The member table still carries the composite members verbatim.
        Assert.True(MethodRefResolver.TryGetUserEnumRegisterInfo(f, out var info));
        Assert.True(info.IsFlags);
        Assert.Equal(new[] { "None", "A", "B", "C", "AB", "All" }, info.Names);
        Assert.Equal(new long[] { 0, 1, 2, 4, 3, 7 }, info.Values);
    }

    [Fact]
    public void FlagsEnum_CanonicalShape_IsFlagsTrue()
    {
        var types = MetadataLoader.LoadAssembly(CaseExePath("flagsenum2"));
        var g = types.Single(t => t.Name == "G" && t.BaseTypeName == "System.Enum");

        Assert.True(g.IsFlags);
        Assert.True(MethodRefResolver.TryGetUserEnumRegisterInfo(g, out var info));
        Assert.True(info.IsFlags);
        Assert.Equal(new long[] { 0, 1, 2, 4, 8 }, info.Values);
    }

    [Fact]
    public void PlainEnum_WithFlagsShape_IsFlagsFalse()
    {
        var types = MetadataLoader.LoadAssembly(CaseExePath("flagsenum3"));
        var p = types.Single(t => t.Name == "P" && t.BaseTypeName == "System.Enum");

        // net48: [Flags] is an attribute, not a shape -- {Zero, One, Two}
        // must NOT be treated as a flags enum (its ToString of 3 is "3").
        Assert.False(p.IsFlags);
        Assert.True(MethodRefResolver.TryGetUserEnumRegisterInfo(p, out var info));
        Assert.False(info.IsFlags);
    }

    //--------------------------------------------------------------------------
    // The emitted register block carries the attribute bit in the LAST
    // column -- the runtime's only decomposition authority.
    //--------------------------------------------------------------------------
    [Fact]
    public void RegisterBlock_EmitsAttributeBitFromTypeDef()
    {
        var types = MetadataLoader.LoadAssembly(CaseExePath("flagsenum1"));
        var f = types.Single(t => t.Name == "F" && t.BaseTypeName == "System.Enum");
        Assert.True(MethodRefResolver.TryGetUserEnumRegisterInfo(f, out var info));
        var block = MethodRefResolver.EmitEnumRegisterBlock(
            info.TypeName, info.UnderlyingTypeCode, info.Names, info.Values, info.IsFlags);
        Assert.Contains(", 1 };", block); // isFlags column == 1
        Assert.Contains("il2cpp::runtime::il2cpp_enum_register(", block);
    }

    private static string CaseExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"fixture {name}.exe not found; build the case first (dotnet build tests/cases/{name}/{name}.csproj -c Release)");
    }
}
