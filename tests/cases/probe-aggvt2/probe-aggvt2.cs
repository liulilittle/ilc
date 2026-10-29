//------------------------------------------------------------------------------
// probe-aggvt2 -- Aggregate(seed, func) over a NON-decimal VALUE-TYPE ARRAY
// source (System.DateTime / System.TimeSpan / System.Guid) with an
// int32 / int64 / float / double accumulator.
//
// The ARRAY Aggregate arm used to bind the int32-element base kernels
// (seed_i4/seed_i8/seed_f/seed_d): the fold read 4 bytes out of an 8-byte
// DateTime/TimeSpan cell (a silent wrong value) or out of the 16-byte Guid
// cell, and called the lambda through an int32 element slot while the
// generated leaf takes the value struct BY VALUE (an ABI mismatch). The
// decimal accumulator family was already wired (agg-vtsrc).
//
// Each line prints label=value; the net48 oracle is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeAggVt2
    {
        private static string FB(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.ToUInt32(BitConverter.GetBytes(v), 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string DB(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.DoubleToInt64Bits(v).ToString(CultureInfo.InvariantCulture);
        }

        public static void Main()
        {
            TimeSpan[] ts = { TimeSpan.FromDays(1), TimeSpan.FromDays(-2), new TimeSpan(3, 0, 0) };
            DateTime[] dt =
            {
                new DateTime(2020, 1, 31),
                new DateTime(2020, 2, 29),
                new DateTime(2021, 3, 1),
            };
            Guid[] gs =
            {
                Guid.Empty,
                new Guid("11111111-2222-3333-4444-555555555555"),
                Guid.Empty,
            };

            // ---- TimeSpan source (8-byte tick cells) ----
            Console.WriteLine("[ts_i4]=" + ts.Aggregate(0, (a, t) => a + t.Days * 24 + t.Hours).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[ts_i8]=" + ts.Aggregate(0L, (a, t) => a + (long)t.Days * 24 + t.Hours).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[ts_f]=" + FB(ts.Aggregate(0f, (a, t) => a + t.Days * 24 + t.Hours)));
            Console.WriteLine("[ts_d]=" + DB(ts.Aggregate(0.0, (a, t) => a + t.Days * 24 + t.Hours)));
            Console.WriteLine("[ts_i4_empty]=" + new TimeSpan[0].Aggregate(7, (a, t) => a + t.Hours).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[ts_f_empty]=" + FB(new TimeSpan[0].Aggregate(1.5f, (a, t) => a + t.Hours)));

            // ---- DateTime source (8-byte packed cell) ----
            Console.WriteLine("[dt_i4]=" + dt.Aggregate(0, (a, d) => a + d.Month * 100 + d.Day).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[dt_i8]=" + dt.Aggregate(0L, (a, d) => a + (long)d.Month * 100 + d.Day).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[dt_f]=" + FB(dt.Aggregate(0f, (a, d) => a + d.Day)));
            Console.WriteLine("[dt_d]=" + DB(dt.Aggregate(0.5, (a, d) => a + d.Day)));

            // ---- Guid source (16-byte cell) ----
            Console.WriteLine("[guid_i4]=" + gs.Aggregate(0, (a, g) => a + g.ToString().Length).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[guid_i8]=" + gs.Aggregate(0L, (a, g) => a + (long)g.ToString().Length).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("[guid_f]=" + FB(gs.Aggregate(0f, (a, g) => a + g.ToString().Length)));
            Console.WriteLine("[guid_d]=" + DB(gs.Aggregate(0.0, (a, g) => a + g.ToString().Length)));

            Console.WriteLine("done");
        }
    }
}
