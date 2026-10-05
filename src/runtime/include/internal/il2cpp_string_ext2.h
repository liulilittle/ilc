#pragma once
//------------------------------------------------------------------------------
// il2cpp_string_ext2.h -- System.String STATIC methods (runtime icalls).
//
// The instance-method icalls live in il2cpp_string_ext.h / stringext.cpp; this
// header adds the STATIC members of System.String that generated code and the
// BCL layer call through the runtime boundary:
//
//   Compare / Equals (ordinal, null-aware)
//   Concat 2/3/4 and the (object, object) numeric conveniences
//   IsNullOrEmpty / IsNullOrWhiteSpace
//   Join(string separator, string[] / object[] values)
//   Copy
//   the char indexer (get_Chars)
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline. All ordinal comparisons operate on UTF-16 code units:
//   * Compare: null == null -> 0; null < non-null (-> -1); non-null > null
//     (-> +1); otherwise the result replicates net48's CompareOrdinalHelper
//     value-for-value (DEFECT-STR2B-1 fix, fresh-oracle verified 2026-08-08):
//     the payloads are scanned in 2-unit pairs and the unit past the end of
//     a string reads as 0 (net48 strings are null-terminated), so a prefix
//     pair with an ODD common length returns the raw boundary-unit
//     difference (e.g. CompareOrdinal("", "a") == -97, CompareOrdinal("ab",
//     "a") == 98) while an EVEN common length returns the plain length
//     difference (CompareOrdinal("abc", "ab") == 1). The scan always reads
//     the first pair, so two equal-prefix strings of unequal length agree
//     with net48 on every shape (144/144 grid + targeted probes validated).
//     The runtime's bounds-checked reads substitute 0 for out-of-range units
//     (the Il2CppString payload has no guaranteed terminator), which is
//     exactly net48's observable terminator value.
//   * Equals: both null -> true; exactly one null -> false; otherwise
//     ordinal content equality (length + units).
//   * Concat: null arguments count as the empty string; 2/3/4-arg forms
//     delegate to il2cpp_string_concat_many (single managed allocation, see
//     il2cpp_concat.h); the numeric conveniences are Concat(object, object)
//     with the number formatted via format.cpp's ToString icalls.
//   * IsNullOrEmpty: null or zero units. IsNullOrWhiteSpace: null, zero
//     units, or all units in Char.IsWhiteSpace (the full .NET set, listed
//     literally in stringext2.cpp -- no host-locale dependence).
//   * Join: separator appears only BETWEEN elements (never leading or
//     trailing); a null separator counts as an empty separator (net48
//     behavior); null elements count as empty strings. Elements that are
//     NOT strings (the object[] overload -- indistinguishable from string[]
//     at the signature shape) are converted with icall_object_to_string
//     (.NET Join(object[]) calls ToString() on each element: boxed
//     primitives format their value, other objects print their type's full
//     name). A null values array throws ArgumentNullException (net48; the
//     string[] overload names the parameter "value" -- the runtime sees both
//     overloads through one icall, so that name is used); an empty or
//     non-reference array yields an empty string (defensive, unreachable
//     through the wired shapes). D-STR2-1 (fresh-oracle verified
//     2026-08-12): an OBJECT[] whose FIRST element is null yields
//     String.Empty (net48 object[] overload shortcut:
//     `if (values.Length == 0 || values[0] == null) return String.Empty;`)
//     -- never a join with a leading separator. The string[] overload has NO
//     such shortcut (Join(",", new string[]{null,"x"}) == ",x"). The array's
//     element type is classified by IsObjectArray (stringext2.cpp): the
//     element descriptor when present ("System.String" -> string[], anything
//     else -> object[]), otherwise by content (any non-string element proves
//     object[]). RESIDUE: an object[] whose non-null elements are all strings
//     is indistinguishable from a string[] when the descriptor is missing
//     (the transpiler emits reference arrays without element-type metadata)
//     -- the runtime keeps string[] semantics for that shape. GC note: a
//     non-string element's ToString conversion allocates, so callers must
//     keep the array AND the separator reachable during the call (the
//     numeric-concat convenience exception below).
//   * Copy: a NEW instance with equal content -- even for the empty string
//     (.NET String.Copy semantics; observable via ReferenceEquals). A null
//     argument throws ArgumentNullException.
//   * get_Chars: returns the code unit at 'index' as int32; an out-of-range
//     index throws IndexOutOfRangeException (net48: the string indexer, NOT
//     ArgumentOutOfRangeException). A null receiver is a defensive NRE.
//     INLINE header body (perf1): one definition site, inlined at every call.
//
// LENGTH: System.String.Length is NOT re-implemented here --
// il2cpp::il2cpp_string_get_length (il2cpp_string.h) already provides it
// (null -> 0); generated code and tests call that directly (reuse, no
// duplicate symbol).
//
// GC-ROOTING CONTRACT (physical -- see il2cpp_gc.h): managed arguments are
// native C++ parameters and are NOT frame roots. Implementations guarantee
// that every managed memory read happens BEFORE the single managed allocation
// of the call (il2cpp_gc_alloc_size may collect, collect-then-retry), EXCEPT
// the numeric concat conveniences, which allocate the number string before
// reading the string argument -- their caller must keep the argument
// reachable (generated-code frame roots do this automatically; C++ tests
// must add static roots).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Ordinal static Compare (see header: null < non-null; unit diff; length diff).
int32_t icall_string_compare_static(Il2CppObject* a, Il2CppObject* b);

// MODE-AWARE static Compare (string.Compare(a, b, StringComparison)):
// Ordinal exact; IgnoreCase folds ASCII; culture forms degrade to the fold
// (documented ASCII-domain deviation).
int32_t icall_string_compare_mode(Il2CppObject* a, Il2CppObject* b, int32_t mode);

// Ordinal static Equals (both null -> true; one null -> false; content).
bool icall_string_equals_static(Il2CppObject* a, Il2CppObject* b);

// Instance Equals(string value, StringComparison comparisonType) -- the
// comparison-mode form. The mode crosses as int32_t (0..5; the same enum as
// il2cpp_string_ext3.h's COMPARISON MODES contract). net48 (fresh-oracle
// verified): a mode outside 0..5 throws ArgumentException BEFORE any null
// handling; a null 'value' returns FALSE -- the comparison form of Equals is
// null-aware for 'value' (unlike Compare/StartsWith/EndsWith, which throw
// ArgumentNullException); the culture modes (0-3) are the ordinal equivalents
// (documented ASCII-domain deviation); the ignore-case modes (1, 3, 5) fold
// ASCII ('A'-'Z' <-> 'a'-'z'). A null receiver is a defensive NRE.
// READ-ONLY: no managed allocation.
bool icall_string_equals_culture(Il2CppObject* this_, Il2CppObject* value, int32_t comparisonType);

// string.Concat(string, string) -- null arguments count as empty. One managed
// allocation via il2cpp_string_concat_many (see il2cpp_concat.h).
Il2CppObject* icall_string_concat_2(Il2CppObject* a, Il2CppObject* b);

// string.Concat(string, string, string).
Il2CppObject* icall_string_concat_3(Il2CppObject* a, Il2CppObject* b, Il2CppObject* c);

// string.Concat(string, string, string, string).
Il2CppObject* icall_string_concat_4(Il2CppObject* a, Il2CppObject* b, Il2CppObject* c, Il2CppObject* d);

// Concat(object, object) convenience: 'value' is formatted with the net48
// ToString digits and appended to 'a' (null -> empty string). FAST PATH
// (perf1): ONE managed allocation -- the digits are formatted inline into a
// non-GC scratch buffer together with the 'a' payload, so no intermediate
// number string is created (previously icall_int32_to_string allocated a
// string, then il2cpp_string_concat_many re-walked it). The string payload is
// read into scratch BEFORE the single allocation (GC-safe, see the contract).
Il2CppObject* icall_string_concat_int(Il2CppObject* a, int32_t value);

// Concat(object, object) convenience: double appended via G15 formatting.
// NOTE: still delegates to icall_double_to_string (G15) + concat_many -- the
// G15 formatter is the dominant cost, the concat re-walk is not (perf1).
Il2CppObject* icall_string_concat_double(Il2CppObject* a, double value);

// Concat(object, object) convenience: "True" / "False" appended (.NET casing).
// FAST PATH (perf1): ONE managed allocation -- the literal units are written
// into the same scratch as the 'a' payload (no intermediate string).
Il2CppObject* icall_string_concat_bool(Il2CppObject* a, bool value);

// Concat(object, object) convenience: double appended via G15 formatting.
Il2CppObject* icall_string_concat_double(Il2CppObject* a, double value);

// Concat(object, object) convenience: "True" / "False" appended (.NET casing).
Il2CppObject* icall_string_concat_bool(Il2CppObject* a, bool value);

// string.IsNullOrEmpty: null or zero units.
bool icall_string_is_null_or_empty(Il2CppObject* s);

// string.IsNullOrWhiteSpace: null, zero units, or all Char.IsWhiteSpace.
bool icall_string_is_null_or_whitespace(Il2CppObject* s);

// string.Join(string, string[] / object[]): separator BETWEEN elements
// only; null separator / null elements count as empty; non-string elements
// (object[]) are converted with icall_object_to_string (.NET ToString
// semantics); null array -> ArgumentNullException; empty or non-reference
// array -> empty string (defensive). Single non-null STRING element ->
// identity (zero allocations, matches .NET Join(single string)); single
// non-string element -> its ToString() text (a new string). D-STR2-1: an
// object[] whose FIRST element is null returns String.Empty (net48 object[]
// overload shortcut), classified by IsObjectArray -- the string[] overload
// joins a null first element as an empty segment instead. One managed
// allocation (plus one per non-string element's ToString conversion -- keep
// the array and separator reachable, see the GC-ROOTING CONTRACT above).
Il2CppObject* icall_string_join(Il2CppObject* separator, Il2CppObject* valuesArray);

// string.Copy: a NEW instance with equal content (always allocates, even for
// ""); null -> ArgumentNullException. One managed allocation.
Il2CppObject* icall_string_copy(Il2CppObject* s);

// string indexer get_Chars: the UTF-16 unit at 'index' as int32.
// Out-of-range -> IndexOutOfRangeException; null receiver -> NRE (defensive).
// INLINE (perf1 fast path): the string layout is known at every call site, so
// the function body compiles into the caller -- a hot loop over s[i] becomes a
// plain char16_t read with the checks on the cold path, instead of a function
// call per indexer access (bench-array str_chars 0.27 -> inline; the .cpp
// definition was removed in favor of this header body, one definition site).
inline int32_t icall_string_get_chars(Il2CppObject* s, int32_t index)
{
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(s);
    if (str == nullptr)
    {
        il2cpp_throw_null_reference_exception("Object reference not set to an instance of an object.");
    }
    if (index < 0 || index >= str->length)
    {
        il2cpp_throw_index_out_of_range_exception("Index was outside the bounds of the string.");
    }
    return static_cast<int32_t>(str->chars[index]);
}

} // namespace runtime
} // namespace il2cpp
