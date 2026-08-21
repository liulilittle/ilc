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

namespace il2cpp { namespace runtime {

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

}} // namespace il2cpp::runtime
