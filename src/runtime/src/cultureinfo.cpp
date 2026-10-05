//------------------------------------------------------------------------------
// cultureinfo.cpp -- CultureInfo minimal runtime domain (cultureinfo.h).
//
// Implementation notes:
//   * There is no CultureInfo object model; every query returns the SAME
//     interned managed string instance on every call
//     (il2cpp_string_intern_literal -- content lookup, miss -> create +
//     insert; the intern pool holds strong GC roots so the statics can never
//     be collected, and reference equality between repeated calls holds,
//     matching .NET's cached CultureInfo singletons).
//   * C42 FOLD FIX (2026-08-11): CurrentCulture now reads the OS user
//     locale (GetUserDefaultLCID -- net48 CultureInfo.CurrentCulture
//     initializes from the process/user locale) instead of being fixed to
//     the invariant culture. The LCID is mapped to the culture NAME via the
//     table in CultureNameFromLcid; unknown LCIDs fall back to "" (the
//     invariant name -- determinism preserved, the previous documented
//     fallback). On non-Windows hosts there is no GetUserDefaultLCID; the
//     probe is compiled out and the fallback "" is returned (the zh-CN
//     verification host is Windows -- the dual-compiler harness runs
//     mingw/msvc). get_current() therefore returns "zh-CN" on this host
//     while get_invariant()/get_name() stay "".
//   * Invariant culture values (net48 oracle, probe-verified 2026-08-08):
//     name "", LCID 127, 2-letter "iv", 3-letter "ivl", 3-letter windows
//     "IVL"; number format decimal separator ".", group separator ",",
//     negative sign "-", positive sign "+", NaN "NaN", +Infinity "Infinity",
//     -Infinity "-Infinity"; currency symbol U+00A4, currency separators
//     "."/","; percent symbol "%", percent separators "."/",", per-mille
//     U+2030. The zh-CN verification host agrees on the three separators
//     ("."/","/"-") but diverges on identity ("" vs "zh-CN") and currency
//     (U+00A4 vs U+00A5 '¥') -- see the header.
//------------------------------------------------------------------------------

#include "cultureinfo.h"
#include "il2cpp_string_intern.h" // il2cpp_string_intern_literal (static returns)

#include <clocale>
#include <cstring>

#if defined(_WIN32)
#include <windows.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

// Interned static managed string: same instance on every call, GC-rooted by
// the intern pool (see the header/implementation notes).
Il2CppString* Intern(const char* utf8)
{
    // Il2CppString embeds Il2CppObject BY VALUE (not inheritance) -> the
    // C-style/reinterpret cast is the project convention (see stringintern
    // and the intern tests for the same pattern).
    return reinterpret_cast<Il2CppString*>(il2cpp_string_intern_literal(utf8));
}

#if defined(_WIN32)
// LCID -> culture name (the culture NAME net48's CultureInfo.CurrentCulture
// exposes -- GetUserDefaultLCID -> CultureInfo.Name). The table covers the
// locales the e2e host and the common verification configurations name;
// an UNMAPPED LCID returns "" (the invariant culture name -- the fallback
// the module documented before the C42 wiring, so determinism is preserved
// on exotic locales).
const char* CultureNameFromLcid(uint32_t lcid)
{
    switch (lcid)
    {
        case 0x0804: return "zh-CN";  // Chinese (Simplified, PRC) -- the e2e host
        case 0x0404: return "zh-TW";  // Chinese (Traditional, Taiwan)
        case 0x0C04: return "zh-HK";  // Chinese (Traditional, Hong Kong)
        case 0x1004: return "zh-SG";  // Chinese (Simplified, Singapore)
        case 0x1404: return "zh-MO";  // Chinese (Traditional, Macao)
        case 0x0409: return "en-US";  // English (United States)
        case 0x0411: return "ja-JP";  // Japanese
        case 0x0412: return "ko-KR";  // Korean
        case 0x0407: return "de-DE";  // German (Germany)
        case 0x040C: return "fr-FR";  // French (France)
        case 0x0410: return "it-IT";  // Italian (Italy)
        case 0x040A: return "es-ES";  // Spanish (Spain)
        case 0x0416: return "pt-BR";  // Portuguese (Brazil)
        case 0x0419: return "ru-RU";  // Russian
        default:     return "";
    }
}
#endif

} // namespace

// --- Culture identity (invariant) ---

Il2CppString* il2cpp_culture_get_name()
{
    return Intern("");
}

Il2CppString* il2cpp_culture_get_invariant()
{
    return Intern("");
}

Il2CppString* il2cpp_culture_get_current()
{
#if defined(_WIN32)
    // net48 CurrentCulture follows the OS user default locale (GetUserDefaultLCID);
    // the LCID -> name table maps it to "zh-CN" on the verification host.
    // GetSystemDefaultLCID would return the same 0x0804 on this host.
    return Intern(CultureNameFromLcid(GetUserDefaultLCID()));
#else
    // POSIX: read the current C locale (setlocale(LC_ALL, NULL) -- a pure
    // query, never mutates state) and map it to the .NET culture-name form:
    // "zh_CN.UTF-8" -> "zh-CN" (strip the '.' codeset and '@' modifier,
    // '_' -> '-'), "C"/"POSIX" -> "" (the invariant fallback -- .NET maps
    // the C locale to the invariant culture, matching the Windows
    // unmapped-LCID fallback semantics; cross-platform consistency).
    const char* loc = std::setlocale(LC_ALL, nullptr);
    if (loc == nullptr)
    {
        return Intern("");
    }
    char name[32];
    size_t n = 0;
    for (const char* p = loc; *p != 0 && n + 1 < sizeof(name); ++p)
    {
        if (*p == '.' || *p == '@')
        {
            break;
        }
        name[n++] = (*p == '_') ? '-' : *p;
    }
    name[n] = 0;
    if (n == 0 || std::strcmp(name, "C") == 0 || std::strcmp(name, "POSIX") == 0)
    {
        return Intern(""); // the C/POSIX locale == the invariant fallback
    }
    return Intern(name);
#endif
}

int32_t il2cpp_culture_get_invariant_lcid()
{
    return 127;
}

Il2CppString* il2cpp_culture_get_two_letter_iso_language_name()
{
    return Intern("iv");
}

Il2CppString* il2cpp_culture_get_three_letter_iso_language_name()
{
    return Intern("ivl");
}

Il2CppString* il2cpp_culture_get_three_letter_windows_language_name()
{
    return Intern("IVL");
}

// --- Number format (invariant NumberFormatInfo) ---

Il2CppString* il2cpp_culture_get_number_decimal_separator()
{
    return Intern(".");
}

Il2CppString* il2cpp_culture_get_number_group_separator()
{
    return Intern(",");
}

Il2CppString* il2cpp_culture_get_number_negative_sign()
{
    return Intern("-");
}

Il2CppString* il2cpp_culture_get_number_positive_sign()
{
    return Intern("+");
}

Il2CppString* il2cpp_culture_get_number_nan_symbol()
{
    return Intern("NaN");
}

Il2CppString* il2cpp_culture_get_number_positive_infinity_symbol()
{
    return Intern("Infinity");
}

Il2CppString* il2cpp_culture_get_number_negative_infinity_symbol()
{
    return Intern("-Infinity");
}

// --- Currency format (invariant NumberFormatInfo) ---

Il2CppString* il2cpp_culture_get_currency_symbol()
{
    return Intern("\xC2\xA4"); // U+00A4 (UTF-8 C2 A4) -- generic currency sign
}

Il2CppString* il2cpp_culture_get_currency_decimal_separator()
{
    return Intern(".");
}

Il2CppString* il2cpp_culture_get_currency_group_separator()
{
    return Intern(",");
}

// --- Percent format (invariant NumberFormatInfo) ---

Il2CppString* il2cpp_culture_get_percent_symbol()
{
    return Intern("%");
}

Il2CppString* il2cpp_culture_get_percent_decimal_separator()
{
    return Intern(".");
}

Il2CppString* il2cpp_culture_get_percent_group_separator()
{
    return Intern(",");
}

Il2CppString* il2cpp_culture_get_per_mille_symbol()
{
    return Intern("\xE2\x80\xB0"); // U+2030 (UTF-8 E2 80 B0) -- per-mille sign
}

}} // namespace il2cpp::runtime
