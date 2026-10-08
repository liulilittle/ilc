//------------------------------------------------------------------------------
// typeinfo.cpp -- runtime reflection metadata registry + member queries
// (see typeinfo.h for the contract).
//
// Two halves:
//   * Registry: a fixed-size static array of Il2CppTypeInfo pointers keyed by
//     full type name (string key -> table), mirroring metadata.cpp's class
//     registry exactly (same storage constraint #12: static storage,
//     zero-initialized, never allocates; overflow aborts loudly). Generated
//     startup code registers one entry per reflected type via
//     il2cpp_typeinfo_register.
//   * Queries: il2cpp_type_get_property/get_method/get_field scan the type's
//     own table then walk the baseType chain (inherited members are returned
//     by .NET reflection), returning the first exact-name match
//     (most-derived declaration wins, .NET shadowing semantics).
//
// All tables are static const read-only data; this translation unit holds
// only the mutable registry and performs no allocation.
//------------------------------------------------------------------------------

#include "typeinfo.h"

#include "attribute.h"          // RF-N6 member-attribute registry (lookup_member)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception / _null_reference
#include "il2cpp_string.h"        // Il2CppString (managed-string ABI queries)
#include "il2cpp_runtime.h"       // il2cpp_box_int32 / il2cpp_unbox_int32 (R2 field values)
#include "il2cpp_boxing_ext2.h"   // runtime::icall_box_long / icall_unbox_long (R2 field values)
#include "il2cpp_boxing_ext.h"    // runtime::icall_unbox_double / icall_box_double (R3 invoke)
#include "invoker.h"              // il2cpp_invoker_invoke / slots (R3 MethodInfo.Invoke)

// C3-2: Marshal.IsComObject (klass == the RCW descriptor) -- declared in
// il2cpp_com.h under il2cpp::runtime; forward-declared inside the namespace
// so this translation unit (which houses the member icalls, not the COM heap)
// can classify an AddEventHandler target without pulling the COM ABIs.
namespace il2cpp {
namespace runtime {
bool icall_com_is_com_object(Il2CppObject* obj);
}
}

#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {

namespace {

// Upper bound on distinct reflected types a program may register (mirrors
// kMaxRegisteredClasses in metadata.cpp). Exceeded only if the transpiler
// emits more type infos than this constant allows.
constexpr int kMaxRegisteredTypeInfos = 256;

// The registry itself: a flat pointer table + running count. Zero-initialized
// static storage (constraint #12), appended to in registration order.
const Il2CppTypeInfo* g_registeredTypeInfos[kMaxRegisteredTypeInfos];
int g_registeredTypeInfoCount = 0;

//------------------------------------------------------------------------------
// ThrowNullTypeReceiver -- the .NET null-RECEIVER failure for the reflection
// queries: calling GetProperty/GetMethod/GetField on a null Type throws
// NullReferenceException (net48, probe-verified: ((Type)null).GetMethod("x")
// -> NullReferenceException). Mirrors the typecheck.cpp instance-method null
// contract; the chain suffix (",System.Exception") lets
// il2cpp_exception_matches accept a catch of System.Exception.
//------------------------------------------------------------------------------
void ThrowNullTypeReceiver()
{
    il2cpp_throw_null_reference_exception(
        "Object reference not set to an instance of an object.");
}

//------------------------------------------------------------------------------
// ThrowNullMemberName -- the .NET null-ARGUMENT failure for the reflection
// queries: GetMethod(null)/GetProperty(null) throw ArgumentNullException with
// paramName "name" (net48, probe-verified); GetField(null) throws
// ArgumentNullException with an EMPTY paramName (probe-verified) -- the caller
// passes nullptr there (the factory then uses the plain default text).
//------------------------------------------------------------------------------
void ThrowNullMemberName(const char* paramName)
{
    il2cpp_throw_argument_null_exception(paramName);
}

//------------------------------------------------------------------------------
// FindMember -- scan one member table for an exact name match.
//
// Shared by the three query functions (their tables differ only in entry
// type, so a single template keeps the scan logic in one place -- DRY).
// Returns nullptr when the table is null/empty or the name is unknown.
// (Null inputs cannot reach this helper -- the query functions throw first.)
//------------------------------------------------------------------------------
template <typename T>
const T* FindMember(const T* table, int32_t count, const char* name)
{
    if (table == nullptr || count <= 0 || name == nullptr)
    {
        return nullptr;
    }
    for (int32_t i = 0; i < count; ++i)
    {
        if (table[i].name != nullptr && std::strcmp(table[i].name, name) == 0)
        {
            return &table[i];
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// FindMemberThroughBaseChain -- own table first, then walk the baseType chain
// (inherited members live under THEIR declaring type's table, design 2.1).
//------------------------------------------------------------------------------
template <typename T>
const T* FindMemberThroughBaseChain(const Il2CppTypeInfo* info,
                                    const char* name,
                                    const T* Il2CppTypeInfo::*table,
                                    int32_t Il2CppTypeInfo::*count)
{
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        const T* found = FindMember(scan->*table, scan->*count, name);
        if (found != nullptr)
        {
            return found;
        }
    }
    return nullptr;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_typeinfo_register -- add a type info table to the registry.
//
//   Idempotent: registering the same table twice is a no-op (duplicate
//   pointers are skipped, keeping the table free of duplicates). Null input
//   is a caller bug and is ignored. Overflow is a programming error surfaced
//   as abort() -- never silent corruption.
//------------------------------------------------------------------------------
void il2cpp_typeinfo_register(const Il2CppTypeInfo* info)
{
    if (info == nullptr)
    {
        return;
    }
    for (int i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        if (g_registeredTypeInfos[i] == info)
        {
            return;
        }
    }
    if (g_registeredTypeInfoCount >= kMaxRegisteredTypeInfos)
    {
        std::fprintf(stderr, "il2cpp_typeinfo_register: registry overflow (%d type infos)\n",
                     kMaxRegisteredTypeInfos);
        std::abort();
    }
    g_registeredTypeInfos[g_registeredTypeInfoCount] = info;
    ++g_registeredTypeInfoCount;
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_lookup -- linear search for a type info by full type name.
//
//   Names are compared as raw C strings; the transpiler emits identical name
//   literals for the same type in every translation unit, so pointer/contents
//   equality agree. Returns nullptr when the name is unknown or the input is
//   null -- callers must treat null as "type not reflected".
//------------------------------------------------------------------------------
const Il2CppTypeInfo* il2cpp_typeinfo_lookup(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    for (int i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info->name != nullptr && std::strcmp(info->name, name) == 0)
        {
            return info;
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_lookup_by_class -- join a class descriptor to its typeinfo
// by the registered klass POINTER (P25c). The name join is broken for nested /
// compiler-generated types: the typeinfo registers the full "+"-chained
// reflection name ("Cases.YieldProbe+<Range>d__0") while the class descriptor
// carries the SHORT name ("<Range>d__0"). The typeinfo struct already holds
// the klass back-pointer (emitted by the transpiler), so a pointer scan is
// exact, order-independent and immune to name-format drift. Returns nullptr
// when the class is not reflected (BCL / boxed / snapshot runtime objects).
//------------------------------------------------------------------------------
const Il2CppTypeInfo* il2cpp_typeinfo_lookup_by_class(const Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        return nullptr;
    }
    for (int i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info->klass == klass)
        {
            return info;
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_property -- simplified Type.GetProperty(name) primitive.
//
// net48 (probe-verified): a null info (null Type receiver) throws
// NullReferenceException; a null name throws ArgumentNullException (param
// "name"). "Member not found" -> nullptr (no throw).
//------------------------------------------------------------------------------
const Il2CppPropertyInfo* il2cpp_type_get_property(const Il2CppTypeInfo* info, const char* name)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName("name");
    }
    return FindMemberThroughBaseChain(info, name,
        &Il2CppTypeInfo::properties, &Il2CppTypeInfo::propertyCount);
}

//------------------------------------------------------------------------------
// il2cpp_type_get_method -- simplified Type.GetMethod(name) primitive.
//
// net48 (probe-verified): a null info (null Type receiver) throws
// NullReferenceException; a null name throws ArgumentNullException (param
// "name"). "Member not found" -> nullptr (no throw).
//------------------------------------------------------------------------------
const Il2CppMethodInfo* il2cpp_type_get_method(const Il2CppTypeInfo* info, const char* name)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName("name");
    }
    return FindMemberThroughBaseChain(info, name,
        &Il2CppTypeInfo::methods, &Il2CppTypeInfo::methodCount);
}

//------------------------------------------------------------------------------
// il2cpp_type_get_field -- simplified Type.GetField(name) primitive.
//
// net48 (probe-verified): a null info (null Type receiver) throws
// NullReferenceException; a null name throws ArgumentNullException with an
// EMPTY paramName (the net48 GetField(null) probe shows ParamName == "" --
// unlike GetMethod/GetProperty which use "name"). "Member not found" ->
// nullptr (no throw).
//------------------------------------------------------------------------------
const Il2CppFieldInfo* il2cpp_type_get_field(const Il2CppTypeInfo* info, const char* name)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName(nullptr); // net48: GetField(null) ParamName is empty
    }
    // raudit3: net48 GetField(string) binds Public|Instance|Static by default,
    // so a PRIVATE field is invisible (typeof(D).GetField("hidden") == null).
    // Il2CppFieldInfo.flags bit0 = public (typeinfo.h). Inherited members live
    // under their declaring type's table -- walk the baseType chain; a private
    // hit is SKIPPED and the scan continues (a public base field of the same
    // name is still found, matching net48).
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->fields == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < scan->fieldCount; ++i)
        {
            const Il2CppFieldInfo& field = scan->fields[i];
            if (field.name != nullptr && std::strcmp(field.name, name) == 0
                && (field.flags & 1) != 0)
            {
                return &field;
            }
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_event -- simplified Type.GetEvent(name) primitive
// (C3-1 / RF-N1; the COM-C3 event sub-table shares the Il2CppPropertyInfo
// query shape). net48: a null info (null Type receiver) throws
// NullReferenceException; a null name throws ArgumentNullException (param
// "name"). "Event not found" -> nullptr (no throw). The events table has no
// flags field (all events are enumerated -- the documented simplification),
// so this is the plain FindMemberThroughBaseChain walk.
//------------------------------------------------------------------------------
const Il2CppEventInfo* il2cpp_type_get_event(const Il2CppTypeInfo* info, const char* name)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName("name");
    }
    return FindMemberThroughBaseChain(info, name,
        &Il2CppTypeInfo::events, &Il2CppTypeInfo::eventCount);
}

//------------------------------------------------------------------------------
// AsciiFromManagedString -- UTF-16 managed string -> ASCII C-string buffer.
//
// Type and member names are ASCII by project convention (generated code is
// pure ASCII, and the transpiler emits ASCII name literals), so the
// transcoding is a plain per-unit truncation with a non-ASCII escape (the
// string is treated as "not found" rather than mis-compared). Buffer size
// 256 covers every realistic full type name + member name; an over-long
// input also escapes to "not found".
//------------------------------------------------------------------------------
namespace {

constexpr int kNameBufferSize = 256;

bool AsciiFromManagedString(Il2CppObject* name, char (&buf)[kNameBufferSize])
{
    if (name == nullptr)
    {
        return false;
    }
    auto s = reinterpret_cast<Il2CppString*>(name);
    if (s->length < 0 || s->length >= kNameBufferSize)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] > 0x7F)
        {
            return false; // non-ASCII: out of the project convention
        }
        buf[i] = static_cast<char>(s->chars[i]);
    }
    buf[s->length] = '\0';
    return true;
}

} // namespace

//------------------------------------------------------------------------------
// QueryManagedString -- shared body of the three managed-string queries:
//   typeName -> registry lookup -> member query (own + base chain).
//
// net48 exception contract (probe-verified; the same layer the raw
// primitives enforce):
//   typeName == nullptr  -> NullReferenceException (null Type receiver --
//                           ((Type)null).GetMethod(x) throws NRE)
//   memberName == nullptr-> ArgumentNullException (param 'paramName' -- "name"
//                           for GetMethod/GetProperty, empty for GetField)
//   otherwise            -> the member-name managed string (the input object
//                           itself, identity -- the simplified member model)
//                           on a hit, or nullptr (not registered / not found
//                           / malformed input -- non-ASCII/over-long names
//                           escape to "not found", matching net48
//                           GetMethod(300-char) -> null). No allocation on any
//                           path.
//------------------------------------------------------------------------------
template <typename QueryFn>
Il2CppObject* QueryManagedString(Il2CppObject* typeName, Il2CppObject* memberName,
                                 const char* paramName, QueryFn query)
{
    if (typeName == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (memberName == nullptr)
    {
        ThrowNullMemberName(paramName);
    }
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr)
    {
        return nullptr;
    }
    return query(info, memberBuf) != nullptr ? memberName : nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_property -- managed-string Type.GetProperty(name) icall.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_property(Il2CppObject* typeName, Il2CppObject* memberName)
{
    return QueryManagedString(typeName, memberName, "name",
        [](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_property(info, name);
        });
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_method -- managed-string Type.GetMethod(name) icall.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_method(Il2CppObject* typeName, Il2CppObject* memberName)
{
    return QueryManagedString(typeName, memberName, "name",
        [](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_method(info, name);
        });
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_field -- managed-string Type.GetField(name) icall.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_field(Il2CppObject* typeName, Il2CppObject* memberName)
{
    // net48: GetField(null) throws ArgumentNullException with an EMPTY
    // paramName (probe-verified), unlike GetMethod/GetProperty ("name").
    return QueryManagedString(typeName, memberName, nullptr,
        [](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_field(info, name);
        });
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_event -- managed-string Type.GetEvent(name) icall
// (C3-1 / RF-N1 -- the C3-2 AttachEventHandler receiver join).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_event(Il2CppObject* typeName, Il2CppObject* memberName)
{
    return QueryManagedString(typeName, memberName, "name",
        [](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_event(info, name);
        });
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_base_type -- managed-string Type.BaseType icall (D /
// audit A3 observation 3).
//
// The Type VALUE is the full-name string (the simplified Type model). The
// icall resolves the typeinfo registry and returns the base type's FULL-NAME
// string:
//   * a resolved USER base  -> info->baseType->name (e.g. "Cases.CustomEx"
//     for CustomMid : CustomEx -- the base typeinfo's name field).
//   * a BCL non-Object base -> info->baseTypeName (e.g. "System.Exception"
//     for CustomEx : Exception -- the emitter's BCL terminal name; the
//     baseType pointer is null there because BCL bases are not translated,
//     but net48 Type.BaseType is NON-null for this edge).
//   * System.Object root    -> nullptr (net48: typeof(object).BaseType == null;
//     a BCL type itself is not registered, so the lookup also yields null).
// Null typeName -> NullReferenceException (net48: ((Type)null).BaseType
// throws NRE -- the null-Type-receiver doctrine, same as the member queries).
// The RESULT is a Type VALUE (full-name string) -- get_Name downstream
// truncates to the short name, exactly like the member-query results.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_base_type(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    char typeBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr)
    {
        return nullptr;
    }
    const char* base = nullptr;
    if (info->baseType != nullptr)
    {
        base = info->baseType->name;
    }
    else if (info->baseTypeName != nullptr)
    {
        base = info->baseTypeName;
    }
    return base != nullptr
        ? reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(base))
        : nullptr;
}

//------------------------------------------------------------------------------
// C3-2 -- EventInfo.AddEventHandler / EventInfo.RemoveEventHandler.
//
// The receiver is the EVENT-NAME string (the simplified member model, as
// il2cpp_typeinfo_get_event returns); the declaring type is joined from the
// TARGET object's klass. A MANAGED target resolves the event through the
// events table (il2cpp_type_get_event), then dispatches the event's ADD /
// REMOVE ACCESSOR METHOD (Il2CppEventInfo.addName / removeName) on the target
// with (handler) through the reflection invoker -- the net48 managed-event
// delivery (the compiler's field-backed add_Tick combines/removes the
// delegate; Raise then reaches the connected handlers). This is the C3 genevt
// fake-source self-loop's managed delivery path.
//
// An RCW target (klass == the RCW descriptor) is the shape-A boundary: a
// Delegate handler cannot be bridged to the shape-B user event-interface
// sink object without the delegate-to-Invoke bridging, which is DEFERRED. The
// C3 connection-point COMBO (ccw_create + register_invoke_dispatch +
// get_connection_point + cp_advise / cp_unadvise) already runs at the raw
// icall level (the test_com C-section); this icall doors the RCW receiver with
// a clear LOUD gap instead of a silent wrong attach.
//------------------------------------------------------------------------------
namespace {

void ThrowEventInfoRcwNotSupported(const char* method)
{
    il2cpp_throw_new("System.NotSupportedException,System.Exception",
        "EventInfo::method on a COM RCW target via a Delegate handler is not wired in C3-2 (shape A -- the delegate-to-Invoke sink bridging is deferred; the COM connection-point combo runs at the raw icall level, see the test_com C-section)");
}

// Invoke the event accessor (addName/removeName) on a MANAGED target with the
// handler as the single argument (il2cpp_methodinfo_invoke dispatches the
// instance method with the target injected as 'this' and paramCount 1).
void InvokeEventAccessor(const Il2CppEventInfo* ev, bool add, Il2CppObject* target,
                         Il2CppObject* handler, const char* what)
{
    const char* accessor = add ? ev->addName : ev->removeName;
    if (accessor == nullptr)
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
            "the event has no accessor for EventInfo::what -- the events table lacks it");
    }
    Il2CppArray* args = il2cpp_array_new(nullptr, sizeof(void*), true, 1);
    *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(args, 0)) = handler;
    Il2CppObject* accessorStr = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(accessor));
    (void)il2cpp_methodinfo_invoke(accessorStr, target, args);
}

} // namespace

void il2cpp_eventinfo_add(Il2CppObject* eventName, Il2CppObject* target, Il2CppObject* handler)
{
    if (target == nullptr) il2cpp_throw_argument_null_exception("target");
    if (handler == nullptr) il2cpp_throw_argument_null_exception("handler");
    char nameBuf[kNameBufferSize];
    if (eventName == nullptr || !AsciiFromManagedString(eventName, nameBuf)) return;
    if (runtime::icall_com_is_com_object(target))
    {
        ThrowEventInfoRcwNotSupported("AddEventHandler");
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(target->klass);
    if (info == nullptr) return;
    const Il2CppEventInfo* ev = il2cpp_type_get_event(info, nameBuf);
    if (ev == nullptr) return; // net48: an EventInfo from another type on this target -> no-op gone
    InvokeEventAccessor(ev, true, target, handler, "AddEventHandler");
}

void il2cpp_eventinfo_remove(Il2CppObject* eventName, Il2CppObject* target, Il2CppObject* handler)
{
    if (target == nullptr) il2cpp_throw_argument_null_exception("target");
    if (handler == nullptr) il2cpp_throw_argument_null_exception("handler");
    char nameBuf[kNameBufferSize];
    if (eventName == nullptr || !AsciiFromManagedString(eventName, nameBuf)) return;
    if (runtime::icall_com_is_com_object(target))
    {
        ThrowEventInfoRcwNotSupported("RemoveEventHandler");
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(target->klass);
    if (info == nullptr) return;
    const Il2CppEventInfo* ev = il2cpp_type_get_event(info, nameBuf);
    if (ev == nullptr) return;
    InvokeEventAccessor(ev, false, target, handler, "RemoveEventHandler");
}

//------------------------------------------------------------------------------
// R2 (B2) member enumeration + FieldInfo value helpers.
//------------------------------------------------------------------------------
namespace {

// Empty reference-element array (net48: GetMethods/GetFields/... on a type
// without members, or an unreflected/malformed type, return an EMPTY array,
// never null).
Il2CppArray* EmptyStringArray()
{
    return il2cpp_array_new(nullptr, sizeof(void*), true, 0);
}

// Materialise a reference-element array whose elements are managed strings
// created from the given C names (the simplified member model: a member
// VALUE is its name string).
Il2CppArray* BuildNameArray(int32_t count)
{
    return il2cpp_array_new(nullptr, sizeof(void*), true, count);
}

void StoreName(Il2CppArray* arr, int32_t index, const char* name)
{
    *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, index)) =
        name != nullptr ? reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name)) : nullptr;
}

// net48 default-binding filter: public only (Il2CppMethodInfo/FieldInfo
// flags bit0 == public; GetMethods/GetFields/GetConstructors default to
// Public | Instance | Static).
bool IsPublic(int32_t flags)
{
    return (flags & 1) != 0;
}

// Enumerate own + base-chain tables into a name array, filtered by the
// predicate (methods/fields: public; constructors: public + ctor bit2;
// properties: all -- Il2CppPropertyInfo has no flags field, documented
// simplification).
template <typename T>
Il2CppArray* EnumerateMembers(const Il2CppTypeInfo* info, const T* Il2CppTypeInfo::*table,
                              int32_t Il2CppTypeInfo::*count, bool (*filter)(const T&))
{
    int32_t total = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        const T* entries = scan->*table;
        if (entries == nullptr) continue;
        for (int32_t i = 0; i < scan->*count; ++i)
        {
            if (filter(entries[i])) ++total;
        }
    }
    Il2CppArray* result = BuildNameArray(total);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        const T* entries = scan->*table;
        if (entries == nullptr) continue;
        for (int32_t i = 0; i < scan->*count; ++i)
        {
            if (filter(entries[i])) StoreName(result, dst++, entries[i].name);
        }
    }
    return result;
}

bool PublicMethod(const Il2CppMethodInfo& m)
{
    // Simplified model (committed R2 red-test pin, test_typeinfo.cpp section 7):
    // GetMethods enumerates PUBLIC methods INCLUDING ctors -- the separate
    // GetConstructors icall is the ctor-filtered view. (net48 GetMethods
    // excludes ctors; the simplified member model keeps them -- documented
    // deviation, pinned by the R2 enumeration probe.)
    return IsPublic(m.flags);
}
// RF-N5: GetMembers methods slice -- public NON-ctor methods (net48
// GetMembers' first group is every method member INCLUDING property/event
// accessors, excluding the constructors, which follow as their own group).
bool PublicNonCtorMethod(const Il2CppMethodInfo& m)
{
    return IsPublic(m.flags) && (m.flags & 4) == 0;
}
bool PublicCtor(const Il2CppMethodInfo& m) { return IsPublic(m.flags) && (m.flags & 4) != 0; }
bool PublicField(const Il2CppFieldInfo& f) { return IsPublic(f.flags); }
bool AnyProperty(const Il2CppPropertyInfo&) { return true; }
bool AnyEvent(const Il2CppEventInfo&) { return true; }

} // namespace

Il2CppArray* il2cpp_typeinfo_get_methods(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembers(info, &Il2CppTypeInfo::methods, &Il2CppTypeInfo::methodCount, PublicMethod);
}

Il2CppArray* il2cpp_typeinfo_get_fields(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembers(info, &Il2CppTypeInfo::fields, &Il2CppTypeInfo::fieldCount, PublicField);
}

Il2CppArray* il2cpp_typeinfo_get_properties(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembers(info, &Il2CppTypeInfo::properties, &Il2CppTypeInfo::propertyCount, AnyProperty);
}

Il2CppArray* il2cpp_typeinfo_get_events(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembers(info, &Il2CppTypeInfo::events, &Il2CppTypeInfo::eventCount, AnyEvent);
}

Il2CppArray* il2cpp_typeinfo_get_constructors(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembers(info, &Il2CppTypeInfo::methods, &Il2CppTypeInfo::methodCount, PublicCtor);
}

Il2CppArray* il2cpp_typeinfo_get_interfaces(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    // Own + base-chain interface lists (a derived type inherits its base's
    // interfaces, matching net48 GetInterfaces).
    int32_t total = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        total += scan->interfaceCount;
    }
    Il2CppArray* result = BuildNameArray(total);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        for (int32_t i = 0; i < scan->interfaceCount; ++i)
        {
            StoreName(result, dst++, scan->interfaces[i] != nullptr ? scan->interfaces[i]->name : nullptr);
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// RF-N5 -- GetNestedTypes / GetMembers (NEXT2-DECISION section 鈶?RF-N5).
//   GetNestedTypes: OWN nested types only, name strings in metadata
//     declaration order (the typeinfo's emitted nestedNames sub-table).
//   GetMembers: the five-enumeration union, own + base chain. Probe-pinned
//     net48 order (rf-members expected.txt): methods (public, INCLUDING the
//     property/event accessors -- net48 returns get_/set_/add_/remove_ as
//     method members, excluding .ctor/.cctor), then ctors (public), then
//     properties (all), then events (all), then fields (public).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_nested_types(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    Il2CppArray* result = BuildNameArray(info->nestedCount);
    for (int32_t i = 0; i < info->nestedCount; ++i)
    {
        StoreName(result, i, info->nestedNames != nullptr ? info->nestedNames[i] : nullptr);
    }
    return result;
}

Il2CppArray* il2cpp_typeinfo_get_members(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    // Count pass (five global slices, net48 probe-pinned):
    //   1. methods -- public non-ctor methods, own + base chain (net48
    //      returns PROPERTY/EVENT accessors as method members too, so
    //      get_/set_/add_/remove_ stay here -- rf-members oracle);
    //   2. ctors -- public ctors of the QUERIED TYPE ONLY (net48 never
    //      returns an inherited constructor);
    //   3. properties -- all, own + base chain;
    //   4. events -- all, own + base chain;
    //   5. fields -- public, own + base chain.
    int32_t methodN = 0, ctorN = 0, propN = 0, evtN = 0, fieldN = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (PublicNonCtorMethod(scan->methods[i])) ++methodN;
        propN += scan->propertyCount;
        evtN += scan->eventCount;
        if (scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (PublicField(scan->fields[i])) ++fieldN;
    }
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (PublicCtor(info->methods[i])) ++ctorN;
    Il2CppArray* result = BuildNameArray(methodN + ctorN + propN + evtN + fieldN);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (PublicNonCtorMethod(scan->methods[i])) StoreName(result, dst++, scan->methods[i].name);
    }
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (PublicCtor(info->methods[i])) StoreName(result, dst++, info->methods[i].name);
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                StoreName(result, dst++, scan->properties[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                StoreName(result, dst++, scan->events[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (PublicField(scan->fields[i])) StoreName(result, dst++, scan->fields[i].name);
    }
    return result;
}

//------------------------------------------------------------------------------
// RF-N4 -- generic-type identification icalls (typeinfo.h managed-string ABI).
// The receiver is the Type VALUE (full-name string); each reads the typeinfo's
// genericArity. A generic DEFINITION and its closed CLONES both register under
// the definition's name key (TypeEmitter.ReflectionFullName) with the SAME
// arity, so IsGenericType answers True for typeof(Box<>) and typeof(Box<int>)
// alike; the runtime NEVER distinguishes a definition from its clones
// (IsGenericTypeDefinition is the transpiler's fold-time decision).
//------------------------------------------------------------------------------
int32_t il2cpp_typeinfo_is_generic_type(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    // An unregistered type (BCL / boxed / snapshot) is treated as NON-generic
    // (net48 typeof(int).IsGenericType == False).
    return (info != nullptr && info->genericArity > 0) ? 1 : 0;
}

int32_t il2cpp_typeinfo_get_generic_arity(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    return info != nullptr ? info->genericArity : 0;
}

Il2CppArray* il2cpp_typeinfo_get_generic_arguments(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr || info->genericArity <= 0)
    {
        // Non-generic / unregistered: net48 returns an EMPTY array.
        return EmptyStringArray();
    }
    // genericArity > 0: the genericArgNames B鏂规 is deferred (RF-N4 瑁佸喅鈶? --
    // the runtime cannot fabricate the closed argument types (the folded name
    // of an instantiation collapses to the definition's name), so this path
    // is a LOUD gap, never a silent wrong result.
    il2cpp_throw_not_supported_exception(
        "Type.GetGenericArguments() on a generic type is not wired in RF-N4 (the genericArgNames B鏂规 is deferred -- no fabricated argument list; only the non-generic empty-array path is supported)");
    return EmptyStringArray(); // unreachable -- the throw does not return
}

Il2CppObject* il2cpp_typeinfo_get_generic_type_definition(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf))
    {
        // Unknown receiver: treat as non-generic (net48 typeof(int)
        // GetGenericTypeDefinition() throws InvalidOperationException).
        il2cpp_throw_invalid_operation_exception(
            "This operation is only valid on generic types.");
        return nullptr; // unreachable -- the throw does not return
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr || info->genericArity <= 0)
    {
        // Non-generic / unregistered: net48 typeof(int).GetGenericTypeDefinition()
        // throws InvalidOperationException.
        il2cpp_throw_invalid_operation_exception(
            "This operation is only valid on generic types.");
        return nullptr; // unreachable -- the throw does not return
    }
    // genericArity > 0: the DEFINITION'S OWN NAME -- identity. The folded name
    // of a closed instantiation IS the definition's full name and a runtime
    // GetType() of a generic object reads klass->name (also the definition's
    // reflection name), so the receiver itself is the definition.
    return typeName;
}

//------------------------------------------------------------------------------
// FindPublicFieldThroughBaseChain -- own table first, then the baseType chain
// (inherited fields live under THEIR declaring type's table), public only
// (the net48 GetField / FieldInfo.GetValue default binding).
//------------------------------------------------------------------------------
namespace {
const Il2CppFieldInfo* FindPublicFieldThroughBaseChain(const Il2CppTypeInfo* info, const char* name)
{
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->fields == nullptr) continue;
        for (int32_t i = 0; i < scan->fieldCount; ++i)
        {
            const Il2CppFieldInfo& f = scan->fields[i];
            if (f.name != nullptr && std::strcmp(f.name, name) == 0 && PublicField(f))
            {
                return &f;
            }
        }
    }
    return nullptr;
}
} // namespace

Il2CppObject* il2cpp_typeinfo_field_get_value(Il2CppObject* fieldName, Il2CppObject* target)
{
    if (target == nullptr)
    {
        ThrowNullTypeReceiver(); // net48 instance-field doctrine: null obj -> NRE
    }
    char nameBuf[kNameBufferSize];
    if (fieldName == nullptr || !AsciiFromManagedString(fieldName, nameBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(target->klass);
    if (info == nullptr)
    {
        return nullptr;
    }
    const Il2CppFieldInfo* field = FindPublicFieldThroughBaseChain(info, nameBuf);
    if (field == nullptr)
    {
        return nullptr;
    }
    char* slot = field->offset >= 0
        ? reinterpret_cast<char*>(target) + field->offset
        : static_cast<char*>(field->staticAddr);
    // Narrow set: slot width dispatched by the field's typeName.
    if (field->typeName != nullptr && std::strcmp(field->typeName, "System.Int32") == 0)
    {
        return il2cpp_box_int32(nullptr, *reinterpret_cast<int32_t*>(slot));
    }
    if (field->typeName != nullptr && std::strcmp(field->typeName, "System.Int64") == 0)
    {
        return runtime::icall_box_long(*reinterpret_cast<int64_t*>(slot));
    }
    if (field->isRef)
    {
        return *reinterpret_cast<Il2CppObject**>(slot);
    }
    return nullptr; // unsupported value-type slot -> defensive null
}

void il2cpp_typeinfo_field_set_value(Il2CppObject* fieldName, Il2CppObject* target,
                                     Il2CppObject* value)
{
    if (target == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    char nameBuf[kNameBufferSize];
    if (fieldName == nullptr || !AsciiFromManagedString(fieldName, nameBuf))
    {
        return;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(target->klass);
    if (info == nullptr)
    {
        return;
    }
    const Il2CppFieldInfo* field = FindPublicFieldThroughBaseChain(info, nameBuf);
    if (field == nullptr)
    {
        return;
    }
    char* slot = field->offset >= 0
        ? reinterpret_cast<char*>(target) + field->offset
        : static_cast<char*>(field->staticAddr);
    if (field->typeName != nullptr && std::strcmp(field->typeName, "System.Int32") == 0)
    {
        *reinterpret_cast<int32_t*>(slot) = il2cpp_unbox_int32(value);
    }
    else if (field->typeName != nullptr && std::strcmp(field->typeName, "System.Int64") == 0)
    {
        *reinterpret_cast<int64_t*>(slot) = runtime::icall_unbox_long(value);
    }
    else if (field->isRef)
    {
        *reinterpret_cast<Il2CppObject**>(slot) = value;
    }
}

//------------------------------------------------------------------------------
// R3 (B4): MethodInfo.Invoke -- il2cpp_methodinfo_invoke.
//
// Two signature encodings are accepted for Il2CppMethodInfo.signature:
//   * the TRANSPILER format "RET(PARAMS)" -- EncodeReturnType(RET) + '(' +
//     comma-joined MethodSignatureCoder ParamTypes (TypeEmitter.cs:1249),
//     e.g. "i(i,i)", "v()", "Cases.Calc(i)". RET is mapped to the invoker's
//     uppercase return code (r:* and any full-name return -> reference).
//   * the INVOKER format "RET[:PARAMS]" -- the committed R3 red-test shape
//     ("I:i,i"), passed through unchanged.
// The param encodings are identical in both formats (the MethodSignatureCoder
// param codes are what the invoker's ParamKind consumes), so the built
// invoker signature is "RETCODE[:PARAMS]"/"RETCODE".
//
// argbuf: one slot per object[] element, unboxed by the parameter's slot kind
// (int32/int64/double families through the box carriers, ref as the object
// itself). RF-1: an INSTANCE method (flags static bit clear) has the target
// injected as the FIRST reference slot ('this', slot 0) and the dispatch
// signature gains a leading "r" parameter; a STATIC method (free function)
// gets NO 'this' slot -- the object[] maps 1:1 to the signature params, the
// R3 committed shape. No new table fields: the static bit on Il2CppMethodInfo
// is the classifier (zero schema change, only the argbuf/arity adapt).
//------------------------------------------------------------------------------
namespace {

// Transpiler RET encoding -> invoker uppercase return code (or '?' unknown).
char InvokeReturnCode(const char* ret)
{
    if (ret[0] == 'v' && ret[1] == '\0') return 'V';
    if (ret[0] == 'i' && ret[1] == '\0') return 'I';
    if (ret[0] == 'n' && ret[1] == '\0') return 'I';
    if ((ret[0] == 'l' || ret[0] == 'q') && ret[1] == '\0') return 'L';
    if ((ret[0] == 'd' || ret[0] == 'f') && ret[1] == '\0') return 'D';
    // "r:FullName" and any full-name return type -> reference slot.
    return 'R';
}

} // namespace

Il2CppObject* il2cpp_methodinfo_invoke(Il2CppObject* memberName, Il2CppObject* target,
                                       Il2CppArray* args)
{
    if (target == nullptr)
    {
        ThrowNullTypeReceiver(); // net48 instance doctrine: null obj -> NRE
    }
    char nameBuf[kNameBufferSize];
    if (memberName == nullptr || !AsciiFromManagedString(memberName, nameBuf))
    {
        return nullptr;
    }
    // Join the declaring type: the target may be passed as a CLASS DESCRIPTOR
    // (the committed R3 red-test shape -- &{T}_class) or as an instance
    // (target->klass).
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(reinterpret_cast<Il2CppClass*>(target));
    if (info == nullptr && target->klass != nullptr)
    {
        info = il2cpp_typeinfo_lookup_by_class(target->klass);
    }
    if (info == nullptr)
    {
        return nullptr;
    }
    const int32_t paramCount = (args != nullptr && args->length > 0) ? args->length : 0;
    if (paramCount > kInvokerMaxParams)
    {
        return nullptr;
    }
    // A method with this NAME that is trimmed (fnPtr null -- runtime-
    // unreachable AOT function) is LOUD, REGARDLESS of the requested arity
    // (the committed R3 red-test shape: Trimmed(name, wrong-arity args) still
    // throws NotSupportedException).
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name != nullptr && std::strcmp(m.name, nameBuf) == 0 && m.fnPtr == nullptr)
            {
                il2cpp_throw_new("System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception",
                                 "Il2CppMethodInfo::Invoke: the method has no runtime-unreachable AOT function (trimmed) -- reflection Invoke is unsupported for it.");
            }
        }
    }
    // Resolve by name + paramCount (overload disambiguation).
    const Il2CppMethodInfo* found = nullptr;
    int32_t matches = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name == nullptr || std::strcmp(m.name, nameBuf) != 0) continue;
            if (m.paramCount != paramCount) continue;
            found = &m;
            ++matches;
        }
    }
    if (matches == 0)
    {
        return nullptr; // not found (defensive query convention)
    }
    if (matches > 1)
    {
        il2cpp_throw_new("System.Reflection.AmbiguousMatchException,System.Reflection.AmbiguousMatchException,System.SystemException,System.Exception",
                         "Il2CppMethodInfo::Invoke: multiple methods match the name and parameter count -- ambiguous.");
    }
    if (found->signature == nullptr)
    {
        return nullptr;
    }
    // Build the invoker signature string.
    char invokerSig[80];
    {
        const char* sig = found->signature;
        const char* open = std::strchr(sig, '(');
        if (open != nullptr)
        {
            // Transpiler format "RET(PARAMS)": map the return, copy the params.
            char retBuf[48];
            size_t retLen = static_cast<size_t>(open - sig);
            if (retLen >= sizeof(retBuf)) retLen = sizeof(retBuf) - 1;
            std::memcpy(retBuf, sig, retLen);
            retBuf[retLen] = '\0';
            const char* close = std::strchr(open, ')');
            const char* params = open + 1;
            size_t paramLen = (close != nullptr) ? static_cast<size_t>(close - params) : 0;
            char code = InvokeReturnCode(retBuf);
            int32_t pos = 0;
            invokerSig[pos++] = code;
            if (paramLen > 0)
            {
                invokerSig[pos++] = ':';
                size_t copy = paramLen < sizeof(invokerSig) - 2 ? paramLen : sizeof(invokerSig) - 2;
                std::memcpy(invokerSig + pos, params, copy);
                pos += static_cast<int32_t>(copy);
            }
            invokerSig[pos] = '\0';
        }
        else
        {
            // Invoker format passed through unchanged (committed R3 probe shape).
            size_t len = std::strlen(sig);
            size_t copy = len < sizeof(invokerSig) - 1 ? len : sizeof(invokerSig) - 1;
            std::memcpy(invokerSig, sig, copy);
            invokerSig[copy] = '\0';
        }
    }
    // RF-1: an INSTANCE method (static bit clear) gets the target injected as
    // the 'this' reference slot at argbuf index 0 -- the transpiler emits
    // instance methods with arg0 = this (RootSlotPass), so the silently
    // missing 'this' made every reflected instance Invoke read garbage state.
    // A STATIC method (free function) keeps the R3 no-this shape: the object[]
    // maps 1:1 to the signature params. argCount is the full slot count the
    // invoker dispatches on (explicit params + the injected 'this').
    const bool isInstance = (found->flags & 2) == 0;
    const int32_t argCount = paramCount + (isInstance ? 1 : 0);
    if (argCount > kInvokerMaxParams)
    {
        return nullptr;
    }
    // Build the argbuf: one slot per object[] element, unboxed by slot kind.
    il2cpp_invoker_slot slots[kInvokerMaxParams];
    {
        // Verify the signature param count agrees with the object[] length
        // (the invoker re-checks arity; the local count keeps the kind loop aligned).
        const char* colon = std::strchr(invokerSig, ':');
        const char* p = colon != nullptr ? colon + 1 : nullptr;
        int32_t parsedParams = 0;
        if (p != nullptr && *p != '\0')
        {
            for (;;)
            {
                const char* comma = std::strchr(p, ',');
                parsedParams++;
                p = comma != nullptr ? comma + 1 : nullptr;
                if (comma == nullptr || p == nullptr || *p == '\0') break;
            }
        }
        if (parsedParams != paramCount)
        {
            return nullptr; // arity mismatch between the table and the object[]
        }
        // Instance methods put the target in the 'this' slot (index 0); the
        // explicit object[] params then start at index 1 (index 0 for static).
        const int32_t explicitBase = isInstance ? 1 : 0;
        if (isInstance)
        {
            slots[0].ptr = target;
        }
        p = colon != nullptr ? colon + 1 : nullptr;
        for (int32_t i = 0; i < paramCount; ++i)
        {
            const char* comma = p != nullptr ? std::strchr(p, ',') : nullptr;
            // The codes are first-character-distinct (invoker.cpp ParamKind).
            char kindChar = (p != nullptr && *p != '\0' && (comma == nullptr || comma > p)) ? *p : '\0';
            Il2CppObject* boxed = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(args, i));
            switch (kindChar)
            {
            case 'i': case 'b': case 'c': case 'u': case 'n':
                slots[i + explicitBase].i32 = il2cpp_unbox_int32(boxed);
                break;
            case 'l': case 'q':
                slots[i + explicitBase].i64 = runtime::icall_unbox_long(boxed);
                break;
            case 'f': case 'd':
                slots[i + explicitBase].dbl = runtime::icall_unbox_double(boxed);
                break;
            case 'r':
                slots[i + explicitBase].ptr = boxed;
                break;
            default:
                return nullptr;
            }
            p = comma != nullptr ? comma + 1 : nullptr;
        }
    }
    // Dispatch through the invoker and box the result by the return kind.
    // Instance methods prepend a leading reference parameter to the dispatch
    // signature so the invoker's typed call table reads the injected 'this'
    // slot first; static/free-function signatures go through verbatim.
    char dispatchSig[88];
    if (!isInstance)
    {
        size_t invokerLen = std::strlen(invokerSig);
        std::memcpy(dispatchSig, invokerSig, invokerLen + 1);
    }
    else
    {
        size_t invokerLen = std::strlen(invokerSig);
        int32_t pos = 0;
        dispatchSig[pos++] = invokerSig[0]; // return code
        dispatchSig[pos++] = ':';
        dispatchSig[pos++] = 'r';
        if (invokerLen > 2)
        {
            // The explicit params follow WITHOUT their ':' separator
            // (invokerSig[1] == ':'): "I:i,i" -> "I:r,i,i".
            dispatchSig[pos++] = ',';
            std::memcpy(dispatchSig + pos, invokerSig + 2, invokerLen - 2);
            pos += static_cast<int32_t>(invokerLen - 2);
        }
        dispatchSig[pos] = '\0';
    }
    // The ret buffer is a full pointer-width slot: the I64/Dbl/Ref return
    // kinds write 8 bytes (int64_t / double / object pointer), so a 4-byte
    // int32_t buffer would overflow into the adjacent stack frame -- the
    // RF-N2 probe exposed this on the reference-return path (get_Name string
    // getter), a shape RF-1 never e2e-exercised (its instance probes were all
    // int32-return). The low 4 bytes are read for the I32/I64/Dbl/Ref
    // boxers below regardless.
    uintptr_t ret = 0;
    if (il2cpp_invoker_invoke(found->fnPtr, dispatchSig, slots, argCount, &ret) != kInvokerOk)
    {
        return nullptr;
    }
    switch (invokerSig[0])
    {
    case 'V': return nullptr;
    case 'I': return il2cpp_box_int32(nullptr, *reinterpret_cast<int32_t*>(&ret));
    case 'L': return runtime::icall_box_long(*reinterpret_cast<int64_t*>(&ret));
    case 'D': return runtime::icall_box_double(*reinterpret_cast<double*>(&ret));
    case 'R': return *reinterpret_cast<Il2CppObject**>(&ret);
    default:  return nullptr;
    }
}

//------------------------------------------------------------------------------
// RF-N2 -- PropertyInfo.GetValue/SetValue. The receiver is the PROPERTY-NAME
// string (the simplified member model, as il2cpp_typeinfo_get_property
// returns); the declaring type is joined from the TARGET object's klass
// (il2cpp_typeinfo_lookup_by_class) and the property is resolved in the own +
// base-chain property tables (il2cpp_type_get_property -- inherited
// properties resolve on a derived receiver, matching net48). The GETTER /
// SETTER ACCESSOR METHOD (Il2CppPropertyInfo.getterName / setterName) is then
// dispatched through il2cpp_methodinfo_invoke -- the SAME invoker wrapper as
// MethodInfo.Invoke (RF-1 instance-'this' injection; the C3-2 event-accessor
// precedent) -- with the index array elements (GetValue) / index elements +
// value (SetValue) as the object[] args.
//
//   Null target -> TargetException (net48 instance-property doctrine: an
//     instance-property GetValue/SetValue on a null obj throws TargetException
//     -- NOT NullReferenceException, the field-value difference; probe-
//     verified against v4.0.30319).
//   Unknown property / unreflected type / malformed name -> nullptr (no-op
//     defensive, the field/method query convention).
//   Missing accessor (write-only GetValue / read-only SetValue) -> LOUD
//     ArgumentException ("Property Get/Set method not found", net48 text).
//   An indexer (indexParams > 0) is in scope through the same invoker: the
//     getter's explicit params are the index values, the setter's are the
//     index values + value. A null/empty index on a non-indexed property is
//     the ordinary zero-explicit-param getter / one-param setter shape.
//------------------------------------------------------------------------------
namespace {

// Throw TargetException for a null-target instance-property access (net48:
// PropertyInfo.GetValue(null)/SetValue(null, ...) on an INSTANCE property
// throws TargetException -- probe-verified; distinct from FieldInfo which
// throws NullReferenceException).
void ThrowPropertyTargetException()
{
    il2cpp_throw_new("System.Reflection.TargetException,System.Exception",
        "Non-static method requires a target.");
}

// Throw TargetParameterCountException when the index array length does not
// match the property's indexer arity (RF-N2 瀹¤A鈶? observation B -- net48
// probe-verified: an indexer with a null/empty index AND a non-indexed
// property receiving a non-null index array BOTH throw
// TargetParameterCountException; the runtime previously built a 0-length
// arg array and the invoker arity mismatch silently returned nullptr).
void ThrowPropertyParamCountException()
{
    il2cpp_throw_new("System.Reflection.TargetParameterCountException,System.Exception",
        "Parameter count mismatch.");
}

// Throw NullReferenceException for a null property-name receiver (RF-N2 瀹¤
// A鈶? observation C -- net48 probe-verified: typeof(X).GetProperty("Missing")
// returns null and the chained .GetValue/.SetValue call then dereferences the
// null PropertyInfo receiver, throwing NullReferenceException; the runtime
// previously returned nullptr silently). Declaration by managed name through
// the thrown-type chain: the catch matcher (il2cpp_exception_matches) walks
// every segment, so the first element is the canonical managed full name.
void ThrowPropertyNullReceiver()
{
    il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
        "Object reference not set to an instance of an object.");
}

// Build the object[] dispatched to the property accessor. For a getter the
// args are the index array elements (null/empty -> zero explicit args -- the
// non-indexed zero-param getter). For a setter the value is APPENDED after
// the index elements (net48 setter signature: set_X([index...,] value)).
Il2CppArray* BuildPropertyArgs(Il2CppArray* index, Il2CppObject* value, bool appendValue)
{
    const int32_t indexLen = index != nullptr ? index->length : 0;
    const int32_t total = indexLen + (appendValue ? 1 : 0);
    Il2CppArray* args = il2cpp_array_new(nullptr, sizeof(void*), true, total);
    for (int32_t i = 0; i < indexLen; ++i)
    {
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(args, i)) =
            *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(index, i));
    }
    if (appendValue)
    {
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(args, indexLen)) = value;
    }
    return args;
}

} // namespace

Il2CppObject* il2cpp_typeinfo_property_get_value(Il2CppObject* propertyName, Il2CppObject* target,
                                                 Il2CppArray* index)
{
    if (target == nullptr)
    {
        ThrowPropertyTargetException();
    }
    char nameBuf[kNameBufferSize];
    if (propertyName == nullptr)
    {
        // RF-N2 瀹¤A鈶?(observation C): a null property-name receiver is the
        // "unknown property" path (GetProperty returned null on a miss) --
        // net48 NREs on the null receiver (probe-verified). The non-ASCII
        // malformed-name case below keeps the defensive nullptr (a bad
        // managed string is unreachable from GetProperty, which null-returns
        // on a miss).
        ThrowPropertyNullReceiver();
    }
    if (!AsciiFromManagedString(propertyName, nameBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(target->klass);
    if (info == nullptr)
    {
        return nullptr;
    }
    const Il2CppPropertyInfo* prop = il2cpp_type_get_property(info, nameBuf);
    if (prop == nullptr)
    {
        return nullptr;
    }
    if (prop->getterName == nullptr)
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
            "Property Get method not found.");
    }
    // RF-N2 瀹¤A鈶?(observation B): the index array length must equal the
    // property's indexer arity; a mismatch (indexer + null index, or a
    // non-indexed property receiving a non-null index) is a
    // TargetParameterCountException on net48.
    if ((index != nullptr ? index->length : 0) != prop->indexParams)
    {
        ThrowPropertyParamCountException();
    }
    Il2CppArray* args = BuildPropertyArgs(index, nullptr, false);
    Il2CppObject* accessorStr = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(prop->getterName));
    return il2cpp_methodinfo_invoke(accessorStr, target, args);
}

void il2cpp_typeinfo_property_set_value(Il2CppObject* propertyName, Il2CppObject* target,
                                        Il2CppObject* value, Il2CppArray* index)
{
    if (target == nullptr)
    {
        ThrowPropertyTargetException();
    }
    char nameBuf[kNameBufferSize];
    if (propertyName == nullptr)
    {
        // RF-N2 瀹¤A鈶?(observation C): null receiver == unknown property
        // (GetProperty null-returned on a miss) -> net48 NullReferenceException
        // on the chained call (probe-verified; see the getter twin above).
        ThrowPropertyNullReceiver();
    }
    if (!AsciiFromManagedString(propertyName, nameBuf))
    {
        return;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup_by_class(target->klass);
    if (info == nullptr)
    {
        return;
    }
    const Il2CppPropertyInfo* prop = il2cpp_type_get_property(info, nameBuf);
    if (prop == nullptr)
    {
        return;
    }
    if (prop->setterName == nullptr)
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
            "Property Set method not found.");
    }
    // RF-N2 瀹¤A鈶?(observation B): index-array arity must match the indexer
    // (see the getter twin above); mismatch -> TargetParameterCountException.
    if ((index != nullptr ? index->length : 0) != prop->indexParams)
    {
        ThrowPropertyParamCountException();
    }
    Il2CppArray* args = BuildPropertyArgs(index, value, true);
    Il2CppObject* accessorStr = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(prop->setterName));
    (void)il2cpp_methodinfo_invoke(accessorStr, target, args);
}

//------------------------------------------------------------------------------
// RF-N6 -- il2cpp_attribute_member_get_custom (typeinfo.h contract): the
// member-level GetCustomAttributes icall. The receiver is the MEMBER-NAME
// string; the declaring type is found by scanning the typeinfo registry for
// the type that DECLARES the member (own method/field/property tables) and
// picking the DEEPEST one (net48 most-derived-first). inherit walks the
// declaring type's own + base chain collecting the same-named member's
// registered attributes from the RF-N6 member registry (attribute.cpp).
//------------------------------------------------------------------------------
namespace {

// Own-table declaration check for one member name across the three member
// kinds (a member value cannot distinguish kind -- net48 member names are
// unique within a type, so the deepest declaring type is unambiguous across
// kinds).
bool DeclaresMemberName(const Il2CppTypeInfo* info, const char* name)
{
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (info->methods[i].name != nullptr && std::strcmp(info->methods[i].name, name) == 0)
                return true;
    if (info->fields != nullptr)
        for (int32_t i = 0; i < info->fieldCount; ++i)
            if (info->fields[i].name != nullptr && std::strcmp(info->fields[i].name, name) == 0)
                return true;
    if (info->properties != nullptr)
        for (int32_t i = 0; i < info->propertyCount; ++i)
            if (info->properties[i].name != nullptr && std::strcmp(info->properties[i].name, name) == 0)
                return true;
    return false;
}

int32_t BaseChainDepth(const Il2CppTypeInfo* info)
{
    int32_t depth = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        ++depth;
    }
    return depth;
}

// Build the member-registry key: "ownerReflectionFullName::memberName".
void BuildMemberKey(const Il2CppTypeInfo* info, const char* name,
                    char (&buf)[kNameBufferSize * 2 + 2])
{
    std::snprintf(buf, sizeof(buf), "%s::%s", info->name != nullptr ? info->name : "", name);
}

} // namespace

Il2CppArray* il2cpp_attribute_member_get_custom(Il2CppObject* memberName, int32_t inherit)
{
    if (memberName == nullptr)
    {
        // net48: ((MemberInfo)null).GetCustomAttributes(false) ->
        // NullReferenceException.
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
        return nullptr; // unreachable -- the throw does not return
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf))
    {
        return EmptyStringArray();
    }
    // Find the deepest registered type that DECLARES the member (own tables).
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info != nullptr && DeclaresMemberName(info, nameBuf))
        {
            const int32_t depth = BaseChainDepth(info);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = info;
            }
        }
    }
    if (best == nullptr)
    {
        return EmptyStringArray();
    }
    // Count pass: most-derived level first, walking the base chain while
    // inherit is set; each level contributes the SAME-NAMED member's member-
    // registry entries (inherited members live under their DECLARING type's
    // table, so the declaring-type name keys the registry).
    int32_t total = 0;
    for (const Il2CppTypeInfo* level = best; level != nullptr; level = level->baseType)
    {
        if (level != best && !inherit)
        {
            break;
        }
        if (!DeclaresMemberName(level, nameBuf))
        {
            continue;
        }
        char key[kNameBufferSize * 2 + 2];
        BuildMemberKey(level, nameBuf, key);
        int32_t count = 0;
        (void)runtime::il2cpp_attribute_lookup_member(key, &count);
        total += count;
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(void*), true, total);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* level = best; level != nullptr; level = level->baseType)
    {
        if (level != best && !inherit)
        {
            break;
        }
        if (!DeclaresMemberName(level, nameBuf))
        {
            continue;
        }
        char key[kNameBufferSize * 2 + 2];
        BuildMemberKey(level, nameBuf, key);
        int32_t count = 0;
        Il2CppObject** attrs = runtime::il2cpp_attribute_lookup_member(key, &count);
        for (int32_t j = 0; j < count; ++j)
        {
            *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(result, dst++)) = attrs[j];
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// RF-N7 -- the LOUD icalls (typeinfo.h contract). Each throws an explicit
// NotSupportedException instead of letting the transpiler fall to a spelling
// generated-member name (the pre-RF-N7 phantom-symbol trap). Arguments are
// consumed for signature shape only; the rejection is unconditional (the
// operations have no AOT implementation and must never fabricate a result).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_makegenericmethod_loud(Il2CppObject* memberName, Il2CppArray* typeArgs)
{
    (void)memberName;
    (void)typeArgs;
    il2cpp_throw_not_supported_exception(
        "MethodInfo.MakeGenericMethod is not wired in RF-N7 -- runtime generic-method instantiation has no AOT support (explicit LOUD; the generated-name fallback is disabled)");
    return nullptr; // unreachable -- the throw does not return
}

Il2CppObject* il2cpp_makegenerictype_loud(Il2CppObject* typeName, Il2CppArray* typeArgs)
{
    (void)typeName;
    (void)typeArgs;
    il2cpp_throw_not_supported_exception(
        "Type.MakeGenericType is not wired in RF-N7 -- no fabricated generic arguments (禁伪造), the generated-name fallback is disabled (explicit LOUD)");
    return nullptr; // unreachable -- the throw does not return
}

Il2CppObject* il2cpp_createdelegate_loud(Il2CppObject* delegateType, Il2CppObject* methodName)
{
    (void)delegateType;
    (void)methodName;
    il2cpp_throw_not_supported_exception(
        "Delegate.CreateDelegate is not wired in RF-N7 -- binding a delegate to a runtime member has no AOT support (explicit LOUD; the generated-name fallback is disabled)");
    return nullptr; // unreachable -- the throw does not return
}

} // namespace il2cpp
