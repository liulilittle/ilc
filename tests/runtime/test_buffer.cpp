//------------------------------------------------------------------------------
// test_buffer.cpp -- System.Buffer (BlockCopy / ByteLength / MemoryCopy) tests.
//
// Coverage (normal + edge + error paths):
//   1. ByteLength -- byte array, int32 array, empty array, reference array
//      (ArgumentException, net48 rejects non-primitive arrays), null array
//      (ArgumentNullException), non-array object (ArgumentException).
//   2. BlockCopy byte[] -> byte[] -- basic, partial, zero-length no-op,
//      boundary (srcOffset == byte length with count 0), same-array overlap
//      in BOTH directions (memmove semantics).
//   3. BlockCopy across element kinds -- int32[] -> byte[] (the .NET byte
//      view of an int32 payload, little-endian), byte[] -> int32[].
//   4. BlockCopy error paths -- null src/dst (ArgumentNullException),
//      negative offset/count (ArgumentOutOfRangeException), range overflow
//      (ArgumentException), reference-element array (ArgumentException).
//   5. MemoryCopy (raw form) -- basic copy, count 0, forward overlap, backward
//      overlap (memmove semantics on a raw buffer).
//   6. MemoryCopy (net48 sized form) -- basic copy, partial copy, count 0,
//      sourceBytesToCopy > destinationSizeInBytes (ArgumentOutOfRangeException),
//      negative sourceBytesToCopy (OverflowException), forward overlap,
//      backward overlap.
//
// Byte-order assumption: the int32 byte views below are little-endian,
// which holds on the x86/x64 MinGW baseline (static_assert guards pointers).
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "buffer.h"
#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "buffer test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType' (see test_arrayext.cpp for the convention).
template <typename F>
void ExpectThrows(const char* expectedType, const char* what, F&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        const bool headMatches = std::strncmp(thrown, expectedType, len) == 0 &&
                                 (thrown[len] == ',' || thrown[len] == '\0');
        if (headMatches)
        {
            return;
        }
        std::printf("FAIL: %s (threw %s, expected %s)\n", what, thrown, expectedType);
        ++g_failures;
        return;
    }
    std::printf("FAIL: %s (no exception thrown, expected %s)\n", what, expectedType);
    ++g_failures;
}

// Element fixture class for the non-array-object and reference-array cases.
il2cpp::Il2CppClass g_mockElemClass = {
    "Test.BufferElem", // name
    24,                // instanceSize
    0b00000010u,       // refFieldBitmap: slot 1 is a GC reference
    1,                 // refFieldCount
    nullptr,           // baseClass
    nullptr,           // vtable
    0,                 // vtableSize
    nullptr,           // interfaces
    0,                 // interfaceCount
};

// Managed exception type names (exact chain head, as thrown by buffer.cpp).
const char kArgNullType[]       = "System.ArgumentNullException";
const char kArgOutOfRangeType[] = "System.ArgumentOutOfRangeException";
const char kArgumentType[]      = "System.ArgumentException";
const char kOverflowType[]      = "System.OverflowException";

il2cpp::Il2CppObject* AsObject(il2cpp::Il2CppArray* arr)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(arr);
}

il2cpp::Il2CppArray* NewByteArray(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 1, false, length);
}

il2cpp::Il2CppArray* NewInt32Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 4, false, length);
}

void FillBytes(il2cpp::Il2CppArray* arr, const uint8_t* values, int32_t count)
{
    uint8_t* data = reinterpret_cast<uint8_t*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        data[i] = values[i];
    }
}

void ExpectByteContent(il2cpp::Il2CppArray* arr, const uint8_t* expected,
                       int32_t count, const char* what)
{
    const uint8_t* data = reinterpret_cast<const uint8_t*>(arr->data);
    bool ok = true;
    for (int32_t i = 0; i < count; ++i)
    {
        if (data[i] != expected[i])
        {
            ok = false;
            break;
        }
    }
    if (!ok)
    {
        std::printf("FAIL: %s (content: ", what);
        for (int32_t i = 0; i < count; ++i)
        {
            std::printf("%s%02X", i > 0 ? " " : "", data[i]);
        }
        std::printf(")\n");
        ++g_failures;
    }
}

} // namespace

int main()
{
    // --- 1. ByteLength ---
    {
        il2cpp::Il2CppArray* bytes = NewByteArray(7);
        ExpectI32(il2cpp::runtime::il2cpp_buffer_byte_length(AsObject(bytes)), 7,
                  "ByteLength of a byte[7] is 7");

        il2cpp::Il2CppArray* ints = NewInt32Array(5);
        ExpectI32(il2cpp::runtime::il2cpp_buffer_byte_length(AsObject(ints)), 20,
                  "ByteLength of an int32[5] is 5 * 4 = 20");

        il2cpp::Il2CppArray* empty = NewInt32Array(0);
        ExpectI32(il2cpp::runtime::il2cpp_buffer_byte_length(AsObject(empty)), 0,
                  "ByteLength of an empty array is 0");

        il2cpp::Il2CppArray* refs = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 3);
        ExpectThrows(kArgumentType, "ByteLength of a reference array throws ArgumentException (net48 rejects non-primitive arrays)",
                     [&] { il2cpp::runtime::il2cpp_buffer_byte_length(AsObject(refs)); });

        ExpectThrows(kArgNullType, "ByteLength of null throws ArgumentNullException",
                     [] { il2cpp::runtime::il2cpp_buffer_byte_length(nullptr); });
        il2cpp::Il2CppObject* notAnArray = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectThrows(kArgumentType, "ByteLength of a non-array object throws ArgumentException",
                     [&] { il2cpp::runtime::il2cpp_buffer_byte_length(notAnArray); });
    }

    // --- 2. BlockCopy -- byte[] -> byte[] ---
    {
        il2cpp::Il2CppArray* src = NewByteArray(5);
        const uint8_t srcVals[5] = { 1, 2, 3, 4, 5 };
        FillBytes(src, srcVals, 5);

        // Zero-length copy leaves the destination untouched.
        il2cpp::Il2CppArray* z = NewByteArray(5);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, AsObject(z), 0, 0);
        const uint8_t zeros[5] = { 0, 0, 0, 0, 0 };
        ExpectByteContent(z, zeros, 5, "zero-length block copy leaves the destination untouched");

        // Partial copy: src[1..3] -> dst[0..2].
        il2cpp::Il2CppArray* dst = NewByteArray(5);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 1, AsObject(dst), 0, 3);
        const uint8_t partial[5] = { 2, 3, 4, 0, 0 };
        ExpectByteContent(dst, partial, 5, "partial block copy copies the requested range only");

        // Full copy.
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, AsObject(dst), 0, 5);
        ExpectByteContent(dst, srcVals, 5, "full block copy replicates the source");

        // Boundary: srcOffset == byte length is legal when count == 0.
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(dst), 5, AsObject(dst), 5, 0);
        ExpectByteContent(dst, srcVals, 5, "block copy at the one-past-end offset with count 0 is a no-op");

        // Overlap, forward direction (memmove semantics: no clobbering).
        il2cpp::Il2CppArray* ov = NewByteArray(5);
        FillBytes(ov, srcVals, 5);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(ov), 0, AsObject(ov), 1, 4);
        const uint8_t shifted[5] = { 1, 1, 2, 3, 4 };
        ExpectByteContent(ov, shifted, 5, "overlapping block copy (forward) behaves like memmove");

        // Overlap, backward direction.
        il2cpp::Il2CppArray* ov2 = NewByteArray(5);
        FillBytes(ov2, srcVals, 5);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(ov2), 1, AsObject(ov2), 0, 4);
        const uint8_t shifted2[5] = { 2, 3, 4, 5, 5 };
        ExpectByteContent(ov2, shifted2, 5, "overlapping block copy (backward) behaves like memmove");
    }

    // --- 3. BlockCopy -- across element kinds (byte view) ---
    {
        // int32[] -> byte[]: the destination sees the raw little-endian
        // payload bytes of the source int32 elements.
        il2cpp::Il2CppArray* ints = NewInt32Array(3);
        const int32_t intVals[3] = { 1, 0x01020304, -2 };
        int32_t* idata = reinterpret_cast<int32_t*>(ints->data);
        idata[0] = intVals[0];
        idata[1] = intVals[1];
        idata[2] = intVals[2];

        il2cpp::Il2CppArray* bytes = NewByteArray(12);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(ints), 0, AsObject(bytes), 0, 12);
        // {1, 0x01020304, -2} little-endian: 01 00 00 00 04 03 02 01 FE FF FF FF
        const uint8_t expected[12] = { 0x01, 0x00, 0x00, 0x00,
                                       0x04, 0x03, 0x02, 0x01,
                                       0xFE, 0xFF, 0xFF, 0xFF };
        ExpectByteContent(bytes, expected, 12, "int32[] -> byte[] exposes the little-endian payload bytes");

        // Partial byte view: int32[1] payload starts at byte offset 4.
        il2cpp::Il2CppArray* tail = NewByteArray(4);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(ints), 4, AsObject(tail), 0, 4);
        const uint8_t tailExpected[4] = { 0x04, 0x03, 0x02, 0x01 };
        ExpectByteContent(tail, tailExpected, 4, "byte offset 4 of an int32[] is element 1's payload");

        // byte[] -> int32[]: reassemble an int32 from its payload bytes.
        il2cpp::Il2CppArray* back = NewInt32Array(2);
        il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(bytes), 4, AsObject(back), 0, 4);
        const int32_t* bdata = reinterpret_cast<const int32_t*>(back->data);
        ExpectI32(bdata[0], 0x01020304, "byte[] -> int32[] reassembles the int32 from its payload bytes");
    }

    // --- 4. BlockCopy -- error paths ---
    {
        il2cpp::Il2CppArray* src = NewByteArray(3);
        il2cpp::Il2CppArray* dst = NewByteArray(3);
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, true, 2);

        ExpectThrows(kArgNullType, "block copy with null src throws ArgumentNullException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(nullptr, 0, AsObject(dst), 0, 1); });
        ExpectThrows(kArgNullType, "block copy with null dst throws ArgumentNullException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, nullptr, 0, 1); });
        ExpectThrows(kArgOutOfRangeType, "negative srcOffset throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), -1, AsObject(dst), 0, 1); });
        ExpectThrows(kArgOutOfRangeType, "negative dstOffset throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, AsObject(dst), -1, 1); });
        ExpectThrows(kArgOutOfRangeType, "negative count throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, AsObject(dst), 0, -1); });

        // 3-byte arrays: offset 2 + count 2 crosses the 3-byte end.
        ExpectThrows(kArgumentType, "srcOffset + count beyond the source throws ArgumentException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 2, AsObject(dst), 0, 2); });
        ExpectThrows(kArgumentType, "dstOffset + count beyond the destination throws ArgumentException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, AsObject(dst), 2, 2); });
        ExpectThrows(kArgumentType, "offset beyond the source throws ArgumentException even with count 0",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 4, AsObject(dst), 0, 0); });

        ExpectThrows(kArgumentType, "reference src array throws ArgumentException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(refArr), 0, AsObject(dst), 0, 1); });
        ExpectThrows(kArgumentType, "reference dst array throws ArgumentException",
                     [&] { il2cpp::runtime::il2cpp_buffer_block_copy(AsObject(src), 0, AsObject(refArr), 0, 1); });
    }

    // --- 5. MemoryCopy (raw pointer form) ---
    {
        const char src[6] = { 'a', 'b', 'c', 'd', 'e', 'f' };

        // Basic copy.
        char dst[6] = {};
        il2cpp::runtime::il2cpp_buffer_memory_copy(dst, src, 6);
        Expect(std::memcmp(dst, src, 6) == 0, "memory copy replicates the source buffer");

        // Zero-length copy is a no-op.
        char z[2] = { 'x', 'y' };
        il2cpp::runtime::il2cpp_buffer_memory_copy(z, src, 0);
        Expect(z[0] == 'x' && z[1] == 'y', "zero-length memory copy leaves the destination untouched");

        // Forward overlap: memmove semantics, no clobbering.
        char fwd[6];
        std::memcpy(fwd, src, 6);
        il2cpp::runtime::il2cpp_buffer_memory_copy(fwd + 1, fwd, 5);
        Expect(std::memcmp(fwd, "aabcde", 6) == 0,
               "forward-overlapping memory copy behaves like memmove");

        // Backward overlap: "abcdef" -> move [1..5] onto [0..4] yields
        // "bcdeff" (b c d e f + the untouched f).
        char bwd[6];
        std::memcpy(bwd, src, 6);
        il2cpp::runtime::il2cpp_buffer_memory_copy(bwd, bwd + 1, 5);
        Expect(std::memcmp(bwd, "bcdeff", 6) == 0,
               "backward-overlapping memory copy behaves like memmove");
    }

    // --- 6. MemoryCopy (net48 sized form) ---
    {
        const char src[6] = { 'a', 'b', 'c', 'd', 'e', 'f' };

        // Basic copy: destination buffer exactly large enough.
        char dst[6] = {};
        il2cpp::runtime::il2cpp_buffer_memory_copy_sized(dst, src, 6, 6);
        Expect(std::memcmp(dst, src, 6) == 0,
               "sized memory copy replicates the source buffer");

        // Partial copy: only sourceBytesToCopy bytes are copied.
        char partial[6] = {};
        il2cpp::runtime::il2cpp_buffer_memory_copy_sized(partial, src, 6, 3);
        Expect(partial[0] == 'a' && partial[1] == 'b' && partial[2] == 'c' &&
                   partial[3] == '\0',
               "sized memory copy copies only sourceBytesToCopy bytes");

        // Zero-length copy is a no-op.
        char z[2] = { 'x', 'y' };
        il2cpp::runtime::il2cpp_buffer_memory_copy_sized(z, src, 2, 0);
        Expect(z[0] == 'x' && z[1] == 'y',
               "zero-length sized memory copy leaves the destination untouched");

        // Out-of-bounds: sourceBytesToCopy > destinationSizeInBytes.
        ExpectThrows(kArgOutOfRangeType,
                     "sized memory copy with sourceBytesToCopy > destinationSizeInBytes throws ArgumentOutOfRangeException",
                     [&] { il2cpp::runtime::il2cpp_buffer_memory_copy_sized(dst, src, 5, 6); });

        // Negative count: net48's checked long->nuint conversion overflows.
        ExpectThrows(kOverflowType,
                     "sized memory copy with negative sourceBytesToCopy throws OverflowException",
                     [&] { il2cpp::runtime::il2cpp_buffer_memory_copy_sized(dst, src, 6, -1); });

        // Forward overlap: memmove semantics, no clobbering.
        char fwd[6];
        std::memcpy(fwd, src, 6);
        il2cpp::runtime::il2cpp_buffer_memory_copy_sized(fwd + 1, fwd, 5, 5);
        Expect(std::memcmp(fwd, "aabcde", 6) == 0,
               "forward-overlapping sized memory copy behaves like memmove");

        // Backward overlap: "abcdef" -> move [1..5] onto [0..4] yields
        // "bcdeff" (b c d e f + the untouched f).
        char bwd[6];
        std::memcpy(bwd, src, 6);
        il2cpp::runtime::il2cpp_buffer_memory_copy_sized(bwd, bwd + 1, 6, 5);
        Expect(std::memcmp(bwd, "bcdeff", 6) == 0,
               "backward-overlapping sized memory copy behaves like memmove");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d buffer assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime buffer tests passed\n");
    return 0;
}
