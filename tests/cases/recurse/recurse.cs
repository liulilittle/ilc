using System;

namespace Cases {
public static class Recurse {

    // 1) Two-way recursion: classic deep-call-chain correctness probe.
    //    fib(25) = 75025 (fits int32; single-digit result lines stay
    //    deterministic across runtimes).
    static int Fib(int n) { return n < 2 ? n : Fib(n - 1) + Fib(n - 2); }

    // 2) Straight-line recursion depth n: sum 1..n. Not tail-recursive
    //    (n + call), so every level really is a stack frame.
    static int Sum1ToN(int n) { return n == 0 ? 0 : n + Sum1ToN(n - 1); }

    // 3) Allocation per level: EVERY level news a small object and holds the
    //    reference across the recursive call (the local is used AFTER the
    //    call returns, so the JIT keeps it live = a GC root at every
    //    mid-recursion safe point). A full GC.Collect() at the bottom runs
    //    with all `limit` frames on the stack: any root iteration bug that
    //    drops a frame-level root lets the collector reclaim a Node and the
    //    sum mismatches (deterministic failure signal).
    //    sum = 0 + 1 + ... + (limit-1) = limit*(limit-1)/2.
    sealed class Node { public int Id; }
    static int DeepAlloc(int n, int limit) {
        if (n >= limit) { GC.Collect(); return 0; }
        Node node = new Node();
        node.Id = n;
        int rest = DeepAlloc(n + 1, limit);
        return rest + node.Id;
    }

    // 4) Tail-recursion style (accumulator): C# emits no tail. prefix and
    //    net48 JIT does not tail-call-optimize C# calls, so this is a real
    //    depth test of the same shape the ILC runtime must execute.
    static int TailSum(int n, int acc) { return n == 0 ? acc : TailSum(n - 1, acc + n); }
    static int TailFact(int n, int acc) { return n <= 1 ? acc : TailFact(n - 1, acc * n); }

    // Probe mode: args override the depths (used to measure the net48 stack
    //   limit); the default run (no args) is the deterministic reference.
    public static void Main(string[] args) {
        // Measured net48 stack limits (probe runs, default 1MB thread stack):
        //   Sum1ToN ~31850 frames, DeepAlloc 5744 frames (5743 ok / 5744 SO),
        //   TailSum no limit up to 1,000,000 (JIT tail-call-optimizes).
        // Final depths carry a safety margin below those limits.
        int sumDepth = 10000;
        int allocDepth = 4000;
        int tailDepth = 20000;
        if (args.Length >= 1) { sumDepth = int.Parse(args[0]); }
        if (args.Length >= 2) { allocDepth = int.Parse(args[1]); }
        if (args.Length >= 3) { tailDepth = int.Parse(args[2]); }

        Console.WriteLine("fib(25)=" + Fib(25));
        Console.WriteLine("sum1to" + sumDepth + "=" + Sum1ToN(sumDepth));
        Console.WriteLine("alloc_deep n=" + allocDepth + " sum=" + DeepAlloc(0, allocDepth));
        Console.WriteLine("tail_sum n=" + tailDepth + "=" + TailSum(tailDepth, 0));
        Console.WriteLine("tail_fact(12)=" + TailFact(12, 1));
    }
}}
