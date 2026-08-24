using System;
using System.Collections.Generic;

namespace Cases { public static class YieldProbe {
    static IEnumerable<int> Range(int n) {
        for (int i = 0; i < n; ++i) yield return i;
    }
    public static void Main() {
        int sum = 0;
        foreach (int x in Range(4)) sum += x;
        Console.WriteLine("sum=" + sum);
    }
}}
