#pragma once
//------------------------------------------------------------------------------
// il2cpp_time_ext.h -- System.DateTime.ToString(format) runtime support.
//
// The transpiler's BCL mapping resolves DateTime.ToString(string) to
// icall_datetime_to_string_format (the plain ToString() overload stays on
// time.cpp's icall_datetime_to_string -- this file does NOT duplicate it).
//
// Format subset (net48 / en-US culture behavior pinned by the timeext e2e
// oracle, run on .NET Framework 4.8 -- see tests/cases/timeext/timeext.cs):
//
//   Standard (single-letter patterns; PROVIDER-AWARE: zh-CN by default,
//   invariant with the empty-name carrier -- see PROVIDER ROUTING below):
//     "d"  -> MM/dd/yyyy                    ("06/15/2020")
//     "D"  -> dddd, dd MMMM yyyy            ("Monday, 15 June 2020")
//     "t"  -> HH:mm                         ("13:45")
//     "T"  -> HH:mm:ss                      ("13:45:30")
//     "f"  -> dddd, dd MMMM yyyy HH:mm      ("Monday, 15 June 2020 13:45")
//     "F"  -> dddd, dd MMMM yyyy HH:mm:ss   ("Monday, 15 June 2020 13:45:30")
//     "g"  -> MM/dd/yyyy HH:mm              ("06/15/2020 13:45")
//     "m"  -> MMMM dd                       ("June 15")
//     "y"  -> yyyy MMMM                     ("2020 June"; year-5 -> "0005 June")
//     "s"  -> yyyy-MM-ddTHH:mm:ss           ("2020-06-15T13:45:30")
//     "G"  -> MM/dd/yyyy HH:mm:ss           (the plain ToString() output;
//            net48 invariant General pattern)
//     "O"  -> yyyy-MM-ddTHH:mm:ss.fffffff   (ISO8601 round trip, always 7
//            fraction digits; the net48-invariant "O" -- a Kind-suffix is
//            NOT emitted: the runtime tracks no DateTime Kind, so the
//            output matches the net48 Unspecified-kind form)
//     "R"/"r" -> ddd, dd MMM yyyy HH:mm:ss 'GMT'  (RFC1123:
//            "Mon, 15 Jun 2020 13:45:30 GMT")
//     "u"  -> yyyy-MM-dd HH:mm:ss'Z'        (universal sortable:
//            "2020-06-15 13:45:30Z")
//     "U"  -> dddd, dd MMMM yyyy HH:mm:ss of the UTC-converted value
//            (net48 universal full; the runtime converts via the local
//            timezone offset -- il2cpp_datetime_from_local -- matching
//            net48's ToUniversalTime of an Unspecified-kind DateTime)
//     Any OTHER single ASCII letter -> FormatException (net48 throws the
//     same for unknown standard specifiers).
//   null or empty format -> General "G" (net48 rule; delegates to the
//     existing culture-neutral default in time.cpp, which IS invariant G).
//     NOTE: net48 uses the CURRENT culture for EVERY standard specifier
//     ("G"/"d"/"g"/"m"/"y"/... all resolve the active culture's pattern) --
//     this runtime fixes en-US/invariant output (locale divergence reported).
//   Custom (multi-character):
//     letters y / M / d / H / h / m / s / f / F / t with repeat semantics:
//       y  no pad      yy  last 2 digits  yyy.. min 3/4/5.. digits
//       M  1..2 digits no pad/2 pad       MMM abbreviated  MMMM+ full name
//       d  1..2 day of month              ddd abbreviated  dddd+ full name
//       H  hour 0-23 (>=2 digits -> 2-digit padded)
//       h  12-hour clock (0 -> 12; >=2 digits -> 2-digit padded)
//       m / s  minute / second (>=2 digits -> 2-digit padded)
//       f / F  fractional second: first N digits of the 7-digit sub-second
//              tick count, zero-padded ('f') or trailing zeros trimmed
//              ('F'); N > 7 throws FormatException (net48 oracle).
//       t  AM/PM designator: "t" -> first char ("A"/"P"), "tt"+ -> full
//              ("AM"/"PM"), net48 DateTimeFormatInfo invariant designators.
//     Quoted literal groups ('...' and "...") emit the enclosed characters
//     verbatim (quotes stripped -- net48; e.g. "'at'" -> "at",
//     "d'dd'd" -> "15dd15"); backslash '\x' escapes one literal character
//     (net48; "\-" -> "-"). An unterminated quote or a trailing backslash
//     throws FormatException (net48).
//     All other characters (including letters outside the subset, e.g. 'a'
//     and the timezone letters z/K/Z) are emitted literally -- net48
//     divergence for the timezone specifiers, documented in the header +
//     report.
//   Month names (en-US, net48): January..December / Jan..Dec.
//   Weekday names (en-US, net48): Sunday..Saturday / Sun..Sat.
//   DayOfWeek encoding matches il2cpp_time.h: Sunday = 0 .. Saturday = 6.
//
// The format argument is an Il2CppObject* (managed string). Null is legal
// (default format). Non-string objects are caller errors -- defensively
// treated as null (no crash), like the other string-accepting icalls.
//
// PROVIDER ROUTING (cultureprobe regression fix): the trailing provider
// argument selects the standard-pattern family --
//   * nullptr -> host default zh-CN (the saudit5 oracle; also what the
//     parameterless ToString() / ToString(string) call sites pass);
//   * empty culture name (the InvariantCulture carrier) -> invariant
//     patterns (culturedeep/timeformat/datetime2 pass InvariantCulture);
//   * "zh-CN" -> zh-CN patterns;
//   * any other name -> invariant fallback, logged when the
//     IL2CPP_CULTURE_PROVIDER_LOG environment variable names a file.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_time.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// DateTime.ToString(string format[, IFormatProvider]): the format subset
// above in the provider-selected culture; returns a new managed string.
// Throws FormatException for an unknown SINGLE-letter format (net48
// contract); out-of-range DateTime components cannot occur (validated at
// construction).
Il2CppObject* icall_datetime_to_string_format(Il2CppDateTime dt, Il2CppObject* format,
                                              Il2CppObject* provider);

} // namespace runtime
} // namespace il2cpp
