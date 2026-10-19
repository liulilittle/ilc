using System;
using System.Collections.Generic;

namespace Cases
{
    public sealed class RevF : IComparer<float>
    {
        public int Compare(float a, float b) { return b.CompareTo(a); }
    }

    public static class Probe6
    {
        static void L(string n, Func<object> f)
        {
            try { Console.WriteLine(n + "=" + (f() ?? "null")); }
            catch (Exception e) { Console.WriteLine(n + "!" + e.GetType().Name); }
        }

        public static void Main()
        {
            L("clear_str", () => { var a = new[] { "a", "b", "c" }; Array.Clear(a, 0, 2); return (a[0] == null) + "," + (a[1] == null) + "," + (a[2] == null); });
            L("clear_long", () => { var a = new[] { 1L, 2L, 3L }; Array.Clear(a, 0, 2); return a[0] + "," + a[1] + "," + a[2]; });
            L("clear_obj", () => { var a = new object[] { "x", "y", "z" }; Array.Clear(a, 0, 2); return (a[0] == null) + "," + (a[1] == null) + "," + (a[2] == null); });
            L("clear_double", () => { var a = new[] { 1.5, 2.5 }; Array.Clear(a, 0, 2); return a[0] + "," + a[1]; });

            L("rev_ng_str", () => { Array a = new[] { "a", "b", "c" }; Array.Reverse(a); return string.Join(",", (string[])a); });
            L("rev_ng_str_range", () => { Array a = new[] { "a", "b", "c", "d" }; Array.Reverse(a, 1, 2); return string.Join(",", (string[])a); });
            L("rev_ng_long", () => { Array a = new long[] { 1, 2, 3 }; Array.Reverse(a); return string.Join(",", (long[])a); });
            L("rev_ng_long_range", () => { Array a = new long[] { 1, 2, 3, 4 }; Array.Reverse(a, 1, 2); return string.Join(",", (long[])a); });
            L("rev_g_double", () => { var a = new[] { 1.5, 2.5, 3.5 }; Array.Reverse(a); return string.Join(",", a); });

            L("sort_f_cmp2", () => { var a = new[] { 1f, 3f, 2f }; Array.Sort(a, new RevF()); return string.Join(",", a); });
            L("sort_f_cmp3", () => { var a = new[] { 5f, 4f, 3f, 2f, 1f }; Array.Sort(a, 1, 3, new RevF()); return string.Join(",", a); });
            L("sort_f_create", () => { var a = new[] { 1f, 3f, 2f }; var c = Comparer<float>.Create((x, y) => y.CompareTo(x)); Array.Sort(a, c); return string.Join(",", a); });

            L("join_f", () => string.Join(",", new[] { 1f, 2f, 3f }));
            L("read_f", () => { var a = new[] { 3f, 1f, 2f }; return a[0].ToString() + "," + a[1].ToString() + "," + a[2].ToString(); });
        }
    }
}
