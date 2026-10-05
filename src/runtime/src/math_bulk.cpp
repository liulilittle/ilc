//==============================================================================
// math_bulk.cpp -- Bulk math primitives, branchless where possible.
//==============================================================================
#include "math_bulk.h"
#include <cstdint>
#include <cstddef>
namespace il2cpp {
bool il2cpp_math_is_pow2_u32(uint32_t v) noexcept { return v!=0 && (v & (v-1))==0; }
bool il2cpp_math_is_pow2_u64(uint64_t v) noexcept { return v!=0 && (v & (v-1))==0; }
int il2cpp_math_ctz_u32(uint32_t v) noexcept {
    if(v==0)  return 32;
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_ctz(v);
#else
    int n=0; while((v & 1)==0){ ++n; v>>=1; } return n;
#endif
}
int il2cpp_math_ctz_u64(uint64_t v) noexcept {
    if(v==0)  return 64;
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_ctzll(v);
#else
    int n=0; while((v & 1)==0){ ++n; v>>=1; } return n;
#endif
}
int il2cpp_math_clz_u32(uint32_t v) noexcept {
    if(v==0)  return 32;
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_clz(v);
#else
    int n=0; uint32_t m=0x80000000u; while(m && (v & m)==0){ ++n; m>>=1; } return n;
#endif
}
int il2cpp_math_clz_u64(uint64_t v) noexcept {
    if(v==0)  return 64;
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_clzll(v);
#else
    int n=0; uint64_t m=0x8000000000000000ull; while(m && (v & m)==0){ ++n; m>>=1; } return n;
#endif
}
int il2cpp_math_popcount_u32(uint32_t v) noexcept {
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_popcount(v);
#else
    int c=0; while(v){ c+=v&1; v>>=1; } return c;
#endif
}
int il2cpp_math_popcount_u64(uint64_t v) noexcept {
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_popcountll(v);
#else
    int c=0; while(v){ c+=v&1; v>>=1; } return c;
#endif
}
uint32_t il2cpp_math_round_up_pow2_u32(uint32_t v) noexcept {
    if(v==0)  return 1;
    if(il2cpp_math_is_pow2_u32(v))  return v;
    v--; v|=v>>1; v|=v>>2; v|=v>>4; v|=v>>8; v|=v>>16; return v+1;
}
uint64_t il2cpp_math_round_up_pow2_u64(uint64_t v) noexcept {
    if(v==0)  return 1;
    if(il2cpp_math_is_pow2_u64(v))  return v;
    v--; v|=v>>1; v|=v>>2; v|=v>>4; v|=v>>8; v|=v>>16; v|=v>>32; return v+1;
}
size_t il2cpp_math_count_pow2_bulk_u32(const uint32_t* data, size_t count) noexcept {
    if(data==nullptr||count==0)  return 0;
    size_t n=0;
    size_t i=0;
    const size_t bulk = count & ~size_t(7);
    for(; i < bulk; i+=8){
        n += il2cpp_math_is_pow2_u32(data[i+0])?1:0; n += il2cpp_math_is_pow2_u32(data[i+1])?1:0;
        n += il2cpp_math_is_pow2_u32(data[i+2])?1:0; n += il2cpp_math_is_pow2_u32(data[i+3])?1:0;
        n += il2cpp_math_is_pow2_u32(data[i+4])?1:0; n += il2cpp_math_is_pow2_u32(data[i+5])?1:0;
        n += il2cpp_math_is_pow2_u32(data[i+6])?1:0; n += il2cpp_math_is_pow2_u32(data[i+7])?1:0;
    }
    for(; i < count; ++i) n += il2cpp_math_is_pow2_u32(data[i])?1:0;
    return n;
}
} // namespace il2cpp

