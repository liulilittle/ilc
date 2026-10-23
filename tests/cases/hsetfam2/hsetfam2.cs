using System;
using System.Collections.Generic;

class Prog
{
    static void DumpI4(string tag, HashSet<int> s)
    {
        string o = "";
        foreach (int v in s) o += v + ",";
        Console.WriteLine(tag + "=" + s.Count + "|" + o);
    }

    static void Main()
    {
        HashSet<int> i4 = new HashSet<int>();
        i4.Add(30); i4.Add(10); i4.Add(20); i4.Add(10);
        DumpI4("i4", i4);
        Console.WriteLine("i4_has=" + i4.Contains(20) + "/" + i4.Contains(40));
    }
}
