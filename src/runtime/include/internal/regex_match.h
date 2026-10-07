#pragma once
//------------------------------------------------------------------------------
// regex_match.h -- Regex matching engine entry point (T2 deliverable).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), verified by probe runs on
// 2026-08-04 (see tests/runtime/test_regex_match.cpp for the exact matrix).
// Where the design doc docs/design/regex-domain.md conflicted with net48
// behavior, the net48 probe result WINS -- the doc is corrected by T2 in
// discover.md:
//   * Empty character classes "[]" / "[^]" THROW ArgumentException at parse
//     time (doc section 3.2 claimed "never matches" / "any char" -- probe: THROW).
//   * "$" (non-Multiline) matches end-of-string OR immediately BEFORE a
//     trailing '\n' ("a$" matches "a\n") -- doc section 3.1 claimed end-of-string
//     only (probe: matches the trailing newline position).
//   * "[a-b-c]" is legal: range a-b, then literal '-' and 'c'.
//   * Unclosed "{n" / "{n,3" are LITERALS ("a{2" matches "a{2"), only a
//     well-formed-but-invalid repeat ({2,1}, numeric overflow) throws.
//   * "a{2}" after nothing / nested quantifiers ("a**") throw.
//
// Engine contract (docs section 2.3): recursive backtracker over the RegexAst tree,
// leftmost-first alternation, greedy repeats with decreasing backtracking,
// capture groups with rollback (captureDepth snapshot + truncate), anchors,
// character classes with ASCII \d \w \s, and a step budget guard that turns
// catastrophic backtracking into a deterministic no-match (docs section 2.4) instead
// of hanging like net48 does.
//------------------------------------------------------------------------------

#include "regex_ast.h"

#include <cstdint>
#include <vector>

namespace il2cpp { namespace runtime {

// Step budget: every MatchNode invocation costs one step; exceeding the
// budget turns the whole call into a no-match (docs section 2.4). net48 with its
// default infinite timeout HANGS on catastrophic patterns (probe: "(a*)*b"
// vs 50 'a' never returned); we fail deterministically instead.
constexpr int64_t kRegexMaxMatchSteps = 10000000;

struct RegexGroupCapture {
    int32_t start;  // -1 = group did not participate
    int32_t length; // 0 when start == -1
};

struct MatchResult {
    bool         success;       // false = no match (or budget exhausted)
    int32_t      start;         // overall match start; -1 when !success
    int32_t      matchedLength; // overall match length; 0 when !success
    // Capture groups: index 0 = group 1, ... index groupCount-1 = group
    // groupCount (RegexAst::groupCount). A group that never participated has
    // start == -1 (net48 Match.Groups[i].Success == false equivalent).
    // A group that participated in several iterations holds the LAST one
    // (net48 semantics, e.g. "(ab)+" vs "ababab" -> group1 = "ab").
    std::vector<RegexGroupCapture> groups;
};

// Match 'input' (UTF-16 units, length in units) against a parsed pattern.
// The AST is read-only and must outlive this call. options were validated at
// parse time and live in the AST. Returns the overall match in *outResult
// (success = true) or sets success = false. outResult may be null (boolean
// probe only). Never throws; budget exhaustion is reported as success=false.
bool il2cpp_regex_match(const RegexAst* pattern, const char16_t* input,
                        int32_t inputLength, MatchResult* outResult);

// Base-aware variant: 'input' is a slice of a longer text whose absolute
// start is 'baseOffset' (the anchors see absolute positions -- the Collect
// suffix scan contract). il2cpp_regex_match == RegexMatchAt(..., 0, ...).
bool RegexMatchAt(const RegexAst* pattern, const char16_t* input,
                  int32_t inputLength, int32_t baseOffset,
                  MatchResult* outResult);

} } // namespace il2cpp::runtime
