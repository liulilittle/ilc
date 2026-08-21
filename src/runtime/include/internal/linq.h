#pragma once
//------------------------------------------------------------------------------
// linq.h -- System.Linq array-family runtime (LINQ domain, T1 card).
//
// The AOT LINQ form (docs/design/linq-domain.md §1.3): LINQ static calls
// resolve to runtime icalls -- the source ARRAY plus the predicate DELEGATE
// OBJECT are the arguments; the runtime walks the array and calls the
// predicate through its methodPtr (the same bare function-pointer call the
// generated code emits for Delegate.Invoke, CodeGenerator.cs DelegateInvokeExpr).
//
// Element families (phase 1): only i4 (int32[]) and ref (Il2CppObject*[] --
// string[]/object[]/class[]) sources are reachable from generated code
// (newarr accepts only int32 and reference types, §4.2). i8/d/b entries
// stay unimplemented until newarr extends (no stubs -- dead code is
// forbidden by the domain contract).
//
// Callback contract (phase 1, §2 of the design): predicates are SINGLE-CAST
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

} // namespace il2cpp
