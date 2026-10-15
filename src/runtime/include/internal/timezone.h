#pragma once
//------------------------------------------------------------------------------
// timezone.h -- TimeZone simplification domain (DateTime UTC <-> local).
//
// System.DateTime stores UTC ticks (see il2cpp_time.h: struct
// Il2CppDateTime { int64_t ticks; } -- 100 ns units since 0001-01-01
// 00:00:00 UTC). This domain adds the LOCAL TIMEZONE:
//
//   offset = local wall time - UTC  (seconds, positive east of UTC;
//            e.g. UTC+8 -> +28800, New York winter -> -18000)
//
// The domain is deliberately a SIMPLIFICATION: the offset is evaluated at
// the CURRENT instant and applied to every input. There is no historical
// or per-date zone table, so converting dates far from "now" (across a DST
// transition) uses today's offset -- the caller accepts this for the
// simplified layer. Round-trips and near-now conversions are exact.
//
// Platform strategy:
//   - Windows : GetTimeZoneInformation. effective bias = Bias + (Daylight
//     Bias if the returned state is TIME_ZONE_ID_DAYLIGHT else
//     StandardBias); offset = -(bias) * 60 (MSDN: UTC = local + bias).
//     Daylight-saving state comes from the return code itself.
//   - POSIX   : localtime_r / gmtime_r on the SAME time_t instant -- the
//     field difference IS the offset at that instant (no mktime / DST
//     ambiguity), and tm_isdst is the daylight-saving state. This is the
//     pattern already pinned by time.cpp's clock implementation.
//
// Failure contract (mirrors time.cpp:220): if the platform lookup fails,
// the offset is 0 (UTC) -- a clock/zone call must never crash.
//
// The offset-query logic is duplicated from time.cpp's private
// LocalUtcOffsetSeconds by design: this domain is a separate translation
// unit with its own include contract (src/timezone.cpp), and the runtime
// exposes no public offset query to reuse.
//------------------------------------------------------------------------------

#include "il2cpp_time.h"

#include "il2cpp_object.h" // Il2CppObject (zone handle)
#include "il2cpp_string.h" // Il2CppString (zone id)

#include <cstdint>

namespace il2cpp {
namespace runtime {

// The local timezone offset at the CURRENT instant, in seconds (positive
// east of UTC). Windows: GetTimeZoneInformation; POSIX: localtime_r /
// gmtime_r field difference. Falls back to 0 (UTC) if the lookup fails.
int64_t il2cpp_timezone_get_utc_offset();

//------------------------------------------------------------------------------
// Per-date evaluation (net48 GetUtcOffset/IsDaylightSavingTime/IsInvalidTime
// semantics -- the current-instant surface above is the simplification that
// the per-date family replaces for the INSTANCE DateTime APIs).
//
// Data source (Windows): the SAME registry the .NET Framework reads --
// HKLM\...\Time Zones\<id>\TZI (the base rule) plus the optional "Dynamic
// DST" subkey (FirstEntry/LastEntry + per-year REG_TZI_FORMAT entries).
// net48 builds AdjustmentRule[]: one rule spanning [1..first] (the
// FIRST dynamic entry), one per in-range year, one spanning [last..9999]
// (the LAST dynamic entry); no dynamic subkey -> a single rule over
// all years from the static TZI. The evaluator replicates that model, so a
// DST-zone machine reproduces net48 byte-for-byte (verified against the
// Eastern Standard Time registry data with the net48-measured oracle).
// POSIX: localtime_r/gmtime_r at the instant (system tzdata, DST-aware).
//
// The WALL-vs-UTC semantics encode net48's measured contract:
//   * Unspecified/Local input (the DateTime's CIVIL face is a local wall
//     clock): the DST window is [startUtc + dstDelta, endUtc) on the
//     candidate instant (wall - standardOffset).
//   * Utc input (the ticks ARE a UTC instant): the DST window is
//     [startUtc, endUtc) directly.
//   * IsInvalidTime: true iff the wall clock falls in the non-existent
//     spring-forward hour [startWall, startWall + dstDelta).
//   * IsDaylightSavingTime: true iff the per-date offset is the daylight
//     offset.
// A no-DST zone (this host: UTC+8 China Standard Time) reduces everything
// to a constant -- byte-exact with net48 and with the current-instant
// surface. Falls back to the current offsets on failure.
//------------------------------------------------------------------------------
int64_t il2cpp_timezone_get_utc_offset_at(Il2CppDateTime dt);

// The obsolete System.TimeZone.GetUtcOffset(DateTime) wrapper (net48
// TimeZoneInfoWrapper): a Kind=Utc input returns TimeSpan.Zero, otherwise
// per-date (Kind-aware -- the legacy API zeros the UTC input).
int64_t il2cpp_timezone_get_utc_offset_legacy(Il2CppDateTime dt);

// net48 TimeZoneInfo.IsDaylightSavingTime(DateTime): per-date 0/1.
int32_t il2cpp_timezone_get_is_daylight_saving_at(Il2CppDateTime dt);

// net48 TimeZoneInfo.IsInvalidTime(DateTime): 1 when the wall clock falls in
// the spring-forward gap (always 0 on a no-DST host).
int32_t il2cpp_timezone_get_is_invalid_time(Il2CppDateTime dt);

// DateTime (UTC ticks) -> local wall time: utc.ticks + offset. The result
// is clamped to [DateTime.MinValue, DateTime.MaxValue] so conversions of
// the extreme values never produce invalid ticks.
Il2CppDateTime il2cpp_datetime_to_local(Il2CppDateTime utc);

// Local wall time -> DateTime (UTC ticks): local.ticks - offset. Clamped
// to [DateTime.MinValue, DateTime.MaxValue] like to_local.
Il2CppDateTime il2cpp_datetime_from_local(Il2CppDateTime local);

// 1 if daylight saving time is in effect at the CURRENT instant, else 0.
// Windows: TIME_ZONE_ID_DAYLIGHT; on registry failure (TIME_ZONE_ID_INVALID)
// the CRT's tm_isdst answers instead -- symmetric with the offset function's
// CRT fallback, 0 if that also fails. POSIX: tm_isdst > 0. Returns 0 when the
// zone has no DST or the state cannot be determined.
int32_t il2cpp_timezone_get_is_daylight_saving();

//------------------------------------------------------------------------------
// Minimal TimeZoneInfo subset (tzimpl, timezone_registry.cpp): a CLOSED zone
// registry + per-date DST conversion for MethodMap.TimeZone.cs
// FindSystemTimeZoneById / ConvertTimeFromUtc.
//
// Supported ids (exactly): "Eastern Standard Time" (UTC-5), "Pacific
// Standard Time" (UTC-8), "Central Standard Time" (UTC-6) -- all with the US
// 2007+ DST rules (DST starts the 2nd Sunday of March 02:00 local standard,
// ends the 1st Sunday of November 02:00 local daylight) -- and "UTC" (no
// DST, all years). find_by_id returns a STABLE handle (address of a static
// zone entry reinterpreted as Il2CppObject*; never null, never GC-traced).
//
// LOUD boundaries: an unknown id throws NotSupportedException carrying the
// id; a DST-zone instant before 2007 throws NotSupportedException carrying
// the id (the historical rule tables are not modelled -- never a silent
// wrong offset). convert_from_utc applies the per-date offset to the UTC
// ticks and returns a DateTime with Kind == Unspecified (the net48 shape).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_timezone_find_by_id(Il2CppString* id);
Il2CppDateTime il2cpp_timezone_convert_from_utc(Il2CppDateTime utc, Il2CppObject* tz);

} // namespace runtime
} // namespace il2cpp
