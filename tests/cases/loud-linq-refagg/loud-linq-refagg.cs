//------------------------------------------------------------------------------
// loud-linq-refagg.cs -- e2e reference case for the reference-source selector
// aggregates (net48 Sum/Min/Max/Average<TSource>(Func<TSource,TResult>)).
//
// The source is a string[] (a REFERENCE family); the selector's RESULT slot
// picks the kernel (linq_map.h):
//   Func<string,int>    -> _ref_i4 (Sum/Average/Min/Max)
//   Func<string,long>   -> _ref_i8
//   Func<string,double> -> _ref_d
//   Func<string,float>  -> _ref_f
// Before this change only Min/Max with an int32-slot projection were wired;
// Sum/Average and the int64/double/float projections LOUD-rejected.
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqRefAgg
    {
        private static int Len(string s) { return s.Length; }
        private static long LenLong(string s) { return (long)s.Length; }
        private static double LenDouble(string s) { return (double)s.Length; }
        private static float LenFloat(string s) { return (float)s.Length; }

        public static void Main()
        {
            string[] words = { "a", "bb", "ccc", "dddd", "eeeee" };

            // ===== int32-slot projection =====
            Console.WriteLine("[sum-i4]=" + words.Sum(Len));
            Console.WriteLine("[min-i4]=" + words.Min(Len));
            Console.WriteLine("[max-i4]=" + words.Max(Len));
            Console.WriteLine("[avg-i4]=" + words.Average(Len));

            // ===== int64 projection =====
            Console.WriteLine("[sum-i8]=" + words.Sum(LenLong));
            Console.WriteLine("[min-i8]=" + words.Min(LenLong));
            Console.WriteLine("[max-i8]=" + words.Max(LenLong));
            Console.WriteLine("[avg-i8]=" + words.Average(LenLong));

            // ===== double projection =====
            Console.WriteLine("[sum-d]=" + words.Sum(LenDouble));
            Console.WriteLine("[min-d]=" + words.Min(LenDouble));
            Console.WriteLine("[max-d]=" + words.Max(LenDouble));
            Console.WriteLine("[avg-d]=" + words.Average(LenDouble));

            // ===== float projection =====
            Console.WriteLine("[sum-f]=" + words.Sum(LenFloat));
            Console.WriteLine("[avg-f]=" + words.Average(LenFloat));

            // ===== empty-source contracts (net48): Min/Max/Average throw,
            // Sum returns the identity. Probed through a try/catch.
            string[] empty = new string[0];
            Console.WriteLine("[sum-empty]=" + empty.Sum(Len));
            Console.WriteLine("[avg-empty-throws]=" + ThrowsInvalidOp(() => empty.Average(Len)));
            Console.WriteLine("[min-empty-throws]=" + ThrowsInvalidOp(() => (double)empty.Min(Len)));
            Console.WriteLine("[max-empty-throws]=" + ThrowsInvalidOp(() => (double)empty.Max(Len)));

            Console.WriteLine("done");
        }

        private static bool ThrowsInvalidOp(Func<double> f)
        {
            try { f(); return false; }
            catch (InvalidOperationException) { return true; }
        }
    }
}
