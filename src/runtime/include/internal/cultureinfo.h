#pragma once
//------------------------------------------------------------------------------
// cultureinfo.h -- System.Globalization.CultureInfo minimal runtime domain
// (cultureinfo.cpp).
//
// SCOPE: basic culture support only -- the fixed invariant culture
// (InvariantCulture) and a simplified CurrentCulture. There is NO
// CultureInfo object model in this runtime yet (no Il2CppObject-based
// culture instances, no CultureInfo class descriptor): every string query
// below returns a STATIC managed string (interned, so the same instance on
// every call and GC-rooted by the string intern pool).
//
// .NET SEMANTIC BASELINE (net48, probe-verified 2026-08-08):
//   * CultureInfo.InvariantCulture -- Name "" (String.Empty), DisplayName/
//     EnglishName/NativeName "Invariant Language (Invariant Country)",
//     TwoLetterISOLanguageName "iv", ThreeLetterISOLanguageName "ivl",
//     ThreeLetterWindowsLanguageName "IVL", LCID 127, Parent.Name "",
//     UseUserOverride false, IsReadOnly true.
//   * NumberFormatInfo (invariant) -- NumberDecimalSeparator ".",
//     NumberGroupSeparator ",", NumberDecimalDigits 2, NumberGroupSizes
//     {3}, NumberNegativePattern 1, NegativeSign "-", PositiveSign "+",
//     NaNSymbol "NaN", PositiveInfinitySymbol "Infinity",
//     NegativeInfinitySymbol "-Infinity", CurrencySymbol U+00A4,
//     CurrencyDecimalSeparator ".", CurrencyGroupSeparator ",",
//     CurrencyDecimalDigits 2, CurrencyGroupSizes {3},
//     CurrencyPositivePattern 0, CurrencyNegativePattern 0,
//     PercentSymbol "%", PercentDecimalSeparator ".", PercentGroupSeparator
//     ",", PercentDecimalDigits 2, PercentGroupSizes {3},
//     PercentNegativePattern 0, PerMilleSymbol U+2030, NativeDigits 0..9.
//   * CultureInfo.CurrentCulture -- the PROCESS DEFAULT (locale probing).
//     On the verification host net48 reports zh-CN (LCID 2052); this
//     runtime reads the OS user locale too (GetUserDefaultLCID, C42 fix)
//     so CurrentCulture.Name is "zh-CN" on this host as well.
//
// CULTURE DIVERGENCE (documented; determinism is preserved):
//   * net48 CurrentCulture is the OS locale (zh-CN on the verification
//     host) and CurrentCulture.Equals(InvariantCulture) is false. This
//     runtime mirrors that identity (get_current reads the OS user locale,
//     mapped through cultureinfo.cpp's LCID table) but keeps the number/
//     currency/percent symbols on the invariant baselines where zh-CN
//     agrees (the number separators) or where a real culture object is not
//     yet reachable (currency U+00A4 -- zh-CN uses U+00A5, formatext.cpp
//     pins the zh-CN currency on its own domain). Unknown LCIDs and
//     non-Windows hosts fall back to the invariant name "" so the output
//     stays deterministic everywhere else.
//   * The transpiler side (MethodMap.CultureInfo.cs) now wires
//     get_CurrentCulture to il2cpp_culture_get_current (the C42 fold fix);
//     get_InvariantCulture still folds to null. The il2cpp_culture_* icalls
//     are therefore REACHABLE from transpiled code, and the culture-identity
//     path ("zh-CN" vs "") is aligned with the net48 oracle on the zh-CN
//     verification host.
//   * The zh-CN-vs-invariant divergence is NOT observable in the three
//     pinned separators (zh-CN also uses "." / "," / "-" -- net48 probe),
//     which is why the fixed-culture format domains (numberformat.cpp
//     invariant, formatext.cpp zh-CN) agree on them; it IS observable in
//     the identity ("" vs "zh-CN") and the currency symbol (U+00A4 vs
//     U+00A5 (yen sign)), so those stay internal to this domain.
//
// The transpiler wires System.Globalization.CultureInfo.* properties
// (Name / InvariantCulture / CurrentCulture) and the NumberFormatInfo
// separator/sign/currency/percent properties to these icalls.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// --- Culture identity (invariant) --------------------------------------

// The name of the culture in effect (CurrentCulture). Fixed to the
// invariant culture -> "" (net48: InvariantCulture.Name is String.Empty).
Il2CppString* il2cpp_culture_get_name();

// The invariant culture's name -- "" (net48 semantics). No culture object
// representation: the name stands in for the instance.
Il2CppString* il2cpp_culture_get_invariant();

// The current culture's name -- the OS user locale's culture NAME on
// Windows (GetUserDefaultLCID, mapped by cultureinfo.cpp's LCID table;
// "zh-CN" on the zh-CN verification host), "" on non-Windows hosts and for
// unmapped LCIDs. C42 fold fix: the transpiler's get_CurrentCulture maps to
// this icall (it used to fold to the fixed "en-US" literal).
Il2CppString* il2cpp_culture_get_current();

// CultureInfo.InvariantCulture.LCID -- 127 (net48 oracle).
int32_t il2cpp_culture_get_invariant_lcid();

// CultureInfo.TwoLetterISOLanguageName of the invariant culture -- "iv".
Il2CppString* il2cpp_culture_get_two_letter_iso_language_name();

// CultureInfo.GetCultureInfo(string) -- handle-gap: a CultureInfo IS its
// culture-name string, so this returns the NAME (canonical-cased for a known
// culture, "en-us"/"EN-us" -> "en-US"; the verbatim input for a well-formed
// unknown whose PRIMARY segment is a known language, "no-Such-XX-yy" ->
// "no-Such-XX-yy"). A null input / an entirely unknown primary returns null
// (M3: net48 throws ArgumentNullException/CultureNotFoundException). The
// transpiler stamps the result as a CultureInfo so chained get_Name/
// get_TwoLetterISOLanguageName fold on the tracked type.
Il2CppString* il2cpp_culture_get_culture_info(Il2CppObject* name);

// CultureInfo.TwoLetterISOLanguageName on the culture-name carrier: the
// primary (first '-' segment) mapped through the ISO 639-1 code table
// (identity for most; net48 legacy pins: "no" -> "nb", "iw" -> "he",
// "in" -> "id", "ji" -> "yi"). The invariant culture's EMPTY name maps to
// "iv" (the net48 invariant pin); any other primary falls back to itself.
Il2CppString* il2cpp_culture_get_two_letter_from_name(Il2CppObject* name);

// CultureInfo.ThreeLetterISOLanguageName of the invariant culture -- "ivl".
Il2CppString* il2cpp_culture_get_three_letter_iso_language_name();

// CultureInfo.ThreeLetterWindowsLanguageName of the invariant culture -- "IVL".
Il2CppString* il2cpp_culture_get_three_letter_windows_language_name();

// --- Number format (invariant NumberFormatInfo) -------------------------

// NumberFormatInfo.DecimalSeparator of the invariant culture: ".".
Il2CppString* il2cpp_culture_get_number_decimal_separator();

// NumberFormatInfo.NumberGroupSeparator of the invariant culture: ",".
Il2CppString* il2cpp_culture_get_number_group_separator();

// NumberFormatInfo.NegativeSign of the invariant culture: "-".
Il2CppString* il2cpp_culture_get_number_negative_sign();

// NumberFormatInfo.PositiveSign of the invariant culture: "+".
Il2CppString* il2cpp_culture_get_number_positive_sign();

// NumberFormatInfo.NaNSymbol of the invariant culture: "NaN".
Il2CppString* il2cpp_culture_get_number_nan_symbol();

// NumberFormatInfo.PositiveInfinitySymbol of the invariant culture:
// "Infinity".
Il2CppString* il2cpp_culture_get_number_positive_infinity_symbol();

// NumberFormatInfo.NegativeInfinitySymbol of the invariant culture:
// "-Infinity".
Il2CppString* il2cpp_culture_get_number_negative_infinity_symbol();

// --- Currency format (invariant NumberFormatInfo) -----------------------

// NumberFormatInfo.CurrencySymbol of the invariant culture: U+00A4 (the
// generic currency sign; the zh-CN host culture instead uses U+00A5 (yen sign)).
Il2CppString* il2cpp_culture_get_currency_symbol();

// NumberFormatInfo.CurrencyDecimalSeparator of the invariant culture: ".".
Il2CppString* il2cpp_culture_get_currency_decimal_separator();

// NumberFormatInfo.CurrencyGroupSeparator of the invariant culture: ",".
Il2CppString* il2cpp_culture_get_currency_group_separator();

// --- Percent format (invariant NumberFormatInfo) ------------------------

// NumberFormatInfo.PercentSymbol of the invariant culture: "%".
Il2CppString* il2cpp_culture_get_percent_symbol();

// NumberFormatInfo.PercentDecimalSeparator of the invariant culture: ".".
Il2CppString* il2cpp_culture_get_percent_decimal_separator();

// NumberFormatInfo.PercentGroupSeparator of the invariant culture: ",".
Il2CppString* il2cpp_culture_get_percent_group_separator();

// NumberFormatInfo.PerMilleSymbol of the invariant culture: U+2030.
Il2CppString* il2cpp_culture_get_per_mille_symbol();

}} // namespace il2cpp::runtime
