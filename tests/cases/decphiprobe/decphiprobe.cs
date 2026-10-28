//------------------------------------------------------------------------------
// decphiprobe.cs -- e2e reference case for DECIMAL branch-merge phi temps in
// branchy lambda bodies: Roslyn duplicates the int->decimal conversion INTO
// each branch arm, so the merged accumulator copy is a decimal stack value.
// The stack-fold previously typed the branch-join temp int32_t (see
// docs/audit/b5-gaps.md 11.5) and icall_decimal_add then received an
// int32_t* -- a C++ compile error. Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class DecPhiProbe
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            // ---- ternary producing decimal inside the lambda ----
            Guid[] gs = { Guid.Empty, new Guid("11111111-2222-3333-4444-555555555555"), Guid.Empty };
            // 1 + 100 + 1 = 102
            Console.WriteLine("guid_ternary=" + D(gs.Aggregate(0m, (acc, g) => acc + (g == Guid.Empty ? 1m : 100m))));

            // ---- if/else producing decimal inside the lambda ----
            int[] flags = { 0, 1, 2, 0 };
            // (1+2+3+1) * 1.5 = 10.5
            Console.WriteLine("flags_ifelse=" + D(flags.Aggregate(0m, (acc, f) => { if (f == 0) { return acc + 1.5m; } else { return acc + f * 1.5m; } })));

            // ---- logical short-circuit producing a decimal operand ----
            // (2 + 5 + 2) = 9
            Console.WriteLine("guid_shortcircuit=" + D(gs.Aggregate(0m, (acc, g) => acc + (g == Guid.Empty ? 2m : 5m))));

            // ---- control: branch-free decimal fold (worked before the fix) ----
            // 7 + 3 + 7 = 17
            Console.WriteLine("guid_branchfree=" + D(gs.Aggregate(0m, (acc, g) => acc + 3.5m)));

            // ---- control: branch on int32 result, decimal merged AFTER ----
            // (10 + 20 + 10 + 10) = 50
            Console.WriteLine("flags_ternary_i4=" + D(flags.Aggregate(0m, (acc, f) => acc + (f == 0 ? 10 : 20))));

            // ---- direct ternary expression (not a lambda) ----
            Guid g2 = Guid.Empty;
            decimal picked = g2 == Guid.Empty ? 11.5m : 99.5m;
            // 50 + 11.5 = 61.5
            Console.WriteLine("direct_ternary=" + D(flags_ternary_i4_sum(picked)));

            Console.WriteLine("done");
        }

        private static decimal flags_ternary_i4_sum(decimal seed)
        {
            return seed + 50m;
        }
    }
}
