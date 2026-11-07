using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var ba = new byte[] { 5, 2, 9, 2, 1, 7 };
        var sa = new sbyte[] { 3, -2, 7, -2, 5, 3 };
        var ia = new short[] { 300, 50, 900, 50, 10, 300 };
        var ua = new ushort[] { 65530, 5, 40000, 5, 60000, 5 };
        var ca = new char[] { 'd', 'a', 'z', 'a', 'm', 'd' };
        var oa = new bool[] { true, false, false, true, false };

        // [n1] byte OrderBy(k1).ThenBy(k2) -- primary asc, secondary asc
        Console.WriteLine("n1:" + string.Join(",", ba.OrderBy(x => x % 3).ThenBy(x => x)));
        // [n2] byte OrderByDescending(k1).ThenBy(k2) -- mixed directions
        Console.WriteLine("n2:" + string.Join(",", ba.OrderByDescending(x => x % 3).ThenBy(x => x)));
        // [n3] byte OrderBy(k1).ThenByDescending(k2)
        Console.WriteLine("n3:" + string.Join(",", ba.OrderBy(x => x % 3).ThenByDescending(x => x)));
        // [n4] sbyte parity primary, then value -- includes a negative key
        Console.WriteLine("n4:" + string.Join(",", sa.OrderBy(x => x % 2).ThenBy(x => x)));
        // [n5] short parity key + value secondary
        Console.WriteLine("n5:" + string.Join(",", ia.OrderBy(x => x % 2).ThenBy(x => x)));
        // [n6] ushort 3-level chain OrderBy(k1).ThenBy(k2).ThenBy(k3)
        Console.WriteLine("n6:" + string.Join(",", ua.OrderBy(x => x % 7).ThenBy(x => x % 3).ThenBy(x => x)));
        // [n7] char primary/secondary
        Console.WriteLine("n7:" + string.Join(",", ca.OrderBy(x => x % 2).ThenByDescending(x => x)));
        // [n8] bool OrderBy then ThenBy -- stability within duplicates
        Console.WriteLine("n8:" + string.Join(",", oa.OrderBy(x => x).ThenBy(x => x)));
        // [n9] side-effect selector counting: exactly-once per level per element
        int c1 = 0, c2 = 0;
        var r9 = ba.OrderBy(x => { ++c1; return x % 2; }).ThenBy(x => { ++c2; return x; }).ToArray();
        Console.WriteLine("n9:" + string.Join(",", r9) + "|c1=" + c1 + "|c2=" + c2);
        // [n10] GC.Collect inside both selectors (narrow root survival)
        Console.WriteLine("n10:" + string.Join(",", ba.OrderBy(x => Gc(x % 3)).ThenBy(x => Gc(x))));
        // [n11] empty narrow source
        Console.WriteLine("n11:" + string.Join(",", new byte[0].OrderBy(x => x).ThenBy(x => x)));
        // [n12] single-element narrow source
        Console.WriteLine("n12:" + string.Join(",", new byte[] { 7 }.OrderBy(x => x).ThenBy(x => x)));
        // [n13] 3-level over byte[] with descending second level
        Console.WriteLine("n13:" + string.Join(",", ba.OrderBy(x => x % 2).ThenByDescending(x => x % 3).ThenBy(x => x)));
    }

    private static int Gc(int x)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return x;
    }
}