// boxwire3 -- boxed values through the FORMATTING paths:
//   * string.Format("{0}", dateTime)  (composite format -> Object.ToString)
//   * Console.WriteLine((object)dt)   (the object printer)
//   * List<object> holding boxed DateTime/TimeSpan/Guid
using System;
using System.Collections.Generic;
using System.Globalization;
namespace Cases { public static class BoxWire3 {
    public static void Main() {
        DateTime dt = new DateTime(2020, 1, 2, 3, 4, 5);
        TimeSpan ts = new TimeSpan(1, 2, 3);
        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        Console.WriteLine("fmt_dt=" + string.Format("{0}", dt));
        Console.WriteLine("fmt_ts=" + string.Format("{0}", ts));
        Console.WriteLine("fmt_guid=" + string.Format("{0}", g));
        Console.WriteLine("fmt_eq=" + (string.Format("{0}", dt) == dt.ToString()));

        Console.WriteLine("cw_dt=" + ((object)dt).ToString());
        Console.WriteLine("cw_ts=" + ((object)ts).ToString());
        Console.WriteLine("cw_guid=" + ((object)g).ToString());

        List<object> lo = new List<object>();
        lo.Add(dt);
        lo.Add(ts);
        lo.Add(g);
        Console.WriteLine("list_count=" + lo.Count);
        Console.WriteLine("list0=" + lo[0].ToString());
        Console.WriteLine("list1=" + lo[1].ToString());
        Console.WriteLine("list2=" + lo[2].ToString());
        Console.WriteLine("list0_is=" + (lo[0] is DateTime));
        Console.WriteLine("list1_is=" + (lo[1] is TimeSpan));
        Console.WriteLine("list2_is=" + (lo[2] is Guid));
        Console.WriteLine("list0_type=" + lo[0].GetType().Name);
        Console.WriteLine("list1_type=" + lo[1].GetType().Name);
        Console.WriteLine("list2_type=" + lo[2].GetType().Name);
        Console.WriteLine("contains_dt=" + lo.Contains(dt));
        Console.WriteLine("contains_ts=" + lo.Contains(ts));
        Console.WriteLine("contains_guid=" + lo.Contains(g));
    }
} }
