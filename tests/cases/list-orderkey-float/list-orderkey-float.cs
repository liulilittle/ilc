//------------------------------------------------------------------------------
// list-orderkey-float -- the List<T> receiver FLOAT key probe (the List face
// residual of b5-gaps section 14).
//
// The List key-selector surface folds float (0x0C) and double (0x0D) keys
// into the SAME "_d" entries:
//   * List<double>.OrderBy(x => float) rides
//     il2cpp_linq_list_order_by_key[_desc]_d -> il2cpp_linq_order_by_key_d
//     (SortDoubleKeyCopy + InvokeKeyDSrcD = double(*)(double)); a
//     Func<double,float> leaf returns a 4-byte float per the MS x64 ABI, so
//     the double read mixes the callee's XMM0 residue into every key.
//   * List<Box>.OrderBy(b => float) (a REFERENCE-element list) rides
//     il2cpp_linq_list_order_by_key_ref_d (InvokeKeyRefD =
//     double(*)(Il2CppObject*)) -- the same misread.
//
// Key design (thenby-floatprobe手法): fractional float keys with duplicates;
// a float-key order and its double-key control DIFFER; duplicate keys force
// the stable tie rule; the double / int32 keys ride as CONTROLS.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public sealed class Box
    {
        public int Id;
        public float W;
        public double D;
    }

    public static class ListOrderByKeyFloatProbe
    {
        private static float KLD(double x)
        {
            long r = ((long)(x * 4.0)) % 4;
            return r == 0 ? 2.5f : r == 1 ? -1.5f : r == 2 ? 0.75f : -1.5f;
        }

        private static string JoinD(double[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        private static string JoinB(Box[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].Id;
            }
            return s;
        }

        public static void Main()
        {
            // List<double> with a FLOAT key (the misread face) + double /
            // int32 controls. 11 distinct markers, float keys duplicate on
            // % 4 (-1.5f hits residues 1 and 3).
            var ld = new List<double>
            {
                7.5, -2.25, 9.75, -2.25, 4.5, 0.25, 8.0, -6.5, 3.25, 5.75, 1.5,
            };
            Console.WriteLine("[list_df_aa]=" + JoinD(ld.OrderBy(KLD).ToArray()));
            Console.WriteLine("[list_df_da]=" + JoinD(ld.OrderByDescending(KLD).ToArray()));
            Console.WriteLine("[list_dd_aa]=" + JoinD(ld.OrderBy(x => x).ToArray()));

            // List<Box> (a REFERENCE-element list) with a FLOAT key.
            var lb = new List<Box>
            {
                new Box { Id = 17, W = -1.5f, D = 0.5 },
                new Box { Id = 4, W = 2.5f, D = -2.25 },
                new Box { Id = 13, W = 0.75f, D = 3.75 },
                new Box { Id = 8, W = -1.5f, D = 0.5 },
                new Box { Id = 25, W = 3.25f, D = -0.75 },
                new Box { Id = 11, W = 2.5f, D = 0.5 },
                new Box { Id = 2, W = 0.75f, D = -1.5 },
                new Box { Id = 19, W = -0.5f, D = 2.25 },
                new Box { Id = 6, W = 2.5f, D = -0.75 },
                new Box { Id = 23, W = -1.5f, D = 2.25 },
                new Box { Id = 9, W = 0.25f, D = 0.5 },
            };
            Console.WriteLine("[list_reff_aa]=" + JoinB(lb.OrderBy(b => b.W).ToArray()));
            Console.WriteLine("[list_reff_da]=" + JoinB(lb.OrderByDescending(b => b.W).ToArray()));
            Console.WriteLine("[list_refd_aa]=" + JoinB(lb.OrderBy(b => b.D).ToArray()));
            Console.WriteLine("[list_refi4_aa]=" + JoinB(lb.OrderBy(b => b.Id).ToArray()));
        }
    }
}
