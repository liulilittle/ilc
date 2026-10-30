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

// p0grp: OrderBy / OrderByDescending over a DECIMAL source with an int32 TKey
// (net48 OrderBy<decimal,TKey>). The decimal element crosses BY VALUE to the
// key selector; the result is a fresh decimal[].
Il2CppArray* il2cpp_linq_order_by_key_dec_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_i4(Il2CppArray* src, Il2CppObject* keySel);
// p0grp: the same over a DECIMAL source with an int64 / double / decimal TKey.
Il2CppArray* il2cpp_linq_order_by_key_dec_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_dec_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_d(Il2CppArray* src, Il2CppObject* keySel);
// impl-orderfam: the DECIMAL-source FLOAT-key counterparts (impl-floatkey:
// a 4-byte float return read through a double(*) fn-pointer mixes the
// callee's XMM0 residue into every key -- b5-gaps §14).
Il2CppArray* il2cpp_linq_order_by_key_dec_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_dec_dec(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_dec(Il2CppArray* src, Il2CppObject* keySel);

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
//
// impl-thenbydesc: `k1desc` is the PRIMARY-key direction (0 asc, 1 desc) of the
// preceding OrderBy/OrderByDescending; it is emitted as a trailing literal by
// the transpiler fold and defaults to 0 (ascending) so older generated code
// keeps the OrderBy(k1).ThenBy(k2) meaning. Both directions are fused: the
// result is the stable (k1 asc|desc, k2 asc) order.
Il2CppArray* il2cpp_linq_then_by_key_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                        Il2CppObject* k2Sel, int32_t k1desc = 0);

// Enumerable.ThenByDescending -- the same fused composite sort with the
// SECONDARY key DESCENDING within each primary-key group: (k1 asc|desc,
// k2 desc), stable. Same single-cache contract (k1 once per element, k2 once
// per element) and same int32 element/key family gate.
Il2CppArray* il2cpp_linq_then_by_key_desc_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                             Il2CppObject* k2Sel, int32_t k1desc = 0);

//------------------------------------------------------------------------------
// Enumerable.OrderBy / OrderByDescending over an INT32 source with a WIDE key
// family (int64 / double). The int32-source entries above read the key from the
// int32 slot; a long/double key needs its own entry because the selector's
// return crosses in the int64 / F-stack slot respectively. The contract is
// otherwise identical to il2cpp_linq_order_by_key_i4: each element's key is
// cached EXACTLY ONCE (net48 EnumerableSorter), the merge sort is STABLE, the
// source is never written and the result is a NEW int32 array.
//   _i4_i8  TKey is int64/uint64;
//   _i4_d   TKey is double (the 8-byte F-stack carrier);
//   _i4_f   TKey is float (impl-floatkey: a Func<TSource,float> leaf returns
//           a 4-byte float per the MS x64 ABI -- reading it through a double
//           fn-pointer mixes the callee's XMM0 residue into every key, the
//           b5-gaps §14 silent mis-order -- thenby-floatprobe).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i4_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i4_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i4_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i4_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i4_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i4_d(Il2CppArray* src, Il2CppObject* keySel);

//------------------------------------------------------------------------------
// Enumerable.ThenBy / ThenByDescending over an INT32 source with a WIDE
// secondary key family (int64 / double) -- the fused (k1, k2) composite sort
// with the same single-cache contract as il2cpp_linq_then_by_key_i4. The
// primary key is the int32 key of the preceding OrderBy entry; the secondary
// key crosses in the int64 / F-stack slot.
//   _i4_i8  TKey is int64/uint64;
//   _i4_d   TKey is double.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_i4_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                           Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_desc_i4_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_i4_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                          Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_desc_i4_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                               Il2CppObject* k2Sel, int32_t k1desc = 0);

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
//   _d   TKey is double (the 8-byte F-stack carrier);
//   _f   TKey is float (impl-orderfam: the float family has its own float(*)
//        reader -- a 4-byte float return read through a double fn-pointer
//        mixes the callee's XMM0 residue into every key, the b5-gaps §14
//        misread);
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
Il2CppArray* il2cpp_linq_order_by_key_ref_f(Il2CppArray* src, Il2CppObject* keySel,
                                            int32_t desc);
Il2CppArray* il2cpp_linq_order_by_key_ref_ref(Il2CppArray* src, Il2CppObject* keySel,
                                              int32_t desc);

//------------------------------------------------------------------------------
// p0conv2 (D3): Enumerable.OrderBy / OrderByDescending over an int64 (long[] /
// ulong[]) SOURCE with a KEY SELECTOR.
//
// The int32-source entries above are value-family specific (they read 4-byte
// cells and materialize an int32[] result). A long[] source needs its own
// entry: the elements are 8-byte cells and the result MUST be an 8-byte
// int64 array stamped with elemKind I8 (otherwise string.Join and the
// element-kind Array scans read the low word as an int32).
//
// The key family decides the suffix, exactly like the reference-source family:
//   _i8_i4  TKey is a 32-bit integral (the selector returns in the int32 slot);
//   _i8_i8  TKey is int64/uint64.
// Keys are cached EXACTLY ONCE per element (net48 EnumerableSorter), the sort
// is STABLE, the source is never written and the result is a NEW int64 array.
// `desc` non-zero flips the order.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i8_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i8_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i8_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i8_i8(Il2CppArray* src, Il2CppObject* keySel);

// The int64-source float/double-key counterparts (p0conv2 D3 extension): the
// key crosses in the F-stack slot. Same cached-once / stable / fresh int64[]
// result contract as the int32-key entries above. _i8_f is the float family
// (impl-floatkey, b5-gaps §14): the 4-byte float return is read through a
// float(*) reader, never through the double reader.
Il2CppArray* il2cpp_linq_order_by_key_i8_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i8_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i8_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i8_d(Il2CppArray* src, Il2CppObject* keySel);

//------------------------------------------------------------------------------
// impl-linq3 (B): OrderBy / OrderByDescending with a DateTime / Guid KEY
// (net48 OrderBy<TSource,System.DateTime/Guid>) over the i4 / i8 / dec / ref
// sources. The key selector returns the struct BY VALUE; the keys are cached
// EXACTLY ONCE per element (net48 EnumerableSorter) and the stable merge sort
// orders by the carrier comparison: DateTime keys compare by their ticks
// (net48 DateTime.CompareTo), Guid keys by the net48 field-wise CompareTo
// contract (il2cpp_guid_compare). The result family follows the source (a
// fresh int32[] / int64[] / decimal[] / reference array); `desc` non-zero
// flips the order on the ref entries (an APPENDED literal, never a consumed
// IL argument).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i4_dt(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i4_dt(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i4_guid(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i4_guid(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i8_dt(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i8_dt(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_i8_guid(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_i8_guid(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_dec_dt(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_dt(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_dec_guid(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_dec_guid(Il2CppArray* src, Il2CppObject* keySel);

//------------------------------------------------------------------------------
// impl-linqleft: ThenBy / ThenByDescending over a DECIMAL source (or a decimal
// GroupBy bucket) -- the fused (k1, k2) composite sort. Each selector is cached
// EXACTLY ONCE, the sort is stable, and the result is a fresh decimal[]. The
// primary and secondary key families are INDEPENDENT and arrive as trailing
// literals: k1desc (0/1), k2desc (0/1), k1fam, k2fam where 0=int32, 1=int64,
// 2=double, 3=decimal, 4=System.DateTime, 5=System.Guid, 6=float
// (impl-orderfam: the float family has its own float(*) reader -- b5-gaps
// §14; the pre-existing 0..5 codes keep their meaning).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_dec(Il2CppArray* src, Il2CppObject* k1Sel,
                                         Il2CppObject* k2Sel, int32_t k1desc,
                                         int32_t k2desc, int32_t k1fam, int32_t k2fam);
Il2CppArray* il2cpp_linq_order_by_key_ref_dt(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t desc);
Il2CppArray* il2cpp_linq_order_by_key_ref_guid(Il2CppArray* src, Il2CppObject* keySel,
                                               int32_t desc);

// impl-vtsel: OrderBy / OrderByDescending over a BY-VALUE VALUE-STRUCT element
// source (net48 OrderBy<DateTime|TimeSpan|Guid|DateTimeOffset, TKey>). The
// element crosses the key selector in its own by-value slot; the result is a
// fresh typed array of the SAME element family.
//   srcKind  13 DateTime | 14 TimeSpan | 15 Guid | 16 DateTimeOffset
//            (IL2CPP_LIST_KIND_*, the list snapshot family)
//   keyFam   0 int32 | 1 int64 | 2 double | 3 float | 4 DateTime | 5 Guid |
//            6 TimeSpan | 7 DateTimeOffset
Il2CppArray* il2cpp_linq_order_by_key_vt(Il2CppArray* src, Il2CppObject* keySel,
                                         int32_t desc, int32_t srcKind, int32_t keyFam);

// impl-vtsel: ThenBy / ThenByDescending over a value-struct element source --
// the FUSED composite (net48 EnumerableSorter chain). Each element's primary
// and secondary keys are cached exactly once; the primary key decides first and
// the secondary only breaks ties. `k1Fam` / `k2Fam` use the keyFam codes above.
Il2CppArray* il2cpp_linq_then_by_key_vt(Il2CppArray* src, Il2CppObject* k1Sel,
                                        Il2CppObject* k2Sel, int32_t k1Desc,
                                        int32_t k2Desc, int32_t srcKind,
                                        int32_t k1Fam, int32_t k2Fam);

// The DOUBLE-source key-selector entries: the elements are 8-byte double cells
// and the key crosses in the F-stack slot. Same cached-once / stable / fresh
// double[] result contract.
//
// impl-orderfam: the WIDE ELEMENT-SOURCE keyed-sort surface (net48
// Enumerable.OrderBy over double[]/float[] sources). One entry per (source,
// key) family pair; the element crosses in the F-stack slot (a float element
// widens to double -- the InvokePredF / FPredFn convention) and the key
// returns in its own family slot:
//   _d_i4 / _d_i8 / _d (double key, the pre-existing entry) / _d_f,
//   _f_i4 / _f_i8 / _f_d / _f_f.
// The result family follows the SOURCE (a fresh double[] / float[] stamped
// with the source's element kind).
Il2CppArray* il2cpp_linq_order_by_key_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_d_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_d_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_d_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_d_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_d_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_d_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_f_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_f_i4(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_f_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_f_i8(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_f_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_f_d(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_f_f(Il2CppArray* src, Il2CppObject* keySel);
Il2CppArray* il2cpp_linq_order_by_key_desc_f_f(Il2CppArray* src, Il2CppObject* keySel);

//------------------------------------------------------------------------------
// p0linq4 (D4): Enumerable.ThenBy / ThenByDescending over an INT64 (long[] /
// ulong[]) SOURCE -- the int64-source counterparts of il2cpp_linq_then_by_key_
// [_desc]_i4. The fused (k1, k2) composite sort is identical in contract
// (each selector cached EXACTLY ONCE, stable, source never written, fresh
// int64[] result with elemKind I8); the elements are 8-byte cells and the key
// family is int32 (_i8_i4) or int64 (_i8_i8).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_i8_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                           Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_desc_i8_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_i8_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                           Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_desc_i8_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, int32_t k1desc = 0);

// The int64-source float/double-key counterparts (p0conv2 D3 extension): the
// secondary key crosses in the F-stack slot. Same fused composite contract as
// the int32-key entries above.
Il2CppArray* il2cpp_linq_then_by_key_i8_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                          Il2CppObject* k2Sel, int32_t k1desc = 0);
Il2CppArray* il2cpp_linq_then_by_key_desc_i8_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                               Il2CppObject* k2Sel, int32_t k1desc = 0);

//------------------------------------------------------------------------------
// impl-thenbyfam: the MIXED key-family fused composite over the int32 / int64
// value sources. The primary and secondary key families are INDEPENDENT (an
// int32 key returns in the int32 slot, an int64 key in the int64 slot, a
// double key in the F-stack slot), so each key gets its own reader and cache
// type -- sharing one reader across both selectors read the secondary key
// from the WRONG return slot (a silent mis-order on x64, the eax / xmm0
// residue). One dispatch entry per element family serves every (k1fam, k2fam)
// pair; the four flags ride trailing literals:
//   k1desc (0 asc / 1 desc), k2desc (0 asc / 1 desc), k1fam, k2fam
// with family codes 0=int32, 1=int64, 2=double, 3=float (impl-floatkey: the
// float family has its own float(*) reader -- the LinqValKeyFamilyCode
// contract). The transpiler gates both families compile-time; an
// out-of-range code is a LOUD InvalidOperationException, never a guessed
// reader. Same single-cache / stable / fresh-array contract as the
// homogeneous entries above.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_val_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                            Il2CppObject* k2Sel, int32_t k1desc,
                                            int32_t k2desc, int32_t k1fam,
                                            int32_t k2fam);
Il2CppArray* il2cpp_linq_then_by_key_val_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                            Il2CppObject* k2Sel, int32_t k1desc,
                                            int32_t k2desc, int32_t k1fam,
                                            int32_t k2fam);

} // namespace il2cpp
