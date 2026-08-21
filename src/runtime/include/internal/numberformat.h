#pragma once
//------------------------------------------------------------------------------
// numberformat.h -- fixed-culture number formatting (numberformat.cpp).
//
// .NET Framework 4.8 ToString(format) semantics with a FIXED CULTURE: the
// net48 default test baseline is the verification host's zh-CN culture --
// ',' thousands separator, '.' decimal point, percent layout "n %" (space
// before '%'), special spellings "NaN"/"∞"/"-∞" (U+221E, the zh-CN
// NumberFormatInfo symbols -- the SAME spelling format.cpp/formatext.cpp
// emit, so all three formatting modules agree).
//
// The transpiler maps BCL numeric ToString(format) overloads to these
// helpers. Unlike il2cpp_format_ext.h (which fixes the verification host's
// zh-CN culture: U+00A5 '¥' currency, "n%" percent, U+221E infinity), this
// module keeps the invariant separators/layout while matching the zh-CN
// infinity symbols -- the divergence-free baseline for the net48 default
// test environment.
//
// FORMATS (rounding is .NET's half AWAY FROM ZERO throughout):
//   general(value)              G   plain decimal digits; the double variant
//                                   prints G15 significant digits (reuses
//                                   icall_double_to_string).
//   fixed(value, prec)          N   thousands-grouped integer part with
//                                   exactly 'prec' fraction digits
//                                   (N0 -> "1,234,567", N2 -> "1,234,567.89").
//   ffixed(value, prec)         F   fixed WITHOUT grouping (F2 of
//                                   1234567.89 -> "1234567.89" -- the F/N
//                                   difference; default precision 2).
//   hex(value, prec)            X   uppercase hex, two's-complement for
//                                   negatives, zero-padded to 'prec' digits
//                                   (255 X4 -> "00FF"; -1 -> 16 digits).
//   scientific(value, prec)     E   "d.fracE[+-]ddd" (3-digit exponent);
//                                   int64 uses the exact decimal digits (no
//                                   double round-trip), double uses
//                                   correctly-rounded digits + guard digits;
//                                   default precision 6.
//   percent(value, prec)        P   value*100, grouped, invariant layout
//                                   "12.3 %"; specials "NaN"/"∞"/"-∞"
//                                   WITHOUT the decoration;
//                                   default precision 2. The int64 variant
//                                   scales via an EXACT decimal digit shift.
//   custom(value, format)       extended custom formats (net48 oracle-
//                                   verified 2026-08-09): '0' mandatory digit,
//                                   '#' optional digit, '.' decimal separator,
//                                   ',' thousands grouping or ÷1000 scaling
//                                   (trailing commas), '%' (×100) / U+2030
//                                   '‰' (×1000) at their pattern position,
//                                   '\' escapes, '..'/".." quoted literals,
//                                   other characters as literals. Integer
//                                   digits beyond the placeholder count are
//                                   emitted before the first placeholder; the
//                                   sign precedes every literal and is
//                                   suppressed for a rounded-zero magnitude.
//                                   Scientific notation ("0.00E+00"/"0E0")
//                                   and ';' sections -> FormatException
//                                   (out of subset); a null format -> "G".
//
// 'prec' is 0..99 (net48 range); a value < 0 selects the net48 per-format
// default (N/F/P -> 2, E -> 6, X -> 1). Every function returns the formatted
// System.String as an Il2CppObject* carrier.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// int64 -> "G" (plain decimal, no grouping/padding).
Il2CppObject* il2cpp_number_format_general(int64_t value);

// double -> "G15" (the net48 default double text).
Il2CppObject* il2cpp_number_format_general_double(double value);

// int64 -> "N<prec>" (thousands-grouped, 'prec' fraction digits).
Il2CppObject* il2cpp_number_format_fixed(int64_t value, int32_t precision);

// double -> "N<prec>" (grouped, same rounding as F).
Il2CppObject* il2cpp_number_format_fixed_double(double value, int32_t precision);

// int64 -> "F<prec>" (fixed WITHOUT grouping, 'prec' fraction digits).
Il2CppObject* il2cpp_number_format_ffixed(int64_t value, int32_t precision);

// double -> "F<prec>" (fixed without grouping, same rounding as N).
Il2CppObject* il2cpp_number_format_ffixed_double(double value, int32_t precision);

// int64 -> "X<prec>" (uppercase hex, two's-complement, zero-padded).
Il2CppObject* il2cpp_number_format_hex(int64_t value, int32_t precision);

// int64 -> "E<prec>" (exact decimal digits, 3-digit exponent).
Il2CppObject* il2cpp_number_format_scientific(int64_t value, int32_t precision);

// double -> "E<prec>" (e.g. 0.00123 E2 -> "1.23E-003").
Il2CppObject* il2cpp_number_format_scientific_double(double value, int32_t precision);

// double -> "P<prec>" (value*100, grouped, invariant " %" layout).
Il2CppObject* il2cpp_number_format_percent(double value, int32_t precision);

// int64 -> "P<prec>" (value*100 via an EXACT decimal digit shift, grouped).
Il2CppObject* il2cpp_number_format_percent_int(int64_t value, int32_t precision);

// int64 -> simplified custom "0.00" family (see header). null -> "G".
Il2CppObject* il2cpp_number_format_custom(int64_t value, Il2CppObject* format);

// double -> simplified custom "0.00" family (see header). null -> "G".
Il2CppObject* il2cpp_number_format_custom_double(double value, Il2CppObject* format);

// D-CUL-1 (batch 118): the zh-CN NumberFormatInfo.CurrencySymbol getter --
// U+00A5 '¥' (the verification host's net48 current culture; formatext.cpp's
// C-format prefix symbol agrees; the invariant culture keeps U+00A4 in
// cultureinfo.cpp). Returns the string in the reference slot (Il2CppObject*),
// like the sibling numberformat helpers.
Il2CppObject* il2cpp_number_format_currency_symbol();

}} // namespace il2cpp::runtime
