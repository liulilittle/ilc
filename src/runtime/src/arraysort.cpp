//------------------------------------------------------------------------------
// arraysort.cpp -- System.Array direct-call API (see arraysort.h for the full
// contract, the ordinal string comparison semantics, the BinarySearch
// complement rule and the comparison-callback shape).
//
// Implementation notes:
//   - Every entry point receives Il2CppArray* (the generated code's array
//     reference) and validates the array layout before dereferencing any
//     field: null -> ArgumentNullException, a non-array object (klass name
//     without the '[' array-class marker, array.cpp convention) ->
//     ArgumentException. Validation helpers are file-local with a Sort
//     suffix so they never collide with the icall domains' helpers.
//   - The numeric sorts share ONE templated Hoare-partition quicksort
//     (int32/uint32/int64 differ only in the element type and the raw <
//     comparison). Middle-element pivot; the smaller half is recursed, the
//     larger half iterated, so the recursion depth is O(log n) for every
//     input ordering.
//   - The reference sorts (string / object-callback) share ONE templated
//     quicksort over Il2CppObject* slots driven by a comparator function
//     pointer (negative/zero/positive). Every swap runs the write barrier on
//     both slots (GC contract, see il2cpp_gc.h); the sorts allocate nothing,
//     so no collection can interleave and no rooting is needed.
//   - BinarySearch uses a lower-bound scan (half-open [lo, hi)): the result
//     is the first matching index or the insertion point, whose complement
//     is returned for a miss -- exactly .NET Array.BinarySearch.
//   - il2cpp_array_copy_to validates (null args, negative index, capacity in
//     64-bit so int32 overflow can never bypass it, element-kind match) then
//     copies with memmove semantics (overlap-safe). Reference-element copies
//     run the write barrier on every written destination slot AFTER the bulk
//     move (barrier depends only on the destination block's generation,
//     which does not change during the copy).
//------------------------------------------------------------------------------

#include "arraysort.h"

#include "il2cpp_boxing_ext.h"
#include "il2cpp_delegate.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "typeinfo.h"

#include <cstdint>
#include <cstring>

//------------------------------------------------------------------------------
// D-ICMP-2 (icomparedeep): the net48 System.Collections.Comparer::Default
// static-readonly-field storage (arraysort.h declares the extern). The value
// is the default-comparer singleton defined below -- il2cpp_comparer_compare /
// il2cpp_array_sort_icomparer identify the default by this pointer identity.
// Global scope, matching the transpiler's FieldStorageName symbol.
//------------------------------------------------------------------------------
namespace il2cpp {
extern Il2CppObject System_Collections_Comparer_singleton;
}
il2cpp::Il2CppObject* System_Collections_Comparer_sf_Default =
    &il2cpp::System_Collections_Comparer_singleton;

namespace il2cpp {

//------------------------------------------------------------------------------
// The System.Collections.Comparer class descriptor + singleton (D-ICMP-2).
// The singleton carries the name-bearing klass (so il2cpp_comparer_compare /
// il2cpp_array_sort_icomparer can identify it by pointer identity); it is a
// static object -- never heap-allocated, never moved by the collector.
//------------------------------------------------------------------------------
Il2CppClass System_Collections_Comparer_class = {
    "System.Collections.Comparer",               // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                          // refFieldBitmap
    0,                                           // refFieldCount
    nullptr,                                     // baseClass (System.Object root)
    nullptr,                                     // vtable
    0,                                           // vtableSize
    nullptr,                                     // interfaces
    0,                                           // interfaceCount
};

Il2CppObject System_Collections_Comparer_singleton = {
    &System_Collections_Comparer_class,
};

namespace {

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

// The System.String descriptor name (string.cpp registration).
const char kStringTypeName[] = "System.String";

// ArrayTypeMismatchException chain (type + base classes, exception.cpp
// catch-matcher walks the chain after the first comma). The type is
// REPEATED at position 1 -- the catch matcher's chain walk starts after the
// first comma and must find the exact type itself before its bases
// (the exceptionext.cpp factory convention; arrayext.cpp's kTypeMismatchChain
// is the same 4-entry shape).
const char kTypeMismatchChain[] = "System.ArrayTypeMismatchException,System.ArrayTypeMismatchException,System.SystemException,System.Exception";

//------------------------------------------------------------------------------
// RequireArraySort -- validate a direct-call array argument.
//   null                -> ArgumentNullException
//   non-array object    -> ArgumentException (caller bug, never UB)
//------------------------------------------------------------------------------
Il2CppArray* RequireArraySort(Il2CppArray* arr)
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
// RequireValueArraySort -- value-array domain contract: isRefElem == false
// and the exact element stride (4 for int32/uint32, 8 for int64). Anything
// else is unsupported in this domain.
//------------------------------------------------------------------------------
Il2CppArray* RequireValueArraySort(Il2CppArray* arr, int32_t elemSize)
{
    if (arr->isRefElem || arr->elemSize != elemSize)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional value-element arrays of the expected stride are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireRefArraySort -- reference-array-only domain contract (string /
// object sorts and searches). The elements are Il2CppObject* slots; the
// element class descriptor is NOT consulted (tests and generated code may
// create reference arrays with a null element descriptor).
//------------------------------------------------------------------------------
Il2CppArray* RequireRefArraySort(Il2CppArray* arr)
{
    if (!arr->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional reference-element arrays are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireRangeSort -- validate the (index, length) range of a PARTIAL
// sort/reverse (net48 Array.Sort<T>(T[], int, int) contract). index < 0 or
// length < 0 -> ArgumentOutOfRangeException; index + length > Length ->
// ArgumentException. The array itself has already been null/kind-checked by
// the caller; the 64-bit sum makes int32 overflow impossible.
//------------------------------------------------------------------------------
void RequireRangeSort(const Il2CppArray* arr, int32_t index, int32_t length)
{
    if (index < 0 || length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "index", "Non-negative number required.");
    }
    if (static_cast<int64_t>(index) + static_cast<int64_t>(length) >
        static_cast<int64_t>(arr->length))
    {
        il2cpp_throw_argument_exception(
            "offset and length were out of bounds for the array or count is "
            "greater than the number of elements from index to the end of the "
            "source collection.");
    }
}

//------------------------------------------------------------------------------
// CompareOrdinal -- static ordinal comparison of two managed strings (null
// policy: null < any non-null string, null == null -- the
// Comparer<string>.Default contract). The first differing UTF-16 unit
// decides (the unit difference is the numeric result); a prefix pair is
// decided by the length difference. Reads only the payloads -- pure CPU,
// no allocation.
//------------------------------------------------------------------------------
int32_t CompareOrdinal(const Il2CppString* a, const Il2CppString* b)
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
        // The difference of two 16-bit units always fits in int32_t.
        const int32_t diff = static_cast<int32_t>(a->chars[i]) -
                             static_cast<int32_t>(b->chars[i]);
        if (diff != 0)
        {
            return diff;
        }
    }
    return a->length - b->length; // prefix: shorter < longer; equal -> 0
}

// Comparison<T> callback shape used by the reference sorts/searches.
using CompareRefFn = int32_t (*)(Il2CppObject* left, Il2CppObject* right);

// String comparator over Il2CppObject* slots (the string sorts/searches'
// CompareRefFn). Elements are read as Il2CppString*; a non-string slot is a
// transpiler-guarantee violation (see the header contract).
int32_t CompareStringRefs(Il2CppObject* left, Il2CppObject* right)
{
    return CompareOrdinal(reinterpret_cast<const Il2CppString*>(left),
                          reinterpret_cast<const Il2CppString*>(right));
}

//------------------------------------------------------------------------------
// QuickSortValues -- Hoare-partition quicksort on data[lo..hi] (inclusive),
// templated over the element type (int32/uint32/int64 share this core).
//   Middle-element pivot: already-sorted input (the common case) splits
//   evenly. The smaller half is recursed, the larger half iterated, so the
//   recursion depth is O(log n) for every input ordering.
//------------------------------------------------------------------------------
template <typename T>
void QuickSortValues(T* data, int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        const T pivot = data[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (data[i] < pivot)
            {
                ++i;
            }
            while (data[j] > pivot)
            {
                --j;
            }
            if (i <= j)
            {
                const T tmp = data[i];
                data[i] = data[j];
                data[j] = tmp;
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortValues(data, lo, j);
            lo = i;
        }
        else
        {
            QuickSortValues(data, i, hi);
            hi = j;
        }
    }
}

//------------------------------------------------------------------------------
// QuickSortRefs -- Hoare-partition quicksort on Il2CppObject* slots[lo..hi]
// (inclusive), ordered by a comparator callback (negative/zero/positive).
// The pivot VALUE is captured before any swap (the pivot slot may move
// during the partition). Every swap runs the write barrier on both slots
// (an old-generation array's card must be marked; see the file header).
//------------------------------------------------------------------------------
void QuickSortRefs(Il2CppObject** slots, int32_t lo, int32_t hi,
                   CompareRefFn compare)
{
    while (lo < hi)
    {
        Il2CppObject* const pivot = slots[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (compare(slots[i], pivot) < 0)
            {
                ++i;
            }
            while (compare(slots[j], pivot) > 0)
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
            QuickSortRefs(slots, lo, j, compare);
            lo = i;
        }
        else
        {
            QuickSortRefs(slots, i, hi, compare);
            hi = j;
        }
    }
}

//------------------------------------------------------------------------------
// LowerBoundValues -- lower_bound on an ascending value array (half-open
// [lo, hi) scan): the index of the FIRST element >= 'value', or 'hi' when
// every element is smaller. This is both the match position and the .NET
// insertion point.
//------------------------------------------------------------------------------
template <typename T>
int32_t LowerBoundValues(const T* data, int32_t lo, int32_t hi, T value)
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
// ReverseValues -- in-place reversal of data[start .. start+count-1] (a
// net48 Array.Reverse range). Templated over the element type; 'start' and
// 'count' are already range-validated by the caller.
//------------------------------------------------------------------------------
template <typename T>
void ReverseValues(T* data, int32_t start, int32_t count)
{
    for (int32_t lo = start, hi = start + count - 1; lo < hi; ++lo, --hi)
    {
        const T tmp = data[lo];
        data[lo] = data[hi];
        data[hi] = tmp;
    }
}

//------------------------------------------------------------------------------
// ReverseRefs -- in-place reversal of Il2CppObject* slots[start .. start+
// count-1]. Every written slot runs the write barrier on both sides of the
// swap (same GC contract as QuickSortRefs).
//------------------------------------------------------------------------------
void ReverseRefs(Il2CppObject** slots, int32_t start, int32_t count)
{
    for (int32_t lo = start, hi = start + count - 1; lo < hi; ++lo, --hi)
    {
        Il2CppObject* tmp = slots[lo];
        slots[lo] = slots[hi];
        slots[hi] = tmp;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[lo]), slots[lo]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[hi]), slots[hi]);
    }
}

//------------------------------------------------------------------------------
// LowerBoundRefs -- lower_bound on an ascending reference array using a
// comparator callback.
//------------------------------------------------------------------------------
int32_t LowerBoundRefs(Il2CppObject** slots, int32_t lo, int32_t hi,
                       Il2CppObject* value, CompareRefFn compare)
{
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (compare(slots[mid], value) < 0)
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
// AsStringOrNull -- interpret a string-domain value argument as a managed
// string. null stays null (a legal value: matches only null slots); any
// non-null object whose klass is not System.String raises
// ArgumentException (defensive -- reading a non-string payload as
// length/chars would be undefined behavior).
//------------------------------------------------------------------------------
Il2CppString* AsStringOrNull(Il2CppObject* value)
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
    return reinterpret_cast<Il2CppString*>(value);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_array_sort_int32/uint32/int64 -- Array.Sort(T[]) (see header).
// Typed ascending quicksort over the raw element values.
//------------------------------------------------------------------------------
void il2cpp_array_sort_int32(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    if (a->length > 1)
    {
        QuickSortValues(reinterpret_cast<int32_t*>(a->data), 0, a->length - 1);
    }
}

void il2cpp_array_sort_uint32(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(uint32_t));
    if (a->length > 1)
    {
        QuickSortValues(reinterpret_cast<uint32_t*>(a->data), 0, a->length - 1);
    }
}

void il2cpp_array_sort_int64(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int64_t));
    if (a->length > 1)
    {
        QuickSortValues(reinterpret_cast<int64_t*>(a->data), 0, a->length - 1);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_string -- Array.Sort(string[]) (see header). Ordinal
// quicksort of the reference slots; null elements sort first.
//------------------------------------------------------------------------------
void il2cpp_array_sort_string(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    if (a->length > 1)
    {
        QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), 0, a->length - 1,
                      CompareStringRefs);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_object -- Array.Sort<T>(T[], Comparison<T>) (see
// header). Reference-array quicksort driven by the caller's comparator
// callback (the transpiler passes the compiled Comparison<T> as a plain
// function pointer). A null callback is an ArgumentNullException; a
// callback that is not a strict weak ordering is undefined (as in .NET).
//------------------------------------------------------------------------------
void il2cpp_array_sort_object(Il2CppArray* arr, ArrayCompareFn compare)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    if (compare == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    if (a->length > 1)
    {
        QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), 0, a->length - 1,
                      compare);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_int32/uint32/int64/string (index, length) -- the net48
// PARTIAL-sort range overloads (Array.Sort<T>(T[], int, int)): only
// [index, index+length) is ordered, head/tail untouched. Validation order:
// null array, element-kind, then the (index, length) range -- see
// RequireRangeSort for the exact exception mapping.
//------------------------------------------------------------------------------
void il2cpp_array_sort_int32(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        QuickSortValues(reinterpret_cast<int32_t*>(a->data), index,
                        index + length - 1);
    }
}

void il2cpp_array_sort_uint32(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(uint32_t));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        QuickSortValues(reinterpret_cast<uint32_t*>(a->data), index,
                        index + length - 1);
    }
}

void il2cpp_array_sort_int64(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int64_t));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        QuickSortValues(reinterpret_cast<int64_t*>(a->data), index,
                        index + length - 1);
    }
}

void il2cpp_array_sort_string(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), index,
                      index + length - 1, CompareStringRefs);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_object(index, length, compare) -- the net48 PARTIAL
// callback sort (Array.Sort<T>(T[], int, int, Comparison<T>)). The range is
// validated (index/length/range) BEFORE the null-callback check, matching
// .NET's validation order.
//------------------------------------------------------------------------------
void il2cpp_array_sort_object(Il2CppArray* arr, int32_t index, int32_t length,
                              ArrayCompareFn compare)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    if (compare == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    if (length > 1)
    {
        QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), index,
                      index + length - 1, compare);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_reverse_int32/uint32/int64/string/object (index, length) --
// the net48 PARTIAL-reverse range overloads (Array.Reverse<T>(T[], int,
// int)): only [index, index+length) is reversed; reversing a range twice
// restores the original order; length 0/1 ranges are validated no-ops.
// Validation order: null array, element-kind, then the range.
//------------------------------------------------------------------------------
void il2cpp_array_reverse_int32(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        ReverseValues(reinterpret_cast<int32_t*>(a->data), index, length);
    }
}

void il2cpp_array_reverse_uint32(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(uint32_t));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        ReverseValues(reinterpret_cast<uint32_t*>(a->data), index, length);
    }
}

void il2cpp_array_reverse_int64(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int64_t));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        ReverseValues(reinterpret_cast<int64_t*>(a->data), index, length);
    }
}

void il2cpp_array_reverse_string(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        ReverseRefs(reinterpret_cast<Il2CppObject**>(a->data), index, length);
    }
}

void il2cpp_array_reverse_object(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        ReverseRefs(reinterpret_cast<Il2CppObject**>(a->data), index, length);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_copy_to -- Array.CopyTo(Array, int) (see header).
// Validation order: null src/dst, negative dstIndex, capacity overflow
// (64-bit sum), element-kind mismatch -- then the copy. An empty src is a
// validated no-op (dstIndex == dst->length is legal, matching .NET).
//------------------------------------------------------------------------------
void il2cpp_array_copy_to(Il2CppArray* src, Il2CppArray* dst, int32_t dstIndex)
{
    Il2CppArray* s = RequireArraySort(src);
    Il2CppArray* d = RequireArraySort(dst);
    if (dstIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "index", "Index was less than the array's lower bound.");
    }
    if (static_cast<int64_t>(s->length) >
        static_cast<int64_t>(d->length) - static_cast<int64_t>(dstIndex))
    {
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check the destination index, length, and the array's lower bounds.");
    }
    if (s->isRefElem != d->isRefElem || s->elemSize != d->elemSize)
    {
        il2cpp_throw_new(kTypeMismatchChain,
                         "Source and destination arrays have incompatible element types.");
    }
    if (s->length == 0)
    {
        return; // validated no-op
    }

    if (s->isRefElem)
    {
        // Reference-element copy: move the Il2CppObject* slots themselves
        // (identity -- never the pointees), then run the write barrier on
        // every written destination slot (the card depends only on the
        // destination block's generation, which does not change during the
        // copy -- marking after the move is equivalent, and no collection
        // can interleave: the copy allocates nothing).
        char* sPtr = s->data;
        char* dPtr = d->data + static_cast<size_t>(dstIndex) * sizeof(void*);
        std::memmove(dPtr, sPtr, static_cast<size_t>(s->length) * sizeof(void*));
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(dPtr);
        for (int32_t i = 0; i < s->length; ++i)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        }
    }
    else
    {
        // Value-element copy: raw element bytes.
        std::memmove(d->data + static_cast<size_t>(dstIndex) * static_cast<size_t>(s->elemSize),
                     s->data,
                     static_cast<size_t>(s->length) * static_cast<size_t>(s->elemSize));
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_index_of_int32/int64 -- Array.IndexOf<T> (see header).
// Linear first-match scan; -1 when absent (also for an empty array).
//------------------------------------------------------------------------------
int32_t il2cpp_array_index_of_int32(Il2CppArray* arr, int32_t value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
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

int32_t il2cpp_array_index_of_int64(Il2CppArray* arr, int64_t value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int64_t));
    const int64_t* data = reinterpret_cast<const int64_t*>(a->data);
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
// il2cpp_array_index_of_string -- Array.IndexOf<string> (see header).
// Linear first-match scan with the ordinal comparison; a null 'value'
// matches the first null slot (EqualityComparer<string>.Default semantics).
//------------------------------------------------------------------------------
int32_t il2cpp_array_index_of_string(Il2CppArray* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[i]),
                           needle) == 0)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// il2cpp_array_binary_search_int32/int64 -- Array.BinarySearch<T> (see
// header). Lower-bound scan; a miss returns the complement of the insertion
// point. An empty array always returns -1 (= ~0).
//------------------------------------------------------------------------------
int32_t il2cpp_array_binary_search_int32(Il2CppArray* arr, int32_t value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    const int32_t at = LowerBoundValues(data, 0, a->length, value);
    if (at < a->length && data[at] == value)
    {
        return at;
    }
    return ~at; // missing: complement of the insertion point
}

int32_t il2cpp_array_binary_search_int64(Il2CppArray* arr, int64_t value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int64_t));
    const int64_t* data = reinterpret_cast<const int64_t*>(a->data);
    const int32_t at = LowerBoundValues(data, 0, a->length, value);
    if (at < a->length && data[at] == value)
    {
        return at;
    }
    return ~at; // missing: complement of the insertion point
}

//------------------------------------------------------------------------------
// il2cpp_array_binary_search_string -- Array.BinarySearch<string> (see
// header). Ordinal lower-bound scan; a null 'value' matches only null
// elements.
//------------------------------------------------------------------------------
int32_t il2cpp_array_binary_search_string(Il2CppArray* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t at = LowerBoundRefs(slots, 0, a->length,
                                      reinterpret_cast<Il2CppObject*>(needle),
                                      CompareStringRefs);
    if (at < a->length && CompareOrdinal(
            reinterpret_cast<const Il2CppString*>(slots[at]), needle) == 0)
    {
        return at;
    }
    return ~at; // missing: complement of the insertion point
}

//==============================================================================
// D-ICMP (icomparedeep): IComparable/IComparer dispatch + Comparer.Default +
// the comparer-driven Array.Sort overloads (arraysort.h header contract).
//==============================================================================

namespace {

//------------------------------------------------------------------------------
// RootScopeSort -- GC rooting scope for the comparer/delegate sort callbacks
// (the arrayext2.cpp RootScopeExt2 precedent). The callback may box or invoke
// user code, so a compaction can move the array, the comparer/delegate and the
// boxed temporaries between comparisons; the locals are registered as
// static-root slots so the collector rewrites the LOCALS themselves. Removal is
// RAII -- the callback may throw a managed exception.
//------------------------------------------------------------------------------
class RootScopeSort
{
public:
    RootScopeSort() : m_count(0) {}
    ~RootScopeSort()
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
    void** m_slots[3];
    int32_t m_count;
};

//------------------------------------------------------------------------------
// Safe element accessors -- every read/write re-derives the array base through
// the ROOTED array slot (*pa) so a compaction between comparisons is picked up
// (the collector rewrites *pa; get_elem_ptr then reads the new data pointer).
//------------------------------------------------------------------------------
int32_t ReadI4Elem(Il2CppArray** pa, int32_t idx)
{
    return *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(*pa, idx));
}

void WriteI4Elem(Il2CppArray** pa, int32_t idx, int32_t v)
{
    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(*pa, idx)) = v;
}

Il2CppObject* ReadRefElem(Il2CppArray** pa, int32_t idx)
{
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(*pa, idx));
}

void WriteRefElem(Il2CppArray** pa, int32_t idx, Il2CppObject* v)
{
    void** slot = reinterpret_cast<void**>(il2cpp_array_get_elem_ptr(*pa, idx));
    *slot = v;
    il2cpp_write_barrier(slot, v);
}

//------------------------------------------------------------------------------
// QuickSortI4Array / QuickSortRefArray -- Hoare-partition quicksorts over the
// ROOTED array slot (safe element accessors above; recursion keeps only the
// indices, so a compaction never invalidates the sort state). The compare
// callback is invoked with raw int32 values / Il2CppObject* slots.
//------------------------------------------------------------------------------
template <typename CompareI4>
void QuickSortI4Array(Il2CppArray** pa, int32_t lo, int32_t hi, CompareI4 compare)
{
    while (lo < hi)
    {
        const int32_t pivot = ReadI4Elem(pa, lo + (hi - lo) / 2);
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (compare(ReadI4Elem(pa, i), pivot) < 0)
            {
                ++i;
            }
            while (compare(ReadI4Elem(pa, j), pivot) > 0)
            {
                --j;
            }
            if (i <= j)
            {
                const int32_t tmp = ReadI4Elem(pa, i);
                WriteI4Elem(pa, i, ReadI4Elem(pa, j));
                WriteI4Elem(pa, j, tmp);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortI4Array(pa, lo, j, compare);
            lo = i;
        }
        else
        {
            QuickSortI4Array(pa, i, hi, compare);
            hi = j;
        }
    }
}

template <typename CompareRef>
void QuickSortRefArray(Il2CppArray** pa, int32_t lo, int32_t hi, CompareRef compare)
{
    while (lo < hi)
    {
        Il2CppObject* const pivot = ReadRefElem(pa, lo + (hi - lo) / 2);
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (compare(ReadRefElem(pa, i), pivot) < 0)
            {
                ++i;
            }
            while (compare(ReadRefElem(pa, j), pivot) > 0)
            {
                --j;
            }
            if (i <= j)
            {
                Il2CppObject* tmp = ReadRefElem(pa, i);
                WriteRefElem(pa, i, ReadRefElem(pa, j));
                WriteRefElem(pa, j, tmp);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortRefArray(pa, lo, j, compare);
            lo = i;
        }
        else
        {
            QuickSortRefArray(pa, i, hi, compare);
            hi = j;
        }
    }
}

//------------------------------------------------------------------------------
// FindVirtualSlotByName -- resolve a virtual slot by METHOD NAME through the
// registered typeinfo (typeinfo.h). The transpiler emits one typeinfo per
// user type with the per-method vtable slot; a boxed builtin has no typeinfo
// (returns -1). Returns -1 when the receiver type has no reflected virtual of
// that name.
//------------------------------------------------------------------------------
int32_t FindVirtualSlotByName(Il2CppObject* obj, const char* name)
{
    if (obj == nullptr || obj->klass == nullptr || obj->klass->name == nullptr)
    {
        return -1;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(obj->klass->name);
    if (info == nullptr)
    {
        return -1;
    }
    const Il2CppMethodInfo* m = il2cpp_type_get_method(info, name);
    if (m == nullptr)
    {
        return -1;
    }
    return m->virtualSlot;
}

// The IComparable.CompareTo ABI: int32_t (*)(this, other).
using IComparableCompareFn = int32_t (*)(Il2CppObject*, Il2CppObject*);

// The IComparer.Compare ABI: int32_t (*)(this, a, b).
using IComparerCompareFn = int32_t (*)(Il2CppObject*, Il2CppObject*, Il2CppObject*);

//------------------------------------------------------------------------------
// ComparerCompareCore -- the DEFAULT comparer's Compare(object, object) (net48
// Comparer semantics): null first (null == null, null < non-null), string
// pairs ordinal, builtin boxes by value, everything else falls back to the
// first argument's IComparable.CompareTo.
//------------------------------------------------------------------------------
int32_t ComparerCompareCore(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr)
    {
        return b == nullptr ? 0 : -1; // null sorts before any non-null object
    }
    if (b == nullptr)
    {
        return 1;
    }
    if (a->klass == nullptr || a->klass->name == nullptr)
    {
        return 0;
    }
    const char* const an = a->klass->name;
    if (std::strcmp(an, kStringTypeName) == 0)
    {
        // String pair: ordinal (the ILC divergence, sortdeep precedent). A
        // non-string 'b' with a string 'a' is a .NET type mismatch that this
        // domain treats as an ordinal-vs-garbage hazard -- validate the second.
        return CompareOrdinal(AsStringOrNull(a), AsStringOrNull(b));
    }
    if (std::strcmp(an, "System.Int32") == 0)
    {
        const int32_t x = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const int32_t y = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        return x < y ? -1 : (x > y ? 1 : 0);
    }
    if (std::strcmp(an, "System.UInt32") == 0)
    {
        const uint32_t x = static_cast<uint32_t>(*reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*)));
        const uint32_t y = static_cast<uint32_t>(*reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*)));
        return x < y ? -1 : (x > y ? 1 : 0);
    }
    if (std::strcmp(an, "System.Int64") == 0 || std::strcmp(an, "System.UInt64") == 0)
    {
        const uint64_t x = *reinterpret_cast<uint64_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const uint64_t y = *reinterpret_cast<uint64_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        return x < y ? -1 : (x > y ? 1 : 0);
    }
    if (std::strcmp(an, "System.Double") == 0 || std::strcmp(an, "System.Single") == 0)
    {
        const double x = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const double y = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        return x < y ? -1 : (x > y ? 1 : 0);
    }
    if (std::strcmp(an, "System.Char") == 0)
    {
        const int32_t x = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const int32_t y = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        return x < y ? -1 : (x > y ? 1 : 0);
    }
    if (std::strcmp(an, "System.Boolean") == 0)
    {
        const bool x = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*)) != 0;
        const bool y = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*)) != 0;
        return x < y ? -1 : (x > y ? 1 : 0);
    }
    // net48 fallback: ((IComparable)a).CompareTo(b).
    return il2cpp_icomparable_compare_to(a, b);
}

//------------------------------------------------------------------------------
// Boxed-int32 rooting scope -- the two temporaries are rooted across the
// comparer Compare call (boxing may compact; the compare may allocate).
//------------------------------------------------------------------------------
class RootedI4Boxes
{
public:
    RootedI4Boxes() : m_a(nullptr), m_b(nullptr)
    {
        il2cpp_gc_add_static_root(reinterpret_cast<void**>(&m_a));
        il2cpp_gc_add_static_root(reinterpret_cast<void**>(&m_b));
    }
    ~RootedI4Boxes()
    {
        il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&m_b));
        il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&m_a));
    }
    Il2CppObject* a() { return m_a; }
    Il2CppObject* b() { return m_b; }
    void set_a(Il2CppObject* v) { m_a = v; }
    void set_b(Il2CppObject* v) { m_b = v; }

private:
    Il2CppObject* m_a;
    Il2CppObject* m_b;
};

// The comparer-driven int32 comparison (elements boxed per pair).
struct I4ComparerCompare
{
    Il2CppObject** comparerSlot; // the ROOTED comparer local
    bool           isDefault;

    I4ComparerCompare(Il2CppObject** slot, bool def)
        : comparerSlot(slot), isDefault(def)
    {
    }

    int32_t operator()(int32_t x, int32_t y) const
    {
        RootedI4Boxes boxes;
        boxes.set_a(il2cpp_box_int32(&System_Int32_class, x));
        boxes.set_b(il2cpp_box_int32(&System_Int32_class, y));
        return isDefault
            ? ComparerCompareCore(boxes.a(), boxes.b())
            : il2cpp_icomparer_compare(*comparerSlot, boxes.a(), boxes.b());
    }
};

// The comparer-driven reference comparison (elements are already objects).
struct RefComparerCompare
{
    Il2CppObject** comparerSlot; // the ROOTED comparer local
    bool           isDefault;

    RefComparerCompare(Il2CppObject** slot, bool def)
        : comparerSlot(slot), isDefault(def)
    {
    }

    int32_t operator()(Il2CppObject* x, Il2CppObject* y) const
    {
        return isDefault
            ? ComparerCompareCore(x, y)
            : il2cpp_icomparer_compare(*comparerSlot, x, y);
    }
};

// Comparison<T> delegate leaf ABIs (single-cast static methodPtr):
//   int32  leaf:  int32_t (*)(int32_t, int32_t)
//   int32  this :  int32_t (*)(Il2CppObject* target, int32_t, int32_t)
//   ref    leaf:  int32_t (*)(Il2CppObject*, Il2CppObject*)
//   ref    this :  int32_t (*)(Il2CppObject* target, Il2CppObject*, Il2CppObject*)
using I4ComparisonFn = int32_t (*)(int32_t, int32_t);
using I4ComparisonThisFn = int32_t (*)(Il2CppObject*, int32_t, int32_t);
using RefComparisonFn = int32_t (*)(Il2CppObject*, Il2CppObject*);
using RefComparisonThisFn = int32_t (*)(Il2CppObject*, Il2CppObject*, Il2CppObject*);

// The Comparison<int32> delegate-driven comparison.
struct I4ComparisonCompare
{
    Il2CppObject** comparisonSlot; // the ROOTED delegate local

    explicit I4ComparisonCompare(Il2CppObject** slot) : comparisonSlot(slot) {}

    int32_t operator()(int32_t x, int32_t y) const
    {
        Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(*comparisonSlot);
        return d->target != nullptr
            ? reinterpret_cast<I4ComparisonThisFn>(d->methodPtr)(d->target, x, y)
            : reinterpret_cast<I4ComparisonFn>(d->methodPtr)(x, y);
    }
};

// The Comparison<T> (reference element) delegate-driven comparison.
struct RefComparisonCompare
{
    Il2CppObject** comparisonSlot; // the ROOTED delegate local

    explicit RefComparisonCompare(Il2CppObject** slot) : comparisonSlot(slot) {}

    int32_t operator()(Il2CppObject* x, Il2CppObject* y) const
    {
        Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(*comparisonSlot);
        return d->target != nullptr
            ? reinterpret_cast<RefComparisonThisFn>(d->methodPtr)(d->target, x, y)
            : reinterpret_cast<RefComparisonFn>(d->methodPtr)(x, y);
    }
};

} // namespace

//------------------------------------------------------------------------------
// il2cpp_icomparable_compare_to -- IComparable.CompareTo interface dispatch
// (D-ICMP-1). See the header contract.
//------------------------------------------------------------------------------
int32_t il2cpp_icomparable_compare_to(Il2CppObject* receiver, Il2CppObject* other)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    const int32_t slot = FindVirtualSlotByName(receiver, "CompareTo");
    if (slot < 0 || receiver->klass->vtable == nullptr ||
        slot >= receiver->klass->vtableSize)
    {
        il2cpp_throw_not_supported_exception(
            "IComparable.CompareTo dispatch: the receiver type has no reflected "
            "virtual CompareTo slot.");
    }
    return reinterpret_cast<IComparableCompareFn>(receiver->klass->vtable[slot])(
        receiver, other);
}

//------------------------------------------------------------------------------
// il2cpp_icomparer_compare -- IComparer.Compare interface dispatch (D-ICMP-1).
//------------------------------------------------------------------------------
int32_t il2cpp_icomparer_compare(Il2CppObject* comparer, Il2CppObject* a,
                                 Il2CppObject* b)
{
    if (comparer == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    const int32_t slot = FindVirtualSlotByName(comparer, "Compare");
    if (slot < 0 || comparer->klass->vtable == nullptr ||
        slot >= comparer->klass->vtableSize)
    {
        il2cpp_throw_not_supported_exception(
            "IComparer.Compare dispatch: the comparer type has no reflected "
            "virtual Compare slot.");
    }
    return reinterpret_cast<IComparerCompareFn>(comparer->klass->vtable[slot])(
        comparer, a, b);
}

//------------------------------------------------------------------------------
// il2cpp_comparer_default -- System.Collections.Comparer::get_Default /
// get_DefaultInvariant (D-ICMP-2). Returns the singleton (registered so
// class_by_name / isinst can resolve it).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_comparer_default(void)
{
    il2cpp_class_register(&System_Collections_Comparer_class);
    return &System_Collections_Comparer_singleton;
}

//------------------------------------------------------------------------------
// il2cpp_comparer_compare -- System.Collections.Comparer::Compare(object,
// object) (D-ICMP-2). See the header contract.
//------------------------------------------------------------------------------
int32_t il2cpp_comparer_compare(Il2CppObject* self, Il2CppObject* a,
                                Il2CppObject* b)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    if (self != &System_Collections_Comparer_singleton)
    {
        // A non-singleton receiver is a user subclass of Comparer in net48;
        // this domain only models the default comparer -- treat the receiver
        // as the default (the Compare semantics are receiver-independent for
        // the builtin/default surface).
    }
    return ComparerCompareCore(a, b);
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_icomparer -- Array.Sort(Array, IComparer) (D-ICMP-3).
// See the header contract. The comparer/array are rooted across the sort; the
// safe quicksorts re-read the array base through the rooted slot.
//------------------------------------------------------------------------------
void il2cpp_array_sort_icomparer(Il2CppArray* arr, Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    const bool isDefault =
        comparer == nullptr || comparer == &System_Collections_Comparer_singleton;
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    if (a->length > 1)
    {
        if (a->isRefElem)
        {
            RefComparerCompare cmp(&comparer, isDefault);
            QuickSortRefArray(&a, 0, a->length - 1, cmp);
        }
        else if (a->elemSize == sizeof(int32_t))
        {
            I4ComparerCompare cmp(&comparer, isDefault);
            QuickSortI4Array(&a, 0, a->length - 1, cmp);
        }
        else
        {
            il2cpp_throw_not_supported_exception(
                "Only one-dimensional int32 or reference-element arrays are "
                "supported for the comparer sort.");
        }
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_int32_comparison -- Array.Sort<int>(int32[], Comparison<int>)
// (D-ICMP-3). Delegate-driven int32 sort (raw values, no boxing).
//------------------------------------------------------------------------------
void il2cpp_array_sort_int32_comparison(Il2CppArray* arr, Il2CppObject* comparison)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    if (comparison == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparison));
    if (a->length > 1)
    {
        I4ComparisonCompare cmp(&comparison);
        QuickSortI4Array(&a, 0, a->length - 1, cmp);
    }
}

//------------------------------------------------------------------------------
// il2cpp_array_sort_ref_comparison -- Array.Sort<T>(T[], Comparison<T>) for
// reference-element arrays (string/object/user classes) (D-ICMP-3).
//------------------------------------------------------------------------------
void il2cpp_array_sort_ref_comparison(Il2CppArray* arr, Il2CppObject* comparison)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    if (comparison == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparison));
    if (a->length > 1)
    {
        RefComparisonCompare cmp(&comparison);
        QuickSortRefArray(&a, 0, a->length - 1, cmp);
    }
}

} // namespace il2cpp

