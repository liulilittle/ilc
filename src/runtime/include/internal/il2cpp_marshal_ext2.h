#pragma once
//------------------------------------------------------------------------------
// il2cpp_marshal_ext2.h -- Marshal primitive extension-2 (SizeOf / OffsetOf /
// last-error / StringToHGlobal; see il2cpp_marshal_ext.h for batch 1 and
// il2cpp_runtime.h for the M4 alloc_hglobal/free_hglobal icalls).
//
// SCOPE
//   Batch 1 (il2cpp_marshal_ext.h) models an all-int32 struct as a tracked
//   byte buffer. This batch completes the Marshal surface the transpiler
//   maps onto while the real struct type system is pending (M5):
//
//     il2cpp_marshal_size_of(typeCode)              -- Marshal.SizeOf(primitive)
//     il2cpp_marshal_offset_of(...)                 -- Marshal.OffsetOf (field
//                                                       offset inside a layout)
//     il2cpp_marshal_get/set_last_win32_error(...)  -- Marshal.Get/SetLastWin32Error
//     il2cpp_marshal_string_to_hglobal_ansi/uni(...)-- Marshal.StringToHGlobalAnsi/Uni
//
// TYPECODE CONVENTION (net48 TypeCode values -- keep in sync with
// icall_object_get_type_code in il2cpp_typecheck.h / il2cpp_typeext.h):
//
//     Boolean=3(1B)  Char=4(2B)   SByte=5(1B)  Byte=6(1B)
//     Int16=7(2B)    UInt16=8(2B) Int32=9(4B)  UInt32=10(4B)
//     Int64=11(8B)   UInt64=12(8B) Single=13(4B) Double=14(8B)
//     Decimal=15(16B)
//
//   Every other code (Empty=0, Object=1, DBNull=2, DateTime=16, String=18,
//   anything unknown) has NO fixed native size -- Marshal.SizeOf throws for
//   these in .NET, so this layer raises NotSupportedException (fail loud,
//   same policy as batch 1).
//
// OFFSET MODEL
//   The layout is the caller's field-type-code table (like batch 1's
//   fieldSizes): a struct with fieldCount consecutive fields of the given
//   type codes, NO padding (each field starts where the previous ends --
//   matching Marshal.SizeOf semantics for packed structs; the M5 real
//   layout engine replaces this). Offset of field i = sum of the sizes of
//   fields 0..i-1.
//
// LAST-ERROR CONTRACT
//   Direct OS wrappers: GetLastError/SetLastError on Windows, errno on
//   POSIX (same platform split as il2cpp_pinvoke_clear_last_error). These
//   are DISTINCT from the SetLastError=true capture slot
//   (il2cpp_pinvoke_get_last_error, pinvoke.cpp) -- that slot holds the
//   error captured after a [DllImport(SetLastError=true)] call, while these
//   two touch the OS error state itself.
//
// HLOBAL STRING CONTRACT
//   Marshal.StringToHGlobalAnsi/Uni semantics: the managed string is copied
//   into a NEW heap buffer (ANSI = UTF-8, Uni = UTF-16 with a trailing NUL
//   unit), and null input marshals as nullptr (IntPtr.Zero). The returned
//   pointer MUST be released with icall_marshal_free_hglobal (same allocator
//   family as icall_marshal_alloc_hglobal: GlobalAlloc/GlobalFree on
//   Windows -- net48 LocalAlloc-backed -- and std::malloc/std::free on
//   POSIX). The UTF-8 encoder is shared with the P/Invoke layer
//   (il2cpp_pinvoke_string_to_utf8_alloc) -- nothing is re-implemented
//   here. The Uni buffer stores explicit char16_t units, so it is UTF-16 on
//   every platform (wchar_t width differs between Windows and POSIX).
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Marshal.SizeOf for a primitive, by net48 TypeCode (see the table above).
// Supported: Boolean/Char/SByte/Byte/Int16/UInt16/Int32/UInt32/Int64/
// UInt64/Single/Double/Decimal. Any other code (Object/String/DateTime/
// unknown) -> NotSupportedException (no fixed native size in .NET either).
int32_t il2cpp_marshal_size_of(int32_t typeCode);

// Marshal.OffsetOf inside a packed layout: the byte offset of field
// 'fieldIndex' in a struct of 'fieldCount' consecutive fields described by
// 'fieldTypeCodes' (no padding -- offset = sum of the sizes of the fields
// before it, via il2cpp_marshal_size_of).
//   fieldCount < 0                        -> ArgumentOutOfRangeException
//   fieldCount > 0 && typeCodes == nullptr -> ArgumentNullException
//   fieldIndex < 0 || fieldIndex >= fieldCount -> ArgumentOutOfRangeException
//   prefix sum overflows int32            -> ArgumentOutOfRangeException
//   a prefix field has no fixed size      -> NotSupportedException
//   fieldIndex == 0                       -> 0 (no typeCodes access)
int32_t il2cpp_marshal_offset_of(int32_t fieldIndex, int32_t fieldCount, int32_t* fieldTypeCodes);

// Marshal.GetLastWin32Error: the OS last-error code (GetLastError on
// Windows, errno on POSIX). Direct OS state -- NOT the SetLastError=true
// capture slot (see il2cpp_pinvoke_get_last_error).
int32_t il2cpp_marshal_get_last_win32_error();

// Marshal.SetLastWin32Error: set the OS last-error code (SetLastError on
// Windows, errno on POSIX). Trivially invertible with the getter above.
void il2cpp_marshal_set_last_win32_error(int32_t error);

// Marshal.StringToHGlobalAnsi: copy the managed string into a NEW heap
// buffer as UTF-8 (null-terminated). null input -> nullptr (IntPtr.Zero).
// Release with icall_marshal_free_hglobal. OOM aborts loudly.
void* il2cpp_marshal_string_to_hglobal_ansi(Il2CppString* s);

// Marshal.StringToHGlobalUni: copy the managed string into a NEW heap
// buffer as UTF-16 char16_t units (trailing NUL unit included). null input
// -> nullptr (IntPtr.Zero). Release with icall_marshal_free_hglobal.
void* il2cpp_marshal_string_to_hglobal_uni(Il2CppString* s);

} // namespace runtime
} // namespace il2cpp
