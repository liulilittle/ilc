using System;
using System.Text.RegularExpressions;
namespace Cases { public static class RegexCase2 {
    public static void Main() {
        // Regex domain DEEP: capture-group reordering via $1-style replacement
        // refs, multi-segment Split, Chinese patterns. The runtime Match
        // surface is a string[] (group0=whole, groups1..n), not a Match
        // object, so group semantics are pinned through Replace $n and Split
        // instead of Match.Groups[1].Value.
        // NOTE: RegexOptions overloads (IsMatch/Replace/Split + IgnoreCase)
        // are NOT covered here -- the transpiler dispatches the enum slot as
        // i4 (0x08) while Roslyn encodes it as VALUETYPE (0x11), so those
        // overloads fall through to the generated-name path and crash the
        // transpile (recorded gap; regex-domain dispatch fix pending).
        Console.WriteLine(Regex.Replace("2024-06-15", "(\\d{4})-(\\d{2})-(\\d{2})", "$3/$2/$1") == "15/06/2024"); // True
        Console.WriteLine(Regex.Replace("a1 b2", "(\\w)(\\d)", "$2$1") == "1a 2b");     // True
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "[$0]") == "[ab]");              // True
        Console.WriteLine(Regex.Replace("abc", "x", "y") == "abc");                      // True
        Console.WriteLine(Regex.Replace("a$b", "\\$", "$$") == "a$b");                   // True
        Console.WriteLine(Regex.IsMatch("abc123", "(\\d+)$") == true);                   // True
        Console.WriteLine(Regex.Split("a1b2c3", "\\d").Length == 4);                     // True
        Console.WriteLine(Regex.Split("a1b2c3", "\\d")[1] == "b");                       // True
        Console.WriteLine(Regex.Split("a,b,", ",").Length == 3);                         // True
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "$2$1") == "ba");                // True
        Console.WriteLine(Regex.Split("a\nb\nc", "\n").Length == 3);                     // True
        Console.WriteLine(Regex.Replace("你好世界", "世界", "大家") == "你好大家");        // True
        Console.WriteLine(Regex.Replace("你好，世界", "(你好)", "[$1]") == "[你好]，世界"); // True
        Console.WriteLine(Regex.Split("你好世界", "世").Length == 2);                     // True
        Console.WriteLine(Regex.IsMatch("ABC", "abc") == false);                         // True
        Console.WriteLine(Regex.IsMatch("abc", "abc") == false);                         // False
        Console.WriteLine(Regex.Replace("a1 b2", "(\\w)(\\d)", "$2$1") == "1a2b");       // False
        Console.WriteLine(Regex.Split("a1b2c3", "\\d").Length == 3);                     // False
    }
}}
