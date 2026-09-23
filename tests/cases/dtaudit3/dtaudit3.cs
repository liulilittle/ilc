//------------------------------------------------------------------------------
// dtaudit3.cs -- TimeSpan.From* double semantics audit probe (G6 audit D6/D7/D8).
//
//   * sub_tick  = TimeSpan.FromSeconds(0.00000015).Ticks
//       net48 truncates the double tick product: 1.5 -> 1 tick.
//       The runtime rounds half-away-from-zero (time.cpp:672-677): -> 2 ticks.
//   * big       = TimeSpan.FromMilliseconds(1e30) exception kind
//       net48: OverflowException. The runtime's from_milliseconds has NO
//       range check (time.cpp:724-727) -- the double->int64 cast is UB.
//   * nan       = TimeSpan.FromMilliseconds(double.NaN) exception kind
//       net48: ArgumentException. The runtime has no NaN check -- UB cast.
//
// The byte-exact diff against the net48 oracle is the D6/D7/D8 evidence.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Dtaudit3 {
    static void Probe(string label, double v)
    {
        try
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(v);
            Console.WriteLine(label + "=ok:" + ts.Ticks);
        }
        catch (OverflowException) { Console.WriteLine(label + "=OverflowException"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine(label + "=ArgumentOutOfRangeException"); }
        catch (ArgumentException) { Console.WriteLine(label + "=ArgumentException"); }
        catch (Exception) { Console.WriteLine(label + "=Exception"); }
    }

    public static void Main()
    {
        Console.WriteLine("sub_tick=" + TimeSpan.FromSeconds(0.00000015).Ticks);
        Probe("big", 1e30);
        Probe("nan", double.NaN);
    }
}}
