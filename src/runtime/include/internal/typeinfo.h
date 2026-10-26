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
#include "il2cpp_array.h"    // Il2CppArray (R2 member-enumeration results)

namespace il2cpp {

// One reflected method. signature is the MethodSignatureCoder encoding
// ("i" / "V:System.String" / "r:Calc.Point" ...) so the runtime and the
// transpiler share one encoding.
struct Il2CppMethodInfo
{
    const char* name;          // method name, e.g. "Add"
    const char* signature;     // signature encoding (return + params)
    int32_t     paramCount;    // number of parameters (from signature)
    int32_t     flags;         // bit0 public, bit1 static, bit2 ctor, bit3 virtual,
                               // bit4 private, bit5 Internal(0x3), bit6 FamORAssem(0x5)
    int32_t     virtualSlot;   // vtable slot if virtual, else -1
    const char* declaringType; // declaring type full name (inherited methods
                               // are listed under THEIR declaring type's table)
    void*       fnPtr;         // AOT static function address (or invoker wrapper).
                               // null = runtime-unreachable method (trimmed).
    // REFL3 (item 2): per-parameter metadata for MethodBase.GetParameters()
    // (the ParameterInfo surface). Trailing field (zero-delta default: existing
    // emitted method tables leave it nullptr -- GetParameters answers an empty
    // array with no ParameterInfo objects). A compact pipe encoding of the
    // per-parameter NAME and `out` flag, one row per param joined by ';':
    //   "a|0;b|0"      -- parameterless / no-meta -> nullptr
    //   "x|0;y|1;z|0"  -- param 1 (`y`) is an `out` parameter (IsOut true)
    // Parameter NAMES cannot contain '|' or ';' (C# identifiers). The
    // parameter TYPE full name is decoded from the signature at runtime
    // (ParamEncodingToTypeValueName), so the meta needs only name + flags.
    const char* paramMeta;     // per-param "name|outFlag" rows, ';'-joined
    // p-refl2-gapmatrix (B05): the method's ECMA MethodDef token
    // (0x06000000 | MethodDef row) for MemberInfo.MetadataToken on a
    // member VALUE. 0 for a synthesized/trimmed entry (documented boundary).
    int32_t     metadataToken;
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
    int32_t     flags;         // bit0 public, bit1 static, bit2 literal, bit4 private,
                               // bit5 Internal(0x3), bit6 FamORAssem(0x5),
                               // bit7 InitOnly (FieldAttributes.InitOnly 0x20)
    // p-refl2-gapmatrix (B05): the field's ECMA FieldDef token
    // (0x04000000 | FieldDef row) for MemberInfo.MetadataToken on a
    // member VALUE. 0 for a synthesized entry (documented boundary).
    int32_t     metadataToken;
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

// One reflected event (C3-1 / RF-N1 -- the COM-C3 event metadata pipeline
// shares this sub-table with GetEvents). Accessors are resolved by name
// through the SAME methods table (single source of truth -- no duplicated
// fnPtrs, the Il2CppPropertyInfo convention).
struct Il2CppEventInfo
{
    const char* name;          // event name
    uint8_t     eventIID[16];  // COM event-source interface IID (the folded
                               // [Guid], .NET Guid byte layout -- Data1/2/3
                               // little-endian, Data4 in order, il2cpp_com.h
                               // kComIid* convention). Zeroed = a managed-only
                               // event (no COM source interface resolved) --
                               // still enumerated by GetEvents; the C3-2 RCW
                               // attach path LOUD-rejects a zeroed IID.
    const char* addName;       // add accessor method name, null if none
    const char* removeName;    // remove accessor method name, null if none
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
    const Il2CppEventInfo*    events;   // event table, null = none (C3-1/RF-N1)
    int32_t              eventCount;
    // RF-N4: generic-type identification (NEXT2-DECISION section ② RF-N4).
    // Trailing field (0 for non-generic types -- the zero-delta default; the
    // transpiler emits it from TypeDef.GenericArity). A generic DEFINITION
    // AND its closed CLONES both carry the definition's arity and register
    // under the SAME name key (ReflectionFullName -- the clone's name IS the
    // definition's name), so the runtime answers IsGenericType for
    //   typeof(Box<>) and typeof(Box<int>) alike and can never distinguish a
    //   definition from its clones (IsGenericTypeDefinition is decided at
    //   TRANSPILE time by the TypeEmitter/StackFold fold, NOT at runtime).
    int32_t              genericArity;
    // D (audit A3/observation 3): the BCL non-Object base terminal. When a
    // user type derives directly from a BCL type (CustomEx : System.Exception),
    // the resolved baseType pointer stays null (BCL bases are not translated)
    // but net48 Type.BaseType is NON-null (System.Exception) -- the runtime
    // get_BaseType icall returns this full base name on the BCL-base edge.
    // User bases (baseType pointer set) keep this null; System.Object roots
    // (BaseTypeName "System.Object") keep this null (net48 typeof(object)
    // .BaseType == null). Trailing field (like genericArity) -- the emitter
    // emits it for every reflected type.
    const char*          baseTypeName;
    // RF-N5 (NEXT2-DECISION section ② RF-N5): nested-type sub-table. One
    // name per DIRECT nested type (metadata declaration order -- net48
    // GetNestedTypes enumerates own nested types only, never inherited).
    // Each name is the nested type's REFLECTION full name -- the net48 '+'
    // form ("Cases.Outer+Inner", TypeEmitter.ReflectionFullName of the
    // expanded nested TypeDef, the same key its own typeinfo registers
    // under) -- so the GetNestedTypes result elements are Type VALUES
    // (full-name strings) exactly like the other Type-returning icalls.
    // null = no nested types. Trailing field (like genericArity/baseTypeName
    // -- the zero-delta default; the emitter emits nothing for nested-free
    // types).
    const char* const*  nestedNames;
    int32_t             nestedCount;
    // REFL6B (item A): the parallel VISIBILITY byte column -- one
    // TypeAttributes&0x7 value per nestedNames row (the expanded nested
    // TypeDef's VisibilityMask: NestedPublic=0x2, NestedPrivate=0x3,
    // NestedFamily=0x4, NestedAssembly=0x5, NestedFamANDAssem=0x6,
    // NestedFamORAssem=0x7). The GetNestedType(string[,BindingFlags]) and
    // GetNestedTypes visibility filters read it (net48: default GetNestedTypes()
    // is Public-only, GetNestedType(name) binds Public-only; the NonPublic bit
    // admits every non-public nested entry). null = absent (nested-free types;
    // the enumerators admit all entries -- defensive pass-through).
    const uint8_t*      nestedVis;
    // O4 (boxed user-struct reflection): whether this type info describes a
    // VALUE type (a translated user struct -- `TypeDef.IsValueType`). The
    // Il2CppFieldInfo.offset column is STRUCT-RELATIVE for a value type
    // (starts at 0 -- no object header), so GetValue/SetValue must add the
    // box payload offset (one object header) to reach the field in a boxed
    // struct. A class's offsets are object-relative already. Enums keep 0
    // (their value__ slot-grid offset coincides with the box payload).
    // Trailing field (like genericArity / baseTypeName / nestedNames -- the
    // zero-delta default; the emitter emits 0 for reference types).
    int32_t             isValueType;

    // refl2 (A): TypeAttributes-derived flags for the Type predicate surface
    // (IsAbstract / IsSealed / IsPublic / IsNested). Trailing field (zero
    // default = a pre-refl2 table answers False for all four, which is the
    // documented "not reflected / unknown" answer). Bits:
    //   0x01 Abstract (TypeAttributes.Abstract 0x20)
    //   0x02 Sealed   (TypeAttributes.Sealed   0x01)
    //   0x04 Public   (VisibilityMask == 0x1 NotPublic? no -- Public is 0x1)
    //   0x08 Nested   (the type is a nested TypeDef)
    // The transpiler bakes them from the TypeDef's raw metadata attributes, so
    // the answers are the exact ECMA bits, not a shape guess.
    int32_t             typeFlags;

    // refl2 (A): the type's ECMA metadata token (0x02000000 | TypeDef row) for
    // Type.MetadataToken on a runtime receiver. Trailing field (0 = unknown,
    // the pre-refl2 zero-delta default).
    int32_t             metadataToken;

    // refl2 (A): the RAW ECMA TypeAttributes value (TypeDef.Attributes) for
    // Type.Attributes and the exact visibility predicates (IsNestedPublic /
    // IsNestedPrivate / ...). Trailing field (0 = unknown, the zero-delta
    // default).
    int32_t             attributes;
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
// p-refl2-gapmatrix (C06): enumerate the typeinfo registry (Assembly.GetTypes
// on the input assembly). il2cpp_typeinfo_at returns nullptr out of range.
int32_t il2cpp_typeinfo_count();
const Il2CppTypeInfo* il2cpp_typeinfo_at(int32_t index);
// ASCII case-insensitive variant of il2cpp_typeinfo_lookup (Type.GetType's
// ignoreCase semantics). Same null / miss contract as the ordinal lookup.
const Il2CppTypeInfo* il2cpp_typeinfo_lookup_ignore_case(const char* name);
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
const Il2CppEventInfo*    il2cpp_type_get_event(const Il2CppTypeInfo* info, const char* name);
// GAP-1 (P0): the BindingFlags-aware member query primitives. `bindingAttr` is
// the net48 System.Reflection.BindingFlags value (Public=16, NonPublic=32,
// Instance=4, Static=8); each hit is filtered by visibility + static as net48
// single-member queries do (the (string) overloads bind Public|Instance|Static
// -- a private member is invisible; an explicit NonPublic bit admits private
// entries; an explicit Instance-only/Static-only bit narrows the search). A
// filtered-out hit is skipped and the base-chain scan continues (a public base
// same-name member is still found). Properties have no flags field
// (Il2CppPropertyInfo) -- their visibility derives from the accessor method
// flags (a property is public when any accessor is public), resolved through
// the same table's methods.
const Il2CppPropertyInfo* il2cpp_type_get_property_flags(const Il2CppTypeInfo* info, const char* name,
                                                         int32_t bindingAttr);
const Il2CppMethodInfo*   il2cpp_type_get_method_flags(const Il2CppTypeInfo* info, const char* name,
                                                       int32_t bindingAttr);
const Il2CppFieldInfo*    il2cpp_type_get_field_flags(const Il2CppTypeInfo* info, const char* name,
                                                      int32_t bindingAttr);

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
Il2CppObject* il2cpp_typeinfo_get_event(Il2CppObject* typeName, Il2CppObject* memberName);
// REFL-ARITY: Type.GetEvent(string, BindingFlags) -- the flags-aware single-
// event query (the (string) overload binds Public|Instance|Static). bindingAttr
// is the net48 System.Reflection.BindingFlags value; the event is filtered by
// its ADD accessor's visibility/static (the events_flags precedent). Returns the
// event-NAME string on a hit, nullptr otherwise. Null typeName/memberName keep
// the NRE/ArgumentNullException doctrine.
Il2CppObject* il2cpp_typeinfo_get_event_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                              int32_t bindingAttr);
// GAP-1 (P0): the BindingFlags-carrying managed-string variants (the
// Type.Get*Method/GetProperty/GetField(string, BindingFlags) overloads). The
// bindingAttr slot is the raw net48 System.Reflection.BindingFlags int32; the
// no-arg overloads above are equivalent to passing Public|Instance|Static
// (16|4|8) -- net48's default binding, so a private member is invisible there.
Il2CppObject* il2cpp_typeinfo_get_property_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                                 int32_t bindingAttr);
Il2CppObject* il2cpp_typeinfo_get_method_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                               int32_t bindingAttr);
Il2CppObject* il2cpp_typeinfo_get_field_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                              int32_t bindingAttr);
// refl2 (Type.GetMethod/GetProperty(string, Type[])): the exact-signature
// variants. `types` is the managed Type[] whose ELEMENTS are Type VALUES
// (full-name strings -- the simplified Type model); the runtime matches the
// member by arity + per-param type identity against the reflected method's
// signature / the property's indexer parameter types (net48 GetMethod(name,
// Type[]) binds Public|Instance|Static by default). Returns the member-name
// string on an exact match, nullptr otherwise (not found / type not reflected
// / malformed). Null typeName/memberName keep the net48 null-receiver /
// null-name contract (NRE / ArgumentNullException).
Il2CppObject* il2cpp_typeinfo_get_method_types(Il2CppObject* typeName, Il2CppObject* memberName,
                                               Il2CppArray* types);
// REFL-ARITY: Type.GetMethod(string, BindingFlags, Binder, Type[],
// ParameterModifier[]) -- the 5-param form. The binder and modifiers are
// ignored by net48's match (default binder; by-ref modifiers only -- W7), but
// bindingAttr DOES filter, so this is the flags-aware sibling of
// il2cpp_typeinfo_get_method_types. Same exact-signature match + result
// contract (the member-NAME string on a hit, nullptr otherwise; NRE /
// ArgumentNullException doctrine).
Il2CppObject* il2cpp_typeinfo_get_method_types_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                                     Il2CppArray* types, int32_t bindingAttr);
Il2CppObject* il2cpp_typeinfo_get_property_types(Il2CppObject* typeName, Il2CppObject* memberName,
                                                 Il2CppArray* types);
// D (audit A3/observation 3): Type.BaseType on a Type VALUE (full-name
// string). Resolves the typeinfo registry and returns the BASE type's full
// name string (the simplified Type model) on a hit -- the resolved user base
// (baseType pointer -> its name) or the BCL terminal base
// (baseTypeName field). System.Object roots / unregistered / malformed ->
// nullptr. Null typeName -> NullReferenceException (net48: ((Type)null)
// .BaseType throws NRE).
Il2CppObject* il2cpp_typeinfo_get_base_type(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// refl2 -- A-class type predicates on the name-string (Type VALUE) receiver.
// The transpiler Type VALUE is the full-name string; the pre-refl2 mappings
// passed that STRING to the descriptor-primitives (il2cpp_type_is_array
// (Il2CppClass*) etc.), which fail to compile. These name-string wrappers
// decide each predicate by NAME / typeinfo, no descriptor required:
//   is_array / has_element_type   -- '['-prefix rule (arrays);
//   is_primitive                  -- the .NET primitive full-name set;
//   is_enum                       -- typeinfo baseTypeName == "System.Enum";
//   is_value_type                 -- primitive name OR reflected isValueType /
//                                    "System.ValueType" base terminal (an enum
//                                    is a value type, net48);
//   is_class                      -- NOT a value type and NOT an interface;
//   is_interface                  -- the reflected klass is a zero-instanceSize
//                                    interface descriptor;
//   get_element_type              -- the name with the leading '[' stripped
//                                    (array element TYPE VALUE), null non-array.
// Null receiver -> NullReferenceException. Unregistered non-primitive names ->
// 0 / null (defensive).
int32_t il2cpp_typeinfo_is_array(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_has_element_type(Il2CppObject* typeName);
Il2CppObject* il2cpp_typeinfo_get_element_type(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_primitive(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_enum(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_value_type(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_interface(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_class(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// refl2 -- Type conformance predicates on the name-STRING receiver.
//   il2cpp_typeinfo_is_assignable_from(targetName, sourceName) -- both Type
//     VALUE names; delegates to il2cpp_type_is_assignable_from (typeext.cpp)
//     on the resolved descriptors (identical semantics: identity incl.
//     arrays-by-prefix, base chain, interface scan).
//   il2cpp_typeinfo_is_instance(obj, typeName) -- Type.IsInstanceOfType(object):
//     the receiver name resolved to a descriptor vs obj->klass through
//     icall_type_is_instance. obj == null -> 0 (net48 IsInstanceOfType(null)
//     is False, no throw).
//   il2cpp_typeinfo_is_subclass_of(derivedName, baseName) -- both names;
//     delegates to icall_type_is_subclass_of (NOT of ITSELF semantics).
// Names resolve through the typeinfo registry first (user types), then the
// builtin class registry (il2cpp_class_by_name -- BCL/primitive/array types).
// Null receiver name -> NullReferenceException (the .NET ((Type)null)
// doctrine); null argument name -> the per-primitive net48 contract
// (IsAssignableFrom(null) False; IsSubclassOf(null) ArgumentNullException).
int32_t il2cpp_typeinfo_is_assignable_from(Il2CppObject* targetName, Il2CppObject* sourceName);
int32_t il2cpp_typeinfo_is_instance(Il2CppObject* obj, Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_subclass_of(Il2CppObject* derivedName, Il2CppObject* baseName);

//------------------------------------------------------------------------------
// refl2 -- Type.get_Namespace on the name-string receiver. net48 Namespace is
// the FULL NAME truncated at the LAST '.' (the final segment is the type's
// short name; a nested type's '+'-chain rides inside that final segment, so
// the truncation keeps the namespace of the outermost declaring type --
// "Cases.Outer+Inner" -> "Cases"). A GLOBAL-namespace type (no '.') returns
// null (probe-pinned: net48 does NOT return the empty string). Null receiver
// -> NullReferenceException.
Il2CppObject* il2cpp_typeinfo_get_namespace(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// refl2 -- Type.GetInterface(string[, bool]) on the name-string receiver.
// Matches the requested name against the interface's FULL name and SIMPLE
// name across the type's own + base chain (net48: a derived type inherits its
// bases' interfaces). net48 matching rules (probe-pinned):
//   * a query name CONTAINING a '.' uses the FULL-name path ONLY -- always
//     ordinal case-sensitive, it IGNORES the ignoreCase flag
//     ("cAsEs.IDerived6", ignoreCase=true -> still no match);
//   * a DOT-FREE query name ("IDerived6") is the SIMPLE-name path, which
//     follows ignoreCase (ordinal-ignore-case when true).
// Returns the interface FULL-NAME string (a Type VALUE) on a hit, nullptr on
// a miss. Null receiver -> NullReferenceException; null name -> the net48
// ArgumentNullException (param "name").
Il2CppObject* il2cpp_typeinfo_get_interface(Il2CppObject* typeName, Il2CppObject* ifaceName,
                                            int32_t ignoreCase);

//------------------------------------------------------------------------------
// RF-N4 -- generic-type identification icalls (NEXT2-DECISION section 2 RF-N4,
// ruling 1: the three-way split lands on the transpiler side). Each takes the Type VALUE (full-name string, the same
// carrier the single-member queries use) and reads the typeinfo's genericArity.
//
//   il2cpp_typeinfo_is_generic_type          -- Type.IsGenericType: 1 iff the
//        typeinfo exists AND genericArity > 0 (0 for an unregistered /
//        non-generic type -- net48 typeof(int).IsGenericType == False).
//   il2cpp_typeinfo_get_generic_arity        -- the raw genericArity (int32).
//   il2cpp_typeinfo_get_generic_arguments   -- Type.GetGenericArguments():
//        THREE-WAY split -- an unregistered / non-generic type -> an EMPTY
//        array (net48 typeof(int).GetGenericArguments() is empty); a type
//        with genericArity > 0 -> LOUD NotSupportedException (the genericArg-
//        Names Plan B is deferred -- no argument fabrication, ever). The
//        empty-array path returns EmptyStringArray() (the shared zero-length
//        reference-element array the not-found member enumerators also use).
//   il2cpp_typeinfo_get_generic_type_definition -- Type.GetGenericTypeDefinition():
//        genericArity > 0 -> the receiver ITSELF (identity -- the simplified
//        Type model; the folded name of a closed instantiation IS the
//        definition's full name, so identity IS the definition). Non-generic /
//        unregistered -> LOUD InvalidOperationException (net48
//        typeof(int).GetGenericTypeDefinition() throws InvalidOperation-
//        Exception).
// A null / malformed typeName is treated as an unknown (non-generic) type:
// predicates -> 0, GetGenericArguments -> empty array, GetGenericTypeDefinition
// -> InvalidOperationException (defensive query convention; a null Type
// receiver is not valid IL).
//------------------------------------------------------------------------------
// REFL-ARITY: Type.GetTypeCode() on a RUNTIME receiver (a GetType() result).
// The receiver is the Type VALUE (full-name string); this maps the NAME to the
// net48 System.TypeCode enum value (the same table as il2cpp_type_get_typecode
// in typeext.cpp -- Int32=9 / Boolean=3 / Char=4 / Double=14 / String=18 /
// Decimal=15 / else Object=1). A null / malformed / unregistered name answers
// 0 (TypeCode.Empty; net48 Type.GetTypeCode(null) -> Empty).
int32_t il2cpp_typeinfo_get_typecode_from_name(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_generic_type(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_get_generic_arity(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_generic_arguments(Il2CppObject* typeName);
Il2CppObject* il2cpp_typeinfo_get_generic_type_definition(Il2CppObject* typeName);
//------------------------------------------------------------------------------
// REFL6B (item B5) -- Type.get_ContainsGenericParameters on a RUNTIME
// receiver. The FOLDED typeof chains are decided (and folded to 1/0) at
// transpile time in StackFold; a runtime Type VALUE (a GetType() result)
// can NEVER denote an open generic (MakeGenericType is LOUD -- no fabricated
// generic arguments, no open instances), so the runtime answers CONSTANT 0
// (net48 ContainsGenericParameters False for every runtime-reachable type).
// Null/malformed typeName -> 0 (defensive).
//------------------------------------------------------------------------------
int32_t il2cpp_typeinfo_is_contains_generic_params(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// R2 (B2) -- member ENUMERATION icalls. Each returns an Il2CppArray* of
// member-NAME managed strings (the simplified member model -- a member VALUE
// is its name string, the same carrier the single-member queries return).
//   GetMethods      : own + base-chain method tables, public only (bit0)
//   GetFields       : own + base-chain field tables, public only (bit0)
//   GetProperties   : own + base-chain property tables (no flags field --
//                     all properties, documented simplification)
//   GetConstructors : own + base-chain method tables, public + ctor (bit2)
//   GetInterfaces   : own + base-chain interface lists (type name strings)
// A null / malformed / unregistered typeName -> an EMPTY array (net48
// returns an empty array, never null).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_methods(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_fields(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_properties(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_constructors(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_interfaces(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_events(Il2CppObject* typeName);
// RF-N5 (NEXT2-DECISION section ② RF-N5): GetNestedTypes / GetMembers.
//   il2cpp_typeinfo_get_nested_types -- OWN nested types only (name strings
//     in metadata declaration order; net48 never inherits nested types).
//   il2cpp_typeinfo_get_members   -- the five-enumeration union (methods +
//     ctors + properties + events + fields, own + base chain). Probe-pinned
//     net48 order: methods (public, INCLUDING accessors get_/set_/add_/
//     remove_ -- net48 returns them as method members, excluding .ctor/.cctor),
//     then ctors (public, of the queried type ONLY -- net48 never returns an
//     inherited constructor), then properties (all), then events (all), then
//     fields (public). Accessors stay in the methods list (the simplified
//     member model folds every active property/event accessor into the
//     methods table -- net48 GetMembers returns them as members too).
// A null / malformed / unregistered typeName -> an EMPTY array (net48
// returns an empty array, never null).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_nested_types(Il2CppObject* typeName);
// REFL-ARITY: Type.GetNestedTypes(BindingFlags) -- the arity-1 filtered
// enumeration (the () overload is Public-only by default). bindingAttr is the
// net48 System.Reflection.BindingFlags value: Public(16) admits a NestedPublic
// entry (nestedVis & 0x7 == 0x2), NonPublic(32) admits every non-public entry,
// NEITHER -> Public-only (the same nested-type filter GetNestedType applies).
// A null / malformed / unregistered typeName -> an EMPTY array (net48 never
// returns null).
Il2CppArray* il2cpp_typeinfo_get_nested_types_flags(Il2CppObject* typeName, int32_t bindingAttr);
//------------------------------------------------------------------------------
// REFL6B (item A) -- Type.GetNestedType(string) / (string, BindingFlags) +
// the GetNestedTypes() public-only default.
//   il2cpp_typeinfo_get_nested_type(typeName, nestedSimpleName, bindingAttr):
//     the SINGLE-nested-type query. net48 semantics (probe-pinned on
//     p-refl6-nested):
//       * name match is by the nested type's SIMPLE name (the "" segment after
//         the last '+' of the '+'-reflection-name rows in nestedNames);
//       * visibility filter follows the bindingAttr: Public(16) admits a
//         NestedPublic entry (nestedVis & 0x7 == 0x2), NonPublic(32) admits
//         every non-public nested entry; NEITHER bit -> Public-only (net48
//         default). Instance/Static bits are irrelevant (types are neither).
//       * an EMPTY simple name returns nullptr without throwing (net48
//         GetNestedType("") -> null); a null nestedName throws
//         ArgumentNullException; a null typeName -> NullReferenceException
//         (the null-Type receiver doctrine).
//     Returns the nested type's FULL-NAME string (the '+'-reflection name -- a
//     Type VALUE, the same carrier GetNestedTypes returns) or nullptr on a
//     name/visibility miss.
//   il2cpp_typeinfo_get_nested_types: FIXED to Public-only by default (net48
//     GetNestedTypes() binds Public -- oracle [n.all]=1 for a type with four
//     nested classes only one public). A null nestedVis (nested-free or
//     defensive) admits all entries (pass-through).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_nested_type(Il2CppObject* typeName,
                                              Il2CppObject* nestedSimpleName,
                                              int32_t bindingAttr);
Il2CppArray* il2cpp_typeinfo_get_members(Il2CppObject* typeName);
// C3-2: EventInfo.AddEventHandler / RemoveEventHandler (the managed events-
// table accessor delivery; an RCW receiver doors the deferred shape A).
void il2cpp_eventinfo_add(Il2CppObject* eventName, Il2CppObject* target, Il2CppObject* handler);
void il2cpp_eventinfo_remove(Il2CppObject* eventName, Il2CppObject* target, Il2CppObject* handler);

//------------------------------------------------------------------------------
// R2 (B2) -- FieldInfo.GetValue/SetValue narrow set (int32/int64/string/
// object; slot width dispatched by the field's typeName). The receiver is
// the FIELD-NAME string; the declaring type is joined from the TARGET
// object's klass (il2cpp_typeinfo_lookup_by_class) and the field is found in
// the own + base-chain tables (public only, net48 default binding).
//   GetValue -> the boxed value (int32/int64 via the box carriers; string/
//               object returned as the reference itself).
//   SetValue -> writes through the box payload (int32/int64) or the
//               reference slot (string/object).
// Null target -> NullReferenceException (net48 instance-field doctrine; the
// static-field-with-null-target case has no klass to join -- documented
// deviation). Unknown field / unreflected type -> nullptr / no-op per the
// defensive query convention.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_field_get_value(Il2CppObject* fieldName, Il2CppObject* target);
void         il2cpp_typeinfo_field_set_value(Il2CppObject* fieldName, Il2CppObject* target,
                                             Il2CppObject* value);

//------------------------------------------------------------------------------
// RF-N2 -- PropertyInfo.GetValue/SetValue: the property table's accessor
// dispatch through the SAME invoker as MethodInfo.Invoke (il2cpp_methodinfo_
// invoke's RF-1 instance-'this' path -- the C3-2 event accessor precedent).
// The receiver is the PROPERTY-NAME string (the simplified member model, as
// il2cpp_typeinfo_get_property returns); the declaring type is joined from
// the TARGET object's klass and the property resolved in the own + base-chain
// property tables.
//   get: dispatches getterName with the index array elements as args (a
//        null/empty index on a non-indexed property = the ordinary zero-arg
//        getter). Returns the boxed value (int32/int64/ref -- the invoker's
//        return-kind boxing).
//   set: dispatches setterName with [index elements..., value] as args (the
//        non-indexed set_<P>(T) has no index elements, so args = { value }).
// Null target -> TargetException (net48 instance-property doctrine -- NOT
// NullReferenceException, the field-value difference; probe-verified).
// Unknown property -> nullptr (defensive query convention).
// Missing accessor (write-only GetValue / read-only SetValue) -> LOUD
// ArgumentException (net48 "Property Get/Set method not found").
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_property_get_value(Il2CppObject* propertyName, Il2CppObject* target,
                                                 Il2CppArray* index);
void         il2cpp_typeinfo_property_set_value(Il2CppObject* propertyName, Il2CppObject* target,
                                                Il2CppObject* value, Il2CppArray* index);
// GAP-3 (P1): PropertyInfo.GetGetMethod()/GetSetMethod(). dir = 0 -> the
// getter accessor name string (a MethodInfo VALUE), 1 -> the setter accessor.
// The receiver is the PROPERTY-NAME string (the simplified member model); the
// declaring type is resolved by scanning the registry for the deepest type
// declaring a property with that name (the same member-name doctrine as RF-N6
// / the static-field null-target path). The PUBLIC accessor is bound by
// default (net48 GetGetMethod()/GetSetMethod() bind publicOnly=true) -- a
// private accessor returns nullptr (the visibility filter), as does a property
// without the accessor or an unregistered property. Null receiver ->
// NullReferenceException (the net48 null-PropertyInfo chain).
Il2CppObject* il2cpp_typeinfo_property_get_accessor(Il2CppObject* propertyName, int32_t dir);
// REFL-ARITY: PropertyInfo.GetGetMethod(bool nonPublic) /
// GetSetMethod(bool nonPublic) -- the arity-1 forms net48 HAS. The no-arg
// overloads bind publicOnly=true; nonPublic != 0 admits a non-public accessor
// too (net48). dir = 0 getter / 1 setter, same result contract (the accessor
// METHOD-NAME string or nullptr; NRE null receiver).
Il2CppObject* il2cpp_typeinfo_property_get_accessor_ex(Il2CppObject* propertyName, int32_t nonPublic,
                                                       int32_t dir);

//------------------------------------------------------------------------------
// GAP-5 (P1) -- ConstructorInfo: Type.GetConstructor(Type[]) + the ctor
// Invoke used by ConstructorInfo.Invoke(object[]).
//
//   il2cpp_typeinfo_get_constructor(typeName, types) -- Type.GetConstructor.
//     The typeName is the TYPE VALUE (full-name string); types is the Type[]
//     (its length = the ctor arity, the simplified net48 param-type match).
//     Returns the ctor MEMBER VALUE "<declaringTypeFullName>::.ctor" (the
//     disambiguated value Invoke can resolve) or nullptr on no public ctor of
//     that arity. Null typeName -> NullReferenceException (the null Type
//     receiver doctrine).
//   il2cpp_constructorinfo_invoke(memberName, args) -- the Invoke. The
//     memberName is the ctor member value above; the runtime parses the
//     declaring type, joins the typeinfo, resolves the PUBLIC ctor of the
//     args length, ALLOCATES a fresh instance and RUNS the ctor on it through
//     il2cpp_methodinfo_invoke's instance-'this' dispatch (the ctor entry's
//     fnPtr is the void(C* this, args...) ABI -- the same shape the invoker
//     dispatches). The result is the constructed object.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_constructor(Il2CppObject* typeName, Il2CppArray* types);
// REFL-ARITY: Type.GetConstructor(BindingFlags, Binder, Type[],
// ParameterModifier[]) -- the 4-param form. The binder and modifiers are
// ignored by net48's match (default binder; by-ref modifiers only -- W7), but
// bindingAttr DOES filter, so this is the flags-aware sibling of
// il2cpp_typeinfo_get_constructor. Same ctor match + disambiguated
// "<FullName>::.ctor" result contract (nullptr on a miss; NRE doctrine).
Il2CppObject* il2cpp_typeinfo_get_constructor_flags(Il2CppObject* typeName, Il2CppArray* types,
                                                    int32_t bindingAttr);
Il2CppObject* il2cpp_constructorinfo_invoke(Il2CppObject* memberName, Il2CppArray* args);

//------------------------------------------------------------------------------
// REFL3 (item 2) -- MethodBase.GetParameters() + the ParameterInfo property
// surface. The simplified ParameterInfo VALUE is a pipe-joined managed string
// "name|typeFullName|position|flags" (built from the transpiler-baked
// Il2CppMethodInfo.paramMeta + the signature's param encodings).
//
//   il2cpp_typeinfo_get_parameters(memberName) -- MethodBase.GetParameters().
//     The receiver is a METHOD-NAME string (the simplified member VALUE) or a
//     disambiguated ctor value "<FullName>::.ctor". The runtime scans the
//     typeinfo registry for the DEEPEST type declaring the member, resolves the
//     method (own + base chain) or ctor, and returns a reference-element array
//     of ParameterInfo VALUE strings. Parameterless / unknown / malformed /
//     unregistered -> an EMPTY array (net48 never returns null). Null receiver
//     -> NullReferenceException.
//   il2cpp_parameterinfo_get_name / get_parameter_type / get_position /
//     is_out -- the four surfaced ParameterInfo properties, decoded from the
//     VALUE string. get_parameter_type returns a Type VALUE (full-name string
//     -- the same carrier typeof() folds; BYREF params decode the pointee's
//     name, the narrow model's documented simplification). Malformed values
//     yield the defensive defaults ("" / 0).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_parameters(Il2CppObject* memberName);
Il2CppObject* il2cpp_parameterinfo_get_name(Il2CppObject* value);
Il2CppObject* il2cpp_parameterinfo_get_parameter_type(Il2CppObject* value);
int32_t il2cpp_parameterinfo_get_position(Il2CppObject* value);
int32_t il2cpp_parameterinfo_is_out(Il2CppObject* value);

//------------------------------------------------------------------------------
// R3 (B4) -- MethodInfo.Invoke: il2cpp_methodinfo_invoke(memberName, target,
// object[] args). The receiver is the METHOD-NAME string (the simplified
// member model -- a member VALUE is its name string); the declaring type is
// joined from the TARGET object's klass (il2cpp_typeinfo_lookup_by_class;
// a class-descriptor target is accepted too) and the method is resolved in
// the own + base-chain tables by NAME + paramCount (the object[] length --
// the invoker's explicit-param count). Dispatch goes through
// il2cpp_invoker_invoke (invoker.h) with the object[] args unboxed by the
// resolved signature's slot kinds; the result is boxed by the signature's
// return kind (int32/int64/double/reference; void -> null). No object
// creation.
//   RF-1: an INSTANCE method (flags static bit clear) has the TARGET injected
//   as the 'this' reference slot at argbuf index 0 (the transpiler emits
//   instance methods with arg0 = this) -- the dispatch signature gains a
//   leading "r" param and the argCount is explicit + 1. A STATIC method (the
//   R3 free-function shape) gets NO 'this' slot: the object[] maps 1:1.
//   Unknown member / unreflected type / arity mismatch / bad signature ->
//     nullptr (defensive).
//   Null target -> NullReferenceException (net48 instance doctrine; a static
//     Invoke through this icall needs a non-null receiver to join the type).
//   Ambiguous name + paramCount -> LOUD AmbiguousMatchException.
//   fnPtr == null (trimmed / abstract / interface) -> LOUD
//     NotSupportedException (runtime-unreachable, checked for every method
//     bearing the requested name regardless of arity -- committed shape).
//   A REF/OUT (by-ref) parameter in the resolved method's signature -> LOUD
//     NotSupportedException (RF-N8 -- the invoker has no by-ref address ABI,
//     so the by-ref argument is NEVER silently defaulted to nullptr; the
//     reject fires before any value conversion, mirroring the RF-N7 style).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_methodinfo_invoke(Il2CppObject* memberName, Il2CppObject* target,
                                       Il2CppArray* args);

//------------------------------------------------------------------------------
// RF-N6 (NEXT2-DECISION section ② RF-N6) -- MEMBER-level GetCustomAttributes.
// The receiver is the MEMBER-NAME string (the simplified member model, the
// same carrier il2cpp_typeinfo_get_method/field/property return).
//
//   memberName : the member name string (method / field / property -- the
//                declaring type is NOT carried by the receiver, so the
//                runtime scans the typeinfo registry for the type that
//                DECLARES the member and picks the deepest one -- net48
//                most-derived-first doctrine; a member name unique across
//                the program is the documented simplification).
//   inherit    : 1 -> walk the declaring type's own + base chain collecting
//                the SAME-NAMED member's registered attributes, most-derived
//                first (net48 GetCustomAttributes(true) on an overridden
//                virtual returns derived + base declarations; fields have no
//                base-chain duplicates so they agree); 0 -> the declaring
//                member's attributes only.
//
// Returns an Il2CppArray* of the registered attribute objects, most-derived
// first. A member with no registered attributes -> EMPTY array (never null);
// a null memberName -> NullReferenceException (the net48 null-receiver
// doctrine); malformed / non-ASCII / over-long -> empty array.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_attribute_member_get_custom(Il2CppObject* memberName, int32_t inherit);

//------------------------------------------------------------------------------
// il2cpp_attribute_member_get_custom_named -- net48
// MemberInfo.GetCustomAttributes(Type attributeType, bool inherit) on the
// MEMBER-level registry (the RF-N6 member walk + the attributeType filter).
// Same receiver / inherit contract as il2cpp_attribute_member_get_custom
// above; the extra attributeTypeName is the managed-string full name of the
// filter type:
//   attributeTypeName : the filter type's full name (managed string). A null
//                       string -> no filter (matches every attribute type,
//                       the type-level CollectAttributes contract); an
//                       unresolvable name -> an empty array (no matches).
//
// Returns an Il2CppArray* of the registered attribute objects whose runtime
// class equals or derives from attributeTypeName, most-derived first. Same
// empty / null / exception doctrines as il2cpp_attribute_member_get_custom.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_attribute_member_get_custom_named(Il2CppObject* memberName,
                                                      Il2CppObject* attributeTypeName,
                                                      int32_t inherit);

//------------------------------------------------------------------------------
// RF-N7 (NEXT2-DECISION section ② RF-N7) -- the reflection operations that
// have NO AOT implementation and MUST fail loudly instead of falling to a
// spelling generated-member name (the pre-RF-N7 behavior: an unwired BCL
// member fell to the generated-name path, a phantom symbol that never linked):
//
//   il2cpp_makegenericmethod_loud(memberName, typeArgs) -- MethodInfo.
//       MakeGenericMethod(Type[]). Runtime generic-method instantiation has
//       no AOT support -> throws NotSupportedException unconditionally.
//   il2cpp_makegenerictype_loud(typeName, typeArgs)     -- Type.MakeGenericType
//       (Type[]). Same: no fabricated generic arguments (no fabrication) -> throws
//       NotSupportedException unconditionally.
//   il2cpp_createdelegate_loud(delegateType, methodName) -- Delegate.CreateDelegate
//       (Type, MethodInfo). The MethodInfo receiver rides as the member-name
//       string (the simplified member model); binding a delegate to a runtime
//       member has no AOT support -> throws NotSupportedException.
//
// All three keep the net48 null-receiver doctrine untouched by NOT throwing
// NRE first -- the rejection is unconditional and explicit (NotSupported-
// Exception), so a caller can never observe a silent wrong link or a null
// deref masquerading as "not implemented".
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_makegenericmethod_loud(Il2CppObject* memberName, Il2CppArray* typeArgs);
Il2CppObject* il2cpp_makegenerictype_loud(Il2CppObject* typeName, Il2CppArray* typeArgs);
Il2CppObject* il2cpp_createdelegate_loud(Il2CppObject* delegateType, Il2CppObject* methodName);

//------------------------------------------------------------------------------
// REFL4 (item 2) -- IntrospectionExtensions.GetTypeInfo(Type): the fold-
// identity. net48 returns a System.Reflection.TypeInfo, a Type SUBCLASS -- a
// Type VALUE in the simplified model. This icall is the runtime identity
// (returns the receiver Type VALUE unchanged) whose ReturnTypeName the
// transpiler records as "System.Type", so every downstream Type member
// (.Name/.FullName/.IsClass/.BaseType/GetProperty/==) on the GetTypeInfo()
// result resolves on the Type domain -- including typeof(int).GetTypeInfo()
// (an unregistered BCL type name, identity must NOT go through the typeinfo
// registry).
Il2CppObject* il2cpp_typeinfo_get_type_info(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// REFL4 (item 1) -- MemberInfo.get_MemberType / get_DeclaringType on the
// member-VALUE string receiver (the simplified member model: a MemberInfo
// VALUE is the member-NAME string the GetMethod/GetField/GetProperty/GetEvent/
// GetConstructor icalls return, and a Type receiver rides as the full-name
// string -- the simplified Type model). Both classify by scanning the typeinfo
// registry's member tables -- member names are unique per type, so the tables
// pin the kind/declarer exactly (a name string has no "&"-typed receiver,
// W7).
//
//   il2cpp_memberinfo_get_member_type(memberName) -- MemberInfo.get_MemberType.
//     Returns the net48 MemberTypes NUMERIC value (not an enum object -- the
//     fold prints (int)):
//       Method=8 Field=4 Property=16 Constructor=1 Event=2 TypeInfo=32
//       NestedType=128.
//     Classification is by registry scan:
//       * the disambiguated ctor value "<FullName>::.ctor" -> Constructor (1);
//       * a REGISTERED typeinfo name -> TypeInfo (32); a nested typeinfo name
//         (contains '+', the ReflectionFullName form) -> NestedType (128);
//       * otherwise scan the methods/fields/properties/events tables of every
//         registered type by exact name -- the FIRST table that owns the name
//         pins the kind (methods incl. accessors get_/set_/add_/remove_ --
//         net48 also reports those as Method, so a property NAME "PropB" (in
//         the properties table) vs its accessor "get_PropB" (in the methods
//         table) answers 16 vs 8 exactly like net48);
//       * not found anywhere -> TypeInfo (32) (defensive -- matches
//         typeof(int).MemberType for an unregistered BCL type-name value).
//     Null receiver -> NullReferenceException (the net48 null-MemberInfo chain).
//   il2cpp_memberinfo_get_declaring_type(memberName) -- get_DeclaringType.
//     Returns the declaring type's FULL-NAME string (a Type VALUE) or nullptr
//     for a top-level TYPE receiver (net48 typeof(MTPlain).DeclaringType is
//     null), the OUTER type name for a NESTED TYPE receiver, the parsed
//     declaring type of a ctor value, or the DEEPEST type declaring a member
//     name (registry scan by own-table membership; overrides re-declare the
//     name in the derived table -> most derived wins, inherited members stay
//     under THEIR declaring type's table -> the base wins). Null receiver ->
//     NullReferenceException.
//------------------------------------------------------------------------------
int32_t il2cpp_memberinfo_get_member_type(Il2CppObject* memberName);
Il2CppObject* il2cpp_memberinfo_get_declaring_type(Il2CppObject* memberName);

//------------------------------------------------------------------------------
// REFL4 (item 4/C1) -- ConstructorInfo.get_Name. The ctor member VALUE is the
// disambiguated "<FullName>::.ctor" (GAP-5 -- Invoke parses the declaring
// type out of it), so get_Name CANNOT be the generic member-name identity:
// net48 ConstructorInfo.Name is ".ctor". This icall decodes the ctor value
// (the ".ctor" key is ALWAYS the terminal segment, bare or "<FullName>::"
// qualified) and returns the literal ".ctor" -- the Invoke dependency on the
// disambiguated VALUE itself is untouched (only the Name property decodes).
// Null receiver / malformed value -> the ".ctor" literal (defensive; a ctor
// member value is always the ctor key).
Il2CppObject* il2cpp_ctorinfo_get_name(Il2CppObject* memberName);

//------------------------------------------------------------------------------
// REFL4 (item 3) -- Type.AssemblyQualifiedName. The receiver is the Type VALUE
// (full-name string); the result is the net48 AQN string
//   "<FullName>, <assemblySimpleName>, Version=1.0.0.0, Culture=neutral,
//    PublicKeyToken=null"
// The assembly SIMPLE NAME is a compile-time constant (single-assembly model)
// passed by the transpiler as the second argument (an interned literal). The
// icall formats "<receiver>, <asmSimpleName>" -- verifying the receiver is
// the byte-identical full name already embedded in the version-1 AQN body.
// Null receiver -> NullReferenceException.
Il2CppObject* il2cpp_typeinfo_get_assembly_qualified_name(Il2CppObject* typeName,
                                                          Il2CppObject* asmSimpleName);

//------------------------------------------------------------------------------
// REFL5 (item 1) -- the (BindingFlags) arity-1 member ENUMERATION overloads.
// net48 semantics (probe-pinned on p-refl5-1flags, no FlattenHierarchy):
//   * a member matches iff its visibility ACCEPTS the binding AND its
//     static/instance-ness accepts the binding:
//       visibility: public member <-> Public(16) bit, nonpublic <-> NonPublic(32);
//       staticness: static member <-> Static(8), instance <-> Instance(4).
//     With NEITHER staticness bit set nothing matches (GetMethods(Public) is
//     EMPTY -- net48, no "both" default in the arity-1 enumeration path).
//   * the BASE chain contributes only INHERITABLE members: INHERITED STATIC
//     members are never listed (own statics only) and INHERITED PRIVATE
//     members (flags bit 0x10) are never listed -- public/protected/internal
//     base members are inherited (protected/internal count as nonpublic).
//   * GetMethods NEVER lists ctors (GAP-4); GetConstructors lists the queried
//     type's OWN ctors only (no inherited ctor), matching the flags bits as
//     above (a private static .cctor answers NonPublic|Static -- net48).
//   * ordering = metadata table order (own, then base chain), the same chain
//     order the () overloads enumerate.
// Each returns the member-NAME string array (the simplified member model).
// A null / malformed / unregistered typeName -> an EMPTY array.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_methods_flags(Il2CppObject* typeName, int32_t bindingAttr);
Il2CppArray* il2cpp_typeinfo_get_fields_flags(Il2CppObject* typeName, int32_t bindingAttr);
Il2CppArray* il2cpp_typeinfo_get_properties_flags(Il2CppObject* typeName, int32_t bindingAttr);
Il2CppArray* il2cpp_typeinfo_get_events_flags(Il2CppObject* typeName, int32_t bindingAttr);
Il2CppArray* il2cpp_typeinfo_get_constructors_flags(Il2CppObject* typeName, int32_t bindingAttr);
Il2CppArray* il2cpp_typeinfo_get_members_flags(Il2CppObject* typeName, int32_t bindingAttr);

//------------------------------------------------------------------------------
// REFL6A (item B) -- Type.GetMember(string[, BindingFlags]). ONE runtime
// filter icall over the refl5 members_flags enumeration: the same five-slice
// union (methods incl. accessors / ctors own-only / properties / events /
// fields) with the refl5 binding filter AND an exact SIMPLE-name strcmp
// (net48 GetMember matches by simple member name). net48 default binding for
// the 1-arg overload is Public|Instance|Static (the transpiler appends the
// constant). Exception contract (net48 probe-pinned on p-refl6-gmember):
//   * null memberName  -> ArgumentNullException
//   * empty memberName -> EMPTY array, no throw
// Matching results keep the members_flags union ORDER (same-name members in
// the five-slice order). Returned elements are member-NAME strings (same
// carrier as GetMembers). A null / malformed / unregistered typeName -> an
// EMPTY array (the typeinfo defensive convention; a null Type receiver is
// not valid IL).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_members_flags_name(Il2CppObject* typeName,
                                                    Il2CppObject* memberName,
                                                    int32_t bindingAttr);

//------------------------------------------------------------------------------
// REFL6A (item A) -- the System.Type enum surface. The four Type.GetEnum*
// instance methods reuse the EXISTING enumext name-string query icalls
// (enumext.h) but ADD net48's non-enum-receiver contract: when the receiver
// type (the Type VALUE full-name string) is NOT a registered enum (the enum
// metadata registry, by name), the wrapper throws ArgumentException (net48
// "Type provided must be an Enum"). Registered enums delegate to the shared
// by_type_name icall unchanged (names / values / name / underlying search).
// The package enumext functions keep their documented unknown-name fallback
// (empty array / Int32) -- only the Type-arm wrappers throw, so the static
// System.Enum API surface is untouched.
//   il2cpp_typeinfo_get_enum_names   -> il2cpp_enum_get_names_by_type_name
//                                       (declaration-order name array)
//   il2cpp_typeinfo_get_enum_values  -> il2cpp_enum_get_values_by_type_name
//                                       (value array, declaration order)
//   il2cpp_typeinfo_get_enum_name    -> il2cpp_enum_get_name_by_type_name
//                                       (value-match name / unnamed->null)
//   il2cpp_typeinfo_get_enum_underlying_type -> il2cpp_enum_get_underlying_
//                                       type_by_type_name ("System.Int32"...)
// A null/malformed receiver typeName -> ArgumentException (defensive; a null
// Type receiver is not valid IL) -- net48 also throws ArgumentException for a
// non-enum receiver, and a null receiver is folded never-null.
//------------------------------------------------------------------------------
Il2CppArray*   il2cpp_typeinfo_get_enum_names(Il2CppObject* typeName);
Il2CppArray*   il2cpp_typeinfo_get_enum_values(Il2CppObject* typeName);
Il2CppObject*  il2cpp_typeinfo_get_enum_name(Il2CppObject* typeName,
                                             Il2CppObject* valueBox);
Il2CppObject*  il2cpp_typeinfo_get_enum_underlying_type(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// REFL5 (item 2) -- the remaining reachable exact-signature overloads.
//   il2cpp_typeinfo_get_property_return_type(typeName, memberName,
//       returnTypeName) -- Type.GetProperty(string, Type returnType). The
//     plain (string, Type) 2-param overload net48 HAS (there is NO
//     (string, Type[], Type[]) form -- refl4 note); matches the property by
//     name + property-type equality, ANY arity (a name+type duplicate would
//     be net48 AmbiguousMatch -- the first match wins in this narrow model,
//     W7), default Public|Instance|Static binding.
//   il2cpp_typeinfo_get_property_types_ret(typeName, memberName,
//       returnTypeName, types) -- Type.GetProperty(string, Type returnType,
//     Type[] types): name + return type + indexer-param exact match.
//   GetMethod(string, Type[], ParameterModifier[]) reuses
//     il2cpp_typeinfo_get_method_types (the ParameterModifier[] is ignored by
//     net48's match -- it only encodes by-ref modifiers, W7).
// Result: the member-NAME string (member VALUE) or nullptr on a miss; null
// typeName/memberName keep the NRE/ArgumentNullException doctrine.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_property_return_type(Il2CppObject* typeName,
                                                       Il2CppObject* memberName,
                                                       Il2CppObject* returnTypeName);
Il2CppObject* il2cpp_typeinfo_get_property_types_ret(Il2CppObject* typeName,
                                                     Il2CppObject* memberName,
                                                     Il2CppObject* returnTypeName,
                                                     Il2CppArray* types);
// REFL-ARITY: Type.GetProperty(string, BindingFlags, Binder, Type returnType,
// Type[] types, ParameterModifier[]) -- the 6-param form net48 HAS. The binder
// and modifiers are ignored by net48's match (default binder; by-ref modifiers
// only -- W7), but the BindingFlags DOES filter, so this is the flags-aware
// sibling of il2cpp_typeinfo_get_property_types_ret. Same result contract (the
// member-NAME string on a hit, nullptr otherwise; NRE / ArgumentNullException
// doctrine).
Il2CppObject* il2cpp_typeinfo_get_property_types_ret_flags(Il2CppObject* typeName,
                                                           Il2CppObject* memberName,
                                                           Il2CppObject* returnTypeName,
                                                           Il2CppArray* types,
                                                           int32_t bindingAttr);

//------------------------------------------------------------------------------
// REFL5 (item 3 / C2) -- the member-TYPE property family + the flag-derived
// predicates. The receiver is the MEMBER-VALUE string (the simplified member
// model). Result TYPE VALUEs are FULL-NAME strings (the simplified Type
// model -- e.g. "System.Int32" / "System.Void" / "System.EventHandler").
//   il2cpp_fieldinfo_get_field_type          -- FieldInfo.get_FieldType
//   il2cpp_propertyinfo_get_property_type    -- PropertyInfo.get_PropertyType
//   il2cpp_methodinfo_get_return_type        -- MethodInfo.get_ReturnType
//   il2cpp_eventinfo_get_event_handler_type  -- EventInfo.get_EventHandlerType
//     (decoded from the event's add accessor first parameter -- the events
//     table carries no type column).
//   il2cpp_memberinfo_get_reflected_type     -- MemberInfo.get_ReflectedType:
//     the DEEPEST type declaring the member (own members: == the query type;
//     W7: the name-string member VALUE carries no query-type, so an inherited
//     member answers its declaring base).
//   il2cpp_fieldinfo_is_assembly             -- FieldInfo.get_IsAssembly
//     (bit0x10-free nonpublic = internal; public/private both False -- net48
//     probe-pinned on public fields).
//   il2cpp_memberinfo_is_static / _is_public / _is_constructor -- the
//     MethodBase/FieldInfo flag predicates (bit1 / bit0 / bit2), including
//     the disambiguated ctor VALUE "<FullName>::.ctor" / bare ".ctor"
//     (IsConstructor True) and the field-table receivers for FieldInfo.
// Null receiver -> NullReferenceException (the null-MemberInfo chain).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_fieldinfo_get_field_type(Il2CppObject* memberName);
Il2CppObject* il2cpp_propertyinfo_get_property_type(Il2CppObject* memberName);
Il2CppObject* il2cpp_methodinfo_get_return_type(Il2CppObject* memberName);
Il2CppObject* il2cpp_eventinfo_get_event_handler_type(Il2CppObject* memberName);
Il2CppObject* il2cpp_memberinfo_get_reflected_type(Il2CppObject* memberName);
int32_t il2cpp_fieldinfo_is_assembly(Il2CppObject* memberName);
// REFL6B (item C): FieldInfo/MethodBase.get_IsFamilyOrAssembly -- vis mask ==
// FamORAssem (0x5) EXACTLY (net48 oracle6d: protected-internal True; plain
// protected(0x4) / internal(0x3) / public(0x6) False). The emitter bakes the
// state as the dedicated member-flag bit 0x40; pre-refl6b tables fall to 0.
int32_t il2cpp_memberinfo_is_family_or_assembly(Il2CppObject* memberName);
int32_t il2cpp_memberinfo_is_static(Il2CppObject* memberName);
int32_t il2cpp_memberinfo_is_public(Il2CppObject* memberName);
int32_t il2cpp_memberinfo_is_constructor(Il2CppObject* memberName);
// p-refl2-gapmatrix (B05/B07/B08/B11/B17/B18): the remaining member flag
// predicates + MemberInfo.MetadataToken. The receiver is the member-VALUE
// string (the simplified member model); a FIELD receiver joins through the
// field table, a METHOD receiver through the method table (the same
// own-then-base chain walk the existing member predicates run). Each returns
// the net48 bool as an int32 (0/1); a null receiver -> NullReferenceException.
//   il2cpp_memberinfo_get_metadata_token -- MemberInfo.MetadataToken: the
//     resolved member's transpiler-baked ECMA token (0x06000000 | MethodDef
//     row / 0x04000000 | FieldDef row; 0 for a synthesized entry). This is the
//     value net48 reports for a member of a translated type.
//   il2cpp_memberinfo_is_virtual   -- MethodBase.IsVirtual (flags bit3)
//   il2cpp_memberinfo_is_abstract  -- MethodBase.IsAbstract (flags bit7 0x80)
//   il2cpp_memberinfo_is_generic_method -- MethodInfo.IsGenericMethod (flags bit8 0x100)
//   il2cpp_fieldinfo_is_init_only  -- FieldInfo.IsInitOnly (flags bit7 0x80)
//   il2cpp_fieldinfo_is_literal    -- FieldInfo.IsLiteral (flags bit2)
int32_t il2cpp_memberinfo_get_metadata_token(Il2CppObject* memberName);
int32_t il2cpp_memberinfo_is_virtual(Il2CppObject* memberName);
int32_t il2cpp_memberinfo_is_abstract(Il2CppObject* memberName);
int32_t il2cpp_memberinfo_is_generic_method(Il2CppObject* memberName);
int32_t il2cpp_fieldinfo_is_init_only(Il2CppObject* memberName);
int32_t il2cpp_fieldinfo_is_literal(Il2CppObject* memberName);
// p-refl2-gapmatrix (E03/E04): MethodBase.MethodHandle / FieldInfo.FieldHandle
// -> the raw handle IntPtr. The receiver is the member-VALUE string; the
// handle VALUE is the resolved table entry address (const Il2CppMethodInfo* /
// const Il2CppFieldInfo*), matching reflecthandle.h's handle-value model. A
// null/unresolved member answers 0 (the default-handle contract). The probes
// only assert Value != IntPtr.Zero (a real handle is never zero).
intptr_t il2cpp_methodinfo_get_method_handle(Il2CppObject* memberName);
intptr_t il2cpp_fieldinfo_get_field_handle(Il2CppObject* memberName);

//------------------------------------------------------------------------------
// p-refl2-gapmatrix (C04/C05/C06/C09) -- the Assembly / AssemblyName surface.
//
// The simplified Assembly model represents an Assembly VALUE as its SIMPLE
// NAME managed string (the same convention as a Type VALUE being its full-name
// string); an AssemblyName VALUE is the assembly's simple-name string too.
// Assembly.GetName() is therefore an identity (the transpiler folds it to the
// receiver), and AssemblyName.get_Name is likewise identity. The INPUT
// assembly's simple name is baked by the transpiler and passed as the trailing
// literal so the runtime can distinguish the input assembly (its reflected
// types live in the typeinfo registry) from a BCL assembly (mscorlib has no
// reflected types -- the runtime's class registry is the identity store).
//   il2cpp_assemblyinfo_get_type(asmName, typeName, inputAsmName) --
//     Assembly.GetType(string): only the INPUT assembly carries reflected
//     types; a BCL assembly answers nullptr for every name (net48: a type of
//     another assembly is not returned). The input assembly delegates to
//     il2cpp_type_get_type (the registry query).
//   il2cpp_assemblyinfo_get_types(asmName, inputAsmName) --
//     Assembly.GetTypes(): the INPUT assembly's typeinfo registry, a BCL
//     assembly's class registry. Returns a Type[] (name strings).
//   il2cpp_assemblyname_get_full_name(name) -- AssemblyName.get_FullName /
//     Assembly.get_FullName: a non-null net48-shaped display name (the AOT
//     model does not track versions/tokens -- the neutral placeholder body).
// A null asmName keeps the net48 NullReferenceException on the handle getters.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_assemblyname_get_full_name(Il2CppObject* name);
Il2CppObject* il2cpp_assemblyinfo_get_type(Il2CppObject* asmName, Il2CppObject* typeName,
                                           Il2CppObject* inputAsmName);
// REFL-ARITY: Assembly.GetType(string, bool throwOnError) -- the 2-param form.
// Same input-assembly scoping as il2cpp_assemblyinfo_get_type; on a well-formed
// but unregistered name with throwOnError != 0 the runtime throws
// TypeLoadException (net48), otherwise nullptr (net48's not-found -> null).
Il2CppObject* il2cpp_assemblyinfo_get_type_ex(Il2CppObject* asmName, Il2CppObject* typeName,
                                              int32_t throwOnError, Il2CppObject* inputAsmName);
Il2CppArray* il2cpp_assemblyinfo_get_types(Il2CppObject* asmName, Il2CppObject* inputAsmName);

//------------------------------------------------------------------------------
// p-refl2-gapmatrix (E01/E02) -- Type.get_TypeHandle on the name-string (Type
// VALUE) receiver. The handle VALUE is the type descriptor's ADDRESS as an
// intptr_t (reflecthandle.h's value model: RuntimeTypeHandle == Il2CppClass*),
// matching the System.IntPtr slot the transpiler gives the handle type. A null
// receiver -> NullReferenceException (the net48 null-Type doctrine); a name the
// runtime cannot resolve answers 0 (the default-handle contract). The consumer
// Type.GetTypeFromHandle maps to il2cpp_type_get_type_from_handle_value.
//------------------------------------------------------------------------------
intptr_t il2cpp_typeinfo_get_type_handle(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// refl2 (A) -- the remaining System.Type predicate / query surface on the
// name-string (Type VALUE) receiver. The FOLDED typeof chains answer these at
// transpile time (StackFold); these icalls serve a RUNTIME receiver (a
// GetType() result) and keep the net48 contract:
//   * il2cpp_typeinfo_get_array_rank -- Type.GetArrayRank. A NON-array receiver
//     throws ArgumentException (net48 "Type must be an array"); a rank-1 array
//     answers 1, an N-dim array its rank (the array CLASS name carries the rank
//     as the trailing-comma count).
//   * il2cpp_typeinfo_is_constructed_generic -- Type.IsConstructedGenericType:
//     the receiver denotes a CLOSED generic instance. The simplified model's
//     registry key is the DEFINITION name, so a runtime receiver can never carry
//     the argument list -> constant 0 (net48 False for a definition; a closed
//     instance is only expressible through the folded typeof chain).
//   * il2cpp_typeinfo_is_abstract / _is_sealed / _is_public / _is_nested -- the
//     TypeAttributes predicates from the typeinfo typeFlags column.
//   * il2cpp_typeinfo_get_metadata_token -- Type.MetadataToken. The AOT model
//     has no ECMA metadata at runtime: a registered typeinfo answers the
//     transpiler-baked token (0 for an unregistered / BCL type, the documented
//     boundary).
//   * il2cpp_typeinfo_make_array_type / _make_byref_type / _make_pointer_type
//     -- the net48 display identity of the constructed type ("<elem>[]",
//     "<elem>&", "<elem>*"); a rank argument > 1 produces "<elem>[,]" form.
// Null receiver -> NullReferenceException (the null-Type doctrine).
//------------------------------------------------------------------------------
int32_t il2cpp_typeinfo_get_array_rank(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_constructed_generic(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_abstract(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_sealed(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_public(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_nested(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_get_metadata_token(Il2CppObject* typeName);
Il2CppObject* il2cpp_typeinfo_make_array_type(Il2CppObject* typeName, int32_t rank);
Il2CppObject* il2cpp_typeinfo_make_byref_type(Il2CppObject* typeName);
Il2CppObject* il2cpp_typeinfo_make_pointer_type(Il2CppObject* typeName);
// refl2 (A): Type.get_Attributes -- the RAW ECMA TypeAttributes int32 of the
// receiver (a registered typeinfo answers its baked TypeDef.Attributes; an
// unregistered / BCL name answers the BCL core table's approximation, else 0).
int32_t il2cpp_typeinfo_get_attributes(Il2CppObject* typeName);
// refl2 (A): the exact nested-visibility predicates Type.IsNestedPublic /
// IsNestedPrivate / IsNestedFamily / IsNestedAssembly / IsNestedFamANDAssem /
// IsNestedFamORAssem -- TypeAttributes & 0x7 compared to the exact ECMA value
// (2/3/4/5/6/7), plus Type.IsVisible (public OR nested-public and every
// enclosing type visible -- the simplified model answers public || nestedPublic).
int32_t il2cpp_typeinfo_is_nested_public(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_nested_private(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_is_visible(Il2CppObject* typeName);
// refl2 (A): Type.Equals(object) for a NON-Type argument -- net48 answers
// False unconditionally (never an exception). A null receiver keeps the
// net48 NullReferenceException. The argument is ignored (it is never a Type in
// the simplified model unless the fold already routed it to icall_type_equals).
int32_t il2cpp_typeinfo_equals_non_type(Il2CppObject* typeName, Il2CppObject* other);

} // namespace il2cpp
