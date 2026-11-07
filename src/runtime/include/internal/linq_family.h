#pragma once
//------------------------------------------------------------------------------
// linq_family.h -- the UNIFIED CELL ABI for the LINQ element families.
//
// HISTORY: the LINQ runtime grew one hand-written kernel per element family
// (i4 / i8 / f / d / narrow / ref / decimal), so every operator needed N
// entries and the transpiler needed an N-way dispatch. The remaining gaps
// (Take/Skip/Distinct/Reverse/Concat/Except/Contains/SequenceEqual/ElementAt/
// DefaultIfEmpty/LongCount/Union/Intersect/Single/OrderBy/ThenBy/GroupBy/
// Aggregate/Zip over the value families) would have multiplied that surface by
// N. This header introduces ONE cell-descriptor space and ONE set of
// family-generic kernels: a caller passes the cell code as a trailing literal
// and the kernel reads/writes the element at its REAL width.
//
// CELL CODES (the transpiler's single authority; a trailing literal, never a
// consumed IL argument):
//     0  sbyte      1  byte       2  bool       3  short
//     4  ushort     5  char       6  uint       7  int32
//     8  int64      9  float      10 double     11 decimal
//     12 DateTime   13 TimeSpan   14 Guid       15 reference
//     16 DateTimeOffset (delta 2 -- the 16-byte Il2CppDateTimeOffset value)
//
// The 0..6 range is byte-identical to IL2CPP_LINQ_NARROW_* (linq.h) so the
// narrow kernels keep their existing codes; 7..10 reuses the IL2CPP_LINQ_ZIP_*
// I4/I8/F/D values. This is deliberate: the same integer means the same cell
// in every LINQ entry point.
//
// DELEGATE ABI CLASSES (what the generated leaf's C++ signature really is --
// CodeGenerator.ParamTypesCpp): the cell determines how the kernel WIDENS an
// element into the callback's parameter slot:
//     i32 slot  -- sbyte/byte/bool/short/ushort/char/uint/int32
//     i64 slot  -- int64
//     dbl slot  -- float/double            (the F-stack carrier: float -> double)
//     ref slot  -- reference                (Il2CppObject*)
//     by value  -- decimal (Il2CppDecimal), DateTime (Il2CppDateTime),
//                  TimeSpan (Il2CppTimeSpan), Guid (Il2CppGuid) -- the
//                  generated thunk takes the struct BY VALUE, so the kernel
//                  must call it through that exact signature.
//------------------------------------------------------------------------------

#include "decimal.h"
#include "guid.h"
#include "il2cpp_dtoffset.h"
#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_time.h"

#include <cstdint>

namespace il2cpp {

// The cell codes (see the header comment).
constexpr int32_t IL2CPP_LINQ_CELL_SBYTE    = 0;
constexpr int32_t IL2CPP_LINQ_CELL_BYTE     = 1;
constexpr int32_t IL2CPP_LINQ_CELL_BOOL     = 2;
constexpr int32_t IL2CPP_LINQ_CELL_SHORT    = 3;
constexpr int32_t IL2CPP_LINQ_CELL_USHORT   = 4;
constexpr int32_t IL2CPP_LINQ_CELL_CHAR     = 5;
constexpr int32_t IL2CPP_LINQ_CELL_UINT     = 6;
constexpr int32_t IL2CPP_LINQ_CELL_I4       = 7;
constexpr int32_t IL2CPP_LINQ_CELL_I8       = 8;
constexpr int32_t IL2CPP_LINQ_CELL_F        = 9;
constexpr int32_t IL2CPP_LINQ_CELL_D        = 10;
constexpr int32_t IL2CPP_LINQ_CELL_DECIMAL  = 11;
constexpr int32_t IL2CPP_LINQ_CELL_DATETIME = 12;
constexpr int32_t IL2CPP_LINQ_CELL_TIMESPAN = 13;
constexpr int32_t IL2CPP_LINQ_CELL_GUID     = 14;
constexpr int32_t IL2CPP_LINQ_CELL_REF      = 15;
constexpr int32_t IL2CPP_LINQ_CELL_DTOFFSET = 16;

// The byte width of a cell (the payload stride of its arrays).
int32_t LinqCellSize(int32_t cell);

// True when the cell's elements are GC references (scanned payload).
bool LinqCellIsRef(int32_t cell);

// True when the cell's delegate ABI is the int32 parameter slot.
bool LinqCellIsI4Slot(int32_t cell);

// The element address (bounds-checked) of element `index`.
char* LinqCellPtr(Il2CppArray* arr, int32_t index);

// Widen element `index` into the callback slot family. The i4 read is the
// CLR's narrow-parameter widening (sign- or zero-extension per cell), never a
// 4-byte over-read of a 1/2-byte cell.
int32_t       LinqCellReadI4(Il2CppArray* arr, int32_t index, int32_t cell);
int64_t       LinqCellReadI8(Il2CppArray* arr, int32_t index, int32_t cell);
double        LinqCellReadD(Il2CppArray* arr, int32_t index, int32_t cell);
Il2CppObject* LinqCellReadRef(Il2CppArray* arr, int32_t index, int32_t cell);

// The by-value struct reads (the 4 struct cells only; a caller must have
// already dispatched on the cell).
runtime::Il2CppDecimal  LinqCellReadDec(Il2CppArray* arr, int32_t index);
runtime::Il2CppDateTime LinqCellReadDt(Il2CppArray* arr, int32_t index);
runtime::Il2CppTimeSpan LinqCellReadTs(Il2CppArray* arr, int32_t index);
Il2CppGuid              LinqCellReadGuid(Il2CppArray* arr, int32_t index);
// impl-listvtops: the 16-byte System.DateTimeOffset cell (delta 2). The ARRAY
// surface had no reader at all, so a DateTimeOffset[] source dispatched its
// operators into the Guid kernels (a 16-byte cell of the WRONG family).
runtime::Il2CppDateTimeOffset LinqCellReadDto(Il2CppArray* arr, int32_t index);

// The family default (default(T)) into `dst` (cell-width bytes).
void LinqCellDefault(char* dst, int32_t cell);

// Copy element `index` of `src` into `dst` at the cell's own width (a
// reference copy runs the write barrier).
void LinqCellCopy(char* dst, Il2CppObject** barrierSlot, Il2CppArray* src,
                  int32_t index, int32_t cell);

// EqualityComparer<T>.Default equality between two elements of the same cell.
bool LinqCellEquals(Il2CppArray* a, int32_t ia, Il2CppArray* b, int32_t ib,
                    int32_t cell);

// Equality between an element and a caller-supplied value in the cell's
// widened slot (Contains).
bool LinqCellEqualsI4(Il2CppArray* a, int32_t ia, int32_t value, int32_t cell);
bool LinqCellEqualsI8(Il2CppArray* a, int32_t ia, int64_t value, int32_t cell);
bool LinqCellEqualsD(Il2CppArray* a, int32_t ia, double value, int32_t cell);
bool LinqCellEqualsRef(Il2CppArray* a, int32_t ia, Il2CppObject* value, int32_t cell);
bool LinqCellEqualsDec(Il2CppArray* a, int32_t ia, runtime::Il2CppDecimal value);
bool LinqCellEqualsDt(Il2CppArray* a, int32_t ia, runtime::Il2CppDateTime value);
bool LinqCellEqualsTs(Il2CppArray* a, int32_t ia, runtime::Il2CppTimeSpan value);
bool LinqCellEqualsGuid(Il2CppArray* a, int32_t ia, Il2CppGuid value);
// impl-listvtops: the DateTimeOffset value form (equality compares the UTC
// instant -- net48 DateTimeOffset.Equals).
bool LinqCellEqualsDto(Il2CppArray* a, int32_t ia, runtime::Il2CppDateTimeOffset value);

// Validate that `arr` is a one-dimensional array whose element width matches
// the cell (a mis-dispatched call must never read a narrower cell as a wider
// one). Null -> ArgumentNullException; a mismatch -> NotSupportedException.
Il2CppArray* LinqRequireCell(Il2CppArray* arr, int32_t cell);

// Allocate a fresh array of `length` elements of the cell's own layout.
Il2CppArray* LinqNewArray(int32_t cell, int32_t length);

//------------------------------------------------------------------------------
// FAMILY-GENERIC ELEMENT OPERATORS
//
// Every entry below takes the source ARRAY plus the cell code as the trailing
// argument and materializes eagerly (the stage-1 AOT form). They are the
// all-family successors of the per-family kernels in linq.cpp / linq_map.cpp:
//   Take / Skip / Reverse / Concat / Distinct / Except / Union / Intersect
//   DefaultIfEmpty / LongCount
// The element-RETURNING operators are generated per result ABI (see the
// macros below) because the C++ return type differs per family.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_take_family(Il2CppArray* src, int32_t count, int32_t cell);
Il2CppArray* il2cpp_linq_skip_family(Il2CppArray* src, int32_t count, int32_t cell);
Il2CppArray* il2cpp_linq_reverse_family(Il2CppArray* src, int32_t cell);
Il2CppArray* il2cpp_linq_concat_family(Il2CppArray* first, Il2CppArray* second, int32_t cell);
Il2CppArray* il2cpp_linq_distinct_family(Il2CppArray* src, int32_t cell);
Il2CppArray* il2cpp_linq_except_family(Il2CppArray* first, Il2CppArray* second, int32_t cell);
Il2CppArray* il2cpp_linq_union_family(Il2CppArray* first, Il2CppArray* second, int32_t cell);
Il2CppArray* il2cpp_linq_intersect_family(Il2CppArray* first, Il2CppArray* second, int32_t cell);
Il2CppArray* il2cpp_linq_default_if_empty_family(Il2CppArray* src, int32_t cell);
int64_t      il2cpp_linq_long_count_family(Il2CppArray* src, int32_t cell);
bool         il2cpp_linq_sequence_equal_family(Il2CppArray* first, Il2CppArray* second, int32_t cell);

// b5-gaps section 56: a USER IEqualityComparer<T> over a REFERENCE element /
// key. Rides the string-comparer singleton fast path then the reflected
// "(r:" Equals slot; falls back to icall_object_equals so a default (null)
// comparer never changes behavior. Shared by the linq_family kernels and the
// groupby key-equality fold (linq_map.cpp).
bool LinqRefEqualsCmp(Il2CppObject* cmp, Il2CppObject* a, Il2CppObject* b);

// Contains: the value rides the family's own parameter slot.
bool il2cpp_linq_contains_family_i4(Il2CppArray* src, int32_t value, int32_t cell);
bool il2cpp_linq_contains_family_i8(Il2CppArray* src, int64_t value, int32_t cell);
bool il2cpp_linq_contains_family_d(Il2CppArray* src, double value, int32_t cell);
bool il2cpp_linq_contains_family_ref(Il2CppArray* src, Il2CppObject* value, int32_t cell);

// b5-gaps section 56: the user-comparer twins of the reference-element set /
// sequence operators. `cmp` is nullable (null == the default equality).
Il2CppArray* il2cpp_linq_distinct_family_cmp(Il2CppArray* src, Il2CppObject* cmp, int32_t cell);
Il2CppArray* il2cpp_linq_except_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                           Il2CppObject* cmp, int32_t cell);
Il2CppArray* il2cpp_linq_union_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                          Il2CppObject* cmp, int32_t cell);
Il2CppArray* il2cpp_linq_intersect_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                              Il2CppObject* cmp, int32_t cell);
bool         il2cpp_linq_sequence_equal_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                                   Il2CppObject* cmp, int32_t cell);
bool         il2cpp_linq_contains_family_ref_cmp(Il2CppArray* src, Il2CppObject* value,
                                                 Il2CppObject* cmp, int32_t cell);

// b5-gaps section 77: Contains(value, comparer) over the 16-BYTE value
// families (decimal / DateTime / TimeSpan / Guid / DateTimeOffset). The value
// crosses by-value in the family's OWN parameter slot; the comparer's
// reflected Equals(TKey,TKey) slot governs membership.
bool il2cpp_linq_contains_family_cmp_dec(Il2CppArray* src, runtime::Il2CppDecimal value,
                                         Il2CppObject* cmp);
bool il2cpp_linq_contains_family_cmp_dt(Il2CppArray* src, runtime::Il2CppDateTime value,
                                        Il2CppObject* cmp);
bool il2cpp_linq_contains_family_cmp_ts(Il2CppArray* src, runtime::Il2CppTimeSpan value,
                                        Il2CppObject* cmp);
bool il2cpp_linq_contains_family_cmp_guid(Il2CppArray* src, Il2CppGuid value,
                                          Il2CppObject* cmp);
bool il2cpp_linq_contains_family_cmp_dtoffset(Il2CppArray* src,
                                              runtime::Il2CppDateTimeOffset value,
                                              Il2CppObject* cmp);

// b5-gaps section 78: Enumerable.ToDictionary over an ARRAY source. keySel is
// Func<TSource,TKey>; elemSel is Func<TSource,TElement> or null (value = the
// element itself). keyKind/valueKind are the dictgen kind codes (Int32=1 /
// String=0). The result is a Managed Dictionary`2 wrapper whose insertion-
// order table feeds the instance get_Item/get_Count/get_Keys/ContainsKey
// icalls; the wrapper roots every reference key and value in a managed pack
// (the dictgen table is a raw allocation the collector cannot see).
Il2CppObject* il2cpp_linq_to_dictionary(Il2CppArray* src, Il2CppObject* keySel,
                                        Il2CppObject* elemSel, int32_t keyKind,
                                        int32_t valueKind, int32_t srcCell);

// b5-gaps section 57: user-comparer twins over the VALUE cell families
// (int32/uint/narrow/char/bool, int64, double/float). Each entry dispatches a
// user IEqualityComparer<T>.Equals through the reflected slot of the element
// family (byte -> "(u1,u1)", ..., long "(l,l)", double "(d,d)", float "(f,f)"
// riding the double ABI like the sort comparers). `cmp` is nullable (null ==
// the default cell equality); the ref cell is serviced by section 56.
// bool-lit cells are equals; the set/sequence kernels keep first-appearance /
// per-position semantics.
Il2CppArray* il2cpp_linq_distinct_value_cmp(Il2CppArray* src, Il2CppObject* cmp, int32_t cell);
Il2CppArray* il2cpp_linq_except_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                          Il2CppObject* cmp, int32_t cell);
Il2CppArray* il2cpp_linq_union_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                         Il2CppObject* cmp, int32_t cell);
Il2CppArray* il2cpp_linq_intersect_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                             Il2CppObject* cmp, int32_t cell);
bool         il2cpp_linq_sequence_equal_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                                  Il2CppObject* cmp, int32_t cell);
bool         il2cpp_linq_contains_value_cmp_i4(Il2CppArray* src, int32_t value,
                                               Il2CppObject* cmp, int32_t cell);
bool         il2cpp_linq_contains_value_cmp_i8(Il2CppArray* src, int64_t value,
                                               Il2CppObject* cmp, int32_t cell);
bool         il2cpp_linq_contains_value_cmp_d(Il2CppArray* src, double value,
                                              Il2CppObject* cmp, int32_t cell);

// b5-gaps section 59: Enumerable.SelectMany(source, collectionSelector) --
// the selector (per-SOURCE-cell ABI, returning Il2CppObject*) flattens the
// outer sequence into a fresh RESULT-cell array. Inner collections must be
// ARRAYS at runtime (the List/IEnumerable inner is a registered LOUD for this
// section). The source / inner-element cells ride trailing literals.
Il2CppArray* il2cpp_linq_select_many(Il2CppArray* src, Il2CppObject* sel,
                                     int32_t srcCell, int32_t resCell);

// b5-gaps section 61: Enumerable.SelectMany(source, collectionSelector,
// resultSelector) over ARRAY sources whose collectionSelector returns ARRAYS.
// Pass 1 caches each inner (reserved vector + per-slot root, the section-59
// shape); pass 2 invokes the TWO-argument resultSelector per (outer, inner)
// pair through a pooled 64-entry ABI table (arg1 per srcCell, arg2 per
// collCell -- i4/i8/double/reference slots; return width per resCell --
// i4-family/i8/float/double/reference); pass 3 copies the collected carriers
// into a fresh resCell array. Using a single erased int64 signature, the
// table's callees sign-extend int32 results, keep int64 raw, and memcpy
// double bit patterns, so the dispatch stays a clean indexed call.
Il2CppArray* il2cpp_linq_select_many3(Il2CppArray* src, Il2CppObject* collSel,
                                      Il2CppObject* resSel, int32_t srcCell,
                                      int32_t collCell, int32_t resCell);

// b5-gaps section 62: Enumerable.Join(outer, inner, outerKeySelector,
// innerKeySelector, resultSelector) over ARRAY sources. Pass 1 indexes the
// inner sequence by TKey (per-key-cell carrier/reference hashing; reference
// TKeys live only for this call and are held by a per-slot rooted vector);
// pass 2 collects (outer, inner) match pairs as pure int indices; pass 3
// invokes the two-argument result selector once per pair through the
// section-61 table and materializes the exact-size TResult array. The outer /
// inner element cells, the TKey cell and the TResult cell ride trailing
// literals. List/IEnumerable sources are rejected loudly by the D-2 guard.
Il2CppArray* il2cpp_linq_join(Il2CppArray* outer, Il2CppArray* inner,
                              Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                              Il2CppObject* resSel, int32_t outerCell,
                              int32_t innerCell, int32_t keyCell, int32_t resCell);

// b5-gaps section 63: Enumerable.GroupJoin(outer, inner, outerKeySelector,
// innerKeySelector, resultSelector) over ARRAY sources -- the LEFT-JOIN
// shape. The pass-1 TKey index is identical to section 62; pass 2 walks the
// outer sequence and for EVERY outer element materializes a fresh
// innerCell-typed GROUP array (the matches in inner insertion order, rooted
// for the result-selector call), invokes the two-argument result selector
// (outer element, group array) and writes the result straight into the
// preallocated exact-size TResult array. List/IEnumerable sources are
// rejected loudly by the D-2 guard.
Il2CppArray* il2cpp_linq_group_join(Il2CppArray* outer, Il2CppArray* inner,
                                    Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                                    Il2CppObject* resSel, int32_t outerCell,
                                    int32_t innerCell, int32_t keyCell, int32_t resCell);

// b5-gaps section 64: Enumerable.Join(..., IEqualityComparer<TKey>) over
// ARRAY sources with REFERENCE TKeys. The comparer hashes and compares the
// keys through a per-comparer ops pair (StringComparer singletons take a
// direct weighted fold; everything else rides the reflected virtual
// GetHashCode/Equals(T,T) slots), and a null comparer falls back to the
// section-62 default carrier/hash/equality. The indexing/collection skeleton
// is the section-62 one; reference TKeys only live for the call.
Il2CppArray* il2cpp_linq_join_cmp(Il2CppArray* outer, Il2CppArray* inner,
                                  Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                                  Il2CppObject* resSel, Il2CppObject* cmp,
                                  int32_t outerCell, int32_t innerCell,
                                  int32_t keyCell, int32_t resCell);

// b5-gaps section 65: Enumerable.GroupJoin(..., IEqualityComparer<TKey>)
// over ARRAY sources with REFERENCE TKeys -- the comparer-supported twin of
// section 63, using the section-64 comparer ops pair and the section-63
// group materialization.
Il2CppArray* il2cpp_linq_group_join_cmp(Il2CppArray* outer, Il2CppArray* inner,
                                        Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                                        Il2CppObject* resSel, Il2CppObject* cmp,
                                        int32_t outerCell, int32_t innerCell,
                                        int32_t keyCell, int32_t resCell);
bool il2cpp_linq_contains_family_dec(Il2CppArray* src, runtime::Il2CppDecimal value);
bool il2cpp_linq_contains_family_dt(Il2CppArray* src, runtime::Il2CppDateTime value);
bool il2cpp_linq_contains_family_ts(Il2CppArray* src, runtime::Il2CppTimeSpan value);
bool il2cpp_linq_contains_family_guid(Il2CppArray* src, Il2CppGuid value);
// impl-listvtops: the System.DateTimeOffset value form (16-byte cell).
bool il2cpp_linq_contains_family_dtoffset(Il2CppArray* src,
                                          runtime::Il2CppDateTimeOffset value);

// ElementAt / Single / SingleOrDefault per RESULT ABI. Each family's entry
// validates the cell it was dispatched for and reads one element at its real
// width. `*OrDefault` answers default(T) for an empty source.
#define IL2CPP_LINQ_DECL_ELEM_ENTRIES(NAME)                                     \
int32_t                 il2cpp_linq_##NAME##_fam_i4(Il2CppArray* src, int32_t arg, int32_t cell);   \
int64_t                 il2cpp_linq_##NAME##_fam_i8(Il2CppArray* src, int32_t arg, int32_t cell);   \
double                  il2cpp_linq_##NAME##_fam_d(Il2CppArray* src, int32_t arg, int32_t cell);    \
Il2CppObject*           il2cpp_linq_##NAME##_fam_ref(Il2CppArray* src, int32_t arg, int32_t cell);  \
runtime::Il2CppDecimal  il2cpp_linq_##NAME##_fam_dec(Il2CppArray* src, int32_t arg, int32_t cell);  \
runtime::Il2CppDateTime il2cpp_linq_##NAME##_fam_dt(Il2CppArray* src, int32_t arg, int32_t cell);   \
runtime::Il2CppTimeSpan il2cpp_linq_##NAME##_fam_ts(Il2CppArray* src, int32_t arg, int32_t cell);   \
Il2CppGuid              il2cpp_linq_##NAME##_fam_guid(Il2CppArray* src, int32_t arg, int32_t cell);  \
runtime::Il2CppDateTimeOffset il2cpp_linq_##NAME##_fam_dtoffset(Il2CppArray* src, int32_t arg,      \
                                                               int32_t cell);

// ElementAt(source, index): index out of range -> ArgumentOutOfRangeException.
IL2CPP_LINQ_DECL_ELEM_ENTRIES(element_at)

// Single / SingleOrDefault are SOURCE-ONLY: they carry no caller argument, so
// their kernels take (source, cell) directly. (A shared 3-parameter shape with
// the unused `arg` slot would make the transpiler emit a 2-argument call that
// does not match the C++ signature.)
#define IL2CPP_LINQ_DECL_ELEM_ENTRIES_NOARG(NAME)                                     \
int32_t                 il2cpp_linq_##NAME##_fam_i4(Il2CppArray* src, int32_t cell);   \
int64_t                 il2cpp_linq_##NAME##_fam_i8(Il2CppArray* src, int32_t cell);   \
double                  il2cpp_linq_##NAME##_fam_d(Il2CppArray* src, int32_t cell);    \
Il2CppObject*           il2cpp_linq_##NAME##_fam_ref(Il2CppArray* src, int32_t cell);  \
runtime::Il2CppDecimal  il2cpp_linq_##NAME##_fam_dec(Il2CppArray* src, int32_t cell);  \
runtime::Il2CppDateTime il2cpp_linq_##NAME##_fam_dt(Il2CppArray* src, int32_t cell);   \
runtime::Il2CppTimeSpan il2cpp_linq_##NAME##_fam_ts(Il2CppArray* src, int32_t cell);   \
Il2CppGuid              il2cpp_linq_##NAME##_fam_guid(Il2CppArray* src, int32_t cell); \
runtime::Il2CppDateTimeOffset il2cpp_linq_##NAME##_fam_dtoffset(Il2CppArray* src, int32_t cell);

// Single: the source must hold exactly one element (Single throws
// InvalidOperationException otherwise; SingleOrDefault throws for more than
// one and answers default(T) for an empty source -- net48).
IL2CPP_LINQ_DECL_ELEM_ENTRIES_NOARG(single)
IL2CPP_LINQ_DECL_ELEM_ENTRIES_NOARG(single_or_default)

#undef IL2CPP_LINQ_DECL_ELEM_ENTRIES
#undef IL2CPP_LINQ_DECL_ELEM_ENTRIES_NOARG

// b5-gaps section 68: Enumerable.Range(start, count) -- a fresh int32 array
// of `count` consecutive integers beginning at `start`. net48 validates the
// range up front: count < 0, or (long)start + count - 1 > int.MaxValue, throws
// ArgumentOutOfRangeException("count"). The eager array answers the same
// enumeration net48's lazy RangeIterator yields (the sequence has no
// side effects), and being an int32[] it feeds every existing array source.
Il2CppArray* il2cpp_linq_range(int32_t start, int32_t count);

// b5-gaps section 69: Enumerable.Empty<T>() -- an empty T[] of the cell.
Il2CppArray* il2cpp_linq_empty(int32_t cell);

// Enumerable.Repeat(element, count) -- an eager array of `count` copies of
// `element` at the element cell; count < 0 throws ArgumentOutOfRangeException
// ("count"), and a REFERENCE element is stored as the SAME pointer in every
// slot (net48 yields the identical object; the eager array uses write
// barriers). Element ABI is per-family: int32/narrow/char/bool ride the i4
// entry, int64 the i8 entry, float/double the d entry, references the ref.
Il2CppArray* il2cpp_linq_repeat_i4(int32_t element, int32_t count, int32_t cell);
Il2CppArray* il2cpp_linq_repeat_i8(int64_t element, int32_t count, int32_t cell);
Il2CppArray* il2cpp_linq_repeat_d(double element, int32_t count, int32_t cell);
Il2CppArray* il2cpp_linq_repeat_ref(Il2CppObject* element, int32_t count);

} // namespace il2cpp
