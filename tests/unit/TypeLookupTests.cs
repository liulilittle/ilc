using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// TypeLookupTests -- the single authority for "metadata full name -> TypeDef".
//
// REGRESSION (nestvirt1/nestabs1/nestiface1/nestdeep1): metadata names nested
// types with the net48 '+' form ("Cases.Outer+Inner") while a nested TypeDef
// carries Namespace="" so FullName is the BARE short name ("Inner"). A plain
// `t.FullName == name` never matched, so:
//   * OwnerIsTranslated said "not translated" -> the callvirt fell to the
//     generated-name path and emitted a DIRECT call to the BASE implementation
//     (silent wrong value: v1=[1] instead of [2]);
//   * abstract/interface owners produced a phantom symbol with no definition
//     (cmake "'Cases_NestAbs1_B_F_...' was not declared").
//------------------------------------------------------------------------------
public class TypeLookupTests
{
    private static TypeDef Top(string ns, string name) => new() { Name = name, Namespace = ns };

    private static TypeDef Nested(string declaring, string name) => new()
    {
        Name = name,
        Namespace = "",
        IsNested = true,
        DeclaringTypeName = declaring,
    };

    [Fact]
    public void Find_TopLevel_MatchesFullName()
    {
        var types = new List<TypeDef> { Top("Cases", "AlgnBI") };
        Assert.Same(types[0], TypeLookup.Find(types, "Cases.AlgnBI"));
        Assert.Null(TypeLookup.Find(types, "AlgnBI"));
    }

    [Fact]
    public void Find_Nested_MatchesPlusForm()
    {
        var types = new List<TypeDef> { Top("Cases", "Outer"), Nested("Cases.Outer", "Inner") };
        Assert.Same(types[1], TypeLookup.Find(types, "Cases.Outer+Inner"));
    }

    [Fact]
    public void Find_Nested_AlsoMatchesBareShortName()
    {
        // The historical bare-name channel (metadata GetFullName on a nested
        // TypeDefinitionHandle in some pipelines) must keep working.
        var types = new List<TypeDef> { Nested("Cases.Outer", "Inner") };
        Assert.Same(types[0], TypeLookup.Find(types, "Inner"));
    }

    [Fact]
    public void Find_ExactFullNameWinsOverPlusForm()
    {
        // Two passes: an exact FullName hit always beats a '+' form hit, even
        // when the '+' match sits EARLIER in the list (the historical bare-name
        // precedence for top-level types is preserved).
        var nested = Nested("Cases.Outer", "Inner");
        var top = Top("", "Cases.Outer+Inner");
        var types = new List<TypeDef> { nested, top };
        Assert.Same(top, TypeLookup.Find(types, "Cases.Outer+Inner"));
    }

    [Fact]
    public void Find_DeeplyNested_MatchesPlusChain()
    {
        var types = new List<TypeDef> { Nested("Cases.A+B", "C") };
        Assert.Same(types[0], TypeLookup.Find(types, "Cases.A+B+C"));
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        var types = new List<TypeDef> { Top("Cases", "X") };
        Assert.Null(TypeLookup.Find(types, "Cases.Y"));
        Assert.Null(TypeLookup.Find(types, null));
        Assert.Null(TypeLookup.Find(types, ""));
    }

    [Fact]
    public void Any_And_Matches_AgreeWithFind()
    {
        var nested = Nested("Cases.Outer", "Inner");
        var types = new List<TypeDef> { nested };
        Assert.True(TypeLookup.Any(types, "Cases.Outer+Inner"));
        Assert.True(TypeLookup.Matches(nested, "Cases.Outer+Inner"));
        Assert.False(TypeLookup.Any(types, "Cases.Outer+Other"));
        Assert.False(TypeLookup.Matches(nested, "Cases.Outer+Other"));
    }
}
