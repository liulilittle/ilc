//------------------------------------------------------------------------------
// format3.cs -- end-to-end reference case for System.String.Format DEEP
// coverage, layer 3 (net48 oracle). Extends format2 (which unlocked
// alignment/precision/hex -- 33/33 byte-exact at HEAD) with deeper
// composite-format grammar:
//
//   * alignment {N,w} with positive AND negative widths, various widths,
//     string AND numeric arguments (right-align pad-left / left-align
//     pad-right; widths <= content do NOT truncate);
//   * precision {0:f2} VARIANTS: f0/f1/f3, F2 uppercase, plus the composite
//     {0,10:f2} (alignment + precision combined);
//   * hex {0:x} / {0:X} VARIANTS with digit widths x4/X8 (zero-padded) and
//     combined {0,8:x};
//   * zero-padded decimal {0:D5}, group-separated number {0:N2}, two-digit
//     {0:d2};
//   * NESTED format items: {0}{1}{0} reuse, {1,3}|{0,-3} reorder + mixed
//     alignment, multi-item lines mixing alignment/precision/hex;
//   * index-out-of-range -> FormatException (already wired in format2, kept
//     as a regression pin);
//   * escaped braces {{ }} and literal text around placeholders.
//
// WIRED surface (MethodMap.String.cs Format -> stringbuilder.cpp
// AppendFormatCore + formatext.cpp icall_format_composite_item, batch 22b):
// the FULL composite grammar {N[,w][:spec]} -- alignment (pos/neg width),
// standard specifiers via the boxed-klass dispatch (Int32/Int64/Double/
// Single/Decimal/String), empty format string = default, index/malformed
// -> FormatException.
//
// Every line is deterministic; net48 is the reference baseline
// (run-tests.ps1 diffs the transpiled run byte-for-byte). Lines that
// diverge expose runtime defects (recorded, NOT fixed -- batch rule).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class Format3 {
    public static void Main() {
        // ============ 1. alignment {N,w}: positive (right) / negative (left) ============
        Console.WriteLine("a0=" + (string.Format("[{0,5}]", 42) == "[   42]"));
        Console.WriteLine("a1=" + (string.Format("[{0,-5}]", 42) == "[42   ]"));
        Console.WriteLine("a2=" + (string.Format("[{0,5}]", "ab") == "[   ab]"));
        Console.WriteLine("a3=" + (string.Format("[{0,-5}]", "ab") == "[ab   ]"));
        Console.WriteLine("a4=" + (string.Format("[{0,12}]", 42) == "[          42]"));
        Console.WriteLine("a5=" + (string.Format("[{0,0}]", 5) == "[5]"));
        Console.WriteLine("a6=" + (string.Format("[{0,1}]", 5) == "[5]"));
        Console.WriteLine("a7=" + (string.Format("[{0,2}]", 5) == "[ 5]"));
        Console.WriteLine("a8=" + (string.Format("[{0,6}]", "hello") == "[ hello]"));
        Console.WriteLine("a9=" + (string.Format("[{0,-6}]", "hello") == "[hello ]"));
        Console.WriteLine("a10=" + (string.Format("val={0,4} done", 7) == "val=   7 done"));
        Console.WriteLine("a11=" + (string.Format("[{0,-3}]{1,3}", "a", "b") == "[a  ]  b"));

        // ============ 2. precision {0:f2} variants ============
        Console.WriteLine("p0=" + (string.Format("{0:f2}", 3.14159) == "3.14"));
        Console.WriteLine("p1=" + (string.Format("{0:f1}", 3.14159) == "3.1"));
        Console.WriteLine("p2=" + (string.Format("{0:f0}", 3.7) == "4"));
        Console.WriteLine("p3=" + (string.Format("{0:F2}", 3.14159) == "3.14"));
        Console.WriteLine("p4=" + (string.Format("{0,10:f2}", 3.14159) == "      3.14"));
        Console.WriteLine("p5=" + (string.Format("{0:N2}", 1234.5) == "1,234.50"));

        // ============ 3. hex {0:x} / {0:X} variants ============
        Console.WriteLine("h0=" + (string.Format("{0:x4}", 255) == "00ff"));
        Console.WriteLine("h1=" + (string.Format("{0:X8}", 255) == "000000FF"));
        Console.WriteLine("h2=" + (string.Format("{0,8:x}", 255) == "      ff"));
        Console.WriteLine("h3=" + (string.Format("{0:X} vs {1:x}", 255, 255) == "FF vs ff"));

        // ============ 4. zero-padded decimal / two-digit ============
        Console.WriteLine("d0=" + (string.Format("{0:D5}", 42) == "00042"));
        Console.WriteLine("d1=" + (string.Format("{0:d2}", 7) == "07"));

        // ============ 5. nested format items + literal text ============
        Console.WriteLine("n0=" + (string.Format("{0}{1}{0}", "a", "b") == "aba"));
        Console.WriteLine("n1=" + (string.Format("{1,3}|{0,-3}", "x", "y") == "  y|x  "));
        Console.WriteLine("n2=" + (string.Format("{0,5} {1,-5} {2:x}", 1, "ab", 255) == "    1 ab    ff"));
        Console.WriteLine("n3=" + (string.Format("{{{0}}}", 99) == "{99}"));
        Console.WriteLine("n4=" + (string.Format("{0}", "hello") == "hello"));

        // ============ 6. index out of range -> FormatException (regression pin) ============
        bool oob = false;
        try { string.Format("{2}", "a", "b"); } catch (FormatException) { oob = true; }
        Console.WriteLine("oob=" + oob);
        bool oob2 = false;
        try { string.Format("{5}", "a", "b", "c"); } catch (FormatException) { oob2 = true; }
        Console.WriteLine("oob2=" + oob2);

        // ============ 7. mixed: alignment + precision + hex on one line ============
        Console.WriteLine("m0=" + (string.Format("{0,6:f2}", 3.14159) == "  3.14"));
        Console.WriteLine("m1=" + (string.Format("{0,-6:x}", 255) == "ff    "));
        Console.WriteLine("m2=" + (string.Format("[{0,8:D3}][{1,-4}]", 7, "z") == "[     007][z   ]"));
    }
}}
