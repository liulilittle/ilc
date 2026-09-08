//==============================================================================
// string_encode.cpp -- UTF-8/UTF-16 bulk helpers, 8x where applicable.
//==============================================================================
#include "string_encode.h"
#include <cstdint>
#include <cstring>
namespace il2cpp {
bool il2cpp_utf8_is_ascii(const char* utf8, size_t len) noexcept
{
    if (utf8==nullptr || len==0)  return true;
    const uint8_t* p = reinterpret_cast<const uint8_t*>(utf8);
    size_t i=0;
    const size_t bulk = len & ~size_t(7);
    for (; i < bulk; i+=8)
    {
        const uint8_t c0=p[i+0],c1=p[i+1],c2=p[i+2],c3=p[i+3],c4=p[i+4],c5=p[i+5],c6=p[i+6],c7=p[i+7];
        if ((c0|c1|c2|c3|c4|c5|c6|c7) & 0x80u) 
        {
            if (c0 & 0x80) return false; if (c1 & 0x80) return false; if (c2 & 0x80) return false; if (c3 & 0x80) return false;
            if (c4 & 0x80) return false; if (c5 & 0x80) return false; if (c6 & 0x80) return false; return false;
        }
    }
    for (; i < len; ++i) if (p[i] & 0x80)  return false;
    return true;
}
bool il2cpp_utf16_is_ascii_raw(const char16_t* chars, int32_t len) noexcept
{
    if (chars==nullptr || len<=0)  return true;
    int32_t i=0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i+=8)
    {
        const char16_t c0=chars[i+0],c1=chars[i+1],c2=chars[i+2],c3=chars[i+3],c4=chars[i+4],c5=chars[i+5],c6=chars[i+6],c7=chars[i+7];
        if ((c0|c1|c2|c3|c4|c5|c6|c7) >= 0x80) 
        {
            if (c0>=0x80) return false; if (c1>=0x80) return false; if (c2>=0x80) return false; if (c3>=0x80) return false;
            if (c4>=0x80) return false; if (c5>=0x80) return false; if (c6>=0x80) return false; return false;
        }
    }
    for (; i < len; ++i) if (chars[i] >= 0x80)  return false;
    return true;
}
size_t il2cpp_utf8_to_utf16_length(const char* utf8, size_t len) noexcept
{
    if (utf8==nullptr || len==0)  return 0;
    const uint8_t* p = reinterpret_cast<const uint8_t*>(utf8);
    size_t units=0;
    size_t i=0;
    // Fast ASCII bulk: each byte == one unit
    const size_t bulk = len & ~size_t(7);
    for (; i < bulk; )
    {
        const uint8_t c0=p[i+0],c1=p[i+1],c2=p[i+2],c3=p[i+3],c4=p[i+4],c5=p[i+5],c6=p[i+6],c7=p[i+7];
        if ((c0|c1|c2|c3|c4|c5|c6|c7) < 0x80)  { units+=8; i+=8; continue; }
        break;
    }
    for (; i < len; )
    {
        uint8_t c = p[i];
        if (c < 0x80) { units+=1; ++i; }
        else if ((c & 0xE0)==0xC0) { // 2 bytes -> 1 unit
            if (i+1 >= len) { units+=1; ++i; continue; }
            units+=1; i+=2;
        } else if ((c & 0xF0)==0xE0) { // 3 bytes -> 1 unit
            if (i+2 >= len) { units+=1; i+=1; continue; }
            units+=1; i+=3;
        } else if ((c & 0xF8)==0xF0) { // 4 bytes -> 2 units (surrogate pair)
            if (i+3 >= len) { units+=1; i+=1; continue; }
            units+=2; i+=4;
        } else { // invalid -> replacement 1 unit
            units+=1; ++i;
        }
    }
    return units;
}
size_t il2cpp_utf16_to_utf8_length(const char16_t* chars, int32_t len) noexcept
{
    if (chars==nullptr || len<=0)  return 0;
    size_t bytes=0;
    int32_t i=0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i+=8)
    {
        // Fast ASCII 8x: each <0x80 => 1 byte
        const char16_t c0=chars[i+0],c1=chars[i+1],c2=chars[i+2],c3=chars[i+3],c4=chars[i+4],c5=chars[i+5],c6=chars[i+6],c7=chars[i+7];
        if ((c0|c1|c2|c3|c4|c5|c6|c7) < 0x80)  { bytes+=8; continue; }
        // Mixed: fall back to scalar for this block
        for (int k=0;k<8;++k)
        {
            char16_t c = chars[i+k];
            if (c < 0x80) bytes+=1;
            else if (c < 0x800) bytes+=2;
            else if (c >= 0xD800 && c <= 0xDBFF) { // high surrogate, expect pair
                if (i+k+1 < len && chars[i+k+1] >= 0xDC00 && chars[i+k+1] <= 0xDFFF) { bytes+=4; ++k; } else bytes+=3;
            } else bytes+=3;
        }
        continue;
    }
    for (; i < len; ++i)
    {
        char16_t c = chars[i];
        if (c < 0x80) bytes+=1;
        else if (c < 0x800) bytes+=2;
        else if (c >= 0xD800 && c <= 0xDBFF) {
            if (i+1 < len && chars[i+1] >= 0xDC00 && chars[i+1] <= 0xDFFF) { bytes+=4; ++i; } else bytes+=3;
        } else bytes+=3;
    }
    return bytes;
}
int32_t il2cpp_utf8_decode_one(const char* s, size_t avail, char32_t* out) noexcept
{
    if (s==nullptr || out==nullptr || avail==0)  return 0;
    const uint8_t* p = reinterpret_cast<const uint8_t*>(s);
    uint8_t c0 = p[0];
    if (c0 < 0x80) { *out = c0; return 1; }
    if ((c0 & 0xE0)==0xC0) {
        if (avail < 2) return 0;
        uint8_t c1 = p[1];
        if ((c1 & 0xC0)!=0x80) return 0;
        *out = ((char32_t)(c0 & 0x1F) << 6) | (c1 & 0x3F);
        if (*out < 0x80) return 0; // overlong
        return 2;
    }
    if ((c0 & 0xF0)==0xE0) {
        if (avail < 3) return 0;
        uint8_t c1=p[1], c2=p[2];
        if ((c1 & 0xC0)!=0x80 || (c2 & 0xC0)!=0x80) return 0;
        *out = ((char32_t)(c0 & 0x0F) << 12) | ((char32_t)(c1 & 0x3F) << 6) | (c2 & 0x3F);
        if (*out < 0x800) return 0;
        if (*out >= 0xD800 && *out <= 0xDFFF) return 0;
        return 3;
    }
    if ((c0 & 0xF8)==0xF0) {
        if (avail < 4) return 0;
        uint8_t c1=p[1], c2=p[2], c3=p[3];
        if ((c1 & 0xC0)!=0x80 || (c2 & 0xC0)!=0x80 || (c3 & 0xC0)!=0x80) return 0;
        *out = ((char32_t)(c0 & 0x07) << 18) | ((char32_t)(c1 & 0x3F) << 12) | ((char32_t)(c2 & 0x3F) << 6) | (c3 & 0x3F);
        if (*out < 0x10000 || *out > 0x10FFFF) return 0;
        return 4;
    }
    return 0;
}
int il2cpp_utf8_validate(const char* utf8, size_t len) noexcept
{
    if (utf8==nullptr)  return 0;
    if (len==0)  return 1;
    const uint8_t* p = reinterpret_cast<const uint8_t*>(utf8);
    size_t i=0;
    while (i < len)
    {
        uint8_t c = p[i];
        if (c < 0x80) { ++i; continue; }
        size_t need=0; char32_t cp=0;
        if ((c & 0xE0)==0xC0) need=2;
        else if ((c & 0xF0)==0xE0) need=3;
        else if ((c & 0xF8)==0xF0) need=4;
        else return 0;
        if (i + need > len) return 0;
        int32_t consumed = il2cpp_utf8_decode_one(reinterpret_cast<const char*>(p+i), need, &cp);
        if (consumed != (int32_t)need) return 0;
        i += need;
    }
    return 1;
}
} // namespace il2cpp

