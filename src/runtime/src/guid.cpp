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

// char.IsWhiteSpace over UTF-16 code units -- the literal set net48's
// Guid input trimming uses (System.String.Trim() trims exactly the
// char.IsWhiteSpace set). Listed literally so the runtime never depends on
// the host C++ locale; U+FEFF and U+180E are NOT whitespace in net48
// (measured: Guid.TryParse rejects them at the edges).
bool IsGuidWhitespace(char16_t c)
{
    switch (c)
    {
        case u'\t': case u'\n': case u'\v': case u'\f': case u'\r':
        case u' ':
        case u'\x0085':  // NEL
        case u'\x00A0':  // NBSP
        case u'\x1680':  // OGHAM SPACE MARK
        case u'\x2000': case u'\x2001': case u'\x2002': case u'\x2003':
        case u'\x2004': case u'\x2005': case u'\x2006': case u'\x2007':
        case u'\x2008': case u'\x2009': case u'\x200A':
        case u'\x2028': case u'\x2029':
        case u'\x202F':  // NARROW NBSP
        case u'\x205F':  // MEDIUM MATHEMATICAL SPACE
        case u'\x3000':  // IDEOGRAPHIC SPACE
            return true;
        default:
            return false;
    }
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
// Guid(int32 a, int16 b, int16 c, byte d..k) -- the 11-argument component
// ctor. The IL pushes 11 int32 slots; the storage layout is the .NET byte
// layout (Data1/Data2/Data3 little-endian, Data4 bytes in order) per the
// guid.h header contract. The icall writes the receiver's 16-byte value
// slot (the emitter passes &slot for this ResultIsReceiver-style ctor).
//------------------------------------------------------------------------------
void il2cpp_guid_ctor_components(uint8_t bytes[16],
                                 int32_t a, int32_t b, int32_t c,
                                 int32_t d, int32_t e, int32_t f, int32_t g,
                                 int32_t h, int32_t i, int32_t j, int32_t k)
{
    bytes[0]  = static_cast<uint8_t>(a & 0xFF);
    bytes[1]  = static_cast<uint8_t>((a >> 8) & 0xFF);
    bytes[2]  = static_cast<uint8_t>((a >> 16) & 0xFF);
    bytes[3]  = static_cast<uint8_t>((a >> 24) & 0xFF);
    bytes[4]  = static_cast<uint8_t>(b & 0xFF);
    bytes[5]  = static_cast<uint8_t>((b >> 8) & 0xFF);
    bytes[6]  = static_cast<uint8_t>(c & 0xFF);
    bytes[7]  = static_cast<uint8_t>((c >> 8) & 0xFF);
    const int32_t rest[8] = { d, e, f, g, h, i, j, k };
    for (int32_t n = 0; n < 8; ++n)
    {
        bytes[8 + n] = static_cast<uint8_t>(rest[n] & 0xFF);
    }
}

//------------------------------------------------------------------------------
// Guid(byte[] b) -- the net48 from-bytes ctor. A null array is
// ArgumentNullException; a length other than 16 is ArgumentException (net48:
// "The byte array must be exactly 16 bytes long"); otherwise the 16 bytes are
// copied verbatim (the .NET byte layout IS the storage layout -- see the
// header).
//------------------------------------------------------------------------------
void il2cpp_guid_ctor_bytes(uint8_t bytes[16], Il2CppArray* b)
{
    if (b == nullptr)
    {
        il2cpp_throw_argument_null_exception("b");
    }
    if (b->length != 16)
    {
        il2cpp_throw_argument_exception("The byte array must be exactly 16 bytes long.");
    }
    std::memcpy(bytes, b->data, 16);
}

//------------------------------------------------------------------------------
// Parse -- net48 Guid.TryParse/Parse input grammar (MEASURED on a real net48
// CLR; the earlier "strict D/N only" reading was wrong about B and P):
//   1. Trim leading/trailing char.IsWhiteSpace code units. net48 trims the
//      outer text BEFORE looking at the decoration, so "  {D}  " is legal,
//      and so are U+00A0 / U+2003 edges; U+FEFF / U+180E are NOT whitespace
//      there and fail.
//   2. After trimming, exactly one of:
//        N -- 32 hex chars, no dashes
//        D -- 36 chars, dashes at 8/13/18/23, hex elsewhere
//        B -- "{" + D + "}"
//        P -- "(" + D + ")"
//      Anything else (mismatched braces/parens, doubled decoration,
//      whitespace INSIDE the braces -- which also changes the length --,
//      an N body inside braces, wrong length/positions) is rejected.
// The "X" form ("{0x…,0x…,0x…,{0x…}}") IS accepted by net48 but is NOT
// implemented here (recorded deviation, see guid.h).
// TryParse-style: verify-then-mutate, so a rejected input never touches the
// output buffer.
//------------------------------------------------------------------------------
bool il2cpp_guid_parse(Il2CppString* s, uint8_t bytes[16])
{
    if (s == nullptr)
    {
        return false;
    }
    int32_t begin = 0;
    int32_t end = s->length;
    while (begin < end && IsGuidWhitespace(s->chars[begin]))
    {
        ++begin;
    }
    while (end > begin && IsGuidWhitespace(s->chars[end - 1]))
    {
        --end;
    }
    const char16_t* p = s->chars + begin;
    int32_t n = end - begin;
    if (n == 38 && ((p[0] == u'{' && p[37] == u'}') || (p[0] == u'(' && p[37] == u')')))
    {
        ++p;    // B ("{D}") / P ("(D)"): the decoration is not part of the body
        n = 36;
    }
    if (n == 36)
    {
        for (int32_t i = 0; i < 36; ++i)
        {
            if (IsDashPosition(i))
            {
                if (p[i] != u'-')
                {
                    return false;
                }
            }
            else if (HexValue(p[i]) < 0)
            {
                return false;
            }
        }
    }
    else if (n == 32)
    {
        for (int32_t i = 0; i < 32; ++i)
        {
            if (HexValue(p[i]) < 0)
            {
                return false;
            }
        }
    }
    else
    {
        return false;
    }
    const CharPair* pairs = (n == 36) ? kPairs : kNFormatPairs;
    uint8_t out[16];
    for (int32_t k = 0; k < 16; ++k)
    {
        const CharPair& pair = pairs[k];
        const int32_t hi = HexValue(p[pair.c0]);
        const int32_t lo = HexValue(p[pair.c1]);
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
// ToString(format) -- the full .NET format family. Contract (net48 probe):
// null or "D" -> dddddddd-dddd-dddd-dddd-dddddddddddd; "N" -> no dashes;
// "B" -> {D}; "P" -> (D); "X" -> {0xdddddddd,0xdddd,0xdddd,{0xdd,...}} with
// Data1/Data2/Data3 shown big-endian and Data4 bytes in stored order. The
// format is case-insensitive; any other string is FormatException.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_guid_to_string_format(const uint8_t bytes[16], Il2CppString* format)
{
    static const char kHex[] = "0123456789abcdef";
    static const int32_t kOrder[16] = {
        3, 2, 1, 0,
        5, 4,
        7, 6,
        8, 9,
        10, 11, 12, 13, 14, 15,
    };
    char hex[33];
    for (int32_t i = 0; i < 16; ++i)
    {
        hex[i * 2]     = kHex[(bytes[kOrder[i]] >> 4) & 0x0F];
        hex[i * 2 + 1] = kHex[bytes[kOrder[i]] & 0x0F];
    }
    hex[32] = '\0';

    char fmtChar = 'd'; // null format behaves as "D"
    if (format != nullptr)
    {
        if (format->length != 1)
        {
            il2cpp_throw_format_exception("format");
        }
        fmtChar = static_cast<char>(format->chars[0]);
        switch (fmtChar)
        {
            case 'd': case 'D': case 'n': case 'N':
            case 'b': case 'B': case 'p': case 'P': case 'x': case 'X':
                break;
            default:
                il2cpp_throw_format_exception("format");
        }
    }

    char buf[80];
    int32_t p = 0;
    auto emit_dashed = [&]()
    {
        for (int32_t i = 0; i < 32; ++i)
        {
            if (i == 8 || i == 12 || i == 16 || i == 20) buf[p++] = '-';
            buf[p++] = hex[i];
        }
    };

    switch (fmtChar)
    {
        case 'n': case 'N':
            std::memcpy(buf, hex, 32);
            p = 32;
            break;
        case 'b': case 'B':
            buf[p++] = '{';
            emit_dashed();
            buf[p++] = '}';
            break;
        case 'p': case 'P':
            buf[p++] = '(';
            emit_dashed();
            buf[p++] = ')';
            break;
        case 'x': case 'X':
        {
            // {0xdddddddd,0xdddd,0xdddd,{0xdd,0xdd,0xdd,0xdd,0xdd,0xdd,0xdd,0xdd}}
            buf[p++] = '{';
            buf[p++] = '0'; buf[p++] = 'x';
            for (int32_t i = 0; i < 8; ++i) buf[p++] = hex[i];          // Data1 BE
            buf[p++] = ','; buf[p++] = '0'; buf[p++] = 'x';
            for (int32_t i = 8; i < 12; ++i) buf[p++] = hex[i];         // Data2 BE
            buf[p++] = ','; buf[p++] = '0'; buf[p++] = 'x';
            for (int32_t i = 12; i < 16; ++i) buf[p++] = hex[i];        // Data3 BE
            buf[p++] = ','; buf[p++] = '{';
            for (int32_t g = 0; g < 8; ++g)                             // Data4 in order
            {
                if (g != 0) buf[p++] = ',';
                buf[p++] = '0'; buf[p++] = 'x';
                buf[p++] = kHex[(bytes[8 + g] >> 4) & 0x0F];
                buf[p++] = kHex[bytes[8 + g] & 0x0F];
            }
            buf[p++] = '}'; buf[p++] = '}';
            break;
        }
        default: // 'd' / 'D'
            emit_dashed();
            break;
    }
    buf[p] = '\0';
    return il2cpp_string_new_from_utf8(buf);
}

//------------------------------------------------------------------------------
// ToByteArray() -- the 16-byte .NET storage image as a FRESH managed byte[16]
// (net48 returns a new array per call, so mutating one return value never
// corrupts the Guid slot or a previous return value -- the guid-tobytes
// contract). Primitive-array convention identical to the
// il2cpp_ipaddress_get_address_bytes precedent (ipaddr.cpp
// AddressBytesToArray): elemSize 1, isRefElement false. A null buffer yields
// nullptr (defensive).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_guid_to_byte_array(const uint8_t bytes[16])
{
    if (bytes == nullptr)
    {
        return nullptr;
    }
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, 16);
    std::memcpy(arr->data, bytes, 16);
    return arr;
}

//------------------------------------------------------------------------------
// Equals / CompareTo -- net48 field-wise contract (see guid.h).
//------------------------------------------------------------------------------
bool il2cpp_guid_equals(const uint8_t a[16], const uint8_t b[16])
{
    return std::memcmp(a, b, 16) == 0;
}

//------------------------------------------------------------------------------
// Guid.GetHashCode -- the ORACLE-probed net48 fold (guid.h contract):
//   LE32(bytes[0..3]) ^ ((int16 LE(bytes[4..5]) << 16) | LE16(bytes[6..7]))
//   ^ (bytes[10] << 24) ^ bytes[15]
// Probe evidence (.NET Framework 4.8, 20000 random GUIDs + every single-bit
// pattern, 0 mismatches): bytes 8, 9 and 11..14 never contribute; byte 10
// contributes its <<24 term; byte 15 contributes the low byte.
//------------------------------------------------------------------------------
int32_t il2cpp_guid_get_hash_code(const uint8_t bytes[16])
{
    const int32_t data1 = static_cast<int32_t>(ReadLE32U(bytes));
    const int32_t data2 = static_cast<int16_t>(ReadLE16U(bytes + 4));
    const int32_t data3 = static_cast<int32_t>(ReadLE16U(bytes + 6));
    return data1 ^ ((data2 << 16) | data3)
        ^ (static_cast<int32_t>(bytes[10]) << 24)
        ^ static_cast<int32_t>(bytes[15]);
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

bool il2cpp_guid_equals_object(const uint8_t a[16], Il2CppObject* obj)
{
    // net48 Guid.Equals(object) (guid.cs:930): null -> false, a non-Guid
    // object -> false, a Guid box -> 16-byte equality. The box payload sits
    // right after the object header (offset sizeof(void*)).
    if (obj == nullptr || obj->klass != &System_Guid_class)
    {
        return false;
    }
    Il2CppGuid g;
    std::memcpy(&g, reinterpret_cast<const char*>(obj) + sizeof(void*), sizeof(g));
    return il2cpp_guid_equals(a, g.b);
}

int32_t il2cpp_guid_compare_object(const uint8_t a[16], Il2CppObject* obj)
{
    // net48 Guid.CompareTo(object) (guid.cs:1001): null -> 1, a non-Guid
    // object throws ArgumentException, a Guid box -> the field-wise compare.
    if (obj == nullptr)
    {
        return 1;
    }
    if (obj->klass != &System_Guid_class)
    {
        il2cpp_throw_argument_exception("Object must be of type System.Guid.");
    }
    Il2CppGuid g;
    std::memcpy(&g, reinterpret_cast<const char*>(obj) + sizeof(void*), sizeof(g));
    return il2cpp_guid_compare(a, g.b);
}

} // namespace il2cpp
