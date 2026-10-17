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
//     (U+00A4 vs U+00A5 (yen sign)) -- see the header.
//------------------------------------------------------------------------------

#include "cultureinfo.h"
#include "il2cpp_string_intern.h" // il2cpp_string_intern_literal (static returns)

#include <clocale>
#include <cstring>
#include <string>
#include <vector>

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

//------------------------------------------------------------------------------
// nb2 CULT-GET: CultureInfo.GetCultureInfo(string) + the culture-carrier
// TwoLetterISOLanguageName. Handle-gap: a CultureInfo IS its name string.
//------------------------------------------------------------------------------
namespace {

// net48 GetCultureInfo case-canonicalizes a KNOWN culture name: the culture
// table lookup is case-insensitive and returns the registered spelling
// ("en-us" / "EN-us" -> "en-US"; nb2 oracle). The fixed canonical table
// below covers the cultures the verification host and probe set exercise;
// every known-language primary carries an identity entry for neutral names
// ("fr" -> "fr"). A well-formed UNKNOWN whose primary IS a known language is
// echoed verbatim (nb2 oracle: "no-Such-XX-yy" -> "no-Such-XX-yy").
const char* const kCanonicalCultures[][2] = {
    {"en", "en"},       {"en-US", "en-US"}, {"en-GB", "en-GB"}, {"en-AU", "en-AU"},
    {"en-CA", "en-CA"}, {"en-IN", "en-IN"}, {"en-NZ", "en-NZ"}, {"en-ZA", "en-ZA"},
    {"en-IE", "en-IE"}, {"en-SG", "en-SG"},
    {"zh", "zh"},       {"zh-CN", "zh-CN"}, {"zh-TW", "zh-TW"}, {"zh-HK", "zh-HK"},
    {"zh-SG", "zh-SG"}, {"zh-MO", "zh-MO"},
    {"de", "de"},       {"de-DE", "de-DE"}, {"fr", "fr"},       {"fr-FR", "fr-FR"},
    {"it", "it"},       {"it-IT", "it-IT"}, {"es", "es"},       {"es-ES", "es-ES"},
    {"ja", "ja"},       {"ja-JP", "ja-JP"}, {"ko", "ko"},       {"ko-KR", "ko-KR"},
    {"pt", "pt"},       {"pt-BR", "pt-BR"}, {"pt-PT", "pt-PT"}, {"ru", "ru"},
    {"ru-RU", "ru-RU"}, {"nl", "nl"},       {"nl-NL", "nl-NL"}, {"pl", "pl"},
    {"pl-PL", "pl-PL"}, {"tr", "tr"},       {"tr-TR", "tr-TR"}, {"ar", "ar"},
    {"ar-SA", "ar-SA"}, {"cs", "cs"},       {"cs-CZ", "cs-CZ"}, {"el", "el"},
    {"el-GR", "el-GR"}, {"hu", "hu"},       {"hu-HU", "hu-HU"}, {"da", "da"},
    {"da-DK", "da-DK"}, {"fi", "fi"},       {"fi-FI", "fi-FI"}, {"sv", "sv"},
    {"sv-SE", "sv-SE"}, {"he", "he"},       {"he-IL", "he-IL"}, {"id", "id"},
    {"id-ID", "id-ID"}, {"nb", "nb"},       {"nb-NO", "nb-NO"}, {"nn", "nn"},
    {"nn-NO", "nn-NO"}, {"no", "nb"},
};

// net48 TwoLetterISOLanguageName -- the ISO 639-1 CODE of the primary
// language, with the legacy registry pins (.NET maps the alias/legacy
// primary codes to their ISO code; probe-pinned:
// GetCultureInfo("no-Such-XX-yy").TwoLetterISOLanguageName == "nb").
const char* const kLegacyTwoLetterPins[][2] = {
    {"no", "nb"}, {"iw", "he"}, {"in", "id"}, {"ji", "yi"},
};

char AsciiLower(char c)
{
    return (c >= 'A' && c <= 'Z') ? static_cast<char>(c + ('a' - 'A')) : c;
}

// ASCII case-insensitive equality (culture names are ASCII).
bool AsciiCiEquals(const char* a, const char* b)
{
    while (*a != '\0' && *b != '\0')
    {
        if (AsciiLower(*a) != AsciiLower(*b))
        {
            return false;
        }
        ++a;
        ++b;
    }
    return *a == *b;
}

// The managed UTF-16 payload -> an ASCII byte buffer (NUL-terminated).
// Returns false when any unit is non-ASCII (such a name cannot be a culture
// name -- net48 would fail it; the caller signals M3 null).
bool CsToAscii(const Il2CppString* s, std::vector<char>& out)
{
    out.reserve(static_cast<size_t>(s->length) + 1);
    for (int32_t i = 0; i < s->length; ++i)
    {
        const char16_t c = s->chars[i];
        if (c > 0x7F)
        {
            return false;
        }
        out.push_back(static_cast<char>(c));
    }
    out.push_back('\0');
    return true;
}

// The 'known primary language' set -- the first segment of every canonical
// culture entry (the set net48 treats as a resolvable primary: an unknown
// well-formed name whose primary is NOT here throws CultureNotFoundException).
bool IsKnownPrimary(const char* primary)
{
    const size_t plen = std::strlen(primary);
    for (const auto& e : kCanonicalCultures)
    {
        const char* dash = std::strchr(e[0], '-');
        const size_t elen = dash ? static_cast<size_t>(dash - e[0])
                                 : std::strlen(e[0]);
        if (plen != elen)
        {
            continue;
        }
        bool eq = true;
        for (size_t i = 0; i < plen; ++i)
        {
            if (AsciiLower(primary[i]) != AsciiLower(e[0][i]))
            {
                eq = false;
                break;
            }
        }
        if (eq)
        {
            return true;
        }
    }
    return false;
}

} // namespace

Il2CppString* il2cpp_culture_get_culture_info(Il2CppObject* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(name);
    std::vector<char> ascii;
    if (!CsToAscii(s, ascii))
    {
        return nullptr;
    }
    // 1. Canonical-cased lookup (case-insensitive table scan): a known
    //    culture returns its registered spelling.
    for (const auto& e : kCanonicalCultures)
    {
        if (AsciiCiEquals(ascii.data(), e[0]))
        {
            return Intern(e[1]);
        }
    }
    // 2. Primary-segment existence gate: a well-formed name whose primary is
    //    a known language is echoed VERBATIM (net48 builds a synthetic
    //    culture -- no re-case); an unknown primary would throw on net48
    //    (CultureNotFoundException) and signals M3 null here.
    std::vector<char> primary(ascii);
    char* dash = std::strchr(primary.data(), '-');
    if (dash != nullptr)
    {
        *dash = '\0';
    }
    if (!IsKnownPrimary(primary.data()))
    {
        return nullptr;
    }
    return reinterpret_cast<Il2CppString*>(
        il2cpp_string_new(s->chars, s->length));
}

Il2CppString* il2cpp_culture_get_two_letter_from_name(Il2CppObject* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(name);
    if (s->length == 0)
    {
        return Intern("iv"); // the invariant culture's ISO 639-1 pin (net48)
    }
    std::vector<char> ascii;
    if (!CsToAscii(s, ascii))
    {
        return Intern("");
    }
    char* dash = std::strchr(ascii.data(), '-');
    if (dash != nullptr)
    {
        *dash = '\0';
    }
    // Lowercase the primary (the ISO code's canonical case -- "EN-us" -> "en")
    // then apply the legacy registry pins ("no" -> "nb").
    for (char* c = ascii.data(); *c != '\0'; ++c)
    {
        *c = AsciiLower(*c);
    }
    for (const auto& pin : kLegacyTwoLetterPins)
    {
        if (AsciiCiEquals(ascii.data(), pin[0]))
        {
            return Intern(pin[1]);
        }
    }
    return Intern(ascii.data());
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
