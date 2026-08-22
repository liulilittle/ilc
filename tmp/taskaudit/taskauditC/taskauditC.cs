using System;
using System.Threading.Tasks;
namespace Cases { public static class TaskAuditC {
    public static void Main() {
        string[] a = S().Result;
        Console.WriteLine(a.Length);
        Console.WriteLine(a[0]);
        Console.WriteLine(a.GetType().Name);
    }
    static async Task<string[]> S() {
        string[] v = new string[] { "x", "y" };
        v = await Task.FromResult(v);
        return v;
    }
}}
