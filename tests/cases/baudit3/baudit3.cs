//------------------------------------------------------------------------------
// baudit3 -- G6 container-audit probe B3: default-comparer primitive carriers.
//
// Audit findings in ComparerCompareCore (arraysort.cpp):
//   * Int64 shares the UInt64 unsigned branch -> a negative long compares as a
//     huge unsigned value;
//   * System.Single is read back as an 8-byte double from a 4-byte boxed float;
//   * a mixed primitive pair (Int32 vs String) reinterprets the other payload
//     instead of throwing;
//   * (net48 Comparer.Compare: Int64 signed; Single as float; Int32.CompareTo("a")
//     throws ArgumentException "Object must be of type Int32").
//
// Expected diff: net48 prints longs=-3,5 singles=0.5,1.5 mixed=ArgumentException;
// the runtime prints a mis-ordered longs pair, a garbage-ordered singles pair and
// mixed=no-throw.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class Baudit3 {
    public static void Main()
    {
        object[] longs = new object[] { 5L, -3L };
        Array.Sort(longs, Comparer.Default);
        Console.WriteLine("longs=" + (long)longs[0] + "," + (long)longs[1]);

        object[] singles = new object[] { 1.5f, 0.5f };
        Array.Sort(singles, Comparer.Default);
        Console.WriteLine("singles=" + (float)singles[0] + "," + (float)singles[1]);

        try
        {
            object[] mixed = new object[] { 1, "a" };
            Array.Sort(mixed, Comparer.Default);
            Console.WriteLine("mixed=no-throw");
        }
        catch (Exception e)
        {
            Console.WriteLine("mixed=" + e.GetType().Name);
        }
    }
} }
