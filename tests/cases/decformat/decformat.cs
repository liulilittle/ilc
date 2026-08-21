//------------------------------------------------------------------------------
// decformat.cs -- DECIMAL FORMAT full-surface probe (e2e batch C30, "decimal
// 格式全测: E/P/G/F/N/C/custom -- 4 缺陷修复后验证"). decfmt (C26) mapped the
// first-contact surface (G/F/N/E/C/lowercase/alignment/composite/exceptions)
// and decfmt2 (C28) went DEEP on the three families that were then DEFECTS
// (E exponent form, P percent, CUSTOM literal fallback). THIS case is the
// post-fix FULL surface: every specifier letter G/F/N/E/P/C at multiple
// precisions over a fresh value set, plus the four fixed behaviors re-pinned:
//   * E CASE (net48 keeps the SPECIFIER CASE: {0:e2} -> "1.23e+003").
//   * E ZERO-PAD (a short mantissa fraction pads to the precision: 1m {0:E2}
//     -> "1.00E+000", NOT "1E+000").
//   * P ARM (percent IS a net48 decimal specifier: 123.456m {0:P2} ->
//     "12,345.60%").
//   * CUSTOM UNKNOWN-PATTERN VERBATIM fallback ({0:xyz} -> "xyz").
// All four were fixed in 26e71c4 + 8d865d2 (decimal_format.cpp FormatE /
// FormatP / TryParseFormat + the custom verbatim fallback) and verified
// green in decfmt2's expected lines; THIS case re-verifies them on DIFFERENT
// values so the fix is value-independent.
//
// KNOWN REMAINING DEFECT (recorded, NOT re-probed here -- decfmt2 holds it):
// digit-placeholder custom patterns ({0:00.00} etc.) are returned VERBATIM by
// the runtime (the custom-format parser falls back on ANY non-letter first
// char) instead of being interpreted as net48 custom-format digit
// placeholders. decfmt2 expected.txt lines for those patterns stay as the
// recorded byte diff.
//
// Every decimal value is a NAMED decimal local first (decops discipline -- the
// icall RESULT slot / temp-bucket rule keeps the case compiler-stable). All
// specifiers go through string.Format so the boxed decimal flows through the
// composite icall (icall_format_composite_item -> icall_decimal_to_string_
// custom, decimal_format.cpp) -- the only wired decimal-format channel.
// Expected values are in trailing comments (written from the net48 oracle;
// zh-CN N/F/E/P layouts use "." + "," identical to invariant; the C symbol is
// U+00A5).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Decformat
    {
        public static void Main()
        {
            // ---- the value set (named decimals; fresh vs decfmt/decfmt2) ----
            decimal v1 = 0.5m;                              // half tie (away)
            decimal v2 = -0.5m;                             // negative half tie
            decimal v3 = 1m;                                // unit
            decimal v4 = -1.0m;                             // negative unit (scale 1; avoids decimal.MinusOne field)
            decimal v5 = 1234567890123456789012345678m;     // 28-digit integer
            decimal v6 = 0.0000000000000000000000000001m;   // scale-28 unit
            decimal v7 = 123.456m;                          // scale 3
            decimal v8 = 999.995m;                          // front carry
            decimal v9 = 0.005m;                            // small rounding
            decimal v10 = -0.005m;                          // negative small
            decimal v11 = 3.14159265358979m;                // 14-digit pi
            decimal v12 = 100m;                             // round hundred

            // ============ S1: G family (default / explicit G) ============
            Console.WriteLine(string.Format("{0}", v7));    // 123.456
            Console.WriteLine(string.Format("{0:G}", v5));  // 1234567890123456789012345678
            Console.WriteLine(string.Format("{0}", v11));   // 3.14159265358979
            Console.WriteLine(string.Format("{0:G}", v6));  // 0.0000000000000000000000000001
            Console.WriteLine(string.Format("{0}", v2));    // -0.5
            Console.WriteLine(string.Format("{0}", v12));   // 100

            // ============ S2: F family (exact fraction digits, half-away) ============
            Console.WriteLine(string.Format("{0:F0}", v7));  // 123
            Console.WriteLine(string.Format("{0:F2}", v7));  // 123.46
            Console.WriteLine(string.Format("{0:F4}", v11)); // 3.1416
            Console.WriteLine(string.Format("{0:F6}", v11)); // 3.141593
            Console.WriteLine(string.Format("{0:F2}", v8));  // 1000.00 (999.995 carry)
            Console.WriteLine(string.Format("{0:F2}", v9));  // 0.01
            Console.WriteLine(string.Format("{0:F2}", v10)); // -0.01
            Console.WriteLine(string.Format("{0:F2}", v2));  // -0.50
            Console.WriteLine(string.Format("{0:F0}", v1));  // 1 (half away)

            // ============ S3: N family (grouped integer part) ============
            Console.WriteLine(string.Format("{0:N2}", v5));  // 1,234,567,890,123,456,789,012,345,678.00
            Console.WriteLine(string.Format("{0:N0}", v5));  // 1,234,567,890,123,456,789,012,345,678
            Console.WriteLine(string.Format("{0:N2}", v7));  // 123.46
            Console.WriteLine(string.Format("{0:N4}", v11)); // 3.1416
            Console.WriteLine(string.Format("{0:N2}", v4));  // -1.00
            Console.WriteLine(string.Format("{0:N0}", v1));  // 1 (half away)

            // ============ S4: E family (exponent form, case + zero-pad fixes) ============
            Console.WriteLine(string.Format("{0:E2}", v7));  // 1.23E+002
            Console.WriteLine(string.Format("{0:E4}", v7));  // 1.2346E+002
            Console.WriteLine(string.Format("{0:E2}", v3));  // 1.00E+000 (zero-pad fix)
            Console.WriteLine(string.Format("{0:e2}", v3));  // 1.00e+000 (lowercase fix)
            Console.WriteLine(string.Format("{0:E0}", v3));  // 1E+000
            Console.WriteLine(string.Format("{0:E2}", v4));  // -1.00E+000
            Console.WriteLine(string.Format("{0:E2}", v8));  // 1.00E+003 (front carry)
            Console.WriteLine(string.Format("{0:E2}", v9));  // 5.00E-003
            Console.WriteLine(string.Format("{0:E2}", v10)); // -5.00E-003
            Console.WriteLine(string.Format("{0:E2}", v6));  // 1.00E-028
            Console.WriteLine(string.Format("{0:E6}", v11)); // 3.141593E+000
            Console.WriteLine(string.Format("{0:E2}", v5));  // 1.23E+027
            Console.WriteLine(string.Format("{0:E0}", v5));  // 1E+027
            Console.WriteLine(string.Format("{0:e0}", v7));  // 1e+002

            // ============ S5: P family (percent arm fix) ============
            Console.WriteLine(string.Format("{0:P2}", v3));  // 100.00%
            Console.WriteLine(string.Format("{0:P0}", v3));  // 100%
            Console.WriteLine(string.Format("{0:P}", v7));   // 12,345.60%
            Console.WriteLine(string.Format("{0:P2}", v4));  // -100.00%
            Console.WriteLine(string.Format("{0:P0}", v1));  // 50%
            Console.WriteLine(string.Format("{0:P2}", v2));  // -50.00%
            Console.WriteLine(string.Format("{0:P4}", v6));  // 0.0000%
            Console.WriteLine(string.Format("{0:P1}", v9));  // 0.5%

            // ============ S6: C family (currency, zh-CN ¥ symbol) ============
            Console.WriteLine(string.Format("{0:C2}", v7));  // ¥123.46
            Console.WriteLine(string.Format("{0:C0}", v7));  // ¥123
            Console.WriteLine(string.Format("{0:C2}", v4));  // ¥-1.00
            Console.WriteLine(string.Format("{0:C4}", v11)); // ¥3.1416

            // ============ S7: lowercase specifiers (case-insensitive letter) ============
            Console.WriteLine(string.Format("{0:n3}", v7));  // 123.456
            Console.WriteLine(string.Format("{0:f1}", v9));  // 0.0
            Console.WriteLine(string.Format("{0:e2}", v7));  // 1.23e+002
            Console.WriteLine(string.Format("{0:g}", v7));   // 123.456

            // ============ S8: alignment (right/left, no truncation) ============
            Console.WriteLine("[" + string.Format("{0,18:F2}", v7) + "]");  // [            123.46]
            Console.WriteLine("[" + string.Format("{0,-18:F2}", v7) + "]"); // [123.46            ]
            Console.WriteLine("[" + string.Format("{0,8:F2}", v5) + "]");   // [1234567890123456789012345678.00]

            // ============ S9: multiple decimal args in one composite ============
            Console.WriteLine(string.Format("{0:N2} | {1:E2} | {2:G}", v7, v8, v11)); // 123.46 | 1.00E+003 | 3.14159265358979

            // ============ S10: exception paths (integer-only specifiers) ============
            string r1 = "no";
            try { string s = string.Format("{0:D2}", v7); r1 = s; }
            catch (FormatException) { r1 = "FormatException"; }
            Console.WriteLine(r1);                            // FormatException (D integer-only)
            string r2 = "no";
            try { string s = string.Format("{0:X}", v7); r2 = s; }
            catch (FormatException) { r2 = "FormatException"; }
            Console.WriteLine(r2);                            // FormatException (X integer-only)

            // ============ S11: custom unknown-pattern verbatim fallback (fix) ============
            Console.WriteLine(string.Format("{0:xyz}", v7));  // xyz (verbatim literal)
            Console.WriteLine(string.Format("{0:ab12}", v5)); // ab12

            // ============ S12: default ToString() control ============
            Console.WriteLine(v7.ToString());                 // 123.456
            Console.WriteLine(v11.ToString());                // 3.14159265358979
            Console.WriteLine(v5.ToString());                 // 1234567890123456789012345678
            Console.WriteLine(v6.ToString());                 // 0.0000000000000000000000000001
        }
    }
}
