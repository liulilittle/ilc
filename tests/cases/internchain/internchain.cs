using System;
namespace Cases { public static class InternChain {
    // Same literal in a helper method -- .NET interning must return the SAME
    // instance as the literal used inside Main (cross-method identity).
    static string Pick() { return "intern-chain"; }
    static string PickOther() { return "other-chain"; }

    public static void Main() {
        // 1. Same literal twice -- compile-time intern: one instance.
        Console.WriteLine(object.ReferenceEquals("intern-chain", "intern-chain"));
        // 2. Literal vs the same literal returned from a method.
        Console.WriteLine(object.ReferenceEquals("intern-chain", Pick()));
        // 3. Different literals -- distinct instances.
        Console.WriteLine(object.ReferenceEquals("intern-chain", PickOther()));
        // 4. Runtime-built copy (string.Copy) -- a FRESH instance, not pooled.
        string fresh = string.Copy("intern-chain");
        Console.WriteLine(object.ReferenceEquals(fresh, "intern-chain"));
        // 5. Runtime-built concat -- also a fresh instance.
        string built = string.Concat("intern-", "chain");
        Console.WriteLine(object.ReferenceEquals(built, "intern-chain"));
        // 6. Two separate runtime builds -- distinct instances.
        Console.WriteLine(object.ReferenceEquals(fresh, built));
        // 7. Explicit String.Intern of a runtime copy -- the pool instance.
        string pooled1 = string.Intern(fresh);
        Console.WriteLine(object.ReferenceEquals(pooled1, "intern-chain"));
        // 8. Intern of a runtime concat -- the SAME pool entry.
        string pooled2 = string.Intern(built);
        Console.WriteLine(object.ReferenceEquals(pooled2, pooled1));
        // 9. The pooled entry is the literal instance itself.
        Console.WriteLine(object.ReferenceEquals(pooled2, "intern-chain"));
        // 10. Intern is idempotent -- the same instance comes back.
        Console.WriteLine(object.ReferenceEquals(string.Intern(pooled1), pooled1));
        // 11. Intern of an already-interned literal -- the same instance.
        Console.WriteLine(object.ReferenceEquals(string.Intern("intern-chain"), pooled1));
        // 12. Intern on a MISS -- the input itself becomes the pool entry.
        string never = string.Concat("never-", "pooled");
        string pooled3 = string.Intern(never);
        Console.WriteLine(object.ReferenceEquals(pooled3, never));
        // 13. Interning did not rewrite the runtime copy -- still fresh.
        Console.WriteLine(object.ReferenceEquals(fresh, "intern-chain"));
    }
}}
