using System;
using System.Text;
namespace Cases { public static class Concat114xD4Case {
    public static void Main() {
        string p = "n=";
        // D1: number-first append at 100k scale (fold-defeat, unfolded path)
        Console.WriteLine("D1");
        StringBuilder d1 = new StringBuilder();
        for (int i = 0; i < 100000; i++) { d1.Append(i + p); }
        Console.WriteLine(d1.Length);
        Console.WriteLine(d1.Length > 600000);
        // D2: null string concat + append -- null + i, p + null, Append(null)
        Console.WriteLine("D2");
        string np = null;
        StringBuilder d2 = new StringBuilder();
        for (int i = 0; i < 100; i++) {
            d2.Append(np + i);
            d2.Append(p + np);
            d2.Append(np);
        }
        Console.WriteLine(d2.Length);
        Console.WriteLine(d2.ToString().Length == d2.Length);
        // D3: self-snapshot 3-arg concat -- sb.Append(p + i + sb.ToString())
        // (full-self append doubles per iteration -- bounded to 18)
        Console.WriteLine("D3");
        StringBuilder d3 = new StringBuilder("s");
        for (int i = 0; i < 18; i++) { d3.Append(p + i + d3.ToString()); }
        Console.WriteLine(d3.Length);
        Console.WriteLine(d3.Length > 1000000);
        // D4: Insert(0, concat) at 100k scale (insert + realloc churn)
        Console.WriteLine("D4");
        StringBuilder d4 = new StringBuilder();
        for (int i = 0; i < 100000; i++) { d4.Insert(0, p + i); }
        Console.WriteLine(d4.Length);
        Console.WriteLine(d4.Length > 600000);
        // D5: Replace with concat-built strings
        Console.WriteLine("D5");
        StringBuilder d5 = new StringBuilder("abc");
        for (int i = 0; i < 500; i++) {
            d5.Append(p + i);
            d5.Replace("n=", "N:" + i);
            d5.Append(d5.ToString().Length % 10);
        }
        Console.WriteLine(d5.Length);
        Console.WriteLine(d5.Length > 5000);
        // D6: Append(concat of two ToString results)
        Console.WriteLine("D6");
        StringBuilder d6 = new StringBuilder();
        for (int i = 0; i < 20000; i++) { d6.Append(i.ToString() + (i * 3).ToString()); }
        Console.WriteLine(d6.Length);
        Console.WriteLine(d6.Length > 100000);
        // D7: 4-operand mixed concat (concat_many path)
        Console.WriteLine("D7");
        StringBuilder d7 = new StringBuilder();
        for (int i = 0; i < 20000; i++) { d7.Append("a" + i + "b" + i); }
        Console.WriteLine(d7.Length);
        Console.WriteLine(d7.Length > 200000);
        // D8: char concat with NUL + Append('\0')
        Console.WriteLine("D8");
        char n0 = '\0';
        StringBuilder d8 = new StringBuilder();
        for (int i = 0; i < 1000; i++) {
            d8.Append(p + n0);
            d8.Append('\0');
        }
        Console.WriteLine(d8.Length);
        Console.WriteLine(d8.Length > 3000);
        // D9: chained AppendFormat 3-arg with concat args
        Console.WriteLine("D9");
        StringBuilder d9 = new StringBuilder();
        for (int i = 0; i < 2000; i++) {
            d9.AppendFormat("{0}|{1}|{2}", p + i, i * 2, i % 3 == 0);
        }
        Console.WriteLine(d9.Length);
        Console.WriteLine(d9.Length > 30000);
        // D10: Insert at END (index == Length) with concat -- no-shift path
        Console.WriteLine("D10");
        StringBuilder d10 = new StringBuilder("tail");
        for (int i = 0; i < 10000; i++) { d10.Insert(d10.Length, p + i); }
        Console.WriteLine(d10.Length);
        Console.WriteLine(d10.Length > 50000);
    }
}}
