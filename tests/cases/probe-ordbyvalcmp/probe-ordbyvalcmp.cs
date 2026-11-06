using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var ia = new[] { 5, 2, 9, 2, 1, 7 };
        var la = new[] { 9L, 3L, 6L, 3L, 1L };
        var da = new[] { 4.0, 1.5, 3.5, 1.5, 2.0 };
        var fa = new[] { 2.0f, 9.0f, 4.0f, 9.0f, 1.0f };
        var sa = new[] { "aa", "b", "AAA", "d", "aa" };

        // [i1] int OrderBy value key + custom comparer (abs descending)
        Console.WriteLine("i1:" + string.Join(",", ia.OrderBy(x => x, new AbsDecComparer())));
        // [i2] int OrderByDescending value key + custom comparer
        Console.WriteLine("i2:" + string.Join(",", ia.OrderByDescending(x => x, new AbsDecComparer())));
        // [i3] int OrderBy with a null comparer == default numeric ascending (stable)
        Console.WriteLine("i3:" + string.Join(",", ia.OrderBy(x => x, (IComparer<int>)null)));
        // [i4] int OrderByDescending with a null comparer
        Console.WriteLine("i4:" + string.Join(",", ia.OrderByDescending(x => x, (IComparer<int>)null)));
        // [i5] int OrderBy a side-effecting key selector (key computed once per element)
        Console.WriteLine("i5:" + string.Join(",", ia.OrderBy(x => SideKey(x), new AbsDecComparer())));
        // [i6] long OrderBy value key + custom comparer
        Console.WriteLine("i6:" + string.Join(",", la.OrderBy(x => x, new AbsDecLongComparer())));
        // [i7] double OrderBy value key + comparer on integer part
        Console.WriteLine("i7:" + string.Join(",", da.OrderBy(x => x, new IntPartComparer())));
        // [i8] float OrderBy value key + comparer on integer part
        Console.WriteLine("i8:" + string.Join(",", fa.OrderBy(x => x, new IntPartFloatComparer())));
        // [i9] int source passed through a NARROW key (byte projection) + comparer
        Console.WriteLine("i9:" + string.Join(",", ia.OrderBy(x => (byte)x, new AbsDecByteComparer())));
        // [i10] int OrderBy ref key (string) with a class comparer
        Console.WriteLine("i10:" + string.Join(",", la.OrderBy(x => x.ToString(), new RefStrComparer())));
        // [i11] empty source
        Console.WriteLine("i11:" + string.Join(",", new int[0].OrderBy(x => x, new AbsDecComparer())));
        // [i12] single-element source
        Console.WriteLine("i12:" + string.Join(",", new[] { 7 }.OrderBy(x => x, new AbsDecComparer())));
        // [i13] GC.Collect inside the comparer (rooting proof: cmp object must survive)
        Console.WriteLine("i13:" + string.Join(",", ia.OrderBy(x => x, new GcCollectComparer())));
        // [i14] OrderBy a projection key later composed (2-arg keeps original element order on equal keys)
        var dup = new[] { 4, 4, 4, 4 };
        Console.WriteLine("i14:" + string.Join(",", dup.OrderBy(x => x, new AbsDecComparer())));
        // [i15] OrderByDescending with a string key and custom comparer
        Console.WriteLine("i15:" + string.Join(",", sa.OrderByDescending(x => x, new RevStrComparer())));
    }

    private static int SideKey(int x)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return x;
    }

    private sealed class AbsDecComparer : IComparer<int>
    {
        public int Compare(int a, int b) { return b.CompareTo(a); }
    }

    private sealed class AbsDecLongComparer : IComparer<long>
    {
        public int Compare(long a, long b) { return b.CompareTo(a); }
    }

    private sealed class IntPartComparer : IComparer<double>
    {
        public int Compare(double a, double b) { return ((long)a & 1).CompareTo((long)b & 1); }
    }

    private sealed class IntPartFloatComparer : IComparer<float>
    {
        public int Compare(float a, float b) { return ((long)a & 1).CompareTo((long)b & 1); }
    }

    private sealed class AbsDecByteComparer : IComparer<byte>
    {
        public int Compare(byte a, byte b) { return b.CompareTo(a); }
    }

    private sealed class RefStrComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
            return string.CompareOrdinal(a, b);
        }
    }

    private sealed class RevStrComparer : IComparer<string>
    {
        public int Compare(string a, string b) { return string.CompareOrdinal(b, a); }
    }

    private sealed class GcCollectComparer : IComparer<int>
    {
        public int Compare(int a, int b)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return b.CompareTo(a);
        }
    }
}