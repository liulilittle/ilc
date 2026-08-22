using System;
using System.Threading.Tasks;
namespace Cases { public static class ClassProbe2 {
    public static void Main() {
        string s = SF().Result;
        Console.WriteLine(s == "hi");
        Point p = PF().Result;
        Console.WriteLine(p == null ? "null" : p.GetType().Name);
    }
    static async Task<string> SF() { return await Task.FromResult("hi"); }
    static async Task<Point> PF() { return await Task.FromResult<Point>(null); }
    class Point { }
}}
