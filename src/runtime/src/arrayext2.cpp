//------------------------------------------------------------------------------
// arrayext2.cpp -- System.Array extension-2 icalls (see il2cpp_array_ext2.h
// for the full contract, the culture string comparison semantics and the
// BinarySearch complement rule).
//
// Implementation notes:
//   - Every public entry point receives Il2CppObject* and validates the
//     array layout before dereferencing any array field: the klass name MUST
//     carry the '[' array-class marker (array.cpp convention), otherwise the
//     object is not an array and the call raises ArgumentException. The
//     validation helpers are file-local with an Ext2 suffix so they never
//     collide with the first batch's helpers in arrayext.cpp (internal
//     linkage either way; the distinct names keep the two domains
//     self-describing).
//   - The string comparator (CompareCulture) follows .NET Framework
//     Comparer<string>.Default -- i.e. the culture-sensitive String.Compare
//     under the process CurrentCulture (zh-CN in the e2e harness): null < any
//     non-null string; otherwise a case-INSENSITIVE primary pass (ASCII
//     letter folding), then the .NET secondary pass on a primary tie --
//     per-character case weight with LOWERCASE < UPPERCASE (the zh-CN
//     collation order, csc-probe-verified 2026-08-11); a prefix pair is
//     decided by the length difference. Punctuation-heavy inputs deviate
//     from the real collation tables (documented in the header). It reads
//     only the payloads -- no allocation.
//   - The string sort is a Hoare-partition quicksort with a middle-element
//     pivot (same shape as the first batch's int32 sort): it recurses into
//     the smaller half and iterates the larger half, bounding the stack
//     depth at O(log n) for any input ordering. Every swap runs the write
//     barrier on both slots (GC contract, see il2cpp_gc.h); the sort
//     allocates nothing, so no collection can interleave and no rooting is
//     needed -- moving Il2CppObject* elements is safe with no GC safe point.
//   - The binary search uses a lower-bound scan (half-open [lo, hi)): the
//     result is either the first matching index or the insertion point,
//     whose complement is returned for a miss -- exactly .NET
//     Array.BinarySearch.
//   - Range validation (Clear / Reverse / FindIndex) computes index + length
//     in 64-bit so an int32 overflow can never bypass the bounds check
//     (index = INT32_MAX, length = INT32_MAX must fail loudly, never wrap).
//------------------------------------------------------------------------------

#include "il2cpp_array_ext2.h"

#include "il2cpp_array.h"
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <type_traits>

namespace il2cpp {
namespace runtime {

namespace {

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

// The System.String descriptor name (string.cpp registration).
const char kStringTypeName[] = "System.String";

// The single-cast delegate class name (delegate.cpp descriptor).
const char kDelegateClass[] = "System.Delegate";

// The multicast container's class name (delegateext.cpp descriptor).
const char kMulticastClass[] = "System.MulticastDelegate";

// Predicate<int32> callback signature (generated-code delegate ABI -- the
// methodPtr of a single-cast STATIC-method delegate, linq_map.cpp PredI4Fn).
typedef bool (*FindIndexPredFn)(int32_t);

// Predicate<int32> INSTANCE-method callback (a single-cast delegate whose
// target is non-null): the generated instance ABI puts `this` (the stored
// target) in the FIRST parameter slot (linq_map.cpp PredI4ThisFn), so the
// call is (target, value).
typedef bool (*FindIndexPredThisFn)(Il2CppObject*, int32_t);

// Converter<int32, int32> callback signature (the same single-cast static
// delegate ABI; the selector shape of linq_map.cpp SelI4I4Fn).
typedef int32_t (*ConvertAllI4Fn)(int32_t);
// Converter<int32, int32> INSTANCE-method callback (target-bound leaf): the
// target arrives in the `this` slot (linq_map.cpp SelI4I4ThisFn).
typedef int32_t (*ConvertAllI4ThisFn)(Il2CppObject*, int32_t);

// Converter<int32, int64> callback signatures (icallwide E2E: the int64
// output variant; the native leaf returns in the full register regardless
// of the declared width, so the thunk reads int64).
typedef int64_t (*ConvertAllI4I8Fn)(int32_t);
typedef int64_t (*ConvertAllI4I8ThisFn)(Il2CppObject*, int32_t);

// Converter<string, int32> callback signatures (net9/10 E2E icallcase:
// Array.ConvertAll<string,int>(string[], int.Parse) -- the same single-cast
// static delegate ABI; the element rides a managed-ref slot).
typedef int32_t (*ConvertAllStrI4Fn)(Il2CppString*);
typedef int32_t (*ConvertAllStrI4ThisFn)(Il2CppObject*, Il2CppString*);

// Predicate<int64> callback signatures (the linq-domain I8PredFn / I8PredThisFn
// ABI -- the arrgen1 generic-family surface).
typedef bool (*I8PredFn)(int64_t);
typedef bool (*I8PredThisFn)(Il2CppObject*, int64_t);

// Predicate<double> callback signatures (the linq-domain DPredFn / DPredThisFn
// ABI; a float leaf widens to the double slot).
typedef bool (*DPredFn)(double);
typedef bool (*DPredThisFn)(Il2CppObject*, double);

// Predicate<ulong> callback signatures (the ELEMENT_TYPE_U8 "q" slot). The
// transpiler emits a ulong predicate leaf as bool(uint64_t); the runtime reads
// the element's 8-byte cell and invokes it through this ABI.
typedef bool (*UPredFn)(uint64_t);
typedef bool (*UPredThisFn)(Il2CppObject*, uint64_t);

// Predicate<string> callback signatures (the element is a managed reference).
typedef bool (*StrPredFn)(Il2CppString*);
typedef bool (*StrPredThisFn)(Il2CppObject*, Il2CppString*);

// Converter<int32, string> callback signatures (Array.ConvertAll<int,string>).
typedef Il2CppString* (*ConvI4StrFn)(int32_t);
typedef Il2CppString* (*ConvI4StrThisFn)(Il2CppObject*, int32_t);

//------------------------------------------------------------------------------
// RequireArrayExt2 -- validate a static-method array argument.
//   null                -> ArgumentNullException
//   non-array object    -> ArgumentException (caller bug, never UB)
//------------------------------------------------------------------------------
Il2CppArray* RequireArrayExt2(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    if (obj->klass == nullptr || obj->klass->name == nullptr ||
        obj->klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_argument_exception("Object is not an array.");
    }
    return reinterpret_cast<Il2CppArray*>(obj);
}

//------------------------------------------------------------------------------
// RequireInt32ArrayExt2 -- int32-only domain contract (BinarySearch / IndexOf
// / LastIndexOf / Clear / FindIndex / ReverseRange).
//------------------------------------------------------------------------------
Il2CppArray* RequireInt32ArrayExt2(Il2CppArray* arr)
{
    if (arr->isRefElem || arr->elemSize != 4)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int32 arrays are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireRefArrayExt2 -- reference-array-only domain contract (sort_strings /
// binary_search_string). The elements are Il2CppObject* slots; the element
// class descriptor is NOT consulted (tests and generated code may create
// reference arrays with a null element descriptor).
//------------------------------------------------------------------------------
Il2CppArray* RequireRefArrayExt2(Il2CppArray* arr)
{
    if (!arr->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional reference-element arrays are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// FoldCase / IsLowerAscii / IsUpperAscii -- ASCII case helpers for the
// culture comparator. The primary pass folds 'a'..'z' to upper; the
// secondary pass needs the exact case class of a UTF-16 unit. Non-ASCII
// units pass through untouched (documented collation deviation).
//------------------------------------------------------------------------------
char16_t FoldCase(char16_t c)
{
    if (c >= 'a' && c <= 'z')
    {
        return static_cast<char16_t>(c - ('a' - 'A'));
    }
    return c;
}

bool IsLowerAscii(char16_t c) { return c >= 'a' && c <= 'z'; }
bool IsUpperAscii(char16_t c) { return c >= 'A' && c <= 'Z'; }

} // namespace

//------------------------------------------------------------------------------
// CompareCulture -- culture-sensitive comparison of two managed strings with
// the static null policy of .NET Framework Comparer<string>.Default (used by
// Array.Sort/BinarySearch on string arrays). The ordering mirrors the
// process CurrentCulture (zh-CN in the e2e harness), csc-probe-verified
// 2026-08-11 against the net48 oracle:
//   * null == null; null < any non-null string.
//   * PRIMARY pass: case-INSENSITIVE (ASCII fold) char-by-char; the first
//     differing fold decides by code-unit order.
//   * a primary tie with unequal lengths: the shorter prefix sorts first.
//   * SECONDARY pass (equal primary + equal length): the first position
//     where the units differ only by case decides with LOWERCASE < UPPERCASE
//     (zh-CN collation; the exact .NET tertiary weight ordering).
// Reads only the payloads -- pure CPU, no allocation.
//------------------------------------------------------------------------------
int32_t CompareCulture(const Il2CppString* a, const Il2CppString* b)
{
    if (a == b)
    {
        return 0; // same pointer, or both null
    }
    if (a == nullptr)
    {
        return -1; // null sorts before any non-null string
    }
    if (b == nullptr)
    {
        return 1;
    }
    const int32_t common = (a->length < b->length) ? a->length : b->length;
    for (int32_t i = 0; i < common; ++i)
    {
        const char16_t fa = FoldCase(a->chars[i]);
        const char16_t fb = FoldCase(b->chars[i]);
        if (fa != fb)
        {
            return fa < fb ? -1 : 1; // primary decided by the folded units
        }
    }
    if (a->length != b->length)
    {
        return a->length < b->length ? -1 : 1; // prefix: shorter < longer
    }
    // Primary tie and equal length: secondary (case) pass. The units can
    // differ only by case here (their folds are equal); zh-CN collation
    // orders the lowercase letter below its uppercase counterpart.
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (a->chars[i] != b->chars[i])
        {
            if (IsLowerAscii(a->chars[i]) && IsUpperAscii(b->chars[i]))
            {
                return -1;
            }
            if (IsUpperAscii(a->chars[i]) && IsLowerAscii(b->chars[i]))
            {
                return 1;
            }
            return a->chars[i] < b->chars[i] ? -1 : 1; // defensive fallback
        }
    }
    return 0;
}

namespace {

//------------------------------------------------------------------------------
// QuickSortRefs -- Hoare-partition quicksort on slots[lo..hi] (inclusive).
//   Middle-element pivot: already-sorted input (the common case) splits
//   evenly. The smaller half is recursed, the larger half iterated, so the
//   recursion depth is O(log n) for every input ordering. Every swap runs
//   the write barrier on both slots (an old-generation array's card must be
//   marked; see the file header). The sort allocates nothing, so no
//   collection can interleave.
//------------------------------------------------------------------------------
void QuickSortRefs(Il2CppObject** slots, int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        const Il2CppString* pivot =
            reinterpret_cast<Il2CppString*>(slots[lo + (hi - lo) / 2]);
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (CompareCulture(reinterpret_cast<const Il2CppString*>(slots[i]),
                                  pivot) < 0)
            {
                ++i;
            }
            while (CompareCulture(reinterpret_cast<const Il2CppString*>(slots[j]),
                                  pivot) > 0)
            {
                --j;
            }
            if (i <= j)
            {
                Il2CppObject* tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
                il2cpp_write_barrier(reinterpret_cast<void**>(&slots[j]), slots[j]);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortRefs(slots, lo, j);
            lo = i;
        }
        else
        {
            QuickSortRefs(slots, i, hi);
            hi = j;
        }
    }
}

//------------------------------------------------------------------------------
// AsStringOrNull -- interpret a BinarySearch value argument as a managed
// string. null stays null (a legal search value); any non-null object whose
// klass is not System.String raises ArgumentException (defensive -- reading
// a non-string payload as length/chars would be undefined behavior).
//------------------------------------------------------------------------------
const Il2CppString* AsStringOrNull(Il2CppObject* value)
{
    if (value == nullptr)
    {
        return nullptr;
    }
    if (value->klass == nullptr || value->klass->name == nullptr ||
        std::strcmp(value->klass->name, kStringTypeName) != 0)
    {
        il2cpp_throw_argument_exception("Object must be of type String.");
    }
    return reinterpret_cast<const Il2CppString*>(value);
}

//------------------------------------------------------------------------------
// RequireFindIndexArray -- validate the FindIndex delegate-form array
// argument (Il2CppArray*, the linq-domain source convention). Null ->
// ArgumentNullException; a non-array object (caller bug) -> ArgumentException.
//------------------------------------------------------------------------------
Il2CppArray* RequireFindIndexArray(Il2CppArray* arr)
{
    if (arr == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    if (arr->obj.klass == nullptr || arr->obj.klass->name == nullptr ||
        arr->obj.klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_argument_exception("Object is not an array.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireArrayCallback -- shared single-cast delegate validation for the
// predicate / converter forms (Find / Exists / TrueForAll / ConvertAll /
// FindIndex): null -> ArgumentNullException (with the caller's paramName); a
// multicast container -> InvalidOperationException (the multicast container
// is not a single leaf -- its `first` field sits at the same offset as
// methodPtr, so invoking it as a bare callback would call a data pointer as
// code); a non-delegate object is a caller bug -> ArgumentException.
// INSTANCE-BOUND (target-carrying) single-cast delegates ARE accepted (the
// linq-domain contract, linq_map.cpp RequireCallback): the delegate carries
// BOTH methodPtr and target (il2cpp_delegate.h), and the invoke side
// dispatches per binding -- a target-bound leaf is called with its target in
// the `this` slot (the generated-code instance ABI). Returns the validated
// delegate.
//------------------------------------------------------------------------------
Il2CppDelegate* RequireArrayCallback(Il2CppObject* cb, const char* paramName)
{
    if (cb == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
        return nullptr; // unreachable (the factory throws)
    }
    const char* name = cb->klass != nullptr ? cb->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, kMulticastClass) == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as an Array callback.");
        return nullptr;
    }
    if (std::strcmp(name, kDelegateClass) != 0)
    {
        il2cpp_throw_argument_exception("Object is not a delegate.");
        return nullptr;
    }
    return reinterpret_cast<Il2CppDelegate*>(cb);
}

//------------------------------------------------------------------------------
// RequireFindIndexPredicate -- FindIndex's predicate validator (net48
// ArgumentNullException paramName "match"); shares the single-cast delegate
// contract above (instance-bound leaves accepted).
//------------------------------------------------------------------------------
Il2CppDelegate* RequireFindIndexPredicate(Il2CppObject* pred, const char* paramName)
{
    return RequireArrayCallback(pred, paramName);
}

//------------------------------------------------------------------------------
// InvokeFindIndexPred / InvokeConvertAllI4 -- per-binding dispatch (the
// linq_map.cpp InvokePredI4 precedent): an instance-bound leaf (target !=
// null) receives its target in the `this` slot, a static leaf the plain
// family signature. The target is re-read through the ROOTED delegate local
// (the callers register 'd' as a static root) so a compaction between
// callbacks (the leaf may allocate) rewrites d and therefore d->target.
//------------------------------------------------------------------------------
bool InvokeFindIndexPred(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<FindIndexPredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<FindIndexPredFn>(d->methodPtr)(v);
}

int32_t InvokeConvertAllI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<ConvertAllI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<ConvertAllI4Fn>(d->methodPtr)(v);
}

int64_t InvokeConvertAllI4I8(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<ConvertAllI4I8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<ConvertAllI4I8Fn>(d->methodPtr)(v);
}

int32_t InvokeConvertAllStrI4(Il2CppDelegate* d, Il2CppString* v)
{
    return d->target != nullptr
        ? reinterpret_cast<ConvertAllStrI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<ConvertAllStrI4Fn>(d->methodPtr)(v);
}

// Per-binding dispatch for the int64 / double / string predicate families and
// the int32->string converter (the same target-in-`this`-slot rule as
// InvokeFindIndexPred). The callers register 'd' as a static root so a
// compaction between callbacks rewrites d->target.
bool InvokeI8Pred(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<I8PredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<I8PredFn>(d->methodPtr)(v);
}

bool InvokeDPred(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<DPredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<DPredFn>(d->methodPtr)(v);
}

bool InvokeUPred(Il2CppDelegate* d, uint64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<UPredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<UPredFn>(d->methodPtr)(v);
}

bool InvokeStrPred(Il2CppDelegate* d, Il2CppString* v)
{
    return d->target != nullptr
        ? reinterpret_cast<StrPredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<StrPredFn>(d->methodPtr)(v);
}

Il2CppString* InvokeConvI4Str(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<ConvI4StrThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<ConvI4StrFn>(d->methodPtr)(v);
}

//------------------------------------------------------------------------------
// RootScopeExt2 -- GC rooting scope for the FindIndex callback loop (the
// linq_map.cpp RootScope precedent). The predicate can allocate on every
// element, so a collection may fire in the middle of the loop and compact
// the young generation; the array and the predicate locals are registered as
// static-root slots so the collector rewrites the LOCALS themselves. Removal
// is RAII -- the predicate may throw a managed exception.
//------------------------------------------------------------------------------
class RootScopeExt2
{
public:
    RootScopeExt2() : m_count(0) {}
    ~RootScopeExt2()
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

} // namespace

//------------------------------------------------------------------------------
// icall_array_sort_strings -- Array.Sort(string[]) (see header). Culture
// quicksort of the reference slots; null elements sort first.
//------------------------------------------------------------------------------
void icall_array_sort_strings(Il2CppObject* arr)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireRefArrayExt2(a);
    if (a->length > 1)
    {
        QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), 0, a->length - 1);
    }
}

//------------------------------------------------------------------------------
// icall_array_binary_search_int32 -- Array.BinarySearch(int32[]) (see
// header). net48 semantics: probe the MIDPOINT and return it on the first
// equality hit (a duplicate search yields one of the occurrences, not
// necessarily the first); a miss returns the complement of the insertion
// point. An empty array always returns -1 (= ~0).
//------------------------------------------------------------------------------
int32_t icall_array_binary_search_int32(Il2CppObject* arr, int32_t value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireInt32ArrayExt2(a);
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t i = lo + ((hi - lo) >> 1);
        const int32_t element = data[i];
        if (element == value)
        {
            return i;
        }
        if (element < value)
        {
            lo = i + 1;
        }
        else
        {
            hi = i - 1;
        }
    }
    return ~lo; // missing: complement of the insertion point
}

//------------------------------------------------------------------------------
// icall_array_binary_search_string -- Array.BinarySearch(string[]) (see
// header). net48 midpoint-equality scan (see the int32 variant); null
// 'value' compares as "sorts before everything" via CompareCulture, so it
// matches only null elements.
//------------------------------------------------------------------------------
int32_t icall_array_binary_search_string(Il2CppObject* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireRefArrayExt2(a);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t i = lo + ((hi - lo) >> 1);
        const Il2CppString* element = reinterpret_cast<Il2CppString*>(slots[i]);
        const int32_t order = CompareCulture(element, needle);
        if (order == 0)
        {
            return i;
        }
        if (order < 0)
        {
            lo = i + 1;
        }
        else
        {
            hi = i - 1;
        }
    }
    return ~lo; // missing: complement of the insertion point
}

//------------------------------------------------------------------------------
// icall_array_index_of_int32 -- Array.IndexOf<int> (see header). Linear
// first-match scan; -1 when absent (also for an empty array).
//------------------------------------------------------------------------------
int32_t icall_array_index_of_int32(Il2CppObject* arr, int32_t value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireInt32ArrayExt2(a);
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (data[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_array_last_index_of_int32 -- Array.LastIndexOf<int> (see header).
// Linear LAST-match scan (backward); -1 when absent.
//------------------------------------------------------------------------------
int32_t icall_array_last_index_of_int32(Il2CppObject* arr, int32_t value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireInt32ArrayExt2(a);
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        if (data[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_array_clear_int32 -- Array.Clear(int32[], index, length) (see
// header). Validation order: null array, negative index/length, range
// overflow (64-bit sum) -- then memset. length == 0 is a validated no-op
// (a legal one-past-end index passes, matching .NET).
//------------------------------------------------------------------------------
void icall_array_clear_int32(Il2CppObject* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    if (index < 0 || length < 0 ||
        static_cast<int64_t>(index) + static_cast<int64_t>(length) > a->length)
    {
        il2cpp_throw_index_out_of_range_exception("\xE7\xB4\xA2\xE5\xBC\x95\xE8\xB6\x85\xE5\x87\xBA\xE4\xBA\x86\xE6\x95\xB0\xE7\xBB\x84\xE7\x95\x8C\xE9\x99\x90\xE3\x80\x82");
    }
    // nb9 E7: a zero fill over elemSize bytes clears any element kind
    // (0 == null for reference elements, default(T) for value elements).
    if (length > 0)
    {
        std::memset(a->data + static_cast<size_t>(index) * a->elemSize, 0,
                    static_cast<size_t>(length) * a->elemSize);
    }
}

//------------------------------------------------------------------------------
// icall_array_find_index_int32 -- Array.FindIndex<int> (see header).
// .NET validation order: startIndex < 0 || startIndex > Length, then
// count < 0 || count > Length - startIndex (both ArgumentOutOfRange). The
// scan covers [startIndex, startIndex + count); -1 when absent. Note
// startIndex == Length with count == 0 is legal (returns -1) -- .NET allows
// the one-past-end empty range.
//------------------------------------------------------------------------------
int32_t icall_array_find_index_int32(Il2CppObject* arr, int32_t startIndex,
                                     int32_t count, int32_t value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireInt32ArrayExt2(a);
    if (startIndex < 0 || startIndex > a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "startIndex", "startIndex was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
    if (count < 0 || count > a->length - startIndex)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count", "count was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (data[i] == value)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_array_find_index -- Array.FindIndex<int>(Predicate<int>) (see
// header). DELEGATE form: the predicate is invoked per element through its
// methodPtr with per-binding dispatch -- an instance-bound (captured-lambda)
// leaf receives its target in the `this` slot (InvokeFindIndexPred).
// net48 validation order (reference source Array.cs): array null, then
// startIndex, then count, then match null. The array and the delegate are
// GC-rooted across the callback loop (RootScopeExt2) and the delegate is
// re-read through its rooted slot every iteration: the predicate may
// allocate, so a collection can compact and move the delegate (and its
// target) or the array. startIndex == Length with count == 0 is legal and
// returns -1.
//------------------------------------------------------------------------------
int32_t icall_array_find_index(Il2CppArray* arr, Il2CppObject* predicate,
                               int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    if (startIndex < 0 || startIndex > a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "startIndex", "startIndex was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
    if (count < 0 || count > a->length - startIndex)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count", "count was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        // The element is read through the rooted 'a' AFTER the previous
        // predicate call: a pointer cached across the callback could be
        // stale if a collection moved the array. The read and the predicate
        // call are separated by no allocation.
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_array_find_index_2 -- Array.FindIndex<T>(T[], Predicate<T>): the
// 2-arg overload as its own entry (the whole range). The header comment of
// icall_array_find_index historically claimed the 2/3-arg overloads were
// "folded in by the MethodMap" -- they never were; this is the real 2-arg
// wiring, delegating to the validated 4-arg core with start=0, count=len.
//------------------------------------------------------------------------------
int32_t icall_array_find_index_2(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index(a, predicate, 0, a->length);
}

//------------------------------------------------------------------------------
// icall_array_find_index_3 -- Array.FindIndex<T>(T[], int, Predicate<T>): the
// 3-arg overload (startIndex, to the end). The old mapping emitted the 4-arg
// entry, so the emitter popped one argument too many and read across the
// stack (0xC0000409). count = Length - startIndex; the 4-arg core validates
// startIndex first, so a start > Length still reports "startIndex".
//------------------------------------------------------------------------------
int32_t icall_array_find_index_3(Il2CppArray* arr, Il2CppObject* predicate,
                                 int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index(a, predicate, startIndex,
                                  a->length - startIndex);
}

//------------------------------------------------------------------------------
// icall_array_reverse_range -- Array.Reverse(int32[], index, length) (see
// header). Validation as Clear; length 0/1 is a no-op. In-place two-pointer
// reversal of [index, index + length).
//------------------------------------------------------------------------------
void icall_array_reverse_range(Il2CppObject* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("index", "\xE8\xA6\x81\xE6\xB1\x82\xE9\x9D\x9E\xE8\xB4\x9F\xE6\x95\xB0\xE3\x80\x82");
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("length", "\xE8\xA6\x81\xE6\xB1\x82\xE9\x9D\x9E\xE8\xB4\x9F\xE6\x95\xB0\xE3\x80\x82");
    }
    if (static_cast<int64_t>(index) + static_cast<int64_t>(length) > a->length)
    {
        il2cpp_throw_argument_exception(
            "偏移量和长度超出数组的界限，或者计数大于从索引到源集合结尾处的元素数量。");
    }
    // nb9 F3: elemSize-byte swap so ref/int64/float/double/user-struct arrays
    // reverse too (the old int32-only path aborted for string[]/long[]).
    unsigned char* data = reinterpret_cast<unsigned char*>(a->data);
    const int32_t es = a->elemSize;
    for (int32_t i = index, j = index + length - 1; i < j; ++i, --j)
    {
        unsigned char* p = data + static_cast<size_t>(i) * es;
        unsigned char* q = data + static_cast<size_t>(j) * es;
        if (es <= 32)
        {
            unsigned char tmp[32];
            std::memcpy(tmp, p, es);
            std::memcpy(p, q, es);
            std::memcpy(q, tmp, es);
        }
        else
        {
            for (int32_t k = 0; k < es; ++k)
            {
                const unsigned char t = p[k];
                p[k] = q[k];
                q[k] = t;
            }
        }
    }
}

//------------------------------------------------------------------------------
// icall_array_find_i4 -- Array.Find<int>(int32[], Predicate<int>) (see
// header). The VALUE of the first element whose predicate returns true, or 0
// (default(int)) when no element matches. net48 validation order (reference
// source Array.cs): array null, then match null (both ArgumentNullException).
// The delegate is invoked per element with per-binding dispatch
// (InvokeFindIndexPred). GC contract as icall_array_find_index: the
// delegate and the array are rooted across the callback loop.
//------------------------------------------------------------------------------
int32_t icall_array_find_i4(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, v))
        {
            return v;
        }
    }
    return 0; // default(int): no element matched
}

//------------------------------------------------------------------------------
// icall_array_exists_i4 -- Array.Exists<int>(int32[], Predicate<int>) (see
// header). 1 when at least one element matches, 0 otherwise (an empty array
// is always 0). Same validation, per-binding callback and rooting contract as
// icall_array_find_i4.
//------------------------------------------------------------------------------
int32_t icall_array_exists_i4(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, v))
        {
            return 1;
        }
    }
    return 0;
}

//------------------------------------------------------------------------------
// icall_array_true_for_all_i4 -- Array.TrueForAll<int>(int32[],
// Predicate<int>) (see header). 1 when EVERY element matches, 0 otherwise;
// an empty array is VACUOUSLY true (net48). Same validation, per-binding
// callback and rooting contract as icall_array_find_i4.
//------------------------------------------------------------------------------
int32_t icall_array_true_for_all_i4(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeFindIndexPred(d, v))
        {
            return 0;
        }
    }
    return 1; // empty array: no element violates the predicate
}

//------------------------------------------------------------------------------
// icall_array_convert_all_i4_i4 -- Array.ConvertAll<int,int>(int32[],
// Converter<int,int>) (see header). A NEW int32[] of the same length whose
// element i is converter(array[i]); the source is never modified. net48
// validation order: array null, then converter null (both
// ArgumentNullException). The converter is invoked per element with
// per-binding dispatch (InvokeConvertAllI4). The delegate, the source and
// the result array are GC-rooted across the callback loop (the converter may
// allocate; the result slots are written through the rooted array AFTER each
// callback).
//------------------------------------------------------------------------------
Il2CppArray* icall_array_convert_all_i4_i4(Il2CppArray* arr, Il2CppObject* converter)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(converter, "converter");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(int32_t), false, a->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < a->length; ++i)
    {
        // The callback runs FIRST; the result slot is computed afterwards
        // through the rooted 'out' (a collection can move the array -- a slot
        // address cached across the call would be stale). No allocation
        // separates the address computation from the store.
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        const int32_t r = InvokeConvertAllI4(d, v);
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

//------------------------------------------------------------------------------
// icall_array_convert_all_str_i4 -- Array.ConvertAll<string,int>(string[],
// Converter<string,int>) (net9/10 E2E icallcase: int.Parse as the converter).
// A NEW int32[] of the same length whose element i is converter(array[i]);
// the source is never modified. net48 validation order: array null, then
// converter null (both ArgumentNullException). The source is a REFERENCE
// array (string elements ride managed-ref slots); the result is int32[].
// Same GC rooting discipline as the i4_i4 variant.
//------------------------------------------------------------------------------
Il2CppArray* icall_array_convert_all_str_i4(Il2CppArray* arr, Il2CppObject* converter)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(converter, "converter");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(int32_t), false, a->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < a->length; ++i)
    {
        // The callback runs FIRST; the result slot is computed afterwards
        // through the rooted 'out' (a collection can move the array -- a slot
        // address cached across the call would be stale). No allocation
        // separates the address computation from the store.
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        const int32_t r = InvokeConvertAllStrI4(d, v);
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

//------------------------------------------------------------------------------
// icall_array_convert_all_i4_i8 -- Array.ConvertAll<int,long>(int32[],
// Converter<int,long>) (icallwide E2E). A NEW int64[] of the same length
// whose element i is converter(array[i]); the source is never modified.
// Same validation order and GC rooting discipline as the i4_i4 variant.
//------------------------------------------------------------------------------
Il2CppArray* icall_array_convert_all_i4_i8(Il2CppArray* arr, Il2CppObject* converter)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(converter, "converter");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* out = il2cpp_array_new_typed(sizeof(int64_t), 2, a->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        const int64_t r = InvokeConvertAllI4I8(d, v);
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

//------------------------------------------------------------------------------
// icall_array_find_all_i4 -- Array.FindAll<int>(int32[], Predicate<int>) (see
// header). A NEW int32[] holding EVERY element for which the predicate
// returns true, in source order (an empty result is a zero-length array); the
// source is never modified. net48 validation order (reference source
// Array.cs): array null, then match null (both ArgumentNullException). The
// predicate is invoked EXACTLY ONCE per element (reference source
// FindAllIterator). The array and the delegate are GC-rooted across the
// callback loop; the matches are gathered into a rooted buffer of the SOURCE
// length and the result is trimmed to the exact match count afterwards (a
// fresh allocation, copied immediately -- no collection can interleave).
//------------------------------------------------------------------------------
Il2CppArray* icall_array_find_all_i4(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new(nullptr, sizeof(int32_t), false, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        // The element is read through the rooted 'a' AFTER the previous
        // predicate call (a pointer cached across the callback could be stale
        // if a collection moved the array); the result slot is written through
        // the rooted 'full'. No allocation separates the read from the call.
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, v))
        {
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(full, wi)) = v;
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full; // every element matched -- the full buffer is exact
    }
    // Trim to exactly 'wi' elements. The source pointer is re-read through the
    // rooted 'full' AFTER the allocation (a collection may have moved it); the
    // copy into the fresh 'out' is separated from any allocation, so no
    // collection can interleave before the result is returned.
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(int32_t), false, wi);
    if (wi > 0)
    {
        std::memcpy(out->data, full->data,
                    static_cast<size_t>(wi) * sizeof(int32_t));
    }
    return out;
}

//------------------------------------------------------------------------------
// icall_array_for_each_i4 -- Array.ForEach<int>(int32[], Action<int>) (see
// header). The action is invoked once per element in source order; the source
// is never modified. net48 validation order: array null, then action null
// (both ArgumentNullException). Same per-binding callback and GC rooting contract
// as icall_array_find_i4.
//------------------------------------------------------------------------------
void icall_array_for_each_i4(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(action, "action");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        InvokeFindIndexPred(d, v);
    }
}

//------------------------------------------------------------------------------
// icall_array_find_last_i4 -- Array.FindLast<int>(int32[], Predicate<int>) (see
// header). BACKWARD scan: the VALUE of the LAST element whose predicate
// returns true, or 0 (default(int)) when none match. Same validation order,
// per-binding callback and GC rooting contract as icall_array_find_i4.
//------------------------------------------------------------------------------
int32_t icall_array_find_last_i4(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, v))
        {
            return v;
        }
    }
    return 0; // default(int): no element matched
}

//------------------------------------------------------------------------------
// ValidateFindLastRangeExt2 -- the shared range validation for the FindLastIndex
// family (net48 Array.FindLastIndex<T> range rules; identical to
// icall_array_find_index's validation).
//------------------------------------------------------------------------------
static void ValidateFindLastRangeExt2(Il2CppArray* a, int32_t startIndex, int32_t count)
{
    if (startIndex < 0 || startIndex > a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "startIndex", "startIndex was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
    if (count < 0 || count > a->length - startIndex)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count", "count was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
}

//------------------------------------------------------------------------------
// ValidateFindLastIndexRangeExt2 -- the net48 Array.FindLastIndex<T> range
// validation. FindLastIndex scans BACKWARD over the window
// [startIndex - count + 1, startIndex] (NOT the forward [startIndex,
// startIndex + count) window the FindIndex family uses): startIndex must be a
// valid ELEMENT index (< Length, not <=), and the window's low edge
// startIndex - count + 1 must not fall below 0.
//------------------------------------------------------------------------------
static void ValidateFindLastIndexRangeExt2(Il2CppArray* a, int32_t startIndex, int32_t count)
{
    if (startIndex < 0 || startIndex >= a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "startIndex", "startIndex was out of range. Must be non-negative and less than the size of the collection.");
    }
    if (count < 0 || startIndex - count + 1 < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count", "count was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }
}

//------------------------------------------------------------------------------
// icall_array_find_last_index -- Array.FindLastIndex<int>(int32[], int, int,
// Predicate<int>) (see header). BACKWARD scan over [startIndex, startIndex +
// count); the index of the LAST match, or -1 when none match. Same validation,
// per-binding callback and GC rooting contract as icall_array_find_index.
//------------------------------------------------------------------------------
int32_t icall_array_find_last_index(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_last_index_2(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    // net48 FindLastIndex(array, match) scans [0, Length-1] -- startIndex =
    // Length - 1, count = Length.
    return icall_array_find_last_index(a, predicate, a->length - 1, a->length);
}

int32_t icall_array_find_last_index_3(Il2CppArray* arr, Il2CppObject* predicate,
                                      int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    // net48 FindLastIndex(array, startIndex, match) scans the window
    // [0, startIndex] -- count = startIndex + 1.
    return icall_array_find_last_index(a, predicate, startIndex,
                                       startIndex + 1);
}

//------------------------------------------------------------------------------
// icall_array_convert_all_i4_str -- Array.ConvertAll<int,string>(int32[],
// Converter<int,string>) (see header). A NEW string[] (reference-element array)
// of the same length whose element i is converter(array[i]); the source is never
// modified. Same validation order and per-binding callback ABI as
// icall_array_convert_all_i4_i4; the result slots are written through the write
// barrier (the array is a reference array).
//------------------------------------------------------------------------------
Il2CppArray* icall_array_convert_all_i4_str(Il2CppArray* arr, Il2CppObject* converter)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireInt32ArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(converter, "converter");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(void*), true, a->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int32_t v = *reinterpret_cast<const int32_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        Il2CppString* r = InvokeConvI4Str(d, v);
        Il2CppObject* robj = reinterpret_cast<Il2CppObject*>(r);
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        *slot = robj;
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), robj);
    }
    return out;
}

//------------------------------------------------------------------------------
// BATCH4: the general Array.ConvertAll<TInput,TOutput> converter.
//
// The stage-1 surface only had the (int32,int32), (string,int32),
// (int32,int64) and (int32,string) combinations, so every other family pair
// was LOUD-rejected at transpile time. This template covers the remaining
// value / reference element families the runtime can read: int32, int64,
// double (a float source is read at its own 4-byte width) and the reference
// families (string / object / class). The converter is invoked through its
// STATICALLY DECLARED (TInput) -> TOutput ABI with per-binding dispatch; the
// result is a value cell (int32/int64/double) or a reference array
// (string/object/class).
//------------------------------------------------------------------------------
namespace {

template <typename T>
void ConvertAllRequireSource(Il2CppArray* a)
{
    if constexpr (std::is_pointer<T>::value)
    {
        if (!a->isRefElem)
            il2cpp_throw_not_supported_exception(
                "Array.ConvertAll: only a reference-element source array is supported.");
    }
    else if constexpr (std::is_same<T, int32_t>::value)
    {
        if (a->isRefElem || a->elemSize != sizeof(int32_t))
            il2cpp_throw_not_supported_exception(
                "Only one-dimensional System.Int32 arrays are supported.");
    }
    else if constexpr (std::is_same<T, int64_t>::value)
    {
        if (a->isRefElem || a->elemSize != sizeof(int64_t))
            il2cpp_throw_not_supported_exception(
                "Only one-dimensional System.Int64 arrays are supported.");
    }
    else if constexpr (std::is_same<T, double>::value)
    {
        if (a->isRefElem || a->elemSize != sizeof(double))
            il2cpp_throw_not_supported_exception(
                "Only one-dimensional System.Double arrays are supported.");
    }
    else
    {
        if (a->isRefElem || a->elemSize != sizeof(float))
            il2cpp_throw_not_supported_exception(
                "Only one-dimensional System.Single arrays are supported.");
    }
}

template <typename T>
T ConvertAllReadSource(Il2CppArray* a, int32_t i)
{
    if constexpr (std::is_pointer<T>::value)
        return reinterpret_cast<T>(*reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(a, i)));
    else
        return *reinterpret_cast<const T*>(il2cpp_array_get_elem_ptr(a, i));
}

// The transpiler stores float in a DOUBLE slot (MethodMap.MathF.cs), so a
// Converter<float,TOut> leaf is emitted as TOut(double) -- the runtime invokes
// it through the double ABI (the float cell widened to double).
template <typename T>
struct ConvertAllAbi { using type = T; };
template <>
struct ConvertAllAbi<float> { using type = double; };

template <typename TIn, typename TOut>
TOut ConvertAllInvoke(Il2CppDelegate* d, TIn v)
{
    using AbiIn = typename ConvertAllAbi<TIn>::type;
    if (d->target != nullptr)
        return reinterpret_cast<TOut (*)(Il2CppObject*, AbiIn)>(d->methodPtr)(
            d->target, static_cast<AbiIn>(v));
    return reinterpret_cast<TOut (*)(AbiIn)>(d->methodPtr)(static_cast<AbiIn>(v));
}

template <typename T>
int32_t ConvertAllResultElemKind()
{
    if constexpr (std::is_same<T, int64_t>::value) return 2;
    else if constexpr (std::is_same<T, double>::value) return 4;
    else if constexpr (std::is_same<T, float>::value) return 3;
    else return 1;
}

template <typename TIn, typename TOut>
Il2CppArray* ConvertAllFamily(Il2CppArray* arr, Il2CppObject* converter)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    ConvertAllRequireSource<TIn>(a);
    Il2CppDelegate* d = RequireArrayCallback(converter, "converter");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* out = std::is_pointer<TOut>::value
        ? il2cpp_array_new(nullptr, sizeof(void*), true, a->length)
        : il2cpp_array_new_typed(sizeof(TOut), ConvertAllResultElemKind<TOut>(), a->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < a->length; ++i)
    {
        // The callback runs FIRST; the result slot is computed afterwards
        // through the rooted 'out' (a collection can move the array -- a slot
        // address cached across the call would be stale). No allocation
        // separates the address computation from the store.
        const TIn v = ConvertAllReadSource<TIn>(a, i);
        const TOut r = ConvertAllInvoke<TIn, TOut>(d, v);
        if constexpr (std::is_pointer<TOut>::value)
        {
            Il2CppObject* robj = reinterpret_cast<Il2CppObject*>(r);
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(out, i));
            *slot = robj;
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), robj);
        }
        else
        {
            *reinterpret_cast<TOut*>(il2cpp_array_get_elem_ptr(out, i)) = r;
        }
    }
    return out;
}

} // namespace

Il2CppArray* icall_array_convert_all_i4_d(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int32_t, double>(arr, converter);
}

Il2CppArray* icall_array_convert_all_i8_i4(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int64_t, int32_t>(arr, converter);
}

Il2CppArray* icall_array_convert_all_i8_i8(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int64_t, int64_t>(arr, converter);
}

Il2CppArray* icall_array_convert_all_i8_d(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int64_t, double>(arr, converter);
}

Il2CppArray* icall_array_convert_all_i8_str(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int64_t, Il2CppString*>(arr, converter);
}

Il2CppArray* icall_array_convert_all_d_i4(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<double, int32_t>(arr, converter);
}

Il2CppArray* icall_array_convert_all_d_i8(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<double, int64_t>(arr, converter);
}

Il2CppArray* icall_array_convert_all_d_d(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<double, double>(arr, converter);
}

Il2CppArray* icall_array_convert_all_d_str(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<double, Il2CppString*>(arr, converter);
}

Il2CppArray* icall_array_convert_all_str_i8(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<Il2CppString*, int64_t>(arr, converter);
}

Il2CppArray* icall_array_convert_all_str_d(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<Il2CppString*, double>(arr, converter);
}

Il2CppArray* icall_array_convert_all_str_str(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<Il2CppString*, Il2CppString*>(arr, converter);
}

// arrgen1b: the float converter families. A float SOURCE rides the double-slot
// ABI (ConvertAllAbi<float> -> double); a float RESULT is the float cell
// (elemKind 3).
Il2CppArray* icall_array_convert_all_f_i4(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<float, int32_t>(arr, converter);
}
Il2CppArray* icall_array_convert_all_f_i8(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<float, int64_t>(arr, converter);
}
Il2CppArray* icall_array_convert_all_f_d(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<float, double>(arr, converter);
}
Il2CppArray* icall_array_convert_all_f_str(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<float, Il2CppString*>(arr, converter);
}
Il2CppArray* icall_array_convert_all_i4_f(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int32_t, float>(arr, converter);
}
Il2CppArray* icall_array_convert_all_i8_f(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<int64_t, float>(arr, converter);
}
Il2CppArray* icall_array_convert_all_d_f(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<double, float>(arr, converter);
}
Il2CppArray* icall_array_convert_all_str_f(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<Il2CppString*, float>(arr, converter);
}
Il2CppArray* icall_array_convert_all_f_f(Il2CppArray* arr, Il2CppObject* converter)
{
    return ConvertAllFamily<float, float>(arr, converter);
}

//------------------------------------------------------------------------------
// icall_array_convert_all_dyn -- the KIND-parameterized ConvertAll fallback for
// the narrow-integral families (and their mixed pairs with the typed
// families). The transpiler passes two kind literals:
//   0 reference | 1 int32 | 2 int64 | 3 single | 4 double
//   8 bool | 9 char | 10 sbyte | 11 byte | 12 short | 13 ushort
// The delegate ABI is selected from the kind CATEGORY (ref / int32 / int64 /
// double); narrow cells widen to the int32 slot, matching the transpiler's
// narrow value model. Same validation order / rooting as ConvertAllFamily.
//------------------------------------------------------------------------------
namespace {

enum : int32_t
{
    kConvRef = 0,
    kConvI4 = 1,
    kConvI8 = 2,
    kConvF = 3,
    kConvD = 4,
    kConvBool = 8,
    kConvChar = 9,
    kConvI1 = 10,
    kConvU1 = 11,
    kConvI2 = 12,
    kConvU2 = 13
};

int32_t ConvKindSize(int32_t kind)
{
    switch (kind)
    {
        case kConvRef: return static_cast<int32_t>(sizeof(void*));
        case kConvI4: return 4;
        case kConvI8: return 8;
        case kConvF: return 4;
        case kConvD: return 8;
        case kConvBool: case kConvI1: case kConvU1: return 1;
        case kConvChar: case kConvI2: case kConvU2: return 2;
        default:
            il2cpp_throw_not_supported_exception("ConvertAll: unknown element kind.");
            return 0;
    }
}

int32_t ConvKindAbi(int32_t kind)
{
    if (kind == kConvRef) return 0;
    if (kind == kConvI8) return 2;
    if (kind == kConvF || kind == kConvD) return 3;
    return 1; // int32 / narrow
}

int32_t ConvKindElemKind(int32_t kind)
{
    if (kind == kConvI4) return 1;
    if (kind == kConvI8) return 2;
    if (kind == kConvF) return 3;
    if (kind == kConvD) return 4;
    return kind; // narrow kinds are already 8..13
}

struct ConvValue
{
    int32_t i;
    int64_t l;
    double d;
    Il2CppObject* r;
};

ConvValue ConvReadElem(Il2CppArray* a, int32_t idx, int32_t kind)
{
    ConvValue v{};
    const char* p = a->data + static_cast<size_t>(idx) * static_cast<size_t>(ConvKindSize(kind));
    switch (kind)
    {
        case kConvRef:
            v.r = *reinterpret_cast<Il2CppObject**>(const_cast<char*>(p));
            break;
        case kConvI4:
            v.i = *reinterpret_cast<const int32_t*>(p);
            break;
        case kConvI8:
            v.l = *reinterpret_cast<const int64_t*>(p);
            break;
        case kConvF:
            v.d = static_cast<double>(*reinterpret_cast<const float*>(p));
            break;
        case kConvD:
            v.d = *reinterpret_cast<const double*>(p);
            break;
        case kConvBool:
            v.i = *reinterpret_cast<const uint8_t*>(p) != 0 ? 1 : 0;
            break;
        case kConvChar:
        case kConvU2:
            v.i = static_cast<int32_t>(*reinterpret_cast<const uint16_t*>(p));
            break;
        case kConvI1:
            v.i = static_cast<int32_t>(*reinterpret_cast<const int8_t*>(p));
            break;
        case kConvU1:
            v.i = static_cast<int32_t>(*reinterpret_cast<const uint8_t*>(p));
            break;
        case kConvI2:
            v.i = static_cast<int32_t>(*reinterpret_cast<const int16_t*>(p));
            break;
        default:
            break;
    }
    return v;
}

void ConvWriteElem(Il2CppArray* out, int32_t idx, int32_t kind, ConvValue v)
{
    char* p = out->data + static_cast<size_t>(idx) * static_cast<size_t>(ConvKindSize(kind));
    switch (kind)
    {
        case kConvRef:
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(p);
            *slot = v.r;
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), v.r);
            break;
        }
        case kConvI4:
            *reinterpret_cast<int32_t*>(p) = v.i;
            break;
        case kConvI8:
            *reinterpret_cast<int64_t*>(p) = v.l;
            break;
        case kConvF:
            *reinterpret_cast<float*>(p) = static_cast<float>(v.d);
            break;
        case kConvD:
            *reinterpret_cast<double*>(p) = v.d;
            break;
        case kConvBool:
            *reinterpret_cast<uint8_t*>(p) = v.i != 0 ? 1 : 0;
            break;
        case kConvChar:
        case kConvU2:
            *reinterpret_cast<uint16_t*>(p) = static_cast<uint16_t>(v.i);
            break;
        case kConvI1:
            *reinterpret_cast<int8_t*>(p) = static_cast<int8_t>(v.i);
            break;
        case kConvU1:
            *reinterpret_cast<uint8_t*>(p) = static_cast<uint8_t>(v.i);
            break;
        case kConvI2:
            *reinterpret_cast<int16_t*>(p) = static_cast<int16_t>(v.i);
            break;
        default:
            break;
    }
}

#define CONV_INVOKE(ARG, RES, ARGFIELD, RESFIELD)                               \
    do                                                                          \
    {                                                                           \
        if (d->target != nullptr)                                               \
            r.RESFIELD = reinterpret_cast<RES (*)(Il2CppObject*, ARG)>(         \
                d->methodPtr)(d->target, v.ARGFIELD);                           \
        else                                                                    \
            r.RESFIELD = reinterpret_cast<RES (*)(ARG)>(d->methodPtr)(v.ARGFIELD); \
    } while (0)

ConvValue ConvInvoke(Il2CppDelegate* d, int32_t inKind, int32_t outKind, ConvValue v)
{
    ConvValue r{};
    const int32_t inCat = ConvKindAbi(inKind);
    const int32_t outCat = ConvKindAbi(outKind);
    switch (inCat)
    {
        case 0: // reference source
            switch (outCat)
            {
                case 0: CONV_INVOKE(Il2CppObject*, Il2CppObject*, r, r); break;
                case 1: CONV_INVOKE(Il2CppObject*, int32_t, r, i); break;
                case 2: CONV_INVOKE(Il2CppObject*, int64_t, r, l); break;
                default: CONV_INVOKE(Il2CppObject*, double, r, d); break;
            }
            break;
        case 1: // int32 / narrow source
            switch (outCat)
            {
                case 0: CONV_INVOKE(int32_t, Il2CppObject*, i, r); break;
                case 1: CONV_INVOKE(int32_t, int32_t, i, i); break;
                case 2: CONV_INVOKE(int32_t, int64_t, i, l); break;
                default: CONV_INVOKE(int32_t, double, i, d); break;
            }
            break;
        case 2: // int64 source
            switch (outCat)
            {
                case 0: CONV_INVOKE(int64_t, Il2CppObject*, l, r); break;
                case 1: CONV_INVOKE(int64_t, int32_t, l, i); break;
                case 2: CONV_INVOKE(int64_t, int64_t, l, l); break;
                default: CONV_INVOKE(int64_t, double, l, d); break;
            }
            break;
        default: // double / float source
            switch (outCat)
            {
                case 0: CONV_INVOKE(double, Il2CppObject*, d, r); break;
                case 1: CONV_INVOKE(double, int32_t, d, i); break;
                case 2: CONV_INVOKE(double, int64_t, d, l); break;
                default: CONV_INVOKE(double, double, d, d); break;
            }
            break;
    }
    return r;
}

#undef CONV_INVOKE

} // namespace

Il2CppArray* icall_array_convert_all_dyn(Il2CppArray* arr, Il2CppObject* converter,
                                         int32_t inKind, int32_t outKind)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem != (inKind == kConvRef) ||
        a->elemSize != ConvKindSize(inKind))
    {
        il2cpp_throw_not_supported_exception(
            "ConvertAll: source array element kind mismatch.");
    }
    Il2CppDelegate* d = RequireArrayCallback(converter, "converter");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* out = outKind == kConvRef
        ? il2cpp_array_new(nullptr, sizeof(void*), true, a->length)
        : il2cpp_array_new_typed(ConvKindSize(outKind), ConvKindElemKind(outKind), a->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const ConvValue v = ConvReadElem(a, i, inKind);
        const ConvValue r = ConvInvoke(d, inKind, outKind, v);
        ConvWriteElem(out, i, outKind, r);
    }
    return out;
}

//------------------------------------------------------------------------------
// icall_array_resize_i8 / _str -- Array.Resize<long>(ref long[], int) /
// Array.Resize<string>(ref string[], int) (see header). The same net48 contract
// as icall_array_resize_i4 (newSize < 0 -> ArgumentOutOfRangeException; a null
// array becomes new T[newSize]; an equal length is a no-op). The element width
// is read from the OLD array (8 for int64, a reference slot for string), so the
// copy is width-correct; the caller's array slot is GC-rooted across the
// allocation.
//------------------------------------------------------------------------------
void icall_array_resize_i8(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return;
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }

    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        *arraySlot = il2cpp_array_new_typed(sizeof(int64_t), 2, newSize);
        return;
    }
    RequireFindIndexArray(old);
    if (old->elemSize != sizeof(int64_t) || old->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return;
    }

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));

    Il2CppArray* fresh = il2cpp_array_new_typed(sizeof(int64_t), 2, newSize);
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    if (copyLen > 0)
    {
        std::memcpy(fresh->data, moved->data,
                    static_cast<size_t>(copyLen) * sizeof(int64_t));
    }
    *arraySlot = fresh;
}

void icall_array_resize_str(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return;
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }

    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        *arraySlot = il2cpp_array_new(nullptr, sizeof(void*), true, newSize);
        return;
    }
    RequireFindIndexArray(old);
    if (!old->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional reference-element arrays are supported.");
    }
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return;
    }

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));

    Il2CppArray* fresh = il2cpp_array_new(nullptr, sizeof(void*), true, newSize);
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    for (int32_t i = 0; i < copyLen; ++i)
    {
        Il2CppObject* v = *reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(moved, i));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(fresh, i));
        *slot = v;
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), v);
    }
    *arraySlot = fresh;
}

//------------------------------------------------------------------------------
// icall_array_find_i8 / _exists_i8 / _true_for_all_i8 / _find_all_i8 /
// _find_index_i8[_2/_3] -- the int64 generic family (see header). The same
// validation, per-binding callback and GC rooting contract as the int32 family;
// the predicate ABI is bool(*)(int64_t) / bool(*)(Il2CppObject*, int64_t).
//------------------------------------------------------------------------------
int64_t icall_array_find_i8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeI8Pred(d, v))
        {
            return v;
        }
    }
    return 0; // default(long): no element matched
}

int32_t icall_array_exists_i8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeI8Pred(d, v))
        {
            return 1;
        }
    }
    return 0;
}

int32_t icall_array_true_for_all_i8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeI8Pred(d, v))
        {
            return 0;
        }
    }
    return 1; // empty array: vacuously true
}

Il2CppArray* icall_array_find_all_i8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new_typed(sizeof(int64_t), 2, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeI8Pred(d, v))
        {
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(full, wi)) = v;
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full;
    }
    Il2CppArray* out = il2cpp_array_new_typed(sizeof(int64_t), 2, wi);
    if (wi > 0)
    {
        std::memcpy(out->data, full->data,
                    static_cast<size_t>(wi) * sizeof(int64_t));
    }
    return out;
}

int32_t icall_array_find_index_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                  int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    ValidateFindLastRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeI8Pred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_index_2_i8(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_i8(a, predicate, 0, a->length);
}

int32_t icall_array_find_index_3_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_i8(a, predicate, startIndex,
                                     a->length - startIndex);
}

//------------------------------------------------------------------------------
// icall_array_find_str / _exists_str / _true_for_all_str / _find_all_str /
// _find_index_str[_2/_3] -- the reference (string) generic family (see header).
// Same contract as the int32 family; the predicate ABI is
// bool(*)(Il2CppString*) / bool(*)(Il2CppObject*, Il2CppString*).
//------------------------------------------------------------------------------
Il2CppObject* icall_array_find_str(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeStrPred(d, v))
        {
            return reinterpret_cast<Il2CppObject*>(v);
        }
    }
    return nullptr; // default(string): no element matched
}

int32_t icall_array_exists_str(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeStrPred(d, v))
        {
            return 1;
        }
    }
    return 0;
}

int32_t icall_array_true_for_all_str(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeStrPred(d, v))
        {
            return 0;
        }
    }
    return 1; // empty array: vacuously true
}

Il2CppArray* icall_array_find_all_str(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new(nullptr, sizeof(void*), true, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeStrPred(d, v))
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(full, wi));
            *slot = reinterpret_cast<Il2CppObject*>(v);
            il2cpp_write_barrier(reinterpret_cast<void**>(slot),
                                 reinterpret_cast<Il2CppObject*>(v));
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full;
    }
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(void*), true, wi);
    for (int32_t i = 0; i < wi; ++i)
    {
        Il2CppObject* v = *reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(full, i));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        *slot = v;
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), v);
    }
    return out;
}

int32_t icall_array_find_index_str(Il2CppArray* arr, Il2CppObject* predicate,
                                   int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    ValidateFindLastRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeStrPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_index_2_str(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_str(a, predicate, 0, a->length);
}

int32_t icall_array_find_index_3_str(Il2CppArray* arr, Il2CppObject* predicate,
                                     int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_str(a, predicate, startIndex,
                                      a->length - startIndex);
}

//------------------------------------------------------------------------------
// icall_array_find_index_d / _f and their _2/_3 overloads -- Array.FindIndex<T>
// for the double and float families. Same contract as the int32 family (range
// validation, first-match forward scan); the predicate ABI is the double-slot
// ABI (InvokeDPred -- a float leaf widens to double).
//------------------------------------------------------------------------------
int32_t icall_array_find_index_d(Il2CppArray* arr, Il2CppObject* predicate,
                                  int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    ValidateFindLastRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_index_2_d(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_d(a, predicate, 0, a->length);
}

int32_t icall_array_find_index_3_d(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_d(a, predicate, startIndex,
                                    a->length - startIndex);
}

int32_t icall_array_find_index_f(Il2CppArray* arr, Il2CppObject* predicate,
                                  int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    ValidateFindLastRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, static_cast<double>(v)))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_index_2_f(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_f(a, predicate, 0, a->length);
}

int32_t icall_array_find_index_3_f(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_f(a, predicate, startIndex,
                                    a->length - startIndex);
}

//------------------------------------------------------------------------------
// icall_array_find_last_index_i8 / _str / _d / _f and their _2/_3 overloads --
// Array.FindLastIndex<T> for the int64, string, double and float families.
// Same contract as the int32 family (range validation, BACKWARD first-match
// scan over [startIndex - count + 1, startIndex]); the predicate ABI is the
// matching family's (I8PredFn / StrPredFn / DPredFn -- a float leaf widens to
// double).
//------------------------------------------------------------------------------
int32_t icall_array_find_last_index_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                        int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeI8Pred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_last_index_2_i8(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_i8(a, predicate, a->length - 1, a->length);
}

int32_t icall_array_find_last_index_3_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                          int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_i8(a, predicate, startIndex,
                                          startIndex + 1);
}

int32_t icall_array_find_last_index_str(Il2CppArray* arr, Il2CppObject* predicate,
                                         int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeStrPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_last_index_2_str(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_str(a, predicate, a->length - 1, a->length);
}

int32_t icall_array_find_last_index_3_str(Il2CppArray* arr, Il2CppObject* predicate,
                                           int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_str(a, predicate, startIndex,
                                           startIndex + 1);
}

int32_t icall_array_find_last_index_d(Il2CppArray* arr, Il2CppObject* predicate,
                                       int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_last_index_2_d(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_d(a, predicate, a->length - 1, a->length);
}

int32_t icall_array_find_last_index_3_d(Il2CppArray* arr, Il2CppObject* predicate,
                                         int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_d(a, predicate, startIndex,
                                         startIndex + 1);
}

int32_t icall_array_find_last_index_f(Il2CppArray* arr, Il2CppObject* predicate,
                                       int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, static_cast<double>(v)))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_last_index_2_f(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_f(a, predicate, a->length - 1, a->length);
}

int32_t icall_array_find_last_index_3_f(Il2CppArray* arr, Il2CppObject* predicate,
                                         int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_f(a, predicate, startIndex,
                                         startIndex + 1);
}

//------------------------------------------------------------------------------
// icall_array_find_d / _exists_d / _true_for_all_d -- the double generic family
// (see header). Same contract as the int32 family; the predicate ABI is
// bool(*)(double) / bool(*)(Il2CppObject*, double).
//------------------------------------------------------------------------------
double icall_array_find_d(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, v))
        {
            return v;
        }
    }
    return 0.0; // default(double): no element matched
}

int32_t icall_array_exists_d(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, v))
        {
            return 1;
        }
    }
    return 0;
}

int32_t icall_array_true_for_all_d(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeDPred(d, v))
        {
            return 0;
        }
    }
    return 1; // empty array: vacuously true
}

//------------------------------------------------------------------------------
// icall_array_find_f / _exists_f / _true_for_all_f -- the float generic family
// (see header). Same contract as the int32 family; the predicate ABI is the
// double-slot ABI (a float leaf widens to double -- the linq-domain FPredFn /
// FPredThisFn contract), exactly like the double family above. The result
// carrier follows the element kind (float -> "f").
//------------------------------------------------------------------------------
float icall_array_find_f(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, static_cast<double>(v)))
        {
            return v;
        }
    }
    return 0.0f; // default(float): no element matched
}

int32_t icall_array_exists_f(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, static_cast<double>(v)))
        {
            return 1;
        }
    }
    return 0;
}

int32_t icall_array_true_for_all_f(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeDPred(d, static_cast<double>(v)))
        {
            return 0;
        }
    }
    return 1; // empty array: vacuously true
}

//------------------------------------------------------------------------------
// The UINT64 generic family (the ELEMENT_TYPE_U8 "q" slot; an 8-byte cell with
// an unsigned predicate ABI -- InvokeUPred). Same contract as the int64 family;
// the element cell is read as uint64_t.
//------------------------------------------------------------------------------
uint64_t icall_array_find_u8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeUPred(d, v))
        {
            return v;
        }
    }
    return 0; // default(ulong): no element matched
}

int32_t icall_array_exists_u8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeUPred(d, v))
        {
            return 1;
        }
    }
    return 0;
}

int32_t icall_array_true_for_all_u8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeUPred(d, v))
        {
            return 0;
        }
    }
    return 1; // empty array: vacuously true
}

uint64_t icall_array_find_last_u8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeUPred(d, v))
        {
            return v;
        }
    }
    return 0; // default(ulong): no element matched
}

Il2CppArray* icall_array_find_all_u8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new(nullptr, sizeof(uint64_t), false, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeUPred(d, v))
        {
            *reinterpret_cast<uint64_t*>(il2cpp_array_get_elem_ptr(full, wi)) = v;
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full;
    }
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(uint64_t), false, wi);
    if (wi > 0)
    {
        std::memcpy(out->data, full->data,
                    static_cast<size_t>(wi) * sizeof(uint64_t));
    }
    return out;
}

void icall_array_for_each_u8(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(action, "action");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        InvokeUPred(d, v);
    }
}

int32_t icall_array_find_index_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                   int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    ValidateFindLastRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeUPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_index_2_u8(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_u8(a, predicate, 0, a->length);
}

int32_t icall_array_find_index_3_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                     int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_index_u8(a, predicate, startIndex,
                                     a->length - startIndex);
}

int32_t icall_array_find_last_index_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                        int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        const uint64_t v = *reinterpret_cast<const uint64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeUPred(d, v))
        {
            return i;
        }
    }
    return -1;
}

int32_t icall_array_find_last_index_2_u8(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_u8(a, predicate, a->length - 1, a->length);
}

int32_t icall_array_find_last_index_3_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                          int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return icall_array_find_last_index_u8(a, predicate, startIndex,
                                          startIndex + 1);
}

void icall_array_resize_u8(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return; // unreachable (the factory throws)
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }

    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        *arraySlot = il2cpp_array_new(nullptr, sizeof(uint64_t), false, newSize);
        return;
    }
    RequireFindIndexArray(old);
    if (old->isRefElem || old->elemSize != sizeof(uint64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.UInt64 arrays are supported.");
    }
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return; // net48: an equal length keeps the same reference (no-op)
    }

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));

    Il2CppArray* fresh = il2cpp_array_new(nullptr, sizeof(uint64_t), false, newSize);
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    if (copyLen > 0)
    {
        std::memcpy(fresh->data, moved->data,
                    static_cast<size_t>(copyLen) * sizeof(uint64_t));
    }
    *arraySlot = fresh;
}

//------------------------------------------------------------------------------
// The NARROW-INTEGRAL generic families (bool/char/sbyte/byte/short/ushort/uint).
// The transpiler stores every narrow integral in an int32 slot (MethodSignature
// Coder: "b"/"c"/"i1"/"u1"/"i2"/"u2" -- uint32 "i"), so a Predicate<byte> leaf
// is emitted as bool(int32_t): the element cell is read at its real width and
// WIDENED to int32 before the int32 predicate ABI (InvokeFindIndexPred). Same
// validation order, per-binding callback and GC rooting contract as the int32
// family. One template per operator; the explicit wrappers below instantiate it
// per cell type.
//------------------------------------------------------------------------------
template <typename TCell>
int32_t FindNarrow(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    for (int32_t i = 0; i < a->length; ++i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            return static_cast<int32_t>(v);
        }
    }
    return 0; // default(T): no element matched
}

template <typename TCell>
int32_t ExistsNarrow(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    for (int32_t i = 0; i < a->length; ++i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            return 1;
        }
    }
    return 0;
}

template <typename TCell>
int32_t TrueForAllNarrow(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    for (int32_t i = 0; i < a->length; ++i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (!InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            return 0;
        }
    }
    return 1; // empty array: vacuously true
}

template <typename TCell>
int32_t FindLastNarrow(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            return static_cast<int32_t>(v);
        }
    }
    return 0; // default(T): no element matched
}

template <typename TCell>
Il2CppArray* FindAllNarrow(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new(nullptr, sizeof(TCell), false, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            *reinterpret_cast<TCell*>(il2cpp_array_get_elem_ptr(full, wi)) = v;
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full;
    }
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(TCell), false, wi);
    if (wi > 0)
    {
        std::memcpy(out->data, full->data, static_cast<size_t>(wi) * sizeof(TCell));
    }
    return out;
}

template <typename TCell>
void ForEachNarrow(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(action, "action");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    for (int32_t i = 0; i < a->length; ++i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        InvokeFindIndexPred(d, static_cast<int32_t>(v));
    }
}

template <typename TCell>
int32_t FindIndexNarrow(Il2CppArray* arr, Il2CppObject* predicate,
                        int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    ValidateFindLastRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            return i;
        }
    }
    return -1;
}

template <typename TCell>
int32_t FindIndexNarrow2(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return FindIndexNarrow<TCell>(a, predicate, 0, a->length);
}

template <typename TCell>
int32_t FindIndexNarrow3(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return FindIndexNarrow<TCell>(a, predicate, startIndex, a->length - startIndex);
}

template <typename TCell>
int32_t FindLastIndexNarrow(Il2CppArray* arr, Il2CppObject* predicate,
                            int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    ValidateFindLastIndexRangeExt2(a, startIndex, count);
    Il2CppDelegate* d = RequireFindIndexPredicate(predicate, "match");
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));
    const int32_t first = startIndex - count + 1; // the lowest scanned index
    for (int32_t i = startIndex; i >= first; --i)
    {
        const TCell v = *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(a, i));
        if (InvokeFindIndexPred(d, static_cast<int32_t>(v)))
        {
            return i;
        }
    }
    return -1;
}

template <typename TCell>
int32_t FindLastIndexNarrow2(Il2CppArray* arr, Il2CppObject* predicate)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return FindLastIndexNarrow<TCell>(a, predicate, a->length - 1, a->length);
}

template <typename TCell>
int32_t FindLastIndexNarrow3(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    return FindLastIndexNarrow<TCell>(a, predicate, startIndex, startIndex + 1);
}

template <typename TCell>
void ResizeNarrow(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return; // unreachable (the factory throws)
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }
    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        *arraySlot = il2cpp_array_new(nullptr, sizeof(TCell), false, newSize);
        return;
    }
    RequireFindIndexArray(old);
    if (old->isRefElem || old->elemSize != sizeof(TCell))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional narrow-integral arrays are supported.");
    }
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return; // net48: an equal length keeps the same reference (no-op)
    }
    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));
    Il2CppArray* fresh = il2cpp_array_new(nullptr, sizeof(TCell), false, newSize);
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    if (copyLen > 0)
    {
        std::memcpy(fresh->data, moved->data, static_cast<size_t>(copyLen) * sizeof(TCell));
    }
    *arraySlot = fresh;
}

//------------------------------------------------------------------------------
// Narrow-integral explicit wrappers (bool/char/sbyte/byte/short/ushort/uint).
// The suffix follows the MethodSignatureCoder slot code: _b/_c/_i1/_u1/_i2/
// _u2/_u4.
//------------------------------------------------------------------------------
int32_t icall_array_find_b(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<bool>(arr, match); }
int32_t icall_array_find_c(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<char16_t>(arr, match); }
int32_t icall_array_find_i1(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<int8_t>(arr, match); }
int32_t icall_array_find_u1(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<uint8_t>(arr, match); }
int32_t icall_array_find_i2(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<int16_t>(arr, match); }
int32_t icall_array_find_u2(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<uint16_t>(arr, match); }
int32_t icall_array_find_u4(Il2CppArray* arr, Il2CppObject* match) { return FindNarrow<uint32_t>(arr, match); }

int32_t icall_array_exists_b(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<bool>(arr, match); }
int32_t icall_array_exists_c(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<char16_t>(arr, match); }
int32_t icall_array_exists_i1(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<int8_t>(arr, match); }
int32_t icall_array_exists_u1(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<uint8_t>(arr, match); }
int32_t icall_array_exists_i2(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<int16_t>(arr, match); }
int32_t icall_array_exists_u2(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<uint16_t>(arr, match); }
int32_t icall_array_exists_u4(Il2CppArray* arr, Il2CppObject* match) { return ExistsNarrow<uint32_t>(arr, match); }

int32_t icall_array_true_for_all_b(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<bool>(arr, match); }
int32_t icall_array_true_for_all_c(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<char16_t>(arr, match); }
int32_t icall_array_true_for_all_i1(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<int8_t>(arr, match); }
int32_t icall_array_true_for_all_u1(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<uint8_t>(arr, match); }
int32_t icall_array_true_for_all_i2(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<int16_t>(arr, match); }
int32_t icall_array_true_for_all_u2(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<uint16_t>(arr, match); }
int32_t icall_array_true_for_all_u4(Il2CppArray* arr, Il2CppObject* match) { return TrueForAllNarrow<uint32_t>(arr, match); }

int32_t icall_array_find_last_b(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<bool>(arr, match); }
int32_t icall_array_find_last_c(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<char16_t>(arr, match); }
int32_t icall_array_find_last_i1(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<int8_t>(arr, match); }
int32_t icall_array_find_last_u1(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<uint8_t>(arr, match); }
int32_t icall_array_find_last_i2(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<int16_t>(arr, match); }
int32_t icall_array_find_last_u2(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<uint16_t>(arr, match); }
int32_t icall_array_find_last_u4(Il2CppArray* arr, Il2CppObject* match) { return FindLastNarrow<uint32_t>(arr, match); }

Il2CppArray* icall_array_find_all_b(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<bool>(arr, match); }
Il2CppArray* icall_array_find_all_c(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<char16_t>(arr, match); }
Il2CppArray* icall_array_find_all_i1(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<int8_t>(arr, match); }
Il2CppArray* icall_array_find_all_u1(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<uint8_t>(arr, match); }
Il2CppArray* icall_array_find_all_i2(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<int16_t>(arr, match); }
Il2CppArray* icall_array_find_all_u2(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<uint16_t>(arr, match); }
Il2CppArray* icall_array_find_all_u4(Il2CppArray* arr, Il2CppObject* match) { return FindAllNarrow<uint32_t>(arr, match); }

void icall_array_for_each_b(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<bool>(arr, action); }
void icall_array_for_each_c(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<char16_t>(arr, action); }
void icall_array_for_each_i1(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<int8_t>(arr, action); }
void icall_array_for_each_u1(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<uint8_t>(arr, action); }
void icall_array_for_each_i2(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<int16_t>(arr, action); }
void icall_array_for_each_u2(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<uint16_t>(arr, action); }
void icall_array_for_each_u4(Il2CppArray* arr, Il2CppObject* action) { ForEachNarrow<uint32_t>(arr, action); }

int32_t icall_array_find_index_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<bool>(arr, predicate, startIndex, count); }
int32_t icall_array_find_index_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<char16_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_index_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<int8_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_index_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<uint8_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_index_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<int16_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_index_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<uint16_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_index_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindIndexNarrow<uint32_t>(arr, predicate, startIndex, count); }

int32_t icall_array_find_index_2_b(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<bool>(arr, predicate); }
int32_t icall_array_find_index_2_c(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<char16_t>(arr, predicate); }
int32_t icall_array_find_index_2_i1(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<int8_t>(arr, predicate); }
int32_t icall_array_find_index_2_u1(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<uint8_t>(arr, predicate); }
int32_t icall_array_find_index_2_i2(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<int16_t>(arr, predicate); }
int32_t icall_array_find_index_2_u2(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<uint16_t>(arr, predicate); }
int32_t icall_array_find_index_2_u4(Il2CppArray* arr, Il2CppObject* predicate) { return FindIndexNarrow2<uint32_t>(arr, predicate); }

int32_t icall_array_find_index_3_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<bool>(arr, predicate, startIndex); }
int32_t icall_array_find_index_3_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<char16_t>(arr, predicate, startIndex); }
int32_t icall_array_find_index_3_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<int8_t>(arr, predicate, startIndex); }
int32_t icall_array_find_index_3_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<uint8_t>(arr, predicate, startIndex); }
int32_t icall_array_find_index_3_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<int16_t>(arr, predicate, startIndex); }
int32_t icall_array_find_index_3_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<uint16_t>(arr, predicate, startIndex); }
int32_t icall_array_find_index_3_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindIndexNarrow3<uint32_t>(arr, predicate, startIndex); }

int32_t icall_array_find_last_index_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<bool>(arr, predicate, startIndex, count); }
int32_t icall_array_find_last_index_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<char16_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_last_index_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<int8_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_last_index_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<uint8_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_last_index_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<int16_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_last_index_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<uint16_t>(arr, predicate, startIndex, count); }
int32_t icall_array_find_last_index_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count) { return FindLastIndexNarrow<uint32_t>(arr, predicate, startIndex, count); }

int32_t icall_array_find_last_index_2_b(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<bool>(arr, predicate); }
int32_t icall_array_find_last_index_2_c(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<char16_t>(arr, predicate); }
int32_t icall_array_find_last_index_2_i1(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<int8_t>(arr, predicate); }
int32_t icall_array_find_last_index_2_u1(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<uint8_t>(arr, predicate); }
int32_t icall_array_find_last_index_2_i2(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<int16_t>(arr, predicate); }
int32_t icall_array_find_last_index_2_u2(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<uint16_t>(arr, predicate); }
int32_t icall_array_find_last_index_2_u4(Il2CppArray* arr, Il2CppObject* predicate) { return FindLastIndexNarrow2<uint32_t>(arr, predicate); }

int32_t icall_array_find_last_index_3_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<bool>(arr, predicate, startIndex); }
int32_t icall_array_find_last_index_3_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<char16_t>(arr, predicate, startIndex); }
int32_t icall_array_find_last_index_3_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<int8_t>(arr, predicate, startIndex); }
int32_t icall_array_find_last_index_3_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<uint8_t>(arr, predicate, startIndex); }
int32_t icall_array_find_last_index_3_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<int16_t>(arr, predicate, startIndex); }
int32_t icall_array_find_last_index_3_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<uint16_t>(arr, predicate, startIndex); }
int32_t icall_array_find_last_index_3_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex) { return FindLastIndexNarrow3<uint32_t>(arr, predicate, startIndex); }

void icall_array_resize_b(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<bool>(arraySlot, newSize); }
void icall_array_resize_c(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<char16_t>(arraySlot, newSize); }
void icall_array_resize_i1(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<int8_t>(arraySlot, newSize); }
void icall_array_resize_u1(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<uint8_t>(arraySlot, newSize); }
void icall_array_resize_i2(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<int16_t>(arraySlot, newSize); }
void icall_array_resize_u2(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<uint16_t>(arraySlot, newSize); }
void icall_array_resize_u4(Il2CppArray** arraySlot, int32_t newSize) { ResizeNarrow<uint32_t>(arraySlot, newSize); }

//------------------------------------------------------------------------------
// icall_array_find_last_i8 / _d / _str -- Array.FindLast<T> for the int64,
// double and reference (string) families. BACKWARD scan: the VALUE of the LAST
// element whose predicate returns true, or default(T) when none match (an empty
// array is always default(T)). Same validation order, per-binding callback and
// GC rooting contract as the matching icall_array_find_*.
//------------------------------------------------------------------------------
int64_t icall_array_find_last_i8(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeI8Pred(d, v))
        {
            return v;
        }
    }
    return 0; // default(long): no element matched
}

double icall_array_find_last_d(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, v))
        {
            return v;
        }
    }
    return 0.0; // default(double): no element matched
}

// Array.FindLast<float>(float[], Predicate<float>): the float twin of
// icall_array_find_last_d -- BACKWARD scan, the double-slot predicate ABI.
float icall_array_find_last_f(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, static_cast<double>(v)))
        {
            return v;
        }
    }
    return 0.0f; // default(float): no element matched
}

Il2CppObject* icall_array_find_last_str(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeStrPred(d, v))
        {
            return reinterpret_cast<Il2CppObject*>(v);
        }
    }
    return nullptr; // default(string): no element matched
}

//------------------------------------------------------------------------------
// icall_array_for_each_i8 / _d / _str -- Array.ForEach<T> for the int64,
// double and reference (string) families. The action is invoked once per
// element in source order; the source is never modified. Same validation order,
// per-binding callback and GC rooting contract as icall_array_for_each_i4.
//------------------------------------------------------------------------------
void icall_array_for_each_i8(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(int64_t))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(action, "action");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const int64_t v = *reinterpret_cast<const int64_t*>(
            il2cpp_array_get_elem_ptr(a, i));
        InvokeI8Pred(d, v);
    }
}

void icall_array_for_each_d(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(action, "action");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        InvokeDPred(d, v);
    }
}

// Array.ForEach<float>(float[], Action<float>): the float twin of
// icall_array_for_each_d -- the double-slot action ABI.
void icall_array_for_each_f(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(action, "action");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        InvokeDPred(d, static_cast<double>(v));
    }
}

void icall_array_for_each_str(Il2CppArray* arr, Il2CppObject* action)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    RequireRefArrayExt2(a);
    Il2CppDelegate* d = RequireArrayCallback(action, "action");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    for (int32_t i = 0; i < a->length; ++i)
    {
        Il2CppString* v = *reinterpret_cast<Il2CppString* const*>(
            il2cpp_array_get_elem_ptr(a, i));
        InvokeStrPred(d, v);
    }
}

//------------------------------------------------------------------------------
// icall_array_find_all_d -- Array.FindAll<double>(double[], Predicate<double>):
// a NEW double[] holding EVERY element for which the predicate returns true, in
// source order (an empty result is a zero-length array). Same contract as
// icall_array_find_all_i8.
//------------------------------------------------------------------------------
Il2CppArray* icall_array_find_all_d(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new_typed(sizeof(double), 4, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        const double v = *reinterpret_cast<const double*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, v))
        {
            *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(full, wi)) = v;
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full;
    }
    Il2CppArray* out = il2cpp_array_new_typed(sizeof(double), 4, wi);
    if (wi > 0)
    {
        std::memcpy(out->data, full->data,
                    static_cast<size_t>(wi) * sizeof(double));
    }
    return out;
}

// Array.FindAll<float>(float[], Predicate<float>): the float twin of
// icall_array_find_all_d -- a NEW float[] of every matching element, in source
// order (an empty result is a zero-length array); the double-slot predicate ABI.
Il2CppArray* icall_array_find_all_f(Il2CppArray* arr, Il2CppObject* match)
{
    Il2CppArray* a = RequireFindIndexArray(arr);
    if (a->isRefElem || a->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    Il2CppDelegate* d = RequireArrayCallback(match, "match");

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&a));

    Il2CppArray* full = il2cpp_array_new_typed(sizeof(float), 3, a->length);
    roots.Add(reinterpret_cast<void**>(&full));

    int32_t wi = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        const float v = *reinterpret_cast<const float*>(
            il2cpp_array_get_elem_ptr(a, i));
        if (InvokeDPred(d, static_cast<double>(v)))
        {
            *reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(full, wi)) = v;
            ++wi;
        }
    }
    if (wi == a->length)
    {
        return full;
    }
    Il2CppArray* out = il2cpp_array_new_typed(sizeof(float), 3, wi);
    if (wi > 0)
    {
        std::memcpy(out->data, full->data,
                    static_cast<size_t>(wi) * sizeof(float));
    }
    return out;
}

//------------------------------------------------------------------------------
// icall_array_resize_d -- Array.Resize<double>(ref double[], int newSize): the
// double twin of icall_array_resize_i4 (the same net48 contract and rooting).
//------------------------------------------------------------------------------
void icall_array_resize_d(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return; // unreachable (the factory throws)
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }

    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        *arraySlot = il2cpp_array_new_typed(sizeof(double), 4, newSize);
        return;
    }
    RequireFindIndexArray(old);
    if (old->isRefElem || old->elemSize != sizeof(double))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return; // net48: an equal length keeps the same reference (no-op)
    }

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));

    Il2CppArray* fresh = il2cpp_array_new_typed(sizeof(double), 4, newSize);
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    if (copyLen > 0)
    {
        std::memcpy(fresh->data, moved->data,
                    static_cast<size_t>(copyLen) * sizeof(double));
    }
    *arraySlot = fresh;
}

//------------------------------------------------------------------------------
// icall_array_resize_f -- Array.Resize<float>(ref float[], int newSize): the
// float twin of icall_array_resize_d (the same net48 contract and rooting).
//------------------------------------------------------------------------------
void icall_array_resize_f(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return; // unreachable (the factory throws)
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }

    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        *arraySlot = il2cpp_array_new_typed(sizeof(float), 3, newSize);
        return;
    }
    RequireFindIndexArray(old);
    if (old->isRefElem || old->elemSize != sizeof(float))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return; // net48: an equal length keeps the same reference (no-op)
    }

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));

    Il2CppArray* fresh = il2cpp_array_new_typed(sizeof(float), 3, newSize);
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    if (copyLen > 0)
    {
        std::memcpy(fresh->data, moved->data,
                    static_cast<size_t>(copyLen) * sizeof(float));
    }
    *arraySlot = fresh;
}

//------------------------------------------------------------------------------
// icall_array_resize_i4 -- Array.Resize<int>(ref int32[], int newSize) (see
// header). net48 semantics: newSize < 0 -> ArgumentOutOfRangeException; a
// null array is legal (becomes new int32[newSize]); an equal length is a
// no-op (the same reference is kept). Otherwise 'array' is replaced by a NEW
// int32[] of exactly 'newSize' elements holding the first min(oldLength,
// newSize) elements of the old array. The caller's array slot is registered
// as a GC root across the allocation, so a compaction rewrites the slot if
// the old array moves; the copy source is re-read through the slot after the
// allocation.
//------------------------------------------------------------------------------
void icall_array_resize_i4(Il2CppArray** arraySlot, int32_t newSize)
{
    if (arraySlot == nullptr)
    {
        il2cpp_throw_argument_exception("Array slot must not be null.");
        return; // unreachable (the factory throws)
    }
    if (newSize < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "newSize", "newSize was out of range. Must be non-negative.");
        return;
    }

    Il2CppArray* old = *arraySlot;
    if (old == nullptr)
    {
        // net48: a null array becomes new T[newSize] (never stays null).
        *arraySlot = il2cpp_array_new(nullptr, sizeof(int32_t), false, newSize);
        return;
    }
    RequireFindIndexArray(old);
    RequireInt32ArrayExt2(old);
    const int32_t oldLength = old->length;
    if (oldLength == newSize)
    {
        return; // net48: an equal length keeps the same reference (no-op)
    }

    RootScopeExt2 roots;
    roots.Add(reinterpret_cast<void**>(arraySlot));

    Il2CppArray* fresh = il2cpp_array_new(nullptr, sizeof(int32_t), false, newSize);
    // The old array is re-read through the rooted slot AFTER the allocation:
    // a collection may have moved it (the collector rewrote the slot).
    Il2CppArray* moved = *arraySlot;
    const int32_t copyLen = oldLength < newSize ? oldLength : newSize;
    if (copyLen > 0)
    {
        std::memcpy(fresh->data, moved->data,
                    static_cast<size_t>(copyLen) * sizeof(int32_t));
    }
    *arraySlot = fresh;
}

//------------------------------------------------------------------------------
// icall_array_empty_* -- Array.Empty<T>() (see the header). Zero-length array
// allocation: the same shape the transpiler's newarr emits with a constant
// length 0 (elemClass null, il2cpp_array.h). Fresh allocation per call
// (documented deviation: .NET caches a per-T singleton).
//------------------------------------------------------------------------------
Il2CppObject* icall_array_empty_char()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(char16_t), false, 0));
}

Il2CppObject* icall_array_empty_int32()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(int32_t), false, 0));
}

Il2CppObject* icall_array_empty_string()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(void*), true, 0));
}

Il2CppObject* icall_array_empty_object()
{
    // Array.Empty<T>() for a CLASS element (0x12) -- an empty object[] (the
    // element type is irrelevant for a zero-length array).
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(void*), true, 0));
}

// W7: the long/double empty factories (elemSize 8, non-reference).
Il2CppObject* icall_array_empty_long()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new_typed(sizeof(int64_t), 2, 0));
}

Il2CppObject* icall_array_empty_double()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new_typed(sizeof(double), 4, 0));
}

// The float / narrow-integral empty factories (elemSize follows the element
// type; the narrow families have no dedicated elemKind, matching the existing
// char factory's plain allocation).
Il2CppObject* icall_array_empty_float()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new_typed(sizeof(float), 3, 0));
}

Il2CppObject* icall_array_empty_sbyte()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(int8_t), false, 0));
}

Il2CppObject* icall_array_empty_byte()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(uint8_t), false, 0));
}

Il2CppObject* icall_array_empty_bool()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(bool), false, 0));
}

Il2CppObject* icall_array_empty_short()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(int16_t), false, 0));
}

Il2CppObject* icall_array_empty_ushort()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(uint16_t), false, 0));
}

Il2CppObject* icall_array_empty_uint()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(uint32_t), false, 0));
}

Il2CppObject* icall_array_empty_uint64()
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(nullptr, sizeof(uint64_t), false, 0));
}

} // namespace runtime
} // namespace il2cpp
