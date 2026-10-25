//------------------------------------------------------------------------------
// loud-linq-fam.cs -- e2e reference case for the FAMILY-GENERIC LINQ surface
// (linq_family.h) over the int64 / double / narrow array element families.
//
// The runtime already ships one cell-descriptor kernel per operator
// (il2cpp_linq_take_family / _skip_family / _reverse_family / _concat_family /
// _distinct_family / _except_family / _union_family / _intersect_family /
// _default_if_empty_family / _long_count_family / _sequence_equal_family /
// _contains_family_* / _element_at_fam_*), but the transpiler's wide/narrow
// dispatch (MethodMap.Linq.cs TryMapLinqWide) did not wire them, so every
// one of these operators over a NON-int32 array LOUD-rejected.
//
// This case drives the wired operators over long[] and double[] sources and
// asserts exact element VALUES plus the boundary semantics (net48):
//   * Take/Skip slice clamps (negative -> empty / full copy)
//   * Reverse order
//   * Concat order + length
//   * Distinct first-appearance dedup
//   * Except / Intersect / Union set algebra
//   * DefaultIfEmpty on an empty source
//   * LongCount int64 result
//   * SequenceEqual element-wise equality
//   * Contains ordinal equality
//   * ElementAt index (in range + the out-of-range throw)
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqFam
    {
        public static void Main()
        {
            long[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
            long[] empty = new long[0];
            double[] dnums = { 1.5, 2.5, 1.5, 3.5, 2.5 };

            // ---- Take / Skip over long[] ----
            long[] t3 = nums.Take(3).ToArray();
            Console.WriteLine("take3=" + (t3.Length == 3 && t3[0] == 5 && t3[2] == 9));
            Console.WriteLine("take0=" + (nums.Take(0).ToArray().Length == 0));
            Console.WriteLine("takeneg=" + (nums.Take(-1).ToArray().Length == 0));
            Console.WriteLine("take99=" + (nums.Take(99).ToArray().Length == 9));
            long[] s3 = nums.Skip(3).ToArray();
            Console.WriteLine("skip3=" + (s3.Length == 6 && s3[0] == 1 && s3[5] == 6));
            Console.WriteLine("skipneg=" + (nums.Skip(-1).ToArray().Length == 9 && nums.Skip(-1).ToArray()[0] == 5));
            Console.WriteLine("skip99=" + (nums.Skip(99).ToArray().Length == 0));
            long[] tksk = nums.Take(6).Skip(2).ToArray();
            Console.WriteLine("tksk=" + (tksk.Length == 4 && tksk[0] == 9 && tksk[3] == 2));

            // ---- Reverse over long[] ----
            long[] rev = nums.Reverse().ToArray();
            Console.WriteLine("rev=" + (rev.Length == 9 && rev[0] == 6 && rev[8] == 5));
            Console.WriteLine("revempty=" + (empty.Reverse().ToArray().Length == 0));

            // ---- Concat over long[] ----
            long[] cat = nums.Take(4).Concat(nums.Skip(4)).ToArray();
            Console.WriteLine("concat=" + (cat.Length == 9 && cat[0] == 5 && cat[4] == 7 && cat[8] == 6));

            // ---- Distinct over double[] (first-appearance) ----
            double[] dist = dnums.Distinct().ToArray();
            Console.WriteLine("distinct=" + (dist.Length == 3 && dist[0] == 1.5 && dist[1] == 2.5 && dist[2] == 3.5));

            // ---- Except / Intersect / Union over long[] ----
            long[] a = { 1, 2, 3, 4 };
            long[] b = { 3, 4, 5, 6 };
            long[] exc = a.Except(b).ToArray();
            Console.WriteLine("except=" + (exc.Length == 2 && exc[0] == 1 && exc[1] == 2));
            long[] inter = a.Intersect(b).ToArray();
            Console.WriteLine("intersect=" + (inter.Length == 2 && inter[0] == 3 && inter[1] == 4));
            long[] uni = a.Union(b).ToArray();
            Console.WriteLine("union=" + (uni.Length == 6 && uni[0] == 1 && uni[5] == 6));

            // ---- DefaultIfEmpty over an empty long[] ----
            long[] die = empty.DefaultIfEmpty().ToArray();
            Console.WriteLine("defaultifempty=" + (die.Length == 1 && die[0] == 0));
            long[] diefull = nums.DefaultIfEmpty().ToArray();
            Console.WriteLine("defaultifemptyfull=" + (diefull.Length == 9 && diefull[0] == 5));

            // ---- LongCount over long[] ----
            Console.WriteLine("longcount=" + (nums.LongCount() == 9L));
            Console.WriteLine("longcountempty=" + (empty.LongCount() == 0L));

            // ---- SequenceEqual over long[] ----
            Console.WriteLine("seqeq=" + nums.SequenceEqual(nums.ToArray()));
            Console.WriteLine("seqneq=" + (!a.SequenceEqual(b)));

            // ---- Contains over long[] ----
            Console.WriteLine("contains=" + nums.Contains(7L));
            Console.WriteLine("containsno=" + (!nums.Contains(99L)));

            // ---- ElementAt over long[] (in range + the out-of-range throw) ----
            Console.WriteLine("elementat=" + (nums.ElementAt(3) == 1));
            bool aoo = false;
            try { long x = nums.ElementAt(99); }
            catch (ArgumentOutOfRangeException) { aoo = true; }
            Console.WriteLine("elementatthrow=" + aoo);
        }
    }
}
