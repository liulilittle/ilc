//------------------------------------------------------------------------------
// io.cpp -- System.IO base icall implementations (see il2cpp_io.h for the
// contract and result conventions).
//
// Portability note: all file OPEN/READ/WRITE/DELETE goes through the
// platform-layer file domain (plat_file_open / plat_file_remove -- UTF-8
// byte paths; Windows converts to the wide CRT internally, so non-ASCII
// UTF-8 paths are NOT mangled by the ANSI code page). Existence/type
// probing uses <sys/stat.h> (::_stat on _WIN32 / ::stat on POSIX). The two
// compilers differ in the POSIX-name surface: MSVC exposes only the
// underscore-prefixed variants (_stat, _mkdir, _rmdir, _S_IFDIR/_S_IFREG),
// which MinGW-w64 also provides, so the _WIN32 branch below uses exactly
// those names on both compilers. The non-Windows branch uses the POSIX
// names (GCC 7.5 baseline: no std::filesystem).
//
// Encoding: file content is UTF-8 both ways (the .NET Framework default for
// ReadAllText/WriteAllText). Two private transcoders bridge the UTF-16
// managed string representation and UTF-8 bytes:
//   * Utf8ToCs  -- UTF-8 bytes (explicit length; embedded NULs are content,
//                  unlike il2cpp_string_new_from_utf8's strlen contract) to
//                  a managed string, with full BMP + surrogate-pair decoding
//                  and U+FFFD replacement for invalid sequences.
//   * CsToUtf8  -- managed UTF-16 string to UTF-8 bytes (incl. surrogate
//                  pairs emitted as 4-byte sequences).
//
// Path semantics (Windows-flavoured, .NET Framework 4.8 reference -- verified
// against a net48 reference run):
//   * GetFileName: segment after the final '/' or '\'; "" for a trailing
//     separator ("a\\b\\" -> "", "plain" -> "plain").
//   * GetExtension: from the final '.' in the file name; "" when the dot is
//     the LAST character ("file." -> "" -- net48 returns empty for a trailing
//     dot), or absent ("noext"). A leading dot IS an extension: ".gitignore"
//     -> ".gitignore" (net48 verified).
//   * Combine: 'first' + '\\' + 'second'; an empty part yields the other
//     part; a second part that STARTS with a separator is rooted and wins
//     unchanged ("a" + "\\b" -> "\\b" -- net48 behavior); otherwise a
//     trailing separator on the first part is reused ("a\\" + "b" -> "a\\b").
//   * CreateDirectory: recursive -- every missing level of the path is
//     created in order; a drive-letter prefix ("C:") and existing levels are
//     skipped rather than recreated (idempotent).
//------------------------------------------------------------------------------

#include "il2cpp_io.h"

#include "il2cpp_array.h"
#include "il2cpp_encoding.h" // il2cpp_encoding_unicode_convert / utf8_convert
#include "il2cpp_exception.h"     // il2cpp_throw_new (Directory.Delete exception transport)
#include "il2cpp_exception_ext.h" // il2cpp_throw_io_exception / _file_not_found (fileio2 status mapping)
#include "il2cpp_metadata.h"      // System_Object_class (FileInfo descriptor base)
#include "il2cpp_runtime.h"   // il2cpp_class_register
#include "il2cpp_string.h"
#include "il2cpp_time.h"      // Il2CppDateTime (GetLastWriteTime)
#include "pathproc.h"         // il2cpp_path_change_extension (icall alias)
#include "platform.h"         // plat_file_open / plat_file_remove (UTF-8 file domain)
#include "timezone.h"         // il2cpp_datetime_to_local (GetLastWriteTime local ticks)

#include <algorithm>   // std::replace (POSIX path normalization)
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#if defined(_WIN32)
    // MSVC and MinGW-w64 both provide the underscore-prefixed CRT names
    // (_mkdir/_rmdir/_stat/_S_IFDIR) -- the portable common denominator.
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>   // CopyFileW/MoveFileW/GetFileAttributesW/GetFileAttributesExW
    #include <sys/stat.h>  // ::_wstat, _S_IFDIR, _S_IFREG
#else
    #include <cerrno>      // ::errno, EXDEV (Move cross-device fallback)
    #include <dirent.h>    // ::opendir / ::readdir / ::closedir (recursive Delete)
    #include <sys/stat.h>  // ::stat, ::mkdir
    #include <sys/types.h>
    #include <unistd.h>    // ::rmdir, ::rename, ::unlink (POSIX; absent from <sys/stat.h>)
#endif

//------------------------------------------------------------------------------
// System.IO.FileInfo runtime descriptor (GLOBAL scope -- the transpiler emits
// &System_IO_FileInfo_class for newobj/isinst/castclass, the same contract as
// System_String_class in metadata.cpp). instanceSize covers the header + the
// path reference slot; slot 1 IS a GC reference, so the collector scans it
// (refFieldBitmap 0b10 -- the fileinfo_length probe relies on the path
// staying reachable).
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_IO_FileInfo_class = {
    "System.IO.FileInfo",                    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppFileInfoObject)), // instanceSize
    0b00010u,                                // refFieldBitmap: bit 1 = path (GC ref)
    0,                                       // refFieldCount
    &System_Object_class,                    // baseClass (a FileInfo IS-A object)
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

namespace il2cpp {

namespace {

// Layout pin: the object icalls read the path at a fixed offset.
static_assert(offsetof(Il2CppFileInfoObject, path) == 1 * sizeof(void*),
              "Il2CppFileInfoObject: path must sit at slot 1 (offset 8)");

bool g_fileinfoClassRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_fileinfo_class -- the System.IO.FileInfo descriptor (lazily
// registered on first use; idempotent -- il2cpp_class_register dedupes by
// pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_fileinfo_class()
{
    if (!g_fileinfoClassRegistered)
    {
        il2cpp_class_register(&System_IO_FileInfo_class);
        g_fileinfoClassRegistered = true;
    }
    return &System_IO_FileInfo_class;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

namespace {

#if !defined(_WIN32)
// Windows-flavoured path names ('\' separators) arriving from managed code
// are normalized to POSIX '/' before touching the real filesystem -- the
// CreateDirectory/Delete/Exists observables must agree on ONE name space
// (NDK residual-delete regression: mkdir("a\b") used to create a LITERAL
// backslash entry that RemoveDirTreeUtf8's '/'-joined walk never matched).
std::string NativePosixPath(const std::string& path)
{
    std::string norm = path;
    std::replace(norm.begin(), norm.end(), '\\', '/');
    return norm;
}
#endif

//------------------------------------------------------------------------------
// Filesystem probes.
//------------------------------------------------------------------------------

// True when 'path' exists and names a directory ("" and impossible paths
// probe false -- stat never matches). Delegated to the platform layer:
// Windows uses the wide stat so UTF-8 paths are exact (the narrow _stat
// reads the bytes through the ANSI code page).
bool PathIsDir(const std::string& path)
{
    if (path.empty())
    {
        return false;
    }
    return plat_path_is_dir(path.c_str());
}

// True when 'path' exists and names a regular file (directories and missing
// paths probe false -- the .NET File.Exists contract). Same wide-path
// delegation as PathIsDir.
bool PathIsFile(const std::string& path)
{
    if (path.empty())
    {
        return false;
    }
    return plat_path_is_file(path.c_str());
}

//------------------------------------------------------------------------------
// UTF-16 <-> UTF-8 transcoding (managed string <-> byte content).
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
            // ASCII: one unit.
            units.push_back(static_cast<char16_t>(c));
            ++i;
        }
        else if ((c & 0xE0) == 0xC0 && i + 1 < length && IsContinuationByte(static_cast<unsigned char>(data[i + 1])))
        {
            // 2-byte sequence: 110xxxxx 10xxxxxx.
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
            // 3-byte sequence: 1110xxxx 10xxxxxx 10xxxxxx.
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
            // 4-byte sequence: 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx -> pair.
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

// Managed string -> null-terminated UTF-8 path string (std::string is
// NUL-terminated; embedded NULs would truncate the path -- acceptable,
// filesystem paths cannot contain NUL in any case).
std::string CsToUtf8Z(Il2CppObject* obj)
{
    return CsToUtf8(obj);
}

#if defined(_WIN32)
// Managed string -> NUL-terminated wchar_t buffer (wchar_t is 16-bit on
// Windows, so this is a straight copy of the UTF-16 payload -- the
// pathproc.cpp CsToWide precedent). Used by the Win32 file operations
// (CopyFileW / MoveFileW / GetFileAttributesW / SetFileAttributesW /
// GetFileAttributesExW / _wstat) so non-ASCII UTF-8 names stay EXACT --
// the narrow CRT would read the UTF-8 bytes through the ANSI code page.
std::vector<wchar_t> CsToWide(Il2CppObject* obj)
{
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(obj);
    std::vector<wchar_t> out(static_cast<size_t>(s->length) + 1);
    for (int32_t i = 0; i < s->length; ++i)
    {
        out[static_cast<size_t>(i)] = static_cast<wchar_t>(s->chars[i]);
    }
    out[static_cast<size_t>(s->length)] = L'\0';
    return out;
}
#endif

//------------------------------------------------------------------------------
// Path string helpers (Windows semantics; see the file header).
//------------------------------------------------------------------------------

// The file-name segment: everything after the final directory separator --
// or, on Windows, the final volume separator ':' (net48 Path.GetFileName /
// GetExtension split on DirectorySeparatorChar + AltDirectorySeparatorChar +
// VolumeSeparatorChar: "C:file.txt" -> "file.txt", "C:" -> "").
std::string FileNameSegment(const std::string& path)
{
#if defined(_WIN32)
    const size_t sep = path.find_last_of("/\\:");
#else
    const size_t sep = path.find_last_of("/\\");
#endif
    return (sep == std::string::npos) ? path : path.substr(sep + 1);
}

//------------------------------------------------------------------------------
// Line splitting (File.ReadAllLines): split on '\n' and on '\r', treating a
// '\r\n' pair as ONE terminator (net48 StreamReader.ReadLine semantics --
// probed: "a\rb" is two lines, "\r\n" is one terminator, "\r\r" yields an
// empty middle line). A trailing terminator does NOT produce a trailing
// empty line ("a\n" -> ["a"]).
//------------------------------------------------------------------------------
std::vector<std::string> SplitLines(const std::string& content)
{
    std::vector<std::string> lines;
    size_t pos = 0;
    while (pos < content.size())
    {
        const size_t r = content.find('\r', pos);
        const size_t n = content.find('\n', pos);
        size_t term;
        if (r == std::string::npos)
        {
            term = n;
        }
        else if (n == std::string::npos)
        {
            term = r;
        }
        else
        {
            term = (r < n) ? r : n;
        }
        if (term == std::string::npos)
        {
            lines.push_back(content.substr(pos)); // last line without trailing newline
            break;
        }
        lines.push_back(content.substr(pos, term - pos));
        if (content[term] == '\r' && term + 1 < content.size() && content[term + 1] == '\n')
        {
            pos = term + 2; // CRLF: consume both units
        }
        else
        {
            pos = term + 1; // lone '\r' or '\n'
        }
    }
    return lines; // "" -> no iterations -> zero-length vector (empty file)
}

// net48 StreamReader BOM detection for the whole-file text APIs: strip a
// UTF-8 BOM (EF BB BF), transcode a UTF-16LE (FF FE) / UTF-16BE (FE FF)
// stream to UTF-8, otherwise pass the bytes through (UTF-8 default). The
// UTF-32LE signature FF FE 00 00 is NOT wired (documented deviation: it is
// treated as UTF-16LE here; no probe exercises it yet).
// Returns the UTF-8 text in 'out'.
void DecodeTextWithBom(const std::string& bytes, std::string& out)
{
    const unsigned char* b = reinterpret_cast<const unsigned char*>(bytes.data());
    const size_t n = bytes.size();
    size_t off = 0;
    int utf16 = 0; // 0 = none, 1 = LE, 2 = BE
    if (n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
    {
        off = 3;
    }
    else if (n >= 2 && b[0] == 0xFF && b[1] == 0xFE)
    {
        off = 2;
        utf16 = 1;
    }
    else if (n >= 2 && b[0] == 0xFE && b[1] == 0xFF)
    {
        off = 2;
        utf16 = 2;
    }
    if (utf16 == 0)
    {
        out.assign(bytes, off, std::string::npos);
        return;
    }
    const size_t unitBytes = n - off;
    std::string le(bytes, off, unitBytes);
    if (utf16 == 2)
    {
        // In-place byte swap: the BE unit stream -> the LE unit stream.
        for (size_t i = 0; i + 1 < le.size(); i += 2)
        {
            const char t = le[i];
            le[i] = le[i + 1];
            le[i + 1] = t;
        }
    }
    // UTF-16LE byte stream -> UTF-16 units. The runtime targets little-endian
    // hosts (x64/aarch64); the BE signature was byte-swapped above, so a raw
    // copy IS the LE decode. The UTF-8->UTF-16 converter must NOT be used
    // here (it decoded the LE bytes as UTF-8: "A\0B\0" -- ioaudit4 bug).
    // A trailing odd byte is dropped (truncated final unit).
    const int32_t units = static_cast<int32_t>(le.size() / 2);
    std::u16string u16(static_cast<size_t>(units), u'\0');
    if (units > 0)
    {
        std::memcpy(u16.data(), le.data(), static_cast<size_t>(units) * sizeof(char16_t));
    }
    // net48 StreamReader flush semantics for unpaired UTF-16 surrogates
    // (oracle: .opencode/tmp/a1reds/evidence-a1surro-oracle.out): a TRAILING
    // high surrogate is DROPPED, a high followed by a non-low unit and every
    // lone low unit decode to U+FFFD, valid pairs pass through. Reproduce
    // before the UTF-8 encode (the encoder alone would emit U+FFFD for the
    // trailing high case).
    std::u16string clean;
    clean.reserve(u16.size());
    for (size_t i = 0; i < u16.size(); ++i)
    {
        const char16_t c = u16[i];
        if (c >= 0xD800 && c <= 0xDBFF)
        {
            if (i + 1 < u16.size() && u16[i + 1] >= 0xDC00 && u16[i + 1] <= 0xDFFF)
            {
                clean.push_back(c);
                clean.push_back(u16[++i]);
            }
            else if (i + 1 < u16.size())
            {
                clean.push_back(0xFFFD);
            }
            // trailing high surrogate: dropped (net48 decoder flush)
        }
        else if (c >= 0xDC00 && c <= 0xDFFF)
        {
            clean.push_back(0xFFFD);
        }
        else
        {
            clean.push_back(c);
        }
    }
    const char16_t* cleanUnits = clean.data();
    const int32_t cleanCount = static_cast<int32_t>(clean.size());
    const int32_t needed = il2cpp_encoding_utf8_convert(cleanUnits, cleanCount, nullptr, 0);
    out.resize(static_cast<size_t>(needed > 0 ? needed : 0));
    if (needed > 0)
    {
        il2cpp_encoding_utf8_convert(cleanUnits, cleanCount, out.data(), needed);
    }
}

//------------------------------------------------------------------------------
// File helpers (binary-mode FILE* via the platform layer -- UTF-8 paths).
//------------------------------------------------------------------------------

// Read the remaining bytes of an open binary stream into 'out'. The caller
// opened the stream with plat_file_open (already platform-correct) and owns
// the fclose.
void ReadFileContents(FILE* fp, std::string& out)
{
    out.clear();
    char buffer[8192];
    std::size_t n;
    while ((n = std::fread(buffer, 1, sizeof(buffer), fp)) != 0)
    {
        out.append(buffer, n);
    }
}

// Write 'bytes' to 'path' opened with 'mode' (binary), closing on every
// path. Returns 0 on success, 1 on failure -- the icall_file_* convention.
int32_t WriteBytesToFile(const std::string& path, const std::string& bytes,
                         const char* mode)
{
    FILE* out = plat_file_open(path.c_str(), mode);
    if (out == nullptr)
    {
        return 1; // unopenable target (missing directory, read-only, ...)
    }
    const std::size_t written = std::fwrite(bytes.data(), 1, bytes.size(), out);
    const bool writeOk = written == bytes.size() && std::ferror(out) == 0;
    const bool closeOk = std::fclose(out) == 0;
    return (writeOk && closeOk) ? 0 : 1;
}

} // namespace

//------------------------------------------------------------------------------
// System.IO.File.
//------------------------------------------------------------------------------

int32_t icall_file_exists(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return 0;
    }
    return PathIsFile(CsToUtf8Z(path)) ? 1 : 0;
}

int32_t icall_file_delete(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return -1;
    }
    const std::string p = CsToUtf8Z(path);
    if (PathIsDir(p))
    {
        return 1; // net48 File.Delete on a directory throws UnauthorizedAccessException
    }
    if (!PathIsFile(p))
    {
        return 0; // .NET semantics: deleting a missing file is silent success
    }
    return plat_file_remove(p.c_str()) ? 0 : 1;
}

int32_t icall_file_write_all_text(Il2CppObject* path, Il2CppObject* contents)
{
    if (path == nullptr)
    {
        return -1;
    }
    const std::string p = CsToUtf8Z(path);
    const std::string bytes = (contents != nullptr) ? CsToUtf8(contents) : std::string();
    return WriteBytesToFile(p, bytes, "wb");
}

int32_t icall_file_append_all_text(Il2CppObject* path, Il2CppObject* contents)
{
    if (path == nullptr)
    {
        return -1;
    }
    const std::string p = CsToUtf8Z(path);
    const std::string bytes = (contents != nullptr) ? CsToUtf8(contents) : std::string();
    // "ab": creates the file when missing, appends otherwise -- the net48
    // File.AppendAllText contract. The explicit length write keeps embedded
    // NULs as content (same as WriteAllText).
    return WriteBytesToFile(p, bytes, "ab");
}

Il2CppObject* icall_file_read_all_text(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const std::string p = CsToUtf8Z(path);
    FILE* in = plat_file_open(p.c_str(), "rb");
    if (in == nullptr)
    {
        // net48 File.ReadAllText on a missing file throws
        // FileNotFoundException (diffio001 readalltext.missing).
        il2cpp::il2cpp_throw_file_not_found_exception(p.c_str());
    }
    std::string bytes;
    ReadFileContents(in, bytes);
    (void)std::fclose(in);
    std::string utf8;
    DecodeTextWithBom(bytes, utf8);
    return Utf8ToCs(utf8.data(), utf8.size());
}

int32_t icall_file_write_all_lines(Il2CppObject* path, Il2CppObject* lines)
{
    if (path == nullptr || lines == nullptr)
    {
        return -1;
    }
    const std::string p = CsToUtf8Z(path);
    const Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(lines);
    FILE* out = plat_file_open(p.c_str(), "wb");
    if (out == nullptr)
    {
        return 1;
    }
    bool writeOk = true;
    for (int32_t i = 0; i < arr->length; ++i)
    {
        Il2CppObject* line = *reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(const_cast<Il2CppArray*>(arr), i));
        if (line == nullptr)
        {
            // A null element is ArgumentNullException in .NET; roll back the
            // partially written file so no half-finished artifact remains.
            (void)std::fclose(out);
            (void)plat_file_remove(p.c_str());
            return -2;
        }
        const std::string bytes = CsToUtf8(line);
        if (std::fwrite(bytes.data(), 1, bytes.size(), out) != bytes.size() ||
            std::fwrite("\r\n", 1, 2, out) != 2)
        {
            writeOk = false;
            break;
        }
    }
    const bool writeErr = std::ferror(out) != 0;
    const bool closeOk = std::fclose(out) == 0;
    return (writeOk && !writeErr && closeOk) ? 0 : 1;
}

Il2CppObject* icall_file_read_all_lines(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const std::string p = CsToUtf8Z(path);
    FILE* in = plat_file_open(p.c_str(), "rb");
    if (in == nullptr)
    {
        // Same net48 contract as ReadAllText (same defect root).
        il2cpp::il2cpp_throw_file_not_found_exception(p.c_str());
    }
    std::string bytes;
    ReadFileContents(in, bytes);
    (void)std::fclose(in);
    std::string utf8;
    DecodeTextWithBom(bytes, utf8);
    const std::vector<std::string> lines = SplitLines(utf8);
    Il2CppArray* arr = il2cpp_array_new(
        il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true,
        static_cast<int32_t>(lines.size()));
    for (size_t i = 0; i < lines.size(); ++i)
    {
        Il2CppString* line = reinterpret_cast<Il2CppString*>(
            Utf8ToCs(lines[i].data(), lines[i].size()));
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, static_cast<int32_t>(i)))
            = reinterpret_cast<Il2CppObject*>(line);
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

  Il2CppArray* icall_file_read_all_bytes(Il2CppObject* path)
  {
      if (path == nullptr)
      {
          return nullptr;
      }
      const std::string p = CsToUtf8Z(path);
      FILE* in = plat_file_open(p.c_str(), "rb");
      if (in == nullptr)
      {
          // Same net48 FileNotFoundException contract (same defect root).
          il2cpp::il2cpp_throw_file_not_found_exception(p.c_str());
      }
      std::string bytes;
      ReadFileContents(in, bytes);
      (void)std::fclose(in);
      Il2CppArray* arr = il2cpp_array_new(
          nullptr, 1, false, static_cast<int32_t>(bytes.size()));
      if (!bytes.empty())
      {
          std::memcpy(arr->data, bytes.data(), bytes.size());
      }
      return arr;
  }

  int32_t icall_file_write_all_bytes(Il2CppObject* path, Il2CppArray* bytes)
  {
      if (path == nullptr)
      {
          return -1;
      }
      const std::string p = CsToUtf8Z(path);
      std::string data;
      if (bytes != nullptr && bytes->length > 0)
      {
          data.assign(reinterpret_cast<const char*>(bytes->data),
                      static_cast<size_t>(bytes->length));
      }
      return WriteBytesToFile(p, data, "wb");
  }

  //------------------------------------------------------------------------------
  // File.Copy / File.Move / File.GetAttributes / File.SetAttributes /
// File.GetLastWriteTime (net48 gap closure -- see il2cpp_io.h).
//------------------------------------------------------------------------------

namespace {

#if !defined(_WIN32)
// The Unix epoch (1970-01-01) expressed in .NET ticks (100 ns units).
constexpr int64_t kIoUnixEpochTicks = 621355968000000000;

// Byte-copy the content of 'src' into 'dst' and mirror the permission bits
// (POSIX-only; Windows uses CopyFileW which preserves attributes itself).
// Returns 0 on success, 1 on failure. The caller owns the overwrite /
// existence gates.
int32_t CopyBytesAndMode(const std::string& src, const std::string& dst)
{
    FILE* in = plat_file_open(src.c_str(), "rb");
    if (in == nullptr)
    {
        return 1;
    }
    FILE* out = plat_file_open(dst.c_str(), "wb");
    if (out == nullptr)
    {
        (void)std::fclose(in);
        return 1;
    }
    bool ok = true;
    char buffer[8192];
    std::size_t n;
    while ((n = std::fread(buffer, 1, sizeof(buffer), in)) != 0)
    {
        if (std::fwrite(buffer, 1, n, out) != n)
        {
            ok = false;
            break;
        }
    }
    if (std::ferror(in) != 0)
    {
        ok = false;
    }
    const bool closeOk = std::fclose(out) == 0;
    (void)std::fclose(in);
    if (ok && closeOk)
    {
        struct stat st;
        if (::stat(NativePosixPath(src).c_str(), &st) == 0)
        {
            (void)::chmod(NativePosixPath(dst).c_str(), st.st_mode & 07777);
        }
        return 0;
    }
    return 1;
}
#endif

} // namespace

// fileio2 (m7m): classify a failed File.Copy/Move -- a missing source is
// FileNotFoundException (net48), every other failure (existing destination
// without overwrite, I/O error) is IOException. Always throws (noreturn).
[[noreturn]] void ThrowFileOperationFailure(Il2CppObject* source)
{
    if (source != nullptr && PathIsFile(CsToUtf8Z(source)))
    {
        il2cpp_throw_io_exception("I/O error.");
    }
    il2cpp_throw_file_not_found_exception("The system cannot find the file specified.");
    // il2cpp_throw_* throws a C++ exception and never returns; the abort is
    // a defensive fallback that also silences -Winvalid-noreturn (the same
    // pattern as array.cpp il2cpp_array_fail).
    std::abort();
}

int32_t icall_file_copy(Il2CppObject* source, Il2CppObject* dest)
{
    // File.Copy(source, dest): never overwrites an existing destination
    // (net48 throws IOException); a missing source is FileNotFoundException.
    // fileio2 (m7m): the icall status now maps to the .NET exceptions instead
    // of being silently discarded.
    if (source == nullptr || dest == nullptr)
    {
        return -1;
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wsrc = CsToWide(source);
    const std::vector<wchar_t> wdst = CsToWide(dest);
    if (wsrc.empty() || wdst.empty())
    {
        return 1;
    }
    // bFailIfExists = TRUE: fails when the destination already exists.
    if (::CopyFileW(wsrc.data(), wdst.data(), TRUE) == 0)
    {
        ThrowFileOperationFailure(source);
    }
    return 0;
#else
    const std::string src = CsToUtf8Z(source);
    const std::string dst = CsToUtf8Z(dest);
    if (!PathIsFile(src))
    {
        il2cpp_throw_file_not_found_exception("The system cannot find the file specified.");
    }
    if (PathIsFile(dst) || PathIsDir(dst))
    {
        il2cpp_throw_io_exception("The destination file already exists.");
    }
    if (CopyBytesAndMode(src, dst) != 0)
    {
        il2cpp_throw_io_exception("I/O error.");
    }
    return 0;
#endif
}

int32_t icall_file_copy_overwrite(Il2CppObject* source, Il2CppObject* dest,
                                  int32_t overwrite)
{
    // File.Copy(source, dest, overwrite): replaces the destination when
    // 'overwrite' is non-zero, fails otherwise (net48 semantics -- an
    // existing destination with overwrite=false is IOException).
    // fileio2 (m7m): the icall status now maps to the .NET exceptions instead
    // of being silently discarded.
    if (source == nullptr || dest == nullptr)
    {
        return -1;
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wsrc = CsToWide(source);
    const std::vector<wchar_t> wdst = CsToWide(dest);
    if (wsrc.empty() || wdst.empty())
    {
        return 1;
    }
    if (::CopyFileW(wsrc.data(), wdst.data(), overwrite ? FALSE : TRUE) == 0)
    {
        ThrowFileOperationFailure(source);
    }
    return 0;
#else
    const std::string src = CsToUtf8Z(source);
    const std::string dst = CsToUtf8Z(dest);
    if (!PathIsFile(src))
    {
        il2cpp_throw_file_not_found_exception("The system cannot find the file specified.");
    }
    if ((PathIsFile(dst) || PathIsDir(dst)) && !overwrite)
    {
        il2cpp_throw_io_exception("The destination file already exists.");
    }
    if (CopyBytesAndMode(src, dst) != 0)
    {
        il2cpp_throw_io_exception("I/O error.");
    }
    return 0;
#endif
}

int32_t icall_file_move(Il2CppObject* source, Il2CppObject* dest)
{
    // File.Move(source, dest): the source must exist and the destination
    // must NOT exist (net48 throws FileNotFoundException / IOException).
    // Works across volumes (Windows MoveFileW handles it; POSIX falls back
    // to copy+delete on EXDEV).
    // fileio2 (m7m): the icall status now maps to the .NET exceptions instead
    // of being silently discarded.
    if (source == nullptr || dest == nullptr)
    {
        return -1;
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wsrc = CsToWide(source);
    const std::vector<wchar_t> wdst = CsToWide(dest);
    if (wsrc.empty() || wdst.empty())
    {
        return 1;
    }
    if (::MoveFileW(wsrc.data(), wdst.data()) == 0)
    {
        ThrowFileOperationFailure(source);
    }
    return 0;
#else
    const std::string src = CsToUtf8Z(source);
    const std::string dst = CsToUtf8Z(dest);
    if (!PathIsFile(src))
    {
        il2cpp_throw_file_not_found_exception("The system cannot find the file specified.");
    }
    if (PathIsFile(dst) || PathIsDir(dst))
    {
        il2cpp_throw_io_exception("The destination file already exists.");
    }
    if (::rename(src.c_str(), dst.c_str()) == 0)
    {
        return 0;
    }
    if (errno == EXDEV)
    {
        // Cross-device move: net48 works via copy+delete.
        if (CopyBytesAndMode(src, dst) == 0 && plat_file_remove(src.c_str()))
        {
            return 0;
        }
    }
    il2cpp_throw_io_exception("I/O error.");
    // il2cpp_throw_io_exception throws a C++ exception and never returns; the
    // abort is a defensive fallback that also silences -Wreturn-type (the same
    // pattern as io.cpp:646).
    std::abort();
#endif
}

int32_t icall_file_get_attributes(Il2CppObject* path)
{
    // File.GetAttributes(path): the FileAttributes mask (net48 returns the
    // Win32 FILE_ATTRIBUTE_* bits; a directory carries the Directory bit).
    // A missing/null path signals 0 (net48 throws FileNotFoundException --
    // not transportable in M3; 0 is never a valid real-file mask).
    if (path == nullptr)
    {
        return 0;
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wide = CsToWide(path);
    if (wide.empty())
    {
        return 0;
    }
    const DWORD attrs = ::GetFileAttributesW(wide.data());
    if (attrs == INVALID_FILE_ATTRIBUTES)
    {
        return 0; // missing path -> 0 (failure signal)
    }
    return static_cast<int32_t>(attrs);
#else
    const std::string p = CsToUtf8Z(path);
    if (p.empty())
    {
        return 0;
    }
    struct stat st;
    if (::stat(NativePosixPath(p).c_str(), &st) != 0)
    {
        return 0;
    }
    if (S_ISDIR(st.st_mode))
    {
        return 16; // FileAttributes.Directory
    }
    int32_t mask = 32; // FileAttributes.Archive (regular file)
    if ((st.st_mode & (S_IWUSR | S_IWGRP | S_IWOTH)) == 0)
    {
        mask |= 1; // FileAttributes.ReadOnly
    }
    return mask;
#endif
}

int32_t icall_file_set_attributes(Il2CppObject* path, int32_t attributes)
{
    // File.SetAttributes(path, attributes): REPLACES the attribute mask
    // (net48 SetFileAttributesW semantics -- probed: setting ReadOnly clears
    // the Archive bit, setting Normal yields exactly 0x80). A missing/null
    // path is an error (net48 FileNotFoundException -> M3 non-zero status).
    if (path == nullptr)
    {
        return 1;
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wide = CsToWide(path);
    if (wide.empty())
    {
        return 1;
    }
    return ::SetFileAttributesW(wide.data(), static_cast<DWORD>(attributes)) != 0 ? 0 : 1;
#else
    const std::string p = CsToUtf8Z(path);
    if (p.empty())
    {
        return 1;
    }
    if (!PathIsFile(p) && !PathIsDir(p))
    {
        return 1;
    }
    struct stat st;
    if (::stat(NativePosixPath(p).c_str(), &st) != 0)
    {
        return 1;
    }
    mode_t mode = st.st_mode;
    if ((attributes & 0x01) != 0)
    {
        mode &= ~static_cast<mode_t>(S_IWUSR | S_IWGRP | S_IWOTH); // ReadOnly
    }
    else
    {
        mode |= static_cast<mode_t>(S_IWUSR | S_IWGRP | S_IWOTH);
    }
    return ::chmod(p.c_str(), mode) == 0 ? 0 : 1;
#endif
}

Il2CppDateTime icall_file_get_last_write_time(Il2CppObject* path)
{
    // File.GetLastWriteTime(path): a LOCAL DateTime (net48 Kind=Local) of
    // the file's last write. A missing path returns 1601-01-01 00:00 LOCAL
    // (DateTime.FromFileTime(0) -- net48 does NOT throw here; probed). A
    // null path returns DateTime.MinValue (M3 convention for null input).
    constexpr int64_t kIoFileTimeEpochTicks = 504911232000000000; // 1601-01-01 00:00 UTC
    if (path == nullptr)
    {
        return Il2CppDateTime(0);
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wide = CsToWide(path);
    if (wide.empty())
    {
        return il2cpp_datetime_to_local(Il2CppDateTime(kIoFileTimeEpochTicks));
    }
    WIN32_FILE_ATTRIBUTE_DATA data = {};
    if (!::GetFileAttributesExW(wide.data(), GetFileExInfoStandard, &data))
    {
        return il2cpp_datetime_to_local(Il2CppDateTime(kIoFileTimeEpochTicks));
    }
    const uint64_t ft = (static_cast<uint64_t>(data.ftLastWriteTime.dwHighDateTime) << 32)
        | static_cast<uint64_t>(data.ftLastWriteTime.dwLowDateTime);
    const int64_t utcTicks = kIoFileTimeEpochTicks + static_cast<int64_t>(ft);
    return il2cpp_datetime_to_local(Il2CppDateTime(utcTicks));
#else
    const std::string p = CsToUtf8Z(path);
    if (p.empty())
    {
        return il2cpp_datetime_to_local(Il2CppDateTime(kIoFileTimeEpochTicks));
    }
    struct stat st;
    if (::stat(NativePosixPath(p).c_str(), &st) != 0)
    {
        return il2cpp_datetime_to_local(Il2CppDateTime(kIoFileTimeEpochTicks));
    }
    const int64_t utcTicks = kIoUnixEpochTicks + static_cast<int64_t>(st.st_mtime) * 10000000;
    return il2cpp_datetime_to_local(Il2CppDateTime(utcTicks));
#endif
}

//------------------------------------------------------------------------------
// System.IO.Path.
//------------------------------------------------------------------------------

Il2CppObject* icall_path_get_file_name(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const std::string name = FileNameSegment(CsToUtf8Z(path));
    return Utf8ToCs(name.data(), name.size());
}

Il2CppObject* icall_path_get_extension(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const std::string name = FileNameSegment(CsToUtf8Z(path));
    std::string ext;
    const size_t dot = name.find_last_of('.');
    // net48 semantics: a trailing dot is NOT an extension ("file." -> "").
    if (dot != std::string::npos && dot != name.size() - 1)
    {
        ext = name.substr(dot);
    }
    return Utf8ToCs(ext.data(), ext.size());
}

Il2CppObject* icall_path_combine(Il2CppObject* first, Il2CppObject* second)
{
    if (first == nullptr || second == nullptr)
    {
        return nullptr;
    }
    const std::string a = CsToUtf8Z(first);
    const std::string b = CsToUtf8Z(second);
    std::string result;
    if (a.empty())
    {
        result = b;
    }
    else if (b.empty())
    {
        result = a;
    }
    else if (b.front() == '\\' || b.front() == '/')
    {
        result = b; // a separator-led second part is rooted and wins unchanged
    }
    else if (a.back() == '\\' || a.back() == '/')
    {
        result = a + b; // trailing separator in the first part is reused
    }
    else
    {
        result = a + "\\" + b; // canonical join
    }
    return Utf8ToCs(result.data(), result.size());
}

Il2CppObject* icall_path_combine_many(Il2CppObject* paths)
{
    // Path.Combine(params string[]) -- the params-array overload. Every
    // element is combined left-to-right through the 2-arg icall semantics
    // (a null element / null array / net48 empty-array ArgumentException all
    // yield null -- the M3 signal; see il2cpp_io.h).
    if (paths == nullptr)
    {
        return nullptr;
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(paths);
    Il2CppObject* acc = nullptr;
    for (int32_t i = 0; i < arr->length; ++i)
    {
        Il2CppObject* const elem = *reinterpret_cast<Il2CppObject* const*>(
            il2cpp_array_get_elem_ptr(arr, i));
        acc = (acc == nullptr) ? elem : icall_path_combine(acc, elem);
    }
    return acc;
}

Il2CppObject* icall_path_change_extension(Il2CppObject* path, Il2CppObject* extension)
{
    // Thin io-domain alias: the pathproc.cpp implementation owns the net48
    // semantics; this wrapper exposes it through the il2cpp_io.h surface that
    // the generated include chain references (pathproc.h is not included by
    // generated.h).
    return il2cpp_path_change_extension(path, extension);
}

//------------------------------------------------------------------------------
// System.IO.Directory.
//------------------------------------------------------------------------------

int32_t icall_directory_exists(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return 0;
    }
    return PathIsDir(CsToUtf8Z(path)) ? 1 : 0;
}

int32_t icall_directory_create_directory(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return -1;
    }
    const std::string p = CsToUtf8Z(path);
    if (p.empty() || PathIsDir(p))
    {
        return p.empty() ? -1 : 0; // empty path fails; existing dir succeeds
    }
    // Create every missing level in order. A drive-letter prefix ("C:") and
    // already-existing levels probe as directories and are skipped. A drive
    // root prefix is skipped WITHOUT a stat: MSVCRT stat("C:") means "the
    // current directory of drive C" (cwd-relative), so it fails when the
    // process cwd is on another drive and would abort the whole create
    // (the p-nb2-dir temp-dir setup on C:).
    std::string prefix;
    size_t i = 0;
    for (;;)
    {
        const size_t j = p.find_first_of("/\\", i);
        prefix = (j == std::string::npos) ? p : p.substr(0, j);
        const bool driveRoot = prefix.size() == 2 && prefix[1] == ':';
        if (!prefix.empty() && !driveRoot && !PathIsDir(prefix))
        {
            if (!plat_dir_mkdir(prefix.c_str()) && !PathIsDir(prefix))
            {
                return 1; // a level could not be created and is not there
            }
        }
        if (j == std::string::npos)
        {
            break;
        }
        i = j + 1;
    }
    return 0;
}

int32_t icall_directory_delete(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return 1;
    }
    const std::string p = CsToUtf8Z(path);
    // .NET Directory.Delete(string): a missing path / a file / a non-empty
    // directory all throw -- M3 signals them with a non-zero status instead.
    if (p.empty() || !PathIsDir(p))
    {
        return 1;
    }
    return plat_dir_rmdir(p.c_str()) ? 0 : 1;
}

//------------------------------------------------------------------------------
// Recursive tree removal (Directory.Delete(path, true) -- net48 semantics,
// probe-pinned: children are removed before the parent; a read-only entry
// blocks the delete WITHOUT clearing the attribute, matching net48's
// UnauthorizedAccessException -- the attribute is left untouched).
//------------------------------------------------------------------------------

namespace {

#if defined(_WIN32)
// Remove the whole tree rooted at 'path' (UTF-16 wide -- the exact-name
// contract: the narrow CRT would read UTF-8 through the ANSI code page).
// Post-order: files via DeleteFileW, subdirectories recurse first, then the
// directory itself via RemoveDirectoryW. Returns 0 on success, 1 on failure
// (an unremovable entry aborts and the status signals it -- net48 throws).
int32_t RemoveDirTreeWide(const std::wstring& path)
{
    const std::wstring pattern = path + L"\\*";
    WIN32_FIND_DATAW fd;
    const HANDLE hFind = ::FindFirstFileW(pattern.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE)
    {
        return 1; // missing path / cannot enumerate
    }
    bool ok = true;
    do
    {
        const std::wstring name = fd.cFileName;
        if (name == L"." || name == L"..")
        {
            continue;
        }
        const std::wstring child = path + L"\\" + name;
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            if (RemoveDirTreeWide(child) != 0)
            {
                ok = false;
                break;
            }
        }
        else
        {
            // No attribute clearing (net48 leaves the read-only bit and
            // throws); DeleteFileW fails on a read-only file, aborting.
            if (::DeleteFileW(child.c_str()) == 0)
            {
                ok = false;
                break;
            }
        }
    } while (::FindNextFileW(hFind, &fd));
    ::FindClose(hFind);
    if (!ok)
    {
        return 1;
    }
    if (::RemoveDirectoryW(path.c_str()) == 0)
    {
        return 1;
    }
    return 0;
}
#else
// Retry a single POSIX removal (unlink or rmdir) a bounded number of times.
// Android emulated/FUSE storage and network mounts can report a TRANSIENT
// failure (EBUSY, ENOTEMPTY, EPERM, EACCES) while a directory entry is still
// being flushed to the backend by the servicing thread; a short bounded
// backoff lets the stale transient state settle before the final verdict.
// A missing entry counts as success (a concurrent removal already finished).
// Returns 0 on success, non-zero after the final failed attempt.
int32_t TryRemoveEntry(const char* path, bool isDir)
{
    for (int attempt = 0; attempt < 4; ++attempt)
    {
        const int rc = isDir ? ::rmdir(path) : ::unlink(path);
        if (rc == 0)
        {
            return 0;
        }
        const int e = errno;
        if (e == ENOENT)
        {
            return 0; // already gone
        }
        if (e == EBUSY || e == ENOTEMPTY || e == EPERM || e == EACCES)
        {
            ::usleep(25000 << attempt); // 25/50/100/200ms backoff
            continue;
        }
        break;
    }
    return 1;
}

// Remove the whole tree rooted at 'path' (UTF-8 byte path -- POSIX names are
// already exact). Post-order: files via ::unlink, subdirectories recurse
// first, then the directory itself via ::rmdir. Returns 0 on success, 1 on
// failure (an unremovable entry aborts and the status signals it).
int32_t RemoveDirTreeUtf8(const std::string& path)
{
    const std::string norm = NativePosixPath(path);
    DIR* dir = ::opendir(norm.c_str());
    if (dir == nullptr)
    {
        return 1; // missing path / cannot enumerate
    }
    bool ok = true;
    struct dirent* ent;
    while ((ent = ::readdir(dir)) != nullptr)
    {
        const std::string name = ent->d_name;
        if (name == "." || name == "..")
        {
            continue;
        }
        const std::string child = norm + "/" + name;
        struct stat st;
        if (::lstat(child.c_str(), &st) != 0)
        {
            ok = false;
            break;
        }
        if (S_ISDIR(st.st_mode))
        {
            if (RemoveDirTreeUtf8(child) != 0)
            {
                ok = false;
                break;
            }
        }
        else
        {
            if (TryRemoveEntry(child.c_str(), false) != 0)
            {
                std::fprintf(stderr,
                             "[io] RemoveDirTreeUtf8: unlink of '%s' failed "
                             "(errno=%d)\n", child.c_str(), errno);
                ok = false;
                break;
            }
        }
    }
    ::closedir(dir);
    if (!ok)
    {
        return 1;
    }
    if (TryRemoveEntry(norm.c_str(), true) != 0)
    {
        std::fprintf(stderr, "[io] RemoveDirTreeUtf8: rmdir of '%s' failed "
                             "(errno=%d)\n", norm.c_str(), errno);
        return 1;
    }
    return 0;
}
#endif

} // namespace

//------------------------------------------------------------------------------
// Directory.Delete(path, recursive) -- net48 exception transport (rtdel2-b23,
// deletedir e2e): the icall now THROWS the managed exceptions net48 throws
// instead of signalling with a status code. The type chains list the exact
// managed full names + base classes so the generated catch guards
// (il2cpp_exception_matches against the token full names) match per .NET
// inheritance: catch(DirectoryNotFoundException) and catch(IOException) both
// match the DNFE chain (DNFE IS-A IOException in .NET), catch(IOException)
// matches the IO chain, catch(ArgumentNullException) the ANE chain. An
// unremovable ENTRY inside a recursive delete (net48
// UnauthorizedAccessException -- not transportable in M3) stays a non-zero
// status: the attribute is left untouched (rtdel-b23 probe-pinned contract).
//------------------------------------------------------------------------------
namespace {

const char kDirectoryNotFoundChain[] =
    "System.IO.DirectoryNotFoundException,System.IO.DirectoryNotFoundException,"
    "System.IO.IOException,System.SystemException,System.Exception";
const char kIOExceptionChain[] =
    "System.IO.IOException,System.IO.IOException,System.SystemException,System.Exception";
const char kArgumentNullChain[] =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";

} // namespace

int32_t icall_directory_delete_recursive(Il2CppString* path, int32_t recursive)
{
    // Directory.Delete(path, recursive) -- net48 exception contract
    // (csc-probe + deletedir e2e): a null path is ArgumentNullException; a
    // MISSING path is DirectoryNotFoundException (both modes); a path naming
    // a FILE is IOException; a non-empty directory with recursive==0 is
    // IOException with the tree left intact.
    if (path == nullptr)
    {
        il2cpp_throw_new(kArgumentNullChain, "path");
        return 0; // unreachable (il2cpp_throw_new always throws); well-formed path only
    }
    const std::string p = CsToUtf8Z(reinterpret_cast<Il2CppObject*>(path));
    if (p.empty() || !PathIsDir(p))
    {
        // A path that exists as a regular FILE is IOException (net48); any
        // other non-directory path (missing) is DirectoryNotFoundException.
        if (!p.empty() && PathIsFile(p))
        {
            il2cpp_throw_new(kIOExceptionChain,
                             "The specified path is not a valid directory.");
            return 0; // unreachable
        }
        il2cpp_throw_new(kDirectoryNotFoundChain,
                         "Could not find a part of the path.");
        return 0; // unreachable
    }
    if (recursive == 0)
    {
        // Only an EMPTY directory is removed; the rmdir fails on a
        // non-empty directory -- the net48 IOException, tree intact.
        if (!plat_dir_rmdir(p.c_str()))
        {
            il2cpp_throw_new(kIOExceptionChain, "The directory is not empty.");
            return 0; // unreachable
        }
        return 0;
    }
#if defined(_WIN32)
    const std::vector<wchar_t> wide = CsToWide(reinterpret_cast<Il2CppObject*>(path));
    if (wide.empty())
    {
        return 1;
    }
    return RemoveDirTreeWide(std::wstring(wide.data()));
#else
    return RemoveDirTreeUtf8(p);
#endif
}

//------------------------------------------------------------------------------
// Directory.GetFiles / GetDirectories -- FindFirstFileW/opendir enumeration
// (net48: the OS enumeration order, byte-exact when both sides enumerate the
// same directory contents on the same host -- p-nb2-dir). The result is a
// managed string[] of FULL PATHS (net48 returns path + name) named by the
// directory icalls in il2cpp_io.h.
//------------------------------------------------------------------------------
namespace {

#if defined(_WIN32)

// Enumerate 'dir' (recursively when 'recursive') into 'out'. 'wantDirs'
// selects the subdirectory set (GetDirectories) vs the file set
// (GetFiles). 'pattern' carries the wildcard filter applied to the FILE /
// DIRECTORY NAMES at every level (FindFirstFileW semantics -- '*' / '?').
// All matched entries are pushed as FULL PATHS (dir + '\\' + name). The
// recursive descent discovers subdirectories with the UNFILTERED "*" search
// (net48 descends into every subdirectory; the pattern only filters the
// returned names), so a "*.txt" pattern still reaches files inside a
// "sub" directory.
void CollectDirectoryEntriesW(const std::wstring& dir, const std::wstring& pattern,
                              bool recursive, bool wantDirs,
                              std::vector<std::wstring>& out)
{
    const std::wstring query = dir + L"\\" + pattern;
    WIN32_FIND_DATAW fd;
    const HANDLE hFind = ::FindFirstFileW(query.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE)
    {
        return; // missing/unopenable dir -> nothing collected (M3 empty array)
    }
    std::vector<std::wstring> subdirs;
    do
    {
        const std::wstring name = fd.cFileName;
        if (name == L"." || name == L"..")
        {
            continue;
        }
        const bool isDir = (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        if (wantDirs)
        {
            if (isDir)
            {
                out.push_back(dir + L"\\" + name);
            }
        }
        else if (!isDir)
        {
            out.push_back(dir + L"\\" + name);
        }
        if (isDir && recursive)
        {
            subdirs.push_back(dir + L"\\" + name);
        }
    } while (::FindNextFileW(hFind, &fd));
    ::FindClose(hFind);
    if (recursive)
    {
        for (const std::wstring& sub : subdirs)
        {
            CollectDirectoryEntriesW(sub, pattern, true, wantDirs, out);
        }
    }
}

#else  // !_WIN32

// UTF-8 wildcard match ('*' any run, '?' exactly one) -- the byte-level
// POSIX equivalent of the FindFirstFileW pattern semantics (UTF-8 payloads
// never contain the ASCII wildcard bytes).
bool WildcardMatch(const char* pat, const char* name)
{
    for (;;)
    {
        if (*pat == '\0')
        {
            return *name == '\0';
        }
        if (*pat == '*')
        {
            ++pat;
            if (*pat == '\0')
            {
                return true;
            }
            while (*name != '\0')
            {
                if (WildcardMatch(pat, name))
                {
                    return true;
                }
                ++name;
            }
            return false;
        }
        if (*name == '\0' || (*pat != '?' && *pat != *name))
        {
            return false;
        }
        ++pat;
        ++name;
    }
}

void CollectDirectoryEntriesU(const std::string& dir, const std::string& pattern,
                              bool recursive, bool wantDirs,
                              std::vector<std::string>& out)
{
    DIR* d = ::opendir(NativePosixPath(dir).c_str());
    if (d == nullptr)
    {
        return;
    }
    std::vector<std::string> subdirs;
    struct dirent* ent;
    while ((ent = ::readdir(d)) != nullptr)
    {
        const std::string name = ent->d_name;
        if (name == "." || name == "..")
        {
            continue;
        }
        const std::string child = NativePosixPath(dir + "/" + name);
        struct stat st;
        const bool isDir = (::lstat(child.c_str(), &st) == 0) && S_ISDIR(st.st_mode);
        const bool matched = WildcardMatch(pattern.c_str(), name.c_str());
        if (wantDirs)
        {
            if (isDir && matched)
            {
                out.push_back(NativePosixPath(dir + "/" + name));
            }
        }
        else if (!isDir && matched)
        {
            out.push_back(NativePosixPath(dir + "/" + name));
        }
        if (isDir && recursive)
        {
            subdirs.push_back(NativePosixPath(dir + "/" + name));
        }
    }
    ::closedir(d);
    if (recursive)
    {
        for (const std::string& sub : subdirs)
        {
            CollectDirectoryEntriesU(sub, pattern, true, wantDirs, out);
        }
    }
}

#endif

// Shared entry: decode the managed args, enumerate, build the managed
// string[] result (empty for a null/missing path -- M3, net48 throws
// DirectoryNotFoundException).
Il2CppObject* CollectDirectoryEntries(Il2CppObject* path, Il2CppObject* patternObj,
                                      int32_t searchOption, bool wantDirs)
{
    if (path == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(
            il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 0));
    }
    const bool recursive = searchOption == 1; // SearchOption.AllDirectories
#if defined(_WIN32)
    const std::vector<wchar_t> wide = CsToWide(path);
    if (wide.empty())
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(
            il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 0));
    }
    std::vector<std::wstring> names;
    std::wstring pattern = L"*";
    if (patternObj != nullptr)
    {
        const Il2CppString* ps = reinterpret_cast<Il2CppString*>(patternObj);
        pattern.assign(ps->chars, ps->chars + ps->length);
    }
    CollectDirectoryEntriesW(std::wstring(wide.data()), pattern, recursive, wantDirs, names);
    Il2CppArray* arr = il2cpp_array_new(il2cpp_string_class(),
        static_cast<int32_t>(sizeof(void*)), true, static_cast<int32_t>(names.size()));
    for (size_t i = 0; i < names.size(); ++i)
    {
        Il2CppString* s = reinterpret_cast<Il2CppString*>(
            il2cpp_string_new(reinterpret_cast<const char16_t*>(names[i].data()),
                              static_cast<int32_t>(names[i].size())));
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, static_cast<int32_t>(i)))
            = reinterpret_cast<Il2CppObject*>(s);
    }
    return reinterpret_cast<Il2CppObject*>(arr);
#else
    const std::string dir = CsToUtf8Z(path);
    if (dir.empty())
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_array_new(
            il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 0));
    }
    std::string pattern = "*";
    if (patternObj != nullptr)
    {
        pattern = CsToUtf8Z(patternObj);
    }
    std::vector<std::string> names;
    CollectDirectoryEntriesU(dir, pattern, recursive, wantDirs, names);
    Il2CppArray* arr = il2cpp_array_new(il2cpp_string_class(),
        static_cast<int32_t>(sizeof(void*)), true, static_cast<int32_t>(names.size()));
    for (size_t i = 0; i < names.size(); ++i)
    {
        Il2CppString* s = reinterpret_cast<Il2CppString*>(
            Utf8ToCs(names[i].data(), names[i].size()));
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, static_cast<int32_t>(i)))
            = reinterpret_cast<Il2CppObject*>(s);
    }
    return reinterpret_cast<Il2CppObject*>(arr);
#endif
}

} // namespace

Il2CppObject* icall_directory_get_files(Il2CppObject* path, Il2CppObject* pattern,
                                        int32_t searchOption)
{
    return CollectDirectoryEntries(path, pattern, searchOption, false);
}

Il2CppObject* icall_directory_get_directories(Il2CppObject* path, Il2CppObject* pattern,
                                              int32_t searchOption)
{
    return CollectDirectoryEntries(path, pattern, searchOption, true);
}

//------------------------------------------------------------------------------
// System.IO.FileInfo (object closure -- see il2cpp_io.h).
//------------------------------------------------------------------------------

void icall_fileinfo_ctor(Il2CppObject* self, Il2CppObject* path)
{
    Il2CppFileInfoObject* fi = reinterpret_cast<Il2CppFileInfoObject*>(self);
    fi->path = path; // the caller keeps the path reachable (GC-ref slot)
}

int64_t icall_fileinfo_get_length(Il2CppObject* self)
{
    const Il2CppFileInfoObject* fi = reinterpret_cast<Il2CppFileInfoObject*>(self);
    if (fi->path == nullptr)
    {
        return 0; // a null stored path is a ctor-layer contract (net48 throws
                  // ArgumentNullException at construction -- not transportable)
    }
    // net48 semantics (D-IOEXT-1): FileInfo.Length on a MISSING path or a
    // DIRECTORY path throws FileNotFoundException (both probed on net48).
    // Follows the File.Copy exception-chain pattern (ThrowFileOperationFailure):
    // a path that does not name a regular file is FileNotFoundException.
    if (!PathIsFile(CsToUtf8Z(fi->path)))
    {
        il2cpp_throw_file_not_found_exception("The system cannot find the file specified.");
    }
#if defined(_WIN32)
    // Wide stat: the narrow _stat reads the UTF-8 bytes through the ANSI
    // code page and mangles non-ASCII names (plat_path_is_file precedent).
    const std::vector<wchar_t> wide = CsToWide(fi->path);
    if (wide.empty())
    {
        return 0;
    }
    struct _stat st;
    return ::_wstat(wide.data(), &st) == 0 ? static_cast<int64_t>(st.st_size) : 0;
#else
    const std::string p = CsToUtf8Z(fi->path);
    if (p.empty())
    {
        return 0;
    }
    struct stat st;
    return ::stat(p.c_str(), &st) == 0 ? static_cast<int64_t>(st.st_size) : 0;
#endif
}

}} // namespace il2cpp::runtime
