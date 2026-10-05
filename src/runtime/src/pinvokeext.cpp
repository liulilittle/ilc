//------------------------------------------------------------------------------
// pinvokeext.cpp -- P/Invoke extension layer (see il2cpp_pinvoke_ext.h).
//
// Complements pinvoke.cpp (read-only, owned elsewhere). This translation
// unit adds:
//   1. Heap-allocated UTF-8 string marshalling (argument direction).
//   2. A static callback registration table (name -> function pointer).
//   3. The SetLastError pre-call OS error slot reset.
//
// Storage is static (constraint #12 -- no global-constructor allocation);
// all tables are bounded flat arrays that abort loudly on overflow, exactly
// like the pinvoke.cpp caches they extend.
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke_ext.h"

#include "il2cpp_array.h"

#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp {

namespace {

// Flat callback table: bounded static storage (constraint #12). Overflow
// aborts loudly (a programming error -- the transpiler emits a bounded set
// of registered callbacks).
constexpr int32_t kMaxCallbacks = 256;

struct CallbackEntry
{
    const char* name; // stable registration name (literal, owned by the caller)
    void*       fn;   // function pointer (cast to the right signature at the call site)
};

CallbackEntry g_callbacks[kMaxCallbacks];
int32_t       g_callbackCount = 0;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_to_utf8_alloc -- managed string -> heap UTF-8 (see
// header for the full contract).
//
// Worst case per UTF-16 unit is 3 output bytes (astral characters occupy TWO
// units and need 4 bytes <= 2*3), so the buffer capacity is 3*length+1.
//------------------------------------------------------------------------------
char* il2cpp_pinvoke_string_to_utf8_alloc(Il2CppString* s)
{
    const size_t capacity = (s != nullptr ? static_cast<size_t>(s->length) : 0) * 3 + 1;
    char* buf = static_cast<char*>(std::malloc(capacity));
    if (buf == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_string_to_utf8_alloc: out of memory\n");
        std::abort();
    }
    size_t o = 0;
    if (s != nullptr)
    {
        for (int32_t i = 0; i < s->length; ++i)
        {
            const char16_t c = s->chars[i];
            // Surrogate PAIR: high + low -> one 4-byte astral character.
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s->length)
            {
                const char16_t lo = s->chars[i + 1];
                if (lo >= 0xDC00 && lo <= 0xDFFF)
                {
                    const uint32_t cp = 0x10000u
                        + ((static_cast<uint32_t>(c) - 0xD800u) << 10)
                        + (static_cast<uint32_t>(lo) - 0xDC00u);
                    buf[o++] = static_cast<char>(0xF0u | (cp >> 18));
                    buf[o++] = static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu));
                    buf[o++] = static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu));
                    buf[o++] = static_cast<char>(0x80u | (cp & 0x3Fu));
                    ++i; // consume the low surrogate
                    continue;
                }
            }
            if (c >= 0xD800 && c <= 0xDFFF)
            {
                // Lone (unpaired) surrogate: encode the replacement character
                // U+FFFD (.NET marshalling semantics), not ill-formed UTF-8.
                buf[o++] = static_cast<char>(0xEFu);
                buf[o++] = static_cast<char>(0xBFu);
                buf[o++] = static_cast<char>(0xBDu);
                continue;
            }
            if (c < 0x80)
            {
                buf[o++] = static_cast<char>(c);
            }
            else if (c < 0x800)
            {
                buf[o++] = static_cast<char>(0xC0u | (c >> 6));
                buf[o++] = static_cast<char>(0x80u | (c & 0x3Fu));
            }
            else
            {
                buf[o++] = static_cast<char>(0xE0u | (c >> 12));
                buf[o++] = static_cast<char>(0x80u | ((c >> 6) & 0x3Fu));
                buf[o++] = static_cast<char>(0x80u | (c & 0x3Fu));
            }
        }
    }
    buf[o] = '\0';
    return buf;
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_utf8_free -- release a buffer from string_to_utf8_alloc.
//------------------------------------------------------------------------------
void il2cpp_pinvoke_utf8_free(char* utf8)
{
    std::free(utf8);
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_to_utf16_alloc -- managed string -> heap UTF-16 (see
// header for the full contract).
//
// The copy is LENGTH-based (s->length units) -- embedded NUL units are
// preserved verbatim, matching .NET's Unicode marshalling. char16_t is
// explicit (NOT wchar_t -- its width differs between Windows 2 and POSIX 4),
// so the buffer is UTF-16 on every platform. Null input marshals as an
// empty allocated buffer (uniform caller-free contract).
//------------------------------------------------------------------------------
char16_t* il2cpp_pinvoke_string_to_utf16_alloc(Il2CppString* s)
{
    const size_t unitCount = (s != nullptr ? static_cast<size_t>(s->length) : 0) + 1;
    char16_t* buf = static_cast<char16_t*>(std::malloc(unitCount * sizeof(char16_t)));
    if (buf == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_string_to_utf16_alloc: out of memory\n");
        std::abort();
    }
    if (s != nullptr && s->length > 0)
    {
        std::memcpy(buf, s->chars, static_cast<size_t>(s->length) * sizeof(char16_t));
    }
    buf[s != nullptr ? s->length : 0] = u'\0';
    return buf;
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_utf16_free -- release a buffer from string_to_utf16_alloc.
//------------------------------------------------------------------------------
void il2cpp_pinvoke_utf16_free(char16_t* utf16)
{
    std::free(utf16);
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_from_utf16 -- null-terminated UTF-16 buffer ->
// managed string (see header). Scans to the first NUL unit
// (PtrToStringUni(IntPtr) semantics).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_pinvoke_string_from_utf16(const char16_t* utf16)
{
    if (utf16 == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(u"", 0));
    }
    int32_t length = 0;
    while (utf16[length] != u'\0')
    {
        ++length;
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(utf16, length));
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_from_utf16_len -- explicit-length UTF-16 buffer ->
// managed string (see header). Copies exactly 'length' units (embedded NUL
// units preserved -- PtrToStringUni(IntPtr, int) semantics); negative length
// clamps to 0; null input decodes as the empty string.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_pinvoke_string_from_utf16_len(const char16_t* utf16, int32_t length)
{
    if (length < 0)
    {
        length = 0;
    }
    if (utf16 == nullptr || length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(u"", 0));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(utf16, length));
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_string_to_utf16_hglobal -- StringToHGlobalUni icall shape
// (see header). The UTF-16 heap buffer rides in an intptr_t pointer carrier
// (the fold's "ptr" result kind). null input -> IntPtr.Zero (0, .NET
// semantics); the buffer is released with icall_marshal_free_hglobal.
//
// On Windows the buffer MUST come from the global heap (GlobalAlloc):
// SetClipboardData requires a GlobalAlloc'd memory object -- the clipboard
// takes ownership and later releases the handle with GlobalFree. A
// std::malloc'd pointer handed to SetClipboardData is freed by GlobalFree
// from a DIFFERENT heap -> heap corruption 0xC0000374 (clipops/winforms1 p12
// defect). GMEM_FIXED (net48 AllocHGlobal -> LocalAlloc LMEM_FIXED) returns a
// DIRECT pointer, so the value stays usable as a plain string pointer
// (winforms1 SendMessageW SB_SETTEXT/WM_SETTEXT, RegisterClassExW
// lpszClassName) AND as a global handle for SetClipboardData/GlobalFree.
// POSIX has no clipboard protocol -> keeps std::malloc/std::free.
//------------------------------------------------------------------------------
intptr_t il2cpp_pinvoke_string_to_utf16_hglobal(Il2CppString* s)
{
    if (s == nullptr)
    {
        return 0;
    }
#if defined(_WIN32)
    const size_t byteCount = (static_cast<size_t>(s->length) + 1) * sizeof(char16_t);
    void* buf = GlobalAlloc(GMEM_FIXED | GMEM_ZEROINIT, byteCount);
    if (buf == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_string_to_utf16_hglobal: out of memory\n");
        std::abort();
    }
    if (s->length > 0)
    {
        std::memcpy(buf, s->chars, static_cast<size_t>(s->length) * sizeof(char16_t));
    }
    static_cast<char16_t*>(buf)[s->length] = u'\0';
    return reinterpret_cast<intptr_t>(buf);
#else
    return reinterpret_cast<intptr_t>(il2cpp_pinvoke_string_to_utf16_alloc(s));
#endif
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_byte_array_data -- byte[] payload address (see header).
//
// The byte[] buffer-parameter marshal (GetKeyboardState(byte[256]) form):
// the generated P/Invoke call site hands the native callee the ADDRESS of
// the array's inline payload, and the callee reads/writes it in place. The
// GC is non-moving across the synchronous native call, so the address is
// stable for exactly the window it is used (the documented pin limitation).
//
// A null array returns nullptr -- .NET marshals a null byte[] argument as
// a NULL pointer to the native callee (the callee's null contract decides
// what happens next), so the helper mirrors that pass-through instead of
// aborting. An array whose element size is not 1 (a non-byte-like array)
// still aborts loudly -- the generated code emits the helper only for
// byte[] parameters, so any other element kind is a transpiler-emission bug.
//------------------------------------------------------------------------------
uint8_t* il2cpp_pinvoke_byte_array_data(Il2CppArray* array)
{
    if (array == nullptr)
    {
        return nullptr;
    }
    if (array->elemSize != 1 || array->isRefElem)
    {
        std::fprintf(stderr,
                     "il2cpp_pinvoke_byte_array_data: not a byte[] (elemSize=%d isRefElem=%d)\n",
                     static_cast<int>(array->elemSize), static_cast<int>(array->isRefElem));
        std::abort();
    }
    return reinterpret_cast<uint8_t*>(array->data);
}

//------------------------------------------------------------------------------
// icall_pinvoke_register_callback -- register (or replace) a callback (see
// header). Duplicate names replace in place so repeated registration of the
// same managed static method converges to the latest entry.
//------------------------------------------------------------------------------
void icall_pinvoke_register_callback(const char* name, void* fn)
{
    if (name == nullptr || fn == nullptr)
    {
        std::fprintf(stderr, "icall_pinvoke_register_callback: null name/fn\n");
        std::abort();
    }
    for (int32_t i = 0; i < g_callbackCount; ++i)
    {
        if (std::strcmp(g_callbacks[i].name, name) == 0)
        {
            g_callbacks[i].fn = fn;
            return;
        }
    }
    if (g_callbackCount >= kMaxCallbacks)
    {
        std::fprintf(stderr, "icall_pinvoke_register_callback: table overflow\n");
        std::abort();
    }
    g_callbacks[g_callbackCount].name = name;
    g_callbacks[g_callbackCount].fn = fn;
    ++g_callbackCount;
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_find_callback -- exact-name lookup (see header). A miss
// returns nullptr -- the caller treats it as "callback not registered" and
// never falls through to a default entry.
//------------------------------------------------------------------------------
void* il2cpp_pinvoke_find_callback(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    for (int32_t i = 0; i < g_callbackCount; ++i)
    {
        if (std::strcmp(g_callbacks[i].name, name) == 0)
        {
            return g_callbacks[i].fn;
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_clear_last_error -- pre-call OS error slot reset (see
// header). The runtime emits this before a P/Invoke call marked
// SetLastError=true so the post-call capture sees only the callee's error.
//------------------------------------------------------------------------------
void il2cpp_pinvoke_clear_last_error()
{
#if defined(_WIN32)
    SetLastError(0);
#else
    errno = 0;
#endif
}

} // namespace il2cpp
