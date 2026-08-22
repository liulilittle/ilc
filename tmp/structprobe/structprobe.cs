using System;
using System.Threading.Tasks;
namespace Cases { public static class StructProbe {
    public static void Main() {
        Console.WriteLine(M().Result);
        Console.WriteLine(G().Result == Guid.Empty);
        Console.WriteLine(G().Result.ToString());
        Console.WriteLine(DT().Result == DateTime.MaxValue);
    }
    static async Task<decimal> M() {
        decimal m = 2.5m;
        m = await Task.FromResult(m + 1.0m);
        return m;
    }
    static async Task<Guid> G() {
        return await Task.FromResult(Guid.Empty);
    }
    static async Task<DateTime> DT() {
        return await Task.FromResult(DateTime.MaxValue);
    }
}}
