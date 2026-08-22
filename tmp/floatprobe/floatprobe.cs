using System;
using System.Threading.Tasks;
namespace Cases { public static class FloatProbe {
    public static void Main() {
        Console.WriteLine(F().Result);
        Console.WriteLine(F().Result == 2.5f);
        Console.WriteLine(F().Result.GetType().Name);
    }
    static async Task<float> F() {
        float x = 1.5f;
        x = await Task.FromResult(x + 1.0f);
        return x;
    }
}}
