//------------------------------------------------------------------------------
// test_regex_match.cpp -- T2 matcher engine tests.
//
// Every expected value below was VERIFIED against .NET Framework 4.8
// (net48 probe programs run 2026-08-04, see docs/design/regex-domain.md §5
// T2 "all assertions benchmark against net48 behavior"). The probe revealed
// three places where
// the design doc diverged from net48; the probe result wins (documented in
// the headers and discover.md):
//   * "[]"/"[^]" parse ERROR (doc §3.2 claimed empty class semantics)
//   * "$" matches before a trailing '\n' (doc §3.1 claimed end-of-string only)
//   * "a{2" is a literal, not an "unclosed {" error (doc §3.4)
//
// Coverage: basic repetition, group capture, alternation priority, greedy
// {n,m} + backtracking, anchors ^$ (default + Multiline), dot (default +
// Singleline), ASCII classes \d\w\s + complements, Chinese (UTF-16) input,
// no-match paths, empty matches, IgnoreCase folding, capture rollback, and
// the step budget (catastrophic "(a*)*b" must return deterministically).
//------------------------------------------------------------------------------

#include "regex_ast.h"
#include "regex_match.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

using il2cpp::runtime::MatchResult;
using il2cpp::runtime::RegexAst;
using il2cpp::runtime::RegexAstFree;
using il2cpp::runtime::RegexParse;
using il2cpp::runtime::RegexParseError;
using il2cpp::runtime::il2cpp_regex_match;
using il2cpp::runtime::kRegexMaxMatchSteps;
using il2cpp::runtime::kRegexOptionsIgnoreCase;
using il2cpp::runtime::kRegexOptionsMultiline;
using il2cpp::runtime::kRegexOptionsSingleline;

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Parses 'pattern' (ASCII literal) with the given options.
RegexAst* Parse(const char* pattern, int32_t options = 0)
{
    const size_t n = std::strlen(pattern);
    char16_t* buf = new char16_t[n];
    for (size_t i = 0; i < n; ++i)
    {
        buf[i] = static_cast<char16_t>(static_cast<unsigned char>(pattern[i]));
    }
    RegexParseError err = RegexParseError::None;
    RegexAst* ast = RegexParse(buf, static_cast<int32_t>(n), options, &err);
    delete[] buf;
    return ast;
}

// Runs the engine; returns the match result.
MatchResult Match(const char* pattern, const char* input, int32_t options = 0)
{
    MatchResult r;
    r.success = false;
    RegexAst* ast = Parse(pattern, options);
    if (ast == nullptr)
    {
        return r;
    }
    const size_t n = std::strlen(input);
    char16_t* buf = new char16_t[n];
    for (size_t i = 0; i < n; ++i)
    {
        buf[i] = static_cast<char16_t>(static_cast<unsigned char>(input[i]));
    }
    il2cpp_regex_match(ast, buf, static_cast<int32_t>(n), &r);
    delete[] buf;
    RegexAstFree(ast);
    return r;
}

// UTF-16 (Chinese-safe) helpers: pattern given as explicit UTF-16 units so
// "\u4F60" style escapes are NOT mangled by the ASCII-literal conversion.
RegexAst* ParseUtf16Pattern(const char16_t* pattern, int32_t patternLen,
                            int32_t options = 0)
{
    RegexParseError err = RegexParseError::None;
    return RegexParse(pattern, patternLen, options, &err);
}

// UTF-16 match: pattern and input both given as explicit UTF-16 units.
MatchResult MatchUtf16(const char16_t* pattern, int32_t patternLen,
                       const char16_t* input, int32_t inputLen,
                       int32_t options = 0)
{
    MatchResult r;
    r.success = false;
    RegexAst* ast = ParseUtf16Pattern(pattern, patternLen, options);
    if (ast == nullptr)
    {
        return r;
    }
    il2cpp_regex_match(ast, input, inputLen, &r);
    RegexAstFree(ast);
    return r;
}

bool IsMatch(const char* pattern, const char* input, int32_t options = 0)
{
    return Match(pattern, input, options).success;
}

} // namespace

int main()
{
    //--------------------------------------------------------------------------
    // Basic repetition: "ab+c" vs "abbbc"
    //--------------------------------------------------------------------------
    {
        const MatchResult r = Match("ab+c", "abbbc");
        Expect(r.success, "ab+c matches abbbc");
        Expect(r.start == 0 && r.matchedLength == 5,
               "ab+c whole match is 'abbbc' (0..5)");
    }

    // "ab*c" vs "ac" (star matches zero times)
    {
        const MatchResult r = Match("ab*c", "ac");
        Expect(r.success && r.matchedLength == 2, "ab*c matches ac");
    }

    //--------------------------------------------------------------------------
    // Alternation: leftmost-first, "a|ab" vs "ab" -> "a"
    //--------------------------------------------------------------------------
    {
        const MatchResult r = Match("a|ab", "ab");
        Expect(r.success && r.matchedLength == 1,
               "a|ab vs ab -> 'a' (leftmost-first)");
        Expect(r.start == 0, "a|ab match starts at 0");
    }

    // "(a|ab)c" backtracks into the second branch -> "abc", group1 = "ab"
    {
        const MatchResult r = Match("(a|ab)c", "abc");
        Expect(r.success && r.matchedLength == 3,
               "(a|ab)c vs abc -> whole 'abc'");
        Expect(r.groups.size() == 1 && r.groups[0].start == 0 &&
                   r.groups[0].length == 2,
               "(a|ab)c group1 = 'ab' (backtracked to second branch)");
    }

    // Empty branch: "a|" matches 'a'; "|a" matches empty at 0
    {
        const MatchResult r1 = Match("a|", "a");
        Expect(r1.success && r1.matchedLength == 1, "a| vs a -> 'a'");
        const MatchResult r2 = Match("|a", "a");
        Expect(r2.success && r2.matchedLength == 0 && r2.start == 0,
               "|a vs a -> empty match at 0 (first branch wins)");
    }

    //--------------------------------------------------------------------------
    // Capture groups: "(ab)+" vs "ababab" -> group1 = LAST iteration "ab"
    //--------------------------------------------------------------------------
    {
        const MatchResult r = Match("(ab)+", "ababab");
        Expect(r.success && r.matchedLength == 6,
               "(ab)+ matches whole ababab");
        Expect(r.groups.size() == 1 && r.groups[0].start == 4 &&
                   r.groups[0].length == 2,
               "(ab)+ group1 = last iteration 'ab' at 4 (net48)");
    }

    // Nested groups: "((a)(b))" group numbering by left-paren order
    {
        const MatchResult r = Match("((a)(b))", "ab");
        Expect(r.success && r.groups.size() == 3,
               "((a)(b)) has 3 groups");
        Expect(r.groups[0].start == 0 && r.groups[0].length == 2,
               "((a)(b)) group1 = whole 'ab'");
        Expect(r.groups[1].start == 0 && r.groups[1].length == 1,
               "((a)(b)) group2 = 'a'");
        Expect(r.groups[2].start == 1 && r.groups[2].length == 1,
               "((a)(b)) group3 = 'b'");
    }

    // Capture rollback: "(a(b)?)+" vs "ab" -> group2='b'; vs "a" -> group2
    // unparticipated (start == -1)
    {
        const MatchResult r1 = Match("(a(b)?)+", "ab");
        Expect(r1.success, "(a(b)?)+ matches ab");
        Expect(r1.groups.size() == 2 && r1.groups[1].start == 1 &&
                   r1.groups[1].length == 1,
               "(a(b)?)+ vs ab: group2 = 'b'");
        const MatchResult r2 = Match("(a(b)?)+", "a");
        Expect(r2.success && r2.matchedLength == 1,
               "(a(b)?)+ matches a");
        Expect(r2.groups.size() == 2 && r2.groups[1].start == -1,
               "(a(b)?)+ vs a: group2 unparticipated (start == -1)");
    }

    //--------------------------------------------------------------------------
    // Greedy {n,m} and backtracking: "\d{2,4}" vs "12345" -> "1234"
    //--------------------------------------------------------------------------
    {
        const MatchResult r = Match("\\d{2,4}", "12345");
        Expect(r.success && r.matchedLength == 4,
               "\\d{2,4} vs 12345 -> '1234' (greedy)");
    }

    // "\d{2,4}45" vs "12345" must give back digits -> whole "12345"
    {
        const MatchResult r = Match("\\d{2,4}45", "12345");
        Expect(r.success && r.matchedLength == 5,
               "\\d{2,4}45 vs 12345 -> '12345' (backtrack)");
    }

    // "a*ab" vs "aaab" classic backtracking
    {
        const MatchResult r = Match("a*ab", "aaab");
        Expect(r.success && r.matchedLength == 4,
               "a*ab vs aaab -> 'aaab' (star gives back)");
    }

    // {n} exact and {n,m} upper bound
    {
        const MatchResult r1 = Match("a{2}", "aaa");
        Expect(r1.success && r1.matchedLength == 2, "a{2} vs aaa -> 'aa'");
        const MatchResult r2 = Match("a{2,3}", "aaaa");
        Expect(r2.success && r2.matchedLength == 3,
               "a{2,3} vs aaaa -> 'aaa' (greedy upper bound)");
        const MatchResult r3 = Match("a{0,2}", "aaa");
        Expect(r3.success && r3.matchedLength == 2, "a{0,2} vs aaa -> 'aa'");
    }

    //--------------------------------------------------------------------------
    // Anchors ^ and $ (default + Multiline)
    //--------------------------------------------------------------------------
    {
        Expect(IsMatch("^ab$", "ab"), "^ab$ matches 'ab'");
        Expect(!IsMatch("^ab$", "xab"), "^ab$ rejects 'xab'");
        Expect(!IsMatch("^ab$", "abx"), "^ab$ rejects 'abx'");
        // $ (non-Multiline) also matches before a TRAILING '\n' (net48)
        Expect(IsMatch("ab$", "ab\n"), "ab$ matches 'ab\\n' (net48 $ rule)");
        Expect(IsMatch("a$", "a\n"), "a$ matches 'a\\n'");
        Expect(!IsMatch("a$", "a\nb"), "a$ rejects 'a\\nb' (only trailing nl)");
        // ^ (non-Multiline) only at position 0
        Expect(!IsMatch("^b", "a\nb"), "^b rejects line start (default)");
        // Multiline: ^ after \n, $ before \n
        Expect(IsMatch("^b$", "a\nb", kRegexOptionsMultiline),
               "^b$ multiline matches 'b' on line 2");
        Expect(!IsMatch("^b", "a\nb"), "^b default still rejects mid-line");
        const MatchResult ml = Match("^b$", "a\nb", kRegexOptionsMultiline);
        Expect(ml.success && ml.start == 2 && ml.matchedLength == 1,
               "^b$ multiline matches at index 2");
        Expect(IsMatch("b$", "a\nb\nc", kRegexOptionsMultiline),
               "b$ multiline matches before internal \\n");
        // ^ multiline also matches position 0
        Expect(IsMatch("^a", "a\nb", kRegexOptionsMultiline),
               "^a multiline matches position 0");
    }

    //--------------------------------------------------------------------------
    // Dot: default excludes '\n', Singleline includes it
    //--------------------------------------------------------------------------
    {
        Expect(!IsMatch("a.b", "a\nb"), "a.b rejects newline (default)");
        Expect(IsMatch("a.b", "a\nb", kRegexOptionsSingleline),
               "a.b matches newline (Singleline)");
        Expect(IsMatch("a.b", "axb"), "a.b matches 'axb'");
    }

    //--------------------------------------------------------------------------
    // Character classes: ASCII \d \w \s + complements
    //--------------------------------------------------------------------------
    {
        Expect(IsMatch("\\d+", "123abc"), "\\d+ matches digits");
        Expect(!IsMatch("\\d", "x"), "\\d rejects letter");
        Expect(IsMatch("\\D", "x"), "\\D matches letter");
        Expect(!IsMatch("\\D", "1"), "\\D rejects digit");
        Expect(IsMatch("\\w+", "ab_9"), "\\w+ matches word chars");
        Expect(!IsMatch("\\w", "!"), "\\w rejects punctuation");
        Expect(IsMatch("\\s", " "), "\\s matches space");
        Expect(IsMatch("\\s", "\n"), "\\s matches newline");
        Expect(IsMatch("[a-c]+", "abc"), "[a-c]+ matches abc");
        Expect(!IsMatch("[a-c]", "d"), "[a-c] rejects d");
        Expect(IsMatch("[^abc]", "d"), "[^abc] matches d");
        Expect(IsMatch("[^a]", "\n"), "[^a] matches newline");
        Expect(IsMatch("[\\d]", "5"), "[\\d] matches 5 inside class");
        Expect(IsMatch("[-a]", "-"), "[-a] leading dash is literal");
        Expect(IsMatch("[a-]", "-"), "[a-] trailing dash is literal");
        Expect(IsMatch("[]]", "]"), "[]] leading ] is literal");
        Expect(IsMatch("[a^b]", "^"), "[a^b] inner ^ is literal");
        Expect(IsMatch("[a-cx-z]", "y"), "[a-cx-z] multiple ranges");
    }

    //--------------------------------------------------------------------------
    // Chinese (UTF-16) input: "ni hao\d+"
    //--------------------------------------------------------------------------
    {
        const char16_t pattern[] = {u'\u4F60', u'\u597D', u'\\', u'd', u'+'};
        const char16_t input[] = {u'\u4F60', u'\u597D', u'1', u'2', u'3'};
        const MatchResult r = MatchUtf16(pattern, 5, input, 5);
        Expect(r.success && r.matchedLength == 5,
               "nihao\\d+ matches nihao123 (UTF-16 units)");
    }

    //--------------------------------------------------------------------------
    // No-match paths
    //--------------------------------------------------------------------------
    {
        Expect(!IsMatch("abc", "abd"), "abc rejects abd");
        Expect(IsMatch("a+b", "ab"), "a+b matches ab");
        Expect(!IsMatch("\\d{4}", "123"), "\\d{4} rejects short input");
        Expect(!IsMatch("^ab", "aab"), "^ab rejects 'aab'");
        const MatchResult r = Match("xyz", "abc");
        Expect(!r.success && r.start == -1 && r.matchedLength == 0,
               "no-match leaves start=-1 length=0");
    }

    //--------------------------------------------------------------------------
    // Empty matches
    //--------------------------------------------------------------------------
    {
        const MatchResult r1 = Match("", "abc");
        Expect(r1.success && r1.matchedLength == 0 && r1.start == 0,
               "empty pattern matches empty at 0");
        const MatchResult r2 = Match("a*", "b");
        Expect(r2.success && r2.matchedLength == 0,
               "a* vs b -> empty match at 0");
        Expect(IsMatch("", ""), "empty pattern matches empty input");
    }

    //--------------------------------------------------------------------------
    // IgnoreCase (ASCII fold only per docs §1.4 -- a documented scope
    // deviation: net48 probe_audit ic_ea/ic_ae/ic_oe all fold non-ASCII
    // é/É æ/Æ œ/Œ. Full invariant-culture folding is out of scope; see
    // docs/audit/regex-conformance.md §5.3.)
    //--------------------------------------------------------------------------
    {
        Expect(IsMatch("a", "A", kRegexOptionsIgnoreCase),
               "a matches A with IgnoreCase");
        Expect(IsMatch("[A-Z]", "a", kRegexOptionsIgnoreCase),
               "[A-Z] matches a with IgnoreCase");
        Expect(IsMatch("Ab", "ab", kRegexOptionsIgnoreCase),
               "Ab matches ab with IgnoreCase");
        Expect(!IsMatch("a", "A"), "a rejects A without IgnoreCase");
        {
            const char16_t pat[] = {u'\u00E9'};
            const char16_t inp[] = {u'\u00C9'};
            const MatchResult r = MatchUtf16(pat, 1, inp, 1, kRegexOptionsIgnoreCase);
            Expect(!r.success,
                   "engine folds ASCII only: é vs É no-match (docs §1.4 scope)");
        }
    }

    //--------------------------------------------------------------------------
    // \p{...} Unicode categories (D-REG-UNI-1, 2026-08-12). Expected values
    // are the net48 probe output (see the e2e regexunicode extension); the
    // runtime tables were generated from the SAME probe over the whole BMP.
    //--------------------------------------------------------------------------
    {
        // ---- \p{L}: letters (ASCII, CJK, kana, hangul, Greek) ----
        Expect(IsMatch("\\p{L}", "a"), "\\p{L} matches 'a'");
        Expect(IsMatch("\\p{L}", "A"), "\\p{L} matches 'A'");
        Expect(!IsMatch("\\p{L}", "1"), "\\p{L} rejects '1'");
        Expect(!IsMatch("\\p{L}", " "), "\\p{L} rejects space");
        Expect(!IsMatch("\\p{L}", "!"), "\\p{L} rejects '!'");
        {
            // CJK/kana/hangul/Greek are letters (single UTF-16 units).
            const char16_t pat[] = {u'\\', u'p', u'{', u'L', u'}'};
             Expect(MatchUtf16(pat, 5, u"\u4E2D", 1).success, "\\p{L} matches U+4E2D");
             Expect(MatchUtf16(pat, 5, u"\u9FA5", 1).success, "\\p{L} matches U+9FA5");
            Expect(MatchUtf16(pat, 5, u"\u3042", 1).success, "\\p{L} matches あ");
            Expect(MatchUtf16(pat, 5, u"\u30A2", 1).success, "\\p{L} matches ア");
            Expect(MatchUtf16(pat, 5, u"\uD55C", 1).success, "\\p{L} matches 한");
            Expect(MatchUtf16(pat, 5, u"\u03B1", 1).success, "\\p{L} matches α");
            Expect(!MatchUtf16(pat, 5, u"\uFF11", 1).success,
                   "\\p{L} rejects fullwidth １ (a number)");
        }

        // ---- \p{N}: numbers (ASCII + fullwidth digits, Roman) ----
        Expect(IsMatch("\\p{N}", "1"), "\\p{N} matches '1'");
        Expect(IsMatch("\\p{N}", "0"), "\\p{N} matches '0'");
        Expect(!IsMatch("\\p{N}", "a"), "\\p{N} rejects 'a'");
        Expect(!IsMatch("\\p{N}", "!"), "\\p{N} rejects '!'");
        {
            const char16_t pat[] = {u'\\', u'p', u'{', u'N', u'}'};
            Expect(MatchUtf16(pat, 5, u"\uFF11", 1).success,
                   "\\p{N} matches fullwidth １");
            Expect(MatchUtf16(pat, 5, u"\uFF19", 1).success,
                   "\\p{N} matches fullwidth ９");
            Expect(MatchUtf16(pat, 5, u"\u2162", 1).success,
                   "\\p{N} matches Roman Ⅲ");
            Expect(!MatchUtf16(pat, 5, u"\u4E2D", 1).success,
                    "\\p{N} rejects U+4E2D");
        }

        // ---- \p{P}: punctuation (ASCII + CJK) ----
        Expect(IsMatch("\\p{P}", "!"), "\\p{P} matches '!'");
        Expect(IsMatch("\\p{P}", "."), "\\p{P} matches '.'");
        Expect(IsMatch("\\p{P}", ","), "\\p{P} matches ','");
        Expect(!IsMatch("\\p{P}", "a"), "\\p{P} rejects 'a'");
        Expect(!IsMatch("\\p{P}", "1"), "\\p{P} rejects '1'");
        {
            const char16_t pat[] = {u'\\', u'p', u'{', u'P', u'}'};
            Expect(MatchUtf16(pat, 5, u"\uFF0C", 1).success,
                   "\\p{P} matches ，");
            Expect(MatchUtf16(pat, 5, u"\u3002", 1).success,
                   "\\p{P} matches 。");
            Expect(MatchUtf16(pat, 5, u"\uFF1F", 1).success,
                   "\\p{P} matches ？");
            Expect(!MatchUtf16(pat, 5, u"\u4E2D", 1).success,
                    "\\p{P} rejects U+4E2D");
        }

        // ---- \p{Lu} / \p{Ll}: case-specific ----
        Expect(IsMatch("\\p{Lu}", "A"), "\\p{Lu} matches 'A'");
        Expect(IsMatch("\\p{Lu}", "Z"), "\\p{Lu} matches 'Z'");
        Expect(!IsMatch("\\p{Lu}", "a"), "\\p{Lu} rejects 'a'");
        Expect(!IsMatch("\\p{Ll}", "A"), "\\p{Ll} rejects 'A'");
        Expect(IsMatch("\\p{Ll}", "a"), "\\p{Ll} matches 'a'");
        {
            const char16_t lu[] = {u'\\', u'p', u'{', u'L', u'u', u'}'};
            const char16_t ll[] = {u'\\', u'p', u'{', u'L', u'l', u'}'};
            Expect(MatchUtf16(lu, 6, u"\u0391", 1).success,
                   "\\p{Lu} matches Greek Α");
            Expect(!MatchUtf16(lu, 6, u"\u03B1", 1).success,
                   "\\p{Lu} rejects Greek α");
            Expect(MatchUtf16(ll, 6, u"\u03B1", 1).success,
                   "\\p{Ll} matches Greek α");
            Expect(!MatchUtf16(lu, 6, u"\u4E2D", 1).success,
                    "\\p{Lu} rejects CJK U+4E2D");
            Expect(!MatchUtf16(ll, 6, u"\u4E2D", 1).success,
                    "\\p{Ll} rejects CJK U+4E2D");
        }

        // ---- \P{...}: complement (top level) ----
        Expect(!IsMatch("\\P{L}", "a"), "\\P{L} rejects 'a'");
        Expect(IsMatch("\\P{L}", "1"), "\\P{L} matches '1'");
        {
            const char16_t pat[] = {u'\\', u'P', u'{', u'L', u'}'};
            Expect(!MatchUtf16(pat, 5, u"\u4E2D", 1).success,
                    "\\P{L} rejects U+4E2D");
            Expect(MatchUtf16(pat, 5, u"!", 1).success, "\\P{L} matches '!'");
        }

        // ---- inside a class: \p{...} and \P{...} as members ----
        Expect(IsMatch("[\\p{L}]", "a"), "[\\p{L}] matches 'a'");
        Expect(!IsMatch("[\\p{L}]", "1"), "[\\p{L}] rejects '1'");
        Expect(IsMatch("[^\\p{L}]", "1"), "[^\\p{L}] matches '1'");
        Expect(!IsMatch("[^\\p{L}]", "a"), "[^\\p{L}] rejects 'a'");
        Expect(IsMatch("[\\p{N}a-z]", "q"), "[\\p{N}a-z] matches 'q'");
        Expect(IsMatch("[\\p{N}a-z]", "1"), "[\\p{N}a-z] matches '1' (is N)");
        Expect(IsMatch("[\\P{N}a-z]", "q"), "[\\P{N}a-z] matches 'q'");
        Expect(!IsMatch("[\\P{N}a-z]", "1"),
               "[\\P{N}a-z] rejects '1' (is N, not in complement)");
        Expect(IsMatch("[a\\P{N}]", "a"), "[a\\P{N}] matches 'a'");
        Expect(!IsMatch("[a\\P{N}]", "1"), "[a\\P{N}] rejects '1'");
        {
            const char16_t pat[] = {u'[', u'\\', u'p', u'{', u'N', u'}', u']'};
            Expect(MatchUtf16(pat, 7, u"\uFF11", 1).success,
                   "[\\p{N}] matches fullwidth １");
        }

        // ---- quantifiers + anchors over categories ----
        Expect(IsMatch("^\\p{L}+$", "abc"), "^\\p{L}+$ matches 'abc'");
        Expect(!IsMatch("^\\p{L}+$", "abc123"), "^\\p{L}+$ rejects 'abc123'");
        Expect(IsMatch("^\\p{N}+$", "123"), "^\\p{N}+$ matches '123'");
        Expect(!IsMatch("^\\p{N}+$", "12a"), "^\\p{N}+$ rejects '12a'");
        Expect(IsMatch("^\\p{P}+$", "!!!"), "^\\p{P}+$ matches '!!!'");
        {
            // net48 probe: (\p{N}+)-(\p{N}+) matches 123-456.
            const char16_t pat[] = {u'(', u'\\', u'p', u'{', u'N', u'}', u'+',
                                    u')', u'-', u'(', u'\\', u'p', u'{', u'N',
                                    u'}', u'+', u')'};
            const MatchResult r = MatchUtf16(pat, 17, u"123-456", 7);
            Expect(r.success && r.matchedLength == 7,
                   "(\\p{N}+)-(\\p{N}+) matches 123-456");
            Expect(r.groups.size() == 2 && r.groups[0].start == 0 &&
                       r.groups[0].length == 3,
                   "(\\p{N}+)-(\\p{N}+): group1 = '123'");
            Expect(r.groups[1].start == 4 && r.groups[1].length == 3,
                   "(\\p{N}+)-(\\p{N}+): group2 = '456'");
        }
        {
            const char16_t pat[] = {u'^', u'\\', u'p', u'{', u'L', u'}', u'+',
                                    u'$'};
            const char16_t input[] = {u'a', u'b', u'c', u'\u4E2D', u'\u6587'};
            Expect(MatchUtf16(pat, 8, input, 5).success,
                    "^\\p{L}+$ matches 'abc' + CJK");
        }

        // ---- IgnoreCase folds \p{Lu}/\p{Ll} ASCII members (net48 probe) ----
        Expect(IsMatch("^\\p{Lu}+$", "abc", kRegexOptionsIgnoreCase),
               "^\\p{Lu}+$ matches 'abc' with IgnoreCase");
        Expect(!IsMatch("^\\p{Lu}+$", "abc"),
               "^\\p{Lu}+$ rejects 'abc' without IgnoreCase");
        Expect(IsMatch("^\\p{Ll}+$", "ABC", kRegexOptionsIgnoreCase),
               "^\\p{Ll}+$ matches 'ABC' with IgnoreCase");

        // ---- unknown / malformed categories -> parse error (net48 throws
        // ArgumentException for all of these) ----
        struct PropErr { const char* pattern; RegexParseError code; };
        const PropErr propErrs[] = {
            {"\\p", RegexParseError::UnrecognizedEscape},
            {"\\p{", RegexParseError::UnrecognizedEscape},
            {"\\p{}", RegexParseError::UnrecognizedEscape},
            {"\\p{L", RegexParseError::UnrecognizedEscape},
            {"\\p{xyz}", RegexParseError::UnsupportedConstruct},
            {"\\p{Z}", RegexParseError::UnsupportedConstruct},
            {"\\p{IsBasicLatin}", RegexParseError::UnsupportedConstruct},
            {"\\pl", RegexParseError::UnrecognizedEscape},
        };
        for (const PropErr& c : propErrs)
        {
            RegexParseError err = RegexParseError::None;
            const size_t n = std::strlen(c.pattern);
            char16_t* buf = new char16_t[n];
            for (size_t i = 0; i < n; ++i)
            {
                buf[i] = static_cast<char16_t>(
                    static_cast<unsigned char>(c.pattern[i]));
            }
            RegexAst* ast = RegexParse(buf, static_cast<int32_t>(n), 0, &err);
            delete[] buf;
            Expect(ast == nullptr && err == c.code,
                   (std::string("parse error: ") + c.pattern).c_str());
            RegexAstFree(ast);
        }
    }

    //--------------------------------------------------------------------------
    // Step budget: catastrophic "(a*)*b" vs 50 'a' must return no-match
    // deterministically (net48 hangs -- probe timed out at 120s).
    //--------------------------------------------------------------------------
    {
        const char16_t pattern[] = {u'(', u'a', u'*', u')', u'*', u'b'};
        char16_t input[50];
        for (int i = 0; i < 50; ++i)
        {
            input[i] = u'a';
        }
        const MatchResult r = MatchUtf16(pattern, 6, input, 50);
        Expect(!r.success, "(a*)*b vs 50 a -> deterministic no-match (budget)");
    }

    //--------------------------------------------------------------------------
    // Long-input smoke: linear pattern on 100k chars completes (budget does
    // not false-positive on normal patterns).
    //--------------------------------------------------------------------------
    {
        const int32_t N = 100000;
        char16_t* input = new char16_t[N];
        for (int32_t i = 0; i < N; ++i)
        {
            input[i] = u'a';
        }
        const char16_t pattern[] = {u'a', u'+'};
        const MatchResult r = MatchUtf16(pattern, 2, input, N);
        Expect(r.success && r.matchedLength == N,
               "a+ matches 100k a's (linear smoke)");
        delete[] input;
    }

    //--------------------------------------------------------------------------
    // Parser error paths (contract: nullptr + error code, never throws)
    //--------------------------------------------------------------------------
    {
        struct ErrCase { const char* pattern; RegexParseError code; };
        const ErrCase cases[] = {
            {"\\q", RegexParseError::UnrecognizedEscape},
            {"[z-a]", RegexParseError::InvalidClassRange},
            {"a{2,1}", RegexParseError::InvalidRepeat},
            {"a{999999999999}", RegexParseError::InvalidRepeat},
            {"(?=a)", RegexParseError::UnsupportedConstruct},
            {"(?:a)", RegexParseError::UnsupportedConstruct},
            {"(?<name>a)", RegexParseError::UnsupportedConstruct},
            {"\\b", RegexParseError::UnsupportedConstruct},
            {"\\x41", RegexParseError::UnsupportedConstruct},
            {"\\2", RegexParseError::UnsupportedConstruct}, // undefined group (only 0)
            {"*", RegexParseError::QuantifierNothing},
            {"+", RegexParseError::QuantifierNothing},
            {"?", RegexParseError::QuantifierNothing},
            {"{2}", RegexParseError::QuantifierNothing},
            {"a**", RegexParseError::QuantifierNothing},
            {"(a", RegexParseError::UnclosedGroup},
            {"[a", RegexParseError::UnclosedClass},
            {"[]", RegexParseError::UnclosedClass}, // net48: ArgumentException
            {"[^]", RegexParseError::UnclosedClass}, // net48: ArgumentException
            {"\\", RegexParseError::UnrecognizedEscape},
        };
        for (const ErrCase& c : cases)
        {
            RegexParseError err = RegexParseError::None;
            RegexAst* ast = Parse(c.pattern);
            // Parse() discards the error; re-parse directly to capture it.
            const size_t n = std::strlen(c.pattern);
            char16_t* buf = new char16_t[n];
            for (size_t i = 0; i < n; ++i)
            {
                buf[i] = static_cast<char16_t>(static_cast<unsigned char>(c.pattern[i]));
            }
            ast = RegexParse(buf, static_cast<int32_t>(n), 0, &err);
            delete[] buf;
            Expect(ast == nullptr, "error pattern must fail parse");
            Expect(err == c.code, "error pattern must report the right code");
            RegexAstFree(ast);
        }
    }

    // Options whitelist: invalid bits rejected at parse time
    {
        RegexParseError err = RegexParseError::None;
        const char16_t p[] = {u'a'};
        RegexAst* ast = RegexParse(p, 1, 0x100, &err); // Compiled(0x08)? use 0x100
        Expect(ast == nullptr && err == RegexParseError::InvalidOptions,
               "options outside whitelist rejected");
        RegexAstFree(ast);
    }

    // Literal '{' forms (net48: "a{2" is a literal, not an error)
    {
        Expect(IsMatch("a{2", "a{2"), "a{2 matches literal a{2");
        Expect(IsMatch("a{2,3", "a{2,3"), "a{2,3 matches literal");
        Expect(IsMatch("a{", "a{"), "a{ matches literal");
        Expect(IsMatch("a{b", "a{b"), "a{b matches literal");
    }

    //--------------------------------------------------------------------------
    // 2026-08-04 conformance audit fixes (net48 probe probe_audit + .NET
    // RegexParser.cs / RegexInterpreter.cs; see docs/audit/regex-conformance.md):
    //   1. \D \W \S INSIDE a class add their COMPLEMENT sets
    //   2. \a \e escapes + in-class [\b] backspace
    //   3. escaping a non-word char yields that literal (\~ == '~')
    //   4. $ (non-Multiline) is EndZ: rejects "a\r\n" (only trailing \n)
    //--------------------------------------------------------------------------
    {
        // 1. in-class complement escapes (net48: cl_D_digit=False,
        //    cl_negD_digit=True, cl_W_punct=True, cl_S_nl=False)
        Expect(!IsMatch("[\\D]", "5"), "[\\D] rejects digit (complement)");
        Expect(IsMatch("[\\D]", "x"), "[\\D] matches letter (complement)");
        Expect(IsMatch("[^\\D]", "5"), "[^\\D] matches digit (double neg)");
        Expect(!IsMatch("[^\\D]", "x"), "[^\\D] rejects letter (double neg)");
        Expect(IsMatch("[\\W]", "!"), "[\\W] matches punctuation");
        Expect(!IsMatch("[\\W]", "a"), "[\\W] rejects word char");
        Expect(IsMatch("[^\\W]", "5"), "[^\\W] matches word char");
        Expect(!IsMatch("[\\S]", "\n"), "[\\S] rejects whitespace");
        Expect(IsMatch("[\\S]", "x"), "[\\S] matches non-space");
        Expect(IsMatch("[a\\D]", "x"), "[a\\D] matches letter");
        Expect(!IsMatch("[a\\D]", "5"), "[a\\D] rejects digit");
        Expect(IsMatch("[\\D\\d]", "5"), "[\\D\\d] matches digit (union)");

        // 2. \a \e escapes, [\b] backspace (net48: esc_a/esc_e/cl_b/cl_a2)
        Expect(IsMatch("\\a", "\x07"), "\\a matches bell");
        Expect(IsMatch("\\e", "\x1B"), "\\e matches escape 0x1B");
        Expect(IsMatch("[\\b]", "\x08"), "[\\b] matches backspace");
        Expect(IsMatch("[\\a]", "\x07"), "[\\a] matches bell in class");

        // 3. non-word escaped char = literal (net48: esc_tilde=match,
        //    esc_q=THROW); \q stays an error (existing error table)
        Expect(IsMatch("\\~", "~"), "\\~ matches literal ~");
        Expect(IsMatch("[\\~]", "~"), "[\\~] matches literal ~ in class");

        // 4. $ is EndZ: only end-of-string or BEFORE a trailing '\n' --
        //    "a\r\n" does NOT match "a$" (net48: dollar_crnl=False)
        Expect(!IsMatch("a$", "a\r\n"), "a$ rejects a\\r\\n (EndZ)");
        Expect(IsMatch("a$", "a\n"), "a$ still matches before trailing \\n");

        // 5. Bare repeat must run its continuation: a repeat that is a
        //    GROUP's whole content used to skip the finalization and any
        //    enclosing sequence remainder ("(\d+)-(\d+)" matched only the
        //    digits with groups -1; net48: "12-34" with groups "12"/"34").
        {
            const MatchResult r = Match("(\\d+)", "abc 12-34 def");
            Expect(r.success && r.start == 4 && r.matchedLength == 2,
                   "(\\d+) matches '12' at 4");
            Expect(r.groups.size() == 1 && r.groups[0].start == 4 &&
                       r.groups[0].length == 2,
                   "(\\d+) group1 = '12' (bare-repeat finalize)");
        }
        {
            const MatchResult r = Match("(\\d+)-(\\d+)", "abc 12-34 def");
            Expect(r.success && r.start == 4 && r.matchedLength == 5,
                   "(\\d+)-(\\d+) matches '12-34'");
            Expect(r.groups.size() == 2 && r.groups[0].start == 4 &&
                       r.groups[0].length == 2 && r.groups[1].start == 7 &&
                       r.groups[1].length == 2,
                   "(\\d+)-(\\d+) groups 1='12' 2='34'");
        }
        {
            const MatchResult r = Match("x(\\d+)y", "x12y");
            Expect(r.success && r.matchedLength == 4,
                   "x(\\d+)y matches x12y (sequence after bare-repeat group)");
            Expect(r.groups.size() == 1 && r.groups[0].start == 1 &&
                       r.groups[0].length == 2,
                   "x(\\d+)y group1 = '12'");
        }
        {
            // Continuation failure must still give back iterations
            // ("12-34": \d+ greedy '12' then '-' ok, 'x' fails -> give back).
            Expect(!IsMatch("(\\d+)-x", "12-34"),
                   "(\\d+)-x rejects 12-34 (give-back through continuation)");
        }
    }

    //--------------------------------------------------------------------------
    // 2026-08-08 regex2 gap fill: BACKREFERENCES (\N). net48 oracle:
    //   * (a)\1 matches "aa" not "ab"; (ab)\1 matches "abab"
    //   * a backref to a group that did NOT participate FAILS (not empty):
    //     "(a)?\1" vs "b" -> False, "(a)*\1" vs "b" -> False
    //   * a forward ref ("\1(a)") is legal but never matches (group unset at
    //     the backref point); "\2(a)(b)" legal, no match
    //   * a group captured EMPTY matches empty: "(a*)b\1" vs "ab" -> True
    //   * \1 (undefined group, no groups in pattern) -> parse error
    //   * IgnoreCase folds the comparison: "(a)\1" vs "aA" -> True
    //--------------------------------------------------------------------------
    {
        const MatchResult r1 = Match("(a)\\1", "aa");
        Expect(r1.success && r1.matchedLength == 2,
               "(a)\\1 matches 'aa'");
        Expect(r1.groups.size() == 1 && r1.groups[0].start == 0 &&
                   r1.groups[0].length == 1,
               "(a)\\1 group1 = 'a'");
        Expect(!IsMatch("(a)\\1", "ab"), "(a)\\1 rejects 'ab'");
        Expect(IsMatch("(ab)\\1", "abab"), "(ab)\\1 matches 'abab'");

        // unset-group backref fails (net48 u1/u4)
        Expect(!IsMatch("(a)?\\1", "b"), "(a)?\\1 vs b: unset group backref fails");
        Expect(!IsMatch("(a)*\\1", "b"), "(a)*\\1 vs b: zero-iter backref fails");
        Expect(IsMatch("(a)?\\1", "aa"), "(a)?\\1 matches 'aa' when group set");

        // empty-captured group matches empty (net48 u5: "(a*)b\1" vs "ab")
        Expect(IsMatch("(a*)b\\1", "ab"), "(a*)b\\1 matches 'ab' (empty backref)");

        // forward reference: legal parse, no match (net48 fwd/fwd2)
        Expect(!IsMatch("\\1(a)", "aa"), "forward ref \\1(a) never matches");
        Expect(!IsMatch("\\2(a)(b)", "ab"), "forward ref \\2(a)(b) never matches");

        // multi-group backrefs (net48 br_group2: (a)(b)\2\1 vs abba)
        Expect(IsMatch("(a)(b)\\2\\1", "abba"), "(a)(b)\\2\\1 matches 'abba'");
        // backref into repeated group (net48 br_rep: (ab)+\1 vs ababab)
        Expect(IsMatch("(ab)+\\1", "ababab"), "(ab)+\\1 matches 'ababab'");
        // nested group backref (net48 br_nested: ((a)b)\1 vs abab)
        Expect(IsMatch("((a)b)\\1", "abab"), "((a)b)\\1 matches 'abab'");
        // alternation + backref (net48 alt/alt2/alt3)
        Expect(IsMatch("(a|b)\\1", "aa"), "(a|b)\\1 matches 'aa'");
        Expect(IsMatch("(a|b)\\1", "bb"), "(a|b)\\1 matches 'bb'");
        Expect(!IsMatch("(a|b)\\1", "ab"), "(a|b)\\1 rejects 'ab'");
        // digit backref (net48 br_alnum: (\d+)\1 vs 123123)
        Expect(IsMatch("(\\d+)\\1", "123123"), "(\\d+)\\1 matches '123123'");

        // IgnoreCase folds the backref comparison (net48 ic1/ic4)
        Expect(IsMatch("(a)\\1", "aA", kRegexOptionsIgnoreCase),
               "(a)\\1 matches 'aA' with IgnoreCase");
        Expect(!IsMatch("(a)\\1", "aB", kRegexOptionsIgnoreCase),
               "(a)\\1 rejects 'aB' with IgnoreCase");
    }

    //--------------------------------------------------------------------------
    // 2026-08-08 regex2 gap fill: LAZY QUANTIFIERS (a*? a+? a?? {n,m}?).
    // net48 oracle (probe 2026-08-08): minimum first, expand only when the
    // continuation demands it.
    //--------------------------------------------------------------------------
    {
        // a+? matches the minimum 'a' (bare)
        {
            const MatchResult r = Match("a+?", "aaa");
            Expect(r.success && r.matchedLength == 1 && r.start == 0,
                   "a+? matches a single 'a' (lazy minimum)");
        }
        // a*? matches empty at 0 (net48 i: empty@0)
        {
            const MatchResult r = Match("a*?", "aaa");
            Expect(r.success && r.matchedLength == 0 && r.start == 0,
                   "a*? matches empty at 0");
        }
        // lazy expands for the continuation: a*?a vs aaa -> "a" (net48 lz_star/h)
        {
            const MatchResult r = Match("a*?a", "aaa");
            Expect(r.success && r.matchedLength == 1 && r.start == 0,
                   "a*?a matches 'a' (lazy expanded once)");
        }
        // (a+?)(b) vs aab -> group1 = "aa" (net48 g1)
        {
            const MatchResult r = Match("(a+?)(b)", "aab");
            Expect(r.success && r.matchedLength == 3,
                   "(a+?)(b) matches 'aab'");
            Expect(r.groups.size() == 2 && r.groups[0].start == 0 &&
                       r.groups[0].length == 2,
                   "(a+?)(b) group1 = 'aa' (lazy plus group)");
            Expect(r.groups.size() == 2 && r.groups[1].start == 2 &&
                       r.groups[1].length == 1,
                   "(a+?)(b) group2 = 'b'");
        }
        // bounded lazy: (a{2,3}?)b vs aaaab -> "aaab"@1 (net48 g2)
        {
            const MatchResult r = Match("(a{2,3}?)b", "aaaab");
            Expect(r.success && r.start == 1 && r.matchedLength == 4,
                   "(a{2,3}?)b matches 'aaab' at 1 (lazy bounded)");
            Expect(r.groups.size() == 1 && r.groups[0].start == 1 &&
                       r.groups[0].length == 3,
                   "(a{2,3}?)b group1 = 'aaa'");
        }
        // a*?b vs "b" -> matches "b" (zero iterations, net48 d)
        Expect(IsMatch("a*?b", "b"), "a*?b matches 'b' (zero lazy iterations)");
        // a*?b vs aaab -> matches whole (net48 lz_alt)
        Expect(IsMatch("a*?b", "aaab"), "a*?b matches 'aaab'");
        // a+?b vs aab (net48 e)
        Expect(IsMatch("a+?b", "aab"), "a+?b matches 'aab'");
        // a??b vs b (net48 f)
        Expect(IsMatch("a??b", "b"), "a??b matches 'b'");
        // lazy does NOT over-match: a+? vs b -> False (net48 lz_plus)
        Expect(!IsMatch("a+?", "b"), "a+? rejects 'b' (needs at least one)");
    }

    //--------------------------------------------------------------------------
    // 2026-08-08 capture-rollback conformance fix: giving back repeat
    // iterations must keep the KEPT iteration's capture. net48 oracle:
    //   * (ab)+ab vs ababab -> group1 = "ab" at 2 (NOT at 0)
    //   * (a)+a vs aa -> group1 = "a" at 0 (NOT unparticipated)
    //--------------------------------------------------------------------------
    {
        const MatchResult r1 = Match("(ab)+ab", "ababab");
        Expect(r1.success && r1.matchedLength == 6,
               "(ab)+ab matches whole 'ababab'");
        Expect(r1.groups.size() == 1 && r1.groups[0].start == 2 &&
                   r1.groups[0].length == 2,
               "(ab)+ab group1 = 'ab' at 2 (kept iteration capture)");
        const MatchResult r2 = Match("(a)+a", "aa");
        Expect(r2.success && r2.matchedLength == 2,
               "(a)+a matches 'aa'");
        Expect(r2.groups.size() == 1 && r2.groups[0].start == 0 &&
                   r2.groups[0].length == 1,
               "(a)+a group1 = 'a' at 0 (participated)");
        const MatchResult r3 = Match("(a(b)?)+a", "aba");
        Expect(r3.success && r3.matchedLength == 3,
               "(a(b)?)+a matches 'aba'");
        Expect(r3.groups.size() == 2 && r3.groups[0].start == 0 &&
                   r3.groups[0].length == 2,
               "(a(b)?)+a group1 = 'ab' at 0");
        Expect(r3.groups.size() == 2 && r3.groups[1].start == 1 &&
                   r3.groups[1].length == 1,
               "(a(b)?)+a group2 = 'b' at 1");
    }

    //--------------------------------------------------------------------------
    // D-REG-1 (2026-08-12): alternation give-back inside a repeated group.
    // When an alternation's FIRST branch is a strict prefix of a later branch
    // ("a" vs "ab") inside a repeat, a failed continuation must give the
    // committed prefix back and re-match with the later branch -- the Repeat
    // node now re-matches the iteration from its start (Continuation::repeat
    // resume) instead of just dropping the whole iteration. Every expected
    // value below was re-verified against the net48 oracle (2026-08-12):
    //   * "^(a|ab)+$" vs "abab" -> group1 = "ab" at 2 (iterations 'ab'+'ab')
    //   * "^(a|ab)+$" vs "aab"  -> group1 = "ab" at 1 ('a'+'ab')
    //   * "^(a|ab)+$" vs "abaab"-> group1 = "ab" at 3
    //   * "^(a|ab)+$" vs "ab"   -> group1 = "ab" at 0 (single 'ab' iteration)
    //--------------------------------------------------------------------------
    {
        // Anchored: prefix branch must give back to the longer branch.
        {
            const MatchResult r1 = Match("^(a|ab)+$", "abab");
            Expect(r1.success && r1.matchedLength == 4,
                   "^(a|ab)+$ matches whole 'abab'");
            Expect(r1.groups.size() == 1 && r1.groups[0].start == 2 &&
                       r1.groups[0].length == 2,
                   "^(a|ab)+$ vs abab: group1 = 'ab' at 2 (ab+ab)");
        }
        {
            const MatchResult r2 = Match("^(a|ab)+$", "aab");
            Expect(r2.success && r2.matchedLength == 3,
                   "^(a|ab)+$ matches whole 'aab'");
            Expect(r2.groups.size() == 1 && r2.groups[0].start == 1 &&
                       r2.groups[0].length == 2,
                   "^(a|ab)+$ vs aab: group1 = 'ab' at 1 (a+ab)");
        }
        {
            const MatchResult r3 = Match("^(a|ab)+$", "abaab");
            Expect(r3.success && r3.matchedLength == 5,
                   "^(a|ab)+$ matches whole 'abaab'");
            Expect(r3.groups.size() == 1 && r3.groups[0].start == 3 &&
                       r3.groups[0].length == 2,
                   "^(a|ab)+$ vs abaab: group1 = 'ab' at 3");
        }
        {
            const MatchResult r4 = Match("^(a|ab)+$", "ab");
            Expect(r4.success && r4.matchedLength == 2,
                   "^(a|ab)+$ matches whole 'ab'");
            Expect(r4.groups.size() == 1 && r4.groups[0].start == 0 &&
                       r4.groups[0].length == 2,
                   "^(a|ab)+$ vs ab: group1 = 'ab' at 0 (single ab)");
        }
        Expect(IsMatch("^(a|ab)+$", "a"), "^(a|ab)+$ matches 'a'");
        Expect(IsMatch("^(a|ab)+$", "ababa"), "^(a|ab)+$ matches 'ababa'");
        Expect(IsMatch("^(a|ab)+$", "aabab"), "^(a|ab)+$ matches 'aabab'");
        Expect(IsMatch("^(a|ab)+$", "ababababab"), "^(a|ab)+$ matches long");
        Expect(!IsMatch("^(a|ab)+$", "ba"), "^(a|ab)+$ rejects 'ba'");
        Expect(!IsMatch("^(a|ab)+$", "abb"), "^(a|ab)+$ rejects 'abb'");
        Expect(!IsMatch("^(a|ab)+$", ""), "^(a|ab)+$ rejects empty");

        // Reversed branch order: "(ab|a)" still backtracks to 'a'.
        {
            const MatchResult r = Match("^(ab|a)+$", "aab");
            Expect(r.success && r.groups.size() == 1 &&
                       r.groups[0].start == 1 && r.groups[0].length == 2,
                   "^(ab|a)+$ vs aab: group1 = 'ab' at 1");
        }

        // Repeat followed by an atom: the continuation demands the give-back.
        {
            const MatchResult r1 = Match("(a|ab)+c", "aabc");
            Expect(r1.success && r1.matchedLength == 4,
                   "(a|ab)+c matches 'aabc'");
            Expect(r1.groups.size() == 1 && r1.groups[0].start == 1 &&
                       r1.groups[0].length == 2,
                   "(a|ab)+c vs aabc: group1 = 'ab' at 1");
            const MatchResult r2 = Match("(a|ab)+c", "ababc");
            Expect(r2.success && r2.matchedLength == 5,
                   "(a|ab)+c matches 'ababc'");
            Expect(r2.groups.size() == 1 && r2.groups[0].start == 2 &&
                       r2.groups[0].length == 2,
                   "(a|ab)+c vs ababc: group1 = 'ab' at 2");
        }

        // {0,} star and lazy forms get the same alternation give-back.
        Expect(IsMatch("^(a|ab)*$", "abab"), "^(a|ab)*$ matches 'abab'");
        Expect(IsMatch("^(a|ab)*$", "aab"), "^(a|ab)*$ matches 'aab'");
        {
            const MatchResult r1 = Match("^(a|ab)+?$", "aab");
            Expect(r1.success && r1.groups.size() == 1 &&
                       r1.groups[0].start == 1 && r1.groups[0].length == 2,
                   "^(a|ab)+?$ vs aab: group1 = 'ab' at 1 (lazy)");
            const MatchResult r2 = Match("^(a|ab)+?$", "abab");
            Expect(r2.success && r2.groups.size() == 1 &&
                       r2.groups[0].start == 2 && r2.groups[0].length == 2,
                   "^(a|ab)+?$ vs abab: group1 = 'ab' at 2 (lazy)");
            const MatchResult r3 = Match("(a|ab)+?c", "aabc");
            Expect(r3.success && r3.groups.size() == 1 &&
                       r3.groups[0].start == 1 && r3.groups[0].length == 2,
                   "(a|ab)+?c vs aabc: group1 = 'ab' at 1 (lazy)");
        }

        // Longer second branch inside the repeat ("(ab|abc)+").
        {
            const MatchResult r = Match("^(ab|abc)+$", "abcabc");
            Expect(r.success && r.groups.size() == 1 &&
                       r.groups[0].start == 3 && r.groups[0].length == 3,
                   "^(ab|abc)+$ vs abcabc: group1 = 'abc' at 3");
        }

        // No-match give-back cycle terminates deterministically (the give-back
        // alternates 'a'@1 <-> 'ab'@2; the state-revisit guard stops it).
        {
            const std::string in = "ab" + std::string(22, 'b') + "ab";
            const char* input = in.c_str();
            const MatchResult r = Match("^(a|ab)+$", input);
            Expect(!r.success, "^(a|ab)+$ rejects 'ab'+b*22+'ab' (deterministic)");
        }
    }

    if (g_failures == 0)
    {
        std::printf("PASS: test_regex_match (%d assertions)\n", 1);
        return 0;
    }
    std::printf("FAILURES: %d\n", g_failures);
    return 1;
}
