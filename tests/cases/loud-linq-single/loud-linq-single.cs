//------------------------------------------------------------------------------
// loud-linq-single.cs -- e2e reference case for the SOURCE-ONLY Single /
// SingleOrDefault operators over the int32 / int64 / double / string element
// families.
//
// The runtime already ships ONE family kernel per result ABI (linq_family.h
// il2cpp_linq_single_fam_i4/_i8/_d/_ref and single_or_default_fam_*) but the
// transpiler did NOT wire them: long[].Single() / double[].SingleOrDefault()
// LOUD-rejected with "the stage-1 runtime LINQ surface is int32 (0x08)
// element-family for this operator".
//
// This case drives the wired operators and asserts the net48 semantics:
//   * Single: exactly one element; empty OR >1 -> InvalidOperationException
//   * SingleOrDefault: default(T) for an empty source; >1 -> throws
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqSingle
    {
        public static void Main()
        {
            long[] lone = { 42L };
            long[] lmany = { 1L, 2L, 3L };
            long[] lempty = new long[0];
            double[] done = { 3.5 };
            double[] dempty = new double[0];
            string[] sone = { "solo" };
            string[] smany = { "a", "b" };
            string[] sempty = new string[0];
            int[] ione = { 7 };
            int[] imany = { 1, 2 };

            // ---- Single over the value families ----
            Console.WriteLine("single_i4=" + (ione.Single() == 7));
            Console.WriteLine("single_i8=" + (lone.Single() == 42L));
            Console.WriteLine("single_d=" + (done.Single() == 3.5));

            // ---- Single over a reference family ----
            Console.WriteLine("single_str=" + (sone.Single() == "solo"));

            // ---- SingleOrDefault: default(T) on an empty source ----
            Console.WriteLine("sod_i8_empty=" + (lempty.SingleOrDefault() == 0L));
            Console.WriteLine("sod_d_empty=" + (dempty.SingleOrDefault() == 0.0));
            Console.WriteLine("sod_str_empty=" + (sempty.SingleOrDefault() == null));
            Console.WriteLine("sod_i4_empty=" + (new int[0].SingleOrDefault() == 0));

            // ---- SingleOrDefault: the single element ----
            Console.WriteLine("sod_i8_one=" + (lone.SingleOrDefault() == 42L));
            Console.WriteLine("sod_str_one=" + (sone.SingleOrDefault() == "solo"));

            // ---- Single: empty source throws InvalidOperationException ----
            bool emptyThrow = false;
            try { lempty.Single(); }
            catch (InvalidOperationException) { emptyThrow = true; }
            Console.WriteLine("single_empty_throw=" + emptyThrow);

            // ---- Single: >1 element throws InvalidOperationException ----
            bool manyThrow = false;
            try { lmany.Single(); }
            catch (InvalidOperationException) { manyThrow = true; }
            Console.WriteLine("single_many_throw=" + manyThrow);

            bool manyThrowI4 = false;
            try { imany.Single(); }
            catch (InvalidOperationException) { manyThrowI4 = true; }
            Console.WriteLine("single_many_throw_i4=" + manyThrowI4);

            // ---- SingleOrDefault: >1 element throws, empty does NOT ----
            bool sodManyThrow = false;
            try { lmany.SingleOrDefault(); }
            catch (InvalidOperationException) { sodManyThrow = true; }
            Console.WriteLine("sod_many_throw=" + sodManyThrow);

            bool sodEmptyOk = false;
            try { long x = lempty.SingleOrDefault(); sodEmptyOk = (x == 0L); }
            catch (InvalidOperationException) { sodEmptyOk = false; }
            Console.WriteLine("sod_empty_ok=" + sodEmptyOk);

            // ---- List<T> source: the list-domain single kernels ----
            var listOne = new System.Collections.Generic.List<long> { 99L };
            Console.WriteLine("single_list=" + (listOne.Single() == 99L));
            var listEmpty = new System.Collections.Generic.List<long>();
            Console.WriteLine("sod_list_empty=" + (listEmpty.SingleOrDefault() == 0L));
            var listMany = new System.Collections.Generic.List<long> { 1L, 2L };
            bool listManyThrow = false;
            try { listMany.Single(); }
            catch (InvalidOperationException) { listManyThrow = true; }
            Console.WriteLine("single_list_many_throw=" + listManyThrow);
        }
    }
}
