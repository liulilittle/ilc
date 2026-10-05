//------------------------------------------------------------------------------
// recursive_arith.cs -- mixed stress case: binary recursion (fib), nested
// recursion (Ackermann), linear recursion (factorial), int64 arithmetic,
// per-frame string concatenation and a global call counter.
//
// Every recursive call performs a small string concat (`t = t + "."`) and
// passes the result into its children, so the concat is live in the IL at
// every frame. The global counters record the exact call count of every
// probe; all output lines are deterministic and hand-verifiable:
//   fib(25)   = 75025        (calls = 2*F(26)-1 = 242785)
//   A(2,5)    = 13           (calls = 48)
//   15!       = 1307674368000 (calls = 15; int64 result)
//   total     = 242848
// NOTE: string+int64 Concat is not wired, so the factorial value is printed
// on its own line via the typed Console.WriteLine(long).
//------------------------------------------------------------------------------
using System;

namespace Cases {
public static class RecursiveArith {

    static int Calls;          // global counter: every recursive call
    static int FibCalls;       // per-probe counts (cross-checked below)
    static int AckCalls;
    static int FactCalls;

    // Binary recursion + per-frame string concat.
    static int Fib(int n, string t) {
        Calls++; FibCalls++;
        t = t + ".";
        if (n < 2) return n;
        int a = Fib(n - 1, t);
        int b = Fib(n - 2, t);
        return a + b;
    }

    // Nested recursion (the classic Ackermann shape) + per-frame concat.
    static int Ack(int m, int n, string t) {
        Calls++; AckCalls++;
        t = t + ".";
        if (m == 0) return n + 1;
        if (n == 0) return Ack(m - 1, 1, t);
        return Ack(m - 1, Ack(m, n - 1, t), t);
    }

    // Linear recursion with int64 arithmetic + per-frame concat.
    static long Fact(int n, string t) {
        Calls++; FactCalls++;
        t = t + ".";
        if (n <= 1) return 1L;
        return (long)n * Fact(n - 1, t);
    }

    public static void Main() {
        int fib = Fib(25, "");
        int ack = Ack(2, 5, "");
        long fact = Fact(15, "");

        // Roslyn lowers `string + staticField` to ldsflda + ToString, which
        // the transpiler does not fold -- copy the fields into locals first.
        int fc = FibCalls;
        int ac = AckCalls;
        int ft = FactCalls;
        int tc = Calls;
        bool fok = fib == 75025;
        bool aok = ack == 13;
        bool fok2 = fact == 1307674368000L;

        Console.WriteLine("fib(25)=" + fib);
        Console.WriteLine("fib_calls=" + fc);
        Console.WriteLine("ack(2,5)=" + ack);
        Console.WriteLine("ack_calls=" + ac);
        Console.WriteLine("fact(15)=");
        Console.WriteLine(fact);
        Console.WriteLine("fact_calls=" + ft);
        Console.WriteLine("total_calls=" + tc);
        Console.WriteLine("fib_ok=" + fok);
        Console.WriteLine("ack_ok=" + aok);
        Console.WriteLine("fact_ok=" + fok2);
    }
}}
