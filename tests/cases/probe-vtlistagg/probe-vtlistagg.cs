using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeVtListAgg
    {
        static string N(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string N(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string N(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        static string N(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        static string N(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string D(DateTime v) { return v.ToString("s", CultureInfo.InvariantCulture); }
        static string TS(TimeSpan v) { return v.Ticks.ToString(CultureInfo.InvariantCulture); }
        static string G(Guid v) { return v.ToString("N"); }
        static string DTO(DateTimeOffset v) { return v.UtcTicks.ToString(CultureInfo.InvariantCulture) + "@" + v.Offset.Ticks.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            var dl = new List<DateTime>
            {
                new DateTime(2020, 3, 4), new DateTime(2019, 1, 2),
                new DateTime(2021, 5, 6), new DateTime(2019, 6, 7),
            };
            Console.WriteLine("[ldt.min]=" + D(dl.Min()));
            Console.WriteLine("[ldt.max]=" + D(dl.Max()));
            Console.WriteLine("[ldt.sum_i4]=" + N(dl.Sum(d => d.Day)));
            Console.WriteLine("[ldt.min_i4]=" + N(dl.Min(d => d.Day)));
            Console.WriteLine("[ldt.max_i4]=" + N(dl.Max(d => d.Day)));
            Console.WriteLine("[ldt.avg_i4]=" + N(dl.Average(d => d.Day)));
            Console.WriteLine("[ldt.sum_i8]=" + N(dl.Sum(d => d.Ticks)));
            Console.WriteLine("[ldt.min_i8]=" + N(dl.Min(d => d.Ticks)));
            Console.WriteLine("[ldt.sum_d]=" + N(dl.Sum(d => d.Day * 1.5)));
            Console.WriteLine("[ldt.avg_d]=" + N(dl.Average(d => d.Day * 1.5)));
            Console.WriteLine("[ldt.sum_f]=" + N(dl.Sum(d => d.Day * 0.5f)));
            Console.WriteLine("[ldt.avg_f]=" + N(dl.Average(d => d.Day * 0.5f)));
            Console.WriteLine("[ldt.sum_dec]=" + N(dl.Sum(d => d.Day * 1.5m)));
            Console.WriteLine("[ldt.min_dec]=" + N(dl.Min(d => d.Day * 1.5m)));
            Console.WriteLine("[ldt.max_dec]=" + N(dl.Max(d => d.Day * 1.5m)));
            Console.WriteLine("[ldt.avg_dec]=" + N(dl.Average(d => d.Day * 1.5m)));

            var tl = new List<TimeSpan>
            {
                new TimeSpan(3, 2, 1), new TimeSpan(1, 4, 2),
                new TimeSpan(2, 1, 5), new TimeSpan(1, 2, 3),
            };
            Console.WriteLine("[lts.min]=" + TS(tl.Min()));
            Console.WriteLine("[lts.max]=" + TS(tl.Max()));
            Console.WriteLine("[lts.sum_i4]=" + N(tl.Sum(t => t.Hours)));
            Console.WriteLine("[lts.min_i4]=" + N(tl.Min(t => t.Hours)));
            Console.WriteLine("[lts.avg_i4]=" + N(tl.Average(t => t.Hours)));
            Console.WriteLine("[lts.sum_i8]=" + N(tl.Sum(t => t.Ticks)));
            Console.WriteLine("[lts.sum_dec]=" + N(tl.Sum(t => t.Hours * 0.5m)));

            var gl = new List<Guid>
            {
                new Guid("11111111-2222-3333-4444-555555555555"),
                new Guid("00000000-0000-0000-0000-000000000001"),
                new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                new Guid("00000000-0000-0000-0000-000000000002"),
            };
            Console.WriteLine("[lg.min]=" + G(gl.Min()));
            Console.WriteLine("[lg.max]=" + G(gl.Max()));
            Console.WriteLine("[lg.sum_i4]=" + N(gl.Sum(g => (int)g.ToByteArray()[0])));
            Console.WriteLine("[lg.min_i4]=" + N(gl.Min(g => (int)g.ToByteArray()[0])));
            Console.WriteLine("[lg.max_i4]=" + N(gl.Max(g => (int)g.ToByteArray()[0])));

            var ol = new List<DateTimeOffset>
            {
                new DateTimeOffset(2020, 3, 4, 5, 6, 7, TimeSpan.FromHours(8)),
                new DateTimeOffset(2019, 1, 2, 3, 4, 5, TimeSpan.Zero),
                new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.FromHours(-5)),
                new DateTimeOffset(2019, 6, 7, 8, 9, 10, TimeSpan.FromHours(2)),
            };
            Console.WriteLine("[lo.min]=" + DTO(ol.Min()));
            Console.WriteLine("[lo.max]=" + DTO(ol.Max()));
            Console.WriteLine("[lo.sum_i4]=" + N(ol.Sum(d => d.Day)));
            Console.WriteLine("[lo.max_i4]=" + N(ol.Max(d => d.Day)));
            Console.WriteLine("[lo.avg_i4]=" + N(ol.Average(d => d.Day)));
            Console.WriteLine("[lo.sum_dec]=" + N(ol.Sum(d => d.Day * 2m)));
        }
    }
}