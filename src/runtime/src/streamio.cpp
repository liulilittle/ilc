//------------------------------------------------------------------------------
// streamio.cpp -- System.IO stream icall implementations (see streamio.h for
// the contract and result conventions).
//
// Backends:
//   * File streams wrap a FILE* opened through _wfopen on Windows -- the
//     managed string is UTF-16, which IS the wchar_t encoding there, so the
//     units pass through directly (a NUL-terminated copy is required: the
//     managed string itself carries no terminator, see il2cpp_string_new).
//     On other platforms the path is transcoded to UTF-8 for fopen.
//     64-bit positions/lengths use the wide fseek surface (_fseeki64 /
//     _ftelli64 on Windows, fseeko / ftello elsewhere -- MinGW-w64 provides
//     both families, so the _WIN32 branch below compiles on MSVC and MinGW).
//   * Memory streams use a malloc/realloc buffer with an explicit position,
//     length (bytes written) and capacity; growth doubles from 64 bytes.
//
// Text encoding: read_line decodes UTF-8 (full BMP + surrogate pairs, U+FFFD
// for invalid sequences); write_line encodes UTF-8 and appends CRLF -- the
// .NET Framework default on Windows, aligned with io.cpp's WriteAllLines.
// The two transcoders (Utf8ToCs / CsToUtf8) mirror the io.cpp versions;
// they live in io.cpp's anonymous namespace, so they are re-implemented here
// to keep this module self-contained (per-file runtime domains).
//------------------------------------------------------------------------------

#include "streamio.h"

#include "il2cpp_array.h"
#include "il2cpp_com.h"           // stage 12: the ABI-safe IStream helpers the kCom backend delegates to
#include "il2cpp_exception.h"     // il2cpp_throw_new (ctor failures)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_* / not_supported
#include "il2cpp_metadata.h"      // System_Object_class (descriptor bases)
#include "il2cpp_runtime.h"       // il2cpp_class_register
#include "platform.h"             // plat_path_is_file (exclusive-create probe)
#include "il2cpp_streamio.h"      // the object model (this file's surface)
#include "decimal.h"              // Il2CppDecimal (binary decimal protocol)
#include "il2cpp_encoding.h"      // P2(5) A1: the Encoding object (page/emitBom)
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#if defined(_WIN32)
    // MSVC and MinGW-w64 both provide the 64-bit wide fseek family.
    #define STREAM_FSEEK(f, o, w) ::_fseeki64((f), (o), (w))
    #define STREAM_FTELL(f) ::_ftelli64(f)
    #include <io.h> // _fileno / _chsize_s (SetLength file backend)
#else
    // POSIX (GCC 7.5 baseline: no std::filesystem, fseeko/ftello are the
    // 64-bit-capable surface on 32-bit hosts).
    #include <algorithm>   // std::replace (path normalization)
    #include <sys/types.h>
    #include <unistd.h> // fileno / ftruncate (SetLength file backend)
    #define STREAM_FSEEK(f, o, w) ::fseeko((f), (o), (w))
    #define STREAM_FTELL(f) ::ftello(f)
#endif

namespace il2cpp { namespace runtime {

#if !defined(_WIN32)
// Normalize Windows-style '\' separators from managed paths to the POSIX '/'
// the host filesystem requires (create/probe/delete must agree on one name
// space -- NDK residual-delete regression: File.Open("a\b") used to fopen a
// literal backslash entry).
std::string NormalizePosixSlash(const std::string& path)
{
    std::string norm = path;
    std::replace(norm.begin(), norm.end(), '\\', '/');
    return norm;
}
#endif

//------------------------------------------------------------------------------
// Stream backend state (opaque to callers; see streamio.h).
//
// THREADING: this struct is deliberately lock-free and is NOT thread-safe.
// net48 FileStream/StreamReader/StreamWriter instances are documented as not
// thread-safe either, and the runtime's wrappers SHARE one StreamState between
// a reader/writer and its underlying stream, so a lock here would have to
// cover the whole wrapper chain. Callers must serialize per handle.
//------------------------------------------------------------------------------

struct StreamState
{
    enum Kind
    {
        kFile,   // FILE* backend (position/length via 64-bit fseek)
        kMemory, // malloc buffer backend (explicit pos/length/capacity)
        kCom     // stage 12: a wrapped native IStream* backend (COM stream)
    };

    Kind           kind;
    FILE*          fp;        // kFile: the underlying handle
    unsigned char* buf;       // kMemory: the byte buffer (malloc'd)
#if defined(_WIN32)
    void*          iStream;          // kCom: the wrapped IStream* (owned reference)
    bool           fDeleteOnRelease; // kCom: CreateStreamOnHGlobal's fDeleteOnRelease
#endif
    int64_t        pos;       // kMemory: current position
    int64_t        length;    // kMemory: bytes written so far
    int64_t        capacity;  // kMemory: allocated buffer size
    // io2 B1 (MemoryStream ctor family): the net48 MemoryStream state matrix.
    //   bufOwned      -- true: 'buf' is the stream's own malloc buffer;
    //                    false: 'buf' POINTS INTO externalArray's payload (the
    //                    net48 array ctors share the caller's array, so writes
    //                    are visible through it and GetBuffer returns IT).
    //   externalArray -- the shared managed byte[] (GC ref; registered as a
    //                    static root so the borrowed payload never moves or
    //                    dies under the collector).
    //   exposable     -- GetBuffer() is legal (net48 publiclyVisible / the
    //                    bufferless ctors).
    //   expandable    -- capacity may grow (net48: the OWN buffer only; an
    //                    array-seeded stream is fixed-size and a write past
    //                    Capacity throws NotSupportedException).
    //   capacity is the net48 Capacity: for a borrowed array it is the WINDOW
    //   length (count), never the array length.
    bool           bufOwned;
    Il2CppObject*  externalArray;
    bool           exposable;
    bool           expandable;
    bool           canRead;   // capability flags from the open mode
    bool           canWrite;
    bool           canSeek;   // net48: true in every mode (append too -- only
                              // write placement differs, "ab" writes land at EOF)
    int32_t        refCount;  // live handles over this state (reader/writer wrappers share it)
    bool           closed;    // close ran: fp/buf released; further ops fail cleanly
    int32_t        pending[4];   // line-reader pushback ring (CRLF lookahead + BOM peek)
    int32_t        pendingCount; // valid entries at the FRONT of pending[]
    int32_t        textEncoding; // 0=UTF8 (default), 1=UTF16LE, 2=UTF16BE (reader BOM)
    bool           appendMode;  // kFile opened "ab": append-start guards live
    int64_t        appendStart; // the EOF position captured at open
    // P2(5) StreamWriter(path,bool,Encoding): the TEXT encoder the writer
    // applies to Write(string)/WriteLine(string) -- 0=UTF-8, 1=UTF-16LE,
    // 2=ASCII. Independent of textEncoding (which is the READER's BOM/encoding
    // detection result). Default 0 == the historical UTF-8 behaviour.
    int32_t        writerEncoding;
    bool           writerBomPending; // emit the encoding preamble before the first text write (net48: only when the target starts at position 0)
    // T4b ReadChar astral stash: a 4-byte UTF-8 sequence decodes to a
    // surrogate PAIR, but ReadChar returns one UTF-16 unit per call. The
    // first call returns the high surrogate and stashes the low surrogate
    // here for the next ReadChar (-1 = empty). Cleared by every seek
    // (repositioning invalidates decoder state, like the pushback ring).
    int32_t        pendingChar;
    // kFile stdio state that used to be refreshed by an identity fseek
    // (fseek(fp,0,SEEK_CUR)) before EVERY byte read:
    //   * readAfterWrite -- C11 7.21.5.3 forbids a read directly after a
    //     write on an update stream; the mode reset must run once, before the
    //     next read, not once per byte.
    //   * atEof -- the EOF indicator is sticky; net48 exposes bytes appended
    //     by another handle after a read reached end-of-stream, so the next
    //     read has to clear the indicator (an identity seek does that).
    // Any explicit seek / truncate already performs a real fseek, so it
    // clears both flags.
    bool           fileReadAfterWrite;
    bool           fileAtEof;
};

namespace {

//------------------------------------------------------------------------------
// UTF-16 <-> UTF-8 transcoding (managed string <-> line content).
// Mirrors the io.cpp versions (see the file header for the rationale).
//------------------------------------------------------------------------------

// Is 'c' a UTF-8 continuation byte (10xxxxxx)?
bool IsContinuationByte(unsigned char c)
{
    return (c & 0xC0) == 0x80;
}

// UTF-8 bytes (explicit length; embedded NULs are content) -> managed string.
// Valid BMP sequences decode to single units, astral sequences to surrogate
// pairs, invalid sequences to U+FFFD (the .NET replacement character).
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
        const unsigned char c = static_cast<unsigned char>(data[i]);
        if (c < 0x80)
        {
            units.push_back(static_cast<char16_t>(c));
            ++i;
        }
        else if ((c & 0xE0) == 0xC0 && i + 1 < length
                 && IsContinuationByte(static_cast<unsigned char>(data[i + 1])))
        {
            const char16_t u = static_cast<char16_t>(
                ((c & 0x1Fu) << 6) | (static_cast<unsigned char>(data[i + 1]) & 0x3Fu));
            if (u >= 0x80)
            {
                units.push_back(u);
                i += 2;
            }
            else
            {
                units.push_back(0xFFFD); // overlong encoding -> replacement
                ++i;
            }
        }
        else if ((c & 0xF0) == 0xE0 && i + 2 < length
                 && IsContinuationByte(static_cast<unsigned char>(data[i + 1]))
                 && IsContinuationByte(static_cast<unsigned char>(data[i + 2])))
        {
            const uint32_t cp = (static_cast<uint32_t>(c & 0x0Fu) << 12)
                | (static_cast<uint32_t>(static_cast<unsigned char>(data[i + 1]) & 0x3Fu) << 6)
                | static_cast<uint32_t>(static_cast<unsigned char>(data[i + 2]) & 0x3Fu);
            if (cp >= 0x800 && (cp < 0xD800 || cp > 0xDFFF))
            {
                units.push_back(static_cast<char16_t>(cp));
                i += 3;
            }
            else
            {
                units.push_back(0xFFFD); // surrogate range / overlong -> replacement
                ++i;
            }
        }
        else if ((c & 0xF8) == 0xF0 && i + 3 < length
                 && IsContinuationByte(static_cast<unsigned char>(data[i + 1]))
                 && IsContinuationByte(static_cast<unsigned char>(data[i + 2]))
                 && IsContinuationByte(static_cast<unsigned char>(data[i + 3])))
        {
            const uint32_t cp = (static_cast<uint32_t>(c & 0x07u) << 18)
                | (static_cast<uint32_t>(static_cast<unsigned char>(data[i + 1]) & 0x3Fu) << 12)
                | (static_cast<uint32_t>(static_cast<unsigned char>(data[i + 2]) & 0x3Fu) << 6)
                | static_cast<uint32_t>(static_cast<unsigned char>(data[i + 3]) & 0x3Fu);
            if (cp >= 0x10000 && cp <= 0x10FFFF)
            {
                const uint32_t v = cp - 0x10000;
                units.push_back(static_cast<char16_t>(0xD800 + (v >> 10)));
                units.push_back(static_cast<char16_t>(0xDC00 + (v & 0x3FFu)));
                i += 4;
            }
            else
            {
                units.push_back(0xFFFD); // out of range -> replacement
                ++i;
            }
        }
        else
        {
            units.push_back(0xFFFD); // lone continuation / lead byte -> replacement
            ++i;
        }
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(units.data(), static_cast<int32_t>(units.size())));
}

// Managed string -> UTF-8 bytes. Surrogate pairs combine into 4-byte
// sequences; lone surrogates encode as 3-byte sequences (best effort).
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
// Memory-stream buffer growth (doubling from 64 bytes; malloc/realloc).
//------------------------------------------------------------------------------

// Grows the buffer until 'need' bytes fit. False on realloc failure (the
// existing buffer is left untouched -- no partial state) or when the state is
// NOT expandable (the net48 array-seeded / window MemoryStream ctors -- a
// write past Capacity is a NotSupportedException, see icall_stream_write_byte).
bool MemoryEnsure(StreamState* s, int64_t need)
{
    if (need <= s->capacity)
    {
        return true;
    }
    if (!s->expandable)
    {
        return false; // fixed-size buffer: the caller raises NotSupportedException
    }
    int64_t cap = (s->capacity > 0) ? s->capacity : 64;
    while (cap < need)
    {
        cap *= 2;
    }
    unsigned char* grown = static_cast<unsigned char*>(
        std::realloc(s->buf, static_cast<size_t>(cap)));
    if (grown == nullptr)
    {
        return false;
    }
    s->buf = grown;
    s->capacity = cap;
    return true;
}

// Counted identity seek (fseek(fp, 0, SEEK_CUR)). The test build defines
// IL2CPP_STREAMIO_FSEEK_PROBE (tests/runtime/CMakeLists.txt) to expose the
// call counter to the regression probe; production builds compile the counter
// away. The definitions are plain C symbols so a test translation unit can
// read them without any runtime header change.
#if defined(IL2CPP_STREAMIO_FSEEK_PROBE)
extern "C" {
long long g_streamioIdentitySeeks = 0;
// Test seam: force the identity seek to fail so the probe can prove the
// "failed reset is surfaced, never read in an illegal stdio mode" contract.
// Never set outside tests.
int g_streamioForceSeekFailure = 0;
}
#endif

// File-local (internal linkage): the only caller is FilePrepareRead below.
static int FileIdentitySeek(StreamState* s)
{
#if defined(IL2CPP_STREAMIO_FSEEK_PROBE)
    ++g_streamioIdentitySeeks;
    if (g_streamioForceSeekFailure != 0)
    {
        return -1;
    }
#endif
    return STREAM_FSEEK(s->fp, 0, SEEK_CUR);
}

// Prepare the FILE* for the next read. Two jobs used to be done by an identity
// fseek (fseek(fp,0,SEEK_CUR)) before EVERY byte:
//   * C11 7.21.5.3 -- a read must not directly follow a write on an update
//     stream. The mode reset is needed ONCE after a write, not per byte.
//   * the EOF indicator is sticky -- net48 exposes bytes appended by another
//     handle after a read reached end-of-stream, so the indicator must be
//     cleared before retrying. Only done when EOF was actually observed, so
//     the steady-state read path issues no fseek at all.
//
// If the reset CANNOT be performed the read must not proceed: C11 7.21.5.3
// makes a read straight after a write undefined, so continuing would return
// arbitrary bytes. The failure is raised as an IOException (the same shape the
// handle layer already uses for the append-mode guard), and the pending flags
// stay set so an explicit seek -- a real reposition that clears them --
// recovers the handle. The steady state costs no syscall.
int FilePrepareRead(StreamState* s)
{
    if (!s->fileReadAfterWrite && !s->fileAtEof)
    {
        return 0; // steady state: no reset pending, no syscall
    }
    if (FileIdentitySeek(s) != 0)
    {
        return -1;
    }
    s->fileReadAfterWrite = false;
    s->fileAtEof = false;
    return 0;
}

// Throw the IOException a read raises when its pending stdio mode reset could
// not be performed (C11 7.21.5.3 would otherwise be violated). Defined below
// next to kStreamIOExceptionChain, which the write/seek paths share.
//
// net48 has NO such exception: a Win32 handle lets WriteFile/ReadFile alternate
// freely, and an unseekable stream raises NotSupportedException from
// Seek/Position, never from Read. This IOException therefore signals a
// BACKEND-side failure, not modelled net48 behaviour. It is also
// production-unreachable as the layer stands: the only FILE* created here
// comes from _wfopen/fopen (no popen/fdopen/_fdopen in this file), so the
// target is always a seekable regular file; the tests reach the branch through
// g_streamioForceSeekFailure.
[[noreturn]] void FileThrowReadResetFailed();

// Record that the FILE* moved to the write side (C11 7.21.5.3 reset pending).
void FileMarkWrite(StreamState* s)
{
    s->fileReadAfterWrite = true;
    s->fileAtEof = false;
}

// Next byte from the stream (-1 at end-of-stream). Dispatches on the
// backend so read_line is shared between file and memory streams.
int StreamGetByte(StreamState* s)
{
    if (s->pendingCount > 0)
    {
        const int32_t b = s->pending[0];
        for (int32_t i = 1; i < s->pendingCount; ++i)
        {
            s->pending[i - 1] = s->pending[i];
        }
        --s->pendingCount;
        return b;
    }
    if (s->kind == StreamState::kFile)
    {
        if (FilePrepareRead(s) != 0)
        {
            FileThrowReadResetFailed(); // never read in an illegal stdio mode
        }
        const int c = std::getc(s->fp); // EOF -> -1
        if (c < 0)
        {
            s->fileAtEof = true;
        }
        return c;
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Single-byte IStream::Read (the stage-12 kCom delegation).
        unsigned char byte = 0;
        uint32_t got = 0;
        const int32_t hr = il2cpp_com_stream_read(
            reinterpret_cast<intptr_t>(s->iStream), &byte, 1, &got);
        return (hr == kComHrOk && got == 1) ? byte : -1;
    }
#endif
    if (s->pos >= s->length)
    {
        return -1;
    }
    return s->buf[s->pos++];
}

// Push bytes back in front of the stream (replayed in the given ORDER). The
// ring holds at most 4 entries; callers never exceed that (CRLF lookahead
// pushes 1, the BOM peek pushes at most 3 when the ring is empty).
void StreamUnread(StreamState* s, const int32_t* bytes, int32_t count)
{
    for (int32_t i = count - 1; i >= 0; --i)
    {
        if (s->pendingCount >= 4)
        {
            break; // defensive: never overrun the ring
        }
        for (int32_t j = s->pendingCount; j > 0; --j)
        {
            s->pending[j] = s->pending[j - 1];
        }
        s->pending[0] = bytes[i];
        ++s->pendingCount;
    }
}

//------------------------------------------------------------------------------
// Refcounted close (the OBJECT layer shares one StreamState between a
// FileStream/MemoryStream and its wrapping StreamReader/StreamWriter -- net48
// ownership: closing the wrapper closes the underlying file). The first close
// releases the backend (fclose the FILE*, free the memory buffer) and flips
// 'closed'; every close drops refCount and frees the state at zero. Closing an
// already-closed state never touches the backend again (no double-fclose).
//------------------------------------------------------------------------------
void StreamCloseHandle(StreamState* s)
{
    if (s == nullptr)
    {
        return;
    }
    if (!s->closed)
    {
        s->closed = true;
        if (s->kind == StreamState::kFile && s->fp != nullptr)
        {
            std::fclose(s->fp);
            s->fp = nullptr;
        }
#if defined(_WIN32)
        if (s->kind == StreamState::kCom && s->iStream != nullptr)
        {
            // Release the wrapped IStream reference (stage 12 -- the runtime
            // holds the reference CreateStreamOnHGlobal handed it). The
            // HGLOBAL lifetime is the native stream's own: its Release frees
            // the HGLOBAL exactly when fDeleteOnRelease was set at creation.
            il2cpp_com_stream_release(reinterpret_cast<intptr_t>(s->iStream));
            s->iStream = nullptr;
        }
#endif
        // net48 parity (2026-10-05): a closed MemoryStream RETAINS its data --
        // MemoryStream.ToArray() still returns the bytes after Close (oracle
        // 0xDEAD / 0x07000000). The buffer is therefore NOT freed here; it is
        // released together with the state at the deferred deletion below.
    }
    if (--s->refCount <= 0)
    {
        if (s->kind == StreamState::kMemory)
        {
            // The owning MemoryStream object keeps a LIVE handle over this
            // state after Close (see icall_stream_close -- net48: the data
            // stays reachable). Defer the deletion: the state (and its
            // retained buffer) is freed at process teardown, mirroring net48's
            // "buffer retained until the object is collected".
            s->refCount = 0;
            return;
        }
        delete s;
    }
}

} // namespace

//------------------------------------------------------------------------------
// FileStream.
//------------------------------------------------------------------------------

il2cpp_stream_t il2cpp_stream_file_open(Il2CppObject* path, int32_t mode)
{
    if (path == nullptr || mode < 0 || mode > 9)
    {
        return nullptr;
    }
    // net48 FileMode fidelity (ioaudit2): modes 5/8 are EXCLUSIVE creates,
    // 6 never creates, 7 truncates an existing file (missing -> fail),
    // 9 seeds an empty file before a read-only open.
    const bool exclusiveCreate = (mode == 5 || mode == 8);
    const bool allowCreate = (mode != 6 && mode != 7);
    const bool truncateExisting = (mode == 7);
    const bool createEmptyFirst = (mode == 9);
    const int32_t baseMode = (mode == 5) ? 1
        : (mode == 8) ? 4
        : (mode == 6 || mode == 7) ? 3
        : (mode == 9) ? 0
        : mode;
    const std::string utf8 = CsToUtf8(path);
    if (exclusiveCreate && plat_path_is_file(utf8.c_str()))
    {
        return nullptr; // already exists (the ctor raises IOException)
    }
    FILE* fp = nullptr;
#if defined(_WIN32)
    const Il2CppString* str = reinterpret_cast<Il2CppString*>(path);
    // UTF-16 managed units -> NUL-terminated wchar_t path (_wfopen).
    std::vector<wchar_t> wpath(static_cast<size_t>(str->length) + 1);
    for (int32_t i = 0; i < str->length; ++i)
    {
        wpath[static_cast<size_t>(i)] = static_cast<wchar_t>(str->chars[i]);
    }
    wpath[static_cast<size_t>(str->length)] = 0;
    if (createEmptyFirst && !plat_path_is_file(utf8.c_str()))
    {
        FILE* seed = nullptr;
#if defined(_MSC_VER)
        if (::_wfopen_s(&seed, wpath.data(), L"wb") != 0)
        {
            seed = nullptr;
        }
#else
        seed = ::_wfopen(wpath.data(), L"wb");
#endif
        if (seed != nullptr)
        {
            std::fclose(seed);
        }
    }
    static const wchar_t* kModes[5] = { L"rb", L"wb", L"ab", L"r+b", L"w+b" };
#if defined(_MSC_VER)
    if (::_wfopen_s(&fp, wpath.data(), kModes[baseMode]) != 0)
    {
        fp = nullptr;
    }
#else
    fp = ::_wfopen(wpath.data(), kModes[baseMode]);
#endif
    if (fp == nullptr && allowCreate && baseMode == 3)
    {
        // Read-write on a file that does not exist yet: "r+b" fails, so
        // fall back to "w+b" -- nothing existed, so nothing is truncated.
        // (Only the OpenOrCreate mode reaches here: 6/7 disallow creation.)
#if defined(_MSC_VER)
        if (::_wfopen_s(&fp, wpath.data(), L"w+b") != 0)
        {
            fp = nullptr;
        }
#else
        fp = ::_wfopen(wpath.data(), L"w+b");
#endif
    }
#else
    const std::string p = NormalizePosixSlash(utf8);
    if (createEmptyFirst && !plat_path_is_file(p.c_str()))
    {
        FILE* seed = std::fopen(p.c_str(), "wb");
        if (seed != nullptr)
        {
            std::fclose(seed);
        }
    }
    static const char* kModes[5] = { "rb", "wb", "ab", "r+b", "w+b" };
    fp = std::fopen(p.c_str(), kModes[baseMode]);
    if (fp == nullptr && allowCreate && baseMode == 3)
    {
        fp = std::fopen(p.c_str(), "w+b");
    }
#endif
    if (fp == nullptr)
    {
        return nullptr;
    }
    if (truncateExisting)
    {
#if defined(_WIN32)
        ::_chsize_s(::_fileno(fp), 0);
#else
        ::ftruncate(::fileno(fp), 0);
#endif
    }
    int64_t appendStart = 0;
    if (mode == 2)
    {
        // net48 Append: the stream opens AT EOF (Position == old length);
        // record the append start for the seek/setlength guards.
        if (std::fseek(fp, 0, SEEK_END) != 0)
        {
            std::fclose(fp);
            return nullptr;
        }
        appendStart = static_cast<int64_t>(STREAM_FTELL(fp));
    }
    StreamState* s = new StreamState;
    s->kind = StreamState::kFile;
    s->fp = fp;
    s->buf = nullptr;
    s->pos = 0;
    s->length = 0;
    s->capacity = 0;
    s->canRead = (baseMode == 0 || baseMode == 3 || baseMode == 4);
    s->canWrite = (baseMode != 0);
    s->canSeek = true; // net48 probe: even append-mode FileStream reports
                       // CanSeek=true (only write placement differs -- "ab"
                       // writes land at end-of-file regardless of the position)
    s->refCount = 1;
    s->closed = false;
    s->pendingCount = 0;
    s->textEncoding = 0;
    s->appendMode = (mode == 2);
    s->appendStart = appendStart;
    s->writerEncoding = 0;
    s->writerBomPending = false;
    s->pendingChar = -1; // T4b: ReadChar astral stash starts empty
    s->fileReadAfterWrite = false;
    s->fileAtEof = false;
    return s;
}

int32_t il2cpp_stream_read(il2cpp_stream_t stream, void* buffer, int32_t count)
{
    StreamState* s = stream;
    if (s == nullptr || buffer == nullptr || s->closed)
    {
        return -1;
    }
    if (count < 0)
    {
        return -1;
    }
    if (count == 0)
    {
        return 0;
    }
    if (!s->canRead)
    {
        return -1;
    }
    // The pushback ring holds bytes peeked by a text-reader ctor (BOM
    // detection). net48's detection seeks the underlying stream back, so a
    // raw Read after the probe observes those bytes; the shared StreamState
    // must replay the ring first or mixing readers reorders/drops bytes
    // (a1ringx: StreamReader ctor -> BinaryReader.ReadInt32 -> ReadToEnd).
    char* out = static_cast<char*>(buffer);
    int32_t drained = 0;
    while (drained < count && s->pendingCount > 0)
    {
        const int b = StreamGetByte(s);
        if (b < 0)
        {
            break;
        }
        out[drained++] = static_cast<char>(b);
    }
    if (drained == count)
    {
        return drained;
    }
    const int32_t remaining = count - drained;
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Read (the ABI-safe helper).
        uint32_t got = 0;
        const int32_t hr = il2cpp_com_stream_read(
            reinterpret_cast<intptr_t>(s->iStream), out + drained, remaining, &got);
        if (hr != kComHrOk)
        {
            return (drained > 0) ? drained : -1;
        }
        return drained + static_cast<int32_t>(got);
    }
#endif
    if (s->kind == StreamState::kFile)
    {
        // C11 7.21.5.3: on an update-mode stream a READ must not be directly
        // followed by a WRITE without an intervening fseek. net48 mixes
        // ReadByte/WriteByte freely (Win32 handles), so the stdio backend must
        // reset the mode with an identity seek before a file write. The reset
        // is per CALL (bulk read), never per byte.
        if (FilePrepareRead(s) != 0)
        {
            FileThrowReadResetFailed(); // never read in an illegal stdio mode
        }
        const size_t got = std::fread(out + drained, 1, static_cast<size_t>(remaining), s->fp);
        if (got < static_cast<size_t>(remaining))
        {
            s->fileAtEof = true; // short read: the EOF indicator is now set
        }
        return drained + static_cast<int32_t>(got);
    }
    if (s->pos >= s->length)
    {
        return drained; // end-of-stream (ring-only reads already returned)
    }
    const int64_t avail = s->length - s->pos;
    const size_t n = (static_cast<int64_t>(remaining) < avail)
        ? static_cast<size_t>(remaining) : static_cast<size_t>(avail);
    std::memcpy(out + drained, s->buf + s->pos, n);
    s->pos += static_cast<int64_t>(n);
    return drained + static_cast<int32_t>(n);
}

// net48 append-mode write guard chain (IO_SeekAppendOverwrite): FileStream
// opened with FileMode.Append rejects a WRITE at a position before the
// open-time EOF; Seek itself is allowed (the guard lives in the write path).
// Also used by FileThrowReadResetFailed above.
const char kStreamIOExceptionChain[] =
    "System.IO.IOException,System.IO.IOException,System.SystemException,System.Exception";

namespace {
// Definition of the read-reset failure thrower declared next to
// FilePrepareRead (C11 7.21.5.3 would be violated if the read proceeded).
// net48 counterpart: none -- see the declaration comment above.
[[noreturn]] void FileThrowReadResetFailed()
{
    il2cpp_throw_new(kStreamIOExceptionChain,
        "The stream could not be repositioned for reading.");
}
} // namespace

int32_t il2cpp_stream_write(il2cpp_stream_t stream, const void* buffer, int32_t count)
{
    StreamState* s = stream;
    if (s == nullptr || buffer == nullptr || s->closed)
    {
        return -1;
    }
    if (count < 0)
    {
        return -1;
    }
    if (count == 0)
    {
        return 0;
    }
    if (!s->canWrite)
    {
        return -1;
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Write (the ABI-safe helper).
        uint32_t written = 0;
        const int32_t hr = il2cpp_com_stream_write(
            reinterpret_cast<intptr_t>(s->iStream), buffer, count, &written);
        return (hr == kComHrOk) ? static_cast<int32_t>(written) : -1;
    }
#endif
    if (s->kind == StreamState::kFile)
    {
        if (s->appendMode && il2cpp_stream_get_position(s) < s->appendStart)
        {
            // net48 FileStream.Write in append mode throws IOException when
            // the position is before the open-time EOF (IO_SeekAppendOverwrite).
            // Seek is unaffected (see il2cpp_stream_seek).
            il2cpp_throw_new(kStreamIOExceptionChain,
                "Unable to overwrite data that existed at open time.");
        }
        // Read->write mode reset (C11 7.21.5.3) -- see il2cpp_stream_read.
        // One identity seek per write call (never per byte), and the state
        // flags keep the next byte read from repeating it.
        (void)STREAM_FSEEK(s->fp, 0, SEEK_CUR);
        FileMarkWrite(s);
        return static_cast<int32_t>(std::fwrite(buffer, 1, static_cast<size_t>(count), s->fp));
    }
    const int64_t end = s->pos + static_cast<int64_t>(count);
    if (end > s->capacity && !MemoryEnsure(s, end))
    {
        return -1; // allocation failure: nothing written
    }
    std::memcpy(s->buf + s->pos, buffer, static_cast<size_t>(count));
    s->pos = end;
    if (end > s->length)
    {
        s->length = end; // a write past the previous end extends the stream
    }
    return count;
}

int32_t il2cpp_stream_flush(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed)
    {
        return 1;
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Commit (STGC_DEFAULT = 0 -- the
        // HGLOBAL-backed stream is memory, but the COM contract still asks
        // Commit to persist).
        return (il2cpp_com_stream_commit(reinterpret_cast<intptr_t>(s->iStream)) == kComHrOk) ? 0 : 1;
    }
#endif
    if (s->kind == StreamState::kMemory)
    {
        return 0; // no buffering to flush
    }
    return std::fflush(s->fp) == 0 ? 0 : 1;
}

int32_t il2cpp_stream_close(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr)
    {
        return 1;
    }
    // Refcounted close: releases the backend on the first close and frees the
    // state when the last live handle closes (shared reader/writer wrappers --
    // see StreamCloseHandle). A caller that never shares a state sees the same
    // single-owner semantics as before (fclose + free on this call).
    StreamCloseHandle(s);
    return 0;
}

int64_t il2cpp_stream_get_position(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed)
    {
        return -1;
    }
    // Logical position: net48 exposes no hidden pushback, so ring bytes that
    // were peeked (BOM detection / CRLF lookahead) but not logically consumed
    // must not count (a6seek: Position right after the reader ctor is 0, not
    // the physical 3).
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Seek(0, STREAM_SEEK_CUR) -> the
        // current position (no move).
        int64_t pos = 0;
        if (il2cpp_com_stream_seek(reinterpret_cast<intptr_t>(s->iStream), 0, 1, &pos) != kComHrOk || pos < 0)
        {
            return -1;
        }
        return pos - s->pendingCount;
    }
#endif
    if (s->kind == StreamState::kMemory)
    {
        return s->pos - s->pendingCount;
    }
    const int64_t physical = static_cast<int64_t>(STREAM_FTELL(s->fp)); // -1 on error
    return physical < 0 ? physical : physical - s->pendingCount;
}

int32_t il2cpp_stream_set_position(il2cpp_stream_t stream, int64_t position)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || position < 0)
    {
        return -1;
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Seek(position, STREAM_SEEK_SET).
        int64_t out = 0;
        if (il2cpp_com_stream_seek(reinterpret_cast<intptr_t>(s->iStream), position, 0, &out) != kComHrOk)
        {
            return -1; // position unchanged on failure
        }
        s->pendingCount = 0; // explicit reposition discards the pushback ring
        s->pendingChar = -1; // ... and the ReadChar astral stash (T4b)
        return 0;
    }
#endif
    if (s->kind == StreamState::kMemory)
    {
        s->pos = position; // seeking past the end is allowed; writes extend
        s->pendingCount = 0;
        s->pendingChar = -1;
        return 0;
    }
    if (STREAM_FSEEK(s->fp, position, SEEK_SET) != 0)
    {
        return -1; // position unchanged on failure
    }
    s->pendingCount = 0;
    s->pendingChar = -1;
    s->fileReadAfterWrite = false; // a real seek is a legal mode reset
    s->fileAtEof = false;          // ... and it clears the EOF indicator
    return 0;
}

Il2CppObject* il2cpp_stream_read_line(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || !s->canRead)
    {
        return nullptr;
    }
    if (s->textEncoding != 0)
    {
        // UTF-16 text stream (BOM-detected at reader construction): read
        // 16-bit units; '\r', '\n' and "\r\n" terminate at the unit level.
        std::vector<char16_t> units;
        bool sawUnit = false;
        for (;;)
        {
            const int lo = StreamGetByte(s);
            if (lo < 0)
            {
                break;
            }
            const int hi = StreamGetByte(s);
            if (hi < 0)
            {
                units.push_back(static_cast<char16_t>(lo)); // lone trailing byte
                sawUnit = true;
                break;
            }
            sawUnit = true;
            const char16_t unit = (s->textEncoding == 1)
                ? static_cast<char16_t>(lo | (hi << 8))
                : static_cast<char16_t>((lo << 8) | hi);
            if (unit == u'\n')
            {
                break;
            }
            if (unit == u'\r')
            {
                const int lo2 = StreamGetByte(s);
                if (lo2 >= 0)
                {
                    const int hi2 = StreamGetByte(s);
                    const char16_t next = (hi2 < 0)
                        ? static_cast<char16_t>(lo2)
                        : (s->textEncoding == 1
                            ? static_cast<char16_t>(lo2 | (hi2 << 8))
                            : static_cast<char16_t>((lo2 << 8) | hi2));
                    if (next != u'\n')
                    {
                        // Not a CRLF pair: restore the peeked unit.
                        if (hi2 >= 0)
                        {
                            const int32_t put[2] = { lo2, hi2 };
                            StreamUnread(s, put, 2);
                        }
                        else
                        {
                            const int32_t put = lo2;
                            StreamUnread(s, &put, 1);
                        }
                    }
                }
                break;
            }
            units.push_back(unit);
        }
        if (!sawUnit)
        {
            return nullptr;
        }
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(
            reinterpret_cast<const char16_t*>(units.data()),
            static_cast<int32_t>(units.size())));
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Documented: no line codec over a COM stream -- the line reader is
        // the file/memory text surface (stage-12 divergence, pinned by the
        // kCom delegation contract).
        return nullptr;
    }
#endif
    std::vector<char> bytes;
    bool sawByte = false;
    for (;;)
    {
        const int c = StreamGetByte(s);
        if (c < 0)
        {
            break; // end-of-stream
        }
        sawByte = true;
        if (c == '\n')
        {
            break;
        }
        if (c == '\r')
        {
            // net48: '\r', '\n' and "\r\n" all terminate a line. A '\r'
            // peeks the next byte: '\n' completes the CRLF pair, anything
            // else (including EOF) is pushed back for the next read.
            const int next = StreamGetByte(s);
            if (next >= 0 && next != '\n')
            {
                const int32_t put = next;
                StreamUnread(s, &put, 1);
            }
            break;
        }
        bytes.push_back(static_cast<char>(c));
    }
    if (!sawByte)
    {
        return nullptr; // end-of-stream without a line start
    }
    return Utf8ToCs(bytes.data(), bytes.size());
}

// P2(5) A1: encode 'text' with the writer's selected encoding (writerEncoding
// 0=UTF-8, 1=UTF-16LE, 2=ASCII) and write it, emitting the encoding preamble
// first when writerBomPending (the first text write after a truncating open).
// A null text emits nothing (net48 Write(null) no-op). Returns 0 on success,
// -1 on a short write.
int32_t WriterEncodeAndWrite(StreamState* s, Il2CppObject* text)
{
    if (text == nullptr)
    {
        return 0; // net48: Write(null) emits nothing
    }
    if (s->writerBomPending)
    {
        s->writerBomPending = false;
        const uint8_t* bom = nullptr;
        int32_t bomLen = 0;
        if (s->writerEncoding == 0) { static const uint8_t k[3] = { 0xEF, 0xBB, 0xBF }; bom = k; bomLen = 3; }
        if (s->writerEncoding == 1) { static const uint8_t k[2] = { 0xFF, 0xFE }; bom = k; bomLen = 2; }
        if (bomLen > 0)
        {
            if (il2cpp_stream_write(s, bom, bomLen) != bomLen)
            {
                return -1;
            }
        }
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(text);
    if (s->writerEncoding == 1)
    {
        // UTF-16LE: the units' little-endian bytes (G12 little-endian host).
        const int32_t bytes = str->length * 2;
        if (bytes == 0)
        {
            return 0;
        }
        return il2cpp_stream_write(s, str->chars, bytes) == bytes ? 0 : -1;
    }
    if (s->writerEncoding == 2)
    {
        // ASCII: units above 0x7F encode as '?' (net48 ASCIIEncoding fallback).
        std::string ascii;
        ascii.reserve(static_cast<size_t>(str->length));
        for (int32_t i = 0; i < str->length; ++i)
        {
            const char16_t c = str->chars[i];
            ascii.push_back(c <= 0x7F ? static_cast<char>(c) : '?');
        }
        if (ascii.empty())
        {
            return 0;
        }
        return il2cpp_stream_write(s, ascii.data(), static_cast<int32_t>(ascii.size()))
            == static_cast<int32_t>(ascii.size()) ? 0 : -1;
    }
    const std::string utf8 = CsToUtf8(text);
    if (utf8.empty())
    {
        return 0;
    }
    return il2cpp_stream_write(s, utf8.data(), static_cast<int32_t>(utf8.size()))
        == static_cast<int32_t>(utf8.size()) ? 0 : -1;
}

int32_t il2cpp_stream_write_line(il2cpp_stream_t stream, Il2CppObject* line)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || !s->canWrite)
    {
        return -1;
    }
    // P2(5) A1: the writer's selected encoding (default UTF-8) + preamble.
    if (WriterEncodeAndWrite(s, line) != 0)
    {
        return -1;
    }
    // net48: the line terminator is written THROUGH the writer's encoding
    // (Encoding.Unicode writers emit the CRLF as 0d00 0a00, not raw 0d0a --
    // iopairprobe t_unicode). UTF-16 is the only codec with a wider cell.
    if (s->writerEncoding == 1)
    {
        static const char16_t crlf[2] = { 0x000D, 0x000A };
        return il2cpp_stream_write(stream, crlf, 4) == 4 ? 0 : -1;
    }
    return il2cpp_stream_write(stream, "\r\n", 2) == 2 ? 0 : -1;
}

// StreamWriter.Write(text): writes the text in the writer's encoding WITHOUT a
// line terminator (net48 Write vs WriteLine). A null string emits NOTHING (the
// empty encoding -- net48 TextWriter.Write(null) is a no-op). 0 on success,
// non-zero on failure (closed receiver, capability violation, short write).
int32_t il2cpp_stream_write_text(il2cpp_stream_t stream, Il2CppObject* text)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || !s->canWrite)
    {
        return -1;
    }
    return WriterEncodeAndWrite(s, text);
}

// StreamReader.ReadToEnd(): everything from the current position to the end
// as one UTF-8 decoded managed string. Returns nullptr for a null/closed
// stream or a read-capability violation; an empty remainder at end-of-stream
// returns an EMPTY string (net48: ReadToEnd at EOF returns "" not null).
Il2CppObject* il2cpp_stream_read_to_end(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || !s->canRead)
    {
        return nullptr;
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Documented: no text ReadToEnd over a COM stream (the text surface is
        // the file/memory line codec; a kCom stream returns nullptr -- the
        // stage-12 delegation contract).
        return nullptr;
    }
#endif
    if (s->textEncoding != 0)
    {
        // Decode the remaining bytes as UTF-16 (the pushback ring replays
        // first). A lone trailing byte decodes to its own unit (defensive).
        std::vector<char16_t> units;
        for (;;)
        {
            const int lo = StreamGetByte(s);
            if (lo < 0)
            {
                break;
            }
            const int hi = StreamGetByte(s);
            if (hi < 0)
            {
                units.push_back(static_cast<char16_t>(lo));
                break;
            }
            units.push_back(s->textEncoding == 1
                ? static_cast<char16_t>(lo | (hi << 8))
                : static_cast<char16_t>((lo << 8) | hi));
        }
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(
            reinterpret_cast<const char16_t*>(units.data()),
            static_cast<int32_t>(units.size())));
    }
    std::vector<char> bytes;
    // The BOM peek (ctor) may have pushed bytes back into the ring; they must
    // replay in front of the backend remainder. The raw backend reads below
    // bypass StreamGetByte, so drain the ring first (ioext rte_read lost the
    // first bytes of a MemoryStream reader without a BOM).
    while (s->pendingCount > 0)
    {
        const int b = StreamGetByte(s);
        if (b < 0)
        {
            break;
        }
        bytes.push_back(static_cast<char>(b));
    }
    if (s->kind == StreamState::kFile)
    {
        char buf[4096];
        if (FilePrepareRead(s) != 0)
        {
            FileThrowReadResetFailed(); // never read in an illegal stdio mode
        }
        for (;;)
        {
            const size_t n = std::fread(buf, 1, sizeof(buf), s->fp);
            if (n == 0)
            {
                s->fileAtEof = true;
                break; // end-of-stream
            }
            bytes.insert(bytes.end(), buf, buf + n);
        }
    }
    else
    {
        if (s->pos < s->length)
        {
            const int64_t avail = s->length - s->pos;
            const size_t base = bytes.size(); // keep the drained pushback prefix
            bytes.resize(base + static_cast<size_t>(avail));
            std::memcpy(bytes.data() + base, s->buf + s->pos,
                        static_cast<size_t>(avail));
            s->pos = s->length; // the read consumes the remainder
        }
    }
    return Utf8ToCs(bytes.data(), bytes.size());
}

int64_t il2cpp_stream_get_length(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed)
    {
        return -1;
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Stat (STATFLAG_NONAME) reads the
        // stream size through the platform-tailored STATSTG mirror.
        int64_t size = 0;
        return (il2cpp_com_stream_stat_size(reinterpret_cast<intptr_t>(s->iStream), &size) == kComHrOk)
            ? size : -1;
    }
#endif
    if (s->kind == StreamState::kMemory)
    {
        return s->length;
    }
    const int64_t saved = static_cast<int64_t>(STREAM_FTELL(s->fp));
    if (saved < 0 || STREAM_FSEEK(s->fp, 0, SEEK_END) != 0)
    {
        return -1;
    }
    const int64_t length = static_cast<int64_t>(STREAM_FTELL(s->fp));
    (void)STREAM_FSEEK(s->fp, saved, SEEK_SET); // restore; best effort
    // The probes above moved the FILE* with real seeks, so both the pending
    // C11 mode reset and the sticky EOF indicator are already cleared.
    s->fileReadAfterWrite = false;
    s->fileAtEof = false;
    return (length < 0) ? -1 : length;
}

int32_t il2cpp_stream_get_can_read(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    return (s != nullptr && !s->closed && s->canRead) ? 1 : 0;
}

int32_t il2cpp_stream_get_can_write(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    return (s != nullptr && !s->closed && s->canWrite) ? 1 : 0;
}

int32_t il2cpp_stream_get_can_seek(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    return (s != nullptr && !s->closed && s->canSeek) ? 1 : 0;
}

// Stream.Seek(offset, origin): moves to 'offset' relative to 'origin'
// (0=Begin, 1=Current, 2=End) and returns the NEW position. Seeking past the
// end is allowed (a later write extends the stream). Returns -1 for a
// null/closed stream, an invalid origin, or a resulting NEGATIVE position
// (a seek before the beginning -- callers validate and throw).
int64_t il2cpp_stream_seek(il2cpp_stream_t stream, int64_t offset, int32_t origin)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || origin < 0 || origin > 2 || !s->canSeek)
    {
        return -1;
    }
    int64_t base = 0;
    switch (origin)
    {
        case 0: base = 0; break;                            // SeekOrigin.Begin
        case 1: base = il2cpp_stream_get_position(s); break; // SeekOrigin.Current
        case 2: base = il2cpp_stream_get_length(s); break;   // SeekOrigin.End
    }
    if (base < 0)
    {
        return -1;
    }
    // Overflow guard: base + offset must stay inside int64 (INT64_MIN/MAX).
    if ((offset > 0 && base > INT64_MAX - offset)
        || (offset < 0 && base < INT64_MIN - offset))
    {
        return -1;
    }
    const int64_t newPos = base + offset;
    if (newPos < 0)
    {
        return -1; // before the beginning of the stream
    }
    if (s->appendMode && newPos < s->appendStart)
    {
        // net48 FileStream.Seek throws IOException when the resulting position
        // is before the open-time EOF (IO_SeekAppendOverwrite). The same guard
        // lives in the write path (il2cpp_stream_write).
        il2cpp_throw_new(kStreamIOExceptionChain,
            "Unable to seek to a position before the file's append start.");
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::Seek (the dedicated int64_t
        // LARGE_INTEGER form). The native stream applies the origin itself
        // (STREAM_SEEK_SET/CUR/END == the .NET SeekOrigin numbering).
        int64_t out = 0;
        const int32_t hr = il2cpp_com_stream_seek(
            reinterpret_cast<intptr_t>(s->iStream), offset, origin, &out);
        if (hr != kComHrOk || out < 0)
        {
            return -1; // position unchanged on failure
        }
        s->pendingCount = 0; // explicit reposition discards the pushback ring
        s->pendingChar = -1; // ... and the ReadChar astral stash (T4b)
        return out;
    }
#endif
    if (s->kind == StreamState::kMemory)
    {
        s->pos = newPos; // seeking past the end is allowed; writes extend
        s->pendingCount = 0;
        s->pendingChar = -1;
        return newPos;
    }
    if (STREAM_FSEEK(s->fp, newPos, SEEK_SET) != 0)
    {
        return -1; // position unchanged on failure
    }
    s->pendingCount = 0;
    s->pendingChar = -1;
    s->fileReadAfterWrite = false; // a real seek is a legal mode reset
    s->fileAtEof = false;          // ... and it clears the EOF indicator
    return newPos;
}

// Stream.SetLength(value): truncates or extends the stream to 'value' bytes.
// An extension is zero-filled (the realloc'd / OS-extended region reads 0 --
// net48). A position past the new end clamps back to 'value'. 0 on success,
// -1 for a null/closed stream, a negative value, or a backend error (the
// state is left untouched on failure -- verify-then-mutate).
int32_t il2cpp_stream_set_length(il2cpp_stream_t stream, int64_t value)
{
    StreamState* s = stream;
    if (s == nullptr || s->closed || value < 0 || !s->canSeek)
    {
        return -1;
    }
    if (s->appendMode && value < s->appendStart)
    {
        return -1; // IO_SeekAppendOverwrite
    }
#if defined(_WIN32)
    if (s->kind == StreamState::kCom)
    {
        // Stage-12 kCom delegation: IStream::SetSize (the dedicated int64_t
        // ULARGE_INTEGER form). The native stream truncates/extends; a seek
        // pointer past the new end clamps back (the native SetSize contract).
        return (il2cpp_com_stream_set_size(reinterpret_cast<intptr_t>(s->iStream), value) == kComHrOk)
            ? 0 : -1;
    }
#endif
    if (s->kind == StreamState::kMemory)
    {
        if (value > s->capacity && !MemoryEnsure(s, value))
        {
            return -1; // allocation failure: nothing changed
        }
        if (value > s->length)
        {
            // net48 zero-fills the extension gap; the realloc'd region is
            // uninitialized, so it must be cleared explicitly (D-STP-2).
            std::memset(s->buf + s->length, 0,
                static_cast<size_t>(value - s->length));
        }
        s->length = value;
        if (s->pos > value)
        {
            s->pos = value; // a position past the new end clamps back
        }
        return 0;
    }
    // File backend: 64-bit truncate/extend on the CRT surface. The fd-level
    // resize must see every buffered stdio write, so flush BEFORE the resize.
    //
    // KNOWN GAP (pre-existing, deliberately not changed here): the pushback
    // ring (pendingCount/pendingChar) is NOT discarded by a resize, so
    // get_position() (= ftell - pendingCount) can report a negative value when
    // the stream is truncated below the number of pushed-back bytes. The
    // reader-buffer survival itself matches net48 (a StreamReader keeps its
    // buffered chars across a FileStream.SetLength), so the fix is to clamp or
    // model the ring explicitly -- tracked as a separate follow-up.
    const int64_t saved = static_cast<int64_t>(STREAM_FTELL(s->fp));
    if (saved < 0 || std::fflush(s->fp) != 0)
    {
        return -1;
    }
#if defined(_WIN32)
    const int rc = ::_chsize_s(_fileno(s->fp), value);
#else
    const int rc = ::ftruncate(::fileno(s->fp), value);
#endif
    if (rc != 0)
    {
        return -1;
    }
    if (saved > value)
    {
        // net48: a position beyond the truncated end moves back to the new end.
        (void)STREAM_FSEEK(s->fp, value, SEEK_SET);
    }
    else
    {
        (void)STREAM_FSEEK(s->fp, saved, SEEK_SET); // restore; best effort
    }
    s->fileReadAfterWrite = false; // the resize seek is a legal mode reset
    s->fileAtEof = false;
    return 0;
}

//------------------------------------------------------------------------------
// MemoryStream.
//------------------------------------------------------------------------------

il2cpp_stream_t il2cpp_stream_memory_create(void)
{
    StreamState* s = new StreamState;
    s->kind = StreamState::kMemory;
    s->fp = nullptr;
    s->buf = nullptr;
    s->pos = 0;
    s->length = 0;
    s->capacity = 0;
    // io2 B1 (MemoryStream ctor family): the bufferless ctors own a growable,
    // GetBuffer-exposable malloc buffer.
    s->bufOwned = true;
    s->externalArray = nullptr;
    s->exposable = true;
    s->expandable = true;
    s->canRead = true;
    s->canWrite = true;
    s->canSeek = true; // memory streams always support seeking
    s->refCount = 1;
    s->closed = false;
    s->pendingCount = 0;
    s->textEncoding = 0;
    s->appendMode = false;
    s->appendStart = 0;
    s->writerEncoding = 0;
    s->writerBomPending = false;
    s->pendingChar = -1; // T4b: ReadChar astral stash starts empty
    s->fileReadAfterWrite = false;
    s->fileAtEof = false;
    return s;
}

// P2 IO-symmetry (2026-09-19): MemoryStream(byte[] buffer) -- seeded with a
// COPY of the payload, pos reset to 0. The OBJECT-layer ctor
// icall_stream_memory_ctor_data supersedes this handle helper: net48 ADOPTS the
// caller's array (no copy), so writes stay visible through it (b_seed1=9 in
// streammem2). This helper remains for a fixed-size, non-exposable COPY with
// the same Position/Length/ToArray semantics.
il2cpp_stream_t il2cpp_stream_memory_create_data(const uint8_t* data, int64_t length)
{
    StreamState* s = static_cast<StreamState*>(il2cpp_stream_memory_create());
    s->bufOwned = true;
    s->externalArray = nullptr;
    s->exposable = false;
    s->expandable = false;
    if (length > 0 && data != nullptr)
    {
        s->buf = static_cast<unsigned char*>(std::malloc(static_cast<size_t>(length)));
        if (s->buf != nullptr)
        {
            std::memcpy(s->buf, data, static_cast<size_t>(length));
            s->capacity = length;
            s->length = length;
        }
    }
    return s;
}

// P2 IO-symmetry (2026-09-19): MemoryStream(int capacity) -- empty stream with
// the requested initial buffer capacity (a negative request is ignored, net48
// would throw ArgumentOutOfRangeException). Owns a growable, exposable buffer.
il2cpp_stream_t il2cpp_stream_memory_create_capacity(int64_t capacity)
{
    StreamState* s = static_cast<StreamState*>(il2cpp_stream_memory_create());
    s->bufOwned = true;
    s->externalArray = nullptr;
    s->exposable = true;
    s->expandable = true;
    if (capacity > 0)
    {
        s->buf = static_cast<unsigned char*>(std::malloc(static_cast<size_t>(capacity)));
        s->capacity = capacity; // length remains 0 (no content yet)
    }
    return s;
}

Il2CppObject* il2cpp_stream_memory_to_array(il2cpp_stream_t stream)
{
    StreamState* s = stream;
    if (s == nullptr || s->kind != StreamState::kMemory)
    {
        return nullptr;
    }
    // net48 parity (2026-10-05): RETAINS the data after Close (the closed
    // memory state keeps its buffer -- see StreamCloseHandle). No closed
    // check: ToArray on a closed MemoryStream returns the bytes (oracle).
    Il2CppArray* arr = il2cpp_array_new(
        nullptr, 1, false, static_cast<int32_t>(s->length));
    if (s->length > 0)
    {
        std::memcpy(arr->data, s->buf, static_cast<size_t>(s->length));
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// OBJECT MODEL (see il2cpp_streamio.h): the class descriptors + object-typed
// icall surface. The receiver is an Il2CppStreamObject (header + int32
// handle); every icall resolves the backend StreamState through the HANDLE
// REGISTRY -- a static table indexed by the object's handle (NON-MOVING
// memory, the gcext weak-target-table contract: the compacting collector
// copies the object but the int32 handle travels with it) -- and delegates to
// the handle layer above. StreamReader / StreamWriter SHARE the wrapped
// stream's state (refCounted close -- StreamCloseHandle).
//------------------------------------------------------------------------------

namespace {

constexpr int32_t kMaxStreamSlots = 256; // bounded registry (WeakRef precedent)

StreamState* g_streamSlots[kMaxStreamSlots];
int32_t       g_streamFree[kMaxStreamSlots];
int32_t       g_streamFreeCount = 0;
bool          g_streamTableArmed = false;

void StreamTableFailLoud(const char* what)
{
    std::fprintf(stderr, "il2cpp_stream: %s\n", what);
    std::abort();
}

// First use pushes every index onto the free stack (descending, so the first
// allocation pops index 0 -- the value of a zero-initialized handle).
void StreamTableEnsureReady()
{
    if (g_streamTableArmed)
    {
        return;
    }
    g_streamTableArmed = true;
    for (int32_t i = kMaxStreamSlots - 1; i >= 0; --i)
    {
        g_streamFree[g_streamFreeCount++] = i;
    }
}

int32_t StreamHandleAlloc(StreamState* state)
{
    StreamTableEnsureReady();
    if (g_streamFreeCount == 0)
    {
        StreamTableFailLoud("stream handle table overflow");
    }
    const int32_t idx = g_streamFree[--g_streamFreeCount];
    g_streamSlots[idx] = state;
    return idx;
}

void StreamHandleRelease(int32_t idx)
{
    if (idx < 0 || idx >= kMaxStreamSlots)
    {
        return; // idempotent teardown: no live handle
    }
    g_streamSlots[idx] = nullptr;
    g_streamFree[g_streamFreeCount++] = idx;
}

Il2CppStreamObject* AsStream(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppStreamObject*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors versionuri.cpp / stringrw.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// The backend state behind 'self', or nullptr when the object owns no live
// handle (never ctor'd, or released by Close -- handle -1 / out of range).
StreamState* StreamStateOf(Il2CppObject* self)
{
    const Il2CppStreamObject* s = AsStream(self);
    if (s->handle < 0 || s->handle >= kMaxStreamSlots)
    {
        return nullptr;
    }
    return g_streamSlots[s->handle];
}

// io2 B1 (MemoryStream ctor family): adopt the caller's byte[] as the stream's
// buffer (no copy). The stream is fixed-size (non-expandable), NOT
// GetBuffer-exposable unless 'publiclyVisible', and writes land in the caller's
// array. 'index'/'count' select the net48 window (the whole array for the
// 1-/2-arg ctors). The array is rooted so the collector cannot reclaim the
// borrowed payload while the state lives.
void MemoryStreamAdoptExternal(Il2CppObject* self, Il2CppArray* data, int32_t index,
                               int32_t count, bool writable, bool publiclyVisible)
{
    StreamState* s = static_cast<StreamState*>(il2cpp_stream_memory_create());
    s->bufOwned = false;
    s->externalArray = reinterpret_cast<Il2CppObject*>(data);
    s->exposable = publiclyVisible;
    s->expandable = false;
    s->canRead = true;
    s->canWrite = writable;
    s->capacity = count;
    s->length = count;
    s->pos = 0;
    s->buf = (data != nullptr && count > 0 && index >= 0)
        ? reinterpret_cast<unsigned char*>(data->data) + index
        : nullptr;
    AsStream(self)->handle = StreamHandleAlloc(s);
    if (s->externalArray != nullptr)
    {
        il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s->externalArray));
    }
}

//------------------------------------------------------------------------------
// net48 closed-stream parity (batch 2026-10-05): the OBJECT-layer stream
// icalls throw ObjectDisposedException-equivalent (invalid-operation) on a
// closed receiver instead of the M3 silent return -- the net48 semantics the
// io probes' `catch (Exception)` observes. The handle layer (il2cpp_stream_*)
// keeps its documented silent conventions. Returns true when closed (the
// caller must return; the throw never unwinds normally).
//------------------------------------------------------------------------------
static bool StreamThrowIfClosed(const StreamState* s)
{
    if (s == nullptr || s->closed)
    {
        il2cpp_throw_invalid_operation_exception("Cannot access a closed Stream.");
        return true;
    }
    return false;
}

// net48 BinaryReader parity (batch 2026-10-05): a read that cannot satisfy
// its full width throws EndOfStreamException (an IOException) at the OBJECT
// layer; the handle layer (il2cpp_binary_read_*) keeps the M3 zero-value
// convention. Returns true when the throw fired.
static bool BinaryThrowEndOfStream(bool satisfied)
{
    if (!satisfied)
    {
        il2cpp_throw_io_exception("End of stream reached.");
        return true;
    }
    return false;
}

// The .NET exception carrier chains for the ctor failure paths (the
// arrayext.cpp convention: type duplicated, then the base chain).
const char kStreamFileNotFoundChain[] =
    "System.IO.FileNotFoundException,System.IO.FileNotFoundException,System.IO.IOException,System.SystemException,System.Exception";

// Validate the (offset, count) window over 'buffer' (net48 Stream buffer
// semantics -- R7, 2026-10-07, oracle probe-verified). net48 splits the
// validation by cause: a NEGATIVE offset/count is ArgumentOutOfRangeException;
// a window BEYOND buffer.Length (buffer.Length - offset < count) is
// ArgumentException. The pre-R7 runtime threw ArgumentOutOfRangeException for
// the whole class, flipping the resprobe out-of-bounds-oracle diff on net48.
// Shared by Stream.Read/Write and the BinaryWriter windowed byte/char writes.
void ThrowIfRangeInvalid(Il2CppArray* buffer, int32_t offset, int32_t count)
{
    if (offset < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("offset",
            "offset must be a non-negative value");
    }
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count",
            "count must be a non-negative value");
    }
    if (offset > buffer->length || count > buffer->length - offset)
    {
        il2cpp_throw_argument_exception(
            "offset and count must specify a range within the buffer");
    }
}

// .NET FileMode / FileAccess constants -> runtime open mode (0..4).
//   FileMode:  CreateNew=1, Create=2, Open=3, OpenOrCreate=4, Truncate=5,
//              Append=6.  FileAccess: Read=1, Write=2, ReadWrite=3.
//   runtime:   0=rb, 1=wb (create/truncate), 2=ab, 3=r+b/w+b (no truncate),
//              4=w+b (always truncate).
// The runtime modes 3 NEVER truncates existing content and 4 ALWAYS does
// (streamio.h contract); the translation enforces the net48 FileMode /
// FileAccess compatibility matrix (probe-verified):
//   * Append (6) requires FileAccess.Write only.
//   * Create / CreateNew / Truncate (1/2/5) require Write or ReadWrite.
//   * Open / OpenOrCreate (3/4) accept Read / Write / ReadWrite.
// Returns -1 for an incompatible (fileMode, fileAccess) combination (the
// caller turns it into an ArgumentException).
int32_t StreamTranslateFileMode(int32_t fileMode, int32_t fileAccess)
{
    if (fileMode == 6)
    {
        // Append -> ab (writes land at end-of-file); net48 rejects any
        // access other than Write (Append + Read/ReadWrite is invalid).
        return (fileAccess == 2) ? 2 : -1;
    }
    switch (fileAccess)
    {
        case 1: // Read
            if (fileMode == 3) return 0;  // Open: rb
            if (fileMode == 4) return 9;  // OpenOrCreate: seed an empty file + rb
            return -1;
        case 3: // ReadWrite
            if (fileMode == 1) return 8;  // CreateNew: exclusive w+b
            if (fileMode == 2) return 4;  // Create: w+b (truncating)
            if (fileMode == 3) return 6;  // Open: strict r+b (no create)
            if (fileMode == 4) return 3;  // OpenOrCreate: r+b -> w+b fallback
            if (fileMode == 5) return 7;  // Truncate: strict r+b + truncate
            return -1;
        case 2: // Write
            if (fileMode == 1) return 5;  // CreateNew: exclusive wb
            if (fileMode == 2) return 1;  // Create: wb (truncating)
            if (fileMode == 3) return 6;  // Open: strict r+b (no create)
            if (fileMode == 4) return 3;  // OpenOrCreate: r+b -> w+b fallback
            if (fileMode == 5) return 7;  // Truncate: strict r+b + truncate
            return -1;
        default: return -1; // invalid access (validated by the caller)
    }
}

// A reader/writer may only wrap the two leaf stream classes (FileStream /
// MemoryStream) -- not a foreign or closed object.
bool IsWrappableStream(const Il2CppObject* stream)
{
    return stream->klass == &System_IO_FileStream_class
        || stream->klass == &System_IO_MemoryStream_class;
}

// net48 parity (2026-10-05): a MemoryStream OBJECT retains its data after
// Close (ToArray works -- oracle 0xDEAD / 0x07000000) and its handle stays
// live so the retained state stays reachable. Every OTHER stream object
// (FileStream / StreamWriter / BinaryReader / BinaryWriter / ComStream)
// releases its handle on Close. The klass discriminates the receiver.
bool IsMemoryStreamObject(const Il2CppObject* obj)
{
    return obj != nullptr && obj->klass == &System_IO_MemoryStream_class;
}

//------------------------------------------------------------------------------
// The System.IO.BinaryReader/BinaryWriter binary protocol (binrw-b23; the
// C56 net48-pinned layout -- LE fixed-width values, IEEE-754 bit patterns,
// 1-byte bools, 7-bit-encoded UTF-8 BYTE length prefixes + UTF-8 content).
// Module-local: mirrors binaryio.cpp's encode/transcode helpers per the
// translation-unit isolation convention (the byte-stream writers live here,
// over the wrapped StreamState, instead of an in-memory buffer).
//------------------------------------------------------------------------------

// Little-endian 2-byte encoder/decoder (parity-T4a short/ushort widths).
void BinaryEncodeLE16(uint8_t* out, uint16_t value)
{
    out[0] = static_cast<uint8_t>(value & 0xFFu);
    out[1] = static_cast<uint8_t>((value >> 8) & 0xFFu);
}

uint16_t BinaryDecodeLE16(const uint8_t* b)
{
    return static_cast<uint16_t>(static_cast<uint16_t>(b[0])
        | (static_cast<uint16_t>(b[1]) << 8));
}

// Little-endian 4-byte encoder (explicit shifts: layout is LE on any host).
void BinaryEncodeLE32(uint8_t* out, uint32_t value)
{
    out[0] = static_cast<uint8_t>(value & 0xFFu);
    out[1] = static_cast<uint8_t>((value >> 8) & 0xFFu);
    out[2] = static_cast<uint8_t>((value >> 16) & 0xFFu);
    out[3] = static_cast<uint8_t>((value >> 24) & 0xFFu);
}

// Little-endian 8-byte encoder.
void BinaryEncodeLE64(uint8_t* out, uint64_t value)
{
    BinaryEncodeLE32(out, static_cast<uint32_t>(value & 0xFFFFFFFFu));
    BinaryEncodeLE32(out + 4, static_cast<uint32_t>(value >> 32));
}

// Little-endian 4-byte decoder.
uint32_t BinaryDecodeLE32(const uint8_t* b)
{
    return static_cast<uint32_t>(b[0])
        | (static_cast<uint32_t>(b[1]) << 8)
        | (static_cast<uint32_t>(b[2]) << 16)
        | (static_cast<uint32_t>(b[3]) << 24);
}

// Little-endian 8-byte decoder.
uint64_t BinaryDecodeLE64(const uint8_t* b)
{
    uint64_t v = 0;
    for (size_t i = 0; i < 8; ++i)
    {
        v |= static_cast<uint64_t>(b[i]) << (8 * i);
    }
    return v;
}

// The BinaryWriter 7-bit integer encoding: 7 bits per byte, low group first,
// the high bit marking a continuation byte (an int32 length needs <= 5 bytes).
void BinaryAppend7BitLen(std::vector<uint8_t>& out, uint32_t value)
{
    while (value >= 0x80u)
    {
        out.push_back(static_cast<uint8_t>((value & 0x7Fu) | 0x80u));
        value >>= 7;
    }
    out.push_back(static_cast<uint8_t>(value));
}

// Managed UTF-16 units -> UTF-8 bytes with net48-exact lone-surrogate
// handling (U+D800/U+DC00 without a partner encode as EF BF BD -- the U+FFFD
// replacement). Shared by the binary string writer (whole strings) and the
// char/char[] writers (single units / subranges -- pairing is resolved
// WITHIN the written range, matching net48 Encoding.GetBytes(subarray)).
void BinaryEncodeUnits(const char16_t* units, int32_t length, std::string& out)
{
    for (int32_t i = 0; i < length; ++i)
    {
        const char16_t c = units[i];
        if (c < 0x80)
        {
            out.push_back(static_cast<char>(c));
        }
        else if (c < 0x800)
        {
            out.push_back(static_cast<char>(0xC0 | (c >> 6)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
        else if (c >= 0xD800 && c <= 0xDBFF && i + 1 < length
                 && units[i + 1] >= 0xDC00 && units[i + 1] <= 0xDFFF)
        {
            const uint32_t cp = 0x10000u
                + (static_cast<uint32_t>(c - 0xD800) << 10)
                + static_cast<uint32_t>(units[i + 1] - 0xDC00);
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            ++i;
        }
        else if (c >= 0xD800 && c <= 0xDFFF)
        {
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
}

// Managed string -> UTF-8 bytes with net48-exact lone-surrogate handling
// (U+D800/U+DC00 without a partner encode as EF BF BD -- the U+FFFD
// replacement). The line codec above (CsToUtf8) is a best-effort 3-byte
// encoder, so the BINARY string writer needs this exact copy of the
// binaryio.cpp encoder (binaryio.cpp CsToUtf8, same per-TU convention).
std::string BinaryCsToUtf8(Il2CppObject* obj)
{
    std::string out;
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(obj);
    if (s == nullptr)
    {
        return out;
    }
    out.reserve(static_cast<size_t>(s->length));
    BinaryEncodeUnits(s->chars, s->length, out);
    return out;
}

// Read exactly 'count' bytes from the wrapped stream into 'out'. True on a
// full read; false on a short read (end-of-stream) or a read-capability
// violation. A PARTIAL read consumes what was available (the stream-level
// read advances the position) -- the M3 convention returns the zero value to
// the managed caller (net48 throws EndOfStreamException instead; the probe
// never exercises a truncated read).
bool BinaryReadBytes(StreamState* s, void* out, int32_t count)
{
    int32_t got = 0;
    while (got < count)
    {
        const int32_t r = il2cpp_stream_read(s, static_cast<char*>(out) + got, count - got);
        if (r <= 0)
        {
            return false;
        }
        got += r;
    }
    return true;
}

// Bytes between the stream's current position and its end (0 on a backend
// error). Bounds the string-content allocation BEFORE reading (a malformed
// 7-bit length must never trigger a multi-gigabyte allocation).
int64_t BinaryRemaining(StreamState* s)
{
    const int64_t length = il2cpp_stream_get_length(s);
    const int64_t pos = il2cpp_stream_get_position(s);
    if (length < 0 || pos < 0)
    {
        return 0;
    }
    return length - pos;
}

} // namespace

// Shared FileStream-ctor tail: validates the .NET FileMode (1..6) /
// FileAccess (1..3) constants, translates them to the runtime mode and opens
// the platform file -- the ArgumentNullException / ArgumentException /
// FileNotFoundException-chain surface both path ctors share (net48). The
// caller has already checked the receiver and registered the stream classes.
namespace
{
void FileStreamCtorCommon(Il2CppObject* self, Il2CppObject* path,
                          int32_t fileMode, int32_t fileAccess)
{
    if (path == nullptr)
    {
        il2cpp_throw_argument_null_exception("path");
    }
    if (fileMode < 1 || fileMode > 6)
    {
        il2cpp_throw_argument_exception(
            "FileMode must be CreateNew(1), Create(2), Open(3), OpenOrCreate(4), Truncate(5) or Append(6)");
    }
    if (fileAccess < 1 || fileAccess > 3)
    {
        il2cpp_throw_argument_exception(
            "FileAccess must be Read(1), Write(2) or ReadWrite(3)");
    }
    const int32_t mode = StreamTranslateFileMode(fileMode, fileAccess);
    if (mode < 0)
    {
        // net48 compatibility matrix violation (e.g. FileMode.Create with
        // FileAccess.Read, or FileMode.Append with FileAccess.ReadWrite).
        il2cpp_throw_argument_exception(
            "The FileMode/FileAccess combination is not supported.");
    }
    // The handle layer opens with the platform-correct dual path (_wfopen on
    // Windows, UTF-8 fopen elsewhere -- the WIN32/POSIX file-open contract).
    il2cpp_stream_t state = il2cpp_stream_file_open(path, mode);
    if (state == nullptr)
    {
        il2cpp_throw_new(kStreamFileNotFoundChain,
            "Could not find the file or directory.");
    }
    AsStream(self)->handle = StreamHandleAlloc(state);
}
} // namespace

void icall_stream_file_ctor(Il2CppObject* self, Il2CppObject* path,
                            int32_t fileMode, int32_t fileAccess)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    FileStreamCtorCommon(self, path, fileMode, fileAccess);
}

//-----------------------------------------------------------------------------
// icall_stream_file_ctor2 -- FileStream(string path, FileMode fileMode): the
// 2-arg net48 ctor. The access default is the net48 rule: FileMode.Append ->
// FileAccess.Write, every other mode -> FileAccess.ReadWrite (FileShare.Read
// is not modeled -- the fopen backend has no share enforcement, same as the
// 3-arg ctor). Everything else defers to FileStreamCtorCommon (validation,
// mode translation, open, exception surface).
//-----------------------------------------------------------------------------
void icall_stream_file_ctor2(Il2CppObject* self, Il2CppObject* path,
                             int32_t fileMode)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    const int32_t fileAccess = (fileMode == 6) ? 2 : 3; // Append -> Write, else ReadWrite
    FileStreamCtorCommon(self, path, fileMode, fileAccess);
}

void icall_stream_memory_ctor(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    AsStream(self)->handle = StreamHandleAlloc(il2cpp_stream_memory_create());
}

// P2 IO-symmetry (2026-09-19): MemoryStream(byte[]) / MemoryStream(int).
// io2 B1: MemoryStream(byte[]) ADOPTS the caller's array (no copy) -- the net48
// stream is fixed-size (non-expandable) and NOT GetBuffer-exposable, and writes
// are visible through the caller's array. The array is rooted so the collector
// cannot reclaim the borrowed payload.
void icall_stream_memory_ctor_data(Il2CppObject* self, Il2CppArray* data)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    MemoryStreamAdoptExternal(self, data, 0, data != nullptr ? static_cast<int32_t>(data->length) : 0,
                               true, false);
}

// io2 B1: MemoryStream(byte[], bool writable) -- the same adopted-array shape
// with the net48 writable flag (CanWrite follows it; CanRead stays true).
void icall_stream_memory_ctor_data_writable(Il2CppObject* self, Il2CppArray* data,
                                            int32_t writable)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    MemoryStreamAdoptExternal(self, data, 0, data != nullptr ? static_cast<int32_t>(data->length) : 0,
                               writable != 0, false);
}

// io2 B1: MemoryStream(byte[], int index, int count) -- the net48 WINDOW ctor:
// the stream exposes [index, index+count) of the caller's array (writes land in
// the array), fixed-size and non-exposable.
void icall_stream_memory_ctor_window(Il2CppObject* self, Il2CppArray* data,
                                     int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    MemoryStreamAdoptExternal(self, data, index, count, true, false);
}

// io2 B1: MemoryStream(byte[], int index, int count, bool writable) -- the
// window ctor with the net48 writable flag.
void icall_stream_memory_ctor_window_writable(Il2CppObject* self, Il2CppArray* data,
                                              int32_t index, int32_t count, int32_t writable)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    MemoryStreamAdoptExternal(self, data, index, count, writable != 0, false);
}

// io2 B1: MemoryStream(byte[], int index, int count, bool writable, bool
// publiclyVisible) -- the full net48 state matrix. publiclyVisible=true makes
// GetBuffer legal (it returns the WHOLE caller array, net48); false keeps the
// UnauthorizedAccessException contract.
void icall_stream_memory_ctor_window_writable_public(Il2CppObject* self, Il2CppArray* data,
                                                     int32_t index, int32_t count,
                                                     int32_t writable, int32_t publiclyVisible)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    MemoryStreamAdoptExternal(self, data, index, count, writable != 0, publiclyVisible != 0);
}

void icall_stream_memory_ctor_capacity(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    AsStream(self)->handle = StreamHandleAlloc(il2cpp_stream_memory_create_capacity(capacity));
}

//------------------------------------------------------------------------------
// il2cpp_stream_memory_from_bytes -- a MEMORY STREAM object seeded with the
// given bytes (read-only over a copy of the payload; pos starts at 0).
// Used by Assembly.GetManifestResourceStream to return a real Stream (the
// resops e2e does s.Read(buf, 0, n) on the result -- a raw byte[] receiver
// would fail StreamStateOf). The handle table keeps the bytes alive; the
// object is a normal MemoryStream (il2cpp_stream_memory_to_array round-trips
// the payload back out).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_stream_memory_from_bytes(const uint8_t* data, int32_t len)
{
    il2cpp::il2cpp_stream_register_classes();
    Il2CppObject* obj = il2cpp_object_new(&System_IO_MemoryStream_class);
    AsStream(obj)->handle = StreamHandleAlloc(il2cpp_stream_memory_create());
    if (len > 0 && data != nullptr)
    {
        StreamState* s = StreamStateOf(obj);
        il2cpp_stream_write(s, data, len);
        s->pos = 0; // the reader starts at the beginning (net48 contract)
    }
    return obj;
}

//------------------------------------------------------------------------------
// The ComStream class descriptor (stage 12 -- the COM stream object shape;
// the RcwClass precedent: a runtime-only descriptor, never transpiler-
// emitted). The object layout is the standard Il2CppStreamObject (object
// header + int32 handle into the streamio registry); the backend state is of
// kind kCom and delegates to the wrapped IStream. The descriptor is
// DELIBERATELY a different klass than com.cpp's RCW descriptor -- a ComStream
// is NOT an RCW (IsComObject false).
//------------------------------------------------------------------------------
Il2CppClass* ComStreamClass()
{
    static Il2CppClass cls = {
        "System.Runtime.InteropServices.ComStream",
        static_cast<int32_t>(sizeof(Il2CppStreamObject)),
        0u,                     // refFieldBitmap -- no managed ref fields
        0,                      // refFieldCount
        &System_Object_class,   // baseClass -- 'o is object' walks to root
        nullptr, 0,             // vtable -- no virtuals in stage 12
        nullptr, 0,             // interfaces -- none
    };
    return &cls;
}

//------------------------------------------------------------------------------
// il2cpp_stream_com_create -- wrap a native IStream* (an OWNED reference)
// behind a ComStream stream object (stage 12). The backend state is a
// handle-registry entry of kind kCom; every stream icall delegates to the
// IStream through the ABI-safe helpers (il2cpp_com.h). Windows: full
// implementation. Non-Windows: nullptr (the runtime's create icall throws
// NotSupportedException before ever calling this).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_stream_com_create(intptr_t iStream, int32_t deleteOnRelease)
{
#if defined(_WIN32)
    StreamState* s = new StreamState;
    s->kind = StreamState::kCom;
    s->fp = nullptr;
    s->buf = nullptr;
    s->iStream = reinterpret_cast<void*>(iStream);
    s->fDeleteOnRelease = deleteOnRelease != 0;
    s->pos = 0;
    s->length = 0;
    s->capacity = 0;
    s->canRead = true;   // an HGLOBAL-backed IStream supports read + write + seek
    s->canWrite = true;
    s->canSeek = true;
    s->refCount = 1;
    s->closed = false;
    s->pendingCount = 0;
    s->pendingChar = -1; // T4b: repositioning invalidates the ReadChar stash
    s->textEncoding = 0;
    s->appendMode = false;
    s->appendStart = 0;
    s->fileReadAfterWrite = false;
    s->fileAtEof = false;
    Il2CppStreamObject* obj = reinterpret_cast<Il2CppStreamObject*>(
        il2cpp_object_new(ComStreamClass()));
    obj->handle = StreamHandleAlloc(s);
    return reinterpret_cast<Il2CppObject*>(obj);
#else
    (void)iStream;
    (void)deleteOnRelease;
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// il2cpp_stream_com_get_iunknown -- the wrapped IStream* of a ComStream
// object (as the intptr carrier), or 0 when 'stream' is not a live COM stream
// (null, a different stream class, or a closed ComStream). The com.cpp
// HGLOBAL / IUnknown accessors resolve the object here and turn a 0 into the
// documented ArgumentException.
//------------------------------------------------------------------------------
intptr_t il2cpp_stream_com_get_iunknown(Il2CppObject* stream)
{
    if (stream == nullptr || stream->klass != ComStreamClass())
    {
        return 0;
    }
    StreamState* state = StreamStateOf(stream);
    if (state == nullptr || state->closed || state->kind != StreamState::kCom)
    {
        return 0;
    }
#if defined(_WIN32)
    return reinterpret_cast<intptr_t>(state->iStream);
#else
    return 0;
#endif
}

// Shared wrap prologue for the StreamReader/StreamWriter/BinaryReader/
// BinaryWriter ctors: the wrapper attaches a NEW handle to the WRAPPED
// stream's existing state (refCount++ -- closing the wrapper closes the
// shared file once, freed at the last handle). The caller arms the
// receiver's handle and (for the TEXT reader only) runs the BOM peek.
StreamState* WrapStreamShared(Il2CppObject* self, Il2CppObject* stream)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    if (stream == nullptr)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
    if (!IsWrappableStream(stream))
    {
        il2cpp_throw_argument_exception("stream must be a FileStream or MemoryStream");
    }
    StreamState* shared = StreamStateOf(stream);
    if (shared == nullptr || shared->closed)
    {
        il2cpp_throw_argument_exception("the wrapped stream is closed");
    }
    ++shared->refCount; // this wrapper is now another live handle
    return shared;
}

// net48 StreamReader BOM detection: peek up to 3 bytes at construction. A
// UTF-8 / UTF-16 BOM is consumed and selects the text encoding; otherwise the
// peeked bytes are pushed back (the ring replays them). Shared by the
// StreamReader(Stream) and StreamReader(string) ctors (P2 IO-symmetry).
void ReaderBomDetect(StreamState* shared)
{
    {
        const int b0 = StreamGetByte(shared);
        if (b0 >= 0)
        {
            const int b1 = StreamGetByte(shared);
            const int b2 = StreamGetByte(shared);
            if (b0 == 0xEF && b1 == 0xBB && b2 == 0xBF)
            {
                // UTF-8 BOM consumed; the encoding stays UTF-8.
            }
            else if (b0 == 0xFF && b1 == 0xFE)
            {
                shared->textEncoding = 1; // UTF-16LE; b2 (when present) is DATA
                if (b2 >= 0)
                {
                    const int32_t put = b2;
                    StreamUnread(shared, &put, 1);
                }
            }
            else if (b0 == 0xFE && b1 == 0xFF)
            {
                shared->textEncoding = 2; // UTF-16BE
                if (b2 >= 0)
                {
                    const int32_t put = b2;
                    StreamUnread(shared, &put, 1);
                }
            }
            else
            {
                const int32_t put[3] = { b0, b1, b2 };
                const int32_t putCount = (b2 >= 0) ? 3 : (b1 >= 0 ? 2 : 1);
                StreamUnread(shared, put, putCount);
            }
        }
    }
}

void icall_stream_reader_ctor(Il2CppObject* self, Il2CppObject* stream)
{
    StreamState* shared = WrapStreamShared(self, stream);
    ReaderBomDetect(shared);
    AsStream(self)->handle = StreamHandleAlloc(shared);
}

// P2 IO-symmetry (2026-09-19): StreamReader(string path) -- net48 opens with
// FileMode.Open + FileAccess.Read (runtime mode 0 "rb") and performs the same
// BOM detection as the (Stream) ctor. Missing file -> FileNotFoundException.
void icall_stream_reader_path_ctor(Il2CppObject* self, Il2CppObject* path)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    if (path == nullptr)
    {
        il2cpp_throw_argument_null_exception("path");
    }
    il2cpp_stream_t state = il2cpp_stream_file_open(path, 0); // rb: open existing for read
    if (state == nullptr)
    {
        il2cpp_throw_new(kStreamFileNotFoundChain,
            "Could not find the file or directory.");
    }
    ReaderBomDetect(static_cast<StreamState*>(state));
    AsStream(self)->handle = StreamHandleAlloc(state);
}

void icall_stream_writer_ctor(Il2CppObject* self, Il2CppObject* stream)
{
    icall_stream_reader_ctor(self, stream); // identical wrap semantics
}

//------------------------------------------------------------------------------
// icall_stream_writer_path_ctor -- StreamWriter(string path) object ctor.
//
//   net48 semantics: `new StreamWriter(path)` opens the file with
//   FileMode.Create + FileAccess.Write (the runtime "wb" mode 1 -- create/
//   truncate) and wraps it as a writer. The handle is stored on the writer
//   object (Il2CppStreamObject layout) exactly like icall_stream_file_ctor.
//   Null path -> ArgumentNullException; an unopenable target (missing
//   directory, ...) -> FileNotFoundException. Null receiver ->
//   NullReferenceException. This is the streamops e2e's `using (StreamWriter
//   usw = new StreamWriter(file))` ctor -- the string-path overload the
//   MethodMap.StreamIO LOUD gap documented.
//------------------------------------------------------------------------------
void icall_stream_writer_path_ctor(Il2CppObject* self, Il2CppObject* path)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    if (path == nullptr)
    {
        il2cpp_throw_argument_null_exception("path");
    }
    il2cpp_stream_t state = il2cpp_stream_file_open(path, 1); // wb: create/truncate
    if (state == nullptr)
    {
        il2cpp_throw_new(kStreamFileNotFoundChain,
            "Could not find the file or directory.");
    }
    AsStream(self)->handle = StreamHandleAlloc(state);
}

// P2(5) IO-symmetry (2026-09-19): StreamWriter(string path, bool append).
//   append=false -> FileMode.Create   (runtime "wb" mode 1: create/truncate)
//   append=true  -> FileMode.Append   (runtime "ab" mode 2: open-or-create,
//                                     every write lands at EOF -- net48
//                                     FileMode.Append implies FileAccess.Write)
// The text encoder is UTF-8 with no preamble (net48 StreamWriter default).
void icall_stream_writer_path_append_ctor(Il2CppObject* self, Il2CppObject* path,
                                          int32_t append)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    if (path == nullptr)
    {
        il2cpp_throw_argument_null_exception("path");
    }
    il2cpp_stream_t state = il2cpp_stream_file_open(path, append ? 2 : 1);
    if (state == nullptr)
    {
        il2cpp_throw_new(kStreamFileNotFoundChain,
            "Could not find the file or directory.");
    }
    StreamState* s = static_cast<StreamState*>(state);
    s->writerEncoding = 0;     // UTF-8
    s->writerBomPending = false;
    AsStream(self)->handle = StreamHandleAlloc(state);
}

// P2(5) A1 (2026-09-19): StreamWriter(string path, bool append, Encoding) and
// its bufferSize overload. The encoding object carries the code page + the
// preamble flag; net48 writes the preamble before the FIRST text write when
// the writer starts at position 0 (append=false truncates, so position 0;
// append=true opens at EOF -- no preamble). ASCII/UTF-8 (no BOM) emit none.
void icall_stream_writer_path_encoding_ctor(Il2CppObject* self, Il2CppObject* path,
                                            int32_t append, Il2CppObject* encoding)
{
    ThrowIfNullReceiver(self);
    il2cpp::il2cpp_stream_register_classes();
    if (path == nullptr)
    {
        il2cpp_throw_argument_null_exception("path");
    }
    if (encoding == nullptr)
    {
        il2cpp_throw_argument_null_exception("encoding");
    }
    il2cpp_stream_t state = il2cpp_stream_file_open(path, append ? 2 : 1);
    if (state == nullptr)
    {
        il2cpp_throw_new(kStreamFileNotFoundChain,
            "Could not find the file or directory.");
    }
    StreamState* s = static_cast<StreamState*>(state);
    // encoding page 65001/1200/1201/20127 -> writerEncoding 0/1/1/2.
    const int32_t page = reinterpret_cast<Il2CppEncodingObject*>(encoding)->page;
    const int32_t emitBom = reinterpret_cast<Il2CppEncodingObject*>(encoding)->emitBom;
    s->writerEncoding = (page == 65001) ? 0 : (page == 20127) ? 2 : 1;
    // net48: the preamble is written only when the stream starts empty
    // (append=false truncates -> yes; append=true -> only when the file was
    // empty; a UTF-16/UTF-8 BOM requires emitBom and a non-UTF8/ASCII page or
    // an explicit BOM flag).
    const bool startsEmpty = !append || il2cpp_stream_get_length(s) == 0;
    s->writerBomPending = startsEmpty && emitBom != 0;
    AsStream(self)->handle = StreamHandleAlloc(state);
}

void icall_stream_writer_path_encoding_ctor4(Il2CppObject* self, Il2CppObject* path,
                                             int32_t append, Il2CppObject* encoding,
                                             int32_t bufferSize)
{
    // net48: bufferSize <= 0 -> ArgumentOutOfRangeException; the value never
    // changes the written bytes (internal buffer size only).
    if (bufferSize <= 0)
    {
        il2cpp_throw_new("System.ArgumentOutOfRangeException,System.Exception",
                         "Positive number required.");
    }
    icall_stream_writer_path_encoding_ctor(self, path, append, encoding);
}

int32_t icall_stream_read_bytes(Il2CppObject* self, Il2CppArray* buffer,
                                int32_t offset, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (buffer == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
    }
    ThrowIfRangeInvalid(buffer, offset, count);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return 0; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!state->canRead)
    {
        il2cpp_throw_not_supported_exception("Stream does not support reading.");
    }
    return il2cpp_stream_read(state, buffer->data + offset, count);
}

void icall_stream_write_bytes(Il2CppObject* self, Il2CppArray* buffer,
                              int32_t offset, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (buffer == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
    }
    ThrowIfRangeInvalid(buffer, offset, count);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!state->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
    }
    // io2 B1: a fixed-size (array-seeded / window) MemoryStream cannot grow --
    // a write past Capacity is a NotSupportedException (net48).
    if (il2cpp_stream_write(state, buffer->data + offset, count) < 0)
    {
        il2cpp_throw_not_supported_exception("MemoryStream expansion is not supported.");
    }
}

int32_t icall_stream_flush(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // net48 closed-parity (2026-10-05): MemoryStream.Flush and BinaryWriter.
    // Flush are NO-OPS on a closed stream (oracle ok); StreamWriter.Flush and
    // FileStream.Flush THROW ObjectDisposedException (oracle EX). The receiver
    // klass discriminates when the handle is already released (state null).
    const bool noOpWhenClosed = IsMemoryStreamObject(self)
        || self->klass == &System_IO_BinaryWriter_class;
    StreamState* state = StreamStateOf(self);
    if (state == nullptr)
    {
        if (noOpWhenClosed)
        {
            return 0;
        }
        il2cpp_throw_invalid_operation_exception("Cannot access a closed Stream.");
        return 1; // unreachable (the throw never returns)
    }
    if (state->closed)
    {
        if (state->kind == StreamState::kMemory || noOpWhenClosed)
        {
            return 0;
        }
        il2cpp_throw_invalid_operation_exception("Cannot access a closed Stream.");
        return 1; // unreachable (the throw never returns)
    }
    return il2cpp_stream_flush(state);
}

int32_t icall_stream_close(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStreamObject* s = AsStream(self);
    StreamState* state = StreamStateOf(self);
    if (state == nullptr)
    {
        return 0; // idempotent: no live handle (already fully released)
    }
    StreamCloseHandle(state); // closes the file once; frees at refCount 0
    if (IsMemoryStreamObject(self))
    {
        // net48 parity (2026-10-05): the closed MemoryStream retains its data
        // (ToArray works -- oracle 0xDEAD / 0x07000000). Re-root the retained
        // state under the object's LIVE handle so the data stays reachable;
        // StreamHandleRelease is NOT called (the slot stays occupied for the
        // object's lifetime, matching net48's buffer-until-collected lifetime).
        if (state->refCount < 1)
        {
            state->refCount = 1;
        }
        return 0;
    }
    StreamHandleRelease(s->handle);
    s->handle = -1;
    return 0;
}

int64_t icall_stream_get_position(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_get_position(state);
}

int32_t icall_stream_set_position(Il2CppObject* self, int64_t position)
{
    ThrowIfNullReceiver(self);
    if (position < 0)
    {
        // net48: a negative Position throws ArgumentOutOfRangeException
        // (the M3 closed-receiver -1 convention does NOT apply -- this is an
        // argument validation, exactly like the buffer-range checks).
        il2cpp_throw_argument_out_of_range_exception("value",
            "Position must be a non-negative value.");
    }
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_set_position(state, position);
}

int64_t icall_stream_get_length(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_get_length(state);
}

int32_t icall_stream_get_can_read(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    return (state != nullptr && !state->closed && state->canRead) ? 1 : 0;
}

int32_t icall_stream_get_can_write(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    return (state != nullptr && !state->closed && state->canWrite) ? 1 : 0;
}

//------------------------------------------------------------------------------
// net48 seek surface (D-STP-2): Seek / CanSeek / SetLength were LOUD-unwired
// (the runtime had only the absolute Position accessor). Now wired:
//   * get_CanSeek: 1 for memory streams and every file-stream mode (net48
//     probe: an append-mode FileStream still reports CanSeek=true -- only
//     write placement differs), 0 for closed receivers.
//   * Seek(long, SeekOrigin): computes base+offset (Begin/Current/End),
//     validates the result BEFORE moving (negative -> IOException), and
//     returns the new position. An invalid origin -> ArgumentException; a
//     non-seekable stream -> NotSupportedException (defensive; no backend
//     currently reports canSeek=false). -1 for a closed receiver (M3).
//   * SetLength(long): truncates/extends through il2cpp_stream_set_length
//     (extension zero-filled; a position past the new end clamps back).
//     Negative -> ArgumentOutOfRangeException (net48 argument validation).
//------------------------------------------------------------------------------
int32_t icall_stream_get_can_seek(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    return (state != nullptr && !state->closed && state->canSeek) ? 1 : 0;
}

int64_t icall_stream_seek(Il2CppObject* self, int64_t offset, int32_t origin)
{
    ThrowIfNullReceiver(self);
    if (origin < 0 || origin > 2)
    {
        il2cpp_throw_argument_exception(
            "The value of the origin argument is invalid.");
        return -1; // unreachable (the throw never returns)
    }
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!state->canSeek)
    {
        il2cpp_throw_not_supported_exception("Stream does not support seeking.");
        return -1; // unreachable
    }
    const int64_t newPos = il2cpp_stream_seek(state, offset, origin);
    if (newPos < 0)
    {
        // A seek before the beginning of the stream (net48 IOException). The
        // handle layer folds null/closed/invalid-origin into the same -1; the
        // closed/invalid-origin cases already returned above, so this is the
        // negative-position path (or a backend error).
        il2cpp_throw_io_exception(
            "An attempt was made to move the position before the beginning of the stream.");
        return -1; // unreachable
    }
    return newPos;
}

void icall_stream_set_length(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    if (value < 0)
    {
        // net48: a negative length throws ArgumentOutOfRangeException (the
        // M3 closed-receiver convention does NOT apply -- argument validation).
        il2cpp_throw_argument_out_of_range_exception("value",
            "The value must be a non-negative number.");
        return;
    }
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!state->canSeek)
    {
        il2cpp_throw_not_supported_exception("Stream does not support seeking.");
        return;
    }
    if (state->appendMode && value < state->appendStart)
    {
        il2cpp_throw_io_exception(
            "Unable to seek backward to overwrite data that previously existed "
            "in a file opened in Append mode.");
        return; // unreachable
    }
    (void)il2cpp_stream_set_length(state, value);
}

//------------------------------------------------------------------------------
// Single-byte surface (D-STP-3): Stream.ReadByte / Stream.WriteByte were
// LOUD-unwired (only the byte[] array form existed). net48 semantics:
//   * ReadByte(): the next byte as an int (0..255), or -1 at end-of-stream
//     (clean EOF, NOT an exception -- the byte[] Read returns 0 at EOF, the
//     byte form returns -1). A read on a write-only stream ->
//     NotSupportedException; -1 for a closed receiver.
//   * WriteByte(byte): writes the single byte at the current position
//     (extending the stream when past the end, like the byte[] form). A write
//     on a read-only stream -> NotSupportedException; a closed receiver is a
//     silent no-op.
//------------------------------------------------------------------------------
int32_t icall_stream_read_byte(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!state->canRead)
    {
        il2cpp_throw_not_supported_exception("Stream does not support reading.");
        return -1; // unreachable
    }
    // Shared backend read: memory (pos/length) and file (mode-safe getc).
    return StreamGetByte(state);
}

//------------------------------------------------------------------------------
// StreamReader.Peek / TextReader.Read() (net48 parity, batch 2026-10-05).
// Read the NEXT text unit (UTF-16 code unit of the reader's selected encoding)
// WITHOUT consuming it (peek) or consuming it (read). Returns -1 at
// end-of-stream (net48 Peek/Read both return -1). UTF-8 scalars decode per the
// Utf8ToCs rules; astral scalars split across two calls via the pendingChar
// stash (the ReadChar precedent); UTF-16 encodings are exact 2-byte units
// (a lone trailing byte decodes to its own unit). The pending pushback ring
// (BOM/CRLF lookahead) replays through StreamGetByte, so a peek reads the
// exact bytes the next Read would see and replays them with StreamUnread.
//------------------------------------------------------------------------------
static int32_t StreamReaderPeekOrRead(StreamState* s, bool peek)
{
    if (StreamThrowIfClosed(s))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!s->canRead)
    {
        return -1;
    }
    if (s->pendingChar >= 0)
    {
        // A stashed low surrogate from a split astral read comes first. Peek
        // returns it WITHOUT consuming (repeated peeks repeat it); Read
        // consumes it (net48 StreamReader.Peek/Read on a surrogate).
        const int32_t unit = s->pendingChar;
        if (!peek)
        {
            s->pendingChar = -1;
        }
        return unit;
    }
    if (s->textEncoding != 0)
    {
        // UTF-16LE (1) / UTF-16BE (2): one unit = one 2-byte pair.
        const int lo = StreamGetByte(s);
        if (lo < 0)
        {
            return -1;
        }
        const int hi = StreamGetByte(s);
        const int32_t unit = (hi < 0)
            ? static_cast<int32_t>(static_cast<char16_t>(lo))
            : (s->textEncoding == 1
                ? static_cast<int32_t>(static_cast<char16_t>(lo | (hi << 8)))
                : static_cast<int32_t>(static_cast<char16_t>((lo << 8) | hi)));
        if (peek)
        {
            const int32_t put[2] = { lo, hi };
            StreamUnread(s, put, (hi < 0) ? 1 : 2);
        }
        // UTF-16 astral pairs are two separate units already -- no stash.
        return unit;
    }
    // UTF-8: decode ONE scalar; a peek replays every byte.
    const int c = StreamGetByte(s);
    if (c < 0)
    {
        return -1;
    }
    if (c < 0x80)
    {
        if (peek)
        {
            const int32_t put = c;
            StreamUnread(s, &put, 1);
        }
        return c;
    }
    int32_t need = 0;
    uint32_t cp = 0;
    if ((c & 0xE0) == 0xC0)
    {
        need = 1;
        cp = static_cast<uint32_t>(c & 0x1Fu) << 6;
    }
    else if ((c & 0xF0) == 0xE0)
    {
        need = 2;
        cp = static_cast<uint32_t>(c & 0x0Fu) << 12;
    }
    else if ((c & 0xF8) == 0xF0)
    {
        need = 3;
        cp = static_cast<uint32_t>(c & 0x07u) << 18;
    }
    else
    {
        if (peek)
        {
            const int32_t put = c;
            StreamUnread(s, &put, 1);
        }
        return 0xFFFD; // lone continuation / impossible lead -> replacement
    }
    int32_t cont[3] = { -1, -1, -1 };
    int32_t nread = 0;
    bool ok = true;
    for (int32_t i = 0; i < need; ++i)
    {
        cont[i] = StreamGetByte(s);
        if (cont[i] < 0)
        {
            ok = false;
            break;
        }
        ++nread;
        if ((cont[i] & 0xC0) != 0x80)
        {
            ok = false; // malformed continuation
            break;
        }
    }
    if (!ok)
    {
        // Utf8ToCs ++i rule: a malformed sequence consumes only the lead
        // byte -- replay the lead + any read continuations.
        const int32_t back[4] = { c, cont[0], cont[1], cont[2] };
        StreamUnread(s, back, nread + 1);
        return 0xFFFD;
    }
    for (int32_t i = 0; i < need; ++i)
    {
        cp |= static_cast<uint32_t>(cont[i] & 0x3Fu) << (6 * (need - 1 - i));
    }
    if (peek)
    {
        const int32_t back[4] = { c, cont[0], cont[1], cont[2] };
        StreamUnread(s, back, need + 1);
    }
    // Range validation mirrors Utf8ToCs: overlong / surrogate / > U+10FFFF.
    if (need == 1)
    {
        return (cp >= 0x80) ? static_cast<int32_t>(cp) : 0xFFFD;
    }
    if (need == 2)
    {
        return (cp >= 0x800 && (cp < 0xD800 || cp > 0xDFFF))
            ? static_cast<int32_t>(cp) : 0xFFFD;
    }
    if (cp < 0x10000 || cp > 0x10FFFF)
    {
        return 0xFFFD;
    }
    // Astral scalar: split the surrogate pair (net48 Decoder statefulness).
    // A PEEK does not advance, so no stash is created (repeated peeks repeat
    // the high surrogate); a READ consumes the scalar and stashes the low.
    const uint32_t v = cp - 0x10000;
    if (!peek)
    {
        s->pendingChar = static_cast<int32_t>(0xDC00 + (v & 0x3FFu));
    }
    return static_cast<int32_t>(0xD800 + (v >> 10));
}

// TextReader.Read(): consume the next UTF-16 unit of the reader's encoding
// (-1 at end-of-stream, net48).
int32_t icall_stream_read_char(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return StreamReaderPeekOrRead(StreamStateOf(self), false);
}

// StreamReader.Peek(): the next UTF-16 unit WITHOUT consuming (-1 at EOF).
int32_t icall_stream_peek(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return StreamReaderPeekOrRead(StreamStateOf(self), true);
}

// MemoryStream.Capacity: the allocation stride of the memory backend buffer
// (int32, net48). A closed receiver throws ObjectDisposedException (net48
// oracle ms_capacity_after_close=EX); a non-memory receiver reports 0.
int32_t icall_stream_memory_get_capacity(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return 0; // net48: ObjectDisposedException (2026-10-05 parity)
    }
    if (state->kind != StreamState::kMemory)
    {
        return 0;
    }
    return static_cast<int32_t>(state->capacity);
}

// MemoryStream.Capacity (setter): resizes the internal buffer to 'value' bytes.
// net48: a value below the current Length is ArgumentOutOfRangeException; a
// non-expandable (array-seeded / window) stream cannot change capacity at all
// (NotSupportedException); a negative value is ArgumentOutOfRangeException.
void icall_stream_memory_set_capacity(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    if (value < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("value",
            "Capacity must be a non-negative value.");
        return;
    }
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return; // net48: ObjectDisposedException (2026-10-05 parity)
    }
    if (state->kind != StreamState::kMemory)
    {
        return;
    }
    if (value < state->length)
    {
        il2cpp_throw_argument_out_of_range_exception("value",
            "Capacity cannot be smaller than the stream length.");
        return;
    }
    if (!state->expandable)
    {
        // The array-seeded / window ctors share a fixed buffer: net48 throws
        // NotSupportedException for ANY capacity change (the case pins
        // e_capthrow=NotSupportedException even for a value that fits).
        il2cpp_throw_not_supported_exception(
            "MemoryStream length cannot be changed when the stream is non-expandable.");
        return;
    }
    if (value == state->capacity)
    {
        return;
    }
    unsigned char* grown = static_cast<unsigned char*>(std::realloc(state->buf, static_cast<size_t>(value)));
    if (value > 0 && grown == nullptr)
    {
        return; // allocation failure: leave the existing buffer untouched
    }
    state->buf = grown;
    state->capacity = value;
}

// MemoryStream.GetBuffer(): the underlying buffer as a managed byte[]. Only
// legal when the stream was created with publiclyVisible=true (or the
// bufferless ctors); otherwise net48 throws UnauthorizedAccessException. A
// closed receiver throws ObjectDisposedException. For an adopted (array-
// seeded / window) stream the WHOLE caller array is returned (net48); for an
// owned buffer a fresh array of 'capacity' bytes is returned.
Il2CppArray* icall_stream_memory_get_buffer(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return nullptr; // net48: ObjectDisposedException (2026-10-05 parity)
    }
    if (state->kind != StreamState::kMemory)
    {
        return nullptr;
    }
    if (!state->exposable)
    {
        il2cpp_throw_new(
            "System.UnauthorizedAccessException,System.UnauthorizedAccessException,"
            "System.SystemException,System.Exception",
            "MemoryStream.GetBuffer is only supported when the stream was created "
            "with publiclyVisible=true.");
        return nullptr; // unreachable (the throw never returns)
    }
    if (state->externalArray != nullptr)
    {
        return reinterpret_cast<Il2CppArray*>(state->externalArray);
    }
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(state->capacity));
    if (state->capacity > 0 && state->buf != nullptr)
    {
        std::memcpy(arr->data, state->buf, static_cast<size_t>(state->capacity));
    }
    return arr;
}

// Stream.CopyTo(Stream destination[, int bufferSize]): copies from the current
// position to the end of THIS stream into 'destination'. net48 validation:
//   * bufferSize <= 0 -> ArgumentOutOfRangeException
//   * null destination -> ArgumentNullException
//   * a non-writable destination -> NotSupportedException
//   * a read-only source -> NotSupportedException (net48: reading is the copy)
// A closed receiver throws ObjectDisposedException.
void icall_stream_copy_to(Il2CppObject* self, Il2CppObject* destination, int32_t bufferSize)
{
    ThrowIfNullReceiver(self);
    if (bufferSize <= 0)
    {
        il2cpp_throw_argument_out_of_range_exception("bufferSize",
            "The buffer size must be greater than zero.");
        return;
    }
    if (destination == nullptr)
    {
        il2cpp_throw_argument_null_exception("destination");
        return;
    }
    StreamState* src = StreamStateOf(self);
    if (StreamThrowIfClosed(src))
    {
        return; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!src->canRead)
    {
        il2cpp_throw_not_supported_exception("Stream does not support reading.");
        return;
    }
    StreamState* dst = StreamStateOf(destination);
    if (StreamThrowIfClosed(dst))
    {
        return; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!dst->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    const int64_t remaining = src->length - src->pos;
    if (remaining <= 0)
    {
        return; // nothing left to copy (src position already at/after EOF)
    }
    // Copy in 'bufferSize' chunks through the backend read/write (works for
    // file and memory sources). A failed write (a non-expandable destination
    // past capacity) raises NotSupportedException.
    int64_t left = remaining;
    while (left > 0)
    {
        const int32_t chunk = static_cast<int32_t>(left < bufferSize ? left : bufferSize);
        unsigned char* tmp = static_cast<unsigned char*>(std::malloc(static_cast<size_t>(chunk)));
        if (tmp == nullptr)
        {
            return;
        }
        const int32_t got = il2cpp_stream_read(src, tmp, chunk);
        if (got <= 0)
        {
            std::free(tmp);
            break;
        }
        const int32_t written = il2cpp_stream_write(dst, tmp, got);
        std::free(tmp);
        if (written < 0)
        {
            il2cpp_throw_not_supported_exception("Stream does not support writing.");
            return;
        }
        left -= got;
    }
}

void icall_stream_write_byte(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    if (!state->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    const uint8_t b = static_cast<uint8_t>(value & 0xFF);
    // io2 B1: a fixed-size (array-seeded / window) MemoryStream cannot grow --
    // a write past Capacity is a NotSupportedException (net48).
    if (il2cpp_stream_write(state, &b, 1) < 0)
    {
        il2cpp_throw_not_supported_exception("MemoryStream expansion is not supported.");
    }
}

Il2CppObject* icall_stream_read_line(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return nullptr; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_read_line(state);
}

int32_t icall_stream_write_line(Il2CppObject* self, Il2CppObject* line)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_write_line(state, line);
}

// TextWriter/StreamWriter.Write(string): UTF-8 text WITHOUT the line
// terminator (net48 Write). 0 on success, -1 on a closed receiver (the M3
// silent-failure convention).
int32_t icall_stream_write_text(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return -1; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_write_text(state, text);
}

// StreamReader.ReadToEnd(): the remainder from the current position to EOF as
// one UTF-8 managed string ("" at end-of-stream, NOT null -- net48).
Il2CppObject* icall_stream_read_to_end(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (StreamThrowIfClosed(state))
    {
        return nullptr; // net48: ObjectDisposedException (batch 2026-10-05 parity)
    }
    return il2cpp_stream_read_to_end(state);
}

Il2CppArray* icall_stream_memory_to_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* state = StreamStateOf(self);
    if (state == nullptr)
    {
        return nullptr; // never-ctor'd / non-memory receiver: no payload
    }
    // net48 parity (2026-10-05): ToArray WORKS on a closed MemoryStream (the
    // data is retained -- oracle 0xDEAD / 0x07000000). The closed check is
    // intentionally absent; the state's buffer survives Close.
    return reinterpret_cast<Il2CppArray*>(il2cpp_stream_memory_to_array(state));
}

// net48 timeout surface (System.IO.Stream): FileStream and MemoryStream never
// support timeouts. CanTimeout is false; the ReadTimeout / WriteTimeout
// accessors (get AND set) throw InvalidOperationException -- probe-verified
// net48 behavior ("Timeouts are not supported on this stream.").
int32_t icall_stream_get_can_timeout(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return 0; // FileStream / MemoryStream: timeouts are not supported
}

int32_t icall_stream_get_read_timeout(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_invalid_operation_exception("Timeouts are not supported on this stream.");
    return -1; // unreachable (the throw never returns)
}

int32_t icall_stream_set_read_timeout(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    (void)value; // the setter always throws (timeouts unsupported)
    il2cpp_throw_invalid_operation_exception("Timeouts are not supported on this stream.");
    return -1; // unreachable
}

int32_t icall_stream_get_write_timeout(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_invalid_operation_exception("Timeouts are not supported on this stream.");
    return -1; // unreachable
}

int32_t icall_stream_set_write_timeout(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    (void)value; // the setter always throws (timeouts unsupported)
    il2cpp_throw_invalid_operation_exception("Timeouts are not supported on this stream.");
    return -1; // unreachable
}

//------------------------------------------------------------------------------
// System.IO.BinaryReader / BinaryWriter OBJECT surface (binrw-b23).
//
// The BinaryReader/BinaryWriter wrap the SAME StreamState as the wrapped
// FileStream/MemoryStream (net48 ownership: the wrapper takes over the wrapped
// stream -- closing the wrapper closes the shared stream once, refCounted).
// Reads/writes operate at the wrapped stream's current position through the
// C56 binary protocol (the byte-level stream surface above).
//------------------------------------------------------------------------------

void icall_binary_reader_ctor(Il2CppObject* self, Il2CppObject* stream)
{
    // net48 BinaryReader has NO BOM handling (BOM detection belongs to the
    // TEXT StreamReader path). Wrap WITHOUT the peek: the pushback ring is
    // only drained by the text byte reader (StreamGetByte), so peeking here
    // would silently drop up to 3 bytes from the binary read path.
    StreamState* shared = WrapStreamShared(self, stream);
    AsStream(self)->handle = StreamHandleAlloc(shared);
}

void icall_binary_writer_ctor(Il2CppObject* self, Il2CppObject* stream)
{
    StreamState* shared = WrapStreamShared(self, stream);
    AsStream(self)->handle = StreamHandleAlloc(shared);
}

void icall_binary_write_int32(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return; // closed receiver: silent no-op (M3 conventions)
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint8_t b[4];
    BinaryEncodeLE32(b, static_cast<uint32_t>(value));
    (void)il2cpp_stream_write(s, b, 4);
}

void icall_binary_write_int64(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint8_t b[8];
    BinaryEncodeLE64(b, static_cast<uint64_t>(value));
    (void)il2cpp_stream_write(s, b, 8);
}

void icall_binary_write_uint32(Il2CppObject* self, uint32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint8_t b[4];
    BinaryEncodeLE32(b, value);
    (void)il2cpp_stream_write(s, b, 4);
}

void icall_binary_write_double(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    uint8_t b[8];
    BinaryEncodeLE64(b, bits);
    (void)il2cpp_stream_write(s, b, 8);
}

void icall_binary_write_single(Il2CppObject* self, float value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint32_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    uint8_t b[4];
    BinaryEncodeLE32(b, bits);
    (void)il2cpp_stream_write(s, b, 4);
}

void icall_binary_write_bool(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    const uint8_t byte = value != 0 ? 0x01 : 0x00;
    (void)il2cpp_stream_write(s, &byte, 1);
}

void icall_binary_write_string(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    std::vector<uint8_t> out;
    if (value != nullptr)
    {
        const std::string utf8 = BinaryCsToUtf8(value);
        BinaryAppend7BitLen(out, static_cast<uint32_t>(utf8.size()));
        out.insert(out.end(), utf8.begin(), utf8.end());
    }
    else
    {
        out.push_back(0); // null writes as the empty string (0x00 prefix)
    }
    (void)il2cpp_stream_write(s, out.data(), static_cast<int32_t>(out.size()));
}

//------------------------------------------------------------------------------
// BinaryWriter fixed narrow/ulong widths (parity-T4a, 2026-09-20): net48
// writes the value's low 1/2/8 bytes little-endian (Write(byte) emits ONE
// byte -- routing through write_int32 would corrupt the protocol with 4).
// Narrow values ride int32 carriers (M2 slot model); ulong rides uint64.
//------------------------------------------------------------------------------
void icall_binary_write_byte(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    const uint8_t byte = static_cast<uint8_t>(value & 0xFF);
    (void)il2cpp_stream_write(s, &byte, 1);
}

void icall_binary_write_sbyte(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    const uint8_t byte = static_cast<uint8_t>(value & 0xFF);
    (void)il2cpp_stream_write(s, &byte, 1);
}

void icall_binary_write_int16(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint8_t b[2];
    BinaryEncodeLE16(b, static_cast<uint16_t>(value & 0xFFFF));
    (void)il2cpp_stream_write(s, b, 2);
}

void icall_binary_write_uint16(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint8_t b[2];
    BinaryEncodeLE16(b, static_cast<uint16_t>(value & 0xFFFF));
    (void)il2cpp_stream_write(s, b, 2);
}

void icall_binary_write_uint64(Il2CppObject* self, uint64_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    uint8_t b[8];
    BinaryEncodeLE64(b, value);
    (void)il2cpp_stream_write(s, b, 8);
}

//------------------------------------------------------------------------------
// BinaryWriter decimal / array / char widths (parity-T4b, 2026-09-20).
//------------------------------------------------------------------------------

void icall_binary_write_decimal(Il2CppObject* self, const Il2CppDecimal* value)
{
    ThrowIfNullReceiver(self);
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
        return;
    }
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    // net48 BinaryWriter.Write(decimal) emits decimal.GetBits() in order:
    // lo, mid, hi, flags -- four little-endian int32s (16 bytes). The
    // Il2CppDecimal packs lo64 = mid<<32|lo, so split it back out.
    uint8_t b[16];
    BinaryEncodeLE32(b, static_cast<uint32_t>(value->lo64 & 0xFFFFFFFFu));
    BinaryEncodeLE32(b + 4, static_cast<uint32_t>(value->lo64 >> 32));
    BinaryEncodeLE32(b + 8, value->hi32);
    BinaryEncodeLE32(b + 12, static_cast<uint32_t>(value->flags));
    (void)il2cpp_stream_write(s, b, 16);
}

void icall_binary_write_bytes_full(Il2CppObject* self, Il2CppArray* buffer)
{
    ThrowIfNullReceiver(self);
    if (buffer == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
        return;
    }
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    // net48 Write(byte[]) emits the RAW bytes (no length prefix).
    if (buffer->length > 0)
    {
        (void)il2cpp_stream_write(s, reinterpret_cast<const uint8_t*>(buffer->data),
                                  buffer->length);
    }
}

void icall_binary_write_bytes(Il2CppObject* self, Il2CppArray* buffer,
                              int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (buffer == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
        return;
    }
    ThrowIfRangeInvalid(buffer, index, count);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    if (count > 0)
    {
        (void)il2cpp_stream_write(s, reinterpret_cast<const uint8_t*>(buffer->data) + index,
                                  count);
    }
}

void icall_binary_write_char(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    // net48 Write(char) encodes the SINGLE unit (no pairing across calls --
    // a lone surrogate encodes as EF BF BD, same as the string writer).
    const char16_t unit = static_cast<char16_t>(value & 0xFFFF);
    std::string utf8;
    BinaryEncodeUnits(&unit, 1, utf8);
    (void)il2cpp_stream_write(s, reinterpret_cast<const uint8_t*>(utf8.data()),
                              static_cast<int32_t>(utf8.size()));
}

void icall_binary_write_chars_full(Il2CppObject* self, Il2CppArray* chars)
{
    ThrowIfNullReceiver(self);
    if (chars == nullptr)
    {
        il2cpp_throw_argument_null_exception("chars");
        return;
    }
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    // net48 Write(char[]) encodes the whole array (pairing WITHIN it).
    std::string utf8;
    BinaryEncodeUnits(reinterpret_cast<const char16_t*>(chars->data), chars->length, utf8);
    if (!utf8.empty())
    {
        (void)il2cpp_stream_write(s, reinterpret_cast<const uint8_t*>(utf8.data()),
                                  static_cast<int32_t>(utf8.size()));
    }
}

void icall_binary_write_chars(Il2CppObject* self, Il2CppArray* chars,
                              int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (chars == nullptr)
    {
        il2cpp_throw_argument_null_exception("chars");
        return;
    }
    ThrowIfRangeInvalid(chars, index, count);
    StreamState* s = StreamStateOf(self);
    if (s == nullptr || s->closed)
    {
        return;
    }
    if (!s->canWrite)
    {
        il2cpp_throw_not_supported_exception("Stream does not support writing.");
        return;
    }
    // net48 Write(chars, index, count) encodes the SUBRANGE (pairing within
    // it -- a pair split by the window edges encodes as two replacements).
    std::string utf8;
    BinaryEncodeUnits(reinterpret_cast<const char16_t*>(chars->data) + index, count, utf8);
    if (!utf8.empty())
    {
        (void)il2cpp_stream_write(s, reinterpret_cast<const uint8_t*>(utf8.data()),
                                  static_cast<int32_t>(utf8.size()));
    }
}

int32_t icall_binary_read_int32(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b[4];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 4))) return 0;
    return static_cast<int32_t>(BinaryDecodeLE32(b));
}

int64_t icall_binary_read_int64(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b[8];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 8))) return 0;
    return static_cast<int64_t>(BinaryDecodeLE64(b));
}

uint32_t icall_binary_read_uint32(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b[4];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 4))) return 0;
    return BinaryDecodeLE32(b);
}

double icall_binary_read_double(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0.0;
    uint8_t b[8];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 8))) return 0.0;
    const uint64_t bits = BinaryDecodeLE64(b);
    double value = 0.0;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

float icall_binary_read_single(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0.0f;
    uint8_t b[4];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 4))) return 0.0f;
    const uint32_t bits = BinaryDecodeLE32(b);
    float value = 0.0f;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

int32_t icall_binary_read_bool(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b = 0;
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, &b, 1))) return 0;
    return b != 0 ? 1 : 0;
}

// BinaryReader.ReadByte: the RAW next byte (0..255 -- net48). Separate from
// icall_binary_read_bool on purpose: the bool icall NORMALIZES to 0/1 (right
// for ReadBoolean, wrong for ReadByte). At EOF the object layer throws
// (net48 EndOfStreamException -- batch 2026-10-05 parity).
int32_t icall_binary_read_byte(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b = 0;
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, &b, 1))) return 0;
    return static_cast<int32_t>(b);
}

//------------------------------------------------------------------------------
// BinaryReader fixed narrow/ulong widths (parity-T4a, 2026-09-20): decode 1/2/8
// little-endian bytes; sbyte/int16 sign-extend into the int32 carrier (M2
// slot model), ushort zero-extends, ulong rides uint64. Truncated: the object
// layer throws EndOfStreamException (batch 2026-10-05 parity).
//------------------------------------------------------------------------------
int32_t icall_binary_read_sbyte(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b = 0;
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, &b, 1))) return 0;
    return static_cast<int32_t>(static_cast<int8_t>(b));
}

int32_t icall_binary_read_int16(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b[2];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 2))) return 0;
    return static_cast<int32_t>(static_cast<int16_t>(BinaryDecodeLE16(b)));
}

int32_t icall_binary_read_uint16(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b[2];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 2))) return 0;
    return static_cast<int32_t>(BinaryDecodeLE16(b));
}

uint64_t icall_binary_read_uint64(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    uint8_t b[8];
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 8))) return 0;
    return BinaryDecodeLE64(b);
}

//------------------------------------------------------------------------------
// BinaryReader decimal / char widths (parity-T4b, 2026-09-20).
//------------------------------------------------------------------------------

void icall_binary_read_decimal(Il2CppObject* self, Il2CppDecimal* out)
{
    ThrowIfNullReceiver(self);
    if (out == nullptr)
    {
        il2cpp_throw_argument_null_exception("out");
        return;
    }
    StreamState* s = StreamStateOf(self);
    uint8_t b[16];
    if (StreamThrowIfClosed(s)) return;
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, b, 16))) return;
    // Inverse of the writer: lo, mid, hi, flags (four LE int32s).
    const uint32_t lo = BinaryDecodeLE32(b);
    const uint32_t mid = BinaryDecodeLE32(b + 4);
    out->hi32 = BinaryDecodeLE32(b + 8);
    out->flags = static_cast<int32_t>(BinaryDecodeLE32(b + 12));
    out->lo64 = (static_cast<uint64_t>(mid) << 32) | lo;
}

int32_t icall_binary_read_char(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return 0;
    // A stashed low surrogate from a split astral read goes first.
    if (s->pendingChar >= 0)
    {
        const int32_t unit = s->pendingChar;
        s->pendingChar = -1;
        return unit;
    }
    uint8_t lead = 0;
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, &lead, 1))) return 0;
    if (lead < 0x80)
    {
        return static_cast<int32_t>(lead);
    }
    // Multi-byte sequence: length from the lead byte (mirrors the Utf8ToCs
    // decoder above -- overlong / surrogate / out-of-range encodings decode
    // as U+FFFD, consuming exactly the lead byte like Utf8ToCs's ++i).
    int32_t need = 0;
    uint32_t cp = 0;
    if ((lead & 0xE0) == 0xC0)
    {
        need = 1;
        cp = static_cast<uint32_t>(lead & 0x1Fu) << 6;
    }
    else if ((lead & 0xF0) == 0xE0)
    {
        need = 2;
        cp = static_cast<uint32_t>(lead & 0x0Fu) << 12;
    }
    else if ((lead & 0xF8) == 0xF0)
    {
        need = 3;
        cp = static_cast<uint32_t>(lead & 0x07u) << 18;
    }
    else
    {
        return 0xFFFD; // lone continuation / impossible lead -> replacement
    }
    uint8_t cont[3] = { 0, 0, 0 };
    int32_t nread = 0;
    bool ok = true;
    for (int32_t i = 0; i < need; ++i)
    {
        if (!BinaryReadBytes(s, &cont[i], 1))
        {
            ok = false; // truncated mid-sequence
            break;
        }
        ++nread; // consumed (valid or not -- unmirrored below on failure)
        if (!IsContinuationByte(cont[i]))
        {
            ok = false; // malformed continuation
            break;
        }
    }
    if (!ok)
    {
        // Mirror Utf8ToCs's ++i rule: only the lead byte is consumed; every
        // trailing byte replays (truncated input replays into EOF).
        int32_t back[3];
        for (int32_t i = 0; i < nread; ++i)
        {
            back[i] = cont[i];
        }
        StreamUnread(s, back, nread);
        return 0xFFFD;
    }
    for (int32_t i = 0; i < need; ++i)
    {
        cp |= static_cast<uint32_t>(cont[i] & 0x3Fu) << (6 * (need - 1 - i));
    }
    // Range validation mirrors Utf8ToCs: overlong / surrogate / > U+10FFFF
    // encode as U+FFFD.
    if (need == 1)
    {
        return (cp >= 0x80) ? static_cast<int32_t>(cp) : 0xFFFD;
    }
    if (need == 2)
    {
        return (cp >= 0x800 && (cp < 0xD800 || cp > 0xDFFF))
            ? static_cast<int32_t>(cp) : 0xFFFD;
    }
    if (cp < 0x10000 || cp > 0x10FFFF)
    {
        return 0xFFFD;
    }
    // Astral scalar: split the surrogate pair across two ReadChar calls
    // (net48 Decoder statefulness); stash the low surrogate.
    const uint32_t v = cp - 0x10000;
    s->pendingChar = static_cast<int32_t>(0xDC00 + (v & 0x3FFu));
    return static_cast<int32_t>(0xD800 + (v >> 10));
}

Il2CppObject* icall_binary_read_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    StreamState* s = StreamStateOf(self);
    if (StreamThrowIfClosed(s)) return nullptr;
    // 7-bit-encoded byte length prefix (at most 5 bytes; a 6th continuation
    // byte is malformed -> null).
    uint32_t len = 0;
    bool terminated = false;
    for (size_t shift = 0; shift <= 28; shift += 7)
    {
        uint8_t b = 0;
        if (BinaryThrowEndOfStream(BinaryReadBytes(s, &b, 1))) return nullptr;
        len |= static_cast<uint32_t>(b & 0x7Fu) << shift;
        if ((b & 0x80u) == 0)
        {
            terminated = true;
            break;
        }
    }
    if (!terminated)
    {
        return nullptr; // length exceeds the int32 range -> malformed
    }
    if (len == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    // Preflight BEFORE allocating: a malformed length prefix must fail with
    // null -- never a multi-gigabyte allocation (the buffer-domain read-string
    // convention, restated over the stream's remaining extent).
    if (static_cast<int64_t>(len) > BinaryRemaining(s))
    {
        if (BinaryThrowEndOfStream(false)) return nullptr;
    }
    std::vector<char> bytes(static_cast<size_t>(len));
    if (BinaryThrowEndOfStream(BinaryReadBytes(s, bytes.data(), static_cast<int32_t>(len)))) return nullptr;
    return Utf8ToCs(bytes.data(), bytes.size());
}

}} // namespace il2cpp::runtime

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptors (matching the il2cpp_streamio.h extern
// declarations and the transpiler's &System_IO_*_class emission -- the
// versionuri.cpp System_Version_class precedent). All four stream classes
// share the Il2CppStreamObject layout (header + int32 handle); NO reference
// fields, so the collector never scans past the header (refFieldBitmap 0).
//------------------------------------------------------------------------------
#define STREAM_CLASS(SYM, DOTNAME) \
    ::il2cpp::Il2CppClass SYM = {   \
        DOTNAME,                                     \
        static_cast<int32_t>(sizeof(::il2cpp::Il2CppStreamObject)), \
        0u,                                          \
        0,                                           \
        &System_Object_class,                        \
        nullptr, 0, nullptr, 0,                      \
    }

STREAM_CLASS(System_IO_FileStream_class, "System.IO.FileStream");
STREAM_CLASS(System_IO_StreamReader_class, "System.IO.StreamReader");
STREAM_CLASS(System_IO_StreamWriter_class, "System.IO.StreamWriter");
STREAM_CLASS(System_IO_MemoryStream_class, "System.IO.MemoryStream");
STREAM_CLASS(System_IO_BinaryReader_class, "System.IO.BinaryReader");
STREAM_CLASS(System_IO_BinaryWriter_class, "System.IO.BinaryWriter");

#undef STREAM_CLASS

namespace il2cpp {

namespace {

// Layout pin: the handle sits at slot 1 (offset 8), mirroring the
// refFieldBitmap slot convention (slot 0 = klass header).
static_assert(offsetof(Il2CppStreamObject, handle) == 1 * sizeof(void*),
              "Il2CppStreamObject: handle must sit at slot 1 (offset 8)");

bool g_streamClassesRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_stream_register_classes -- register the SIX stream descriptors once
// (idempotent -- il2cpp_class_register dedupes by pointer; called by the
// creation icalls and tests before the first allocation).
//------------------------------------------------------------------------------
void il2cpp_stream_register_classes()
{
    if (g_streamClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_IO_FileStream_class);
    il2cpp_class_register(&System_IO_StreamReader_class);
    il2cpp_class_register(&System_IO_StreamWriter_class);
    il2cpp_class_register(&System_IO_MemoryStream_class);
    il2cpp_class_register(&System_IO_BinaryReader_class);
    il2cpp_class_register(&System_IO_BinaryWriter_class);
    g_streamClassesRegistered = true;
}

} // namespace il2cpp
