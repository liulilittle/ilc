//------------------------------------------------------------------------------
// orderwide-elem -- the double[]/float[] ELEMENT-source OrderBy(keySelector)
// probe (b5-gaps section: the wide element-source keyed-sort surface).
//
// Before the wiring, a double[]/float[] source with OrderBy(keySelector) is
// LOUD-rejected at transpile ("the stage-1 runtime LINQ surface is int32
// (0x08) element-family") even though net48 fully supports it.
//
// The wired surface (post-fix): both sources ride the F-slot element ABI (a
// float element widens to double -- the InvokePredF / FPredFn convention),
// with one entry per key family:
//   double[] source: _d_i4 / _d_i8 / _d (double key, the pre-existing
//                    order_by_key_d) / _d_f;
//   float[]  source: _f_i4 / _f_i8 / _f_d / _f_f.
// The FLOAT keys are genuinely fractional and duplicate across residues, so
// a float-key order and its double-key control DIFFER, duplicate keys force
// the stable tie rule to be visible, and a float key read through a double
// reader (the impl-floatkey misread) cannot reproduce the net48 order.
// Every element is a distinct marker, so byte-equality against the net48
// oracle validates the exact order including stability.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class OrderWideElemProbe
    {
        // Float key over a double source (duplicates on % 4: -1.5f hits
        // residues 1 and 3).
        private static float KDF(double x)
        {
            long r = ((long)(x * 4.0)) % 4;
            return r == 0 ? 2.5f : r == 1 ? -1.5f : r == 2 ? 0.75f : -1.5f;
        }

        // Float key over a float source.
        private static float KFF(float x)
        {
            long r = ((long)(x * 4.0)) % 4;
            return r == 0 ? 2.5f : r == 1 ? -1.5f : r == 2 ? 0.75f : -1.5f;
        }

        // int32 / int64 / double keys (the wired control families).
        private static int KI4(double x) { return (int)(((long)(x * 4.0)) % 3); }
        private static long KI8(double x) { return ((long)(x * 4.0)) % 4; }
        private static int KFI4(float x) { return (int)(((long)(x * 4.0)) % 3); }
        private static long KFI8(float x) { return ((long)(x * 4.0)) % 4; }

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

        private static string JoinF(float[] a)
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
            // 11 distinct markers; the % 4 spread duplicates the float keys
            // (-1.5f hits residues 1 and 3), % 3 duplicates the int32 keys.
            double[] ds = { 7.5, -2.25, 9.75, -2.25, 4.5, 0.25, 8.0, -6.5, 3.25, 5.75, 1.5 };
            float[] fs = { 7.5f, -2.25f, 9.75f, -2.25f, 4.5f, 0.25f, 8f, -6.5f, 3.25f, 5.75f, 1.5f };

            // double[] source: float key + double / int32 / int64 controls.
            Console.WriteLine("[d_f_aa]=" + JoinD(ds.OrderBy(KDF).ToArray()));
            Console.WriteLine("[d_f_da]=" + JoinD(ds.OrderByDescending(KDF).ToArray()));
            Console.WriteLine("[d_d_aa]=" + JoinD(ds.OrderBy(x => x).ToArray()));
            Console.WriteLine("[d_i4_aa]=" + JoinD(ds.OrderBy(KI4).ToArray()));
            Console.WriteLine("[d_i8_aa]=" + JoinD(ds.OrderBy(KI8).ToArray()));

            // float[] source: float key + double / int32 / int64 controls.
            Console.WriteLine("[f_f_aa]=" + JoinF(fs.OrderBy(KFF).ToArray()));
            Console.WriteLine("[f_f_da]=" + JoinF(fs.OrderByDescending(KFF).ToArray()));
            Console.WriteLine("[f_d_aa]=" + JoinF(fs.OrderBy(x => (double)x).ToArray()));
            Console.WriteLine("[f_i4_aa]=" + JoinF(fs.OrderBy(KFI4).ToArray()));
            Console.WriteLine("[f_i8_aa]=" + JoinF(fs.OrderBy(KFI8).ToArray()));

            // The fresh result keeps the SOURCE family (a float[] source
            // yields a float[] -- validated by the [1] element read).
            Console.WriteLine("[f_f_elem]=" + fs.OrderBy(KFF).ToArray()[1]);
            Console.WriteLine("[d_f_elem]=" + ds.OrderBy(KDF).ToArray()[1]);
        }
    }
}
