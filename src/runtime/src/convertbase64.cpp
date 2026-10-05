//------------------------------------------------------------------------------
// convertbase64.cpp -- System.Convert Base64 icall implementation
// (see il2cpp_convertbase64.h).
//
// Standard RFC 4648 Base64 (alphabet 'A'-'Z','a'-'z','0'-'9','+','/',
// padding '=') following the net48 Convert contract (probe-verified against
// csc.exe net48 on 2026-08-04 -- see docs/audit/baseconv-conformance.md):
//   * Encode: 3-byte quantums -> 4 chars; final partial quantum padded
//     with '=' so the output length is always a multiple of 4.
//   * Decode: ASCII whitespace (space/tab/CR/LF) is ignored anywhere;
//     '=' is legal only in the FINAL quantum: a single '=' after exactly
//     three valid chars, or '==' after exactly two (whitespace may sit
//     between the two '=' and trail after them). net48 quirks reproduced:
//     "AB= " (single '=' after two chars, trailing space) throws, while
//     "TWE==" (three valid chars + '==') decodes to ONE byte and "TWE"
//     (unpadded three chars) throws. A leftover single valid char
//     (length % 4 == 1) is invalid. Illegal characters -> FormatException,
//     null input -> ArgumentNullException.
//------------------------------------------------------------------------------

#include "il2cpp_convertbase64.h"

#include <cstdint>
#include <cstdlib>
#include <vector>

namespace il2cpp { namespace runtime {

namespace {

// The RFC 4648 standard alphabet, index == the 6-bit value.
const char kBase64Alphabet[] =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

// The .NET FromBase64String whitespace set (space / tab / CR / LF).
bool IsBase64Whitespace(char16_t c)
{
    return c == u' ' || c == u'\t' || c == u'\r' || c == u'\n';
}

// 6-bit value of a base64 character, or -1 when 'c' is not in the alphabet.
int Base64Value(char16_t c)
{
    if (c >= u'A' && c <= u'Z') return static_cast<int>(c - u'A');
    if (c >= u'a' && c <= u'z') return static_cast<int>(c - u'a') + 26;
    if (c >= u'0' && c <= u'9') return static_cast<int>(c - u'0') + 52;
    if (c == u'+') return 62;
    if (c == u'/') return 63;
    return -1;
}

// The standard net48 FormatException text for malformed Base64 input.
[[noreturn]] void ThrowBadBase64()
{
    il2cpp_throw_format_exception(
        "The input is not a valid Base-64 string as it contains a non-base 64 "
        "character, more than two padding characters, or an illegal character "
        "among the padding characters.");
    // il2cpp_throw_format_exception throws a C++ exception and never returns;
    // the abort is a defensive fallback that also silences -Winvalid-noreturn
    // (same pattern as monitor.cpp's throw helpers).
    std::abort();
}

// Byte accessor: bounds-checked element read (elemSize 1 -> one byte).
uint8_t ByteAt(Il2CppArray* arr, int32_t index)
{
    return *reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(arr, index));
}

// Byte writer: bounds-checked element write (elemSize 1 -> one byte).
void ByteSet(Il2CppArray* arr, int32_t index, uint8_t value)
{
    *reinterpret_cast<uint8_t*>(il2cpp_array_get_elem_ptr(arr, index)) = value;
}

} // namespace

//------------------------------------------------------------------------------
// Convert.ToBase64String(byte[]): 3 bytes -> 4 chars; the final quantum is
// zero-padded and emitted as '=' (1 for 1 leftover byte, 2 for 2). The
// output is written into a temporary buffer first (the encoder is the sole
// writer; the managed string is published only after it is complete).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_convert_to_base64_string(Il2CppArray* bytes)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("inArray");
    }
    const int32_t n = il2cpp_array_get_length(bytes);
    const int32_t outLen = ((n + 2) / 3) * 4;
    std::vector<char16_t> buf(static_cast<size_t>(outLen));

    int32_t i = 0;
    int32_t o = 0;
    while (i + 2 < n)
    {
        const uint32_t v = (static_cast<uint32_t>(ByteAt(bytes, i)) << 16)
                         | (static_cast<uint32_t>(ByteAt(bytes, i + 1)) << 8)
                         | static_cast<uint32_t>(ByteAt(bytes, i + 2));
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 18) & 63]);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 12) & 63]);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 6) & 63]);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[v & 63]);
        i += 3;
    }
    const int32_t rem = n - i;
    if (rem == 1)
    {
        const uint32_t v = static_cast<uint32_t>(ByteAt(bytes, i)) << 16;
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 18) & 63]);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 12) & 63]);
        buf[static_cast<size_t>(o++)] = u'=';
        buf[static_cast<size_t>(o++)] = u'=';
    }
    else if (rem == 2)
    {
        const uint32_t v = (static_cast<uint32_t>(ByteAt(bytes, i)) << 16)
                         | (static_cast<uint32_t>(ByteAt(bytes, i + 1)) << 8);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 18) & 63]);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 12) & 63]);
        buf[static_cast<size_t>(o++)] = static_cast<char16_t>(kBase64Alphabet[(v >> 6) & 63]);
        buf[static_cast<size_t>(o++)] = u'=';
    }
    return il2cpp_string_new(buf.data(), outLen);
}

//------------------------------------------------------------------------------
// Convert.FromBase64String(string): two passes over the input.
//
// Pass 1 (validation): every character is classified -- whitespace (ignored),
// '=' (padding) or a valid alphabet char. '=' placement follows the net48
// final-quantum rule EXACTLY (probe-verified):
//   * first '=' at quantum position 2 ("AB==") requires a closing second
//     '='; a lone '=' there throws even with trailing whitespace ("AB= ");
//   * first '=' at quantum position 3 ("TWE=") may be final, or may be
//     followed by one closing '=' at the next quantum's position 0 --
//     net48 accepts "TWE==" (three valid chars + '==') and decodes it to
//     ONE byte, and the same with whitespace between the pads;
//   * a third '=' or a non-whitespace char after padding -> FormatException.
// The decoded length is derived from the valid-char count and padding
// count exactly like net48: leftover 1 -> FormatException; leftover 0/2/3
// are legal only with 0/2/1-or-2 padding chars respectively (unpadded
// "TWE"/"TW" throw).
//
// Pass 2 (decode): a faithful port of the net48 FromBase64_Decode state
// machine -- a 4-byte marker block accumulates six-bit codes; every fourth
// valid char flushes 3 bytes; a trailing '=' shifts zero code bits in (6 for
// one pad, 12 for two) and flushes the partial quantum. This reproduces the
// net48 byte output for every legal shape, including the "TWE==" -> 1 byte
// case (the byte comes from the LAST two valid chars).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_convert_from_base64_string(Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("s");
    }
    const char16_t* begin = text->chars;
    const char16_t* end = begin + text->length;

    // --- Pass 1: validate and compute the decoded length. ---
    int32_t validCount = 0;   // non-whitespace chars, '=' included
    int32_t paddingCount = 0;
    bool paddingSeen = false;
    bool firstEqAtPosTwo = false; // first '=' was at quantum position 2
    for (const char16_t* p = begin; p != end; ++p)
    {
        const char16_t c = *p;
        if (IsBase64Whitespace(c))
        {
            continue;
        }
        if (c == u'=')
        {
            // net48 final-quantum placement rule (see the header comment).
            const int32_t pos = validCount & 3;
            bool legal = false;
            if (paddingCount == 0)
            {
                legal = (pos == 2 || pos == 3);
                firstEqAtPosTwo = (pos == 2);
            }
            else if (paddingCount == 1)
            {
                // Second '=': closes the "==" pair at position 3, or lands
                // at the next quantum's position 0 after a position-3 first
                // '=' (the "TWE==" shape net48 accepts).
                legal = (pos == 3 && firstEqAtPosTwo) ||
                        (pos == 0 && !firstEqAtPosTwo);
            }
            if (!legal)
            {
                ThrowBadBase64();
            }
            ++paddingCount;
            paddingSeen = true;
            ++validCount;
        }
        else
        {
            // Non-whitespace after the first '=' is only another '=' (handled
            // above); any other character -> FormatException.
            if (paddingSeen)
            {
                ThrowBadBase64();
            }
            if (Base64Value(c) < 0)
            {
                ThrowBadBase64();
            }
            ++validCount;
        }
    }

    // A lone '=' at quantum position 2 needs its closing '=': net48 trims
    // trailing whitespace first, so "AB= " is exactly "AB=" and throws.
    if (paddingCount == 1 && firstEqAtPosTwo)
    {
        ThrowBadBase64();
    }

    // Length per net48: (valid/4)*3 plus the trailing-padding bytes (one
    // '=' -> 2 bytes, "==" -> 1 byte). The leftover/padding coupling below
    // mirrors the net48 decoder: an unpadded leftover of 2 or 3 valid chars
    // ("TW"/"TWE") never completes a quantum and throws.
    const int32_t nonPad = validCount - paddingCount;
    const int32_t leftover = nonPad & 3;
    if (leftover == 1 ||
        (leftover == 0 && paddingCount != 0) ||
        (leftover == 2 && paddingCount != 2) ||
        (leftover == 3 && paddingCount == 0))
    {
        il2cpp_throw_format_exception("Invalid length for a Base-64 char array or string.");
    }
    const int32_t outLen = (nonPad / 4) * 3
        + (paddingCount == 1 ? 2 : (paddingCount == 2 ? 1 : 0));

    // --- Pass 2: decode into the (zero-initialized) byte array. ---
    Il2CppArray* result = il2cpp_array_new(nullptr, 1, false, outLen);
    uint32_t block = 0x000000FFu; // 4 x 6-bit code accumulator + marker bit
    int32_t oi = 0;
    for (const char16_t* p = begin; p != end; ++p)
    {
        const char16_t c = *p;
        if (IsBase64Whitespace(c))
        {
            continue;
        }
        const int32_t code = Base64Value(c);
        if (code >= 0)
        {
            block = (block << 6) | static_cast<uint32_t>(code);
            // The 0xFF marker reaches bit 31 exactly when four valid chars
            // have been accumulated -> emit 3 bytes (net48 marker logic).
            if ((block & 0x80000000u) != 0)
            {
                ByteSet(result, oi, static_cast<uint8_t>(block >> 16));
                ByteSet(result, oi + 1, static_cast<uint8_t>(block >> 8));
                ByteSet(result, oi + 2, static_cast<uint8_t>(block));
                oi += 3;
                block = 0x000000FFu;
            }
        }
        else
        {
            // '=' (pass 1 guarantees no other character reaches here).
            const char16_t* q = p + 1;
            while (q != end && IsBase64Whitespace(*q))
            {
                ++q;
            }
            if (q != end)
            {
                // A closing '=' follows: both pads shift in as zero codes
                // ("==" -> 1 byte; net48 emits byte (block >> 16)).
                block <<= 12;
                ByteSet(result, oi, static_cast<uint8_t>(block >> 16));
                oi += 1;
            }
            else
            {
                // Single trailing '=': one zero code ("TWE=" -> 2 bytes).
                block <<= 6;
                ByteSet(result, oi, static_cast<uint8_t>(block >> 16));
                ByteSet(result, oi + 1, static_cast<uint8_t>(block >> 8));
                oi += 2;
            }
            break; // nothing but whitespace may follow the padding
        }
    }
    return result;
}

}} // namespace il2cpp::runtime
