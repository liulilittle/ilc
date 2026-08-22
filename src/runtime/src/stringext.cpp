//------------------------------------------------------------------------------
// stringext.cpp -- System.String instance operations (see il2cpp_string_ext.h
// for the full contract, semantics list and known deviations).
//
// All operations work on the managed UTF-16 payload of Il2CppString
// (length + chars[], see il2cpp_string.h). Results are allocated through the
// SAME size-extended path string.cpp uses (il2cpp_gc_alloc_size on the
// lazily-registered System.String descriptor), so every produced string is a
// first-class GC object with the correct physical block extent.
//
// Design notes:
//   * Parameter convention matches the other icalls: managed strings cross
//     the boundary as Il2CppObject* and are cast internally (AsString).
//   * Exceptions travel as il2cpp::DotNetException (il2cpp_exception.h) with
//     the "{Type},{Type},{Base...}" chain format used by exception.cpp (the
//     catch matcher walks the chain AFTER the first comma, so the type is
//     repeated as its first walk segment -- the exceptionext.cpp convention):
//       ArgumentNullException   -> "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"
//       ArgumentOutOfRange      -> "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception"
//       NullReferenceException  -> "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception"
//     A null receiver is a defensive NRE (generated code null-checks first);
//     a null string-valued argument is an ANE exactly like .NET.
//   * Identity optimizations (returning the receiver when nothing changes)
//     match .NET Framework / Core observable behavior (ReferenceEquals).
//   * Substring bounds are checked with a 64-bit sum so startIndex + length
//     can never overflow the int32 domain and bypass the check.
//------------------------------------------------------------------------------

#include "il2cpp_string_ext.h"
#include "il2cpp_string_ext2.h"
#include "il2cpp_string_ext3.h"
#include "il2cpp_concat.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstring>
#include <vector>

namespace il2cpp {
namespace runtime {

namespace {

// Cast a managed string parameter (Il2CppObject*) to its payload view.
Il2CppString* AsString(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppString*>(obj);
}

// .NET Char.IsWhiteSpace: the exact code-unit set the BCL treats as
// whitespace (control whitespace + space + the Unicode Zs/Zl/Zp spaces).
// Listed literally so the runtime never depends on the host C++ locale.
bool IsWhitespace(char16_t c)
{
    switch (c)
    {
        case u'\t':                            // HT
        case u'\n':                            // LF
        case u'\v':                            // VT
        case u'\f':                            // FF
        case u'\r':                            // CR
        case u' ':                             // SPACE
        case u'\x0085':                        // NEL (next line)
        case u'\x00A0':                        // NBSP (no-break space)
        case u'\x1680':                        // OGHAM SPACE MARK
        case u'\x2000':                        // EN QUAD .. HAIR SPACE
        case u'\x2001':
        case u'\x2002':
        case u'\x2003':
        case u'\x2004':
        case u'\x2005':
        case u'\x2006':
        case u'\x2007':
        case u'\x2008':
        case u'\x2009':
        case u'\x200A':
        case u'\x2028':                        // LINE SEPARATOR
        case u'\x2029':                        // PARAGRAPH SEPARATOR
        case u'\x202F':                        // NARROW NBSP
        case u'\x205F':                        // MEDIUM MATHEMATICAL SPACE
        case u'\x3000':                        // IDEOGRAPHIC SPACE
            return true;
        default:
            return false;
    }
}

// ASCII upper-casing of a single unit: 'a'-'z' -> 'A'-'Z', others unchanged.
char16_t ToUpperUnit(char16_t c)
{
    return (c >= u'a' && c <= u'z') ? static_cast<char16_t>(c - 32) : c;
}

// ASCII lower-casing of a single unit: 'A'-'Z' -> 'a'-'z', others unchanged.
char16_t ToLowerUnit(char16_t c)
{
    return (c >= u'A' && c <= u'Z') ? static_cast<char16_t>(c + 32) : c;
}

// Allocate a managed string holding 'count' units copied from 'units'
// (null units allowed for count == 0). The block shape replicates
// string.cpp's StringAllocSize exactly: sizeof(Il2CppString) (which already
// includes the trailing chars[1] stub) plus (count-1) extra units when
// count > 1. The collector zero-initializes the block; the length slot and
// payload are then filled in.
Il2CppString* NewStringFromUnits(const char16_t* units, int32_t count)
{
    if (count < 0)
    {
        count = 0; // defensive: negative count is a caller bug
    }
    const size_t allocSize = sizeof(Il2CppString)
        + ((count > 1) ? static_cast<size_t>(count - 1) * sizeof(char16_t) : 0);
    Il2CppString* s = reinterpret_cast<Il2CppString*>(
        il2cpp_gc_alloc_size(il2cpp_string_class(), allocSize));
    s->length = count;
    if (count > 0 && units != nullptr)
    {
        std::memcpy(s->chars, units, static_cast<size_t>(count) * sizeof(char16_t));
    }
    return s;
}

// Null-receiver guard: a .NET instance call on null is a
// NullReferenceException. Generated code null-checks before the call
// (il2cpp_null_check); this is the icall's defensive contract.
void ThrowIfNullReceiver(Il2CppObject* this_)
{
    if (this_ == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
}

// Null-argument guard for string-valued parameters: .NET throws
// ArgumentNullException (never ArgumentException) for these methods.
void ThrowIfNullArgument(Il2CppObject* value, const char* paramName)
{
    if (value == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                         paramName);
    }
}

//------------------------------------------------------------------------------
// RootScope -- RAII managed-object rooting (regex_api.cpp:71 / linq_map.cpp:
// 151 precedent). A root slot is the ADDRESS of a local Il2CppObject*
// variable; the collector rewrites the slot when the object moves (Gen0
// compaction), so re-deriving pointers from the rooted slot after any
// allocation is mandatory. The destructor removes every registered slot even
// when a managed exception escapes, so the icall can never leak a stale root.
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
    void** m_slots[4]; // max 2 used today (the result array + a transient)
    int32_t m_count;
};

} // namespace

//------------------------------------------------------------------------------
// il2cpp_throw_argument_out_of_range -- bounds-check helper (see header).
//------------------------------------------------------------------------------
void il2cpp_throw_argument_out_of_range(const char* message)
{
    il2cpp_throw_new("System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Argument is out of range.");
}

//------------------------------------------------------------------------------
// icall_string_substring -- Substring(int32_t) (see header).
// net48 identity (fresh-oracle verified 2026-08-08): startIndex == 0 returns
// the RECEIVER instance itself (ReferenceEquals), not a copy.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_substring(Il2CppObject* this_, int32_t startIndex)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    if (startIndex < 0 || startIndex > s->length)
    {
        il2cpp_throw_argument_out_of_range("startIndex");
    }
    if (startIndex == 0)
    {
        return this_; // net48: Substring(0) is the receiver, no allocation
    }
    return reinterpret_cast<Il2CppObject*>(
        NewStringFromUnits(s->chars + startIndex, s->length - startIndex));
}

//------------------------------------------------------------------------------
// icall_string_substring_range -- Substring(int32_t, int32_t) (see header).
// net48 identity: (startIndex == 0 && length == Length) returns the RECEIVER
// instance itself (fresh-oracle verified 2026-08-08: Substring(0, Length) is
// ReferenceEquals the receiver; a partial-length cut allocates).
//------------------------------------------------------------------------------
Il2CppObject* icall_string_substring_range(Il2CppObject* this_, int32_t startIndex, int32_t length)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    // 64-bit sum: an int32 overflow (e.g. length = INT32_MAX) must never
    // wrap the bounds check into accepting the range.
    if (startIndex < 0 || length < 0 ||
        static_cast<int64_t>(startIndex) + static_cast<int64_t>(length) > s->length)
    {
        il2cpp_throw_argument_out_of_range("startIndex or length");
    }
    if (startIndex == 0 && length == s->length)
    {
        return this_; // net48: the full-range cut is the receiver, no allocation
    }
    return reinterpret_cast<Il2CppObject*>(
        NewStringFromUnits(s->chars + startIndex, length));
}

//------------------------------------------------------------------------------
// icall_string_startswith -- StartsWith (see header). Prefix test, ordinal.
//------------------------------------------------------------------------------
bool icall_string_startswith(Il2CppObject* this_, Il2CppObject* value)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (v->length > s->length)
    {
        return false; // the needle is longer than the haystack
    }
    return std::memcmp(s->chars, v->chars,
                       static_cast<size_t>(v->length) * sizeof(char16_t)) == 0;
}

//------------------------------------------------------------------------------
// icall_string_endswith -- EndsWith (see header). Suffix test, ordinal.
//------------------------------------------------------------------------------
bool icall_string_endswith(Il2CppObject* this_, Il2CppObject* value)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (v->length > s->length)
    {
        return false; // the needle is longer than the haystack
    }
    const int32_t offset = s->length - v->length;
    return std::memcmp(s->chars + offset, v->chars,
                       static_cast<size_t>(v->length) * sizeof(char16_t)) == 0;
}

//------------------------------------------------------------------------------
// icall_string_index_of_c -- IndexOf(char) (see header). Linear first-match
// scan; -1 when absent.
//------------------------------------------------------------------------------
int32_t icall_string_index_of_c(Il2CppObject* this_, char16_t ch)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] == ch)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_index_of_s -- IndexOf(string) (see header). Ordinal naive
// scan (documented O(n*m); a Boyer-Moore upgrade is a later optimization).
// The empty needle matches at index 0 (.NET semantics).
//------------------------------------------------------------------------------
int32_t icall_string_index_of_s(Il2CppObject* this_, Il2CppObject* value)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    if (v->length == 0)
    {
        return 0;
    }
    if (v->length > s->length)
    {
        return -1;
    }
    const int32_t limit = s->length - v->length;
    for (int32_t i = 0; i <= limit; ++i)
    {
        if (std::memcmp(s->chars + i, v->chars,
                        static_cast<size_t>(v->length) * sizeof(char16_t)) == 0)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_last_index_of_c -- LastIndexOf(char) (see header). Linear
// LAST-match scan (backward); -1 when absent.
//------------------------------------------------------------------------------
int32_t icall_string_last_index_of_c(Il2CppObject* this_, char16_t ch)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    for (int32_t i = s->length - 1; i >= 0; --i)
    {
        if (s->chars[i] == ch)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_string_replace -- Replace(char, char) (see header). Identity
// semantics per the net48 BASELINE (empirically verified against the
// reference runtime): no occurrence -> the SAME instance; oldChar ==
// newChar -> a NEW (content-equal) instance. .NET Core's extra
// oldChar == newChar identity fast path is intentionally NOT reproduced.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_replace(Il2CppObject* this_, char16_t oldChar, char16_t newChar)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    bool found = false;
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] == oldChar)
        {
            found = true;
            break;
        }
    }
    if (!found)
    {
        return this_; // nothing to replace -> same instance
    }
    Il2CppString* result = NewStringFromUnits(s->chars, s->length);
    for (int32_t i = 0; i < result->length; ++i)
    {
        if (result->chars[i] == oldChar)
        {
            result->chars[i] = newChar;
        }
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// icall_string_replace_s_s -- Replace(string, string) (see header). net48
// semantics (csc-probe-verified 2026-08-09):
//   * null 'oldValue' -> ArgumentNullException; EMPTY 'oldValue' ->
//     ArgumentException ("String cannot be of zero length.") -- both BEFORE
//     any occurrence scan (an empty oldValue on the empty receiver still
//     throws).
//   * non-overlapping replacement: the greedy left-to-right scan advances
//     past each match by oldValue->length, so overlapping occurrences are
//     NOT recursively re-matched ("aaa".Replace("aa","b") == "ba").
//   * null 'newValue' removes the occurrences (treated as the empty string).
//   * no occurrence -> the SAME instance (net48 ReferenceEquals identity);
//     occurrences exist -> a NEW instance is ALWAYS returned, even when the
//     replacement reproduces the original content (net48 has no
//     oldValue == newValue identity fast path).
//
// GC safety (the two-phase discipline): the receiver, oldValue and newValue
// payloads are staged into non-GC scratch (std::vector) BEFORE the first
// managed allocation -- a Gen0 compaction during the result allocation would
// move the unrooted source objects, so no post-allocation code may read
// them. The result is ROOTED for the whole fill (RootScope): the
// per-match newValue copies cannot trigger a collection here (no further
// allocations), but the root keeps the invariant documented for the split
// icall -- re-deriving the result pointer from its rooted slot is mandatory
// after any allocation.
//------------------------------------------------------------------------------
Il2CppString* icall_string_replace_s_s(Il2CppString* self, Il2CppString* oldValue, Il2CppString* newValue)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    if (oldValue == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                         "Value cannot be null. (Parameter 'oldValue')");
    }
    const int32_t oldLen = oldValue->length;
    if (oldLen == 0)
    {
        il2cpp_throw_new("System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception",
                         "String cannot be of zero length.");
    }

    // --- Stage the receiver + old/new payloads into non-GC scratch BEFORE
    //     any allocation (Gen0 compaction can move unrooted objects).
    const int32_t len = self->length;
    std::vector<char16_t> text(self->chars, self->chars + len);
    std::vector<char16_t> oldUnits(oldValue->chars, oldValue->chars + oldLen);
    const int32_t newLen = (newValue != nullptr) ? newValue->length : 0;
    std::vector<char16_t> newUnits;
    if (newLen > 0)
    {
        newUnits.assign(newValue->chars, newValue->chars + newLen);
    }

    // --- Count the non-overlapping occurrences (greedy left-to-right scan,
    //     the net48 maximum-match semantics).
    int32_t occurrenceCount = 0;
    int32_t pos = 0;
    while (pos <= len - oldLen)
    {
        if (std::memcmp(text.data() + pos, oldUnits.data(),
                        static_cast<size_t>(oldLen) * sizeof(char16_t)) == 0)
        {
            ++occurrenceCount;
            pos += oldLen;
        }
        else
        {
            ++pos;
        }
    }
    if (occurrenceCount == 0)
    {
        return self; // nothing to replace -> same instance (net48 identity)
    }

    // --- Compute the result length in 64-bit (a wrap-around could bypass
    //     the int32 allocation bounds).
    const int64_t delta64 = static_cast<int64_t>(newLen) - oldLen;
    const int64_t resultLength64 = static_cast<int64_t>(len) + delta64 * occurrenceCount;
    if (resultLength64 > INT32_MAX)
    {
        il2cpp_throw_new("System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                         "Insufficient memory to continue the execution of the program.");
    }
    const int32_t resultLength = static_cast<int32_t>(resultLength64);

    // --- Allocate the result and ROOT it for the whole fill.
    Il2CppString* result = NewStringFromUnits(nullptr, resultLength);
    RootScope root;
    root.Add(reinterpret_cast<void**>(&result));

    // --- Fill: copy the unchanged runs, splice the newValue at each match.
    int32_t write = 0;
    pos = 0;
    while (pos <= len - oldLen)
    {
        if (std::memcmp(text.data() + pos, oldUnits.data(),
                        static_cast<size_t>(oldLen) * sizeof(char16_t)) == 0)
        {
            if (newLen > 0)
            {
                std::memcpy(result->chars + write, newUnits.data(),
                            static_cast<size_t>(newLen) * sizeof(char16_t));
            }
            write += newLen;
            pos += oldLen;
        }
        else
        {
            result->chars[write++] = text[static_cast<size_t>(pos)];
            ++pos;
        }
    }
    // Tail beyond the last possible match start: no further matches.
    const int32_t remaining = len - pos;
    if (remaining > 0)
    {
        std::memcpy(result->chars + write, text.data() + pos,
                    static_cast<size_t>(remaining) * sizeof(char16_t));
    }

    return result;
}

//------------------------------------------------------------------------------
// icall_string_to_upper -- ToUpper() (see header). ASCII-only mapping; the
// unchanged fast path returns the SAME instance.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_to_upper(Il2CppObject* this_)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    bool changed = false;
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (ToUpperUnit(s->chars[i]) != s->chars[i])
        {
            changed = true;
            break;
        }
    }
    if (!changed)
    {
        return this_;
    }
    Il2CppString* result = NewStringFromUnits(s->chars, s->length);
    for (int32_t i = 0; i < result->length; ++i)
    {
        result->chars[i] = ToUpperUnit(result->chars[i]);
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// icall_string_to_lower -- ToLower() (see header). ASCII-only mapping;
// symmetric identity fast path to ToUpper.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_to_lower(Il2CppObject* this_)
{
    ThrowIfNullReceiver(this_);
    const Il2CppString* s = AsString(this_);
    bool changed = false;
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (ToLowerUnit(s->chars[i]) != s->chars[i])
        {
            changed = true;
            break;
        }
    }
    if (!changed)
    {
        return this_;
    }
    Il2CppString* result = NewStringFromUnits(s->chars, s->length);
    for (int32_t i = 0; i < result->length; ++i)
    {
        result->chars[i] = ToLowerUnit(result->chars[i]);
    }
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// Trim core -- shared by Trim / TrimStart / TrimEnd. Computes the trimmed
// span according to 'mode' and returns the receiver (nothing to trim) or a
// fresh substring (or "" for all-whitespace input).
//------------------------------------------------------------------------------
namespace {

// Which side(s) of the string the whitespace scan must remove.
enum class TrimMode
{
    Both,   // Trim(): leading AND trailing
    Start,  // TrimStart(): leading only
    End,    // TrimEnd(): trailing only
};

Il2CppObject* TrimImpl(Il2CppObject* this_, TrimMode mode)
{
    const Il2CppString* s = AsString(this_);
    int32_t first = 0;
    int32_t last = s->length - 1;
    if (mode != TrimMode::End)
    {
        while (first < s->length && IsWhitespace(s->chars[first]))
        {
            ++first;
        }
    }
    if (mode != TrimMode::Start)
    {
        while (last >= first && IsWhitespace(s->chars[last]))
        {
            --last;
        }
    }
    if (first > last)
    {
        return reinterpret_cast<Il2CppObject*>(NewStringFromUnits(nullptr, 0)); // all whitespace -> ""
    }
    const bool trimmedStart = (first != 0);
    const bool trimmedEnd = (last != s->length - 1);
    if (mode == TrimMode::Start && !trimmedStart)
    {
        return this_; // TrimStart on an already-clean start
    }
    if (mode == TrimMode::End && !trimmedEnd)
    {
        return this_; // TrimEnd on an already-clean end
    }
    if (mode == TrimMode::Both && !trimmedStart && !trimmedEnd)
    {
        return this_; // Trim on a clean string
    }
    return reinterpret_cast<Il2CppObject*>(
        NewStringFromUnits(s->chars + first, last - first + 1));
}

} // namespace

//------------------------------------------------------------------------------
// icall_string_trim / trim_start / trim_end -- see header.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_trim(Il2CppObject* this_)
{
    ThrowIfNullReceiver(this_);
    return TrimImpl(this_, TrimMode::Both);
}

Il2CppObject* icall_string_trim_start(Il2CppObject* this_)
{
    ThrowIfNullReceiver(this_);
    return TrimImpl(this_, TrimMode::Start);
}

Il2CppObject* icall_string_trim_end(Il2CppObject* this_)
{
    ThrowIfNullReceiver(this_);
    return TrimImpl(this_, TrimMode::End);
}

//------------------------------------------------------------------------------
// icall_string_compare_to -- CompareTo(string) (see header). ORDINAL
// comparison: first differing unit decides; a prefix pair is decided by the
// length difference. The RESULT is clamped to -1/0/+1 -- net48's instance
// CompareTo (culture-aware string.Compare, netcore String.Comparison.cs:415)
// never reports the raw unit/length difference; only the sign. (The STATIC
// string.CompareOrdinal keeps the raw difference -- icall_string_compare_static.)
//------------------------------------------------------------------------------
int32_t icall_string_compare_to(Il2CppObject* this_, Il2CppObject* value)
{
    ThrowIfNullReceiver(this_);
    if (value == nullptr)
    {
        return 1; // net48: CompareTo(null) returns 1 (null sorts below any string)
    }
    const Il2CppString* s = AsString(this_);
    const Il2CppString* v = AsString(value);
    const int32_t common = (s->length < v->length) ? s->length : v->length;
    for (int32_t i = 0; i < common; ++i)
    {
        // The difference of two 16-bit units always fits in int32_t.
        const int32_t diff = static_cast<int32_t>(s->chars[i]) - static_cast<int32_t>(v->chars[i]);
        if (diff != 0)
        {
            return (diff < 0) ? -1 : 1; // net48 instance CompareTo: clamped sign
        }
    }
    if (s->length == v->length)
    {
        return 0;
    }
    return (s->length < v->length) ? -1 : 1; // prefix: clamped sign of the length diff
}

//------------------------------------------------------------------------------
// icall_string_contains -- Contains(string) (see header). Empty value is
// contained in every string (true); otherwise delegates to the ordinal
// IndexOf(string) search.
//------------------------------------------------------------------------------
bool icall_string_contains(Il2CppObject* this_, Il2CppObject* value)
{
    ThrowIfNullReceiver(this_);
    ThrowIfNullArgument(value, "value");
    return icall_string_index_of_s(this_, value) != -1;
}

//------------------------------------------------------------------------------
// icall_string_split -- Split(char[]) (see header). net48 semantics
// (csc-probe-verified 2026-08-09):
//   * null OR EMPTY 'separator' array -> split on WHITESPACE (the full
//     Char.IsWhiteSpace code-unit set below; net48 routes Split((char[])null)
//     and Split(new char[0]) through the whitespace overload).
//   * a non-empty separator array splits on every occurrence of any element.
//   * StringSplitOptions.None is assumed: empty segments are RETAINED --
//     consecutive, leading and trailing separators each contribute an empty
//     segment (always String.Empty, never null).
//   * the result is a string[] with exactly (number of separator units) + 1
//     elements; an empty receiver yields [""].
//
// GC safety (the header's two-phase discipline): the receiver payload and
// the separator units are STAGED into non-GC scratch (std::vector) BEFORE
// the first managed allocation -- a Gen0 compaction during the fill would
// move the unrooted receiver/separator objects, so no post-allocation code
// may read them. The result array is ROOTED for the whole fill (RootScope):
// the per-segment NewStringFromUnits allocations can trigger a collection
// that would otherwise reclaim the not-yet-returned array.
//------------------------------------------------------------------------------
Il2CppArray* icall_string_split(Il2CppString* self, Il2CppArray* separators)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }

    // --- Stage the receiver payload + separator units into non-GC scratch
    //     BEFORE any allocation (Gen0 compaction can move unrooted objects).
    const int32_t len = self->length;
    std::vector<char16_t> text(self->chars, self->chars + len);

    const bool whitespaceMode = (separators == nullptr || separators->length <= 0);
    std::vector<char16_t> seps;
    if (!whitespaceMode)
    {
        seps.reserve(static_cast<size_t>(separators->length));
        for (int32_t i = 0; i < separators->length; ++i)
        {
            seps.push_back(*reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(separators, i)));
        }
    }

    // --- Count the segments: every separator unit adds one.
    int32_t segmentCount = 1;
    for (int32_t i = 0; i < len; ++i)
    {
        const char16_t c = text[static_cast<size_t>(i)];
        if (whitespaceMode)
        {
            if (IsWhitespace(c)) ++segmentCount;
        }
        else
        {
            for (size_t s = 0; s < seps.size(); ++s)
            {
                if (seps[s] == c)
                {
                    ++segmentCount;
                    break;
                }
            }
        }
    }

    // --- Allocate the result array and ROOT it for the whole fill.
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(void*), true, segmentCount);
    RootScope root;
    root.Add(reinterpret_cast<void**>(&result));

    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(result->data);
    int32_t segmentStart = 0;
    int32_t w = 0;
    for (int32_t i = 0; i < len; ++i)
    {
        const char16_t c = text[static_cast<size_t>(i)];
        bool isSep = false;
        if (whitespaceMode)
        {
            isSep = IsWhitespace(c);
        }
        else
        {
            for (size_t s = 0; s < seps.size(); ++s)
            {
                if (seps[s] == c)
                {
                    isSep = true;
                    break;
                }
            }
        }
        if (isSep)
        {
            Il2CppString* seg = NewStringFromUnits(text.data() + segmentStart, i - segmentStart);
            slots[w++] = reinterpret_cast<Il2CppObject*>(seg);
            segmentStart = i + 1;
        }
    }
    Il2CppString* tail = NewStringFromUnits(text.data() + segmentStart, len - segmentStart);
    slots[w] = reinterpret_cast<Il2CppObject*>(tail);

    return result;
}

//------------------------------------------------------------------------------
// icall_string_split_options -- Split(char[]/string[], StringSplitOptions)
// (see header for the full contract). net48 semantics (csc-probe-verified
// 2026-08-09, %TEMP%\opencode\rtspl2-probe):
//   * 'options' must be 0 (None) or 1 (RemoveEmptyEntries); ANY other value
//     throws System.ArgumentException ("a,b" with (StringSplitOptions)2 or
//     -1 or 5 all throw; net48 validates the enum BEFORE splitting).
//   * null OR length-0 'separators' (char[] or string[]) -> the WHITESPACE
//     overload (the same full Char.IsWhiteSpace set as icall_string_split).
//   * a char[] separator array (isRefElem false) splits on every occurrence
//     of any code unit -- the per-unit semantics of the 1-arg split.
//   * a string[] separator array (isRefElem true) splits on every occurrence
//     of any ELEMENT as a multi-unit separator. Null and EMPTY elements are
//     SKIPPED (net48: Split("abc", new string[] { null }) == ["abc"],
//     Split("abc", new string[] { "" }) == ["abc"]). At each position the
//     separator that matches is the FIRST in array order (the net48 reference
//     algorithm walks the separator array and breaks on the first match,
//     advancing by that element's length -- non-overlapping greedy; the
//     { "a","aa" } vs { "aa","a" } ORDER probe pins this: "xaaay" splits to
//     4 vs 3 segments).
//   * None retains every segment (always String.Empty, never null);
//     RemoveEmptyEntries drops the empty ones. When no non-empty segment
//     survives, the result is a 0-LENGTH string[] ("a,,b" RemoveEmpty ->
//     ["a","b"]; ",," RemoveEmpty -> []; "" RemoveEmpty -> []; "" None -> [""]).
//
// GC safety (the header's two-phase discipline): the receiver payload AND
// every separator unit (char and string-element alike) are staged into
// non-GC scratch BEFORE the first managed allocation -- a Gen0 compaction
// during the fill would move the unrooted receiver/separator objects, so no
// post-allocation code may read them. The result array is ROOTED for the
// whole fill (RootScope): the per-segment NewStringFromUnits allocations can
// trigger a collection that would otherwise reclaim the not-yet-returned
// array.
//------------------------------------------------------------------------------
Il2CppArray* icall_string_split_options(Il2CppString* self, Il2CppArray* separators, int32_t options)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    if (options != 0 && options != 1)
    {
        // net48 validates the StringSplitOptions enum value before splitting:
        // anything other than None(0) / RemoveEmptyEntries(1) is an
        // ArgumentException. Static diagnostic (the exception carrier keeps
        // the pointer -- never freed -- so a dynamic buffer would dangle).
        il2cpp_throw_new("System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception",
                         "Invalid enum value for StringSplitOptions.");
    }

    // --- Stage the receiver payload + separator units into non-GC scratch
    //     BEFORE any allocation (Gen0 compaction can move unrooted objects).
    const int32_t len = self->length;
    std::vector<char16_t> text(self->chars, self->chars + len);

    const bool whitespaceMode = (separators == nullptr || separators->length <= 0);
    const bool stringMode = !whitespaceMode && separators->isRefElem;
    std::vector<char16_t> seps;             // char[] separators (per-unit)
    std::vector<char16_t> strUnits;         // string[] separators: flat unit store
    std::vector<int32_t> strLens;           // string[] separators: per-element lengths
    if (!whitespaceMode)
    {
        if (stringMode)
        {
            for (int32_t i = 0; i < separators->length; ++i)
            {
                Il2CppObject* elem = *reinterpret_cast<Il2CppObject**>(
                    il2cpp_array_get_elem_ptr(separators, i));
                if (elem == nullptr)
                {
                    continue; // null elements are skipped (net48)
                }
                const Il2CppString* s = AsString(elem);
                if (s->length <= 0)
                {
                    continue; // empty-string elements are skipped (net48)
                }
                strLens.push_back(s->length);
                strUnits.insert(strUnits.end(), s->chars, s->chars + s->length);
            }
        }
        else
        {
            seps.reserve(static_cast<size_t>(separators->length));
            for (int32_t i = 0; i < separators->length; ++i)
            {
                seps.push_back(*reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(separators, i)));
            }
        }
    }

    // --- First pass: compute the RAW segments (start + length) without any
    //     managed allocation. The scan is per-position; a separator match
    //     closes [start, i) and advances the window by the matched length.
    std::vector<int32_t> segStart;
    std::vector<int32_t> segLen;
    segStart.reserve(static_cast<size_t>(len) + 1);
    segLen.reserve(static_cast<size_t>(len) + 1);
    int32_t i = 0;
    int32_t start = 0;
    while (i < len)
    {
        int32_t matchLen = 0; // 0 = no separator matches at position i
        if (whitespaceMode)
        {
            if (IsWhitespace(text[static_cast<size_t>(i)])) matchLen = 1;
        }
        else if (stringMode)
        {
            int32_t off = 0;
            for (size_t k = 0; k < strLens.size(); ++k)
            {
                const int32_t sl = strLens[k];
                if (i + sl <= len)
                {
                    bool eq = true;
                    for (int32_t u = 0; u < sl; ++u)
                    {
                        if (text[static_cast<size_t>(i + u)] != strUnits[static_cast<size_t>(off + u)])
                        {
                            eq = false;
                            break;
                        }
                    }
                    if (eq)
                    {
                        matchLen = sl; // FIRST match in array order wins (net48)
                        break;
                    }
                }
                off += sl;
            }
        }
        else
        {
            const char16_t c = text[static_cast<size_t>(i)];
            for (size_t s = 0; s < seps.size(); ++s)
            {
                if (seps[s] == c)
                {
                    matchLen = 1;
                    break;
                }
            }
        }
        if (matchLen > 0)
        {
            segStart.push_back(start);
            segLen.push_back(i - start);
            i += matchLen;
            start = i;
        }
        else
        {
            ++i;
        }
    }
    segStart.push_back(start);
    segLen.push_back(len - start);

    // --- Apply StringSplitOptions to the segment list: None keeps every
    //     segment; RemoveEmptyEntries drops the empty ones (an all-empty
    //     split yields a 0-length array, never ["\""\""]).
    const bool removeEmpty = (options == 1);
    int32_t finalCount = 0;
    for (size_t s = 0; s < segStart.size(); ++s)
    {
        if (!removeEmpty || segLen[s] > 0) ++finalCount;
    }

    // --- Allocate the result array and ROOT it for the whole fill.
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(void*), true, finalCount);
    RootScope root;
    root.Add(reinterpret_cast<void**>(&result));

    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(result->data);
    int32_t w = 0;
    for (size_t s = 0; s < segStart.size(); ++s)
    {
        if (removeEmpty && segLen[s] == 0) continue;
        const char16_t* base = (segLen[s] > 0) ? text.data() + segStart[s] : nullptr;
        slots[w++] = reinterpret_cast<Il2CppObject*>(NewStringFromUnits(base, segLen[s]));
    }

    return result;
}

//------------------------------------------------------------------------------
// net9/10 E2E icallcase: String.Insert / String.Remove -- pure string
// surgery on the UTF-16 unit array (net48 contract: Insert index must be
// within [0, Length]; Remove startIndex within [0, Length], count >= 0 and
// startIndex+count <= Length; out-of-range -> ArgumentOutOfRangeException).
//------------------------------------------------------------------------------
Il2CppString* icall_string_insert(Il2CppString* self, int32_t index, Il2CppString* value)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    if (value == nullptr)
        il2cpp_throw_argument_null_exception("value");
    if (index < 0 || index > self->length)
        il2cpp_throw_argument_out_of_range_exception("index", "Index must be within the string bounds.");
    const int32_t outLen = self->length + value->length;
    Il2CppString* out = NewStringFromUnits(nullptr, outLen);
    for (int32_t i = 0; i < index; ++i)
        out->chars[i] = self->chars[i];
    for (int32_t i = 0; i < value->length; ++i)
        out->chars[index + i] = value->chars[i];
    for (int32_t i = index; i < self->length; ++i)
        out->chars[value->length + i] = self->chars[i];
    return out;
}

Il2CppString* icall_string_remove(Il2CppString* self, int32_t startIndex, int32_t count)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    if (startIndex < 0 || startIndex > self->length)
        il2cpp_throw_argument_out_of_range_exception("startIndex", "StartIndex must be within the string bounds.");
    if (count < 0 || count > self->length - startIndex)
        il2cpp_throw_argument_out_of_range_exception("count", "Count must be within the remaining length.");
    const int32_t outLen = self->length - count;
    Il2CppString* out = NewStringFromUnits(nullptr, outLen);
    for (int32_t i = 0; i < startIndex; ++i)
        out->chars[i] = self->chars[i];
    for (int32_t i = startIndex; i < outLen; ++i)
        out->chars[i] = self->chars[i + count];
    return out;
}

// net9/10 E2E campaign: Remove(int startIndex) -- removes through the END
// (the emitter cannot compute length-startIndex for the 2-arg form).
Il2CppString* icall_string_remove_to_end(Il2CppString* self, int32_t startIndex)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    if (startIndex < 0 || startIndex > self->length)
        il2cpp_throw_argument_out_of_range_exception("startIndex", "StartIndex must be within the string bounds.");
    return icall_string_remove(self, startIndex, self->length - startIndex);
}

//------------------------------------------------------------------------------
// net9/10 E2E campaign: CopyTo(srcIndex, dest, destIndex, count) -- copies
// 'count' UTF-16 units from this string into the caller's char[].
// net48 validation order: source null -> ANE; destination null -> ANE;
// srcIndex/destIndex/count out of range -> ArgumentOutOfRange.
//------------------------------------------------------------------------------
void icall_string_copy_to_array(Il2CppString* self, int32_t srcIndex,
                                Il2CppArray* dest, int32_t destIndex, int32_t count)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    if (dest == nullptr)
        il2cpp_throw_argument_null_exception("destination");
    if (srcIndex < 0 || destIndex < 0 || count < 0)
        il2cpp_throw_argument_out_of_range_exception("count", "Negative indices or count.");
    if (srcIndex + count > self->length || destIndex + count > static_cast<int32_t>(dest->length))
        il2cpp_throw_argument_out_of_range_exception("count", "Ranges exceed the string or array bounds.");
    auto* dst = reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(dest, 0));
    for (int32_t i = 0; i < count; ++i)
        dst[destIndex + i] = self->chars[srcIndex + i];
}

//------------------------------------------------------------------------------
// net9/10 E2E campaign (string sweep): String.ToCharArray() -- a NEW char[]
// holding a copy of the UTF-16 units (the caller may mutate it freely; the
// source string is never affected). Empty string -> zero-length array.
//------------------------------------------------------------------------------
Il2CppArray* icall_string_to_char_array(Il2CppString* self)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    const int32_t len = self->length;
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(char16_t), false, len);
    auto* dst = reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(out, 0));
    for (int32_t i = 0; i < len; ++i)
        dst[i] = self->chars[i];
    return out;
}

//------------------------------------------------------------------------------
// net9/10 E2E campaign: IndexOfAny/LastIndexOfAny 1- and 2-arg forms --
// thin wrappers over the ext3 range icalls with the default window
// ([0, Length) / [startIndex, Length)). Validation lives in the range
// implementations (null anyOf -> ANE; out-of-range -> AOORE).
//------------------------------------------------------------------------------
int32_t icall_string_index_of_any(Il2CppObject* self, Il2CppObject* anyOf)
{
    auto* s = reinterpret_cast<Il2CppString*>(self);
    return icall_string_index_of_any_range(self, anyOf, 0, s->length);
}

int32_t icall_string_index_of_any_start(Il2CppObject* self, Il2CppObject* anyOf, int32_t startIndex)
{
    auto* s = reinterpret_cast<Il2CppString*>(self);
    return icall_string_index_of_any_range(self, anyOf, startIndex, s->length - startIndex);
}

int32_t icall_string_last_index_of_any(Il2CppObject* self, Il2CppObject* anyOf)
{
    auto* s = reinterpret_cast<Il2CppString*>(self);
    return icall_string_last_index_of_any_range(self, anyOf, s->length - 1, s->length);
}

int32_t icall_string_last_index_of_any_start(Il2CppObject* self, Il2CppObject* anyOf, int32_t startIndex)
{
    return icall_string_last_index_of_any_range(self, anyOf, startIndex, startIndex + 1);
}

//------------------------------------------------------------------------------
// net9/10 E2E campaign: Contains(char) -- ordinal scan for one UTF-16 unit
// (the net9 char overload; the string overload already routes to
// icall_string_contains).
//------------------------------------------------------------------------------
bool icall_string_contains_char(Il2CppString* self, char16_t value)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    for (int32_t i = 0; i < self->length; ++i)
    {
        if (self->chars[i] == value)
            return true;
    }
    return false;
}

//------------------------------------------------------------------------------
// net9/10 E2E campaign: ReplaceLineEndings(replacementText) -- normalizes
// CRLF / CR / LF sequences to 'replacementText' (net48 contract: CRLF is
// ONE break, a lone CR or LF is one break). The 0-arg overload passes
// Environment.NewLine from the caller side is impossible (no injection) --
// so the no-replacement variant lives in environment.cpp where the newline
// getter is in scope; THIS entry takes the explicit replacement.
//------------------------------------------------------------------------------
Il2CppString* icall_string_replace_line_endings(Il2CppString* self, Il2CppString* replacement)
{
    if (self == nullptr)
        il2cpp_throw_argument_null_exception("self");
    if (replacement == nullptr)
        il2cpp_throw_argument_null_exception("replacementText");

    // Pass 1: count output units.
    int32_t outLen = 0;
    for (int32_t i = 0; i < self->length; ++i)
    {
        if (self->chars[i] == u'\r')
        {
            outLen += replacement->length;
            if (i + 1 < self->length && self->chars[i + 1] == u'\n') ++i; // CRLF = one break
        }
        else if (self->chars[i] == u'\n')
        {
            outLen += replacement->length;
        }
        else
        {
            ++outLen;
        }
    }

    // Pass 2: emit.
    Il2CppString* out = NewStringFromUnits(nullptr, outLen);
    int32_t w = 0;
    for (int32_t i = 0; i < self->length; ++i)
    {
        if (self->chars[i] == u'\r')
        {
            if (i + 1 < self->length && self->chars[i + 1] == u'\n') ++i;
            for (int32_t k = 0; k < replacement->length; ++k) out->chars[w++] = replacement->chars[k];
        }
        else if (self->chars[i] == u'\n')
        {
            for (int32_t k = 0; k < replacement->length; ++k) out->chars[w++] = replacement->chars[k];
        }
        else
        {
            out->chars[w++] = self->chars[i];
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// net9/10 E2E campaign: Join separator start/count forms -- slice the
// values array into a fresh reference sub-array [startIndex,
// startIndex+count), then delegate to the existing whole-array join icall.
// net48 validation: null array -> ANE; startIndex/count out of range ->
// ArgumentOutOfRange.
//------------------------------------------------------------------------------
static Il2CppArray* SliceRefArray(Il2CppArray* values, int32_t startIndex, int32_t count)
{
    if (values == nullptr)
        il2cpp_throw_argument_null_exception("value");
    if (startIndex < 0 || count < 0 || startIndex + count > static_cast<int32_t>(values->length))
        il2cpp_throw_argument_out_of_range_exception("startIndex", "StartIndex and Count must be within the array bounds.");
    Il2CppArray* sub = il2cpp_array_new(nullptr, sizeof(Il2CppObject*), true, count);
    for (int32_t i = 0; i < count; ++i)
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(sub, i)) =
            *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(values, startIndex + i));
    return sub;
}

Il2CppString* icall_string_join_range_s(Il2CppString* separator, Il2CppArray* values, int32_t startIndex, int32_t count)
{
    Il2CppArray* sub = SliceRefArray(values, startIndex, count);
    return reinterpret_cast<Il2CppString*>(
        icall_string_join(separator, reinterpret_cast<Il2CppObject*>(sub)));
}

Il2CppString* icall_string_join_range_c(char16_t separator, Il2CppArray* values, int32_t startIndex, int32_t count)
{
    Il2CppArray* sub = SliceRefArray(values, startIndex, count);
    // The char-separator join lives in the concat domain (il2cpp_concat.h).
    return il2cpp_string_builder_join(separator, reinterpret_cast<Il2CppObject*>(sub));
}

} // namespace runtime
} // namespace il2cpp
