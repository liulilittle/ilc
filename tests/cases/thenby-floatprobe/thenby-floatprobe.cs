//------------------------------------------------------------------------------
// thenby-floatprobe -- the float (0x0C) sort-key family probe (b5-gaps §14).
//
// LinqKeyFamilyCode folds float (0x0C) and double (0x0D) into the SAME key
// family "2" (MethodMap.Linq.cs), so every float key selector is invoked
// through a double(*) reader: OrderBy(int, float) rides
// il2cpp_linq_order_by_key[_desc]_i4_d (SortInt32KeyCopyT<double> +
// InvokeKeyI4D = double(*)(int32_t)), the fused ThenBy composite rides the
// family-2 cases of the val_i4/_i8 dispatch, and the int64-source keys ride
// _i8_d / InvokeKeyI8SrcD. A Func<int,float> leaf returns a 4-byte float per
// the MS x64 ABI (XMM0 low 32 bits); reading that return through a double
// fn-pointer is an ABI misread (the upper XMM0 half is callee residue), so
// the float ordering can silently differ from net48.
//
// NOTE (measured, this seat): a double[]/float[] ELEMENT source has NO wired
// OrderBy(keySelector) at all -- the transpiler rejects it LOUD ("stage-1
// runtime LINQ surface is int32 (0x08) element-family"), so the probe covers
// the wired value sources: int32[] (the §14 surface) and int64[] (the
// p0conv2 D3 extension, same double-reader fold).
//
// Key design: the float keys are genuinely fractional (2.5f / -1.5f / 0.75f
// / 3.25f, computed as float arithmetic, not cvtsi2sd widenings) and
// duplicate across residues, so
//   * a float-key order and its double-key control DIFFER,
//   * duplicate keys force the stable tie rule to be visible,
//   * a float read as a 64-bit double (denormal / residue bits) cannot
//     reproduce the net48 order.
// Every element is a distinct marker, so byte-equality against the net48
// oracle validates the exact order including stability.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ThenByFloatProbe
    {
        // int32[] source key selectors: float family + double control.
        private static float KF(int x)
        {
            int r = x % 5;
            return r == 0 ? 2.5f : r == 1 ? -1.5f : r == 2 ? 0.75f
                : r == 3 ? -1.5f : 3.25f;
        }
        private static double KD(int x) { return (x % 5) * 1.5 - 2.25; }

        // int64[] source key selectors: float family + double control.
        private static float KLF(long x)
        {
            long r = x % 5;
            return r == 0 ? 2.5f : r == 1 ? -1.5f : r == 2 ? 0.75f
                : r == 3 ? -1.5f : 3.25f;
        }
        private static double KLD(long x) { return (x % 5) * 1.5 - 2.25; }

        // Secondary float keys for the fused composite (duplicates on % 3).
        private static float KF2(int x) { return x % 3 == 0 ? 1.25f : -0.5f; }
        private static double KD2(int x) { return (x % 3) * 2.0 - 1.0; }

        // Exactly-once contract probe (net48 caches each key once).
        private static int s_calls;
        private static float CountKF(int x) { ++s_calls; return KF(x); }

        private static string JoinInt(int[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        private static string JoinLong(long[] a)
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
            // 11 distinct markers; % 5 spreads the float keys with duplicates
            // (-1.5f hits residues 1 and 3), % 3 duplicates the secondary.
            int[] src = { 17, 4, 13, 8, 25, 11, 2, 19, 6, 23, 9 };

            // Direct OrderBy / OrderByDescending, float key + double control.
            Console.WriteLine("[i4_f_aa]=" + JoinInt(src.OrderBy(KF).ToArray()));
            Console.WriteLine("[i4_f_da]=" + JoinInt(src.OrderByDescending(KF).ToArray()));
            Console.WriteLine("[i4_d_aa]=" + JoinInt(src.OrderBy(KD).ToArray()));
            Console.WriteLine("[i4_d_da]=" + JoinInt(src.OrderByDescending(KD).ToArray()));

            // Fused composite, float secondary after a float primary, 4
            // direction combinations (duplicate keys on BOTH slots).
            Console.WriteLine("[i4_ff_aa]=" + JoinInt(src.OrderBy(KF).ThenBy(KF2).ToArray()));
            Console.WriteLine("[i4_ff_da]=" + JoinInt(src.OrderByDescending(KF).ThenBy(KF2).ToArray()));
            Console.WriteLine("[i4_ff_ad]=" + JoinInt(src.OrderBy(KF).ThenByDescending(KF2).ToArray()));
            Console.WriteLine("[i4_ff_dd]=" + JoinInt(src.OrderByDescending(KF).ThenByDescending(KF2).ToArray()));

            // Mixed pairs both ways (the family literal must survive).
            Console.WriteLine("[i4_df_aa]=" + JoinInt(src.OrderBy(KD).ThenBy(KF2).ToArray()));
            Console.WriteLine("[i4_df_dd]=" + JoinInt(src.OrderBy(KD).ThenByDescending(KF2).ToArray()));
            Console.WriteLine("[i4_fd_aa]=" + JoinInt(src.OrderBy(KF).ThenBy(KD2).ToArray()));
            Console.WriteLine("[i4_fd_da]=" + JoinInt(src.OrderByDescending(KF).ThenBy(KD2).ToArray()));

            // int64[] source: the wide-cell float-key surface.
            long[] src64 = { 17L, 4L, 13L, 8L, 25L, 11L, 2L, 19L, 6L, 23L, 9L };
            Console.WriteLine("[i8_f_aa]=" + JoinLong(src64.OrderBy(KLF).ToArray()));
            Console.WriteLine("[i8_f_da]=" + JoinLong(src64.OrderByDescending(KLF).ToArray()));
            Console.WriteLine("[i8_d_aa]=" + JoinLong(src64.OrderBy(KLD).ToArray()));
            Console.WriteLine("[i8_f_then_f_aa]=" + JoinLong(src64.OrderBy(KLF).ThenBy(KLF).ToArray()));
            Console.WriteLine("[i8_f_then_f_dd]=" + JoinLong(src64.OrderByDescending(KLF).ThenByDescending(KLF).ToArray()));

            // Each key selector must be invoked EXACTLY ONCE per element in
            // the fused composite (n = 11 -> 22 calls).
            s_calls = 0;
            int[] counted = src.OrderBy(CountKF).ThenBy(KF2).ToArray();
            Console.WriteLine("[i4_float_once]=" + s_calls + "|" + JoinInt(counted));
        }
    }
}
