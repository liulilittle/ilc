using System;
using System.Threading.Tasks;
namespace Cases { public static class Prog {
    static async Task<int> M() {
        int r = await Task.Run(() => 41);
        return r + 1;
    }
    static async Task<string> S() {
        string s = await Task.Run(() => "hi");
        return s + "!";
    }
    public static void Main() {
        Console.WriteLine("-- shard exc001 start --");
        Console.WriteLine("[exc001.run.int] " + M().Result);
        Console.WriteLine("[exc001.run.string] " + S().Result);
        Console.WriteLine("-- shard exc001 end --");
    }
}}
