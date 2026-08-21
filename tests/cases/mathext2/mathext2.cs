//------------------------------------------------------------------------------
// mathext2.cs -- System.Math extension DEEP 2 e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C60, AFTER the mathext domain
// (trig/log/exp/hyperbolic + IEEERemainder basics) is fully wired and PASSing.
//
// THIS case drives the ABS / IEEE-REMAINDER / BIGMUL boundaries:
//
//   1) Abs EDGE CASES (icall_math_abs_i/l/d): the int32/int64 MINIMUM value
//      throws OverflowException (net48 contract) while every other value
//      succeeds; the double overload never throws -- probe the signed-zero
//      sign-clearing (Abs(-0.0) -> +0.0, bit-verified), NaN passthrough,
//      +/-Infinity collapse to +Infinity, and the double.MinValue ->
//      double.MaxValue magnitude flip.
//   2) IEEERemainder DEEP (icall_math_ieee_remainder_d = std::remainder, the
//      IEEE-754 exact remainder): ties-to-EVEN quotients on 2.5/1.5-style
//      half-integral ratios, x < y (result == x), exact multiples (result
//      0), negative divisors / dividends (result sign follows the DIVIDEND,
//      bit-verified for the -0.0 case), and large-magnitude inputs (1e15)
//      where the result is still exactly representable.
//   3) BigMul VALUE CONTRACT (recorded GAP, NOT wired -- see GAPS.md D-ME2-1):
//      Math.BigMul(int,int) aborts the transpile with a LOUD
//      NotSupportedException (MethodMap.MathExtra.cs), so the API itself is
//      never invoked here. The CONTRACT it must satisfy -- result == the
//      exact int64 product (long)a * b -- is pinned by the equivalent inline
//      long multiplication, which both the net48 oracle and the transpiled
//      runtime compute identically.
//   4) DivRem int64/int32 edge cases (net48 PUBLIC, wired): MinValue/-1 ->
//      OverflowException, 0 divisor -> DivideByZeroException, negative
//      operand sign rules (remainder follows the dividend).
//
// Every printed line is "label=True/False" from a BOOL comparison or a
// caught-exception bool -- no raw double/format output, so the reference run
// and the transpiled AOT build agree byte-for-byte.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class MathExt2Case {
    // NaN probe helper: `v != v` is true ONLY for NaN (the double.IsNaN icall
    // is not wired in the M-stage transpiler; this is the mathdeep pattern).
    private static bool IsNaN(double v) { return v != v; }
    public static void Main() {
        // ============ 1. Abs boundaries: int32 ============
        Console.WriteLine("abs_i_max=" + (Math.Abs(int.MaxValue) == int.MaxValue));
        Console.WriteLine("abs_i_min1=" + (Math.Abs(int.MinValue + 1) == int.MaxValue));
        Console.WriteLine("abs_i_zero=" + (Math.Abs(0) == 0));
        Console.WriteLine("abs_i_neg1=" + (Math.Abs(-1) == 1));
        Console.WriteLine("abs_i_pos1=" + (Math.Abs(1) == 1));
        bool ovfI = false;
        try { Math.Abs(int.MinValue); } catch (OverflowException) { ovfI = true; }
        Console.WriteLine("abs_i_min_ovf=" + ovfI);

        // ============ 2. Abs boundaries: int64 ============
        Console.WriteLine("abs_l_max=" + (Math.Abs(long.MaxValue) == long.MaxValue));
        Console.WriteLine("abs_l_min1=" + (Math.Abs(long.MinValue + 1) == long.MaxValue));
        Console.WriteLine("abs_l_zero=" + (Math.Abs(0L) == 0L));
        Console.WriteLine("abs_l_neg42=" + (Math.Abs(-42L) == 42L));
        bool ovfL = false;
        try { Math.Abs(long.MinValue); } catch (OverflowException) { ovfL = true; }
        Console.WriteLine("abs_l_min_ovf=" + ovfL);

        // ============ 3. Abs boundaries: double ============
        // Abs(-0.0) clears the sign bit (IEEE-754): bits -> +0.0 (0x0).
        Console.WriteLine("abs_d_negzero_signcleared=" +
            (BitConverter.DoubleToInt64Bits(Math.Abs(-0.0)) == 0x0L));
        Console.WriteLine("abs_d_poszero=" +
            (BitConverter.DoubleToInt64Bits(Math.Abs(0.0)) == 0x0L));
        Console.WriteLine("abs_d_nan=" + IsNaN(Math.Abs(double.NaN)));
        Console.WriteLine("abs_d_pinf=" + (Math.Abs(double.PositiveInfinity) == double.PositiveInfinity));
        Console.WriteLine("abs_d_ninf_collapses=" + (Math.Abs(double.NegativeInfinity) == double.PositiveInfinity));
        // double.MinValue == -double.MaxValue; Abs flips to the positive max.
        Console.WriteLine("abs_d_min_flips=" +
            (BitConverter.DoubleToInt64Bits(Math.Abs(double.MinValue)) == BitConverter.DoubleToInt64Bits(double.MaxValue)));
        Console.WriteLine("abs_d_negval=" + (Math.Abs(-2.5) == 2.5));
        Console.WriteLine("abs_d_small=" + (Math.Abs(-1e-300) == 1e-300));

        // ============ 4. IEEERemainder: tie-to-EVEN quotient ============
        // x/y half-integral -> the NEAREST EVEN integral quotient n.
        // 2.5/1.0 == 2.5 -> n == 2 -> 2.5 - 2*1 = 0.5.
        Console.WriteLine("rem_25_1=" + (Math.IEEERemainder(2.5, 1.0) == 0.5));
        // 1.5/1.0 == 1.5 -> n == 2 -> 1.5 - 2*1 = -0.5 (result sign == dividend).
        Console.WriteLine("rem_15_1=" + (Math.IEEERemainder(1.5, 1.0) == -0.5));
        // 0.5/1.0 == 0.5 -> tie between 0 and 1, even is 0 -> 0.5 - 0 = 0.5.
        Console.WriteLine("rem_05_1=" + (Math.IEEERemainder(0.5, 1.0) == 0.5));
        // 3.5/1.0 == 3.5 -> n == 4 -> 3.5 - 4 = -0.5.
        Console.WriteLine("rem_35_1=" + (Math.IEEERemainder(3.5, 1.0) == -0.5));
        // 6/4 == 1.5 -> tie 1|2, even 2 -> 6 - 2*4 = -2.
        Console.WriteLine("rem_6_4=" + (Math.IEEERemainder(6.0, 4.0) == -2.0));
        // 14/4 == 3.5 -> n == 4 -> 14 - 16 = -2.
        Console.WriteLine("rem_14_4=" + (Math.IEEERemainder(14.0, 4.0) == -2.0));
        // 18/4 == 4.5 -> tie 4|5, even 4 -> 18 - 16 = 2.
        Console.WriteLine("rem_18_4=" + (Math.IEEERemainder(18.0, 4.0) == 2.0));
        // 15/10 == 1.5 -> n == 2 -> 15 - 20 = -5.
        Console.WriteLine("rem_15_10=" + (Math.IEEERemainder(15.0, 10.0) == -5.0));
        // 25/10 == 2.5 -> tie 2|3, even 2 -> 25 - 20 = 5.
        Console.WriteLine("rem_25_10=" + (Math.IEEERemainder(25.0, 10.0) == 5.0));
        // 35/10 == 3.5 -> n == 4 -> 35 - 40 = -5.
        Console.WriteLine("rem_35_10=" + (Math.IEEERemainder(35.0, 10.0) == -5.0));

        // ============ 5. IEEERemainder: x < y, exact multiples ============
        // |x| < |y|/2 -> quotient 0 -> result == x.
        Console.WriteLine("rem_5_10=" + (Math.IEEERemainder(5.0, 10.0) == 5.0));
        // x == y/2 EXACTLY -> tie to even 0 (quotient 0.5) -> result == x.
        Console.WriteLine("rem_5_10_exacthalf=" + (Math.IEEERemainder(5.0, 10.0) == 5.0));
        // exact multiple: 10/5 == 2 -> remainder 0.
        Console.WriteLine("rem_10_5_zero=" + (Math.IEEERemainder(10.0, 5.0) == 0.0));
        // exact multiple: 20/4 == 5 -> remainder 0.
        Console.WriteLine("rem_20_4_zero=" + (Math.IEEERemainder(20.0, 4.0) == 0.0));
        // 1.0/1.0 exact -> 0.
        Console.WriteLine("rem_1_1_zero=" + (Math.IEEERemainder(1.0, 1.0) == 0.0));
        // negative divisor, positive dividend: 10/-4 == -2.5 -> n == -2
        // (even) -> 10 - (-2 * -4) = 2 (sign follows DIVIDEND).
        Console.WriteLine("rem_10_neg4=" + (Math.IEEERemainder(10.0, -4.0) == 2.0));
        // negative dividend: -10/4 == -2.5 -> n == -2 -> -10 + 8 = -2.
        Console.WriteLine("rem_neg10_4=" + (Math.IEEERemainder(-10.0, 4.0) == -2.0));
        // both negative: -10/-4 == 2.5 -> tie 2|3, even 2 -> -10 - (-8) = -2.
        Console.WriteLine("rem_neg10_neg4=" + (Math.IEEERemainder(-10.0, -4.0) == -2.0));

        // ============ 6. IEEERemainder: signed zero + specials ============
        // 0 / y -> +0.0 (bits 0x0).
        Console.WriteLine("rem_zero=" +
            (BitConverter.DoubleToInt64Bits(Math.IEEERemainder(0.0, 3.0)) == 0x0L));
        // -0.0 / y -> -0.0 (sign of dividend preserved: bits 0x8000...0).
        Console.WriteLine("rem_negzero_sign=" +
            (BitConverter.DoubleToInt64Bits(Math.IEEERemainder(-0.0, 3.0)) == unchecked((long)0x8000000000000000UL)));
        // 0 / y where y is a NEGATIVE divisor also yields +0.0 (sign of x).
        Console.WriteLine("rem_zero_negdiv=" +
            (BitConverter.DoubleToInt64Bits(Math.IEEERemainder(0.0, -3.0)) == 0x0L));
        // x / 0 -> NaN; x / Inf -> x (quotient 0); Inf / y -> NaN; NaN -> NaN.
        Console.WriteLine("rem_divzero_nan=" + IsNaN(Math.IEEERemainder(3.0, 0.0)));
        Console.WriteLine("rem_x_inf=" + (Math.IEEERemainder(3.0, double.PositiveInfinity) == 3.0));
        Console.WriteLine("rem_x_neginf=" + (Math.IEEERemainder(3.0, double.NegativeInfinity) == 3.0));
        Console.WriteLine("rem_inf_nan=" + IsNaN(Math.IEEERemainder(double.PositiveInfinity, 3.0)));
        Console.WriteLine("rem_neginf_nan=" + IsNaN(Math.IEEERemainder(double.NegativeInfinity, 3.0)));
        Console.WriteLine("rem_nan=" + IsNaN(Math.IEEERemainder(double.NaN, 3.0)));
        Console.WriteLine("rem_ynan=" + IsNaN(Math.IEEERemainder(3.0, double.NaN)));

        // ============ 7. IEEERemainder: large magnitudes ============
        // 1e15/7: nearest integral quotient 142857142857143 (its *7 ==
        // 1000000000000001) -> remainder 1e15 - 1000000000000001 == -1.
        Console.WriteLine("rem_1e15_7=" + (Math.IEEERemainder(1e15, 7.0) == -1.0));
        // 1e15/3: quotient 333333333333333 -> 1e15 - 999999999999999 == 1.
        Console.WriteLine("rem_1e15_3=" + (Math.IEEERemainder(1e15, 3.0) == 1.0));
        // 1e15/2 == 5e14 exact -> 0.
        Console.WriteLine("rem_1e15_2_zero=" + (Math.IEEERemainder(1e15, 2.0) == 0.0));
        // x == y -> 0.
        Console.WriteLine("rem_1e15_1e15_zero=" + (Math.IEEERemainder(1e15, 1e15) == 0.0));
        // 1e15 / (5e14) == 2 exact -> 0.
        Console.WriteLine("rem_1e15_5e14_zero=" + (Math.IEEERemainder(1e15, 5e14) == 0.0));

        // ============ 8. BigMul DIRECT (D-ME2-1 now wired) ============
        // Math.BigMul(a,b) == (long)a * b (exact int64 product). Since batch
        // D-ME2-1 the API itself is wired (icall_math_bigmul) -- these call
        // Math.BigMul directly AND pin the value contract.
        Console.WriteLine("bigmul_2_3=" + (Math.BigMul(2, 3) == 6L));
        Console.WriteLine("bigmul_max_max=" + (Math.BigMul(int.MaxValue, int.MaxValue) == 4611686014132420609L));
        Console.WriteLine("bigmul_min_min=" + (Math.BigMul(int.MinValue, int.MinValue) == 4611686018427387904L));
        Console.WriteLine("bigmul_max_min=" + (Math.BigMul(int.MaxValue, int.MinValue) == -4611686016279904256L));
        Console.WriteLine("bigmul_zero=" + (Math.BigMul(0, int.MaxValue) == 0L));
        Console.WriteLine("bigmul_neg=" + (Math.BigMul(-123456789, 987654321) == -121932631112635269L));
        Console.WriteLine("bigmul_46340=" + (Math.BigMul(46340, 46341) == 2147441940L));
        Console.WriteLine("bigmul_46341=" + (Math.BigMul(46341, 46341) == 2147488281L));
        Console.WriteLine("bigmul_1e5=" + (Math.BigMul(100000, 100000) == 10000000000L));
        Console.WriteLine("bigmul_max_neg1=" + (Math.BigMul(int.MaxValue, -1) == -2147483647L));
        Console.WriteLine("bigmul_min_1=" + (Math.BigMul(int.MinValue, 1) == -2147483648L));
        Console.WriteLine("bigmul_neg1_neg1=" + (Math.BigMul(-1, -1) == 1L));

        // ============ 8b. double.IsNaN / IsPositiveInfinity / IsNegativeInfinity
        // / IsInfinity DIRECT (D-ME2-2 now wired) ============
        // net48 predicate probes: IsNaN true ONLY for NaN (1.0/0.0/-0.0/+-inf
        // all false); IsPositiveInfinity true ONLY for +inf; IsNegativeInfinity
        // true ONLY for -inf; IsInfinity true for BOTH infinities, false for
        // NaN/finite.
        Console.WriteLine("d_isnan_nan=" + double.IsNaN(double.NaN));
        Console.WriteLine("d_isnan_1=" + double.IsNaN(1.0));
        Console.WriteLine("d_isnan_0=" + double.IsNaN(0.0));
        Console.WriteLine("d_isnan_neg0=" + double.IsNaN(-0.0));
        Console.WriteLine("d_isnan_pinf=" + double.IsNaN(double.PositiveInfinity));
        Console.WriteLine("d_isnan_ninf=" + double.IsNaN(double.NegativeInfinity));
        Console.WriteLine("d_ispinf_pinf=" + double.IsPositiveInfinity(double.PositiveInfinity));
        Console.WriteLine("d_ispinf_ninf=" + double.IsPositiveInfinity(double.NegativeInfinity));
        Console.WriteLine("d_ispinf_nan=" + double.IsPositiveInfinity(double.NaN));
        Console.WriteLine("d_ispinf_1e300=" + double.IsPositiveInfinity(1e300));
        Console.WriteLine("d_ispinf_neg1e300=" + double.IsPositiveInfinity(-1e300));
        Console.WriteLine("d_ispinf_0=" + double.IsPositiveInfinity(0.0));
        Console.WriteLine("d_isninf_ninf=" + double.IsNegativeInfinity(double.NegativeInfinity));
        Console.WriteLine("d_isninf_pinf=" + double.IsNegativeInfinity(double.PositiveInfinity));
        Console.WriteLine("d_isninf_nan=" + double.IsNegativeInfinity(double.NaN));
        Console.WriteLine("d_isninf_neg1e300=" + double.IsNegativeInfinity(-1e300));
        Console.WriteLine("d_isninf_1e300=" + double.IsNegativeInfinity(1e300));
        Console.WriteLine("d_isinf_pinf=" + double.IsInfinity(double.PositiveInfinity));
        Console.WriteLine("d_isinf_ninf=" + double.IsInfinity(double.NegativeInfinity));
        Console.WriteLine("d_isinf_nan=" + double.IsInfinity(double.NaN));
        Console.WriteLine("d_isinf_0=" + double.IsInfinity(0.0));
        Console.WriteLine("d_isinf_neg0=" + double.IsInfinity(-0.0));
        Console.WriteLine("d_isinf_42=" + double.IsInfinity(42.0));

        // ============ 9. DivRem (net48 PUBLIC, wired): edge cases ============
        int remQ; int remR;
        remQ = Math.DivRem(7, 2, out remR);
        Console.WriteLine("divrem_7_2=" + (remQ == 3 && remR == 1));
        remQ = Math.DivRem(7, -2, out remR);
        Console.WriteLine("divrem_7_neg2=" + (remQ == -3 && remR == 1));
        remQ = Math.DivRem(-7, 2, out remR);
        Console.WriteLine("divrem_neg7_2=" + (remQ == -3 && remR == -1));
        remQ = Math.DivRem(0, 5, out remR);
        Console.WriteLine("divrem_0_5=" + (remQ == 0 && remR == 0));
        bool dvzI = false;
        try { Math.DivRem(5, 0, out remR); } catch (DivideByZeroException) { dvzI = true; }
        Console.WriteLine("divrem_divzero=" + dvzI);
        bool ovfDivI = false;
        try { Math.DivRem(int.MinValue, -1, out remR); } catch (OverflowException) { ovfDivI = true; }
        Console.WriteLine("divrem_min_neg1_ovf=" + ovfDivI);
        long remLq; long remLr;
        remLq = Math.DivRem(long.MaxValue, 2, out remLr);
        Console.WriteLine("divrem_lmax_2=" + (remLq == 4611686018427387903L && remLr == 1L));
        remLq = Math.DivRem(-9L, 4L, out remLr);
        Console.WriteLine("divrem_neg9_4=" + (remLq == -2L && remLr == -1L));
        bool ovfDivL = false;
        try { Math.DivRem(long.MinValue, -1L, out remLr); } catch (OverflowException) { ovfDivL = true; }
        Console.WriteLine("divrem_lmin_neg1_ovf=" + ovfDivL);
    }
}}
