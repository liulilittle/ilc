//------------------------------------------------------------------------------
// test_platform.cpp -- runtime unit tests for the platform-abstraction layer
// (see platform.h for the contract under test).
//
// Coverage (all eight domains of docs/design/cross-platform-design.md §2.2):
//   1. thread id      : nonzero, stable within a thread, distinct across
//                       threads.
//   2. thread spawn   : two spawned threads both run and join.
//   3. mutex exclusion: two threads, 10000 mutex-guarded increments each --
//                       the final counter is EXACTLY 20000.
//   4. mutex trylock  : a second thread observes the lock held (false);
//                       the main thread trylocks successfully once free.
//   5. cond signal    : waiter sets a ready flag under the mutex, then waits;
//                       the main thread's trylock loop proves the waiter is
//                       inside the wait before signalling -- no lost wakeup,
//                       no hang (both platforms release the mutex atomically
//                       with entering the wait).
//   6. cond broadcast : two waiters both wake.
//   7. clock          : monotonic_ns advances across a 10 ms sleep and the
//                       frequency is positive.
//   8. environment    : set/get roundtrip, overwrite=false keeps the old
//                       value, empty-string and null values DELETE.
//   9. dynamic library: open + resolve a symbol from the OS library
//                       (kernel32.dll / libc.so.6); a versioned POSIX
//                       soname (libm.so.6) loads directly.
//  10. process        : pid > 0, cpu count >= 1.
//  11. directory      : tmpdir-based mkdir -> exists -> is_dir -> rmdir ->
//                       gone; repeated mkdir (EEXIST) is not an error.
//  12. paths          : tmpdir is non-empty and carries NO trailing separator
//                       (both implementations strip it); plat_path_full(".")
//                       yields an absolute existing directory.
//
// Deadlock avoidance: every condition-variable wait is guaranteed by
// construction (the signaler observes the ready flag AND the mutex-release
// handoff before signalling); the polling loops carry a 2 s deadline and
// broadcast on timeout so a failed test can never hang the process.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "platform.h"

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <windows.h>
#endif

using namespace il2cpp::runtime; // plat_* surface (test_pathproc.cpp precedent)

namespace {

std::atomic<int> g_failures{0};

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

//------------------------------------------------------------------------------
// Section 1/2 -- thread id + spawn.
//------------------------------------------------------------------------------
struct ThreadIdProbe
{
    thread_id_t tid;
    std::atomic<bool> ran{false};
};

void ThreadIdWorker(void* arg)
{
    auto* probe = static_cast<ThreadIdProbe*>(arg);
    probe->tid = plat_thread_current_id();
    probe->ran.store(true);
}

//------------------------------------------------------------------------------
// Section 3/4 -- mutex exclusion + trylock.
//------------------------------------------------------------------------------
struct Counter
{
    plat_mutex_t mutex;
    int64_t value;
};

void IncrementLoop(void* arg)
{
    auto* counter = static_cast<Counter*>(arg);
    for (int i = 0; i < 10000; ++i)
    {
        plat_mutex_lock(counter->mutex);
        ++counter->value;
        plat_mutex_unlock(counter->mutex);
    }
}

struct TrylockProbe
{
    plat_mutex_t mutex;
    std::atomic<bool> heldSeen{false};
    std::atomic<bool> freeSeen{false};
    std::atomic<bool> done{false};
};

void TrylockWorker(void* arg)
{
    auto* probe = static_cast<TrylockProbe*>(arg);
    probe->heldSeen.store(!plat_mutex_trylock(probe->mutex));
    plat_mutex_lock(probe->mutex); // blocks until the main thread unlocks
    plat_mutex_unlock(probe->mutex);
    probe->done.store(true);
}

//------------------------------------------------------------------------------
// Section 5 -- cond signal handoff (ready flag + mutex-release handoff).
//------------------------------------------------------------------------------
struct SignalProbe
{
    plat_mutex_t mutex;
    plat_cond_t cond;
    std::atomic<bool> ready{false};
    std::atomic<bool> woke{false};
};

void SignalWaiter(void* arg)
{
    auto* probe = static_cast<SignalProbe*>(arg);
    plat_mutex_lock(probe->mutex);
    probe->ready.store(true);
    plat_cond_wait(probe->cond, probe->mutex);
    probe->woke.store(true);
    plat_mutex_unlock(probe->mutex);
}

void SignalProbeMain(SignalProbe* probe)
{
    // Poll the ready flag (2 s deadline). The waiter set it while holding
    // the mutex, immediately before waiting.
    for (int i = 0; i < 200 && !probe->ready.load(); ++i)
    {
        plat_thread_sleep(10);
    }
    // Acquiring the mutex after 'ready' proves the waiter is INSIDE the
    // wait (both platforms release the mutex atomically with entering the
    // wait) -- a signal under the lock cannot be lost.
    if (probe->ready.load())
    {
        plat_mutex_lock(probe->mutex);
        plat_cond_signal(probe->cond);
        plat_mutex_unlock(probe->mutex);
    }
    else
    {
        plat_mutex_lock(probe->mutex);
        plat_cond_broadcast(probe->cond); // unblock the waiter on failure
        plat_mutex_unlock(probe->mutex);
    }
}

//------------------------------------------------------------------------------
// Section 6 -- cond broadcast.
//------------------------------------------------------------------------------
struct BroadcastProbe
{
    plat_mutex_t mutex;
    plat_cond_t cond;
    std::atomic<bool> ready1{false};
    std::atomic<bool> ready2{false};
    std::atomic<int> wokeCount{0};
};

struct BroadcastWaiterArg
{
    BroadcastProbe* probe;
    std::atomic<bool>* ready;
};

void BroadcastWaiter(void* arg)
{
    auto* wa = static_cast<BroadcastWaiterArg*>(arg);
    plat_mutex_lock(wa->probe->mutex);
    wa->ready->store(true);
    plat_cond_wait(wa->probe->cond, wa->probe->mutex);
    wa->probe->wokeCount.fetch_add(1);
    plat_mutex_unlock(wa->probe->mutex);
}

//------------------------------------------------------------------------------
// Helpers for the path/dir sections.
//------------------------------------------------------------------------------
std::string UniqueTestDir()
{
    std::string dir = std::string(plat_path_tmpdir()) + "/il2cpp_plat_test_";
    char pidBuffer[16];
    std::snprintf(pidBuffer, sizeof(pidBuffer), "%d", plat_process_id());
    return dir + pidBuffer;
}

} // namespace

int main()
{
    //--- 1. thread id: nonzero, stable, distinct across threads ------------
    const thread_id_t mainTid = plat_thread_current_id();
    Expect(mainTid != 0, "main thread id is nonzero");
    Expect(plat_thread_current_id() == mainTid, "thread id is stable");
    ThreadIdProbe probe;
    plat_thread_t t1 = plat_thread_spawn(&ThreadIdWorker, &probe);
    Expect(t1 != nullptr, "thread spawn succeeds");
    if (t1 != nullptr)
    {
        plat_thread_join(t1);
        Expect(probe.ran.load(), "spawned thread ran");
        Expect(probe.tid != 0, "spawned thread id is nonzero");
        Expect(probe.tid != mainTid, "spawned thread id differs from main");
    }

    //--- 2. thread spawn/join pair -----------------------------------------
    ThreadIdProbe probe2;
    plat_thread_t t2 = plat_thread_spawn(&ThreadIdWorker, &probe2);
    Expect(t2 != nullptr, "second spawn succeeds");
    if (t2 != nullptr)
    {
        plat_thread_join(t2);
        Expect(probe2.ran.load(), "second thread ran");
    }

    //--- 3. mutex exclusion: exact 20000 after two 10000-loops -------------
    Counter counter;
    counter.mutex = plat_mutex_init();
    counter.value = 0;
    plat_thread_t t3 = plat_thread_spawn(&IncrementLoop, &counter);
    plat_thread_t t4 = plat_thread_spawn(&IncrementLoop, &counter);
    Expect(t3 != nullptr && t4 != nullptr, "contention threads spawn");
    if (t3 != nullptr) { plat_thread_join(t3); }
    if (t4 != nullptr) { plat_thread_join(t4); }
    Expect(counter.value == 20000, "mutex serialized 20000 increments");
    plat_mutex_destroy(counter.mutex);

    //--- 4. mutex trylock: held by another thread -> false; free -> true ---
    TrylockProbe trylock;
    trylock.mutex = plat_mutex_init();
    plat_mutex_lock(trylock.mutex); // main holds the lock
    plat_thread_t t5 = plat_thread_spawn(&TrylockWorker, &trylock);
    Expect(t5 != nullptr, "trylock thread spawns");
    for (int i = 0; i < 200 && !trylock.heldSeen.load(); ++i)
    {
        plat_thread_sleep(5);
    }
    Expect(trylock.heldSeen.load(), "trylock observes the held lock");
    plat_mutex_unlock(trylock.mutex); // release: worker's lock() proceeds
    if (t5 != nullptr) { plat_thread_join(t5); }
    // The mutex is now free (the worker exited); the OWNER-less trylock is
    // the tested path -- a reentrant trylock from the lock holder is
    // undefined behaviour on the non-recursive POSIX mutex (header).
    trylock.freeSeen.store(plat_mutex_trylock(trylock.mutex));
    if (trylock.freeSeen.load())
    {
        plat_mutex_unlock(trylock.mutex);
    }
    Expect(trylock.freeSeen.load(), "trylock succeeds on the free lock");
    plat_mutex_destroy(trylock.mutex);

    //--- 5. cond signal handoff --------------------------------------------
    SignalProbe signalProbe;
    signalProbe.mutex = plat_mutex_init();
    signalProbe.cond = plat_cond_init();
    plat_thread_t t6 = plat_thread_spawn(&SignalWaiter, &signalProbe);
    Expect(t6 != nullptr, "cond waiter spawns");
    SignalProbeMain(&signalProbe);
    if (t6 != nullptr) { plat_thread_join(t6); }
    Expect(signalProbe.woke.load(), "cond signal woke the waiter");
    plat_cond_destroy(signalProbe.cond);
    plat_mutex_destroy(signalProbe.mutex);

    //--- 6. cond broadcast: both waiters wake ------------------------------
    BroadcastProbe bcastProbe;
    bcastProbe.mutex = plat_mutex_init();
    bcastProbe.cond = plat_cond_init();
    BroadcastWaiterArg arg1{&bcastProbe, &bcastProbe.ready1};
    BroadcastWaiterArg arg2{&bcastProbe, &bcastProbe.ready2};
    plat_thread_t t7 = plat_thread_spawn(&BroadcastWaiter, &arg1);
    plat_thread_t t8 = plat_thread_spawn(&BroadcastWaiter, &arg2);
    Expect(t7 != nullptr && t8 != nullptr, "broadcast waiters spawn");
    for (int i = 0; i < 200 &&
            !(bcastProbe.ready1.load() && bcastProbe.ready2.load()); ++i)
    {
        plat_thread_sleep(10);
    }
    plat_mutex_lock(bcastProbe.mutex);
    plat_cond_broadcast(bcastProbe.cond);
    plat_mutex_unlock(bcastProbe.mutex);
    if (t7 != nullptr) { plat_thread_join(t7); }
    if (t8 != nullptr) { plat_thread_join(t8); }
    Expect(bcastProbe.wokeCount.load() == 2, "broadcast woke both waiters");
    plat_cond_destroy(bcastProbe.cond);
    plat_mutex_destroy(bcastProbe.mutex);

    //--- 7. clock: monotonic advance across a 10 ms sleep ------------------
    const int64_t freq = plat_clock_frequency();
    Expect(freq > 0, "clock frequency is positive");
    const int64_t c0 = plat_clock_monotonic_ns();
    plat_thread_sleep(10);
    const int64_t c1 = plat_clock_monotonic_ns();
    Expect(c1 > c0, "monotonic clock advanced");
    Expect(c1 - c0 >= INT64_C(5000000), "10 ms sleep advanced >= 5 ms");
    Expect(c1 - c0 < INT64_C(10000000000), "10 ms sleep advanced < 10 s");
    plat_thread_sleep(0); // yield: must return without sleeping

#if defined(_WIN32)
    // 7b. absolute-value regression: the naive (QPC * 1e9) / freq product
    // overflows int64 once QPC exceeds 2^63/1e9 ~ 922 s at a 10 MHz
    // frequency, making the reading negative (or garbage). Cross-check the
    // reading against an independent divide-first computation from the raw
    // QPC values -- the two must agree within a small slack. The delta-only
    // checks above pass with the overflow (the wrap is a constant offset),
    // so this absolute check is the one that catches it.
    {
        LARGE_INTEGER qpc;
        LARGE_INTEGER qpf;
        ::QueryPerformanceCounter(&qpc);
        ::QueryPerformanceFrequency(&qpf);
        const int64_t expected =
            (qpc.QuadPart / qpf.QuadPart) * INT64_C(1000000000) +
            ((qpc.QuadPart % qpf.QuadPart) * INT64_C(1000000000)) / qpf.QuadPart;
        const int64_t got = plat_clock_monotonic_ns();
        const int64_t diff = (got > expected) ? (got - expected) : (expected - got);
        Expect(got > 0, "monotonic clock is positive (no int64 overflow)");
        // The two reads happen microseconds apart; 10 ms slack is generous.
        Expect(diff < INT64_C(10000000),
               "monotonic clock agrees with an independent QPC computation");
    }
#endif

    //--- 8. environment: roundtrip, overwrite guard, delete semantics ------
    const char* kEnvName = "IL2CPP_PLATFORM_TEST_ENV";
    Expect(plat_env_set(kEnvName, "first", true), "env set succeeds");
    Expect(plat_env_get(kEnvName) == "first", "env get returns the set value");
    Expect(plat_env_set(kEnvName, "second", false), "env set (no overwrite) reports ok");
    Expect(plat_env_get(kEnvName) == "first", "overwrite=false kept the old value");
    Expect(plat_env_set(kEnvName, "second", true), "env set (overwrite) succeeds");
    Expect(plat_env_get(kEnvName) == "second", "overwrite=true replaced the value");
    Expect(plat_env_set(kEnvName, nullptr, true), "null value deletes the variable");
    Expect(plat_env_get(kEnvName).empty(), "env get is empty after null delete");
    Expect(plat_env_set(kEnvName, "x", true) && plat_env_set(kEnvName, "", true),
           "empty value delete sequence succeeds");
    Expect(plat_env_get(kEnvName).empty(), "env get is empty after empty delete");

    // 8b. UTF-8 non-ASCII value round-trips EXACTLY. The Windows A-variants
    // (GetEnvironmentVariableA/SetEnvironmentVariableA) interpret bytes
    // through the ANSI code page and mangle non-ASCII UTF-8 values on any
    // non-UTF-8 codepage; the W-variants the platform layer bridges keep
    // the exact bytes ("hello" = E4 BD A0 E5 A5 BD).
    {
        const char* kU8Env = "IL2CPP_PLATFORM_TEST_ENV_U8";
        const char* u8Value = "\xE4\xBD\xA0\xE5\xA5\xBD"; // "hello"
        Expect(plat_env_set(kU8Env, u8Value, true), "utf8 env set succeeds");
        Expect(plat_env_get(kU8Env) == u8Value, "utf8 env value round-trips exactly");
        Expect(plat_env_set(kU8Env, "", true), "utf8 env empty deletes");
        Expect(plat_env_get(kU8Env).empty(), "utf8 env is empty after delete");
    }

    //--- 9. dynamic library: open + symbol resolution ----------------------
#if defined(_WIN32)
    void* lib = plat_dl_open("kernel32.dll");
    const void* sym = lib != nullptr ? plat_dl_sym(lib, "GetCurrentProcessId") : nullptr;
#else
    void* lib = plat_dl_open("libc.so.6");
    const void* sym = lib != nullptr ? plat_dl_sym(lib, "getpid") : nullptr;
#endif
    Expect(lib != nullptr, "dl_open resolves the OS library");
    Expect(sym != nullptr, "dl_sym resolves a known symbol");
    Expect(plat_dl_sym(lib, "no_such_symbol_xyz") == nullptr,
           "dl_sym returns null for an unknown symbol");

    //--- 9b. Windows-style .dll name maps to the local convention ----------
    // DllImport attributes carry the original "*.dll" name; plat_dl_open
    // maps it to the host convention (lib<base>.so / <base>.so -- with
    // .dylib candidates FIRST on Apple, G3). Bare .so names only resolve
    // when the library's dev name is in the loader path (linker-script
    // .so files do NOT load), so the assertion is conditional: when the
    // mapped .so loads, the .dll map MUST have produced it too.
#if defined(__linux__) || defined(__ANDROID__) || defined(__OpenBSD__)
    void* mappedDll = plat_dl_open("libm.dll");
    if (mappedDll == nullptr)
    {
        void* mappedSo = plat_dl_open("libm.so");
        Expect(mappedSo == nullptr,
               "a loadable libm.so implies the .dll name must map to it (no regression)");
    }
#endif

    //--- 9c. versioned soname resolves directly (POSIX runtime only) -------
    // A versioned soname (libm.so.6) is a real loadable object on glibc
    // systems (the dev-name libm.so is a linker script that dlopen cannot
    // load), so the raw-name path is exercised against a real library.
#if defined(__linux__) || defined(__ANDROID__) || defined(__OpenBSD__)
    void* libm = plat_dl_open("libm.so.6");
    Expect(libm != nullptr, "dl_open resolves the versioned soname libm.so.6");
    if (libm != nullptr)
    {
        Expect(plat_dl_sym(libm, "pow") != nullptr,
               "dl_sym resolves pow from the versioned libm");
    }
#endif

    //--- 10. process: pid + cpu count --------------------------------------
    Expect(plat_process_id() > 0, "process id is positive");
    Expect(plat_cpu_count() >= 1, "cpu count is at least 1");
#if defined(_WIN32)
    // GetSystemInfo.dwNumberOfProcessors is capped at 64 (one processor
    // group); plat_cpu_count must report AT LEAST that (GetActiveProcessorCount
    // across ALL_PROCESSOR_GROUPS counts >64-core machines correctly).
    {
        SYSTEM_INFO si;
        ::GetSystemInfo(&si);
        Expect(plat_cpu_count() >= si.dwNumberOfProcessors,
               "cpu count is not capped below the GetSystemInfo processor count");
    }
#endif

    //--- 11. directory roundtrip (tmpdir-based unique dir) -----------------
    const std::string dir = UniqueTestDir();
    Expect(plat_dir_mkdir(dir.c_str()), "mkdir creates the test dir");
    Expect(plat_dir_exists(dir.c_str()), "dir exists after mkdir");
    Expect(plat_path_is_dir(dir.c_str()), "path is a directory");
    Expect(plat_dir_mkdir(dir.c_str()), "repeated mkdir (EEXIST) is not an error");
    Expect(plat_dir_rmdir(dir.c_str()), "rmdir removes the test dir");
    Expect(!plat_dir_exists(dir.c_str()), "dir is gone after rmdir");
    Expect(!plat_dir_rmdir(dir.c_str()), "rmdir of a missing dir reports failure");

    // 11b. UTF-8 non-ASCII directory name round-trips EXACTLY. The Windows
    // narrow CRT (_stat/_mkdir/_rmdir) would read the UTF-8 bytes through
    // the ANSI code page and mangle (or miss) non-ASCII names; the wide CRT
    // (_wstat/_wmkdir/_wrmdir) creates and probes by the exact name
    // ("data" = E6 95 B0 E6 8D AE).
    {
        const std::string u8Dir = UniqueTestDir() + "_u8";
        const std::string u8Suffix = "\xE6\x95\xB0\xE6\x8D\xAE"; // data
        const std::string u8Path = u8Dir + u8Suffix;
        Expect(plat_dir_mkdir(u8Path.c_str()), "utf8 dir mkdir succeeds");
        Expect(plat_dir_exists(u8Path.c_str()), "utf8 dir exists after mkdir");
        Expect(plat_path_is_dir(u8Path.c_str()), "utf8 dir is a directory");
        Expect(plat_dir_rmdir(u8Path.c_str()), "utf8 dir rmdir removes it");
        Expect(!plat_dir_exists(u8Path.c_str()), "utf8 dir is gone after rmdir");
    }

    //--- 12. paths: tmpdir + full path -------------------------------------
    const char* tmpdir = plat_path_tmpdir();
    const size_t tmpLen = std::strlen(tmpdir);
    Expect(tmpLen > 0, "tmpdir is non-empty");
    Expect(tmpdir[tmpLen - 1] != '/' && tmpdir[tmpLen - 1] != '\\',
           "tmpdir has no trailing separator");
    std::string full;
    Expect(plat_path_full(".", full) && !full.empty(), "path_full resolves '.'");
#if defined(_WIN32)
    Expect(full.size() >= 3 && full[1] == ':', "resolved path is drive-absolute");
#else
    Expect(!full.empty() && full[0] == '/', "resolved path is root-absolute");
#endif
    Expect(plat_path_is_dir(full.c_str()), "resolved path is the cwd directory");

    //--- summary ------------------------------------------------------------
    if (g_failures.load() == 0)
    {
        std::printf("all checks passed\n");
        return 0;
    }
    std::printf("%d check(s) failed\n", g_failures.load());
    return 1;
}
