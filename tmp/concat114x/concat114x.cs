using System;
using System.Text;
namespace Cases { public static class Concat114xCase {
    public static void Main() {
        // V1: huge loop -- sb.Append(p + i) x100000 (memory pressure + realloc path)
        Console.WriteLine("V1");
        StringBuilder v1 = new StringBuilder();
        string p1 = "n=";
        for (int i = 0; i < 100000; i++) { v1.Append(p1 + i); }
        Console.WriteLine(v1.Length);
        Console.WriteLine(v1.Length > 600000);
        // V2: nested StringBuilder -- per-iteration fresh sb, sb2.Append(sb1.ToString())
        Console.WriteLine("V2");
        StringBuilder v2o = new StringBuilder();
        for (int i = 0; i < 20000; i++) {
            StringBuilder v2i = new StringBuilder("seed");
            v2i.Append("x" + i);
            v2o.Append(v2i.ToString());
        }
        Console.WriteLine(v2o.Length);
        Console.WriteLine(v2o.Length > 100000);
        // V3: Insert(0,...) / Replace / Remove mixed with concat
        Console.WriteLine("V3");
        StringBuilder v3 = new StringBuilder("abcdef");
        string p3 = "k=";
        for (int i = 0; i < 2000; i++) {
            v3.Insert(0, p3 + i);
            v3.Replace("k=", "K-");
            v3.Remove(0, 3);
            v3.Append("t" + i);
        }
        Console.WriteLine(v3.Length);
        Console.WriteLine(v3.ToString().Length == v3.Length);
        // V4: preallocated capacity 16, then over-append (capacity growth path)
        Console.WriteLine("V4");
        StringBuilder v4 = new StringBuilder(16);
        for (int i = 0; i < 10000; i++) { v4.Append(p1 + i); }
        Console.WriteLine(v4.Length);
        Console.WriteLine(v4.Length > 50000);
        // V5: multi-arg concat -- 4-arg fixed and 5-arg params-array forms
        Console.WriteLine("V5");
        string a5 = "A", b5 = "B", c5 = "C", d5 = "D", e5 = "E";
        for (int i = 0; i < 8; i++) {
            Console.WriteLine(string.Concat(a5, b5, c5, d5, i));
            Console.WriteLine(string.Concat(a5, b5, c5, d5, e5, i));
            Console.WriteLine(string.Concat(a5, b5, c5, d5));
        }
        // V6: mixed chain -- sb.Append("a" + i + sb2.ToString() + (i * 2))
        Console.WriteLine("V6");
        StringBuilder v6 = new StringBuilder();
        StringBuilder v6s = new StringBuilder("mid");
        for (int i = 0; i < 2000; i++) { v6.Append("a" + i + v6s.ToString() + (i * 2)); }
        Console.WriteLine(v6.Length);
        Console.WriteLine(v6.Length > 20000);
        // V7: empty-string / single-char boundaries (bounded doubling: the
        // append of the full ToString() doubles the length each iteration,
        // so 20 iterations stay within ~4M chars -- net48 x86 and the gen
        // runtime both survive; unbounded would OOM the oracle itself)
        Console.WriteLine("V7");
        string e7 = "";
        StringBuilder v7 = new StringBuilder("core");
        for (int i = 0; i < 20; i++) {
            v7.Append(e7 + v7.ToString());
            v7.Append('x');
        }
        Console.WriteLine(v7.Length);
        Console.WriteLine(v7.Length > 1000000);
        // V8: GC pressure -- GC.Collect() every 100 iterations
        Console.WriteLine("V8");
        StringBuilder v8 = new StringBuilder(4);
        for (int i = 0; i < 10000; i++) {
            v8.Append(p1 + i);
            if (i % 100 == 0) { GC.Collect(); }
        }
        Console.WriteLine(v8.Length);
        Console.WriteLine(v8.Length > 50000);
        // V9: fold-defeat -- number FIRST (concat_2(t0, p), temp is arg0), and
        //     multi-use ToString result (single-read guard fails)
        Console.WriteLine("V9");
        StringBuilder v9 = new StringBuilder();
        for (int i = 0; i < 2000; i++) {
            v9.Append(i + p1);            // number-first: no fold (temp is ARG0)
            string t = i.ToString();      // multi-use: no fold (temp read twice)
            v9.Append(p1 + t);
            v9.Append(t);
        }
        Console.WriteLine(v9.Length);
        Console.WriteLine(v9.Length > 30000);
        // V10: int64 / negative / large values
        Console.WriteLine("V10");
        StringBuilder v10 = new StringBuilder();
        for (int i = -3; i < 8; i++) {
            v10.Append(p1 + 2147483647L);
            v10.Append(p1 + (-i));
            v10.Append(p1 + 2147483647);
            v10.Append(p1 + (i * 100000000));
        }
        Console.WriteLine(v10.Length);
        Console.WriteLine(v10.Length > 1000);
        // V11: self-referential -- Append(sb.ToString() + i) on the SAME
        // builder (bounded: doubling growth, 18 iterations ~ 1M chars)
        Console.WriteLine("V11");
        StringBuilder v11 = new StringBuilder("self");
        for (int i = 0; i < 18; i++) { v11.Append(v11.ToString() + i); }
        Console.WriteLine(v11.Length);
        Console.WriteLine(v11.Length > 1000000);
        // V12: Append(object) path -- concat result as boxed object arg
        Console.WriteLine("V12");
        StringBuilder v12 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { v12.Append((object)(p1 + i)); }
        Console.WriteLine(v12.Length);
        Console.WriteLine(v12.Length > 12000);
    }
}}
