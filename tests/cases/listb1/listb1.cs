using System;
using System.Collections.Generic;

// listb1 -- List<int> Capacity semantics + range operations (net48 oracle).
// net48: default Capacity 0, first Add -> 4, doubling; set_Capacity below
// Count throws ArgumentOutOfRangeException; AddRange/InsertRange/RemoveRange/
// GetRange/RemoveAll/LastIndexOf/Find* /Sort overloads/BinarySearch/Reverse.
class Prog
{
    static string J(List<int> l)
    {
        string s = "";
        for (int i = 0; i < l.Count; i++) { if (i > 0) s += ","; s += l[i]; }
        return "[" + s + "]";
    }

    static void Main()
    {
        List<int> l = new List<int>();
        Console.WriteLine("cap0=" + l.Capacity);
        l.Add(1);
        Console.WriteLine("cap1=" + l.Capacity);
        l.Add(2); l.Add(3); l.Add(4);
        Console.WriteLine("cap4=" + l.Capacity);
        l.Add(5);
        Console.WriteLine("cap5=" + l.Capacity);
        l.Capacity = 20;
        Console.WriteLine("cap20=" + l.Capacity + " count=" + l.Count);
        try { l.Capacity = 3; Console.WriteLine("capset_small=no_throw"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("capset_small=aoore"); }
        Console.WriteLine("cap_after=" + l.Capacity);

        List<int> c = new List<int>(10);
        Console.WriteLine("ctor10=" + c.Capacity + " count=" + c.Count);

        List<int> r = new List<int>(new int[] { 1, 2, 3 });
        r.AddRange(new int[] { 4, 5 });
        Console.WriteLine("addrange=" + J(r) + " cap=" + r.Capacity);
        r.InsertRange(1, new int[] { 9, 8 });
        Console.WriteLine("insertrange=" + J(r));
        r.RemoveRange(1, 3);
        Console.WriteLine("removerange=" + J(r));
        List<int> g = r.GetRange(1, 2);
        Console.WriteLine("getrange=" + J(g) + " count=" + g.Count);
        Console.WriteLine("removeall=" + r.RemoveAll(x => x % 2 == 0) + " " + J(r));

        List<int> q = new List<int>(new int[] { 5, 3, 9, 3, 1 });
        Console.WriteLine("lastidx=" + q.LastIndexOf(3));
        Console.WriteLine("firstidx=" + q.IndexOf(3));
        Console.WriteLine("find=" + q.Find(x => x > 4));
        Console.WriteLine("findlast=" + q.FindLast(x => x > 4));
        Console.WriteLine("findidx=" + q.FindIndex(x => x == 9));
        Console.WriteLine("findlastidx=" + q.FindLastIndex(x => x == 3));
        Console.WriteLine("exists=" + q.Exists(x => x == 9));
        Console.WriteLine("trueforall=" + q.TrueForAll(x => x > 0));
        int sum = 0;
        q.ForEach(x => sum += x);
        Console.WriteLine("foreach_sum=" + sum);
        List<int> fa = q.FindAll(x => x > 3);
        Console.WriteLine("findall=" + J(fa));

        List<int> s = new List<int>(new int[] { 5, 3, 9, 1 });
        s.Sort();
        Console.WriteLine("sort=" + J(s));
        s.Reverse();
        Console.WriteLine("reverse=" + J(s));
        s.Reverse(1, 2);
        Console.WriteLine("reverse_range=" + J(s));

        List<int> bs = new List<int>(new int[] { 1, 3, 5, 7 });
        Console.WriteLine("bin_3=" + bs.BinarySearch(3));
        Console.WriteLine("bin_4=" + bs.BinarySearch(4));
        Console.WriteLine("bin_0=" + bs.BinarySearch(0));
        Console.WriteLine("bin_9=" + bs.BinarySearch(9));

        List<int> cmp = new List<int>(new int[] { 1, 5, 2, 4 });
        cmp.Sort((x, y) => y.CompareTo(x));
        Console.WriteLine("sort_cmp=" + J(cmp));
        cmp.Sort(0, 2, Comparer<int>.Default);
        Console.WriteLine("sort_range=" + J(cmp));

        int[] dst = new int[6];
        bs.CopyTo(dst, 1);
        Console.WriteLine("copyto=" + dst[1] + "," + dst[2] + "," + dst[3] + "," + dst[4]);
        int[] arr = bs.ToArray();
        Console.WriteLine("toarray_len=" + arr.Length + " last=" + arr[3]);

        List<int> rm = new List<int>(new int[] { 1, 2, 3, 4, 5 });
        rm.RemoveAt(0);
        rm.Remove(3);
        Console.WriteLine("remove=" + J(rm) + " contains=" + rm.Contains(5));
        rm.Clear();
        Console.WriteLine("clear=" + rm.Count + " cap=" + rm.Capacity);
    }
}
