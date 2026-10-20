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
// The risks are therefore not "wrong math" but (a) a stale index built for a
// previous type list answering for the current one, and (b) the same List
// mutated in place after the pass, which a pure ReferenceEquals guard would not
// notice (silently dropping children from typeinfo_nested). Both are pinned
// here.
//
// ISOLATION: writes TypeEmitter's process-wide statics directly, so this class
// is in the serialized "TypeEmitterSerial" collection (see
// TypeEmitterSerialCollection.cs) and restores the state in finally blocks.
//------------------------------------------------------------------------------
[Collection("TypeEmitterSerial")]
public class TypeEmitterNestedIndexTests
{
    private static void ResetSessionState()
        => TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());

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
        try
        {
            ResetSessionState();
            var outer = MakeOuter();
            var n1 = MakeNested("First");
            var n2 = MakeNested("Second");
            var unrelated = new TypeDef { Name = "Other", Namespace = "Cases" };
            var types = new List<TypeDef> { outer, n1, n2, unrelated };

            TypeEmitter.ResolveTypeCppNameCollisions(types);
            var indexed = TypeEmitter.EmitTypeInfo(outer, allTypes: types);

            // A DIFFERENT list instance forces the fallback scan path; the
            // output must be byte-identical.
            var copy = new List<TypeDef>(types);
            var scanned = TypeEmitter.EmitTypeInfo(outer, allTypes: copy);

            Assert.Equal(scanned, indexed);
            Assert.Contains("static const char* Cases_Outer_typeinfo_nested[] = {", indexed);
            var first = indexed.IndexOf("\"Cases.Outer+First\",", System.StringComparison.Ordinal);
            var second = indexed.IndexOf("\"Cases.Outer+Second\",", System.StringComparison.Ordinal);
            Assert.True(first >= 0 && second > first, "nested entries must keep source-list order");
            // The unrelated top-level type is NOT a nested entry.
            Assert.DoesNotContain("\"Cases.Other\",", indexed.Split("typeinfo_nested")[1]);
        }
        finally
        {
            ResetSessionState();
        }
    }

    // Scope: only DIRECT children of t are listed -- a grandchild nested in a
    // child must not leak into t's table (depth >= 2 nesting).
    [Fact]
    public void EmitTypeInfo_NestedTable_OnlyDirectChildren()
    {
        try
        {
            ResetSessionState();
            var outer = MakeOuter();
            var child = MakeNested("Child");
            var grandchild = MakeNested("Grand", "Cases.Outer+Child");
            var types = new List<TypeDef> { outer, child, grandchild };

            TypeEmitter.ResolveTypeCppNameCollisions(types);
            var code = TypeEmitter.EmitTypeInfo(outer, allTypes: types);
            var table = code.Split("typeinfo_nested")[1];
            Assert.Contains("\"Cases.Outer+Child\",", table);
            Assert.DoesNotContain("\"Cases.Outer+Child+Grand\",", table);

            // The CHILD's own table lists the grandchild (depth 2 works).
            var childCode = TypeEmitter.EmitTypeInfo(child, allTypes: types);
            Assert.Contains("\"Cases.Outer+Child+Grand\",", childCode);
        }
        finally
        {
            ResetSessionState();
        }
    }

    // Lifetime: after a NEW collision pass the index describes the new list,
    // not the previous one (no cross-Generate leakage).
    [Fact]
    public void NestedIndex_IsRebuiltPerCollisionPass()
    {
        try
        {
            ResetSessionState();
            var outerA = MakeOuter();
            var childA = MakeNested("Old");
            TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef> { outerA, childA });
            Assert.Contains("\"Cases.Outer+Old\",",
                TypeEmitter.EmitTypeInfo(outerA, allTypes: new List<TypeDef> { outerA, childA }));

            // Second Generate: a different outer with a different child. The
            // index must no longer answer for the first list.
            var outerB = new TypeDef { Name = "Outer", Namespace = "Other" };
            var childB = MakeNested("New", "Other.Outer");
            var second = new List<TypeDef> { outerB, childB };
            TypeEmitter.ResolveTypeCppNameCollisions(second);
            var code = TypeEmitter.EmitTypeInfo(outerB, allTypes: second);
            Assert.Contains("\"Other.Outer+New\",", code);
            Assert.DoesNotContain("\"Cases.Outer+Old\",", code);
        }
        finally
        {
            ResetSessionState();
        }
    }

    // A nested-free type emits NO nested table at all (zero-delta contract).
    [Fact]
    public void EmitTypeInfo_NoNestedChildren_EmitsNoNestedTable()
    {
        try
        {
            ResetSessionState();
            var outer = MakeOuter();
            var types = new List<TypeDef> { outer };
            TypeEmitter.ResolveTypeCppNameCollisions(types);
            var code = TypeEmitter.EmitTypeInfo(outer, allTypes: types);
            Assert.DoesNotContain("typeinfo_nested", code);
        }
        finally
        {
            ResetSessionState();
        }
    }

    // A caller-supplied list WINS over the per-Generate index. The pre-index
    // code answered from whatever list it was handed, so passing a filtered
    // list (mode B / a probe) must not resurrect children from the full
    // Generate list. Without the ReferenceEquals guard the index answers for
    // the wrong list and this test fails.
    [Fact]
    public void EmitTypeInfo_ForeignList_IsHonoredOverGenerateIndex()
    {
        try
        {
            ResetSessionState();
            var outer = MakeOuter();
            var child = MakeNested("Child");
            var full = new List<TypeDef> { outer, child };
            TypeEmitter.ResolveTypeCppNameCollisions(full);

            // Same outer, but the caller's list excludes the child.
            var filtered = new List<TypeDef> { outer };
            var code = TypeEmitter.EmitTypeInfo(outer, allTypes: filtered);
            Assert.DoesNotContain("typeinfo_nested", code);

            // The full list still reports the child (index intact for it).
            Assert.Contains("\"Cases.Outer+Child\",",
                TypeEmitter.EmitTypeInfo(outer, allTypes: full));
        }
        finally
        {
            ResetSessionState();
        }
    }

    // The SAME List mutated in place after the collision pass: a bare
    // ReferenceEquals guard would keep serving the stale index and silently
    // under-report children. The element-count check forces the linear-scan
    // fallback, so the newly appended nested type is still emitted.
    [Fact]
    public void EmitTypeInfo_SameListAppendedAfterPass_StillSeesNewChild()
    {
        try
        {
            ResetSessionState();
            var outer = MakeOuter();
            var first = MakeNested("First");
            var types = new List<TypeDef> { outer, first };
            TypeEmitter.ResolveTypeCppNameCollisions(types);
            Assert.DoesNotContain("\"Cases.Outer+Late\",",
                TypeEmitter.EmitTypeInfo(outer, allTypes: types));

            // Mutate the very same List instance (what a late clone append in
            // CodeGenerator.EnsureClone would do after the pass).
            types.Add(MakeNested("Late"));

            var code = TypeEmitter.EmitTypeInfo(outer, allTypes: types);
            Assert.Contains("\"Cases.Outer+First\",", code);
            Assert.Contains("\"Cases.Outer+Late\",", code);
        }
        finally
        {
            ResetSessionState();
        }
    }

    // Two sibling nested types with the SAME short name under DIFFERENT outers
    // (Ns1.A+Dup / Ns2.B+Dup) plus a generic outer: the index must keep them in
    // their own buckets and preserve source order within each.
    [Fact]
    public void NestedIndex_SameNameSiblingsUnderDifferentOuters_StaySeparate()
    {
        try
        {
            ResetSessionState();
            var a = new TypeDef { Name = "A", Namespace = "Ns1" };
            var b = new TypeDef { Name = "B", Namespace = "Ns2" };
            var dup1 = MakeNested("Dup", "Ns1.A");
            var dup2 = MakeNested("Dup", "Ns2.B");
            var genOuter = new TypeDef { Name = "Box", Namespace = "Ns3", GenericArity = 2 };
            var genInner = MakeNested("Inner", "Ns3.Box");
            var types = new List<TypeDef> { a, b, dup1, dup2, genOuter, genInner };

            TypeEmitter.ResolveTypeCppNameCollisions(types);

            var codeA = TypeEmitter.EmitTypeInfo(a, allTypes: types);
            Assert.Contains("\"Ns1.A+Dup\",", codeA);
            Assert.DoesNotContain("\"Ns2.B+Dup\",", codeA);

            var codeB = TypeEmitter.EmitTypeInfo(b, allTypes: types);
            Assert.Contains("\"Ns2.B+Dup\",", codeB);
            Assert.DoesNotContain("\"Ns1.A+Dup\",", codeB);

            var codeBox = TypeEmitter.EmitTypeInfo(genOuter, allTypes: types);
            Assert.Contains("\"Ns3.Box+Inner\",", codeBox);
        }
        finally
        {
            ResetSessionState();
        }
    }
}
