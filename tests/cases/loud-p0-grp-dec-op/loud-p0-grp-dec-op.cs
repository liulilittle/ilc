using System;
using System.Linq;

//------------------------------------------------------------------------------
// loud-p0-grp-dec-op -- p0grp companion: the int32-only bucket OPERATORS over a
// DECIMAL-source bucket.
//
// `decimal[].GroupBy(x => (int)x)` is wired (il2cpp_linq_group_by_dec_key), and
// its buckets hold 16-byte System.Decimal cells. The per-bucket operator family
// (Where / Select / Sum / Min / Max / First / ElementAt / Contains /
// Count(pred) / Any(pred) / All) is int32-kernel only: reading a 16-byte cell
// through those kernels would silently reinterpret every decimal as an int32.
// They therefore raise NotSupportedException at RUN time
// (RequireI4GroupElements / the grouping_sum guard in linq_map.cpp).
//
// Count() / Any() / get_Key / ToArray() / foreach stay family-agnostic and are
// covered by tests/cases/p0grp.
//
// This case is a LOUD contract: the runtime exit code is non-zero and the
// output carries "Unhandled managed exception" (see scripts/sweep-exclude.txt
// and scripts/loud-gate.ps1).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class LoudP0GrpDecOp
    {
        public static void Main()
        {
            decimal[] ds = { 3.7m, 1.2m, 3.1m };

            // First bucket: the decimals sharing key 3.
            foreach (var g in ds.GroupBy(x => (int)x))
            {
                if (g.Key == 3)
                {
                    // int32-only bucket operator over a decimal bucket.
                    Console.WriteLine("[decop] sum=" + g.Sum());
                }
            }
        }
    }
}
