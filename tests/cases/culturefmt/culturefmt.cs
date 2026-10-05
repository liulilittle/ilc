//------------------------------------------------------------------------------
// culturefmt.cs -- zh-CN CULTURE FORMAT FULL-MATRIX e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C58, AFTER the
// culturedeep (C42) / numfmtdeep (C20) format families are PASSing. THIS case
// drives the FULL CURRENCY / PERCENT / NUMBER matrices across int32 / int64 /
// double -- the value spaces numfmtdeep only sampled -- plus the G/F/E
// complement, all against the zh-CN fixed-culture runtime surface
// (formatext.cpp, the 15-significant-digit round-half-away base).
//
// Verified zh-CN net48 facts pinned here (probed, not assumed):
//   * CurrencyNegativePattern == 2 -> the SIGN follows the symbol:
//     "-1234.5" C2 == "\u00A5-1,234.50" (NOT "-¥1,234.50").
//   * CurrencyPositivePattern == 0 -> "\u00A5<value>".
//   * PercentPositivePattern == 1 / PercentNegativePattern == 1 -> "n%"
//     / "-n%" (no space, sign before the value).
//   * CurrencySymbol == "\u00A5", PercentSymbol == "%".
//   * Formatting uses round-HALF-AWAY-FROM-ZERO: (2.5).F0 == "3",
//     (1234.5).C0 == "\u00A51,235", (0.005).P0 == "1%", (2.675).F2 ==
//     "2.68" (the .NET Framework 15-digit CRT base).
//   * Infinity/NaN render as "\u221E" / "-" + "\u221E" / "NaN" for EVERY
//     format (F/N/C/P/G/E).
//
// Every line prints "label=True/False" against an ASCII literal (the \u00A5 /
// \u221E escapes keep the source ASCII -- no encoding pitfalls). net48 is the
// reference baseline the transpiled build must match byte for byte.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class CultureFmt
    {
        public static void Main()
        {
            //===== 1. CURRENCY C -- full matrix (int32 / int64 / double) =====
            Console.WriteLine("c_pos2=" + ((1234.5).ToString("C2") == "\u00A51,234.50"));
            Console.WriteLine("c_pos0=" + ((0).ToString("C2") == "\u00A50.00"));
            Console.WriteLine("c_zero3=" + ((0).ToString("C3") == "\u00A50.000"));
            Console.WriteLine("c_neg_int=" + ((-1234).ToString("C2") == "\u00A5-1,234.00"));
            Console.WriteLine("c_neg_dbl=" + ((-1234.5).ToString("C2") == "\u00A5-1,234.50"));
            Console.WriteLine("c_neg0=" + ((-1234.5).ToString("C0") == "\u00A5-1,235"));
            Console.WriteLine("c_neg_c4=" + ((-0.0012).ToString("C4") == "\u00A5-0.0012"));
            Console.WriteLine("c_neg_int0=" + ((-123456).ToString("C0") == "\u00A5-123,456"));
            Console.WriteLine("c_half_up=" + ((1234.5).ToString("C0") == "\u00A51,235"));
            Console.WriteLine("c_round=" + ((1234.567).ToString("C2") == "\u00A51,234.57"));
            Console.WriteLine("c_round995=" + ((1234.995).ToString("C2") == "\u00A51,235.00"));
            Console.WriteLine("c_group999=" + ((999).ToString("C2") == "\u00A5999.00"));
            Console.WriteLine("c_group1000=" + ((1000).ToString("C2") == "\u00A51,000.00"));
            Console.WriteLine("c_group999k=" + ((999999).ToString("C2") == "\u00A5999,999.00"));
            Console.WriteLine("c_group1m=" + ((1000000).ToString("C2") == "\u00A51,000,000.00"));
            Console.WriteLine("c_group_round=" + ((999999.995).ToString("C2") == "\u00A51,000,000.00"));
            Console.WriteLine("c_neg_group_round=" + ((-999999.995).ToString("C2") == "\u00A5-1,000,000.00"));
            Console.WriteLine("c_intmax=" + ((2147483647).ToString("C0") == "\u00A52,147,483,647"));
            Console.WriteLine("c_intmin=" + ((-2147483648).ToString("C0") == "\u00A5-2,147,483,648"));
            Console.WriteLine("c_long=" + ((123456789012345L).ToString("C2") == "\u00A5123,456,789,012,345.00"));
            Console.WriteLine("c_special_inf=" + ((double.PositiveInfinity).ToString("C2") == "\u221E"));
            Console.WriteLine("c_special_ninf=" + ((double.NegativeInfinity).ToString("C2") == "-\u221E"));
            Console.WriteLine("c_special_nan=" + ((double.NaN).ToString("C2") == "NaN"));

            //===== 2. PERCENT P -- full matrix (double / int) =====
            Console.WriteLine("p_half=" + ((0.5).ToString("P0") == "50%"));
            Console.WriteLine("p_onehalf=" + ((1.5).ToString("P0") == "150%"));
            Console.WriteLine("p_quarter1=" + ((0.25).ToString("P1") == "25.0%"));
            Console.WriteLine("p_2=" + ((0.1234).ToString("P2") == "12.34%"));
            Console.WriteLine("p_neg=" + ((-0.1234).ToString("P2") == "-12.34%"));
            Console.WriteLine("p_neg1=" + ((-0.1234).ToString("P1") == "-12.3%"));
            Console.WriteLine("p_round_up=" + ((0.005).ToString("P0") == "1%"));
            Console.WriteLine("p_neg_round=" + ((-0.005).ToString("P0") == "-1%"));
            Console.WriteLine("p_round2=" + ((0.12345).ToString("P2") == "12.35%"));
            Console.WriteLine("p_int_scale=" + ((12345).ToString("P0") == "1,234,500%"));
            Console.WriteLine("p_int2=" + ((123).ToString("P2") == "12,300.00%"));
            Console.WriteLine("p_int_zero=" + ((0).ToString("P2") == "0.00%"));
            Console.WriteLine("p_neg_scale=" + ((-12345).ToString("P0") == "-1,234,500%"));
            Console.WriteLine("p_big=" + ((999999).ToString("P0") == "99,999,900%"));
            Console.WriteLine("p_one1=" + ((1).ToString("P1") == "100.0%"));
            Console.WriteLine("p_negone1=" + ((-1).ToString("P1") == "-100.0%"));
            Console.WriteLine("p_dbl_big=" + ((1234567.89).ToString("P2") == "123,456,789.00%"));

            //===== 3. NUMBER N -- full matrix =====
            Console.WriteLine("n_zero=" + ((0).ToString("N2") == "0.00"));
            Console.WriteLine("n_3=" + ((1234567).ToString("N3") == "1,234,567.000"));
            Console.WriteLine("n_neg0=" + ((-1234567).ToString("N0") == "-1,234,567"));
            Console.WriteLine("n_round=" + ((999999.999).ToString("N2") == "1,000,000.00"));
            Console.WriteLine("n_negmax=" + ((-2147483647).ToString("N2") == "-2,147,483,647.00"));
            Console.WriteLine("n_longmax=" + ((9223372036854775806L).ToString("N0") == "9,223,372,036,854,775,806"));
            Console.WriteLine("n_half=" + ((2.5).ToString("N0") == "3"));
            Console.WriteLine("n_half_neg=" + ((-2.5).ToString("N0") == "-3"));
            Console.WriteLine("n_big1=" + ((9876543210.5).ToString("N1") == "9,876,543,210.5"));
            Console.WriteLine("n_tiny=" + ((0.001).ToString("N2") == "0.00"));
            Console.WriteLine("n_1m1=" + ((1000000).ToString("N1") == "1,000,000.0"));
            Console.WriteLine("n_999m=" + ((999999999).ToString("N0") == "999,999,999"));
            Console.WriteLine("n_negbig=" + ((-999999999.9).ToString("N0") == "-1,000,000,000"));

            //===== 4. G / F / E complement (round-half-away + 15-digit base) =====
            Console.WriteLine("f_half=" + ((2.5).ToString("F0") == "3"));
            Console.WriteLine("f_half3=" + ((3.5).ToString("F0") == "4"));
            Console.WriteLine("f_half0=" + ((0.5).ToString("F0") == "1"));
            Console.WriteLine("f_neg1=" + ((-1234.56).ToString("F1") == "-1234.6"));
            Console.WriteLine("f_neg0=" + ((-1234.56).ToString("F0") == "-1235"));
            Console.WriteLine("f_2=" + ((1234.5).ToString("F2") == "1234.50"));
            Console.WriteLine("f_2_675=" + ((2.675).ToString("F2") == "2.68"));
            Console.WriteLine("g_default=" + ((1234.5).ToString("G") == "1234.5"));
            Console.WriteLine("g_9=" + ((123456789.123456789).ToString("G9") == "123456789"));
            Console.WriteLine("g_4=" + ((12345.0).ToString("G4") == "1.235E+04"));
            Console.WriteLine("g_1e20=" + ((1e20).ToString("G") == "1E+20"));
            Console.WriteLine("g_small_fixed=" + ((0.0001).ToString("G") == "0.0001"));
            Console.WriteLine("g_small_sci=" + ((0.00005).ToString("G") == "5E-05"));
            Console.WriteLine("g_pos0=" + ((0.00001).ToString("G") == "1E-05"));
            Console.WriteLine("g_neg0=" + ((-0.00001).ToString("G") == "-1E-05"));
            Console.WriteLine("e_0=" + ((1234.5).ToString("E0") == "1E+003"));
            Console.WriteLine("e_6=" + ((0.000012345).ToString("E6") == "1.234500E-005"));
            Console.WriteLine("e_neg3=" + ((-0.00123).ToString("E3") == "-1.230E-003"));

            //===== 5. culture literal pins (zh-CN identity through formats) =====
            Console.WriteLine("pct_sym=" + (NumberFormatInfo.CurrentInfo.PercentSymbol == "%"));
            Console.WriteLine("cur_sym=" + (NumberFormatInfo.CurrentInfo.CurrencySymbol == "\u00A5"));
            Console.WriteLine("cur_in_fmt=" + ((1234.5).ToString("C2").Substring(0, 1) == "\u00A5"));
            Console.WriteLine("pct_in_fmt=" + ((0.5).ToString("P0").Substring(2, 1) == "%"));
            Console.WriteLine("inf_utf8=" + (double.PositiveInfinity.ToString("N2") == "\u221E"));
            Console.WriteLine("ninf_utf8=" + (double.NegativeInfinity.ToString("E1") == "-\u221E"));
            Console.WriteLine("nan_utf8=" + (double.NaN.ToString("F1") == "NaN"));
        }
    }
}
