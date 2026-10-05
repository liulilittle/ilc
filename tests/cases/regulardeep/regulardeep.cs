//------------------------------------------------------------------------------
// regulardeep.cs -- Regex domain DEEP probe 3 (e2e batch C48), net48 oracle,
// byte-for-byte. The regex static surface (MethodMap.Regex.cs -> regex_api.cpp:
// IsMatch / Match / Split / Replace) is fully wired; this case drives the
// THIRD deep axis -- REPETITION + ANCHORS + GREEDY/LAZY COMBINATIONS -- on top
// of regex2 (classes/greedy/groups), regex3 (backrefs/lazy/anchors) and
// regexdeep2 (char classes/groups/backtracking/unicode).
//
// Probed axes:
//   1. BOUNDED REPETITION under anchors: {n} / {n,} / {n,m} with ^ and $,
//      including the greedy-max boundary ("aaaa" with a{2,3} consumes 3).
//   2. GROUP repetition + anchors: (ab)+, (ab){2}, (a|b){3}, (a|ab)+ and
//      (ab)* on empty input.
//   3. GREEDY give-back with following atoms: ^a+b$, ^.*c$, a{2,3}b, and the
//      greedy-only (non-lazy) Replace consuming the maximal run.
//   4. LAZY vs GREEDY under anchors: ^a+?b$ vs ^a+b$ (lazy expands to the
//      same final state), ^a*?b$, and the empty-match iteration of a*.
//   5. CHAR-CLASS repetition: \d{2,3}, [a-z]{2}, \d+ anchored full-string.
//   6. Split with repetition patterns (\d+, \d{1,2}) and anchored alternation
//      repetition ((cat|dog)+).
//
// WIRED-surface discipline (regex3/regexdeep2 convention): STATIC 2-arg
// IsMatch / 2-arg Split / 3-arg Replace ONLY. NO RegexOptions overload (the
// enum slot is a documented build breaker: Roslyn encodes VALUETYPE(0x11)
// while the map dispatches i4 -- regex2 note), NO instance forms, NO
// MatchEvaluator, NO Match (the runtime returns string[] while net48 returns
// a Match object -- recorded divergence), NO \b /\A \z \Z \G / lookarounds /
// named groups / (?i) (parse whitelist). Every line prints a bool; net48 is
// the reference oracle.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases { public static class RegularDeep {
    public static void Main() {
        // ============ 1. bounded repetition under anchors ============
        Console.WriteLine(Regex.IsMatch("aaa", "^a{3}$") == true);       // True
        Console.WriteLine(Regex.IsMatch("aa", "^a{3}$") == false);       // True
        Console.WriteLine(Regex.IsMatch("aaaa", "^a{3}$") == false);     // True
        Console.WriteLine(Regex.IsMatch("aa", "^a{2,}$") == true);       // True
        Console.WriteLine(Regex.IsMatch("aaaaa", "^a{2,}$") == true);    // True
        Console.WriteLine(Regex.IsMatch("a", "^a{2,}$") == false);       // True
        Console.WriteLine(Regex.IsMatch("aaa", "^a{2,4}$") == true);     // True
        Console.WriteLine(Regex.IsMatch("aaaaa", "^a{2,4}$") == false);  // True
        Console.WriteLine(Regex.IsMatch("abb", "^ab{2}$") == true);      // True
        Console.WriteLine(Regex.IsMatch("ab", "^ab{2}$") == false);      // True
        Console.WriteLine(Regex.IsMatch("abbb", "^ab{1,3}$") == true);   // True
        Console.WriteLine(Regex.IsMatch("abbbbb", "^ab{1,3}$") == false);// True

        // ============ 2. group repetition + anchors ============
        Console.WriteLine(Regex.IsMatch("abab", "^(ab){2}$") == true);   // True
        Console.WriteLine(Regex.IsMatch("aba", "^(ab){2}$") == false);   // True
        Console.WriteLine(Regex.IsMatch("ababab", "^(ab)+$") == true);   // True
        Console.WriteLine(Regex.IsMatch("aba", "^(ab)+$") == false);     // True
        Console.WriteLine(Regex.IsMatch("aba", "^(a|b){3}$") == true);   // True
        Console.WriteLine(Regex.IsMatch("ab", "^(a|b){3}$") == false);   // True
        // D-REG-1 FIXED (2026-08-12): alternation give-back inside a repeated
        // group. An alternation whose FIRST branch is a strict prefix of a
        // later branch ("a" vs "ab") must give the committed prefix back when
        // the repeat's continuation fails, so the later branch can be tried.
        // net48 oracle (probe 2026-08-12): all IsMatch values below.
        Console.WriteLine(Regex.IsMatch("abab", "^(a|ab)+$") == true);   // True
        Console.WriteLine(Regex.IsMatch("aab", "^(a|ab)+$") == true);    // True
        Console.WriteLine(Regex.IsMatch("abaab", "^(a|ab)+$") == true);  // True
        Console.WriteLine(Regex.IsMatch("ab", "^(a|ab)+$") == true);     // True
        Console.WriteLine(Regex.IsMatch("ababa", "^(a|ab)+$") == true);  // True
        Console.WriteLine(Regex.IsMatch("aabab", "^(a|ab)+$") == true);  // True
        Console.WriteLine(Regex.IsMatch("ba", "^(a|ab)+$") == false);    // True
        Console.WriteLine(Regex.IsMatch("abb", "^(a|ab)+$") == false);   // True
        Console.WriteLine(Regex.IsMatch("", "^(a|ab)+$") == false);      // True
        Console.WriteLine(Regex.IsMatch("aabc", "(a|ab)+c") == true);    // True
        Console.WriteLine(Regex.IsMatch("ababc", "(a|ab)+c") == true);   // True
        Console.WriteLine(Regex.IsMatch("aab", "^(ab|a)+$") == true);    // True
        Console.WriteLine(Regex.IsMatch("abab", "^(ab|a)+$") == true);   // True
        Console.WriteLine(Regex.IsMatch("abab", "^(a|ab)*$") == true);   // True
        Console.WriteLine(Regex.IsMatch("aab", "^(a|ab)*$") == true);    // True
        Console.WriteLine(Regex.IsMatch("aab", "^(a|ab)+?$") == true);   // True
        Console.WriteLine(Regex.IsMatch("abab", "^(a|ab)+?$") == true);  // True
        Console.WriteLine(Regex.IsMatch("aabc", "(a|ab)+?c") == true);   // True
        Console.WriteLine(Regex.IsMatch("abcabc", "^(ab|abc)+$") == true); // True
        Console.WriteLine(Regex.IsMatch("abbbbbbbbbbbbbbbbbbbbbbbab", "^(a|ab)+$") == false); // True
        Console.WriteLine(Regex.IsMatch("", "^(ab)*$") == true);         // True
        Console.WriteLine(Regex.IsMatch("abab", "^(ab)*$") == true);     // True
        Console.WriteLine(Regex.IsMatch("aba", "^(ab)*$") == false);     // True

        // ============ 3. greedy give-back with following atoms ============
        Console.WriteLine(Regex.IsMatch("aaab", "^a+b$") == true);       // True (a+ gives back one)
        Console.WriteLine(Regex.IsMatch("aaa", "^a+b$") == false);       // True
        Console.WriteLine(Regex.IsMatch("abcabc", "^.*c$") == true);     // True (.* gives back)
        Console.WriteLine(Regex.IsMatch("abcXc", "^.*c$") == true);      // True
        Console.WriteLine(Regex.IsMatch("aaaa", "^.*a$") == true);       // True
        Console.WriteLine(Regex.Replace("aaaa", "a{2,3}", "X") == "Xa"); // True (greedy max 3)
        Console.WriteLine(Regex.Replace("baaaab", "a{2,3}b", "X") == "baX"); // True (leftmost "aaab" at pos 2, leading 'a' stays)

        // ============ 4. lazy vs greedy under anchors ============
        Console.WriteLine(Regex.IsMatch("aab", "^a+?b$") == true);       // True (lazy expands)
        Console.WriteLine(Regex.IsMatch("aaab", "^a+?b$") == true);      // True
        Console.WriteLine(Regex.IsMatch("aaa", "^a+$") == true);         // True
        Console.WriteLine(Regex.IsMatch("aaa", "^a+?$") == true);        // True (lazy covers all at end)
        Console.WriteLine(Regex.IsMatch("ab", "^a*?b$") == true);        // True
        Console.WriteLine(Regex.IsMatch("aab", "^a*b$") == true);        // True
        Console.WriteLine(Regex.Replace("aaa", "a+?", "-") == "---");    // True (lazy single)
        Console.WriteLine(Regex.Replace("aaab", "a*?b", "X") == "X");    // True (lazy to b)

        // ============ 5. char-class repetition + anchors ============
        Console.WriteLine(Regex.IsMatch("123", "^\\d{2,3}$") == true);   // True
        Console.WriteLine(Regex.IsMatch("1", "^\\d{2,3}$") == false);    // True
        Console.WriteLine(Regex.IsMatch("1234", "^\\d{2,3}$") == false); // True
        Console.WriteLine(Regex.IsMatch("ab", "^[a-z]{2}$") == true);    // True
        Console.WriteLine(Regex.IsMatch("abc", "^[a-z]{2}$") == false);  // True
        Console.WriteLine(Regex.IsMatch("hello", "^[a-z]+$") == true);   // True
        Console.WriteLine(Regex.IsMatch("Hello", "^[a-z]+$") == false);  // True
        Console.WriteLine(Regex.IsMatch("a1b2c3", "^[a-z]\\d+$") == false); // True

        // ============ 6. Split with repetition + alternation repetition ============
        Console.WriteLine(Regex.Split("a1b22c333", "\\d+").Length == 4); // True
        Console.WriteLine(Regex.Split("a1b22c333", "\\d+")[2] == "c");   // True
        Console.WriteLine(Regex.Split("x1y22z", "\\d{1,2}").Length == 3);// True
        Console.WriteLine(Regex.Split("x1y22z", "\\d{1,2}")[1] == "y");  // True
        Console.WriteLine(Regex.Split("ababab", "ab").Length == 4);      // True
        Console.WriteLine(Regex.IsMatch("catdog", "^(cat|dog)+$") == true); // True
        Console.WriteLine(Regex.IsMatch("catcat", "^(cat|dog)+$") == true); // True
        Console.WriteLine(Regex.IsMatch("catx", "^(cat|dog)+$") == false);// True
        Console.WriteLine(Regex.IsMatch("", "^(cat|dog)*$") == true);    // True
        Console.WriteLine(Regex.IsMatch("dogcatdog", "^(cat|dog)+$") == true); // True

        // ============ deliberate-false pins ============
        Console.WriteLine(Regex.IsMatch("aaab", "^a+b$") == false);      // False
        Console.WriteLine(Regex.Replace("aaaa", "a{2,3}", "X") == "XX"); // False
        Console.WriteLine(Regex.Split("a1b22c333", "\\d+").Length == 3); // False
    }
}}
