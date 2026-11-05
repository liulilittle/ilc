using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    static IEnumerable<bool> Booleans(int x)
    {
        yield return x % 2 == 1;
        yield return false;
    }

    static IEnumerable<sbyte> Sbytes(int x)
    {
        yield return (sbyte)x;
        yield return (sbyte)(x * 2);
    }

    static IEnumerable<char> Chars(int x)
    {
        yield return (char)('a' + x);
        yield return (char)('A' + x);
    }

    static IEnumerable<short> Shorts(int x)
    {
        yield return (short)x;
        yield return (short)(x + 1);
    }

    static IEnumerable<char> Emp(int x)
    {
        yield break;
    }

    static IEnumerable<bool> BoolGc(int x)
    {
        GC.Collect();
        yield return x == 1;
        yield return true;
    }

    static IEnumerable<byte> BytesGc(int x)
    {
        GC.Collect();
        yield return (byte)x;
        yield return (byte)0xFF;
    }

    static IEnumerable<bool> ListB(int x)
    {
        var l = new List<bool> { x % 2 == 0, x > 1 };
        return l;
    }

    public static void Main()
    {
        var src = new[] { 1, 2 };
        Console.WriteLine("[b.sm]=" + string.Join(",", src.SelectMany(Booleans)));
        Console.WriteLine("[sb.sm]=" + string.Join(",", src.SelectMany(Sbytes)));
        Console.WriteLine("[c.sm]=" + string.Join(",", src.SelectMany(Chars)));
        Console.WriteLine("[s.sm]=" + string.Join(",", src.SelectMany(Shorts)));
        var src3 = new[] { 3 };
        Console.WriteLine("[b.gc]=" + string.Join(",", src3.SelectMany(BoolGc)));
        Console.WriteLine("[by.gc]=" + string.Join(",", src3.SelectMany(BytesGc)));
        Console.WriteLine("[empty]=" + src.SelectMany(Emp).Count());
        Console.WriteLine("[list.bool]=" + string.Join(",", src.SelectMany(ListB)));
        Console.WriteLine("[3arg.c]=" + string.Join(",", src.SelectMany(x => new List<char> { (char)('a' + x) }, (x, c) => (int)c)));
    }
}