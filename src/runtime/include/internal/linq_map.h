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
// STAGE-1 DEVIATION (documented, accepted per design section 1.3): Where runs the
// predicate TWICE per element (two passes: count matches, then fill an
// exact-size result) -- observable only for side-effecting predicates.
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
//       int32_t      key;      // the bucket key (NOT a GC reference)
//       Il2CppArray* elements; // INT32 value array: slot i = bucket element i
//       int32_t      count;    // number of elements in the bucket
//   };
//   // instanceSize 32, refFieldBitmap bit 2 (elements)
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
struct Il2CppLinqGrouping
{
    Il2CppObject obj;       // object header (klass pointer)
    int32_t      key;       // the bucket key (NOT a GC reference)
    Il2CppArray* elements;  // INT32 value array: slot i = bucket element i
    int32_t      count;     // number of elements in the bucket
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
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_full_key(Il2CppArray* src, Il2CppObject* keySel,
                                            Il2CppObject* elemSel, Il2CppObject* cmp,
                                            Il2CppObject* resSel, int32_t elemKind,
                                            int32_t rsKind, int32_t keyKind);

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
double  il2cpp_linq_average_sel_i4(Il2CppArray* src, Il2CppObject* sel);

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
bool          il2cpp_linq_grouping_all(Il2CppObject* grouping, Il2CppObject* pred);
int32_t       il2cpp_linq_grouping_first_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_first_or_default_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_last_i4(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_element_at_i4(Il2CppObject* grouping, int32_t index);
bool          il2cpp_linq_grouping_contains_i4(Il2CppObject* grouping, int32_t value);
Il2CppArray*  il2cpp_linq_grouping_to_array(Il2CppObject* grouping);
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
Il2CppObject* il2cpp_linq_grouping_result_get_grouping(Il2CppObject* result, int32_t index);
int32_t       il2cpp_linq_grouping_get_key(Il2CppObject* grouping);
int32_t       il2cpp_linq_grouping_get_count(Il2CppObject* grouping);
Il2CppArray*  il2cpp_linq_grouping_get_elements(Il2CppObject* grouping);

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

} // namespace il2cpp
