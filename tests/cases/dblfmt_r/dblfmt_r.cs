using System;
using System.Globalization;

// dblfmt_r -- DOUBLE "R" (round-trip) FORMAT gap probe (G4 era).
// Moved out of g4listdouble (which must stay focused on the List<double>
// domain): ToString("R") throws "Format specifier was invalid" in the
// runtime while net48 prints the shortest round-trip form. Expected red
// until the G17/precision ladder lands in formatext.cpp (D-DBLF-1).
// Deterministic: InvariantCulture, no time/random/paths.
class Prog
{
    static void Main()
    {
        Console.WriteLine("fmt_default=" + (0.1 + 0.2).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("fmt_r=" + (0.1 + 0.2).ToString("R", CultureInfo.InvariantCulture));
        Console.WriteLine("fmt_r_g17=" + (1.0 / 3.0).ToString("R", CultureInfo.InvariantCulture));
        Console.WriteLine("fmt_r_short=" + (0.5).ToString("R", CultureInfo.InvariantCulture));
        Console.WriteLine("fmt_r_max=" + double.MaxValue.ToString("R", CultureInfo.InvariantCulture));
        Console.WriteLine("done");
    }
}
