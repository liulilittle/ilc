#pragma once
//------------------------------------------------------------------------------
// il2cpp_parse.h -- .NET TryParse-family icalls (parse.cpp).
//
// The transpiler maps the BCL TryParse entry points (Int32.TryParse,
// Int64.TryParse, Double.TryParse, Boolean.TryParse, Byte.TryParse and the
// Convert.ToInt32(string, fromBase) style parse) to these runtime helpers:
//
//   icall_try_parse_<type>
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour. These icalls implement the *TryParse* contract:
//   * SUCCESS: the out parameter is written and the return value is 1.
//   * FAILURE (null text, empty, malformed, out of range): the return value
//     is 0 and the out parameter is LEFT UNTOUCHED -- .NET TryParse leaves
//     the out variable at its previous value on failure.
//   * THESE FUNCTIONS NEVER THROW. There is no try/catch around the Parse
//     icalls: the parsing is implemented from scratch, error-path first, so
//     the no-throw property is structural, not defensive.
//
// OUT PARAMETER CONVENTION (all functions):
//   * 'result' must be non-null; a null out pointer yields 0 without
//     writing anything (defensive: generated code always passes a real
//     slot, but the contract is total).
//   * 's' arrives as a generic Il2CppObject* (the transpiler's object
//     slot type). A null object is a failure -> 0. Non-null objects are
//     always System.String instances (C# overload resolution guarantees
//     it); the payload is read through the string layout directly.
//
// PARSING RULES (identical to the throwing Parse family in il2cpp_format.h,
// so TryParse and Parse agree on what is valid):
//   * int32/int64/byte (base 10): leading/trailing ASCII whitespace
//     (0x09-0x0D, 0x20), optional '+'/'-' sign, decimal digits; leading
//     zeros are allowed; out-of-range magnitude -> failure.
//   * double: leading/trailing ASCII whitespace, then either the exact
//     special words "NaN"/U+221E/-U+221E (U+221E -- the zh-CN NumberFormatInfo
//     infinity symbols), or a decimal literal with optional sign, fraction
//     and e/E exponent. The ASCII words "Infinity"/"-Infinity" are
//     REJECTED: net48 reads the special words from the culture's
//     NumberFormatInfo (zh-CN's PositiveInfinitySymbol is U+221E, so net48
//     TryParse/Parse throw FormatException on the ASCII "Infinity" text).
//     Magnitude beyond double.MaxValue -> failure (net48 TryParse returns
//     false; Parse throws OverflowException). Underflow -> 0.0 success.
//   * bool: leading/trailing ASCII whitespace, then the exact words
//     "true"/"false" in ANY case (OrdinalIgnoreCase); anything else fails.
//   * fromBase (2/8/10/16): digits valid in that base ('a'-'f'/'A'-'F' for
//     16), no "0b" prefixes (and "0x" only for radix 16, see below).
//     EMPIRICALLY (net48 reference run of
//     Convert.ToInt32(string, fromBase), which passes ParseNumbers.
//     StringToInt IsTight|NoSpace):
//       - NO whitespace is accepted for ANY radix (" 42 " fails).
//       - A leading '+' is accepted for EVERY radix (skipped, no sign
//         effect); a leading '-' is accepted ONLY for radix 10 ("-42" ok;
//         radix 2/8/16 reject '-' with ArgumentException, which the
//         TryParse contract turns into failure -> 0).
//       - radix 16 strips a "0x"/"0X" prefix ("0x10",16 -> 16); no other
//         radix accepts the prefix, and "0x" with no digits fails.
//       - radix 10 range-checks against int32 ("2147483648" fails).
//       - radix 2/8/16 accumulate in uint32 with WRAPAROUND up to a hard
//         32-bit cap: "80000000",16 -> INT32_MIN, "FFFFFFFF",16 -> -1
//         (both wrap within uint32), while "100000000",16 and 11 octal
//         digits -> OverflowException on net48 -> TryParse failure.
//     RADIX CONTRACT (measured against a real net48 CLR): the legal fromBase
//     set is EXACTLY {2, 8, 10, 16}. Every other base -- 1, 3, 17, 36, 37,
//     negative, ... -- throws ArgumentException, and the radix is checked
//     BEFORE the text (Convert.ToInt32(null, 3) throws, while
//     Convert.ToInt32(null, 2/8/10/16) returns 0). This is NOT a "2..36
//     range" and an invalid base NEVER returns 0.
//
//   Measured group-separator contract (see il2cpp_parse_styles.h): the
//   double text grammar takes its group and decimal separators from the
//   provider's NumberFormatInfo and allows thousands by default, so on this
//   host double.Parse("1,000") == 1000 -- the old "no group separators"
//   claim was false. The INTEGER parsers in this file intentionally do NOT
//   take group separators: net48 int.Parse rejects them in every culture
//   (measured: invariant, de-DE, fr-FR all FormatException).
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// Int32.TryParse: 1 + value on success; 0 on failure (no throw).
int32_t icall_try_parse_int32(Il2CppObject* s, int32_t* result);

// Int64.TryParse: 1 + value on success; 0 on failure (no throw).
int32_t icall_try_parse_int64(Il2CppObject* s, int64_t* result);

// Double.TryParse: 1 + value on success (incl. "NaN" and the zh-CN U+221E
// infinity symbols; ASCII "Infinity"/"-Infinity" are rejected per net48
// culture semantics); 0 on failure (no throw).
int32_t icall_try_parse_double(Il2CppObject* s, double* result);

// Boolean.TryParse: 1 + 1/0 ("true"/"false", case-insensitive) on success;
// 0 on failure (no throw).
int32_t icall_try_parse_bool(Il2CppObject* s, int32_t* result);

// Byte.TryParse: 1 + value in [0, 255] on success; 0 on failure (no throw).
int32_t icall_try_parse_byte(Il2CppObject* s, int32_t* result);

// Convert.ToInt32(string, fromBase) style parse for base 2/8/10/16: 1 +
// value on success; 0 on failure. An invalid fromBase THROWS
// ArgumentException (see the header; the radix is checked before the text).
int32_t icall_try_parse_int32_base(Il2CppObject* s, int32_t fromBase, int32_t* result);

}} // namespace il2cpp::runtime

