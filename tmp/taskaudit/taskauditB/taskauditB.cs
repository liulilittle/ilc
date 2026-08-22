using System;
using System.Threading.Tasks;
namespace Cases { public static class TaskAuditB {
    public static void Main() {
        Console.WriteLine(D().Result);
        Console.WriteLine(T().Result);
    }
    static async Task<decimal> D() {
        decimal v = 1.5m;
        v = await Task.FromResult(v + 1m);
        return v;
    }
    static async Task<DateTime> T() {
        DateTime v = new DateTime(2020, 1, 2);
        v = await Task.FromResult(v.AddDays(1));
        return v;
    }
}}
