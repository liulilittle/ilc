#pragma once
//------------------------------------------------------------------------------
// il2cpp_string_ext3.h -- System.String positional-search and comparison-mode
// icalls (the net48 overload family the M5 string domains left unwired).
//
// The instance icalls live in il2cpp_string_ext.h / stringext.cpp and the
// static methods in il2cpp_string_ext2.h / stringext2.cpp; this header adds
// the OVERLOAD family .NET Framework 4.8 exposes for the search and prefix
// tests that carry an explicit position and/or a comparison mode:
//
//   IndexOf(char, int32_t)                       -- "from" variants
//   IndexOf(char, int32_t, int32_t)              -- "range" variants
//   IndexOf(string, int32_t)
//   IndexOf(string, int32_t, int32_t)
//   LastIndexOf(char, int32_t)
//   LastIndexOf(char, int32_t, int32_t)
//   LastIndexOf(string)
//   LastIndexOf(string, int32_t)
//   LastIndexOf(string, int32_t, int32_t)
//   StartsWith(string, StringComparison)         -- comparison-mode variants
//   EndsWith(string, StringComparison)
//   PadLeft(int32_t) / PadLeft(int32_t, char16_t)
//   PadRight(int32_t) / PadRight(int32_t, char16_t)
//
// The single-position forms already covered elsewhere (IndexOf(char),
// IndexOf(string), LastIndexOf(char), StartsWith(string), EndsWith(string))
// are NOT re-declared -- generated code and tests keep calling the existing
// icalls (no duplicate symbols).
//
// SEMANTIC BASELINE: .NET Framework 4.8, empirically verified against the
// reference runtime (csc v4.0.30319 oracle; edge values quoted below). All
// comparisons are ORDINAL on UTF-16 code units -- a plain 16-bit-unit
// comparison, never a host-locale function (wcscmp / strcoll are FORBIDDEN
// here: the runtime must not depend on the host C++ locale).
//
// BOUNDS (net48-verified; all violations throw
// ArgumentOutOfRangeException with the parameter name as message):
//   IndexOf(char, startIndex):          0 <= startIndex <= Length; the scan
//     covers [startIndex, Length). startIndex == Length -> -1 (no throw).
//   IndexOf(char, startIndex, count):   0 <= startIndex, 0 <= count, and
//     startIndex + count <= Length (sum computed in 64-bit); count == 0 ->
//     -1. Scan covers [startIndex, startIndex + count).
//   IndexOf(string, startIndex):        as the char form; an EMPTY value
//     returns startIndex (no scan). A value longer than the remaining span
//     -> -1.
//   IndexOf(string, startIndex, count): as the char form; an EMPTY value
//     returns startIndex; a value longer than 'count' -> -1.
//   LastIndexOf(char, startIndex):      0 <= startIndex < Length (startIndex
//     == Length THROWS -- unlike the string form). Backward scan [startIndex,
//     0].
//   LastIndexOf(char, startIndex, count): 0 <= startIndex, 0 <= count, and
//     count <= startIndex + 1 (net48 quirk: 'count' is bounded by the search
//     start, NOT by Length). count == 0 -> -1. Backward scan
//     [startIndex, startIndex - count + 1].
//   LastIndexOf(string, startIndex):    0 <= startIndex <= Length (startIndex
//     == Length is LEGAL -- differs from the char form). Backward scan from
//     min(startIndex, Length - valueLen).
//   LastIndexOf(string, startIndex, count): 0 <= startIndex <= Length,
//     0 <= count, count <= startIndex + 1. count == 0 -> -1. Backward scan
//     from min(startIndex, Length - valueLen) down to
//     max(startIndex - count + 1, 0).
//   EMPTY-VALUE QUIRK (net48-verified): LastIndexOf with an empty value
//     returns 0 for an empty string, Length - 1 when startIndex >= Length,
//     and startIndex otherwise (count has no effect on the empty-value
//     result beyond the bounds check above).
//
// COMPARISON MODES (the StringComparison enum crossing as int32_t):
//   0 CurrentCulture, 1 CurrentCultureIgnoreCase, 2 InvariantCulture,
//   3 InvariantCultureIgnoreCase, 4 Ordinal, 5 OrdinalIgnoreCase.
//   The culture modes (0-3) are implemented as their ORDINAL equivalents --
//   the documented ASCII-domain deviation shared with ToUpper/ToLower
//   (stringext.cpp): ASCII inputs behave exactly like .NET; non-ASCII units
//   are compared as-is instead of being case-folded per culture. MEASURED
//   2026-10 (.opencode/tmp/gn20-legacy, W7): net48 culture modes on the
//   zh-CN host are OS-NLS collation and disagree with the ordinal fold
//   beyond plain ASCII (case tiebreak lower<upper, U+00DF==ss, punctuation
//   weights, Greek/Cyrillic/CJK ordering; CurrentCultureIgnoreCase Equals
//   folds those classes -- "StraBe" == "STRASSE"). A value outside 0..5
//   throws System.ArgumentException (net48-verified). The ignore-case
//   folding is the SAME ASCII mapping ToLowerUnit uses ('A'-'Z' -> 'a'-'z');
//   non-ASCII units compare case-sensitively. Null 'value' throws
//   ArgumentNullException before the mode is checked.
//
// GC-ROOTING: every function in this header is READ-ONLY (no managed
// allocation), so no rooting contract is needed beyond the caller's normal
// argument liveness. EXCEPTION: the pad family (icall_string_pad_left/right)
// allocates the padded result -- they follow the stringext2.cpp
// single-allocation discipline: the payload is staged into non-GC scratch
// (alloc::il2cpp_alloc_fast, which can never collect) BEFORE the one
// il2cpp_string_new, so the receiver is only read before the allocation.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// IndexOf(char value, int32_t startIndex): the first 'value' occurrence in
// [startIndex, Length), or -1 (also for the empty string). Bounds:
// 0 <= startIndex <= Length, else ArgumentOutOfRangeException.
int32_t icall_string_index_of_c_from(Il2CppObject* this_, char16_t value, int32_t startIndex);

// IndexOf(char value, int32_t startIndex, int32_t count): the first 'value'
// occurrence in [startIndex, startIndex + count), or -1. count == 0 -> -1.
// Bounds: 0 <= startIndex, 0 <= count, startIndex + count <= Length (64-bit
// sum), else ArgumentOutOfRangeException.
int32_t icall_string_index_of_c_range(Il2CppObject* this_, char16_t value, int32_t startIndex, int32_t count);

// IndexOf(string value, int32_t startIndex): ordinal search of 'value' in
// [startIndex, Length), or -1. Empty value -> startIndex. Null 'value' ->
// ArgumentNullException; bounds as the char form.
int32_t icall_string_index_of_s_from(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex);

// IndexOf(string value, int32_t startIndex, int32_t count): ordinal search
// in [startIndex, startIndex + count), or -1. Empty value -> startIndex; a
// value longer than 'count' -> -1. Null 'value' -> ArgumentNullException.
int32_t icall_string_index_of_s_range(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count);

// IndexOf(string value, StringComparison comparisonType): the
// comparison-mode forms of icall_string_index_of_s (batch 149). The mode
// crosses as int32_t (0..5, the shared StringComparison enum); the
// ignore-case modes (1/3/5) fold ASCII case (documented ASCII-domain
// deviation). Null 'value' -> ArgumentNullException.
int32_t icall_string_index_of_s_sc(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType);

// IndexOf(string value, int32_t startIndex, StringComparison comparisonType).
int32_t icall_string_index_of_s_from_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t comparisonType);

// IndexOf(string value, int32_t startIndex, int32_t count, StringComparison comparisonType).
int32_t icall_string_index_of_s_range_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count, int32_t comparisonType);

// LastIndexOf(string value, StringComparison comparisonType).
int32_t icall_string_last_index_of_s_sc(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType);

// LastIndexOf(string value, int32_t startIndex, StringComparison comparisonType).
int32_t icall_string_last_index_of_s_from_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t comparisonType);

// LastIndexOf(string value, int32_t startIndex, int32_t count, StringComparison comparisonType).
int32_t icall_string_last_index_of_s_range_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count, int32_t comparisonType);

// LastIndexOf(char value, int32_t startIndex): the LAST 'value' occurrence
// in [0, startIndex] (backward scan), or -1. Bounds: 0 <= startIndex <
// Length (startIndex == Length THROWS), else ArgumentOutOfRangeException.
int32_t icall_string_last_index_of_c_from(Il2CppObject* this_, char16_t value, int32_t startIndex);

// LastIndexOf(char value, int32_t startIndex, int32_t count): the LAST
// 'value' occurrence in [startIndex - count + 1, startIndex], or -1.
// count == 0 -> -1. Bounds: 0 <= startIndex, 0 <= count,
// count <= startIndex + 1 (net48 quirk -- bounded by start, not Length).
int32_t icall_string_last_index_of_c_range(Il2CppObject* this_, char16_t value, int32_t startIndex, int32_t count);

// LastIndexOf(string value): ordinal search of the LAST 'value' occurrence,
// or -1. Empty value -> Length - 1 (0 for the empty string). Null 'value' ->
// ArgumentNullException.
int32_t icall_string_last_index_of_s(Il2CppObject* this_, Il2CppObject* value);

// LastIndexOf(string value, int32_t startIndex): as above, backward scan of
// the match START positions. net48 requires the match to END at or before
// startIndex (a match starting AT startIndex that extends past it is
// invalid -- csc-probe-verified), so the scan starts at
// min(startIndex - valueLen + 1, Length - valueLen). Bounds:
// 0 <= startIndex <= Length (startIndex == Length is LEGAL here), else
// ArgumentOutOfRangeException.
int32_t icall_string_last_index_of_s_from(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex);

// LastIndexOf(string value, int32_t startIndex, int32_t count): backward
// scan of the match START positions in [startIndex - count + 1,
// startIndex], with the same end-at-or-before-startIndex rule: the scan
// starts at min(startIndex - valueLen + 1, Length - valueLen) and an empty
// window yields -1. count == 0 -> -1. Bounds: 0 <= startIndex <= Length,
// 0 <= count, count <= startIndex + 1. Empty value returns 0 /
// Length - 1 / startIndex per the net48 quirk above.
int32_t icall_string_last_index_of_s_range(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count);

// StartsWith(string value, StringComparison comparisonType): prefix test
// under the mode (see the COMPARISON MODES contract above). Null 'value' ->
// ArgumentNullException; a mode outside 0..5 -> ArgumentException; the empty
// value matches any string (true).
bool icall_string_startswith_culture(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType);

// EndsWith(string value, StringComparison comparisonType): suffix test under
// the mode; contract symmetric to StartsWith.
bool icall_string_endswith_culture(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType);

// PadLeft(int32_t totalWidth): left-pad with SPACES (the 2-arg form pads
// with ' '), returning a NEW string of length totalWidth. IDENTITY RULE
// (csc-probe-verified on net48): totalWidth < Length returns the ORIGINAL
// instance (zero allocations); totalWidth == Length produces a NEW string
// with equal content (net48 differs from .NET 8, which returns the instance
// for ==). totalWidth < 0 -> ArgumentOutOfRangeException; null receiver ->
// NullReferenceException.
Il2CppObject* icall_string_pad_left(Il2CppObject* this_, int32_t totalWidth);

// PadLeft(int32_t totalWidth, char16_t paddingChar): left-pad with an
// explicit pad character; identity and bounds rules as above.
Il2CppObject* icall_string_pad_left(Il2CppObject* this_, int32_t totalWidth, char16_t paddingChar);

// PadRight(int32_t totalWidth): right-pad with SPACES; rules as above.
Il2CppObject* icall_string_pad_right(Il2CppObject* this_, int32_t totalWidth);

// PadRight(int32_t totalWidth, char16_t paddingChar): right-pad with an
// explicit pad character; rules as above.
Il2CppObject* icall_string_pad_right(Il2CppObject* this_, int32_t totalWidth, char16_t paddingChar);

// System.String..ctor(char, int32) -- `new string(ch, n)`: a freshly
// allocated string of 'count' copies of 'ch'. Bounds (net48-verified):
// count < 0 -> ArgumentOutOfRangeException("count"); count == 0 -> the empty
// string. The newobj placeholder receiver (this_) is IGNORED -- the ctor
// allocates through il2cpp_string_new (the create-style convention shared
// with the stringext2 allocation family). A null receiver is legal (a ctor
// does not dereference the placeholder).
Il2CppObject* icall_string_ctor_repeat(Il2CppObject* this_, char16_t c, int32_t count);

// String.IndexOfAny(char[] anyOf, int32_t startIndex, int32_t count): the
// first index i in [startIndex, startIndex + count) whose char is present in
// 'anyOf', or -1. Checks in net48 order (csc-probe-verified):
//   * null 'anyOf'               -> ArgumentNullException
//   * (uint32)startIndex > Length -> ArgumentOutOfRangeException
//   * (uint32)count > Length - startIndex -> ArgumentOutOfRangeException
// An empty 'anyOf' never matches (-1). The receiver scan is ORDINAL on UTF-16
// code units (no host-locale function).
int32_t icall_string_index_of_any_range(Il2CppObject* this_, Il2CppObject* anyOf, int32_t startIndex, int32_t count);

// String.LastIndexOfAny(char[] anyOf, int32_t startIndex, int32_t count): the
// LAST index i in [startIndex - count + 1, startIndex] whose char is present
// in 'anyOf', or -1. Checks in net48 order (csc-probe-verified):
//   * null 'anyOf'               -> ArgumentNullException
//   * Length == 0                -> -1 (short-circuits BEFORE the bounds checks)
//   * (uint32)startIndex >= Length -> ArgumentOutOfRangeException
//   * count < 0 || count - 1 > startIndex -> ArgumentOutOfRangeException
// 'startSearchAt = startIndex + 1 - count' is the backward window start.
int32_t icall_string_last_index_of_any_range(Il2CppObject* this_, Il2CppObject* anyOf, int32_t startIndex, int32_t count);

// P6: String.SetTrailByte(byte) -- sets the last byte of the string's
// internal UTF-16 buffer (used for encoding metadata). The byte is stored
// at position length-1 of the char16_t buffer. No-op if the string is empty.
void icall_string_set_trail_byte(Il2CppObject* this_, uint8_t value);

// P6: String.TryGetTrailByte(out byte) -- reads the last byte of the
// string's internal UTF-16 buffer. Returns true and writes the byte if
// the string is non-empty; returns false if empty.
bool icall_string_try_get_trail_byte(Il2CppObject* this_, uint8_t* result);

} // namespace runtime
} // namespace il2cpp
