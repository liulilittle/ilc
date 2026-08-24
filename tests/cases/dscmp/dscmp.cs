using System;
using System.Runtime;
using System.Threading;
class P
{
    static void Main()
    {
        Console.WriteLine("c1=" + (string.CompareOrdinal("abc", "abc") == 0));
        Console.WriteLine("c2=" + (string.CompareOrdinal("abc", "abd") < 0));
        Console.WriteLine("c3=" + (string.CompareOrdinal("abd", "abc") > 0));
        Console.WriteLine("c4=" + string.Equals("ABC", "abc", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("c5=" + !string.Equals("ABC", "abc", StringComparison.Ordinal));
        Console.WriteLine("c6=" + (string.Compare("abc", "abd", StringComparison.Ordinal) < 0));
        Console.WriteLine("c7=" + (string.Compare("abc", null, StringComparison.Ordinal) > 0));
        Console.WriteLine("c8=" + "HeLLo".IndexOf("lo", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("c9=" + "HeLLo".Contains("LL"));
        Console.WriteLine("ca=" + "a,b,,c".Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length);
        long x = 0;
        Console.WriteLine("i1=" + (Interlocked.CompareExchange(ref x, 5L, 0L) == 0L));
        Console.WriteLine("i2=" + (Interlocked.CompareExchange(ref x, 7L, 5L) == 5L));
        Console.WriteLine("i3=" + (Interlocked.CompareExchange(ref x, 1L, 99L) == 7L) + " x=" + x);
        Console.WriteLine("i4=" + (Interlocked.Exchange(ref x, 0L) == 1L));
        Interlocked.Increment(ref x);
        Console.WriteLine("i5=" + (Interlocked.Read(ref x) == 1L));
        object o = new object();
        Console.WriteLine("i6=" + (Interlocked.CompareExchange(ref o, null, o) == null));
Console.WriteLine("g1=" + GCSettings.IsServerGC);
        Console.WriteLine("v1=" + Environment.Version.ToString());
    }
}
