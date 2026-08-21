using System;
namespace Cases { public static class StringExt {
    public static void Main() {
        string s = "Hello, World!";
        // Substring(int)
        Console.WriteLine(s.Substring(7));           // World!
        Console.WriteLine(s.Substring(0));           // Hello, World!
        Console.WriteLine("[" + s.Substring(13) + "]"); // []
        // Substring(int, int)
        Console.WriteLine(s.Substring(0, 5));        // Hello
        Console.WriteLine(s.Substring(7, 6));        // World!
        Console.WriteLine(s.Substring(6, 1));        // ,
        Console.WriteLine("[" + s.Substring(7, 0) + "]"); // []
        // StartsWith / EndsWith
        Console.WriteLine(s.StartsWith("Hello"));    // True
        Console.WriteLine(s.StartsWith("World"));    // False
        Console.WriteLine(s.StartsWith(""));         // True
        Console.WriteLine(s.EndsWith("World!"));     // True
        Console.WriteLine(s.EndsWith("world!"));     // False
        Console.WriteLine(s.EndsWith(""));           // True
        // IndexOf / LastIndexOf
        Console.WriteLine(s.IndexOf('o'));           // 4
        Console.WriteLine(s.LastIndexOf('o'));       // 8
        Console.WriteLine(s.IndexOf('z'));           // -1
        Console.WriteLine(s.IndexOf("World"));       // 7
        Console.WriteLine(s.IndexOf(""));            // 0
        Console.WriteLine(s.IndexOf("xyz"));         // -1
        // Replace
        Console.WriteLine(s.Replace('o', '0'));      // Hell0, W0rld!
        Console.WriteLine(ReferenceEquals(s, s.Replace('z', 'x'))); // True (no occurrence -> same instance)
        Console.WriteLine(ReferenceEquals(s, s.Replace('o', 'o'))); // False (net48: old==new allocates)
        // ToUpper / ToLower
        Console.WriteLine(s.ToUpper());              // HELLO, WORLD!
        Console.WriteLine(s.ToLower());              // hello, world!
        Console.WriteLine("abc123!?".ToUpper());     // ABC123!?
        // Trim / TrimStart / TrimEnd
        string t = "  trim me  ";
        Console.WriteLine("[" + t.Trim() + "]");     // [trim me]
        Console.WriteLine("[" + t.TrimStart() + "]");// [trim me  ]
        Console.WriteLine("[" + t.TrimEnd() + "]");  // [  trim me]
        Console.WriteLine("[" + "   ".Trim() + "]"); // []
        Console.WriteLine("[" + "\r\n abc \t".Trim() + "]"); // [abc]
        // CompareTo (ordinal, ASCII inputs coincide with culture compare)
        Console.WriteLine(s.CompareTo("Hello, World!")); // 0
        Console.WriteLine("a".CompareTo("b"));       // -1
        Console.WriteLine("b".CompareTo("a"));       // 1
        Console.WriteLine("abc".CompareTo("abcd"));  // -1
        // Contains
        Console.WriteLine(s.Contains("World"));      // True
        Console.WriteLine(s.Contains("world"));      // False
        Console.WriteLine(s.Contains(""));           // True
        Console.WriteLine(s.Contains("xyz"));        // False
        // Exception paths
        try { Console.WriteLine(s.StartsWith(null)); } catch (ArgumentNullException) { Console.WriteLine("ANE"); }
        try { Console.WriteLine(s.Substring(-1)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE"); }
        try { Console.WriteLine(s.Substring(3, 100)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("AOORE2"); }
        try { string n = null; Console.WriteLine(n.Trim()); } catch (NullReferenceException) { Console.WriteLine("NRE"); }
    }
}}
