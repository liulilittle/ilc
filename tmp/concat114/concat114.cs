using System;
using System.Text;
namespace Cases { public static class Concat114Case {
    public static void Main() {
        // S1: string + int concat (the #114 concat+sb combination candidate: p + i)
        Console.WriteLine("S1");
        string p1 = "n=";
        for (int i = 0; i < 8; i++) { Console.WriteLine(p1 + i); }
        // S2: string + StringBuilder.ToString()
        Console.WriteLine("S2");
        StringBuilder sb2 = new StringBuilder("inner");
        for (int i = 0; i < 8; i++) { Console.WriteLine("w=" + sb2.ToString() + "-" + i); }
        // S3: sb.Append(const string)
        Console.WriteLine("S3");
        StringBuilder sb3 = new StringBuilder();
        for (int i = 0; i < 8; i++) { sb3.Append("const"); }
        Console.WriteLine(sb3.Length);
        // S4: sb.Append(variable string)
        Console.WriteLine("S4");
        string v4 = "var";
        StringBuilder sb4 = new StringBuilder();
        for (int i = 0; i < 8; i++) { sb4.Append(v4); }
        Console.WriteLine(sb4.Length);
        // S5: sb.Append chain
        Console.WriteLine("S5");
        StringBuilder sb5 = new StringBuilder();
        for (int i = 0; i < 8; i++) { sb5.Append("a").Append(i).Append("b"); }
        Console.WriteLine(sb5.Length);
        // S6: nested concat (inner concat result feeds outer concat)
        Console.WriteLine("S6");
        for (int i = 0; i < 8; i++) { Console.WriteLine("x=" + ("y" + i + "z")); }
        // S7: concat(sb.ToString(), number)
        Console.WriteLine("S7");
        StringBuilder sb7 = new StringBuilder("acc");
        for (int i = 0; i < 8; i++) { Console.WriteLine(sb7.ToString() + i); }
        // S8: loop append (concat result as direct Append argument, 4000x scale)
        Console.WriteLine("S8");
        StringBuilder sb8 = new StringBuilder();
        string p8 = "p=";
        for (int i = 0; i < 4000; i++) { sb8.Append(p8 + i); }
        Console.WriteLine(sb8.Length);
        Console.WriteLine(sb8.Length > 20000);
    }
}}
