//------------------------------------------------------------------------------
// valuetuple.cpp -- System.ValueTuple`N element formatting / hashing
// (see il2cpp_valuetuple.h for the model and the net48 semantics).
//------------------------------------------------------------------------------
#include "il2cpp_valuetuple.h"

#include "il2cpp_format.h"            // icall_int32/int64/double/char/bool_to_string
#include "il2cpp_string_ext2.h"       // icall_string_concat_2
#include "il2cpp_value_semantics.h"   // icall_*_get_hash_code, icall_object_to_string

#include <cstring>

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
    {
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return icall_double_to_string(value);
    }
    case 'f':
    {
        // float-in-double slot (the transpiler's "f" element kind stores the
        // float promoted to double -- the same rule the P5 tuple path uses).
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return icall_double_to_string(value);
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
// il2cpp_tuple_combine_hash -- h = (h * 397) ^ elementHash (unchecked int32).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_combine_hash(int32_t h, int32_t elementHash)
{
    const uint32_t product =
        static_cast<uint32_t>(static_cast<uint32_t>(h) * 397u);
    return static_cast<int32_t>(product ^ static_cast<uint32_t>(elementHash));
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
    case 'f':
    {
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return icall_double_get_hash_code(value);
    }
    case 'b':
        return icall_bool_get_hash_code(bits != 0);
    case 'c':
        // Char.GetHashCode == the UTF-16 code unit (net48).
        return static_cast<int32_t>(bits);
    case 'r':
        if (obj == nullptr) return 0;
        // A string element hashes by ordinal content; any other reference by
        // its object hash (net48 EqualityComparer<T>.Default on a reference
        // type delegates to the instance's GetHashCode).
        return icall_object_get_hash_code(obj);
    default:
        return icall_int64_get_hash_code(bits);
    }
}

} // namespace runtime
} // namespace il2cpp
