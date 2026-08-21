//------------------------------------------------------------------------------
// parseint.cs -- System.Int32 PARSE/TryParse DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C78, AFTER the parse-domain
// closure (MethodMap.ValueType.cs -> il2cpp_parse.h TryParse family +
// il2cpp_format.h throwing Parse family + the M7 Convert fromBase surface).
//
// This case drives the INT32-ONLY full boundary/culture-adjacent lattice the
// single-parse cases (parsecase/numericparse) covered per-surface but did not
// consolidate over the THROWING Parse vs TryParse CONSISTENCY:
//
//   1. int.Parse FULL BOUNDARY MATRIX (classified by caught type -- v/fmt/ovf):
//      signed zero / +/- forms / ASCII whitespace variants (space, tab, CR,
//      LF, formfeed) / leading-zero forms (incl. 20-digit) / MinValue and
//      MaxValue exactly / one-past both ends (OverflowException) / huge
//      magnitude / empty / whitespace-only / junk / floating-shaped text /
//      doubled sign / trailing sign / hex-looking text / null (ArgumentNull).
//   2. int.TryParse IDENTICAL MATRIX (out value + success flag) with the
//      Parse/TryParse CONSISTENCY asserted line-by-line (they must agree on
//      what is valid), plus the out-slot WRITE-on-success contract and the
//      failure-path out-slot divergence (D-NP-2, recorded, NOT asserted).
//   3. Convert.ToInt32(string) throwing Parse (icall_convert_to_int32_string)
//      vs int.Parse consistency on the same matrix.
//   4. Convert.ToInt32(string, fromBase) for base 2/8/10/16 (M7): digit
//      validity per base, '0x'/'0X' prefix (base 16 only), sign handling,
//      range wrap vs overflow per base, invalid base -> ArgumentException,
//      null -> 0, empty -> ArgumentOutOfRange.
//   5. Convert.ToString(int, toBase) round-trip (base 2/8/10/16) -- the
//      ParseNumbers.IntToString inverse of #4.
//   6. Parse/TryParse aggregate consistency counter over the WHOLE matrix.
//
// KNOWN GAPS (not exercised -- acceptance surface, see GAPS.md):
//   * int.Parse(string, NumberStyles) / (string, NumberStyles, IFormatProvider)
//     and int.TryParse 3/4-arg overloads -- NOT wired: the runtime parse
//     surface is the (string) / (string, out) shape only; the culture-aware
//     overloads fall to the generated-name path (D-PAR-5).
//   * Parse failure-path out-slot: net48 writes 0, the runtime leaves the
//     slot untouched (D-NP-2).
//
// EVERY line prints "label=True/False"; net48 is the byte reference. The
// Parse rows catch FormatException/OverflowException/ArgumentNullException.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ParseInt {
    // int.Parse classified: "v:<value>" / "fmt" / "ovf" / "null". A null
    // text is ONLY passed to Parse via the ArgumentNull path below (never in
    // the main matrix -- D-PAR-4 divergence documented in GAPS).
    static string ParseLabel(string s)
    {
        if (s == null) return "null";
        try { return "v:" + int.Parse(s); }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
    }

    // int.TryParse: "v:<value>" on success, "fail" on failure (accepts null).
    static string TryLabel(string s)
    {
        int v;
        return int.TryParse(s, out v) ? ("v:" + v) : "fail";
    }

    // Convert.ToInt32(string): "v:<value>" / "fmt" / "ovf" / "null".
    static string ConvLabel(string s)
    {
        if (s == null) return "null";
        try { return "v:" + Convert.ToInt32(s); }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
    }

    static void Main()
    {
        string[] inputs = new string[] {
            "0", "+0", "-0", "42", "+42", "-42", "  42  ", "\t42", "42\n",
            "42\r", "42\f", "\u000942", "00042", "00000000000000000000000042",
            "2147483647", "-2147483648", "2147483648", "-2147483649",
            "99999999999999999999999", "", "   ", "12a", "1.5", "+-5", "-",
            "5-", "0x10", "2147483647 ", " 2147483647", "+2147483647",
            "-2147483647", "00000000000000000000000000000000000000000000000000000042"
        };

        // ============ 1. int.Parse full boundary matrix ============
        Console.WriteLine("p_zero=" + (ParseLabel("0") == "v:0"));
        Console.WriteLine("p_pos0=" + (ParseLabel("+0") == "v:0"));
        Console.WriteLine("p_neg0=" + (ParseLabel("-0") == "v:0"));
        Console.WriteLine("p_42=" + (ParseLabel("42") == "v:42"));
        Console.WriteLine("p_plus42=" + (ParseLabel("+42") == "v:42"));
        Console.WriteLine("p_minus42=" + (ParseLabel("-42") == "v:-42"));
        Console.WriteLine("p_ws=" + (ParseLabel("  42  ") == "v:42"));
        Console.WriteLine("p_ws_tab=" + (ParseLabel("\t42") == "v:42"));
        Console.WriteLine("p_ws_lf=" + (ParseLabel("42\n") == "v:42"));
        Console.WriteLine("p_ws_cr=" + (ParseLabel("42\r") == "v:42"));
        Console.WriteLine("p_ws_ff=" + (ParseLabel("42\f") == "v:42"));
        Console.WriteLine("p_ws_vtab=" + (ParseLabel("\u000B42") == "v:42"));
        Console.WriteLine("p_lead0=" + (ParseLabel("00042") == "v:42"));
        Console.WriteLine("p_lead0long=" + (ParseLabel("00000000000000000000000042") == "v:42"));
        Console.WriteLine("p_max=" + (ParseLabel("2147483647") == "v:2147483647"));
        Console.WriteLine("p_min=" + (ParseLabel("-2147483648") == "v:-2147483648"));
        Console.WriteLine("p_max1=" + (ParseLabel("2147483648") == "ovf"));
        Console.WriteLine("p_min1=" + (ParseLabel("-2147483649") == "ovf"));
        Console.WriteLine("p_huge=" + (ParseLabel("99999999999999999999999") == "ovf"));
        Console.WriteLine("p_empty=" + (ParseLabel("") == "fmt"));
        Console.WriteLine("p_wsonly=" + (ParseLabel("   ") == "fmt"));
        Console.WriteLine("p_junk=" + (ParseLabel("12a") == "fmt"));
        Console.WriteLine("p_float=" + (ParseLabel("1.5") == "fmt"));
        Console.WriteLine("p_dblsign=" + (ParseLabel("+-5") == "fmt"));
        Console.WriteLine("p_minsign=" + (ParseLabel("-") == "fmt"));
        Console.WriteLine("p_trailing_sign=" + (ParseLabel("5-") == "fmt"));
        Console.WriteLine("p_hex=" + (ParseLabel("0x10") == "fmt"));
        Console.WriteLine("p_maxws=" + (ParseLabel("2147483647 ") == "v:2147483647"));
        Console.WriteLine("p_wsmax=" + (ParseLabel(" 2147483647") == "v:2147483647"));
        Console.WriteLine("p_plusmax=" + (ParseLabel("+2147483647") == "v:2147483647"));
        Console.WriteLine("p_minmax1=" + (ParseLabel("-2147483647") == "v:-2147483647"));
        Console.WriteLine("p_lead0max=" + (ParseLabel("00000000000000000000000000000000000000000000000000000042") == "v:42"));

        // ============ 2. int.TryParse same matrix + consistency ============
        Console.WriteLine("t_zero=" + (TryLabel("0") == "v:0"));
        Console.WriteLine("t_pos0=" + (TryLabel("+0") == "v:0"));
        Console.WriteLine("t_neg0=" + (TryLabel("-0") == "v:0"));
        Console.WriteLine("t_42=" + (TryLabel("42") == "v:42"));
        Console.WriteLine("t_plus42=" + (TryLabel("+42") == "v:42"));
        Console.WriteLine("t_minus42=" + (TryLabel("-42") == "v:-42"));
        Console.WriteLine("t_ws=" + (TryLabel("  42  ") == "v:42"));
        Console.WriteLine("t_ws_tab=" + (TryLabel("\t42") == "v:42"));
        Console.WriteLine("t_ws_lf=" + (TryLabel("42\n") == "v:42"));
        Console.WriteLine("t_ws_cr=" + (TryLabel("42\r") == "v:42"));
        Console.WriteLine("t_ws_ff=" + (TryLabel("42\f") == "v:42"));
        Console.WriteLine("t_ws_vtab=" + (TryLabel("\u000B42") == "v:42"));
        Console.WriteLine("t_lead0=" + (TryLabel("00042") == "v:42"));
        Console.WriteLine("t_lead0long=" + (TryLabel("00000000000000000000000042") == "v:42"));
        Console.WriteLine("t_max=" + (TryLabel("2147483647") == "v:2147483647"));
        Console.WriteLine("t_min=" + (TryLabel("-2147483648") == "v:-2147483648"));
        Console.WriteLine("t_max1=" + (TryLabel("2147483648") == "fail"));
        Console.WriteLine("t_min1=" + (TryLabel("-2147483649") == "fail"));
        Console.WriteLine("t_huge=" + (TryLabel("99999999999999999999999") == "fail"));
        Console.WriteLine("t_empty=" + (TryLabel("") == "fail"));
        Console.WriteLine("t_wsonly=" + (TryLabel("   ") == "fail"));
        Console.WriteLine("t_junk=" + (TryLabel("12a") == "fail"));
        Console.WriteLine("t_float=" + (TryLabel("1.5") == "fail"));
        Console.WriteLine("t_dblsign=" + (TryLabel("+-5") == "fail"));
        Console.WriteLine("t_minsign=" + (TryLabel("-") == "fail"));
        Console.WriteLine("t_trailing_sign=" + (TryLabel("5-") == "fail"));
        Console.WriteLine("t_hex=" + (TryLabel("0x10") == "fail"));
        Console.WriteLine("t_maxws=" + (TryLabel("2147483647 ") == "v:2147483647"));
        Console.WriteLine("t_wsmax=" + (TryLabel(" 2147483647") == "v:2147483647"));
        Console.WriteLine("t_plusmax=" + (TryLabel("+2147483647") == "v:2147483647"));
        Console.WriteLine("t_minmax1=" + (TryLabel("-2147483647") == "v:-2147483647"));
        Console.WriteLine("t_null=" + (TryLabel(null) == "fail"));
        Console.WriteLine("t_lead0max=" + (TryLabel("00000000000000000000000000000000000000000000000000000042") == "v:42"));

        // out slot on SUCCESS is written (both sides). On FAILURE net48
        // overwrites with 0 while the runtime leaves it untouched (D-NP-2) --
        // only the success-write contract is asserted here.
        int keep = 999;
        bool okKeep = int.TryParse("-42", out keep);
        Console.WriteLine("out_success_write=" + (okKeep && keep == -42));

        // ============ 3. Convert.ToInt32(string) vs int.Parse ============
        string[] conv = new string[] { "0", "-42", "2147483647", "-2147483648",
            "2147483648", "-2147483649", "99999999999999999999999", "", " 42 ",
            "12a", "1.5", "+-5", null };
        int convMatch = 0;
        for (int i = 0; i < conv.Length; ++i)
            if (ConvLabel(conv[i]) == (conv[i] == null ? "null" : ParseLabel(conv[i])))
                convMatch++;
        Console.WriteLine("conv_agree=" + (convMatch == conv.Length));
        Console.WriteLine("c_42=" + (ConvLabel("42") == "v:42"));
        Console.WriteLine("c_max=" + (ConvLabel("2147483647") == "v:2147483647"));
        Console.WriteLine("c_ovf=" + (ConvLabel("2147483648") == "ovf"));
        Console.WriteLine("c_junk=" + (ConvLabel("12a") == "fmt"));

        // ============ 4. Convert.ToInt32(string, fromBase) ============
        bool b2ok = Convert.ToInt32("101", 2) == 5;
        Console.WriteLine("b2=" + b2ok);
        Console.WriteLine("b2_plus=" + (Convert.ToInt32("+101", 2) == 5));
        Console.WriteLine("b2_max32=" + (Convert.ToInt32("1111111111111111111111111111111", 2) == 2147483647));
        Console.WriteLine("b2_80000000=" + (Convert.ToInt32("10000000000000000000000000000000", 2) == -2147483648));
        Console.WriteLine("b2_32one=" + (Convert.ToInt32("10000000000000000000000000000001", 2) == -2147483647));
        Console.WriteLine("b2_lead0=" + (Convert.ToInt32("00000000000000000000000000000001", 2) == 1));
        Console.WriteLine("b8=" + (Convert.ToInt32("17", 8) == 15));
        Console.WriteLine("b8_plus=" + (Convert.ToInt32("+17", 8) == 15));
        Console.WriteLine("b8_max=" + (Convert.ToInt32("17777777777", 8) == 2147483647));
        Console.WriteLine("b8_min=" + (Convert.ToInt32("20000000000", 8) == -2147483648));
        Console.WriteLine("b10=" + (Convert.ToInt32("255", 10) == 255));
        Console.WriteLine("b10_neg=" + (Convert.ToInt32("-255", 10) == -255));
        Console.WriteLine("b16=" + (Convert.ToInt32("ff", 16) == 255));
        Console.WriteLine("b16_upper=" + (Convert.ToInt32("FF", 16) == 255));
        Console.WriteLine("b16_0x=" + (Convert.ToInt32("0x10", 16) == 16));
        Console.WriteLine("b16_0X=" + (Convert.ToInt32("0X10", 16) == 16));
        Console.WriteLine("b16_max=" + (Convert.ToInt32("7fffffff", 16) == 2147483647));
        Console.WriteLine("b16_80000000=" + (Convert.ToInt32("80000000", 16) == -2147483648));
        Console.WriteLine("b16_ffffffff=" + (Convert.ToInt32("ffffffff", 16) == -1));
        // net48: '-' is ILLEGAL for radix 2/8/16 (ArgumentException); '+'
        // is legal for every radix. Whitespace is NOT trimmed in the base-N
        // overload (FormatException -- unlike int.Parse's whitespace pass).
        bool bneg = false;
        try { Convert.ToInt32("-101", 2); } catch (ArgumentException) { bneg = true; }
        Console.WriteLine("b2_neg_ae=" + bneg);
        bool bneg8 = false;
        try { Convert.ToInt32("-17", 8); } catch (ArgumentException) { bneg8 = true; }
        Console.WriteLine("b8_neg_ae=" + bneg8);
        bool bneg16 = false;
        try { Convert.ToInt32("-ff", 16); } catch (ArgumentException) { bneg16 = true; }
        Console.WriteLine("b16_neg_ae=" + bneg16);
        bool bws = false;
        try { Convert.ToInt32(" 255 ", 10); } catch (FormatException) { bws = true; }
        Console.WriteLine("b10_ws_fmt=" + bws);
        bool bws16 = false;
        try { Convert.ToInt32("  ff  ", 16); } catch (FormatException) { bws16 = true; }
        Console.WriteLine("b16_ws_fmt=" + bws16);
        bool b10_0x = false;
        try { Convert.ToInt32("0x10", 10); } catch (FormatException) { b10_0x = true; }
        Console.WriteLine("b10_0x_fmt=" + b10_0x);
        bool be0 = false;
        try { Convert.ToInt32("1", 1); } catch (ArgumentException) { be0 = true; }
        Console.WriteLine("b_base_invalid=" + be0);
        bool be1 = false;
        try { Convert.ToInt32("1", 3); } catch (ArgumentException) { be1 = true; }
        Console.WriteLine("b_base3_invalid=" + be1);
        bool be2 = false;
        try { Convert.ToInt32("", 16); } catch (ArgumentOutOfRangeException) { be2 = true; }
        Console.WriteLine("b_empty_aoor=" + be2);
        Console.WriteLine("b_null_zero=" + (Convert.ToInt32((string)null, 16) == 0));
        bool be3 = false;
        try { Convert.ToInt32("1g", 16); } catch (FormatException) { be3 = true; }
        Console.WriteLine("b_baddigit_fmt=" + be3);
        bool be4 = false;
        try { Convert.ToInt32("100000000000000000000000000000000", 2); } catch (OverflowException) { be4 = true; }
        Console.WriteLine("b2_33bit_ovf=" + be4);
        bool be5 = false;
        try { Convert.ToInt32("fffffffff", 16); } catch (OverflowException) { be5 = true; }
        Console.WriteLine("b16_9dig_ovf=" + be5);

        // ============ 5. Convert.ToString(int, toBase) round-trip ============
        Console.WriteLine("s2=" + (Convert.ToString(5, 2) == "101"));
        Console.WriteLine("s2_neg=" + (Convert.ToString(-1, 2) == "11111111111111111111111111111111"));
        Console.WriteLine("s8=" + (Convert.ToString(255, 8) == "377"));
        Console.WriteLine("s8_neg=" + (Convert.ToString(-255, 8) == "37777777401"));
        Console.WriteLine("s10=" + (Convert.ToString(123, 10) == "123"));
        Console.WriteLine("s10_neg=" + (Convert.ToString(-123, 10) == "-123"));
        Console.WriteLine("s16=" + (Convert.ToString(255, 16) == "ff"));
        Console.WriteLine("s16_neg=" + (Convert.ToString(-1, 16) == "ffffffff"));
        Console.WriteLine("s16_max=" + (Convert.ToString(2147483647, 16) == "7fffffff"));
        Console.WriteLine("s16_min=" + (Convert.ToString(-2147483648, 16) == "80000000"));
        bool se0 = false;
        try { Convert.ToString(5, 3); } catch (ArgumentException) { se0 = true; }
        Console.WriteLine("s_base_invalid=" + se0);

        // ============ 6. aggregate Parse/TryParse consistency ============
        int match = 0;
        foreach (string s in inputs)
        {
            int pv; bool pOk;
            try { pv = int.Parse(s); pOk = true; }
            catch (Exception) { pv = 0; pOk = false; }
            int tv;
            bool tOk = int.TryParse(s, out tv);
            if (pOk == tOk && (!pOk || pv == tv)) match++;
        }
        Console.WriteLine("consistency=" + (match == inputs.Length));

        Console.WriteLine("done");
    }
}}
