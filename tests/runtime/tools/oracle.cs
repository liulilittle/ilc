//------------------------------------------------------------------------------
// oracle.cs -- the net48 side of tests/runtime/README-timezone-posix.md.
//
// Prints the CSV the POSIX probe (tools/zprobe.cpp) prints, so the two can be
// diffed row-for-row:
//
//   zone,date,kind,offset_seconds,isdst
//
// Build + run (Windows, .NET Framework 4.8):
//   csc /nologo /out:oracle.exe oracle.cs && oracle.exe > net48.csv
//
// The zone ids are the Windows registry names; the labels are the IANA ids the
// POSIX probe uses, so the comparison is on the same rows.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

class Oracle
{
    static readonly string[] Ids = {
        "UTC", "China Standard Time", "Eastern Standard Time",
        "Lord Howe Standard Time", "GMT Standard Time",
        "New Zealand Standard Time", "AUS Eastern Standard Time",
        "India Standard Time"
    };
    static readonly string[] Short = {
        "UTC", "Asia/Shanghai", "America/New_York",
        "Australia/Lord_Howe", "Europe/Dublin",
        "Pacific/Auckland", "Australia/Sydney", "Asia/Kolkata"
    };
    static readonly int[] Months = { 1, 4, 7, 10 };

    static void Main()
    {
        Console.WriteLine("zone,date,kind,offset_seconds,isdst");
        for (int z = 0; z < Ids.Length; z++)
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(Ids[z]);
            for (int m = 0; m < Months.Length; m++)
            {
                int mo = Months[m];
                foreach (DateTimeKind kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Utc })
                {
                    DateTime dt = new DateTime(2000, mo, 1, 12, 0, 0, kind);
                    TimeSpan off = tz.GetUtcOffset(dt);
                    int dst;
                    try { dst = tz.IsDaylightSavingTime(dt) ? 1 : 0; }
                    catch (Exception) { dst = -1; }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},2000-{1:00}-01,{2},{3},{4}",
                        Short[z], mo, kind == DateTimeKind.Utc ? "utc" : "wall",
                        (int)off.TotalSeconds, dst));
                }
            }
        }
    }
}
