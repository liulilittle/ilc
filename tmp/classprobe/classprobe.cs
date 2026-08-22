using System;
using System.Threading.Tasks;
namespace Cases { public static class ClassProbe {
    public static void Main() {
        int[] a = AF().Result;
        Console.WriteLine(a != null && a.Length == 3 && a[2] == 9);
        string s = SF().Result;
        Console.WriteLine(s == "hi");
        Point p = PF().Result;
        Console.WriteLine(p == null ? "null" : p.GetType().Name);
    }
    static async Task<int[]> AF() { int[] x = { 1, 2 }; x = await Task.FromResult(new int[] { 1, 2, 9 }); return x; }
    static async Task<string> SF() { return await Task.FromResult("hi"); }
    static async Task<Point> PF() { return await Task.FromResult<Point>(null); }
    class Point { }
}}
