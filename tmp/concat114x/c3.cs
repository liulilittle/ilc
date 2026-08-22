using System;
using System.Text;
namespace Cases { public static class Concat114xC3Case {
    public static void Main() {
        string p = "n=";
        // C1: 5-operand mixed concat -> concat_many path (prependCount)
        Console.WriteLine("C1");
        StringBuilder c1 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { c1.Append("a" + i + "b" + (i * 0.5) + "c"); }
        Console.WriteLine(c1.Length);
        Console.WriteLine(c1.Length > 30000);
        // C2: Concat(object[]) with boxed values -> concat_array
        Console.WriteLine("C2");
        StringBuilder c2 = new StringBuilder();
        for (int i = 0; i < 1000; i++) {
            c2.Append(string.Concat(new object[] { "x", i, "y", i * 0.25, "z" }));
        }
        Console.WriteLine(c2.Length);
        Console.WriteLine(c2.Length > 15000);
        // C3: Concat(string[]) explicit -> concat_array
        Console.WriteLine("C3");
        StringBuilder c3 = new StringBuilder();
        string[] segs = { "p1", "p2", "p3", "p4" };
        for (int i = 0; i < 2000; i++) { c3.Append(string.Concat(segs)); }
        Console.WriteLine(c3.Length);
        Console.WriteLine(c3.Length > 23000);
        // C4: capacity-1 builder -- aggressive growth from minimum
        Console.WriteLine("C4");
        StringBuilder c4 = new StringBuilder(1);
        for (int i = 0; i < 10000; i++) { c4.Append(p + i); }
        Console.WriteLine(c4.Length);
        Console.WriteLine(c4.Length > 50000);
        // C5: Append(long) interleaved with Append(concat) -- append_l + concat mix
        Console.WriteLine("C5");
        StringBuilder c5 = new StringBuilder(8);
        for (int i = 0; i < 10000; i++) {
            c5.Append((long)i * 1000000000L);
            c5.Append(p + i);
        }
        Console.WriteLine(c5.Length);
        Console.WriteLine(c5.Length > 120000);
        // C6: huge loop + GC every 1000 (collection DURING concat+append churn)
        Console.WriteLine("C6");
        StringBuilder c6 = new StringBuilder(4);
        for (int i = 0; i < 100000; i++) {
            c6.Append(p + i);
            if (i % 1000 == 0) { GC.Collect(); }
        }
        Console.WriteLine(c6.Length);
        Console.WriteLine(c6.Length > 600000);
        // C7: Append(concat) where concat embeds ANOTHER sb's ToString of a
        // builder at its own reallocation moment (cross-builder aliasing;
        // mutual full-append doubles each iteration -- bounded to 16)
        Console.WriteLine("C7");
        StringBuilder c7a = new StringBuilder(2);
        StringBuilder c7b = new StringBuilder(2);
        for (int i = 0; i < 16; i++) {
            c7a.Append(c7b.ToString() + i);
            c7b.Append(c7a.ToString() + i);
        }
        Console.WriteLine(c7a.Length);
        Console.WriteLine(c7b.Length);
        // C8: concat chain result stored in static-like local reused 1000x
        Console.WriteLine("C8");
        StringBuilder c8 = new StringBuilder();
        string fixedStr = p + "static";
        for (int i = 0; i < 10000; i++) { c8.Append(fixedStr); }
        Console.WriteLine(c8.Length);
        Console.WriteLine(c8.Length > 80000);
        // C9: Append(string.Empty) + Append("") + Append(concat-to-empty)
        Console.WriteLine("C9");
        StringBuilder c9 = new StringBuilder();
        string empty = "";
        for (int i = 0; i < 5000; i++) {
            c9.Append(empty);
            c9.Append("" + i);
            c9.Append(p + empty);
        }
        Console.WriteLine(c9.Length);
        Console.WriteLine(c9.Length > 40000);
        // C10: deep concat nesting -- (((p+i)+"a")+i)+... 8 levels
        Console.WriteLine("C10");
        StringBuilder c10 = new StringBuilder();
        for (int i = 0; i < 2000; i++) {
            c10.Append(p + i + "a" + i + "b" + i + "c" + i + "d" + i);
        }
        Console.WriteLine(c10.Length);
        Console.WriteLine(c10.Length > 40000);
    }
}}
