//------------------------------------------------------------------------------
// strfmtdeep.cs -- STRING FORMAT DEEP probe (e2e batch C55, "string format deep probe:
// composite format / alignment / placeholders -- after the format domain"). Builds on the format-domain surface
// (strfmt C: ToString(format) wired subset; strformat C25: composite
// alignment/precision/forms; format2 C28: wired+defect-exposing; format3:
// deeper composite grammar; customfmt: custom engine) and goes DEEPER on the
// COMPOSITE channel (string.Format / StringBuilder.AppendFormat ->
// AppendFormatCore + icall_format_composite_item) --
// byte-exact vs the net48 oracle, dual-compiler (mingw + msvc).
//
// Every probe is a labeled bool: the formatted text is COMPARED in-process
// against a net48-known literal, so a diff failure pinpoints the exact line
// and the case is immune to culture/encoding oracle artifacts (the U+00A5
// currency symbol, exponent padding) -- the strfmt discipline.
//
// Wired surface exercised (MethodMap.String.cs Format dispatch +
// stringbuilder.cpp AppendFormatCore, the FULL "{N[,alignment][:fmt]}"
// grammar; formatext.cpp icall_format_composite_item klass dispatch:
// Int32/Int64/Double/Single -> icall_format_*_custom, Decimal ->
// icall_decimal_to_string_custom, String -> identity, bool/char/other ->
// icall_object_to_string):
//   * DEEP ALIGNMENT: multi-placeholder lines mixing alignment, reorder,
//     repeats; widths below/equal/above the text; width 0/1; left/right at
//     the SAME field; alignment + custom format combined.
//   * CUSTOM digit patterns through the composite: "000"/"00.00"/"#.##"/
//     "#,##0.00"/"0.0%"/"000.00" on int32/double (the custom engine is the
//     SAME engine ToString(format) uses -- probed here THROUGH the
//     composite channel).
//   * PRECISION LATTICE on every composite arg klass: D/X/N/F/E/G on int32,
//     F/N/E/G on double, F/N/E on decimal, P percent, C currency (compared
//     to the zh-CN ¥-literal in-process -- never printed raw).
//   * ARG-KIND MIXING: int + string + bool + char + null + decimal on one
//     format line, each with its own alignment/spec.
//   * NESTED formats: a composite result fed as an ARG to another composite.
//   * StringBuilder.AppendFormat CHAINS: accumulated content across calls
//     with alignment + custom digits + reorder.
//   * ERROR PATHS: index-out-of-range -> FormatException; malformed
//     "{0,}" / "{x}" / unclosed -> FormatException (caught, bool).
//
// Determinism: pure managed; N/F "." decimal + "," group (same in zh-CN and
// invariant). Expected values in trailing comments.
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class Strfmtdeep {
    public static void Main() {
        // ===== 1. DEEP ALIGNMENT: mixed multi-placeholder lines =====
        Console.WriteLine("a0=" + (string.Format("[{0,5}|{1,-5}]", 42, 42) == "[   42|42   ]"));
        Console.WriteLine("a1=" + (string.Format("[{0,1}|{0,0}|{0,2}]", 5) == "[5|5| 5]"));
        Console.WriteLine("a2=" + (string.Format("[{0,7}|{0,-7}]", -123) == "[   -123|-123   ]"));
        Console.WriteLine("a3=" + (string.Format("[{0,2}]", "abcdefgh") == "[abcdefgh]"));
        Console.WriteLine("a4=" + (string.Format("[{0,6}]", "hello") == "[ hello]"));
        Console.WriteLine("a5=" + (string.Format("[{2,4}|{0,-4}|{1,4}]", "a", "b", "z") == "[   z|a   |   b]"));
        Console.WriteLine("a6=" + (string.Format("[{0,5}{0,-5}]", 42) == "[   4242   ]"));
        Console.WriteLine("a7=" + (string.Format("[{1,5}{0,-5}]", "x", 7) == "[    7x    ]"));

        // ===== 2. ALIGNMENT + CUSTOM digit patterns =====
        Console.WriteLine("c0=" + (string.Format("[{0,8:000}]", 42) == "[     042]"));
        Console.WriteLine("c1=" + (string.Format("[{0,-8:000}]", 42) == "[042     ]"));
        Console.WriteLine("c2=" + (string.Format("[{0,8:00.00}]", 3.5) == "[   03.50]"));
        Console.WriteLine("c3=" + (string.Format("[{0,9:#,##0.00}]", 1234.5) == "[ 1,234.50]"));
        Console.WriteLine("c4=" + (string.Format("[{0,8:0.0%}]", 0.25) == "[   25.0%]"));

        // ===== 3. CUSTOM digit patterns through the composite channel =====
        Console.WriteLine("p0=" + (string.Format("{0:000}", 42) == "042"));
        Console.WriteLine("p1=" + (string.Format("{0:00000}", -42) == "-00042"));
        Console.WriteLine("p2=" + (string.Format("{0:00.00}", 3.5) == "03.50"));
        Console.WriteLine("p3=" + (string.Format("{0:#.##}", 3.14159) == "3.14"));
        Console.WriteLine("p4=" + (string.Format("{0:#.##}", 2.0) == "2"));
        Console.WriteLine("p5=" + (string.Format("{0:#,##0.00}", 1234567) == "1,234,567.00"));
        Console.WriteLine("p6=" + (string.Format("{0:#,##0}", -1234) == "-1,234"));
        Console.WriteLine("p7=" + (string.Format("{0:000.00}", 12.5) == "012.50"));
        Console.WriteLine("p8=" + (string.Format("{0:0.0%}", 0.25) == "25.0%"));
        Console.WriteLine("p9=" + (string.Format("{0:0%}", 0.5) == "50%"));

        // ===== 4. PRECISION LATTICE per arg klass =====
        Console.WriteLine("s0=" + (string.Format("{0:D3}", 42) == "042"));
        Console.WriteLine("s1=" + (string.Format("{0:X4}", 255) == "00FF"));
        Console.WriteLine("s2=" + (string.Format("{0:N2}", 1234.5678) == "1,234.57"));
        Console.WriteLine("s3=" + (string.Format("{0:F2}", 3.14159) == "3.14"));
        Console.WriteLine("s4=" + (string.Format("{0:E2}", 1234.5678) == "1.23E+003"));
        Console.WriteLine("s5=" + (string.Format("{0:G4}", 123456) == "1.235E+05"));
        Console.WriteLine("s6=" + (string.Format("{0:F3}", 9.9999) == "10.000"));
        Console.WriteLine("s7=" + (string.Format("{0:N1}", 12345) == "12,345.0"));
        Console.WriteLine("s8=" + (string.Format("{0:P1}", 0.1234) == "12.3%"));
        Console.WriteLine("s9=" + (string.Format("{0:D6}", 123456789012345678L) == "123456789012345678"));
        Console.WriteLine("s10=" + (string.Format("{0:X16}", -1L) == "FFFFFFFFFFFFFFFF"));
        Console.WriteLine("s11=" + (string.Format("{0:N0}", 987654321L) == "987,654,321"));

        // ===== 5. decimal through the composite =====
        Console.WriteLine("d0=" + (string.Format("{0:N2}", 1234.5678m) == "1,234.57"));
        Console.WriteLine("d1=" + (string.Format("{0:F3}", 1.23456m) == "1.235"));
        Console.WriteLine("d2=" + (string.Format("{0:E2}", 123.45m) == "1.23E+002"));
        Console.WriteLine("d3=" + (string.Format("{0:C}", 1234.5m) == "\u00A51,234.50"));
        Console.WriteLine("d4=" + (string.Format("{0:000.00}", 12.345m) == "012.35"));
        Console.WriteLine("d5=" + (string.Format("[{0,10:N2}]", 1234.5678m) == "[  1,234.57]"));

        // ===== 6. ARG-KIND mixing on one line =====
        Console.WriteLine("m0=" + (string.Format("[{0}][{1}][{2}][{3}][{4}]", 42, "s", true, 'c', (object)null)
                          == "[42][s][True][c][]"));
        Console.WriteLine("m1=" + (string.Format("{0,5}|{1,-5}|{2,5}|{3,-5}", 1, "a", 2.5, 'b')
                          == "    1|a    |  2.5|b    "));
        Console.WriteLine("m2=" + (string.Format("{0:D3}-{1:X2}-{2:F1}", 7, 255, 3.5) == "007-FF-3.5"));
        Console.WriteLine("m3=" + (string.Format("{0,6:x}|{1,-6:X4}", 255, 255) == "    ff|00FF  "));
        Console.WriteLine("m4=" + (string.Format("{0:N1}|{1:C}", 1234.5, 50m) == "1,234.5|\u00A550.00"));

        // ===== 7. NESTED formats (composite result as an arg) =====
        Console.WriteLine("n0=" + (string.Format("[{0}]", string.Format("{0:D3}", 7)) == "[007]"));
        Console.WriteLine("n1=" + (string.Format("{0}-{1}", string.Format("{0:X2}", 15), string.Format("{0:N1}", 1.25))
                          == "0F-1.3"));
        Console.WriteLine("n2=" + (string.Format("{0,6}", string.Format("{0,4}", 42)) == "    42"));
        Console.WriteLine("n3=" + (string.Format("{0}{1}{0}", "a", "b") == "aba"));

        // ===== 8. StringBuilder.AppendFormat chains =====
        StringBuilder sb = new StringBuilder();
        sb.AppendFormat("[{0,5}]", 42);
        sb.AppendFormat(" {0,-6:000}", 7);
        sb.AppendFormat(" {0:N2}", 1.5);
        sb.AppendFormat(" {0:X4}", 255);
        Console.WriteLine("b0=" + (sb.ToString() == "[   42] 007    1.50 00FF"));
        StringBuilder sb2 = new StringBuilder();
        sb2.AppendFormat("{0,8:00.00}", 3.5);
        sb2.AppendFormat("{0,8:000}", -12);
        sb2.AppendFormat("{0}", string.Format("{0:D2}", 9));
        Console.WriteLine("b1=" + (sb2.ToString() == "   03.50    -01209"));

        // ===== 9. ERROR PATHS (caught -> bool) =====
        bool e0 = true;
        try { string s = string.Format("{0}{1}{2}", 1, 2); e0 = false; }
        catch (FormatException) { e0 = true; }
        Console.WriteLine("e0_oob=" + e0);                                   // True (caught)
        bool e1 = true;
        try { string s = string.Format("{0,}", 1); e1 = false; }
        catch (FormatException) { e1 = true; }
        Console.WriteLine("e1_align=" + e1);                                 // True (caught)
        bool e2 = true;
        try { string s = string.Format("{x}", 1); e2 = false; }
        catch (FormatException) { e2 = true; }
        Console.WriteLine("e2_malformed=" + e2);                             // True (caught)
        bool e3 = true;
        try { string s = string.Format("{0", 1); e3 = false; }
        catch (FormatException) { e3 = true; }
        Console.WriteLine("e3_unclosed=" + e3);                              // True (caught)
        bool e4 = true;
        try { string s = string.Format("{0,5", 1); e4 = false; }
        catch (FormatException) { e4 = true; }
        Console.WriteLine("e4_unclosed2=" + e4);                             // True (caught)
        bool e5 = true;
        try { string s = string.Format("}", 1); e5 = false; }
        catch (FormatException) { e5 = true; }
        Console.WriteLine("e5_stray=" + e5);                                 // True (caught)
    }
}}
