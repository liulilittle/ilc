//------------------------------------------------------------------------------
// loud-linq-decorderbygrp.cs -- e2e reference case for OrderBy / ThenBy over a
// System.Decimal GroupBy BUCKET with System.DateTime / System.Guid wide keys
// (net48 OrderBy<decimal,TKey>) and the chained ThenBy / ThenByDescending.
//
// The bucket snapshot feeds the decimal-source carrier sort (DateTime compares
// by ticks -- 8 bytes; Guid by the net48 field-wise CompareTo -- 16 bytes).
// ThenBy is a fused composite sort (k1, k2) with each selector invoked exactly
// once. Before this change the bucket OrderBy LOUD-rejected "covers
// int32/int64/double/decimal keys only" and the decimal-element ThenBy had no
// composite entry.
//
// Covers: ascending/descending, tie stability, key width (high-tick DateTime,
// field-wise Guid), mixed dt->guid chains, empty/single boundaries. Every
// printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecOrderByGrp
    {
        private static string JoinDec(decimal[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString(CultureInfo.InvariantCulture);
            }
            return s;
        }

        private static Guid[] GuidKeys()
        {
            Guid g1 = new Guid("10000000-0000-0000-0000-000000000000");
            Guid g2 = new Guid("0f000000-ffff-ffff-ffff-ffffffffffff");
            Guid g3 = new Guid("10000000-0000-0000-0000-0000000000ff");
            Guid g4 = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            Guid g5 = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            return new Guid[] { g1, g2, g3, g4, g5 };
        }

        public static void Main()
        {
            Guid[] G = GuidKeys();

            // x=1..5; bucket key 1 -> {1,3,5}, key 0 -> {2,4} (first-appearance).
            decimal[] a = { 1m, 2m, 3m, 4m, 5m };
            DateTime[] dt =
            {
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),  // ties x=1
                new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc),  // ties x=2
            };
            Guid[] gd = { G[0], G[4], G[2], G[1], G[3] };

            // A single GroupBy pass: the runtime tracks the bucket result and
            // re-iteration is not part of the Decimal-bucket contract.
            foreach (var grp in a.GroupBy(x => (int)(x % 2m)))
            {
                Console.WriteLine("bdt=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => dt[(int)x - 1]).ToArray()));
                Console.WriteLine("bdtD=" + grp.Key + ":" +
                    JoinDec(grp.OrderByDescending(x => dt[(int)x - 1]).ToArray()));
                Console.WriteLine("bg=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => gd[(int)x - 1]).ToArray()));
                Console.WriteLine("bgD=" + grp.Key + ":" +
                    JoinDec(grp.OrderByDescending(x => gd[(int)x - 1]).ToArray()));
                Console.WriteLine("btb=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => dt[(int)x - 1])
                        .ThenBy(x => gd[(int)x - 1]).ToArray()));
                Console.WriteLine("btbD=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => dt[(int)x - 1])
                        .ThenByDescending(x => gd[(int)x - 1]).ToArray()));
            }

            // ===== decimal SOURCE ThenBy chain (same composite kernels) =====
            Console.WriteLine("stb=" + JoinDec(a.OrderBy(x => dt[(int)x - 1])
                .ThenBy(x => gd[(int)x - 1]).ToArray()));

            // ===== the remaining key-pair combinations =====
            Console.WriteLine("gdtb=" + JoinDec(a.OrderBy(x => gd[(int)x - 1])
                .ThenBy(x => dt[(int)x - 1]).ToArray()));
            Console.WriteLine("ggtb=" + JoinDec(a.OrderBy(x => gd[(int)x - 1])
                .ThenByDescending(x => gd[(int)x - 1]).ToArray()));
            Console.WriteLine("dtdt=" + JoinDec(a.OrderBy(x => dt[(int)x - 1])
                .ThenByDescending(x => dt[(int)x - 1]).ToArray()));

            // ===== bucket Guid primary + DateTime secondary (desc) =====
            foreach (var grp in a.GroupBy(x => (int)(x % 2m)))
            {
                Console.WriteLine("bgdt=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => gd[(int)x - 1])
                        .ThenByDescending(x => dt[(int)x - 1]).ToArray()));
            }

            // ===== the primary key family is independent of the wide secondary =====
            Console.WriteLine("i4dt=" + JoinDec(a.OrderBy(x => (int)x)
                .ThenBy(x => dt[(int)x - 1]).ToArray()));
            Console.WriteLine("i8guid=" + JoinDec(a.OrderBy(x => (long)x)
                .ThenByDescending(x => gd[(int)x - 1]).ToArray()));
            Console.WriteLine("ddt=" + JoinDec(a.OrderByDescending(x => (double)x)
                .ThenBy(x => dt[(int)x - 1]).ToArray()));
            Console.WriteLine("decdguid=" + JoinDec(a.OrderByDescending(x => x)
                .ThenByDescending(x => gd[(int)x - 1]).ToArray()));
            foreach (var grp in a.GroupBy(x => (int)(x % 2m)))
            {
                Console.WriteLine("bi4dt=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => (int)x)
                        .ThenBy(x => dt[(int)x - 1]).ToArray()));
                Console.WriteLine("bdecguid=" + grp.Key + ":" +
                    JoinDec(grp.OrderBy(x => x)
                        .ThenByDescending(x => gd[(int)x - 1]).ToArray()));
            }

            // ===== full-tie stability: equal dt AND equal guid keep source order =====
            decimal[] s = { 1m, 2m };
            DateTime[] sdt =
            {
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
                new DateTime(2020, 5, 17, 9, 30, 0, DateTimeKind.Utc),
            };
            Guid tie = G[0];
            Guid[] sgd = { tie, tie };
            Console.WriteLine("stable=" + JoinDec(s.OrderBy(x => sdt[(int)x - 1])
                .ThenBy(x => sgd[(int)x - 1]).ToArray()));

            // ===== empty and single-element boundaries =====
            decimal[] empty = new decimal[0];
            Console.WriteLine("empty=" + JoinDec(empty.OrderBy(x => dt[0])
                .ThenBy(x => G[0]).ToArray()));
            decimal[] single = { 42m };
            Console.WriteLine("single=" + JoinDec(single.OrderByDescending(x => dt[4])
                .ThenByDescending(x => G[4]).ToArray()));

            Console.WriteLine("done");
        }
    }
}