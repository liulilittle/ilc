#pragma once
//------------------------------------------------------------------------------
// il2cpp_array_ext.h -- System.Array method implementations (icall layer).
//
// Implements the System.Array surface the BCL needs beyond the raw storage
// helpers in il2cpp_array.h (creation / length / bounds-checked element
// access):
//
//   icall_array_copy       -- Array.Copy(Array, int, Array, int, int)
//   icall_array_sort       -- Array.Sort(Array)     (int32 arrays only)
//   icall_array_reverse    -- Array.Reverse(Array)  (int32 arrays only)
//   icall_array_index_of   -- Array.IndexOf(Array, object) (int32 + boxing)
//   icall_array_get_length      -- instance GetLength(int dimension)
//   icall_array_get_upper_bound -- instance GetUpperBound(int dimension)
//   icall_array_get_rank        -- instance get_Rank (1 in the one-dimensional
//                                  scope; the rank field sits on the array)
//
// Element-kind contract (matches array.cpp's M3 model):
//   - int32 arrays:   isRefElem == false, elemSize == 4
//   - reference arrays: isRefElem == true (elements are Il2CppObject* slots)
//   Sort / Reverse / IndexOf support int32 arrays ONLY; any other element
//   kind throws System.NotSupportedException (domain contract). Copy supports
//   both kinds -- reference-element copies move the references themselves
//   (identity), never the pointees.
//
// Exception mapping (managed semantics, thrown through il2cpp_throw_new with
// the full base-chain string):
//   null argument                    -> ArgumentNullException
//   negative index / length          -> ArgumentOutOfRangeException
//   index + length beyond an array   -> ArgumentException
//   int32 vs reference kind mismatch -> ArrayTypeMismatchException
//   unsupported element kind         -> NotSupportedException
//   out-of-range dimension           -> IndexOutOfRangeException
//   null instance receiver           -> NullReferenceException
//
// GC contract (reference-element copies): the destination slots are written
// through il2cpp_write_barrier semantics -- when the destination array lives
// in an old generation (>= 1), the covering card is marked so a young-only
// collection rescans the region (see gc.cpp). The copy itself performs no
// allocation, so no collection can interleave and no rooting is required.
//
// All parameters are Il2CppObject* (the generated code's reference type);
// the array layout is validated internally (klass name '[' prefix) before
// any array field is dereferenced -- a non-array object is a caller bug and
// raises ArgumentException instead of undefined behavior.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Array.Copy: copy 'length' elements from src[srcIndex] into dst[dstIndex].
//   Both arrays must have the same element kind (both int32 or both
//   reference); overlapping ranges (src == dst) copy as if through a
//   temporary buffer (memmove semantics). See the header comment for the
//   full validation order and exception mapping.
void icall_array_copy(Il2CppObject* src, int32_t srcIndex, Il2CppObject* dst,
                      int32_t dstIndex, int32_t length);

// Array.Sort: ascending in-place quicksort of an int32 array.
//   Non-int32 arrays throw System.NotSupportedException (domain contract).
void icall_array_sort(Il2CppObject* arr);

// Array.Reverse: in-place reversal of an int32 array.
//   Non-int32 arrays throw System.NotSupportedException (domain contract).
void icall_array_reverse(Il2CppObject* arr);

// Array.IndexOf: first index whose element equals 'value', or -1.
//   The value is compared after unboxing: only a boxed System.Int32 can
//   match an int32 element (null and every other object never match, which
//   mirrors EqualityComparer<int>.Default). Non-int32 arrays throw
//   System.NotSupportedException.
int32_t icall_array_index_of(Il2CppObject* arr, Il2CppObject* value);

// Instance Array.GetLength(int dimension): the number of elements in
// 'dimension' (one-dimensional scope: only dimension 0 is valid; any other
// dimension throws IndexOutOfRangeException, a null receiver throws
// NullReferenceException).
int32_t icall_array_get_length(Il2CppObject* arr, int32_t dimension);

// Instance Array.GetUpperBound(int dimension): the largest valid index in
// 'dimension' (length - 1 for one-dimensional arrays; -1 for an empty
// array). Dimension validation matches icall_array_get_length.
int32_t icall_array_get_upper_bound(Il2CppObject* arr, int32_t dimension);

// Instance Array.get_Rank: the number of dimensions. The one-dimensional
// scope always yields 1 (the array's rank field, set by the allocator). A
// null receiver throws NullReferenceException (instance semantics).
int32_t icall_array_get_rank(Il2CppObject* arr);

// Instance Array.get_LongLength: the element count widened to int64 (the
// net48 LongLength property returns Int64; for one-dimensional arrays it
// equals Length). A null receiver throws NullReferenceException.
int64_t icall_array_get_long_length(Il2CppObject* arr);

// Instance Array.GetLowerBound(int dimension): the lowest valid index in
// 'dimension'. One-dimensional scope: a zero-based array has lower bound 0;
// any dimension other than 0 throws IndexOutOfRangeException, a null
// receiver throws NullReferenceException (validation matches
// icall_array_get_length).
int32_t icall_array_get_lower_bound(Il2CppObject* arr, int32_t dimension);

// Instance Array.Initialize: value-type-element arrays have their element
// storage zeroed (the .NET observable result -- a modified int[] returns to
// all zeros; value types have a default ctor that produces the all-zero
// value). Reference-element arrays are a no-op (reference types have no
// default ctor to run). A null receiver throws NullReferenceException.
void icall_array_initialize(Il2CppObject* arr);

// System.ValueType.GetHashCode default for a value type whose FIRST instance
// field is a GC reference -- the reference arm of the transpiler's per-kind
// value-type hash wiring (MethodMap.ValueType.cs). 'data' is the value-type
// receiver's ADDRESS (the first field sits at offset 0); the object is
// hashed through icall_object_get_hash_code (string -> ordinal content,
// boxed Int32 -> the value, other -> address hash); a null reference field
// -> 0 (the net48 documented "first non-static field hash (null -> 0)"
// rule). Implemented in object.cpp. Declared here (the icall-layer header
// visible to the transpiler's generated-include chain) because it is the
// icall-layer surface in the writable domain.
int32_t icall_value_type_get_hash_code_ref(const void* data);

} // namespace runtime
} // namespace il2cpp
