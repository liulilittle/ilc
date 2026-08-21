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

#include <cstdint>

namespace il2cpp {
namespace runtime {

// The local timezone offset at the CURRENT instant, in seconds (positive
// east of UTC). Windows: GetTimeZoneInformation; POSIX: localtime_r /
// gmtime_r field difference. Falls back to 0 (UTC) if the lookup fails.
int64_t il2cpp_timezone_get_utc_offset();

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

} // namespace runtime
} // namespace il2cpp
