//------------------------------------------------------------------------------
// convertfull.cs -- Convert FULL-TYPE-MATRIX + EXCEPTION deep e2e reference
// case (net48 oracle, byte-for-byte). Created in e2e batch C74, AFTER the
// convert domain is fully wired (convertcase/convertdeep/convertbase/
// converted2 already cover base-radix + Base64 + encoding; THIS case drives
// the WIRED cross-conversion matrix and its boundary/exception lattice).
//
// Wired surface (MethodMap.Convert.cs -> convert.cpp / format.cpp), every
// probe stays within it:
//   ToInt32(string) / (long) / (double) / (char) / (string, base)
//   ToInt64(string)
//   ToDouble(string) / (int)
//   ToString(int) / (long) / (double) / (bool) / (char) / (int, base)
//   ToBoolean(int)     ToChar(int)
// (ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToSingle/ToDecimal are
// LOUDLY rejected by the transpiler -- MethodMap.ConvertExtra.cs -- and are
// deliberately NOT exercised.)
//
// net48-shape discipline (pinned in convert.cpp / earlier cases):
//   * ToInt32(double) rounds half-to-even FIRST (2.5->2, 3.5->4) then
//     range-checks the ROUNDED value against [INT32_MIN, 2^31); NaN and
//     +/-Infinity throw OverflowException.
//   * ToInt32(long) is a checked narrowing (2147483648L throws).
//   * ToChar(int) is checked over 0..0xFFFF (-1 and 0x10000 throw).
//   * ToDouble(string) grammar (zh-CN culture): ASCII whitespace, the
//     culture words "NaN"/"∞"/"-∞" (U+221E) or a decimal literal with
//     optional sign/fraction/eE exponent; overflow -> OverflowException,
//     underflow -> 0.0, and the ASCII words "Infinity"/"-Infinity", the
//     abbreviated forms "inf"/"+Infinity"/"nan" and hex floats are NOT legal
//     .NET text under the zh-CN NumberFormatInfo (FormatException).
//   * ToString(double) is the net48 G15 layout (%.15g with C-isms adapted;
//     -0.0 -> "0", NaN -> "NaN").
//   * Exceptions print TYPE NAME only (messages are OS-localized).
//
// Every line prints "label=True/False" or "label:<ExceptionTypeName>".
//------------------------------------------------------------------------------
using System;

class Program
{
    static void Main()
    {
        // ============ A. ToInt32(long): checked narrowing lattice ============
        Console.WriteLine("i8_0=" + (Convert.ToInt32(0L) == 0));
        Console.WriteLine("i8_1=" + (Convert.ToInt32(1L) == 1));
        Console.WriteLine("i8_neg1=" + (Convert.ToInt32(-1L) == -1));
        Console.WriteLine("i8_max=" + (Convert.ToInt32(2147483647L) == int.MaxValue));
        Console.WriteLine("i8_min=" + (Convert.ToInt32(-2147483648L) == int.MinValue));
        try { Convert.ToInt32(2147483648L); Console.WriteLine("i8_max1:ok"); }
        catch (Exception e) { Console.WriteLine("i8_max1:" + e.GetType().Name); }
        try { Convert.ToInt32(-2147483649L); Console.WriteLine("i8_min1:ok"); }
        catch (Exception e) { Console.WriteLine("i8_min1:" + e.GetType().Name); }
        try { Convert.ToInt32(long.MaxValue); Console.WriteLine("i8_lmax:ok"); }
        catch (Exception e) { Console.WriteLine("i8_lmax:" + e.GetType().Name); }
        try { Convert.ToInt32(long.MinValue); Console.WriteLine("i8_lmin:ok"); }
        catch (Exception e) { Console.WriteLine("i8_lmin:" + e.GetType().Name); }

        // ============ B. ToInt32(double): rounding + range lattice ============
        Console.WriteLine("d_0=" + (Convert.ToInt32(0.0) == 0));
        Console.WriteLine("d_05=" + (Convert.ToInt32(0.5) == 0));       // half-to-even -> 0
        Console.WriteLine("d_15=" + (Convert.ToInt32(1.5) == 2));       // half-to-even -> 2
        Console.WriteLine("d_neg05=" + (Convert.ToInt32(-0.5) == 0));
        Console.WriteLine("d_25=" + (Convert.ToInt32(2.5) == 2));       // even neighbor
        Console.WriteLine("d_35=" + (Convert.ToInt32(3.5) == 4));       // even neighbor
        Console.WriteLine("d_neg35=" + (Convert.ToInt32(-3.5) == -4));
        Console.WriteLine("d_0499=" + (Convert.ToInt32(0.4999) == 0));
        Console.WriteLine("d_299=" + (Convert.ToInt32(2.99) == 3));
        Console.WriteLine("d_neg299=" + (Convert.ToInt32(-2.99) == -3));
        Console.WriteLine("d_max_ok=" + (Convert.ToInt32(2147483647.0) == int.MaxValue));
        Console.WriteLine("d_min_ok=" + (Convert.ToInt32(-2147483648.0) == int.MinValue));
        Console.WriteLine("d_646=" + (Convert.ToInt32(2147483646.5) == 2147483646));
        try { Convert.ToInt32(2147483647.5); Console.WriteLine("d_6475:ok"); }
        catch (Exception e) { Console.WriteLine("d_6475:" + e.GetType().Name); } // rounds to 2^31
        try { Convert.ToInt32(2147483648.0); Console.WriteLine("d_2p31:ok"); }
        catch (Exception e) { Console.WriteLine("d_2p31:" + e.GetType().Name); }
        try { Convert.ToInt32(-2147483649.0); Console.WriteLine("d_neg2p31:ok"); }
        catch (Exception e) { Console.WriteLine("d_neg2p31:" + e.GetType().Name); }
        try { Convert.ToInt32(double.NaN); Console.WriteLine("d_nan:ok"); }
        catch (Exception e) { Console.WriteLine("d_nan:" + e.GetType().Name); }
        try { Convert.ToInt32(double.PositiveInfinity); Console.WriteLine("d_inf:ok"); }
        catch (Exception e) { Console.WriteLine("d_inf:" + e.GetType().Name); }
        try { Convert.ToInt32(double.NegativeInfinity); Console.WriteLine("d_ninf:ok"); }
        catch (Exception e) { Console.WriteLine("d_ninf:" + e.GetType().Name); }

        // ============ C. ToInt32(char) lattice ============
        Console.WriteLine("c_0=" + (Convert.ToInt32('\0') == 0));
        Console.WriteLine("c_A=" + (Convert.ToInt32('A') == 65));
        Console.WriteLine("c_max=" + (Convert.ToInt32(char.MaxValue) == 65535));
        Console.WriteLine("c_hi=" + (Convert.ToInt32('\u00FF') == 255));

        // ============ D. ToInt64(string): boundary lattice ============
        Console.WriteLine("l_0=" + (Convert.ToInt64("0") == 0L));
        Console.WriteLine("l_42=" + (Convert.ToInt64("42") == 42L));
        Console.WriteLine("l_neg42=" + (Convert.ToInt64("-42") == -42L));
        Console.WriteLine("l_ws=" + (Convert.ToInt64("  -42  ") == -42L));
        Console.WriteLine("l_plus=" + (Convert.ToInt64("+7") == 7L));
        Console.WriteLine("l_zero=" + (Convert.ToInt64("000000000000000000042") == 42L));
        Console.WriteLine("l_max=" + (Convert.ToInt64("9223372036854775807") == long.MaxValue));
        Console.WriteLine("l_min=" + (Convert.ToInt64("-9223372036854775808") == long.MinValue));
        Console.WriteLine("l_max_1=" + (Convert.ToInt64("9223372036854775806") == long.MaxValue - 1));
        Console.WriteLine("l_min_1=" + (Convert.ToInt64("-9223372036854775807") == long.MinValue + 1));
        try { Convert.ToInt64("9223372036854775808"); Console.WriteLine("l_ovf1:ok"); }
        catch (Exception e) { Console.WriteLine("l_ovf1:" + e.GetType().Name); }
        try { Convert.ToInt64("-9223372036854775809"); Console.WriteLine("l_ovf2:ok"); }
        catch (Exception e) { Console.WriteLine("l_ovf2:" + e.GetType().Name); }
        try { Convert.ToInt64("99999999999999999999"); Console.WriteLine("l_20d:ok"); }
        catch (Exception e) { Console.WriteLine("l_20d:" + e.GetType().Name); }
        try { Convert.ToInt64(""); Console.WriteLine("l_empty:ok"); }
        catch (Exception e) { Console.WriteLine("l_empty:" + e.GetType().Name); }
        try { Convert.ToInt64("12x"); Console.WriteLine("l_fmt:ok"); }
        catch (Exception e) { Console.WriteLine("l_fmt:" + e.GetType().Name); }
        Console.WriteLine("l_null=" + (Convert.ToInt64(null) == 0L));

        // ============ E. ToDouble(string): grammar + specials lattice ============
        Console.WriteLine("f_0=" + (Convert.ToDouble("0") == 0.0));
        Console.WriteLine("f_05=" + (Convert.ToDouble("0.5") == 0.5));
        Console.WriteLine("f_5=" + (Convert.ToDouble("5.") == 5.0));
        Console.WriteLine("f_dot5=" + (Convert.ToDouble(".5") == 0.5));
        Console.WriteLine("f_neg=" + (Convert.ToDouble("-1.25e2") == -125.0));
        Console.WriteLine("f_e=" + (Convert.ToDouble("1e3") == 1000.0));
        Console.WriteLine("f_E=" + (Convert.ToDouble("1E+3") == 1000.0));
        Console.WriteLine("f_e2=" + (Convert.ToDouble("5e-2") == 0.05));
        Console.WriteLine("f_ws=" + (Convert.ToDouble("  3.5  ") == 3.5));
        Console.WriteLine("f_tab=" + (Convert.ToDouble("\t4.5\n") == 4.5));
        Console.WriteLine("f_int=" + (Convert.ToDouble("42") == 42.0));
        Console.WriteLine("f_neg0=" + (Convert.ToDouble("-0.0") == 0.0));
        Console.WriteLine("f_nan=" + double.IsNaN(Convert.ToDouble("NaN")));
        // zh-CN culture infinity words (net48 oracle, D-CF-1 now aligned):
        // U+221E / "-" + U+221E parse to +/-Infinity; the ASCII
        // "Infinity"/"-Infinity" words and "+∞" throw FormatException.
        Console.WriteLine("f_inf=" + double.IsPositiveInfinity(Convert.ToDouble("\u221E")));
        Console.WriteLine("f_ninf=" + double.IsNegativeInfinity(Convert.ToDouble("-\u221E")));
        Console.WriteLine("f_ws_inf=" + double.IsPositiveInfinity(Convert.ToDouble("  \u221E  ")));
        try { Convert.ToDouble("Infinity"); Console.WriteLine("f_ascii_inf:ok"); }
        catch (Exception e) { Console.WriteLine("f_ascii_inf:" + e.GetType().Name); }
        try { Convert.ToDouble("-Infinity"); Console.WriteLine("f_ascii_ninf:ok"); }
        catch (Exception e) { Console.WriteLine("f_ascii_ninf:" + e.GetType().Name); }
        try { Convert.ToDouble("+\u221E"); Console.WriteLine("f_plus_inf:ok"); }
        catch (Exception e) { Console.WriteLine("f_plus_inf:" + e.GetType().Name); }
        Console.WriteLine("f_under=" + (Convert.ToDouble("1e-999") == 0.0));
        try { Convert.ToDouble("1e999"); Console.WriteLine("f_ovf:ok"); }
        catch (Exception e) { Console.WriteLine("f_ovf:" + e.GetType().Name); }
        try { Convert.ToDouble("1e"); Console.WriteLine("f_eonly:ok"); }
        catch (Exception e) { Console.WriteLine("f_eonly:" + e.GetType().Name); }
        try { Convert.ToDouble("inf"); Console.WriteLine("f_inf_abbrev:ok"); }
        catch (Exception e) { Console.WriteLine("f_inf_abbrev:" + e.GetType().Name); }
        try { Convert.ToDouble("0x1p3"); Console.WriteLine("f_hex:ok"); }
        catch (Exception e) { Console.WriteLine("f_hex:" + e.GetType().Name); }
        try { Convert.ToDouble("1.2.3"); Console.WriteLine("f_multi:ok"); }
        catch (Exception e) { Console.WriteLine("f_multi:" + e.GetType().Name); }
        try { Convert.ToDouble(""); Console.WriteLine("f_empty:ok"); }
        catch (Exception e) { Console.WriteLine("f_empty:" + e.GetType().Name); }
        try { Convert.ToDouble(" "); Console.WriteLine("f_ws_only:ok"); }
        catch (Exception e) { Console.WriteLine("f_ws_only:" + e.GetType().Name); }

        // ============ F. ToDouble(int) lattice ============
        Console.WriteLine("fi_0=" + (Convert.ToDouble(0) == 0.0));
        Console.WriteLine("fi_42=" + (Convert.ToDouble(42) == 42.0));
        Console.WriteLine("fi_neg=" + (Convert.ToDouble(-7) == -7.0));
        Console.WriteLine("fi_max=" + (Convert.ToDouble(int.MaxValue) == 2147483647.0));
        Console.WriteLine("fi_min=" + (Convert.ToDouble(int.MinValue) == -2147483648.0));

        // ============ G. ToString(double): G15 lattice ============
        Console.WriteLine("ts_0=" + (Convert.ToString(0.0) == "0"));
        Console.WriteLine("ts_neg0=" + (Convert.ToString(-0.0) == "0"));
        Console.WriteLine("ts_05=" + (Convert.ToString(0.5) == "0.5"));
        Console.WriteLine("ts_15=" + (Convert.ToString(1.5) == "1.5"));
        Console.WriteLine("ts_neg25=" + (Convert.ToString(-2.5) == "-2.5"));
        Console.WriteLine("ts_01=" + (Convert.ToString(0.1) == "0.1"));
        Console.WriteLine("ts_100=" + (Convert.ToString(100.0) == "100"));
        Console.WriteLine("ts_1e10=" + (Convert.ToString(1e10) == "10000000000"));
        Console.WriteLine("ts_1e15=" + (Convert.ToString(1e15) == "1E+15"));
        Console.WriteLine("ts_1e_5=" + (Convert.ToString(1e-5) == "1E-05"));
        Console.WriteLine("ts_123=" + (Convert.ToString(123.456) == "123.456"));
        Console.WriteLine("ts_nan=" + (Convert.ToString(double.NaN) == "NaN"));

        // ============ H. cross round-trips (string <-> number) ============
        Console.WriteLine("rt_i32=" + (Convert.ToInt32(Convert.ToString(123456789)) == 123456789));
        Console.WriteLine("rt_i32m=" + (Convert.ToInt32(Convert.ToString(int.MinValue)) == int.MinValue));
        Console.WriteLine("rt_i64=" + (Convert.ToInt64(Convert.ToString(long.MaxValue)) == long.MaxValue));
        Console.WriteLine("rt_d=" + (Convert.ToDouble(Convert.ToString(3.5)) == 3.5));
        Console.WriteLine("rt_d0=" + (Convert.ToString(Convert.ToDouble("42")) == "42"));
        Console.WriteLine("rt_neg=" + (Convert.ToDouble(Convert.ToString(-2.5)) == -2.5));

        // ============ I. ToBoolean / ToChar lattices ============
        Console.WriteLine("b_0=" + (Convert.ToBoolean(0) == false));
        Console.WriteLine("b_1=" + (Convert.ToBoolean(1) == true));
        Console.WriteLine("b_neg1=" + (Convert.ToBoolean(-1) == true));
        Console.WriteLine("b_max=" + (Convert.ToBoolean(int.MaxValue) == true));
        Console.WriteLine("b_min=" + (Convert.ToBoolean(int.MinValue) == true));
        Console.WriteLine("ch_0=" + (Convert.ToChar(0) == '\0'));
        Console.WriteLine("ch_A=" + (Convert.ToChar(65) == 'A'));
        Console.WriteLine("ch_max=" + (Convert.ToChar(65535) == char.MaxValue));
        try { Convert.ToChar(-1); Console.WriteLine("ch_neg1:ok"); }
        catch (Exception e) { Console.WriteLine("ch_neg1:" + e.GetType().Name); }
        try { Convert.ToChar(0x10000); Console.WriteLine("ch_10000:ok"); }
        catch (Exception e) { Console.WriteLine("ch_10000:" + e.GetType().Name); }

        // ============ J. ToString int/long/bool/char family ============
        Console.WriteLine("si_0=" + (Convert.ToString(0) == "0"));
        Console.WriteLine("si_42=" + (Convert.ToString(42) == "42"));
        Console.WriteLine("si_neg42=" + (Convert.ToString(-42) == "-42"));
        Console.WriteLine("sl_max=" + (Convert.ToString(long.MaxValue) == "9223372036854775807"));
        Console.WriteLine("sl_min=" + (Convert.ToString(long.MinValue) == "-9223372036854775808"));
        Console.WriteLine("sb_t=" + (Convert.ToString(true) == "True"));
        Console.WriteLine("sb_f=" + (Convert.ToString(false) == "False"));
        Console.WriteLine("sc_A=" + (Convert.ToString('A') == "A"));

        // ============ K. ToInt32(string, base): shared-reject radix lattice ============
        // net48 AND the runtime both reject radices outside {2,8,10,16}
        // (convert.cpp IsValidRadix == the net48 set), so 0/1/3/37/100 are
        // SHARED ArgumentException probes (byte-exact).
        try { Convert.ToInt32("10", 3); Console.WriteLine("b3:ok"); }
        catch (Exception e) { Console.WriteLine("b3:" + e.GetType().Name); }
        try { Convert.ToInt32("10", 36); Console.WriteLine("b36:ok"); }
        catch (Exception e) { Console.WriteLine("b36:" + e.GetType().Name); }
        try { Convert.ToInt32("10", 100); Console.WriteLine("b100:ok"); }
        catch (Exception e) { Console.WriteLine("b100:" + e.GetType().Name); }
        try { Convert.ToInt32("10", 1); Console.WriteLine("b1:ok"); }
        catch (Exception e) { Console.WriteLine("b1:" + e.GetType().Name); }
        Console.WriteLine("b16_ok=" + (Convert.ToInt32("ff", 16) == 255));

        // ============ L. ToString(int, base): shared-reject lattice ============
        try { Convert.ToString(42, 1); Console.WriteLine("tb1:ok"); }
        catch (Exception e) { Console.WriteLine("tb1:" + e.GetType().Name); }
        try { Convert.ToString(42, 37); Console.WriteLine("tb37:ok"); }
        catch (Exception e) { Console.WriteLine("tb37:" + e.GetType().Name); }
        Console.WriteLine("tb16=" + (Convert.ToString(255, 16) == "ff"));
    }
}
