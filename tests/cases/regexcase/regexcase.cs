using System;
using System.Text.RegularExpressions;
namespace Cases { public static class RegexCase {
    public static void Main() {
        // Regex domain: STATIC IsMatch / Replace / Split chain (the runtime
        // surface mirrors the .NET static signatures -- input first, pattern
        // second -- so every call below is order-sensitive: a wrong-order
        // wiring makes the runtime parse the INPUT as the pattern and throw).
        Console.WriteLine(Regex.IsMatch("abbbc", "ab+c") == true);       // True
        Console.WriteLine(Regex.IsMatch("axb", "ab+c") == false);        // True
        Console.WriteLine(Regex.IsMatch("abc", "^abc$") == true);        // True
        Console.WriteLine(Regex.IsMatch("xabc", "^abc$") == false);      // True
        Console.WriteLine(Regex.IsMatch("ab\nc", "^abc$") == false);     // True
        Console.WriteLine(Regex.Replace("a1b2", "\\d", "X") == "aXbX");  // True
        Console.WriteLine(Regex.Replace("a1b2", "(\\d)", "[$1]") == "a[1]b[2]"); // True
        Console.WriteLine(Regex.Replace("abc", "a", "$$") == "$bc");     // True
        Console.WriteLine(Regex.Split("a,b,c", ",").Length == 3);        // True
        Console.WriteLine(Regex.Split("a,,b", ",").Length == 3);         // True
        Console.WriteLine(Regex.Split("a,b,c", ",")[1] == "b");          // True
        Console.WriteLine(Regex.Split("abc", "x")[0] == "abc");          // True
        Console.WriteLine(Regex.IsMatch("你好世界", "你好") == true);      // True
        Console.WriteLine(Regex.IsMatch("你好世界", "世界") == true);      // True
        bool threw = false;
        try { Regex.IsMatch("abc", "["); }
        catch (ArgumentException) { threw = true; }
        Console.WriteLine(threw == true);                                // True
        Console.WriteLine(Regex.IsMatch("abbbc", "ab+c") == false);      // False
        Console.WriteLine(Regex.IsMatch("axb", "ab+c") == true);         // False
    }
}}
