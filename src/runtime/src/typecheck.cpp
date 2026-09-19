//------------------------------------------------------------------------------
// typecheck.cpp -- runtime type-check icalls (see il2cpp_typecheck.h).
//
// Conformance model (M4 scope):
//   1. Identity:     obj->klass == klass.
//   2. Base chain:   baseClass-pointer walk (derived -> base -> ...).
//   3. Arrays:       M4 SIMPLIFICATION -- when both the runtime class and the
//                    target class are array classes (name[0] == '[' -- the
//                    array-class marker, see array.cpp), the check succeeds
//                    on the '[' prefix alone; the ELEMENT type is not
//                    compared (weaker than .NET, documented in the header).
//   4. Interfaces:   the interfaces table of the runtime class and of every
//                    base class (a derived type inherits its base's
//                    interfaces -- matches boxing.cpp's scan shape).
//
// The check deliberately duplicates boxing.cpp's il2cpp_is_instance instead
// of delegating: the icall adds the array rule (boxing.cpp is read-only) and
// owns the int32 0/1 ABI (il2cpp_is_instance returns bool). Keep the two in
// sync when the conformance model evolves.
//
// Exception contract: icall_object_get_type / get_type_code on a null object
// throw NullReferenceException -- .NET instance-method semantics (a null
// receiver can never legally pass through GetType()/GetTypeCode()). The throw
// uses the standard carrier (il2cpp_throw_new, chain suffix
// ",System.Exception") so generated try/catch and the top-level guard handle
// it like any other managed exception.
//------------------------------------------------------------------------------

#include "il2cpp_typecheck.h"

#include "il2cpp_exception.h" // il2cpp_throw_new
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs

#include <cstring>
#include <string>

namespace il2cpp {
namespace runtime {

namespace {

// The core conformance test shared by is_instance and cast (see the file
// header for the rule order: identity -> array prefix -> base chain ->
// interface table scan).
bool TypeIsInstance(Il2CppObject* obj, Il2CppClass* klass)
{
    if (obj == nullptr || klass == nullptr)
    {
        return false;
    }
    Il2CppClass* runtime = obj->klass;
    if (runtime == nullptr)
    {
        return false;
    }
    // 1. Identity.
    if (runtime == klass)
    {
        return true;
    }
    // 2. Array conformance (M4 simplification): both classes are arrays
    //    ('[' name prefix) -> match on the prefix alone.
    if (runtime->name != nullptr && klass->name != nullptr &&
        runtime->name[0] == '[' && klass->name[0] == '[')
    {
        return true;
    }
    // 3. Base chain walk (starts at the runtime class; identity already
    //    handled above).
    for (Il2CppClass* cur = runtime; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == klass)
        {
            return true;
        }
    }
    // 4. Interface conformance: scan the interfaces table of the runtime
    //    class AND its base chain (interfaces are inherited).
    for (Il2CppClass* scan = runtime; scan != nullptr; scan = scan->baseClass)
    {
        if (scan->interfaces == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < scan->interfaceCount; ++i)
        {
            if (scan->interfaces[i].iface == klass)
            {
                return true;
            }
        }
    }
    return false;
}

// Null-receiver failure: .NET throws NullReferenceException. The chain
// suffix (",System.Exception") is the transpiler's chain format -- it lets
// il2cpp_exception_matches accept a catch of System.Exception.
[[noreturn]] void ThrowNullReference()
{
    il2cpp_throw_new("System.NullReferenceException,System.Exception",
                     "Object reference not set to an instance of an object.");
}

// Null-argument failure: .NET throws ArgumentNullException. Same carrier
// convention as ThrowNullReference.
[[noreturn]] void ThrowArgumentNull()
{
    il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                     "Value cannot be null.");
}

} // namespace

//------------------------------------------------------------------------------
// icall_type_is_instance -- isinst conformance test (see header).
//------------------------------------------------------------------------------
int32_t icall_type_is_instance(Il2CppObject* obj, Il2CppClass* klass)
{
    return TypeIsInstance(obj, klass) ? 1 : 0;
}

//------------------------------------------------------------------------------
// icall_type_cast -- castclass conformance test (see header).
//
// Contract: match -> obj (the SAME pointer), mismatch -> nullptr, null input
// -> nullptr. The .NET InvalidCastException on a failed castclass is NOT
// thrown here -- the transpiler side (H4 audit) owns that decision; this
// primitive reports the outcome.
//------------------------------------------------------------------------------
Il2CppObject* icall_type_cast(Il2CppObject* obj, Il2CppClass* klass)
{
    return TypeIsInstance(obj, klass) ? obj : nullptr;
}

//------------------------------------------------------------------------------
// icall_type_equals -- type identity (see header).
//------------------------------------------------------------------------------
int32_t icall_type_equals(Il2CppClass* a, Il2CppClass* b)
{
    // Identity fast path (also covers both-null).
    if (a == b)
    {
        return 1;
    }
    if (a == nullptr || b == nullptr)
    {
        return 0;
    }
    if (a->name == nullptr || b->name == nullptr)
    {
        return 0;
    }
    // Name equality: the transpiler emits identical name literals for the
    // same type in every translation unit, so name equality == type
    // equality (contents compare; the registry dedupes descriptors).
    return std::strcmp(a->name, b->name) == 0 ? 1 : 0;
}

//------------------------------------------------------------------------------
// icall_type_is_subclass_of -- Type.IsSubclassOf (see header).
//------------------------------------------------------------------------------
int32_t icall_type_is_subclass_of(Il2CppClass* derived, Il2CppClass* base)
{
    if (derived == nullptr)
    {
        // null receiver: .NET ((Type)null).IsSubclassOf(x) throws
        // NullReferenceException (net48 probe-verified).
        ThrowNullReference();
    }
    if (base == nullptr)
    {
        // null argument: .NET IsSubclassOf(null) throws ArgumentNullException
        // with ParamName "type" (net48 probe-verified).
        ThrowArgumentNull();
    }
    // .NET IsSubclassOf excludes self: the walk starts at derived->baseClass,
    // never at derived itself.
    for (Il2CppClass* cur = derived->baseClass; cur != nullptr; cur = cur->baseClass)
    {
        if (cur == base)
        {
            return 1;
        }
    }
    return 0;
}

//------------------------------------------------------------------------------
// icall_object_get_type_code -- TypeCode of the runtime type (see header).
//
// Values are the net48 enum constants, empirically verified against .NET
// Framework 4.8 (tests/cases/typecheckcase + a csc probe on the full 12-box
// sweep): Boolean=3, Char=4, SByte=5, Byte=6, Int16=7, UInt16=8, Int32=9,
// UInt32=10, Int64=11, UInt64=12, Single=13, Double=14, Decimal=15,
// String=18; everything else falls back to Object=1 (the DateTime/DBNull
// box classes have no runtime box descriptor -- M4 scope).
//------------------------------------------------------------------------------
int32_t icall_object_get_type_code(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowNullReference();
    }
    const char* name = (obj->klass != nullptr) ? obj->klass->name : nullptr;
    if (name == nullptr)
    {
        // Malformed descriptor: safest fallback is TypeCode.Object.
        return 1;
    }
    // Full 12-type box mapping (net48 empirical; csc probe verified the exact
    // value for every box class -- Int64=11, UInt32=10, UInt64=12, Byte=6,
    // SByte=5, Int16=7, UInt16=8, Single=13 -- all previously falling through
    // to Object=1). Matching on the klass NAME (the box classes register their
    // .NET full names; see boxingext2.cpp / metadata.cpp builtins).
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
    if (std::strcmp(name, "System.SByte") == 0)
    {
        return 5;
    }
    if (std::strcmp(name, "System.Byte") == 0)
    {
        return 6;
    }
    if (std::strcmp(name, "System.Int16") == 0)
    {
        return 7;
    }
    if (std::strcmp(name, "System.UInt16") == 0)
    {
        return 8;
    }
    if (std::strcmp(name, "System.UInt32") == 0)
    {
        return 10;
    }
    if (std::strcmp(name, "System.Int64") == 0)
    {
        return 11;
    }
    if (std::strcmp(name, "System.UInt64") == 0)
    {
        return 12;
    }
    if (std::strcmp(name, "System.Single") == 0)
    {
        return 13; // net48: TypeCode.Single == 13 (Double == 14)
    }
    if (std::strcmp(name, "System.Double") == 0)
    {
        return 14; // net48: TypeCode.Double == 14 (Single == 13)
    }
    // decimal-domain T3: net48 TypeCode.Decimal == 15 (mirrors the
    // typeext.cpp / StackFold TypeCodeOfTypeName entries -- keep all three
    // in sync).
    if (std::strcmp(name, "System.Decimal") == 0)
    {
        return 15;
    }
    if (std::strcmp(name, "System.String") == 0)
    {
        return 18;
    }
    return 1; // TypeCode.Object -- M4 fallback for all other types
              // (incl. DateTime=16 / DBNull=2 -- no runtime box descriptor)
}

//------------------------------------------------------------------------------
// icall_object_get_type -- simplified Type.ToString() (see header).
//------------------------------------------------------------------------------
Il2CppObject* icall_object_get_type(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        ThrowNullReference();
    }
    const char* name = (obj->klass != nullptr && obj->klass->name != nullptr)
        ? obj->klass->name
        : "System.Object"; // malformed descriptor fallback
    // Array classes carry the "[<Element>" marker name (see array.cpp); .NET
    // GetType().ToString()/Name render it as "<Element>[]" (net48:
    // `new int[3].GetType().Name == "Int32[]"`). Translate the leading '['
    // into a trailing "[]" -- the string the callers then short-name.
    if (name[0] == '[' && name[1] != '\0')
    {
        std::string arrName = std::string(name + 1) + "[]";
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(arrName.c_str()));
    }
    // Type names are ASCII by project convention (generated code is pure
    // ASCII), so the ASCII literal path of new_from_cs is always legal.
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name));
}

//------------------------------------------------------------------------------
// il2cpp_type_get_short_name -- Type.Name on a Type VALUE (see header).
//
// The Type value is the FULL name string; .NET Type.Name is the last
// identifier segment -- after the last '.' (namespace) AND after the last
// '+' (nested-type separator): "System.Int32" -> "Int32",
// "Cases.Exops2+CxEx2" -> "CxEx2" (net48 nested-type probe; the old
// dot-only split returned "Exops2+CxEx2" -- the exops2 Type.Name diff).
// A name with neither separator (no namespace) is already the short name --
// return the input unchanged (identity, no allocation). The truncation rule
// mirrors the transpiler's ShortTypeName fold (typeof(X).Name) -- both paths
// agree.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_short_name(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        ThrowNullReference();
    }
    auto s = reinterpret_cast<Il2CppString*>(typeName);
    int32_t cut = -1;
    for (int32_t i = 0; i < s->length; i++)
    {
        if (s->chars[i] == u'.' || s->chars[i] == u'+')
        {
            cut = i;
        }
    }
    if (cut < 0)
    {
        return typeName; // no namespace: the name IS the short name
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(s->chars + cut + 1, s->length - cut - 1));
}

} // namespace runtime
} // namespace il2cpp
