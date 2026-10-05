#pragma once
//------------------------------------------------------------------------------
// il2cpp_concat.h -- single-allocation multi-segment string concatenation.
//
// PURPOSE
//   A .NET expression like "a" + b + c compiles to a chain of string.Concat
//   calls; every step allocates a fresh managed string, so N segments cost
//   N-1 managed allocations (and O(N * totalLength) unit copies -- each
//   intermediate string re-copies the whole prefix). These helpers collapse
//   that pattern into ONE managed allocation, mirroring .NET Core's Concat
//   fast path and StringBuilder's grow-once-then-fill strategy:
//
//     pass 1 : sum the segment lengths (single traversal of metadata)
//     stage  : copy all payloads into a NON-GC pool scratch buffer
//     finish : ONE il2cpp_string_new(scratch, total) -- the same creation
//              API string.cpp uses, called exactly once
//
// ALLOCATION COMPLEXITY (vs naive chained concat)
//   naive:   N-1 managed allocations, O(N * totalLength) unit copies.
//   here:    1 managed allocation + 1 non-GC pool scratch (O(1) bump, freed
//            immediately after the copy) + exactly totalLength unit copies.
//   Identity fast paths (single non-empty input) return the INPUT string
//   unchanged: ZERO allocations (matches .NET Concat(single) / Join(single)).
//
// GC SAFETY (physical contract -- see il2cpp_gc.h)
//   il2cpp_gc_alloc_size can trigger a collection when the heap cannot
//   satisfy an allocation (collect-then-retry). Every managed memory read
//   (segment lengths and payloads) therefore happens BEFORE the single GC
//   allocation; the staged content lives in non-GC pool memory, so a
//   mid-call collection can never invalidate the copied data.
//
// NULL POLICY (String.Concat semantics)
//   null segments and null array elements count as empty strings. A null
//   array itself yields an empty string (defensive -- .NET throws
//   ArgumentNullException, but this icall never crashes its caller).
//   Length overflow beyond Int32.MaxValue throws OverflowException
//   (unreachable in practice; the guard keeps the contract honest).
//
// OBJECT POLICY (String.Concat(object,..) / Concat(params object[]) semantics)
//   Non-string arguments and array elements are converted with their net48
//   ToString() text (icall_object_to_string): boxed primitives format their
//   VALUE (Int32/Boolean/etc.), any other object prints its type's full name
//   -- the same conversion the join domain uses (ElementAsSegment). This makes
//   the fixed-arity forms (2-N, the object overloads route here) and the
//   object[] array form net48-correct.
//
// EMPTY-RESULT IDENTITY (net48)
//   An empty result (all-null / all-empty segments, count <= 0, an empty
//   array) returns the INTERNED empty string (String.Empty -- the same
//   instance the intern pool returns for "" and StackFold's String.Empty fold
//   resolves to): ReferenceEquals(Concat("", ""), "") == true, matching net48.
//
// EMPTY-SEGMENT IDENTITY (net48 Concat(s0, s1[, ...]))
//   When exactly ONE argument is non-empty (the others null/empty), the result
//   IS that argument -- returned by reference, zero allocations (net48's
//   IsNullOrEmpty fast paths). Only a STRING argument is eligible for the
//   identity fast path (a boxed object's ToString result is a fresh string);
//   boxed-object inputs fall through to the single-allocation path with
//   identical content.
//
// GC-ROOTING CONTRACT (physical -- see il2cpp_gc.h)
//   Managed arguments are native C++ parameters and are NOT frame roots. The
//   pure-string paths read every managed payload BEFORE the single managed
//   allocation (the final il2cpp_string_new). A NON-STRING argument/element's
//   ToString conversion ALLOCATES during the length pass (the numeric-concat
//   convenience exception, il2cpp_string_ext2.h): callers must keep the array
//   and arguments reachable for the whole call (generated-code frame roots do
//   this automatically; C++ tests must add static roots). Pass 2 re-converts
//   and copies IMMEDIATELY (no allocation between conversion and copy), so a
//   later conversion can never reclaim already-staged content.
//
// All results are GC references: the caller must keep them reachable (frame
// root / field / parameter) until used.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Concatenate 'count' string segments (variadic Il2CppString* arguments) into
// ONE freshly allocated string. count <= 0 yields an empty string; null
// segments count as empty; a single non-null segment is returned unchanged
// (identity, zero allocations). Content equals string.Concat(a, b, c, ...).
// Complexity: 1 managed allocation (+1 non-GC scratch) vs N-1 naive.
Il2CppString* il2cpp_string_concat_many(int32_t count, ...);

// Concatenate the elements of a string[] array into ONE freshly allocated
// string (String.Concat(string[]) semantics). A null array or a null element
// counts as empty; an array with a single non-null element is returned
// unchanged (identity, zero allocations). Complexity: 1 managed allocation
// vs N-1 naive.
Il2CppString* il2cpp_string_concat_array(Il2CppObject* arr);

// Join the elements of a string[] array with a single UTF-16 separator unit
// between them into ONE freshly allocated string (string.Join semantics with
// a one-char separator). The separator appears only BETWEEN elements (never
// leading or trailing); a null element counts as empty; a single non-null
// element is returned unchanged (identity). Complexity: 1 managed allocation
// vs N-1 naive concatenations with N-1 separator insertions.
Il2CppString* il2cpp_string_builder_join(char16_t separator, Il2CppObject* values);

// Repeat string 's' 'count' times into ONE freshly allocated string (the
// optimization of a count-way repeated Concat). count <= 0 yields an empty
// string; count == 1 returns 's' unchanged (identity, zero allocations).
// Complexity: 1 managed allocation vs count-1 naive.
Il2CppString* il2cpp_string_repeat(Il2CppObject* s, int32_t count);

} // namespace runtime
} // namespace il2cpp
