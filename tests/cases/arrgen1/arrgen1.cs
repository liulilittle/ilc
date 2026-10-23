//------------------------------------------------------------------------------
// arrgen1.cs -- Array predicate-family GENERIC ELEMENT-FAMILY probe.
//
// The M6/arrfuncs surface wired only the int32 element family
// (icall_array_find_i4 / _exists_i4 / _true_for_all_i4 / _find_all_i4 /
// _find_index / _resize_i4). net48 has these methods for EVERY T, so this
// probe drives the non-int32 families:
//
//   int64    (0x0A)   long[]
//   string   (0x0E)   string[]  (reference elements)
//   double   (0x0D)   double[]
//
// Every line prints a label + bool/int; net48 is the byte oracle.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ArrGen1
    {
        private static bool IsBig(long x) { return x > 100; }
        private static bool IsPear(string s) { return s == "pear"; }
        private static bool IsNeg(double d) { return d < 0; }
        private static bool IsNull(string s) { return s == null; }

        public static void Main()
        {
            // ---- A. Array.Find<long> ----
            long[] la = { 10L, 200L, 3000L };
            Console.WriteLine("f_i8=" + (Array.Find(la, IsBig) == 200L));
            Console.WriteLine("f_i8_miss=" + (Array.Find(la, x => x < 0) == 0L));
            long[] le = new long[0];
            Console.WriteLine("f_i8_empty=" + (Array.Find(le, IsBig) == 0L));

            // ---- B. Array.Find<string> ----
            string[] sa = { "apple", null, "pear" };
            Console.WriteLine("f_str=" + (Array.Find(sa, IsPear) == "pear"));
            Console.WriteLine("f_str_null=" + (Array.Find(sa, IsNull) == null));
            Console.WriteLine("f_str_miss=" + (Array.Find(sa, x => x == "zzz") == null));

            // ---- C. Array.Find<double> ----
            double[] da = { 1.5, -2.5, 3.5 };
            Console.WriteLine("f_d=" + (Array.Find(da, IsNeg) == -2.5));
            Console.WriteLine("f_d_miss=" + (Array.Find(da, x => x > 100) == 0.0));

            // ---- D. Array.FindIndex<long> / <string> ----
            Console.WriteLine("fi_i8=" + Array.FindIndex(la, IsBig));
            Console.WriteLine("fi_i8_miss=" + Array.FindIndex(la, x => x < 0));
            Console.WriteLine("fi_str=" + Array.FindIndex(sa, IsPear));
            Console.WriteLine("fi_str_3=" + Array.FindIndex(sa, 1, IsPear));
            Console.WriteLine("fi_str_4=" + Array.FindIndex(sa, 1, 2, IsPear));

            // ---- E. Array.Exists<long> / <string> / <double> ----
            Console.WriteLine("e_i8=" + Array.Exists(la, IsBig));
            Console.WriteLine("e_i8_f=" + Array.Exists(la, x => x < 0));
            Console.WriteLine("e_str=" + Array.Exists(sa, IsPear));
            Console.WriteLine("e_d=" + Array.Exists(da, IsNeg));
            Console.WriteLine("e_empty=" + Array.Exists(le, IsBig));

            // ---- F. Array.TrueForAll<double> / <long> ----
            Console.WriteLine("t_d=" + Array.TrueForAll(da, x => x < 100));
            Console.WriteLine("t_i8=" + Array.TrueForAll(la, IsBig));
            Console.WriteLine("t_empty=" + Array.TrueForAll(le, x => false));

            // ---- G. Array.FindAll<long> / <string> ----
            long[] fa = Array.FindAll(la, IsBig);
            Console.WriteLine("fa_i8_len=" + fa.Length);
            Console.WriteLine("fa_i8_v=" + (fa[0] == 200L && fa[1] == 3000L));
            string[] fs = Array.FindAll(sa, x => x != null);
            Console.WriteLine("fa_str_len=" + fs.Length);
            Console.WriteLine("fa_str_v=" + (fs[0] == "apple" && fs[1] == "pear"));
            Console.WriteLine("fa_empty_len=" + Array.FindAll(le, IsBig).Length);

            // ---- H. Array.Resize<long> / <string> ----
            long[] rl = { 1L, 2L, 3L };
            Array.Resize(ref rl, 5);
            Console.WriteLine("rs_i8_len=" + rl.Length);
            Console.WriteLine("rs_i8_pad=" + (rl[3] == 0L && rl[4] == 0L));
            Console.WriteLine("rs_i8_keep=" + (rl[0] == 1L && rl[2] == 3L));
            Array.Resize(ref rl, 2);
            Console.WriteLine("rs_i8_shrink=" + (rl.Length == 2 && rl[1] == 2L));
            string[] rs = { "a", "b" };
            Array.Resize(ref rs, 3);
            Console.WriteLine("rs_str_len=" + rs.Length);
            Console.WriteLine("rs_str_pad=" + (rs[2] == null));
            Console.WriteLine("rs_str_keep=" + (rs[0] == "a" && rs[1] == "b"));

            // ---- I. exception contracts (per family) ----
            bool ane = false;
            try { Array.Find((long[])null, IsBig); } catch (ArgumentNullException) { ane = true; }
            Console.WriteLine("ane_i8=" + ane);
            ane = false;
            try { Array.Exists((string[])null, IsPear); } catch (ArgumentNullException) { ane = true; }
            Console.WriteLine("ane_str=" + ane);
            bool aoo = false;
            try { long[] t = { 1L }; Array.Resize(ref t, -1); } catch (ArgumentOutOfRangeException) { aoo = true; }
            Console.WriteLine("aoo_i8=" + aoo);
            Console.WriteLine("done");
        }
    }
}
