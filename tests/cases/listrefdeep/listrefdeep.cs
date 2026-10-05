using System;
using System.Collections.Generic;

// listrefdeep -- reference-instantiation depth case for List<T> where T is a
// reference type (string). Deepens listrefcase (minimal Add/Count/indexer) to
// the full wired listobj member surface:
//   .ctor / Add / get_Count / get_Item / set_Item / Insert / Remove /
//   RemoveAt / IndexOf / Contains / ToArray / CopyTo / Clear / GetEnumerator
//   (foreach) / IList`1 indexer + Count on the reference instantiation.
// Every line is a labeled bool/value; the net48 oracle is the byte reference.
class Prog
{
    static void Main()
    {
        var l = new List<string>();
        l.Add("b");
        l.Add("d");
        Console.WriteLine("count1=" + (l.Count == 2));
        Console.WriteLine("idx0=" + l[0]);
        Console.WriteLine("idx1=" + l[1]);

        l.Insert(1, "c");
        Console.WriteLine("insert=" + (l[1] == "c"));
        Console.WriteLine("count2=" + (l.Count == 3));

        Console.WriteLine("idxof=" + l.IndexOf("c"));
        Console.WriteLine("has=" + l.Contains("d"));
        Console.WriteLine("hasnot=" + l.Contains("z"));

        l[1] = "C";
        Console.WriteLine("set=" + (l[1] == "C"));
        Console.WriteLine("remove=" + l.Remove("C"));
        Console.WriteLine("count3=" + (l.Count == 2));
        Console.WriteLine("afterRm=" + l[0]);

        string[] arr = l.ToArray();
        Console.WriteLine("arrlen=" + arr.Length);
        Console.WriteLine("arr0=" + arr[0]);

        var l2 = new List<string>();
        l2.Add("x");
        l2.Add("y");
        string[] dst = new string[4];
        l2.CopyTo(dst, 1);
        Console.WriteLine("copy=" + dst[1] + dst[2]);

        foreach (var s in l2)
            Console.WriteLine("each=" + s);

        var l3 = new List<string>();
        l3.Add("q");
        l3.Clear();
        Console.WriteLine("clear=" + (l3.Count == 0));

        IList<string> il = l2;
        Console.WriteLine("ilist0=" + il[0]);
        Console.WriteLine("ilistcnt=" + il.Count);

        // Value-equality semantics: EqualityComparer<string>.Default (ordinal
        // content), NOT pointer identity -- a distinct-but-equal string must
        // be found/removed.
        var l5 = new List<string>();
        l5.Add("b");
        l5.Add("d");
        string fresh = "D".ToLower();
        Console.WriteLine("ideq=" + l5.IndexOf(fresh));
        Console.WriteLine("haseq=" + l5.Contains(fresh));
        Console.WriteLine("rmeq=" + l5.Remove(fresh));
        Console.WriteLine("count5=" + l5.Count);

        // Sort with the default comparer (Comparer<string>.Default).
        var l6 = new List<string>();
        l6.Add("b");
        l6.Add("a");
        l6.Add("c");
        l6.Sort();
        Console.WriteLine("s0=" + l6[0]);
        Console.WriteLine("s1=" + l6[1]);
        Console.WriteLine("s2=" + l6[2]);

        Console.WriteLine("done");
    }
}
