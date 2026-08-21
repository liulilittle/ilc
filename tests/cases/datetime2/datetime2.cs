using System;
using System.Globalization;

//------------------------------------------------------------------------------
// datetime2 -- DateTime deep-chain e2e: ParseExact width semantics + yy
// two-digit-year expansion (DateTime conformance fix verification, e2e-recheck2
// §6: timeext2.cpp Parse/ParseExact/yy-expansion audit), ToString G/fff/FFF,
// leap-year validation, AddDays month-crossing, Ticks round trip.
//
// Every probe prints a bool -- no culture-dependent text output. Culture-
// pinned probes use CultureInfo.InvariantCulture (folds to null at the call
// site; the runtime fixes en-US/invariant output).
//
// GATING (PARSE_PROBES, csproj DefineConstants, DEFAULT ON):
//   Both DateTime.ParseExact (general ParseExact and the yy expansion live
//   here) and DateTime.Parse are NOT reachable through ILC today:
//   * W2 (known): MethodMap.TimeExt2.cs wires only ParseExact(string,string)
//     -- but net48 has NO such overload (CS1501: the 2-arg form exists only
//     in .NET Core 2.1+), so C# against net48 refs can only emit the 3-arg
//     provider form, which TimeExt2.cs:39 LOUDly rejects. General
//     DateTime.Parse is likewise unmapped (DateTimeExtra.cs:42).
//   * W2a (new gap): map ParseExact(string,string,IFormatProvider) with the
//     provider arg DROPPED at emission (the "drop" cast precedent:
//     MethodMap.DateTime.cs ToString 2-arg) -> il2cpp_datetime_parse_exact.
//   The gated sections below are the net48 ORACLE for these fixes: every
//   shape is pinned in expected.txt; they become ILC-reachable once the
//   mappings land, then rebuild out/ WITHOUT the -p:DefineConstants= override
//   and the matrix flips to PASS.
//   The un-gated sections (ToString/leap ctor/AddDays/Ticks) verify the wired
//   surface end-to-end TODAY.
//
// Deliberately EXCLUDED Parse shapes (net48 == runtime divergence, would make
// the oracle disagree with the runtime subset pinned in timeext2.h):
// timezone suffixes, fractional seconds, time-only input, 5+ digit years,
// ISO shapes with 1-digit month/day ("2024-2-29" parses in the runtime subset
// but THROWS under net48 invariant -- excluded).
//
// Ticks probes route through the TimeSpan surface: DateTime.get_Ticks is LOUD
// (MethodMap.DateTimeExtra.cs), TimeSpan ctor(ticks)/get_Ticks are wired.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class DateTime2Case
    {
        public static void Main()
        {
#if PARSE_PROBES
            //===== 1. ParseExact width semantics (conformance fix verification) =====
            // 3-arg provider form -- the only ParseExact overload net48 C# can
            // emit (W2a). Single M/d tokens consume 1..2 digits; doubled
            // MM/dd consume EXACTLY 2. "yyyy-M-d" accepts 1-digit fields...
            DateTime px = DateTime.ParseExact("2024-2-9", "yyyy-M-d", CultureInfo.InvariantCulture);
            Console.WriteLine(px.Year == 2024);
            Console.WriteLine(px.Month == 2);
            Console.WriteLine(px.Day == 9);
            // yy: EXACTLY 2 digits + TwoDigitYearMax (2049) expansion:
            // 24 -> 2024, 49 -> 2049 (boundary), 50 -> 1950, 29 -> 2029, 00 -> 2000.
            Console.WriteLine(DateTime.ParseExact("24", "yy", CultureInfo.InvariantCulture).Year == 2024);
            Console.WriteLine(DateTime.ParseExact("49", "yy", CultureInfo.InvariantCulture).Year == 2049);
            Console.WriteLine(DateTime.ParseExact("50", "yy", CultureInfo.InvariantCulture).Year == 1950);
            Console.WriteLine(DateTime.ParseExact("29", "yy", CultureInfo.InvariantCulture).Year == 2029);
            Console.WriteLine(DateTime.ParseExact("00", "yy", CultureInfo.InvariantCulture).Year == 2000);
            // yyyy: EXACTLY 4 digits, year as-is; literals '-' and '/' verbatim.
            Console.WriteLine(DateTime.ParseExact("2024-02-29 13:45:06", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture).Hour == 13);
            Console.WriteLine(DateTime.ParseExact("2024/02/29", "yyyy/MM/dd", CultureInfo.InvariantCulture).Day == 29);
            // Strict-width rejections: doubled tokens reject 1-digit fields;
            // trailing input and 4-digit tokens reject 2-digit years.
            Console.WriteLine(ThrowsParseExact("2024-2-9", "yyyy-MM-dd"));          // MM needs exactly 2
            Console.WriteLine(ThrowsParseExact("2024-02-9", "yyyy-MM-dd"));         // dd needs exactly 2
            Console.WriteLine(ThrowsParseExact("2024-2-09", "yyyy-MM-dd"));         // MM needs exactly 2
            Console.WriteLine(ThrowsParseExact("2024-02-29 junk", "yyyy-MM-dd"));   // trailing input
            Console.WriteLine(ThrowsParseExact("24", "yyyy"));                      // yyyy needs exactly 4
            // Leap-day validation through ParseExact; round trip through the formatter.
            Console.WriteLine(ThrowsParseExact("2023-02-29", "yyyy-MM-dd"));   // non-leap
            Console.WriteLine(DateTime.ParseExact("2024-02-29 13:45:06", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                  .ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 13:45:06");

            //===== 2. DateTime.Parse (general) -- invariant-culture subset =====
            // GATED (W2): il2cpp_datetime_parse exists in timeext2.cpp but is
            // not mapped in MethodMap.TimeExt2.cs. These probes are the W2
            // acceptance reference: every shape below is net48-invariant-oracle
            // == runtime-subset contract (timeext2.h), ISO and US orders.
            Console.WriteLine(DateTime.Parse("2024-02-29", CultureInfo.InvariantCulture).Day == 29);
            Console.WriteLine(DateTime.Parse("2024-02-29 13:45:06", CultureInfo.InvariantCulture).Hour == 13);
            Console.WriteLine(DateTime.Parse("2024-02-29T13:45:30", CultureInfo.InvariantCulture).Second == 30);
            Console.WriteLine(DateTime.Parse("2024/02/29", CultureInfo.InvariantCulture).Day == 29);
            Console.WriteLine(DateTime.Parse("2024-02-29 13:45", CultureInfo.InvariantCulture).Minute == 45);
            Console.WriteLine(DateTime.Parse(" 2024-02-29 ", CultureInfo.InvariantCulture).Day == 29);
            Console.WriteLine(DateTime.Parse("0001-01-01", CultureInfo.InvariantCulture).Year == 1);
            // US order, 2-digit years via TwoDigitYearMax, 4-digit as-is.
            Console.WriteLine(DateTime.Parse("6/15/50", CultureInfo.InvariantCulture).Year == 1950);
            Console.WriteLine(DateTime.Parse("6/15/29", CultureInfo.InvariantCulture).Year == 2029);
            Console.WriteLine(DateTime.Parse("6/15/49", CultureInfo.InvariantCulture).Year == 2049);
            Console.WriteLine(DateTime.Parse("6/15/2024", CultureInfo.InvariantCulture).Year == 2024);
            // Error probes: leap validation, month range, US-order reading.
            Console.WriteLine(ThrowsParse("2023-02-29"));   // non-leap
            Console.WriteLine(ThrowsParse("2024-13-01"));   // month 13
            Console.WriteLine(ThrowsParse("24-06-15"));     // US order: month 24
            Console.WriteLine(ThrowsParse("29/02/2024"));   // US order: month 29
#endif

            //===== 3. ToString: yyyy-MM-dd / G / fff / FFF (wired) =====
            DateTime d = new DateTime(2024, 2, 29, 13, 45, 6);
            Console.WriteLine(d.ToString("yyyy-MM-dd") == "2024-02-29");
            Console.WriteLine(d.ToString("yyyy-MM-dd HH:mm:ss") == "2024-02-29 13:45:06");
            Console.WriteLine(d.ToString("G", CultureInfo.InvariantCulture) == "02/29/2024 13:45:06");
            Console.WriteLine(d.ToString("G").Contains("2024"));
            Console.WriteLine(d.ToString("G").Contains(":"));
            Console.WriteLine(d.ToString("fff") == "000");   // zero sub-second
            Console.WriteLine(d.ToString("FFF") == "");      // F trims trailing zeros
            DateTime ms = d.AddTicks(1230000);               // +123 ms exactly
            Console.WriteLine(ms.ToString("fff") == "123");
            Console.WriteLine(ms.ToString("FFF") == "123");

            //===== 4. Leap year ctor: 2024-02-29 ok, 2023 / 1900 throw (wired) =====
            Console.WriteLine(new DateTime(2024, 2, 29).Day == 29);
            Console.WriteLine(new DateTime(2024, 2, 29).Month == 2);
            Console.WriteLine(ThrowsArgYmd(2023, 2, 29));    // non-leap ctor
            Console.WriteLine(ThrowsArgYmd(1900, 2, 29));    // century non-leap
            Console.WriteLine(new DateTime(2000, 2, 29).Day == 29);  // 400-year leap

            //===== 5. AddDays across month boundaries (wired) =====
            Console.WriteLine(new DateTime(2024, 1, 31).AddDays(1).Month == 2);
            Console.WriteLine(new DateTime(2024, 1, 31).AddDays(1).Day == 1);
            Console.WriteLine(new DateTime(2024, 2, 28).AddDays(1).Day == 29);  // leap-year Feb
            Console.WriteLine(new DateTime(2024, 2, 29).AddDays(1).Month == 3); // leap-day rollover
            Console.WriteLine(new DateTime(2023, 2, 28).AddDays(1).Month == 3); // non-leap Feb
            Console.WriteLine(new DateTime(2023, 2, 28).AddDays(1).Day == 1);
            Console.WriteLine(new DateTime(2024, 12, 31).AddDays(1).Year == 2025);

            //===== 6. Ticks round trip (TimeSpan surface -- see header) =====
            Console.WriteLine(new TimeSpan(new TimeSpan(1, 2, 3).Ticks).Ticks == 37230000000L);
            Console.WriteLine(new TimeSpan(1, 2, 3).Ticks == 37230000000L);    // 1h 2m 3s = 3723e7
            Console.WriteLine((d - new DateTime(2024, 2, 29, 13, 45, 0)).Ticks == 60000000L); // 6s
            Console.WriteLine(d.AddTicks(0).CompareTo(d) == 0);                // zero-delta identity
            Console.WriteLine(d.AddTicks(1230000).CompareTo(ms) == 0);         // +123 ms round trip
        }

        // Exception probes: catch-type matching walks the throw chain (the
        // chain includes the thrown type itself -- exception.cpp matcher).
        // NOTE: the bool must be produced INSIDE the handlers -- the
        // transpiler rejects code after a try/catch construct (H2: leave
        // target != handler end, StackFold.cs:582).
        static bool ThrowsArgYmd(int year, int month, int day)
        {
            try
            {
                new DateTime(year, month, day);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }

#if PARSE_PROBES
        static bool ThrowsParseExact(string input, string format)
        {
            try
            {
                DateTime.ParseExact(input, format, CultureInfo.InvariantCulture);
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        }

        static bool ThrowsParse(string input)
        {
            try
            {
                DateTime.Parse(input, CultureInfo.InvariantCulture);
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        }
#endif
    }
}
