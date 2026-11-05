using System;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var r = new[] { 1, 2, 3 }.SelectMany(x => new[] { x, x * 10 });
        Console.WriteLine("m:" + string.Join(",", r));

        var rl = new[] { 1L, 2L }.SelectMany(x => new[] { x, x + 1L });
        Console.WriteLine("l:" + string.Join(",", rl));

        var rs = new[] { "a", "bb" }.SelectMany(s => new[] { "[" + s + "]", "{" + s + "}" });
        Console.WriteLine("s:" + string.Join(",", rs));

        var rd = new[] { 1.0, 2.0 }.SelectMany(x => new[] { x, x * 2.0 });
        Console.WriteLine("d:" + string.Join(",", rd));
    }
}