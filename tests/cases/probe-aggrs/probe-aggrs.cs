//------------------------------------------------------------------------------
// probe-aggrs -- RED/GREEN e2e probe for the two D2 surfaces that b87d225f left
// LOUD:
//
//   (1) List<T>.Aggregate(seed, func, resultSelector)  -- the 4-parameter form.
//       The runtime ships the full (accumulator, element, result) matrix
//       (il2cpp_linq_list_aggregate_seed_<acc>_<elem>_rs_<res>); the ARRAY
//       surface already had it (_rs_*). Every (element, accumulator) diagonal
//       is crossed with all six result families, plus off-diagonal pairs
//       (notably every DECIMAL-accumulator row, which exercises the T3
//       result-slot fold body).
//
//   (2) DefaultIfEmpty(source, defaultValue) -- the 2-parameter form, over
//       ARRAY sources (int/long/float/double/string/decimal) AND List<T>
//       sources. Non-empty -> the source as-is; empty -> one element holding
//       the caller's default value (net48 Enumerable.cs:1010).
//
// SCOPE NOTE (List<decimal> ELEMENT): a List<decimal> is materialized here via
// decimal[].ToList() (the listobj decimal domain). The ELEMENT-decimal row is
// probed with the DECIMAL accumulator (the D2 diagonal). A List<decimal> with a
// NON-decimal accumulator is still rejected by the pre-existing ARRAY gate
// ("decimal source with a non-decimal accumulator"), which fires on the first
// resolve pass before the List re-resolve -- a separate ARRAY-surface gap, not
// part of D2 (see docs/audit/b5-gaps.md).
//
// Every printed value is deterministic (InvariantCulture; float/double print
// their raw bit pattern so a width/ABI misread is visible).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeAggrs
    {
        private static string FB(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.ToUInt32(BitConverter.GetBytes(v), 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string DB(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.DoubleToInt64Bits(v).ToString(CultureInfo.InvariantCulture);
        }

        private static string S(string v) { return v ?? "<null>"; }
        private static string Dec(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        //======================================================================
        // (1) List<T>.Aggregate(seed, func, resultSelector)
        //======================================================================

        // element int32, accumulator int32
        private static int L_I4_I4_I4(List<int> l) { return l.Aggregate(0, (a, x) => a + x, a => a * 2); }
        private static long L_I4_I4_I8(List<int> l) { return l.Aggregate(0, (a, x) => a + x, a => (long)a * 1000000000L); }
        private static float L_I4_I4_F(List<int> l) { return l.Aggregate(0, (a, x) => a + x, a => a * 1.5f); }
        private static double L_I4_I4_D(List<int> l) { return l.Aggregate(0, (a, x) => a + x, a => a * 0.25); }
        private static decimal L_I4_I4_Dec(List<int> l) { return l.Aggregate(0, (a, x) => a + x, a => (decimal)a / 4m); }
        private static string L_I4_I4_Ref(List<int> l) { return l.Aggregate(0, (a, x) => a + x, a => "n" + a.ToString(CultureInfo.InvariantCulture)); }

        // element int64, accumulator int64
        private static int L_I8_I8_I4(List<long> l) { return l.Aggregate(0L, (a, x) => a + x, a => (int)(a % 100000)); }
        private static long L_I8_I8_I8(List<long> l) { return l.Aggregate(0L, (a, x) => a + x, a => a * 3L); }
        private static float L_I8_I8_F(List<long> l) { return l.Aggregate(0L, (a, x) => a + x, a => (float)(a % 1000)); }
        private static double L_I8_I8_D(List<long> l) { return l.Aggregate(0L, (a, x) => a + x, a => (double)(a % 1000) / 8.0); }
        private static decimal L_I8_I8_Dec(List<long> l) { return l.Aggregate(0L, (a, x) => a + x, a => (decimal)(a % 1000)); }
        private static string L_I8_I8_Ref(List<long> l) { return l.Aggregate(0L, (a, x) => a + x, a => "n" + a.ToString(CultureInfo.InvariantCulture)); }

        // element float, accumulator float
        private static int L_F_F_I4(List<float> l) { return l.Aggregate(0f, (a, x) => a + x, a => (int)(a * 10f)); }
        private static long L_F_F_I8(List<float> l) { return l.Aggregate(0f, (a, x) => a + x, a => (long)(a * 1000f)); }
        private static float L_F_F_F(List<float> l) { return l.Aggregate(0f, (a, x) => a + x, a => a * 2f); }
        private static double L_F_F_D(List<float> l) { return l.Aggregate(0f, (a, x) => a + x, a => (double)a / 4.0); }
        private static decimal L_F_F_Dec(List<float> l) { return l.Aggregate(0f, (a, x) => a + x, a => (decimal)(int)a); }
        private static string L_F_F_Ref(List<float> l) { return l.Aggregate(0f, (a, x) => a + x, a => FB(a)); }

        // element double, accumulator double
        private static int L_D_D_I4(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x, a => (int)(a * 10.0)); }
        private static long L_D_D_I8(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x, a => (long)(a * 1000.0)); }
        private static float L_D_D_F(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x, a => (float)a); }
        private static double L_D_D_D(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x, a => a / 4.0); }
        private static decimal L_D_D_Dec(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x, a => (decimal)(int)a); }
        private static string L_D_D_Ref(List<double> l) { return l.Aggregate(0.0, (a, x) => a + x, a => DB(a)); }

        // element decimal, accumulator decimal (materialized through ToList)
        private static int L_Dec_Dec_I4(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x, a => (int)(a * 10m)); }
        private static long L_Dec_Dec_I8(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x, a => (long)(a * 1000m)); }
        private static float L_Dec_Dec_F(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x, a => (float)a); }
        private static double L_Dec_Dec_D(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x, a => (double)a / 4.0); }
        private static decimal L_Dec_Dec_Dec(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x, a => a / 4m); }
        private static string L_Dec_Dec_Ref(List<decimal> l) { return l.Aggregate(0m, (a, x) => a + x, a => Dec(a)); }

        // element reference, accumulator reference
        private static int L_Ref_Ref_I4(List<string> l) { return l.Aggregate("", (a, x) => a + x, a => a.Length); }
        private static long L_Ref_Ref_I8(List<string> l) { return l.Aggregate("", (a, x) => a + x, a => (long)a.Length * 1000L); }
        private static float L_Ref_Ref_F(List<string> l) { return l.Aggregate("", (a, x) => a + x, a => a.Length / 2f); }
        private static double L_Ref_Ref_D(List<string> l) { return l.Aggregate("", (a, x) => a + x, a => a.Length / 4.0); }
        private static decimal L_Ref_Ref_Dec(List<string> l) { return l.Aggregate("", (a, x) => a + x, a => a.Length / 4m); }
        private static string L_Ref_Ref_Ref(List<string> l) { return l.Aggregate("", (a, x) => a + x, a => "[" + a + "]"); }

        // ---- off-diagonal accumulators (non-decimal) ----
        private static string L_I4_I8_Ref(List<int> l) { return l.Aggregate(0L, (a, x) => a + x, a => "L" + a.ToString(CultureInfo.InvariantCulture)); }
        private static string L_I4_D_Ref(List<int> l) { return l.Aggregate(0.0, (a, x) => a + x, a => DB(a)); }
        private static decimal L_I4_Dec_Dec(List<int> l) { return l.Aggregate(0m, (a, x) => a + x, a => a * 2m); }
        private static int L_Ref_I4_I4(List<string> l) { return l.Aggregate(0, (a, x) => a + x.Length, a => a * 2); }
        private static decimal L_Ref_I4_Dec(List<string> l) { return l.Aggregate(0, (a, x) => a + x.Length, a => a / 4m); }
        private static double L_F_D_D(List<float> l) { return l.Aggregate(0.0, (a, x) => a + x, a => a / 2.0); }
        private static decimal L_D_Dec_Dec(List<double> l) { return l.Aggregate(0m, (a, x) => a + (decimal)(int)x, a => a + 1m); }

        // ---- DECIMAL-accumulator off-diagonals (the T3 result-slot fold body) ----
        private static int L_I4_Dec_I4(List<int> l) { return l.Aggregate(0m, (a, x) => a + x, a => (int)a); }
        private static string L_I4_Dec_Ref(List<int> l) { return l.Aggregate(0m, (a, x) => a + x, a => "M" + a.ToString(CultureInfo.InvariantCulture)); }
        private static double L_I8_Dec_D(List<long> l) { return l.Aggregate(0m, (a, x) => a + (decimal)(x % 1000), a => (double)a / 2.0); }
        private static decimal L_Ref_Dec_Dec(List<string> l) { return l.Aggregate(0m, (a, x) => a + x.Length, a => a * 2m); }
        private static float L_F_Dec_F(List<float> l) { return l.Aggregate(0m, (a, x) => a + (decimal)(int)x, a => (float)a); }

        // empty list -> the selector runs on the seed
        private static string L_Empty_I4_Ref(List<int> l) { return l.Aggregate(7, (a, x) => a + x, a => "e" + a.ToString(CultureInfo.InvariantCulture)); }

        public static void Main()
        {
            List<int> li = new List<int> { 1, 2, 3, 4 };
            List<long> ll = new List<long> { 4294967298L, 3000000000L, -7L };
            List<float> lf = new List<float> { 0.1f, 0.1f, 0.1f, 16777216.0f, 1.0f };
            List<double> ld = new List<double> { 0.1, 0.1, 16777216.0, 1.0 };
            List<decimal> lm = new decimal[] { 1.5m, 2.25m, 3.125m }.ToList();
            List<string> ls = new List<string> { "a", "bb", "ccc" };
            List<int> lempty = new List<int>();

            Console.WriteLine("[i4_i4_i4]=" + L_I4_I4_I4(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_i4_i8]=" + L_I4_I4_I8(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i4_i4_f]=" + FB(L_I4_I4_F(li)));
            Console.WriteLine("[i4_i4_d]=" + DB(L_I4_I4_D(li)));
            Console.WriteLine("[i4_i4_dec]=" + Dec(L_I4_I4_Dec(li)));
            Console.WriteLine("[i4_i4_ref]=" + S(L_I4_I4_Ref(li)));

            Console.WriteLine("[i8_i8_i4]=" + L_I8_I8_I4(ll).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_i8_i8]=" + L_I8_I8_I8(ll).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[i8_i8_f]=" + FB(L_I8_I8_F(ll)));
            Console.WriteLine("[i8_i8_d]=" + DB(L_I8_I8_D(ll)));
            Console.WriteLine("[i8_i8_dec]=" + Dec(L_I8_I8_Dec(ll)));
            Console.WriteLine("[i8_i8_ref]=" + S(L_I8_I8_Ref(ll)));

            Console.WriteLine("[f_f_i4]=" + L_F_F_I4(lf).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_f_i8]=" + L_F_F_I8(lf).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[f_f_f]=" + FB(L_F_F_F(lf)));
            Console.WriteLine("[f_f_d]=" + DB(L_F_F_D(lf)));
            Console.WriteLine("[f_f_dec]=" + Dec(L_F_F_Dec(lf)));
            Console.WriteLine("[f_f_ref]=" + S(L_F_F_Ref(lf)));

            Console.WriteLine("[d_d_i4]=" + L_D_D_I4(ld).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_d_i8]=" + L_D_D_I8(ld).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[d_d_f]=" + FB(L_D_D_F(ld)));
            Console.WriteLine("[d_d_d]=" + DB(L_D_D_D(ld)));
            Console.WriteLine("[d_d_dec]=" + Dec(L_D_D_Dec(ld)));
            Console.WriteLine("[d_d_ref]=" + S(L_D_D_Ref(ld)));

            Console.WriteLine("[dec_dec_i4]=" + L_Dec_Dec_I4(lm).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[dec_dec_i8]=" + L_Dec_Dec_I8(lm).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[dec_dec_f]=" + FB(L_Dec_Dec_F(lm)));
            Console.WriteLine("[dec_dec_d]=" + DB(L_Dec_Dec_D(lm)));
            Console.WriteLine("[dec_dec_dec]=" + Dec(L_Dec_Dec_Dec(lm)));
            Console.WriteLine("[dec_dec_ref]=" + S(L_Dec_Dec_Ref(lm)));

            Console.WriteLine("[ref_ref_i4]=" + L_Ref_Ref_I4(ls).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[ref_ref_i8]=" + L_Ref_Ref_I8(ls).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[ref_ref_f]=" + FB(L_Ref_Ref_F(ls)));
            Console.WriteLine("[ref_ref_d]=" + DB(L_Ref_Ref_D(ls)));
            Console.WriteLine("[ref_ref_dec]=" + Dec(L_Ref_Ref_Dec(ls)));
            Console.WriteLine("[ref_ref_ref]=" + S(L_Ref_Ref_Ref(ls)));

            Console.WriteLine("[off_i4_i8_ref]=" + S(L_I4_I8_Ref(li)));
            Console.WriteLine("[off_i4_d_ref]=" + S(L_I4_D_Ref(li)));
            Console.WriteLine("[off_i4_dec_dec]=" + Dec(L_I4_Dec_Dec(li)));
            Console.WriteLine("[off_ref_i4_i4]=" + L_Ref_I4_I4(ls).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[off_ref_i4_dec]=" + Dec(L_Ref_I4_Dec(ls)));
            Console.WriteLine("[off_f_d_d]=" + DB(L_F_D_D(lf)));
            Console.WriteLine("[off_d_dec_dec]=" + Dec(L_D_Dec_Dec(ld)));
            Console.WriteLine("[off_i4_dec_i4]=" + L_I4_Dec_I4(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[off_i4_dec_ref]=" + S(L_I4_Dec_Ref(li)));
            Console.WriteLine("[off_i8_dec_d]=" + DB(L_I8_Dec_D(ll)));
            Console.WriteLine("[off_ref_dec_dec]=" + Dec(L_Ref_Dec_Dec(ls)));
            Console.WriteLine("[off_f_dec_f]=" + FB(L_F_Dec_F(lf)));
            Console.WriteLine("[off_empty_i4_ref]=" + S(L_Empty_I4_Ref(lempty)));

            //==================================================================
            // (2) DefaultIfEmpty(source, defaultValue)
            //==================================================================
            int[] ia = { 1, 2, 3 };
            int[] iae = new int[0];
            long[] la = { 4000000000L, 7L };
            long[] lae = new long[0];
            float[] fa = { 1.5f, 2.5f };
            float[] fae = new float[0];
            double[] da = { 0.25, 0.75 };
            double[] dae = new double[0];
            string[] sa = { "x", "yy" };
            string[] sae = new string[0];
            decimal[] ma = { 1.5m, 2.5m };
            decimal[] mae = new decimal[0];

            Console.WriteLine("[die_arr_i4]=" + ia.DefaultIfEmpty(42).Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_i4e]=" + iae.DefaultIfEmpty(42).First().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_i8]=" + la.DefaultIfEmpty(9L).Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_i8e]=" + lae.DefaultIfEmpty(9L).First().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_f]=" + fa.DefaultIfEmpty(3.5f).Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_fe]=" + FB(fae.DefaultIfEmpty(3.5f).First()));
            Console.WriteLine("[die_arr_d]=" + da.DefaultIfEmpty(8.125).Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_de]=" + DB(dae.DefaultIfEmpty(8.125).First()));
            Console.WriteLine("[die_arr_ref]=" + sa.DefaultIfEmpty("dflt").Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_refe]=" + S(sae.DefaultIfEmpty("dflt").First()));
            Console.WriteLine("[die_arr_dec]=" + ma.DefaultIfEmpty(7m).Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_arr_dece]=" + Dec(mae.DefaultIfEmpty(7m).First()));

            List<int> ldie = new List<int> { 5, 6 };
            List<int> ldiee = new List<int>();
            List<long> lldiee = new List<long>();
            List<float> lfdiee = new List<float>();
            List<double> lddiee = new List<double>();
            List<string> lsdiee = new List<string>();
            List<decimal> lmdiee = new decimal[0].ToList();

            Console.WriteLine("[die_list_i4]=" + ldie.DefaultIfEmpty(42).Count().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_list_i4e]=" + ldiee.DefaultIfEmpty(42).First().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_list_i8e]=" + lldiee.DefaultIfEmpty(9L).First().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[die_list_fe]=" + FB(lfdiee.DefaultIfEmpty(3.5f).First()));
            Console.WriteLine("[die_list_de]=" + DB(lddiee.DefaultIfEmpty(8.125).First()));
            Console.WriteLine("[die_list_refe]=" + S(lsdiee.DefaultIfEmpty("dflt").First()));
            Console.WriteLine("[die_list_dece]=" + Dec(lmdiee.DefaultIfEmpty(7m).First()));

            Console.WriteLine("done");
        }
    }
}
