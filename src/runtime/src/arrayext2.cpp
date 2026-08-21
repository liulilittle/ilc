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
#include <cstring>

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
// LowerBoundInt32 -- lower_bound on an ascending int32 array (half-open
// [lo, hi) scan): the index of the FIRST element >= 'value', or 'hi' when
// every element is smaller. This is both the match position and the
// .NET insertion point.
//------------------------------------------------------------------------------
int32_t LowerBoundInt32(const int32_t* data, int32_t lo, int32_t hi, int32_t value)
{
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (data[mid] < value)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    return lo;
}

//------------------------------------------------------------------------------
// LowerBoundString -- lower_bound on a string (reference) array using the
// culture comparison. 'value' may be null (sorts before everything).
//------------------------------------------------------------------------------
int32_t LowerBoundString(Il2CppObject** slots, int32_t lo, int32_t hi,
                         const Il2CppString* value)
{
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const Il2CppString* element = reinterpret_cast<Il2CppString*>(slots[mid]);
        if (CompareCulture(element, value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    return lo;
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
// header). Lower-bound scan; a miss returns the complement of the insertion
// point. An empty array always returns -1 (= ~0).
//------------------------------------------------------------------------------
int32_t icall_array_binary_search_int32(Il2CppObject* arr, int32_t value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireInt32ArrayExt2(a);
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    const int32_t at = LowerBoundInt32(data, 0, a->length, value);
    if (at < a->length && data[at] == value)
    {
        return at;
    }
    return ~at; // missing: complement of the insertion point
}

//------------------------------------------------------------------------------
// icall_array_binary_search_string -- Array.BinarySearch(string[]) (see
// header). Culture lower-bound scan; null 'value' matches only null
// elements.
//------------------------------------------------------------------------------
int32_t icall_array_binary_search_string(Il2CppObject* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireRefArrayExt2(a);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t at = LowerBoundString(slots, 0, a->length, needle);
    if (at < a->length && CompareCulture(
            reinterpret_cast<const Il2CppString*>(slots[at]), needle) == 0)
    {
        return at;
    }
    return ~at; // missing: complement of the insertion point
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
    RequireInt32ArrayExt2(a);
    if (index < 0 || length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            index < 0 ? "index" : "length",
            "Index or length is less than zero.");
    }
    if (static_cast<int64_t>(index) + static_cast<int64_t>(length) > a->length)
    {
        il2cpp_throw_argument_exception(
            "Offset and length were out of bounds for the array.");
    }
    if (length > 0)
    {
        std::memset(a->data + static_cast<size_t>(index) * sizeof(int32_t), 0,
                    static_cast<size_t>(length) * sizeof(int32_t));
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
// icall_array_reverse_range -- Array.Reverse(int32[], index, length) (see
// header). Validation as Clear; length 0/1 is a no-op. In-place two-pointer
// reversal of [index, index + length).
//------------------------------------------------------------------------------
void icall_array_reverse_range(Il2CppObject* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArrayExt2(arr);
    RequireInt32ArrayExt2(a);
    if (index < 0 || length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            index < 0 ? "index" : "length",
            "Index or length is less than zero.");
    }
    if (static_cast<int64_t>(index) + static_cast<int64_t>(length) > a->length)
    {
        il2cpp_throw_argument_exception(
            "Offset and length were out of bounds for the array.");
    }
    int32_t* data = reinterpret_cast<int32_t*>(a->data);
    for (int32_t i = index, j = index + length - 1; i < j; ++i, --j)
    {
        const int32_t tmp = data[i];
        data[i] = data[j];
        data[j] = tmp;
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

} // namespace runtime
} // namespace il2cpp
