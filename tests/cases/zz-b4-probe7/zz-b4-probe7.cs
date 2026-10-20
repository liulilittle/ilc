using System;
using System.Collections.Generic;

namespace Cases
{
    public sealed class Box
    {
        public int V;
        public Box(int v) { V = v; }
    }

    public static class Probe7
    {
        static void L(string n, Func<object> f)
        {
            try { Console.WriteLine(n + "=" + (f() ?? "null")); }
            catch (Exception e) { Console.WriteLine(n + "!" + e.GetType().Name); }
        }

        public static void Main()
        {
            L("pn_aoore", () =>
            {
                try { var a = new int[2]; var x = a[5]; return "no"; }
                catch (IndexOutOfRangeException) { return "ioore"; }
            });
            L("pn_lastidx", () =>
            {
                try { Array.LastIndexOf(new int[1], 1, 5, 2); return "no"; }
                catch (ArgumentException e) { return e.GetType().Name + ":" + (e.ParamName ?? "null"); }
            });
            L("pn_capacity", () =>
            {
                try { var sb = new System.Text.StringBuilder("ab"); sb.Capacity = 1; return "no"; }
                catch (ArgumentException e) { return e.GetType().Name + ":" + (e.ParamName ?? "null"); }
            });
            L("pn_sort", () =>
            {
                try { Array.Sort(new int[3], 0, -1); return "no"; }
                catch (ArgumentException e) { return e.GetType().Name + ":" + (e.ParamName ?? "null"); }
            });

            L("box_create", () =>
            {
                var a = new[] { new Box(1), new Box(3), new Box(2) };
                var c = Comparer<Box>.Create((x, y) => y.V - x.V);
                Array.Sort(a, c);
                return a[0].V + "," + a[1].V + "," + a[2].V;
            });
            L("box_keys", () =>
            {
                var k = new[] { new Box(1), new Box(3), new Box(2) };
                var v = new[] { "a", "b", "c" };
                var c = Comparer<Box>.Create((x, y) => x.V - y.V);
                Array.Sort(k, v, c);
                return k[0].V + "," + k[1].V + "," + k[2].V + "|" + v[0] + "," + v[1] + "," + v[2];
            });
            L("bs_dup", () =>
            {
                var a = new[] { 1, 3, 3, 5, 7 };
                return Array.BinarySearch(a, 0, 5, 3);
            });
            L("bs_dup2", () =>
            {
                var a = new[] { 1, 2, 2, 3 };
                return Array.BinarySearch(a, 0, 4, 2);
            });
            L("bs_dup4", () =>
            {
                var a = new[] { 1, 2, 2, 2, 3 };
                return Array.BinarySearch(a, 0, 5, 2);
            });
        }
    }
}
