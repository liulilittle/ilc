using System;

namespace DecPhi
{
    public static class Program
    {
        // The branchy decimal lambda shape from docs/audit/b5-gaps.md 11.5:
        // Roslyn duplicates the int->decimal conversion INTO each arm, so the
        // merged accumulator copy is a decimal stack value at the join.
        public static decimal Pick(Guid g, decimal acc)
        {
            return acc + (g == Guid.Empty ? 1m : 100m);
        }

        public static void Main()
        {
            Console.WriteLine(Pick(Guid.Empty, 2.5m));
            Console.WriteLine(Pick(new Guid("11111111-2222-3333-4444-555555555555"), 2.5m));
        }
    }
}
