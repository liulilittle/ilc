using System;
namespace Cases { public static class TimeZoneCase {
    public static void Main() {
        // timezone.h simplification surface probes (MethodMap.TimeZone.cs):
        //   L1 Local.GetUtcOffset(UtcNow).TotalMinutes -- machine offset
        //      (Local branch: tzsec cast -> icall_timespan_from_ticks(
        //      il2cpp_timezone_get_utc_offset() * 10^7); UTC+8 -> 480)
        //   L2 Utc.GetUtcOffset(UtcNow).TotalMinutes -- 0, never the machine
        //      offset (Utc branch: PrependZeroArg)
        //   L3 CurrentTimeZone.GetUtcOffset(Now).TotalMinutes -- the old
        //      net48 TimeZone API (Kind-aware: a Utc input returns 0, so
        //      the probe passes DateTime.Now -- the Local Kind -- which
        //      equals the machine offset, == TimeZoneInfo.Local)
        //   L4 CurrentTimeZone.ToLocalTime(UtcNow) vs Now -- within 2 s
        //      (the net48 TimeZone virtual converter; TimeZoneInfo's own
        //      ToLocalTime(DateTime) is a netcore-only API -- the mapping is
        //      a dead path under net48, same icalls; Duration avoided -- an
        //      unmapped TimeSpan-domain member, its VALUETYPE result would
        //      downgrade the shared loc0 slot to int32_t)
        //   L5 CurrentTimeZone.ToUniversalTime(Now) vs UtcNow -- within 2 s
        //   L6 Local.IsDaylightSavingTime(Now) -- current-instant probe
        //   L7 chained member on the converter result (.Year resolves via
        //      ReturnTypeName "System.DateTime")
        Console.WriteLine(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes);
        Console.WriteLine(TimeZoneInfo.Utc.GetUtcOffset(DateTime.UtcNow).TotalMinutes);
        Console.WriteLine(TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now).TotalMinutes);
        Console.WriteLine((TimeZone.CurrentTimeZone.ToLocalTime(DateTime.UtcNow) - DateTime.Now).TotalSeconds < 2.0);
        Console.WriteLine((TimeZone.CurrentTimeZone.ToUniversalTime(DateTime.Now) - DateTime.UtcNow).TotalSeconds < 2.0);
        Console.WriteLine(TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now));
        Console.WriteLine(TimeZone.CurrentTimeZone.ToLocalTime(DateTime.UtcNow).Year == DateTime.Now.Year);
    }
}}
