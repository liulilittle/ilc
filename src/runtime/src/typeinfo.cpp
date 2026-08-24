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

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception / _null_reference
#include "il2cpp_string.h"        // Il2CppString (managed-string ABI queries)

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
    return FindMemberThroughBaseChain(info, name,
        &Il2CppTypeInfo::fields, &Il2CppTypeInfo::fieldCount);
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

} // namespace il2cpp
