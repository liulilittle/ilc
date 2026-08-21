#pragma once
//------------------------------------------------------------------------------
// il2cpp_format_ext.h -- ToString(format) custom-format icalls (formatext.cpp).
//
// The transpiler maps the BCL ToString(string format) overloads
// (System.Int32.ToString(string), System.Int64.ToString(string),
// System.Double.ToString(string)) to these runtime helpers -- the same icall
// pattern as il2cpp_format.h:
//
//   icall_format_int32_custom(int32_t value, Il2CppObject* format)
//   icall_format_int64_custom(int64_t value, Il2CppObject* format)
//   icall_format_double_custom(double value, Il2CppObject* format)
//
// 'format' is a System.String object (null is legal); the return value is the
// formatted System.String (Il2CppObject* carrier). SEMANTIC BASELINE: .NET
// Framework 4.8 (net48) -- empirically verified via tests/cases/formatext
// (oracle capture on the verification host, zh-CN default culture).
//
// SUPPORTED SUBSET (standard formats; <prec> = 0..99 fraction/significant
// digits where applicable, default in parentheses):
//   int32/int64:  D<prec>  decimal, zero-padded to <prec> digits after the
//                          sign ("-00042"); no grouping.
//                  X<prec>  uppercase hex, two's-complement for negatives
//                          ("FFFFFF85" for -123, 32/16 digits), zero-padded.
//                  N<prec>  thousands-grouped ("12,345.00", <prec> default 2).
//                  C        currency: grouped, 2 fraction digits, fixed
//                          symbol (culture fixing below).
//                  E<prec>  scientific ("1.23E+004"; exponent sign + at
//                          least 3 digits; <prec> default 6 mantissa digits).
//                  G<prec>  decimal digits, no grouping/padding; with an
//                          explicit precision, significant-digit rounding
//                          ("G2" of 12345 -> "1.2E+04").
//                  F<prec>  fixed without grouping ("255.00", default 2).
//                  P<prec>  percent: exact ×100 digit shift, grouped,
//                          trailing '%' ("25,500.0%", default 2).
//                  CUSTOM   the extended custom engine (below).
//   double:       F<prec>  fixed ("1234.57", default 2).
//                  N<prec>  thousands-grouped (same rounding as F).
//                  E<prec>  scientific ("1.23E-003").
//                  G        net48 default "G15" (15 significant digits --
//                          reuses icall_double_to_string).
//                  P<prec>  percent: value*100, grouped, trailing '%'
//                          (default 2).
//                  CUSTOM   extended custom engine (net48 oracle-verified
//                          2026-08-09): '0' (mandatory digit), '#' (optional
//                          digit), '.' (decimal separator; extra '.' ignored),
//                          ',' (thousands grouping between two integer digit
//                          placeholders, or ÷1000 scaling per trailing comma),
//                          '%' (×100) and U+2030 '‰' (×1000) at their pattern
//                          position, '\' escapes, '..'/".." quoted literals,
//                          other characters as literals. Integer digits beyond
//                          the placeholder count are emitted before the first
//                          placeholder; the sign precedes every literal and is
//                          suppressed for a rounded-zero magnitude; a standard
//                          specifier with trailing junk falls back to custom
//                          ("D100" of 12345 -> "D112345").
//   format == null or "" behaves like "G" (.NET contract).
//
// ERROR CONTRACT: unknown specifier letter (or a letter outside the type's
// subset), a custom format containing scientific notation ("0.00E+00"/"0E0")
// or ';' section separators, trailing junk after the precision digits, or a
// precision outside 0-99 -> FormatException ("Format specifier was invalid.").
// Non-finite values return the culture symbol BEFORE any format validation
// (net48: NaN with "Q" -> "NaN", +Inf with "Q" -> U+221E).
//
// KNOWN DIVERGENCES (honest; net48 oracle capture):
//   * CULTURE FIXING: net48 "C"/"P" output follows the process
//     CurrentCulture. This runtime FIXES the verification host's net48
//     default (zh-CN): currency symbol U+00A5 '¥' with negative pattern
//     "¥-n" ("¥-12,345.00"), percent "n%"/"-n%" (no space). Under other
//     cultures net48 output differs -- reported.
//   * Custom scientific notation ("0.00E+00" -> net48 "1.23E+04") and
//     ';' section separators ("0;#", "#,##0.00;(#,##0.00)") are out of
//     subset and throw FormatException (net48 formats them).
//   * int32/int64 accept the standard subset above plus the full custom
//     engine; net48 additionally accepts a few spellings this runtime does
//     not distinguish (out of subset -- FormatException).
//   * Rounding is .NET's half AWAY FROM ZERO, implemented over
//     correctly-rounded snprintf output plus 4 guard digits; a value whose
//     exact decimal expansion sits inside the guard region below a half-way
//     tie may round differently (pathological; no practical case).
//   * Non-finite double output follows the existing runtime contract
//     ("NaN"/"Infinity"/"-Infinity" -- format.cpp precedent). net48's
//     infinity spelling is CULTURE-DEPENDENT: the zh-CN default culture
//     renders "∞"/"-∞" (U+221E) in F/N/E too, while InvariantCulture
//     renders "Infinity"/"-Infinity" (both verified against net48); the
//     runtime fixes the ASCII spelling.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "il2cpp_exception.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// int32/uint32 -> formatted string per the subset above. 'format' == null or
// "" selects "G". Unknown format -> FormatException.
Il2CppObject* icall_format_int32_custom(int32_t value, Il2CppObject* format);

// int64 -> formatted string per the subset above (hex is 16-digit
// two's-complement for negatives).
Il2CppObject* icall_format_int64_custom(int64_t value, Il2CppObject* format);

// double -> formatted string per the subset above (F/N/E/G/P + simplified
// '0'/'.' custom formats). Unknown format -> FormatException.
Il2CppObject* icall_format_double_custom(double value, Il2CppObject* format);

//------------------------------------------------------------------------------
// icall_format_composite_item -- format ONE composite-format argument (the
// "{N[,width][:formatString]}" item of String.Format / StringBuilder.AppendFormat
// -- the shared engine the composite parser delegates to).
//
//   boxed     : the boxed argument (null -> the empty string, net48);
//   format    : the format-string component (System.String, null/empty -> the
//               arg's default text);
//   alignment : the net48 field-width specifier (0 = none; positive =
//               right-align to |alignment|; negative = left-align; the width
//               is a MINIMUM -- a longer formatted text is never truncated).
//
// Dispatch (net48 composite semantics): boxed Int32/Int64/Double/Single route
// through the icall_format_*_custom engine (so "f2"/"x"/"X"/... work); boxed
// Decimal through the decimal custom icall; a STRING arg returns its payload
// unchanged (the specifier is ignored -- System.String is not IFormattable);
// any other object prints its plain ToString() text (specifier ignored). The
// result is then space-padded to the alignment width. Returns a fresh string.
//------------------------------------------------------------------------------
Il2CppObject* icall_format_composite_item(Il2CppObject* boxed, Il2CppObject* format, int32_t alignment);

}} // namespace il2cpp::runtime
