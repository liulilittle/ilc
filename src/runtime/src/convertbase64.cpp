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

// The .NET ArgumentOutOfRangeException text for a negative offset/length.
void ThrowOutOfRange(const char* paramName)
{
    il2cpp_throw_argument_out_of_range_exception(paramName, "Value must be non-negative.");
}

// The .NET ArgumentOutOfRangeException text for an offset/length outside the
// array (net48 throws ArgumentOutOfRangeException, not ArgumentException).
void ThrowBase64Range(const char* paramName)
{
    il2cpp_throw_argument_out_of_range_exception(paramName,
        "Offset and length were out of bounds for the array or the length is greater than the number of elements from the index to the end of the source collection.");
}

// Exact net48 output length (ToBase64_CalculateAndValidateOutputLength,
// convert.cs:2315): 4 chars per 3-byte quantum (rounded up) plus one CRLF per
// complete 76-char line. No trailing CRLF when the output is an exact multiple
// of 76.
int32_t Base64OutputLength(int32_t length, bool lineBreaks)
{
    int64_t outLen = (static_cast<int64_t>(length) / 3) * 4;
    outLen += (length % 3 != 0) ? 4 : 0;
    if (outLen == 0)
    {
        return 0;
    }
    if (lineBreaks)
    {
        int64_t newLines = outLen / 76;
        if ((outLen % 76) == 0)
        {
            --newLines;
        }
        outLen += newLines * 2;
    }
    return static_cast<int32_t>(outLen);
}

// Faithful port of Convert.ConvertToBase64Array (convert.cs:2258-2313): write
// the RFC 4648 encoding of [offset, offset+length) into 'out' and return the
// number of chars written. The CRLF insertion points match net48 exactly (a
// break is emitted BEFORE the quantum that would start line 77, including a
// final partial quantum).
int32_t EncodeBase64Core(Il2CppArray* bytes, int32_t offset, int32_t length, bool lineBreaks, char16_t* out)
{
    const int32_t lengthmod3 = length % 3;
    const int32_t calcLength = offset + (length - lengthmod3);
    int32_t j = 0;
    int32_t charcount = 0;
    int32_t i;
    for (i = offset; i < calcLength; i += 3)
    {
        if (lineBreaks)
        {
            if (charcount == 76)
            {
                out[j++] = u'\r';
                out[j++] = u'\n';
                charcount = 0;
            }
            charcount += 4;
        }
        out[j] = static_cast<char16_t>(kBase64Alphabet[(ByteAt(bytes, i) & 0xfc) >> 2]);
        out[j + 1] = static_cast<char16_t>(kBase64Alphabet[((ByteAt(bytes, i) & 0x03) << 4)
                                                          | ((ByteAt(bytes, i + 1) & 0xf0) >> 4)]);
        out[j + 2] = static_cast<char16_t>(kBase64Alphabet[((ByteAt(bytes, i + 1) & 0x0f) << 2)
                                                          | ((ByteAt(bytes, i + 2) & 0xc0) >> 6)]);
        out[j + 3] = static_cast<char16_t>(kBase64Alphabet[ByteAt(bytes, i + 2) & 0x3f]);
        j += 4;
    }
    i = calcLength;
    if (lineBreaks && (lengthmod3 != 0) && (charcount == 76))
    {
        out[j++] = u'\r';
        out[j++] = u'\n';
    }
    switch (lengthmod3)
    {
        case 2:
            out[j] = static_cast<char16_t>(kBase64Alphabet[(ByteAt(bytes, i) & 0xfc) >> 2]);
            out[j + 1] = static_cast<char16_t>(kBase64Alphabet[((ByteAt(bytes, i) & 0x03) << 4)
                                                              | ((ByteAt(bytes, i + 1) & 0xf0) >> 4)]);
            out[j + 2] = static_cast<char16_t>(kBase64Alphabet[(ByteAt(bytes, i + 1) & 0x0f) << 2]);
            out[j + 3] = u'=';
            j += 4;
            break;
        case 1:
            out[j] = static_cast<char16_t>(kBase64Alphabet[(ByteAt(bytes, i) & 0xfc) >> 2]);
            out[j + 1] = static_cast<char16_t>(kBase64Alphabet[(ByteAt(bytes, i) & 0x03) << 4]);
            out[j + 2] = u'=';
            out[j + 3] = u'=';
            j += 4;
            break;
        default:
            break;
    }
    return j;
}

// Core encoder over the byte range [offset, offset+length) of 'bytes'.
// When 'lineBreaks' is true a CRLF is inserted every 76 output characters
// (net48 Base64FormattingOptions.InsertLineBreaks).
Il2CppString* EncodeBase64Range(Il2CppArray* bytes, int32_t offset, int32_t length, bool lineBreaks)
{
    const int32_t outLen = Base64OutputLength(length, lineBreaks);
    std::vector<char16_t> buf(static_cast<size_t>(outLen));
    const int32_t written = EncodeBase64Core(bytes, offset, length, lineBreaks, buf.data());
    return il2cpp_string_new(buf.data(), written);
}

// Validate the (offset, length) pair against the array length (net48 contract).
void ValidateRange(Il2CppArray* arr, int32_t offset, int32_t length, const char* offsetName, const char* lengthName)
{
    if (offset < 0) ThrowOutOfRange(offsetName);
    if (length < 0) ThrowOutOfRange(lengthName);
    const int32_t n = il2cpp_array_get_length(arr);
    if (offset > n || length > n - offset)
    {
        ThrowBase64Range(lengthName);
    }
}

} // namespace

// Core decoder over a char16 range [begin, end) -- defined below.
Il2CppArray* DecodeBase64Core(const char16_t* begin, const char16_t* end);

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
    return EncodeBase64Range(bytes, 0, n, false);
}

Il2CppString* il2cpp_convert_to_base64_string_range(Il2CppArray* bytes, int32_t offset, int32_t length)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("inArray");
    }
    ValidateRange(bytes, offset, length, "offset", "length");
    return EncodeBase64Range(bytes, offset, length, false);
}

Il2CppString* il2cpp_convert_to_base64_string_options(Il2CppArray* bytes, int32_t options)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("inArray");
    }
    // net48 Base64FormattingOptions: 0 = None, 1 = InsertLineBreaks; any other
    // value throws ArgumentException.
    if (options != 0 && options != 1)
    {
        il2cpp_throw_argument_exception("The options parameter must be None or InsertLineBreaks.");
    }
    const int32_t n = il2cpp_array_get_length(bytes);
    return EncodeBase64Range(bytes, 0, n, options == 1);
}

Il2CppString* il2cpp_convert_to_base64_string_range_options(Il2CppArray* bytes, int32_t offset, int32_t length, int32_t options)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("inArray");
    }
    if (options != 0 && options != 1)
    {
        il2cpp_throw_argument_exception("The options parameter must be None or InsertLineBreaks.");
    }
    ValidateRange(bytes, offset, length, "offset", "length");
    return EncodeBase64Range(bytes, offset, length, options == 1);
}

//------------------------------------------------------------------------------
// Convert.ToBase64CharArray(byte[], int, int, char[], int[, options]) --
// net48 validation order (convert.cs:2203-2255): inArray null, outArray null,
// negative length/offsetIn/offsetOut, invalid options, offsetIn past the end,
// then (after the empty-input early return) the offsetOut capacity check.
// The encoder is the same ConvertToBase64Array port the string form uses.
//------------------------------------------------------------------------------
int32_t il2cpp_convert_to_base64_char_array_options(Il2CppArray* bytes, int32_t offsetIn, int32_t length,
                                                    Il2CppArray* outChars, int32_t offsetOut, int32_t options)
{
    if (bytes == nullptr)
    {
        il2cpp_throw_argument_null_exception("inArray");
    }
    if (outChars == nullptr)
    {
        il2cpp_throw_argument_null_exception("outArray");
    }
    if (length < 0)
    {
        ThrowOutOfRange("length");
    }
    if (offsetIn < 0)
    {
        ThrowOutOfRange("offsetIn");
    }
    if (offsetOut < 0)
    {
        ThrowOutOfRange("offsetOut");
    }
    if (options != 0 && options != 1)
    {
        il2cpp_throw_argument_exception("The options parameter must be None or InsertLineBreaks.");
    }
    const int32_t inLength = il2cpp_array_get_length(bytes);
    if (offsetIn > inLength - length)
    {
        ThrowBase64Range("offsetIn");
    }
    if (inLength == 0)
    {
        return 0;
    }
    const bool lineBreaks = (options == 1);
    const int32_t needed = Base64OutputLength(length, lineBreaks);
    const int32_t outLength = il2cpp_array_get_length(outChars);
    if (offsetOut > outLength - needed)
    {
        ThrowBase64Range("offsetOut");
    }
    char16_t* dst = reinterpret_cast<char16_t*>(il2cpp_array_get_elem_ptr(outChars, offsetOut));
    return EncodeBase64Core(bytes, offsetIn, length, lineBreaks, dst);
}

int32_t il2cpp_convert_to_base64_char_array(Il2CppArray* bytes, int32_t offsetIn, int32_t length,
                                            Il2CppArray* outChars, int32_t offsetOut)
{
    return il2cpp_convert_to_base64_char_array_options(bytes, offsetIn, length, outChars, offsetOut, 0);
}

//------------------------------------------------------------------------------
// Core decoder over a char16 range [begin, end).
Il2CppArray* DecodeBase64Core(const char16_t* begin, const char16_t* end)
{
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
    return DecodeBase64Core(begin, end);
}

Il2CppArray* il2cpp_convert_from_base64_char_array(Il2CppArray* chars, int32_t offset, int32_t length)
{
    if (chars == nullptr)
    {
        il2cpp_throw_argument_null_exception("inArray");
    }
    ValidateRange(chars, offset, length, "offset", "length");
    const char16_t* base = reinterpret_cast<const char16_t*>(il2cpp_array_get_elem_ptr(chars, 0));
    return DecodeBase64Core(base + offset, base + offset + length);
}


}} // namespace il2cpp::runtime
