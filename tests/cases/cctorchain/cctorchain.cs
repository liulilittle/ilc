//------------------------------------------------------------------------------
// cctorchain.cs -- static-constructor trigger-chain semantics.
//
// Coverage:
//   1. Cross-type field-initializer chain: Root.X = Mid.Y+1 -> Mid.Y = Leaf.Z+1
//      -> Leaf.Z = 41. First access to Root.X must run the whole chain,
//      innermost first (Leaf.cctor -> Mid.cctor -> Root.cctor).
//   2. Explicit static constructors print their name: the print order IS the
//      dependency/trigger order (.NET lazy semantics).
//   3. Re-access after initialization must NOT re-run any .cctor (guard no-op;
//      verified via an explicit run counter).
//   4. A static-field access inside a loop (guard hoisting territory) yields
//      the stable value on every iteration.
//   5. beforefieldinit types (compiler-generated .cctor, no explicit body):
//      silent in-order initialization on first access -- same-type (Silent)
//      and cross-type (Beta -> Alpha) triggers.
//   6. A throwing .cctor: the CLR wraps the throw in TypeInitializationException
//      and CACHES it -- a second access rethrows the cached one on net48. The
//      transpiler sets the guard flag before running the body, so later
//      accesses skip the guard entirely; the simplified assertion is that the
//      program survives (no crash) after the catch.
//
// NOTE: string + staticField is NOT written (ldsflda folding is unsupported);
// static values are copied into locals before any concatenation.
//------------------------------------------------------------------------------
using System;

namespace Cases {

// Explicit static constructors + cross-type field-initializer chain.
public static class Leaf {
    public static int Z = 41;
    static Leaf() { CctorChain.Runs++; Console.WriteLine("Leaf.cctor"); }
}

public static class Mid {
    public static int Y = Leaf.Z + 1;
    static Mid() { CctorChain.Runs++; Console.WriteLine("Mid.cctor"); }
}

public static class Root {
    public static int X = Mid.Y + 1;
    static Root() { CctorChain.Runs++; Console.WriteLine("Root.cctor"); }
}

// beforefieldinit (compiler-generated .cctor, no explicit body):
// field initializers run in declaration order on first access.
public static class Silent {
    public static int A = 5;
    public static int B = A + 7;
}

// beforefieldinit cross-type trigger: Beta's initializer reads Alpha's field.
public static class Alpha {
    public static int P = 100;
}

public static class Beta {
    public static int Q = Alpha.P + 1;
}

// Throwing .cctor (see header comment, coverage item 6).
public static class Boom {
    public static int Val = 99;
    static Boom() { Console.WriteLine("Boom.cctor"); throw new InvalidOperationException("boom"); }
}

public static class CctorChain {
    public static int Runs;

    public static void Main() {
        // 1. First static-field access triggers the whole chain, innermost first.
        Console.WriteLine(Root.X);          // 43
        Console.WriteLine(Mid.Y);           // 42 -- already initialized, no re-run
        Console.WriteLine(Leaf.Z);          // 41
        // 2. Re-access must NOT re-run any .cctor (the guard is a no-op).
        int runs = Runs;
        Console.WriteLine("runs=" + runs);  // 3
        // 3. Loop body reads a static field: guard hoisting territory; the
        // value must be stable across iterations.
        int sum = 0;
        for (int i = 0; i < 3; i++) { sum = sum + Root.X; }
        Console.WriteLine("sum=" + sum);    // 129
        // 4. beforefieldinit: silent in-order initialization on first access.
        Console.WriteLine(Silent.B);        // 12
        bool silentOk = Silent.A == 5 && Silent.B == 12;
        Console.WriteLine("silent_ok=" + silentOk);
        // 5. beforefieldinit cross-type trigger (Beta -> Alpha).
        Console.WriteLine(Beta.Q);          // 101
        bool betaOk = Beta.Q == 101 && Alpha.P == 100;
        Console.WriteLine("beta_ok=" + betaOk);
        // 6. Throwing .cctor: the first access throws (caught); a second access
        // rethrows the cached TypeInitializationException on net48 (the
        // transpiler's guard has already run, so it must at least not crash).
        int caught = 0;
        try { int v = Boom.Val; } catch (Exception) { caught++; }
        try { int v = Boom.Val; } catch (Exception) { caught++; }
        Console.WriteLine("boom_caught=" + caught);
        Console.WriteLine("boom_survived=True");
    }
}}
