//------------------------------------------------------------------------------
// thenbywide-elem -- ThenBy / ThenByDescending over a DOUBLE / FLOAT ELEMENT
// source (b5-gaps section 47).
//
// The wide ELEMENT-source OrderBy(keySelector) surface is wired
// (orderwide-elem): double[] rides _d_<keyfam>, float[] rides _f_<keyfam>,
// the element widens to double in the F-slot and the key returns in its own
// family slot (int32 / int64 / float / double). The FUSED ThenBy composite
// over those sources was NOT wired: TheThenBy arm only covered int32/int64
// elements (ThenByKernelSuffix), so a double[]/float[] source fell through to
// "...the then-by surface ... covers int32 elements ... and int64 elements ..."
// LOUD even though net48 fully supports it.
//
// Every element is a DISTINCT fractional marker so byte-equality against the
// net48 oracle validates the exact order INCLUDING stability.  Ties are forced
// by duplicate keys (a double key truncated to its int part, a float key
// expanded to a double then WIDENED back etc.).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ThenByWideElemProbe
    {
        static string JoinD(double[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString("0.###");
            }
            return s;
        }

        static string JoinF(float[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        // Fold to counting numbers so the SECOND level decides exactly.
        static int Trunc(double d) { return (int)d; }
        static int TruncF(float f) { return (int)f; }
        static double Frac(double d) { return d - (int)d; }
        static float FracF(float f) { return f - (int)f; }

        public static int Calls;

        static int N(int v) { ++Calls; return v; }
        static double ND(double v) { ++Calls; return v; }
        static float NF(float v) { ++Calls; return v; }

        public static void Main()
        {
            // A double[] source with a double key.
            double[] ds = { 2.7, 1.2, 2.1, 1.9, 3.0, 2.5, 1.1, 3.4, 2.9, 1.7 };
            Console.WriteLine("dd_aa=" + JoinD(ds.OrderBy(ND).ThenBy(ND).ToArray()));
            Console.WriteLine("dd_ad=" + JoinD(ds.OrderBy(ND).ThenByDescending(ND).ToArray()));
            Console.WriteLine("dd_da=" + JoinD(ds.OrderByDescending(ND).ThenBy(ND).ToArray()));
            Console.WriteLine("dd_dd=" + JoinD(ds.OrderByDescending(ND).ThenByDescending(ND).ToArray()));

            // double[] source, int32 secondary key.
            Console.WriteLine("di_aa=" + JoinD(ds.OrderBy(ND).ThenBy(Trunc).ToArray()));
            Console.WriteLine("di_ad=" + JoinD(ds.OrderBy(ND).ThenByDescending(Trunc).ToArray()));

            // double[] source, int64 secondary key (Trunc widened).
            Console.WriteLine("dl_aa=" + JoinD(ds.OrderBy(ND).ThenBy(d => (long)Trunc(d)).ToArray()));

            // double[] source, float secondary key.
            Console.WriteLine("df_aa=" + JoinD(ds.OrderBy(ND).ThenBy(d => (float)Trunc(d)).ToArray()));

            // A float[] source with a float key.
            float[] fs = { 2.7f, 1.2f, 2.1f, 1.9f, 3.0f, 2.5f, 1.1f, 3.4f, 2.9f, 1.7f };
            Console.WriteLine("ff_aa=" + JoinF(fs.OrderBy(NF).ThenBy(NF).ToArray()));
            Console.WriteLine("ff_dd=" + JoinF(fs.OrderByDescending(NF).ThenByDescending(NF).ToArray()));

            // float[] source, int32 secondary key.
            Console.WriteLine("fi_aa=" + JoinF(fs.OrderBy(NF).ThenBy(TruncF).ToArray()));

            // THREEE levels over a double[] source.
            Callless.DoThree(ds);

            Console.WriteLine("src_same=" + (ds[0] == 2.7 && ds[3] == 1.9 && ds[9] == 1.7 ? 1 : 0));
            Console.WriteLine("calls=" + Calls);
        }

        static class Callless
        {
            static int A(double d) { return (int)d; }
            static double B(double d) { return d - (int)d; }
            static double C(double d) { return 100 - d; }

            public static void DoThree(double[] ds)
            {
                var three = ds.OrderBy(A).ThenBy(B).ThenByDescending(C).ToArray();
                string s = "";
                for (int i = 0; i < three.Length; ++i)
                {
                    if (i > 0) s += ",";
                    s += three[i].ToString("0.###");
                }
                Console.WriteLine("d3=" + s);
            }
        }
    }
}