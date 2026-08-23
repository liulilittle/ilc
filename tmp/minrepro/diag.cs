using System;

enum Color { Red, Green, Blue }

static class Program
{
    sealed class Cloneable { public int X; }

    static void Main()
    {
        Color pc;
        bool f = Enum.TryParse<Color>("Mauve", out pc);
        Console.WriteLine("TRYFAIL pc=" + pc + " str=" + pc.ToString());
        var vals = Enum.GetValues(typeof(Color));
        var v2 = vals.GetValue(2);
        Console.WriteLine("GETVAL=" + v2 + " eq=" + v2.Equals(Color.Blue));
        var c = new Cloneable();
        Console.WriteLine("GT1=" + (c.GetType() == typeof(Cloneable)) + " GT2=" + (c.GetType() != typeof(object)));
        var v4 = new Version(1, 2, 3, 4);
        Console.WriteLine("VERHASH=" + (v4.GetHashCode() == new Version(1, 2, 3, 4).GetHashCode()));
        Console.WriteLine("DONE");
    }
}