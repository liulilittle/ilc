using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using System.Collections.Generic;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterTypeCppNameCacheTests -- guards the TypeCppName memo (H2,
// transpiler compile-time perf).
//
// The memo is a pure layer over the static session state _cppNameOverrides, so
// the risk it introduces is NOT "wrong math" but "stale value": a name computed
// BEFORE ResolveTypeCppNameCollisions would keep the bare (pre-collision)
// spelling and collapse two distinct C++ structs into one symbol. These tests
// pin the invalidation contract.
//------------------------------------------------------------------------------
public class TypeEmitterTypeCppNameCacheTests
{
    // "Cases.A_B" (top level) and the nested "Cases.A.B" both sanitize to
    // "Cases_A_B" -- the GN-N5 collision pair the suffix map exists for.
    private static TypeDef MakeCollidingTop() => new() { Name = "A_B", Namespace = "Cases" };

    private static TypeDef MakeCollidingNested() => new()
    {
        Name = "B", Namespace = "", IsNested = true, DeclaringTypeName = "Cases.A",
    };

    // H2 invalidation: a name memoized BEFORE the collision pass must not
    // survive it. Without the token comparison the second read returns the
    // stale bare name and two distinct C++ structs collapse into one symbol
    // (the exact bug the suffix map fixes).
    [Fact]
    public void TypeCppName_CachedBeforeCollisionPass_IsInvalidated()
    {
        var top = MakeCollidingTop();
        var nested = MakeCollidingNested();
        var types = new List<TypeDef> { top, nested };

        // Pre-pass: no overrides yet -> both report the bare name (and both
        // are now memoized under the null token).
        Assert.Equal("Cases_A_B", TypeEmitter.TypeCppName(top));
        Assert.Equal("Cases_A_B", TypeEmitter.TypeCppName(nested));

        TypeEmitter.ResolveTypeCppNameCollisions(types);

        // Ordinal identity order: nested "Cases.A+B|" sorts before top
        // "Cases.A_B|" ('+' 0x2B < '_' 0x5F), so the nested type keeps the
        // bare name and the top-level type gets the deterministic suffix.
        Assert.Equal("Cases_A_B", TypeEmitter.TypeCppName(nested));
        var renamed = TypeEmitter.TypeCppName(top);
        Assert.StartsWith("Cases_A_B_h", renamed);
        Assert.Equal("Cases_A_B_h".Length + 8, renamed.Length);

        // Idempotent: the memo returns the same post-pass value.
        Assert.Equal(renamed, TypeEmitter.TypeCppName(top));

        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }

    // H2 keying: the memo is per TypeDef REFERENCE, so two TypeDefs with
    // identical names/namespaces never share a value.
    [Fact]
    public void TypeCppName_MemoIsKeyedByTypeDefReference()
    {
        var a = new TypeDef { Name = "Same", Namespace = "Cases" };
        var b = new TypeDef { Name = "Same", Namespace = "Cases" };
        Assert.Equal("Cases_Same", TypeEmitter.TypeCppName(a));
        Assert.Equal("Cases_Same", TypeEmitter.TypeCppName(b));
        // Same identity -> the collision pass treats them as ONE name owner
        // (same identity string), so neither is renamed; the point here is
        // that the two distinct instances both resolve consistently.
        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef> { a, b });
        Assert.Equal("Cases_Same", TypeEmitter.TypeCppName(a));
        Assert.Equal("Cases_Same", TypeEmitter.TypeCppName(b));
        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }

    // The memo must not change the plain (no-collision) result either.
    [Fact]
    public void TypeCppName_NoCollision_StableAcrossRepeatedCalls()
    {
        var t = new TypeDef { Name = "Plain", Namespace = "Cases" };
        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef> { t });
        var first = TypeEmitter.TypeCppName(t);
        Assert.Equal("Cases_Plain", first);
        Assert.Equal(first, TypeEmitter.TypeCppName(t));
        TypeEmitter.ResolveTypeCppNameCollisions(new List<TypeDef>());
    }
}
