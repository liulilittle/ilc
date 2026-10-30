// probe-floatgrp.cs -- the System.Single (float[]) GroupBy ELEMENT surface.
//
// Baseline (569d9c92) LOUD: "Enumerable.GroupBy<System.Single,...> not wired:
// the stage-1 runtime LINQ surface is int32 (0x08) element-family for this
// operator" -- there is no GroupElemKind::F bucket family and no
// IEnumerator<float> carrier (b5-gaps 23.5 (a)/(b)).
//
// The bucket must stay a REAL 4-byte float[] (net48 layout): get_elements /
// ToArray copy 4-byte cells, the enumerator boxes each cell through
// icall_box_float (which stores the widened double) and the float carrier
// unboxes it back. Every float record prints "value/bits" so a width or
// carrier mistake is visible in the value AND in the raw bit pattern.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeFloatGrp
    {
        private static string FB(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.ToUInt32(BitConverter.GetBytes(v), 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string DB(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string I(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        public static void Main()
        {
            float[] xs = { 1.5f, 2.5f, 1.5f, -0.0f, 3.25f };

            // ===== 1. float[] source, int key, default comparer =====
            foreach (var g in xs.GroupBy(x => (int)x))
            {
                Console.WriteLine("[arr] key=" + I(g.Key) + " n=" + I(g.Count()));
                float[] a = g.ToArray();
                Console.WriteLine("[arr] len=" + I(a.Length));
                for (int i = 0; i < a.Length; i++)
                    Console.WriteLine("[arr] a" + I(i) + "=" + FB(a[i]));
                foreach (float e in g)
                    Console.WriteLine("[arr] e=" + FB(e));
                Console.WriteLine("[arr] sum=" + FB(g.Sum()));
                Console.WriteLine("[arr] first=" + FB(g.First()));
            }

            // ===== element selector projecting a float =====
            foreach (var g in xs.GroupBy(x => (int)x, x => x * 2f))
            {
                Console.WriteLine("[sel] key=" + I(g.Key) + " n=" + I(g.Count()));
                foreach (float e in g)
                    Console.WriteLine("[sel] e=" + FB(e));
            }

            // ===== 2. List<float> source =====
            List<float> lf = new List<float> { 4.5f, 4.5f, 9.75f };
            foreach (var g in lf.GroupBy(x => (int)x))
            {
                Console.WriteLine("[list] key=" + I(g.Key) + " n=" + I(g.Count()));
                float[] a = g.ToArray();
                for (int i = 0; i < a.Length; i++)
                    Console.WriteLine("[list] a" + I(i) + "=" + FB(a[i]));
                foreach (float e in g)
                    Console.WriteLine("[list] e=" + FB(e));
            }

            // ===== 3. float special values: NaN / -0.0 / +0.0 / +-Inf /
            // MaxValue / MinValue / smallest denormal =====
            float[] sp =
            {
                float.NaN, -0.0f, 0.0f, float.PositiveInfinity, float.NegativeInfinity,
                float.MaxValue, float.MinValue, float.Epsilon,
            };
            foreach (var g in sp.GroupBy(x => 0))
            {
                Console.WriteLine("[sp] key=" + I(g.Key) + " n=" + I(g.Count()));
                float[] a = g.ToArray();
                for (int i = 0; i < a.Length; i++)
                    Console.WriteLine("[sp] a" + I(i) + "=" + FB(a[i]));
                foreach (float e in g)
                    Console.WriteLine("[sp] e=" + FB(e));
            }

            // ===== 4. empty source =====
            float[] none = new float[0];
            Console.WriteLine("[empty] n=" + I(none.GroupBy(x => (int)x).Count()));

            // ===== 5. single element =====
            float[] one = { 7.5f };
            foreach (var g in one.GroupBy(x => 42))
            {
                Console.WriteLine("[one] key=" + I(g.Key) + " n=" + I(g.Count()));
                foreach (float e in g)
                    Console.WriteLine("[one] e=" + FB(e));
                Console.WriteLine("[one] first=" + FB(g.First()));
                Console.WriteLine("[one] last=" + FB(g.Last()));
                Console.WriteLine("[one] sum=" + FB(g.Sum()));
                Console.WriteLine("[one] min=" + FB(g.Min()));
                Console.WriteLine("[one] max=" + FB(g.Max()));
            }

            // ===== 7. result selector (key + bucket) =====
            foreach (string s in xs.GroupBy(x => (int)x,
                         (k, g) => I(k) + ":" + I(g.Count()) + ":" + FB(g.Sum())))
                Console.WriteLine("[rs] " + s);

            // ===== 8. float ELEMENT bucket combined with the other key
            // families: int64 / double / string =====
            foreach (var g in xs.GroupBy(x => (long)(int)x))
            {
                Console.WriteLine("[ki8] key=" + g.Key.ToString(CultureInfo.InvariantCulture) +
                                  " n=" + I(g.Count()) + " first=" + FB(g.First()));
                foreach (float e in g)
                    Console.WriteLine("[ki8] e=" + FB(e));
            }
            foreach (var g in xs.GroupBy(x => (double)x))
            {
                Console.WriteLine("[kd] key=" + DB(g.Key) + " n=" + I(g.Count()) +
                                  " first=" + FB(g.First()));
                foreach (float e in g)
                    Console.WriteLine("[kd] e=" + FB(e));
            }
            foreach (var g in xs.GroupBy(x => x > 2f ? "hi" : "lo"))
            {
                Console.WriteLine("[kref] key=" + g.Key + " n=" + I(g.Count()) +
                                  " first=" + FB(g.First()));
                foreach (float e in g)
                    Console.WriteLine("[kref] e=" + FB(e));
            }

            Console.WriteLine("done");
        }
    }
}
