//------------------------------------------------------------------------------
// baudit4 -- G6 container-audit probe B4: ArrayList capacity + range bounds.
//
// Audit findings (arraylist.cpp):
//   * create() allocates the minimum 4-slot items array, so Capacity is 4 for a
//     fresh list (net48 ArrayList() keeps emptyArray -> Capacity 0);
//   * CopyTo/RemoveRange bound checks add in int32: index = int.MaxValue
//     overflows to a negative sum, bypassing the check (net48 throws);
//   * out-of-range CopyTo throws ArgumentOutOfRangeException here, net48
//     Array.Copy throws ArgumentException (and RemoveRange range overflow is
//     ArgumentException too).
//
// Expected diff: net48 prints cap=0 copy=ArgumentException
// rr=ArgumentException; the runtime prints cap=4, copy=ArgumentOutOfRangeException,
// and the RemoveRange probe either throws ArgumentOutOfRangeException or takes
// the overflow path past the checks (potential out-of-bounds write/crash).
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class Baudit4 {
    public static void Main()
    {
        ArrayList a = new ArrayList();
        Console.WriteLine("cap=" + a.Capacity);

        try
        {
            a.CopyTo(new object[1], int.MaxValue);
            Console.WriteLine("copy=no-throw");
        }
        catch (Exception e)
        {
            Console.WriteLine("copy=" + e.GetType().Name);
        }

        try
        {
            a.RemoveRange(int.MaxValue, 1);
            Console.WriteLine("rr=no-throw");
        }
        catch (Exception e)
        {
            Console.WriteLine("rr=" + e.GetType().Name);
        }
    }
} }
