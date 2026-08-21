//------------------------------------------------------------------------------
// strcomp2.cs -- System.String comprehensive reference program (net48 oracle).
//
// Covers the string "composition + case + split" surface:
//
//   * Concat 2/3/4-arg fixed forms + the + operator -- operands are
//     runtime-known variables so the string pool cannot constant-fold the
//     call -- plus the null-argument policy (null counts as empty)
//   * Substring(startIndex) / Substring(startIndex, length) + chains +
//     ArgumentOutOfRangeException error paths
//   * the char indexer s[i] + IndexOutOfRangeException error paths
//   * ToUpper / ToLower (ASCII) + chains + empty string
//   * Trim / TrimStart / TrimEnd whitespace family + chains
//   * Split with multiple char delimiters, string[] separators and
//     StringSplitOptions
//   * Replace(char, char) AND Replace(string, string)
//   * Contains / IndexOf chains across the char/string overloads
//
// Every line prints a BOOL, so a diff failure shows exactly which
// assertion flipped.
//
// WIRING NOTES (M5 transpiler + runtime, verified 2026-08-06):
//   * string.Split is NOT mapped in MethodMap.String.cs -- the M5 resolver
//     throws before codegen. The Split section below is the future wiring
//     target; until then this case fails at TRANSPILE (recorded gap).
//   * Replace(string, string) hits the Replace case which hard-codes
//     char16_t casts -- the runtime sees a garbage oldChar and returns the
//     SAME instance (identity) -- recorded gap (no replace_s_s icall).
//   * Trim(char[]) / TrimStart(char[]) / TrimEnd(char[]) with a NON-empty
//     trim set are documented runtime deviations (whitespace-only); only
//     the no-arg whitespace family is asserted here.
//------------------------------------------------------------------------------

using System;

class Program
{
    static void Main()
    {
        // --- Concat: 2/3/4-arg fixed forms (runtime-known operands) ---
        string a = "abc";
        string b = "def";
        string c = "ghi";
        string d = "jkl";
        string n = null;
        Console.WriteLine(string.Concat(a, b) == "abcdef");
        Console.WriteLine(string.Concat(a, b, c) == "abcdefghi");
        Console.WriteLine(string.Concat(a, b, c, d) == "abcdefghijkl");
        Console.WriteLine(string.Concat(a, "") == "abc");
        Console.WriteLine(string.Concat("", a) == "abc");
        Console.WriteLine(string.Concat("", "") == "");
        Console.WriteLine(string.Concat(n, "x") == "x");        // null counts as empty
        Console.WriteLine(string.Concat("x", n) == "x");
        Console.WriteLine(string.Concat(n, n, "z") == "z");
        Console.WriteLine(string.Concat(a, b, c, d) == string.Concat(string.Concat(a, b), string.Concat(c, d)));
        Console.WriteLine(a + b == "abcdef");                   // + operator lowers to Concat
        Console.WriteLine(a + b + c + d == "abcdefghijkl");
        Console.WriteLine(a + "!" == "abc!");

        // --- Substring(startIndex) / Substring(startIndex, length) ---
        string s = "abcdefghij";
        Console.WriteLine(s.Substring(3) == "defghij");
        Console.WriteLine(s.Substring(0) == s);
        Console.WriteLine(s.Substring(10) == "");               // start == Length -> empty
        Console.WriteLine(s.Substring(3, 3) == "def");
        Console.WriteLine(s.Substring(0, 10) == s);
        Console.WriteLine(s.Substring(10, 0) == "");
        Console.WriteLine("abc".Substring(1, 2) == "bc");
        Console.WriteLine(s.Substring(2, 5).Substring(1) == "defg");   // chain
        Console.WriteLine(s.Substring(s.Length - 3) == "hij");          // computed start
        bool threw = false;
        try { s.Substring(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // negative start throws
        threw = false;
        try { s.Substring(11); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // start > Length throws
        threw = false;
        try { s.Substring(8, 3); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // start + length > Length throws
        threw = false;
        try { s.Substring(3, -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // negative length throws

        // --- char indexer s[i] ---
        Console.WriteLine(s[0] == 'a');
        Console.WriteLine(s[9] == 'j');
        Console.WriteLine(s[s.Length - 1] == 'j');
        Console.WriteLine("hello"[1] == 'e');
        Console.WriteLine("hello"[4] == 'o');
        threw = false;
        try { char x = s[10]; } catch (IndexOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // index == Length throws
        threw = false;
        try { char y = s[-1]; } catch (IndexOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // negative index throws

        // --- ToUpper / ToLower (ASCII) ---
        Console.WriteLine("Hello World".ToUpper() == "HELLO WORLD");
        Console.WriteLine("Hello World".ToLower() == "hello world");
        Console.WriteLine("MiXeD123".ToUpper() == "MIXED123");
        Console.WriteLine("MiXeD123".ToLower() == "mixed123");
        Console.WriteLine("ABC".ToUpper() == "ABC");
        Console.WriteLine("abc".ToLower() == "abc");
        Console.WriteLine("".ToUpper() == "");
        Console.WriteLine("".ToLower() == "");
        Console.WriteLine("AbC".ToUpper().ToLower() == "abc");  // chain
        Console.WriteLine(s.ToUpper() == "ABCDEFGHIJ");

        // --- Trim / TrimStart / TrimEnd (whitespace family) ---
        Console.WriteLine("  abc  ".Trim() == "abc");
        Console.WriteLine(" \t\n abc \r\n ".Trim() == "abc");
        Console.WriteLine("   ".Trim() == "");
        Console.WriteLine("".Trim() == "");
        Console.WriteLine("abc".Trim() == "abc");
        Console.WriteLine("  abc  ".TrimStart() == "abc  ");
        Console.WriteLine("  abc  ".TrimEnd() == "  abc");
        Console.WriteLine("\t abc".TrimStart() == "abc");
        Console.WriteLine("abc \t".TrimEnd() == "abc");
        Console.WriteLine("  a  ".Trim().ToUpper() == "A");     // chain
        Console.WriteLine(" \t  ".TrimStart() == "");

        // --- Replace ---
        Console.WriteLine("a-b-c".Replace('-', '_') == "a_b_c");
        Console.WriteLine("hello".Replace('l', 'L') == "heLLo");
        Console.WriteLine("abc".Replace('x', 'y') == "abc");    // no occurrence -> unchanged
        Console.WriteLine("".Replace('a', 'b') == "");          // empty string
        Console.WriteLine("abcabc".Replace("bc", "BC") == "aBCaBC");     // string form (GAP)
        Console.WriteLine("hello world".Replace("world", "ILC") == "hello ILC");  // string form (GAP)

        // --- Contains / IndexOf chains ---
        string text = "the quick brown fox";
        Console.WriteLine(text.Contains("quick"));
        Console.WriteLine(text.Contains("QUICK") == false);     // case-sensitive
        Console.WriteLine(text.Contains(""));
        Console.WriteLine(text.IndexOf("quick") == 4);
        Console.WriteLine(text.IndexOf('q') == 4);
        Console.WriteLine(text.IndexOf("fox") == text.Length - 3);
        Console.WriteLine(text.IndexOf("zzz") == -1);
        Console.WriteLine(text.IndexOf('z') == -1);
        Console.WriteLine(text.IndexOf("brown") >= 0);
        Console.WriteLine("abcabc".IndexOf('a', 1) == 3);       // (char, startIndex)
        Console.WriteLine("abcabc".IndexOf("bc", 2) == 4);      // (string, startIndex)
        Console.WriteLine(text.Substring(text.IndexOf("brown")) == "brown fox");   // chain
        Console.WriteLine(text.Contains(text.Substring(4, 5)));                 // chain
        Console.WriteLine(text.IndexOf(text.Substring(4, 5)) == 4);             // chain

        // --- Split: multi-delimiter (params char[]) ---
        string csv = "a,b;c";
        string[] p1 = csv.Split(',', ';');
        Console.WriteLine(p1.Length == 3);
        Console.WriteLine(p1[0] == "a" && p1[1] == "b" && p1[2] == "c");
        string[] p2 = "abc".Split(',');
        Console.WriteLine(p2.Length == 1 && p2[0] == "abc");    // no match -> single element
        string[] p3 = "a,,b".Split(',');
        Console.WriteLine(p3.Length == 3 && p3[0] == "a" && p3[1] == "" && p3[2] == "b"); // empty segments kept
        string[] p4 = "a--b--c".Split(new string[] { "--" }, StringSplitOptions.None);
        Console.WriteLine(p4.Length == 3 && p4[0] == "a" && p4[1] == "b" && p4[2] == "c"); // string[] separators
        string[] p5 = "a,,b".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(p5.Length == 2 && p5[0] == "a" && p5[1] == "b");                 // options
        string[] p6 = "x1,y 2;z".Split(',', ' ', ';');
        Console.WriteLine(p6.Length == 4);
        Console.WriteLine(p6[0] == "x1" && p6[1] == "y" && p6[2] == "2" && p6[3] == "z");
    }
}
