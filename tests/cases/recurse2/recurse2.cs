// recurse2.cs -- deeper recursion e2e reference case (net48 baseline).
//
// Extends the recurse case (fib / sum / alloc / tail) with four NEW shapes:
//
//   1) MUTUAL recursion: IsEven <-> IsOdd, each level is TWO C++ frames.
//   2) Tail-recursion STYLE mixed with a non-tail sibling: TailSum (self
//      tail call; the transpiler's M6 tail-fold may collapse it -- the OUTPUT
//      is identical either way) next to NonTailSum (real frame per level).
//   3) Recursion with EXCEPTION UNWINDING: ThrowDeep throws at depth `n`,
//      the exception propagates up through all n frames to a caller try/catch.
//   4) A straight-line depth-5000 CASCADE (one frame per level).
//
// All depths stay below the measured net48 stack limits (Sum1ToN ~31850
// single frames) AND below the unoptimized (-O0, no CMAKE_BUILD_TYPE in the
// generated CMakeLists) MinGW C++ frame budget, verified empirically.
//
// Uses `Main()` with NO parameters: the recurse case's cmake_build defect is
// the entry.cpp caller (no-arg `Cases_Recurse_Main_<hash>();`) vs the
// Main(string[]) declaration `(Il2CppObject* arg0)` -- a no-arg Main avoids
// it entirely (documented in the batch-B report).

using System;
namespace Cases { public static class Recurse2 {
    // ---- 1) mutual recursion: two frames per level ----
    static bool IsEven(int n) { return n == 0 ? true : IsOdd(n - 1); }
    static bool IsOdd(int n) { return n == 0 ? false : IsEven(n - 1); }

    // ---- 2) tail style + non-tail sibling ----
    static long TailSum(long n, long acc) { return n == 0 ? acc : TailSum(n - 1, acc + n); }
    static long NonTailSum(long n) { return n == 0 ? 0 : n + NonTailSum(n - 1); }

    // ---- 3) exception thrown at depth n, unwound through n frames ----
    static int ThrowDeep(int n) {
        if (n == 0) throw new InvalidOperationException("deep");
        return ThrowDeep(n - 1);
    }

    // ---- 4) depth-5000 cascade ----
    static int Cascade(int n) { return n == 0 ? 0 : n + Cascade(n - 1); }

    public static void Main() {
        Console.WriteLine("even(6000)=" + IsEven(6000));       // true
        Console.WriteLine("odd(6001)=" + IsOdd(6001));         // true
        Console.WriteLine("even(6001)=" + IsEven(6001));       // false
        Console.WriteLine("odd(6000)=" + IsOdd(6000));         // false
        Console.WriteLine("tail_sum(6000)=" + TailSum(6000, 0));
        Console.WriteLine("nontail_sum(4000)=" + NonTailSum(4000));
        try { ThrowDeep(200); Console.WriteLine("no_catch"); }
        catch (InvalidOperationException) { Console.WriteLine("unwound_caught"); }
        Console.WriteLine("after_unwind=ok");
        Console.WriteLine("cascade(5000)=" + Cascade(5000));
        Console.WriteLine("cascade(1000)=" + Cascade(1000));
    }
}}
