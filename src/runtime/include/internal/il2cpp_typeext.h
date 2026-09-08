#pragma once
//------------------------------------------------------------------------------
// il2cpp_typeext.h -- System.Type primitives (GetTypeFromHandle/
// GetElementType/IsAssignableFrom/GetTypeCode/FullName).
//
// Sibling of il2cpp_typecheck.h: typecheck owns the INSTANCE-side primitives
// (isinst/castclass/TypeCode-of-an-object/GetType-of-an-object), this header
// owns the CLASS-descriptor-side members the transpiler emits for Type's
// surface. Every function takes a RUNTIME descriptor (Il2CppClass*) -- the
// transpiler passes the address of the static {Type}_class descriptor emitted
// per translated type.
//
// Assumptions (SIMPLIFIED Type model -- there is no managed System.Type
// object in this runtime; the transpiler has no typeof/GetTypeFromHandle
// emission yet, these primitives define the forward ABI):
//   1. "A Type" is represented by the full type NAME as a managed string
//      (Il2CppString*) -- the same representation icall_object_get_type
//      uses for GetType().ToString(). GetTypeFromHandle / GetElementType
//      return that string; a future managed Type object replaces this without
//      ABI change (the return slot stays a GC reference).
//   2. IsAssignableFrom uses the typecheck.cpp conformance model (identity,
//      '['-prefix array rule, baseClass chain, interface table scan) with the
//      same M4 simplifications -- see the notes below.
//   3. TypeCode maps EXACTLY like icall_object_get_type_code (net48
//      empirical values); keep the two mapping tables in sync.
//
// Exception contract (net48 probe-verified against v4.0.30319; each primitive
// mirrors its own .NET null semantics -- note net48's asymmetry):
//   * GetTypeFromHandle -- a null descriptor (the default RuntimeTypeHandle)
//     returns nullptr with NO throw: Type.GetTypeFromHandle(default
//     RuntimeTypeHandle) -> null, probe-verified. A null descriptor is NOT a
//     malformed-input error for this STATIC member.
//   * GetElementType -- a null receiver (a null Type) throws
//     NullReferenceException (((Type)null).GetElementType() throws NRE); a
//     NON-array type returns nullptr with NO throw
//     (typeof(int).GetElementType() -> null).
//   * IsAssignableFrom -- a null RECEIVER (target) throws
//     NullReferenceException (((Type)null).IsAssignableFrom(x) throws NRE); a
//     null ARGUMENT (source) returns false with NO throw
//     (typeof(int).IsAssignableFrom(null) -> False).
//   * GetTypeCode -- a null Type returns TypeCode.Empty (0)
//     (Type.GetTypeCode(null) -> Empty, NOT Object and NOT a throw); a
//     non-null descriptor with a malformed name falls back to Object (1).
//   * FullName -- a null descriptor is a malformed input (throws
//     ArgumentNullException via the standard carrier, chain suffix
//     ",System.Exception"); the other instance-member primitives above carry
//     their own net48 null contract as documented.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h"
#include "il2cpp_string.h" // Il2CppString (managed-string return type)

#include <cstdint>

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// il2cpp_type_get_type_from_handle -- Type.GetTypeFromHandle simplified.
//
//   klass: the runtime type descriptor (the transpiler's handle value)
//
//   Returns the type NAME as a managed string (Il2CppString* cast to
//   Il2CppObject*; the caller treats it as a GC reference) -- the
//   simplified Type representation (assumption 1 above; identical to
//   icall_object_get_type's output shape).
//   klass == nullptr -> nullptr with NO throw (net48 probe-verified:
//   Type.GetTypeFromHandle(default(RuntimeTypeHandle)) -> null).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_type_from_handle(Il2CppClass* klass);

//------------------------------------------------------------------------------
// il2cpp_type_get_element_type -- Type.GetElementType() simplified.
//
//   klass: the runtime type descriptor (the receiver Type)
//
//   Returns the ELEMENT type NAME as a managed string (Il2CppObject* -- the
//   simplified Type representation, same shape as get_type_from_handle) when
//   'klass' is an array type, or nullptr when it is NOT an array type.
//   Array detection is the '['-prefix rule (the array-class marker, same rule
//   as IsAssignableFrom / the GC array scan): an array descriptor's name is
//   "[<element type name>" (array.cpp convention), so the element type name
//   is the name with the leading '[' stripped ("[System.Int32" -> "System.Int32").
//   net48 probe-verified: typeof(int[]).GetElementType() == typeof(int),
//   typeof(string[]).GetElementType() == typeof(string),
//   typeof(int[,]).GetElementType() == typeof(int) (rank is NOT part of the
//   element identity), typeof(int).GetElementType() == null (non-array), and
//   typeof(int[][]).GetElementType() == typeof(int[]) (the element of a jagged
//   array is itself an array -- the '['-stripped name is again an array class).
//   klass == nullptr (null receiver) -> NullReferenceException (net48:
//   ((Type)null).GetElementType() throws NRE).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_type_get_element_type(Il2CppClass* klass);

//------------------------------------------------------------------------------
// il2cpp_type_is_assignable_from -- Type.IsAssignableFrom(c) primitive.
//
//   target, source: type descriptors
//
//   Returns 1 when a value of 'source' can be assigned to 'target', i.e.
//   the typecheck.cpp conformance model applied to class descriptors:
//     target == source                                  -> 1 (identity)
//     both are arrays ('['-prefix names)                -> 1 (M4 prefix-only
//                                                          rule; element type
//                                                          NOT compared)
//     source's baseClass chain contains target          -> 1
//     target is an interface implemented by source or
//     any of source's bases (interface table scan)      -> 1
//   Otherwise 0. Note the DIRECTION: IsAssignableFrom(typeof(Base),
//   typeof(Derived)) is 1, the reverse is 0. Unlike IsSubclassOf, identity
//   IS assignable (a type is assignable to itself).
//
//   net48 null contract (probe-verified): target == nullptr (null receiver)
//   throws NullReferenceException; source == nullptr returns 0 (false, no
//   throw) -- see the header exception contract.
//------------------------------------------------------------------------------
int32_t il2cpp_type_is_assignable_from(Il2CppClass* target, Il2CppClass* source);

//------------------------------------------------------------------------------
// il2cpp_type_get_typecode -- Type.GetTypeCode(Type) primitive.
//
//   klass: the runtime type descriptor
//
//   Returns the net48 TypeCode value, mapping IDENTICAL to
//   icall_object_get_type_code (keep the two tables in sync):
//     System.Int32 -> 9, System.Boolean -> 3, System.Char -> 4,
//     System.Double -> 14 (net48: Single == 13), System.String -> 18,
//     anything else -> 1 (TypeCode.Object -- M4 fallback; the full
//     SByte/Byte/Int16/UInt16/UInt32/Int64/UInt64/Single/Decimal/DateTime
//     mapping is deferred).
//   net48 null contract (probe-verified): Type.GetTypeCode(null) returns
//   TypeCode.Empty (0); a non-null descriptor with a malformed name falls
//   back to Object (1).
//------------------------------------------------------------------------------
int32_t il2cpp_type_get_typecode(Il2CppClass* klass);

//------------------------------------------------------------------------------
// il2cpp_type_get_full_name -- Type.FullName primitive.
//
//   klass: the runtime type descriptor
//
//   Returns klass->name as a managed string (Il2CppString*). Type names are
//   ASCII by project convention, so the ASCII literal path of
//   new_from_cs is always legal.
//   klass == nullptr -> ArgumentNullException.
//   klass->name == nullptr (malformed descriptor) -> "System.Object"
//   fallback, mirroring icall_object_get_type.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_type_get_full_name(Il2CppClass* klass);

//------------------------------------------------------------------------------
// Type predicate primitives (A-class): each answers one System.Type boolean
// property from EXISTING descriptor fields only (name / baseClass chain /
// instanceSize) -- no new metadata required. klass == nullptr (null receiver)
// throws NullReferenceException (instance-member contract, mirrors
// GetElementType). All return int32_t 1/0.
//------------------------------------------------------------------------------
int32_t il2cpp_type_is_array(Il2CppClass* klass);
int32_t il2cpp_type_is_enum(Il2CppClass* klass);
int32_t il2cpp_type_is_value_type(Il2CppClass* klass);
int32_t il2cpp_type_is_interface(Il2CppClass* klass);
int32_t il2cpp_type_is_class(Il2CppClass* klass);
int32_t il2cpp_type_is_primitive(Il2CppClass* klass);
int32_t il2cpp_type_has_element_type(Il2CppClass* klass);
Il2CppObject* il2cpp_type_get_base_type(Il2CppClass* klass);

} // namespace runtime
} // namespace il2cpp
