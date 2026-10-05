//------------------------------------------------------------------------------
// formatchain.cs -- FORMAT CHAIN deep probe (e2e batch C71). Builds on the
// format-domain surface (strfmt / strformat / format2 / format3 / strfmtdeep:
// composite grammar, alignment, precision, custom digits, single-level nesting
// of Format results, AppendFormat chains) and goes DEEPER on the CHAIN axis --
// the output of one composite format feeding the next:
//
//   1. MULTI-LEVEL NESTING (2-3 levels): a Format result used as an ARG of
//      another Format, recursively; reordered inner results in the outer
//      call; a Format result INSIDE a formatted placeholder.
//   2. ALIGNMENT OVER NESTED OUTPUT: alignment applied to an already-formatted
//      inner result (2-level alignment stacks: inner {0,4} -> outer {0,8});
//      left/right alignment at both levels; alignment + precision at the
//      SAME placeholder inside a nested chain.
//   3. ALIGNMENT + PRECISION COMBINED per chain level: {0,10:D3}, {0,8:f2},
//      {0,-10:X4}, {0,6:000}, {0,12:N2} (the strformat lattice) NESTED one
//      level deeper than the earlier case.
//   4. FORMAT-STRING CHAIN: a format string built by string.Format (whose own
//      placeholders expand first) reused as the format of another Format.
//   5. StringBuilder.AppendFormat CHAIN feeding a string.Format (builder
//      ToString as a nested arg), and a builder whose AppendFormat payload
//      is itself a string.Format result.
//   6. ESCAPING INTERLEAVED WITH NESTING: {{ }} around a nested Format result.
//
// Every probe is a labeled bool comparing the chained result against a
// net48-known literal (the strfmt discipline -- diff stays immune to
// culture/encoding artifacts). net48 is the byte reference.
//
// Wired surface: string.Format arity 0/1/2/3 + params object[]
// (MethodMap.String.cs Format -> icall_string_format_0/1/2/3 / icall_string_format)
// and StringBuilder.AppendFormat (MethodMap.StringBuilder.cs -> AppendFormatCore,
// stringbuilder.cpp -- the same composite kernel). Alignment / precision /
// custom digits delegate to icall_format_composite_item (formatext.cpp).
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class Formatchain {
    public static void Main() {
        // ===== 1. multi-level nesting (Format result as an arg) =====
        Console.WriteLine("n0=" + (string.Format("[{0}]", string.Format("{0:D3}", 7)) == "[007]"));
        Console.WriteLine("n1=" + (string.Format("{0}", string.Format("{0}", string.Format("{0:X2}", 255))) == "FF"));
        Console.WriteLine("n2=" + (string.Format("[{0}][{1}]", string.Format("{0:D2}", 7), string.Format("{0:X4}", 255)) == "[07][00FF]"));
        Console.WriteLine("n3=" + (string.Format("{1}/{0}", string.Format("{0:N1}", 1.25), string.Format("{0:F2}", 3.5)) == "3.50/1.3"));
        Console.WriteLine("n4=" + (string.Format("[{0}]", string.Format("{{{0}}}", string.Format("{0:D2}", 9))) == "[{09}]"));
        Console.WriteLine("n5=" + (string.Format("{0}-{1}-{2}", string.Format("{0:D2}", 1), string.Format("{0:D2}", 2), string.Format("{0:D2}", 3)) == "01-02-03"));
        Console.WriteLine("n6=" + (string.Format("{0}", string.Format("{0:N2}", string.Format("{0:D3}", 7).Length)) == "3.00"));
        Console.WriteLine("n7=" + (string.Format("[{0}]", string.Format("{0:x}", 255)) == "[ff]"));

        // ===== 2. alignment over nested output (2-level alignment stacks) =====
        Console.WriteLine("a0=" + (string.Format("[{0,6}]", string.Format("{0:D2}", 7)) == "[    07]"));
        Console.WriteLine("a1=" + (string.Format("[{0,-6}]", string.Format("{0:X4}", 255)) == "[00FF  ]"));
        Console.WriteLine("a2=" + (string.Format("[{0,8}]", string.Format("{0,4}", 42)) == "[      42]"));
        Console.WriteLine("a3=" + (string.Format("[{0,-8}]", string.Format("{0,-4}", 42)) == "[42      ]"));
        Console.WriteLine("a4=" + (string.Format("[{0,8}]", string.Format("{0,-4}", 42)) == "[    42  ]"));
        Console.WriteLine("a5=" + (string.Format("[{0,10}]", string.Format("{0,6}", string.Format("{0:D2}", 7))) == "[        07]"));
        Console.WriteLine("a6=" + (string.Format("[{0,10:N2}]", string.Format("{0,5}", 1234.5)) == "[    1234.5]"));
        Console.WriteLine("a7=" + (string.Format("[{0,8:x}]", 255) == "[      ff]"));

        // ===== 3. alignment + precision at the same placeholder, in-chain =====
        Console.WriteLine("b0=" + (string.Format("[{0,10:D3}]", 42) == "[       042]"));
        Console.WriteLine("b1=" + (string.Format("[{0,8:f2}]", 3.14159) == "[    3.14]"));
        Console.WriteLine("b2=" + (string.Format("[{0,-10:X4}]", 255) == "[00FF      ]"));
        Console.WriteLine("b3=" + (string.Format("[{0,6:000}]", 42) == "[   042]"));
        Console.WriteLine("b4=" + (string.Format("[{0,12:N2}]", 1234.5678) == "[    1,234.57]"));
        Console.WriteLine("b5=" + (string.Format("[{0,8:00.00}]", 3.5) == "[   03.50]"));
        Console.WriteLine("b6=" + (string.Format("[{0,9:#,##0.00}]", 1234.5) == "[ 1,234.50]"));

        // ===== 4. format-string chain (a Format-built format reused) =====
        Console.WriteLine("f0=" + (string.Format(string.Format("{0} {1}", "{0:D2}", "!"), 7) == "07 !"));
        Console.WriteLine("f1=" + (string.Format(string.Format("[{0}]", "{0:X2}"), 255) == "[FF]"));
        Console.WriteLine("f2=" + (string.Format(string.Format("{{{0}}}", "{0}"), "q") == "{0}"));
        Console.WriteLine("f3=" + (string.Format(string.Format("{0,-5}", "{1}"), 1, "ab") == "ab  "));
        Console.WriteLine("f4=" + (string.Format(string.Format("{0}", "{0,6}"), 42) == "    42"));

        // ===== 5. StringBuilder.AppendFormat chains feeding string.Format =====
        StringBuilder sb = new StringBuilder();
        sb.AppendFormat("[{0,5}]", 42);
        sb.AppendFormat(" {0,-6:000}", 7);
        Console.WriteLine("s0=" + (string.Format("[{0}]", sb.ToString()) == "[[   42] 007   ]"));
        Console.WriteLine("s1=" + (string.Format("{0,10}", sb.ToString()) == "[   42] 007   "));
        StringBuilder sb2 = new StringBuilder();
        sb2.AppendFormat("{0}", string.Format("{0:D2}", 9));
        sb2.AppendFormat(" {0,8:N2}", 1.5);
        Console.WriteLine("s2=" + (sb2.ToString() == "09     1.50"));
        Console.WriteLine("s3=" + (string.Format("{{{0}}}", sb2.ToString()) == "{09     1.50}"));
        StringBuilder sb3 = new StringBuilder();
        sb3.AppendFormat("{0,6:x}|{1,-6:X4}", 255, 255);
        Console.WriteLine("s4=" + (sb3.ToString() == "    ff|00FF  "));

        // ===== 6. escaping interleaved with nesting =====
        Console.WriteLine("e0=" + (string.Format("{{{0}}}-{{}}-{1}", string.Format("{0:D2}", 3), "z") == "{03}-{}-z"));
        Console.WriteLine("e1=" + (string.Format("a{{0}}b{0}c", string.Format("{0}", "x")) == "a{0}bxc"));
        Console.WriteLine("e2=" + (string.Format("{{{{", 0) == "{{"));
        Console.WriteLine("e3=" + (string.Format("[{0,6}]", string.Format("{{0}}", 42)) == "[   {0}]"));
    }
}}
