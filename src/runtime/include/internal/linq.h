#pragma once
//------------------------------------------------------------------------------
// linq.h -- System.Linq array-family runtime (LINQ domain, T1 card).
//
// The AOT LINQ form (docs/design/linq-domain.md section 1.3): LINQ static calls
// resolve to runtime icalls -- the source ARRAY plus the predicate DELEGATE
// OBJECT are the arguments; the runtime walks the array and calls the
// predicate through its methodPtr (the same bare function-pointer call the
// generated code emits for Delegate.Invoke, CodeGenerator.cs DelegateInvokeExpr).
//
// Element families (phase 1): only i4 (int32[]) and ref (Il2CppObject*[] --
// string[]/object[]/class[]) sources are reachable from generated code
// (newarr accepts only int32 and reference types, section 4.2). i8/d/b entries
// stay unimplemented until newarr extends (no stubs -- dead code is
// forbidden by the domain contract).
//
// Callback contract (phase 1, section 2 of the design): predicates are SINGLE-CAST
// delegates with the generated-code ABI bool(*)(int32_t) /
// bool(*)(Il2CppObject*) (STATIC leaf) -- or, for INSTANCE-BOUND leaves
// (target != null, the linqcase shape p.IsEven / p.IsBig), the generated
// instance ABI bool(*)(Il2CppObject* this, args...) with the stored target
// passed in the `this` slot (linqib mode -- linq_map.cpp precedent). The
// runtime validates the delegate (null predicate -> ArgumentNullException,
// the LINQ exception contract; a multicast container -> InvalidOperation-
// Exception; a non-delegate object -> ArgumentException), then dispatches
// per binding.
//
// Empty-sequence truth table (net48): Count -> 0, Any()/Any(pred) -> false,
// All(pred) -> true (the predicate is NEVER called), Sum -> 0, Min/Max ->
// InvalidOperationException. Null source -> ArgumentNullException on every
// entry. Sum overflow throws OverflowException (net48 Enumerable.Sum is
// CHECKED -- D-LINQ-3). Any/All short-circuit at the first hit / first miss.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"

#include <cstdint>

namespace il2cpp {

// Count: the number of elements; null source -> ArgumentNullException;
// empty -> 0. Type-independent (the array is self-describing).
int32_t il2cpp_linq_count(Il2CppArray* src);

// Count(predicate) on int32[]: the number of elements satisfying the
// predicate (net48 Enumerable.Count<T>(IEnumerable<T>, Func<T,bool>)).
// The predicate is called on EVERY element -- NO short circuit (unlike
// Any/All); empty -> 0 without ever calling the predicate. Null source or
// null predicate -> ArgumentNullException; multicast predicate ->
// InvalidOperationException; instance-bound predicate -> dispatched per
// binding (the linq.cpp callback contract).
int32_t il2cpp_linq_count_i4(Il2CppArray* src, Il2CppObject* pred);

// Count(predicate) on reference-element arrays (string[]/object[]/class[]).
int32_t il2cpp_linq_count_ref(Il2CppArray* src, Il2CppObject* pred);

// Any(): true iff the sequence has at least one element; empty -> false.
// Type-independent.
bool il2cpp_linq_any(Il2CppArray* src);

// Any(predicate) on int32[]: true at the FIRST element satisfying the
// predicate (short circuit); empty -> false without calling the predicate.
bool il2cpp_linq_any_i4(Il2CppArray* src, Il2CppObject* pred);

// Any(predicate) on reference-element arrays (string[]/object[]/class[]).
bool il2cpp_linq_any_ref(Il2CppArray* src, Il2CppObject* pred);

// All(predicate) on int32[]: false at the FIRST element failing the
// predicate (short circuit); empty -> true without calling the predicate
// (vacuous truth, net48).
bool il2cpp_linq_all_i4(Il2CppArray* src, Il2CppObject* pred);

// All(predicate) on reference-element arrays.
bool il2cpp_linq_all_ref(Il2CppArray* src, Il2CppObject* pred);

// Sum on int32[]: CHECKED int32 addition -- an overflowing sum throws
// OverflowException (net48 Enumerable.Sum is checked, D-LINQ-3); empty -> 0.
int32_t il2cpp_linq_sum_i4(Il2CppArray* src);

// Min on int32[]: the smallest element; a single element returns itself;
// empty -> InvalidOperationException.
int32_t il2cpp_linq_min_i4(Il2CppArray* src);

// Max on int32[]: the largest element; a single element returns itself;
// empty -> InvalidOperationException.
int32_t il2cpp_linq_max_i4(Il2CppArray* src);

// Average on int32[]: the arithmetic mean as a double (net48 Enumerable.Average
// returns double); empty -> InvalidOperationException (net48). The sum is
// computed as int64 to avoid intermediate overflow (net48 uses a checked
// long accumulator), then divided by the count as a double.
double il2cpp_linq_average_i4(Il2CppArray* src);

// Reverse on int32[]: a NEW int32[] with elements in reverse order. The
// source is never modified; empty -> a new empty array.
Il2CppArray* il2cpp_linq_reverse_i4(Il2CppArray* src);

// Last on int32[]: the last element; empty -> InvalidOperationException.
int32_t il2cpp_linq_last_i4(Il2CppArray* src);

// LastOrDefault on int32[]: the last element, or 0 (int32 default) when empty.
int32_t il2cpp_linq_last_or_default_i4(Il2CppArray* src);

// ElementAt on int32[]: the element at the given index. index < 0 or
// index >= length -> ArgumentOutOfRangeException (net48).
int32_t il2cpp_linq_element_at_i4(Il2CppArray* src, int32_t index);

// Contains on int32[]: true iff the sequence contains the given value (net48
// ordinal int32 equality, EqualityComparer<int>.Default). Empty -> false.
bool il2cpp_linq_contains_i4(Il2CppArray* src, int32_t value);

// Aggregate(source, func) on int32[]: fold the sequence left-to-right using
// the binary func, seeding from the FIRST element (net48 Aggregate without a
// seed). Empty -> InvalidOperationException. The func delegate ABI is
// int32_t(*)(int32_t, int32_t) (static leaf) / int32_t(*)(Il2CppObject*,
// int32_t, int32_t) (instance leaf -- target in the `this` slot, linq_map
// per-binding dispatch). Each step's result becomes the next accumulator.
int32_t il2cpp_linq_aggregate_i4(Il2CppArray* src, Il2CppObject* func);

// LongCount on int32[]: the number of elements as int64 (net48 Enumerable.
// LongCount returns long). Null source -> ArgumentNullException; empty -> 0.
int64_t il2cpp_linq_long_count(Il2CppArray* src);

// SequenceEqual on int32[]: true iff both arrays have the same length and
// every pair of elements at the same position is equal (net48 ordinal int32
// equality). Null source -> ArgumentNullException; different lengths -> false.
bool il2cpp_linq_sequence_equal_i4(Il2CppArray* first, Il2CppArray* second);

// DefaultIfEmpty on int32[]: if the source is non-empty, return it as-is;
// if empty, return a NEW single-element int32[] containing 0 (the int32
// default). Null source -> ArgumentNullException.
Il2CppArray* il2cpp_linq_default_if_empty_i4(Il2CppArray* src);

} // namespace il2cpp
