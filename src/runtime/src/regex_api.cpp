//------------------------------------------------------------------------------
// regex_api.cpp -- System.Text.RegularExpressions string-API implementation.
//
// T3 card (docs/design/regex-domain.md section 5): the four C entry points of
// regex.h. PARAMETER ORDER: input first, pattern second -- the .NET static
// signatures (net48 IsMatch(string input, string pattern)); regex.h's
// pattern-first draft order was corrected on 2026-08-04 when the transpiler
// wiring surfaced that the M5 emitter emits arguments positionally in IL
// order with no reordering facility (MethodMap.Buffer.cs MemoryCopy note).
// Pipeline per call: validate args -> copy text to C++ buffers ->
// RegexParse (T1, regex_ast.h) -> delegate matching to the T2 engine
// (regex_match.h) -> build the managed result (GC allocations rooted per
// linq_map.cpp:142-171).
//
// T2 ENGINE CONTRACT (regex_match.h, landed 2026-08-04):
//   bool il2cpp_regex_match(const RegexAst*, const char16_t* input,
//                           int32_t inputLength, MatchResult* outResult);
//   struct MatchResult { bool success; int32_t start; int32_t matchedLength;
//                        std::vector<RegexGroupCapture> groups; }
//   -- leftmost match via bumpalong over 0..inputLength; groups[i] = capture
//   of group i+1 (start -1 = not participating); NEVER throws.
//   NOTE: the engine entry shares the flat name il2cpp_regex_match with the
//   T3 API (regex.h) -- legal C++ overloading (4 vs 5 params, distinct
//   param types); every call site below resolves unambiguously.
//
// ITERATION (Replace/Split, docs section 2.5): the engine searches a WHOLE input
// with no start position, so iteration calls it on SUFFIX slices (input+pos,
// len-pos) and offsets the results by pos. Slice calls re-expose absolute
// position 0 as a candidate, which start-anchored patterns (^) would match
// falsely; a precomputed AST walk detects start-anchored patterns and the
// loop validates slice-0 matches against the anchor's real positions
// (string start, or after '\n' with Multiline) -- phantoms are skipped by
// advancing the search one unit.
//
// MEMORY/THREADING: every call parses and matches independently -- no shared
// state (D13 lock-free). The GC compacts the young generation, so raw chars
// pointers must never survive a GC allocation: pattern/input/replacement are
// deep-copied into std::u16string at entry, and result arrays are registered
// as GC roots while their element strings are allocated.
//------------------------------------------------------------------------------

#include "regex.h"
#include "regex_ast.h"
#include "regex_match.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <string>
#include <utility>
#include <vector>

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Text copies (GC-immune, see header note)
//------------------------------------------------------------------------------

std::u16string ToText(Il2CppString* s)
{
    return std::u16string(s->chars, static_cast<size_t>(s->length));
}

//------------------------------------------------------------------------------
// Managed-object safety (linq_map.cpp:142-171 precedent): registered root
// slots are rewritten by the collector when objects move; RAII removal so a
// throwing allocation cannot leak a root slot.
//------------------------------------------------------------------------------
class RootScope
{
public:
    RootScope() : m_count(0) {}
    ~RootScope()
    {
        while (m_count > 0)
        {
            il2cpp_gc_remove_static_root(m_slots[m_count - 1]);
            --m_count;
        }
    }
    void Add(void** slot)
    {
        il2cpp_gc_add_static_root(slot);
        m_slots[m_count++] = slot;
    }

private:
    void** m_slots[4]; // max 2 used today (the result array per call)
    int32_t m_count;
};

//------------------------------------------------------------------------------
// Argument / option validation (regex.h error semantics)
//------------------------------------------------------------------------------

void RequireString(Il2CppString* s, const char* paramName)
{
    if (s == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
}

void RequireOptions(int32_t options)
{
    if ((options & ~kRegexOptionsAllowedMask) != 0)
    {
        il2cpp_throw_not_supported_exception(
            "RegexOptions bits outside the supported subset are rejected.");
    }
}

// Parse; on failure throws ArgumentException (or NotSupportedException for
// option bits -- defensive, RequireOptions already rejects them) and never
// returns.
RegexAst* ParsePattern(const std::u16string& pattern, int32_t options)
{
    RegexParseError error = RegexParseError::None;
    RegexAst* ast = RegexParse(pattern.data(),
                               static_cast<int32_t>(pattern.size()),
                               options, &error);
    if (ast != nullptr)
    {
        return ast;
    }
    if (error == RegexParseError::InvalidOptions)
    {
        il2cpp_throw_not_supported_exception(
            "RegexOptions bits outside the supported subset are rejected.");
    }
    il2cpp_throw_argument_exception("Regex pattern is invalid.");
    return nullptr;
}

// RAII release of the parse arena (RegexAstFree is nullptr-safe).
class AstScope
{
public:
    explicit AstScope(RegexAst* ast) : m_ast(ast) {}
    ~AstScope() { RegexAstFree(m_ast); }
    RegexAst* Get() const { return m_ast; }

private:
    RegexAst* m_ast;
};

//------------------------------------------------------------------------------
// Match iteration (docs section 2.5)
//------------------------------------------------------------------------------

// One match in ABSOLUTE input coordinates, with its group captures
// (groups[i] = group i+1; start -1 = not participating).
struct MatchSpan
{
    int32_t start;
    int32_t length;
    std::vector<RegexGroupCapture> groups;
};

// Collect every match in input[0..textLen) per docs section 2.5: non-overlapping
// (next search from the match end), empty matches advance the search one
// unit (no dead loop). Anchor positions are ABSOLUTE (RegexMatchAt), so the
// suffix scan cannot re-anchor and no phantom filter is needed.
void CollectMatches(const RegexAst* ast, const char16_t* text, int32_t textLen,
                    std::vector<MatchSpan>& spans)
{
    int32_t pos = 0;
    for (;;)
    {
        MatchResult m;
        const bool ok = RegexMatchAt(ast, text + pos, textLen - pos, pos, &m);
        if (!ok || !m.success)
        {
            break;
        }
        const int32_t absStart = pos + m.start;
        MatchSpan span;
        span.start = absStart;
        span.length = m.matchedLength;
        span.groups.swap(m.groups);
        for (size_t i = 0; i < span.groups.size(); ++i)
        {
            if (span.groups[i].start >= 0)
            {
                span.groups[i].start += pos;
            }
        }
        spans.push_back(std::move(span));
        if (m.matchedLength > 0)
        {
            pos = absStart + m.matchedLength;
        }
        else
        {
            pos = absStart + 1; // empty-match advance (docs section 2.5)
        }
        if (pos > textLen)
        {
            break; // defensive: the engine reports no-match past the end
        }
    }
}

//------------------------------------------------------------------------------
// Replacement token expansion (regex-domain.md section 1.4-6)
//------------------------------------------------------------------------------

// Append the text of group 'group' (0 = whole match) when it participated;
// a non-participating group expands to nothing (net48 semantics).
void AppendGroup(std::u16string& out, const char16_t* input,
                 const MatchSpan& span, int32_t group)
{
    int32_t gstart;
    int32_t glength;
    if (group == 0)
    {
        gstart = span.start;
        glength = span.length;
    }
    else
    {
        const RegexGroupCapture& g = span.groups[group - 1];
        gstart = g.start;
        glength = g.length;
    }
    if (gstart < 0)
    {
        return;
    }
    out.append(input + gstart, static_cast<size_t>(glength));
}

// Expand rep[0..repLen) per net48 ParseReplacement ($0-$9 group refs, $$
// literal '$', two-digit $nn = group nn when it exists else first digit
// group + literal second digit, any other '$' sequence kept literally).
void AppendReplacement(std::u16string& out, const char16_t* input,
                       const char16_t* rep, int32_t repLen,
                       const MatchSpan& span, int32_t groupCount)
{
    for (int32_t i = 0; i < repLen; ++i)
    {
        const char16_t c = rep[i];
        if (c != u'$')
        {
            out.push_back(c);
            continue;
        }
        if (i + 1 >= repLen)
        {
            out.push_back(u'$'); // trailing '$' is literal
            break;
        }
        const char16_t next = rep[i + 1];
        if (next == u'$')
        {
            out.push_back(u'$');
            ++i;
            continue;
        }
        if (next >= u'0' && next <= u'9')
        {
            const int32_t first = next - u'0';
            const bool twoDigits = (i + 2 < repLen) &&
                                   rep[i + 2] >= u'0' && rep[i + 2] <= u'9';
            if (twoDigits)
            {
                const int32_t num = first * 10 + (rep[i + 2] - u'0');
                if (num <= groupCount)
                {
                    AppendGroup(out, input, span, num);
                }
                else
                {
                    AppendGroup(out, input, span, first);
                    out.push_back(rep[i + 2]);
                }
                i += 2;
                continue;
            }
            AppendGroup(out, input, span, first);
            ++i;
            continue;
        }
        out.push_back(u'$'); // '$x': '$' literal, 'x' handled next iteration
    }
}

} // namespace

//------------------------------------------------------------------------------
// IsMatch
//------------------------------------------------------------------------------
int32_t il2cpp_regex_is_match(Il2CppString* input, Il2CppString* pattern,
                              int32_t options)
{
    RequireString(pattern, "pattern");
    RequireString(input, "input");
    RequireOptions(options);
    const std::u16string p = ToText(pattern);
    const std::u16string t = ToText(input);
    AstScope ast(ParsePattern(p, options));
    MatchResult m;
    const bool ok = il2cpp_regex_match(ast.Get(), t.data(),
                                       static_cast<int32_t>(t.size()), &m);
    return ok && m.success ? 1 : 0;
}

//------------------------------------------------------------------------------
// Match
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_regex_match(Il2CppString* input, Il2CppString* pattern,
                                int32_t options,
                                int32_t* outStart, int32_t* outLength)
{
    RequireString(pattern, "pattern");
    RequireString(input, "input");
    RequireOptions(options);
    const std::u16string p = ToText(pattern);
    const std::u16string t = ToText(input);
    AstScope ast(ParsePattern(p, options));

    const int32_t groupSlots = ast.Get()->groupCount + 1; // group 0 = whole
    MatchResult m;
    const bool ok = il2cpp_regex_match(ast.Get(), t.data(),
                                       static_cast<int32_t>(t.size()), &m);
    if (!ok || !m.success)
    {
        if (outStart != nullptr) *outStart = -1;
        if (outLength != nullptr) *outLength = -1;
        return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)),
                                true, 0);
    }
    if (outStart != nullptr) *outStart = m.start;
    if (outLength != nullptr) *outLength = m.matchedLength;

    Il2CppArray* arr = il2cpp_array_new(nullptr,
                                        static_cast<int32_t>(sizeof(void*)),
                                        true, groupSlots);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&arr));
    for (int32_t i = 0; i < groupSlots; ++i)
    {
        Il2CppString* element = nullptr; // non-participating group -> null
        if (i == 0)
        {
            element = il2cpp_string_new(t.data() + m.start, m.matchedLength);
        }
        else if (m.groups[i - 1].start >= 0)
        {
            const RegexGroupCapture& g = m.groups[i - 1];
            element = il2cpp_string_new(t.data() + g.start, g.length);
        }
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(arr, i));
        *slot = reinterpret_cast<Il2CppObject*>(element);
    }
    return arr;
}

//------------------------------------------------------------------------------
// Replace
//------------------------------------------------------------------------------
Il2CppString* il2cpp_regex_replace(Il2CppString* input, Il2CppString* pattern,
                                   Il2CppString* replacement, int32_t options)
{
    RequireString(pattern, "pattern");
    RequireString(input, "input");
    RequireString(replacement, "replacement");
    RequireOptions(options);
    const std::u16string p = ToText(pattern);
    const std::u16string t = ToText(input);
    const std::u16string r = ToText(replacement);
    AstScope ast(ParsePattern(p, options));

    std::vector<MatchSpan> spans;
    CollectMatches(ast.Get(), t.data(), static_cast<int32_t>(t.size()), spans);

    const char16_t* text = t.data();
    std::u16string out;
    int32_t prev = 0;
    for (size_t i = 0; i < spans.size(); ++i)
    {
        const MatchSpan& span = spans[i];
        out.append(text + prev, static_cast<size_t>(span.start - prev));
        AppendReplacement(out, text, r.data(), static_cast<int32_t>(r.size()),
                          span, ast.Get()->groupCount);
        prev = span.start + span.length;
    }
    out.append(text + prev, static_cast<size_t>(t.size() - prev));
    return il2cpp_string_new(out.data(), static_cast<int32_t>(out.size()));
}

//------------------------------------------------------------------------------
// Split
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_regex_split(Il2CppString* input, Il2CppString* pattern,
                                int32_t options)
{
    RequireString(pattern, "pattern");
    RequireString(input, "input");
    RequireOptions(options);
    const std::u16string p = ToText(pattern);
    const std::u16string t = ToText(input);
    AstScope ast(ParsePattern(p, options));

    std::vector<MatchSpan> spans;
    CollectMatches(ast.Get(), t.data(), static_cast<int32_t>(t.size()), spans);

    const char16_t* text = t.data();
    const int32_t textLen = static_cast<int32_t>(t.size());
    std::vector<std::u16string> pieces;
    int32_t prev = 0;
    for (size_t i = 0; i < spans.size(); ++i)
    {
        const MatchSpan& span = spans[i];
        pieces.push_back(std::u16string(text + prev,
                                        static_cast<size_t>(span.start - prev)));
        prev = span.start + span.length;
    }
    pieces.push_back(std::u16string(text + prev,
                                    static_cast<size_t>(textLen - prev)));

    Il2CppArray* arr = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true,
        static_cast<int32_t>(pieces.size()));
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&arr));
    for (size_t i = 0; i < pieces.size(); ++i)
    {
        const std::u16string& piece = pieces[i];
        Il2CppString* element = il2cpp_string_new(
            piece.data(), static_cast<int32_t>(piece.size()));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(arr, static_cast<int32_t>(i)));
        *slot = reinterpret_cast<Il2CppObject*>(element);
    }
    return arr;
}

}} // namespace il2cpp::runtime
