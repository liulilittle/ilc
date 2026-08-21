//------------------------------------------------------------------------------
// arrayext.cpp -- System.Array method implementations (icall layer).
//
// See il2cpp_array_ext.h for the full contract: supported element kinds,
// validation order, exception mapping and the GC/write-barrier rule for
// reference-element copies.
//
// Implementation notes:
//   - Every public entry point receives Il2CppObject* (the generated code's
//     reference type) and validates the array layout before dereferencing
//     any array field: the klass name MUST carry the '[' array-class marker
//     (array.cpp convention), otherwise the object is not an array and the
//     call raises ArgumentException instead of undefined behavior.
//   - Static-method null arguments are ArgumentNullException; the instance
//     receivers (GetLength / GetUpperBound) are NullReferenceException --
//     .NET instance semantics.
//   - Overlapping copies (src == dst) go through std::memmove so forward
//     and backward overlap both behave as if a temporary buffer were used.
//   - Reference-element copies run the write barrier on every written slot
//     AFTER the bulk memmove: the barrier only depends on the destination
//     block's generation (which does not change during the copy) and the
//     card is a "dirty since last collection" marker, so marking after the
//     move is equivalent to marking before each store -- and no collection
//     can interleave (the copy allocates nothing).
//   - Sort is a Hoare-partition quicksort with a middle-element pivot; it
//     recurses into the smaller half and iterates the larger half, bounding
//     the stack depth at O(log n) for any input ordering.
//------------------------------------------------------------------------------

#include "il2cpp_array_ext.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Managed exception type chains (type + base classes, the exception.cpp
// catch-matcher walks the chain after the first comma). Chain layout
// contract (exceptionext.cpp): each chain repeats the type as its first
// walk segment -- "{Type},{Type},{Base1},{Base2},..." -- so a catch of the
// thrown type itself matches (exact whole-string compare against the
// first element, walk starts after the first comma).
//------------------------------------------------------------------------------
const char kArgNullChain[]        = "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char kArgOutOfRangeChain[]  = "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
const char kArgumentChain[]       = "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char kTypeMismatchChain[]   = "System.ArrayTypeMismatchException,System.ArrayTypeMismatchException,System.SystemException,System.Exception";
const char kNotSupportedChain[]   = "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
const char kIndexOutOfRangeChain[] = "System.IndexOutOfRangeException,System.IndexOutOfRangeException,System.SystemException,System.Exception";
const char kNullRefChain[]        = "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

// Boxed int32 layout (boxing.cpp): payload at byte offset 8 (header + one
// aligned slot); the box class is named "System.Int32". The descriptor is
// file-local to boxing.cpp, so the type check compares the klass name.
constexpr int32_t kBoxedInt32PayloadOffset = 8;
const char kInt32BoxTypeName[] = "System.Int32";

//------------------------------------------------------------------------------
// RequireArray -- validate a static-method array argument.
//   null                -> ArgumentNullException
//   non-array object    -> ArgumentException (caller bug, never UB)
//------------------------------------------------------------------------------
Il2CppArray* RequireArray(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_new(kArgNullChain, "Value cannot be null.");
    }
    if (obj->klass == nullptr || obj->klass->name == nullptr ||
        obj->klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_new(kArgumentChain, "Object is not an array.");
    }
    return reinterpret_cast<Il2CppArray*>(obj);
}

//------------------------------------------------------------------------------
// RequireInstanceArray -- validate an instance-method receiver (null is a
// NullReferenceException, matching .NET instance-call semantics).
//------------------------------------------------------------------------------
Il2CppArray* RequireInstanceArray(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_new(kNullRefChain, "Object reference not set to an instance of an object.");
    }
    if (obj->klass == nullptr || obj->klass->name == nullptr ||
        obj->klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_new(kArgumentChain, "Object is not an array.");
    }
    return reinterpret_cast<Il2CppArray*>(obj);
}

//------------------------------------------------------------------------------
// RequireInt32Array -- int32-only domain contract (Sort / Reverse / IndexOf).
//   The M3 model has exactly two element kinds; anything that is not the
//   int32 layout (isRefElem == false, elemSize == 4) is unsupported.
//------------------------------------------------------------------------------
Il2CppArray* RequireInt32Array(Il2CppArray* arr)
{
    if (arr->isRefElem || arr->elemSize != 4)
    {
        il2cpp_throw_new(kNotSupportedChain,
                         "Only one-dimensional System.Int32 arrays are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireDimension -- one-dimensional scope: only dimension 0 exists.
//------------------------------------------------------------------------------
void RequireDimension(int32_t dimension)
{
    if (dimension != 0)
    {
        il2cpp_throw_new(kIndexOutOfRangeChain, "Dimension is out of range.");
    }
}

//------------------------------------------------------------------------------
// QuickSortRange -- Hoare-partition quicksort on data[lo..hi] (inclusive).
//   Middle-element pivot: already-sorted input (the common case) splits
//   evenly. The smaller half is recursed, the larger half iterated, so the
//   recursion depth is O(log n) for every input ordering.
//------------------------------------------------------------------------------
void QuickSortRange(int32_t* data, int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        const int32_t pivot = data[lo + (hi - lo) / 2];
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
                const int32_t tmp = data[i];
                data[i] = data[j];
                data[j] = tmp;
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            QuickSortRange(data, lo, j);
            lo = i;
        }
        else
        {
            QuickSortRange(data, i, hi);
            hi = j;
        }
    }
}

} // namespace

//------------------------------------------------------------------------------
// icall_array_copy -- Array.Copy (see the header contract).
//   Validation order: null args, negative indices/length, range overflow,
//   element-kind mismatch -- then the copy. length == 0 is a validated no-op
//   (a legal one-past-end index passes, matching .NET).
//------------------------------------------------------------------------------
void icall_array_copy(Il2CppObject* src, int32_t srcIndex, Il2CppObject* dst,
                      int32_t dstIndex, int32_t length)
{
    Il2CppArray* s = RequireArray(src);
    Il2CppArray* d = RequireArray(dst);

    if (srcIndex < 0 || dstIndex < 0 || length < 0)
    {
        il2cpp_throw_new(kArgOutOfRangeChain, "Index or length is less than zero.");
    }
    if (srcIndex > s->length || srcIndex + length > s->length)
    {
        il2cpp_throw_new(kArgumentChain, "Source array is not long enough.");
    }
    if (dstIndex > d->length || dstIndex + length > d->length)
    {
        il2cpp_throw_new(kArgumentChain, "Destination array is not long enough.");
    }
    if (s->isRefElem != d->isRefElem || s->elemSize != d->elemSize)
    {
        il2cpp_throw_new(kTypeMismatchChain,
                         "Source and destination arrays have incompatible element types.");
    }
    if (length == 0)
    {
        return; // validated no-op (indices may sit one past the end)
    }

    if (s->isRefElem)
    {
        // Reference-element copy: move the Il2CppObject* slots themselves
        // (identity -- never the pointees), then run the write barrier on
        // every written destination slot so an old-generation destination's
        // card is marked for a young-only collection (see the file comment).
        char* sPtr = s->data + static_cast<size_t>(srcIndex) * sizeof(void*);
        char* dPtr = d->data + static_cast<size_t>(dstIndex) * sizeof(void*);
        std::memmove(dPtr, sPtr, static_cast<size_t>(length) * sizeof(void*));
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(dPtr);
        for (int32_t i = 0; i < length; ++i)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        }
    }
    else
    {
        // Value-element copy: raw element bytes (int32 stride).
        std::memmove(d->data + static_cast<size_t>(dstIndex) * static_cast<size_t>(s->elemSize),
                     s->data + static_cast<size_t>(srcIndex) * static_cast<size_t>(s->elemSize),
                     static_cast<size_t>(length) * static_cast<size_t>(s->elemSize));
    }
}

//------------------------------------------------------------------------------
// icall_array_sort -- Array.Sort (int32 ascending; see the header contract).
//------------------------------------------------------------------------------
void icall_array_sort(Il2CppObject* arr)
{
    Il2CppArray* a = RequireArray(arr);
    RequireInt32Array(a);
    if (a->length > 1)
    {
        QuickSortRange(reinterpret_cast<int32_t*>(a->data), 0, a->length - 1);
    }
}

//------------------------------------------------------------------------------
// icall_array_reverse -- Array.Reverse (int32; see the header contract).
//------------------------------------------------------------------------------
void icall_array_reverse(Il2CppObject* arr)
{
    Il2CppArray* a = RequireArray(arr);
    RequireInt32Array(a);
    int32_t* data = reinterpret_cast<int32_t*>(a->data);
    for (int32_t i = 0, j = a->length - 1; i < j; ++i, --j)
    {
        const int32_t tmp = data[i];
        data[i] = data[j];
        data[j] = tmp;
    }
}

//------------------------------------------------------------------------------
// icall_array_index_of -- Array.IndexOf(Array, object) (see the header
// contract). The comparison mirrors EqualityComparer<int>.Default: only a
// boxed System.Int32 can equal an int32 element; null and every other
// object never match (return -1).
//------------------------------------------------------------------------------
int32_t icall_array_index_of(Il2CppObject* arr, Il2CppObject* value)
{
    Il2CppArray* a = RequireArray(arr);
    RequireInt32Array(a);
    if (value == nullptr || value->klass == nullptr || value->klass->name == nullptr ||
        std::strcmp(value->klass->name, kInt32BoxTypeName) != 0)
    {
        return -1; // no value of any other type can equal an int32 element
    }
    const int32_t boxed = *reinterpret_cast<const int32_t*>(
        reinterpret_cast<const char*>(value) + kBoxedInt32PayloadOffset);
    const int32_t* data = reinterpret_cast<const int32_t*>(a->data);
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (data[i] == boxed)
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// icall_array_get_length -- instance Array.GetLength (one-dimensional scope).
//------------------------------------------------------------------------------
int32_t icall_array_get_length(Il2CppObject* arr, int32_t dimension)
{
    Il2CppArray* a = RequireInstanceArray(arr);
    RequireDimension(dimension);
    return a->length;
}

//------------------------------------------------------------------------------
// icall_array_get_upper_bound -- instance Array.GetUpperBound (one-
// dimensional scope; -1 for an empty array, matching .NET).
//------------------------------------------------------------------------------
int32_t icall_array_get_upper_bound(Il2CppObject* arr, int32_t dimension)
{
    Il2CppArray* a = RequireInstanceArray(arr);
    RequireDimension(dimension);
    return a->length - 1;
}

//------------------------------------------------------------------------------
// icall_array_get_rank -- instance Array.get_Rank: the number of dimensions.
// The one-dimensional scope always returns 1 (the rank field the allocator
// stamps on every array; multi-dimensional arrays would carry a higher rank
// here). Null receiver -> NullReferenceException (instance semantics).
//------------------------------------------------------------------------------
int32_t icall_array_get_rank(Il2CppObject* arr)
{
    Il2CppArray* a = RequireInstanceArray(arr);
    return a->rank;
}

//------------------------------------------------------------------------------
// icall_array_get_long_length -- instance Array.get_LongLength: the element
// count widened to int64 (net48's LongLength returns Int64; for the one-
// dimensional scope it equals Length). Null receiver -> NullReferenceException.
//------------------------------------------------------------------------------
int64_t icall_array_get_long_length(Il2CppObject* arr)
{
    Il2CppArray* a = RequireInstanceArray(arr);
    return static_cast<int64_t>(a->length);
}

//------------------------------------------------------------------------------
// icall_array_get_lower_bound -- instance Array.GetLowerBound(int dimension):
// the lowest valid index in 'dimension'. A zero-based one-dimensional array
// has lower bound 0; validation (dimension != 0 -> IndexOutOfRangeException,
// null -> NullReferenceException) matches icall_array_get_length.
//------------------------------------------------------------------------------
int32_t icall_array_get_lower_bound(Il2CppObject* arr, int32_t dimension)
{
    RequireInstanceArray(arr);
    RequireDimension(dimension);
    return 0;
}

//------------------------------------------------------------------------------
// icall_array_initialize -- instance Array.Initialize: value-type-element
// arrays have their element storage zeroed (the net48 observable result -- a
// modified int[] returns to all zeros, because a value type's default ctor
// produces the all-zero value). Reference-element arrays are a no-op: the
// reference slots are left untouched (reference types have no default ctor
// for the runtime to run). Null receiver -> NullReferenceException.
//------------------------------------------------------------------------------
void icall_array_initialize(Il2CppObject* arr)
{
    Il2CppArray* a = RequireInstanceArray(arr);
    if (!a->isRefElem)
    {
        std::memset(a->data, 0,
                    static_cast<size_t>(a->length) * static_cast<size_t>(a->elemSize));
    }
}

} // namespace runtime
} // namespace il2cpp
