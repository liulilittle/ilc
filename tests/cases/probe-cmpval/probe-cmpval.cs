using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var ia = new[] { 1, 3, 2, 4, 3 };
        var ib = new[] { 2, 4, 5 };

        // [i1] int Distinct with parity comparer: 1,3,2,4 (odd/even first-appearance)
        Console.WriteLine("i1:" + string.Join(",", ia.Distinct(new ParityIntComparer())));

        // [i2] int Except with parity: elements whose parity NOT in ib {2,4,5} -> 1,3
        Console.WriteLine("i2:" + string.Join(",", ia.Except(ib, new ParityIntComparer())));

        // [i3] int Union with parity: ia then ib dedup by parity -> 1,3,2,4,5
        Console.WriteLine("i3:" + string.Join(",", ia.Union(ib, new ParityIntComparer())));

        // [i4] int Intersect with parity: ia elements with parity in ib -> 3,2,4
        Console.WriteLine("i4:" + string.Join(",", ia.Intersect(ib, new ParityIntComparer())));

        // [i5] int Contains with parity
        Console.WriteLine("i5:" + ia.Contains(7, new ParityIntComparer()));
        Console.WriteLine("i6:" + ia.Contains(5, new ParityIntComparer()));

        // [i7] int SequenceEqual with parity
        Console.WriteLine("i7:" + ia.SequenceEqual(new[] { 11, 13, 21, 41, 33 }, new ParityIntComparer()));
        Console.WriteLine("i8:" + ia.SequenceEqual(new[] { 11 }, new ParityIntComparer()));

        // [l1] long Distinct with parity
        var la = new[] { 1L, 3L, 2L, 4L, 3L };
        Console.WriteLine("l1:" + string.Join(",", la.Distinct(new ParityLongComparer())));
        Console.WriteLine("l2:" + la.Contains(7L, new ParityLongComparer()));

        // [d1] double Distinct with parity on the integer part
        var da = new[] { 1.0, 3.0, 2.0, 4.0, 3.0 };
        Console.WriteLine("d1:" + string.Join(",", da.Distinct(new ParityDoubleComparer())));
        Console.WriteLine("d2:" + da.Contains(7.0, new ParityDoubleComparer()));

        // [n1] byte (narrow cell) Distinct with parity
        var ba = new byte[] { 1, 3, 2, 4, 3 };
        Console.WriteLine("n1:" + string.Join(",", ba.Distinct(new ParityByteComparer())));
    }

    private sealed class ParityIntComparer : IEqualityComparer<int>
    {
        public bool Equals(int a, int b) { return (a & 1) == (b & 1); }
        public int GetHashCode(int s) { return s & 1; }
    }

    private sealed class ParityLongComparer : IEqualityComparer<long>
    {
        public bool Equals(long a, long b) { return (a & 1) == (b & 1); }
        public int GetHashCode(long s) { return (int)(s & 1); }
    }

    private sealed class ParityDoubleComparer : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) { return ((long)a & 1) == ((long)b & 1); }
        public int GetHashCode(double s) { return (int)((long)s & 1); }
    }

    private sealed class ParityByteComparer : IEqualityComparer<byte>
    {
        public bool Equals(byte a, byte b) { return (a & 1) == (b & 1); }
        public int GetHashCode(byte s) { return s & 1; }
    }
}