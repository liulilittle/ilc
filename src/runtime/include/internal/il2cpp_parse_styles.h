#pragma once
//------------------------------------------------------------------------------
// il2cpp_parse_styles.h -- IFormatProvider / NumberStyles-aware numeric parse
// icalls (parse_styles.cpp).
//
// The pre-existing parse surface is CULTURE-FIXED:
//   icall_string_parse_double / icall_try_parse_double (il2cpp_format.h /
//   il2cpp_parse.h) always use the CURRENT culture and the double.Parse
//   default NumberStyles.
// The overloads that take an IFormatProvider / NumberStyles need the
// provider's NumberFormatInfo, which is what this module adds.
//
// PROVIDER MODEL: this runtime has no IFormatProvider object model -- a
// provider IS its culture-NAME string (the CultureInfo handle-gap documented
// in cultureinfo.h). nullptr means "no provider" == the CURRENT culture,
// exactly like net48. cultureinfo.h's il2cpp_culture_number_info resolves a
// provider to the separator/currency members the parsers consume.
//
// SEMANTICS (measured against a real net48 CLR; see parsegroupsep1):
//   * The GROUP separator is whatever the provider exposes ("," invariant /
//     zh-CN, "." de-DE, U+202F fr-FR). Grouping is NOT validated: separator
//     runs anywhere after the first digit are skipped ("1,,2" -> 12,
//     "1,00" -> 100, "1," -> 1), a separator BEFORE the first digit fails.
//   * The DECIMAL separator is likewise the provider's (de-DE swaps the
//     roles: "1.000,5" -> 1000.5).
//   * AllowLeadingWhite/AllowTrailingWhite accept the ASCII set only (the
//     measured net48 double behaviour: U+00A0 edges are a FormatException).
//   * AllowLeadingSign/AllowTrailingSign/AllowParentheses/
//     AllowCurrencySymbol (provider symbol)/AllowExponent/AllowThousands/
//     AllowDecimalPoint are honored per flag.
//   * Undefined style bits -> ArgumentException. AllowHexSpecifier is a
//     DEFINED bit that the floating-point parse REJECTS (FormatException on
//     net48 -- no hex float text exists).
//   * Malformed text -> FormatException; |value| beyond double.MaxValue ->
//     OverflowException (Parse) / failure (TryParse).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// double.Parse(string, NumberStyles, IFormatProvider). Throws on bad text.
double icall_string_parse_double_styles(Il2CppString* text, int32_t styles,
                                       Il2CppObject* provider);

// double.TryParse(string, NumberStyles, IFormatProvider, out double).
// Never throws for bad TEXT (out slot zeroed, 0 returned); undefined style
// bits still throw ArgumentException (net48 validates the style first).
int32_t icall_try_parse_double_styles(Il2CppObject* s, int32_t styles,
                                      Il2CppObject* provider, double* result);

// Shared core for the DEFAULT-provider double parse entry points
// (icall_string_parse_double / icall_try_parse_double). 'provider' nullptr
// means the CURRENT culture. NumberStyles must already be validated.
// Returns 0 + *out on success, 1 on malformed text, 2 on out-of-range
// magnitude (so the throwing wrapper can pick FormatException vs
// OverflowException while the TryParse wrapper folds both to failure).
int32_t il2cpp_parse_double_default(const char16_t* chars, int32_t length,
                                    Il2CppObject* provider, double* out);

}} // namespace il2cpp::runtime

