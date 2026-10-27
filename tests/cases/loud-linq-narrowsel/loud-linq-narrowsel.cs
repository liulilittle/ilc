//------------------------------------------------------------------------------
// loud-linq-narrowsel.cs -- e2e for the NARROW-source selector aggregates
// (impl-linq3 A): net48 Sum/Min/Max/Average<TSource,TResult> over sbyte / byte
// / short / ushort / uint / char sources with int / long / float / double
// projections. Before this change every narrow source with a selector
// LOUD-rejected ("the wide-source selector-aggregate surface covers
// long/float/double elements"). Covers the normal path (all six sources, all
// four projections), single-element sources, the empty-source
// InvalidOperationException for Min/Max/Average, the checked int32 sum
// overflow, and the uint bit-pattern widening. Every printed value is
// deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqNarrowSel
    {
        private static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        public static void Main()
        {
            sbyte[] sb = { -3, 5, -1, 9 };
            Console.WriteLine("sb si=" + sb.Sum(x => (int)x) + " mi=" + sb.Min(x => (int)x) +
                " ma=" + sb.Max(x => (int)x) + " av=" + R(sb.Average(x => (int)x)) +
                " sl=" + sb.Sum(x => (long)x) + " sd=" + R(sb.Sum(x => (double)x)) +
                " sf=" + sb.Sum(x => (float)x).ToString(CultureInfo.InvariantCulture) +
                " af=" + sb.Average(x => (float)x).ToString(CultureInfo.InvariantCulture));

            byte[] b = { 1, 250, 4 };
            Console.WriteLine("b si=" + b.Sum(x => (int)x) + " mi=" + b.Min(x => (int)x) +
                " ma=" + b.Max(x => (int)x) + " av=" + R(b.Average(x => (int)x)) +
                " sl=" + b.Sum(x => (long)x) + " md=" + R(b.Min(x => (double)x)) +
                " sd=" + R(b.Sum(x => (double)x)));

            short[] sh = { -300, 700, 42 };
            Console.WriteLine("sh si=" + sh.Sum(x => (int)x) + " mi=" + sh.Min(x => (int)x) +
                " ma=" + sh.Max(x => (int)x) + " av=" + R(sh.Average(x => (int)x)) +
                " sl=" + sh.Sum(x => (long)x) + " sf=" + sh.Sum(x => (float)x).ToString(CultureInfo.InvariantCulture));

            ushort[] us = { 1, 60000, 3 };
            Console.WriteLine("us si=" + us.Sum(x => (int)x) + " ma=" + us.Max(x => (int)x) +
                " sl=" + us.Sum(x => (long)x) + " ad=" + R(us.Average(x => (double)x)));

            uint[] ui = { 1u, 0xFFFFFFFFu, 3u };
            // The uint element crosses the int32 slot as its BIT PATTERN (the
            // narrow value model), so (int)0xFFFFFFFF == -1 on both sides.
            Console.WriteLine("ui si=" + ui.Sum(x => (int)x) + " mi=" + ui.Min(x => (int)x) +
                " sl=" + ui.Sum(x => (long)x) + " av=" + R(ui.Average(x => (long)x)));

            char[] c = { 'a', 'C', 'z' };
            Console.WriteLine("c si=" + c.Sum(x => (int)x) + " mi=" + c.Min(x => (int)x) +
                " ma=" + c.Max(x => (int)x) + " av=" + R(c.Average(x => (int)x)) +
                " sd=" + R(c.Sum(x => (double)x)));

            // Single-element sources.
            short[] one = { 7 };
            Console.WriteLine("one si=" + one.Sum(x => (int)x) + " mi=" + one.Min(x => (int)x) +
                " ma=" + one.Max(x => (int)x) + " av=" + R(one.Average(x => (int)x)) +
                " sf=" + one.Sum(x => (float)x).ToString(CultureInfo.InvariantCulture));

            // Empty sources: Sum returns the identity; Min/Max/Average throw.
            byte[] empty = { };
            Console.WriteLine("empty sum=" + empty.Sum(x => (int)x) +
                " suml=" + empty.Sum(x => (long)x));
            bool minThrow = false, avgThrow = false, ovfThrow = false;
            try { empty.Min(x => (int)x); }
            catch (InvalidOperationException) { minThrow = true; }
            try { empty.Average(x => (float)x); }
            catch (InvalidOperationException) { avgThrow = true; }
            Console.WriteLine("minThrow=" + minThrow + " avgThrow=" + avgThrow);

            // The checked int32 sum: two int.MaxValue projections overflow.
            byte[] ovf = { 1, 1 };
            try { Console.WriteLine("ovf sum=" + ovf.Sum(x => int.MaxValue)); }
            catch (OverflowException) { ovfThrow = true; }
            Console.WriteLine("ovfThrow=" + ovfThrow);

            Console.WriteLine("done");
        }
    }
}
