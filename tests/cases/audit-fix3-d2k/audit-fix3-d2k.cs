// audit-fix3-d2k -- the SOURCE-ONLY aggregates (Sum / Min / Max / Average) over a
// List<decimal>. LinqListKind answers "12" for a _System.Decimal list, but the
// source-only suffix table in TryMapLinqList only covered "2"/"3"/"4", so every
// other kind fell to the int32 entry: the fold read 4-byte slices out of the
// 16-byte decimal cells and returned an int32 into a decimal slot.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2k
    {
        private static void Run(string tag, List<decimal> l)
        {
            Console.WriteLine("[" + tag + "_sum]=" + l.Sum().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[" + tag + "_min]=" + l.Min().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[" + tag + "_max]=" + l.Max().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[" + tag + "_avg]=" + l.Average().ToString(CultureInfo.InvariantCulture));
        }

        // net48: Min/Max/Average throw InvalidOperationException on an empty
        // source. The probe prints whether the throw happened (and its type).
        private static string Throws(Action a)
        {
            try
            {
                a();
                return "no-throw";
            }
            catch (Exception e)
            {
                return e.GetType().Name;
            }
        }

        public static void Main()
        {
            Run("a", new List<decimal> { 1.5m, 2.25m, -0.75m });

            List<decimal> conv = new decimal[] { 100.5m, 0.25m }.ToList();
            Run("b", conv);

            // An EMPTY list: Sum answers 0, Min/Max/Average throw
            // InvalidOperationException (net48). Printed as a tag so the empty
            // path is covered without an unhandled exception.
            List<decimal> none = new List<decimal>();
            Console.WriteLine("[c_sum]=" + none.Sum().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[c_min_throws]=" + Throws(() => none.Min()));
            Console.WriteLine("[c_max_throws]=" + Throws(() => none.Max()));
            Console.WriteLine("[c_avg_throws]=" + Throws(() => none.Average()));

            // The decimal LIST must keep decimal arithmetic: a value that does
            // not fit an int32 and carries a fractional part proves the cell is
            // read at its real width.
            List<decimal> big = new List<decimal> { 3000000000.5m, 2000000000.25m };
            Console.WriteLine("[big_sum]=" + big.Sum().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[big_avg]=" + big.Average().ToString(CultureInfo.InvariantCulture));

            // Negatives exercise Min/Max sign handling.
            List<decimal> neg = new List<decimal> { -5.5m, -1.25m, -9.75m };
            Console.WriteLine("[neg_min]=" + neg.Min().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[neg_max]=" + neg.Max().ToString(CultureInfo.InvariantCulture));
        }
    }
}
