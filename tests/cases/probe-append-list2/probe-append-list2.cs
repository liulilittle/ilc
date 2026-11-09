using System;
using System.Collections.Generic;
using System.Linq;

static class P
{
    static void Main()
    {
        var i4 = new List<int> { 1, 2 };
        Console.WriteLine("[i4app] " + string.Join(",", i4.Append(3)));
        Console.WriteLine("[i4pre] " + string.Join(",", i4.Prepend(3)));

        var i8 = new List<long> { 10L, 20L };
        Console.WriteLine("[i8app] " + string.Join(",", i8.Append(30L)));
        Console.WriteLine("[i8pre] " + string.Join(",", i8.Prepend(30L)));

        var dbl = new List<double> { 1.5, 2.5 };
        Console.WriteLine("[dblapp] " + string.Join(",", dbl.Append(3.5)));
        Console.WriteLine("[dblpre] " + string.Join(",", dbl.Prepend(3.5)));

        var fl = new List<float> { 1.5f, 2.5f };
        Console.WriteLine("[flapp] " + string.Join(",", fl.Append(3.5f)));

        var str = new List<string> { "a", "b" };
        Console.WriteLine("[strapp] " + string.Join(",", str.Append("c")));
        Console.WriteLine("[strpre] " + string.Join(",", str.Prepend("c")));

        // NARROW / BOOL results: string.Join reads a narrow-family array's
        // elemKind 0 as int32 (the PRE-EXISTING shared formatter limitation
        // registered at linq_list.cpp:182-194). Verify the appended element by
        // its scalar readback instead of the array formatter.
        var sh = new List<short> { 1, 2 };
        Console.WriteLine("[shortapp] " + sh.Append((short)3).Last());
        var ch = new List<char> { 'x', 'y' };
        Console.WriteLine("[charpre] " + ch.Prepend('z').First());
        var bl = new List<bool> { true };
        Console.WriteLine("[boolapp] " + bl.Append(false).Last());

        var empty = new List<int>();
        Console.WriteLine("[emptyapp] " + string.Join(",", empty.Append(7)));
        Console.WriteLine("[emptypre] " + string.Join(",", empty.Prepend(7)));
    }
}
