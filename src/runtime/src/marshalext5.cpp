//------------------------------------------------------------------------------
// marshalext5.cpp -- Marshal VARIANT + COM-identity gap batch (net48 inventory
// §3 Marshal rows: GetObjectForNativeVariant / GetNativeVariantForObject /
// GetObjectsForNativeVariants / IsComObject). Declarations in
// il2cpp_marshal_ext.h.
//
// This translation unit adds the four surface groups the transpiler maps
// Marshal.* calls onto (see the "ILC VARIANT subset" contract in the header
// and the reference semantics below):
//
//      1. icall_marshal_get_object_for_native_variant -- Marshal.
//      GetObjectForNativeVariant(IntPtr): decode one 16-byte native VARIANT
//      into a managed object. Reference: coreclr olevariant.cpp
//      MarshalObjectForOleVariant (VT_EMPTY/VT_NULL -> null, VT_I4/VT_INT ->
//      Int32, VT_UI4/VT_UINT -> UInt32, VT_I2 -> Int16, VT_UI2 -> UInt16,
//      VT_I1 -> SByte, VT_UI1 -> Byte, VT_I8 -> Int64, VT_UI8 -> UInt64,
//      VT_R4 -> Single, VT_R8 -> Double, VT_BOOL -> Boolean, VT_ERROR ->
//      Int32).
//   2. icall_marshal_get_native_variant_for_object -- Marshal.
//      GetNativeVariantForObject(object, IntPtr): encode a boxed primitive
//      into a 16-byte VARIANT (the mirror of 1; null -> VT_EMPTY).
//   3. icall_marshal_get_objects_for_native_variants -- Marshal.
//      GetObjectsForNativeVariants(IntPtr, int): decode 'cVars' consecutive
//      VARIANTs at the base pointer into a managed object[] (coreclr
//      MarshalNative_GetObjectsForNativeVariants allocates the object array
//      and decodes each VARIANT in turn).
//   4. icall_marshal_is_com_object -- Marshal.IsComObject(object): net48
//      returns true only for COM RCW/CCW objects. ILC has no COM runtime, so
//      a non-null object is never a COM wrapper -> always false.
//
// VARIANT memory layout (Windows ABI, little-endian -- the layout the native
// side of a P/Invoke uses; all ILC targets are little-endian):
//
//     offset 0 (2 bytes): vt      VARTYPE
//     offset 2 (6 bytes): reserved (zeroed by the write side)
//     offset 8 (8 bytes): data    the primitive payload (union)
//
// Data width per VT: VT_I1/UI1 -> 1 byte at offset 8; VT_I2/UI2/BOOL -> 2
// bytes at offset 8; VT_I4/UI4/R4/INT/UINT -> 4 bytes at offset 8;
// VT_I8/UI8/R8 -> 8 bytes at offset 8. BOOL stores a VARIANT_BOOL (int16):
// -1 == true, 0 == false (the read side maps any nonzero value to true,
// matching coreclr's `V_BOOL(pOle) ? 1 : 0`).
//
// DOCUMENTED SUBSET (divergence): the types that need a real COM substrate --
// VT_BSTR (no BSTR allocator), VT_CY / VT_DECIMAL (no currency / decimal-in-
// variant marshaling), VT_DATE (no OLE date conversion), VT_ARRAY (no
// SAFEARRAY), VT_RECORD, VT_DISPATCH / VT_UNKNOWN (no COM identity) and the
// VT_BYREF forms -- raise NotSupportedException. VT_ERROR (an int32 SCODE with
// no COM substrate) is SUPPORTED on the decode side: 0 (S_OK) reads as null
// and any other SCODE reads as a boxed Int32 (coreclr's V_ERROR mapping; the
// .NET Framework's MarshalObjectForOleVariant returned null for a zero
// SCODE). netcore's non-Windows Marshal.NoCom.cs throws
// PlatformNotSupportedException for the whole surface; ILC instead implements
// the primitive subset so primitive round-trips work and every other VT fails
// loudly with a specific reason.
//
// EXCEPTION MAPPING (managed semantics via il2cpp_exception_ext.h)
//   get_object_for_native_variant: null src -> ArgumentNullException("pSrcNativeVariant");
//     unsupported vt -> NotSupportedException.
//   get_native_variant_for_object: null dst -> ArgumentNullException("pDstNativeVariant");
//     non-primitive / String / Decimal obj -> NotSupportedException.
//   get_objects_for_native_variants: null base -> ArgumentNullException;
//     negative cVars -> ArgumentOutOfRangeException; per-slot decode errors
//     propagate (an unsupported vt aborts the whole conversion, net48-style).
//   is_com_object: null -> ArgumentNullException("o"); otherwise false.
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"

#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

//------------------------------------------------------------------------------
// VARIANT VARTYPE constants (subset; the values are the Windows wtypes.h
// ABI -- never rename, they are the wire format).
//------------------------------------------------------------------------------
enum Vartype : uint16_t
{
    kVtEmpty   = 0,
    kVtNull    = 1,
    kVtI2      = 2,
    kVtI4      = 3,
    kVtR4      = 4,
    kVtR8      = 5,
    kVtCy      = 6,
    kVtDate    = 7,
    kVtBstr    = 8,
    kVtError   = 10,
    kVtBool    = 11,
    kVtDecimal = 14,
    kVtI1      = 16,
    kVtUi1     = 17,
    kVtUi2     = 18,
    kVtUi4     = 19,
    kVtI8      = 20,
    kVtUi8     = 21,
    kVtInt     = 22,
    kVtUint    = 23,
    kVtRecord  = 36,
    kVtByref   = 0x4000,
};

constexpr size_t kVariantSize = 16;
constexpr size_t kVtOffset = 0;
constexpr size_t kDataOffset = 8;

// The ILC VARIANT subset. Anything else (VT_BYREF forms, VT_ARRAY, VT_BSTR,
// VT_CY, VT_DATE, VT_DECIMAL, VT_RECORD, VT_DISPATCH, VT_UNKNOWN, ...) raises
// NotSupportedException -- the "no COM substrate" divergence.
bool IsSupportedVt(uint16_t vt)
{
    switch (vt)
    {
        case kVtEmpty:
        case kVtNull:
        case kVtI1:
        case kVtUi1:
        case kVtI2:
        case kVtUi2:
        case kVtI4:
        case kVtUi4:
        case kVtI8:
        case kVtUi8:
        case kVtR4:
        case kVtR8:
        case kVtBool:
        case kVtError:
        case kVtInt:
        case kVtUint:
            return true;
        default:
            return false;
    }
}

void ThrowUnsupportedVt(uint16_t vt)
{
    // DotNetException carries the message pointer WITHOUT copying
    // (il2cpp_exception.h), so the composed text MUST live in a thread-local
    // scratch (the established pattern: exceptionext.cpp g_messageScratch).
    // Same caveat applies: the scratch is overwritten by the next composed
    // throw, so a catch handler must read it before throwing again.
    thread_local char g_scratch[128];
    std::snprintf(g_scratch, sizeof(g_scratch),
                  "VARIANT type 0x%04X is outside the ILC VARIANT subset "
                  "(no BSTR/SAFEARRAY/DECIMAL/DATE/COM identity marshaling)",
                  static_cast<unsigned int>(vt));
    il2cpp_throw_not_supported_exception(g_scratch);
}

// Read the box class name; nullptr -> nullptr (a malformed box object).
const char* BoxClassName(Il2CppObject* obj)
{
    if (obj == nullptr || obj->klass == nullptr)
    {
        return nullptr;
    }
    return obj->klass->name;
}

} // namespace

//------------------------------------------------------------------------------
// icall_marshal_get_object_for_native_variant -- Marshal.GetObjectForNativeVariant
// (IntPtr): decode one 16-byte native VARIANT into a managed object.
//------------------------------------------------------------------------------
Il2CppObject* icall_marshal_get_object_for_native_variant(intptr_t pSrcNativeVariant)
{
    if (pSrcNativeVariant == 0)
    {
        il2cpp_throw_argument_null_exception("pSrcNativeVariant");
    }
    const unsigned char* variant = reinterpret_cast<const unsigned char*>(pSrcNativeVariant);
    uint16_t vt = 0;
    std::memcpy(&vt, variant + kVtOffset, sizeof(vt));
    if (!IsSupportedVt(vt))
    {
        ThrowUnsupportedVt(vt);
    }
    switch (vt)
    {
        case kVtEmpty:
        case kVtNull:
            return nullptr;
        case kVtI1:
        {
            int8_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_sbyte(v);
        }
        case kVtUi1:
        {
            uint8_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_byte(v);
        }
        case kVtI2:
        {
            int16_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_short(v);
        }
        case kVtUi2:
        {
            uint16_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_ushort(v);
        }
        case kVtI4:
        case kVtInt:
        {
            int32_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return il2cpp_box_int32(nullptr, v);
        }
        case kVtUi4:
        case kVtUint:
        {
            uint32_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_uint(static_cast<int32_t>(v));
        }
        case kVtI8:
        {
            int64_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_long(v);
        }
        case kVtUi8:
        {
            uint64_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_ulong(v);
        }
        case kVtR4:
        {
            float v = 0.0f;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_float(static_cast<double>(v));
        }
        case kVtR8:
        {
            double v = 0.0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_double(v);
        }
        case kVtBool:
        {
            int16_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            return icall_box_bool(v != 0);
        }
        case kVtError:
        {
            // VT_ERROR carries an int32 SCODE (no COM substrate needed -- the
            // payload is a plain 4-byte int). net48 returns null for S_OK (0)
            // and a boxed Int32 for any other SCODE (coreclr V_ERROR mapping /
            // the .NET Framework's null-for-zero MarshalObjectForOleVariant).
            int32_t v = 0;
            std::memcpy(&v, variant + kDataOffset, sizeof(v));
            if (v == 0)
            {
                return nullptr;
            }
            return il2cpp_box_int32(nullptr, v);
        }
        default:
            ThrowUnsupportedVt(vt); // unreachable (IsSupportedVt above)
            return nullptr;
    }
}

//------------------------------------------------------------------------------
// icall_marshal_get_native_variant_for_object -- Marshal.GetNativeVariantForObject
// (object, IntPtr): encode a boxed primitive into a 16-byte VARIANT.
//------------------------------------------------------------------------------
void icall_marshal_get_native_variant_for_object(Il2CppObject* obj, intptr_t pDstNativeVariant)
{
    if (pDstNativeVariant == 0)
    {
        il2cpp_throw_argument_null_exception("pDstNativeVariant");
    }
    unsigned char* variant = reinterpret_cast<unsigned char*>(pDstNativeVariant);
    std::memset(variant, 0, kVariantSize); // VT_EMPTY + zeroed reserved/data default

    if (obj == nullptr)
    {
        return; // VT_EMPTY
    }

    // Dispatch on the box class name (the runtime box classes register exactly
    // the .NET full names; see il2cpp_boxing_ext2.h).
    const char* name = BoxClassName(obj);
    uint16_t vt = 0;
    if (name == nullptr)
    {
        vt = 0; // fall through to the unsupported path below
    }
    else if (std::strcmp(name, "System.Byte") == 0)
    {
        vt = kVtUi1;
    }
    else if (std::strcmp(name, "System.SByte") == 0)
    {
        vt = kVtI1;
    }
    else if (std::strcmp(name, "System.Int16") == 0)
    {
        vt = kVtI2;
    }
    else if (std::strcmp(name, "System.UInt16") == 0 || std::strcmp(name, "System.Char") == 0)
    {
        vt = kVtUi2;
    }
    else if (std::strcmp(name, "System.Int32") == 0)
    {
        vt = kVtI4;
    }
    else if (std::strcmp(name, "System.UInt32") == 0)
    {
        vt = kVtUi4;
    }
    else if (std::strcmp(name, "System.Int64") == 0)
    {
        vt = kVtI8;
    }
    else if (std::strcmp(name, "System.UInt64") == 0)
    {
        vt = kVtUi8;
    }
    else if (std::strcmp(name, "System.Single") == 0)
    {
        vt = kVtR4;
    }
    else if (std::strcmp(name, "System.Double") == 0)
    {
        vt = kVtR8;
    }
    else if (std::strcmp(name, "System.Boolean") == 0)
    {
        vt = kVtBool;
    }

    if (vt == 0)
    {
        il2cpp_throw_not_supported_exception(
            "GetNativeVariantForObject: the boxed type is outside the ILC VARIANT "
            "subset (primitives only; no BSTR/DECIMAL/DATE/COM object marshaling)");
    }

    std::memcpy(variant + kVtOffset, &vt, sizeof(vt));
    unsigned char* data = variant + kDataOffset;
    switch (vt)
    {
        case kVtUi1:
        {
            const uint8_t v = static_cast<uint8_t>(icall_unbox_byte(obj));
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtI1:
        {
            const int8_t v = static_cast<int8_t>(icall_unbox_sbyte(obj));
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtI2:
        {
            const int16_t v = static_cast<int16_t>(icall_unbox_short(obj));
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtUi2:
        {
            const uint16_t v = static_cast<uint16_t>(
                (std::strcmp(name, "System.Char") == 0) ? icall_unbox_char(obj)
                                                        : icall_unbox_ushort(obj));
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtI4:
        {
            const int32_t v = icall_unbox_int32(obj);
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtUi4:
        {
            const uint32_t v = static_cast<uint32_t>(icall_unbox_uint(obj));
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtI8:
        {
            const int64_t v = icall_unbox_long(obj);
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtUi8:
        {
            const uint64_t v = icall_unbox_ulong(obj);
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtR4:
        {
            const float v = static_cast<float>(icall_unbox_float(obj));
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtR8:
        {
            const double v = icall_unbox_double(obj);
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        case kVtBool:
        {
            const int16_t v = icall_unbox_bool(obj) ? -1 : 0; // VARIANT_BOOL
            std::memcpy(data, &v, sizeof(v));
            break;
        }
        default:
            ThrowUnsupportedVt(vt); // unreachable
            break;
    }
}

//------------------------------------------------------------------------------
// icall_marshal_get_objects_for_native_variants -- Marshal.GetObjectsForNativeVariants
// (IntPtr, int): decode 'cVars' consecutive 16-byte VARIANTs into a managed
// object[]. A per-slot decode error propagates (net48 aborts the whole
// conversion on the first unsupported VARIANT).
//------------------------------------------------------------------------------
Il2CppObject* icall_marshal_get_objects_for_native_variants(intptr_t aSrcNativeVariant, int32_t cVars)
{
    if (aSrcNativeVariant == 0)
    {
        il2cpp_throw_argument_null_exception("aSrcNativeVariant");
    }
    if (cVars < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("cVars", nullptr);
    }
    Il2CppArray* arr = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, cVars);
    const unsigned char* base = reinterpret_cast<const unsigned char*>(aSrcNativeVariant);
    for (int32_t i = 0; i < cVars; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(arr, i));
        *slot = icall_marshal_get_object_for_native_variant(
            reinterpret_cast<intptr_t>(base + static_cast<size_t>(i) * kVariantSize));
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// icall_marshal_is_com_object -- Marshal.IsComObject(object): net48 returns
// true only for COM RCW/CCW wrappers; ILC has no COM runtime, so a non-null
// object is never a COM wrapper -> always false.
//------------------------------------------------------------------------------
bool icall_marshal_is_com_object(Il2CppObject* o)
{
    if (o == nullptr)
    {
        il2cpp_throw_argument_null_exception("o");
    }
    return false;
}

} // namespace runtime
} // namespace il2cpp
