//------------------------------------------------------------------------------
// attribute.cpp -- System.Attribute runtime registry (see attribute.h).
//
// Flat fixed-capacity registry: type name -> attribute object array.
// Registration is single-threaded startup (transpiler-emitted static
// initializers); queries are read-only afterwards -- no lock (see header
// threading contract).
//------------------------------------------------------------------------------

#include "attribute.h"

#include "il2cpp_runtime.h"   // il2cpp_class_by_name (klass resolution for R4 _ex_named)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null_exception (R4 null element)
#include "il2cpp_string.h" // Il2CppString (managed-string ABI queries)

#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// Registry capacity. AOT programs annotate a bounded number of types; this
// must be kept in sync with what the transpiler emits (overflow is a loud
// rejection, not silent truncation -- see the register contract).
constexpr int32_t kMaxAttributeTypes = 32;

struct AttributeEntry
{
    const char*    typeName;   // registry key: full type name (static string)
    Il2CppObject** attributes; // the register-time array (owned by generated code)
    int32_t        count;      // number of attributes
};

AttributeEntry g_entries[kMaxAttributeTypes];
int32_t        g_entryCount = 0; // number of live entries

//------------------------------------------------------------------------------
// AttributeUsage registry (net48 AttributeUsageAttribute mirror). Keyed by the
// attribute class's full name; consulted by the inheritance walk. Shares the
// single-threaded-startup / read-only-afterwards contract with the attribute
// registry above (see header threading note).
//------------------------------------------------------------------------------

// net48 AttributeUsageAttribute.Default: Inherited = true, AllowMultiple =
// false -- used for attribute classes without a registered usage.
constexpr int32_t kDefaultAllowMultiple = 0;
constexpr int32_t kDefaultInherited     = 1;

// The inheritance walk stops before System.Object (net48). The root descriptor
// always carries exactly this name.
constexpr const char* kObjectTypeName = "System.Object";

struct AttributeUsageEntry
{
    const char* typeName;    // registry key: attribute class full name
    int32_t     allowMultiple; // net48 AllowMultiple (0/1)
    int32_t     inherited;     // net48 Inherited (0/1)
};

AttributeUsageEntry g_usage[kMaxAttributeTypes];
int32_t             g_usageCount = 0;

// Linear key lookup for the usage registry; returns the index or -1.
int32_t FindUsage(const char* typeName)
{
    for (int32_t i = 0; i < g_usageCount; ++i)
    {
        if (std::strcmp(g_usage[i].typeName, typeName) == 0)
        {
            return i;
        }
    }
    return -1;
}

// AttributeUsage of an attribute instance's runtime class; net48 defaults when
// the class has no registered usage (or a malformed klass/name).
void GetAttributeUsage(Il2CppClass* attrKlass, int32_t* allowMultiple, int32_t* inherited)
{
    *allowMultiple = kDefaultAllowMultiple;
    *inherited = kDefaultInherited;
    if (attrKlass == nullptr || attrKlass->name == nullptr)
    {
        return;
    }
    const int32_t index = FindUsage(attrKlass->name);
    if (index >= 0)
    {
        *allowMultiple = g_usage[index].allowMultiple;
        *inherited = g_usage[index].inherited;
    }
}

// net48 attributeType filter: nullptr matches everything; otherwise the
// attribute's runtime class must equal attributeType or derive from it (walk
// the klass->baseClass chain -- attributeType.IsAssignableFrom).
bool MatchesAttributeType(Il2CppClass* attrKlass, Il2CppClass* attributeType)
{
    if (attributeType == nullptr)
    {
        return true;
    }
    for (Il2CppClass* current = attrKlass; current != nullptr; current = current->baseClass)
    {
        if (current == attributeType)
        {
            return true;
        }
    }
    return false;
}

// Exact-type membership in the shadowing set (net48 AllowMultiple = false rule
// keys on the attribute's exact runtime type).
bool ContainsType(const Il2CppClass* const* seen, int32_t seenCount, const Il2CppClass* type)
{
    for (int32_t i = 0; i < seenCount; ++i)
    {
        if (seen[i] == type)
        {
            return true;
        }
    }
    return false;
}

// Linear key lookup for the attribute registry; returns the entry index or -1
// (defined below; forward-declared here for CollectAttributes).
int32_t FindEntry(const char* typeName);

//------------------------------------------------------------------------------
// CollectAttributes -- the shared net48-semantics walk behind get_custom_ex /
// is_defined / get_single. See the header contract of
// il2cpp_attribute_get_custom_ex for the full rules.
//------------------------------------------------------------------------------
int32_t CollectAttributes(Il2CppClass* klass,
                          Il2CppClass* attributeType,
                          int32_t inherit,
                          Il2CppObject** results,
                          int32_t resultsCapacity)
{
    if (klass == nullptr || klass->name == nullptr)
    {
        return 0;
    }
    const Il2CppClass* seen[kMaxAttributeTypes]; // exact types already emitted
    int32_t            seenCount = 0;
    int32_t            written = 0;
    bool               firstLevel = true;
    for (Il2CppClass* level = klass; level != nullptr; level = level->baseClass)
    {
        if (level->name == nullptr)
        {
            break; // malformed chain (a descriptor without a name); stop
        }
        if (!firstLevel)
        {
            if (std::strcmp(level->name, kObjectTypeName) == 0)
            {
                break; // net48: the walk stops before System.Object
            }
            if (!inherit)
            {
                break; // own type only: no base-level walk
            }
        }
        const int32_t index = FindEntry(level->name);
        if (index >= 0)
        {
            const AttributeEntry& entry = g_entries[index];
            for (int32_t i = 0; i < entry.count; ++i)
            {
                Il2CppObject* const   attr = entry.attributes[i];
                Il2CppClass* const    attrKlass = (attr != nullptr) ? attr->klass : nullptr;
                if (!MatchesAttributeType(attrKlass, attributeType))
                {
                    continue;
                }
                int32_t allowMultiple = kDefaultAllowMultiple;
                int32_t inherited = kDefaultInherited;
                GetAttributeUsage(attrKlass, &allowMultiple, &inherited);
                if (!firstLevel && !inherited)
                {
                    continue; // base-class attribute not inheritable (net48)
                }
                const bool alreadyEmitted = attrKlass != nullptr &&
                                            ContainsType(seen, seenCount, attrKlass);
                if (alreadyEmitted && !(inherited && allowMultiple))
                {
                    continue; // AllowMultiple = false: more-derived wins (net48)
                }
                if (attrKlass != nullptr && !alreadyEmitted && seenCount < kMaxAttributeTypes)
                {
                    seen[seenCount++] = attrKlass;
                }
                if (results != nullptr && written < resultsCapacity)
                {
                    results[written] = attr;
                }
                ++written;
            }
        }
        firstLevel = false;
    }
    return written;
}

// Linear key lookup (attribute queries are cold reflection paths; a handful
// of entries). Returns the entry index or -1 when the key is absent.
int32_t FindEntry(const char* typeName)
{
    for (int32_t i = 0; i < g_entryCount; ++i)
    {
        if (std::strcmp(g_entries[i].typeName, typeName) == 0)
        {
            return i;
        }
    }
    return -1;
}

} // namespace

namespace {
//------------------------------------------------------------------------------
// AsciiFromManagedString -- UTF-16 managed string -> ASCII C-string buffer.
//
// The managed-string query ABI (D-ATTR-1, mirroring typeinfo.cpp) receives the
// queried type NAME as a managed string. Type names are ASCII by project
// convention (generated code is pure ASCII and the transpiler emits ASCII name
// literals), so the transcoding is a plain per-unit truncation with a non-ASCII
// escape (the string is treated as "cannot name a type" rather than
// mis-compared). Buffer size 256 covers every realistic full type name; an
// over-long input also escapes. Mirrors typeinfo.cpp's AsciiFromManagedString
// exactly (the two registries share the same name contract).
//------------------------------------------------------------------------------
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
// il2cpp_attribute_register -- see header for the full contract.
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_register(const char* typeName,
                                  Il2CppObject* const* attributes,
                                  int32_t count)
{
    if (typeName == nullptr || count < 0)
    {
        return 0;
    }
    if (count > 0 && attributes == nullptr)
    {
        return 0;
    }
    // Empty registration is a no-op by contract: a type without attributes
    // is an unregistered type.
    if (count == 0)
    {
        return 1;
    }
    const int32_t existing = FindEntry(typeName);
    if (existing >= 0)
    {
        // Re-registration replaces the stored array; always succeeds (the
        // entry already occupies its slot, so the capacity bound is not
        // touched). The const cast drops the caller's read-only qualification
        // -- the registry itself stores mutable pointers per its contract.
        g_entries[existing].attributes = const_cast<Il2CppObject**>(attributes);
        g_entries[existing].count = count;
        return 1;
    }
    if (g_entryCount >= kMaxAttributeTypes)
    {
        return 0; // loud rejection: the transpiler must raise capacity
    }
    g_entries[g_entryCount].typeName = typeName;
    g_entries[g_entryCount].attributes = const_cast<Il2CppObject**>(attributes);
    g_entries[g_entryCount].count = count;
    ++g_entryCount;
    return 1;
}

//------------------------------------------------------------------------------
// il2cpp_attribute_get_custom -- see header for the full contract.
//------------------------------------------------------------------------------
Il2CppObject** il2cpp_attribute_get_custom(Il2CppClass* klass,
                                           int32_t* outCount)
{
    if (outCount != nullptr)
    {
        *outCount = 0;
    }
    if (klass == nullptr || klass->name == nullptr || outCount == nullptr)
    {
        return nullptr;
    }
    const int32_t index = FindEntry(klass->name);
    if (index < 0)
    {
        return nullptr;
    }
    *outCount = g_entries[index].count;
    return g_entries[index].attributes;
}

//------------------------------------------------------------------------------
// il2cpp_attribute_get_custom_named -- see header for the full contract.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_attribute_get_custom_named(Il2CppObject* typeName)
{
    // net48: GetCustomAttributes on a type without attributes (registered or
    // not) returns an EMPTY array, never null. A null/malformed typeName
    // cannot name a type, so no attributes can match -- same empty array.
    if (typeName == nullptr)
    {
        return il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, 0);
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, nameBuf))
    {
        return il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, 0);
    }
    const int32_t index = FindEntry(nameBuf);
    if (index < 0 || g_entries[index].count <= 0)
    {
        return il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, 0);
    }
    const int32_t count = g_entries[index].count;
    Il2CppArray* const result = il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, count);
    for (int32_t i = 0; i < count; ++i)
    {
        // Reference-element array: the payload holds Il2CppObject* entries
        // (the registered attribute objects, declaration order preserved).
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(result, i)) =
            g_entries[index].attributes[i];
    }
    return result;
}

//------------------------------------------------------------------------------
// il2cpp_attribute_get_single_named -- see header for the full contract.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_attribute_get_single_named(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        return nullptr;
    }
    char nameBuf[kNameBufferSize];
    if (!AsciiFromManagedString(typeName, nameBuf))
    {
        return nullptr;
    }
    const int32_t index = FindEntry(nameBuf);
    if (index < 0 || g_entries[index].count <= 0)
    {
        return nullptr;
    }
    return g_entries[index].attributes[0];
}

//------------------------------------------------------------------------------
// il2cpp_attribute_register_usage -- see header for the full contract.
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_register_usage(const char* attributeTypeName,
                                        int32_t allowMultiple,
                                        int32_t inherited)
{
    if (attributeTypeName == nullptr)
    {
        return 0;
    }
    const int32_t existing = FindUsage(attributeTypeName);
    if (existing >= 0)
    {
        g_usage[existing].allowMultiple = allowMultiple != 0 ? 1 : 0;
        g_usage[existing].inherited = inherited != 0 ? 1 : 0;
        return 1;
    }
    if (g_usageCount >= kMaxAttributeTypes)
    {
        return 0; // loud rejection: the transpiler must raise capacity
    }
    g_usage[g_usageCount].typeName = attributeTypeName;
    g_usage[g_usageCount].allowMultiple = allowMultiple != 0 ? 1 : 0;
    g_usage[g_usageCount].inherited = inherited != 0 ? 1 : 0;
    ++g_usageCount;
    return 1;
}

//------------------------------------------------------------------------------
// il2cpp_attribute_get_custom_ex -- see header for the full contract.
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_get_custom_ex(Il2CppClass* klass,
                                       Il2CppClass* attributeType,
                                       int32_t inherit,
                                       Il2CppObject** results,
                                       int32_t resultsCapacity)
{
    const int32_t capacity = (results != nullptr && resultsCapacity > 0) ? resultsCapacity : 0;
    return CollectAttributes(klass, attributeType, inherit, results, capacity);
}

//------------------------------------------------------------------------------
// il2cpp_attribute_is_defined -- see header for the full contract.
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_is_defined(Il2CppClass* klass,
                                    Il2CppClass* attributeType,
                                    int32_t inherit)
{
    return CollectAttributes(klass, attributeType, inherit, nullptr, 0) > 0 ? 1 : 0;
}

//------------------------------------------------------------------------------
// il2cpp_attribute_get_single -- see header for the full contract.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_attribute_get_single(Il2CppClass* klass,
                                          Il2CppClass* attributeType,
                                          int32_t inherit,
                                          int32_t* outCount)
{
    if (outCount != nullptr)
    {
        *outCount = 0;
    }
    Il2CppObject* first = nullptr;
    int32_t       matches = 0;
    Il2CppObject* scratch[1];
    const int32_t count = CollectAttributes(klass, attributeType, inherit, scratch, 1);
    if (count > 0)
    {
        first = scratch[0];
    }
    matches = count;
    if (outCount != nullptr)
    {
        *outCount = matches;
    }
    return first;
}

//------------------------------------------------------------------------------
// R4 (B3) shared body: resolve the queried type name + optional attributeType
// name to klass descriptors (il2cpp_class_by_name), then run CollectAttributes.
//
// net48: a null ELEMENT argument (the queried type) is ArgumentNullException
// -- the GetCustomAttributes(MemberInfo/Type element) reflection-static
// contract. attributeTypeName null = NO filter (the net48 2-arg inherit-only
// overload passes only element + inherit); a null attributeType string cannot
// be resolved and matches nothing (empty array / null first). Malformed /
// non-ASCII / over-long names escape to "no matches" like the custom_named
// path.
//------------------------------------------------------------------------------
namespace {

Il2CppClass* ResolveClassByName(const char* name)
{
    if (name == nullptr || name[0] == '\0')
    {
        return nullptr;
    }
    return il2cpp_class_by_name(name);
}

// The typeName element is REQUIRED (net48 ArgumentNullException on a null
// element argument); attributeTypeName is OPTIONAL (a null string = no
// filter). Returns false when the required element is null (an exception has
// been thrown) or when a present attributeTypeName cannot be resolved (no
// matches -- handled by the caller via a null filter result convention).
bool ResolveElements(Il2CppObject* typeName, Il2CppObject* attributeTypeName,
                     char (&typeBuf)[kNameBufferSize],
                     char (&attrBuf)[kNameBufferSize],
                     Il2CppClass** klassOut,
                     Il2CppClass** attrTypeOut,
                     bool* attrTypeGiven)
{
    *klassOut = nullptr;
    *attrTypeOut = nullptr;
    *attrTypeGiven = false;
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("element");
        return false;
    }
    if (!AsciiFromManagedString(typeName, typeBuf))
    {
        // Malformed element: cannot name a type -> no matches (contract), no throw.
        *klassOut = nullptr;
        return false;
    }
    *klassOut = ResolveClassByName(typeBuf);
    if (attributeTypeName != nullptr)
    {
        *attrTypeGiven = true;
        if (AsciiFromManagedString(attributeTypeName, attrBuf))
        {
            *attrTypeOut = ResolveClassByName(attrBuf);
        }
        else
        {
            *attrTypeOut = nullptr;
        }
    }
    return true;
}

} // namespace

Il2CppArray* il2cpp_attribute_get_custom_ex_named(Il2CppObject* typeName,
                                                  Il2CppObject* attributeTypeName,
                                                  int32_t inherit)
{
    char typeBuf[kNameBufferSize];
    char attrBuf[kNameBufferSize];
    Il2CppClass* klass = nullptr;
    Il2CppClass* attrType = nullptr;
    bool attrTypeGiven = false;
    if (!ResolveElements(typeName, attributeTypeName, typeBuf, attrBuf,
                         &klass, &attrType, &attrTypeGiven))
    {
        // A null element threw ArgumentNullException already (net48). A
        // malformed element or unresolvable attributeType yields no matches.
        return il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, 0);
    }
    if (klass == nullptr || (attrTypeGiven && attrType == nullptr))
    {
        // Unreflected element type / unresolvable attributeType filter ->
        // empty array (net48: an unresolvable attributeType matches nothing).
        return il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, 0);
    }
    const int32_t count = CollectAttributes(klass, attrType, inherit, nullptr, 0);
    Il2CppArray* const result = il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, count);
    Il2CppObject* const scratch = nullptr;
    (void)scratch;
    if (count > 0)
    {
        // CollectAttributes writes most-derived-first; a fixed buffer of
        // exactly count(<= kMaxAttributeTypes) is safe -- CollectAttributes
        // never writes past capacity and the count query matched the same
        // inputs.
        struct { Il2CppObject* slots[kMaxAttributeTypes]; } buf = {};
        CollectAttributes(klass, attrType, inherit, buf.slots, kMaxAttributeTypes);
        for (int32_t i = 0; i < count && i < kMaxAttributeTypes; ++i)
        {
            *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(result, i)) =
                buf.slots[i];
        }
    }
    return result;
}

Il2CppObject* il2cpp_attribute_get_single_ex_named(Il2CppObject* typeName,
                                                   Il2CppObject* attributeTypeName,
                                                   int32_t inherit)
{
    char typeBuf[kNameBufferSize];
    char attrBuf[kNameBufferSize];
    Il2CppClass* klass = nullptr;
    Il2CppClass* attrType = nullptr;
    bool attrTypeGiven = false;
    if (!ResolveElements(typeName, attributeTypeName, typeBuf, attrBuf,
                         &klass, &attrType, &attrTypeGiven))
    {
        return nullptr; // null element threw ArgumentNullException already
    }
    if (klass == nullptr || (attrTypeGiven && attrType == nullptr))
    {
        return nullptr;
    }
    Il2CppObject* scratch[1];
    const int32_t count = CollectAttributes(klass, attrType, inherit, scratch, 1);
    return count > 0 ? scratch[0] : nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_attribute_is_defined_named -- net48 IsDefined existence query on the
// managed-string ABI (refl3 item 1). Mirrors il2cpp_attribute_get_custom_ex_
// named's element/attributeType resolution exactly (ResolveElements ->
// il2cpp_class_by_name) and answers "would the equivalent GetCustomAttributes
// query return at least one match" via CollectAttributes -- the same semantics
// as il2cpp_attribute_is_defined (the raw-primitive form) on the name strings.
//   1 when at least one attribute matches (element chain + attributeType
//   assignability + inherit walk + AttributeUsage rules), 0 otherwise.
//   A null element throws ArgumentNullException (net48 IsDefined element
//   doctrine, same as the _ex_named queries); a malformed / unresolvable
//   element or attributeType yields 0 (no matches -- defensive).
//------------------------------------------------------------------------------
int32_t il2cpp_attribute_is_defined_named(Il2CppObject* typeName,
                                          Il2CppObject* attributeTypeName,
                                          int32_t inherit)
{
    char typeBuf[kNameBufferSize];
    char attrBuf[kNameBufferSize];
    Il2CppClass* klass = nullptr;
    Il2CppClass* attrType = nullptr;
    bool attrTypeGiven = false;
    if (!ResolveElements(typeName, attributeTypeName, typeBuf, attrBuf,
                         &klass, &attrType, &attrTypeGiven))
    {
        return 0; // null element threw ArgumentNullException already
    }
    if (klass == nullptr || (attrTypeGiven && attrType == nullptr))
    {
        return 0;
    }
    return CollectAttributes(klass, attrType, inherit, nullptr, 0) > 0 ? 1 : 0;
}

//------------------------------------------------------------------------------
// RF-N6 -- MEMBER-level attribute registry (method / field / property). A
// separate flat fixed-capacity registry keyed by "ownerReflectionFullName::
// memberName" (a member name is unique within its declaring type). Same
// single-threaded-startup / read-only-afterwards contract as the type-level
// registry (see header). The READ side (il2cpp_attribute_lookup_member) is
// consumed by the RF-N6 query icall in typeinfo.cpp, which scans the typeinfo
// registry to find the declaring type and aggregates the base-chain walk.
//------------------------------------------------------------------------------
namespace {

constexpr int32_t kMaxAttributeMembers = 64;

struct MemberAttributeEntry
{
    const char*    memberKey;   // "FullName::memberName" (static string)
    Il2CppObject** attributes;  // the register-time array (owned by generated code)
    int32_t        count;       // number of attributes
};

MemberAttributeEntry g_memberEntries[kMaxAttributeMembers];
int32_t              g_memberEntryCount = 0;

// Linear key lookup for the member registry; returns the index or -1.
int32_t FindMemberEntry(const char* memberKey)
{
    for (int32_t i = 0; i < g_memberEntryCount; ++i)
    {
        if (std::strcmp(g_memberEntries[i].memberKey, memberKey) == 0)
        {
            return i;
        }
    }
    return -1;
}

} // namespace

int32_t il2cpp_attribute_register_member(const char* memberKey,
                                         Il2CppObject* const* attributes,
                                         int32_t count)
{
    if (memberKey == nullptr || count < 0)
    {
        return 0;
    }
    if (count > 0 && attributes == nullptr)
    {
        return 0;
    }
    if (count == 0)
    {
        return 1; // a member without attributes is an unregistered member
    }
    const int32_t existing = FindMemberEntry(memberKey);
    if (existing >= 0)
    {
        g_memberEntries[existing].attributes = const_cast<Il2CppObject**>(attributes);
        g_memberEntries[existing].count = count;
        return 1;
    }
    if (g_memberEntryCount >= kMaxAttributeMembers)
    {
        return 0; // loud rejection: the transpiler must raise capacity
    }
    g_memberEntries[g_memberEntryCount].memberKey = memberKey;
    g_memberEntries[g_memberEntryCount].attributes = const_cast<Il2CppObject**>(attributes);
    g_memberEntries[g_memberEntryCount].count = count;
    ++g_memberEntryCount;
    return 1;
}

Il2CppObject** il2cpp_attribute_lookup_member(const char* memberKey,
                                              int32_t* outCount)
{
    if (outCount != nullptr)
    {
        *outCount = 0;
    }
    if (memberKey == nullptr || outCount == nullptr)
    {
        return nullptr;
    }
    const int32_t index = FindMemberEntry(memberKey);
    if (index < 0)
    {
        return nullptr;
    }
    *outCount = g_memberEntries[index].count;
    return g_memberEntries[index].attributes;
}

} // namespace runtime
} // namespace il2cpp
