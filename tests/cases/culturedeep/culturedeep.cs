//------------------------------------------------------------------------------
// culturedeep.cs -- Culture-domain DEEP e2e case (batch C42): the zh-CN
// number / date FORMAT FAMILY, aligned to the culture-pinned runtime
// surface (cef2bdd culture alignment). Every probe is net48-oracle ==
// transpiled-runtime byte-exact:
//
//   1) Culture trivia folds (MethodMap.CultureInfo.cs / NumberFormat.cs):
//      CurrentCulture non-null; NumberFormatInfo separator / percent /
//      sign getters fold to the invariant-culture literals that ALSO match
//      the zh-CN host oracle (NumberDecimalSeparator ".", NumberGroupSeparator
//      ",", PercentSymbol "%", PercentDecimalSeparator ".", PercentGroupSeparator
//      ",", PositiveSign "+", NegativeSign "-").
//   2) Numeric format family via icall_format_int32/int64/double_custom
//      (formatext.cpp, zh-CN-pinned): D / N / X / E / G / F / P on
//      int32/int64/double + currency C asserted as bool (U+00A5 literal via
//      the \u escape keeps the .cs source ASCII -- no encoding pitfalls).
//   3) DateTime format family via icall_datetime_to_string_format
//      (il2cpp_time_ext.h, en-US/invariant subset) with CultureInfo
//      InvariantCulture passed EXPLICITLY (the provider arg is dropped at
//      emission, the runtime fixes invariant output -- the timeext
//      precedent): standard d/D/t/T/f/F/g/G/m/y/s + custom token combos.
//
// DELIBERATELY AVOIDED (would diverge or abort -- documented, not probed):
//   * CultureInfo.InvariantCulture.Name -- the transpiler folds
//     get_InvariantCulture to null (no culture object model); a .Name on
//     null would be a NullReferenceException (net48: "").
//   * NumberFormatInfo.PositiveInfinitySymbol / NegativeInfinitySymbol /
//     CurrencySymbol -- the InfinitySymbol getters fold at COMPILE time to
//     the ASCII "Infinity"/"-Infinity" (MethodMap.NumberFormat.cs, read-only
//     transpiler domain) and cannot diverge per host culture; the runtime
//     zh-CN U+221E symbol is instead probed through the double
//     ToString/ToString(format) surface (section 4). CurrencySymbol is
//     probed through ToString("C") instead.
//   * TextInfo.ToLower/ToUpper -- wired in MethodMap.NumberFormat.cs but
//     no culture object is reachable to produce a TextInfo receiver
//     (get_TextInfo is unwired; CurrentCulture folds to a string literal).
//   * DateTime standard specifiers outside the documented subset ("M"/"Y"/
//     "u"/"R"/"O" single letters -> FormatException divergence).
//   * DateTime.ToString(format) WITHOUT an explicit provider -- zh-CN
//     standard patterns ("yyyy/M/d"...) differ from the runtime's invariant.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class CultureDeep {
    public static void Main() {
        //===== 1. culture trivia (ASCII; current-culture IDENTITY is now the
        //          OS locale -- C42 fix -- while the format folds stay
        //          invariant/zh-CN-aligned) =====
        Console.WriteLine(CultureInfo.CurrentCulture != null);            // True
        // C42 fold fix (2026-08-11): CurrentCulture.Name is no longer the
        // fixed "en-US" literal -- get_CurrentCulture maps to the runtime
        // icall il2cpp_culture_get_current (cultureinfo.cpp reads the OS
        // user locale). On the zh-CN verification host the net48 oracle
        // prints "zh-CN", and the transpiled runtime now matches it.
        Console.WriteLine(CultureInfo.CurrentCulture.Name);               // zh-CN
        Console.WriteLine(CultureInfo.CurrentCulture.Name == "zh-CN");    // True
        Console.WriteLine(CultureInfo.CurrentCulture.Name != null);       // True
        Console.WriteLine(CultureInfo.CurrentCulture.Name.Length > 0);    // True
        Console.WriteLine(NumberFormatInfo.CurrentInfo.NumberDecimalSeparator == ".");  // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.NumberGroupSeparator == ",");    // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.PercentSymbol == "%");           // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.PercentDecimalSeparator == "."); // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.PercentGroupSeparator == ",");   // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.PositiveSign == "+");            // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.NegativeSign == "-");            // True (zh-CN)
        Console.WriteLine(NumberFormatInfo.InvariantInfo.NumberDecimalSeparator == "."); // True
        Console.WriteLine(NumberFormatInfo.InvariantInfo.NumberGroupSeparator == ",");   // True
        Console.WriteLine(NumberFormatInfo.InvariantInfo.NaNSymbol == "NaN");            // True

        //===== 2. numeric format family (zh-CN format_ext surface) =====
        // ---- int32: D / X / N ----
        Console.WriteLine((0).ToString("D"));                              // 0
        Console.WriteLine((42).ToString("D3"));                            // 042
        Console.WriteLine((255).ToString("X"));                            // FF
        Console.WriteLine((255).ToString("X4"));                           // 00FF
        Console.WriteLine((-1).ToString("X"));                             // FFFFFFFF
        Console.WriteLine((-42).ToString("X"));                            // FFFFFFD6
        Console.WriteLine((1234567).ToString("N0"));                       // 1,234,567
        Console.WriteLine((1234567).ToString("N2"));                       // 1,234,567.00
        Console.WriteLine((-1234567).ToString("N0"));                      // -1,234,567
        // ---- int64: D / X / N / E ----
        Console.WriteLine((0L).ToString("D"));                             // 0
        Console.WriteLine((-1L).ToString("X"));                            // FFFFFFFFFFFFFFFF
        Console.WriteLine((1234567890123456789L).ToString("X"));           // 112210F47DE98115
        Console.WriteLine((1234567890123L).ToString("N0"));                // 1,234,567,890,123
        Console.WriteLine((1234567890123L).ToString("E2"));                // 1.23E+012
        // ---- double: G / F / N / E / P ----
        Console.WriteLine((1234.5).ToString("G"));                         // 1234.5
        Console.WriteLine((1234.5).ToString("F2"));                        // 1234.50
        Console.WriteLine((1234567.89).ToString("N2"));                    // 1,234,567.89
        Console.WriteLine((0.00123).ToString("E2"));                       // 1.23E-003
        Console.WriteLine((-0.00123).ToString("E2"));                      // -1.23E-003
        Console.WriteLine((1234.5).ToString("E2"));                        // 1.23E+003
        Console.WriteLine((0.5).ToString("P0"));                           // 50%  (zh-CN "n%" layout)
        Console.WriteLine((1.5).ToString("P0"));                           // 150%
        Console.WriteLine((0.25).ToString("P1"));                          // 25.0%
        // ---- currency C (bool vs U+00A5 literal; ASCII output only) ----
        Console.WriteLine((1234.5).ToString("C2") == "\u00A51,234.50");    // True (zh-CN ¥)
        Console.WriteLine((0).ToString("C2") == "\u00A50.00");             // True
        Console.WriteLine((1234).ToString("C0") == "\u00A51,234");         // True
        Console.WriteLine((1234567.89).ToString("C2") == "\u00A51,234,567.89"); // True

        //===== 3. DateTime format family (explicit InvariantCulture -> runtime
        //          en-US/invariant subset) =====
        DateTime dt = new DateTime(2020, 6, 15, 13, 45, 30);
        Console.WriteLine(dt.ToString("d", CultureInfo.InvariantCulture));   // 06/15/2020
        Console.WriteLine(dt.ToString("D", CultureInfo.InvariantCulture));   // Monday, 15 June 2020
        Console.WriteLine(dt.ToString("t", CultureInfo.InvariantCulture));   // 13:45
        Console.WriteLine(dt.ToString("T", CultureInfo.InvariantCulture));   // 13:45:30
        Console.WriteLine(dt.ToString("f", CultureInfo.InvariantCulture));   // Monday, 15 June 2020 13:45
        Console.WriteLine(dt.ToString("F", CultureInfo.InvariantCulture));   // Monday, 15 June 2020 13:45:30
        Console.WriteLine(dt.ToString("g", CultureInfo.InvariantCulture));   // 06/15/2020 13:45
        Console.WriteLine(dt.ToString("G", CultureInfo.InvariantCulture));   // 06/15/2020 13:45:30
        Console.WriteLine(dt.ToString("m", CultureInfo.InvariantCulture));   // June 15
        Console.WriteLine(dt.ToString("y", CultureInfo.InvariantCulture));   // 2020 June
        Console.WriteLine(dt.ToString("s", CultureInfo.InvariantCulture));   // 2020-06-15T13:45:30
        // custom token combinations (ASCII-only output)
        Console.WriteLine(dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));   // 15/06/2020
        Console.WriteLine(dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-15 13:45:30
        Console.WriteLine(dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-15T13:45:30
        Console.WriteLine(dt.ToString("hh:mm tt", CultureInfo.InvariantCulture)); // 01:45 PM
        Console.WriteLine(dt.ToString("ddd MMMM d", CultureInfo.InvariantCulture)); // Mon June 15
        Console.WriteLine(dt.ToString("yyyy-MM", CultureInfo.InvariantCulture));    // 2020-06
        DateTime night = new DateTime(2020, 12, 31, 23, 59, 59);
        Console.WriteLine(night.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-12-31 23:59:59
        Console.WriteLine(night.ToString("tt", CultureInfo.InvariantCulture));      // PM
        DateTime noon = new DateTime(2020, 6, 15, 12, 0, 0);
        Console.WriteLine(noon.ToString("hh", CultureInfo.InvariantCulture));       // 12
        Console.WriteLine(noon.ToString("tt", CultureInfo.InvariantCulture));       // PM

        //===== 4. Infinity/NaN family (zh-CN U+221E '∞' via the runtime
        //          format surface -- double.ToString()/ToString(format)):
        //          net48 zh-CN renders +-Infinity as the culture symbol
        //          U+221E and NaN as "NaN" for EVERY format; the runtime
        //          format family (format.cpp G15 + formatext.cpp) matches.
        //          The NumberFormatInfo SYMBOL getters stay avoided (see
        //          the header: a compile-time fold cannot be culture-aware).
        Console.WriteLine(double.PositiveInfinity.ToString());          // ∞
        Console.WriteLine(double.NegativeInfinity.ToString());          // -∞
        Console.WriteLine(double.NaN.ToString());                       // NaN
        Console.WriteLine((double.PositiveInfinity).ToString("G"));     // ∞
        Console.WriteLine((double.PositiveInfinity).ToString("N2"));    // ∞
        Console.WriteLine((double.NegativeInfinity).ToString("E1"));    // -∞
        Console.WriteLine((double.NaN).ToString("F1"));                 // NaN
        // codepoint pins (ASCII output; the \u221E escape keeps the source
        // ASCII -- no encoding pitfalls)
        Console.WriteLine(double.PositiveInfinity.ToString() == "\u221E");      // True
        Console.WriteLine(double.NegativeInfinity.ToString() == "-\u221E");     // True
        Console.WriteLine(double.PositiveInfinity.ToString("N2") == "\u221E");  // True
        Console.WriteLine(double.NaN.ToString() == "NaN");                      // True
    }
}}
