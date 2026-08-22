#pragma once
//------------------------------------------------------------------------------
// il2cpp_string.h -- managed string type (System.String).
//
// Layout (aligned with .NET's string representation):
//
//   struct Il2CppString
//   {
//       Il2CppObject obj;    // object header: klass pointer (System.String descriptor)
//       int32_t      length; // number of char16_t units (NOT bytes)
//       char16_t     chars[]; // payload: 'length' UTF-16 code units
//   };
//
//   The string is a GC object: il2cpp_string_new* allocates through the
//   collector (size-extended allocation -- the payload bytes exceed the
//   fixed descriptor instanceSize, so the collector's alloc_size path is
//   used and the block table records the true extent).
//
//   Encoding notes:
//     * 'length' counts UTF-16 code units. Surrogate pairs (astral
//       characters) occupy TWO units -- matching .NET's string length
//       semantics exactly.
//     * The literal-creation helpers (new_from_cs / new_from_utf8) accept
//       ASCII / UTF-8 byte strings and transcode to UTF-16 units.
//
//   M3 scope: creation + layout + WriteLine printing. String OPERATIONS
//   (Concatenate/Substring/Compare/IndexOf/...) are BCL methods transpiled
//   against this layout -- they arrive with the BCL tasks; the runtime
//   exposes the raw allocation/creation primitives here.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_exception_ext.h"

#include <cstdint>

namespace il2cpp {

// Managed string: object header + length + UTF-16 payload (see header comment).
struct Il2CppString
{
    Il2CppObject obj;       // object header (klass pointer)
    int32_t      length;    // number of char16_t units (not bytes)
    char16_t     chars[1];  // payload: 'length' units follow inline
};

// Allocate a managed string holding 'length' UTF-16 units copied from 'chars'
// (null chars allowed for length == 0). The string is zero-initialized by the
// collector and then filled from the source. Returns a GC reference; the
// caller must keep it reachable (frame root / field / parameter) until used.
Il2CppString* il2cpp_string_new(const char16_t* chars, int32_t length);

// Allocate a managed string from a null-terminated UTF-8 byte string (the
// icall / native boundary form; ASCII is a subset). The byte length is
// determined by strlen; multi-byte UTF-8 sequences are transcoded to UTF-16
// units (M3 minimal: BMP only -- surrogates from astral input are NOT
// generated yet; such input is truncated to its ASCII prefix defensively).
Il2CppString* il2cpp_string_new_from_utf8(const char* utf8);

// Allocate a managed string from a null-terminated ASCII literal (generated
// code path for ldstr of a compile-time constant). 'ascii' must contain only
// bytes 0x00-0x7F; each byte becomes one UTF-16 unit.
Il2CppString* il2cpp_string_new_from_cs(const char* ascii);

// The System.String runtime descriptor (registered lazily on first use).
// Exposed for tests and the type registry; the descriptor's instanceSize
// covers the header + length slot only (payload is size-extended allocation).
Il2CppClass* il2cpp_string_class();

// M4 Task 5: System.String.Length -- the string's UTF-16 unit count.
// INLINE: a single cheap null test plus a plain field read, so the C++
// compiler can hoist the non-null path out of hot loops. Following .NET
// semantics, a null receiver throws NullReferenceException -- but the throw
// sits on the error path (rare, cold branch); the common path is one
// compare-and-branch, no call.
inline int32_t il2cpp_string_get_length(Il2CppObject* s)
{
    Il2CppString* str = reinterpret_cast<Il2CppString*>(s);
    if (str == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    return str->length;
}

//==============================================================================
// String allocation roots -- the Core FCall table gStringFuncs entries
// (reference/netcore/src/coreclr/vm/ecalllist.h): FastAllocateString plus the
// 8 .NET Framework constructor forms. The ILC runtime implements the same
// allocation family so every string-construction path (transpiled `new
// string(...)` and the FCall table's fast allocation root) shares ONE
// allocation discipline: il2cpp_string_new with the true size-extended extent
// (see string.cpp StringAllocSize).
//
// SEMANTIC BASELINE: .NET Framework 4.8, empirically verified against the
// reference runtime (stralloc net48 probe; edge values quoted in the
// test_stringalloc.cpp comments). Cross-platform: all GC allocation goes
// through the existing il2cpp_string_new (never a host-codepage allocation).
//
// The sbyte* ctor forms decode the byte payload as UTF-8 (net48 decodes the
// system ANSI code page on Windows -- documented deviation, matching
// il2cpp_string_new_from_utf8's net48 replacement-fallback codec).
//------------------------------------------------------------------------------
namespace runtime {

// FastAllocateString(int32_t length): allocate a string holding 'length'
// UTF-16 units WITHOUT filling the content (the collector zero-initializes
// the payload; the caller fills it). This is the FCall table's allocation
// root that every string ctor builds on. A negative length is clamped to 0
// (defensive; the managed ctors validate before calling).
Il2CppObject* icall_string_fast_allocate(int32_t length);

// System.String..ctor(char[]): copy the whole array. null value -> the empty
// string (net48: NOT ArgumentNullException -- probe carr_null=RET:""), an
// empty array -> the empty string, otherwise a fresh copy of every unit.
Il2CppObject* icall_string_ctor_char_array(Il2CppObject* this_, Il2CppObject* value);

// System.String..ctor(char[], int32_t, int32_t): copy a window. net48 order:
// null value -> ArgumentNullException; startIndex < 0 -> AOO("startIndex");
// length < 0 -> AOO("length"); startIndex > value.Length - length -> AOO
// (covers both startIndex beyond the tail and window overflow); length == 0
// -> the empty string (a legal (Length, 0) window).
Il2CppObject* icall_string_ctor_char_array_range(Il2CppObject* this_, Il2CppObject* value,
                                                  int32_t startIndex, int32_t length);

// System.String..ctor(char*): copy a null-terminated UTF-16 buffer (wcslen
// semantics -- the terminator is NOT included). null value -> the empty
// string. UNSAFE pointer form (see the LOUD note in the wiring report).
Il2CppObject* icall_string_ctor_char_ptr(Il2CppObject* this_, const char16_t* value);

// System.String..ctor(char*, int32_t, int32_t): copy 'length' units from
// value + startIndex (NO null-terminator scan -- net48 copies exactly the
// window, probe cptrss_start0_len6 returns the embedded NUL). net48 order:
// length < 0 -> AOO("length"); startIndex < 0 -> AOO("startIndex"); the
// value+startIndex address wraps -> AOO; length == 0 -> the empty string
// (checked BEFORE the null test); value == null -> AOO("value"). UNSAFE
// pointer form (see the LOUD note in the wiring report).
Il2CppObject* icall_string_ctor_char_ptr_range(Il2CppObject* this_, const char16_t* value,
                                                int32_t startIndex, int32_t length);

// System.String..ctor(sbyte*): copy a null-terminated byte buffer (strlen
// semantics) decoded as UTF-8 (net48 uses the ANSI code page -- documented
// deviation). null value -> the empty string. UNSAFE pointer form.
Il2CppObject* icall_string_ctor_sbyte_ptr(Il2CppObject* this_, const char* value);

// System.String..ctor(sbyte*, int32_t, int32_t): decode 'length' bytes from
// value + startIndex as UTF-8. net48 order: startIndex < 0 -> AOO;
// length < 0 -> AOO; value == null -> ArgumentNullException (UNCONDITIONALLY
// -- even for length == 0, probe sbptrs_null_start0_len0; differs from the
// char* form); the value+startIndex address wraps -> AOO; length == 0 -> the
// empty string. UNSAFE pointer form.
Il2CppObject* icall_string_ctor_sbyte_ptr_range(Il2CppObject* this_, const char* value,
                                                 int32_t startIndex, int32_t length);

// System.String..ctor(sbyte*, int32_t, int32_t, Encoding): decode 'length'
// bytes from value + startIndex with an explicit encoding. The Encoding
// object crosses as its code-page number (il2cpp_encoding_code_page value;
// 0 == null encoding -> the 3-arg form, mirroring net48's null-delegate).
// net48 order (non-null encoding): length < 0 -> AOO; startIndex < 0 -> AOO;
// value == null -> length == 0 ? the empty string : AOO("value"); the
// value+startIndex address wraps -> AOO; then decode via the code page.
// UNSAFE pointer form.
Il2CppObject* icall_string_ctor_sbyte_ptr_range_encoding(Il2CppObject* this_, const char* value,
                                                          int32_t startIndex, int32_t length,
                                                          int32_t encoding);

// System.String..ctor(ReadOnlySpan<char>) (net5+; raw sig 20-01-01-15-...):
// the span rides as a caller SLOT with the two-field local-storage layout
// { const char16_t* ptr; int32_t length }. Create-style: receiver dropped;
// null/empty -> the empty string.
Il2CppObject* icall_string_ctor_char_span(Il2CppObject* this_, const void* span);

} // namespace runtime

} // namespace il2cpp
