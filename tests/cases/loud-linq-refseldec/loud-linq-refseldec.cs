//------------------------------------------------------------------------------
// loud-linq-refseldec.cs -- e2e reference case for the two remaining
// selector-aggregate families:
//   * a REFERENCE source with a DECIMAL projection (net48
//     Sum/Min/Max/Average<TSource>(source, Func<TSource,decimal>) --
//     Enumerable.cs:1528/1735/1939/2133). Before this change the decimal
//     projection LOUD-rejected ("the reference selector-aggregate surface
//     covers an int32-slot (char/int32), int64, double or float projection
//     only").
//   * a DECIMAL GroupBy bucket with a NON-decimal projection (net48
//     Sum/Min/Max/Average<decimal>(bucket, Func<decimal,int32/int64/double>)).
//     Before this change the decimal bucket surface only accepted the decimal
//     projection.
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqRefSelDec
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            string[] words = { "1.5", "2.5", "3.5" };
            Console.WriteLine("sum=" + D(words.Sum(w => decimal.Parse(w, CultureInfo.InvariantCulture))));
            Console.WriteLine("min=" + D(words.Min(w => decimal.Parse(w, CultureInfo.InvariantCulture))));
            Console.WriteLine("max=" + D(words.Max(w => decimal.Parse(w, CultureInfo.InvariantCulture))));
            Console.WriteLine("avg=" + D(words.Average(w => decimal.Parse(w, CultureInfo.InvariantCulture))));

            decimal[] a = { 1m, 2m, 3m, 4m };
            var g = a.GroupBy(x => (int)x % 2);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key +
                    " si=" + grp.Sum(x => (int)x) +
                    " mi=" + grp.Min(x => (int)x) +
                    " md=" + D(grp.Max(x => (decimal)x)) +
                    " sl=" + grp.Sum(x => (long)x) +
                    " ad=" + grp.Average(x => (double)x).ToString("R", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("done");
        }
    }
}
