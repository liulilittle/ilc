using System;
using System.Globalization;

//------------------------------------------------------------------------------
// dtapi1 -- DateTime CONSTRUCTOR surface (A item of the value-type family
// alignment): every public net48 ctor overload, including the Calendar forms.
//
// One case = one theme: constructors + the components/ticks they produce.
// Format strings are out of scope (owned by the dtfmt seat); only
// ToString()/plain numeric component reads are used.
//------------------------------------------------------------------------------
class Program
{
    static void Show(string id, Func<string> f)
    {
        try { Console.WriteLine("[" + id + "] " + f()); }
        catch (Exception e) { Console.WriteLine("[" + id + "] EX " + e.GetType().Name); }
    }

    static string D(DateTime d)
    {
        return d.Ticks + "|" + d.Year + "-" + d.Month + "-" + d.Day + " " + d.Hour + ":" + d.Minute + ":" + d.Second + "." + d.Millisecond + "|" + (int)d.Kind;
    }

    static void Main()
    {
        Show("c1", delegate { return D(new DateTime(2020, 2, 29)); });
        Show("c2", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30)); });
        Show("c3", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123)); });
        Show("c4", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, DateTimeKind.Utc)); });
        Show("c5", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, DateTimeKind.Local)); });
        Show("c6", delegate { return D(new DateTime(637185764960000000L)); });
        Show("c7", delegate { return D(new DateTime(637185764960000000L, DateTimeKind.Utc)); });
        Show("c8", delegate { return D(new DateTime(2020, 2, 29, new GregorianCalendar())); });
        Show("c9", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, new GregorianCalendar())); });
        Show("c10", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, new GregorianCalendar())); });
        Show("c11", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, new GregorianCalendar(), DateTimeKind.Utc)); });
        Show("c12", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, new GregorianCalendar(), DateTimeKind.Local)); });
        Show("c13", delegate { return D(new DateTime(2020, 2, 30)); });
        Show("c14", delegate { return D(new DateTime(2020, 13, 1)); });
        Show("c15", delegate { return D(new DateTime(0, 1, 1)); });
        Show("c16", delegate { return D(new DateTime(2020, 1, 1, 24, 0, 0)); });
        Show("c17", delegate { return D(new DateTime(2020, 1, 1, 0, 0, 0, 1000)); });
        Show("c18", delegate { return D(new DateTime(-1L)); });
        Show("c19", delegate { return D(new DateTime(0L)); });
        Show("c20", delegate { return D(new DateTime(3155378975999999999L)); });
        Show("c21", delegate { return D(new DateTime(3155378976000000000L)); });
        Show("c22", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123, DateTimeKind.Utc).AddTicks(1)); });
        // component accessors
        DateTime x = new DateTime(2020, 12, 31, 23, 59, 58, 987, DateTimeKind.Utc);
        Show("p1", delegate { return x.Year + "," + x.Month + "," + x.Day + "," + x.Hour + "," + x.Minute + "," + x.Second + "," + x.Millisecond; });
        Show("p2", delegate { return x.DayOfWeek.ToString() + "," + x.DayOfYear + "," + x.Kind.ToString() + "," + x.Ticks; });
        Show("p3", delegate { return x.Date.Ticks + "|" + x.TimeOfDay.Ticks + "|" + x.Date.Kind.ToString(); });
//PROBE_END
    }
}
