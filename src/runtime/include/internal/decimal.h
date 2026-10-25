#pragma once
//------------------------------------------------------------------------------
// decimal.h -- System.Decimal runtime support (16-byte value type).
//
// Representation (net48 / Win32 DECIMAL layout, byte-for-byte):
//   struct Il2CppDecimal {
//       int32_t flags;   // bit31 = sign (1 = negative); bit16-23 = scale
//                        // (0..28); all other bits MUST be zero (IsValid,
//                        // Decimal.cs:260)
//       uint32_t hi32;   // high 32 bits of the 96-bit mantissa
//       uint64_t lo64;   // low 64 bits of the 96-bit mantissa
//   };
// Value = (sign ? -1 : +1) * mantissa / 10^scale, mantissa in [0, 2^96).
// This is the same layout oleaut DECIMAL uses; little-endian x86/x64 keeps
// the field order identical to the .NET struct. sizeof == 16.
//
// DecCalc view (Decimal.DecCalc.cs:43-57): explicit uint32 offsets
// uflags@0 / uhi@4 / ulo@8 / umid@12 -- the C++ port in decimal.cpp works
// against this view (uflags, uhi, ulo, umid, and lo64 = ulo|umid<<32).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline. ALGORITHM BASELINE: dotnet/runtime Decimal.cs + Decimal.DecCalc.cs
// (reference/netcore) -- iron rule 3: the arithmetic is a line-by-line C++
// port of DecCalc.DecAddSub / VarDecMul / VarDecDiv / VarDecMod / VarDecCmp /
// Equals / InternalRound / GetHashCode, no self-invented big-number
// algorithms.
//
// Exception contract (net48):
//   * result > 2^96-1 or scale > 28   -> OverflowException
//   * divide / remainder by zero      -> DivideByZeroException
//   * Round(decimals) outside 0..28   -> ArgumentOutOfRangeException
//   * Round(mode) outside ToEven..AwayFromZero -> ArgumentException (D-DR-1:
//     net48 rejects MidpointRounding 2/3/4 through Round)
//   * ToInt32/64/UInt32/64 truncation out of range   -> OverflowException
//
// icall surface: value types follow the Guid precedent (MethodMap.Guid.cs:
// 7-13) -- decimal arguments/receivers are RAW POINTERS to value slots
// (Il2CppDecimal*), because a 16-byte value does not fit the single-register
// return path. T3 RESULT-SLOT CONTRACT (MethodMap.Decimal.cs:23-35): every
// decimal-PRODUCING icall takes the result slot FIRST -- icall_decimal_add(t,
// a, b) -- the emitter's AssignStmt "dec" case prepends the target slot.
// Inputs are const (never mutated); the result may ALIAS an input (the
// implementation loads every input before writing the result slot).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// System.Decimal's raw 16-byte image (see header comment). static_assert
// guard in decimal.cpp keeps the layout pinned to 16 bytes.
struct Il2CppDecimal
{
    int32_t flags;   // bit31 sign; bit16-23 scale; rest zero
    uint32_t hi32;   // 96-bit mantissa: bits 64..95
    uint64_t lo64;   // 96-bit mantissa: bits 0..63
};

// Mask/shift constants (Decimal.cs:80-88).
constexpr int32_t kDecimalSignMask = static_cast<int32_t>(0x80000000);
constexpr int32_t kDecimalScaleMask = 0x00FF0000;
constexpr int32_t kDecimalScaleShift = 16;
constexpr int32_t kDecimalMaxScale = 28;

// True when 'flags' is a valid decimal flags word: no reserved bits set and
// scale <= 28 (Decimal.cs:260).
bool il2cpp_decimal_is_valid_flags(int32_t flags);

// --- T1: add/sub/mul/div/rem/compare/equals/negate -------------------------
// Result-slot-first shape (T3 contract): result receives a+b, a-b, ... The
// inputs are const; loads precede the store, so result may alias an input
// (the legacy d1-in-place call shape is `fn(&d1, &d1, &d2)`).
// Div/Rem throw DivideByZeroException when b == 0; any overflow throws
// OverflowException.
void icall_decimal_add(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);
void icall_decimal_sub(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);
void icall_decimal_mul(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);
void icall_decimal_div(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);
void icall_decimal_rem(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);

// FCall in-place family (net48 Decimal internals): the BCL op_/named
// wrappers call FCallAddSub/FCallMultiply/FCallDivide, which mutate d1
// in place (callers read back arg0). b: bit7 set = subtract, else add.
// The Overflowed twins share the core (overflow already throws).
void icall_decimal_fcall_addsub(Il2CppDecimal* d1, const Il2CppDecimal* d2, int32_t b);
void icall_decimal_fcall_multiply(Il2CppDecimal* d1, const Il2CppDecimal* d2);
void icall_decimal_fcall_divide(Il2CppDecimal* d1, const Il2CppDecimal* d2);

// DecCalc.VarDecCmp (Decimal.cs:408-411): -1 / 0 / 1.
int32_t icall_decimal_compare(const Il2CppDecimal* d1, const Il2CppDecimal* d2);

// DecCalc.Equals (Decimal.cs:1003-1007): true iff numerically equal
// (different scales compare equal, e.g. 1.00m == 1m).
bool icall_decimal_equals(const Il2CppDecimal* d1, const Il2CppDecimal* d2);

// Unary minus: result = -a (flags ^= SignMask, Decimal.cs:961).
void icall_decimal_negate(Il2CppDecimal* result, const Il2CppDecimal* a);

// p0conv2 (D1): decimal identity copy (Convert.ToDecimal(decimal) -- net48
// returns the value unchanged, scale preserved). Result-slot first.
void icall_decimal_copy(Il2CppDecimal* result, const Il2CppDecimal* a);

// Math.Abs(decimal) / Math.Max(decimal,decimal) / Math.Min(decimal,decimal) /
// Math.Sign(decimal) (LOUD P0-19). Abs clears the sign flag; Max/Min return
// the winning OPERAND unchanged (scale preserved -- net48's
// `val1 < val2 ? val2 : val1` / `val1 < val2 ? val1 : val2`: a numeric tie
// yields the FIRST operand for Max and the SECOND for Min, oracle-verified);
// Sign returns -1/0/+1 off the sign flag and mantissa zero (a signed zero
// still yields 0).
void icall_decimal_abs(Il2CppDecimal* result, const Il2CppDecimal* a);
void icall_decimal_max(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);
void icall_decimal_min(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b);
int32_t icall_decimal_sign(const Il2CppDecimal* a);

// --- T1: rounding -----------------------------------------------------------
// Result-slot-first shape (T3 contract): result receives the rounded value;
// d is const. 'mode' is the net48 decimal.Round MidpointRounding pair only:
// 0=ToEven, 1=AwayFromZero. decimals > 28 -> ArgumentOutOfRangeException;
// mode outside 0..1 -> ArgumentException (Decimal.cs:678-689; D-DR-1 fix --
// the netcore modes 2=ToZero/3=ToNegativeInfinity/4=ToPositiveInfinity are
// NOT reachable through Round, matching the net48 oracle). The netcore modes
// remain reachable only via Truncate/Floor/Ceiling, which call
// DecCalc.InternalRound directly (Decimal.cs:888-893).
void icall_decimal_round(Il2CppDecimal* result, const Il2CppDecimal* d, int32_t decimals, int32_t mode);
// Math.Round(decimal, MidpointRounding) -- the 2-arg C# shape has no digits
// slot, so it maps to this decimals=0 wrapper (same RoundCore validation:
// mode outside 0..1 -> ArgumentException).
void icall_decimal_round_mode(Il2CppDecimal* result, const Il2CppDecimal* d, int32_t mode);
void icall_decimal_truncate(Il2CppDecimal* result, const Il2CppDecimal* d);
void icall_decimal_floor(Il2CppDecimal* result, const Il2CppDecimal* d);
void icall_decimal_ceiling(Il2CppDecimal* result, const Il2CppDecimal* d);

// --- T1: construction -------------------------------------------------------
// Integer ctors (Decimal.cs:133-183): scale 0, sign from the value.
void icall_decimal_ctor_int32(Il2CppDecimal* out, int32_t value);
void icall_decimal_ctor_uint32(Il2CppDecimal* out, uint32_t value);
void icall_decimal_ctor_int64(Il2CppDecimal* out, int64_t value);
void icall_decimal_ctor_uint64(Il2CppDecimal* out, uint64_t value);

// 5-arg ctor (Decimal.cs:309-317) -- the IL literal shape
// newobj Decimal(int lo, int mid, int hi, bool isNegative, byte scale);
// scale > 28 -> ArgumentOutOfRangeException.
void icall_decimal_ctor_5(Il2CppDecimal* out, int32_t lo, int32_t mid,
                          int32_t hi, bool isNegative, uint8_t scale);

// --- T1: truncating conversions (Decimal.cs:763-871) ------------------------
// Truncate towards zero, then range check; out of range -> OverflowException.
int32_t icall_decimal_to_int32(const Il2CppDecimal* d);
int64_t icall_decimal_to_int64(const Il2CppDecimal* d);
uint32_t icall_decimal_to_uint32(const Il2CppDecimal* d);
uint64_t icall_decimal_to_uint64(const Il2CppDecimal* d);

// --- P0 (p0conv2): System.Convert's ROUNDING conversions --------------------
// Convert.ToInt32/ToInt64/ToUInt32/ToUInt64(decimal) on net48 do NOT truncate:
// they round the value to the nearest INTEGER, ties to EVEN (banker's
// rounding), and only THEN range-check (OverflowException when the ROUNDED
// value falls outside the target). Probe-pinned on the net48 oracle:
//   Convert.ToInt32(12.5m) == 12   (truncation would also give 12)
//   Convert.ToInt32(3.5m)  ==  4   (truncation gives 3)
//   Convert.ToInt32(12.9m) == 13   (truncation gives 12)
//   Convert.ToInt32(-12.5m) == -12, ToInt32(-0.9m) == -1
//   Convert.ToInt32(2147483647.6m) -> OverflowException (rounds to 2^31)
//   Convert.ToInt32(-2147483647.5m) == -2147483648 (rounds INTO range)
//   Convert.ToUInt32(-0.5m) == 0 (rounds to -0, which is zero)
// These entries therefore must NEVER be served by the truncating pair above --
// that would be a silent wrong value (the MethodMap.ConvertExtra.cs:39 note).
// Implementation: RoundCore(0 decimals, ToEven) then the SAME range check the
// truncating cores use.
int32_t icall_decimal_to_int32_round(const Il2CppDecimal* d);
int64_t icall_decimal_to_int64_round(const Il2CppDecimal* d);
uint32_t icall_decimal_to_uint32_round(const Il2CppDecimal* d);
uint64_t icall_decimal_to_uint64_round(const Il2CppDecimal* d);
// Batch 6 (conv-narrow): the same round-then-range-check contract for the
// NARROW integral targets Convert.ToByte/ToSByte/ToInt16/ToUInt16(decimal).
// net48 rounds half-to-even first, then range-checks the target
// (probe-pinned: ToByte(3.5m)==4, ToByte(-0.5m)==0, ToByte(256m)->Overflow,
// ToSByte(128m)->Overflow, ToInt16(32768m)->Overflow, ToUInt16(-1m)->Overflow).
// The result is carried in an int32 slot (the MethodMap "i" cast).
int32_t icall_decimal_to_byte_round(const Il2CppDecimal* d);
int32_t icall_decimal_to_sbyte_round(const Il2CppDecimal* d);
int32_t icall_decimal_to_int16_round(const Il2CppDecimal* d);
int32_t icall_decimal_to_uint16_round(const Il2CppDecimal* d);

// --- T1: misc ---------------------------------------------------------------
// DecCalc.GetHashCode (DecCalc.cs:1854-1871): canonicalizes the scale
// (Unscale) before hashing so 1.00m and 1m hash alike.
int32_t icall_decimal_get_hash_code(const Il2CppDecimal* d);

// Floating-point conversions (the netcore21+ audit closure -- VarDecFromR4/R8
// and VarR4/R8FromDec). ctor_double rounds to 15 significant digits (the
// documented VarDecFromR8 contract); ctor_float rounds to 7 significant digits
// (FLT_DIG -- the VarDecFromR4 contract, p0conv2 probe-pinned: (decimal)1f/3f
// == 0.3333333m, (decimal)16777216f == 16777220m, NOT the 15-digit expansion
// of the exact binary float). Both throw OverflowException for
// NaN/Infinity/magnitude overflow; to_double decomposes the 96-bit mantissa
// exactly and divides by 10^scale (nearest-double within <=1 ulp).
void icall_decimal_ctor_double(Il2CppDecimal* out, double value);
void icall_decimal_ctor_float(Il2CppDecimal* out, float value);
double icall_decimal_to_double(const Il2CppDecimal* d);
float icall_decimal_to_float(const Il2CppDecimal* d);

} // namespace runtime
} // namespace il2cpp
