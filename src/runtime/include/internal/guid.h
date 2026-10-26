#pragma once
//------------------------------------------------------------------------------
// guid.h -- System.Guid runtime support (16-byte value type).
//
// A Guid is an opaque 16-byte value with .NET-defined byte order (the same
// layout the BCL produces on little-endian platforms): the four int fields
// Data1/Data2/Data3 are stored little-endian (byte[0] is Data1's LSB), the
// eight Data4 bytes follow in order. All helpers here operate on the raw
// 16 bytes; the BCL layout contract is what parse/to_string are based on:
//
//   byte[0..3]  Data1  (LE int32)   -> first 8 hex chars
//   byte[4..5]  Data2  (LE int16)   -> next 4 hex chars
//   byte[6..7]  Data3  (LE int16)   -> next 4 hex chars
//   byte[8..15] Data4  (8 bytes)    -> last 16 hex chars
//
// String form is the .NET "D" format, ALWAYS lowercase:
//   "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"   (36 chars, dashes at 8/13/18/23)
//   e.g. Guid(0x00112233,0x4455,0x6677,0x88,0x99,0xaa,0xbb,0xcc,0xdd,0xee,0xff)
//   -> "00112233-4455-6677-8899-aabbccddeeff"
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline:
//   * CompareTo is field-wise: Data1 (unsigned LE int32), then Data2
//     (unsigned LE int16), then Data3 (unsigned LE int16), then Data4 bytes
//     (unsigned) -- the first differing field decides; unsigned compares mean
//     a Guid whose Data1 has the high bit set sorts AFTER one with Data1 == 1.
//   * ToString("D") is lowercase even when the Guid was parsed from an
//     uppercase string.
//   * NewGuid() is RFC 4122 v4 over the OS CSPRNG (see below): the version
//     nibble (4) lands in the first hex char of the third string group and
//     the variant bits are 10xx xxxx -- net48 Guid.NewGuid() carries no such
//     format guarantee, so tests asserting these bits are our own (stricter)
//     contract, not net48's.
//   * Parse/TryParse accept BOTH the 36-char "D" form (dashes at 8/13/18/23)
//     and the 32-char "N" form (no dashes), matching net48's input formats.
//
// Deliberate deviation: the parse core is STRICT (TryParse-style, returns
// bool) -- exactly the "D"/"N" forms above, no surrounding braces ("B"),
// no parentheses ("P"), no "X" form, no whitespace; anything else is
// rejected with false and the output buffer untouched. Guid.Parse maps to
// the strict WRAPPER below, which recovers the net48 throwing semantics on
// top of that core.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {

// The 16-byte System.Guid VALUE image (the guid.h ABI: every helper above
// takes uint8_t[16]). A POD wrapper is what generated C++ declarations name
// for System.Guid fields/locals/signatures; the byte layout is identical to
// the raw arrays the icalls use. alignas(4) matches the net48 field
// alignment (Data1 int32 / Data2+Data3 int16 / Data4 byte[8]).
struct alignas(4) Il2CppGuid
{
    uint8_t b[16];
};

// NewGuid(): write a fresh GUID's 16 bytes to 'bytes'. All 16 bytes come
// from the OS CSPRNG through il2cpp::runtime::il2cpp_rng_fill (rng.h -- the
// same hardened source that backs System.Security.Cryptography's RNG; it
// throws CryptographicException if every OS provider fails, so no weak
// source is ever substituted). The output is then stamped per RFC 4122
// section 4.4: the version nibble (4) in byte[7] high nibble (Data3's MSB --
// the first hex char of the third "D"-form group) and the variant bits
// (10xx xxxx) in byte[8].
void il2cpp_guid_new_guid(uint8_t bytes[16]);

// Guid(int32, int16, int16, byte[8]) -- the 11-argument component ctor
// (see guid.cpp for the layout contract).
void il2cpp_guid_ctor_components(uint8_t bytes[16],
                                 int32_t a, int32_t b, int32_t c,
                                 int32_t d, int32_t e, int32_t f, int32_t g,
                                 int32_t h, int32_t i, int32_t j, int32_t k);

// TryParse of the strict forms (see header comment): the 36-char "D" form
// (dashes at 8/13/18/23) or the 32-char "N" form (hex digits only, no
// dashes -- net48 accepts both). Hex digits are 0-9 a-f A-F.
// Returns true and writes the 16 bytes only on full validation; returns
// false (bytes untouched -- verify-then-mutate) for a null string, wrong
// length, misplaced/missing dash, or any non-hex character.
bool il2cpp_guid_parse(Il2CppString* s, uint8_t bytes[16]);

// Guid.Parse semantics (net48): the same strict forms as
// il2cpp_guid_parse, but failure THROWS instead of returning false -- a null
// string throws ArgumentNullException, any other invalid input throws
// FormatException. On success writes the 16 bytes (the core's
// verify-then-mutate leaves them untouched on failure).
void il2cpp_guid_parse_strict(Il2CppString* s, uint8_t bytes[16]);

// Guid(byte[] b) -- the net48 from-bytes ctor. A null array is
// ArgumentNullException; a length other than 16 is ArgumentException; otherwise
// the 16 bytes are copied verbatim (the .NET byte layout IS the storage
// layout). The icall writes the receiver's 16-byte value slot (the emitter
// passes &slot, ResultIsReceiver-style -- the il2cpp_guid_ctor_string
// precedent).
void il2cpp_guid_ctor_bytes(uint8_t bytes[16], Il2CppArray* b);

// new Guid(string): the managed ctor is Guid.Parse (strict); this wrapper
// puts the 16-byte output slot FIRST to match the transpiler's in-place
// value-type ctor call shape `il2cpp_guid_ctor_string(slot, string)`.
inline void il2cpp_guid_ctor_string(uint8_t bytes[16], Il2CppString* s)
{
    // The transpiler's "s" cast renders the managed string as Il2CppString*;
    // the strict parse wrapper takes the same pointer.
    il2cpp_guid_parse_strict(s, bytes);
}

// Same value-type ctor call shape where the IR argument is the generic
// managed-string slot type (string-pool entries are Il2CppObject*): keep the
// historical object-typed overload so both emission forms compile.
inline void il2cpp_guid_ctor_string(uint8_t bytes[16], Il2CppObject* s)
{
    il2cpp_guid_parse_strict(reinterpret_cast<Il2CppString*>(s), bytes);
}

// ToString("D"): the 36-char lowercase form above, as a managed string.
Il2CppString* il2cpp_guid_to_string(const uint8_t bytes[16]);

// ToString(format): the full .NET family -- null/"D" dashed, "N" bare,
// "B" braced, "P" parenthesised, "X" the {0x...} hex form. Case-insensitive;
// anything else is FormatException (net48 contract).
Il2CppString* il2cpp_guid_to_string_format(const uint8_t bytes[16], Il2CppString* format);

// Value equality: true iff all 16 bytes match.
bool il2cpp_guid_equals(const uint8_t a[16], const uint8_t b[16]);

// net48 Guid.GetHashCode: the ORACLE-probed fold (20000 random GUIDs + all
// 128 single-bit patterns, 0 mismatch against .NET Framework 4.8):
//   LE32(bytes[0..3]) ^ ((int16 LE(bytes[4..5]) << 16) | LE16(bytes[6..7]))
//   ^ (bytes[10] << 24) ^ bytes[15]
// NOTE the referencesource formula folds bytes 8..14 as well; the net48
// implementation does NOT (probe: bytes[8], bytes[9], bytes[11..14] never
// affect the hash; bytes[10] contributes <<24; bytes[15] contributes the
// low byte). The oracle wins over the reference text.
int32_t il2cpp_guid_get_hash_code(const uint8_t bytes[16]);

// net48 Guid.CompareTo contract: -1 / 0 / 1. Field-wise comparison in
// layout order -- Data1 (unsigned LE int32), Data2 (unsigned LE int16),
// Data3 (unsigned LE int16), then Data4 bytes (unsigned); the first differing
// field decides (see header comment for the unsigned-compare consequence).
int32_t il2cpp_guid_compare(const uint8_t a[16], const uint8_t b[16]);

} // namespace il2cpp
