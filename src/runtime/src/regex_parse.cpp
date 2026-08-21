#include "regex_ast.h"
#include "regex_unicode_data.h"

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <new>
#include <vector>

//------------------------------------------------------------------------------
// regex_parse.cpp -- MINIMAL parser for the docs/design/regex-domain.md §3
// grammar subset. FALLBACK AUTHORSHIP: this is the T1 deliverable
// (regex_parse.cpp) that the parallel T1 agent had not produced when T2
// started; T2 built it to keep the matcher testable. All error paths return
// nullptr + RegexParseError (the header contract) -- this layer never throws.
//
// Behavior verified against net48 probe runs (2026-08-04); the probe results
// are the authority where docs/design/regex-domain.md §3 diverges:
//   * "[]" / "[^]"  -> error (UnclosedClass)      [doc §3.2 WRONG]
//   * "[a-b-c]"     -> legal: range a-b + literal '-' + literal 'c'
//   * "[z-a]"       -> error (InvalidClassRange)
//   * "a{2" etc.    -> LITERAL '{' (only well-formed {n}/{n,}/{n,m} parse as
//                      repeats; numeric overflow or {2,1} errors) [doc §3.4
//                      WRONG about "unclosed {"]
//   * "{2}" at atom position (nothing to repeat) -> error (QuantifierNothing)
//   * "a**"         -> nested quantifier -> error (QuantifierNothing)
//   * leading "*+?" -> error (QuantifierNothing)
//   * "\q", "(?=", "\1", "\b", "\x", "\u" -> error (see §3.4 whitelist);
//     "\p{L}/\p{N}/\p{P}/\p{Lu}/\p{Ll}" SUPPORTED (D-REG-UNI-1, 2026-08-12,
//     net48-derived BMP category tables -- other categories stay errors)
//   * "\a" (bell) "\e" (0x1B) and in-class "\b" (backspace) are SUPPORTED
//     (net48 probe + RegexParser.cs ScanCharEscape); top-level "\b" stays an
//     error (word boundary is out of subset)
//   * "\D \W \S" INSIDE a class add their complement sets (net48 probe:
//     "[\D]" matches 'x' not '5', "[^\D]" matches '5' not 'x')
//   * escaping a NON-word char yields that literal ("\~" == '~'); escaping a
//     word char ("\q") is still UnrecognizedEscape (net48 probe + ScanCharEscape)
//   * semantic audit 2026-08-04: docs/audit/regex-conformance.md
//------------------------------------------------------------------------------

namespace il2cpp { namespace runtime {

namespace {

struct ParseState {
    const char16_t* pattern;
    int32_t length;
    int32_t pos;
    int32_t options;
    RegexParseError error;
    int32_t nextGroup; // next group index (1-based), left-paren order
    int32_t groupCount; // total capture groups in the WHOLE pattern (pre-pass)
};

// Defined at the bottom of this namespace; used by error paths below.
void FreeNode(RegexNode* n);

RegexNode* NewNode(RegexNodeKind kind)
{
    RegexNode* n = new (std::nothrow) RegexNode();
    if (n == nullptr)
    {
        return nullptr;
    }
    std::memset(n, 0, sizeof(*n));
    n->kind = kind;
    n->max = -1; // Repeat default: unbounded
    return n;
}

//------------------------------------------------------------------------------
// Character sets (docs §1.4-4: ASCII-only)
//------------------------------------------------------------------------------

// \D/\W/\S map to the same sets as \d/\w/\s (the complement flag is applied
// by the caller).
char16_t BuiltinSetChar(char16_t c)
{
    if (c >= u'A' && c <= u'Z')
    {
        return static_cast<char16_t>(c + (u'a' - u'A'));
    }
    return c;
}

void AddBuiltinRanges(std::vector<RegexRange>* buf, char16_t kind)
{
    switch (BuiltinSetChar(kind))
    {
    case u'd': // [0-9]
        buf->push_back({u'0', u'9'});
        break;
    case u'w': // [A-Za-z0-9_]
        buf->push_back({u'A', u'Z'});
        buf->push_back({u'a', u'z'});
        buf->push_back({u'0', u'9'});
        buf->push_back({u'_', u'_'});
        break;
    case u's': // [ \t\r\n\f\v]
        buf->push_back({u' ', u' '});
        buf->push_back({u'\t', u'\t'});
        buf->push_back({u'\r', u'\r'});
        buf->push_back({u'\n', u'\n'});
        buf->push_back({u'\f', u'\f'});
        buf->push_back({u'\v', u'\v'});
        break;
    default:
        break;
    }
}

// INSIDE a character class, \D \W \S add the COMPLEMENT set as ordinary
// members (RegexParser.cs ScanCharClass: AddDigit(ch == 'D') etc.; net48
// probe: [\D] matches 'x' not '5', [^\D] matches '5' not 'x'). The
// complement is taken over the full char16_t domain minus the ASCII set
// this engine uses for \d \w \s (docs §1.4 ASCII scope -- the Unicode
// membership difference is a documented scope deviation).
void AddBuiltinComplementRanges(std::vector<RegexRange>* buf, char16_t kind)
{
    switch (kind)
    {
    case u'D': // complement of [0-9]
        buf->push_back({0x0000, static_cast<char16_t>(u'0' - 1)});
        buf->push_back({static_cast<char16_t>(u'9' + 1), 0xFFFF});
        break;
    case u'W': // complement of [A-Za-z0-9_]
        buf->push_back({0x0000, 0x002F});
        buf->push_back({0x003A, 0x0040});
        buf->push_back({0x005B, 0x005E});
        buf->push_back({0x0060, 0x0060});
        buf->push_back({0x007B, 0xFFFF});
        break;
    case u'S': // complement of [ \t\r\n\f\v]
        buf->push_back({0x0000, 0x0008});
        buf->push_back({0x000E, 0x001F});
        buf->push_back({0x0021, 0xFFFF});
        break;
    default:
        break;
    }
}

bool IsBuiltinClassEscape(char16_t c)
{
    return c == u'd' || c == u'D' || c == u'w' || c == u'W' ||
           c == u's' || c == u'S';
}

//------------------------------------------------------------------------------
// \p{...} / \P{...} Unicode categories (D-REG-UNI-1, 2026-08-12)
//
// SUBSET: L (Letter), N (Number), P (Punctuation), Lu (UppercaseLetter),
// Ll (LowercaseLetter) -- the five categories requested by D-REG-UNI-1.
// The BMP membership tables (regex_unicode_data.h) were generated 2026-08-12
// from a net48 probe over every code point 0x0000..0xFFFF, so the runtime
// agrees byte-for-byte with net48 on single-UTF-16-unit input. Other
// categories (M S Z C, IsBasicLatin blocks, scripts) stay UnsupportedConstruct
// (net48 accepts them; out of the subset -- recorded in the e2e report).
//
// net48 parse behavior (probe-verified):
//   * \p{L}, \p{Lu}, \p{Ll}, \p{N}, \p{P} valid; names are case-sensitive
//     ("\p{l}" -> error)
//   * \p alone, \p{, \p{} (empty), \p{xyz} (unknown), unclosed \p{L ->
//     ArgumentException (reported as UnrecognizedEscape / UnsupportedConstruct)
//   * \p{...} is usable INSIDE a class: "[\p{L}]" = L members, "[^\p{L}]" =
//     non-L; "\P{L}" adds the COMPLEMENT set as members (probe: "[\P{N}a-z]"
//     matches 'q' not '1')
//   * RegexOptions.IgnoreCase folds \p{Lu}/\p{Ll} ASCII members (probe:
//     "^\p{Lu}+$" matches "abc" under IgnoreCase) -- handled by the Set
//     node's ignoreCase flag (ASCII fold, docs §1.4-3)
//------------------------------------------------------------------------------

// Returns the range table for a category name (case-sensitive; the five
// supported names only) or nullptr when unknown.
const RegexRange* LookupUnicodeCategory(const char16_t* name, int32_t nameLen)
{
    if (nameLen == 1 && name[0] == u'L')
    {
        return kUnicodeCatL;
    }
    if (nameLen == 1 && name[0] == u'N')
    {
        return kUnicodeCatN;
    }
    if (nameLen == 1 && name[0] == u'P')
    {
        return kUnicodeCatP;
    }
    if (nameLen == 2 && name[0] == u'L' && name[1] == u'u')
    {
        return kUnicodeCatLu;
    }
    if (nameLen == 2 && name[0] == u'L' && name[1] == u'l')
    {
        return kUnicodeCatLl;
    }
    return nullptr;
}

int32_t UnicodeCategoryCount(const RegexRange* table)
{
    if (table == kUnicodeCatL)
    {
        return static_cast<int32_t>(sizeof(kUnicodeCatL) / sizeof(kUnicodeCatL[0]));
    }
    if (table == kUnicodeCatN)
    {
        return static_cast<int32_t>(sizeof(kUnicodeCatN) / sizeof(kUnicodeCatN[0]));
    }
    if (table == kUnicodeCatP)
    {
        return static_cast<int32_t>(sizeof(kUnicodeCatP) / sizeof(kUnicodeCatP[0]));
    }
    if (table == kUnicodeCatLu)
    {
        return static_cast<int32_t>(sizeof(kUnicodeCatLu) / sizeof(kUnicodeCatLu[0]));
    }
    return static_cast<int32_t>(sizeof(kUnicodeCatLl) / sizeof(kUnicodeCatLl[0]));
}

// Parses \p{Name} / \P{Name} with st->pos pointing at the '\\'. On success
// appends the category's ranges to 'ranges' (plain, sorted membership) and
// returns true with st->pos past '}'. 'complement' selects the net48 in-class
// semantics: \P{Name} INSIDE a class adds the COMPLEMENT set as ordinary
// members (probe: "[\P{N}a-z]" matches 'q' not '1'); the top-level \P is
// handled by the caller via the Set node's negate flag instead. On
// malformed/unknown input returns false with st->error set (net48 throws
// ArgumentException; here the parse contract returns an error code).
bool ParseUnicodeProperty(ParseState* st, std::vector<RegexRange>* ranges,
                          bool complement)
{
    if (st->pos + 2 >= st->length || st->pattern[st->pos + 2] != u'{')
    {
        st->error = RegexParseError::UnrecognizedEscape; // \p / \pX
        return false;
    }
    int32_t at = st->pos + 3;
    const int32_t nameStart = at;
    while (at < st->length && st->pattern[at] != u'}')
    {
        ++at;
    }
    if (at >= st->length)
    {
        st->error = RegexParseError::UnrecognizedEscape; // unclosed \p{
        return false;
    }
    if (at == nameStart)
    {
        st->error = RegexParseError::UnrecognizedEscape; // \p{}
        return false;
    }
    const RegexRange* table = LookupUnicodeCategory(st->pattern + nameStart, at - nameStart);
    if (table == nullptr)
    {
        st->error = RegexParseError::UnsupportedConstruct; // unknown category
        return false;
    }
    const int32_t count = UnicodeCategoryCount(table);
    if (complement)
    {
        // \P{Name} inside a class: add the COMPLEMENT (everything not in the
        // category) as ordinary members (net48 ScanCharClass
        // AddUnicodeCategory behavior; probe: "[\P{L}]" matches '1' not 'a').
        char16_t next = 0;
        for (int32_t i = 0; i < count; ++i)
        {
            if (static_cast<int32_t>(table[i].lo) > static_cast<int32_t>(next))
            {
                ranges->push_back({next, static_cast<char16_t>(table[i].lo - 1)});
            }
            next = static_cast<char16_t>(table[i].hi + 1);
        }
        if (static_cast<int32_t>(next) <= 0xFFFF)
        {
            ranges->push_back({next, 0xFFFF});
        }
    }
    else
    {
        for (int32_t i = 0; i < count; ++i)
        {
            ranges->push_back(table[i]);
        }
    }
    st->pos = at + 1;
    return true;
}

// Builds a Set node from 'ranges' (frees it on failure).
RegexNode* NewSetFromRanges(std::vector<RegexRange>* ranges, bool negate,
                            bool ignoreCase)
{
    RegexNode* set = NewNode(RegexNodeKind::Set);
    if (set == nullptr)
    {
        return nullptr;
    }
    set->negate = negate;
    set->rangeCount = static_cast<int32_t>(ranges->size());
    set->ignoreCase = ignoreCase;
    if (!ranges->empty())
    {
        set->ranges = static_cast<RegexRange*>(
            std::malloc(ranges->size() * sizeof(RegexRange)));
        if (set->ranges == nullptr)
        {
            delete set;
            return nullptr;
        }
        std::memcpy(set->ranges, ranges->data(), ranges->size() * sizeof(RegexRange));
    }
    return set;
}

// net48/RegexParser.cs ScanCharEscape: \a=bell \e=escape(0x1B) \f \n \r \t \v.
// \b = backspace is handled ONLY in class context (word boundary at top
// level is out of subset and rejected before this is reached).
char16_t ControlChar(char16_t c)
{
    switch (c)
    {
    case u'a': return u'\x07';
    case u'e': return u'\x1B';
    case u'b': return u'\x08';
    case u'n': return u'\n';
    case u't': return u'\t';
    case u'r': return u'\r';
    case u'f': return u'\f';
    case u'v': return u'\v';
    default:   return c;
    }
}

// .NET rejects escapes of word characters ("\q") but treats escapes of any
// OTHER character as that literal ("\~" matches '~') -- RegexParser.cs
// ScanCharEscape default case. net48 probe: esc_q=THROW, esc_tilde=match.
bool IsWordChar(char16_t c)
{
    return (c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z') ||
           (c >= u'0' && c <= u'9');
}

bool IsMetaChar(char16_t c)
{
    switch (c)
    {
    case u'.': case u'*': case u'+': case u'?': case u'{': case u'}':
    case u'[': case u']': case u'(': case u')': case u'|': case u'^':
    case u'$': case u'\\':
        return true;
    default:
        return false;
    }
}

// Counts the TOTAL number of capture groups in the pattern (left-paren order).
// Runs as a cheap pre-pass so backreferences can be validated against groups
// defined ANYWHERE in the pattern, including forward references (net48:
// "\1(a)" is legal -- a forward backref merely fails at match time). A '(' is
// a group opener unless it is escaped or inside a [...] class.
int32_t CountCaptureGroups(const char16_t* p, int32_t len)
{
    int32_t count = 0;
    bool inClass = false;
    for (int32_t i = 0; i < len; ++i)
    {
        const char16_t c = p[i];
        if (c == u'\\')
        {
            ++i; // skip the escaped char
            continue;
        }
        if (inClass)
        {
            if (c == u']')
            {
                inClass = false;
            }
            continue;
        }
        if (c == u'[')
        {
            inClass = true;
            continue;
        }
        if (c == u'(')
        {
            ++count;
        }
    }
    return count;
}

//------------------------------------------------------------------------------
// Repeat spec "{n}" "{n,}" "{n,m}"
//
// net48 semantics (probe-verified): '{' must be followed by digits to even
// attempt a repeat. If the spec is malformed (missing '}', missing m after
// ',') the WHOLE thing stays a literal -- "a{2" and "a{2,3" match themselves.
// A well-formed spec with n>m or numeric overflow errors.
//------------------------------------------------------------------------------

// Returns 1 = repeat parsed (st->pos advanced past '}'); 0 = literal '{'
// (st->pos unchanged); -1 = well-formed but invalid (st->error set, position
// advanced past the whole spec -- the parse fails).
int TryParseRepeatSpec(ParseState* st, int32_t* outMin, int32_t* outMax)
{
    const int32_t start = st->pos;
    int32_t at = start + 1; // skip '{'
    if (at >= st->length || st->pattern[at] < u'0' || st->pattern[at] > u'9')
    {
        return 0; // literal '{'
    }

    int64_t v = 0;
    while (at < st->length && st->pattern[at] >= u'0' && st->pattern[at] <= u'9')
    {
        v = v * 10 + (st->pattern[at] - u'0');
        if (v > INT32_MAX)
        {
            st->error = RegexParseError::InvalidRepeat; // overflow
            return -1;
        }
        ++at;
    }
    const int32_t n = static_cast<int32_t>(v);

    if (at < st->length && st->pattern[at] == u'}')
    {
        *outMin = n;
        *outMax = n;
        st->pos = at + 1;
        return 1;
    }

    if (at < st->length && st->pattern[at] == u',')
    {
        ++at;
        if (at < st->length && st->pattern[at] == u'}')
        {
            *outMin = n;
            *outMax = -1; // infinite
            st->pos = at + 1;
            return 1;
        }
        if (at >= st->length || st->pattern[at] < u'0' || st->pattern[at] > u'9')
        {
            return 0; // "a{2," -> literal
        }
        v = 0;
        while (at < st->length && st->pattern[at] >= u'0' && st->pattern[at] <= u'9')
        {
            v = v * 10 + (st->pattern[at] - u'0');
            if (v > INT32_MAX)
            {
                st->error = RegexParseError::InvalidRepeat; // overflow
                return -1;
            }
            ++at;
        }
        if (at >= st->length || st->pattern[at] != u'}')
        {
            return 0; // "a{2,3" -> literal
        }
        const int32_t m = static_cast<int32_t>(v);
        if (m < n)
        {
            st->error = RegexParseError::InvalidRepeat; // {2,1}
            return -1;
        }
        *outMin = n;
        *outMax = m;
        st->pos = at + 1;
        return 1;
    }

    return 0; // "a{2x" -> literal
}

//------------------------------------------------------------------------------
// Character class parsing (net48 semantics, probe-verified)
//------------------------------------------------------------------------------

// Parses a class body into 'ranges'; on failure returns false with st->error
// set. 'negate' receives the '^' flag.
bool ParseClassBody(ParseState* st, std::vector<RegexRange>* ranges, bool* negate)
{
    // st->pos points right after '['
    if (st->pos < st->length && st->pattern[st->pos] == u'^')
    {
        *negate = true;
        ++st->pos;
    }
    else
    {
        *negate = false;
    }

    bool first = true;        // first element in the body ('[]'/'[^]' -> error)
    bool prevIsChar = false;  // previous element was a single char
    bool prevIsRange = false; // previous element was a range, so '-' is literal
    bool prevIsSet = false;   // previous element was \d/\w/\s, so '-' is literal
    char16_t prevChar = 0;    // previous single char (range endpoint candidate)

    while (true)
    {
        if (st->pos >= st->length)
        {
            st->error = RegexParseError::UnclosedClass;
            return false;
        }
        const char16_t c = st->pattern[st->pos];

        if (c == u']' && !first)
        {
            ++st->pos; // consume ']', class done
            break;
        }
        if (c == u']' && first)
        {
            // Leading ']' is a LITERAL when more chars follow ("[]]" matches
            // ']'); a bare "[]" / "[^]" has nothing but the ']' -> net48
            // throws ArgumentException, reported as UnclosedClass.
            if (st->pos + 1 < st->length)
            {
                ranges->push_back({u']', u']'});
                ++st->pos;
                prevIsChar = true;
                prevIsRange = false;
                prevIsSet = false;
                prevChar = u']';
                first = false;
                continue;
            }
            st->error = RegexParseError::UnclosedClass;
            return false;
        }

        first = false;

        if (c == u'\\')
        {
            if (st->pos + 1 >= st->length)
            {
                st->error = RegexParseError::UnrecognizedEscape;
                return false;
            }
            const char16_t e = st->pattern[st->pos + 1];
            if (IsBuiltinClassEscape(e))
            {
                // \D \W \S inside a class add their COMPLEMENT as members
                // (net48 probe + RegexParser.cs ScanCharClass).
                if (e >= u'A' && e <= u'Z')
                {
                    AddBuiltinComplementRanges(ranges, e);
                }
                else
                {
                    AddBuiltinRanges(ranges, e);
                }
                st->pos += 2;
                prevIsChar = false;
                prevIsRange = false;
                prevIsSet = true;
                prevChar = 0;
            }
            else if (e == u'n' || e == u't' || e == u'r' || e == u'f' || e == u'v' ||
                     e == u'a' || e == u'e' || e == u'b')
            {
                // \a \e \b are plain char escapes inside a class
                // (RegexParser.cs ScanCharEscape; net48 probe: [\b] matches
                // backspace, [\a] matches bell).
                const char16_t ctl = ControlChar(e);
                ranges->push_back({ctl, ctl});
                st->pos += 2;
                prevIsChar = true;
                prevIsRange = false;
                prevIsSet = false;
                prevChar = ctl;
            }
            else if (IsMetaChar(e))
            {
                ranges->push_back({e, e});
                st->pos += 2;
                prevIsChar = true;
                prevIsRange = false;
                prevIsSet = false;
                prevChar = e;
            }
            else if (e == u'p' || e == u'P')
            {
                // \p{Name} / \P{Name} inside a class: category ranges (or the
                // complement for \P) become ordinary members (net48 probe:
                // "[\p{L}]" matches 'a' not '1'; "[\P{N}a-z]" matches 'q' not
                // '1'). '-' after a set stays literal (prevIsSet).
                std::vector<RegexRange> prop;
                if (!ParseUnicodeProperty(st, &prop, e == u'P'))
                {
                    return false;
                }
                for (const RegexRange& r : prop)
                {
                    ranges->push_back(r);
                }
                prevIsChar = false;
                prevIsRange = false;
                prevIsSet = true;
                prevChar = 0;
            }
            else if (IsWordChar(e))
            {
                st->error = RegexParseError::UnrecognizedEscape;
                return false;
            }
            else
            {
                // Escaping a non-word char yields that literal ("[\~]" ==
                // '~'; net48 probe + RegexParser.cs ScanCharEscape default).
                ranges->push_back({e, e});
                st->pos += 2;
                prevIsChar = true;
                prevIsRange = false;
                prevIsSet = false;
                prevChar = e;
            }
            continue;
        }

        if (c == u'-' && prevIsChar && !prevIsRange && !prevIsSet)
        {
            // '-' is a range operator only when the previous element was a
            // single char (net48: "[a-b-c]" -> a-b range, then literal '-'
            // and 'c'; "[-a]" / "[a-]" keep '-' literal).
            if (st->pos + 1 < st->length && st->pattern[st->pos + 1] != u']')
            {
                const char16_t hi = st->pattern[st->pos + 1];
                if (hi < prevChar)
                {
                    st->error = RegexParseError::InvalidClassRange; // [z-a]
                    return false;
                }
                ranges->back().lo = prevChar;
                ranges->back().hi = hi;
                st->pos += 2;
                prevIsChar = false;
                prevIsRange = true;
                prevIsSet = false;
                prevChar = 0;
                continue;
            }
        }

        // plain literal char (includes '-' in non-range position, '^' inside)
        ranges->push_back({c, c});
        ++st->pos;
        prevIsChar = true;
        prevIsRange = false;
        prevIsSet = false;
        prevChar = c;
    }

    return true;
}

//------------------------------------------------------------------------------
// Recursive descent: Alternation -> Sequence -> Quantified -> Atom
//------------------------------------------------------------------------------

RegexNode* ParseAlternation(ParseState* st);

RegexNode* ParseAtom(ParseState* st)
{
    if (st->pos >= st->length)
    {
        st->error = RegexParseError::UnsupportedConstruct; // dangling '(' etc.
        return nullptr;
    }
    const char16_t c = st->pattern[st->pos];

    switch (c)
    {
    case u'(':
    {
        if (st->pos + 1 < st->length && st->pattern[st->pos + 1] == u'?')
        {
            // (?: (?= (?! (?<= (?<! (?> (?<name> (?i) -- all out of subset
            st->error = RegexParseError::UnsupportedConstruct;
            return nullptr;
        }
        ++st->pos; // consume '('
        RegexNode* group = NewNode(RegexNodeKind::Group);
        if (group == nullptr)
        {
            return nullptr;
        }
        group->groupIndex = st->nextGroup++;
        if (st->pos < st->length && st->pattern[st->pos] == u')')
        {
            ++st->pos; // empty group "()"
            group->child = NewNode(RegexNodeKind::Empty);
            if (group->child == nullptr)
            {
                delete group;
                return nullptr;
            }
            return group;
        }
        RegexNode* child = ParseAlternation(st);
        if (child == nullptr || st->error != RegexParseError::None)
        {
            delete group;
            return nullptr;
        }
        if (st->pos >= st->length || st->pattern[st->pos] != u')')
        {
            st->error = RegexParseError::UnclosedGroup;
            delete group;
            return nullptr;
        }
        ++st->pos; // consume ')'
        group->child = child;
        return group;
    }
    case u')':
        st->error = RegexParseError::UnclosedGroup;
        return nullptr;
    case u'[':
    {
        ++st->pos; // consume '['
        std::vector<RegexRange> ranges;
        bool negate = false;
        if (!ParseClassBody(st, &ranges, &negate))
        {
            return nullptr;
        }
        RegexNode* set = NewNode(RegexNodeKind::Set);
        if (set == nullptr)
        {
            return nullptr;
        }
        set->negate = negate;
        set->rangeCount = static_cast<int32_t>(ranges.size());
        set->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
        if (!ranges.empty())
        {
            set->ranges = static_cast<RegexRange*>(
                std::malloc(ranges.size() * sizeof(RegexRange)));
            if (set->ranges == nullptr)
            {
                delete set;
                return nullptr;
            }
            std::memcpy(set->ranges, ranges.data(), ranges.size() * sizeof(RegexRange));
        }
        return set;
    }
    case u'.':
        ++st->pos;
        return NewNode(RegexNodeKind::Any);
    case u'^':
        ++st->pos;
    {
        RegexNode* anchor = NewNode(RegexNodeKind::Anchor);
        if (anchor == nullptr)
        {
            return nullptr;
        }
        anchor->anchor = (st->options & kRegexOptionsMultiline) != 0
                             ? AnchorKind::LineStart
                             : AnchorKind::Start;
        return anchor;
    }
    case u'$':
        ++st->pos;
    {
        RegexNode* anchor = NewNode(RegexNodeKind::Anchor);
        if (anchor == nullptr)
        {
            return nullptr;
        }
        anchor->anchor = (st->options & kRegexOptionsMultiline) != 0
                             ? AnchorKind::LineEnd
                             : AnchorKind::End;
        return anchor;
    }
    case u'*':
    case u'+':
    case u'?':
        st->error = RegexParseError::QuantifierNothing; // leading quantifier
        return nullptr;
    case u'{':
    {
        // At atom position "{...}" repeats nothing -> error (net48 throws
        // "Quantifier {x,y} following nothing"); "{2x" stays a literal.
        int32_t min = 0, max = -1;
        const int32_t save = st->pos;
        const int r = TryParseRepeatSpec(st, &min, &max);
        if (r < 0)
        {
            return nullptr; // well-formed but invalid
        }
        if (r == 1)
        {
            st->error = RegexParseError::QuantifierNothing;
            return nullptr;
        }
        st->pos = save; // literal '{'
        RegexNode* lit = NewNode(RegexNodeKind::Char);
        if (lit == nullptr)
        {
            return nullptr;
        }
        lit->ch = u'{';
        lit->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
        ++st->pos;
        return lit;
    }
    case u'\\':
    {
        if (st->pos + 1 >= st->length)
        {
            st->error = RegexParseError::UnrecognizedEscape;
            return nullptr;
        }
        const char16_t e = st->pattern[st->pos + 1];
        if (e >= u'0' && e <= u'9')
        {
            // \N backreference (net48 semantics, probe-verified 2026-08-08):
            //   * "0" is not a capture slot -> treated as octal / error (net48:
            //     "\0" is octal NUL -- out of subset, we error loudly).
            //   * a number that names a real group (defined ANYWHERE in the
            //     pattern, before or after) is a Backref node; a forward ref
            //     to a group that has not been captured YET fails at match time
            //     (net48 probe: "\1(a)" vs "aa" -> no match, no parse error).
            //   * a number that names NO group: 1..9 -> net48 throws
            //     ArgumentException (UndefinedNumberedReference), > 9 -> net48
            //     falls back to an octal escape; we report UnsupportedConstruct
            //     (loud) since octal is outside the subset.
            if (e == u'0')
            {
                st->error = RegexParseError::UnsupportedConstruct; // octal NUL: out of subset
                return nullptr;
            }
            int64_t num = 0;
            int32_t at = st->pos + 1;
            while (at < st->length && st->pattern[at] >= u'0' && st->pattern[at] <= u'9')
            {
                num = num * 10 + (st->pattern[at] - u'0');
                if (num > INT32_MAX)
                {
                    st->error = RegexParseError::UnsupportedConstruct;
                    return nullptr;
                }
                ++at;
            }
            const int32_t ref = static_cast<int32_t>(num);
            if (ref < 1 || ref > st->groupCount)
            {
                st->error = RegexParseError::UnsupportedConstruct; // undefined group ref
                return nullptr;
            }
            RegexNode* br = NewNode(RegexNodeKind::Backref);
            if (br == nullptr)
            {
                return nullptr;
            }
            br->backrefGroup = ref;
            br->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
            st->pos = at;
            return br;
        }
        if (e == u'b' || e == u'B' || e == u'x' || e == u'u')
        {
            st->error = RegexParseError::UnsupportedConstruct;
            return nullptr;
        }
        if (e == u'p' || e == u'P')
        {
            // \p{Name} / \P{Name}: category set (or complement for \P).
            // Top-level \P is the Set negate flag (net48: \P{N} rejects '1').
            std::vector<RegexRange> ranges;
            if (!ParseUnicodeProperty(st, &ranges, false))
            {
                return nullptr;
            }
            return NewSetFromRanges(&ranges, e == u'P',
                                    (st->options & kRegexOptionsIgnoreCase) != 0);
        }
        if (IsBuiltinClassEscape(e))
        {
            st->pos += 2;
            std::vector<RegexRange> ranges;
            AddBuiltinRanges(&ranges, e);
            RegexNode* set = NewNode(RegexNodeKind::Set);
            if (set == nullptr)
            {
                return nullptr;
            }
            set->negate = (e >= u'A' && e <= u'Z'); // \D \W \S are complements
            set->rangeCount = static_cast<int32_t>(ranges.size());
            if (!ranges.empty())
            {
                set->ranges = static_cast<RegexRange*>(
                    std::malloc(ranges.size() * sizeof(RegexRange)));
                if (set->ranges == nullptr)
                {
                    delete set;
                    return nullptr;
                }
                std::memcpy(set->ranges, ranges.data(), ranges.size() * sizeof(RegexRange));
            }
            return set;
        }
        if (e == u'n' || e == u't' || e == u'r' || e == u'f' || e == u'v' ||
            e == u'a' || e == u'e')
        {
            st->pos += 2;
            RegexNode* ch = NewNode(RegexNodeKind::Char);
            if (ch == nullptr)
            {
                return nullptr;
            }
            ch->ch = ControlChar(e);
            ch->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
            return ch;
        }
        if (IsMetaChar(e))
        {
            st->pos += 2;
            RegexNode* ch = NewNode(RegexNodeKind::Char);
            if (ch == nullptr)
            {
                return nullptr;
            }
            ch->ch = e;
            ch->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
            return ch;
        }
        if (IsWordChar(e))
        {
            st->error = RegexParseError::UnrecognizedEscape; // \q
            return nullptr;
        }
        // Escaping a non-word char yields that literal ("\~" matches '~';
        // RegexParser.cs ScanCharEscape default, net48 probe esc_tilde).
        st->pos += 2;
        {
            RegexNode* ch = NewNode(RegexNodeKind::Char);
            if (ch == nullptr)
            {
                return nullptr;
            }
            ch->ch = e;
            ch->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
            return ch;
        }
    }
    default:
    {
        RegexNode* ch = NewNode(RegexNodeKind::Char);
        if (ch == nullptr)
        {
            return nullptr;
        }
        ch->ch = c;
        ch->ignoreCase = (st->options & kRegexOptionsIgnoreCase) != 0;
        ++st->pos;
        return ch;
    }
    }
}

// Parses an atom followed by at most one quantifier. net48 throws on nested
// quantifiers ("a**") -- reported as QuantifierNothing here.
RegexNode* ParseQuantified(ParseState* st)
{
    RegexNode* atom = ParseAtom(st);
    if (atom == nullptr || st->error != RegexParseError::None)
    {
        return nullptr;
    }
    if (st->pos >= st->length)
    {
        return atom;
    }

    const char16_t c = st->pattern[st->pos];
    int32_t min = 0, max = -1;
    bool isQuantifier = false;
    if (c == u'*')
    {
        min = 0; max = -1; isQuantifier = true; ++st->pos;
    }
    else if (c == u'+')
    {
        min = 1; max = -1; isQuantifier = true; ++st->pos;
    }
    else if (c == u'?')
    {
        min = 0; max = 1; isQuantifier = true; ++st->pos;
    }
    else if (c == u'{')
    {
        const int32_t save = st->pos;
        const int r = TryParseRepeatSpec(st, &min, &max);
        if (r < 0)
        {
            return nullptr;
        }
        if (r == 1)
        {
            isQuantifier = true;
        }
        else
        {
            st->pos = save; // literal '{' belongs to the next atom
        }
    }

    if (isQuantifier)
    {
        // Lazy suffix: "*?" "+?" "??" "{n,m}?" (net48-supported; probe-verified
        // 2026-08-08: "a+?" vs "aaa" replaces to "---", "a{2,3}?" matches the
        // minimum first). "??" here is the lazy form of '?'.
        bool lazy = false;
        if (st->pos < st->length && st->pattern[st->pos] == u'?')
        {
            lazy = true;
            ++st->pos;
        }
        RegexNode* rep = NewNode(RegexNodeKind::Repeat);
        if (rep == nullptr)
        {
            return nullptr;
        }
        rep->min = min;
        rep->max = max;
        rep->lazy = lazy;
        rep->child = atom;
        return rep;
    }
    return atom;
}

// Returns nullptr ONLY on error; an empty sequence is a valid Empty node.
RegexNode* ParseSequence(ParseState* st)
{
    std::vector<RegexNode*> atoms;
    while (st->pos < st->length)
    {
        const char16_t c = st->pattern[st->pos];
        if (c == u'|' || c == u')')
        {
            break;
        }
        RegexNode* a = ParseQuantified(st);
        if (a == nullptr || st->error != RegexParseError::None)
        {
            for (RegexNode* owned : atoms)
            {
                FreeNode(owned);
            }
            return nullptr;
        }
        atoms.push_back(a);
    }

    if (atoms.empty())
    {
        return NewNode(RegexNodeKind::Empty);
    }
    if (atoms.size() == 1)
    {
        return atoms[0];
    }

    RegexNode* seq = NewNode(RegexNodeKind::Sequence);
    if (seq == nullptr)
    {
        for (RegexNode* owned : atoms)
        {
            FreeNode(owned);
        }
        return nullptr;
    }
    seq->children = static_cast<RegexNode**>(
        std::malloc(atoms.size() * sizeof(RegexNode*)));
    if (seq->children == nullptr)
    {
        for (RegexNode* owned : atoms)
        {
            FreeNode(owned);
        }
        delete seq;
        return nullptr;
    }
    seq->childCount = static_cast<int32_t>(atoms.size());
    std::memcpy(seq->children, atoms.data(), atoms.size() * sizeof(RegexNode*));
    return seq;
}

RegexNode* ParseAlternation(ParseState* st)
{
    std::vector<RegexNode*> branches;
    branches.push_back(ParseSequence(st));
    if (branches[0] == nullptr || st->error != RegexParseError::None)
    {
        return nullptr;
    }
    while (st->pos < st->length && st->pattern[st->pos] == u'|')
    {
        ++st->pos; // consume '|'
        RegexNode* b = ParseSequence(st);
        if (b == nullptr || st->error != RegexParseError::None)
        {
            for (RegexNode* owned : branches)
            {
                FreeNode(owned);
            }
            return nullptr;
        }
        branches.push_back(b);
    }

    if (branches.size() == 1)
    {
        return branches[0];
    }

    RegexNode* alt = NewNode(RegexNodeKind::Alternation);
    if (alt == nullptr)
    {
        for (RegexNode* owned : branches)
        {
            FreeNode(owned);
        }
        return nullptr;
    }
    alt->children = static_cast<RegexNode**>(
        std::malloc(branches.size() * sizeof(RegexNode*)));
    if (alt->children == nullptr)
    {
        for (RegexNode* owned : branches)
        {
            FreeNode(owned);
        }
        delete alt;
        return nullptr;
    }
    alt->childCount = static_cast<int32_t>(branches.size());
    std::memcpy(alt->children, branches.data(), branches.size() * sizeof(RegexNode*));
    return alt;
}

//------------------------------------------------------------------------------
// Node traversal + freeing (RegexAstFree)
//------------------------------------------------------------------------------

void FreeNode(RegexNode* n)
{
    if (n == nullptr)
    {
        return;
    }
    switch (n->kind)
    {
    case RegexNodeKind::Sequence:
    case RegexNodeKind::Alternation:
        for (int32_t i = 0; i < n->childCount; ++i)
        {
            FreeNode(n->children[i]);
        }
        std::free(n->children);
        break;
    case RegexNodeKind::Repeat:
    case RegexNodeKind::Group:
        FreeNode(n->child);
        break;
    case RegexNodeKind::Set:
        std::free(n->ranges);
        break;
    default:
        break;
    }
    delete n;
}

} // namespace

//------------------------------------------------------------------------------
// Public contract (regex_ast.h)
//------------------------------------------------------------------------------

RegexAst* RegexParse(const char16_t* pattern, int32_t length, int32_t options,
                     RegexParseError* outError)
{
    if (outError != nullptr)
    {
        *outError = RegexParseError::None;
    }
    if (pattern == nullptr || length < 0)
    {
        if (outError != nullptr)
        {
            *outError = RegexParseError::UnsupportedConstruct;
        }
        return nullptr;
    }
    if ((options & ~kRegexOptionsAllowedMask) != 0)
    {
        if (outError != nullptr)
        {
            *outError = RegexParseError::InvalidOptions;
        }
        return nullptr;
    }

    ParseState st;
    st.pattern = pattern;
    st.length = length;
    st.pos = 0;
    st.options = options;
    st.error = RegexParseError::None;
    st.nextGroup = 1;
    st.groupCount = CountCaptureGroups(pattern, length);

    RegexNode* root = ParseAlternation(&st);
    if (root == nullptr || st.error != RegexParseError::None)
    {
        FreeNode(root);
        if (outError != nullptr)
        {
            *outError = st.error;
        }
        return nullptr;
    }

    RegexAst* ast = new (std::nothrow) RegexAst();
    if (ast == nullptr)
    {
        FreeNode(root);
        return nullptr;
    }
    ast->root = root;
    ast->groupCount = st.nextGroup - 1;
    ast->options = options;
    return ast;
}

void RegexAstFree(RegexAst* ast)
{
    if (ast == nullptr)
    {
        return;
    }
    FreeNode(ast->root);
    delete ast;
}

} } // namespace il2cpp::runtime
