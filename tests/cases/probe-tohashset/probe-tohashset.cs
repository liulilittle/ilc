using System;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var a = new[] { 1, 2, 2, 3 };
        Console.WriteLine("c:" + a.ToHashSet().Count);
        Console.WriteLine("h:" + a.ToHashSet().Contains(3));
        Console.WriteLine("hn:" + a.ToHashSet().Contains(9));

        var s = new[] { "a", "A", "b" };
        Console.WriteLine("s:" + s.ToHashSet().Count);
        Console.WriteLine("si:" + s.ToHashSet(StringComparer.OrdinalIgnoreCase).Count);
        Console.WriteLine("si2:" + s.ToHashSet(StringComparer.OrdinalIgnoreCase).Contains("B"));

        var la = new[] { 1L, 1L, 2L };
        Console.WriteLine("l:" + la.ToHashSet().Count);
        Console.WriteLine("lh:" + la.ToHashSet().Contains(2L));
    }
}