using System;
using System.Collections.Generic;
using System.Linq;

// thenby3ref -- the CHAINED ThenBy surface over a REFERENCE-element source
// (Item[]), i.e. the shape `OrderBy(x => x.A).ThenBy(x => x.B).ThenBy(x => x.C)`
// that C# LINQ code actually writes. net48 builds the same EnumerableSorter
// chain as for the numeric sources: the key phase runs level by level and each
// selector runs exactly once per element.
namespace Cases
{
    public sealed class Item
    {
        public int A;
        public int B;
        public int C;
        public string Tag;

        public Item(int a, int b, int c, string tag)
        {
            A = a; B = b; C = c; Tag = tag;
        }
    }

    public sealed class Sel
    {
        public int CallsA, CallsB, CallsC, CallsTag;
        public int KA(Item x) { ++CallsA; return x.A; }
        public int KB(Item x) { ++CallsB; return x.B; }
        public int KC(Item x) { ++CallsC; return x.C; }
        public string KTag(Item x) { ++CallsTag; return x.Tag; }
        public long LA(Item x) { return (long)x.A << 32; }
        public double DA(Item x) { return x.A * 1.5 + 0.25; }
    }

    public static class ThenBy3Ref
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

        static Item[] Build()
        {
            // Shuffled multiset: primary groups by A, secondary by B, tertiary by C.
            return new Item[]
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

        public static void Main()
        {
            Item[] src = Build();
            Sel s = new Sel();
            Func<Item, int> ka = s.KA;
            Func<Item, int> kb = s.KB;
            Func<Item, int> kc = s.KC;
            Func<Item, string> kt = s.KTag;
            Func<Item, long> la = s.LA;
            Func<Item, double> da = s.DA;

            // 1) three ascending keys over a reference source
            Console.WriteLine("ref3=" + J(src.OrderBy(ka).ThenBy(kb).ThenBy(kc).ToArray()));

            // 2) independent directions
            Console.WriteLine("ref3_d2=" + J(src.OrderBy(ka).ThenByDescending(kb).ThenBy(kc).ToArray()));
            Console.WriteLine("ref3_d3=" + J(src.OrderBy(ka).ThenBy(kb).ThenByDescending(kc).ToArray()));
            Console.WriteLine("ref3_d12=" + J(src.OrderByDescending(ka).ThenByDescending(kb).ThenBy(kc).ToArray()));

            // 3) stability: full-tie elements keep source order
            Item[] tie = new Item[]
            {
                new Item(1, 1, 1, "t1"),
                new Item(1, 1, 1, "t2"),
                new Item(1, 1, 1, "t3"),
            };
            Console.WriteLine("ref3_tie=" + J(tie.OrderBy(ka).ThenBy(kb).ThenBy(kc).ToArray()));

            // 4) exactly-once: each selector runs ONE time per element
            Sel cnt = new Sel();
            Func<Item, int> c1 = cnt.KA, c2 = cnt.KB, c3 = cnt.KC;
            Item[] rc = src.OrderBy(c1).ThenBy(c2).ThenBy(c3).ToArray();
            Console.WriteLine("ref3_calls=" + cnt.CallsA + "," + cnt.CallsB + "," + cnt.CallsC);

            // 5) MIXED families through the chain (int32 -> int64 -> double)
            Console.WriteLine("ref3_mixed=" + J(src.OrderBy(ka).ThenBy(la).ThenBy(da).ToArray()));

            // 6a) MIXED-CASE string key: net48 orders through CULTURE collation
            //     ("a" before "A"), which differs from an ordinal sort -- this is
            //     the record that makes the collation decision observable.
            Item[] mixed = new Item[]
            {
                new Item(1, 1, 1, "B"),
                new Item(1, 1, 2, "a"),
                new Item(1, 1, 3, "A"),
                new Item(1, 1, 4, "b"),
                new Item(2, 1, 1, "z"),
                new Item(2, 1, 2, "Z"),
            };
            Func<Item, string> mt = s.KTag;
            Console.WriteLine("ref3_case=" + J(mixed.OrderBy(ka).ThenBy(mt).ThenBy(kc).ToArray()));
            // 6) string key as the SECOND level (culture collation), then int
            Console.WriteLine("ref3_str=" + J(src.OrderBy(ka).ThenBy(kt).ThenBy(kc).ToArray()));

            // 7) empty and single
            Console.WriteLine("ref3_empty=" + new Item[0].OrderBy(ka).ThenBy(kb).ThenBy(kc).Count());
            Console.WriteLine("ref3_one=" + J(new Item[] { new Item(9, 9, 9, "one") }
                .OrderBy(ka).ThenBy(kb).ThenBy(kc).ToArray()));

            // 8) FOUR keys
            Console.WriteLine("ref3_four=" + J(src.OrderBy(ka).ThenBy(kb).ThenBy(kc).ThenBy(kt).ToArray()));

            // 9) the source array is NOT written
            Item[] copy = Build();
            Item[] sorted = copy.OrderBy(ka).ThenBy(kb).ThenBy(kc).ToArray();
            Console.WriteLine("ref3_src=" + J(copy) + "|" + (ReferenceEquals(copy, sorted) ? "same" : "fresh"));
        }
    }
}
