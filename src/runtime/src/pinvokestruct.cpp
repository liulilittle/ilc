//------------------------------------------------------------------------------
// pinvokestruct.cpp -- P/Invoke struct marshalling (see il2cpp_pinvoke_struct.h).
//
// A layout registry for the high-frequency user32 structs (POINT/RECT/MSG):
// canonical Win32 field offsets + native sizes, plus size-checked memcpy
// marshalling in both directions. The registry is constexpr static data
// (constraint #12 -- no global-constructor allocation): every layout is
// fully computed at compile time, so the module has zero static init and is
// safe to read from any thread.
//
// MSG is the only pointer-width-dependent layout. It follows the Win32 x64/
// x86 member-alignment rules (verified against windows.h): pointer-width
// members (hwnd/wParam/lParam) align to sizeof(void*)=P, the 4-byte scalars
// (message/time) and the POINT (two int32s -- 4-byte alignment) align to 4:
//   hwnd@0(P) message@P(4) wParam@2P(P) lParam@3P(P) time@4P(4) pt@4P+4(8)
//   -> AlignUp(4P+12, P) = 48 bytes (x64) / 28 bytes (x86)
// matching the Win32 tagMSG definition on both architectures. Note pt lands
// at 4P+4, NOT 5P -- POINT needs only 4-byte alignment, so no pad follows
// time on x64 (a probe against windows.h MSG confirmed pt@36 = 4*8+4).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke_struct.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {

namespace {

constexpr int32_t AlignUp(int32_t value, int32_t alignment)
{
    return (value + alignment - 1) & ~(alignment - 1);
}

constexpr int32_t kPointerSize = static_cast<int32_t>(sizeof(void*));

// Field #i start offset of the MSG layout (fields: 0 hwnd, 1 message,
// 2 wParam, 3 lParam, 4 time, 5 pt). The start of every field is aligned to
// that field's own alignment (pointer-width members to P, scalars and POINT
// to 4), exactly like the C++/Win32 ABI.
constexpr int32_t MsgFieldAlign(int32_t field)
{
    return (field == 0 || field == 2 || field == 3) ? kPointerSize : 4;
}

constexpr int32_t MsgFieldSize(int32_t field)
{
    return (field == 1 || field == 4) ? 4
         : (field == 5)               ? 8
         : kPointerSize;
}

constexpr int32_t MsgFieldOffset(int32_t field)
{
    int32_t offset = 0;
    for (int32_t i = 0; i < field; ++i)
    {
        offset = AlignUp(offset, MsgFieldAlign(i));
        offset += MsgFieldSize(i);
    }
    return AlignUp(offset, MsgFieldAlign(field));
}

// Native byte size of MSG: the end offset just past pt (MsgFieldOffset(6))
// struct-aligned to the pointer width (the struct's alignment is the max
// member alignment = P -- see the header comment).
constexpr int32_t MsgNativeSize()
{
    return AlignUp(MsgFieldOffset(6), kPointerSize);
}

constexpr Il2CppPinvokeStructField kPointLayout[2] = {
    { 0, 4 }, { 4, 4 },
};
constexpr Il2CppPinvokeStructField kRectLayout[4] = {
    { 0, 4 }, { 4, 4 }, { 8, 4 }, { 12, 4 },
};
constexpr Il2CppPinvokeStructField kMsgLayout[6] = {
    { MsgFieldOffset(0), kPointerSize }, // hwnd
    { MsgFieldOffset(1), 4 },            // message
    { MsgFieldOffset(2), kPointerSize }, // wParam
    { MsgFieldOffset(3), kPointerSize }, // lParam
    { MsgFieldOffset(4), 4 },            // time
    { MsgFieldOffset(5), 8 },            // pt
};

// Compile-time consistency: the C++ struct types in the header must agree
// with the registry (the transpiler emits the struct types into native
// signatures; the marshalling helpers copy registry-sized bytes).
static_assert(sizeof(Il2CppPinvokePoint) == 8, "Il2CppPinvokePoint layout mismatch");
static_assert(sizeof(Il2CppPinvokeRect) == 16, "Il2CppPinvokeRect layout mismatch");
static_assert(sizeof(Il2CppPinvokeMsg) == MsgNativeSize(), "Il2CppPinvokeMsg layout mismatch");

struct StructDescriptor
{
    const char*                managedName; // full managed type name (kind_by_name lookup)
    const Il2CppPinvokeStructField* fields; // canonical native field layout
    const char* const*         fieldNames;  // net48 field names (parallel to fields)
    int32_t                    fieldCount;
    int32_t                    nativeSize;
};

// Net48 field-name tables (Marshal.OffsetOf is FIELD-based -- the exact
// declared names, case-sensitive). System.Drawing.Rectangle declares PRIVATE
// fields x/y/width/height (net48 OffsetOf(typeof(Rectangle), "X") THROWS --
// "X" is a property, not a field; "x" is the field at offset 0). The Win32
// RECT byte offsets are identical (0/4/8/12); only the names differ. Point
// declares x/y. MSG uses the Win32 tagMSG field names (the layout this
// registry models); note the documented divergence: net48
// System.Windows.Forms.Message declares hwnd/msg/wParam/lParam/result
// (Message.SizeOf is 40/20, this registry models the 48/28 Win32 MSG -- the
// SizeOf/OffsetOf on Message are NOT net48-faithful).
constexpr const char* kPointFieldNames[2] = { "x", "y" };
constexpr const char* kRectFieldNames[4] = { "x", "y", "width", "height" };
constexpr const char* kMsgFieldNames[6] = { "hwnd", "message", "wParam", "lParam", "time", "pt" };

constexpr StructDescriptor kDescriptors[kPinvokeStructKindCount] = {
    { "System.Drawing.Point",       kPointLayout, kPointFieldNames, 2, 8           },
    { "System.Drawing.Rectangle",   kRectLayout,  kRectFieldNames,  4, 16          },
    { "System.Windows.Forms.Message", kMsgLayout,  kMsgFieldNames,   6, MsgNativeSize() },
};

// Validate a kind index; aborts loudly on out-of-range (a generated-call-site
// bug -- the transpiler only emits registered kinds).
const StructDescriptor& Descriptor(Il2CppPinvokeStructKind kind)
{
    if (kind < 0 || kind >= kPinvokeStructKindCount)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_struct: invalid struct kind %d\n", static_cast<int>(kind));
        std::abort();
    }
    return kDescriptors[kind];
}

} // namespace

int32_t il2cpp_pinvoke_struct_size(Il2CppPinvokeStructKind kind)
{
    return Descriptor(kind).nativeSize;
}

const Il2CppPinvokeStructField* il2cpp_pinvoke_struct_field_layout(Il2CppPinvokeStructKind kind)
{
    return Descriptor(kind).fields;
}

int32_t il2cpp_pinvoke_struct_field_count(Il2CppPinvokeStructKind kind)
{
    return Descriptor(kind).fieldCount;
}

const char* il2cpp_pinvoke_struct_field_name(Il2CppPinvokeStructKind kind, int32_t fieldIndex)
{
    const StructDescriptor& d = Descriptor(kind);
    if (fieldIndex < 0 || fieldIndex >= d.fieldCount)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_struct: invalid field index %d for kind %d\n",
            static_cast<int>(fieldIndex), static_cast<int>(kind));
        std::abort();
    }
    return d.fieldNames[fieldIndex];
}

int32_t il2cpp_pinvoke_struct_field_offset(Il2CppPinvokeStructKind kind, const char* fieldName)
{
    if (fieldName == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_struct: null field name\n");
        std::abort();
    }
    const StructDescriptor& d = Descriptor(kind);
    for (int32_t i = 0; i < d.fieldCount; ++i)
    {
        if (std::strcmp(d.fieldNames[i], fieldName) == 0)
        {
            return d.fields[i].offset;
        }
    }
    return -1;
}

int32_t il2cpp_pinvoke_struct_kind_by_name(const char* managedTypeFullName)
{
    if (managedTypeFullName == nullptr)
    {
        return -1;
    }
    for (int32_t i = 0; i < kPinvokeStructKindCount; ++i)
    {
        if (std::strcmp(kDescriptors[i].managedName, managedTypeFullName) == 0)
        {
            return i;
        }
    }
    return -1;
}

void il2cpp_pinvoke_struct_to_native(Il2CppPinvokeStructKind kind, const void* managedSrc, void* nativeDst)
{
    if (managedSrc == nullptr || nativeDst == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_struct_to_native: null source/destination\n");
        std::abort();
    }
    const StructDescriptor& d = Descriptor(kind);
    std::memcpy(nativeDst, managedSrc, static_cast<size_t>(d.nativeSize));
}

void il2cpp_pinvoke_struct_from_native(Il2CppPinvokeStructKind kind, const void* nativeSrc, void* managedDst)
{
    if (nativeSrc == nullptr || managedDst == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_struct_from_native: null source/destination\n");
        std::abort();
    }
    const StructDescriptor& d = Descriptor(kind);
    std::memcpy(managedDst, nativeSrc, static_cast<size_t>(d.nativeSize));
}

} // namespace il2cpp
