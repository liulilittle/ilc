using System;
using System.Collections.Generic;

//------------------------------------------------------------------------------
// p-nb9-usercmp -- nb9 H-B3/H-B4 acceptance probe.
//
// The USER-REPORTED crash shape: Array.Sort<T>(T[], IComparer<T>) was routed
// to the Comparison<T> delegate entry, so the IComparer object was invoked as
// a delegate -> function pointer read from a garbage field -> 0xC0000005.
//
// Every line is a deterministic net48 record. Covers:
//   * Array.Sort<int>(int[], IComparer<int>)                      descending
//   * Array.Sort<string>(string[], IComparer<string>)             custom
//   * Array.Sort<int>(int[], Comparison<int>)                     non-capturing
//   * Array.Sort<int>(int[], Comparison<int>)                     capturing
//   * Array.Sort(string[]) + StringComparer.Ordinal / IgnoreCase  ordinal path
//   * Array.BinarySearch<int>(int[], int, IComparer<int>)         H-B4
//------------------------------------------------------------------------------
namespace Cases
{
    public sealed class RevCmp : IComparer<int>
    {
        public int Compare(int a, int b) { return b.CompareTo(a); }
    }

    public sealed class RevStrCmp : IComparer<string>
    {
        public int Compare(string a, string b) { return string.CompareOrdinal(b, a); }
    }

    public sealed class OffsetCmp : IComparer<int>
    {
        private readonly int _k;
        public OffsetCmp(int k) { _k = k; }
        public int Compare(int a, int b) { return a.CompareTo(b) * _k; }
    }

    public static class PNb9UserCmp
    {
        public static void Main()
        {
            // --- the reported crash ---
            var arr = new[] { 1, 3, 2, 5, 4 };
            Array.Sort(arr, new RevCmp());
            Console.WriteLine("[i4rev]=" + string.Join(",", arr));

            // comparer held in a typed local (IComparer<int> static type)
            IComparer<int> cmp = new RevCmp();
            var arr2 = new[] { 9, 1, 4 };
            Array.Sort(arr2, cmp);
            Console.WriteLine("[i4rev_local]=" + string.Join(",", arr2));

            // --- string IComparer<string> ---
            var sarr = new[] { "b", "d", "a", "c" };
            Array.Sort(sarr, new RevStrCmp());
            Console.WriteLine("[strrev]=" + string.Join(",", sarr));

            // comparer with state (reference field)
            var arr3 = new[] { 3, -1, 2, 0 };
            Array.Sort(arr3, new OffsetCmp(-1));
            Console.WriteLine("[i4state]=" + string.Join(",", arr3));

            // --- Comparison<int>: non-capturing lambda ---
            var c1 = new[] { 4, 1, 3, 2 };
            Array.Sort(c1, (a, b) => a.CompareTo(b));
            Console.WriteLine("[cmp_nocap]=" + string.Join(",", c1));

            // --- Comparison<int>: capturing lambda ---
            int sign = -1;
            var c2 = new[] { 4, 1, 3, 2 };
            Array.Sort(c2, (a, b) => sign * a.CompareTo(b));
            Console.WriteLine("[cmp_cap]=" + string.Join(",", c2));

            // --- StringComparer ordinal path (must stay on icomparer) ---
            // Tie-free inputs: equal-under-ignore-case keys would leave the
            // (unspecified) tie order to the sort implementation.
            var o1 = new[] { "gamma", "Alpha", "beta", "Delta" };
            Array.Sort(o1, StringComparer.Ordinal);
            Console.WriteLine("[ord]=" + string.Join(",", o1));
            var o2 = new[] { "gamma", "Alpha", "beta", "Delta" };
            Array.Sort(o2, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("[ordic]=" + string.Join(",", o2));

            // --- BinarySearch<T>(T[], T, IComparer<T>) ---
            var sorted = new[] { 9, 7, 5, 3, 1 }; // descending under RevCmp
            Console.WriteLine("[bs_hit]=" + Array.BinarySearch(sorted, 5, new RevCmp()));
            Console.WriteLine("[bs_miss]=" + Array.BinarySearch(sorted, 6, new RevCmp()));
            Console.WriteLine("[bs_first]=" + Array.BinarySearch(sorted, 9, new RevCmp()));
            Console.WriteLine("[bs_last]=" + Array.BinarySearch(sorted, 1, new RevCmp()));
        }
    }
}
