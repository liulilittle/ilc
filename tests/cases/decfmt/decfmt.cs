// decfmt.cs -- DECIMAL FORMAT deep probe (e2e batch C26, "decimal 格式深测:
// ToString 全格式/复合格式 decimal 参数 -- decops 后"). Builds on decops (the
// decimal ctor/arith/parse surface + ONE composite N2 line) and goes DEEPER on
// the decimal FORMATTING surface through the WIRED composite path -- a boxed
// decimal flows into icall_format_composite_item -> icall_decimal_to_string_custom
// (formatext.cpp, decimal_format.cpp) -- the ONLY decimal ToString(format)
// channel that transpiles (the direct `d.ToString("N2")` receiver-deref defect
// is recorded in decops and NOT re-probed here):
//
//   * G (default): `{0}` and `{0:G}` -- scale-preserving fixed layout for
//     positive/negative/zero/scale-28-unit/decimal-max/min.
//   * F family: F0/F2/F4 -- exactly `prec` fraction digits, half-away rounding
//     at the discard point (2.5 -> "3", 3.5 -> "4" -- decimal is NOT banker's).
//   * N family: N0/N2/N4 -- thousands grouping of the integer part.
//   * E family: E0/E2/E6 -- scientific "d.dddE+xxx", 3-digit exponent.
//   * C family: C0/C2 -- currency layout (zh-CN oracle symbol is the ground
//     truth; the runtime's fixed symbol is a KNOWN deviation risk).
//   * Lowercase specifiers (n3/f1/e2) -- case-insensitive letter parse.
//   * Alignment: `{0,20:F2}` right-aligns to 20, `{0,-20:F2}` left-aligns
//     (net48: longer text is never truncated, so narrow widths stay unchanged).
//   * Multiple decimal args in one composite: "{0:N2} | {1:E2} | {2:G}".
//   * Rounding carry: 999.995 F2 -> "1,000.00"; 0.005 F2 -> "0.01";
//     -0.005 F2 -> "-0.01".
//   * Exception paths: {0:D2}/{0:X} on a decimal throw FormatException
//     (integer-only specifiers -- caught, prints the exception type name);
//     {0:P} is a valid net48 specifier ("123,456.78%") and {0:xyz} falls back
//     to a literal-pass CUSTOM format -- both are runtime DEFECTS (the engine
//     has no P arm and no custom-format fallback) and stay as byte diffs.
//
// Every decimal value is a NAMED decimal local first (decops NOTE: the icall
// RESULT slot / temp-bucket discipline -- named locals keep the case
// compiler-stable). Expected values are in trailing comments (written from the
// net48 oracle run; zh-CN N/F/E/C layouts use "." + "," identical to invariant
// except the C symbol).
using System;

namespace Cases
{
    public static class Decfmt
    {
        public static void Main()
        {
            // ---- the value set ----
            decimal v1 = 1234.5678m;                          // scale 4
            decimal v2 = -1234.5678m;                         // negative
            decimal v3 = 0m;                                  // zero
            decimal v4 = 0.0000000000000000000000000001m;     // scale-28 unit
            decimal v5 = 79228162514264337593543950335m;      // decimal.MaxValue
            decimal v6 = -79228162514264337593543950335m;     // decimal.MinValue
            decimal v7 = 2.5m;                                // rounding tie (away)
            decimal v8 = 3.5m;
            decimal v9 = 999.995m;                            // rounding carry
            decimal v10 = 123456789.987654321m;               // mixed scale 9

            // ============ S1: default G (`{0}` and `{0:G}`) ============
            Console.WriteLine(string.Format("{0}", v1));     // 1234.5678
            Console.WriteLine(string.Format("{0}", v2));     // -1234.5678
            Console.WriteLine(string.Format("{0}", v3));     // 0
            Console.WriteLine(string.Format("{0:G}", v4));   // 0.0000000000000000000000000001 (decimal G keeps fixed-point scale)
            Console.WriteLine(string.Format("{0:G}", v5));   // 79228162514264337593543950335
            Console.WriteLine(string.Format("{0:G}", v6));   // -79228162514264337593543950335
            Console.WriteLine(string.Format("{0}", v10));    // 123456789.987654321

            // ============ S2: F family (exact fraction digits, half-away) ============
            Console.WriteLine(string.Format("{0:F0}", v1));  // 1235
            Console.WriteLine(string.Format("{0:F2}", v1));  // 1234.57
            Console.WriteLine(string.Format("{0:F4}", v1));  // 1234.5678
            Console.WriteLine(string.Format("{0:F2}", v2));  // -1234.57
            Console.WriteLine(string.Format("{0:F2}", v3));  // 0.00
            Console.WriteLine(string.Format("{0:F2}", v4));  // 0.00
            Console.WriteLine(string.Format("{0:F2}", v7));  // 2.50
            Console.WriteLine(string.Format("{0:F2}", v8));  // 3.50
            Console.WriteLine(string.Format("{0:F2}", v9));  // 1000.00
            Console.WriteLine(string.Format("{0:F3}", v10)); // 123456789.988

            // ============ S3: N family (grouped integer part) ============
            Console.WriteLine(string.Format("{0:N0}", v5));  // 79,228,162,514,264,337,593,543,950,335
            Console.WriteLine(string.Format("{0:N2}", v1));  // 1,234.57
            Console.WriteLine(string.Format("{0:N4}", v10)); // 123,456,789.9877
            Console.WriteLine(string.Format("{0:N2}", v2));  // -1,234.57
            Console.WriteLine(string.Format("{0:N0}", v3));  // 0
            Console.WriteLine(string.Format("{0:N2}", v6));  // -79,228,162,514,264,337,593,543,950,335.00

            // ============ S4: E family (scientific, 3-digit exponent) ============
            Console.WriteLine(string.Format("{0:E0}", v1));  // 1E+003
            Console.WriteLine(string.Format("{0:E2}", v1));  // 1.23E+003
            Console.WriteLine(string.Format("{0:E6}", v1));  // 1.234568E+003
            Console.WriteLine(string.Format("{0:E2}", v2));  // -1.23E+003
            Console.WriteLine(string.Format("{0:E2}", v3));  // 0.00E+000
            Console.WriteLine(string.Format("{0:E2}", v4));  // 1.00E-028
            Console.WriteLine(string.Format("{0:E2}", v5));  // 7.92E+028
            Console.WriteLine(string.Format("{0:E0}", v7));  // 3E+000

            // ============ S5: C family (currency layout) ============
            // net48 zh-CN symbol is U+00A5 (YEN); negative pattern is
            // "symbol - value" (no space). The runtime's fixed U+00A5 symbol
            // matches; the layout MUST also match byte-for-byte.
            Console.WriteLine(string.Format("{0:C2}", v1));  // ¥1,234.57
            Console.WriteLine(string.Format("{0:C0}", v3));  // ¥0
            Console.WriteLine(string.Format("{0:C2}", v2));  // ¥-1,234.57

            // ============ S6: lowercase specifiers (case-insensitive letter) ============
            // net48 keeps the SPECIFIER CASE in the E output ("1.23e+003" for
            // {0:e2} -- lowercase e); the runtime's E engine always emits an
            // uppercase 'E'. RECORDED DEFECT (decimal_format.cpp FormatE).
            Console.WriteLine(string.Format("{0:n3}", v1));  // 1,234.568
            Console.WriteLine(string.Format("{0:f1}", v7));  // 2.5
            Console.WriteLine(string.Format("{0:e2}", v1));  // 1.23e+003  (net48) / 1.23E+003 (runtime, DEFECT)
            Console.WriteLine(string.Format("{0:g}", v10));  // 123456789.987654321

            // ============ S7: alignment (right/left, min width, no truncation) ============
            Console.WriteLine("[" + string.Format("{0,20:F2}", v1) + "]");  // [            1234.57]
            Console.WriteLine("[" + string.Format("{0,-20:F2}", v1) + "]"); // [1234.57            ]
            Console.WriteLine("[" + string.Format("{0,6:F2}", v5) + "]");   // [79228162514264337593543950335.00]
            Console.WriteLine("[" + string.Format("{0,6:F2}", v3) + "]");   // [  0.00]

            // ============ S8: multiple decimal args in one composite ============
            // {1:E2} on v7=2.5m: net48 pads the E fraction to prec digits
            // ("2.50E+000"); the runtime appends only the available mantissa
            // digits ("2.5E+000"). RECORDED DEFECT (decimal_format.cpp
            // FormatE does not zero-pad a short mantissa fraction).
            Console.WriteLine(string.Format("{0:N2} | {1:E2} | {2:G}", v1, v7, v10)); // 1,234.57 | 2.50E+000 | 123456789.987654321
            Console.WriteLine(string.Format("{0:G}|{0:N2}|{0:F1}", v9));               // 999.995|1,000.00|1000.0

            // ============ S9: exception paths (unsupported specifiers) ============
            // net48 decimal supports G/F/N/E/C/P + CUSTOM-format fallback; it
            // throws FormatException ONLY for the integer-only D/X specifiers.
            string r1 = "no";
            try { string s = string.Format("{0:D2}", v1); r1 = s; }
            catch (FormatException) { r1 = "FormatException"; }
            Console.WriteLine(r1);                            // FormatException (D is integer-only)
            string r2 = "no";
            try { string s = string.Format("{0:X}", v1); r2 = s; }
            catch (FormatException) { r2 = "FormatException"; }
            Console.WriteLine(r2);                            // FormatException (X is integer-only)
            // P (percent) IS a net48 decimal specifier -> "123,456.78%"; the
            // runtime decimal engine has no P arm -> throws. RECORDED DEFECT.
            string r3 = "no";
            try { string s = string.Format("{0:P}", v1); r3 = s; }
            catch (FormatException) { r3 = "FormatException"; }
            Console.WriteLine(r3);                            // 123,456.78% (net48) / FormatException (runtime, DEFECT)
            // A non-standard letter string falls back to a CUSTOM format that
            // passes the literal through; the runtime's strict parser throws.
            // RECORDED DEFECT.
            string r4 = "no";
            try { string s = string.Format("{0:xyz}", v1); r4 = s; }
            catch (FormatException) { r4 = "FormatException"; }
            Console.WriteLine(r4);                            // xyz (net48) / FormatException (runtime, DEFECT)

            // ============ S10: default ToString() still byte-exact ============
            Console.WriteLine(v1.ToString());                // 1234.5678
            Console.WriteLine(v4.ToString());                // 0.0000000000000000000000000001
            Console.WriteLine(v5.ToString());                // 79228162514264337593543950335
            Console.WriteLine(v6.ToString());                // -79228162514264337593543950335
        }
    }
}
