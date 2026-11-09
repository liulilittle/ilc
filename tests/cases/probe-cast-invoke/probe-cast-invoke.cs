using System;
using System.Linq;

static class P
{
    static void Main()
    {
        // Cast<int> over boxed object[] with a boxed DOUBLE / INT64 / STRING /
        // BOOL element: net48's unbox.any throws InvalidCastException. The
        // message text is localized, so classify by TYPE.
        try { var a = new object[] { 1, 2.5, 3 }; Console.WriteLine("[c1] " + string.Join(",", a.Cast<int>())); }
        catch (InvalidCastException) { Console.WriteLine("[c1] InvalidCastException"); }

        try { var a = new object[] { 1L, 2L }; Console.WriteLine("[c2] " + string.Join(",", a.Cast<int>())); }
        catch (InvalidCastException) { Console.WriteLine("[c2] InvalidCastException"); }

        try { var a = new object[] { "x", 1 }; Console.WriteLine("[c3] " + string.Join(",", a.Cast<int>())); }
        catch (InvalidCastException) { Console.WriteLine("[c3] InvalidCastException"); }

        // Homogeneous int boxes work.
        var c = new object[] { 1, 2, 3 };
        Console.WriteLine("[c4] " + string.Join(",", c.Cast<int>()));

        // A boxed BOOL is not a boxed Int32.
        try { var a = new object[] { 1, true, 3 }; Console.WriteLine("[c5] " + string.Join(",", a.Cast<int>())); }
        catch (InvalidCastException) { Console.WriteLine("[c5] InvalidCastException"); }

        // Cast<double> over boxed doubles works.
        var e = new object[] { 1.5, 2.5 };
        Console.WriteLine("[c6] " + string.Join(",", e.Cast<double>()));

        // Cast<long> over boxed ints: net48 throws (an Int32 box is not an Int64).
        try { var a = new object[] { 1, 2 }; Console.WriteLine("[c7] " + string.Join(",", a.Cast<long>())); }
        catch (InvalidCastException) { Console.WriteLine("[c7] InvalidCastException"); }

        // OfType<int> DROPS the non-matching element (no throw).
        var b = new object[] { 1, "x", 2 };
        Console.WriteLine("[oft] " + string.Join(",", b.OfType<int>()));

        // Cast<int> over a null element: net48 throws NullReferenceException.
        try { var a = new object[] { 1, null, 2 }; Console.WriteLine("[c8] " + string.Join(",", a.Cast<int>())); }
        catch (NullReferenceException) { Console.WriteLine("[c8] NullReferenceException"); }
        catch (InvalidCastException) { Console.WriteLine("[c8] InvalidCastException"); }
    }
}
