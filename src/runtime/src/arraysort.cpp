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

#include "dictsi_obj.h" // P25d: Dictionary<string,int> wrapper + snap enumerator
#include "listobj.h" // ilistException: List<object> wrapper + snapshot enumerator re-use
#include "il2cpp_array_ext2.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h" // nb9 fix P0-4: icall_box_long (array-enumerator ref carrier)
#include "genericcomparer_obj.h" // nb9 H-B3: Comparer<T>.Create family dispatch
#include "il2cpp_delegate.h"
#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "linq_map.h" // nb9: GroupBy result/bucket enumerator dispatch
#include "string_compare.h" // a5reds: ordinal / ordinal-ignore-case helpers
#include "sortedlist.h" // P25d: array snapshot enumerator re-use
#include "sortedlistint_obj.h" // P3-extra: SortedList<string,int> values view enumerator
#include "typeinfo.h"
#include "il2cpp_value_semantics.h" // nb9 P0: runtime::icall_object_equals (ref-family scans)

#include <cstdint>
#include <cstdlib>
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

//------------------------------------------------------------------------------
// a5reds (strsort_ordinal): the System.StringComparer ordinal singletons.
// Same contract as the Comparer.Default singleton above: static objects with
// a name-bearing klass, identified by pointer identity in the comparer sort
// (il2cpp_array_sort_icomparer) and in the defensive forward in
// il2cpp_array_sort_ref_comparison. Registered lazily by the getters.
//------------------------------------------------------------------------------
Il2CppClass System_StringComparer_Ordinal_class = {
    "System.StringComparer.Ordinal",                     // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                          // refFieldBitmap
    0,                                           // refFieldCount
    nullptr,                                     // baseClass (System.Object root)
    nullptr,                                     // vtable
    0,                                           // vtableSize
    nullptr,                                     // interfaces
    0,                                           // interfaceCount
};

Il2CppObject System_StringComparer_Ordinal_singleton = {
    &System_StringComparer_Ordinal_class,
};

Il2CppClass System_StringComparer_OrdinalIgnoreCase_class = {
    "System.StringComparer.OrdinalIgnoreCase",           // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                          // refFieldBitmap
    0,                                           // refFieldCount
    nullptr,                                     // baseClass (System.Object root)
    nullptr,                                     // vtable
    0,                                           // vtableSize
    nullptr,                                     // interfaces
    0,                                           // interfaceCount
};

Il2CppObject System_StringComparer_OrdinalIgnoreCase_singleton = {
    &System_StringComparer_OrdinalIgnoreCase_class,
};

namespace {

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

// The System.String descriptor name (string.cpp registration).
const char kStringTypeName[] = "System.String";

// net48 ArraySortHelper / List.Sort comparer-failure wrap message (zh-CN
// oracle, see the patch header; UTF-8 byte escapes -- the exceptionext.cpp
// convention).
const char* kComparerFailedMessage =
    "\xE6\x9C\xAA\xE8\x83\xBD\xE6\xAF\x94\xE8\xBE\x83\xE6\x95\xB0\xE7\xBB\x84"
    "\xE4\xB8\xAD\xE7\x9A\x84\xE4\xB8\xA4\xE4\xB8\xAA\xE5\x85\x83\xE7\xB4\xA0"
    "\xE3\x80\x82";

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
    // nb9 D13: net48 reports ParamName "index" for a negative index and
    // "length" for a negative length.
    if (index < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "index", "Non-negative number required.");
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "length", "Non-negative number required.");
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
// W7: the net48 floating-point total order (Double.CompareTo /
// Single.CompareTo): NaN sorts before any non-NaN; NaN vs NaN equal; -0.0
// == +0.0. Plain </> cannot express this (NaN compares false both ways),
// so the double/float sorts and searches go through these verdicts.
//------------------------------------------------------------------------------
inline int32_t DoubleCompareTotal(double x, double y)
{
    if (x < y) return -1;
    if (x > y) return 1;
    if (x == y) return 0; // folds -0.0 == +0.0
    const bool xn = x != x;
    const bool yn = y != y;
    if (xn) return yn ? 0 : -1; // NaN first; NaN vs NaN equal
    return 1;
}

inline bool DoubleEqualsNet(double x, double y)
{
    // .NET Double.Equals: NaN == NaN true; +0.0 == -0.0 (== covers it).
    return x == y || (x != x && y != y);
}

// Hoare-partition quicksort over doubles in the total order above
// (same shape as QuickSortValues, ordered by verdict instead of </>).
void QuickSortDoubles(double* data, int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        const double pivot = data[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (DoubleCompareTotal(data[i], pivot) < 0)
            {
                ++i;
            }
            while (DoubleCompareTotal(data[j], pivot) > 0)
            {
                --j;
            }
            if (i <= j)
            {
                const double tmp = data[i];
                data[i] = data[j];
                data[j] = tmp;
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortDoubles(data, lo, j);
            lo = i;
        }
        else
        {
            QuickSortDoubles(data, i, hi);
            hi = j;
        }
    }
}

// Same total order over float elements (comparison widens to double --
// exact for every float bit pattern, including NaN and -0.0).
void QuickSortFloats(float* data, int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        const float pivot = data[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (DoubleCompareTotal(static_cast<double>(data[i]), static_cast<double>(pivot)) < 0)
            {
                ++i;
            }
            while (DoubleCompareTotal(static_cast<double>(data[j]), static_cast<double>(pivot)) > 0)
            {
                --j;
            }
            if (i <= j)
            {
                const float tmp = data[i];
                data[i] = data[j];
                data[j] = tmp;
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortFloats(data, lo, j);
            lo = i;
        }
        else
        {
            QuickSortFloats(data, i, hi);
            hi = j;
        }
    }
}

void il2cpp_array_sort_double(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(double));
    if (a->length > 1)
    {
        QuickSortDoubles(reinterpret_cast<double*>(a->data), 0, a->length - 1);
    }
}

void il2cpp_array_sort_float(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    if (a->length > 1)
    {
        QuickSortFloats(reinterpret_cast<float*>(a->data), 0, a->length - 1);
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
        try
        {
            QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), 0, a->length - 1,
                          compare);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
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
        try
        {
            QuickSortRefs(reinterpret_cast<Il2CppObject**>(a->data), index,
                          index + length - 1, compare);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
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
// il2cpp_array_index_of_double -- Array.IndexOf<double> (see header).
// Linear first-match scan with Double.Equals semantics (a NaN needle finds
// the first NaN slot, matching EqualityComparer<double>.Default).
//------------------------------------------------------------------------------
int32_t il2cpp_array_index_of_double(Il2CppArray* arr, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(double));
    const double* data = reinterpret_cast<const double*>(a->data);
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (DoubleEqualsNet(data[i], value))
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

//------------------------------------------------------------------------------
// il2cpp_array_binary_search_double -- Array.BinarySearch<double> (see
// header). Total-order lower bound on an ASCENDING double array (NaN sorts
// first, so a NaN needle's insertion point is 0); a match uses
// Double.Equals (NaN finds NaN); a miss returns the complement.
//------------------------------------------------------------------------------
int32_t il2cpp_array_binary_search_double(Il2CppArray* arr, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(double));
    const double* data = reinterpret_cast<const double*>(a->data);
    int32_t lo = 0;
    int32_t hi = a->length;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (DoubleCompareTotal(data[mid], value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    if (lo < a->length && DoubleEqualsNet(data[lo], value))
    {
        return lo;
    }
    return ~lo; // missing: complement of the insertion point
}

//------------------------------------------------------------------------------
// il2cpp_array_last_index_of_double/long/string -- Array.LastIndexOf<T>
// (see header). Reverse first-match scans; -1 when absent (also for an
// empty array). Double uses Double.Equals (a NaN needle finds the last NaN
// slot); string uses the ordinal comparison (a null needle matches the
// last null slot).
//------------------------------------------------------------------------------
int32_t il2cpp_array_last_index_of_double(Il2CppArray* arr, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(double));
    const double* data = reinterpret_cast<const double*>(a->data);
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        if (DoubleEqualsNet(data[i], value))
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_long(Il2CppArray* arr, int64_t value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int64_t));
    const int64_t* data = reinterpret_cast<const int64_t*>(a->data);
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        if (data[i] == value)
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_string(Il2CppArray* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        if (CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[i]),
                           needle) == 0)
        {
            return i;
        }
    }
    return -1;
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
        // nb9 H-B1: a capacity overflow would corrupt m_count through the
        // adjacent member; a programming error must abort loudly instead.
        if (m_count >= static_cast<int32_t>(sizeof(m_slots) / sizeof(m_slots[0])))
        {
            std::abort();
        }
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

} // namespace

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

//------------------------------------------------------------------------------
// FindVirtualSlotByNameAndSignature -- baudit2: the overload-safe sibling of
// FindVirtualSlotByName. Scans the receiver's typeinfo method table (own
// table first, then the baseType chain -- an inherited override still
// dispatches) for a method with the exact NAME whose encoded signature
// CONTAINS signatureFragment, and returns its vtable slot. The fragment is
// what makes the Object.Equals dispatch safe: a user type declaring
// Equals(T) before Equals(object) has the IEquatable<T> slot first, and
// calling it through the (this, object) ABI would break (the boxingext2
// signature-matched dispatch precedent). -1 when nothing matches.
//------------------------------------------------------------------------------
int32_t FindVirtualSlotByNameAndSignature(Il2CppObject* obj, const char* name,
                                          const char* signatureFragment)
{
    if (obj == nullptr || obj->klass == nullptr || obj->klass->name == nullptr ||
        name == nullptr || signatureFragment == nullptr)
    {
        return -1;
    }
    for (const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(obj->klass->name);
         info != nullptr; info = info->baseType)
    {
        for (int32_t i = 0; i < info->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = info->methods[i];
            if (m.name != nullptr && m.signature != nullptr &&
                std::strcmp(m.name, name) == 0 &&
                std::strstr(m.signature, signatureFragment) != nullptr)
            {
                return m.virtualSlot;
            }
        }
    }
    return -1;
}

namespace {

// The IComparable.CompareTo ABI: int32_t (*)(this, other).
using IComparableCompareFn = int32_t (*)(Il2CppObject*, Il2CppObject*);

// The IComparer.Compare ABI: int32_t (*)(this, a, b).
using IComparerCompareFn = int32_t (*)(Il2CppObject*, Il2CppObject*, Il2CppObject*);

// nb9 H-B3: the per-element-family ABI of a USER IComparer<T>.Compare(T,T).
using IComparerI4Fn = int32_t (*)(Il2CppObject*, int32_t, int32_t);
using IComparerI8Fn = int32_t (*)(Il2CppObject*, int64_t, int64_t);
using IComparerDFn = int32_t (*)(Il2CppObject*, double, double);

//------------------------------------------------------------------------------
// ComparerCompareCore -- the DEFAULT comparer's Compare(object, object) (net48
// Comparer semantics): null first (null == null, null < non-null), string
// pairs CULTURE (Comparer<string>.Default -- String.CompareTo; shared
// CompareCulture), builtin boxes by value with the SECOND operand's type
// validated (net48 Int32.CompareTo(object) throws ArgumentException("Object
// must be of type Int32.") for a foreign type), everything else falls back to
// the first argument's IComparable.CompareTo.
//------------------------------------------------------------------------------
namespace {

// The net48 ArgumentException message for a foreign second operand
// ("Object must be of type Int32." etc.).
void ThrowComparandTypeMismatch(const char* typeName)
{
    // "Object must be of type <name>." -- built without <string>.
    char message[96];
    int i = 0;
    const char* prefix = "Object must be of type ";
    while (*prefix != '\0' && i < 60)
    {
        message[i++] = *prefix++;
    }
    while (*typeName != '\0' && i < 90)
    {
        message[i++] = *typeName++;
    }
    message[i++] = '.';
    message[i] = '\0';
    il2cpp_throw_argument_exception(message);
}

bool SameKlassName(const Il2CppObject* a, const Il2CppObject* b)
{
    return b->klass != nullptr && b->klass->name != nullptr
        && a->klass->name != nullptr
        && std::strcmp(a->klass->name, b->klass->name) == 0;
}

bool HasVirtualSlot(Il2CppObject* obj, const char* name)
{
    const int32_t slot = FindVirtualSlotByName(obj, name);
    return slot >= 0 && obj->klass->vtable != nullptr
        && slot < obj->klass->vtableSize && obj->klass->vtable[slot] != nullptr;
}

int32_t CallVirtualInt32(Il2CppObject* obj, const char* name, Il2CppObject* other)
{
    const int32_t slot = FindVirtualSlotByName(obj, name);
    using CompareToFn = int32_t (*)(Il2CppObject*, Il2CppObject*);
    return reinterpret_cast<CompareToFn>(obj->klass->vtable[slot])(obj, other);
}

//------------------------------------------------------------------------------
// TryCompareBoxedBuiltin -- the RAW VALUE COMPARE TABLE for boxed BUILTIN
// receivers (G2/D3). The per-type arms mirror the historical
// ComparerCompareCore arms 1:1 (Int32/UInt32/Int64/UInt64/Single/Double/
// Char/Boolean plus the String culture arm) so the IComparable interface
// dispatch and the default comparer share ONE table.
//
// Returns true when 'a' is one of these builtin box classes (the value arms
// ran; *result holds the net48 CompareTo sign). false means 'a' is NOT a
// table type -- the caller falls to the reflected vtable dispatch.
//
// null 'b' yields *result = 1 (net48: every value type's CompareTo(object
// null) returns 1 -- "a value type is greater than null"; String too).
// A foreign-type 'b' throws the net48 ArgumentException ("Object must be of
// type <name>.") -- the SAME contract as the default comparer arms.
//------------------------------------------------------------------------------
bool TryCompareBoxedBuiltin(Il2CppObject* a, Il2CppObject* b, int32_t* result)
{
    if (a->klass == nullptr || a->klass->name == nullptr)
    {
        return false;
    }
    const char* const an = a->klass->name;
    if (std::strcmp(an, kStringTypeName) == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b))
        {
            ThrowComparandTypeMismatch("String");
        }
        *result = runtime::CompareCulture(reinterpret_cast<const Il2CppString*>(a),
                                          reinterpret_cast<const Il2CppString*>(b));
        return true;
    }
    if (std::strcmp(an, "System.Int32") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("Int32");
        const int32_t x = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const int32_t y = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.UInt32") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("UInt32");
        const uint32_t x = static_cast<uint32_t>(*reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*)));
        const uint32_t y = static_cast<uint32_t>(*reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*)));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.Int64") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("Int64");
        const int64_t x = *reinterpret_cast<int64_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const int64_t y = *reinterpret_cast<int64_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.UInt64") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("UInt64");
        const uint64_t x = *reinterpret_cast<uint64_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const uint64_t y = *reinterpret_cast<uint64_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.Single") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("Single");
        // M4 ABI (boxingext2.cpp icall_box_float/AllocBoxed64): the
        // System.Single box stores the float WIDENED TO DOUBLE in the aligned
        // 8-byte payload slot (the type system carries float as double; the
        // widening is exact). Reading a 4-byte float here truncated the
        // payload -- -1.5f/1.5f both read back as +0.0f and every sign probe
        // collapsed to 0 (comparerdeep regression). Read the double, exactly
        // like the System.Double arm below.
        const double x = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const double y = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.Double") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("Double");
        const double x = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const double y = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.Char") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("Char");
        const int32_t x = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*));
        const int32_t y = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*));
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    if (std::strcmp(an, "System.Boolean") == 0)
    {
        if (b == nullptr)
        {
            *result = 1;
            return true;
        }
        if (!SameKlassName(a, b)) ThrowComparandTypeMismatch("Boolean");
        const bool x = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(a) + sizeof(void*)) != 0;
        const bool y = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(b) + sizeof(void*)) != 0;
        *result = x < y ? -1 : (x > y ? 1 : 0);
        return true;
    }
    return false;
}

} // namespace

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
    // G2/D3: the per-type boxed-builtin value table moved into
    // TryCompareBoxedBuiltin (shared with il2cpp_icomparable_compare_to).
    // String pairs (culture), Int32/UInt32/Int64/UInt64/Single/Double/Char/
    // Boolean boxes compare by value with the net48 type-mismatch contract.
    int32_t boxed = 0;
    if (TryCompareBoxedBuiltin(a, b, &boxed))
    {
        return boxed;
    }
    // net48 Comparer.Compare fallback (caudit3):
    //   a as IComparable -> a.CompareTo(b);
    //   else b as IComparable -> -b.CompareTo(a);
    //   else throw ArgumentException("At least one object must implement
    //   IComparable.").
    // The reflected virtual slot comes from the SAME typeinfo resolver the
    // IComparable dispatcher uses (FindVirtualSlotByName -- shared via
    // arraysort.h with the object-equals patch).
    if (HasVirtualSlot(a, "CompareTo"))
    {
        return CallVirtualInt32(a, "CompareTo", b);
    }
    if (HasVirtualSlot(b, "CompareTo"))
    {
        return -CallVirtualInt32(b, "CompareTo", a);
    }
    il2cpp_throw_argument_exception(
        "At least one object must implement IComparable.");
    return 0; // unreachable (the factory throws)
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
        if (isDefault)
        {
            return ComparerCompareCore(boxes.a(), boxes.b());
        }
        // nb9 H-B3: dispatch through the element family -- a user IComparer<int>
        // exposes Compare(int,int), not Compare(object,object).
        return il2cpp_icomparer_compare_family(
            *comparerSlot, kComparerFamilyI4, &x, &y);
    }
};

// nb9 L-11/D7: the wide value-family comparer sorts (int64/double/float).
template <typename T>
T ReadValElem(Il2CppArray** pa, int32_t idx)
{
    return *reinterpret_cast<T*>(il2cpp_array_get_elem_ptr(*pa, idx));
}

template <typename T>
void WriteValElem(Il2CppArray** pa, int32_t idx, T v)
{
    *reinterpret_cast<T*>(il2cpp_array_get_elem_ptr(*pa, idx)) = v;
}

template <typename T, typename Compare>
void QuickSortValArray(Il2CppArray** pa, int32_t lo, int32_t hi, Compare compare)
{
    while (lo < hi)
    {
        const T pivot = ReadValElem<T>(pa, lo + (hi - lo) / 2);
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (compare(ReadValElem<T>(pa, i), pivot) < 0) ++i;
            while (compare(ReadValElem<T>(pa, j), pivot) > 0) --j;
            if (i <= j)
            {
                const T tmp = ReadValElem<T>(pa, i);
                WriteValElem<T>(pa, i, ReadValElem<T>(pa, j));
                WriteValElem<T>(pa, j, tmp);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortValArray<T>(pa, lo, j, compare);
            lo = i;
        }
        else
        {
            QuickSortValArray<T>(pa, i, hi, compare);
            hi = j;
        }
    }
}

struct I8ComparerCompare
{
    Il2CppObject** slot;
    int32_t operator()(int64_t x, int64_t y) const
    {
        if (*slot == nullptr) return x < y ? -1 : (x > y ? 1 : 0);
        return il2cpp_icomparer_compare_family(*slot, kComparerFamilyI8, &x, &y);
    }
};

struct DComparerCompare
{
    Il2CppObject** slot;
    int32_t operator()(double x, double y) const
    {
        if (*slot == nullptr) return DoubleCompareTotal(x, y);
        return il2cpp_icomparer_compare_family(*slot, kComparerFamilyD, &x, &y);
    }
};

struct FComparerCompare
{
    Il2CppObject** slot;
    int32_t operator()(float x, float y) const
    {
        double dx = static_cast<double>(x);
        double dy = static_cast<double>(y);
        if (*slot == nullptr) return DoubleCompareTotal(dx, dy);
        return il2cpp_icomparer_compare_family(*slot, kComparerFamilyD, &dx, &dy);
    }
};

// The comparer-driven reference comparison (elements are already objects).
// a5reds (strsort_ordinal): the StringComparer singletons get a dedicated
// ordinal element comparison; everything else keeps the existing dispatch.
enum class RefComparerMode
{
    Dispatch,         // a user IComparer object: il2cpp_icomparer_compare
    Default,          // null / Comparer.Default singleton: ComparerCompareCore
    Ordinal,          // StringComparer.Ordinal singleton
    OrdinalIgnoreCase // StringComparer.OrdinalIgnoreCase singleton
};

// a5reds: the ordinal element comparison. net48 StringComparer null ordering:
// null == null, null sorts before any non-null; a non-string element is the
// comparer type mismatch (ArgumentException, the ComparerCompareCore
// contract) because StringComparer.Compare casts both sides to string.
int32_t CompareStringOrdinalElement(Il2CppObject* x, Il2CppObject* y, bool ignoreCase)
{
    if (x == nullptr)
    {
        return y == nullptr ? 0 : -1;
    }
    if (y == nullptr)
    {
        return 1;
    }
    const char* const xn = x->klass != nullptr ? x->klass->name : nullptr;
    const char* const yn = y->klass != nullptr ? y->klass->name : nullptr;
    if (xn == nullptr || yn == nullptr ||
        std::strcmp(xn, kStringTypeName) != 0 || std::strcmp(yn, kStringTypeName) != 0)
    {
        ThrowComparandTypeMismatch("String");
    }
    const Il2CppString* sx = reinterpret_cast<const Il2CppString*>(x);
    const Il2CppString* sy = reinterpret_cast<const Il2CppString*>(y);
    return ignoreCase ? il2cpp_string_compare_ordinal_ignore_case(sx, sy)
                      : il2cpp_string_compare_ordinal(sx, sy);
}

struct RefComparerCompare
{
    Il2CppObject**  comparerSlot; // the ROOTED comparer local
    RefComparerMode mode;

    RefComparerCompare(Il2CppObject** slot, bool def)
        : comparerSlot(slot),
          mode(def ? RefComparerMode::Default : RefComparerMode::Dispatch)
    {
    }

    RefComparerCompare(Il2CppObject** slot, RefComparerMode m)
        : comparerSlot(slot), mode(m)
    {
    }

    int32_t operator()(Il2CppObject* x, Il2CppObject* y) const
    {
        switch (mode)
        {
            case RefComparerMode::Ordinal:
                return CompareStringOrdinalElement(x, y, false);
            case RefComparerMode::OrdinalIgnoreCase:
                return CompareStringOrdinalElement(x, y, true);
            case RefComparerMode::Default:
                return ComparerCompareCore(x, y);
            default:
                // nb9 H-B3: the reference family -- the element objects ARE the
                // Compare(T,T) operands (string / object / user class).
                return il2cpp_icomparer_compare_family(
                    *comparerSlot, kComparerFamilyRef, x, y);
        }
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
    // G2/D3: a boxed BUILTIN receiver (Int32/Int64/UInt64/Single/Double/
    // Char/Boolean/String -- the box klass has no reflected vtable;
    // boxingext2's kBoxUnwiredSlotMap leaves it null) resolves through the
    // RAW VALUE TABLE first, exactly like comparerdeep's default comparer.
    // A non-table receiver falls through to the reflected vtable dispatch.
    int32_t boxed = 0;
    if (TryCompareBoxedBuiltin(receiver, other, &boxed))
    {
        return boxed;
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
// il2cpp_icomparer_compare_family -- the element-family dispatch shared by the
// array sorts (this file) and the sorted containers (sortedlist.cpp
// CompareWithComparer). See arraysort.h for the contract.
//
// Slot selection is SIGNATURE-BASED, exactly like sortedlist's H3 fix: a user
// IComparer<int> stores "i(i,i)", an IComparer<string> "i(r:System.String,...)".
// A name-only "Compare" lookup would return the (T,T) slot and the
// (this, object, object) ABI would pass pointers where the value ABI expects
// raw int32/int64/double -- the H-B3 0xC0000005.
//------------------------------------------------------------------------------
int32_t il2cpp_icomparer_compare_family(Il2CppObject* comparer, int32_t family,
                                        const void* a, const void* b)
{
    if (comparer == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // Comparer<T>.Create wrapper -- run the wrapped Comparison<T> delegate with
    // the family ABI (the wrapper has no reflected Compare slot).
    if (runtime::il2cpp_is_created_comparer(comparer))
    {
        switch (family)
        {
            case kComparerFamilyI4:
                return runtime::il2cpp_created_comparer_compare_int(
                    comparer, *static_cast<const int32_t*>(a),
                    *static_cast<const int32_t*>(b));
            case kComparerFamilyI8:
                return runtime::il2cpp_created_comparer_compare_long(
                    comparer, *static_cast<const int64_t*>(a),
                    *static_cast<const int64_t*>(b));
            case kComparerFamilyD:
                return runtime::il2cpp_created_comparer_compare_double(
                    comparer, *static_cast<const double*>(a),
                    *static_cast<const double*>(b));
            default:
                return runtime::il2cpp_created_comparer_compare_ref(
                    comparer, const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(a)),
                    const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(b)));
        }
    }

    // The default Comparer<T>/Comparer.Default (and EqualityComparer<T>)
    // singletons carry no reflected Compare slot: compare by the element
    // family's builtin order (this is what made BinarySearch(..,
    // Comparer<int>.Default) throw NotSupported before).
    if (comparer->klass != nullptr && comparer->klass->name != nullptr &&
        (std::strstr(comparer->klass->name, "System.Collections.Generic.Comparer`1") != nullptr ||
         std::strstr(comparer->klass->name, "System.Collections.Generic.EqualityComparer`1") != nullptr ||
         std::strcmp(comparer->klass->name, "System.Collections.Comparer") == 0))
    {
        switch (family)
        {
            case kComparerFamilyI4:
            {
                const int32_t x = *static_cast<const int32_t*>(a);
                const int32_t y = *static_cast<const int32_t*>(b);
                return x < y ? -1 : (x > y ? 1 : 0);
            }
            case kComparerFamilyI8:
            {
                const int64_t x = *static_cast<const int64_t*>(a);
                const int64_t y = *static_cast<const int64_t*>(b);
                return x < y ? -1 : (x > y ? 1 : 0);
            }
            case kComparerFamilyD:
                return DoubleCompareTotal(*static_cast<const double*>(a),
                                          *static_cast<const double*>(b));
            default:
                return ComparerCompareCore(
                    const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(a)),
                    const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(b)));
        }
    }

    const char* const fragment =
        family == kComparerFamilyI4 ? "(i,i)"
        : family == kComparerFamilyI8 ? "(l,l)"
        : family == kComparerFamilyD ? "(d,d)"
        : "(r:";
    const int32_t slot =
        FindVirtualSlotByNameAndSignature(comparer, "Compare", fragment);
    if (slot >= 0 && comparer->klass != nullptr && comparer->klass->vtable != nullptr &&
        slot < comparer->klass->vtableSize && comparer->klass->vtable[slot] != nullptr)
    {
        void* const fn = comparer->klass->vtable[slot];
        switch (family)
        {
            case kComparerFamilyI4:
                return reinterpret_cast<IComparerI4Fn>(fn)(
                    comparer, *static_cast<const int32_t*>(a),
                    *static_cast<const int32_t*>(b));
            case kComparerFamilyI8:
                return reinterpret_cast<IComparerI8Fn>(fn)(
                    comparer, *static_cast<const int64_t*>(a),
                    *static_cast<const int64_t*>(b));
            case kComparerFamilyD:
                return reinterpret_cast<IComparerDFn>(fn)(
                    comparer, *static_cast<const double*>(a),
                    *static_cast<const double*>(b));
            default:
                return reinterpret_cast<IComparerCompareFn>(fn)(
                    comparer, const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(a)),
                    const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(b)));
        }
    }

    // Compatibility fallback: the NON-GENERIC IComparer.Compare(object,object)
    // slot (Array.Sort(Array, IComparer); a SortedSet built with a
    // System.Collections.IComparer). The signature must be object/object --
    // re-resolving by name alone could still land on a (T,T) slot and repeat
    // the H-B3 crash.
    const int32_t objSlot =
        FindVirtualSlotByNameAndSignature(comparer, "Compare", "(r:System.Object");
    if (objSlot >= 0 && comparer->klass != nullptr && comparer->klass->vtable != nullptr &&
        objSlot < comparer->klass->vtableSize && comparer->klass->vtable[objSlot] != nullptr)
    {
        void* const fn = comparer->klass->vtable[objSlot];
        if (family == kComparerFamilyRef)
        {
            return reinterpret_cast<IComparerCompareFn>(fn)(
                comparer, const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(a)),
                const_cast<Il2CppObject*>(static_cast<const Il2CppObject*>(b)));
        }
        // Box the two value operands for the object ABI; the boxes are rooted
        // across the callback (it may allocate / compact).
        RootedI4Boxes boxes;
        switch (family)
        {
            case kComparerFamilyI4:
                boxes.set_a(il2cpp_box_int32(&System_Int32_class,
                                             *static_cast<const int32_t*>(a)));
                boxes.set_b(il2cpp_box_int32(&System_Int32_class,
                                             *static_cast<const int32_t*>(b)));
                break;
            case kComparerFamilyI8:
                boxes.set_a(runtime::icall_box_long(*static_cast<const int64_t*>(a)));
                boxes.set_b(runtime::icall_box_long(*static_cast<const int64_t*>(b)));
                break;
            default:
                boxes.set_a(runtime::icall_box_double(*static_cast<const double*>(a)));
                boxes.set_b(runtime::icall_box_double(*static_cast<const double*>(b)));
                break;
        }
        return reinterpret_cast<IComparerCompareFn>(fn)(
            comparer, boxes.a(), boxes.b());
    }

    il2cpp_throw_not_supported_exception(
        "IComparer dispatch: the comparer type has no reflected virtual Compare "
        "slot for the element family (and no object/object Compare).");
    return 0; // unreachable (the factory throws)
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
// a5reds (strsort_ordinal): System.StringComparer::get_Ordinal /
// get_OrdinalIgnoreCase. See arraysort.h; the singletons are recognized by
// pointer identity in il2cpp_array_sort_icomparer (and forwarded there from
// il2cpp_array_sort_ref_comparison).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_string_comparer_ordinal(void)
{
    il2cpp_class_register(&System_StringComparer_Ordinal_class);
    return &System_StringComparer_Ordinal_singleton;
}

Il2CppObject* il2cpp_string_comparer_ordinal_ignore_case(void)
{
    il2cpp_class_register(&System_StringComparer_OrdinalIgnoreCase_class);
    return &System_StringComparer_OrdinalIgnoreCase_singleton;
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
namespace {

// The shared comparer-sort body (full and range overloads). The element kind
// is validated BEFORE the try so a NotSupported is never remapped to the
// "comparer failed" InvalidOperationException.
void SortIComparerCore(Il2CppArray** pa, int32_t lo, int32_t hi,
                       Il2CppObject** comparerSlot, RefComparerMode mode)
{
    Il2CppArray* a = *pa;
    if (lo >= hi)
    {
        return;
    }
    if (a->isRefElem)
    {
        try
        {
            RefComparerCompare cmp(comparerSlot, mode);
            QuickSortRefArray(pa, lo, hi, cmp);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
    else if (a->elemSize == sizeof(int32_t))
    {
        try
        {
            // The StringComparer singletons cannot compare int32 elements in
            // net48 either; the dispatch path reports NotSupported (the
            // singleton klass has no Compare slot). Unchanged behavior.
            I4ComparerCompare cmp(comparerSlot, mode == RefComparerMode::Default);
            QuickSortI4Array(pa, lo, hi, cmp);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
    else
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional int32 or reference-element arrays are "
            "supported for the comparer sort.");
    }
}

RefComparerMode ClassifyComparer(Il2CppObject* comparer)
{
    return comparer == nullptr || comparer == &System_Collections_Comparer_singleton
        ? RefComparerMode::Default
        : comparer == &System_StringComparer_Ordinal_singleton
              ? RefComparerMode::Ordinal
              : comparer == &System_StringComparer_OrdinalIgnoreCase_singleton
                    ? RefComparerMode::OrdinalIgnoreCase
                    : RefComparerMode::Dispatch;
}

} // namespace

void il2cpp_array_sort_icomparer(Il2CppArray* arr, Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    // a5reds: null / Comparer.Default -> the default core; the StringComparer
    // singletons -> ordinal element comparison; any other object -> IComparer
    // dispatch.
    const RefComparerMode mode = ClassifyComparer(comparer);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    SortIComparerCore(&a, 0, a->length - 1, &comparer, mode);
}

// nb9 zz-b4-cmp4: Array.Sort<T>(T[], int, int, IComparer<T>) -- only
// [index, index+length) is ordered.
void il2cpp_array_sort_icomparer_range(Il2CppArray* arr, int32_t index, int32_t length,
                                       Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRangeSort(a, index, length);
    const RefComparerMode mode = ClassifyComparer(comparer);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    SortIComparerCore(&a, index, index + length - 1, &comparer, mode);
}

// nb9 L-11/D7: the wide value-family IComparer sorts.
#define NB9_DEFINE_ICOMPARER_SORT(SUF, T, CMPTYPE)                              \
void il2cpp_array_sort_icomparer_##SUF(Il2CppArray* arr, Il2CppObject* comparer) \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    RootScopeSort roots;                                                        \
    roots.Add(reinterpret_cast<void**>(&a));                                    \
    roots.Add(reinterpret_cast<void**>(&comparer));                             \
    if (a->length > 1)                                                          \
    {                                                                           \
        try                                                                     \
        {                                                                       \
            CMPTYPE cmp{&comparer};                                             \
            QuickSortValArray<T>(&a, 0, a->length - 1, cmp);                    \
        }                                                                       \
        catch (const DotNetException&)                                          \
        {                                                                       \
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);   \
        }                                                                       \
    }                                                                           \
}                                                                               \
void il2cpp_array_sort_icomparer_##SUF##_range(Il2CppArray* arr, int32_t index, \
                                               int32_t length,                  \
                                               Il2CppObject* comparer)          \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    RequireRangeSort(a, index, length);                                         \
    RootScopeSort roots;                                                        \
    roots.Add(reinterpret_cast<void**>(&a));                                    \
    roots.Add(reinterpret_cast<void**>(&comparer));                             \
    if (length > 1)                                                             \
    {                                                                           \
        try                                                                     \
        {                                                                       \
            CMPTYPE cmp{&comparer};                                             \
            QuickSortValArray<T>(&a, index, index + length - 1, cmp);           \
        }                                                                       \
        catch (const DotNetException&)                                          \
        {                                                                       \
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);   \
        }                                                                       \
    }                                                                           \
}

NB9_DEFINE_ICOMPARER_SORT(int64, int64_t, I8ComparerCompare)
NB9_DEFINE_ICOMPARER_SORT(double, double, DComparerCompare)
NB9_DEFINE_ICOMPARER_SORT(float, float, FComparerCompare)

#undef NB9_DEFINE_ICOMPARER_SORT

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
        try
        {
            I4ComparisonCompare cmp(&comparison);
            QuickSortI4Array(&a, 0, a->length - 1, cmp);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
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
    // a5reds (strsort_ordinal): the generic Sort<T>(T[], IComparer<T>) and
    // Sort<T>(T[], Comparison<T>) call sites can be indistinguishable when
    // the comparer's tracked singleton type is lost (e.g. it rides an
    // IComparer<T>-typed local): a StringComparer singleton arriving here is
    // FORWARDED to the comparer sort instead of being misinterpreted as a
    // delegate (garbage methodPtr -> crash).
    if (comparison == &System_StringComparer_Ordinal_singleton ||
        comparison == &System_StringComparer_OrdinalIgnoreCase_singleton)
    {
        il2cpp_array_sort_icomparer(arr, comparison);
        return;
    }
    if (comparison == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparison");
    }
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparison));
    if (a->length > 1)
    {
        try
        {
            RefComparisonCompare cmp(&comparison);
            QuickSortRefArray(&a, 0, a->length - 1, cmp);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
}

//------------------------------------------------------------------------------
// Array.BinarySearch<T>(T[], T, IComparer<T>) (nb9 H-B4). The comparer's
// element family is fixed by the ARRAY, never by the dynamic element klass:
// a value array uses the raw-value ABI, a reference array the object ABI.
//------------------------------------------------------------------------------
namespace {

int32_t CompareI4ForSearch(Il2CppObject* comparer, int32_t x, int32_t y)
{
    return comparer == nullptr
        ? (x < y ? -1 : (x > y ? 1 : 0))
        : il2cpp_icomparer_compare_family(comparer, kComparerFamilyI4, &x, &y);
}

int32_t CompareRefForSearch(Il2CppObject* comparer, Il2CppObject* x, Il2CppObject* y)
{
    if (comparer == nullptr)
    {
        return ComparerCompareCore(x, y);
    }
    if (comparer == &System_StringComparer_Ordinal_singleton)
    {
        return CompareStringOrdinalElement(x, y, false);
    }
    if (comparer == &System_StringComparer_OrdinalIgnoreCase_singleton)
    {
        return CompareStringOrdinalElement(x, y, true);
    }
    return il2cpp_icomparer_compare_family(comparer, kComparerFamilyRef, x, y);
}

} // namespace

int32_t il2cpp_array_binary_search_icomparer_int32(Il2CppArray* arr, int32_t value,
                                                   Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareI4ForSearch(comparer, ReadI4Elem(&a, mid), value);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_icomparer_ref(Il2CppArray* arr, Il2CppObject* value,
                                                 Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&value));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareRefForSearch(comparer, ReadRefElem(&a, mid), value);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_icomparer_int32_range(Il2CppArray* arr, int32_t index,
                                                         int32_t length, int32_t value,
                                                         Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(int32_t));
    RequireRangeSort(a, index, length);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = index;
    int32_t hi = index + length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareI4ForSearch(comparer, ReadI4Elem(&a, mid), value);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_icomparer_ref_range(Il2CppArray* arr, int32_t index,
                                                       int32_t length, Il2CppObject* value,
                                                       Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&value));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = index;
    int32_t hi = index + length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareRefForSearch(comparer, ReadRefElem(&a, mid), value);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

//==============================================================================
// nb9 P0 (arity mispair): the REMAINING arities of the generic
// IndexOf/LastIndexOf/BinarySearch/Sort surface, per element family, in the
// REAL net48 parameter order. Before this the mapping selected a 2-argument
// entry for EVERY arity, so e.g. IndexOf(a,3,1) emitted
// icall_array_index_of_int32(3, 1): the array slot received the literal 3 and
// the first dereference AV'd. Every entry here carries the explicit
// (array, value, startIndex[, count]) / (array, index, length, value
// [, comparer]) order so the emitted argument list matches the runtime order.
//==============================================================================

namespace {

// ---- scan validation (net48 Array.IndexOf / Array.LastIndexOf) --------------
void ValidateIndexOfStartIndex(const Il2CppArray* a, int32_t startIndex)
{
    if (startIndex < 0 || startIndex > a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "startIndex",
            "Index was out of range. Must be non-negative and less than the "
            "size of the collection.");
    }
}

void ValidateIndexOfRange(const Il2CppArray* a, int32_t startIndex, int32_t count)
{
    ValidateIndexOfStartIndex(a, startIndex);
    if (count < 0 ||
        static_cast<int64_t>(startIndex) + static_cast<int64_t>(count) >
            static_cast<int64_t>(a->length))
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count",
            "Count must be positive and count must refer to a location within "
            "the string/array/collection.");
    }
}

void ValidateLastIndexOfStartIndex(const Il2CppArray* a, int32_t startIndex)
{
    // nb9 D12: an EMPTY array accepts startIndex 0 and yields -1 (net48),
    // unlike the non-empty contract (startIndex >= Length is out of range).
    if (a->length == 0 && startIndex == 0)
    {
        return;
    }
    if (startIndex < 0 || startIndex >= a->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "startIndex",
            "Index was out of range. Must be non-negative and less than the "
            "size of the collection.");
    }
}

// nb9 D13: the NON-GENERIC Array.LastIndexOf(Array, object, int, int) names
// the range-overflow parameter "endIndex" (the generic overload names it
// "count"). Both keep "count" for a negative count.
void ValidateLastIndexOfRangeAny(const Il2CppArray* a, int32_t startIndex, int32_t count)
{
    ValidateLastIndexOfStartIndex(a, startIndex);
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count",
            "Count must be positive and count must refer to a location within "
            "the string/array/collection.");
    }
    if (static_cast<int64_t>(count) > static_cast<int64_t>(startIndex) + 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "endIndex",
            "Count must be positive and count must refer to a location within "
            "the string/array/collection.");
    }
}

void ValidateLastIndexOfRange(const Il2CppArray* a, int32_t startIndex, int32_t count)
{
    ValidateLastIndexOfStartIndex(a, startIndex);
    // nb9 D13 matrix (net48 host measured): negative count and a range that
    // runs past the beginning both report ParamName "count".
    if (count < 0 ||
        static_cast<int64_t>(count) > static_cast<int64_t>(startIndex) + 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "count",
            "Count must be positive and count must refer to a location within "
            "the string/array/collection.");
    }
}

// Raw value equality (int32/int64) and the wide float/double identities.
template <typename T>
bool ValueEqualRaw(T x, T y)
{
    return x == y;
}

bool FloatEqualsNet(float x, float y)
{
    return DoubleEqualsNet(static_cast<double>(x), static_cast<double>(y));
}

} // namespace

// ---- Array.IndexOf<T>(T[], T, int[, int]) per value family ------------------
#define NB9_DEFINE_VALUE_SCAN(SUF, T, EQFN)                                     \
int32_t il2cpp_array_index_of_##SUF##_start(Il2CppArray* arr, T value,          \
                                            int32_t startIndex)                 \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    ValidateIndexOfStartIndex(a, startIndex);                                   \
    const T* data = reinterpret_cast<const T*>(a->data);                        \
    for (int32_t i = startIndex; i < a->length; ++i)                            \
    {                                                                           \
        if (EQFN(data[i], value)) return i;                                     \
    }                                                                           \
    return -1;                                                                  \
}                                                                               \
int32_t il2cpp_array_index_of_##SUF##_range(Il2CppArray* arr, T value,          \
                                            int32_t startIndex, int32_t count)  \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    ValidateIndexOfRange(a, startIndex, count);                                 \
    const T* data = reinterpret_cast<const T*>(a->data);                        \
    const int32_t end = startIndex + count;                                     \
    for (int32_t i = startIndex; i < end; ++i)                                  \
    {                                                                           \
        if (EQFN(data[i], value)) return i;                                     \
    }                                                                           \
    return -1;                                                                  \
}                                                                               \
int32_t il2cpp_array_last_index_of_##SUF##_start(Il2CppArray* arr, T value,     \
                                                 int32_t startIndex)            \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    ValidateLastIndexOfStartIndex(a, startIndex);                               \
    const T* data = reinterpret_cast<const T*>(a->data);                        \
    for (int32_t i = startIndex; i >= 0 && i < a->length; --i)                                   \
    {                                                                           \
        if (EQFN(data[i], value)) return i;                                     \
    }                                                                           \
    return -1;                                                                  \
}                                                                               \
int32_t il2cpp_array_last_index_of_##SUF##_range(Il2CppArray* arr, T value,     \
                                                 int32_t startIndex,            \
                                                 int32_t count)                 \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    ValidateLastIndexOfRange(a, startIndex, count);                             \
    const T* data = reinterpret_cast<const T*>(a->data);                        \
    const int32_t lo = startIndex - count + 1;                                  \
    for (int32_t i = startIndex; i >= lo; --i)                                  \
    {                                                                           \
        if (EQFN(data[i], value)) return i;                                     \
    }                                                                           \
    return -1;                                                                  \
}

NB9_DEFINE_VALUE_SCAN(int32, int32_t, ValueEqualRaw)
NB9_DEFINE_VALUE_SCAN(int64, int64_t, ValueEqualRaw)
NB9_DEFINE_VALUE_SCAN(double, double, DoubleEqualsNet)

#undef NB9_DEFINE_VALUE_SCAN

// float elements: the value carrier is a double (the H-F2 float convention --
// float32 values ride the F stack family widened), so the entries take a
// double and the element is widened before the comparison.
int32_t il2cpp_array_index_of_float_start(Il2CppArray* arr, double value,
                                         int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    ValidateIndexOfStartIndex(a, startIndex);
    const float* data = reinterpret_cast<const float*>(a->data);
    for (int32_t i = startIndex; i < a->length; ++i)
    {
        if (DoubleEqualsNet(static_cast<double>(data[i]), value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_index_of_float_range(Il2CppArray* arr, double value,
                                          int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    ValidateIndexOfRange(a, startIndex, count);
    const float* data = reinterpret_cast<const float*>(a->data);
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (DoubleEqualsNet(static_cast<double>(data[i]), value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_float(Il2CppArray* arr, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    const float* data = reinterpret_cast<const float*>(a->data);
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        if (DoubleEqualsNet(static_cast<double>(data[i]), value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_float_start(Il2CppArray* arr, double value,
                                               int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    ValidateLastIndexOfStartIndex(a, startIndex);
    const float* data = reinterpret_cast<const float*>(a->data);
    for (int32_t i = startIndex; i >= 0 && i < a->length; --i)
    {
        if (DoubleEqualsNet(static_cast<double>(data[i]), value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_float_range(Il2CppArray* arr, double value,
                                               int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    ValidateLastIndexOfRange(a, startIndex, count);
    const float* data = reinterpret_cast<const float*>(a->data);
    const int32_t lo = startIndex - count + 1;
    for (int32_t i = startIndex; i >= lo; --i)
    {
        if (DoubleEqualsNet(static_cast<double>(data[i]), value)) return i;
    }
    return -1;
}

// ---- Array.IndexOf/LastIndexOf<string> (ordinal, null matches null) ---------
int32_t il2cpp_array_index_of_string_start(Il2CppArray* arr, Il2CppObject* value,
                                           int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateIndexOfStartIndex(a, startIndex);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    for (int32_t i = startIndex; i < a->length; ++i)
    {
        if (CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[i]), needle) == 0)
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_array_index_of_string_range(Il2CppArray* arr, Il2CppObject* value,
                                           int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateIndexOfRange(a, startIndex, count);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[i]), needle) == 0)
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_string_start(Il2CppArray* arr, Il2CppObject* value,
                                                int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateLastIndexOfStartIndex(a, startIndex);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    for (int32_t i = startIndex; i >= 0 && i < a->length; --i)
    {
        if (CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[i]), needle) == 0)
        {
            return i;
        }
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_string_range(Il2CppArray* arr, Il2CppObject* value,
                                                int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateLastIndexOfRange(a, startIndex, count);
    const Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t lo = startIndex - count + 1;
    for (int32_t i = startIndex; i >= lo; --i)
    {
        if (CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[i]), needle) == 0)
        {
            return i;
        }
    }
    return -1;
}

// ---- Array.IndexOf/LastIndexOf<object> (EqualityComparer<object>.Default) --
int32_t il2cpp_array_index_of_object_start(Il2CppArray* arr, Il2CppObject* value,
                                           int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateIndexOfStartIndex(a, startIndex);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    for (int32_t i = startIndex; i < a->length; ++i)
    {
        if (runtime::icall_object_equals(slots[i], value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_index_of_object_range(Il2CppArray* arr, Il2CppObject* value,
                                           int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateIndexOfRange(a, startIndex, count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (runtime::icall_object_equals(slots[i], value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_object_start(Il2CppArray* arr, Il2CppObject* value,
                                                int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateLastIndexOfStartIndex(a, startIndex);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    for (int32_t i = startIndex; i >= 0 && i < a->length; --i)
    {
        if (runtime::icall_object_equals(slots[i], value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_object_range(Il2CppArray* arr, Il2CppObject* value,
                                                int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    ValidateLastIndexOfRange(a, startIndex, count);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t lo = startIndex - count + 1;
    for (int32_t i = startIndex; i >= lo; --i)
    {
        if (runtime::icall_object_equals(slots[i], value)) return i;
    }
    return -1;
}

// ---- Array.BinarySearch<T>(T[], int, int, T) per value family --------------
#define NB9_DEFINE_VALUE_BSEARCH(SUF, T, EQFN)                                  \
int32_t il2cpp_array_binary_search_##SUF##_range(Il2CppArray* arr, int32_t index,\
                                                 int32_t length, T value)       \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    RequireRangeSort(a, index, length);                                         \
    const T* data = reinterpret_cast<const T*>(a->data);                        \
    const int32_t hi = index + length;                                          \
    const int32_t at = LowerBoundValues(data, index, hi, value);                \
    if (at < hi && EQFN(data[at], value)) return at;                            \
    return ~at;                                                                 \
}

NB9_DEFINE_VALUE_BSEARCH(int32, int32_t, ValueEqualRaw)
NB9_DEFINE_VALUE_BSEARCH(int64, int64_t, ValueEqualRaw)

#undef NB9_DEFINE_VALUE_BSEARCH

// float/double use the net48 TOTAL order (NaN first), so the lower bound is
// not the raw `<` scan.
int32_t il2cpp_array_binary_search_float_range(Il2CppArray* arr, int32_t index,
                                               int32_t length, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    RequireRangeSort(a, index, length);
    const float* data = reinterpret_cast<const float*>(a->data);
    int32_t lo = index;
    int32_t hi = index + length;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (DoubleCompareTotal(static_cast<double>(data[mid]), value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    const int32_t end = index + length;
    if (lo < end && DoubleEqualsNet(static_cast<double>(data[lo]), value)) return lo;
    return ~lo;
}

int32_t il2cpp_array_binary_search_float(Il2CppArray* arr, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    const float* data = reinterpret_cast<const float*>(a->data);
    int32_t lo = 0;
    int32_t hi = a->length;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (DoubleCompareTotal(static_cast<double>(data[mid]), value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    if (lo < a->length && DoubleEqualsNet(static_cast<double>(data[lo]), value)) return lo;
    return ~lo;
}

int32_t il2cpp_array_binary_search_object(Il2CppArray* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    int32_t lo = 0;
    int32_t hi = a->length;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (ComparerCompareCore(slots[mid], value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    if (lo < a->length && ComparerCompareCore(slots[lo], value) == 0) return lo;
    return ~lo;
}

int32_t il2cpp_array_binary_search_double_range(Il2CppArray* arr, int32_t index,
                                                int32_t length, double value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(double));
    RequireRangeSort(a, index, length);
    const double* data = reinterpret_cast<const double*>(a->data);
    int32_t lo = index;
    int32_t hi = index + length;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (DoubleCompareTotal(data[mid], value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    const int32_t end = index + length;
    if (lo < end && DoubleEqualsNet(data[lo], value)) return lo;
    return ~lo;
}

int32_t il2cpp_array_binary_search_string_range(Il2CppArray* arr, int32_t index,
                                                int32_t length, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    Il2CppString* needle = AsStringOrNull(value);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    const int32_t at = LowerBoundRefs(slots, index, index + length,
                                      reinterpret_cast<Il2CppObject*>(needle),
                                      CompareStringRefs);
    if (at < index + length &&
        CompareOrdinal(reinterpret_cast<const Il2CppString*>(slots[at]), needle) == 0)
    {
        return at;
    }
    return ~at;
}

// Array.BinarySearch<object>(T[], int, int, T) -- the default comparer's
// Compare (IComparable dispatch) is the element order.
int32_t il2cpp_array_binary_search_object_range(Il2CppArray* arr, int32_t index,
                                                int32_t length, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRefArraySort(a);
    RequireRangeSort(a, index, length);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
    int32_t lo = index;
    int32_t hi = index + length;
    while (lo < hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        if (ComparerCompareCore(slots[mid], value) < 0)
        {
            lo = mid + 1;
        }
        else
        {
            hi = mid;
        }
    }
    if (lo < index + length && ComparerCompareCore(slots[lo], value) == 0)
    {
        return lo;
    }
    return ~lo;
}

// ---- Array.BinarySearch<T>(T[], T, IComparer<T>) for the wide families ----
// (int32/ref already have entries; i8/double/float reuse the shared
// il2cpp_icomparer_compare_family dispatch.)
#define NB9_DEFINE_ICOMPARER_SEARCH(SUF, FAMILY, T, CAST)                       \
int32_t il2cpp_array_binary_search_icomparer_##SUF(Il2CppArray* arr, T value,    \
                                                   Il2CppObject* comparer)      \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    RootScopeSort roots;                                                        \
    roots.Add(reinterpret_cast<void**>(&a));                                    \
    roots.Add(reinterpret_cast<void**>(&comparer));                             \
    int32_t lo = 0;                                                             \
    int32_t hi = a->length - 1;                                                 \
    while (lo <= hi)                                                            \
    {                                                                           \
        const int32_t mid = lo + (hi - lo) / 2;                                 \
        const T elem = reinterpret_cast<const T*>(a->data)[mid];                \
        const T xv = elem;                                                      \
        const T yv = value;                                                     \
        const int32_t c = il2cpp_icomparer_compare_family(                      \
            comparer, FAMILY, reinterpret_cast<const void*>(&xv),               \
            reinterpret_cast<const void*>(&yv));                                \
        if (c == 0) return mid;                                                 \
        if (c < 0) lo = mid + 1;                                                \
        else hi = mid - 1;                                                      \
    }                                                                           \
    return ~lo;                                                                 \
}                                                                               \
int32_t il2cpp_array_binary_search_icomparer_##SUF##_range(                     \
    Il2CppArray* arr, int32_t index, int32_t length, T value,                   \
    Il2CppObject* comparer)                                                     \
{                                                                               \
    Il2CppArray* a = RequireArraySort(arr);                                     \
    RequireValueArraySort(a, static_cast<int32_t>(sizeof(T)));                  \
    RequireRangeSort(a, index, length);                                         \
    RootScopeSort roots;                                                        \
    roots.Add(reinterpret_cast<void**>(&a));                                    \
    roots.Add(reinterpret_cast<void**>(&comparer));                             \
    int32_t lo = index;                                                         \
    int32_t hi = index + length - 1;                                            \
    while (lo <= hi)                                                            \
    {                                                                           \
        const int32_t mid = lo + (hi - lo) / 2;                                 \
        const T xv = reinterpret_cast<const T*>(a->data)[mid];                  \
        const T yv = value;                                                     \
        const int32_t c = il2cpp_icomparer_compare_family(                      \
            comparer, FAMILY, reinterpret_cast<const void*>(&xv),               \
            reinterpret_cast<const void*>(&yv));                                \
        if (c == 0) return mid;                                                 \
        if (c < 0) lo = mid + 1;                                                \
        else hi = mid - 1;                                                      \
    }                                                                           \
    return ~lo;                                                                 \
}

NB9_DEFINE_ICOMPARER_SEARCH(int64, kComparerFamilyI8, int64_t, _)
NB9_DEFINE_ICOMPARER_SEARCH(double, kComparerFamilyD, double, _)

#undef NB9_DEFINE_ICOMPARER_SEARCH

// float elements widen to the double carrier for the family-D compare; the
// value carrier is likewise the widened double (H-F2 float convention).
int32_t il2cpp_array_binary_search_icomparer_float(Il2CppArray* arr, double value,
                                                   Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const double xv = static_cast<double>(reinterpret_cast<const float*>(a->data)[mid]);
        const int32_t c = il2cpp_icomparer_compare_family(
            comparer, kComparerFamilyD, &xv, &value);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_icomparer_float_range(
    Il2CppArray* arr, int32_t index, int32_t length, double value,
    Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    RequireRangeSort(a, index, length);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = index;
    int32_t hi = index + length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const double xv = static_cast<double>(reinterpret_cast<const float*>(a->data)[mid]);
        const int32_t c = il2cpp_icomparer_compare_family(
            comparer, kComparerFamilyD, &xv, &value);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

//==============================================================================
// nb9 P0: the NON-GENERIC Array.IndexOf/LastIndexOf/BinarySearch arities
// (Array, object[, startIndex[, count]]) and the comparer forms. Elements are
// either raw int32 (boxed comparand) or reference slots (Object.Equals /
// IComparer family dispatch).
//==============================================================================

namespace {

enum AnyValueKind
{
    AnyI4,
    AnyI8,
    AnyD,
    AnyF,
    AnyRef,
};

AnyValueKind ClassifyAnyValue(Il2CppObject* value)
{
    if (value == nullptr || value->klass == nullptr || value->klass->name == nullptr)
    {
        return AnyRef;
    }
    const char* n = value->klass->name;
    if (std::strcmp(n, "System.Int32") == 0) return AnyI4;
    if (std::strcmp(n, "System.Int64") == 0) return AnyI8;
    if (std::strcmp(n, "System.Double") == 0) return AnyD;
    if (std::strcmp(n, "System.Single") == 0) return AnyF;
    return AnyRef;
}

// nb9 D9/D10: value-family-aware equality for the non-generic scans. The
// ARRAY's recorded element kind decides the read; the boxed value must belong
// to the same family, otherwise it is simply "not equal" (net48 IndexOf
// compares boxed values and never coerces). Arrays created without a kind
// (runtime-helper arrays) fall back to the conservative int32 / box-family
// heuristic.
bool AnyElementEquals(Il2CppArray* a, int32_t idx, Il2CppObject* value)
{
    if (a->isRefElem)
    {
        return runtime::icall_object_equals(
            reinterpret_cast<Il2CppObject**>(a->data)[idx], value);
    }
    const AnyValueKind k = ClassifyAnyValue(value);
    const int32_t kind = a->elemKind;
    if (kind == 1)
    {
        return k == AnyI4 &&
               *reinterpret_cast<const int32_t*>(il2cpp_array_get_elem_ptr(a, idx)) ==
                   runtime::icall_unbox_int32(value);
    }
    if (kind == 2)
    {
        return k == AnyI8 &&
               *reinterpret_cast<const int64_t*>(il2cpp_array_get_elem_ptr(a, idx)) ==
                   runtime::icall_unbox_long(value);
    }
    if (kind == 3)
    {
        return k == AnyF &&
               DoubleEqualsNet(
                   static_cast<double>(*reinterpret_cast<const float*>(
                       il2cpp_array_get_elem_ptr(a, idx))),
                   runtime::icall_unbox_float(value));
    }
    if (kind == 4)
    {
        return k == AnyD &&
               DoubleEqualsNet(
                   *reinterpret_cast<const double*>(il2cpp_array_get_elem_ptr(a, idx)),
                   runtime::icall_unbox_double(value));
    }
    if (a->elemSize == sizeof(int32_t) && k == AnyI4)
    {
        return *reinterpret_cast<const int32_t*>(il2cpp_array_get_elem_ptr(a, idx)) ==
               runtime::icall_unbox_int32(value);
    }
    if (a->elemSize == sizeof(int64_t) && k == AnyI8)
    {
        return *reinterpret_cast<const int64_t*>(il2cpp_array_get_elem_ptr(a, idx)) ==
               runtime::icall_unbox_long(value);
    }
    if (a->elemSize == sizeof(int64_t) && k == AnyD)
    {
        return DoubleEqualsNet(
            *reinterpret_cast<const double*>(il2cpp_array_get_elem_ptr(a, idx)),
            runtime::icall_unbox_double(value));
    }
    return false;
}

} // namespace

int32_t il2cpp_array_index_of_any_start(Il2CppArray* arr, Il2CppObject* value,
                                        int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    ValidateIndexOfStartIndex(a, startIndex);
    for (int32_t i = startIndex; i < a->length; ++i)
    {
        if (AnyElementEquals(a, i, value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_index_of_any_range(Il2CppArray* arr, Il2CppObject* value,
                                        int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    ValidateIndexOfRange(a, startIndex, count);
    const int32_t end = startIndex + count;
    for (int32_t i = startIndex; i < end; ++i)
    {
        if (AnyElementEquals(a, i, value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_any(Il2CppArray* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    for (int32_t i = a->length - 1; i >= 0; --i)
    {
        if (AnyElementEquals(a, i, value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_any_start(Il2CppArray* arr, Il2CppObject* value,
                                             int32_t startIndex)
{
    Il2CppArray* a = RequireArraySort(arr);
    ValidateLastIndexOfStartIndex(a, startIndex);
    for (int32_t i = startIndex; i >= 0 && i < a->length; --i)
    {
        if (AnyElementEquals(a, i, value)) return i;
    }
    return -1;
}

int32_t il2cpp_array_last_index_of_any_range(Il2CppArray* arr, Il2CppObject* value,
                                             int32_t startIndex, int32_t count)
{
    Il2CppArray* a = RequireArraySort(arr);
    ValidateLastIndexOfRangeAny(a, startIndex, count);
    const int32_t lo = startIndex - count + 1;
    for (int32_t i = startIndex; i >= lo && i < a->length; --i)
    {
        if (AnyElementEquals(a, i, value)) return i;
    }
    return -1;
}

namespace {

// One comparison of a non-generic array element against the object value.
int32_t CompareAnyElement(Il2CppArray* a, int32_t index, Il2CppObject* value,
                          Il2CppObject* comparer)
{
    if (a->isRefElem)
    {
        Il2CppObject* elem = reinterpret_cast<Il2CppObject**>(a->data)[index];
        if (comparer == nullptr) return ComparerCompareCore(elem, value);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyRef, elem, value);
    }
    // nb9 D10/D11: the ARRAY's recorded element kind decides the read; the
    // boxed value must belong to the same value family. A mismatch is NOT
    // coerced to 0: net48 raises InvalidOperationException (inner
    // ArgumentException) instead. Arrays with no recorded kind fall back to
    // the conservative stride heuristic.
    const AnyValueKind k = ClassifyAnyValue(value);
    const int32_t kind = a->elemKind;
    if (kind == 1 && k == AnyI4)
    {
        const int32_t elem = *reinterpret_cast<const int32_t*>(il2cpp_array_get_elem_ptr(a, index));
        const int32_t needle = runtime::icall_unbox_int32(value);
        if (comparer == nullptr) return elem < needle ? -1 : (elem > needle ? 1 : 0);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyI4, &elem, &needle);
    }
    if (kind == 2 && k == AnyI8)
    {
        const int64_t elem = *reinterpret_cast<const int64_t*>(il2cpp_array_get_elem_ptr(a, index));
        const int64_t needle = runtime::icall_unbox_long(value);
        if (comparer == nullptr) return elem < needle ? -1 : (elem > needle ? 1 : 0);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyI8, &elem, &needle);
    }
    if (kind == 3 && k == AnyF)
    {
        const double elem = static_cast<double>(
            *reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(a, index)));
        const double needle = runtime::icall_unbox_float(value);
        if (comparer == nullptr) return DoubleCompareTotal(elem, needle);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyD, &elem, &needle);
    }
    if (kind == 4 && k == AnyD)
    {
        const double elem = *reinterpret_cast<const double*>(il2cpp_array_get_elem_ptr(a, index));
        const double needle = runtime::icall_unbox_double(value);
        if (comparer == nullptr) return DoubleCompareTotal(elem, needle);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyD, &elem, &needle);
    }
    if (kind == 0)
    {
        if (a->elemSize == sizeof(int32_t) && k == AnyI4)
        {
            const int32_t elem = *reinterpret_cast<const int32_t*>(il2cpp_array_get_elem_ptr(a, index));
            const int32_t needle = runtime::icall_unbox_int32(value);
            if (comparer == nullptr) return elem < needle ? -1 : (elem > needle ? 1 : 0);
            return il2cpp_icomparer_compare_family(comparer, kComparerFamilyI4, &elem, &needle);
        }
        if (a->elemSize == sizeof(int64_t) && k == AnyI8)
        {
            const int64_t elem = *reinterpret_cast<const int64_t*>(il2cpp_array_get_elem_ptr(a, index));
            const int64_t needle = runtime::icall_unbox_long(value);
            if (comparer == nullptr) return elem < needle ? -1 : (elem > needle ? 1 : 0);
            return il2cpp_icomparer_compare_family(comparer, kComparerFamilyI8, &elem, &needle);
        }
        if (a->elemSize == sizeof(int64_t) && k == AnyD)
        {
            const double elem = *reinterpret_cast<const double*>(il2cpp_array_get_elem_ptr(a, index));
            const double needle = runtime::icall_unbox_double(value);
            if (comparer == nullptr) return DoubleCompareTotal(elem, needle);
            return il2cpp_icomparer_compare_family(comparer, kComparerFamilyD, &elem, &needle);
        }
    }
    il2cpp_throw_invalid_operation_exception("Failed to compare two elements in the array.");
    return 0; // unreachable
}

} // namespace

int32_t il2cpp_array_binary_search_any(Il2CppArray* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareAnyElement(a, mid, value, nullptr);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_any_icomparer(Il2CppArray* arr, Il2CppObject* value,
                                                 Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&value));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = 0;
    int32_t hi = a->length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareAnyElement(a, mid, value, comparer);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_any_range(Il2CppArray* arr, int32_t index,
                                             int32_t length, Il2CppObject* value)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRangeSort(a, index, length);
    int32_t lo = index;
    int32_t hi = index + length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareAnyElement(a, mid, value, nullptr);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

int32_t il2cpp_array_binary_search_any_icomparer_range(
    Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* value,
    Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRangeSort(a, index, length);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&value));
    roots.Add(reinterpret_cast<void**>(&comparer));
    int32_t lo = index;
    int32_t hi = index + length - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + (hi - lo) / 2;
        const int32_t c = CompareAnyElement(a, mid, value, comparer);
        if (c == 0) return mid;
        if (c < 0) lo = mid + 1;
        else hi = mid - 1;
    }
    return ~lo;
}

//==============================================================================
// nb9 D2/D3/D5/D6/D8: the element-kind-driven sorts (non-generic Sort(Array),
// Sort(Array,Array), Sort<object>(object[])) and the paired keys/items sort.
//==============================================================================

namespace {

int32_t ReadAnyCompareKind(const Il2CppArray* a)
{
    if (a->isRefElem) return 0;
    if (a->elemKind != 0) return a->elemKind;
    return a->elemSize == 8 ? 2 : 1; // fallback: int64 for 8-byte, int32 for 4
}

void SwapValueCells(Il2CppArray* a, int32_t i, int32_t j)
{
    if (a->isRefElem)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
        Il2CppObject* t = slots[i];
        slots[i] = slots[j];
        slots[j] = t;
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[j]), slots[j]);
        return;
    }
    char* base = a->data;
    char tmp[16];
    char* pi = base + static_cast<size_t>(i) * static_cast<size_t>(a->elemSize);
    char* pj = base + static_cast<size_t>(j) * static_cast<size_t>(a->elemSize);
    std::memcpy(tmp, pi, static_cast<size_t>(a->elemSize));
    std::memcpy(pi, pj, static_cast<size_t>(a->elemSize));
    std::memcpy(pj, tmp, static_cast<size_t>(a->elemSize));
}

int32_t CompareAnyDefault(Il2CppArray* a, int32_t i, int32_t j)
{
    const int32_t kind = ReadAnyCompareKind(a);
    if (kind == 0)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
        return ComparerCompareCore(slots[i], slots[j]);
    }
    if (kind == 1)
    {
        const int32_t* d = reinterpret_cast<const int32_t*>(a->data);
        return d[i] < d[j] ? -1 : (d[i] > d[j] ? 1 : 0);
    }
    if (kind == 3)
    {
        const float* d = reinterpret_cast<const float*>(a->data);
        return DoubleCompareTotal(static_cast<double>(d[i]), static_cast<double>(d[j]));
    }
    if (kind == 4)
    {
        const double* d = reinterpret_cast<const double*>(a->data);
        return DoubleCompareTotal(d[i], d[j]);
    }
    const int64_t* d = reinterpret_cast<const int64_t*>(a->data);
    return d[i] < d[j] ? -1 : (d[i] > d[j] ? 1 : 0);
}

template <typename Compare>
void QuickSortAnyIndices(Il2CppArray** pa, int32_t lo, int32_t hi, Compare compare)
{
    while (lo < hi)
    {
        const int32_t pivot = lo + (hi - lo) / 2;
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (compare(*pa, i, pivot) < 0) ++i;
            while (compare(*pa, j, pivot) > 0) --j;
            if (i <= j)
            {
                SwapValueCells(*pa, i, j);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortAnyIndices(pa, lo, j, compare);
            lo = i;
        }
        else
        {
            QuickSortAnyIndices(pa, i, hi, compare);
            hi = j;
        }
    }
}

struct AnyComparerCompare
{
    Il2CppObject** slot;
    int32_t operator()(Il2CppArray* a, int32_t i, int32_t j) const
    {
        const int32_t kind = ReadAnyCompareKind(a);
        if (kind == 0)
        {
            Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(a->data);
            return il2cpp_icomparer_compare_family(*slot, kComparerFamilyRef, slots[i], slots[j]);
        }
        if (kind == 1)
        {
            const int32_t* d = reinterpret_cast<const int32_t*>(a->data);
            int32_t x = d[i], y = d[j];
            return il2cpp_icomparer_compare_family(*slot, kComparerFamilyI4, &x, &y);
        }
        if (kind == 3)
        {
            const float* d = reinterpret_cast<const float*>(a->data);
            double x = static_cast<double>(d[i]), y = static_cast<double>(d[j]);
            return il2cpp_icomparer_compare_family(*slot, kComparerFamilyD, &x, &y);
        }
        if (kind == 4)
        {
            const double* d = reinterpret_cast<const double*>(a->data);
            double x = d[i], y = d[j];
            return il2cpp_icomparer_compare_family(*slot, kComparerFamilyD, &x, &y);
        }
        const int64_t* d = reinterpret_cast<const int64_t*>(a->data);
        int64_t x = d[i], y = d[j];
        return il2cpp_icomparer_compare_family(*slot, kComparerFamilyI8, &x, &y);
    }
};

// keys/items paired key comparison and swap.
int32_t CompareKeyPair(Il2CppArray* keys, int32_t i, int32_t j, Il2CppObject* comparer)
{
    const int32_t kind = ReadAnyCompareKind(keys);
    if (kind == 0)
    {
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(keys->data);
        if (comparer == nullptr) return ComparerCompareCore(slots[i], slots[j]);
        // nb9 E3: the StringComparer singletons have no reflected Compare
        // slot; compare the string keys ordinally (same special case as the
        // single-array comparer sort).
        if (comparer == &System_StringComparer_Ordinal_singleton)
            return CompareStringOrdinalElement(slots[i], slots[j], false);
        if (comparer == &System_StringComparer_OrdinalIgnoreCase_singleton)
            return CompareStringOrdinalElement(slots[i], slots[j], true);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyRef, slots[i], slots[j]);
    }
    if (kind == 1)
    {
        const int32_t* d = reinterpret_cast<const int32_t*>(keys->data);
        int32_t x = d[i], y = d[j];
        if (comparer == nullptr) return x < y ? -1 : (x > y ? 1 : 0);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyI4, &x, &y);
    }
    if (kind == 3)
    {
        const float* d = reinterpret_cast<const float*>(keys->data);
        double x = static_cast<double>(d[i]), y = static_cast<double>(d[j]);
        if (comparer == nullptr) return DoubleCompareTotal(x, y);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyD, &x, &y);
    }
    if (kind == 4)
    {
        const double* d = reinterpret_cast<const double*>(keys->data);
        double x = d[i], y = d[j];
        if (comparer == nullptr) return DoubleCompareTotal(x, y);
        return il2cpp_icomparer_compare_family(comparer, kComparerFamilyD, &x, &y);
    }
    const int64_t* d = reinterpret_cast<const int64_t*>(keys->data);
    int64_t x = d[i], y = d[j];
    if (comparer == nullptr) return x < y ? -1 : (x > y ? 1 : 0);
    return il2cpp_icomparer_compare_family(comparer, kComparerFamilyI8, &x, &y);
}

struct KeyPairCompare
{
    Il2CppArray** keys;
    Il2CppObject** comparer;
    int32_t operator()(Il2CppArray** /*unused*/, int32_t i, int32_t j) const
    {
        return CompareKeyPair(*keys, i, j, *comparer);
    }
};

void QuickSortKeyPairs(Il2CppArray** keys, Il2CppArray** items, int32_t lo, int32_t hi,
                       Il2CppObject** comparer)
{
    while (lo < hi)
    {
        const int32_t pivot = lo + (hi - lo) / 2;
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (CompareKeyPair(*keys, i, pivot, *comparer) < 0) ++i;
            while (CompareKeyPair(*keys, j, pivot, *comparer) > 0) --j;
            if (i <= j)
            {
                SwapValueCells(*keys, i, j);
                if (*items != nullptr) SwapValueCells(*items, i, j);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortKeyPairs(keys, items, lo, j, comparer);
            lo = i;
        }
        else
        {
            QuickSortKeyPairs(keys, items, i, hi, comparer);
            hi = j;
        }
    }
}

} // namespace

void il2cpp_array_sort_any(Il2CppArray* arr)
{
    Il2CppArray* a = RequireArraySort(arr);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    if (a->length > 1)
    {
        try
        {
            QuickSortAnyIndices(&a, 0, a->length - 1, CompareAnyDefault);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
}

void il2cpp_array_sort_any_range_icomparer(Il2CppArray* arr, int32_t index,
                                           int32_t length, Il2CppObject* comparer)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRangeSort(a, index, length);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    roots.Add(reinterpret_cast<void**>(&comparer));
    if (length > 1)
    {
        try
        {
            AnyComparerCompare cmp{&comparer};
            QuickSortAnyIndices(&a, index, index + length - 1, cmp);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
}

void il2cpp_array_sort_any_range(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireRangeSort(a, index, length);
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&a));
    if (length > 1)
    {
        try
        {
            QuickSortAnyIndices(&a, index, index + length - 1, CompareAnyDefault);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
}

void il2cpp_array_sort_keys_items(Il2CppArray* keys, Il2CppArray* items,
                                  Il2CppObject* comparer)
{
    Il2CppArray* k = RequireArraySort(keys);
    if (items == nullptr)
    {
        il2cpp_throw_argument_null_exception("items");
    }
    Il2CppArray* it = RequireArraySort(items);
    if (it->length < k->length)
    {
        il2cpp_throw_argument_exception(
            "The destination array was not long enough.");
    }
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&k));
    roots.Add(reinterpret_cast<void**>(&it));
    roots.Add(reinterpret_cast<void**>(&comparer));
    if (k->length > 1)
    {
        try
        {
            QuickSortKeyPairs(&k, &it, 0, k->length - 1, &comparer);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
}

void il2cpp_array_sort_keys_items_default(Il2CppArray* keys, Il2CppArray* items)
{
    il2cpp_array_sort_keys_items(keys, items, nullptr);
}

void il2cpp_array_sort_keys_items_range(Il2CppArray* keys, Il2CppArray* items,
                                        int32_t index, int32_t length,
                                        Il2CppObject* comparer)
{
    Il2CppArray* k = RequireArraySort(keys);
    if (items == nullptr)
    {
        il2cpp_throw_argument_null_exception("items");
    }
    Il2CppArray* it = RequireArraySort(items);
    RequireRangeSort(k, index, length);
    if (it->length < k->length)
    {
        il2cpp_throw_argument_exception(
            "The destination array was not long enough.");
    }
    RootScopeSort roots;
    roots.Add(reinterpret_cast<void**>(&k));
    roots.Add(reinterpret_cast<void**>(&it));
    roots.Add(reinterpret_cast<void**>(&comparer));
    if (length > 1)
    {
        try
        {
            QuickSortKeyPairs(&k, &it, index, index + length - 1, &comparer);
        }
        catch (const DotNetException&)
        {
            il2cpp_throw_invalid_operation_exception(kComparerFailedMessage);
        }
    }
}

void il2cpp_array_sort_keys_items_default_range(Il2CppArray* keys, Il2CppArray* items,
                                                int32_t index, int32_t length)
{
    il2cpp_array_sort_keys_items_range(keys, items, index, length, nullptr);
}

// ---- Array.Sort<T>(T[], int, int) for double/float (int32/uint32/int64/
// string partial sorts already exist) ---------------------------------------
void il2cpp_array_sort_double(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(double));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        QuickSortDoubles(reinterpret_cast<double*>(a->data), index, index + length - 1);
    }
}

void il2cpp_array_sort_float(Il2CppArray* arr, int32_t index, int32_t length)
{
    Il2CppArray* a = RequireArraySort(arr);
    RequireValueArraySort(a, sizeof(float));
    RequireRangeSort(a, index, length);
    if (length > 1)
    {
        QuickSortFloats(reinterpret_cast<float*>(a->data), index, index + length - 1);
    }
}

//------------------------------------------------------------------------------
// P25c enumerator-interface dispatch (yield iterators / user IEnumerator<T> /
// IEnumerable<T>). See arraysort.h for the full contract.
//
// The dispatchers resolve the receiver's vtable slot for a BCL iterator
// interface member through the typeinfo registry JOINED BY THE KLASS POINTER
// (il2cpp_typeinfo_lookup_by_class -- immune to the nested-type
// short/full-name mismatch that breaks the name lookup). The typeinfo method
// NAME for an explicit interface implementation carries the interface-qualified
// metadata name ("System.Collections.Generic.IEnumerator<System.Int32>.
// get_Current") while an implicit implementation carries the plain name
// ("MoveNext"); both share the member-name SUFFIX. The two get_Current /
// GetEnumerator slots (generic vs non-generic) are disambiguated by the
// SIGNATURE and the stored name's generic/non-generic marker.
//------------------------------------------------------------------------------

namespace {

// The enumerator-interface member selector for slot resolution.
enum IEnumMemberKind
{
    kIEnumMoveNext,        // "MoveNext" -- bool
    kIEnumGetCurrentI4,    // generic IEnumerator<T>.get_Current, int32-slot T
    kIEnumGetCurrentI8,    // generic IEnumerator<T>.get_Current, int64 T
    kIEnumGetCurrentD,     // generic IEnumerator<T>.get_Current, double/float T
    kIEnumGetCurrentRef,   // generic IEnumerator<T>.get_Current, ref-typed T
    kIEnumGetCurrentObj,   // non-generic IEnumerator.get_Current (object)
    kIEnumGetEnumerator,   // generic IEnumerable<T>.GetEnumerator
    kIEnumGetEnumeratorObj,// non-generic IEnumerable.GetEnumerator
    kIEnumReset,           // IEnumerator.Reset
    kIEnumDispose,         // System.IDisposable.Dispose
};

// Match the stored typeinfo method name against the member's SHORT name. An
// explicit interface impl stores "System.Collections.Generic.IEnumerator<System.Int32>.
// get_Current" (the interface-qualified metadata name); an implicit impl stores
// "MoveNext". Both share the "." + member suffix; the plain name matches
// exactly. A '.' boundary guard prevents "ResetAsync"-style false positives.
bool IEnumNameSuffixMatches(const char* stored, const char* member)
{
    if (stored == nullptr || member == nullptr)
    {
        return false;
    }
    const size_t slen = std::strlen(stored);
    const size_t mlen = std::strlen(member);
    if (mlen > slen)
    {
        return false;
    }
    const char* tail = stored + (slen - mlen);
    if (std::strcmp(tail, member) != 0)
    {
        return false;
    }
    return mlen == 0 || slen == mlen || tail[-1] == '.';
}

// True when the stored signature (MethodSignatureCoder encoding, e.g. "i()" /
// "b()" / "r:System.Object()") satisfies the member's carrier requirement.
bool IEnumSigMatches(IEnumMemberKind kind, const char* sig)
{
    if (sig == nullptr)
    {
        return false;
    }
    switch (kind)
    {
    case kIEnumMoveNext:
        return std::strncmp(sig, "b", 1) == 0 && sig[1] == '(';
    case kIEnumGetCurrentI4:
        // int32-slot value primitives (M1): i b c i1 u1 i2 u2 n. Two-char
        // codes checked first so "i1(" isn't misread as "i(".
        return std::strncmp(sig, "i1", 2) == 0 || std::strncmp(sig, "u1", 2) == 0 ||
            std::strncmp(sig, "i2", 2) == 0 || std::strncmp(sig, "u2", 2) == 0 ||
            std::strncmp(sig, "i(", 2) == 0 || std::strncmp(sig, "b(", 2) == 0 ||
            std::strncmp(sig, "c(", 2) == 0 || std::strncmp(sig, "n(", 2) == 0;
    case kIEnumGetCurrentI8:
        return std::strncmp(sig, "l", 1) == 0 || std::strncmp(sig, "q", 1) == 0;
    case kIEnumGetCurrentD:
        return std::strncmp(sig, "d", 1) == 0 || std::strncmp(sig, "f", 1) == 0;
    case kIEnumGetCurrentRef:
        // The generic IEnumerator<T>.get_Current for a reference-typed T. The
        // non-generic IEnumerator.get_Current slot's name is the explicit
        // "System.Collections.IEnumerator.get_Current" (impossible for a
        // generic interface impl); it is excluded by name below. T=object
        // produces the same "r:System.Object()" signature as the non-generic
        // slot, so the name check is the discriminator there.
        return std::strncmp(sig, "r:", 2) == 0 && sig[2] != '(';
    case kIEnumGetCurrentObj:
        // The NON-generic object-returning get_Current slot. Its name is the
        // explicit "System.Collections.IEnumerator.get_Current" on a class
        // that also implements IEnumerator<T>; a class implementing ONLY the
        // non-generic IEnumerator carries the plain "get_Current" name.
        return std::strcmp(sig, "r:System.Object()") == 0;
    case kIEnumGetEnumerator:
        // Generic IEnumerable<T>.GetEnumerator returns IEnumerator<T>; the
        // signature encodes the instantiation. A class that implements the
        // generic interface IMPLICITLY stores the PLAIN name "GetEnumerator"
        // with this same signature (MyEnum2) -- matched by signature alone.
        // (The prefix "r:System.Collections.Generic.IEnumerator`1" is 42 chars.)
        return std::strncmp(sig, "r:System.Collections.Generic.IEnumerator`1", 42) == 0;
    case kIEnumGetEnumeratorObj:
        // Non-generic IEnumerable.GetEnumerator returns plain IEnumerator.
        return std::strcmp(sig, "r:System.Collections.IEnumerator()") == 0;
    case kIEnumReset:
    case kIEnumDispose:
        return std::strncmp(sig, "v", 1) == 0 && sig[1] == '(';
    }
    return false;
}

// True when the stored name is VALID for the member kind. The generic
// get_Current / GetEnumerator interfaces can be implemented implicitly (the
// metadata name stays the plain "GetEnumerator"); the sole name-based
// discriminator needed is rejecting the EXACT non-generic interface-qualified
// "System.Collections.IEnumerator.get_Current" for the reference carrier
// (T=object collides with it on signature -- the name decides).
bool IEnumNameAcceptable(IEnumMemberKind kind, const char* stored)
{
    if (stored == nullptr)
    {
        return false;
    }
    switch (kind)
    {
    case kIEnumGetCurrentRef:
        return std::strcmp(stored, "System.Collections.IEnumerator.get_Current") != 0;
    case kIEnumGetCurrentObj:
        // The non-generic object get_Current either carries the explicit
        // interface-qualified name, or (a class implementing ONLY non-generic
        // IEnumerator) the plain "get_Current". A generic interface impl can
        // never carry the exact non-generic interface-qualified name.
        return std::strcmp(stored, "System.Collections.IEnumerator.get_Current") == 0
            || std::strcmp(stored, "get_Current") == 0;
    default:
        return true;
    }
}

} // namespace

// Resolve the receiver's vtable slot for the given enumerator-interface member.
// Returns -1 when the receiver or its type is null, the typeinfo is missing,
// or no reflected method matches the member name and carrier.
int32_t FindIEnumSlot(Il2CppObject* receiver, IEnumMemberKind kind, const char* member)
{
    if (receiver == nullptr || receiver->klass == nullptr)
    {
        return -1;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(receiver->klass);
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo* m = &scan->methods[i];
            if (m->virtualSlot < 0)
            {
                continue; // unreflected/non-virtual entries (property accessors)
            }
            if (!IEnumNameSuffixMatches(m->name, member))
            {
                continue;
            }
            if (!IEnumNameAcceptable(kind, m->name))
            {
                continue;
            }
            if (!IEnumSigMatches(kind, m->signature))
            {
                continue;
            }
            return m->virtualSlot;
        }
    }
    return -1;
}

// The C ABI of the iterator-member vtable slots (this-style receiver).
using IEnumFnVoid = void (*)(Il2CppObject*);
using IEnumFnBool = int32_t (*)(Il2CppObject*);
using IEnumFnI32 = int32_t (*)(Il2CppObject*);
using IEnumFnI64 = int64_t (*)(Il2CppObject*);
using IEnumFnD = double (*)(Il2CppObject*);
using IEnumFnRef = Il2CppObject* (*)(Il2CppObject*);

// Validate the dispatch target: the receiver's klass has a reflected vtable
// and the resolved slot is in range; otherwise throw NotSupportedException
// (the D-ICMP-1 contract -- the member must exist on the receiver's type).
void* RequireIEnumVtableSlot(Il2CppObject* receiver, int32_t slot, const char*)
{
    if (slot < 0 || receiver->klass->vtable == nullptr ||
        slot >= receiver->klass->vtableSize)
    {
        il2cpp_throw_not_supported_exception(
            "IEnumerator interface dispatch: the receiver type has no reflected "
            "virtual member for the requested iterator interface method.");
        return nullptr; // unreachable
    }
    return receiver->klass->vtable[slot];
}

//------------------------------------------------------------------------------
// il2cpp_ienum_move_next -- IEnumerator.MoveNext (bool, int32 slot).
//------------------------------------------------------------------------------
int32_t il2cpp_ienum_move_next(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P25d: BCL snapshot receivers with NO typeinfo route to their concrete
    // enumeration icalls (the SortedList/array snapshot enumerator object and
    // the Dictionary<string,int> snapshot enumerator object).
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
        return il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next(receiver);
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_Enumerator_string_int_class)
        return il2cpp_dict_si_enumerator_move_next(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumMoveNext, "MoveNext");
    return reinterpret_cast<IEnumFnBool>(
        RequireIEnumVtableSlot(receiver, slot, "MoveNext"))(receiver);
}

//------------------------------------------------------------------------------
// The get_Current carriers (generic IEnumerator<T>.get_Current).
//------------------------------------------------------------------------------
namespace {

// nb9 fix P0-4: read the CURRENT element of the shared array enumerator by the
// SNAPSHOT ARRAY's own layout. The enumerator backs plain `foreach` over an
// array as well as the LINQ Select/GroupBy results, so the element may be:
//   * a reference slot        (isRefElem -- a boxed value or a real reference)
//   * a 4-byte value          (int32 family)
//   * an 8-byte value         (int64 / double family)
// Reading a value array through the reference slot (the old i8/d path) treated
// the payload as a pointer -> NullReferenceException / access violation.
Il2CppArrayEnumerator* ArrayEnum(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppArrayEnumerator*>(self);
}

const char* ArrayEnumElemPtr(Il2CppArray* arr, int32_t index, int32_t* elemSize)
{
    *elemSize = arr->elemSize;
    return arr->data + static_cast<size_t>(index) * static_cast<size_t>(arr->elemSize);
}

// nb9 fix P0-3: unbox a boxed Int64/Int32 payload for the snapshot-enumerator
// i8 carrier. The box layout is the documented M2 one (klass at 0, aligned
// value at 8). A foreign payload is an InvalidCastException, never garbage.
int64_t UnboxInt64Payload(Il2CppObject* boxed, const char* expected)
{
    if (boxed == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    if (boxed->klass != nullptr)
    {
        if (std::strcmp(boxed->klass->name, "System.Int64") == 0)
            return *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(boxed) + 8);
        if (std::strcmp(boxed->klass->name, "System.Int32") == 0)
            return static_cast<int64_t>(
                *reinterpret_cast<int32_t*>(reinterpret_cast<char*>(boxed) + 8));
    }
    (void)expected;
    il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
    return 0; // unreachable
}

int64_t ArrayEnumI8(Il2CppObject* self)
{
    Il2CppArray* arr = ArrayEnum(self)->array;
    const int32_t idx = ArrayEnum(self)->index;
    if (arr->isRefElem)
    {
        Il2CppObject* boxed = reinterpret_cast<Il2CppObject* const*>(arr->data)[idx];
        return UnboxInt64Payload(boxed, "Int64");
    }
    int32_t size = 0;
    const char* p = ArrayEnumElemPtr(arr, idx, &size);
    if (size == 8) return *reinterpret_cast<const int64_t*>(p);
    if (size == 4) return static_cast<int64_t>(*reinterpret_cast<const int32_t*>(p));
    il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
    return 0;
}

double ArrayEnumD(Il2CppObject* self)
{
    Il2CppArray* arr = ArrayEnum(self)->array;
    const int32_t idx = ArrayEnum(self)->index;
    if (arr->isRefElem)
    {
        Il2CppObject* boxed = reinterpret_cast<Il2CppObject* const*>(arr->data)[idx];
        if (boxed == nullptr)
        {
            il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
        }
        if (boxed->klass != nullptr &&
            (std::strcmp(boxed->klass->name, "System.Double") == 0 ||
             std::strcmp(boxed->klass->name, "System.Single") == 0))
        {
            return *reinterpret_cast<double*>(reinterpret_cast<char*>(boxed) + 8);
        }
        il2cpp_throw_new(
            "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
            "Specified cast is not valid.");
    }
    int32_t size = 0;
    const char* p = ArrayEnumElemPtr(arr, idx, &size);
    if (size == 8) return *reinterpret_cast<const double*>(p);
    if (size == 4) return static_cast<double>(*reinterpret_cast<const int32_t*>(p));
    il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
    return 0.0;
}

int32_t ArrayEnumI4(Il2CppObject* self)
{
    Il2CppArray* arr = ArrayEnum(self)->array;
    const int32_t idx = ArrayEnum(self)->index;
    if (arr->isRefElem)
    {
        Il2CppObject* boxed = reinterpret_cast<Il2CppObject* const*>(arr->data)[idx];
        return runtime::icall_unbox_int32(boxed);
    }
    int32_t size = 0;
    const char* p = ArrayEnumElemPtr(arr, idx, &size);
    if (size == 4) return *reinterpret_cast<const int32_t*>(p);
    if (size == 8) return static_cast<int32_t>(*reinterpret_cast<const int64_t*>(p));
    il2cpp_throw_new(
        "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception",
        "Specified cast is not valid.");
    return 0;
}

Il2CppObject* ArrayEnumRef(Il2CppObject* self)
{
    Il2CppArray* arr = ArrayEnum(self)->array;
    const int32_t idx = ArrayEnum(self)->index;
    if (arr->isRefElem)
    {
        return reinterpret_cast<Il2CppObject* const*>(arr->data)[idx];
    }
    // A value array read through the reference carrier boxes the value.
    int32_t size = 0;
    const char* p = ArrayEnumElemPtr(arr, idx, &size);
    if (size == 4)
        return il2cpp_box_int32(nullptr, *reinterpret_cast<const int32_t*>(p));
    if (size == 8)
        return runtime::icall_box_long(*reinterpret_cast<const int64_t*>(p));
    return nullptr;
}

} // namespace

int32_t il2cpp_ienum_get_current_i4(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P3-extra (g4sortedlist2vals): the SortedList snapshot enumerator over
    // the boxed-int Values snapshot carries the i4 carrier directly (the
    // concrete wrapper unboxes; get_current_ref below already forwards to the
    // same core enumerator for the reference domain).
    // nb9 fix P0-4: the same enumerator backs `foreach` over a plain array AND
    // over the LINQ Select/GroupBy results, so the carrier reads the CURRENT
    // element by the snapshot array's own layout.
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
        return ArrayEnumI4(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetCurrentI4, "get_Current");
    return reinterpret_cast<IEnumFnI32>(
        RequireIEnumVtableSlot(receiver, slot, "get_Current"))(receiver);
}

int64_t il2cpp_ienum_get_current_i8(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // nb9 fix P0-3/P0-4: the shared array enumerator (plain arrays, Select and
    // GroupBy results). Reading it through the i8 virtual slot threw because
    // the snapshot type has no such slot.
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
        return ArrayEnumI8(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetCurrentI8, "get_Current");
    return reinterpret_cast<IEnumFnI64>(
        RequireIEnumVtableSlot(receiver, slot, "get_Current"))(receiver);
}

double il2cpp_ienum_get_current_d(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // nb9 fix P0-3/P0-4: same shared-enumerator arm for the double carrier.
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
        return ArrayEnumD(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetCurrentD, "get_Current");
    return reinterpret_cast<IEnumFnD>(
        RequireIEnumVtableSlot(receiver, slot, "get_Current"))(receiver);
}

Il2CppObject* il2cpp_ienum_get_current_ref(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P25d: the snapshot-object receivers forward to their concrete get_current
    // (the SortedList/array enumerator yields the element ref; the Dictionary
    // enumerator yields the per-pair KeyValuePair<string,int> object).
    // nb9 fix P0-4: a value-array element is boxed so the reference carrier
    // stays well-typed.
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
        return ArrayEnumRef(receiver);
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_Enumerator_string_int_class)
        return il2cpp_dict_si_enumerator_get_current(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetCurrentRef, "get_Current");
    return reinterpret_cast<IEnumFnRef>(
        RequireIEnumVtableSlot(receiver, slot, "get_Current"))(receiver);
}

//------------------------------------------------------------------------------
// il2cpp_ienum_get_current_obj -- the NON-generic IEnumerator.get_Current
// (always returns System.Object / r:System.Object()).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_ienum_get_current_obj(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P25d: the snapshot-object receivers forward (the non-generic get_Current
    // over a Dictionary enumerator still yields the pair object).
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
        return il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current(receiver);
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_Enumerator_string_int_class)
        return il2cpp_dict_si_enumerator_get_current(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetCurrentObj, "get_Current");
    return reinterpret_cast<IEnumFnRef>(
        RequireIEnumVtableSlot(receiver, slot, "get_Current"))(receiver);
}

//------------------------------------------------------------------------------
// il2cpp_ienum_get_enumerator -- the GENERIC IEnumerable<T>.GetEnumerator;
// il2cpp_ienum_get_enumerator_nongeneric -- the non-generic
// IEnumerable.GetEnumerator. The two are distinct vtable slots on the
// compiler-generated state machine (generic slot 1 vs non-generic slot 3).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_ienum_get_enumerator(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P25d: receivers with NO typeinfo route to their concrete enumerators --
    // a snapshot ARRAY (Keys/Values snapshots, klass name "["-prefixed)
    // becomes a snapshot array enumerator; the Dictionary<string,int> wrapper
    // becomes the snapshot PAIR enumerator; a List<object> wrapper snapshots
    // its items (listobj_get_enumerator returns the same array enumerator
    // the MoveNext/get_Current forwarders below already handle).
    if (receiver->klass->name != nullptr && receiver->klass->name[0] == kArrayClassMarker)
        return il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(receiver);
    // P3-extra (g4sortedlist2vals): the SortedList<string,int> Values view IS
    // the list object itself (get_Values returns self), so a generic
    // `foreach (int v in sl.Values)` reaches THIS dispatch with the list
    // klass. Forward to the concrete values snapshot enumerator (boxed-int
    // elements wrapped in the same Il2CppArrayEnumerator); the MoveNext /
    // get_Current(i4) / Reset / Dispose forwarders below handle the result.
    if (receiver->klass == &System_Collections_SortedList_class)
        return il2cpp::runtime::icall_sortedlist_si_values_get_enumerator(receiver);
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_string_int_class)
        return il2cpp_dict_si_get_enumerator(receiver);
    // nb9: the LINQ GroupBy surfaces (a grouping RESULT enumerates its
    // buckets; one grouping enumerates its elements). Both return the shared
    // managed array enumerator, so MoveNext/get_Current/Dispose ride the
    // forwarders below.
    if (receiver->klass == il2cpp_linq_grouping_result_class())
        return il2cpp_linq_grouping_result_get_enumerator(receiver);
    if (receiver->klass == il2cpp_linq_grouping_class())
        return il2cpp_linq_grouping_get_enumerator(receiver);
    if (il2cpp::il2cpp_is_list_obj(receiver))
        return il2cpp::il2cpp_list_obj_get_enumerator(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetEnumerator, "GetEnumerator");
    return reinterpret_cast<IEnumFnRef>(
        RequireIEnumVtableSlot(receiver, slot, "GetEnumerator"))(receiver);
}

Il2CppObject* il2cpp_ienum_get_enumerator_nongeneric(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P25d: same concrete receivers as the generic form above.
    if (receiver->klass->name != nullptr && receiver->klass->name[0] == kArrayClassMarker)
        return il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(receiver);
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_string_int_class)
        return il2cpp_dict_si_get_enumerator(receiver);
    // nb9: the LINQ GroupBy surfaces (a grouping RESULT enumerates its
    // buckets; one grouping enumerates its elements). Both return the shared
    // managed array enumerator, so MoveNext/get_Current/Dispose ride the
    // forwarders below.
    if (receiver->klass == il2cpp_linq_grouping_result_class())
        return il2cpp_linq_grouping_result_get_enumerator(receiver);
    if (receiver->klass == il2cpp_linq_grouping_class())
        return il2cpp_linq_grouping_get_enumerator(receiver);
    if (il2cpp::il2cpp_is_list_obj(receiver))
        return il2cpp::il2cpp_list_obj_get_enumerator(receiver);
    const int32_t slot = FindIEnumSlot(receiver, kIEnumGetEnumeratorObj, "GetEnumerator");
    return reinterpret_cast<IEnumFnRef>(
        RequireIEnumVtableSlot(receiver, slot, "GetEnumerator"))(receiver);
}

//------------------------------------------------------------------------------
// il2cpp_ienum_reset -- IEnumerator.Reset; il2cpp_ienum_dispose --
// System.IDisposable.Dispose. Dispose comes from the foreach `isinst
// System.IDisposable` guard; a receiver with NO reflected Dispose slot (e.g. a
// SortedList snapshot enumerator -- not IDisposable, the guard never fires) is
// a no-op, not a throw.
//------------------------------------------------------------------------------
void il2cpp_ienum_reset(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    // P25d: the snapshot-object receivers forward to their concrete resets
    // (the SortedList/array snapshot enumerator supports Reset).
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
    {
        il2cpp::runtime::il2cpp_sortedlist_enumerator_reset(receiver);
        return;
    }
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_Enumerator_string_int_class)
        return; // dict enumerator resets by index -- nothing to do (idempotent)
    const int32_t slot = FindIEnumSlot(receiver, kIEnumReset, "Reset");
    reinterpret_cast<IEnumFnVoid>(
        RequireIEnumVtableSlot(receiver, slot, "Reset"))(receiver);
}

void il2cpp_ienum_dispose(Il2CppObject* receiver)
{
    if (receiver == nullptr)
    {
        return; // null-safe: the isinst guard's null branch never calls in
                // (mirrors the D-SL-3 sortedlist no-op contract).
    }
    // P25d: the snapshot-object receivers dispose as no-ops (the SortedList /
    // Dictionary snapshot enumerators are not IDisposable).
    if (receiver->klass == &System_Collections_SortedList_Enumerator_class)
    {
        il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose(receiver);
        return;
    }
    if (receiver->klass == &System_Collections_Generic_Dictionary_2_Enumerator_string_int_class)
    {
        il2cpp_dict_si_enumerator_dispose(receiver);
        return;
    }
    const int32_t slot = FindIEnumSlot(receiver, kIEnumDispose, "Dispose");
    if (slot < 0)
    {
        return; // no reflected Dispose -- snapshot/BCL enumerators are not
                // IDisposable and the caller's guard already skipped them.
    }
    reinterpret_cast<IEnumFnVoid>(
        RequireIEnumVtableSlot(receiver, slot, "Dispose"))(receiver);
}

} // namespace il2cpp

