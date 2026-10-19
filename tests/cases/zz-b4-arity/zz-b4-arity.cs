using System;
using System.Collections.Generic;
using System.Text;

//------------------------------------------------------------------------------
// zz-b4-arity -- nb9 P0 arity mispair: the IndexOf/LastIndexOf/BinarySearch
// higher arities must pass (array, value, startIndex[, count]) and
// (array, index, length, value[, comparer]) in the REAL parameter order.
// Before the fix the mapping collapsed every arity onto the 2-argument entry
// (e.g. BinarySearch(a,0,5,3) -> icall_array_binary_search_int32(5,3) -> AV).
//------------------------------------------------------------------------------
namespace Cases
{
    public sealed class RevInt : IComparer<int>
    {
        public int Compare(int a, int b) { return b.CompareTo(a); }
    }

    public static class ZZB4Arity
    {
        static string J(int[] xs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xs.Length; ++i) { if (i > 0) sb.Append(","); sb.Append(xs[i]); }
            return sb.ToString();
        }

        static string P(Func<int> f)
        {
            try { return f().ToString(); }
            catch (Exception e) { return e.GetType().Name; }
        }

        public static void Main()
        {
            int[] asc = { 1, 3, 5, 7, 9 };
            Console.WriteLine("[bs_2]=" + P(() => Array.BinarySearch(asc, 5)));
            Console.WriteLine("[bs_3cmp]=" + P(() => Array.BinarySearch(asc, 5, Comparer<int>.Default)));
            Console.WriteLine("[bs_4]=" + P(() => Array.BinarySearch(asc, 0, 5, 7)));
            Console.WriteLine("[bs_4_miss]=" + P(() => Array.BinarySearch(asc, 0, 5, 4)));
            Console.WriteLine("[bs_5cmp]=" + P(() => Array.BinarySearch(asc, 0, 5, 7, Comparer<int>.Default)));
            Console.WriteLine("[bs_5cmp_miss]=" + P(() => Array.BinarySearch(asc, 0, 5, 4, Comparer<int>.Default)));

            int[] dup = { 1, 2, 2, 3, 2, 4 };
            Console.WriteLine("[io_2]=" + P(() => Array.IndexOf(dup, 2)));
            Console.WriteLine("[io_3]=" + P(() => Array.IndexOf(dup, 2, 2)));
            Console.WriteLine("[io_4]=" + P(() => Array.IndexOf(dup, 2, 2, 3)));
            Console.WriteLine("[io_4_miss]=" + P(() => Array.IndexOf(dup, 9, 0, 6)));
            Console.WriteLine("[lio_2]=" + P(() => Array.LastIndexOf(dup, 2)));
            Console.WriteLine("[lio_3]=" + P(() => Array.LastIndexOf(dup, 2, 3)));
            Console.WriteLine("[lio_4]=" + P(() => Array.LastIndexOf(dup, 2, 3, 2)));

            int[] r = { 5, 4, 3, 2, 1 };
            Array.Sort(r, 1, 3);
            Console.WriteLine("[sort_3]=" + J(r));
            int[] r4 = { 5, 4, 3, 2, 1 };
            Array.Sort(r4, 1, 3, new RevInt());
            Console.WriteLine("[sort_4cmp]=" + J(r4));

            string[] ss = { "b", "a", "d", "a", "c" };
            Console.WriteLine("[io_str_3]=" + P(() => Array.IndexOf(ss, "a", 2)));
            Console.WriteLine("[lio_str_4]=" + P(() => Array.LastIndexOf(ss, "a", 3, 4)));

            // Non-generic Array.IndexOf/LastIndexOf/BinarySearch(Array, object...)
            Array na = dup;
            Array ns = ss;
            Console.WriteLine("[ng_io_2]=" + P(() => Array.IndexOf(na, 2)));
            Console.WriteLine("[ng_io_3]=" + P(() => Array.IndexOf(na, 2, 2)));
            Console.WriteLine("[ng_io_4]=" + P(() => Array.IndexOf(na, 2, 2, 3)));
            Console.WriteLine("[ng_lio_2]=" + P(() => Array.LastIndexOf(na, 2)));
            Console.WriteLine("[ng_lio_4]=" + P(() => Array.LastIndexOf(na, 2, 3, 2)));
            Console.WriteLine("[ng_bs_2]=" + P(() => Array.BinarySearch((Array)asc, 5)));
            Console.WriteLine("[ng_bs_4]=" + P(() => Array.BinarySearch((Array)asc, 0, 5, 7)));
            Console.WriteLine("[ng_bs_5]=" + P(() => Array.BinarySearch((Array)asc, 0, 5, 7, Comparer<int>.Default)));
            Console.WriteLine("[ng_io_str]=" + P(() => Array.IndexOf(ns, "a", 2)));
        }
    }
}
