//------------------------------------------------------------------------------
// p-timezone-dst.cs -- TimeZone / TimeZoneInfo per-date DST audit probe
// (GN-20 MSCORLIB batch). net48 oracle == transpiled runtime byte-exact.
//
// Audit record: net48's TimeZone.GetUtcOffset(DateTime) /
// TimeZoneInfo.GetUtcOffset(DateTime) / IsDaylightSavingTime(DateTime) /
// IsInvalidTime(DateTime) are PER-DATE (historical + DST-transition aware),
// while the wired runtime surface (timezone.h) historically evaluated ONE
// current-instant offset. This probe pins the observable contract on
// deterministic fixed instants and compares byte-for-byte.
//
// Determinism: every DateTime below is constructed from a fixed calendar
// date (no DateTime.Now / UtcNow), so the oracle and the runtime print
// identical records on any host. The assertion differs by the host zone's
// SupportsDaylightSavingTime: a no-DST machine (this host: UTC+8 "China
// Standard Time") must print constant offsets; a DST machine prints the
// January-vs-July delta. Both are fixed for a fixed year/rule set.
//
// The wire surface under test (MethodMap.TimeZone.cs):
//   * TimeZoneInfo.Local.GetUtcOffset(DateTime)        -- per-date LOCAL offset
//   * TimeZoneInfo.Local.IsDaylightSavingTime(DateTime)-- per-date DST flag
//   * TimeZoneInfo.Local.IsInvalidTime(DateTime)       -- per-date invalid hour
//   * TimeZone.CurrentTimeZone.GetUtcOffset(DateTime)  -- legacy wrapper:
//     net48 returns TimeSpan.Zero for a Kind=Utc input, the local offset
//     otherwise (Kind-aware).
//   * TimeZoneInfo.Utc.GetUtcOffset(DateTime)          -- always zero
//   * the multi-region registry path (FindSystemTimeZoneById +
//     ConvertTimeFromUtc) exercising the US 2007+ DST rules across the 2026
//     spring-forward boundary.
// NOTE: all DateTimes are INLINED at the call sites -- the transpiler's
// value-type ABI cannot carry a DateTime through a user helper method or a
// static readonly field (pinned limitations).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class PTimeZoneDst
    {
        // [id]=value records: consumed by the difftest record map.
        private static void R(string id, object value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }

        public static void Main()
        {
            // --- 1/2. Local GetUtcOffset across the DST calendar span ---
            R("local.winter.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2026, 1, 15, 12, 0, 0)).TotalMinutes);
            R("local.summer.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2026, 7, 15, 12, 0, 0)).TotalMinutes);
            R("local.constant", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2026, 1, 15, 12, 0, 0)) == TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2026, 7, 15, 12, 0, 0)));
            R("local.utckind.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).TotalMinutes);

            // --- legacy TimeZone.GetUtcOffset (Kind-aware in net48: a Utc
            //     input returns TimeSpan.Zero, otherwise the local offset) ---
            R("legacy.winter.min", TimeZone.CurrentTimeZone.GetUtcOffset(
                new DateTime(2026, 1, 15, 12, 0, 0)).TotalMinutes);
            R("legacy.summer.min", TimeZone.CurrentTimeZone.GetUtcOffset(
                new DateTime(2026, 7, 15, 12, 0, 0)).TotalMinutes);
            R("legacy.utckind.min", TimeZone.CurrentTimeZone.GetUtcOffset(
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).TotalMinutes);

            // --- Utc singleton: always zero, never the machine offset ---
            R("utc.winter.min", TimeZoneInfo.Utc.GetUtcOffset(
                new DateTime(2026, 1, 15, 12, 0, 0)).TotalMinutes);
            R("utc.summer.min", TimeZoneInfo.Utc.GetUtcOffset(
                new DateTime(2026, 7, 15, 12, 0, 0)).TotalMinutes);

            // --- 3. IsDaylightSavingTime boundary values ---
            R("isdst.local.winter", TimeZoneInfo.Local.IsDaylightSavingTime(
                new DateTime(2026, 1, 15, 12, 0, 0)));
            R("isdst.local.summer", TimeZoneInfo.Local.IsDaylightSavingTime(
                new DateTime(2026, 7, 15, 12, 0, 0)));
            R("isdst.legacy.winter", TimeZone.CurrentTimeZone.IsDaylightSavingTime(
                new DateTime(2026, 1, 15, 12, 0, 0)));
            R("isdst.legacy.summer", TimeZone.CurrentTimeZone.IsDaylightSavingTime(
                new DateTime(2026, 7, 15, 12, 0, 0)));

            // --- 4. IsInvalidTime (the spring-forward non-existent hour; on
            //        a no-DST host always false -- net48 never throws) ---
            R("isinvalid.local.winter", TimeZoneInfo.Local.IsInvalidTime(
                new DateTime(2026, 1, 15, 12, 0, 0)));
            R("isinvalid.local.summer", TimeZoneInfo.Local.IsInvalidTime(
                new DateTime(2026, 7, 15, 12, 0, 0)));

            // --- 6. historical instants: a no-DST machine must be constant ---
            R("hist.2000.jan.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2000, 1, 15, 12, 0, 0)).TotalMinutes);
            R("hist.2000.jul.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2000, 7, 15, 12, 0, 0)).TotalMinutes);
            R("hist.2020.jan.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2020, 1, 15, 12, 0, 0)).TotalMinutes);
            R("hist.2020.jul.min", TimeZoneInfo.Local.GetUtcOffset(
                new DateTime(2020, 7, 15, 12, 0, 0)).TotalMinutes);

            // --- 5. multi-region via the registry (US 2007+ DST rules,
            //        Eastern Standard Time, 2026 spring-forward boundary:
            //        02:00 EST -> 03:00 EDT on 03-08, i.e. 07:00Z) ---
            R("reg.e.winter.h", TimeZoneInfo.ConvertTimeFromUtc(
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).ToString("HH"));
            R("reg.e.summer.h", TimeZoneInfo.ConvertTimeFromUtc(
                new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).ToString("HH"));
            R("reg.e.boundary1.h", TimeZoneInfo.ConvertTimeFromUtc(
                new DateTime(2026, 3, 8, 6, 59, 59, DateTimeKind.Utc),
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).ToString("HH:mm:ss"));
            R("reg.e.boundary2.h", TimeZoneInfo.ConvertTimeFromUtc(
                new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc),
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).ToString("HH:mm:ss"));
            R("reg.utc.hour", TimeZoneInfo.ConvertTimeFromUtc(
                new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
                TimeZoneInfo.FindSystemTimeZoneById("UTC")).Hour);
        }
    }
}