using System;
using System.Collections.Generic;

// hseta2 -- HashSet<T> set algebra + constructors + CopyTo/TryGetValue.
// net48 surface: UnionWith / IntersectWith / ExceptWith / SymmetricExceptWith /
// SetEquals / IsSubsetOf / IsSupersetOf / IsProperSubsetOf /
// IsProperSupersetOf / Overlaps / CopyTo / TryGetValue / ctor(IEnumerable) /
// ctor(IEqualityComparer).
class Prog
{
    static void Dump(string tag, HashSet<int> s)
    {
        int[] a = new int[s.Count];
        s.CopyTo(a);
        Array.Sort(a);
        string joined = "";
        for (int i = 0; i < a.Length; i++) { if (i > 0) joined += ","; joined += a[i]; }
        Console.WriteLine(tag + "=[" + joined + "]");
    }

    static void Main()
    {
        HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3, 4, 5 });
        HashSet<int> b = new HashSet<int>(new int[] { 4, 5, 6, 7 });
        Console.WriteLine("a_count=" + a.Count);
        Console.WriteLine("b_count=" + b.Count);

        HashSet<int> u = new HashSet<int>(a);
        u.UnionWith(b);
        Dump("union", u);

        HashSet<int> i = new HashSet<int>(a);
        i.IntersectWith(b);
        Dump("inter", i);

        HashSet<int> e = new HashSet<int>(a);
        e.ExceptWith(b);
        Dump("except", e);

        HashSet<int> x = new HashSet<int>(a);
        x.SymmetricExceptWith(b);
        Dump("symdiff", x);

        Console.WriteLine("seteq=" + a.SetEquals(new int[] { 5, 4, 3, 2, 1 }));
        Console.WriteLine("seteq2=" + a.SetEquals(b));
        Console.WriteLine("subset=" + new HashSet<int>(new int[] { 2, 3 }).IsSubsetOf(a));
        Console.WriteLine("subset_self=" + a.IsSubsetOf(a));
        Console.WriteLine("propersub=" + a.IsProperSubsetOf(a));
        Console.WriteLine("propersub2=" + new HashSet<int>(new int[] { 1 }).IsProperSubsetOf(a));
        Console.WriteLine("superset=" + a.IsSupersetOf(new int[] { 1, 2 }));
        Console.WriteLine("propersuper=" + a.IsProperSupersetOf(new int[] { 1, 2 }));
        Console.WriteLine("propersuper2=" + a.IsProperSupersetOf(a));
        Console.WriteLine("overlaps=" + a.Overlaps(b));
        Console.WriteLine("overlaps2=" + a.Overlaps(new int[] { 9, 10 }));

        HashSet<int> t = new HashSet<int>(new int[] { 10, 20, 30 });
        int got;
        Console.WriteLine("tryget_hit=" + t.TryGetValue(20, out got) + ":" + got);
        Console.WriteLine("tryget_miss=" + t.TryGetValue(99, out got));

        int[] dst = new int[5];
        t.CopyTo(dst);
        Console.WriteLine("copyto0=" + dst[0] + "," + dst[1] + "," + dst[2]);

        HashSet<string> sa = new HashSet<string>(new string[] { "aa", "bb", "aa" });
        Console.WriteLine("str_ctor_count=" + sa.Count);
        Console.WriteLine("str_contains=" + sa.Contains("aa"));

        // ctor(capacity)
        HashSet<int> cap = new HashSet<int>(64);
        cap.Add(1);
        Console.WriteLine("cap_ok=" + cap.Count);

        // ctor(IEqualityComparer) -- ordinal-ignore-case comparer
        HashSet<string> ic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Console.WriteLine("ic_add_a=" + ic.Add("Alpha"));
        Console.WriteLine("ic_add_b=" + ic.Add("alpha"));
        Console.WriteLine("ic_count=" + ic.Count);
        Console.WriteLine("ic_has_upper=" + ic.Contains("ALPHA"));
    }
}
