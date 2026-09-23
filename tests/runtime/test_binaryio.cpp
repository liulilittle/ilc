//------------------------------------------------------------------------------
// test_binaryio.cpp -- System.IO.BinaryReader / BinaryWriter byte-stream
// domain runtime unit tests. See binaryio.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. int32   : round-trips incl. INT32_MIN/MAX; byte layout 04 03 02 01
//                proves little-endian order.
//   2. int64   : round-trips incl. INT64_MIN/MAX; layout 01 00 .. 00 for 1.
//   3. uint32  : round-trips incl. UINT32_MAX; 0xDEADBEEF layout EF BE AD DE.
//   4. double  : bit-exact round-trips (0.0, -0.0, 1.0, -2.5, pi, 1e300);
//                1.0 layout 00 00 00 00 00 00 F0 3F.
//   5. single  : bit-exact round-trips (0.0f, 1.0f, -0.5f, FLT_MAX);
//                1.0f layout 00 00 80 3F.
//   6. bool    : true -> 0x01, false -> 0x00, both round-trip.
//   7. string  : 7-bit-encoded UTF-8 byte length + UTF-8 content: "abc" ->
//                03 61 62 63; empty -> 00; null -> 00; embedded NUL unit ->
//                03 61 00 62; Chinese "ni hao" -> 06 E4 BD A0 E5 A5 BD; a
//                >127-byte string -> two-byte prefix C8 01.
//   8. cross-writer/reader consistency: two independent writers produce
//                byte-identical streams; a reader over writer A's bytes
//                returns exactly what writer B wrote.
//   9. mixed sequence: interleaved types round-trip in order through one
//                writer and one reader.
//  10. seek     : Begin/Current/End origins, position reporting, -1 for
//                out-of-range targets and unknown origins.
//  11. truncation: short buffers return the zero value, latch the failed
//                flag (eof -> 1) and never advance the position; the flag
//                stays latched for later reads.
//  12. empty buffer: every read fails cleanly (zero value + eof).
//  14. 7-bit prefix boundaries (writer): lengths 127 / 128 / 16383 / 16384 /
//                0x200000 emit 7F / 80 01 / FF 7F / 80 80 01 / 80 80 80 01 and
//                round-trip through one reader.
//  15. malformed prefixes (reader): five continuation bytes (no terminator)
//                and a huge 0xFFFFFFFF length both fail cleanly -- null +
//                latched flag, position unchanged, and NO allocation attempt
//                (the read-string preflight).
//  16. lone surrogates (net48 write): U+D800 / U+DC00 encode as EF BF BD
//                (U+FFFD replacement) and read back as U+FFFD; a valid astral
//                pair U+1F600 round-trips as F0 9F 98 80 <-> D83D DE00.
//  17. invalid UTF-8 read: replacement-fallback counts match net48
//                (E0 80 80 -> 2 x U+FFFD, ED A0 80 -> 2, F0 80 80 80 -> 3,
//                F4 90 80 80 -> 3, E4 BD -> 1, E4 80 41 -> U+FFFD + 'A',
//                C1 BF -> 2, lone 80 -> 1).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "binaryio.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"  // DotNetException carrier (ThrowsDotNet)
#include "il2cpp_io.h"         // icall_file_delete (test fixture cleanup)
#include "il2cpp_runtime.h"    // il2cpp_object_new / il2cpp_class_by_name /
                               // il2cpp_is_instance
#include "il2cpp_streamio.h"   // the BinaryReader/Writer OBJECT surface (binrw-b23)
#include "il2cpp_string.h"
#include "streamio.h"          // the stream HANDLE layer (file-open fixture)

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Managed string <-> ASCII comparison helper.
bool StrEquals(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// Managed string <-> UTF-16 units comparison helper (Chinese / NUL tests).
bool StrEquals16(il2cpp::Il2CppObject* s, const char16_t* units, int32_t length)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    if (str->length != length)
    {
        return false;
    }
    for (int32_t i = 0; i < length; ++i)
    {
        if (str->chars[i] != units[i])
        {
            return false;
        }
    }
    return true;
}

// Build a managed string from an ASCII literal (test input helper).
il2cpp::Il2CppObject* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Build a managed string from UTF-16 units (test input helper).
il2cpp::Il2CppObject* Cs16(const char16_t* units, int32_t length)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(units, length));
}

// Byte 'i' of a writer snapshot (managed byte[]).
uint8_t WByte(il2cpp::Il2CppArray* arr, int32_t i)
{
    return *reinterpret_cast<const uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(arr, i));
}

// Reader over a writer snapshot's bytes (the reader copies the range).
il2cpp::runtime::Il2CppBinaryReader* ReaderOf(il2cpp::Il2CppArray* arr)
{
    return il2cpp::runtime::il2cpp_binary_reader_create(
        reinterpret_cast<const uint8_t*>(arr->data),
        static_cast<size_t>(arr->length));
}

// Bit-exact comparisons for floating-point round-trips.
uint64_t DoubleBits(double v)
{
    uint64_t bits;
    std::memcpy(&bits, &v, sizeof(bits));
    return bits;
}

uint32_t SingleBits(float v)
{
    uint32_t bits;
    std::memcpy(&bits, &v, sizeof(bits));
    return bits;
}

// 7-bit-encode 'len' (the writer's length-prefix convention) then append
// 'content' -- builds a reader input buffer for a string payload.
std::vector<uint8_t> Prefixed(const std::vector<uint8_t>& content)
{
    std::vector<uint8_t> buf;
    uint32_t len = static_cast<uint32_t>(content.size());
    while (len >= 0x80u)
    {
        buf.push_back(static_cast<uint8_t>((len & 0x7Fu) | 0x80u));
        len >>= 7;
    }
    buf.push_back(static_cast<uint8_t>(len));
    buf.insert(buf.end(), content.begin(), content.end());
    return buf;
}

// Read a string whose payload is exactly 'content' (with the 7-bit prefix).
il2cpp::Il2CppObject* ReadStringOf(const std::vector<uint8_t>& content)
{
    const std::vector<uint8_t> buf = Prefixed(content);
    il2cpp::runtime::Il2CppBinaryReader* r = il2cpp::runtime::il2cpp_binary_reader_create(
        buf.data(), buf.size());
    il2cpp::Il2CppObject* s = il2cpp::runtime::il2cpp_binary_read_string(r);
    il2cpp::runtime::il2cpp_binary_reader_destroy(r);
    return s;
}

// Run 'fn' and report whether it threw a DotNetException whose type string
// (with base chain) exactly matches 'expectedChain' (the test_streamio.cpp
// helper convention -- the object ctors throw the M3 exception carriers).
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedChain)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

const char kArgNullExceptionChain[] =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char kArgumentChain[] =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char kNullRefChain[] =
    "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";
const char kNotSupportedChain[] =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
const char kArgOutOfRangeChain[] =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";

// Allocate fresh objects through the runtime descriptors (the same shape
// generated newobj emits -- the ctor icall then arms the handle).
il2cpp::Il2CppObject* NewMemoryStream()
{
    return il2cpp::il2cpp_object_new(&System_IO_MemoryStream_class);
}
il2cpp::Il2CppObject* NewBinaryWriter()
{
    return il2cpp::il2cpp_object_new(&System_IO_BinaryWriter_class);
}
il2cpp::Il2CppObject* NewBinaryReader()
{
    return il2cpp::il2cpp_object_new(&System_IO_BinaryReader_class);
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    // --- 1. int32 -----------------------------------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_int32(w, 0x01020304);
        il2cpp_binary_write_int32(w, INT32_MIN);
        il2cpp_binary_write_int32(w, INT32_MAX);
        il2cpp_binary_write_int32(w, -1);
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == 16, "int32 writes occupy 4 bytes each");
        Expect(WByte(arr, 0) == 0x04 && WByte(arr, 1) == 0x03 && WByte(arr, 2) == 0x02 && WByte(arr, 3) == 0x01,
               "int32 little-endian byte order (0x01020304 -> 04 03 02 01)");
        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(il2cpp_binary_read_int32(r) == 0x01020304, "int32 round-trips");
        Expect(il2cpp_binary_read_int32(r) == INT32_MIN, "int32 INT32_MIN round-trips");
        Expect(il2cpp_binary_read_int32(r) == INT32_MAX, "int32 INT32_MAX round-trips");
        Expect(il2cpp_binary_read_int32(r) == -1, "int32 -1 round-trips");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 2. int64 -----------------------------------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_int64(w, 1);
        il2cpp_binary_write_int64(w, -1);
        il2cpp_binary_write_int64(w, INT64_MIN);
        il2cpp_binary_write_int64(w, INT64_MAX);
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == 32, "int64 writes occupy 8 bytes each");
        Expect(WByte(arr, 0) == 0x01 && WByte(arr, 1) == 0x00 && WByte(arr, 7) == 0x00,
               "int64 little-endian byte order (1 -> 01 00 00 00 00 00 00 00)");
        Expect(WByte(arr, 8) == 0xFF && WByte(arr, 15) == 0xFF, "int64 -1 writes all 0xFF");
        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(il2cpp_binary_read_int64(r) == 1, "int64 1 round-trips");
        Expect(il2cpp_binary_read_int64(r) == -1, "int64 -1 round-trips");
        Expect(il2cpp_binary_read_int64(r) == INT64_MIN, "int64 INT64_MIN round-trips");
        Expect(il2cpp_binary_read_int64(r) == INT64_MAX, "int64 INT64_MAX round-trips");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 3. uint32 ----------------------------------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_uint32(w, 0);
        il2cpp_binary_write_uint32(w, UINT32_MAX);
        il2cpp_binary_write_uint32(w, 0xDEADBEEFu);
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == 12, "uint32 writes occupy 4 bytes each");
        Expect(WByte(arr, 8) == 0xEF && WByte(arr, 9) == 0xBE && WByte(arr, 10) == 0xAD && WByte(arr, 11) == 0xDE,
               "uint32 little-endian byte order (0xDEADBEEF -> EF BE AD DE)");
        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(il2cpp_binary_read_uint32(r) == 0, "uint32 0 round-trips");
        Expect(il2cpp_binary_read_uint32(r) == UINT32_MAX, "uint32 UINT32_MAX round-trips");
        Expect(il2cpp_binary_read_uint32(r) == 0xDEADBEEFu, "uint32 0xDEADBEEF round-trips");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 4. double ----------------------------------------------------------
    {
        const double values[] = { 0.0, -0.0, 1.0, -2.5, 3.141592653589793, 1e300 };
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        for (size_t i = 0; i < sizeof(values) / sizeof(values[0]); ++i)
        {
            il2cpp_binary_write_double(w, values[i]);
        }
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == 48, "double writes occupy 8 bytes each");
        Expect(WByte(arr, 16) == 0x00 && WByte(arr, 22) == 0xF0 && WByte(arr, 23) == 0x3F,
               "double 1.0 little-endian IEEE-754 (00 00 00 00 00 00 F0 3F)");
        Il2CppBinaryReader* r = ReaderOf(arr);
        for (size_t i = 0; i < sizeof(values) / sizeof(values[0]); ++i)
        {
            const double got = il2cpp_binary_read_double(r);
            if (DoubleBits(got) != DoubleBits(values[i]))
            {
                std::printf("FAIL: double value %zu bit-exact round-trip\n", i);
                ++g_failures;
            }
        }
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 5. single ----------------------------------------------------------
    {
        const float values[] = { 0.0f, 1.0f, -0.5f, 3.402823466e38f };
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        for (size_t i = 0; i < sizeof(values) / sizeof(values[0]); ++i)
        {
            il2cpp_binary_write_single(w, values[i]);
        }
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == 16, "single writes occupy 4 bytes each");
        Expect(WByte(arr, 4) == 0x00 && WByte(arr, 6) == 0x80 && WByte(arr, 7) == 0x3F,
               "single 1.0f little-endian IEEE-754 (00 00 80 3F)");
        Il2CppBinaryReader* r = ReaderOf(arr);
        for (size_t i = 0; i < sizeof(values) / sizeof(values[0]); ++i)
        {
            const float got = il2cpp_binary_read_single(r);
            if (SingleBits(got) != SingleBits(values[i]))
            {
                std::printf("FAIL: single value %zu bit-exact round-trip\n", i);
                ++g_failures;
            }
        }
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 6. bool ------------------------------------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_bool(w, 1);
        il2cpp_binary_write_bool(w, 0);
        il2cpp_binary_write_bool(w, 123); // any non-zero writes 0x01
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == 3, "bool writes occupy 1 byte each");
        Expect(WByte(arr, 0) == 0x01 && WByte(arr, 1) == 0x00 && WByte(arr, 2) == 0x01,
               "bool byte layout (true 0x01, false 0x00)");
        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(il2cpp_binary_read_bool(r) == 1, "bool true round-trips");
        Expect(il2cpp_binary_read_bool(r) == 0, "bool false round-trips");
        Expect(il2cpp_binary_read_bool(r) == 1, "bool non-zero writes round-trip as true");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 7. string (7-bit UTF-8 byte length + UTF-8 content) ----------------
    {
        const char16_t han[] = u"\u4F60\u597D";           // "ni hao"
        const char16_t nl[] = { 0x61, 0x00, 0x62 };       // "a" NUL "b"
        const char16_t empty[] = u"";
        char16_t wide[200];
        for (int32_t i = 0; i < 200; ++i) // >127-byte 7-bit prefix case
        {
            wide[i] = u'a';
        }

        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_string(w, Cs("abc"));
        il2cpp_binary_write_string(w, Cs16(han, 2));
        il2cpp_binary_write_string(w, Cs16(nl, 3));
        il2cpp_binary_write_string(w, Cs16(empty, 0));
        il2cpp_binary_write_string(w, nullptr);
        il2cpp_binary_write_string(w, Cs16(wide, 200));
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);

        // "abc": prefix 0x03 + 61 62 63 (4 bytes total).
        Expect(WByte(arr, 0) == 0x03 && WByte(arr, 1) == 0x61 && WByte(arr, 2) == 0x62 && WByte(arr, 3) == 0x63,
               "string ASCII: 7-bit length prefix + UTF-8 content");
        // "ni hao": 6 UTF-8 bytes (E4 BD A0 E5 A5 BD) after prefix 0x06.
        Expect(WByte(arr, 4) == 0x06 && WByte(arr, 5) == 0xE4 && WByte(arr, 6) == 0xBD && WByte(arr, 7) == 0xA0
               && WByte(arr, 8) == 0xE5 && WByte(arr, 9) == 0xA5 && WByte(arr, 10) == 0xBD,
               "string Chinese: prefix is the UTF-8 byte count (你好 -> 06)");
        // "a" NUL "b": prefix 0x03 + 61 00 62 (embedded NUL is content).
        Expect(WByte(arr, 11) == 0x03 && WByte(arr, 12) == 0x61 && WByte(arr, 13) == 0x00 && WByte(arr, 14) == 0x62,
               "string embedded NUL: unit 0x00 encodes as content byte 0x00");
        // Empty and null both write a single 0x00 prefix.
        Expect(WByte(arr, 15) == 0x00 && WByte(arr, 16) == 0x00,
               "string empty and null write a single 0x00 length prefix");
        // 200 UTF-8 bytes: 7-bit prefix C8 01 (200 = 0xC8 | 0x80, then 1).
        Expect(WByte(arr, 17) == 0xC8 && WByte(arr, 18) == 0x01,
               "string >127 bytes: two-byte 7-bit length prefix (C8 01)");
        Expect(il2cpp_array_get_length(arr) == 219, "string sequence totals 219 bytes");

        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(StrEquals(il2cpp_binary_read_string(r), "abc"), "string ASCII round-trips");
        Expect(StrEquals16(il2cpp_binary_read_string(r), han, 2), "string Chinese round-trips");
        Expect(StrEquals16(il2cpp_binary_read_string(r), nl, 3), "string embedded NUL round-trips");
        Expect(StrEquals16(il2cpp_binary_read_string(r), empty, 0), "string empty round-trips");
        Expect(StrEquals16(il2cpp_binary_read_string(r), empty, 0), "string null write round-trips as empty");
        Expect(StrEquals16(il2cpp_binary_read_string(r), wide, 200), "string 200 bytes round-trips");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 8. cross-writer/reader consistency ----------------------------------
    {
        Il2CppBinaryWriter* a = il2cpp_binary_writer_create();
        Il2CppBinaryWriter* b = il2cpp_binary_writer_create();
        for (int32_t pass = 0; pass < 2; ++pass)
        {
            Il2CppBinaryWriter* w = (pass == 0) ? a : b;
            il2cpp_binary_write_int32(w, 42);
            il2cpp_binary_write_string(w, Cs("hello"));
            il2cpp_binary_write_double(w, 2.5);
            il2cpp_binary_write_bool(w, 1);
            il2cpp_binary_write_uint32(w, 7);
            il2cpp_binary_write_int64(w, -9);
            il2cpp_binary_write_single(w, 0.25f);
            il2cpp_binary_write_string(w, Cs16(u"\u4E2D\u6587", 2)); // "zhong wen"
        }
        Il2CppArray* arrA = il2cpp_binary_writer_get_bytes(a);
        Il2CppArray* arrB = il2cpp_binary_writer_get_bytes(b);
        Expect(il2cpp_array_get_length(arrA) == 42, "cross-writer stream is 42 bytes");
        Expect(il2cpp_array_get_length(arrA) == il2cpp_array_get_length(arrB)
               && std::memcmp(arrA->data, arrB->data, static_cast<size_t>(arrA->length)) == 0,
               "two writers produce byte-identical streams");
        Il2CppBinaryReader* r = ReaderOf(arrA);
        Expect(il2cpp_binary_read_int32(r) == 42, "reader over writer A reads writer B's int32");
        Expect(StrEquals(il2cpp_binary_read_string(r), "hello"), "reader over writer A reads writer B's string");
        Expect(il2cpp_binary_read_double(r) == 2.5, "reader over writer A reads writer B's double");
        Expect(il2cpp_binary_read_bool(r) == 1, "reader over writer A reads writer B's bool");
        Expect(il2cpp_binary_read_uint32(r) == 7, "reader over writer A reads writer B's uint32");
        Expect(il2cpp_binary_read_int64(r) == -9, "reader over writer A reads writer B's int64");
        Expect(il2cpp_binary_read_single(r) == 0.25f, "reader over writer A reads writer B's single");
        Expect(StrEquals16(il2cpp_binary_read_string(r), u"\u4E2D\u6587", 2),
               "reader over writer A reads writer B's Chinese string");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(a);
        il2cpp_binary_writer_destroy(b);
    }

    // --- 9. mixed sequence ---------------------------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_int32(w, 12345);
        il2cpp_binary_write_string(w, Cs("abc"));
        il2cpp_binary_write_double(w, 1.25);
        il2cpp_binary_write_bool(w, 0);
        il2cpp_binary_write_uint32(w, 0xCAFEBABEu);
        il2cpp_binary_write_int64(w, 0x1122334455667788LL);
        il2cpp_binary_write_single(w, 3.5f);
        il2cpp_binary_write_string(w, Cs16(u"\u4F60\u597D\uFF0C\u4E16\u754C", 5)); // "ni hao, shi jie"
        Il2CppBinaryReader* r = ReaderOf(il2cpp_binary_writer_get_bytes(w));
        Expect(il2cpp_binary_read_int32(r) == 12345, "mixed sequence int32");
        Expect(StrEquals(il2cpp_binary_read_string(r), "abc"), "mixed sequence string");
        Expect(il2cpp_binary_read_double(r) == 1.25, "mixed sequence double");
        Expect(il2cpp_binary_read_bool(r) == 0, "mixed sequence bool");
        Expect(il2cpp_binary_read_uint32(r) == 0xCAFEBABEu, "mixed sequence uint32");
        Expect(il2cpp_binary_read_int64(r) == 0x1122334455667788LL, "mixed sequence int64");
        Expect(il2cpp_binary_read_single(r) == 3.5f, "mixed sequence single");
        Expect(StrEquals16(il2cpp_binary_read_string(r), u"\u4F60\u597D\uFF0C\u4E16\u754C", 5),
               "mixed sequence Chinese string");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 10. seek -----------------------------------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_int32(w, 10);
        il2cpp_binary_write_int32(w, 20);
        il2cpp_binary_write_int32(w, 30);
        il2cpp_binary_write_int32(w, 40);
        Il2CppBinaryReader* r = ReaderOf(il2cpp_binary_writer_get_bytes(w));
        Expect(il2cpp_binary_reader_position(r) == 0, "reader starts at position 0");
        Expect(il2cpp_binary_seek(r, 0, 0) == 0, "seek Begin offset 0 -> 0");
        Expect(il2cpp_binary_seek(r, 4, 0) == 4, "seek Begin offset 4 -> 4");
        Expect(il2cpp_binary_read_int32(r) == 20, "seek then read hits the second int32");
        Expect(il2cpp_binary_seek(r, 0, 2) == 16, "seek End offset 0 -> buffer length");
        Expect(il2cpp_binary_seek(r, -4, 2) == 12, "seek End offset -4 -> 12");
        Expect(il2cpp_binary_read_int32(r) == 40, "seek then read hits the fourth int32");
        Expect(il2cpp_binary_seek(r, -1, 0) == -1, "seek Begin negative offset fails");
        Expect(il2cpp_binary_seek(r, 1, 1) == -1, "seek Current past the end fails");
        Expect(il2cpp_binary_seek(r, -17, 2) == -1, "seek End before the start fails");
        Expect(il2cpp_binary_seek(r, 0, 3) == -1, "seek unknown origin fails");
        Expect(il2cpp_binary_seek(r, 0, 0) == 0, "seek back to 0 after failures");
        Expect(il2cpp_binary_read_int32(r) == 10, "seek to 0 then read hits the first int32");
        Expect(il2cpp_binary_reader_position(r) == 4, "position advances after a read");
        Expect(il2cpp_binary_reader_eof(r) == 0, "successful reads leave the failed flag clear");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 11. truncation (short buffers) -------------------------------------
    {
        const uint8_t three[] = { 0x01, 0x02, 0x03 };
        Il2CppBinaryReader* r = il2cpp_binary_reader_create(three, sizeof(three));
        Expect(il2cpp_binary_read_int32(r) == 0, "truncated int32 returns 0");
        Expect(il2cpp_binary_reader_eof(r) == 1, "truncated int32 latches the failed flag");
        Expect(il2cpp_binary_reader_position(r) == 0, "failed read never advances the position");
        Expect(il2cpp_binary_read_int32(r) == 0, "failed reader keeps returning 0");
        Expect(il2cpp_binary_reader_eof(r) == 1, "failed flag stays latched");
        il2cpp_binary_reader_destroy(r);

        const uint8_t four[] = { 0x01, 0x02, 0x03, 0x04 };
        r = il2cpp_binary_reader_create(four, sizeof(four));
        Expect(il2cpp_binary_read_int32(r) == 0x04030201, "full int32 reads correctly");
        Expect(il2cpp_binary_reader_eof(r) == 0, "successful read leaves the flag clear");
        Expect(il2cpp_binary_read_int64(r) == 0, "truncated int64 returns 0");
        Expect(il2cpp_binary_reader_eof(r) == 1, "truncated int64 latches the failed flag");
        il2cpp_binary_reader_destroy(r);

        const uint8_t cut[] = { 0x05, 0x61 }; // length says 5, content has 1
        r = il2cpp_binary_reader_create(cut, sizeof(cut));
        Expect(il2cpp_binary_read_string(r) == nullptr, "content-truncated string reads null");
        Expect(il2cpp_binary_reader_eof(r) == 1, "content-truncated string latches the failed flag");
        il2cpp_binary_reader_destroy(r);
    }

    // --- 12. empty buffer ---------------------------------------------------
    {
        Il2CppBinaryReader* r = il2cpp_binary_reader_create(nullptr, 0);
        Expect(il2cpp_binary_read_int32(r) == 0, "empty buffer int32 reads 0");
        Expect(il2cpp_binary_reader_eof(r) == 1, "empty buffer latches the failed flag");
        Expect(il2cpp_binary_read_string(r) == nullptr, "empty buffer string reads null");
        Expect(il2cpp_binary_seek(r, 0, 0) == 0, "empty buffer seek to 0 succeeds");
        Expect(il2cpp_binary_seek(r, 1, 0) == -1, "empty buffer seek past 0 fails");
        il2cpp_binary_reader_destroy(r);
    }

    // --- 13. snapshot copy + null-destroy -----------------------------------
    {
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_int32(w, 1);
        Il2CppArray* snap = il2cpp_binary_writer_get_bytes(w);
        il2cpp_binary_write_int32(w, 2);
        Expect(il2cpp_array_get_length(snap) == 4, "writer snapshot is a copy, not a view");
        il2cpp_binary_writer_destroy(w);
        Expect(il2cpp_binary_writer_get_bytes(nullptr) == nullptr, "get_bytes on null writer returns null");
        il2cpp_binary_writer_destroy(nullptr); // must not crash
        il2cpp_binary_reader_destroy(nullptr); // must not crash
    }

    // --- 14. 7-bit prefix boundaries (writer -> reader) ----------------------
    {
        const uint8_t b1[] = { 0x7F };
        const uint8_t b2[] = { 0x80, 0x01 };
        const uint8_t b3[] = { 0xFF, 0x7F };
        const uint8_t b4[] = { 0x80, 0x80, 0x01 };
        struct Boundary { const uint8_t* prefix; int prefixLen; size_t length; };
        const Boundary cases[] = {
            { b1, 1, 127 }, { b2, 2, 128 }, { b3, 2, 16383 }, { b4, 3, 16384 },
        };
        for (size_t t = 0; t < sizeof(cases) / sizeof(cases[0]); ++t)
        {
            std::vector<char16_t> units(cases[t].length, u'a');
            Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
            il2cpp_binary_write_string(w, Cs16(units.data(), static_cast<int32_t>(units.size())));
            Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
            Expect(il2cpp_array_get_length(arr)
                       == static_cast<int32_t>(cases[t].prefixLen + cases[t].length),
                   "7-bit prefix: total stream length");
            bool prefixOk = true;
            for (int i = 0; i < cases[t].prefixLen; ++i)
            {
                prefixOk = prefixOk && WByte(arr, i) == cases[t].prefix[i];
            }
            Expect(prefixOk, "7-bit prefix: boundary bytes");
            Il2CppBinaryReader* r = ReaderOf(arr);
            Expect(StrEquals16(il2cpp_binary_read_string(r), units.data(),
                               static_cast<int32_t>(units.size())),
                   "7-bit prefix: round-trips the boundary length");
            il2cpp_binary_reader_destroy(r);
            il2cpp_binary_writer_destroy(w);
        }

        // 4-byte prefix: 0x200000 (2 MiB) bytes -> 80 80 80 01.
        std::vector<char16_t> big(0x200000, u'a');
        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_string(w, Cs16(big.data(), static_cast<int32_t>(big.size())));
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);
        Expect(il2cpp_array_get_length(arr) == static_cast<int32_t>(0x200000) + 4,
               "4-byte 7-bit prefix: 0x200000 length + 4 prefix bytes");
        Expect(WByte(arr, 0) == 0x80 && WByte(arr, 1) == 0x80
               && WByte(arr, 2) == 0x80 && WByte(arr, 3) == 0x01,
               "4-byte 7-bit prefix: 80 80 80 01");
        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(StrEquals16(il2cpp_binary_read_string(r), big.data(),
                           static_cast<int32_t>(big.size())),
               "4-byte 7-bit prefix: 0x200000 bytes round-trip");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 15. malformed / huge 7-bit prefixes (reader) ------------------------
    {
        const uint8_t noTerminator[] = { 0x80, 0x80, 0x80, 0x80, 0x80 };
        Il2CppBinaryReader* r = il2cpp_binary_reader_create(noTerminator, sizeof(noTerminator));
        Expect(il2cpp_binary_read_string(r) == nullptr, "5 continuation bytes -> null");
        Expect(il2cpp_binary_reader_eof(r) == 1, "5 continuation bytes latch the failed flag");
        Expect(il2cpp_binary_reader_position(r) == 0, "5 continuation bytes never advance the position");
        il2cpp_binary_reader_destroy(r);

        const uint8_t sixBytes[] = { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        r = il2cpp_binary_reader_create(sixBytes, sizeof(sixBytes));
        Expect(il2cpp_binary_read_string(r) == nullptr, "6th byte after 5 continuations -> null");
        Expect(il2cpp_binary_reader_eof(r) == 1, "6th byte after 5 continuations latches failed");
        il2cpp_binary_reader_destroy(r);

        // Length 0xFFFFFFFF with nothing following: must fail cleanly WITHOUT
        // attempting a ~4 GiB allocation (the preflight bounds the allocation
        // to the remaining buffer).
        const uint8_t hugeLen[] = { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F };
        r = il2cpp_binary_reader_create(hugeLen, sizeof(hugeLen));
        Expect(il2cpp_binary_read_string(r) == nullptr, "huge 0xFFFFFFFF length -> null");
        Expect(il2cpp_binary_reader_eof(r) == 1, "huge 0xFFFFFFFF length latches the failed flag");
        Expect(il2cpp_binary_reader_position(r) == 0, "huge 0xFFFFFFFF length never advances the position");
        il2cpp_binary_reader_destroy(r);

        // Length 0x200000 whose content is cut short: the preflight rejects it too.
        const uint8_t shortContent[] = { 0x80, 0x80, 0x80, 0x01, 0x61, 0x62 };
        r = il2cpp_binary_reader_create(shortContent, sizeof(shortContent));
        Expect(il2cpp_binary_read_string(r) == nullptr, "4-byte prefix with short content -> null");
        Expect(il2cpp_binary_reader_eof(r) == 1, "4-byte prefix with short content latches failed");
        il2cpp_binary_reader_destroy(r);
    }

    // --- 16. lone surrogates + astral pair (net48 write semantics) -----------
    {
        const char16_t loneHigh[] = { 0x61, 0xD800, 0x62 }; // 'a' D800 'b'
        const char16_t loneLow[] = { 0xDC00 };
        const char16_t astral[] = { 0xD83D, 0xDE00 };       // U+1F600
        const char16_t fffd[] = { 0xFFFD };
        const char16_t mix[] = { 0x61, 0xFFFD, 0x62 };      // 'a' U+FFFD 'b'

        Il2CppBinaryWriter* w = il2cpp_binary_writer_create();
        il2cpp_binary_write_string(w, Cs16(loneHigh, 3));
        il2cpp_binary_write_string(w, Cs16(loneLow, 1));
        il2cpp_binary_write_string(w, Cs16(astral, 2));
        Il2CppArray* arr = il2cpp_binary_writer_get_bytes(w);

        // 'a' D800 'b' -> 05 61 EF BF BD 62 (lone high surrogate -> U+FFFD).
        Expect(WByte(arr, 0) == 0x05 && WByte(arr, 1) == 0x61 && WByte(arr, 2) == 0xEF
               && WByte(arr, 3) == 0xBF && WByte(arr, 4) == 0xBD && WByte(arr, 5) == 0x62,
               "lone high surrogate writes EF BF BD (U+FFFD replacement)");
        // D800 -> 03 EF BF BD (lone low surrogate -> U+FFFD).
        Expect(WByte(arr, 6) == 0x03 && WByte(arr, 7) == 0xEF && WByte(arr, 8) == 0xBF
               && WByte(arr, 9) == 0xBD,
               "lone low surrogate writes EF BF BD (U+FFFD replacement)");
        // U+1F600 -> 04 F0 9F 98 80 (valid astral pair, 4-byte sequence).
        Expect(WByte(arr, 10) == 0x04 && WByte(arr, 11) == 0xF0 && WByte(arr, 12) == 0x9F
               && WByte(arr, 13) == 0x98 && WByte(arr, 14) == 0x80,
               "astral pair U+1F600 writes F0 9F 98 80");

        Il2CppBinaryReader* r = ReaderOf(arr);
        Expect(StrEquals16(il2cpp_binary_read_string(r), mix, 3),
               "lone high surrogate reads back as U+FFFD");
        Expect(StrEquals16(il2cpp_binary_read_string(r), fffd, 1),
               "lone low surrogate reads back as U+FFFD");
        Expect(StrEquals16(il2cpp_binary_read_string(r), astral, 2),
               "astral pair round-trips as a surrogate pair");
        il2cpp_binary_reader_destroy(r);
        il2cpp_binary_writer_destroy(w);
    }

    // --- 17. invalid UTF-8 read: net48 replacement-fallback counts -----------
    {
        const char16_t one[] = { 0xFFFD };
        const char16_t two[] = { 0xFFFD, 0xFFFD };
        const char16_t three[] = { 0xFFFD, 0xFFFD, 0xFFFD };

        const uint8_t e0[] = { 0xE0, 0x80, 0x80 };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(e0, e0 + 3)), two, 2),
               "E0 80 80 (overlong) -> 2 x U+FFFD (net48)");

        const uint8_t ed[] = { 0xED, 0xA0, 0x80 };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(ed, ed + 3)), two, 2),
               "ED A0 80 (surrogate range) -> 2 x U+FFFD (net48)");

        const uint8_t f0[] = { 0xF0, 0x80, 0x80, 0x80 };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(f0, f0 + 4)), three, 3),
               "F0 80 80 80 (overlong) -> 3 x U+FFFD (net48)");

        const uint8_t f4[] = { 0xF4, 0x90, 0x80, 0x80 };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(f4, f4 + 4)), three, 3),
               "F4 90 80 80 (out of range) -> 3 x U+FFFD (net48)");

        const uint8_t trunc[] = { 0xE4, 0xBD };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(trunc, trunc + 2)), one, 1),
               "E4 BD (truncated) -> 1 x U+FFFD (net48)");

        const uint8_t mixb[] = { 0xE4, 0x80, 0x41 };
        const char16_t mix[] = { 0xFFFD, u'A' };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(mixb, mixb + 3)), mix, 2),
               "E4 80 41 (invalid continuation) -> U+FFFD + 'A' (net48)");

        const uint8_t c1[] = { 0xC1, 0xBF };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(c1, c1 + 2)), two, 2),
               "C1 BF (overlong lead) -> 2 x U+FFFD (net48)");

        const uint8_t lone[] = { 0x80 };
        Expect(StrEquals16(ReadStringOf(std::vector<uint8_t>(lone, lone + 1)), one, 1),
               "lone continuation 0x80 -> 1 x U+FFFD (net48)");
    }

    // --- 18. OBJECT surface: BinaryReader/Writer as STREAM WRAPPERS (binrw-b23)
    // The managed BinaryReader/BinaryWriter wrap the SAME StreamState as the
    // wrapped MemoryStream (net48 ownership) and speak the C56 protocol at the
    // shared stream position. Exercises: descriptor identity, wrap ctor
    // validation, byte-exact writer layout, reader round-trips (incl. the
    // 7-bit string prefixes), the SHARED position (the wrapped stream's
    // position advances with every read/write), and close-once semantics.
    il2cpp::il2cpp_stream_register_classes();
    Expect(il2cpp_class_by_name("System.IO.BinaryReader") == &System_IO_BinaryReader_class,
           "System.IO.BinaryReader resolves by name (registered)");
    Expect(il2cpp_class_by_name("System.IO.BinaryWriter") == &System_IO_BinaryWriter_class,
           "System.IO.BinaryWriter resolves by name (registered)");
    Expect(System_IO_BinaryReader_class.instanceSize == sizeof(Il2CppStreamObject),
           "BinaryReader instanceSize == sizeof(Il2CppStreamObject)");
    Expect(System_IO_BinaryWriter_class.instanceSize == sizeof(Il2CppStreamObject),
           "BinaryWriter instanceSize == sizeof(Il2CppStreamObject)");
    Expect(System_IO_BinaryReader_class.refFieldBitmap == 0u
           && System_IO_BinaryWriter_class.refFieldBitmap == 0u,
           "binary reader/writer descriptors carry no GC reference fields");

    Il2CppObject* bwms = NewMemoryStream();
    icall_stream_memory_ctor(bwms);
    Il2CppObject* bw = NewBinaryWriter();
    Expect(bw->klass == &System_IO_BinaryWriter_class, "BinaryWriter object klass stamped");
    Expect(il2cpp_is_instance(bw, &System_Object_class),
           "a BinaryWriter IS-A System.Object (baseClass chain)");
    icall_binary_writer_ctor(bw, bwms);

    // Mixed writes over the shared stream: int32 + double + string + bool +
    // int64 + uint32 + single = 4+8+4+1+8+4+4 = 33 bytes.
    icall_binary_write_int32(bw, 0x01020304);
    icall_binary_write_double(bw, 1.0);
    icall_binary_write_string(bw, Cs("abc"));
    icall_binary_write_bool(bw, 1);
    icall_binary_write_int64(bw, -9);
    icall_binary_write_uint32(bw, 0xDEADBEEFu);
    icall_binary_write_single(bw, 2.5f);
    Expect(icall_stream_get_position(bwms) == 33,
           "BinaryWriter writes advance the wrapped stream's position");
    Expect(icall_stream_get_length(bwms) == 33,
           "BinaryWriter writes extend the wrapped stream's length");

    il2cpp::Il2CppArray* bbytes = icall_stream_memory_to_array(bwms);
    Expect(bbytes != nullptr, "BinaryWriter over MemoryStream snapshots via ToArray");
    il2cpp::Il2CppArray* barr = reinterpret_cast<il2cpp::Il2CppArray*>(bbytes);
    Expect(il2cpp_array_get_length(barr) == 33, "object writer stream is 33 bytes");
    // int32 0x01020304 -> 04 03 02 01; double 1.0 -> .. F0 3F; "abc" -> 03 61 62 63.
    Expect(WByte(barr, 0) == 0x04 && WByte(barr, 1) == 0x03 && WByte(barr, 2) == 0x02 && WByte(barr, 3) == 0x01,
           "object writer int32 little-endian layout (04 03 02 01)");
    Expect(WByte(barr, 10) == 0xF0 && WByte(barr, 11) == 0x3F,
           "object writer double 1.0 layout (.. .. .. F0 3F)");
    Expect(WByte(barr, 12) == 0x03 && WByte(barr, 13) == 0x61 && WByte(barr, 14) == 0x62 && WByte(barr, 15) == 0x63,
           "object writer string \"abc\" 7-bit prefix + content");
    // bool true -> 01 at offset 16; int64 -9 -> F7 FF .. FF at 17..24.
    Expect(WByte(barr, 16) == 0x01, "object writer bool true -> 01");
    Expect(WByte(barr, 17) == 0xF7 && WByte(barr, 24) == 0xFF,
           "object writer int64 -9 little-endian (F7 FF FF FF FF FF FF FF)");
    // uint32 0xDEADBEEF -> EF BE AD DE at 25; single 2.5f -> 00 00 20 40 at 29.
    Expect(WByte(barr, 25) == 0xEF && WByte(barr, 26) == 0xBE && WByte(barr, 27) == 0xAD && WByte(barr, 28) == 0xDE,
           "object writer uint32 0xDEADBEEF little-endian");
    Expect(WByte(barr, 29) == 0x00 && WByte(barr, 30) == 0x00 && WByte(barr, 31) == 0x20 && WByte(barr, 32) == 0x40,
           "object writer single 2.5f little-endian (00 00 20 40)");

    // Rewind and read the whole record back through a BinaryReader wrapper.
    Expect(icall_stream_set_position(bwms, 0) == 0, "rewind the wrapped stream before reading");
    Il2CppObject* br = NewBinaryReader();
    Expect(br->klass == &System_IO_BinaryReader_class, "BinaryReader object klass stamped");
    Expect(il2cpp_is_instance(br, &System_Object_class),
           "a BinaryReader IS-A System.Object (baseClass chain)");
    icall_binary_reader_ctor(br, bwms);
    Expect(icall_binary_read_int32(br) == 0x01020304, "object ReadInt32 round-trips");
    Expect(DoubleBits(icall_binary_read_double(br)) == DoubleBits(1.0),
           "object ReadDouble bit-exact round-trip");
    Expect(StrEquals(icall_binary_read_string(br), "abc"), "object ReadString round-trips");
    Expect(icall_binary_read_bool(br) == 1, "object ReadBoolean round-trips");
    Expect(icall_binary_read_int64(br) == -9, "object ReadInt64 round-trips");
    Expect(icall_binary_read_uint32(br) == 0xDEADBEEFu, "object ReadUInt32 round-trips");
    Expect(SingleBits(icall_binary_read_single(br)) == SingleBits(2.5f),
           "object ReadSingle bit-exact round-trip");
    Expect(icall_stream_get_position(bwms) == 33,
           "BinaryReader reads advance the SHARED stream position");

    // WAVE2: ReadByte is the RAW-byte icall -- 0x02 must read back as 2, not
    // the ReadBoolean normalization 1; the bool surface stays unchanged.
    {
        Il2CppObject* rbms = NewMemoryStream();
        icall_stream_memory_ctor(rbms);
        icall_stream_write_byte(rbms, 0xAB);
        icall_stream_write_byte(rbms, 0x02);
        icall_stream_write_byte(rbms, 0x00);
        Expect(icall_stream_set_position(rbms, 0) == 0, "ReadByte fixture rewinds");
        Il2CppObject* rbr = NewBinaryReader();
        icall_binary_reader_ctor(rbr, rbms);
        Expect(icall_binary_read_byte(rbr) == 0xAB,
               "object ReadByte returns the RAW byte (0xAB, not normalized)");
        Expect(icall_binary_read_byte(rbr) == 0x02,
               "object ReadByte does NOT normalize (0x02 stays 2)");
        Expect(icall_binary_read_bool(rbr) == 0,
               "ReadBoolean still normalizes (0x00 -> false)");
        Expect(icall_binary_read_byte(rbr) == 0,
               "ReadByte at EOF returns 0 (M3 zero-value convention)");
    }

    // String surface through the object writers/readers (7-bit prefixes +
    // net48-exact lone-surrogate encoding = EF BF BD).
    {
        Il2CppObject* sms = NewMemoryStream();
        icall_stream_memory_ctor(sms);
        Il2CppObject* sw = NewBinaryWriter();
        icall_binary_writer_ctor(sw, sms);
        icall_binary_write_string(sw, Cs16(u"\u4F60\u597D", 2));         // Chinese BMP
        icall_binary_write_string(sw, Cs16(u"", 0));                     // empty
        icall_binary_write_string(sw, nullptr);                          // null -> empty
        icall_binary_write_string(sw, Cs16(u"\u4E2D\u6587\u8FD4\u5F80", 4)); // BMP round-trip
        Expect(icall_stream_set_position(sms, 0) == 0, "string writer rewind");
        Il2CppObject* sr = NewBinaryReader();
        icall_binary_reader_ctor(sr, sms);
        Expect(StrEquals16(icall_binary_read_string(sr), u"\u4F60\u597D", 2),
               "object string Chinese BMP round-trips");
        Expect(StrEquals16(icall_binary_read_string(sr), u"", 0),
               "object string empty round-trips");
        Expect(StrEquals16(icall_binary_read_string(sr), u"", 0),
               "object string null write reads back empty");
        Expect(StrEquals16(icall_binary_read_string(sr), u"\u4E2D\u6587\u8FD4\u5F80", 4),
               "object string BMP round-trips unit for unit");
        Expect(icall_stream_close(sr) == 0, "string reader Close closes the shared stream");
        Expect(icall_stream_close(sms) == 0, "string stream Close after the wrapper is a no-op");
    }
    // Lone surrogates: net48-exact EF BF BD through the OBJECT writer.
    {
        Il2CppObject* sms = NewMemoryStream();
        icall_stream_memory_ctor(sms);
        Il2CppObject* sw = NewBinaryWriter();
        icall_binary_writer_ctor(sw, sms);
        const char16_t lone[] = { 0x61, 0xD800, 0x62 }; // 'a' D800 'b'
        icall_binary_write_string(sw, Cs16(lone, 3));
        il2cpp::Il2CppArray* sbytes = icall_stream_memory_to_array(sms);
        il2cpp::Il2CppArray* sarr = reinterpret_cast<il2cpp::Il2CppArray*>(sbytes);
        Expect(il2cpp_array_get_length(sarr) == 6 && WByte(sarr, 0) == 0x05 && WByte(sarr, 1) == 0x61
               && WByte(sarr, 2) == 0xEF && WByte(sarr, 3) == 0xBF && WByte(sarr, 4) == 0xBD
               && WByte(sarr, 5) == 0x62,
               "object writer lone high surrogate -> EF BF BD (net48)");
        Expect(icall_stream_close(sw) == 0, "lone-surrogate writer Close");
        Expect(icall_stream_close(sms) == 0, "lone-surrogate stream Close (no-op)");
    }

    // Object ctor / error paths (the stream-wrapper validation re-used).
    Expect(ThrowsDotNet([&] { icall_binary_reader_ctor(nullptr, NewMemoryStream()); }, kNullRefChain),
           "binary reader ctor(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_binary_reader_ctor(NewBinaryReader(), nullptr); }, kArgNullExceptionChain),
           "binary reader ctor(null stream) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_binary_writer_ctor(NewBinaryWriter(), Cs("not a stream")); }, kArgumentChain),
           "binary writer ctor(non-stream) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_binary_write_int32(nullptr, 1); }, kNullRefChain),
           "binary write(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_binary_read_int32(nullptr); }, kNullRefChain),
           "binary read(null receiver) throws NullReferenceException");

    // An empty MemoryStream reader: EOF reads return the zero value.
    {
        Il2CppObject* ros = NewMemoryStream();
        icall_stream_memory_ctor(ros);
        Il2CppObject* rbr = NewBinaryReader();
        icall_binary_reader_ctor(rbr, ros);
        Expect(icall_binary_read_int32(rbr) == 0,
               "binary reader over an empty stream returns 0 (EOF)");
        Expect(icall_stream_close(rbr) == 0, "empty-stream binary reader Close");
        Expect(icall_stream_close(ros) == 0, "empty-stream MemoryStream Close (no-op)");
    }

    // A read-only FileStream wrapper: the writer throws NotSupportedException
    // (net48 shape). MemoryStream is always read-write, so this needs a real
    // read-only file handle.
    {
        il2cpp_stream_t tmp = il2cpp_stream_file_open(Cs("binrw_tmp.bin"), 1); // wb
        Expect(tmp != nullptr && il2cpp_stream_write(tmp, "x", 1) == 1,
               "read-only fixture file is created");
        Expect(il2cpp_stream_close(tmp) == 0, "read-only fixture file closes");

        Il2CppObject* rofs = il2cpp_object_new(&System_IO_FileStream_class);
        icall_stream_file_ctor(rofs, Cs("binrw_tmp.bin"), 3, 1); // Open + Read
        Il2CppObject* rbw = NewBinaryWriter();
        icall_binary_writer_ctor(rbw, rofs);
        Expect(ThrowsDotNet([&] { icall_binary_write_int32(rbw, 1); }, kNotSupportedChain),
               "binary write on a non-writable wrapper throws NotSupportedException");
        Expect(icall_stream_close(rbw) == 0, "read-only binary writer Close");
        Expect(icall_stream_close(rofs) == 0, "read-only FileStream Close (no-op)");
        icall_file_delete(Cs("binrw_tmp.bin"));
    }

    // net48 ownership: closing the BinaryReader closes the shared stream once
    // (the wrapped MemoryStream goes closed -- ToArray returns null).
    {
        Il2CppObject* cms = NewMemoryStream();
        icall_stream_memory_ctor(cms);
        Il2CppObject* cbw = NewBinaryWriter();
        icall_binary_writer_ctor(cbw, cms);
        icall_binary_write_int32(cbw, 7);
        Expect(icall_stream_close(cbw) == 0, "closing the BinaryWriter closes the shared stream");
        Expect(icall_stream_memory_to_array(cms) == nullptr,
               "the wrapped MemoryStream is closed after the BinaryWriter closed it");
        Expect(icall_stream_close(cms) == 0, "MemoryStream double-close is a no-op");
    }

    // T4a fixed narrow/ulong widths (parity): byte-exact LE layout +
    // round-trips through the OBJECT wrappers. byte/sbyte = 1 byte,
    // int16/uint16 = 2 bytes LE, uint64 = 8 bytes LE; sbyte/int16
    // sign-extend, ushort zero-extends. Total fixture: 1+1+2+2+8 = 14 bytes.
    {
        Il2CppObject* wms = NewMemoryStream();
        icall_stream_memory_ctor(wms);
        Il2CppObject* ww = NewBinaryWriter();
        icall_binary_writer_ctor(ww, wms);
        icall_binary_write_byte(ww, 0xAB);
        icall_binary_write_sbyte(ww, -2);
        icall_binary_write_int16(ww, 0x0102);
        icall_binary_write_uint16(ww, 0xFEFF);
        icall_binary_write_uint64(ww, 0x0102030405060708ULL);
        Expect(icall_stream_get_length(wms) == 14, "narrow/ulong writes occupy 1+1+2+2+8 = 14 bytes");
        il2cpp::Il2CppArray* wbytes = icall_stream_memory_to_array(wms);
        il2cpp::Il2CppArray* warr = reinterpret_cast<il2cpp::Il2CppArray*>(wbytes);
        Expect(WByte(warr, 0) == 0xAB, "Write(byte) emits the single raw byte");
        Expect(WByte(warr, 1) == 0xFE, "Write(sbyte -2) emits 0xFE (low byte only, never a 4-byte int32)");
        Expect(WByte(warr, 2) == 0x02 && WByte(warr, 3) == 0x01,
               "Write(int16 0x0102) little-endian (02 01)");
        Expect(WByte(warr, 4) == 0xFF && WByte(warr, 5) == 0xFE,
               "Write(uint16 0xFEFF) little-endian (FF FE)");
        Expect(WByte(warr, 6) == 0x08 && WByte(warr, 13) == 0x01,
               "Write(uint64) little-endian (08 .. 01)");
        Expect(icall_stream_set_position(wms, 0) == 0, "narrow/ulong fixture rewinds");
        Il2CppObject* rr = NewBinaryReader();
        icall_binary_reader_ctor(rr, wms);
        Expect(icall_binary_read_byte(rr) == 0xAB, "ReadByte still raw (0xAB)");
        Expect(icall_binary_read_sbyte(rr) == -2, "ReadSByte sign-extends (0xFE -> -2)");
        Expect(icall_binary_read_int16(rr) == 0x0102, "ReadInt16 round-trips");
        Expect(icall_binary_read_uint16(rr) == 0xFEFF, "ReadUInt16 zero-extends (0xFEFF)");
        Expect(icall_binary_read_uint64(rr) == 0x0102030405060708ULL, "ReadUInt64 round-trips");
        Expect(icall_stream_close(rr) == 0, "narrow/ulong reader Close");
        Expect(icall_stream_close(wms) == 0, "narrow/ulong stream Close (no-op)");
    }

    // --- 18c. T4b decimal / array / char / ReadChar (parity) ---
    // Decimal emits GetBits order (lo, mid, hi, flags -- 16 bytes LE);
    // byte[] writes RAW bytes; char/char[] encode UTF-8 (pairing within the
    // written range); ReadChar decodes one scalar (astral splits across two
    // calls, malformed -> U+FFFD, EOF -> 0).
    {
        Il2CppObject* dms = NewMemoryStream();
        icall_stream_memory_ctor(dms);
        Il2CppObject* dw = NewBinaryWriter();
        icall_binary_writer_ctor(dw, dms);
        // 1.50m: mantissa 150, scale 2 -> flags 0x00020000.
        Il2CppDecimal onefifty;
        onefifty.flags = 0x00020000;
        onefifty.hi32 = 0;
        onefifty.lo64 = 150;
        icall_binary_write_decimal(dw, &onefifty);
        // -2m: flags sign bit, mantissa 2.
        Il2CppDecimal negtwo;
        negtwo.flags = static_cast<int32_t>(0x80000000);
        negtwo.hi32 = 0;
        negtwo.lo64 = 2;
        icall_binary_write_decimal(dw, &negtwo);
        Expect(icall_stream_get_length(dms) == 32, "two decimal writes occupy 16 bytes each");
        il2cpp::Il2CppArray* dbytes = icall_stream_memory_to_array(dms);
        il2cpp::Il2CppArray* darr = reinterpret_cast<il2cpp::Il2CppArray*>(dbytes);
        Expect(WByte(darr, 0) == 0x96 && WByte(darr, 1) == 0x00 && WByte(darr, 2) == 0x00 && WByte(darr, 3) == 0x00,
               "decimal lo 150 little-endian (96 00 00 00)");
        Expect(WByte(darr, 12) == 0x00 && WByte(darr, 13) == 0x00 && WByte(darr, 14) == 0x02 && WByte(darr, 15) == 0x00,
               "decimal flags scale 2 little-endian (00 00 02 00)");
        Expect(WByte(darr, 16) == 0x02 && WByte(darr, 28) == 0x00 && WByte(darr, 31) == 0x80,
               "second decimal lo=2 with sign-bit flags (02 .. 00 00 00 80)");
        Expect(icall_stream_set_position(dms, 0) == 0, "decimal fixture rewinds");
        Il2CppObject* dr = NewBinaryReader();
        icall_binary_reader_ctor(dr, dms);
        Il2CppDecimal back150;
        icall_binary_read_decimal(dr, &back150);
        Expect(back150.flags == 0x00020000 && back150.hi32 == 0 && back150.lo64 == 150,
               "ReadDecimal round-trips 1.50m (flags/hi/lo)");
        Il2CppDecimal backneg;
        icall_binary_read_decimal(dr, &backneg);
        Expect(backneg.flags == static_cast<int32_t>(0x80000000) && backneg.hi32 == 0 && backneg.lo64 == 2,
               "ReadDecimal round-trips -2m (sign bit preserved)");
        Expect(ThrowsDotNet([&] { icall_binary_write_decimal(dw, nullptr); }, kArgNullExceptionChain),
               "Write(decimal null slot) throws ArgumentNullException");
        Expect(icall_stream_close(dr) == 0, "decimal reader Close");
        Expect(icall_stream_close(dms) == 0, "decimal stream Close (no-op)");
    }
    // Truncated decimal read -> the zero decimal (M3 zero-value convention).
    {
        Il2CppObject* tms = NewMemoryStream();
        icall_stream_memory_ctor(tms);
        Il2CppObject* tw = NewBinaryWriter();
        icall_binary_writer_ctor(tw, tms);
        icall_binary_write_byte(tw, 0xAA);
        icall_binary_write_byte(tw, 0xBB);
        icall_binary_write_byte(tw, 0xCC);
        Expect(icall_stream_set_position(tms, 0) == 0, "truncated decimal fixture rewinds");
        Il2CppObject* tr = NewBinaryReader();
        icall_binary_reader_ctor(tr, tms);
        Il2CppDecimal zero;
        zero.flags = 1;
        zero.hi32 = 1;
        zero.lo64 = 1;
        icall_binary_read_decimal(tr, &zero);
        Expect(zero.flags == 0 && zero.hi32 == 0 && zero.lo64 == 0,
               "ReadDecimal on a 3-byte stream yields the zero decimal");
        Expect(icall_stream_close(tr) == 0, "truncated decimal reader Close");
        Expect(icall_stream_close(tms) == 0, "truncated decimal stream Close (no-op)");
    }
    // byte[] / char[] / char writes + ReadChar round-trips.
    {
        Il2CppObject* ams = NewMemoryStream();
        icall_stream_memory_ctor(ams);
        Il2CppObject* aw = NewBinaryWriter();
        icall_binary_writer_ctor(aw, ams);
        Il2CppArray* bytes = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* bp = reinterpret_cast<uint8_t*>(bytes->data);
        bp[0] = 0xDE; bp[1] = 0xAD; bp[2] = 0xBE; bp[3] = 0xEF;
        icall_binary_write_bytes_full(aw, bytes);
        icall_binary_write_bytes(aw, bytes, 1, 2); // AD BE subrange
        Expect(icall_stream_get_length(ams) == 6, "byte[] full (4) + range (2) occupy 6 raw bytes");
        il2cpp::Il2CppArray* abytes = icall_stream_memory_to_array(ams);
        il2cpp::Il2CppArray* aarr = reinterpret_cast<il2cpp::Il2CppArray*>(abytes);
        Expect(WByte(aarr, 0) == 0xDE && WByte(aarr, 3) == 0xEF && WByte(aarr, 4) == 0xAD && WByte(aarr, 5) == 0xBE,
               "byte[] writes are raw copies (no length prefix)");
        Expect(ThrowsDotNet([&] { icall_binary_write_bytes_full(aw, nullptr); }, kArgNullExceptionChain),
               "Write(byte[] null) throws ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_binary_write_bytes(aw, bytes, -1, 2); }, kArgOutOfRangeChain),
               "Write(byte[], -1, 2) throws ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&] { icall_binary_write_bytes(aw, bytes, 3, 2); }, kArgOutOfRangeChain),
               "Write(byte[], 3, 2) throws ArgumentOutOfRangeException");
        // chars: 'h' 'i' + astral pair U+1F600 + lone D800.
        Il2CppArray* chars = il2cpp::il2cpp_array_new(nullptr, 2, false, 5);
        char16_t* cp = reinterpret_cast<char16_t*>(chars->data);
        cp[0] = u'h'; cp[1] = u'i'; cp[2] = 0xD83D; cp[3] = 0xDE00; cp[4] = 0xD800;
        icall_binary_write_chars_full(aw, chars);   // hi + F0 9F 98 80 + EF BF BD
        icall_binary_write_chars(aw, chars, 0, 1);  // 'h' subrange
        icall_binary_write_char(aw, 0x4E2D);        // E4 B8 AD
        icall_binary_write_char(aw, 0xD800);        // lone surrogate -> EF BF BD
        Expect(icall_stream_get_length(ams) == 6 + 2 + 4 + 3 + 1 + 3 + 3,
               "char writes occupy hi(2) + astral(4) + lone(3) + h(1) + BMP(3) + lone(3)");
        Expect(icall_stream_set_position(ams, 6) == 0, "char fixture rewinds past the byte[] record");
        Il2CppObject* ar = NewBinaryReader();
        icall_binary_reader_ctor(ar, ams);
        Expect(icall_binary_read_char(ar) == u'h', "ReadChar 'h' round-trips");
        Expect(icall_binary_read_char(ar) == u'i', "ReadChar 'i' round-trips");
        Expect(icall_binary_read_char(ar) == 0xD83D, "ReadChar astral returns the HIGH surrogate first");
        Expect(icall_binary_read_char(ar) == 0xDE00, "ReadChar astral returns the stashed LOW surrogate next");
        Expect(icall_binary_read_char(ar) == 0xFFFD, "ReadChar lone surrogate decodes as U+FFFD");
        Expect(icall_binary_read_char(ar) == u'h', "ReadChar subrange 'h' round-trips");
        Expect(icall_binary_read_char(ar) == 0x4E2D, "ReadChar BMP round-trips");
        Expect(icall_binary_read_char(ar) == 0xFFFD, "ReadChar single-lone-surrogate write decodes as U+FFFD");
        Expect(icall_binary_read_char(ar) == 0, "ReadChar at EOF returns 0");
        Expect(ThrowsDotNet([&] { icall_binary_write_chars_full(aw, nullptr); }, kArgNullExceptionChain),
               "Write(char[] null) throws ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_binary_write_chars(aw, chars, 4, 2); }, kArgOutOfRangeChain),
               "Write(chars, 4, 2) throws ArgumentOutOfRangeException");
        Expect(icall_stream_close(ar) == 0, "char reader Close");
        Expect(icall_stream_close(ams) == 0, "char stream Close (no-op)");
    }
    // ReadChar malformed input: C3 28 -> U+FFFD consuming only the lead,
    // so the next ReadChar observes '('.
    {
        Il2CppObject* mms = NewMemoryStream();
        icall_stream_memory_ctor(mms);
        Il2CppObject* mw = NewBinaryWriter();
        icall_binary_writer_ctor(mw, mms);
        icall_binary_write_byte(mw, 0xC3);
        icall_binary_write_byte(mw, 0x28);
        Expect(icall_stream_set_position(mms, 0) == 0, "malformed fixture rewinds");
        Il2CppObject* mr = NewBinaryReader();
        icall_binary_reader_ctor(mr, mms);
        Expect(icall_binary_read_char(mr) == 0xFFFD, "ReadChar malformed lead decodes as U+FFFD");
        Expect(icall_binary_read_char(mr) == 0x28, "ReadChar after malformed lead observes the unconsumed byte");
        Expect(icall_stream_close(mr) == 0, "malformed reader Close");
        Expect(icall_stream_close(mms) == 0, "malformed stream Close (no-op)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d binaryio assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime binaryio tests passed\n");
    return 0;
}
