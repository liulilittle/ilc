#pragma once
//------------------------------------------------------------------------------
// il2cpp_array.h -- managed array type (System.Array).
//
// Layout (one-dimensional, M3 scope; multi-dimensional is M3.5):
//
//   struct Il2CppArray
//   {
//       Il2CppObject obj;       // object header (klass pointer)
//       int32_t      length;    // number of elements
//       int32_t      elemSize;  // bytes per element (element storage stride)
//       Il2CppClass* elemClass; // element type descriptor (for arrays of
//                               // reference types; null for primitive arrays)
//       bool         isRefElem; // true: elements are GC references (Il2CppObject*)
//       int32_t      rank;      // number of dimensions (always 1: one-dimensional
//                               // scope -- the rank field carries Array.get_Rank
//                               // and future multi-dimensional support)
//       char         data[1];   // element storage: length * elemSize bytes inline
//   };
//
//   The array is a GC object allocated through il2cpp_gc_alloc_size (header +
//   length*elemSize payload). Reference-element arrays (Il2CppObject*[] etc.)
//   hold GC references that the collector scans -- the array class descriptor
//   carries a refFieldBitmap marking the payload region as references.
//
//   Element access is bounds-checked (.NET IndexOutOfRangeException
//   semantics: an out-of-range index throws a catchable managed exception,
//   a null array throws NullReferenceException -- never a process abort).
//
//   M3 scope: creation, length, element get/set (int32 and reference
//   elements), bounds checking. Multi-dimensional arrays, Array.Copy and the
//   full System.Array API are BCL tasks.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {

// Managed array: object header + length + element metadata + payload.
struct Il2CppArray
{
    Il2CppObject obj;       // object header (klass pointer)
    int32_t      length;    // number of elements
    int32_t      elemSize;  // bytes per element
    Il2CppClass* elemClass; // element descriptor (null for primitive arrays)
    bool         isRefElem; // true: elements are GC references (Il2CppObject*)
    int32_t      rank;      // number of dimensions (1 in the one-dimensional scope)
    char         data[1];   // element storage: length * elemSize bytes inline
};

// Allocate a zero-initialized array of 'length' elements of 'elemClass'.
//   elemSize    : bytes per element (4 for int32, sizeof(void*) for references)
//   isRefElement: true when elements are GC references (Il2CppObject*)
// Returns a GC reference; the caller must keep it reachable.
Il2CppArray* il2cpp_array_new(Il2CppClass* elemClass, int32_t elemSize, bool isRefElement, int32_t length);

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
