using System;
using System.Collections.Generic;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var ia = new int[] { 5, 2, 9, 2, 1, 7, 4 };
        var la = new long[] { 501, 200, 900, 201, 100, 700 };

        // [i1] int OrderBy(k1).ThenBy(k2, user comparer) -- k2 via parity comparer
        Console.WriteLine("i1:" + string.Join(",", ia.OrderBy(x => x % 3).ThenBy(x => x, new ParityComparer())));
        // [i2] int OrderByDescending(k1).ThenByDescending(k2, comparer) -- both directions
        Console.WriteLine("i2:" + string.Join(",", ia.OrderByDescending(x => x % 3).ThenByDescending(x => x, new ParityComparer())));
        // [i3] mixed: OrderBy(k1).ThenByDescending(k2, comparer)
        Console.WriteLine("i3:" + string.Join(",", ia.OrderBy(x => x % 3).ThenByDescending(x => x, new ParityComparer())));
        // [i4] long source with a long-key parity comparer (i8 key cell)
        Console.WriteLine("i4:" + string.Join(",", la.OrderBy(x => x % 3).ThenBy(x => x, new ParityLongComparer())));
        // [i5] null comparer -- falls back to Comparer<TKey>.Default (net48)
        Console.WriteLine("i5:" + string.Join(",", ia.OrderBy(x => x % 3).ThenBy(x => x, (IComparer<int>)null)));
        // [i6] side-effect exactly-once: both selectors counted (the comparer
        // is invoked per comparison, whose COUNT is algorithm-specific -- net48
        // IntroSort vs the runtime's stable merge -- so only the SELECTOR
        // counts are pinned byte-exact, not cc).
        int s1 = 0, s2 = 0;
        var r6 = ia.OrderBy(x => { ++s1; return x % 3; })
                   .ThenBy(x => { ++s2; return x; }, new ParityComparer())
                   .ToArray();
        Console.WriteLine("i6:" + string.Join(",", r6) + "|s1=" + s1 + "|s2=" + s2);
        // [i7] GC.Collect inside the secondary selector (root survival)
        Console.WriteLine("i7:" + string.Join(",", ia.OrderBy(x => x % 3).ThenBy(x => Gc(x), new ParityComparer())));
        // [i8] empty source
        Console.WriteLine("i8:" + string.Join(",", new int[0].OrderBy(x => x % 3).ThenBy(x => x, new ParityComparer())));
        // [i9] single-element source
        Console.WriteLine("i9:" + string.Join(",", new int[] { 7 }.OrderBy(x => x % 3).ThenBy(x => x, new ParityComparer())));
        // [i10] dup keys stable under the comparer (k2 equal -> primary order kept)
        Console.WriteLine("i10:" + string.Join(",", ia.OrderBy(x => x % 3).ThenBy(x => x / 6, new ParityComparer())));
    }

    private static int Gc(int x)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return x;
    }

    private sealed class ParityComparer : IComparer<int>
    {
        public int Compare(int a, int b)
        {
            int pa = a & 1, pb = b & 1;
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }

    private sealed class ParityLongComparer : IComparer<long>
    {
        public int Compare(long a, long b)
        {
            int pa = (int)(a & 1), pb = (int)(b & 1);
            if (pa != pb) return pa.CompareTo(pb);
            return a.CompareTo(b);
        }
    }
}