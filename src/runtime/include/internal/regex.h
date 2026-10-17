#pragma once
//------------------------------------------------------------------------------
// regex.h -- System.Text.RegularExpressions string-API surface (basic domain).
//
// Four C entry points mirroring the STATIC Regex overloads wired by the
// transpiler (docs/design/regex-domain.md section 4): IsMatch / Match / Replace /
// Split. The parameter ORDER mirrors the .NET static signatures exactly --
// input first, pattern second (net48: IsMatch(string input, string pattern))
// -- because the transpiler emits arguments positionally in IL order (the M5
// emitter has no arg-reordering facility; see MethodMap.Buffer.cs's MemoryCopy
// note). Runtime-only trailing parameters (options, Match's outStart/
// outLength) default, so a 2-arg .NET call maps to a plain 2-arg C call.
// Strings cross the boundary as managed references (Il2CppString*, layout
// il2cpp_string.h:39-44); the match result is a string[] of managed
// references (Il2CppArray* of Il2CppString*, element 0 = whole match).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline -- with documented simplifications (regex-domain.md section 1.4):
//
//   * Match result shape: string[] [0]=whole match, [1..n]=capture groups;
//     a group that did not participate is a NULL element (net48
//     Match.Groups[i].Success == false); no Match/Group object graph.
//   * Split ignores capture groups (net48 inserts them; v1 does not).
//   * IgnoreCase folds ASCII only; \d \w \s are ASCII sets; no timeout
//     (internal step budget yields no-match instead of hanging).
//   * Replacement tokens: $0-$9 (group refs; group 0 = whole match), $$
//     (literal '$'), two-digit $nn (group nn when it exists, else first
//     digit group + literal second digit); any other '$' sequence is kept
//     literally.
//   * RegexOptions subset: IgnoreCase=1 | Multiline=2 | Singleline=0x10
//     (the REAL net48 enum numbering -- .NET Singleline is 16, not 4; see
//     regex_ast.h). Any other bit throws NotSupportedException (loud,
//     section 1.2).
//
// ERROR SEMANTICS (net48-aligned, regex-domain.md section 4.3): null pattern /
// input / replacement -> ArgumentNullException; syntactically invalid
// pattern -> ArgumentException; step-budget exhaustion -> no-match (never
// throws). Exception helpers: il2cpp_exception_ext.h.
//
// MEMORY/THREADING: each call parses and matches independently -- no shared
// state, safe to call from multiple threads without locking (D13). Returned
// strings/arrays are GC objects; the caller keeps them reachable.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// 1 = a match was found; 0 = none.
// Invalid pattern -> ArgumentException; null pattern/input ->
// ArgumentNullException; options outside the whitelist -> NotSupportedException.
int32_t il2cpp_regex_is_match(Il2CppString* input, Il2CppString* pattern,
                              int32_t options = 0);

// Returns string[]: [0] = whole match, [1..n] = capture-group strings
// (null element when the group did not participate). No match -> EMPTY
// array (length 0) and *outStart = *outLength = -1. outStart / outLength
// may be null (caller does not need the position).
Il2CppArray* il2cpp_regex_match(Il2CppString* input, Il2CppString* pattern,
                                int32_t options = 0,
                                int32_t* outStart = nullptr,
                                int32_t* outLength = nullptr);

// Returns the input with every match replaced by the (token-expanded)
// replacement text (net48 static Replace semantics, all matches).
Il2CppString* il2cpp_regex_replace(Il2CppString* input, Il2CppString* pattern,
                                   Il2CppString* replacement,
                                   int32_t options = 0);

// Returns string[]: input split at every match (delimiter text excluded,
// capture groups ignored -- section 1.4-2). No match -> single-element array
// [original input].
Il2CppArray* il2cpp_regex_split(Il2CppString* input, Il2CppString* pattern,
                                int32_t options = 0);

}} // namespace il2cpp::runtime
