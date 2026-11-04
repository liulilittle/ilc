//------------------------------------------------------------------------------
// thenbywide-list -- ThenBy / ThenByDescending chains over a List<double> and
// List<float> source (b5-gaps section 49).
//
// Section 47 wired the ARRAY double/float sources; the List<T> chain face
// covered only int32 and reference elements. A List<double> / List<float>
// OrderBy primary was folded into the ARRAY chain entry with the LIST cast to
// Il2CppArray* (the generated code read the items pointer as a length) -- a
// runtime abort, not a LOUD. This case pins the list-wide chain: the list is
// snapped ONCE and the array wide chain kernel does the level-by-level key
// phase, exactly-once selector contract and stable merge.
//
// The case cross-joins a double/float element list, primary/secondary key
// families (int32/double/float), both directions, and a three-level chain, and
// verifies the source list is not written.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ThenByWideListProbe
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

        static double ND(double v) { ++Calls; return v; }
        static float NF(float v) { ++Calls; return v; }

        public static void Main()
        {
            // A List<double> source with a double key.
            var ds = new List<double> { 2.7, 1.2, 2.1, 1.9, 3.0, 2.5, 1.1, 3.4, 2.9, 1.7 };
            Console.WriteLine("dd_aa=" + JoinD(ds.OrderBy(ND).ThenBy(ND).ToArray()));
            Console.WriteLine("dd_ad=" + JoinD(ds.OrderBy(ND).ThenByDescending(ND).ToArray()));
            Console.WriteLine("dd_da=" + JoinD(ds.OrderByDescending(ND).ThenBy(ND).ToArray()));
            Console.WriteLine("dd_dd=" + JoinD(ds.OrderByDescending(ND).ThenByDescending(ND).ToArray()));

            // List<double> source, int32 secondary key.
            Console.WriteLine("di_aa=" + JoinD(ds.OrderBy(ND).ThenBy(Trunc).ToArray()));

            // List<float> source, float secondary key.
            var fs = new List<float> { 2.7f, 1.2f, 2.1f, 1.9f, 3.0f, 2.5f, 1.1f, 3.4f, 2.9f, 1.7f };
            Console.WriteLine("ff_aa=" + JoinF(fs.OrderBy(NF).ThenBy(NF).ToArray()));
            Console.WriteLine("ff_dd=" + JoinF(fs.OrderByDescending(NF).ThenByDescending(NF).ToArray()));

            // THREE levels over a List<double> source.
            var three = ds.OrderBy(Trunc).ThenBy(Frac).ThenByDescending(Trunc).ToArray();
            Console.WriteLine("d3=" + JoinD(three));

            Console.WriteLine("src_same=" + (ds[0] == 2.7 && fs[1] == 1.2 ? 1 : 0));
            Console.WriteLine("calls=" + Calls);
        }
    }
}