#pragma once
//------------------------------------------------------------------------------
// timeext2.h -- System.DateTime extension module 2: ParseExact, tick
// arithmetic, and comparison.
//
// This module EXTENDS the existing DateTime surface (il2cpp_time.h /
// time.cpp) WITHOUT touching it. Naming contract: every entry point here is
// il2cpp_datetime_* -- deliberately distinct from the existing icall_*
// names, so no symbol collides:
//
//   icall_datetime_to_string             (time.cpp)  -- plain ToString(), "G"
//   icall_datetime_to_string_format      (time_ext.h) -- ToString(format)
//   il2cpp_datetime_to_string_format     (THIS file) -- ext2 entry point;
//          delegates to the canonical formatter (never duplicated).
//   icall_datetime_compare_to            (time.cpp)  -- instance CompareTo
//   il2cpp_datetime_compare              (THIS file) -- static Compare
//   il2cpp_datetime_parse_exact          (THIS file) -- ParseExact
//   il2cpp_datetime_parse                (THIS file) -- Parse (subset)
//   il2cpp_datetime_add_ticks / subtract_ticks (THIS file) -- tick arithmetic
//
// Representation: the SAME 64-bit tick count as il2cpp_time.h (100 ns units
// since 0001-01-01T00:00:00, MinValue = 0, MaxValue = 3155378975999999999
// = 9999-12-31 23:59:59.9999999) -- DateTime values remain blittable
// Il2CppDateTime structs passed by value.
//
// ParseExact token semantics mirror .NET Core DateTimeParse.ParseByFormat /
// ParseDigits (System/Globalization/DateTimeParse.cs, pinned by the net48
// oracle):
//   yyyy/yyy:  EXACTLY 4/3 digits, year as-is (no expansion)
//   yy:        EXACTLY 2 digits, then TwoDigitYearMax (2049) expansion:
//              49 -> 2049, 50 -> 1950 (Gregorian invariant rule)
//   y:         1..2 digits, then the same expansion
//   MM/dd/HH/mm/ss: EXACTLY 2 digits
//   M/d/H/m/s: 1..2 digits
//   Quoted literals ('...' / "...") match verbatim; '\x' escapes a single
//   literal character; unknown single letters match ONE literal character
//   each (.NET default case). The token letters h/t/f/F/g/z/Z/K are
//   recognized .NET tokens this subset does not implement -- their formats
//   are rejected (FormatException) rather than misread as literals.
// Errors mirror .NET Framework ParseExact:
//   null input / null format -> ArgumentNullException
//   empty or unsupported format -> FormatException ("Format specifier was
//     invalid.")
//   input that does not match the format, or out-of-range components
//   (month 13, Feb 30, hour 24, year 0, ...) -> FormatException
//     ("String was not recognized as a valid DateTime.")
//
// Parse (il2cpp_datetime_parse) is a documented invariant-culture subset:
//   "YYYY-MM-DD[ T]HH:MM[:SS]" (4-digit year; '-' or '/') and
//   "M/D/YY[YY][ T]HH:MM[:SS]" (month-first; '-' or '/'), 1-2 digit
//   month/day/time fields, US 1-2 digit years expanded via TwoDigitYearMax,
//   leading/trailing whitespace trimmed, leap-day validated. Rejected with
//   FormatException (documented divergences vs net48, which converts
//   timezone offsets to local time and defaults time-only input to today):
//   timezone suffixes ("Z", "+HH:MM"), fractional seconds, compact forms,
//   time-only input, inner whitespace, 5+ digit years.
//
// Tick arithmetic mirrors .NET Framework DateTime.AddTicks: the result must
// stay within [MinValue, MaxValue], otherwise ArgumentOutOfRangeException.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "il2cpp_time.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// DateTime.ParseExact(string input, string format): parses 'input' against
// the format subset above and returns the DateTime value. Throws
// ArgumentNullException / FormatException per the header contract.
Il2CppDateTime il2cpp_datetime_parse_exact(Il2CppObject* input, Il2CppObject* format);

// DateTime.Parse(string input): parses 'input' against the documented
// invariant-culture subset above and returns the DateTime value. Throws
// ArgumentNullException / FormatException per the header contract. (Not yet
// wired in MethodMap.TimeExt2.cs -- transpiled code reaches it once the
// mapping lands.)
Il2CppDateTime il2cpp_datetime_parse(Il2CppObject* input);

// TimeSpan.Parse(string input) / TryParse(string, out TimeSpan): the net48
// invariant "c" grammar (whitespace, optional sign, d.hh:mm:ss.fffffff with
// the slash-tolerant shorter forms; a lone number is days). Parse throws
// ArgumentNullException/FormatException/OverflowException; TryParse is the
// no-throw twin (returns false for null/format/overflow).
Il2CppTimeSpan il2cpp_timespan_parse(Il2CppString* input);
bool il2cpp_timespan_try_parse(Il2CppString* input, Il2CppTimeSpan* value);

// DateTime.ToString(string format) -- the ext2 entry point. Distinct from
// the plain icall_datetime_to_string ("G"); delegates to the canonical
// formatter icall_datetime_to_string_format (il2cpp_time_ext.h), which
// implements the full standard/custom subset pinned by the timeext oracle.
Il2CppObject* il2cpp_datetime_to_string_format(Il2CppDateTime dt, Il2CppObject* format);

// DateTime.AddTicks(long ticks): a new DateTime 'ticks' ahead (100 ns
// units). ArgumentOutOfRangeException when the result leaves
// [MinValue, MaxValue]. Accepts negative ticks.
Il2CppDateTime il2cpp_datetime_add_ticks(Il2CppDateTime dt, int64_t ticks);

// DateTime - ticks (the inverse of AddTicks; .NET has no SubtractTicks
// overload -- provided as the tick arithmetic companion). Same bounds
// contract as AddTicks.
Il2CppDateTime il2cpp_datetime_subtract_ticks(Il2CppDateTime dt, int64_t ticks);

// DateTime.Compare(DateTime, DateTime): -1 / 0 / +1 (static Compare
// semantics == instance CompareTo; same -1/0/+1 encoding).
int32_t il2cpp_datetime_compare(Il2CppDateTime left, Il2CppDateTime right);

} // namespace runtime
} // namespace il2cpp
