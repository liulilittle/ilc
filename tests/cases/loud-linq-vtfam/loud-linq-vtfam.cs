//------------------------------------------------------------------------------
// loud-linq-vtfam.cs -- e2e reference case for the FAMILY-GENERIC LINQ surface
// (linq_family.h) over the VALUE-TYPE array element families
// (System.DateTime / System.TimeSpan / System.Guid).
//
// The family-generic kernels read/write the element at its REAL width when the
// caller passes the cell code as the trailing literal (12 / 13 / 14), so the
// transpiler wires them for the operators the runtime covers: Take / Skip /
// Reverse / Concat / Distinct / Except / Union / Intersect / DefaultIfEmpty /
// LongCount / SequenceEqual / Contains / ElementAt / Single / SingleOrDefault.
//
// Before this change every one of these operators over a DateTime[] /
// TimeSpan[] / Guid[] source LOUD-rejected (the int32-only gate) -- or, worse,
// rode the DECIMAL kernels (a 16-byte decimal cell read over an 8/16-byte
// payload: a silent wrong value).
//
// The operators with NO value-family kernel (the predicate family -- Where /
// All / TakeWhile / SkipWhile / Count(pred) / Any(pred) -- and First / Last)
// stay LOUD; they are registered in docs/audit/b5-gaps.md.
//
// Every printed value is deterministic; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqVtfam
    {
        // The transpiler's static storage has no 16-byte Guid slot, so the
        // Guid carriers come from locals (the loud-linq-grpguid convention).
        private static Guid[] GuidValues()
        {
            Guid g1 = new Guid("0aa79e57-1c2b-4d3e-9f40-112233445566");
            Guid g2 = new Guid("0aa79e57-1c2b-4d3e-8f40-112233445566");
            Guid g3 = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
            return new Guid[] { g1, g2, g3, g1 };
        }

        // The DateTime / TimeSpan values are printed through their int32
        // properties (the int64 Ticks property is not part of the wired
        // surface); the DateTime.Year / Month / Day / Hour properties are the
        // loud-linq-grpdt convention.
        private static string Stamp(DateTime d)
        {
            return d.Year + "-" + d.Month + "-" + d.Day + " " + d.Hour;
        }

        private static string Span(TimeSpan t)
        {
            return t.Days + "d" + t.Hours + "h" + t.Minutes + "m" + t.Seconds + "s";
        }

        public static void Main()
        {
            DateTime[] dts =
            {
                new DateTime(2020, 5, 17, 9, 30, 0),
                new DateTime(1999, 12, 31, 23, 59, 59),
                new DateTime(2024, 2, 29, 12, 0, 0),
                new DateTime(2020, 5, 17, 9, 30, 0),
            };
            TimeSpan[] tss =
            {
                new TimeSpan(1, 2, 3),
                new TimeSpan(0, 0, 5),
                new TimeSpan(2, 0, 0),
                new TimeSpan(1, 2, 3),
            };
            Guid[] gs = GuidValues();

            // ---- Take / Skip / Reverse over DateTime[] ----
            DateTime[] dtTake = dts.Take(2).ToArray();
            Console.WriteLine("dt_take=" + dtTake.Length + ":" + Stamp(dtTake[0]) + ":" + Stamp(dtTake[1]));
            DateTime[] dtSkip = dts.Skip(2).ToArray();
            Console.WriteLine("dt_skip=" + dtSkip.Length + ":" + Stamp(dtSkip[0]) + ":" + Stamp(dtSkip[1]));
            DateTime[] dtRev = dts.Reverse().ToArray();
            Console.WriteLine("dt_rev=" + dtRev.Length + ":" + Stamp(dtRev[0]) + ":" + Stamp(dtRev[3]));
            DateTime[] dtTake0 = dts.Take(0).ToArray();
            Console.WriteLine("dt_take0=" + dtTake0.Length);

            // ---- Distinct (first-appearance dedup) ----
            DateTime[] dtDist = dts.Distinct().ToArray();
            Console.WriteLine("dt_distinct=" + dtDist.Length + ":" + Stamp(dtDist[0]) + ":" + Stamp(dtDist[1]));

            // ---- Concat / Except / Union / Intersect ----
            DateTime[] dtCat = dts.Take(1).Concat(dts.Skip(3)).ToArray();
            Console.WriteLine("dt_concat=" + dtCat.Length + ":" + Stamp(dtCat[0]) + ":" + Stamp(dtCat[1]));
            DateTime[] dtExc = dts.Except(dts.Skip(3)).ToArray();
            Console.WriteLine("dt_except=" + dtExc.Length + ":" + Stamp(dtExc[0]) + ":" + Stamp(dtExc[1]));
            DateTime[] dtUni = dts.Union(dts.Skip(3)).ToArray();
            Console.WriteLine("dt_union=" + dtUni.Length + ":" + Stamp(dtUni[0]));
            DateTime[] dtInt = dts.Intersect(dts.Skip(3)).ToArray();
            Console.WriteLine("dt_intersect=" + dtInt.Length + ":" + Stamp(dtInt[0]));

            // ---- DefaultIfEmpty / LongCount / SequenceEqual / Contains ----
            Console.WriteLine("dt_defaultifempty=" + new DateTime[0].DefaultIfEmpty().ToArray().Length);
            Console.WriteLine("dt_longcount=" + dts.LongCount());
            Console.WriteLine("dt_seqeq=" + dts.SequenceEqual(dts.ToArray()));
            Console.WriteLine("dt_contains=" + dts.Contains(dts[1]) + ":" + dts.Contains(dts[2]));

            // ---- ElementAt / Single / SingleOrDefault ----
            Console.WriteLine("dt_elementat=" + Stamp(dts.ElementAt(2)));
            DateTime[] dtOne = new DateTime[] { dts[0] };
            Console.WriteLine("dt_single=" + Stamp(dtOne.Single()));
            Console.WriteLine("dt_singleordefault=" + Stamp(dtOne.SingleOrDefault()));

            // ---- TimeSpan[] ----
            TimeSpan[] tsTake = tss.Take(2).ToArray();
            Console.WriteLine("ts_take=" + tsTake.Length + ":" + Span(tsTake[0]));
            Console.WriteLine("ts_distinct=" + tss.Distinct().ToArray().Length);
            Console.WriteLine("ts_longcount=" + tss.LongCount());
            Console.WriteLine("ts_contains=" + tss.Contains(tss[1]));
            Console.WriteLine("ts_elementat=" + Span(tss.ElementAt(2)));
            Console.WriteLine("ts_reverse=" + Span(tss.Reverse().ToArray()[0]));

            // ---- Guid[] ----
            Guid[] gTake = gs.Take(2).ToArray();
            Console.WriteLine("gd_take=" + gTake.Length + ":" + gTake[0].ToString());
            Console.WriteLine("gd_distinct=" + gs.Distinct().ToArray().Length);
            Console.WriteLine("gd_longcount=" + gs.LongCount());
            Console.WriteLine("gd_contains=" + gs.Contains(gs[1]));
            Console.WriteLine("gd_elementat=" + gs.ElementAt(2).ToString());
            Console.WriteLine("gd_reverse=" + gs.Reverse().ToArray()[0].ToString());
            Console.WriteLine("gd_single=" + new Guid[] { gs[0] }.Single().ToString());

            Console.WriteLine("done");
        }
    }
}
