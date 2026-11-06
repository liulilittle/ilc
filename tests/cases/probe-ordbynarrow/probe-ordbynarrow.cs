using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var ba = new byte[] { 5, 2, 9, 2, 1 };
        var sa = new sbyte[] { 3, -2, 7, -2, 5 };
        var ia = new short[] { 300, 50, 900, 50, 10 };
        var ua = new ushort[] { 65530, 5, 40000, 5, 60000 };
        var ca = new char[] { 'd', 'a', 'z', 'a', 'm' };
        var oa = new bool[] { true, false, false, true };

        // [b1] byte OrderBy default (numeric asc, stable)
        Console.WriteLine("b1:" + string.Join(",", ba.OrderBy(x => x)));
        // [b2] byte OrderByDescending default
        Console.WriteLine("b2:" + string.Join(",", ba.OrderByDescending(x => x)));
        // [b3] byte OrderBy with a byte-key comparer (parity then value)
        Console.WriteLine("b3:" + string.Join(",", ba.OrderBy(x => x, new ParityByteComparer())));
        // [b4] sbyte OrderBy with a sbyte-key comparer
        Console.WriteLine("b4:" + string.Join(",", sa.OrderBy(x => x, new ParitySbyteComparer())));
        // [b5] short OrderBy with a short-key comparer
        Console.WriteLine("b5:" + string.Join(",", ia.OrderBy(x => x, new ParityShortComparer())));
        // [b6] ushort OrderByDescending with a comparer
        Console.WriteLine("b6:" + string.Join(",", ua.OrderByDescending(x => x, new ParityUshortComparer())));
        // [b7] char OrderBy default (char key)
        Console.WriteLine("b7:" + string.Join(",", ca.OrderBy(x => x)));
        // [b8] char OrderByDescending with a char-key comparer
        Console.WriteLine("b8:" + string.Join(",", ca.OrderByDescending(x => x, new ParityCharComparer())));
        // [b9] bool OrderBy (false before true)
        Console.WriteLine("b9:" + string.Join(",", oa.OrderBy(x => x)));
        // [b10] byte OrderBy by an int projection then Select
        Console.WriteLine("b10:" + string.Join(",", ba.OrderBy(x => x * 2).ToArray()));
        // [b11] empty narrow source
        Console.WriteLine("b11:" + string.Join(",", new byte[0].OrderBy(x => x)));
        // [b12] single-element narrow source
        Console.WriteLine("b12:" + string.Join(",", new byte[] { 7 }.OrderBy(x => x)));
        // [b13] GC.Collect inside the selector (narrow root survival)
        Console.WriteLine("b13:" + string.Join(",", ba.OrderBy(x => Gc(x))));
        // [b14] byte OrderByDescending null comparer (default)
        Console.WriteLine("b14:" + string.Join(",", ba.OrderByDescending(x => x, (IComparer<byte>)null)));
        // [b15] short OrderBy with a REFERENCE string key + StringComparer
        Console.WriteLine("b15:" + string.Join(",", ia.OrderBy(x => x.ToString(), StringComparer.Ordinal)));
    }

    private static int Gc(int x)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return x;
    }

    private sealed class ParityByteComparer : IComparer<byte>
    {
        public int Compare(byte a, byte b)
        {
            int pa = a & 1, pb = b & 1;
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }

    private sealed class ParitySbyteComparer : IComparer<sbyte>
    {
        public int Compare(sbyte a, sbyte b)
        {
            int pa = a & 1, pb = b & 1;
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }

    private sealed class ParityShortComparer : IComparer<short>
    {
        public int Compare(short a, short b)
        {
            int pa = a & 1, pb = b & 1;
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }

    private sealed class ParityUshortComparer : IComparer<ushort>
    {
        public int Compare(ushort a, ushort b)
        {
            int pa = a & 1, pb = b & 1;
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }

    private sealed class ParityCharComparer : IComparer<char>
    {
        public int Compare(char a, char b)
        {
            int pa = a & 1, pb = b & 1;
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }
}