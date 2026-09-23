using System;
using System.Collections.Generic;

// g4hashval -- T2 W3 probe: HashSet<long/double/uint/object/string>.
// net48 oracle byte-exact; enumeration sorted for determinism.
class Prog
{
    static void Main()
    {
        HashSet<long> hl = new HashSet<long>();
        Console.WriteLine("ladd3=" + hl.Add(3L));
        Console.WriteLine("ladd1=" + hl.Add(1L));
        Console.WriteLine("ladd2=" + hl.Add(2L));
        Console.WriteLine("ladd2dup=" + hl.Add(2L));
        Console.WriteLine("lcount=" + hl.Count);
        Console.WriteLine("lhas2=" + hl.Contains(2L));
        Console.WriteLine("lhas9=" + hl.Contains(9L));
        Console.WriteLine("lrem2=" + hl.Remove(2L));
        Console.WriteLine("lrem9=" + hl.Remove(9L));
        Console.WriteLine("lcount2=" + hl.Count);
        long[] la = new long[hl.Count];
        int ln = 0;
        foreach (long v in hl) la[ln++] = v;
        Array.Sort(la);
        Console.WriteLine("larrlen=" + la.Length);
        for (int i = 0; i < la.Length; i++)
            Console.WriteLine("litem=" + la[i]);

        HashSet<double> hd = new HashSet<double>();
        Console.WriteLine("dadd2=" + hd.Add(2.5));
        Console.WriteLine("dadd1=" + hd.Add(1.5));
        Console.WriteLine("dadd1dup=" + hd.Add(1.5));
        Console.WriteLine("dcount=" + hd.Count);
        Console.WriteLine("dhas15=" + hd.Contains(1.5));
        Console.WriteLine("drem15=" + hd.Remove(1.5));
        Console.WriteLine("dcount2=" + hd.Count);

        HashSet<uint> hu = new HashSet<uint>();
        Console.WriteLine("uadd0=" + hu.Add(0u));
        Console.WriteLine("uaddmax=" + hu.Add(uint.MaxValue));
        Console.WriteLine("uaddmaxdup=" + hu.Add(uint.MaxValue));
        Console.WriteLine("ucount=" + hu.Count);
        Console.WriteLine("uhasmax=" + hu.Contains(uint.MaxValue));
        Console.WriteLine("urem0=" + hu.Remove(0u));
        Console.WriteLine("ucount2=" + hu.Count);

        HashSet<string> hs = new HashSet<string>();
        string a1 = string.Join("", new string[] { "a", "b" });
        string a2 = string.Join("", new string[] { "a", "b" });
        Console.WriteLine("sadd1=" + hs.Add(a1));
        Console.WriteLine("sadd2samecontent=" + hs.Add(a2));
        Console.WriteLine("scount=" + hs.Count);

        HashSet<object> ho = new HashSet<object>();
        object o = new object();
        Console.WriteLine("oadd=" + ho.Add(o));
        Console.WriteLine("oadddup=" + ho.Add(o));
        Console.WriteLine("ocount=" + ho.Count);
        Console.WriteLine("ohas=" + ho.Contains(o));
        Console.WriteLine("ohasnew=" + ho.Contains(new object()));

        Console.WriteLine("done");
    }
}
