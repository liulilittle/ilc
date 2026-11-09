// probe-floatgrp-elemops.cs -- the System.Single GroupBy bucket's
// Single / SingleOrDefault / Aggregate / ElementAt / Contains surface.
//
// Baseline (main 1c850325): the bucket switch's float arms end at Average, so
// these five operators fell through to the !valueBucket arms and bound the
// INT32 bucket kernels. MEASURED (probe-s100felem3 / probe-s100fagg2 /
// probe-s100fsingle / probe-s100fcontains): ElementAt answered
// 1.077936E+09 for a float cell read as int32 (a SILENT WRONG VALUE), Aggregate
// folded the float bits as int32 (6 -> 1.517427E+09), and Single / Contains hit
// the int32-family guard (a loud abort).
//
// Each shape is its own method: a single LOUD abort zeroes the whole probe.
// A float KEY bucket is used for Single/Contains (ToLookup's float TKey is
// still LOUD -- b5-gaps section 79, an independent gate).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeFloatGrpElemOps
    {
        private static string FB(float v)
        {
            return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ===== 1. ElementAt over a float bucket (GroupBy, int key) =====
        private static void ElemAt()
        {
            float[] xs = { 3f, 1f, 3f, 2.5f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[felem0] " + FB(g.ElementAt(0)));
            Console.WriteLine("[felem1] " + FB(g.ElementAt(1)));
        }

        // ===== 2. Aggregate(func) over a float bucket (GroupBy, int key) =====
        private static void Agg()
        {
            float[] xs = { 3f, 1f, 3f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fagg] " + FB(g.Aggregate((a, b) => a + b)));
        }

        // ===== 3. Single / SingleOrDefault over a float bucket (GroupBy, int key) =====
        // A one-element bucket: Single/SingleOrDefault answer the element; the
        // multi-element form throws in BOTH oracle and runtime (a difftest
        // false-red when the oracle itself faults), so it is excluded here.
        private static void Single()
        {
            float[] xs = { 3f, 4f, 4f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fsingle] " + FB(g.Single()));
            Console.WriteLine("[fsingleord] " + FB(g.SingleOrDefault()));
        }

        // ===== 4. Contains over a float bucket (GroupBy, int key) =====
        private static void Contains()
        {
            float[] xs = { 3f, 4f, 4f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[fcontains] " + g.Contains(3f));
            Console.WriteLine("[fcontains2] " + g.Contains(9f));
        }

        public static void Main()
        {
            ElemAt();
            Agg();
            Single();
            Contains();
        }
    }
}
