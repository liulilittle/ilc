#pragma once
//------------------------------------------------------------------------------
// arraysort.h -- System.Array direct-call API (transpiler-facing domain).
//
// This is the GENERATED-CODE-facing surface for the System.Array methods the
// BCL wiring needs next (the MethodRefResolver / CodeGenerator layer calls
// these directly with typed values -- no boxing, no Il2CppObject* icall
// indirection). The icall-layer equivalents (icall_array_sort /
// icall_array_index_of / icall_array_binary_search_*) live in
// il2cpp_array_ext.h / il2cpp_array_ext2.h and remain the managed-semantics
// reference for validation order and exception mapping; this domain mirrors
// those contracts with typed signatures:
//
//   il2cpp_array_sort_int32/uint32/int64  -- Array.Sort(T[]) ascending
//                                            (in-place quicksort, direct
//                                            < comparison on the raw values)
//   il2cpp_array_sort_string              -- Array.Sort(string[]) ascending
//                                            (ORDINAL comparison; null
//                                            elements sort first, matching
//                                            Comparer<string>.Default)
//   il2cpp_array_sort_object              -- Array.Sort<T>(T[], Comparison<T>):
//                                            sort driven by a CALLBACK the
//                                            transpiler supplies (the
//                                            Comparison<T> delegate becomes a
//                                            plain C function pointer with
//                                            .NET Comparison<T> semantics:
//                                            <0 / 0 / >0)
//   il2cpp_array_copy_to                  -- Array.CopyTo(Array, int): copy
//                                            EVERY element of src into dst
//                                            starting at dst[dstIndex]
//   il2cpp_array_index_of_int32/int64/string -- Array.IndexOf<T>: linear
//                                            first-match scan, -1 when absent
//   il2cpp_array_binary_search_int32/int64/string -- Array.BinarySearch<T>:
//                                            lower-bound binary search on an
//                                            ASCENDING array; a found element
//                                            returns its index, a missing one
//                                            the bitwise complement (~index)
//                                            of the insertion point; an empty
//                                            array returns -1 (= ~0)
//
// Element-kind contract (array.cpp's M3 model):
//   - value arrays:  isRefElem == false, elemSize == 4 (int32/uint32) or 8
//                    (int64) -- anything else throws NotSupportedException
//   - reference arrays: isRefElem == true (elements are Il2CppObject* slots);
//                    sort_string / index_of_string / binary_search_string
//                    treat every slot as an Il2CppString* or null
//
// String comparison contract (ORDINAL, domain-pinned like arrayext2.cpp):
//   null < any non-null string, null == null; otherwise the first differing
//   UTF-16 unit decides, a prefix pair by the length difference. The
//   comparison is pure CPU (no allocation), so moving Il2CppObject* slots
//   during a sort is safe with no GC safe point and no rooting.
//
// BinarySearch complement semantics (.NET Array.BinarySearch): a missing
// value returns ~insertionPoint (the index of the first element greater than
// the value, or the array length); a caller restores the insertion point
// with ~result. The array MUST be ascending-sorted (undefined otherwise,
// matching .NET).
//
// Exception mapping (through the DotNetException carrier, same chains as the
// icall domains):
//   null array                        -> ArgumentNullException
//   null copy target / bad index      -> ArgumentNullException /
//                                         ArgumentOutOfRangeException
//   destination too small             -> ArgumentException
//   element-kind mismatch on copy     -> ArrayTypeMismatchException
//   non-string value in string search -> ArgumentException (defensive)
//   null comparison callback          -> ArgumentNullException
//
// GC contract (reference-element sorts/copies): every written slot of a
// reference array goes through il2cpp_write_barrier; the sorts/copies
// allocate nothing, so no collection can interleave and no rooting is
// required.
//
// The comparison callback for il2cpp_array_sort_object must implement a
// strict weak ordering with .NET Comparison<T> semantics (negative/zero/
// positive). It is invoked with the two elements being compared, never with
// null (null slots compare first via the barrier order only when the
// callback says so -- the domain does not impose a null policy on
// object arrays; the transpiler's Comparison<T> decides).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_time.h" // A3: Il2CppDateTime/Il2CppTimeSpan by-value entries
#include "decimal.h"     // p0grp: the 16-byte System.Decimal enumerator carrier
#include <cstdint>
#include <cstring>       // il2cpp_ienum_get_current_tuple_t (payload copy)

//------------------------------------------------------------------------------
// D-ICMP-2 (icomparedeep): the net48 System.Collections.Comparer::Default /
// DefaultInvariant are PUBLIC STATIC READONLY FIELDS (probe-verified -- the
// reference assembly has NO get_ property), so generated `ldsfld
// System.Collections.Comparer::Default` references the transpiler's
// FieldStorageName symbol ({Owner}_sf_{Field}). The runtime owns the default
// comparer singleton, so it ALSO provides that storage symbol (the
// il2cpp_metadata.h extern + metadata.cpp define precedent for runtime-owned
// descriptors). Global scope: generated code references it unqualified.
//------------------------------------------------------------------------------
extern il2cpp::Il2CppObject* System_Collections_Comparer_sf_Default;

namespace il2cpp {

// Comparison<T> callback shape: negative/zero/positive like .NET's
// Comparison<T>. The transpiler passes the compiled Comparison<T> method as
// a plain function pointer.
using ArrayCompareFn = int32_t (*)(Il2CppObject* left, Il2CppObject* right);

// Array.Sort(int32[]): ascending in-place quicksort. int32 arrays only
// (isRefElem == false, elemSize == 4); anything else throws
// System.NotSupportedException.
void il2cpp_array_sort_int32(Il2CppArray* arr);

// Array.Sort(uint32[]): ascending in-place quicksort. uint32 arrays only
// (isRefElem == false, elemSize == 4).
void il2cpp_array_sort_uint32(Il2CppArray* arr);

// Array.Sort(int64[]): ascending in-place quicksort. int64 arrays only
// (isRefElem == false, elemSize == 8).
void il2cpp_array_sort_int64(Il2CppArray* arr);

// Array.Sort(string[]): ascending in-place quicksort using the ORDINAL
// comparison (null elements sort first). Reference arrays only; every slot
// is read as an Il2CppString* (defensive validation of elements is a
// transpiler guarantee, same as arrayext2's icall_array_sort_strings).
void il2cpp_array_sort_string(Il2CppArray* arr);

// Array.Sort<T>(T[], Comparison<T>): ascending in-place quicksort of a
// reference array driven by 'compare' (negative/zero/positive). Reference
// arrays only; null 'compare' throws System.ArgumentNullException. The
// callback must not allocate or collect (no GC safe point during the sort).
void il2cpp_array_sort_object(Il2CppArray* arr, ArrayCompareFn compare);

// Array.Sort<T>(T[], int, int): PARTIAL-sort range overloads (net48
// Array.Sort<T>(T[], int, int)). Only [index, index+length) is ordered; the
// rest of the array is untouched. The full range [0, length) is a full sort;
// a length 0/1 range is a validated no-op (index == Length with length 0 is
// legal). Validation order (same as .NET): null array
// (ArgumentNullException), element-kind (NotSupportedException),
// index/length < 0 (ArgumentOutOfRangeException), index + length > Length
// (ArgumentException) -- then the sort.
void il2cpp_array_sort_int32(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_sort_uint32(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_sort_int64(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_sort_string(Il2CppArray* arr, int32_t index, int32_t length);

// Array.Sort<T>(T[], int, int, Comparison<T>): the partial object sort. The
// range is validated (index/length/range) BEFORE the null-callback check,
// matching .NET's validation order (see the test's range-vs-callback order).
void il2cpp_array_sort_object(Il2CppArray* arr, int32_t index, int32_t length,
                              ArrayCompareFn compare);

// Array.Reverse<T>(T[], int, int): PARTIAL-reverse range overloads (net48
// Array.Reverse<T>(T[], int, int)). Only [index, index+length) is reversed;
// reversing a range twice restores the original order; length 0/1 ranges are
// validated no-ops (index == Length with length 0 legal). Same validation
// order as the partial sorts (null -> element-kind -> index/length -> range).
void il2cpp_array_reverse_int32(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_reverse_uint32(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_reverse_int64(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_reverse_string(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_reverse_object(Il2CppArray* arr, int32_t index, int32_t length);

// Array.CopyTo(Array, int): copy EVERY element of 'src' into 'dst' starting
// at dst[dstIndex]. Validation order: null src/dst (ArgumentNullException),
// dstIndex < 0 (ArgumentOutOfRangeException), src does not fit
// (ArgumentException -- this also rejects any offset self-copy, which .NET
// CopyTo cannot express; only the dstIndex == 0 identity copy overlaps),
// element-kind mismatch (ArrayTypeMismatchException) -- then the copy
// (memmove, defensive overlap safety). An empty src is a validated no-op
// (dstIndex == dst->length legal, matching .NET).
void il2cpp_array_copy_to(Il2CppArray* src, Il2CppArray* dst, int32_t dstIndex);

// Array.IndexOf<int32>: index of the FIRST element equal to 'value', or -1
// when absent (also for an empty array). int32 arrays only.
int32_t il2cpp_array_index_of_int32(Il2CppArray* arr, int32_t value);

// Array.IndexOf<int64>: int64 variant of il2cpp_array_index_of_int32.
int32_t il2cpp_array_index_of_int64(Il2CppArray* arr, int64_t value);

// Array.IndexOf<string>: first slot whose ordinal comparison with 'value' is
// equal, or -1 when absent. 'value' may be null (matches the first null
// slot, .NET EqualityComparer<string>.Default semantics); any other
// non-string object throws System.ArgumentException (defensive). Reference
// arrays only.
int32_t il2cpp_array_index_of_string(Il2CppArray* arr, Il2CppObject* value);

// Array.BinarySearch<int32>: lower-bound search on an ASCENDING int32 array
// (see the header comment for the complement rule). int32 arrays only.
int32_t il2cpp_array_binary_search_int32(Il2CppArray* arr, int32_t value);

// Array.BinarySearch<int64>: int64 variant of il2cpp_array_binary_search_int32.
int32_t il2cpp_array_binary_search_int64(Il2CppArray* arr, int64_t value);

int32_t il2cpp_array_binary_search_uint32(Il2CppArray* arr, int32_t value);
int32_t il2cpp_array_binary_search_icomparer_uint32(Il2CppArray* arr, int32_t value, Il2CppObject* comparer);
// Array.BinarySearch<string>: ordinal variant of the int32 binary search.
// 'value' may be null (matches only null slots); any other non-string object
// throws System.ArgumentException. Reference arrays only.
int32_t il2cpp_array_binary_search_string(Il2CppArray* arr, Il2CppObject* value);

//------------------------------------------------------------------------------
// W7 (T2): the double/float/long generic static surface. Double and float
// sort with the net48 total order (NaN sorts before any non-NaN; NaN vs
// NaN equal -- the Double.CompareTo contract); IndexOf<double> uses
// Double.Equals semantics (a NaN needle finds NaN slots); the binary
// search is the total-order lower bound with the complement rule.
// LastIndexOf<double/long/string> are reverse first-match scans (double
// with Double.Equals; string ordinal with null matching null slots).
//------------------------------------------------------------------------------

// Array.Sort(double[]): ascending total-order in-place quicksort. double
// arrays only (isRefElem == false, elemSize == 8).
void il2cpp_array_sort_double(Il2CppArray* arr);

// Array.Sort(float[]): ascending total-order in-place quicksort. float
// arrays only (isRefElem == false, elemSize == 4; comparison widens to
// double, so NaN/-0.0 behave exactly like the double sort).
void il2cpp_array_sort_float(Il2CppArray* arr);

// Array.Sort<T>(T[], Comparison<T>) for the wide value families (nb9
// E6/D17): int64, double and float. The delegate carries the model ABI
// (int64_t / double / double).
void il2cpp_array_sort_icomparer_uint32_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* comparer);
void il2cpp_array_sort_int64_comparison(Il2CppArray* arr, Il2CppObject* comparison);
void il2cpp_array_sort_double_comparison(Il2CppArray* arr, Il2CppObject* comparison);
void il2cpp_array_sort_float_comparison(Il2CppArray* arr, Il2CppObject* comparison);

// Array.IndexOf<double>: index of the FIRST element Double.Equals to
// 'value' (NaN finds NaN), or -1 when absent. double arrays only.
int32_t il2cpp_array_index_of_double(Il2CppArray* arr, double value);

// Array.BinarySearch<double>: total-order lower-bound search on an
// ASCENDING double array (same complement rule as the int32 search).
// double arrays only.
int32_t il2cpp_array_binary_search_double(Il2CppArray* arr, double value);

// Array.LastIndexOf<double/long/string>: index of the LAST matching
// element, or -1 when absent (also for an empty array).
int32_t il2cpp_array_last_index_of_double(Il2CppArray* arr, double value);
int32_t il2cpp_array_last_index_of_long(Il2CppArray* arr, int64_t value);
int32_t il2cpp_array_last_index_of_string(Il2CppArray* arr, Il2CppObject* value);

//------------------------------------------------------------------------------
// D-ICMP (icomparedeep, 2026-08-12): the comparer/comparison dispatch surface.
//
// These entries wire the comparison DOMAIN the icomparedeep e2e case needs:
// the mscorlib IComparable/IComparer interface dispatch, the
// System.Collections.Comparer default comparer (Comparer.Default), and the
// comparer-driven Array.Sort overloads.
//------------------------------------------------------------------------------

// IComparable.CompareTo(object) interface dispatch. Resolves the receiver's
// CompareTo vtable slot through the registered typeinfo method table (the
// transpiler lowers an interface-typed callvirt to this runtime entry when the
// receiver's concrete type is a translated user type). G2/D3: a boxed BUILTIN
// receiver (Int32/UInt32/Int64/UInt64/Single/Double/Char/Boolean/String -- no
// reflected vtable) resolves through the raw VALUE table first
// (TryCompareBoxedBuiltin, arraysort.cpp), so the boxed-primitive IComparable
// surface works with net48 semantics (value compare, null -> 1, foreign type
// -> ArgumentException). A null receiver throws NullReferenceException; a
// receiver with no table entry AND no reflected virtual CompareTo slot throws
// NotSupportedException. The user implementation decides the null-other
// ordering (.NET semantics: the called CompareTo runs verbatim).
int32_t il2cpp_icomparable_compare_to(Il2CppObject* receiver, Il2CppObject* other);

// IComparer.Compare(object, object) interface dispatch -- the non-generic
// System.Collections.IComparer counterpart of il2cpp_icomparable_compare_to.
int32_t il2cpp_icomparer_compare(Il2CppObject* comparer, Il2CppObject* a, Il2CppObject* b);

// nb9 (H-B3): the element-family dispatch for a USER IComparer<T> (shared with
// the sorted-container domain -- sortedlist.cpp's CompareWithComparer calls
// this, so there is exactly ONE family-aware slot resolver).
//
// A user IComparer<int> is emitted with the REAL T signature ("i(i,i)"), so the
// name-only dispatch would select that slot and call it through the
// (this, object, object) ABI -- a pointer where the emitted (int,int) slot
// expects int32 (the user-reported 0xC0000005). 'family' selects the signature
// fragment and call ABI:
//   kComparerFamilyI4  : int32/uint32/char/bool elements -- fragment "(i,i)",
//                        a/b point to the raw int32 value
//   kComparerFamilyI8  : int64/uint64 elements -- "(l,l)", a/b -> int64
//   kComparerFamilyD   : float/double elements -- "(d,d)", a/b -> double
//   kComparerFamilyRef : string/object/class elements -- "(r:", a/b ARE the
//                        element object pointers
//   kComparerFamilyU4  : uint32 elements -- the SAME "(i,i)" slot fragment and
//                        raw-int32 ABI as I4, but the DEFAULT comparer orders
//                        UNSIGNED. A uint[] shares the int32 stride AND
//                        Comparer<uint>.Default rides the Comparer`1_int
//                        singleton (its klass name cannot tell the two apart),
//                        so the signedness must come from the ARRAY's elemKind
//                        -- B8-1: without this, 4000000000 sorted below 1.
// A Comparer<T>.Create wrapper runs its wrapped delegate. When no family slot
// exists the non-generic IComparer.Compare(object, object) slot (the
// Array.Sort(Array, IComparer) / SortedSet-with-non-generic-comparer shape)
// is used -- value operands boxed for that call -- and NotSupported is thrown
// when neither exists (never a name-only call through a mismatched ABI).
enum ComparerValueFamily : int32_t
{
    kComparerFamilyI4 = 0,
    kComparerFamilyI8 = 1,
    kComparerFamilyD = 2,
    kComparerFamilyRef = 3,
    // nb9 F4: float elements with an IComparer<float>. The reflected slot
    // signature is "(f,f)" while the C++ ABI carries the values widened to
    // double, exactly like the D family.
    kComparerFamilyF = 4,
    // B8-1: uint32 elements -- the "(i,i)" slot + raw-int32 ABI of I4 with an
    // UNSIGNED default order (see the contract above).
    kComparerFamilyU4 = 5,
    // Narrow-integral families: the reflected Compare slot carries the REAL
    // narrow signature ("(b,b)" etc.) while the C++ ABI widens the cell to
    // int32 (the transpiler narrow value slot). The DEFAULT order is the
    // widened int32 order (sign/zero extension makes it exact for every kind).
    kComparerFamilyBool = 6,  // "(b,b)"
    kComparerFamilyChar = 7,  // "(c,c)"
    kComparerFamilyI1 = 8,    // "(i1,i1)"
    kComparerFamilyU1 = 9,    // "(u1,u1)"
    kComparerFamilyI2 = 10,   // "(i2,i2)"
    kComparerFamilyU2 = 11,   // "(u2,u2)"
};

int32_t il2cpp_icomparer_compare_family(Il2CppObject* comparer, int32_t family,
                                        const void* a, const void* b);

// System.Collections.Comparer::get_Default / get_DefaultInvariant -- the
// runtime's default-comparer SINGLETON object (the resolver maps the static
// getters to this; the returned object's Compare dispatches on the runtime
// type). Idempotent; the same pointer is returned every call.
Il2CppObject* il2cpp_comparer_default(void);

// nb16 (b16low): the non-generic default comparer SINGLETON OBJECT itself
// (DEFINED in arraysort.cpp). The default-comparer classification now tests
// pointer identity against this address (the klass-name substring probe was
// removed); sortedlist.cpp's IsDefaultComparerObject shares it.
extern Il2CppObject System_Collections_Comparer_singleton;

// a5reds (strsort_ordinal): System.StringComparer::get_Ordinal /
// get_OrdinalIgnoreCase -- the runtime's ordinal-string-comparer SINGLETONS
// (klass names "System.StringComparer.Ordinal" / ".OrdinalIgnoreCase").
// il2cpp_array_sort_icomparer recognizes them by POINTER IDENTITY and sorts
// reference-element arrays with il2cpp_string_compare_ordinal /
// _ignore_case (null-first ordering, net48); a non-string element throws
// ArgumentException (the comparand type-mismatch contract). Idempotent; the
// same pointer is returned every call.
Il2CppObject* il2cpp_string_comparer_ordinal(void);
Il2CppObject* il2cpp_string_comparer_ordinal_ignore_case(void);

// System.Collections.Comparer::Compare(object, object) -- the DEFAULT comparer
// semantics (also the fallback for a null IComparer in the comparer sorts):
// null < any non-null (null == null); a string pair compares ORDINAL; a
// numeric/char/bool box pair compares by value; anything else dispatches to
// the first argument's IComparable.CompareTo. A non-comparable argument pair
// throws ArgumentException (net48 "At least one object must implement
// IComparable.").
int32_t il2cpp_comparer_compare(Il2CppObject* self, Il2CppObject* a, Il2CppObject* b);

// Array.Sort(Array, IComparer) -- the NON-GENERIC comparer sort. Both
// int32 value arrays (elements boxed per comparison) and reference arrays
// (elements are Il2CppObject* slots) are supported. A null comparer means the
// default comparer (net48 Array.Sort contract). The comparer's Compare is
// dispatched through il2cpp_icomparer_compare (a user comparer object) or the
// builtin default dispatch (the Comparer.Default singleton / null).
void il2cpp_array_sort_icomparer(Il2CppArray* arr, Il2CppObject* comparer);

// Array.BinarySearch<T>(T[], T, IComparer<T>) (nb9 H-B4). The typed value
// carriers follow the same split as the sorts: the int32 entry takes the raw
// int32 value, the ref entry the element object (string/object/class arrays).
// A null comparer means the default comparer (net48); the search uses the
// user comparer's element-family Compare(T,T) slot (the H-B3 dispatch) and
// returns the found index or the bitwise complement of the insertion point.
int32_t il2cpp_array_binary_search_icomparer_int32(Il2CppArray* arr, int32_t value,
                                                   Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_ref(Il2CppArray* arr, Il2CppObject* value,
                                                 Il2CppObject* comparer);

// The RANGE overloads (nb9 zz-b4-cmp4 / zz-b4-bsearch2):
//   Array.Sort<T>(T[], int, int, IComparer<T>)
//   Array.BinarySearch<T>(T[], int, int, T, IComparer<T>)
// Only [index, index+length) is touched; the BinarySearch complement is the
// insertion point in ARRAY coordinates (~(index + localInsertion)).
void il2cpp_array_sort_icomparer_range(Il2CppArray* arr, int32_t index, int32_t length,
                                       Il2CppObject* comparer);

// nb9 L-11/D7: the wide value-family IComparer<T> sorts (long/float/double).
void il2cpp_array_sort_icomparer_int64(Il2CppArray* arr, Il2CppObject* comparer);
void il2cpp_array_sort_icomparer_int64_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* comparer);
void il2cpp_array_sort_icomparer_float(Il2CppArray* arr, Il2CppObject* comparer);
void il2cpp_array_sort_icomparer_float_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* comparer);
void il2cpp_array_sort_icomparer_double(Il2CppArray* arr, Il2CppObject* comparer);
void il2cpp_array_sort_icomparer_double_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_int32_range(Il2CppArray* arr, int32_t index,
                                                         int32_t length, int32_t value,
                                                         Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_ref_range(Il2CppArray* arr, int32_t index,
                                                       int32_t length, Il2CppObject* value,
                                                       Il2CppObject* comparer);

//------------------------------------------------------------------------------
// nb9 P0 (arity mispair): the RANGE/START arities of the generic scan surface
// in net48 parameter order. Each family has:
//   index_of_*_start(arr, value, startIndex)          Array.IndexOf(T[],T,int)
//   index_of_*_range(arr, value, startIndex, count)   ...(T[],T,int,int)
//   last_index_of_*_start(arr, value, startIndex)     Array.LastIndexOf(T[],T,int)
//   last_index_of_*_range(arr, value, startIndex,count)...(T[],T,int,int)
// The value families are int32/int64/float/double; 'string' uses the ordinal
// comparison (null matches null), 'object' uses Object.Equals via
// runtime::icall_object_equals. Validation matches net48 (AOORE "startIndex" /
// "count").
//------------------------------------------------------------------------------
int32_t il2cpp_array_index_of_int32_start(Il2CppArray* arr, int32_t value, int32_t startIndex);
int32_t il2cpp_array_index_of_int32_range(Il2CppArray* arr, int32_t value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_int32_start(Il2CppArray* arr, int32_t value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_int32_range(Il2CppArray* arr, int32_t value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_index_of_int64_start(Il2CppArray* arr, int64_t value, int32_t startIndex);
int32_t il2cpp_array_index_of_int64_range(Il2CppArray* arr, int64_t value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_int64_start(Il2CppArray* arr, int64_t value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_int64_range(Il2CppArray* arr, int64_t value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_index_of_float_start(Il2CppArray* arr, double value, int32_t startIndex);
int32_t il2cpp_array_index_of_float_range(Il2CppArray* arr, double value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_float(Il2CppArray* arr, double value);
int32_t il2cpp_array_last_index_of_float_start(Il2CppArray* arr, double value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_float_range(Il2CppArray* arr, double value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_index_of_double_start(Il2CppArray* arr, double value, int32_t startIndex);
int32_t il2cpp_array_index_of_double_range(Il2CppArray* arr, double value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_double_start(Il2CppArray* arr, double value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_double_range(Il2CppArray* arr, double value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_index_of_string_start(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_array_index_of_string_range(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_string_start(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_string_range(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_index_of_object_start(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_array_index_of_object_range(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_object_start(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_object_range(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex, int32_t count);

// Non-generic Array.IndexOf/LastIndexOf/BinarySearch arities (Array, object
// [, startIndex[, count]]) over int32 element arrays (boxed comparand) and
// reference arrays (Object.Equals / default comparer).
int32_t il2cpp_array_index_of_any_start(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_array_index_of_any_range(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_any(Il2CppArray* arr, Il2CppObject* value);
int32_t il2cpp_array_last_index_of_any_start(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex);
int32_t il2cpp_array_last_index_of_any_range(Il2CppArray* arr, Il2CppObject* value, int32_t startIndex, int32_t count);
int32_t il2cpp_array_binary_search_any(Il2CppArray* arr, Il2CppObject* value);
int32_t il2cpp_array_binary_search_any_icomparer(Il2CppArray* arr, Il2CppObject* value, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_any_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* value);
int32_t il2cpp_array_binary_search_any_icomparer_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* value, Il2CppObject* comparer);

// A3: Array.IndexOf<DateTime>/BinarySearch<DateTime> (the by-VALUE generic
// instantiation -- no boxing needed). Cells are the CLR 8-byte images
// (kind<<62|ticks); comparison uses TICKS only, matching DateTime.CompareTo
// (DateTimeKind is not part of ordering or equality).
int32_t il2cpp_array_index_of_datetime(Il2CppArray* arr, runtime::Il2CppDateTime value);
int32_t il2cpp_array_binary_search_datetime(Il2CppArray* arr, runtime::Il2CppDateTime value);
int32_t il2cpp_array_index_of_timespan(Il2CppArray* arr, runtime::Il2CppTimeSpan value);
int32_t il2cpp_array_binary_search_timespan(Il2CppArray* arr, runtime::Il2CppTimeSpan value);
// The range forms: IndexOf<T>(T[], T, int startIndex, int count) and
// BinarySearch<T>(T[], int index, int length, T).
int32_t il2cpp_array_index_of_datetime_range(Il2CppArray* arr, runtime::Il2CppDateTime value,
                                             int32_t startIndex, int32_t count);
int32_t il2cpp_array_binary_search_datetime_range(Il2CppArray* arr, int32_t index, int32_t length,
                                                  runtime::Il2CppDateTime value);
int32_t il2cpp_array_index_of_timespan_range(Il2CppArray* arr, runtime::Il2CppTimeSpan value,
                                             int32_t startIndex, int32_t count);
int32_t il2cpp_array_binary_search_timespan_range(Il2CppArray* arr, int32_t index, int32_t length,
                                                  runtime::Il2CppTimeSpan value);
// The start / LastIndexOf forms (net48 IndexOf<T>(T[],T,int) and
// LastIndexOf<T>(T[],T[,int[,int]])). Ticks-only comparison as above.
int32_t il2cpp_array_index_of_datetime_start(Il2CppArray* arr, runtime::Il2CppDateTime value,
                                             int32_t startIndex);
int32_t il2cpp_array_index_of_timespan_start(Il2CppArray* arr, runtime::Il2CppTimeSpan value,
                                             int32_t startIndex);
int32_t il2cpp_array_last_index_of_datetime(Il2CppArray* arr, runtime::Il2CppDateTime value);
int32_t il2cpp_array_last_index_of_timespan(Il2CppArray* arr, runtime::Il2CppTimeSpan value);
int32_t il2cpp_array_last_index_of_datetime_start(Il2CppArray* arr, runtime::Il2CppDateTime value,
                                                  int32_t startIndex);
int32_t il2cpp_array_last_index_of_timespan_start(Il2CppArray* arr, runtime::Il2CppTimeSpan value,
                                                  int32_t startIndex);
int32_t il2cpp_array_last_index_of_datetime_range(Il2CppArray* arr, runtime::Il2CppDateTime value,
                                                  int32_t startIndex, int32_t count);
int32_t il2cpp_array_last_index_of_timespan_range(Il2CppArray* arr, runtime::Il2CppTimeSpan value,
                                                  int32_t startIndex, int32_t count);

// Array.BinarySearch<T>(T[], int, int, T) -- the range search (no comparer).
int32_t il2cpp_array_binary_search_int32_range(Il2CppArray* arr, int32_t index, int32_t length, int32_t value);
int32_t il2cpp_array_binary_search_int64_range(Il2CppArray* arr, int32_t index, int32_t length, int64_t value);
int32_t il2cpp_array_binary_search_float_range(Il2CppArray* arr, int32_t index, int32_t length, double value);
int32_t il2cpp_array_binary_search_uint32_range(Il2CppArray* arr, int32_t index, int32_t length, int32_t value);
int32_t il2cpp_array_binary_search_double_range(Il2CppArray* arr, int32_t index, int32_t length, double value);
int32_t il2cpp_array_binary_search_float(Il2CppArray* arr, double value);
int32_t il2cpp_array_binary_search_object(Il2CppArray* arr, Il2CppObject* value);
int32_t il2cpp_array_binary_search_string_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* value);
int32_t il2cpp_array_binary_search_object_range(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* value);

// Array.BinarySearch<T>(T[], T, IComparer<T>) / (..., int, int, T, IComparer<T>)
// for the wide families (int32/ref already declared above).
int32_t il2cpp_array_binary_search_icomparer_int64(Il2CppArray* arr, int64_t value, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_int64_range(Il2CppArray* arr, int32_t index, int32_t length, int64_t value, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_double(Il2CppArray* arr, double value, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_double_range(Il2CppArray* arr, int32_t index, int32_t length, double value, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_float(Il2CppArray* arr, double value, Il2CppObject* comparer);
int32_t il2cpp_array_binary_search_icomparer_float_range(Il2CppArray* arr, int32_t index, int32_t length, double value, Il2CppObject* comparer);

// Array.Sort<T>(T[], int, int) for the double/float families (the int32/
// uint32/int64/string partial sorts are declared above).
// nb9 D2/D3/D5/D6/D8: element-kind-driven sorts. il2cpp_array_sort_any sorts
// an array whose element kind was recorded at allocation (int32/int64/float/
// double/reference); the keys/items pair sorts the key array and applies the
// same permutation to items.
void il2cpp_array_sort_any(Il2CppArray* arr);
void il2cpp_array_sort_any_range_icomparer(Il2CppArray* arr, int32_t index, int32_t length, Il2CppObject* comparer);
void il2cpp_array_sort_any_range(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_sort_keys_items(Il2CppArray* keys, Il2CppArray* items, Il2CppObject* comparer);
void il2cpp_array_sort_keys_items_default(Il2CppArray* keys, Il2CppArray* items);
void il2cpp_array_sort_keys_items_range(Il2CppArray* keys, Il2CppArray* items, int32_t index, int32_t length, Il2CppObject* comparer);
void il2cpp_array_sort_keys_items_default_range(Il2CppArray* keys, Il2CppArray* items, int32_t index, int32_t length);void il2cpp_array_sort_double(Il2CppArray* arr, int32_t index, int32_t length);
void il2cpp_array_sort_float(Il2CppArray* arr, int32_t index, int32_t length);

// Array.Sort<T>(T[], Comparison<T>) -- the DELEGATE sorts. The transpiler
// passes the compiled Comparison<T> as a managed delegate (single-cast static
// methodPtr ABI, the Predicate<int> precedent); the runtime invokes it per
// comparison pair (an instance-bound leaf receives its target in the `this`
// slot). int32 arrays compare the RAW element values; reference arrays pass
// the element slots. A null comparison throws ArgumentNullException.
void il2cpp_array_sort_int32_comparison(Il2CppArray* arr, Il2CppObject* comparison);
void il2cpp_array_sort_ref_comparison(Il2CppArray* arr, Il2CppObject* comparison);

//------------------------------------------------------------------------------
// P25c enumerator-interface dispatch (yield iterators / user IEnumerator<T> /
// IEnumerable<T>). A foreach over a USER-enumerable / yield-returning iterator
// compiles to callvirts whose declaring owner is a BCL interface
// (System.Collections.Generic.IEnumerable`1 / IEnumerator`1 or the non-generic
// IEnumerable / IEnumerator). Those owners have no translated TypeDef, so the
// transpiler lowers each member call to one of these runtime entries
// (BclResolveCore -> TryMapEnumIface, the D-ICMP-1 dispatch pattern). At
// runtime the receiver object's vtable slot for the interface member is
// resolved through the typeinfo registry JOINED BY THE KLASS POINTER
// (il2cpp_typeinfo_lookup_by_class -- immune to the nested-type
// short/full-name mismatch) and dispatched through the receiver's vtable.
//
// Contract (common to all entries): a null receiver throws
// NullReferenceException; a receiver whose reflected typeinfo has no virtual
// slot matching the member's NAME AND SIGNATURE throws NotSupportedException.
// get/Current selections are SIGNATURE-AWARE: a compiler state machine --
// and a user IEnumerator<T> -- implement BOTH `IEnumerator<T>.get_Current`
// (e.g. "i()") and `IEnumerator.get_Current` ("r:System.Object()"), so the
// entries pick the slot whose stored signature matches the requested carrier
// (the dispatcher-level carrier is the transpiler's instName decision),
// never a name-only first match.

// IEnumerator.MoveNext -- returns the enumerator's bool (int32 slot).
int32_t il2cpp_ienum_move_next(Il2CppObject* receiver);

// IEnumerator<T>/IEnumerable<T>.get_Current -- per-carrier C++ return types.
int32_t     il2cpp_ienum_get_current_i4(Il2CppObject* receiver);
int64_t     il2cpp_ienum_get_current_i8(Il2CppObject* receiver);
double      il2cpp_ienum_get_current_d(Il2CppObject* receiver);
Il2CppObject* il2cpp_ienum_get_current_ref(Il2CppObject* receiver);

// p0grp: the 16-byte System.Decimal carrier (IEnumerator<decimal>.get_Current).
// A 16-byte struct cannot ride a register return slot in the generated-code
// model, so the entry follows the decimal T3 result-slot contract: the FIRST
// parameter is the caller's Il2CppDecimal result slot. The receiver may be a
// snapshot enumerator over a decimal[] (the GroupBy bucket surface) or over a
// reference array of BOXED decimals; any other layout raises
// InvalidCastException rather than reinterpreting memory.
void il2cpp_ienum_get_current_dec(runtime::Il2CppDecimal* result, Il2CppObject* receiver);

// The NON-generic IEnumerator.get_Current -- always the object-returning slot
// ("r:System.Object()"). Distinct from il2cpp_ienum_get_current_ref, which
// targets the generic IEnumerator<T>.get_Current carrier slot.
Il2CppObject* il2cpp_ienum_get_current_obj(Il2CppObject* receiver);

// Value-type tuple element (a LINQ Select result): the snapshot array holds
// BOXED tuples (the shared tuple-box contract -- see il2cpp_valuetuple.h);
// get_Current copies the box payload into the caller's struct slot and returns
// it BY VALUE (the same shape as a user-struct element). 'kinds' is the
// element-kind string (diagnostic only; the box klass name is authoritative).
template <typename T>
inline T il2cpp_ienum_get_current_tuple_t(Il2CppObject* receiver, const char* kinds)
{
    (void)kinds;
    Il2CppObject* box = il2cpp_ienum_get_current_ref(receiver);
    T out;
    if (box != nullptr)
    {
        const void* payload = reinterpret_cast<const char*>(box) + 8;
        std::memcpy(&out, payload, sizeof(T));
    }
    else
    {
        std::memset(&out, 0, sizeof(T));
    }
    return out;
}

// IEnumerable<T>.GetEnumerator (and the non-generic IEnumerable/IEnumerator
// pair below) -- the generic interface entry dispatches to the generic
// GetEnumerator-vtable slot; the non-generic pair (owner is the non-generic
// interface) dispatches to the non-generic slot. They differ on the state
// machine by vtable position (generic slot 1 vs non-generic slot 3 in
// _Range_d__0), so the two entries are NOT interchangeable.
Il2CppObject* il2cpp_ienum_get_enumerator(Il2CppObject* receiver);
Il2CppObject* il2cpp_ienum_get_enumerator_nongeneric(Il2CppObject* receiver);

// IEnumerator.Reset / System.IDisposable.Dispose -- void members. Dispose is
// reached from the foreach `isinst System.IDisposable` guard; a receiver with
// no reflected Dispose slot is a NO-OP (the SortedList snapshot enumerator is
// not IDisposable and the guard never fires) rather than a throw.
void il2cpp_ienum_reset(Il2CppObject* receiver);
void il2cpp_ienum_dispose(Il2CppObject* receiver);

// Resolve a virtual slot by METHOD NAME through the registered typeinfo
// (D-ICMP-1). Returns -1 when the receiver type has no reflected virtual of
// that name (a boxed builtin has no typeinfo). Shared by the IComparable /
// IComparer dispatch and the Object.Equals virtual fallback.
int32_t FindVirtualSlotByName(Il2CppObject* obj, const char* name);

// baudit2: resolve a virtual slot by METHOD NAME + a signature FRAGMENT
// (own typeinfo table first, then the baseType chain -- the inherited
// override case). The fragment selects the intended overload: the
// Object.Equals virtual dispatch must use "(r:System.Object)" so a type
// whose IEquatable<T>.Equals overload is declared first does NOT get its
// (different-ABI) slot. Returns -1 when no reflected method matches.
int32_t FindVirtualSlotByNameAndSignature(Il2CppObject* obj, const char* name,
                                          const char* signatureFragment);

//------------------------------------------------------------------------------
// NARROW-INTEGRAL generic families (bool/char/sbyte/byte/short/ushort/uint).
// The transpiler passes a KIND literal (8 bool, 9 char, 10 sbyte, 11 byte,
// 12 short, 13 ushort) because all narrow cells share the int32 IL slot; the
// kernel selects the real storage width from it. The comparer / Comparison<T>
// ABI widens the cell to int32 (the transpiler's narrow value slot), so the
// existing I4 comparer/Comparison machinery is reused.
//------------------------------------------------------------------------------
void il2cpp_array_sort_narrow(Il2CppArray* arr, int32_t kind);
void il2cpp_array_sort_narrow_range(Il2CppArray* arr, int32_t index, int32_t length, int32_t kind);
void il2cpp_array_sort_narrow_icomparer(Il2CppArray* arr, Il2CppObject* comparer, int32_t kind);
void il2cpp_array_sort_narrow_icomparer_range(Il2CppArray* arr, int32_t index, int32_t length,
                                              Il2CppObject* comparer, int32_t kind);
void il2cpp_array_sort_narrow_comparison(Il2CppArray* arr, Il2CppObject* comparison, int32_t kind);

int32_t il2cpp_array_index_of_narrow(Il2CppArray* arr, int32_t value, int32_t kind);
int32_t il2cpp_array_index_of_narrow_start(Il2CppArray* arr, int32_t value, int32_t startIndex, int32_t kind);
int32_t il2cpp_array_index_of_narrow_range(Il2CppArray* arr, int32_t value, int32_t startIndex,
                                           int32_t count, int32_t kind);
int32_t il2cpp_array_last_index_of_narrow(Il2CppArray* arr, int32_t value, int32_t kind);
int32_t il2cpp_array_last_index_of_narrow_start(Il2CppArray* arr, int32_t value, int32_t startIndex, int32_t kind);
int32_t il2cpp_array_last_index_of_narrow_range(Il2CppArray* arr, int32_t value, int32_t startIndex,
                                                int32_t count, int32_t kind);
int32_t il2cpp_array_binary_search_narrow(Il2CppArray* arr, int32_t value, int32_t kind);
int32_t il2cpp_array_binary_search_narrow_range(Il2CppArray* arr, int32_t index, int32_t length,
                                                int32_t value, int32_t kind);
int32_t il2cpp_array_binary_search_narrow_icomparer(Il2CppArray* arr, int32_t value,
                                                    Il2CppObject* comparer, int32_t kind);
int32_t il2cpp_array_binary_search_narrow_icomparer_range(Il2CppArray* arr, int32_t index,
                                                          int32_t length, int32_t value,
                                                          Il2CppObject* comparer, int32_t kind);

// Array.Sort<UIntPtr>/BinarySearch<UIntPtr>: the unsigned 64-bit twins of the
// int64 kernels (the transpiler carries the UIntPtr bits in an int64 slot).
void il2cpp_array_sort_uint64(Il2CppArray* arr);
void il2cpp_array_sort_uint64_range(Il2CppArray* arr, int32_t index, int32_t length);
int32_t il2cpp_array_binary_search_uint64(Il2CppArray* arr, int64_t value);
int32_t il2cpp_array_binary_search_uint64_range(Il2CppArray* arr, int32_t index, int32_t length,
                                                int64_t value);

// net48 parity for a type that does NOT implement IComparable (IntPtr/UIntPtr
// on .NET Framework): Comparer<T>.Default raises InvalidOperationException
// ("Failed to compare two elements in the array."). An EMPTY search range
// returns -1 without invoking the comparer, exactly like net48.
int32_t il2cpp_array_binary_search_incomparable(Il2CppArray* arr, int64_t value);
int32_t il2cpp_array_binary_search_incomparable_range(Il2CppArray* arr, int32_t index,
                                                      int32_t length, int64_t value);

} // namespace il2cpp
