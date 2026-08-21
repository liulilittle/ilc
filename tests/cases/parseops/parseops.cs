//------------------------------------------------------------------------------
// parseops.cs -- end-to-end reference case for the System numeric-parsing
// family (net48 oracle). Exercises the wired surface (MethodMap.ValueType.cs
// -> il2cpp_parse.h TryParse family + il2cpp_format.h throwing Parse family):
//
// Coverage (active, all wired):
//   * int.Parse(string): valid forms (sign / '+' / whitespace / leading zeros
//     / int32 min-max) plus the throwing family (FormatException / j
//     OverflowException) captured per input.
//   * int.TryParse(string, out int): the SAME input matrix -> success flag +
//     value, printed pair-wise with Parse so the Parse/TryParse CONSISTENCY is
//     visible line-by-line, plus an aggregate consistency count over all
//     inputs (Parse-ok == TryParse-ok and equal values on success).
//   * long.TryParse(string, out long): MaxValue / MinValue valid, overflow
//     and malformed invalid.
//   * double.Parse(string): CULTURE-INDEPENDENT decimal forms ('.' separator,
//     sign, e/E exponent -- stable on zh-CN and the invariant runtime),
//     FormatException and OverflowException capture.
//   * double.TryParse(string, out double): NaN, decimals, invalid forms.
//
// ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed; see GAPS.md):
//   * int.Parse(null) / double.Parse(null) -- the runtime Parse family throws
//     FormatException for a null text (il2cpp_format.h:321/395) where net48
//     throws ArgumentNullException; the null inputs below are therefore
//     exercised through TryParse only (returns false on both).
//   * int.Parse 各进制 Convert.ToInt32(string, fromBase) -- LOUD rejection
//     (MethodMap.Convert.cs:37); the runtime has only the no-throw
//     icall_try_parse_int32_base. GAPS.md D-PAR-1.
//   * long.Parse -- LOUD rejection (MethodMap.ValueType.cs:65, no int64 Parse
//     icall). GAPS.md D-PAR-2.
//   * int.TryParse(string, NumberStyles, IFormatProvider, out int) 4-arg
//     overload -- falls through to the generated-name path (undefined extern).
//     GAPS.md D-PAR-3.
//   * double "Infinity"/"-Infinity" words -- zh-CN net48 rejects the ASCII
//     spelling (U+221E symbol) while the runtime accepts it (documented
//     InvariantCulture simplification); NOT probed.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ParseOps {
    // int.Parse label: v:<value> / fmt / ovf / an (FormatException /
    // OverflowException / ArgumentNullException). null text is NOT passed
    // here (net48 ArgumentNullException vs runtime FormatException -- the
    // divergence is documented in the header).
    static string ParseLabel(string s)
    {
        try { return "v:" + int.Parse(s); }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
    }

    // int.TryParse label: v:<value> on success, "fail" on failure.
    static string TryLabel(string s)
    {
        int v;
        return int.TryParse(s, out v) ? ("v:" + v) : "fail";
    }

    // double.Parse label: v:<value> / fmt / ovf.
    static string DoubleLabel(string s)
    {
        try { return "v:" + double.Parse(s); }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
    }

    public static void Main()
    {
        // ---- int.Parse / int.TryParse pair matrix (consistency visible) ----
        Console.WriteLine("pair_plain=" + ParseLabel("42") + "/" + TryLabel("42"));
        Console.WriteLine("pair_neg=" + ParseLabel("-42") + "/" + TryLabel("-42"));
        Console.WriteLine("pair_plus=" + ParseLabel("+42") + "/" + TryLabel("+42"));
        Console.WriteLine("pair_space=" + ParseLabel("  42  ") + "/" + TryLabel("  42  "));
        Console.WriteLine("pair_zero=" + ParseLabel("00042") + "/" + TryLabel("00042"));
        Console.WriteLine("pair_max=" + ParseLabel("2147483647") + "/" + TryLabel("2147483647"));
        Console.WriteLine("pair_min=" + ParseLabel("-2147483648") + "/" + TryLabel("-2147483648"));
        Console.WriteLine("pair_empty=" + ParseLabel("") + "/" + TryLabel(""));
        Console.WriteLine("pair_ws=" + ParseLabel("   ") + "/" + TryLabel("   "));
        Console.WriteLine("pair_junk=" + ParseLabel("12a") + "/" + TryLabel("12a"));
        Console.WriteLine("pair_sign=" + ParseLabel("-") + "/" + TryLabel("-"));
        Console.WriteLine("pair_ovf=" + ParseLabel("2147483648") + "/" + TryLabel("2147483648"));
        Console.WriteLine("pair_ovf_neg=" + ParseLabel("-2147483649") + "/" + TryLabel("-2147483649"));
        Console.WriteLine("pair_null=" + TryLabel(null));

        // ---- aggregate Parse/TryParse consistency over all 14 inputs ----
        string[] inputs = new string[] {
            "42", "-42", "+42", "  42  ", "00042", "2147483647", "-2147483648",
            "", "   ", "12a", "-", "2147483648", "-2147483649", null
        };
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
        Console.WriteLine("consistency=" + match + "/" + inputs.Length);

        // ---- long.TryParse: boundaries + invalid ----
        long l;
        Console.WriteLine("ltp_max=" + (long.TryParse("9223372036854775807", out l) && l == long.MaxValue));
        Console.WriteLine("ltp_min=" + (long.TryParse("-9223372036854775808", out l) && l == long.MinValue));
        Console.WriteLine("ltp_ovf=" + (long.TryParse("99999999999999999999", out l) == false));
        Console.WriteLine("ltp_junk=" + (long.TryParse("abc", out l) == false));

        // ---- double.Parse: culture-stable decimal forms ----
        Console.WriteLine("dp_frac=" + DoubleLabel("3.25"));
        Console.WriteLine("dp_neg=" + DoubleLabel("-0.5"));
        Console.WriteLine("dp_space=" + DoubleLabel("  7.5  "));
        Console.WriteLine("dp_int=" + DoubleLabel("42"));
        Console.WriteLine("dp_exp=" + DoubleLabel("1e2"));
        Console.WriteLine("dp_nexp=" + DoubleLabel("1.5e-3"));
        Console.WriteLine("dp_ovf=" + DoubleLabel("1e999"));
        Console.WriteLine("dp_fmt=" + DoubleLabel("1.5x"));
        Console.WriteLine("dp_ws=" + DoubleLabel("   "));

        // ---- double.TryParse: NaN + decimals + invalid ----
        double d;
        Console.WriteLine("dtp_nan=" + (double.TryParse("NaN", out d) && d.ToString() == "NaN"));
        Console.WriteLine("dtp_frac=" + (double.TryParse(" -2.25 ", out d) && d == -2.25));
        Console.WriteLine("dtp_int=" + (double.TryParse("42", out d) && d == 42.0));
        Console.WriteLine("dtp_exp=" + (double.TryParse("1.5e-3", out d) && d == 0.0015));
        Console.WriteLine("dtp_fail=" + (double.TryParse("abc", out d) == false));
        Console.WriteLine("dtp_empty=" + (double.TryParse("", out d) == false));
    }
}}
