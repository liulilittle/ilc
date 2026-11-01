#pragma once
//------------------------------------------------------------------------------
// il2cpp_pinvoke_struct.h -- P/Invoke STRUCT marshalling (WinForms stage-1
// gap #2: MSG/RECT/POINT by-value / by-ref).
//
// The transpiler's P/Invoke parameter path today only understands
// int32_t / const char* (CodeGenerator.PinvokeParamList). This layer gives
// the generated code a minimal struct marshal for the high-frequency
// user32 structs -- the byte layout + a size-checked memcpy. The model is a
// LAYOUT REGISTRY: each registered struct kind owns a canonical Win32
// sequential layout (field offsets + native byte size). All registered
// structs are BLITTABLE (the managed sequential layout matches the native
// layout byte-for-byte), so marshalling is a plain memcpy in both
// directions -- "generic memcpy form":
//
//   BY-VALUE  : il2cpp_pinvoke_struct_to_native(kind, &managed, buffer)
//               copies the managed struct value into a native buffer (48
//               bytes for MSG on x64) that the generated call site passes
//               to the native callee.
//   BY-REF    : the native callee receives the ADDRESS of the managed struct
//               (no copy); il2cpp_pinvoke_struct_from_native(kind, native,
//               &managed) copies the filled native bytes back into the
//               managed value after the call (out/ref result marshalling).
//
// The C++ struct types below (Il2CppPinvokePoint / Rect / Msg) are the
// native-layout declarations the transpiler's extern "C" P/Invoke
// signatures can emit; the registry (sizes + field offsets) is the runtime
// source of truth the marshalling helpers and the unit tests check against.
//
// WNDCLASS (RegisterClass) is NOT registered: it is always passed by
// ADDRESS and its byte layout depends on WNDPROC callback pointers -- the
// managed side fills the struct and hands the pointer through untouched
// (a plain pointer passthrough, no layout translation needed).
//
// DEVMODE (EnumDisplaySettings/ChangeDisplaySettings) is also NOT
// registered: it is always passed by ADDRESS, and its layout is
// NON-BLITTABLE (a union overlaps the dmOrientation/dmPaperSize/dmPosition/
// dmPrintQuality scalars, plus bitfields and a variable-size dmDriverExtra
// tail -- empirically sizeof(DEVMODEW)=220 on x64, with dmPosition@76 and
// dmOrientation@76 sharing the same bytes). The registry only holds structs
// whose managed sequential layout is byte-identical to the native layout,
// which a union breaks; DEVMODE crosses the boundary as a raw pointer
// passthrough like WNDCLASS.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"  // Il2CppObject
#include "il2cpp_string.h"  // Il2CppString (ByValTStr inline buffers)
#include <cstdint>
#include <cstring>

namespace il2cpp {

//------------------------------------------------------------------------------
// ByValTStr inline-buffer helpers ([MarshalAs(UnmanagedType.ByValTStr,
// SizeConst = N)]). net48 lays the field out as WCHAR[N] INSIDE the value
// type, so the generated struct member is a plain char16_t[N] and the managed
// string is converted at the field access, not stored as a GC pointer:
//
//   load  : buffer -> managed string (stops at the first NUL, or at
//           'capacity' units when the native side filled the buffer without a
//           terminator -- measured net48 PtrToStructure behaviour).
//   store : managed string -> buffer (copies at most capacity-1 units and
//           NUL-terminates; a null string zero-fills -- measured net48
//           StructureToPtr behaviour: a 32-unit input into SizeConst=32 keeps
//           31 units + NUL).
//
// The helpers own no GC roots: the buffer is plain native memory, the string
// is a managed object the caller roots, exactly like any other string temp.
//------------------------------------------------------------------------------
inline Il2CppObject* il2cpp_pinvoke_byvaltstr_load(const char16_t* buffer, int32_t capacity)
{
    int32_t length = 0;
    if (buffer != nullptr)
    {
        while (length < capacity && buffer[length] != 0)
        {
            ++length;
        }
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(buffer, length));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(u"", 0));
}

inline void il2cpp_pinvoke_byvaltstr_store(char16_t* buffer, int32_t capacity, Il2CppObject* value)
{
    if (buffer == nullptr || capacity <= 0)
    {
        return;
    }
    int32_t length = 0;
    if (value != nullptr)
    {
        const Il2CppString* str = reinterpret_cast<const Il2CppString*>(value);
        length = str->length;
        const int32_t limit = capacity - 1;
        if (length > limit)
        {
            length = limit;
        }
        if (length > 0)
        {
            std::memcpy(buffer, str->chars, static_cast<size_t>(length) * sizeof(char16_t));
        }
    }
    for (int32_t i = length; i < capacity; ++i)
    {
        buffer[i] = 0;
    }
}

//------------------------------------------------------------------------------
// Native struct kinds (canonical Win32 sequential layouts).
//------------------------------------------------------------------------------
enum Il2CppPinvokeStructKind
{
    kPinvokeStructPoint = 0, // POINT { int32 x, y }                     -- 8 bytes
    kPinvokeStructRect,      // RECT  { int32 left, top, right, bottom } -- 16 bytes
    kPinvokeStructMsg,       // MSG   (hwnd, message, wParam, lParam,
                             //        time, pt) -- 48 bytes x64 / 28 x86
    kPinvokeStructKindCount
};

//------------------------------------------------------------------------------
// Native-layout C++ types (declarations for generated P/Invoke signatures).
// Field order mirrors the Win32 definitions; the embedded POINT member makes
// the compiler insert the pointer-alignment padding that MSG needs on x64,
// exactly like windows.h's tagMSG.
//------------------------------------------------------------------------------
struct Il2CppPinvokePoint
{
    int32_t x;
    int32_t y;
};

struct Il2CppPinvokeRect
{
    int32_t left;
    int32_t top;
    int32_t right;
    int32_t bottom;
};

struct Il2CppPinvokeMsg
{
    void*                hwnd;    // HWND   (pointer-width)
    int32_t              message; // UINT
    void*                wParam;  // WPARAM (pointer-width)
    void*                lParam;  // LPARAM (pointer-width)
    int32_t              time;    // DWORD
    Il2CppPinvokePoint   pt;      // POINT  (x, y)
};

//------------------------------------------------------------------------------
// A single field of a registered native layout: byte offset + byte size.
//------------------------------------------------------------------------------
struct Il2CppPinvokeStructField
{
    int32_t offset;
    int32_t size;
};

// Native byte size of the given struct kind (canonical layout). An invalid
// kind aborts loudly (a transpiler/emission bug -- the generated code emits
// only registered kinds).
int32_t il2cpp_pinvoke_struct_size(Il2CppPinvokeStructKind kind);

// Field layout of the given kind: an array of {offset, size} pairs of length
// il2cpp_pinvoke_struct_field_count(kind). The array is static runtime
// storage -- valid for the process lifetime. Invalid kind aborts loudly.
const Il2CppPinvokeStructField* il2cpp_pinvoke_struct_field_layout(Il2CppPinvokeStructKind kind);
int32_t il2cpp_pinvoke_struct_field_count(Il2CppPinvokeStructKind kind);

// The canonical net48 field NAME of each layout field (parallel to
// il2cpp_pinvoke_struct_field_layout): "x"/"y" for POINT and "x"/"y"/"width"/
// "height" for RECT (net48 System.Drawing.Rectangle declares PRIVATE fields
// x/y/width/height -- the byte offsets match the Win32 RECT 0/4/8/12),
// "hwnd"/"message"/"wParam"/"lParam"/"time"/"pt" for MSG (the Win32 tagMSG
// names -- net48 System.Windows.Forms.Message substitutes result for time+pt;
// see pinvokestruct.cpp). Same storage lifetime as the layout arrays. Invalid
// kind aborts loudly.
const char* il2cpp_pinvoke_struct_field_name(Il2CppPinvokeStructKind kind, int32_t fieldIndex);

// Byte offset of the field with the given NAME in the given kind's canonical
// layout (Marshal.OffsetOf support -- exact, case-sensitive net48 match).
// Returns -1 when the kind has no field with that name (the caller maps it to
// ArgumentException). An invalid kind or null fieldName aborts loudly.
int32_t il2cpp_pinvoke_struct_field_offset(Il2CppPinvokeStructKind kind, const char* fieldName);

// Resolve a managed type full name to a struct kind ("System.Drawing.Rectangle"
// -> kPinvokeStructRect). The transpiler emits the managed full name and the
// runtime maps it; an UNREGISTERED name returns -1 (never a default kind).
int32_t il2cpp_pinvoke_struct_kind_by_name(const char* managedTypeFullName);

//------------------------------------------------------------------------------
// Marshal a struct value between the MANAGED value-type layout (the generated
// C++ struct -- a blittable sequential layout with identical byte order) and
// the NATIVE Win32 layout (dst):
//
//   il2cpp_pinvoke_struct_to_native(kind, &managed, &native)
//     managed -> native (by-value argument marshalling: the generated call
//     site copies the managed value into a stack buffer of
//     il2cpp_pinvoke_struct_size(kind) bytes and passes the buffer).
//
//   il2cpp_pinvoke_struct_from_native(kind, &native, &managed)
//     native -> managed (by-ref / out result marshalling: after the native
//     callee fills the buffer, copy it back into the managed struct value).
//
// Both directions are size-checked memcpy (every registered struct is
// blittable -- the layout registry documents the exact bytes). Null pointers
// abort loudly (a generated-call-site bug).
//------------------------------------------------------------------------------
void il2cpp_pinvoke_struct_to_native(Il2CppPinvokeStructKind kind, const void* managedSrc, void* nativeDst);
void il2cpp_pinvoke_struct_from_native(Il2CppPinvokeStructKind kind, const void* nativeSrc, void* managedDst);

} // namespace il2cpp
