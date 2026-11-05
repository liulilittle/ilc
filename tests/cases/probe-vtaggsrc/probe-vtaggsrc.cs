using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeVtAggSrc
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
            DateTime[] ds =
            {
                new DateTime(2020, 3, 4, 5, 6, 7),
                new DateTime(2019, 1, 2, 3, 4, 5),
                new DateTime(2021, 5, 6, 7, 8, 9),
                new DateTime(2019, 6, 7, 8, 9, 10),
            };
            Console.WriteLine("[dt.min]=" + D(ds.Min()));
            Console.WriteLine("[dt.max]=" + D(ds.Max()));
            Console.WriteLine("[dt.min_i4]=" + N(ds.Min(d => d.Day)));
            Console.WriteLine("[dt.max_i4]=" + N(ds.Max(d => d.Day)));
            Console.WriteLine("[dt.sum_i4]=" + N(ds.Sum(d => d.Day)));
            Console.WriteLine("[dt.avg_i4]=" + N(ds.Average(d => d.Day)));
            Console.WriteLine("[dt.sum_i8]=" + N(ds.Sum(d => d.Ticks)));
            Console.WriteLine("[dt.min_i8]=" + N(ds.Min(d => d.Ticks)));
            Console.WriteLine("[dt.sum_d]=" + N(ds.Sum(d => d.Day * 1.5)));
            Console.WriteLine("[dt.max_d]=" + N(ds.Max(d => d.Day * 1.5)));
            Console.WriteLine("[dt.avg_d]=" + N(ds.Average(d => d.Day * 1.5)));
            Console.WriteLine("[dt.sum_f]=" + N(ds.Sum(d => d.Day * 0.5f)));
            Console.WriteLine("[dt.avg_f]=" + N(ds.Average(d => d.Day * 0.5f)));
            Console.WriteLine("[dt.sum_dec]=" + N(ds.Sum(d => d.Day * 1.25m)));
            Console.WriteLine("[dt.min_dec]=" + N(ds.Min(d => d.Day * 1.25m)));
            Console.WriteLine("[dt.avg_dec]=" + N(ds.Average(d => d.Day * 1.25m)));

            TimeSpan[] ts =
            {
                new TimeSpan(3, 2, 1),
                new TimeSpan(1, 4, 2),
                new TimeSpan(2, 1, 5),
                new TimeSpan(1, 2, 3),
            };
            Console.WriteLine("[ts.min]=" + TS(ts.Min()));
            Console.WriteLine("[ts.max]=" + TS(ts.Max()));
            Console.WriteLine("[ts.sum_i4]=" + N(ts.Sum(t => t.Hours)));
            Console.WriteLine("[ts.min_i4]=" + N(ts.Min(t => t.Hours)));
            Console.WriteLine("[ts.avg_i4]=" + N(ts.Average(t => t.Hours)));
            Console.WriteLine("[ts.sum_i8]=" + N(ts.Sum(t => t.Ticks)));
            Console.WriteLine("[ts.min_i8]=" + N(ts.Min(t => t.Ticks)));
            Console.WriteLine("[ts.sum_dec]=" + N(ts.Sum(t => t.Hours * 0.5m)));

            Guid[] gs =
            {
                new Guid("11111111-2222-3333-4444-555555555555"),
                new Guid("00000000-0000-0000-0000-000000000001"),
                new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                new Guid("00000000-0000-0000-0000-000000000002"),
            };
            Console.WriteLine("[guid.min]=" + G(gs.Min()));
            Console.WriteLine("[guid.max]=" + G(gs.Max()));
            Console.WriteLine("[guid.sum_i4]=" + N(gs.Sum(g => (int)g.ToByteArray()[0])));
            Console.WriteLine("[guid.min_i4]=" + N(gs.Min(g => (int)g.ToByteArray()[0])));
            Console.WriteLine("[guid.max_i4]=" + N(gs.Max(g => (int)g.ToByteArray()[0])));

            DateTimeOffset[] dtos =
            {
                new DateTimeOffset(2020, 3, 4, 5, 6, 7, TimeSpan.FromHours(8)),
                new DateTimeOffset(2019, 1, 2, 3, 4, 5, TimeSpan.Zero),
                new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.FromHours(-5)),
                new DateTimeOffset(2019, 6, 7, 8, 9, 10, TimeSpan.FromHours(2)),
            };
            Console.WriteLine("[dto.min]=" + DTO(dtos.Min()));
            Console.WriteLine("[dto.max]=" + DTO(dtos.Max()));
            Console.WriteLine("[dto.sum_i4]=" + N(dtos.Sum(d => d.Day)));
            Console.WriteLine("[dto.max_i4]=" + N(dtos.Max(d => d.Day)));
            Console.WriteLine("[dto.avg_i4]=" + N(dtos.Average(d => d.Day)));
            Console.WriteLine("[dto.sum_dec]=" + N(dtos.Sum(d => d.Day * 2m)));
        }
    }
}