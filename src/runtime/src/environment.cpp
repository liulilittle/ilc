//------------------------------------------------------------------------------
// environment.cpp -- System.Environment icall implementations (see
// il2cpp_environment.h for the contract).
//
// Portability strategy: the Windows branch uses the ANSI (A-suffix) Win32
// APIs -- GetEnvironmentVariableA / SetEnvironmentVariableA /
// GetCurrentDirectoryA / SetCurrentDirectoryA / GetTickCount / GetSystemInfo
// / GetUserNameA / GetComputerNameA -- so the same source compiles under
// MSVC and MinGW-w64 without any wide-character conversion (UTF-16 <-> UTF-8
// transcoding is the icall boundary's job). The POSIX branch uses the C
// library (getenv / setenv / unsetenv / getcwd / chdir / gethostname /
// uname) plus <chrono> and <thread> -- the GCC 7.5 baseline has no
// std::filesystem, so paths never go through it.
//
// Encoding: managed strings are UTF-16; the Win32 A-suffix APIs expect the
// system ANSI code page. Values are bridged as UTF-8 bytes (ASCII names and
// values -- the practical .NET convention -- are exact; non-ASCII names
// would require CP_ACP conversion, out of the simplified scope). The two
// private transcoders below are the same design as the file-private ones in
// io.cpp (that translation unit is owned elsewhere and not shared here).
//
// net48 semantics verified against a reference run of the environment2 e2e
// case: SetEnvironmentVariable(name, "") DELETES the variable just like a
// null value.
//------------------------------------------------------------------------------

// MSVC deprecates std::getenv (C4996) in favor of _dupenv_s, which does not
// exist on POSIX. getenv is the portable choice and its use here is
// intentional (the process environment block); silence the deprecation for
// MSVC only. This MUST precede EVERY include -- this toolchain's <cstdint>/
// <cstddef> (pulled in by the project headers below) already declares getenv
// inside <cstdlib>, so a later define would not remove the deprecation
// attribute (verified empirically).
#ifdef _MSC_VER
    #define _CRT_SECURE_NO_WARNINGS
#endif

#include "il2cpp_environment.h"

#include "il2cpp_string.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception (D-ENV-4)
#include "il2cpp_pinvoke_ext.h"   // il2cpp_pinvoke_string_to_utf8_alloc (P6 FailFast)
#include "threadobj.h"           // icall_threadobj_get_current_thread (P6 CurrentManagedThreadId)
#include "versionuri.h"          // il2cpp_version_class / icall_version_ctor_4 (Environment.Version)
#include "il2cpp_runtime.h"      // il2cpp_object_new (Environment.Version)

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <windows.h>
#else
    #include <chrono>
    #include <thread>
    #include <unistd.h>      // ::getcwd, ::chdir, ::gethostname
    #include <sys/utsname.h> // ::uname
#endif

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// UTF-16 <-> UTF-8 transcoding (managed string <-> ANSI-API byte content).
//------------------------------------------------------------------------------

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

// UTF-8 bytes (explicit length) -> managed string. Valid BMP sequences
// decode to single units, astral sequences to surrogate pairs, invalid
// sequences to U+FFFD (the .NET replacement character).
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
                 && (static_cast<unsigned char>(data[i + 1]) & 0xC0) == 0x80)
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
                 && (static_cast<unsigned char>(data[i + 1]) & 0xC0) == 0x80
                 && (static_cast<unsigned char>(data[i + 2]) & 0xC0) == 0x80)
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
                 && (static_cast<unsigned char>(data[i + 1]) & 0xC0) == 0x80
                 && (static_cast<unsigned char>(data[i + 2]) & 0xC0) == 0x80
                 && (static_cast<unsigned char>(data[i + 3]) & 0xC0) == 0x80)
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

//------------------------------------------------------------------------------
// Environment-variable helpers.
//------------------------------------------------------------------------------

// The process environment value of 'name' ("" when unset) -- std::getenv is
// available on every supported platform, including Windows (it reads the
// process environment block).
std::string GetEnvValue(const char* name)
{
    const char* value = std::getenv(name);
    return (value != nullptr) ? std::string(value) : std::string();
}

// Set or delete a process environment variable (UTF-8 bytes). 'deleteVar'
// removes it entirely (SetEnvironmentVariableA(name, NULL) / unsetenv);
// otherwise the value is overwritten/created.
int32_t ApplyEnvChange(const std::string& name, const std::string& value, bool deleteVar)
{
#if defined(_WIN32)
    const BOOL ok = ::SetEnvironmentVariableA(name.c_str(),
                                              deleteVar ? nullptr : value.c_str());
    return ok ? 0 : 1;
#else
    const int rc = deleteVar ? ::unsetenv(name.c_str()) : ::setenv(name.c_str(), value.c_str(), 1);
    return rc == 0 ? 0 : 1;
#endif
}

// The user profile / home directory ("" when unavailable).
std::string HomeDir()
{
#if defined(_WIN32)
    return GetEnvValue("USERPROFILE");
#else
    return GetEnvValue("HOME");
#endif
}

// The platform path separator (single character: '\' on Windows, '/' on
// every POSIX platform). get_special_folder joins a home directory with a
// folder name; the backslash form would produce "~/Desktop"-style broken
// paths on POSIX.
const char* DirSep()
{
#if defined(_WIN32)
    return "\\";
#else
    return "/";
#endif
}

// The process-wide exit code (Environment.ExitCode). net48 keeps a single
// process-lifetime value, defaulting to 0; the getter returns the last set
// value (verified against a net48 reference run: 0 initially, the set value
// thereafter). The process-exit integration (Environment.Exit) belongs to
// the runtime main, not this property icall.
int32_t g_exit_code = 0;

} // namespace

//------------------------------------------------------------------------------
// System.Environment icalls.
//------------------------------------------------------------------------------

Il2CppObject* icall_environment_get_environment_variable(Il2CppObject* name)
{
    if (name == nullptr)
    {
        // net48 semantics (D-ENV-4, probe-pinned): Environment.
        // GetEnvironmentVariable(null) throws ArgumentNullException with
        // ParamName "variable" -- it does NOT return null. Throw the managed
        // carrier so the transpiled try/catch(ArgumentNullException) catches
        // it exactly like the net48 oracle.
        il2cpp_throw_argument_null_exception("variable");
        return nullptr; // unreachable (il2cpp_throw_argument_null_exception throws)
    }
    const std::string n = CsToUtf8(name);
#if defined(_WIN32)
    // First call with a null buffer returns the required size including the
    // terminating NUL; zero means the variable does not exist.
    const DWORD required = ::GetEnvironmentVariableA(n.c_str(), nullptr, 0);
    if (required == 0)
    {
        return nullptr; // not found -> .NET returns null
    }
    std::vector<char> buffer(required);
    const DWORD got = ::GetEnvironmentVariableA(n.c_str(), buffer.data(), required);
    if (got == 0 || got >= required)
    {
        return nullptr; // race (variable deleted meanwhile) -> treat as missing
    }
    return Utf8ToCs(buffer.data(), std::strlen(buffer.data()));
#else
    const char* value = std::getenv(n.c_str());
    if (value == nullptr || value[0] == '\0')
    {
        // net48 semantics (verified against a reference run): a variable
        // with an EMPTY value reads as null, exactly like a missing one --
        // the Windows branch hits the same null via the got==0 guard.
        return nullptr;
    }
    return Utf8ToCs(value, std::strlen(value));
#endif
}

int32_t icall_environment_set_environment_variable(Il2CppObject* name, Il2CppObject* value)
{
    if (name == nullptr)
    {
        return -1;
    }
    const std::string n = CsToUtf8(name);
    if (value == nullptr)
    {
        return ApplyEnvChange(n, std::string(), true); // null value -> delete
    }
    const std::string v = CsToUtf8(value);
    if (v.empty())
    {
        // net48 semantics (verified against the reference run): an empty
        // value also deletes the variable.
        return ApplyEnvChange(n, std::string(), true);
    }
    return ApplyEnvChange(n, v, false);
}

Il2CppObject* icall_environment_get_current_directory()
{
#if defined(_WIN32)
    const DWORD required = ::GetCurrentDirectoryA(0, nullptr);
    if (required == 0)
    {
        return nullptr;
    }
    std::vector<char> buffer(required);
    if (::GetCurrentDirectoryA(required, buffer.data()) == 0)
    {
        return nullptr;
    }
    return Utf8ToCs(buffer.data(), std::strlen(buffer.data()));
#else
    std::vector<char> buffer(4096);
    if (::getcwd(buffer.data(), buffer.size()) == nullptr)
    {
        return nullptr; // path longer than the buffer / deleted directory
    }
    return Utf8ToCs(buffer.data(), std::strlen(buffer.data()));
#endif
}

int32_t icall_environment_set_current_directory(Il2CppObject* path)
{
    if (path == nullptr)
    {
        return -1;
    }
    const std::string p = CsToUtf8(path);
#if defined(_WIN32)
    return ::SetCurrentDirectoryA(p.c_str()) ? 0 : 1;
#else
    return ::chdir(p.c_str()) == 0 ? 0 : 1;
#endif
}

int32_t icall_environment_get_tick_count()
{
#if defined(_WIN32)
    // net48 semantics: the DWORD GetTickCount() reinterpreted as 32-bit
    // signed milliseconds (wraps through negative values every ~49.7 days).
    return static_cast<int32_t>(::GetTickCount());
#else
    const int64_t ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
    // Mask modulo 2^31: stays in [0, INT32_MAX] and wraps to 0 after
    // ~24.9 days, approximating net48's signed-32-bit millisecond counter.
    return static_cast<int32_t>(ms % 2147483648LL);
#endif
}

// P6: Environment.TickCount64 (net8+): 64-bit monotonic millisecond counter.
// Windows: GetTickCount64() (UINT64, never wraps). POSIX: steady_clock in ms.
int64_t icall_environment_get_tick_count64()
{
#if defined(_WIN32)
    return static_cast<int64_t>(::GetTickCount64());
#else
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
#endif
}

// P6: Environment.FailFast(string message): terminate the process immediately.
// Prints the message to stderr (if non-null), flushes, and calls std::abort().
// The managed message is an Il2CppString* (UTF-16); convert to UTF-8 for stderr.
void icall_environment_fail_fast(Il2CppObject* message)
{
    if (message != nullptr)
    {
        auto* str = reinterpret_cast<Il2CppString*>(message);
        auto utf8 = il2cpp_pinvoke_string_to_utf8_alloc(str);
        if (utf8 != nullptr)
        {
            std::fprintf(stderr, "Fatal error: %s\n", utf8);
            il2cpp_pinvoke_utf8_free(utf8);
        }
    }
    std::fflush(stderr);
    std::abort();
}

// P6: Environment.CurrentManagedThreadId (net8+): the current thread's
// managed thread ID. Delegates to Thread.CurrentThread.ManagedThreadId.
// The OS thread id IS the managed thread id (threadobj.h:22).
int32_t icall_environment_get_current_managed_thread_id()
{
    auto* thread = icall_threadobj_get_current_thread();
    return icall_threadobj_get_managed_thread_id(thread);
}

int32_t icall_environment_get_exit_code()
{
    return g_exit_code;
}

//------------------------------------------------------------------------------
// icall_environment_get_has_shutdown_started -- Environment.HasShutdownStarted
// (the netcore21+ audit surface): the simplified runtime never initiates
// shutdown while managed code runs -- the honest constant answer is false on
// every platform (cross-platform consistency).
//------------------------------------------------------------------------------
int32_t icall_environment_get_has_shutdown_started()
{
    return 0;
}


int32_t icall_environment_set_exit_code(int32_t code)
{
    g_exit_code = code;
    return 0; // net48's property setter never fails
}

int32_t icall_environment_get_processor_count()
{
#if defined(_WIN32)
    SYSTEM_INFO si;
    ::GetSystemInfo(&si);
    const DWORD n = si.dwNumberOfProcessors;
    return n > 0 ? static_cast<int32_t>(n) : 1;
#else
    const unsigned int n = std::thread::hardware_concurrency();
    return n > 0 ? static_cast<int32_t>(n) : 1;
#endif
}

Il2CppObject* icall_environment_get_user_name()
{
#if defined(_WIN32)
    // Fixed buffer, single call: GetUserNameA sizes "UNLEN" (256) characters
    // without the terminator; 512 bytes covers any real account name. The
    // two-call sizing pattern is NOT used -- GetUserNameA and
    // GetComputerNameA report DIFFERENT error codes for the same size
    // query (ERROR_INSUFFICIENT_BUFFER vs ERROR_BUFFER_OVERFLOW), which
    // makes the error-code guard brittle across the two APIs.
    char buffer[512];
    DWORD size = sizeof(buffer);
    if (::GetUserNameA(buffer, &size) == 0)
    {
        return nullptr;
    }
    return Utf8ToCs(buffer, std::strlen(buffer));
#else
    // USER wins over LOGNAME (both are standard; USER is what .NET reads).
    const char* user = std::getenv("USER");
    if (user == nullptr || user[0] == '\0')
    {
        user = std::getenv("LOGNAME");
    }
    if (user == nullptr)
    {
        return nullptr;
    }
    return Utf8ToCs(user, std::strlen(user));
#endif
}

Il2CppObject* icall_environment_get_machine_name()
{
#if defined(_WIN32)
    // Fixed buffer, single call (see get_user_name -- the two-call pattern's
    // error code differs between the two Name APIs).
    char buffer[512];
    DWORD size = sizeof(buffer);
    if (::GetComputerNameA(buffer, &size) == 0)
    {
        return nullptr;
    }
    return Utf8ToCs(buffer, std::strlen(buffer));
#else
    char buffer[256];
    if (::gethostname(buffer, sizeof(buffer)) != 0)
    {
        return nullptr; // failure to obtain the machine name -> null
    }
    buffer[sizeof(buffer) - 1] = '\0';
    return Utf8ToCs(buffer, std::strlen(buffer));
#endif
}

Il2CppObject* icall_environment_get_os_version()
{
#if defined(_WIN32)
    // net48 semantics (D-ENV-1, probe-pinned): Environment.OSVersion.ToString()
    // = "Microsoft Windows NT X.Y.Z.R" -- a 4-component Version (revision 0 on
    // the NT line) built from GetVersionEx. GetVersionEx is subject to the
    // app-compat manifest shim: a NON-manifested process reports 6.2.9200.0
    // (exactly the net48 reference value -- the net48 oracle and the
    // transpiled exe both have no Win10 compatibility manifest), whereas
    // RtlGetVersion returns the REAL 10.0.xxxxx (a surface divergence). The
    // service-pack suffix is appended when szCSDVersion is non-empty (net48
    // OperatingSystem.ToString(); empty on modern hosts). GetVersionExA is
    // deprecated by the SDK (C4996 on MSVC /W4), so it is loaded dynamically
    // from kernel32 -- the same GetProcAddress pattern the RtlGetVersion code
    // it replaces used (no import-library dependency, no deprecation warning).
    typedef BOOL(WINAPI* GetVersionExFn)(LPOSVERSIONINFOA);
    const HMODULE kernel32 = ::GetModuleHandleA("kernel32.dll"); // always loaded
    if (kernel32 == nullptr)
    {
        return nullptr;
    }
    const GetVersionExFn getVersionEx =
        reinterpret_cast<GetVersionExFn>(::GetProcAddress(kernel32, "GetVersionExA"));
    if (getVersionEx == nullptr)
    {
        return nullptr;
    }
    OSVERSIONINFOA info;
    info.dwOSVersionInfoSize = sizeof(info);
    if (getVersionEx(&info) == 0)
    {
        return nullptr;
    }
    char buffer[64];
    std::snprintf(buffer, sizeof(buffer), "Microsoft Windows NT %lu.%lu.%lu.0",
                  info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
    std::string result = buffer;
    if (info.szCSDVersion[0] != '\0')
    {
        result += " ";
        result += info.szCSDVersion;
    }
    return Utf8ToCs(result.data(), result.size());
#else
    struct utsname u;
    if (::uname(&u) != 0)
    {
        return nullptr;
    }
    // "<sysname> <release>": uname reports the actual OS product (Linux /
    // Darwin / OpenBSD / ...) plus the kernel release -- the net48
    // Environment.OSVersion shape (product name + version) and the same
    // format as the platform-layer plat_os_version (G8 unification).
    char buffer[256];
    std::snprintf(buffer, sizeof(buffer), "%s %s", u.sysname, u.release);
    return Utf8ToCs(buffer, std::strlen(buffer));
#endif
}

Il2CppObject* icall_environment_get_version()
{
    // System.Version OBJECT (the net48 closure contract; D-ENV-V). net48's
    // Environment.Version wraps the CLR release version -- the shared
    // 4.0.30319.xxxxx line (4.0.30319.42000 on every modern host, matching
    // the live net48 oracle). The other targets (net8+) never reach this
    // icall: their Environment.Version rides a different code path. Allocate
    // through the registered System.Version descriptor and fill the four
    // components via the existing ctor icall so ToString()/CompareTo() work
    // through the carried System.Version runtime surface.
    Il2CppObject* v = il2cpp_object_new(il2cpp_version_class());
    icall_version_ctor_4(v, 4, 0, 30319, 42000);
    return v;
}

Il2CppObject* icall_environment_get_special_folder(int32_t folder_id)
{
#if defined(_WIN32)
    // net48 semantics (D-ENV-2, probe-pinned): Environment.GetFolderPath
    // resolves the REAL known-folder path via the shell known-folder API
    // (SHGetFolderPath) -- OneDrive-redirected Desktop/MyDocuments, the
    // actual LocalApplicationData -- NOT a home-directory guess. The
    // SpecialFolder enum values ARE the CSIDL values for the three supported
    // ids (Desktop=0, MyDocuments=5, LocalApplicationData=28), so the id is
    // passed directly as the CSIDL. SHGetFolderPathW is loaded dynamically
    // from shell32 (no import-library dependency, the same pattern as the
    // ntdll RtlGetVersion loading the earlier code used); the wide result
    // bridges directly into the managed string (same UTF-16 code units net48
    // holds -- no encoding round trip). A failure falls back to the
    // simplified env-var mapping below.
    if (folder_id == 0 || folder_id == 5 || folder_id == 28)
    {
        typedef HRESULT(WINAPI* SHGetFolderPathFn)(HWND, int, HANDLE, DWORD, LPWSTR);
        // shell32.dll may not be loaded in a console process -- LoadLibrary
        // (increments the refcount; the loader unloads it at exit), NOT
        // GetModuleHandle which only finds already-loaded modules.
        const HMODULE shell32 = ::LoadLibraryA("shell32.dll");
        if (shell32 != nullptr)
        {
            const SHGetFolderPathFn shGetFolderPath =
                reinterpret_cast<SHGetFolderPathFn>(::GetProcAddress(shell32, "SHGetFolderPathW"));
            if (shGetFolderPath != nullptr)
            {
                WCHAR buffer[1024];
                const HRESULT hr = shGetFolderPath(nullptr, folder_id, nullptr, 0, buffer);
                if (hr == 0 && buffer[0] != L'\0') // S_OK with a non-empty path
                {
                    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(
                        reinterpret_cast<const char16_t*>(buffer),
                        static_cast<int32_t>(std::wcslen(buffer))));
                }
            }
            ::FreeLibrary(shell32);
        }
    }
#endif
    std::string result;
    if (folder_id == 0) // Environment.SpecialFolder.Desktop
    {
        const std::string home = HomeDir();
        if (!home.empty())
        {
            result = home + DirSep() + "Desktop"; // per-platform default location
        }
    }
    else if (folder_id == 5) // Environment.SpecialFolder.MyDocuments
    {
        const std::string home = HomeDir();
        if (!home.empty())
        {
            result = home + DirSep() + "Documents";
        }
    }
    else if (folder_id == 28) // Environment.SpecialFolder.LocalApplicationData
    {
#if defined(_WIN32)
        result = GetEnvValue("LOCALAPPDATA");
        if (result.empty())
        {
            const std::string home = HomeDir();
            if (!home.empty())
            {
                result = home + "\\AppData\\Local";
            }
        }
#else
        result = GetEnvValue("XDG_DATA_HOME");
        if (result.empty())
        {
            const std::string home = HomeDir();
            if (!home.empty())
            {
                result = home + "/.local/share";
            }
        }
#endif
    }
    // else: unsupported folder id -> empty string (simplified contract).
    // An empty result ALSO yields an empty string -- never null.
    return Utf8ToCs(result.data(), result.size());
}

Il2CppObject* icall_environment_get_command_line()
{
#if defined(_WIN32)
    // net48 semantics: the full process command line (executable path +
    // arguments) exactly as the OS supplied it. GetCommandLineW returns
    // UTF-16 -- the managed string's native encoding -- so the result is
    // bridged directly without transcoding.
    const wchar_t* cmd = ::GetCommandLineW();
    if (cmd == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(reinterpret_cast<const char16_t*>(cmd),
                          static_cast<int32_t>(std::wcslen(cmd))));
#elif defined(__linux__) || defined(__ANDROID__)
    // Minimal POSIX implementation (G11): reconstruct the command line from
    // /proc/self/cmdline -- the kernel's argv view (argv[0] = executable
    // path, then each argument, NUL-separated). Joining the entries with
    // single spaces reproduces the net48 "executable path + arguments"
    // shape. The runtime has no argv injection point yet (the transpiler
    // builds the managed Main(string[]) itself), so /proc is the
    // self-contained source; the exact inter-argument whitespace/quoting is
    // not preserved (documented simplification, same source .NET Core
    // uses). Other POSIX targets (macOS/OpenBSD) have no /proc and keep the
    // empty fallback below.
    std::string raw;
    {
        FILE* f = std::fopen("/proc/self/cmdline", "rb");
        if (f != nullptr)
        {
            char buf[512];
            for (size_t n = std::fread(buf, 1, sizeof(buf), f); n > 0;
                 n = std::fread(buf, 1, sizeof(buf), f))
            {
                raw.append(buf, n);
            }
            std::fclose(f);
        }
    }
    if (raw.empty())
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    // Join the NUL-separated argv entries with single spaces. The kernel
    // appends a trailing NUL (the final entry's terminator + an empty entry),
    // so empty entries are skipped -- never a trailing space.
    std::string cmd;
    size_t start = 0;
    for (size_t i = 0; i <= raw.size(); ++i)
    {
        if (i == raw.size() || raw[i] == '\0')
        {
            if (i > start)
            {
                if (!cmd.empty()) { cmd.push_back(' '); }
                cmd.append(raw, start, i - start);
            }
            start = i + 1;
        }
    }
    return Utf8ToCs(cmd.data(), cmd.size());
#else
    // Stub for POSIX targets without /proc/self/cmdline (macOS/OpenBSD):
    // the runtime has no argv injection point, so the command line is
    // unknown; return "" (see il2cpp_environment.h).
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
#endif
}

}} // namespace il2cpp::runtime
