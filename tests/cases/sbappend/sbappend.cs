using System;
using System.Text;
namespace Cases { public static class SbAppendCase {
    public static void Main() {
        StringBuilder sb = new StringBuilder(16);
        for (int i = 0; i < 40000; i++) {
            sb.Append((long)i);
        }
        Console.WriteLine(sb.Length);
        Console.WriteLine(sb.Length > 200000);
        // concat+sb combination (batch 154d crash #114 repro): the concat
        // result as the DIRECT Append argument (the string is the icall arg).
        string p = "n=";
        for (int i = 0; i < 40000; i++) {
            string s = p + i;
            sb.Append((long)s.Length);
        }
        Console.WriteLine(sb.Length > 200000);
        // batch 157: Append(concat-result) directly -- the string flows into
        // the append icall (the #114 combination-interaction candidate).
        StringBuilder a = new StringBuilder(8);
        for (int i = 0; i < 40000; i++) {
            a.Append(p + i);
        }
        Console.WriteLine(a.Length > 200000);
        // batch 157: double concat + append (two allocations per append).
        string q = "!";
        StringBuilder d = new StringBuilder(8);
        for (int i = 0; i < 20000; i++) {
            d.Append(p + i + q);
        }
        Console.WriteLine(d.Length > 200000);
        // batch 157: two SBs interleaved with concat (cross-sb churn).
        StringBuilder h1 = new StringBuilder(8);
        StringBuilder h2 = new StringBuilder(8);
        for (int i = 0; i < 20000; i++) {
            h1.Append(p + i);
            h2.Append(p + (i * 2));
        }
        Console.WriteLine(h1.Length > 100000 && h2.Length > 100000);
    }
}}
