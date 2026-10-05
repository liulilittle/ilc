//------------------------------------------------------------------------------
// baudit1 -- G6 container-audit probe B1: Array.BinarySearch duplicate index.
//
// Audit finding: the runtime BinarySearch (arrayext2.cpp icall_array_binary_search_int32)
// is a lower_bound scan and returns the FIRST duplicate index. net48
// (ArraySortHelper.InternalBinarySearch, referencesource) probes the midpoint and
// returns it on the first equality hit (`if (order == 0) return i;`), which for
// duplicates is "one of the occurrences, not necessarily the first".
//
// Expected diff: net48 prints bs1=2, bs2=2; the runtime lower_bound prints
// bs1=1, bs2=1.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Baudit1 {
    public static void Main()
    {
        int[] a = { 0, 1, 1, 1, 2 };
        Console.WriteLine("bs1=" + Array.BinarySearch(a, 1));
        int[] b = { 1, 2, 2, 2, 2, 3 };
        Console.WriteLine("bs2=" + Array.BinarySearch(b, 2));
    }
} }
