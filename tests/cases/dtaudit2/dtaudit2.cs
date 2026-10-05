//------------------------------------------------------------------------------
// dtaudit2.cs -- TimeZoneInfo per-instant DST audit probe (G6 audit D3/D5).
//
// Finds the "Eastern Standard Time" zone and converts two UTC instants:
//   * 2020-01-15 12:00Z (EST, UTC-5) -> Hour 7
//   * 2020-07-15 12:00Z (EDT, UTC-4) -> Hour 8
//
// net48 resolves the per-instant offset from the zone's adjustment rules.
// The runtime maps FindSystemTimeZoneById / ConvertTimeFromUtc to LOUD
// rejections (MethodMap.TimeZone.cs:156-167 -- no zone registry), so the
// expected transpile result is a NotSupportedException. The transpile.log
// tail is the D3 defect evidence (per-date DST surface absent).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Dtaudit2 {
    public static void Main()
    {
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Console.WriteLine("winter_hour=" + TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2020, 1, 15, 12, 0, 0, DateTimeKind.Utc), tz).Hour);
        Console.WriteLine("summer_hour=" + TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2020, 7, 15, 12, 0, 0, DateTimeKind.Utc), tz).Hour);
    }
}}
