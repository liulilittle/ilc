//------------------------------------------------------------------------------
// linq-0x11-noname-safety -- e2e guard for impl-linq-0x11 (audit loud-inv §3.1
// Δ0): an UNRESOLVED 0x11 value-type source name must never be assumed to be
// System.Decimal.
//
// The 0x11 dispatch byte covers every value type, so the decoder's generic
// argument name decides the family. Two shapes matter:
//
//   * NULL name table -- the NON-generic MemberRef aggregate surface
//     (Enumerable.Sum/Min/Max/Average over decimal[]: genArgs are decoded from
//     the IEnumerable<decimal> signature parameter, no MethodSpec exists). On
//     net48 the only 0x11 overload there IS System.Decimal, so it must keep
//     binding il2cpp_linq_*_dec. A parameter source is the worst case: no
//     tracked array name is available either.
//   * decoded DateTime sources -- the family kernels must read the real 8-byte
//     DateTime cells (cell code 12), never the 16-byte decimal cells.
//
// Before the fix a "?" name (undecodable VALUETYPE argument) was treated as
// decimal, so a DateTime source bound il2cpp_linq_*_dec: a SILENT wrong value
// (16-byte decimal read out of an 8-byte cell). That "?" shape only exists in
// synthetic/broken metadata and is covered by the unit test
// Resolve_0x11_NonameSource_DeclaresLoudOrCorrectFamily; this case is the e2e
// regression guard for the two REAL shapes above.
//
// Every printed line is "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class Linq0x11NonameSafety
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string Day(DateTime v) { return v.Year + "-" + v.Month + "-" + v.Day; }

        // NULL-name MemberRef aggregate surface: the source is a PARAMETER, so
        // neither a MethodSpec name nor a tracked array name is available.
        private static decimal DecSum(decimal[] a) { return a.Sum(); }
        private static decimal DecMin(decimal[] a) { return a.Min(); }
        private static decimal DecMax(decimal[] a) { return a.Max(); }
        private static decimal DecAvg(decimal[] a) { return a.Average(); }

        // Decoded DateTime family: the MethodSpec name says System.DateTime, so
        // the value-family kernels (cell code 12) must serve these operators.
        private static int DtTakeCount(DateTime[] a) { return a.Take(2).Count(); }
        private static long DtLongCount(DateTime[] a) { return a.LongCount(); }
        private static bool DtSeq(DateTime[] a, DateTime[] b) { return a.SequenceEqual(b); }
        private static DateTime DtSingle(DateTime[] a) { return a.Single(); }
        private static DateTime DtElementAt(DateTime[] a, int i) { return a.ElementAt(i); }
        private static bool DtContains(DateTime[] a, DateTime v) { return a.Contains(v); }
        private static DateTime[] DtTake(DateTime[] a, int n) { return a.Take(n).ToArray(); }

        // Decoded decimal family (MethodSpec name path) control.
        private static int DecTakeCount(decimal[] a) { return a.Take(2).Count(); }
        private static decimal DecSingle(decimal[] a) { return a.Single(); }
        private static bool DecSeq(decimal[] a, decimal[] b) { return a.SequenceEqual(b); }

        public static void Main()
        {
            decimal[] ds = new decimal[3];
            ds[0] = 1.5m; ds[1] = -2.25m; ds[2] = 100m;
            Console.WriteLine("dec_sum=" + D(DecSum(ds)));
            Console.WriteLine("dec_min=" + D(DecMin(ds)));
            Console.WriteLine("dec_max=" + D(DecMax(ds)));
            Console.WriteLine("dec_avg=" + D(DecAvg(ds)));
            Console.WriteLine("dec_take_count=" + DecTakeCount(ds));
            Console.WriteLine("dec_seq=" + DecSeq(ds, ds));

            decimal[] one = new decimal[1];
            one[0] = 7.25m;
            Console.WriteLine("dec_single=" + D(DecSingle(one)));

            DateTime[] dts = new DateTime[3];
            dts[0] = new DateTime(2020, 1, 2, 3, 4, 5);
            dts[1] = new DateTime(2021, 6, 7, 8, 9, 10);
            dts[2] = new DateTime(2022, 12, 31, 23, 59, 58);
            Console.WriteLine("dt_take_count=" + DtTakeCount(dts));
            Console.WriteLine("dt_long_count=" + DtLongCount(dts));
            Console.WriteLine("dt_seq=" + DtSeq(dts, dts));
            Console.WriteLine("dt_elem1=" + Day(DtElementAt(dts, 1)));
            Console.WriteLine("dt_contains=" + DtContains(dts, dts[2]));
            DateTime probe = new DateTime(1999, 12, 31, 1, 2, 3);
            Console.WriteLine("dt_not_contains=" + DtContains(dts, probe));
            DateTime[] taken = DtTake(dts, 2);
            Console.WriteLine("dt_take_len=" + taken.Length);
            Console.WriteLine("dt_take0=" + Day(taken[0]));
            Console.WriteLine("dt_take1=" + Day(taken[1]));

            DateTime[] single = new DateTime[1];
            single[0] = new DateTime(2019, 3, 4, 5, 6, 7);
            Console.WriteLine("dt_single=" + Day(DtSingle(single)));

            int[] xs = new int[3];
            xs[0] = 4; xs[1] = 5; xs[2] = 6;
            Console.WriteLine("i4_sum=" + xs.Sum());
            Console.WriteLine("i4_min=" + xs.Min());
            Console.WriteLine("i4_max=" + xs.Max());

            Console.WriteLine("done");
        }
    }
}