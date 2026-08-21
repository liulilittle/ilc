//------------------------------------------------------------------------------
// deflate.cpp -- System.IO.Compression.DeflateStream runtime support (see
// il2cpp_deflate.h).
//
// DEFLATE (RFC 1951) subset: stored blocks (BTYPE=00) + fixed-Huffman blocks
// (BTYPE=01) on the inflate side; stored blocks only on the deflate side.
// The fixed Huffman tables are the RFC 1951 section 3.2.6 tables (built into
// lookup tables at first use).
//------------------------------------------------------------------------------

#include "il2cpp_deflate.h"

#include "il2cpp_array.h"       // il2cpp_array_new / il2cpp_array_get_elem_ptr
#include "il2cpp_exception.h"     // il2cpp_throw_new
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_runtime.h"       // il2cpp_class_register
#include "il2cpp_streamio.h"      // icall_stream_read_bytes / write_bytes

#include <cstdint>
#include <cstring>
#include <vector>

//------------------------------------------------------------------------------
// Class descriptor (GLOBAL scope -- the transpiler emits
// &System_IO_Compression_DeflateStream_class for newobj/isinst/castclass).
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_IO_Compression_DeflateStream_class = {
    "System.IO.Compression.DeflateStream",             // name
    static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppDeflateStreamObject)), // instanceSize
    0u,                                                // refFieldBitmap: no reference fields
    0,                                                 // refFieldCount
    &System_Object_class,                              // baseClass
    nullptr, 0, nullptr, 0,                            // vtable/interfaces
};

namespace il2cpp { namespace runtime {

struct DeflateStreamState
{
    Il2CppObject* stream;     // the underlying managed stream object
    int32_t mode;             // 0 = Compress, 1 = Decompress
    std::vector<uint8_t> buffer; // Compress: buffered input; Decompress: inflate buffer
    size_t bufferPos;         // Decompress: read position in the inflate buffer
    bool closed;
};

namespace {

bool g_deflateClassesRegistered = false;

//------------------------------------------------------------------------------
// Fixed Huffman tables (RFC 1951 section 3.2.6).
//------------------------------------------------------------------------------

// The literal/length table: 512 entries (9-bit index). Each entry packs
// (symbol << 4) | codeLength; -1 marks an invalid code.
int16_t g_fixedLitTable[512];
// The distance table: 32 entries (5-bit index).
int16_t g_fixedDistTable[32];
bool g_tablesBuilt = false;

// Fill the table entries for a canonical code: every 9-bit value whose top
// 'length' bits equal 'code' maps to the symbol.
void FillCode(int16_t* table, int tableBits, uint16_t code, uint8_t length,
              int16_t symbol)
{
    const int shift = tableBits - length;
    const int count = 1 << shift;
    const int base = static_cast<int>(code) << shift;
    const int16_t packed = static_cast<int16_t>((symbol << 4) | length);
    for (int i = 0; i < count; ++i)
    {
        table[base + i] = packed;
    }
}

void BuildFixedTables()
{
    if (g_tablesBuilt)
    {
        return;
    }
    for (int i = 0; i < 512; ++i)
    {
        g_fixedLitTable[i] = -1;
    }
    for (int i = 0; i < 32; ++i)
    {
        g_fixedDistTable[i] = -1;
    }
    // Literal/length codes (RFC 1951 3.2.6):
    //   0-143:   8 bits, codes 0x30-0xBF
    //   144-255: 9 bits, codes 0x190-0x1FF
    //   256-279: 7 bits, codes 0x00-0x17
    //   280-287: 8 bits, codes 0xC0-0xC7
    for (int sym = 0; sym <= 143; ++sym)
    {
        FillCode(g_fixedLitTable, 9, static_cast<uint16_t>(0x30 + sym), 8, static_cast<int16_t>(sym));
    }
    for (int sym = 144; sym <= 255; ++sym)
    {
        FillCode(g_fixedLitTable, 9, static_cast<uint16_t>(0x190 + (sym - 144)), 9, static_cast<int16_t>(sym));
    }
    for (int sym = 256; sym <= 279; ++sym)
    {
        FillCode(g_fixedLitTable, 9, static_cast<uint16_t>(sym - 256), 7, static_cast<int16_t>(sym));
    }
    for (int sym = 280; sym <= 287; ++sym)
    {
        FillCode(g_fixedLitTable, 9, static_cast<uint16_t>(0xC0 + (sym - 280)), 8, static_cast<int16_t>(sym));
    }
    // Distance codes: 30 codes, 5 bits each (0x00-0x1D).
    for (int sym = 0; sym < 30; ++sym)
    {
        FillCode(g_fixedDistTable, 5, static_cast<uint16_t>(sym), 5, static_cast<int16_t>(sym));
    }
    g_tablesBuilt = true;
}

// Length base / extra bits for codes 257-285 (RFC 1951 3.2.5).
int32_t LengthBase(int32_t code)
{
    if (code <= 264) return code - 254;          // 257-264: 3..10
    if (code <= 268) return 11 + (code - 265) * 2; // 265-268: 11,13,15,17
    if (code <= 272) return 19 + (code - 269) * 4; // 269-272: 19,23,27,31
    if (code <= 276) return 35 + (code - 273) * 8; // 273-276: 35,43,51,59
    if (code <= 280) return 67 + (code - 277) * 16; // 277-280: 67,83,99,115
    if (code <= 284) return 131 + (code - 281) * 32; // 281-284: 131,163,195,227
    return 258;                                    // 285
}

int32_t LengthExtraBits(int32_t code)
{
    if (code <= 264) return 0;
    if (code <= 268) return 1;
    if (code <= 272) return 2;
    if (code <= 276) return 3;
    if (code <= 280) return 4;
    if (code <= 284) return 5;
    return 0; // 285
}

// Distance base / extra bits for codes 0-29 (RFC 1951 3.2.5).
int32_t DistanceBase(int32_t code)
{
    if (code <= 3) return code + 1;              // 0-3: 1..4
    if (code <= 7) return 5 + (code - 4) * 2;    // 4-7: 5,7,9,11
    if (code <= 11) return 13 + (code - 8) * 4;  // 8-11: 13,17,21,25
    if (code <= 15) return 29 + (code - 12) * 8; // 12-15: 29,37,45,53
    if (code <= 19) return 61 + (code - 16) * 16; // 16-19: 61,77,93,109
    if (code <= 23) return 125 + (code - 20) * 32; // 20-23: 125,157,189,221
    if (code <= 27) return 253 + (code - 24) * 64; // 24-27: 253,317,381,445
    return 509 + (code - 28) * 128;                // 28-29: 509,637
}

int32_t DistanceExtraBits(int32_t code)
{
    if (code <= 3) return 0;
    if (code <= 7) return 1;
    if (code <= 11) return 2;
    if (code <= 15) return 3;
    if (code <= 19) return 4;
    if (code <= 23) return 5;
    if (code <= 27) return 6;
    return 7; // 28-29
}

//------------------------------------------------------------------------------
// Bit reader (LSB first, RFC 1951).
//------------------------------------------------------------------------------
struct BitReader
{
    const uint8_t* data;
    int32_t byteLen;
    int32_t bitPos; // bit position (LSB of byte bitPos/8)

    int32_t ReadBits(int32_t count)
    {
        int32_t value = 0;
        for (int32_t i = 0; i < count; ++i)
        {
            const int32_t byteIdx = bitPos >> 3;
            if (byteIdx >= byteLen)
            {
                return -1; // truncated
            }
            const int32_t bit = (data[byteIdx] >> (bitPos & 7)) & 1;
            value |= bit << i;
            ++bitPos;
        }
        return value;
    }

    // Align to the next byte boundary (for stored blocks).
    void AlignToByte()
    {
        bitPos = (bitPos + 7) & ~7;
    }
};

//------------------------------------------------------------------------------
// Inflate: decode a stored or fixed-Huffman block stream into 'out'.
// Returns the bytes written, or -1 on malformed input.
//------------------------------------------------------------------------------
int32_t Inflate(const uint8_t* src, int32_t srcLen, uint8_t* out, int32_t outCapacity)
{
    BuildFixedTables();
    BitReader br{ src, srcLen, 0 };
    std::vector<uint8_t> result;
    result.reserve(static_cast<size_t>(outCapacity > 0 ? outCapacity : 64));

    for (;;)
    {
        const int32_t bfinal = br.ReadBits(1);
        const int32_t btype = br.ReadBits(2);
        if (bfinal < 0 || btype < 0)
        {
            return -1;
        }
        if (btype == 0) // stored block
        {
            br.AlignToByte();
            const int32_t len = br.ReadBits(16);
            const int32_t nlen = br.ReadBits(16);
            if (len < 0 || nlen < 0 || (len ^ 0xFFFF) != nlen)
            {
                return -1;
            }
            for (int32_t i = 0; i < len; ++i)
            {
                const int32_t b = br.ReadBits(8);
                if (b < 0)
                {
                    return -1;
                }
                result.push_back(static_cast<uint8_t>(b));
            }
        }
        else if (btype == 1) // fixed Huffman block
        {
            for (;;)
            {
                // Read the first 9 bits MSB-first (the Huffman code is packed
                // MSB first -- the first bit read is the code's MSB) and look
                // up the literal/length code.
                int32_t bits = 0;
                for (int32_t i = 0; i < 9; ++i)
                {
                    const int32_t b = br.ReadBits(1);
                    if (b < 0)
                    {
                        return -1;
                    }
                    bits = (bits << 1) | b;
                }
                const int16_t packed = g_fixedLitTable[bits];
                if (packed < 0)
                {
                    return -1;
                }
                const int32_t codeLen = packed & 0x0F;
                const int32_t symbol = packed >> 4;
                // Rewind the extra bits (we read 9, the code is codeLen).
                br.bitPos -= (9 - codeLen);
                if (symbol < 256) // literal
                {
                    result.push_back(static_cast<uint8_t>(symbol));
                }
                else if (symbol == 256) // end of block
                {
                    break;
                }
                else // length code 257-285
                {
                    const int32_t length = LengthBase(symbol) + br.ReadBits(LengthExtraBits(symbol));
                    // Read the distance code (5 bits, MSB-first).
                    int32_t distBits = 0;
                    for (int32_t i = 0; i < 5; ++i)
                    {
                        const int32_t b = br.ReadBits(1);
                        if (b < 0)
                        {
                            return -1;
                        }
                        distBits = (distBits << 1) | b;
                    }
                    const int16_t distPacked = g_fixedDistTable[distBits];
                    if (distPacked < 0)
                    {
                        return -1;
                    }
                    const int32_t distCode = distPacked >> 4;
                    const int32_t distance = DistanceBase(distCode) + br.ReadBits(DistanceExtraBits(distCode));
                    if (distance <= 0 || distance > static_cast<int32_t>(result.size()))
                    {
                        return -1;
                    }
                    for (int32_t i = 0; i < length; ++i)
                    {
                        result.push_back(result[result.size() - static_cast<size_t>(distance)]);
                    }
                }
            }
        }
        else
        {
            return -1; // dynamic Huffman block (out of scope)
        }
        if (bfinal)
        {
            break;
        }
    }

    if (out == nullptr)
    {
        return static_cast<int32_t>(result.size());
    }
    if (static_cast<int32_t>(result.size()) > outCapacity)
    {
        return -1;
    }
    if (!result.empty())
    {
        std::memcpy(out, result.data(), result.size());
    }
    return static_cast<int32_t>(result.size());
}

//------------------------------------------------------------------------------
// Deflate (stored blocks only): emit the stored-block stream.
//------------------------------------------------------------------------------
int32_t Deflate(const uint8_t* src, int32_t srcLen, uint8_t* dst, int32_t dstCapacity)
{
    // Each stored block: 1 header byte + 4 bytes (LEN + NLEN) + up to 65535
    // data bytes. The max block size is 65535; the total output is
    // srcLen + blockCount * 5 (1 header + 4 LEN/NLEN per block). An empty
    // input still emits ONE empty stored block (a valid DEFLATE stream must
    // contain at least one block).
    int32_t blockCount = (srcLen + 65534) / 65535;
    if (blockCount == 0)
    {
        blockCount = 1;
    }
    const int32_t required = srcLen + blockCount * 5;
    if (dst == nullptr)
    {
        return required;
    }
    if (required > dstCapacity)
    {
        return -1;
    }
    int32_t pos = 0;
    int32_t srcPos = 0;
    for (int32_t block = 0; block < blockCount; ++block)
    {
        const int32_t len = (srcLen - srcPos) > 65535 ? 65535 : (srcLen - srcPos);
        const bool isFinal = (block == blockCount - 1);
        // Header byte: BFINAL (bit 0) + BTYPE=00 (bits 1-2).
        dst[pos++] = static_cast<uint8_t>(isFinal ? 1 : 0);
        // LEN (little-endian) + NLEN.
        dst[pos++] = static_cast<uint8_t>(len & 0xFF);
        dst[pos++] = static_cast<uint8_t>((len >> 8) & 0xFF);
        const uint16_t nlen = static_cast<uint16_t>(len ^ 0xFFFF);
        dst[pos++] = static_cast<uint8_t>(nlen & 0xFF);
        dst[pos++] = static_cast<uint8_t>((nlen >> 8) & 0xFF);
        if (len > 0)
        {
            std::memcpy(dst + pos, src + srcPos, static_cast<size_t>(len));
            pos += len;
            srcPos += len;
        }
    }
    return pos;
}

} // namespace

void il2cpp_deflate_register_classes()
{
    if (g_deflateClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_IO_Compression_DeflateStream_class);
    g_deflateClassesRegistered = true;
}

//------------------------------------------------------------------------------
// Raw primitives.
//------------------------------------------------------------------------------
int32_t il2cpp_deflate_compress(const uint8_t* src, int32_t srcLen,
                                uint8_t* dst, int32_t dstCapacity)
{
    if (src == nullptr && srcLen > 0)
    {
        return -1;
    }
    return Deflate(src, srcLen, dst, dstCapacity);
}

int32_t il2cpp_deflate_decompress(const uint8_t* src, int32_t srcLen,
                                  uint8_t* dst, int32_t dstCapacity)
{
    if (src == nullptr && srcLen > 0)
    {
        return -1;
    }
    return Inflate(src, srcLen, dst, dstCapacity);
}

//------------------------------------------------------------------------------
// DeflateStream object icalls.
//------------------------------------------------------------------------------
void icall_deflate_stream_ctor(Il2CppObject* self, Il2CppObject* stream,
                               int32_t mode)
{
    il2cpp_deflate_register_classes();
    if (mode != 0 && mode != 1)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "mode", "The compression mode must be Compress or Decompress.");
    }
    auto* state = new DeflateStreamState();
    state->stream = stream;
    state->mode = mode; // net48 CompressionMode: 0 = Decompress, 1 = Compress
    state->bufferPos = 0;
    state->closed = false;
    reinterpret_cast<Il2CppDeflateStreamObject*>(self)->state = state;
}

void icall_deflate_stream_write(Il2CppObject* self, Il2CppArray* buffer,
                                int32_t offset, int32_t count)
{
    DeflateStreamState* state = reinterpret_cast<Il2CppDeflateStreamObject*>(self)->state;
    if (state == nullptr || state->closed)
    {
        il2cpp_throw_invalid_operation_exception("The DeflateStream is closed.");
    }
    if (state->mode != 1) // Compress only
    {
        il2cpp_throw_invalid_operation_exception("Cannot write to a Decompress-mode DeflateStream.");
    }
    if (buffer == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception", "Value cannot be null.");
    }
    
    const int32_t n = il2cpp_array_get_length(buffer);
    if (offset < 0 || count < 0 || n - offset < count)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Offset and length were out of bounds for the array.");
    }
    const uint8_t* src = reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(buffer, offset));
    state->buffer.insert(state->buffer.end(), src, src + count);
}

int32_t icall_deflate_stream_read(Il2CppObject* self, Il2CppArray* buffer,
                                  int32_t offset, int32_t count)
{
    DeflateStreamState* state = reinterpret_cast<Il2CppDeflateStreamObject*>(self)->state;
    if (state == nullptr || state->closed)
    {
        il2cpp_throw_invalid_operation_exception("The DeflateStream is closed.");
    }
    if (state->mode != 0) // Decompress only
    {
        il2cpp_throw_invalid_operation_exception("Cannot read from a Compress-mode DeflateStream.");
    }
    if (buffer == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception", "Value cannot be null.");
    }
    
    const int32_t n = il2cpp_array_get_length(buffer);
    if (offset < 0 || count < 0 || n - offset < count)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Offset and length were out of bounds for the array.");
    }
    // Lazily inflate the underlying stream on the first read.
    if (state->buffer.empty() && state->bufferPos == 0)
    {
        std::vector<uint8_t> compressed;
        for (;;)
        {
            Il2CppArray* chunkArr = il2cpp_array_new(nullptr, 1, false, 4096);
            const int32_t got = icall_stream_read_bytes(state->stream, chunkArr, 0, 4096);
            if (got <= 0)
            {
                break;
            }
            const uint8_t* p = reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(chunkArr, 0));
            compressed.insert(compressed.end(), p, p + got);
        }
        std::vector<uint8_t> inflated;
        // Query the required size, then inflate.
        const int32_t required = Inflate(compressed.data(),
                                         static_cast<int32_t>(compressed.size()),
                                         nullptr, 0);
        if (required < 0)
        {
            il2cpp_throw_new("System.IO.InvalidDataException,System.Exception",
                             "The compressed data is corrupt.");
        }
        inflated.resize(static_cast<size_t>(required));
        if (required > 0)
        {
            Inflate(compressed.data(), static_cast<int32_t>(compressed.size()),
                    inflated.data(), required);
        }
        state->buffer = std::move(inflated);
        state->bufferPos = 0;
    }
    const size_t available = state->buffer.size() - state->bufferPos;
    const size_t toCopy = available < static_cast<size_t>(count) ? available : static_cast<size_t>(count);
    if (toCopy > 0)
    {
        uint8_t* dst = reinterpret_cast<uint8_t*>(il2cpp_array_get_elem_ptr(buffer, offset));
        std::memcpy(dst, state->buffer.data() + state->bufferPos, toCopy);
        state->bufferPos += toCopy;
    }
    return static_cast<int32_t>(toCopy);
}

void icall_deflate_stream_close(Il2CppObject* self)
{
    DeflateStreamState* state = reinterpret_cast<Il2CppDeflateStreamObject*>(self)->state;
    if (state == nullptr || state->closed)
    {
        return;
    }
    if (state->mode == 1 && !state->buffer.empty()) // Compress
    {
        // Compress mode: write the stored-block DEFLATE stream to the
        // underlying stream.
        const int32_t required = Deflate(state->buffer.data(),
                                         static_cast<int32_t>(state->buffer.size()),
                                         nullptr, 0);
        std::vector<uint8_t> out(static_cast<size_t>(required));
        Deflate(state->buffer.data(), static_cast<int32_t>(state->buffer.size()),
                out.data(), required);
        Il2CppArray* outArr = il2cpp_array_new(nullptr, 1, false, required);
        std::memcpy(il2cpp_array_get_elem_ptr(outArr, 0), out.data(),
                    static_cast<size_t>(required));
        icall_stream_write_bytes(state->stream, outArr, 0, required);
    }
    state->closed = true;
}

}} // namespace il2cpp::runtime
