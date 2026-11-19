#pragma once
//------------------------------------------------------------------------------
// il2cpp_char.h -- System.Char STATIC surface runtime support (D-CHA-2).
//
// The transpiler maps System.Char static methods to these runtime helpers
// (MethodMap.Char.cs). Like the math domain, Char members have no IL body of
// their own: they are [MethodImplOptions.InternalCall]-style intrinsics
// delegating to character-classification / case-conversion logic in C++.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) probes pin the ASCII domain
// (IsDigit('5')/IsLetter('a')/ToUpper('a')='A' -- charstr GAPS.md D-CHA-2):
//   * IsDigit/IsLetter/IsLetterOrDigit/IsLower/IsUpper: the ASCII subset of
//     the net48 Unicode-category tests (U+0030-U+0039 digits, U+0041-U+005A /
//     U+0061-U+007A letters). Code units outside the ASCII ranges return
//     false -- the documented ASCII-domain divergence (the full Unicode
//     category table is out of scope; the zh-CN surface is recorded as a
//     future expansion, not probed).
//   * IsWhiteSpace: the EXACT .NET code-unit set (control whitespace + space
//     + the Unicode Zs/Zl/Zp spaces) -- the same literal list IsWhitespace
//     uses in stringext.cpp (Trim/Contains are exercised against it).
//   * ToUpper/ToLower/ToUpperInvariant/ToLowerInvariant: ASCII case
//     conversion of a single code unit ('a'-'z' -> 'A'-'Z' and the reverse),
//     identical to the unit mapping icall_string_to_upper/to_lower use
//     (stringext.cpp). net48 char.ToUpper/char.ToLower are culture-sensitive;
//     the runtime pins en-US (documented culture divergence), where the ASCII
//     result equals the invariant form -- all four C# methods map to the same
//     two unit functions.
//
// ABI: char values cross the boundary as char16_t (the icall_stringbuilder_
// append_c precedent); the transpiler's "c" arg cast produces
// static_cast<char16_t>, and the char-typed icall RESULTS widen exactly into
// the int32 value slots the emitter allocates (the bitconverter ToChar
// precedent -- null ResultKind).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp {
struct Il2CppObject; // forward: the (string, int) overload family below

namespace runtime {

// char.IsDigit(char) -- ASCII digit (U+0030-U+0039).
bool icall_char_is_digit(char16_t c);

// char.IsLetter(char) -- ASCII letter (U+0041-U+005A, U+0061-U+007A).
bool icall_char_is_letter(char16_t c);

// char.IsLetterOrDigit(char) -- ASCII letter OR ASCII digit.
bool icall_char_is_letter_or_digit(char16_t c);

// char.IsLower(char) -- ASCII lowercase letter (U+0061-U+007A).
bool icall_char_is_lower(char16_t c);

// char.IsUpper(char) -- ASCII uppercase letter (U+0041-U+005A).
bool icall_char_is_upper(char16_t c);

// char.IsWhiteSpace(char) -- the exact .NET whitespace code-unit set.
bool icall_char_is_whitespace(char16_t c);

// char.ToUpper(char) / char.ToUpperInvariant(char) -- ASCII upper-case one
// code unit ('a'-'z' -> 'A'-'Z', others unchanged).
char16_t icall_char_to_upper(char16_t c);

// char.ToLower(char) / char.ToLowerInvariant(char) -- ASCII lower-case one
// code unit ('A'-'Z' -> 'a'-'z', others unchanged).
    char16_t icall_char_to_lower(char16_t c);

// The ASCII-domain classification family (icallwide E2E; see char.cpp for
// the exact-on-ASCII / conservative-false-elsewhere contract).
bool icall_char_is_control(char16_t c);
bool icall_char_is_punctuation(char16_t c);
bool icall_char_is_surrogate(char16_t c);
bool icall_char_is_high_surrogate(char16_t c);
bool icall_char_is_low_surrogate(char16_t c);
bool icall_char_is_separator(char16_t c);
bool icall_char_is_symbol(char16_t c);
double icall_char_get_numeric_value(char16_t c);

// The (string, int) overload family. net48 declares a SECOND set of the same
// classification methods that index into a string instead of taking one char:
// char.IsDigit(s, i), char.IsLetter(s, i), ... Only the single-char overloads
// were wired, so every call here failed to transpile with "no BCL mapping in
// M5" -- a whole-method loss, since the BCL body is not in the input assembly.
//
// The contract is exactly "classify s[index]": the same predicate the char
// overload answers, applied to the selected code unit. The index is bounds
// checked against the string length and throws
// ArgumentOutOfRangeException when out of range (net48 behaviour); a null
// string throws NullReferenceException. Each entry is a thin adapter over the
// char overload above, so the two families can never disagree.
//
// The UnicodeCategory return is the enum's NUMERIC value (contract pinned by
// the probe), matching the int32 ABI slot the emitter allocates.
bool icall_char_s_is_digit(Il2CppObject* s, int32_t index);
bool icall_char_s_is_letter(Il2CppObject* s, int32_t index);
bool icall_char_s_is_upper(Il2CppObject* s, int32_t index);
bool icall_char_s_is_lower(Il2CppObject* s, int32_t index);
bool icall_char_s_is_whitespace(Il2CppObject* s, int32_t index);
bool icall_char_s_is_punctuation(Il2CppObject* s, int32_t index);
bool icall_char_s_is_symbol(Il2CppObject* s, int32_t index);
bool icall_char_s_is_separator(Il2CppObject* s, int32_t index);
bool icall_char_s_is_control(Il2CppObject* s, int32_t index);
bool icall_char_s_is_surrogate(Il2CppObject* s, int32_t index);
bool icall_char_s_is_high_surrogate(Il2CppObject* s, int32_t index);
bool icall_char_s_is_low_surrogate(Il2CppObject* s, int32_t index);
bool icall_char_s_is_number(Il2CppObject* s, int32_t index);
// char.IsSurrogatePair(char, char): true when the FIRST unit is a high
// surrogate and the second a low one (net48: 0xD800-0xDBFF followed by
// 0xDC00-0xDFFF). net48 also declares (string, int), covered by
// icall_char_s_is_surrogate_pair below.
bool icall_char_is_surrogate_pair(char16_t high, char16_t low);
bool icall_char_s_is_surrogate_pair(Il2CppObject* s, int32_t index);
int32_t icall_char_s_get_unicode_category(Il2CppObject* s, int32_t index);
double icall_char_s_get_numeric_value(Il2CppObject* s, int32_t index);

}} // namespace il2cpp::runtime
