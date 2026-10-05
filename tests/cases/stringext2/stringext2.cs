using System;
namespace Cases { public static class StringExt2 {
    public static void Main() {
        // --- String.Compare / CompareOrdinal (ordinal, locale-independent) ---
        Console.WriteLine(String.Compare("abc", "abc", StringComparison.Ordinal)); // 0
        Console.WriteLine(String.Compare("abc", "abd", StringComparison.Ordinal)); // -1
        Console.WriteLine(String.Compare("abd", "abc", StringComparison.Ordinal)); // 1
        Console.WriteLine(String.Compare("abc", "ABC", StringComparison.Ordinal)); // 32 ('a'-'A')
        Console.WriteLine(String.Compare(null, null, StringComparison.Ordinal));   // 0
        Console.WriteLine(String.Compare("", "", StringComparison.Ordinal));       // 0
        Console.WriteLine(String.Compare("a", null, StringComparison.Ordinal));    // 1 (non-null > null)
        Console.WriteLine(String.Compare(null, "a", StringComparison.Ordinal));    // -1
        Console.WriteLine(String.CompareOrdinal("abc", "abc"));                    // 0
        Console.WriteLine(String.CompareOrdinal("abc", "abd"));                    // -1
        // --- String.Concat ---
        Console.WriteLine(String.Concat("ab", "cd"));                              // abcd
        Console.WriteLine(String.Concat("a", "b", "c"));                           // abc
        Console.WriteLine(String.Concat("a", "b", "c", "d"));                      // abcd
        Console.WriteLine(String.Concat(new string[] { "x", "y", "z" }));          // xyz
        Console.WriteLine(String.Concat(null, "a"));                               // a (null -> empty)
        Console.WriteLine(String.Concat("", "b"));                                 // b
        // --- String.Join ---
        Console.WriteLine(String.Join(",", new string[] { "a", "b", "c" }));       // a,b,c
        Console.WriteLine(String.Join("", new string[] { "a", "b" }));             // ab
        Console.WriteLine(String.Join(",", new object[] { 1, 2, 3 }));             // 1,2,3
        Console.WriteLine(String.Join(" - ", new string[] { "solo" }));            // solo
        Console.WriteLine(String.Join(",", new string[] { "a", null, "c" }));      // a,,c (null -> empty)
        Console.WriteLine(String.Join(",", new string[] { }).Length);              // 0
        try { String.Join(",", (string[])null); } catch (ArgumentNullException e) {
            Console.WriteLine(e.GetType().Name);                                   // ArgumentNullException
        }
        // --- String.IsNullOrEmpty / IsNullOrWhiteSpace ---
        Console.WriteLine(String.IsNullOrEmpty(null));                             // True
        Console.WriteLine(String.IsNullOrEmpty(""));                               // True
        Console.WriteLine(String.IsNullOrEmpty(" "));                              // False
        Console.WriteLine(String.IsNullOrEmpty("x"));                              // False
        Console.WriteLine(String.IsNullOrWhiteSpace(""));                          // True
        Console.WriteLine(String.IsNullOrWhiteSpace(" "));                         // True
        Console.WriteLine(String.IsNullOrWhiteSpace("\t"));                        // True
        Console.WriteLine(String.IsNullOrWhiteSpace("x"));                         // False
        // --- Chars indexer ---
        string s = "Hello";
        Console.WriteLine(s.Length);                                               // 5
        Console.WriteLine(s[0]);                                                   // H
        Console.WriteLine(s[4]);                                                   // o
        Console.WriteLine((int)s[0]);                                              // 72
        Console.WriteLine(s[s.Length - 1]);                                        // o
        // --- Copy / Equals / Empty ---
        string cp = String.Copy("abc");
        Console.WriteLine(cp == "abc");                                            // True
        Console.WriteLine(ReferenceEquals(cp, "abc"));                             // False (Copy allocates)
        Console.WriteLine(String.Equals("a", "a"));                                // True
        Console.WriteLine(String.Equals(null, null));                              // True
        Console.WriteLine(String.Equals("a", "b"));                                // False
        Console.WriteLine(string.Empty.Length);                                    // 0
    }
}}
