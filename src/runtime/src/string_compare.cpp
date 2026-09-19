//==============================================================================
// string_compare.cpp -- Ordinal compare, 8x bulk + early exit.
//==============================================================================
#include "string_compare.h"
#include <cstdint>
#include <cstring>
namespace il2cpp {
static inline char16_t ToLowerAscii(char16_t c) noexcept { return (c>=u'A'&&c<=u'Z') ? (c+32) : c; }

bool il2cpp_string_equals_ordinal(const Il2CppString* a, const Il2CppString* b) noexcept
{
    if (a==b)  return true;
    if (a==nullptr || b==nullptr)  return false;
    if (a->length != b->length)  return false;
    const int32_t len = a->length;
    const char16_t* pa = a->chars;
    const char16_t* pb = b->chars;
    int32_t i=0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i+=8)
    {
        if (pa[i+0]!=pb[i+0]||pa[i+1]!=pb[i+1]||pa[i+2]!=pb[i+2]||pa[i+3]!=pb[i+3]||
            pa[i+4]!=pb[i+4]||pa[i+5]!=pb[i+5]||pa[i+6]!=pb[i+6]||pa[i+7]!=pb[i+7]) 
        {
            if (pa[i+0]!=pb[i+0]) return false;
            if (pa[i+1]!=pb[i+1]) return false;
            if (pa[i+2]!=pb[i+2]) return false;
            if (pa[i+3]!=pb[i+3]) return false;
            if (pa[i+4]!=pb[i+4]) return false;
            if (pa[i+5]!=pb[i+5]) return false;
            if (pa[i+6]!=pb[i+6]) return false;
            return false;
        }
    }
    for (; i < len; ++i) if (pa[i]!=pb[i])  return false;
    return true;
}
bool il2cpp_string_equals_ordinal_ignore_case(const Il2CppString* a, const Il2CppString* b) noexcept
{
    if (a==b)  return true;
    if (a==nullptr || b==nullptr)  return false;
    if (a->length != b->length)  return false;
    const int32_t len = a->length;
    const char16_t* pa = a->chars;
    const char16_t* pb = b->chars;
    int32_t i=0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i+=8)
    {
        char16_t la0=ToLowerAscii(pa[i+0]), lb0=ToLowerAscii(pb[i+0]);
        char16_t la1=ToLowerAscii(pa[i+1]), lb1=ToLowerAscii(pb[i+1]);
        char16_t la2=ToLowerAscii(pa[i+2]), lb2=ToLowerAscii(pb[i+2]);
        char16_t la3=ToLowerAscii(pa[i+3]), lb3=ToLowerAscii(pb[i+3]);
        char16_t la4=ToLowerAscii(pa[i+4]), lb4=ToLowerAscii(pb[i+4]);
        char16_t la5=ToLowerAscii(pa[i+5]), lb5=ToLowerAscii(pb[i+5]);
        char16_t la6=ToLowerAscii(pa[i+6]), lb6=ToLowerAscii(pb[i+6]);
        char16_t la7=ToLowerAscii(pa[i+7]), lb7=ToLowerAscii(pb[i+7]);
        if (la0!=lb0||la1!=lb1||la2!=lb2||la3!=lb3||la4!=lb4||la5!=lb5||la6!=lb6||la7!=lb7) 
        {
            if (la0!=lb0) return false;
            if (la1!=lb1) return false;
            if (la2!=lb2) return false;
            if (la3!=lb3) return false;
            if (la4!=lb4) return false;
            if (la5!=lb5) return false;
            if (la6!=lb6) return false;
            return false;
        }
    }
    for (; i < len; ++i) if (ToLowerAscii(pa[i]) != ToLowerAscii(pb[i]))  return false;
    return true;
}
int il2cpp_string_compare_ordinal(const Il2CppString* a, const Il2CppString* b) noexcept
{
    if (a==b)  return 0;
    if (a==nullptr)  return -1;
    if (b==nullptr)  return 1;
    const int32_t alen=a->length, blen=b->length;
    const int32_t minLen = alen < blen ? alen : blen;
    const char16_t* pa=a->chars; const char16_t* pb=b->chars;
    int32_t i=0;
    const int32_t bulk = minLen & ~7;
    for (; i < bulk; i+=8)
    {
        if (pa[i+0]!=pb[i+0]) return pa[i+0] < pb[i+0] ? -1 : 1;
        if (pa[i+1]!=pb[i+1]) return pa[i+1] < pb[i+1] ? -1 : 1;
        if (pa[i+2]!=pb[i+2]) return pa[i+2] < pb[i+2] ? -1 : 1;
        if (pa[i+3]!=pb[i+3]) return pa[i+3] < pb[i+3] ? -1 : 1;
        if (pa[i+4]!=pb[i+4]) return pa[i+4] < pb[i+4] ? -1 : 1;
        if (pa[i+5]!=pb[i+5]) return pa[i+5] < pb[i+5] ? -1 : 1;
        if (pa[i+6]!=pb[i+6]) return pa[i+6] < pb[i+6] ? -1 : 1;
        if (pa[i+7]!=pb[i+7]) return pa[i+7] < pb[i+7] ? -1 : 1;
    }
    for (; i < minLen; ++i) if (pa[i]!=pb[i]) return pa[i] < pb[i] ? -1 : 1;
    if (alen==blen) return 0;
    return alen < blen ? -1 : 1;
}
int il2cpp_string_compare_ordinal_ignore_case(const Il2CppString* a, const Il2CppString* b) noexcept
{
    if (a==b)  return 0;
    if (a==nullptr)  return -1;
    if (b==nullptr)  return 1;
    const int32_t alen=a->length, blen=b->length;
    const int32_t minLen = alen < blen ? alen : blen;
    const char16_t* pa=a->chars; const char16_t* pb=b->chars;
    int32_t i=0;
    const int32_t bulk = minLen & ~7;
    for (; i < bulk; i+=8)
    {
        char16_t la0=ToLowerAscii(pa[i+0]), lb0=ToLowerAscii(pb[i+0]); if (la0!=lb0) return la0<lb0?-1:1;
        char16_t la1=ToLowerAscii(pa[i+1]), lb1=ToLowerAscii(pb[i+1]); if (la1!=lb1) return la1<lb1?-1:1;
        char16_t la2=ToLowerAscii(pa[i+2]), lb2=ToLowerAscii(pb[i+2]); if (la2!=lb2) return la2<lb2?-1:1;
        char16_t la3=ToLowerAscii(pa[i+3]), lb3=ToLowerAscii(pb[i+3]); if (la3!=lb3) return la3<lb3?-1:1;
        char16_t la4=ToLowerAscii(pa[i+4]), lb4=ToLowerAscii(pb[i+4]); if (la4!=lb4) return la4<lb4?-1:1;
        char16_t la5=ToLowerAscii(pa[i+5]), lb5=ToLowerAscii(pb[i+5]); if (la5!=lb5) return la5<lb5?-1:1;
        char16_t la6=ToLowerAscii(pa[i+6]), lb6=ToLowerAscii(pb[i+6]); if (la6!=lb6) return la6<lb6?-1:1;
        char16_t la7=ToLowerAscii(pa[i+7]), lb7=ToLowerAscii(pb[i+7]); if (la7!=lb7) return la7<lb7?-1:1;
    }
    for (; i < minLen; ++i) { char16_t la=ToLowerAscii(pa[i]), lb=ToLowerAscii(pb[i]); if (la!=lb) return la<lb?-1:1; }
    if (alen==blen) return 0;
    return alen < blen ? -1 : 1;
}
} // namespace il2cpp

