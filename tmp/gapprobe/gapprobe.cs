using System;
using System.Threading.Tasks;
namespace Cases { public static class GapProbe {
    public static void Main() {
        Console.WriteLine(UF().Result);
        Console.WriteLine(UF().Result == 3000000001UL);
        Console.WriteLine(OF().Result);
        Console.WriteLine(OF().Result == null);
        object o = OF().Result;
        Console.WriteLine(o == null ? "null" : o.GetType().Name);
        Console.WriteLine(FF().Result);
        Console.WriteLine(FF().Result == 2.5f);
    }
    static async Task<ulong> UF() {
        ulong b = 3000000000UL;
        b = await Task.FromResult(b + 1);
        return b;
    }
    static async Task<object> OF() {
        return await Task.FromResult<object>(null);
    }
    static async Task<float> FF() {
        float x = 1.5f;
        x = await Task.FromResult(x + 1.0f);
        return x;
    }
}}
