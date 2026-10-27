//------------------------------------------------------------------------------
// loud-linq-grpdecops.cs -- e2e reference case for the DECIMAL-bucket operator
// surface (impl-linq2 B-3): the remaining Enumerable operators over a
// System.Decimal GroupBy bucket. Each entry snapshots the bucket's decimal
// cells and reuses the DECIMAL-SOURCE kernel (net48 Enumerable.cs:591-1213 /
// 1341-1352 / 605-661 / 732-744). Covers Where / First(pred) / LastOrDefault /
// Single / SingleOrDefault(pred) / ElementAt / Reverse / Take / Skip /
// TakeWhile / SkipWhile / Distinct / Except / Concat / DefaultIfEmpty /
// SequenceEqual / LongCount / Count(pred) / Any(pred) / All / OrderBy with
// int32 and decimal keys. Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpDecOps
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static void Dump(string label, System.Collections.Generic.IEnumerable<decimal> xs)
        {
            Console.WriteLine(label + "[" + string.Join(",", xs.Select(x => D(x))) + "]");
        }

        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m, 4m, 5m, 6m };
            var g = a.GroupBy(x => (int)x % 3);
            System.Linq.IGrouping<int, decimal> bucket = null;
            System.Linq.IGrouping<int, decimal> zeroBucket = null;
            foreach (var grp in g)
            {
                if (grp.Key == 1) bucket = grp;
                if (grp.Key == 0) zeroBucket = grp;
            }

            Dump("where:", bucket.Where(x => x > 2m));
            Console.WriteLine("firstp=" + D(bucket.First(x => x > 2m)));
            Console.WriteLine("lastod=" + D(bucket.LastOrDefault(x => x > 99m)));
            Console.WriteLine("single=" + D(zeroBucket.Single(x => x == 3m)));
            Console.WriteLine("singleod=" + D(bucket.SingleOrDefault(x => x == 99m)));
            Console.WriteLine("elemat=" + D(bucket.ElementAt(1)));
            Dump("reverse:", bucket.Reverse());
            Dump("take:", bucket.Take(2));
            Dump("skip:", bucket.Skip(2));
            Dump("takewhile:", bucket.TakeWhile(x => x < 5m));
            Dump("skipwhile:", bucket.SkipWhile(x => x < 5m));
            Dump("distinct:", bucket.Distinct());
            Dump("except:", bucket.Except(new decimal[] { 4m }));
            Dump("concat:", bucket.Concat(new decimal[] { 7m, 8m }));
            Dump("die2:", new decimal[] { }.DefaultIfEmpty());
            Dump("die3:", bucket.DefaultIfEmpty(0m));
            Console.WriteLine("seq=" + (bucket.SequenceEqual(new decimal[] { 1m, 4m })
                ? "1" : "0"));
            Console.WriteLine("lc=" + bucket.LongCount());
            Console.WriteLine("cp=" + bucket.Count(x => x > 2m));
            Console.WriteLine("ap=" + (bucket.Any(x => x == 4m) ? "1" : "0"));
            Console.WriteLine("all=" + (bucket.All(x => x > 0m) ? "1" : "0"));
            Dump("obk:", bucket.OrderBy(x => -x));
            Dump("obd:", bucket.OrderByDescending(x => x));
            Dump("obdec:", bucket.OrderBy(x => 100m - x * 10m));

            Console.WriteLine("done");
        }
    }
}
