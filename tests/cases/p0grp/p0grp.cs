//------------------------------------------------------------------------------
// p0grp -- Enumerable.GroupBy over a DECIMAL-element array source.
//
// Before this batch, `decimal[].GroupBy(x => (int)x)` did NOT reach the GroupBy
// mapping at all: the source-family gate in MethodMap.Linq.cs (the int32-only
// gate at :522-539) returned null for genArgs[0] == 0x11, and `GroupBy` was not
// in the gate's LOUD list, so the MethodSpec fell through to the resolver's
// phantom "generic instantiation of System.Linq.Enumerable.GroupBy has no BCL
// mapping in M5" (MethodRefResolver.cs:459) -- a MISLEADING message (the real
// gate was the SOURCE family, not the key family) and a runtime stub.
//
// The minimal closed loop implemented here: TElement = System.Decimal (16-byte
// source cells) with an int32-slot KEY. The key selector receives the decimal
// BY VALUE (int32_t (*)(Il2CppDecimal) -- the generated leaf's real ABI), each
// bucket stores 16-byte decimal VALUE cells (so a bucket's ToArray is a real
// decimal[] with net48's layout), and a bucket iterates through the
// IEnumerator<decimal> 16-byte carrier (the decimal T3 result-slot contract).
//
// NOT wired (registered in docs/audit/b4-gaps.md, transpile-time LOUD):
//   * long/ulong/decimal/DateTime/Guid KEYS (no int64/16-byte key carrier)
//   * a decimal SOURCE with an element selector / comparer / result selector
//   * reference-element sources (string[] etc.) -- see loud-p0-grp-key-long
//   * the int32-only bucket OPERATORS (Where/Select/Sum/Min/Max/First/
//     ElementAt/Contains/Count(pred)/Any(pred)/All) over a decimal bucket
//
// Every line is deterministic: decimal text goes through
// CultureInfo.InvariantCulture.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class P0Grp
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            // ===== 1. decimal[] source, int32 key =====
            // (int)decimal TRUNCATES toward zero: 3.7 -> 3, -3.7 -> -3.
            decimal[] ds = { 3.7m, 1.2m, 3.1m, 1.9m, 1.0m };
            int buckets = 0;
            foreach (var g in ds.GroupBy(x => (int)x))
            {
                buckets++;
                Console.WriteLine("[dec] key=" + g.Key + " n=" + g.Count());
                int i = 0;
                foreach (decimal v in g)
                {
                    Console.WriteLine("[dec] e" + i + "=" + D(v));
                    i++;
                }
                Console.WriteLine("[dec] iter=" + i);
                decimal[] arr = g.ToArray();
                Console.WriteLine("[dec] arr=" + arr.Length);
                for (int k = 0; k < arr.Length; k++) Console.WriteLine("[dec] a" + k + "=" + D(arr[k]));
            }
            Console.WriteLine("[dec] buckets=" + buckets);
            Console.WriteLine("[dec] result_count=" + ds.GroupBy(x => (int)x).Count());

            // Negative keys: truncation toward zero keeps -3 as ONE bucket.
            decimal[] neg = { -3.7m, -3.2m, 3.9m, 0.5m };
            foreach (var g in neg.GroupBy(x => (int)x))
            {
                Console.WriteLine("[neg] key=" + g.Key + " n=" + g.Count());
                int i = 0;
                foreach (object o in (IEnumerable)g)
                {
                    Console.WriteLine("[neg] e" + i + "=" + D((decimal)o));
                    i++;
                }
            }

            // Empty source -> zero buckets.
            Console.WriteLine("[dec] empty_buckets=" + new decimal[0].GroupBy(x => (int)x).Count());

            // Single bucket, source order AND scale preserved (7.25 stays 7.25).
            decimal[] same = { 7.25m, 7.5m, 7.0m };
            foreach (var g in same.GroupBy(x => (int)x))
            {
                Console.WriteLine("[same] key=" + g.Key + " n=" + g.Count());
                int i = 0;
                foreach (decimal v in g)
                {
                    Console.WriteLine("[same] e" + i + "=" + D(v));
                    i++;
                }
            }

            // Key selector projection (parity) over the decimal source.
            foreach (var g in ds.GroupBy(x => (int)x % 2))
                Console.WriteLine("[par] key=" + g.Key + " n=" + g.Count());

            // A bucket LARGER than the runtime's initial capacity (4): the
            // bucket element array must grow at its 16-byte stride (a 4-byte
            // grow copy would keep only the low dword of each decimal).
            decimal[] big = { 1.1m, 1.2m, 1.3m, 1.4m, 1.5m, 1.6m, 1.7m };
            foreach (var g in big.GroupBy(x => (int)x))
            {
                Console.WriteLine("[big] key=" + g.Key + " n=" + g.Count());
                decimal[] arr = g.ToArray();
                Console.WriteLine("[big] arr=" + arr.Length);
                for (int k = 0; k < arr.Length; k++) Console.WriteLine("[big] a" + k + "=" + D(arr[k]));
                int i = 0;
                foreach (decimal v in g)
                {
                    Console.WriteLine("[big] e" + i + "=" + D(v));
                    i++;
                }
            }

            // ===== 2. int32[] regression (unchanged surface) =====
            int[] xs = { 5, 3, 9, 3, 5, 1 };
            int ibuckets = 0;
            foreach (var g in xs.GroupBy(x => x))
            {
                ibuckets++;
                Console.WriteLine("[int] key=" + g.Key + " n=" + g.Count());
                int[] e = g.ToArray();
                for (int i = 0; i < e.Length; i++) Console.WriteLine("[int] e" + i + "=" + e[i]);
            }
            Console.WriteLine("[int] buckets=" + ibuckets);
            Console.WriteLine("[int] result_count=" + xs.GroupBy(x => x).Count());

            foreach (var g in xs.GroupBy(x => x % 3))
                Console.WriteLine("[mod] key=" + g.Key + " n=" + g.Count());
        }
    }
}
