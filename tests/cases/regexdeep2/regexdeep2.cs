using System;
using System.Text.RegularExpressions;
namespace Cases { public static class RegexDeep2 {
    // Regex domain DEEP layer 2 (batch C41). net48 oracle baseline. Extends
    // regex2/regex3/regdeep over the WIRED static surface (MethodMap.Regex.cs
    // -> regex_api.cpp: IsMatch/Match/Split/Replace) with four deeper axes:
    //
    //   1. CHAR CLASSES (character classes): union/negation combos, meta-characters as
    //      class literals ([+*], [.]), builtin \d\w as class members, class
    //      sequences, '^' as a literal inside a class.
    //   2. GROUPS (groups): nested capturing groups and net48 group NUMBERING
    //      (outermost-left-to-right: ((a)(b))c -> $1 outer, $2/$3 inner),
    //      groups under quantifiers, alternation inside a group, empty
    //      groups.
    //   3. BACKTRACKING (backtracking): alternation first-branch-fail, greedy
    //      quantifier give-back under a following atom, nested-quantifier
    //      iteration, anchored .* give-back.
    //   4. UNICODE (Unicode): literal CJK in input/pattern/replacement, CJK
    //      char-class RANGE ([U+4E00-U+9FA5] code-unit range), negation probes, CJK
    //      split separator. Output stays ASCII bool (the e2e wide-char diff
    //      is UTF-8 pinned on both sides, but ASCII output is the safest).
    //
    // RECORDED GAPS -- NOT exercised (net48 succeeds, runtime throws
    // ArgumentException -> byte-diff split): \uXXXX / \xNN / \p{L} escapes,
    // \b \B word boundaries, (?:...) (?=...) (?!...) (?<=...) (?<!...)
    // (?>...) (?<name>...) (?i) (regex_parse.cpp whitelist), and the
    // RegexOptions overloads (enum-slot dispatch mismatch, build breaker).
    // NOTE: \d \w \s are ASCII-only in the runtime (docs 1.4) while net48
    // \w/\s are Unicode -- so no \w/\s probe may run on non-ASCII input.
    public static void Main() {
        // ============ 1. char classes ============
        Console.WriteLine(Regex.IsMatch("abc123", "^[a-z]+[0-9]+$") == true);
        Console.WriteLine(Regex.IsMatch("abc", "^[a-z0-9]+$") == true);
        Console.WriteLine(Regex.IsMatch("ABC", "^[a-z0-9]+$") == false);
        Console.WriteLine(Regex.IsMatch("ab", "^[^0-9]+$") == true);
        Console.WriteLine(Regex.IsMatch("123", "^[^0-9]+$") == false);
        Console.WriteLine(Regex.IsMatch("a1", "^[^0-9]+$") == false);
        Console.WriteLine(Regex.IsMatch("hello-world_1", "^[\\w-]+$") == true);
        Console.WriteLine(Regex.IsMatch("---", "^[\\w-]+$") == true);
        Console.WriteLine(Regex.IsMatch("++*+", "^[+*]+$") == true);
        Console.WriteLine(Regex.IsMatch("abc", "^[+*]+$") == false);
        Console.WriteLine(Regex.IsMatch("^", "^[^a]+$") == true);
        Console.WriteLine(Regex.IsMatch("a", "^[^a]+$") == false);
        Console.WriteLine(Regex.IsMatch("1.5", "^[0-9.]+$") == true);
        Console.WriteLine(Regex.IsMatch("1.5", "^[0-9]+$") == false);
        Console.WriteLine(Regex.IsMatch("_x9", "^[\\w]+$") == true);
        Console.WriteLine(Regex.IsMatch("a.z", "^[a.z]+$") == true);
        Console.WriteLine(Regex.IsMatch("az", "^[a.z]+$") == true);
        Console.WriteLine(Regex.IsMatch("a5x", "^[a-z\\d]+$") == true);
        Console.WriteLine(Regex.IsMatch("XYZ", "^[a-z\\d]+$") == false);
        Console.WriteLine(Regex.IsMatch("5", "^[\\d]a?$") == true);

        // ============ 2. groups (nested numbering) ============
        Console.WriteLine(Regex.IsMatch("ab", "^((a)(b))$") == true);
        Console.WriteLine(Regex.IsMatch("a", "^((a)(b))$") == false);
        Console.WriteLine(Regex.IsMatch("abab", "^(ab)+$") == true);
        Console.WriteLine(Regex.IsMatch("ababab", "^(ab)+$") == true);
        Console.WriteLine(Regex.IsMatch("aba", "^(ab)+$") == false);
        Console.WriteLine(Regex.IsMatch("abc", "^(a|ab)c$") == true);
        Console.WriteLine(Regex.IsMatch("ac", "^(a|ab)c$") == true);
        Console.WriteLine(Regex.IsMatch("ab", "^(a|ab)c$") == false);
        Console.WriteLine(Regex.IsMatch("", "^()$") == true);
        Console.WriteLine(Regex.IsMatch("a", "^()$") == false);
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "$2$1") == "ba");
        Console.WriteLine(Regex.Replace("abc", "((a)(b))c", "$1-$2-$3") == "ab-a-b");
        Console.WriteLine(Regex.Replace("abc", "((a)(b))(c)", "$1$2$3$4") == "ababc");
        Console.WriteLine(Regex.Replace("xy", "(x)(y)", "$1$2$1$2") == "xyxy");
        Console.WriteLine(Regex.Replace("a1", "(a)(\\d)", "$2-$1") == "1-a");
        Console.WriteLine(Regex.Replace("a1b22c333", "(\\d)+", "[$1]") == "a[1]b[2]c[3]");

        // ============ 3. backtracking ============
        Console.WriteLine(Regex.IsMatch("abababc", "^(ab)*c$") == true);
        Console.WriteLine(Regex.IsMatch("ababab", "^(ab)*c$") == false);
        Console.WriteLine(Regex.IsMatch("ac", "^a(ab)*c$") == true);
        Console.WriteLine(Regex.IsMatch("aabb", "^a(ab)*b$") == true);
        Console.WriteLine(Regex.IsMatch("aaab", "^(a|aa)*b$") == true);
        Console.WriteLine(Regex.IsMatch("aaaaab", "^(a|aa)*b$") == true);
        Console.WriteLine(Regex.IsMatch("abcabc", ".*c$") == true);
        Console.WriteLine(Regex.IsMatch("abcabc", ".*ab") == true);
        Console.WriteLine(Regex.IsMatch("abc", "a(bc|b)c") == true);
        Console.WriteLine(Regex.IsMatch("abbc", "a(bc|b)c") == false);
        Console.WriteLine(Regex.IsMatch("abbbbc", "ab+c") == true);
        Console.WriteLine(Regex.Replace("abbbbc", "ab+c", "X") == "X");
        Console.WriteLine(Regex.Replace("abbbbc", "ab+", "X") == "Xc");
        Console.WriteLine(Regex.Replace("aaaa", "a{2,3}", "X") == "Xa");
        Console.WriteLine(Regex.IsMatch("ababab", "^(ab){2,3}$") == true);
        Console.WriteLine(Regex.IsMatch("abab", "^(ab){3}$") == false);
        Console.WriteLine(Regex.IsMatch("aaabb", "a+bb") == true);
        Console.WriteLine(Regex.IsMatch("aaac", "a+b?c") == true);

        // ============ 4. Unicode (literal CJK) ============
        Console.WriteLine(Regex.IsMatch("你好世界", "你好") == true);
        Console.WriteLine(Regex.IsMatch("你好世界", "你好世界") == true);
        Console.WriteLine(Regex.IsMatch("你好世界", "再见") == false);
        Console.WriteLine(Regex.Replace("你好世界", "世界", "abc") == "你好abc");
        Console.WriteLine(Regex.IsMatch("abc你好", "^abc.*你好$") == true);
        Console.WriteLine(Regex.IsMatch("中", "[一-龥]") == true);
        Console.WriteLine(Regex.IsMatch("A", "[一-龥]") == false);
        Console.WriteLine(Regex.IsMatch("中", "[A-Za-z]") == false);
        Console.WriteLine(Regex.IsMatch("中", "[^A-Za-z]") == true);
        Console.WriteLine(Regex.Split("a中b", "中").Length == 2);
        Console.WriteLine(Regex.Split("a中b", "中")[1] == "b");
        Console.WriteLine(Regex.Replace("hello 你好 world", " ", "_") == "hello_你好_world");
        Console.WriteLine(Regex.IsMatch("中文ABC123", "\\d+$") == true);
        Console.WriteLine(Regex.IsMatch("ABC123", "^\\w+$") == true);
        Console.WriteLine(Regex.IsMatch("中文", "^[a-zA-Z]+$") == false);
        Console.WriteLine(Regex.Replace("甲/乙/丙", "/", "-") == "甲-乙-丙");

        // ============ 5. deliberate-false pins ============
        Console.WriteLine(Regex.IsMatch("你好世界", "^你好$") == false);
        Console.WriteLine(Regex.Replace("abbbbc", "ab+c", "X") == "Xb");
    }
}}
