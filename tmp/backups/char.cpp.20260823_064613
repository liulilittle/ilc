//------------------------------------------------------------------------------
// char.cpp -- System.Char STATIC surface runtime support (see il2cpp_char.h
// for the full contract and the ASCII-domain semantic baseline).
//
// All functions are pure unit-level helpers: no allocation, no receiver, no
// exception path (net48 char statics never throw for a single code unit).
//------------------------------------------------------------------------------

#include "il2cpp_char.h"

namespace il2cpp {
namespace runtime {

bool icall_char_is_digit(char16_t c)
{
    return c >= u'0' && c <= u'9';
}

bool icall_char_is_letter(char16_t c)
{
    return (c >= u'a' && c <= u'z') || (c >= u'A' && c <= u'Z');
}

bool icall_char_is_letter_or_digit(char16_t c)
{
    return icall_char_is_letter(c) || icall_char_is_digit(c);
}

bool icall_char_is_lower(char16_t c)
{
    return c >= u'a' && c <= u'z';
}

bool icall_char_is_upper(char16_t c)
{
    return c >= u'A' && c <= u'Z';
}

// char.IsWhiteSpace: the exact code-unit set the BCL treats as whitespace
// (control whitespace + space + the Unicode Zs/Zl/Zp spaces). Listed
// literally so the runtime never depends on the host C++ locale -- the same
// literal set IsWhitespace uses in stringext.cpp (Trim/Contains).
bool icall_char_is_whitespace(char16_t c)
{
    switch (c)
    {
        case u'\t':                            // HT
        case u'\n':                            // LF
        case u'\v':                            // VT
        case u'\f':                            // FF
        case u'\r':                            // CR
        case u' ':                             // SPACE
        case u'\x0085':                        // NEL (next line)
        case u'\x00A0':                        // NBSP (no-break space)
        case u'\x1680':                        // OGHAM SPACE MARK
        case u'\x2000':                        // EN QUAD .. HAIR SPACE
        case u'\x2001':
        case u'\x2002':
        case u'\x2003':
        case u'\x2004':
        case u'\x2005':
        case u'\x2006':
        case u'\x2007':
        case u'\x2008':
        case u'\x2009':
        case u'\x200A':
        case u'\x2028':                        // LINE SEPARATOR
        case u'\x2029':                        // PARAGRAPH SEPARATOR
        case u'\x202F':                        // NARROW NBSP
        case u'\x205F':                        // MEDIUM MATHEMATICAL SPACE
        case u'\x3000':                        // IDEOGRAPHIC SPACE
            return true;
        default:
            return false;
    }
}

char16_t icall_char_to_upper(char16_t c)
{
    return (c >= u'a' && c <= u'z') ? static_cast<char16_t>(c - 32) : c;
}

char16_t icall_char_to_lower(char16_t c)
{
    return (c >= u'A' && c <= u'Z') ? static_cast<char16_t>(c + 32) : c;
}

}} // namespace il2cpp::runtime
