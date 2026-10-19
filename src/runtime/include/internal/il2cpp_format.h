#pragma once
//------------------------------------------------------------------------------
// il2cpp_format.h -- .NET-style numeric formatting icalls (format.cpp).
//
// The transpiler maps the BCL formatting entry points (System.Int32.ToString,
// System.Int64.ToString, System.Boolean.ToString, System.Char.ToString,
// System.Double.ToString, System.Int32.ToString("X") and the Parse family) to
// these runtime helpers -- the same icall pattern as Console.WriteLine:
//
//   icall_<owner>_<name>_<type-suffix>
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour:
//   * int32/int64 ToString: decimal digits, '-' prefix for negatives, no
//     grouping, no leading zeros, no '+' sign (InvariantCulture).
//   * bool ToString: "True" / "False" (capitalized T/F -- .NET semantics).
//   * char ToString: a single-UTF-16-unit string.
//   * double ToString: net48's DEFAULT "G15" behaviour -- 15 significant
//     digits; fixed-point when the (rounded) decimal exponent is in
//     [-4, 14] ("0.0001", "100000000000000"), scientific notation outside
//     ("1E-05", "1E-06", "1E+15"); negative zero prints "0";
//     NaN -> "NaN". EMPIRICALLY VERIFIED against a net48 reference run:
//     (0.1 + 0.2) -> "0.3" (plain G15 -- net48 has NO round-trip fallback;
//     .NET Core 3.0+ prints "0.30000000000000004") and the boundary values
//     above match exactly.
//     infinities render as the zh-CN culture symbols U+221E/-U+221E (U+221E,
//     UTF-8 E2 88 9E), byte-identical to the net48 reference output.
//   * int32 ToString("X"): uppercase hex without "0x"; no zero-padding
//     ("FF", "10"); negative values print the full 8-digit twos-complement
//     ("FFFFFFFF" for -1). "0" prints "0".
//   * int.Parse: skips leading/trailing ASCII whitespace, optional +/-
//     sign, decimal digits; non-digit or empty text -> FormatException;
//     value outside [int.MinValue, int.MaxValue] -> OverflowException.
//   * double.Parse: skips leading/trailing ASCII whitespace, optional sign,
//     digits, optional '.', optional e/E exponent, or the exact culture
//     words "NaN" / U+221E / -U+221E (U+221E -- the zh-CN NumberFormatInfo
//     infinity symbols); the ASCII words "Infinity"/"-Infinity" are NOT
//     legal double text (FormatException, net48 zh-CN probe-pinned);
//     anything else -> FormatException; |value| beyond double.MaxValue ->
//     OverflowException (net48 throws; .NET Core 3.0+ returns Infinity).
//     Underflow -> 0.0.
//
//   Known deviations from .NET (documented, see format.cpp):
//     * Whitespace handling is the ASCII set (0x09-0x0D, 0x20); .NET also
//       accepts Unicode whitespace (e.g. U+00A0).
//     * double.Parse does not accept group separators ("1,000" -> Format
//       Exception; .NET's default NumberStyles.Float|AllowThousands does).
//     * Integer formatting never uses std::to_chars / snprintf: a manual
//       digit loop (deterministic, locale-independent, GCC 7.5 compatible).
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "il2cpp_exception.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// int32 -> decimal string ("-123", "0", "2147483647", "-2147483648").
Il2CppString* icall_int32_to_string(int32_t value);

// int64 -> decimal string ("-9223372036854775808" .. "9223372036854775807").
Il2CppString* icall_int64_to_string(int64_t value);

// uint32/uint64 -> full-range decimal string (never the signed
// reinterpretation: UInt32.MaxValue -> "4294967295", UInt64.MaxValue ->
// "18446744073709551615"). Decimal-domain C44 (decgap) wiring for
// System.UInt32.ToString()/System.UInt64.ToString().
Il2CppString* icall_uint32_to_string(uint32_t value);
Il2CppString* icall_uint64_to_string(uint64_t value);

// bool -> "True" / "False" (capitalized, .NET semantics).
Il2CppString* icall_bool_to_string(bool value);

// One UTF-16 code unit -> single-unit managed string (unit 0 produces a
// 1-unit string containing NUL -- .NET char.ToString semantics).
Il2CppString* icall_char_to_string(int32_t utf16);

// double -> net48-style "G15" text (see header: 15 sig digits, .NET
// fixed/scientific layout, specials "NaN"/U+221E/-U+221E/"0").
Il2CppString* icall_double_to_string(double value);
Il2CppString* icall_single_to_string(double value);

// int32 -> uppercase hex, no "0x", no padding ("FF", "80000000" for
// int.MinValue -- twos-complement over 32 bits).
Il2CppString* icall_int32_to_string_hex(int32_t value);

// int.Parse: ASCII-whitespace tolerant, optional sign, decimal digits.
// Empty/invalid text -> FormatException; out of int32 range ->
// OverflowException. Returns the parsed value.
int32_t icall_string_parse_int(Il2CppString* text);

// Int64.Parse: the same strict contract with int64 bounds (see
// icall_string_parse_int). Returns the parsed value.
int64_t icall_string_parse_long(Il2CppString* text);

// Convert.ToBoolean(string): null -> false; "true"/"false"
// case-insensitive with surrounding whitespace; anything else ->
// FormatException. Returns 0/1.
int32_t icall_convert_to_boolean_string(Il2CppString* text);

// bool.Parse(string): the strict form -- null -> ArgumentNullException;
// otherwise identical to icall_convert_to_boolean_string.
int32_t icall_bool_parse(Il2CppString* text);

// char.Parse(string): exactly one UTF-16 unit; null -> ArgumentNullException,
// any other length -> FormatException. Returns the unit.
int32_t icall_char_parse(Il2CppString* text);

// double.Parse: ASCII-whitespace tolerant, legal double text (see header).
// Invalid text -> FormatException; |value| > double.MaxValue ->
// OverflowException. Returns the parsed value.
double icall_string_parse_double(Il2CppString* text);

}} // namespace il2cpp::runtime
