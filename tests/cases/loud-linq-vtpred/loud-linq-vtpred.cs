//------------------------------------------------------------------------------
// loud-linq-vtpred.cs -- e2e reference case for the LINQ SOURCE PREDICATE
// family over the by-value struct element families (System.DateTime /
// System.TimeSpan / System.Guid), plus the reference-source predicate forms of
// First / FirstOrDefault.
//
// Before this change every one of these operators over a DateTime[] /
// TimeSpan[] / Guid[] source LOUD-rejected at the int32-only gate (the runtime
// only shipped the i4 / ref / wide predicate entries, whose callbacks use a
// foreign ABI and read the wrong cell width). The new per-family entries read
// the element at its real cell width and call the generated predicate through
// the by-value struct ABI:
//   Where / Where(index) / All / TakeWhile / TakeWhile(index) /
//   SkipWhile / SkipWhile(index) / Count(pred) / Any(pred) /
//   First / FirstOrDefault / Last / LastOrDefault
// and the reference-source First(pred) / FirstOrDefault(pred) ride the
// Il2CppObject* predicate ABI.
//
// Every printed value is deterministic; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqVtpred
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
                new TimeSpan(0, 0, 0, 5),
                new TimeSpan(2, 0, 0, 0),
                new TimeSpan(1, 2, 3, 4),
                new TimeSpan(0, 0, 0, 5),
            };
            Guid[] gs = GuidValues();
            string[] words = { "pear", "fig", "kiwi", "plum", "date" };

            // ---- DateTime[]: Where / Where(index) ----
            DateTime[] dtWhere = dts.Where(d => d.Year == 2020).ToArray();
            Console.WriteLine("dt_where=" + dtWhere.Length + ":" + Stamp(dtWhere[0]) + ":" + Stamp(dtWhere[1]));
            DateTime[] dtWhereIdx = dts.Where((d, i) => i % 2 == 0).ToArray();
            Console.WriteLine("dt_where_idx=" + dtWhereIdx.Length + ":" + Stamp(dtWhereIdx[0]) + ":" + Stamp(dtWhereIdx[1]));

            // ---- All ----
            Console.WriteLine("dt_all=" + dts.All(d => d.Year >= 1999) + ":" + dts.All(d => d.Year >= 2020));

            // ---- TakeWhile / SkipWhile (plain + index) ----
            DateTime[] dtTake = dts.TakeWhile(d => d.Year >= 2020).ToArray();
            Console.WriteLine("dt_takewhile=" + dtTake.Length + ":" + Stamp(dtTake[0]));
            DateTime[] dtTakeIdx = dts.TakeWhile((d, i) => i < 2).ToArray();
            Console.WriteLine("dt_takewhile_idx=" + dtTakeIdx.Length + ":" + Stamp(dtTakeIdx[1]));
            DateTime[] dtSkip = dts.SkipWhile(d => d.Year >= 2020).ToArray();
            Console.WriteLine("dt_skipwhile=" + dtSkip.Length + ":" + Stamp(dtSkip[0]) + ":" + Stamp(dtSkip[2]));
            DateTime[] dtSkipIdx = dts.SkipWhile((d, i) => i < 2).ToArray();
            Console.WriteLine("dt_skipwhile_idx=" + dtSkipIdx.Length + ":" + Stamp(dtSkipIdx[0]));

            // ---- Count(pred) / Any(pred) ----
            Console.WriteLine("dt_count=" + dts.Count(d => d.Month == 5));
            Console.WriteLine("dt_any=" + dts.Any(d => d.Year == 2024) + ":" + dts.Any(d => d.Year == 1500));

            // ---- First / FirstOrDefault / Last / LastOrDefault ----
            Console.WriteLine("dt_first=" + Stamp(dts.First()));
            Console.WriteLine("dt_firstod=" + Stamp(dts.FirstOrDefault()));
            Console.WriteLine("dt_last=" + Stamp(dts.Last()));
            Console.WriteLine("dt_lastod=" + Stamp(dts.LastOrDefault()));

            // ---- empty sequence truth table ----
            DateTime[] emptyDt = new DateTime[0];
            Console.WriteLine("dt_empty=" + emptyDt.Where(d => d.Year > 0).ToArray().Length
                + ":" + emptyDt.Count(d => true)
                + ":" + emptyDt.Any(d => true)
                + ":" + emptyDt.All(d => false)
                + ":" + emptyDt.TakeWhile(d => true).ToArray().Length
                + ":" + emptyDt.SkipWhile(d => true).ToArray().Length
                + ":" + emptyDt.FirstOrDefault().Year
                + ":" + emptyDt.LastOrDefault().Year);

            // ---- TimeSpan[] ----
            TimeSpan[] tsWhere = tss.Where(t => t.Days >= 1).ToArray();
            Console.WriteLine("ts_where=" + tsWhere.Length + ":" + Span(tsWhere[0]) + ":" + Span(tsWhere[1]));
            Console.WriteLine("ts_all=" + tss.All(t => t.Seconds >= 0) + ":" + tss.All(t => t.Days >= 1));
            TimeSpan[] tsTake = tss.TakeWhile(t => t.Days == 0).ToArray();
            Console.WriteLine("ts_takewhile=" + tsTake.Length + ":" + Span(tsTake[0]));
            TimeSpan[] tsSkip = tss.SkipWhile(t => t.Days == 0).ToArray();
            Console.WriteLine("ts_skipwhile=" + tsSkip.Length + ":" + Span(tsSkip[0]) + ":" + Span(tsSkip[2]));
            Console.WriteLine("ts_count=" + tss.Count(t => t.Days >= 1) + ":" + tss.Any(t => t.Days == 2));
            Console.WriteLine("ts_first=" + Span(tss.First()) + ":" + Span(tss.Last())
                + ":" + Span(tss.FirstOrDefault()) + ":" + Span(tss.LastOrDefault()));

            // ---- Guid[] ----
            Guid[] gdWhere = gs.Where(g => g != gs[1]).ToArray();
            Console.WriteLine("gd_where=" + gdWhere.Length + ":" + gdWhere[0].ToString() + ":" + gdWhere[1].ToString());
            Console.WriteLine("gd_all=" + gs.All(g => g != gs[1]) + ":" + gs.All(g => g != gs[3]));
            Guid[] gdTake = gs.TakeWhile(g => g != gs[2]).ToArray();
            Console.WriteLine("gd_takewhile=" + gdTake.Length + ":" + gdTake[1].ToString());
            Guid[] gdSkip = gs.SkipWhile(g => g != gs[2]).ToArray();
            Console.WriteLine("gd_skipwhile=" + gdSkip.Length + ":" + gdSkip[0].ToString() + ":" + gdSkip[1].ToString());
            Console.WriteLine("gd_count=" + gs.Count(g => g == gs[0]) + ":" + gs.Any(g => g == gs[2]));
            Console.WriteLine("gd_first=" + gs.First().ToString());
            Console.WriteLine("gd_firstod=" + gs.FirstOrDefault().ToString());
            Console.WriteLine("gd_last=" + gs.Last().ToString());
            Console.WriteLine("gd_lastod=" + gs.LastOrDefault().ToString());

            // ---- reference-source First(pred) / FirstOrDefault(pred) ----
            Console.WriteLine("ref_first=" + words.First(w => w.Length >= 4));
            Console.WriteLine("ref_firstod=" + (words.FirstOrDefault(w => w.Length > 99) ?? "<null>"));
            Console.WriteLine("ref_firstod_empty=" + (new string[0].FirstOrDefault(w => true) ?? "<null>"));

            Console.WriteLine("done");
        }
    }
}
