using System;

//------------------------------------------------------------------------------
// dtctor1 -- DateTime CONSTRUCTOR theme: every public net48 ctor (components /
// ticks / milliseconds / DateTimeKind) plus the plain component accessors.
// int32-only; no enum-typed operand (DateTimeKind = per-task dtkind1 theme,
// Calendar = per-task dtcal1 theme), no format strings (dtfmt seat).
//
// One case = one theme: constructors + the components/ticks they produce.
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
        return d.Ticks + "|" + d.Year + "-" + d.Month + "-" + d.Day + " " + d.Hour + ":" + d.Minute + ":" + d.Second + "." + d.Millisecond;
    }

    static void Main()
    {
        Show("c1", delegate { return D(new DateTime(2020, 2, 29)); });
        Show("c2", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30)); });
        Show("c3", delegate { return D(new DateTime(2020, 2, 29, 13, 45, 30, 123)); });
        Show("c6", delegate { return D(new DateTime(637185764960000000L)); });
        Show("c13", delegate { return D(new DateTime(2020, 2, 30)); });
        Show("c14", delegate { return D(new DateTime(2020, 13, 1)); });
        Show("c15", delegate { return D(new DateTime(0, 1, 1)); });
        Show("c16", delegate { return D(new DateTime(2020, 1, 1, 24, 0, 0)); });
        Show("c17", delegate { return D(new DateTime(2020, 1, 1, 0, 0, 0, 1000)); });
        Show("c18", delegate { return D(new DateTime(-1L)); });
        Show("c19", delegate { return D(new DateTime(0L)); });
        Show("c20", delegate { return D(new DateTime(3155378975999999999L)); });
        Show("c21", delegate { return D(new DateTime(3155378976000000000L)); });
        // component accessors (int32 + long only, no enum operand)
        int y = new DateTime(2020, 12, 31, 23, 59, 58, 987).Year;
        Show("p1", delegate { return y + ""; });
    }
}
