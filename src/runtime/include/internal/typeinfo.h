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
const Il2CppEventInfo*    il2cpp_type_get_event(const Il2CppTypeInfo* info, const char* name);

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
// D (audit A3/observation 3): Type.BaseType on a Type VALUE (full-name
// string). Resolves the typeinfo registry and returns the BASE type's full
// name string (the simplified Type model) on a hit -- the resolved user base
// (baseType pointer -> its name) or the BCL terminal base
// (baseTypeName field). System.Object roots / unregistered / malformed ->
// nullptr. Null typeName -> NullReferenceException (net48: ((Type)null)
// .BaseType throws NRE).
Il2CppObject* il2cpp_typeinfo_get_base_type(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// RF-N4 -- generic-type identification icalls (NEXT2-DECISION section ② RF-N4,
// 裁决①三分落转译端). Each takes the Type VALUE (full-name string, the same
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
//        Names B方案 is deferred -- no argument fabrication, ever). The
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
int32_t il2cpp_typeinfo_is_generic_type(Il2CppObject* typeName);
int32_t il2cpp_typeinfo_get_generic_arity(Il2CppObject* typeName);
Il2CppArray* il2cpp_typeinfo_get_generic_arguments(Il2CppObject* typeName);
Il2CppObject* il2cpp_typeinfo_get_generic_type_definition(Il2CppObject* typeName);

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
// RF-N7 (NEXT2-DECISION section ② RF-N7) -- the reflection operations that
// have NO AOT implementation and MUST fail loudly instead of falling to a
// spelling generated-member name (the pre-RF-N7 behavior: an unwired BCL
// member fell to the generated-name path, a phantom symbol that never linked):
//
//   il2cpp_makegenericmethod_loud(memberName, typeArgs) -- MethodInfo.
//       MakeGenericMethod(Type[]). Runtime generic-method instantiation has
//       no AOT support -> throws NotSupportedException unconditionally.
//   il2cpp_makegenerictype_loud(typeName, typeArgs)     -- Type.MakeGenericType
//       (Type[]). Same: no fabricated generic arguments (禁伪造) -> throws
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

} // namespace il2cpp
