//==============================================================================
// char_bulk.cpp -- Bulk char classification, 8x unrolled, table-free range.
//==============================================================================
#include "char_bulk.h"
#include <cstdint>
namespace il2cpp {
bool il2cpp_char_is_digit_bulk(char16_t c) noexcept { return c>=u'0'&&c<=u'9'; }
bool il2cpp_char_is_letter_bulk(char16_t c) noexcept { return (c>=u'A'&&c<=u'Z')||(c>=u'a'&&c<=u'z'); }
static inline bool IsDigit(char16_t c) noexcept { return c>=u'0'&&c<=u'9'; }
static inline bool IsLetter(char16_t c) noexcept { return (c>=u'A'&&c<=u'Z')||(c>=u'a'&&c<=u'z'); }
static inline bool IsLetterOrDigit(char16_t c) noexcept { return IsDigit(c)||IsLetter(c); }
static inline bool IsWhitespace(char16_t c) noexcept {
    // 25 codepoints, same set as stringext IsWhitespace bitmap
    switch(c){ case 0x0009:case 0x000A:case 0x000B:case 0x000C:case 0x000D:case 0x0020:case 0x0085:case 0x00A0:
    case 0x1680:case 0x2000:case 0x2001:case 0x2002:case 0x2003:case 0x2004:case 0x2005:case 0x2006:case 0x2007:
    case 0x2008:case 0x2009:case 0x200A:case 0x2028:case 0x2029:case 0x202F:case 0x205F:case 0x3000: return true; default: return false; }
}
bool il2cpp_char_all_digit(const Il2CppString* s) noexcept
{
    if(s==nullptr||s->length==0)  return false;
    const int32_t len=s->length; const char16_t* p=s->chars;
    int32_t i=0; const int32_t bulk=len & ~7;
    for(; i < bulk; i+=8)
    {
        char16_t c0=p[i+0],c1=p[i+1],c2=p[i+2],c3=p[i+3],c4=p[i+4],c5=p[i+5],c6=p[i+6],c7=p[i+7];
        if(!IsDigit(c0)||!IsDigit(c1)||!IsDigit(c2)||!IsDigit(c3)||!IsDigit(c4)||!IsDigit(c5)||!IsDigit(c6)||!IsDigit(c7)) 
        {
            if(!IsDigit(c0)) return false;
            if(!IsDigit(c1)) return false;
            if(!IsDigit(c2)) return false;
            if(!IsDigit(c3)) return false;
            if(!IsDigit(c4)) return false;
            if(!IsDigit(c5)) return false;
            if(!IsDigit(c6)) return false;
            return false;
        }
    }
    for(; i < len; ++i) if(!IsDigit(p[i]))  return false;
    return true;
}
bool il2cpp_char_all_letter(const Il2CppString* s) noexcept
{
    if(s==nullptr||s->length==0)  return false;
    const int32_t len=s->length; const char16_t* p=s->chars;
    int32_t i=0; const int32_t bulk=len & ~7;
    for(; i < bulk; i+=8)
    {
        char16_t c0=p[i+0],c1=p[i+1],c2=p[i+2],c3=p[i+3],c4=p[i+4],c5=p[i+5],c6=p[i+6],c7=p[i+7];
        if(!IsLetter(c0)||!IsLetter(c1)||!IsLetter(c2)||!IsLetter(c3)||!IsLetter(c4)||!IsLetter(c5)||!IsLetter(c6)||!IsLetter(c7)) 
        {
            if(!IsLetter(c0)) return false;
            if(!IsLetter(c1)) return false;
            if(!IsLetter(c2)) return false;
            if(!IsLetter(c3)) return false;
            if(!IsLetter(c4)) return false;
            if(!IsLetter(c5)) return false;
            if(!IsLetter(c6)) return false;
            return false;
        }
    }
    for(; i < len; ++i) if(!IsLetter(p[i]))  return false;
    return true;
}
bool il2cpp_char_all_letter_or_digit(const Il2CppString* s) noexcept
{
    if(s==nullptr||s->length==0)  return false;
    const int32_t len=s->length; const char16_t* p=s->chars;
    int32_t i=0; const int32_t bulk=len & ~7;
    for(; i < bulk; i+=8)
    {
        char16_t c0=p[i+0],c1=p[i+1],c2=p[i+2],c3=p[i+3],c4=p[i+4],c5=p[i+5],c6=p[i+6],c7=p[i+7];
        if(!IsLetterOrDigit(c0)||!IsLetterOrDigit(c1)||!IsLetterOrDigit(c2)||!IsLetterOrDigit(c3)||!IsLetterOrDigit(c4)||!IsLetterOrDigit(c5)||!IsLetterOrDigit(c6)||!IsLetterOrDigit(c7)) 
        {
            if(!IsLetterOrDigit(c0)) return false;
            if(!IsLetterOrDigit(c1)) return false;
            if(!IsLetterOrDigit(c2)) return false;
            if(!IsLetterOrDigit(c3)) return false;
            if(!IsLetterOrDigit(c4)) return false;
            if(!IsLetterOrDigit(c5)) return false;
            if(!IsLetterOrDigit(c6)) return false;
            return false;
        }
    }
    for(; i < len; ++i) if(!IsLetterOrDigit(p[i]))  return false;
    return true;
}
int32_t il2cpp_char_count_digit(const Il2CppString* s) noexcept
{
    if(s==nullptr||s->length==0)  return 0;
    const int32_t len=s->length; const char16_t* p=s->chars;
    int32_t n=0; int32_t i=0; const int32_t bulk=len & ~7;
    for(; i < bulk; i+=8){ n+=(IsDigit(p[i+0])?1:0); n+=(IsDigit(p[i+1])?1:0); n+=(IsDigit(p[i+2])?1:0); n+=(IsDigit(p[i+3])?1:0); n+=(IsDigit(p[i+4])?1:0); n+=(IsDigit(p[i+5])?1:0); n+=(IsDigit(p[i+6])?1:0); n+=(IsDigit(p[i+7])?1:0); }
    for(; i < len; ++i) n+=(IsDigit(p[i])?1:0);
    return n;
}
int32_t il2cpp_char_count_letter(const Il2CppString* s) noexcept
{
    if(s==nullptr||s->length==0)  return 0;
    const int32_t len=s->length; const char16_t* p=s->chars;
    int32_t n=0; int32_t i=0; const int32_t bulk=len & ~7;
    for(; i < bulk; i+=8){ n+=(IsLetter(p[i+0])?1:0); n+=(IsLetter(p[i+1])?1:0); n+=(IsLetter(p[i+2])?1:0); n+=(IsLetter(p[i+3])?1:0); n+=(IsLetter(p[i+4])?1:0); n+=(IsLetter(p[i+5])?1:0); n+=(IsLetter(p[i+6])?1:0); n+=(IsLetter(p[i+7])?1:0); }
    for(; i < len; ++i) n+=(IsLetter(p[i])?1:0);
    return n;
}
int32_t il2cpp_char_count_whitespace(const Il2CppString* s) noexcept
{
    if(s==nullptr||s->length==0)  return 0;
    const int32_t len=s->length; const char16_t* p=s->chars;
    int32_t n=0; int32_t i=0; const int32_t bulk=len & ~7;
    for(; i < bulk; i+=8){ n+=(IsWhitespace(p[i+0])?1:0); n+=(IsWhitespace(p[i+1])?1:0); n+=(IsWhitespace(p[i+2])?1:0); n+=(IsWhitespace(p[i+3])?1:0); n+=(IsWhitespace(p[i+4])?1:0); n+=(IsWhitespace(p[i+5])?1:0); n+=(IsWhitespace(p[i+6])?1:0); n+=(IsWhitespace(p[i+7])?1:0); }
    for(; i < len; ++i) n+=(IsWhitespace(p[i])?1:0);
    return n;
}
} // namespace il2cpp

