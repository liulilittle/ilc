#if defined(__arm__) || defined(_M_ARM) || defined(__i386__) || defined(_M_IX86)
#error "il2cpp runtime: 32-bit targets are not supported in phase 1 (arm64-v8a/x86_64 only)"
#endif

#pragma once
//------------------------------------------------------------------------------
// platform.h -- the runtime platform-abstraction layer.
//
// Design: docs/design/cross-platform-design.md section 2 (lightweight platform layer). The
// eight domains of scattered system calls (thread/sync/clock/file/dl/env/
// dir/process) are converged into this single interface; business files call
// plat_* only. The header itself is platform-neutral: it includes NO platform
// headers. The two implementation translation units are
//
//   platform_win.cpp   -- _WIN32 implementation
//   platform_posix.cpp -- every other platform (__linux__ / __ANDROID__ /
//                         __APPLE__ / __OpenBSD__; platform differences are
//                         inline #if branches)
//
// Both .cpp files guard their implementation bodies so the translation unit
// is an EMPTY TU on the other platform -- the GLOB-based CMakeLists compiles
// every src/*.cpp on every platform without needing file-list edits
// (encoding.cpp precedent).
//
// Platform-layer contract (all APIs):
//   * Every string parameter is UTF-8 bytes (wchar_t NEVER crosses the
//     interface -- the 32-bit-wchar_t trap on Apple platforms stays inside
//     the platform layer).
//   * Failures: thread spawn returns nullptr; trylock returns false;
//     dl_open returns nullptr; env_set returns false; file/path/dir probes
//     return false with errno/last-error left for the caller to inspect.
//   * Nothing here throws (the layer is exception-neutral; the spawn
//     wrappers swallow thread-function exceptions so they never reach
//     std::terminate).
//------------------------------------------------------------------------------

#include <cstdint>
#include <cstdio>  // FILE
#include <string>

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Opaque handles (the concrete structs live in the platform .cpp files).
//------------------------------------------------------------------------------
struct plat_thread;
struct plat_mutex;
struct plat_cond;

using plat_thread_t = plat_thread*;
using plat_mutex_t = plat_mutex*;
using plat_cond_t = plat_cond*;

// OS thread id. int32_t mirrors the existing icall_thread_get_current_thread_id
// contract (GetCurrentThreadId / SYS_gettid / OpenBSD getthrid are 32-bit;
// Apple's pthread_threadid_np returns 64-bit and is truncated into the same
// width); the remaining non-Linux/Apple/OpenBSD POSIX fallback hashes the
// std::thread id into the same width.
using thread_id_t = int32_t;

//------------------------------------------------------------------------------
// 1. Thread domain.
//------------------------------------------------------------------------------

// Current thread's OS id (nonzero in practice, stable for the process).
thread_id_t plat_thread_current_id();

// Suspend the current thread for 'milliseconds'. 0 yields the time slice
// without sleeping (Windows Sleep(0) / POSIX sched_yield); negative values
// are rejected by the icall layer -- the platform layer treats them as 0.
void plat_thread_sleep(int32_t milliseconds);

// Yield the current time slice to other runnable threads (best effort).
void plat_thread_yield();

// Spawn a detached-joinable thread running fn(arg). Returns nullptr when
// creation fails. The returned handle owns the thread: plat_thread_join
// must be called exactly once, and frees the handle. Exceptions thrown by
// fn are swallowed (the wrapper must not reach std::terminate).
plat_thread_t plat_thread_spawn(void (*fn)(void*), void* arg);

// Block until the spawned thread exits, then release the handle.
void plat_thread_join(plat_thread_t thread);

//------------------------------------------------------------------------------
// 2. Synchronization domain (mutex + condition variable).
//------------------------------------------------------------------------------

plat_mutex_t plat_mutex_init();
void plat_mutex_lock(plat_mutex_t mutex);
// Non-blocking acquire: true when the lock was taken (the caller then owns
// it and must balance with plat_mutex_unlock). NOT reentrant -- locking an
// already-held mutex from the owning thread is undefined behavior.
bool plat_mutex_trylock(plat_mutex_t mutex);
void plat_mutex_unlock(plat_mutex_t mutex);
// Frees the mutex; the caller must not hold it and must not touch the
// handle afterwards.
void plat_mutex_destroy(plat_mutex_t mutex);

plat_cond_t plat_cond_init();
// Atomically releases 'mutex' and blocks until signalled; upon return the
// mutex is re-acquired (the caller owns it). The mutex must be held by the
// caller on entry. spurious wakeups are possible and must be re-checked by
// the caller.
void plat_cond_wait(plat_cond_t cond, plat_mutex_t mutex);
// Wake ONE waiting thread (if any).
void plat_cond_signal(plat_cond_t cond);
// Wake ALL waiting threads (if any).
void plat_cond_broadcast(plat_cond_t cond);
void plat_cond_destroy(plat_cond_t cond);

//------------------------------------------------------------------------------
// 3. High-resolution clock domain.
//------------------------------------------------------------------------------

// Monotonic clock reading in NANOSECONDS (never goes backwards; unaffected
// by wall-clock adjustments).
int64_t plat_clock_monotonic_ns();

// The underlying counter frequency in Hz: QueryPerformanceFrequency on
// Windows, 1'000'000'000 on POSIX (clock_gettime already counts in ns).
int64_t plat_clock_frequency();

//------------------------------------------------------------------------------
// 4. File domain.
//------------------------------------------------------------------------------

// Open 'utf8_path' with 'mode' (fopen semantics; UTF-8 on every platform --
// Windows converts to the wide CRT internally). Returns nullptr on failure.
FILE* plat_file_open(const char* utf8_path, const char* mode);

// Remove the file named 'utf8_path'. True when the file is GONE afterwards
// -- removing a missing file also returns true (the .NET File.Delete
// silent-success semantics the icall layer builds on); a removal failure
// (permissions, sharing, ...) leaves the file and returns false.
bool plat_file_remove(const char* utf8_path);

// Resolve 'utf8_path' to an absolute path into 'out'. Returns false when
// the resolution is impossible. POSIX (realpath) requires the path to
// EXIST; Windows (GetFullPathNameW) does not -- callers must not rely on
// the non-existent-path case until a migration adopts it explicitly.
bool plat_path_full(const char* utf8_path, std::string& out);

// The process temp directory (TMPDIR on POSIX, falling back to
// /data/local/tmp on Android -- which has no /tmp -- and /tmp elsewhere;
// GetTempPathW on Windows). The returned pointer is stable for the process
// lifetime (static buffer, set on first call).
const char* plat_path_tmpdir();

//------------------------------------------------------------------------------
// 5. Dynamic library domain.
//------------------------------------------------------------------------------

// Load a shared library by name. POSIX maps a Windows-style "*.dll" name
// to the local convention (lib<base>.so / <base>.so) when the raw name
// does not load (design doc section 7.2 -- the mapping lives HERE, so the
// transpiler never needs a platform branch). Returns nullptr on failure.
void* plat_dl_open(const char* name);

// Resolve a symbol address inside a previously opened library handle.
// Returns nullptr when the symbol is absent.
void* plat_dl_sym(void* handle, const char* name);

//------------------------------------------------------------------------------
// 6. Environment variable domain.
//------------------------------------------------------------------------------

// Value of the variable, or an EMPTY string when unset (the "unset" and
// "empty" cases are indistinguishable -- matching the .NET Environment
// surface the icall layer builds on).
std::string plat_env_get(const char* name);

// Set (or with overwrite=false keep) the variable. A null or EMPTY value
// DELETES the variable on both platforms (SetEnvironmentVariableA(name,
// nullptr) semantics aligned with setenv/unsetenv -- an empty string is
// not representable as a Windows environment value). Returns false when
// the operation failed.
bool plat_env_set(const char* name, const char* value, bool overwrite);

//------------------------------------------------------------------------------
// 7. Directory domain.
//------------------------------------------------------------------------------

// True when 'path' exists (file or directory).
bool plat_dir_exists(const char* path);
// Create ONE directory level (no parents). True when it now exists.
bool plat_dir_mkdir(const char* path);
// Remove ONE empty directory level. False when it does not exist or is
// not empty.
bool plat_dir_rmdir(const char* path);
// True when 'path' exists AND names a directory.
bool plat_path_is_dir(const char* path);
// True when 'path' exists AND names a regular file (directories, symlinks
// to directories and missing paths probe false -- the .NET File.Exists
// contract). Windows uses the wide stat so UTF-8 paths are exact.
bool plat_path_is_file(const char* path);

//------------------------------------------------------------------------------
// 8. Process domain.
//------------------------------------------------------------------------------

int32_t plat_process_id();
uint32_t plat_cpu_count();
// Human-readable OS version string ("Windows <ver> <build>" / "<sysname>
// <release>"), never null -- a fallback is returned on probe failure.
std::string plat_os_version();

}} // namespace il2cpp::runtime
