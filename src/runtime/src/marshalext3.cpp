//------------------------------------------------------------------------------
// marshalext3.cpp -- Marshal gap batch (net48 inventory section 5 items 6/8/9 +
// the SEH queries; declarations in il2cpp_marshal_ext.h).
//
// This translation unit adds the four icalls the transpiler maps the
// net48 Marshal surface onto (the header holds the contracts):
//
//   1. icall_marshal_unsafe_addr_of_pinned_array_element -- the address of
//      element 'index' (array data base + index * elemSize), as the IntPtr
//      carrier (intptr_t).
//   2. icall_marshal_get_hr_for_exception -- net48 Marshal.GetHRForException:
//      the exception's HRESULT. ILC exceptions carry no instance _HResult
//      field (the managed Exception object is the catch handler's interned
//      type-name string), so this maps the exception TYPE to its net48
//      default HResult.
//   3. icall_marshal_get_exception_code / _get_exception_pointers -- the
//      x86-only SEH queries. ILC runs in a fully managed context with no SEH,
//      so GetExceptionCode always returns 0 and GetExceptionPointers always
//      returns IntPtr.Zero on every platform. impl-p0b MEASURED this against
//      the .NET Framework 4.8 x64 oracle (tests/cases/probe-p0b G4 section):
//      GetExceptionCode() == 0 and GetExceptionPointers() == IntPtr.Zero,
//      byte-identical to these two returns.
//
// EXCEPTION MAPPING (managed semantics via il2cpp_exception_ext.h)
//   unsafe_addr_of_pinned_array_element: null array -> ArgumentNullException;
//     index < 0 || index >= length -> IndexOutOfRangeException (net48 is
//     UNDEFINED on invalid input -- "passing in invalid index or an array
//     that is not pinned can cause unexpected results"; ILC bounds-checks as
//     a safe superset).
//   get_hr_for_exception: null -> 0 (net48 `e?.HResult ?? 0`); unknown or
//     non-string object -> COR_E_EXCEPTION (0x80131500), the base default.
//   get_exception_code / _pointers: never throw (constant semantics).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"
#include "il2cpp_marshal_ext2.h"

#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_pinvoke_struct.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// icall_marshal_unsafe_addr_of_pinned_array_element -- Marshal.
// UnsafeAddrOfPinnedArrayElement(Array, int): the address of element 'index'
// = array data base + index * elemSize. See the header's PIN LIMITATION note:
// ILC has no real pinning, so the address is only valid while the array stays
// rooted and no collection runs in between (net48 requires a prior
// GCHandle.Alloc(arr, Pinned)).
//------------------------------------------------------------------------------
intptr_t icall_marshal_unsafe_addr_of_pinned_array_element(Il2CppArray* array, int32_t index)
{
    if (array == nullptr)
    {
        il2cpp_throw_argument_null_exception("arr");
    }
    if (index < 0 || index >= array->length)
    {
        il2cpp_throw_index_out_of_range_exception(nullptr);
    }
    const size_t byteOffset = static_cast<size_t>(index) * static_cast<size_t>(array->elemSize);
    return static_cast<intptr_t>(
        reinterpret_cast<uintptr_t>(array->data + byteOffset));
}

//------------------------------------------------------------------------------
// net48 default HResult by exception TYPE (short name key). These are the
// documented .NET Framework default _HResult values the exception ctors set
// via SetErrorCode -- the value Marshal.GetHRForException(e) returns when the
// program never overrides HResult. Values align with the corert HResults.cs
// constants (reference/corert/src/Common/src/System/HResults.cs). The base
// fallback is COR_E_EXCEPTION (0x80131500).
//------------------------------------------------------------------------------
namespace {

struct ExceptionHResultEntry
{
    const char* shortName;
    int32_t     hresult;
};

constexpr ExceptionHResultEntry kExceptionHResults[] = {
    // Base / runtime plumbing
    { "Exception",                  static_cast<int32_t>(0x80131500) }, // COR_E_EXCEPTION
    { "SystemException",            static_cast<int32_t>(0x80131501) }, // COR_E_SYSTEM
    { "ApplicationException",       static_cast<int32_t>(0x80131600) }, // COR_E_APPLICATION
    { "ExecutionEngineException",   static_cast<int32_t>(0x80131506) }, // COR_E_EXECUTIONENGINE
    // Argument family
    { "ArgumentException",          static_cast<int32_t>(0x80070057) }, // COR_E_ARGUMENT (E_INVALIDARG)
    { "ArgumentNullException",      static_cast<int32_t>(0x80004003) }, // E_POINTER
    { "ArgumentOutOfRangeException",static_cast<int32_t>(0x80131502) }, // COR_E_ARGUMENTOUTOFRANGE
    // Numeric / arithmetic
    { "ArithmeticException",        static_cast<int32_t>(0x80070216) }, // COR_E_ARITHMETIC
    { "DivideByZeroException",      static_cast<int32_t>(0x80020012) }, // COR_E_DIVIDEBYZERO (DISP_E_DIVBYZERO)
    { "OverflowException",          static_cast<int32_t>(0x80131516) }, // COR_E_OVERFLOW
    { "NotFiniteNumberException",   static_cast<int32_t>(0x80131528) }, // COR_E_NOTFINITENUMBER
    // Memory / stack
    { "OutOfMemoryException",       static_cast<int32_t>(0x8007000E) }, // COR_E_OUTOFMEMORY
    { "StackOverflowException",     static_cast<int32_t>(0x800703E9) }, // COR_E_STACKOVERFLOW
    { "InsufficientExecutionStackException",
                                    static_cast<int32_t>(0x80131578) }, // COR_E_INSUFFICIENTEXECUTIONSTACK
    // Runtime type / cast / index
    { "InvalidCastException",       static_cast<int32_t>(0x80004002) }, // COR_E_INVALIDCAST (E_NOINTERFACE)
    { "IndexOutOfRangeException",   static_cast<int32_t>(0x80131508) }, // COR_E_INDEXOUTOFRANGE
    { "RankException",              static_cast<int32_t>(0x80131517) }, // COR_E_RANK
    { "ArrayTypeMismatchException", static_cast<int32_t>(0x80131503) }, // COR_E_ARRAYTYPEMISMATCH
    { "TypeInitializationException",static_cast<int32_t>(0x80131534) }, // COR_E_TYPEINITIALIZATION
    { "TypeLoadException",          static_cast<int32_t>(0x80131522) }, // COR_E_TYPELOAD
    { "EntryPointNotFoundException",static_cast<int32_t>(0x80131523) }, // COR_E_ENTRYPOINTNOTFOUND
    { "DllNotFoundException",       static_cast<int32_t>(0x80131524) }, // COR_E_DLLNOTFOUND
    { "BadImageFormatException",    static_cast<int32_t>(0x8007000B) }, // COR_E_BADIMAGEFORMAT
    { "InvalidProgramException",    static_cast<int32_t>(0x8013153A) }, // COR_E_INVALIDPROGRAM
    // Common operation / state
    { "InvalidOperationException",  static_cast<int32_t>(0x80131509) }, // COR_E_INVALIDOPERATION
    { "NotSupportedException",      static_cast<int32_t>(0x80131515) }, // COR_E_NOTSUPPORTED
    { "NotImplementedException",    static_cast<int32_t>(0x80004001) }, // E_NOTIMPL
    { "NullReferenceException",     static_cast<int32_t>(0x80004003) }, // E_POINTER
    { "ObjectDisposedException",    static_cast<int32_t>(0x80131622) }, // COR_E_OBJECTDISPOSED
    { "SynchronizationLockException",static_cast<int32_t>(0x80131518) }, // COR_E_SYNCHRONIZATIONLOCK
    { "OperationCanceledException", static_cast<int32_t>(0x8013153B) }, // COR_E_OPERATIONCANCELED
    { "TimeoutException",           static_cast<int32_t>(0x80131505) }, // COR_E_TIMEOUT
    { "ThreadAbortException",       static_cast<int32_t>(0x80131530) }, // COR_E_THREADABORTED
    { "ThreadInterruptedException", static_cast<int32_t>(0x80131519) }, // COR_E_THREADINTERRUPTED
    { "ThreadStateException",       static_cast<int32_t>(0x80131520) }, // COR_E_THREADSTATE
    // Text / parsing
    { "FormatException",            static_cast<int32_t>(0x80131537) }, // COR_E_FORMAT
    { "UriFormatException",         static_cast<int32_t>(0x80131537) }, // COR_E_FORMAT
    // Collections
    { "KeyNotFoundException",       static_cast<int32_t>(0x80131577) }, // COR_E_KEYNOTFOUND
    { "InvalidFilterCriteriaException",
                                    static_cast<int32_t>(0x80131601) }, // COR_E_INVALIDFILTERCRITERIA
    // IO
    { "IOException",                static_cast<int32_t>(0x80131620) }, // COR_E_IO
    { "FileNotFoundException",      static_cast<int32_t>(0x80070002) }, // COR_E_FILENOTFOUND
    { "DirectoryNotFoundException", static_cast<int32_t>(0x80070003) }, // COR_E_DIRECTORYNOTFOUND
    { "EndOfStreamException",       static_cast<int32_t>(0x80070026) }, // COR_E_ENDOFSTREAM
    { "PathTooLongException",       static_cast<int32_t>(0x800700CE) }, // COR_E_PATHTOOLONG
    { "UnauthorizedAccessException",static_cast<int32_t>(0x80070005) }, // COR_E_UNAUTHORIZEDACCESS
    { "FileLoadException",          static_cast<int32_t>(0x80131621) }, // COR_E_FILELOAD
    // Security / access
    { "SecurityException",          static_cast<int32_t>(0x8013150A) }, // COR_E_SECURITY
    { "FieldAccessException",       static_cast<int32_t>(0x80131507) }, // COR_E_FIELDACCESS
    { "MethodAccessException",      static_cast<int32_t>(0x80131510) }, // COR_E_METHODACCESS
    { "MemberAccessException",      static_cast<int32_t>(0x8013151A) }, // COR_E_MEMBERACCESS
    { "MissingMemberException",     static_cast<int32_t>(0x80131512) }, // COR_E_MISSINGMEMBER
    { "MissingFieldException",      static_cast<int32_t>(0x80131511) }, // COR_E_MISSINGFIELD
    { "MissingMethodException",     static_cast<int32_t>(0x80131513) }, // COR_E_MISSINGMETHOD
    { "TargetException",            static_cast<int32_t>(0x80131603) }, // COR_E_TARGET
    { "TargetInvocationException",  static_cast<int32_t>(0x80131604) }, // COR_E_TARGETINVOCATION
    { "TargetParameterCountException",
                                    static_cast<int32_t>(0x8002000E) }, // COR_E_TARGETPARAMCOUNT
};

// Look up the net48 default HResult for a short type name ("ArgumentException").
// Returns COR_E_EXCEPTION when the type is unknown (the base-class default).
int32_t LookupExceptionHResult(const char* shortName)
{
    for (const auto& entry : kExceptionHResults)
    {
        if (std::strcmp(shortName, entry.shortName) == 0)
        {
            return entry.hresult;
        }
    }
    return static_cast<int32_t>(0x80131500); // COR_E_EXCEPTION
}

// Is this descriptor a VALUE type (its base chain reaches System.ValueType)?
// Same criterion as boxingext2.cpp's IsValueTypeClass (that helper is
// file-local there): the transpiler wires every generated user-struct {T}_class
// to &System_ValueType_class, while the builtin primitive box classes (Int32
// etc., base System.Object) and enum boxes (base System.Enum) do not reach it.
// It matters for the boxed-payload size: a value-type descriptor reports
// instanceSize WITHOUT the object header (the physical box is
// kBoxPayloadOffset + sizeof(T), so instanceSize == sizeof(T)), whereas a
// boxed REFERENCE object counts the header inside instanceSize.
bool MarshalBoxIsValueType(Il2CppClass* klass)
{
    for (Il2CppClass* cur = klass; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == &System_ValueType_class)
        {
            return true;
        }
    }
    return false;
}

} // namespace

//------------------------------------------------------------------------------
// icall_marshal_get_hr_for_exception -- Marshal.GetHRForException(Exception).
// The managed Exception object is the catch handler's interned type-name
// string (a full name like "System.ArgumentException" is accepted; the LAST
// '.'-suffixed segment is the table key). null -> 0; a non-string or unknown
// type -> COR_E_EXCEPTION.
//------------------------------------------------------------------------------
int32_t icall_marshal_get_hr_for_exception(Il2CppObject* exception)
{
    if (exception == nullptr)
    {
        return 0;
    }
    if (exception->klass != il2cpp_string_class())
    {
        // Not an exception type-name string: no type information to map.
        return static_cast<int32_t>(0x80131500); // COR_E_EXCEPTION
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(exception);
    if (str->length <= 0)
    {
        return static_cast<int32_t>(0x80131500);
    }
    // The key is the last '.'-suffixed segment of the type name (the short
    // name), or the whole content when the name has no '.' (short already).
    // UTF-16 content is ASCII for these BCL type names, so a byte-wise scan
    // for '.' within the unit array is exact.
    const char16_t* const units = str->chars;
    const char16_t* lastDot = nullptr;
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (units[i] == u'.')
        {
            lastDot = units + i;
        }
    }
    const char16_t* key = (lastDot != nullptr) ? (lastDot + 1) : units;
    const int32_t keyLen = (lastDot != nullptr)
        ? static_cast<int32_t>(str->length - static_cast<int32_t>(lastDot - units) - 1)
        : str->length;
    // ASCII-only key: transcode into a stack buffer and table-lookup by name.
    // (A non-ASCII type name cannot match any BCL entry and falls through to
    // the base default -- the buffer write is bounded by the table's maximum
    // key length.)
    char name[48];
    if (keyLen >= static_cast<int32_t>(sizeof(name)))
    {
        return static_cast<int32_t>(0x80131500);
    }
    for (int32_t i = 0; i < keyLen; ++i)
    {
        const char16_t u = key[i];
        if (u > 0x7F)
        {
            return static_cast<int32_t>(0x80131500);
        }
        name[i] = static_cast<char>(u);
    }
    name[keyLen] = '\0';
    return LookupExceptionHResult(name);
}

//------------------------------------------------------------------------------
// icall_marshal_get_exception_code -- Marshal.GetExceptionCode(): the x86
// SEH exception code, meaningful only inside a native SEH filter. ILC has no
// SEH (fully managed context) on any platform -> always 0.
//------------------------------------------------------------------------------
int32_t icall_marshal_get_exception_code()
{
    return 0;
}

//------------------------------------------------------------------------------
// icall_marshal_get_exception_pointers -- Marshal.GetExceptionPointers():
// the x86 SEH EXCEPTION_POINTERS address, meaningful only inside a native
// SEH filter. ILC has no SEH on any platform -> always IntPtr.Zero.
//------------------------------------------------------------------------------
intptr_t icall_marshal_get_exception_pointers()
{
    return 0;
}

//------------------------------------------------------------------------------
// D-MAR-1 (2026-08-12): Marshal.SizeOf/OffsetOf for the REGISTERED struct
// family (System.Drawing.Point/Rectangle/System.Windows.Forms.Message). The
// struct layout registry (il2cpp_pinvoke_struct.h) is the single layout
// authority: the named icalls resolve the managed type-name STRING (the ILC
// Type VALUE -- typeof(X) folds to the full name) through
// il2cpp_pinvoke_struct_kind_by_name; the per-struct icalls hard-code the
// kind (the transpiler picks them from the generic argument). OffsetOf
// resolves the field by its net48 name (il2cpp_pinvoke_struct_field_offset).
//
// Exception mapping (net48 semantics):
//   SizeOf:   null type-name string      -> ArgumentNullException
//             unregistered type          -> NotSupportedException
//   OffsetOf: null type-name/field name  -> ArgumentNullException
//             unregistered type          -> NotSupportedException
//             unknown field name         -> ArgumentException
//------------------------------------------------------------------------------

namespace {

// Transcode an ASCII-only managed string into a caller char buffer. Returns
// false on null / too long / any non-ASCII unit (BCL type and field names
// are ASCII; a non-ASCII name cannot match the registry).
bool Utf16ToAscii(const Il2CppString* s, char* out, int32_t outSize)
{
    if (s == nullptr || s->length < 0 || s->length >= outSize)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        const char16_t u = s->chars[i];
        if (u > 0x7F)
        {
            return false;
        }
        out[i] = static_cast<char>(u);
    }
    out[s->length] = '\0';
    return true;
}

// Resolve a struct kind by its ASCII full name; NotSupportedException for an
// unregistered name (never a default kind).
int32_t StructKindByAsciiName(const char* name)
{
    const int32_t kind = il2cpp::il2cpp_pinvoke_struct_kind_by_name(name);
    if (kind < 0)
    {
        il2cpp_throw_not_supported_exception(
            "Marshal.SizeOf/OffsetOf: type is not a registered struct -- only System.Drawing.Rectangle, System.Drawing.Point and System.Windows.Forms.Message have a native layout registry");
        return 0; // unreachable (the throw above always throws)
    }
    return kind;
}

// Field offset by name inside a kind; ArgumentException for an unknown field
// (net48 Marshal.OffsetOf throws ArgumentException for a non-existent field).
int32_t StructFieldOffsetByAsciiName(int32_t kind, const char* fieldName)
{
    const int32_t offset = il2cpp::il2cpp_pinvoke_struct_field_offset(
        static_cast<Il2CppPinvokeStructKind>(kind), fieldName);
    if (offset < 0)
    {
        il2cpp_throw_argument_exception(
            "Marshal.OffsetOf: the registered struct has no field with that name");
        return 0; // unreachable (the throw above always throws)
    }
    return offset;
}

// Shared null/ASCII/kind resolution for the named SizeOf/OffsetOf icalls.
int32_t StructKindFromManagedName(Il2CppString* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
        return 0; // unreachable (the throw above always throws)
    }
    char name[64];
    if (!Utf16ToAscii(typeName, name, static_cast<int32_t>(sizeof(name))))
    {
        il2cpp_throw_argument_exception(
            "Marshal.SizeOf/OffsetOf: the type name must be a short ASCII full type name");
        return 0; // unreachable (the throw above always throws)
    }
    return StructKindByAsciiName(name);
}

// ASCII field name from a managed string argument.
void FieldNameFromManagedString(Il2CppString* fieldName, char* out, int32_t outSize)
{
    if (fieldName == nullptr)
    {
        il2cpp_throw_argument_null_exception("fieldName");
    }
    if (!Utf16ToAscii(fieldName, out, outSize))
    {
        il2cpp_throw_argument_exception(
            "Marshal.OffsetOf: the field name must be a short ASCII name");
    }
}

// net48 System.Windows.Forms.Message MANAGED marshal layout (ipaudit3): the
// 5-field struct hwnd/msg/wParam/lParam/result -- 40 bytes x64 / 20 x86 --
// distinct from the NATIVE tagMSG registry layout (48/28) that stays the
// P/Invoke marshaling authority.
int32_t MessageNet48WParamOffset()
{
    const int32_t p = static_cast<int32_t>(sizeof(void*));
    return (p + 4 + (p - 1)) & ~(p - 1);
}

int32_t MessageNet48Size()
{
    const int32_t p = static_cast<int32_t>(sizeof(void*));
    return MessageNet48WParamOffset() + 3 * p; // result (+ p)
}

int32_t MessageNet48FieldOffset(const char* field)
{
    const int32_t p = static_cast<int32_t>(sizeof(void*));
    if (std::strcmp(field, "hwnd") == 0) return 0;
    if (std::strcmp(field, "msg") == 0) return p;
    if (std::strcmp(field, "wParam") == 0) return MessageNet48WParamOffset();
    if (std::strcmp(field, "lParam") == 0) return MessageNet48WParamOffset() + p;
    if (std::strcmp(field, "result") == 0) return MessageNet48WParamOffset() + 2 * p;
    return -1; // unknown field -- caller raises the net48 ArgumentException
}

// impl-io2: the net48 SizeOf layout registry, keyed by the type's ASCII full
// name -- the PRIMITIVE table (default Ansi CharSet: Boolean 4, Char 1),
// IntPtr/UIntPtr (pointer width), the net48 managed Message size and the
// registered struct family. Shared by the typeof(Type) string form and the
// generic SizeOf<T>() form (the transpiler passes T's full name as a C++
// string literal).
int32_t MarshalSizeOfNameAscii(const char* name)
{
    // ipaudit2: the PRIMITIVE typeof(X) names resolve through the same net48
    // table as the object form (default Ansi CharSet: Boolean 4, Char 1).
    static const struct { const char* full; int32_t code; } kPrimitives[] = {
        { "System.Boolean", 3 }, { "System.Char", 4 }, { "System.SByte", 5 },
        { "System.Byte", 6 }, { "System.Int16", 7 }, { "System.UInt16", 8 },
        { "System.Int32", 9 }, { "System.UInt32", 10 }, { "System.Int64", 11 },
        { "System.UInt64", 12 }, { "System.Single", 13 }, { "System.Double", 14 },
        { "System.Decimal", 15 },
    };
    for (size_t i = 0; i < sizeof(kPrimitives) / sizeof(kPrimitives[0]); ++i)
    {
        if (std::strcmp(name, kPrimitives[i].full) == 0)
        {
            return il2cpp_marshal_size_of(kPrimitives[i].code);
        }
    }
    if (std::strcmp(name, "System.IntPtr") == 0
        || std::strcmp(name, "System.UIntPtr") == 0)
    {
        return static_cast<int32_t>(sizeof(void*));
    }
    if (std::strcmp(name, "System.Windows.Forms.Message") == 0)
    {
        return MessageNet48Size();
    }
    // ipaudit2 follow-up: an unregistered type must throw the managed
    // NotSupportedException (same contract as the named OffsetOf path) -- the
    // raw kind lookup can return -1 and passing it to the registry aborts.
    const int32_t kind = StructKindByAsciiName(name);
    return il2cpp::il2cpp_pinvoke_struct_size(static_cast<Il2CppPinvokeStructKind>(kind));
}

} // namespace

//------------------------------------------------------------------------------
// Named SizeOf/OffsetOf (the typeof(Type) / typeof(Type),string forms). The
// ILC Type VALUE is the full-name string; the registry is the layout source.
//------------------------------------------------------------------------------
int32_t il2cpp_marshal_size_of_struct_named(Il2CppString* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    char name[64];
    if (!Utf16ToAscii(typeName, name, static_cast<int32_t>(sizeof(name))))
    {
        il2cpp_throw_argument_exception(
            "Marshal.SizeOf/OffsetOf: the type name must be a short ASCII full type name");
    }
    return MarshalSizeOfNameAscii(name);
}

// impl-io2: the generic Marshal.SizeOf<T>() form. The transpiler knows the
// generic argument at compile time and passes T's full name as a C++ string
// literal (the same net48 layout registry as the typeof(Type) path above).
// An unregistered name throws the managed NotSupportedException (net48
// SizeOf<T> for a blittable struct is computable but this runtime has no
// layout descriptor for it).
int32_t icall_marshal_size_of_named_ascii(const char* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    return MarshalSizeOfNameAscii(typeName);
}

int32_t il2cpp_marshal_offset_of_struct_named(Il2CppString* typeName, Il2CppString* fieldName)
{
    const int32_t kind = StructKindFromManagedName(typeName);
    char field[48];
    FieldNameFromManagedString(fieldName, field, static_cast<int32_t>(sizeof(field)));
    if (kind == il2cpp::kPinvokeStructMsg)
    {
        const int32_t off = MessageNet48FieldOffset(field);
        if (off < 0)
        {
            il2cpp_throw_argument_exception(
                "Field name not found: the net48 Message fields are hwnd, msg, wParam, lParam, result");
        }
        return off;
    }
    return StructFieldOffsetByAsciiName(kind, field);
}

//------------------------------------------------------------------------------
// Per-struct SizeOf (the SizeOf<T>() generic form -- the transpiler selects
// the function from the compile-time generic argument, so no name crosses the
// boundary). MSG size is pointer-width-dependent (48 x64 / 28 x86) -- the
// runtime registry computes it, the transpiler must NOT hard-code it.
//------------------------------------------------------------------------------
int32_t il2cpp_marshal_size_of_struct_point()
{
    return il2cpp::il2cpp_pinvoke_struct_size(il2cpp::kPinvokeStructPoint);
}

int32_t il2cpp_marshal_size_of_struct_rect()
{
    return il2cpp::il2cpp_pinvoke_struct_size(il2cpp::kPinvokeStructRect);
}

int32_t il2cpp_marshal_size_of_struct_msg()
{
    // ipaudit3: SizeOf<T>() reports the NET48 managed Message size (40 x64),
    // not the native tagMSG size.
    return MessageNet48Size();
}

//------------------------------------------------------------------------------
// Per-struct OffsetOf (the OffsetOf<T>(string) generic form): only the field
// name string crosses; the kind is baked into the function.
//------------------------------------------------------------------------------
int32_t il2cpp_marshal_offset_of_struct_point(Il2CppString* fieldName)
{
    char field[48];
    FieldNameFromManagedString(fieldName, field, static_cast<int32_t>(sizeof(field)));
    return StructFieldOffsetByAsciiName(il2cpp::kPinvokeStructPoint, field);
}

int32_t il2cpp_marshal_offset_of_struct_rect(Il2CppString* fieldName)
{
    char field[48];
    FieldNameFromManagedString(fieldName, field, static_cast<int32_t>(sizeof(field)));
    return StructFieldOffsetByAsciiName(il2cpp::kPinvokeStructRect, field);
}

int32_t il2cpp_marshal_offset_of_struct_msg(Il2CppString* fieldName)
{
    char field[48];
    FieldNameFromManagedString(fieldName, field, static_cast<int32_t>(sizeof(field)));
    return StructFieldOffsetByAsciiName(il2cpp::kPinvokeStructMsg, field);
}

//------------------------------------------------------------------------------
// PtrToStructure/StructureToPtr byte semantics for the REGISTERED struct
// family. Every registered struct is BLITTABLE (managed sequential layout
// == native Win32 layout -- the registry's contract), so the copy is a
// size-checked memcpy in both directions, exactly like
// il2cpp_pinvoke_struct_to/from_native. These functions add the net48
// Marshal surface semantics (ArgumentNullException on a null buffer) and the
// by-VALUE result shape (PtrToStructure returns the struct value). The
// transpiler cannot reach them today: a PtrToStructure result needs a
// struct-typed local/temp, which requires the CodeGenerator TypeCppName /
// result-kind extension (documented in MethodMap.Marshal.cs). Unit tests
// (test_marshalstruct.cpp) pin the byte round-trip + null semantics.
//------------------------------------------------------------------------------
Il2CppPinvokePoint il2cpp_marshal_struct_point_load(const void* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    Il2CppPinvokePoint value;
    std::memcpy(&value, src, sizeof(Il2CppPinvokePoint));
    return value;
}

Il2CppPinvokeRect il2cpp_marshal_struct_rect_load(const void* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    Il2CppPinvokeRect value;
    std::memcpy(&value, src, sizeof(Il2CppPinvokeRect));
    return value;
}

Il2CppPinvokeMsg il2cpp_marshal_struct_msg_load(const void* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    Il2CppPinvokeMsg value;
    std::memcpy(&value, src, sizeof(Il2CppPinvokeMsg));
    return value;
}

void il2cpp_marshal_struct_point_store(void* dst, const Il2CppPinvokePoint* value)
{
    if (dst == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("structure");
    }
    std::memcpy(dst, value, sizeof(Il2CppPinvokePoint));
}

void il2cpp_marshal_struct_rect_store(void* dst, const Il2CppPinvokeRect* value)
{
    if (dst == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("structure");
    }
    std::memcpy(dst, value, sizeof(Il2CppPinvokeRect));
}

void il2cpp_marshal_struct_msg_store(void* dst, const Il2CppPinvokeMsg* value)
{
    if (dst == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("structure");
    }
    std::memcpy(dst, value, sizeof(Il2CppPinvokeMsg));
}

//------------------------------------------------------------------------------
// StructureToPtr runtime type dispatch: the managed struct object's TYPE
// (read from the object header's klass->name) selects the per-struct store
// icall. The object-typed first param of StructureToPtr(object, IntPtr, bool)
// erases the compile-time generic argument, so the dispatch happens here at
// runtime -- the single entry point the transpiler maps the generic
// StructureToPtr<T> onto when T is not statically known.
//------------------------------------------------------------------------------
void il2cpp_marshal_struct_store(Il2CppObject* structure, void* dst, bool fDeleteOld)
{
    if (structure == nullptr)
    {
        il2cpp_throw_argument_null_exception("structure");
    }
    if (dst == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    // net48 fDeleteOld semantics: DestroyStructure runs on the block CONTENTS
    // (releasing embedded reference members). For a blittable struct that is a
    // no-op; the caller-owned block itself is NEVER freed. Reference-bearing
    // structs stay NotSupported in the default arm below.
    (void)fDeleteOld;
    const char* const fullName = structure->klass->name;
    const int32_t kind = il2cpp::il2cpp_pinvoke_struct_kind_by_name(fullName);
    const void* data = reinterpret_cast<const void*>(&structure[1]);
    switch (kind)
    {
    case il2cpp::kPinvokeStructPoint:
        il2cpp_marshal_struct_point_store(dst, static_cast<const Il2CppPinvokePoint*>(data));
        return;
    case il2cpp::kPinvokeStructRect:
        il2cpp_marshal_struct_rect_store(dst, static_cast<const Il2CppPinvokeRect*>(data));
        return;
    case il2cpp::kPinvokeStructMsg:
        il2cpp_marshal_struct_msg_store(dst, static_cast<const Il2CppPinvokeMsg*>(data));
        return;
    default:
    {
        // User blittable struct fallback (ipaudit1): no reference fields ->
        // net48 copies the value layout byte for byte. The boxed payload
        // starts at &structure[1]. A VALUE-type descriptor (base chain reaches
        // System.ValueType) reports instanceSize as the payload size WITHOUT
        // the object header -- icall_box_user_struct allocates the box as
        // kBoxPayloadOffset + sizeof(T) and the descriptor's instanceSize is
        // sizeof(T) (the GN-20.4 compact value-type layout) -- so the payload
        // size IS instanceSize; only a boxed REFERENCE object (instanceSize
        // includes the header) subtracts it.
        if (structure->klass != nullptr && structure->klass->refFieldCount == 0)
        {
            const int32_t payload = MarshalBoxIsValueType(structure->klass)
                ? structure->klass->instanceSize
                : structure->klass->instanceSize
                      - static_cast<int32_t>(sizeof(Il2CppObject));
            if (payload > 0)
            {
                std::memcpy(dst, reinterpret_cast<const void*>(&structure[1]),
                            static_cast<size_t>(payload));
            }
            return;
        }
        il2cpp_throw_not_supported_exception(
            "StructureToPtr: the struct type carries reference fields or has no native layout -- only System.Drawing.Point, System.Drawing.Rectangle, System.Windows.Forms.Message and reference-free user structs are supported");
        return;
    }
    }
}

//------------------------------------------------------------------------------
// marshalrw (2026-09-13): il2cpp_marshal_struct_store_value -- the
// StructureToPtr<T> VALUE form. The generic T crosses the icall boundary as
// its native struct pointer (the transpiler passes &value + sizeof(T) + the
// generated class descriptor); no box is materialized. net48 semantics match
// il2cpp_marshal_struct_store above: fDeleteOld is the DestroyStructure-on-
// contents no-op for blittable data, the caller-owned block is never freed,
// reference-bearing / unknown structs stay NotSupported.
//------------------------------------------------------------------------------
void il2cpp_marshal_struct_store_value(Il2CppClass* klass, const void* value, void* dst,
                                       bool fDeleteOld, size_t payloadSize)
{
    if (klass == nullptr || value == nullptr)
    {
        il2cpp_throw_argument_null_exception("structure");
    }
    if (dst == nullptr)
    {
        il2cpp_throw_argument_null_exception("ptr");
    }
    (void)fDeleteOld;
    const int32_t kind = il2cpp::il2cpp_pinvoke_struct_kind_by_name(klass->name);
    switch (kind)
    {
    case il2cpp::kPinvokeStructPoint:
        il2cpp_marshal_struct_point_store(dst, static_cast<const Il2CppPinvokePoint*>(value));
        return;
    case il2cpp::kPinvokeStructRect:
        il2cpp_marshal_struct_rect_store(dst, static_cast<const Il2CppPinvokeRect*>(value));
        return;
    case il2cpp::kPinvokeStructMsg:
        il2cpp_marshal_struct_msg_store(dst, static_cast<const Il2CppPinvokeMsg*>(value));
        return;
    default:
        // User blittable struct (P25b value form): the caller's payloadSize
        // IS the struct size -- the value-type descriptor instanceSize is
        // sizeof(T) WITHOUT the object header (the same payload semantics the
        // boxed-object path above uses for value types).
        if (klass->refFieldCount == 0 && payloadSize > 0)
        {
            std::memcpy(dst, value, payloadSize);
            return;
        }
        il2cpp_throw_not_supported_exception(
            "StructureToPtr: the struct type carries reference fields or has no native layout -- only System.Drawing.Point, System.Drawing.Rectangle, System.Windows.Forms.Message and reference-free user structs are supported");
        return;
    }
}

} // namespace runtime
} // namespace il2cpp
