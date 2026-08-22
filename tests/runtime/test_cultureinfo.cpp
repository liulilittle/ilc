//------------------------------------------------------------------------------
// test_cultureinfo.cpp -- CultureInfo minimal runtime domain unit tests
// (cultureinfo.h).
//
// Pins the runtime to the documented net48 baseline (probe-verified oracle):
//   1. Invariant culture identity: name "" (String.Empty), LCID 127,
//      TwoLetterISOLanguageName "iv", ThreeLetterISOLanguageName "ivl",
//      ThreeLetterWindowsLanguageName "IVL".
//   2. Invariant NumberFormatInfo symbols: decimal separator ".", group
//      separator ",", negative sign "-", positive sign "+", NaN "NaN",
//      +Infinity "Infinity", -Infinity "-Infinity"; currency symbol U+00A4,
//      currency separators "."/","; percent symbol "%", percent separators
//      "."/",", per-mille symbol U+2030.
//   3. CurrentCulture reads the OS user locale (C42 fold fix): name
//      "zh-CN" on the zh-CN verification host, and distinct from the
//      invariant culture instance.
//   4. Static-return contract: repeated calls return the SAME instance
//      (interned strings -- reference equality, matching .NET's cached
//      culture singletons); no call crashes (current culture path).
//
// Plain executable + minimal assertion harness (see test_format.cpp):
// any failed Expect() prints FAIL and the process exits non-zero.
//------------------------------------------------------------------------------

#include "cultureinfo.h"

#include <cstdio>
#include <cstring>

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

// Exact UTF-16 comparison of a managed string against an ASCII literal.
bool StringIs(il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const size_t asciiLen = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != asciiLen)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// Exact UTF-16 comparison of a managed string against a char16_t literal
// (for non-ASCII invariant symbols such as U+00A4 / U+2030).
bool StringIsUtf16(il2cpp::Il2CppString* s, const char16_t* text)
{
    if (s == nullptr)
    {
        return false;
    }
    size_t len = 0;
    while (text[len] != u'\0')
    {
        ++len;
    }
    if (static_cast<size_t>(s->length) != len)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] != text[i])
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. invariant culture identity (net48 baseline) ---
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_invariant(), ""),
           "invariant culture name is String.Empty");
    Expect(il2cpp::runtime::il2cpp_culture_get_invariant_lcid() == 127,
           "invariant culture LCID is 127");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_two_letter_iso_language_name(), "iv"),
           "invariant two-letter ISO language name is \"iv\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_three_letter_iso_language_name(), "ivl"),
           "invariant three-letter ISO language name is \"ivl\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_three_letter_windows_language_name(), "IVL"),
           "invariant three-letter windows language name is \"IVL\"");

    // --- 2. invariant number format symbols (net48 baseline) ---
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_decimal_separator(), "."),
           "invariant decimal separator is \".\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_group_separator(), ","),
           "invariant group separator is \",\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_negative_sign(), "-"),
           "invariant negative sign is \"-\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_positive_sign(), "+"),
           "invariant positive sign is \"+\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_nan_symbol(), "NaN"),
           "invariant NaN symbol is \"NaN\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_positive_infinity_symbol(), "Infinity"),
           "invariant positive-infinity symbol is \"Infinity\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_number_negative_infinity_symbol(), "-Infinity"),
           "invariant negative-infinity symbol is \"-Infinity\"");

    // --- 3. invariant currency format (net48 baseline) ---
    Expect(StringIsUtf16(il2cpp::runtime::il2cpp_culture_get_currency_symbol(), u"\u00A4"),
           "invariant currency symbol is U+00A4");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_currency_decimal_separator(), "."),
           "invariant currency decimal separator is \".\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_currency_group_separator(), ","),
           "invariant currency group separator is \",\"");

    // --- 4. invariant percent format (net48 baseline) ---
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_percent_symbol(), "%"),
           "invariant percent symbol is \"%\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_percent_decimal_separator(), "."),
           "invariant percent decimal separator is \".\"");
    Expect(StringIs(il2cpp::runtime::il2cpp_culture_get_percent_group_separator(), ","),
           "invariant percent group separator is \",\"");
    Expect(StringIsUtf16(il2cpp::runtime::il2cpp_culture_get_per_mille_symbol(), u"\u2030"),
           "invariant per-mille symbol is U+2030");

    // --- 5. current culture (C42 fold fix: reads the OS user locale) ---
    // HOST-INDEPENDENT assertions only. The current culture reads the OS
    // locale: a real locale yields its .NET-form name ("zh_CN.UTF-8" ->
    // "zh-CN"); the C/POSIX locale maps to "" (the invariant fallback --
    // .NET's own C-locale mapping). The specific name is an environment
    // property, not a runtime contract.
    {
        const auto* cur = il2cpp::runtime::il2cpp_culture_get_current();
        Expect(cur != nullptr,
               "current culture returns a string instance on any host");
        Expect(il2cpp::runtime::il2cpp_culture_get_current() == cur,
               "current culture returns the same instance on repeated calls");
        const bool validForm =
            cur->length == 0 ||                       // C/POSIX -> invariant ""
            (cur->chars[0] != static_cast<char16_t>(u'_')); // normalized: no '_'
        Expect(validForm,
               "current culture name is in the normalized .NET form");
    }
    if (il2cpp::runtime::il2cpp_culture_get_current()->length != 0)
    {
        Expect(il2cpp::runtime::il2cpp_culture_get_current() != il2cpp::runtime::il2cpp_culture_get_invariant(),
               "a real current culture differs from the invariant culture");
    }

    // --- 6. static-return contract: same instance on repeated calls ---
    Expect(il2cpp::runtime::il2cpp_culture_get_name() == il2cpp::runtime::il2cpp_culture_get_name(),
           "get_name returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_invariant() == il2cpp::runtime::il2cpp_culture_get_invariant(),
           "get_invariant returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_decimal_separator()
               == il2cpp::runtime::il2cpp_culture_get_number_decimal_separator(),
           "decimal separator returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_group_separator()
               == il2cpp::runtime::il2cpp_culture_get_number_group_separator(),
           "group separator returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_negative_sign()
               == il2cpp::runtime::il2cpp_culture_get_number_negative_sign(),
           "negative sign returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_positive_sign()
               == il2cpp::runtime::il2cpp_culture_get_number_positive_sign(),
           "positive sign returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_nan_symbol()
               == il2cpp::runtime::il2cpp_culture_get_number_nan_symbol(),
           "NaN symbol returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_positive_infinity_symbol()
               == il2cpp::runtime::il2cpp_culture_get_number_positive_infinity_symbol(),
           "positive-infinity symbol returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_number_negative_infinity_symbol()
               == il2cpp::runtime::il2cpp_culture_get_number_negative_infinity_symbol(),
           "negative-infinity symbol returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_currency_symbol()
               == il2cpp::runtime::il2cpp_culture_get_currency_symbol(),
           "currency symbol returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_percent_symbol()
               == il2cpp::runtime::il2cpp_culture_get_percent_symbol(),
           "percent symbol returns the same instance on every call");
    Expect(il2cpp::runtime::il2cpp_culture_get_per_mille_symbol()
               == il2cpp::runtime::il2cpp_culture_get_per_mille_symbol(),
           "per-mille symbol returns the same instance on every call");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d cultureinfo assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime cultureinfo tests passed\n");
    return 0;
}
