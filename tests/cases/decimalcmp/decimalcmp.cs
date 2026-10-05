//------------------------------------------------------------------------------
// decimalcmp.cs -- decimal COMPARE deep probe (e2e batch C62, after the
// decimal domain landed -- decimal.cpp / decimal_format.cpp + MethodMap.Decimal.cs):
// the Compare FULL GRID and the ROUNDING INTERACTIONS, byte-exact vs the net48
// oracle, dual-compiler (mingw + msvc).
//
// Goes deeper than decops (C25: Compare/CompareTo sign, scale-insensitive
// equality) and decimalops2 (C54: operator lattice + Max/Min extremes): this
// case is ENTIRELY Compare-focused on the VarDecCmp semantics
// (icall_decimal_compare, -1/0/1):
//
//   1) S1 SIGN GRID -- raw int32 Compare outputs across the full sign /
//      magnitude matrix: (0,0) (0,+),(+,0),(0,-),(-,0),(-,+),(+,-),(-,-),
//      including scale-insensitive zero (0m vs 0.00m) and scale-insensitive
//      equality (1m vs 1.00m).
//   2) S2 SCALE + MAGNITUDE -- scale-equality lattice (1.5m vs 1.50m vs
//      1.500m), adjacent-value discrimination at the last digit, the smallest
//      nonzero (1e-28), int32-MaxValue boundary, and decimal.MaxValue /
//      decimal.MinValue extremes (the BCL static storage proven in decimalops2).
//   3) S3 ROUNDING INTERACTIONS -- rounding CHANGES comparison outcomes:
//      2.675m vs 2.685m (both ToEven-round to 2.68m, Compare == 0, while the
//      unrounded values Compare == -1); midpoint ToEven vs AwayFromZero on the
//      same value (-2.5m -> -2m vs -3m); the 1m/3m*3m = 0.999...9 < 1 lattice
//      (Round back to 1, Floor -> 0, Ceiling -> 1); Floor/Ceiling/Truncate
//      direction on negatives (-2.9m); and Parse'd values with trailing zero
//      scales (1.0050m == 1.005m).
//   4) S4 OPERATORS -- == / != / < / > / <= / >= agreement with the Compare
//      sign (the superposition shapes icall_decimal_compare, MethodMap.Decimal.cs).
//
// Discipline (decimaldeep2/decimalops2 precedents -- KEEP): EVERY decimal
// value is a NAMED decimal local first; Round/Floor/Ceiling/Truncate results
// land in named locals; value output rides string.Format("{0}", x); comparison
// results print as raw int32 (-1/0/1) or bool. No boxed-object Equals/CompareTo
// (LOUD), no float/double conversions (LOUD).
//
// Every line prints "label=True/False" or a raw int; net48 is the byte
// reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class DecimalCmp {
    // ---------- 1. S1: Compare SIGN GRID (raw int32 outputs) ----------
    private static void S1SignGrid() {
        decimal z = 0m;
        decimal p = 1m;
        decimal n = -1m;
        decimal p2 = 1.00m;
        decimal z2 = 0.00m;

        Console.WriteLine("s1_00=" + decimal.Compare(z, z));
        Console.WriteLine("s1_0p=" + decimal.Compare(z, p));
        Console.WriteLine("s1_p0=" + decimal.Compare(p, z));
        Console.WriteLine("s1_0n=" + decimal.Compare(z, n));
        Console.WriteLine("s1_n0=" + decimal.Compare(n, z));
        Console.WriteLine("s1_np=" + decimal.Compare(n, p));
        Console.WriteLine("s1_pn=" + decimal.Compare(p, n));
        Console.WriteLine("s1_nn=" + decimal.Compare(-2m, n));
        Console.WriteLine("s1_nn2=" + decimal.Compare(n, -2m));
        Console.WriteLine("s1_scale0=" + decimal.Compare(z, z2));
        Console.WriteLine("s1_scaleEq=" + decimal.Compare(p, p2));
        Console.WriteLine("s1_self=" + decimal.Compare(n, n));
    }

    // ---------- 2. S2: SCALE + MAGNITUDE lattice ----------
    private static void S2ScaleMagnitude() {
        decimal a = 1.5m;
        decimal b = 1.50m;
        decimal c = 1.500m;
        decimal big = 100m;
        decimal small = 99.99m;
        decimal bigFrac = 123456789.123456789m;
        decimal bigFrac2 = 123456789.123456788m;
        decimal tiny = 0.0000000000000000000000000001m;   // 1e-28, smallest nonzero
        decimal intMax = 2147483647m;
        decimal mx = decimal.MaxValue;
        decimal mn = decimal.MinValue;

        Console.WriteLine("s2_scale1=" + (decimal.Compare(a, b) == 0));
        Console.WriteLine("s2_scale2=" + (decimal.Compare(b, c) == 0));
        Console.WriteLine("s2_scale3=" + (decimal.Compare(a, c) == 0));
        Console.WriteLine("s2_scaleRev=" + (decimal.Compare(b, a) == 0));
        Console.WriteLine("s2_mag=" + (decimal.Compare(big, small) > 0));
        Console.WriteLine("s2_magRev=" + (decimal.Compare(small, big) < 0));
        Console.WriteLine("s2_lastDigit=" + (decimal.Compare(bigFrac, bigFrac2) > 0));
        Console.WriteLine("s2_lastDigitRev=" + (decimal.Compare(bigFrac2, bigFrac) < 0));
        Console.WriteLine("s2_tiny=" + (decimal.Compare(tiny, 0m) > 0));
        Console.WriteLine("s2_tinyRev=" + (decimal.Compare(0m, tiny) < 0));
        Console.WriteLine("s2_intMax=" + (decimal.Compare(intMax, intMax) == 0));
        Console.WriteLine("s2_max=" + (decimal.Compare(mx, mn) == 1));
        Console.WriteLine("s2_min=" + (decimal.Compare(mn, mx) == -1));
        Console.WriteLine("s2_mn0=" + (decimal.Compare(mn, 0m) == -1));
        Console.WriteLine("s2_0mx=" + (decimal.Compare(0m, mx) == -1));
        Console.WriteLine("s2_mnSelf=" + (decimal.Compare(mn, mn) == 0));
        Console.WriteLine("s2_mxSelf=" + (decimal.Compare(mx, mx) == 0));
        Console.WriteLine("s2_maxGtZero=" + (decimal.Compare(mx, 0m) == 1));
        Console.WriteLine("s2_minLtZero=" + (decimal.Compare(mn, 0m) == -1));
        // sign symmetry: Compare(a,b) == -Compare(b,a) across the lattice.
        Console.WriteLine("s2_sym=" + (decimal.Compare(big, small) == -decimal.Compare(small, big)));
        Console.WriteLine("s2_symTiny=" + (decimal.Compare(tiny, 0m) == -decimal.Compare(0m, tiny)));
    }

    // ---------- 3. S3: ROUNDING INTERACTIONS ----------
    private static void S3Rounding() {
        decimal v2675 = 2.675m;
        decimal v2685 = 2.685m;
        decimal rEven1 = decimal.Round(v2675, 2, MidpointRounding.ToEven);      // 2.68
        decimal rAway1 = decimal.Round(v2675, 2, MidpointRounding.AwayFromZero); // 2.68
        decimal rEven2 = decimal.Round(v2685, 2, MidpointRounding.ToEven);      // 2.68 (even 8)
        decimal rAway2 = decimal.Round(v2685, 2, MidpointRounding.AwayFromZero); // 2.69
        Console.WriteLine("s3_rEven1=" + (decimal.Compare(rEven1, 2.68m) == 0));
        Console.WriteLine("s3_rAway1=" + (decimal.Compare(rAway1, 2.68m) == 0));
        Console.WriteLine("s3_rEven2=" + (decimal.Compare(rEven2, 2.68m) == 0));
        Console.WriteLine("s3_rAway2=" + (decimal.Compare(rAway2, 2.69m) == 0));
        // rounding CHANGES the comparison: 2.675 < 2.685, but both ToEven-round
        // to the same 2.68 -> Compare == 0 on the rounded values.
        Console.WriteLine("s3_unrounded=" + (decimal.Compare(v2675, v2685) == -1));
        Console.WriteLine("s3_roundedEq=" + (decimal.Compare(rEven1, rEven2) == 0));
        // AwayFromZero preserves the ordering that ToEven collapses.
        Console.WriteLine("s3_awayKeep=" + (decimal.Compare(rAway1, rAway2) == -1));
        // below-the-midpoint value rounds DOWN (2.674 -> 2.67).
        decimal v2674 = 2.674m;
        decimal rDown = decimal.Round(v2674, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("s3_down=" + (decimal.Compare(rDown, 2.67m) == 0));
        Console.WriteLine("s3_belowMid=" + (decimal.Compare(v2674, 2.68m) == -1));
        // midpoint sign: ToEven vs AwayFromZero on the same value.
        decimal rNegEven = decimal.Round(-2.5m, 0, MidpointRounding.ToEven);
        decimal rNegAway = decimal.Round(-2.5m, 0, MidpointRounding.AwayFromZero);
        decimal rPosEven = decimal.Round(2.5m, 0, MidpointRounding.ToEven);
        decimal rPosAway = decimal.Round(2.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("s3_negEven=" + (decimal.Compare(rNegEven, -2m) == 0));
        Console.WriteLine("s3_negAway=" + (decimal.Compare(rNegAway, -3m) == 0));
        Console.WriteLine("s3_posEven=" + (decimal.Compare(rPosEven, 2m) == 0));
        Console.WriteLine("s3_posAway=" + (decimal.Compare(rPosAway, 3m) == 0));
        Console.WriteLine("s3_negEvenGt=" + (decimal.Compare(rNegEven, rNegAway) == 1));
        // 1m/3m*3m = 0.999...9: Compare < 1, but Round/Floor/Ceiling recover.
        decimal third = 1m / 3m;
        decimal q2 = third * 3m;
        Console.WriteLine("s3_third=" + (decimal.Compare(third, 0.3333333333333333333333333333m) == 0));
        Console.WriteLine("s3_q2=" + (decimal.Compare(q2, 1m) == -1));
        decimal q2r0 = decimal.Round(q2, 0, MidpointRounding.ToEven);
        Console.WriteLine("s3_q2r0=" + (decimal.Compare(q2r0, 1m) == 0));
        decimal q2r28 = decimal.Round(q2, 28, MidpointRounding.ToEven);
        Console.WriteLine("s3_q2r28=" + (decimal.Compare(q2r28, q2) == 0));
        Console.WriteLine("s3_floor=" + (decimal.Compare(decimal.Floor(q2), 0m) == 0));
        Console.WriteLine("s3_ceil=" + (decimal.Compare(decimal.Ceiling(q2), 1m) == 0));
        // Floor/Ceiling/Truncate direction on negatives.
        Console.WriteLine("s3_truncNeg=" + (decimal.Compare(decimal.Truncate(-2.9m), -2m) == 0));
        Console.WriteLine("s3_floorNeg=" + (decimal.Compare(decimal.Floor(-2.9m), -3m) == 0));
        Console.WriteLine("s3_ceilNeg=" + (decimal.Compare(decimal.Ceiling(-2.9m), -2m) == 0));
        Console.WriteLine("s3_floorPos=" + (decimal.Compare(decimal.Floor(2.1m), 2m) == 0));
        Console.WriteLine("s3_ceilPos=" + (decimal.Compare(decimal.Ceiling(2.1m), 3m) == 0));
        // Parse'd values with trailing-zero scales compare scale-insensitively.
        decimal pp = decimal.Parse("1.0050");
        decimal pp2 = decimal.Parse("0.100");
        Console.WriteLine("s3_parse=" + (decimal.Compare(pp, 1.005m) == 0));
        Console.WriteLine("s3_parse2=" + (decimal.Compare(pp2, 0.1m) == 0));
        // instance CompareTo agrees with the static Compare sign.
        Console.WriteLine("s3_ci=" + (v2675.CompareTo(2.68m) == -1));
        Console.WriteLine("s3_ci2=" + (2.68m.CompareTo(v2675) == 1));
        Console.WriteLine("s3_ci3=" + (1m.CompareTo(1.00m) == 0));
    }

    // ---------- 4. S4: OPERATOR agreement with the Compare sign ----------
    private static void S4Operators() {
        decimal p = 1m;
        decimal p2 = 1.00m;
        decimal half = 1.5m;
        decimal two = 2m;
        decimal neg = -1m;
        decimal z = 0m;
        decimal z2 = 0.000m;
        decimal v2675 = 2.675m;
        decimal v268 = 2.68m;
        decimal r1 = decimal.Round(v2675, 2, MidpointRounding.ToEven);

        Console.WriteLine("o_eq=" + (p == p2));
        Console.WriteLine("o_eq2=" + (half == half));
        Console.WriteLine("o_ne=" + (p != p2));
        Console.WriteLine("o_ne2=" + (half != two));
        Console.WriteLine("o_lt=" + (half < two));
        Console.WriteLine("o_gt=" + (two > half));
        Console.WriteLine("o_le=" + (half <= p2));
        Console.WriteLine("o_ge=" + (two >= half));
        Console.WriteLine("o_leScale=" + (half <= 1.50m));
        Console.WriteLine("o_geScale=" + (1.50m >= half));
        Console.WriteLine("o_neg=" + (neg < z));
        Console.WriteLine("o_neg2=" + (z > neg));
        Console.WriteLine("o_zero=" + (z == z2));
        Console.WriteLine("o_zeroNe=" + (z != z2));
        Console.WriteLine("o_mid=" + (v2675 < v268));
        Console.WriteLine("o_midNe=" + (v2675 != v268));
        // operator result == Compare sign agreement.
        Console.WriteLine("o_agree=" + ((v2675 < v268) == (decimal.Compare(v2675, v268) < 0)));
        Console.WriteLine("o_agree2=" + ((half <= p2) == (decimal.Compare(half, p2) <= 0)));
        Console.WriteLine("o_agree3=" + ((neg > z) == (decimal.Compare(neg, z) > 0)));
        Console.WriteLine("o_rounded=" + (r1 == 2.68m));
        Console.WriteLine("o_roundedNe=" + (r1 != 2.675m));
    }

    public static void Main() {
        S1SignGrid();
        S2ScaleMagnitude();
        S3Rounding();
        S4Operators();
    }
}}
