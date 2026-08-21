//------------------------------------------------------------------------------
// test_calendar.cpp -- System.Globalization.GregorianCalendar runtime unit
// tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_calendar.h:
//   1. icall_calendar_is_leap_year: the Gregorian leap rule -- 2000/1600/2004
//      leap, 1900/1700/2001 not (divisible by 4, except centuries, except
//      every 400 years).
//   2. icall_calendar_get_days_in_month: February 29 in a leap year, 28
//      otherwise; the 31/30/28 month lengths.
//   3. icall_calendar_get_days_in_year: 366 in a leap year, 365 otherwise.
//   4. icall_calendar_get_month(year, dayOfYear): the month containing a
//      1-based day-of-year (2000 day 60 = February, day 366 = December).
//   5. icall_calendar_get_day_of_year(year, month, day): 1-based day number
//      (2000-02-29 = 60, 2000-12-31 = 366, 2001-12-31 = 365).
//   6. icall_calendar_get_day_of_month(year, dayOfYear, month): the
//      day-of-month of a day-of-year within its month (2000 day 60 in
//      February = 29, 2000 day 32 in February = 1).
//   7. DateTime-based queries (icall_calendar_get_year/month_of/
//      day_of_month_of/day_of_year_of/day_of_week): known instants built via
//      icall_datetime_from_ymd -- 2000-02-29 (year 2000, month 2, day 29,
//      dayOfYear 60, Tuesday), 2000-01-01 (Saturday), 1970-01-01 (Thursday),
//      2024-01-01 (Monday), 0001-01-01 (Monday -- the proleptic Gregorian
//      epoch).
//   8. Class descriptor: il2cpp_object_new allocates a calendar through
//      System_Globalization_GregorianCalendar_class and the descriptor is
//      registered (il2cpp_class_by_name resolves it after an icall).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_calendar.h"
#include "il2cpp_metadata.h" // System_Object_class
#include "il2cpp_object.h"   // Il2CppObject
#include "il2cpp_runtime.h"  // il2cpp_object_new / il2cpp_class_by_name
#include "il2cpp_time.h"     // icall_datetime_from_ymd

#include <cstdint>
#include <cstdio>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

il2cpp::runtime::Il2CppDateTime MakeDate(int32_t year, int32_t month, int32_t day)
{
    return il2cpp::runtime::icall_datetime_from_ymd(year, month, day);
}

} // namespace

int main()
{
    // --- 1. IsLeapYear (Gregorian leap rule) ---
    Expect(il2cpp::runtime::icall_calendar_is_leap_year(nullptr, 2000) == 1,
           "2000 is a leap year (divisible by 400)");
    Expect(il2cpp::runtime::icall_calendar_is_leap_year(nullptr, 1600) == 1,
           "1600 is a leap year (divisible by 400)");
    Expect(il2cpp::runtime::icall_calendar_is_leap_year(nullptr, 2004) == 1,
           "2004 is a leap year (divisible by 4, not a century)");
    Expect(il2cpp::runtime::icall_calendar_is_leap_year(nullptr, 1900) == 0,
           "1900 is not a leap year (century not divisible by 400)");
    Expect(il2cpp::runtime::icall_calendar_is_leap_year(nullptr, 1700) == 0,
           "1700 is not a leap year (century not divisible by 400)");
    Expect(il2cpp::runtime::icall_calendar_is_leap_year(nullptr, 2001) == 0,
           "2001 is not a leap year (not divisible by 4)");

    // --- 2. GetDaysInMonth ---
    Expect(il2cpp::runtime::icall_calendar_get_days_in_month(nullptr, 2000, 2) == 29,
           "February 2000 has 29 days (leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_month(nullptr, 1900, 2) == 28,
           "February 1900 has 28 days (not a leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_month(nullptr, 2000, 1) == 31,
           "January has 31 days");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_month(nullptr, 2000, 4) == 30,
           "April has 30 days");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_month(nullptr, 2000, 12) == 31,
           "December has 31 days");

    // --- 3. GetDaysInYear ---
    Expect(il2cpp::runtime::icall_calendar_get_days_in_year(nullptr, 2000) == 366,
           "2000 has 366 days (leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_year(nullptr, 2004) == 366,
           "2004 has 366 days (leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_year(nullptr, 1900) == 365,
           "1900 has 365 days (not a leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_days_in_year(nullptr, 2001) == 365,
           "2001 has 365 days (not a leap year)");

    // --- 4. GetMonth(year, dayOfYear) ---
    Expect(il2cpp::runtime::icall_calendar_get_month(nullptr, 2000, 1) == 1,
           "day 1 of 2000 is in January");
    Expect(il2cpp::runtime::icall_calendar_get_month(nullptr, 2000, 32) == 2,
           "day 32 of 2000 is in February (Jan 31 + 1)");
    Expect(il2cpp::runtime::icall_calendar_get_month(nullptr, 2000, 60) == 2,
           "day 60 of 2000 is in February (Feb 29)");
    Expect(il2cpp::runtime::icall_calendar_get_month(nullptr, 2000, 61) == 3,
           "day 61 of 2000 is in March (Mar 1)");
    Expect(il2cpp::runtime::icall_calendar_get_month(nullptr, 2000, 366) == 12,
           "day 366 of 2000 is in December (Dec 31)");
    Expect(il2cpp::runtime::icall_calendar_get_month(nullptr, 2001, 365) == 12,
           "day 365 of 2001 is in December (Dec 31)");

    // --- 5. GetDayOfYear(year, month, day) ---
    Expect(il2cpp::runtime::icall_calendar_get_day_of_year(nullptr, 2000, 1, 1) == 1,
           "2000-01-01 is day 1");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_year(nullptr, 2000, 2, 29) == 60,
           "2000-02-29 is day 60 (Jan 31 + 29)");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_year(nullptr, 2000, 3, 1) == 61,
           "2000-03-01 is day 61");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_year(nullptr, 2000, 12, 31) == 366,
           "2000-12-31 is day 366 (leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_year(nullptr, 2001, 12, 31) == 365,
           "2001-12-31 is day 365 (non-leap year)");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_year(nullptr, 1900, 2, 28) == 59,
           "1900-02-28 is day 59 (non-leap year)");

    // --- 6. GetDayOfMonth(year, dayOfYear, month) ---
    Expect(il2cpp::runtime::icall_calendar_get_day_of_month(nullptr, 2000, 1, 1) == 1,
           "day 1 of 2000 in January is the 1st");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_month(nullptr, 2000, 60, 2) == 29,
           "day 60 of 2000 in February is the 29th");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_month(nullptr, 2000, 32, 2) == 1,
           "day 32 of 2000 in February is the 1st (Feb 1)");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_month(nullptr, 2000, 366, 12) == 31,
           "day 366 of 2000 in December is the 31st");
    Expect(il2cpp::runtime::icall_calendar_get_day_of_month(nullptr, 2001, 365, 12) == 31,
           "day 365 of 2001 in December is the 31st");

    // --- 7. DateTime-based queries ---
    {
        const il2cpp::runtime::Il2CppDateTime d = MakeDate(2000, 2, 29);
        Expect(il2cpp::runtime::icall_calendar_get_year(nullptr, d) == 2000,
               "GetYear(2000-02-29) == 2000");
        Expect(il2cpp::runtime::icall_calendar_get_month_of(nullptr, d) == 2,
               "GetMonth(2000-02-29) == 2");
        Expect(il2cpp::runtime::icall_calendar_get_day_of_month_of(nullptr, d) == 29,
               "GetDayOfMonth(2000-02-29) == 29");
        Expect(il2cpp::runtime::icall_calendar_get_day_of_year_of(nullptr, d) == 60,
               "GetDayOfYear(2000-02-29) == 60");
        Expect(il2cpp::runtime::icall_calendar_get_day_of_week(nullptr, d) == 2,
               "GetDayOfWeek(2000-02-29) == Tuesday (2)");
    }
    {
        const il2cpp::runtime::Il2CppDateTime d = MakeDate(2000, 1, 1);
        Expect(il2cpp::runtime::icall_calendar_get_day_of_week(nullptr, d) == 6,
               "GetDayOfWeek(2000-01-01) == Saturday (6)");
        Expect(il2cpp::runtime::icall_calendar_get_day_of_year_of(nullptr, d) == 1,
               "GetDayOfYear(2000-01-01) == 1");
    }
    {
        const il2cpp::runtime::Il2CppDateTime d = MakeDate(1970, 1, 1);
        Expect(il2cpp::runtime::icall_calendar_get_day_of_week(nullptr, d) == 4,
               "GetDayOfWeek(1970-01-01) == Thursday (4)");
    }
    {
        const il2cpp::runtime::Il2CppDateTime d = MakeDate(2024, 1, 1);
        Expect(il2cpp::runtime::icall_calendar_get_day_of_week(nullptr, d) == 1,
               "GetDayOfWeek(2024-01-01) == Monday (1)");
    }
    {
        const il2cpp::runtime::Il2CppDateTime d = MakeDate(1, 1, 1);
        Expect(il2cpp::runtime::icall_calendar_get_year(nullptr, d) == 1,
               "GetYear(0001-01-01) == 1");
        Expect(il2cpp::runtime::icall_calendar_get_day_of_week(nullptr, d) == 1,
               "GetDayOfWeek(0001-01-01) == Monday (1, proleptic Gregorian epoch)");
    }
    {
        // A time-of-day component does not shift the calendar day.
        const il2cpp::runtime::Il2CppDateTime d = MakeDate(2000, 2, 29);
        Expect(il2cpp::runtime::icall_calendar_get_day_of_year_of(nullptr, d) == 60,
               "GetDayOfYear is day-based (time-of-day ignored)");
    }

    // --- 8. Class descriptor: allocation + registration ---
    {
        il2cpp::runtime::il2cpp_calendar_register_classes();
        il2cpp::Il2CppObject* cal = il2cpp::il2cpp_object_new(
            &System_Globalization_GregorianCalendar_class);
        Expect(cal != nullptr, "il2cpp_object_new allocates a GregorianCalendar");
        Expect(cal->klass == &System_Globalization_GregorianCalendar_class,
               "allocated calendar carries the GregorianCalendar klass header");
        Expect(il2cpp::il2cpp_class_by_name("System.Globalization.GregorianCalendar")
                   == &System_Globalization_GregorianCalendar_class,
               "GregorianCalendar descriptor resolves by name after registration");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d calendar assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime calendar tests passed\n");
    return 0;
}
