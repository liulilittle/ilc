//------------------------------------------------------------------------------
// doublefmt.cs -- DOUBLE FORMAT (G/E/F) DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C76, AFTER the double format
// domain: formatext.cpp icall_format_double_custom (G/F/N/E/C/P/custom) with
// the 15-significant-digit correctly-rounded CRT base and round-HALF-AWAY-
// FROM-ZERO (fmtround/culturefmt/culturedeep/numfmtdeep all PASS).
//
// THIS case drives the FULL-PRECISION and ROUNDING dimensions of the G / E /
// F families one stage deeper than the earlier format cases sampled:
//
//   1. G DEFAULT (G15, no format string): the full magnitude lattice -- 0,
//      -0.0, 0.1, PI, E, 123.456, 1e20, 1e-5, 2^53 and 2^53+1 (G15 rounding
//      of a 53-bit value), double.MaxValue/MinValue, negatives.
//   2. G EXPLICIT PRECISION (G1..G15): the fixed/scientific switch
//      (scale > sigDigits -> scientific, scale < -3 -> scientific), the
//      sig-digit rounding half away from zero, trailing-zero stripping, and
//      the exponent min-2-digit marker (E+04 vs e+04 lowercase).
//   3. F FIXED-POINT (F0..F15): rounding half away from zero at every
//      precision, carry into the integer part (999.6 F0 -> "1000"), tiny
//      values collapsing to "0.000", the 2.675 near-tie, negatives, and the
//      full F15 digit count.
//   4. E SCIENTIFIC (E0..E15): the 3-digit minimum exponent, rounding carry
//      (99999 E3 -> "1.000E+005"), mantissa rounding at every precision,
//      huge/tiny magnitudes, negatives, zero, lowercase 'e'.
//   5. SPECIALS: NaN renders "NaN" on the F path (ASCII, encoding-safe);
//      +-Infinity (U+221E) rows are EXCLUDED -- the ILC console byte-encodes
//      the symbol differently than the net48 oracle (fmtround precedent).
//      G16/G17 rows are EXCLUDED too (recorded defect: the G path ignores
//      precision > 15 and emits the 15-digit text -- GAPS).
//
// Every expected value is spelled in a trailing comment; the live net48
// oracle is the diff authority (runs the SAME source).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class DoubleFmt {
    private static void P(string label, object v) { Console.WriteLine(label + "=" + v); }

    public static void Main() {
        // ================= 1. G default (G15) =================
        P("g_0", (0.0).ToString());                          // 0
        P("g_neg0", (-0.0).ToString());                      // 0
        P("g_01", (0.1).ToString());                         // 0.1
        P("g_pi", Math.PI.ToString());                       // 3.14159265358979
        P("g_e", Math.E.ToString());                         // 2.71828182845905
        P("g_123", (123.456).ToString());                    // 123.456
        P("g_1e20", (1e20).ToString());                      // 1E+20
        P("g_1e-5", (1e-5).ToString());                      // 1E-05
        P("g_2p53", (9007199254740992.0).ToString());        // 9.00719925474099E+15 (16 sig digits -> G15 sci)
        P("g_2p53p1", (9007199254740993.0).ToString());      // 9.00719925474099E+15 (G15 rounds)
        P("g_max", double.MaxValue.ToString());              // 1.79769313486232E+308
        P("g_min", double.MinValue.ToString());              // -1.79769313486232E+308
        P("g_neg", (-123.456).ToString());                   // -123.456
        P("g_bigfrac", (123456789.123456789).ToString());    // 123456789.123457

        // ================= 2. G explicit precision =================
        P("g1", (12345.6789).ToString("G1"));                // 1E+04
        P("g2", (12345.6789).ToString("G2"));                // 1.2E+04
        P("g3", (12345.6789).ToString("G3"));                // 1.23E+04
        P("g4", (12345.6789).ToString("G4"));                // 1.235E+04
        P("g5", (12345.6789).ToString("G5"));                // 12346
        P("g6", (12345.6789).ToString("G6"));                // 12345.7
        P("g7", (12345.6789).ToString("G7"));                // 12345.68
        P("g9", (12345.6789).ToString("G9"));                // 12345.6789
        P("g15", (12345.6789).ToString("G15"));              // 12345.6789
        P("g1_small", (0.00012345).ToString("G1"));          // 0.0001
        P("g2_small", (0.00012345).ToString("G2"));          // 0.00012
        P("g1_sci", (1e-5).ToString("G1"));                  // 1E-05
        P("g1_half", (0.5).ToString("G1"));                  // 0.5
        P("g1_round", (0.95).ToString("G1"));                // 1
        P("g2_round", (0.995).ToString("G2"));               // 1
        P("g2_lower", (12345.6789).ToString("g2"));          // 1.2e+04
        P("g1_1e6", (999999.0).ToString("G1"));              // 1E+06
        P("g15_pi", Math.PI.ToString("G15"));                // 3.14159265358979
        P("g15_01", (0.1).ToString("G15"));                  // 0.1

        // ================= 3. F fixed-point =================
        P("f0", (123.456).ToString("F0"));                   // 123
        P("f1", (123.456).ToString("F1"));                   // 123.5
        P("f2", (123.456).ToString("F2"));                   // 123.46
        P("f3", (123.456).ToString("F3"));                   // 123.456
        P("f6", (123.456).ToString("F6"));                   // 123.456000
        P("f15", (123.456).ToString("F15"));                 // 123.456000000000000
        P("f0_round", (999.6).ToString("F0"));               // 1000
        P("f2_675", (2.675).ToString("F2"));                 // 2.68
        P("f1_015", (0.15).ToString("F1"));                  // 0.2
        P("f1_005", (0.05).ToString("F1"));                  // 0.1
        P("f2_tiny", (0.0001234).ToString("F2"));            // 0.00
        P("f3_tiny", (0.0001234).ToString("F3"));            // 0.000
        P("f0_half", (2.5).ToString("F0"));                  // 3
        P("f0_half_neg", (-2.5).ToString("F0"));             // -3
        P("f0_half35", (3.5).ToString("F0"));                // 4
        P("f2_neg", (-123.456).ToString("F2"));              // -123.46
        P("f0_neg_round", (-999.6).ToString("F0"));          // -1000
        P("f6_01", (0.1).ToString("F6"));                    // 0.100000
        P("f0_nan", double.NaN.ToString("F0"));              // NaN
        P("f0_big", (1234567890123456.0).ToString("F0"));    // 1234567890123460 (15-digit base)

        // ================= 4. E scientific =================
        P("e0", (12345.678).ToString("E0"));                 // 1E+004
        P("e1", (12345.678).ToString("E1"));                 // 1.2E+004
        P("e2", (12345.678).ToString("E2"));                 // 1.23E+004
        P("e4", (12345.678).ToString("E4"));                 // 1.2346E+004
        P("e6", (12345.678).ToString("E6"));                 // 1.234568E+004
        P("e15", (12345.678).ToString("E15"));               // 1.234567800000000E+004
        P("e2_small", (0.0001234).ToString("E2"));           // 1.23E-004
        P("e6_small", (0.0001234).ToString("E6"));           // 1.234000E-004
        P("e0_zero", (0.0).ToString("E0"));                  // 0E+000
        P("e3_carry", (99999.0).ToString("E3"));             // 1.000E+005
        P("e0_carry", (999999.0).ToString("E0"));            // 1E+006
        P("e2_neg", (-12345.678).ToString("E2"));            // -1.23E+004
        P("e2_1e30", (1e30).ToString("E2"));                 // 1.00E+030
        P("e2_1e-300", (1e-300).ToString("E2"));             // 1.00E-300
        P("e1_lower", (12345.678).ToString("e1"));           // 1.2e+004
        P("e0_max", double.MaxValue.ToString("E0"));         // 2E+308
        P("e0_min", double.MinValue.ToString("E0"));         // -2E+308

        // ================= 5. rounding edges (half away from zero) =================
        P("r_05_f0", (0.5).ToString("F0"));                  // 1
        P("r_15_f0", (1.5).ToString("F0"));                  // 2
        P("r_25_f0", (2.5).ToString("F0"));                  // 3
        P("r_n05_f0", (-0.5).ToString("F0"));                // -1
        P("r_015_f1", (0.15).ToString("F1"));                // 0.2
        P("r_025_f1", (0.25).ToString("F1"));                // 0.3
        P("r_0015_f2", (0.015).ToString("F2"));              // 0.02
        P("r_1005_f2", (1.005).ToString("F2"));              // 1.01 (double 1.005 >= the tie)
        P("r_1675_f2", (1.675).ToString("F2"));              // 1.68
        P("r_9995_f2", (999.995).ToString("F2"));            // 1000.00
        P("r_99995_f1", (999.995).ToString("F1"));           // 1000.0
        P("r_99_f1", (99.99).ToString("F1"));                // 100.0
        P("r_1e20_f2", (1e20).ToString("F2"));               // 100000000000000000000.00
        P("r_0_0005_f3", (0.0005).ToString("F3"));           // 0.001
        P("r_0_00005_f4", (0.00005).ToString("F4"));         // 0.0001
    }
}}
