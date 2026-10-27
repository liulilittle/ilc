using System;

// dtextra2 (impl-tz2): the DateTimeExtra analyzable surface -- Add(TimeSpan) /
// op_Addition / TryParse (both net48 overloads) / Today / ToOADate / FromOADate
// / ToBinary / FromBinary / IsDaylightSavingTime / GetTypeCode. Every record
// is machine-stable on a single host (the net48 oracle and the runtime see the
// same zone/clock), so the difftest byte-compare pins them exactly.
internal static class Program
{
    private static string KindTag(DateTime d)
    {
        return d.Kind == DateTimeKind.Utc ? "Z" : d.Kind == DateTimeKind.Local ? "L" : "U";
    }

    private static int Main()
    {
        // --- Add(TimeSpan) / op_Addition ---
        var base1 = new DateTime(2026, 10, 27, 12, 0, 0);
        var added = base1.Add(new TimeSpan(1, 2, 3));
        Console.WriteLine("[add.ts] " + added.ToString("yyyy-MM-dd HH:mm:ss"));
        var plus = base1 + new TimeSpan(0, 23, 59, 59);
        Console.WriteLine("[add.op] " + plus.ToString("yyyy-MM-dd HH:mm:ss"));
        var minus = base1 - new TimeSpan(1, 0, 0, 0);
        Console.WriteLine("[sub.ts] " + minus.ToString("yyyy-MM-dd HH:mm:ss"));

        // --- TryParse (the no-throw parse twin) ---
        DateTime parsed = DateTime.MaxValue;
        var ok1 = DateTime.TryParse("2026-01-02 03:04:05", out parsed);
        Console.WriteLine("[tryparse.ok] " + ok1 + " " + parsed.ToString("yyyy-MM-dd HH:mm:ss"));
        var ok2 = DateTime.TryParse("not-a-date", out parsed);
        Console.WriteLine("[tryparse.bad] " + ok2 + " " + (parsed == DateTime.MinValue) + " " + parsed.Ticks);
        var ok3 = DateTime.TryParse(null, out parsed);
        Console.WriteLine("[tryparse.null] " + ok3 + " " + (parsed == DateTime.MaxValue));
        var ok4 = DateTime.TryParse("2026-01-02", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out parsed);
        Console.WriteLine("[tryparse.styles] " + ok4 + " " + parsed.ToString("yyyy-MM-dd"));

        // --- Today (net48: Now.Date, Kind=Local) ---
        var today = DateTime.Today;
        Console.WriteLine("[today] " + today.ToString("yyyy-MM-dd") + " " + today.TimeOfDay.Ticks + " " + KindTag(today));

        // --- ToOADate / FromOADate ---
        var oaSource = new DateTime(2026, 10, 27, 0, 0, 0);
        Console.WriteLine("[oa.out] " + oaSource.ToOADate().ToString("F10"));
        var oaBack = DateTime.FromOADate(46322.0); // 2026-10-27 00:00:00
        Console.WriteLine("[oa.in] " + oaBack.ToString("yyyy-MM-dd HH:mm:ss") + " " + KindTag(oaBack));
        var oaEpoch = new DateTime(1899, 12, 30);
        Console.WriteLine("[oa.zero] " + oaEpoch.ToOADate().ToString("F10"));
        var sink = DateTime.MinValue;
        try { sink = DateTime.FromOADate(2958466.0); Console.WriteLine("[oa.max] no-throw " + sink.Ticks); }
        catch (ArgumentException) { Console.WriteLine("[oa.max] ArgumentException"); }
        try { sink = DateTime.FromOADate(-657435.5); Console.WriteLine("[oa.min] " + sink.Ticks); }
        catch (ArgumentException) { Console.WriteLine("[oa.min] ArgumentException"); }
        try { sink = DateTime.FromOADate(double.NaN); Console.WriteLine("[oa.nan] " + sink.Ticks); }
        catch (ArgumentException) { Console.WriteLine("[oa.nan] ArgumentException"); }

        // --- ToBinary / FromBinary (the Local flavor stores UTC ticks) ---
        var localDt = new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Local);
        var bin = localDt.ToBinary();
        var round = DateTime.FromBinary(bin);
        Console.WriteLine("[bin.local] " + bin + " " + round.ToString("yyyy-MM-dd HH:mm:ss") + " " + KindTag(round));
        var utcDt = new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);
        Console.WriteLine("[bin.utc] " + utcDt.ToBinary());
        var unspec = new DateTime(2026, 10, 27, 8, 0, 0);
        Console.WriteLine("[bin.unspec] " + unspec.ToBinary());
        Console.WriteLine("[bin.min] " + DateTime.MinValue.ToBinary());
        Console.WriteLine("[bin.max] " + DateTime.MaxValue.ToBinary());
        Console.WriteLine("[bin.fromzero] " + DateTime.FromBinary(0).Ticks);
        try { sink = DateTime.FromBinary(-1); Console.WriteLine("[bin.bad] no-throw " + sink.Ticks); }
        catch (ArgumentException) { Console.WriteLine("[bin.bad] ArgumentException"); }

        // --- IsDaylightSavingTime (Kind-aware) ---
        Console.WriteLine("[isdst.utc] " + utcDt.IsDaylightSavingTime());
        Console.WriteLine("[isdst.local] " + localDt.IsDaylightSavingTime());
        Console.WriteLine("[isdst.unspec] " + unspec.IsDaylightSavingTime());

        // --- GetTypeCode ---
        Console.WriteLine("[typecode] " + (int)base1.GetTypeCode());

        return 0;
    }
}
