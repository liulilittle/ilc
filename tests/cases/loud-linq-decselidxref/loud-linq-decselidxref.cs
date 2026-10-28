//------------------------------------------------------------------------------
// loud-linq-decselidxref.cs -- e2e reference case for Enumerable.Select with an
// (element, index) selector over a DECIMAL source and a DECIMAL GroupBy bucket
// returning a REFERENCE TResult (net48 Select<decimal,int,TResult>).
//
// The element crosses as a 16-byte decimal cell BY VALUE; the selector gets the
// element AND its zero-based index and returns Il2CppObject*. The result is a
// fresh reference array. Before this change the shape LOUD-rejected ("the
// runtime has no decimal index reference-result entry").
//
// Covers: value/index correctness, a non-string reference shape, empty and
// single-element sources/buckets. Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecSelIdxRef
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 1.5m, 2.5m, 3.5m };

            // ===== decimal source, (element, index) -> string =====
            var s = a.Select((x, i) => D(x) + "#" + i).ToArray();
            Console.WriteLine("src=" + string.Join(",", s));

            // ===== decimal source, index drives the value =====
            var s2 = a.Select((x, i) => "i" + i + "=" + D(x * (decimal)(i + 1))).ToArray();
            Console.WriteLine("idx=" + string.Join(",", s2));

            // ===== decimal GroupBy bucket, (element, index) -> string =====
            decimal[] b = { 1m, 2m, 3m, 4m, 5m };
            var g = b.GroupBy(x => (int)(x % 2m));
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key);
                var bs = grp.Select((x, i) => "e" + i + ":" + D(x)).ToArray();
                Console.WriteLine("  " + string.Join(",", bs));
            }

            // ===== reference source, (element, index) -> decimal =====
            string[] w = { "a", "b", "c" };
            var rd = w.Select((x, i) => (decimal)i * 1.5m).ToArray();
            Console.WriteLine("refsrcdec=" + D(rd[0]) + "," + D(rd[1]) + "," + D(rd[2]));

            // ===== empty and single-element boundaries =====
            decimal[] empty = new decimal[0];
            Console.WriteLine("empty=" + empty.Select((x, i) => D(x) + "#" + i).Count());
            decimal[] one = { 9.25m };
            Console.WriteLine("one=" + string.Join(",", one.Select((x, i) => D(x) + "@" + i).ToArray()));

            var ge = empty.GroupBy(x => (int)x);
            int buckets = 0;
            foreach (var grp in ge)
            {
                buckets += grp.Select((x, i) => D(x) + "#" + i).Count();
            }
            Console.WriteLine("emptybuckets=" + buckets);

            Console.WriteLine("done");
        }
    }
}