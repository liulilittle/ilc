using System;
using System.Collections.Generic;

namespace Cases
{
    public sealed class RevD : IComparer<double>
    {
        public int Compare(double a, double b) { return b.CompareTo(a); }
    }

    public static class Probe11
    {
        static void L(string n, Func<object> f)
        {
            try { Console.WriteLine(n + "=" + (f() ?? "null")); }
            catch (Exception e) { Console.WriteLine(n + "!" + e.GetType().Name); }
        }

        public static void Main()
        {
            L("d2", () => { var a = new[] { 4d, 1d, 3d, 2d }; Array.Sort(a, new RevD()); return string.Join(",", a); });
            L("d3", () => { var a = new[] { 4d, 1d, 3d, 2d }; Array.Sort(a, 1, 2, new RevD()); return string.Join(",", a); });
            L("d4", () => { var a = new[] { 4d, 1d, 3d, 2d }; Array.Sort(a, 0, 4, new RevD()); return string.Join(",", a); });
            L("ddef", () => { var a = new[] { 4d, 1d, 3d, 2d }; Array.Sort(a, Comparer<double>.Default); return string.Join(",", a); });
            L("ddef4", () => { var a = new[] { 4d, 1d, 3d, 2d }; Array.Sort(a, 0, 4, Comparer<double>.Default); return string.Join(",", a); });
            L("f2", () => { var a = new[] { 4f, 1f, 3f, 2f }; Array.Sort(a, Comparer<float>.Create((x, y) => y.CompareTo(x))); return string.Join(",", a); });
            L("l2", () => { var a = new[] { 4L, 1L, 3L, 2L }; Array.Sort(a, Comparer<long>.Create((x, y) => y.CompareTo(x))); return string.Join(",", a); });
        }
    }
}
