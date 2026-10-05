#pragma once
//------------------------------------------------------------------------------
// reflect_base.h -- reflection base pair: Activator.CreateInstance(string) +
// Type.GetType(string) (reflection-status-v2 tickets R-REFL-07/08).
//
// Both icalls are the TRANSPILER-wired surface (MethodMap.Reflect.cs):
//   * il2cpp_activator_create_instance -- Activator.CreateInstance(string):
//     the type NAME string is resolved through the typeinfo registry
//     (typeinfo.h -- the same source of truth the Type member queries use)
//     and the corresponding class descriptor (Il2CppTypeInfo::klass) is
//     instantiated via il2cpp_object_new (zero-init + klass-stamp, the
//     standard generated-code allocation path). NOT REGISTERED -> the type
//     has no descriptor to instantiate -> MissingMethodException (the .NET
//     "type cannot be created" failure family; a plain new cannot be
//     emitted because the descriptor does not exist in this build).
//   * il2cpp_type_get_type -- Type.GetType(string): the same registry
//     lookup, but QUERY-ONLY. On a hit the input name string is returned
//     AS-IS (identity, no allocation) -- the simplified Type model
//     (il2cpp_typeext.h): a Type VALUE is its full name string. On a miss
//     (malformed name or unregistered type) the result is nullptr, matching
//     .NET Type.GetType's "not found -> null" contract (no throw).
//
// Exception contract: create_instance throws (null name ->
// ArgumentNullException; unregistered type -> MissingMethodException).
// get_type throws ONLY for a null name (ArgumentNullException -- net48
// Type.GetType(null) verified against v4.0.30319, paramName "typeName");
// every other miss returns nullptr and never throws (the .NET GetType
// not-found contract). Name inputs are managed strings (Il2CppObject* =
// Il2CppString* -- the managed-string ABI family of typeinfo.h's queries).
//------------------------------------------------------------------------------

#include "il2cpp_object.h" // Il2CppObject (managed-string ABI)

namespace il2cpp {

// Activator.CreateInstance(string typeName) -> the new instance (object
// ref), or throws when the type cannot be created: null name ->
// ArgumentNullException; unregistered type (or a malformed name that
// escapes to "not found") -> MissingMethodException.
Il2CppObject* il2cpp_activator_create_instance(Il2CppObject* typeName);

// Type.GetType(string typeName) -> the type's full-name string (the INPUT
// object itself -- simplified Type model identity), or nullptr when the
// type is not registered or the name is malformed (no throw -- .NET GetType
// "not found -> null" contract). A NULL typeName throws
// ArgumentNullException (paramName "typeName" -- net48 Type.GetType(null),
// verified): the query's ONE throwing case, mirroring the public .NET API.
Il2CppObject* il2cpp_type_get_type(Il2CppObject* typeName);

} // namespace il2cpp
