//------------------------------------------------------------------------------
// stringext2.cpp -- System.String STATIC method icalls (see
// il2cpp_string_ext2.h for the full contract and known deviations).
//
// Design notes:
//   * Parameter convention matches the other string icalls: managed strings
//     cross the boundary as Il2CppObject* and are cast internally (AsString).
//   * All results are produced with the SAME size-extended allocation path
//     string.cpp uses (il2cpp_string_new), so every produced string is a
//     first-class GC object with the correct physical block extent.
//   * Single-allocation discipline (mirrors concat.cpp): every managed
//     payload read happens BEFORE the call's only managed allocation, because
//     il2cpp_gc_alloc_size can trigger a collection (collect-then-retry) and
//     an unrooted input could be reclaimed mid-call. Concat and join stage
//     their content in the non-GC pool scratch first (il2cpp_alloc_fast, an
//     O(1) bump that can never collect); copy stages the payload the same
//     way. The numeric concat conveniences are the documented exception (see
//     the header's GC-ROOTING CONTRACT): the ToString allocation precedes the
//     read of the string argument.
//   * Exceptions travel as il2cpp::DotNetException via the exceptionext
//     factories (full "{Type},{Type},{Base1},..." chains -- the catch matcher
//     walks starting after the first comma, so the type is repeated).
//------------------------------------------------------------------------------

#include "il2cpp_string_ext2.h"

#include "il2cpp_allocator.h"
#include "il2cpp_concat.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_format.h"
#include "il2cpp_string_intern.h"
#include "il2cpp_value_semantics.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// Cast a managed string parameter (Il2CppObject*) to its payload view.
Il2CppString* AsString(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppString*>(obj);
}

// Element of a reference array (null slot -> null).
Il2CppObject* ArrayElement(Il2CppArray* array, int32_t index)
{
    return *reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(array, index));
}

// Is 'obj' a managed string? NAME comparison (not klass pointer equality)
// follows valuesemantics.cpp's IsString: the runtime has two System.String
// descriptors (the allocator's g_stringClass and the metadata
// System_String_class), both named "System.String", and either may appear.
bool IsStringObject(Il2CppObject* obj)
{
    return obj->klass != nullptr &&
           std::strcmp(obj->klass->name, "System.String") == 0;
}

// An array element as its join segment (null slot -> null). Strings pass
// through BY REFERENCE (identity); every other element is converted with
// icall_object_to_string -- .NET Join(object[]) calls ToString() on each
// element, so boxed primitives format their VALUE (format.cpp icalls) and
// any other object prints its type's full name. The conversion may ALLOCATE
// (a ToString result is a new managed string), so callers must keep the
// array AND the separator reachable for the whole call (the numeric-concat
// convenience exception, see the header's GC-ROOTING CONTRACT).
Il2CppString* ElementAsSegment(Il2CppObject* elem)
{
    if (elem == nullptr)
    {
        return nullptr;
    }
    if (IsStringObject(elem))
    {
        return reinterpret_cast<Il2CppString*>(elem);
    }
    return icall_object_to_string(elem);
}

// A VALUE-array element (byte[]/int32[]/char16[]/int64[]) formatted with its
// net48 ToString() text -- the generic string.Join<T>(separator, values)
// shape (System/String.Manipulation.cs JoinCore, the ISpanFormattable fast
// path: each element's ToString is appended between separators). The stride
// picks the width: 1 -> byte, 2 -> UTF-16 unit (char16_t), 4 -> int32, 8 ->
// int64. The conversion ALLOCATES (the ToString result is a managed string),
// so callers must keep the array reachable for the whole call (same
// GC-ROOTING CONTRACT as ElementAsSegment). Unsupported strides (defensive)
// yield an empty segment -- never an out-of-bounds read.
Il2CppString* ValueArrayElement(Il2CppArray* array, int32_t index)
{
    const char* base = array->data + static_cast<size_t>(index) * static_cast<size_t>(array->elemSize);
    switch (array->elemSize)
    {
    case 1:
        return icall_int32_to_string(static_cast<int32_t>(static_cast<uint8_t>(*base)));
    case 2:
        return icall_char_to_string(*reinterpret_cast<const char16_t*>(base));
    case 4:
        return icall_int32_to_string(*reinterpret_cast<const int32_t*>(base));
    case 8:
        return icall_int64_to_string(*reinterpret_cast<const int64_t*>(base));
    default:
        return nullptr; // defensive: unsupported stride -> empty segment
    }
}

// The join segment for element 'i': reference arrays go through
// ArrayElement + ElementAsSegment (string identity / object ToString); value
// arrays (generic Join<T>) go through ValueArrayElement.
Il2CppString* JoinElementSegment(Il2CppArray* array, int32_t index, bool isRef)
{
    if (isRef)
    {
        return ElementAsSegment(ArrayElement(array, index));
    }
    return ValueArrayElement(array, index);
}

// Null-safe segment length: null counts as an empty string.
int32_t SegmentLength(const Il2CppString* seg)
{
    return seg != nullptr ? seg->length : 0;
}

// .NET Char.IsWhiteSpace: the exact code-unit set the BCL treats as
// whitespace (control whitespace + space + the Unicode Zs/Zl/Zp spaces).
// Listed literally so the runtime never depends on the host C++ locale.
//
// DUPLICATE NOTE: stringext.cpp's anonymous namespace already holds an
// identical IsWhitespace (stringext.cpp:49). It is unreachable from this TU
// (anonymous namespace = internal linkage) and stringext.cpp is read-only
// per domain constraints, so the set is reproduced here; the two copies must
// stay in sync when the .NET whitespace set changes.
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

// The largest length a managed string can carry (.NET string.Length).
constexpr int32_t kMaxStringLength = 2147483647;

// Abort with a managed OverflowException when a join's total length exceeds
// Int32.MaxValue (the same guard concat.cpp uses; unreachable in practice).
void ThrowLengthOverflow()
{
    il2cpp_throw_new("System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                     "The concatenated string length exceeds Int32.MaxValue");
}

// Ordinal comparison of two NON-NULL payloads (static Compare core),
// replicating net48's CompareOrdinalHelper EXACT value-for-value
// (fresh-oracle verified 2026-08-08, see GAPS.md DEFECT-STR2B-1).
//
// net48 reads the payloads in 2-UNIT pairs (a native 32-bit load) and the
// managed strings are null-terminated, so the unit PAST a string's end reads
// as 0. On a pair mismatch the FIRST differing unit of the pair decides (the
// raw unit difference is the result); otherwise the scan advances by 2. The
// scan runs AT LEAST ONCE (a do-while): a zero common length still compares
// the boundary pair, so e.g. CompareOrdinal("", "a") == -97 (0 - 'a'), NOT
// the length difference -1. After the loop the length difference decides the
// still-equal prefix pair. This reproduces net48's opaque mix of raw
// boundary-unit differences and plain length differences exactly:
//   CompareOrdinal("",  "a")     == -97    (0 - 'a')
//   CompareOrdinal("ab", "a")    ==  98    ('b' - 0)
//   CompareOrdinal("abc","ab")   ==   1    (length difference)
//   CompareOrdinal("abc","abcd") == -100   (0 - 'd')
// Bounds-checked reads substitute 0 for out-of-range units -- the runtime's
// Il2CppString payload has no guaranteed terminator, so a real read past
// `length` would be a heap overrun.
int32_t ComparePayloads(const Il2CppString* a, const Il2CppString* b)
{
    int32_t common = (a->length < b->length) ? a->length : b->length;
    int32_t i = 0;
    do
    {
        const int32_t a0 = (i < a->length) ? static_cast<int32_t>(a->chars[i]) : 0;
        const int32_t b0 = (i < b->length) ? static_cast<int32_t>(b->chars[i]) : 0;
        if (a0 != b0)
        {
            return a0 - b0; // first unit of the pair decides
        }
        const int32_t a1 = (i + 1 < a->length) ? static_cast<int32_t>(a->chars[i + 1]) : 0;
        const int32_t b1 = (i + 1 < b->length) ? static_cast<int32_t>(b->chars[i + 1]) : 0;
        if (a1 != b1)
        {
            return a1 - b1; // second unit of the pair decides
        }
        i += 2;
        common -= 2;
    } while (common > 0);
    return a->length - b->length; // still-equal prefix: the length difference
}

// ComparePayloads with the ASCII fold (icall_string_compare_mode core): the
// SAME CompareOrdinalHelper pair-scan and boundary-unit-as-0 rule, with each
// unit folded to lowercase (A-Z -> a-z) BEFORE comparing when 'ignoreCase'.
// The boundary pins hold for the folded scan too -- Compare("ab", "A",
// OrdinalIgnoreCase) == ('b' - 0) = 98, and the unit-past-the-end is never
// folded (0 stays 0), exactly net48's null-terminator reads.
static int32_t ComparePayloadsFold(const Il2CppString* a, const Il2CppString* b,
                                   bool ignoreCase)
{
    int32_t common = (a->length < b->length) ? a->length : b->length;
    int32_t i = 0;
    auto fold = [ignoreCase](char16_t c) -> int32_t
        {
            if (ignoreCase && c >= u'A' && c <= u'Z')
            {
                return static_cast<int32_t>(c - u'A' + u'a');
            }
            return static_cast<int32_t>(c);
        };
    do
    {
        const int32_t a0 = (i < a->length) ? fold(a->chars[i]) : 0;
        const int32_t b0 = (i < b->length) ? fold(b->chars[i]) : 0;
        if (a0 != b0)
        {
            return a0 - b0; // first unit of the pair decides
        }
        const int32_t a1 = (i + 1 < a->length) ? fold(a->chars[i + 1]) : 0;
        const int32_t b1 = (i + 1 < b->length) ? fold(b->chars[i + 1]) : 0;
        if (a1 != b1)
        {
            return a1 - b1; // second unit of the pair decides
        }
        i += 2;
        common -= 2;
    } while (common > 0);
    return a->length - b->length; // still-equal prefix: the length difference
}

// string.Copy core: a NEW instance with equal content. The payload is staged
// into non-GC scratch BEFORE the single managed allocation (see header), so
// a mid-call collection can never reclaim the unrooted source. The empty
// string still yields a fresh instance (.NET String.Copy semantics).
Il2CppString* CopyFromString(const Il2CppString* src)
{
    if (src == nullptr || src->length == 0)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    char16_t* scratch = static_cast<char16_t*>(alloc::il2cpp_alloc_fast(
        static_cast<size_t>(src->length) * sizeof(char16_t)));
    std::memcpy(scratch, src->chars, static_cast<size_t>(src->length) * sizeof(char16_t));
    Il2CppString* result = il2cpp_string_new(scratch, src->length);
    alloc::il2cpp_free_fast(scratch);
    return result;
}

// D-STR2-1 (net48 first-element-null shortcut): whether a REFERENCE array is
// an OBJECT[] (System.Object[] or any non-string reference array) rather than
// a string[]. net48's object[] Join returns String.Empty when the FIRST
// element is null (reference source: `if (values.Length == 0 || values[0] ==
// null) return String.Empty;`) while the string[] overload has NO such
// shortcut (a null first element joins as an empty segment: Join(",", new
// string[]{null,"x"}) == ",x", fresh-oracle verified 2026-08-12). The
// transpiler emits every reference array WITHOUT element-type metadata
// (generated code calls il2cpp_array_new with a null element descriptor), so
// when the descriptor is missing the array is classified by CONTENT: any
// non-null non-string element proves an object[]. A descriptor naming
// "System.String" proves string[]; any other descriptor (System.Object, a
// custom class) is object[]-like. RESIDUE (recorded D-STR2-1): an object[]
// whose non-null elements are ALL strings (e.g. new object[]{null,"x"}) is
// indistinguishable from the same-shaped string[] when the descriptor is
// missing -- the runtime keeps the string[] semantics for that shape.
bool IsObjectArray(Il2CppArray* array)
{
    if (array->elemClass != nullptr && array->elemClass->name != nullptr)
    {
        return std::strcmp(array->elemClass->name, "System.String") != 0;
    }
    for (int32_t i = 0; i < array->length; ++i)
    {
        Il2CppObject* elem = ArrayElement(array, i);
        if (elem != nullptr && !IsStringObject(elem))
        {
            return true; // a non-string element proves an object[] array
        }
    }
    return false; // every non-null element is a string (string[] or ambiguous)
}

// 2026-09-23 (perf): the 1MB Gen0 segment makes large arrays (64KB..1MB,
// previously oversized/non-moving) ordinary MOVABLE young objects. A
// collection during JoinFromArray's pass-1 element conversions can therefore
// move the array/separator under this function; the RAII scope roots both so
// Phase C rewrites the locals, and unregisters on every exit path (including
// the length-overflow throw). Same shape as stringbuilder.cpp's RootScope
// (keep the two copies in sync).
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
        // nb9 H-B1: guard the fixed slot table (an overflow corrupts m_count).
        if (m_count >= static_cast<int32_t>(sizeof(m_slots) / sizeof(m_slots[0])))
        {
            std::abort();
        }
        il2cpp_gc_add_static_root(slot);
        m_slots[m_count++] = slot;
    }

private:
    void** m_slots[4];
    int32_t m_count;
};

// string.Join core: separator (a string, possibly null -> empty) BETWEEN
// consecutive elements only, never leading/trailing. Elements are strings
// or null, OR any object reachable through the object[] overload -- the
// latter are converted with icall_object_to_string (.NET Join(object[])
// calls ToString() on each element; see ElementAsSegment). A VALUE array
// (byte[]/int32[]/char16[]/int64[] -- the generic Join<T> shape, mapped to
// this same icall) formats each element with its ToString() text. Three-stage
// shape (mirrors concat.cpp's ConcatFromArray): pass 1 sums the total
// length in 64-bit arithmetic, the payloads are staged into non-GC scratch,
// then ONE il2cpp_string_new finishes. Null elements count as empty; a
// single non-null STRING element returns the element itself (identity, zero
// allocations); a single non-string element returns its ToString() text (a
// new string); an empty array yields an empty string; a
// NULL array throws ArgumentNullException (.NET semantics -- net48 probe:
// the string[] overload's parameter is named "value", the object[]'s
// "values"; the runtime sees both shapes through the same icall, so the
// string[] name is used). D-STR2-1: an object[] whose FIRST element is null
// yields String.Empty (net48 object[] overload shortcut), NOT a join with a
// leading separator -- the string[] overload instead joins a null first
// element as an empty segment (see IsObjectArray).
Il2CppString* JoinFromArray(Il2CppArray* array, const Il2CppString* sep)
{
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    // Root the array + separator for the whole call: pass-1 element
    // conversions allocate, and a collection can move them (1MB segments
    // made large arrays movable); Phase C rewrites these slots. The
    // separator is const at the call site, so root a non-const alias.
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&array));
    Il2CppString* sepAlias = const_cast<Il2CppString*>(sep);
    scope.Add(reinterpret_cast<void**>(&sepAlias));
    sep = sepAlias;
    if (array->length <= 0)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    const bool isRef = array->isRefElem;
    const int32_t count = array->length;
    // net48 object[] first-element-null shortcut (D-STR2-1, fresh-oracle
    // verified 2026-08-12): Join(",", new object[]{null,"x"}) == "" -- never
    // ",x". The string[] overload keeps the empty-segment behavior, so only
    // object[]-classified arrays take the shortcut (see IsObjectArray).
    if (isRef && count > 0 && ArrayElement(array, 0) == nullptr && IsObjectArray(array))
    {
        return il2cpp_string_new(nullptr, 0);
    }
    if (count == 1)
    {
        // Single-element fast path: a STRING element returns itself
        // (identity, zero allocations -- .NET Join(single string)); a
        // non-string element returns its ToString() text (a new string,
        // never the box itself); a single value element formats its value.
        if (isRef)
        {
            Il2CppObject* only = ArrayElement(array, 0);
            if (only == nullptr)
            {
                return il2cpp_string_new(nullptr, 0);
            }
            if (IsStringObject(only))
            {
                return reinterpret_cast<Il2CppString*>(only);
            }
            return icall_object_to_string(only);
        }
        return ValueArrayElement(array, 0);
    }
    const int32_t sepLen = SegmentLength(sepAlias);

    // Pass 1: total length in 64-bit arithmetic (overflow-safe accumulation).
    // Every element length is read HERE, before any allocation of this call.
    // Non-string elements are converted with the per-element segment helper --
    // each conversion may allocate (a ToString result), so the caller must keep
    // the array and the separator reachable for the whole call (documented
    // exception, see the header). Only the LENGTH is retained: the pass-1
    // conversion results are unreferenced garbage once a later conversion
    // collects them, so pass 2 re-converts instead of caching pointers.
    int64_t total = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        total += SegmentLength(JoinElementSegment(array, i, isRef));
    }
    total += static_cast<int64_t>(sepLen) * (static_cast<int64_t>(count) - 1);
    if (total > kMaxStringLength)
    {
        ThrowLengthOverflow();
    }
    if (total == 0)
    {
        return il2cpp_string_new(nullptr, 0);
    }

    // Stage: copy all payloads and separators into non-GC memory BEFORE the
    // single managed allocation (see the header's GC-safety contract). Each
    // non-string element is re-converted and its payload copied IMMEDIATELY
    // (no allocation between conversion and copy), so a later conversion can
    // never reclaim content that is already staged.
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(static_cast<size_t>(total) * sizeof(char16_t)));
    size_t offset = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        const Il2CppString* seg = JoinElementSegment(array, i, isRef);
        if (seg != nullptr && seg->length > 0)
        {
            std::memcpy(scratch + offset, seg->chars,
                        static_cast<size_t>(seg->length) * sizeof(char16_t));
            offset += static_cast<size_t>(seg->length);
        }
        if (i + 1 < count && sepLen > 0)
        {
            std::memcpy(scratch + offset, sepAlias->chars,
                        static_cast<size_t>(sepLen) * sizeof(char16_t));
            offset += static_cast<size_t>(sepLen);
        }
    }
    // Finish: the ONLY managed allocation of this call.
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return result;
}

//------------------------------------------------------------------------------
// Comparison-mode helpers for icall_string_equals_culture below.
// DUPLICATE NOTE: stringext3.cpp's anonymous namespace holds identical
// helpers (ThrowIfNullReceiver / ThrowIfInvalidComparison / the
// StringComparison enum / IsIgnoreCaseMode / FoldUnit / UnitsEqual; its
// ThrowIfNullArgument is NOT reproduced here -- Equals is null-aware for
// 'value' and never throws ArgumentNullException). They are unreachable from
// this TU (anonymous namespace = internal linkage) and stringext3.cpp is
// read-only per domain constraints, so the comparison-mode set is reproduced
// here; the two copies must stay in sync when the StringComparison contract
// changes.
//------------------------------------------------------------------------------

// Null-receiver guard: a .NET instance call on null is a
// NullReferenceException (the defensive contract of stringext.cpp).
void ThrowIfNullReceiver(Il2CppObject* this_)
{
    if (this_ == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
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

// ASCII case folding for the culture ignore-case comparison modes:
// 'A'-'Z' -> 'a'-'z'; every other unit is returned unchanged. This is the
// ordinal/ASCII-fold fallback documented in the header W7 collation record
// (culture modes degrade to the fold; net48's OS-NLS collation is not
// replicated). Note ToUpperUnit/ToLowerUnit in stringext.cpp are culture-wide
// (stringcase.cpp tables) and are NOT the mapping used here.
char16_t FoldUnit(char16_t c)
{
    return (c >= u'A' && c <= u'Z') ? static_cast<char16_t>(c + 32) : c;
}

// Unit-wise equality under a comparison mode (see IsIgnoreCaseMode).
bool UnitsEqual(char16_t a, char16_t b, bool ignoreCase)
{
    return ignoreCase ? (FoldUnit(a) == FoldUnit(b)) : (a == b);
}

//------------------------------------------------------------------------------
// PERF1 concat fast paths -- fixed-arity string concatenation with ONE managed
// allocation and ONE non-GC scratch bump (no intermediate strings, no
// per-segment class-name strcmp / re-walk like the variadic concat_many).
// The scratch staging is MANDATORY (see il2cpp_concat.h GC SAFETY): every
// managed payload is copied into non-GC memory BEFORE the single
// il2cpp_string_new, so a mid-call collection can never reclaim the inputs.
//------------------------------------------------------------------------------

// net48 String.Empty -- the INTERNED empty string (same helper intent as
// concat.cpp's EmptyString; the concat.cpp copy is internal to that TU).
Il2CppString* ConcatEmptyString()
{
    return reinterpret_cast<Il2CppString*>(il2cpp_string_intern_literal(""));
}

// Format 'value' as UTF-16 decimal units into 'out' (must hold 16 units).
// Returns the unit count; NOT null-terminated. INT32_MIN is handled without
// UB (unsigned negation -- mirrors format.cpp's FormatInt64Decimal).
int FormatInt32Utf16(char16_t* out, int32_t value)
{
    if (value == 0)
    {
        out[0] = u'0';
        return 1;
    }
    const bool negative = value < 0;
    const uint32_t magnitude = negative
        ? (static_cast<uint32_t>(-(value + 1)) + 1u)
        : static_cast<uint32_t>(value);
    char16_t tmp[11];
    int n = 0;
    uint32_t mag = magnitude;
    do
    {
        tmp[n++] = static_cast<char16_t>(u'0' + static_cast<int>(mag % 10u));
        mag /= 10u;
    } while (mag != 0u);
    int len = 0;
    if (negative)
    {
        out[len++] = u'-';
    }
    while (n > 0)
    {
        out[len++] = tmp[--n];
    }
    return len;
}

// Concatenate 'count' fixed Il2CppString* segments (null -> empty) into ONE
// managed string. net48 semantics: an all-empty result -> the interned empty
// string; exactly ONE non-empty segment -> that segment by reference (zero
// allocations). Complexity: 1 managed + 1 scratch, ONE payload pass.
Il2CppString* ConcatFixed(const Il2CppString* const* segs, int32_t count)
{
    int64_t total = 0;
    int32_t nonEmptyCount = 0;
    const Il2CppString* only = nullptr;
    for (int32_t i = 0; i < count; ++i)
    {
        const Il2CppString* seg = segs[i];
        if (seg != nullptr && seg->length > 0)
        {
            ++nonEmptyCount;
            only = seg;
        }
        total += seg != nullptr ? seg->length : 0;
    }
    if (total > kMaxStringLength)
    {
        ThrowLengthOverflow();
    }
    if (total == 0)
    {
        return ConcatEmptyString();
    }
    if (nonEmptyCount == 1)
    {
        return const_cast<Il2CppString*>(only);
    }
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(static_cast<size_t>(total) * sizeof(char16_t)));
    size_t offset = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        const Il2CppString* seg = segs[i];
        if (seg != nullptr && seg->length > 0)
        {
            std::memcpy(scratch + offset, seg->chars,
                        static_cast<size_t>(seg->length) * sizeof(char16_t));
            offset += static_cast<size_t>(seg->length);
        }
    }
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return result;
}

// string + decimal-number fast path: format the number digits inline into the
// same scratch as the string payload -- ONE managed allocation total (no
// intermediate number string, no concat_many re-walk).
Il2CppObject* ConcatWithInt32(Il2CppObject* a, int32_t value)
{
    const Il2CppString* sa = AsString(a);
    const int32_t lenA = sa != nullptr ? sa->length : 0;
    char16_t digits[16];
    const int numLen = FormatInt32Utf16(digits, value);
    const int64_t total = static_cast<int64_t>(lenA) + numLen;
    if (total > kMaxStringLength)
    {
        ThrowLengthOverflow();
    }
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(static_cast<size_t>(total) * sizeof(char16_t)));
    if (lenA > 0)
    {
        std::memcpy(scratch, sa->chars, static_cast<size_t>(lenA) * sizeof(char16_t));
    }
    std::memcpy(scratch + lenA, digits, static_cast<size_t>(numLen) * sizeof(char16_t));
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return reinterpret_cast<Il2CppObject*>(result);
}

// string + "True"/"False" fast path (ONE managed allocation).
Il2CppObject* ConcatWithBool(Il2CppObject* a, bool value)
{
    static const char16_t kTrue[4] = { u'T', u'r', u'u', u'e' };
    static const char16_t kFalse[5] = { u'F', u'a', u'l', u's', u'e' };
    const char16_t* units = value ? kTrue : kFalse;
    const int32_t numLen = value ? 4 : 5;
    const Il2CppString* sa = AsString(a);
    const int32_t lenA = sa != nullptr ? sa->length : 0;
    const int64_t total = static_cast<int64_t>(lenA) + numLen;
    if (total > kMaxStringLength)
    {
        ThrowLengthOverflow();
    }
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(static_cast<size_t>(total) * sizeof(char16_t)));
    if (lenA > 0)
    {
        std::memcpy(scratch, sa->chars, static_cast<size_t>(lenA) * sizeof(char16_t));
    }
    std::memcpy(scratch + lenA, units, static_cast<size_t>(numLen) * sizeof(char16_t));
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return reinterpret_cast<Il2CppObject*>(result);
}

} // namespace

//------------------------------------------------------------------------------
// icall_string_compare_static -- string.Compare / CompareOrdinal (ordinal).
// null == null -> 0; null < non-null -> -1; non-null > null -> +1; otherwise
// ComparePayloads (the net48 CompareOrdinalHelper pair scan, see above).
//------------------------------------------------------------------------------
int32_t icall_string_compare_static(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr && b == nullptr)
    {
        return 0;
    }
    if (a == nullptr)
    {
        return -1; // null < non-null
    }
    if (b == nullptr)
    {
        return 1; // non-null > null
    }
    return ComparePayloads(AsString(a), AsString(b));
}

//------------------------------------------------------------------------------
// icall_string_compare_mode -- string.Compare(a, b, StringComparison mode):
// the MODE-AWARE static comparator. Ordinal(4) is the exact byte-unit
// comparison; OrdinalIgnoreCase(5) folds ASCII case before deciding; the
// culture forms (0..3) degrade to the ordinal fold (the documented
// ASCII-domain collation deviation shared with the whole culture layer).
// Null doctrine matches CompareOrdinal: null sorts below any non-null.
//------------------------------------------------------------------------------
int32_t icall_string_compare_mode(Il2CppObject* a, Il2CppObject* b, int32_t mode)
{
    if (a == nullptr && b == nullptr)
    {
        return 0;
    }
    if (a == nullptr)
    {
        return -1;
    }
    if (b == nullptr)
    {
        return 1;
    }
    const bool ignoreCase = (mode == 1 || mode == 3 || mode == 5);
    return ComparePayloadsFold(AsString(a), AsString(b), ignoreCase);
}

//------------------------------------------------------------------------------
// icall_string_equals_static -- string.Equals(a, b) ordinal static form.
//------------------------------------------------------------------------------
bool icall_string_equals_static(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr || b == nullptr)
    {
        return a == b; // both null -> true; exactly one null -> false
    }
    const Il2CppString* x = AsString(a);
    const Il2CppString* y = AsString(b);
    if (x->length != y->length)
    {
        return false;
    }
    return std::memcmp(x->chars, y->chars,
                       static_cast<size_t>(x->length) * sizeof(char16_t)) == 0;
}

//------------------------------------------------------------------------------
// icall_string_equals_culture -- string.Equals(string, StringComparison).
// net48 contract (fresh-oracle verified 2026-08-05: variable-null probes on
// the net48 reference runtime): a mode outside 0..5 throws ArgumentException
// BEFORE any null handling; a null 'value' returns FALSE (the comparison
// form of Equals IS null-aware for 'value' -- unlike Compare/StartsWith/
// EndsWith, which throw ArgumentNullException). The culture modes (0-3) are
// the ordinal equivalents (documented ASCII-domain deviation); the
// ignore-case modes (1, 3, 5) fold ASCII ('A'-'Z' <-> 'a'-'z').
//------------------------------------------------------------------------------
bool icall_string_equals_culture(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType)
{
    ThrowIfNullReceiver(this_);
    ThrowIfInvalidComparison(comparisonType);
    if (value == nullptr)
    {
        return false;
    }
    const Il2CppString* x = AsString(this_);
    const Il2CppString* y = AsString(value);
    if (x->length != y->length)
    {
        return false;
    }
    const bool ignoreCase = IsIgnoreCaseMode(comparisonType);
    for (int32_t i = 0; i < x->length; ++i)
    {
        if (!UnitsEqual(x->chars[i], y->chars[i], ignoreCase))
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// icall_string_concat_2/3/4 -- string.Concat fixed-arity forms. Null
// arguments count as empty strings. PERF1 fast path: ONE managed allocation +
// one scratch bump via ConcatFixed (no variadic re-walk, no per-segment class
// strcmp -- the concat_many path this used to delegate to).
//------------------------------------------------------------------------------
Il2CppObject* icall_string_concat_2(Il2CppObject* a, Il2CppObject* b)
{
    const Il2CppString* segs[2] = { AsString(a), AsString(b) };
    return reinterpret_cast<Il2CppObject*>(ConcatFixed(segs, 2));
}

Il2CppObject* icall_string_concat_3(Il2CppObject* a, Il2CppObject* b, Il2CppObject* c)
{
    const Il2CppString* segs[3] = { AsString(a), AsString(b), AsString(c) };
    return reinterpret_cast<Il2CppObject*>(ConcatFixed(segs, 3));
}

Il2CppObject* icall_string_concat_4(Il2CppObject* a, Il2CppObject* b, Il2CppObject* c, Il2CppObject* d)
{
    const Il2CppString* segs[4] = { AsString(a), AsString(b), AsString(c), AsString(d) };
    return reinterpret_cast<Il2CppObject*>(ConcatFixed(segs, 4));
}

//------------------------------------------------------------------------------
// icall_string_concat_int / concat_double / concat_bool -- the Concat(object,
// object) numeric conveniences: the number is formatted and appended to the
// string argument (null -> empty). PERF1 fast paths: concat_int and concat_bool
// format inline into the scratch with ONE managed allocation (see the header);
// concat_double still delegates to icall_double_to_string (G15) + concat_many
// -- the G15 formatter dominates, the concat re-walk is not.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_concat_int(Il2CppObject* a, int32_t value)
{
    return ConcatWithInt32(a, value);
}

Il2CppObject* icall_string_concat_double(Il2CppObject* a, double value)
{
    Il2CppString* num = icall_double_to_string(value);
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_concat_many(2, AsString(a), num));
}

Il2CppObject* icall_string_concat_bool(Il2CppObject* a, bool value)
{
    return ConcatWithBool(a, value);
}

//------------------------------------------------------------------------------
// icall_string_is_null_or_empty -- string.IsNullOrEmpty.
//------------------------------------------------------------------------------
bool icall_string_is_null_or_empty(Il2CppObject* s)
{
    const Il2CppString* str = AsString(s);
    return str == nullptr || str->length == 0;
}

//------------------------------------------------------------------------------
// icall_string_is_null_or_whitespace -- string.IsNullOrWhiteSpace: null,
// empty, or every unit in Char.IsWhiteSpace (the full .NET set, above).
//------------------------------------------------------------------------------
bool icall_string_is_null_or_whitespace(Il2CppObject* s)
{
    const Il2CppString* str = AsString(s);
    if (str == nullptr || str->length == 0)
    {
        return true;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (!IsWhitespace(str->chars[i]))
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// icall_string_join -- string.Join(string, string[] / object[]). The
// object[] overload arrives through the SAME signature shape (the resolver
// cannot distinguish the element type), so this single icall serves both:
// string elements pass through, other elements are converted with
// icall_object_to_string (see JoinFromArray).
//------------------------------------------------------------------------------
Il2CppObject* icall_string_join(Il2CppObject* separator, Il2CppObject* valuesArray)
{
    return reinterpret_cast<Il2CppObject*>(
        JoinFromArray(reinterpret_cast<Il2CppArray*>(valuesArray), AsString(separator)));
}

//------------------------------------------------------------------------------
// icall_string_copy -- string.Copy: a NEW instance with equal content.
//------------------------------------------------------------------------------
Il2CppObject* icall_string_copy(Il2CppObject* s)
{
    if (s == nullptr)
    {
        il2cpp_throw_argument_null_exception("s"); // .NET String.Copy throws ANE
    }
    return reinterpret_cast<Il2CppObject*>(CopyFromString(AsString(s)));
}

//------------------------------------------------------------------------------
// icall_string_get_chars -- INLINE in il2cpp_string_ext2.h (perf1): one
// definition site, inlined at every call site. Removed from this TU.
//------------------------------------------------------------------------------

} // namespace runtime
} // namespace il2cpp
