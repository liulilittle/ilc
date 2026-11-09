// probe-grp-forder.cs -- OrderBy / OrderByDescending over a System.Single
// GroupBy bucket (float ELEMENT, int32 KEY).
//
// Baseline (main 074ae92b): the bucket switch's float arms end at Contains, so
// OrderBy fell through to the floatBucket LOUD gate (an honest rejection). The
// wrapper snapshots the bucket to its REAL float[] (RequireFGroupElements) and
// reuses the float-SOURCE keyed-sort kernel; the result is a fresh float[].
//
// Each shape is its own method: a single LOUD abort zeroes the whole probe.
// A float TKey GroupBy is a SEPARATE gap (the grouping surface buckets by an
// int32/int64/16-byte-struct key carrier; a float key has no carrier), so the
// key here is int32 (x => (int)x).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpFOrder
    {
        private static string FB(float v)
        {
            return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ===== 1. OrderBy(x => x) over a float bucket =====
        // NEGATIVE values: the int32 BIT-PATTERN order of a float cell is the
        // REVERSE of its value order for same-sign floats (0xC0000000 as int32
        // is negative), so reading the float bits as int32 is observable.
        private static void OrderBySelf()
        {
            float[] xs = { -3f, 1f, -2.5f, 2.5f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fSelf] " + string.Join(",", g.OrderBy(x => x).Select(v => FB(v))));
        }

        // ===== 2. OrderByDescending(x => x) over a float bucket =====
        private static void OrderByDescSelf()
        {
            float[] xs = { -3f, 1f, -2.5f, 2.5f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fDesc] " + string.Join(",", g.OrderByDescending(x => x).Select(v => FB(v))));
        }

        // ===== 3. OrderBy(x => (int)x) -- an int32 KEY over a float bucket =====
        private static void OrderByIntKey()
        {
            float[] xs = { 3.7f, 1.2f, 3.1f, 2.5f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fIntKey] " + string.Join(",", g.OrderBy(x => (int)x).Select(v => FB(v))));
        }

        // ===== 4. OrderBy(x => (long)x) -- an int64 KEY over a float bucket =====
        private static void OrderByLongKey()
        {
            float[] xs = { 3.7f, 1.2f, 3.1f, 2.5f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fLongKey] " + string.Join(",", g.OrderBy(x => (long)x).Select(v => FB(v))));
        }

        // ===== 5. OrderBy(x => (double)x) -- a double KEY over a float bucket =====
        private static void OrderByDoubleKey()
        {
            float[] xs = { 3.7f, 1.2f, 3.1f, 2.5f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fDoubleKey] " + string.Join(",", g.OrderBy(x => (double)x).Select(v => FB(v))));
        }

        public static void Main()
        {
            OrderBySelf();
            OrderByDescSelf();
            OrderByIntKey();
            OrderByLongKey();
            OrderByDoubleKey();
        }
    }
}
