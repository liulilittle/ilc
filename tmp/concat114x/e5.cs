using System;
using System.Text;
namespace Cases { public static class Concat114xE5Case {
    public static void Main() {
        string p = "n=";
        // E1: int boundary values through concat_int and append_i
        Console.WriteLine("E1");
        StringBuilder e1 = new StringBuilder();
        e1.Append(p + int.MinValue);
        e1.Append(p + int.MaxValue);
        e1.Append(p + 0);
        e1.Append(p + (-1));
        e1.Append(int.MinValue);
        e1.Append(int.MaxValue);
        e1.Append(0);
        e1.Append(-2147483647 - 1);
        for (int i = 0; i < 100; i++) {
            e1.Append(p + (int.MinValue + i));
            e1.Append(int.MaxValue - i);
        }
        Console.WriteLine(e1.Length);
        Console.WriteLine(e1.Length > 3000);
        // E2: 3-arg fixed concat (all strings) -> icall_string_concat_3
        Console.WriteLine("E2");
        string a2 = "a", b2 = "b", c2 = "c";
        StringBuilder e2 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { e2.Append(string.Concat(a2, b2, c2)); }
        Console.WriteLine(e2.Length);
        Console.WriteLine(e2.Length > 5000);
        // E3: 3-arg concat with one ToString inside -> concat_3 mixed
        Console.WriteLine("E3");
        StringBuilder e3 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { e3.Append(string.Concat(a2, i.ToString(), c2)); }
        Console.WriteLine(e3.Length);
        Console.WriteLine(e3.Length > 10000);
        // E4: big-segment concat -- 1M-char segment + int, then append
        Console.WriteLine("E4");
        StringBuilder big = new StringBuilder();
        for (int i = 0; i < 100000; i++) { big.Append("x"); }
        string bigStr = big.ToString();
        StringBuilder e4 = new StringBuilder();
        for (int i = 0; i < 10; i++) { e4.Append(bigStr + i); }
        Console.WriteLine(e4.Length);
        Console.WriteLine(e4.Length > 10000000);
        // E5: append_i / append_l boundary values
        Console.WriteLine("E5");
        StringBuilder e5 = new StringBuilder();
        for (int i = 0; i < 100; i++) {
            e5.Append(int.MinValue + i);
            e5.Append((long)int.MinValue + i);
            e5.Append((long)int.MaxValue + i);
            e5.Append(-0L);
        }
        Console.WriteLine(e5.Length);
        Console.WriteLine(e5.Length > 4000);
        // E6: concat of sb.ToString() with int (self-snapshot -- doubles per
        // iteration, bounded to 18 like V11/D3)
        Console.WriteLine("E6");
        StringBuilder e6 = new StringBuilder("acc");
        for (int i = 0; i < 18; i++) { e6.Append(e6.ToString() + i); }
        Console.WriteLine(e6.Length);
        Console.WriteLine(e6.Length > 1000000);
        // E7: string.Concat(string, string, string, string) with ToString temps
        Console.WriteLine("E7");
        StringBuilder e7 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { e7.Append(string.Concat(a2, i.ToString(), b2, c2)); }
        Console.WriteLine(e7.Length);
        Console.WriteLine(e7.Length > 10000);
// E8: REMOVED -- StringBuilder.Insert(int,int) numeric overload is a TRANSLATE FAIL (uncompilable C++), isolated in f6 probe
        // E9: nested concat with number-first INSIDE: sb.Append("x" + (i + p))
        Console.WriteLine("E9");
        StringBuilder e9 = new StringBuilder();
        for (int i = 0; i < 2000; i++) { e9.Append("x" + (i + p)); }
        Console.WriteLine(e9.Length);
        Console.WriteLine(e9.Length > 12000);
        // E10: repeated ToString() of growing builder, no append (memory churn)
        Console.WriteLine("E10");
        StringBuilder e10 = new StringBuilder("grow");
        long sum = 0;
        for (int i = 0; i < 2000; i++) {
            e10.Append("a" + i);
            string snap = e10.ToString();
            sum += snap.Length;
        }
        Console.WriteLine(sum);
        Console.WriteLine(sum > 4000000);
    }
}}

