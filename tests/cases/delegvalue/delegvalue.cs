// delegvalue.cs -- delegate Invoke with VALUETYPE generic arguments.
//
// Every delegate below instantiates a BCL generic delegate (Func`N / Action`N /
// Predicate`1) with a VALUE-TYPE argument: System.Guid (16 bytes),
// System.DateTime (16-byte runtime struct), System.TimeSpan (8 bytes) and
// System.Decimal (16 bytes). The Invoke signature therefore carries a
// VALUETYPE (0x11) parameter and/or return, which the multicast helper must
// pass BY VALUE with the SAME C++ type the leaf method declares (the managed
// value-type ABI: one/two eightbyte slots, no heap, no boxing).
//
// Coverage per value type:
//   * DIRECT Invoke (single cast)
//   * += multicast with 2+ leaves -- the LAST leaf's return value wins
//     (.NET multicast return-value semantics) and every leaf runs in order
//   * STATIC + INSTANCE leaves mixed in one invocation list
//   * Predicate`1 (bool return) and Action`N (void return)
//
// Everything printed is deterministic (parsed Guids, fixed dates/spans,
// fixed decimals), so the net48 oracle output is byte-stable.
using System;

namespace Cases
{
    public class GuidInst
    {
        public int Bias;
        public GuidInst(int b) { Bias = b; }

        public int Rank(Guid g) { return (g == Guid.Empty ? 0 : 1) + Bias; }
        public string Text(Guid g) { return "I" + Bias + ":" + g.ToString(); }
        public void Sink(Guid g) { Console.WriteLine("isink=" + Bias + ":" + g.ToString()); }
        public bool IsZero(Guid g) { return g == Guid.Empty; }

        public DateTime Plus(DateTime d) { return d.AddDays(Bias); }
        public void DSink(DateTime d) { Console.WriteLine("idsink=" + Bias + ":" + d.Ticks); }
        public bool IsBefore2000(DateTime d) { return d.Year < 2000; }

        public int SpanSec(TimeSpan t) { return t.Seconds + Bias; }
        public void TSink(TimeSpan t) { Console.WriteLine("itsink=" + Bias + ":" + t.Ticks); }

        public decimal Mul(decimal d) { return d * Bias; }
        public bool IsBig(decimal d) { return d > 100m; }
    }

    public static class DelegateValue
    {
        // ---------------- Guid leaves ----------------
        private static int GuidRank(Guid g) { return g.ToString() == "00000000-0000-0000-0000-000000000001" ? 1 : 2; }
        private static int GuidRank2(Guid g) { return g.ToString() == "00000000-0000-0000-0000-000000000001" ? 10 : 20; }
        private static string GuidText(Guid g) { return "S:" + g.ToString(); }
        private static void GuidSink(Guid g) { Console.WriteLine("ssink=" + g.ToString()); }
        private static void GuidSink2(Guid g) { Console.WriteLine("ssink2=" + g.ToString()); }
        private static bool GuidIsZero(Guid g) { return g == Guid.Empty; }
        private static bool GuidIsZero2(Guid g) { return g != Guid.Empty; }

        // ---------------- DateTime leaves ----------------
        private static DateTime NextDay(DateTime d) { return d.AddDays(1); }
        private static DateTime NextWeek(DateTime d) { return d.AddDays(7); }
        private static void DateSink(DateTime d) { Console.WriteLine("dsink=" + d.Ticks); }
        private static void DateSink2(DateTime d) { Console.WriteLine("dsink2=" + d.Ticks); }
        private static bool IsLeapDate(DateTime d) { return DateTime.IsLeapYear(d.Year); }
        private static bool IsLaterThan2000(DateTime d) { return d.Year > 2000; }

        // ---------------- TimeSpan leaves ----------------
        private static int SpanSecs(TimeSpan t) { return t.Seconds; }
        private static int SpanMinutes(TimeSpan t) { return t.Minutes; }
        private static void SpanSink(TimeSpan t) { Console.WriteLine("tsink=" + t.Ticks); }
        private static void SpanSink2(TimeSpan t) { Console.WriteLine("tsink2=" + t.Ticks); }

        // ---------------- decimal leaves ----------------
        private static decimal Twice(decimal d) { return d * 2m; }
        private static decimal AddTen(decimal d) { return d + 10m; }
        private static bool IsPos(decimal d) { return d > 0m; }
        private static bool IsNeg(decimal d) { return d < 0m; }

        public static void Main()
        {
            Guid zero = Guid.Parse("00000000-0000-0000-0000-000000000000");
            Guid one = Guid.Parse("00000000-0000-0000-0000-000000000001");
            GuidInst gi = new GuidInst(5);

            // =============== Guid: Func<Guid,int> ===============
            Func<Guid, int> r = GuidRank;
            Console.WriteLine("g_int_direct=" + r(one));
            r += GuidRank2;
            Console.WriteLine("g_int_multi=" + r(one));
            Func<Guid, int> rm = GuidRank;
            rm += gi.Rank;
            Console.WriteLine("g_int_mixed=" + rm(one));
            Console.WriteLine("g_int_mixed_zero=" + rm(zero));

            // =============== Guid: Func<Guid,string> ===============
            Func<Guid, string> s = GuidText;
            Console.WriteLine("g_str_direct=" + s(one));
            s += gi.Text;
            Console.WriteLine("g_str_mixed=" + s(one));

            // =============== Guid: Action<Guid> ===============
            Action<Guid> a = GuidSink;
            a(one);
            a += GuidSink2;
            a += gi.Sink;
            a(one);

            // =============== Guid: Predicate<Guid> ===============
            Predicate<Guid> p = GuidIsZero;
            Console.WriteLine("g_pred_direct_zero=" + p(zero));
            Console.WriteLine("g_pred_direct_one=" + p(one));
            p += GuidIsZero2;
            Console.WriteLine("g_pred_multi_zero=" + p(zero));
            Console.WriteLine("g_pred_multi_one=" + p(one));
            Predicate<Guid> pm = GuidIsZero;
            pm += gi.IsZero;
            Console.WriteLine("g_pred_mixed_zero=" + pm(zero));

            // =============== DateTime: Func<DateTime,DateTime> ===============
            DateTime d0 = new DateTime(2020, 1, 1, 0, 0, 0);
            Func<DateTime, DateTime> dd = NextDay;
            Console.WriteLine("d_dt_direct=" + dd(d0).Ticks);
            dd += NextWeek;
            Console.WriteLine("d_dt_multi=" + dd(d0).Ticks);
            Func<DateTime, DateTime> dm = NextDay;
            dm += gi.Plus;
            Console.WriteLine("d_dt_mixed=" + dm(d0).Ticks);

            // =============== DateTime: Action<DateTime> ===============
            Action<DateTime> ad = DateSink;
            ad(d0);
            ad += DateSink2;
            ad += gi.DSink;
            ad(d0);

            // =============== DateTime: Predicate<DateTime> ===============
            Predicate<DateTime> pd = IsLeapDate;
            Console.WriteLine("d_pred_direct_leap=" + pd(d0));
            Console.WriteLine("d_pred_direct_nonleap=" + pd(new DateTime(2021, 5, 6, 7, 8, 9)));
            pd += IsLaterThan2000;
            Console.WriteLine("d_pred_multi_2021=" + pd(new DateTime(2021, 5, 6, 7, 8, 9)));
            Predicate<DateTime> pdm = IsLeapDate;
            pdm += gi.IsBefore2000;
            Console.WriteLine("d_pred_mixed_2020=" + pdm(d0));

            // =============== TimeSpan: Func<TimeSpan,int> ===============
            TimeSpan ts = new TimeSpan(0, 2, 30);
            Func<TimeSpan, int> ft = SpanSecs;
            Console.WriteLine("t_int_direct=" + ft(ts));
            ft += SpanMinutes;
            Console.WriteLine("t_int_multi=" + ft(ts));
            Func<TimeSpan, int> ftm = SpanSecs;
            ftm += gi.SpanSec;
            Console.WriteLine("t_int_mixed=" + ftm(ts));

            // =============== TimeSpan: Action<TimeSpan> ===============
            Action<TimeSpan> at = SpanSink;
            at(ts);
            at += SpanSink2;
            at += gi.TSink;
            at(ts);

            // =============== decimal: Func<decimal,decimal> ===============
            decimal dval = 2.5m;
            Func<decimal, decimal> fd = Twice;
            Console.WriteLine("m_dec_direct=" + fd(dval).ToString());
            fd += AddTen;
            Console.WriteLine("m_dec_multi=" + fd(dval).ToString());
            Func<decimal, decimal> fdm = Twice;
            fdm += gi.Mul;
            Console.WriteLine("m_dec_mixed=" + fdm(dval).ToString());

            // =============== decimal: Predicate<decimal> ===============
            Predicate<decimal> pr = IsPos;
            Console.WriteLine("m_pred_direct_pos=" + pr(dval));
            Console.WriteLine("m_pred_direct_neg=" + pr(-dval));
            pr += IsNeg;
            Console.WriteLine("m_pred_multi_pos=" + pr(dval));
            Console.WriteLine("m_pred_multi_neg=" + pr(-dval));
            Predicate<decimal> prm = IsPos;
            prm += gi.IsBig;
            Console.WriteLine("m_pred_mixed=" + prm(200m));
        }
    }
}
