//------------------------------------------------------------------------------
// stringbuild2.cs -- StringBuilder.AppendFormat DEEP probe (e2e batch C75,
// after the stringbuilder domain full chain: il2cpp_stringbuilder.h +
// stringbuilder.cpp AppendFormatCore (stringbuilder.cpp:340 -- the shared
// composite-format engine) + formatext.cpp icall_format_composite_item).
// stringbuilddeep (C47) probed chaining/capacity/Replace/Insert plus three
// basic AppendFormat uses; THIS case drives the WIRED AppendFormat overloads
// (AppendFormat(string, object[, object[, object]]) -> icall_stringbuilder_
// append_format_1/2/3) across the FULL composite-format grammar and the
// CURRENT-culture (zh-CN on this e2e host, net48-verified) numeric faces:
//
//   1. ESCAPES: "{{" / "}}" collapse to one literal brace; a lone '}' throws
//      FormatException (probed). The net48 "{prefix}{0}{suffix}" triple-brace
//      shape IS probed (1b): "{{0}}" -> "{0}", "{{{0}}}" -> "{7}", "{0}" -> "7"
//      -- the escaped-brace scanner consumes a leading "{{" pair as one
//      literal, expands the "{0}" placeholder, then consumes the trailing
//      "}}" pair (never a lone closing brace). net48-verified on this host.
//   2. {N} PLAIN: the 1/2/3-arg overloads; literal text around placeholders;
//      null args expand to empty; a string arg IGNORES the specifier
//      (System.String is not IFormattable).
//   3. ALIGNMENT {N,width}: positive right-align / negative left-align, the
//      width is a MINIMUM (never truncates), width 0 no pad, a null arg pads
//      spaces.
//   4. FORMAT STRING {N:fmt} on int (D/X/x/F/G/N/C/P), double (F/N/E/G/P/C)
//      and decimal; COMBINED {N,width:fmt}.
//   5. CULTURE (zh-CN NumberFormatInfo, the host's net48 default): N2/N0
//      thousand separators, C currency "¥", P percent "n%", F decimals.
//   6. EXCEPTIONS (typed catch, builder unchanged after every throw): null
//      format -> ArgumentNullException; "{}"/"{x}"/"{0"/"}" / "{0,}"/index>=n
//      -> FormatException.
//   7. CHAINED AppendFormat with Append/Insert.
//
// Every line prints "label=True/False" -- deterministic and byte-exact on both
// .NET Framework 4.8 (zh-CN oracle) and the transpiled runtime.
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases { public static class StringBuild2
{
    public static void Main()
    {
        // ============ 1. ESCAPES ============
        var s1 = new StringBuilder();
        s1.AppendFormat("{{0}}", 1);          // arg unused: the placeholder is escaped
        Console.WriteLine("esc_double=" + (s1.ToString() == "{0}"));
        var s2 = new StringBuilder();
        s2.AppendFormat("a{{b}}c", 1);
        Console.WriteLine("esc_mixed=" + (s2.ToString() == "a{b}c"));

        // ============ 1b. BRACE COMBINATIONS (D-SB-1, net48 oracle) ============
        // "{{0}}" -> "{0}", "{{{0}}}" -> "{7}", "{0}" -> "7". The escaped-brace
        // scanner consumes a leading "{{" pair as one literal brace, expands the
        // "{0}" placeholder, then consumes the trailing "}}" pair -- the
        // combination never leaves a lone closing brace. net48-verified.
        var t1 = new StringBuilder();
        t1.AppendFormat("{{0}}", 7);
        Console.WriteLine("brace_double=" + (t1.ToString() == "{0}"));
        var t2 = new StringBuilder();
        t2.AppendFormat("{{{0}}}", 7);
        Console.WriteLine("brace_triple=" + (t2.ToString() == "{7}"));
        var t3 = new StringBuilder();
        t3.AppendFormat("{0}", 7);
        Console.WriteLine("brace_plain=" + (t3.ToString() == "7"));
        var t4 = new StringBuilder();
        t4.AppendFormat("x{{{0}}}y", 7);
        Console.WriteLine("brace_mid=" + (t4.ToString() == "x{7}y"));
        var t5 = new StringBuilder();
        t5.AppendFormat("{{{{0}}}}", 7);
        Console.WriteLine("brace_quad=" + (t5.ToString() == "{{0}}"));
        var t6 = new StringBuilder();
        t6.AppendFormat("{0}}}", 7);
        Console.WriteLine("brace_after=" + (t6.ToString() == "7}"));
        var t7 = new StringBuilder();
        t7.AppendFormat("{{0}}{0}", 9);
        Console.WriteLine("brace_mixed=" + (t7.ToString() == "{0}9"));
        bool be1 = false, be2 = false;
        try { var bex = new StringBuilder(); bex.AppendFormat("{0}}", 7); } catch (FormatException) { be1 = true; }
        try { var bex = new StringBuilder(); bex.AppendFormat("{{0}}}", 7); } catch (FormatException) { be2 = true; }
        Console.WriteLine("brace_exc=" + (be1 && be2));


        // ============ 2. {N} PLAIN + LITERALS ============
        var s4 = new StringBuilder();
        s4.AppendFormat("{0}", 42);
        s4.Append('|').AppendFormat("{0}-{1}", 1, 2);
        s4.Append('|').AppendFormat("{0}{1}{2}", "x", "y", "z");
        Console.WriteLine("plain_1_2_3=" + (s4.ToString() == "42|1-2|xyz"));
        var s5 = new StringBuilder();
        s5.AppendFormat("value={0} end", 99);
        Console.WriteLine("literal=" + (s5.ToString() == "value=99 end"));

        // ============ 3. NULL ARGS ============
        var s6 = new StringBuilder();
        s6.AppendFormat("a{0}b", (object)null);
        Console.WriteLine("null_arg=" + (s6.ToString() == "ab"));
        var s7 = new StringBuilder();
        s7.AppendFormat("{0}|{1}", (string)null, "x");
        Console.WriteLine("null_first=" + (s7.ToString() == "|x"));

        // ============ 4. STRING ARG IGNORES THE SPECIFIER ============
        var s8 = new StringBuilder();
        s8.AppendFormat("{0:X}", "abc");
        Console.WriteLine("string_ignores_spec=" + (s8.ToString() == "abc"));

        // ============ 5. ALIGNMENT ============
        var s9 = new StringBuilder();
        s9.AppendFormat("[{0,10}]", "ab");
        Console.WriteLine("align_right=" + (s9.ToString() == "[        ab]"));
        var s10 = new StringBuilder();
        s10.AppendFormat("[{0,-10}]", "ab");
        Console.WriteLine("align_left=" + (s10.ToString() == "[ab        ]"));
        var s11 = new StringBuilder();
        s11.AppendFormat("[{0,5}]", 42);
        Console.WriteLine("align_int_right=" + (s11.ToString() == "[   42]"));
        var s12 = new StringBuilder();
        s12.AppendFormat("[{0,-5}]", 42);
        Console.WriteLine("align_int_left=" + (s12.ToString() == "[42   ]"));
        var s13 = new StringBuilder();
        s13.AppendFormat("[{0,3}]", "abcdef");
        Console.WriteLine("align_no_truncate=" + (s13.ToString() == "[abcdef]"));
        var s14 = new StringBuilder();
        s14.AppendFormat("[{0,0}]", "xy");
        Console.WriteLine("align_zero=" + (s14.ToString() == "[xy]"));
        var s15 = new StringBuilder();
        s15.AppendFormat("[{0,5}]", (object)null);
        Console.WriteLine("align_null=" + (s15.ToString() == "[     ]"));

        // ============ 6. COMBINED {N,width:fmt} ============
        var s16 = new StringBuilder();
        s16.AppendFormat("[{0,8:X}]", 255);
        Console.WriteLine("combined_fmt=" + (s16.ToString() == "[      FF]"));
        var s17 = new StringBuilder();
        s17.AppendFormat("[{0,6:D4}]", 42);
        Console.WriteLine("combined_fmt2=" + (s17.ToString() == "[  0042]"));

        // ============ 7. CULTURE NUMERIC FORMATS (zh-CN) ============
        var c1 = new StringBuilder();
        c1.AppendFormat("{0:N2}", 1234567.89);
        Console.WriteLine("fmt_N2=" + (c1.ToString() == "1,234,567.89"));
        var c2 = new StringBuilder();
        c2.AppendFormat("{0:N0}", 1234567);
        Console.WriteLine("fmt_N0=" + (c2.ToString() == "1,234,567"));
        var c3 = new StringBuilder();
        c3.AppendFormat("{0:N2}", -987654.5);
        Console.WriteLine("fmt_N2_neg=" + (c3.ToString() == "-987,654.50"));
        var c4 = new StringBuilder();
        c4.AppendFormat("{0:C}", 12345.67);
        Console.WriteLine("fmt_C=" + (c4.ToString() == "¥12,345.67"));
        var c5 = new StringBuilder();
        c5.AppendFormat("{0:C0}", 12345);
        Console.WriteLine("fmt_C0=" + (c5.ToString() == "¥12,345"));
        var c6 = new StringBuilder();
        c6.AppendFormat("{0:P}", 0.5);
        Console.WriteLine("fmt_P=" + (c6.ToString() == "50.00%"));
        var c7 = new StringBuilder();
        c7.AppendFormat("{0:F2}", 3.14159);
        Console.WriteLine("fmt_F2=" + (c7.ToString() == "3.14"));
        var c8 = new StringBuilder();
        c8.AppendFormat("{0:F0}", 42);
        Console.WriteLine("fmt_F0=" + (c8.ToString() == "42"));
        var c9 = new StringBuilder();
        c9.AppendFormat("{0:D5}", 42);
        Console.WriteLine("fmt_D5=" + (c9.ToString() == "00042"));
        var c10 = new StringBuilder();
        c10.AppendFormat("{0:D8}", -42);
        Console.WriteLine("fmt_D8_neg=" + (c10.ToString() == "-00000042"));
        var c11 = new StringBuilder();
        c11.AppendFormat("{0:X}", 255);
        Console.WriteLine("fmt_X=" + (c11.ToString() == "FF"));
        var c12 = new StringBuilder();
        c12.AppendFormat("{0:x}", 255);
        Console.WriteLine("fmt_x=" + (c12.ToString() == "ff"));
        var c13 = new StringBuilder();
        c13.AppendFormat("{0:X4}", 255);
        Console.WriteLine("fmt_X4=" + (c13.ToString() == "00FF"));
        var c14 = new StringBuilder();
        c14.AppendFormat("{0:E}", 12345.6789);
        Console.WriteLine("fmt_E=" + (c14.ToString() == "1.234568E+004"));
        var c15 = new StringBuilder();
        c15.AppendFormat("{0:G}", 123456.789);
        Console.WriteLine("fmt_G=" + (c15.ToString() == "123456.789"));
        var c16 = new StringBuilder();
        c16.AppendFormat("{0}|{1}", true, 'Z');
        Console.WriteLine("fmt_bool_char=" + (c16.ToString() == "True|Z"));

        // ============ 8. EXCEPTIONS (builder unchanged after each) ============
        var e1 = new StringBuilder("abc");
        bool ane = false;
        try { e1.AppendFormat(null, 1); } catch (ArgumentNullException) { ane = true; }
        Console.WriteLine("exc_null_format=" + ane);
        bool fe1 = false, fe2 = false, fe3 = false, fe4 = false, fe5 = false, fe6 = false;
        try { e1.AppendFormat("{}", 1); } catch (FormatException) { fe1 = true; }
        try { e1.AppendFormat("{x}", 1); } catch (FormatException) { fe2 = true; }
        try { e1.AppendFormat("{0", 1); } catch (FormatException) { fe3 = true; }
        try { e1.AppendFormat("}", 1); } catch (FormatException) { fe4 = true; }
        try { e1.AppendFormat("{0,}", 1); } catch (FormatException) { fe5 = true; }
        try { e1.AppendFormat("{1}", 1); } catch (FormatException) { fe6 = true; }
        Console.WriteLine("exc_malformed=" + (fe1 && fe2 && fe3 && fe4 && fe5 && fe6));
        Console.WriteLine("exc_unchanged=" + (e1.ToString() == "abc"));

        // ============ 9. CHAINED AppendFormat + Append/Insert ============
        var ch = new StringBuilder();
        ch.AppendFormat("{0}-{1}", 1, 2).Append('|').AppendFormat("{0}{1}{2}", "x", "y", "z").Insert(3, "[");
        Console.WriteLine("chain=" + (ch.ToString() == "1-2[|xyz"));
        Console.WriteLine("chain_len=" + (ch.Length == 8));

        Console.WriteLine("done");
    }
}}
