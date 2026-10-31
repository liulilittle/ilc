//------------------------------------------------------------------------------
// pinvoke.cpp -- P/Invoke dynamic resolution (see il2cpp_pinvoke.h).
//
// Cache: a flat static table of {dll, entry, charSet, exactSpelling, symbol}
// records. Static storage (constraint #12 -- no global-constructor allocation);
// the table is bounded by the number of distinct P/Invoke entries the
// transpiler emits. The symbol pointer is a void* (cast to the calling
// convention by the generated call site). All table access is serialized by a
// function-local std::mutex: every generated thread can reach a call site.
//
// Platform split: library loading and symbol resolution are delegated to the
// platform layer (plat_dl_open / plat_dl_sym -- platform.h), which owns the
// per-platform branches AND the POSIX ".dll" -> local-convention name mapping
// (design doc section 7.2). Only last-error capture keeps an inline branch
// (GetLastError on Windows / errno on POSIX).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke.h"

#include "il2cpp_exception.h"
#include "il2cpp_string.h"
#include "platform.h" // plat_dl_open / plat_dl_sym (the platform layer owns
                      // the per-platform branches and the POSIX .dll name
                      // mapping -- design doc section 7.2)

#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

#if defined(_WIN32)
    // Windows last-error capture (il2cpp_pinvoke_capture_last_error) reads
    // GetLastError directly; library loading now lives in the platform layer
    // (platform_win.cpp).
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp {

namespace {

// Flat cache: bounded static table (constraint #12). Overflow is NOT fatal --
// the resolved symbol is simply not cached (see StoreCached).
//
// Capacity: every Windows system-DLL [DllImport] now routes through the
// resolver (the CharSet/A-W probe needs GetProcAddress, exactly like net48),
// so the WinForms surface alone contributes ~360 distinct entries. 4096 leaves
// generous headroom for an app that imports from several modules.
constexpr int32_t kMaxPinvokeEntries = 4096;

struct PinvokeEntry
{
    const char* dll;   // library name ("mylib")
    const char* entry; // ORIGINAL [DllImport] entry name ("MessageBox")
    int32_t     charSet;       // MethodImportAttributes CharSet mask (0x00/0x02/0x04/0x06)
    int32_t     exactSpelling; // 0 = probe suffixes, 1 = resolve verbatim
    void*       symbol; // resolved pointer (null until resolved)
};

PinvokeEntry g_pinvokeCache[kMaxPinvokeEntries];
int32_t      g_pinvokeCount = 0;

// The tables are touched from every generated thread (a P/Invoke call site can
// run on any thread), so every read/modify of g_pinvokeCount and of an entry
// happens under this lock. Function-local static: no global-constructor
// allocation (constraint #12) and thread-safe lazy initialization.
std::mutex& PinvokeLock()
{
    static std::mutex s_lock;
    return s_lock;
}

// Captured OS error code (SetLastError support, M4 Task 4).
// net48 semantics: Marshal.GetLastWin32Error is PER-THREAD (.NET keeps the
// captured OS error in thread-local storage), so the slot is thread_local --
// two threads doing SetLastError=true P/Invokes never clobber each other
// (the established runtime pattern: com.cpp/exceptionext.cpp/threadpool.cpp).
thread_local int32_t g_lastError = 0;

// A library HANDLE (void* from plat_dl_open -- HMODULE on Windows, dlopen
// handle on POSIX) per distinct library, cached alongside.
struct LibraryHandle
{
    const char* dll;
    void*       handle; // HMODULE (Windows) / void* (POSIX)
};
LibraryHandle g_libraryHandles[kMaxPinvokeEntries];
int32_t       g_libraryCount = 0;

//------------------------------------------------------------------------------
// P-D4 failed-resolution semantics: a missing LIBRARY throws the managed
// DllNotFoundException carrier, a missing SYMBOL the EntryPointNotFoundException
// carrier (net48 parity -- both are catchable managed exceptions). The message
// is composed into a thread-local scratch (DotNetException carries pointers
// WITHOUT copying -- the same lifetime contract as exceptionext.cpp's
// ComposeMessage; a later composed-message call overwrites the scratch, so a
// catch handler reads it before the next throw). NULL arguments and table
// OVERFLOW keep the abort: they are internal/programming errors with no
// net48 counterpart (the entry cannot be a library-load outcome).
//------------------------------------------------------------------------------
thread_local char g_resolveMessage[160];

const char* kDllNotFoundChain =
    "System.DllNotFoundException,System.DllNotFoundException,System.SystemException,System.Exception";
const char* kEntryPointNotFoundChain =
    "System.EntryPointNotFoundException,System.EntryPointNotFoundException,System.SystemException,System.Exception";

// Compose "prefix<name>suffix" into the thread-local scratch (manual bounded
// copy -- avoids -Wformat-truncation, matches the exceptionext.cpp pattern).
char* ComposeResolveMessage(const char* prefix, const char* name, const char* suffix)
{
    char* p = g_resolveMessage;
    const char* const end = g_resolveMessage + sizeof(g_resolveMessage) - 1;
    while (*prefix != '\0' && p < end)
    {
        *p++ = *prefix++;
    }
    while (*name != '\0' && p < end)
    {
        *p++ = *name++;
    }
    while (*suffix != '\0' && p < end)
    {
        *p++ = *suffix++;
    }
    *p = '\0';
    return g_resolveMessage;
}

// Look up a cached symbol; returns null when not cached. The key is
// (dll, entry, charSet, exactSpelling) -- see the D1 note in the header: two
// imports that differ only in the import flags must never share an entry.
void* LookupCached(const char* dll, const char* entry, int32_t charSet, int32_t exactSpelling)
{
    std::lock_guard<std::mutex> guard(PinvokeLock());
    for (int32_t i = 0; i < g_pinvokeCount; ++i)
    {
        const PinvokeEntry& e = g_pinvokeCache[i];
        if (e.charSet == charSet && e.exactSpelling == exactSpelling
            && std::strcmp(e.dll, dll) == 0 && std::strcmp(e.entry, entry) == 0)
        {
            return e.symbol;
        }
    }
    return nullptr;
}

// One-shot overflow diagnostics (guarded by PinvokeLock).
bool g_pinvokeCacheOverflowReported = false;
bool g_libraryTableOverflowReported = false;

// Cache a resolved symbol and return the value every caller must use: a
// concurrent caller that stored this key first wins, so the whole process
// observes a single symbol per key. A FULL table degrades to "resolved but
// uncached" -- aborting here would turn a successful resolution into a crash
// (the old behaviour), which no net48 scenario has.
void* StoreCached(const char* dll, const char* entry, int32_t charSet, int32_t exactSpelling, void* symbol)
{
    std::lock_guard<std::mutex> guard(PinvokeLock());
    for (int32_t i = 0; i < g_pinvokeCount; ++i)
    {
        const PinvokeEntry& e = g_pinvokeCache[i];
        if (e.charSet == charSet && e.exactSpelling == exactSpelling
            && std::strcmp(e.dll, dll) == 0 && std::strcmp(e.entry, entry) == 0)
        {
            return e.symbol;
        }
    }
    if (g_pinvokeCount >= kMaxPinvokeEntries)
    {
        if (!g_pinvokeCacheOverflowReported)
        {
            g_pinvokeCacheOverflowReported = true;
            std::fprintf(stderr,
                         "il2cpp_pinvoke_resolve: cache full (%d entries) -- further symbols resolve "
                         "uncached (call still succeeds)\n",
                         static_cast<int>(kMaxPinvokeEntries));
        }
        return symbol;
    }
    PinvokeEntry& slot = g_pinvokeCache[g_pinvokeCount];
    slot.dll = dll;
    slot.entry = entry;
    slot.charSet = charSet;
    slot.exactSpelling = exactSpelling;
    slot.symbol = symbol;
    ++g_pinvokeCount;
    return symbol;
}

// Load (or fetch cached) a library handle. plat_dl_open runs OUTSIDE the lock
// (it can block on the loader); the table update re-checks under the lock so a
// racing loader does not duplicate the entry.
void* GetLibraryHandle(const char* dll)
{
    {
        std::lock_guard<std::mutex> guard(PinvokeLock());
        for (int32_t i = 0; i < g_libraryCount; ++i)
        {
            if (std::strcmp(g_libraryHandles[i].dll, dll) == 0)
            {
                return g_libraryHandles[i].handle;
            }
        }
    }
    void* h = runtime::plat_dl_open(dll);
    std::lock_guard<std::mutex> guard(PinvokeLock());
    for (int32_t i = 0; i < g_libraryCount; ++i)
    {
        if (std::strcmp(g_libraryHandles[i].dll, dll) == 0)
        {
            return g_libraryHandles[i].handle;
        }
    }
    if (g_libraryCount >= kMaxPinvokeEntries)
    {
        if (!g_libraryTableOverflowReported)
        {
            g_libraryTableOverflowReported = true;
            std::fprintf(stderr,
                         "il2cpp_pinvoke_resolve: library handle table full (%d entries) -- "
                         "further libraries load uncached\n",
                         static_cast<int>(kMaxPinvokeEntries));
        }
        return h;
    }
    g_libraryHandles[g_libraryCount].dll = dll;
    g_libraryHandles[g_libraryCount].handle = h;
    ++g_libraryCount;
    return h;
}

// `entry` + one suffix character, into a bounded stack buffer. An over-long
// entry name yields no suffixed candidate (the bare name was already tried).
void* TrySuffixedSymbol(void* handle, const char* entry, char suffix)
{
    char buffer[512];
    const size_t n = std::strlen(entry);
    if (n + 2 > sizeof(buffer))
    {
        return nullptr;
    }
    std::memcpy(buffer, entry, n);
    buffer[n] = suffix;
    buffer[n + 1] = '\0';
    return runtime::plat_dl_sym(handle, buffer);
}

// Shared resolution body (no lock held across a throw).
void* ResolveCore(const char* dll, const char* entry, int32_t charSet, int32_t exactSpelling)
{
    // Null dll/entry: an internal/programming error (the transpiler always
    // emits literals) -- no net48 scenario; abort is intentional.
    if (dll == nullptr || entry == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_resolve: null dll/entry\n");
        std::abort();
    }
    void* cached = LookupCached(dll, entry, charSet, exactSpelling);
    if (cached != nullptr)
    {
        return cached;
    }
    // P-D4: a failed library load is a managed DllNotFoundException (net48
    // parity -- catchable by the generated try/catch through the type chain).
    void* handle = GetLibraryHandle(dll);
    if (handle == nullptr)
    {
        il2cpp_throw_new(kDllNotFoundChain,
                         ComposeResolveMessage("Unable to load DLL '", dll, "'."));
    }
    // net48 candidate list (see the header): the exact-spelling flag disables
    // probing entirely; otherwise the CharSet decides whether the "A" arm is
    // part of the chain. The bare name is always tried first.
    void* symbol = runtime::plat_dl_sym(handle, entry);
    if (symbol == nullptr && exactSpelling == 0)
    {
        const bool ansiFamily = charSet == 0x00 || charSet == 0x02;
        if (ansiFamily)
        {
            symbol = TrySuffixedSymbol(handle, entry, 'A');
        }
        if (symbol == nullptr)
        {
            symbol = TrySuffixedSymbol(handle, entry, 'W');
        }
    }
    if (symbol == nullptr)
    {
        // P-D4: a missing symbol is a managed EntryPointNotFoundException.
        // The message quotes the ORIGINAL entry name -- net48 never reports the
        // probed name (measured: "...named 'MessageBox'", not 'MessageBoxW').
        il2cpp_throw_new(kEntryPointNotFoundChain,
                         ComposeResolveMessage("Unable to find an entry point named '", entry, "'."));
    }
    return StoreCached(dll, entry, charSet, exactSpelling, symbol);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_pinvoke_resolve -- resolve a native symbol (see header).
//------------------------------------------------------------------------------
void* il2cpp_pinvoke_resolve(const char* dll, const char* entry)
{
    // Legacy two-argument form: verbatim resolution (exactSpelling = 1), i.e.
    // exactly the pre-probing behaviour those call sites were written for.
    return ResolveCore(dll, entry, 0x02, 1);
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_resolve_probe -- CharSet/ExactSpelling-aware resolution.
//------------------------------------------------------------------------------
void* il2cpp_pinvoke_resolve_probe(const char* dll, const char* entry, int32_t charSet,
                                   int32_t exactSpelling)
{
    return ResolveCore(dll, entry, charSet, exactSpelling != 0 ? 1 : 0);
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_to_utf8 -- managed string -> UTF-8 buffer (see header).
//
// M3 string payload is UTF-16; the Ansi marshalling transcodes to UTF-8 into
// a rotating runtime-owned buffer (valid until the next P/Invoke string
// marshalling -- the callee consumes it synchronously).
//------------------------------------------------------------------------------
const char* il2cpp_pinvoke_string_to_utf8(Il2CppObject* s)
{
    // net48 semantics: Ansi marshalling is per-thread (the runtime's rotating
    // slot never crosses a thread boundary), so the buffer is thread_local.
    thread_local char g_marshalBuffer[4096];
    Il2CppString* str = reinterpret_cast<Il2CppString*>(s);
    if (str == nullptr)
    {
        return "";
    }
    size_t o = 0;
    for (int32_t i = 0; i < str->length && o + 4 < sizeof(g_marshalBuffer); ++i)
    {
        const char16_t c = str->chars[i];
        // Surrogate PAIR: high + low -> one 4-byte astral character (the
        // identical codec as il2cpp_pinvoke_string_to_utf8_alloc -- encoding
        // the two surrogate units separately would emit ill-formed UTF-8,
        // which the .NET Ansi marshaller never produces).
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < str->length)
        {
            const char16_t lo = str->chars[i + 1];
            if (lo >= 0xDC00 && lo <= 0xDFFF)
            {
                const uint32_t cp = 0x10000u
                    + ((static_cast<uint32_t>(c) - 0xD800u) << 10)
                    + (static_cast<uint32_t>(lo) - 0xDC00u);
                g_marshalBuffer[o++] = static_cast<char>(0xF0u | (cp >> 18));
                g_marshalBuffer[o++] = static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu));
                g_marshalBuffer[o++] = static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu));
                g_marshalBuffer[o++] = static_cast<char>(0x80u | (cp & 0x3Fu));
                ++i; // consume the low surrogate
                continue;
            }
        }
        if (c >= 0xD800 && c <= 0xDFFF)
        {
            // Lone (unpaired) surrogate: replacement character U+FFFD (.NET
            // marshalling semantics -- never ill-formed UTF-8).
            g_marshalBuffer[o++] = static_cast<char>(0xEFu);
            g_marshalBuffer[o++] = static_cast<char>(0xBFu);
            g_marshalBuffer[o++] = static_cast<char>(0xBDu);
            continue;
        }
        if (c < 0x80)
        {
            g_marshalBuffer[o++] = static_cast<char>(c);
        }
        else if (c < 0x800)
        {
            g_marshalBuffer[o++] = static_cast<char>(0xC0 | (c >> 6));
            g_marshalBuffer[o++] = static_cast<char>(0x80 | (c & 0x3F));
        }
        else
        {
            g_marshalBuffer[o++] = static_cast<char>(0xE0 | (c >> 12));
            g_marshalBuffer[o++] = static_cast<char>(0x80 | ((c >> 6) & 0x3F));
            g_marshalBuffer[o++] = static_cast<char>(0x80 | (c & 0x3F));
        }
    }
    g_marshalBuffer[o] = '\0';
    return g_marshalBuffer;
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_from_utf8 -- UTF-8 buffer -> managed string (see header).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_pinvoke_string_from_utf8(const char* utf8)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(utf8 != nullptr ? utf8 : ""));
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_capture_last_error -- capture the OS error code after a
// SetLastError=true P/Invoke call (GetLastError on Windows, errno on POSIX).
//------------------------------------------------------------------------------
void il2cpp_pinvoke_capture_last_error()
{
#if defined(_WIN32)
    g_lastError = static_cast<int32_t>(GetLastError());
#else
    g_lastError = errno;
#endif
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_get_last_error -- the captured error code (Marshal.
// GetLastWin32Error).
//------------------------------------------------------------------------------
int32_t il2cpp_pinvoke_get_last_error()
{
    return g_lastError;
}

} // namespace il2cpp
