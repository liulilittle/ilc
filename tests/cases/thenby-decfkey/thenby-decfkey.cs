//------------------------------------------------------------------------------
// thenby-decfkey -- the decimal-source FLOAT key probe (the dec fusion float
// residual of b5-gaps section 14).
//
// The DECIMAL composite's key-family code (LinqKeyFamilyCode) folds float
// (0x0C) and double (0x0D) into the SAME family "2", so every Func<decimal,
// float> key selector is invoked through a double(*) reader:
//   * the fused composite (OrderBy(k1).ThenBy(k2) over a decimal[] source)
//     rides the family-2 cases of the SortDecTwoKeysDispatch matrix
//     (InvokeKeyDecD = double(*)(decimal));
//   * the DIRECT decimal-source OrderBy(float key) rides
//     il2cpp_linq_order_by_key[_desc]_dec_d (SortDecKeyCopyT<double>).
// A Func<decimal,float> leaf returns a 4-byte float per the MS x64 ABI (XMM0
// low 32 bits); reading that return through a double fn-pointer is an ABI
// misread (the upper XMM0 half is callee residue), so the float ordering can
// silently differ from net48.
//
// Key design (thenby-floatprobe手法): the float keys are genuinely fractional
// (2.5f / -1.5f / 0.75f / 3.25f, float arithmetic, not cvtsi2sd widenings) and
// duplicate across residues, so a float-key order and its double-key control
// DIFFER, duplicate keys force the stable tie rule to be visible, and a float
// read as a 64-bit double (residue bits) cannot reproduce the net48 order.
// Every element is a distinct marker, so byte-equality against the net48
// oracle validates the exact order including stability. The decimal secondary
// key (composite family "3") and the double keys ride as CONTROLS -- they are
// wired and must stay byte-identical across the fix.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ThenByDecFKeyProbe
    {
        // Direct decimal-source float key (duplicates on % 4: -1.5f hits
        // residues 1 and 3).
        private static float KDF(decimal x)
        {
            long r = decimal.ToInt64(x) % 4;
            return r == 0 ? 2.5f : r == 1 ? -1.5f : r == 2 ? 0.75f : -1.5f;
        }

        // Secondary float keys for the fused composite (duplicates on % 3).
        private static float KDF2(decimal x)
        {
            long r = decimal.ToInt64(x) % 3;
            return r == 0 ? 1.25f : r == 1 ? -0.5f : 1.25f;
        }

        // Double-key controls (the wired _dec_d surface).
        private static double KDD(decimal x) { return (decimal.ToInt64(x) % 4) * 1.5 - 2.25; }
        private static double KDD2(decimal x) { return (decimal.ToInt64(x) % 3) * 2.0 - 1.0; }

        // int32 / int64 / decimal keys (the wired composite primaries).
        private static int KI4(decimal x) { return (int)(decimal.ToInt64(x) % 4); }
        private static long KI8(decimal x) { return decimal.ToInt64(x) % 4; }
        private static decimal KDec(decimal x) { return x % 3m; }

        private static string Join(decimal[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        public static void Main()
        {
            // 11 distinct markers; % 4 spreads the float keys with duplicates
            // (-1.5f hits residues 1 and 3), % 3 duplicates the secondary.
            decimal[] src = { 17m, 4m, 13m, 8m, 25m, 11m, 2m, 19m, 6m, 23m, 9m };

            // Direct OrderBy / OrderByDescending over the decimal source,
            // float key + double control.
            Console.WriteLine("[dec_f_aa]=" + Join(src.OrderBy(KDF).ToArray()));
            Console.WriteLine("[dec_f_da]=" + Join(src.OrderByDescending(KDF).ToArray()));
            Console.WriteLine("[dec_d_aa]=" + Join(src.OrderBy(KDD).ToArray()));

            // Fused composite, FLOAT secondary after an int32 primary, 4
            // direction combinations (duplicate keys on BOTH slots).
            Console.WriteLine("[dec_i4f_aa]=" + Join(src.OrderBy(KI4).ThenBy(KDF2).ToArray()));
            Console.WriteLine("[dec_i4f_da]=" + Join(src.OrderByDescending(KI4).ThenBy(KDF2).ToArray()));
            Console.WriteLine("[dec_i4f_ad]=" + Join(src.OrderBy(KI4).ThenByDescending(KDF2).ToArray()));
            Console.WriteLine("[dec_i4f_dd]=" + Join(src.OrderByDescending(KI4).ThenByDescending(KDF2).ToArray()));

            // Mixed pairs both ways (the family literal must survive): a float
            // PRIMARY with a double secondary, a double primary with a float
            // secondary, and an int64 primary with a float secondary.
            Console.WriteLine("[dec_ff_aa]=" + Join(src.OrderBy(KDF).ThenBy(KDF2).ToArray()));
            Console.WriteLine("[dec_fd_aa]=" + Join(src.OrderBy(KDF).ThenBy(KDD2).ToArray()));
            Console.WriteLine("[dec_df_aa]=" + Join(src.OrderBy(KDD).ThenBy(KDF2).ToArray()));
            Console.WriteLine("[dec_i8f_aa]=" + Join(src.OrderBy(KI8).ThenBy(KDF2).ToArray()));
            Console.WriteLine("[dec_i8f_dd]=" + Join(src.OrderByDescending(KI8).ThenByDescending(KDF2).ToArray()));

            // The decimal SECONDARY control (composite family "3") must keep
            // its meaning across the fix.
            Console.WriteLine("[dec_dec_aa]=" + Join(src.OrderBy(KI8).ThenBy(KDec).ToArray()));
        }
    }
}
