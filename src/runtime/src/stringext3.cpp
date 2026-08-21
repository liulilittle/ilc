//------------------------------------------------------------------------------
// stringext3.cpp -- System.String positional-search and comparison-mode
// icalls (see il2cpp_string_ext3.h for the full contract; every bounds rule
// and empty-value quirk below was empirically verified against the .NET
// Framework 4.8 reference runtime before implementation).
//
// Design notes:
//   * Parameter convention matches the other string icalls: managed strings
//     cross the boundary as Il2CppObject* and are cast internally (AsString).
//   * ALL comparisons are ORDINAL on UTF-16 code units -- the functions in
//     this file compare char16_t values directly (int32_t-diff style like
//     ComparePayloads in stringext2.cpp, or unit-wise equality). No
//     host-locale function is ever consulted.
//   * READ-ONLY domain: no function here allocates managed memory, so the
//     GC-rooting contract of stringext2.cpp does not apply.
//   * Exceptions travel as il2cpp::DotNetException via the exceptionext
//     factories (full "{Type},{Type},{Base1},..." chains, see
//     il2cpp_exception_ext.h). ArgumentOutOfRangeException carries the
//     parameter name as its message; ArgumentException carries the net48
//     text for an invalid StringComparison value.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext3.h"

#include "il2cpp_allocator.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// Cast a managed string parameter (Il2CppObject*) to its payload view.
Il2CppString* AsString(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppString*>(obj);
}

// Null-receiver guard: a .NET instance call on null is a
// NullReferenceException (the defensive contract of stringext.cpp).
void ThrowIfNullReceiver(Il2CppObject* this_)
{
    if (this_ == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
}

// Null-argument guard for string-valued parameters: .NET throws
// ArgumentNullException (never ArgumentException) for these methods.
void ThrowIfNullArgument(Il2CppObject* value, const char* paramName)
{
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
}

// ArgumentOutOfRangeException with the parameter name as message (net48
// message shape, stable per the exceptionext scratch contract).
void ThrowOutOfRange(const char* paramName)
{
    il2cpp_throw_argument_out_of_range_exception(paramName, nullptr);
}

// The .NET StringComparison enum values (crossing the boundary as int32_t).
enum StringComparison : int32_t
{
    kCurrentCulture = 0,
    kCurrentCultureIgnoreCase = 1,
    kInvariantCulture = 2,
    kInvariantCultureIgnoreCase = 3,
    kOrdinal = 4,
    kOrdinalIgnoreCase = 5,
};

// net48-verified: a StringComparison value outside 0..5 throws
// System.ArgumentException (NOT ArgumentOutOfRangeException).
void ThrowIfInvalidComparison(int32_t comparisonType)
{
    if (comparisonType < kCurrentCulture || comparisonType > kOrdinalIgnoreCase)
    {
        il2cpp_throw_argument_exception("comparisonType must be a valid StringComparison value.");
    }
}

// Whether the mode folds ASCII case ('A'-'Z' <-> 'a'-'z'). The culture
// ignore-case modes (1, 3) and OrdinalIgnoreCase (5) fold; the plain modes
// (0, 2, 4) do not. ASCII-domain deviation: culture modes equal their
// ordinal counterparts (documented in the header).
bool IsIgnoreCaseMode(int32_t comparisonType)
{
    return comparisonType == kCurrentCultureIgnoreCase
        || comparisonType == kInvariantCultureIgnoreCase
        || comparisonType == kOrdinalIgnoreCase;
}

// ASCII case folding, the SAME mapping ToLowerUnit uses (stringext.cpp):
// 'A'-'Z' -> 'a'-'z'; every other unit is returned unchanged. Non-ASCII
// units therefore compare case-sensitively (documented ASCII-domain
// deviation).
char16_t FoldUnit(char16_t c)
{
    return (c >= u'A' && c <= u'Z') ? static_cast<char16_t>(c + 32) : c;
}

// Unit-wise equality under a comparison mode (see IsIgnoreCaseMode).
bool UnitsEqual(char16_t a, char16_t b, bool ignoreCase)
{
    return ignoreCase ? (FoldUnit(a) == FoldUnit(b)) : (a == b);
}

// Prefix test of 'value' against 's' under 'ignoreCase' (ordinal scan).
// The caller has already enforced: value non-null, comparison valid.
bool PrefixMatches(const Il2CppString* s, const Il2CppString* value, bool ignoreCase)
{
    if (value->length > s->length)
    {
        return false; // the needle is longer than the haystack
    }
    for (int32_t i = 0; i < value->length; ++i)
    {
        if (!UnitsEqual(s->chars[i], value->chars[i], ignoreCase))
        {
            return false;
        }
    }
    return true;
}

// Suffix test of 'value' against 's' under 'ignoreCase' (ordinal scan).
bool SuffixMatches(const Il2CppString* s, const Il2CppString* value, bool ignoreCase)
{
    if (value->length > s->length)
    {
        return false; // the needle is longer than the haystack
    }
    const int32_t offset = s->length - value->length;
    for (int32_t i = 0; i < value->length; ++i)
    {
        if (!UnitsEqual(s->chars[offset + i], value->chars[i], ignoreCase))
        {
            return false;
        }
    }
    return true;
}

// Unit-wise equality of 'count' units at 's + sOffset' with 'value' (ordinal
// or folded per 'ignoreCase'). Used by every string-search scan; the folded
// path maps each unit pair independently, so results coincide with .NET for
// ASCII inputs (documented deviation).
bool SpanMatches(const Il2CppString* s, int32_t sOffset, const Il2CppString* value, bool ignoreCase)
{
    for (int32_t i = 0; i < value->length; ++i)
    {
        if (!UnitsEqual(s->chars[sOffset + i], value->chars[i], ignoreCase))
        {
            return false;
        }
    }
    return true;
}

// Forward ordinal scan of 'value' over the unit window [first, last]
// inclusive; the first match index or -1. 'last' is a valid end index of the
// scan (an empty window yields -1 without touching memory).
int32_t ScanForward(const Il2CppString* s, const Il2CppString* value, bool ignoreCase,
                    int32_t first, int32_t last)
{
    for (int32_t i = first; i <= last; ++i)
    {
        if (SpanMatches(s, i, value, ignoreCase))
        {
            return i;
        }
    }
    return -1;
}

// Backward scan of 'value' from 'hi' down to 'lo' (both inclusive); the last
// match index or -1. An empty window yields -1.
int32_t ScanBackward(const Il2CppString* s, const Il2CppString* value, bool ignoreCase,
                     int32_t hi, int32_t lo)
{
    for (int32_t i = hi; i >= lo; --i)
    {
        if (SpanMatches(s, i, value, ignoreCase))
        {
            return i;
        }
    }
    return -1;
}

// Forward declaration: the shared empty-value LastIndexOf result helper is
// defined in a later anonymous block of this TU (same anonymous namespace,
// so this declaration and that definition name the SAME entity -- a
// declaration in the enclosing runtime namespace would shadow it and make
// the later calls ambiguous).
int32_t EmptyValueLastIndex(const Il2CppString* s, int32_t startIndex);

} // namespace

//------------------------------------------------------------------------------
// icall_string_index_of_c_from -- IndexOf(char, int32_t). Bounds:
// 0 <= startIndex <= Length (startIndex == Length -> -1, no throw).
//------------------------------------------------------------------------------
int32_t icall_string_index_of_c_from(Il2CppObject* this_, char16_t value, int32_t startIndex)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    if (startIndex < 0 || startIndex > s->length)
    {
        ThrowOutOfRange("startIndex");
    }
    for (int32_t i = startIndex; i < s->length; ++i)
    {
        if (s->chars[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_index_of_c_range -- IndexOf(char, int32_t, int32_t). Bounds:
// 0 <= startIndex, 0 <= count, startIndex + count <= Length (64-bit sum);
// count == 0 -> -1.
//------------------------------------------------------------------------------
int32_t icall_string_index_of_c_range(Il2CppObject* this_, char16_t value, int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    if (startIndex < 0 || count < 0 ||
        static_cast<int64_t>(startIndex) + static_cast<int64_t>(count) > s->length)
    {
        ThrowOutOfRange("startIndex or count");
    }
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (s->chars[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_index_of_s_from -- IndexOf(string, int32_t). Empty value ->
// startIndex; a value longer than the remaining span -> -1.
//------------------------------------------------------------------------------
int32_t icall_string_index_of_s_from(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || startIndex > s->length)
    {
        ThrowOutOfRange("startIndex");
    }
    if (v->length == 0)
    {
        return startIndex; // net48: the empty value matches at startIndex
    }
    if (v->length > s->length - startIndex)
    {
        return -1; // the needle cannot fit in the remaining span
    }
    return ScanForward(s, v, false, startIndex, s->length - v->length);
}

//------------------------------------------------------------------------------
// icall_string_index_of_s_range -- IndexOf(string, int32_t, int32_t). Empty
// value -> startIndex; a value longer than 'count' -> -1.
//------------------------------------------------------------------------------
int32_t icall_string_index_of_s_range(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || count < 0 ||
        static_cast<int64_t>(startIndex) + static_cast<int64_t>(count) > s->length)
    {
        ThrowOutOfRange("startIndex or count");
    }
    if (v->length == 0)
    {
        return startIndex; // net48: the empty value matches at startIndex
    }
    if (v->length > count)
    {
        return -1; // the needle cannot fit in the window
    }
    return ScanForward(s, v, false, startIndex, startIndex + count - v->length);
}

//------------------------------------------------------------------------------
// StringComparison-mode index searches (batch 149). The mode crosses as
// int32_t (0..5, the shared enum); the ignore-case modes fold ASCII case
// (IsIgnoreCaseMode + FoldUnit/ScanForward/ScanBackward -- the same
// infrastructure the StartsWith/EndsWith/Equals comparison forms use).
//------------------------------------------------------------------------------

// Validate + decode the comparison mode; returns the ignoreCase flag.
bool DecodeComparisonMode(int32_t comparisonType, bool* ignoreCase)
{
    if (comparisonType < kCurrentCulture || comparisonType > kOrdinalIgnoreCase)
    {
        ThrowOutOfRange("comparisonType");
    }
    *ignoreCase = IsIgnoreCaseMode(comparisonType);
    return true;
}

int32_t icall_string_index_of_s_sc(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    bool ignoreCase = false;
    DecodeComparisonMode(comparisonType, &ignoreCase);
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (v->length == 0)
    {
        return 0; // net48: the empty value matches at index 0
    }
    if (v->length > s->length)
    {
        return -1;
    }
    return ScanForward(s, v, ignoreCase, 0, s->length - v->length);
}

int32_t icall_string_index_of_s_from_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    bool ignoreCase = false;
    DecodeComparisonMode(comparisonType, &ignoreCase);
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || startIndex > s->length)
    {
        ThrowOutOfRange("startIndex");
    }
    if (v->length == 0)
    {
        return startIndex;
    }
    if (v->length > s->length - startIndex)
    {
        return -1;
    }
    return ScanForward(s, v, ignoreCase, startIndex, s->length - v->length);
}

int32_t icall_string_index_of_s_range_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    bool ignoreCase = false;
    DecodeComparisonMode(comparisonType, &ignoreCase);
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || count < 0 ||
        static_cast<int64_t>(startIndex) + static_cast<int64_t>(count) > s->length)
    {
        ThrowOutOfRange("startIndex or count");
    }
    if (v->length == 0)
    {
        return startIndex;
    }
    if (v->length > count)
    {
        return -1;
    }
    return ScanForward(s, v, ignoreCase, startIndex, startIndex + count - v->length);
}

int32_t icall_string_last_index_of_s_sc(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    bool ignoreCase = false;
    DecodeComparisonMode(comparisonType, &ignoreCase);
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (v->length == 0)
    {
        return EmptyValueLastIndex(s, s->length);
    }
    if (v->length > s->length)
    {
        return -1;
    }
    return ScanBackward(s, v, ignoreCase, s->length - v->length, 0);
}

int32_t icall_string_last_index_of_s_from_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    bool ignoreCase = false;
    DecodeComparisonMode(comparisonType, &ignoreCase);
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || startIndex > s->length)
    {
        ThrowOutOfRange("startIndex");
    }
    if (v->length == 0)
    {
        return EmptyValueLastIndex(s, startIndex);
    }
    if (v->length > s->length)
    {
        return -1;
    }
    const int32_t hi = (startIndex - v->length + 1 < s->length - v->length)
        ? startIndex - v->length + 1
        : s->length - v->length;
    return ScanBackward(s, v, ignoreCase, hi, 0);
}

int32_t icall_string_last_index_of_s_range_sc(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    bool ignoreCase = false;
    DecodeComparisonMode(comparisonType, &ignoreCase);
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || startIndex > s->length || count < 0 || count > startIndex + 1)
    {
        ThrowOutOfRange("startIndex or count");
    }
    if (v->length == 0)
    {
        return EmptyValueLastIndex(s, startIndex);
    }
    if (v->length > s->length)
    {
        return -1;
    }
    const int32_t hi = (startIndex - v->length + 1 < s->length - v->length)
        ? startIndex - v->length + 1
        : s->length - v->length;
    const int32_t lo = (startIndex - count + 1 > 0) ? startIndex - count + 1 : 0;
    if (hi < lo)
    {
        return -1;
    }
    return ScanBackward(s, v, ignoreCase, hi, lo);
}

//------------------------------------------------------------------------------
// icall_string_last_index_of_c_from -- LastIndexOf(char, int32_t). Bounds:
// 0 <= startIndex < Length (startIndex == Length THROWS -- the char form is
// stricter than the string form).
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_c_from(Il2CppObject* this_, char16_t value, int32_t startIndex)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    if (startIndex < 0 || startIndex >= s->length)
    {
        ThrowOutOfRange("startIndex");
    }
    for (int32_t i = startIndex; i >= 0; --i)
    {
        if (s->chars[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_last_index_of_c_range -- LastIndexOf(char, int32_t, int32_t).
// Bounds: 0 <= startIndex, 0 <= count, count <= startIndex + 1 (net48
// quirk: bounded by the search start, NOT by Length); count == 0 -> -1.
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_c_range(Il2CppObject* this_, char16_t value, int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    if (startIndex < 0 || count < 0 || count > startIndex + 1)
    {
        ThrowOutOfRange("startIndex or count");
    }
    if (count == 0)
    {
        return -1;
    }
    const int32_t lo = startIndex - count + 1;
    for (int32_t i = startIndex; i >= lo; --i)
    {
        if (s->chars[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// LastIndexOf(string) EMPTY-VALUE QUIRK (net48-verified): the empty value
// matches at Length - 1 (0 for the empty string). Non-empty: backward scan
// from Length - valueLen; -1 when the needle cannot fit.
//------------------------------------------------------------------------------
namespace {

int32_t EmptyValueLastIndex(const Il2CppString* s, int32_t startIndex)
{
    if (s->length == 0)
    {
        return 0; // net48: an empty string still matches the empty value
    }
    return startIndex >= s->length ? s->length - 1 : startIndex;
}

} // namespace

//------------------------------------------------------------------------------
// icall_string_last_index_of_s -- LastIndexOf(string).
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_s(Il2CppObject* this_, Il2CppObject* value)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (v->length == 0)
    {
        return EmptyValueLastIndex(s, s->length);
    }
    if (v->length > s->length)
    {
        return -1; // the needle cannot fit
    }
    return ScanBackward(s, v, false, s->length - v->length, 0);
}

//------------------------------------------------------------------------------
// icall_string_last_index_of_s_from -- LastIndexOf(string, int32_t). Bounds:
// 0 <= startIndex <= Length (startIndex == Length is LEGAL -- differs from
// the char form). Backward scan of the match START positions; net48 requires
// the match to END at or before startIndex (a match STARTING at startIndex
// is invalid when it extends past it -- csc-probe-verified), so the highest
// valid start is min(startIndex - valueLen + 1, Length - valueLen).
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_s_from(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || startIndex > s->length)
    {
        ThrowOutOfRange("startIndex");
    }
    if (v->length == 0)
    {
        return EmptyValueLastIndex(s, startIndex);
    }
    if (v->length > s->length)
    {
        return -1; // the needle cannot fit
    }
    const int32_t hi = (startIndex - v->length + 1 < s->length - v->length)
        ? startIndex - v->length + 1
        : s->length - v->length;
    return ScanBackward(s, v, false, hi, 0);
}

//------------------------------------------------------------------------------
// icall_string_last_index_of_s_range -- LastIndexOf(string, int32_t,
// int32_t). Bounds: 0 <= startIndex <= Length, 0 <= count,
// count <= startIndex + 1; count == 0 -> -1. Backward scan of the match
// START positions in [startIndex - count + 1, startIndex], where net48
// requires the match to END at or before startIndex (see the "from" form);
// the highest valid start is min(startIndex - valueLen + 1,
// Length - valueLen).
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_s_range(Il2CppObject* this_, Il2CppObject* value, int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (startIndex < 0 || startIndex > s->length || count < 0 || count > startIndex + 1)
    {
        ThrowOutOfRange("startIndex or count");
    }
    if (v->length == 0)
    {
        return EmptyValueLastIndex(s, startIndex);
    }
    if (v->length > s->length)
    {
        return -1; // the needle cannot fit
    }
    const int32_t hi = (startIndex - v->length + 1 < s->length - v->length)
        ? startIndex - v->length + 1
        : s->length - v->length;
    const int32_t lo = (startIndex - count + 1 > 0) ? startIndex - count + 1 : 0;
    if (hi < lo)
    {
        return -1; // net48: no valid start in the window
    }
    return ScanBackward(s, v, false, hi, lo);
}

//------------------------------------------------------------------------------
// icall_string_startswith_culture -- StartsWith(string, StringComparison).
// Mode 0-5 (see the header); culture modes equal their ordinal counterparts
// (ASCII-domain deviation); invalid modes throw ArgumentException.
//------------------------------------------------------------------------------
bool icall_string_startswith_culture(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    ThrowIfInvalidComparison(comparisonType);
    return PrefixMatches(AsString(this_), AsString(value), IsIgnoreCaseMode(comparisonType));
}

//------------------------------------------------------------------------------
// icall_string_endswith_culture -- EndsWith(string, StringComparison);
// symmetric contract to StartsWith.
//------------------------------------------------------------------------------
bool icall_string_endswith_culture(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    ThrowIfInvalidComparison(comparisonType);
    return SuffixMatches(AsString(this_), AsString(value), IsIgnoreCaseMode(comparisonType));
}

//------------------------------------------------------------------------------
// Pad core (PadLeft/PadRight share it). IDENTITY RULE, csc-probe-verified
// against net48 (non-interned receiver): totalWidth < Length returns the
// ORIGINAL instance; totalWidth == Length creates a NEW string with equal
// content (.NET 8 returns the instance for == -- net48 does not). Bounds:
// totalWidth < 0 -> ArgumentOutOfRangeException; null receiver -> NRE.
// Allocation follows the stringext2.cpp single-allocation discipline: the
// payload is staged into non-GC scratch (il2cpp_alloc_fast can never
// collect) BEFORE the one il2cpp_string_new, so the receiver is only read
// before the allocation point.
//------------------------------------------------------------------------------
namespace {

Il2CppObject* PadString(Il2CppObject* this_, int32_t totalWidth, char16_t paddingChar, bool isRightPadded)
{
    ThrowIfNullReceiver(this_);
    if (totalWidth < 0)
    {
        ThrowOutOfRange("totalWidth");
    }
    const Il2CppString* s = AsString(this_);
    if (totalWidth < s->length)
    {
        return this_; // net48: strictly-less widths return the ORIGINAL instance
    }
    const int32_t padCount = totalWidth - s->length;
    char16_t* scratch = static_cast<char16_t*>(alloc::il2cpp_alloc_fast(
        static_cast<size_t>(totalWidth) * sizeof(char16_t)));
    if (isRightPadded)
    {
        std::memcpy(scratch, s->chars, static_cast<size_t>(s->length) * sizeof(char16_t));
        for (int32_t i = 0; i < padCount; ++i)
        {
            scratch[s->length + i] = paddingChar;
        }
    }
    else
    {
        for (int32_t i = 0; i < padCount; ++i)
        {
            scratch[i] = paddingChar;
        }
        std::memcpy(scratch + padCount, s->chars, static_cast<size_t>(s->length) * sizeof(char16_t));
    }
    Il2CppString* result = il2cpp_string_new(scratch, totalWidth);
    alloc::il2cpp_free_fast(scratch);
    return reinterpret_cast<Il2CppObject*>(result);
}

} // namespace

//------------------------------------------------------------------------------
// icall_string_pad_left -- PadLeft(int32_t): space-padded left pad (the
// 2-arg net48 overload delegates to the char form with ' ').
//------------------------------------------------------------------------------
Il2CppObject* icall_string_pad_left(Il2CppObject* this_, int32_t totalWidth)
{
    return icall_string_pad_left(this_, totalWidth, u' ');
}

//------------------------------------------------------------------------------
// icall_string_pad_left -- PadLeft(int32_t, char16_t): explicit pad char.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_pad_left(Il2CppObject* this_, int32_t totalWidth, char16_t paddingChar)
{
    return PadString(this_, totalWidth, paddingChar, false);
}

//------------------------------------------------------------------------------
// icall_string_pad_right -- PadRight(int32_t): space-padded right pad.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_pad_right(Il2CppObject* this_, int32_t totalWidth)
{
    return icall_string_pad_right(this_, totalWidth, u' ');
}

//------------------------------------------------------------------------------
// icall_string_pad_right -- PadRight(int32_t, char16_t): explicit pad char.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_pad_right(Il2CppObject* this_, int32_t totalWidth, char16_t paddingChar)
{
    return PadString(this_, totalWidth, paddingChar, true);
}

//------------------------------------------------------------------------------
// icall_string_ctor_repeat -- System.String..ctor(char, int32):
// `new string(ch, n)` -> a fresh string of 'count' copies of 'ch'. Bounds
// (csc-probe-verified on net48): count < 0 -> ArgumentOutOfRangeException
// ("count"); count == 0 -> the empty string. The newobj placeholder receiver
// is ignored (the create-style convention -- the runtime allocates through
// il2cpp_string_new). A null receiver is legal: a ctor never dereferences it.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_ctor_repeat(Il2CppObject* this_, char16_t c, int32_t count)
{
    (void)this_; // the newobj placeholder is a discarded allocation
    if (count < 0)
    {
        ThrowOutOfRange("count");
    }
    if (count == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(static_cast<size_t>(count) * sizeof(char16_t)));
    for (int32_t i = 0; i < count; ++i)
    {
        scratch[i] = c;
    }
    Il2CppString* result = il2cpp_string_new(scratch, count);
    alloc::il2cpp_free_fast(scratch);
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// MemberOfAny -- whether the char[] 'anyOf' contains 'ch'. A null 'anyOf' has
// already been rejected by the caller (ArgumentNullException precedes every
// scan); an empty array yields false.
//------------------------------------------------------------------------------
namespace {

bool MemberOfAny(Il2CppArray* anyOf, char16_t ch)
{
    for (int32_t i = 0; i < anyOf->length; ++i)
    {
        const char* slot = il2cpp_array_get_elem_ptr(anyOf, i);
        char16_t elem = *reinterpret_cast<const char16_t*>(slot);
        if (elem == ch)
        {
            return true;
        }
    }
    return false;
}

} // namespace

//------------------------------------------------------------------------------
// icall_string_index_of_any_range -- String.IndexOfAny(char[], int32_t,
// int32_t). Checks in net48 order: null 'anyOf' -> ANE; (uint32)startIndex >
// Length -> AOO; (uint32)count > Length - startIndex -> AOO. Then the first
// index in [startIndex, startIndex + count) whose char is in 'anyOf', or -1.
//------------------------------------------------------------------------------
int32_t icall_string_index_of_any_range(Il2CppObject* this_, Il2CppObject* anyOf, int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(this_);
    if (anyOf == nullptr)
    {
        il2cpp_throw_argument_null_exception("anyOf");
    }
    const Il2CppString* s = AsString(this_);
    if (static_cast<uint32_t>(startIndex) > static_cast<uint32_t>(s->length))
    {
        ThrowOutOfRange("startIndex");
    }
    if (static_cast<uint32_t>(count) > static_cast<uint32_t>(s->length - startIndex))
    {
        ThrowOutOfRange("count");
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(anyOf);
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (MemberOfAny(arr, s->chars[i]))
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_last_index_of_any_range -- String.LastIndexOfAny(char[],
// int32_t, int32_t). net48 order: null 'anyOf' -> ANE; Length == 0 -> -1
// (short-circuits BEFORE the bounds checks -- csc-probe-verified);
// (uint32)startIndex >= Length -> AOO; count < 0 || count - 1 > startIndex ->
// AOO. Backward scan of [startSearchAt, startIndex] where
// startSearchAt = startIndex + 1 - count.
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_any_range(Il2CppObject* this_, Il2CppObject* anyOf, int32_t startIndex, int32_t count)
{
    ThrowIfNullReceiver(this_);
    if (anyOf == nullptr)
    {
        il2cpp_throw_argument_null_exception("anyOf");
    }
    const Il2CppString* s = AsString(this_);
    if (s->length == 0)
    {
        return -1; // net48: an empty string short-circuits before the bounds checks
    }
    if (static_cast<uint32_t>(startIndex) >= static_cast<uint32_t>(s->length))
    {
        ThrowOutOfRange("startIndex");
    }
    if (count < 0 || (count - 1) > startIndex)
    {
        ThrowOutOfRange("count");
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(anyOf);
    const int32_t lo = startIndex + 1 - count;
    for (int32_t i = startIndex; i >= lo; --i)
    {
        if (MemberOfAny(arr, s->chars[i]))
        {
            return i;
        }
    }
    return -1;
}

// P6: String.SetTrailByte(byte) -- sets the last byte of the string's
// internal UTF-16 buffer (used for encoding metadata). The byte is stored
// at position length-1 of the char16_t buffer. No-op if the string is empty.
void icall_string_set_trail_byte(Il2CppObject* this_, uint8_t value)
{
    if (this_ == nullptr) return;
    auto* s = reinterpret_cast<Il2CppString*>(this_);
    if (s->length <= 0) return;
    auto* chars = reinterpret_cast<uint8_t*>(&s->chars[s->length - 1]);
    *chars = value;
}

// P6: String.TryGetTrailByte(out byte) -- reads the last byte of the
// string's internal UTF-16 buffer. Returns true and writes the byte if
// the string is non-empty; returns false if empty.
bool icall_string_try_get_trail_byte(Il2CppObject* this_, uint8_t* result)
{
    if (this_ == nullptr || result == nullptr) return false;
    auto* s = reinterpret_cast<Il2CppString*>(this_);
    if (s->length <= 0) { *result = 0; return false; }
    auto* chars = reinterpret_cast<const uint8_t*>(&s->chars[s->length - 1]);
    *result = *chars;
    return true;
}

} // namespace runtime
} // namespace il2cpp
