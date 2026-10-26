//------------------------------------------------------------------------------
// loud-linq-refsel-val.cs -- e2e reference case for the REFERENCE-source Select
// with a VALUE-STRUCT result (net48 Select<TSource,TResult> and the
// (element,index) form).
//
// The element crosses as Il2CppObject*; the result struct rides the selector's
// return slot (linq_map.h):
//   Select<string,DateTime>    -> il2cpp_linq_select_ref_dt
//   Select<string,TimeSpan>    -> il2cpp_linq_select_ref_ts
//   Select<string,Guid>        -> il2cpp_linq_select_ref_guid
//   (element,index) forms      -> il2cpp_linq_select_idx_ref_dt/_ts/_guid
// Before this change these results LOUD-rejected ("the runtime reference-source
// select surface is ref -> ref, ref -> decimal and ref -> int32/int64/double/
// float").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqRefSelVal
    {
        public static void Main()
        {
            string[] s = { "a", "b" };

            // ===== DateTime =====
            DateTime[] d = s.Select(x => new DateTime(2020, 1, 2)).ToArray();
            Console.WriteLine("dt_len=" + d.Length);
            Console.WriteLine("dt0=" + d[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            DateTime[] di = s.Select((x, i) => new DateTime(2020, 1, 2).AddDays(i)).ToArray();
            Console.WriteLine("dti1=" + di[1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            // ===== TimeSpan =====
            TimeSpan[] t = s.Select(x => new TimeSpan(1, 2, 3)).ToArray();
            Console.WriteLine("ts0=" + t[0].ToString());
            TimeSpan[] ti = s.Select((x, i) => new TimeSpan(i, 0, 0)).ToArray();
            Console.WriteLine("tsi1=" + ti[1].ToString());

            // ===== Guid =====
            Guid g0 = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            Guid[] g = s.Select(x => g0).ToArray();
            Console.WriteLine("guid0=" + g[0]);
            Guid[] gi = s.Select((x, i) => g0).ToArray();
            Console.WriteLine("guidi1=" + gi[1]);

            // ===== an empty source =====
            string[] empty = new string[0];
            Console.WriteLine("empty=" + (empty.Select(x => new DateTime(2020, 1, 2)).ToArray().Length == 0));

            Console.WriteLine("done");
        }
    }
}
