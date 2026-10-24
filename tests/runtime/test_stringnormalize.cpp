//------------------------------------------------------------------------------
// test_stringnormalize.cpp -- System.String.Normalize / IsNormalized unit tests
// (il2cpp_string_normalize.h: icall_string_normalize /
// icall_string_is_normalized).
//
// The runtime implements UAX #15 normalization over the complete Unicode 12.0.0
// tables (src/runtime/src/stringnormalize.cpp + the generated
// il2cpp_unicode_norm_data.cpp). Every expected value below was captured from
// the net48 oracle (System.Text.NormalizationForm: FormC=1, FormD=2, FormKC=5,
// FormKD=6) on 2026-10-24 and is pinned here as a constant.
//
// Coverage:
//   1. ASCII identity (empty / "abc").
//   2. Canonical decomposition + composition (U+00E9, U+00C5, U+1E09, U+1EA7).
//   3. Canonical composition EXCLUSIONS (U+0958 script-specific, U+1E9B).
//   4. Compatibility decomposition (U+0F77, U+FB01, U+212A, U+212B, U+00B2,
//      U+2460, U+00A0, U+FF21, U+FDFA, U+0149).
//   5. Canonical ordering of combining classes (U+0344 -> 0308 0301,
//      "a\u0300\u0327" -> 00E0 0327, U+1F80 U+0345).
//   6. Hangul algorithmic decomposition/composition (U+AC00, U+1100 U+1161 U+11A8).
//   7. Astral pass-through (U+1F600).
//   8. IsNormalized true/false across the forms.
//   9. Error paths: invalid form -> ArgumentException; null receiver ->
//      NullReferenceException; noncharacter / lone surrogate ->
//      ArgumentException (net48 rejects them).
//
// Plain executable + minimal assertion harness (see test_stringhash.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_string_normalize.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

il2cpp::Il2CppString* Str(const char16_t* units, int32_t n)
{
    return il2cpp::il2cpp_string_new(units, n);
}

// Parse a hex-codepoint string ("00061 00062", empty allowed) into UTF-16 units.
std::vector<char16_t> ParseHexUnits(const char* hex)
{
    std::vector<char16_t> out;
    const char* p = hex;
    while (*p != '\0')
    {
        while (*p == ' ') { ++p; }
        if (*p == '\0') { break; }
        uint32_t cp = 0;
        int digits = 0;
        while (digits < 5)
        {
            const char c = *p;
            int v;
            if (c >= '0' && c <= '9') { v = c - '0'; }
            else if (c >= 'A' && c <= 'F') { v = c - 'A' + 10; }
            else { break; }
            cp = (cp << 4) | static_cast<uint32_t>(v);
            ++p;
            ++digits;
        }
        if (cp >= 0x10000u)
        {
            cp -= 0x10000u;
            out.push_back(static_cast<char16_t>(0xD800u + (cp >> 10)));
            out.push_back(static_cast<char16_t>(0xDC00u + (cp & 0x3FFu)));
        }
        else
        {
            out.push_back(static_cast<char16_t>(cp));
        }
    }
    return out;
}

// Render a managed string's UTF-16 units as hex codepoints.
std::string HexUnits(il2cpp::Il2CppObject* s)
{
    const il2cpp::Il2CppString* str = reinterpret_cast<const il2cpp::Il2CppString*>(s);
    std::string out;
    for (int32_t i = 0; i < str->length; ++i)
    {
        uint32_t cp = static_cast<uint32_t>(str->chars[i]);
        if (cp >= 0xD800u && cp <= 0xDBFFu && i + 1 < str->length)
        {
            const uint32_t lo = static_cast<uint32_t>(str->chars[i + 1]);
            if (lo >= 0xDC00u && lo <= 0xDFFFu)
            {
                cp = 0x10000u + ((cp - 0xD800u) << 10) + (lo - 0xDC00u);
                ++i;
            }
        }
        char buf[12];
        std::snprintf(buf, sizeof(buf), "%X", cp);
        if (!out.empty()) { out += " "; }
        out += buf;
    }
    return out;
}

struct Case
{
    const char* label;
    const char16_t* input;
    int32_t inputLen;
    const char* c;
    const char* d;
    const char* kc;
    const char* kd;
    bool isc;
    bool isd;
    bool iskc;
    bool iskd;
};

const Case kCases[] = {
    { "empty", u"", 0,
      "", "", "", "", true, true, true, true },
    { "ascii", u"abc", 3,
      "61 62 63", "61 62 63", "61 62 63", "61 62 63", true, true, true, true },
    { "e-acute-precomposed", u"\u00E9", 1,
      "E9", "65 301", "E9", "65 301", true, false, true, false },
    { "e-acute-decomposed", u"e\u0301", 2,
      "E9", "65 301", "E9", "65 301", false, true, false, true },
    { "aring-precomposed", u"\u00C5", 1,
      "C5", "41 30A", "C5", "41 30A", true, false, true, false },
    { "aring-decomposed", u"A\u030A", 2,
      "C5", "41 30A", "C5", "41 30A", false, true, false, true },
    { "deva-qa-excluded", u"\u0958", 1,
      "915 93C", "915 93C", "915 93C", "915 93C", false, false, false, false },
    { "long-s-dot-excluded", u"\u1E9B", 1,
      "1E9B", "17F 307", "1E61", "73 307", true, false, false, false },
    { "tibetan-compat", u"\u0F77", 1,
      "F77", "F77", "FB2 F71 F80", "FB2 F71 F80", true, true, false, false },
    { "fi-ligature", u"\uFB01", 1,
      "FB01", "FB01", "66 69", "66 69", true, true, false, false },
    { "angstrom", u"\u212B", 1,
      "C5", "41 30A", "C5", "41 30A", false, false, false, false },
    { "kelvin", u"\u212A", 1,
      "4B", "4B", "4B", "4B", false, false, false, false },
    { "combining-diacritic", u"\u0344", 1,
      "308 301", "308 301", "308 301", "308 301", false, false, false, false },
    { "hangul-syllable", u"\uAC00", 1,
      "AC00", "1100 1161", "AC00", "1100 1161", true, false, true, false },
    { "hangul-jamo", u"\u1100\u1161\u11A8", 3,
      "AC01", "1100 1161 11A8", "AC01", "1100 1161 11A8", false, true, false, true },
    { "a-grave-cedilla", u"a\u0300\u0327", 3,
      "E0 327", "61 327 300", "E0 327", "61 327 300", false, false, false, false },
    { "combining-order", u"\u031B\u0323\u0300", 3,
      "31B 323 300", "31B 323 300", "31B 323 300", "31B 323 300", true, true, true, true },
    { "greek-iota-subscript", u"\u1F80\u0345", 2,
      "1F80 345", "3B1 313 345 345", "1F80 345", "3B1 313 345 345", true, false, true, false },
    { "c-cedilla-acute", u"\u1E09", 1,
      "1E09", "63 327 301", "1E09", "63 327 301", true, false, true, false },
    { "I-dot", u"\u0130", 1,
      "130", "49 307", "130", "49 307", true, false, true, false },
    { "sharp-s", u"\u00DF", 1,
      "DF", "DF", "DF", "DF", true, true, true, true },
    { "n-apostrophe", u"\u0149", 1,
      "149", "149", "2BC 6E", "2BC 6E", true, true, false, false },
    { "a-breve-grave", u"\u1EA7", 1,
      "1EA7", "61 302 300", "1EA7", "61 302 300", true, false, true, false },
    { "superscript-two", u"\u00B2", 1,
      "B2", "B2", "32", "32", true, true, false, false },
    { "circled-one", u"\u2460", 1,
      "2460", "2460", "31", "31", true, true, false, false },
    { "nbsp", u"\u00A0", 1,
      "A0", "A0", "20", "20", true, true, false, false },
    { "fullwidth-a", u"\uFF21", 1,
      "FF21", "FF21", "41", "41", true, true, false, false },
    { "arabic-ligature", u"\uFDFA", 1,
      "FDFA", "FDFA",
      "635 644 649 20 627 644 644 647 20 639 644 64A 647 20 648 633 644 645",
      "635 644 649 20 627 644 644 647 20 639 644 64A 647 20 648 633 644 645",
      true, true, false, false },
    { "cgj", u"\u034F", 1,
      "34F", "34F", "34F", "34F", true, true, true, true },
    { "emoji-astral", u"\U0001F600", 2,
      "1F600", "1F600", "1F600", "1F600", true, true, true, true },
    { "capital-sharp-s", u"\u1E9E", 1,
      "1E9E", "1E9E", "1E9E", "1E9E", true, true, true, true },
};

void TestCases()
{
    const int32_t forms[4] = { 1, 2, 5, 6 }; // FormC, FormD, FormKC, FormKD
    const char* names[4] = { "FormC", "FormD", "FormKC", "FormKD" };
    for (const Case& c : kCases)
    {
        il2cpp::Il2CppString* s = Str(c.input, c.inputLen);
        const char* expected[4] = { c.c, c.d, c.kc, c.kd };
        const bool expectedIs[4] = { c.isc, c.isd, c.iskc, c.iskd };
        for (int32_t f = 0; f < 4; ++f)
        {
            il2cpp::Il2CppObject* n = il2cpp::runtime::icall_string_normalize(
                reinterpret_cast<il2cpp::Il2CppObject*>(s), forms[f]);
            const std::string got = HexUnits(n);
            if (got != expected[f])
            {
                std::printf("FAIL: %s %s expected [%s] got [%s]\n", c.label, names[f], expected[f], got.c_str());
                ++g_failures;
            }
            const bool is = il2cpp::runtime::icall_string_is_normalized(
                reinterpret_cast<il2cpp::Il2CppObject*>(s), forms[f]);
            if (is != expectedIs[f])
            {
                std::printf("FAIL: %s IsNormalized(%s) expected %s got %s\n",
                            c.label, names[f], expectedIs[f] ? "T" : "F", is ? "T" : "F");
                ++g_failures;
            }
        }
    }
}

void TestErrors()
{
    // Invalid form -> ArgumentException.
    il2cpp::Il2CppString* s = Str(u"abc", 3);
    try
    {
        il2cpp::runtime::icall_string_normalize(reinterpret_cast<il2cpp::Il2CppObject*>(s), 99);
        Expect(false, "invalid form must throw ArgumentException");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(std::strstr(e.type_name(), "System.ArgumentException") != nullptr,
               "invalid form throws ArgumentException");
    }

    // Null receiver -> NullReferenceException (checked before the form).
    try
    {
        il2cpp::runtime::icall_string_normalize(nullptr, 99);
        Expect(false, "null receiver must throw NullReferenceException");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(std::strstr(e.type_name(), "System.NullReferenceException") != nullptr,
               "null receiver throws NullReferenceException");
    }

    // Noncharacter -> ArgumentException.
    il2cpp::Il2CppString* nc = Str(u"\uFDD0", 1);
    try
    {
        il2cpp::runtime::icall_string_normalize(reinterpret_cast<il2cpp::Il2CppObject*>(nc), 1);
        Expect(false, "noncharacter must throw ArgumentException");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(std::strstr(e.type_name(), "System.ArgumentException") != nullptr,
               "noncharacter throws ArgumentException");
    }

    // Lone surrogate -> ArgumentException.
    il2cpp::Il2CppString* lone = Str(u"\xD800", 1);
    try
    {
        il2cpp::runtime::icall_string_normalize(reinterpret_cast<il2cpp::Il2CppObject*>(lone), 1);
        Expect(false, "lone surrogate must throw ArgumentException");
    }
    catch (const il2cpp::DotNetException& e)
    {
        Expect(std::strstr(e.type_name(), "System.ArgumentException") != nullptr,
               "lone surrogate throws ArgumentException");
    }
}

} // namespace

int main()
{
    TestCases();
    TestErrors();
    if (g_failures != 0)
    {
        std::printf("%d stringnormalize assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("test_stringnormalize: all assertions passed\n");
    return 0;
}
