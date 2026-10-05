//------------------------------------------------------------------------------
// decfmt2.cs -- DECIMAL FORMAT deep probe 2 (e2e batch C28): the E/P/custom
// specifier surface AFTER the decimal-format engine gained the custom-format
// and percent arms. Builds on decfmt (C26) which probed G/F/N/E/C/lowercase/
// alignment/composite/exception. This case goes DEEPER on the three specifier
// families that decfmt recorded as DEFECTS -- the E exponent form, the P
// percent form, and the CUSTOM (literal-pattern) form -- through the SAME
// wired composite path (boxed decimal -> icall_format_composite_item ->
// icall_decimal_to_string_custom, decimal_format.cpp).
//
// The E/P/custom defects recorded in decfmt S6/S9 were the surface gap:
//   * FormatE always emits an uppercase 'E' (net48 keeps the SPECIFIER CASE
//     for {0:e2} -> "1.23e+003").
//   * FormatE does not zero-pad a short mantissa fraction (2.5m {1:E2} ->
//     "2.50E+000" vs the engine's "2.5E+000").
//   * P (percent) is a valid net48 decimal specifier ("123,456.78%") -- the
//     engine had no P arm and threw FormatException.
//   * A non-letter pattern ({0:xyz}) falls back to CUSTOM formatting that
//     passes literals through (net48 "xyz") -- the engine's strict parser
//     threw FormatException.
// This case re-probes those three families at higher depth to VERIFY the
// fixes; expected values are written from the net48 oracle run (zh-CN N/F/E
// layouts use "." + "," identical to invariant; the C symbol is U+00A5).
//
// Every decimal value is a NAMED decimal local first (decops NOTE: the icall
// RESULT slot / temp-bucket discipline -- named locals keep the case
// compiler-stable). All specifiers go through string.Format so the boxed
// decimal flows through the composite icall (the only wired decimal-format
// channel, decfmt S0).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Decfmt2
    {
        public static void Main()
        {
            // ---- the value set (named decimals, decops discipline) ----
            decimal v1 = 1234.5678m;                          // scale 4
            decimal v2 = -1234.5678m;                         // negative
            decimal v3 = 0m;                                  // zero
            decimal v4 = 0.0000000000000000000000000001m;     // scale-28 unit
            decimal v5 = 79228162514264337593543950335m;      // decimal.MaxValue
            decimal v6 = 1.5m;                                // rounding tie (away)
            decimal v7 = 9.999m;                              // front carry
            decimal v8 = 0.0001m;                             // small positive
            decimal v9 = -0.0001m;                            // small negative

            // ============ S1: E family deep (exponent form, specifier case) ============
            // net48 keeps the SPECIFIER CASE: {0:e2} -> "1.23e+003".
            Console.WriteLine(string.Format("{0:E0}", v1));  // 1E+003
            Console.WriteLine(string.Format("{0:E2}", v1));  // 1.23E+003
            Console.WriteLine(string.Format("{0:E4}", v1));  // 1.2346E+003
            Console.WriteLine(string.Format("{0:E6}", v1));  // 1.234568E+003
            Console.WriteLine(string.Format("{0:e2}", v1));  // 1.23e+003 (net48 keeps lowercase specifier)
            Console.WriteLine(string.Format("{0:e0}", v1));  // 1e+003
            Console.WriteLine(string.Format("{0:E2}", v2));  // -1.23E+003
            Console.WriteLine(string.Format("{0:E2}", v3));  // 0.00E+000
            Console.WriteLine(string.Format("{0:E2}", v4));  // 1.00E-028
            Console.WriteLine(string.Format("{0:E2}", v5));  // 7.92E+028
            Console.WriteLine(string.Format("{0:E0}", v5));  // 8E+028
            Console.WriteLine(string.Format("{0:E2}", v6));  // 1.50E+000
            Console.WriteLine(string.Format("{0:E1}", v6));  // 1.5E+000
            Console.WriteLine(string.Format("{0:E0}", v7));  // 1E+001
            Console.WriteLine(string.Format("{0:E2}", v7));  // 1.00E+001 (9.999 rounds up: 9.99|9 -> carry)
            Console.WriteLine(string.Format("{0:E2}", v8));  // 1.00E-004
            Console.WriteLine(string.Format("{0:E2}", v9));  // -1.00E-004

            // ============ S2: P family deep (percent, no space before %) ============
            // net48 zh-CN percent layout: grouped value x 100 + "%" (no space).
            Console.WriteLine(string.Format("{0:P}", v1));  // 123,456.78%
            Console.WriteLine(string.Format("{0:P0}", v1)); // 123,457%
            Console.WriteLine(string.Format("{0:P2}", v1)); // 123,456.78%
            Console.WriteLine(string.Format("{0:P2}", v2)); // -123,456.78%
            Console.WriteLine(string.Format("{0:P2}", v3)); // 0.00%
            Console.WriteLine(string.Format("{0:P0}", v3)); // 0%
            Console.WriteLine(string.Format("{0:P4}", v4)); // 0.0000% (scale-28 unit x100 still zero)
            Console.WriteLine(string.Format("{0:P2}", v6)); // 150.00%
            Console.WriteLine(string.Format("{0:P0}", v7)); // 1,000%
            Console.WriteLine(string.Format("{0:P1}", v8)); // 0.0%

            // ============ S3: CUSTOM format deep (literal-pattern fallback) ============
            // A custom pattern (non-letter specifier) is a net48 custom format:
            // literal characters pass through, '0' is a digit place (fill 0),
            // '#' is a digit place (no fill), '.' the decimal point, ',' the
            // group separator, '%' scales x100.
            Console.WriteLine(string.Format("{0:xyz}", v1));      // xyz (literal passthrough)
            Console.WriteLine(string.Format("{0:00.00}", v1));    // 1234.57
            Console.WriteLine(string.Format("{0:000000}", v3));   // 000000
            Console.WriteLine(string.Format("{0:#,##0.00}", v1)); // 1,234.57
            Console.WriteLine(string.Format("{0:0.###}", v8));    // 0 (max 3 fraction digits, trailing # trimmed)
            Console.WriteLine(string.Format("{0:0.0}", v6));      // 1.5
            Console.WriteLine(string.Format("{0:#.00}", v3));     // .00
            Console.WriteLine(string.Format("{0:000.00#}", v2));  // -1234.568
            Console.WriteLine(string.Format("{0:0.00%}", v6));    // 150.00%
            Console.WriteLine(string.Format("{0:'Q'0}", v1));     // Q1235 (quoted literal)
            Console.WriteLine(string.Format("{0:ab12}", v5));     // ab12

            // ============ S4: default G still byte-exact (control) ============
            Console.WriteLine(string.Format("{0}", v1));  // 1234.5678
            Console.WriteLine(string.Format("{0}", v8));  // 0.0001
            Console.WriteLine(string.Format("{0:G}", v5)); // 79228162514264337593543950335
        }
    }
}
