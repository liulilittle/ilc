#pragma once
//------------------------------------------------------------------------------
// linq_list.h -- System.Linq over a List<T> RECEIVER (nb15 B5 FIX, D-2).
//
// A List<T> is a managed LIST object (Il2CppListI4 / Il2CppListObj), NOT an
// array. Casting it to Il2CppArray* reads the items pointer's low 32 bits as
// the array length (offsetof(Il2CppArray,length) == offsetof(Il2CppListI4,
// items) == 8) -- a silent wrong value or an access violation. Every entry
// here takes the LIST OBJECT and reaches the elements through the list
// domain's own snapshot accessor (il2cpp_list_i4_to_array /
// il2cpp_list_obj_to_array_<kind>), then runs the matching ARRAY kernel or
// walks the exact-size snapshot at the operation's REAL element width. The
// snapshot is an independent copy -- the same storage `List<T>.ToArray()`
// hands out -- so the eager LINQ semantics are unchanged and the list is
// never mutated.
//
// Element-kind codes (the transpiler passes the static one as a literal):
//   0  int32    (List<int>  -- the listi4 storage)
//   1  reference (List<string>/List<object>/List<class>/List<array>)
//   2  int64    3  float    4  double
//   5  sbyte    6  byte     7  bool     8  short
//   9  ushort   10 char     11 uint
//
// Kinds 5..11 are the listobj boxed-value expansions (listobj.h *_<kind>
// forwarders); their snapshots are typed value arrays of the matching width,
// so the helpers below read the cell at its REAL width and widen a narrow
// integral cell into the int32 delegate slot (the CLR's parameter-widening
// rule -- never a 4-byte over-read).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "decimal.h"      // runtime::Il2CppDecimal (the decimal-projection aggregates)

#include <cstdint>

namespace il2cpp {

// Element-kind codes (see the header comment).
constexpr int32_t IL2CPP_LIST_KIND_I4 = 0;
constexpr int32_t IL2CPP_LIST_KIND_REF = 1;
constexpr int32_t IL2CPP_LIST_KIND_I8 = 2;
constexpr int32_t IL2CPP_LIST_KIND_F = 3;
constexpr int32_t IL2CPP_LIST_KIND_D = 4;
constexpr int32_t IL2CPP_LIST_KIND_SBYTE = 5;
constexpr int32_t IL2CPP_LIST_KIND_BYTE = 6;
constexpr int32_t IL2CPP_LIST_KIND_BOOL = 7;
constexpr int32_t IL2CPP_LIST_KIND_SHORT = 8;
constexpr int32_t IL2CPP_LIST_KIND_USHORT = 9;
constexpr int32_t IL2CPP_LIST_KIND_CHAR = 10;
constexpr int32_t IL2CPP_LIST_KIND_UINT = 11;
// P0LINQ1: a System.Decimal element (16-byte value cell). Only the decimal
// value type has a Zip cell ABI; DateTime/TimeSpan/Guid stay LOUD.
constexpr int32_t IL2CPP_LIST_KIND_DEC = 12;

// The exact-size element snapshot of a list (an independent array; the list is
// never modified). Null list -> ArgumentNullException.
Il2CppArray* il2cpp_linq_list_snapshot(Il2CppObject* list, int32_t kind);

// The element count (T-free: the count slot has the same offset in every list
// layout). Null list -> ArgumentNullException.
int32_t il2cpp_linq_list_count(Il2CppObject* list);

// ToArray over the list (the snapshot IS the exact-size copy).
Il2CppArray* il2cpp_linq_list_to_array(Il2CppObject* list, int32_t kind);

// ToList over the list: a fresh List<T> holding the same elements.
Il2CppObject* il2cpp_linq_list_to_list(Il2CppObject* list, int32_t kind);

// LongCount over the list (int64 result).
int64_t il2cpp_linq_list_long_count(Il2CppObject* list);

//------------------------------------------------------------------------------
// Predicate operators (Any/Count/All). Each reads every element at its real
// cell width and passes the widened value to the delegate:
//   kind 0 / 5..11 -> bool(*)(int32_t)   (narrow cells widened)
//   kind 1         -> bool(*)(Il2CppObject*)
//   kind 2         -> bool(*)(int64_t)
//   kind 3 / 4     -> bool(*)(double)
//------------------------------------------------------------------------------
bool    il2cpp_linq_list_any(Il2CppObject* list);
bool    il2cpp_linq_list_any_pred(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
int32_t il2cpp_linq_list_count_pred(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
bool    il2cpp_linq_list_all_pred(Il2CppObject* list, Il2CppObject* pred, int32_t kind);

//------------------------------------------------------------------------------
// Map operators. Where keeps the source element family; Select writes the
// result family resKind (0 int32 / 1 ref / 2 int64 / 3 float / 4 double).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_where(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_select(Il2CppObject* list, Il2CppObject* sel,
                                     int32_t kind, int32_t resKind);
// Index-predicate / index-selector counterparts (net48 Func<TSource,int,bool> /
// Func<TSource,int,TResult>): the callback receives the element AND its
// zero-based source index. Same result/element family contracts as the 1-arg
// entries; the int32-source and reference-source shapes reuse the array index
// kernels (il2cpp_linq_where_idx_i4 / _ref, il2cpp_linq_select_idx_*).
Il2CppArray* il2cpp_linq_list_where_idx(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_select_idx(Il2CppObject* list, Il2CppObject* sel,
                                         int32_t kind, int32_t resKind);
// Value-type tuple result (resKind 5): the selector returns the tuple struct by
// value; the result array holds BOXED tuples (the shared tuple-box contract --
// see il2cpp_valuetuple.h). 'kinds' is the element-kind string carried in the
// box klass name.
Il2CppArray* il2cpp_linq_list_select_tuple(Il2CppObject* list, Il2CppObject* sel,
                                           int32_t kind, const char* kinds);

//------------------------------------------------------------------------------
// Element operators. The entry suffix is the RESULT ABI, not the list kind:
//   _i4  -> int32_t  (kind 0 and the narrow kinds)
//   _i8  -> int64_t  (kind 2)
//   _d   -> double   (kinds 3/4)
//   _ref -> Il2CppObject* (kind 1)
//------------------------------------------------------------------------------
int32_t       il2cpp_linq_list_first_i4(Il2CppObject* list, int32_t kind);
int64_t       il2cpp_linq_list_first_i8(Il2CppObject* list, int32_t kind);
double        il2cpp_linq_list_first_d(Il2CppObject* list, int32_t kind);
Il2CppObject* il2cpp_linq_list_first_ref(Il2CppObject* list, int32_t kind);
int32_t       il2cpp_linq_list_first_or_default_i4(Il2CppObject* list, int32_t kind);
int64_t       il2cpp_linq_list_first_or_default_i8(Il2CppObject* list, int32_t kind);
double        il2cpp_linq_list_first_or_default_d(Il2CppObject* list, int32_t kind);
Il2CppObject* il2cpp_linq_list_first_or_default_ref(Il2CppObject* list, int32_t kind);

// Last / LastOrDefault (the array surface has no ref last kernel, so the list
// domain carries all four result families itself).
int32_t       il2cpp_linq_list_last_i4(Il2CppObject* list, int32_t kind);
int64_t       il2cpp_linq_list_last_i8(Il2CppObject* list, int32_t kind);
double        il2cpp_linq_list_last_d(Il2CppObject* list, int32_t kind);
Il2CppObject* il2cpp_linq_list_last_ref(Il2CppObject* list, int32_t kind);
int32_t       il2cpp_linq_list_last_or_default_i4(Il2CppObject* list, int32_t kind);
int64_t       il2cpp_linq_list_last_or_default_i8(Il2CppObject* list, int32_t kind);
double        il2cpp_linq_list_last_or_default_d(Il2CppObject* list, int32_t kind);
Il2CppObject* il2cpp_linq_list_last_or_default_ref(Il2CppObject* list, int32_t kind);

// Single / SingleOrDefault (source-only): exactly one element required
// (Single throws InvalidOperationException for an empty OR a multi-element
// source; SingleOrDefault answers default(T) for an empty source and throws
// only for more than one element -- net48). Same result ABI classes as the
// First/Last family above.
int32_t       il2cpp_linq_list_single_i4(Il2CppObject* list, int32_t kind);
int64_t       il2cpp_linq_list_single_i8(Il2CppObject* list, int32_t kind);
double        il2cpp_linq_list_single_d(Il2CppObject* list, int32_t kind);
Il2CppObject* il2cpp_linq_list_single_ref(Il2CppObject* list, int32_t kind);
int32_t       il2cpp_linq_list_single_or_default_i4(Il2CppObject* list, int32_t kind);
int64_t       il2cpp_linq_list_single_or_default_i8(Il2CppObject* list, int32_t kind);
double        il2cpp_linq_list_single_or_default_d(Il2CppObject* list, int32_t kind);
Il2CppObject* il2cpp_linq_list_single_or_default_ref(Il2CppObject* list, int32_t kind);

// First(predicate) / FirstOrDefault(predicate) over the list: the FIRST
// element satisfying the predicate (short-circuit). Empty or no match:
// First -> InvalidOperationException, FirstOrDefault -> the family default.
int32_t       il2cpp_linq_list_first_pred_i4(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
int64_t       il2cpp_linq_list_first_pred_i8(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
double        il2cpp_linq_list_first_pred_d(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppObject* il2cpp_linq_list_first_pred_ref(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
int32_t       il2cpp_linq_list_first_or_default_pred_i4(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
int64_t       il2cpp_linq_list_first_or_default_pred_i8(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
double        il2cpp_linq_list_first_or_default_pred_d(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppObject* il2cpp_linq_list_first_or_default_pred_ref(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
// ElementAt / Contains (the value slot follows the entry suffix).
int32_t       il2cpp_linq_list_element_at_i4(Il2CppObject* list, int32_t index, int32_t kind);
int64_t       il2cpp_linq_list_element_at_i8(Il2CppObject* list, int32_t index, int32_t kind);
double        il2cpp_linq_list_element_at_d(Il2CppObject* list, int32_t index, int32_t kind);
Il2CppObject* il2cpp_linq_list_element_at_ref(Il2CppObject* list, int32_t index, int32_t kind);
bool          il2cpp_linq_list_contains_i4(Il2CppObject* list, int32_t value, int32_t kind);
bool          il2cpp_linq_list_contains_i8(Il2CppObject* list, int64_t value, int32_t kind);
bool          il2cpp_linq_list_contains_d(Il2CppObject* list, double value, int32_t kind);
bool          il2cpp_linq_list_contains_ref(Il2CppObject* list, Il2CppObject* value, int32_t kind);

// Reverse over the list (a fresh array in reverse source order).
Il2CppArray* il2cpp_linq_list_reverse(Il2CppObject* list, int32_t kind);

// DefaultIfEmpty over the list (the source when non-empty, else a one-element
// array holding the family default).
Il2CppArray* il2cpp_linq_list_default_if_empty(Il2CppObject* list, int32_t kind);

//------------------------------------------------------------------------------
// Slices / concatenation (family-agnostic: element copies, no comparison).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_take(Il2CppObject* list, int32_t count, int32_t kind);
Il2CppArray* il2cpp_linq_list_skip(Il2CppObject* list, int32_t count, int32_t kind);
Il2CppArray* il2cpp_linq_list_take_while(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_skip_while(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_concat(Il2CppObject* list, Il2CppObject* second, int32_t kind);

//------------------------------------------------------------------------------
// Set operators / Zip / SequenceEqual (element equality -- int32 and
// reference families; the array kernels own the semantics).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_distinct(Il2CppObject* list, int32_t kind);
Il2CppArray* il2cpp_linq_list_except(Il2CppObject* list, Il2CppObject* second, int32_t kind);
// Zip: `kind` is the RECEIVER list's element kind and `secondKind` the SECOND
// source's own element kind (IL2CPP_LIST_KIND_*) -- the two may DIFFER
// (List<double>.Zip(int[])), so the runtime must not read the second source
// with the receiver's family. `resKind` is the selector's return family
// (0 int32 / 2 int64 / 3 float / 4 double / 5 uint32).
Il2CppArray* il2cpp_linq_list_zip(Il2CppObject* list, Il2CppObject* second,
                                  Il2CppObject* sel, int32_t kind, int32_t resKind,
                                  int32_t secondKind);

// The UNIFIED Zip entry used by the transpiler for EVERY Zip call. A List
// object and an array are indistinguishable at transpile time (Zip's arity puts
// the selector on top of the stack, and the type tracker names a List<T> local
// after its element array), so the List-vs-array decision is made HERE from the
// object's own descriptor. Each side is materialized at ITS OWN element kind;
// the two families may differ. Reference-element sources pair reference slots.
Il2CppArray* il2cpp_linq_zip_family(Il2CppObject* first, Il2CppObject* second,
                                    Il2CppObject* sel, int32_t firstKind,
                                    int32_t secondKind, int32_t resKind);
bool        il2cpp_linq_list_sequence_equal(Il2CppObject* list, Il2CppObject* second, int32_t kind);

// Ordering over a REFERENCE-element list (List<string>/List<object>/...): the
// snapshot is a reference array, so the reference key-selector surface runs on
// it. The suffix is the KEY family (i4 / i8 / d / f / ref) exactly like the
// array entries; `desc` non-zero flips the order. _ref_f is the impl-orderfam
// float face (the float family has its own reader -- b5-gaps §14).
Il2CppArray* il2cpp_linq_list_order_by_key_ref_i4(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t desc, int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_ref_i8(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t desc, int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_ref_d(Il2CppObject* list, Il2CppObject* keySel,
                                                 int32_t desc, int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_ref_f(Il2CppObject* list, Il2CppObject* keySel,
                                                 int32_t desc, int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_ref_ref(Il2CppObject* list, Il2CppObject* keySel,
                                                   int32_t desc, int32_t kind);

// The DOUBLE-element list key-selector entries: the snapshot is a double array
// and the key crosses in the F-stack slot. The snapshot is taken through the
// list's own accessor, so a List<double> is never cast to an array header.
// _f is the impl-orderfam float-key face (Func<double,float>: the key returns
// in the float slot and is read through the float(*) reader -- b5-gaps §14).
Il2CppArray* il2cpp_linq_list_order_by_key_d(Il2CppObject* list, Il2CppObject* keySel,
                                            int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_desc_d(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_f(Il2CppObject* list, Il2CppObject* keySel,
                                             int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_desc_f(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t kind);

// Ordering (int32 element + int32 key family: the array key-selector surface).
Il2CppArray* il2cpp_linq_list_order_by_key_i4(Il2CppObject* list, Il2CppObject* keySel, int32_t kind);
Il2CppArray* il2cpp_linq_list_order_by_key_desc_i4(Il2CppObject* list, Il2CppObject* keySel, int32_t kind);
Il2CppArray* il2cpp_linq_list_then_by_key_i4(Il2CppObject* list, Il2CppObject* k1,
                                             Il2CppObject* k2, int32_t kind);
Il2CppArray* il2cpp_linq_list_then_by_key_desc_i4(Il2CppObject* list, Il2CppObject* k1,
                                                  Il2CppObject* k2, int32_t kind);

//------------------------------------------------------------------------------
// Source-only aggregates (net48 Sum/Min/Max/Average over the list's elements).
// The suffix is the RESULT family, exactly like the array entries.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_sum_i4(Il2CppObject* list, int32_t kind);
int32_t il2cpp_linq_list_min_i4(Il2CppObject* list, int32_t kind);
int32_t il2cpp_linq_list_max_i4(Il2CppObject* list, int32_t kind);
double  il2cpp_linq_list_average_i4(Il2CppObject* list, int32_t kind);
int64_t il2cpp_linq_list_sum_i8(Il2CppObject* list, int32_t kind);
int64_t il2cpp_linq_list_min_i8(Il2CppObject* list, int32_t kind);
int64_t il2cpp_linq_list_max_i8(Il2CppObject* list, int32_t kind);
double  il2cpp_linq_list_average_i8(Il2CppObject* list, int32_t kind);
double  il2cpp_linq_list_sum_d(Il2CppObject* list, int32_t kind);
double  il2cpp_linq_list_min_d(Il2CppObject* list, int32_t kind);
double  il2cpp_linq_list_max_d(Il2CppObject* list, int32_t kind);
double  il2cpp_linq_list_average_d(Il2CppObject* list, int32_t kind);
float   il2cpp_linq_list_sum_f(Il2CppObject* list, int32_t kind);
float   il2cpp_linq_list_min_f(Il2CppObject* list, int32_t kind);
float   il2cpp_linq_list_max_f(Il2CppObject* list, int32_t kind);
float   il2cpp_linq_list_average_f(Il2CppObject* list, int32_t kind);

// Selector aggregates (Sum/Min/Max/Average(source, selector)) -- the projection
// family is the entry suffix, as on the array surface.
int32_t il2cpp_linq_list_sum_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
int32_t il2cpp_linq_list_min_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
int32_t il2cpp_linq_list_max_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
double  il2cpp_linq_list_average_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
int64_t il2cpp_linq_list_sum_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
int64_t il2cpp_linq_list_min_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
int64_t il2cpp_linq_list_max_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
double  il2cpp_linq_list_average_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
double  il2cpp_linq_list_sum_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
double  il2cpp_linq_list_min_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
double  il2cpp_linq_list_max_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
double  il2cpp_linq_list_average_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
float   il2cpp_linq_list_sum_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
float   il2cpp_linq_list_min_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
float   il2cpp_linq_list_max_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind);
float   il2cpp_linq_list_average_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind);

// Selector aggregates with a DECIMAL projection (net48 Sum/Min/Max/Average(
// source, selector) over a decimal-selector). The 16-byte decimal result rides
// an explicit result slot (the decimal T3 contract: the slot is the FIRST
// parameter), exactly like the array-side decimal aggregates. The element is
// read at the LIST element kind and widened into the selector's first slot.
void il2cpp_linq_list_sum_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  Il2CppObject* sel, int32_t kind);
void il2cpp_linq_list_min_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  Il2CppObject* sel, int32_t kind);
void il2cpp_linq_list_max_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  Il2CppObject* sel, int32_t kind);
void il2cpp_linq_list_average_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                      Il2CppObject* sel, int32_t kind);

//------------------------------------------------------------------------------
// Aggregate (no seed / int32 seed / int64 seed / double seed) over the list.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_aggregate_i4(Il2CppObject* list, Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_i8(Il2CppObject* list, Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_d(Il2CppObject* list, Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4(Il2CppObject* list, int32_t seed,
                                           Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8(Il2CppObject* list, int64_t seed,
                                           Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d(Il2CppObject* list, double seed,
                                          Il2CppObject* func, int32_t kind);

// GroupBy over the list (snapshot + the array grouping kernel; int32 lists).
Il2CppObject* il2cpp_linq_list_group_by_full(Il2CppObject* list,
                                             Il2CppObject* keySel, Il2CppObject* elemSel,
                                             Il2CppObject* cmp, Il2CppObject* resSel,
                                             int32_t elemKind, int32_t rsKind);

} // namespace il2cpp
