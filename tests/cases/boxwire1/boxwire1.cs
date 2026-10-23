// boxwire1 -- `box` of the three BCL value structs (DateTime / TimeSpan /
// Guid) into an `object` parameter, then the klass-dispatched members:
// ToString() (through the object parameter, untracked), GetType().Name and
// the `is` test (isinst against the box klass).
//
// Ground truth is net48 (the oracle); every line prints a bool or a
// culture-independent value so the differential harness stays stable.
using System;
namespace Cases { public static class BoxWire1 {
    static string Show(object o) { return o.ToString(); }
    static string NameOf(object o) { return o.GetType().Name; }

    public static void Main() {
        DateTime dt = new DateTime(2020, 1, 2, 3, 4, 5);
        TimeSpan ts = new TimeSpan(1, 2, 3);
        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        // box -> object parameter -> virtual ToString
        Console.WriteLine("dt_str_eq=" + (Show(dt) == dt.ToString()));
        Console.WriteLine("ts_str=" + Show(ts));
        Console.WriteLine("guid_str=" + Show(g));

        // box -> GetType().Name (the box klass's short name)
        Console.WriteLine("dt_name=" + NameOf(dt));
        Console.WriteLine("ts_name=" + NameOf(ts));
        Console.WriteLine("guid_name=" + NameOf(g));

        // box -> isinst against the value type
        object od = dt;
        object ot = ts;
        object og = g;
        Console.WriteLine("dt_is=" + (od is DateTime));
        Console.WriteLine("ts_is=" + (ot is TimeSpan));
        Console.WriteLine("guid_is=" + (og is Guid));
        Console.WriteLine("dt_is_ts=" + (od is TimeSpan));
        Console.WriteLine("guid_is_dt=" + (og is DateTime));
    }
} }
