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

// Last / LastOrDefault on reference-element arrays (nb15 B5 FIX: net48
// Last/LastOrDefault are generic, so a string[]/object[]/class[] source must
// not be rejected; empty -> InvalidOperationException / nullptr).
Il2CppObject* il2cpp_linq_last_ref(Il2CppArray* src);
Il2CppObject* il2cpp_linq_last_or_default_ref(Il2CppArray* src);

//------------------------------------------------------------------------------
// nb15 B5 FIX -- the WIDE / NARROW element families for the predicate
// operators (Any/Count/All).
//
// The int32 entries above read every element as a 4-byte cell. An int64[] /
// double[] / float[] / byte[] / sbyte[] / short[] / ushort[] / char[] / bool[]
// source has a DIFFERENT cell width, so reusing an i4 entry reads garbage (and
// a 1/2-byte array is read past its element). Each entry below reads the cell
// at its REAL width and widens it to the delegate ABI slot:
//   * _i8   int64_t(*)(int64_t)              (int64[]/uint64[])
//   * _d    bool(*)(double)                  (double[]; float[] widened)
//   * _f    bool(*)(double)                  (float[] -- F-stack carrier is
//                                             double; a float leaf's C++
//                                             return is float, so the result
//                                             is read as float)
//   * _narrow  bool(*)(int32_t)              (byte/sbyte/short/ushort/char/
//                                             bool/uint -- the CLR widens
//                                             narrow integral parameters to
//                                             the int32 slot, zero/sign
//                                             extending from the cell)
//   * _ref  the reference family (already above)
// Empty-sequence semantics match the int32 entries: Any -> false (predicate
// never called), All -> true (vacuous truth), Count -> 0.
//------------------------------------------------------------------------------
bool    il2cpp_linq_any_i8(Il2CppArray* src, Il2CppObject* pred);
bool    il2cpp_linq_any_d(Il2CppArray* src, Il2CppObject* pred);
bool    il2cpp_linq_any_f(Il2CppArray* src, Il2CppObject* pred);
bool    il2cpp_linq_any_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind);
int32_t il2cpp_linq_count_i8(Il2CppArray* src, Il2CppObject* pred);
int32_t il2cpp_linq_count_d(Il2CppArray* src, Il2CppObject* pred);
int32_t il2cpp_linq_count_f(Il2CppArray* src, Il2CppObject* pred);
int32_t il2cpp_linq_count_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind);
bool    il2cpp_linq_all_i8(Il2CppArray* src, Il2CppObject* pred);
bool    il2cpp_linq_all_d(Il2CppArray* src, Il2CppObject* pred);
bool    il2cpp_linq_all_f(Il2CppArray* src, Il2CppObject* pred);
bool    il2cpp_linq_all_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind);

// The narrow element kind codes for the *_narrow entries above. They name the
// CELL width and the widening rule (zero- vs sign-extension), so the runtime
// never guesses from the array class alone (byte[] and bool[] are both 1-byte
// cells but widen differently).
//   0 = int8_t   (sbyte)   1 = uint8_t  (byte)    2 = bool (1 byte, 0/1)
//   3 = int16_t  (short)   4 = uint16_t (ushort)  5 = char16_t (UTF-16)
//   6 = uint32_t (uint -- 4 bytes, ZERO-extended into the int32 slot)
constexpr int32_t IL2CPP_LINQ_NARROW_SBYTE  = 0;
constexpr int32_t IL2CPP_LINQ_NARROW_BYTE   = 1;
constexpr int32_t IL2CPP_LINQ_NARROW_BOOL   = 2;
constexpr int32_t IL2CPP_LINQ_NARROW_SHORT  = 3;
constexpr int32_t IL2CPP_LINQ_NARROW_USHORT = 4;
constexpr int32_t IL2CPP_LINQ_NARROW_CHAR   = 5;
constexpr int32_t IL2CPP_LINQ_NARROW_UINT   = 6;
//------------------------------------------------------------------------------
// The ZIP SIDE descriptor for il2cpp_linq_zip_wide: the two sources of a Zip
// may have DIFFERENT families, so each side carries its own descriptor -- the
// narrow codes above (0..6) plus four sentinels for the wide families. It is
// deliberately NOT the IL2CPP_LIST_KIND_* space (both are small integers with
// different meanings); linq_list.cpp translates through the explicit named
// table in il2cpp_linq_list_zip.
//------------------------------------------------------------------------------
constexpr int32_t IL2CPP_LINQ_ZIP_I4 = 7;
constexpr int32_t IL2CPP_LINQ_ZIP_I8 = 8;
constexpr int32_t IL2CPP_LINQ_ZIP_F  = 9;
constexpr int32_t IL2CPP_LINQ_ZIP_D  = 10;
// P0LINQ1: a System.Decimal source side. The element crosses as the 16-byte
// runtime value struct BY VALUE (the decimal T3 contract), so the selector's
// declared parameter type is il2cpp::runtime::Il2CppDecimal.
constexpr int32_t IL2CPP_LINQ_ZIP_DEC = 11;
//------------------------------------------------------------------------------
// nb15 B5 FIX -- the same WIDE / NARROW families for Where / First /
// FirstOrDefault / Last / LastOrDefault. Each keeps the source element family
// (Where returns a fresh array of the SAME cell width; the element operators
// return the widened value through the family's result ABI).
// Select over a wide/narrow source needs the full source x result matrix and
// is NOT wired (documented in docs/audit/b5-gaps.md).
//------------------------------------------------------------------------------
Il2CppArray*  il2cpp_linq_where_i8(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_d(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_f(Il2CppArray* src, Il2CppObject* pred);
Il2CppArray*  il2cpp_linq_where_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind);
// Select over a wide / narrow source. resKind selects the result family
// (0 int32 / 1 reference / 2 int64 / 3 float / 4 double); the selector
// receives the widened source value (int32 slot for narrow, int64 for int64,
// double for float/double).
Il2CppArray*  il2cpp_linq_select_wide(Il2CppArray* src, Il2CppObject* sel,
                                      int32_t kind, int32_t resKind);

int64_t       il2cpp_linq_first_i8(Il2CppArray* src);
// The float family shares the double RESULT ABI (a float parameter/return
// crosses as double), so these return double like the _d entries; they stay
// separate so the transpiler dispatches on the metadata family.
double        il2cpp_linq_first_f(Il2CppArray* src);
double        il2cpp_linq_first_or_default_f(Il2CppArray* src);
int64_t       il2cpp_linq_first_or_default_i8(Il2CppArray* src);
double        il2cpp_linq_first_d(Il2CppArray* src);
double        il2cpp_linq_first_or_default_d(Il2CppArray* src);
int32_t       il2cpp_linq_first_narrow(Il2CppArray* src, int32_t elemKind);
int32_t       il2cpp_linq_first_or_default_narrow(Il2CppArray* src, int32_t elemKind);

int64_t       il2cpp_linq_last_i8(Il2CppArray* src);
double        il2cpp_linq_last_f(Il2CppArray* src);
double        il2cpp_linq_last_or_default_f(Il2CppArray* src);
int64_t       il2cpp_linq_last_or_default_i8(Il2CppArray* src);
double        il2cpp_linq_last_d(Il2CppArray* src);
double        il2cpp_linq_last_or_default_d(Il2CppArray* src);
int32_t       il2cpp_linq_last_narrow(Il2CppArray* src, int32_t elemKind);
int32_t       il2cpp_linq_last_or_default_narrow(Il2CppArray* src, int32_t elemKind);

// Zip over the WIDE / NARROW element families: pair the two sources to the
// shorter length and project each pair through `sel`. The two sources may have
// DIFFERENT families (List<double>.Zip(int[])), so each side carries its OWN
// descriptor: `firstSide` / `secondSide` are ZIP SIDE codes (the narrow codes
// 0..6 above, or IL2CPP_LINQ_ZIP_I4/I8/F/D). Each side is read at its own
// width, and `sel` is called through the ABI of its STATICALLY DECLARED
// parameter types -- int32_t for the int32 slot and every narrow family,
// int64_t for i8, double for float/double (CodeGenerator's delegate-param
// carrier). Calling a (double, int32_t) thunk through a double(*)(double,
// double) pointer would put the second argument in XMM1 while the callee reads
// a GPR, so the per-combination table in linq.cpp is load-bearing.
// `resKind` is the selector's RETURN family: 0 int32 / 2 int64 / 3 float
// (written as the 8-byte double carrier, the float's own bits in the low 4
// bytes -- the same convention as il2cpp_linq_select_wide) / 4 double /
// 5 uint32. The transpiler only emits this entry for those result families; a
// 1/2-byte integral, bool/char or reference TResult is rejected at transpile
// time (no cell ABI).
Il2CppArray* il2cpp_linq_zip_wide(Il2CppArray* first, Il2CppArray* second,
                                  Il2CppObject* sel, int32_t firstSide,
                                  int32_t secondSide, int32_t resKind);

// BATCH4: the reference-source / narrow-result Zip entry. Same source
// descriptors as il2cpp_linq_zip_wide (the IL2CPP_LIST_KIND_* codes; a
// reference element byte maps to IL2CPP_LIST_KIND_REF), but it reads a
// reference side as Il2CppObject* and supports a wider result cell ABI:
//   0 int32 / 2 int64 / 3 float (double carrier) / 4 double / 5 uint32 /
//   6 reference (a REFERENCE array) /
//   7 sbyte / 8 byte / 12 bool (1-byte cells) /
//   9 short / 10 ushort / 11 char (2-byte cells).
Il2CppArray* il2cpp_linq_zip_mixed(Il2CppArray* first, Il2CppArray* second,
                                   Il2CppObject* sel, int32_t firstKind,
                                   int32_t secondKind, int32_t resKind);

// The ZIP SIDE descriptor (IL2CPP_LINQ_ZIP_*/IL2CPP_LINQ_NARROW_*) of an
// IL2CPP_LIST_KIND_* element family. Defined in linq_list.cpp and shared with
// linq.cpp's il2cpp_linq_zip_mixed. IL2CPP_LIST_KIND_REF has no value-side
// code (a reference side is read directly); the caller must not pass it.
int32_t ZipSideOfListKind(int32_t kind);

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
