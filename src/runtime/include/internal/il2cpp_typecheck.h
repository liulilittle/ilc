#pragma once
//------------------------------------------------------------------------------
// il2cpp_typecheck.h -- runtime type-check icalls (isinst/castclass/subclass/
// TypeCode/GetType primitives).
//
// These are the runtime side of the transpiler's isinst/castclass/GetTypeCode
// /GetType emission (the CodeGenerator side is a separate domain; H4 audit
// wires these calls in). Every function takes RUNTIME descriptors
// (Il2CppClass*) -- the transpiler passes the address of the static
// {Type}_class descriptor emitted per translated type.
//
// Conformance model (M4 scope):
//   Class check    -- identity (obj->klass == klass) or a baseClass-pointer
//                     chain walk (derived -> base -> ... -> System.Object).
//   Array check    -- M4 SIMPLIFICATION: when BOTH the object's runtime class
//                     and the target class are arrays (name[0] == '[' -- the
//                     array-class marker used by array.cpp), the check returns
//                     true on the name-prefix match alone; the ELEMENT type is
//                     NOT compared. This is weaker than .NET (an int[] is NOT
//                     an instance of a double[] class, and element covariance
//                     is not modelled) and is documented as a simplification.
//   Interface check-- scan the interfaces table of the runtime class AND its
//                     base chain for the entry whose iface == klass.
//
// Return-convention notes:
//   All 0/1 functions return int32_t (1 = true, 0 = false) -- the generated
//   code ABI for bool is int32 0/1.
//   icall_type_cast follows the "return obj or null" contract: a failing
//   castclass yields null, NOT an InvalidCastException. Full .NET semantics
//   throw InvalidCastException on mismatch; the transpiler side (H4 audit)
//   owns that decision -- this primitive reports the outcome and lets the
//   caller decide.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// icall_type_is_instance -- .NET isinst conformance test.
//
//   obj  : the managed object (may be null -- isinst(null) is false)
//   klass: the target type descriptor (class, array class, or interface)
//
//   Returns 1 when obj's runtime type conforms to klass:
//     obj == nullptr || klass == nullptr               -> 0
//     obj->klass == klass                              -> 1
//     both runtime class and klass are arrays ('['-prefix names) -> 1
//     baseClass chain walk hits klass                  -> 1
//     klass is an interface the runtime class (or any
//     base-class) implements (interfaces table scan)   -> 1
//   Otherwise 0.
//------------------------------------------------------------------------------
int32_t icall_type_is_instance(Il2CppObject* obj, Il2CppClass* klass);

//------------------------------------------------------------------------------
// icall_type_cast -- .NET castclass conformance test.
//
//   obj  : the managed object (may be null -- castclass(null) is null)
//   klass: the target type descriptor
//
//   Returns obj when it conforms (see icall_type_is_instance), nullptr
//   otherwise. Per the M4 contract a mismatch returns null -- it does NOT
//   throw InvalidCastException here (the transpiler side decides; see header
//   note). Null input returns null.
//------------------------------------------------------------------------------
Il2CppObject* icall_type_cast(Il2CppObject* obj, Il2CppClass* klass);

//------------------------------------------------------------------------------
// icall_type_equals -- type identity comparison (Type == Type / typeof ==).
//
//   a, b: type descriptors (may be null)
//
//   Returns 1 when the two descriptors denote the same type. Identity
//   (a == b, including both-null) is the fast path; otherwise the full type
//   NAMES are compared (the transpiler emits identical name literals for the
//   same type in every translation unit, so name equality == type equality).
//------------------------------------------------------------------------------
int32_t icall_type_equals(Il2CppClass* a, Il2CppClass* b);

//------------------------------------------------------------------------------
// icall_type_is_subclass_of -- Type.IsSubclassOf primitive.
//
//   derived, base: type descriptors
//
//   Returns 1 when derived's baseClass chain contains base. .NET semantics:
//   a type is NOT a subclass of itself (the walk starts at derived->baseClass,
//   never at derived) -- IsSubclassOf(typeof(T)) for T is false.
//
//   Null contract (net48 probe-verified against v4.0.30319):
//     derived == nullptr -> NullReferenceException (a null Type receiver --
//       ((Type)null).IsSubclassOf(x) throws NRE)
//     base     == nullptr -> ArgumentNullException (ParamName "type" --
//       typeof(int).IsSubclassOf(null) throws ANE)
//   NOTE the asymmetry with IsAssignableFrom (il2cpp_typeext.h): net48
//   IsAssignableFrom(null) RETURNS false (no throw) while IsSubclassOf(null)
//   THROWS ArgumentNullException -- each primitive mirrors its own .NET API.
//------------------------------------------------------------------------------
int32_t icall_type_is_subclass_of(Il2CppClass* derived, Il2CppClass* base);

//------------------------------------------------------------------------------
// icall_object_get_type_code -- TypeCode of an object's runtime type.
//
//   obj: the managed object (null -> NullReferenceException, matching .NET's
//        null instance-method semantics)
//
//   Returns the net48 TypeCode value (empirically verified against .NET
//   Framework 4.8 -- see the typecheckcase reference program + the csc probe
//   that swept every box class):
//     System.Boolean  -> 3
//     System.Char     -> 4
//     System.SByte    -> 5
//     System.Byte     -> 6
//     System.Int16    -> 7
//     System.UInt16   -> 8
//     System.Int32    -> 9
//     System.UInt32   -> 10
//     System.Int64    -> 11
//     System.UInt64   -> 12
//     System.Single   -> 13
//     System.Double   -> 14   (net48: Single == 13, Double == 14)
//     System.Decimal  -> 15
//     System.String   -> 18
//     anything else   -> 1    (TypeCode.Object -- M4 fallback; DateTime=16 /
//                              DBNull=2 have no runtime box descriptor)
//------------------------------------------------------------------------------
int32_t icall_object_get_type_code(Il2CppObject* obj);

//------------------------------------------------------------------------------
// icall_object_get_type -- simplified Type.ToString() for an object.
//
//   obj: the managed object (null -> NullReferenceException, matching .NET's
//        null.GetType() semantics)
//
//   Returns the object's runtime type NAME (klass->name, e.g. "System.Int32"
//   for a boxed int) as a MANAGED STRING (Il2CppString* cast to Il2CppObject*;
//   the caller treats it as a GC reference). .NET's full Type object /
//   GetType().Name flow is reduced to the name string -- the primitive the
//   transpiler needs for GetType().ToString()/Name emission.
//------------------------------------------------------------------------------
Il2CppObject* icall_object_get_type(Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_type_get_short_name -- Type.Name on a Type VALUE (name string).
//
//   typeName: the full type NAME string (the Type-value shape -- the same
//             string icall_object_get_type / il2cpp_type_get_type_from_handle
//             produce; null -> NullReferenceException, matching .NET's null
//             instance-method semantics)
//
//   Returns the SHORT name -- the segment after the last '.'
//   ("System.Int32" -> "Int32"); a name without '.' (no namespace) is
//   returned unchanged (the identity result, no allocation). net48 semantics
//   (probe-verified): GetType().Name is the SHORT name while
//   GetType().ToString() is the FULL name -- the transpiler's get_Name
//   mapping routes through this primitive instead of the identity (the
//   full-name string cannot serve Type.Name directly).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_short_name(Il2CppObject* typeName);

} // namespace runtime
} // namespace il2cpp
