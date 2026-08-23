//------------------------------------------------------------------------------
// guid.cpp -- System.Guid runtime support (see guid.h).
//
// Random source (new_guid): all 16 bytes come from the OS CSPRNG through
// il2cpp::runtime::il2cpp_rng_fill (rng.h/rng.cpp -- the same hardened
// source that backs System.Security.Cryptography's RNG): BCryptGenRandom
// (bcrypt.dll) with an RtlGenRandom fallback on Windows, getrandom(2)
// (non-Android Linux) with a /dev/urandom read loop fallback on POSIX.
// The historical in-TU SplitMix64 LCG (seeded from steady_clock + an address
// constant) is gone: a clock/address-seeded LCG is predictable and unsuitable
// for GUIDs. GUID layout: on little-endian platforms the 16 bytes are the
// .NET byte layout (Data1/Data2/Data3 little-endian, Data4 in order).
//------------------------------------------------------------------------------

#include "guid.h"

#include "il2cpp_exception_ext.h"
#include "rng.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// RFC 4122 v4 over the OS CSPRNG. The random bytes come from
// il2cpp::runtime::il2cpp_rng_fill (which throws CryptographicException when
// every OS provider fails -- loud, never a silent degradation to a weak
// source); version and variant bits are set explicitly on the output per
// RFC 4122 section 4.4:
//   * version nibble = 4 in the first hex char of the third string group,
//     which is byte[7] high nibble in .NET's byte layout (Data3's MSB -- see
//     guid.h; the D-form group 3 renders byte[7] then byte[6]).
//   * variant bits = 10xx xxxx in byte[8] (clk_seq_hi_and_reserved).
//------------------------------------------------------------------------------

// Layout constants: the "D" string's dash positions (0-based).
bool IsDashPosition(int32_t i)
{
    return i == 8 || i == 13 || i == 18 || i == 23;
}

// Hex nibble value of a UTF-16 unit; -1 when not a hex digit.
int32_t HexValue(char16_t c)
{
    if (c >= u'0' && c <= u'9')
    {
        return static_cast<int32_t>(c - u'0');
    }
    if (c >= u'a' && c <= u'f')
    {
        return static_cast<int32_t>(c - u'a') + 10;
    }
    if (c >= u'A' && c <= u'F')
    {
        return static_cast<int32_t>(c - u'A') + 10;
    }
    return -1;
}

// Parse table: each pair of hex char positions maps to one output byte
// (little-endian field order, see guid.h). 16 pairs cover all 32 hex slots;
// the four gaps (8/13/18/23) are the mandatory dashes of the "D" form.
struct CharPair
{
    int32_t c0;       // first hex char position (high nibble)
    int32_t c1;       // second hex char position (low nibble)
    int32_t byteIdx;  // output byte index
};

const CharPair kPairs[16] = {
    { 0, 1, 3 }, { 2, 3, 2 }, { 4, 5, 1 }, { 6, 7, 0 },
    { 9, 10, 5 }, { 11, 12, 4 }, { 14, 15, 7 }, { 16, 17, 6 },
    { 19, 20, 8 }, { 21, 22, 9 },
    { 24, 25, 10 }, { 26, 27, 11 }, { 28, 29, 12 }, { 30, 31, 13 },
    { 32, 33, 14 }, { 34, 35, 15 },
};

// "N"-form (32 hex digits, no dashes -- net48 Guid.Parse/TryParse accept it)
// pair table: the same byte order as the "D" form with the four dash
// positions (8/13/18/23) simply removed, so every position shifts left by
// the number of preceding dashes.
const CharPair kNFormatPairs[16] = {
    { 0, 1, 3 }, { 2, 3, 2 }, { 4, 5, 1 }, { 6, 7, 0 },
    { 8, 9, 5 }, { 10, 11, 4 }, { 12, 13, 7 }, { 14, 15, 6 },
    { 16, 17, 8 }, { 18, 19, 9 },
    { 20, 21, 10 }, { 22, 23, 11 }, { 24, 25, 12 }, { 26, 27, 13 },
    { 28, 29, 14 }, { 30, 31, 15 },
};

// net48 compare helpers: little-endian UNSIGNED field reads (net48
// Guid.CompareTo compares Data1/Data2/Data3 as unsigned 32/16-bit values --
// see guid.h; a field whose high bit is set sorts AFTER a low-bit field).
uint32_t ReadLE32U(const uint8_t* p)
{
    return static_cast<uint32_t>(p[0])
        | (static_cast<uint32_t>(p[1]) << 8)
        | (static_cast<uint32_t>(p[2]) << 16)
        | (static_cast<uint32_t>(p[3]) << 24);
}

uint16_t ReadLE16U(const uint8_t* p)
{
    return static_cast<uint16_t>(
        static_cast<uint16_t>(p[0]) | (static_cast<uint16_t>(p[1]) << 8));
}

} // namespace

//------------------------------------------------------------------------------
// NewGuid() -- RFC 4122 v4 over the OS CSPRNG. il2cpp_rng_fill throws
// CryptographicException when every OS provider fails (a call that returns
// always holds fresh OS random bytes -- no weak source is ever substituted);
// version/variant bits are then set per RFC 4122 section 4.4.
//------------------------------------------------------------------------------
void il2cpp_guid_new_guid(uint8_t bytes[16])
{
    il2cpp::runtime::il2cpp_rng_fill(bytes, 16);
    bytes[7] = static_cast<uint8_t>((bytes[7] & 0x0F) | 0x40);
    bytes[8] = static_cast<uint8_t>((bytes[8] & 0x3F) | 0x80);
}

//------------------------------------------------------------------------------
// Parse -- strict "D" (36-char, dashes at 8/13/18/23) or "N" (32-char, no
// dashes) form, both net48-accepted (TryParse-style, verify-then-mutate).
//------------------------------------------------------------------------------
bool il2cpp_guid_parse(Il2CppString* s, uint8_t bytes[16])
{
    if (s == nullptr)
    {
        return false;
    }
    if (s->length == 36)
    {
        for (int32_t i = 0; i < 36; ++i)
        {
            if (IsDashPosition(i))
            {
                if (s->chars[i] != u'-')
                {
                    return false;
                }
            }
            else if (HexValue(s->chars[i]) < 0)
            {
                return false;
            }
        }
    }
    else if (s->length == 32)
    {
        for (int32_t i = 0; i < 32; ++i)
        {
            if (HexValue(s->chars[i]) < 0)
            {
                return false;
            }
        }
    }
    else
    {
        return false;
    }
    const CharPair* pairs = (s->length == 36) ? kPairs : kNFormatPairs;
    uint8_t out[16];
    for (int32_t k = 0; k < 16; ++k)
    {
        const CharPair& pair = pairs[k];
        const int32_t hi = HexValue(s->chars[pair.c0]);
        const int32_t lo = HexValue(s->chars[pair.c1]);
        out[pair.byteIdx] = static_cast<uint8_t>((hi << 4) | lo);
    }
    std::memcpy(bytes, out, 16);
    return true;
}

//------------------------------------------------------------------------------
// Parse (strict) -- net48 Guid.Parse semantics: throws instead of returning
// bool. A null string is ArgumentNullException; any other invalid input
// (wrong length, misplaced/missing dash, non-hex character) is
// FormatException. The core il2cpp_guid_parse is verify-then-mutate, so a
// thrown path never touches the output buffer.
//------------------------------------------------------------------------------
void il2cpp_guid_parse_strict(Il2CppString* s, uint8_t bytes[16])
{
    if (s == nullptr)
    {
        il2cpp_throw_argument_null_exception("input");
    }
    if (!il2cpp_guid_parse(s, bytes))
    {
        il2cpp_throw_format_exception(nullptr);
    }
}

//------------------------------------------------------------------------------
// ToString("D") -- 36-char lowercase form (byte order per guid.h).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_guid_to_string(const uint8_t bytes[16])
{
    static const char kHex[] = "0123456789abcdef";
    // Output order: Data1 bytes reversed, Data2 reversed, Data3 reversed,
    // Data4 in order -- dashes after groups of 4/2/2/2 hex chars.
    static const int32_t kOrder[16] = {
        3, 2, 1, 0,
        5, 4,
        7, 6,
        8, 9,
        10, 11, 12, 13, 14, 15,
    };
    char buf[37];
    int32_t p = 0;
    for (int32_t i = 0; i < 16; ++i)
    {
        if (i == 4 || i == 6 || i == 8 || i == 10)
        {
            buf[p++] = '-';
        }
        buf[p++] = kHex[(bytes[kOrder[i]] >> 4) & 0x0F];
        buf[p++] = kHex[bytes[kOrder[i]] & 0x0F];
    }
    buf[p] = '\0';
    return il2cpp_string_new_from_utf8(buf);
}

//------------------------------------------------------------------------------
// Equals / CompareTo -- net48 field-wise contract (see guid.h).
//------------------------------------------------------------------------------
bool il2cpp_guid_equals(const uint8_t a[16], const uint8_t b[16])
{
    return std::memcmp(a, b, 16) == 0;
}

int32_t il2cpp_guid_compare(const uint8_t a[16], const uint8_t b[16])
{
    const uint32_t d1a = ReadLE32U(a);
    const uint32_t d1b = ReadLE32U(b);
    if (d1a != d1b)
    {
        return (d1a < d1b) ? -1 : 1;
    }
    const uint16_t d2a = ReadLE16U(a + 4);
    const uint16_t d2b = ReadLE16U(b + 4);
    if (d2a != d2b)
    {
        return (d2a < d2b) ? -1 : 1;
    }
    const uint16_t d3a = ReadLE16U(a + 6);
    const uint16_t d3b = ReadLE16U(b + 6);
    if (d3a != d3b)
    {
        return (d3a < d3b) ? -1 : 1;
    }
    for (int32_t i = 8; i < 16; ++i)
    {
        if (a[i] != b[i])
        {
            return (a[i] < b[i]) ? -1 : 1;
        }
    }
    return 0;
}

} // namespace il2cpp
