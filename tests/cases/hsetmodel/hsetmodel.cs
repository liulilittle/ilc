using System;
using System.Collections.Generic;

// hsetmodel -- ORACLE-ONLY probe (run under net48 directly, not difftest):
// determine the exact HashSet<T> enumeration model so the runtime can mirror
// it. Prints the raw enumeration order for a sequence of mutations.
class Prog
{
    static void Show(string tag, HashSet<int> s)
    {
        string o = "";
        foreach (int v in s) o += v + ",";
        Console.WriteLine(tag + " -> " + o);
    }

    static void Main()
    {
        // M1: pure insert order?
        HashSet<int> a = new HashSet<int>();
        for (int i = 10; i < 16; i++) a.Add(i);
        Show("M1_insert10to15", a);

        // M2: remove middle then re-add (slot reuse?)
        HashSet<int> b = new HashSet<int>();
        for (int i = 0; i < 6; i++) b.Add(i);
        b.Remove(2);
        b.Remove(4);
        Show("M2_after_rm2_4", b);
        b.Add(99);
        Show("M2_add99", b);
        b.Add(98);
        Show("M2_add98", b);

        // M3: clear then refill (does Clear reset slot order?)
        b.Clear();
        b.Add(7);
        b.Add(6);
        b.Add(5);
        Show("M3_after_clear", b);

        // M4: growth: add 20, then remove some, then add (order across resize)
        HashSet<int> c = new HashSet<int>();
        for (int i = 0; i < 20; i++) c.Add(i * 7);
        Show("M4_20", c);
        c.Remove(0);
        c.Remove(14);
        Show("M4_rm", c);
        c.Add(1000);
        c.Add(1001);
        Show("M4_add2", c);

        // M5: string set, insertion order with remove/re-add
        HashSet<string> d = new HashSet<string>();
        d.Add("one"); d.Add("two"); d.Add("three"); d.Add("four");
        d.Remove("two");
        d.Add("five");
        d.Add("two");
        string o = "";
        foreach (string v in d) o += v + ",";
        Console.WriteLine("M5_str -> " + o);

        // M6: default Capacity of an empty HashSet? (observe via no API; use reflection-free)
        // Report the count of a set built from an enumerable with duplicates.
        HashSet<int> e = new HashSet<int>(new int[] { 3, 1, 3, 2, 1 });
        Show("M6_from_enum", e);
    }
}
