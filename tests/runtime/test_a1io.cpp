//------------------------------------------------------------------------------
// test_a1io.cpp -- A1 io closed-loop regression tests (ioaudit2 / ioaudit4 /
// ioext roots). See the a1reds report for the defect evidence.
//
// Coverage (normal + edge + error paths):
//   1. StreamReader/FileStream ctor error chains expose System.IO.IOException:
//      a missing Open throws FileNotFoundException WITH the IOException base
//      (ioaوندit2: `catch (IOException)` must match), and CreateNew on an
//      existing file throws an IOException-chained DotNetException.
//   2. File.ReadAllText / ReadAllLines BOM detection (ioaudit4): UTF-8 BOM,
//      UTF-16LE BOM, UTF-16BE BOM, and plain UTF-8 (no BOM). The UTF-16
//      branches must transcode the LE/BE units to UTF-8 (the pre-fix code
//      decoded the LE bytes as UTF-8 and returned "A\0B\0").
//   3. StreamReader.ReadToEnd after the ctor BOM peek (ioext rte_read): the
//      peeked bytes pushed back onto a MemoryStream (and a FileStream without
//      a BOM) must replay in front of the backend remainder.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness; test artifacts are relative-path files removed at the end.
//------------------------------------------------------------------------------

#include "streamio.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h" // DotNetException carrier (ThrowsDotNet)
#include "il2cpp_io.h"
#include "il2cpp_runtime.h"
#include "il2cpp_streamio.h" // the stream OBJECT model + System_IO_* descs
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

// Managed string <-> explicit UTF-16 unit comparison helper (surrogate edge
// assertions: the oracle values live in evidence-a1surro-oracle.out).
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

// Build a managed byte[] from 'n' bytes (test input helper).
il2cpp::Il2CppArray* ByteArray(const char* bytes, int32_t n)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 1, false, n);
    for (int32_t i = 0; i < n; ++i)
    {
        arr->data[i] = bytes[i];
    }
    return arr;
}

// Allocate a fresh stream object through the runtime descriptor (the same
// shape generated newobj emits -- the ctor icall then arms the handle).
il2cpp::Il2CppObject* NewFileStream()
{
    return il2cpp::il2cpp_object_new(&System_IO_FileStream_class);
}
il2cpp::Il2CppObject* NewMemoryStream()
{
    return il2cpp::il2cpp_object_new(&System_IO_MemoryStream_class);
}
il2cpp::Il2CppObject* NewStreamReader()
{
    return il2cpp::il2cpp_object_new(&System_IO_StreamReader_class);
}
il2cpp::Il2CppObject* NewStreamWriter()
{
    return il2cpp::il2cpp_object_new(&System_IO_StreamWriter_class);
}

// Run 'fn' and report whether it threw a DotNetException whose chain exactly
// matches 'expectedChain' (test_stringrwobj.cpp helper convention).
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

// Run 'fn' and report whether it threw a DotNetException whose chain string
// contains 'needle' (the catch-clause compat check).
template <typename Fn>
bool ThrowsChainContains(Fn&& fn, const char* needle)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), needle) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Write raw bytes to 'path' (BOM fixtures; fclose flushes to the OS).
bool WriteRawBytes(const char* path, const char* bytes, size_t n)
{
    std::FILE* fp = std::fopen(path, "wb");
    if (fp == nullptr)
    {
        return false;
    }
    const size_t wrote = std::fwrite(bytes, 1, n, fp);
    return std::fclose(fp) == 0 && wrote == n;
}

// Managed string[] element accessor (test_io.cpp convention).
il2cpp::Il2CppObject* ArrayElem(il2cpp::Il2CppObject* arrObj, int32_t index)
{
    if (arrObj == nullptr)
    {
        return nullptr;
    }
    il2cpp::Il2CppArray* arr = reinterpret_cast<il2cpp::Il2CppArray*>(arrObj);
    if (index < 0 || index >= arr->length)
    {
        return nullptr;
    }
    return *reinterpret_cast<il2cpp::Il2CppObject**>(
        il2cpp::il2cpp_array_get_elem_ptr(arr, index));
}

int32_t ArrayLen(il2cpp::Il2CppObject* arrObj)
{
    if (arrObj == nullptr)
    {
        return -1;
    }
    return reinterpret_cast<il2cpp::Il2CppArray*>(arrObj)->length;
}

const char kFileNotFoundChainIo[] =
    "System.IO.FileNotFoundException,System.IO.FileNotFoundException,System.IO.IOException,System.SystemException,System.Exception";

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    // --- 1. Ctor error chains expose System.IO.IOException (ioaوندit2) ------
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("a1io_missing.bin"), 3, 1); },
                        kFileNotFoundChainIo),
           "missing Open throws FileNotFoundException with the IOException base");

    Expect(icall_file_write_all_text(Cs("a1io_cn.bin"), Cs("x")) == 0,
           "CreateNew fixture file created");
    Expect(ThrowsChainContains([&] { icall_stream_file_ctor(NewFileStream(), Cs("a1io_cn.bin"), 1, 2); },
                               "System.IO.IOException"),
           "CreateNew on an existing file throws an IOException-chained DotNetException");

    // --- 2. File text APIs: BOM detection (ioaudit4) ------------------------
    // 2a. UTF-16LE BOM (FF FE) -> "AB" (the pre-fix result was "A\0B\0").
    Expect(WriteRawBytes("a1io_u16le.bin", "\xFF\xFE\x41\x00\x42\x00", 6),
           "UTF-16LE fixture written");
    Il2CppObject* le = icall_file_read_all_text(Cs("a1io_u16le.bin"));
    Expect(le != nullptr && StrEquals(le, "AB"),
           "ReadAllText decodes a UTF-16LE BOM stream to \"AB\"");

    // 2b. UTF-16LE lines: "A\r\nB" -> ["A", "B"] (BOM stripped, CRLF split).
    Expect(WriteRawBytes("a1io_u16le_lines.bin",
                         "\xFF\xFE\x41\x00\x0D\x00\x0A\x00\x42\x00", 10),
           "UTF-16LE lines fixture written");
    Il2CppObject* leLines = icall_file_read_all_lines(Cs("a1io_u16le_lines.bin"));
    Expect(ArrayLen(leLines) == 2 && StrEquals(ArrayElem(leLines, 0), "A")
               && StrEquals(ArrayElem(leLines, 1), "B"),
           "ReadAllLines decodes a UTF-16LE BOM stream into [A, B]");

    // 2c. UTF-16BE BOM (FE FF) -> "AB".
    Expect(WriteRawBytes("a1io_u16be.bin", "\xFE\xFF\x00\x41\x00\x42", 6),
           "UTF-16BE fixture written");
    Il2CppObject* be = icall_file_read_all_text(Cs("a1io_u16be.bin"));
    Expect(be != nullptr && StrEquals(be, "AB"),
           "ReadAllText decodes a UTF-16BE BOM stream to \"AB\"");

    // 2d. UTF-8 BOM (EF BB BF) is stripped.
    Expect(WriteRawBytes("a1io_u8bom.bin", "\xEF\xBB\xBF\x41\x42", 5),
           "UTF-8 BOM fixture written");
    Il2CppObject* u8 = icall_file_read_all_text(Cs("a1io_u8bom.bin"));
    Expect(u8 != nullptr && StrEquals(u8, "AB"),
           "ReadAllText strips a UTF-8 BOM");

    // 2e. No BOM: plain UTF-8 passes through.
    Expect(WriteRawBytes("a1io_plain.bin", "\x41\x42", 2),
           "plain fixture written");
    Il2CppObject* plain = icall_file_read_all_text(Cs("a1io_plain.bin"));
    Expect(plain != nullptr && StrEquals(plain, "AB"),
           "ReadAllText passes plain UTF-8 through");

    // --- 2f. BOM edge inputs (oracle: evidence-a1edge-ref.out) --------------
    Expect(WriteRawBytes("a1io_empty.bin", "", 0), "empty fixture written");
    Il2CppObject* empty = icall_file_read_all_text(Cs("a1io_empty.bin"));
    Expect(empty != nullptr && StrEquals(empty, ""), "ReadAllText of an empty file is \"\"");
    Il2CppObject* emptyLines = icall_file_read_all_lines(Cs("a1io_empty.bin"));
    Expect(ArrayLen(emptyLines) == 0, "ReadAllLines of an empty file is []");

    Expect(WriteRawBytes("a1io_u8bom_only.bin", "\xEF\xBB\xBF", 3), "u8 BOM-only fixture");
    Il2CppObject* u8Only = icall_file_read_all_text(Cs("a1io_u8bom_only.bin"));
    Expect(u8Only != nullptr && StrEquals(u8Only, ""), "ReadAllText of a UTF-8 BOM-only file is \"\"");

    Expect(WriteRawBytes("a1io_u16le_bom_only.bin", "\xFF\xFE", 2), "u16le BOM-only fixture");
    Il2CppObject* leOnly = icall_file_read_all_text(Cs("a1io_u16le_bom_only.bin"));
    Expect(leOnly != nullptr && StrEquals(leOnly, ""), "ReadAllText of a UTF-16LE BOM-only file is \"\"");

    Expect(WriteRawBytes("a1io_u16be_bom_only.bin", "\xFE\xFF", 2), "u16be BOM-only fixture");
    Il2CppObject* beOnly = icall_file_read_all_text(Cs("a1io_u16be_bom_only.bin"));
    Expect(beOnly != nullptr && StrEquals(beOnly, ""), "ReadAllText of a UTF-16BE BOM-only file is \"\"");

    // Odd-length UTF-16: the trailing half unit is dropped (oracle "A").
    Expect(WriteRawBytes("a1io_u16le_odd.bin", "\xFF\xFE\x41\x00\x42", 5), "u16le odd fixture");
    Il2CppObject* leOdd = icall_file_read_all_text(Cs("a1io_u16le_odd.bin"));
    Expect(leOdd != nullptr && StrEquals(leOdd, "A"), "ReadAllText drops the odd trailing UTF-16LE byte");
    Expect(WriteRawBytes("a1io_u16be_odd.bin", "\xFE\xFF\x00\x41\x42", 5), "u16be odd fixture");
    Il2CppObject* beOdd = icall_file_read_all_text(Cs("a1io_u16be_odd.bin"));
    Expect(beOdd != nullptr && StrEquals(beOdd, "A"), "ReadAllText drops the odd trailing UTF-16BE byte");

    // Surrogate matrix, net48 flush semantics (a1surro oracle): trailing high
    // dropped; unpaired high/low -> U+FFFD; a valid pair passes through.
    {
        const char16_t highOnly[] = { 0xD800 };
        const char16_t lowOnly[] = { 0xFFFD };
        const char16_t highA[] = { 0xFFFD, 0x0041 };
        const char16_t aHigh[] = { 0x0041 };
        const char16_t highHigh[] = { 0xFFFD };
        const char16_t lowLow[] = { 0xFFFD, 0xFFFD };
        const char16_t highLow[] = { 0xD800, 0xDC00 };
        const char16_t lowHigh[] = { 0xFFFD };
        const char16_t highALow[] = { 0xFFFD, 0x0041, 0xFFFD };
        Expect(WriteRawBytes("a1io_s_high.bin", "\xFF\xFE\x00\xD8", 4), "surro high fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_high.bin")), highOnly, 0),
               "lone trailing high surrogate is dropped");
        Expect(WriteRawBytes("a1io_s_low.bin", "\xFF\xFE\x00\xDC", 4), "surro low fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_low.bin")), lowOnly, 1),
               "lone low surrogate -> U+FFFD");
        Expect(WriteRawBytes("a1io_s_highA.bin", "\xFF\xFE\x00\xD8\x41\x00", 6), "surro highA fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_highA.bin")), highA, 2),
               "high followed by non-low -> U+FFFD + next");
        Expect(WriteRawBytes("a1io_s_Ahigh.bin", "\xFF\xFE\x41\x00\x00\xD8", 6), "surro Ahigh fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_Ahigh.bin")), aHigh, 1),
               "trailing high after content is dropped");
        Expect(WriteRawBytes("a1io_s_highhigh.bin", "\xFF\xFE\x00\xD8\x00\xD8", 6), "surro highhigh fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_highhigh.bin")), highHigh, 1),
               "high+high -> one U+FFFD");
        Expect(WriteRawBytes("a1io_s_lowlow.bin", "\xFF\xFE\x00\xDC\x00\xDC", 6), "surro lowlow fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_lowlow.bin")), lowLow, 2),
               "low+low -> U+FFFD + U+FFFD");
        Expect(WriteRawBytes("a1io_s_highlow.bin", "\xFF\xFE\x00\xD8\x00\xDC", 6), "surro highlow fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_highlow.bin")), highLow, 2),
               "valid surrogate pair passes through");
        Expect(WriteRawBytes("a1io_s_lowhigh.bin", "\xFF\xFE\x00\xDC\x00\xD8", 6), "surro lowhigh fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_lowhigh.bin")), lowHigh, 1),
               "low then trailing high -> one U+FFFD");
        Expect(WriteRawBytes("a1io_s_highAlow.bin", "\xFF\xFE\x00\xD8\x41\x00\x00\xDC", 8), "surro highAlow fixture");
        Expect(StrEquals16(icall_file_read_all_text(Cs("a1io_s_highAlow.bin")), highALow, 3),
               "high + A + low -> U+FFFD + A + U+FFFD");
    }

    // Embedded NUL is content (net48): the write path must emit all 3 bytes
    // and the read path must keep them (a1edge nul.text probe).
    {
        const char16_t nulContent[] = { 0x0041, 0x0000, 0x0042 };
        Il2CppObject* s = reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nulContent, 3));
        Expect(icall_file_write_all_text(Cs("a1io_nul.bin"), s) == 0,
               "WriteAllText NUL fixture written");
        std::FILE* fp = std::fopen("a1io_nul.bin", "rb");
        char raw[8] = { 0 };
        const size_t rawN = (fp != nullptr) ? std::fread(raw, 1, sizeof(raw), fp) : 0;
        if (fp != nullptr)
        {
            std::fclose(fp);
        }
        Expect(rawN == 3 && raw[0] == 0x41 && raw[1] == 0x00 && raw[2] == 0x42,
               "WriteAllText keeps the embedded NUL bytes");
        Il2CppObject* back = icall_file_read_all_text(Cs("a1io_nul.bin"));
        Expect(StrEquals16(back, nulContent, 3), "ReadAllText keeps embedded NULs");
    }

    // --- 3. ReadToEnd replays the ctor BOM peek (ioext rte_read) ------------
    // 3a. MemoryStream: writer commits the lines, the reader ctor peeks 3
    //     bytes ('h','e','l' -- no BOM) and must push them back.
    Il2CppObject* ms = NewMemoryStream();
    icall_stream_memory_ctor(ms);
    Il2CppObject* msw = NewStreamWriter();
    icall_stream_writer_ctor(msw, ms);
    Expect(icall_stream_write_line(msw, Cs("hello")) == 0, "memory WriteLine one");
    Expect(icall_stream_write_line(msw, Cs("world")) == 0, "memory WriteLine two");
    Expect(icall_stream_flush(msw) == 0, "memory writer Flush");
    Expect(icall_stream_set_position(ms, 0) == 0, "memory rewind to 0");
    Il2CppObject* msr = NewStreamReader();
    icall_stream_reader_ctor(msr, ms);
    Il2CppObject* mAll = icall_stream_read_to_end(msr);
    Expect(mAll != nullptr && StrEquals(mAll, "hello\r\nworld\r\n"),
           "MemoryStream ReadToEnd returns the full CRLF text after the BOM peek");
    Expect(icall_stream_close(msr) == 0, "memory reader Close");
    Expect(icall_stream_close(msw) == 0, "memory writer Close");
    Expect(icall_stream_close(ms) == 0, "memory stream Close (no-op at zero refs)");

    // 3b. FileStream without a BOM: the pushback must replay for the file
    //     backend too (read_to_end's fread path bypasses StreamGetByte).
    Expect(icall_file_write_all_text(Cs("a1io_rte.txt"), Cs("hello\r\nworld\r\n")) == 0,
           "rte file fixture written");
    Il2CppObject* fr = NewFileStream();
    icall_stream_file_ctor(fr, Cs("a1io_rte.txt"), 3, 1); // Open + Read
    Il2CppObject* frr = NewStreamReader();
    icall_stream_reader_ctor(frr, fr);
    Il2CppObject* fAll = icall_stream_read_to_end(frr);
    Expect(fAll != nullptr && StrEquals(fAll, "hello\r\nworld\r\n"),
           "FileStream ReadToEnd returns the full text after the BOM peek");
    Expect(icall_stream_close(frr) == 0, "rte file reader Close");
    Expect(icall_stream_close(fr) == 0, "rte file Close (no-op)");

    // --- 3c. ReadToEnd ring boundaries on MemoryStream ----------------------
    // Empty stream: ctor peek hits EOF, ReadToEnd returns "" and stays "".
    {
        Il2CppObject* ms0 = NewMemoryStream();
        icall_stream_memory_ctor(ms0);
        Il2CppObject* r0 = NewStreamReader();
        icall_stream_reader_ctor(r0, ms0);
        Il2CppObject* a0 = icall_stream_read_to_end(r0);
        Expect(a0 != nullptr && StrEquals(a0, ""), "empty stream ReadToEnd is \"\"");
        Il2CppObject* b0 = icall_stream_read_to_end(r0);
        Expect(b0 != nullptr && StrEquals(b0, ""), "second ReadToEnd at EOF is \"\"");
        Expect(icall_stream_close(r0) == 0, "empty reader Close");
        Expect(icall_stream_close(ms0) == 0, "empty stream Close");
    }
    // 1/2/3-byte remainders: the ctor peek pushes 1..3 bytes and pos == length
    // (drain-only path); 4 bytes exercises the kMemory append after the drain.
    {
        struct SzCase { const char* bytes; int32_t n; const char* expect; };
        const SzCase cases[] = {
            { "x", 1, "x" }, { "xy", 2, "xy" }, { "xyz", 3, "xyz" }, { "xyzw", 4, "xyzw" },
        };
        for (size_t ci = 0; ci < sizeof(cases) / sizeof(cases[0]); ++ci)
        {
            il2cpp::Il2CppObject* ms = NewMemoryStream();
            icall_stream_memory_ctor(ms);
            icall_stream_write_bytes(ms, ByteArray(cases[ci].bytes, cases[ci].n), 0, cases[ci].n);
            Expect(icall_stream_get_length(ms) == cases[ci].n,
                   "ring-boundary fixture write");
            Expect(icall_stream_set_position(ms, 0) == 0, "ring-boundary rewind");
            Il2CppObject* r = NewStreamReader();
            icall_stream_reader_ctor(r, ms);
            Il2CppObject* all = icall_stream_read_to_end(r);
            Expect(all != nullptr && StrEquals(all, cases[ci].expect),
                   "ReadToEnd replays the pushed-back remainder");
            Il2CppObject* eof = icall_stream_read_to_end(r);
            Expect(eof != nullptr && StrEquals(eof, ""), "ReadToEnd EOF after boundary case is \"\"");
            Expect(icall_stream_close(r) == 0, "ring-boundary reader Close");
            Expect(icall_stream_close(ms) == 0, "ring-boundary stream Close");
        }
    }

    // --- Cleanup ------------------------------------------------------------
    std::remove("a1io_cn.bin");
    std::remove("a1io_u16le.bin");
    std::remove("a1io_u16le_lines.bin");
    std::remove("a1io_u16be.bin");
    std::remove("a1io_u8bom.bin");
    std::remove("a1io_plain.bin");
    std::remove("a1io_rte.txt");
    std::remove("a1io_empty.bin");
    std::remove("a1io_u8bom_only.bin");
    std::remove("a1io_u16le_bom_only.bin");
    std::remove("a1io_u16be_bom_only.bin");
    std::remove("a1io_u16le_odd.bin");
    std::remove("a1io_u16be_odd.bin");
    std::remove("a1io_s_high.bin");
    std::remove("a1io_s_low.bin");
    std::remove("a1io_s_highA.bin");
    std::remove("a1io_s_Ahigh.bin");
    std::remove("a1io_s_highhigh.bin");
    std::remove("a1io_s_lowlow.bin");
    std::remove("a1io_s_highlow.bin");
    std::remove("a1io_s_lowhigh.bin");
    std::remove("a1io_s_highAlow.bin");

    if (g_failures != 0)
    {
        std::printf("%d a1io assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all a1io tests passed\n");
    return 0;
}
