#pragma once
//------------------------------------------------------------------------------
// il2cpp_format_ext.h -- ToString(format) custom-format icalls (formatext.cpp).
//
// The transpiler maps the BCL ToString(string format) overloads
// (System.Int32.ToString(string), System.Int64.ToString(string),
// System.Double.ToString(string)) to these runtime helpers -- the same icall
// pattern as il2cpp_format.h:
//
//   icall_format_int32_custom(int32_t value, Il2CppObject* format, Il2CppObject* provider = nullptr)
//   icall_format_int64_custom(int64_t value, Il2CppObject* format, Il2CppObject* provider = nullptr)
//   icall_format_double_custom(double value, Il2CppObject* format, Il2CppObject* provider)
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
//                  P<prec>  percent: exact x100 digit shift, grouped,
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
//                          placeholders, or /1000 scaling per trailing comma),
//                          '%' (x100) and U+2030 (per-mille) (x1000) at their pattern
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
//     default (zh-CN): currency symbol U+00A5 (yen sign) with negative pattern
//     "U+00A5-n" ("U+00A5-12,345.00"), percent "n%"/"-n%" (no space). Under other
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
//     renders U+221E/-U+221E (U+221E) in F/N/E too, while InvariantCulture
//     renders "Infinity"/"-Infinity" (both verified against net48); the
//     runtime fixes the ASCII spelling.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "il2cpp_exception.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// int32/uint32 -> formatted string per the subset above. 'format' == null or
// "" selects "G". Unknown format -> FormatException.
// cultureprobe (saudit4): 'provider' mirrors the double formatter contract --
// nullptr / non-empty name -> the zh-CN host layout (byte-identical to the
// pre-provider behavior); the empty-name (invariant) provider -> invariant
// C ("(U+00A4n)-negatives", U+00A4) / P ("n %") layouts. Defaulted so every
// pre-existing 2-arg call site stays ABI- and byte-compatible.
Il2CppObject* icall_format_int32_custom(int32_t value, Il2CppObject* format,
                                        Il2CppObject* provider = nullptr);

// int64 -> formatted string per the subset above (hex is 16-digit
// two's-complement for negatives). Provider semantics as int32 (above).
Il2CppObject* icall_format_int64_custom(int64_t value, Il2CppObject* format,
                                        Il2CppObject* provider = nullptr);

// double -> formatted string per the subset above (F/N/E/G/P + simplified
// '0'/'.' custom formats). Unknown format -> FormatException.
// cultureprobe: 'provider' is the optional IFormatProvider (the 2-arg
// ToString(string, IFormatProvider) overload passes it through; the 1-arg
// form and the composite engine pass nullptr). nullptr / non-empty name ->
// the zh-CN host layout (byte-identical to the pre-provider behavior); an
// empty-name (invariant) provider -> the invariant P ("n %") / C (U+00A4,
// "(U+00A4n)") layouts.
Il2CppObject* icall_format_double_custom(double value, Il2CppObject* format,
                                         Il2CppObject* provider);

//------------------------------------------------------------------------------
// floatfix/singlefmt: icall_format_float_custom -- Single.ToString(format[,
// provider]). "R" is the shortest FLOAT round-trip (net48 two-tier
// G7->G9; the double engine printed full double precision). EVERY OTHER
// format runs the FLOAT-width digit base -- net48's native Number.FormatSingle
// converts the widened double at a 7-significant-digit base (9 for E with
// precision > 6 and G with precision > 7), not the double engine's 15-digit
// base (oracle probe-singlefmt: 0.1f "F10" -> "0.1000000000"; 0.1f "G10" ->
// "0.100000001"; 16777216f "F" -> "16777220.00"). Specials (NaN / +-Infinity)
// return the PROVIDER spelling before any format work (InvariantCulture
// "Infinity"/"-Infinity"; the default zh-CN culture U+221E). The boxed
// composite items ({0:F10} of a float) route the same engine. The value rides
// the double carrier (the exact float widening; the target is (float)value).
//------------------------------------------------------------------------------
Il2CppObject* icall_format_float_custom(double value, Il2CppObject* format,
                                        Il2CppObject* provider);

//------------------------------------------------------------------------------
// tiebreak2: format_double_default -- the net48 DEFAULT double text (the
// "G15" base: 15 significant digits, .NET fixed/scientific layout switch,
// trailing zeros stripped) written as NUL-terminated UTF-8 into 'out' (at
// least 32 bytes; 'capacity' bounds the copy). This is the single source of
// the double G15 text shared by format.cpp's icall_double_to_string and
// console.cpp's console_format_double.
//
// The digit base is formatext.cpp's GetTieBaseDigits, so an EXACT half-way tie
// rounds AWAY FROM ZERO like net48 (oracle: 1234567890123445.0 ->
// "1.23456789012345E+15"; the CRT's "%.15g" half-to-even gave
// "1.23456789012344E+15"). Non-tie values are byte-identical to the previous
// snprintf("%.15g") text.
//
// FINITE values only: the caller keeps its own NaN / +-Infinity / zero
// spelling (format.cpp and console.cpp differ there by design -- the console
// emits the culture U+221E bytes directly).
//------------------------------------------------------------------------------
void format_double_default(double value, char* out, int capacity);

//------------------------------------------------------------------------------
// icall_format_narrow_custom -- ToString(format[, provider]) for the NARROW
// integer types (System.Byte / System.SByte / System.Int16 / System.UInt16).
//
// net48 oracle (fmtprobe, zh-CN + invariant): every standard/custom specifier
// behaves exactly like the int32 engine ON THE SIGN/ZERO-EXTENDED VALUE --
// EXCEPT the hex specifiers X/x, which format the value as the TYPE's own
// unsigned bit pattern with MINIMAL digits (precision = minimum width):
//
//   value            "X"     "X2"    "X4"     (int32 engine would give)
//   byte   255       FF      FF      00FF     FF
//   byte     0       0       00      0000     0
//   sbyte   -1       FF      FF      00FF     FFFFFFFF   <- masked
//   sbyte -128       80      80      0080     FFFFFF80   <- masked
//   short   -1       FFFF    FFFF    FFFF     FFFFFFFF   <- masked
//   short -32768     8000    8000    8000     FFFF8000   <- masked
//   ushort 65535     FFFF    FFFF    FFFF     FFFF
//
// 'value' is the caller's already sign/zero-extended narrow value (the
// transpiler's deref:i1/deref:i2/deref:u2/unbox:i casts), so D/N/C/E/F/G/P and
// the custom engine need no change; 'hexMask' selects the type width used ONLY
// for the hex bit pattern (0xFF = 8-bit, 0xFFFF = 16-bit).
//
// Provider semantics as the int32 formatter (nullptr / non-empty name -> the
// zh-CN host layout, the invariant name -> invariant C/P).
//------------------------------------------------------------------------------
Il2CppObject* icall_format_narrow_custom(int32_t value, Il2CppObject* format,
                                         Il2CppObject* provider, int32_t hexMask);

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
//   provider  : the IFormatProvider of the enclosing String.Format /
//               AppendFormat call (saudit4; nullptr for the provider-less
//               overloads -> zh-CN layout, invariant name -> invariant).
//
// Dispatch (net48 composite semantics): boxed Int32/Int64/Double/Single route
// through the icall_format_*_custom engine (so "f2"/"x"/"X"/... work); boxed
// Decimal through the decimal custom icall; a STRING arg returns its payload
// unchanged (the specifier is ignored -- System.String is not IFormattable);
// any other object prints its plain ToString() text (specifier ignored). The
// result is then space-padded to the alignment width. Returns a fresh string.
//------------------------------------------------------------------------------
Il2CppObject* icall_format_composite_item(Il2CppObject* boxed, Il2CppObject* format, int32_t alignment,
                                          Il2CppObject* provider = nullptr);

//------------------------------------------------------------------------------
// icall_format_iface_tostring -- IFormattable.ToString(format, provider) on a
// BOXED primitive receiver (`((IFormattable)sb).ToString("X", inv)`).
//
// net48 (probe-verified): IFormattable::ToString(format, provider) formats
// EXACTLY like ToString(format, provider) -- including the narrow-integer hex
// width rule (sbyte -1 "X" -> "FF", short -1 "X" -> "FFFB", ushort.MaxValue
// "X" -> "FFFF"), uint/ulong full-range hex, float/double F/N/E, decimal
// F/G/N and "Q" -> FormatException. So this is the composite formatter with
// alignment 0 (the same boxed-klass dispatch).
//
// ERROR CONTRACT (net48): a NULL receiver throws NullReferenceException
// (icall_format_composite_item would render the empty string -- Convert.ToString
// semantics -- so the null check lives here).
//------------------------------------------------------------------------------
Il2CppObject* icall_format_iface_tostring(Il2CppObject* boxed, Il2CppObject* format,
                                          Il2CppObject* provider = nullptr);

}} // namespace il2cpp::runtime
