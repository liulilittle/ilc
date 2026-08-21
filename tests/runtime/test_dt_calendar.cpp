//------------------------------------------------------------------------------
// test_dt_calendar.cpp -- System.DateTime calendar helper unit tests.
//
// Coverage (normal + edge + error paths):
//   1. IsLeapYear Gregorian rule: 2000 (divisible by 400 -> leap),
//      1900 (divisible by 100 but not 400 -> common), 2024 (divisible by 4
//      -> leap), 2023 (not divisible by 4 -> common).
//   2. DaysInMonth full table: all 12 months of a common year (2023) against
//      the canonical 31/28/31/30/31/30/31/31/30/31/30/31 layout, and the
//      leap-year February shift (2024 -> 29) with every other month pinned
//      unchanged.
//   3. DaysInMonth x IsLeapYear agreement on the rule years: February length
//      tracks the leap verdict for 2000/1900/2024/2023.
//   4. Validation error paths: month 0 and month 13 throw
//      ArgumentOutOfRangeException.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_time.h"

#include "il2cpp_exception.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

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

// Runs fn; returns true iff it throws a DotNetException whose type chain
// contains 'typeFragment' (e.g. "System.ArgumentOutOfRangeException").
template <typename Fn>
bool ThrowsWithType(Fn fn, const char* typeFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeFragment) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

} // namespace

int main()
{
    // --- 1. IsLeapYear: the Gregorian rule years ---
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(2000) == 1,
           "IsLeapYear(2000) == 1 (divisible by 400)");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(1900) == 0,
           "IsLeapYear(1900) == 0 (divisible by 100, not by 400)");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(2024) == 1,
           "IsLeapYear(2024) == 1 (divisible by 4)");
    Expect(il2cpp::runtime::icall_datetime_is_leap_year(2023) == 0,
           "IsLeapYear(2023) == 0 (not divisible by 4)");

    // --- 2. DaysInMonth: full 12-month table ---
    // Common year (2023): the canonical month lengths.
    const int32_t kCommon[12] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    bool commonTableOk = true;
    for (int32_t m = 1; m <= 12; ++m)
    {
        if (il2cpp::runtime::icall_datetime_days_in_month(2023, m) != kCommon[m - 1])
        {
            commonTableOk = false;
            break;
        }
    }
    Expect(commonTableOk,
           "DaysInMonth(2023, 1..12) matches the canonical common-year table");
    // Leap year (2024): only February shifts (+1), everything else identical.
    bool leapTableOk = true;
    for (int32_t m = 1; m <= 12; ++m)
    {
        const int32_t expected = (m == 2) ? 29 : kCommon[m - 1];
        if (il2cpp::runtime::icall_datetime_days_in_month(2024, m) != expected)
        {
            leapTableOk = false;
            break;
        }
    }
    Expect(leapTableOk,
           "DaysInMonth(2024, 1..12): only February shifts to 29");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2023, 2) == 28,
           "DaysInMonth(2023, 2) == 28 (common February)");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2024, 2) == 29,
           "DaysInMonth(2024, 2) == 29 (leap February)");

    // --- 3. DaysInMonth x IsLeapYear agreement on the rule years ---
    Expect(il2cpp::runtime::icall_datetime_days_in_month(2000, 2) == 29,
           "DaysInMonth(2000, 2) == 29 (400-year rule February)");
    Expect(il2cpp::runtime::icall_datetime_days_in_month(1900, 2) == 28,
           "DaysInMonth(1900, 2) == 28 (century non-leap February)");

    // --- 4. Validation error paths ---
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_days_in_month(2023, 0); },
                          "System.ArgumentOutOfRangeException"),
           "DaysInMonth month 0 throws ArgumentOutOfRangeException");
    Expect(ThrowsWithType([]() { il2cpp::runtime::icall_datetime_days_in_month(2023, 13); },
                          "System.ArgumentOutOfRangeException"),
           "DaysInMonth month 13 throws ArgumentOutOfRangeException");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d dt_calendar assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime dt_calendar tests passed\n");
    return 0;
}
