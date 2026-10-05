#pragma once
//------------------------------------------------------------------------------
// attribute.h -- System.Attribute runtime primitives (simplified AOT model).
//
// Design (AOT constraint): there is no metadata table to walk at runtime --
// attribute instances are compiled-time known. The transpiler therefore emits
// a static-initialization call per annotated type: il2cpp_attribute_register
// hangs a compile-time-built array of attribute OBJECTS on the type's full
// name. The runtime GetCustomAttributes flow (transpiler side) calls
// il2cpp_attribute_get_custom to look that array up. This is the
// reflection-mode-B/C bridge: the generated code owns the objects, the
// runtime owns the registry.
//
// Storage: a flat registry of (type name -> object array) entries, fixed
// capacity (kMaxAttributeTypes, see attribute.cpp). Lookup is a linear
// scan on the type name -- attribute counts are small in AOT programs and
// queries are cold reflection paths.
//
// Threading contract: registration happens during class static
// initialization (single-threaded startup, before any user code runs);
// queries happen after. The registry is read-only once user code starts,
// so no synchronization is needed -- writers and readers never overlap
// (documented; see D13 -- a lock would protect nothing).
//
// Semantics vs .NET (documented simplifications):
//   - The full net48 query surface is modelled: il2cpp_attribute_get_custom_ex
//     performs the inheritance walk (klass->baseClass chain, stopping before
//     System.Object), the attributeType filter (assignability), and the
//     AttributeUsage Inherited / AllowMultiple rules -- see the declaration
//     contract below. il2cpp_attribute_get_custom is the own-type-only slice
//     that the current transpiler bridge emits; il2cpp_attribute_is_defined /
//     il2cpp_attribute_get_single cover IsDefined / the singular overloads.
//   - Attribute instances are whatever the transpiler emitted (typically
//     newobj of the attribute class with ctor args applied) -- the runtime
//     does not construct or validate them.
//   - A type with no registered attributes is indistinguishable from an
//     unregistered type: the query returns count 0 / null array.
//   - net48's AmbiguousMatchException for the singular overload (more than one
//     match) is not raised here: il2cpp_attribute_get_single returns the first
//     match and reports the total count, leaving the exception to the caller.
//   - Only attributes on the class chain are modelled (type-level targets);
//     member-level (method/field/property/parameter) attributes are out of
//     scope -- the registry is keyed by type name only.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"   // Il2CppArray (managed-string ABI queries)
#include "il2cpp_object.h"
#include "il2cpp_metadata.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// il2cpp_attribute_register -- hang a compile-time-built attribute array on a
// type name (called by the transpiler's emitted static initializer).
//
//   typeName  : full type name of the annotated type, e.g. "Calc.Foo"
//               (must be a static string; the registry keeps the POINTER,
//               never copies -- generated code emits identical literals per
//               translation unit, so pointer equality is not relied on, the
//               string is only compared via strcmp)
//   attributes: the attribute objects, in declaration order (typically a
//               static array of Il2CppObject* emitted next to the type
//               descriptor); may be null when count == 0
//   count     : number of attributes (>= 0)
//
//   Returns 1 on success, 0 on rejection:
//     typeName == nullptr                  -> 0
//     attributes == nullptr && count > 0   -> 0
//     count < 0                            -> 0
//     registry full (no existing entry)    -> 0
//
//   Re-registering an existing type name REPLACES the stored array (the new
//   registration wins) and always succeeds -- even when the registry is
//   otherwise full. count == 0 registers nothing and returns 1 (a type
//   without attributes is an unregistered type by contract).
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_register(const char* typeName,
                                  Il2CppObject* const* attributes,
                                  int32_t count);

//------------------------------------------------------------------------------
// il2cpp_attribute_get_custom -- simplified GetCustomAttributes().
//
//   klass   : runtime type descriptor of the queried type (the transpiler
//             passes the address of the {Type}_class static descriptor)
//   outCount: receives the number of attributes found
//
//   Returns the stored array POINTER (the register-time array, in
//   declaration order) when the type is registered; nullptr otherwise.
//   *outCount is always written: 0 when unregistered or on null input,
//   the array length otherwise.
//
//   Input errors (klass == nullptr, klass->name == nullptr) behave like an
//   unregistered type: *outCount = 0 and nullptr returned -- the .NET
//   NullReferenceException semantics do not apply because GetCustomAttributes
//   is a reflection-static flow the transpiler calls with a descriptor, not
//   an instance method on a managed receiver.
//------------------------------------------------------------------------------
Il2CppObject** il2cpp_attribute_get_custom(Il2CppClass* klass,
                                           int32_t* outCount);

//------------------------------------------------------------------------------
// il2cpp_attribute_get_custom_named -- managed-string GetCustomAttributes
// icall (D-ATTR-1, 2026-08-11): the ABI the TRANSPILER emits. The simplified
// Type model (il2cpp_typeext.h assumption 1) represents a Type VALUE as its
// full name string, so the query element rides as a managed string instead of
// a raw descriptor -- exactly the typeinfo.cpp managed-string ABI pattern.
//
//   typeName -- the queried type's full name string (e.g. "Calc.Foo")
//
//   Returns an Il2CppArray* of the registered attribute objects, in
//   declaration order. net48 semantics: a type with NO attributes (registered
//   or not) yields an EMPTY array, never null -- so `result.Length` and
//   foreach work exactly like .NET. A null/malformed (non-ASCII / over-long)
//   typeName also yields the empty array (the query cannot name a type, so no
//   attributes can match). The array is a reference-element array (the
//   attributes are Il2CppObject*), allocated per query.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_attribute_get_custom_named(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// il2cpp_attribute_get_single_named -- managed-string GetCustomAttribute
// (singular) icall (D-ATTR-1): the first registered attribute object for the
// type or nullptr when none match. The transpiler drops the attributeType /
// inherit overload arguments (the registry answers only by the element's type
// name), so this is the own-type-only first-match slice of the custom_named
// result above. null/malformed typeName -> nullptr (no attributes can match).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_attribute_get_single_named(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// il2cpp_attribute_register_usage -- AttributeUsage (net48 AttributeUsage-
// Attribute) for an attribute class, registered by the transpiler next to the
// type descriptor (mirrors the [AttributeUsage] declaration on the class).
//
//   attributeTypeName: full type name of the ATTRIBUTE class, e.g. "T.Foo"
//                      (the same literal the class descriptor carries; the
//                      registry keeps the POINTER, never copies)
//   allowMultiple     : 1 -> the attribute may be applied more than once to
//                      the same target (net48 AllowMultiple)
//   inherited         : 1 -> a base-class instance is inherited by derived
//                      classes (net48 Inherited)
//
//   Returns 1 on success, 0 on rejection (null name / registry full -- loud,
//   the transpiler must raise capacity like the attribute registry).
//   Re-registering an existing attribute type REPLACES the stored flags and
//   always succeeds. An attribute class WITHOUT a registered usage uses the
//   net48 defaults: AllowMultiple = false, Inherited = true.
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_register_usage(const char* attributeTypeName,
                                        int32_t allowMultiple,
                                        int32_t inherited);

//------------------------------------------------------------------------------
// il2cpp_attribute_get_custom_ex -- net48-semantics GetCustomAttributes
// (inheritance walk + attributeType filter). il2cpp_attribute_get_custom
// above is the no-filter, own-type-only slice; this is the full query.
//
//   klass           : queried type descriptor
//   attributeType   : filter -- only attributes whose runtime class equals
//                     attributeType or derives from it are returned (net48
//                     attributeType.IsAssignableFrom); nullptr = no filter
//   inherit         : 1 -> walk klass->baseClass chain (net48 inherit = true);
//                     0 -> only attributes declared on klass itself
//   results         : caller buffer receiving the matched attribute objects,
//                     most-derived type first (declaration order within a
//                     level); may be nullptr for a count-only query
//   resultsCapacity : capacity of results (ignored when results == nullptr)
//
//   Returns the TOTAL number of matches (>= the number written to results
//   when the buffer is too small; never writes past resultsCapacity).
//
//   Inheritance semantics (net48): a base-class attribute is included only
//   when its AttributeUsage.Inherited is true; when AllowMultiple is false an
//   attribute is shadowed by an earlier (more-derived) attribute of the SAME
//   exact type. The walk processes the queried type first (its own attributes
//   are returned regardless of usage) and stops before System.Object.
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_get_custom_ex(Il2CppClass* klass,
                                       Il2CppClass* attributeType,
                                       int32_t inherit,
                                       Il2CppObject** results,
                                       int32_t resultsCapacity);

//------------------------------------------------------------------------------
// il2cpp_attribute_is_defined -- net48-semantics IsDefined. Returns 1 when the
// equivalent GetCustomAttributes query (same klass / attributeType / inherit)
// would return at least one match, 0 otherwise. attributeType == nullptr tests
// "any attribute is defined".
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_is_defined(Il2CppClass* klass,
                                    Il2CppClass* attributeType,
                                    int32_t inherit);

//------------------------------------------------------------------------------
// il2cpp_attribute_get_single -- net48-semantics GetCustomAttribute (singular):
// the FIRST matching attribute (most-derived first) or nullptr when none
// matches. *outCount receives the total number of matches: net48 throws
// AmbiguousMatchException when it is > 1 -- the caller decides whether to
// raise (the runtime has no managed exception surface for it yet).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_attribute_get_single(Il2CppClass* klass,
                                          Il2CppClass* attributeType,
                                          int32_t inherit,
                                          int32_t* outCount);

} // namespace runtime
} // namespace il2cpp
