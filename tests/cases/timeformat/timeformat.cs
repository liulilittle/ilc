//------------------------------------------------------------------------------
// timeformat.cs -- Time domain DEEP probe 3 (e2e batch C49): the DateTime
// format family (every wired standard specifier + the custom token set,
// pinned against the timeext oracle), parse/parse-exact round trips over
// the wired invariant-culture subset, tick arithmetic, and the current-
// instant timezone surface -- byte-exact vs the net48 oracle.
//
// Wired surface (MethodMap.DateTime.cs / TimeExt2.cs / TimeZone.cs /
// TimeSpan.cs), all probes stay within it:
//   DateTime ctor(3/6 arg), Year..Second, DayOfWeek
//   AddDays/AddHours/AddMinutes/AddTicks, Subtract(DateTime), op_Subtraction
//   CompareTo, static Compare
//   ToString() / ToString(format) / ToString(format, IFormatProvider)
//     -- the runtime formatter is invariant-culture ONLY, so every
//     culture-sensitive probe passes CultureInfo.InvariantCulture explicitly
//     (net48 then agrees with the runtime's fixed output; the provider arg
//     is DROPPED at emission).
//   Parse(string[, provider]) and ParseExact(string, format[, provider])
//     -- the wired invariant subset: ISO "YYYY-MM-DD[ T]HH:MM[:SS]" and US
//     "M/D/YY[YY]" orders, TwoDigitYearMax expansion, whitespace trim.
//   TimeZoneInfo.Local/Utc + TimeZone.CurrentTimeZone GetUtcOffset /
//     ToLocalTime / ToUniversalTime / IsDaylightSavingTime (current-instant
//     simplification; both net48 and the runtime evaluate on THIS machine,
//     so the offset/relative values agree).
//
// Deliberately OUT of scope (would diverge or fail to transpile):
//   * the round-trip/RFC/universal singles "O"/"R"/"u"/"U" ARE now wired
//     (D-FMT-1 fix, 2026-08-12: ISO8601 round-trip / RFC1123 / universal
//     sortable / universal full); "O" emits the Unspecified-kind form (no
//     Kind suffix -- the runtime tracks no DateTime Kind), "U" converts to
//     UTC via the local offset (net48 ToUniversalTime of Unspecified);
//   * quoted-literal/backslash ToString formats ARE now wired (D-FMT-1 fix:
//     net48 strips quotes, '\x' escapes one literal; unterminated
//     quote/backslash throws FormatException) -- ParseExact also supports
//     quotes;
//   * timezone suffixes / fractional seconds / time-only Parse input
//     (net48 accepts, the runtime subset rejects -- RECORDED divergence);
//   * relational operators (unmapped), ToShortDateString/LongDateString
//     etc. (LOUD rejections in MethodMap.DateTimeExtra.cs);
//   * 2-digit-year values 30..49 were previously flagged as a divergence
//     (a RECORDED but FACTUALLY WRONG D-FMT-2 note claimed net48
//     TwoDigitYearMax=2029; the fresh 2026-08-12 net48 probe shows
//     TwoDigitYearMax=2049, identical to the runtime's 2049 -- the band
//     30..49 maps to 2030..2049 on BOTH, probed below).
//   Exceptions print TYPE NAME only (messages are OS-localized).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class TimeFormat {

    public static void Main()
    {
        DateTime dt = new DateTime(2020, 6, 15, 13, 45, 30);

        // ============ S1: standard single-letter family (invariant) ============
        Console.WriteLine("std_d=" + dt.ToString("d", CultureInfo.InvariantCulture));   // 06/15/2020
        Console.WriteLine("std_D=" + dt.ToString("D", CultureInfo.InvariantCulture));   // Monday, 15 June 2020
        Console.WriteLine("std_t=" + dt.ToString("t", CultureInfo.InvariantCulture));   // 13:45
        Console.WriteLine("std_T=" + dt.ToString("T", CultureInfo.InvariantCulture));   // 13:45:30
        Console.WriteLine("std_f=" + dt.ToString("f", CultureInfo.InvariantCulture));   // Monday, 15 June 2020 13:45
        Console.WriteLine("std_F=" + dt.ToString("F", CultureInfo.InvariantCulture));   // Monday, 15 June 2020 13:45:30
        Console.WriteLine("std_g=" + dt.ToString("g", CultureInfo.InvariantCulture));   // 06/15/2020 13:45
        Console.WriteLine("std_G=" + dt.ToString("G", CultureInfo.InvariantCulture));   // 06/15/2020 13:45:30
        Console.WriteLine("std_m=" + dt.ToString("m", CultureInfo.InvariantCulture));   // June 15
        Console.WriteLine("std_y=" + dt.ToString("y", CultureInfo.InvariantCulture));   // 2020 June
        Console.WriteLine("std_s=" + dt.ToString("s", CultureInfo.InvariantCulture));   // 2020-06-15T13:45:30
        Console.WriteLine("std_O=" + dt.ToString("O", CultureInfo.InvariantCulture));   // 2020-06-15T13:45:30.0000000 (ISO8601 round-trip)
        Console.WriteLine("std_R=" + dt.ToString("R", CultureInfo.InvariantCulture));   // Mon, 15 Jun 2020 13:45:30 GMT (RFC1123)
        Console.WriteLine("std_r=" + dt.ToString("r", CultureInfo.InvariantCulture));   // Mon, 15 Jun 2020 13:45:30 GMT (RFC1123)
        Console.WriteLine("std_u=" + dt.ToString("u", CultureInfo.InvariantCulture));   // 2020-06-15 13:45:30Z (universal sortable)
        Console.WriteLine("std_U=" + dt.ToString("U", CultureInfo.InvariantCulture));   // Monday, 15 June 2020 05:45:30 (universal full, UTC-converted on UTC+8)
        Console.WriteLine("std_Of=" + dt.AddTicks(1234567L).ToString("O", CultureInfo.InvariantCulture)); // 2020-06-15T13:45:30.1234567 (O keeps all 7 fraction digits)
        Console.WriteLine("std_empty=[" + dt.ToString("", CultureInfo.InvariantCulture) + "]"); // [06/15/2020 13:45:30]
        Console.WriteLine("std_null=" + dt.ToString((string)null, CultureInfo.InvariantCulture)); // 06/15/2020 13:45:30
        Console.WriteLine("std_y5=" + new DateTime(5, 6, 15).ToString("y", CultureInfo.InvariantCulture)); // 0005 June
        Console.WriteLine("std_dpad=" + new DateTime(2020, 6, 5).ToString("d", CultureInfo.InvariantCulture)); // 06/05/2020
        try { dt.ToString("q", CultureInfo.InvariantCulture); Console.WriteLine("std_q=false"); }
        catch (FormatException) { Console.WriteLine("std_q=true"); }                      // True (unknown single letter)

        // ============ S2: custom format tokens ============
        Console.WriteLine("cust1=" + dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-15 13:45:30
        Console.WriteLine("cust2=" + dt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));          // 06/15/2020
        Console.WriteLine("cust3=" + dt.ToString("M/d/yy", CultureInfo.InvariantCulture));               // 6/15/20
        Console.WriteLine("cust4=" + dt.ToString("dddd, MMMM d", CultureInfo.InvariantCulture));         // Monday, June 15
        Console.WriteLine("cust5=" + dt.ToString("ddd MMM d", CultureInfo.InvariantCulture));            // Mon Jun 15
        Console.WriteLine("cust6=" + dt.ToString("HH:mm", CultureInfo.InvariantCulture));                // 13:45
        Console.WriteLine("cust7=" + dt.ToString("H:m", CultureInfo.InvariantCulture));                  // 13:45
        Console.WriteLine("cust8=" + dt.ToString("h:mm tt", CultureInfo.InvariantCulture));              // 1:45 PM
        Console.WriteLine("cust9=" + dt.ToString("hh:mm tt", CultureInfo.InvariantCulture));             // 01:45 PM
        Console.WriteLine("cust10=" + dt.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture));         // 01:45:30 PM
        Console.WriteLine("cust11=" + dt.ToString("tt", CultureInfo.InvariantCulture));                  // PM
        Console.WriteLine("cust12=" + dt.ToString("t", CultureInfo.InvariantCulture));                   // P
        Console.WriteLine("cust13=" + new DateTime(2020, 6, 15, 0, 30, 0).ToString("hh:mm tt", CultureInfo.InvariantCulture));   // 12:30 AM
        Console.WriteLine("cust14=" + new DateTime(2020, 6, 15, 12, 0, 0).ToString("hh:mm tt", CultureInfo.InvariantCulture));  // 12:00 PM
        Console.WriteLine("cust15=" + new DateTime(2020, 6, 15, 23, 59, 59).ToString("h:mm:ss tt", CultureInfo.InvariantCulture)); // 11:59:59 PM

        // ============ S2b: quoted literals + backslash escapes (D-FMT-1 fix) ============
        Console.WriteLine("q1=" + dt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-15T13:45:30 (quotes stripped)
        Console.WriteLine("q2=" + dt.ToString("yyyy-MM-dd\"T\"HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-15T13:45:30 (double-quoted literal)
        Console.WriteLine("q3=" + dt.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)); // Monday, 15 June 2020 at 13:45
        Console.WriteLine("q4=" + dt.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture)); // 2020-06-15 (quoted separators)
        Console.WriteLine("q5=" + dt.ToString("d'dd'd", CultureInfo.InvariantCulture)); // 15dd15 (quoted group is literal)
        Console.WriteLine("q6=" + dt.ToString("yyyy'yy'", CultureInfo.InvariantCulture)); // 2020yy (quoted 'yy' is literal)
        Console.WriteLine("q7=" + dt.ToString("a\\b", CultureInfo.InvariantCulture)); // ab (backslash escape)
        Console.WriteLine("q8=" + dt.ToString("yyyy\\-MM\\-dd", CultureInfo.InvariantCulture)); // 2020-06-15 (escaped separators)
        try { dt.ToString("'unterminated", CultureInfo.InvariantCulture); Console.WriteLine("q9=false"); }
        catch (FormatException) { Console.WriteLine("q9=true"); } // True (unterminated quote)
        try { dt.ToString("abc\\", CultureInfo.InvariantCulture); Console.WriteLine("q10=false"); }
        catch (FormatException) { Console.WriteLine("q10=true"); } // True (trailing backslash)

        // ============ S3: fractional-second tokens (f / F) ============
        DateTime frac = new DateTime(2020, 6, 15, 13, 45, 30).AddTicks(1234567L);
        Console.WriteLine("frac_f7=" + frac.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)); // 13:45:30.1234567
        Console.WriteLine("frac_f3=" + frac.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));     // 13:45:30.123
        Console.WriteLine("frac_F5=" + frac.ToString("HH:mm:ss.FFFFF", CultureInfo.InvariantCulture));   // 13:45:30.12345
        DateTime frac2 = new DateTime(2020, 6, 15, 13, 45, 30).AddTicks(1200000L);
        Console.WriteLine("frac2_f3=" + frac2.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));   // 13:45:30.120
        Console.WriteLine("frac2_F3=" + frac2.ToString("HH:mm:ss.FFF", CultureInfo.InvariantCulture));   // 13:45:30.12
        Console.WriteLine("frac2_f7=" + frac2.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)); // 13:45:30.1200000
        try { frac.ToString("HH:mm:ss.ffffffff", CultureInfo.InvariantCulture); Console.WriteLine("frac8=false"); }
        catch (FormatException) { Console.WriteLine("frac8=true"); }                                     // True (N > 7)

        // ============ S4: parse round trips (invariant subset) ============
        DateTime p1 = DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p1=" + p1.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:30
        DateTime p2 = DateTime.Parse("2020-06-15T13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p2=" + p2.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:30
        DateTime p3 = DateTime.Parse("2020-06-15 13:45", CultureInfo.InvariantCulture);
        Console.WriteLine("p3=" + p3.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:00
        DateTime p4 = DateTime.Parse("6/15/2020 13:45:30", CultureInfo.InvariantCulture);
        Console.WriteLine("p4=" + p4.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:30
        DateTime p5 = DateTime.Parse("6/15/20", CultureInfo.InvariantCulture);
        Console.WriteLine("p5=" + p5.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));              // 2020-06-15
        // D-FMT-2 correction: the band 30..49 maps to 2030..2049 on net48
        // (TwoDigitYearMax=2049, fresh probe) -- identical to the runtime.
        DateTime p30 = DateTime.Parse("6/15/30", CultureInfo.InvariantCulture);
        Console.WriteLine("p30=" + p30.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));           // 2030-06-15
        DateTime p49 = DateTime.Parse("6/15/49", CultureInfo.InvariantCulture);
        Console.WriteLine("p49=" + p49.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));           // 2049-06-15
        DateTime p50 = DateTime.Parse("6/15/50", CultureInfo.InvariantCulture);
        Console.WriteLine("p50=" + p50.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));           // 1950-06-15
        DateTime p6 = DateTime.Parse("  2020-06-15 13:45:30  ", CultureInfo.InvariantCulture);
        Console.WriteLine("p6=" + p6.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:30
        Console.WriteLine("p7=" + DateTime.Parse("2020-02-29", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); // 2020-02-29
        Console.WriteLine("p8=" + (DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture).CompareTo(DateTime.Parse("2020-06-15 13:45:30", CultureInfo.InvariantCulture)) == 0)); // True
        try { DateTime.Parse("2019-02-29", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_leap=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_leap=true"); }
        try { DateTime.Parse("2020-13-01", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_month=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_month=true"); }
        try { DateTime.Parse("2020-06-32", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_day=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_day=true"); }
        try { DateTime.Parse("2020-06-15 25:00:00", CultureInfo.InvariantCulture); Console.WriteLine("p_bad_hour=false"); }
        catch (FormatException) { Console.WriteLine("p_bad_hour=true"); }

        // ============ S5: ParseExact round trips + errors ============
        DateTime x1 = DateTime.ParseExact("2020-06-15", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Console.WriteLine("x1=" + x1.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 00:00:00
        DateTime x2 = DateTime.ParseExact("2020-06-15T13:45:30", "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        Console.WriteLine("x2=" + x2.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:30
        DateTime x3 = DateTime.ParseExact("6/15/2020", "M/d/yyyy", CultureInfo.InvariantCulture);
        Console.WriteLine("x3=" + x3.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));              // 2020-06-15
        DateTime x4 = DateTime.ParseExact("15/6/2020", "d/M/yyyy", CultureInfo.InvariantCulture);
        Console.WriteLine("x4=" + x4.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));              // 2020-06-15
        DateTime x5 = DateTime.ParseExact("2020-6-5", "yyyy-M-d", CultureInfo.InvariantCulture);
        Console.WriteLine("x5=" + x5.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));              // 2020-06-05
        DateTime x6 = DateTime.ParseExact("20200615134530", "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        Console.WriteLine("x6=" + x6.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));     // 2020-06-15 13:45:30
        Console.WriteLine("x7=" + DateTime.ParseExact("6/15/20", "M/d/yy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));   // 2020-06-15
        Console.WriteLine("x8=" + DateTime.ParseExact("99/12/31", "yy/MM/dd", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); // 1999-12-31
        Console.WriteLine("x9=" + (DateTime.ParseExact("01/02/03 04:05:06", "yy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))); // 2001-02-03 04:05:06
        // round trip: ParseExact output feeds Parse again
        DateTime rt = DateTime.Parse(DateTime.ParseExact("2020-06-15 13:45:30", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        Console.WriteLine("x_rt=" + rt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));   // 2020-06-15 13:45:30
        // time-only ParseExact -> components (date defaults to today on BOTH)
        DateTime to = DateTime.ParseExact("13:45", "HH:mm", CultureInfo.InvariantCulture);
        Console.WriteLine("x_to_h=" + (to.Hour == 13));                                                 // True
        Console.WriteLine("x_to_m=" + (to.Minute == 45));                                               // True
        try { DateTime.ParseExact("2020-06-15", "yyyy/MM/dd", CultureInfo.InvariantCulture); Console.WriteLine("x_sep=false"); }
        catch (FormatException) { Console.WriteLine("x_sep=true"); }                                     // True (separator mismatch)
        try { DateTime.ParseExact("2020-06-15 13", "yyyy-MM-dd", CultureInfo.InvariantCulture); Console.WriteLine("x_trail=false"); }
        catch (FormatException) { Console.WriteLine("x_trail=true"); }                                   // True (trailing input)
        try { DateTime.ParseExact("2020-13-01", "yyyy-MM-dd", CultureInfo.InvariantCulture); Console.WriteLine("x_month=false"); }
        catch (FormatException) { Console.WriteLine("x_month=true"); }                                   // True
        try { DateTime.ParseExact("2020-06-15", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); Console.WriteLine("x_miss=false"); }
        catch (FormatException) { Console.WriteLine("x_miss=true"); }                                    // True (missing time)

        // ============ S6: tick arithmetic + comparison ============
        DateTime baseD = new DateTime(2020, 6, 15, 12, 0, 0);
        Console.WriteLine("t1=" + baseD.AddTicks(1234567L).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)); // 12:00:00.1234567
        Console.WriteLine("t2=" + baseD.AddTicks(-1234567L).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)); // 11:59:59.8765433
        Console.WriteLine("t3=" + baseD.AddDays(1).AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); // 2020-06-16 11:00:00
        Console.WriteLine("t4=" + (baseD.CompareTo(baseD.AddMinutes(1)) < 0));                          // True
        Console.WriteLine("t5=" + (baseD.CompareTo(baseD) == 0));                                       // True
        Console.WriteLine("t6=" + (DateTime.Compare(baseD.AddMinutes(1), baseD) > 0));                  // True
        Console.WriteLine("t7=" + ((baseD.AddHours(2) - baseD).TotalMinutes == 120.0));                 // True
        Console.WriteLine("t8=" + ((baseD - baseD.AddHours(2)).TotalHours == -2.0));                    // True

        // ============ S7: timezone (current-instant surface) ============
        Console.WriteLine("tz_local=" + TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes);     // machine offset (e.g. 480)
        Console.WriteLine("tz_utc=" + TimeZoneInfo.Utc.GetUtcOffset(DateTime.UtcNow).TotalMinutes);      // 0
        Console.WriteLine("tz_cur=" + TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now).TotalMinutes); // == local
        Console.WriteLine("tz_local_neq0=" + (TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes != 0)); // True (UTC+8)
        Console.WriteLine("tz_same=" + (TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes == TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now).TotalMinutes)); // True
        Console.WriteLine("tz_tolocal=" + ((TimeZone.CurrentTimeZone.ToLocalTime(DateTime.UtcNow) - DateTime.Now).TotalSeconds < 2.0)); // True
        Console.WriteLine("tz_toutc=" + ((TimeZone.CurrentTimeZone.ToUniversalTime(DateTime.Now) - DateTime.UtcNow).TotalSeconds < 2.0)); // True
        Console.WriteLine("tz_roundtrip=" + ((TimeZone.CurrentTimeZone.ToUniversalTime(TimeZone.CurrentTimeZone.ToLocalTime(DateTime.UtcNow)) - DateTime.UtcNow).TotalSeconds < 2.0)); // True
        Console.WriteLine("tz_dst=" + TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now));            // False (UTC+8)
        Console.WriteLine("tz_year=" + (TimeZone.CurrentTimeZone.ToLocalTime(DateTime.UtcNow).Year == DateTime.Now.Year)); // True
    }
}}
