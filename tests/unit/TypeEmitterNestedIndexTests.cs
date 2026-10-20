using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using System.Collections.Generic;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterNestedIndexTests -- guards the nested-type index (H3, transpiler
// compile-time perf).
//
// EmitTypeInfo used to answer "which types are DIRECTLY nested in t?" with a
// Where() over the whole type list per type (N^2 string comparisons). The
// replacement is a Dictionary<string, TypeDef[]> built in
// ResolveTypeCppNameCollisions -- the same per-Generate pass that finalizes the
// collision map -- so its lifetime is one Generate.
//
// The risk is therefore not "wrong math" but "stale/leaked index": an index
// built for a previous type list answering for the current one. These tests
// pin the equivalence with the linear scan and the per-pass rebuild.
//------------------------------------------------------------------------------
public class TypeEmitterNestedIndexTests
{
    private static TypeDef MakeOuter() => new() { Name = "Outer", Namespace = "Cases" };

    private static TypeDef MakeNested(string shortName, string declaring = "Cases.Outer") => new()
    {
        Name = shortName, Namespace = "", IsNested = true, DeclaringTypeName = declaring,
    };

    // Equivalence: with the index in place EmitTypeInfo must emit exactly what
    // the linear scan emitted -- same entries, same order.
    [Fact]
    public void EmitTypeInfo_NestedTable_MatchesLinearScan()
    {
        var outer = MakeOuter();
        var n1 = MakeNested("First");
        var n2 = MakeNested("Second");
        var unrelated = new TypeDef { Name = "Other", Namespace = "Cases" };
        var types = new List<TypeDef> { outer, n1, n2, unrelated };

        TypeEmitter.ResolveTypeCppNameCollisions(types);
        var indexed = TypeEmitter.EmitTypeInfo(outer, allTypes: types);

        // A DIFFERENT list instance forces the fallback scan path; the output
        // must be byte-identical.
        var copy = new List<TypeDef>(types);
        var scanned = TypeEmitter.EmitTypeInfo(outer, allTypes: copy);

        Assert.Equal(scanned, indexed);
        Assert.Contains("static const char* Cases_Outer_typeinfo_nested[] = {", indexed);
        var first = indexed.IndexOf("\"Cases.Outer+First\",", System.StringComparison.Ordinal);
        var second = indexed.IndexOf("\"Cases.Outer+Second\",", System.StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, "nested entries must keep source-list order");
        // The unrelated top-level type is NOT a nested entry.
        Assert.DoesNotContain("\"Cases.Other\",", indexed.Split("typeinfo_nested")[1]);

        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }

    // Scope: only DIRECT children of t are listed -- a grandchild nested in a
    // child must not leak into t's table.
    [Fact]
    public void EmitTypeInfo_NestedTable_OnlyDirectChildren()
    {
        var outer = MakeOuter();
        var child = MakeNested("Child");
        var grandchild = MakeNested("Grand", "Cases.Outer+Child");
        var types = new List<TypeDef> { outer, child, grandchild };

        TypeEmitter.ResolveTypeCppNameCollisions(types);
        var code = TypeEmitter.EmitTypeInfo(outer, allTypes: types);
        var table = code.Split("typeinfo_nested")[1];
        Assert.Contains("\"Cases.Outer+Child\",", table);
        Assert.DoesNotContain("\"Cases.Outer+Child+Grand\",", table);

        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }

    // Lifetime: after a NEW collision pass the index describes the new list,
    // not the previous one (no cross-Generate leakage).
    [Fact]
    public void NestedIndex_IsRebuiltPerCollisionPass()
    {
        var outerA = MakeOuter();
        var childA = MakeNested("Old");
        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef> { outerA, childA });
        Assert.Contains("\"Cases.Outer+Old\",",
            TypeEmitter.EmitTypeInfo(outerA, allTypes: new List<TypeDef> { outerA, childA }));

        // Second Generate: a different outer with a different child. The index
        // must no longer answer for the first list.
        var outerB = new TypeDef { Name = "Outer", Namespace = "Other" };
        var childB = MakeNested("New", "Other.Outer");
        var second = new List<TypeDef> { outerB, childB };
        TypeEmitter.ResolveTypeCppNameCollisions(second);
        var code = TypeEmitter.EmitTypeInfo(outerB, allTypes: second);
        Assert.Contains("\"Other.Outer+New\",", code);
        Assert.DoesNotContain("\"Cases.Outer+Old\",", code);

        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }

    // A nested-free type emits NO nested table at all (zero-delta contract).
    [Fact]
    public void EmitTypeInfo_NoNestedChildren_EmitsNoNestedTable()
    {
        var outer = MakeOuter();
        var types = new List<TypeDef> { outer };
        TypeEmitter.ResolveTypeCppNameCollisions(types);
        var code = TypeEmitter.EmitTypeInfo(outer, allTypes: types);
        Assert.DoesNotContain("typeinfo_nested", code);
        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }

    // A caller-supplied list WINS over the per-Generate index. The pre-index
    // code answered from whatever list it was handed, so passing a filtered
    // list (mode B / a probe) must not resurrect children from the full
    // Generate list. Without the ReferenceEquals guard the index answers for
    // the wrong list and this test fails.
    [Fact]
    public void EmitTypeInfo_ForeignList_IsHonoredOverGenerateIndex()
    {
        var outer = MakeOuter();
        var child = MakeNested("Child");
        var full = new List<TypeDef> { outer, child };
        TypeEmitter.ResolveTypeCppNameCollisions(full);

        // Same outer, but the caller's list excludes the child.
        var filtered = new List<TypeDef> { outer };
        var code = TypeEmitter.EmitTypeInfo(outer, allTypes: filtered);
        Assert.DoesNotContain("typeinfo_nested", code);

        // The full list still reports the child (index intact for that list).
        Assert.Contains("\"Cases.Outer+Child\",",
            TypeEmitter.EmitTypeInfo(outer, allTypes: full));

        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }
}
