using System;
using System.Collections.Generic;

namespace Cases
{
    public static class Probe4
    {
        static void L(string n, Func<object> f)
        {
            try { Console.WriteLine(n + "=" + (f() ?? "null")); }
            catch (Exception e) { Console.WriteLine(n + "!" + e.GetType().Name); }
        }

        public static void Main()
        {
            L("sort_i8_cmp", () => { var a = new long[] { 3, 1, 2 }; Array.Sort(a, (Comparison<long>)((x, y) => y.CompareTo(x))); return string.Join(",", a); });
            L("sort_d_cmp", () => { var a = new double[] { 3, 1, 2 }; Array.Sort(a, (Comparison<double>)((x, y) => y.CompareTo(x))); return string.Join(",", a); });
            L("sort_f_cmp", () => { var a = new float[] { 3, 1, 2 }; Array.Sort(a, (Comparison<float>)((x, y) => y.CompareTo(x))); return string.Join(",", a); });
            L("sort_u_cmp", () => { var a = new uint[] { 3, 1, 2 }; Array.Sort(a, (Comparison<uint>)((x, y) => y.CompareTo(x))); return string.Join(",", a); });
            L("sort_u1", () => { var a = new uint[] { 3, 1, 2 }; Array.Sort(a); return string.Join(",", a); });
            L("sort_f1", () => { var a = new float[] { 3, 1, 2 }; Array.Sort(a); return string.Join(",", a); });
            L("sort_u3", () => { var a = new uint[] { 5, 4, 3, 2, 1 }; Array.Sort(a, 1, 3); return string.Join(",", a); });

            L("idx_u2", () => Array.IndexOf(new uint[] { 3, 1, 2 }, 1u));
            L("idx_u3", () => Array.IndexOf(new uint[] { 3, 1, 2 }, 1u, 1));
            L("idx_u4", () => Array.IndexOf(new uint[] { 3, 1, 2 }, 1u, 0, 2));
            L("lidx_u2", () => Array.LastIndexOf(new uint[] { 3, 1, 2, 1 }, 1u));
            L("lidx_u3", () => Array.LastIndexOf(new uint[] { 3, 1, 2, 1 }, 1u, 2));
            L("bs_u2", () => Array.BinarySearch(new uint[] { 1, 2, 3 }, 2u));
            L("bs_u3", () => Array.BinarySearch(new uint[] { 1, 2, 3 }, 2u, Comparer<uint>.Default));

            L("sort_obj_keys", () => { var k = new object[] { "c", "a", "b" }; var v = new int[] { 3, 1, 2 }; Array.Sort(k, v); return string.Join(",", k) + "|" + string.Join(",", v); });
            L("sort_obj_1", () => { var k = new object[] { "c", "a", "b" }; Array.Sort(k); return string.Join(",", k); });
            L("sort_obj_3", () => { var k = new object[] { "c", "a", "b" }; Array.Sort(k, 0, 3); return string.Join(",", k); });
        }
    }
}
