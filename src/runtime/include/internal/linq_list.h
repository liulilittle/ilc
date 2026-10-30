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
#include "guid.h"         // Il2CppGuid (the by-value value-struct DefaultIfEmpty forms)
#include "il2cpp_dtoffset.h" // runtime::Il2CppDateTimeOffset
#include "il2cpp_time.h"  // runtime::Il2CppDateTime / Il2CppTimeSpan

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
// impl-loudvalstruct (D): the remaining BY-VALUE VALUE-STRUCT element families.
// The listobj storage BOXES a value element, so the snapshot has to unbox into
// the family's own cell (listobj.h il2cpp_list_obj_to_array_dt/_ts/_guid/
// _dtoffset); before these codes the families fell into IL2CPP_LIST_KIND_REF
// and a boxed POINTER reached a lambda that takes the struct BY VALUE.
constexpr int32_t IL2CPP_LIST_KIND_DT = 13;
constexpr int32_t IL2CPP_LIST_KIND_TS = 14;
constexpr int32_t IL2CPP_LIST_KIND_GUID = 15;
constexpr int32_t IL2CPP_LIST_KIND_DTOFFSET = 16;

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

// DefaultIfEmpty(source, defaultValue) over the list (net48 Enumerable.cs:1010):
// the source when non-empty, else a one-element array holding `value` at the
// element family's REAL cell width. The value rides the element family's own
// parameter slot (int32 for the narrow/int32 cells, int64 for int64, double for
// float/double, the reference slot for a reference element, and the 16-byte
// value for decimal).
Il2CppArray* il2cpp_linq_list_default_if_empty_val_i4(Il2CppObject* list, int32_t value,
                                                      int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_i8(Il2CppObject* list, int64_t value,
                                                      int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_d(Il2CppObject* list, double value,
                                                     int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_ref(Il2CppObject* list, Il2CppObject* value,
                                                       int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_dec(Il2CppObject* list,
                                                       runtime::Il2CppDecimal value,
                                                       int32_t kind);

//------------------------------------------------------------------------------
// Slices / concatenation (family-agnostic: element copies, no comparison).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_take(Il2CppObject* list, int32_t count, int32_t kind);
Il2CppArray* il2cpp_linq_list_skip(Il2CppObject* list, int32_t count, int32_t kind);
Il2CppArray* il2cpp_linq_list_take_while(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_skip_while(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
// The INDEX overloads (net48 TakeWhile(source, Func<TSource,int,bool>) /
// SkipWhile(...)): the predicate receives the element AND its zero-based
// SOURCE index. impl-listvtops added them for the by-value value-struct list
// families; the kind-generic loop also serves every other list kind.
Il2CppArray* il2cpp_linq_list_take_while_idx(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_skip_while_idx(Il2CppObject* list, Il2CppObject* pred, int32_t kind);
Il2CppArray* il2cpp_linq_list_concat(Il2CppObject* list, Il2CppObject* second, int32_t kind);

//------------------------------------------------------------------------------
// Set operators / Zip / SequenceEqual (element equality -- int32 and
// reference families; the array kernels own the semantics).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_distinct(Il2CppObject* list, int32_t kind);
Il2CppArray* il2cpp_linq_list_except(Il2CppObject* list, Il2CppObject* second, int32_t kind);
// impl-listvtops: Union / Intersect over a List receiver (the list domain had
// no entries for them at all). Both normalize the second operand (a List is
// snapshotted at the same element kind) and compare at the kind's real cell
// width (ElemEquals), so the by-value value-struct kinds (13..16) are exact.
Il2CppArray* il2cpp_linq_list_union(Il2CppObject* list, Il2CppObject* second, int32_t kind);
Il2CppArray* il2cpp_linq_list_intersect(Il2CppObject* list, Il2CppObject* second, int32_t kind);
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

// SOURCE-ONLY aggregates over a List<System.Decimal> (net48 Sum/Min/Max/Average
// without a selector). The int32/i8/d/f source-only entries read their own cell
// width, so a decimal list (IL2CPP_LIST_KIND_DEC) went through the int32 reader:
// a 4-byte slice of every 16-byte cell plus an int32 result assigned into a
// decimal slot (a build failure). These read the cell at its real width and keep
// DECIMAL arithmetic; the result rides the explicit T3 slot like the selector
// forms, and an empty source throws for Min/Max/Average (net48).
void il2cpp_linq_list_sum_dec(runtime::Il2CppDecimal* result, Il2CppObject* list, int32_t kind);
void il2cpp_linq_list_min_dec(runtime::Il2CppDecimal* result, Il2CppObject* list, int32_t kind);
void il2cpp_linq_list_max_dec(runtime::Il2CppDecimal* result, Il2CppObject* list, int32_t kind);
void il2cpp_linq_list_average_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  int32_t kind);

//------------------------------------------------------------------------------
// Aggregate (no seed / int32 seed / int64 seed / double seed) over the list.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_aggregate_i4(Il2CppObject* list, Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_i8(Il2CppObject* list, Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_d(Il2CppObject* list, Il2CppObject* func, int32_t kind);
// floatfix: the FLOAT no-seed entry -- the kind=F snapshot is REAL 4-byte
// float cells, so the fold reads them at that width and the accumulator
// rounds to R4 every step (net48 List<float>.Aggregate semantics).
float   il2cpp_linq_list_aggregate_f(Il2CppObject* list, Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4(Il2CppObject* list, int32_t seed,
                                           Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8(Il2CppObject* list, int64_t seed,
                                           Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d(Il2CppObject* list, double seed,
                                          Il2CppObject* func, int32_t kind);
// floatfix: the FLOAT seed entry -- the seed rides the double F stack (a
// float parameter widens exactly) and the accumulator stays float, folding
// through the FAccDFn float(*)(double,double) leaf ABI (a float-returning
// lambda read through a float fn-pointer, per-step R4 rounding).
float   il2cpp_linq_list_aggregate_seed_f(Il2CppObject* list, double seed,
                                          Il2CppObject* func, int32_t kind);
// The (ELEMENT family, ACCUMULATOR family) seed matrix. The entry name is
// seed_<acc>_<elem>: every entry reads the snapshot at the LIST element kind's
// real width and folds through the accumulator family's own lambda ABI.
//   elem: i4 (int32-slot, incl. the 1/2-byte narrow cells), i8, f (4-byte
//         float cells), d (8-byte double cells), ref, dec (16-byte decimal);
//   acc:  i4, i8, f (float return / double parameters), d, dec (result slot).
// int32 accumulator
int32_t il2cpp_linq_list_aggregate_seed_i4_i4(Il2CppObject* list, int32_t seed,
                                              Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4_i8(Il2CppObject* list, int32_t seed,
                                              Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4_f(Il2CppObject* list, int32_t seed,
                                             Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4_d(Il2CppObject* list, int32_t seed,
                                             Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4_ref(Il2CppObject* list, int32_t seed,
                                               Il2CppObject* func, int32_t kind);
int32_t il2cpp_linq_list_aggregate_seed_i4_dec(Il2CppObject* list, int32_t seed,
                                               Il2CppObject* func, int32_t kind);
// int64 accumulator
int64_t il2cpp_linq_list_aggregate_seed_i8_i4(Il2CppObject* list, int64_t seed,
                                              Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8_i8(Il2CppObject* list, int64_t seed,
                                              Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8_f(Il2CppObject* list, int64_t seed,
                                             Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8_d(Il2CppObject* list, int64_t seed,
                                             Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8_ref(Il2CppObject* list, int64_t seed,
                                               Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8_dec(Il2CppObject* list, int64_t seed,
                                               Il2CppObject* func, int32_t kind);
// float accumulator (the seed rides the double F stack)
float   il2cpp_linq_list_aggregate_seed_f_i4(Il2CppObject* list, double seed,
                                             Il2CppObject* func, int32_t kind);
float   il2cpp_linq_list_aggregate_seed_f_i8(Il2CppObject* list, double seed,
                                             Il2CppObject* func, int32_t kind);
float   il2cpp_linq_list_aggregate_seed_f_f(Il2CppObject* list, double seed,
                                            Il2CppObject* func, int32_t kind);
float   il2cpp_linq_list_aggregate_seed_f_d(Il2CppObject* list, double seed,
                                            Il2CppObject* func, int32_t kind);
float   il2cpp_linq_list_aggregate_seed_f_ref(Il2CppObject* list, double seed,
                                              Il2CppObject* func, int32_t kind);
float   il2cpp_linq_list_aggregate_seed_f_dec(Il2CppObject* list, double seed,
                                              Il2CppObject* func, int32_t kind);
// double accumulator
double  il2cpp_linq_list_aggregate_seed_d_i4(Il2CppObject* list, double seed,
                                             Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d_i8(Il2CppObject* list, double seed,
                                             Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d_f(Il2CppObject* list, double seed,
                                            Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d_d(Il2CppObject* list, double seed,
                                            Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d_ref(Il2CppObject* list, double seed,
                                              Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d_dec(Il2CppObject* list, double seed,
                                              Il2CppObject* func, int32_t kind);
// decimal accumulator (the 16-byte result slot is the FIRST parameter)
void    il2cpp_linq_list_aggregate_seed_dec_i4(runtime::Il2CppDecimal* result,
                                               Il2CppObject* list, const runtime::Il2CppDecimal* seed,
                                               Il2CppObject* func, int32_t kind);
void    il2cpp_linq_list_aggregate_seed_dec_i8(runtime::Il2CppDecimal* result,
                                               Il2CppObject* list, const runtime::Il2CppDecimal* seed,
                                               Il2CppObject* func, int32_t kind);
void    il2cpp_linq_list_aggregate_seed_dec_f(runtime::Il2CppDecimal* result,
                                              Il2CppObject* list, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, int32_t kind);
void    il2cpp_linq_list_aggregate_seed_dec_d(runtime::Il2CppDecimal* result,
                                              Il2CppObject* list, const runtime::Il2CppDecimal* seed,
                                              Il2CppObject* func, int32_t kind);
void    il2cpp_linq_list_aggregate_seed_dec_ref(runtime::Il2CppDecimal* result,
                                                Il2CppObject* list, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, int32_t kind);
void    il2cpp_linq_list_aggregate_seed_dec_dec(runtime::Il2CppDecimal* result,
                                                Il2CppObject* list, const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, int32_t kind);
// reference accumulator
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_i4(Il2CppObject* list, Il2CppObject* seed,
                                                     Il2CppObject* func, int32_t kind);
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_i8(Il2CppObject* list, Il2CppObject* seed,
                                                     Il2CppObject* func, int32_t kind);
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_f(Il2CppObject* list, Il2CppObject* seed,
                                                    Il2CppObject* func, int32_t kind);
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_d(Il2CppObject* list, Il2CppObject* seed,
                                                    Il2CppObject* func, int32_t kind);
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_ref(Il2CppObject* list, Il2CppObject* seed,
                                                      Il2CppObject* func, int32_t kind);
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_dec(Il2CppObject* list, Il2CppObject* seed,
                                                      Il2CppObject* func, int32_t kind);

// Aggregate(seed, func) over a NON-decimal VALUE-TYPE ARRAY source
// (DateTime / TimeSpan / Guid) with an int32/int64/float/double accumulator.
// The cells are read at their real width (8 / 8 / 16 bytes) and the lambda is
// called with the value struct BY VALUE. Defined in linq_list.cpp (the
// value-cell readers are exported by linq_family.h).
int32_t il2cpp_linq_aggregate_seed_dt_i4(Il2CppArray* src, int32_t seed, Il2CppObject* func);
int64_t il2cpp_linq_aggregate_seed_dt_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func);
float   il2cpp_linq_aggregate_seed_dt_f(Il2CppArray* src, float seed, Il2CppObject* func);
double  il2cpp_linq_aggregate_seed_dt_d(Il2CppArray* src, double seed, Il2CppObject* func);
int32_t il2cpp_linq_aggregate_seed_ts_i4(Il2CppArray* src, int32_t seed, Il2CppObject* func);
int64_t il2cpp_linq_aggregate_seed_ts_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func);
float   il2cpp_linq_aggregate_seed_ts_f(Il2CppArray* src, float seed, Il2CppObject* func);
double  il2cpp_linq_aggregate_seed_ts_d(Il2CppArray* src, double seed, Il2CppObject* func);
int32_t il2cpp_linq_aggregate_seed_guid_i4(Il2CppArray* src, int32_t seed, Il2CppObject* func);
int64_t il2cpp_linq_aggregate_seed_guid_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func);
float   il2cpp_linq_aggregate_seed_guid_f(Il2CppArray* src, float seed, Il2CppObject* func);
double  il2cpp_linq_aggregate_seed_guid_d(Il2CppArray* src, double seed, Il2CppObject* func);

// DefaultIfEmpty(source, defaultValue) over an ARRAY source of any element
// family (net48 Enumerable.cs:1010). The source is returned as-is when it is
// non-empty; otherwise a one-element array is allocated at the family's REAL
// cell width and `value` is written into it. `cell` is the IL2CPP_LINQ_CELL_*
// code of the source element (linq_family.h); the value crosses in the family's
// own parameter slot (int32 / int64 / double for float+double / reference).
// Defined in linq_list.cpp next to the other ARRAY value-family kernels so the
// generated code needs no extra header.
Il2CppArray* il2cpp_linq_default_if_empty_family_i4(Il2CppArray* src, int32_t value,
                                                    int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family_i8(Il2CppArray* src, int64_t value,
                                                    int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family_d(Il2CppArray* src, double value,
                                                   int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family_ref(Il2CppArray* src, Il2CppObject* value,
                                                     int32_t cell);
// impl-loudvalstruct (B): the BY-VALUE VALUE-STRUCT forms (System.DateTime /
// System.TimeSpan / System.Guid / System.DateTimeOffset). The default crosses in
// the struct's own by-value ABI and is written into the cell at its real width
// (DateTime: the 62-bit ticks + 2-bit kind ARRAY image; TimeSpan: the ticks;
// Guid: the 16-byte payload; DateTimeOffset: the 16-byte instant+offset).
Il2CppArray* il2cpp_linq_default_if_empty_family_dt(Il2CppArray* src,
                                                    runtime::Il2CppDateTime value,
                                                    int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family_ts(Il2CppArray* src,
                                                    runtime::Il2CppTimeSpan value,
                                                    int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family_guid(Il2CppArray* src, Il2CppGuid value,
                                                      int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family_dtoffset(
    Il2CppArray* src, runtime::Il2CppDateTimeOffset value, int32_t cell);

// impl-loudvalstruct (D): the LIST value-struct forms (List<DateTime> /
// List<TimeSpan> / List<Guid> / List<DateTimeOffset>). The list snapshot
// unboxes the elements into the family's own cell, and the caller's default is
// written at that width. `kind` is the IL2CPP_LIST_KIND_* code.
Il2CppArray* il2cpp_linq_list_default_if_empty_val_dt(Il2CppObject* list,
                                                      runtime::Il2CppDateTime value,
                                                      int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_ts(Il2CppObject* list,
                                                      runtime::Il2CppTimeSpan value,
                                                      int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_guid(Il2CppObject* list, Il2CppGuid value,
                                                        int32_t kind);
Il2CppArray* il2cpp_linq_list_default_if_empty_val_dtoffset(
    Il2CppObject* list, runtime::Il2CppDateTimeOffset value, int32_t kind);

//------------------------------------------------------------------------------
// impl-listvtops: the BY-VALUE VALUE-STRUCT list operator surface
// (List<System.DateTime> / List<System.TimeSpan> / List<System.Guid> /
// List<System.DateTimeOffset>, IL2CPP_LIST_KIND_DT/TS/GUID/DTOFFSET 13..16).
//
// The list storage BOXES the element, and the list snapshot unboxes it into
// the family's OWN cell (8 / 8 / 16 / 16 bytes). The generated predicate /
// selector / accumulator leaf takes the value struct BY VALUE, so every entry
// below reads the snapshot cell at its real width and invokes the delegate
// through that exact signature -- the int32/i8/double/reference list entries
// would read a 4-byte slice out of an 8/16-byte cell AND call the leaf through
// a foreign ABI (a silent wrong value / a stack-corrupting call).
//
// The predicate operators (Where/All/Count(pred)/Any(pred)/TakeWhile/
// SkipWhile and the *Pred element accessors) ride the existing kind-generic
// list entries: PredicateOn / PredicateOnIdx dispatch the four kinds to the
// by-value invokers.
//
// The entry suffix is the ELEMENT family, exactly like the array surface:
//   _dt  System.DateTime      (Il2CppDateTime)
//   _ts  System.TimeSpan      (Il2CppTimeSpan)
//   _guid System.Guid         (Il2CppGuid, returned BY VALUE)
//   _dtoffset System.DateTimeOffset (Il2CppDateTimeOffset, BY VALUE)
//------------------------------------------------------------------------------
#define IL2CPP_DECL_LST_VT_ACCESSORS(FAM, TYPE)                                          \
    TYPE il2cpp_linq_list_first_##FAM(Il2CppObject* list, int32_t kind);                 \
    TYPE il2cpp_linq_list_first_or_default_##FAM(Il2CppObject* list, int32_t kind);      \
    TYPE il2cpp_linq_list_last_##FAM(Il2CppObject* list, int32_t kind);                  \
    TYPE il2cpp_linq_list_last_or_default_##FAM(Il2CppObject* list, int32_t kind);       \
    TYPE il2cpp_linq_list_single_##FAM(Il2CppObject* list, int32_t kind);                \
    TYPE il2cpp_linq_list_single_or_default_##FAM(Il2CppObject* list, int32_t kind);     \
    TYPE il2cpp_linq_list_element_at_##FAM(Il2CppObject* list, int32_t index,            \
                                           int32_t kind);                                \
    TYPE il2cpp_linq_list_element_at_or_default_##FAM(Il2CppObject* list, int32_t index, \
                                                      int32_t kind);                     \
    TYPE il2cpp_linq_list_first_pred_##FAM(Il2CppObject* list, Il2CppObject* pred,       \
                                           int32_t kind);                                \
    TYPE il2cpp_linq_list_first_or_default_pred_##FAM(Il2CppObject* list,                \
                                                      Il2CppObject* pred, int32_t kind); \
    TYPE il2cpp_linq_list_last_pred_##FAM(Il2CppObject* list, Il2CppObject* pred,        \
                                          int32_t kind);                                 \
    TYPE il2cpp_linq_list_last_or_default_pred_##FAM(Il2CppObject* list,                 \
                                                     Il2CppObject* pred, int32_t kind);  \
    bool il2cpp_linq_list_contains_##FAM(Il2CppObject* list, TYPE value, int32_t kind);  \
    TYPE il2cpp_linq_list_aggregate_##FAM(Il2CppObject* list, Il2CppObject* func,        \
                                          int32_t kind);

IL2CPP_DECL_LST_VT_ACCESSORS(dt, runtime::Il2CppDateTime)
IL2CPP_DECL_LST_VT_ACCESSORS(ts, runtime::Il2CppTimeSpan)
IL2CPP_DECL_LST_VT_ACCESSORS(guid, Il2CppGuid)
IL2CPP_DECL_LST_VT_ACCESSORS(dtoffset, runtime::Il2CppDateTimeOffset)

#undef IL2CPP_DECL_LST_VT_ACCESSORS

//------------------------------------------------------------------------------
// Aggregate(seed, func) / Aggregate(seed, func, resultSelector) over a List of
// a BY-VALUE VALUE-STRUCT element family. The element family rides the `kind`
// argument (the snapshot cell is 8 / 8 / 16 / 16 bytes), so ONE entry per
// ACCUMULATOR family serves all four element families -- the `_val` tag is the
// element-family-generic counterpart of the per-family i4/i8/f/d/ref/dec
// element entries above. The accumulator keeps its OWN family and its own leaf
// ABI; the result selector runs ONCE on the final accumulator and its RESULT
// carrier follows the entry suffix, exactly like the 6x6x10 matrix above.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_aggregate_seed_i4_val(Il2CppObject* list, int32_t seed,
                                               Il2CppObject* func, int32_t kind);
int64_t il2cpp_linq_list_aggregate_seed_i8_val(Il2CppObject* list, int64_t seed,
                                               Il2CppObject* func, int32_t kind);
float   il2cpp_linq_list_aggregate_seed_f_val(Il2CppObject* list, double seed,
                                              Il2CppObject* func, int32_t kind);
double  il2cpp_linq_list_aggregate_seed_d_val(Il2CppObject* list, double seed,
                                              Il2CppObject* func, int32_t kind);
void    il2cpp_linq_list_aggregate_seed_dec_val(runtime::Il2CppDecimal* result,
                                                Il2CppObject* list,
                                                const runtime::Il2CppDecimal* seed,
                                                Il2CppObject* func, int32_t kind);
Il2CppObject* il2cpp_linq_list_aggregate_seed_ref_val(Il2CppObject* list,
                                                      Il2CppObject* seed,
                                                      Il2CppObject* func, int32_t kind);

#define IL2CPP_DECL_LST_AGG_VAL_RS(ACC, ELEM, SEEDTYPE)                                  \
    int32_t il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_i4(                      \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    int64_t il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_i8(                      \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    double il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_f(                        \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    double il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_d(                        \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    Il2CppObject* il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_ref(               \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    void il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_dec(                        \
        runtime::Il2CppDecimal* result, Il2CppObject* list, SEEDTYPE seed,               \
        Il2CppObject* func, Il2CppObject* resSel, int32_t kind);                         \
    runtime::Il2CppDateTime il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_dt(      \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    runtime::Il2CppTimeSpan il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_ts(      \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    Il2CppGuid il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_guid(                 \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                           \
        Il2CppObject* resSel, int32_t kind);                                             \
    runtime::Il2CppDateTimeOffset                                                       \
        il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_dtoffset(                    \
            Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                       \
            Il2CppObject* resSel, int32_t kind);

IL2CPP_DECL_LST_AGG_VAL_RS(i4, val, int32_t)
IL2CPP_DECL_LST_AGG_VAL_RS(i8, val, int64_t)
IL2CPP_DECL_LST_AGG_VAL_RS(f, val, double)
IL2CPP_DECL_LST_AGG_VAL_RS(d, val, double)
IL2CPP_DECL_LST_AGG_VAL_RS(dec, val, const runtime::Il2CppDecimal*)
IL2CPP_DECL_LST_AGG_VAL_RS(ref, val, Il2CppObject*)

#undef IL2CPP_DECL_LST_AGG_VAL_RS

//------------------------------------------------------------------------------
// Aggregate(seed, func, resultSelector) over a List<T> (net48
// Enumerable.Aggregate<TSource,TAccumulate,TResult> -- Enumerable.cs:1411).
//
// The fold is the (accumulator, element) seed matrix above; the result selector
// then runs ONCE on the final accumulator. Its ABI depends ONLY on the
// ACCUMULATOR family (it takes the accumulator) and the RESULT family decides
// the carrier, so the entry set is the 6x6 fold matrix crossed with a 10-way
// projection: 6 accumulator families x 6 element families x 10 result families
// = 360 entries, all generated from the declaration macros below (the seed type
// is the accumulator family's; the return is the RESULT family's).
//
// The entry name is il2cpp_linq_list_aggregate_seed_<acc>_<elem>_rs_<res>.
//
// impl-loudvalstruct (C): the BY-VALUE VALUE-STRUCT results (dt / ts / guid /
// dtoffset) join the projection. They ride the same by-value return convention
// the family element accessors already use (Il2CppDateTime / Il2CppTimeSpan /
// Il2CppGuid / Il2CppDateTimeOffset returned in registers), so no new ABI is
// needed -- only the entries. Before this they were a transpile-time LOUD even
// though the selector leaf returns exactly that struct.
//------------------------------------------------------------------------------
#define IL2CPP_DECL_LST_AGG_RS(ACC, ELEM, SEEDTYPE)                                      \
    int32_t il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_i4(                       \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    int64_t il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_i8(                       \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    double il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_f(                         \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    double il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_d(                         \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    Il2CppObject* il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_ref(                \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    void il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_dec(                         \
        runtime::Il2CppDecimal* result, Il2CppObject* list, SEEDTYPE seed,                \
        Il2CppObject* func, Il2CppObject* resSel, int32_t kind);                          \
    runtime::Il2CppDateTime il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_dt(       \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    runtime::Il2CppTimeSpan il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_ts(       \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    Il2CppGuid il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_guid(                  \
        Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                            \
        Il2CppObject* resSel, int32_t kind);                                              \
    runtime::Il2CppDateTimeOffset                                                        \
        il2cpp_linq_list_aggregate_seed_##ACC##_##ELEM##_rs_dtoffset(                     \
            Il2CppObject* list, SEEDTYPE seed, Il2CppObject* func,                        \
            Il2CppObject* resSel, int32_t kind);

#define IL2CPP_DECL_LST_AGG_RS_ROW(ACC, SEEDTYPE)                                        \
    IL2CPP_DECL_LST_AGG_RS(ACC, i4, SEEDTYPE)                                            \
    IL2CPP_DECL_LST_AGG_RS(ACC, i8, SEEDTYPE)                                            \
    IL2CPP_DECL_LST_AGG_RS(ACC, f, SEEDTYPE)                                             \
    IL2CPP_DECL_LST_AGG_RS(ACC, d, SEEDTYPE)                                             \
    IL2CPP_DECL_LST_AGG_RS(ACC, ref, SEEDTYPE)                                           \
    IL2CPP_DECL_LST_AGG_RS(ACC, dec, SEEDTYPE)

IL2CPP_DECL_LST_AGG_RS_ROW(i4, int32_t)
IL2CPP_DECL_LST_AGG_RS_ROW(i8, int64_t)
IL2CPP_DECL_LST_AGG_RS_ROW(f, double)
IL2CPP_DECL_LST_AGG_RS_ROW(d, double)
IL2CPP_DECL_LST_AGG_RS_ROW(dec, const runtime::Il2CppDecimal*)
IL2CPP_DECL_LST_AGG_RS_ROW(ref, Il2CppObject*)

#undef IL2CPP_DECL_LST_AGG_RS
#undef IL2CPP_DECL_LST_AGG_RS_ROW

// impl-vtsel: the value-struct List keyed sorts (snapshot -> array struct kernels).
Il2CppArray* il2cpp_linq_list_order_by_key_vt(Il2CppObject* list, Il2CppObject* keySel,
                                              int32_t desc, int32_t kind, int32_t keyFam);
Il2CppArray* il2cpp_linq_list_then_by_key_vt(Il2CppObject* list, Il2CppObject* k1,
                                             Il2CppObject* k2, int32_t k1Desc,
                                             int32_t k2Desc, int32_t kind,
                                             int32_t k1Fam, int32_t k2Fam);
// impl-vtsel: GroupBy over a List with an EXPLICIT snapshot family (the
// pre-existing entry always snapshotted through the int32 kind).
Il2CppObject* il2cpp_linq_list_group_by_full_kind(Il2CppObject* list,
                                                  Il2CppObject* keySel, Il2CppObject* elemSel,
                                                  Il2CppObject* cmp, Il2CppObject* resSel,
                                                  int32_t elemKind, int32_t rsKind,
                                                  int32_t kind, int32_t keyKind,
                                                  int32_t srcKind);
// GroupBy over the list (snapshot + the array grouping kernel). `kind` is the
// receiver's IL2CPP_LIST_KIND_* element family: the snapshot MUST be taken at
// that family's real cell width (the pre-impl-listselnum body snapshotted every
// list as IL2CPP_LIST_KIND_I4, so an int64/float/double/reference source was
// read as int32). `keyKind` is the KeyKindFor code of TKey (0 = the int32 slot).
Il2CppObject* il2cpp_linq_list_group_by_full(Il2CppObject* list,
                                             Il2CppObject* keySel, Il2CppObject* elemSel,
                                             Il2CppObject* cmp, Il2CppObject* resSel,
                                             int32_t elemKind, int32_t rsKind,
                                             int32_t kind, int32_t keyKind);

} // namespace il2cpp

