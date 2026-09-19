//------------------------------------------------------------------------------
// marshalext2.cpp -- Marshal primitive extension-2 (SizeOf / OffsetOf /
// last-error / StringToHGlobal; see il2cpp_marshal_ext2.h for the full
// contract and il2cpp_marshal_ext.h for batch 1).
//
// Batch 1 (marshalext.cpp, M4) models an all-int32 struct as a tracked byte
// buffer; this translation unit adds the four Marshal-surface groups the
// transpiler maps Marshal.* calls onto:
//
//   1. il2cpp_marshal_size_of  -- primitive byte size by net48 TypeCode
//   2. il2cpp_marshal_offset_of -- field offset in a packed type-code layout
//   3. il2cpp_marshal_get/set_last_win32_error -- OS error state wrappers
//   4. il2cpp_marshal_string_to_hglobal_ansi/uni -- heap string copies
//
// Platform split for the last-error wrappers mirrors pinvokeext.cpp
// (GetLastError/SetLastError on Windows, errno on POSIX). The ANSI encoder
// is REUSED from il2cpp_pinvoke_string_to_utf8_alloc (DRY -- the P/Invoke
// layer already owns the full UTF-8 encoder); the Uni path writes explicit
// char16_t units so the buffer is UTF-16 on every platform.
//
// Buffers returned by the string functions are released with
// icall_marshal_free_hglobal (runtime.cpp): same std::malloc/std::free
// pair as icall_marshal_alloc_hglobal, so allocator choice is consistent
// across the whole hglobal surface.
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext2.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_pinvoke_ext.h"

#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// il2cpp_marshal_size_of -- Marshal.SizeOf(primitive) by net48 TypeCode (see
// header). The supported codes and their sizes:
//
//   3 Boolean 4   4 Char 1   5 SByte 1   6 Byte 1
//   7 Int16 2     8 UInt16 2 9 Int32 4   10 UInt32 4
//   11 Int64 8    12 UInt64 8 13 Single 4 14 Double 8
//   15 Decimal 16
//
// Everything else (Object/String/DateTime/unknown) has no fixed native size
// -- Marshal.SizeOf throws for these in .NET, so NotSupportedException
// (fail loud, matching batch 1's exception policy).
//------------------------------------------------------------------------------
int32_t il2cpp_marshal_size_of(int32_t typeCode)
{
    switch (typeCode)
    {
        case 3:  return 4;  // Boolean (Win32 BOOL under the default Ansi CharSet)
        case 5:  return 1;  // SByte
        case 6:  return 1;  // Byte
        case 4:  return 1;  // Char (Ansi, not the 2-byte managed char16)
        case 7:  return 2;  // Int16
        case 8:  return 2;  // UInt16
        case 9:  return 4;  // Int32
        case 10: return 4;  // UInt32
        case 13: return 4;  // Single
        case 11: return 8;  // Int64
        case 12: return 8;  // UInt64
        case 14: return 8;  // Double
        case 15: return 16; // Decimal
        default:
            il2cpp_throw_not_supported_exception(
                "Marshal.SizeOf: type code has no fixed native size "
                "(Object/String/DateTime are not blittable primitives)");
            return 0; // unreachable (the throw above always throws)
    }
}

//------------------------------------------------------------------------------
// il2cpp_marshal_offset_of -- Marshal.OffsetOf inside a packed layout (see
// header). Offset of field 'fieldIndex' = sum of the sizes of fields
// 0..fieldIndex-1 (no padding). The prefix sum accumulates in int64 and is
// checked against INT32_MAX after every addition -- sizes are all positive,
// so the sum is monotonic and one check per step is sufficient.
//------------------------------------------------------------------------------
int32_t il2cpp_marshal_offset_of(int32_t fieldIndex, int32_t fieldCount, int32_t* fieldTypeCodes)
{
    if (fieldCount < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("fieldCount", nullptr);
    }
    if (fieldCount > 0 && fieldTypeCodes == nullptr)
    {
        il2cpp_throw_argument_null_exception("fieldTypeCodes");
    }
    if (fieldIndex < 0 || fieldIndex >= fieldCount)
    {
        il2cpp_throw_argument_out_of_range_exception("fieldIndex", nullptr);
    }
    int64_t offset = 0;
    for (int32_t i = 0; i < fieldIndex; ++i)
    {
        const int64_t size = il2cpp_marshal_size_of(fieldTypeCodes[i]);
        offset += size;
        if (offset > INT32_MAX)
        {
            il2cpp_throw_argument_out_of_range_exception(
                "fieldTypeCodes", "layout offset overflows int32");
        }
    }
    return static_cast<int32_t>(offset);
}

//------------------------------------------------------------------------------
// il2cpp_marshal_get_last_win32_error / set -- Marshal.Get/SetLastWin32Error:
// direct OS error-state wrappers (GetLastError/SetLastError on Windows,
// errno on POSIX -- the same platform split as
// il2cpp_pinvoke_clear_last_error in pinvokeext.cpp). These are NOT the
// SetLastError=true capture slot (il2cpp_pinvoke_get_last_error).
//------------------------------------------------------------------------------
int32_t il2cpp_marshal_get_last_win32_error()
{
#if defined(_WIN32)
    return static_cast<int32_t>(GetLastError());
#else
    return errno;
#endif
}

void il2cpp_marshal_set_last_win32_error(int32_t error)
{
#if defined(_WIN32)
    SetLastError(static_cast<DWORD>(error));
#else
    errno = error;
#endif
}

//------------------------------------------------------------------------------
// il2cpp_marshal_string_to_hglobal_ansi -- Marshal.StringToHGlobalAnsi: a
// NEW heap buffer with the managed string encoded as UTF-8 (null-
// terminated). null input -> nullptr (IntPtr.Zero, .NET semantics). The
// encoder is shared with the P/Invoke layer (DRY): every unit of the
// 3*length+1-byte worst-case capacity is written, then terminated -- the
// buffer is a self-contained copy, valid until the caller releases it with
// icall_marshal_free_hglobal.
//
// On Windows the buffer comes from the Global heap (GlobalAlloc GMEM_FIXED):
// icall_marshal_free_hglobal is GlobalFree there, so every hglobal string
// buffer must be GlobalAlloc'd (same family as StringToHGlobalUni).
//------------------------------------------------------------------------------
void* il2cpp_marshal_string_to_hglobal_ansi(Il2CppString* s)
{
    if (s == nullptr)
    {
        return nullptr;
    }
#if defined(_WIN32)
    char* utf8 = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
    const size_t byteCount = std::strlen(utf8) + 1;
    void* buf = GlobalAlloc(GMEM_FIXED | GMEM_ZEROINIT, byteCount);
    if (buf == nullptr)
    {
        std::fprintf(stderr, "il2cpp_marshal_string_to_hglobal_ansi: out of memory\n");
        std::free(utf8);
        std::abort();
    }
    std::memcpy(buf, utf8, byteCount);
    std::free(utf8);
    return buf;
#else
    return il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
#endif
}

//------------------------------------------------------------------------------
// il2cpp_marshal_string_to_hglobal_uni -- Marshal.StringToHGlobalUni: a NEW
// heap buffer with the managed string copied as char16_t units plus a
// trailing NUL unit. char16_t is explicit (NOT wchar_t -- its width differs
// between Windows 2 and POSIX 4), so the buffer is UTF-16 on every
// platform. null input -> nullptr (IntPtr.Zero). Release with
// icall_marshal_free_hglobal; OOM aborts loudly.
//
// On Windows the buffer comes from the Global heap (GlobalAlloc GMEM_FIXED --
// GMEM_FIXED returns a DIRECT pointer, so the value stays usable as a plain
// char16_t* AND as a global handle for icall_marshal_free_hglobal's
// GlobalFree).
//------------------------------------------------------------------------------
void* il2cpp_marshal_string_to_hglobal_uni(Il2CppString* s)
{
    if (s == nullptr)
    {
        return nullptr;
    }
    const size_t byteCount = (static_cast<size_t>(s->length) + 1) * sizeof(char16_t);
#if defined(_WIN32)
    void* buf = GlobalAlloc(GMEM_FIXED | GMEM_ZEROINIT, byteCount);
#else
    void* buf = std::malloc(byteCount);
#endif
    if (buf == nullptr)
    {
        std::fprintf(stderr, "il2cpp_marshal_string_to_hglobal_uni: out of memory\n");
        std::abort();
    }
    if (s->length > 0)
    {
        std::memcpy(buf, s->chars, static_cast<size_t>(s->length) * sizeof(char16_t));
    }
    static_cast<char16_t*>(buf)[s->length] = u'\0';
    return buf;
}

} // namespace runtime
} // namespace il2cpp
