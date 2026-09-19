//==============================================================================
// buffer_bulk.cpp -- Bulk buffer primitives, 8x unrolled.
//==============================================================================
#include "buffer_bulk.h"
#include <cstdint>
#include <cstring>
namespace il2cpp {
bool il2cpp_buffer_is_zero_bulk(const void* src, size_t bytes) noexcept
{
    if (src == nullptr || bytes == 0) 
    {
        return true;
    }
    const uint8_t* p = static_cast<const uint8_t*>(src);
    size_t i = 0;
    const size_t bulk = bytes & ~size_t(7);
    for (; i < bulk; i += 8)
    {
        const uint8_t c0 = p[i+0], c1=p[i+1], c2=p[i+2], c3=p[i+3], c4=p[i+4], c5=p[i+5], c6=p[i+6], c7=p[i+7];
        if ((c0|c1|c2|c3|c4|c5|c6|c7)!=0) 
        {
            if (c0) return false;
            if (c1) return false;
            if (c2) return false;
            if (c3) return false;
            if (c4) return false;
            if (c5) return false;
            if (c6) return false;
            return false;
        }
    }
    for (; i < bytes; ++i) if (p[i])  return false;
    return true;
}
void il2cpp_buffer_memset_bulk(void* dst, uint8_t value, size_t bytes) noexcept
{
    if (dst == nullptr || bytes == 0)  return;
    uint8_t* d = static_cast<uint8_t*>(dst);
    size_t i=0;
    const size_t bulk = bytes & ~size_t(7);
    // Broadcast value to 8 lanes
    const uint8_t v = value;
    for (; i < bulk; i+=8)
    {
        d[i+0]=v; d[i+1]=v; d[i+2]=v; d[i+3]=v; d[i+4]=v; d[i+5]=v; d[i+6]=v; d[i+7]=v;
    }
    for (; i < bytes; ++i) d[i]=v;
}
void il2cpp_buffer_memcpy_bulk(void* dst, const void* src, size_t bytes) noexcept
{
    if (dst==nullptr || src==nullptr || bytes==0)  return;
    // Use memmove semantics for overlapping (caller may assume copy)
    std::memmove(dst, src, bytes);
}
int il2cpp_buffer_memcmp_bulk(const void* a, const void* b, size_t bytes) noexcept
{
    if (a==b)  return 0;
    if (a==nullptr || b==nullptr)  return (a==nullptr)?-1:1;
    if (bytes==0)  return 0;
    const uint8_t* pa = static_cast<const uint8_t*>(a);
    const uint8_t* pb = static_cast<const uint8_t*>(b);
    size_t i=0;
    const size_t bulk = bytes & ~size_t(7);
    for (; i < bulk; i+=8)
    {
        if (pa[i+0]!=pb[i+0]) return (pa[i+0] < pb[i+0]) ? -1 : 1;
        if (pa[i+1]!=pb[i+1]) return (pa[i+1] < pb[i+1]) ? -1 : 1;
        if (pa[i+2]!=pb[i+2]) return (pa[i+2] < pb[i+2]) ? -1 : 1;
        if (pa[i+3]!=pb[i+3]) return (pa[i+3] < pb[i+3]) ? -1 : 1;
        if (pa[i+4]!=pb[i+4]) return (pa[i+4] < pb[i+4]) ? -1 : 1;
        if (pa[i+5]!=pb[i+5]) return (pa[i+5] < pb[i+5]) ? -1 : 1;
        if (pa[i+6]!=pb[i+6]) return (pa[i+6] < pb[i+6]) ? -1 : 1;
        if (pa[i+7]!=pb[i+7]) return (pa[i+7] < pb[i+7]) ? -1 : 1;
    }
    for (; i < bytes; ++i) if (pa[i]!=pb[i]) return (pa[i] < pb[i]) ? -1 : 1;
    return 0;
}
bool il2cpp_buffer_try_copy_checked(void* dst, size_t dstSize, const void* src, size_t srcSize, size_t bytes) noexcept
{
    if (dst==nullptr || src==nullptr)  return false;
    if (bytes==0)  return true;
    if (bytes > dstSize || bytes > srcSize)  return false;
    // Overflow-safe: sizes are size_t, caller ensures bytes <= sizes, no wrap.
    std::memmove(dst, src, bytes);
    return true;
}
} // namespace il2cpp

