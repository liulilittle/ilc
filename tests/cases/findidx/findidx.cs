//------------------------------------------------------------------------------
// findidx.cs -- System.Array.FindIndex<T> DEEP e2e reference case (net48
// oracle, byte-for-byte).
//
// Exercises the DELEGATE form of Array.FindIndex (rtfind-b23 + CG7 wiring at
// HEAD 8572fa6) mapped to the runtime delegate-callback icall
//   icall_array_find_index(Il2CppArray* arr, Il2CppObject* predicate,
//                          int32_t startIndex, int32_t count)
// (il2cpp_array_ext2.h:145 -- the linq where_i4 callback pattern).
//
// ARG-ORDER (verified, CG7 IcallArgOrder CodeGenerator.cs:4631): the .NET
// 4-arg overload pushes (array, startIndex, count, match) while the icall
// takes (arr, predicate, start, count); the emitter's order table maps
// slot->IL-arg as {0,3,1,2}. THIS CASE USES THE 4-ARG FORM ONLY -- the 2-arg
// /3-arg net48 overloads (FindIndex(array, match) / FindIndex(array, start,
// match)) are NOT folded by the MethodMap (their IL pushes 2/3 args against a
// ParamCount=4 declaration -> StackFold "pops 4 args but the stack has only
// 0/2" transpile abort; recorded as D-findidx-1). The scenarios 命中 / 未命中
// / start+count 窗口 / 越界 / null are all expressed through the 4-arg form
// (the full-window form FindIndex(arr, 0, arr.Length, match) covers the 2-arg
// semantics).
//
// net48 semantics (reference source Array.cs): validation order is array null
// -> ArgumentNullException, startIndex <0 or >Length -> AOO, count <0 or
// >Length-start -> AOO, match null -> ArgumentNullException; returns the
// FIRST index in [startIndex, startIndex+count) whose element satisfies the
// predicate, or -1. startIndex==Length with count==0 is a legal empty window.
//
// EXPECTED BUILD DEFECT (recorded, not fixed): the delegate construction
// `newobj System.Predicate<int>` is NOT folded to il2cpp_delegate_new --
// StackFold.IsDelegateType lists Func`/Action`/ThreadStart/WaitCallback only,
// not Predicate`1 -- so the generated code references the non-existent
// symbols System_Predicate_1_int_class / System_Predicate_1_int__ctor_<hash>
// and the cmake build aborts (D-findidx-2). The net48 oracle is captured
// regardless; the transpiled diff is unavailable until D-findidx-2 lands.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class FindIdx {
    private static bool IsTwenty(int x) { return x == 20; }
    private static bool IsOdd(int x) { return (x & 1) == 1; }
    private static bool AlwaysTrue(int x) { return true; }

    public static void Main() {
        int[] a = { 10, 20, 30, 20, 40 };
        int[] c = { 4, 6, 9, 8 };
        int[] b = { 1, 3, 5 };
        int[] empty = new int[0];

        // ===== 1. hit / miss (full-window 4-arg form) =====
        Console.WriteLine("hit=" + Array.FindIndex(a, 0, a.Length, IsTwenty));        // 1
        Console.WriteLine("hit_odd=" + Array.FindIndex(c, 0, c.Length, IsOdd));       // 2
        Console.WriteLine("miss=" + Array.FindIndex(b, 0, b.Length, IsTwenty));       // -1
        Console.WriteLine("alltrue=" + Array.FindIndex(a, 0, a.Length, AlwaysTrue));  // 0
        Console.WriteLine("empty=" + Array.FindIndex(empty, 0, 0, IsTwenty));         // -1

        // ===== 2. startIndex window (start, Length-start) =====
        Console.WriteLine("win_start=" + Array.FindIndex(a, 2, a.Length - 2, IsTwenty));     // 3
        Console.WriteLine("win_start_miss=" + Array.FindIndex(a, 4, a.Length - 4, IsTwenty)); // -1
        Console.WriteLine("win_start_odd=" + Array.FindIndex(c, 1, c.Length - 1, IsOdd));    // 2

        // ===== 3. startIndex + count window =====
        Console.WriteLine("win_sc=" + Array.FindIndex(a, 2, 2, IsTwenty));                  // 3
        Console.WriteLine("win_sc_miss=" + Array.FindIndex(a, 2, 1, IsTwenty));             // -1
        Console.WriteLine("win_zero_at_len=" + Array.FindIndex(a, 5, 0, IsTwenty));         // -1

        // ===== 4. bounds (ArgumentOutOfRangeException) =====
        bool b1 = false;
        try { Array.FindIndex(a, -1, 1, IsTwenty); } catch (ArgumentOutOfRangeException) { b1 = true; }
        Console.WriteLine("aoo_start_neg=" + b1);
        bool b2 = false;
        try { Array.FindIndex(a, 6, 0, IsTwenty); } catch (ArgumentOutOfRangeException) { b2 = true; }
        Console.WriteLine("aoo_start_hi=" + b2);
        bool b3 = false;
        try { Array.FindIndex(a, 1, -1, IsTwenty); } catch (ArgumentOutOfRangeException) { b3 = true; }
        Console.WriteLine("aoo_count_neg=" + b3);
        bool b4 = false;
        try { Array.FindIndex(a, 1, 5, IsTwenty); } catch (ArgumentOutOfRangeException) { b4 = true; }
        Console.WriteLine("aoo_count_hi=" + b4);

        // ===== 5. null (ArgumentNullException) =====
        bool n1 = false;
        try { Array.FindIndex((int[])null, 0, 0, IsTwenty); } catch (ArgumentNullException) { n1 = true; }
        Console.WriteLine("ane_arr=" + n1);
        bool n2 = false;
        try { Array.FindIndex(a, 0, a.Length, (Predicate<int>)null); } catch (ArgumentNullException) { n2 = true; }
        Console.WriteLine("ane_match=" + n2);
    }
}}
