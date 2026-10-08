// strformat.cs -- STRING FORMAT deep probe (e2e batch C25, "string format deep probe:
// composite-format all forms / alignment / precision -- after the format completion"). Builds on strfmt (per-type
// ToString(format) wired subset) and format2 (which EXPOSED alignment/precision
// as NotSupportedException defects). The composite engine is now complete --
// stringbuilder.cpp AppendFormatCore + formatext.cpp icall_format_composite_item
// support the FULL "{N[,alignment][:formatString]}" grammar. This case verifies
// the complete surface against the net48 oracle byte-for-byte.
//
// Coverage:
//   * ALIGNMENT: right ({0,w}) / left ({0,-w}) at widths below/equal/above the
//     text length (min-width semantics -- never truncates), on ints and strings.
//   * ALIGNMENT + FORMAT combined: "{0,10:D3}", "{0,8:f2}", "{0,8:x}",
//     "{0,-10:X4}", "{0,6:000}", left/right align over custom-digit formats.
//   * PRECISION through the composite: D5/X4/F2/N1/E2/G4/000 on boxed ints,
//     F2/N2 on doubles, N2 on decimal (boxed decimal routes through the
//     decimal custom icall).
//   * ALL FORMS: fixed arity 1/2/3, params object[], reordered indexes,
//     repeated placeholders, {{ }} escaping interleaved with alignment,
//     multi-arg mixed widths.
//   * ARG KIND dispatch: string arg with a specifier (ignored -- padded to the
//     alignment), null arg (empty, still aligned), boxed bool (plain "True",
//     aligned).
//   * StringBuilder.AppendFormat: the SAME kernel through the builder surface
//     (alignment + precision), with the builder content accumulated across
//     calls.
//
// Determinism: pure managed. N/F group/separators are "." decimal + ","
// grouping (identical in zh-CN and invariant) so the raw strings are stable.
// The net48 oracle captures the exact expected output (exponent padding etc.
// is whatever net48 produces -- the ILC side must match byte-for-byte).
using System;
using System.Text;

namespace Cases { public static class Strformat {
    public static void Main() {
        // ============ 1. alignment: right / left, min-width semantics ============
        Console.WriteLine(string.Format("[{0,5}]", 42));            // [   42]
        Console.WriteLine(string.Format("[{0,-5}]", 42));           // [42   ]
        Console.WriteLine(string.Format("[{0,5}]", "ab"));          // [   ab]
        Console.WriteLine(string.Format("[{0,-5}]", "ab"));         // [ab   ]
        Console.WriteLine(string.Format("[{0,2}]", "abcdef"));      // [abcdef] (no truncation)
        Console.WriteLine(string.Format("[{0,0}]", 42));            // [42]
        Console.WriteLine(string.Format("[{0,1}]", 42));            // [42]
        Console.WriteLine(string.Format("[{0,7}]", -123));          // [   -123]
        Console.WriteLine(string.Format("[{0,-7}]", -123));         // [-123   ]

        // ============ 2. alignment + format string combined ============
        Console.WriteLine(string.Format("[{0,10:D3}]", 42));        // [      0042]
        Console.WriteLine(string.Format("[{0,8:f2}]", 3.14159));    // [    3.14]
        Console.WriteLine(string.Format("[{0,8:x}]", 255));         // [      ff]
        Console.WriteLine(string.Format("[{0,-10:X4}]", 255));      // [00FF      ]
        Console.WriteLine(string.Format("[{0,6:000}]", 42));        // [   042]
        Console.WriteLine(string.Format("[{0,12:N2}]", 1234.5678)); // [    1,234.57]

        // ============ 3. precision through the composite grammar ============
        Console.WriteLine(string.Format("[{0:D5}]", 42));           // [00042]
        Console.WriteLine(string.Format("[{0:X4}]", -123));         // [FFFFFF85]
        Console.WriteLine(string.Format("[{0:F2}]", 3.14159));      // [3.14]
        Console.WriteLine(string.Format("[{0:N1}]", 12345));        // [12,345.0]
        Console.WriteLine(string.Format("[{0:E2}]", 1234.5678));    // [1.23E+003]
        Console.WriteLine(string.Format("[{0:G4}]", 123456));       // [1.235E+05]
        Console.WriteLine(string.Format("[{0:F1}]", 9.96));         // [10.0]
        Console.WriteLine(string.Format("[{0:D2}]", 7));            // [07]
        Console.WriteLine(string.Format("[{0:000}]", 42));          // [042]
        Console.WriteLine(string.Format("[{0:X8}]", 255));          // [000000FF]

        // ============ 4. all forms: arities / params / reorder / repeat ============
        Console.WriteLine(string.Format("{0}|{1}|{2}", "a", "b", "c"));          // a|b|c
        object[] arr = new object[] { "p", "q", "r" };
        Console.WriteLine(string.Format("{2}{0}{1}", arr));                      // rpq
        Console.WriteLine(string.Format("{1} {0} {1}", "x", "y"));               // y x y
        Console.WriteLine(string.Format("{2,4} {0} {1,3}", "x", "y", "z"));      //    z x   y
        Console.WriteLine(string.Format("{0,5}|{1,-5}|{2}", "a", "b", 7));       //     a|b    |7
        Console.WriteLine(string.Format("{0,3}{1,3}{2,3}", 1, 2, 3));            //   1  2  3
        Console.WriteLine(string.Format("{{0}} {0,3}", 42));                     // {0}  42
        Console.WriteLine(string.Format("x{{{0,3}}}y", 5));                      // x{  5}y

        // ============ 5. arg-kind dispatch ============
        Console.WriteLine(string.Format("[{0,6:x}]", "hello"));     // [ hello] (specifier ignored for string)
        Console.WriteLine(string.Format("[{0}]", (object)null));    // [] (null -> empty)
        Console.WriteLine(string.Format("[{0,5}]", (object)null));  // [     ]
        Console.WriteLine(string.Format("[{0,6}]", true));          // [  True]
        Console.WriteLine(string.Format("[{0,10:N2}]", 1234.5678m));// [  1,234.57] (boxed decimal)
        Console.WriteLine(string.Format("[{0,6:F1}]", 3.5));        // [   3.5]

        // ============ 6. StringBuilder.AppendFormat (same kernel) ============
        StringBuilder sb1 = new StringBuilder();
        sb1.AppendFormat("[{0,5}]", 42);
        Console.WriteLine(sb1.ToString());                          // [   42]
        sb1.AppendFormat(" {0,-5}", "ab");
        Console.WriteLine(sb1.ToString());                          // [   42] ab   
        sb1.AppendFormat(" {0,8:f2}", 3.14159);
        Console.WriteLine(sb1.ToString());                          // [   42] ab        3.14
        sb1.AppendFormat(" {0:D3} {1:X4}", 7, 255);
        Console.WriteLine(sb1.ToString());                          // [   42] ab        3.14 007 00FF
        StringBuilder sb2 = new StringBuilder();
        sb2.AppendFormat("[{0,9:N1}]", 1234.5);
        Console.WriteLine(sb2.ToString());                          // [  1,234.5]
    }
}}
