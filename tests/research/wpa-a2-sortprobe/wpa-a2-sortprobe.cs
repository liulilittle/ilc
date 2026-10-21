using System;
using System.Collections.Generic;

// wpa-a2-sortprobe -- audit A2 (1)(2)(3)(4): the Array.Sort/IndexOf guards must
// not over-reject. Each probe prints the OBSERVED outcome (ok / exception type),
// so the runtime output can be diffed against the intent below.
//
//   (1) length<=1 sorts of bool/char/decimal/Guid  -> no throw, array unchanged
//   (2) keys/items: a bad range must still raise the net48
//       ArgumentNullException / ArgumentOutOfRangeException (not NotSupported)
//   (3) IndexOf on an unsupported stride / empty array -> -1 (never throws)
//   (4) DateTime[] sort -> NOT a silent raw-int64 order (throws NotSupported)
public struct S16 { public long a; public long b; }

class Q
{
    static string T(Action a)
    {
        try { a(); return "ok"; }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    static void Main()
    {
        // (1) single-element / empty arrays: sorting must be a no-op.
        Console.WriteLine("[1] bool0   " + T(() => Array.Sort((Array)new bool[0])));
        Console.WriteLine("[1] bool1   " + T(() => { var b = new[] { true }; Array.Sort((Array)b); Console.Write(b[0] + " "); }));
        Console.WriteLine("[1] char1   " + T(() => { var c = new[] { 'x' }; Array.Sort((Array)c); Console.Write(c[0] + " "); }));
        Console.WriteLine("[1] dec0    " + T(() => Array.Sort((Array)new decimal[0])));
        Console.WriteLine("[1] guid1   " + T(() => { var g = new Guid[1]; Array.Sort((Array)g); Console.Write(g[0] == Guid.Empty ? "empty " : "bad "); }));
        Console.WriteLine("[1] dt1     " + T(() => { var d = new DateTime[1]; Array.Sort((Array)d); Console.Write(d[0].Year + " "); }));

        // (2) keys/items validation order.
        Console.WriteLine("[2] bad_range   " + T(() => Array.Sort((Array)new bool[2], (Array)new bool[2], -1, 1)));
        Console.WriteLine("[2] long_range  " + T(() => Array.Sort((Array)new bool[2], (Array)new bool[2], 1, 5)));

        // (3) IndexOf on unsupported strides / empty arrays -> -1.
        Console.WriteLine("[3] idx_empty   " + T(() => { var r = Array.IndexOf((Array)new bool[0], (object)true); Console.Write(r + " "); }));
        Console.WriteLine("[3] idx_bool    " + T(() => { var r = Array.IndexOf((Array)new[] { true, false }, (object)true); Console.Write(r + " "); }) + " (net48 0; runtime -1: bool cells have no recorded kind -- recorded gap)");
        Console.WriteLine("[3] idx_char    " + T(() => { var r = Array.IndexOf((Array)new[] { 'x', 'y' }, (object)'y'); Console.Write(r + " "); }) + " (net48 1; runtime -1: char cells have no recorded kind -- recorded gap)");
        Console.WriteLine("[3] idx_int     " + T(() => { var r = Array.IndexOf((Array)new[] { 5, 6 }, (object)6); Console.Write(r + " "); }));

        // (4) DateTime[] must never be ordered as a raw int64 (kind in the top
        // bits would make a Local value compare as a huge negative).
        Console.WriteLine("[4] dt_sort     " + T(() => { var d = new[] { new DateTime(2020, 1, 1), new DateTime(2019, 1, 1) }; Array.Sort((Array)d); Console.Write(d[0].Year + " "); }));
        // control: a runtime-built long[] (List<long>.ToArray) keeps its kind
        // and still sorts.
        Console.WriteLine("[4] long_sort   " + T(() => { var l = new List<long>(); l.Add(2); l.Add(1); var a = l.ToArray(); Array.Sort((Array)a); Console.Write(a[0] + " "); }));
    }
}
