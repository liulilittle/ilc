//------------------------------------------------------------------------------
// conv.cpp -- numeric conversion (conv family) runtime primitives
// (see il2cpp_conv.h for the API contract and the net48 empirical baseline).
//
// Implementation notes:
//   * The int32/int64/uint paths are plain (explicit) casts. The C++17
//     standard defines signed -> unsigned (modulo) and only leaves unsigned
//     -> signed implementation-defined for values above the signed maximum;
//     every targeted platform (MSVC/GCC/clang, x86-64/AArch64) performs the
//     two's-complement wrap the .NET conv.* instructions specify.
//   * The double paths are NOT raw casts: C++ floating -> integer conversion
//     is undefined behaviour out of range. The explicit range guards (with
//     the net48 "integer indefinite" result) make the final cast strictly
//     defined and reproduce the net48 runtime conversion on every target
//     (including AArch64, whose saturating FCVTZS would differ from x86).
//   * The checked variants reuse il2cpp_throw_overflow_exception
//     (exceptionext.cpp); the net48 OverflowException message is the
//     standard default text.
//------------------------------------------------------------------------------

#include "il2cpp_conv.h"
#include "il2cpp_exception_ext.h"

#include <cmath>
#include <cstdint>

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// int32 -> int64: sign extension (conv.i8 / conv.ovf.i8 -- always exact).
//------------------------------------------------------------------------------
int64_t icall_conv_i8_i4(int32_t value)
{
    return static_cast<int64_t>(value);
}

int64_t icall_conv_ovf_i8_i4(int32_t value)
{
    return static_cast<int64_t>(value);
}

//------------------------------------------------------------------------------
// int64 -> int32, unchecked (conv.i4): the low 32 bits interpreted as a
// two's-complement int32. net48 oracle: (int)long.MaxValue == -1,
// (int)long.MinValue == 0, (int)0x123456789ABCDEF0 == -1698898192.
//------------------------------------------------------------------------------
int32_t icall_conv_i4_i8(int64_t value)
{
    // The intermediate unsigned cast makes the wrap defined for every input
    // (signed -> unsigned is modulo); the final unsigned -> signed cast is
    // the two's-complement wrap on every targeted platform.
    return static_cast<int32_t>(static_cast<uint32_t>(static_cast<uint64_t>(value)));
}

//------------------------------------------------------------------------------
// double -> int64, unchecked (conv.i8): truncation toward zero; NaN,
// +/-Infinity and |value| >= 2^63 -> int64.MinValue. Empirically verified on
// net48 (the RUNTIME path; the C# compiler's constant folding differs and
// yields 0 -- see the header's divergence note).
//
//   - 2^63 = 9223372036854775808.0 is exactly representable; every double
//     >= 2^63 is out of the int64 range.
//   - -2^63 exactly IS in range (int64.MinValue) and converts to it.
//   - doubles below -2^63 are out of range.
// The guards leave a value in [-2^63, 2^63) whose truncation fits int64,
// so the final cast is strictly defined.
//------------------------------------------------------------------------------
int64_t icall_conv_i8_d(double value)
{
    if (std::isnan(value))
    {
        return INT64_MIN;
    }
    if (value >= 9223372036854775808.0)
    {
        return INT64_MIN;
    }
    if (value < -9223372036854775808.0)
    {
        return INT64_MIN;
    }
    return static_cast<int64_t>(value);
}

//------------------------------------------------------------------------------
// double -> int32, unchecked (conv.i4): same contract as the int64 variant
// with the 2^31 boundaries; NaN, +/-Infinity and |value| >= 2^31 ->
// int32.MinValue (net48 runtime, empirically verified).
//------------------------------------------------------------------------------
int32_t icall_conv_i4_d(double value)
{
    if (std::isnan(value))
    {
        return INT32_MIN;
    }
    if (value >= 2147483648.0)
    {
        return INT32_MIN;
    }
    if (value < -2147483648.0)
    {
        return INT32_MIN;
    }
    return static_cast<int32_t>(value);
}

//------------------------------------------------------------------------------
// int64 -> double (conv.r8): exact below 2^53, otherwise correctly rounded
// (round-to-nearest-even -- the hardware conversion on every targeted
// compiler, matching the net48 JIT's cvtsi2sd). Always in range.
//------------------------------------------------------------------------------
double icall_conv_d_i8(int64_t value)
{
    return static_cast<double>(value);
}

//------------------------------------------------------------------------------
// int32 -> double (conv.r8): exact (int32 fits the 53-bit mantissa).
//------------------------------------------------------------------------------
double icall_conv_d_i4(int32_t value)
{
    return static_cast<double>(value);
}

//------------------------------------------------------------------------------
// Signed <-> unsigned bit patterns (conv.u4 / conv.i4 / conv.u8 / conv.i8):
// the same bits, re-interpreted. Signed -> unsigned is defined (modulo);
// unsigned -> signed wraps on every targeted platform.
//------------------------------------------------------------------------------
uint32_t icall_conv_u4_i4(int32_t value)
{
    return static_cast<uint32_t>(value);
}

int32_t icall_conv_i4_u4(uint32_t value)
{
    return static_cast<int32_t>(value);
}

uint64_t icall_conv_u8_i8(int64_t value)
{
    return static_cast<uint64_t>(value);
}

//------------------------------------------------------------------------------
// int32 -> uint64 (conv.u8): ZERO extension. The int32 slot holds the uint32
// bit pattern (0xFFFFFFFF as -1); .NET's conv.u8 reinterprets it as uint32
// and widens UNSIGNED (4294967295), NOT sign-extended (-1). A bare C++ cast
// would sign-extend -- the two-step static_cast makes the widening explicit.
//------------------------------------------------------------------------------
uint64_t icall_conv_u8_i4(int32_t value)
{
    return static_cast<uint64_t>(static_cast<uint32_t>(value));
}

int64_t icall_conv_i8_u8(uint64_t value)
{
    return static_cast<int64_t>(value);
}

//------------------------------------------------------------------------------
// int64 -> uint32 / uint64 -> uint32 / uint64 -> int32, unchecked (conv.u4 /
// conv.i4): the low 32 bits, interpreted per the result type. .NET's conv.u4
// from a 64-bit value takes the low 32 bits; conv.i4 reinterprets them as a
// two's-complement int32. net48 oracle: (uint)long.MaxValue == 4294967295,
// (uint)long.MinValue == 0, (int)ulong.MaxValue == -1.
//------------------------------------------------------------------------------
uint32_t icall_conv_u4_i8(int64_t value)
{
    return static_cast<uint32_t>(static_cast<uint64_t>(value));
}

uint32_t icall_conv_u4_u8(uint64_t value)
{
    return static_cast<uint32_t>(value);
}

int32_t icall_conv_i4_u8(uint64_t value)
{
    return static_cast<int32_t>(static_cast<uint32_t>(value));
}

//------------------------------------------------------------------------------
// uint32 -> int64 / uint64 (conv.i8 / conv.u8 from a u4): ZERO extension.
// The uint32 slot holds the unsigned bit pattern; widening must stay unsigned:
// (long)uint.MaxValue == 4294967295, NOT -1 (a bare C++ cast from a negative
// int32 would sign-extend). net48 oracle: (long)uint.MaxValue == 4294967295,
// (ulong)uint.MaxValue == 4294967295.
//------------------------------------------------------------------------------
int64_t icall_conv_i8_u4(uint32_t value)
{
    return static_cast<int64_t>(value);
}

uint64_t icall_conv_u8_u4(uint32_t value)
{
    return static_cast<uint64_t>(value);
}

//------------------------------------------------------------------------------
// double -> uint64, unchecked (conv.u8): the x64 JIT's cvttsd2si sequence,
// EMPIRICALLY VERIFIED on net48:
//   - NaN and value >= 2^64 (incl +Inf)          -> 0
//   - value < -2^63 (incl -Inf)                  -> 2^63 (0x8000000000000000,
//     the cvttsd2si "integer indefinite" as an unsigned pattern)
//   - value in [-2^63, 0)                        -> (uint64)(int64)trunc
//     (two's-complement wrap: -1.0 -> 18446744073709551615)
//   - value in [0, 2^64)                         -> (uint64)trunc
// (ulong)(double)2^64 == 0 and (ulong)(double)-2^64 == 2^63 -- the
// positive/negative overflow sides differ. The guards keep every final cast
// strictly defined C++ (no UB on any target).
//------------------------------------------------------------------------------
uint64_t icall_conv_u8_d(double value)
{
    if (std::isnan(value) || value >= 18446744073709551616.0)
    {
        return 0;
    }
    if (value < -9223372036854775808.0)
    {
        return 0x8000000000000000ULL;
    }
    if (value < 0.0)
    {
        return static_cast<uint64_t>(static_cast<int64_t>(value));
    }
    return static_cast<uint64_t>(value);
}

//------------------------------------------------------------------------------
// double -> uint32, unchecked (conv.u4): the LOW 32 BITS of the int64
// conversion (x64 cvttsd2si then eax), so it is exactly the low-32 of
// icall_conv_i8_d's result. net48 oracle: (uint)2^32 == 0, (uint)-1.0 ==
// 4294967295, (uint)-2^31-1 == 2147483647, (uint)NaN == 0.
//------------------------------------------------------------------------------
uint32_t icall_conv_u4_d(double value)
{
    return static_cast<uint32_t>(static_cast<uint64_t>(icall_conv_i8_d(value)));
}

//------------------------------------------------------------------------------
// uint32/uint64 -> double (conv.r8): uint32 is exact (fits the 53-bit
// mantissa); uint64 is correctly rounded (round-to-nearest-even -- the
// hardware conversion on every targeted compiler). net48 oracle:
// (double)uint.MaxValue == 4294967295, (double)ulong.MaxValue == 2^64.
//------------------------------------------------------------------------------
double icall_conv_d_u4(uint32_t value)
{
    return static_cast<double>(value);
}

double icall_conv_d_u8(uint64_t value)
{
    return static_cast<double>(value);
}

//------------------------------------------------------------------------------
// double -> double identity (conv.r8): the float source rides a double slot
// already widened to the exact float value, so Convert.ToDouble(float) is a
// bit-exact pass-through (no d->d helper existed; the float arm of the
// Convert.ToDouble switch was LOUD before batch 7).
//------------------------------------------------------------------------------
double icall_conv_d_d(double value)
{
    return value;
}

//------------------------------------------------------------------------------
// int/uint -> float and double -> float (conv.r4): round-to-nearest-even to
// single precision. The net48 JIT uses the hardware cvtsi2ss / cvttsd2ss
// (RNE), which every targeted compiler reproduces with a plain static_cast.
// net48 oracle: (float)16777217 == 16777216 (2^24 + 1 rounds to 2^24),
// (float)int.MaxValue == 2.14748365E+09, (float)0.1 carries the
// single-precision bits.
//------------------------------------------------------------------------------
float icall_conv_r4_i4(int32_t value)
{
    return static_cast<float>(value);
}

float icall_conv_r4_u4(uint32_t value)
{
    return static_cast<float>(value);
}

float icall_conv_r4_i8(int64_t value)
{
    return static_cast<float>(value);
}

float icall_conv_r4_u8(uint64_t value)
{
    return static_cast<float>(value);
}

float icall_conv_r4_d(double value)
{
    return static_cast<float>(value);
}

//------------------------------------------------------------------------------
// int64 -> int32, checked (conv.ovf.i4): outside [int32.MinValue,
// int32.MaxValue] throws OverflowException (net48: checked((int)long.MaxValue)
// throws; the in-range boundaries convert).
//------------------------------------------------------------------------------
int32_t icall_conv_ovf_i4_i8(int64_t value)
{
    if (value < INT32_MIN || value > INT32_MAX)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

//------------------------------------------------------------------------------
// Checked integer conversions (conv.ovf.*): the destination range test on the
// integer value; out-of-range throws OverflowException (net48 empirical:
// checked((uint)4294967295L) == 4294967295, checked((uint)4294967296L) and
// checked((uint)-1L) throw; checked((ulong)-1L) throws; checked((ulong)0L)
// passes). The range checks keep the final cast in-range, so it is defined.
//------------------------------------------------------------------------------
uint32_t icall_conv_ovf_u4_i8(int64_t value)
{
    if (value < 0 || value > 4294967295LL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint32_t>(value);
}

uint32_t icall_conv_ovf_u4_u8(uint64_t value)
{
    if (value > 4294967295ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint32_t>(value);
}

int32_t icall_conv_ovf_i4_u8(uint64_t value)
{
    if (value > 2147483647ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int64_t icall_conv_ovf_i8_u8(uint64_t value)
{
    if (value > 9223372036854775807ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int64_t>(value);
}

uint64_t icall_conv_ovf_u8_i8(int64_t value)
{
    if (value < 0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint64_t>(value);
}

uint64_t icall_conv_ovf_u8_i4(int32_t value)
{
    if (value < 0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint64_t>(value);
}

uint32_t icall_conv_ovf_u4_i4(int32_t value)
{
    if (value < 0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint32_t>(value);
}

//------------------------------------------------------------------------------
// double -> int32/int64/uint32/uint64, CHECKED (conv.ovf.*): the check is on
// the TRUNCATED result fitting the destination (net48 empirical -- verified
// this session with the fractional-boundary probe):
//   * checked((int)-2147483648.5) == -2147483648  (truncation fits, NO throw)
//   * checked((int)-2147483649.5) throws           (truncation < int32.Min)
//   * checked((uint)-0.5) == 0                     (truncation 0, NO throw)
//   * checked((uint)-1.0) throws                   (truncation < 0)
//   * checked((uint)4294967295.9) == 4294967295, checked((uint)2^32) throws
//   * checked((ulong)(2^64-2048)) passes, checked((ulong)2^64) throws
//   * NaN and +-Infinity always throw.
// The guards leave the truncation in the destination range, so the final
// casts are strictly defined C++ on every target.
//------------------------------------------------------------------------------
int32_t icall_conv_ovf_i4_d(double value)
{
    if (std::isnan(value) || value >= 2147483648.0 || value <= -2147483649.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int64_t icall_conv_ovf_i8_d(double value)
{
    if (std::isnan(value) || value >= 9223372036854775808.0 || value < -9223372036854775808.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int64_t>(value);
}

uint32_t icall_conv_ovf_u4_d(double value)
{
    if (std::isnan(value) || value >= 4294967296.0 || value <= -1.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint32_t>(value);
}

uint64_t icall_conv_ovf_u8_d(double value)
{
    if (std::isnan(value) || value >= 18446744073709551616.0 || value <= -1.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint64_t>(value);
}

//------------------------------------------------------------------------------
// int32 -> bool (conv.i1 / conv.u1): any non-zero bit pattern -> 1.
//------------------------------------------------------------------------------
int32_t icall_conv_bool_i4(int32_t value)
{
    return (value != 0) ? 1 : 0;
}

//------------------------------------------------------------------------------
// conv.ovf narrow widths (batch 152e): checked int64/double -> byte/sbyte/
// ushort/short. The 8/16-bit values ride int32 slots (zero/sign-extended);
// outside the target range throws OverflowException. Double inputs truncate
// (like the wider ovf_d forms) BEFORE the range check.
//------------------------------------------------------------------------------
int32_t icall_conv_ovf_u1_i8(int64_t value)
{
    if (value < 0 || value > 255)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_i1_i8(int64_t value)
{
    if (value < -128 || value > 127)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_u2_i8(int64_t value)
{
    if (value < 0 || value > 65535)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_i2_i8(int64_t value)
{
    if (value < -32768 || value > 32767)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

//------------------------------------------------------------------------------
// conv.ovf UN-SUFFIXED narrow widths (batch 154b): the unsigned-input
// variants (conv.ovf.u1.un etc. -- C# checked casts from uint/ulong). The
// caller passes the int32/int64 slot bit pattern; sign extension to uint64
// makes a negative bit pattern (>= 2^31/2^63) a huge unsigned value that
// fails the range test -- exactly the checked-cast throw the C# semantics
// require. In-range unsigned values pass.
//------------------------------------------------------------------------------
int32_t icall_conv_ovf_u1_u8(uint64_t value)
{
    if (value > 255ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_i1_u8(uint64_t value)
{
    if (value > 127ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_u2_u8(uint64_t value)
{
    if (value > 65535ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_i2_u8(uint64_t value)
{
    if (value > 32767ULL)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

int32_t icall_conv_ovf_u1_d(double value)
{
    if (std::isnan(value) || value >= 256.0 || value < 0.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(static_cast<uint8_t>(value));
}

int32_t icall_conv_ovf_i1_d(double value)
{
    if (std::isnan(value) || value >= 128.0 || value < -128.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(static_cast<int8_t>(value));
}

int32_t icall_conv_ovf_u2_d(double value)
{
    if (std::isnan(value) || value >= 65536.0 || value < 0.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(static_cast<uint16_t>(value));
}

int32_t icall_conv_ovf_i2_d(double value)
{
    if (std::isnan(value) || value >= 32768.0 || value < -32768.0)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(static_cast<int16_t>(value));
}

}} // namespace il2cpp::runtime

namespace il2cpp {

//------------------------------------------------------------------------------
// D-I64D-1: il2cpp_unchecked_mul_64 -- int64 UNCHECKED multiplication (IL mul
// on i8 operands, the C# unchecked context) with net48 WRAP semantics. A raw
// C++ `a * b` on two int64_t operands is signed-overflow UB when the product
// exceeds int64 range -- clang -O2 folds INT64_MIN * 2 differently than the
// two's-complement wrap (int64deep D-I64D-1, the removed o4_min_mul2 probe).
// The UNSIGNED intermediate (C++17 unsigned arithmetic is defined modulo 2^64)
// defines the product for every input; the unsigned -> signed conversion is
// implementation-defined for out-of-range values but performs the
// two's-complement wrap on every targeted platform (MSVC/GCC/clang -- the
// conv-family convention, see the file header). Mirrors the checked antonym
// il2cpp_checked_mul_64 (exception.cpp), which throws OverflowException
// instead of wrapping.
//------------------------------------------------------------------------------
int64_t il2cpp_unchecked_mul_64(int64_t a, int64_t b)
{
    return static_cast<int64_t>(static_cast<uint64_t>(a) * static_cast<uint64_t>(b));
}

} // namespace il2cpp
