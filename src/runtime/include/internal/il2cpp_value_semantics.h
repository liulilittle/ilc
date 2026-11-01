#pragma once
//------------------------------------------------------------------------------
// il2cpp_value_semantics.h -- value-semantics runtime icalls: the System.Object
// virtual/static methods every boxed value and string participates in
// (GetHashCode / ToString / Equals) plus the per-type GetHashCode icalls the
// transpiler maps the BCL value-type methods to.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- empirically verified by the
// valuesem e2e reference program (tests/cases/valuesem). Hash VALUES below are
// net48-exact (the unit tests pin them).
//
// Hash strategy table (per type -- ALL consistent with the hashtable runtime,
// hashtable.cpp, so "equal keys -> equal hashes" holds across every use):
//   System.String   -> ordinal content hash: the SAME simplified .NET
//                      Framework GetHashCode as hashtable.cpp's
//                      HashStringOrdinal (5381<<16+5381 dual accumulator, *33,
//                      0x5D588B65 combine). DELIBERATELY unrandomized: net48
//                      randomizes the seed PER PROCESS (DoS hardening), so raw
//                      values are not reproducible across processes -- only
//                      same-process stability is guaranteed; AOT output must
//                      be deterministic, so this runtime pins the seed.
//   System.Int32    -> the value itself (Int32.GetHashCode).
//   System.Int64    -> low 32 bits XOR high 32 bits (Int64.GetHashCode).
//   System.Double   -> net48 Double.GetHashCode: bit pattern with the
//                      d == 0 shortcut, so +0.0 and -0.0 BOTH hash to 0;
//                      otherwise unchecked((int)bits) ^ (int)(bits >> 32).
//                      NaN: net48 does NOT unify payloads (probed with
//                      BitConverter.Int64BitsToDouble -- an exotic payload
//                      hashes differently from double.NaN); this runtime
//                      mirrors net48 exactly. The C# canonical NaN pattern
//                      (0xFFF8000000000000 -- every common NaN-producing
//                      operation on x86 yields it) hashes to -524288
//                      (constant in practice).
//   System.Boolean  -> true -> 1, false -> 0 (Boolean.GetHashCode).
//   other objects   -> ADDRESS hash (ptr >> 3 -- same as hashtable address
//                      keys; see icall_object_get_hash_code).
//
// Dispatch contracts (Object.GetHashCode / Object.ToString / Object.Equals):
//   icall_object_get_hash_code -- string -> ordinal hash; boxed Int32 -> the
//       value; anything else -> address hash; null -> NullReferenceException.
//       KNOWN DIVERGENCE vs net48: a boxed bool/long/double hashed through
//       the object path gives the ADDRESS hash here (net48 dispatches the
//       virtual value-type GetHashCode and yields 1 / high^low / bits-based).
//       The per-type icalls below carry the net48 semantics; generated code
//       calls them directly for statically-known value types, so only
//       genuinely object-typed dispatch takes the address-hash path.
//   icall_object_to_string -- string -> the string ITSELF (identity); boxed
//       Int32 -> decimal; boxed Boolean -> "True"/"False"; anything else ->
//       the type's full name (klass->name); null -> NullReferenceException.
//       KNOWN DIVERGENCE vs net48: net48 prints the VALUE for boxed
//       long/double/char; this runtime prints the type name (M-scope
//       simplification -- il2cpp_format.h icalls exist for future wiring).
//   icall_object_equals -- static Object.Equals: reference identity first;
//       two strings -> ordinal content equality; two boxes with the SAME
//       klass name -> value equality (double NaN == NaN is TRUE, net48);
//       everything else -> false. null,null -> true (net48).
//
// Exception transport: il2cpp_throw_null_reference_exception (exceptionext)
// for null receivers -- chain "{Type},{Type},{SystemException},{Exception}".
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h" // Il2CppClass / Il2CppVtField (the user-struct default hash)
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Object.GetHashCode virtual dispatch (see the hash strategy table above).
int32_t icall_object_get_hash_code(Il2CppObject* obj);

// Object.ToString virtual dispatch (see the dispatch contracts above).
// Returns Il2CppString* (an Il2CppObject* by layout -- same ABI).
Il2CppString* icall_object_to_string(Il2CppObject* obj);

// Static Object.Equals(a, b) (see the dispatch contracts above).
bool icall_object_equals(Il2CppObject* a, Il2CppObject* b);

// Value-type GetHashCode icalls (net48-exact values; see the table).
int32_t icall_int32_get_hash_code(int32_t value);
int32_t icall_int64_get_hash_code(int64_t value);
int32_t icall_double_get_hash_code(double value);
int32_t icall_single_get_hash_code(double value);
int32_t icall_string_get_hash_code(Il2CppObject* str);

// string.GetHashCode(StringComparison) (net core 2.0+; net48 has no such
// overload). Ordinal is the legacy ordinal hash; OrdinalIgnoreCase folds
// ASCII before hashing (equal-under-fold strings hash equally). The four
// CULTURE modes are net48-EXACT: they route through the culture hash below
// (see il2cpp_culture_hash's contract). A mode outside 0..5 throws
// ArgumentException (the StringComparison contract).
int32_t icall_string_get_hash_code_mode(Il2CppObject* str, int32_t comparisonType);
int32_t icall_bool_get_hash_code(bool value);

// Culture-aware string hash -- EXACTLY net48's
// CultureInfo.CompareInfo.GetHashCode(string, CompareOptions) /
// StringComparer.{Current,Invariant}Culture[IgnoreCase].GetHashCode(string).
//
// Measured construction (net48 probe, 2026-11-01; see
// tests/runtime/test_urihash.cpp): .NET does NOT case-fold and then re-use
// the ordinal hash. It asks the NLS/ICU layer for the string's SORT KEY and
// folds the sort-key BYTES with the same two-accumulator (5381 base, *33)
// combiner the ordinal hash uses over UTF-16 units:
//
//   key = LCMapStringEx(locale, LCMAP_SORTKEY | (ignoreCase ? NORM_IGNORECASE : 0),
//                       chars, length, ...)            // n = cchResult - 1 (the API
//                                                      // counts the terminating NUL)
//   h1 = h2 = 5381;
//   for (i = 0; i < n; i += 2) { h1 = ((h1 << 5) + h1) ^ key[i];
//                                if (i + 1 < n) h2 = ((h2 << 5) + h2) ^ key[i + 1]; }
//   return h1 + h2 * 1566083941;                        // 32-bit wrap
//
// 'locale' is "" (LOCALE_NAME_INVARIANT) for the invariant culture and the
// culture NAME for a current-culture query; "" and the user-default locale
// agree on the invariant identity. NORM_IGNORECASE is the ONLY flag the
// oracle uses -- SORT_STRINGSORT / NORM_IGNORENONSPACE / NORM_IGNORESYMBOLS /
// NORM_IGNOREWIDTH / LINGUISTIC_IGNORECASE all measurably DIVERGE from it.
//
// The empty string hashes to 0 (net48) and a null string throws
// ArgumentNullException. Hosts without an NLS sort-key API fall back to the
// ASCII case fold over UTF-16 units (a documented, deterministic POSIX
// fallback -- it is NOT net48-exact, and no Windows/Android target takes it).
int32_t il2cpp_culture_hash(const Il2CppString* s, const char* locale, bool ignoreCase);

// The narrow integral/char GetHashCode family (net48-exact). The value rides the
// int32 slot (the CLR widens every narrow cell to the int32 parameter); 'kind'
// selects the rule:
//   0 sbyte   -> v ^ (v << 8)              (v = signed 8-bit)
//   1 byte    -> value & 0xFF
//   2 short   -> (ushort)v | (v << 16)     (v = signed 16-bit)
//   3 ushort  -> value & 0xFFFF
//   4 char    -> value | (value << 16)
//   5 uint    -> value (the full 32-bit pattern)
int32_t icall_narrow_get_hash_code(int32_t value, int32_t kind);

//------------------------------------------------------------------------------
// icall_value_type_get_hash_code -- the System.ValueType.GetHashCode default
// implementation for a TRANSLATED value type (the constrained-callvirt
// surface; transpiler-injected constants).
//
//   typeHash  : the deterministic per-type hash (djb2 over the full type
//               name, computed by the transpiler) -- returned verbatim when
//               the struct has no instance fields (net48's "no fields ->
//               type hash" rule).
//   fieldKind : the first instance field's kind, decided at COMPILE time by
//               the transpiler from the metadata (the runtime has no value-
//               type descriptors -- value types are plain C++ values):
//                 0 = no instance fields     -> return typeHash
//                 1 = first field is a value -> return the int32 at data[0]
//                 2 = first field is a GC reference -> hash the object at
//                     data[0] (null -> 0)
//   data      : the value-type receiver's ADDRESS (the first field always
//               sits at offset 0 -- the transpiler's value-type slot grid
//               starts at 0, no object header).
//
// SEMANTIC BASELINE: the DOCUMENTED net48 rule (netcore ValueType.cs:89-94):
// "look for the first non-static field and get its hashcode; if the type has
// no non-static fields, return the hashcode of the type." NOTE: the probed
// net48 byte-level behavior is a process-randomized whole-struct hash (the
// documented rule is NOT what net48 actually computes) -- the oracle
// discrepancy is documented in discover.md / the array-vt-gap-report.
//------------------------------------------------------------------------------
int32_t icall_value_type_get_hash_code(int32_t typeHash, int32_t fieldKind, const void* data);

//------------------------------------------------------------------------------
// icall_user_struct_default_hash -- the net48 ValueType.GetHashCode default for
// a TRANSLATED user struct.
//
// net48 (desktop CLR mscorlib ValueType.GetHashCode) -- MEASURED against .NET
// Framework 4.8; see docs/audit/b5-gaps.md for the probe record:
//
//     hash = munge(typeID) ^ fieldImage
//     munge(id) = id * 711650207 + 2506965631U          (32-bit wrap)
//
//   FAST path (the struct is tightly packed -- NO padding bytes -- and holds
//   no GC reference):
//       fieldImage = XOR of every complete 4-byte word of the NET48 image
//                    (a trailing 1..3-byte remainder is DROPPED)
//
//   REGULAR path (any padding, or any GC reference):
//       fieldImage = XOR of the FIRST NON-NULL field's own bytes, 4-byte words
//                    (so a 1/2-byte field contributes 0, a 4-byte field its
//                    value, an 8-byte field lo32^hi32, a float its raw bits);
//                    a NULL reference field is skipped and the next one is
//                    tried; a non-null reference contributes its object hash.
//
//   `fields` describes the instance fields in DECLARATION order (the transpiler
//   emits it next to the generated struct, because the generated C++ layout
//   WIDENS short/byte fields into int32 slots while net48 packs them).
//
// 'typeID' is the CLR ValueType typeID: assigned LAZILY on the first
// ValueType.GetHashCode call for that type from a counter that starts at 3 and
// advances by 2. il2cpp_user_struct_type_id owns that ONE counter, shared by
// this path and the tuple-element path, exactly as net48 does.
//------------------------------------------------------------------------------
int32_t icall_user_struct_default_hash(const void* data, const Il2CppVtField* fields,
                                       const Il2CppClass* klass);

// The shared lazy ValueType typeID counter (3, 5, 7, ... in first-call order).
int32_t il2cpp_user_struct_type_id(const Il2CppClass* klass);

// Upper bound on a user struct's net48 natural-layout size for the default
// hash's image assembly (the transpiler rejects larger structs from the fast
// path; a struct beyond this uses the regular first-field path).
static const int32_t kMaxVtPackedSize = 512;

} // namespace runtime
} // namespace il2cpp
