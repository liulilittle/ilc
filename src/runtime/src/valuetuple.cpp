//------------------------------------------------------------------------------
// valuetuple.cpp -- System.ValueTuple`N element formatting / hashing
// (see il2cpp_valuetuple.h for the model and the net48 semantics).
//------------------------------------------------------------------------------
#include "il2cpp_valuetuple.h"

#include "il2cpp_format.h"            // icall_int32/int64/double/single/char/bool_to_string
#include "il2cpp_string_ext2.h"       // icall_string_concat_2
#include "il2cpp_value_semantics.h"   // icall_*_get_hash_code, icall_object_to_string
#include "il2cpp_boxing_ext2.h"       // kBoxPayloadOffset, box helpers
#include "il2cpp_runtime.h"           // il2cpp_object_new
#include "typeinfo.h"                 // il2cpp_typeinfo_lookup + reflected dispatch
#include "arraysort.h"                // il2cpp_icomparable_compare_to (user-struct element order)

#include <cstring>
#include <unordered_map>

namespace il2cpp {
namespace runtime {

namespace {

// The empty managed string, interned once (net48 renders a null tuple element
// as nothing: "(1, )").
Il2CppString* EmptyString()
{
    static Il2CppString* cached = nullptr;
    if (cached == nullptr)
    {
        cached = il2cpp_string_new_from_cs("");
    }
    return cached;
}

const int32_t kTupleBoxPayloadOffset = 8;

// The reflected virtual slot of a method on ONE type (the typeinfo table's
// virtualSlot, verified against the klass's vtable). -1 when the type
// declares no such override.
int32_t ReflectedSlot(const Il2CppClass* klass, const char* name)
{
    if (klass == nullptr || klass->name == nullptr) return -1;
    const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(klass->name);
    if (info == nullptr) return -1;
    for (int32_t i = 0; i < info->methodCount; ++i)
    {
        const Il2CppMethodInfo& m = info->methods[i];
        if (m.name == nullptr || std::strcmp(m.name, name) != 0) continue;
        if (m.paramCount != 0 || m.virtualSlot < 0) continue;
        if (klass->vtable == nullptr || m.virtualSlot >= klass->vtableSize) continue;
        if (klass->vtable[m.virtualSlot] == nullptr) continue;
        return m.virtualSlot;
    }
    return -1;
}

    // The reflected Equals(object) slot (one parameter).
    int32_t ReflectedEqualsSlot(const Il2CppClass* klass)
    {
        if (klass == nullptr || klass->name == nullptr) return -1;
        const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(klass->name);
        if (info == nullptr) return -1;
        for (int32_t i = 0; i < info->methodCount; ++i)
        {
            const Il2CppMethodInfo& m = info->methods[i];
            if (m.name == nullptr || std::strcmp(m.name, "Equals") != 0) continue;
            if (m.paramCount != 1 || m.virtualSlot < 0) continue;
            if (klass->vtable == nullptr || m.virtualSlot >= klass->vtableSize) continue;
            if (klass->vtable[m.virtualSlot] == nullptr) continue;
            return m.virtualSlot;
        }
        return -1;
    }

    //--------------------------------------------------------------------------
    // The CLR ValueType type ID (net48 ValueTypeHelper::GetHashCode):
    //   hashCode = typeID * 711650207 + 2506965631U;
    // The CLR assigns each MethodTable its typeID LAZILY, on the first
    // ValueType.GetHashCode call for that type, from a per-AppDomain counter
    // that starts at 3 and advances by 2 (observed: the first hashed user
    // struct gets 3, the second 5, the third 7 -- IL-verified against net48
    // with single-int-field structs). The runtime reproduces that exact order
    // (the transpiled program hashes its user structs in the same order the
    // oracle does), so the munged hash matches byte-for-byte.
    //--------------------------------------------------------------------------
    uint32_t UserStructTypeId(const Il2CppClass* klass)
    {
        static std::unordered_map<const Il2CppClass*, uint32_t> typeIds;
        static uint32_t nextId = 3;
        if (klass == nullptr) return 0;
        auto it = typeIds.find(klass);
        if (it != typeIds.end()) return it->second;
        const uint32_t id = nextId;
        nextId += 2;
        typeIds.emplace(klass, id);
        return id;
    }

    // The net48 fast path: XOR the instance bytes 32 bits at a time
    // (FastGetValueTypeHashCodeHelper -- GetNumInstanceFieldBytes() is the
    // struct's C++ size for a translated user value type).
    int32_t FastUserStructHash(const Il2CppClass* klass, const void* valueAddr)
    {
        if (klass == nullptr || valueAddr == nullptr) return 0;
        const int32_t size = klass->instanceSize;
        if (size <= 0) return 0;
        const int32_t* words = reinterpret_cast<const int32_t*>(valueAddr);
        int32_t acc = 0;
        for (int32_t i = 0; i < size / static_cast<int32_t>(sizeof(int32_t)); ++i)
            acc ^= words[i];
        return acc;
    }

} // namespace


//------------------------------------------------------------------------------
// il2cpp_tuple_double_bits -- the 64-bit pattern of a double.
//------------------------------------------------------------------------------
int64_t il2cpp_tuple_double_bits(double value)
{
    int64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    return bits;
}

//------------------------------------------------------------------------------
// il2cpp_tuple_element_to_string -- one element's net48 ToString text.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_tuple_element_to_string(char kind, int64_t bits,
                                             Il2CppObject* obj)
{
    switch (kind)
    {
    case 'i':
        return icall_int32_to_string(static_cast<int32_t>(bits));
    case 'l':
        return icall_int64_to_string(bits);
    case 'd':
    case 'f':
    {
        // The float element stores its float value widened to double; net48
        // Single.ToString uses the shortest round-trip float text, so the
        // formatter must narrow back before formatting.
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return kind == 'f'
            ? icall_single_to_string(value)
            : icall_double_to_string(value);
    }
    case 'b':
        return icall_bool_to_string(bits != 0);
    case 'c':
        return icall_char_to_string(static_cast<int32_t>(bits));
    case 'r':
        // net48 ValueTuple.ToString renders a null reference element as the
        // EMPTY string (it appends the element's ToString, and ValueTuple's
        // own builder skips nulls). A non-null element uses its own ToString
        // (string identity, boxed primitives by value).
        return obj == nullptr
            ? EmptyString()
            : icall_object_to_string(obj);
    default:
        return icall_int64_to_string(bits);
    }
}

//------------------------------------------------------------------------------
// il2cpp_tuple_ref_to_string -- net48 renders a null reference element as the
// EMPTY string; a non-null element uses its own ToString.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_tuple_ref_to_string(Il2CppObject* obj)
{
    return obj == nullptr ? EmptyString() : icall_object_to_string(obj);
}

//------------------------------------------------------------------------------
// il2cpp_tuple_combine_hash -- net48 ValueTuple hash step
//   h = unchecked((rol5(h) + h) ^ elementHash)
// (System.Numerics.Hashing.HashHelpers.Combine; IL-verified in mscorlib's
// ValueTuple::CombineHashCodes. The historical "(h * 397) ^ e" formula was a
// different type's -- it produced wrong tuple hashes.)
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_combine_hash(int32_t h, int32_t elementHash)
{
    const uint32_t uh = static_cast<uint32_t>(h);
    const uint32_t rolled = (uh << 5) | (uh >> 27);
    const int32_t sum = static_cast<int32_t>(rolled + uh);
    return sum ^ elementHash;
}

//------------------------------------------------------------------------------
// il2cpp_tuple_ref_combine_hash -- System.Tuple`N (REFERENCE) hash step:
//   h = unchecked(((h << 5) + h) ^ elementHash)
// (System.Tuple.CombineHashCodes -- a plain shift, not the rotate-left-5 the
// ValueTuple uses). The uint32 arithmetic keeps the overflow defined.
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_ref_combine_hash(int32_t h, int32_t elementHash)
{
    const uint32_t uh = static_cast<uint32_t>(h);
    return static_cast<int32_t>((uh << 5) + uh) ^ elementHash;
}

//------------------------------------------------------------------------------
// il2cpp_tuple_element_hash -- one element's net48 hash.
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_element_hash(char kind, int64_t bits, Il2CppObject* obj)
{
    switch (kind)
    {
    case 'i':
        return icall_int32_get_hash_code(static_cast<int32_t>(bits));
    case 'l':
        return icall_int64_get_hash_code(bits);
    case 'd':
    {
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return icall_double_get_hash_code(value);
    }
    case 'f':
    {
        // net48 Single.GetHashCode == the 32-bit float pattern (0 for +-0).
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return il2cpp_tuple_single_hash(value);
    }
    case 'b':
        return icall_bool_get_hash_code(bits != 0);
    case 'c':
        // Char.GetHashCode == the UTF-16 code unit (net48).
        return static_cast<int32_t>(bits);
    case 'r':
        // null hashes to 0 (EqualityComparer<T>.Default.GetHashCode(null) == 0).
        return obj == nullptr ? 0 : icall_object_get_hash_code(obj);
    default:
        return icall_int64_get_hash_code(bits);
    }
}

//------------------------------------------------------------------------------
// il2cpp_tuple_single_hash -- net48 Single.GetHashCode: the 32-bit float
// pattern, with +-0 hashing to 0 (probe: 1.5f -> 1069547520).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_single_hash(double value)
{
    const float narrowed = static_cast<float>(value);
    if (narrowed == 0.0f) return 0;
    int32_t bits = 0;
    std::memcpy(&bits, &narrowed, sizeof(bits));
    return bits;
}

//------------------------------------------------------------------------------
// il2cpp_tuple_ref_hash -- null hashes to 0.
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_ref_hash(Il2CppObject* obj)
{
    return obj == nullptr ? 0 : icall_object_get_hash_code(obj);
}

//------------------------------------------------------------------------------
// il2cpp_tuple_user_struct_to_string -- a translated USER struct element's
// ToString: the type's own reflected override when it has one, else the type
// name (net48 Object.ToString).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_tuple_user_struct_to_string(Il2CppClass* klass, const void* valueAddr)
{
    return il2cpp_user_struct_to_string(klass, const_cast<void*>(valueAddr));
}

//------------------------------------------------------------------------------
// il2cpp_tuple_user_struct_hash -- a translated USER struct element's hash.
// The type's own reflected GetHashCode override when it has one; otherwise the
// net48 CLR ValueType.GetHashCode default:
//   hashCode = typeID * 711650207 + 2506965631U;
//   hashCode ^= FastGetValueTypeHashCodeHelper(...);   // XOR of int32 words
// (see UserStructTypeId / FastUserStructHash above; IL-verified against net48
// -- the struct's own C++ instance size is GetNumInstanceFieldBytes).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_user_struct_hash(Il2CppClass* klass, const void* valueAddr)
{
    if (klass != nullptr && valueAddr != nullptr)
    {
        const int32_t slot = ReflectedSlot(klass, "GetHashCode");
        if (slot >= 0)
        {
            using GetHashCodeFn = int32_t (*)(const void*);
            return reinterpret_cast<GetHashCodeFn>(klass->vtable[slot])(valueAddr);
        }
    }
    // No override: the net48 ValueType.GetHashCode default. A null klass (an
    // untracked element) yields 0, matching the historical fallback.
    if (klass == nullptr) return 0;
    const uint32_t typeID = UserStructTypeId(klass);
    int32_t h = static_cast<int32_t>(typeID * 711650207u + 2506965631u);
    h ^= FastUserStructHash(klass, valueAddr);
    return h;
}

//------------------------------------------------------------------------------
// il2cpp_tuple_user_struct_equals -- a translated USER struct element's value
// equality. The type's own reflected Equals(object) override when it has one
// (compared through the boxed-other dispatch); otherwise the net48 ValueType
// default -- a field-image comparison (memcmp, the CLR's CanCompareBits path
// for reference-free structs).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_user_struct_equals(Il2CppClass* klass, const void* a, const void* b)
{
    if (a == b) return 1;
    if (a == nullptr || b == nullptr) return 0;
    if (klass != nullptr)
    {
        // The struct's own Equals(object) override wins (net48 ValueType
        // dispatch): the override takes the boxed OTHER object, so box b and
        // pass each receiver's value address -- the generated value-type
        // method ABI (the il2cpp_unbox_user_struct contract).
        const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup(klass->name);
        const Il2CppMethodInfo* equalsOverride = nullptr;
        if (info != nullptr)
        {
            for (int32_t i = 0; i < info->methodCount; ++i)
            {
                const Il2CppMethodInfo& m = info->methods[i];
                if (m.name != nullptr && std::strcmp(m.name, "Equals") == 0 &&
                    m.paramCount == 1 && m.signature != nullptr &&
                    std::strstr(m.signature, "(r:System.Object)") != nullptr)
                {
                    equalsOverride = &m;
                    break;
                }
            }
        }
        if (equalsOverride != nullptr && equalsOverride->virtualSlot >= 0 &&
            klass->vtable != nullptr && equalsOverride->virtualSlot < klass->vtableSize &&
            klass->vtable[equalsOverride->virtualSlot] != nullptr)
        {
            using EqualsObjectFn = int32_t (*)(void*, Il2CppObject*);
            Il2CppObject* boxedB = il2cpp_object_new(klass);
            if (boxedB == nullptr) return 0;
            const int32_t size = klass->instanceSize - kTupleBoxPayloadOffset;
            if (size > 0) std::memcpy(reinterpret_cast<char*>(boxedB) + kTupleBoxPayloadOffset, b, static_cast<size_t>(size));
            return reinterpret_cast<EqualsObjectFn>(
                klass->vtable[equalsOverride->virtualSlot])(
                const_cast<void*>(a), boxedB) != 0 ? 1 : 0;
        }
        // No override: the net48 ValueType default -- a field-image comparison
        // (memcmp, the CLR's CanCompareBits path for reference-free structs).
        const int32_t size = klass->instanceSize - kTupleBoxPayloadOffset;
        if (size <= 0) return 1;
        return std::memcmp(a, b, static_cast<size_t>(size)) == 0 ? 1 : 0;
    }
    return std::memcmp(a, b, sizeof(void*)) == 0 ? 1 : 0;
}

//------------------------------------------------------------------------------
// il2cpp_tuple_user_struct_compare -- a translated USER struct element's ORDER
// comparison (the tuple IComparable/IStructuralComparable element-wise
// compare). net48's ValueTuple.CompareTo uses Comparer<object>.Default per
// element, which boxes both operands and dispatches IComparable.CompareTo;
// the runtime mirrors that by boxing both values and delegating to
// il2cpp_icomparable_compare_to (arraysort.cpp).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_user_struct_compare(Il2CppClass* klass, const void* a, const void* b)
{
    if (a == b) return 0;
    if (a == nullptr || b == nullptr || klass == nullptr)
    {
        return a == b ? 0 : (a == nullptr ? -1 : 1);
    }
    Il2CppObject* boxedA = il2cpp_object_new(klass);
    Il2CppObject* boxedB = il2cpp_object_new(klass);
    if (boxedA == nullptr || boxedB == nullptr) return 0;
    const int32_t size = klass->instanceSize - kTupleBoxPayloadOffset;
    if (size > 0)
    {
        std::memcpy(reinterpret_cast<char*>(boxedA) + kTupleBoxPayloadOffset, a, static_cast<size_t>(size));
        std::memcpy(reinterpret_cast<char*>(boxedB) + kTupleBoxPayloadOffset, b, static_cast<size_t>(size));
    }
    return il2cpp_icomparable_compare_to(boxedA, boxedB);
}

} // namespace runtime
} // namespace il2cpp
