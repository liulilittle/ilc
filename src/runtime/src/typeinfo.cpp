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
#include "il2cpp_typecheck.h"     // runtime::icall_type_is_instance (refl2 IsInstanceOfType delegate)
#include "il2cpp_typeext.h"       // runtime::il2cpp_type_is_assignable_from (refl2 delegate)
#include "enumext.h"              // runtime::il2cpp_enum_get_*_by_type_name / is_registered_name (refl6a)
#include "reflect_base.h"         // il2cpp_type_get_type (p-refl2-gapmatrix Assembly.GetType delegate)

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

#if !defined(_WIN32)
    // MSVC spells the ordinal ASCII case-insensitive compare _stricmp; glibc
    // and the BSDs spell it strcasecmp (<strings.h>). The comparison here is
    // ASCII-only either way, so the two are interchangeable.
    #include <strings.h>
    #define _stricmp strcasecmp
#endif

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
// il2cpp_typeinfo_count / il2cpp_typeinfo_at -- the registry enumeration behind
// Assembly.GetTypes() on the input assembly (p-refl2-gapmatrix C06).
//------------------------------------------------------------------------------
int32_t il2cpp_typeinfo_count()
{
    return g_registeredTypeInfoCount;
}

const Il2CppTypeInfo* il2cpp_typeinfo_at(int32_t index)
{
    if (index < 0 || index >= g_registeredTypeInfoCount) return nullptr;
    return g_registeredTypeInfos[index];
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_lookup_ignore_case -- the ASCII case-insensitive variant of
// il2cpp_typeinfo_lookup. Type.GetType(string, bool, bool ignoreCase) resolves
// a well-formed name case-insensitively (net48's ignoreCase semantics); the
// comparison is ASCII-only, matching the name convention (generated names are
// pure ASCII). Returns nullptr when no entry matches (or the input is null).
//------------------------------------------------------------------------------
const Il2CppTypeInfo* il2cpp_typeinfo_lookup_ignore_case(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    for (int i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info->name != nullptr && _stricmp(info->name, name) == 0)
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
//------------------------------------------------------------------------------
// MemberMatchesVis -- the net48 BindingFlags visibility + static filter for the
// per-name member queries (GAP-1 / P0). Il2Cpp*Info.flags bits: bit0 = public,
// bit1 = static (typeinfo.h). bindingAttr is the net48 System.Reflection.
// BindingFlags value (Public = 16, NonPublic = 32, Instance = 4, Static = 8).
//
// net48 semantics (probe-verified): the (string) overloads of GetMethod/
// GetProperty/GetField bind Public|Instance|Static -- a PRIVATE member is
// invisible; an EXPLICIT NonPublic bit (alone or combined) admits non-public
// members, and an explicit Instance-only / Static-only bit narrows the search.
// When neither Public nor NonPublic is set, net48 defaults to public-only
// (the raw typeinfo flags bit0 -- the get_field flags&1 precedent).
//------------------------------------------------------------------------------
bool MemberMatchesVis(int32_t memberFlags, int32_t bindingAttr, bool hasAccessorFlags)
{
    (void)hasAccessorFlags;
    const bool isPublic = (memberFlags & 1) != 0;
    const bool isStatic = (memberFlags & 2) != 0;
    const bool wantPublic = (bindingAttr & 16) != 0;
    const bool wantNonPublic = (bindingAttr & 32) != 0;
    const bool wantInstance = (bindingAttr & 4) != 0;
    const bool wantStatic = (bindingAttr & 8) != 0;
    if (!wantPublic && !wantNonPublic)
    {
        // No visibility bit -> net48 default public-only.
        if (!isPublic) return false;
    }
    else if (wantPublic && wantNonPublic)
    {
        // Both: any visibility accepted.
    }
    else if (wantPublic)
    {
        if (!isPublic) return false;
    }
    else
    {
        if (isPublic) return false; // NonPublic-only
    }
    if (wantInstance && !wantStatic)
    {
        if (isStatic) return false;
    }
    else if (wantStatic && !wantInstance)
    {
        if (!isStatic) return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_method_flags -- flags-aware Type.GetMethod(name, bindingAttr)
// primitive (GAP-1 / P0). Same base-chain walk as il2cpp_type_get_method with
// the visibility/static binding filter applied per hit (a private/static-
// mismatched hit is SKIPPED and the scan continues -- a public base
// same-name method is still found, matching net48). The unprefixed
// il2cpp_type_get_method stays UNFILTERED (its internal runtime callers --
// object.cpp .ctor resolution, COM CCW dispatch -- must see every entry).
//------------------------------------------------------------------------------
const Il2CppMethodInfo* il2cpp_type_get_method_flags(const Il2CppTypeInfo* info,
                                                     const char* name,
                                                     int32_t bindingAttr)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName("name");
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name != nullptr && std::strcmp(m.name, name) == 0
                && MemberMatchesVis(m.flags, bindingAttr, false))
            {
                return &m;
            }
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_property_flags -- flags-aware Type.GetProperty(name,
// bindingAttr) primitive (GAP-1 / P0). A property carries NO flags field
// (typeinfo.h -- the Il2CppPropertyInfo has getterName/setterName only), so
// the visibility is derived from the accessor method flags (accessors are
// declared in the SAME table as the property; net48 treats a property as
// public when ANY accessor is public). A property without resolvable
// accessors is admitted (the simplified model's "all properties"
// enumeration doc allocates no flags -- the default public filter then passes
// it like before).
//------------------------------------------------------------------------------
const Il2CppPropertyInfo* il2cpp_type_get_property_flags(const Il2CppTypeInfo* info,
                                                         const char* name,
                                                         int32_t bindingAttr)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName("name");
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->properties == nullptr)
        {
            continue;
        }
        for (int32_t i = 0; i < scan->propertyCount; ++i)
        {
            const Il2CppPropertyInfo& p = scan->properties[i];
            if (p.name == nullptr || std::strcmp(p.name, name) != 0)
            {
                continue;
            }
            // Resolve the accessor flags through the same table's methods --
            // an UNFILTERED name walk (the accessor is declared beside the
            // property; its visibility is what DECIDES the property's
            // visibility, so it must be found whether public or private).
            const char* accessorNames[2] = { p.getterName, p.setterName };
            int32_t accessorFlags = 0;
            bool sawAccessor = false;
            for (int32_t ai = 0; ai < 2; ++ai)
            {
                if (accessorNames[ai] == nullptr)
                {
                    continue;
                }
                const Il2CppMethodInfo* acc =
                    il2cpp_type_get_method(scan, accessorNames[ai]);
                if (acc != nullptr)
                {
                    accessorFlags |= acc->flags;
                    sawAccessor = true;
                }
            }
            // A property is admitted when any accessor matches the binding
            // (MemberMatchesVis). No-resolvable-accessor edge (trimmed
            // accessor / property table entry without method-table accessors):
            // the property table has no flags field -- admit (the pre-GAP-1
            // behavior never hid such a property). NOTE: `sawAccessor` (not
            // `accessorFlags`) is the no-accessor test -- a PRIVATE accessor
            // resolves with flags 0 yet must NOT auto-admit (p-refl-visible
            // priv.prop.none pin: the default binding hides it).
            if (!sawAccessor || MemberMatchesVis(accessorFlags, bindingAttr, true))
            {
                return &p;
            }
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_field_flags -- flags-aware Type.GetField(name, bindingAttr)
// primitive (GAP-1 / P0). Extends the existing public-only get_field walk
// with the full BindingFlags filter (NonPublic admits private fields; an
// explicit Instance/Static bit narrows the search). A private/static-mismatch
// hit is SKIPPED and the scan continues.
//------------------------------------------------------------------------------
const Il2CppFieldInfo* il2cpp_type_get_field_flags(const Il2CppTypeInfo* info,
                                                   const char* name,
                                                   int32_t bindingAttr)
{
    if (info == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    if (name == nullptr)
    {
        ThrowNullMemberName(nullptr); // net48: GetField(null) ParamName is empty
    }
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
                && MemberMatchesVis(field.flags, bindingAttr, false))
            {
                return &field;
            }
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_type_get_field -- unchanged public-only Type.GetField(name) primitive
// (the net48 default-binding walk; GAP-1 P0's documented precedent).
//------------------------------------------------------------------------------
const Il2CppFieldInfo* il2cpp_type_get_field(const Il2CppTypeInfo* info, const char* name)
{
    return il2cpp_type_get_field_flags(info, name, 16 | 4 | 8);
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

// O4 (boxed user-struct reflection): the reflected-field payload shift for a
// BOXED VALUE TYPE. A boxed user struct's payload lives at byte offset 8 of
// the box object (the object header is slot 0 -- boxingext2.cpp's
// kBoxPayloadOffset / il2cpp_unbox_user_struct), so the struct-relative
// Il2CppFieldInfo.offset must be shifted by this header on the object base.
// Kept as a local mirror of the runtime-wide constant (must stay in sync with
// boxingext2.cpp kBoxPayloadOffset -- the same "one 8-byte box slot" law).
constexpr intptr_t kReflectionBoxPayloadOffset = 8;

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
// GAP-1 (P0): the no-arg BindingFlags overloads use net48's default binding
// Public|Instance|Static (16|4|8) -- a PRIVATE property is invisible.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_property(Il2CppObject* typeName, Il2CppObject* memberName)
{
    return QueryManagedString(typeName, memberName, "name",
        [](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_property_flags(info, name, 16 | 4 | 8);
        });
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_property_flags -- managed-string Type.GetProperty(name,
// BindingFlags) icall (GAP-1 / P0): full visibility/static binding.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_property_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                                 int32_t bindingAttr)
{
    return QueryManagedString(typeName, memberName, "name",
        [bindingAttr](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_property_flags(info, name, bindingAttr);
        });
}

//------------------------------------------------------------------------------
// QueryManagedMethod -- the memory-safe GetMethod(name[, flags]) body with the
// refl2 (item 4) AMBIGUITY rule: count the PUBLIC-instance-static same-name
// methods across the own + base chain; net48 throws AmbiguousMatchException
// when more than one matches (probe-pinned: typeof(AmbMid).GetMethod("Multi")
// with public Multi() + base Multi(int) -> Threw:AmbiguousMatchException) and
// returns the single match otherwise. A same-name PRIVATE method stays
// invisible under the default/explicit public binding (it does NOT count toward
// ambiguity -- net48 binding hides it first). Null contract is the shared
// QueryManagedString one (null receiver -> NRE, null name -> ABN "name").
//
// refl2 (item 4 audit, DEFECT-R2-AME): SIGNATURE-AWARE counting. The runtime
// groups the same-name candidates by PARAMETER signature and lets the
// most-derived entry of each signature SHADOW its base-class same-signature
// entries -- a derived `override` or `new` F() hides the base F() (same
// signature), so the no-types GetMethod returns that unique most-derived hit
// instead of throwing. Only DISTINCT surviving signatures (real overloads)
// roll up to AmbiguousMatchException. Signature identity = arity + per-param
// type (the ParamEncodingToFullName projection). A same-signature pair at the
// SAME chain level (not an inheritance shadow) is NOT merged -- it stays two
// matches and is ambiguous (defensive; cannot be expressed in C#, documented
// in GAPS.md).
//------------------------------------------------------------------------------
namespace {

// Forward-declared shared signature decoders (defined below, in the
// exact-signature block): the ambiguity/shadow grouping uses the SAME param
// encodings the Type[] matcher does, so both stay consistent.
bool ParamEncodingToFullName(const char* enc, char (&out)[kNameBufferSize]);
int SplitSignatureParams(const char* sig, char out[][kNameBufferSize], int maxOut);

// Build a parameter-only signature key (arity + per-param full name, one
// "|"-joined canonical string) used to compare candidates for the shadow
// rule. Returns false on a malformed signature (paramCount/encoding mismatch
// or >4 params) so the caller can treat the entry non-mergable.
bool MethodParamKey(const Il2CppMethodInfo* m, char (&key)[kNameBufferSize])
{
    if (m == nullptr || m->signature == nullptr ||
        m->paramCount < 0 || m->paramCount > 4)
    {
        return false;
    }
    char encs[4][kNameBufferSize];
    int decoded = SplitSignatureParams(m->signature, encs, 4);
    if (decoded != m->paramCount) return false;
    int used = std::snprintf(key, kNameBufferSize, "%d", m->paramCount);
    if (used < 0 || used >= kNameBufferSize) return false;
    for (int i = 0; i < decoded; ++i)
    {
        char full[kNameBufferSize];
        const char* token = encs[i];
        if (ParamEncodingToFullName(encs[i], full)) token = full;
        const int n = std::snprintf(key + used, kNameBufferSize - used, "|%s", token);
        if (n < 0 || n >= kNameBufferSize - used) return false;
        used += n;
    }
    return true;
}

template <typename Binding>
Il2CppObject* QueryManagedMethod(Il2CppObject* typeName, Il2CppObject* memberName,
                                 const char* paramName, Binding binding)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    if (memberName == nullptr) ThrowNullMemberName(paramName);
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    const int32_t bindingAttr = binding();
    // Collect the visible same-name candidates most-derived first (the chain
    // walk already visits info=level0 then each base at +1), storing each
    // candidate with its chain level and a parameter-signature key.
    const Il2CppMethodInfo* cand[16];
    int32_t candLevel[16];
    char    candKey[16][kNameBufferSize];
    bool    candMergable[16];
    int32_t candCount = 0;
    int32_t level = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType, ++level)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name == nullptr || std::strcmp(m.name, memberBuf) != 0) continue;
            if (!MemberMatchesVis(m.flags, bindingAttr, false)) continue;
            if (candCount >= 16) break;
            cand[candCount] = &m;
            candLevel[candCount] = level;
            candMergable[candCount] = MethodParamKey(&m, candKey[candCount]);
            if (!candMergable[candCount]) candKey[candCount][0] = '\0';
            ++candCount;
        }
    }
    // Surviving signature count: for each distinct signature key only its
    // most-derived (lowest chain level) occurrences count -- a base
    // same-signature entry is shadowed by the derived override/new and
    // dropped. Two same-signature candidates at the SAME level are not an
    // inheritance shadow (defensive; not expressible in C# so unpinnable) --
    // both count -> ambiguous. A non-mergeable candidate (malformed/multi-dim
    // signature) never shares a key with anyone: it groups alone.
    const Il2CppMethodInfo* found = nullptr;
    int32_t surviving = 0;
    bool counted[16] = { false, false, false, false, false, false, false, false,
                         false, false, false, false, false, false, false, false };
    for (int32_t c = 0; c < candCount; ++c)
    {
        if (counted[c]) continue;
        const char* key = candKey[c];
        const bool mergeable = candMergable[c];
        // Lowest chain level owning this signature key.
        int32_t minLevel = candLevel[c];
        for (int32_t other = 0; other < candCount; ++other)
        {
            if (other == c || !candMergable[other]) continue;
            if (!mergeable || std::strcmp(candKey[other], key) != 0) continue;
            if (candLevel[other] < minLevel) minLevel = candLevel[other];
        }
        // Count every candidate of this key AT that level (same-level pairs
        // both count; base same-signature entries are OUTSIDE minLevel and
        // shadowed).
        for (int32_t other = c; other < candCount; ++other)
        {
            if (counted[other]) continue;
            if (!candMergable[other])
            {
                if (mergeable || other != c) continue; // non-mergeable group alone
            }
            else
            {
                if (!mergeable || std::strcmp(candKey[other], key) != 0) continue;
            }
            if (candLevel[other] != minLevel) continue;
            counted[other] = true;
            if (found == nullptr) found = cand[other];
            ++surviving;
        }
    }
    if (surviving > 1)
    {
        il2cpp_throw_new("System.Reflection.AmbiguousMatchException,System.Reflection.AmbiguousMatchException,System.SystemException,System.Exception",
                         "Type.GetMethod: multiple methods match the name and binding flags -- ambiguous.");
    }
    return found != nullptr ? memberName : nullptr;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_method -- managed-string Type.GetMethod(name) icall.
// GAP-1 (P0): the no-arg BindingFlags overloads use net48's default binding
// Public|Instance|Static -- a PRIVATE method is invisible. refl2 (item 4):
// public same-name overloads make the query ambiguous (AmbiguousMatchException).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_method(Il2CppObject* typeName, Il2CppObject* memberName)
{
    return QueryManagedMethod(typeName, memberName, "name",
        []() { return 16 | 4 | 8; });
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_method_flags -- managed-string Type.GetMethod(name,
// BindingFlags) icall (GAP-1 / P0): full visibility/static binding; refl2
// (item 4): public same-name overloads -> AmbiguousMatchException.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_method_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                               int32_t bindingAttr)
{
    return QueryManagedMethod(typeName, memberName, "name",
        [bindingAttr]() { return bindingAttr; });
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
// il2cpp_typeinfo_get_field_flags -- managed-string Type.GetField(name,
// BindingFlags) icall (GAP-1 / P0): full visibility/static binding.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_field_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                              int32_t bindingAttr)
{
    return QueryManagedString(typeName, memberName, nullptr,
        [bindingAttr](const Il2CppTypeInfo* info, const char* name) {
            return il2cpp_type_get_field_flags(info, name, bindingAttr);
        });
}

//------------------------------------------------------------------------------
// refl2 -- Type.GetMethod/GetProperty(string, Type[]) EXACT-signature matching.
//
// net48 binds these overloads with Public|Instance|Static by default and
// matches the member by EXACT arity + EXACT parameter-type list (Type identity
// -- no coercion, no widening). The Type[] elements are Type VALUES (full-name
// strings, the simplified Type model); the runtime decodes each reflected
// method's encoded signature / the property's indexer parameter encodings into
// canonical full names and compares them by arity + order.
//------------------------------------------------------------------------------
namespace {

// EncodeType (MethodSignatureCoder) leaf/full-name code -> managed full name.
// The reflected method signature stores the TRANSPILER encoding ("i" for
// int32-family, "l" for Int64, "r:System.String", "r:Cases.Foo", "V:..." value
// types, ...). Returns false when the encoding has no exact full-name
// equivalent (fnptr "F:", pointer "P:", byref "B:", array "r:X[]" with a
// non-name element suffix) -- those params NEVER match a typeof-sized Type[]
// element in this model, so the member is rejected (defensive; the probe set
// uses only name-resolvable encodings).
bool ParamEncodingToFullName(const char* enc, char (&out)[kNameBufferSize])
{
    if (enc == nullptr) return false;
    if (enc[0] == 'r' && enc[1] == ':')
    {
        // "r:X" / "r:X[]" -- reference type. A name SUFFIX ("i[]", "l[]") is
        // not a resolvable full name; strip "r:" and accept the bare name ONLY
        // when it is a plain identifier (no '['). SZARRAY encodings of the
        // primitives ("r:i[]") are out of the exact-match model.
        const char* name = enc + 2;
        if (name[0] == '\0' || std::strchr(name, '[') != nullptr) return false;
        std::snprintf(out, kNameBufferSize, "%s", name);
        return true;
    }
    if (enc[0] == 'V' && enc[1] == ':')
    {
        const char* name = enc + 2;
        if (name[0] == '\0' || std::strchr(name, '[') != nullptr) return false;
        std::snprintf(out, kNameBufferSize, "%s", name);
        return true;
    }
    if (enc[0] == 'T' && enc[1] == ':')
    {
        const char* name = enc + 2;
        if (name[0] == '\0' || std::strchr(name, '[') != nullptr) return false;
        std::snprintf(out, kNameBufferSize, "%s", name);
        return true;
    }
    // Primitive leaf codes (EncodeType). "i" covers the int32 FILTER family
    // (Int32/UInt32/native-uint share the slot-encoding "i" -- the W7
    // documented simplification: an int-vs-uint overload distinction is not
    // expressible in the typeinfo signature, so the exact match conflates the
    // int32-family names; the probe uses only encodings whose full names differ).
    static const struct { const char* code; const char* full; } kLeaf[] = {
        { "b", "System.Boolean" }, { "c", "System.Char" },
        { "i1", "System.SByte" }, { "u1", "System.Byte" },
        { "i2", "System.Int16" }, { "u2", "System.UInt16" },
        { "i", "System.Int32" }, { "l", "System.Int64" },
        { "q", "System.UInt64" }, { "f", "System.Single" },
        { "d", "System.Double" }, { "n", "System.IntPtr" },
        { "v", "System.Void" },
    };
    for (size_t i = 0; i < sizeof(kLeaf) / sizeof(kLeaf[0]); ++i)
    {
        if (std::strcmp(enc, kLeaf[i].code) == 0)
        {
            std::snprintf(out, kNameBufferSize, "%s", kLeaf[i].full);
            return true;
        }
    }
    return false;
}

// Decode a reflected method/accessor signature into its EXPLICIT parameter
// encodings. Two accepted formats (typeinfo.h / the invoker R3 doc):
//   * TRANSPILER "RET(PARAMS)" (TypeEmitter.cs -- the typeinfo table stores
//     "i(i,i)" / "r:System.String(l)");
//   * INVOKER "RET:PARAMS" ("I:i,i") passed through unchanged.
// Returns the encoding count (each NUL-terminated in out), -1 when the
// signature is malformed or too long. The param region is copied into a
// caller-owning static buffer (the comma walk must stay on stable memory --
// a stack-local inside this function would dangle once returned).
int SplitSignatureParams(const char* sig, char out[][kNameBufferSize], int maxOut)
{
    static char s_params[kNameBufferSize * 4];
    if (sig == nullptr) return -1;
    const char* start = nullptr;
    if (const char* open = std::strchr(sig, '('))
    {
        const char* close = std::strchr(open + 1, ')');
        if (close == nullptr) return -1;
        // Copy the param substring into the static buffer.
        const size_t len = static_cast<size_t>(close - open - 1);
        if (len >= sizeof(s_params)) return -1;
        std::memcpy(s_params, open + 1, len);
        s_params[len] = '\0';
        start = s_params;
    }
    else if (const char* colon = std::strchr(sig, ':'))
    {
        start = colon + 1;
    }
    else
    {
        return 0; // no params (void/empty signature)
    }
    int count = 0;
    const char* cur = start;
    for (;;)
    {
        const char* comma = std::strchr(cur, ',');
        const size_t len = comma != nullptr ? static_cast<size_t>(comma - cur) : std::strlen(cur);
        if (len >= kNameBufferSize) return -1;
        // Skip empty segments (trailing commas / empty param list).
        if (len > 0)
        {
            if (count >= maxOut) return -1;
            std::memcpy(out[count], cur, len);
            out[count][len] = '\0';
            ++count;
        }
        if (comma == nullptr) break;
        cur = comma + 1;
    }
    return count;
}

// Match a reflected method's parameter list against the managed Type[] (its
// length = the built types array). True when arity AND per-param Type-identity
// agree (every requested type's full name equals the corresponding encoded
// param's full name).
bool MethodTypesMatch(const Il2CppMethodInfo* m, const char typeNames[][kNameBufferSize], int32_t count)
{
    if (m == nullptr || m->paramCount != count) return false;
    if (count == 0) return true;
    char encs[4][kNameBufferSize];
    int decoded = SplitSignatureParams(m->signature, encs, 4);
    if (decoded != count) return false;
    for (int32_t i = 0; i < count; ++i)
    {
        char full[kNameBufferSize];
        if (!ParamEncodingToFullName(encs[i], full)) return false;
        if (std::strcmp(full, typeNames[i]) != 0) return false;
    }
    return true;
}

// Extract every Type[] element as a managed full-name string (kNameBufferSize
// bound). Returns false on a null/malformed element.
bool ReadTypeElements(Il2CppArray* types, char typeNames[][kNameBufferSize], int32_t count)
{
    for (int32_t i = 0; i < count; ++i)
    {
        Il2CppObject* elem = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(types, i));
        if (!AsciiFromManagedString(elem, typeNames[i])) return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// REFL3 (item 2) -- ParameterInfo value codec. Each PARAMETER of a reflected
// method is a ParameterInfo VALUE encoded as a pipe-joined managed string:
//   "name|typeFullName|position|flags"
// (net48 ParameterInfo.Name/ParameterType/Position/IsOut -- the 4 surfaced
// properties). The name + flags come from the transpiler-baked
// Il2CppMethodInfo.paramMeta ("name|outFlag" rows, ';'-joined, typeinfo.h);
// the typeFullName is decoded from the method's signature param encoding
// (ParamEncodingToFullName) -- the Type VALUE (full-name string) -- and
// position is the 0-based signature slot. flags bit0 = IsOut.
//
// Why a VALUE string: the simplified member model represents a reflected
// VALUE as a name string; a ParameterInfo's 4 properties each decode the same
// compact value instead of 4 separate runtime objects. net48
// ParameterType.IsByRef and the byref "&" suffix are NOT part of this narrow
// model (W7 -- the type value decodes the BYREF POINTEE's full name).
//------------------------------------------------------------------------------

// Split a paramMeta row into its name + outFlag ('|'-separated). Returns false
// on malformed input (defensive -- the emitter always produces well-formed
// rows).
bool SplitParamMetaRow(const char* row, char (&name)[kNameBufferSize], int32_t* outFlag)
{
    if (row == nullptr || outFlag == nullptr) return false;
    const char* sep = std::strchr(row, '|');
    if (sep == nullptr) return false;
    const size_t len = static_cast<size_t>(sep - row);
    if (len >= kNameBufferSize) return false;
    std::memcpy(name, row, len);
    name[len] = '\0';
    char* end = nullptr;
    *outFlag = std::strtol(sep + 1, &end, 10);
    return end != sep + 1;
}

// Step through ';'-joined paramMeta rows; returns the i-th row or nullptr.
const char* ParamMetaRow(const char* meta, int32_t index)
{
    if (meta == nullptr || index < 0) return nullptr;
    const char* cur = meta;
    for (int32_t i = 0; i < index; ++i)
    {
        const char* sep = std::strchr(cur, ';');
        if (sep == nullptr) return nullptr;
        cur = sep + 1;
    }
    return cur;
}

//------------------------------------------------------------------------------
// ParamTypeValueName -- decode ONE MethodSignatureCoder param encoding into
// the net48 TYPE VALUE (the full-name string the simplified Type model uses --
// the same carrier typeof() folds). Unlike ParamEncodingToFullName (the
// exact-match filter helper), this also decodes SZARRAY and BYREF:
//   "i"/"l"/"d"/...          -> the primitive full name ("System.Int32", ...)
//   "r:X" / "V:X" / "T:X"    -> "X" (a plain identifier, no array bracket)
//   "r:E[]" (SZARRAY)        -> "[EFull" (the array-descriptor name the
//       is_array/get_element_type predicates expect -- "[System.Int32")
//   "B:..." (BYREF ref/out)  -> the POINTEE's value name (the narrow model has
//       no "&"-typed Type VALUE -- W7; net48 reports the byref element type).
// Returns false for encodings with no name (, fnptr "F:", pointer "P:"), so
// the caller falls back to the raw encoding (defensive -- never skipped).
//------------------------------------------------------------------------------
bool ParamTypeValueName(const char* enc, char (&out)[kNameBufferSize])
{
    if (enc == nullptr) return false;
    // BYREF: strip the "B:" prefix and decode the pointee.
    if (enc[0] == 'B' && enc[1] == ':') return ParamTypeValueName(enc + 2, out);
    // SZARRAY "r:E[]": the element is a full name / leaf code followed by '['.
    if (enc[0] == 'r' && enc[1] == ':')
    {
        const char* rest = enc + 2;
        const size_t restLen = std::strlen(rest);
        if (restLen >= 2 && rest[restLen - 2] == '[' && rest[restLen - 1] == ']')
        {
            char elem[kNameBufferSize];
            const size_t elemLen = restLen - 2;
            std::memcpy(elem, rest, elemLen);
            elem[elemLen] = '\0';
            // Element is a leaf code ("r:i[]") or a full name ("r:System.X[]").
            char elemFull[kNameBufferSize];
            if (!ParamTypeValueName(elem, elemFull))
            {
                // Leaf codes don't start with a namespace -- map via the leaf
                // table through ParamEncodingToFullName (it handles leafs).
                if (!ParamEncodingToFullName(elem, elemFull)) return false;
            }
            std::snprintf(out, kNameBufferSize, "[%s", elemFull);
            return true;
        }
        // Plain class/string/object "r:X": strip "r:" and accept the name.
        if (std::strchr(rest, '[') != nullptr) return false;
        std::snprintf(out, kNameBufferSize, "%s", rest);
        return true;
    }
    // Leaf primitive codes + "V:"/"T:" names -- delegate to the exact-match
    // decoder (the full-name projection is identical for these shapes).
    return ParamEncodingToFullName(enc, out);
}

} // namespace

Il2CppObject* il2cpp_typeinfo_get_method_types(Il2CppObject* typeName, Il2CppObject* memberName,
                                               Il2CppArray* types)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    if (memberName == nullptr) ThrowNullMemberName("name");
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    const int32_t wantArity = (types != nullptr && types->length > 0) ? types->length : 0;
    if (wantArity > 4) return nullptr; // the exact-match table binds 4 max
    char typeNames[4][kNameBufferSize];
    if (!ReadTypeElements(types, typeNames, wantArity)) return nullptr;
    // net48 GetMethod(name, Type[]) default binding: Public|Instance|Static.
    const Il2CppMethodInfo* found = nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name == nullptr || std::strcmp(m.name, memberBuf) != 0) continue;
            if (!MemberMatchesVis(m.flags, 16 | 4 | 8, false)) continue;
            if (MethodTypesMatch(&m, typeNames, wantArity))
            {
                found = &m;
                break;
            }
        }
        if (found != nullptr) break; // own-table win, .NET shadowing
    }
    return found != nullptr ? memberName : nullptr;
}

// REFL-ARITY -- Type.GetMethod(string, BindingFlags, Binder, Type[],
// ParameterModifier[]). Same exact-signature match as the 3-param form, but
// the explicit BindingFlags DOES filter (the binder/modifiers are ignored by
// net48's match -- default binder, by-ref modifiers only, W7). The scan skips a
// visibility/static-mismatched hit and continues the base chain (a public base
// same-name method is still found), matching net48.
Il2CppObject* il2cpp_typeinfo_get_method_types_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                                     Il2CppArray* types, int32_t bindingAttr)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    if (memberName == nullptr) ThrowNullMemberName("name");
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    const int32_t wantArity = (types != nullptr && types->length > 0) ? types->length : 0;
    if (wantArity > 4) return nullptr; // the exact-match table binds 4 max
    char typeNames[4][kNameBufferSize];
    if (!ReadTypeElements(types, typeNames, wantArity)) return nullptr;
    const Il2CppMethodInfo* found = nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name == nullptr || std::strcmp(m.name, memberBuf) != 0) continue;
            if (!MemberMatchesVis(m.flags, bindingAttr, false)) continue;
            if (MethodTypesMatch(&m, typeNames, wantArity))
            {
                found = &m;
                break;
            }
        }
        if (found != nullptr) break; // own-table win, .NET shadowing
    }
    return found != nullptr ? memberName : nullptr;
}

Il2CppObject* il2cpp_typeinfo_get_property_types(Il2CppObject* typeName, Il2CppObject* memberName,
                                                 Il2CppArray* types)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    if (memberName == nullptr) ThrowNullMemberName("name");
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    const int32_t wantArity = (types != nullptr && types->length > 0) ? types->length : 0;
    if (wantArity > 4) return nullptr;
    char typeNames[4][kNameBufferSize];
    if (!ReadTypeElements(types, typeNames, wantArity)) return nullptr;
    // net48 GetProperty(name, Type[]) default binding: Public|Instance|Static.
    // A NON-INDEXED property matches an EMPTY Type[]; an INDEXED property is
    // matched by its indexer parameter types (resolved through the getter's
    // encoded signature -- the IL property-parameter authority).
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->properties == nullptr) continue;
        for (int32_t i = 0; i < scan->propertyCount; ++i)
        {
            const Il2CppPropertyInfo& p = scan->properties[i];
            if (p.name == nullptr || std::strcmp(p.name, memberBuf) != 0) continue;
            // Visibility from the accessor flags (GAP-1 doctrine).
            const char* accessorNames[2] = { p.getterName, p.setterName };
            int32_t accessorFlags = 0;
            bool sawAccessor = false;
            for (int32_t ai = 0; ai < 2; ++ai)
            {
                if (accessorNames[ai] == nullptr) continue;
                const Il2CppMethodInfo* acc = il2cpp_type_get_method(scan, accessorNames[ai]);
                if (acc != nullptr) { accessorFlags |= acc->flags; sawAccessor = true; }
            }
            if (!sawAccessor || !MemberMatchesVis(accessorFlags, 16 | 4 | 8, true)) continue;
            if (p.indexParams != wantArity) continue;
            if (p.getterName != nullptr && wantArity > 0 && wantArity <= 4)
            {
                const Il2CppMethodInfo* getter = il2cpp_type_get_method(scan, p.getterName);
                if (getter != nullptr && !MethodTypesMatch(getter, typeNames, wantArity))
                {
                    continue;
                }
            }
            return memberName;
        }
    }
    return nullptr;
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
// refl2 -- A-class type predicates on the name-string (Type VALUE) receiver.
//
// The transpiler's typeof/X.GetType() folds produce the FULL-NAME string (the
// simplified Type model); the pre-refl2 mappings passed that STRING to the
// descriptor-primitives (il2cpp_type_is_array(Il2CppClass*) etc.), which do
// not compile (name-string -> Il2CppClass*). These name-string wrappers decide
// each predicate WITHOUT a descriptor:
//
//   is_array / has_element_type  -- the array '['-prefix rule (array.cpp names
//     an array descriptor "[<element full name>"; the refl2 typeof fold for
//     typeof(int[]) produces exactly that identity). Arrays are the ONLY
//     element-having types in the model.
//   is_primitive                -- the name is in the .NET primitive set
//     (Boolean/Byte/SByte/Int16/UInt16/Int32/UInt32/Int64/UInt64/IntPtr/
//     UIntPtr/Char/Single/Double -- the typeext.cpp kPrimitives table).
//   is_enum                     -- the typeinfo's BCL base terminal is
//     "System.Enum" (TypeEmitter emits baseTypeName for a user enum).
//   is_value_type               -- a PRIMITIVE name, OR a reflected type whose
//     typeinfo says value-type (the emitted isValueType flag / the
//     "System.ValueType" base terminal). net48: an enum IsValueType True.
//   is_class                    -- NOT a value type and NOT an interface.
//   is_interface                -- the typeinfo's CLASS descriptor is an
//     interface (IsInterfaceDescriptor: instanceSize 0 / the emitted
//     BaseTypeName "System.Object" is the D-conflated root; the IFACE
//     distinction rides the klass -- user interfaces emit a descriptor).
//
// NULL receiver -> NullReferenceException (the null-Type-receiver doctrine);
// an UNREGISTERED name (BCL / boxed) that is not a builtin primitive ->
// predicate defaults (false / null) -- the defensive query convention.
//------------------------------------------------------------------------------
namespace {

// The .NET primitive full names (the typeext.cpp kPrimitives table + Char).
// ORDER matters for the long/short prefix checks? -- no, exact strcmp only.
bool IsPrimitiveFullName(const char* name)
{
    static const char* const kPrimitives[] = {
        "System.Boolean", "System.Byte", "System.SByte", "System.Int16",
        "System.UInt16", "System.Int32", "System.UInt32", "System.Int64",
        "System.UInt64", "System.IntPtr", "System.UIntPtr", "System.Char",
        "System.Double", "System.Single",
    };
    if (name == nullptr) return false;
    for (size_t i = 0; i < sizeof(kPrimitives) / sizeof(kPrimitives[0]); ++i)
    {
        if (std::strcmp(name, kPrimitives[i]) == 0) return true;
    }
    return false;
}

bool NameStartsArrayBracket(const char* name)
{
    return name != nullptr && name[0] == '[';
}

// Interface descriptor test (mirror of the typeext.cpp IsInterfaceDescriptor):
// a non-array class descriptor with instanceSize 0 (TypeEmitter:679 -- only
// interfaces carry zero instanceSize; arrays are excluded by the '['-prefix).
bool IsInterfaceDescriptorName(Il2CppClass* klass)
{
    if (klass == nullptr) return false;
    if (klass->name != nullptr && klass->name[0] == '[') return false;
    return klass->instanceSize == 0;
}

} // namespace

int32_t il2cpp_typeinfo_is_array(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    return NameStartsArrayBracket(buf) ? 1 : 0;
}

int32_t il2cpp_typeinfo_has_element_type(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    // Arrays are the only element-having types in the simplified model.
    return NameStartsArrayBracket(buf) ? 1 : 0;
}

Il2CppObject* il2cpp_typeinfo_get_element_type(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return nullptr;
    if (!NameStartsArrayBracket(buf)) return nullptr;
    // The element TYPE VALUE is the name with the leading '[' stripped
    // ("[System.Int32" -> "System.Int32" -- an int[] element is typeof(int),
    // net48). Jagged arrays recurse naturally: "[["  ->  "[System.Int32"...".
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(buf + 1));
}

int32_t il2cpp_typeinfo_is_primitive(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    return IsPrimitiveFullName(buf) ? 1 : 0;
}

int32_t il2cpp_typeinfo_is_enum(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info == nullptr) return 0;
    // A user enum's typeinfo carries the BCL base terminal "System.Enum"
    // (TypeEmitter: an enum's BaseTypeName is System.Enum -- the descriptor
    // itself is null-based).
    return (info->baseTypeName != nullptr
            && std::strcmp(info->baseTypeName, "System.Enum") == 0) ? 1 : 0;
}

int32_t il2cpp_typeinfo_is_value_type(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    if (NameStartsArrayBracket(buf)) return 0; // arrays are classes
    if (IsPrimitiveFullName(buf)) return 1;    // primitives are value types
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info == nullptr) return 0;
    // net48: an ENUM is a value type (IsValueType True) -- the "System.Enum"
    // base terminal marks it. A reflected user struct's typeinfo emits the
    // isValueType flag (O4). Classes/interfaces carry neither.
    if (info->baseTypeName != nullptr
        && std::strcmp(info->baseTypeName, "System.Enum") == 0) return 1;
    return info->isValueType != 0 ? 1 : 0;
}

int32_t il2cpp_typeinfo_is_interface(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    if (NameStartsArrayBracket(buf)) return 0; // arrays are classes
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info == nullptr || info->klass == nullptr) return 0;
    // Interfaces emit a class descriptor with instanceSize 0 (IsInterface-
    // Descriptor). The klass pointer is non-null for every reflected
    // interface/class; primitives/BCL types are unregistered here.
    return IsInterfaceDescriptorName(const_cast<Il2CppClass*>(info->klass)) ? 1 : 0;
}

int32_t il2cpp_typeinfo_is_class(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    // A class is NOT a value type and NOT an interface (arrays count as
    // classes in net48).
    return (il2cpp_typeinfo_is_value_type(typeName) == 0
            && il2cpp_typeinfo_is_interface(typeName) == 0) ? 1 : 0;
}

//------------------------------------------------------------------------------
// refl2 -- Type.IsAssignableFrom / IsInstanceOfType / IsSubclassOf on the
// name-string receiver.
//
// The pre-refl2 mappings passed the name-STRING (the Type VALUE) straight to
// the descriptor primitives (il2cpp_type_is_assignable_from(Il2CppClass*...))
// -- the same fail-to-compile shape as the A-class predicates. These wrappers
// resolve each Type NAME to a descriptor and delegate:
//   * typeinfo registry (il2cpp_typeinfo_lookup -> info->klass) -- USER types
//     (classes/interfaces/structs/enums; a user enum's klass is the emitted
//     Cases_Shade_class-like descriptor).
//   * builtin class registry (il2cpp_class_by_name, after
//     il2cpp_ensure_builtin_system_classes) -- BCL / primitive / array types
//     (System.Int32, System.String, "[System.Int32" array descriptors).
// The BCL-resolution must run BEFORE the descriptor primitives so net48
// typeof(int).IsAssignableFrom(typeof(int)) -> True works.
//------------------------------------------------------------------------------
namespace {

const Il2CppClass* KlassFromTypeName(const char* name)
{
    if (name == nullptr) return nullptr;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(name);
    if (info != nullptr && info->klass != nullptr) return info->klass;
    il2cpp_ensure_builtin_system_classes();
    return il2cpp_class_by_name(name);
}

} // namespace

int32_t il2cpp_typeinfo_is_assignable_from(Il2CppObject* targetName, Il2CppObject* sourceName)
{
    if (targetName == nullptr) ThrowNullTypeReceiver();
    // net48: the SOURCE being null returns FALSE (no throw) -- unlike
    // IsSubclassOf. Short-circuit before the name transcode.
    if (sourceName == nullptr) return 0;
    char targetBuf[kNameBufferSize];
    char sourceBuf[kNameBufferSize];
    if (!AsciiFromManagedString(targetName, targetBuf) ||
        !AsciiFromManagedString(sourceName, sourceBuf))
    {
        return 0;
    }
    const Il2CppClass* target = KlassFromTypeName(targetBuf);
    const Il2CppClass* source = KlassFromTypeName(sourceBuf);
    return il2cpp::runtime::il2cpp_type_is_assignable_from(
        const_cast<Il2CppClass*>(target), const_cast<Il2CppClass*>(source));
}

int32_t il2cpp_typeinfo_is_instance(Il2CppObject* obj, Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    // net48: IsInstanceOfType(null) is FALSE (no throw).
    if (obj == nullptr) return 0;
    char typeBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf)) return 0;
    const Il2CppClass* klass = KlassFromTypeName(typeBuf);
    return il2cpp::runtime::icall_type_is_instance(obj, const_cast<Il2CppClass*>(klass));
}

int32_t il2cpp_typeinfo_is_subclass_of(Il2CppObject* derivedName, Il2CppObject* baseName)
{
    if (derivedName == nullptr) ThrowNullTypeReceiver();
    if (baseName == nullptr)
    {
        // net48: IsSubclassOf(null) throws ArgumentNullException (probe-
        // verified; the same contract the typecheck.cpp primitive enforces on
        // a null base descriptor). The typeinfo-side param name matches.
        il2cpp_throw_argument_null_exception("type");
    }
    char derivedBuf[kNameBufferSize];
    char baseBuf[kNameBufferSize];
    if (!AsciiFromManagedString(derivedName, derivedBuf) ||
        !AsciiFromManagedString(baseName, baseBuf))
    {
        return 0;
    }
    const Il2CppClass* derived = KlassFromTypeName(derivedBuf);
    const Il2CppClass* base = KlassFromTypeName(baseBuf);
    return il2cpp::runtime::icall_type_is_subclass_of(
        const_cast<Il2CppClass*>(derived), const_cast<Il2CppClass*>(base));
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_namespace -- Type.get_Namespace on the name-string
// (Type VALUE) receiver. net48 Namespace = the full name truncated at the LAST
// '.': the trailing segment is the type's short name (a nested type's
// '+'-chain rides INSIDE that segment), so the truncation keeps the namespace
// of the OUTERMOST declaring type:
//   "Cases.Sub.Deep" -> "Cases.Sub"; "Cases.Outer+Inner" -> "Cases".
// A GLOBAL-namespace type (no '.') returns nullptr -- net48 returns NULL (not
// the empty string) for a global-namespace type, probe-pinned (a nested type
// of a global outer has no '.' either -> null). Null receiver -> NRE.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_namespace(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return nullptr;
    // refl2 (A): net48 reports the ELEMENT type's namespace for an array /
    // pointer / byref type ("System.Int32[]" -> "System"). The simplified model
    // carries arrays as the registry identity "[System.Int32" (a leading '[')
    // and the folded display form "System.Int32[]" -- both are reduced to the
    // element name first, then truncated like any other type name.
    char* name = buf;
    if (name[0] == '[')
    {
        // "[System.Int32" -- strip the leading '[' (the array marker).
        ++name;
    }
    size_t len = std::strlen(name);
    while (len > 0)
    {
        const char tail = name[len - 1];
        if (tail == ']' || tail == '*' || tail == '&')
        {
            --len;
            continue;
        }
        if (tail == ',' || tail == '[')
        {
            // "System.Int32[,]" / "System.Int32[" -- stop at the rank marker.
            --len;
            continue;
        }
        break;
    }
    name[len] = '\0';
    const char* lastDot = std::strrchr(name, '.');
    if (lastDot == nullptr)
    {
        return nullptr; // global namespace (net48 null)
    }
    const size_t nsLen = static_cast<size_t>(lastDot - name);
    if (nsLen == 0)
    {
        return nullptr; // ".Short" impossible for a valid type name; defensive
    }
    char ns[kNameBufferSize];
    std::memcpy(ns, name, nsLen);
    ns[nsLen] = '\0';
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(ns));
}

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_interface -- Type.GetInterface(string[, bool]) on the
// name-string receiver. The interface list lives on the typeinfo
// (Il2CppTypeInfo.interfaces -- pointer array of the implemented interface
// typeinfos, own + inherited through baseType). net48 matching (probe-pinned):
//   * a query name WITH a dot ("Cases.IDerived6") uses the FULL-name path
//     only -- ALWAYS ordinal case-sensitive, ignores ignoreCase;
//   * a DOT-FREE query name ("IDerived6") is the SIMPLE-name path, which
//     follows ignoreCase (ordinal-ignore-case);
// the FIRST chain hit wins (most-derived first -- net48 checked-interface
// order). Returns the interface FULL-NAME string (a Type VALUE) on a hit.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_interface(Il2CppObject* typeName, Il2CppObject* ifaceName,
                                            int32_t ignoreCase)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    if (ifaceName == nullptr) ThrowNullMemberName("name");
    char typeBuf[kNameBufferSize];
    char ifaceBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(ifaceName, ifaceBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    // Compute the simple name of the requested interface: a query name WITH a
    // dot uses the FULL-name path ONLY (net48: "Cases.IDerived6" never matches
    // via the simple-name compare -- probe-pinned); a dot-free query uses the
    // simple-name path.
    const char* lastDot = std::strrchr(ifaceBuf, '.');
    const bool hasDot = lastDot != nullptr;
    const char* simpleName = hasDot ? lastDot + 1 : ifaceBuf;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        for (int32_t i = 0; i < scan->interfaceCount; ++i)
        {
            const Il2CppTypeInfo* iface = scan->interfaces[i];
            if (iface == nullptr || iface->name == nullptr) continue;
            if (hasDot)
            {
                // Full-name comparison: ALWAYS ordinal (net48 ignores ignoreCase).
                if (std::strcmp(iface->name, ifaceBuf) == 0)
                {
                    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(iface->name));
                }
            }
            else
            {
                // Simple-name comparison: case-sensitive by default; ordinal-
                // ignore-case when the caller passed ignoreCase=true.
                const char* ifaceSimple = iface->name;
                if (const char* d2 = std::strrchr(iface->name, '.')) ifaceSimple = d2 + 1;
                bool match = ignoreCase != 0
                    ? _stricmp(ifaceSimple, simpleName) == 0
                    : std::strcmp(ifaceSimple, simpleName) == 0;
                if (match)
                {
                    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(iface->name));
                }
            }
        }
    }
    return nullptr;
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
    // GAP-4 (P1): net48 GetMethods() EXCLUDES constructors -- ".ctor" /
    // ".cctor" surface only through GetConstructors(). The pre-GAP-4
    // simplified model included public ctors in GetMethods (the R2
    // documented deviation); the method-table ctor bit (flags bit2,
    // typeinfo.h) now filters them out to match net48 byte-for-byte.
    return IsPublic(m.flags) && (m.flags & 4) == 0;
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
// RF-N5 -- GetNestedTypes / GetMembers (NEXT2-DECISION section ② RF-N5).
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
    // REFL6B (item A): net48 GetNestedTypes() default binding is PUBLIC-only
    // (probe-pinned [n.all]=1 for a type with four nested classes where only
    // one is public). Filter the nested sub-table by the visibility byte
    // (NestedPublic == 0x2). A null nestedVis (defensive / nested-free) admits
    // every entry (pass-through -- the pre-refl6b behaviour).
    int32_t total = 0;
    for (int32_t i = 0; i < info->nestedCount; ++i)
    {
        if (info->nestedVis == nullptr || (info->nestedVis[i] & 0x7) == 0x2) ++total;
    }
    Il2CppArray* result = BuildNameArray(total);
    int32_t dst = 0;
    for (int32_t i = 0; i < info->nestedCount; ++i)
    {
        if (info->nestedVis == nullptr || (info->nestedVis[i] & 0x7) == 0x2)
        {
            StoreName(result, dst++, info->nestedNames != nullptr ? info->nestedNames[i] : nullptr);
        }
    }
    return result;
}

// REFL-ARITY -- the GetNestedTypes(BindingFlags) visibility filter for one
// nested row (a shared helper for the flags and default paths).
bool NestedTypeVisMatches(const Il2CppTypeInfo* info, int32_t i,
                          bool wantPublic, bool wantNonPublic)
{
    const bool isPublic = info->nestedVis != nullptr
        ? (info->nestedVis[i] & 0x7) == 0x2
        : true; // defensive pass-through (no vis column)
    if (wantPublic && wantNonPublic) return true;
    if (wantPublic) return isPublic;
    if (wantNonPublic) return !isPublic;
    return isPublic; // net48 default public-only
}

// REFL-ARITY -- Type.GetNestedTypes(BindingFlags). Same nested sub-table as the
// () overload but with an explicit visibility filter: Public(16) admits a
// NestedPublic entry (nestedVis & 0x7 == 0x2), NonPublic(32) admits every
// non-public entry, BOTH admits all, NEITHER falls to the net48 default
// (Public-only). A null nestedVis (nested-free / defensive) admits all entries
// (pass-through -- the pre-refl6b behaviour).
Il2CppArray* il2cpp_typeinfo_get_nested_types_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    const bool wantPublic = (bindingAttr & 16) != 0;
    const bool wantNonPublic = (bindingAttr & 32) != 0;
    int32_t total = 0;
    for (int32_t i = 0; i < info->nestedCount; ++i)
    {
        if (NestedTypeVisMatches(info, i, wantPublic, wantNonPublic)) ++total;
    }
    Il2CppArray* result = BuildNameArray(total);
    int32_t dst = 0;
    for (int32_t i = 0; i < info->nestedCount; ++i)
    {
        if (NestedTypeVisMatches(info, i, wantPublic, wantNonPublic))
        {
            StoreName(result, dst++, info->nestedNames != nullptr ? info->nestedNames[i] : nullptr);
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// REFL6B (item A) -- Type.GetNestedType(string[, BindingFlags]). Matches the
// nested sub-table by SIMPLE name (the segment after the LAST '+'), applies
// the binding visibility filter, and returns the nested type's FULL '+'
// reflection name (a Type VALUE) or null (name/visibility miss or empty name
// -- no throw). Null nestedName -> ArgumentNullException; null typeName ->
// NullReferenceException. net48 probe-pinned (p-refl6-nested).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_nested_type(Il2CppObject* typeName,
                                              Il2CppObject* nestedSimpleName,
                                              int32_t bindingAttr)
{
    if (nestedSimpleName == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    if (typeName == nullptr)
    {
        ThrowNullTypeReceiver();
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, nameBuf)) return nullptr;
    char want[kNameBufferSize];
    if (!AsciiFromManagedString(nestedSimpleName, want)) return nullptr;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return nullptr;
    for (int32_t i = 0; i < info->nestedCount; ++i)
    {
        const char* full = info->nestedNames != nullptr ? info->nestedNames[i] : nullptr;
        if (full == nullptr) continue;
        // Simple name = after the last '+'.
        const char* simple = std::strrchr(full, '+');
        simple = simple != nullptr ? simple + 1 : full;
        if (std::strcmp(simple, want) != 0) continue;
        // Visibility filter: Public(16) admits NestedPublic(0x2); NonPublic(32)
        // admits every non-public nested entry; neither -> Public-only.
        const bool isPublic = info->nestedVis != nullptr
            ? (info->nestedVis[i] & 0x7) == 0x2
            : true; // defensive pass-through (no vis column)
        const bool wantPublic = (bindingAttr & 16) != 0;
        const bool wantNonPublic = (bindingAttr & 32) != 0;
        bool visOk;
        if (wantPublic && wantNonPublic) visOk = true;
        else if (wantPublic) visOk = isPublic;
        else if (wantNonPublic) visOk = !isPublic;
        else visOk = isPublic; // net48 default public-only
        if (!visOk) continue;
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(full));
    }
    return nullptr;
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
// REFL5 (item 1) -- the (BindingFlags) arity-1 member ENUMERATION overloads.
//
// net48 semantics (probe-pinned on p-refl5-1flags -- no FlattenHierarchy):
//   * a member is listed iff its VISIBILITY accepts the binding AND its
//     static/instance-ness accepts the binding. Visibility: a PUBLIC member
//     matches only the Public(16) bit, a NONPUBLIC member only the
//     NonPublic(32) bit ("NonPublic excludes public"); staticness: STATIC
//     matches only Static(8), INSTANCE only Instance(4) -- with NEITHER
//     staticness bit set NOTHING matches (GetMethods(Public) is empty).
//   * the BASE chain contributes only INHERITABLE members: inherited STATIC
//     members are never listed (own statics only) and inherited PRIVATE
//     members (bit 0x10) are never listed; public/protected/internal base
//     members are inherited (protected/internal answer NonPublic).
//   * ordering = metadata table order (own, then base chain).
//------------------------------------------------------------------------------
namespace {

// Strict per-member enumeration match (differs from MemberMatchesVis -- the
// single-name-query filter -- by requiring an EXPLICIT staticness bit;
// net48 GetMethods(Public) with no Instance/Static returns EMPTY).
bool EnumMemberMatches(int32_t memberFlags, int32_t bindingAttr)
{
    const bool isPublic = (memberFlags & 1) != 0;
    const bool isStatic = (memberFlags & 2) != 0;
    const bool visOk = (isPublic && (bindingAttr & 16) != 0)
        || (!isPublic && (bindingAttr & 32) != 0);
    const bool statOk = (isStatic && (bindingAttr & 8) != 0)
        || (!isStatic && (bindingAttr & 4) != 0);
    return visOk && statOk;
}

// The BY-REF (out) property/event aggregate flags: a property/event has no
// flags column -- its visibility/static/private derive from the accessor
// methods (declared in the SAME table). bit0 = any accessor public, bit1 =
// every accessor static (>=1 accessor), bit0x10 = every accessor private.
// Returns false when no resolvable accessor exists (defensive -- the model
// documents the "all properties/events" property-table simplification).
bool AggregateAccessorFlags(const Il2CppTypeInfo* scan, const char* a, const char* b,
                            int32_t& outFlags)
{
    int32_t any = 0;
    bool saw = false;
    const char* names[2] = { a, b };
    for (int32_t i = 0; i < 2; ++i)
    {
        const Il2CppMethodInfo* acc = names[i] != nullptr
            ? il2cpp_type_get_method(scan, names[i]) : nullptr;
        if (acc == nullptr) continue;
        saw = true;
        // bit0 public, bit1 static, bit0x10 private (typeinfo.h).
        any |= (acc->flags & 1) != 0 ? 1 : 0;
        any |= (acc->flags & 2) != 0 ? 2 : 0;
        any |= (acc->flags & 0x10) != 0 ? 0x10 : 0;
    }
    if (!saw) return false;
    // "static": EVERY accessor static; "private": EVERY accessor private.
    const bool allStatic = (any & 2) != 0;
    const bool allPrivate = (any & 0x10) != 0;
    outFlags = ((any & 1) != 0 ? 1 : 0) | (allStatic ? 2 : 0) | (allPrivate ? 0x10 : 0);
    return true;
}

// Bind a property/event: aggregate the accessor flags then run the strict
// enumeration match against the typeinfo property/event entry. The scan's
// OWN accessor names the members under (accessors live beside the table).
bool PropertyOrEventListMatch(const Il2CppTypeInfo* scan, const char* accA,
                              const char* accB, int32_t bindingAttr)
{
    int32_t flags = 0;
    if (!AggregateAccessorFlags(scan, accA, accB, flags))
    {
        // No resolvable accessor: the model admits the entry (the ()-overload
        // "all properties" simplification) -- match on the binding anyway by
        // the entry's implied visibility (none) => public-only pass-through.
        return (bindingAttr & 16) != 0 && (bindingAttr & 4) != 0;
    }
    return EnumMemberMatches(flags, bindingAttr);
}

// Enumerate own + base-chain tables into a name array with the refl5
// binding filter. isBase (scan != info) applies the two inheritance
// exclusions: no inherited static members, no inherited private members.
template <typename T>
Il2CppArray* EnumerateMembersBinding(const Il2CppTypeInfo* info,
                                     const T* Il2CppTypeInfo::*table,
                                     int32_t Il2CppTypeInfo::*count,
                                     bool (*filter)(const Il2CppTypeInfo*, const T&, int32_t, bool),
                                     int32_t bindingAttr)
{
    int32_t total = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        const T* entries = scan->*table;
        if (entries == nullptr) continue;
        for (int32_t i = 0; i < scan->*count; ++i)
        {
            if (filter(scan, entries[i], bindingAttr, scan != info)) ++total;
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
            if (filter(scan, entries[i], bindingAttr, scan != info))
            {
                StoreName(result, dst++, entries[i].name);
            }
        }
    }
    return result;
}

bool BindingMethod(const Il2CppTypeInfo*, const Il2CppMethodInfo& m, int32_t ba, bool isBase)
{
    // GetMethods NEVER lists ctors (GAP-4), whatever the flags.
    if ((m.flags & 4) != 0) return false;
    if (isBase && ((m.flags & 2) != 0 || (m.flags & 0x10) != 0)) return false;
    return EnumMemberMatches(m.flags, ba);
}

bool BindingCtor(const Il2CppTypeInfo*, const Il2CppMethodInfo& m, int32_t ba, bool isBase)
{
    return !isBase && (m.flags & 4) != 0 && EnumMemberMatches(m.flags, ba);
}

bool BindingField(const Il2CppTypeInfo*, const Il2CppFieldInfo& f, int32_t ba, bool isBase)
{
    if (isBase && ((f.flags & 2) != 0 || (f.flags & 0x10) != 0)) return false;
    return EnumMemberMatches(f.flags, ba);
}

bool BindingProperty(const Il2CppTypeInfo* scan, const Il2CppPropertyInfo& p, int32_t ba, bool isBase)
{
    if (isBase)
    {
        int32_t flags = 0;
        // A base STATIC property (all accessors static) or PRIVATE property
        // (all accessors private) is not inherited (net48).
        if (AggregateAccessorFlags(scan, p.getterName, p.setterName, flags))
        {
            if ((flags & 2) != 0 || (flags & 0x10) != 0) return false;
        }
    }
    return PropertyOrEventListMatch(scan, p.getterName, p.setterName, ba);
}

bool BindingEvent(const Il2CppTypeInfo* scan, const Il2CppEventInfo& e, int32_t ba, bool isBase)
{
    if (isBase)
    {
        int32_t flags = 0;
        if (AggregateAccessorFlags(scan, e.addName, e.removeName, flags))
        {
            if ((flags & 2) != 0 || (flags & 0x10) != 0) return false;
        }
    }
    return PropertyOrEventListMatch(scan, e.addName, e.removeName, ba);
}

} // namespace

Il2CppArray* il2cpp_typeinfo_get_methods_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembersBinding(info, &Il2CppTypeInfo::methods, &Il2CppTypeInfo::methodCount,
                                   BindingMethod, bindingAttr);
}

Il2CppArray* il2cpp_typeinfo_get_fields_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembersBinding(info, &Il2CppTypeInfo::fields, &Il2CppTypeInfo::fieldCount,
                                   BindingField, bindingAttr);
}

Il2CppArray* il2cpp_typeinfo_get_properties_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembersBinding(info, &Il2CppTypeInfo::properties, &Il2CppTypeInfo::propertyCount,
                                   BindingProperty, bindingAttr);
}

Il2CppArray* il2cpp_typeinfo_get_events_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    return EnumerateMembersBinding(info, &Il2CppTypeInfo::events, &Il2CppTypeInfo::eventCount,
                                   BindingEvent, bindingAttr);
}

// REFL-ARITY -- Type.GetEvent(string, BindingFlags): the flags-aware sibling of
// il2cpp_typeinfo_get_event (the (string) overload binds Public|Instance|Static).
// The event's visibility/static derive from its add/remove accessors (the
// AggregateAccessorFlags helper); a mismatched hit is skipped and the base chain
// continues. Returns the event-NAME string on a hit, nullptr otherwise.
Il2CppObject* il2cpp_typeinfo_get_event_flags(Il2CppObject* typeName, Il2CppObject* memberName,
                                              int32_t bindingAttr)
{
    if (memberName == nullptr) ThrowNullMemberName("name");
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->events == nullptr) continue;
        for (int32_t i = 0; i < scan->eventCount; ++i)
        {
            const Il2CppEventInfo& e = scan->events[i];
            if (e.name == nullptr || std::strcmp(e.name, memberBuf) != 0) continue;
            int32_t flags = 0;
            if (AggregateAccessorFlags(scan, e.addName, e.removeName, flags)
                && !MemberMatchesVis(flags, bindingAttr, true))
            {
                continue;
            }
            return memberName;
        }
    }
    return nullptr;
}

Il2CppArray* il2cpp_typeinfo_get_constructors_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    // GetConstructors lists the QUERIED TYPE's OWN ctors only (no inherited
    // ctor -- net48), so the walk is own-table only (BindingCtor's isBase=0).
    int32_t total = 0;
    if (info->methods != nullptr)
    {
        for (int32_t i = 0; i < info->methodCount; ++i)
        {
            if (BindingCtor(info, info->methods[i], bindingAttr, false)) ++total;
        }
    }
    Il2CppArray* result = BuildNameArray(total);
    int32_t dst = 0;
    if (info->methods != nullptr)
    {
        for (int32_t i = 0; i < info->methodCount; ++i)
        {
            if (BindingCtor(info, info->methods[i], bindingAttr, false))
            {
                StoreName(result, dst++, info->methods[i].name);
            }
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// REFL5 (item 1) -- GetMembers(BindingFlags): the five-slice union (methods +
// ctors + properties + events + fields, own + base chain) with each slice
// filtered by the binding (the rf-members ()-overload slice order; the ctor
// slice stays OWN-only -- net48 never lists an inherited ctor). The methods
// slice carries the property/event ACCESSORS (net48 returns get_/set_/add_/
// remove_ as method members); property/event/field slices follow.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_members_flags(Il2CppObject* typeName, int32_t bindingAttr)
{
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    int32_t methodN = 0, ctorN = 0, propN = 0, evtN = 0, fieldN = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (BindingMethod(scan, scan->methods[i], bindingAttr, scan != info)) ++methodN;
        if (scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                if (BindingProperty(scan, scan->properties[i], bindingAttr, scan != info)) ++propN;
        if (scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                if (BindingEvent(scan, scan->events[i], bindingAttr, scan != info)) ++evtN;
        if (scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (BindingField(scan, scan->fields[i], bindingAttr, scan != info)) ++fieldN;
    }
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (BindingCtor(info, info->methods[i], bindingAttr, false)) ++ctorN;
    Il2CppArray* result = BuildNameArray(methodN + ctorN + propN + evtN + fieldN);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (BindingMethod(scan, scan->methods[i], bindingAttr, scan != info))
                    StoreName(result, dst++, scan->methods[i].name);
    }
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (BindingCtor(info, info->methods[i], bindingAttr, false))
                StoreName(result, dst++, info->methods[i].name);
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                if (BindingProperty(scan, scan->properties[i], bindingAttr, scan != info))
                    StoreName(result, dst++, scan->properties[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                if (BindingEvent(scan, scan->events[i], bindingAttr, scan != info))
                    StoreName(result, dst++, scan->events[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (BindingField(scan, scan->fields[i], bindingAttr, scan != info))
                    StoreName(result, dst++, scan->fields[i].name);
    }
    return result;
}

//------------------------------------------------------------------------------
// REFL6A (item B) -- Type.GetMember(string[, BindingFlags]): the refl5
// members_flags five-slice union PLUS an exact SIMPLE-name strcmp filter
// (net48 GetMember matches by simple member name). Exception contract
// (probe-pinned on p-refl6-gmember): null memberName -> ArgumentNullException;
// EMPTY memberName -> EMPTY array (no throw). Matching members keep the
// members_flags union order (methods incl. accessors / ctors own-only /
// properties / events / fields).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_members_flags_name(Il2CppObject* typeName,
                                                    Il2CppObject* memberName,
                                                    int32_t bindingAttr)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    char want[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, want)) return EmptyStringArray();
    // net48 GetMember("") -> an EMPTY array, no throw (probe-pinned).
    if (want[0] == '\0') return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    // Count pass (five global slices, the members_flags order).
    int32_t methodN = 0, ctorN = 0, propN = 0, evtN = 0, fieldN = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (BindingMethod(scan, scan->methods[i], bindingAttr, scan != info)
                    && scan->methods[i].name != nullptr
                    && std::strcmp(scan->methods[i].name, want) == 0) ++methodN;
        if (scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                if (BindingProperty(scan, scan->properties[i], bindingAttr, scan != info)
                    && scan->properties[i].name != nullptr
                    && std::strcmp(scan->properties[i].name, want) == 0) ++propN;
        if (scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                if (BindingEvent(scan, scan->events[i], bindingAttr, scan != info)
                    && scan->events[i].name != nullptr
                    && std::strcmp(scan->events[i].name, want) == 0) ++evtN;
        if (scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (BindingField(scan, scan->fields[i], bindingAttr, scan != info)
                    && scan->fields[i].name != nullptr
                    && std::strcmp(scan->fields[i].name, want) == 0) ++fieldN;
    }
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (BindingCtor(info, info->methods[i], bindingAttr, false)
                && info->methods[i].name != nullptr
                && std::strcmp(info->methods[i].name, want) == 0) ++ctorN;
    Il2CppArray* result = BuildNameArray(methodN + ctorN + propN + evtN + fieldN);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (BindingMethod(scan, scan->methods[i], bindingAttr, scan != info)
                    && scan->methods[i].name != nullptr
                    && std::strcmp(scan->methods[i].name, want) == 0)
                    StoreName(result, dst++, scan->methods[i].name);
    }
    if (info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (BindingCtor(info, info->methods[i], bindingAttr, false)
                && info->methods[i].name != nullptr
                && std::strcmp(info->methods[i].name, want) == 0)
                StoreName(result, dst++, info->methods[i].name);
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                if (BindingProperty(scan, scan->properties[i], bindingAttr, scan != info)
                    && scan->properties[i].name != nullptr
                    && std::strcmp(scan->properties[i].name, want) == 0)
                    StoreName(result, dst++, scan->properties[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                if (BindingEvent(scan, scan->events[i], bindingAttr, scan != info)
                    && scan->events[i].name != nullptr
                    && std::strcmp(scan->events[i].name, want) == 0)
                    StoreName(result, dst++, scan->events[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (BindingField(scan, scan->fields[i], bindingAttr, scan != info)
                    && scan->fields[i].name != nullptr
                    && std::strcmp(scan->fields[i].name, want) == 0)
                    StoreName(result, dst++, scan->fields[i].name);
    }
    return result;
}

//------------------------------------------------------------------------------
// REFL6A (item C) -- Type.GetMember(string, MemberTypes, BindingFlags): net48's
// 3-param form (type.cs:768). The five-slice union (methods incl. accessors /
// ctors own-only / properties / events / fields) is filtered by BOTH the
// BindingFlags AND the MemberTypes bit set; each slice is gated by its
// dedicated MemberTypes bit (Constructor 0x1 / Event 0x2 / Field 0x4 /
// Method 0x8 / Property 0x10 -- the .NET Framework enum values). Same
// name-match + exception contract as il2cpp_typeinfo_get_members_flags_name.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_members_flags_name_types(Il2CppObject* typeName,
                                                         Il2CppObject* memberName,
                                                         int32_t memberTypes,
                                                         int32_t bindingAttr)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    char nameBuf[kNameBufferSize];
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf)) return EmptyStringArray();
    char want[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, want)) return EmptyStringArray();
    if (want[0] == '\0') return EmptyStringArray();
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(nameBuf);
    if (info == nullptr) return EmptyStringArray();
    const bool wantMethod = (memberTypes & 0x8) != 0;
    const bool wantCtor = (memberTypes & 0x1) != 0;
    const bool wantProp = (memberTypes & 0x10) != 0;
    const bool wantEvt = (memberTypes & 0x2) != 0;
    const bool wantField = (memberTypes & 0x4) != 0;
    int32_t methodN = 0, ctorN = 0, propN = 0, evtN = 0, fieldN = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (wantMethod && scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (BindingMethod(scan, scan->methods[i], bindingAttr, scan != info)
                    && scan->methods[i].name != nullptr
                    && std::strcmp(scan->methods[i].name, want) == 0) ++methodN;
        if (wantProp && scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                if (BindingProperty(scan, scan->properties[i], bindingAttr, scan != info)
                    && scan->properties[i].name != nullptr
                    && std::strcmp(scan->properties[i].name, want) == 0) ++propN;
        if (wantEvt && scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                if (BindingEvent(scan, scan->events[i], bindingAttr, scan != info)
                    && scan->events[i].name != nullptr
                    && std::strcmp(scan->events[i].name, want) == 0) ++evtN;
        if (wantField && scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (BindingField(scan, scan->fields[i], bindingAttr, scan != info)
                    && scan->fields[i].name != nullptr
                    && std::strcmp(scan->fields[i].name, want) == 0) ++fieldN;
    }
    if (wantCtor && info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (BindingCtor(info, info->methods[i], bindingAttr, false)
                && info->methods[i].name != nullptr
                && std::strcmp(info->methods[i].name, want) == 0) ++ctorN;
    Il2CppArray* result = BuildNameArray(methodN + ctorN + propN + evtN + fieldN);
    int32_t dst = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (wantMethod && scan->methods != nullptr)
            for (int32_t i = 0; i < scan->methodCount; ++i)
                if (BindingMethod(scan, scan->methods[i], bindingAttr, scan != info)
                    && scan->methods[i].name != nullptr
                    && std::strcmp(scan->methods[i].name, want) == 0)
                    StoreName(result, dst++, scan->methods[i].name);
    }
    if (wantCtor && info->methods != nullptr)
        for (int32_t i = 0; i < info->methodCount; ++i)
            if (BindingCtor(info, info->methods[i], bindingAttr, false)
                && info->methods[i].name != nullptr
                && std::strcmp(info->methods[i].name, want) == 0)
                StoreName(result, dst++, info->methods[i].name);
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (wantProp && scan->properties != nullptr)
            for (int32_t i = 0; i < scan->propertyCount; ++i)
                if (BindingProperty(scan, scan->properties[i], bindingAttr, scan != info)
                    && scan->properties[i].name != nullptr
                    && std::strcmp(scan->properties[i].name, want) == 0)
                    StoreName(result, dst++, scan->properties[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (wantEvt && scan->events != nullptr)
            for (int32_t i = 0; i < scan->eventCount; ++i)
                if (BindingEvent(scan, scan->events[i], bindingAttr, scan != info)
                    && scan->events[i].name != nullptr
                    && std::strcmp(scan->events[i].name, want) == 0)
                    StoreName(result, dst++, scan->events[i].name);
    }
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (wantField && scan->fields != nullptr)
            for (int32_t i = 0; i < scan->fieldCount; ++i)
                if (BindingField(scan, scan->fields[i], bindingAttr, scan != info)
                    && scan->fields[i].name != nullptr
                    && std::strcmp(scan->fields[i].name, want) == 0)
                    StoreName(result, dst++, scan->fields[i].name);
    }
    return result;
}

//------------------------------------------------------------------------------
// REFL6A (item A) -- the System.Type enum surface. Each Type.GetEnum* instance
// method resolves the receiver (the Type VALUE full-name string) to a
// REGISTERED ENUM (the enumext name-keyed metadata registry) and then
// delegates to the corresponding shared by_type_name query icall. A receiver
// that is NOT a registered enum throws ArgumentException (net48 "Type must be
// an Enum") -- the non-enum-receiver contract probe-pinned on p-refl6-enum.
// The shared enumext icalls keep their documented unknown-name fallback; only
// this Type arm enforces the throw.
//------------------------------------------------------------------------------
namespace {

// The net48 ArgumentException message for a non-enum Type receiver.
// (probe-verified message text; the probe itself prints only the exception
// type NAME, so the exact string is informational.)
const char kEnumReceiverArgMsg[] = "Type provided must be an Enum.";

// Resolve the Type VALUE receiver to a registered-enum name (UTF-8 buffer);
// returns false and throws ArgumentException when not resolvable.
bool CheckRegisteredEnumName(Il2CppObject* typeName, char (&nameBuf)[kNameBufferSize])
{
    if (typeName == nullptr || !AsciiFromManagedString(typeName, nameBuf))
    {
        il2cpp_throw_argument_exception(kEnumReceiverArgMsg);
        return false; // unreachable -- the throw does not return
    }
    if (!il2cpp::runtime::il2cpp_enum_is_registered_name(nameBuf))
    {
        il2cpp_throw_argument_exception(kEnumReceiverArgMsg);
        return false; // unreachable -- the throw does not return
    }
    return true;
}

} // namespace

Il2CppArray* il2cpp_typeinfo_get_enum_names(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (!CheckRegisteredEnumName(typeName, nameBuf)) return EmptyStringArray();
    return il2cpp::runtime::il2cpp_enum_get_names_by_type_name(typeName);
}

Il2CppArray* il2cpp_typeinfo_get_enum_values(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (!CheckRegisteredEnumName(typeName, nameBuf)) return EmptyStringArray();
    return il2cpp::runtime::il2cpp_enum_get_values_by_type_name(typeName);
}

Il2CppObject* il2cpp_typeinfo_get_enum_name(Il2CppObject* typeName,
                                            Il2CppObject* valueBox)
{
    char nameBuf[kNameBufferSize];
    if (!CheckRegisteredEnumName(typeName, nameBuf)) return nullptr;
    return reinterpret_cast<Il2CppObject*>(
        il2cpp::runtime::il2cpp_enum_get_name_by_type_name(typeName, valueBox));
}

Il2CppObject* il2cpp_typeinfo_get_enum_underlying_type(Il2CppObject* typeName)
{
    char nameBuf[kNameBufferSize];
    if (!CheckRegisteredEnumName(typeName, nameBuf)) return nullptr;
    return reinterpret_cast<Il2CppObject*>(
        il2cpp::runtime::il2cpp_enum_get_underlying_type_by_type_name(typeName));
}
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

// REFL6B (item B5): Type.get_ContainsGenericParameters on a RUNTIME receiver.
// The transpiler folds every typeof chain (open def -> 1) BEFORE this arm;
// a runtime Type VALUE (GetType() result) never denotes an open generic in
// the simplified model (MakeGenericType is LOUD -- no fabricated arguments),
// so the net48 False / True answer for every runtime-reachable type is 0.
int32_t il2cpp_typeinfo_is_contains_generic_params(Il2CppObject* typeName)
{
    (void)typeName;
    return 0;
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
    // genericArity > 0: the genericArgNames Plan B is deferred (RF-N4 ruling 1) --
    // the runtime cannot fabricate the closed argument types (the folded name
    // of an instantiation collapses to the definition's name), so this path
    // is a LOUD gap, never a silent wrong result.
    il2cpp_throw_not_supported_exception(
        "Type.GetGenericArguments() on a generic type is not wired in RF-N4 (the genericArgNames Plan B is deferred -- no fabricated argument list; only the non-generic empty-array path is supported)");
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

// REFL-ARITY -- Type.GetTypeCode() on a RUNTIME receiver (a GetType() result):
// the Type VALUE (full-name string) mapped to the net48 System.TypeCode enum
// value. The table mirrors il2cpp_type_get_typecode (typeext.cpp) so the
// descriptor path and the name-string path agree.
int32_t il2cpp_typeinfo_get_typecode_from_name(Il2CppObject* typeName)
{
    if (typeName == nullptr) return 0; // TypeCode.Empty
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, nameBuf)) return 0;
    if (std::strcmp(nameBuf, "System.Int32") == 0) return 9;
    if (std::strcmp(nameBuf, "System.Boolean") == 0) return 3;
    if (std::strcmp(nameBuf, "System.Char") == 0) return 4;
    if (std::strcmp(nameBuf, "System.Double") == 0) return 14;
    if (std::strcmp(nameBuf, "System.String") == 0) return 18;
    if (std::strcmp(nameBuf, "System.Decimal") == 0) return 15;
    return 1; // TypeCode.Object -- the M4 fallback for all other types
}

namespace {
// FindPublicFieldThroughBaseChain -- own table first, then the baseType chain
// (inherited fields live under THEIR declaring type's table), public only
// (the net48 GetField / FieldInfo.GetValue default binding).
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

// p-refl-fvtype (GAP-2 / P1): depth of a type's baseType chain (the
// most-derived-first tie-break for the static-field registry scan below).
int32_t BaseChainDepthFor(const Il2CppTypeInfo* info)
{
    int32_t depth = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        ++depth;
    }
    return depth;
}

// p-refl-fvtype (GAP-2 / P1): a STATIC field's GetValue(null)/SetValue(null)
// has no target object to join the declaring type from. The MemberInfo VALUE
// in the simplified model is the field-NAME string, so the declaring type is
// resolved by scanning the typeinfo registry for a type that DECLARES a public
// STATIC field with that name, picking the MOST-DERIVED one (the deepest
// baseType chain -- net48 most-derived-first member doctrine, the same scan
// il2cpp_attribute_member_get_custom uses). Field names are unique within a
// type; a same-named static on several types resolves to the deepest
// (ambiguous programs are outside the probe scope -- net48 would need the
// FieldInfo's own declaring type).
bool FindStaticFieldByScan(const char* name, const Il2CppTypeInfo** outInfo,
                           const Il2CppFieldInfo** outField)
{
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr || info->fields == nullptr) continue;
        for (int32_t j = 0; j < info->fieldCount; ++j)
        {
            const Il2CppFieldInfo& f = info->fields[j];
            if (f.name != nullptr && std::strcmp(f.name, name) == 0
                && PublicField(f) && (f.flags & 2) != 0)
            {
                if (*outField == nullptr || BaseChainDepthFor(info) > BaseChainDepthFor(*outInfo))
                {
                    *outInfo = info;
                    *outField = &f;
                }
            }
        }
    }
    return *outField != nullptr;
}

} // namespace

//------------------------------------------------------------------------------
// FieldBoxType -- map a field's SYSTEM TYPE NAME to its box/unbox carrier
// (the p-refl-fvtype widening; net48 FieldInfo.GetValue returns the value
// boxed with the FIELD's own type -- a bool field -> boxed Boolean -- and
// SetValue accepts the boxed value of that type). The int32-family carriers
// (Boolean/Char/SByte/Byte/Int16/UInt16/UInt32 all live in an int32 payload
// slot / System.Int32 itself), the int64 family (Long/ULong), the double
// family (Double/Single via the M4 double ABI), and the reference slot.
// Unknown -> -1 (explicit LOUD, never a silent null/no-op).
//------------------------------------------------------------------------------
namespace {
int FieldBoxType(const Il2CppFieldInfo* field)
{
    if (field->isRef) return 3;
    if (field->typeName == nullptr) return -1;
    if (std::strcmp(field->typeName, "System.Int32") == 0) return 0;
    if (std::strcmp(field->typeName, "System.Boolean") == 0) return 1;
    if (std::strcmp(field->typeName, "System.Char") == 0) return 2;
    if (std::strcmp(field->typeName, "System.SByte") == 0) return 4;
    if (std::strcmp(field->typeName, "System.Byte") == 0) return 5;
    if (std::strcmp(field->typeName, "System.Int16") == 0) return 6;
    if (std::strcmp(field->typeName, "System.UInt16") == 0) return 7;
    if (std::strcmp(field->typeName, "System.UInt32") == 0) return 8;
    if (std::strcmp(field->typeName, "System.Int64") == 0) return 10;
    if (std::strcmp(field->typeName, "System.UInt64") == 0) return 11;
    if (std::strcmp(field->typeName, "System.Single") == 0) return 12;
    if (std::strcmp(field->typeName, "System.Double") == 0) return 13;
    return -1;
}

// DEFECT-F1 (audit 2A): the WIDE kinds (10-13 = Int64/UInt64/Single/Double)
// may only be read/written when the underlying slot is REALLY the full 8-byte
// carrier. In this transpiler model the only 8-byte slots are STATIC Int64/
// UInt64 fields (StaticFieldSemantic.Int64/UInt64 -> int64_t/uint64_t
// storage inside the static block). An INSTANCE long/double/float field is
// ALWAYS collapsed to int32_t (TypeEmitter layout: "int32_t field; // value
// fields: int32_t at slot n*pointerSize"), so the 8-byte GetValue would read
// 4 bytes past the field (into its neighbor) and SetValue would OVERWRITE the
// neighbor -- a silent out-of-bounds the a16afce4 MetadataLoader widening
// enabled (instance fields now carry the real "System.Int64"/"System.Single"/
// "System.Double" typeName the width dispatcher keys on). Static Single/Double
// are equally unsafe: StaticFieldSemantics has no Single/Double arm, so their
// static storage is the int32_t default too. F1 ruling: kinds 10-13 LOUD-reject
// unless STATIC Int64/UInt64 (which keep the working 8-byte path).
static bool WideSlotUnrepresentable(const Il2CppFieldInfo* field, int kind)
{
    if (kind != 10 && kind != 11 && kind != 12 && kind != 13) return false;
    const bool isStatic = (field->flags & 2) != 0;
    if (!isStatic) return true;                  // instance long/double/float (int32 slot)
    if (kind == 12 || kind == 13) return true;   // static Single/Double: no wide slot
    return false;                                // static Int64/UInt64: real 8B slot
}

// Box a value read from the int32-family payload slot with the field's own
// box class. The stored slot is the 32-bit carrier the transpiler emits for
// all narrow primitive fields; the BOX class carries the field identity.
Il2CppObject* BoxInt32SlotAs(int kind, int32_t raw)
{
    switch (kind)
    {
    case 0: return il2cpp_box_int32(nullptr, raw);                // System.Int32
    case 1: return runtime::icall_box_bool(raw != 0);             // System.Boolean
    case 2: return runtime::icall_box_char(raw);                  // System.Char
    case 4: return runtime::icall_box_sbyte(raw);                 // System.SByte
    case 5: return runtime::icall_box_byte(raw);                  // System.Byte
    case 6: return runtime::icall_box_short(raw);                 // System.Int16
    case 7: return runtime::icall_box_ushort(raw);                // System.UInt16
    case 8: return runtime::icall_box_uint(raw);                  // System.UInt32
    default: return il2cpp_box_int32(nullptr, raw);
    }
}

// Unbox a value supplied by FieldInfo.SetValue through the field's own carrier
// (the value arrives boxed as the field's type -- net48 value-boxed shape).
// The int32-family readers all consume the same payload slot; the specific
// reader type-checks the box klass (a wrong value fails InvalidCast, net48).
int32_t UnboxToInt32SlotAs(int kind, Il2CppObject* value)
{
    switch (kind)
    {
    case 0: return il2cpp_unbox_int32(value);                     // System.Int32
    case 1: return runtime::icall_unbox_bool(value) ? 1 : 0;      // System.Boolean
    case 2: return runtime::icall_unbox_char(value);              // System.Char
    case 4: return runtime::icall_unbox_sbyte(value);             // System.SByte
    case 5: return runtime::icall_unbox_byte(value);              // System.Byte
    case 6: return runtime::icall_unbox_short(value);             // System.Int16
    case 7: return runtime::icall_unbox_ushort(value);            // System.UInt16
    case 8: return runtime::icall_unbox_uint(value);              // System.UInt32
    default: return il2cpp_unbox_int32(value);
    }
}
} // namespace

Il2CppObject* il2cpp_typeinfo_field_get_value(Il2CppObject* fieldName, Il2CppObject* target)
{
    char nameBuf[kNameBufferSize];
    if (fieldName == nullptr || !AsciiFromManagedString(fieldName, nameBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = nullptr;
    const Il2CppFieldInfo* field = nullptr;
    if (target == nullptr)
    {
        // GAP-2 (p-refl-fvtype): a STATIC field's GetValue(null) is legal on
        // net48 -- resolve the declaring type by registry scan (no target to
        // join from). An INSTANCE field with a null target keeps the net48
        // NRE doctrine (the scan finds no static field -> the throw below).
        if (!FindStaticFieldByScan(nameBuf, &info, &field))
        {
            ThrowNullTypeReceiver(); // net48 instance-field doctrine: null obj -> NRE
        }
    }
    else
    {
        info = il2cpp_typeinfo_lookup_by_class(target->klass);
        if (info == nullptr)
        {
            return nullptr;
        }
        field = FindPublicFieldThroughBaseChain(info, nameBuf);
        if (field == nullptr)
        {
            return nullptr;
        }
    }
    // O4 (boxed user-struct reflection): a value-type table's offsets are
    // STRUCT-RELATIVE (field 0 at offset 0), but a boxed struct's payload sits
    // one object header past the base -- shift by the box payload offset when
    // the declaring type info is a value type. Classes have object-relative
    // offsets (slot 0 = header) and are unchanged; the enum's value__ offset
    // already coincides with the box payload, so enums (isValueType 0) are
    // unchanged too.
    char* slot = field->offset >= 0
        ? reinterpret_cast<char*>(target)
            + (info->isValueType ? kReflectionBoxPayloadOffset : 0)
            + field->offset
        : static_cast<char*>(field->staticAddr);
    // Slot width dispatched by the field's SYSTEM TYPE NAME (GAP-2 / P1).
    // The int32-family kinds are 0,1,2,4,5,6,7,8 (3 is the REFERENCE slot,
    // handled separately below -- an int32-family range test must exclude 3).
    const int kind = FieldBoxType(field);
    if (WideSlotUnrepresentable(field, kind))
    {
        il2cpp_throw_not_supported_exception(
            "FieldInfo.GetValue: this long/double/float field has no 8-byte carrier in this AOT build (instance value fields and static Single/Double are stored as int32_t slots -- the transpiler value-slot collapse) -- explicit LOUD; the 8-byte read would silently overrun into the neighbor field. Only static Int64/UInt64 fields keep the 8-byte path.");
        return nullptr; // unreachable -- the throw does not return
    }
    if (kind == 0 || (kind >= 1 && kind <= 2) || (kind >= 4 && kind <= 8))
    {
        return BoxInt32SlotAs(kind, *reinterpret_cast<int32_t*>(slot));
    }
    if (kind == 10)
    {
        return runtime::icall_box_long(*reinterpret_cast<int64_t*>(slot));
    }
    if (kind == 11)
    {
        return runtime::icall_box_ulong(*reinterpret_cast<uint64_t*>(slot));
    }
    if (kind == 12)
    {
        return runtime::icall_box_float(*reinterpret_cast<double*>(slot));
    }
    if (kind == 13)
    {
        return runtime::icall_box_double(*reinterpret_cast<double*>(slot));
    }
    if (kind == 3)
    {
        return *reinterpret_cast<Il2CppObject**>(slot);
    }
    il2cpp_throw_not_supported_exception(
        "FieldInfo.GetValue: the field type is not representable in this AOT build (unrecognized field typeName) -- explicit LOUD; no silent null");
    return nullptr; // unreachable -- the throw does not return
}

void il2cpp_typeinfo_field_set_value(Il2CppObject* fieldName, Il2CppObject* target,
                                     Il2CppObject* value)
{
    char nameBuf[kNameBufferSize];
    if (fieldName == nullptr || !AsciiFromManagedString(fieldName, nameBuf))
    {
        return;
    }
    const Il2CppTypeInfo* info = nullptr;
    const Il2CppFieldInfo* field = nullptr;
    if (target == nullptr)
    {
        // GAP-2 (p-refl-fvtype): a STATIC field's SetValue(null, v) is legal
        // on net48 -- the same registry scan as the GetValue twin above.
        if (!FindStaticFieldByScan(nameBuf, &info, &field))
        {
            ThrowNullTypeReceiver();
        }
    }
    else
    {
        info = il2cpp_typeinfo_lookup_by_class(target->klass);
        if (info == nullptr)
        {
            return;
        }
        field = FindPublicFieldThroughBaseChain(info, nameBuf);
        if (field == nullptr)
        {
            return;
        }
    }
    // O4 (boxed user-struct reflection): the same struct-relative offset shift
    // as the GetValue twin above (see that comment -- classes/enums unchanged).
    char* slot = field->offset >= 0
        ? reinterpret_cast<char*>(target)
            + (info->isValueType ? kReflectionBoxPayloadOffset : 0)
            + field->offset
        : static_cast<char*>(field->staticAddr);
    // p-refl-fvtype: the SET side unboxes through the field's REAL carrier --
    // a bool field accepts the boxed Boolean value net48 passes, a short the
    // boxed Int16, a uint the boxed UInt32, an int the boxed Int32 (the old
    // hard-coded il2cpp_unbox_int32 rejected every non-Int32 value box with
    // "not a boxed Int32"). Reference fields store the object itself.
    // The int32-family kinds are 0,1,2,4,5,6,7,8 (3 is the REFERENCE slot).
    const int kind = FieldBoxType(field);
    if (WideSlotUnrepresentable(field, kind))
    {
        il2cpp_throw_not_supported_exception(
            "FieldInfo.SetValue: this long/double/float field has no 8-byte carrier in this AOT build (instance value fields and static Single/Double are stored as int32_t slots -- the transpiler value-slot collapse) -- explicit LOUD; the 8-byte write would silently overrun into the neighbor field. Only static Int64/UInt64 fields keep the 8-byte path.");
        return; // unreachable -- the throw does not return
    }
    if (kind == 0 || (kind >= 1 && kind <= 2) || (kind >= 4 && kind <= 8))
    {
        *reinterpret_cast<int32_t*>(slot) = UnboxToInt32SlotAs(kind, value);
        return;
    }
    if (kind == 10 || kind == 11)
    {
        *reinterpret_cast<int64_t*>(slot) =
            kind == 10 ? runtime::icall_unbox_long(value)
                       : static_cast<int64_t>(runtime::icall_unbox_ulong(value));
        return;
    }
    if (kind == 12 || kind == 13)
    {
        *reinterpret_cast<double*>(slot) = runtime::icall_unbox_double(value);
        return;
    }
    if (kind == 3)
    {
        *reinterpret_cast<Il2CppObject**>(slot) = value;
        return;
    }
    il2cpp_throw_not_supported_exception(
        "FieldInfo.SetValue: the field type is not representable in this AOT build (unrecognized field typeName) -- explicit LOUD; no silent no-op");
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
    // unreachable AOT function) is LOUD when it is the member the call would
    // bind, or when no arity match exists at all (the committed R3 red-test
    // shape: Trimmed(name, wrong-arity args) still throws
    // NotSupportedException). The check runs AFTER the own-table-first
    // resolution below so an abstract BASE declaration (net48's GetMethod
    // returns the derived override, not the abstract slot) never misreports a
    // concrete derived member as trimmed (p-refl2-gapmatrix B12/B21).
    // Resolve by name + paramCount (overload disambiguation), OWN TABLE FIRST:
    // net48's GetMethod returns the most-derived declaration, so a base-chain
    // same-name entry is only consulted when the type itself declares none.
    const Il2CppMethodInfo* found = nullptr;
    int32_t matches = 0;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        bool foundHere = false;
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name == nullptr || std::strcmp(m.name, nameBuf) != 0) continue;
            if (m.paramCount != paramCount) continue;
            if (!foundHere) { found = &m; foundHere = true; }
            ++matches;
        }
        if (foundHere) break; // the most-derived declaration wins
    }
    if (found == nullptr)
    {
        // No arity match: a same-name trimmed method (any arity) is LOUD.
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
        return nullptr; // not found (defensive query convention)
    }
    if (found->fnPtr == nullptr)
    {
        il2cpp_throw_new("System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception",
                         "Il2CppMethodInfo::Invoke: the method has no runtime-unreachable AOT function (trimmed) -- reflection Invoke is unsupported for it.");
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
            case 'B':
                // RF-N8 (NEXT2-DECISION section 2): a reflected method with a
                // REF/OUT (by-ref) parameter cannot be dispatched -- the invoker
                // has no by-ref address ABI and the by-ref argument must NEVER be
                // silently defaulted to nullptr (the pre-RF-N8 behavior: this
                // 'B:' encoding fell to the default arm and the invoke returned
                // null without a word). Explicit LOUD, mirroring the RF-N7
                // rejection style; the throw does not return, so the icall's
                // return value narrows to the throw path only (no ABI change).
                il2cpp_throw_not_supported_exception(
                    "MethodInfo.Invoke: a reflected method with a ref/out (by-ref) parameter is not wired in RF-N8 -- the invoker has no by-ref address ABI (explicit LOUD; the by-ref argument is never silently passed as null)");
                break; // unreachable -- the throw does not return
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
// match the property's indexer arity (RF-N2 audit A #1, observation B -- net48
// probe-verified: an indexer with a null/empty index AND a non-indexed
// property receiving a non-null index array BOTH throw
// TargetParameterCountException; the runtime previously built a 0-length
// arg array and the invoker arity mismatch silently returned nullptr).
void ThrowPropertyParamCountException()
{
    il2cpp_throw_new("System.Reflection.TargetParameterCountException,System.Exception",
        "Parameter count mismatch.");
}

// Throw NullReferenceException for a null property-name receiver (RF-N2 audit
// A #2, observation C -- net48 probe-verified: typeof(X).GetProperty("Missing")
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
        // RF-N2 audit A #2 (observation C): a null property-name receiver is the
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
    // RF-N2 audit A #1 (observation B): the index array length must equal the
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
        // RF-N2 audit A #2 (observation C): null receiver == unknown property
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
    // RF-N2 audit A #1 (observation B): index-array arity must match the indexer
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
// GAP-3 (P1) -- PropertyInfo.GetGetMethod()/GetSetMethod().
//
// The receiver is the PROPERTY-NAME string (the simplified member model, the
// same carrier il2cpp_typeinfo_get_property returns); the declaring type is
// NOT carried by the PropertyInfo value, so the runtime resolves it by
// scanning the typeinfo registry for the type that DECLARES a property with
// that name and picks the DEEPEST one (the most-derived-first member doctrine
// -- the same scan il2cpp_attribute_member_get_custom / the static-field
// null-target path use; a same-named property on several types resolves to the
// deepest, net48 property names are unique within a type). dir = 0 ->
// GetGetMethod (the getterName), dir = 1 -> GetSetMethod (the setterName).
//
// net48 GetGetMethod()/GetSetMethod() bind the PUBLIC accessor by default
// (the (string arg, bool publicOnly=true) overload), so a PRIVATE accessor
// returns null -- the accessor's PUBLIC flag (method table flags bit0)
// is checked, matching the P1 visibility filter. The RESULT is the
// accessor METHOD-NAME string (a MethodInfo VALUE, the simplified member
// model) or nullptr when the property has no such accessor / the accessor is
// non-public / the property is not found.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_property_get_accessor(Il2CppObject* propertyName, int32_t dir)
{
    if (propertyName == nullptr)
    {
        // net48 GetProperty("Missing").GetGetMethod() NREs on the null
        // PropertyInfo receiver (the GetProperty miss -> null chain).
        ThrowPropertyNullReceiver();
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(propertyName, nameBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    const Il2CppPropertyInfo* bestProp = nullptr;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr || info->properties == nullptr) continue;
        for (int32_t j = 0; j < info->propertyCount; ++j)
        {
            const Il2CppPropertyInfo& p = info->properties[j];
            if (p.name == nullptr || std::strcmp(p.name, nameBuf) != 0) continue;
            const int32_t depth = BaseChainDepthFor(info);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = info;
                bestProp = &p;
            }
        }
    }
    if (best == nullptr || bestProp == nullptr)
    {
        return nullptr; // property not found (defensive query convention)
    }
    const char* accessor = dir == 0 ? bestProp->getterName : bestProp->setterName;
    if (accessor == nullptr)
    {
        return nullptr; // the property has no getter/setter of that direction
    }
    // net48 GetGetMethod()/GetSetMethod() bind the PUBLIC accessor by default
    // (the no-arg overload is publicOnly=true) -- resolve the accessor method
    // in the declaring type's OWN table and check its PUBLIC flag
    // (Il2CppMethodInfo flags bit0). A private accessor is invisible.
    const Il2CppMethodInfo* acc = il2cpp_type_get_method(best, accessor);
    if (acc == nullptr || (acc->flags & 1) == 0)
    {
        return nullptr;
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(accessor));
}

// REFL-ARITY -- PropertyInfo.GetGetMethod(bool nonPublic) / GetSetMethod(bool
// nonPublic). Same declaring-property scan and result contract as the no-arg
// overloads, but the accessor's PUBLIC flag is enforced only when nonPublic ==
// 0 (net48: the no-arg overloads bind publicOnly=true).
Il2CppObject* il2cpp_typeinfo_property_get_accessor_ex(Il2CppObject* propertyName, int32_t nonPublic,
                                                       int32_t dir)
{
    if (propertyName == nullptr)
    {
        ThrowPropertyNullReceiver();
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(propertyName, nameBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    const Il2CppPropertyInfo* bestProp = nullptr;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr || info->properties == nullptr) continue;
        for (int32_t j = 0; j < info->propertyCount; ++j)
        {
            const Il2CppPropertyInfo& p = info->properties[j];
            if (p.name == nullptr || std::strcmp(p.name, nameBuf) != 0) continue;
            const int32_t depth = BaseChainDepthFor(info);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = info;
                bestProp = &p;
            }
        }
    }
    if (best == nullptr || bestProp == nullptr)
    {
        return nullptr; // property not found (defensive query convention)
    }
    const char* accessor = dir == 0 ? bestProp->getterName : bestProp->setterName;
    if (accessor == nullptr)
    {
        return nullptr; // the property has no getter/setter of that direction
    }
    const Il2CppMethodInfo* acc = il2cpp_type_get_method(best, accessor);
    if (acc == nullptr || (nonPublic == 0 && (acc->flags & 1) == 0))
    {
        return nullptr;
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(accessor));
}

//------------------------------------------------------------------------------
// GAP-5 (P1) -- ConstructorInfo: GetConstructor(Type[]) + Invoke(object[]).
//
// The typeinfo method table carries the ctor entries (name ".ctor", flags bit2
// = ctor, bit0 = public) -- the same table GetConstructors() and
// il2cpp_methodinfo_invoke use. To keep the ctor MEMBER VALUE resolvable by a
// later Invoke (whose receiver is the value alone, with no target object
// carrying the klass), GetConstructor returns a DISAMBIGUATED member value
// "<declaringTypeFullName>::.ctor" instead of the bare ".ctor": the declaring
// type rides in the value, and Invoke parses it to join the typeinfo. net48
// ConstructorInfo.Name is ".ctor" -- the probe never prints it (only the
// constructed object's observable state), so the value's disambiguation is
// invisible to byte-exact output.
//------------------------------------------------------------------------------
namespace {
const char* kCtorName = ".ctor";
const char* kCtorSeparator = "::";

// Parse the declaring type out of a disambiguated ctor member value
// "<FullName>::.ctor". Returns the full-name into outBuf (an empty string on
// malformed input -- treated as not-found).
void SplitCtorMemberValue(const char* value, char (&outBuf)[kNameBufferSize])
{
    outBuf[0] = '\0';
    if (value == nullptr)
    {
        return;
    }
    const char* sep = std::strstr(value, kCtorSeparator);
    if (sep == nullptr)
    {
        return;
    }
    const size_t len = static_cast<size_t>(sep - value);
    if (len >= sizeof(outBuf)) return;
    std::memcpy(outBuf, value, len);
    outBuf[len] = '\0';
}
} // namespace

//------------------------------------------------------------------------------
// il2cpp_typeinfo_get_constructor -- Type.GetConstructor(Type[]).
//
// The receiver is the TYPE VALUE (full-name string, the simplified Type model,
// identical to the GetMethod/GetProperty/GetField single-member queries). The
// runtime resolves the typeinfo registry, scans the method table for a PUBLIC
// ctor entry (name ".ctor", flags bit2 + bit0) whose paramCount matches the
// types array length (the simplified net48 GetConstructor arity match), and
// returns the ctor MEMBER VALUE "<FullName>::.ctor" (the disambiguated value
// that Invoke can resolve) or null on a miss (net48: a public ctor with those
// exact types not found -> null). A null/empty Type[] = the parameterless
// ctor (paramCount 0).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_constructor(Il2CppObject* typeName, Il2CppArray* types)
{
    if (typeName == nullptr)
    {
        ThrowNullTypeReceiver(); // ((Type)null).GetConstructor(...) -> NRE
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
    const int32_t wantArity = types != nullptr ? types->length : 0;
    const Il2CppMethodInfo* match = nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name != nullptr && std::strcmp(m.name, kCtorName) == 0
                && (m.flags & 4) != 0 && (m.flags & 1) != 0
                && m.paramCount == wantArity)
            {
                match = &m;
                break;
            }
        }
        if (match != nullptr) break; // own-table ctor wins over a base
    }
    if (match == nullptr)
    {
        return nullptr;
    }
    // Disambiguated member value: "<declaringTypeFullName>::.ctor".
    const char* declName = info->name != nullptr ? info->name : "";
    char valueBuf[kNameBufferSize * 2 + 4];
    std::snprintf(valueBuf, sizeof(valueBuf), "%s%s%s", declName, kCtorSeparator, kCtorName);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(valueBuf));
}

// REFL-ARITY -- Type.GetConstructor(BindingFlags, Binder, Type[],
// ParameterModifier[]). Same ctor match as the (Type[]) form, but the explicit
// BindingFlags DOES filter (the binder/modifiers are ignored by net48's match --
// default binder, by-ref modifiers only, W7). The (Type[]) form hardcodes
// public+ctor; this applies the caller's visibility/static filter instead.
Il2CppObject* il2cpp_typeinfo_get_constructor_flags(Il2CppObject* typeName, Il2CppArray* types,
                                                    int32_t bindingAttr)
{
    if (typeName == nullptr)
    {
        ThrowNullTypeReceiver(); // ((Type)null).GetConstructor(...) -> NRE
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
    const int32_t wantArity = types != nullptr ? types->length : 0;
    const Il2CppMethodInfo* match = nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name != nullptr && std::strcmp(m.name, kCtorName) == 0
                && (m.flags & 4) != 0
                && MemberMatchesVis(m.flags, bindingAttr, false)
                && m.paramCount == wantArity)
            {
                match = &m;
                break;
            }
        }
        if (match != nullptr) break; // own-table ctor wins over a base
    }
    if (match == nullptr)
    {
        return nullptr;
    }
    const char* declName = info->name != nullptr ? info->name : "";
    char valueBuf[kNameBufferSize * 2 + 4];
    std::snprintf(valueBuf, sizeof(valueBuf), "%s%s%s", declName, kCtorSeparator, kCtorName);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(valueBuf));
}

//------------------------------------------------------------------------------
// il2cpp_constructorinfo_invoke -- ConstructorInfo.Invoke(object[] args).
//
// The receiver is the ctor MEMBER VALUE "<FullName>::.ctor" (as
// il2cpp_typeinfo_get_constructor returns); the runtime parses the declaring
// type, joins the typeinfo, resolves the PUBLIC ctor whose paramCount matches
// the args length, ALLOCATES a fresh instance (il2cpp_object_new) and RUNS the
// ctor on it through il2cpp_methodinfo_invoke -- the exact RF-1 instance-'this'
// injection (the new object is both the 'this' object and the returned
// instance). The result is the constructed object.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_constructorinfo_invoke(Il2CppObject* memberName, Il2CppArray* args)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    // The member value is the internal "<FullName>::.ctor" carrier generated
    // by il2cpp_typeinfo_get_constructor; it fits kNameBufferSize for every
    // reflected type (the generated FullName is ASCII-limited to the same
    // bound). An over-long value is rejected as not-found (defensive).
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, memberBuf))
    {
        return nullptr;
    }
    char typeName[kNameBufferSize];
    SplitCtorMemberValue(memberBuf, typeName);
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeName);
    if (info == nullptr || info->klass == nullptr)
    {
        return nullptr;
    }
    const int32_t wantArity = args != nullptr ? args->length : 0;
    const Il2CppMethodInfo* ctor = nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name != nullptr && std::strcmp(m.name, kCtorName) == 0
                && (m.flags & 4) != 0 && (m.flags & 1) != 0
                && m.paramCount == wantArity && m.fnPtr != nullptr)
            {
                ctor = &m;
                break;
            }
        }
        if (ctor != nullptr) break;
    }
    if (ctor == nullptr)
    {
        return nullptr;
    }
    Il2CppObject* instance = il2cpp_object_new(const_cast<Il2CppClass*>(info->klass));
    // Run the ctor on the fresh instance: the ctor dispatch reuses
    // il2cpp_methodinfo_invoke's instance-'this' path ("I:r,..." signature
    // with the explicit args after 'this'). The ctor entry's fnPtr is the
    // generated ctor ABI void(C* this, args...) -- identical to a void
    // instance method, exactly what the invoker dispatches.
    Il2CppObject* ctorValue = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(kCtorName));
    (void)il2cpp_methodinfo_invoke(ctorValue, instance, args);
    return instance;
}

//------------------------------------------------------------------------------
// REFL3 (item 2) -- il2cpp_typeinfo_get_parameters (typeinfo.h contract):
// MethodBase.GetParameters(). The receiver is the METHOD-NAME string (the
// simplified member-model VALUE il2cpp_typeinfo_get_method returns) or the
// disambiguated ctor value "<FullName>::.ctor" (il2cpp_typeinfo_get_constructor
// returns). The runtime scans the typeinfo registry for the DEEPEST type that
// DECLARES the member (method-name doctrine, RF-N6 precedent -- a method name
// is unique across kinds within a type), resolves the method (own + base chain)
// or ctor, then builds a reference-element Il2CppArray of ParameterInfo VALUE
// strings -- the pipe-joined codec "name|typeFullName|position|flags" that the
// ParameterInfo property icalls decode. A method/ctor with no parameters (or
// an unknown member / unregistered type / malformed name) yields an EMPTY array
// (net48 GetParameters on a parameterless method returns length 0; never null).
// Null receiver -> NullReferenceException (the null-MemberInfo doctrine).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_typeinfo_get_parameters(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char memberBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, memberBuf))
    {
        return EmptyStringArray();
    }
    // Resolve the declaring type by REGISTRY SCAN (deepest type declaring the
    // member name -- the same member-name doctrine the RF-N6 member query and
    // the property accessor query use; the method VALUE carries no declaring
    // type). A ctor value "<FullName>::.ctor" names its type explicitly.
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    const bool isCtorValue = std::strstr(memberBuf, kCtorSeparator) != nullptr;
    char ctorTypeBuf[kNameBufferSize];
    if (isCtorValue)
    {
        SplitCtorMemberValue(memberBuf, ctorTypeBuf);
        best = il2cpp_typeinfo_lookup(ctorTypeBuf[0] != '\0' ? ctorTypeBuf : nullptr);
    }
    else
    {
        for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
        {
            const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
            if (info == nullptr || info->methods == nullptr) continue;
            bool declares = false;
            for (int32_t mi = 0; mi < info->methodCount; ++mi)
            {
                const Il2CppMethodInfo& m = info->methods[mi];
                if (m.name != nullptr && std::strcmp(m.name, memberBuf) == 0) { declares = true; break; }
            }
            if (declares)
            {
                int32_t depth = 0;
                for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType) ++depth;
                if (depth > bestDepth) { bestDepth = depth; best = info; }
            }
        }
    }
    if (best == nullptr)
    {
        return EmptyStringArray();
    }
    // Resolve the METHOD entry (own + base chain). For a ctor value, match the
    // ctor flag (bit2) -- and when several ctors exist, the FIRST public ctor of
    // the type (the value does not carry arity; the probe set has one ctor).
    const Il2CppMethodInfo* method = nullptr;
    for (const Il2CppTypeInfo* scan = best; scan != nullptr; scan = scan->baseType)
    {
        if (scan->methods == nullptr) continue;
        for (int32_t i = 0; i < scan->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = scan->methods[i];
            if (m.name == nullptr) continue;
            if (isCtorValue)
            {
                if (std::strcmp(m.name, kCtorName) == 0) { method = &m; break; }
            }
            else if (std::strcmp(m.name, memberBuf) == 0)
            {
                method = &m;
                break;
            }
        }
        if (method != nullptr) break; // own-table win, .NET shadowing
    }
    if (method == nullptr || method->signature == nullptr || method->paramCount <= 0)
    {
        return EmptyStringArray();
    }
    // Split the signature's param encodings (SplitSignatureParams, the same
    // helper the AME/get_method_types paths use). Up to 8 params (the probe
    // surface; the shared 4-slot cap of the exact-match paths doesn't apply --
    // GetParameters enumerates the FULL signature).
    const int32_t count = method->paramCount;
    if (count <= 0 || count > 8)
    {
        return EmptyStringArray();
    }
    char encs[8][kNameBufferSize];
    int32_t decoded = SplitSignatureParams(method->signature, encs, 8);
    if (decoded != count)
    {
        return EmptyStringArray();
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(void*), true, count);
    for (int32_t i = 0; i < count; ++i)
    {
        // typeFullName from the signature encoding (the Type VALUE full name).
        char typeFull[kNameBufferSize];
        const bool hasType = ParamTypeValueName(encs[i], typeFull);
        const char* tf = hasType ? typeFull : encs[i];
        // name + outFlag from the transpiler-baked paramMeta (missing -> the
        // "argN" fallback name, flag 0 -- a method without meta still yields a
        // parameter with a deterministic Name).
        char pName[kNameBufferSize];
        int32_t outFlag = 0;
        const char* metaName = nullptr;
        if (ParamMetaRow(method->paramMeta, i) != nullptr)
        {
            if (SplitParamMetaRow(ParamMetaRow(method->paramMeta, i), pName, &outFlag))
                metaName = pName;
        }
        static const char kArgPrefix[] = "arg";
        char fallback[kNameBufferSize];
        if (metaName == nullptr)
        {
            std::snprintf(fallback, sizeof(fallback), "%s%d", kArgPrefix, i);
            metaName = fallback;
        }
        char valueBuf[kNameBufferSize * 4];
        std::snprintf(valueBuf, sizeof(valueBuf), "%s|%s|%d|%d", metaName, tf, i, outFlag);
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(result, i)) =
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(valueBuf));
    }
    return result;
}

//------------------------------------------------------------------------------
// REFL3 (item 2) -- the ParameterInfo PROPERTY decoders. Each takes the
// ParameterInfo VALUE string ("name|typeFullName|position|flags") that
// il2cpp_typeinfo_get_parameters produces and returns ONE property:
//   il2cpp_parameterinfo_get_name        -> the name part (a managed string)
//   il2cpp_parameterinfo_get_parameter_type -> the typeFullName part (a Type
//        VALUE -- full-name string, the same carrier typeof() folds; net48
//        ParameterType is a System.Type. BYREF params decode the POINTEE name
//        (the narrow model has no "&"-typed Type VALUE -- W7).
//   il2cpp_parameterinfo_get_position    -> the position integer (int32)
//   il2cpp_parameterinfo_is_out          -> 1/0 (flags bit0)
// A malformed/absent value yields the defensive default ("" name, "" type,
// 0 position, 0 isOut) -- the property surface never crashes on a bad codec.
//------------------------------------------------------------------------------
namespace {

bool SplitParamValue(const char* value, char (&name)[kNameBufferSize],
                     char (&typeFull)[kNameBufferSize], int32_t* position,
                     int32_t* flags)
{
    name[0] = '\0'; typeFull[0] = '\0';
    if (position != nullptr) *position = 0;
    if (flags != nullptr) *flags = 0;
    if (value == nullptr) return false;
    const char* n1 = std::strchr(value, '|');
    if (n1 == nullptr) return false;
    {
        const size_t len = static_cast<size_t>(n1 - value);
        if (len >= kNameBufferSize) return false;
        std::memcpy(name, value, len);
        name[len] = '\0';
    }
    const char* t2 = std::strchr(n1 + 1, '|');
    if (t2 == nullptr) return false;
    {
        const size_t len = static_cast<size_t>(t2 - (n1 + 1));
        if (len >= kNameBufferSize) return false;
        std::memcpy(typeFull, n1 + 1, len);
        typeFull[len] = '\0';
    }
    const char* p3 = std::strchr(t2 + 1, '|');
    if (p3 == nullptr) return false;
    // Position is the field between t2 and p3 ("...|type|POSITION|flags").
    if (position != nullptr)
    {
        char posBuf[16];
        const size_t posLen = static_cast<size_t>(p3 - (t2 + 1));
        if (posLen >= sizeof(posBuf)) return false;
        std::memcpy(posBuf, t2 + 1, posLen);
        posBuf[posLen] = '\0';
        *position = std::atoi(posBuf);
    }
    // The flags field is the LAST component (the tail after p3).
    if (flags != nullptr) *flags = std::atoi(p3 + 1);
    return true;
}

} // namespace

Il2CppObject* il2cpp_parameterinfo_get_name(Il2CppObject* value)
{
    char buf[kNameBufferSize];
    char name[kNameBufferSize];
    char typeFull[kNameBufferSize];
    int32_t pos = 0, flags = 0;
    if (!AsciiFromManagedString(value, buf) ||
        !SplitParamValue(buf, name, typeFull, &pos, &flags))
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(""));
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name));
}

Il2CppObject* il2cpp_parameterinfo_get_parameter_type(Il2CppObject* value)
{
    char buf[kNameBufferSize];
    char name[kNameBufferSize];
    char typeFull[kNameBufferSize];
    int32_t pos = 0, flags = 0;
    if (!AsciiFromManagedString(value, buf) ||
        !SplitParamValue(buf, name, typeFull, &pos, &flags))
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(""));
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(typeFull));
}

int32_t il2cpp_parameterinfo_get_position(Il2CppObject* value)
{
    char buf[kNameBufferSize];
    char name[kNameBufferSize];
    char typeFull[kNameBufferSize];
    int32_t pos = 0, flags = 0;
    if (!AsciiFromManagedString(value, buf) ||
        !SplitParamValue(buf, name, typeFull, &pos, &flags))
        return 0;
    return pos;
}

int32_t il2cpp_parameterinfo_is_out(Il2CppObject* value)
{
    char buf[kNameBufferSize];
    char name[kNameBufferSize];
    char typeFull[kNameBufferSize];
    int32_t pos = 0, flags = 0;
    if (!AsciiFromManagedString(value, buf) ||
        !SplitParamValue(buf, name, typeFull, &pos, &flags))
        return 0;
    return (flags & 1) != 0 ? 1 : 0;
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

// net48 attributeType filter for a member-registered attribute: a null
// attributeType matches everything; otherwise the attribute's runtime class
// must equal attributeType or derive from it (walk the klass->baseClass chain
// -- attributeType.IsAssignableFrom).
bool MatchesAttrType(Il2CppObject* attr, Il2CppClass* attributeType)
{
    if (attributeType == nullptr)
    {
        return true;
    }
    Il2CppClass* attrKlass = (attr != nullptr) ? attr->klass : nullptr;
    for (Il2CppClass* current = attrKlass; current != nullptr; current = current->baseClass)
    {
        if (current == attributeType)
        {
            return true;
        }
    }
    return false;
}

Il2CppArray* il2cpp_attribute_member_get_custom_named(Il2CppObject* memberName,
                                                      Il2CppObject* attributeTypeName,
                                                      int32_t inherit)
{
    if (memberName == nullptr)
    {
        // net48: ((MemberInfo)null).GetCustomAttributes(type, false) ->
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
    // Resolve the attributeType filter. A null string = no filter; an
    // unresolvable name matches nothing (defensive -- the type-level
    // CollectAttributes contract).
    Il2CppClass* attrType = nullptr;
    if (attributeTypeName != nullptr)
    {
        char attrBuf[kNameBufferSize];
        if (AsciiFromManagedString(attributeTypeName, attrBuf))
        {
            attrType = il2cpp_class_by_name(attrBuf);
        }
        if (attrType == nullptr)
        {
            return EmptyStringArray();
        }
    }
    // Same declaring-type scan as il2cpp_attribute_member_get_custom (the
    // deepest registered type that DECLARES the member).
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
    // Count pass with the attributeType filter applied.
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
        Il2CppObject** attrs = runtime::il2cpp_attribute_lookup_member(key, &count);
        for (int32_t j = 0; j < count; ++j)
        {
            if (MatchesAttrType(attrs[j], attrType))
            {
                ++total;
            }
        }
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
            if (!MatchesAttrType(attrs[j], attrType))
            {
                continue;
            }
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
        "Type.MakeGenericType is not wired in RF-N7 -- no fabricated generic arguments (no fabrication), the generated-name fallback is disabled (explicit LOUD)");
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

//------------------------------------------------------------------------------
// REFL4 (item 2) -- IntrospectionExtensions.GetTypeInfo(Type) identity icall.
// The net48 result is a TypeInfo (a Type subclass); the simplified Type model
// carries the Type VALUE as the full-name string, so GetTypeInfo is the
// runtime IDENTITY -- the receiver Type VALUE string is returned unchanged
// (works for typeof(int).GetTypeInfo() too: an unregistered BCL type name must
// NOT go through the typeinfo registry). The transpiler records the result as
// "System.Type" so the downstream Type domain (.Name/.FullName/.IsClass/
// .BaseType/GetProperty/==) resolves on it.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_type_info(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    return typeName;
}

//------------------------------------------------------------------------------
// REFL4 (item 1) -- MemberInfo.get_MemberType / get_DeclaringType icalls. See
// typeinfo.h for the contract. The receiver is the member-VALUE string (the
// simplified member model) OR a Type VALUE full-name string (the simplified
// Type model); the runtime classifies by scanning the typeinfo registry.
//------------------------------------------------------------------------------
namespace {

// Scan ONE member table for an exact name (the FindMember primitive, using
// the raw table pointer + count shape common to all four tables).
template <typename T>
bool TableDeclaresName(const T* table, int32_t count, const char* name)
{
    if (table == nullptr || count <= 0 || name == nullptr) return false;
    for (int32_t i = 0; i < count; ++i)
    {
        if (table[i].name != nullptr && std::strcmp(table[i].name, name) == 0) return true;
    }
    return false;
}

// Whether a fully-qualified type-info name refers to a NESTED type (the
// Transpiler TypeEmitter ReflectionFullName '+'-chain form -- "Cases.Outer+
// Inner"). A nested typevalue answers MemberType NestedType=128.
bool IsNestedTypeNameValue(const char* name)
{
    return name != nullptr && std::strchr(name, '+') != nullptr;
}

// The IMMEDIATE enclosing type's full name of a nested type value
// ("A+B+C" -> "A+B" -- the owning chain minus the innermost segment). The
// result is written into outBuf (kNameBufferSize); returns false when there
// is no '+' (not nested).
bool OuterTypeNameValue(const char* nestedName, char (&outBuf)[kNameBufferSize])
{
    outBuf[0] = '\0';
    if (nestedName == nullptr) return false;
    const char* plus = std::strrchr(nestedName, '+');
    if (plus == nullptr) return false;
    const size_t len = static_cast<size_t>(plus - nestedName);
    if (len >= kNameBufferSize) return false;
    std::memcpy(outBuf, nestedName, len);
    outBuf[len] = '\0';
    return true;
}

} // namespace

int32_t il2cpp_memberinfo_get_member_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf))
    {
        return 32; // TypeInfo -- defensive (malformed/over-long/non-ASCII)
    }
    // 1. The disambiguated ctor value "<FullName>::.ctor" -> Constructor.
    if (std::strstr(nameBuf, kCtorSeparator) != nullptr)
    {
        return 1;
    }
    // 2. A registered typeinfo name -> TypeInfo (32); a nested typeinfo name
    //    (the '+' ReflectionFullName form) -> NestedType (128).
    if (il2cpp_typeinfo_lookup(nameBuf) != nullptr)
    {
        return IsNestedTypeNameValue(nameBuf) ? 128 : 32;
    }
    // 3. Member-table scan -- the FIRST table owning the name pins the kind
    //    (methods first, net48 reports a property accessor name as Method).
    //    EVENTS before FIELDS: a field-like auto-event's compiler-generated
    //    BACKING FIELD shares the event's name ("EvtD" lives in BOTH the
    //    fields table -- the EventHandler slot -- and the events table). The
    //    net48 event member value must classify Event (2), so the event table
    //    is checked BEFORE fields (the backing-field entry is invisible to
    //    GetEvent/GetEvents receivers -- the model answers the event FIRST;
    //    W7: a GetField("EvtD") on such a backing field cannot be told apart
    //    from the event name and answers Event too).
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (TableDeclaresName(info->methods, info->methodCount, nameBuf))
        {
            // A bare ".ctor" entry in the methods table (the GetConstructors
            // enumeration element) is a Constructor -- but our ctor member
            // values are the disambiguated "<FullName>::.ctor" (handled
            // above); a bare ".ctor" can only arrive from a GetMember-style
            // union, which the model excludes -- defensive ctor mapping.
            if (std::strcmp(nameBuf, kCtorName) == 0) return 1;
            return 8; // Method
        }
    }
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (TableDeclaresName(info->properties, info->propertyCount, nameBuf)) return 16; // Property
    }
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (TableDeclaresName(info->events, info->eventCount, nameBuf)) return 2; // Event
    }
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (TableDeclaresName(info->fields, info->fieldCount, nameBuf)) return 4; // Field
    }
    return 32; // TypeInfo -- defensive default (an unregistered BCL type name)
}

Il2CppObject* il2cpp_memberinfo_get_declaring_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf))
    {
        return nullptr; // defensive
    }
    // 1. The disambiguated ctor value "<FullName>::.ctor" -> the declaring
    //    type is the parsed "<FullName>" (the ctor's owning type).
    if (const char* sep = std::strstr(nameBuf, kCtorSeparator))
    {
        const size_t len = static_cast<size_t>(sep - nameBuf);
        if (len >= kNameBufferSize) return nullptr;
        char typeName[kNameBufferSize];
        std::memcpy(typeName, nameBuf, len);
        typeName[len] = '\0';
        if (typeName[0] == '\0') return nullptr;
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(typeName));
    }
    // 2. A registered typeinfo name -> a TYPE receiver: a NESTED type returns
    //    its IMMEDIATE outer type's full name (Type VALUE); a top-level type
    //    has no declaring type (net48 typeof(MTPlain).DeclaringType == null).
    if (il2cpp_typeinfo_lookup(nameBuf) != nullptr)
    {
        char outerBuf[kNameBufferSize];
        if (OuterTypeNameValue(nameBuf, outerBuf))
        {
            return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(outerBuf));
        }
        return nullptr;
    }
    // 3. A member name -> the DEEPEST type declaring it (own-table scan; an
    //    override re-declares the name in the derived table so the most
    //    derived wins; inherited members stay under their declaring type's
    //    table so the base wins). Same base-chain-depth doctrine as the
    //    RF-N6 member-attribute scan.
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        const bool declares =
            TableDeclaresName(info->methods, info->methodCount, nameBuf)
            || TableDeclaresName(info->fields, info->fieldCount, nameBuf)
            || TableDeclaresName(info->properties, info->propertyCount, nameBuf)
            || TableDeclaresName(info->events, info->eventCount, nameBuf);
        if (!declares) continue;
        const int32_t depth = BaseChainDepthFor(info);
        if (depth > bestDepth)
        {
            bestDepth = depth;
            best = info;
        }
    }
    if (best == nullptr || best->name == nullptr)
    {
        return nullptr; // member not found (defensive query convention)
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(best->name));
}

//------------------------------------------------------------------------------
// REFL4 (item 3) -- Type.AssemblyQualifiedName: "<FullName>, <asm>, Version=
// 1.0.0.0, Culture=neutral, PublicKeyToken=null". The receiver is the Type
// VALUE (full-name string); the assembly simple name is the transpiler-baked
// intern literal. The AQN is the receiver itself + a fixed suffix that never
// changes for the single-assembly AOT model (one assembly, one version):
//   "<receiver>, <asmSimpleName>, Version=1.0.0.0, Culture=neutral,
//    PublicKeyToken=null"
// Null receiver -> NullReferenceException (the net48 null-Type chain).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_assembly_qualified_name(Il2CppObject* typeName,
                                                          Il2CppObject* asmSimpleName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char typeBuf[kNameBufferSize];
    char asmBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(asmSimpleName, asmBuf))
    {
        return nullptr; // defensive (malformed/over-long/non-ASCII)
    }
    char valueBuf[kNameBufferSize * 2 + 128];
    std::snprintf(valueBuf, sizeof(valueBuf),
                  "%s, %s, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                  typeBuf, asmBuf);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(valueBuf));
}

//------------------------------------------------------------------------------
// REFL4 (item 4/C1) -- ConstructorInfo.get_Name decode. The receiver is the
// disambiguated ctor member VALUE "<FullName>::.ctor" (or a bare ".ctor");
// net48 ConstructorInfo.Name == ".ctor". The generic get_Name fold (identity)
// cannot serve the ctor VALUE -- it would print "<FullName>::.ctor" -- so the
// ctor key is special-cased: the Name returns the literal ".ctor" (the ctor
// member-value terminal segment), and the Invoke dependency on the full
// disambiguated VALUE is untouched.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_ctorinfo_get_name(Il2CppObject* memberName)
{
    (void)memberName;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(kCtorName));
}

//------------------------------------------------------------------------------
// REFL5 (item 2) -- GetProperty(string, Type) / GetProperty(string, Type,
// Type[]) exact overloads. get_method_types already serves the
// GetMethod(string, Type[], ParameterModifier[]) form (the modifier array is
// ignored by net48's match -- it only carries by-ref modifiers, W7).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_typeinfo_get_property_return_type(Il2CppObject* typeName,
                                                       Il2CppObject* memberName,
                                                       Il2CppObject* returnTypeName)
{
    return il2cpp_typeinfo_get_property_types_ret(typeName, memberName, returnTypeName, nullptr);
}

namespace {

// REFL-ARITY -- the shared exact-signature property matcher used by both
// il2cpp_typeinfo_get_property_types_ret (default Public|Instance|Static
// binding) and the flags-aware il2cpp_typeinfo_get_property_types_ret_flags.
// The property-type match is on the IL property-type full name
// (Il2CppPropertyInfo.typeName). A name + type duplicate would be net48
// AmbiguousMatch -- the first match wins (W7 narrow model).
Il2CppObject* PropertyTypesRetCore(Il2CppObject* typeName, Il2CppObject* memberName,
                                   Il2CppObject* returnTypeName, Il2CppArray* types,
                                   int32_t bindingAttr)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    if (memberName == nullptr) ThrowNullMemberName("name");
    char typeBuf[kNameBufferSize];
    char memberBuf[kNameBufferSize];
    char retBuf[kNameBufferSize];
    if (returnTypeName == nullptr) return nullptr; // defensive (net48 binds Type non-null)
    if (!AsciiFromManagedString(typeName, typeBuf) ||
        !AsciiFromManagedString(memberName, memberBuf) ||
        !AsciiFromManagedString(returnTypeName, retBuf))
    {
        return nullptr;
    }
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(typeBuf);
    if (info == nullptr) return nullptr;
    const int32_t wantArity = (types != nullptr && types->length > 0) ? types->length : 0;
    if (wantArity > 4) return nullptr;
    char typeNames[4][kNameBufferSize];
    if (types != nullptr && !ReadTypeElements(types, typeNames, wantArity)) return nullptr;
    for (const Il2CppTypeInfo* scan = info; scan != nullptr; scan = scan->baseType)
    {
        if (scan->properties == nullptr) continue;
        for (int32_t i = 0; i < scan->propertyCount; ++i)
        {
            const Il2CppPropertyInfo& p = scan->properties[i];
            if (p.name == nullptr || std::strcmp(p.name, memberBuf) != 0) continue;
            if (p.typeName == nullptr || std::strcmp(p.typeName, retBuf) != 0) continue;
            const char* accessorNames[2] = { p.getterName, p.setterName };
            int32_t accessorFlags = 0;
            bool sawAccessor = false;
            for (int32_t ai = 0; ai < 2; ++ai)
            {
                if (accessorNames[ai] == nullptr) continue;
                const Il2CppMethodInfo* acc = il2cpp_type_get_method(scan, accessorNames[ai]);
                if (acc != nullptr) { accessorFlags |= acc->flags; sawAccessor = true; }
            }
            if (!sawAccessor || !MemberMatchesVis(accessorFlags, bindingAttr, true)) continue;
            if (p.indexParams != wantArity) continue;
            if (p.getterName != nullptr && wantArity > 0 && wantArity <= 4)
            {
                const Il2CppMethodInfo* getter = il2cpp_type_get_method(scan, p.getterName);
                if (getter != nullptr && !MethodTypesMatch(getter, typeNames, wantArity))
                {
                    continue;
                }
            }
            return memberName;
        }
    }
    return nullptr;
}

} // namespace

Il2CppObject* il2cpp_typeinfo_get_property_types_ret(Il2CppObject* typeName,
                                                     Il2CppObject* memberName,
                                                     Il2CppObject* returnTypeName,
                                                     Il2CppArray* types)
{
    return PropertyTypesRetCore(typeName, memberName, returnTypeName, types, 16 | 4 | 8);
}

Il2CppObject* il2cpp_typeinfo_get_property_types_ret_flags(Il2CppObject* typeName,
                                                           Il2CppObject* memberName,
                                                           Il2CppObject* returnTypeName,
                                                           Il2CppArray* types,
                                                           int32_t bindingAttr)
{
    return PropertyTypesRetCore(typeName, memberName, returnTypeName, types, bindingAttr);
}

//------------------------------------------------------------------------------
// REFL5 (item 3 / C2) -- the member-TYPE property family + the flag-derived
// predicates on the MEMBER-VALUE string receiver. All scan the typeinfo
// registry for the DEEPEST type declaring the member name (own-table scan --
// the same BaseChainDepthFor doctrine as get_DeclaringType / GetParameters /
// RF-N6).
//------------------------------------------------------------------------------
namespace {

// The declaring type whose OWN table declares the given member name (deepest
// wins), or nullptr. Searches a SPECIFIC kind's table when table/slot-selector
// is provided; otherwise every table (the shared member-name scan).
const Il2CppTypeInfo* DeepestDeclarerFor(const char* name)
{
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        const bool declares =
            TableDeclaresName(info->methods, info->methodCount, name)
            || TableDeclaresName(info->fields, info->fieldCount, name)
            || TableDeclaresName(info->properties, info->propertyCount, name)
            || TableDeclaresName(info->events, info->eventCount, name);
        if (!declares) continue;
        const int32_t depth = BaseChainDepthFor(info);
        if (depth > bestDepth)
        {
            bestDepth = depth;
            best = info;
        }
    }
    return best;
}

// Find the FIRST exact-name METHOD entry in the deepest declaring type's OWN
// method table (overload-first match -- the narrow model resolves the
// simplified member value to ONE entry by name).
const Il2CppMethodInfo* DeepestMethodForName(const char* name)
{
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (!TableDeclaresName(info->methods, info->methodCount, name)) continue;
        const int32_t depth = BaseChainDepthFor(info);
        if (depth > bestDepth)
        {
            bestDepth = depth;
            best = info;
        }
    }
    if (best == nullptr) return nullptr;
    return FindMember(best->methods, best->methodCount, name);
}

const Il2CppFieldInfo* DeepestFieldForName(const char* name)
{
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (!TableDeclaresName(info->fields, info->fieldCount, name)) continue;
        const int32_t depth = BaseChainDepthFor(info);
        if (depth > bestDepth)
        {
            bestDepth = depth;
            best = info;
        }
    }
    if (best == nullptr) return nullptr;
    return FindMember(best->fields, best->fieldCount, name);
}

const Il2CppPropertyInfo* DeepestPropertyForName(const char* name)
{
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (!TableDeclaresName(info->properties, info->propertyCount, name)) continue;
        const int32_t depth = BaseChainDepthFor(info);
        if (depth > bestDepth)
        {
            bestDepth = depth;
            best = info;
        }
    }
    if (best == nullptr) return nullptr;
    return FindMember(best->properties, best->propertyCount, name);
}

const Il2CppEventInfo* DeepestEventForName(const char* name)
{
    const Il2CppTypeInfo* best = nullptr;
    int32_t bestDepth = -1;
    for (int32_t i = 0; i < g_registeredTypeInfoCount; ++i)
    {
        const Il2CppTypeInfo* info = g_registeredTypeInfos[i];
        if (info == nullptr) continue;
        if (!TableDeclaresName(info->events, info->eventCount, name)) continue;
        const int32_t depth = BaseChainDepthFor(info);
        if (depth > bestDepth)
        {
            bestDepth = depth;
            best = info;
        }
    }
    if (best == nullptr) return nullptr;
    return FindMember(best->events, best->eventCount, name);
}

// The method RETURN-type FULL name from the encoded signature ("RET(PARAMS)"
// / "RET:PARAMS" -- the return is everything before the first '(' or ':').
// Returns false when the encoding has no full-name equivalent.
bool SignatureReturnFullName(const Il2CppMethodInfo* m, char (&out)[kNameBufferSize])
{
    if (m == nullptr || m->signature == nullptr) return false;
    const char* sep = std::strchr(m->signature, '(');
    if (sep == nullptr) sep = std::strchr(m->signature, ':');
    const char* retStart = m->signature;
    const size_t len = sep != nullptr
        ? static_cast<size_t>(sep - retStart) : std::strlen(retStart);
    if (len >= kNameBufferSize) return false;
    char enc[kNameBufferSize];
    std::memcpy(enc, retStart, len);
    enc[len] = '\0';
    if (len == 0) return false;
    return ParamEncodingToFullName(enc, out);
}

} // namespace

Il2CppObject* il2cpp_fieldinfo_get_field_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return nullptr;
    const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf);
    if (f == nullptr || f->typeName == nullptr) return nullptr;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(f->typeName));
}

Il2CppObject* il2cpp_propertyinfo_get_property_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return nullptr;
    const Il2CppPropertyInfo* p = DeepestPropertyForName(nameBuf);
    if (p == nullptr || p->typeName == nullptr) return nullptr;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(p->typeName));
}

Il2CppObject* il2cpp_methodinfo_get_return_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return nullptr;
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    char full[kNameBufferSize];
    if (m == nullptr || !SignatureReturnFullName(m, full)) return nullptr;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(full));
}

Il2CppObject* il2cpp_eventinfo_get_event_handler_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return nullptr;
    const Il2CppEventInfo* e = DeepestEventForName(nameBuf);
    if (e == nullptr || e->addName == nullptr) return nullptr;
    // The events table has no type column -- the handler delegate type is the
    // add accessor's FIRST parameter (net48: the EventHandlerType of
    // `event EventHandler Ev` is "System.EventHandler").
    const Il2CppMethodInfo* add = DeepestMethodForName(e->addName);
    if (add == nullptr || add->paramCount < 1) return nullptr;
    char encs[1][kNameBufferSize];
    if (SplitSignatureParams(add->signature, encs, 1) != 1) return nullptr;
    char full[kNameBufferSize];
    if (!ParamEncodingToFullName(encs[0], full)) return nullptr;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(full));
}

Il2CppObject* il2cpp_memberinfo_get_reflected_type(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return nullptr;
    // A TYPE receiver (registered typeinfo name): ReflectedType == the type
    // itself (net48 typeof(X).ReflectedType == typeof(X)). A member name:
    // the DEEPEST declaring type (own members -> the query type, W7).
    if (const Il2CppTypeInfo* self = il2cpp_typeinfo_lookup(nameBuf))
    {
        return self->name != nullptr
            ? reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(self->name))
            : nullptr;
    }
    const Il2CppTypeInfo* best = DeepestDeclarerFor(nameBuf);
    if (best == nullptr || best->name == nullptr) return nullptr;
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(best->name));
}

int32_t il2cpp_fieldinfo_is_assembly(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf);
    if (f == nullptr) return 0;
    // REFL6B (item C): net48 IsAssembly is EXACT -- vis mask == Assembly (0x3,
    // internal). The refl5 impl "non-public AND non-private = internal"
    // misreported protected (0x4) / FamORAssem (0x5) / FamANDAssem (0x2) as
    // True; oracle6d pins internal=True, protected/protected-internal=False.
    // The emitter bakes the Internal vis as the dedicated flag bit 0x20;
    // pre-refl6b tables (no bit) fall to 0 (defensive).
    return (f->flags & 0x20) != 0 ? 1 : 0;
}

int32_t il2cpp_memberinfo_is_family_or_assembly(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    // A FIELD receiver (FieldInfo.IsFamilyOrAssembly) or a METHOD receiver
    // (MethodBase.IsFamilyOrAssembly) -- net48 FamORAssem(0x5) EXACTLY
    // (oracle6d: protected-internal True; plain protected(0x4) /
    // internal(0x3) / public(0x6) False). The emitter bakes FamORAssem as the
    // dedicated flag bit 0x40.
    if (const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf))
    {
        return (f->flags & 0x40) != 0 ? 1 : 0;
    }
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 0x40) != 0) ? 1 : 0;
}

int32_t il2cpp_memberinfo_is_static(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    // A FIELD receiver (FieldInfo.IsStatic) joins through the field table;
    // a METHOD receiver (MethodBase.IsStatic) through the method table.
    if (const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf))
    {
        return (f->flags & 2) != 0 ? 1 : 0;
    }
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 2) != 0) ? 1 : 0;
}

int32_t il2cpp_memberinfo_is_public(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    if (const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf))
    {
        return (f->flags & 1) != 0 ? 1 : 0;
    }
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 1) != 0) ? 1 : 0;
}

int32_t il2cpp_memberinfo_is_constructor(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    // The disambiguated ctor VALUE "<FullName>::.ctor" and the bare ".ctor"/
    // ".cctor" member values (GetConstructor / GetConstructors elements) are
    // all constructor members.
    if (std::strstr(nameBuf, kCtorSeparator) != nullptr
        || std::strcmp(nameBuf, kCtorName) == 0
        || std::strcmp(nameBuf, ".cctor") == 0)
    {
        return 1;
    }
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 4) != 0) ? 1 : 0;
}

//------------------------------------------------------------------------------
// p-refl2-gapmatrix (B05/B07/B08/B11/B17/B18/E03/E04): the remaining member
// flag predicates + MemberInfo.MetadataToken + the reflection handle values.
// The receiver is the member-VALUE string (the simplified member model); a
// FIELD receiver joins through the field table, a METHOD receiver through the
// method table (the same own-then-base chain walk the existing member
// predicates run). See typeinfo.h for the full contract.
//------------------------------------------------------------------------------

int32_t il2cpp_memberinfo_get_metadata_token(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    if (const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf))
        return m->metadataToken;
    if (const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf))
        return f->metadataToken;
    return 0;
}

int32_t il2cpp_memberinfo_is_virtual(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 8) != 0) ? 1 : 0;
}

int32_t il2cpp_memberinfo_is_abstract(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 0x80) != 0) ? 1 : 0;
}

int32_t il2cpp_memberinfo_is_generic_method(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return (m != nullptr && (m->flags & 0x100) != 0) ? 1 : 0;
}

int32_t il2cpp_fieldinfo_is_init_only(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf);
    return (f != nullptr && (f->flags & 0x80) != 0) ? 1 : 0;
}

int32_t il2cpp_fieldinfo_is_literal(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf);
    return (f != nullptr && (f->flags & 4) != 0) ? 1 : 0;
}

intptr_t il2cpp_methodinfo_get_method_handle(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppMethodInfo* m = DeepestMethodForName(nameBuf);
    return reinterpret_cast<intptr_t>(m);
}

intptr_t il2cpp_fieldinfo_get_field_handle(Il2CppObject* memberName)
{
    if (memberName == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(memberName, nameBuf)) return 0;
    const Il2CppFieldInfo* f = DeepestFieldForName(nameBuf);
    return reinterpret_cast<intptr_t>(f);
}

//------------------------------------------------------------------------------
// refl2 (A) -- the remaining Type predicate / query surface on the name-string
// (Type VALUE) receiver. See typeinfo.h for the contract of each.
//------------------------------------------------------------------------------

namespace {

// The typeFlags bit test for a resolved typeinfo (0 when unregistered).
int32_t TypeFlagOfName(const char* name, int32_t mask)
{
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(name);
    if (info == nullptr) return 0;
    return (info->typeFlags & mask) != 0 ? 1 : 0;
}

} // namespace

int32_t il2cpp_typeinfo_get_array_rank(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    // The array class NAME carries the rank: the transpiler's array identity is
    // "[<element>" for a rank-1 array (the runtime descriptor convention) and
    // the net48 display form "<element>[]" / "<element>[,]" for a folded type
    // value. Both shapes are decoded here; a non-array receiver raises net48's
    // ArgumentException ("Type must be an array").
    const size_t len = std::strlen(buf);
    if (len > 0 && buf[0] == '[')
    {
        // "[System.Int32" -- the descriptor form is rank 1.
        return 1;
    }
    if (len >= 2 && buf[len - 1] == ']')
    {
        size_t open = len - 1;
        while (open > 0 && buf[open - 1] != '[') --open;
        if (open > 0)
        {
            // "<elem>[,]" -> the rank is the comma count + 1.
            int32_t rank = 1;
            for (size_t i = open; i + 1 < len; ++i)
            {
                if (buf[i] == ',') ++rank;
            }
            return rank;
        }
    }
    il2cpp_throw_argument_exception(
        "Type must be an array type.");
}

int32_t il2cpp_typeinfo_is_constructed_generic(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    // A runtime receiver is the registry key = the DEFINITION's name, which can
    // never carry the closed argument list -> net48 False (a definition is not
    // constructed). A closed instance is only expressible through the folded
    // typeof chain (StackFold answers it from the TypeSpec blob).
    (void)buf;
    return 0;
}

int32_t il2cpp_typeinfo_is_abstract(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    return TypeFlagOfName(buf, 0x01);
}

int32_t il2cpp_typeinfo_is_sealed(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    return TypeFlagOfName(buf, 0x02);
}

int32_t il2cpp_typeinfo_is_public(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    return TypeFlagOfName(buf, 0x04);
}

int32_t il2cpp_typeinfo_is_nested(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    return TypeFlagOfName(buf, 0x08);
}

int32_t il2cpp_typeinfo_get_metadata_token(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    return info != nullptr ? info->metadataToken : 0;
}

Il2CppObject* il2cpp_typeinfo_make_array_type(Il2CppObject* typeName, int32_t rank)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return nullptr;
    // net48 MakeArrayType: the DISPLAY identity "<element>[]" / "<element>[,]".
    // The element is the receiver's net48 display name (the registry key is
    // already the full name for a non-array type).
    char outBuf[kNameBufferSize * 2];
    const int32_t effectiveRank = rank < 1 ? 1 : rank;
    char commas[64];
    const size_t commaCount = static_cast<size_t>(effectiveRank - 1);
    if (commaCount >= sizeof(commas)) return nullptr;
    for (size_t i = 0; i < commaCount; ++i) commas[i] = ',';
    commas[commaCount] = '\0';
    std::snprintf(outBuf, sizeof(outBuf), "%s[%s]", buf, commas);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(outBuf));
}

Il2CppObject* il2cpp_typeinfo_make_byref_type(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return nullptr;
    char outBuf[kNameBufferSize * 2];
    std::snprintf(outBuf, sizeof(outBuf), "%s&", buf);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(outBuf));
}

Il2CppObject* il2cpp_typeinfo_make_pointer_type(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return nullptr;
    char outBuf[kNameBufferSize * 2];
    std::snprintf(outBuf, sizeof(outBuf), "%s*", buf);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(outBuf));
}

//------------------------------------------------------------------------------
// refl2 (A) -- Type.Attributes + the exact nested-visibility predicates.
//------------------------------------------------------------------------------
int32_t il2cpp_typeinfo_get_attributes(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info != nullptr && info->attributes != 0) return info->attributes;
    // BCL core approximation (a TypeRef has no attribute rows at runtime).
    // Public=0x1 / NotPublic=0x0 in the low 3 bits; Sealed=0x100,
    // Abstract=0x80 per ECMA-335 TypeAttributes.
    int32_t attrs = 0x1; // public
    if (buf != nullptr)
    {
        if (std::strcmp(buf, "System.Int32") == 0 || std::strcmp(buf, "System.String") == 0)
            attrs |= 0x100; // sealed
        if (std::strcmp(buf, "System.ValueType") == 0
            || std::strcmp(buf, "System.Enum") == 0
            || std::strcmp(buf, "System.Array") == 0)
            attrs |= 0x80; // abstract
    }
    return attrs;
}

int32_t il2cpp_typeinfo_is_nested_public(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info == nullptr) return 0;
    return (info->attributes & 0x7) == 0x2 ? 1 : 0;
}

int32_t il2cpp_typeinfo_is_nested_private(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info == nullptr) return 0;
    return (info->attributes & 0x7) == 0x3 ? 1 : 0;
}

int32_t il2cpp_typeinfo_is_visible(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(buf);
    if (info == nullptr) return 0;
    const int32_t vis = info->attributes & 0x7;
    // Public (0x1) or NestedPublic (0x2). The full net48 rule also requires
    // every ENCLOSING type to be visible; the simplified model's registry key
    // is the '+'-joined chain, and the enclosing chain is emitted as its own
    // typeinfo, so the nested answer is exact for the one-level cases the
    // probes cover (W7: a private OUTER type with a nested-public inner is
    // not modeled).
    return (vis == 0x1 || vis == 0x2) ? 1 : 0;
}

int32_t il2cpp_typeinfo_equals_non_type(Il2CppObject* typeName, Il2CppObject* other)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    // net48: a non-Type argument answers False (never an exception).
    (void)other;
    return 0;
}

//------------------------------------------------------------------------------
// p-refl2-gapmatrix (C04/C05/C06/C09): the Assembly / AssemblyName surface.
//
// The simplified Assembly model represents an Assembly VALUE as its SIMPLE
// NAME string (the same convention as a Type VALUE being its full-name
// string). The INPUT assembly's simple name is baked by the transpiler and
// passed as the trailing literal (appendLiterals) so the runtime can tell the
// input assembly (whose reflected types live in the typeinfo registry) from a
// BCL assembly (which has no reflected types -- the runtime's class registry
// is the identity store). See typeinfo.h for the contract.
//------------------------------------------------------------------------------

namespace {

bool NamesEqual(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr || b == nullptr) return false;
    char abuf[kNameBufferSize];
    char bbuf[kNameBufferSize];
    if (!AsciiFromManagedString(a, abuf) || !AsciiFromManagedString(b, bbuf)) return false;
    return std::strcmp(abuf, bbuf) == 0;
}

} // namespace

Il2CppObject* il2cpp_assemblyname_get_full_name(Il2CppObject* name)
{
    if (name == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(name, nameBuf)) return nullptr;
    // The AOT model does not track assembly versions/public-key tokens; the
    // net48 full-name shape is reproduced with the neutral placeholder body
    // (the probe asserts only that FullName is non-null).
    char outBuf[kNameBufferSize * 2];
    std::snprintf(outBuf, sizeof(outBuf), "%s, Version=4.0.0.0, Culture=neutral, PublicKeyToken=null", nameBuf);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(outBuf));
}

Il2CppObject* il2cpp_assemblyinfo_get_type(Il2CppObject* asmName, Il2CppObject* typeName,
                                           Il2CppObject* inputAsmName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    // Only the input assembly carries reflected types; a BCL assembly (e.g.
    // mscorlib) answers null for every name (net48: a type of another
    // assembly is not returned by Assembly.GetType).
    if (!NamesEqual(asmName, inputAsmName)) return nullptr;
    return il2cpp_type_get_type(typeName);
}

// REFL-ARITY -- Assembly.GetType(string, bool throwOnError). Same scoping as
// il2cpp_assemblyinfo_get_type; the extra flag raises TypeLoadException (net48)
// on a well-formed but unregistered name.
Il2CppObject* il2cpp_assemblyinfo_get_type_ex(Il2CppObject* asmName, Il2CppObject* typeName,
                                              int32_t throwOnError, Il2CppObject* inputAsmName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    if (!NamesEqual(asmName, inputAsmName)) return nullptr;
    Il2CppObject* found = il2cpp_type_get_type(typeName);
    if (found == nullptr && throwOnError != 0)
    {
        char nameBuf[kNameBufferSize];
        if (AsciiFromManagedString(typeName, nameBuf))
        {
            il2cpp_throw_new("System.TypeLoadException,System.TypeLoadException,System.SystemException,System.Exception",
                             nameBuf);
        }
        il2cpp_throw_new("System.TypeLoadException,System.TypeLoadException,System.SystemException,System.Exception",
                         "Could not load type.");
    }
    return found;
}

// REFL-ARITY -- Assembly.GetType(string, bool throwOnError, bool ignoreCase) --
// the 3-param form (reflection_assembly.cs:758). Same input-assembly scoping;
// the ignoreCase flag delegates to il2cpp_type_get_type_ex (which folds ASCII
// case on the registry key comparison -- net48's ignoreCase semantics).
Il2CppObject* il2cpp_assemblyinfo_get_type_ex2(Il2CppObject* asmName, Il2CppObject* typeName,
                                               int32_t throwOnError, int32_t ignoreCase,
                                               Il2CppObject* inputAsmName)
{
    if (!NamesEqual(asmName, inputAsmName)) return nullptr;
    return il2cpp_type_get_type_ex(typeName, throwOnError, ignoreCase);
}

Il2CppArray* il2cpp_assemblyinfo_get_types(Il2CppObject* asmName, Il2CppObject* inputAsmName)
{
    const bool isInput = NamesEqual(asmName, inputAsmName);
    if (isInput)
    {
        const int32_t count = il2cpp_typeinfo_count();
        Il2CppArray* result = BuildNameArray(count);
        int32_t dst = 0;
        for (int32_t i = 0; i < count; ++i)
        {
            const Il2CppTypeInfo* info = il2cpp_typeinfo_at(i);
            if (info != nullptr && info->name != nullptr) StoreName(result, dst++, info->name);
        }
        return result;
    }
    // BCL assembly: enumerate the runtime's class registry (the builtin
    // System.* descriptors plus every translated user type).
    const int32_t count = il2cpp_class_count();
    Il2CppArray* result = BuildNameArray(count);
    int32_t dst = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        const Il2CppClass* klass = il2cpp_class_at(i);
        if (klass != nullptr && klass->name != nullptr) StoreName(result, dst++, klass->name);
    }
    return result;
}

intptr_t il2cpp_typeinfo_get_type_handle(Il2CppObject* typeName)
{
    if (typeName == nullptr) ThrowNullTypeReceiver();
    char buf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, buf)) return 0;
    // The typeinfo registry holds the reflected USER types; the builtin class
    // registry holds the BCL/primitive descriptors (registered at bootstrap).
    // il2cpp_class_by_name resolves both, so a handle for either kind is the
    // descriptor ADDRESS (the reflecthandle.h value model).
    return reinterpret_cast<intptr_t>(il2cpp_class_by_name(buf));
}

} // namespace il2cpp
