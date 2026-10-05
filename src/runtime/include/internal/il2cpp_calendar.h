#pragma once
//------------------------------------------------------------------------------
// il2cpp_calendar.h -- System.Globalization.GregorianCalendar runtime support
// (calendar.cpp).
//
// OBJECT MODEL: a GregorianCalendar is a STATELESS managed object (no fields
// -- the calendar carries no per-instance state; every query is a pure
// function of its arguments). It is allocated through the GLOBAL-scope class
// descriptor System_Globalization_GregorianCalendar_class (defined in
// calendar.cpp, the same contract as System_Version_class in versionuri.cpp:
// the transpiler's BclAllocExpr fallback emits
// il2cpp_object_new(&System_Globalization_GregorianCalendar_class) for
// `new GregorianCalendar()`). instanceSize is exactly the object header and
// refFieldBitmap is 0 -- the collector never scans past the header.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline. The Gregorian calendar is the proleptic Gregorian calendar over
// the DateTime range (year 1..9999):
//   * IsLeapYear(year): divisible by 4, except centuries, except every 400
//     years (2000 leap, 1900 not, 2004 leap).
//   * GetDaysInMonth(year, month): 28..31 (February 29 in a leap year).
//   * GetDaysInYear(year): 365 / 366.
//   * GetMonth(year, dayOfYear) / GetDayOfYear(year, month, day) /
//     GetDayOfMonth(year, dayOfYear, month): the civil-date round-trips
//     (dayOfYear is 1-based; GetDayOfMonth's month argument is the month the
//     dayOfYear falls in -- net48 Calendar contract).
//   * GetYear/GetMonth/GetDayOfMonth/GetDayOfYear/GetDayOfWeek(DateTime):
//     the component queries over a DateTime value (ticks since 0001-01-01).
//     DayOfWeek is the .NET enum encoding: Sunday = 0 .. Saturday = 6.
//
// ALGORITHM BASELINE: Howard Hinnant's days-from-civil / civil-from-days
// (the same civil-date algorithms the .NET Framework's GregorianCalendar
// math reduces to) -- no self-invented date arithmetic.
//
// The DateTime-based icalls take Il2CppDateTime BY VALUE (one register on
// x64, exactly the managed value-type ABI -- see il2cpp_time.h); the
// int-based icalls take plain int32. Every icall takes the managed receiver
// (Il2CppObject* self) as its first parameter -- the transpiler passes the
// calendar object through the "" passthrough cast.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)
#include "il2cpp_time.h"     // Il2CppDateTime (8-byte value)

#include <cstdint>

// System.Globalization.GregorianCalendar runtime descriptor (GLOBAL scope --
// the transpiler emits &System_Globalization_GregorianCalendar_class for
// newobj/isinst/castclass; defined in calendar.cpp).
extern il2cpp::Il2CppClass System_Globalization_GregorianCalendar_class;

namespace il2cpp { namespace runtime {

// Register the GregorianCalendar descriptor once (idempotent --
// il2cpp_class_register dedupes by pointer). Called defensively by the
// icalls before any use so isinst/castclass/typeinfo lookups resolve.
void il2cpp_calendar_register_classes();

// --- int-based queries (net48 GregorianCalendar semantics) -----------------

// IsLeapYear(year): 1 if year is a Gregorian leap year, else 0 (int32 ABI --
// the debuggerext.h bool convention).
int32_t icall_calendar_is_leap_year(Il2CppObject* self, int32_t year);

// GetDaysInMonth(year, month): 28..31 (February 29 in a leap year).
int32_t icall_calendar_get_days_in_month(Il2CppObject* self, int32_t year, int32_t month);

// GetDaysInYear(year): 365 / 366.
int32_t icall_calendar_get_days_in_year(Il2CppObject* self, int32_t year);

// GetMonth(year, dayOfYear): the month (1..12) containing the 1-based
// dayOfYear.
int32_t icall_calendar_get_month(Il2CppObject* self, int32_t year, int32_t dayOfYear);

// GetDayOfYear(year, month, day): the 1-based day number within the year.
int32_t icall_calendar_get_day_of_year(Il2CppObject* self, int32_t year, int32_t month, int32_t day);

// GetDayOfMonth(year, dayOfYear, month): the day-of-month (1..31) of the
// 1-based dayOfYear within the given month (net48 Calendar contract -- the
// month argument is the month the dayOfYear falls in).
int32_t icall_calendar_get_day_of_month(Il2CppObject* self, int32_t year, int32_t dayOfYear, int32_t month);

// --- DateTime-based queries (Il2CppDateTime by value) ----------------------

// GetYear(DateTime): the Gregorian year of the instant.
int32_t icall_calendar_get_year(Il2CppObject* self, Il2CppDateTime dt);

// GetMonth(DateTime): 1..12.
int32_t icall_calendar_get_month_of(Il2CppObject* self, Il2CppDateTime dt);

// GetDayOfMonth(DateTime): 1..31.
int32_t icall_calendar_get_day_of_month_of(Il2CppObject* self, Il2CppDateTime dt);

// GetDayOfYear(DateTime): 1..366.
int32_t icall_calendar_get_day_of_year_of(Il2CppObject* self, Il2CppDateTime dt);

// GetDayOfWeek(DateTime): the .NET DayOfWeek encoding -- Sunday = 0 ..
// Saturday = 6.
int32_t icall_calendar_get_day_of_week(Il2CppObject* self, Il2CppDateTime dt);

}} // namespace il2cpp::runtime
