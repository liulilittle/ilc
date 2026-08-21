//------------------------------------------------------------------------------
// platform_win.cpp -- the _WIN32 implementation of the platform layer (see
// platform.h for the contract and docs/design/cross-platform-design.md
// section 2 for the design).
//
// Implementation bodies are guarded by #if defined(_WIN32): on every other
// platform this translation unit is an EMPTY TU (the GLOB-based CMakeLists
// compiles every src/*.cpp everywhere; encoding.cpp precedent). All UTF-8
// string crossing is converted to/from wide strings here -- the public
// interface stays UTF-8 byte strings only.
//------------------------------------------------------------------------------

#include "platform.h"

#if defined(_WIN32)

    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <windows.h>

    #include <direct.h>    // ::_wmkdir, ::_wrmdir
    #include <sys/stat.h>  // ::_wstat, _S_IFDIR

    #include <cerrno>
    #include <cstring>
    #include <string>
    #include <thread>

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// UTF-8 <-> UTF-16 conversion (Windows CRT is wide-string based).
//------------------------------------------------------------------------------
std::wstring Utf8ToWide(const char* utf8)
{
    if (utf8 == nullptr || utf8[0] == '\0')
    {
        return std::wstring();
    }
    const int n = ::MultiByteToWideChar(CP_UTF8, 0, utf8, -1, nullptr, 0);
    if (n <= 0)
    {
        return std::wstring();
    }
    std::wstring w(static_cast<size_t>(n - 1), L'\0'); // n includes the terminator
    ::MultiByteToWideChar(CP_UTF8, 0, utf8, -1, &w[0], n);
    return w;
}

std::string WideToUtf8(const wchar_t* wide, size_t length)
{
    if (length == 0)
    {
        return std::string();
    }
    const int n = ::WideCharToMultiByte(CP_UTF8, 0, wide,
                                        static_cast<int>(length), nullptr, 0,
                                        nullptr, nullptr);
    if (n <= 0)
    {
        return std::string();
    }
    std::string s(static_cast<size_t>(n), '\0');
    ::WideCharToMultiByte(CP_UTF8, 0, wide, static_cast<int>(length),
                          &s[0], n, nullptr, nullptr);
    return s;
}

// Win32 thread entry trampoline: repacks the (fn, arg) pair and swallows
// any exception so it never crosses the thread boundary (std::terminate).
struct SpawnArg
{
    void (*fn)(void*);
    void* arg;
};

DWORD WINAPI SpawnTrampoline(LPVOID p)
{
    SpawnArg* pair = static_cast<SpawnArg*>(p);
    void (*fn)(void*) = pair->fn;
    void* arg = pair->arg;
    delete pair;
    try
    {
        fn(arg);
    }
    catch (...)
    {
        // The platform layer is exception-neutral: a throwing thread
        // function must not kill the process.
    }
    return 0;
}

} // namespace

//------------------------------------------------------------------------------
// Handle structs (opaque to the header).
//------------------------------------------------------------------------------
struct plat_thread
{
    HANDLE h;
};

struct plat_mutex
{
    CRITICAL_SECTION cs;
};

struct plat_cond
{
    CONDITION_VARIABLE cv;
};

//------------------------------------------------------------------------------
// 1. Thread domain.
//------------------------------------------------------------------------------
thread_id_t plat_thread_current_id()
{
    // GetCurrentThreadId is a DWORD (low 32 bits of the OS thread id);
    // int32_t mirrors the existing icall contract (always nonzero in
    // practice).
    return static_cast<thread_id_t>(::GetCurrentThreadId());
}

void plat_thread_sleep(int32_t milliseconds)
{
    // Negative values are rejected by the icall layer; guard here so the
    // platform layer never passes a huge DWORD to Sleep.
    if (milliseconds <= 0)
    {
        ::Sleep(0); // yield the time slice (Sleep(0) semantics)
    }
    else
    {
        ::Sleep(static_cast<DWORD>(milliseconds));
    }
}

void plat_thread_yield()
{
    // SwitchToThread returns FALSE when no other thread was ready; the
    // call itself is the contract.
    (void)::SwitchToThread();
}

plat_thread_t plat_thread_spawn(void (*fn)(void*), void* arg)
{
    auto* pair = new SpawnArg{fn, arg};
    const HANDLE h = ::CreateThread(nullptr, 0, &SpawnTrampoline, pair, 0, nullptr);
    if (h == nullptr)
    {
        delete pair;
        return nullptr;
    }
    auto* t = new plat_thread;
    t->h = h;
    return t;
}

void plat_thread_join(plat_thread_t thread)
{
    ::WaitForSingleObject(thread->h, INFINITE);
    ::CloseHandle(thread->h);
    delete thread;
}

//------------------------------------------------------------------------------
// 2. Synchronization domain.
//------------------------------------------------------------------------------
plat_mutex_t plat_mutex_init()
{
    auto* m = new plat_mutex;
    ::InitializeCriticalSection(&m->cs);
    return m;
}

void plat_mutex_lock(plat_mutex_t mutex)
{
    ::EnterCriticalSection(&mutex->cs);
}

bool plat_mutex_trylock(plat_mutex_t mutex)
{
    // TryEnterCriticalSection: FALSE when another thread holds the lock
    // (TRUE for the owning thread -- recursive; the header documents the
    // non-reentrant contract, so cross-thread trylock is the tested path).
    return ::TryEnterCriticalSection(&mutex->cs) != FALSE;
}

void plat_mutex_unlock(plat_mutex_t mutex)
{
    ::LeaveCriticalSection(&mutex->cs);
}

void plat_mutex_destroy(plat_mutex_t mutex)
{
    ::DeleteCriticalSection(&mutex->cs);
    delete mutex;
}

plat_cond_t plat_cond_init()
{
    auto* c = new plat_cond;
    ::InitializeConditionVariable(&c->cv);
    return c;
}

void plat_cond_wait(plat_cond_t cond, plat_mutex_t mutex)
{
    // Returns FALSE on timeout/error; INFINITE wait and the bool result
    // are dropped -- the contract is wait-until-signalled (threadpool's
    // timed waits stay in the business layer until a timed API is added).
    (void)::SleepConditionVariableCS(&cond->cv, &mutex->cs, INFINITE);
}

void plat_cond_signal(plat_cond_t cond)
{
    ::WakeConditionVariable(&cond->cv);
}

void plat_cond_broadcast(plat_cond_t cond)
{
    ::WakeAllConditionVariable(&cond->cv);
}

void plat_cond_destroy(plat_cond_t cond)
{
    // CONDITION_VARIABLE needs no teardown; the heap object is released.
    delete cond;
}

//------------------------------------------------------------------------------
// 3. High-resolution clock domain.
//------------------------------------------------------------------------------
int64_t plat_clock_monotonic_ns()
{
    LARGE_INTEGER counter;
    ::QueryPerformanceCounter(&counter);
    // QPC counts in frequency() ticks; convert to ns WITHOUT overflowing
    // int64: at a 10 MHz frequency the counter exceeds 2^63/1e9 ~ 922 s of
    // uptime, so the naive (counter * 1e9) / freq product wraps and the
    // reading goes negative (verified on a machine with 38e9 ticks). Split
    // into whole seconds plus the fractional ticks so every multiply stays
    // inside int64 for any realistic uptime.
    const int64_t freq = plat_clock_frequency();
    if (freq <= 0)
    {
        return 0;
    }
    const int64_t wholeSeconds = counter.QuadPart / freq;
    const int64_t fraction = counter.QuadPart % freq;
    return wholeSeconds * INT64_C(1000000000) +
           (fraction * INT64_C(1000000000)) / freq;
}

int64_t plat_clock_frequency()
{
    LARGE_INTEGER freq;
    ::QueryPerformanceFrequency(&freq);
    return freq.QuadPart;
}

//------------------------------------------------------------------------------
// 4. File domain.
//------------------------------------------------------------------------------
FILE* plat_file_open(const char* utf8_path, const char* mode)
{
    const std::wstring wide = Utf8ToWide(utf8_path);
    if (wide.empty())
    {
        return nullptr;
    }
    // _wfopen takes a wide mode string; the fopen mode codes are ASCII,
    // so the UTF-8 converter doubles as the ASCII-to-wide converter.
    const std::wstring wmode = Utf8ToWide(mode);
    if (wmode.empty())
    {
        return nullptr;
    }
#if defined(_MSC_VER)
    FILE* fp = nullptr;
    if (::_wfopen_s(&fp, wide.c_str(), wmode.c_str()) != 0)
    {
        return nullptr;
    }
    return fp;
#else
    return ::_wfopen(wide.c_str(), wmode.c_str());
#endif
}

bool plat_file_remove(const char* utf8_path)
{
    const std::wstring wide = Utf8ToWide(utf8_path);
    if (wide.empty())
    {
        return false;
    }
    // DeleteFileW removes by the exact UTF-16 name -- the wide path fixes
    // the ANSI-codepage mangling of ::remove on a UTF-8 byte path. A
    // missing file counts as success (File.Delete silent-success semantics,
    // header contract).
    return ::DeleteFileW(wide.c_str()) != 0 ||
           ::GetLastError() == ERROR_FILE_NOT_FOUND;
}

bool plat_path_full(const char* utf8_path, std::string& out)
{
    const std::wstring wide = Utf8ToWide(utf8_path);
    if (wide.empty())
    {
        return false;
    }
    // First call with a null buffer returns the required length
    // (including the terminator).
    const DWORD n = ::GetFullPathNameW(wide.c_str(), 0, nullptr, nullptr);
    if (n == 0)
    {
        return false;
    }
    std::wstring resolved(n, L'\0');
    const DWORD written = ::GetFullPathNameW(wide.c_str(), n, &resolved[0], nullptr);
    if (written == 0 || written > n)
    {
        return false;
    }
    resolved.resize(written);
    out = WideToUtf8(resolved.c_str(), resolved.size());
    return !out.empty();
}

const char* plat_path_tmpdir()
{
    // Static buffer: the value is fixed on first call and stable for the
    // process lifetime (the header promises pointer stability).
    static const std::string dir = []() -> std::string {
        const DWORD n = ::GetTempPathW(0, nullptr); // includes terminator
        if (n == 0)
        {
            return std::string();
        }
        std::wstring tmp(n, L'\0');
        const DWORD written = ::GetTempPathW(n, &tmp[0]);
        if (written == 0)
        {
            return std::string();
        }
        tmp.resize(written);
        std::string s = WideToUtf8(tmp.c_str(), tmp.size());
        // GetTempPathW appends a trailing backslash; strip it so the
        // result joins like the POSIX "/tmp" (no trailing slash).
        if (!s.empty() && s.back() == '\\')
        {
            s.pop_back();
        }
        return s;
    }();
    return dir.c_str();
}

//------------------------------------------------------------------------------
// 5. Dynamic library domain.
//------------------------------------------------------------------------------
void* plat_dl_open(const char* name)
{
    // LoadLibraryW accepts the raw name on Windows (no .dll mapping); the
    // wide variant keeps non-ASCII UTF-8 library names exact (LoadLibraryA
    // would read them through the ANSI code page).
    const std::wstring wname = Utf8ToWide(name);
    if (wname.empty())
    {
        return nullptr;
    }
    return reinterpret_cast<void*>(::LoadLibraryW(wname.c_str()));
}

void* plat_dl_sym(void* handle, const char* name)
{
    if (handle == nullptr)
    {
        return nullptr;
    }
    return reinterpret_cast<void*>(
        ::GetProcAddress(static_cast<HMODULE>(handle), name));
}

//------------------------------------------------------------------------------
// 6. Environment variable domain.
//------------------------------------------------------------------------------
std::string plat_env_get(const char* name)
{
    // The A variants interpret bytes through the ANSI code page, which
    // mangles non-ASCII UTF-8 names/values; the W variants bridge exactly
    // (Utf8ToWide/WideToUtf8 keep the public interface UTF-8 only).
    const std::wstring wname = Utf8ToWide(name);
    if (wname.empty())
    {
        return std::string();
    }
    // First call with a null buffer returns the required size (including
    // the terminator); 0 means unset.
    const DWORD n = ::GetEnvironmentVariableW(wname.c_str(), nullptr, 0);
    if (n == 0)
    {
        return std::string();
    }
    std::wstring value(static_cast<size_t>(n), L'\0');
    const DWORD got = ::GetEnvironmentVariableW(wname.c_str(), &value[0], n);
    if (got == 0)
    {
        return std::string();
    }
    value.resize(got); // drop the terminator
    return WideToUtf8(value.c_str(), value.size());
}

bool plat_env_set(const char* name, const char* value, bool overwrite)
{
    const std::wstring wname = Utf8ToWide(name);
    if (wname.empty())
    {
        return false;
    }
    // Empty (or null) value DELETES the variable -- SetEnvironmentVariableW
    // cannot represent an empty value, and the contract aligns both
    // platforms on "empty string deletes".
    if (value == nullptr || value[0] == '\0')
    {
        return ::SetEnvironmentVariableW(wname.c_str(), nullptr) != FALSE;
    }
    if (!overwrite && ::GetEnvironmentVariableW(wname.c_str(), nullptr, 0) != 0)
    {
        return true; // already set and overwrite is refused
    }
    const std::wstring wvalue = Utf8ToWide(value);
    if (wvalue.empty())
    {
        return false;
    }
    return ::SetEnvironmentVariableW(wname.c_str(), wvalue.c_str()) != FALSE;
}

//------------------------------------------------------------------------------
// 7. Directory domain.
//------------------------------------------------------------------------------
bool plat_dir_exists(const char* path)
{
    // _wstat probes by the exact UTF-16 name -- the narrow _stat would read
    // the UTF-8 bytes through the ANSI code page and miss (or mangle)
    // non-ASCII paths (plat_path_is_file precedent).
    const std::wstring wide = Utf8ToWide(path);
    if (wide.empty())
    {
        return false;
    }
    struct _stat st;
    return ::_wstat(wide.c_str(), &st) == 0;
}

bool plat_dir_mkdir(const char* path)
{
    // _wmkdir creates ONE level (no parents); EEXIST is not an error --
    // the contract is "true when it now exists". Wide so a non-ASCII UTF-8
    // directory name is created EXACTLY (the narrow _mkdir would pass the
    // bytes through the ANSI code page).
    const std::wstring wide = Utf8ToWide(path);
    if (wide.empty())
    {
        return false;
    }
    return ::_wmkdir(wide.c_str()) == 0 || errno == EEXIST;
}

bool plat_dir_rmdir(const char* path)
{
    const std::wstring wide = Utf8ToWide(path);
    if (wide.empty())
    {
        return false;
    }
    return ::_wrmdir(wide.c_str()) == 0;
}

bool plat_path_is_dir(const char* path)
{
    // _wstat probes by the exact UTF-16 name (wide rationale above).
    const std::wstring wide = Utf8ToWide(path);
    if (wide.empty())
    {
        return false;
    }
    struct _stat st;
    return ::_wstat(wide.c_str(), &st) == 0 && (st.st_mode & _S_IFDIR) != 0;
}

bool plat_path_is_file(const char* path)
{
    // _wstat probes by the exact UTF-16 name -- the narrow _stat would
    // read the UTF-8 bytes through the ANSI code page and miss (or mangle)
    // non-ASCII paths.
    const std::wstring wide = Utf8ToWide(path);
    if (wide.empty())
    {
        return false;
    }
    struct _stat st;
    return ::_wstat(wide.c_str(), &st) == 0 && (st.st_mode & _S_IFREG) != 0;
}

//------------------------------------------------------------------------------
// 8. Process domain.
//------------------------------------------------------------------------------
int32_t plat_process_id()
{
    return static_cast<int32_t>(::GetCurrentProcessId());
}

uint32_t plat_cpu_count()
{
    // GetSystemInfo.dwNumberOfProcessors is capped at 64 (the calling
    // process's processor group); machines with more than 64 logical
    // processors undercount. GetActiveProcessorCount(ALL_PROCESSOR_GROUPS)
    // reports the true total across every group (Windows 7+); fall back to
    // GetSystemInfo when it reports zero (extremely old systems).
    const DWORD total = ::GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
    if (total != 0)
    {
        return total;
    }
    SYSTEM_INFO info;
    ::GetSystemInfo(&info);
    return info.dwNumberOfProcessors;
}

std::string plat_os_version()
{
    // RtlGetVersion resolved dynamically (environment.cpp:440-447
    // precedent) -- GetVersionEx lies on Windows 8.1+.
    struct OsVersionInfo
    {
        ULONG dwOSVersionInfoSize;
        ULONG dwMajorVersion;
        ULONG dwMinorVersion;
        ULONG dwBuildNumber;
        ULONG dwPlatformId;
        WCHAR szCSDVersion[128];
    };
    typedef LONG(WINAPI* RtlGetVersionFn)(OsVersionInfo*);

    const HMODULE ntdll = ::GetModuleHandleA("ntdll.dll"); // always loaded
    if (ntdll != nullptr)
    {
        const RtlGetVersionFn rtlGetVersion =
            reinterpret_cast<RtlGetVersionFn>(::GetProcAddress(ntdll, "RtlGetVersion"));
        if (rtlGetVersion != nullptr)
        {
            OsVersionInfo info;
            info.dwOSVersionInfoSize = sizeof(info);
            if (rtlGetVersion(&info) == 0) // STATUS_SUCCESS
            {
                char buffer[64];
                std::snprintf(buffer, sizeof(buffer), "Windows %lu.%lu.%lu",
                              info.dwMajorVersion, info.dwMinorVersion,
                              info.dwBuildNumber);
                return std::string(buffer);
            }
        }
    }
    return std::string("Windows (unknown version)");
}

}} // namespace il2cpp::runtime

#endif // defined(_WIN32)
