#pragma once
//------------------------------------------------------------------------------
// il2cpp_encoding.h -- System.Text.Encoding runtime support (icalls).
//
// The transpiler maps Encoding.GetBytes / GetChars / GetString calls to these
// helpers. The three raw convert functions transcode a caller-owned buffer
// pair and follow the .NET Encoding pattern of a size-query mode:
//
//   * dst == nullptr    -> returns the EXACT size required (bytes for the
//     UTF-8/ASCII paths, UTF-16 units for the unicode path) without writing;
//   * dst != nullptr    -> converts into the buffer, writing at most
//     dstCapacity units, and returns the number of units written;
//   * dstCapacity < required -> ArgumentException (the caller must query
//     first, exactly like .NET's GetByteCount/GetBytes split).
//
// Code pages (subset of the .NET code-page numbers):
//   * utf8    (65001): UTF-16 <-> UTF-8. Windows converts via
//                      WideCharToMultiByte / MultiByteToWideChar (CP_UTF8);
//                      other targets (Linux/Android/macOS/OpenBSD) use the
//                      hand-written codec in encoding.cpp -- identical
//                      semantics, no iconv dependency (Android's Bionic
//                      libc ships no iconv). Malformed input uses .NET's
//                      default replacement-fallback semantics: an invalid
//                      UTF-8 lead byte decodes to U+FFFD (consuming its
//                      byte alone); a broken, truncated or completed-but-
//                      invalid (overlong / surrogate / above-U+10FFFF)
//                      sequence decodes to U+FFFD consuming the lead plus at
//                      most ONE continuation byte, reprocessing the
//                      remaining bytes fresh (net48 probe-verified:
//                      ED A0 80 and E0 80 80 -> two U+FFFD, F0 80 80 80 ->
//                      three); an unpaired UTF-16 surrogate encodes to
//                      U+FFFD (EF BF BD) -- the WideCharToMultiByte
//                      replacement, which equals .NET's default replacement
//                      fallback. Both the Win32 API (Windows 10 1809+) and
//                      the hand-written codec follow these net48 semantics,
//                      the declared baseline.
//   * unicode (1200):  UTF-16LE passthrough (units copied verbatim; on the
//                      byte side the units are written little-endian).
//                      CONTRACT: the host must be little-endian -- the byte
//                      copy assumes it. Every supported target (x86/x64/
//                      ARM32/ARM64) is little-endian; encoding.cpp enforces
//                      the contract at compile time (a __BYTE_ORDER__ guard,
//                      the portable C++17 form of a HostIsLittleEndian
//                      check) so a future big-endian port fails the build
//                      instead of producing byte-swapped output (G12).
//                      Decode (GetString/GetChars) follows the net48 default
//                      replacement fallback: a valid high+low surrogate pair
//                      passes through as two units; an unpaired high/low
//                      surrogate and a dangling odd trailing byte each decode
//                      to ONE U+FFFD -- an odd byte count does NOT throw
//                      (the throw-on-invalid UnicodeEncoding ctor variant
//                      is out of scope).
//   * ascii   (20127): 7-bit only; non-ASCII input units become '?' on
//                      encode, bytes above 0x7F become U+FFFD on decode
//                      (both are the .NET ASCIIEncoding default fallbacks).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) with default replacement
// fallbacks; exceptions transported through il2cpp_throw_new.
//
// ALLOCATING icalls (the Encoding.UTF8.GetBytes(string) -> byte[] and
// Encoding.UTF8.GetString(byte[]) -> string surface the transpiler wires):
// one icall per code page, baked into the NAME -- the emitter cannot inject
// a page constant (EmitIcallCallArgs prepends only ParamCount and 0), so
// the resolver picks the page-specific entry by the receiver's tracked type
// (System.Text.UTF8Encoding / UnicodeEncoding / ASCIIEncoding). The
// raw-buffer convert/get_chars entries below remain for C++ callers.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_string.h"
#include "il2cpp_metadata.h"   // Il2CppClass (the encoding object descriptors)

#include <cstdint>

//------------------------------------------------------------------------------
// P2(5) A1 (2026-09-19): System.Text.Encoding OBJECT MODEL.
//
// net48 Encoding is a real object (identity, GetPreamble). The earlier
// null-fold made `Encoding.UTF8` an opaque tag; the StreamWriter/StreamReader
// encoding overloads and `new UTF8Encoding(bool)` need a real instance
// carrying the code page + the encoder preamble flag. The descriptor set
// below mirrors the stream classes: the transpiler emits
// `il2cpp_object_new(&System_Text_UTF8Encoding_class)` for `new
// UTF8Encoding(...)`; the singleton getters return cached instances.
//
//   struct Il2CppEncodingObject { Il2CppObject obj; int32 page; int32 emitBom; }
//
// page: the .NET code-page number (65001 utf8 / 1200 unicode / 20127 ascii).
// emitBom: the encoder preamble flag (UTF8Encoding(true) -> BOM).
//------------------------------------------------------------------------------
extern ::il2cpp::Il2CppClass System_Text_Encoding_class;
extern ::il2cpp::Il2CppClass System_Text_UTF8Encoding_class;
extern ::il2cpp::Il2CppClass System_Text_UnicodeEncoding_class;
extern ::il2cpp::Il2CppClass System_Text_ASCIIEncoding_class;

namespace il2cpp {

// Managed System.Text.Encoding layout: object header + code page + preamble
// flag. NO GC reference fields (refFieldBitmap 0), so compaction copies stay
// valid without fixups.
struct Il2CppEncodingObject
{
    Il2CppObject obj;    // object header (klass pointer, offset 0)
    int32_t     page;    // .NET code-page number
    int32_t     emitBom; // 1 = write the encoder preamble before the first byte
};

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// Supported code pages; the values are the .NET code-page numbers.
enum class il2cpp_encoding_code_page : int32_t
{
    utf8 = 65001,
    unicode = 1200,
    unicodebe = 1201, // UTF-16 big-endian (Encoding.BigEndianUnicode)
    ascii = 20127,
};

// UTF-16 -> UTF-8. 'src' holds 'srcLen' UTF-16 units; writes at most
// 'dstCapacity' bytes and returns the bytes written (or the exact byte count
// required when 'dst' is nullptr). Invalid trailing sequences and unpaired
// surrogates follow the replacement semantics documented above.
int32_t il2cpp_encoding_utf8_convert(const char16_t* src, int32_t srcLen,
                                     char* dst, int32_t dstCapacity);

// UTF-8 -> UTF-16. 'src' holds 'srcByteLen' bytes; writes at most
// 'dstCapacity' UTF-16 units and returns the units written (or the exact
// unit count required when 'dst' is nullptr). Invalid bytes decode to
// U+FFFD (the .NET default UTF-8 replacement fallback).
int32_t il2cpp_encoding_unicode_convert(const char* src, int32_t srcByteLen,
                                        char16_t* dst, int32_t dstCapacity);

// UTF-16 -> ASCII. 'src' holds 'srcLen' UTF-16 units; writes at most
// 'dstCapacity' bytes and returns the bytes written (or the exact byte count
// required when 'dst' is nullptr). Units above 0x7F encode as '?' (the .NET
// ASCIIEncoding default encoder fallback).
int32_t il2cpp_encoding_ascii_convert(const char16_t* src, int32_t srcLen,
                                      char* dst, int32_t dstCapacity);

// Encoding.GetBytes: encode a managed string ('text' is never null; the
// managed layer guards the null reference) into 'dst'. Same query mode as
// the convert helpers; unknown code pages throw ArgumentException.
int32_t il2cpp_encoding_get_bytes(Il2CppString* text,
                                  il2cpp_encoding_code_page page,
                                  uint8_t* dst, int32_t dstCapacity);

// Encoding.GetString: decode 'byteLen' bytes into a freshly allocated
// managed string (the GetChars result wrapped as a string, which is the
// form the transpiler consumes). Unknown code pages throw ArgumentException.
Il2CppString* il2cpp_encoding_get_chars(const uint8_t* src, int32_t byteLen,
                                        il2cpp_encoding_code_page page);

//------------------------------------------------------------------------------
// Allocating GetBytes: .NET Encoding.GetBytes(string) semantics -- encode a
// managed string ('text' is never null; the managed layer guards the null
// reference) into a FRESH managed byte[] of the exact required length. The
// code page is baked into the function name (one entry per page; the
// transpiler dispatches by the receiver's tracked encoding type, since the
// emitter cannot inject a page constant).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_encoding_utf8_get_bytes(Il2CppString* text);
Il2CppArray* il2cpp_encoding_unicode_get_bytes(Il2CppString* text);
Il2CppArray* il2cpp_encoding_unicodebe_get_bytes(Il2CppString* text);
Il2CppArray* il2cpp_encoding_ascii_get_bytes(Il2CppString* text);

//------------------------------------------------------------------------------
// Allocating GetString: .NET Encoding.GetString(byte[]) semantics -- decode a
// managed byte[] ('bytes' is never null; null throws ArgumentNullException)
// into a freshly allocated managed string. Same per-page split as the
// GetBytes family.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_encoding_utf8_get_string(Il2CppArray* bytes);
Il2CppString* il2cpp_encoding_unicode_get_string(Il2CppArray* bytes);
Il2CppString* il2cpp_encoding_unicodebe_get_string(Il2CppArray* bytes);
Il2CppString* il2cpp_encoding_ascii_get_string(Il2CppArray* bytes);

//------------------------------------------------------------------------------
// Allocating GetChars(byte[]): .NET Encoding.GetChars(byte[]) semantics --
// decode a managed byte[] ('bytes' is never null; null throws
// ArgumentNullException) into a FRESH managed char[] of the exact required
// length. The utf8 page only ships this whole-array form (the M5 batch-20
// icall); the ascii/unicode pages use the ranged form below.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_encoding_utf8_get_chars(Il2CppArray* bytes);

//------------------------------------------------------------------------------
// Allocating GetChars(byte[], int, int): .NET Encoding.GetChars(byte[],
// index, count) semantics -- decode 'count' bytes starting at 'index' of a
// managed byte[] into a FRESH managed char[] of the exact required length.
// Argument validation follows net48: null 'bytes' -> ArgumentNullException;
// a negative 'index'/'count' or 'index'+'count' past the end of the array ->
// ArgumentOutOfRangeException. Invalid byte sequences follow each page's
// net48 default replacement fallback (the same semantics as the raw-buffer
// convert): UTF-8 -> one U+FFFD per maximal invalid subpart, ASCII -> U+FFFD
// for bytes above 0x7F, Unicode (UTF-16LE) -> U+FFFD for an unpaired
// surrogate or a dangling odd trailing byte, while a valid high+low
// surrogate pair passes through as two units.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_encoding_utf8_get_chars(Il2CppArray* bytes, int32_t index, int32_t count);
Il2CppArray* il2cpp_encoding_unicode_get_chars(Il2CppArray* bytes, int32_t index, int32_t count);
Il2CppArray* il2cpp_encoding_unicodebe_get_chars(Il2CppArray* bytes, int32_t index, int32_t count);
Il2CppArray* il2cpp_encoding_ascii_get_chars(Il2CppArray* bytes, int32_t index, int32_t count);

//------------------------------------------------------------------------------
// P2(5) A1: the OBJECT model surface.
//
// il2cpp_encoding_register_classes -- register the four descriptors
//   (idempotent; call before any newobj/isinst targeting an encoding type).
// icall_encoding_get_UTF8/Unicode/ASCII -- the static singleton getters: a
//   cached instance (page 65001/1200/20127; UTF8 emitBom=1, the net48
//   Encoding.UTF8 = UTF8Encoding(true)).
// icall_encoding_ctor_utf8/unicode/ascii -- the `new XEncoding(bool[,bool])`
//   instance ctors: set page + emitBom on the fresh object.
// icall_encoding_get_preamble -- GetPreamble() -> byte[] (the BOM bytes for
//   the receiver's page/emitBom; empty array when emitBom=0).
//
// The object-based get_bytes/get_string/get_chars dispatch on the RECEIVER
// object's page (self != null). The legacy per-page entries (page baked into
// the NAME) stay for the null-folded callers -- the compatibility shim.
//------------------------------------------------------------------------------
void il2cpp_encoding_register_classes();

Il2CppObject* icall_encoding_get_UTF8();
Il2CppObject* icall_encoding_get_Unicode();
Il2CppObject* icall_encoding_get_UnicodeBE();
Il2CppObject* icall_encoding_get_ASCII();

void icall_encoding_ctor_utf8(Il2CppObject* self, int32_t emitBom);
void icall_encoding_ctor_utf8_2(Il2CppObject* self, int32_t emitBom, int32_t throwOnInvalid);
void icall_encoding_ctor_unicode(Il2CppObject* self, int32_t bigEndian, int32_t byteOrderMark);
void icall_encoding_ctor_ascii(Il2CppObject* self);

Il2CppArray* icall_encoding_get_preamble(Il2CppObject* self);

// Object-receiver conversions (self != null -> page/emitBom from the object).
Il2CppArray* icall_encoding_get_bytes(Il2CppObject* self, Il2CppString* text);
Il2CppString* icall_encoding_get_string(Il2CppObject* self, Il2CppArray* bytes);
Il2CppArray* icall_encoding_get_chars(Il2CppObject* self, Il2CppArray* bytes, int32_t index, int32_t count);
Il2CppArray* icall_encoding_get_chars_all(Il2CppObject* self, Il2CppArray* bytes);

//------------------------------------------------------------------------------
// ENC-REST (nb9): the remaining net48 Encoding surface -- byte/char COUNTS and
// the range/char[] overloads. The receiver object selects the code page
// (PageOf); each entry follows the page's net48 default replacement fallback
// (the same kernels as the existing GetBytes/GetString/GetChars entries).
//------------------------------------------------------------------------------

// GetByteCount(string) / GetByteCount(char[]) / GetByteCount(char[],int,int).
int32_t icall_encoding_get_byte_count_string(Il2CppObject* self, Il2CppString* text);
int32_t icall_encoding_get_byte_count_chars(Il2CppObject* self, Il2CppArray* chars);
int32_t icall_encoding_get_byte_count_chars_range(Il2CppObject* self, Il2CppArray* chars,
                                                  int32_t index, int32_t count);

// GetCharCount(byte[]) / GetCharCount(byte[],int,int).
int32_t icall_encoding_get_char_count_bytes(Il2CppObject* self, Il2CppArray* bytes);
int32_t icall_encoding_get_char_count_bytes_range(Il2CppObject* self, Il2CppArray* bytes,
                                                  int32_t index, int32_t count);

// GetBytes(char[]) / GetBytes(char[],int,int) -> byte[].
Il2CppArray* icall_encoding_get_bytes_chars(Il2CppObject* self, Il2CppArray* chars);
Il2CppArray* icall_encoding_get_bytes_chars_range(Il2CppObject* self, Il2CppArray* chars,
                                                  int32_t index, int32_t count);

// GetString(byte[],int,int) -> string.
Il2CppString* icall_encoding_get_string_range(Il2CppObject* self, Il2CppArray* bytes,
                                              int32_t index, int32_t count);

// GetChars(byte[],int,int,char[],int) -> the number of chars written into the
// caller-supplied destination array.
int32_t icall_encoding_get_chars_into(Il2CppObject* self, Il2CppArray* bytes,
                                      int32_t byteIndex, int32_t byteCount,
                                      Il2CppArray* chars, int32_t charIndex);

// nb9 (M8): the DESTINATION-BUFFER GetBytes overloads --
//   GetBytes(string, int charIndex, int charCount, byte[] bytes, int byteIndex)
//   GetBytes(char[], int charIndex, int charCount, byte[] bytes, int byteIndex)
// Both encode the requested char window and copy it into the caller's byte
// array. net48 bounds: charIndex/charCount -> AOORE("chars"), byteIndex ->
// AOORE("bytes"); a destination too small -> ArgumentException.
int32_t icall_encoding_get_bytes_into_string(Il2CppObject* self, Il2CppString* text,
                                             int32_t charIndex, int32_t charCount,
                                             Il2CppArray* bytes, int32_t byteIndex);
int32_t icall_encoding_get_bytes_into_chars(Il2CppObject* self, Il2CppArray* chars,
                                            int32_t charIndex, int32_t charCount,
                                            Il2CppArray* bytes, int32_t byteIndex);

}} // namespace il2cpp::runtime
