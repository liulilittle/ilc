//------------------------------------------------------------------------------
// typeext.cpp -- System.Type static-member primitives (see il2cpp_typeext.h).
//
// Conformance model (M4 scope, mirrors typecheck.cpp):
//   1. Identity:     target == source.
//   2. Array rule:   M4 SIMPLIFICATION -- when BOTH descriptors are array
//                    classes (name[0] == '[' -- the array-class marker, see
//                    array.cpp), the check succeeds on the '[' prefix alone;
//                    the ELEMENT type is not compared (weaker than .NET,
//                    documented in the header). Note the model does NOT
//                    special-case System.Object: an array descriptor has no
//                    base chain in this model, so an array is NOT assignable
//                    to System.Object here (matches the typecheck.cpp isinst
//                    shape for array classes).
//   3. Base chain:   source's baseClass-pointer walk contains target
//                    (derived -> base -> ... -> System.Object).
//   4. Interfaces:   the interfaces table of source and of every base class
//                    (a derived type inherits its base's interfaces -- same
//                    scan shape as boxing.cpp / typecheck.cpp).
//
// This deliberately duplicates typecheck.cpp's TypeIsInstance conformance
// logic (class-descriptor flavor) instead of delegating: TypeIsInstance is
// anonymous-namespace in another TU and the project convention (see
// typecheck.cpp header) is to duplicate rather than couple translation
// units. Keep the rule order in sync when the conformance model evolves.
//
// TypeCode mapping: intentionally IDENTICAL to icall_object_get_type_code
// (same net48 empirical values); keep the two functions in sync.
//------------------------------------------------------------------------------

#include "il2cpp_typeext.h"

#include "il2cpp_exception.h" // il2cpp_throw_new
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs

#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// Null-descriptor failure for the .NET static-member contract: a null Type
// argument is an ArgumentNullException. The chain suffix (",System.Exception")
// is the transpiler's chain format -- it lets il2cpp_exception_matches accept
// a catch of System.Exception.
[[noreturn]] void ThrowArgumentNull()
{
    il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                     "Value cannot be null.");
}

// Null-RECEIVER failure: net48 ((Type)null).IsAssignableFrom(x) throws
// NullReferenceException (probe-verified). Same carrier convention as
// ThrowArgumentNull.
[[noreturn]] void ThrowNullReference()
{
    il2cpp_throw_new("System.NullReferenceException,System.Exception",
                     "Object reference not set to an instance of an object.");
}

// The class-descriptor conformance test shared by is_assignable_from (see
// the file header for the rule order: identity -> array prefix -> base chain
// -> interface table scan). Mirror of typecheck.cpp's TypeIsInstance applied
// to (target, source) instead of (obj->klass, klass).
bool ClassIsAssignableFrom(Il2CppClass* target, Il2CppClass* source)
{
    if (target == nullptr || source == nullptr)
    {
        return false;
    }
    // 1. Identity.
    if (target == source)
    {
        return true;
    }
    // 2. Array conformance (M4 simplification): both classes are arrays
    //    ('[' name prefix) -> match on the prefix alone.
    if (target->name != nullptr && source->name != nullptr &&
        target->name[0] == '[' && source->name[0] == '[')
    {
        return true;
    }
    // 3. Base chain walk (starts at source; identity already handled above).
    for (Il2CppClass* cur = source; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == target)
        {
            return true;
        }
    }
    // 4. Interface conformance: scan the interfaces table of source AND its
    //    base chain (interfaces are inherited).
    for (Il2CppClass* scan = source; scan != nullptr; scan = scan->baseClass)
    {
        if (scan->interfaces == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < scan->interfaceCount; ++i)
        {
            if (scan->interfaces[i].iface == target)
            {
                return true;
            }
        }
    }
    return false;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_type_get_type_from_handle -- Type.GetTypeFromHandle simplified.
//
// net48 null contract (probe-verified against v4.0.30319): a null descriptor
// IS the default RuntimeTypeHandle, and Type.GetTypeFromHandle(default) ->
// null -- NOT a throw (this runtime models a RuntimeTypeHandle VALUE as the
// descriptor address, so null == default). A null descriptor is therefore
// NOT a malformed input for this STATIC member (unlike FullName below);
// returning the null Type mirrors net48 exactly.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_type_from_handle(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        return nullptr; // net48: Type.GetTypeFromHandle(default) -> null
    }
    // Simplified Type model: a Type is represented by its full NAME as a
    // managed string (see header assumption 1; same shape as
    // icall_object_get_type).
    const char* name = (klass->name != nullptr) ? klass->name : "System.Object";
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name));
}

//------------------------------------------------------------------------------
// il2cpp_type_get_type_from_handle_value -- the intptr_t handle-VALUE wrapper
// (p-refl2-gapmatrix E01/E02). The transpiler models a RuntimeTypeHandle
// VALUE as an intptr_t slot (reflecthandle.h's value model), so the IL passes
// the descriptor ADDRESS where the descriptor primitive wants Il2CppClass*.
// Cast and delegate -- identical null/identity semantics.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_type_from_handle_value(intptr_t handle)
{
    return il2cpp_type_get_type_from_handle(reinterpret_cast<Il2CppClass*>(handle));
}

//------------------------------------------------------------------------------
// il2cpp_type_get_element_type -- Type.GetElementType() simplified.
//
// net48 null contract (probe-verified against v4.0.30319): a null receiver
// (((Type)null).GetElementType()) throws NullReferenceException; a NON-array
// type returns null with NO throw (typeof(int).GetElementType() -> null).
// Array detection is the '['-prefix rule: an array descriptor's name is
// "[<element type name>" (array.cpp convention), so the element type name is
// the name with the leading '[' stripped ("[System.Int32" -> "System.Int32").
// A '['-prefixed descriptor whose name is ONLY "[" (a malformed empty element
// name) falls back to the element name "" -- the caller's malformed-input
// responsibility (the header's Array rule uses the same '['-prefix marker).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_element_type(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    const char* name = klass->name;
    if (name == nullptr || name[0] != '[')
    {
        // Non-array type: net48 typeof(X).GetElementType() -> null, no throw.
        return nullptr;
    }
    // Array descriptor: element type name = name without the leading '['.
    // Jagged arrays are handled naturally: the element of "[[System.Int32" is
    // "[System.Int32" (an array class) -- net48 typeof(int[][]).GetElementType()
    // == typeof(int[]), probe-verified.
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name + 1));
}

//------------------------------------------------------------------------------
// il2cpp_type_is_assignable_from -- Type.IsAssignableFrom primitive.
//
// net48 null contract (probe-verified): the RECEIVER (target) being null is a
// null Type -> NullReferenceException (((Type)null).IsAssignableFrom(x)
// throws NRE); the ARGUMENT (source) being null RETURNS false, no throw
// (typeof(int).IsAssignableFrom(null) -> False -- net48 asymmetry with
// IsSubclassOf, which throws ArgumentNullException; each primitive mirrors
// its own .NET API).
//------------------------------------------------------------------------------
int32_t il2cpp_type_is_assignable_from(Il2CppClass* target, Il2CppClass* source)
{
    if (target == nullptr)
    {
        ThrowNullReference();
    }
    if (source == nullptr)
    {
        return 0; // net48: IsAssignableFrom(null) returns False, no throw
    }
    return ClassIsAssignableFrom(target, source) ? 1 : 0;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_typecode -- Type.GetTypeCode(Type) primitive.
//
// Values are the net48 enum constants (empirically verified in
// typecheckcase): Int32=9, Boolean=3, Char=4, Double=14 (Single is 13 -- NOT
// 13 for Double), String=18, everything else falls back to Object=1 (M4
// scope; the full primitive/DateTime mapping is deferred). Mapping is
// IDENTICAL to icall_object_get_type_code -- keep both tables in sync.
//
// net48 null contract (probe-verified): Type.GetTypeCode(null) returns
// TypeCode.Empty (0) -- NOT a throw, NOT Object. A non-null descriptor with
// a malformed (null) name falls back to TypeCode.Object (1).
//------------------------------------------------------------------------------
int32_t il2cpp_type_get_typecode(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        return 0; // net48: Type.GetTypeCode(null) -> TypeCode.Empty (probe-verified)
    }
    const char* name = klass->name;
    if (name == nullptr)
    {
        // Malformed descriptor: safest fallback is TypeCode.Object.
        return 1;
    }
    if (std::strcmp(name, "System.Int32") == 0)
    {
        return 9;
    }
    if (std::strcmp(name, "System.Boolean") == 0)
    {
        return 3;
    }
    if (std::strcmp(name, "System.Char") == 0)
    {
        return 4;
    }
    if (std::strcmp(name, "System.Double") == 0)
    {
        return 14; // net48: TypeCode.Double == 14 (Single == 13)
    }
    if (std::strcmp(name, "System.String") == 0)
    {
        return 18;
    }
    // decimal-domain T3: net48 TypeCode.Decimal == 15 (mirrors the
    // typecheck.cpp / StackFold TypeCodeOfTypeName entries -- keep all three
    // in sync).
    if (std::strcmp(name, "System.Decimal") == 0)
    {
        return 15;
    }
    return 1; // TypeCode.Object -- M4 fallback for all other types
}

//------------------------------------------------------------------------------
// il2cpp_type_get_full_name -- Type.FullName primitive.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_type_get_full_name(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowArgumentNull();
    }
    const char* name = (klass->name != nullptr) ? klass->name : "System.Object";
    // Type names are ASCII by project convention (generated code is pure
    // ASCII), so the ASCII literal path of new_from_cs is always legal.
    return il2cpp_string_new_from_cs(name);
}

namespace {

// Base-chain name probe shared by the A-class predicates below: walks
// klass -> baseClass -> ... looking for 'target' (exact strcmp). A null
// klass answers false (callers throw NRE on null receivers first).
bool BaseChainContains(Il2CppClass* klass, const char* target)
{
    for (Il2CppClass* cur = klass; cur != nullptr; cur = cur->baseClass)
    {
        if (cur->name != nullptr && std::strcmp(cur->name, target) == 0)
        {
            return true;
        }
    }
    return false;
}

bool IsValueTypeChain(Il2CppClass* klass)
{
    return BaseChainContains(klass, "System.ValueType");
}

// Interfaces carry instanceSize 0 (TypeEmitter:679); array descriptors are
// excluded by the '['-prefix guard (arrays are classes, never interfaces).
bool IsInterfaceDescriptor(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        return false;
    }
    const char* name = klass->name;
    if (name != nullptr && name[0] == '[')
    {
        return false;
    }
    return klass->instanceSize == 0;
}

} // namespace

// A-class predicates: each reads EXISTING descriptor fields only.
// Null receiver -> NullReferenceException (instance-member contract).
int32_t il2cpp_type_is_array(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    const char* name = klass->name;
    return (name != nullptr && name[0] == '[') ? 1 : 0;
}

int32_t il2cpp_type_has_element_type(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    const char* name = klass->name;
    return (name != nullptr && name[0] == '[') ? 1 : 0;
}

int32_t il2cpp_type_is_enum(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    return BaseChainContains(klass, "System.Enum") ? 1 : 0;
}

int32_t il2cpp_type_is_value_type(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    return IsValueTypeChain(klass) ? 1 : 0;
}

int32_t il2cpp_type_is_interface(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    return IsInterfaceDescriptor(klass) ? 1 : 0;
}

int32_t il2cpp_type_is_class(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    if (IsValueTypeChain(klass) || IsInterfaceDescriptor(klass))
    {
        return 0;
    }
    return 1;
}

int32_t il2cpp_type_is_primitive(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    const char* name = klass->name;
    if (name == nullptr)
    {
        return 0;
    }
    static const char* const kPrimitives[] = {
        "System.Boolean", "System.Byte", "System.SByte", "System.Int16",
        "System.UInt16", "System.Int32", "System.UInt32", "System.Int64",
        "System.UInt64", "System.IntPtr", "System.UIntPtr", "System.Char",
        "System.Double", "System.Single",
    };
    for (size_t i = 0; i < sizeof(kPrimitives) / sizeof(kPrimitives[0]); ++i)
    {
        if (std::strcmp(name, kPrimitives[i]) == 0)
        {
            return 1;
        }
    }
    return 0;
}

Il2CppObject* il2cpp_type_get_base_type(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        ThrowNullReference();
    }
    Il2CppClass* base = klass->baseClass;
    if (base == nullptr || base->name == nullptr)
    {
        return nullptr; // net48: typeof(object).BaseType -> null
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(base->name));
}

} // namespace runtime
} // namespace il2cpp
