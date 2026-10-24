#pragma once
//------------------------------------------------------------------------------
// il2cpp_convert.h -- System.Convert static-method runtime support (icalls).
//
// The transpiler maps the System.Convert family (Convert.ToInt32 / ToInt64 /
// ToDouble / ToString / ToBoolean / ToChar) to these runtime helpers -- the
// same icall pattern as the format.cpp and math.cpp helpers.
//
// Naming follows the task-domain convention:
//   icall_convert_to_<target>_<source-suffix>
// with _i4 = int32, _i8 = int64, _d = double, _string = managed string,
// _bool = bool, _char = UTF-16 unit (int32 carrier, matching the generated
// code ABI: bool/char travel as int32 0/1 and UTF-16 units).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- EMPIRICALLY PINNED by the
// convertcase e2e reference run (tests/cases/convertcase/convertcase.cs):
//   * The "null means zero" rule: Convert.ToInt32(null) / ToInt64(null) /
//     ToDouble(null) return 0 -- the Convert family differs from int.Parse
//     here (Parse throws). null is NOT an error for the string conversions.
//   * Convert.ToInt32(long) / ToInt32(double) / ToChar(int) are CHECKED
//     conversions: out-of-range input throws OverflowException (net48
//     verified: ToInt32(2147483648L), ToInt32(2147483648.0), ToInt32(NaN),
//     ToChar(0x1F600), ToChar(-1) all throw OverflowException). In-range
//     doubles ROUND half-to-even (Math.Round semantics: ToInt32(2.9) == 3,
//     ToInt32(2.5) == 2, ToInt32(3.5) == 4).
//   * Convert.ToBoolean(int): non-zero -> true (0/1 result).
//   * Convert.ToInt32(char): zero-extends 0..0xFFFF, never throws.
//   * The ToString family delegates to the format.cpp icalls (int32/int64/
//     double/bool), and the string conversions delegate to the format.cpp
//     Parse icalls where a compatible one exists; int64 parsing is
//     implemented here (format.cpp has no int64 parser -- read-only).
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "il2cpp_exception.h"
#include "decimal.h" // p0conv2 (D1): Il2CppDecimal result-slot parameters

#include <cstdint>

namespace il2cpp { namespace runtime {

// Convert.ToInt32(string): null -> 0 (Convert null semantics); otherwise the
// int.Parse semantics of icall_string_parse_int (FormatException for invalid
// text, OverflowException for out-of-range values).
int32_t icall_convert_to_int32_string(Il2CppString* value);

// Convert.ToInt32(string, int fromBase): net48 ParseNumbers.StringToInt with
// the IsTight contract (EMPIRICALLY PINNED against a net48 reference run --
// parsecase e2e + a dedicated probe; the check order below is exact):
//   * fromBase must be 2, 8, 10 or 16; otherwise ArgumentException (checked
//     BEFORE the string, so Convert.ToInt32(null, 3) throws).
//   * null text -> 0 (the Convert "null means zero" rule -- this overload
//     does NOT throw ArgumentNullException in net48).
//   * the parse is TIGHT: no leading/trailing whitespace (" 42 ", 10 throws
//     FormatException); an EMPTY string throws ArgumentOutOfRangeException
//     (net48 StringToInt quirk, probe-pinned).
//   * sign: '-' is legal ONLY for radix 10 ("-101", 2 throws
//     ArgumentException); '+' is consumed for ANY radix ("+101", 2 == 5).
//   * radix 16 consumes an optional "0x"/"0X" prefix AFTER the sign
//     ("0xFF", 16 == 255); "0b"/"0o" prefixes are NOT accepted.
//   * radix 10 range-checks against int32 ("2147483648" throws
//     OverflowException; "-2147483648" yields INT32_MIN).
//   * radix 2/8/16 accumulate in uint32: the sign bit is allowed
//     ("80000000", 16 -> INT32_MIN; "FFFFFFFF", 16 -> -1) but exceeding
//     0xFFFFFFFF throws OverflowException ("100000000", 16).
//   * no parsable digits or trailing junk -> FormatException.
int32_t icall_convert_to_int32_string_base(Il2CppString* value, int32_t fromBase);

// Convert.ToInt32(long): CHECKED truncation -- in-range values truncate to
// int32; out-of-range -> OverflowException (net48 contract).
int32_t icall_convert_to_int32_i8(int64_t value);

// Convert.ToInt32(double): ROUNDS half-to-even (Math.Round semantics -- NOT
// truncation) then range-checks the rounded value against [int32.MinValue,
// 2^31): 2147483647.0 legal, 2147483647.5 -> OverflowException,
// -2147483648.5 -> int32.MinValue. NaN / +-Infinity -> OverflowException.
int32_t icall_convert_to_int32_d(double value);

// Convert.ToInt64/ToUInt32/ToUInt64/ToByte/ToSByte/ToUInt16/ToInt16(double):
// same contract as ToInt32(double) -- round half-to-even FIRST (NOT the
// conv.ovf truncation), then range-check the ROUNDED value against the
// destination: int64 [-2^63, 2^63), uint32 [0, 2^32), uint64 [0, 2^64),
// byte [0, 256), sbyte [-128, 128), ushort [0, 65536), short [-32768, 32768).
// NaN / +-Infinity -> OverflowException; unsigned targets also throw for ANY
// negative input (even fractions rounding to -0.0), -0.0 itself yields 0.
// Narrow widths ride int32 carriers
// (M2 slot model); float sources widen to double at the call site.
int64_t icall_convert_to_int64_d(double value);
uint32_t icall_convert_to_uint32_d(double value);
uint64_t icall_convert_to_uint64_d(double value);
int32_t icall_convert_to_byte_d(double value);
int32_t icall_convert_to_sbyte_d(double value);
int32_t icall_convert_to_uint16_d(double value);
int32_t icall_convert_to_int16_d(double value);

// Convert.ToDouble(int32): exact widening, never throws.
double icall_convert_to_double_i4(int32_t value);

// Convert.ToDouble(string): null -> 0.0 (Convert null semantics); otherwise
// the double.Parse semantics of icall_string_parse_double (FormatException
// for invalid text, OverflowException for out-of-range magnitudes).
double icall_convert_to_double_string(Il2CppString* value);

// Convert.ToInt64(string): null -> 0 (Convert null semantics); decimal parse
// over the full int64 range (ASCII whitespace, optional sign); invalid text
// -> FormatException; out-of-range -> OverflowException.
int64_t icall_convert_to_int64_string(Il2CppString* value);

// Convert.ToString(int32/int64/double/bool): delegate to the format.cpp
// formatting icalls (net48 "G15" / decimal / "True"/"False").
Il2CppString* icall_convert_to_string_i4(int32_t value);
Il2CppString* icall_convert_to_string_i8(int64_t value);
Il2CppString* icall_convert_to_string_d(double value);
Il2CppString* icall_convert_to_string_bool(bool value);

// Convert.ToString(int, int toBase): net48 ParseNumbers.IntToString
// (EMPIRICALLY PINNED against a net48 reference run): toBase must be 2, 8, 10
// or 16, otherwise ArgumentException. Base 10 renders the signed decimal
// (identical to icall_convert_to_string_i4); bases 2/8/16 render the value as
// UNSIGNED (uint32) binary / octal / lowercase-hex with no leading zeros ("0"
// for zero) -- negative values print their two's-complement representation
// ("-42", 16 -> "ffffffd6"; INT32_MIN, 8 -> "20000000000").
Il2CppString* icall_convert_to_string_i4_base(int32_t value, int32_t toBase);

// Convert.ToString(object): net48 returns the EMPTY string for null and the
// argument's ToString() text otherwise (the IFormatProvider overload ignores
// the provider for every primitive). This is the null-safe wrapper over
// icall_object_to_string (which throws NullReferenceException on null, the
// Object.ToString contract) -- P0-narrow, 2026-10-22.
Il2CppString* icall_convert_to_string_obj(Il2CppObject* value);

// Convert.ToBoolean(int32): 0 -> 0, any non-zero (incl. negative) -> 1.
int32_t icall_convert_to_bool_i4(int32_t value);

// Convert.ToChar(int32): CHECKED -- value must be in 0..0xFFFF (the UTF-16
// unit range); outside -> OverflowException (net48: NOT a 16-bit truncation).
// Returns the UTF-16 unit as an int32 carrier.
int32_t icall_convert_to_char_i4(int32_t value);

// Convert.ToInt32(char): zero-extends the UTF-16 unit to int32, never throws.
int32_t icall_convert_to_int32_char(int32_t utf16);
int32_t icall_convert_to_int32_bool(int32_t value);

// System.IConvertible::ToInt32 on a boxed System.Int32 receiver: net48's
// Int32.ToInt32(IFormatProvider) is the identity (the provider is ignored).
// The transpiler unboxes the receiver at the call site, so this icall takes
// the already-extracted int32 value (convic1).
int32_t icall_iconvertible_to_int32(int32_t value);

//------------------------------------------------------------------------------
// p0conv2 (D1): System.Convert -> System.Decimal.
//
// The result is a 16-byte decimal, so these follow the decimal RESULT-SLOT
// contract (decimal.h): the target slot comes FIRST and the value is written
// through it (Il2CppDecimal* out).
//
//   * icall_convert_to_decimal_string -- Convert.ToDecimal(string[, provider]):
//     net48's "null means zero" rule (null -> 0m) over the Number.ParseDecimal
//     grammar; malformed text -> FormatException, out of range ->
//     OverflowException. It is NOT icall_decimal_parse (which throws
//     ArgumentNullException on null).
//   * icall_convert_to_decimal_char -- Convert.ToChar-style rejection:
//     Convert.ToDecimal(char) throws InvalidCastException on net48 (probe-pinned
//     for 'A'); no managed char can be a decimal, so the helper always throws.
//------------------------------------------------------------------------------
void icall_convert_to_decimal_string(Il2CppDecimal* out, Il2CppString* value);
void icall_convert_to_decimal_char(Il2CppDecimal* out, int32_t utf16);

}} // namespace il2cpp::runtime
