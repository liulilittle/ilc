using System;

static class Program
{
    static void Check(string name, bool ok)
    {
        if (!ok) Console.WriteLine("FAIL " + name);
        else Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        var y2k = new DateTime(2000, 1, 1, 12, 30, 45, DateTimeKind.Utc);
        Check("kind.cast2", y2k.Kind == (DateTimeKind)2);
        Check("kind.intcast", (int)y2k.Kind == 2);
        Check("kind.direct", y2k.Kind == DateTimeKind.Utc);
        Console.WriteLine("ALL-PASS");
    }
}