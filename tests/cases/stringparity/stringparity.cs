using System;
namespace Cases { public static class Stringparity {
    public static void Main() {
        string s = "  hello,world  ";
        Console.WriteLine(s.Trim()); // hello,world
        Console.WriteLine(s.Trim().ToUpper()); // HELLO,WORLD
        Console.WriteLine(s.Trim().ToLower()); // hello,world
        string[] parts = "a,b,c".Split(',');
        Console.WriteLine(parts.Length); // 3
        Console.WriteLine(string.Join("-", parts)); // a-b-c
        Console.WriteLine("abc".Insert(1, "X")); // aXbc
        Console.WriteLine("abc".Remove(1, 1)); // ac
        Console.WriteLine("a,b,a".Replace("a", "z")); // z,b,z
        Console.WriteLine("  ".Trim().Length); // 0
        Console.WriteLine(string.IsNullOrWhiteSpace("   ")); // True
    }
}}
