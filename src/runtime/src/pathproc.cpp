//------------------------------------------------------------------------------
// pathproc.cpp -- System.IO.Path extensions + simplified System.Diagnostics.
// Process implementations (see pathproc.h for the contract).
//
// The Path extensions are split by nature:
//   * get_temp_path / get_full_path delegate the OS query to the platform
//     layer (plat_path_tmpdir / plat_path_full -- platform.h): the layer owns
//     GetTempPathW / GetFullPathNameW on Windows and $TMPDIR (else /tmp) /
//     realpath() on POSIX, and always speaks UTF-8 across the interface (the
//     wchar_t CRT stays inside the layer). The business layer bridges the
//     managed UTF-16 payload <-> UTF-8 and restores the trailing separator
//     Path.GetTempPath / GetFullPath guarantee. On POSIX, get_full_path keeps
//     a handwritten lexical fallback for non-existent paths (realpath
//     requires existence; the platform layer documents this gap).
//   * change_extension / has_extension are pure string logic shared by both
//     platforms; their semantics were pinned against a net48 reference
//     probe (Path.ChangeExtension / Path.HasExtension on .NET Framework
//     4.8, 2026-08-03 -- the cases the tests assert are exactly the probed
//     outputs, including the odd ones: "file." + null -> "file",
//     ".gitignore" + "txt" -> ".txt", "a\b.txt\" + "c" -> "a\b.txt\.c").
//
// The Process domain wraps CreateProcessW (simplified: exe path + command
// line, CREATE_NO_WINDOW) and the two id queries (the process id delegates
// to plat_process_id, the thread id to plat_thread_current_id), and maps
// process_exit to ExitProcess. A started child is tracked: its
// stdout/stderr are redirected into anonymous pipes (CreatePipe, nb7
// PROC-STARTINFO) drained by read_stdout_to_end / read_stderr_to_end and its
// process handle lives in a PID-keyed table until wait_for_exit caches the
// exit code and closes it. The POSIX branch
// (WSL/GCC baseline) keeps the sources compiling with best-effort semantics
// (process_start returns non-zero; see the header).
//------------------------------------------------------------------------------

#include "pathproc.h"

#include "il2cpp_exception.h" // il2cpp_throw_new (GetDirectoryName ArgumentException)
#include "il2cpp_exception_ext.h" // il2cpp_throw_invalid_operation_exception (POSTEXIT IOE)
#include "il2cpp_gcroot.h"        // GcRoot (ProcessStartInfo ctor/setter rooting)
#include "il2cpp_metadata.h"      // Il2CppClass / System_Object_class (ProcessStartInfo descriptor)
#include "il2cpp_runtime.h"       // il2cpp_class_register / il2cpp_object_new
#include "il2cpp_string.h"
#include "platform.h" // plat_path_tmpdir / plat_path_full / plat_process_id / plat_thread_current_id

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <string>
#include <unordered_map>
#include <vector>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
    #include <psapi.h> // GetProcessMemoryInfo (Process.WorkingSet64, nb4 PROC-DIAG)
#else
    #include <limits.h>   // PATH_MAX (fallback guard in the anonymous namespace)
    #include <unistd.h>   // getcwd
#endif

//------------------------------------------------------------------------------
// nb7 PROC-STARTINFO: the System.Diagnostics.ProcessStartInfo GLOBAL descriptor
// (the regex_obj.cpp / sync_lite.cpp contract). Layout: the GC reference fields
// (fileName slot 1, arguments slot 2) -> refFieldBitmap 0x03 (bits 0/1), 2 refs;
// the object header is the first member so the bitmap is pointer-size
// independent. Registered lazily by the ctors; the transpiler's newobj fallback
// emits il2cpp_object_new(&System_Diagnostics_ProcessStartInfo_class).
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_Diagnostics_ProcessStartInfo_class = {
    "System.Diagnostics.ProcessStartInfo",
    static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppProcessStartInfo)),
    0x03u,
    2,
    &System_Object_class,
    nullptr,
    0,
    nullptr,
    0,
};

namespace il2cpp { namespace runtime {

namespace {

#if defined(_WIN32)

// Managed string -> NUL-terminated wchar_t buffer (wchar_t is 16-bit on
// Windows, so this is a straight copy of the UTF-16 payload).
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

// Opaque byte buffer (a drained pipe; length excludes any terminator) ->
// managed string, 1:1 byte pass-through like the POSIX CsFromBytes below.
// The capture is binary-opaque data (see read_stdout_to_end); ASCII output
// round-trips, higher bytes do not.
Il2CppObject* CsFromBytes(const char* data, size_t length)
{
    std::vector<char16_t> out;
    out.reserve(length);
    for (size_t i = 0; i < length; ++i)
    {
        out.push_back(static_cast<char16_t>(static_cast<unsigned char>(data[i])));
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

// One tracked child process. The slot exists from start until the redirected
// stream(s) reach EOF or process exit, so the exit code stays queryable after
// wait_for_exit. Not thread-safe, like the rest of this simplified domain.
struct ProcessSlot
{
    HANDLE process;            // closed by wait_for_exit; null afterwards
    // nb7 PROC-STARTINFO: the anonymous-pipe READ ends of the redirected
    // streams (INVALID_HANDLE_VALUE = not redirected). Drained by
    // read_stdout_to_end / read_stderr_to_end (closed at EOF). A stream that
    // was not redirected keeps a null placeholder and its ReadToEnd throws.
    HANDLE stdoutRead;
    HANDLE stderrRead;
    // nb8 STDIN-WRITE: the anonymous-pipe WRITE end feeding the child's stdin
    // (INVALID_HANDLE_VALUE = not redirected). Closed by close_stdin (EOF).
    HANDLE stdinWrite;
    bool stdoutRedirected;
    bool stderrRedirected;
    bool stdinRedirected;
    bool stdoutDrained;
    bool stderrDrained;
    bool stdinClosed;
    uint32_t exitCode;
    bool waited;
    // nb4 PROC-DIAG (双审 defect 4): the net48 first-read WorkingSet64 cache.
    // The first successful reading during the process lifetime is cached; a
    // post-exit read on the SAME process object returns the cached value
    // instead of throwing (net48 semantics, probe-pinned).
    bool wsCached;
    int64_t wsCache;
    // nb4 PROC-DIAG proctime: the creation/exit times (DateTime ticks) are
    // captured while the slot still owns a live process handle. wait_for_exit
    // closes that handle, and re-opening the PID after the child object is
    // recycled fails, so a post-exit StartTime / ExitTime read would silently
    // degrade to a zero DateTime. Same lifetime rule as exitCode (net48 keeps
    // the retained SafeProcessHandle and stays exact).
    bool timesCached;
    int64_t startTicks;
    int64_t exitTicks;
};

std::unordered_map<int32_t, ProcessSlot>& g_processes()
{
    static std::unordered_map<int32_t, ProcessSlot> table;
    return table;
}

// The FILETIME <-> DateTime ticks epoch shift: a FILETIME counts 100 ns
// intervals since 1601-01-01 UTC; DateTime ticks count since 0001-01-01.
constexpr int64_t kFileTimeToTicks = 116444736000000000LL;

// FILETIME (1601 epoch, 100 ns) -> DateTime ticks (0001 epoch, 100 ns).
int64_t FileTimeToDateTimeTicks(const FILETIME& ft)
{
    const uint64_t raw = (static_cast<uint64_t>(ft.dwHighDateTime) << 32)
                       | static_cast<uint64_t>(ft.dwLowDateTime);
    return static_cast<int64_t>(raw) + kFileTimeToTicks;
}

// Capture the process creation / exit times while the slot still owns a valid
// handle. This is the ONE authoritative point (called just before CloseHandle
// in wait_for_exit and the Kill fold): afterwards the PID-only fallback may
// no longer resolve the recycled child object. A GetProcessTimes failure
// leaves any previously cached values untouched (the getters then keep their
// documented zero/fallback behavior).
void CacheProcessTimes(ProcessSlot& slot)
{
    if (slot.process == nullptr)
    {
        return;
    }
    FILETIME ct, et, kt, ut;
    if (::GetProcessTimes(slot.process, &ct, &et, &kt, &ut) == 0)
    {
        return;
    }
    slot.startTicks = FileTimeToDateTimeTicks(ct);
    slot.exitTicks = FileTimeToDateTimeTicks(et);
    slot.timesCached = true;
}

#endif // _WIN32

// True when 'c' terminates the file-name segment (extension scanning). The
// volume separator is a segment terminator on Windows only -- the POSIX
// branch keeps ':' an ordinary filename character (both separators are
// recognized on both platforms, mirroring il2cpp_path_has_extension).
bool IsSegmentTerminator(char16_t c)
{
#if defined(_WIN32)
    return c == u'\\' || c == u'/' || c == u':';
#else
    return c == u'\\' || c == u'/';
#endif
}

// Managed string -> UTF-8 bytes (surrogate pairs become 4-byte sequences;
// lone surrogates encode as 3-byte, best effort). The platform layer speaks
// UTF-8 (platform.h contract); this is the reverse bridge for the path
// functions below (the Windows/wide CRT path never crosses here).
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

// The native path separator ('\\' on Windows, '/' everywhere else). The
// platform layer returns paths WITHOUT a trailing separator (platform.h
// contract), and Path.GetTempPath / GetFullPath guarantee one -- restored
// here with the platform-native character.
char NativeSeparator()
{
#if defined(_WIN32)
    return '\\';
#else
    return '/';
#endif
}

// UTF-8 bytes -> managed string (il2cpp_string_new_from_utf8: full UTF-8
// decoding with the net48 replacement fallback). Restores a trailing
// separator the input carried (.NET Path.GetFullPath keeps it) -- used only
// where the platform primitive strips one (realpath on POSIX); Windows
// GetFullPathNameW preserves it itself, so the Windows caller passes false.
Il2CppObject* CsFromUtf8(const char* data, bool trailingSeparator)
{
    const size_t len = std::strlen(data);
    std::string utf8(data, len);
    if (trailingSeparator && len > 1 && data[len - 1] != '/'
        && data[len - 1] != '\\')
    {
        utf8.push_back(NativeSeparator());
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_utf8(utf8.c_str()));
}

// net48 GetFullPath applies a normalization the OS resolvers lack: a path
// segment that is ALL dots of length >= 2 ("...", "....", "......", ...) is
// a SINGLE parent reference, not a literal name (probed:
// GetFullPath("a\\......\\b") pops exactly one level). GetFullPathNameW
// keeps such segments literal, so the UTF-8 input is pre-collapsed
// ("...." -> "..") before the platform call -- the OS then collapses the
// resulting ".." the same way .NET does. No-op for realistic paths (no
// all-dot segments). On POSIX '\\' is an ordinary filename character, so
// only '/' separates there (mirroring IsSegmentTerminator).
void CollapseDotRuns(std::string& s)
{
    std::string out;
    out.reserve(s.size());
    size_t i = 0;
    const size_t n = s.size();
    while (i < n)
    {
        size_t j = i;
        while (j < n && (s[j] == '/'
#if defined(_WIN32)
                         || s[j] == '\\'
#endif
                        ))
        {
            ++j;
        }
        out.append(s, i, j - i); // separator run, verbatim
        i = j;
        const size_t start = i;
        while (i < n && s[i] != '/'
#if defined(_WIN32)
               && s[i] != '\\'
#endif
              )
        {
            ++i;
        }
        const size_t len = i - start;
        if (len >= 2)
        {
            bool allDots = true;
            for (size_t k = 0; k < len; ++k)
            {
                if (s[start + k] != '.')
                {
                    allDots = false;
                    break;
                }
            }
            if (allDots)
            {
                out.append("..");
                continue;
            }
        }
        out.append(s, start, len);
    }
    s = std::move(out);
}

#if !defined(_WIN32)

// Conservative path buffer size: POSIX does not require PATH_MAX to be
// defined (Linux glibc and Android Bionic both define it, in <limits.h>);
// fall back to the Linux value when absent.
#ifndef PATH_MAX
    static const size_t kPathMax = 4096;
#else
    static const size_t kPathMax = PATH_MAX;
#endif

// Lexical normalization of an absolute NUL-terminated byte path: collapses
// duplicate separators, drops "." segments and resolves ".." against the
// accumulated segments (the root cannot be popped). No filesystem access --
// this is the get_full_path fallback for paths realpath() rejects because
// a component does not exist (see il2cpp_path_get_full_path).
void NormalizeAbsolutePath(std::vector<char>& path)
{
    const size_t total = path.size() - 1; // excludes the NUL
    std::vector<std::vector<char>> segments;
    size_t i = 0;
    while (i < total)
    {
        while (i < total && path[i] == '/')
        {
            ++i;
        }
        const size_t start = i;
        while (i < total && path[i] != '/')
        {
            ++i;
        }
        if (i == start)
        {
            continue;
        }
        std::vector<char> segment(path.begin() + static_cast<ptrdiff_t>(start),
                                  path.begin() + static_cast<ptrdiff_t>(i));
        if (segment.size() == 1 && segment[0] == '.')
        {
            continue; // "." collapses
        }
        if (segment.size() == 2 && segment[0] == '.' && segment[1] == '.')
        {
            if (!segments.empty())
            {
                segments.pop_back(); // ".." pops the previous segment
            }
            continue;
        }
        // net48 dot-run rule: a segment that is ALL dots of length >= 2 is a
        // single parent reference (probed: GetFullPath("a\\......\\b") pops one
        // level). "..." / "...." collapse like "..", never two levels.
        bool allDots = true;
        for (char c : segment)
        {
            if (c != '.')
            {
                allDots = false;
                break;
            }
        }
        if (allDots && segment.size() >= 2)
        {
            if (!segments.empty())
            {
                segments.pop_back();
            }
            continue;
        }
        segments.push_back(std::move(segment));
    }
    path.clear();
    path.push_back('/');
    for (size_t k = 0; k < segments.size(); ++k)
    {
        if (k != 0)
        {
            path.push_back('/');
        }
        path.insert(path.end(), segments[k].begin(), segments[k].end());
    }
    path.push_back('\0');
}

// NUL-terminated byte path -> managed string (1:1 byte pass-through; the
// payload may not be ASCII). Restores a trailing separator the input
// carried (.NET Path.GetFullPath keeps it), except at the root.
Il2CppObject* CsFromBytes(const char* data, bool trailingSeparator)
{
    const size_t len = std::strlen(data);
    std::vector<char16_t> out;
    out.reserve(len + 1);
    for (size_t i = 0; i < len; ++i)
    {
        out.push_back(static_cast<char16_t>(static_cast<unsigned char>(data[i])));
    }
    if (trailingSeparator && len > 1 && data[len - 1] != '/')
    {
        out.push_back(u'/');
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

#endif // !_WIN32

} // namespace

//------------------------------------------------------------------------------
// System.IO.Path (extensions).
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_path_get_temp_path()
{
    // The platform layer owns the OS query (GetTempPathW on Windows, $TMPDIR
    // else /tmp on POSIX -- platform.h) and returns UTF-8 WITHOUT a trailing
    // separator (both implementations strip it so the result joins like
    // "C:\Temp" / "/tmp"). Path.GetTempPath guarantees one, so it is restored
    // here with the platform-native separator.
    const char* base = plat_path_tmpdir();
    if (base == nullptr || base[0] == '\0')
    {
        return nullptr;
    }
    std::string result(base);
    if (result.back() != '/' && result.back() != '\\')
    {
        result.push_back(NativeSeparator());
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_utf8(result.c_str()));
}

Il2CppObject* il2cpp_path_get_full_path(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const Il2CppString* p = reinterpret_cast<Il2CppString*>(path);
    if (p->length == 0)
    {
        return nullptr; // contract: empty input is rejected, like the Win32 branch
    }
#if !defined(_WIN32)
    // realpath() strips a trailing separator; Path.GetFullPath keeps it.
    const bool hadTrailingSeparator = p->chars[p->length - 1] == u'/';
#endif

    // Managed payload -> UTF-8 (the platform layer speaks UTF-8 only --
    // platform.h contract; the wchar_t CRT stays inside the layer).
    std::string utf8 = CsToUtf8(path);

#if defined(_WIN32)
    // net48 rejects a path whose ':' is not the drive separator at index 1
    // ("C:\x:y.txt" -> NotSupportedException; "a:b" -> "A:\b" is allowed).
    // The runtime maps managed throws to null (no exception system yet).
    for (size_t k = 0; k < utf8.size(); ++k)
    {
        if (utf8[k] == ':' && k != 1)
        {
            return nullptr;
        }
    }
#endif

    // net48 dot-run rule: pre-collapse all-dot segments ("...." -> "..")
    // so GetFullPathNameW/realpath reproduce Path.GetFullPath exactly (see
    // CollapseDotRuns). No-op for realistic paths.
    CollapseDotRuns(utf8);

    // Primary resolution through the platform layer: GetFullPathNameW on
    // Windows (no filesystem access, so non-existent paths normalize too),
    // realpath() on POSIX (canonical absolute form -- requires every
    // component to exist). Both write UTF-8 into 'resolved'.
    std::string resolved;
    if (plat_path_full(utf8.c_str(), resolved))
    {
#if defined(_WIN32)
        // GetFullPathNameW preserves a trailing separator itself.
        return CsFromUtf8(resolved.c_str(), false);
#else
        // realpath() strips it; restore what the input carried.
        return CsFromUtf8(resolved.c_str(), hadTrailingSeparator);
#endif
    }
#if !defined(_WIN32)
    // POSIX: realpath() failed -- the path does not exist. .NET
    // Path.GetFullPath does NOT require existence, so the handwritten
    // absolutization below restores that behavior: cwd join for relative
    // input + lexical "."/".." collapsing (conservative: no symlink
    // resolution, no filesystem access). On Windows GetFullPathNameW never
    // fails for a non-empty path, so this branch is unreachable there.
    std::vector<char> joined;
    if (utf8.empty() || utf8[0] == '/')
    {
        joined.assign(utf8.begin(), utf8.end());
    }
    else
    {
        char cwdBuf[kPathMax];
        if (::getcwd(cwdBuf, sizeof(cwdBuf)) == nullptr)
        {
            return nullptr; // no current directory -- cannot absolutize
        }
        joined.assign(cwdBuf, cwdBuf + std::strlen(cwdBuf));
        if (joined.back() != '/')
        {
            joined.push_back('/');
        }
        joined.insert(joined.end(), utf8.begin(), utf8.end());
    }
    joined.push_back('\0');
    NormalizeAbsolutePath(joined);
    return CsFromBytes(joined.data(), hadTrailingSeparator);
#else
    // GetFullPathNameW failure (effectively never for a non-empty path).
    return nullptr;
#endif
}

Il2CppObject* il2cpp_path_change_extension(Il2CppObject* path, Il2CppObject* extension)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const Il2CppString* p = reinterpret_cast<Il2CppString*>(path);
    // Find the extension split: the first '.' scanning backwards; a
    // segment terminator before any dot means the path carries no
    // extension (net48 semantics -- "a\b.txt\" + "c" appends to the whole
    // path: "a\b.txt\.c").
    int32_t split = -1;
    for (int32_t i = p->length - 1; i >= 0; --i)
    {
        const char16_t c = p->chars[i];
        if (c == u'.')
        {
            split = i;
            break;
        }
        if (IsSegmentTerminator(c))
        {
            break;
        }
    }
    std::vector<char16_t> out;
    out.reserve(static_cast<size_t>(p->length) + 4);
    out.assign(p->chars, p->chars + (split >= 0 ? split : p->length));
    if (extension != nullptr)
    {
        const Il2CppString* e = reinterpret_cast<Il2CppString*>(extension);
        if (e->length == 0)
        {
            out.push_back(u'.'); // empty extension -> trailing dot ("a." )
        }
        else
        {
            if (e->chars[0] != u'.')
            {
                out.push_back(u'.'); // "b" -> ".b"
            }
            out.insert(out.end(), e->chars, e->chars + e->length);
        }
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

int32_t il2cpp_path_has_extension(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return 0;
    }
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(path);
    // net48 scan rule: a '.' after the last separator that is not the final
    // character is an extension. Notably ".git" -> 1 (differs from the
    // dotfile rule io.cpp applies to GetExtension) and "file." -> 0.
    for (int32_t i = s->length - 1; i >= 0; --i)
    {
        const char16_t c = s->chars[i];
        if (c == u'\\' || c == u'/')
        {
            return 0;
        }
        if (c == u'.')
        {
            return (i != s->length - 1) ? 1 : 0;
        }
    }
    return 0;
}

Il2CppObject* il2cpp_path_get_directory_name(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(path);
    // net48 boundary (probe-pinned): a null path -> null; "" ->
    // ArgumentException -- THROWN through the managed exception transport.
    if (s->length == 0)
    {
        il2cpp_throw_new(
            "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception",
            "Path cannot be the empty string or all whitespace.");
    }
    // net48 GetDirectoryName (probe-pinned matrix):
    //   * a TRAILING separator run means the path names a directory: the
    //     result is the path minus the trailing run ("a\\b\\" -> "a\\b",
    //     "plain\\" -> "plain"). A path that is ALL separators ("\\") is the
    //     root: GetDirectoryName -> null.
    int32_t end = s->length;
    int32_t skipped = 0;
    while (end > 0 && (s->chars[end - 1] == u'\\' || s->chars[end - 1] == u'/'))
    {
        --end;
        ++skipped;
    }
    if (end == 0)
    {
        return nullptr; // the root ("\\" / "\\\\") -> null
    }
    // D2 (nb3): a DRIVE ROOT ("C:\" -- the trailing separator stripped so
    // the remainder is the bare volume) and a BARE DRIVE ("C:") are both
    // ROOTS: net48 returns null for them (probe-pinned [d2.root]/[d2.bare] --
    // a root has no directory portion). The old code returned "C:" for the
    // drive root and "" for the bare drive. "C:a" (a drive-relative path
    // with content) is NOT a root and keeps the normal splitting below.
    if (end == 2 && s->chars[1] == u':'
        && ((s->chars[0] >= u'A' && s->chars[0] <= u'Z')
            || (s->chars[0] >= u'a' && s->chars[0] <= u'z')))
    {
        return nullptr;
    }
    if (skipped > 0)
    {
        return reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new(s->chars, end));
    }
    //   * no trailing separator: the result is the path before the LAST
    //     separator; no separator -> "" ("plain"); a separator-led path's
    //     parent is the root separator ("\\a" -> "\\"); a drive volume keeps
    //     its separator ("C:\\a" -> "C:\\").
    int32_t lastSep = -1;
    for (int32_t i = end - 1; i >= 0; --i)
    {
        const char16_t c = s->chars[i];
        if (c == u'\\' || c == u'/')
        {
            lastSep = i;
            break;
        }
    }
    if (lastSep < 0)
    {
        return reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new(nullptr, 0));
    }
    if (lastSep == 0)
    {
        std::vector<char16_t> root(1, u'\\');
        return reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new(root.data(), 1));
    }
    std::vector<char16_t> out;
    const size_t take = (lastSep >= 2 && s->chars[lastSep - 1] == u':')
        ? static_cast<size_t>(lastSep) + 1
        : static_cast<size_t>(lastSep);
    out.assign(s->chars, s->chars + take);
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(out.data(), static_cast<int32_t>(out.size())));
}

Il2CppObject* il2cpp_path_get_file_name_without_extension(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return nullptr;
    }
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(path);
    // Locate the last segment (after the final separator). A TRAILING
    // separator leaves an empty name (net48: "a\\" -> ""); otherwise the
    // segment runs to the end of the path.
    int32_t segStart = 0;
    bool trailingSep = false;
    for (int32_t i = s->length - 1; i >= 0; --i)
    {
        const char16_t c = s->chars[i];
        if (c == u'\\' || c == u'/')
        {
            segStart = i + 1;
            trailingSep = (i == s->length - 1);
            break;
        }
    }
    if (trailingSep)
    {
        return reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new(nullptr, 0));
    }
    // Extension cut mirrors GetExtension (io.cpp): the LAST '.' in the
    // segment is the extension start unless it is the final character (a
    // trailing dot is NOT an extension; a LEADING dot IS -- ".git" -> "").
    int32_t cut = s->length - segStart; // default: no extension
    for (int32_t i = segStart; i < s->length; ++i)
    {
        if (s->chars[i] == u'.' && i != s->length - 1)
        {
            cut = i - segStart;
        }
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(s->chars + segStart, cut));
}

//------------------------------------------------------------------------------
// System.Diagnostics.Process (simplified).
//------------------------------------------------------------------------------

#if defined(_WIN32)

// nb7 PROC-STARTINFO: the shared child launcher. The executable is passed
// inside the command line, quoted, with lpApplicationName left null:
// lpApplicationName without a path does not always resolve (probe: "cmd.exe"
// -> ERROR_FILE_NOT_FOUND), while the null-app command-line form applies the
// standard executable search (application dir, current dir, system dirs,
// PATH) -- the same resolution .NET Process.Start relies on. lpCommandLine
// must be writable (CreateProcessW rewrites it in place). CsToWide appends a
// NUL terminator: it is dropped here so the composed line stays a single
// NUL-terminated string (an embedded NUL truncates the command line --
// cmd.exe then starts interactively and prints its banner).
//
// Redirected streams use anonymous pipes (CreatePipe): a redirected OUTPUT
// stream makes its READ end parent-only, a redirected INPUT stream (nb8
// STDIN-WRITE) makes its WRITE end parent-only; SetHandleInformation achieves
// both. A NON-redirected stream is bound to NUL: the child can never write
// through to the parent's captured stdout/stderr (the diff harness would
// otherwise observe output the net48 oracle cannot) -- a documented deviation
// from net48's console inheritance.
// The process is ALWAYS started with CREATE_NO_WINDOW: the ProcessStartInfo
// CreateNoWindow flag is stored/observable but deliberately not consulted
// (nb8 documented deviation -- see pathproc.h). Returns the child PID (> 0)
// and stores the tracked slot; 0 on failure.
static int32_t LaunchChild(const std::vector<wchar_t>& app, const std::vector<wchar_t>& argLine,
                           bool redirectOut, bool redirectErr, bool redirectIn)
{
    std::vector<wchar_t> cmd;
    cmd.reserve(app.size() + argLine.size() + 4);
    cmd.push_back(L'"');
    cmd.insert(cmd.end(), app.begin(), app.end() - 1);
    cmd.push_back(L'"');
    cmd.push_back(L' ');
    cmd.insert(cmd.end(), argLine.begin(), argLine.end() - 1);
    cmd.push_back(L'\0');

    SECURITY_ATTRIBUTES sa;
    sa.nLength = sizeof(sa);
    sa.lpSecurityDescriptor = nullptr;
    sa.bInheritHandle = TRUE; // the child must inherit the write/NUL handles

    HANDLE outRead = INVALID_HANDLE_VALUE;
    HANDLE outWrite = INVALID_HANDLE_VALUE;
    HANDLE errRead = INVALID_HANDLE_VALUE;
    HANDLE errWrite = INVALID_HANDLE_VALUE;
    HANDLE inRead = INVALID_HANDLE_VALUE;
    HANDLE inWrite = INVALID_HANDLE_VALUE;
    if (redirectOut && ::CreatePipe(&outRead, &outWrite, &sa, 0))
    {
        ::SetHandleInformation(outRead, HANDLE_FLAG_INHERIT, 0); // parent-only read end
    }
    else
    {
        redirectOut = false;
    }
    if (redirectErr && ::CreatePipe(&errRead, &errWrite, &sa, 0))
    {
        ::SetHandleInformation(errRead, HANDLE_FLAG_INHERIT, 0);
    }
    else
    {
        redirectErr = false;
    }
    // nb8 STDIN-WRITE: the WRITE end stays parent-only (the child inherits only
    // the READ end as its stdin).
    if (redirectIn && ::CreatePipe(&inRead, &inWrite, &sa, 0))
    {
        ::SetHandleInformation(inWrite, HANDLE_FLAG_INHERIT, 0); // parent-only write end
    }
    else
    {
        redirectIn = false;
    }
    HANDLE hIn = redirectIn ? inRead
        : ::CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        &sa, OPEN_EXISTING, 0, nullptr);
    HANDLE hOut = redirectOut ? outWrite
        : ::CreateFileW(L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        &sa, OPEN_EXISTING, 0, nullptr);
    HANDLE hErr = redirectErr ? errWrite
        : ::CreateFileW(L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        &sa, OPEN_EXISTING, 0, nullptr);
    if (hIn == INVALID_HANDLE_VALUE || hOut == INVALID_HANDLE_VALUE || hErr == INVALID_HANDLE_VALUE)
    {
        // A NUL fallback could not be opened: fail the launch, close everything.
        if (outWrite != INVALID_HANDLE_VALUE) ::CloseHandle(outWrite);
        if (errWrite != INVALID_HANDLE_VALUE) ::CloseHandle(errWrite);
        if (inWrite != INVALID_HANDLE_VALUE) ::CloseHandle(inWrite);
        if (hIn != INVALID_HANDLE_VALUE) ::CloseHandle(hIn);
        if (!redirectOut && hOut != INVALID_HANDLE_VALUE) ::CloseHandle(hOut);
        if (!redirectErr && hErr != INVALID_HANDLE_VALUE) ::CloseHandle(hErr);
        if (outRead != INVALID_HANDLE_VALUE) ::CloseHandle(outRead);
        if (errRead != INVALID_HANDLE_VALUE) ::CloseHandle(errRead);
        if (inRead != INVALID_HANDLE_VALUE) ::CloseHandle(inRead);
        return 0;
    }

    STARTUPINFOW si;
    std::memset(&si, 0, sizeof(si));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdInput = hIn;
    si.hStdOutput = hOut;
    si.hStdError = hErr;
    PROCESS_INFORMATION pi;
    std::memset(&pi, 0, sizeof(pi));
    const BOOL created = ::CreateProcessW(nullptr, cmd.data(), nullptr, nullptr,
                                          TRUE /* inherit the pipe write / NUL handles */,
                                          CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
    // Parent-side duplicates: the child keeps its own inherited copies. Close
    // the child-side write/read ends (EOF depends on this) and every NUL
    // fallback handle. The parent KEEPS the stdin WRITE end (inWrite) -- the
    // child-side stdin READ end (inRead) is closed here.
    if (outWrite != INVALID_HANDLE_VALUE) ::CloseHandle(outWrite);
    if (errWrite != INVALID_HANDLE_VALUE) ::CloseHandle(errWrite);
    if (!redirectOut && hOut != INVALID_HANDLE_VALUE) ::CloseHandle(hOut);
    if (!redirectErr && hErr != INVALID_HANDLE_VALUE) ::CloseHandle(hErr);
    if (redirectIn) ::CloseHandle(inRead);
    else ::CloseHandle(hIn);
    if (created == 0)
    {
        if (outRead != INVALID_HANDLE_VALUE) ::CloseHandle(outRead);
        if (errRead != INVALID_HANDLE_VALUE) ::CloseHandle(errRead);
        if (inWrite != INVALID_HANDLE_VALUE) ::CloseHandle(inWrite);
        return 0; // launch failure (missing exe, bad command line, ...); header contract: 0
    }
    // Tracked start: the process handle lives in the table until wait_for_exit;
    // the child PID identifies the slot.
    const int32_t pid = static_cast<int32_t>(::GetProcessId(pi.hProcess));
    ::CloseHandle(pi.hThread);
    ProcessSlot slot;
    slot.process = pi.hProcess;
    slot.stdoutRead = redirectOut ? outRead : INVALID_HANDLE_VALUE;
    slot.stderrRead = redirectErr ? errRead : INVALID_HANDLE_VALUE;
    slot.stdinWrite = redirectIn ? inWrite : INVALID_HANDLE_VALUE;
    slot.stdoutRedirected = redirectOut;
    slot.stderrRedirected = redirectErr;
    slot.stdinRedirected = redirectIn;
    slot.stdoutDrained = !redirectOut;
    slot.stderrDrained = !redirectErr;
    slot.stdinClosed = !redirectIn;
    slot.exitCode = 0;
    slot.waited = false;
    slot.wsCached = false;
    slot.wsCache = 0;
    slot.timesCached = false;
    slot.startTicks = 0;
    slot.exitTicks = 0;
    g_processes()[pid] = std::move(slot);
    return pid;
}

#endif // _WIN32

int32_t il2cpp_process_start(Il2CppObject* exePath, Il2CppObject* args)
{
#if defined(_WIN32)
    if (exePath == nullptr || args == nullptr)
    {
        return -1;
    }
    // The static Start(string, string) redirects BOTH streams (the captured
    // output is only observable through the ProcessStartInfo redirect form;
    // net48's ShellExecute static Start does not redirect at all).
    const std::vector<wchar_t> app = CsToWide(exePath);
    const std::vector<wchar_t> argLine = CsToWide(args);
    return LaunchChild(app, argLine, true, true, false);
#else
    (void)exePath;
    (void)args;
    return -1; // fork/exec is beyond this simplified surface
#endif
}

// nb8 PROC-SECURE: the SecureString Start overloads (pathproc.h contract).
namespace {

// The net48 UserName/Domain getters normalize null to ""; an EMPTY UserName
// selects the current-credential launch path (CreateProcessW), a non-empty one
// the CreateProcessWithLogonW credential path. The credential path is reported
// as a Win32Exception (the SecureString type is not materialized and credential
// launch is OS/credential-state dependent -- documented deviation).
void ThrowCredentialLaunch()
{
    il2cpp_throw_new(
        "System.ComponentModel.Win32Exception,System.ComponentModel.Win32Exception,"
        "System.ComponentModel.ExternalException,System.SystemException,System.Exception",
        "Logon failure: unknown user name or bad password.");
}

} // namespace

int32_t il2cpp_process_start_secure_4(Il2CppObject* exePath, Il2CppObject* userName,
                                      Il2CppObject* password, Il2CppObject* domain)
{
    (void)password;
    (void)domain;
    if (exePath == nullptr)
    {
        return -1;
    }
    if (userName != nullptr)
    {
        ThrowCredentialLaunch();
    }
    return il2cpp_process_start(exePath, reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8("")));
}

int32_t il2cpp_process_start_secure_5(Il2CppObject* exePath, Il2CppObject* args,
                                      Il2CppObject* userName, Il2CppObject* password,
                                      Il2CppObject* domain)
{
    (void)password;
    (void)domain;
    if (exePath == nullptr)
    {
        return -1;
    }
    if (userName != nullptr)
    {
        ThrowCredentialLaunch();
    }
    return il2cpp_process_start(exePath,
        args != nullptr ? args : reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8("")));
}

// nb7 PROC-STARTINFO: Process.Start(ProcessStartInfo). See pathproc.h for the
// contract (redirect pipes + the net48 UseShellExecute/redirect guard).
int32_t il2cpp_process_start_info(Il2CppObject* psi)
{
#if defined(_WIN32)
    if (psi == nullptr)
    {
        return -1;
    }
    const Il2CppProcessStartInfo* info = reinterpret_cast<const Il2CppProcessStartInfo*>(psi);
    const bool redirectOut = info->redirectStandardOutput != 0;
    const bool redirectErr = info->redirectStandardError != 0;
    const bool redirectIn = info->redirectStandardInput != 0;
    if (info->useShellExecute != 0 && (redirectOut || redirectErr || redirectIn))
    {
        // net48 (probe-pinned): "The Process object must have the
        // UseShellExecute property set to false in order to redirect IO
        // streams." -- raised through the managed transport.
        il2cpp_throw_invalid_operation_exception(
            "The Process object must have the UseShellExecute property set to false in order to redirect IO streams.");
    }
    if (info->fileName == nullptr)
    {
        return -1;
    }
    const std::vector<wchar_t> app = CsToWide(reinterpret_cast<Il2CppObject*>(info->fileName));
    const std::vector<wchar_t> argLine = info->arguments != nullptr
        ? CsToWide(reinterpret_cast<Il2CppObject*>(info->arguments))
        : std::vector<wchar_t>(1, L'\0');
    return LaunchChild(app, argLine, redirectOut, redirectErr, redirectIn);
#else
    (void)psi;
    return -1; // fork/exec is beyond this simplified surface
#endif
}

int32_t il2cpp_process_wait_for_exit(int32_t pid)
{
#if defined(_WIN32)
    auto it = g_processes().find(pid);
    if (it == g_processes().end())
    {
        return 1; // unknown PID
    }
    ProcessSlot& slot = it->second;
    if (!slot.waited)
    {
        ::WaitForSingleObject(slot.process, INFINITE);
        DWORD code = 0;
        if (::GetExitCodeProcess(slot.process, &code) == 0)
        {
            code = static_cast<DWORD>(-1); // no exit code available
        }
        slot.exitCode = code;
        // The authoritative proctime capture: the handle is still open here.
        CacheProcessTimes(slot);
        ::CloseHandle(slot.process);
        slot.process = nullptr;
        slot.waited = true;
    }
    return 0; // idempotent: already-waited slots succeed too
#else
    (void)pid;
    return -1; // unsupported on POSIX
#endif
}

int32_t il2cpp_process_get_exit_code(int32_t pid)
{
#if defined(_WIN32)
    const auto it = g_processes().find(pid);
    if (it == g_processes().end() || !it->second.waited)
    {
        return -1; // unknown PID or not waited on yet
    }
    return static_cast<int32_t>(it->second.exitCode);
#else
    (void)pid;
    return -1; // unsupported on POSIX
#endif
}

#if defined(_WIN32)

// nb7 PROC-STARTINFO: drain one redirected stream's anonymous pipe to EOF and
// return the full captured text. The drain is BLOCKING (see pathproc.h for the
// documented single-stream simplification). A stream that was not redirected
// raises InvalidOperationException (the net48 "not been redirected" contract);
// an already-drained pipe yields "". Not thread-safe (the simplified domain).
static Il2CppObject* ReadPipeToEnd(int32_t pid, bool stderrStream)
{
    const auto it = g_processes().find(pid);
    if (it == g_processes().end())
    {
        return nullptr; // unknown PID
    }
    ProcessSlot& slot = it->second;
    const bool redirected = stderrStream ? slot.stderrRedirected : slot.stdoutRedirected;
    bool& drained = stderrStream ? slot.stderrDrained : slot.stdoutDrained;
    HANDLE& h = stderrStream ? slot.stderrRead : slot.stdoutRead;
    if (!redirected)
    {
        il2cpp_throw_invalid_operation_exception(
            stderrStream
                ? "StandardError has not been redirected."
                : "StandardOutput has not been redirected.");
    }
    if (drained || h == INVALID_HANDLE_VALUE)
    {
        return CsFromBytes(nullptr, 0); // already at EOF: net48 ReadToEnd -> ""
    }
    std::vector<char> captured;
    char buf[4096];
    for (;;)
    {
        DWORD got = 0;
        if (::ReadFile(h, buf, sizeof(buf), &got, nullptr) == 0 || got == 0)
        {
            break; // EOF (all write ends closed) or a read error
        }
        captured.insert(captured.end(), buf, buf + got);
    }
    ::CloseHandle(h);
    h = INVALID_HANDLE_VALUE;
    drained = true;
    return CsFromBytes(captured.data(), captured.size());
}

#endif // _WIN32

Il2CppObject* il2cpp_process_read_stdout_to_end(int32_t pid)
{
#if defined(_WIN32)
    return ReadPipeToEnd(pid, false);
#else
    (void)pid;
    return nullptr; // unsupported on POSIX
#endif
}

Il2CppObject* il2cpp_process_read_stderr_to_end(int32_t pid)
{
#if defined(_WIN32)
    return ReadPipeToEnd(pid, true);
#else
    (void)pid;
    return nullptr; // unsupported on POSIX
#endif
}

#if defined(_WIN32)

// nb8 STDIN-WRITE: write raw UTF-8 bytes to the child's redirected stdin pipe.
// The pipe is the parent-only WRITE end; WriteFile blocks until the child
// drains (the documented single-stream simplification). Returns false on an
// unknown PID / write failure.
static bool WriteStdinBytes(int32_t pid, const char* data, size_t length, bool& redirected)
{
    redirected = false;
    const auto it = g_processes().find(pid);
    if (it == g_processes().end())
    {
        return false; // unknown PID
    }
    ProcessSlot& slot = it->second;
    if (!slot.stdinRedirected)
    {
        return false; // stream not redirected (the caller raises IOE)
    }
    redirected = true;
    if (slot.stdinClosed || slot.stdinWrite == INVALID_HANDLE_VALUE)
    {
        // net48: a write AFTER Close is silently dropped (no throw).
        return true;
    }
    size_t written = 0;
    while (written < length)
    {
        DWORD chunk = static_cast<DWORD>(
            (length - written > 0x40000000u) ? 0x40000000u : (length - written));
        DWORD got = 0;
        if (::WriteFile(slot.stdinWrite, data + written, chunk, &got, nullptr) == 0)
        {
            return false;
        }
        written += got;
    }
    return true;
}

#endif // _WIN32

// The shared stdin-write entry: root-free (no allocation -> no GC hazard), the
// managed text arrives as the uniform Il2CppObject* reference slot.
static void WriteStdinText(int32_t pid, Il2CppObject* text, bool newline)
{
#if defined(_WIN32)
    std::string utf8;
    if (text != nullptr)
    {
        utf8 = CsToUtf8(text);
    }
    if (newline)
    {
        utf8.push_back('\r');
        utf8.push_back('\n');
    }
    bool redirected = false;
    if (!WriteStdinBytes(pid, utf8.data(), utf8.size(), redirected))
    {
        if (g_processes().find(pid) != g_processes().end())
        {
            il2cpp_throw_invalid_operation_exception(
                "StandardInput has not been redirected.");
        }
        return; // unknown PID: silent no-op (the simplified-domain convention)
    }
    (void)redirected;
#else
    (void)pid;
    (void)text;
    (void)newline;
#endif
}

void il2cpp_process_write_stdin_line(int32_t pid, Il2CppObject* text)
{
    WriteStdinText(pid, text, true);
}

void il2cpp_process_write_stdin(int32_t pid, Il2CppObject* text)
{
    WriteStdinText(pid, text, false);
}

void il2cpp_process_flush_stdin(int32_t pid)
{
    // Every stdin write goes straight to the pipe (AutoFlush-equivalent), so
    // Flush is a no-op. Return early for an unknown PID.
    (void)pid;
}

int32_t il2cpp_process_get_stdin_autofush(int32_t pid)
{
    (void)pid;
    return 1; // writes always flush (documented deviation from net48 default)
}

void il2cpp_process_set_stdin_autofush(int32_t pid, int32_t value)
{
    (void)pid;
    (void)value;
}

void il2cpp_process_close_stdin(int32_t pid)
{
#if defined(_WIN32)
    const auto it = g_processes().find(pid);
    if (it == g_processes().end())
    {
        return;
    }
    ProcessSlot& slot = it->second;
    if (!slot.stdinRedirected)
    {
        il2cpp_throw_invalid_operation_exception(
            "StandardInput has not been redirected.");
    }
    if (!slot.stdinClosed && slot.stdinWrite != INVALID_HANDLE_VALUE)
    {
        ::CloseHandle(slot.stdinWrite);
        slot.stdinWrite = INVALID_HANDLE_VALUE;
        slot.stdinClosed = true;
    }
#else
    (void)pid;
#endif
}

int32_t il2cpp_process_get_current_process_id()
{
    // OS process id -- delegated to the platform layer (GetCurrentProcessId
    // on Windows, getpid on POSIX).
    return plat_process_id();
}

//------------------------------------------------------------------------------
// PROC-GAP (nb3): ProcessName / HasExited / Kill -- the PID-domain closures
// over the tracked-child table + direct Win32 queries (the current process
// pid is NOT in the table, so the PID is opened directly).
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_process_get_name(int32_t pid)
{
#if defined(_WIN32)
    // Prefer the tracked handle when the child is still alive (no extra
    // open); the direct PID open covers the current process and every other
    // queryable process. An opened handle is always closed by the caller.
    HANDLE owned = nullptr;
    const auto it = g_processes().find(pid);
    HANDLE h = (it != g_processes().end() && it->second.process != nullptr)
        ? it->second.process
        : nullptr;
    if (h == nullptr)
    {
        h = ::OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
                          FALSE, static_cast<DWORD>(pid));
        owned = h;
    }
    if (h == nullptr)
    {
        return nullptr; // unopenable PID (already exited, access denied)
    }
    wchar_t image[MAX_PATH];
    DWORD size = MAX_PATH;
    const BOOL ok = ::QueryFullProcessImageNameW(h, 0, image, &size);
    if (owned != nullptr)
    {
        ::CloseHandle(owned);
    }
    if (!ok)
    {
        return nullptr;
    }
    const std::wstring path(image, image + static_cast<size_t>(size));
    const size_t sep = path.find_last_of(L"\\/");
    std::wstring name = (sep == std::wstring::npos) ? path : path.substr(sep + 1);
    // net48 ProcessName has no extension: strip a case-insensitive ".exe".
    if (name.size() >= 4)
    {
        const wchar_t* tail = name.data() + name.size() - 4;
        if (tail[0] == L'.' && (tail[1] == L'e' || tail[1] == L'E')
            && (tail[2] == L'x' || tail[2] == L'X')
            && (tail[3] == L'e' || tail[3] == L'E'))
        {
            name.resize(name.size() - 4);
        }
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(reinterpret_cast<const char16_t*>(name.data()),
                          static_cast<int32_t>(name.size())));
#else
    (void)pid;
    return nullptr; // unsupported on POSIX
#endif
}

int32_t il2cpp_process_has_exited(int32_t pid)
{
#if defined(_WIN32)
    const auto it = g_processes().find(pid);
    if (it != g_processes().end() && it->second.waited)
    {
        return 1; // wait_for_exit already observed the termination
    }
    HANDLE h = ::OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE,
                             FALSE, static_cast<DWORD>(pid));
    if (h == nullptr)
    {
        return 1; // unopenable PID: treat as terminated (M3)
    }
    const DWORD rc = ::WaitForSingleObject(h, 0);
    const bool exited = (rc == WAIT_OBJECT_0);
    ::CloseHandle(h);
    return exited ? 1 : 0;
#else
    (void)pid;
    return 1; // unsupported on POSIX
#endif
}

int32_t il2cpp_process_kill(int32_t pid)
{
#if defined(_WIN32)
    HANDLE h = ::OpenProcess(PROCESS_TERMINATE, FALSE, static_cast<DWORD>(pid));
    if (h == nullptr)
    {
        return 1;
    }
    const BOOL ok = ::TerminateProcess(h, 1);
    ::CloseHandle(h);
    if (ok)
    {
        // A tracked child's process handle now signals the termination: fold
        // the wait so a later wait_for_exit / get_exit_code / has_exited stay
        // consistent even without an explicit WaitForExit (net48 Kill leaves
        // the process a terminated handle).
        auto it = g_processes().find(pid);
        if (it != g_processes().end() && !it->second.waited)
        {
            ::WaitForSingleObject(it->second.process, INFINITE);
            DWORD code = 0;
            if (::GetExitCodeProcess(it->second.process, &code) == 0)
            {
                code = static_cast<DWORD>(-1);
            }
            it->second.exitCode = code;
            // Same authoritative point as wait_for_exit: the handle is live.
            CacheProcessTimes(it->second);
            ::CloseHandle(it->second.process);
            it->second.process = nullptr;
            it->second.waited = true;
        }
    }
    return ok ? 0 : 1;
#else
    (void)pid;
    return 1; // unsupported on POSIX
#endif
}

int32_t il2cpp_process_get_current_thread_id()
{
    // OS thread id -- delegated to the platform layer (GetCurrentThreadId on
    // Windows, SYS_gettid on Linux/Android, pthread_threadid_np on Apple,
    // getthrid on OpenBSD, a stable hash on other POSIX). Centralizing it
    // removes the duplicate in thread.cpp/monitor.cpp and fixes the Apple/
    // OpenBSD cases where truncating pthread_self to int32 can go negative.
    return plat_thread_current_id();
}

void il2cpp_process_exit(int32_t code)
{
#if defined(_WIN32)
    ::ExitProcess(static_cast<UINT>(code));
#else
    ::exit(code);
#endif
}

//------------------------------------------------------------------------------
// nb4 PROC-DIAG: StartTime / ExitTime / WorkingSet64 -- the Win32 diagnostic
// queries over the tracked-child table (preferred handle) or a direct PID
// open (the current process and untracked children). Post-exit semantics:
// net48 throws InvalidOperationException for a process that already exited
// (unless the working set was previously read -- then the first-read cache
// below returns the cached value); the runtime mirrors it for the
// deterministic shapes.
//------------------------------------------------------------------------------

// The helpers below are Win32-only (HANDLE / OpenProcess / FILETIME /
// g_processes) -- they must not compile on POSIX (the consuming icalls are
// each #if _WIN32-gated; the POSIX branch provides its own zero stubs).
#if defined(_WIN32)

namespace {

// The child slot's process handle (alive) or a fresh direct PID open. The
// caller closes 'owned' when non-null.
HANDLE ResolveProcessHandle(int32_t pid, HANDLE* owned)
{
    *owned = nullptr;
    const auto it = g_processes().find(pid);
    if (it != g_processes().end() && it->second.process != nullptr)
    {
        return it->second.process;
    }
    HANDLE h = ::OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
                             FALSE, static_cast<DWORD>(pid));
    *owned = h;
    return h;
}

} // namespace

#endif // _WIN32

#if defined(_WIN32)

Il2CppDateTime il2cpp_process_get_start_time(int32_t pid)
{
    // A waited tracked child has no handle left: answer from the times cached
    // at the authoritative wait/kill point. A cached miss (GetProcessTimes
    // failed there) keeps the documented zero/fallback behavior.
    const auto it = g_processes().find(pid);
    if (it != g_processes().end() && it->second.waited && it->second.timesCached)
    {
        return Il2CppDateTime(it->second.startTicks);
    }
    HANDLE owned = nullptr;
    const HANDLE h = ResolveProcessHandle(pid, &owned);
    if (h == nullptr)
    {
        return Il2CppDateTime(0);
    }
    FILETIME ct, et, kt, ut;
    const BOOL ok = ::GetProcessTimes(h, &ct, &et, &kt, &ut);
    if (owned != nullptr)
    {
        ::CloseHandle(owned);
    }
    if (!ok)
    {
        return Il2CppDateTime(0);
    }
    return Il2CppDateTime(FileTimeToDateTimeTicks(ct));
}

Il2CppDateTime il2cpp_process_get_exit_time(int32_t pid)
{
    // Same cache-first rule as StartTime (see above).
    const auto it = g_processes().find(pid);
    if (it != g_processes().end() && it->second.waited && it->second.timesCached)
    {
        return Il2CppDateTime(it->second.exitTicks);
    }
    HANDLE owned = nullptr;
    const HANDLE h = ResolveProcessHandle(pid, &owned);
    if (h == nullptr)
    {
        return Il2CppDateTime(0);
    }
    // net48 only exposes ExitTime after the process exited: a live process
    // raises InvalidOperationException (oracle-pinned by probe-proctime).
    // GetProcessTimes on a live process would otherwise report an all-zero
    // exit FILETIME, which the epoch shift turns into a bogus 1601 DateTime.
    if (::WaitForSingleObject(h, 0) == WAIT_TIMEOUT)
    {
        if (owned != nullptr)
        {
            ::CloseHandle(owned);
        }
        il2cpp_throw_invalid_operation_exception(
            "Process must exit before requested information can be determined.");
    }
    FILETIME ct, et, kt, ut;
    const BOOL ok = ::GetProcessTimes(h, &ct, &et, &kt, &ut);
    if (owned != nullptr)
    {
        ::CloseHandle(owned);
    }
    if (!ok)
    {
        return Il2CppDateTime(0);
    }
    return Il2CppDateTime(FileTimeToDateTimeTicks(et));
}

#endif // _WIN32

#if !defined(_WIN32)
Il2CppDateTime il2cpp_process_get_start_time(int32_t)
{
    return Il2CppDateTime(0);
}
Il2CppDateTime il2cpp_process_get_exit_time(int32_t)
{
    return Il2CppDateTime(0);
}
#endif // !_WIN32

int64_t il2cpp_process_get_working_set64(int32_t pid)
{
#if defined(_WIN32)
    const auto it = g_processes().find(pid);
    if (it != g_processes().end())
    {
        ProcessSlot& slot = it->second;
        if (slot.wsCached)
        {
            // net48 first-read cache: the working set was read during the
            // process lifetime, so a post-exit read on the SAME object returns
            // the cached value (it never touches the handle -> no IOE).
            return slot.wsCache;
        }
        if (slot.waited)
        {
            // net48: the process has exited and the working set was never read
            // while alive -> a first-ever post-exit read raises
            // InvalidOperationException.
            il2cpp_throw_invalid_operation_exception(
                "Cannot process request because the process has exited.");
        }
        // Alive tracked child: query through the slot handle and cache.
        PROCESS_MEMORY_COUNTERS pmc;
        std::memset(&pmc, 0, sizeof(pmc));
        const BOOL ok = ::GetProcessMemoryInfo(slot.process, &pmc, sizeof(pmc));
        if (!ok)
        {
            return 0;
        }
        slot.wsCache = static_cast<int64_t>(pmc.WorkingSetSize);
        slot.wsCached = true;
        return slot.wsCache;
    }
    HANDLE owned = nullptr;
    const HANDLE h = ResolveProcessHandle(pid, &owned);
    if (h == nullptr)
    {
        il2cpp_throw_invalid_operation_exception(
            "Cannot process request because the process has exited.");
    }
    PROCESS_MEMORY_COUNTERS pmc;
    std::memset(&pmc, 0, sizeof(pmc));
    const BOOL ok = ::GetProcessMemoryInfo(h, &pmc, sizeof(pmc));
    if (owned != nullptr)
    {
        ::CloseHandle(owned);
    }
    if (!ok)
    {
        return 0;
    }
    return static_cast<int64_t>(pmc.WorkingSetSize);
#else
    (void)pid;
    return 0; // unsupported on POSIX
#endif
}

//------------------------------------------------------------------------------
// nb7 PROC-STARTINFO: the ProcessStartInfo object model (GLOBAL descriptor +
// the property icalls). The descriptor is registered lazily by the ctors; the
// object is a plain light object allocated by the transpiler's newobj fallback.
//------------------------------------------------------------------------------

namespace {

bool g_processStartInfoRegistered = false;

// The net48 default: FileName/Arguments "" (never null) and UseShellExecute
// true; the remaining flags false. The two string allocations can trigger a
// compacting collection that MOVES the fresh object (reachable only through
// the caller's frame slot): 'self' is rooted across them and re-read after
// every allocation (the versionuri.cpp GcRoot contract).
void InitProcessStartInfo(Il2CppObject*& self)
{
    GcRoot<Il2CppObject*> root(self, "ProcessStartInfo..ctor");
    static const char16_t kEmpty[1] = { 0 };
    // Allocation then immediate store (no allocation in between keeps the
    // fresh string valid); the rooted object is re-read after each allocation.
    Il2CppString* fn = il2cpp_string_new(kEmpty, 0);
    reinterpret_cast<Il2CppProcessStartInfo*>(root.Get())->fileName = fn;
    Il2CppString* args = il2cpp_string_new(kEmpty, 0);
    Il2CppProcessStartInfo* info = reinterpret_cast<Il2CppProcessStartInfo*>(root.Get());
    info->arguments = args;
    info->useShellExecute = 1;
    info->redirectStandardOutput = 0;
    info->redirectStandardError = 0;
    info->redirectStandardInput = 0;
    info->createNoWindow = 0;
}

// A null managed setter argument is stored as the empty string (net48 never
// holds null for these text properties). The generated code passes the uniform
// Il2CppObject* reference slot.
Il2CppString* NonNullString(Il2CppObject* value)
{
    if (value != nullptr)
    {
        return reinterpret_cast<Il2CppString*>(value);
    }
    static const char16_t kEmpty[1] = { 0 };
    return il2cpp_string_new(kEmpty, 0);
}

} // namespace

void il2cpp_process_start_info_register_classes()
{
    if (g_processStartInfoRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Diagnostics_ProcessStartInfo_class);
    g_processStartInfoRegistered = true;
}

void icall_processstartinfo_ctor(Il2CppObject* self)
{
    il2cpp_process_start_info_register_classes();
    if (self == nullptr)
    {
        return;
    }
    InitProcessStartInfo(self); // updates 'self' in place if the object moved
}

void icall_processstartinfo_ctor_fn(Il2CppObject* self, Il2CppObject* fileName)
{
    il2cpp_process_start_info_register_classes();
    if (self == nullptr)
    {
        return;
    }
    InitProcessStartInfo(self);
    GcRoot<Il2CppObject*> root(self, "ProcessStartInfo..ctor(string)");
    reinterpret_cast<Il2CppProcessStartInfo*>(root.Get())->fileName = NonNullString(fileName);
}

void icall_processstartinfo_ctor_fn_args(Il2CppObject* self, Il2CppObject* fileName, Il2CppObject* arguments)
{
    il2cpp_process_start_info_register_classes();
    if (self == nullptr)
    {
        return;
    }
    InitProcessStartInfo(self);
    GcRoot<Il2CppObject*> root(self, "ProcessStartInfo..ctor(string, string)");
    Il2CppString* fn = NonNullString(fileName);
    reinterpret_cast<Il2CppProcessStartInfo*>(root.Get())->fileName = fn;
    Il2CppString* args = NonNullString(arguments);
    reinterpret_cast<Il2CppProcessStartInfo*>(root.Get())->arguments = args;
}

Il2CppObject* icall_processstartinfo_get_filename(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppObject*>(
        reinterpret_cast<Il2CppProcessStartInfo*>(self)->fileName);
}

void icall_processstartinfo_set_filename(Il2CppObject* self, Il2CppObject* value)
{
    // A null value allocates the empty string -> root 'self' across it.
    GcRoot<Il2CppObject*> root(self, "ProcessStartInfo.set_FileName");
    reinterpret_cast<Il2CppProcessStartInfo*>(root.Get())->fileName = NonNullString(value);
}

Il2CppObject* icall_processstartinfo_get_arguments(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppObject*>(
        reinterpret_cast<Il2CppProcessStartInfo*>(self)->arguments);
}

void icall_processstartinfo_set_arguments(Il2CppObject* self, Il2CppObject* value)
{
    GcRoot<Il2CppObject*> root(self, "ProcessStartInfo.set_Arguments");
    reinterpret_cast<Il2CppProcessStartInfo*>(root.Get())->arguments = NonNullString(value);
}

int32_t icall_processstartinfo_get_useshellexecute(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppProcessStartInfo*>(self)->useShellExecute;
}

void icall_processstartinfo_set_useshellexecute(Il2CppObject* self, int32_t value)
{
    reinterpret_cast<Il2CppProcessStartInfo*>(self)->useShellExecute = value;
}

int32_t icall_processstartinfo_get_redirectstdout(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppProcessStartInfo*>(self)->redirectStandardOutput;
}

void icall_processstartinfo_set_redirectstdout(Il2CppObject* self, int32_t value)
{
    reinterpret_cast<Il2CppProcessStartInfo*>(self)->redirectStandardOutput = value;
}

int32_t icall_processstartinfo_get_redirectstderr(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppProcessStartInfo*>(self)->redirectStandardError;
}

void icall_processstartinfo_set_redirectstderr(Il2CppObject* self, int32_t value)
{
    reinterpret_cast<Il2CppProcessStartInfo*>(self)->redirectStandardError = value;
}

int32_t icall_processstartinfo_get_redirectstdin(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppProcessStartInfo*>(self)->redirectStandardInput;
}

void icall_processstartinfo_set_redirectstdin(Il2CppObject* self, int32_t value)
{
    reinterpret_cast<Il2CppProcessStartInfo*>(self)->redirectStandardInput = value;
}

int32_t icall_processstartinfo_get_createnowindow(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppProcessStartInfo*>(self)->createNoWindow;
}

void icall_processstartinfo_set_createnowindow(Il2CppObject* self, int32_t value)
{
    reinterpret_cast<Il2CppProcessStartInfo*>(self)->createNoWindow = value;
}

}} // namespace il2cpp::runtime
