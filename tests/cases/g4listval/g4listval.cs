using System;
using System.Collections.Generic;
using System.Globalization;

// g4listval -- W5 probe: List<float/bool/char/byte/uint> (small-value
// domains). Full surface per kind: Add/Count/indexer/set_Item/IndexOf/
// Contains/Remove/ToArray/Sort/foreach. Deterministic: InvariantCulture,
// no time/random/paths.
class Prog
{
    static void Main()
    {
        List<float> fl = new List<float>();
        fl.Add(1.5f);
        fl.Add(3.25f);
        fl.Add(-2.5f);
        Console.WriteLine("fcount=" + fl.Count);
        Console.WriteLine("fget0=" + fl[0].ToString(CultureInfo.InvariantCulture));
        fl[1] = 4.5f;
        Console.WriteLine("fset1=" + fl[1].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("fcontains=" + fl.Contains(4.5f));
        Console.WriteLine("findexof=" + fl.IndexOf(-2.5f));
        Console.WriteLine("fremoved=" + fl.Remove(1.5f));
        Console.WriteLine("fcount2=" + fl.Count);
        fl.Sort();
        Console.WriteLine("fsorted0=" + fl[0].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("fsorted1=" + fl[1].ToString(CultureInfo.InvariantCulture));
        float[] farr = fl.ToArray();
        Console.WriteLine("farrlen=" + farr.Length);
        foreach (float v in fl)
            Console.WriteLine("feach=" + v.ToString(CultureInfo.InvariantCulture));

        List<bool> bl = new List<bool>();
        bl.Add(true);
        bl.Add(false);
        bl.Add(true);
        Console.WriteLine("bcount=" + bl.Count);
        Console.WriteLine("bget1=" + bl[1]);
        Console.WriteLine("bcontains=" + bl.Contains(false));
        Console.WriteLine("bindexof=" + bl.IndexOf(true));
        Console.WriteLine("bremoved=" + bl.Remove(true));
        Console.WriteLine("bcount2=" + bl.Count);
        bool[] barr = bl.ToArray();
        Console.WriteLine("barrlen=" + barr.Length);

        List<char> cl = new List<char>();
        cl.Add('a');
        cl.Add('Z');
        Console.WriteLine("ccount=" + cl.Count);
        Console.WriteLine("cget0=" + cl[0]);
        Console.WriteLine("ccontains=" + cl.Contains('Z'));
        Console.WriteLine("cindexof=" + cl.IndexOf('a'));
        char[] carr = cl.ToArray();
        Console.WriteLine("carrlen=" + carr.Length);
        Console.WriteLine("carr1=" + carr[1]);

        List<byte> yl = new List<byte>();
        yl.Add(0);
        yl.Add(255);
        yl.Add(17);
        Console.WriteLine("ycount=" + yl.Count);
        Console.WriteLine("yget1=" + yl[1]);
        Console.WriteLine("ycontains=" + yl.Contains(17));
        Console.WriteLine("yindexof=" + yl.IndexOf(255));
        Console.WriteLine("yremoved=" + yl.Remove(0));
        byte[] yarr = yl.ToArray();
        Console.WriteLine("yarrlen=" + yarr.Length);

        List<uint> ul = new List<uint>();
        ul.Add(0u);
        ul.Add(4294967295u);
        ul.Add(3000000000u);
        Console.WriteLine("ucount=" + ul.Count);
        Console.WriteLine("uget1eq=" + (ul[1] == 4294967295u));
        Console.WriteLine("ucontains=" + ul.Contains(3000000000u));
        Console.WriteLine("uindexof=" + ul.IndexOf(4294967295u));
        ul.Sort();
        Console.WriteLine("usorted0=" + ul[0].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("usorted2eq=" + (ul[2] == 4294967295u));
        uint[] uarr = ul.ToArray();
        Console.WriteLine("uarrlen=" + uarr.Length);

        Console.WriteLine("done");
    }
}
