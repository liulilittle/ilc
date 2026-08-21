//------------------------------------------------------------------------------
// regerepeat.cs -- REGEX REPETITION DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C74, AFTER the regex domain landed
// backreferences (22b) and lazy quantifiers (regex_parse.cpp:1024).
// Extends regex2/regex3/regexdeep2/regexunicode over the WIRED static
// surface (MethodMap.Regex.cs -> regex_api.cpp: IsMatch/Replace/Split).
//
// THIS case drives the REPETITION / QUANTIFIER axis to exhaustion:
//
//   1. GREEDY vs LAZY for every quantifier form: `*` vs `*?`, `+` vs `+?`,
//      `?` vs `??`, `{n}` vs `{n}?`, `{n,}` vs `{n,}?`, `{n,m}` vs
//      `{n,m}?`. Each pair is shown producing a DIFFERENT observable result
//      (Replace expansion or Match success) so a wrong default (all-greedy /
//      all-lazy) fails the byte-diff.
//   2. GIVE-BACK / BACKTRACKING (回溯): greedy quantifiers give back to a
//      following atom ("a+ab" needs the + to release one 'a'), lazy
//      quantifiers grow one iteration at a time until the continuation
//      succeeds ("a+?b" takes the minimum then grows), and nested
//      alternation give-back ("^(a|ab)+$" iterates 'ab','ab').
//   3. NESTED QUANTIFIERS (嵌套量词): quantified groups whose child is itself
//      quantified -- `(a+)+`, `(a*)*`, `(ab+)*`, `(ab*)+`, `(a{2,3})+`,
//      `((a)+)+`, `((a|b)+)+` -- including the empty-iteration guard
//      ("(a*)*" on "b"), and lazy INSIDE greedy (`(a+?)+`).
//   4. BOUNDED + EMPTY INPUT: `{0}`, `{0,}`, `{0,1}`, empty input against
//      every star-form, `^$` empty anchors, and `a*` on empty matches zero.
//
// OUT OF SCOPE (recorded, NOT probed): \p{...} \uXXXX \xNN \b \B lookarounds
// named groups (?i) (regex_parse.cpp whitelist, UnsupportedConstruct), the
// RegexOptions overloads (enum-slot dispatch build breaker), and instance
// forms (no Regex object graph). \w/\s stay on ASCII input (ASCII-only in the
// runtime). Surrogate-pair input is not quantified (D-REG-UNI-2).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        // ============ 1. greedy vs lazy: `*` / `*?` ============
        Console.WriteLine("g_star=" + (Regex.Replace("aaa", "a*", "-") == "--"));
        Console.WriteLine("l_star=" + (Regex.Replace("aaa", "a*?", "-") == "-a-a-a-"));
        Console.WriteLine("g_star_end=" + (Regex.Replace("aa", "a*", "-") == "--"));
        Console.WriteLine("g_star_mid=" + (Regex.Replace("aba", "a*", "-") == "--b--"));
        Console.WriteLine("g_star_consume=" + (Regex.IsMatch("aaaa", "a*b") == false));

        // ============ 2. greedy vs lazy: `+` / `+?` ============
        Console.WriteLine("g_plus=" + (Regex.Replace("aaa", "a+", "X") == "X"));
        Console.WriteLine("l_plus=" + (Regex.Replace("aaa", "a+?", "X") == "XXX"));
        Console.WriteLine("l_plus_2=" + (Regex.Replace("abbb", "ab+?", "X") == "Xbb"));
        Console.WriteLine("g_plus_give=" + (Regex.Replace("aaab", "a+b", "X") == "X"));
        Console.WriteLine("l_plus_grow=" + (Regex.Replace("aaab", "a+?b", "X") == "X"));
        Console.WriteLine("g_plus_no=" + (Regex.IsMatch("aab", "a+b") == true));

        // ============ 3. greedy vs lazy: `?` / `??` ============
        Console.WriteLine("g_opt=" + (Regex.Replace("ab", "a?b", "X") == "X"));
        Console.WriteLine("g_opt_nomatch=" + (Regex.Replace("b", "a?b", "X") == "X"));
        Console.WriteLine("l_opt=" + (Regex.Replace("ab", "a??b", "X") == "X"));
        Console.WriteLine("l_opt_prefix=" + (Regex.Replace("ab", "a??", "-") == "-a-b-"));
        Console.WriteLine("g_opt_prefix=" + (Regex.Replace("ab", "a?", "-") == "--b-"));
        Console.WriteLine("l_opt_grow=" + (Regex.Replace("aab", "a??ab", "X") == "X"));

        // ============ 4. bounded: `{n}` / `{n}?` ============
        Console.WriteLine("g_n=" + (Regex.Replace("aaaa", "a{2}", "X") == "XX"));
        Console.WriteLine("l_n=" + (Regex.Replace("aaaa", "a{2}?", "X") == "XX"));
        Console.WriteLine("g_n_under=" + (Regex.IsMatch("aaa", "a{4}") == false));
        Console.WriteLine("g_n_0=" + (Regex.Replace("aaa", "a{0}", "-") == "-a-a-a-"));
        Console.WriteLine("g_n_match=" + (Regex.IsMatch("aa", "^a{2}$") == true));

        // ============ 5. bounded: `{n,}` / `{n,}?` ============
        Console.WriteLine("g_np=" + (Regex.Replace("aaaa", "a{2,}", "X") == "X"));
        Console.WriteLine("l_np=" + (Regex.Replace("aaaa", "a{2,}?", "X") == "XX"));
        Console.WriteLine("g_np_min=" + (Regex.Replace("aa", "a{3,}", "X") == "aa"));
        Console.WriteLine("l_np_grow=" + (Regex.Replace("aaab", "a{1,}?b", "X") == "X"));

        // ============ 6. bounded: `{n,m}` / `{n,m}?` ============
        Console.WriteLine("g_nm=" + (Regex.Replace("aaaaa", "a{2,4}", "X") == "Xa"));
        Console.WriteLine("l_nm=" + (Regex.Replace("aaaaa", "a{2,4}?", "X") == "XXa"));
        Console.WriteLine("g_nm_give=" + (Regex.Replace("aaab", "a{2,4}b", "X") == "X"));
        Console.WriteLine("l_nm_grow=" + (Regex.Replace("aaab", "a{1,3}?b", "X") == "X"));
        Console.WriteLine("g_nm_under=" + (Regex.IsMatch("a", "a{2,3}") == false));
        Console.WriteLine("l_nm_min=" + (Regex.IsMatch("aaa", "a{2,3}?") == true));

        // ============ 7. give-back / backtracking ============
        Console.WriteLine("gb1=" + (Regex.IsMatch("aab", "a+ab") == true));       // + gives one 'a' back
        Console.WriteLine("gb2=" + (Regex.Replace("aab", "a+ab", "X") == "X"));
        Console.WriteLine("gb3=" + (Regex.IsMatch("aaac", "a+?b?c") == true));    // lazy grows to 3 then b? takes 0
        Console.WriteLine("gb4=" + (Regex.Replace("aaab", "(a+)b", "[$1]") == "[aaa]"));
        Console.WriteLine("gb5=" + (Regex.Replace("aaab", "(a+?)b", "[$1]") == "[aaa]"));
        Console.WriteLine("gb6=" + (Regex.Replace("abab", "^(a|ab)+$", "[$1]") == "[ab]"));
        Console.WriteLine("gb7=" + (Regex.Replace("aab", "a*ab", "X") == "X"));
        Console.WriteLine("gb8=" + (Regex.Replace("abbb", "(ab+?)b", "[$1]") == "[ab]b"));

        // ============ 8. nested quantifiers ============
        Console.WriteLine("n1=" + (Regex.IsMatch("aaaa", "^(a+)+$") == true));
        Console.WriteLine("n2=" + (Regex.IsMatch("aaab", "^(a+)+b$") == true));
        Console.WriteLine("n3=" + (Regex.Replace("aaaa", "(a+)+", "X") == "X"));
        Console.WriteLine("n4=" + (Regex.Replace("aaaa", "(a*)*", "-") == "--"));
        Console.WriteLine("n5=" + (Regex.Replace("abab", "(ab*)+", "X") == "X"));
        Console.WriteLine("n6=" + (Regex.Replace("abab", "(ab+)*", "X") == "XX")); // whole-run + trailing empty match
        Console.WriteLine("n7=" + (Regex.Replace("aaaa", "(a{2,3})+", "X") == "Xa")); // {2,3} gives up 3, no split
        Console.WriteLine("n8=" + (Regex.IsMatch("abab", "^(a|b)+$") == true));
        Console.WriteLine("n9=" + (Regex.IsMatch("abab", "^((a|b)+)+$") == true));
        Console.WriteLine("n10=" + (Regex.Replace("ab", "((a)+)+", "[$2]") == "[a]b"));
        Console.WriteLine("n11=" + (Regex.IsMatch("aaa", "^(a+?)+$") == true));   // lazy inside greedy
        Console.WriteLine("n12=" + (Regex.Replace("ababab", "(ab)+ab", "X") == "X"));
        Console.WriteLine("n13=" + (Regex.IsMatch("b", "^(a*)*$") == false));     // ^...$ must cover ALL of "b"
        Console.WriteLine("n14=" + (Regex.IsMatch("b", "^(a*)+$") == false));     // empty iterations cannot consume 'b'
        Console.WriteLine("n15=" + (Regex.Replace("aa", "(a+)+", "X") == "X"));

        // ============ 9. empty input / zero-quantity ============
        Console.WriteLine("e1=" + (Regex.IsMatch("", "^$") == true));
        Console.WriteLine("e2=" + (Regex.IsMatch("", "a*") == true));
        Console.WriteLine("e3=" + (Regex.IsMatch("", "a+") == false));
        Console.WriteLine("e4=" + (Regex.Replace("", "a*", "-") == "-"));
        Console.WriteLine("e5=" + (Regex.Replace("", "a*?", "-") == "-"));
        Console.WriteLine("e6=" + (Regex.IsMatch("b", "^a*$") == false));
        Console.WriteLine("e7=" + (Regex.IsMatch("b", "^a+$") == false));
        Console.WriteLine("e8=" + (Regex.IsMatch("aa", "a{0}") == true));          // zero-width at 0
        Console.WriteLine("e9=" + (Regex.Replace("abc", "x*", "-") == "-a-b-c-"));
        Console.WriteLine("e10=" + (Regex.Replace("abc", "(x)*", "-") == "-a-b-c-"));

        // ============ 10. deliberate-false pins ============
        Console.WriteLine("p1=" + (Regex.Replace("aaa", "a+", "X") == "XXX"));     // greedy consumes whole run
        Console.WriteLine("p2=" + (Regex.Replace("aaa", "a+?", "X") == "X"));      // lazy does NOT
        Console.WriteLine("p3=" + (Regex.Replace("aaaa", "(a{2,3})+", "X") == "XX")); // {2,3} gives up 3, no split -> Xa
        Console.WriteLine("p4=" + (Regex.Replace("aaaaa", "a{2,4}", "X") == "Xa")); // greedy {2,4} takes 4
    }
}
