#pragma once
//------------------------------------------------------------------------------
// decimal_format.h -- System.Decimal formatting + parsing (decimal domain T2).
//
// LAYOUT NOTE: T1's decimal.h (representation + arithmetic) owns the
// Il2CppDecimal struct -- the member names and 16-byte layout are the frozen
// contract (decimal-domain.md §1.2). This header includes decimal.h instead
// of carrying a duplicate definition.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline. The reference algorithm sources (dotnet/runtime Decimal.cs /
// Decimal.DecCalc.cs / Number.*, pulled at reference/netcore) are followed,
// with the net48 deviations called out below.
//
// LAYOUT (identical to .NET Decimal, little-endian; sizeof == 16):
//   flags   bit31 = sign (1 = negative); bit16-23 = scale (0..28); rest 0
//   hi32    96-bit mantissa, high 32 bits
//   lo64    96-bit mantissa, low 64 bits
//   value   = (sign ? -1 : +1) * mantissa / 10^scale
//
// FORMATTING (Number.FormatDecimal contract, net48):
//   * G default (null format, "" , "G"/"g", optional precision ignored):
//     ALWAYS fixed point -- net48 decimal never switches to scientific for G
//     (the netcore switch needs scale >= 29, unreachable for decimal) and
//     trailing zeros are PRESERVED: 1.50m -> "1.50", 0.100m -> "0.100".
//     Negative zero keeps its sign: -0.000m -> "-0.000".
//   * F<prec> fixed, default 2: exact <prec> fraction digits, rounding HALF
//     AWAY FROM ZERO (net48 rule; 2.5m "F0" -> "3", -2.5m -> "-3").
//   * N<prec> same as F plus 3-digit grouping of the integer part, default 2.
//   * E<prec> scientific "d.dddE+xxx" (exponent sign + at least 3 digits),
//     default 6; 12345.678m "E" -> "1.234568E+004". The exponent letter
//     keeps the SPECIFIER CASE ({0:e2} -> "1.23e+003") and a short mantissa
//     fraction is zero-padded to exactly 'prec' digits (2.5m "E2" ->
//     "2.50E+000", 0m "E2" -> "0.00E+000").
//   * P<prec> percent, default 2: value x 100, N-style grouped integer +
//     'prec' fraction digits + trailing '%' (zh-CN "n%" layout, no space):
//     1234.5678m "P" -> "123,456.78%", 1.5m "P2" -> "150.00%".
//   * C<prec> currency, default 2: same culture-fixing as il2cpp_format_ext.h
//     (the verification host's net48 default zh-CN): symbol U+00A5 '¥',
//     negative pattern "¥-n" ("¥-1,234.50").
//   * Format parsing is STRICT (formatext.cpp precedent): unknown letter
//     (incl. D/X for decimal), precision > 99, or any trailing junk ->
//     FormatException ("Format specifier was invalid.").
//
// PARSING (Number.ParseDecimal contract, net48 -- the NET48 variant: parse
// NEVER rounds; the netcore TryNumberToDecimal half-to-even path is a
// .NET Core 3.0+ deviation and is NOT followed):
//   * Optional leading +/-; optional single '.'; ASCII whitespace trimmed
//     around the text; ',' tolerated between digits (NumberStyles.Number
//     AllowThousands). No exponent (FormatException).
//   * OverflowException when: significant digit count > 29, the 96-bit
//     mantissa exceeds 2^96-1 (e.g. "79228162514264337593543950336"), or the
//     accumulated scale > 28 (e.g. "0.00000000000000000000000000001").
//     net48 oracle: Parse("79228162514264337593543950335.1") throws
//     OverflowException (30 significant digits).
//   * FormatException for malformed input (empty, junk, misplaced sign).
//   * Parse(null) -> ArgumentNullException; TryParse(null) -> false.
//   * Parse preserves the exact mantissa+scale: "1.50" parses to scale 2 and
//     ToString() -> "1.50"; "-0.00" keeps the negative zero.
//
// TRYPARSE CONTRACT (repo precedent, guid.h): on failure the result buffer is
// left UNTOUCHED (verify-then-mutate). This is a deliberate deviation from
// net48, which zeroes the out parameter on failure.
//------------------------------------------------------------------------------

#include "decimal.h"
#include "il2cpp_string.h"
#include "il2cpp_exception.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// ToString() / ToString(null): net48 G default layout (see header comment).
// 'value' must be non-null and valid (IsValid contract); returns a managed
// string. Never throws.
Il2CppString* icall_decimal_to_string(const Il2CppDecimal* value);

// ToString(string format): G/F/N/E/P/C subset per the header comment. null
// and "" select G. Unknown specifier / precision > 99 / trailing junk ->
// FormatException. 'format' may be null.
Il2CppString* icall_decimal_to_string_custom(const Il2CppDecimal* value,
                                             Il2CppString* format);

// Parse(string): net48 semantics per the header comment. On success writes
// the value into 'result'. Throws ArgumentNullException (null text),
// FormatException (malformed) or OverflowException (out of range). Throws
// leave 'result' untouched.
//
// RESULT-SLOT-FIRST (T3 contract, decimal.h:37-42): Parse produces a decimal,
// so -- like the arithmetic icalls -- the result slot comes FIRST,
// icall_decimal_parse(t, s). The emitter's AssignStmt "dec" case prepends the
// target slot (MethodMap.Decimal.cs:29, CodeGenerator.cs:1226).
void icall_decimal_parse(Il2CppDecimal* result, Il2CppString* text);

// TryParse(string, out decimal): never throws. Returns true and writes
// 'result' only on success; on any failure (null, malformed, overflow)
// returns false and leaves 'result' untouched (see header comment).
bool icall_decimal_try_parse(Il2CppString* text, Il2CppDecimal* result);

}} // namespace il2cpp::runtime
