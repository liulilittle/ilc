//------------------------------------------------------------------------------
// format2.cs -- end-to-end reference case for System.String.Format DEEP
// coverage (net48 oracle). Extends consoleio/strfmt (positional composite
// only) with the FULL composite-format grammar: multi-arg and reordered
// placeholders, {{ }} escaping, index-out-of-range FormatException, malformed
// brace FormatException, null-format ArgumentNullException, plus the
// ALIGNMENT {N,w} / PRECISION {N:f2} / HEX {0:x} / DateTime {0:spec} grammar
// the runtime kernel rejects (see ACCEPTANCE SURFACE).
//
// WIRED surface (MethodMap.String.cs Format -> stringbuilder.cpp
// AppendFormatCore / icall_string_format_0/1/2/3 + params array):
//   * {N} placeholders (fixed arities 1/2/3 + params object[])
//   * {{ }} escaping, literal text around placeholders
//   * index >= argCount -> FormatException (runtime throws)
//   * unclosed / stray '}' -> FormatException (runtime throws)
//   * null format -> ArgumentNullException (runtime throws)
//
// ACCEPTANCE SURFACE (defect-exposing probes -- DO NOT make these PASS):
//   * alignment   "{N,w}"   -> the runtime kernel throws NotSupportedException
//     (stringbuilder.cpp AppendFormatCore, the "alignment/format specifiers
//     are not supported" branch). net48 formats "   42". Each probe CATCHES
//     the exception and prints the marker vs the net48 formatted value, so the
//     diff pinpoints the divergence.
//   * precision   "{N:f2}"  -> same NotSupportedException branch.
//   * hex         "{0:x}"   -> same NotSupportedException branch.
//   * DateTime specifier "{0:yyyy}" -> needs a BOXED DateTime argument; the
//     transpiler's box switch has no System.DateTime helper (CodeGenerator.cs
//     ~2948 box_ switch) -> transpile aborts, so the probe stays DOCUMENTED
//     here rather than active (an active probe would kill the whole case).
//
// Every active line prints a bool or a fixed marker; net48 is the reference
// baseline. Lines that diverge from net48 expose runtime defects (recorded,
// NOT fixed -- batch rule).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class Format2 {
    public static void Main() {
        // ============ 1. WIRED: composite placeholders, fixed arities ============
        Console.WriteLine("p0=" + (string.Format("{0}", 42) == "42"));
        Console.WriteLine("p1=" + (string.Format("{0} {1}", "a", "b") == "a b"));
        Console.WriteLine("p2=" + (string.Format("{0},{1},{2}", 1, 2, 3) == "1,2,3"));
        Console.WriteLine("p3=" + (string.Format("{2} {0} {1}", "x", "y", "z") == "z x y"));
        Console.WriteLine("p4=" + (string.Format("{1}{1}", "a", "b") == "bb"));
        Console.WriteLine("p5=" + (string.Format("{0}", true) == "True"));
        Console.WriteLine("p6=" + (string.Format("{0}", 3.5) == "3.5"));
        Console.WriteLine("p7=" + (string.Format("{0}{1}{2}{3}", 1, 2, 3, 4) == "1234"));
        // params object[] form via explicit array
        object[] arr = new object[] { "p", "q", "r" };
        Console.WriteLine("p8=" + (string.Format("{2}{0}{1}", arr) == "rpq"));

        // ============ 2. WIRED: {{ }} escaping and literal text ============
        Console.WriteLine("e0=" + (string.Format("{{0}}", 1) == "{0}"));
        Console.WriteLine("e1=" + (string.Format("{{}}", 1) == "{}"));
        Console.WriteLine("e2=" + (string.Format("{{{0}}}", 7) == "{7}"));
        Console.WriteLine("e3=" + (string.Format("x{{{0}}}y", 5) == "x{5}y"));
        Console.WriteLine("e4=" + (string.Format("a}}b", 1) == "a}b"));
        Console.WriteLine("e5=" + (string.Format("{{{{", 1) == "{{"));
        Console.WriteLine("e6=" + (string.Format("plain", 1) == "plain"));
        Console.WriteLine("e7=" + (string.Format("", 1) == ""));

        // ============ 3. WIRED: index-out-of-range -> FormatException ============
        bool oob = false;
        try { string.Format("{0}", new object[] { "only" }); string x = string.Format("{1}", "a"); Console.WriteLine(x); }
        catch (FormatException) { oob = true; }
        Console.WriteLine("oob=" + oob);
        bool oob2 = false;
        try { string.Format("{5}", "a", "b", "c"); } catch (FormatException) { oob2 = true; }
        Console.WriteLine("oob2=" + oob2);

        // ============ 4. WIRED: malformed braces -> FormatException ============
        bool mal1 = false, mal2 = false, mal3 = false;
        try { string.Format("{0", 1); } catch (FormatException) { mal1 = true; }
        try { string.Format("}", 1); } catch (FormatException) { mal2 = true; }
        try { string.Format("{x}", 1); } catch (FormatException) { mal3 = true; }
        Console.WriteLine("mal1=" + mal1);
        Console.WriteLine("mal2=" + mal2);
        Console.WriteLine("mal3=" + mal3);

        // ============ 5. WIRED: null format -> ArgumentNullException ============
        bool nf = false;
        try { string.Format(null, 1); } catch (ArgumentNullException) { nf = true; }
        Console.WriteLine("nullfmt=" + nf);

        // ============ 6. DEFECT EXPOSURE: alignment {N,w} ============
        // net48: string.Format("{0,5}", 42) == "   42" (right-aligned, width 5).
        // ILC runtime: NotSupportedException (AppendFormatCore alignment branch).
        string aln = "?";
        bool alnNse = false;
        try { aln = string.Format("{0,5}", 42); } catch (NotSupportedException) { alnNse = true; }
        Console.WriteLine("aln_val=" + (alnNse ? "NSE" : (aln == "   42" ? "OK" : aln)));
        Console.WriteLine("aln_nse=" + alnNse);
        string alnL = "?";
        bool alnLNse = false;
        try { alnL = string.Format("{0,-5}", 42); } catch (NotSupportedException) { alnLNse = true; }
        Console.WriteLine("alnl_val=" + (alnLNse ? "NSE" : (alnL == "42   " ? "OK" : alnL)));
        Console.WriteLine("alnl_nse=" + alnLNse);

        // ============ 7. DEFECT EXPOSURE: precision {N:f2} ============
        string prc = "?";
        bool prcNse = false;
        try { prc = string.Format("{0:f2}", 3.14159); } catch (NotSupportedException) { prcNse = true; }
        Console.WriteLine("prc_val=" + (prcNse ? "NSE" : (prc == "3.14" ? "OK" : prc)));
        Console.WriteLine("prc_nse=" + prcNse);

        // ============ 8. DEFECT EXPOSURE: hex {0:x} / {0:X} ============
        string hx = "?";
        bool hxNse = false;
        try { hx = string.Format("{0:x}", 255); } catch (NotSupportedException) { hxNse = true; }
        Console.WriteLine("hex_val=" + (hxNse ? "NSE" : (hx == "ff" ? "OK" : hx)));
        Console.WriteLine("hex_nse=" + hxNse);
        string hxU = "?";
        bool hxUNse = false;
        try { hxU = string.Format("{0:X}", 255); } catch (NotSupportedException) { hxUNse = true; }
        Console.WriteLine("hexu_val=" + (hxUNse ? "NSE" : (hxU == "FF" ? "OK" : hxU)));
        Console.WriteLine("hexu_nse=" + hxUNse);

        // ============ 9. DOCUMENTED (acceptance surface, not active) ============
        // DateTime specifier "{0:yyyy}" needs a BOXED DateTime argument.
        // net48: string.Format("{0:yyyy}", new DateTime(2020,6,15)) == "2020".
        // The transpiler box switch has no System.DateTime helper -> the case
        // would abort at transpile. Boxed-DateTime support is the missing
        // layer; format specifiers themselves also hit the AppendFormatCore
        // NotSupportedException branch once the arg CAN be passed.
    }
}}
