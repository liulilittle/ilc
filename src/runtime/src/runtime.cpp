#include "il2cpp_runtime.h"
#include "il2cpp_array.h"
#include "il2cpp_string.h"
#include "il2cpp_console.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp { namespace runtime {

void icall_console_write_line_i(int32_t value) {
    std::printf("%d", value);
    console_write_newline();
}

namespace {

// UTF-16 emission for the console string icalls (net48 Console.OutputEncoding
// default: the console output code page). CP 65001 keeps the exact legacy
// UTF-8 loop below (byte-identical -- ASCII 1 byte, BMP 2-3 bytes, a valid
// surrogate pair one 4-byte sequence, unpaired -> U+FFFD EF BF BD); other
// code pages route pair-aware through console_write_units (WideCharToMultiByte,
// unmappable -> '?' -- the diffuri002 GBK parity: U+4E2D -> D6 D0).
// stdout runs in binary mode (console.cpp ConsoleStreamInitializer), so the
// bytes reach the stream verbatim.
void WriteStringUtf8(const Il2CppString* str)
{
    if (str == nullptr)
    {
        return;
    }
#ifdef _WIN32
    if (console_output_code_page() != 65001u)
    {
        console_write_units(str->chars, str->length, stdout);
        return;
    }
#endif
    for (int32_t i = 0; i < str->length; ++i)
    {
        const uint32_t u = static_cast<uint32_t>(str->chars[i]);
        if (u < 0x80)
        {
            std::fputc(static_cast<char>(u), stdout);
        }
        else if (u < 0x800)
        {
            std::fputc(static_cast<char>(0xC0 | (u >> 6)), stdout);
            std::fputc(static_cast<char>(0x80 | (u & 0x3F)), stdout);
        }
        else if (u >= 0xD800 && u <= 0xDBFF)
        {
            // High surrogate: a following low surrogate forms one 4-byte
            // pair; without one the unit is unpaired and becomes U+FFFD
            // (the WideCharToMultiByte / .NET replacement).
            if (i + 1 < str->length)
            {
                const uint32_t lo = static_cast<uint32_t>(str->chars[i + 1]);
                if (lo >= 0xDC00 && lo <= 0xDFFF)
                {
                    const uint32_t cp = 0x10000u + ((u - 0xD800u) << 10) + (lo - 0xDC00u);
                    std::fputc(static_cast<char>(0xF0 | (cp >> 18)), stdout);
                    std::fputc(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)), stdout);
                    std::fputc(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)), stdout);
                    std::fputc(static_cast<char>(0x80 | (cp & 0x3F)), stdout);
                    ++i; // consume the low surrogate
                    continue;
                }
            }
            std::fputs("\xEF\xBF\xBD", stdout);
        }
        else if (u >= 0xDC00 && u <= 0xDFFF)
        {
            // Unpaired low surrogate -> U+FFFD.
            std::fputs("\xEF\xBF\xBD", stdout);
        }
        else
        {
            std::fputc(static_cast<char>(0xE0 | (u >> 12)), stdout);
            std::fputc(static_cast<char>(0x80 | ((u >> 6) & 0x3F)), stdout);
            std::fputc(static_cast<char>(0x80 | (u & 0x3F)), stdout);
        }
    }
}

} // namespace

void icall_console_write_line_s(Il2CppObject* value) {
    // Full UTF-16 -> UTF-8 encoding (see WriteStringUtf8). The receiver is
    // a managed string object (cast internally).
    WriteStringUtf8(reinterpret_cast<Il2CppString*>(value));
    console_write_newline();
}
void icall_console_write_line_n() {
    console_write_newline();
}
void icall_console_write_i(int32_t value) {
    std::printf("%d", value);
}
void icall_console_write_s(Il2CppObject* value) {
    WriteStringUtf8(reinterpret_cast<Il2CppString*>(value));
}

// System.Object::.ctor no-op: the object header is stamped by the allocator
// and System.Object declares no fields, so the implicit base constructor has
// no observable behaviour. Generated code calls this as a pure nop. The
// receiver (this) is passed but ignored -- the newobj call shape always
// carries it.
void il2cpp_object_ctor_nop(Il2CppObject* this_) {
    (void)this_;
}

// M4 Task 5: Environment.NewLine -- the reference baseline (net48 on
// Windows) prints CRLF; the transpiled output must match byte-for-byte, so
// the runtime reports "\r\n" on every platform.
Il2CppObject* icall_environment_get_newline() {
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new_from_cs("\r\n"));
}

//------------------------------------------------------------------------------
// M4 Task 6: Marshal icalls.
//------------------------------------------------------------------------------

// AllocHGlobal: native heap allocation (zeroed). On Windows the buffer must
// come from the Global heap (GlobalAlloc GMEM_FIXED): the hglobal family is
// shared with StringToHGlobalUni (il2cpp_pinvoke_string_to_utf16_hglobal,
// pinvokeext.cpp), which SetClipboardData takes ownership of and later frees
// with GlobalFree -- a calloc'd/malloc'd pointer would be freed from the
// WRONG heap (0xC0000374). net48 AllocHGlobal is LocalAlloc-backed (the
// same global-heap family on Windows), so this is net48-accurate. GMEM_FIXED
// returns a DIRECT pointer (usable as the plain buffer AllocHGlobal callers
// expect). POSIX keeps calloc (no clipboard protocol).
intptr_t icall_marshal_alloc_hglobal(int32_t size)
{
    if (size < 0)
    {
        return 0;
    }
#if defined(_WIN32)
    HGLOBAL h = GlobalAlloc(GMEM_FIXED | GMEM_ZEROINIT, static_cast<size_t>(size));
    if (h == nullptr)
    {
        std::fprintf(stderr, "icall_marshal_alloc_hglobal: out of memory\n");
        std::abort();
    }
    return reinterpret_cast<intptr_t>(h);
#else
    void* p = std::calloc(1, static_cast<size_t>(size));
    if (p == nullptr)
    {
        std::fprintf(stderr, "icall_marshal_alloc_hglobal: out of memory\n");
        std::abort();
    }
    return reinterpret_cast<intptr_t>(p);
#endif
}

// FreeHGlobal: release a native allocation (must be a pointer from
// AllocHGlobal or StringToHGlobalUni; null is a no-op). GlobalFree on
// Windows (matches the GlobalAlloc alloc side), std::free on POSIX.
void icall_marshal_free_hglobal(void* ptr)
{
#if defined(_WIN32)
    GlobalFree(ptr);
#else
    std::free(ptr);
#endif
}

// Copy(int[] src, startIndex, nativeDest, length): int32 array -> native.
void icall_marshal_copy_to_unmanaged(Il2CppArray* arr, int32_t start, void* dest, int32_t length)
{
    if (arr == nullptr || dest == nullptr || start < 0 || length < 0 || start + length > arr->length)
    {
        std::fprintf(stderr, "icall_marshal_copy_to_unmanaged: invalid arguments\n");
        std::abort();
    }
    std::memcpy(dest, arr->data + static_cast<size_t>(start) * 4,
                static_cast<size_t>(length) * 4);
}

// Copy(nativeSrc, int[] dest, startIndex, length): native -> int32 array.
void icall_marshal_copy_from_unmanaged(void* src, Il2CppArray* arr, int32_t start, int32_t length)
{
    if (arr == nullptr || src == nullptr || start < 0 || length < 0 || start + length > arr->length)
    {
        std::fprintf(stderr, "icall_marshal_copy_from_unmanaged: invalid arguments\n");
        std::abort();
    }
    std::memcpy(arr->data + static_cast<size_t>(start) * 4, src,
                static_cast<size_t>(length) * 4);
}

void runtime_init() {
    // The runtime's INTERNAL half of il2cpp_runtime_init (the public C API
    // entry is generated in entry.cpp and calls this first). The collector
    // heap must be usable before the generated init proceeds to the string /
    // resource pool fills and the per-type registrations; the bootstrap is
    // idempotent (gc.cpp il2cpp_gc_init -> EnsureInitialized guard).
    ::il2cpp_runtime_bootstrap();
}

}} // namespace il2cpp::runtime
