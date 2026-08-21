//------------------------------------------------------------------------------
// pinvoke.cpp -- P/Invoke dynamic resolution (see il2cpp_pinvoke.h).
//
// Cache: a flat static table of {dll, entry, symbol} triples. Static storage
// (constraint #12 -- no global-constructor allocation); the table is small
// (bounded by the number of distinct P/Invoke entries the transpiler emits).
// The symbol pointer is a void* (cast to the calling convention by the
// generated call site).
//
// Platform split: library loading and symbol resolution are delegated to the
// platform layer (plat_dl_open / plat_dl_sym -- platform.h), which owns the
// per-platform branches AND the POSIX ".dll" -> local-convention name mapping
// (design doc section 7.2). Only last-error capture keeps an inline branch
// (GetLastError on Windows / errno on POSIX).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke.h"

#include "il2cpp_string.h"
#include "platform.h" // plat_dl_open / plat_dl_sym (the platform layer owns
                      // the per-platform branches and the POSIX .dll name
                      // mapping -- design doc section 7.2)

#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
    // Windows last-error capture (il2cpp_pinvoke_capture_last_error) reads
    // GetLastError directly; library loading now lives in the platform layer
    // (platform_win.cpp).
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp {

namespace {

// Flat cache: bounded static table (constraint #12). Overflow aborts.
constexpr int32_t kMaxPinvokeEntries = 256;

struct PinvokeEntry
{
    const char* dll;   // library name ("mylib")
    const char* entry; // symbol name ("add_ints")
    void*       symbol; // resolved pointer (null until resolved)
};

PinvokeEntry g_pinvokeCache[kMaxPinvokeEntries];
int32_t      g_pinvokeCount = 0;

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

// Look up a cached symbol; returns null when not cached.
void* LookupCached(const char* dll, const char* entry)
{
    for (int32_t i = 0; i < g_pinvokeCount; ++i)
    {
        if (std::strcmp(g_pinvokeCache[i].dll, dll) == 0
            && std::strcmp(g_pinvokeCache[i].entry, entry) == 0)
        {
            return g_pinvokeCache[i].symbol;
        }
    }
    return nullptr;
}

// Load (or fetch cached) a library handle.
void* GetLibraryHandle(const char* dll)
{
    for (int32_t i = 0; i < g_libraryCount; ++i)
    {
        if (std::strcmp(g_libraryHandles[i].dll, dll) == 0)
        {
            return g_libraryHandles[i].handle;
        }
    }
    void* h = runtime::plat_dl_open(dll);
    if (g_libraryCount >= kMaxPinvokeEntries)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_resolve: library handle table overflow\n");
        std::abort();
    }
    g_libraryHandles[g_libraryCount].dll = dll;
    g_libraryHandles[g_libraryCount].handle = h;
    ++g_libraryCount;
    return h;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_pinvoke_resolve -- resolve a native symbol (see header).
//------------------------------------------------------------------------------
void* il2cpp_pinvoke_resolve(const char* dll, const char* entry)
{
    if (dll == nullptr || entry == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_resolve: null dll/entry\n");
        std::abort();
    }
    void* cached = LookupCached(dll, entry);
    if (cached != nullptr)
    {
        return cached;
    }
    void* handle = GetLibraryHandle(dll);
    if (handle == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_resolve: cannot load library '%s'\n", dll);
        std::abort();
    }
    void* symbol = runtime::plat_dl_sym(handle, entry);
    if (symbol == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_resolve: symbol '%s' not found in '%s'\n", entry, dll);
        std::abort();
    }
    if (g_pinvokeCount >= kMaxPinvokeEntries)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_resolve: cache overflow\n");
        std::abort();
    }
    g_pinvokeCache[g_pinvokeCount].dll = dll;
    g_pinvokeCache[g_pinvokeCount].entry = entry;
    g_pinvokeCache[g_pinvokeCount].symbol = symbol;
    ++g_pinvokeCount;
    return symbol;
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
