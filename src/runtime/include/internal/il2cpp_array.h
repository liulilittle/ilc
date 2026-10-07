#pragma once
//------------------------------------------------------------------------------
// il2cpp_array.h -- managed array type (System.Array).
//
// Layout (GN-3 multidim: rank-aware, reference/single-dim zero-change):
//
//   struct Il2CppArray
//   {
//       Il2CppObject obj;       // object header (klass pointer)
//       int32_t      length;    // total element count (single-dim: element
//                               // count; multidim: product of per-dim sizes)
//       int32_t      elemSize;  // bytes per element (element storage stride)
//       Il2CppClass* elemClass; // element type descriptor (for arrays of
//                               // reference types; null for primitive arrays)
//       bool         isRefElem; // true: elements are GC references (Il2CppObject*)
//       int32_t      rank;      // number of dimensions (1: single-dim;
//                               // N: multidim -- Array.get_Rank returns it)
//       int32_t      dims[IL2CPP_MAX_ARRAY_RANK]; // per-dim sizes, dims[0..rank-1];
//                               // single-dim fills dims[0] = length. net48 stores
//                               // per-dim lengths with lower bounds (always 0 here).)
//       char         data[1];   // element storage: length * elemSize bytes inline,
//                               // row-major FLAT for multidim (d0-major, ...).
//   };
//
//   The array is a GC object allocated through il2cpp_gc_alloc_size (header +
//   length*elemSize payload). Reference-element arrays (Il2CppObject*[] etc.)
//   hold GC references that the collector scans -- the array class descriptor
//   carries a refFieldBitmap marking the payload region as references. The
//   GC scan reads arr->data / arr->length by FIELD access, so the explicit
//   dims field sits transparently in the header for every array (single-dim
//   payload offset auto-adjusts via offsetof).
//
//   Element access is bounds-checked (.NET IndexOutOfRangeException
//   semantics: an out-of-range index throws a catchable managed exception,
//   a null array throws NullReferenceException -- never a process abort).
//
//   M3.5/GN-3 scope: creation, length, element get/set (int32 and reference
//   elements) plus multi-dimensional int32 arrays (il2cpp_array_new_multi_
//   {v|ref}{N}d allocators + il2cpp_array_get/set_i4_{N}d accessors with
//   per-dim bounds checks). The full System.Array API is a BCL task.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {

// Maximum array rank supported by the runtime (net48 supports up to 32 in
// practice; the transpiler emits rank-fixed allocators/accessors only for the
// ranks it sees, so a modest compile-time ceiling keeps the explicit dims
// field small while leaving room beyond the probe's 2/3-rank scope).
constexpr int32_t IL2CPP_MAX_ARRAY_RANK = 8;

// Managed array: object header + length + element metadata + payload.
struct Il2CppArray
{
    Il2CppObject obj;       // object header (klass pointer)
    int32_t      length;    // total element count
    int32_t      elemSize;  // bytes per element
    Il2CppClass* elemClass; // element descriptor (null for primitive arrays)
    bool         isRefElem; // true: elements are GC references (Il2CppObject*)
    int32_t      rank;      // number of dimensions (1 in the one-dimensional scope)
    int32_t      dims[IL2CPP_MAX_ARRAY_RANK]; // per-dim sizes (dims[0..rank-1])
    char         data[1];   // element storage: length * elemSize bytes inline
};

// Allocate a zero-initialized array of 'length' elements of 'elemClass'.
//   elemSize    : bytes per element (4 for int32, sizeof(void*) for references)
//   isRefElement: true when elements are GC references (Il2CppObject*)
// Returns a GC reference; the caller must keep it reachable.
Il2CppArray* il2cpp_array_new(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t length);

// Multi-dimensional array allocation (GN-3): 'rank' per-dimension sizes, data
// stored ROW-MAJOR FLAT in the single inline payload (d0-major, then d1, ...).
// 'length' carries the TOTAL element count (product of dims), so the GC scan
// (arr->data / arr->length via field access, gc.cpp) and the flat-stelem
// initializer fold (il2cpp_array_get_elem_ptr) behave exactly as a 1-D array.
// Rank is capped at IL2CPP_MAX_ARRAY_RANK; dims[i] must be >= 0.
Il2CppArray* il2cpp_array_new_multi(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement,
                                    int32_t rank, const int32_t* dims);

// Rank-fixed multidim allocators (the transpiler emits these exact names):
//   {v|ref} -- value/reference element tag; <N>d -- the rank.
Il2CppArray* il2cpp_array_new_multi_v2d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1);
Il2CppArray* il2cpp_array_new_multi_v3d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1, int32_t d2);
Il2CppArray* il2cpp_array_new_multi_ref2d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1);
Il2CppArray* il2cpp_array_new_multi_ref3d(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t d0, int32_t d1, int32_t d2);

// Rank-fixed multidim int32 element access (GN-3): index -> flat offset
// (row-major), per-dimension bounds check (net48 IndexOutOfRangeException,
// catchable; null receiver -> NullReferenceException).
int32_t il2cpp_array_get_i4_2d(Il2CppArray* arr, int32_t i, int32_t j);
int32_t il2cpp_array_get_i4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k);
void    il2cpp_array_set_i4_2d(Il2CppArray* arr, int32_t i, int32_t j, int32_t value);
void    il2cpp_array_set_i4_3d(Il2CppArray* arr, int32_t i, int32_t j, int32_t k, int32_t value);

int32_t il2cpp_array_index_of_i4(const Il2CppArray* arr, int32_t value, int32_t start) noexcept;
int32_t il2cpp_array_last_index_of_i4(const Il2CppArray* arr, int32_t value, int32_t start) noexcept;
bool il2cpp_array_contains_i4(const Il2CppArray* arr, int32_t value) noexcept;
int32_t il2cpp_array_count_i4(const Il2CppArray* arr, int32_t value, int32_t start, int32_t count) noexcept;
bool il2cpp_array_try_bulk_copy_i4(Il2CppArray* dst, int32_t dstIndex, const Il2CppArray* src, int32_t srcIndex, int32_t count) noexcept;

//------------------------------------------------------------------------------
// Hot-path helpers (generated-code audit Top 1): INLINE definitions so the
// C++ compiler sees the fast path -- the bounds test becomes a register
// compare and the element address a direct pointer computation; the length
// read is a plain field load (hoistable / CSE-able / vectorizable). The
// failure path (null array / out-of-range index) is COLD: a single
// out-of-line [[noreturn]] throw call that the optimizer sinks out of the
// hot loop.
//------------------------------------------------------------------------------

// Cold failure path for il2cpp_array_get_elem_ptr (defined in array.cpp).
// Null array -> NullReferenceException, out-of-range -> IndexOutOfRangeException;
// both throw catchable managed exceptions; never returns.
[[noreturn]] void il2cpp_array_fail(Il2CppArray* arr, int32_t index);

// Bounds-checked element access. Returns the address of element 'index'
// (caller casts and reads/writes through it). Out-of-range access throws
// IndexOutOfRangeException; a null array throws NullReferenceException
// (both catchable by generated try/catch -- .NET semantics).
inline char* il2cpp_array_get_elem_ptr(Il2CppArray* arr, int32_t index)
{
    if (arr != nullptr && index >= 0 && index < arr->length)
    {
        return arr->data + static_cast<size_t>(index) * static_cast<size_t>(arr->elemSize);
    }
    il2cpp_array_fail(arr, index);
}

// Element count (the .NET Length property). Inline: a plain field read
// (null -> 0, matching the previous out-of-line behavior).
inline int32_t il2cpp_array_get_length(Il2CppArray* arr)
{
    return arr != nullptr ? arr->length : 0;
}

} // namespace il2cpp
