using System;
using System.Collections.Generic;

namespace Cases
{
    public static class Probe3
    {
        static void L(string n, Func<object> f)
        {
            try { Console.WriteLine(n + "=" + (f() ?? "null")); }
            catch (Exception e) { Console.WriteLine(n + "!" + e.GetType().Name); }
        }

        public static void Main()
        {
            L("cmp_i8", () => 3L.CompareTo(5L));
            L("cmp_i8b", () => 7L.CompareTo(5L));
            L("cmp_d", () => 1.5.CompareTo(1.5));
            L("cmp_dnan", () => double.NaN.CompareTo(1.0));
            L("cmp_dnan2", () => 1.0.CompareTo(double.NaN));
            L("cmp_f", () => 2.5f.CompareTo(2.5f));
            L("cmp_u64", () => 3UL.CompareTo(5UL));
            L("cmp_obj", () => ((IComparable)3L).CompareTo((object)5L));
            L("cmp_ss_long", () => { var ss = new SortedSet<long>(Comparer<long>.Create((x, y) => y.CompareTo(x))); ss.Add(3L); ss.Add(5L); return ss.Count; });

            L("ng_sort_pairs3", () => { Array k = new[] { "c", "a", "b" }; Array v = new[] { 3, 1, 2 }; Array.Sort(k, v, StringComparer.Ordinal); return string.Join(",", (string[])k) + "|" + string.Join(",", (int[])v); });
            L("g_sort_pairs3", () => { var k = new[] { "c", "a", "b" }; var v = new[] { 3, 1, 2 }; Array.Sort(k, v, StringComparer.Ordinal); return string.Join(",", k) + "|" + string.Join(",", v); });
            L("ng_sort_pairs_cmp3", () => { Array k = new[] { "c", "a", "b" }; Array v = new[] { 3, 1, 2 }; Array.Sort(k, v, (System.Collections.IComparer)StringComparer.Ordinal); return string.Join(",", (string[])k) + "|" + string.Join(",", (int[])v); });
            L("ng_sort_range_cmp4", () => { var a = new[] { 5, 4, 3, 2, 1 }; Array.Sort(a, 1, 3, (System.Collections.IComparer)Comparer<int>.Create((x, y) => y.CompareTo(x))); return string.Join(",", a); });
            L("ng_sort_pairs4", () => { Array k = new[] { "c", "a", "b", "d" }; Array v = new[] { 3, 1, 2, 4 }; Array.Sort(k, v, 0, 3); return string.Join(",", (string[])k) + "|" + string.Join(",", (int[])v); });

            L("bs2_dup", () => Array.BinarySearch(new[] { 1, 2, 2, 3 }, 2));
            L("bs4_dup", () => Array.BinarySearch(new[] { 1, 2, 2, 3 }, 0, 4, 2));
            L("bs5_dup", () => Array.BinarySearch(new[] { 1, 2, 2, 3 }, 0, 4, 2, Comparer<int>.Default));

            L("lio_ng_underflow", () => { Array a = new[] { 3, 1, 2 }; return Array.LastIndexOf(a, (object)1, 2, 5); });
            L("lio_g_underflow", () => Array.LastIndexOf(new[] { 3, 1, 2 }, 1, 2, 5));
            L("lio_ng_negcount", () => { Array a = new[] { 3, 1, 2 }; return Array.LastIndexOf(a, (object)1, 1, -1); });
            L("lio_g_negcount", () => Array.LastIndexOf(new[] { 3, 1, 2 }, 1, 1, -1));
            L("lio_empty", () => Array.LastIndexOf(new int[0], 5, 0));
            L("lio_empty4", () => Array.LastIndexOf(new int[0], 5, 0, 0));

            L("f_to_s", () => (1f / 3f).ToString());
            L("d_to_s", () => (1.0 / 3.0).ToString());
        }
    }
}
