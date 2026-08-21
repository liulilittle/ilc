#pragma once
//------------------------------------------------------------------------------
// il2cpp_array_ext2.h -- System.Array extension-2 icalls (System.Array extended
// surface, second batch; see il2cpp_array_ext.h for the first batch).
//
// This header completes the one-dimensional System.Array surface the BCL
// needs beyond the storage helpers of il2cpp_array.h and the first icall
// batch of il2cpp_array_ext.h:
//
//   icall_array_sort_strings      -- Array.Sort(string[])   (culture compare)
//   icall_array_binary_search_int32    -- Array.BinarySearch(int32[])
//   icall_array_binary_search_string   -- Array.BinarySearch(string[])
//   icall_array_index_of_int32     -- Array.IndexOf<int>(int32[])
//   icall_array_last_index_of_int32     -- Array.LastIndexOf<int>(int32[])
//   icall_array_clear_int32        -- Array.Clear(int32[], int, int)
//   icall_array_find_index_int32   -- Array.FindIndex<int>(int32[], ...)
//   icall_array_reverse_range      -- Array.Reverse(int32[], int, int)
//
// Element-kind contract (matches array.cpp's M3 model):
//   - int32 arrays:    isRefElem == false, elemSize == 4
//   - reference arrays: isRefElem == true (elements are Il2CppObject* slots)
//   The int32-suffixed icalls support int32 arrays ONLY (NotSupportedException
//   for any other kind, mirroring the first batch's Sort/Reverse/IndexOf).
//   The string icalls (sort_strings / binary_search_string) support
//   reference arrays ONLY; every element is treated as an Il2CppString* or
//   null (see below).
//
// String comparison contract (CULTURE -- the domain pins the comparer here):
//   The runtime has no shared static culture comparer yet (the existing
//   icall_string_compare_to in il2cpp_string_ext.h is an INSTANCE method:
//   receiver vs. argument, ArgumentNullException on null argument). This
//   domain therefore carries its own static culture comparator (arrayext2.cpp,
//   CompareCulture) with .NET Framework Comparer<string>.Default semantics --
//   i.e. the culture-sensitive String.Compare under the process
//   CurrentCulture (zh-CN in the e2e harness), csc-probe-verified 2026-08-11:
//       null < any non-null string; null == null;
//       otherwise a case-INSENSITIVE PRIMARY pass (ASCII letter fold), then
//       on a primary tie the .NET SECONDARY pass -- per-character case weight
//       with LOWERCASE < UPPERCASE (the zh-CN collation order); a prefix pair
//       is decided by the length difference.
//   DOCUMENTED DEVIATION: full .NET collation tables (punctuation weights,
//   non-ASCII case folding) are not replicated -- punctuation-heavy or
//   non-ASCII inputs may order differently than net48. The comparison is a
//   pure CPU operation (no allocation, no collection interleaving), so
//   moving Il2CppObject* elements during a sort is safe with no GC safe
//   point and no rooting.
//
// BinarySearch complement semantics (.NET Array.BinarySearch contract):
//   A found element returns its index. A missing element returns the
//   bitwise complement (~index, i.e. -index - 1) of the INSERTION POINT --
//   the index of the first element greater than the searched value, or the
//   array length when every element is smaller. Every negative result is
//   therefore a complement: a caller restores the insertion point with
//   ~result. An empty array always returns -1 (= ~0, insertion point 0).
//
// Validation order (shared across the range-taking icalls, .NET semantics):
//   null array                  -> ArgumentNullException
//   negative index / length     -> ArgumentOutOfRangeException
//   index + length > array len  -> ArgumentException
//     (the sum is computed in 64-bit so int32 overflow can never bypass it)
//
// Exception mapping (managed semantics through il2cpp_throw_new, same chain
// convention as the first batch):
//   null argument                  -> ArgumentNullException
//   negative / out-of-range index  -> ArgumentOutOfRangeException
//   index + length beyond the array -> ArgumentException
//   unsupported element kind       -> NotSupportedException
//   non-string value / element     -> ArgumentException (defensive)
//
// GC contract (reference-element moves): every written slot of a reference
// array goes through il2cpp_write_barrier so an old-generation array's card
// is marked -- the sort performs no allocation, so no collection can
// interleave and no rooting is required.
//
// All parameters are Il2CppObject* (the generated code's reference type);
// the array layout is validated internally (klass name '[' prefix) before
// any array field is dereferenced -- a non-array object is a caller bug and
// raises ArgumentException instead of undefined behavior.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Array.Sort(string[]): ascending in-place sort using the CULTURE comparison
// described in the header comment (null elements sort first). Reference
// arrays only; an int32 array throws System.NotSupportedException. The sort
// is a Hoare-partition quicksort (middle-element pivot, smaller half recursed
// / larger half iterated -- O(log n) stack depth for every input ordering).
// It moves Il2CppObject* slots with write barriers and allocates nothing: no
// GC safe point, no rooting required.
void icall_array_sort_strings(Il2CppObject* arr);

// Array.BinarySearch(int32[]): binary search on an ASCENDING int32 array.
//   Returns the index of a found element, or the bitwise complement (~index)
//   of the insertion point when absent (see header comment). An empty array
//   returns -1. int32 arrays only.
int32_t icall_array_binary_search_int32(Il2CppObject* arr, int32_t value);

// Array.BinarySearch(string[]): the culture string version of
// icall_array_binary_search_int32. 'value' must be null (a legal search
// value: the null slot compares equal only to null elements) or a managed
// System.String; any other object throws System.ArgumentException. Reference
// arrays only.
int32_t icall_array_binary_search_string(Il2CppObject* arr, Il2CppObject* value);

// Array.IndexOf(int32): the index of the FIRST element equal to 'value',
// or -1 when absent (also for an empty array). int32 arrays only. This is
// the value-comparison variant (Array.IndexOf<int>) -- the boxed-object
// variant lives in il2cpp_array_ext.h (icall_array_index_of).
int32_t icall_array_index_of_int32(Il2CppObject* arr, int32_t value);

// Array.LastIndexOf(int32): the index of the LAST element equal to 'value'
// (backward scan), or -1 when absent. int32 arrays only.
int32_t icall_array_last_index_of_int32(Il2CppObject* arr, int32_t value);

// Array.Clear(int32[], int index, int length): zero 'length' int32 elements
// starting at 'index'. Validation per the header comment; length == 0 is a
// validated no-op (index may sit one past the end). int32 arrays only.
void icall_array_clear_int32(Il2CppObject* arr, int32_t index, int32_t length);

// Array.FindIndex(int32[]): the index of the first element equal to 'value'
// in [startIndex, startIndex + count), or -1 when absent. .NET validation:
//   startIndex < 0 || startIndex > Length   -> ArgumentOutOfRangeException
//   count < 0 || count > Length - startIndex -> ArgumentOutOfRangeException
// (startIndex == Length with count == 0 is legal and returns -1). int32
// arrays only.
int32_t icall_array_find_index_int32(Il2CppObject* arr, int32_t startIndex,
                                     int32_t count, int32_t value);

// Array.FindIndex<int>(int32[], int, int, Predicate<int>) -- the DELEGATE
// form (net48 Array.FindIndex<T>: every overload takes a Predicate<T>, never
// a value). The predicate is invoked per element through its methodPtr with
// PER-BINDING dispatch (the linq-domain delegate ABI -- see linq_map.h
// il2cpp_linq_where_i4): an instance-bound (captured-lambda) leaf receives
// its target in the `this` slot, a static leaf the plain Predicate<int>
// signature. null predicate -> ArgumentNullException; a multicast container
// -> InvalidOperationException; a non-delegate object -> ArgumentException.
// The array and the delegate are GC-rooted across the callback loop (the
// callback may allocate and the collector compacts the young generation; the
// delegate's target is re-read through the rooted delegate slot). int32
// arrays only. Semantics: the index of the FIRST element in
// [startIndex, startIndex + count) for which the predicate returns true, or
// -1 when no element matches. net48 range validation (same shape as
// icall_array_find_index_int32): startIndex < 0 || startIndex > Length ->
// ArgumentOutOfRangeException; count < 0 || count > Length - startIndex ->
// ArgumentOutOfRangeException. (The 2-arg and 3-arg net48 overloads
// FindIndex(array, Predicate) / FindIndex(array, startIndex, Predicate) are
// wired as the same call with the range folded in by the MethodMap.)
int32_t icall_array_find_index(Il2CppArray* arr, Il2CppObject* predicate,
                               int32_t startIndex, int32_t count);

// Array.Reverse(int32[], int index, int length): reverse the range
// [index, index + length) in place. Validation per the header comment
// (index + length > Length -> ArgumentException); length 0/1 is a no-op.
// int32 arrays only.
void icall_array_reverse_range(Il2CppObject* arr, int32_t index, int32_t length);

// Array.Find<int>(int32[], Predicate<int>) -- the PREDICATE form. Returns
// the VALUE of the first element for which the predicate returns true, or 0
// (default(int)) when no element matches (an empty array is always 0). The
// predicate is invoked per element through its methodPtr with the same
// PER-BINDING delegate ABI as icall_array_find_index (instance-bound leaves
// accepted; net48 order: array null -> ArgumentNullException, then match
// null -> ArgumentNullException). The array and the delegate are GC-rooted
// across the callback loop. int32 arrays only.
int32_t icall_array_find_i4(Il2CppArray* arr, Il2CppObject* match);

// Array.Exists<int>(int32[], Predicate<int>): 1 when at least one element
// matches, 0 otherwise (an empty array is always 0). Same validation order,
// callback contract and rooting as icall_array_find_i4. int32 arrays only.
int32_t icall_array_exists_i4(Il2CppArray* arr, Il2CppObject* match);

// Array.TrueForAll<int>(int32[], Predicate<int>): 1 when EVERY element
// matches, 0 otherwise (an empty array is VACUOUSLY true -- net48 semantics).
// Same validation order, callback contract and rooting as
// icall_array_find_i4. int32 arrays only.
int32_t icall_array_true_for_all_i4(Il2CppArray* arr, Il2CppObject* match);

// Array.ConvertAll<int,int>(int32[], Converter<int,int>): a NEW int32[] of
// the same length whose element i is converter(array[i]); the source is never
// modified. net48 validation order: array null -> ArgumentNullException, then
// converter null -> ArgumentNullException. The converter is invoked per
// element (int32 -> int32 signature) with the same PER-BINDING delegate ABI
// as icall_array_find_i4 (instance-bound leaves accepted). The delegate, the
// source and the result array are GC-rooted across the callback loop. int32
// arrays only.
Il2CppArray* icall_array_convert_all_i4_i4(Il2CppArray* arr, Il2CppObject* converter);

// Array.FindAll<int>(int32[], Predicate<int>): a NEW int32[] holding EVERY
// element for which the predicate returns true, in source order (an empty
// result is a zero-length array); the source is never modified. net48
// semantics: the predicate is invoked exactly once per element (reference
// source FindAllIterator). Same validation order (array null ->
// ArgumentNullException, then match null -> ArgumentNullException), callback
// contract and rooting as icall_array_find_i4. int32 arrays only.
Il2CppArray* icall_array_find_all_i4(Il2CppArray* arr, Il2CppObject* match);

// Array.Resize<int>(ref int32[] array, int newSize): 'array' is replaced by
// a NEW int32[] of exactly 'newSize' elements holding the first
// min(oldLength, newSize) elements of the old array (any extra elements are
// zero). net48 semantics: newSize < 0 -> ArgumentOutOfRangeException; a null
// array is legal (becomes new int32[newSize]); an equal length is a no-op
// (the same reference is kept). The caller's array slot is GC-rooted across
// the allocation so the collector rewrites it if the old array moves. int32
// arrays only.
void icall_array_resize_i4(Il2CppArray** arraySlot, int32_t newSize);

// Array.Empty<T>() -- a zero-length T[] (the source of the params-array
// Roslyn lowers TrimStart()/TrimEnd() no-arg calls to, among other
// consumers). One icall per wired generic argument:
//   icall_array_empty_char   -- char[]   (elemSize 2, non-reference)
//   icall_array_empty_int32  -- int32[]
//   icall_array_empty_string -- string[] (reference elements)
// The allocation is the newarr shape with a constant length 0 (elemClass
// null -- the primitive/reference distinction lives in the
// elemSize/isRefElement pair, il2cpp_array.h). DOCUMENTED DEVIATION: .NET
// caches one Array.Empty<T> singleton per T; here every call allocates a
// fresh zero-length array (no per-type cache exists in the runtime).
Il2CppObject* icall_array_empty_char();
Il2CppObject* icall_array_empty_int32();
Il2CppObject* icall_array_empty_string();
Il2CppObject* icall_array_empty_object();

} // namespace runtime
} // namespace il2cpp
