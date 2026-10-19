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

// Aggregate(source, seed, func) on int32[]: fold the sequence left-to-right
// using the binary func, seeding the accumulator with 'seed' (net48
// Aggregate(seed, func)). An EMPTY source returns the seed unchanged (net48:
// no InvalidOperationException when a seed is supplied). The func ABI is the
// same binary family as the seedless overload
// (int32(*)(int32_t,int32_t) / int32(*)(Il2CppObject*,int32_t,int32_t)).
int32_t il2cpp_linq_aggregate_seed_i4(Il2CppArray* src, int32_t seed, Il2CppObject* func);

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

//------------------------------------------------------------------------------
// nb9 FIX BATCH -- Aggregate's accumulator family + the result-selector form.
//
// net48 Aggregate<TSource,TAccumulate[,TResult]>:
//   Aggregate(seed, func)                  -- returns TAccumulate
//   Aggregate(seed, func, resultSelector)  -- returns TResult, the selector
//                                             runs ONCE on the final accumulator
// TAccumulate decides the fold ABI: int32, int64, double or a managed
// reference (string/object/class). An EMPTY source returns the seed (and then
// runs the result selector on it).
//------------------------------------------------------------------------------
int64_t       il2cpp_linq_aggregate_seed_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func);
double        il2cpp_linq_aggregate_seed_d(Il2CppArray* src, double seed, Il2CppObject* func);
Il2CppObject* il2cpp_linq_aggregate_seed_ref(Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func);

int32_t       il2cpp_linq_aggregate_seed_rs_i4(Il2CppArray* src, int32_t seed,
                                               Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_rs_ref(Il2CppArray* src, int32_t seed,
                                                Il2CppObject* func, Il2CppObject* resSel);
int32_t       il2cpp_linq_aggregate_seed_i8_rs_i4(Il2CppArray* src, int64_t seed,
                                                  Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_i8_rs_ref(Il2CppArray* src, int64_t seed,
                                                   Il2CppObject* func, Il2CppObject* resSel);
int32_t       il2cpp_linq_aggregate_seed_d_rs_i4(Il2CppArray* src, double seed,
                                                 Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_d_rs_ref(Il2CppArray* src, double seed,
                                                  Il2CppObject* func, Il2CppObject* resSel);
int32_t       il2cpp_linq_aggregate_seed_ref_rs_i4(Il2CppArray* src, Il2CppObject* seed,
                                                   Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_ref_rs_ref(Il2CppArray* src, Il2CppObject* seed,
                                                    Il2CppObject* func, Il2CppObject* resSel);

// nb9 H-F3: numeric TResult variants. The result selector's return carrier
// follows TResult: int64_t (long), double (float -- the C++ float return is
// widened -- and double). A float/double TResult is carried as double at the
// transpiler boundary (the F-stack convention).
int64_t il2cpp_linq_aggregate_seed_rs_i8(Il2CppArray* src, int32_t seed,
                                         Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_rs_f(Il2CppArray* src, int32_t seed,
                                        Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_rs_d(Il2CppArray* src, int32_t seed,
                                        Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_i8_rs_i8(Il2CppArray* src, int64_t seed,
                                            Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_i8_rs_f(Il2CppArray* src, int64_t seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_i8_rs_d(Il2CppArray* src, int64_t seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_d_rs_i8(Il2CppArray* src, double seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_d_rs_f(Il2CppArray* src, double seed,
                                          Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_d_rs_d(Il2CppArray* src, double seed,
                                          Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_ref_rs_i8(Il2CppArray* src, Il2CppObject* seed,
                                             Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_ref_rs_f(Il2CppArray* src, Il2CppObject* seed,
                                            Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_ref_rs_d(Il2CppArray* src, Il2CppObject* seed,
                                            Il2CppObject* func, Il2CppObject* resSel);

// nb9 H-B6: ref-accumulator entries per SOURCE element family (the base
// il2cpp_linq_aggregate_seed_ref is the int32 source). TSource decides how the
// element is read and passed to the func delegate.
#define NB9_DECL_AGG_REF_SRC(SRC)                                                  \
Il2CppObject* il2cpp_linq_aggregate_seed_ref_##SRC(Il2CppArray* src,               \
    Il2CppObject* seed, Il2CppObject* func);                                        \
int32_t il2cpp_linq_aggregate_seed_ref_##SRC##_rs_i4(Il2CppArray* src,             \
    Il2CppObject* seed, Il2CppObject* func, Il2CppObject* resSel);                  \
Il2CppObject* il2cpp_linq_aggregate_seed_ref_##SRC##_rs_ref(Il2CppArray* src,      \
    Il2CppObject* seed, Il2CppObject* func, Il2CppObject* resSel);                  \
int64_t il2cpp_linq_aggregate_seed_ref_##SRC##_rs_i8(Il2CppArray* src,             \
    Il2CppObject* seed, Il2CppObject* func, Il2CppObject* resSel);                  \
double il2cpp_linq_aggregate_seed_ref_##SRC##_rs_f(Il2CppArray* src,               \
    Il2CppObject* seed, Il2CppObject* func, Il2CppObject* resSel);                  \
double il2cpp_linq_aggregate_seed_ref_##SRC##_rs_d(Il2CppArray* src,               \
    Il2CppObject* seed, Il2CppObject* func, Il2CppObject* resSel);

NB9_DECL_AGG_REF_SRC(refsrc)
NB9_DECL_AGG_REF_SRC(i8src)
NB9_DECL_AGG_REF_SRC(dsrc)
NB9_DECL_AGG_REF_SRC(fsrc)

#undef NB9_DECL_AGG_REF_SRC

// nb9 fix P0-1: the FLOAT accumulator channel. The transpiler puts float on
// the IL F stack, so a float-returning lambda is emitted as
// `float f(double, int32_t)` (int32-element source) or `float f(double, double)`
// (widened double-element source): the accumulator arrives as double and the
// result is float. The old mapping folded float into the double family and
// read the float return back as a double (upper 32 bits garbage).
float il2cpp_linq_aggregate_seed_f(Il2CppArray* src, float seed, Il2CppObject* func);
float il2cpp_linq_aggregate_seed_f_d(Il2CppArray* src, float seed, Il2CppObject* func);
// nb9 H-F2: a float accumulator over a REAL float[] source (float Select).
float il2cpp_linq_aggregate_f_f(Il2CppArray* src, Il2CppObject* func);
float il2cpp_linq_aggregate_seed_f_f(Il2CppArray* src, float seed, Il2CppObject* func);
int32_t il2cpp_linq_aggregate_seed_f_rs_i4(Il2CppArray* src, float seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
Il2CppObject* il2cpp_linq_aggregate_seed_f_rs_ref(Il2CppArray* src, float seed,
                                                  Il2CppObject* func, Il2CppObject* resSel);
int64_t il2cpp_linq_aggregate_seed_f_rs_i8(Il2CppArray* src, float seed,
                                           Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_f_rs_f(Il2CppArray* src, float seed,
                                          Il2CppObject* func, Il2CppObject* resSel);
double  il2cpp_linq_aggregate_seed_f_rs_d(Il2CppArray* src, float seed,
                                          Il2CppObject* func, Il2CppObject* resSel);

// nb9 fix P0-1/P0-4: the WIDENED source families. An int64/double/float
// Select result is a real int64[]/double[]; these fold it without narrowing.
int64_t il2cpp_linq_aggregate_i8(Il2CppArray* src, Il2CppObject* func);
double  il2cpp_linq_aggregate_d(Il2CppArray* src, Il2CppObject* func);
float   il2cpp_linq_aggregate_f_d(Il2CppArray* src, Il2CppObject* func);
int64_t il2cpp_linq_aggregate_seed_i8_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func);// nb9 H-F3: matching wide-source numeric accumulator + wide result selector
// variants (the plain *_i8_rs_* / *_d_rs_* read int32 elements).
#define NB9_DECL_AGG_NUM_SRC(NAME, SEEDTYPE)                                       \
int32_t NAME##_rs_i4(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,          \
    Il2CppObject* resSel);                                                         \
Il2CppObject* NAME##_rs_ref(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,   \
    Il2CppObject* resSel);                                                         \
int64_t NAME##_rs_i8(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,          \
    Il2CppObject* resSel);                                                         \
double NAME##_rs_f(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,            \
    Il2CppObject* resSel);                                                         \
double NAME##_rs_d(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,            \
    Il2CppObject* resSel);
NB9_DECL_AGG_NUM_SRC(il2cpp_linq_aggregate_seed_i8_i8, int64_t)
NB9_DECL_AGG_NUM_SRC(il2cpp_linq_aggregate_seed_d_d, double)
#undef NB9_DECL_AGG_NUM_SRC

double  il2cpp_linq_aggregate_seed_d_d(Il2CppArray* src, double seed, Il2CppObject* func);

// nb9 fix P0-4: ToArray over ANY element family (clones by the source's own
// elemSize / isRefElem).
Il2CppArray* il2cpp_linq_to_array_family(Il2CppArray* src);

// nb9 fix P0-4: the source-only aggregates over the widened element families.
int64_t il2cpp_linq_sum_i8(Il2CppArray* src);
int64_t il2cpp_linq_min_i8(Il2CppArray* src);
int64_t il2cpp_linq_max_i8(Il2CppArray* src);
double  il2cpp_linq_average_i8(Il2CppArray* src);
double  il2cpp_linq_sum_d(Il2CppArray* src);
double  il2cpp_linq_min_d(Il2CppArray* src);
double  il2cpp_linq_max_d(Il2CppArray* src);
double  il2cpp_linq_average_d(Il2CppArray* src);
float   il2cpp_linq_sum_f(Il2CppArray* src);
float   il2cpp_linq_min_f(Il2CppArray* src);
float   il2cpp_linq_max_f(Il2CppArray* src);
float   il2cpp_linq_average_f(Il2CppArray* src);

} // namespace il2cpp
