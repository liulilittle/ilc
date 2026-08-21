//------------------------------------------------------------------------------
// timeparse.cs -- Time-domain PARSE DEEP probe (e2e batch C66): DateTime.Parse
// (every wired shape: ISO / US orders, '-' and '/' separators, 1-2 digit
// fields, TwoDigitYearMax band edges, whitespace trim, leap rules, reject
// set) + DateTime.ParseExact (token-width rules, quoted/backslash literals,
// literal-letter matching, missing-field defaults, error set) -- byte-exact
// vs the net48 oracle.
//
// Wired surface (MethodMap.TimeExt2.cs -> timeext2.cpp il2cpp_datetime_parse /
// il2cpp_datetime_parse_exact), every probe stays within it:
//   Parse:      ISO "YYYY-MM-DD[ T]HH:MM[:SS]" + US "M/D/YY[YY]" orders,
//               '-' or '/' (identical pair), month/day/time 1-2 digits, US
//               year 1-4 digits (1-2 -> ExpandTwoDigitYear, TwoDigitYearMax
//               2049), leading/trailing ASCII whitespace trimmed. REJECTS
//               timezone suffixes, fractional seconds, compact forms,
//               time-only input, inner whitespace, 5+ digit years.
//   ParseExact: y/M/d/H/m/s (1-2 digits when the run is 1, exactly the run
//               length otherwise); 'yyyy' exact-4; 'yy' 2-digit expansion;
//               quoted '...' / "..." and backslash literals; unknown single
//               letters match verbatim (the 'T' in an ISO format); missing
//               date fields default (year-only -> Jan 1; time-only -> today);
//               trailing input / separators / bad components -> FormatException.
//   CULTURE:    every call passes CultureInfo.InvariantCulture EXPLICITLY.
//               The IFormatProvider arg is DROPPED at emission (the runtime
//               is invariant-culture only); net48 then agrees because the
//               invariant-culture subset == the runtime's fixed output.
//               Culture-sensitive parse shapes (zh-CN 'yyyy/M/d', month-name
//               patterns) are a RECORDED gap -- NOT probed.
//   VALUES:     a parsed result is stored in a DateTime local and compared
//               against a freshly CONSTRUCTED DateTime via the wired CompareTo
//               (new DateTime(y,m,d,h,mi,s) -> icall_datetime_from_ymdhms;
//               CompareTo -> icall_datetime_compare_to). User methods taking a
//               DateTime ARGUMENT are a RECORDED transpiler gap (generated
//               signature lowers the param to int32_t while the caller passes
//               the 8-byte Il2CppDateTime temp -> C++ "no matching function"),
//               so no helper method takes a DateTime here.
//
// Deliberately OUT (net48-vs-runtime DIVERGENCES, oracle-verified 2026-08-12
// -- net48 ACCEPTS, the runtime subset REJECTS, so they are recorded, not
// probed):
//   * 3-digit-year ISO "123-06-15" (net48 parses year 123; the runtime only
//     accepts 4-digit ISO or 1-2 digit US first fields).
//   * timezone suffix "2020-06-15 13:45:30Z" (net48 converts to local time).
//   * fractional seconds "2020-06-15 13:45:30.5" (net48 keeps the fraction).
//   * time-only "13:45" Parse (net48 defaults the date to today).
//   * 'h'/'t'/'f'/'F'/'z'/'Z'/'K' tokens in ParseExact (runtime FormatException
//     vs net48 12-hour/AM-PM acceptance).
//   * Parse null input, provider+DateTimeStyles overloads (not wired).
//   Exceptions print TYPE NAME only (messages are OS-localized).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class TimeParse {

    public static void Main()
    {
        // ============ S1: Parse -- ISO shapes ============
        DateTime p;
        p = DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.Parse("2020-06-15T13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p_isoT=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.Parse("2020-06-15 13:45", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso_nosec=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 0)) == 0));
        p = DateTime.Parse("2020-06-15", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso_date=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.Parse("2020/06/15 13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso_slash=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.Parse("2020-6-5 3:5:5", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso_singled=" + (p.CompareTo(new DateTime(2020, 6, 5, 3, 5, 5)) == 0));
        p = DateTime.Parse("2020-06-05 03:05:05", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso_2dig=" + (p.CompareTo(new DateTime(2020, 6, 5, 3, 5, 5)) == 0));

        // ============ S2: Parse -- US shapes + TwoDigitYearMax ============
        p = DateTime.Parse("6/15/2020 13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.Parse("6/15/2020", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_date=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.Parse("6/15/20", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_2y=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.Parse("12/31/49", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_49=" + (p.CompareTo(new DateTime(2049, 12, 31, 0, 0, 0)) == 0));
        p = DateTime.Parse("12/31/50", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_50=" + (p.CompareTo(new DateTime(1950, 12, 31, 0, 0, 0)) == 0));
        p = DateTime.Parse("1/2/00", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_00=" + (p.CompareTo(new DateTime(2000, 1, 2, 0, 0, 0)) == 0));
        p = DateTime.Parse("1/2/1", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_1y=" + (p.CompareTo(new DateTime(2001, 1, 2, 0, 0, 0)) == 0));
        p = DateTime.Parse("1/2/999", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_3y=" + (p.CompareTo(new DateTime(999, 1, 2, 0, 0, 0)) == 0));
        p = DateTime.Parse("  6/15/2020 13:45:30  ", CultureInfo.InvariantCulture);
        Console.WriteLine("p_us_trim=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.Parse("\t2020-06-15 13:45:30\n", CultureInfo.InvariantCulture);
        Console.WriteLine("p_iso_trim=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));

        // ============ S3: Parse -- leap rules + error set (FormatException) ============
        p = DateTime.Parse("2000-02-29", CultureInfo.InvariantCulture);
        Console.WriteLine("p_leap400=" + (p.CompareTo(new DateTime(2000, 2, 29, 0, 0, 0)) == 0));
        p = DateTime.Parse("2020-02-29", CultureInfo.InvariantCulture);
        Console.WriteLine("p_leap4=" + (p.CompareTo(new DateTime(2020, 2, 29, 0, 0, 0)) == 0));
        try { DateTime.Parse("2019-02-29", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_leap=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_leap=true"); }
        try { DateTime.Parse("1900-02-29", CultureInfo.InvariantCulture); Console.WriteLine("p_leap100=false"); }
        catch (FormatException) { Console.WriteLine("p_leap100=true"); }
        try { DateTime.Parse("2020-13-01", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_month=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_month=true"); }
        try { DateTime.Parse("2020-06-32", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_day=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_day=true"); }
        try { DateTime.Parse("2020-06-15 25:00:00", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_hour=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_hour=true"); }
        try { DateTime.Parse("2020-06-15 13:60:00", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_min=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_min=true"); }
        try { DateTime.Parse("2020-06-15 13:45:60", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_sec=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_sec=true"); }
        try { DateTime.Parse("20200615", CultureInfo.InvariantCulture); Console.WriteLine("p_compact=false"); }
        catch (FormatException) { Console.WriteLine("p_compact=true"); }
        try { DateTime.Parse("2020-06-15 13:45:30 extra", CultureInfo.InvariantCulture); Console.WriteLine("p_trail=false"); }
        catch (FormatException) { Console.WriteLine("p_trail=true"); }
        try { DateTime.Parse("", CultureInfo.InvariantCulture); Console.WriteLine("p_empty=false"); }
        catch (FormatException) { Console.WriteLine("p_empty=true"); }

        // ============ S4: ParseExact -- success set ============
        p = DateTime.ParseExact("2020-06-15", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Console.WriteLine("x_ymd=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("2020-06-15T13:45:30", "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        Console.WriteLine("x_isoT=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.ParseExact("6/15/2020", "M/d/yyyy", CultureInfo.InvariantCulture);
        Console.WriteLine("x_mdy=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("15/6/2020", "d/M/yyyy", CultureInfo.InvariantCulture);
        Console.WriteLine("x_dmy=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("2020-6-5", "yyyy-M-d", CultureInfo.InvariantCulture);
        Console.WriteLine("x_ymd1=" + (p.CompareTo(new DateTime(2020, 6, 5, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("20200615134530", "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        Console.WriteLine("x_compact=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.ParseExact("6/15/20", "M/d/yy", CultureInfo.InvariantCulture);
        Console.WriteLine("x_yy20=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("49/12/31", "yy/MM/dd", CultureInfo.InvariantCulture);
        Console.WriteLine("x_yy49=" + (p.CompareTo(new DateTime(2049, 12, 31, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("50/01/01", "yy/MM/dd", CultureInfo.InvariantCulture);
        Console.WriteLine("x_yy50=" + (p.CompareTo(new DateTime(1950, 1, 1, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("00/02/03", "yy/MM/dd", CultureInfo.InvariantCulture);
        Console.WriteLine("x_yy00=" + (p.CompareTo(new DateTime(2000, 2, 3, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("0001", "yyyy", CultureInfo.InvariantCulture);
        Console.WriteLine("x_y1=" + (p.CompareTo(new DateTime(1, 1, 1, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("2020-06", "yyyy-MM", CultureInfo.InvariantCulture);
        Console.WriteLine("x_ym=" + (p.CompareTo(new DateTime(2020, 6, 1, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("2020-06-15 3:5:5", "yyyy-MM-dd H:m:s", CultureInfo.InvariantCulture);
        Console.WriteLine("x_single=" + (p.CompareTo(new DateTime(2020, 6, 15, 3, 5, 5)) == 0));
        p = DateTime.ParseExact("2020-06-15 13", "yyyy-MM-dd HH", CultureInfo.InvariantCulture);
        Console.WriteLine("x_hour=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 0, 0)) == 0));
        p = DateTime.ParseExact("2020-06-15 at 13:45", "yyyy-MM-dd 'at' HH:mm", CultureInfo.InvariantCulture);
        Console.WriteLine("x_quote=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 0)) == 0));
        p = DateTime.ParseExact("2020-06-15", "yyyy\\-MM\\-dd", CultureInfo.InvariantCulture);
        Console.WriteLine("x_esc=" + (p.CompareTo(new DateTime(2020, 6, 15, 0, 0, 0)) == 0));
        p = DateTime.ParseExact("2020-06-15T13:45", "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        Console.WriteLine("x_Tliteral=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 0)) == 0));
        // time-only ParseExact -> date defaults to today on BOTH (assert the time only)
        DateTime to = DateTime.ParseExact("13:45", "HH:mm", CultureInfo.InvariantCulture);
        Console.WriteLine("x_to_h=" + (to.Hour == 13 && to.Minute == 45));

        // ============ S5: ParseExact -- error set (FormatException) ============
        try { DateTime.ParseExact("2020-06-15", "yyyy/MM/dd", CultureInfo.InvariantCulture); Console.WriteLine("x_sep=false"); }
        catch (FormatException) { Console.WriteLine("x_sep=true"); }
        try { DateTime.ParseExact("2020-06-15 13", "yyyy-MM-dd", CultureInfo.InvariantCulture); Console.WriteLine("x_trail=false"); }
        catch (FormatException) { Console.WriteLine("x_trail=true"); }
        try { DateTime.ParseExact("2020-13-01", "yyyy-MM-dd", CultureInfo.InvariantCulture); Console.WriteLine("x_month=false"); }
        catch (FormatException) { Console.WriteLine("x_month=true"); }
        try { DateTime.ParseExact("2020-06-15", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); Console.WriteLine("x_miss=false"); }
        catch (FormatException) { Console.WriteLine("x_miss=true"); }
        try { DateTime.ParseExact("2020-06-15", "", CultureInfo.InvariantCulture); Console.WriteLine("x_emptyfmt=false"); }
        catch (FormatException) { Console.WriteLine("x_emptyfmt=true"); }
        try { DateTime.ParseExact("2020-06-15", "yyyy-MM-dd 'open", CultureInfo.InvariantCulture); Console.WriteLine("x_unterm=false"); }
        catch (FormatException) { Console.WriteLine("x_unterm=true"); }
        try { DateTime.ParseExact("2020-06-15", "yyyy-MM-dd\\", CultureInfo.InvariantCulture); Console.WriteLine("x_bareslash=false"); }
        catch (FormatException) { Console.WriteLine("x_bareslash=true"); }

        // ============ S6: round-trip (ParseExact -> Parse -> compare) ============
        p = DateTime.Parse(DateTime.ParseExact("2020-06-15 13:45:30", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                  .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        Console.WriteLine("rt1=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        p = DateTime.ParseExact(DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture)
                                  .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Console.WriteLine("rt2=" + (p.CompareTo(new DateTime(2020, 6, 15, 13, 45, 30)) == 0));
        Console.WriteLine("rt_cmp=" + (DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture)
                                       .CompareTo(DateTime.ParseExact("2020-06-15 13:45:30", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)) == 0));
    }
}}
