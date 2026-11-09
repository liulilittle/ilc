#pragma once
//------------------------------------------------------------------------------
// linq_map.h -- System.Linq filtering / mapping domain (LINQ T2 card).
//
// AOT FORM (docs/design/linq-domain.md section 1.3, scheme 3): System.Linq is
// transpiled as runtime icalls that iterate an ARRAY and invoke a managed
// DELEGATE callback per element through its methodPtr (the generated-code
// delegate ABI, il2cpp_delegate.h). No iterators, no IEnumerable, no
// deferred execution -- stage 1 is EAGER materialization: every op returns a
// fresh array (il2cpp_array_new), so a where().select() chain is an
// array -> array icall sequence with no intermediate enumerator objects.
//
// FAMILIES (element type -> entry, section 4.2): only i4 (int32) and ref
// (Il2CppObject*) source families exist in stage 1 -- the transpiler's
// newarr only emits int32[] and reference-element arrays. select is a
// two-dimensional family <in>_<out>: the RESULT side is unrestricted (the
// runtime builds result arrays itself, e.g. int32 -> string[]).
//
// CALLBACK CONTRACT (section 2): the callback is passed as a DELEGATE OBJECT
// (Il2CppObject*). The runtime validates it (null -> ArgumentNullException;
// multicast container -> InvalidOperationException -- only single-cast
// delegates are callable as a bare methodPtr) and calls the methodPtr with
// the family's fixed signature, dispatching per binding:
//   predicate    bool(*)(T)                          (static leaf)
//                bool(*)(Il2CppObject*, T)           (instance leaf, target)
//   selector     Out(*)(T)                           (static leaf)
//                Out(*)(Il2CppObject*, T)            (instance leaf, target)
// INSTANCE-BOUND delegates (target != null -- a bound instance method such
// as Where(p.IsEven)) ARE supported: the generated instance ABI puts `this`
// (the stored target) in the FIRST parameter slot (CodeGenerator: instance
// arg0 = this), so the runtime calls methodPtr(target, args...) -- the same
// per-leaf dispatch as icall_multicast_invoke_i4 / the WindowProc slot pool.
// The callback's target is a GC reference, so the icalls root the delegate
// across the callback loop and re-read d->target through the rooted slot.
//
// EMPTY-SEQUENCE SEMANTICS (net48 baseline): First on an empty array ->
// InvalidOperationException; FirstOrDefault -> 0 (i4) / nullptr (ref).
//
// WHERE PASSES: the predicate runs EXACTLY ONCE per element (a single pass
// collects the matches into a growable buffer, then the exact-size result is
// trimmed) -- see linq_map.cpp il2cpp_linq_where_ref and the [where-calls] probe.
//
// GC SAFETY (see linq_map.cpp): the callback may allocate (a collection may
// move the source/result arrays), so the icalls root every array it holds
// across the callback loop through registered static-root slots and read
// the arrays back through those slots after each callback invocation.
//
// Namespace follows the il2cpp_*_* domain convention: namespace il2cpp,
// referenced from the transpiler as il2cpp::il2cpp_linq_*.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "decimal.h"     // P0LINQ1 (D2): the 16-byte System.Decimal value slot
#include "guid.h"        // BATCH4: the 16-byte System.Guid key carrier
#include "il2cpp_time.h" // BATCH4: the System.DateTime key carrier
// impl-groupbykey (the System.DateTimeOffset KEY carrier) and impl-listvtops
// (the System.DateTimeOffset ARRAY family) both need this header.
#include "il2cpp_dtoffset.h"

#include <cstdint>

namespace il2cpp {

// Where -- filter the source array through the predicate. Returns a NEW
// array of the same family (i4 -> int32[], ref -> Il2CppObject*[]) holding
// the matching elements in source order; the source is never modified.
//   src  : source array (null -> ArgumentNullException; wrong element
//          family for the entry -> NotSupportedException)
//   pred : delegate wrapping bool(*)(T) (validation per the contract above)
Il2CppArray* il2cpp_linq_where_i4(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray* il2cpp_linq_where_ref(Il2CppArray* src, Il2CppObject* pred);

// Where(source, (element, index) => bool) -- the index-aware overload
// (net48 Func<TSource,int,bool>). The predicate runs EXACTLY ONCE per element
// with the element's 0-based position in the source sequence; the result is a
// fresh exact-size int32[] preserving first-appearance order.
Il2CppArray* il2cpp_linq_where_idx_i4(Il2CppArray* src, Il2CppObject* pred);

// Where(source, (element, index) => bool) over a REFERENCE-element source --
// the reference counterpart of il2cpp_linq_where_idx_i4 (net48
// Func<TSource,int,bool>). The predicate runs EXACTLY ONCE per element with
// the element AND its 0-based SOURCE index; the result is a fresh exact-size
// reference-element array preserving first-appearance order. The predicate ABI
// is bool(*)(Il2CppObject*, int32_t) for a STATIC leaf / bool(*)(Il2CppObject*,
// Il2CppObject*, int32_t) for an INSTANCE leaf (target in the `this` slot).
Il2CppArray* il2cpp_linq_where_idx_ref(Il2CppArray* src, Il2CppObject* pred);

// Select -- map every element through the selector. Result length equals the
// source length. The selector returns Il2CppObject* for the ref-result
// entries (a string IS an Il2CppObject* -- int32 -> string[] uses the
// existing string domain inside the callback).
//   sel : delegate wrapping Out(*)(T) (validation per the contract above)
Il2CppArray* il2cpp_linq_select_i4_i4(Il2CppArray* src, Il2CppObject* sel);   // int32 -> int32
Il2CppArray* il2cpp_linq_select_i4_ref(Il2CppArray* src, Il2CppObject* sel);  // int32 -> string/object
Il2CppArray* il2cpp_linq_select_ref_ref(Il2CppArray* src, Il2CppObject* sel); // ref -> ref

// Select(source, (element, index) => int) -- the index-aware overload (net48
// Func<TSource,int,TResult>). The selector runs EXACTLY ONCE per element with
// the element's 0-based position; the result is a fresh int32[] of the same
// length as the source.
Il2CppArray* il2cpp_linq_select_idx_i4_i4(Il2CppArray* src, Il2CppObject* sel);

// nb9 FIX (M19): the non-int32 Select result families. net48's Select is
// generic in TResult, so the transpiler dispatches on it:
//   * int64   -> an int64[] value array
//   * double  -> a double[] value array
//   * ref     -> a reference array
// The *_idx forms are the Func<TSource,int,TResult> overloads (the selector
// receives the element AND its SOURCE index, exactly once per element).
Il2CppArray* il2cpp_linq_select_i4_i8(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_i4_d(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_ref(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_i8(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_d(Il2CppArray* src, Il2CppObject* sel);

// First -- the first element. Empty array -> InvalidOperationException.
int32_t       il2cpp_linq_first_i4(Il2CppArray* src);
Il2CppObject* il2cpp_linq_first_ref(Il2CppArray* src);

// First(predicate) -- the first element satisfying the predicate; walks the
// array from the start and returns the FIRST match (short-circuit). An empty
// array, or an array with no matching element, -> InvalidOperationException.
//   src  : source array (null -> ArgumentNullException; wrong element
//          family for the entry -> NotSupportedException)
//   pred : delegate wrapping bool(*)(int32_t) (validation per the contract
//          above: null -> ArgumentNullException)
int32_t il2cpp_linq_first_predicate(Il2CppArray* src, Il2CppObject* pred);

// FirstOrDefault -- the first element, or the family default when empty
// (0 / nullptr); never throws for an empty source.
int32_t       il2cpp_linq_first_or_default_i4(Il2CppArray* src);
Il2CppObject* il2cpp_linq_first_or_default_ref(Il2CppArray* src);

// FirstOrDefault(predicate) -- the first element satisfying the predicate,
// or 0 (the int32 default) when no element matches / the source is empty.
// Never throws for an empty or no-match source; walks from the start and
// returns the FIRST match (short-circuit).
//   src  : source array (null -> ArgumentNullException; wrong element
//          family for the entry -> NotSupportedException)
//   pred : delegate wrapping bool(*)(int32_t) (validation per the contract
//          above: null -> ArgumentNullException)
int32_t       il2cpp_linq_first_or_default_predicate(Il2CppArray* src, Il2CppObject* pred);

// Last(predicate) over a REFERENCE-element array -- the reference counterpart
// of il2cpp_linq_last_predicate. Walks the array BACKWARD and returns the LAST
// element satisfying the predicate (net48 short-circuits from the end). No
// match, or an empty source -> InvalidOperationException.
//   src  : reference-element source array (null -> ArgumentNullException)
//   pred : delegate wrapping bool(*)(Il2CppObject*); null -> ArgumentNullException
// impl-linq-vtpred: First(predicate) / FirstOrDefault(predicate) over a
// REFERENCE-element source -- the reference counterparts of
// il2cpp_linq_first_predicate / il2cpp_linq_first_or_default_predicate. The
// predicate is bool(*)(Il2CppObject*) (bound leaves take the target in the
// `this` slot); First throws when no element matches, FirstOrDefault answers
// nullptr for an empty / no-match source.
Il2CppObject* il2cpp_linq_first_pred_ref(Il2CppArray* src, Il2CppObject* pred);
Il2CppObject* il2cpp_linq_first_or_default_pred_ref(Il2CppArray* src, Il2CppObject* pred);

Il2CppObject* il2cpp_linq_last_pred_ref(Il2CppArray* src, Il2CppObject* pred);

// LastOrDefault(predicate) over a REFERENCE-element array: the LAST matching
// element, or nullptr when no element matches / the source is empty (never
// throws for those). Same walk direction and callback ABI as last_pred_ref.
Il2CppObject* il2cpp_linq_last_or_default_pred_ref(Il2CppArray* src, Il2CppObject* pred);

// Last(predicate) / LastOrDefault(predicate) over the int32 family: FORWARD
// full walk keeping the last match (net48 4.8 has no reverse optimization for
// the predicate form, so the predicate runs once per element).
int32_t il2cpp_linq_last_pred_i4(Il2CppArray* src, Il2CppObject* pred);
int32_t il2cpp_linq_last_or_default_pred_i4(Il2CppArray* src, Il2CppObject* pred);

// Take -- the first `count` elements (net48 semantics, probe-verified):
//   count <= 0      -> an EMPTY int32[] (net48's deferred TakeIterator only
//                      walks while `count > 0`, so a NEGATIVE count yields an
//                      empty result -- NO ArgumentOutOfRangeException, a real
//                      net48 deviation from the documented contract);
//   count >= length -> a full copy of the source;
//   otherwise       -> the first `count` elements in source order.
// Returns a NEW int32[]; the source is never modified.
//   src   : source array (null -> ArgumentNullException; ref elements ->
//           NotSupportedException)
//   count : the number of leading elements to keep
Il2CppArray*  il2cpp_linq_take_i4(Il2CppArray* src, int32_t count);

// Skip -- everything AFTER the first `count` elements (net48 semantics,
// probe-verified):
//   count <= 0      -> a FULL copy of the source (net48's SkipIterator yields
//                      everything when count <= 0 -- a NEGATIVE count returns
//                      all elements, NO ArgumentOutOfRangeException);
//   count >= length -> an EMPTY int32[];
//   otherwise       -> the elements from index `count` to the end, in order.
// Returns a NEW int32[]; the source is never modified.
//   src   : source array (null -> ArgumentNullException; ref elements ->
//           NotSupportedException)
//   count : the number of leading elements to drop
Il2CppArray*  il2cpp_linq_skip_i4(Il2CppArray* src, int32_t count);

// TakeWhile -- the longest PREFIX of elements satisfying the predicate
// (net48 TakeWhile semantics, probe-verified): walks from the start while
// predicate(e) is true and STOPS at the FIRST element for which the
// predicate returns false -- that element is NOT included in the result. An
// all-true source yields a full copy; an empty source yields an empty result
// with NO predicate invocation. The predicate runs EXACTLY ONCE per visited
// element (net48 -- unlike Where's documented two-pass deviation).
//   src  : source array (null -> ArgumentNullException; ref elements ->
//          NotSupportedException)
//   pred : delegate wrapping bool(*)(int32_t) (validation per the callback
//          contract: null -> ArgumentNullException)
Il2CppArray*  il2cpp_linq_take_while_i4(Il2CppArray* src, Il2CppObject* pred);

// SkipWhile -- everything AFTER the longest prefix satisfying the predicate
// (net48 SkipWhile semantics, probe-verified): walks from the start while
// predicate(e) is true; at the FIRST element for which the predicate returns
// false, that element AND EVERY REMAINING element are yielded -- the
// predicate is NEVER called again after the prefix ends. An all-true source
// yields an empty result; an empty source yields an empty result with NO
// predicate invocation.
//   src  : source array (null -> ArgumentNullException; ref elements ->
//          NotSupportedException)
//   pred : delegate wrapping bool(*)(int32_t) (validation per the callback
//          contract: null -> ArgumentNullException)
Il2CppArray*  il2cpp_linq_skip_while_i4(Il2CppArray* src, Il2CppObject* pred);

// The INDEX overloads (net48 TakeWhile(source, Func<TSource,int,bool>) /
// SkipWhile(source, Func<TSource,int,bool>)): the predicate receives the
// element AND its index in the SOURCE array (0-based), same prefix/skip
// semantics as the 1-arg forms above. The 2-arg predicate ABI is
// bool(*)(int32_t, int32_t) for a STATIC leaf and
// bool(*)(Il2CppObject*, int32_t, int32_t) for an INSTANCE leaf (target in
// the `this` slot -- the linq_map per-binding dispatch, Zip precedent).
Il2CppArray*  il2cpp_linq_take_while_idx_i4(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_idx_i4(Il2CppArray* src, Il2CppObject* pred);

// ToArray -- a deep clone: a fresh array with identical length, element
// layout and element values. Mutating the result never affects the source.
// Element-agnostic single entry (arrays are self-describing).
Il2CppArray*  il2cpp_linq_to_array(Il2CppArray* src);

// ToList -- materialize into a System.Collections.Generic.List<int32>
// managed object (the listi4 domain; the ONLY List<T> instantiation in
// stage 1). The entry is therefore int32-family-specific: a reference-
// element source raises NotSupportedException (a List<object> domain does
// not exist yet, design section 4.2).
//   src : source array (null -> ArgumentNullException; ref elements ->
//         NotSupportedException)
// Returns a NEW List<int> with count == src->length holding the source
// elements in order (il2cpp_list_i4_create + one add per element).
Il2CppObject* il2cpp_linq_to_list(Il2CppArray* src);

// P0LINQ1 (D1): ToList over the NON-int32 element families. net48's
// Enumerable.ToList<TSource> is generic in TSource, so the transpiler must
// dispatch on it; the runtime carries each family through the listobj domain
// (listobj.h -- the SAME List<T> object the reference/value List surface
// already uses, so Count/indexer/foreach resolve downstream):
//   * _ref    reference elements (string[]/object[]/class[]) -> List<T>
//             through il2cpp_list_obj_add (write barrier per slot).
//   * _i8     int64[]  -> il2cpp_list_obj_add_long  (box on entry)
//   * _d      double[] -> il2cpp_list_obj_add_double
//   * _f      float[]  -> il2cpp_list_obj_add_float (double carrier)
//   src : source array (null -> ArgumentNullException; a wrong element family
//         for the entry -> NotSupportedException)
// Every entry roots the source and the growing list across the per-element
// add (each add may allocate -> a compaction can move both).
Il2CppObject* il2cpp_linq_to_list_ref(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_i8(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_d(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_f(Il2CppArray* src);

// p0linq4 (D1): ToList over the REMAINING value families. The listobj storage
// is a reference array, so each element is BOXED on entry (see listobj.h):
//   * _dec  decimal[]     -> icall_box_decimal (16-byte payload)
//   * _dt   DateTime[]    -> the Int64 box carrying the CLR array image
//                            (ticks + 2-bit kind), so the element equals the
//                            managed DateTime[] cell
//   * _ts   TimeSpan[]    -> the Int64 box carrying the ticks
//   * _struct user struct[] -> icall_box_user_struct with the element type's
//                            OWN descriptor (payloadSize = sizeof(T), the
//                            transpiler passes it as a literal)
// GC SAFETY is the same contract as the entries above.
Il2CppObject* il2cpp_linq_to_list_dec(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_dt(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_ts(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_guid(Il2CppArray* src);
Il2CppObject* il2cpp_linq_to_list_struct(Il2CppArray* src, Il2CppClass* klass,
                                         int32_t payloadSize);

// delta 2: ToList<System.DateTimeOffset>. DateTimeOffset is the 16-byte
// Il2CppDateTimeOffset value (il2cpp_dtoffset.h); the listobj storage boxes
// each element through icall_box_date_time_offset (the Guid family contract).
Il2CppObject* il2cpp_linq_to_list_dtoffset(Il2CppArray* src);

//------------------------------------------------------------------------------
// P0LINQ1 (D2) -- the SOURCE-ONLY decimal aggregates. net48 has
// Sum/Min/Max/Average(this IEnumerable<decimal>) overloads; decimal is a
// 16-byte value type (decimal.h), so each entry walks the 16-byte cells:
//   * Sum      -- Decimal.Add accumulation (OVERFLOW -> OverflowException,
//                 net48 semantics); an empty source -> 0.
//   * Average  -- Sum / count as a DECIMAL division (net48 returns decimal,
//                 e.g. {1.5,2.5} -> 2.0); empty -> InvalidOperationException.
//   * Min/Max  -- numeric comparison (icall_decimal_compare), returning the
//                 WINNING OPERAND unchanged (scale preserved: {1.50,1.5} ->
//                 Min/Max both 1.50); empty -> InvalidOperationException.
// All four take a result slot FIRST (the decimal T3 contract, like
// icall_decimal_add) because a 16-byte value cannot ride the return register.
//   result : the output slot (never null -- caller-provided stack slot)
//   src    : source array (null -> ArgumentNullException; a non-16-byte or
//            reference-element source -> NotSupportedException)
//------------------------------------------------------------------------------
void il2cpp_linq_sum_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void il2cpp_linq_min_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void il2cpp_linq_max_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void il2cpp_linq_average_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);

// P0LINQ1 (D2) follow-up: the SELECTOR aggregates with a DECIMAL projection
// (net48 Sum/Min/Max/Average<TSource>(source, Func<TSource,decimal>)) over an
// INT32 source. The 16-byte decimal result rides an explicit result slot (the
// decimal T3 contract -- the slot is the FIRST parameter).
void il2cpp_linq_sum_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_min_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_max_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_average_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);

// impl-linq2 (B-1): the REFERENCE-source selector aggregates with a DECIMAL
// projection (net48 Sum/Min/Max/Average<TSource>(source, Func<TSource,decimal>)
// -- Enumerable.cs:1528/1735/1939/2133). The selector takes the reference
// element and returns the decimal BY VALUE; the 16-byte result rides the
// explicit result slot (the decimal T3 contract).
void il2cpp_linq_sum_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_min_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_max_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_average_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);

// P0LINQ1 (D2) follow-up: Aggregate with a DECIMAL accumulator (net48
// Aggregate<TSource,decimal>(source, seed, Func<decimal,TSource,decimal>)).
// The 16-byte decimal result rides an explicit result slot (the decimal T3
// contract -- the slot is the FIRST parameter); the seed crosses as a decimal
// value slot and the callback returns the decimal BY VALUE.
void il2cpp_linq_aggregate_seed_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                   const runtime::Il2CppDecimal* seed, Il2CppObject* func);

// impl-linq2 (A): the DECIMAL-SOURCE Aggregate family (net48 Aggregate over a
// System.Decimal source -- Enumerable.cs:1379/1391/1399). The decimal cells are
// read BY VALUE; the fold callback is decimal x decimal -> decimal. The 3-arity
// form folds from 'seed' (an empty source returns it unchanged); the no-seed
// form seeds from the FIRST cell (empty -> InvalidOperationException).
void il2cpp_linq_aggregate_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* func);
void il2cpp_linq_aggregate_seed_dec_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                        const runtime::Il2CppDecimal* seed, Il2CppObject* func);

// impl-linq2 (A): Aggregate(seed, func, resultSelector) with a DECIMAL
// accumulator (net48 Aggregate<TSource,TAccumulate,TResult> -- Enumerable.cs
// :1399). The fold runs first, then the result selector runs ONCE on the final
// decimal accumulator; TResult picks the projection ABI (int32/int64/float/
// double/decimal-by-value/reference). The "_dec" base folds the INT32 source
// reader (TSource = int32); the "_decsrc" base reads 16-byte decimal cells
// (TSource = System.Decimal).
int32_t il2cpp_linq_aggregate_seed_dec_rs_i4(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                             Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_dec_rs_i8(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                             Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_dec_rs_f(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_dec_rs_d(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_dec_rs_ref(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                    Il2CppObject* func, Il2CppObject* resSel);
void il2cpp_linq_aggregate_seed_dec_rs_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                           const runtime::Il2CppDecimal* seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
int32_t il2cpp_linq_aggregate_seed_decsrc_rs_i4(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_decsrc_rs_i8(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_decsrc_rs_f(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_decsrc_rs_d(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_decsrc_rs_ref(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                       Il2CppObject* func, Il2CppObject* resSel);
void il2cpp_linq_aggregate_seed_decsrc_rs_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                              const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);

// impl-aggvt: Aggregate over a NON-decimal VALUE-TYPE source (System.DateTime /
// System.TimeSpan / System.Guid) with a DECIMAL accumulator (net48
// Aggregate<TSource,decimal>/<TSource,decimal,TResult>). The element is read at
// the source array's own cell width (8 / 8 / 16 bytes) and crosses the fold
// callback BY VALUE (Il2CppDateTime / Il2CppTimeSpan / Il2CppGuid); the 16-byte
// decimal result keeps the T3 contract (explicit result slot, seed by pointer).
// A user-struct source has no cell family and stays a transpile-time LOUD.
void il2cpp_linq_aggregate_seed_dt_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                       const runtime::Il2CppDecimal* seed, Il2CppObject* func);
void il2cpp_linq_aggregate_seed_ts_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                       const runtime::Il2CppDecimal* seed, Il2CppObject* func);
void il2cpp_linq_aggregate_seed_guid_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                         const runtime::Il2CppDecimal* seed, Il2CppObject* func);
int32_t il2cpp_linq_aggregate_seed_dt_dec_rs_i4(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_dt_dec_rs_i8(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_dt_dec_rs_f(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_dt_dec_rs_d(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_dt_dec_rs_ref(Il2CppArray* src,
                                                       const runtime::Il2CppDecimal* seed,
                                                       Il2CppObject* func, Il2CppObject* resSel);
void il2cpp_linq_aggregate_seed_dt_dec_rs_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                              const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
int32_t il2cpp_linq_aggregate_seed_ts_dec_rs_i4(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_ts_dec_rs_i8(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_ts_dec_rs_f(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_ts_dec_rs_d(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_ts_dec_rs_ref(Il2CppArray* src,
                                                       const runtime::Il2CppDecimal* seed,
                                                       Il2CppObject* func, Il2CppObject* resSel);
void il2cpp_linq_aggregate_seed_ts_dec_rs_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                              const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, Il2CppObject* resSel);
int32_t il2cpp_linq_aggregate_seed_guid_dec_rs_i4(Il2CppArray* src,
                                                  const runtime::Il2CppDecimal* seed,
                                                  Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_guid_dec_rs_i8(Il2CppArray* src,
                                                  const runtime::Il2CppDecimal* seed,
                                                  Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_guid_dec_rs_f(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
double il2cpp_linq_aggregate_seed_guid_dec_rs_d(Il2CppArray* src, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_guid_dec_rs_ref(Il2CppArray* src,
                                                         const runtime::Il2CppDecimal* seed,
                                                         Il2CppObject* func, Il2CppObject* resSel);
void il2cpp_linq_aggregate_seed_guid_dec_rs_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                                const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, Il2CppObject* resSel);

//------------------------------------------------------------------------------
// P0LINQ1 (D3) -- Select with TResult = System.Decimal. net48's Select is
// generic in TResult and decimal is a 16-byte value type, so the selector is
// invoked through a RESULT-SLOT ABI:
//   void (*)(Il2CppDecimal* out, TSource value)                    (static leaf)
//   void (*)(Il2CppDecimal* out, Il2CppObject* this, TSource value) (bound leaf)
// The source family picks the entry (each validates its own cell layout):
//   _i4  int32[]   _i8  int64[]   _d  double[]   _f  float[]   _dec decimal[]
// Returns a FRESH array of 16-byte decimal cells (elemSize 16, non-reference)
// of the source length. The selector runs EXACTLY ONCE per element.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_i4_dec(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_i8_dec(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_d_dec(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_f_dec(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_dec_dec(Il2CppArray* src, Il2CppObject* sel);

// The DECIMAL-source Select with a VALUE result (net48 Select<decimal,TResult>
// with a non-decimal TResult): int32 / int64 / double / float. The element
// crosses as a 16-byte decimal cell BY VALUE.
Il2CppArray* il2cpp_linq_select_dec_i4(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_dec_i8(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_dec_d(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_dec_f(Il2CppArray* src, Il2CppObject* sel);
// p0grp: Select over a DECIMAL source with a REFERENCE result (net48
// Select<decimal,TResult> with a reference TResult). The selector returns
// Il2CppObject*; the result is a fresh reference array.
Il2CppArray* il2cpp_linq_select_dec_ref(Il2CppArray* src, Il2CppObject* sel);

// p0grp: Where over a DECIMAL source (net48 Where<decimal>(source,
// Func<decimal,bool>)). The predicate takes the decimal BY VALUE; the result is
// a fresh decimal[] with the matching elements.
Il2CppArray* il2cpp_linq_where_dec(Il2CppArray* src, Il2CppObject* pred);
// b5-gaps section 90: the DECIMAL-source INDEX predicate (net48
// Where<decimal>(source, Func<decimal,int,bool>)) -- the counterpart of the
// dt/ts/guid/dtoffset where_idx_<fam> entries; the predicate takes the decimal
// BY VALUE plus the zero-based source index; a fresh decimal[] of the matches.
Il2CppArray* il2cpp_linq_where_idx_dec(Il2CppArray* src, Il2CppObject* pred);

// p0grp: the DECIMAL-source element operators (net48 First / Last / ElementAt /
// Contains / Reverse / Take / Skip). The element-returning entries write the
// decimal through an explicit result slot (the decimal T3 contract); the range
// entries materialize a fresh decimal[].
void          il2cpp_linq_first_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void          il2cpp_linq_last_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void          il2cpp_linq_element_at_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, int32_t index);
bool          il2cpp_linq_contains_dec(Il2CppArray* src, runtime::Il2CppDecimal value);
Il2CppArray*  il2cpp_linq_reverse_dec(Il2CppArray* src);
Il2CppArray*  il2cpp_linq_take_dec(Il2CppArray* src, int32_t count);
Il2CppArray*  il2cpp_linq_skip_dec(Il2CppArray* src, int32_t count);

// p0grp: the DECIMAL-source set / concat / equality operators (net48 Distinct /
// Except / Concat / DefaultIfEmpty / SequenceEqual).
Il2CppArray*  il2cpp_linq_distinct_dec(Il2CppArray* src);
Il2CppArray*  il2cpp_linq_except_dec(Il2CppArray* src, Il2CppArray* second);
Il2CppArray*  il2cpp_linq_concat_dec(Il2CppArray* src, Il2CppArray* second);
Il2CppArray*  il2cpp_linq_default_if_empty_dec(Il2CppArray* src, runtime::Il2CppDecimal defaultValue);
bool          il2cpp_linq_sequence_equal_dec(Il2CppArray* src, Il2CppArray* second);

// p0linq4 (D3) follow-up: the INT32-source Select with a DateTime / TimeSpan /
// Guid result (net48 Select<int,TResult>). The selector returns the struct BY
// VALUE and the output cell carries the raw struct image.
Il2CppArray* il2cpp_linq_select_i4_dt(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_i4_ts(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_i4_guid(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_dt(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_ts(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_guid(Il2CppArray* src, Il2CppObject* sel);

// The DECIMAL-source INDEX Select (net48 Select<decimal,int,TResult>): the
// selector receives the element AND its 0-based source index. TResult is int32 /
// int64 / double / float / decimal.
Il2CppArray* il2cpp_linq_select_idx_dec_i4(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dec_i8(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dec_d(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dec_f(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dec_dec(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dec_ref(Il2CppArray* src, Il2CppObject* sel);

// p0linq4 (D2): Select with an (element, index) selector returning decimal --
// the Func<TSource,int,decimal> overload over an int32 source. Same 16-byte
// result-slot contract; the selector is invoked EXACTLY ONCE per element with
// the element AND its 0-based SOURCE index:
//   Il2CppDecimal (*)(int32_t value, int32_t index)                     (static)
//   Il2CppDecimal (*)(Il2CppObject* this, int32_t value, int32_t index) (bound)
Il2CppArray* il2cpp_linq_select_idx_i4_dec(Il2CppArray* src, Il2CppObject* sel);

// p0linq4 (D3): Select over a REFERENCE-element source returning decimal --
// the ref -> decimal counterpart of il2cpp_linq_select_ref_ref. The element
// crosses as Il2CppObject*:
//   Il2CppDecimal (*)(Il2CppObject* elem)                            (static)
//   Il2CppDecimal (*)(Il2CppObject* this, Il2CppObject* elem)        (bound)
Il2CppArray* il2cpp_linq_select_ref_dec(Il2CppArray* src, Il2CppObject* sel);

// p0linq4 (D3) follow-up: Select over a REFERENCE-element source returning
// DateTime / TimeSpan / Guid -- the remaining 8/16-byte value results. The
// element crosses as Il2CppObject* and the result struct rides the return
// slot (the same by-value convention as the decimal selector above):
//   Il2CppDateTime (*)(Il2CppObject* elem)                        (static)
//   Il2CppDateTime (*)(Il2CppObject* this, Il2CppObject* elem)    (bound)
Il2CppArray* il2cpp_linq_select_ref_dt(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ref_ts(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ref_guid(Il2CppArray* src, Il2CppObject* sel);

// delta 2: Select<TResult = System.DateTimeOffset>. The 16-byte value result
// has its own selector ABI (the generated leaf returns Il2CppDateTimeOffset BY
// VALUE) and materializes a fresh 16-byte-element DateTimeOffset[] (non-ref;
// ldelem/stelem of that array is a separate wiring item). One entry per
// SOURCE family, plus an (element,index) selector form:
//   i4 / i8 / f / d / ref / dt / ts / guid / dtoffset sources
Il2CppArray* il2cpp_linq_select_i4_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_i8_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i8_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_f_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_f_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_d_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_d_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ref_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_dt_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dt_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ts_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ts_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_guid_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_guid_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_dto_dtoffset(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_dto_dtoffset(Il2CppArray* src, Il2CppObject* sel);

// impl-vtsel: the STRUCT-TYPED SELECTOR ABI (net48 Select<TSource,TResult>),
// family-generic in BOTH type arguments. `srcKind` is the IL2CPP_LIST_KIND_*
// code of the source cell (0 int32, 1 ref, 2 int64, 3 float, 4 double,
// 13 DateTime, 14 TimeSpan, 15 Guid, 16 DateTimeOffset) and `resKind` is the
// result family:
//   0 int32 | 1 reference | 2 int64 | 3 float (double carrier) | 4 double |
//   5 DateTime | 6 TimeSpan | 7 Guid | 8 DateTimeOffset
// The selector leaf is called through its OWN by-value parameter and return
// slots -- never the int32 ABI. The result is a typed array (DateTime/TimeSpan
// carry their element kind; Guid/DateTimeOffset are 16-byte value cells).
Il2CppArray* il2cpp_linq_select_family(Il2CppArray* src, Il2CppObject* sel,
                                       int32_t srcKind, int32_t resKind);
Il2CppArray* il2cpp_linq_select_family_idx(Il2CppArray* src, Il2CppObject* sel,
                                           int32_t srcKind, int32_t resKind);

// The reference-source Select with a VALUE result (net48
// Select<TSource,TResult> with a non-reference TResult over a reference source):
// int32 / int64 / double / float. The result array carries the matching
// elemKind.
Il2CppArray* il2cpp_linq_select_ref_i4(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ref_i8(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ref_d(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_ref_f(Il2CppArray* src, Il2CppObject* sel);

// The reference-source INDEX Select with a VALUE result (net48
// Select<TSource,int,TResult>): the selector receives the element AND its
// zero-based source index.
Il2CppArray* il2cpp_linq_select_idx_ref_i4(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_i8(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_d(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_f(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_dt(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_ts(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_guid(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_ref_dec(Il2CppArray* src, Il2CppObject* sel);

//------------------------------------------------------------------------------
// LINQ GROUPING domain (D-LINQ-4) -- Enumerable.GroupBy, int32 family.
//
// Eager materialization (the stage-1 AOT form): GroupBy walks the source
// ONCE, runs the key selector EXACTLY ONCE per element (net48 GroupBy caches
// each element's key on first enumeration -- a second pass would re-run a
// side-effecting selector, an observable deviation), and buckets the elements
// by the int32 key. The buckets appear in FIRST-APPEARANCE order of the
// distinct keys (net48 Lookup semantics, probe-verified).
//
// Managed object model (GC-bitmap conventions, listi4.h precedent):
//
//   struct Il2CppLinqGrouping         // one bucket (an IGrouping<int,int>)
//   {
//       Il2CppObject obj;      // object header: klass pointer
//       int64_t      key;      // bucket key: low 64 bits (NOT a GC reference)
//       Il2CppArray* elements; // INT32 value array: slot i = bucket element i
//       int32_t      count;    // number of elements in the bucket
//       int64_t      keyHi;    // bucket key: high 64 bits (16-byte struct keys)
//   };
//   // instanceSize 40, refFieldBitmap bit 2 (elements)
//
//   struct Il2CppLinqGroupingResult    // the GroupBy() return value
//   {
//       Il2CppObject obj;      // object header
//       Il2CppArray* groups;   // REFERENCE array of Il2CppLinqGrouping*
//                              //   (bucket objects, first-appearance order)
//       int32_t      count;    // number of buckets (distinct keys)
//   };
//   // instanceSize 24, refFieldBitmap bit 1 (groups)
//
// The transpiler wires GroupBy(source, keySelector) -> il2cpp_linq_group_by_i4
// and per-bucket Sum on an IGrouping receiver -> il2cpp_linq_grouping_sum
// (MethodMap.Linq.cs). The result's iteration contract is exposed through
// the *_result_get_* / *_grouping_get_* entries below; the stage-1
// transpiler has no grouping-enumerator wiring (TryMapLinqGrouping is not
// emitted), so the runtime unit test drives Count/Sum/order directly.
//------------------------------------------------------------------------------
// Managed bucket (an IGrouping<int,int>): key + element int32[].
//
// The key is a 16-byte carrier split across two 8-byte slots so ONE struct
// serves every key family the grouping surface buckets by:
//   * int32-slot keys (int/bool/char/sbyte/byte/short/ushort/uint): key holds
//     the narrowed int32 (zero/sign-extended), keyHi is 0;
//   * int64 keys (long/ulong): key/keyHi hold the 64-bit value (ulong rides the
//     same bit pattern);
//   * 16-byte struct keys (System.Decimal / System.DateTime / System.Guid):
//     the raw 16 bytes are split low64 -> key, high64 -> keyHi.
// The elements/count offsets are UNCHANGED from the original 32-byte layout
// (elements at slot 2 = bitmap bit 2, count at slot 3), so the shared
// IGrouping readers (get_elements / get_count / get_enumerator) keep working
// for every key family; only the key accessors are family-specific.
struct Il2CppLinqGrouping
{
    Il2CppObject obj;       // object header (klass pointer)
    int64_t      key;       // bucket key: low 64 bits (int32-slot keys ride here)
    Il2CppArray* elements;  // INT32 value array: slot i = bucket element i
    int32_t      count;     // number of elements in the bucket
    int64_t      keyHi;     // bucket key: high 64 bits (0 for int32/int64 keys)
    // p0grp: a REFERENCE bucket key (System.String / class / object) -- a GC
    // reference (bitmap bit 5). 0 for every value key family.
    Il2CppObject* keyRef;
};

// Managed GroupBy() result: the buckets in first-appearance key order.
struct Il2CppLinqGroupingResult
{
    Il2CppObject obj;       // object header (klass pointer)
    Il2CppArray* groups;    // REFERENCE array of Il2CppLinqGrouping*
    int32_t      count;     // number of buckets (distinct keys)
};

// The managed descriptors (registered lazily on first use; create the
// managed objects through the icalls below).
Il2CppClass* il2cpp_linq_grouping_class();
Il2CppClass* il2cpp_linq_grouping_result_class();

// GroupBy(source, keySelector) -- bucket the int32 source by the key the
// selector computes per element (int32 -> int32; instance-bound selectors
// dispatch per binding like the predicate/selector families above). Returns
// a NEW managed grouping-result object whose buckets appear in
// FIRST-APPEARANCE order of the distinct keys; each bucket is a grouping
// object (key + element int32[]).
//   src     : source array (null -> ArgumentNullException; ref elements ->
//             NotSupportedException)
//   keySel  : delegate wrapping int32(*)(int32_t) (validation per the
//             callback contract: null -> ArgumentNullException)
Il2CppObject* il2cpp_linq_group_by_i4(Il2CppArray* src, Il2CppObject* keySel);

// GroupBy(source, keySelector, elementSelector) -- the 2-delegate overload
// (net48 Func<TSource,TKey> + Func<TSource,TElement>). Each element is
// projected through elementSelector BEFORE it is appended to its bucket, so
// the bucket's element array holds the projected values; the key selector
// runs once per source element and the element selector runs once per source
// element (net48 GroupBy semantics: both run exactly once per element).
//   src     : source array (null -> ArgumentNullException; ref elements ->
//             NotSupportedException)
//   keySel  : delegate wrapping int32(*)(int32_t)
//   elemSel : delegate wrapping int32(*)(int32_t) (projected element)
Il2CppObject* il2cpp_linq_group_by_elem_i4(Il2CppArray* src, Il2CppObject* keySel, Il2CppObject* elemSel);

//------------------------------------------------------------------------------
// nb9 FIX BATCH -- the remaining GroupBy overloads (no LOUD, no mis-wiring).
//
// net48's GroupBy family:
//   GroupBy(keySelector)
//   GroupBy(keySelector, elementSelector)
//   GroupBy(keySelector, comparer)
//   GroupBy(keySelector, resultSelector)
//   GroupBy(keySelector, elementSelector, comparer)
//   GroupBy(keySelector, elementSelector, resultSelector)
//   GroupBy(keySelector, elementSelector, comparer, resultSelector)
//
// ONE entry serves every shape: the transpiler passes nullptr for the absent
// delegates plus the two family codes as trailing literals:
//   elemKind : 0 int32 | 1 reference | 2 int64 (boxed) | 3 double (boxed)
//   rsKind   : 0 no result selector | 1 int32 TResult | 2 reference TResult |
//              3 int64 TResult | 4 double TResult | 5 float TResult (nb9 H-F4)
// The RESULT selector (`Func<TKey,IEnumerable<TElement>,TResult>`) receives
// (int32 key, Il2CppObject* bucketElements) -- the SECOND argument is the
// bucket's exact-size element ARRAY (an IEnumerable<TElement> carrier with the
// same contents as net48's IGrouping), so a `vals.Count()`/`Sum()` inside the
// selector resolves on the array surface.
//
// Return: the grouping-result object (rsKind == 0) or the materialized result
// array (rsKind != 0).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_full(Il2CppArray* src, Il2CppObject* keySel,
                                        Il2CppObject* elemSel, Il2CppObject* cmp,
                                        Il2CppObject* resSel, int32_t elemKind, int32_t rsKind);

//------------------------------------------------------------------------------
// B4-1 (P0-04): the integer KEY FAMILY of the same entry.
//
// The bucket key is an int32 slot, but a NARROW key selector (`x => (byte)x`)
// returns its value in a register whose upper bits are NOT defined by the C++
// return type (uint8_t/int8_t/uint16_t/int16_t) while the callback ABI reads
// the result as int32_t. Reading that register raw produced a key such as 300
// where net48's key is 44 -- a WRONG bucket (split/merged groups), not a
// display artifact. keyKind makes the runtime NARROW the callback result back
// to the declared TKey width before bucketing:
//   0 int32 (identity) | 1 uint8 | 2 int8 | 3 uint16 | 4 int16 |
//   5 uint32 (bit pattern, equality only) | 6 bool (0/1)
// The element/result families and every other argument are identical to
// il2cpp_linq_group_by_full (which forwards keyKind = 0).
//
// vtsrc: `srcKind` names the SOURCE element family (the array image alone
// cannot tell a Guid[] from a decimal[] -- both are 16-byte non-reference):
//   0 int32 | 1 reference | 2 decimal | 3 DateTime | 4 Guid
// impl-groupbykey adds the WIDE / NARROW source families:
//   5 int64/uint64 | 6 double | 7 narrow (1/2/4-byte; the cell code rides
//   il2cpp_linq_group_by_full_key_cell's trailing srcCell)
// The TElement family code covers 0 int32 | 1 reference | 2 int64 | 3 double |
// 4 decimal | 5 DateTime | 6 Guid.
// The KEY family (KeyKindFor) covers 0..6 int32-slot, 7 int64/uint64, 8
// System.Decimal, 9 System.DateTime, 10 System.Guid, 11 reference,
// 12 System.TimeSpan, 13 System.DateTimeOffset, 14 user enum (int32 value__).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_full_key(Il2CppArray* src, Il2CppObject* keySel,
                                            Il2CppObject* elemSel, Il2CppObject* cmp,
                                            Il2CppObject* resSel, int32_t elemKind,
                                            int32_t rsKind, int32_t keyKind,
                                            int32_t srcKind = 0);

// impl-groupbykey: the NARROW-source form of the entry above. srcKind 7 needs
// the SOURCE CELL CODE (0 sbyte | 1 byte | 2 bool | 3 short | 4 ushort |
// 5 char | 6 uint -- the transpiler's LinqNarrowCode) so the kernel reads the
// element at its real width before widening it into the int32 selector slot.
// Every other srcKind ignores srcCell.
Il2CppObject* il2cpp_linq_group_by_full_key_cell(Il2CppArray* src, Il2CppObject* keySel,
                                                 Il2CppObject* elemSel, Il2CppObject* cmp,
                                                 Il2CppObject* resSel, int32_t elemKind,
                                                 int32_t rsKind, int32_t keyKind,
                                                 int32_t srcKind, int32_t srcCell);

//------------------------------------------------------------------------------
// p0grp -- GroupBy over a DECIMAL-element ARRAY source (int32-slot key).
//
// The pre-existing entries read their source through I4At (an int32 4-byte
// cell); a System.Decimal[] source is a 16-byte value array, so it needs its
// own reader. This entry is the minimal closed loop for
// `decimal[].GroupBy(x => (int)x)`:
//
//   * the source is validated with RequireDecimalFamily (16-byte cells, not a
//     reference array);
//   * the key selector receives the decimal element BY VALUE -- the generated
//     leaf ABI is int32_t (*)(Il2CppDecimal) / int32_t (*)(Il2CppObject*,
//     Il2CppDecimal) (the struct rides the by-value return/parameter path,
//     exact_decimal_select precedent);
//   * the bucket key is the int32 slot, narrowed by keyKind (the same
//     KeyKindFor codes as il2cpp_linq_group_by_full_key);
//   * each bucket stores its elements as BOXED System.Decimal references
//     (icall_box_decimal) in a reference array, so the shared IGrouping
//     reader/enumerator surface works unchanged: foreach over the bucket
//     yields the boxed decimal, and `(decimal)o` unboxes it through
//     icall_unbox_decimal.
//
// NOT supported here (transpile-time LOUD, see docs/audit/b4-gaps.md): an
// element selector / comparer / result selector over a decimal source (their
// callbacks would need additional by-value struct ABIs), and non-int32-slot
// keys (long/ulong/decimal/DateTime/Guid).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_dec_key(Il2CppArray* src, Il2CppObject* keySel,
                                           int32_t keyKind);

//------------------------------------------------------------------------------
// nb9 FIX BATCH -- Enumerable operators over an IGrouping bucket / the whole
// GroupBy result. Each entry materializes the grouping's EXACT-SIZE element
// snapshot (il2cpp_linq_grouping_get_elements) and then reuses the int32 array
// kernel, so the semantics are identical to the array surface.
//------------------------------------------------------------------------------

// nb9 FIX (M21): the SELECTOR forms of the int32 aggregates. net48
// Sum(source, Func<TSource,int>) / Min / Max / Average project EVERY element
// through the selector FIRST and aggregate the projected int32 values (Min/Max
// over an empty source still throw InvalidOperationException; Sum -> 0;
// Average -> InvalidOperationException). The old mapping dropped the selector
// and returned the selector-less aggregate (a silent wrong value).
int32_t il2cpp_linq_sum_sel_i4(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_i4(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_i4(Il2CppArray* src, Il2CppObject* sel);
// loud-p0-probe: reference-source, int32-result selector aggregates
// (Func<string,char/int32>).
int32_t il2cpp_linq_min_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i4(Il2CppArray* src, Il2CppObject* sel);
// The reference-source selector-aggregate family with a WIDE result slot:
// Sum/Average over Func<string,int32> and Sum/Min/Max/Average over
// Func<string,long> / Func<string,double> / Func<string,float>. net48 has
// every one of these overloads (Sum/Average return the projected type, Min/Max
// throw on an empty source).
int32_t il2cpp_linq_sum_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_ref_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_ref_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_ref_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_ref_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_ref_f(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_ref_f(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_ref_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_ref_f(Il2CppArray* src, Il2CppObject* sel);

// The WIDE-source selector-aggregate family (net48
// Sum/Min/Max/Average<TSource>(Func<TSource,TResult>) with TSource = long /
// double / float). The element crosses in its own slot (int64 for long, the
// F-stack carrier for float/double); the result crosses in the int32 / int64 /
// F-stack slot. Sum returns the identity for an empty source; Min/Max/Average
// throw InvalidOperationException on an empty source.
int32_t il2cpp_linq_sum_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel);

//------------------------------------------------------------------------------
// impl-linq3 (A): the NARROW-source selector-aggregate family (net48
// Sum/Min/Max/Average<TSource,TResult> with TSource = sbyte / byte / short /
// ushort / uint / char). The element cell is read at its real width and
// widened to int32 (the arrayext2 narrow value model), so the selector ABI is
// the int32 one (InvokeSelI4*). The fold semantics follow the dec-projection
// aggregates: the int32/int64 sums are CHECKED, the float sum/average
// accumulate in double and cast at the end, Min/Max/Average throw
// InvalidOperationException on an empty source, Sum returns the identity.
//   _i1src sbyte | _u1src byte | _i2src short | _u2src ushort
//   _u4src uint  | _csrc  char
//------------------------------------------------------------------------------
int32_t il2cpp_linq_sum_sel_i1src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_i1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_i1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_i1src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_i1src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_i1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_i1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_i1src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_i1src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_i1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_i1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_i1src_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i1src_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_i1src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_u1src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_u1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_u1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_u1src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_u1src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_u1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_u1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_u1src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_u1src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_u1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_u1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_u1src_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u1src_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u1src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u1src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_u1src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_i2src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_i2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_i2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_i2src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_i2src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_i2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_i2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_i2src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_i2src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_i2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_i2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_i2src_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i2src_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_i2src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_u2src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_u2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_u2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_u2src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_u2src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_u2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_u2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_u2src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_u2src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_u2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_u2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_u2src_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u2src_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u2src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u2src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_u2src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_u4src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_u4src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_u4src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_u4src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_u4src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_u4src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_u4src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_u4src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_u4src_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_u4src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_u4src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_u4src_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u4src_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u4src_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_u4src_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_u4src_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_sum_sel_csrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_csrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_csrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_csrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_csrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_csrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_csrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_csrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_csrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_csrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_csrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_csrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_csrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_csrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_csrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_csrc_f(Il2CppArray* src, Il2CppObject* sel);

// impl-linq-vtagg (§52): the selector aggregates over the BY-VALUE value-struct
// SOURCES (System.DateTime / System.TimeSpan / System.Guid /
// System.DateTimeOffset). The selector takes the element by value through the
// family's own struct ABI; the projection crosses in its own slot (the decimal
// projection rides the result-slot-first contract). The source cell family is
// checked at entry.
int32_t il2cpp_linq_sum_sel_dtsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_dtsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_dtsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_dtsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_dtsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_dtsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_dtsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_dtsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_dtsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_dtsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_dtsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_dtsrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtsrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtsrc_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtsrc_f(Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_sum_sel_dtsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_min_sel_dtsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_max_sel_dtsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_average_sel_dtsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);

int32_t il2cpp_linq_sum_sel_tssrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_tssrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_tssrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_tssrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_tssrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_tssrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_tssrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_tssrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_tssrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_tssrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_tssrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_tssrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_tssrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_tssrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_tssrc_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_tssrc_f(Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_sum_sel_tssrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_min_sel_tssrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_max_sel_tssrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_average_sel_tssrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);

int32_t il2cpp_linq_sum_sel_guidsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_guidsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_guidsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_guidsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_guidsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_guidsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_guidsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_guidsrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_guidsrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_guidsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_guidsrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_guidsrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_guidsrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_guidsrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_guidsrc_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_guidsrc_f(Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_sum_sel_guidsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_min_sel_guidsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_max_sel_guidsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_average_sel_guidsrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);

int32_t il2cpp_linq_sum_sel_dtosrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_sum_sel_dtosrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_dtosrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_sum_sel_dtosrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_min_sel_dtosrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_dtosrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_dtosrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_dtosrc_f(Il2CppArray* src, Il2CppObject* sel);
int32_t il2cpp_linq_max_sel_dtosrc_i4(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_dtosrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_dtosrc_d(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_dtosrc_f(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtosrc_i4(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtosrc_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtosrc_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_dtosrc_f(Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_sum_sel_dtosrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_min_sel_dtosrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_max_sel_dtosrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);
void il2cpp_linq_average_sel_dtosrc_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel);

// The same selector forms for the int64 / double projection families
// (Func<TSource,long> / Func<TSource,double>). net48 has these overloads, so
// the transpiler must not silently truncate them into the int32 family.
int64_t il2cpp_linq_sum_sel_i8(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_min_sel_i8(Il2CppArray* src, Il2CppObject* sel);
int64_t il2cpp_linq_max_sel_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_i8(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_sum_sel_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_min_sel_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_max_sel_d(Il2CppArray* src, Il2CppObject* sel);
double  il2cpp_linq_average_sel_d(Il2CppArray* src, Il2CppObject* sel);
// nb9 fix P0-1: the float selector projection (Func<TSource,float>). The
// elements materialize as double[]; the result carrier stays float.
Il2CppArray* il2cpp_linq_select_i4_f(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray* il2cpp_linq_select_idx_i4_f(Il2CppArray* src, Il2CppObject* sel);

// Select(source, (element, index) => TResult) over a REFERENCE-element source
// returning a REFERENCE result (net48 Func<TSource,int,TResult>) -- the
// reference counterpart of il2cpp_linq_select_idx_i4_ref. The selector runs
// EXACTLY ONCE per element with the element AND its 0-based SOURCE index; the
// result is a fresh reference-element array of the SAME length as the source.
// The selector ABI is Il2CppObject*(*)(Il2CppObject*, int32_t) for a STATIC
// leaf / Il2CppObject*(*)(Il2CppObject*, Il2CppObject*, int32_t) for an
// INSTANCE leaf (target in the `this` slot).
Il2CppArray* il2cpp_linq_select_idx_ref_ref(Il2CppArray* src, Il2CppObject* sel);

//------------------------------------------------------------------------------
// Single / SingleOrDefault WITH a predicate (net48 Func<TSource,bool>).
//
// The source-only forms (il2cpp_linq_single_fam_* / *_single_or_default_fam_*,
// linq_family.h) answer on the SOURCE length alone. The predicate forms walk
// every element and count the matches (net48 does NOT short-circuit -- a second
// match must remain detectable even when it appears early, so the predicate
// runs on EVERY element):
//   * Single(predicate)          -- 0 matches -> InvalidOperationException;
//                                  >1 -> InvalidOperationException; 1 -> it.
//   * SingleOrDefault(predicate) -- 0 -> default(T); >1 -> InvalidOperationException.
// The int32 family returns the widened value; the reference family returns the
// matching reference (nullptr for the empty SingleOrDefault). Both validate the
// source (null -> ArgumentNullException) and the predicate (null ->
// ArgumentNullException; multicast -> InvalidOperationException).
//------------------------------------------------------------------------------
int32_t       il2cpp_linq_single_pred_i4(Il2CppArray* src, Il2CppObject* pred);
int32_t       il2cpp_linq_single_or_default_pred_i4(Il2CppArray* src, Il2CppObject* pred);
Il2CppObject* il2cpp_linq_single_pred_ref(Il2CppArray* src, Il2CppObject* pred);
Il2CppObject* il2cpp_linq_single_or_default_pred_ref(Il2CppArray* src, Il2CppObject* pred);
float   il2cpp_linq_sum_sel_f(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_min_sel_f(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_max_sel_f(Il2CppArray* src, Il2CppObject* sel);
float   il2cpp_linq_average_sel_f(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_where(Il2CppObject* grouping, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_grouping_select(Il2CppObject* grouping, Il2CppObject* sel);
int32_t       il2cpp_linq_grouping_count_pred(Il2CppObject* grouping, Il2CppObject* pred);
int32_t       il2cpp_linq_grouping_sum_sel(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_average_i4(Il2CppObject* grouping);
double        il2cpp_linq_grouping_average_sel(Il2CppObject* grouping, Il2CppObject* sel);
int32_t       il2cpp_linq_grouping_min_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_max_i4(Il2CppObject* grouping);
bool          il2cpp_linq_grouping_any(Il2CppObject* grouping);
bool          il2cpp_linq_grouping_any_pred(Il2CppObject* grouping, Il2CppObject* pred);
// b5-gaps §55: the OBJECT-dispatch no-arg Count/Any. Used when the receiver's
// tracked type is System.Object (a GroupBy bucket/result element inside a
// lambda); the runtime dispatches by the object's class name.
int32_t       il2cpp_linq_count_object(Il2CppObject* obj);
bool          il2cpp_linq_any_object(Il2CppObject* obj);
bool          il2cpp_linq_grouping_all(Il2CppObject* grouping, Il2CppObject* pred);
int32_t       il2cpp_linq_grouping_first_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_first_or_default_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_last_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_element_at_i4(Il2CppObject* grouping, int32_t index);
bool          il2cpp_linq_grouping_contains_i4(Il2CppObject* grouping, int32_t value);
// b5-gaps section 93: the bucket's Single / SingleOrDefault / Aggregate. The
// bucket switch had no arm for them, so the generic element block cast the
// GROUPING OBJECT to Il2CppArray* (Single: a loud element-family mismatch) or
// read the bucket's IGrouping* cells through the int32 kernels (Aggregate: a
// silent wrong value). Each wrapper extracts the bucket's exact-size int32
// element snapshot first, then runs the family kernel over it.
int32_t       il2cpp_linq_grouping_single_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_single_or_default_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_aggregate_i4(Il2CppObject* grouping, Il2CppObject* func);
Il2CppArray*  il2cpp_linq_grouping_to_array(Il2CppObject* grouping);
// b5-gaps section 98: ToList over ONE GroupBy bucket. The bucket's ToList had
// no arm, so it fell through to the generic ToList arm and bound
// il2cpp_linq_to_list -- which casts the GROUPING OBJECT to Il2CppArray* and
// dies with "Object is not an array.". The wrapper snapshots the bucket
// (get_elements, an exact-size array at the bucket's own element width) and
// materializes the list in the bucket's OWN family (its recorded elemKind).
Il2CppObject* il2cpp_linq_grouping_to_list(Il2CppObject* grouping);

// floatgrp: the System.Single bucket operator surface. The bucket snapshot is
// a REAL float[] (4-byte kElemKindF cells), so each entry reuses the float
// ARRAY kernel; the F-stack carrier means a float leaf crosses as a double.
Il2CppArray*  il2cpp_linq_grouping_where_f(Il2CppObject* grouping, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_grouping_select_f(Il2CppObject* grouping, Il2CppObject* sel, int32_t resKind);
int32_t       il2cpp_linq_grouping_count_pred_f(Il2CppObject* grouping, Il2CppObject* pred);
bool          il2cpp_linq_grouping_any_pred_f(Il2CppObject* grouping, Il2CppObject* pred);
bool          il2cpp_linq_grouping_all_f(Il2CppObject* grouping, Il2CppObject* pred);
double        il2cpp_linq_grouping_first_f(Il2CppObject* grouping);
double        il2cpp_linq_grouping_first_or_default_f(Il2CppObject* grouping);
double        il2cpp_linq_grouping_last_f(Il2CppObject* grouping);
double        il2cpp_linq_grouping_last_or_default_f(Il2CppObject* grouping);
float         il2cpp_linq_grouping_sum_f(Il2CppObject* grouping);
float         il2cpp_linq_grouping_min_f(Il2CppObject* grouping);
float         il2cpp_linq_grouping_max_f(Il2CppObject* grouping);
float         il2cpp_linq_grouping_average_f(Il2CppObject* grouping);
// b5-gaps section 97: the float bucket's Single / SingleOrDefault / Aggregate /
// ElementAt / Contains -- the bucket switch's float arms ended at Average, so
// these five fell through to the !valueBucket (int32) arms: ElementAt and
// Aggregate read the 4-byte float cells as int32 (a silent wrong value) and
// Single / Contains hit the int32-family guard (a loud abort). Each wrapper
// snapshots the bucket to its REAL float[] (RequireFGroupElements) and reuses
// the float array kernel; the F-stack carrier means a float leaf crosses as a
// double.
double        il2cpp_linq_grouping_single_f(Il2CppObject* grouping);
double        il2cpp_linq_grouping_single_or_default_f(Il2CppObject* grouping);
float         il2cpp_linq_grouping_aggregate_f(Il2CppObject* grouping, Il2CppObject* func);
double        il2cpp_linq_grouping_element_at_f(Il2CppObject* grouping, int32_t index);
bool          il2cpp_linq_grouping_contains_f(Il2CppObject* grouping, double value);

// b5-gaps section 100: the float bucket's OrderBy / OrderByDescending. The
// bucket switch's float arms ended at Contains, so OrderBy fell through to the
// floatBucket LOUD gate (an honest rejection). The wrapper snapshots the bucket
// to its REAL float[] (RequireFGroupElements) and reuses the float-SOURCE
// keyed-sort kernel (il2cpp_linq_order_by_key[_desc]_f_<keyfam>); the result is
// a fresh float[] stamped kElemKindF.
Il2CppArray*  il2cpp_linq_grouping_order_by_key_f_i4(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_f_i8(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_f_d(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_f_f(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_f_i4(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_f_i8(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_f_d(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_f_f(Il2CppObject* grouping, Il2CppObject* keySel);

// b5-gaps section 101: Append / Prepend over a GroupBy bucket. The bucket
// switch had no arm, so the call was LOUD (the floatBucket / valueBucket gates)
// or fell through to the array kernels and cast the GROUPING OBJECT to
// Il2CppArray* (a loud element-family mismatch). The wrapper snapshots the
// bucket to its REAL element array (RequireI4GroupElements /
// RequireFGroupElements) or reads its reference elements directly, and reuses
// the array append/prepend kernel.
Il2CppArray*  il2cpp_linq_grouping_append_i4(Il2CppObject* grouping, int32_t element);
Il2CppArray*  il2cpp_linq_grouping_prepend_i4(Il2CppObject* grouping, int32_t element);
Il2CppArray*  il2cpp_linq_grouping_append_d(Il2CppObject* grouping, double element);
Il2CppArray*  il2cpp_linq_grouping_prepend_d(Il2CppObject* grouping, double element);
Il2CppArray*  il2cpp_linq_grouping_append_ref(Il2CppObject* grouping, Il2CppObject* element);
Il2CppArray*  il2cpp_linq_grouping_prepend_ref(Il2CppObject* grouping, Il2CppObject* element);

// BATCH4: the DECIMAL-bucket operators (a decimal-source bucket stores 16-byte
// System.Decimal cells). The aggregate results ride the decimal T3 result-slot
// ABI (the result slot is the FIRST C++ parameter); Contains takes the value by
// const reference (the decimal equality kernel).
void          il2cpp_linq_grouping_sum_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_min_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_max_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_average_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
// P0LINQ1 follow-up: the decimal-bucket SELECTOR aggregates (net48
// Sum/Min/Max/Average<decimal>(bucket, Func<decimal,decimal>)). The selector
// returns the decimal BY VALUE; the fold runs over the projected values.
void          il2cpp_linq_grouping_sum_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* sel);
void          il2cpp_linq_grouping_min_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* sel);
void          il2cpp_linq_grouping_max_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* sel);
void          il2cpp_linq_grouping_average_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* sel);
// impl-linq2 (B-2): the decimal-bucket PROJECTION aggregates (net48
// Sum/Min/Max/Average<decimal>(bucket, Func<decimal,int32/int64/float/double>)
// -- Enumerable.cs:1496-1536/1703-1739/1907-1943/2101-2137 generic forms). The
// bucket's decimal cells feed the selector and the fold runs over the
// PROJECTED values (int32/int64 sums CHECKED, net48 semantics).
int32_t       il2cpp_linq_grouping_sum_dec_proj_i4(Il2CppObject* grouping, Il2CppObject* sel);
int64_t       il2cpp_linq_grouping_sum_dec_proj_i8(Il2CppObject* grouping, Il2CppObject* sel);
float         il2cpp_linq_grouping_sum_dec_proj_f(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_sum_dec_proj_d(Il2CppObject* grouping, Il2CppObject* sel);
int32_t       il2cpp_linq_grouping_min_dec_proj_i4(Il2CppObject* grouping, Il2CppObject* sel);
int64_t       il2cpp_linq_grouping_min_dec_proj_i8(Il2CppObject* grouping, Il2CppObject* sel);
float         il2cpp_linq_grouping_min_dec_proj_f(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_min_dec_proj_d(Il2CppObject* grouping, Il2CppObject* sel);
int32_t       il2cpp_linq_grouping_max_dec_proj_i4(Il2CppObject* grouping, Il2CppObject* sel);
int64_t       il2cpp_linq_grouping_max_dec_proj_i8(Il2CppObject* grouping, Il2CppObject* sel);
float         il2cpp_linq_grouping_max_dec_proj_f(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_max_dec_proj_d(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_average_dec_proj_i4(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_average_dec_proj_i8(Il2CppObject* grouping, Il2CppObject* sel);
float         il2cpp_linq_grouping_average_dec_proj_f(Il2CppObject* grouping, Il2CppObject* sel);
double        il2cpp_linq_grouping_average_dec_proj_d(Il2CppObject* grouping, Il2CppObject* sel);

// impl-linq2 (B-3): the remaining DECIMAL-source element / predicate operators
// (net48 FirstOrDefault / LastOrDefault / Single / SingleOrDefault and their
// predicate forms, LongCount, Count(pred), Any(pred), All, TakeWhile /
// SkipWhile) plus the source-only DefaultIfEmpty<decimal>(). The default cell
// is the zero decimal; the predicate ABI is bool(*)(decimal) by value.
void          il2cpp_linq_first_or_default_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void          il2cpp_linq_last_or_default_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void          il2cpp_linq_first_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred);
void          il2cpp_linq_first_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred);
void          il2cpp_linq_last_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred);
void          il2cpp_linq_last_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred);
void          il2cpp_linq_single_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void          il2cpp_linq_single_or_default_dec(runtime::Il2CppDecimal* result, Il2CppArray* src);
void          il2cpp_linq_single_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred);
void          il2cpp_linq_single_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred);
int32_t       il2cpp_linq_count_pred_dec(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_any_pred_dec(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_all_dec(Il2CppArray* src, Il2CppObject* pred);
int64_t       il2cpp_linq_long_count_dec(Il2CppArray* src);
Il2CppArray*  il2cpp_linq_take_while_dec(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_dec(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_default_if_empty1_dec(Il2CppArray* src);

// impl-linq-vtpred: the SOURCE PREDICATE family over the by-value struct
// families (System.DateTime / System.TimeSpan / System.Guid). net48's
// Enumerable operators are generic in TSource, and the generated predicate
// leaf takes the value struct BY VALUE (Il2CppDateTime / Il2CppTimeSpan /
// Il2CppGuid) -- the i4 / ref / wide predicate entries would call it through
// a foreign ABI and read the wrong cell width. Every entry validates the
// source's real cell width before any element work.
//   * array-returning: Where / TakeWhile / SkipWhile (and their index-aware
//     Func<TSource,int,bool> forms) materialize a fresh array of the source's
//     own element family;
//   * scalar predicates: All / Count(pred) / Any(pred) follow the net48 empty
//     truth table (All -> true without calling the predicate, Count -> 0,
//     Any -> false);
//   * First / FirstOrDefault / Last / LastOrDefault return the element BY
//     VALUE (the family result-slot ABI; First/Last throw on an empty source,
//     the *OrDefault forms answer default(T)).
Il2CppArray*  il2cpp_linq_where_dt(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_idx_dt(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_dt(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_idx_dt(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_dt(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_idx_dt(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_all_dt(Il2CppArray* src, Il2CppObject* pred);
int32_t       il2cpp_linq_count_pred_dt(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_any_pred_dt(Il2CppArray* src, Il2CppObject* pred);
runtime::Il2CppDateTime il2cpp_linq_first_dt(Il2CppArray* src);
runtime::Il2CppDateTime il2cpp_linq_first_or_default_dt(Il2CppArray* src);
runtime::Il2CppDateTime il2cpp_linq_last_dt(Il2CppArray* src);
runtime::Il2CppDateTime il2cpp_linq_last_or_default_dt(Il2CppArray* src);

Il2CppArray*  il2cpp_linq_where_ts(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_idx_ts(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_ts(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_idx_ts(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_ts(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_idx_ts(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_all_ts(Il2CppArray* src, Il2CppObject* pred);
int32_t       il2cpp_linq_count_pred_ts(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_any_pred_ts(Il2CppArray* src, Il2CppObject* pred);
runtime::Il2CppTimeSpan il2cpp_linq_first_ts(Il2CppArray* src);
runtime::Il2CppTimeSpan il2cpp_linq_first_or_default_ts(Il2CppArray* src);
runtime::Il2CppTimeSpan il2cpp_linq_last_ts(Il2CppArray* src);
runtime::Il2CppTimeSpan il2cpp_linq_last_or_default_ts(Il2CppArray* src);

Il2CppArray*  il2cpp_linq_where_guid(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_idx_guid(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_guid(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_idx_guid(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_guid(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_idx_guid(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_all_guid(Il2CppArray* src, Il2CppObject* pred);
int32_t       il2cpp_linq_count_pred_guid(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_any_pred_guid(Il2CppArray* src, Il2CppObject* pred);
Il2CppGuid    il2cpp_linq_first_guid(Il2CppArray* src);
Il2CppGuid    il2cpp_linq_first_or_default_guid(Il2CppArray* src);
Il2CppGuid    il2cpp_linq_last_guid(Il2CppArray* src);
Il2CppGuid    il2cpp_linq_last_or_default_guid(Il2CppArray* src);

// impl-listvtops: the System.DateTimeOffset ARRAY predicate family (delta 2).
// The 16-byte DateTimeOffset cell had NO array entries, so a DateTimeOffset[]
// source dispatched every one of these operators into the Guid kernels -- a
// 16-byte cell of the WRONG family (the cell guard then threw, or the value
// was read as a Guid). Same shapes as the dt/ts/guid families above.
Il2CppArray*  il2cpp_linq_where_dtoffset(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_idx_dtoffset(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_dtoffset(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_take_while_idx_dtoffset(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_dtoffset(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_skip_while_idx_dtoffset(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_all_dtoffset(Il2CppArray* src, Il2CppObject* pred);
int32_t       il2cpp_linq_count_pred_dtoffset(Il2CppArray* src, Il2CppObject* pred);
bool          il2cpp_linq_any_pred_dtoffset(Il2CppArray* src, Il2CppObject* pred);
runtime::Il2CppDateTimeOffset il2cpp_linq_first_dtoffset(Il2CppArray* src);
runtime::Il2CppDateTimeOffset il2cpp_linq_first_or_default_dtoffset(Il2CppArray* src);
runtime::Il2CppDateTimeOffset il2cpp_linq_last_dtoffset(Il2CppArray* src);
runtime::Il2CppDateTimeOffset il2cpp_linq_last_or_default_dtoffset(Il2CppArray* src);

// impl-linq2 (B-3): the DECIMAL-BUCKET operator surface -- each entry
// snapshots the bucket's decimal cells and reuses the DECIMAL-SOURCE kernel
// (the same operator set net48 exposes over IEnumerable<decimal>).
void          il2cpp_linq_grouping_first_or_default_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_last_or_default_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_first_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred);
void          il2cpp_linq_grouping_first_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred);
void          il2cpp_linq_grouping_last_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred);
void          il2cpp_linq_grouping_last_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred);
void          il2cpp_linq_grouping_single_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_single_or_default_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping);
void          il2cpp_linq_grouping_single_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred);
void          il2cpp_linq_grouping_single_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred);
void          il2cpp_linq_grouping_element_at_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, int32_t index);
Il2CppArray*  il2cpp_linq_grouping_where_dec(Il2CppObject* grouping, Il2CppObject* pred);
// impl-linq3 (C): the DECIMAL-bucket Select surface -- each entry snapshots
// the bucket's decimal cells (RequireDecGroupElements) and reuses the
// DECIMAL-SOURCE select_dec_* / select_idx_dec_* kernel over the snapshot;
// the result family follows the projection (decimal/int32/int64/double/float
// cells, or a fresh reference array).
Il2CppArray*  il2cpp_linq_grouping_select_dec_dec(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_dec_i4(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_dec_i8(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_dec_d(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_dec_f(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_dec_ref(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_idx_dec_dec(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_idx_dec_i4(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_idx_dec_i8(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_idx_dec_d(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_idx_dec_f(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_select_idx_dec_ref(Il2CppObject* grouping, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_grouping_reverse_dec(Il2CppObject* grouping);
Il2CppArray*  il2cpp_linq_grouping_take_dec(Il2CppObject* grouping, int32_t count);
Il2CppArray*  il2cpp_linq_grouping_skip_dec(Il2CppObject* grouping, int32_t count);
Il2CppArray*  il2cpp_linq_grouping_take_while_dec(Il2CppObject* grouping, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_grouping_skip_while_dec(Il2CppObject* grouping, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_grouping_distinct_dec(Il2CppObject* grouping);
Il2CppArray*  il2cpp_linq_grouping_except_dec(Il2CppObject* grouping, Il2CppArray* second);
Il2CppArray*  il2cpp_linq_grouping_concat_dec(Il2CppObject* grouping, Il2CppArray* second);
Il2CppArray*  il2cpp_linq_grouping_default_if_empty_dec(Il2CppObject* grouping, runtime::Il2CppDecimal value);
bool          il2cpp_linq_grouping_sequence_equal_dec(Il2CppObject* grouping, Il2CppArray* second);
int64_t       il2cpp_linq_grouping_long_count_dec(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_count_pred_dec(Il2CppObject* grouping, Il2CppObject* pred);
bool          il2cpp_linq_grouping_any_pred_dec(Il2CppObject* grouping, Il2CppObject* pred);
bool          il2cpp_linq_grouping_all_dec(Il2CppObject* grouping, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_dec_i4(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_dec_i8(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_dec_d(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_dec_dec(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_dec_i4(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_dec_i8(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_dec_d(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_dec_dec(Il2CppObject* grouping, Il2CppObject* keySel);
// impl-linqleft: the decimal bucket with a System.DateTime / System.Guid key.
Il2CppArray*  il2cpp_linq_grouping_order_by_key_dec_dt(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_dec_dt(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_dec_guid(Il2CppObject* grouping, Il2CppObject* keySel);
Il2CppArray*  il2cpp_linq_grouping_order_by_key_desc_dec_guid(Il2CppObject* grouping, Il2CppObject* keySel);
// impl-linqleft: the fused decimal-bucket ThenBy composite (desc/k1fam/k2fam
// arrive as trailing literals).
Il2CppArray*  il2cpp_linq_grouping_then_by_key_dec(Il2CppObject* grouping, Il2CppObject* k1Sel, Il2CppObject* k2Sel, int32_t k1desc, int32_t k2desc, int32_t k1fam, int32_t k2fam);
bool          il2cpp_linq_grouping_contains_dec(Il2CppObject* grouping, runtime::Il2CppDecimal value);
Il2CppObject* il2cpp_linq_grouping_result_to_list(Il2CppObject* result);

// The whole GroupBy result: the buckets as a reference array / the first bucket.
Il2CppArray*  il2cpp_linq_grouping_result_to_array(Il2CppObject* result);
Il2CppObject* il2cpp_linq_grouping_result_first(Il2CppObject* result);
Il2CppObject* il2cpp_linq_grouping_result_first_or_default(Il2CppObject* result);
bool          il2cpp_linq_grouping_result_any(Il2CppObject* result);

// Sum on a grouping (the per-bucket aggregate): CHECKED int32 addition over
// the bucket's elements (net48 Enumerable.Sum is checked -- D-LINQ-3); an
// empty bucket -> 0. A null grouping raises NullReferenceException (the
// receiver icall contract, listi4 precedent).
int32_t il2cpp_linq_grouping_sum(Il2CppObject* grouping);

// Iteration surface (first-appearance bucket order):
//   result_count(groupingResult)               -- number of buckets
//   result_get_grouping(result, index)         -- the index-th bucket object
//   grouping_get_key(grouping)                 -- the bucket's key
//   grouping_get_count(grouping)               -- elements in the bucket
//   grouping_get_elements(grouping)            -- an EXACT-SIZE int32[] copy
//     of the bucket's contents (the internal backing array is growable, so
//     the public surface materializes exact storage, listi4 to_array
//     contract; independent of the bucket's internal buffer).
int32_t       il2cpp_linq_grouping_result_get_count(Il2CppObject* result);
// b5-gaps §55: Select over the whole GroupBy result. resKind 0 ref / 1 i4 /
// 2 i8 / 3 f / 4 d.
Il2CppArray*   il2cpp_linq_grouping_result_select(Il2CppObject* result,
                                                  Il2CppObject* sel,
                                                  int32_t resKind);
Il2CppObject* il2cpp_linq_grouping_result_get_grouping(Il2CppObject* result, int32_t index);
int32_t       il2cpp_linq_grouping_get_key(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_get_count(Il2CppObject* grouping);
Il2CppArray*  il2cpp_linq_grouping_get_elements(Il2CppObject* grouping);

// BATCH4: the wide-key accessors. The bucket's key is a 16-byte carrier; each
// entry returns it in the family's REAL ABI:
//   _i8   long / ulong  -> int64_t (ulong rides the same bit pattern)
//   _dec  System.Decimal -> by value (the result slot is the FIRST C++ param,
//                           the decimal T3 contract)
//   _dt   System.DateTime -> runtime::Il2CppDateTime (ticks + kind; the struct's trailing
//                             padding is not part of the key)
//   _guid System.Guid   -> by value through the uint8_t* output buffer (the
//                          guid.h contract: args first, buffer last)
int64_t            il2cpp_linq_grouping_get_key_i8(Il2CppObject* grouping);
void               il2cpp_linq_grouping_get_key_dec(runtime::Il2CppDecimal* result,
                                                    Il2CppObject* grouping);
runtime::Il2CppDateTime     il2cpp_linq_grouping_get_key_dt(Il2CppObject* grouping);
void               il2cpp_linq_grouping_get_key_guid(Il2CppObject* grouping,
                                                     uint8_t bytes[16]);
// p0grp: the REFERENCE-key accessor (System.String / class / object).
Il2CppObject*      il2cpp_linq_grouping_get_key_ref(Il2CppObject* grouping);
// impl-groupbykey: the System.TimeSpan (ticks) and System.DateTimeOffset
// (UTC instant + offset) key accessors. Both return the value BY VALUE; a
// translated user ENUM key keeps the plain int32 accessor
// (il2cpp_linq_grouping_get_key), because the enum's only payload is value__.
runtime::Il2CppTimeSpan     il2cpp_linq_grouping_get_key_ts(Il2CppObject* grouping);
runtime::Il2CppDateTimeOffset il2cpp_linq_grouping_get_key_dto(Il2CppObject* grouping);
// floatgrp: the System.Double key accessor (the raw 64-bit carrier image).
double              il2cpp_linq_grouping_get_key_d(Il2CppObject* grouping);

// floatgrp: GroupBy over a List<System.Single> source. The list snapshot is the
// REAL float[] (il2cpp_list_obj_to_array_float), which then rides the ordinary
// float-source kernel -- the list domain's own entry would read every boxed
// Single as an int32 and drop the key family.
Il2CppObject*       il2cpp_linq_list_float_group_by_full(Il2CppObject* list,
                                                         Il2CppObject* keySel,
                                                         Il2CppObject* elemSel,
                                                         Il2CppObject* cmp,
                                                         Il2CppObject* resSel,
                                                         int32_t elemKind, int32_t rsKind,
                                                         int32_t keyKind);

// IEnumerable.GetEnumerator over the two grouping surfaces (nb9): the
// result yields its buckets in first-appearance order (reference elements),
// one bucket yields its elements (boxed int32, so both the i4 and the ref
// get_Current carriers observe the net48 value). Both materialize an
// EXACT-SIZE snapshot and wrap it in the shared managed array enumerator
// (arraysort.cpp dispatches these two klasses to them).
Il2CppObject* il2cpp_linq_grouping_result_get_enumerator(Il2CppObject* result);
Il2CppObject* il2cpp_linq_grouping_get_enumerator(Il2CppObject* grouping);

//------------------------------------------------------------------------------
// SEQUENCE-COMBINATION / SET-FAMILY OPERATORS (D-LINQZ-1) -- int32 element
// family. Eager materialization (the stage-1 AOT form): every entry returns a
// FRESH int32[] (il2cpp_array_new); the source arrays are never modified. All
// four validate the source arrays (null -> ArgumentNullException; a non-array
// object -> ArgumentException; a reference-element array -> NotSupported-
// Exception -- the linq_map RequireArray / RequireInt32Family contract).
//
// net48 semantics (probe-pinned by the linqzip e2e case, expected.txt):
//   * Concat(first, second) -- ALL elements of first then ALL of second,
//     preserving order AND duplicates; length = sum of the parts; any empty
//     operand contributes nothing.
//   * Distinct(source)      -- remove duplicates keeping FIRST-APPEARANCE
//     order (default ordinal int32 equality, net48 EqualityComparer<int>);
//     an empty / single-element source passes through.
//   * Except(first, second) -- the elements of first NOT present in second
//     (set membership -- duplicates in second are irrelevant), first-appearance
//     order, DEDUPLICATED output.
//   * Zip(first, second, selector) -- element-wise pairing to the SHORTER
//     length; the two-arg selector Func<TSource,TSecond,TResult> is invoked
//     EXACTLY ONCE per output element (net48); an empty operand yields an
//     empty result with NO selector invocation.
//
// Zip CALLBACK CONTRACT (the 2-arg selector family): the delegate object is
// validated like every LINQ callback (null -> ArgumentNullException; a
// multicast container -> InvalidOperationException). The selector ABI is
// int32_t(*)(int32_t, int32_t) for a STATIC leaf; an INSTANCE-bound leaf
// (target != null -- a bound instance method such as p.Add) receives its
// target in the FIRST parameter slot (CodeGenerator: instance arg0 = this,
// the linq_map per-binding dispatch): int32_t(*)(Il2CppObject*, int32_t,
// int32_t).
//------------------------------------------------------------------------------
// Concat(first, second) -- concatenate the two int32 arrays (see above).
Il2CppArray* il2cpp_linq_concat_i4(Il2CppArray* first, Il2CppArray* second);

// Distinct(source) -- the source with duplicates removed, first-appearance
// order (see above).
Il2CppArray* il2cpp_linq_distinct_i4(Il2CppArray* src);

// Except(first, second) -- the elements of first not in second, first-
// appearance order, deduplicated (see above).
Il2CppArray* il2cpp_linq_except_i4(Il2CppArray* first, Il2CppArray* second);

// Zip(first, second, resultSelector) -- element-wise pairing to the shorter
// length; the selector Func<int,int,int> runs once per output element.
//   sel : delegate wrapping int32_t(*)(int32_t, int32_t) (STATIC leaf) /
//         int32_t(*)(Il2CppObject*, int32_t, int32_t) (instance leaf --
//         validation per the callback contract above)
Il2CppArray* il2cpp_linq_zip_i4(Il2CppArray* first, Il2CppArray* second,
                                Il2CppObject* sel);

// b5-gaps §54: Cast<T> / OfType<T> over an OBJECT-element array source. The
// value-family entries take the result family code; the *_ref entries take the
// TARGET Il2CppClass* (conformance by il2cpp_is_instance). Cast<T> follows
// unbox.any (null -> NullReferenceException, wrong type -> InvalidCastException);
// OfType<T> drops nulls/non-matching elements.
Il2CppArray* il2cpp_linq_cast(Il2CppArray* src, int32_t fam);
Il2CppArray* il2cpp_linq_of_type(Il2CppArray* src, int32_t fam);
Il2CppArray* il2cpp_linq_cast_ref(Il2CppArray* src, Il2CppClass* klass);
Il2CppArray* il2cpp_linq_of_type_ref(Il2CppArray* src, Il2CppClass* klass);
} // namespace il2cpp

