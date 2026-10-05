//------------------------------------------------------------------------------
// rlaudit3.cs -- LINQ Where two-pass defect probe (linq_map.cpp: the
// predicate runs once to count matches, then again to fill the result; the
// closure state carries over between passes).
// net48 (single pass): {1,3} and {1,3,5}.
// runtime: pass 2 starts with the advanced counter; the odd-length case
// exposes the mis-sized result (fewer stores -> trailing default element).
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases { public static class Rlaudit3 {
    public static void Main()
    {
        int n = 0;
        int[] a = new[] { 1, 2, 3, 4 }.Where(x => (n++ % 2) == 0).ToArray();
        Console.WriteLine("a_len=" + a.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Console.WriteLine("a" + i + "=" + a[i]);
        }

        int m = 0;
        int[] b = new[] { 1, 2, 3, 4, 5 }.Where(x => (m++ % 2) == 0).ToArray();
        Console.WriteLine("b_len=" + b.Length);
        for (int i = 0; i < b.Length; i++)
        {
            Console.WriteLine("b" + i + "=" + b[i]);
        }
    }
} }
