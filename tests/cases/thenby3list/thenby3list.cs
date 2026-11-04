using System;
using System.Collections.Generic;
using System.Linq;

// thenby3list -- CHAINED ThenBy over a List<T> source.
// OrderBy(k1).ThenBy(k2).ThenBy(k3) on a List<T> is the same net48
// EnumerableSorter chain as on an array; the list is snapshotted ONCE by
// Enumerable.OrderBy (the list itself is never written).
namespace Cases
{
    public sealed class Item
    {
        public int A;
        public int B;
        public int C;
        public string Tag;
        public Item(int a, int b, int c, string tag) { A = a; B = b; C = c; Tag = tag; }
    }

    public sealed class Sel
    {
        public int Calls;
        public int KA(Item x) { ++Calls; return x.A; }
        public int KB(Item x) { ++Calls; return x.B; }
        public int KC(Item x) { ++Calls; return x.C; }
    }

    public static class ThenBy3List
    {
        static string J(Item[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].Tag;
            }
            return s;
        }

        static string JI(List<int> a)
        {
            string s = "";
            for (int i = 0; i < a.Count; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        static List<Item> Items()
        {
            return new List<Item>
            {
                new Item(2, 1, 2, "c"),
                new Item(1, 2, 1, "b"),
                new Item(1, 1, 2, "a"),
                new Item(2, 2, 1, "f"),
                new Item(1, 1, 1, "z"),
                new Item(2, 1, 1, "e"),
                new Item(1, 2, 2, "y"),
                new Item(3, 1, 1, "g"),
            };
        }

        static int Tens(int v) { return v / 10; }
        static int Units(int v) { return v % 10; }
        static int Hundreds(int v) { return v / 100; }

        public static void Main()
        {
            // 1) three keys over a List<Item>
            Sel s = new Sel();
            Func<Item, int> ka = s.KA, kb = s.KB, kc = s.KC;
            List<Item> src = Items();
            Console.WriteLine("list3=" + J(src.OrderBy(ka).ThenBy(kb).ThenBy(kc).ToArray()));

            // 2) independent directions
            Console.WriteLine("list3_d2=" + J(src.OrderBy(ka).ThenByDescending(kb).ThenBy(kc).ToArray()));
            Console.WriteLine("list3_d3=" + J(src.OrderBy(ka).ThenBy(kb).ThenByDescending(kc).ToArray()));
            Console.WriteLine("list3_d12=" + J(src.OrderByDescending(ka).ThenByDescending(kb).ThenBy(kc).ToArray()));

            // 3) FOUR keys
            Func<Item, string> kt = x => x.Tag;
            Console.WriteLine("list3_src_count=" + src.Count);

            // 4) exactly-once per element
            Sel cnt = new Sel();
            Func<Item, int> c1 = cnt.KA, c2 = cnt.KB, c3 = cnt.KC;
            Item[] rc = src.OrderBy(c1).ThenBy(c2).ThenBy(c3).ToArray();
            Console.WriteLine("list3_calls=" + cnt.Calls);

            // 5) a List<int> with three int keys (multi-digit so each level decides)
            List<int> nums = new List<int> { 123, 213, 132, 231, 312, 321, 112, 121 };
            Console.WriteLine("list3_i4=" + JI(nums.OrderBy(Hundreds).ThenBy(Tens).ThenBy(Units).ToList()));

            // 6) stability on full ties
            List<int> tie = new List<int> { 11, 11, 11 };
            Console.WriteLine("list3_tie=" + JI(tie.OrderBy(Hundreds).ThenBy(Tens).ThenBy(Units).ToList()));

            // 7) empty / single
            Console.WriteLine("list3_empty=" + new List<int>().OrderBy(Hundreds).ThenBy(Tens).ThenBy(Units).Count());
            Console.WriteLine("list3_one=" + new List<int> { 7 }.OrderBy(Hundreds).ThenBy(Tens).ThenBy(Units).First());

            // 8) the LIST itself is not written
            List<int> orig = new List<int> { 123, 213, 132 };
            List<int> sorted = orig.OrderBy(Hundreds).ThenBy(Tens).ThenBy(Units).ToList();
            Console.WriteLine("list3_src=" + JI(orig) + "|" + JI(sorted));
        }
    }
}