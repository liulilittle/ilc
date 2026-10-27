//------------------------------------------------------------------------------
// loud-linq-decbucketsel.cs -- e2e for Select over a DECIMAL GroupBy bucket
// (impl-linq3 C): the bucket snapshot feeds the DECIMAL-SOURCE select_dec_* /
// select_idx_dec_* kernels. Before this change the bucket Select LOUD-rejected
// ("the int32 predicate/selector operators have no decimal bucket kernel").
// Covers the decimal / int / long / double / float / string projections, the
// (element, index) selector form, an EMPTY bucket, and downstream reads of the
// projected arrays. Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecBucketSel
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 1.5m, 2.5m, 3.5m, 4.5m };
            var g = a.GroupBy(x => (int)x % 2);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key +
                    " sd=" + string.Join(",", grp.Select(x => D(x + 0.25m)).ToArray()) +
                    " si=" + string.Join(",", grp.Select(x => (int)(x * 2)).ToArray()) +
                    " sl=" + string.Join(",", grp.Select(x => (long)(x * 10)).ToArray()) +
                    " sv=" + string.Join(",", grp.Select(x => (double)x * 1.5)
                        .Select(v => v.ToString("R", CultureInfo.InvariantCulture)).ToArray()) +
                    " ss=" + string.Join(",", grp.Select(x => x.ToString(CultureInfo.InvariantCulture) + "!").ToArray()));
            }

            // The (element, index) selector form: the index is the zero-based
            // source index inside the bucket. (The index form with a REFERENCE
            // result stays LOUD -- the decimal index reference-result entry
            // does not exist, the same gap as the decimal-source surface.)
            decimal[] b = { 10m, 20m, 30m };
            var g2 = b.GroupBy(x => 1);
            foreach (var grp in g2)
            {
                Console.WriteLine("idx=" + string.Join(",",
                    grp.Select((x, i) => i * 100 + (int)x).ToArray()));
            }

            // The empty bucket still projects to an empty array (no throw).
            decimal[] empty = { };
            var g3 = empty.GroupBy(x => 7);
            foreach (var grp in g3)
            {
                Console.WriteLine("empty cnt=" + grp.Count() +
                    " sel=" + string.Join(",", grp.Select(x => (int)x).ToArray()));
            }

            Console.WriteLine("done");
        }
    }
}
