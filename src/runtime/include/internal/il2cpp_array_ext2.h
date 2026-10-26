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
#include "il2cpp_string.h"

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
// ArgumentOutOfRangeException. (The 3-arg overload
// FindIndex(array, startIndex, Predicate) is NOT yet wired -- only the full
// 4-arg form and the 2-arg icall_array_find_index_2 exist.)
int32_t icall_array_find_index(Il2CppArray* arr, Il2CppObject* predicate,
                               int32_t startIndex, int32_t count);

// Array.FindIndex<T>(T[], Predicate<T>): the 2-arg overload -- the whole
// range (start=0, count=Length), delegating to the validated 4-arg core.
int32_t icall_array_find_index_2(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3(Il2CppArray* arr, Il2CppObject* predicate,
                                     int32_t startIndex);

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

// Array.ConvertAll<string,int>(string[], Converter<string,int>): the string
// source variant (net9/10 E2E icallcase: int.Parse as the converter). A NEW
// int32[] of the same length whose element i is converter(array[i]); the
// source is never modified. Same validation order and per-binding delegate
// ABI as the i4_i4 variant; the source is a REFERENCE array (string elements
// ride managed-ref slots). GC-rooted across the callback loop.
Il2CppArray* icall_array_convert_all_str_i4(Il2CppArray* arr, Il2CppObject* converter);

// Array.ConvertAll<int,long>(int32[], Converter<int,long>): the int64-output
// variant (icallwide E2E). A NEW int64[] of the same length; same validation
// order and per-binding delegate ABI as the i4_i4 variant. GC-rooted across
// the callback loop.
Il2CppArray* icall_array_convert_all_i4_i8(Il2CppArray* arr, Il2CppObject* converter);

// Array.FindAll<int>(int32[], Predicate<int>): a NEW int32[] holding EVERY
// element for which the predicate returns true, in source order (an empty
// result is a zero-length array); the source is never modified. net48
// semantics: the predicate is invoked exactly once per element (reference
// source FindAllIterator). Same validation order (array null ->
// ArgumentNullException, then match null -> ArgumentNullException), callback
// contract and rooting as icall_array_find_i4. int32 arrays only.
Il2CppArray* icall_array_find_all_i4(Il2CppArray* arr, Il2CppObject* match);

// Array.ForEach<int>(int32[], Action<int>): invoke the action once per
// element in source order (the source is never modified). net48 validation
// order (array null -> ArgumentNullException, then action null ->
// ArgumentNullException); the same single-cast delegate ABI and GC rooting as
// icall_array_find_i4. int32 arrays only.
void icall_array_for_each_i4(Il2CppArray* arr, Il2CppObject* action);

// Array.FindLast<int>(int32[], Predicate<int>): the VALUE of the LAST element
// whose predicate returns true, or 0 (default(int)) when none match (an empty
// array is always 0). Same validation order, per-binding callback and rooting
// contract as icall_array_find_i4, but the scan is BACKWARD (the last match is
// returned, not the first). int32 arrays only.
int32_t icall_array_find_last_i4(Il2CppArray* arr, Il2CppObject* match);

// Array.FindLastIndex<int>(int32[], Predicate<int>) and its range overloads:
// the index of the LAST element in [startIndex, startIndex + count) whose
// predicate returns true, or -1 when none match. Same range validation as
// icall_array_find_index (startIndex < 0 || startIndex > Length ->
// ArgumentOutOfRangeException; count < 0 || count > Length - startIndex ->
// ArgumentOutOfRangeException), the same per-binding callback ABI and GC
// rooting, but the scan is BACKWARD. The 2-arg overload is the whole range,
// the 3-arg form starts at startIndex. int32 arrays only.
int32_t icall_array_find_last_index(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3(Il2CppArray* arr, Il2CppObject* predicate,
                                      int32_t startIndex);

// Array.Resize<int>(ref int32[] array, int newSize): 'array' is replaced by
// a NEW int32[] of exactly 'newSize' elements holding the first
// min(oldLength, newSize) elements of the old array (any extra elements are
// zero). net48 semantics: newSize < 0 -> ArgumentOutOfRangeException; a null
// array is legal (becomes new int32[newSize]); an equal length is a no-op
// (the same reference is kept). The caller's array slot is GC-rooted across
// the allocation so the collector rewrites it if the old array moves. int32
// arrays only.
void icall_array_resize_i4(Il2CppArray** arraySlot, int32_t newSize);

// Array.ConvertAll<int,string>(int32[], Converter<int,string>): a NEW string[]
// (reference-element array) of the same length whose element i is
// converter(array[i]); the source is never modified. net48 validation order:
// array null, then converter null (both ArgumentNullException). Same
// per-binding delegate ABI and GC rooting discipline as icall_array_find_i4;
// the converter returns Il2CppString* and the result array is a reference array
// (the slots are written through the write barrier).
Il2CppArray* icall_array_convert_all_i4_str(Il2CppArray* arr, Il2CppObject* converter);

// BATCH4: the general Array.ConvertAll<TInput,TOutput> converter (see
// arrayext2.cpp). A NEW array of the same length whose element i is
// converter(array[i]); the source is never modified. The converter is invoked
// through its statically declared (TInput) -> TOutput ABI with per-binding
// dispatch; the result is a value cell (int32/int64/double) or a reference
// array (string/object/class). Same validation order and GC rooting as
// icall_array_convert_all_i4_i4. The family suffixes:
//   i4 int32 | i8 int64 | d double | str the reference families.
Il2CppArray* icall_array_convert_all_i4_d(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_i8_i4(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_i8_i8(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_i8_d(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_i8_str(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_d_i4(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_d_i8(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_d_d(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_d_str(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_str_i8(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_str_d(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_str_str(Il2CppArray* arr, Il2CppObject* converter);

// arrgen1b: the float converter families. A float SOURCE rides the double-slot
// ABI (the transpiler stores float in a double slot); a float RESULT is the
// float cell.
Il2CppArray* icall_array_convert_all_f_i4(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_f_i8(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_f_d(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_f_str(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_i4_f(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_i8_f(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_d_f(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_str_f(Il2CppArray* arr, Il2CppObject* converter);
Il2CppArray* icall_array_convert_all_f_f(Il2CppArray* arr, Il2CppObject* converter);

// The KIND-parameterized ConvertAll fallback for the narrow-integral families
// and their mixed pairs with the typed families. Kind encoding:
//   0 reference | 1 int32 | 2 int64 | 3 single | 4 double
//   8 bool | 9 char | 10 sbyte | 11 byte | 12 short | 13 ushort
// The delegate ABI is selected from the kind CATEGORY (ref / int32 / int64 /
// double); narrow cells widen to the int32 slot (the transpiler's narrow value
// model). Same validation order and GC rooting as ConvertAllFamily.
Il2CppArray* icall_array_convert_all_dyn(Il2CppArray* arr, Il2CppObject* converter,
                                         int32_t inKind, int32_t outKind);

// Array.Resize<long>(ref long[], int) / Array.Resize<string>(ref string[],
// int): the int64 / reference-element twins of icall_array_resize_i4 (the same
// net48 contract: newSize < 0 -> ArgumentOutOfRangeException; a null array is
// legal and becomes new T[newSize]; an equal length is a no-op). The array slot
// is GC-rooted across the allocation; the copy source is re-read through the
// rooted slot after the allocation. int64 arrays / reference arrays only.
void icall_array_resize_i8(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_str(Il2CppArray** arraySlot, int32_t newSize);

// Array.Find<long>(long[], Predicate<long>): the VALUE of the first element
// whose predicate returns true, or 0 (default(long)) when none match. Same
// validation order, per-binding callback and rooting as icall_array_find_i4;
// the predicate ABI is bool(*)(int64_t) / bool(*)(Il2CppObject*, int64_t)
// (the linq-domain I8PredFn/I8PredThisFn contract). int64 arrays only.
int64_t icall_array_find_i8(Il2CppArray* arr, Il2CppObject* match);

// Array.Exists<long>(long[], Predicate<long>) / Array.TrueForAll<long>:
// 1/0 verdicts (an empty array: Exists 0, TrueForAll vacuously 1). Same
// contract and ABI as icall_array_find_i8. int64 arrays only.
int32_t icall_array_exists_i8(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_i8(Il2CppArray* arr, Il2CppObject* match);

// Array.FindAll<long>(long[], Predicate<long>): a NEW int64[] holding every
// matching element in source order (an empty result is a zero-length array).
// Same contract and ABI as icall_array_find_i8. int64 arrays only.
Il2CppArray* icall_array_find_all_i8(Il2CppArray* arr, Il2CppObject* match);

// Array.FindIndex<long>(long[], Predicate<long>) and its range overloads: the
// index of the first matching element in the range, or -1. Same range
// validation and ABI as icall_array_find_i8. int64 arrays only.
int32_t icall_array_find_index_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                  int32_t startIndex, int32_t count);
int32_t icall_array_find_index_2_i8(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex);

// Array.Find<string>(string[], Predicate<string>): the VALUE (a managed
// reference) of the first element whose predicate returns true, or null when
// none match. The predicate ABI is bool(*)(Il2CppString*) /
// bool(*)(Il2CppObject*, Il2CppString*); the result is a managed reference.
// Reference arrays only.
Il2CppObject* icall_array_find_str(Il2CppArray* arr, Il2CppObject* match);

// Array.Exists<string> / Array.TrueForAll<string>: 1/0 verdicts (an empty
// array: Exists 0, TrueForAll vacuously 1). Same contract and ABI as
// icall_array_find_str. Reference arrays only.
int32_t icall_array_exists_str(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_str(Il2CppArray* arr, Il2CppObject* match);

// Array.FindAll<string>(string[], Predicate<string>): a NEW string[] holding
// every matching element in source order (an empty result is a zero-length
// array). Same contract and ABI as icall_array_find_str. Reference arrays only.
Il2CppArray* icall_array_find_all_str(Il2CppArray* arr, Il2CppObject* match);

// Array.FindIndex<string>(string[], Predicate<string>) and its range overloads:
// the index of the first matching element in the range, or -1. Same range
// validation and ABI as icall_array_find_str. Reference arrays only.
int32_t icall_array_find_index_str(Il2CppArray* arr, Il2CppObject* predicate,
                                   int32_t startIndex, int32_t count);
int32_t icall_array_find_index_2_str(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3_str(Il2CppArray* arr, Il2CppObject* predicate,
                                     int32_t startIndex);

// Array.Find<double>(double[], Predicate<double>): the VALUE of the first
// element whose predicate returns true, or 0.0 (default(double)) when none
// match. The predicate ABI is bool(*)(double) / bool(*)(Il2CppObject*, double)
// (the linq-domain DPredFn/DPredThisFn contract). double arrays only.
double icall_array_find_d(Il2CppArray* arr, Il2CppObject* match);

// Array.Exists<double> / Array.TrueForAll<double>: 1/0 verdicts (an empty
// array: Exists 0, TrueForAll vacuously 1). Same contract and ABI as
// icall_array_find_d. double arrays only.
int32_t icall_array_exists_d(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_d(Il2CppArray* arr, Il2CppObject* match);

// Array.FindLast<long> / Array.FindLast<double> / Array.FindLast<string>: the
// VALUE of the LAST element whose predicate returns true, or default(T) when
// none match (an empty array is always default(T)). BACKWARD scan; the same
// validation order, per-binding callback and GC rooting contract as the
// matching icall_array_find_*.
int64_t      icall_array_find_last_i8(Il2CppArray* arr, Il2CppObject* match);
double       icall_array_find_last_d(Il2CppArray* arr, Il2CppObject* match);
Il2CppObject* icall_array_find_last_str(Il2CppArray* arr, Il2CppObject* match);

// Array.ForEach<long> / Array.ForEach<double> / Array.ForEach<string>: invoke
// the action once per element in source order (the source is never modified).
// Same validation order, per-binding callback and GC rooting contract as
// icall_array_for_each_i4.
void icall_array_for_each_i8(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_d(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_str(Il2CppArray* arr, Il2CppObject* action);

// Array.FindAll<double>(double[], Predicate<double>): a NEW double[] holding
// every matching element in source order (an empty result is a zero-length
// array). Same contract and ABI as icall_array_find_all_i8. double arrays only.
Il2CppArray* icall_array_find_all_d(Il2CppArray* arr, Il2CppObject* match);

// Array.Resize<double>(ref double[], int newSize): the double twin of
// icall_array_resize_i4 (same net48 contract and rooting discipline).
void icall_array_resize_d(Il2CppArray** arraySlot, int32_t newSize);

//------------------------------------------------------------------------------
// The FLOAT generic family (the double twins with a 4-byte element stride).
// A float leaf's predicate ABI is the DOUBLE slot (the linq-domain FPredFn /
// FPredThisFn contract -- a float widens to double), so the callbacks are
// invoked through InvokeDPred. Same validation order, per-binding callback and
// GC rooting contract as the int32 family; float arrays only.
//------------------------------------------------------------------------------
float icall_array_find_f(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_f(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_f(Il2CppArray* arr, Il2CppObject* match);
float icall_array_find_last_f(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_f(Il2CppArray* arr, Il2CppObject* match);
void icall_array_for_each_f(Il2CppArray* arr, Il2CppObject* action);
void icall_array_resize_f(Il2CppArray** arraySlot, int32_t newSize);

// Array.FindIndex<double/float>(T[], Predicate<T>) and their _2/_3 overloads:
// the index of the first matching element in the range, or -1. Same range
// validation as icall_array_find_index; the double/float predicate ABI is
// InvokeDPred.
int32_t icall_array_find_index_d(Il2CppArray* arr, Il2CppObject* predicate,
                                  int32_t startIndex, int32_t count);
int32_t icall_array_find_index_2_d(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3_d(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex);
int32_t icall_array_find_index_f(Il2CppArray* arr, Il2CppObject* predicate,
                                  int32_t startIndex, int32_t count);
int32_t icall_array_find_index_2_f(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3_f(Il2CppArray* arr, Il2CppObject* predicate,
                                    int32_t startIndex);

// Array.FindLastIndex<long/string/double/float>(T[], Predicate<T>) and their
// _2/_3 overloads: the index of the LAST matching element in the range, or -1
// (BACKWARD scan). Same range validation as icall_array_find_last_index; the
// predicate ABI follows the family (I8PredFn / StrPredFn / DPredFn).
int32_t icall_array_find_last_index_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                        int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2_i8(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3_i8(Il2CppArray* arr, Il2CppObject* predicate,
                                          int32_t startIndex);
int32_t icall_array_find_last_index_str(Il2CppArray* arr, Il2CppObject* predicate,
                                         int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2_str(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3_str(Il2CppArray* arr, Il2CppObject* predicate,
                                           int32_t startIndex);
int32_t icall_array_find_last_index_d(Il2CppArray* arr, Il2CppObject* predicate,
                                       int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2_d(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3_d(Il2CppArray* arr, Il2CppObject* predicate,
                                         int32_t startIndex);
int32_t icall_array_find_last_index_f(Il2CppArray* arr, Il2CppObject* predicate,
                                       int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2_f(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3_f(Il2CppArray* arr, Il2CppObject* predicate,
                                         int32_t startIndex);

//------------------------------------------------------------------------------
// The UINT64 generic family (the ELEMENT_TYPE_U8 "q" slot). Same contract as
// the int64 family; the predicate ABI is bool(*)(uint64_t) /
// bool(*)(Il2CppObject*, uint64_t) (InvokeUPred). ulong arrays only.
//------------------------------------------------------------------------------
uint64_t icall_array_find_u8(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_u8(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_u8(Il2CppArray* arr, Il2CppObject* match);
uint64_t icall_array_find_last_u8(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_u8(Il2CppArray* arr, Il2CppObject* match);
void icall_array_for_each_u8(Il2CppArray* arr, Il2CppObject* action);
void icall_array_resize_u8(Il2CppArray** arraySlot, int32_t newSize);
int32_t icall_array_find_index_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                   int32_t startIndex, int32_t count);
int32_t icall_array_find_index_2_u8(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                     int32_t startIndex);
int32_t icall_array_find_last_index_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                        int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2_u8(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3_u8(Il2CppArray* arr, Il2CppObject* predicate,
                                          int32_t startIndex);

//------------------------------------------------------------------------------
// The NARROW-INTEGRAL generic families (bool/char/sbyte/byte/short/ushort/uint).
// Every narrow integral lives in an int32 slot (MethodSignatureCoder "b"/"c"/
// "i1"/"u1"/"i2"/"u2"/"i"), so the predicate ABI is the int32 one
// (InvokeFindIndexPred): the element cell is read at its real width and widened
// to int32 before the callback. The suffix follows the slot code:
//   _b bool | _c char | _i1 sbyte | _u1 byte | _i2 short | _u2 ushort | _u4 uint
// One template per operator (arrayext2.cpp); the explicit wrappers instantiate
// it per cell type. Same contract as the int32 family; the matching narrow
// array only.
//------------------------------------------------------------------------------
int32_t icall_array_find_b(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_c(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_i1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_u1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_i2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_u2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_u4(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_b(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_c(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_i1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_u1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_i2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_u2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_exists_u4(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_b(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_c(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_i1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_u1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_i2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_u2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_true_for_all_u4(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_b(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_c(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_i1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_u1(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_i2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_u2(Il2CppArray* arr, Il2CppObject* match);
int32_t icall_array_find_last_u4(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_b(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_c(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_i1(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_u1(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_i2(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_u2(Il2CppArray* arr, Il2CppObject* match);
Il2CppArray* icall_array_find_all_u4(Il2CppArray* arr, Il2CppObject* match);
void icall_array_for_each_b(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_c(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_i1(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_u1(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_i2(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_u2(Il2CppArray* arr, Il2CppObject* action);
void icall_array_for_each_u4(Il2CppArray* arr, Il2CppObject* action);
int32_t icall_array_find_index_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_index_2_b(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_2_c(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_2_i1(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_2_u1(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_2_i2(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_2_u2(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_2_u4(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_index_3_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_index_3_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_index_3_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_index_3_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_index_3_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_index_3_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_index_3_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex, int32_t count);
int32_t icall_array_find_last_index_2_b(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_2_c(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_2_i1(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_2_u1(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_2_i2(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_2_u2(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_2_u4(Il2CppArray* arr, Il2CppObject* predicate);
int32_t icall_array_find_last_index_3_b(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_3_c(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_3_i1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_3_u1(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_3_i2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_3_u2(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
int32_t icall_array_find_last_index_3_u4(Il2CppArray* arr, Il2CppObject* predicate, int32_t startIndex);
void icall_array_resize_b(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_c(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_i1(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_u1(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_i2(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_u2(Il2CppArray** arraySlot, int32_t newSize);
void icall_array_resize_u4(Il2CppArray** arraySlot, int32_t newSize);

// Array.Empty<T>() -- a zero-length T[] (the source of the params-array
// Roslyn lowers TrimStart()/TrimEnd() no-arg calls to, among other
// consumers). One icall per wired generic argument:
//   icall_array_empty_char   -- char[]   (elemSize 2, non-reference)
//   icall_array_empty_int32  -- int32[]
//   icall_array_empty_string -- string[] (reference elements)
//   icall_array_empty_long   -- int64[]  (W7; elemSize 8, non-reference)
//   icall_array_empty_double -- double[] (W7; elemSize 8, non-reference)
// The allocation is the newarr shape with a constant length 0 (elemClass
// null -- the primitive/reference distinction lives in the
// elemSize/isRefElement pair, il2cpp_array.h). DOCUMENTED DEVIATION: .NET
// caches one Array.Empty<T> singleton per T; here every call allocates a
// fresh zero-length array (no per-type cache exists in the runtime).
Il2CppObject* icall_array_empty_char();
Il2CppObject* icall_array_empty_int32();
Il2CppObject* icall_array_empty_string();
Il2CppObject* icall_array_empty_object();
Il2CppObject* icall_array_empty_long();
Il2CppObject* icall_array_empty_double();
// The float / narrow-integral empty factories (elemSize follows the element
// type). The narrow families have no dedicated elemKind, matching the char
// factory's plain allocation.
Il2CppObject* icall_array_empty_float();
Il2CppObject* icall_array_empty_sbyte();
Il2CppObject* icall_array_empty_byte();
Il2CppObject* icall_array_empty_bool();
Il2CppObject* icall_array_empty_short();
Il2CppObject* icall_array_empty_ushort();
Il2CppObject* icall_array_empty_uint();
// Array.Empty<ulong>(): the uint64 zero-length factory (the one primitive
// element family without a dedicated factory -- ulong[] has elemSize 8, the
// same plain-allocation shape as the uint32 factory above).
Il2CppObject* icall_array_empty_uint64();

// The shared CULTURE string comparator used by Array.Sort/BinarySearch
// (string[]) and the default Comparer<string> paths (defined in arrayext2.cpp;
// see the header's string-comparison contract). Exposed so the default
// comparer core (arraysort.cpp ComparerCompareCore) shares ONE implementation.
int32_t CompareCulture(const Il2CppString* a, const Il2CppString* b);

} // namespace runtime
} // namespace il2cpp
