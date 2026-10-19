using System;
using System.Collections.Generic;
using System.Text;

//------------------------------------------------------------------------------
// zz-b4-sort5 -- round-5 D2/D3/D5/D6/D8:
//   Sort<TKey,TValue>(keys, items) / (keys, items, IComparer)
//   non-generic Sort(Array, Array) and Sort(Array)
//   Sort<object>(object[]) and its range form
//   non-generic Sort(Array, int, int)
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ZZB4Sort5
    {
        static string J(int[] xs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xs.Length; ++i) { if (i > 0) sb.Append(","); sb.Append(xs[i]); }
            return sb.ToString();
        }

        static string J(string[] xs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xs.Length; ++i) { if (i > 0) sb.Append(","); sb.Append(xs[i]); }
            return sb.ToString();
        }

        static string J(object[] xs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xs.Length; ++i) { if (i > 0) sb.Append(","); sb.Append(xs[i]); }
            return sb.ToString();
        }

        public static void Main()
        {
            int[] k = { 3, 1, 2 };
            string[] v = { "c", "a", "b" };
            Array.Sort(k, v);
            Console.WriteLine("[keys_items]=" + J(k) + "|" + J(v));

            int[] k2 = { 3, 1, 2 };
            string[] v2 = { "c", "a", "b" };
            Array.Sort(k2, v2, Comparer<int>.Create((a, b) => b.CompareTo(a)));
            Console.WriteLine("[keys_items_cmp]=" + J(k2) + "|" + J(v2));

            string[] sk = { "b", "a", "c" };
            object[] si = { 1, 2, 3 };
            Array.Sort(sk, si);
            Console.WriteLine("[keys_items_str]=" + J(sk) + "|" + J(si));

            int[] nk = { 3, 1, 2 };
            string[] nv = { "c", "a", "b" };
            Array.Sort((Array)nk, (Array)nv);
            Console.WriteLine("[ng_keys_items]=" + J(nk) + "|" + J(nv));

            string[] ns = { "b", "a", "c" };
            Array.Sort((Array)ns);
            Console.WriteLine("[ng_sort_str]=" + J(ns));

            string[] nr = { "b", "a", "c" };
            Array.Sort((Array)nr, 0, 3);
            Console.WriteLine("[ng_sort_str_range]=" + J(nr));

            // nb9 E2/E3: non-generic keys/items + comparer, range + comparer,
            // and StringComparer over string keys.
            string[] k3 = { "c", "a", "b" };
            int[] v3 = { 3, 1, 2 };
            Array.Sort((Array)k3, (Array)v3, StringComparer.Ordinal);
            Console.WriteLine("[ng_keys3_str]=" + J(k3) + "|" + J(v3));

            int[] r4 = { 5, 4, 3, 2, 1 };
            Array.Sort((Array)r4, 1, 3, Comparer<int>.Create((a, b) => b.CompareTo(a)));
            Console.WriteLine("[ng_range4_cmp]=" + J(r4));

            object[] o = { 3, 1, 2 };
            Array.Sort(o);
            Console.WriteLine("[obj_sort]=" + J(o));

            object[] o2 = { 3, 1, 2 };
            Array.Sort(o2, 0, 3);
            Console.WriteLine("[obj_sort_range]=" + J(o2));
        }
    }
}
