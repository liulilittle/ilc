//------------------------------------------------------------------------------
// loud-linq-refsel.cs -- e2e reference case for the reference-source Select with
// a VALUE result (net48 Select<TSource,TResult> over a reference source).
//
// The source is a string[] (a REFERENCE family); the TResult picks the kernel
// (linq_map.h):
//   Select<string,int>    -> il2cpp_linq_select_ref_i4
//   Select<string,long>   -> il2cpp_linq_select_ref_i8
//   Select<string,double> -> il2cpp_linq_select_ref_d
//   Select<string,float>  -> il2cpp_linq_select_ref_f (F-stack carrier)
// and the (element, index) overloads:
//   Select<string,int,int>    -> il2cpp_linq_select_idx_ref_i4
//   Select<string,int,long>   -> il2cpp_linq_select_idx_ref_i8
// Before this change the non-reference non-decimal results LOUD-rejected.
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqRefSel
    {
        private static string Join(int[] a) { return string.Join(",", a); }
        private static string JoinL(long[] a) { return string.Join(",", a); }
        private static string JoinD(double[] a) { return string.Join(",", a); }
        private static string JoinF(float[] a) { return string.Join(",", a); }

        public static void Main()
        {
            string[] words = { "a", "bb", "ccc", "dddd" };

            // ===== non-index overloads =====
            Console.WriteLine("[i4]=" + Join(words.Select(x => x.Length).ToArray()));
            Console.WriteLine("[i8]=" + JoinL(words.Select(x => (long)x.Length).ToArray()));
            Console.WriteLine("[d]=" + JoinD(words.Select(x => (double)x.Length).ToArray()));

            // ===== (element, index) overloads =====
            Console.WriteLine("[idx-i4]=" + Join(words.Select((x, i) => x.Length + i).ToArray()));
            Console.WriteLine("[idx-i8]=" + JoinL(words.Select((x, i) => (long)(x.Length + i)).ToArray()));
            Console.WriteLine("[idx-d]=" + JoinD(words.Select((x, i) => (double)(x.Length + i)).ToArray()));

            // ===== a reference result still rides the ref kernel (regression) =====
            Console.WriteLine("[ref]=" + string.Join(",", words.Select(x => x + "!").ToArray()));

            // ===== an empty source =====
            string[] empty = new string[0];
            Console.WriteLine("[empty]=" + (empty.Select(x => x.Length).Count() == 0));

            Console.WriteLine("done");
        }
    }
}
