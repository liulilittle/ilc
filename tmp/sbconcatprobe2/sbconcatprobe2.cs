using System;
using System.Text;
namespace Probe { public static class SbConcatProbe2 {
    public static void Main() {
        // Shape E: AppendFormat with a concat arg (composite formatting
        // allocates internally + the concat arg).
        StringBuilder e = new StringBuilder(8);
        string p = "n=";
        for (int i = 0; i < 20000; i++) {
            e.AppendFormat("{0}", p + i);
        }
        Console.WriteLine(e.Length > 100000);
        // Shape F: 100k scale concat+Append(string).
        StringBuilder f = new StringBuilder(8);
        for (int i = 0; i < 100000; i++) {
            f.Append(p + i);
        }
        Console.WriteLine(f.Length > 500000);
        // Shape G: sb grows to a big buffer, then concat+append churn.
        StringBuilder g = new StringBuilder(16);
        for (int i = 0; i < 5000; i++) g.Append("abcdefghij"); // 50k chars
        for (int i = 0; i < 40000; i++) {
            g.Append(p + i);
        }
        Console.WriteLine(g.Length > 200000);
        // Shape H: two SBs interleaved with concat (cross-sb churn).
        StringBuilder h1 = new StringBuilder(8);
        StringBuilder h2 = new StringBuilder(8);
        for (int i = 0; i < 20000; i++) {
            h1.Append(p + i);
            h2.Append(p + (i * 2));
        }
        Console.WriteLine(h1.Length > 100000 && h2.Length > 100000);
    }
}}
