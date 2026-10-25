//------------------------------------------------------------------------------
// reflecthandle.cpp -- the reflection HANDLE value-type icall family
// (see reflecthandle.h for the contract and the coverage notes -- this TU
// adds ONLY the minimal handle surface; GetTypeFromHandle/type identity/
// typeinfo member queries already live in typeext.cpp / typecheck.cpp /
// typeinfo.cpp and are DELEGATED to, never duplicated).
//
// Handle value model (header section 1): RuntimeTypeHandle = Il2CppClass* descriptor
// address; RuntimeMethodHandle / RuntimeFieldHandle = the typeinfo table
// entry addresses. Every function is defensive by convention: null inputs
// return 0 / nullptr ("default handle" semantics -- net48 probed that all of
// default(RuntimeTypeHandle).Value/Equals/GetHashCode and the STATIC
// Type.GetTypeFromHandle(default) succeed on the default handle, never throw;
// GetRuntimeType is a .NET Core-shaped convenience mapping onto the SAME
// GetTypeFromHandle primitive -- see the function comment).
//
// Hash convention: deterministic name-based hashing (djb2, seed 5381, step
// *33+c) -- the same convention the value-semantics type hash uses
// (valuesemantics.cpp:419 "djb2 over the full type name"). Multi-component
// identities (method: declaringType+name+signature; field: name+typeName)
// fold through the SAME accumulator so the hash is one continuous djb2 over
// the identity components -- equal identities ALWAYS hash equal (the
// "equal keys -> equal hashes" invariant the handle types participate in).
//------------------------------------------------------------------------------

#include "reflecthandle.h"

#include "il2cpp_typecheck.h" // icall_type_equals (delegated type identity)
#include "il2cpp_typeext.h"   // il2cpp_type_get_type_from_handle (delegated)

#include <cstdint>
#include <cstring> // std::strcmp (method/field identity comparison)

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Djb2Fold -- deterministic name hash (djb2: seed 5381, step (h*33) ^ c).
//
//   acc    -- running accumulator (0 to start fresh -> seeded to 5381)
//   s      -- the component string (null / empty folds nothing -- a null
//             identity field contributes zero bytes to the fold)
//
// Multi-component identities hash by folding each component through the SAME
// accumulator (a continuous djb2 over "dt" + "name" + "signature"), so
// equality of ALL components implies equality of the fold. Unsigned
// arithmetic keeps the wraps well-defined (no signed-overflow UB).
//------------------------------------------------------------------------------
int32_t Djb2Fold(int32_t acc, const char* s)
{
    if (s == nullptr)
    {
        return acc;
    }
    uint32_t h = (acc == 0) ? 5381u : static_cast<uint32_t>(acc);
    for (const char* p = s; *p != '\0'; ++p)
    {
        h = (h * 33u) ^ static_cast<uint32_t>(static_cast<uint8_t>(*p));
    }
    return static_cast<int32_t>(h);
}

} // namespace

//------------------------------------------------------------------------------
// RuntimeTypeHandle surface (handle = Il2CppClass* descriptor)
//------------------------------------------------------------------------------

intptr_t il2cpp_rthandle_get_value(Il2CppClass* handle)
{
    return reinterpret_cast<intptr_t>(handle); // null -> 0
}

intptr_t il2cpp_rthandle_get_value_intptr(intptr_t handle)
{
    return handle; // the handle VALUE is the intptr_t carrier (null -> 0)
}

int32_t il2cpp_rthandle_equals(Il2CppClass* a, Il2CppClass* b)
{
    // Delegated: descriptor identity fast path (incl. both-null -> 1) then
    // full-name equality -- EXACTLY the net48 type-handle Equals semantics
    // (probed: default==default true, default vs any real handle false).
    return il2cpp::runtime::icall_type_equals(a, b);
}

int32_t il2cpp_rthandle_get_hash_code(Il2CppClass* handle)
{
    if (handle == nullptr)
    {
        return 0; // net48: default.GetHashCode() == 0 (probed)
    }
    // Name-based (NOT address): equal handles (same name, possibly distinct
    // descriptors -- the typecheck name-equality rule) MUST hash equal.
    return Djb2Fold(0, handle->name);
}

Il2CppObject* il2cpp_rthandle_get_runtime_type(Il2CppClass* handle)
{
    if (handle == nullptr)
    {
        // Null handle -> nullptr: mirrors the net48 STATIC
        // Type.GetTypeFromHandle(RuntimeTypeHandle) default-handle contract
        // (probe-verified against v4.0.30319: default handle -> null, never a
        // throw). GetRuntimeType() itself is a .NET Core-shaped convenience --
        // net48 mscorlib has NO such member -- but it maps onto the SAME
        // primitive, so the net48 GetTypeFromHandle contract is the anchor.
        return nullptr;
    }
    // Delegated: the existing GetTypeFromHandle primitive already builds the
    // full-name string (typeext.cpp) -- the handle->Type direction is NOT
    // re-implemented here. NOTE the guard ordering: the null-handle path
    // returns BEFORE reaching the delegate (typeext's null-descriptor path
    // throws ArgumentNullException -- its own GetTypeFromHandle contract).
    return il2cpp::runtime::il2cpp_type_get_type_from_handle(handle);
}

//------------------------------------------------------------------------------
// RuntimeMethodHandle surface (handle = const Il2CppMethodInfo* entry)
//------------------------------------------------------------------------------

intptr_t il2cpp_rmethodhandle_get_value(const Il2CppMethodInfo* handle)
{
    return reinterpret_cast<intptr_t>(handle); // null -> 0
}

intptr_t il2cpp_rmethodhandle_get_value_intptr(intptr_t handle)
{
    return handle; // the handle VALUE is the intptr_t carrier (null -> 0)
}

int32_t il2cpp_rmethodhandle_equals(const Il2CppMethodInfo* a, const Il2CppMethodInfo* b)
{
    if (a == b)
    {
        return 1; // identity fast path (incl. both-null -> 1)
    }
    if (a == nullptr || b == nullptr)
    {
        return 0; // default vs any real handle -> false (net48 probed)
    }
    // Full method identity: declaringType + name + signature. Overloads of
    // the same name differ by signature -> stay DISTINCT (net48 probe against
    // v4.0.30319: string.IndexOf(char) vs IndexOf(string) handles are NOT
    // equal; two same-identity entries from different declaring types are
    // NOT equal either -- the declaring-type dimension is a real identity
    // component).
    if (a->declaringType == nullptr || b->declaringType == nullptr ||
        a->name == nullptr || b->name == nullptr ||
        a->signature == nullptr || b->signature == nullptr)
    {
        return 0;
    }
    if (std::strcmp(a->declaringType, b->declaringType) != 0)
    {
        return 0;
    }
    if (std::strcmp(a->name, b->name) != 0)
    {
        return 0;
    }
    return std::strcmp(a->signature, b->signature) == 0 ? 1 : 0;
}

int32_t il2cpp_rmethodhandle_get_hash_code(const Il2CppMethodInfo* handle)
{
    if (handle == nullptr)
    {
        return 0;
    }
    // One continuous djb2 fold over the identity components -- equal
    // (declaringType, name, signature) triples ALWAYS hash equal.
    int32_t h = Djb2Fold(0, handle->declaringType);
    h = Djb2Fold(h, handle->name);
    return Djb2Fold(h, handle->signature);
}

//------------------------------------------------------------------------------
// RuntimeFieldHandle surface (handle = const Il2CppFieldInfo* entry)
//------------------------------------------------------------------------------

intptr_t il2cpp_rfieldhandle_get_value(const Il2CppFieldInfo* handle)
{
    return reinterpret_cast<intptr_t>(handle); // null -> 0
}

intptr_t il2cpp_rfieldhandle_get_value_intptr(intptr_t handle)
{
    return handle; // the handle VALUE is the intptr_t carrier (null -> 0)
}

int32_t il2cpp_rfieldhandle_equals(const Il2CppFieldInfo* a, const Il2CppFieldInfo* b)
{
    // Field identity = name + fieldTypeName. DIVERGENCE from net48 field
    // identity, documented: net48 field handles compare (declaringType,
    // fieldName) -- probe-verified against v4.0.30319 that
    // typeof(string).GetField("Empty") vs typeof(Guid).GetField("Empty")
    // are NOT equal (same name, different declaring types). Il2CppFieldInfo
    // carries NO declaring-type field (typeinfo.h; the transpiler emits
    // name + typeName only -- TypeEmitter.cs), so that dimension is not
    // recoverable from the entry: same-named same-typed fields from DIFFERENT
    // declaring types compare EQUAL here vs DIFFERENT in net48 (residual
    // divergence, pinned by the test). Within a type the identity is exact
    // (fields are name-unique per table).
    if (a == b)
    {
        return 1; // identity fast path (incl. both-null -> 1)
    }
    if (a == nullptr || b == nullptr)
    {
        return 0; // default vs any real handle -> false (net48 probed)
    }
    if (a->name == nullptr || b->name == nullptr ||
        a->typeName == nullptr || b->typeName == nullptr)
    {
        return 0;
    }
    if (std::strcmp(a->name, b->name) != 0)
    {
        return 0;
    }
    return std::strcmp(a->typeName, b->typeName) == 0 ? 1 : 0;
}

int32_t il2cpp_rfieldhandle_get_hash_code(const Il2CppFieldInfo* handle)
{
    if (handle == nullptr)
    {
        return 0;
    }
    // One continuous djb2 fold over the identity components (name + typeName)
    // -- equal field identities ALWAYS hash equal.
    int32_t h = Djb2Fold(0, handle->name);
    return Djb2Fold(h, handle->typeName);
}

} // namespace il2cpp
