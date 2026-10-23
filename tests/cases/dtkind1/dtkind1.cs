using System;

//------------------------------------------------------------------------------
// dtkind1 -- DateTimeKind theme: enum-typed ctor operands + get_Kind +
// SpecifyKind. The CTOR(ymdhms,kind)/CTOR(ymdhms,ms,kind)/CTOR(ticks,kind)
// overloads decode their trailing param as 0x11 VALUETYPE; the calendars
// (dtcal1 theme) are the same 0x11 byte and must NOT merge here.
//------------------------------------------------------------------------------
class Program
{
    static void Show(string id, Func<string> f)
    {
        try { Console.WriteLine("[" + id + "] " + f()); }
        catch (Exception e) { var ae = e as ArgumentException; Console.WriteLine("[" + id + "] EX " + e.GetType().Name + (ae != null ? " param=" + ae.ParamName : "")); }
    }

    static string D(DateTime d)
    {
        return d.Ticks + "|" + (d.Kind == DateTimeKind.Unspecified ? "U" : d.Kind == DateTimeKind.Utc ? "Z" : "L");
    }

    static void Main()
    {
        Show("k1", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, DateTimeKind.Unspecified)); });
        Show("k2", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, DateTimeKind.Utc)); });
        Show("k3", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, DateTimeKind.Local)); });
        Show("k4", delegate { return D(new DateTime(637185764960000000L, DateTimeKind.Utc)); });
        Show("k5", delegate { return D(new DateTime(637185764960000000L, DateTimeKind.Local)); });
        Show("k6", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, DateTimeKind.Utc)); });
        Show("k7", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, DateTimeKind.Local)); });
        Show("k8", delegate { return D(DateTime.SpecifyKind(new DateTime(2020, 1, 2, 3, 4, 5), DateTimeKind.Utc)); });
        Show("k9", delegate { return D(DateTime.SpecifyKind(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), DateTimeKind.Local)); });
        Show("k10", delegate { return D(new DateTime(2020, 1, 2)); });
        Show("k11", delegate { return D(DateTime.SpecifyKind(new DateTime(2020, 1, 2), (DateTimeKind)9)); });
    }
}
