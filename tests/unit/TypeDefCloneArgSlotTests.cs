using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// GN-15: TypeDef.CloneArgSlotIsRef -- the per-argument VAR-slot reference
// classifier used by every declaration/body/initobj face of an arity>=2
// closed clone. An arity>=2 clone with a per-argument parts array resolves the
// EXACT argument ("[int2]_int" -> index 0 "[int2]" is a reference, index 1
// "int" is a value); arity-1 / invalid index / no parts keeps the legacy
// whole-suffix classifier (suffix != "int").
//------------------------------------------------------------------------------
public class TypeDefCloneArgSlotTests
{
    [Fact]
    public void CloneArgSlotIsRef_PerArgument_Aryity2Mixed_PartIndexWins()
    {
        // Pair<int[,],int> -> suffix "[int2]_int", parts ["[int2]","int"].
        var clone = new TypeDef
        {
            Name = "Pair`2",
            Namespace = "Cases",
            GenericArity = 2,
            InstantiationSuffix = "[int2]_int",
            InstantiationSuffixParts = new[] { "[int2]", "int" },
        };

        Assert.True(clone.CloneArgSlotIsRef(0));  // int[,] -> reference
        Assert.False(clone.CloneArgSlotIsRef(1)); // int    -> value
    }

    [Fact]
    public void CloneArgSlotIsRef_StaticOverload_PerArgumentMatchesInstance()
    {
        Assert.True(TypeDef.CloneArgSlotIsRef("[int2]_int", new[] { "[int2]", "int" }, 0));
        Assert.False(TypeDef.CloneArgSlotIsRef("[int2]_int", new[] { "[int2]", "int" }, 1));
    }

    [Fact]
    public void CloneArgSlotIsRef_SinglePartArray_FallsBackToLegacyClassifier()
    {
        // parts length 1 (< 2) -> legacy whole-suffix classifier: a non-"int"
        // suffix is a reference regardless of the index.
        var clone = new TypeDef
        {
            Name = "Box`1",
            Namespace = "Cases",
            GenericArity = 1,
            InstantiationSuffix = "[int]",
            InstantiationSuffixParts = new[] { "[int]" },
        };

        Assert.True(clone.CloneArgSlotIsRef(0));
        Assert.True(clone.CloneArgSlotIsRef(5)); // invalid index -> legacy path
    }

    [Fact]
    public void CloneArgSlotIsRef_NoParts_LegacySuffixClassifier()
    {
        var intClone = new TypeDef { Name = "Box`1", Namespace = "Cases", GenericArity = 1, InstantiationSuffix = "int" };
        var strClone = new TypeDef { Name = "Box`1", Namespace = "Cases", GenericArity = 1, InstantiationSuffix = "string" };
        var def = new TypeDef { Name = "Box`1", Namespace = "Cases", GenericArity = 1, InstantiationSuffix = null };

        Assert.False(intClone.CloneArgSlotIsRef(0));
        Assert.True(strClone.CloneArgSlotIsRef(0));
        Assert.False(def.CloneArgSlotIsRef(0));
    }

    [Fact]
    public void CloneArgSlotIsRef_InvalidIndex_FallsBackToLegacySuffix()
    {
        // arity>=2 parts but an out-of-range index keeps the legacy classifier.
        var clone = new TypeDef
        {
            Name = "Pair`2",
            Namespace = "Cases",
            GenericArity = 2,
            InstantiationSuffix = "[int2]_int",
            InstantiationSuffixParts = new[] { "[int2]", "int" },
        };

        Assert.True(clone.CloneArgSlotIsRef(7)); // invalid index -> suffix != "int" -> ref
    }
}
