#pragma once
//------------------------------------------------------------------------------
// linq_sort.h -- System.Linq OrderBy domain (LINQ T3, docs/design/linq-domain.md
// section 4.5). The transpiler-facing surface for Enumerable.OrderBy over arrays.
//
// Shape (design section 4.1-4.2): one entry per element-type family (i4 / ref /
// string); the comparator arrives as a DELEGATE OBJECT (Il2CppObject*) and the
// runtime calls through its methodPtr with the fixed signature
// int32_t(*)(T, T) -- negative/zero/positive, the .NET Comparison<T>
// convention. The delegate object (not a bare pointer) keeps the callback
// rooted on the caller's stack.
//
// Semantics (net48-aligned, design section 3 / T3 card):
//   * STABLE sort -- equal keys keep their relative source order (net48
//     OrderBy is stable; verified by the index-marked keyed-pair tests).
//   * The result is a NEW array; the source array is never written.
//   * Empty / single-element arrays are validated no-ops returning a new
//     empty / single-element array.
//
// Validation / exceptions (DotNetException carrier, see il2cpp_exception_ext.h):
//   * null source                -> ArgumentNullException("source")
//   * non-array object           -> ArgumentException
//   * wrong element family for the entry -> NotSupportedException
//   * null comparator            -> ArgumentNullException("comparer")
//   * multicast container        -> InvalidOperationException (a raw
//     methodPtr cannot represent an invocation list; same loud-rejection
//     policy as icall_delegate_get_fn_ptr)
//   * instance-bound comparator  -> InvalidOperationException (no target
//     binding in this stage, design section 2)
//
// GC contract: the comparator is a user callback that may allocate, so the
// sort ROOTS both the source and the freshly allocated result array across
// the comparator phase (registered static-root slots; see linq_sort.cpp) and
// never holds a raw pointer into a managed payload across a comparator
// invocation -- the int32 sort runs on native heap and the reference sort
// orders native indices, materializing the result fresh from the rooted
// source only after the last comparator call. Every reference-slot store of
// the result runs the write barrier.
//
// Implementation note: the stable sort is a comparator-driven MERGE sort
// (template over the element type); it deliberately shares NO code with the
// in-place quicksorts of the arraysort domain (design section 4.5: OrderBy is
// comparator + stable + copy semantics).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_delegate.h"

#include <cstdint>

namespace il2cpp {

// Enumerable.OrderBy(int32[]): stable ascending order by 'cmp' (negative/zero/
// positive), returned as a NEW int32 array; the source is never modified.
// int32 arrays only (isRefElem == false, elemSize == 4); anything else throws
// System.NotSupportedException.
Il2CppArray* il2cpp_linq_order_by_i4(Il2CppArray* src, Il2CppObject* cmp);

// LOW-LEVEL comparison-ABI entry (NOT what Enumerable.OrderBy emits):
// stable order by a 2-arg Comparison<T>-shaped delegate over the reference
// slots (Il2CppObject*), returned as a NEW reference array; the source is never
// modified. Reference arrays only (isRefElem == true); the comparator decides
// the null policy (null elements ARE passed to the callback).
//
// NO CALLER: the transpiler has no emission site for this entry (grep over
// src/il2cppc finds only the explanatory comment in MethodMap.Linq.cs).
// Enumerable.OrderBy passes a 1-arg KEY SELECTOR, and calling a 1-arg function
// through a 2-arg signature is undefined behaviour, so this is kept only as the
// declared ABI for a caller that already HAS a Comparison<T>. Registered as an
// unreferenced legacy declaration in docs/audit/b5-gaps.md section 5c -- do NOT
// describe it as "wired".
Il2CppArray* il2cpp_linq_order_by_ref(Il2CppArray* src, Il2CppObject* cmp);

// String-typed variant of il2cpp_linq_order_by_ref: same ABI and stability
// contract. NO CALLER as well (see above); the transpiler's Enumerable.OrderBy
// path uses the key-selector surface, and a STRING KEY is rejected at transpile
// time (no collation in this runtime).
Il2CppArray* il2cpp_linq_order_by_string(Il2CppArray* src, Il2CppObject* cmp);

// Enumerable.OrderBy(TSource[], Func<TSource,TKey>) -- the KEY-SELECTOR
// surface (the D-LINQ-1 ABI bridge). Enumerable.OrderBy takes a 1-arg KEY
// SELECTOR Func<TSource,TKey>, NOT a 2-arg Comparison<T>; passing the selector
// to il2cpp_linq_order_by_i4 would call a 1-arg function with 2 args (UB).
// This entry takes the selector's delegate object and performs the sort
// itself: it caches each element's key EXACTLY ONCE (net48 EnumerableSorter
// semantics -- a side-effecting selector is never re-invoked), then runs the
// stable merge sort on the cached keys. int32 elements AND int32 keys only
// (the stage-1 key family); anything else throws System.NotSupportedException.
// Instance-bound single-cast selectors ARE accepted (linqib mode: target in
// the `this` slot); multicast containers are rejected.
Il2CppArray* il2cpp_linq_order_by_key_i4(Il2CppArray* src, Il2CppObject* keySel);

// Enumerable.OrderByDescending(TSource[], Func<TSource,TKey>) -- the KEY-
// SELECTOR surface, DESCENDING. Identical to il2cpp_linq_order_by_key_i4
// except the sort order is DESCENDING (keys compared in reverse). int32
// elements AND int32 keys only; anything else throws
// System.NotSupportedException. Instance-bound selectors accepted; multicast
// containers rejected.
Il2CppArray* il2cpp_linq_order_by_key_desc_i4(Il2CppArray* src, Il2CppObject* keySel);

// Enumerable.ThenBy -- the CHAINED-SORTER surface (D-LO2-1). Enumerable.
// OrderBy(k1).ThenBy(k2) is a FUSED composite sort (net48 EnumerableSorter
// chain): the elements order by (k1 asc, k2 asc), stable. This entry takes
// BOTH key selectors and runs the composite sort in one pass -- each
// element's PRIMARY key is cached EXACTLY ONCE, the SECONDARY key EXACTLY
// ONCE (net48 never re-invokes a selector), then the stable merge sort orders
// native indices by the tuple (k1, k2). int32 elements AND int32 keys only;
// everything else throws System.NotSupportedException. The transpiler's fold
// rewrites OrderBy(k1).ThenBy(k2) into a single call to this entry (the
// eager array materialization has no post-hoc primary-key recovery).
Il2CppArray* il2cpp_linq_then_by_key_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                        Il2CppObject* k2Sel);

// Enumerable.ThenByDescending -- the same fused composite sort with the
// SECONDARY key DESCENDING within each primary-key group: (k1 asc, k2 desc),
// stable. Same single-cache contract (k1 once per element, k2 once per
// element) and same int32 element/key family gate.
Il2CppArray* il2cpp_linq_then_by_key_desc_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                             Il2CppObject* k2Sel);

// Enumerable.OrderBy / OrderByDescending over a REFERENCE-element array with a
// KEY SELECTOR (Func<TSource,TKey>) -- the reference-source counterpart of
// il2cpp_linq_order_by_key_i4. Enumerable.OrderBy passes a 1-arg selector, so
// it must NOT go through il2cpp_linq_order_by_ref/string (those take a 2-arg
// Comparison<T>; calling a 1-arg function with 2 args is UB).
//
// The key family decides the entry suffix:
//   _i4  TKey is a 32-bit integral (bool/char/sbyte/byte/short/ushort/int/uint)
//        -- the selector's return is read from the int32 slot;
//   _i8  TKey is int64/uint64;
//   _d   TKey is float/double (the F-stack carrier);
//   _ref TKey is a reference: strings compare ORDINAL (Comparer<string>.Default
//        -- the arraysort ordinal contract), any other reference type defers to
//        IComparable.CompareTo on the first key (Comparer<T>.Default).
// Keys are cached EXACTLY ONCE per element (net48 EnumerableSorter). The
// result is a NEW reference array; the source is never modified. `desc`
// non-zero flips the order.
Il2CppArray* il2cpp_linq_order_by_key_ref_i4(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t desc);
Il2CppArray* il2cpp_linq_order_by_key_ref_i8(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t desc);
Il2CppArray* il2cpp_linq_order_by_key_ref_d(Il2CppArray* src, Il2CppObject* keySel,
                                            int32_t desc);
Il2CppArray* il2cpp_linq_order_by_key_ref_ref(Il2CppArray* src, Il2CppObject* keySel,
                                              int32_t desc);

} // namespace il2cpp
