//==============================================================================
// hash_bulk.cpp -- Bulk FNV-1a hash, 8x unrolled where beneficial.
//==============================================================================
#include "hash_bulk.h"
#include <cstdint>
#include <cstddef>
namespace il2cpp {
static inline uint32_t Fnv1aStep(uint32_t h, uint32_t c) noexcept { return (h ^ c) * 16777619u; }
static inline uint32_t ToLowerAsciiU32(uint32_t c) noexcept { return (c>=65 && c<=90) ? (c+32) : c; }

uint32_t il2cpp_string_hash_ordinal(const Il2CppString* s) noexcept
{
    if(s==nullptr || s->length==0)  return 2166136261u;
    uint32_t h = 2166136261u;
    const char16_t* p = s->chars;
    int32_t len = s->length;
    int32_t i=0;
    const int32_t bulk = len & ~7;
    for(; i < bulk; i+=8){
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+0]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+1]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+2]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+3]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+4]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+5]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+6]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+7]));
    }
    for(; i < len; ++i) h = Fnv1aStep(h, static_cast<uint32_t>(p[i]));
    return h;
}
uint32_t il2cpp_string_hash_ordinal_ignore_case(const Il2CppString* s) noexcept
{
    if(s==nullptr || s->length==0)  return 2166136261u;
    uint32_t h = 2166136261u;
    const char16_t* p = s->chars;
    int32_t len = s->length;
    int32_t i=0;
    const int32_t bulk = len & ~7;
    for(; i < bulk; i+=8){
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+0]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+1]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+2]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+3]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+4]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+5]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+6]));
        h = Fnv1aStep(h, ToLowerAsciiU32(p[i+7]));
    }
    for(; i < len; ++i) h = Fnv1aStep(h, ToLowerAsciiU32(p[i]));
    return h;
}
uint32_t il2cpp_buffer_hash_fnv1a(const void* data, size_t bytes) noexcept
{
    if(data==nullptr || bytes==0)  return 2166136261u;
    uint32_t h = 2166136261u;
    const uint8_t* p = static_cast<const uint8_t*>(data);
    size_t i=0;
    const size_t bulk = bytes & ~size_t(7);
    for(; i < bulk; i+=8){
        h = Fnv1aStep(h, p[i+0]); h = Fnv1aStep(h, p[i+1]); h = Fnv1aStep(h, p[i+2]); h = Fnv1aStep(h, p[i+3]);
        h = Fnv1aStep(h, p[i+4]); h = Fnv1aStep(h, p[i+5]); h = Fnv1aStep(h, p[i+6]); h = Fnv1aStep(h, p[i+7]);
    }
    for(; i < bytes; ++i) h = Fnv1aStep(h, p[i]);
    return h;
}
uint32_t il2cpp_array_hash_i4_bulk(const void* data, size_t count) noexcept
{
    if(data==nullptr || count==0)  return 2166136261u;
    uint32_t h = 2166136261u;
    const int32_t* p = static_cast<const int32_t*>(data);
    size_t i=0;
    const size_t bulk = count & ~size_t(7);
    for(; i < bulk; i+=8){
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+0])); h = Fnv1aStep(h, static_cast<uint32_t>(p[i+1]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+2])); h = Fnv1aStep(h, static_cast<uint32_t>(p[i+3]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+4])); h = Fnv1aStep(h, static_cast<uint32_t>(p[i+5]));
        h = Fnv1aStep(h, static_cast<uint32_t>(p[i+6])); h = Fnv1aStep(h, static_cast<uint32_t>(p[i+7]));
    }
    for(; i < count; ++i) h = Fnv1aStep(h, static_cast<uint32_t>(p[i]));
    return h;
}
} // namespace il2cpp

