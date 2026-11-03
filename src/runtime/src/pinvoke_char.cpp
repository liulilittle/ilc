// pinvoke_char.cpp -- CP_ACP code-page conversion for the Ansi `ref/out char`
// P/Invoke cell (declared in il2cpp_pinvoke_bridge.h).
//
// MEASURED net48 contract (Framework64 v4.0.30319; host CP_ACP=936 zh-CN;
// pinned byte-exactly by tests/cases/pinvoke-charref):
//   * Ansi `ref/out char` is a 1-BYTE native cell. The managed UTF-16 unit is
//     converted through the SYSTEM ANSI code page (Encoding.Default) and only
//     the FIRST encoded byte crosses the boundary (U+4E2D -> 0xD6 under CP936;
//     .NET measures Encoding.Default.GetBytes(ch)[0]). Requesting a 1-byte
//     target buffer from WideCharToMultiByte instead FAILS with
//     ERROR_INSUFFICIENT_BUFFER and leaves the cell untouched, so the unit is
//     encoded into a small buffer and its first byte is taken.
//   * The copy-back decodes that single byte through the same code page. An
//     incomplete / invalid byte maps to '?' (0xD6 -> 0x003F); CP936's 0xFF
//     fallback (-> 0xF8F5) is reproduced verbatim by MultiByteToWideChar.
//   * Unrepresentable input units use '?' (U+FFFF -> 0x3F), matching net48's
//     replacement fallback.
// CharSet.Unicode / Auto do NOT use this path (bare 2-byte uint16_t cell).
//
// POSIX targets have no ANSI code page (net48's Ansi semantics are Windows
// only). They use the documented UTF-8 approximation below instead of failing
// LOUD: ASCII survives both directions, every other unit maps to '?'.
#include "il2cpp_pinvoke_bridge.h"

#include <cstdint>
#include <cstdlib>

#if defined(_WIN32)
#include <windows.h>
#endif

namespace il2cpp {
namespace runtime {

int32_t il2cpp_pinvoke_char_ansi_to_byte(int32_t utf16)
{
    const uint16_t unit = (uint16_t)(utf16 & 0xFFFF);
#if defined(_WIN32)
    wchar_t wc = (wchar_t)unit;
    // Large enough for any single BMP unit in any ANSI code page (DBCS <= 2).
    char buf[8];
    const int n = ::WideCharToMultiByte(CP_ACP, 0, &wc, 1, buf, (int)sizeof(buf), "?", nullptr);
    if (n <= 0) return 0x3F;
    return (int32_t)(uint8_t)buf[0];
#else
    if (unit < 0x80) return (int32_t)unit;
    return 0x3F;
#endif
}

int32_t il2cpp_pinvoke_char_ansi_from_byte(uint8_t byte)
{
#if defined(_WIN32)
    wchar_t wc = 0;
    const int n = ::MultiByteToWideChar(CP_ACP, 0, (const char*)&byte, 1, &wc, 1);
    if (n <= 0) return 0x3F;
    return (int32_t)wc;
#else
    if (byte < 0x80) return (int32_t)byte;
    return 0x3F;
#endif
}

//------------------------------------------------------------------------------
// char[] + CharSet.Ansi: the WHOLE ARRAY is converted to one CP_ACP byte buffer
// (unlike the per-unit ref/out cell above, this is a bulk conversion because
// net48 hands the callee a NUL-terminated narrow string).
//
// MEASURED net48 contract (Framework64 v4.0.30319, CP_ACP=936; oracles
// .opencode/tmp/oracle-chararr2 and oracle-chararr3/oracle-chararr4):
//   * {'\u4E2D','\u6587'} -> D6 D0 CE C4 (2 bytes per unit, strlen 4)
//   * {'A','b','c'}       -> 41 62 63      (strlen 3)
//   * an EMPTY array      -> a NON-NULL pointer whose first byte is NUL
//                            (strlen 0). It is NOT NULL and NOT a zero-length
//                            allocation.
//   * a null array        -> NULL (the only case that passes NULL)
//   * {'A','\0','B'}      -> strlen 1: an embedded U+0000 truncates naturally
//                            because the encoded buffer is NUL-terminated.
//   * copy-back: default/[In] -> NONE; [In,Out] -> yes; [Out] -> the callee
//     receives a ZEROED buffer (hash 0, measured) and the result is copied back.
//------------------------------------------------------------------------------

// Encode the managed UTF-16 payload into a freshly allocated, NUL-terminated
// CP_ACP buffer. Never returns NULL for a non-null array (an empty array yields
// a 1-byte buffer holding NUL), matching the measured non-NULL contract.
char* il2cpp_pinvoke_char_array_ansi_encode(const Il2CppArray* a, int32_t* outLen)
{
    const int32_t length = a->length;
    *outLen = 0;
    // Worst case for any ANSI code page is 2 bytes per UTF-16 unit (DBCS).
    const size_t cap = (size_t)length * 2 + 1;
    char* buf = (char*)std::calloc(cap, 1);
    if (buf == nullptr) return nullptr;
#if defined(_WIN32)
    if (length > 0)
    {
        const int n = ::WideCharToMultiByte(CP_ACP, 0,
                                            reinterpret_cast<const wchar_t*>(a->data),
                                            (int)length, buf, (int)(cap - 1), "?", nullptr);
        if (n > 0) *outLen = (int32_t)n;
    }
#else
    // POSIX has no ANSI code page: ASCII survives, every other unit maps to '?'.
    {
        const uint16_t* src = reinterpret_cast<const uint16_t*>(a->data);
        int32_t n = 0;
        for (int32_t i = 0; i < length; i++)
            buf[n++] = (src[i] < 0x80) ? (char)src[i] : (char)'?';
        *outLen = n;
    }
#endif
    buf[*outLen] = '\0';
    return buf;
}

// Decode the callee's CP_ACP buffer back into the managed char[] payload
// ([In,Out] / [Out]). The callee's byte count is NOT known here (a P/Invoke
// callee only returns its own value), so the buffer is walked as the
// NUL-terminated string net48 hands over: units are decoded up to the first
// NUL, and every remaining element is zeroed (matching the zeroed [Out] temp).
void il2cpp_pinvoke_char_array_ansi_decode(const char* buf, int32_t bufLen, Il2CppArray* a)
{
    if (buf == nullptr || a == nullptr) return;
    uint16_t* dst = reinterpret_cast<uint16_t*>(a->data);
    const int32_t length = a->length;
    if (length <= 0) return;
    // Stop at the buffer's NUL terminator (or the supplied bound).
    int32_t n = 0;
    while (n < bufLen && buf[n] != '\0') n++;
#if defined(_WIN32)
    int converted = 0;
    if (n > 0)
        converted = ::MultiByteToWideChar(CP_ACP, 0, buf, (int)n,
                                          reinterpret_cast<wchar_t*>(dst), (int)(length < n ? length : n));
    if (converted < 0) converted = 0;
    for (int32_t i = converted; i < length; i++) dst[i] = 0;
#else
    for (int32_t i = 0; i < length; i++)
        dst[i] = (i < n && (uint8_t)buf[i] < 0x80) ? (uint16_t)(uint8_t)buf[i] : (uint16_t)0;
#endif
}

} // namespace runtime
} // namespace il2cpp