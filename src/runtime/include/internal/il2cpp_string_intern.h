#pragma once
//------------------------------------------------------------------------------
// il2cpp_string_intern.h -- string literal interning (.NET String.Intern pool).
//
// PURPOSE
//   .NET semantics: every occurrence of the same string literal is the SAME
//   instance (interned). The generated-code ldstr path used to allocate a
//   fresh string on EVERY execution (il2cpp_string_new_from_cs), which both
//   allocates per use and breaks reference equality between identical
//   literals. These helpers restore the .NET contract:
//
//     il2cpp_string_intern(str)        -- pool lookup by CONTENT:
//                                           hit  -> the pool instance
//                                           miss -> 'str' is inserted and
//                                                   returned (identity)
//     il2cpp_string_intern_literal(s)  -- ldstr path: pool lookup by content;
//                                           miss -> create + insert
//
//   The pool holds STRONG references: interned strings are never collected
//   (the .NET intern pool has the same lifetime semantics).
//
// INTERN TABLE (GC-safe managed structure)
//   The pool is an open-addressing hash table stored in a managed REFERENCE
//   ARRAY (Il2CppObject*[], "[System.Object" descriptor, isRefElem = true):
//     * the collector scans the array's payload element-wise (gc.cpp
//       ScanObject keys on the '[' array-class prefix + isRefElem flag), so
//       every interned string is marked from the table alone -- no descriptor
//       refFieldBitmap is involved (array payloads are scanned dynamically);
//     * the table pointer lives in a C++ static global REGISTERED AS A GC
//       STATIC ROOT (il2cpp_gc_add_static_root, lazily on first use) -- the
//       table itself can never be collected and Phase C (compaction reference
//       update) rewrites the root when the table is moved;
//     * every reference-slot store goes through il2cpp_write_barrier first
//       (old-gen storing object -> card marked; same convention as
//       arrayext2/hashtable) so a young-only collection can never miss an
//       interned string stored into an old table;
//     * all pointer re-reads happen from the GLOBAL after any allocation
//       (a collection may move the table -- C++ locals are NOT rewritten).
//
// LOOKUP / HASH CONTRACT
//   Keys compare by CONTENT (ordinal): length + payload bytes. The hash is
//   FNV-1a over UTF-16 units with a final avalanche mixer (a plain FNV over
//   16-bit units leaves the low index bits dominated by the last unit -- the
//   mixer spreads every unit over the slot mask). Slot count is always a
//   power of two (mask arithmetic for probe wraparound -- see the hashtable
//   domain's 2^N lesson).
//
// CAPACITY POLICY
//   The table grows by doubling at a 70% load factor, capped at 2^20 slots.
//   If the cap is reached with the table full, the pool reports loudly
//   (stderr message + abort -- same loud-abort contract as the array bounds
//   check); it never silently stops interning.
//
// THREAD SAFETY (net48 parity)
//   String.Intern is documented thread-safe; this runtime spawns real OS
//   threads (threadobj Start, threadpool workers) whose generated callbacks
//   can reach the pool through ldstr and String.Intern -- including a
//   concurrent GROW while another thread probes. Every public entry point
//   (il2cpp_string_intern / il2cpp_string_intern_literal) serializes the
//   whole find/insert/grow operation under an internal recursive mutex
//   (stringintern.cpp). The mutex is recursive because the entries nest
//   (intern_literal delegates to intern for a null input) and a pool
//   operation may allocate, which can collect and run finalizers on the same
//   thread -- a finalizer may itself call intern.
//
// DEFENSIVE INPUT POLICY (C-ABI form)
//   null input        -> returns nullptr (documented; .NET String.Intern
//                        would throw ArgumentNullException -- generated code
//                        never passes null, ldstr literals are non-null)
//   non-string input  -> returned unchanged (a caller bug; content lookup
//                        requires System.String instances)
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"

namespace il2cpp {

// Intern an existing managed string: returns the pool instance with the same
// CONTENT if one exists, otherwise inserts 'str' (strong reference) and
// returns it. null and non-string inputs are returned unchanged (defensive,
// see the header comment).
Il2CppObject* il2cpp_string_intern(Il2CppObject* str);

// Check if a string is in the intern pool WITHOUT adding it (.NET
// String.IsInterned: returns the interned string if found, nullptr if not).
Il2CppObject* il2cpp_string_is_interned(Il2CppObject* str);

// Intern a null-terminated UTF-8 literal (the generated-code ldstr path):
// pool lookup by content -- hit returns the pool instance, miss creates the
// managed string (il2cpp_string_new_from_utf8 semantics: ASCII units 1:1,
// multi-byte bytes skipped defensively) and inserts it. Same-instance
// guarantee: two calls with the same content return the same pointer.
Il2CppObject* il2cpp_string_intern_literal(const char* utf8);

} // namespace il2cpp
