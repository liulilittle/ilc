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
#include "il2cpp_gc.h"                // il2cpp_gc_alloc_size (tuple key box)
#include "il2cpp_metadata.h"          // il2cpp_class_register (tuple box descriptor)
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
    // The user-struct default hash (typeID munge + field image) lives in
    // valuesemantics.cpp as icall_user_struct_default_hash, because the PLAIN
    // `struct.GetHashCode()` path must run the SAME function from the SAME lazy
    // typeID counter. net48 assigns one typeID per type, in first-hash order,
    // whichever path touches the type first -- a private counter here (this
    // file's original shape) made the two paths hand out different ids.
    //--------------------------------------------------------------------------

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
// net48 CLR ValueType.GetHashCode default, which is EXACTLY the same function
// the plain `struct.GetHashCode()` path runs (one lazy typeID counter, one
// field-image rule -- see icall_user_struct_default_hash). Routing both paths
// through one implementation is what keeps net48's typeID ASSIGNMENT ORDER:
// net48 hands the ids out in FIRST-HASH order, whichever path touches the type
// first, so a separate counter here would desynchronize the two.
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
    if (klass == nullptr) return 0;
    return icall_user_struct_default_hash(valueAddr,
                                          klass->vtFields != nullptr ? klass->vtFields : nullptr,
                                          klass);
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

//------------------------------------------------------------------------------
// Value-type tuple KEY carrier (see il2cpp_valuetuple.h). A boxed tuple is a
// GC object whose payload at kTupleBoxPayloadOffset holds the struct bytes;
// its klass name "System.ValueTuple`<kinds>" carries the element kinds so the
// key path can read the payload structurally. The descriptor registry is a
// small static table (one entry per distinct kind string); the runtime is
// single-threaded on this path.
//------------------------------------------------------------------------------
namespace {

constexpr int32_t kMaxTupleBoxClasses = 16;
constexpr int32_t kTupleBoxNameMax = 48;

struct TupleBoxClassSlot
{
    char name[kTupleBoxNameMax];
    Il2CppClass klass;
};

TupleBoxClassSlot g_tupleBoxSlots[kMaxTupleBoxClasses];
int32_t g_tupleBoxSlotCount = 0;

// The element kinds encoded in a tuple-box klass name, or null when the name
// is not a tuple-box descriptor ("System.ValueTuple`<kinds>").
const char* TupleBoxKinds(const Il2CppObject* box)
{
    if (box == nullptr || box->klass == nullptr || box->klass->name == nullptr)
    {
        return nullptr;
    }
    const char* name = box->klass->name;
    if (std::strncmp(name, "System.ValueTuple`", 18) != 0)
    {
        return nullptr;
    }
    const char* kinds = name + 18;
    return kinds[0] != '\0' ? kinds : nullptr;
}

// One element's offset + kind char in the generated C++ struct layout.
struct TupleElemView
{
    int32_t offset;
    char kind;
};

// The (size, alignment) of one element kind. Returns false for an unsupported
// kind (nested tuple / user struct / BCL value struct -- the caller falls back
// to identity semantics).
bool TupleElemShape(char kind, int32_t* size, int32_t* align)
{
    switch (kind)
    {
    case 'i': case 'b': case 'c':
        *size = 4; *align = 4; return true;
    case 'l': case 'd': case 'f':
        *size = 8; *align = 8; return true;
    case 'r':
        *size = static_cast<int32_t>(sizeof(void*));
        *align = static_cast<int32_t>(sizeof(void*));
        return true;
    default:
        return false;
    }
}

// Compute the total struct size from the kinds, mirroring the transpiler's C++
// layout: fields in order, each aligned to its natural alignment, the whole
// struct rounded up to the largest member alignment. Returns false on an
// unsupported kind.
bool TupleElemTotalSize(const char* kinds, int32_t* totalSize)
{
    int32_t offset = 0;
    int32_t maxAlign = 1;
    for (int32_t i = 0; kinds[i] != '\0'; ++i)
    {
        int32_t size = 0;
        int32_t align = 1;
        if (!TupleElemShape(kinds[i], &size, &align))
        {
            return false;
        }
        offset = (offset + align - 1) / align * align + size;
        if (align > maxAlign) maxAlign = align;
    }
    *totalSize = (offset + maxAlign - 1) / maxAlign * maxAlign;
    return true;
}

// The offset of element 'index' (the layout authority is the same loop as
// TupleElemTotalSize; recomputed per call so the hash/equals paths need no
// stored array).
int32_t TupleElemOffset(const char* kinds, int32_t index)
{
    int32_t offset = 0;
    for (int32_t i = 0; i < index; ++i)
    {
        int32_t size = 0;
        int32_t align = 1;
        TupleElemShape(kinds[i], &size, &align);
        offset = (offset + align - 1) / align * align + size;
    }
    int32_t size = 0;
    int32_t align = 1;
    TupleElemShape(kinds[index], &size, &align);
    return (offset + align - 1) / align * align;
}

// Read one element's raw bits (and the managed reference for 'r').
int64_t ReadElemBits(const char* payload, const TupleElemView& view, Il2CppObject** obj)
{
    const char* p = payload + view.offset;
    *obj = nullptr;
    switch (view.kind)
    {
    case 'i': case 'b': case 'c':
        return *reinterpret_cast<const int32_t*>(p);
    case 'l':
        return *reinterpret_cast<const int64_t*>(p);
    case 'd': case 'f':
    {
        double value = *reinterpret_cast<const double*>(p);
        int64_t bits = 0;
        std::memcpy(&bits, &value, sizeof(bits));
        return bits;
    }
    case 'r':
        *obj = *reinterpret_cast<Il2CppObject* const*>(p);
        return 0;
    default:
        return 0;
    }
}

// The per-kind descriptor for a tuple box (lazily created + registered).
Il2CppClass* TupleBoxClassFor(const char* kinds)
{
    for (int32_t i = 0; i < g_tupleBoxSlotCount; ++i)
    {
        if (std::strcmp(g_tupleBoxSlots[i].name + 18, kinds) == 0)
        {
            return &g_tupleBoxSlots[i].klass;
        }
    }
    if (g_tupleBoxSlotCount >= kMaxTupleBoxClasses)
    {
        return nullptr;
    }
    TupleBoxClassSlot& slot = g_tupleBoxSlots[g_tupleBoxSlotCount++];
    std::memset(&slot, 0, sizeof(slot));
    std::snprintf(slot.name, kTupleBoxNameMax, "System.ValueTuple`%s", kinds);
    slot.klass.name = slot.name;
    slot.klass.instanceSize = 8; // header only; il2cpp_gc_alloc_size extends
    slot.klass.refFieldBitmap = 0;
    slot.klass.refFieldCount = 0;
    // A value-type box: the base chain reaches System.ValueType so the shared
    // user-struct unbox helper (il2cpp_unbox_user_struct) accepts the box and
    // the listobj struct forwarders can reuse it for List<ValueTuple<..>>.
    slot.klass.baseClass = &System_ValueType_class;
    slot.klass.vtable = nullptr;
    slot.klass.vtableSize = 0;
    slot.klass.interfaces = nullptr;
    slot.klass.interfaceCount = 0;
    il2cpp_class_register(&slot.klass);
    return &slot.klass;
}

} // namespace

Il2CppObject* il2cpp_tuple_box(const void* value, const char* kinds)
{
    if (value == nullptr || kinds == nullptr || kinds[0] == '\0')
    {
        return nullptr;
    }
    int32_t size = il2cpp_tuple_box_size(kinds);
    if (size < 0)
    {
        return nullptr;
    }
    Il2CppClass* klass = TupleBoxClassFor(kinds);
    if (klass == nullptr)
    {
        return nullptr;
    }
    Il2CppObject* box = il2cpp_gc_alloc_size(klass, static_cast<size_t>(kTupleBoxPayloadOffset + size));
    if (box == nullptr)
    {
        return nullptr;
    }
    std::memcpy(reinterpret_cast<char*>(box) + kTupleBoxPayloadOffset, value, static_cast<size_t>(size));
    return box;
}

int32_t il2cpp_tuple_box_size(const char* kinds)
{
    if (kinds == nullptr)
    {
        return -1;
    }
    int32_t size = 0;
    return TupleElemTotalSize(kinds, &size) ? size : -1;
}

Il2CppClass* il2cpp_tuple_box_class(const char* kinds)
{
    if (kinds == nullptr || kinds[0] == '\0')
    {
        return nullptr;
    }
    int32_t size = 0;
    if (!TupleElemTotalSize(kinds, &size))
    {
        return nullptr;
    }
    return TupleBoxClassFor(kinds);
}

int32_t il2cpp_tuple_box_hash(const Il2CppObject* carrier)
{
    const char* kinds = TupleBoxKinds(carrier);
    if (kinds == nullptr)
    {
        // Not a tuple box: the identity hash fallback (never a silent
        // wrong-bucket, equality still decides).
        return static_cast<int32_t>(reinterpret_cast<uintptr_t>(carrier) >> 3) & 0x7FFFFFFF;
    }
    const char* payload = reinterpret_cast<const char*>(carrier) + kTupleBoxPayloadOffset;
    int32_t h = 0;
    bool first = true;
    for (int32_t i = 0; kinds[i] != '\0'; ++i)
    {
        int32_t size = 0;
        int32_t align = 1;
        if (!TupleElemShape(kinds[i], &size, &align))
        {
            return static_cast<int32_t>(reinterpret_cast<uintptr_t>(carrier) >> 3) & 0x7FFFFFFF;
        }
        TupleElemView view { TupleElemOffset(kinds, i), kinds[i] };
        Il2CppObject* obj = nullptr;
        const int64_t bits = ReadElemBits(payload, view, &obj);
        const int32_t eh = il2cpp_tuple_element_hash(kinds[i], bits, obj);
        h = first ? eh : il2cpp_tuple_combine_hash(h, eh);
        first = false;
    }
    return h & 0x7FFFFFFF;
}

bool il2cpp_tuple_box_equals(const Il2CppObject* a, const Il2CppObject* b)
{
    const char* kinds = TupleBoxKinds(a);
    if (kinds == nullptr || TupleBoxKinds(b) == nullptr)
    {
        return a == b;
    }
    const char* pa = reinterpret_cast<const char*>(a) + kTupleBoxPayloadOffset;
    const char* pb = reinterpret_cast<const char*>(b) + kTupleBoxPayloadOffset;
    for (int32_t i = 0; kinds[i] != '\0'; ++i)
    {
        int32_t size = 0;
        int32_t align = 1;
        if (!TupleElemShape(kinds[i], &size, &align))
        {
            return a == b;
        }
        TupleElemView view { TupleElemOffset(kinds, i), kinds[i] };
        Il2CppObject* oa = nullptr;
        Il2CppObject* ob = nullptr;
        const int64_t ba = ReadElemBits(pa, view, &oa);
        const int64_t bb = ReadElemBits(pb, view, &ob);
        if (kinds[i] == 'r')
        {
            if (!icall_object_equals(oa, ob))
            {
                return false;
            }
        }
        else if (ba != bb)
        {
            return false;
        }
    }
    return true;
}

} // namespace runtime
} // namespace il2cpp
