//------------------------------------------------------------------------------
// test_dtoffset.cpp -- System.DateTimeOffset runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_dtoffset.h:
//   1. icall_dtoffset_ctor: a local time + offset produces the right UTC
//      instant; the accessors read the local time, the UTC instant and the
//      offset back.
//   2. icall_dtoffset_ctor_ymd: component construction (local time from
//      parts + offset) matches the (DateTime, TimeSpan) ctor.
//   3. Component accessors (Year/Month/Day/Hour/Minute/Second) read the
//      LOCAL time (a +08:00 offset shifts the UTC date).
//   4. Negative offsets (e.g. -05:30) round-trip.
//   5. icall_dtoffset_compare / equals: two offsets of the SAME UTC instant
//      compare equal and are equal (net48 semantics); different instants
//      order by UTC.
//   6. icall_dtoffset_to_string: the net48 default -- the selected
//      culture's General "G" pattern plus " zzz" (host-default zh-CN
//      "2000/2/29 12:00:00 +08:00", "-05:30" for a negative offset).
//   7. Offset validation errors: a non-whole-minute offset and an offset
//      outside [-14:00, +14:00] throw ArgumentOutOfRangeException; the
//      boundary offsets (+14:00 / -14:00) are accepted.
//   8. icall_dtoffset_to_string_format: invariant/zh-CN standard patterns,
//      explicit "G" without the offset, o/O round trip, u/r UTC conversion,
//      "U"/unknown-letter FormatException, custom z/zz/zzz/K and '%' tokens,
//      null/empty format + provider carriers (the dtoffsetfmt oracle).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_dtoffset.h"
#include "il2cpp_exception.h" // DotNetException
#include "il2cpp_string.h"    // Il2CppString

#include <cstdint>
#include <cstdio>
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

il2cpp::runtime::Il2CppDateTime MakeDate(int32_t year, int32_t month, int32_t day,
                                         int32_t hour, int32_t minute, int32_t second)
{
    return il2cpp::runtime::icall_datetime_from_ymdhms(
        year, month, day, hour, minute, second);
}

il2cpp::runtime::Il2CppTimeSpan MakeSpan(int32_t hours, int32_t minutes)
{
    return il2cpp::runtime::icall_timespan_from_hms(hours, minutes, 0);
}

// The managed string's UTF-16 chars equal the ASCII literal.
bool TextEquals(il2cpp::Il2CppObject* s, const char* expected)
{
    auto* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(expected);
    if (str == nullptr || str->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(expected[i]))
        {
            return false;
        }
    }
    return true;
}

// ToString(format, provider) with ASCII carriers (nullptr = absent).
il2cpp::Il2CppObject* Fmt(const il2cpp::runtime::Il2CppDateTimeOffset& dto,
                          const char* format, const char* provider)
{
    return il2cpp::runtime::icall_dtoffset_to_string_format(
        dto,
        format == nullptr ? nullptr
                          : reinterpret_cast<il2cpp::Il2CppObject*>(
                                il2cpp::il2cpp_string_new_from_cs(format)),
        provider == nullptr ? nullptr
                            : reinterpret_cast<il2cpp::Il2CppObject*>(
                                  il2cpp::il2cpp_string_new_from_cs(provider)));
}

} // namespace

int main()
{
    using il2cpp::runtime::Il2CppDateTimeOffset;

    // --- 1. ctor(DateTime, TimeSpan): local + offset -> UTC ---
    {
        Il2CppDateTimeOffset dto;
        il2cpp::runtime::icall_dtoffset_ctor(
            &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(8, 0));
        // Local time is 2000-02-29 12:00:00; UTC is 04:00:00; offset +08:00.
        Expect(il2cpp::runtime::icall_dtoffset_get_year(dto) == 2000,
               "GetYear reads the local year");
        Expect(il2cpp::runtime::icall_dtoffset_get_month(dto) == 2,
               "GetMonth reads the local month");
        Expect(il2cpp::runtime::icall_dtoffset_get_day(dto) == 29,
               "GetDay reads the local day");
        Expect(il2cpp::runtime::icall_dtoffset_get_hour(dto) == 12,
               "GetHour reads the local hour");
        Expect(il2cpp::runtime::icall_dtoffset_get_minute(dto) == 0,
               "GetMinute reads the local minute");
        Expect(il2cpp::runtime::icall_dtoffset_get_second(dto) == 0,
               "GetSecond reads the local second");
        const il2cpp::runtime::Il2CppDateTime local = il2cpp::runtime::icall_dtoffset_get_datetime(dto);
        Expect(il2cpp::runtime::icall_datetime_get_year(local) == 2000
               && il2cpp::runtime::icall_datetime_get_month(local) == 2
               && il2cpp::runtime::icall_datetime_get_day(local) == 29
               && il2cpp::runtime::icall_datetime_get_hour(local) == 12,
               "DateTime reads the local time");
        const il2cpp::runtime::Il2CppDateTime utc = il2cpp::runtime::icall_dtoffset_get_utc_datetime(dto);
        Expect(il2cpp::runtime::icall_datetime_get_year(utc) == 2000
               && il2cpp::runtime::icall_datetime_get_month(utc) == 2
               && il2cpp::runtime::icall_datetime_get_day(utc) == 29
               && il2cpp::runtime::icall_datetime_get_hour(utc) == 4,
               "UtcDateTime is local minus the offset (04:00 UTC)");
        const il2cpp::runtime::Il2CppTimeSpan off = il2cpp::runtime::icall_dtoffset_get_offset(dto);
        Expect(il2cpp::runtime::icall_timespan_get_hours(off) == 8
               && il2cpp::runtime::icall_timespan_get_minutes(off) == 0,
               "Offset reads back as +08:00");
    }

    // --- 2. ctor_ymd: component construction matches the (DateTime, TimeSpan) ctor ---
    {
        Il2CppDateTimeOffset a;
        Il2CppDateTimeOffset b;
        il2cpp::runtime::icall_dtoffset_ctor_ymd(&a, 2000, 2, 29, 12, 0, 0, MakeSpan(8, 0));
        il2cpp::runtime::icall_dtoffset_ctor(
            &b, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(8, 0));
        Expect(il2cpp::runtime::icall_dtoffset_equals(a, b),
               "ctor_ymd produces the same instant as ctor(DateTime, TimeSpan)");
        Expect(il2cpp::runtime::icall_dtoffset_get_hour(a) == 12,
               "ctor_ymd local hour is 12");
    }

    // --- 3. Component accessors read the LOCAL time (offset shifts the UTC date) ---
    {
        // 2000-02-29 00:30:00 local with +08:00 -> UTC is 2000-02-28 16:30:00.
        Il2CppDateTimeOffset dto;
        il2cpp::runtime::icall_dtoffset_ctor(
            &dto, MakeDate(2000, 2, 29, 0, 30, 0), MakeSpan(8, 0));
        Expect(il2cpp::runtime::icall_dtoffset_get_day(dto) == 29,
               "local day is 29 (offset does not shift the local date)");
        const il2cpp::runtime::Il2CppDateTime utc = il2cpp::runtime::icall_dtoffset_get_utc_datetime(dto);
        Expect(il2cpp::runtime::icall_datetime_get_day(utc) == 28
               && il2cpp::runtime::icall_datetime_get_hour(utc) == 16,
               "UTC date is the previous day (2000-02-28 16:30 UTC)");
    }

    // --- 4. Negative offset (-05:30) round-trips ---
    {
        Il2CppDateTimeOffset dto;
        il2cpp::runtime::icall_dtoffset_ctor(
            &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(-5, -30));
        Expect(il2cpp::runtime::icall_dtoffset_get_hour(dto) == 12,
               "negative offset local hour is 12");
        const il2cpp::runtime::Il2CppDateTime utc = il2cpp::runtime::icall_dtoffset_get_utc_datetime(dto);
        Expect(il2cpp::runtime::icall_datetime_get_hour(utc) == 17
               && il2cpp::runtime::icall_datetime_get_minute(utc) == 30,
               "negative offset UTC is 17:30 (local + 5:30)");
        const il2cpp::runtime::Il2CppTimeSpan off = il2cpp::runtime::icall_dtoffset_get_offset(dto);
        Expect(il2cpp::runtime::icall_timespan_get_hours(off) == -5
               && il2cpp::runtime::icall_timespan_get_minutes(off) == -30,
               "negative offset reads back as -05:30");
    }

    // --- 5. compare / equals: same UTC instant, different offsets ---
    {
        Il2CppDateTimeOffset a;
        Il2CppDateTimeOffset b;
        // 2000-02-29 12:00:00 +08:00 == 2000-02-29 04:00:00 +00:00 (same UTC).
        il2cpp::runtime::icall_dtoffset_ctor(
            &a, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(8, 0));
        il2cpp::runtime::icall_dtoffset_ctor(
            &b, MakeDate(2000, 2, 29, 4, 0, 0), MakeSpan(0, 0));
        Expect(il2cpp::runtime::icall_dtoffset_equals(a, b),
               "same UTC instant with different offsets is equal");
        Expect(il2cpp::runtime::icall_dtoffset_compare(a, b) == 0,
               "same UTC instant compares equal");
        Il2CppDateTimeOffset c;
        il2cpp::runtime::icall_dtoffset_ctor(
            &c, MakeDate(2000, 2, 29, 5, 0, 0), MakeSpan(0, 0));
        Expect(il2cpp::runtime::icall_dtoffset_compare(a, c) == -1,
               "earlier UTC instant compares less");
        Expect(il2cpp::runtime::icall_dtoffset_compare(c, a) == 1,
               "later UTC instant compares greater");
        Expect(!il2cpp::runtime::icall_dtoffset_equals(a, c),
               "different UTC instants are not equal");
    }

    // --- 6. ToString: the net48 default (culture General + " zzz") ---
    {
        Il2CppDateTimeOffset dto;
        il2cpp::runtime::icall_dtoffset_ctor(
            &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(8, 0));
        Expect(TextEquals(il2cpp::runtime::icall_dtoffset_to_string(dto),
                          "2000/2/29 12:00:00 +08:00"),
               "ToString formats the host-default zh-CN G pattern + positive offset");
        il2cpp::runtime::icall_dtoffset_ctor(
            &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(-5, -30));
        Expect(TextEquals(il2cpp::runtime::icall_dtoffset_to_string(dto),
                          "2000/2/29 12:00:00 -05:30"),
               "ToString formats a negative offset");
        il2cpp::runtime::icall_dtoffset_ctor(
            &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(0, 0));
        Expect(TextEquals(il2cpp::runtime::icall_dtoffset_to_string(dto),
                          "2000/2/29 12:00:00 +00:00"),
               "ToString formats a zero offset as +00:00");
    }

    // --- 7. Offset validation errors ---
    {
        // A non-whole-minute offset throws ArgumentOutOfRangeException.
        Expect(ThrowsWithType([&] {
                   Il2CppDateTimeOffset dto;
                   il2cpp::runtime::icall_dtoffset_ctor(
                       &dto, MakeDate(2000, 2, 29, 12, 0, 0),
                       il2cpp::runtime::Il2CppTimeSpan(600000001LL)); // 10 min + 1 tick
               }, "System.ArgumentOutOfRangeException"),
               "non-whole-minute offset throws ArgumentOutOfRangeException");
        // An offset beyond +14:00 throws.
        Expect(ThrowsWithType([&] {
                   Il2CppDateTimeOffset dto;
                   il2cpp::runtime::icall_dtoffset_ctor(
                       &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(14, 1));
               }, "System.ArgumentOutOfRangeException"),
               "offset beyond +14:00 throws ArgumentOutOfRangeException");
        // An offset beyond -14:00 throws.
        Expect(ThrowsWithType([&] {
                   Il2CppDateTimeOffset dto;
                   il2cpp::runtime::icall_dtoffset_ctor(
                       &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(-14, -1));
               }, "System.ArgumentOutOfRangeException"),
               "offset beyond -14:00 throws ArgumentOutOfRangeException");
        // The boundary offsets (+14:00 / -14:00) are accepted.
        {
            Il2CppDateTimeOffset dto;
            il2cpp::runtime::icall_dtoffset_ctor(
                &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(14, 0));
            Expect(il2cpp::runtime::icall_dtoffset_get_hour(dto) == 12,
                   "+14:00 boundary offset is accepted");
            il2cpp::runtime::icall_dtoffset_ctor(
                &dto, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(-14, 0));
            Expect(il2cpp::runtime::icall_dtoffset_get_hour(dto) == 12,
                   "-14:00 boundary offset is accepted");
        }
    }

    // --- 8. ToString(format[, provider]): the net48 DateTimeOffset contract ---
    {
        Il2CppDateTimeOffset p8;
        Il2CppDateTimeOffset m530;
        il2cpp::runtime::icall_dtoffset_ctor(
            &p8, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(8, 0));
        il2cpp::runtime::icall_dtoffset_ctor(
            &m530, MakeDate(2000, 2, 29, 12, 0, 0), MakeSpan(-5, -30));

        // Standard specifiers: invariant (empty-name carrier) vs zh-CN.
        Expect(TextEquals(Fmt(p8, "d", ""), "02/29/2000"),
               "invariant 'd' short date");
        Expect(TextEquals(Fmt(p8, "d", "zh-CN"), "2000/2/29"),
               "zh-CN 'd' short date");
        Expect(TextEquals(Fmt(p8, "D", ""), "Tuesday, 29 February 2000"),
               "invariant 'D' long date");
        Expect(TextEquals(Fmt(p8, "G", ""), "02/29/2000 12:00:00"),
               "explicit 'G' has NO offset (net48)");
        Expect(TextEquals(Fmt(p8, "g", "zh-CN"), "2000/2/29 12:00"),
               "zh-CN 'g' general short");
        Expect(TextEquals(Fmt(p8, "o", ""), "2000-02-29T12:00:00.0000000+08:00"),
               "'o' round trip: local + 7 fraction digits + offset");
        Expect(TextEquals(Fmt(p8, "O", ""), "2000-02-29T12:00:00.0000000+08:00"),
               "'O' round trip");
        Expect(TextEquals(Fmt(p8, "r", ""), "Tue, 29 Feb 2000 04:00:00 GMT"),
               "'r' formats the UTC instant (12:00+08 -> 04:00Z)");
        Expect(TextEquals(Fmt(p8, "R", ""), "Tue, 29 Feb 2000 04:00:00 GMT"),
               "'R' formats the UTC instant");
        Expect(TextEquals(Fmt(p8, "u", ""), "2000-02-29 04:00:00Z"),
               "'u' formats the UTC instant");
        Expect(ThrowsWithType([&] { (void)Fmt(p8, "U", ""); },
                              "System.FormatException"),
               "DateTimeOffset 'U' throws FormatException (net48)");
        Expect(ThrowsWithType([&] { (void)Fmt(p8, "q", ""); },
                              "System.FormatException"),
               "unknown standard letter throws FormatException");
        Expect(ThrowsWithType([&] { (void)Fmt(p8, "K", ""); },
                              "System.FormatException"),
               "single-letter 'K' is a standard lookup -> FormatException");
        Expect(ThrowsWithType([&] { (void)Fmt(p8, "z", ""); },
                              "System.FormatException"),
               "single-letter 'z' is a standard lookup -> FormatException");

        // Custom offset tokens and the '%' escape (net48 oracle).
        Expect(TextEquals(Fmt(p8, "yyyy-MM-dd HH:mm:ss zzz", ""),
                          "2000-02-29 12:00:00 +08:00"),
               "custom zzz = +HH:mm");
        Expect(TextEquals(Fmt(m530, "yyyy-MM-dd HH:mm:ss zzz", ""),
                          "2000-02-29 12:00:00 -05:30"),
               "custom zzz for a negative half-hour offset");
        Expect(TextEquals(Fmt(p8, "H:mm z", ""), "12:00 +8"),
               "custom z = unpadded hours");
        Expect(TextEquals(Fmt(p8, "H:mm zz", ""), "12:00 +08"),
               "custom zz = 2-digit hours");
        Expect(TextEquals(Fmt(p8, "yyyy K", ""), "2000 +08:00"),
               "custom K = the full +HH:mm offset");
        Expect(TextEquals(Fmt(p8, "%z", ""), "+8"),
               "'%z' forces the custom z token");
        Expect(TextEquals(Fmt(p8, "%K", ""), "+08:00"),
               "'%K' forces the custom K token");

        // Null/empty format + provider selection (the parameterless path).
        Expect(TextEquals(Fmt(p8, nullptr, ""), "02/29/2000 12:00:00 +08:00"),
               "null format + invariant carrier = invariant G + zzz");
        Expect(TextEquals(Fmt(p8, "", ""), "02/29/2000 12:00:00 +08:00"),
               "empty format + invariant carrier = invariant G + zzz");
        Expect(TextEquals(Fmt(p8, nullptr, nullptr), "2000/2/29 12:00:00 +08:00"),
               "null provider selects the host default zh-CN");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d dtoffset assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime dtoffset tests passed\n");
    return 0;
}
