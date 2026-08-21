//------------------------------------------------------------------------------
// datetime2deep.cs -- DateTime domain DEEP probe 2 (e2e batch C62, after the
// datetime domain landed -- time.cpp / time_ext.h / timeext2.h wired in M5/M6):
// ADD / SUBTRACT / COMPARE / BOUNDARY. Goes deeper than dateops (C27: ctor
// forms / Parse round trip / basic Compare) and datetime2 (C29: ParseExact /
// yy expansion / ToString G/fff), which this case builds on:
//
//   1) ADD     -- AddDays / AddHours / AddMinutes / AddTicks with fractional,
//                negative and chained deltas; component verification through
//                Year..Second accessors and "yyyy-MM-dd HH:mm:ss" round trips;
//                the equivalence AddDays(1) == AddTicks(864000000000).
//   2) SUBTRACT-- DateTime - DateTime / Subtract(DateTime) -> TimeSpan across
//                midnight / month / year boundaries, negative spans, and the
//                TimeSpan getter surface (Days/Hours/Minutes/Seconds/Ticks/
//                Total*). Subtract(TimeSpan) and SubtractTicks are NOT net48
//                members (LOUD) -- only the (DateTime, DateTime) shapes run.
//   3) COMPARE -- DateTime.CompareTo + static DateTime.Compare sign grid:
//                year/month/day/hour/minute/second/tick differences, equality,
//                sign symmetry, transitivity, Compare-vs-CompareTo agreement,
//                and the tick-equivalence identity (same ticks -> 0).
//   4) BOUNDARY-- ctor validation (year 0/10000, month 0/13, day 0/32,
//                Feb 30, hour 24, minute 60, second 60 -> ArgumentOutOfRange-
//                Exception), AddDays/AddHours/AddTicks overflow past the
//                0001-01-01 and 9999-12-31 extremes -> ArgumentOutOfRange-
//                Exception, and the boundary dates themselves (min/max reachable
//                via ctor -- DateTime.MinValue/MaxValue static fields are not
//                probed).
//
// Reachable-surface discipline (dateops / datetime2 precedents): the DateTime
// relational operators (== < > <= >=), get_Ticks, get_Date, AddMonths/Years/
// Seconds/Milliseconds, op_Addition and Subtract(TimeSpan) are LOUD
// (MethodMap.DateTimeExtra.cs) -- EVERY comparison routes through CompareTo /
// Compare and every tick read through the TimeSpan surface. DateTime.MinValue/
// MaxValue static fields are avoided (their class-static storage is not
// verified for DateTime); boundary values are constructed with the wired ctors.
//
// Every line prints "label=True/False" or a pinned formatted string; net48 is
// the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class DateTime2Deep {
    // ---------- 1. ADD: fractional / negative / chained deltas ----------
    private static void S1Add() {
        DateTime baseT = new DateTime(2024, 2, 29, 12, 30, 45);  // leap-day 12:30:45
        // AddDays fractional: +0.5d == +12h. baseT 12:30:45 + 12h rolls past
        // midnight into 2024-03-01 (leap-year Feb has 29 days).
        DateTime ad1 = baseT.AddDays(0.5);
        Console.WriteLine("ad1_str=" + (ad1.ToString("yyyy-MM-dd HH:mm:ss") == "2024-03-01 00:30:45"));
        Console.WriteLine("ad1_day=" + (ad1.Day == 1));
        DateTime ad2 = baseT.AddDays(1.25);
        Console.WriteLine("ad2_str=" + (ad2.ToString("yyyy-MM-dd HH:mm:ss") == "2024-03-01 18:30:45"));
        // AddDays negative across the leap-day boundary.
        DateTime ad3 = baseT.AddDays(-1);
        Console.WriteLine("ad3_str=" + (ad3.ToString("yyyy-MM-dd") == "2024-02-28"));
        DateTime ad4 = baseT.AddDays(-1.5);
        Console.WriteLine("ad4_str=" + (ad4.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-28 00:30:45"));
        // AddHours: +24 wraps a full day; fraction +0.5h == +30min.
        DateTime ah1 = baseT.AddHours(24);
        Console.WriteLine("ah1=" + (ah1.ToString("yyyy-MM-dd HH:mm:ss") == "2024-03-01 12:30:45"));
        DateTime ah2 = baseT.AddHours(0.5);
        Console.WriteLine("ah2=" + (ah2.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 13:00:45"));
        DateTime ah3 = baseT.AddHours(-12);
        Console.WriteLine("ah3=" + (ah3.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 00:30:45"));
        // AddMinutes: +90 == +1h30m; -60 == -1h.
        DateTime am1 = baseT.AddMinutes(90);
        Console.WriteLine("am1=" + (am1.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 14:00:45"));
        DateTime am2 = baseT.AddMinutes(-60);
        Console.WriteLine("am2=" + (am2.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 11:30:45"));
        // AddTicks: +1s == +10000000 ticks; negative ticks; exact 0 identity.
        DateTime at1 = baseT.AddTicks(10000000L);
        Console.WriteLine("at1=" + (at1.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 12:30:46"));
        DateTime at2 = baseT.AddTicks(-10000000L);
        Console.WriteLine("at2=" + (at2.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 12:30:44"));
        DateTime at3 = baseT.AddTicks(0);
        Console.WriteLine("at3=" + (at3.CompareTo(baseT) == 0));
        // Equivalence: AddDays(1) == AddTicks(864000000000) (same tick delta).
        DateTime eq1 = baseT.AddDays(1);
        DateTime eq2 = baseT.AddTicks(864000000000L);
        Console.WriteLine("add_eq=" + (eq1.CompareTo(eq2) == 0));
        // Chained add: +3d +2h +30m +7s (AddSeconds is LOUD -- the +7s rides
        // AddTicks: 7s == 70000000 ticks). baseT 2024-02-29 12:30:45
        //   +3d -> 2024-03-03 12:30:45, +2h -> 14:30:45, +30m -> 15:00:45,
        //   +7s -> 15:00:52.
        DateTime ch = baseT.AddDays(3).AddHours(2).AddMinutes(30).AddTicks(70000000L);
        Console.WriteLine("ch_str=" + (ch.ToString("yyyy-MM-dd HH:mm:ss") == "2024-03-03 15:00:52"));
    }

    // ---------- 2. SUBTRACT: spans across boundaries ----------
    private static void S2Subtract() {
        // midnight-crossing span: 2024-06-15 23:30:00 -> 2024-06-16 01:15:00.
        DateTime s1 = new DateTime(2024, 6, 15, 23, 30, 0);
        DateTime s2 = new DateTime(2024, 6, 16, 1, 15, 0);
        TimeSpan sp1 = s2 - s1;
        Console.WriteLine("sb_days=" + (sp1.Days == 0));
        Console.WriteLine("sb_hours=" + (sp1.Hours == 1));
        Console.WriteLine("sb_min=" + (sp1.Minutes == 45));
        Console.WriteLine("sb_sec=" + (sp1.Seconds == 0));
        Console.WriteLine("sb_ticks=" + (sp1.Ticks == 63000000000L));  // 1h45m = 6300s * 1e7
        Console.WriteLine("sb_total_m=" + (sp1.TotalMinutes == 105.0));
        // month/year-crossing span: 2024-01-31 -> 2024-03-01.
        TimeSpan sp2 = new DateTime(2024, 3, 1) - new DateTime(2024, 1, 31);
        Console.WriteLine("sb2_days=" + (sp2.Days == 30));
        Console.WriteLine("sb2_hours=" + (sp2.Hours == 0));
        // year-crossing: 2023-12-31 -> 2024-01-02.
        TimeSpan sp3 = new DateTime(2024, 1, 2) - new DateTime(2023, 12, 31);
        Console.WriteLine("sb3=" + (sp3.TotalDays == 2.0));
        // negative span: op_Subtraction order flip -> negative ticks.
        TimeSpan sp4 = s1 - s2;
        Console.WriteLine("sb_neg=" + (sp4.Ticks == -63000000000L));
        // Subtract(DateTime) method agrees with op_Subtraction.
        TimeSpan sp5 = s2.Subtract(s1);
        Console.WriteLine("sb_method=" + (sp5.Ticks == sp1.Ticks));
        // reverse Subtract mirrors sign.
        TimeSpan sp6 = s1.Subtract(s2);
        Console.WriteLine("sb_method_neg=" + (sp6.Ticks == sp4.Ticks));
        // span components via TimeSpan ctor(ticks) round trip.
        TimeSpan sp7 = new TimeSpan(sp1.Ticks);
        Console.WriteLine("sb_ctor_rt=" + (sp7.TotalMinutes == 105.0));
        // two spans that equal in ticks compare 0 (TimeSpan.CompareTo wired).
        Console.WriteLine("sb_cmp=" + (sp1.CompareTo(new TimeSpan(1, 45, 0)) == 0));
        Console.WriteLine("sb_cmp_neg=" + (sp1.CompareTo(new TimeSpan(2, 0, 0)) < 0));
    }

    // ---------- 3. COMPARE: sign grid + symmetry + transitivity ----------
    private static void S3Compare() {
        DateTime y2023 = new DateTime(2023, 6, 15, 12, 0, 0);
        DateTime y2024 = new DateTime(2024, 6, 15, 12, 0, 0);
        DateTime m0124 = new DateTime(2024, 1, 15, 12, 0, 0);
        DateTime d0102 = new DateTime(2024, 6, 2, 12, 0, 0);
        DateTime d16 = new DateTime(2024, 6, 16, 12, 0, 0);
        DateTime h13 = new DateTime(2024, 6, 15, 13, 0, 0);
        DateTime mi46 = new DateTime(2024, 6, 15, 12, 46, 0);
        DateTime s46 = new DateTime(2024, 6, 15, 12, 0, 46);
        DateTime tPlus = new DateTime(2024, 6, 15, 12, 0, 0).AddTicks(1);
        DateTime tBase = new DateTime(2024, 6, 15, 12, 0, 0);
        // component-wise sign grid: each later component yields +1, earlier -1.
        Console.WriteLine("cp_year=" + (y2024.CompareTo(y2023) > 0 && y2023.CompareTo(y2024) < 0));
        Console.WriteLine("cp_month=" + (y2024.CompareTo(m0124) > 0 && m0124.CompareTo(y2024) < 0));
        Console.WriteLine("cp_day=" + (d16.CompareTo(d0102) > 0 && d0102.CompareTo(d16) < 0));
        Console.WriteLine("cp_hour=" + (h13.CompareTo(tBase) > 0 && tBase.CompareTo(h13) < 0));
        Console.WriteLine("cp_min=" + (mi46.CompareTo(tBase) > 0 && tBase.CompareTo(mi46) < 0));
        Console.WriteLine("cp_sec=" + (s46.CompareTo(tBase) > 0 && tBase.CompareTo(s46) < 0));
        Console.WriteLine("cp_tick=" + (tPlus.CompareTo(tBase) > 0 && tBase.CompareTo(tPlus) < 0));
        // equality: same components + same ticks -> 0 both ways.
        Console.WriteLine("cp_eq=" + (tBase.CompareTo(new DateTime(2024, 6, 15, 12, 0, 0)) == 0));
        Console.WriteLine("cp_eq_ticks=" + (tPlus.CompareTo(tPlus) == 0));
        // sign symmetry: Compare(a,b) == -Compare(b,a).
        Console.WriteLine("cp_sym=" + (DateTime.Compare(y2023, y2024) == -DateTime.Compare(y2024, y2023)));
        Console.WriteLine("cp_sym2=" + (DateTime.Compare(d0102, d16) == -DateTime.Compare(d16, d0102)));
        // transitivity: a < b, b < c => a < c (ordered triple Jan15 < Jun2 < Jun16).
        Console.WriteLine("cp_trans=" + (m0124.CompareTo(d0102) < 0 && d0102.CompareTo(d16) < 0 && m0124.CompareTo(d16) < 0));
        // static Compare == instance CompareTo agreement (sign preserved).
        Console.WriteLine("cp_agree=" + (DateTime.Compare(m0124, d16) == m0124.CompareTo(d16)));
        // full -1/0/+1 encoding.
        Console.WriteLine("cp_minus1=" + (DateTime.Compare(m0124, d16) == -1));
        Console.WriteLine("cp_zero=" + (DateTime.Compare(tBase, new DateTime(2024, 6, 15, 12, 0, 0)) == 0));
        Console.WriteLine("cp_plus1=" + (DateTime.Compare(d16, m0124) == 1));
    }

    // ---------- 4. BOUNDARY: ctor validation + overflow ----------
    private static void S4Boundary() {
        // valid extremes reachable via ctor.
        DateTime dMin = new DateTime(1, 1, 1);
        Console.WriteLine("bd_min=" + (dMin.Year == 1 && dMin.Month == 1 && dMin.Day == 1));
        Console.WriteLine("bd_min_dow=" + ((int)dMin.DayOfWeek == 1));       // 0001-01-01 = Monday
        DateTime dMax = new DateTime(9999, 12, 31, 23, 59, 59);
        Console.WriteLine("bd_max=" + (dMax.Year == 9999 && dMax.Hour == 23 && dMax.Second == 59));
        Console.WriteLine("bd_max_dow=" + ((int)dMax.DayOfWeek == 5));       // 9999-12-31 = Friday
        // ctor validation (ArgumentOutOfRangeException).
        Console.WriteLine("bd_y0=" + ThrowsCtor(0, 1, 1));
        Console.WriteLine("bd_y10000=" + ThrowsCtor(10000, 1, 1));
        Console.WriteLine("bd_m0=" + ThrowsCtor(2024, 0, 1));
        Console.WriteLine("bd_m13=" + ThrowsCtor(2024, 13, 1));
        Console.WriteLine("bd_d0=" + ThrowsCtor(2024, 1, 0));
        Console.WriteLine("bd_d32=" + ThrowsCtor(2024, 1, 32));
        Console.WriteLine("bd_feb30=" + ThrowsCtor(2024, 2, 30));
        Console.WriteLine("bd_feb30nl=" + ThrowsCtor(2023, 2, 29));
        Console.WriteLine("bd_apr31=" + ThrowsCtor(2024, 4, 31));
        Console.WriteLine("bd_h24=" + ThrowsCtorH(2024, 6, 15, 24, 0, 0));
        Console.WriteLine("bd_mi60=" + ThrowsCtorH(2024, 6, 15, 23, 60, 0));
        Console.WriteLine("bd_s60=" + ThrowsCtorH(2024, 6, 15, 23, 59, 60));
        // AddDays overflow past the extremes (inline try/catch -- a DateTime
        // VALUE cannot ride a helper-method argument slot, so each probe owns
        // its handler; the bool is produced inside the handler).
        bool bd1 = false;
        try { DateTime x = dMin.AddDays(-1); } catch (ArgumentOutOfRangeException) { bd1 = true; }
        Console.WriteLine("bd_addmin=" + bd1);
        bool bd2 = false;
        try { DateTime x = dMax.AddDays(1); } catch (ArgumentOutOfRangeException) { bd2 = true; }
        Console.WriteLine("bd_addmax=" + bd2);
        Console.WriteLine("bd_addmax_ok=" + (new DateTime(9999, 12, 30).AddDays(1).Year == 9999));
        // AddHours overflow.
        bool bd3 = false;
        try { DateTime x = dMax.AddHours(1); } catch (ArgumentOutOfRangeException) { bd3 = true; }
        Console.WriteLine("bd_addhmax=" + bd3);
        // AddTicks overflow: max-date +1s leaves the valid range.
        bool bd4 = false;
        try { DateTime x = dMax.AddTicks(10000000L); } catch (ArgumentOutOfRangeException) { bd4 = true; }
        Console.WriteLine("bd_ticksmax=" + bd4);
        bool bd5 = false;
        try { DateTime x = dMin.AddTicks(-1L); } catch (ArgumentOutOfRangeException) { bd5 = true; }
        Console.WriteLine("bd_ticksmin=" + bd5);
        // just-inside tick boundary stays valid.
        Console.WriteLine("bd_ticksok=" + (dMax.AddTicks(9999999L).ToString("yyyy-MM-dd HH:mm:ss") == "9999-12-31 23:59:59"));
    }

    // Exception helpers (INT-arg only -- a DateTime VALUE cannot be a helper
    // method argument; the bool is produced INSIDE the handler -- the
    // transpiler rejects code after a try/catch construct).
    private static bool ThrowsCtor(int y, int m, int d) {
        try { new DateTime(y, m, d); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }

    private static bool ThrowsCtorH(int y, int m, int d, int h, int mi, int s) {
        try { new DateTime(y, m, d, h, mi, s); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }

    public static void Main() {
        S1Add();
        S2Subtract();
        S3Compare();
        S4Boundary();
    }
}}
