//------------------------------------------------------------------------------
// test_streamio.cpp -- System.IO stream icall runtime unit tests (FileStream
// / StreamReader / StreamWriter / MemoryStream subset). See streamio.h for
// the icall contract under test.
//
// Coverage (normal + edge + error paths):
//   1. FileStream write -> read back : mode-1 open, write, flush, close,
//      then mode-0 open, read, EOF; raw content cross-checked through
//      File.ReadAllText (il2cpp_io.h).
//   2. Mixed read/write : mode-3 open, overwrite prefix, seek, read windows.
//   3. read_line / write_line : CRLF round-trip incl. empty line and EOF
//      (nullptr), capability violations, UTF-8 (non-ASCII) round-trip.
//   4. MemoryStream round-trip : write, seek, read, grow-past-end, ToArray
//      (managed byte[]), close.
//   5. Position movement : set/get position and length on file + memory.
//   6. Error paths : null stream/buffer, negative counts, missing file,
//      missing directory, wrong-direction capability reads.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal. All
// test artifacts live under the relative directory "st_tmp" (created fresh
// in the working directory and removed at the end).
//------------------------------------------------------------------------------

#include "streamio.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"  // DotNetException carrier (ThrowsDotNet)
#include "il2cpp_io.h"
#include "il2cpp_runtime.h"    // il2cpp_object_new / il2cpp_class_by_name /
                               // il2cpp_is_instance
#include "il2cpp_streamio.h"   // the stream OBJECT model (this file's surface)
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
    #include <direct.h> // ::_rmdir
    #define IL2CPP_TEST_RMDIR(p) ::_rmdir(p)
#else
    #include <unistd.h> // ::rmdir
    #define IL2CPP_TEST_RMDIR(p) ::rmdir(p)
#endif

namespace {

static_assert(sizeof(void*) == 8, "streamio test fixture assumes 8-byte pointers");

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

// Managed string <-> explicit UTF-16 unit comparison helper (non-ASCII
// round-trips: the written and read strings must match unit for unit).
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

// Byte-pattern comparison helper: does the first 'n' elements of the managed
// byte[] equal the given bytes?
bool BytesEqual(il2cpp::Il2CppObject* arrObj, const char* bytes, int32_t n)
{
    if (arrObj == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppArray* arr = reinterpret_cast<il2cpp::Il2CppArray*>(arrObj);
    if (arr->length != n)
    {
        return false;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        if (arr->data[i] != bytes[i])
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

// Run 'fn' and report whether it threw a DotNetException whose type string
// (with base chain) exactly matches 'expectedChain' (test_stringrwobj.cpp
// helper convention).
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
const char kArgOutOfRangeChain[] =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
const char kArgumentChain[] =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char kNullRefChain[] =
    "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";
const char kNotSupportedChain[] =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
const char kInvalidOperationChain[] =
    "System.InvalidOperationException,System.InvalidOperationException,System.SystemException,System.Exception";
const char kFileNotFoundChain[] =
    "System.IO.FileNotFoundException,System.IO.FileNotFoundException,System.IO.IOException,System.SystemException,System.Exception";
const char kIOExceptionChain[] =
    "System.IO.IOException,System.IO.IOException,System.SystemException,System.Exception";

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    // --- 0. Prepare the test directory (all artifacts live under st_tmp) ---
    Expect(icall_directory_create_directory(Cs("st_tmp")) == 0,
           "Directory.CreateDirectory creates the test directory");

    // --- 1. FileStream: write -> flush -> close -> read back ----------------
    il2cpp_stream_t w = il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 1);
    Expect(w != nullptr, "FileStream open (write mode) succeeds");
    Expect(il2cpp_stream_get_can_write(w) == 1, "write-mode stream CanWrite is 1");
    Expect(il2cpp_stream_get_can_read(w) == 0, "write-mode stream CanRead is 0");
    Expect(il2cpp_stream_write(w, "hello stream", 12) == 12,
           "FileStream.Write returns the full byte count");
    Expect(il2cpp_stream_get_position(w) == 12, "FileStream position advances after write");
    Expect(il2cpp_stream_get_length(w) == 12, "FileStream length equals written bytes");
    Expect(il2cpp_stream_read(w, nullptr, 4) == -1, "read on a write-only stream returns -1");
    Expect(il2cpp_stream_read_line(w) == nullptr, "read_line on a write-only stream returns null");
    Expect(il2cpp_stream_flush(w) == 0, "FileStream.Flush returns 0");
    Expect(il2cpp_stream_close(w) == 0, "FileStream.Close returns 0");

    // Raw content cross-check through the File whole-content icall.
    Il2CppObject* raw = icall_file_read_all_text(Cs("st_tmp/data.bin"));
    Expect(raw != nullptr && StrEquals(raw, "hello stream"),
           "written file content matches File.ReadAllText");

    il2cpp_stream_t r = il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 0);
    Expect(r != nullptr, "FileStream open (read mode) succeeds");
    Expect(il2cpp_stream_get_can_read(r) == 1, "read-mode stream CanRead is 1");
    Expect(il2cpp_stream_get_can_write(r) == 0, "read-mode stream CanWrite is 0");
    char buf[64];
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(r, buf, 12) == 12, "FileStream.Read returns the requested bytes");
    Expect(std::memcmp(buf, "hello stream", 12) == 0, "FileStream read-back matches written bytes");
    Expect(il2cpp_stream_read(r, buf, 12) == 0, "FileStream.Read at end-of-stream returns 0");
    Expect(il2cpp_stream_write(r, "x", 1) == -1, "write on a read-only stream returns -1");
    Expect(il2cpp_stream_read(r, nullptr, 4) == -1, "read with a null buffer returns -1");
    Expect(il2cpp_stream_close(r) == 0, "FileStream.Close (read) returns 0");

    // --- 2. Mixed read/write (mode 3) ---------------------------------------
    il2cpp_stream_t rw = il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 3);
    Expect(rw != nullptr, "FileStream open (read-write mode) succeeds");
    Expect(il2cpp_stream_get_can_read(rw) == 1 && il2cpp_stream_get_can_write(rw) == 1,
           "read-write stream reports both capabilities");
    Expect(il2cpp_stream_write(rw, "ABC", 3) == 3, "read-write stream accepts writes");
    Expect(il2cpp_stream_get_position(rw) == 3, "position advanced to 3 after prefix write");
    Expect(il2cpp_stream_set_position(rw, 0) == 0, "set_position(0) succeeds");
    Expect(il2cpp_stream_get_position(rw) == 0, "get_position reports 0 after seek");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(rw, buf, 3) == 3, "read-write stream accepts reads");
    Expect(std::memcmp(buf, "ABC", 3) == 0, "overwritten prefix reads back as ABC");
    Expect(il2cpp_stream_set_position(rw, 1) == 0, "set_position(1) succeeds");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(rw, buf, 2) == 2 && std::memcmp(buf, "BC", 2) == 0,
           "windowed read at offset 1 returns BC");
    Expect(il2cpp_stream_get_length(rw) == 12,
           "mode-3 writes do not truncate the existing file (length stays 12)");
    Expect(il2cpp_stream_close(rw) == 0, "FileStream.Close (read-write) returns 0");

    // --- 3. read_line / write_line (StreamReader/Writer subset) -------------
    il2cpp_stream_t wl = il2cpp_stream_file_open(Cs("st_tmp/lines.txt"), 1);
    Expect(wl != nullptr, "lines file open (write mode) succeeds");
    Expect(il2cpp_stream_write_line(wl, Cs("one")) == 0, "write_line(\"one\") returns 0");
    Expect(il2cpp_stream_write_line(wl, Cs("")) == 0, "write_line(\"\") returns 0");
    Expect(il2cpp_stream_write_line(wl, Cs("three")) == 0, "write_line(\"three\") returns 0");
    Expect(il2cpp_stream_close(wl) == 0, "lines file close returns 0");

    raw = icall_file_read_all_text(Cs("st_tmp/lines.txt"));
    Expect(raw != nullptr && StrEquals(raw, "one\r\n\r\nthree\r\n"),
           "write_line joins lines with CRLF (Windows semantics)");

    il2cpp_stream_t rl = il2cpp_stream_file_open(Cs("st_tmp/lines.txt"), 0);
    Expect(rl != nullptr, "lines file open (read mode) succeeds");
    Il2CppObject* line = il2cpp_stream_read_line(rl);
    Expect(line != nullptr && StrEquals(line, "one"), "read_line returns the first line");
    line = il2cpp_stream_read_line(rl);
    Expect(line != nullptr && StrEquals(line, ""), "read_line of an empty line returns \"\" (not null)");
    line = il2cpp_stream_read_line(rl);
    Expect(line != nullptr && StrEquals(line, "three"), "read_line returns the last line");
    Expect(il2cpp_stream_read_line(rl) == nullptr, "read_line at end-of-stream returns null");
    Expect(il2cpp_stream_close(rl) == 0, "lines file close (read) returns 0");

    // Non-ASCII (UTF-8) line round-trip: h(0x00E9)llo encodes as 2-byte UTF-8.
    const char16_t nonAscii[] = { u'h', 0x00E9, u'l', u'l', u'o' };
    il2cpp_stream_t wn = il2cpp_stream_file_open(Cs("st_tmp/utf8.txt"), 1);
    Il2CppString* nonAsciiStr = il2cpp_string_new(nonAscii, 5);
    Expect(wn != nullptr && il2cpp_stream_write_line(wn, reinterpret_cast<Il2CppObject*>(nonAsciiStr)) == 0,
           "write_line accepts non-ASCII content");
    Expect(il2cpp_stream_close(wn) == 0, "utf8 file close returns 0");
    il2cpp_stream_t rn = il2cpp_stream_file_open(Cs("st_tmp/utf8.txt"), 0);
    Il2CppObject* nonAsciiBack = il2cpp_stream_read_line(rn);
    Expect(nonAsciiBack != nullptr && StrEquals16(nonAsciiBack, nonAscii, 5),
           "non-ASCII line round-trips unit for unit (UTF-8 encode/decode)");
    Expect(il2cpp_stream_close(rn) == 0, "utf8 file close (read) returns 0");

    // --- 4. MemoryStream round-trip -----------------------------------------
    il2cpp_stream_t m = il2cpp_stream_memory_create();
    Expect(m != nullptr, "MemoryStream create succeeds");
    Expect(il2cpp_stream_get_can_read(m) == 1 && il2cpp_stream_get_can_write(m) == 1,
           "MemoryStream reports both capabilities");
    Expect(il2cpp_stream_get_position(m) == 0 && il2cpp_stream_get_length(m) == 0,
           "fresh MemoryStream has position 0 and length 0");
    Expect(il2cpp_stream_write(m, "abcdef", 6) == 6, "MemoryStream.Write returns the byte count");
    Expect(il2cpp_stream_get_length(m) == 6, "MemoryStream length follows writes");
    Expect(il2cpp_stream_get_position(m) == 6, "MemoryStream position advances");
    Expect(il2cpp_stream_set_position(m, 2) == 0, "MemoryStream set_position succeeds");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(m, buf, 3) == 3 && std::memcmp(buf, "cde", 3) == 0,
           "MemoryStream windowed read returns cde");
    Expect(il2cpp_stream_get_position(m) == 5, "MemoryStream position follows the read");

    Il2CppObject* arr = il2cpp_stream_memory_to_array(m);
    Expect(arr != nullptr && BytesEqual(arr, "abcdef", 6),
           "MemoryStream.ToArray returns the full content (6 bytes)");

    // Overwrite in the middle extends correctly.
    Expect(il2cpp_stream_set_position(m, 4) == 0, "MemoryStream seek to 4 succeeds");
    Expect(il2cpp_stream_write(m, "XY", 2) == 2, "MemoryStream mid-stream write succeeds");
    Expect(il2cpp_stream_get_length(m) == 6, "mid-stream write does not extend the length");
    arr = il2cpp_stream_memory_to_array(m);
    Expect(arr != nullptr && BytesEqual(arr, "abcdXY", 6), "ToArray reflects the overwrite");

    // Reading past the end is a clean EOF.
    Expect(il2cpp_stream_set_position(m, 10) == 0, "seek past the end succeeds");
    Expect(il2cpp_stream_read(m, buf, 3) == 0, "read past the end returns 0");

    // Seeking past the end and writing extends the stream.
    Expect(il2cpp_stream_set_position(m, 20) == 0, "seek to 20 succeeds");
    Expect(il2cpp_stream_write(m, "Z", 1) == 1, "write past the end succeeds");
    Expect(il2cpp_stream_get_length(m) == 21, "write past the end extends the length to 21");
    Expect(il2cpp_stream_flush(m) == 0, "MemoryStream.Flush is a no-op success");
    Expect(il2cpp_stream_close(m) == 0, "MemoryStream.Close returns 0");

    // --- 5. Position movement on files ---------------------------------------
    // (memory-side movement was covered in section 4; file-side seek/read
    // windows were covered in section 2 -- here the length after mode-3
    // truncation-free writes is re-verified through a fresh read handle.)
    il2cpp_stream_t chk = il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 0);
    Expect(chk != nullptr, "position-check handle opens");
    Expect(il2cpp_stream_get_length(chk) == 12, "file length is 12 across handles");
    Expect(il2cpp_stream_set_position(chk, 2) == 0, "file set_position(2) succeeds");
    Expect(il2cpp_stream_get_position(chk) == 2, "file get_position reports 2");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(chk, buf, 1) == 1 && buf[0] == 'C',
           "file read at offset 2 returns the overwritten C");
    Expect(il2cpp_stream_close(chk) == 0, "position-check handle closes");

    // --- 5b. Seek / CanSeek / SetLength (D-STP-2) -- handle layer ------------
    il2cpp_stream_t sk = il2cpp_stream_memory_create();
    Expect(sk != nullptr && il2cpp_stream_get_can_seek(sk) == 1,
           "MemoryStream CanSeek is 1");
    Expect(il2cpp_stream_write(sk, "abcdef", 6) == 6, "seek fixture writes 6 bytes");
    Expect(il2cpp_stream_seek(sk, 2, 0) == 2, "Seek(2, Begin) returns 2");
    Expect(il2cpp_stream_seek(sk, 2, 1) == 4, "Seek(2, Current) returns 4");
    Expect(il2cpp_stream_seek(sk, -2, 2) == 4, "Seek(-2, End) returns 4 (6-2)");
    Expect(il2cpp_stream_get_position(sk) == 4, "position after the Seek chain is 4");
    Expect(il2cpp_stream_seek(sk, 0, 0) == 0, "Seek(0, Begin) returns 0");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(sk, buf, 2) == 2 && std::memcmp(buf, "ab", 2) == 0,
           "read after Seek(0, Begin) returns ab");
    Expect(il2cpp_stream_seek(sk, 100, 0) == 100, "Seek past the end returns 100");
    Expect(il2cpp_stream_write(sk, "Z", 1) == 1, "write after seek-past-end succeeds");
    Expect(il2cpp_stream_get_length(sk) == 101, "seek-past-end write extends length to 101");
    Expect(il2cpp_stream_seek(sk, -5, 0) == -1, "Seek(-5, Begin) returns -1 (before beginning)");
    Expect(il2cpp_stream_seek(sk, 0, 7) == -1, "Seek with an invalid origin returns -1");
    Expect(il2cpp_stream_get_position(sk) == 101, "failed seeks leave the position unchanged");

    // SetLength truncate / extend / position-clamp (memory backend).
    Expect(il2cpp_stream_set_length(sk, 50) == 0, "SetLength(50) truncates the stream");
    Expect(il2cpp_stream_get_length(sk) == 50, "length is 50 after truncation");
    Expect(il2cpp_stream_set_position(sk, 55) == 0, "position 55 past the truncated end");
    Expect(il2cpp_stream_set_length(sk, 40) == 0, "SetLength(40) clamps the position");
    Expect(il2cpp_stream_get_position(sk) == 40, "position clamped to 40 after SetLength");
    Expect(il2cpp_stream_set_length(sk, 46) == 0, "SetLength(46) extends the stream");
    Expect(il2cpp_stream_get_position(sk) == 40, "extension does not move the position");
    Expect(il2cpp_stream_set_length(sk, -3) == -1, "SetLength(-3) returns -1");
    Expect(il2cpp_stream_set_position(sk, 40) == 0, "seek to the extension boundary");
    std::memset(buf, 0x7F, sizeof(buf));
    Expect(il2cpp_stream_read(sk, buf, 6) == 6, "read the extension gap");
    Expect(buf[0] == 0 && buf[1] == 0 && buf[2] == 0 && buf[3] == 0
           && buf[4] == 0 && buf[5] == 0,
           "SetLength extension is zero-filled (D-STP-2)");
    Expect(il2cpp_stream_close(sk) == 0, "memory seek fixture closes");

    // File backend: Seek origins + SetLength truncate/extend + position clamp.
    il2cpp_stream_t fseek = il2cpp_stream_file_open(Cs("st_tmp/seek.bin"), 4); // w+b
    Expect(fseek != nullptr && il2cpp_stream_get_can_seek(fseek) == 1,
           "read-write file CanSeek is 1");
    Expect(il2cpp_stream_write(fseek, "hello", 5) == 5, "seek file writes 5 bytes");
    Expect(il2cpp_stream_seek(fseek, 2, 0) == 2, "file Seek(2, Begin) returns 2");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(fseek, buf, 2) == 2 && std::memcmp(buf, "ll", 2) == 0,
           "file read at offset 2 returns ll");
    Expect(il2cpp_stream_seek(fseek, -1, 2) == 4, "file Seek(-1, End) returns 4");
    Expect(il2cpp_stream_set_length(fseek, 7) == 0, "file SetLength(7) extends");
    Expect(il2cpp_stream_get_length(fseek) == 7, "file length is 7 after extension");
    Expect(il2cpp_stream_seek(fseek, 0, 0) == 0, "file seek back to 0");
    std::memset(buf, 0, sizeof(buf));
    Expect(il2cpp_stream_read(fseek, buf, 5) == 5 && std::memcmp(buf, "hello", 5) == 0,
           "file content preserved across the SetLength extension");
    Expect(il2cpp_stream_set_length(fseek, 2) == 0, "file SetLength(2) truncates");
    Expect(il2cpp_stream_get_length(fseek) == 2, "file length is 2 after truncation");
    Expect(il2cpp_stream_seek(fseek, 0, 0) == 0 && il2cpp_stream_read(fseek, buf, 2) == 2
           && std::memcmp(buf, "he", 2) == 0, "file truncation keeps the prefix");
    Expect(il2cpp_stream_seek(fseek, 5, 0) == 5, "file seek past the truncated end");
    Expect(il2cpp_stream_set_length(fseek, 3) == 0, "file SetLength(3) clamps the position");
    Expect(il2cpp_stream_get_position(fseek) == 3, "file position clamped to 3");
    Expect(il2cpp_stream_set_length(fseek, -1) == -1, "file SetLength(-1) returns -1");
    Expect(il2cpp_stream_close(fseek) == 0, "seek file closes");

    // Append-mode file streams stay CanSeek=true, but a seek to a position
    // BEFORE the open-time EOF throws IOException (net48 oracle:
    // FileStream.Seek in append mode -> IO_SeekAppendOverwrite; this file
    // holds 3 bytes from the section above).
    il2cpp_stream_t fapp = il2cpp_stream_file_open(Cs("st_tmp/seek.bin"), 2); // ab
    Expect(fapp != nullptr && il2cpp_stream_get_can_seek(fapp) == 1,
           "append-mode file CanSeek is 1 (net48 probe)");
    Expect(ThrowsDotNet([&] { (void)il2cpp_stream_seek(fapp, 0, 0); }, kIOExceptionChain),
           "Seek before the append start throws IOException (net48 oracle)");
    Expect(il2cpp_stream_close(fapp) == 0, "append file closes");

    // null-stream errors for the new handle surface.
    Expect(il2cpp_stream_get_can_seek(nullptr) == 0, "get_can_seek(null) returns 0");
    Expect(il2cpp_stream_seek(nullptr, 0, 0) == -1, "seek(null) returns -1");
    Expect(il2cpp_stream_set_length(nullptr, 0) == -1, "set_length(null) returns -1");

    // --- 6. Error paths -------------------------------------------------------
    Expect(il2cpp_stream_file_open(nullptr, 0) == nullptr, "file_open(null) returns null");
    Expect(il2cpp_stream_file_open(Cs("st_tmp/no_such.bin"), 0) == nullptr,
           "file_open of a missing file (read) returns null");
    Expect(il2cpp_stream_file_open(Cs("st_tmp/no_dir/x.bin"), 1) == nullptr,
           "file_open into a missing directory returns null");
    Expect(il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 10) == nullptr,
           "file_open with an out-of-range mode (10) returns null (modes 0..9 are valid)");
    Expect(il2cpp_stream_read(nullptr, buf, 4) == -1, "read(null) returns -1");
    Expect(il2cpp_stream_write(nullptr, buf, 4) == -1, "write(null) returns -1");
    Expect(il2cpp_stream_write(nullptr, nullptr, 0) == -1, "write(null, null, 0) returns -1");
    Expect(il2cpp_stream_flush(nullptr) == 1, "flush(null) returns 1");
    Expect(il2cpp_stream_close(nullptr) == 1, "close(null) returns 1");
    Expect(il2cpp_stream_get_position(nullptr) == -1, "get_position(null) returns -1");
    Expect(il2cpp_stream_set_position(nullptr, 0) == -1, "set_position(null, 0) returns -1");
    Expect(il2cpp_stream_get_length(nullptr) == -1, "get_length(null) returns -1");
    Expect(il2cpp_stream_get_can_read(nullptr) == 0, "get_can_read(null) returns 0");
    Expect(il2cpp_stream_get_can_write(nullptr) == 0, "get_can_write(null) returns 0");
    Expect(il2cpp_stream_read_line(nullptr) == nullptr, "read_line(null) returns null");
    Expect(il2cpp_stream_memory_to_array(nullptr) == nullptr, "memory_to_array(null) returns null");
    il2cpp_stream_t ro = il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 0);
    Expect(ro != nullptr, "read-only handle for error checks opens");
    Expect(il2cpp_stream_read(ro, nullptr, 4) == -1, "read with null buffer returns -1");
    Expect(il2cpp_stream_write(ro, "x", 1) == -1, "write on read-only returns -1");
    Expect(il2cpp_stream_read(ro, buf, -1) == -1, "read with negative count returns -1");
    Expect(il2cpp_stream_write_line(ro, Cs("x")) == -1, "write_line on read-only returns -1");
    Expect(il2cpp_stream_memory_to_array(ro) == nullptr,
           "memory_to_array on a file stream returns null");
    Expect(il2cpp_stream_set_position(ro, -5) == -1, "set_position with negative position returns -1");
    Expect(il2cpp_stream_close(ro) == 0, "read-only handle closes");

    // === OBJECT MODEL (il2cpp_streamio.h) ==================================
    // Mirrors the streamcase oracle at the runtime-object level: the four
    // System_IO_*_class descriptors + object-typed icall surface, ending with
    // the net48 ownership rule (closing a StreamReader/Writer closes the
    // shared underlying file).

    // --- 7. descriptor identity + layout + raw byte[] FileStream roundtrip --
    il2cpp::il2cpp_stream_register_classes();
    Expect(il2cpp_class_by_name("System.IO.FileStream") == &System_IO_FileStream_class,
           "System.IO.FileStream resolves by name (registered)");
    Expect(il2cpp_class_by_name("System.IO.StreamReader") == &System_IO_StreamReader_class,
           "System.IO.StreamReader resolves by name (registered)");
    Expect(il2cpp_class_by_name("System.IO.StreamWriter") == &System_IO_StreamWriter_class,
           "System.IO.StreamWriter resolves by name (registered)");
    Expect(il2cpp_class_by_name("System.IO.MemoryStream") == &System_IO_MemoryStream_class,
           "System.IO.MemoryStream resolves by name (registered)");
    Expect(System_IO_FileStream_class.instanceSize == sizeof(Il2CppStreamObject),
           "FileStream instanceSize == sizeof(Il2CppStreamObject)");
    Expect(System_IO_StreamReader_class.instanceSize == sizeof(Il2CppStreamObject),
           "StreamReader instanceSize == sizeof(Il2CppStreamObject)");
    Expect(System_IO_StreamWriter_class.instanceSize == sizeof(Il2CppStreamObject),
           "StreamWriter instanceSize == sizeof(Il2CppStreamObject)");
    Expect(System_IO_MemoryStream_class.instanceSize == sizeof(Il2CppStreamObject),
           "MemoryStream instanceSize == sizeof(Il2CppStreamObject)");
    Expect(System_IO_FileStream_class.refFieldBitmap == 0u
           && System_IO_StreamReader_class.refFieldBitmap == 0u
           && System_IO_StreamWriter_class.refFieldBitmap == 0u
           && System_IO_MemoryStream_class.refFieldBitmap == 0u,
           "stream descriptors carry no GC reference fields (bitmap 0)");

    Il2CppObject* rawFs = NewFileStream();
    Expect(rawFs->klass == &System_IO_FileStream_class, "FileStream object klass stamped");
    Expect(il2cpp_is_instance(rawFs, &System_Object_class),
           "a FileStream IS-A System.Object (baseClass chain)");
    icall_stream_file_ctor(rawFs, Cs("st_tmp/obj_raw.bin"), 2, 2); // Create + Write
    Expect(icall_stream_get_can_write(rawFs) == 1, "write-mode FileStream object CanWrite is 1");
    Expect(icall_stream_get_can_read(rawFs) == 0, "write-mode FileStream object CanRead is 0");
    icall_stream_write_bytes(rawFs, ByteArray("hello stream", 12), 0, 12);
    Expect(icall_stream_get_position(rawFs) == 12, "FileStream object position advances after write");
    Expect(icall_stream_get_length(rawFs) == 12, "FileStream object length equals written bytes");
    Expect(icall_stream_flush(rawFs) == 0, "FileStream object Flush returns 0");
    Expect(ThrowsDotNet([&] { icall_stream_read_bytes(rawFs, ByteArray("", 0), 0, 0); },
                        kNotSupportedChain),
           "read_bytes on a write-only stream throws NotSupportedException");
    Expect(icall_stream_close(rawFs) == 0, "write-mode FileStream object Close returns 0");
    Expect(icall_stream_close(rawFs) == 0, "object double-close is an idempotent no-op");

    Il2CppObject* rawFr = NewFileStream();
    icall_stream_file_ctor(rawFr, Cs("st_tmp/obj_raw.bin"), 3, 1); // Open + Read
    Expect(icall_stream_get_can_read(rawFr) == 1, "read-mode FileStream object CanRead is 1");
    Expect(icall_stream_get_can_write(rawFr) == 0, "read-mode FileStream object CanWrite is 0");
    Il2CppArray* rbuf = il2cpp_array_new(nullptr, 1, false, 12);
    Expect(icall_stream_read_bytes(rawFr, rbuf, 0, 12) == 12,
           "FileStream object Read returns the requested bytes");
    Expect(std::memcmp(rbuf->data, "hello stream", 12) == 0,
           "FileStream object read-back matches the written bytes");
    Expect(icall_stream_read_bytes(rawFr, rbuf, 0, 12) == 0,
           "FileStream object Read at end-of-stream returns 0");
    Expect(icall_stream_close(rawFr) == 0, "read-mode FileStream object Close returns 0");

    // --- 8. StreamWriter wrap chain (the streamcase write side) ------------
    Il2CppObject* fs = NewFileStream();
    icall_stream_file_ctor(fs, Cs("st_tmp/obj.bin"), 2, 2); // Create + Write
    Il2CppObject* sw = NewStreamWriter();
    Expect(sw->klass == &System_IO_StreamWriter_class, "StreamWriter object klass stamped");
    Expect(il2cpp_is_instance(sw, &System_Object_class),
           "a StreamWriter IS-A System.Object (baseClass chain)");
    icall_stream_writer_ctor(sw, fs);
    Expect(icall_stream_write_line(sw, Cs("line one")) == 0, "writer WriteLine(\"line one\") returns 0");
    Expect(icall_stream_flush(sw) == 0, "writer Flush returns 0");
    Expect(icall_stream_get_position(fs) == 10,
           "fs.Position == 10 after WriteLine + Flush (\"line one\" + CRLF)");
    Expect(icall_stream_write_line(sw, Cs("line two")) == 0, "writer WriteLine(\"line two\") returns 0");
    Expect(icall_stream_write_line(sw, Cs("line three")) == 0, "writer WriteLine(\"line three\") returns 0");
    Expect(icall_stream_close(sw) == 0, "writer Close closes the shared file");
    Expect(icall_stream_get_position(fs) == -1,
           "fs.Position after the wrapper closed it returns -1 (net48 ownership)");
    Expect(icall_stream_get_can_write(fs) == 0, "fs.CanWrite after the wrapper closed it is 0");
    Expect(icall_stream_close(fs) == 0, "fs.Close after the wrapper closed it is a no-op");

    // --- 8b. StreamWriter(string path) ctor (streamops using-block shape) ---
    Il2CppObject* pw = NewStreamWriter();
    icall_stream_writer_path_ctor(pw, Cs("st_tmp/path_writer.txt"));
    Expect(icall_stream_write_line(pw, Cs("using-line")) == 0,
           "path writer WriteLine returns 0");
    Expect(icall_stream_get_position(pw) == 12,
           "path writer Position == 12 (\"using-line\" + CRLF)");
    Expect(icall_stream_close(pw) == 0, "path writer Close returns 0");
    Il2CppObject* pr = NewFileStream();
    icall_stream_file_ctor(pr, Cs("st_tmp/path_writer.txt"), 3, 1); // Open + Read
    Il2CppObject* prLine = icall_stream_read_line(pr);
    Expect(prLine != nullptr && StrEquals(prLine, "using-line"),
           "path writer file reads back the written line");
    Expect(icall_stream_close(pr) == 0, "path writer reader Close returns 0");

    // --- 9. StreamReader wrap chain (the streamcase read side) --------------
    Il2CppObject* fr = NewFileStream();
    icall_stream_file_ctor(fr, Cs("st_tmp/obj.bin"), 3, 1); // Open + Read
    Expect(icall_stream_get_can_read(fr) == 1, "read-mode FileStream object CanRead is 1");
    Expect(icall_stream_get_can_write(fr) == 0, "read-mode FileStream object CanWrite is 0");
    Expect(icall_stream_get_length(fr) == 32, "fr.Length == 32 (10+10+12 bytes)");
    Il2CppObject* sr = NewStreamReader();
    Expect(sr->klass == &System_IO_StreamReader_class, "StreamReader object klass stamped");
    icall_stream_reader_ctor(sr, fr);
    Il2CppObject* objLine = icall_stream_read_line(sr);
    Expect(objLine != nullptr && StrEquals(objLine, "line one"), "sr.ReadLine() == \"line one\"");
    objLine = icall_stream_read_line(sr);
    Expect(objLine != nullptr && StrEquals(objLine, "line two"), "sr.ReadLine() == \"line two\"");
    objLine = icall_stream_read_line(sr);
    Expect(objLine != nullptr && StrEquals(objLine, "line three"), "sr.ReadLine() == \"line three\"");
    Expect(icall_stream_read_line(sr) == nullptr, "sr.ReadLine() == null (EOF)");
    Expect(icall_stream_close(sr) == 0, "reader Close closes the shared file");
    Expect(icall_stream_close(fr) == 0, "fr.Close after the wrapper closed it is a no-op");

    // --- 10. MemoryStream object round-trip (Write/Read/Position/ToArray) ---
    Il2CppObject* ms = NewMemoryStream();
    Expect(ms->klass == &System_IO_MemoryStream_class, "MemoryStream object klass stamped");
    Expect(il2cpp_is_instance(ms, &System_Object_class),
           "a MemoryStream IS-A System.Object (baseClass chain)");
    icall_stream_memory_ctor(ms);
    Expect(icall_stream_get_can_read(ms) == 1 && icall_stream_get_can_write(ms) == 1,
           "MemoryStream object CanRead/CanWrite are both 1");
    Expect(icall_stream_get_length(ms) == 0 && icall_stream_get_position(ms) == 0,
           "fresh MemoryStream object has position 0 and length 0");
    icall_stream_write_bytes(ms, ByteArray("\x01\x02\x03", 3), 0, 3);
    Expect(icall_stream_get_length(ms) == 3, "MemoryStream object length is 3 after write");
    Expect(icall_stream_get_position(ms) == 3, "MemoryStream object position is 3 after write");
    Expect(icall_stream_set_position(ms, 0) == 0, "ms.Position = 0 succeeds");
    Expect(icall_stream_get_position(ms) == 0, "ms.Position == 0");
    Il2CppArray* mbuf = il2cpp_array_new(nullptr, 1, false, 3);
    Expect(icall_stream_read_bytes(ms, mbuf, 0, 3) == 3, "ms.Read(buf, 0, 3) == 3");
    Expect(mbuf->data[0] == 1 && mbuf->data[1] == 2 && mbuf->data[2] == 3,
           "read-back bytes equal the written bytes");
    Il2CppObject* marr = reinterpret_cast<Il2CppObject*>(icall_stream_memory_to_array(ms));
    Expect(marr != nullptr && BytesEqual(marr, "\x01\x02\x03", 3),
           "ms.ToArray returns the full content");
    Expect(icall_stream_close(ms) == 0, "MemoryStream object Close returns 0");

    // --- 11. UTF-8 content round-trip through the object surface ------------
    const char16_t objNonAscii[] = { u'h', 0x00E9, u'l', u'l', u'o' };
    Il2CppObject* u8fs = NewFileStream();
    icall_stream_file_ctor(u8fs, Cs("st_tmp/obj_utf8.txt"), 2, 2); // Create + Write
    Il2CppObject* u8w = NewStreamWriter();
    icall_stream_writer_ctor(u8w, u8fs);
    Il2CppString* objNonAsciiStr = il2cpp_string_new(objNonAscii, 5);
    Expect(icall_stream_write_line(u8w, reinterpret_cast<Il2CppObject*>(objNonAsciiStr)) == 0,
           "writer WriteLine accepts non-ASCII content");
    Expect(icall_stream_close(u8w) == 0, "utf8 writer Close");
    Expect(icall_stream_close(u8fs) == 0, "utf8 writer-wrapped file Close (no-op)");

    Il2CppObject* u8fr = NewFileStream();
    icall_stream_file_ctor(u8fr, Cs("st_tmp/obj_utf8.txt"), 3, 1); // Open + Read
    Il2CppObject* u8r = NewStreamReader();
    icall_stream_reader_ctor(u8r, u8fr);
    Il2CppObject* back = icall_stream_read_line(u8r);
    Expect(back != nullptr && StrEquals16(back, objNonAscii, 5),
           "non-ASCII line round-trips unit for unit (UTF-8 encode/decode)");
    Expect(icall_stream_read_line(u8r) == nullptr, "utf8 reader hits EOF after the line");
    Expect(icall_stream_close(u8r) == 0, "utf8 reader Close");
    Expect(icall_stream_close(u8fr) == 0, "utf8 reader-wrapped file Close (no-op)");

    // --- 12. object error paths ---------------------------------------------
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(nullptr, Cs("x"), 2, 2); }, kNullRefChain),
           "file ctor(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), nullptr, 2, 2); }, kArgNullExceptionChain),
           "file ctor(null path) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 0, 2); }, kArgumentChain),
           "file ctor(invalid FileMode) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 2, 9); }, kArgumentChain),
           "file ctor(invalid FileAccess) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("st_tmp/no_such.bin"), 3, 1); }, kFileNotFoundChain),
           "file ctor(missing file) throws FileNotFoundException");
    Expect(ThrowsDotNet([&] { icall_stream_reader_ctor(nullptr, NewFileStream()); }, kNullRefChain),
           "reader ctor(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_reader_ctor(NewStreamReader(), nullptr); }, kArgNullExceptionChain),
           "reader ctor(null stream) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_stream_writer_ctor(NewStreamWriter(), Cs("not a stream")); }, kArgumentChain),
           "writer ctor(non-stream) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_writer_path_ctor(nullptr, Cs("st_tmp/x.bin")); }, kNullRefChain),
           "writer path ctor(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_writer_path_ctor(NewStreamWriter(), nullptr); }, kArgNullExceptionChain),
           "writer path ctor(null path) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_stream_read_bytes(nullptr, ByteArray("", 0), 0, 0); }, kNullRefChain),
           "read_bytes(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_write_bytes(nullptr, ByteArray("", 0), 0, 0); }, kNullRefChain),
           "write_bytes(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_flush(nullptr); }, kNullRefChain),
           "flush(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_close(nullptr); }, kNullRefChain),
           "close(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_get_position(nullptr); }, kNullRefChain),
           "get_position(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_set_position(nullptr, 0); }, kNullRefChain),
           "set_position(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_get_length(nullptr); }, kNullRefChain),
           "get_length(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_get_can_read(nullptr); }, kNullRefChain),
           "get_can_read(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_get_can_write(nullptr); }, kNullRefChain),
           "get_can_write(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_read_line(nullptr); }, kNullRefChain),
           "read_line(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_write_line(nullptr, Cs("x")); }, kNullRefChain),
           "write_line(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_memory_to_array(nullptr); }, kNullRefChain),
           "memory_to_array(null receiver) throws NullReferenceException");

    Il2CppObject* closedFr = NewFileStream();
    icall_stream_file_ctor(closedFr, Cs("st_tmp/obj.bin"), 3, 1); // Open + Read
    Expect(icall_stream_close(closedFr) == 0, "closed-wrap fixture closes");
    Expect(ThrowsDotNet([&] { icall_stream_writer_ctor(NewStreamWriter(), closedFr); }, kArgumentChain),
           "writer ctor over a closed stream throws ArgumentException");

    Il2CppObject* ro2 = NewFileStream();
    icall_stream_file_ctor(ro2, Cs("st_tmp/obj.bin"), 3, 1); // Open + Read
    Expect(ThrowsDotNet([&] { icall_stream_read_bytes(ro2, nullptr, 0, 0); }, kArgNullExceptionChain),
           "read_bytes(null buffer) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_stream_read_bytes(ro2, ByteArray("", 0), 1, 0); }, kArgOutOfRangeChain),
           "read_bytes(out-of-range offset) throws ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_stream_read_bytes(ro2, ByteArray("", 0), 0, 1); }, kArgOutOfRangeChain),
           "read_bytes(count past the end) throws ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_stream_write_bytes(ro2, ByteArray("x", 1), 0, 1); }, kNotSupportedChain),
           "write_bytes on a read-only stream throws NotSupportedException");
    Expect(icall_stream_close(ro2) == 0, "read-only object error fixture closes");

    // === 13. net48 semantics: ReadToEnd / timeout / negative position / FileMode matrix
    // (probe-verified against net48 -- see stio-net48-probe.exe output) ===

    // 13a. StreamReader.ReadToEnd: remainder + "" at EOF (not null).
    Il2CppObject* rte = NewFileStream();
    icall_stream_file_ctor(rte, Cs("st_tmp/obj_rte.txt"), 2, 2); // Create + Write
    Il2CppObject* rtew = NewStreamWriter();
    icall_stream_writer_ctor(rtew, rte);
    Expect(icall_stream_write_line(rtew, Cs("line one")) == 0, "rte WriteLine one");
    Expect(icall_stream_write_line(rtew, Cs("line two")) == 0, "rte WriteLine two");
    Expect(icall_stream_close(rtew) == 0, "rte writer Close");
    Expect(icall_stream_close(rte) == 0, "rte file Close (no-op)");

    Il2CppObject* rter = NewFileStream();
    icall_stream_file_ctor(rter, Cs("st_tmp/obj_rte.txt"), 3, 1); // Open + Read
    Il2CppObject* rterr = NewStreamReader();
    icall_stream_reader_ctor(rterr, rter);
    Il2CppObject* firstLine = icall_stream_read_line(rterr);
    Expect(firstLine != nullptr && StrEquals(firstLine, "line one"), "ReadToEnd fixture reads the first line");
    Il2CppObject* rest = icall_stream_read_to_end(rterr);
    Expect(rest != nullptr && StrEquals(rest, "line two\r\n"),
           "ReadToEnd returns the remainder incl. the trailing CRLF");
    Il2CppObject* eof = icall_stream_read_to_end(rterr);
    Expect(eof != nullptr && StrEquals(eof, ""),
           "ReadToEnd at end-of-stream returns \"\" (not null)");
    Expect(icall_stream_close(rterr) == 0, "rte reader Close");
    Expect(icall_stream_close(rter) == 0, "rte reader file Close (no-op)");

    // ReadToEnd on a MemoryStream object.
    Il2CppObject* mste = NewMemoryStream();
    icall_stream_memory_ctor(mste);
    icall_stream_write_bytes(mste, ByteArray("abc", 3), 0, 3);
    Expect(icall_stream_set_position(mste, 1) == 0, "rte memory seek to 1");
    Il2CppObject* mrest = icall_stream_read_to_end(mste);
    Expect(mrest != nullptr && StrEquals(mrest, "bc"),
           "MemoryStream ReadToEnd returns the remainder");
    Expect(icall_stream_get_position(mste) == 3,
           "ReadToEnd consumed the MemoryStream to EOF");
    Expect(icall_stream_close(mste) == 0, "rte memory Close");

    // ReadToEnd UTF-8 astral/BMP roundtrip through the object surface.
    const char16_t zhWrite[] = { u'\u4E2D', u'\u6587', u'\u8FD4', u'\u5F80',
                                 0xD83D, 0xDE00 }; // 中文往返 + emoji (astral pair)
    // write_line appends CRLF, so ReadToEnd sees the line + the terminator.
    const char16_t zhWithCrlf[] = { u'\u4E2D', u'\u6587', u'\u8FD4', u'\u5F80',
                                    0xD83D, 0xDE00, u'\r', u'\n' };
    Il2CppObject* utf8Fs = NewFileStream();
    icall_stream_file_ctor(utf8Fs, Cs("st_tmp/obj_rte_utf8.txt"), 2, 2); // Create + Write
    Il2CppObject* utf8W = NewStreamWriter();
    icall_stream_writer_ctor(utf8W, utf8Fs);
    Il2CppString* zhStr = il2cpp_string_new(zhWrite, 6);
    Expect(icall_stream_write_line(utf8W, reinterpret_cast<Il2CppObject*>(zhStr)) == 0,
           "rte utf8 WriteLine accepts BMP + astral");
    Expect(icall_stream_close(utf8W) == 0, "rte utf8 writer Close");
    Expect(icall_stream_close(utf8Fs) == 0, "rte utf8 file Close (no-op)");
    Il2CppObject* utf8Fr = NewFileStream();
    icall_stream_file_ctor(utf8Fr, Cs("st_tmp/obj_rte_utf8.txt"), 3, 1); // Open + Read
    Il2CppObject* utf8R = NewStreamReader();
    icall_stream_reader_ctor(utf8R, utf8Fr);
    Il2CppObject* zhBack = icall_stream_read_to_end(utf8R);
    Expect(zhBack != nullptr && StrEquals16(zhBack, zhWithCrlf, 8),
           "ReadToEnd round-trips BMP + astral units unit for unit");
    Expect(icall_stream_close(utf8R) == 0, "rte utf8 reader Close");
    Expect(icall_stream_close(utf8Fr) == 0, "rte utf8 reader file Close (no-op)");

    // 13b. Timeout surface: CanTimeout=0, Read/WriteTimeout throw IOE.
    Il2CppObject* tm = NewMemoryStream();
    icall_stream_memory_ctor(tm);
    Expect(icall_stream_get_can_timeout(tm) == 0, "MemoryStream.CanTimeout is false");
    Expect(ThrowsDotNet([&] { icall_stream_get_read_timeout(tm); }, kInvalidOperationChain),
           "get_ReadTimeout throws InvalidOperationException");
    Expect(ThrowsDotNet([&] { icall_stream_set_read_timeout(tm, 1000); }, kInvalidOperationChain),
           "set_ReadTimeout throws InvalidOperationException");
    Expect(ThrowsDotNet([&] { icall_stream_get_write_timeout(tm); }, kInvalidOperationChain),
           "get_WriteTimeout throws InvalidOperationException");
    Expect(ThrowsDotNet([&] { icall_stream_set_write_timeout(tm, 1000); }, kInvalidOperationChain),
           "set_WriteTimeout throws InvalidOperationException");
    Expect(icall_stream_close(tm) == 0, "timeout fixture closes");

    Il2CppObject* tmFs = NewFileStream();
    icall_stream_file_ctor(tmFs, Cs("st_tmp/obj.bin"), 3, 1); // Open + Read
    Expect(icall_stream_get_can_timeout(tmFs) == 0, "FileStream.CanTimeout is false");
    Expect(ThrowsDotNet([&] { icall_stream_get_read_timeout(tmFs); }, kInvalidOperationChain),
           "FileStream get_ReadTimeout throws InvalidOperationException");
    Expect(icall_stream_close(tmFs) == 0, "FileStream timeout fixture closes");

    // 13c. Negative Position -> ArgumentOutOfRangeException (object layer).
    Il2CppObject* np = NewMemoryStream();
    icall_stream_memory_ctor(np);
    Expect(ThrowsDotNet([&] { icall_stream_set_position(np, -1); }, kArgOutOfRangeChain),
           "set_Position(-1) throws ArgumentOutOfRangeException");
    Expect(icall_stream_get_position(np) == 0, "position unchanged after the throw");
    Expect(icall_stream_close(np) == 0, "negative-position fixture closes");

    // 13d. FileMode/FileAccess compatibility matrix (net48-probed).
    // Create/Truncate/CreateNew require Write -- Read-only combos throw.
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 2, 1); }, kArgumentChain),
           "FileMode.Create + FileAccess.Read throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 5, 1); }, kArgumentChain),
           "FileMode.Truncate + FileAccess.Read throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 1, 1); }, kArgumentChain),
           "FileMode.CreateNew + FileAccess.Read throws ArgumentException");
    // Append requires Write-only.
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 6, 1); }, kArgumentChain),
           "FileMode.Append + FileAccess.Read throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor(NewFileStream(), Cs("x"), 6, 3); }, kArgumentChain),
           "FileMode.Append + FileAccess.ReadWrite throws ArgumentException");
    // Create + ReadWrite TRUNCATES existing content (mode 4 = w+b).
    Il2CppObject* tw = NewFileStream();
    icall_stream_file_ctor(tw, Cs("st_tmp/trunc.bin"), 2, 2); // Create + Write
    icall_stream_write_bytes(tw, ByteArray("abcdefgh", 8), 0, 8);
    Expect(icall_stream_close(tw) == 0, "trunc fixture writes 8 bytes");
    Il2CppObject* trw = NewFileStream();
    icall_stream_file_ctor(trw, Cs("st_tmp/trunc.bin"), 2, 3); // Create + ReadWrite
    Expect(icall_stream_get_length(trw) == 0,
           "FileMode.Create + ReadWrite truncates existing content (length 0)");
    Expect(icall_stream_get_position(trw) == 0, "truncated stream starts at position 0");
    icall_stream_write_bytes(trw, ByteArray("Z", 1), 0, 1);
    Expect(icall_stream_get_length(trw) == 1, "write after Create+RW extends to 1");
    Expect(icall_stream_close(trw) == 0, "Create+RW fixture closes");

    // Open + ReadWrite does NOT truncate (mode 3 = r+b).
    Il2CppObject* ow = NewFileStream();
    icall_stream_file_ctor(ow, Cs("st_tmp/trunc.bin"), 3, 2); // Open + Write
    icall_stream_write_bytes(ow, ByteArray("X", 1), 0, 1);
    Expect(icall_stream_get_length(ow) == 1, "Open+Write keeps length 1 (no truncate)");
    Expect(icall_stream_close(ow) == 0, "Open+Write fixture closes");

    // Append + Write: writes land at end-of-file (net48).
    Il2CppObject* ap = NewFileStream();
    icall_stream_file_ctor(ap, Cs("st_tmp/trunc.bin"), 6, 2); // Append + Write
    Expect(icall_stream_get_length(ap) == 1, "Append opens with the existing length");
    icall_stream_write_bytes(ap, ByteArray("Q", 1), 0, 1);
    Expect(icall_stream_get_length(ap) == 2, "Append write lands at end-of-file");
    Expect(icall_stream_close(ap) == 0, "Append fixture closes");

    // 13e. Seek / CanSeek / SetLength / ReadByte / WriteByte (D-STP-2/3) ------
    // net48 seek surface over the OBJECT layer: SeekOrigin (Begin=0/Current=1/
    // End=2), CanSeek, SetLength truncate/extend with position clamping.
    Il2CppObject* skObj = NewMemoryStream();
    icall_stream_memory_ctor(skObj);
    Expect(icall_stream_get_can_seek(skObj) == 1, "MemoryStream object CanSeek is 1");
    icall_stream_write_bytes(skObj, ByteArray("abcdef", 6), 0, 6);
    Expect(icall_stream_seek(skObj, 2, 0) == 2, "object Seek(2, Begin) returns 2");
    Expect(icall_stream_seek(skObj, 2, 1) == 4, "object Seek(2, Current) returns 4");
    Expect(icall_stream_seek(skObj, -4, 2) == 2, "object Seek(-4, End) returns 2");
    Expect(ThrowsDotNet([&] { icall_stream_seek(skObj, 0, 7); }, kArgumentChain),
           "object Seek(invalid origin) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_seek(skObj, -1, 0); }, kIOExceptionChain),
           "object Seek(before beginning) throws IOException");
    Expect(ThrowsDotNet([&] { icall_stream_set_length(skObj, -1); }, kArgOutOfRangeChain),
           "object SetLength(-1) throws ArgumentOutOfRangeException");

    // SetLength(10) extends a 6-byte stream (the gap [6,10) is zero-filled).
    icall_stream_set_length(skObj, 10);
    Expect(icall_stream_get_length(skObj) == 10, "object SetLength(10) extends the stream");
    Expect(icall_stream_seek(skObj, 0, 0) == 0, "object seek to 0 for ReadByte probe");
    Expect(icall_stream_read_byte(skObj) == 'a', "object ReadByte returns the first byte");
    Expect(icall_stream_read_byte(skObj) == 'b', "object ReadByte advances to the next byte");
    icall_stream_write_byte(skObj, 'X'); // overwrite index 2
    Expect(icall_stream_seek(skObj, 0, 0) == 0, "object seek to 0 for byte verify");
    Il2CppArray* sbuf = il2cpp_array_new(nullptr, 1, false, 10);
    Expect(icall_stream_read_bytes(skObj, sbuf, 0, 10) == 10, "object reads back 10 bytes");
    Expect(sbuf->data[0] == 'a' && sbuf->data[1] == 'b' && sbuf->data[2] == 'X'
           && sbuf->data[3] == 'd' && sbuf->data[9] == 0,
           "WriteByte overwrites and the SetLength gap reads as zero");
    Expect(icall_stream_seek(skObj, 0, 2) == 10, "object Seek(0, End) returns the length");
    Expect(icall_stream_read_byte(skObj) == -1, "object ReadByte at end-of-stream returns -1");
    Expect(icall_stream_close(skObj) == 0, "object seek fixture closes");

    // Read-only / write-only capability violations (net48 NotSupportedException).
    Il2CppObject* roSeek = NewFileStream();
    icall_stream_file_ctor(roSeek, Cs("st_tmp/obj_seek.bin"), 2, 2); // Create + Write
    Expect(ThrowsDotNet([&] { icall_stream_read_byte(roSeek); }, kNotSupportedChain),
           "ReadByte on a write-only stream throws NotSupportedException");
    icall_stream_write_byte(roSeek, 'Q');
    Expect(icall_stream_get_position(roSeek) == 1, "WriteByte advances the position");
    Expect(icall_stream_close(roSeek) == 0, "write-only object closes");

    Il2CppObject* roRead = NewFileStream();
    icall_stream_file_ctor(roRead, Cs("st_tmp/obj_seek.bin"), 3, 1); // Open + Read
    Expect(ThrowsDotNet([&] { icall_stream_write_byte(roRead, 0); }, kNotSupportedChain),
           "WriteByte on a read-only stream throws NotSupportedException");
    Expect(icall_stream_read_byte(roRead) == 'Q', "ReadByte reads the written byte back");
    Expect(icall_stream_close(roRead) == 0, "read-only object closes");

    // File object seek surface (read-write).
    Il2CppObject* fsObj = NewFileStream();
    icall_stream_file_ctor(fsObj, Cs("st_tmp/obj_seek.bin"), 2, 3); // Create + ReadWrite
    Expect(icall_stream_get_can_seek(fsObj) == 1, "read-write FileStream object CanSeek is 1");
    icall_stream_write_bytes(fsObj, ByteArray("hello", 5), 0, 5);
    Expect(icall_stream_seek(fsObj, 2, 0) == 2, "file object Seek(2, Begin) returns 2");
    Expect(icall_stream_read_byte(fsObj) == 'l', "file object ReadByte returns the window byte");
    icall_stream_set_length(fsObj, 8);
    Expect(icall_stream_get_length(fsObj) == 8, "file object SetLength(8) extends");
    Expect(icall_stream_seek(fsObj, 0, 0) == 0, "file object seek to 0");
    Il2CppArray* fbuf = il2cpp_array_new(nullptr, 1, false, 5);
    Expect(icall_stream_read_bytes(fsObj, fbuf, 0, 5) == 5, "file object reads back 5 bytes");
    Expect(fbuf->data[0] == 'h' && fbuf->data[1] == 'e' && fbuf->data[2] == 'l'
           && fbuf->data[3] == 'l' && fbuf->data[4] == 'o',
           "file content preserved across the SetLength extension");
    Expect(icall_stream_close(fsObj) == 0, "file object seek fixture closes");

    // read->write mode transition on a FILE (C11 7.21.5.3): net48 mixes
    // ReadByte/WriteByte freely; the stdio backend resets the mode with an
    // identity fseek before each file read/write. Regression for the e2e
    // fs_wb_len failure (ReadByte x2 -> WriteByte extended the file wrongly).
    Il2CppObject* rwByte = NewFileStream();
    icall_stream_file_ctor(rwByte, Cs("st_tmp/obj_rwbyte.bin"), 2, 3); // Create + ReadWrite
    icall_stream_write_bytes(rwByte, ByteArray("hello", 5), 0, 5);
    Expect(icall_stream_seek(rwByte, 0, 0) == 0, "rwbyte seek to 0");
    Expect(icall_stream_read_byte(rwByte) == 'h', "rwbyte ReadByte 1");
    Expect(icall_stream_read_byte(rwByte) == 'e', "rwbyte ReadByte 2");
    icall_stream_write_byte(rwByte, 'X'); // read->write WITHOUT an intervening seek
    Expect(icall_stream_get_length(rwByte) == 5, "read->write overwrites (length stays 5)");
    Expect(icall_stream_get_position(rwByte) == 3, "read->write advances the position to 3");
    Expect(icall_stream_seek(rwByte, 0, 0) == 0, "rwbyte seek to 0");
    Il2CppArray* rwb = il2cpp_array_new(nullptr, 1, false, 5);
    Expect(icall_stream_read_bytes(rwByte, rwb, 0, 5) == 5, "rwbyte reads back 5 bytes");
    Expect(rwb->data[0] == 'h' && rwb->data[1] == 'e' && rwb->data[2] == 'X',
           "rwbyte overwrite landed at index 2");
    Expect(icall_stream_close(rwByte) == 0, "rwbyte closes");

    // write->read transition on a FILE.
    Il2CppObject* wrByte = NewFileStream();
    icall_stream_file_ctor(wrByte, Cs("st_tmp/obj_rwbyte.bin"), 2, 3); // Create + ReadWrite
    icall_stream_write_byte(wrByte, 'A');
    icall_stream_write_byte(wrByte, 'B');
    Expect(icall_stream_seek(wrByte, 0, 0) == 0, "wrbyte seek to 0");
    Expect(icall_stream_read_byte(wrByte) == 'A', "write->read ReadByte 1");
    Expect(icall_stream_read_byte(wrByte) == 'B', "write->read ReadByte 2");
    Expect(icall_stream_close(wrByte) == 0, "wrbyte closes");

    // The exact e2e fs_wb_len repro: read to EOF, then WriteByte extends.
    Il2CppObject* rwe = NewFileStream();
    icall_stream_file_ctor(rwe, Cs("st_tmp/obj_rwe.bin"), 2, 3); // Create + ReadWrite
    icall_stream_write_bytes(rwe, ByteArray("abcdef", 6), 0, 6);
    Expect(icall_stream_seek(rwe, 4, 0) == 4, "rwe seek to 4");
    Expect(icall_stream_read_byte(rwe) == 'e', "rwe ReadByte at 4");
    Expect(icall_stream_read_byte(rwe) == 'f', "rwe ReadByte at 5");
    icall_stream_write_byte(rwe, 'G'); // read->write at EOF extends the file
    Expect(icall_stream_get_length(rwe) == 7,
           "read->write at EOF extends the file (fs_wb_len repro)");
    Expect(icall_stream_close(rwe) == 0, "rwe closes");

    // Append-mode FileStream object: still seekable (net48 probe).
    Il2CppObject* apObj = NewFileStream();
    icall_stream_file_ctor(apObj, Cs("st_tmp/obj_seek.bin"), 6, 2); // Append + Write
    Expect(icall_stream_get_can_seek(apObj) == 1,
           "append FileStream object CanSeek is 1 (net48 probe)");
    Expect(ThrowsDotNet([&] { (void)icall_stream_seek(apObj, 0, 0); }, kIOExceptionChain),
           "Seek before the append start throws IOException (net48 oracle)");
    Expect(icall_stream_close(apObj) == 0, "append object closes");

    // Closed-receiver M3 conventions + null-receiver guards for the new surface.
    Il2CppObject* clSeek = NewMemoryStream();
    icall_stream_memory_ctor(clSeek);
    Expect(icall_stream_close(clSeek) == 0, "closed-receiver fixture closes");
    Expect(icall_stream_get_can_seek(clSeek) == 0, "CanSeek on a closed stream is 0");
    Expect(icall_stream_seek(clSeek, 0, 0) == -1, "Seek on a closed stream returns -1");
    Expect(icall_stream_read_byte(clSeek) == -1, "ReadByte on a closed stream returns -1");
    icall_stream_write_byte(clSeek, 0); // silent no-op, no throw
    icall_stream_set_length(clSeek, 5); // silent no-op, no throw
    Expect(icall_stream_get_position(clSeek) == -1,
           "WriteByte/SetLength on a closed stream are silent no-ops");
    Expect(ThrowsDotNet([&] { icall_stream_get_can_seek(nullptr); }, kNullRefChain),
           "get_can_seek(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_seek(nullptr, 0, 0); }, kNullRefChain),
           "seek(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_set_length(nullptr, 0); }, kNullRefChain),
           "set_length(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_read_byte(nullptr); }, kNullRefChain),
           "read_byte(null receiver) throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stream_write_byte(nullptr, 0); }, kNullRefChain),
           "write_byte(null receiver) throws NullReferenceException");

    // --- WAVE2: write_text (no newline, null no-op) -------------------------
    {
        il2cpp_stream_t wt = il2cpp_stream_memory_create();
        Expect(il2cpp_stream_write_text(wt, Cs("AB")) == 0, "write_text returns 0");
        Expect(il2cpp_stream_write_text(wt, nullptr) == 0, "write_text(null) is a no-op");
        Expect(il2cpp_stream_write_line(wt, Cs("C")) == 0, "write_line after write_text");
        Expect(il2cpp_stream_get_length(wt) == 5,
               "write_text emits no line terminator (2 + 0 + 3 bytes)");
        Expect(il2cpp_stream_set_position(wt, 0) == 0, "write_text buffer rewinds");
        char wbuf[8];
        std::memset(wbuf, 0, sizeof(wbuf));
        Expect(il2cpp_stream_read(wt, wbuf, 5) == 5, "write_text buffer reads back");
        Expect(std::memcmp(wbuf, "ABC\r\n", 5) == 0,
               "write_text/write_line byte order is AB + C CRLF");
        Expect(il2cpp_stream_close(wt) == 0, "write_text fixture closes");
    }

    // icall_stream_write_text on the object surface; closed receiver -> -1.
    Il2CppObject* wtObj = NewMemoryStream();
    icall_stream_memory_ctor(wtObj);
    Expect(icall_stream_write_text(wtObj, Cs("XY")) == 0, "object write_text returns 0");
    Expect(icall_stream_write_text(wtObj, nullptr) == 0, "object write_text(null) is a no-op");
    Expect(icall_stream_get_length(wtObj) == 2, "object write_text wrote exactly XY");
    Expect(icall_stream_close(wtObj) == 0, "object write_text fixture closes");
    Expect(icall_stream_write_text(wtObj, Cs("Z")) == -1,
           "write_text on a closed receiver returns -1 (M3 convention)");

    // --- WAVE2: FileStream(string, FileMode) 2-arg ctor ---------------------
    // Access defaults (net48 oracle): Append -> Write, every other mode ->
    // ReadWrite.
    Il2CppObject* fs2c = NewFileStream();
    icall_stream_file_ctor2(fs2c, Cs("st_tmp/obj2.bin"), 2); // Create
    Expect(icall_stream_get_can_read(fs2c) == 1 && icall_stream_get_can_write(fs2c) == 1,
           "2-arg Create opens ReadWrite (net48 oracle)");
    icall_stream_write_bytes(fs2c, ByteArray("abc", 3), 0, 3);
    Expect(icall_stream_close(fs2c) == 0, "2-arg Create stream closes");
    Il2CppObject* raw2 = icall_file_read_all_text(Cs("st_tmp/obj2.bin"));
    Expect(raw2 != nullptr && StrEquals(raw2, "abc"), "2-arg written file content matches");

    Il2CppObject* fs2o = NewFileStream();
    icall_stream_file_ctor2(fs2o, Cs("st_tmp/obj2.bin"), 3); // Open
    Expect(icall_stream_get_can_read(fs2o) == 1 && icall_stream_get_can_write(fs2o) == 1,
           "2-arg Open opens ReadWrite (net48 oracle)");
    Expect(icall_stream_close(fs2o) == 0, "2-arg Open stream closes");

    Il2CppObject* fs2a = NewFileStream();
    icall_stream_file_ctor2(fs2a, Cs("st_tmp/obj2.bin"), 6); // Append
    Expect(icall_stream_get_can_read(fs2a) == 0 && icall_stream_get_can_write(fs2a) == 1,
           "2-arg Append opens Write-only (net48 oracle)");
    icall_stream_write_bytes(fs2a, ByteArray("d", 1), 0, 1);
    Expect(icall_stream_close(fs2a) == 0, "2-arg Append stream closes");
    Il2CppObject* raw3 = icall_file_read_all_text(Cs("st_tmp/obj2.bin"));
    Expect(raw3 != nullptr && StrEquals(raw3, "abcd"), "2-arg Append extends the file");

    Il2CppObject* fs2m = NewFileStream();
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor2(fs2m, Cs("st_tmp/missing2.bin"), 3); },
                        kFileNotFoundChain),
           "2-arg Open on a missing file throws FileNotFoundException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor2(fs2m, Cs("st_tmp/obj2.bin"), 7); },
                        kArgumentChain),
           "2-arg invalid FileMode throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_stream_file_ctor2(fs2m, nullptr, 3); },
                        kArgNullExceptionChain),
           "2-arg null path throws ArgumentNullException");

    // --- Cleanup: remove the files + the created directory chain --------------
    (void)il2cpp_stream_close(il2cpp_stream_file_open(Cs("st_tmp/data.bin"), 1));
    icall_file_delete(Cs("st_tmp/data.bin"));
    icall_file_delete(Cs("st_tmp/lines.txt"));
    icall_file_delete(Cs("st_tmp/utf8.txt"));
    icall_file_delete(Cs("st_tmp/obj_raw.bin"));
    icall_file_delete(Cs("st_tmp/obj.bin"));
    icall_file_delete(Cs("st_tmp/obj_utf8.txt"));
    icall_file_delete(Cs("st_tmp/obj_rte.txt"));
    icall_file_delete(Cs("st_tmp/obj_rte_utf8.txt"));
    icall_file_delete(Cs("st_tmp/trunc.bin"));
    icall_file_delete(Cs("st_tmp/path_writer.txt"));
    icall_file_delete(Cs("st_tmp/seek.bin"));
    icall_file_delete(Cs("st_tmp/obj_seek.bin"));
    icall_file_delete(Cs("st_tmp/obj_rwbyte.bin"));
    icall_file_delete(Cs("st_tmp/obj_rwe.bin"));
    icall_file_delete(Cs("st_tmp/obj2.bin"));
    (void)IL2CPP_TEST_RMDIR("st_tmp");
    Expect(icall_directory_exists(Cs("st_tmp")) == 0,
           "test directory is fully removed after cleanup");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d streamio assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime streamio tests passed\n");
    return 0;
}
