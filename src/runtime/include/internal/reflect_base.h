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
#include "il2cpp_array.h"  // Il2CppArray (the object[] args carrier)

namespace il2cpp {

// Activator.CreateInstance(string typeName) -> the new instance (object
// ref), or throws when the type cannot be created: null name ->
// ArgumentNullException; unregistered type (or a malformed name that
// escapes to "not found") -> MissingMethodException.
Il2CppObject* il2cpp_activator_create_instance(Il2CppObject* typeName);

// p-refl2-gapmatrix (C02): Activator.CreateInstance(Type, object[] args) --
// the params-array overload. Same registry resolution as
// il2cpp_activator_create_instance, but the ctor is selected by the ARG COUNT
// (the net48 overload runs the matching ctor with the supplied args). The
// args array is passed to the ctor through il2cpp_methodinfo_invoke (the
// instance-'this' injection). The nonPublic overload (CreateInstance(Type,
// bool)) ignores the flag (net48 default = public-only binding) and reuses
// il2cpp_activator_create_instance. Null typeName -> ArgumentNullException.
Il2CppObject* il2cpp_activator_create_instance_args(Il2CppObject* typeName, Il2CppArray* args);
// REFL-ARITY: Activator.CreateInstance(Type, object[] args, object[]
// activationAttributes) -- net48's 3-param form. A null/EMPTY
// activationAttributes behaves exactly like the 2-param form; a NON-EMPTY one
// throws NotSupportedException (net48, on a non-MBR type). Delegates to
// il2cpp_activator_create_instance_args after that check.
Il2CppObject* il2cpp_activator_create_instance_args_attrs(Il2CppObject* typeName, Il2CppArray* args,
                                                          Il2CppArray* activationAttributes);

// Type.GetType(string typeName) -> the type's full-name string (the INPUT
// object itself -- simplified Type model identity), or nullptr when the
// type is not registered or the name is malformed (no throw -- .NET GetType
// "not found -> null" contract). A NULL typeName throws
// ArgumentNullException (paramName "typeName" -- net48 Type.GetType(null),
// verified): the query's ONE throwing case, mirroring the public .NET API.
Il2CppObject* il2cpp_type_get_type(Il2CppObject* typeName);

// Type.GetType(string typeName, bool throwOnError[, bool ignoreCase]) -- the
// throwOnError / ignoreCase overloads. Same normalization + registry
// resolution as il2cpp_type_get_type; a well-formed but unregistered name
// throws TypeLoadException when throwOnError != 0 (else nullptr), and the
// registry key comparison folds ASCII case when ignoreCase != 0. A null
// typeName throws ArgumentNullException regardless of throwOnError.
Il2CppObject* il2cpp_type_get_type_ex(Il2CppObject* typeName, int32_t throwOnError,
                                      int32_t ignoreCase);

} // namespace il2cpp
