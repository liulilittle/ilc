#pragma once
//------------------------------------------------------------------------------
// typeinfo.h -- M5.5 reflection metadata tables (Il2CppTypeInfo and friends).
//
// One Il2CppTypeInfo is emitted per REFLECTED type (mode A/B/C decide the set)
// by the transpiler and registered alongside the type descriptor (see
// docs/architecture/reflection-runtime-design.md section 2). The tables are
// the runtime's source of truth for reflection queries; direct-call code
// NEVER goes through them (performance).
//
// Layout contract (design section 2.1, landed as drafted):
//   * Il2CppClass is UNCHANGED (add-only evolution). Il2CppTypeInfo is a
//     sibling structure associated through the registry; the type NAME is the
//     natural join key (full names are unique).
//   * No duplication: instance layout / GC bitmap / baseClass / interfaces
//     already live on Il2CppClass. TypeInfo only stores what reflection
//     queries need beyond the descriptor: method table, field name+offset
//     table, property table, signatures.
//   * Single authority: property getters/setters store only ACCESSOR NAMES,
//     resolved through the SAME methods table -- no second set of function
//     pointers on the property table.
//   * Read-only segment: all tables are static const, generated at compile
//     time (metadata is statically frozen).
//   * GC visibility: TypeInfo is bare pointers + constants (no managed
//     reference fields); the GC does not need to see it. Reflected
//     MethodInfo/PropertyInfo/FieldInfo wrapper objects are constructed at
//     runtime from these tables.
//
// Exception contract (net48-aligned, probe-verified against v4.0.30319):
//   * A null TYPE receiver (null info / null typeName) throws
//     NullReferenceException -- ((Type)null).GetMethod(x) throws NRE in net48.
//   * A null member NAME throws ArgumentNullException -- GetMethod(null) /
//     GetProperty(null) throw with ParamName "name"; GetField(null) throws
//     with an EMPTY ParamName (net48 probe result). The primitive and the
//     managed-string ABI enforce the same contract.
//   * A non-null name that is simply NOT FOUND (own table + base chain) or a
//     malformed/over-long/non-ASCII name returns nullptr -- "not found -> null"
//     never throws (net48: GetMethod("")/GetMethod(300-char) -> null).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_metadata.h" // Il2CppClass
#include "il2cpp_object.h"   // Il2CppObject (managed-string ABI queries)

namespace il2cpp {

// One reflected method. signature is the MethodSignatureCoder encoding
// ("i" / "V:System.String" / "r:Calc.Point" ...) so the runtime and the
// transpiler share one encoding.
struct Il2CppMethodInfo
{
    const char* name;          // method name, e.g. "Add"
    const char* signature;     // signature encoding (return + params)
    int32_t     paramCount;    // number of parameters (from signature)
    int32_t     flags;         // bit0 public, bit1 static, bit2 ctor, bit3 virtual
    int32_t     virtualSlot;   // vtable slot if virtual, else -1
    const char* declaringType; // declaring type full name (inherited methods
                               // are listed under THEIR declaring type's table)
    void*       fnPtr;         // AOT static function address (or invoker wrapper).
                               // null = runtime-unreachable method (trimmed).
};

// One reflected field. Instance fields: offset + isRef (same convention as
// Il2CppClass.refFieldBitmap). Static fields: offset = -1, staticAddr points
// at the generated static storage slot.
struct Il2CppFieldInfo
{
    const char* name;          // field name
    const char* typeName;      // field type full name (for box/unbox checks)
    int32_t     offset;        // instance: byte offset in the instance struct;
                               // static: -1
    int32_t     isRef;         // 1 = GC reference field
    void*       staticAddr;    // static: &StaticStorage slot; instance: null
    int32_t     flags;         // bit0 public, bit1 static, bit2 literal
};

// One reflected property. Accessors are resolved by name through the SAME
// methods table (single source of truth -- no duplicated fnPtrs).
struct Il2CppPropertyInfo
{
    const char* name;          // property name
    const char* typeName;      // property type full name
    const char* getterName;    // accessor method name, null if none
    const char* setterName;    // accessor method name, null if none
    int32_t     indexParams;   // indexer parameter count (0 = non-indexed)
};

// Reflection metadata for one type. klass is the EXISTING runtime descriptor
// (vtable/GC/baseClass are already there -- never duplicated).
struct Il2CppTypeInfo
{
    const char*          name;          // full type name == klass->name
    const Il2CppClass*   klass;         // existing runtime descriptor
    const Il2CppTypeInfo* baseType;     // base type info, null for System.Object
    const Il2CppTypeInfo* const* interfaces; // implemented interface infos
    int32_t              interfaceCount;
    const Il2CppFieldInfo*    fields;   // field table, null = none
    int32_t              fieldCount;
    const Il2CppMethodInfo*   methods;  // method table, null = none
    int32_t              methodCount;
    const Il2CppPropertyInfo* properties; // property table, null = none
    int32_t              propertyCount;
};

//------------------------------------------------------------------------------
// Registry (mirrors il2cpp_class_register / il2cpp_class_by_name).
//
// il2cpp_typeinfo_register -- add a type info table to the registry. The
//   transpiler emits one call per reflected type in generated startup code
//   (alongside il2cpp_class_register). Idempotent: registering the same
//   pointer twice is a no-op; null is a caller bug and is ignored. Overflow
//   is a hard error surfaced as abort() (fixed-size static storage,
//   constraint #12 -- no global-constructor heap allocation).
// il2cpp_typeinfo_lookup  -- linear search for a type info by full type name
//   (string key). Registry is small (<= kMaxRegisteredTypeInfos), so a
//   linear scan is simpler and cache-friendlier than a hash. Returns nullptr
//   for unknown or null names -- callers treat null as "not reflected".
//------------------------------------------------------------------------------
void il2cpp_typeinfo_register(const Il2CppTypeInfo* info);
const Il2CppTypeInfo* il2cpp_typeinfo_lookup(const char* name);
// P25c: join a class descriptor to its typeinfo by KLASS POINTER. The
// name-based lookup is unreliable for nested/compiler-generated types (the
// typeinfo registers the FULL "+"-chained reflection name -- "Cases.YieldProbe+
// <Range>d__0" -- while Il2CppClass.name holds the SHORT name "<Range>d__0"),
// so the enum-interface dispatchers join by the typeinfo's existing klass
// back-pointer instead of the name string. Returns nullptr when the class has
// no reflected typeinfo (BCL/boxed/snapshot runtime objects).
const Il2CppTypeInfo* il2cpp_typeinfo_lookup_by_class(const Il2CppClass* klass);

//------------------------------------------------------------------------------
// Simplified reflection member queries (the primitives behind Type's
// GetProperty / GetMethod / GetField icalls; design section 1).
//
//   info -- the type info table (null -> NullReferenceException, the null
//           Type receiver contract)
//   name -- member name (null -> ArgumentNullException -- see the header
//           exception contract above for the per-member paramName)
//
// Each query searches the type's OWN table first (most-derived declaration
// wins, .NET shadowing semantics), then walks the baseType chain so members
// declared on base types are found for derived types (inherited members are
// returned by .NET reflection). Tables are compared by exact member name
// (strcmp); no BindingFlags, overload resolution or case-insensitive
// matching (simplified M5.5 model).
//
//   Returns the matching entry pointer, or nullptr when the member does not
//   exist (own table + base chain).
//------------------------------------------------------------------------------
const Il2CppPropertyInfo* il2cpp_type_get_property(const Il2CppTypeInfo* info, const char* name);
const Il2CppMethodInfo*   il2cpp_type_get_method(const Il2CppTypeInfo* info, const char* name);
const Il2CppFieldInfo*    il2cpp_type_get_field(const Il2CppTypeInfo* info, const char* name);

//------------------------------------------------------------------------------
// Managed-string ABI queries -- the icall surface the TRANSPILER wires
// (System.Type.GetProperty/GetMethod/GetField; MethodMap.Type.cs). The
// simplified Type model (il2cpp_typeext.h assumption 1) represents a Type
// VALUE as the full name string, so these take the type name + member name
// as managed strings (Il2CppObject* = Il2CppString*) instead of the raw
// table pointers above.
//
//   typeName  -- the full type name string (e.g. "T.Derived")
//   memberName-- the member name string (e.g. "Value")
//
// Each query resolves typeName through the registry
// (il2cpp_typeinfo_lookup), then runs the table query above (own table
// first, baseType chain second). The RESULT is the member-name string
// (receiver identity, no allocation) on a hit, or nullptr when the type is
// not registered, the member is absent (own + base chain), or the name is
// malformed -- matching .NET's "GetProperty/GetMethod/GetField return null"
// contract for a NOT-FOUND name (no throw; net48: GetMethod("") -> null).
// Exception contract (the icall surface mirrors the raw primitives):
// typeName null -> NullReferenceException (null Type receiver, net48);
// memberName null -> ArgumentNullException (param "name" for GetMethod/
// GetProperty, empty for GetField -- net48 probe-verified). The simplified
// member model (like the Type model) represents the returned
// PropertyInfo/MethodInfo/FieldInfo VALUE as its name string;
// MemberInfo::get_Name folds to identity downstream.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_property(Il2CppObject* typeName, Il2CppObject* memberName);
Il2CppObject* il2cpp_typeinfo_get_method(Il2CppObject* typeName, Il2CppObject* memberName);
Il2CppObject* il2cpp_typeinfo_get_field(Il2CppObject* typeName, Il2CppObject* memberName);

} // namespace il2cpp
