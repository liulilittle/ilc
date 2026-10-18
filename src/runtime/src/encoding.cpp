//------------------------------------------------------------------------------
// encoding.cpp -- System.Text.Encoding runtime support (see il2cpp_encoding.h).
//
// The conversion core is platform-split: Windows delegates the UTF-8 code
// page to the Win32 conversion APIs (WideCharToMultiByte / MultiByteToWideChar
// with CP_UTF8); every other target (Linux/Android/macOS/OpenBSD) uses the
// hand-written UTF-8 <-> UTF-16 codec below -- no iconv dependency (Android's
// Bionic libc ships no iconv). The only platform-dependent code is the
// code-page conversion provider; the unicode page is a UTF-16LE passthrough
// and the ascii page a 7-bit fold, both platform-independent. All contracts
// (query mode, capacity check, exception mapping) are documented in the
// header.
//
// BYTE-ORDER CONTRACT (G12): the unicode page is a UTF-16LE passthrough --
// the char16_t unit stream is copied to/from the byte buffer verbatim, so
// the on-wire bytes are the little-endian representation. Every supported
// target (x86/x64/ARM32/ARM64) is little-endian; the compile-time guard
// below turns a future big-endian port into a compile error instead of
// silently byte-swapped output. The Windows wide APIs share the assumption
// (wchar_t is 16-bit LE there), so no runtime branch exists -- it is a
// documented contract, not a runtime check.
//------------------------------------------------------------------------------

#include "il2cpp_encoding.h"
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"   // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_gc.h"        // il2cpp_gc_add_static_root (singleton liveness)

#include <cstdint>
#include <cstring>
#include <vector>

#if defined(_WIN32)
#include <windows.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

// Compile-time host byte-order guard (G12): UTF-16LE passthrough requires a
// little-endian host. GCC/Clang expose __BYTE_ORDER__ (no std::endian in
// C++17 -- <bit> is C++20), so a big-endian GCC/Clang host fails the build
// below; MSVC does not define the macro but every supported MSVC target
// (x86/x64/ARM64) is little-endian, so the guard is simply a no-op there.
// This is the portable form -- a constexpr union probe that reads the
// inactive union member is rejected by clang's constant-expression rules.
#if defined(__BYTE_ORDER__) && (__BYTE_ORDER__ == __ORDER_BIG_ENDIAN__)
#error "the unicode code page is a UTF-16LE passthrough and requires a little-endian host (G12 contract)"
#endif

// Guard: a negative length is a managed-caller bug; a null source is only
// legal together with a zero length (the empty-input convention).
void CheckSource(const void* src, int32_t len)
{
    if (len < 0)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Non-negative number required.");
    }
    if (src == nullptr && len != 0)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
}

// Guard: a non-null destination must be able to hold 'required' units.
void CheckCapacity(const void* dst, int32_t dstCapacity, int32_t required)
{
    if (dst != nullptr && dstCapacity < required)
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
                         "Insufficient destination buffer capacity.");
    }
}

#if defined(_WIN32)
// Windows conversion APIs take wchar_t; on every supported target (MSVC,
// MinGW-w64 default -fshort-wchar) wchar_t is 16-bit, so the UTF-16 unit
// buffers are interchangeable.
const wchar_t* ToWide(const char16_t* src) { return reinterpret_cast<const wchar_t*>(src); }
wchar_t* ToWide(char16_t* src) { return reinterpret_cast<wchar_t*>(src); }
#else
//------------------------------------------------------------------------------
// Hand-written UTF-8 <-> UTF-16 codec (POSIX targets: __linux__, __ANDROID__,
// __APPLE__, __OpenBSD__). Pure standard C++ -- no platform calls -- so it is
// the shared algorithm both conversion directions route through.
//
// Encode semantics match the Windows CP_UTF8 path exactly:
//   * an unpaired UTF-16 surrogate encodes as U+FFFD (EF BF BD) -- the
//     WideCharToMultiByte replacement, which equals .NET's default
//     replacement fallback;
//   * a valid high+low surrogate pair encodes as one 4-byte sequence.
//
// Decode semantics match .NET's UTF8Encoding default fallback: every
// invalid byte, truncated tail, overlong sequence or surrogate encoding
// collapses into ONE U+FFFD (maximal-subpart replacement), then decoding
// resumes after the consumed run. (Windows note: MultiByteToWideChar
// replaces the surrogate-encoding / overlong / above-U+10FFFF classes
// per byte instead of per maximal subpart; the codec follows the .NET
// semantics -- the declared baseline.)
//
// Both functions implement the query mode: with dst == nullptr they only
// count the units (bytes / char16_t units) the input would produce, which is
// exactly what the size-query path of the public API returns.
//------------------------------------------------------------------------------

// UTF-16 -> UTF-8. Returns the byte count; writes only when dst != nullptr.
int32_t Utf8Encode(const char16_t* src, int32_t srcLen, char* dst)
{
    int32_t written = 0;
    for (int32_t i = 0; i < srcLen; ++i)
    {
        const uint32_t u = static_cast<uint32_t>(src[i]);
        if (u < 0x80)
        {
            if (dst != nullptr) { dst[written] = static_cast<char>(u); }
            written += 1;
        }
        else if (u < 0x800)
        {
            if (dst != nullptr)
            {
                dst[written] = static_cast<char>(0xC0 | (u >> 6));
                dst[written + 1] = static_cast<char>(0x80 | (u & 0x3F));
            }
            written += 2;
        }
        else if (u >= 0xD800 && u <= 0xDBFF)
        {
            // High surrogate: a following low surrogate forms one 4-byte
            // pair; without one the unit is unpaired and becomes U+FFFD
            // (the WideCharToMultiByte / .NET replacement).
            if (i + 1 < srcLen)
            {
                const uint32_t lo = static_cast<uint32_t>(src[i + 1]);
                if (lo >= 0xDC00 && lo <= 0xDFFF)
                {
                    const uint32_t cp = 0x10000 + ((u - 0xD800) << 10) + (lo - 0xDC00);
                    if (dst != nullptr)
                    {
                        dst[written] = static_cast<char>(0xF0 | (cp >> 18));
                        dst[written + 1] = static_cast<char>(0x80 | ((cp >> 12) & 0x3F));
                        dst[written + 2] = static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
                        dst[written + 3] = static_cast<char>(0x80 | (cp & 0x3F));
                    }
                    written += 4;
                    ++i;
                    continue;
                }
            }
            if (dst != nullptr)
            {
                dst[written] = static_cast<char>(0xEF);
                dst[written + 1] = static_cast<char>(0xBF);
                dst[written + 2] = static_cast<char>(0xBD);
            }
            written += 3;
        }
        else if (u >= 0xDC00 && u <= 0xDFFF)
        {
            // Unpaired low surrogate -> U+FFFD.
            if (dst != nullptr)
            {
                dst[written] = static_cast<char>(0xEF);
                dst[written + 1] = static_cast<char>(0xBF);
                dst[written + 2] = static_cast<char>(0xBD);
            }
            written += 3;
        }
        else
        {
            if (dst != nullptr)
            {
                dst[written] = static_cast<char>(0xE0 | (u >> 12));
                dst[written + 1] = static_cast<char>(0x80 | ((u >> 6) & 0x3F));
                dst[written + 2] = static_cast<char>(0x80 | (u & 0x3F));
            }
            written += 3;
        }
    }
    return written;
}

// UTF-8 -> UTF-16. Returns the unit count; writes only when dst != nullptr.
int32_t Utf8Decode(const char* src, int32_t byteLen, char16_t* dst)
{
    int32_t written = 0;
    int32_t i = 0;
    while (i < byteLen)
    {
        const uint8_t b = static_cast<uint8_t>(src[i]);
        if (b < 0x80)
        {
            if (dst != nullptr) { dst[written] = static_cast<char16_t>(b); }
            written += 1;
            i += 1;
            continue;
        }
        uint32_t cp = 0;
        int32_t seqLen = 0;
        if (b >= 0xC2 && b <= 0xDF)      { cp = b & 0x1F; seqLen = 2; }
        else if (b >= 0xE0 && b <= 0xEF) { cp = b & 0x0F; seqLen = 3; }
        else if (b >= 0xF0 && b <= 0xF4) { cp = b & 0x07; seqLen = 4; }
        else
        {
            // 0x80..0xC1 / 0xF5..0xFF can never lead a sequence.
            if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
            written += 1;
            i += 1;
            continue;
        }
        // Consume the maximal run of continuation bytes (0x80..0xBF).
        int32_t consumed = 1;
        while (consumed < seqLen && i + consumed < byteLen)
        {
            const uint8_t c = static_cast<uint8_t>(src[i + consumed]);
            if (c < 0x80 || c > 0xBF)
            {
                break;
            }
            cp = (cp << 6) | (c & 0x3F);
            consumed += 1;
        }
        bool ok = consumed == seqLen;
        if (ok)
        {
            switch (seqLen)
            {
            case 2: ok = cp >= 0x80; break;
            case 3: ok = cp >= 0x800 && !(cp >= 0xD800 && cp <= 0xDFFF); break;
            case 4: ok = cp >= 0x10000 && cp <= 0x10FFFF; break;
            }
        }
        if (!ok)
        {
            // Maximal invalid subpart -> ONE U+FFFD, then resume after it.
            // net48 replacement semantics (probe-verified): an invalid lead
            // (no continuation byte consumed -- consumed == 1) consumes the
            // lead byte alone; any invalid sequence that consumed at least
            // one continuation byte (broken mid-sequence, truncated, or
            // completed-but-invalid -- overlong / surrogate / above
            // U+10FFFF) consumes the lead plus ONE continuation byte and
            // reprocesses the remaining bytes fresh. Examples: ED A0 80 and
            // E0 80 80 each decode to TWO U+FFFD; F0 80 80 80 to THREE.
            if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
            written += 1;
            i += (consumed == 1) ? 1 : 2;
            continue;
        }
        if (dst != nullptr)
        {
            if (cp < 0x10000)
            {
                dst[written] = static_cast<char16_t>(cp);
            }
            else
            {
                const uint32_t v = cp - 0x10000;
                dst[written] = static_cast<char16_t>(0xD800 | (v >> 10));
                dst[written + 1] = static_cast<char16_t>(0xDC00 | (v & 0x3FF));
            }
        }
        written += (cp < 0x10000) ? 1 : 2;
        i += consumed;
    }
    return written;
}
#endif // !defined(_WIN32): hand-written codec

//------------------------------------------------------------------------------
// Code-page conversion providers -- the ONLY platform-dependent code in this
// file. Windows converts via the Win32 CP_UTF8 APIs; every other target via
// the hand-written codec above. Query mode: dst == nullptr returns the exact
// size (bytes / UTF-16 units); the caller's CheckCapacity runs first, so the
// write path always fits and dstCapacity is only meaningful to the Win32 API.
//------------------------------------------------------------------------------
int32_t ConvertUtf16ToUtf8(const char16_t* src, int32_t srcLen,
                           char* dst, int32_t dstCapacity)
{
#if defined(_WIN32)
    if (dst == nullptr)
    {
        return ::WideCharToMultiByte(CP_UTF8, 0, ToWide(src), srcLen,
                                     nullptr, 0, nullptr, nullptr);
    }
    return ::WideCharToMultiByte(CP_UTF8, 0, ToWide(src), srcLen,
                                 dst, dstCapacity, nullptr, nullptr);
#else
    (void)dstCapacity;
    return Utf8Encode(src, srcLen, dst);
#endif
}

int32_t ConvertUtf8ToUtf16(const char* src, int32_t srcByteLen,
                           char16_t* dst, int32_t dstCapacity)
{
#if defined(_WIN32)
    if (dst == nullptr)
    {
        return ::MultiByteToWideChar(CP_UTF8, 0, src, srcByteLen, nullptr, 0);
    }
    return ::MultiByteToWideChar(CP_UTF8, 0, src, srcByteLen, ToWide(dst), dstCapacity);
#else
    (void)dstCapacity;
    return Utf8Decode(src, srcByteLen, dst);
#endif
}

// UTF-16LE -> UTF-16. 'src' holds 'byteLen' bytes (little-endian units);
// writes at most the required units and returns the unit count (or only
// counts when 'dst' is nullptr). Follows .NET's UnicodeEncoding default
// replacement fallback: a valid high+low surrogate pair passes through as two
// units; an unpaired high or low surrogate and a dangling odd trailing byte
// each decode to ONE U+FFFD (the net48 DecoderReplacementFallback behavior).
int32_t Utf16LeDecode(const uint8_t* src, int32_t byteLen, char16_t* dst)
{
    int32_t written = 0;
    bool hasHigh = false;
    char16_t high = 0;
    int32_t i = 0;
    while (i + 1 < byteLen)
    {
        const char16_t u = static_cast<char16_t>(src[i]) |
                           static_cast<char16_t>(static_cast<char16_t>(src[i + 1]) << 8);
        i += 2;
        if (u >= 0xD800 && u <= 0xDBFF)
        {
            // High surrogate: a preceding unpaired high collapses to U+FFFD
            // (maximal invalid subpart), then the new high is remembered.
            if (hasHigh)
            {
                if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
                ++written;
            }
            hasHigh = true;
            high = u;
            continue;
        }
        if (u >= 0xDC00 && u <= 0xDFFF)
        {
            if (hasHigh)
            {
                // Valid high+low pair: both units pass through.
                if (dst != nullptr)
                {
                    dst[written] = high;
                    dst[written + 1] = u;
                }
                written += 2;
                hasHigh = false;
            }
            else
            {
                // Unpaired low surrogate -> U+FFFD.
                if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
                ++written;
            }
            continue;
        }
        // A normal unit: a pending unpaired high surrogate collapses first.
        if (hasHigh)
        {
            if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
            ++written;
            hasHigh = false;
        }
        if (dst != nullptr) { dst[written] = u; }
        ++written;
    }
    if (hasHigh)
    {
        if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
        ++written;
    }
    if (i < byteLen)
    {
        if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
        ++written;
    }
    return written;
}

// UTF-16BE -> UTF-16. 'src' holds 'byteLen' bytes (big-endian units -- the
// high byte first); writes at most the required units and returns the unit
// count (or only counts when 'dst' is nullptr). Same .NET UnicodeEncoding
// default replacement fallback as Utf16LeDecode: a valid high+low surrogate
// pair passes through as two units; an unpaired high or low surrogate and a
// dangling odd trailing byte each decode to ONE U+FFFD.
int32_t Utf16BeDecode(const uint8_t* src, int32_t byteLen, char16_t* dst)
{
    int32_t written = 0;
    bool hasHigh = false;
    char16_t high = 0;
    int32_t i = 0;
    while (i + 1 < byteLen)
    {
        const char16_t u = static_cast<char16_t>(static_cast<char16_t>(src[i]) << 8) |
                           static_cast<char16_t>(src[i + 1]);
        i += 2;
        if (u >= 0xD800 && u <= 0xDBFF)
        {
            if (hasHigh)
            {
                if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
                ++written;
            }
            hasHigh = true;
            high = u;
            continue;
        }
        if (u >= 0xDC00 && u <= 0xDFFF)
        {
            if (hasHigh)
            {
                if (dst != nullptr)
                {
                    dst[written] = high;
                    dst[written + 1] = u;
                }
                written += 2;
                hasHigh = false;
            }
            else
            {
                if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
                ++written;
            }
            continue;
        }
        if (hasHigh)
        {
            if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
            ++written;
            hasHigh = false;
        }
        if (dst != nullptr) { dst[written] = u; }
        ++written;
    }
    if (hasHigh)
    {
        if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
        ++written;
    }
    if (i < byteLen)
    {
        if (dst != nullptr) { dst[written] = static_cast<char16_t>(0xFFFD); }
        ++written;
    }
    return written;
}

} // namespace

//------------------------------------------------------------------------------
// UTF-16 -> UTF-8.
//------------------------------------------------------------------------------
int32_t il2cpp_encoding_utf8_convert(const char16_t* src, int32_t srcLen,
                                     char* dst, int32_t dstCapacity)
{
    CheckSource(src, srcLen);
    if (srcLen == 0)
    {
        return 0;
    }
    const int32_t required = ConvertUtf16ToUtf8(src, srcLen, nullptr, 0);
    CheckCapacity(dst, dstCapacity, required);
    if (dst == nullptr)
    {
        return required;
    }
    return ConvertUtf16ToUtf8(src, srcLen, dst, dstCapacity);
}

//------------------------------------------------------------------------------
// UTF-8 -> UTF-16. Invalid bytes decode to U+FFFD -- the .NET default UTF-8
// replacement fallback.
//------------------------------------------------------------------------------
int32_t il2cpp_encoding_unicode_convert(const char* src, int32_t srcByteLen,
                                        char16_t* dst, int32_t dstCapacity)
{
    CheckSource(src, srcByteLen);
    if (srcByteLen == 0)
    {
        return 0;
    }
    const int32_t required = ConvertUtf8ToUtf16(src, srcByteLen, nullptr, 0);
    CheckCapacity(dst, dstCapacity, required);
    if (dst == nullptr)
    {
        return required;
    }
    return ConvertUtf8ToUtf16(src, srcByteLen, dst, dstCapacity);
}

//------------------------------------------------------------------------------
// UTF-16 -> ASCII: 7-bit passthrough, non-ASCII units become '?'.
//------------------------------------------------------------------------------
int32_t il2cpp_encoding_ascii_convert(const char16_t* src, int32_t srcLen,
                                      char* dst, int32_t dstCapacity)
{
    CheckSource(src, srcLen);
    CheckCapacity(dst, dstCapacity, srcLen);
    if (dst == nullptr)
    {
        return srcLen;
    }
    for (int32_t i = 0; i < srcLen; ++i)
    {
        dst[i] = static_cast<char>((src[i] <= 0x7F) ? src[i] : '?');
    }
    return srcLen;
}

//------------------------------------------------------------------------------
// Encoding.GetBytes: dispatch on the code page.
//------------------------------------------------------------------------------
int32_t il2cpp_encoding_get_bytes(Il2CppString* text,
                                  il2cpp_encoding_code_page page,
                                  uint8_t* dst, int32_t dstCapacity)
{
    if (text == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    switch (page)
    {
    case il2cpp_encoding_code_page::utf8:
        return il2cpp_encoding_utf8_convert(text->chars, text->length,
                                            reinterpret_cast<char*>(dst),
                                            dstCapacity);
    case il2cpp_encoding_code_page::ascii:
        return il2cpp_encoding_ascii_convert(text->chars, text->length,
                                             reinterpret_cast<char*>(dst),
                                             dstCapacity);
    case il2cpp_encoding_code_page::unicode:
    {
        const int32_t required = text->length * 2;
        CheckCapacity(dst, dstCapacity, required);
        if (dst == nullptr)
        {
            return required;
        }
        // UTF-16LE passthrough (G12 contract): the units' byte representation
        // IS the little-endian encoding; the file-scope byte-order guard
        // (#error on a big-endian GCC/Clang host) enforces the contract.
        std::memcpy(dst, text->chars, static_cast<size_t>(required));
        return required;
    }
    case il2cpp_encoding_code_page::unicodebe:
    {
        const int32_t required = text->length * 2;
        CheckCapacity(dst, dstCapacity, required);
        if (dst == nullptr)
        {
            return required;
        }
        // UTF-16BE: each unit's high byte first (byte-swap the LE layout).
        for (int32_t i = 0; i < text->length; ++i)
        {
            const char16_t c = text->chars[i];
            dst[i * 2] = static_cast<uint8_t>(c >> 8);
            dst[i * 2 + 1] = static_cast<uint8_t>(c & 0xFF);
        }
        return required;
    }
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

//------------------------------------------------------------------------------
// Encoding.GetString: dispatch on the code page and allocate the result.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_encoding_get_chars(const uint8_t* src, int32_t byteLen,
                                        il2cpp_encoding_code_page page)
{
    CheckSource(src, byteLen);
    if (byteLen == 0)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    switch (page)
    {
    case il2cpp_encoding_code_page::utf8:
    {
        const int32_t units = ConvertUtf8ToUtf16(reinterpret_cast<const char*>(src),
                                                 byteLen, nullptr, 0);
        std::vector<char16_t> tmp(static_cast<size_t>(units));
        ConvertUtf8ToUtf16(reinterpret_cast<const char*>(src), byteLen,
                           tmp.data(), units);
        return il2cpp_string_new(tmp.data(), units);
    }
    case il2cpp_encoding_code_page::unicode:
    {
        // UTF-16LE decode with the net48 default replacement fallback (the
        // runtime models the default non-throwing UnicodeEncoding): an
        // unpaired high/low surrogate and a dangling odd trailing byte each
        // decode to ONE U+FFFD, a valid high+low pair passes through as two
        // units. This is the same decoder as the ranged GetChars path
        // (Utf16LeDecode), so the whole-string and ranged forms agree --
        // net48 probe-verified: Encoding.Unicode.GetString/GetChars does NOT
        // throw on an odd byte count with the default encoding; the trailing
        // byte becomes a replacement char. (The throw-on-invalid Unicode
        // ctor variant is out of scope.) The little-endian read is explicit,
        // so decoding is byte-order independent; the encode side stays under
        // the file-scope byte-order guard (G12).
        const int32_t units = Utf16LeDecode(src, byteLen, nullptr);
        std::vector<char16_t> tmp(static_cast<size_t>(units));
        if (units > 0)
        {
            Utf16LeDecode(src, byteLen, tmp.data());
        }
        return il2cpp_string_new(tmp.data(), units);
    }
    case il2cpp_encoding_code_page::unicodebe:
    {
        // UTF-16BE decode with the same net48 default replacement fallback as
        // the LE page (Utf16BeDecode -- big-endian unit reads, unpaired
        // surrogate / dangling odd trailing byte -> U+FFFD).
        const int32_t units = Utf16BeDecode(src, byteLen, nullptr);
        std::vector<char16_t> tmp(static_cast<size_t>(units));
        if (units > 0)
        {
            Utf16BeDecode(src, byteLen, tmp.data());
        }
        return il2cpp_string_new(tmp.data(), units);
    }
    case il2cpp_encoding_code_page::ascii:
    {
        std::vector<char16_t> tmp(static_cast<size_t>(byteLen));
        for (int32_t i = 0; i < byteLen; ++i)
        {
            // net48 ASCIIEncoding default decoder fallback: bytes above 0x7F
            // decode to '?' (EncoderReplacementFallback("?")), NOT U+FFFD.
            tmp[static_cast<size_t>(i)] = (src[i] <= 0x7F)
                ? static_cast<char16_t>(src[i]) : static_cast<char16_t>(0x3F);
        }
        return il2cpp_string_new(tmp.data(), byteLen);
    }
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

//------------------------------------------------------------------------------
// Allocating GetBytes: encode into a fresh managed byte[] of the exact
// required length (the query-mode convert sizes it; the write path then
// fills it). The code page is baked into the caller-selected entry name.
//------------------------------------------------------------------------------
namespace {

Il2CppArray* AllocBytes(int32_t len)
{
    return il2cpp_array_new(nullptr, 1, false, len);
}

Il2CppArray* EncodeBytes(Il2CppString* text, int32_t (*convert)(
    const char16_t* src, int32_t srcLen, char* dst, int32_t dstCapacity))
{
    if (text == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    const int32_t required = convert(text->chars, text->length, nullptr, 0);
    Il2CppArray* arr = AllocBytes(required);
    if (required > 0)
    {
        convert(text->chars, text->length,
                reinterpret_cast<char*>(il2cpp_array_get_elem_ptr(arr, 0)),
                required);
    }
    return arr;
}

} // namespace

Il2CppArray* il2cpp_encoding_utf8_get_bytes(Il2CppString* text)
{
    return EncodeBytes(text, &il2cpp_encoding_utf8_convert);
}

Il2CppArray* il2cpp_encoding_ascii_get_bytes(Il2CppString* text)
{
    return EncodeBytes(text, &il2cpp_encoding_ascii_convert);
}

Il2CppArray* il2cpp_encoding_unicode_get_bytes(Il2CppString* text)
{
    // UTF-16LE: the unit count is the string length; the byte count its
    // double (little-endian on every supported target -- G12 contract,
    // enforced by the file-scope byte-order guard: #error on a big-endian
    // GCC/Clang host).
    if (text == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    const int32_t required = text->length * 2;
    Il2CppArray* arr = AllocBytes(required);
    if (required > 0)
    {
        std::memcpy(il2cpp_array_get_elem_ptr(arr, 0), text->chars,
                    static_cast<size_t>(required));
    }
    return arr;
}

Il2CppArray* il2cpp_encoding_unicodebe_get_bytes(Il2CppString* text)
{
    // UTF-16BE: each unit's high byte first (byte-swap the LE layout).
    if (text == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    const int32_t required = text->length * 2;
    Il2CppArray* arr = AllocBytes(required);
    if (required > 0)
    {
        uint8_t* dst = reinterpret_cast<uint8_t*>(il2cpp_array_get_elem_ptr(arr, 0));
        for (int32_t i = 0; i < text->length; ++i)
        {
            const char16_t c = text->chars[i];
            dst[i * 2] = static_cast<uint8_t>(c >> 8);
            dst[i * 2 + 1] = static_cast<uint8_t>(c & 0xFF);
        }
    }
    return arr;
}

//------------------------------------------------------------------------------
// Allocating GetString: decode a managed byte[] into a fresh string (the
// raw-buffer get_chars does the per-page work; the wrapper adapts the
// managed array to its pointer form). An empty array must not reach
// il2cpp_array_get_elem_ptr -- the bounds check would throw for index 0.
//------------------------------------------------------------------------------
namespace {

Il2CppString* DecodeBytes(Il2CppArray* bytes, il2cpp_encoding_code_page page)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    const int32_t n = il2cpp_array_get_length(bytes);
    const uint8_t* src = (n == 0)
        ? nullptr
        : reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(bytes, 0));
    return il2cpp_encoding_get_chars(src, n, page);
}

} // namespace

Il2CppString* il2cpp_encoding_utf8_get_string(Il2CppArray* bytes)
{
    return DecodeBytes(bytes, il2cpp_encoding_code_page::utf8);
}

Il2CppString* il2cpp_encoding_unicode_get_string(Il2CppArray* bytes)
{
    return DecodeBytes(bytes, il2cpp_encoding_code_page::unicode);
}

Il2CppString* il2cpp_encoding_unicodebe_get_string(Il2CppArray* bytes)
{
    return DecodeBytes(bytes, il2cpp_encoding_code_page::unicodebe);
}

Il2CppString* il2cpp_encoding_ascii_get_string(Il2CppArray* bytes)
{
    return DecodeBytes(bytes, il2cpp_encoding_code_page::ascii);
}

//------------------------------------------------------------------------------
// Allocating GetChars: decode a byte[] RANGE into a FRESH managed char[].
// net48 Encoding.GetChars(byte[], int, int) semantics: null 'bytes' ->
// ArgumentNullException; negative index/count or index+count past the end ->
// ArgumentOutOfRangeException; then the page's decode with its default
// replacement fallback (invalid sequences collapse to U+FFFD -- one per
// maximal invalid subpart, so the result length is the DECODED unit count,
// not the byte count). An empty range must not reach
// il2cpp_array_get_elem_ptr -- the bounds check would throw for index 0.
//------------------------------------------------------------------------------
namespace {

// Validate the .NET GetChars(byte[], int, int) argument contract and return
// the element pointer at 'index' (or nullptr for an empty range).
const uint8_t* CheckBytesRange(Il2CppArray* bytes, int32_t index, int32_t count,
                               int32_t* outCount)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    const int32_t n = il2cpp_array_get_length(bytes);
    if (index < 0 || count < 0)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Non-negative number required.");
    }
    if (n - index < count)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }
    *outCount = count;
    if (count == 0)
    {
        return nullptr;
    }
    return reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(bytes, index));
}

Il2CppArray* GetCharsRange(Il2CppArray* bytes, int32_t index, int32_t count,
                           il2cpp_encoding_code_page page)
{
    int32_t n = 0;
    const uint8_t* src = CheckBytesRange(bytes, index, count, &n);
    if (n == 0)
    {
        return il2cpp_array_new(nullptr, 2, false, 0);
    }
    switch (page)
    {
    case il2cpp_encoding_code_page::utf8:
    {
        const int32_t units = ConvertUtf8ToUtf16(reinterpret_cast<const char*>(src), n,
                                                 nullptr, 0);
        Il2CppArray* arr = il2cpp_array_new(nullptr, 2, false, units);
        if (units > 0)
        {
            ConvertUtf8ToUtf16(reinterpret_cast<const char*>(src), n,
                               reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(arr, 0)),
                               units);
        }
        return arr;
    }
    case il2cpp_encoding_code_page::unicode:
    {
        const int32_t units = Utf16LeDecode(src, n, nullptr);
        Il2CppArray* arr = il2cpp_array_new(nullptr, 2, false, units);
        if (units > 0)
        {
            Utf16LeDecode(src, n,
                          reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(arr, 0)));
        }
        return arr;
    }
    case il2cpp_encoding_code_page::unicodebe:
    {
        const int32_t units = Utf16BeDecode(src, n, nullptr);
        Il2CppArray* arr = il2cpp_array_new(nullptr, 2, false, units);
        if (units > 0)
        {
            Utf16BeDecode(src, n,
                          reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(arr, 0)));
        }
        return arr;
    }
    case il2cpp_encoding_code_page::ascii:
    {
        // One output unit per input byte: 7-bit passthrough, bytes above
        // 0x7F -> '?' (the net48 ASCIIEncoding default decoder fallback).
        Il2CppArray* arr = il2cpp_array_new(nullptr, 2, false, n);
        char16_t* dst = reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(arr, 0));
        for (int32_t i = 0; i < n; ++i)
        {
            dst[i] = (src[i] <= 0x7F) ? static_cast<char16_t>(src[i])
                                      : static_cast<char16_t>(0x3F);
        }
        return arr;
    }
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

} // namespace

// Encoding.GetChars(byte[]) -> char[]: the whole array is the range
// [0, length) (the M5 batch-20 utf8 icall keeps this single-argument form).
Il2CppArray* il2cpp_encoding_utf8_get_chars(Il2CppArray* bytes)
{
    if (bytes == nullptr)
    {
        return il2cpp_encoding_utf8_get_chars(bytes, 0, 0);
    }
    return il2cpp_encoding_utf8_get_chars(bytes, 0, il2cpp_array_get_length(bytes));
}

Il2CppArray* il2cpp_encoding_utf8_get_chars(Il2CppArray* bytes, int32_t index, int32_t count)
{
    return GetCharsRange(bytes, index, count, il2cpp_encoding_code_page::utf8);
}

Il2CppArray* il2cpp_encoding_unicode_get_chars(Il2CppArray* bytes, int32_t index, int32_t count)
{
    return GetCharsRange(bytes, index, count, il2cpp_encoding_code_page::unicode);
}

Il2CppArray* il2cpp_encoding_unicodebe_get_chars(Il2CppArray* bytes, int32_t index, int32_t count)
{
    return GetCharsRange(bytes, index, count, il2cpp_encoding_code_page::unicodebe);
}

Il2CppArray* il2cpp_encoding_ascii_get_chars(Il2CppArray* bytes, int32_t index, int32_t count)
{
    return GetCharsRange(bytes, index, count, il2cpp_encoding_code_page::ascii);
}

//------------------------------------------------------------------------------
// P2(5) A1: the OBJECT model (see il2cpp_encoding.h).
//------------------------------------------------------------------------------
namespace {

// Cast an encoding object parameter to its payload view.
Il2CppEncodingObject* AsEncoding(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppEncodingObject*>(obj);
}

int32_t PageOf(Il2CppObject* self)
{
    return AsEncoding(self)->page;
}

int32_t EmitBomOf(Il2CppObject* self)
{
    return AsEncoding(self)->emitBom;
}

// The per-page allocating GetBytes used by the object-receiver entry: pick the
// page-specific helper by the receiver's code page.
Il2CppArray* GetBytesForPage(Il2CppObject* self, Il2CppString* text)
{
    switch (PageOf(self))
    {
    case 65001: return il2cpp_encoding_utf8_get_bytes(text);
    case 1200:  return il2cpp_encoding_unicode_get_bytes(text);
    case 1201:  return il2cpp_encoding_unicodebe_get_bytes(text);
    case 20127: return il2cpp_encoding_ascii_get_bytes(text);
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

Il2CppString* GetStringForPage(Il2CppObject* self, Il2CppArray* bytes)
{
    switch (PageOf(self))
    {
    case 65001: return il2cpp_encoding_utf8_get_string(bytes);
    case 1200:  return il2cpp_encoding_unicode_get_string(bytes);
    case 1201:  return il2cpp_encoding_unicodebe_get_string(bytes);
    case 20127: return il2cpp_encoding_ascii_get_string(bytes);
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

// The cached singleton instances (GC roots -- registered once on first use).
Il2CppObject* g_utf8Singleton = nullptr;
Il2CppObject* g_unicodeSingleton = nullptr;
Il2CppObject* g_unicodeBeSingleton = nullptr;
Il2CppObject* g_asciiSingleton = nullptr;
bool g_encodingRootsArmed = false;

void ArmEncodingRoots()
{
    if (g_encodingRootsArmed)
    {
        return;
    }
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_utf8Singleton));
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_unicodeSingleton));
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_unicodeBeSingleton));
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_asciiSingleton));
    g_encodingRootsArmed = true;
}

Il2CppObject* NewEncoding(Il2CppClass* klass, int32_t page, int32_t emitBom)
{
    il2cpp_encoding_register_classes();
    ArmEncodingRoots();
    Il2CppObject* obj = il2cpp_object_new(klass);
    AsEncoding(obj)->page = page;
    AsEncoding(obj)->emitBom = emitBom;
    return obj;
}

} // namespace

void icall_encoding_ctor_utf8(Il2CppObject* self, int32_t emitBom)
{
    il2cpp_encoding_register_classes();
    AsEncoding(self)->page = 65001;
    AsEncoding(self)->emitBom = emitBom ? 1 : 0;
}

void icall_encoding_ctor_utf8_2(Il2CppObject* self, int32_t emitBom, int32_t throwOnInvalid)
{
    (void)throwOnInvalid; // the throw-on-invalid decoder variant is out of scope
    icall_encoding_ctor_utf8(self, emitBom);
}

void icall_encoding_ctor_unicode(Il2CppObject* self, int32_t bigEndian, int32_t byteOrderMark)
{
    il2cpp_encoding_register_classes();
    AsEncoding(self)->page = bigEndian ? 1201 : 1200;
    AsEncoding(self)->emitBom = byteOrderMark ? 1 : 0;
}

void icall_encoding_ctor_ascii(Il2CppObject* self)
{
    il2cpp_encoding_register_classes();
    AsEncoding(self)->page = 20127;
    AsEncoding(self)->emitBom = 0;
}

Il2CppObject* icall_encoding_get_UTF8()
{
    if (g_utf8Singleton == nullptr)
    {
        g_utf8Singleton = NewEncoding(&System_Text_UTF8Encoding_class, 65001, 1);
    }
    return g_utf8Singleton;
}

Il2CppObject* icall_encoding_get_Unicode()
{
    if (g_unicodeSingleton == nullptr)
    {
        g_unicodeSingleton = NewEncoding(&System_Text_UnicodeEncoding_class, 1200, 1);
    }
    return g_unicodeSingleton;
}

// Encoding.BigEndianUnicode -- UTF-16BE (page 1201). The .NET TYPE is
// UnicodeEncoding for both LE and BE, so the descriptor is shared; only the
// page differs. Without this dedicated singleton the getter returned the LE
// instance and BigEndianUnicode.GetBytes produced little-endian bytes
// (encodingconvcase regression).
Il2CppObject* icall_encoding_get_UnicodeBE()
{
    if (g_unicodeBeSingleton == nullptr)
    {
        g_unicodeBeSingleton = NewEncoding(&System_Text_UnicodeEncoding_class, 1201, 1);
    }
    return g_unicodeBeSingleton;
}

Il2CppObject* icall_encoding_get_ASCII()
{
    if (g_asciiSingleton == nullptr)
    {
        g_asciiSingleton = NewEncoding(&System_Text_ASCIIEncoding_class, 20127, 0);
    }
    return g_asciiSingleton;
}

Il2CppArray* icall_encoding_get_preamble(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    const int32_t page = PageOf(self);
    const int32_t emitBom = EmitBomOf(self);
    if (emitBom == 0)
    {
        return il2cpp_array_new(nullptr, 1, false, 0);
    }
    uint8_t bom[3];
    int32_t n = 0;
    if (page == 65001)
    {
        bom[0] = 0xEF; bom[1] = 0xBB; bom[2] = 0xBF; n = 3;
    }
    else if (page == 1200)
    {
        bom[0] = 0xFF; bom[1] = 0xFE; n = 2;
    }
    else if (page == 1201)
    {
        bom[0] = 0xFE; bom[1] = 0xFF; n = 2;
    }
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, n);
    if (n > 0)
    {
        std::memcpy(arr->data, bom, static_cast<size_t>(n));
    }
    return arr;
}

Il2CppArray* icall_encoding_get_bytes(Il2CppObject* self, Il2CppString* text)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    return GetBytesForPage(self, text);
}

Il2CppString* icall_encoding_get_string(Il2CppObject* self, Il2CppArray* bytes)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    return GetStringForPage(self, bytes);
}

Il2CppArray* icall_encoding_get_chars(Il2CppObject* self, Il2CppArray* bytes,
                                      int32_t index, int32_t count)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    switch (PageOf(self))
    {
    case 65001: return il2cpp_encoding_utf8_get_chars(bytes, index, count);
    case 1200:  return il2cpp_encoding_unicode_get_chars(bytes, index, count);
    case 1201:  return il2cpp_encoding_unicodebe_get_chars(bytes, index, count);
    case 20127: return il2cpp_encoding_ascii_get_chars(bytes, index, count);
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

Il2CppArray* icall_encoding_get_chars_all(Il2CppObject* self, Il2CppArray* bytes)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    const int32_t len = il2cpp_array_get_length(bytes);
    return icall_encoding_get_chars(self, bytes, 0, len);
}

//------------------------------------------------------------------------------
// ENC-REST (nb9): counts + range/char[] overloads (see il2cpp_encoding.h).
//------------------------------------------------------------------------------
namespace {

void ThrowNullSelf()
{
    il2cpp_throw_new("System.NullReferenceException,System.Exception",
                     "Object reference not set to an instance of an object.");
}

// Validate a managed char[] argument and return its length.
int32_t CheckCharArrayArg(Il2CppArray* chars)
{
    if (chars == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    if (chars->isRefElem || chars->elemSize != static_cast<int32_t>(sizeof(char16_t)))
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
                         "Object must be an array of chars.");
    }
    return chars->length;
}

// Validate a managed char[] RANGE and return the unit pointer (nullptr for an
// empty range).
const char16_t* CheckCharsRange(Il2CppArray* chars, int32_t index, int32_t count, int32_t* outCount)
{
    const int32_t n = CheckCharArrayArg(chars);
    if (index < 0 || count < 0 || n - index < count)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Index and count must refer to a location within the buffer.");
    }
    *outCount = count;
    if (count == 0)
    {
        return nullptr;
    }
    return reinterpret_cast<const char16_t*>(il2cpp_array_get_elem_ptr(chars, index));
}

int32_t EncodeCountForPage(int32_t page, const char16_t* src, int32_t len)
{
    switch (page)
    {
    case 65001: return il2cpp_encoding_utf8_convert(src, len, nullptr, 0);
    case 1200:
    case 1201: return len * 2;
    case 20127: return il2cpp_encoding_ascii_convert(src, len, nullptr, 0);
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

Il2CppArray* EncodeToBytesForPage(int32_t page, const char16_t* src, int32_t len)
{
    const int32_t required = EncodeCountForPage(page, src, len);
    Il2CppArray* arr = AllocBytes(required);
    if (required == 0)
    {
        return arr;
    }
    char* dst = reinterpret_cast<char*>(il2cpp_array_get_elem_ptr(arr, 0));
    switch (page)
    {
    case 65001: il2cpp_encoding_utf8_convert(src, len, dst, required); break;
    case 1200:
        std::memcpy(dst, src, static_cast<size_t>(required));
        break;
    case 1201:
        for (int32_t i = 0; i < len; ++i)
        {
            dst[i * 2] = static_cast<char>(static_cast<uint16_t>(src[i]) >> 8);
            dst[i * 2 + 1] = static_cast<char>(static_cast<uint16_t>(src[i]) & 0xFF);
        }
        break;
    case 20127: il2cpp_encoding_ascii_convert(src, len, dst, required); break;
    default:
        il2cpp_throw_new("System.ArgumentException,System.Exception",
                         "Unknown encoding code page.");
    }
    return arr;
}

int32_t DecodeCountForPage(int32_t page, const uint8_t* src, int32_t n)
{
    switch (page)
    {
    case 65001: return ConvertUtf8ToUtf16(reinterpret_cast<const char*>(src), n, nullptr, 0);
    case 1200: return Utf16LeDecode(src, n, nullptr);
    case 1201: return Utf16BeDecode(src, n, nullptr);
    case 20127: return n;
    }
    il2cpp_throw_new("System.ArgumentException,System.Exception",
                     "Unknown encoding code page.");
}

} // namespace

int32_t icall_encoding_get_byte_count_string(Il2CppObject* self, Il2CppString* text)
{
    if (self == nullptr) ThrowNullSelf();
    if (text == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    return EncodeCountForPage(PageOf(self), text->chars, text->length);
}

int32_t icall_encoding_get_byte_count_chars(Il2CppObject* self, Il2CppArray* chars)
{
    if (self == nullptr) ThrowNullSelf();
    const int32_t n = CheckCharArrayArg(chars);
    return EncodeCountForPage(PageOf(self),
        n == 0 ? nullptr : reinterpret_cast<const char16_t*>(chars->data), n);
}

int32_t icall_encoding_get_byte_count_chars_range(Il2CppObject* self, Il2CppArray* chars,
                                                  int32_t index, int32_t count)
{
    if (self == nullptr) ThrowNullSelf();
    int32_t n = 0;
    const char16_t* src = CheckCharsRange(chars, index, count, &n);
    return EncodeCountForPage(PageOf(self), src, n);
}

int32_t icall_encoding_get_char_count_bytes(Il2CppObject* self, Il2CppArray* bytes)
{
    if (self == nullptr) ThrowNullSelf();
    if (bytes == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.");
    }
    const int32_t n = bytes->length;
    const uint8_t* src = (n == 0)
        ? nullptr : reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(bytes, 0));
    return DecodeCountForPage(PageOf(self), src, n);
}

int32_t icall_encoding_get_char_count_bytes_range(Il2CppObject* self, Il2CppArray* bytes,
                                                  int32_t index, int32_t count)
{
    if (self == nullptr) ThrowNullSelf();
    int32_t n = 0;
    const uint8_t* src = CheckBytesRange(bytes, index, count, &n);
    return DecodeCountForPage(PageOf(self), src, n);
}

Il2CppArray* icall_encoding_get_bytes_chars(Il2CppObject* self, Il2CppArray* chars)
{
    if (self == nullptr) ThrowNullSelf();
    const int32_t n = CheckCharArrayArg(chars);
    return EncodeToBytesForPage(PageOf(self),
        n == 0 ? nullptr : reinterpret_cast<const char16_t*>(chars->data), n);
}

Il2CppArray* icall_encoding_get_bytes_chars_range(Il2CppObject* self, Il2CppArray* chars,
                                                  int32_t index, int32_t count)
{
    if (self == nullptr) ThrowNullSelf();
    int32_t n = 0;
    const char16_t* src = CheckCharsRange(chars, index, count, &n);
    return EncodeToBytesForPage(PageOf(self), src, n);
}

Il2CppString* icall_encoding_get_string_range(Il2CppObject* self, Il2CppArray* bytes,
                                              int32_t index, int32_t count)
{
    if (self == nullptr) ThrowNullSelf();
    int32_t n = 0;
    const uint8_t* src = CheckBytesRange(bytes, index, count, &n);
    return il2cpp_encoding_get_chars(src, n,
        static_cast<il2cpp_encoding_code_page>(PageOf(self)));
}

int32_t icall_encoding_get_chars_into(Il2CppObject* self, Il2CppArray* bytes,
                                      int32_t byteIndex, int32_t byteCount,
                                      Il2CppArray* chars, int32_t charIndex)
{
    if (self == nullptr) ThrowNullSelf();
    const int32_t dstLen = CheckCharArrayArg(chars);
    if (charIndex < 0)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Non-negative number required.");
    }
    // Decode the byte range into a fresh char[] (the existing ranged entry),
    // then copy it into the caller's destination after the capacity check.
    Il2CppArray* decoded = GetCharsRange(bytes, byteIndex, byteCount,
        static_cast<il2cpp_encoding_code_page>(PageOf(self)));
    const int32_t units = decoded->length;
    if (static_cast<int64_t>(charIndex) + units > dstLen)
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
                         "Destination array is not long enough.");
    }
    if (units > 0)
    {
        std::memcpy(chars->data + static_cast<size_t>(charIndex) * sizeof(char16_t),
                    decoded->data, static_cast<size_t>(units) * sizeof(char16_t));
    }
    return units;
}

}} // namespace il2cpp::runtime

//------------------------------------------------------------------------------
// GLOBAL-scope descriptors (matching the il2cpp_encoding.h externs and the
// transpiler's &System_Text_*Encoding_class emission -- the
// System_IO_*_class / System_Version_class precedent).
//------------------------------------------------------------------------------
#define ENCODING_CLASS(SYM, DOTNAME)  \
    ::il2cpp::Il2CppClass SYM = {     \
        DOTNAME,                      \
        static_cast<int32_t>(sizeof(::il2cpp::Il2CppEncodingObject)), \
        0u,                           \
        0,                            \
        &System_Object_class,         \
        nullptr, 0, nullptr, 0,       \
    }

ENCODING_CLASS(System_Text_Encoding_class, "System.Text.Encoding");
ENCODING_CLASS(System_Text_UTF8Encoding_class, "System.Text.UTF8Encoding");
ENCODING_CLASS(System_Text_UnicodeEncoding_class, "System.Text.UnicodeEncoding");
ENCODING_CLASS(System_Text_ASCIIEncoding_class, "System.Text.ASCIIEncoding");

#undef ENCODING_CLASS

namespace il2cpp { namespace runtime {

void il2cpp_encoding_register_classes()
{
    static bool registered = false;
    if (registered)
    {
        return;
    }
    il2cpp_class_register(&System_Text_Encoding_class);
    il2cpp_class_register(&System_Text_UTF8Encoding_class);
    il2cpp_class_register(&System_Text_UnicodeEncoding_class);
    il2cpp_class_register(&System_Text_ASCIIEncoding_class);
    registered = true;
}

}} // namespace il2cpp::runtime
