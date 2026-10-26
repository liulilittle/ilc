//------------------------------------------------------------------------------
// calendar.cpp -- System.Globalization.GregorianCalendar runtime support
// (see il2cpp_calendar.h).
//
// The Gregorian calendar is the proleptic Gregorian calendar over the
// DateTime range (year 1..9999). All date math is Howard Hinnant's
// days-from-civil / civil-from-days (the same civil-date algorithms the
// .NET Framework's GregorianCalendar math reduces to): a date is converted
// to a day number (days since 1970-01-01), the day number is converted back
// to a civil date, and the day-of-week is the day number mod 7 (1970-01-01
// was a Thursday = 4 in the .NET DayOfWeek encoding, Sunday = 0).
//
// The class descriptor System_Globalization_GregorianCalendar_class is a
// GLOBAL (the transpiler emits &System_Globalization_GregorianCalendar_class
// for newobj/isinst/castclass -- the System_Version_class contract). The
// calendar is stateless: instanceSize is exactly the object header and
// refFieldBitmap is 0 (no reference fields, the collector never scans past
// the header).
//------------------------------------------------------------------------------

#include "il2cpp_calendar.h"

#include "il2cpp_runtime.h" // il2cpp_class_register

#include <cstdint>

//------------------------------------------------------------------------------
// Civil-date math (Howard Hinnant's algorithms).
//------------------------------------------------------------------------------
namespace {

constexpr int64_t kTicksPerDay = 864000000000LL; // 86400 s * 10^7 ticks/s

// Days since 1970-01-01 for a civil date (proleptic Gregorian). The
// algorithm is valid for the full DateTime year range (1..9999).
int64_t DaysFromCivil(int32_t y, int32_t m, int32_t d)
{
    y -= m <= 2;
    const int64_t era = (y >= 0 ? y : y - 399) / 400;
    const uint32_t yoe = static_cast<uint32_t>(y - era * 400);              // [0, 399]
    const uint32_t doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;    // [0, 365]
    const uint32_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;             // [0, 146096]
    return era * 146097 + static_cast<int64_t>(doe) - 719468;
}

// Inverse of DaysFromCivil: the civil date (y/m/d) for a day number.
void CivilFromDays(int64_t z, int32_t& y, int32_t& m, int32_t& d)
{
    z += 719468;
    const int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const uint32_t doe = static_cast<uint32_t>(z - era * 146097);           // [0, 146096]
    const uint32_t yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365; // [0, 399]
    const int64_t y2 = static_cast<int64_t>(yoe) + era * 400;
    const uint32_t doy = doe - (365 * yoe + yoe / 4 - yoe / 100);           // [0, 365]
    const uint32_t mp = (5 * doy + 2) / 153;                                // [0, 11]
    d = static_cast<int32_t>(doy - (153 * mp + 2) / 5 + 1);                 // [1, 31]
    m = static_cast<int32_t>(mp + (mp < 10 ? 3 : -9));                      // [1, 12]
    y = static_cast<int32_t>(y2 + (m <= 2));
}

// Gregorian leap rule: divisible by 4, except centuries, except every 400
// years.
bool IsLeap(int32_t year)
{
    return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
}

// Days in a month (1..12); February 29 in a leap year.
int32_t DaysInMonth(int32_t year, int32_t month)
{
    static const int32_t kDays[12] = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    if (month == 2 && IsLeap(year))
    {
        return 29;
    }
    return kDays[month - 1];
}

// .NET DayOfWeek (Sunday = 0 .. Saturday = 6) from a day number: 1970-01-01
// (day 0) was a Thursday (4). Floor-mod so pre-1970 day numbers stay valid.
int32_t DayOfWeekFromDays(int64_t days)
{
    int64_t r = (days + 4) % 7;
    if (r < 0)
    {
        r += 7;
    }
    return static_cast<int32_t>(r);
}

// The day number of a DateTime value (ticks since 0001-01-01), expressed in
// the 1970-epoch day count that DaysFromCivil / CivilFromDays /
// DayOfWeekFromDays use. Valid DateTimes have ticks >= 0, so truncation
// toward zero is the floor; the 719162-day offset converts the .NET epoch
// (0001-01-01) to the Unix epoch (1970-01-01) -- the same offset time.cpp
// uses (kUnixEpochTicks = 719162 days).
int64_t Days1970FromTicks(int64_t ticks)
{
    return ticks / kTicksPerDay - 719162;
}

} // namespace

//------------------------------------------------------------------------------
// Class descriptor (GLOBAL scope -- the transpiler emits
// &System_Globalization_GregorianCalendar_class for newobj/isinst/castclass,
// the System_Version_class contract in versionuri.cpp). The calendar is
// stateless: instanceSize is exactly the object header and refFieldBitmap is
// 0 (no reference fields, the collector never scans past the header).
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_Globalization_GregorianCalendar_class = {
    "System.Globalization.GregorianCalendar",        // name
    static_cast<int32_t>(sizeof(::il2cpp::Il2CppObject)), // instanceSize: header only (stateless)
    0u,                                              // refFieldBitmap: no reference fields
    0,                                               // refFieldCount
    &System_Object_class,                            // baseClass (a calendar IS-A object)
    nullptr,                                         // vtable
    0,                                               // vtableSize
    nullptr,                                         // interfaces
    0,                                               // interfaceCount
};

namespace il2cpp { namespace runtime {

namespace {

bool g_calendarClassRegistered = false;

} // namespace

void il2cpp_calendar_register_classes()
{
    if (g_calendarClassRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Globalization_GregorianCalendar_class);
    g_calendarClassRegistered = true;
}

//------------------------------------------------------------------------------
// int-based queries.
//------------------------------------------------------------------------------
int32_t icall_calendar_is_leap_year(Il2CppObject* self, int32_t year)
{
    (void)self;
    il2cpp_calendar_register_classes();
    return IsLeap(year) ? 1 : 0;
}

int32_t icall_calendar_get_days_in_month(Il2CppObject* self, int32_t year, int32_t month)
{
    (void)self;
    il2cpp_calendar_register_classes();
    return DaysInMonth(year, month);
}

int32_t icall_calendar_get_days_in_year(Il2CppObject* self, int32_t year)
{
    (void)self;
    il2cpp_calendar_register_classes();
    return IsLeap(year) ? 366 : 365;
}

int32_t icall_calendar_get_month(Il2CppObject* self, int32_t year, int32_t dayOfYear)
{
    (void)self;
    il2cpp_calendar_register_classes();
    int32_t remaining = dayOfYear;
    for (int32_t m = 1; m <= 12; ++m)
    {
        const int32_t dim = DaysInMonth(year, m);
        if (remaining <= dim)
        {
            return m;
        }
        remaining -= dim;
    }
    return 12; // unreachable for a valid 1..days-in-year input
}

int32_t icall_calendar_get_day_of_year(Il2CppObject* self, int32_t year, int32_t month, int32_t day)
{
    (void)self;
    il2cpp_calendar_register_classes();
    int32_t doy = 0;
    for (int32_t m = 1; m < month; ++m)
    {
        doy += DaysInMonth(year, m);
    }
    return doy + day;
}

int32_t icall_calendar_get_day_of_month(Il2CppObject* self, int32_t year, int32_t dayOfYear, int32_t month)
{
    (void)self;
    il2cpp_calendar_register_classes();
    int32_t doy = 0;
    for (int32_t m = 1; m < month; ++m)
    {
        doy += DaysInMonth(year, m);
    }
    return dayOfYear - doy;
}

//------------------------------------------------------------------------------
// DateTime-based queries (Il2CppDateTime by value).
//------------------------------------------------------------------------------
int32_t icall_calendar_get_year(Il2CppObject* self, Il2CppDateTime dt)
{
    (void)self;
    il2cpp_calendar_register_classes();
    int32_t y, m, d;
    CivilFromDays(Days1970FromTicks(dt.ticks), y, m, d);
    return y;
}

int32_t icall_calendar_get_month_of(Il2CppObject* self, Il2CppDateTime dt)
{
    (void)self;
    il2cpp_calendar_register_classes();
    int32_t y, m, d;
    CivilFromDays(Days1970FromTicks(dt.ticks), y, m, d);
    return m;
}

int32_t icall_calendar_get_day_of_month_of(Il2CppObject* self, Il2CppDateTime dt)
{
    (void)self;
    il2cpp_calendar_register_classes();
    int32_t y, m, d;
    CivilFromDays(Days1970FromTicks(dt.ticks), y, m, d);
    return d;
}

int32_t icall_calendar_get_day_of_year_of(Il2CppObject* self, Il2CppDateTime dt)
{
    (void)self;
    il2cpp_calendar_register_classes();
    const int64_t days = Days1970FromTicks(dt.ticks);
    int32_t y, m, d;
    CivilFromDays(days, y, m, d);
    const int64_t jan1 = DaysFromCivil(y, 1, 1);
    return static_cast<int32_t>(days - jan1 + 1);
}

int32_t icall_calendar_get_day_of_week(Il2CppObject* self, Il2CppDateTime dt)
{
    (void)self;
    il2cpp_calendar_register_classes();
    return DayOfWeekFromDays(Days1970FromTicks(dt.ticks));
}

// Calendar.ToDateTime(year, month, day, hour, minute, second, millisecond)
// -> DateTime: the Gregorian calendar's civil-date composition (net48: the
// GregorianCalendar's era-1 proleptic Gregorian calendar). The runtime reuses
// the DateTime millisecond ctor's composition (icall_datetime_from_ymdhms_ms,
// il2cpp_time.h) -- the same civil-date math (the component validation and the
// tick composition) with the calendar object's identity ignored (the
// GregorianCalendar is the only calendar model).
Il2CppDateTime icall_calendar_to_datetime(Il2CppObject* self, int32_t year, int32_t month,
                                          int32_t day, int32_t hour, int32_t minute,
                                          int32_t second, int32_t millisecond)
{
    (void)self;
    il2cpp_calendar_register_classes();
    return icall_datetime_from_ymdhms_ms(year, month, day, hour, minute, second, millisecond);
}

}} // namespace il2cpp::runtime
