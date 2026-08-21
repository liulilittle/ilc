#pragma once
//------------------------------------------------------------------------------
// il2cpp_string_ext.h -- System.String INSTANCE-operation runtime semantics.
//
// This header extends the string CREATION primitives of il2cpp_string.h with
// the instance OPERATIONS of System.String (the M5 BCL string-domain icalls).
// Every function is an InternalCall surface for transpiled code: managed
// strings cross the boundary as Il2CppObject* (cast internally, matching the
// existing icall convention, e.g. icall_console_write_line_s in
// il2cpp_runtime.h) and string RESULTS come back as Il2CppObject*.
//
// .NET semantics implemented (all in the UTF-16 code-unit domain -- the
// unit count equals System.String.Length exactly):
//   Substring(start) / Substring(start, length)  -- bounds-checked
//   StartsWith / EndsWith / Contains             -- ArgumentNullException on null arg
//   IndexOf(char|string) / LastIndexOf(char)     -- -1 when absent
//   Replace(char, char)                          -- SAME instance when nothing changes
//   ToUpper / ToLower                            -- ASCII case mapping (see below)
//   Trim / TrimStart / TrimEnd                   -- .NET Char.IsWhiteSpace set
//   CompareTo(string)                            -- ORDINAL comparison (domain contract)
//
// Error semantics (thrown as the managed carrier, see il2cpp_exception.h):
//   null receiver  -> System.NullReferenceException
//   null argument  -> System.ArgumentNullException
//   bad index/len  -> System.ArgumentOutOfRangeException (via the helper below)
//
// Known deviations (documented, BCL-milestone scope):
//   * ToUpper/ToLower map ASCII only; non-ASCII units pass through unchanged
//     (full Unicode case mapping arrives with the globalization tasks).
//   * IndexOf(string)/Contains are ORDINAL searches (naive scan, O(n*m));
//     .NET Framework defaults to a culture-sensitive search -- the results
//     coincide for ASCII inputs.
//   * The .NET char type is one UTF-16 code unit; the C++ contract is
//     char16_t (surrogate pairs occupy two units, exactly like .NET).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Substring(int32_t startIndex): the substring from startIndex to the end.
// Bounds: 0 <= startIndex <= Length; anything else throws
// ArgumentOutOfRangeException. A startIndex == Length yields "".
// Identity (net48-verified 2026-08-08): startIndex == 0 returns the RECEIVER
// instance itself (ReferenceEquals) -- no allocation.
Il2CppObject* icall_string_substring(Il2CppObject* this_, int32_t startIndex);

// Substring(int32_t startIndex, int32_t length): 'length' units starting at
// startIndex. Bounds: 0 <= startIndex, 0 <= length, and
// startIndex + length <= Length (the sum is computed in 64-bit so an int32
// overflow can never bypass the check); anything else throws
// ArgumentOutOfRangeException. length == 0 yields "".
// Identity (net48-verified 2026-08-08): (startIndex == 0 && length == Length)
// returns the RECEIVER instance itself (ReferenceEquals) -- no allocation.
Il2CppObject* icall_string_substring_range(Il2CppObject* this_, int32_t startIndex, int32_t length);

// StartsWith(string): prefix test, ordinal. Null 'value' throws
// ArgumentNullException; the empty value matches any string (true).
bool icall_string_startswith(Il2CppObject* this_, Il2CppObject* value);

// EndsWith(string): suffix test, ordinal. Null 'value' throws
// ArgumentNullException; the empty value matches any string (true).
bool icall_string_endswith(Il2CppObject* this_, Il2CppObject* value);

// IndexOf(char): the index of the FIRST 'ch' occurrence, or -1 when absent
// (also for the empty string).
int32_t icall_string_index_of_c(Il2CppObject* this_, char16_t ch);

// IndexOf(string): the index of the first 'value' occurrence (ordinal
// search), or -1 when absent. Null 'value' throws ArgumentNullException;
// the empty value matches at index 0 (.NET semantics).
int32_t icall_string_index_of_s(Il2CppObject* this_, Il2CppObject* value);

// LastIndexOf(char): the index of the LAST 'ch' occurrence, or -1 when
// absent (also for the empty string).
int32_t icall_string_last_index_of_c(Il2CppObject* this_, char16_t ch);

// Replace(char, char): a new string with every 'oldChar' unit replaced by
// 'newChar'. Identity semantics follow the net48 BASELINE (empirically
// verified): when no occurrence exists, the SAME instance is returned
// (observable via ReferenceEquals); when oldChar == newChar and occurrences
// exist, a NEW content-equal instance is returned (.NET Framework 4.8 does
// not have .NET Core's oldChar == newChar identity fast path).
Il2CppObject* icall_string_replace(Il2CppObject* this_, char16_t oldChar, char16_t newChar);

// Replace(string, string): a new string with every NON-OVERLAPPING occurrence
// of 'oldValue' (greedy left-to-right scan, .NET semantics) replaced by
// 'newValue'; a null 'newValue' removes the occurrences (treated as the empty
// string). Null 'oldValue' throws ArgumentNullException (paramName
// "oldValue"); EMPTY 'oldValue' throws ArgumentException ("String cannot be
// of zero length.") -- both net48 csc-probe-verified (2026-08-09). When no
// occurrence exists the SAME instance is returned (net48 ReferenceEquals
// identity); when occurrences exist a NEW instance is ALWAYS returned, even
// when the replacement reproduces the original content (net48 has no
// oldValue == newValue identity fast path). GC safety: the receiver, oldValue
// and newValue payloads are staged into non-GC scratch BEFORE the first
// managed allocation, and the result is rooted until the fill completes.
Il2CppString* icall_string_replace_s_s(Il2CppString* self, Il2CppString* oldValue, Il2CppString* newValue);

// ToUpper(): ASCII upper-casing ('a'-'z' -> 'A'-'Z'; every other unit is
// copied unchanged). When nothing changes the SAME instance is returned.
Il2CppObject* icall_string_to_upper(Il2CppObject* this_);

// ToLower(): ASCII lower-casing ('A'-'Z' -> 'a'-'z'); symmetric contract to
// ToUpper (unchanged input returns the same instance).
Il2CppObject* icall_string_to_lower(Il2CppObject* this_);

// Trim(): a new string with leading AND trailing whitespace removed
// (whitespace = the .NET Char.IsWhiteSpace code-unit set, see stringext.cpp).
// All-whitespace input yields ""; nothing to trim returns the same instance.
Il2CppObject* icall_string_trim(Il2CppObject* this_);

// net9/10 E2E icallcase: Insert(int index, string value) / Remove(int
// startIndex, int count) -- pure UTF-16 unit surgery (stringext.cpp). Both
// return a NEW string; the source is never modified. Range violations throw
// ArgumentOutOfRangeException (net48 contract).
Il2CppString* icall_string_insert(Il2CppString* self, int32_t index, Il2CppString* value);
Il2CppString* icall_string_remove(Il2CppString* self, int32_t startIndex, int32_t count);
// Remove(int startIndex): removes through the END (stringext.cpp).
Il2CppString* icall_string_remove_to_end(Il2CppString* self, int32_t startIndex);

// net9/10 E2E campaign: CopyTo(srcIndex, dest, destIndex, count) -- copies
// 'count' UTF-16 units into the caller's char[] (stringext.cpp).
void icall_string_copy_to_array(Il2CppString* self, int32_t srcIndex,
                                Il2CppArray* dest, int32_t destIndex, int32_t count);

// net9/10 E2E campaign: IndexOfAny/LastIndexOfAny 1- and 2-arg forms --
// wrappers over the ext3 range icalls with default windows (stringext.cpp).
int32_t icall_string_index_of_any(Il2CppObject* self, Il2CppObject* anyOf);
int32_t icall_string_index_of_any_start(Il2CppObject* self, Il2CppObject* anyOf, int32_t startIndex);
int32_t icall_string_last_index_of_any(Il2CppObject* self, Il2CppObject* anyOf);
int32_t icall_string_last_index_of_any_start(Il2CppObject* self, Il2CppObject* anyOf, int32_t startIndex);

// net9/10 E2E campaign: Contains(char) -- ordinal single-unit scan
// (stringext.cpp); the net9 char overload.
bool icall_string_contains_char(Il2CppString* self, char16_t value);

// net9/10 E2E campaign (string sweep): ToCharArray() -- a NEW char[] copy of
// the UTF-16 units (stringext.cpp). Null self -> ArgumentNullException.
Il2CppArray* icall_string_to_char_array(Il2CppString* self);

// TrimStart(): leading whitespace only (contract as Trim).
Il2CppObject* icall_string_trim_start(Il2CppObject* this_);

// TrimEnd(): trailing whitespace only (contract as Trim).
Il2CppObject* icall_string_trim_end(Il2CppObject* this_);

// CompareTo(string): ORDINAL lexicographic comparison by UTF-16 code unit
// (domain contract; .NET Framework's CompareTo is culture-sensitive but the
// domain pins this icall to ordinal). Returns -1 when this < value, 0 when
// equal, +1 when this > value -- the INSTANCE CompareTo CLAMPS the raw
// unit/length difference to the sign (net48 behavior; the static
// string.CompareOrdinal / icall_string_compare_static reports the raw
// difference). Null 'value' returns 1 (net48: null sorts below any string).
int32_t icall_string_compare_to(Il2CppObject* this_, Il2CppObject* value);

// Contains(string): true when 'value' occurs in this string (ordinal
// search). Null 'value' throws ArgumentNullException; the empty value is
// contained in every string (true).
bool icall_string_contains(Il2CppObject* this_, Il2CppObject* value);

// Split(char[] separator): split this string on every occurrence of any
// character in 'separator' (a char[] SZARRAY crossing as Il2CppArray*; the
// element stride is 2 = char16_t). The default StringSplitOptions.None is
// assumed (net48): EMPTY segments are retained -- consecutive, leading and
// trailing separators each contribute an empty string to the result, and
// every segment is String.Empty (never null). A null OR EMPTY 'separator'
// array splits on WHITESPACE (the full .NET Char.IsWhiteSpace set -- the
// same code-unit set stringext.cpp's Trim uses; net48 treats
// Split((char[])null) and Split(new char[0]) as the whitespace overload).
// Result: a string[] (Il2CppArray* of Il2CppString* references) with
// exactly (number of separator code units) + 1 elements; an empty receiver
// yields ["\"\""] (one empty string). Segment count and content are both
// csc-probe-verified against net48 (2026-08-09). The receiver is the
// Il2CppString* self (the wired "s" cast); a null receiver throws
// NullReferenceException. GC safety: the receiver payload and separator
// units are staged into non-GC scratch BEFORE the first managed allocation
// (Gen0 compaction can move unrooted objects), and the result array is
// ROOTED for the whole fill (RootScope) so the per-segment string
// allocations can never reclaim it.
Il2CppArray* icall_string_split(Il2CppString* self, Il2CppArray* separators);

// Split(char[] separator, StringSplitOptions options) / Split(string[]
// separator, StringSplitOptions options) -- the 2-arg options forms (the
// strcomp2 / RT-SPL2 wiring, 2026-08-09). 'options' is the raw int32
// StringSplitOptions value: 0 = None, 1 = RemoveEmptyEntries; ANY other
// value throws System.ArgumentException (net48 csc-probe-verified).
//
// 'separators' crosses as Il2CppArray* and is DISAMBIGUATED by isRefElem:
//   * isRefElem == false (elemSize 2, the char[] overload): each code unit is
//     a single-unit separator -- the same per-unit semantics as
//     icall_string_split.
//   * isRefElem == true (elemSize sizeof(void*), the string[] overload): each
//     element is a multi-code-unit separator. Null and EMPTY elements are
//     SKIPPED (net48 probe: Split("abc", new string[] { null }) == ["abc"] and
//     Split("abc", new string[] { "" }) == ["abc"]). At every position the
//     separator that matches is the FIRST in array order (net48 reference
//     algorithm: String.SplitInternal checks separators in order and takes the
//     first match, advancing by its length -- the { "a","aa" }-vs-{ "aa","a" }
//     order probe pins this, len 4 vs 3 on "xaaay"), non-overlapping greedy.
//   * null OR length-0 array (either overload) -> the WHITESPACE overload
//     (the full Char.IsWhiteSpace set), exactly like the 1-arg split.
//
// RemoveEmptyEntries drops every EMPTY segment; None retains them. When the
// split of an all-separator / whitespace / empty receiver leaves no non-empty
// segment, the RESULT IS A 0-LENGTH string[] (net48: ",," RemoveEmpty -> [],
// "" RemoveEmpty -> [], "" None -> [""]).
//
// Segment content/length are csc-probe-verified against net48 (2026-08-09).
// GC safety mirrors icall_string_split: the receiver payload AND every
// separator unit (char and string-element alike) are staged into non-GC
// scratch BEFORE the first managed allocation, and the result array is ROOTED
// for the whole fill (RootScope) so the per-segment allocations can never
// reclaim it. A null receiver throws NullReferenceException.
Il2CppArray* icall_string_split_options(Il2CppString* self, Il2CppArray* separators, int32_t options);

//------------------------------------------------------------------------------
// Bounds-check helper: throw System.ArgumentOutOfRangeException with a
// stable diagnostic message. 'message' must be a static literal (the carrier
// never frees it). Declared here because the runtime has no shared
// ArgumentOutOfRangeException helper yet (il2cpp_exception.h is read-only
// for this domain).
//------------------------------------------------------------------------------
void il2cpp_throw_argument_out_of_range(const char* message);

} // namespace runtime
} // namespace il2cpp
