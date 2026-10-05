//------------------------------------------------------------------------------
// binaryio.cpp -- System.IO.BinaryReader / BinaryWriter byte-stream icall
// implementations (see binaryio.h for the contract and conventions).
//
// Protocol (the .NET BinaryReader/BinaryWriter binary format):
//   * Fixed-width values are little-endian; floating-point types are the
//     IEEE-754 bit patterns (binary64 / binary32), transmitted as plain
//     little-endian integers of the same width. bool is exactly 1 byte.
//   * Strings are a 7-bit-encoded UTF-8 byte length prefix followed by the
//     UTF-8 content -- the length counts BYTES of the UTF-8 encoding, not
//     UTF-16 units (matching BinaryWriter.Write(string) on .NET
//     Framework 4.8 and .NET Core alike). The 7-bit encoding writes 7 bits
//     per byte, low group first, the high bit marking a continuation byte;
//     an int32 length needs at most 5 bytes.
//
// Portability: byte order is produced by explicit shifts (little-endian on
// any host), floats bit-copy through an unsigned integer with memcpy, and
// everything else is the C++ standard library -- the same sources compile
// under MSVC and MinGW.
//
// Failure handling (M3: no managed exceptions -- failures are return
// values, never C++ throws): reads verify the full extent is available
// BEFORE consuming anything (atomic: a failed read neither advances the
// position nor partially consumes), return the zero value and latch the
// reader's failed flag. The flag is sticky -- a failed reader stays failed.
//------------------------------------------------------------------------------

#include "binaryio.h"

#include "il2cpp_string.h"

#include <cstring>
#include <string>
#include <vector>

namespace il2cpp { namespace runtime {

struct Il2CppBinaryWriter
{
    std::vector<uint8_t> bytes;
};

struct Il2CppBinaryReader
{
    std::vector<uint8_t> bytes;
    size_t pos;    // next byte to read (0..bytes.size())
    bool failed;   // latched by the first failed read
};

namespace {

//------------------------------------------------------------------------------
// Little-endian append helpers (explicit shifts: the emitted layout is
// little-endian regardless of the host byte order).
//------------------------------------------------------------------------------

void AppendLE32(std::vector<uint8_t>& bytes, uint32_t value)
{
    bytes.push_back(static_cast<uint8_t>(value & 0xFFu));
    bytes.push_back(static_cast<uint8_t>((value >> 8) & 0xFFu));
    bytes.push_back(static_cast<uint8_t>((value >> 16) & 0xFFu));
    bytes.push_back(static_cast<uint8_t>((value >> 24) & 0xFFu));
}

void AppendLE64(std::vector<uint8_t>& bytes, uint64_t value)
{
    AppendLE32(bytes, static_cast<uint32_t>(value & 0xFFFFFFFFu));
    AppendLE32(bytes, static_cast<uint32_t>(value >> 32));
}

// The BinaryWriter 7-bit integer encoding: 7 bits per byte, low group
// first, the high bit marking a continuation byte.
void Append7BitLen(std::vector<uint8_t>& bytes, uint32_t value)
{
    while (value >= 0x80u)
    {
        bytes.push_back(static_cast<uint8_t>((value & 0x7Fu) | 0x80u));
        value >>= 7;
    }
    bytes.push_back(static_cast<uint8_t>(value));
}

//------------------------------------------------------------------------------
// UTF-8 <-> UTF-16 transcoding. Module-local copy: the io.cpp transcoders
// live in that translation unit's anonymous namespace and are not shared.
//------------------------------------------------------------------------------

// Is 'c' a UTF-8 continuation byte (10xxxxxx)?
bool IsContinuationByte(unsigned char c)
{
    return (c & 0xC0) == 0x80;
}

// Decode one UTF-8 sequence at data[i] into 'units'. Returns the number of
// input bytes consumed; invalid sequences append exactly one U+FFFD.
//
// net48 replacement-fallback semantics (the codec string.cpp uses -- kept
// module-local per the translation-unit isolation convention; empirically
// verified against a net48 reference run):
//   * lead + out-of-range continuation   -> ONE U+FFFD, consumes 2 bytes
//   * lead + non-continuation byte       -> ONE U+FFFD, consumes 1 byte
//   * truncated at end of input          -> ONE U+FFFD for the whole sequence
//   * every other invalid byte           -> ONE U+FFFD per byte
// Valid leads: C2-DF (2 bytes), E0-EF (3 bytes), F0-F4 (4 bytes). The lead
// constrains its first continuation: E0 needs 0xA0-0xBF, ED 0x80-0x9F
// (excludes the surrogate range), F0 0x90-0xBF, F4 0x80-0x8F (caps at
// U+10FFFF); C0/C1 and F5-FF are never valid leads, a lone continuation
// byte is invalid.
size_t DecodeUtf8Sequence(const char* data, size_t length, size_t i,
                          std::vector<char16_t>& units)
{
    const unsigned char c = static_cast<unsigned char>(data[i]);
    if (c < 0x80)
    {
        units.push_back(static_cast<char16_t>(c));
        return 1;
    }

    size_t seqLen = 0; // expected sequence length from the lead byte
    if (c >= 0xC2 && c <= 0xDF)
    {
        seqLen = 2;
    }
    else if (c >= 0xE0 && c <= 0xEF)
    {
        seqLen = 3;
    }
    else if (c >= 0xF0 && c <= 0xF4)
    {
        seqLen = 4;
    }
    if (seqLen == 0)
    {
        units.push_back(0xFFFD); // lone continuation / invalid lead (C0,C1,F5-FF)
        return 1;
    }

    const unsigned char b1 = (i + 1 < length)
        ? static_cast<unsigned char>(data[i + 1]) : 0x00;
    if (i + 1 >= length || !IsContinuationByte(b1))
    {
        units.push_back(0xFFFD); // lead truncated or followed by a non-continuation
        return 1;
    }
    if ((c == 0xE0 && b1 < 0xA0) || (c == 0xED && b1 > 0x9F)
        || (c == 0xF0 && b1 < 0x90) || (c == 0xF4 && b1 > 0x8F))
    {
        units.push_back(0xFFFD); // overlong / surrogate / out-of-range second byte
        return 2;
    }

    size_t consumed = 2; // lead + b1 are valid so far
    for (size_t k = 2; k < seqLen; ++k)
    {
        if (i + k >= length
            || !IsContinuationByte(static_cast<unsigned char>(data[i + k])))
        {
            units.push_back(0xFFFD); // truncated / invalid continuation
            return consumed;
        }
        ++consumed;
    }

    if (seqLen == 2)
    {
        units.push_back(static_cast<char16_t>(
            ((c & 0x1Fu) << 6) | (b1 & 0x3Fu)));
        return 2;
    }
    const unsigned char b2 = static_cast<unsigned char>(data[i + 2]);
    if (seqLen == 3)
    {
        units.push_back(static_cast<char16_t>(
            ((c & 0x0Fu) << 12) | ((b1 & 0x3Fu) << 6) | (b2 & 0x3Fu)));
        return 3;
    }
    const unsigned char b3 = static_cast<unsigned char>(data[i + 3]);
    const uint32_t cp = (static_cast<uint32_t>(c & 0x07u) << 18)
        | (static_cast<uint32_t>(b1 & 0x3Fu) << 12)
        | (static_cast<uint32_t>(b2 & 0x3Fu) << 6)
        | static_cast<uint32_t>(b3 & 0x3Fu);
    const uint32_t v = cp - 0x10000u;
    units.push_back(static_cast<char16_t>(0xD800u + (v >> 10)));
    units.push_back(static_cast<char16_t>(0xDC00u + (v & 0x3FFu)));
    return 4;
}

// UTF-8 bytes (explicit length; embedded NULs are content) -> managed
// string. Decodes with net48 replacement-fallback semantics
// (DecodeUtf8Sequence above): valid BMP sequences decode to single units,
// astral sequences to surrogate pairs, invalid sequences to one or more
// U+FFFD exactly as the net48 reference produces. Never returns null.
Il2CppObject* Utf8ToCs(const char* data, size_t length)
{
    if (length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    std::vector<char16_t> units;
    units.reserve(length);
    size_t i = 0;
    while (i < length)
    {
        i += DecodeUtf8Sequence(data, length, i, units);
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(units.data(), static_cast<int32_t>(units.size())));
}

// Managed string -> UTF-8 bytes. Surrogate pairs combine into 4-byte
// sequences; lone surrogates encode as the .NET replacement character
// U+FFFD (EF BF BD), matching Encoding.UTF8.GetBytes on net48.
std::string CsToUtf8(Il2CppObject* obj)
{
    std::string out;
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(obj);
    if (s == nullptr)
    {
        return out;
    }
    out.reserve(static_cast<size_t>(s->length));
    for (int32_t i = 0; i < s->length; ++i)
    {
        const char16_t c = s->chars[i];
        if (c < 0x80)
        {
            out.push_back(static_cast<char>(c));
        }
        else if (c < 0x800)
        {
            out.push_back(static_cast<char>(0xC0 | (c >> 6)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
        else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s->length
                 && s->chars[i + 1] >= 0xDC00 && s->chars[i + 1] <= 0xDFFF)
        {
            // High surrogate followed by a low surrogate: astral code point.
            const uint32_t cp = 0x10000u
                + (static_cast<uint32_t>(c - 0xD800) << 10)
                + static_cast<uint32_t>(s->chars[i + 1] - 0xDC00);
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            ++i; // consume the low surrogate
        }
        else if (c >= 0xD800 && c <= 0xDFFF)
        {
            // Lone surrogate (no partner): .NET's UTF-8 encoder replaces it
            // with U+FFFD -> EF BF BD.
            out.push_back(static_cast<char>(0xEF));
            out.push_back(static_cast<char>(0xBF));
            out.push_back(static_cast<char>(0xBD));
        }
        else
        {
            out.push_back(static_cast<char>(0xE0 | (c >> 12)));
            out.push_back(static_cast<char>(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// Reader primitives. Every read verifies the full extent is available
// BEFORE consuming anything, so a failed read leaves the position untouched
// (atomic verify-then-mutate; the failed flag latches instead).
//------------------------------------------------------------------------------

// Consume exactly 'count' bytes into 'out'. False + latched failed flag
// (position unchanged) when the reader already failed or fewer remain.
bool TakeBytes(Il2CppBinaryReader* reader, size_t count, uint8_t* out)
{
    if (reader->failed || count > reader->bytes.size() - reader->pos)
    {
        reader->failed = true;
        return false;
    }
    std::memcpy(out, reader->bytes.data() + reader->pos, count);
    reader->pos += count;
    return true;
}

// Little-endian 4-byte read; 0 + failed flag when truncated.
uint32_t TakeLE32(Il2CppBinaryReader* reader)
{
    uint8_t b[4];
    if (!TakeBytes(reader, sizeof(b), b))
    {
        return 0;
    }
    return static_cast<uint32_t>(b[0])
        | (static_cast<uint32_t>(b[1]) << 8)
        | (static_cast<uint32_t>(b[2]) << 16)
        | (static_cast<uint32_t>(b[3]) << 24);
}

// Little-endian 8-byte read; 0 + failed flag when truncated.
uint64_t TakeLE64(Il2CppBinaryReader* reader)
{
    uint8_t b[8];
    if (!TakeBytes(reader, sizeof(b), b))
    {
        return 0;
    }
    uint64_t v = 0;
    for (size_t i = 0; i < 8; ++i)
    {
        v |= static_cast<uint64_t>(b[i]) << (8 * i);
    }
    return v;
}

// 7-bit-encoded int32 length (at most 5 bytes; a 6th continuation byte is
// malformed -> false + latched failed flag).
bool Take7BitLen(Il2CppBinaryReader* reader, uint32_t& out)
{
    uint32_t value = 0;
    for (size_t shift = 0; shift <= 28; shift += 7)
    {
        uint8_t b = 0;
        if (!TakeBytes(reader, 1, &b))
        {
            return false;
        }
        value |= static_cast<uint32_t>(b & 0x7Fu) << shift;
        if ((b & 0x80u) == 0)
        {
            out = value;
            return true;
        }
    }
    reader->failed = true; // length exceeds the int32 range -> malformed
    return false;
}

} // namespace

//------------------------------------------------------------------------------
// System.IO.BinaryWriter.
//------------------------------------------------------------------------------

Il2CppBinaryWriter* il2cpp_binary_writer_create()
{
    return new Il2CppBinaryWriter();
}

void il2cpp_binary_writer_destroy(Il2CppBinaryWriter* writer)
{
    delete writer;
}

Il2CppArray* il2cpp_binary_writer_get_bytes(Il2CppBinaryWriter* writer)
{
    if (writer == nullptr)
    {
        return nullptr;
    }
    Il2CppArray* arr = il2cpp_array_new(
        nullptr, 1, false, static_cast<int32_t>(writer->bytes.size()));
    if (!writer->bytes.empty())
    {
        std::memcpy(arr->data, writer->bytes.data(), writer->bytes.size());
    }
    return arr;
}

void il2cpp_binary_write_int32(Il2CppBinaryWriter* writer, int32_t value)
{
    AppendLE32(writer->bytes, static_cast<uint32_t>(value));
}

void il2cpp_binary_write_int64(Il2CppBinaryWriter* writer, int64_t value)
{
    AppendLE64(writer->bytes, static_cast<uint64_t>(value));
}

void il2cpp_binary_write_uint32(Il2CppBinaryWriter* writer, uint32_t value)
{
    AppendLE32(writer->bytes, value);
}

void il2cpp_binary_write_double(Il2CppBinaryWriter* writer, double value)
{
    uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    AppendLE64(writer->bytes, bits);
}

void il2cpp_binary_write_single(Il2CppBinaryWriter* writer, float value)
{
    uint32_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    AppendLE32(writer->bytes, bits);
}

void il2cpp_binary_write_bool(Il2CppBinaryWriter* writer, int32_t value)
{
    writer->bytes.push_back(value != 0 ? 0x01 : 0x00);
}

void il2cpp_binary_write_string(Il2CppBinaryWriter* writer, Il2CppObject* value)
{
    if (value == nullptr)
    {
        Append7BitLen(writer->bytes, 0); // null writes as the empty string
        return;
    }
    const std::string utf8 = CsToUtf8(value);
    Append7BitLen(writer->bytes, static_cast<uint32_t>(utf8.size()));
    writer->bytes.insert(writer->bytes.end(), utf8.begin(), utf8.end());
}

//------------------------------------------------------------------------------
// System.IO.BinaryReader.
//------------------------------------------------------------------------------

Il2CppBinaryReader* il2cpp_binary_reader_create(const uint8_t* data, size_t length)
{
    Il2CppBinaryReader* reader = new Il2CppBinaryReader();
    reader->pos = 0;
    reader->failed = false;
    if (length != 0)
    {
        reader->bytes.assign(data, data + length);
    }
    return reader;
}

void il2cpp_binary_reader_destroy(Il2CppBinaryReader* reader)
{
    delete reader;
}

int32_t il2cpp_binary_read_int32(Il2CppBinaryReader* reader)
{
    return static_cast<int32_t>(TakeLE32(reader));
}

int64_t il2cpp_binary_read_int64(Il2CppBinaryReader* reader)
{
    return static_cast<int64_t>(TakeLE64(reader));
}

uint32_t il2cpp_binary_read_uint32(Il2CppBinaryReader* reader)
{
    return TakeLE32(reader);
}

double il2cpp_binary_read_double(Il2CppBinaryReader* reader)
{
    const uint64_t bits = TakeLE64(reader);
    double value = 0.0;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

float il2cpp_binary_read_single(Il2CppBinaryReader* reader)
{
    const uint32_t bits = TakeLE32(reader);
    float value = 0.0f;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

int32_t il2cpp_binary_read_bool(Il2CppBinaryReader* reader)
{
    uint8_t b = 0;
    if (!TakeBytes(reader, 1, &b))
    {
        return 0;
    }
    return b != 0 ? 1 : 0;
}

Il2CppObject* il2cpp_binary_read_string(Il2CppBinaryReader* reader)
{
    // Atomicity (the module's failure convention): a failed read must NOT
    // advance the position, even when the 7-bit length prefix was partially
    // consumed before the failure was detected. Snapshot the start so every
    // failure path restores it.
    const size_t start = reader->pos;
    uint32_t len = 0;
    if (!Take7BitLen(reader, len))
    {
        reader->pos = start; // malformed prefix (no terminator / overflows int32)
        return nullptr;
    }
    if (len == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    // Preflight BEFORE allocating: a malformed length prefix (up to ~4 GiB in
    // 5 bytes) must fail with null + a latched flag -- never a multi-gigabyte
    // allocation or a C++ throw (this module's failure convention is return
    // values). The extent checked is the whole remaining buffer, i.e. exactly
    // the bytes TakeBytes would need.
    if (reader->failed || static_cast<size_t>(len) > reader->bytes.size() - reader->pos)
    {
        reader->failed = true;
        reader->pos = start; // length prefix's content extent not available
        return nullptr;
    }
    std::vector<uint8_t> utf8(static_cast<size_t>(len));
    if (!TakeBytes(reader, utf8.size(), utf8.data()))
    {
        reader->pos = start; // unreachable given the preflight; kept as a guard
        return nullptr;
    }
    return Utf8ToCs(reinterpret_cast<const char*>(utf8.data()), utf8.size());
}

int32_t il2cpp_binary_seek(Il2CppBinaryReader* reader, int64_t offset, int32_t origin)
{
    int64_t base = 0;
    if (origin == 0)
    {
        base = 0;
    }
    else if (origin == 1)
    {
        base = static_cast<int64_t>(reader->pos);
    }
    else if (origin == 2)
    {
        base = static_cast<int64_t>(reader->bytes.size());
    }
    else
    {
        return -1; // unknown origin
    }
    const int64_t target = base + offset;
    if (target < 0 || target > static_cast<int64_t>(reader->bytes.size()))
    {
        return -1; // outside [0, length]
    }
    reader->pos = static_cast<size_t>(target);
    return static_cast<int32_t>(reader->pos);
}

int32_t il2cpp_binary_reader_position(Il2CppBinaryReader* reader)
{
    return static_cast<int32_t>(reader->pos);
}

int32_t il2cpp_binary_reader_eof(Il2CppBinaryReader* reader)
{
    return reader->failed ? 1 : 0;
}

}} // namespace il2cpp::runtime
