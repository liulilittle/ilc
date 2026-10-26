//------------------------------------------------------------------------------
// loud-linq-grpdec.cs -- e2e reference case for the DECIMAL-bucket GroupBy
// operator family (net48 Sum/Min/Max/Average over an IGrouping<decimal> bucket,
// both the source-only and the decimal-selector forms).
//
// The bucket holds 16-byte System.Decimal cells, so the int32 operator family
// would reinterpret every cell as an int32 (a silent wrong value). The decimal
// bucket kernels run the decimal array kernels over the bucket's exact-size
// element snapshot:
//   Sum()/Min()/Max()/Average()        -> il2cpp_linq_grouping_*_dec
//   Sum(x=>x+1)/Min/Max/Average(sel)   -> il2cpp_linq_grouping_*_dec_sel
// Before this change the selector forms LOUD-rejected ("the int32
// predicate/selector operators have no decimal bucket kernel").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqGrpDec
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m, 4m };
            var g = a.GroupBy(x => (int)x % 2);
            foreach (var grp in g)
            {
                Console.WriteLine("key=" + grp.Key +
                    " sum=" + D(grp.Sum()) +
                    " selsum=" + D(grp.Sum(x => x + 1m)) +
                    " selmin=" + D(grp.Min(x => x + 1m)) +
                    " selmax=" + D(grp.Max(x => x + 1m)) +
                    " selavg=" + D(grp.Average(x => x + 1m)));
            }
            Console.WriteLine("done");
        }
    }
}
