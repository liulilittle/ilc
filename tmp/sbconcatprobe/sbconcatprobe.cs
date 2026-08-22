using System;
using System.Text;
namespace Probe { public static class SbConcatProbe {
    public static void Main() {
        // Shape A: append the concat RESULT directly (the string is the
        // icall arg -- the #114 combination crash candidate).
        StringBuilder a = new StringBuilder(8);
        string p = "n=";
        for (int i = 0; i < 40000; i++) {
            a.Append(p + i);
        }
        Console.WriteLine(a.Length > 200000);
        // Shape B: concat result into a local, then append (the local
        // survives the append's buffer reallocation).
        StringBuilder b = new StringBuilder(8);
        for (int i = 0; i < 40000; i++) {
            string s = p + i;
            b.Append(s);
        }
        Console.WriteLine(b.Length > 200000);
        // Shape C: interleave GC.Collect with the concat+append pair.
        StringBuilder c = new StringBuilder(8);
        for (int i = 0; i < 10000; i++) {
            c.Append(p + i);
            if (i % 100 == 0) GC.Collect();
        }
        Console.WriteLine(c.Length > 200000);
        // Shape D: double concat + append (two allocations per append).
        StringBuilder d = new StringBuilder(8);
        string q = "!";
        for (int i = 0; i < 20000; i++) {
            d.Append(p + i + q);
        }
        Console.WriteLine(d.Length > 200000);
    }
}}
