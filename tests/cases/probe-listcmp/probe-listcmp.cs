using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var li = new List<int> { 5, 2, 9, 2, 1, 7, 4, 2 };
        var ls = new List<string> { "Apple", "banana", "cherry", "Date", "apple", "Banana" };
        var ll = new List<long> { 500, 200, 900, 200, 100, 700 };

        // [l1] List<int>.Distinct(comparer) -- parity equality folds 2,4,6
        Console.WriteLine("l1:" + string.Join(",", li.Distinct(new ParityEqualityComparer())));
        // [l2] List<int>.Contains(value, comparer) -- parity match
        Console.WriteLine("l2:" + string.Join(",", li.Contains(8, new ParityEqualityComparer())));
        Console.WriteLine("l2b:" + string.Join(",", li.Contains(7, new ParityEqualityComparer())));
        // [l3] List<string>.Distinct(StringComparer.OrdinalIgnoreCase) -- case-fold
        Console.WriteLine("l3:" + string.Join(",", ls.Distinct(StringComparer.OrdinalIgnoreCase)));
        // [l4] List<int>.Union(other, comparer) -- parity first-appearance
        Console.WriteLine("l4:" + string.Join(",", li.Union(new int[] { 3, 6, 9 }, new ParityEqualityComparer())));
        // [l5] List<int>.Intersect(other, comparer)
        Console.WriteLine("l5:" + string.Join(",", li.Intersect(new int[] { 2, 3, 4, 8 }, new ParityEqualityComparer())));
        // [l6] List<int>.Except(other, comparer)
        Console.WriteLine("l6:" + string.Join(",", li.Except(new int[] { 2, 3, 5 }, new ParityEqualityComparer())));
        // [l7] List<long>.Contains(long, comparer) -- i8 parity
        Console.WriteLine("l7:" + string.Join(",", ll.Contains(300, new ParityLongEqualityComparer())));
        // [l8] List<int>.SequenceEqual(other, comparer) -- parity-wise equal
        Console.WriteLine("l8:" + string.Join(",", li.SequenceEqual(new int[] { 3, 6, 1, 5, 4, 2, 8, 2 }, new ParityEqualityComparer())));
        // [l9] empty List + comparer
        Console.WriteLine("l9:" + string.Join(",", new List<int>().Distinct(new ParityEqualityComparer())));
        // [l10] null comparer -- defaults to EqualityComparer<T>.Default
        Console.WriteLine("l10:" + string.Join(",", li.Distinct((IEqualityComparer<int>)null)));
        // [l11] GC.Collect inside the comparer (list element rooting)
        Console.WriteLine("l11:" + string.Join(",", li.Where(x => x % 2 != 0).Distinct(new GcEqualityComparer())));
    }

    private sealed class ParityEqualityComparer : IEqualityComparer<int>
    {
        public bool Equals(int a, int b) { return (a & 1) == (b & 1); }
        public int GetHashCode(int v) { return v & 1; }
    }

    private sealed class ParityLongEqualityComparer : IEqualityComparer<long>
    {
        public bool Equals(long a, long b) { return (a & 1) == (b & 1); }
        public int GetHashCode(long v) { return (int)(v & 1); }
    }

    private sealed class GcEqualityComparer : IEqualityComparer<int>
    {
        public bool Equals(int a, int b)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return (a & 1) == (b & 1);
        }
        public int GetHashCode(int v) { return v & 1; }
    }
}