//------------------------------------------------------------------------------
// p0linq4 -- the four LINQ gaps reported honestly by p0linq1/p0linq2.
//
//   D1  Enumerable.ToList over decimal[] / DateTime[] / TimeSpan[] / a USER
//       STRUCT[] -- the transpiler rejected every one of them
//       ("no List<T> storage domain exists for this element type") even
//       though the runtime can materialize them through the listobj domain
//       (box on entry, unbox on exit -- the G4 long/double precedent).
//   D2  Enumerable.Select((element, index) => decimal) -- the index overload
//       with a decimal result ("no il2cpp_linq_select_idx_*_dec entry").
//   D3  Enumerable.Select over a REFERENCE source returning decimal
//       ("no il2cpp_linq_select_ref_dec entry").
//   D4  Enumerable.ThenBy / ThenByDescending over a long[] source
//       ("the stage-1 then-by surface is int32 elements AND int32 keys").
//
// Every printed value is deterministic (decimal/DateTime/TimeSpan formatted
// through CultureInfo.InvariantCulture; everything else is bool/int/long).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public struct Pt
    {
        public int X;
        public int Y;
        public Pt(int x, int y) { X = x; Y = y; }
    }

    public static class P0Linq4
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static decimal WithIndex(int x, int i) { return x * 10m + i; }
        private static decimal RefToDec(string s) { return s.Length / 2m; }

        private static int KeyMod2(long x) { return (int)(x % 2); }
        private static int KeyInt(long x) { return (int)x; }
        private static int KeyHalf(long x) { return (int)(x / 2); }
        private static long KeyTens(long x) { return x / 10L; }
        private static long KeySelf(long x) { return x; }

        private static string JoinLongs(long[] a)
        {
            return string.Join(",", a.Select(x => x.ToString()).ToArray());
        }

        public static void Main()
        {
            // ===== D1a: ToList over decimal[] =====
            decimal[] ds = { 1.5m, -2.25m, 3.125m };
            List<decimal> ld = ds.ToList();
            Console.WriteLine("d1dec_count=" + ld.Count);
            Console.WriteLine("d1dec_0=" + D(ld[0]));
            Console.WriteLine("d1dec_1=" + D(ld[1]));
            Console.WriteLine("d1dec_2=" + D(ld[2]));
            ld.Add(9.5m);
            Console.WriteLine("d1dec_after_add=" + ld.Count + "/" + D(ld[3]));
            Console.WriteLine("d1dec_src_len=" + ds.Length);
            decimal lsum = 0m;
            foreach (decimal v in ld) { lsum = lsum + v; }
            Console.WriteLine("d1dec_sum=" + D(lsum));
            Console.WriteLine("d1dec_empty=" + new decimal[0].ToList().Count);

            // ===== D1b: ToList over DateTime[] =====
            DateTime[] dts =
            {
                new DateTime(2020, 1, 2),
                new DateTime(1999, 12, 31),
            };
            List<DateTime> ldt = dts.ToList();
            Console.WriteLine("d1dt_count=" + ldt.Count);
            Console.WriteLine("d1dt_0=" + ldt[0].Year + "-" + ldt[0].Month + "-" + ldt[0].Day);
            Console.WriteLine("d1dt_1=" + ldt[1].Year + "-" + ldt[1].Month + "-" + ldt[1].Day);
            Console.WriteLine("d1dt_ticks_eq=" + (ldt[0].Ticks == dts[0].Ticks));
            ldt.Add(new DateTime(2001, 2, 3));
            Console.WriteLine("d1dt_added=" + ldt[2].Year + "/" + ldt[2].Month + "/" + ldt[2].Day);

            // ===== D1c: ToList over TimeSpan[] =====
            TimeSpan[] tss = { TimeSpan.FromMinutes(90), TimeSpan.FromSeconds(-5) };
            List<TimeSpan> lts = tss.ToList();
            Console.WriteLine("d1ts_count=" + lts.Count);
            Console.WriteLine("d1ts_0_min=" + lts[0].TotalMinutes);
            Console.WriteLine("d1ts_1_sec=" + lts[1].TotalSeconds);
            Console.WriteLine("d1ts_ticks_eq=" + (lts[0].Ticks == tss[0].Ticks));

            // ===== D1d: ToList over a USER STRUCT[] =====
            Pt[] pts = { new Pt(1, 2), new Pt(3, 4), new Pt(5, 6) };
            List<Pt> lpt = pts.ToList();
            Console.WriteLine("d1pt_count=" + lpt.Count);
            Console.WriteLine("d1pt_0=" + lpt[0].X + "," + lpt[0].Y);
            Console.WriteLine("d1pt_1=" + lpt[1].X + "," + lpt[1].Y);
            Console.WriteLine("d1pt_2=" + lpt[2].X + "," + lpt[2].Y);
            lpt.Add(new Pt(7, 8));
            Console.WriteLine("d1pt_after_add=" + lpt.Count + "/" + lpt[3].X + "," + lpt[3].Y);
            Console.WriteLine("d1pt_src_len=" + pts.Length);
            int px = 0;
            for (int i = 0; i < lpt.Count; i++) { px = px + lpt[i].X; }
            Console.WriteLine("d1pt_xsum=" + px);
            Console.WriteLine("d1pt_empty=" + new Pt[0].ToList().Count);

            // ===== D2: Select with (element, index) -> decimal =====
            int[] ints = { 1, 2, 3 };
            decimal[] d2 = ints.Select(WithIndex).ToArray();
            Console.WriteLine("d2_len=" + d2.Length);
            Console.WriteLine("d2_0=" + D(d2[0]));
            Console.WriteLine("d2_1=" + D(d2[1]));
            Console.WriteLine("d2_2=" + D(d2[2]));
            // the selector runs EXACTLY ONCE per element, with the SOURCE index.
            decimal d2sum = ints.Select(WithIndex).Sum();
            Console.WriteLine("d2_sum=" + D(d2sum));
            Console.WriteLine("d2_empty=" + new int[0].Select(WithIndex).ToArray().Length);
            // the result is a fresh array (the source is never written).
            Console.WriteLine("d2_fresh=" + (ints[0] == 1));

            // ===== D3: Select over a REFERENCE source -> decimal =====
            string[] strs = { "a", "bb", "cccc" };
            decimal[] d3 = strs.Select(RefToDec).ToArray();
            Console.WriteLine("d3_len=" + d3.Length);
            Console.WriteLine("d3_0=" + D(d3[0]));
            Console.WriteLine("d3_1=" + D(d3[1]));
            Console.WriteLine("d3_2=" + D(d3[2]));
            Console.WriteLine("d3_empty=" + new string[0].Select(RefToDec).ToArray().Length);
            decimal d3sum = strs.Select(RefToDec).Sum();
            Console.WriteLine("d3_sum=" + D(d3sum));

            // ===== D4: long[] ThenBy / ThenByDescending =====
            long[] la = { 3L, 1L, 2L, 11L, 10L, 12L };
            long[] o1 = la.OrderBy(KeyMod2).ThenBy(KeyInt).ToArray();
            Console.WriteLine("d4_asc=" + JoinLongs(o1));
            long[] o2 = la.OrderBy(KeyMod2).ThenByDescending(KeyInt).ToArray();
            Console.WriteLine("d4_desc=" + JoinLongs(o2));
            // int64 keys on both levels.
            long[] o3 = la.OrderBy(KeyTens).ThenBy(KeySelf).ToArray();
            Console.WriteLine("d4_i8asc=" + JoinLongs(o3));
            long[] o4 = la.OrderBy(KeyTens).ThenByDescending(KeySelf).ToArray();
            Console.WriteLine("d4_i8desc=" + JoinLongs(o4));
            // stability: equal (k1, k2) tuples keep their source order.
            long[] st = { 5L, 4L, 5L, 4L };
            long[] o5 = st.OrderBy(KeyMod2).ThenBy(KeyHalf).ToArray();
            Console.WriteLine("d4_stable=" + JoinLongs(o5));
            // the source is never written.
            Console.WriteLine("d4_src=" + JoinLongs(la));
            Console.WriteLine("d4_empty=" + new long[0].OrderBy(KeyInt).ThenBy(KeyInt).ToArray().Length);
        }
    }
}
