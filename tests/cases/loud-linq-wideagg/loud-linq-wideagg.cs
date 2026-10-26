//------------------------------------------------------------------------------
// loud-linq-wideagg.cs -- e2e reference case for the WIDE-source selector
// aggregates (net48 Sum/Min/Max/Average<TSource>(Func<TSource,TResult>) with
// TSource = long / float / double).
//
// The source element crosses in its own slot (int64 for long, the F-stack
// carrier for float/double); the selector's result crosses in the int32 /
// int64 / F-stack slot. Before this change every one of these LOUD-rejected
// ("the stage-1 selector-aggregate surface is int32 (0x08) element-family").
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqWideAgg
    {
        public static void Main()
        {
            long[] longs = { 5L, -3L, 9L, 1L };
            double[] doubles = { 1.5, -2.5, 3.25, 0.0 };
            float[] floats = { 1.5f, -2.5f, 3.25f };

            // ===== long source: identity projection (long result) =====
            Console.WriteLine("[l-sum]=" + longs.Sum(x => x));
            Console.WriteLine("[l-min]=" + longs.Min(x => x));
            Console.WriteLine("[l-max]=" + longs.Max(x => x));
            Console.WriteLine("[l-avg]=" + longs.Average(x => x));

            // ===== long source: int32 projection =====
            Console.WriteLine("[l-sum-i4]=" + longs.Sum(x => (int)x));
            Console.WriteLine("[l-avg-i4]=" + longs.Average(x => (int)x));

            // ===== double source: identity projection (double result) =====
            Console.WriteLine("[d-sum]=" + doubles.Sum(x => x));
            Console.WriteLine("[d-min]=" + doubles.Min(x => x));
            Console.WriteLine("[d-max]=" + doubles.Max(x => x));
            Console.WriteLine("[d-avg]=" + doubles.Average(x => x));

            // ===== double source: int32 projection =====
            Console.WriteLine("[d-sum-i4]=" + doubles.Sum(x => (int)x));

            // ===== float source: identity projection (float result) =====
            Console.WriteLine("[f-sum]=" + floats.Sum(x => x));
            Console.WriteLine("[f-avg]=" + floats.Average(x => x));

            // ===== empty-source contracts (net48): Min/Max/Average throw,
            // Sum returns the identity.
            long[] emptyL = new long[0];
            double[] emptyD = new double[0];
            Console.WriteLine("[l-sum-empty]=" + emptyL.Sum(x => x));
            Console.WriteLine("[l-avg-empty-throws]=" + ThrowsInvalidOp(() => emptyL.Average(x => x)));
            Console.WriteLine("[l-min-empty-throws]=" + ThrowsInvalidOp(() => (double)emptyL.Min(x => x)));
            Console.WriteLine("[d-sum-empty]=" + emptyD.Sum(x => x));
            Console.WriteLine("[d-min-empty-throws]=" + ThrowsInvalidOp(() => emptyD.Min(x => x)));

            Console.WriteLine("done");
        }

        private static bool ThrowsInvalidOp(Func<double> f)
        {
            try { f(); return false; }
            catch (InvalidOperationException) { return true; }
        }
    }
}
