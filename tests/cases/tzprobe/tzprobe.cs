// tzprobe -- net48 oracle + e2e probe for the TimeZoneInfo minimal subset
// (Eastern/Pacific/Central Standard Time + UTC; US 2007+ DST rules). Every
// instant below is deterministic on net48 (registry rules) and must match
// the transpiled runtime byte-for-byte.
using System;

namespace Cases
{
    public static class TzProbe
    {
        static void P(string label, string id, DateTime utc)
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(id);
            DateTime l = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            Console.WriteLine(label + "=" + l.Month + "/" + l.Day + " " + l.Hour + ":" + l.Minute);
        }

        public static void Main()
        {
            // Winter/summer instants (the dtaudit2 shape), per zone.
            P("e_winter", "Eastern Standard Time", new DateTime(2020, 1, 15, 12, 0, 0, DateTimeKind.Utc));
            P("e_summer", "Eastern Standard Time", new DateTime(2020, 7, 15, 12, 0, 0, DateTimeKind.Utc));
            P("p_winter", "Pacific Standard Time", new DateTime(2020, 1, 15, 12, 0, 0, DateTimeKind.Utc));
            P("p_summer", "Pacific Standard Time", new DateTime(2020, 7, 15, 12, 0, 0, DateTimeKind.Utc));
            P("c_winter", "Central Standard Time", new DateTime(2020, 1, 15, 12, 0, 0, DateTimeKind.Utc));
            P("c_summer", "Central Standard Time", new DateTime(2020, 7, 15, 12, 0, 0, DateTimeKind.Utc));
            P("utc", "UTC", new DateTime(2020, 7, 15, 12, 0, 0, DateTimeKind.Utc));

            // Eastern DST boundaries 2020: start 03-08 07:00Z (02:00 EST ->
            // 03:00 EDT), end 11-01 06:00Z (02:00 EDT -> 01:00 EST).
            P("e_start_before", "Eastern Standard Time", new DateTime(2020, 3, 8, 6, 59, 59, DateTimeKind.Utc));
            P("e_start_after", "Eastern Standard Time", new DateTime(2020, 3, 8, 7, 0, 0, DateTimeKind.Utc));
            P("e_end_before", "Eastern Standard Time", new DateTime(2020, 11, 1, 5, 59, 59, DateTimeKind.Utc));
            P("e_end_after", "Eastern Standard Time", new DateTime(2020, 11, 1, 6, 0, 0, DateTimeKind.Utc));

            // Pacific boundaries 2020: start 03-08 10:00Z, end 11-01 09:00Z.
            P("p_start_before", "Pacific Standard Time", new DateTime(2020, 3, 8, 9, 59, 59, DateTimeKind.Utc));
            P("p_start_after", "Pacific Standard Time", new DateTime(2020, 3, 8, 10, 0, 0, DateTimeKind.Utc));
            P("p_end_before", "Pacific Standard Time", new DateTime(2020, 11, 1, 8, 59, 59, DateTimeKind.Utc));
            P("p_end_after", "Pacific Standard Time", new DateTime(2020, 11, 1, 9, 0, 0, DateTimeKind.Utc));

            // Central boundaries 2020: start 03-08 08:00Z, end 11-01 07:00Z.
            P("c_start_before", "Central Standard Time", new DateTime(2020, 3, 8, 7, 59, 59, DateTimeKind.Utc));
            P("c_start_after", "Central Standard Time", new DateTime(2020, 3, 8, 8, 0, 0, DateTimeKind.Utc));
            P("c_end_before", "Central Standard Time", new DateTime(2020, 11, 1, 6, 59, 59, DateTimeKind.Utc));
            P("c_end_after", "Central Standard Time", new DateTime(2020, 11, 1, 7, 0, 0, DateTimeKind.Utc));
        }
    }
}
