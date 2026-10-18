using System;
using System.Collections.Generic;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-bcl -- nb9 BCL-surface regression probe.
//
// Covers the other three nb9 areas:
//   * Encoding ranged overloads (GetBytes(char[],int,int),
//     GetByteCount(char[],int,int), GetCharCount(byte[],int,int),
//     GetString(byte[],int,int), GetChars(byte[],int,int,char[],int)).
//   * DateTimeOffset arithmetic / ticks / comparison (nb9 dtoffset).
//   * SortedSet<int> ctor(int[]) gate (a 1-arg ctor whose argument is an
//     array must NOT be routed to the 1-arg default ctor -- the old gate
//     ignored ps[0] and emitted a C0 break).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9Bcl
    {
        public static void Main()
        {
            var utf8 = Encoding.UTF8;

            char[] chars = { 'a', 'b', 'c', 'd' };
            byte[] ranged = utf8.GetBytes(chars, 1, 2);
            Console.WriteLine("[enc.gb.range]=" + ranged.Length + ":" + ranged[0] + "," + ranged[1]);
            Console.WriteLine("[enc.gbc.range]=" + utf8.GetByteCount(chars, 1, 2));

            byte[] bytes = utf8.GetBytes("hello");
            Console.WriteLine("[enc.gs.range]=" + utf8.GetString(bytes, 1, 3));
            Console.WriteLine("[enc.gcc.range]=" + utf8.GetCharCount(bytes, 1, 3));

            char[] dst = new char[8];
            int written = utf8.GetChars(bytes, 1, 3, dst, 0);
            Console.WriteLine("[enc.gc.into]=" + written + ":" + new string(dst, 0, written));

            var dto = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            Console.WriteLine("[dto.ticks]=" + dto.Ticks);
            Console.WriteLine("[dto.utc]=" + dto.UtcTicks);
            Console.WriteLine("[dto.dow]=" + (int)dto.DayOfWeek);
            Console.WriteLine("[dto.doy]=" + dto.DayOfYear);
            Console.WriteLine("[dto.offset]=" + dto.Offset.Ticks);
            Console.WriteLine("[dto.addd]=" + dto.AddDays(3).Ticks);
            Console.WriteLine("[dto.addh]=" + dto.AddHours(5).Ticks);
            Console.WriteLine("[dto.add]=" + dto.Add(TimeSpan.FromMinutes(90)).Ticks);
            Console.WriteLine("[dto.sub]=" + dto.Subtract(TimeSpan.FromMinutes(90)).Ticks);
            Console.WriteLine("[dto.unix]=" + dto.ToUnixTimeSeconds());
            Console.WriteLine("[dto.unixms]=" + dto.ToUnixTimeMilliseconds());
            Console.WriteLine("[dto.lt]=" + (dto < dto.AddTicks(1)));
            Console.WriteLine("[dto.eq]=" + (dto == dto.ToOffset(TimeSpan.FromHours(2))));
            Console.WriteLine("[dto.plus]=" + (dto + TimeSpan.FromSeconds(30)).Ticks);

            var ss = new SortedSet<int>(new[] { 5, 3, 3, 1 });
            Console.WriteLine("[ss.count]=" + ss.Count);
            Console.WriteLine("[ss.min]=" + ss.Min + "," + ss.Max);
            Console.WriteLine("[ss.has3]=" + ss.Contains(3));
            var sb = new StringBuilder();
            foreach (int v in ss) sb.Append(v + ",");
            Console.WriteLine("[ss.enum]=" + sb.ToString());
            Console.WriteLine("[ss.add]=" + ss.Add(4) + "," + ss.Add(5));
            Console.WriteLine("[ss.remove]=" + ss.Remove(3) + "," + ss.Remove(9));
            Console.WriteLine("[ss.reverse]=" + string.Join(",", ss.Reverse()));
        }
    }
}
