//------------------------------------------------------------------------------
// rng.cpp -- CSPRNG implementation (see rng.h).
//
// Platform dispatch (same per-platform-TU pattern as platform_win.cpp, but
// self-contained in this TU so the GLOB-based build picks it up anywhere):
//
//   Windows:
//     primary   BCryptGenRandom(bcrypt.dll) -- loaded at runtime via
//               LoadLibraryA + GetProcAddress (guid.cpp precedent): no
//               link-time -lbcrypt, so no shared-CMakeLists change.
//     fallback  RtlGenRandom (SystemFunction036, advapi32.dll) -- present on
//               every Windows since XP; also runtime-resolved.
//   POSIX:
//     primary   getrandom(2) (Linux 3.17+, EXCLUDING Android: Bionic defines
//               __linux__ but getrandom only becomes visible at
//               __ANDROID_API__ >= 28, above the android-24 design baseline,
//               so the guard is __linux__ && !__ANDROID__; chunked to <=
//               1 MiB per call, the man-page bound for a single call is
//               33554431 bytes).
//     primary   arc4random_buf (Apple + BSD family -- __APPLE__, __FreeBSD__,
//               __OpenBSD__, __NetBSD__, __DragonFlyBSD__: the platform libc
//               CSPRNG where getrandom(2) does not exist; fills the whole
//               buffer in one call and never fails -- a failed seed aborts
//               inside libc, so a returning call always holds OS bytes).
//     fallback  /dev/urandom read loop (all POSIX, including Android and
//               non-Linux platforms).
//
// The red line is enforced structurally: this file contains NO rand(), NO
// LCG, no deterministic PRNG of any kind. Every byte comes from the OS.
// Backend failure (all providers unavailable) throws CryptographicException
// (loud) -- never a silent degradation to weak randomness.
//------------------------------------------------------------------------------

#include "rng.h"

#include "il2cpp_exception_ext.h"

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#else
    #include <cerrno>
    #include <fcntl.h>
    #include <unistd.h>
    #if defined(__linux__) && !defined(__ANDROID__)
        #include <sys/random.h>
    #endif
    #if defined(__APPLE__) || defined(__FreeBSD__) || defined(__OpenBSD__) || \
        defined(__NetBSD__) || defined(__DragonFlyBSD__)
        #include <stdlib.h>   // arc4random_buf
    #endif
#endif

namespace il2cpp { namespace runtime {

namespace {

// Exception type-name chain for System.Security.Cryptography.CryptographicException
// (il2cpp_exception.h carrier format -- aes.cpp carries the same constant;
// the two modules keep their own copy because the shared factory header
// (il2cpp_exception_ext.h) is out of scope for this task).
const char kCryptoChain[] =
    "System.Security.Cryptography.CryptographicException,"
    "System.Security.Cryptography.CryptographicException,"
    "System.SystemException,System.Exception";

#if defined(_WIN32)

// Minimal BCryptGenRandom / SystemFunction036 prototypes. The real headers
// (bcrypt.h/ntsecapi.h) are avoided so the TU builds on any MinGW w64
// snapshot -- we only need the call shape, resolved at runtime.
typedef LONG         BcryptStatus;     // NTSTATUS
typedef void*        BcryptAlgHandle;  // BCRYPT_ALG_HANDLE
typedef BcryptStatus (WINAPI* BcryptGenRandomFn)(BcryptAlgHandle, uint8_t*, uint32_t, uint32_t);
typedef uint8_t      (WINAPI* RtlGenRandomFn)(void*, uint32_t); // BOOLEAN

const uint32_t kBcryptUseSystemPreferredRng = 0x2u; // BCRYPT_USE_SYSTEM_PREFERRED_RNG
const BcryptStatus kStatusSuccess = 0;               // STATUS_SUCCESS

// Resolve BCryptGenRandom from bcrypt.dll once (thread-safe magic static);
// returns null when the library or entry point is unavailable.
BcryptGenRandomFn BcryptGenRandomProc()
{
    static BcryptGenRandomFn proc = []() -> BcryptGenRandomFn {
        HMODULE h = LoadLibraryA("bcrypt.dll");
        if (h == nullptr)
        {
            return nullptr;
        }
        return reinterpret_cast<BcryptGenRandomFn>(reinterpret_cast<void*>(GetProcAddress(h, "BCryptGenRandom")));
    }();
    return proc;
}

// Resolve SystemFunction036 (RtlGenRandom) from advapi32.dll once.
RtlGenRandomFn RtlGenRandomProc()
{
    static RtlGenRandomFn proc = []() -> RtlGenRandomFn {
        HMODULE h = LoadLibraryA("advapi32.dll");
        if (h == nullptr)
        {
            return nullptr;
        }
        return reinterpret_cast<RtlGenRandomFn>(reinterpret_cast<void*>(GetProcAddress(h, "SystemFunction036")));
    }();
    return proc;
}

#else // !_WIN32

// Single /dev/urandom descriptor shared across calls (never closed -- the
// process-lifetime file descriptor pattern).
int UrandomFd()
{
    static int fd = []() -> int {
        return open("/dev/urandom", O_RDONLY);
    }();
    return fd;
}

#endif

} // namespace

//------------------------------------------------------------------------------
// il2cpp_rng_fill -- OS CSPRNG fill with provider fallback (see rng.h).
//------------------------------------------------------------------------------
void il2cpp_rng_fill(uint8_t* bytes, size_t len)
{
    if (bytes == nullptr && len > 0)
    {
        il2cpp_throw_argument_null_exception("bytes");
    }
    if (len == 0)
    {
        return;
    }

#if defined(_WIN32)

    // Chunk at 1 GiB: BCryptGenRandom takes a ULONG length; the chunking
    // also keeps RtlGenRandom well inside its documented per-call envelope.
    const size_t kChunk = static_cast<size_t>(1) << 30;
    size_t filled = 0;

    BcryptGenRandomFn bcrypt = BcryptGenRandomProc();
    if (bcrypt != nullptr)
    {
        while (filled < len)
        {
            const size_t n = (len - filled) < kChunk ? (len - filled) : kChunk;
            if (bcrypt(nullptr, bytes + filled, static_cast<uint32_t>(n),
                       kBcryptUseSystemPreferredRng) == kStatusSuccess)
            {
                filled += n;
                continue;
            }
            break; // provider failed mid-fill: fall through to the next one
        }
        if (filled == len)
        {
            return;
        }
    }

    RtlGenRandomFn rtl = RtlGenRandomProc();
    if (rtl != nullptr)
    {
        while (filled < len)
        {
            const size_t n = (len - filled) < kChunk ? (len - filled) : kChunk;
            if (rtl(bytes + filled, static_cast<uint32_t>(n)) != 0u)
            {
                filled += n;
                continue;
            }
            break;
        }
        if (filled == len)
        {
            return;
        }
    }

#else // !_WIN32

    size_t filled = 0;

#if defined(__linux__) && !defined(__ANDROID__)
    // getrandom(2) primary (Linux 3.17+, not Android -- see the header
    // comment): one call may return short, so loop; EINTR retries; capped
    // at 1 MiB per call per the man page.
    const size_t kChunk = static_cast<size_t>(1) << 20;
    while (filled < len)
    {
        const size_t n = (len - filled) < kChunk ? (len - filled) : kChunk;
        const ssize_t r = getrandom(bytes + filled, n, 0);
        if (r > 0)
        {
            filled += static_cast<size_t>(r);
            continue;
        }
        if (r < 0 && errno == EINTR)
        {
            continue;
        }
        break; // getrandom unavailable or failed: fall through to /dev/urandom
    }
    if (filled == len)
    {
        return;
    }
#endif // __linux__ && !__ANDROID__

#if defined(__APPLE__) || defined(__FreeBSD__) || defined(__OpenBSD__) || \
    defined(__NetBSD__) || defined(__DragonFlyBSD__)
    // arc4random_buf primary (Apple + BSD family): the libc CSPRNG fills the
    // entire buffer in one call and never returns failure (a bad seed aborts
    // inside libc, so a returning call always holds fresh OS bytes). No chunk
    // bound exists for arc4random_buf (unlike the getrandom(2) man-page cap),
    // so one call covers the whole remaining span.
    arc4random_buf(bytes + filled, len - filled);
    return;
#endif // Apple / BSD arc4random_buf

    // /dev/urandom fallback (all POSIX).
    const int fd = UrandomFd();
    if (fd >= 0)
    {
        while (filled < len)
        {
            const ssize_t r = read(fd, bytes + filled, len - filled);
            if (r > 0)
            {
                filled += static_cast<size_t>(r);
                continue;
            }
            if (r < 0 && errno == EINTR)
            {
                continue;
            }
            break;
        }
        if (filled == len)
        {
            return;
        }
    }

#endif // _WIN32 / !_WIN32

    // Every provider failed: loud failure, never a weak-RNG fallback.
    il2cpp_throw_new(kCryptoChain,
                     "Unable to obtain random bytes from the operating system.");
}

}} // namespace il2cpp::runtime
