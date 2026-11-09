using System;
using System.Linq;

static class P
{
    static void Main()
    {
        var i4 = new[] { 1, 2 };
        Console.WriteLine("[i4app] " + string.Join(",", i4.Append(3)));
        Console.WriteLine("[i4pre] " + string.Join(",", i4.Prepend(3)));

        var i8 = new long[] { 10L, 20L };
        Console.WriteLine("[i8app] " + string.Join(",", i8.Append(30L)));
        Console.WriteLine("[i8pre] " + string.Join(",", i8.Prepend(30L)));

        var dbl = new[] { 1.5, 2.5 };
        Console.WriteLine("[dblapp] " + string.Join(",", dbl.Append(3.5)));
        Console.WriteLine("[dblpre] " + string.Join(",", dbl.Prepend(3.5)));

        var str = new[] { "a", "b" };
        Console.WriteLine("[strapp] " + string.Join(",", str.Append("c")));
        Console.WriteLine("[strpre] " + string.Join(",", str.Prepend("c")));

        var sh = new short[] { 1, 2 };
        Console.WriteLine("[shortapp] " + string.Join(",", sh.Append((short)3)));
        var ch = new char[] { 'x', 'y' };
        Console.WriteLine("[charpre] " + string.Join(",", ch.Prepend('z')));

        var bl = new bool[] { true };
        Console.WriteLine("[boolapp] " + string.Join(",", bl.Append(false)));

        var empty = new int[0];
        Console.WriteLine("[emptyapp] " + string.Join(",", empty.Append(7)));
        Console.WriteLine("[emptypre] " + string.Join(",", empty.Prepend(7)));
    }
}
