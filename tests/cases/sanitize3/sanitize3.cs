using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cases { public static class Sanitize3 {
    // iterator -> <Range>d__0
    static IEnumerable<int> Range(int n) {
        for (int i = 0; i < n; ++i) yield return i * 2;
    }

    // iterator returning a reference type (string)
    static IEnumerable<string> Words(int n) {
        for (int i = 0; i < n; ++i) yield return "w" + i;
    }

    // async -> <AsyncAdd>d__N
    static async Task<int> AsyncAdd(int a, int b) {
        int seed = await Task.FromResult(a);
        return seed + b;
    }

    // async with capture + exception
    static async Task<string> AsyncThrow(int x) {
        int v = await Task.FromResult(x);
        if (v < 0) throw new InvalidOperationException("neg");
        return "ok" + v;
    }

    public static void Main() {
        int sum = 0;
        foreach (int v in Range(4)) sum += v;
        Console.WriteLine("sum=" + sum);
        int cnt = 0;
        foreach (string s in Words(3)) { cnt++; Console.WriteLine(s); }
        Console.WriteLine("cnt=" + cnt);
        Console.WriteLine("async=" + AsyncAdd(2, 3).Result);
        Console.WriteLine("aok=" + AsyncThrow(5).Result);
        try {
            string r = AsyncThrow(-1).Result;
            Console.WriteLine("no-throw:" + r);
        } catch (AggregateException) {
            Console.WriteLine("agg");
        }
    }
}}
