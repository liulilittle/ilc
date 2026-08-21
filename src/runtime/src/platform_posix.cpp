//------------------------------------------------------------------------------
// platform_posix.cpp -- the non-_WIN32 implementation of the platform layer
// (see platform.h for the contract and docs/design/cross-platform-design.md
// section 2 for the design).
//
// Implementation bodies are guarded by #if !defined(_WIN32): on Windows this
// translation unit is an EMPTY TU (the GLOB-based CMakeLists compiles every
// src/*.cpp everywhere; encoding.cpp precedent).
//
// Platform branches (docs/design/cross-platform-design.md sections 3-5):
//   __linux__   : syscall(SYS_gettid) thread ids; dlopen/dladdr fully usable
//   __ANDROID__ : Bionic libc -- pthreads are in libc (no -pthread flag),
//                 SYS_gettid available, dladdr limited (unused here)
//   __APPLE__   : pthread_threadid_np thread ids; clock_gettime requires
//                 macOS 10.12+ / iOS 10+ (the documented baseline)
//   __OpenBSD__ : getthrid(2) thread ids; pthread/dlopen/clock_gettime in
//                 libc (no -pthread, no -ldl)
//   other POSIX : std::hash<std::thread::id> thread-id fallback
//------------------------------------------------------------------------------

#include "platform.h"

#if !defined(_WIN32)

    #include <dlfcn.h>     // ::dlopen, ::dlsym
    #include <pthread.h>   // pthread_mutex_* / pthread_cond_*
    #include <sched.h>     // ::sched_yield
    #include <sys/stat.h>  // ::stat, ::mkdir
    #include <sys/types.h>
    #include <sys/utsname.h> // ::uname
    #include <time.h>      // ::clock_gettime
    #include <unistd.h>    // ::getpid, ::unsetenv, ::rmdir, ::syscall

    #if defined(__linux__)
        #include <sys/syscall.h> // SYS_gettid
    #elif defined(__APPLE__)
        #include <cstdlib> // std::getenv / ::setenv (unistd on Linux)
    #elif defined(__OpenBSD__)
        #include <pthread_np.h> // ::getthrid
    #endif

    #include <cerrno>    // errno / ENOENT / EEXIST (file & dir domain)
    #include <chrono>
    #include <cstdlib>
    #include <cstring>
    #include <functional>
    #include <new>       // std::nothrow (spawn failure path)
    #include <string>
    #include <thread>

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Spawn trampoline: swallows thread-function exceptions (a throw must
// never cross the thread boundary into std::terminate).
//------------------------------------------------------------------------------
void SpawnTrampoline(void (*fn)(void*), void* arg)
{
    try
    {
        fn(arg);
    }
    catch (...)
    {
        // Exception-neutral platform layer.
    }
}

bool EndsWith(const char* s, const char* suffix)
{
    const size_t slen = std::strlen(s);
    const size_t suffixLen = std::strlen(suffix);
    return slen >= suffixLen &&
           std::strcmp(s + (slen - suffixLen), suffix) == 0;
}

} // namespace

//------------------------------------------------------------------------------
// Handle structs (opaque to the header).
//------------------------------------------------------------------------------
struct plat_thread
{
    std::thread t;
};

struct plat_mutex
{
    pthread_mutex_t m;
};

struct plat_cond
{
    pthread_cond_t c;
};

//------------------------------------------------------------------------------
// 1. Thread domain.
//------------------------------------------------------------------------------
thread_id_t plat_thread_current_id()
{
#if defined(__linux__) || defined(__ANDROID__)
    // gettid is a syscall on Linux (glibc exposes it as a real function
    // only in glibc 2.30+; the syscall form is the portable baseline).
    return static_cast<thread_id_t>(::syscall(SYS_gettid));
#elif defined(__APPLE__)
    uint64_t tid = 0;
    if (::pthread_threadid_np(nullptr, &tid) != 0)
    {
        return 0;
    }
    return static_cast<thread_id_t>(tid); // truncation documented in the header
#elif defined(__OpenBSD__)
    return static_cast<thread_id_t>(::getthrid());
#else
    // Non-Linux/Apple/OpenBSD POSIX fallback: a stable hash of the
    // std::thread id keeps the TU compilable on other POSIX platforms.
    return static_cast<thread_id_t>(
        std::hash<std::thread::id>{}(std::this_thread::get_id()));
#endif
}

void plat_thread_sleep(int32_t milliseconds)
{
    // Negative values are rejected by the icall layer; treat them as 0.
    if (milliseconds <= 0)
    {
        (void)::sched_yield(); // Sleep(0) semantics: yield, do not sleep
    }
    else
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds));
    }
}

void plat_thread_yield()
{
    // sched_yield returns EAGAIN when the calling thread is the only one
    // runnable -- the call itself is the contract.
    (void)::sched_yield();
}

plat_thread_t plat_thread_spawn(void (*fn)(void*), void* arg)
{
    // The header contract promises nullptr when creation fails (threadobj.cpp
    // checks for it). Windows returns nullptr on CreateThread failure; the
    // std::thread constructor here throws std::system_error instead -- catch
    // it (and the handle's allocation failure) so the contract holds on every
    // platform and the exception never crosses the layer boundary.
    auto* t = new (std::nothrow) plat_thread;
    if (t == nullptr)
    {
        return nullptr;
    }
    try
    {
        t->t = std::thread(&SpawnTrampoline, fn, arg);
    }
    catch (...)
    {
        delete t;
        return nullptr;
    }
    return t;
}

void plat_thread_join(plat_thread_t thread)
{
    thread->t.join();
    delete thread;
}

//------------------------------------------------------------------------------
// 2. Synchronization domain.
//------------------------------------------------------------------------------
plat_mutex_t plat_mutex_init()
{
    auto* m = new plat_mutex;
    (void)pthread_mutex_init(&m->m, nullptr); // default (non-recursive) mutex
    return m;
}

void plat_mutex_lock(plat_mutex_t mutex)
{
    (void)pthread_mutex_lock(&mutex->m);
}

bool plat_mutex_trylock(plat_mutex_t mutex)
{
    return pthread_mutex_trylock(&mutex->m) == 0;
}

void plat_mutex_unlock(plat_mutex_t mutex)
{
    (void)pthread_mutex_unlock(&mutex->m);
}

void plat_mutex_destroy(plat_mutex_t mutex)
{
    (void)pthread_mutex_destroy(&mutex->m);
    delete mutex;
}

plat_cond_t plat_cond_init()
{
    auto* c = new plat_cond;
    (void)pthread_cond_init(&c->c, nullptr);
    return c;
}

void plat_cond_wait(plat_cond_t cond, plat_mutex_t mutex)
{
    // Spurious wakeups are possible (documented in the header); the
    // caller re-checks its predicate.
    (void)pthread_cond_wait(&cond->c, &mutex->m);
}

void plat_cond_signal(plat_cond_t cond)
{
    (void)pthread_cond_signal(&cond->c);
}

void plat_cond_broadcast(plat_cond_t cond)
{
    (void)pthread_cond_broadcast(&cond->c);
}

void plat_cond_destroy(plat_cond_t cond)
{
    (void)pthread_cond_destroy(&cond->c);
    delete cond;
}

//------------------------------------------------------------------------------
// 3. High-resolution clock domain.
//------------------------------------------------------------------------------
int64_t plat_clock_monotonic_ns()
{
    // CLOCK_MONOTONIC counts nanoseconds on POSIX (macOS 10.12+ / iOS 10+
    // is the documented baseline; older versions would need
    // mach_absolute_time, deferred to the Apple batch).
    struct timespec ts;
    if (::clock_gettime(CLOCK_MONOTONIC, &ts) != 0)
    {
        return 0;
    }
    return static_cast<int64_t>(ts.tv_sec) * INT64_C(1000000000) +
           static_cast<int64_t>(ts.tv_nsec);
}

int64_t plat_clock_frequency()
{
    // clock_gettime already counts in nanoseconds.
    return INT64_C(1000000000);
}

//------------------------------------------------------------------------------
// 4. File domain.
//------------------------------------------------------------------------------
FILE* plat_file_open(const char* utf8_path, const char* mode)
{
    // POSIX fopen is byte-based: UTF-8 passes through unchanged.
    return ::fopen(utf8_path, mode);
}

bool plat_file_remove(const char* utf8_path)
{
    // POSIX remove is byte-based; ENOENT counts as success (File.Delete
    // silent-success semantics, header contract).
    return ::remove(utf8_path) == 0 || errno == ENOENT;
}

bool plat_path_full(const char* utf8_path, std::string& out)
{
    // realpath resolves symlinks and requires the path to EXIST
    // (GetFullPathNameW does not -- documented difference in the header).
    char* resolved = ::realpath(utf8_path, nullptr);
    if (resolved == nullptr)
    {
        return false;
    }
    out = resolved;
    ::free(resolved);
    return !out.empty();
}

const char* plat_path_tmpdir()
{
    // Static buffer: the value is fixed on first call and stable for the
    // process lifetime (the header promises pointer stability).
    static const std::string dir = []() -> std::string {
        std::string result;
        const char* tmpdir = ::getenv("TMPDIR");
        if (tmpdir != nullptr && tmpdir[0] != '\0')
        {
            result = std::string(tmpdir);
        }
#if defined(__ANDROID__)
        // Android has no /tmp directory -- /data/local/tmp is the
        // conventional writable temp location on the platform (G7).
        else
        {
            result = "/data/local/tmp";
        }
#else
        // POSIX fallback.
        else
        {
            result = "/tmp";
        }
#endif
        // Strip a trailing separator so the result joins like the Windows
        // "C:\Temp" (no trailing slash -- platform.h contract; the Windows
        // implementation strips the GetTempPathW backslash, platform_win.cpp).
        if (!result.empty() && result.back() == '/')
        {
            result.pop_back();
        }
        return result;
    }();
    return dir.c_str();
}

//------------------------------------------------------------------------------
// 5. Dynamic library domain.
//------------------------------------------------------------------------------
void* plat_dl_open(const char* name)
{
    void* handle = ::dlopen(name, RTLD_LAZY | RTLD_LOCAL);
    if (handle != nullptr || name == nullptr)
    {
        return handle;
    }
    // Windows-style "mylib.dll" names (DllImport attributes carry the
    // original name) are mapped to the local convention -- the mapping
    // lives HERE so the transpiler never needs a platform branch
    // (design doc section 7.2). The candidates follow the host's native
    // convention: Apple tries .dylib FIRST then .so (both are loadable);
    // every other POSIX platform tries .so.
    if (EndsWith(name, ".dll"))
    {
        const size_t baseLen = std::strlen(name) - 4; // strip ".dll"
        const std::string base(name, baseLen);
#if defined(__APPLE__)
        const char* const kExtensions[] = { ".dylib", ".so" };
#else
        const char* const kExtensions[] = { ".so" };
#endif
        for (const char* ext : kExtensions)
        {
            handle = ::dlopen(("lib" + base + ext).c_str(), RTLD_LAZY | RTLD_LOCAL);
            if (handle != nullptr)
            {
                return handle;
            }
            handle = ::dlopen((base + ext).c_str(), RTLD_LAZY | RTLD_LOCAL);
            if (handle != nullptr)
            {
                return handle;
            }
        }
    }
    return handle;
}

void* plat_dl_sym(void* handle, const char* name)
{
    if (handle == nullptr)
    {
        return nullptr;
    }
    return ::dlsym(handle, name);
}

//------------------------------------------------------------------------------
// 6. Environment variable domain.
//------------------------------------------------------------------------------
std::string plat_env_get(const char* name)
{
    const char* value = ::getenv(name);
    if (value == nullptr)
    {
        return std::string();
    }
    return std::string(value);
}

bool plat_env_set(const char* name, const char* value, bool overwrite)
{
    // Empty (or null) value DELETES the variable -- aligned with the
    // Windows SetEnvironmentVariableA semantics ("empty string deletes",
    // header contract).
    if (value == nullptr || value[0] == '\0')
    {
        return ::unsetenv(name) == 0;
    }
    return ::setenv(name, value, overwrite ? 1 : 0) == 0;
}

//------------------------------------------------------------------------------
// 7. Directory domain.
//------------------------------------------------------------------------------
bool plat_dir_exists(const char* path)
{
    struct stat st;
    return ::stat(path, &st) == 0;
}

bool plat_dir_mkdir(const char* path)
{
    // mkdir creates ONE level (no parents); EEXIST is not an error --
    // the contract is "true when it now exists".
    return ::mkdir(path, 0755) == 0 || errno == EEXIST;
}

bool plat_dir_rmdir(const char* path)
{
    return ::rmdir(path) == 0;
}

bool plat_path_is_dir(const char* path)
{
    struct stat st;
    return ::stat(path, &st) == 0 && S_ISDIR(st.st_mode);
}

bool plat_path_is_file(const char* path)
{
    struct stat st;
    return ::stat(path, &st) == 0 && S_ISREG(st.st_mode);
}

//------------------------------------------------------------------------------
// 8. Process domain.
//------------------------------------------------------------------------------
int32_t plat_process_id()
{
    return static_cast<int32_t>(::getpid());
}

uint32_t plat_cpu_count()
{
    const unsigned n = std::thread::hardware_concurrency();
    return n == 0 ? 1u : static_cast<uint32_t>(n);
}

std::string plat_os_version()
{
    struct utsname u;
    if (::uname(&u) == 0)
    {
        return std::string(u.sysname) + " " + std::string(u.release);
    }
    return std::string("Unknown POSIX");
}

}} // namespace il2cpp::runtime

#endif // !defined(_WIN32)
