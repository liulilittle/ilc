using System;
using System.Threading.Tasks;
namespace Cases { public static class StructProbe2 {
    public static void Main() {
        Task<decimal> t = M();
        Console.WriteLine(t.IsCompleted);
        Console.WriteLine(t.Result == 3.5m);
    }
    static async Task<decimal> M() {
        decimal m = 2.5m;
        m = await Task.FromResult(m + 1.0m);
        return m;
    }
}}
