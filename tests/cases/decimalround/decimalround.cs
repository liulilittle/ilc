//------------------------------------------------------------------------------
// decimalround.cs -- System.Decimal ROUNDING DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C79, AFTER the decimal domain
// closure: decimal.cpp InternalRound/RoundCore + decimal_format.cpp + the
// MethodMap.Decimal.cs Round overload mappings (1/2/3-arg, with the missing
// decimals/mode defaults injected as literal 0s -- decgap-b23).
//
// THIS case drives the ROUNDING dimension one stage deeper than decimaldeep2
// (C43, which pinned the ToEven/AwayFromZero core + Floor/Ceiling/Truncate
// direction) and decimalcmp (C62, Compare-vs-round interactions):
//
//   1. TOEVEN MIDPOINT LATTICE at decimals=0: every .5 value across the
//      integer span -- 0.5/1.5/2.5/3.5/4.5 and their negatives -- rounds to
//      the EVEN neighbor. This is the banker's-rounding contract the 1-arg
//      and 2-arg overloads use (mode default = 0).
//   2. AWAYFROMZERO MIDPOINT LATTICE: the same .5 values round AWAY from
//      zero (0.5->1, -0.5->-1), and non-midpoint values behave identically
//      to ToEven (the two modes differ ONLY at exact midpoints).
//   3. MODE VALIDATION (net48 oracle PROBE-VERIFIED 2026-08-13): the net48
//      decimal.Round accepts ONLY MidpointRounding values 0 (ToEven) and 1
//      (AwayFromZero); (MidpointRounding)2/3/4 all throw ArgumentException
//      ("invalid MidpointRounding ... value"). The runtime's RoundCore
//      previously mapped 2=ToZero, 3=ToNegativeInfinity, 4=ToPositiveInfinity
//      (decimal.cpp InternalRound) -- a netcore-style extension net48 never
//      exposes through Round. D-DR-1 FIX: RoundCore now rejects mode > 1, so
//      2/3/4 throw ArgumentException on BOTH sides and ARE probed (mb_2_throw/
//      mb_3_throw/mb_4_throw below). The netcore rounding semantics remain
//      reachable only via Truncate/Floor/Ceiling.
//   4. DECIMALS BOUNDARY: decimals 0..28 accepted, 29 and -1 throw
//      ArgumentOutOfRangeException (net48 contract, runtime RoundCore).
//   5. MODE BOUNDARY beyond the valid pair: (MidpointRounding)5 and -1 throw
//      ArgumentException on BOTH sides (runtime RoundCore's (uint32)mode > 4
//      guard fires for 5 and for the huge uint32 of -1).
//   6. SCALE-BOUNDARY ROUNDING: rounding a scale-28 unit (1e-28) to 27
//      decimals collapses to zero; rounding to 28 decimals is a no-op;
//      decimal.MaxValue / MinValue round back to themselves at decimals 0.
//   7. CARRY PROPAGATION: 999.995 -> 1000.00, 999.9999 -> 1000.000,
//      9.999999m -> 10.00, 999999999.5 -> 1000000000, and a MaxValue/10
//      boundary carry.
//   8. VALUE-OUTPUT ROUND TRIPS: the rounded VALUES print byte-exact through
//      string.Format("{0}") -- layout (trailing zeros, scale preservation)
//      verified against net48.
//
// DISCIPLINE (decimaldeep2/decimalcmp precedents -- KEEP): EVERY decimal
// value is a NAMED decimal local first; Round/Floor/Ceiling/Truncate results
// land in named locals; value output rides string.Format("{0}", x);
// comparison results print as labeled bools. No boxed-object Equals/CompareTo
// (LOUD), no float/double conversions (LOUD).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Decimalround {
    // ---------- 1. ToEven midpoint lattice at decimals = 0 ----------
    private static void S1ToEven() {
        decimal r0 = decimal.Round(0.5m);
        Console.WriteLine("te_05=" + (r0 == 0m));
        decimal r1 = decimal.Round(1.5m);
        Console.WriteLine("te_15=" + (r1 == 2m));
        decimal r2 = decimal.Round(2.5m);
        Console.WriteLine("te_25=" + (r2 == 2m));
        decimal r3 = decimal.Round(3.5m);
        Console.WriteLine("te_35=" + (r3 == 4m));
        decimal r4 = decimal.Round(4.5m);
        Console.WriteLine("te_45=" + (r4 == 4m));
        decimal r5 = decimal.Round(-0.5m);
        Console.WriteLine("te_n05=" + (r5 == 0m));
        decimal r6 = decimal.Round(-1.5m);
        Console.WriteLine("te_n15=" + (r6 == -2m));
        decimal r7 = decimal.Round(-2.5m);
        Console.WriteLine("te_n25=" + (r7 == -2m));
        decimal r8 = decimal.Round(-3.5m);
        Console.WriteLine("te_n35=" + (r8 == -4m));
        decimal r9 = decimal.Round(-4.5m);
        Console.WriteLine("te_n45=" + (r9 == -4m));
        // non-midpoint values: below the tie rounds down, above rounds up
        decimal r10 = decimal.Round(2.4m);
        Console.WriteLine("te_24=" + (r10 == 2m));
        decimal r11 = decimal.Round(2.6m);
        Console.WriteLine("te_26=" + (r11 == 3m));
        decimal r12 = decimal.Round(-2.4m);
        Console.WriteLine("te_n24=" + (r12 == -2m));
        decimal r13 = decimal.Round(-2.6m);
        Console.WriteLine("te_n26=" + (r13 == -3m));
        // exact integers / zero are identity
        decimal r14 = decimal.Round(5m);
        Console.WriteLine("te_5=" + (r14 == 5m));
        decimal r15 = decimal.Round(0m);
        Console.WriteLine("te_0=" + (r15 == 0m));
        // 2-arg form defaults to ToEven (mode literal 0)
        decimal r16 = decimal.Round(1.25m, 1);
        Console.WriteLine("te_125_1=" + (r16 == 1.2m));
        decimal r17 = decimal.Round(1.35m, 1);
        Console.WriteLine("te_135_1=" + (r17 == 1.4m));
        decimal r18 = decimal.Round(2.675m, 2);
        Console.WriteLine("te_2675_2=" + (r18 == 2.68m));
        decimal r19 = decimal.Round(2.685m, 2);
        Console.WriteLine("te_2685_2=" + (r19 == 2.68m));
        decimal r20 = decimal.Round(1.005m, 2);
        Console.WriteLine("te_1005_2=" + (r20 == 1.00m));
    }

    // ---------- 2. AwayFromZero midpoint lattice (mode = 1) ----------
    private static void S2Away() {
        decimal r0 = decimal.Round(0.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_05=" + (r0 == 1m));
        decimal r1 = decimal.Round(1.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_15=" + (r1 == 2m));
        decimal r2 = decimal.Round(2.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_25=" + (r2 == 3m));
        decimal r3 = decimal.Round(3.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_35=" + (r3 == 4m));
        decimal r4 = decimal.Round(-0.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_n05=" + (r4 == -1m));
        decimal r5 = decimal.Round(-1.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_n15=" + (r5 == -2m));
        decimal r6 = decimal.Round(-2.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_n25=" + (r6 == -3m));
        // non-midpoint values agree with ToEven
        decimal r7 = decimal.Round(2.4m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_24=" + (r7 == 2m));
        decimal r8 = decimal.Round(-2.4m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_n24=" + (r8 == -2m));
        // decimals > 0 with AwayFromZero
        decimal r9 = decimal.Round(1.005m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_1005_2=" + (r9 == 1.01m));
        decimal r10 = decimal.Round(1.005m, 2, MidpointRounding.AwayFromZero);
        decimal r11 = decimal.Round(1.005m, 2, MidpointRounding.ToEven);
        Console.WriteLine("af_diff=" + (r10 != r11));
        decimal r12 = decimal.Round(2.675m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_2675_2=" + (r12 == 2.68m));
        decimal r13 = decimal.Round(2.685m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_2685_2=" + (r13 == 2.69m));
        decimal r14 = decimal.Round(2.25m, 1, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_225_1=" + (r14 == 2.3m));
        decimal r15 = decimal.Round(-2.25m, 1, MidpointRounding.AwayFromZero);
        Console.WriteLine("af_n225_1=" + (r15 == -2.3m));
    }

    // ---------- 3. decimals boundary: 0 and 28 accepted, 29/-1 throw ----------
    private static void S3Decimals() {
        decimal r0 = decimal.Round(1.5m, 0);
        Console.WriteLine("db_0=" + (r0 == 2m));
        decimal tiny = 0.0000000000000000000000000001m; // 1e-28, scale 28
        decimal r1 = decimal.Round(tiny, 28);
        Console.WriteLine("db_28_keep=" + (r1 == tiny));
        decimal r2 = decimal.Round(tiny, 27);
        Console.WriteLine("db_27_collapse=" + (r2 == 0m));
        decimal r3 = decimal.Round(tiny, 0);
        Console.WriteLine("db_0_collapse=" + (r3 == 0m));
        decimal r4 = decimal.Round(2.5m, 28);
        Console.WriteLine("db_28_nop=" + (r4 == 2.5m));
        bool a1 = false;
        try { decimal x = decimal.Round(1.5m, 29); Console.WriteLine("db_29=" + x); }
        catch (ArgumentOutOfRangeException) { a1 = true; }
        Console.WriteLine("db_29_throw=" + a1);
        bool a2 = false;
        try { decimal x = decimal.Round(1.5m, -1); Console.WriteLine("db_neg1=" + x); }
        catch (ArgumentOutOfRangeException) { a2 = true; }
        Console.WriteLine("db_neg1_throw=" + a2);
    }

    // ---------- 4. mode boundary: 0/1 valid, 2/3/4/5/-1 throw ArgumentException ----------
    // D-DR-1 FIX (2026-08-13): net48 decimal.Round accepts ONLY MidpointRounding
    // 0 (ToEven) and 1 (AwayFromZero); (MidpointRounding)2/3/4 throw
    // ArgumentException -- the runtime's RoundCore previously mapped them to
    // ToZero/ToNegativeInfinity/ToPositiveInfinity (a netcore-style extension
    // net48 never exposes through Round). These three were the RECORDED (not
    // probed) byte-diff gap in GAPS.md; now that both sides reject them, they
    // ARE probed below.
    private static void S4Modes() {
        decimal r0 = decimal.Round(2.5m, 0, MidpointRounding.ToEven);
        decimal r1 = decimal.Round(2.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("mb_0_1=" + (r0 == 2m && r1 == 3m));
        bool a0 = false;
        try { decimal x = decimal.Round(1.5m, 0, (MidpointRounding)2); Console.WriteLine("mb_2=" + x); }
        catch (ArgumentException) { a0 = true; }
        Console.WriteLine("mb_2_throw=" + a0);
        bool a1 = false;
        try { decimal x = decimal.Round(1.5m, 0, (MidpointRounding)3); Console.WriteLine("mb_3=" + x); }
        catch (ArgumentException) { a1 = true; }
        Console.WriteLine("mb_3_throw=" + a1);
        bool a2 = false;
        try { decimal x = decimal.Round(1.5m, 0, (MidpointRounding)4); Console.WriteLine("mb_4=" + x); }
        catch (ArgumentException) { a2 = true; }
        Console.WriteLine("mb_4_throw=" + a2);
        bool a3 = false;
        try { decimal x = decimal.Round(1.5m, 0, (MidpointRounding)5); Console.WriteLine("mb_5=" + x); }
        catch (ArgumentException) { a3 = true; }
        Console.WriteLine("mb_5_throw=" + a3);
        bool a4 = false;
        try { decimal x = decimal.Round(1.5m, 0, (MidpointRounding)(-1)); Console.WriteLine("mb_neg1=" + x); }
        catch (ArgumentException) { a4 = true; }
        Console.WriteLine("mb_neg1_throw=" + a4);
    }

    // ---------- 5. scale-boundary rounding on MaxValue/MinValue ----------
    private static void S5Scale() {
        decimal mx = decimal.MaxValue;
        decimal mn = decimal.MinValue;
        decimal r0 = decimal.Round(mx, 0);
        Console.WriteLine("sc_max0=" + (r0 == mx));
        decimal r1 = decimal.Round(mn, 0);
        Console.WriteLine("sc_min0=" + (r1 == mn));
        // MaxValue/10 + .5 -> (MaxValue/10 + 1) carry (MaxValue itself has no
        // fractional room; the /10 scaled form is exactly representable)
        decimal tenth = 7922816251426433759354395033m;
        decimal half = tenth + 0.5m;
        decimal r2 = decimal.Round(half, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("sc_maxhalf_away=" + (r2 == 7922816251426433759354395034m));
        decimal r3 = decimal.Round(half, 0, MidpointRounding.ToEven);
        Console.WriteLine("sc_maxhalf_even=" + (r3 == 7922816251426433759354395034m));
        decimal negHalf = -tenth + -0.5m;
        decimal r4 = decimal.Round(negHalf, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("sc_negmaxhalf_away=" + (r4 == -7922816251426433759354395034m));
    }

    // ---------- 6. carry propagation across the digit boundary ----------
    private static void S6Carry() {
        decimal r0 = decimal.Round(999.995m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_999995=" + (r0 == 1000.00m));
        decimal r1 = decimal.Round(999.9999m, 3, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_9999999=" + (r1 == 1000.000m));
        decimal r2 = decimal.Round(9.999999m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_9999999_2=" + (r2 == 10.00m));
        decimal r3 = decimal.Round(0.00099999m, 5, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_00099999=" + (r3 == 0.00100m));
        decimal r4 = decimal.Round(999999999.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_9999999995=" + (r4 == 1000000000m));
        decimal r5 = decimal.Round(1999999999.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_19999999995=" + (r5 == 2000000000m));
        // to-string round trip of a carry result (scale preserved)
        decimal r6 = decimal.Round(999.995m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("cr_val=" + string.Format("{0}", r6));
    }

    // ---------- 7. value-output round trips (string.Format layout) ----------
    private static void S7Value() {
        decimal r0 = decimal.Round(12345.6789m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_12345=" + string.Format("{0}", r0));
        decimal r1 = decimal.Round(12345.6789m, 3, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_123456789=" + string.Format("{0}", r1));
        decimal r2 = decimal.Round(12345.6789m, 4, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_123456789_4=" + string.Format("{0}", r2));
        decimal r3 = decimal.Round(1.2345m, 3, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_12345_3=" + string.Format("{0}", r3));
        decimal r4 = decimal.Round(1.005m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_1005=" + string.Format("{0}", r4));
        decimal r5 = decimal.Round(2.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_25=" + string.Format("{0}", r5));
        decimal r6 = decimal.Round(-2.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_n25=" + string.Format("{0}", r6));
        decimal r7 = decimal.Round(1m, 2, MidpointRounding.ToEven);
        Console.WriteLine("vo_1m2=" + string.Format("{0}", r7));
        decimal r8 = decimal.Round(1000.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_10005=" + string.Format("{0}", r8));
        decimal r9 = decimal.Round(0.5m, 1, MidpointRounding.AwayFromZero);
        Console.WriteLine("vo_05_1=" + string.Format("{0}", r9));
    }

    public static void Main() {
        S1ToEven();
        S2Away();
        S3Decimals();
        S4Modes();
        S5Scale();
        S6Carry();
        S7Value();
        Console.WriteLine("done");
    }
}}
