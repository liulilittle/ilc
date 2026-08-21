//------------------------------------------------------------------------------
// marshalext4.cpp -- Marshal medium-priority gap batch (net48 inventory §4
// line 971: DestroyStructure; §4/§5 task picks: SizeOf-object and the
// elem-width Copy forms). Declarations in il2cpp_marshal_ext.h.
//
// This translation unit adds the three surface groups the transpiler maps
// Marshal.* calls onto:
//
//   1. icall_marshal_destroy_structure -- net48 Marshal.DestroyStructure:
//      frees the substructures (BSTR, delegates, arrays) embedded in a native
//      memory block, using the struct layout from the Type. ILC has NO COM/
//      BSTR marshalling and the M4 struct is an all-int32 byte buffer with no
//      embedded substructures, so after argument validation this is a
//      documented NO-OP (nothing to free). The Type argument crosses as the
//      ILC Type VALUE -- the full-name string (il2cpp_typeext.h assumption 1).
//   2. icall_marshal_size_of_object -- net48 Marshal.SizeOf(object) /
//      SizeOf<T>(T): the marshal size of a boxed value's RUNTIME type. The
//      box class name maps to a net48 TypeCode (the full primitive table --
//      an extension of the M4-scope typecheck table), then il2cpp_marshal_size_of
//      resolves the size (reused, DRY). A non-marshalable boxed type (String
//      / Object / unknown) raises NotSupportedException -- the layer's
//      established no-fixed-native-size policy (marshalext2.h), a documented
//      exception-type divergence from net48's ArgumentException.
//   3. icall_marshal_copy_to/from_unmanaged_elem -- the elem-WIDTH-aware
//      Marshal.Copy forms. The runtime.cpp int32-stride icalls
//      (icall_marshal_copy_to_unmanaged/from_unmanaged) only handle
//      elemSize==4; these dispatch on arr->elemSize so byte/short/char/int/
//      long/float/double/IntPtr arrays copy the correct byte count per
//      element. A reference-element array (string[]/object[]) is rejected with
//      NotSupportedException -- its payload is raw object refs, and copying
//      them as bytes into/out of native memory is a GC hazard (net48 throws
//      ArgumentException for unsupported element types). net48 exception
//      semantics (ArgumentNullException / ArgumentOutOfRangeException) replace
//      the old icalls' loud abort.
//
// EXCEPTION MAPPING (managed semantics via il2cpp_exception_ext.h)
//   destroy_structure: ptr == 0 -> ArgumentNullException("ptr"); null Type
//     -> ArgumentNullException("structuretype"); non-string Type value ->
//     ArgumentException. No memory is modified (documented no-op).
//   size_of_object: null -> ArgumentNullException("structure"); a boxed type
//     with no fixed native size (String/Object/unknown) -> NotSupportedException
//     (same policy as il2cpp_marshal_size_of, which is reused here).
//   copy_*_elem: null array/src/dest -> ArgumentNullException; a negative
//     startIndex/length or a window past the array end -> ArgumentOutOfRangeException
//     (net48 Span semantics).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_marshal_ext2.h" // il2cpp_marshal_size_of (net48 TypeCode -> size)
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Box-class name -> net48 TypeCode table (keep in sync with typecheck.cpp's
// icall_object_get_type_code, typeext.cpp, and StackFold.TypeCodeOfTypeName).
// This is the FULL primitive table -- an extension of the M4-scope typecheck
// table (which maps Int32/Boolean/Char/Double/String/Decimal only) so that
// SizeOf((object)5L) == 8 etc. resolve correctly for every boxed primitive.
// Values are the net48 TypeCode constants (marshalext2.h's convention).
//------------------------------------------------------------------------------
struct PrimitiveCodeEntry
{
    const char* name;
    int32_t     typeCode;
};

constexpr PrimitiveCodeEntry kBoxPrimitiveCodes[] = {
    { "System.Boolean",  3  },
    { "System.Char",     4  },
    { "System.SByte",    5  },
    { "System.Byte",     6  },
    { "System.Int16",    7  },
    { "System.UInt16",   8  },
    { "System.Int32",    9  },
    { "System.UInt32",   10 },
    { "System.Int64",    11 },
    { "System.UInt64",   12 },
    { "System.Single",   13 },
    { "System.Double",   14 },
    { "System.Decimal",  15 },
    { "System.String",   18 }, // mapped so SizeOf((object)"...") reaches the
                               // no-fixed-size path (NotSupportedException)
};

// Resolve the box class name to a net48 TypeCode; TypeCode.Object (1) when
// the name is unknown (non-primitive box / malformed descriptor).
int32_t TypeCodeOfBoxName(const char* name)
{
    if (name == nullptr)
    {
        return 1; // TypeCode.Object -- malformed descriptor fallback
    }
    for (const auto& entry : kBoxPrimitiveCodes)
    {
        if (std::strcmp(name, entry.name) == 0)
        {
            return entry.typeCode;
        }
    }
    return 1; // TypeCode.Object
}

} // namespace

//------------------------------------------------------------------------------
// icall_marshal_destroy_structure -- Marshal.DestroyStructure(IntPtr, Type).
// net48 frees the embedded substructures (BSTR / delegates / nested arrays)
// at the native block, walking the struct layout from the Type. ILC has no
// COM/BSTR marshalling and the M4 struct is an all-int32 byte buffer, so
// there is NOTHING to free -- this validates the arguments per net48 and is
// otherwise a documented no-op (the memory block itself is untouched; freeing
// the block is Marshal.FreeHGlobal's job, never DestroyStructure's).
//------------------------------------------------------------------------------
void icall_marshal_destroy_structure(intptr_t ptr, Il2CppObject* structureType)
{
    if (ptr == 0)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    if (structureType == nullptr)
    {
        il2cpp_throw_argument_null_exception("structuretype");
    }
    // The ILC Type VALUE is the type's full-name string. A non-string object
    // is a malformed Type argument (net48: "type must be a runtime type").
    if (structureType->klass != il2cpp_string_class())
    {
        il2cpp_throw_argument_exception(
            "structuretype must be a runtime Type (the type-name string)");
    }
    // Documented no-op: ILC M4 structs are all-int32 byte buffers with no
    // embedded substructures to free (no COM / BSTR / delegate marshalling).
}

//------------------------------------------------------------------------------
// icall_marshal_size_of_object -- Marshal.SizeOf(object) / SizeOf<T>(T).
// net48 resolves the marshal size from the value's RUNTIME type. ILC maps the
// box class name to a net48 TypeCode (full primitive table above) and reuses
// il2cpp_marshal_size_of (DRY) for the size. A boxed type with no fixed
// native size (String / Object / unknown) -> NotSupportedException, the
// layer's established policy.
//------------------------------------------------------------------------------
int32_t icall_marshal_size_of_object(Il2CppObject* structure)
{
    if (structure == nullptr)
    {
        il2cpp_throw_argument_null_exception("structure");
    }
    const char* name = (structure->klass != nullptr) ? structure->klass->name : nullptr;
    const int32_t code = TypeCodeOfBoxName(name);
    return il2cpp_marshal_size_of(code);
}

//------------------------------------------------------------------------------
// Shared range validation for the elem-width Copy icalls (net48 Span
// semantics): a negative startIndex or length, or a window past the array
// end, raises ArgumentOutOfRangeException. 'what' names the array parameter
// ("source" / "destination") for the window-overflow case.
//------------------------------------------------------------------------------
namespace {

void CheckCopyRange(int32_t startIndex, int32_t length, int32_t arrayLength, const char* what)
{
    if (startIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("length", nullptr);
    }
    if (static_cast<int64_t>(startIndex) + static_cast<int64_t>(length) >
        static_cast<int64_t>(arrayLength))
    {
        il2cpp_throw_argument_out_of_range_exception(what, nullptr);
    }
}

} // namespace

//------------------------------------------------------------------------------
// icall_marshal_copy_to_unmanaged_elem -- Marshal.Copy(array, startIndex,
// nativeDest, length) with the array's element width (arr->elemSize). Copies
// length * elemSize bytes: arr[startIndex .. startIndex+length-1] -> dest.
//------------------------------------------------------------------------------
void icall_marshal_copy_to_unmanaged_elem(Il2CppArray* array, int32_t startIndex,
                                          void* destination, int32_t length)
{
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
    }
    if (destination == nullptr)
    {
        il2cpp_throw_argument_null_exception("destination");
    }
    if (array->isRefElem)
    {
        // net48 Marshal.Copy supports only the 8 blittable element kinds
        // (byte/sbyte/char/short/ushort/int/uint/long/ulong/float/double/
        // IntPtr). A reference-element array (string[]/object[]) has raw object
        // references as its payload -- copying those bytes into a native buffer
        // (or back into the array data) would leak/corrupt managed object refs,
        // so the layer rejects it loudly (net48's ArgumentException becomes the
        // layer's NotSupportedException convention).
        il2cpp_throw_not_supported_exception(
            "Marshal.Copy: reference-element arrays are not blittable; only "
            "primitive element kinds (byte/short/char/int/long/float/double/"
            "IntPtr) are supported");
    }
    CheckCopyRange(startIndex, length, array->length, "source");
    const size_t elemSize = static_cast<size_t>(array->elemSize);
    const size_t byteCount = static_cast<size_t>(length) * elemSize;
    std::memcpy(destination,
                array->data + static_cast<size_t>(startIndex) * elemSize,
                byteCount);
}

//------------------------------------------------------------------------------
// icall_marshal_copy_from_unmanaged_elem -- Marshal.Copy(nativeSrc, array,
// startIndex, length) with the array's element width (arr->elemSize). Copies
// length * elemSize bytes: source -> arr[startIndex .. startIndex+length-1].
//------------------------------------------------------------------------------
void icall_marshal_copy_from_unmanaged_elem(void* source, Il2CppArray* array,
                                            int32_t startIndex, int32_t length)
{
    if (source == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
    }
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("destination");
    }
    if (array->isRefElem)
    {
        // Mirror of copy_to_unmanaged_elem's guard: a reference-element
        // destination array would have object references overwritten with raw
        // native bytes -- managed-heap corruption. Reject loudly.
        il2cpp_throw_not_supported_exception(
            "Marshal.Copy: reference-element arrays are not blittable; only "
            "primitive element kinds (byte/short/char/int/long/float/double/"
            "IntPtr) are supported");
    }
    CheckCopyRange(startIndex, length, array->length, "destination");
    const size_t elemSize = static_cast<size_t>(array->elemSize);
    const size_t byteCount = static_cast<size_t>(length) * elemSize;
    std::memcpy(array->data + static_cast<size_t>(startIndex) * elemSize,
                source,
                byteCount);
}

} // namespace runtime
} // namespace il2cpp
