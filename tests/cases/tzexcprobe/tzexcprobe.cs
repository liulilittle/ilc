// tzexcprobe -- TZEXC e2e probe (NON-POOL): unknown zone ids must throw the
// net48 exception type and a TYPED catch (TimeZoneNotFoundException) must
// match the runtime's exception chain. The typed-catch dispatch prints
// TZNF/OTHER; a known-zone sanity line guards against conversion regressions.
using System;

namespace Cases
{
    public static class TzExcProbe
    {
        private static void Probe(string label, string id)
        {
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                // NOTE: do not touch tz.Id here -- System.TimeZoneInfo::get_Id
                // is still an M5 gap and would reject the whole method fold.
                Console.WriteLine(label + "=OK");
            }
            catch (TimeZoneNotFoundException)
            {
                Console.WriteLine(label + "=TZNF");
            }
            catch (Exception)
            {
                Console.WriteLine(label + "=OTHER");
            }
        }

        public static void Main()
        {
            Probe("custom", "Custom Zone");
            Probe("foo", "Foo Bar");
            Probe("slash", "Not/AZone");

            // Known-zone sanity: same offset as the tzprobe oracle (EDT).
            TimeZoneInfo e = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime l = TimeZoneInfo.ConvertTimeFromUtc(
                new DateTime(2020, 7, 15, 12, 0, 0, DateTimeKind.Utc), e);
            Console.WriteLine("e_summer=" + l.Month + "/" + l.Day + " " + l.Hour + ":" + l.Minute);
        }
    }
}
