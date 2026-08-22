using System;
using System.Text;
namespace Cases { public static class Concat114xB2Case {
    public static void Main() {
        // B1: bool concat path -- p + (i % 2 == 0) -> icall_string_concat_bool
        Console.WriteLine("B1");
        StringBuilder b1 = new StringBuilder();
        string p = "b=";
        for (int i = 0; i < 2000; i++) { b1.Append(p + (i % 2 == 0)); }
        Console.WriteLine(b1.Length);
        Console.WriteLine(b1.Length > 7000);
        // B2: double concat path -- p + (i * 0.5) -> icall_string_concat_double
        Console.WriteLine("B2");
        StringBuilder b2 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { b2.Append(p + (i * 0.5)); }
        Console.WriteLine(b2.Length);
        Console.WriteLine(b2.Length > 15000);
        // B3: char in concat -- p + 'x' -> Concat(object,object) boxed char
        Console.WriteLine("B3");
        StringBuilder b3 = new StringBuilder();
        char cx = 'x';
        for (int i = 0; i < 2000; i++) { b3.Append(p + cx + i); }
        Console.WriteLine(b3.Length);
        Console.WriteLine(b3.Length > 12000);
        // B4: AppendFormat with concat args
        Console.WriteLine("B4");
        StringBuilder b4 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { b4.AppendFormat("{0}-{1}", p + i, i * 2); }
        Console.WriteLine(b4.Length);
        Console.WriteLine(b4.Length > 20000);
        // B5: chained appends -- sb.Append("a").Append(p + i).Append("b")
        Console.WriteLine("B5");
        StringBuilder b5 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { b5.Append("a").Append(p + i).Append("b"); }
        Console.WriteLine(b5.Length);
        Console.WriteLine(b5.Length > 15000);
        // B6: append-chain result feeds ToString() feeds concat feeds append
        // (doubling growth -- bounded to 15 iterations, ~150k chars)
        Console.WriteLine("B6");
        StringBuilder b6 = new StringBuilder("seed");
        for (int i = 0; i < 15; i++) {
            b6.Append(b6.Append(p + i).ToString() + "!");
        }
        Console.WriteLine(b6.Length);
        Console.WriteLine(b6.Length > 100000);
        // B7: 3-level nested StringBuilder + GC every 50
        Console.WriteLine("B7");
        StringBuilder b7o = new StringBuilder();
        for (int i = 0; i < 5000; i++) {
            StringBuilder b7a = new StringBuilder("a" + i);
            StringBuilder b7b = new StringBuilder();
            b7b.Append(b7a.ToString() + "m");
            b7o.Append(b7b.ToString() + "z");
            if (i % 50 == 0) { GC.Collect(); }
        }
        Console.WriteLine(b7o.Length);
        Console.WriteLine(b7o.Length > 30000);
        // B8: AppendLine with concat + AppendFormat single arg + capacity read
        Console.WriteLine("B8");
        StringBuilder b8 = new StringBuilder(8);
        for (int i = 0; i < 500; i++) {
            b8.AppendLine(p + i);
            b8.AppendFormat("x={0}", i);
        }
        Console.WriteLine(b8.Length);
        Console.WriteLine(b8.Capacity);
        // B9: concat result reused across two builders (dual-sb churn, batch157 style)
        Console.WriteLine("B9");
        StringBuilder b9a = new StringBuilder(8);
        StringBuilder b9b = new StringBuilder(8);
        for (int i = 0; i < 20000; i++) {
            string s = p + i;
            b9a.Append(s);
            b9b.Append(s);
        }
        Console.WriteLine(b9a.Length);
        Console.WriteLine(b9b.Length);
        // B10: StringBuilder ctor(string) + Insert(0, concat) + ToString concat loop
        Console.WriteLine("B10");
        StringBuilder b10 = new StringBuilder("init");
        for (int i = 0; i < 2000; i++) {
            b10.Insert(0, p + i);
            b10.Append(b10.ToString().Length > 0 ? "T" : "F");
        }
        Console.WriteLine(b10.Length);
        Console.WriteLine(b10.Length > 12000);
    }
}}
