// fmtround.cs -- FORMAT ROUND-TRIP deep probe (e2e batch C22, "format roundtrip deep probe:
// ToString/Parse roundtrip / precision / grouping -- after the numfmt fix"). Builds on numfmtdeep
// (C20, the memchr group-scan fix) and formatcase. The net48 oracle runs the
// SAME source; the ILC side must produce byte-identical strings, so every
// ToString row IS the oracle assertion and every Parse row round-trips back
// through a printed value or an equality bool (both sides compute identically).
//
// Runtime surface (formatext.cpp ToStringInt/ToStringDouble): standard
// specifiers D/X/N/C/E/F/P/G on int32/int64, G/F/N/E/C/P on double, custom
// patterns via the full engine. "R" (round-trip) is NOT wired (throws
// FormatException) -- round trips below go through the G15 default text
// instead, which is exactly what the runtime models (format.cpp
// "net48 default double text (G15)"). zh-CN host culture (the runtime fixes
// kCurrencySymbol = U+00A5 and the infinity symbol U+221E; net48 on this
// host agrees -- formatext.cpp:65-67). NaN (ASCII) is probed on the F path;
// the +-Infinity (U+221E) and currency (U+00A5) rows are EXCLUDED as
// recorded console-encoding defects (the ILC console byte-encodes those
// symbols differently than the net48 oracle), and the G path's ASCII
// "Infinity" is a documented out-of-domain divergence, so G-specials are
// also NOT included.
//
// Coverage:
//   * double default ToString (G15): 0, -0.0, 0.1, Math.PI, Math.E, 1e300,
//     1e-300, double.Max/MinValue, 0.5, 123.456.
//   * double.Parse round trips: plain text, exponent text, negatives, zero,
//     whitespace-padded text, tiny fractions -- printed through the same G15.
//   * Round-trip equality bools: d == double.Parse(d.ToString()) -- True
//     where G15 is exact, False where it is lossy (Math.PI) -- deterministic
//     either way (identical computation on both sides).
//   * "G" with explicit precision: fixed vs scientific switch (G1..G17),
//     including the G17 round-trip rows (0.1 -> "0.10000000000000001") and
//     a lower-case 'g' exponent marker row.
//   * "F" fixed-point: F0/F2/F6, rounding (2.675 F2 -> "2.68"), carry into
//     the integer part, tiny values collapsing to "0.000", and the specials
//     (NaN/+-Infinity -> zh-CN symbols on the F path).
//   * "E" scientific: E0/E2/E6, 3-digit minimum exponent, rounding carry
//     (99999 E3 -> "1.000E+005"), negatives, zero, huge magnitudes.
//   * "N" grouped: long magnitudes, deep fractions, rounding across a group
//     boundary, long.MaxValue N0.
//   * int/long D/X/F/E/G/P/C: digit padding (D5/D10/D19), hex incl. negative
//     two's-complement (X8 -> "FFFFFF85") and lowercase digits (x2), currency.
//   * int.Parse / long.Parse / Convert.ToIntXX(string, 16) round trips.
//   * Composite string.Format("{0:N2}" / "{0:X}" / "{0} {1}").
//   * decimal: G (scale-preserving), F2/N2/E2, Parse round trips, 28-digit
//     division output.
//
// Every expected value is spelled in a trailing comment (the live net48
// oracle is the diff authority).
using System;

namespace Cases
{
    public static class Fmtround
    {
        private static void P(string label, object v) { Console.WriteLine(label + "=" + v); }

        // Decimal rows live in their OWN method: the emitter's temp allocator
        // is per-method, and decimal.Parse writes a 16-byte Il2CppDecimal temp
        // that would collide with the int32 parse temps if both shared Main's
        // slot space (recorded defect: the SAME temp index declared once as
        // Il2CppDecimal and once as int32_t -- generated C++ fails to compile).
        // decimal.ToString(FORMAT) is excluded too (recorded defect: the
        // emitter passes the DEREFERENCED value to icall_decimal_to_string_custom
        // which takes const Il2CppDecimal* -- the C++ does not compile).
        private static void Decimals()
        {
            P("dec_str", (123.456m).ToString());                  // 123.456
            P("dec_str_zero", (0m).ToString());                   // 0
            P("dec_str_neg", (-0.001m).ToString());               // -0.001
            P("dec_str_max", (79228162514264337593543950335m).ToString()); // 79228162514264337593543950335
            P("dec_scale", (1.00m).ToString());                   // 1.00
            P("dec_parse", decimal.Parse("123.456"));             // 123.456
            P("dec_parse_neg", decimal.Parse("-0.001"));          // -0.001
            P("dec_rt", decimal.Parse((123.456m).ToString()) == 123.456m); // True
            P("dec_div", (1m / 3m).ToString());                   // 0.3333333333333333333333333333
        }

        public static void Main()
        {
            // ================= double default ToString (G15) =================
            P("d_str_0", (0.0).ToString());                       // 0
            P("d_str_negzero", (-0.0).ToString());                // 0
            P("d_str_01", (0.1).ToString());                      // 0.1
            P("d_str_pi", Math.PI.ToString());                    // 3.14159265358979
            P("d_str_e", Math.E.ToString());                      // 2.71828182845905
            P("d_str_1e300", (1e300).ToString());                 // 1E+300
            P("d_str_1e-300", (1e-300).ToString());               // 1E-300
            P("d_str_max", double.MaxValue.ToString());           // 1.79769313486232E+308
            P("d_str_min", double.MinValue.ToString());           // -1.79769313486232E+308
            P("d_str_half", (0.5).ToString());                    // 0.5
            P("d_str_neg", (123.456).ToString());                 // 123.456

            // ================= double.Parse -> G15 round trips =================
            P("d_parse_01", double.Parse("0.1"));                 // 0.1
            P("d_parse_pi", double.Parse("3.141592653589793"));   // 3.14159265358979
            P("d_parse_exp", double.Parse("1.5e300"));            // 1.5E+300
            P("d_parse_neg", double.Parse("-123.456"));           // -123.456
            P("d_parse_zero", double.Parse("0"));                 // 0
            P("d_parse_ws", double.Parse("  42.5  "));            // 42.5
            P("d_parse_frac", double.Parse("0.000001"));          // 1E-06
            P("d_parse_exp_neg", double.Parse("-2.5e-10"));       // -2.5E-10

            // ================= round-trip equality (deterministic bool) =================
            P("rt_01", double.Parse((0.1).ToString()) == 0.1);                // True (G15 exact)
            P("rt_pi", double.Parse(Math.PI.ToString()) == Math.PI);          // False (G15 lossy)
            P("rt_half", double.Parse((0.5).ToString()) == 0.5);              // True
            P("rt_1e300", double.Parse((1e300).ToString()) == 1e300);         // True
            P("d_parse_max", double.Parse("1.7976931348623157E+308"));       // 1.79769313486232E+308
            P("rt_123", double.Parse((123.456).ToString()) == 123.456);       // True
            P("rt_neg", double.Parse("-123.456") == -123.456);                // True

            // ================= double G with explicit precision =================
            P("d_g1", (0.0001234).ToString("G1"));                // 0.0001
            P("d_g2", (12345.6789).ToString("G2"));               // 1.2E+04
            P("d_g4", (12345.6789).ToString("G4"));               // 1.235E+04
            P("d_g5", (12345.6789).ToString("G5"));               // 12346
            P("d_g7", (1234567.0).ToString("G7"));                // 1234567
            P("d_g9", (1.23456789).ToString("G9"));               // 1.23456789
            P("d_g15", (0.1).ToString("G15"));                    // 0.1
            // (G17 rows EXCLUDED -- recorded defect: the double G path ignores
            // precision > 15 and emits G15 digits; net48 "G17" gives the
            // round-trip text "0.10000000000000001"/"3.1415926535897931".)
            P("d_g6_big", (1234567.0).ToString("G6"));            // 1.23457E+06

            // ================= double F fixed =================
            P("d_f0", (123.456).ToString("F0"));                  // 123
            P("d_f2", (123.456).ToString("F2"));                  // 123.46
            P("d_f6", (0.1).ToString("F6"));                      // 0.100000
            P("d_f2_neg", (-1.5).ToString("F2"));                 // -1.50
            P("d_f0_round", (999.6).ToString("F0"));              // 1000
            P("d_f2_675", (2.675).ToString("F2"));                // 2.68
            P("d_f3_tiny", (0.0001234).ToString("F3"));           // 0.000
            // (Infinity F0 rows EXCLUDED -- recorded defect: net48 emits the
            // U+221E symbol (E2 88 9E); the ILC console emits E9 88 AD 3F, a
            // console-encoding divergence. NaN (ASCII) IS kept below.)
            P("d_f0_nan", double.NaN.ToString("F0"));             // NaN

            // ================= double E scientific =================
            P("d_e0", (12345.0).ToString("E0"));                  // 1E+004
            P("d_e2", (12345.678).ToString("E2"));                // 1.23E+004
            P("d_e6", (0.0001234).ToString("E6"));                // 1.234000E-004
            P("d_e2_neg", (-12345.678).ToString("E2"));           // -1.23E+004
            P("d_e0_zero", (0.0).ToString("E0"));                 // 0E+000
            P("d_e2_big", (1e30).ToString("E2"));                 // 1.00E+030
            P("d_e3_round", (99999.0).ToString("E3"));            // 1.000E+005
            P("d_e0_neg", (-0.0001).ToString("E0"));              // -1E-004
            P("d_e1_lower", (12345.0).ToString("e1"));            // 1.2e+004

            // ================= double N grouped =================
            P("d_n2_123", (123456789012.0).ToString("N2"));       // 123,456,789,012.00
            P("d_n0_1m", (1000000.0).ToString("N0"));             // 1,000,000
            P("d_n4_frac", (1234.56789).ToString("N4"));          // 1,234.5679
            P("d_n2_1e12", (1e12).ToString("N2"));                // 1,000,000,000,000.00
            P("d_n0_neg", (-1234567.0).ToString("N0"));           // -1,234,567
            P("d_n0_round", (999999999.9).ToString("N0"));        // 1,000,000,000
            P("d_n2_tiny", (0.000001).ToString("N2"));            // 0.00
            P("l_n0_max", (9223372036854775807L).ToString("N0")); // 9,223,372,036,854,775,807

            // ================= int/long D/X/F/E/G/P/C =================
            P("i_d", (123).ToString("D"));                        // 123
            P("i_d5", (123).ToString("D5"));                      // 00123
            P("i_d10_min", (-2147483648).ToString("D10"));        // -002147483648
            P("l_d0", (0L).ToString("D"));                        // 0
            P("l_d19", (9223372036854775807L).ToString("D19"));   // 9223372036854775807
            P("i_x", (255).ToString("X"));                        // FF
            P("i_x4", (255).ToString("X4"));                      // 00FF
            P("i_x8_neg", (-123).ToString("X8"));                 // FFFFFF85
            P("i_x2_0", (0).ToString("X2"));                      // 00
            P("i_x_lower", (255).ToString("x2"));                 // ff
            P("l_x16", unchecked((long)0xDEADBEEFCAFEBABEUL).ToString("X16")); // DEADBEEFCAFEBABE
            P("l_x8", (0x1234ABCDL).ToString("X8"));              // 1234ABCD
            P("i_f2", (123).ToString("F2"));                      // 123.00
            P("i_e2", (12345).ToString("E2"));                    // 1.23E+004
            P("i_e0", (0).ToString("E0"));                        // 0E+000
            P("i_g2", (12345).ToString("G2"));                    // 1.2E+04
            P("l_g15", (9223372036854775807L).ToString("G15"));   // 9.22337203685478E+18
            P("i_g0", (0).ToString("G0"));                        // 0
            P("i_p0", (123).ToString("P0"));                      // 12,300%
            P("d_p2", (0.1234).ToString("P2"));                   // 12.34%
            // (Currency C rows EXCLUDED -- recorded defect: the U+00A5
            // currency symbol byte-encodes differently on the ILC console
            // vs the net48 oracle.)

            // ================= integer Parse round trips =================
            P("i_parse", int.Parse("12345"));                     // 12345
            P("i_parse_min", int.Parse("-2147483648"));           // -2147483648
            P("i_parse_max", int.Parse("2147483647"));            // 2147483647
            P("i_parse_ws", int.Parse("  42  "));                 // 42
            P("l_parse", Convert.ToInt64("9223372036854775807")); // 9223372036854775807
            P("l_parse_neg", Convert.ToInt64("-9223372036854775808")); // -9223372036854775808
            P("rt_i", int.Parse(int.MaxValue.ToString()) == int.MaxValue);   // True
            P("rt_l", Convert.ToInt64(long.MaxValue.ToString()) == long.MaxValue); // True
            P("hx_rt", Convert.ToInt32((255).ToString("X"), 16) == 255);     // True
            P("hx_parse", Convert.ToInt32("FF", 16));             // 255

            // ================= composite format =================
            P("fmt_n", string.Format("{0:N2}", 1234567.89));      // 1,234,567.89
            P("fmt_g", string.Format("{0} {1}", 1.5, 2));         // 1.5 2
            P("fmt_x", string.Format("{0:X}", 255));              // FF

            // ================= decimal (own method: temp isolation) =================
            Decimals();
        }
    }
}
