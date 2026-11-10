using System;
using System.Collections.Generic;
using System.Linq;
static class P
{
    static void Main()
    {
        var a = new[] { 3L, 1L, 2L };
        var d = a.ToDictionary(x => x);
        Console.WriteLine("[cnt] " + d.Count);
        Console.WriteLine("[k1] " + d[1L]);
        Console.WriteLine("[k2] " + d[2L]);
        Console.WriteLine("[has3] " + d.ContainsKey(3L));
        Console.WriteLine("[has9] " + d.ContainsKey(9L));

        // The long values (an x*10 elementSelector) ride the int64 value carrier.
        var b = new[] { 1L, 2L, 3L };
        var d2 = b.ToDictionary(x => x, x => x * 10);
        Console.WriteLine("[v1] " + d2[1L]);
        Console.WriteLine("[v3] " + d2[3L]);

        // Duplicate key -> ArgumentException.
        try
        {
            var d3 = new[] { 1L, 1L }.ToDictionary(x => x);
            Console.WriteLine("[dup] none");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("[dup] ArgumentException");
        }

        // A missing key get_Item -> KeyNotFoundException.
        try
        {
            var v = d[99L];
            Console.WriteLine("[miss] none:" + v);
        }
        catch (KeyNotFoundException)
        {
            Console.WriteLine("[miss] KeyNotFoundException");
        }

        // TryGetValue hit/miss.
        long got = 0;
        Console.WriteLine("[try1] " + d.TryGetValue(1L, out got) + ":" + got);
        got = 77;
        Console.WriteLine("[try9] " + d.TryGetValue(9L, out got) + ":" + got);

        // Remove + Count, Keys snapshot, Values snapshot.
        Console.WriteLine("[rm] " + d.Remove(2L) + ":" + d.Count);
        Console.WriteLine("[keys] " + string.Join(",", d.Keys));
        Console.WriteLine("[vals] " + string.Join(",", d.Values));

        // A long key whose value exceeds int32 range (the full 64-bit carrier).
        var d4 = new[] { 1L }.ToDictionary(x => x, x => 4000000001L);
        Console.WriteLine("[wide] " + d4[1L]);

        // A List<long> source.
        var src = new List<long> { 5L, 6L, 5L };
        try
        {
            var d5 = src.ToDictionary(x => x);
            Console.WriteLine("[l.cnt] " + d5.Count);
            Console.WriteLine("[l.k5] " + d5[5L]);
        }
        catch (ArgumentException)
        {
            Console.WriteLine("[l.dup] ArgumentException");
        }

        // A Dictionary<long,int> (the int64-key + int32-value twin).
        var f = new[] { 1L, 2L, 3L };
        var di = f.ToDictionary(x => x, x => (int)(x * 70000));
        Console.WriteLine("[i.cnt] " + di.Count);
        Console.WriteLine("[i.k1] " + di[1L]);
        Console.WriteLine("[i.k3] " + di[3L]);
        Console.WriteLine("[i.has2] " + di.ContainsKey(2L));
        Console.WriteLine("[i.has9] " + di.ContainsKey(9L));
        int igot = 0;
        Console.WriteLine("[i.try1] " + di.TryGetValue(1L, out igot) + ":" + igot);
        igot = 55;
        Console.WriteLine("[i.try9] " + di.TryGetValue(9L, out igot) + ":" + igot);
        Console.WriteLine("[i.rm] " + di.Remove(2L) + ":" + di.Count);
        Console.WriteLine("[i.keys] " + string.Join(",", di.Keys));
        Console.WriteLine("[i.vals] " + string.Join(",", di.Values));

        // A Dictionary<long,string> (the int64-key + reference-value twin).
        var g = new[] { 1L, 2L };
        var ds = g.ToDictionary(x => x, x => "v" + x);
        Console.WriteLine("[s.k1] " + ds[1L]);
        Console.WriteLine("[s.has2] " + ds.ContainsKey(2L));
        Console.WriteLine("[s.vals] " + string.Join(",", ds.Values));
    }
}
