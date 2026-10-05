//------------------------------------------------------------------------------
// test_a1ringx.cpp -- A1/A6 pushback-ring interaction regression (a1ringx):
// after a StreamReader ctor BOM peek pushes bytes into the shared ring, a raw
// BinaryReader-style read (icall_stream_read_bytes on the same StreamState)
// must replay the ring bytes FIRST, and a later ReadToEnd must return exactly
// the remaining text -- no byte loss, duplication or reorder. net48 reference:
// EOF evidence-a1ringx-ref.out (i2=6C6C6568 "hell", rest="o\r\nworld\r\n").
//------------------------------------------------------------------------------

#include "streamio.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"
#include "il2cpp_streamio.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

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

il2cpp::Il2CppObject* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

il2cpp::Il2CppArray* ByteArray(const char* bytes, int32_t n)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 1, false, n);
    for (int32_t i = 0; i < n; ++i)
    {
        arr->data[i] = bytes[i];
    }
    return arr;
}

int32_t ByteAt(il2cpp::Il2CppArray* arr, int32_t i)
{
    return static_cast<unsigned char>(arr->data[i]);
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    Il2CppObject* ms = il2cpp::il2cpp_object_new(&System_IO_MemoryStream_class);
    icall_stream_memory_ctor(ms);
    Il2CppObject* w = il2cpp::il2cpp_object_new(&System_IO_StreamWriter_class);
    icall_stream_writer_ctor(w, ms);
    Expect(icall_stream_write_line(w, Cs("hello")) == 0, "fixture WriteLine hello");
    Expect(icall_stream_write_line(w, Cs("world")) == 0, "fixture WriteLine world");
    Expect(icall_stream_flush(w) == 0, "fixture Flush");
    Expect(icall_stream_set_position(ms, 0) == 0, "fixture rewind");

    // StreamReader ctor peeks 3 bytes ("hel") into the pushback ring.
    Il2CppObject* sr = il2cpp::il2cpp_object_new(&System_IO_StreamReader_class);
    icall_stream_reader_ctor(sr, ms);

    // Raw read on the SAME shared state must replay the ring first: the first
    // 4 bytes are "hell" (net48 BinaryReader.ReadInt32 = 0x6C6C6568).
    Il2CppArray* raw = ByteArray("\0\0\0\0", 4);
    Expect(icall_stream_read_bytes(ms, raw, 0, 4) == 4, "ring-aware raw read returns 4 bytes");
    Expect(ByteAt(raw, 0) == 'h' && ByteAt(raw, 1) == 'e'
               && ByteAt(raw, 2) == 'l' && ByteAt(raw, 3) == 'l',
           "raw read replays the peeked ring bytes first (\"hell\")");

    // The text reader then returns exactly the remainder ("o\r\nworld\r\n").
    Il2CppObject* rest = icall_stream_read_to_end(sr);
    Expect(rest != nullptr && StrEquals(rest, "o\r\nworld\r\n"),
           "ReadToEnd after the raw read returns the exact remainder");

    // Reverse order: raw read first (no ring yet), then reader ctor + read.
    Il2CppObject* ms2 = il2cpp::il2cpp_object_new(&System_IO_MemoryStream_class);
    icall_stream_memory_ctor(ms2);
    Il2CppObject* w2 = il2cpp::il2cpp_object_new(&System_IO_StreamWriter_class);
    icall_stream_writer_ctor(w2, ms2);
    Expect(icall_stream_write_line(w2, Cs("hello")) == 0, "fixture2 WriteLine hello");
    Expect(icall_stream_write_line(w2, Cs("world")) == 0, "fixture2 WriteLine world");
    Expect(icall_stream_flush(w2) == 0, "fixture2 Flush");
    Expect(icall_stream_set_position(ms2, 0) == 0, "fixture2 rewind");
    Il2CppArray* raw2 = ByteArray("\0\0\0\0", 4);
    Expect(icall_stream_read_bytes(ms2, raw2, 0, 4) == 4, "fixture2 raw read");
    Expect(ByteAt(raw2, 0) == 'h' && ByteAt(raw2, 3) == 'l', "fixture2 raw bytes");
    Il2CppObject* sr2 = il2cpp::il2cpp_object_new(&System_IO_StreamReader_class);
    icall_stream_reader_ctor(sr2, ms2);
    Il2CppObject* rest2 = icall_stream_read_to_end(sr2);
    Expect(rest2 != nullptr && StrEquals(rest2, "o\r\nworld\r\n"),
           "reverse order: ReadToEnd returns the exact remainder");

    if (g_failures != 0)
    {
        std::printf("%d a1ringx assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all a1ringx tests passed\n");
    return 0;
}
