using System;
using System.Collections.Generic;
using System.Linq;

// thenby3 -- CHAINED ThenBy: OrderBy(k1).ThenBy(k2).ThenBy(k3) and deeper.
// The fused two-key composite handles exactly one ThenBy; a second ThenBy
// must keep the SAME net48 EnumerableSorter contract: each selector is
// invoked exactly once per element, the sort is stable on full ties, and the
// direction of every key is independent.
namespace Cases
{
    public sealed class Keys3
    {
        public int K1(int x) { return x / 1000; }
        public int K2(int x) { return (x / 100) % 10; }
        public int K3(int x) { return (x / 10) % 10; }
        public int K4(int x) { return x % 10; }

        public long L2(int x) { return (long)((x / 100) % 10) << 32; }
        public double D2(int x) { return ((x / 100) % 10) * 1.5 + 0.25; }
        public int K1L(long x) { return (int)(x / 1000L); }
        public long L3L(long x) { return (long)((x / 10L) % 10L) << 32; }

        private int m_c1;
        private int m_c2;
        private int m_c3;
        public int Calls1 { get { return m_c1; } }
        public int Calls2 { get { return m_c2; } }
        public int Calls3 { get { return m_c3; } }
        public int Count1(int x) { ++m_c1; return x / 1000; }
        public int Count2(int x) { ++m_c2; return (x / 100) % 10; }
        public int Count3(int x) { ++m_c3; return (x / 10) % 10; }
    }

    public static class ThenBy3
    {
        static string J(int[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        static string J8(long[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        public static void Main()
        {
            Keys3 k = new Keys3();
            Func<int, int> k1 = k.K1;
            Func<int, int> k2 = k.K2;
            Func<int, int> k3 = k.K3;
            Func<int, int> k4 = k.K4;
            Func<int, long> l2 = k.L2;
            Func<int, double> d2 = k.D2;

            // 1) three ascending keys over a shuffled multiset
            int[] src = new int[]
            {
                1234, 1243, 1324, 1342, 1423, 1432,
                2134, 2143, 2314, 2341, 2413, 2431,
                3214, 3241,
            };
            int[] r3 = src.OrderBy(k1).ThenBy(k2).ThenBy(k3).ToArray();
            Console.WriteLine("three=" + J(r3));

            // 2) independent directions on every key
            int[] rd = src.OrderBy(k1).ThenByDescending(k2).ThenBy(k3).ToArray();
            Console.WriteLine("three_d2=" + J(rd));
            int[] rd2 = src.OrderBy(k1).ThenBy(k2).ThenByDescending(k3).ToArray();
            Console.WriteLine("three_d3=" + J(rd2));
            int[] rd12 = src.OrderByDescending(k1).ThenByDescending(k2).ThenBy(k3).ToArray();
            Console.WriteLine("three_d12=" + J(rd12));

            // 3) FOUR keys
            int[] r4 = src.OrderBy(k1).ThenBy(k2).ThenBy(k3).ThenBy(k4).ToArray();
            Console.WriteLine("four=" + J(r4));

            // 4) mixed key families in the chain (int32 / int64 / double)
            int[] rm = src.OrderBy(k1).ThenBy(l2).ThenBy(d2).ToArray();
            Console.WriteLine("three_mixed=" + J(rm));

            // 5) stability: full-tie elements keep their original order
            int[] tie = new int[] { 11, 11, 11, 11 };
            int[] rt = tie.OrderBy(k1).ThenBy(k2).ThenBy(k3).ToArray();
            Console.WriteLine("three_tie=" + J(rt));

            // 6) exactly-once: every selector runs ONE time per element
            Keys3 cnt = new Keys3();
            int[] rc = src.OrderBy(cnt.Count1).ThenBy(cnt.Count2).ThenBy(cnt.Count3).ToArray();
            Console.WriteLine("three_calls=" + cnt.Calls1 + "," + cnt.Calls2 + "," + cnt.Calls3);

            // 7) empty / single
            int[] empty = new int[0];
            Console.WriteLine("three_empty=" + empty.OrderBy(k1).ThenBy(k2).ThenBy(k3).Count());
            int[] one = new int[] { 7 };
            Console.WriteLine("three_one=" + one.OrderBy(k1).ThenBy(k2).ThenBy(k3).First());

            // 8) int64 source
            long[] lsrc = new long[] { 1234L, 1243L, 1324L, 3214L, 2134L };
            Keys3 lk = new Keys3();
            Func<long, int> f1 = lk.K1L;
            Func<long, long> f3 = lk.L3L;
            long[] rl = lsrc.OrderBy(f1).ThenBy(f3).ThenBy(f1).ToArray();
            Console.WriteLine("three_i8=" + J8(rl));
        }
    }
}