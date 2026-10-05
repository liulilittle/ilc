using System;
namespace Cases { public static class Env {
    public static void Main() {
        string nl = Environment.NewLine;
        Console.WriteLine(nl.Length);          // 2 (CRLF on net48 Windows)
        Console.WriteLine("done");
    }
}}
