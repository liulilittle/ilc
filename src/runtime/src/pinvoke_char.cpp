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

} // namespace runtime
} // namespace il2cpp