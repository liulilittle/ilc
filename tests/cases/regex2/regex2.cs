using System;
using System.Text.RegularExpressions;
namespace Cases { public static class Regex2 {
    // Regex domain DEEP (batch C5): char classes, greedy quantifiers,
    // group capture, backrefs, replacement, anchors. net48 baseline is the
    // oracle (run-tests.ps1 diffs the transpiled run byte-for-byte).
    //
    // WIRED surface (MethodMap.Regex.cs -> regex_api.cpp): the STATIC
    // IsMatch/Match/Split/Replace forms (input, pattern[, ...]).
    //
    // KNOWN RUNTIME GAPS -- these throw ArgumentException at runtime while
    // net48 succeeds, so they are exercised in try/catch and the diverging
    // exception type IS the recorded defect (diff shows the split):
    //   * Backreferences: "\1" is a parse error in the runtime subset
    //     (regex_parse.cpp:600 -- UnsupportedConstruct, "not in grammar").
    //   * Non-greedy (lazy) quantifiers "a+?" / "a*?" / "{2,3}?": the parser
    //     rejects the trailing '?' (regex_parse.cpp:744 LazyQuantifier --
    //     "lazy: out of subset"). Greedy-only quantifier engine.
    //   * RegexOptions overloads (IsMatch(input, pattern, IgnoreCase),
    //     Multiline): Roslyn encodes the enum slot as VALUETYPE(0x11) while
    //     MethodMap.Regex.cs RegexStatic dispatches ps[2]==0x08 (i4) -- the
    //     call falls to the generated-name path `Regex_IsMatch_<hash>`, an
    //     UNDEFINED C++ symbol (verified: cmake build fails). NOT exercised
    //     here (build-breaker); the Multiline anchor semantics and
    //     IgnoreCase character-class matching are therefore unreachable.
    //   * Instance forms (new Regex(...)) and MatchEvaluator Replace: LOUD
    //     NotSupportedException (no Regex object graph / delegates).
    //
    // ACCEPTANCE surface (documented, exercised below): char classes incl.
    // negation/range/builtin sets, greedy quantifiers incl. bounded {n,m},
    // capture groups + $0/$1..$9 replacement, alternation, anchors ^ $ (the
    // runtime "$" also matches before a trailing '\n', net48-compatible),
    // empty-match iteration, dot (excludes '\n').
    public static void Main() {
        // ---------- character classes ----------
        Console.WriteLine(Regex.IsMatch("a1c", "[a-c]1") == true);              // True: class [a-c] then '1'
        Console.WriteLine(Regex.IsMatch("a1c", "[^b]+") == true);               // True: negated class run
        Console.WriteLine(Regex.IsMatch("abc", "[^b]+") == true);               // True: "a" then? -> a,c both non-b
        Console.WriteLine(Regex.IsMatch("xyz", "[^a-c]+") == true);             // True: all outside a-c
        Console.WriteLine(Regex.IsMatch("b", "[^a-c]") == false);               // True: b IS inside a-c
        Console.WriteLine(Regex.IsMatch("5", "\\d") == true);                   // True: builtin digit
        Console.WriteLine(Regex.IsMatch("x", "\\d") == false);                  // True
        Console.WriteLine(Regex.IsMatch("_", "\\w") == true);                   // True: word char
        Console.WriteLine(Regex.IsMatch("-", "\\w") == false);                  // True
        Console.WriteLine(Regex.IsMatch(" ", "\\s") == true);                   // True: whitespace
        Console.WriteLine(Regex.IsMatch("x", "\\s") == false);                  // True
        Console.WriteLine(Regex.IsMatch("5", "\\D") == false);                  // True: \D complement of digit
        Console.WriteLine(Regex.IsMatch("x", "\\D") == true);                   // True
        Console.WriteLine(Regex.IsMatch("a", "[\\d]") == false);                // True: digit class as member
        Console.WriteLine(Regex.IsMatch("a", "[a-z]") == true);                 // True: range
        Console.WriteLine(Regex.IsMatch("m", "[a-z]") == true);                 // True: range middle
        Console.WriteLine(Regex.IsMatch("A", "[a-z]") == false);                // True: case-sensitive
        Console.WriteLine(Regex.IsMatch("-", "[a-z]") == false);                // True
        Console.WriteLine(Regex.IsMatch("a-b", "a\\-b") == true);               // True: escaped literal '-'
        Console.WriteLine(Regex.IsMatch(".", "\\.") == true);                   // True: escaped dot
        Console.WriteLine(Regex.IsMatch("a", "[.]") == false);                 // True: [.] is literal dot only
        Console.WriteLine(Regex.IsMatch(".", "[.]") == true);                  // True: dot literal in class
        Console.WriteLine(Regex.IsMatch("a.b", "a.b") == true);                 // True: dot matches any
        Console.WriteLine(Regex.IsMatch("a\nb", "a.b") == false);               // True: dot excludes \n
        Console.WriteLine(Regex.IsMatch("a\tb", "a\\tb") == true);              // True: \t escape
        Console.WriteLine(Regex.IsMatch("a\nb", "a\\nb") == true);              // True: \n escape

        // ---------- greedy quantifiers ----------
        Console.WriteLine(Regex.IsMatch("abbbc", "ab+c") == true);              // True: + run
        Console.WriteLine(Regex.IsMatch("ac", "ab+c") == false);                // True
        Console.WriteLine(Regex.IsMatch("ac", "ab*c") == true);                 // True: * zero allowed
        Console.WriteLine(Regex.IsMatch("abc", "ab?c") == true);                // True: ? one allowed
        Console.WriteLine(Regex.IsMatch("ac", "ab?c") == true);                 // True: ? zero allowed
        Console.WriteLine(Regex.IsMatch("abbc", "ab?c") == false);              // True: ? max one
        Console.WriteLine(Regex.IsMatch("a12345", "a\\d{3}") == true);          // True: {n} exactly
        Console.WriteLine(Regex.IsMatch("a12", "a\\d{3}") == false);            // True
        Console.WriteLine(Regex.IsMatch("a12345", "a\\d{3,}") == true);         // True: {n,} at least
        Console.WriteLine(Regex.IsMatch("a12", "a\\d{3,}") == false);           // True
        Console.WriteLine(Regex.IsMatch("a123", "a\\d{2,4}") == true);          // True: {n,m}
        Console.WriteLine(Regex.IsMatch("a1", "a\\d{2,4}") == false);           // True: below min
        Console.WriteLine(Regex.IsMatch("a123456", "a\\d{2,4}") == true);       // True: {2,4} matches "a1234"
        Console.WriteLine(Regex.IsMatch("aaaa", "a{4}") == true);               // True
        Console.WriteLine(Regex.IsMatch("aaa", "a{4}") == false);               // True
        Console.WriteLine(Regex.IsMatch("", "a*") == true);                     // True: * matches empty
        Console.WriteLine(Regex.IsMatch("", "a?") == true);                     // True
        Console.WriteLine(Regex.Replace("aaa", "a+", "X") == "X");              // True: greedy consumes whole run
        Console.WriteLine(Regex.Replace("baaaab", "a{2,3}", "X") == "bXab");    // True: greedy takes 3, one 'a' left

        // ---------- group capture + replacement ----------
        Console.WriteLine(Regex.Replace("2024-06-15", "(\\d{4})-(\\d{2})-(\\d{2})", "$3/$2/$1") == "15/06/2024"); // True
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "$2$1") == "ba");       // True: group reorder
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "[$0]") == "[ab]");     // True: $0 whole match
        Console.WriteLine(Regex.Replace("abc", "x", "y") == "abc");             // True: no match
        Console.WriteLine(Regex.Replace("a$b", "\\$", "$$") == "a$b");          // True: $$ literal $
        Console.WriteLine(Regex.Replace("a1b2", "(\\d)", "<$1>") == "a<1>b<2>"); // True: per-match $1
        Console.WriteLine(Regex.Replace("ab", "(a)(b)", "$1") == "a");          // True: drop group2
        Console.WriteLine(Regex.Replace("abcdef", "^(a)(b)(c)(d)(e)(f)$", "$6$5$4$3$2$1") == "fedcba"); // True: 6 groups
        Console.WriteLine(Regex.Replace("aa", "(a)", "$1$1") == "aaaa");        // True: repeated ref
        Console.WriteLine(Regex.Replace("ab", "(a)?(b)", "[$1]") == "[a]");     // True: participating group
        Console.WriteLine(Regex.IsMatch("ab", "(a)(b)") == true);               // True: plain group match
        Console.WriteLine(Regex.Replace("a,b,c", "([a-z])", "$1$1") == "aa,bb,cc"); // True: all matches

        // ---------- alternation ----------
        Console.WriteLine(Regex.IsMatch("b", "^(a|b|c)$") == true);             // True
        Console.WriteLine(Regex.IsMatch("ab", "^(a|b|c)$") == false);           // True: full-string anchor
        Console.WriteLine(Regex.Replace("a1b2", "\\d|[ab]", "X") == "XXXX");   // True: alternation union
        Console.WriteLine(Regex.IsMatch("dog", "cat|dog|fish") == true);        // True
        Console.WriteLine(Regex.IsMatch("cat", "cat|dog|fish") == true);        // True

        // ---------- anchors ----------
        Console.WriteLine(Regex.IsMatch("abc", "^abc$") == true);               // True
        Console.WriteLine(Regex.IsMatch("xabc", "^abc$") == false);             // True
        Console.WriteLine(Regex.IsMatch("abc", "^") == true);                   // True: bare ^
        Console.WriteLine(Regex.IsMatch("abc", "$") == true);                   // True: bare $
        Console.WriteLine(Regex.IsMatch("", "^$") == true);                     // True: empty input
        Console.WriteLine(Regex.IsMatch("a", "^$") == false);                   // True
        Console.WriteLine(Regex.IsMatch("abc123", "\\d+$") == true);            // True: anchored tail
        Console.WriteLine(Regex.IsMatch("abc\n", "c$") == true);                // True: $ before trailing \n
        Console.WriteLine(Regex.IsMatch("xabc", "abc$") == true);               // True: $ end anchor
        Console.WriteLine(Regex.IsMatch("abcx", "abc$") == false);              // True

        // ---------- backreferences: RUNTIME GAP (net48 True, runtime ArgumentException) ----------
        try { Console.WriteLine(Regex.IsMatch("aa", "(a)\\1") == true); }
        catch (Exception e) { Console.WriteLine("backref:" + e.GetType().Name); } // runtime: backref:ArgumentException
        try { Console.WriteLine(Regex.Replace("abab", "(ab)\\1", "X") == "X"); }
        catch (Exception e) { Console.WriteLine("backref2:" + e.GetType().Name); }

        // ---------- lazy quantifiers: RUNTIME GAP ----------
        try { Console.WriteLine(Regex.Replace("aaa", "a+?", "-") == "---"); }
        catch (Exception e) { Console.WriteLine("lazy:" + e.GetType().Name); } // runtime: lazy:ArgumentException
        try { Console.WriteLine(Regex.Replace("aaa", "a*?", "-") == "-a-a-a-"); }
        catch (Exception e) { Console.WriteLine("lazy2:" + e.GetType().Name); }

        // ---------- deliberate false pins ----------
        Console.WriteLine(Regex.IsMatch("abc", "^abc$") == false);              // False
        Console.WriteLine(Regex.Replace("a1b2", "(\\d)", "<$1>") == "a<1>b");   // False
    }
}}
