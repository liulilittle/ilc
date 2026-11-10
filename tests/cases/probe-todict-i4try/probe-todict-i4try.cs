using System;
using System.Collections.Generic;

static class P
{
    static void Main()
    {
        // Dictionary<int,int> -- the iiReceiver int32-value TryGetValue.
        var d = new Dictionary<int, int> { { 1, 10 }, { 2, 20 } };
        int got = 0;
        Console.WriteLine("[i.hit] " + d.TryGetValue(1, out got) + ":" + got);
        got = 99;
        Console.WriteLine("[i.miss] " + d.TryGetValue(9, out got) + ":" + got);

        // Dictionary<string,int> -- the siValueGenReceiver int32-value
        // TryGetValue (the LINQ ToDictionary string->int32 wrapper).
        var s = new Dictionary<string, int> { { "a", 5 } };
        int sgot = 0;
        Console.WriteLine("[s.hit] " + s.TryGetValue("a", out sgot) + ":" + sgot);
        sgot = 77;
        Console.WriteLine("[s.miss] " + s.TryGetValue("z", out sgot) + ":" + sgot);

        // A pre-set out local must be overwritten with default(TValue) on a miss
        // (net48: out is default(TValue) on a miss, not the caller's prior value).
        var e = new Dictionary<int, int> { { 1, 10 } };
        int preset = 123;
        Console.WriteLine("[i.miss.preset] " + e.TryGetValue(42, out preset) + ":" + preset);
    }
}
