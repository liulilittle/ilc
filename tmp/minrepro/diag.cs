using System;

static class Program
{
    static void Main()
    {
        string s = "Hello World Hello";
        Console.WriteLine("IDXANY=" + s.IndexOfAny(new char[] { 'l', 'o' }, 5));
        var sb = new System.Text.StringBuilder("hi");
        sb.Append(true); sb.AppendLine();
        Console.WriteLine("SBLEN=" + sb.Length);
        Console.WriteLine("SBEXP=1.000000E+000".Length);
        Console.WriteLine("EXP1=" + Math.Exp(1.0));
        decimal d;
        bool ok = decimal.TryParse("not", out d);
        Console.WriteLine("DECTRY=" + ok + " val=" + d);
        int ti;
        bool ok2 = int.TryParse("12x", out ti);
        Console.WriteLine("INTTRY=" + ok2 + " val=" + ti);
        var ts = new TimeSpan(1, 2, 3, 0, 4);
        Console.WriteLine("TSCOMP=" + ts.Hours + "," + ts.Minutes + "," + ts.Seconds + "," + ts.Milliseconds);
        var v4 = new Version(1, 2, 3, 4);
        Console.WriteLine("VERHASH=" + v4.GetHashCode());
        var vals = Enum.GetValues(typeof(Color));
        Console.WriteLine("ENVALS=" + (vals == null ? "null" : vals.GetValue(2).ToString()));
        Color pc;
        bool ok3 = Enum.TryParse<Color>("Mauve", out pc);
        Console.WriteLine("ENTRY=" + ok3 + " pc=" + pc);
        var c = new Cloneable();
        Console.WriteLine("GETTYPE=" + (c.GetType() == typeof(Cloneable)));
        Console.WriteLine("DONE");
    }
}

enum Color { Red, Green, Blue }

sealed class Cloneable { public int X; }