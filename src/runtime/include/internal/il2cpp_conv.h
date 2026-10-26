#pragma once
//------------------------------------------------------------------------------
// il2cpp_conv.h -- numeric conversion (conv family) runtime primitives.
//
// The transpiler maps IL conv.* / conv.ovf.* instructions on the
// int32/int64/double/uint stack slots to these helpers, so the generated
// code never performs raw C++ casts (which are UB at the boundaries).
//
// Naming follows the existing icall convention:
//   icall_conv_<result>_<source>  with _i4 = int32, _i8 = int64,
//   _u4 = uint32, _u8 = uint64, _d = double, _r4 = float (single), _bool =
//   bool (as 0/1).
// The _ovf_ variants are the checked (conv.ovf.*) instructions: out-of-range
// results throw System.OverflowException instead of wrapping.
//
// FLOAT NOTE: in this runtime a float value lives in a double slot (the
// transpiler promotes every float to a double; see the boxing header). The
// _r4 conversions therefore take/return the float as its own float type and
// the generated code widens to the double slot on assignment (exact); the
// ROUND-TO-NEAREST-EVEN float rounding happens inside the _r4 helpers
// (conv.r4 semantics: (float)16777217 == 16777216, (float)0.1 != 0.1).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- EMPIRICALLY VERIFIED by
// running tests/cases/convcase (the net48 executable) before implementing:
//   * int32 -> int64        sign extension (always exact).
//   * int64 -> int32        unchecked conv.i4: the low 32 bits, interpreted
//                           as a two's-complement int32 (wrap).
//   * double -> int64       unchecked conv.i8: truncation toward zero;
//                           NaN, +/-Infinity and |value| >= 2^63 yield
//                           int64.MinValue (the x64 cvttsd2si "integer
//                           indefinite"; verified at runtime on net48).
//   * double -> int32       unchecked conv.i4: truncation toward zero;
//                           NaN, +/-Infinity and |value| >= 2^31 yield
//                           int32.MinValue (same indefinite semantics,
//                           runtime-verified on net48).
//   * int64 <-> double      exact below 2^53, otherwise correctly rounded
//                           (round-to-nearest-even -- the hardware
//                           conversion on every targeted compiler).
//   * int32 <-> double      exact (int32 fits the 53-bit mantissa).
//   * signed <-> unsigned   bit patterns (two's complement).
//   * int32 -> bool         any non-zero bit pattern -> 1.
//   * conv.ovf.i8 (int32)   never overflows (sign extension).
//   * conv.ovf.i4 (int64)   out-of-range -> System.OverflowException
//                           ("Arithmetic operation resulted in an overflow.").
//   * int64 -> uint32 / uint64 -> uint32 / uint64 -> int32 (unchecked):
//                           the LOW 32 BITS, interpreted per the result type
//                           (conv.u4 / conv.i4 wrapping; net48 oracle:
//                           (uint)long.MaxValue == 4294967295,
//                           (int)ulong.MaxValue == -1).
//   * uint32 -> int64 / uint64: ZERO extension (the uint32 bit pattern
//                           widens unsigned: (long)uint.MaxValue ==
//                           4294967295, NOT -1 -- conv.i8/conv.u8 of a u4).
//   * uint64 -> double      correctly rounded (round-to-nearest-even;
//                           (double)ulong.MaxValue == 2^64, net48 oracle).
//   * uint32 -> double      exact (uint32 fits the 53-bit mantissa).
//   * double -> uint64 (unchecked conv.u8): the x64 cvttsd2si + reinterpret
//                           sequence: NaN and >= 2^64 -> 0; values below
//                           -2^63 (incl -Inf) -> 2^63 (the "integer
//                           indefinite" 0x8000000000000000); [-2^63, 0) ->
//                           (uint64)(int64)trunc (wrap: -1.0 -> 2^64-1);
//                           [0, 2^64) -> (uint64)trunc. NET48-EMPIRICAL.
//   * double -> uint32 (unchecked conv.u4): the LOW 32 BITS of the int64
//                           conversion (x64 cvttsd2si then low 32):
//                           (uint)2^32 == 0, (uint)-1.0 == 4294967295,
//                           (uint)NaN == 0. NET48-EMPIRICAL.
//   * float conversions (conv.r4): round-to-nearest-even to single precision
//                           then widened (exact) for the double slot:
//                           (float)16777217 == 16777216, (float)0.1 has the
//                           single-precision bits (0.100000001490116...).
//   * CHECKED floating -> integer (conv.ovf.i4/.i8/.u4/.u8 from double):
//                           the check is on the TRUNCATED result fitting the
//                           destination (NOT the raw double range):
//                           checked((int)-2147483648.5) == -2147483648 (no
//                           throw), checked((int)-2147483649.5) throws;
//                           checked((uint)-0.5) == 0 (no throw),
//                           checked((uint)-1.0) throws. NET48-EMPIRICAL.
//   * CHECKED integer widening/narrowing (conv.ovf.* on integers): the
//                           destination range test on the integer value;
//                           signed -> unsigned throws on negatives.
//
// KNOWN DIVERGENCE (disclosed): the C# compiler's CONSTANT folding of an
// out-of-range double -> integer conversion yields 0 (probed on net48), while
// the RUNTIME conversion yields the integer indefinite (MinValue). These
// helpers implement the RUNTIME semantics -- the transpiled code performs the
// conversion at run time. The double -> int64/int32 guards also make the
// final cast strictly defined C++ (no UB on any target, including AArch64
// whose saturating FCVTZS would otherwise differ).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// conv.i8: int32 -> int64, sign extension (always exact).
int64_t icall_conv_i8_i4(int32_t value);

// conv.i4 (unchecked): int64 -> int32, low 32 bits as a two's-complement
// int32 (net48 wrap semantics).
int32_t icall_conv_i4_i8(int64_t value);

// conv.i8 (unchecked): double -> int64, truncation toward zero; NaN,
// +/-Infinity and |value| >= 2^63 -> int64.MinValue (net48 runtime).
int64_t icall_conv_i8_d(double value);

// conv.r8: int64 -> double (exact below 2^53, correctly rounded above).
double icall_conv_d_i8(int64_t value);

// conv.r8: int32 -> double (always exact).
double icall_conv_d_i4(int32_t value);

// conv.i4 (unchecked): double -> int32, truncation toward zero; NaN,
// +/-Infinity and |value| >= 2^31 -> int32.MinValue (net48 runtime).
int32_t icall_conv_i4_d(double value);

// conv.u4: int32 -> uint32, same bit pattern.
uint32_t icall_conv_u4_i4(int32_t value);

// conv.i4: uint32 -> int32, same bit pattern (two's complement).
int32_t icall_conv_i4_u4(uint32_t value);

// conv.u8: int64 -> uint64, same bit pattern.
uint64_t icall_conv_u8_i8(int64_t value);

// conv.u8: int32 -> uint64, ZERO extension (int32 -> uint32 -> uint64 --
// valdeep L26 root cause: (ulong)uintVal must widen the uint32 bit pattern
// UNSIGNED; the int32 slot holds the signed pattern, so a plain C++ cast
// would sign-extend 0xFFFFFFFF to -1 instead of 4294967295).
uint64_t icall_conv_u8_i4(int32_t value);

// conv.i8: uint64 -> int64, same bit pattern (two's complement).
int64_t icall_conv_i8_u8(uint64_t value);

// conv.ovf.i8: int32 -> int64, checked (never overflows).
int64_t icall_conv_ovf_i8_i4(int32_t value);

// conv.ovf.i4: int64 -> int32, checked; outside [int32.MinValue,
// int32.MaxValue] -> System.OverflowException.
int32_t icall_conv_ovf_i4_i8(int64_t value);

// conv.i1/conv.u1 to bool: any non-zero bit pattern -> 1, zero -> 0.
int32_t icall_conv_bool_i4(int32_t value);

// conv.u4 (unchecked): int64 -> uint32, low 32 bits (wrap).
uint32_t icall_conv_u4_i8(int64_t value);

// conv.u4 (unchecked): uint64 -> uint32, low 32 bits (wrap).
uint32_t icall_conv_u4_u8(uint64_t value);

// conv.i4 (unchecked): uint64 -> int32, low 32 bits as a two's-complement
// int32 ((int)ulong.MaxValue == -1).
int32_t icall_conv_i4_u8(uint64_t value);

// conv.i8: uint32 -> int64, ZERO extension ((long)uint.MaxValue == 4294967295).
int64_t icall_conv_i8_u4(uint32_t value);

// conv.u8: uint32 -> uint64, ZERO extension.
uint64_t icall_conv_u8_u4(uint32_t value);

// conv.u8 (unchecked): double -> uint64, x64 cvttsd2si semantics (see the
// baseline above): NaN / >= 2^64 -> 0; < -2^63 -> 2^63; else truncation.
uint64_t icall_conv_u8_d(double value);

// conv.u4 (unchecked): double -> uint32, the low 32 bits of the int64
// conversion ((uint)2^32 == 0, (uint)-1.0 == 4294967295, (uint)NaN == 0).
uint32_t icall_conv_u4_d(double value);

// conv.r8: uint32 -> double (always exact).
double icall_conv_d_u4(uint32_t value);

// conv.r8: uint64 -> double (correctly rounded, round-to-nearest-even).
double icall_conv_d_u8(uint64_t value);

// conv.r8: double -> double identity. Convert.ToDouble(float) -- the float
// source rides an already-widened double slot, so the conversion is a
// bit-exact pass-through (batch 7).
double icall_conv_d_d(double value);

// conv.r4: int32 -> float, round-to-nearest-even to single precision.
float icall_conv_r4_i4(int32_t value);

// conv.r4: uint32 -> float, RNE to single precision.
float icall_conv_r4_u4(uint32_t value);

// conv.r4: int64 -> float, RNE to single precision.
float icall_conv_r4_i8(int64_t value);

// conv.r4: uint64 -> float, RNE to single precision.
float icall_conv_r4_u8(uint64_t value);

// conv.r4: double -> float, RNE to single precision ((float)16777217 ==
// 16777216, (float)0.1 carries the single-precision bits).
float icall_conv_r4_d(double value);

// conv.ovf.u4: checked int64 -> uint32; outside [0, 2^32) throws
// OverflowException.
uint32_t icall_conv_ovf_u4_i8(int64_t value);

// conv.ovf.u4: checked uint64 -> uint32; outside [0, 2^32) throws.
uint32_t icall_conv_ovf_u4_u8(uint64_t value);

// conv.ovf.i4: checked uint64 -> int32; outside [0, 2^31) throws.
int32_t icall_conv_ovf_i4_u8(uint64_t value);

// conv.ovf.i8: checked uint64 -> int64; outside [0, 2^63) throws.
int64_t icall_conv_ovf_i8_u8(uint64_t value);

// conv.ovf.u8: checked int64 -> uint64; negative throws.
uint64_t icall_conv_ovf_u8_i8(int64_t value);

// conv.ovf.u8: checked int32 -> uint64; negative throws.
uint64_t icall_conv_ovf_u8_i4(int32_t value);

// conv.ovf.u4: checked int32 -> uint32; negative throws.
uint32_t icall_conv_ovf_u4_i4(int32_t value);

// conv.ovf.i4: checked double -> int32; NaN, +-Inf and a TRUNCATED result
// outside [int32.Min, int32.Max] throws (checked((int)-2147483648.5) ==
// -2147483648 does NOT throw; checked((int)-2147483649.5) throws).
int32_t icall_conv_ovf_i4_d(double value);

// conv.ovf.i8: checked double -> int64; NaN, +-Inf and a truncated result
// outside [int64.Min, int64.Max] throws.
int64_t icall_conv_ovf_i8_d(double value);

// conv.ovf.u4: checked double -> uint32; NaN, +-Inf, a truncated result
// outside [0, 2^32) throws (checked((uint)-0.5) == 0 does NOT throw;
// checked((uint)-1.0) throws).
uint32_t icall_conv_ovf_u4_d(double value);

// conv.ovf.u8: checked double -> uint64; NaN, +-Inf, a truncated result
// outside [0, 2^64) throws.
uint64_t icall_conv_ovf_u8_d(double value);

//------------------------------------------------------------------------------
// conv.ovf narrow widths (batch 152e): checked int64 -> byte/sbyte/ushort/
// short. The 8/16-bit values ride int32 slots (zero/sign-extended); outside
// the target range throws OverflowException. The double inputs truncate
// (like the wider ovf_d forms) BEFORE the range check.
//------------------------------------------------------------------------------
int32_t icall_conv_ovf_u1_i8(int64_t value);   // byte:      [0, 255]
int32_t icall_conv_ovf_i1_i8(int64_t value);   // sbyte:     [-128, 127]
int32_t icall_conv_ovf_u2_i8(int64_t value);   // ushort:    [0, 65535]
int32_t icall_conv_ovf_i2_i8(int64_t value);   // short:     [-32768, 32767]
int32_t icall_conv_ovf_u1_d(double value);
int32_t icall_conv_ovf_i1_d(double value);
int32_t icall_conv_ovf_u2_d(double value);
int32_t icall_conv_ovf_i2_d(double value);
// Batch 154b: the UN-suffixed (unsigned-input) narrow forms. The caller
// passes the int32/int64 slot bit pattern; sign extension to uint64 makes a
// negative bit pattern fail the range test (the checked-cast throw).
int32_t icall_conv_ovf_u1_u8(uint64_t value);
int32_t icall_conv_ovf_i1_u8(uint64_t value);
int32_t icall_conv_ovf_u2_u8(uint64_t value);
int32_t icall_conv_ovf_i2_u8(uint64_t value);

}} // namespace il2cpp::runtime

namespace il2cpp {

// D-I64D-1: int64 UNCHECKED multiplication with defined wrap semantics (IL mul
// on i8 operands, net48 wraps mod 2^64). The unsigned intermediate makes the
// product defined (C++17 unsigned arithmetic); the unsigned -> signed cast is
// the two's-complement wrap on every targeted platform (the conv family
// convention). Mirrors il2cpp_checked_mul_64 (exception.h) -- the checked
// antonym throws OverflowException instead of wrapping.
int64_t il2cpp_unchecked_mul_64(int64_t a, int64_t b);

} // namespace il2cpp
