//------------------------------------------------------------------------------
// regex3.cs -- end-to-end reference case for System.Text.RegularExpressions
// DEEP coverage, layer 3 (net48 oracle). Extends regex2 (batch 22b unlocked
// backreferences + lazy quantifiers at the runtime engine) with deeper
// probes:
//
//   * BACKREFERENCES \1: (a)\1, (ab)\1, (a|b)\1, (xy)\1, multi-group
//     (a)(b)(c)\1\2\3, (a)(b)\1 -- net48 group-numbering + capture reuse;
//   * LAZY quantifiers: a+?, a*?, ab+?, ab*?, a+?b (minimal-first),
//     a*? empty-match iteration, anchored lazy;
//   * ANCHORS ^ $ in DEFAULT (non-multiline) mode: ^ matches only input
//     start, $ only input end (or before a trailing \n). NOTE: the
//     RegexOptions.Multiline overload is NOT exercised -- the transpiler
//     falls to the generated-name path (build breaker, recorded gap in
//     regex2/regexcase2/regdeep); multiline anchor semantics are therefore
//     unreachable from a transpiled case.
//   * CHAR-CLASS nesting: [a-z\d] (digit member), \w+\d alternation, ranges;
//   * REPLACEMENT $1 refs: (\w)(\d) swap, $1$2$1 reuse, multi-group
//     reversal $4$3$2$1, whole-word swap.
//
// WIRED surface (MethodMap.Regex.cs -> regex_api.cpp): static
// IsMatch/Replace only (2/3-arg); instance Regex and MatchEvaluator are
// recorded gaps. Backrefs/lazy landed in regex_parse/match (batch 22b) --
// regex2's try/catch gap markers no longer fire.
//
// Every line is deterministic; net48 is the reference baseline
// (run-tests.ps1 diffs byte-for-byte). Lines that diverge expose runtime
// defects (recorded, NOT fixed -- batch rule).
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

namespace Cases { public static class Regex3 {
    public static void Main() {
        // ============ 1. backreferences \1 ============
        Console.WriteLine("b0=" + (Regex.IsMatch("aa", "(a)\\1") == true));
        Console.WriteLine("b1=" + (Regex.IsMatch("ab", "(a)\\1") == false));
        Console.WriteLine("b2=" + (Regex.IsMatch("abab", "(ab)\\1") == true));
        Console.WriteLine("b3=" + (Regex.Replace("abab", "(ab)\\1", "X") == "X"));
        Console.WriteLine("b4=" + (Regex.Replace("aaaa", "(a)\\1", "X") == "XX"));
        Console.WriteLine("b5=" + (Regex.IsMatch("aab", "(a)\\1b") == true));
        Console.WriteLine("b6=" + (Regex.IsMatch("aac", "(a)\\1b") == false));
        Console.WriteLine("b7=" + (Regex.Replace("abcabc", "(a)(b)(c)\\1\\2\\3", "[$0]") == "[abcabc]"));
        Console.WriteLine("b8=" + (Regex.IsMatch("xyxy", "(xy)\\1") == true));
        Console.WriteLine("b9=" + (Regex.IsMatch("xyyx", "(xy)\\1") == false));
        Console.WriteLine("b10=" + (Regex.IsMatch("ab", "(a|b)\\1") == false));
        Console.WriteLine("b11=" + (Regex.IsMatch("aa", "(a|b)\\1") == true));
        Console.WriteLine("b12=" + (Regex.IsMatch("aba", "(a)(b)\\1") == true));
        Console.WriteLine("b13=" + (Regex.IsMatch("abb", "(a)(b)\\1") == false));

        // ============ 2. lazy quantifiers *? +? (minimal match) ============
        Console.WriteLine("l0=" + (Regex.Replace("aaa", "a+?", "-") == "---"));
        Console.WriteLine("l1=" + (Regex.Replace("aaa", "a*?", "-") == "-a-a-a-"));
        Console.WriteLine("l2=" + (Regex.Replace("abbb", "ab+?", "X") == "Xbb"));
        Console.WriteLine("l3=" + (Regex.Replace("abbb", "ab*?", "X") == "Xbbb"));
        Console.WriteLine("l4=" + (Regex.Replace("aaab", "a+?b", "X") == "X"));
        Console.WriteLine("l5=" + (Regex.IsMatch("aaa", "a+?") == true));
        Console.WriteLine("l6=" + (Regex.IsMatch("aaa", "^a+?$") == true));
        Console.WriteLine("l7=" + (Regex.Replace("abc", "a*?", "-") == "-a-b-c-"));

        // ============ 3. anchors ^ $ (default, non-multiline mode) ============
        Console.WriteLine("an0=" + (Regex.IsMatch("line1\nline2", "^line2") == false));
        Console.WriteLine("an1=" + (Regex.IsMatch("line1\nline2", "line2$") == true));
        Console.WriteLine("an2=" + (Regex.IsMatch("line1\nline2", "line1$") == false));
        Console.WriteLine("an3=" + (Regex.IsMatch("", "^$") == true));

        // ============ 4. char-class nesting ============
        Console.WriteLine("c0=" + (Regex.IsMatch("ABC", "[a-z\\d]+") == false));
        Console.WriteLine("c1=" + (Regex.IsMatch("a5x", "[a-z\\d]+") == true));
        // ============ 5. replacement $1 refs ============
        Console.WriteLine("r0=" + (Regex.Replace("a1b2c3", "(\\w)(\\d)", "$2$1") == "1a2b3c"));
        Console.WriteLine("r1=" + (Regex.Replace("xabx", "(a)(b)", "$1$2$1") == "xabax"));
        Console.WriteLine("r2=" + (Regex.Replace("a1b", "([a-z])(\\d)", "<$1$2>") == "<a1>b"));
        Console.WriteLine("r3=" + (Regex.Replace("a1b2", "([a-z])(\\d)", "<$2$1>") == "<1a><2b>"));
        Console.WriteLine("r4=" + (Regex.Replace("hello world", "(\\w+) (\\w+)", "$2 $1") == "world hello"));
        Console.WriteLine("r5=" + (Regex.Replace("2024", "(\\d)(\\d)(\\d)(\\d)", "$4$3$2$1") == "4202"));

        // ============ 6. alternation ============
        Console.WriteLine("t0=" + (Regex.IsMatch("ab", "^(a|b)+$") == true));
    }
}}
