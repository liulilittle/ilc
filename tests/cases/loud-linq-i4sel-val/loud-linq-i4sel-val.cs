//------------------------------------------------------------------------------
// loud-linq-i4sel-val.cs -- e2e reference case for the INT32-source Select with
// a VALUE-STRUCT result (net48 Select<int,TResult> and the (element,index) form).
//
// The selector returns the struct BY VALUE (linq_map.h):
//   Select<int,DateTime> -> il2cpp_linq_select_i4_dt
//   Select<int,TimeSpan> -> il2cpp_linq_select_i4_ts
//   Select<int,Guid>     -> il2cpp_linq_select_i4_guid
//   (element,index) forms -> il2cpp_linq_select_idx_i4_dt/_ts/_guid
// Before this change these results LOUD-rejected ("the value-type result family
// with a kernel is System.Decimal only").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqI4SelVal
    {
        public static void Main()
        {
            int[] a = { 1, 2 };

            // ===== DateTime =====
            DateTime[] d = a.Select(x => new DateTime(2020, 1, 2)).ToArray();
            Console.WriteLine("dt_len=" + d.Length);
            Console.WriteLine("dt0=" + d[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            DateTime[] di = a.Select((x, i) => new DateTime(2020, 1, 2).AddDays(i)).ToArray();
            Console.WriteLine("dti1=" + di[1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            // ===== TimeSpan =====
            TimeSpan[] t = a.Select(x => new TimeSpan(1, 2, 3)).ToArray();
            Console.WriteLine("ts0=" + t[0].ToString());
            TimeSpan[] ti = a.Select((x, i) => new TimeSpan(i, 0, 0)).ToArray();
            Console.WriteLine("tsi1=" + ti[1].ToString());

            // ===== Guid =====
            Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Guid[] gg = a.Select(x => g).ToArray();
            Console.WriteLine("guid0=" + gg[0]);
            Guid[] gi = a.Select((x, i) => g).ToArray();
            Console.WriteLine("guidi1=" + gi[1]);

            // ===== an empty source =====
            int[] empty = new int[0];
            Console.WriteLine("empty=" + (empty.Select(x => new DateTime(2020, 1, 2)).ToArray().Length == 0));

            Console.WriteLine("done");
        }
    }
}
