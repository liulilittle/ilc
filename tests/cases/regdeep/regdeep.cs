//------------------------------------------------------------------------------
// regdeep.cs -- Regex static-domain DEEP chain reference program (net48).
//
// Every line is a BOOL (== known string). Exercises the wired static surface
// (MethodMap.Regex.cs -> il2cpp_regex_is_match/replace/split):
//
//   * Replace multi-pattern CHAIN (date / mobile-phone / HTML-tag stripping)
//     -- the output of one Replace feeds the pattern of the next
//   * $1-$9 group references (9-group reversal, $0 whole-match, $n adjacent
//     to non-token text)
//   * Replace with EMPTY matches ("x*", "" -- net48 replaces at every
//     position: "abc" -> "-a-b-c-")
//   * Split with multi-separator patterns (alternation ",|;|\|", \d+ runs,
//     \s+ runs, character class [,;:], trailing empty element)
//   * IsMatch anchor combinations (^ $, "^$" on empty/non-empty input,
//     bare ^ and $, ^(a|b|c)$ alternation, ^1[3-9]\d{9}$ phone full-match)
//
// GAP (recorded, NOT exercised): top-level \b word boundary is a parse
// ERROR in the runtime engine (regex_parse.cpp whitelist §3.4 -- "\b" at
// top level is rejected; only in-class [\b] backspace is supported), so no
// \b line appears here. \A \z \Z \G escapes are likewise unsupported
// (only ^ $ with the Multiline option variant are parsed as anchors).
// RegexOptions overloads stay excluded (regexcase2 note: the enum slot is
// encoded VALUETYPE by Roslyn vs i4 by the transpiler dispatch -- crash).
//
// net48 BASELINE caveats (regex.h §1.4): Split IGNORES capture groups
// (net48 inserts them -- divergence recorded, not exercised here); \d \w \s
// are ASCII sets; $nn two-digit groups resolve to nn when it exists.
//------------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        // ================= Replace multi-pattern CHAIN =================
        Console.WriteLine(Regex.Replace(Regex.Replace("2024-06-15", "-", "/"),
                          "(\\d{4})/(\\d{2})/(\\d{2})", "$3-$2-$1") == "15-06-2024"); // True
        Console.WriteLine(Regex.Replace(Regex.Replace(Regex.Replace(
                          "<p>张三 13812345678 2024-06-15</p>", "<[^>]+>", ""),
                          "(1[3-9]\\d)(\\d{4})(\\d{4})", "$1****$3"),
                          "(\\d{4})-(\\d{2})-(\\d{2})", "$2/$3/$1") == "张三 138****5678 06/15/2024"); // True

        // ================= Replace: phone mask / HTML strip =================
        Console.WriteLine(Regex.Replace("手机 13911112222", "(1[3-9]\\d)(\\d{4})(\\d{4})",
                          "$1****$2") == "手机 139****1111");                     // True
        Console.WriteLine(Regex.Replace("<div><b>Hi</b></div>", "<[^>]+>", "") == "Hi"); // True

        // ================= Replace: $1-$9 references =================
        Console.WriteLine(Regex.Replace("abcdefghi",
                          "^(a)(b)(c)(d)(e)(f)(g)(h)(i)$",
                          "$9$8$7$6$5$4$3$2$1") == "ihgfedcba");                  // True
        Console.WriteLine(Regex.Replace("2024-06-15", "(\\d{4})-(\\d{2})-(\\d{2})",
                          "$3年$2月$1日") == "15年06月2024日");                     // True
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "[$0]") == "[ab]");       // True

        // ================= Replace: empty matches =================
        Console.WriteLine(Regex.Replace("abc", "x*", "-") == "-a-b-c-");          // True
        Console.WriteLine(Regex.Replace("", "x*", "-") == "-");                   // True
        Console.WriteLine(Regex.Replace("abc", "", "-") == "-a-b-c-");            // True

        // ================= Split: multi-separator =================
        Console.WriteLine(Regex.Split("a,b;c|d", ",|;|\\|").Length == 4);         // True
        Console.WriteLine(Regex.Split("a,b;c|d", ",|;|\\|")[2] == "c");           // True
        Console.WriteLine(Regex.Split("a1b22c333", "\\d+").Length == 4);          // True
        Console.WriteLine(Regex.Split("a1b22c333", "\\d+")[3] == "");             // True
        Console.WriteLine(Regex.Split("a  b\tc", "\\s+")[1] == "b");              // True
        Console.WriteLine(Regex.Split("a,b;c:d", "[,;:]").Length == 4);           // True

        // ================= IsMatch: anchor combinations =================
        Console.WriteLine(Regex.IsMatch("123-4567", "^\\d{3}-\\d{4}$") == true);  // True
        Console.WriteLine(Regex.IsMatch("x123-4567", "^\\d{3}-\\d{4}$") == false);// True
        Console.WriteLine(Regex.IsMatch("", "^$") == true);                       // True
        Console.WriteLine(Regex.IsMatch("a", "^$") == false);                     // True
        Console.WriteLine(Regex.IsMatch("abc", "^") == true);                     // True
        Console.WriteLine(Regex.IsMatch("abc", "$") == true);                     // True
        Console.WriteLine(Regex.IsMatch("b", "^(a|b|c)$") == true);               // True
        Console.WriteLine(Regex.IsMatch("ab", "^(a|b|c)$") == false);             // True
        Console.WriteLine(Regex.IsMatch("13812345678", "^1[3-9]\\d{9}$") == true);// True
        Console.WriteLine(Regex.IsMatch("12812345678", "^1[3-9]\\d{9}$") == false); // True

        // ================= deliberate-false pins =================
        Console.WriteLine(Regex.Replace("abc", "", "-") == "abc");                // False
        Console.WriteLine(Regex.Replace("abc", "x*", "-") == "-abc-");            // False
    }
}
