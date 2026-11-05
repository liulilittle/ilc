using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public static class P
{
    public static void Main()
    {
        var src = new[] { "a", "A", "b", "c", "B" };
        var other = new[] { "a", "b", "d" };

        // [d1] Distinct with OrdinalIgnoreCase: "a","b","c" (a/A one bucket)
        Console.WriteLine("d:" + string.Join(",", src.Distinct(StringComparer.OrdinalIgnoreCase)));

        // [d2] Distinct with a custom user comparer
        Console.WriteLine("d3:" + string.Join(",", src.Distinct(new FirstCharComparer())));

        // [c1] Contains with OrdinalIgnoreCase (value among ignore-case peers)
        Console.WriteLine("c:" + src.Contains("A", StringComparer.OrdinalIgnoreCase));
        Console.WriteLine("c2:" + src.Contains("z", StringComparer.OrdinalIgnoreCase));

        // [c2] Contains with custom comparer
        Console.WriteLine("c3:" + src.Contains("aa", new FirstCharComparer()));

        // [s1] SequenceEqual with OrdinalIgnoreCase
        Console.WriteLine("s:" + src.SequenceEqual(new[] { "A", "a", "B", "C", "b" }, StringComparer.OrdinalIgnoreCase));
        Console.WriteLine("s2:" + src.SequenceEqual(new[] { "A", "a", "B" }, StringComparer.OrdinalIgnoreCase));

        // [u1] Union / Except / Intersect with OrdinalIgnoreCase
        Console.WriteLine("u:" + string.Join(",", src.Union(new[] { "D", "a" }, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine("e:" + string.Join(",", src.Except(other, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine("i:" + string.Join(",", src.Intersect(other, StringComparer.OrdinalIgnoreCase)));

        // [o1] OrderBy(string key, IComparer) OrdinalIgnoreCase: "A","a","b","B","c"
        Console.WriteLine("o:" + string.Join(",", src.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine("od:" + string.Join(",", src.OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)));

        // [o2] OrderBy with custom user comparer
        Console.WriteLine("o2:" + string.Join(",", new[] { "bbb", "a", "cc" }.OrderBy(x => x, new LengthComparer())));

        // [g1] GroupBy ref key with comparer (case-insensitive buckets, key = first form)
        Console.WriteLine("g:" + string.Join(",", src.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine("g2:" + string.Join(",", src.GroupBy(x => x, new FirstCharComparer()).Select(g => g.Key + ":" + g.Count())));
    }

    private sealed class FirstCharComparer : IEqualityComparer<string>
    {
        public bool Equals(string a, string b)
        {
            if (a == null) return b == null;
            if (b == null) return false;
            return char.ToLowerInvariant(a[0]) == char.ToLowerInvariant(b[0]);
        }

        public int GetHashCode(string s)
        {
            return s == null ? 0 : char.ToLowerInvariant(s[0]).GetHashCode();
        }
    }

    private sealed class LengthComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            return a.Length.CompareTo(b.Length);
        }
    }
}