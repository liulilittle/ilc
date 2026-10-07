#pragma once
//------------------------------------------------------------------------------
// regex_ast.h -- Regex AST contract (System.Text.RegularExpressions basic domain).
//
// NOTE (fallback authorship): this header is the T1 deliverable per
// docs/design/regex-domain.md section 5 (T1 lexer + parser, parallel-agent task). The
// parallel T1 agent had NOT produced it when T2 started (glob for
// src/runtime/**/regex* matched nothing), so T2 defined the contract itself
// following the design doc section 2.2 field-for-field, plus the minimal parser in
// regex_parse.cpp to keep the matcher testable. If the T1 agent lands its own
// regex_ast.h later, its contract may differ -- reconcile before T3.
//
// Ownership: the parser arena-allocates the whole tree; T2 only reads it;
// RegexAstFree() releases it in one call (RAII wrapper on the C++ side).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) per docs/design/regex-domain.md
// section 1. Documented divergences:
//   * Parse errors are SIGNALLED BY RETURN VALUE (nullptr + error code), not
//     by throwing ArgumentException -- this layer never throws; the T3 API
//     layer translates RegexParseError into the managed exception.
//   * Options outside the whitelist {IgnoreCase=1, Multiline=2, Singleline=4}
//     are rejected at parse time with InvalidOptions (net48 would throw
//     NotSupported/Argument at construction; loud failure either way).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// RegexOptions bit subset accepted at parse time (docs section 1.2).
constexpr int32_t kRegexOptionsIgnoreCase  = 0x01;
constexpr int32_t kRegexOptionsMultiline   = 0x02;
constexpr int32_t kRegexOptionsSingleline  = 0x04;
constexpr int32_t kRegexOptionsAllowedMask =
    kRegexOptionsIgnoreCase | kRegexOptionsMultiline | kRegexOptionsSingleline;

//------------------------------------------------------------------------------
// AST contract (docs section 2.2 -- field-for-field)
//------------------------------------------------------------------------------

enum class RegexNodeKind {
    Empty,       // empty sequence (empty pattern "")
    Char,        // a single literal character (folded under IgnoreCase)
    Any,         // '.' : any character except \n; includes \n under Singleline
    Set,         // character class [...] (including negation and \d\w\s expansion)
    Sequence,    // ordered list of child nodes
    Alternation, // list of alternatives (leftmost-first)
    Repeat,      // quantifier: child repeated [min,max] (lazy when lazy==true)
    Group,       // capture group: child + groupIndex (1-based; 0 = the whole match)
    Anchor,      // ^ $ (Start/End/LineStart/LineEnd)
    Backref,     // \N : match the current captured text of group N (unset group -> fail)
};

enum class AnchorKind {
    Start,      // ^ without Multiline: start of the string only
    End,        // $ without Multiline: end of the string only
    LineStart,  // ^ with Multiline: start of the string or after \n
    LineEnd,    // $ with Multiline: end of the string or before \n
};

struct RegexRange {
    char16_t lo; // inclusive endpoint
    char16_t hi;
};

struct RegexNode {
    RegexNodeKind kind;
    char16_t     ch;          // Char
    bool         ignoreCase;  // Char/Set folded under IgnoreCase (ASCII)
    bool         negate;      // Set
    RegexRange*  ranges;      // Set: [lo,hi] inclusive, sorted and non-overlapping
    int32_t      rangeCount;
    RegexNode*   child;       // Repeat / Group
    RegexNode**  children;    // Sequence / Alternation
    int32_t      childCount;
    int32_t      min, max;    // Repeat: max=-1 = infinity; {n} = {n,n}
    int32_t      groupIndex;  // Group
    AnchorKind   anchor;      // Anchor
    int32_t      backrefGroup; // Backref: referenced capture-group number (1-based)
    bool         lazy;        // Repeat: lazy quantifier (a*? a+? a?? a{n,m}?)
};

struct RegexAst {
    RegexNode* root;
    int32_t    groupCount;    // number of capture groups (max groupIndex; 0 = none)
    int32_t    options;       // validated option bits (kRegexOptions*)
};

//------------------------------------------------------------------------------
// Parser contract (T1 fallback)
//------------------------------------------------------------------------------

enum class RegexParseError {
    None,
    InvalidOptions,        // options contain bits outside the whitelist
    UnclosedGroup,         // unclosed '('
    UnclosedClass,         // unclosed '['
    InvalidClassRange,     // illegal ranges like [z-a], [a-b-c]
    UnrecognizedEscape,    // unrecognized escape like \q
    QuantifierNothing,     // quantifier with no preceding atom (leading * / {2})
    InvalidRepeat,         // illegal range {2,1} / {n} out of bounds / unclosed {n,m}
    LazyQuantifier,        // legacy: lazy-quantifier suffix ? (section 3.4 cut, now SUPPORTED)
    UnsupportedConstruct,  // (?: (?= (?< (?> (?i) \b \x \u \p{unknown-category}
                           // etc., outside the section 3.4 whitelist; \p{L}/\p{N}/
                           // \p{P}/\p{Lu}/\p{Ll} ARE supported (D-REG-UNI-1)
};

// Parse a pattern. Returns a heap RegexAst on success (caller must
// RegexAstFree it); returns nullptr on failure and stores the error code in
// *outError when non-null. options bits outside kRegexOptionsAllowedMask are
// rejected (InvalidOptions). The parser NEVER throws.
RegexAst* RegexParse(const char16_t* pattern, int32_t length, int32_t options,
                     RegexParseError* outError = nullptr);

// Free a tree returned by RegexParse (nullptr-safe).
void RegexAstFree(RegexAst* ast);

} } // namespace il2cpp::runtime
