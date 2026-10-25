//------------------------------------------------------------------------------
// decimal.cpp -- System.Decimal arithmetic (decimal domain T1).
//
// Algorithm base (iron rule 3): line-by-line C++ port of dotnet/runtime
// Decimal.DecCalc.cs (reference/netcore) -- DecAddSub / VarDecMul /
// VarDecDiv / VarDecMod / VarDecCmp / Equals / InternalRound / GetHashCode
// and every helper they use (UInt32Powers10 / UInt64Powers10 / TenToPowerNine
// / Buf12 / Buf16 / Buf24 / UInt64x64To128 / Div96By32 / Div96By64 /
// Div128By64 / Div128By96 / Div64By32 / IncreaseScale / IncreaseScale64 /
// Unscale / Div96ByConst / DivByConst / ScaleResult / SearchScale /
// Add32To96 / OverflowUnscale / VarDecCmpSub / VarDecModFull). No
// self-invented big-number algorithms.
//
// The port targets the TARGET_64BIT + non-X86-intrinsic fallback paths of the
// C# source (x64 Windows/Linux builds; the X86Base/X64 fast paths are a
// RyuJIT-specific optimization, the fallbacks are portable 64-bit math).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48). Exception contract (net48):
//   * result > 2^96-1 or scale > 28     -> OverflowException
//   * divide / remainder by zero        -> DivideByZeroException
//   * Round(decimals) outside 0..28     -> ArgumentOutOfRangeException
//   * Round(mode) outside ToEven..AwayFromZero -> ArgumentException (D-DR-1:
//     net48 rejects MidpointRounding 2/3/4 through Round; probe-verified)
//   * ToInt32/64/UInt32/64 out of range -> OverflowException
//
// GetHashCode SEMANTIC NOTE: net48's GetHashCode is an InternalCall into the
// native CLR: it converts the decimal to a double (VarR8FromDec) and returns
// (low32 & 0xFFFFFFF0) ^ high32 of the IEEE-754 bits, with a zero-mantissa
// shortcut (coreclr-1.x src/vm/comdecimal.cpp). That is what this file now
// implements (DecimalGetHashCodeNet48), NOT DecCalc.cs:1854 -- the .NET Core
// algorithm the T1 card originally pointed at. The two disagree on ordinary
// values (1.5m: 65551 vs 1073217536), and because Dictionary<decimal,T> /
// HashSet<decimal> bucket on this hash the Core algorithm was a user-visible
// silent wrong value. The decimal->double conversion net48 uses is also NOT
// the "exact decomposition / 10^scale" form: it is M / T10[scale] with T10 the
// correctly rounded powers of ten (probe-fitted, 2M samples, 0 mismatches).
//------------------------------------------------------------------------------

#include "decimal.h"
#include "decimal_format.h" // icall_decimal_parse (ctor_double digit-string path)
#include "il2cpp_string.h"  // il2cpp_string_new_from_utf8
#include "il2cpp_exception_ext.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_MSC_VER)
#include <intrin.h>
#endif

namespace il2cpp { namespace runtime {
namespace {

// DecCalc constants (Decimal.DecCalc.cs:93-104).
constexpr uint32_t kSignMask = 0x80000000u;
constexpr uint32_t kScaleMask = 0x00FF0000u;
constexpr int32_t kScaleShift = 16;
constexpr int32_t kDecScaleMax = 28;
constexpr uint32_t kTenToPowerNine = 1000000000u;
constexpr int32_t kMaxInt32Scale = 9;
constexpr int32_t kMaxInt64Scale = 19;

// UInt32Powers10 (DecCalc.cs:106-118), index n = 10^n for n in 0..9.
constexpr uint32_t kUInt32Powers10[10] = {
    1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000,
};

// UInt64Powers10 (DecCalc.cs:121-142), index n = 10^(n+1) for n in 0..18.
constexpr uint64_t kUInt64Powers10[19] = {
    10ull, 100ull, 1000ull, 10000ull, 100000ull, 1000000ull, 10000000ull,
    100000000ull, 1000000000ull, 10000000000ull, 100000000000ull,
    1000000000000ull, 10000000000000ull, 100000000000000ull,
    1000000000000000ull, 10000000000000000ull, 100000000000000000ull,
    1000000000000000000ull, 10000000000000000000ull,
};

// PowerOvfl + PowerOvflValues (DecCalc.cs:2453-2481): largest values that can
// sit in the upper two uints of a 96-bit number without overflowing when
// multiplied by the given power. MidLo = (mid << 32) | lo.
struct PowerOvfl
{
    uint32_t Hi;
    uint64_t MidLo;
};

constexpr PowerOvfl kPowerOvflValues[8] = {
    {429496729u, 2576980377ull * 4294967296ull + 2576980377ull},
    {42949672u, 4123168604ull * 4294967296ull + 687194767ull},
    {4294967u, 1271310319ull * 4294967296ull + 2645699854ull},
    {429496u, 3133608139ull * 4294967296ull + 694066715ull},
    {42949u, 2890341191ull * 4294967296ull + 2216890319ull},
    {4294u, 4154504685ull * 4294967296ull + 2369172679ull},
    {429u, 2133437386ull * 4294967296ull + 4102387834ull},
    {42u, 4078814305ull * 4294967296ull + 410238783ull},
};

// 96/128-bit buffers (DecCalc.cs:2483-2619). All uint32-lane based; the
// Low64/Mid64/High64 accessors are the little-endian ulong views.
struct Buf12
{
    uint32_t U0;
    uint32_t U1;
    uint32_t U2;

    uint64_t Low64() const { return (uint64_t)U1 << 32 | U0; }
    void SetLow64(uint64_t v)
    {
        U0 = (uint32_t)v;
        U1 = (uint32_t)(v >> 32);
    }
    uint64_t High64() const { return (uint64_t)U2 << 32 | U1; }
    void SetHigh64(uint64_t v)
    {
        U1 = (uint32_t)v;
        U2 = (uint32_t)(v >> 32);
    }
};

struct Buf16
{
    uint32_t U0;
    uint32_t U1;
    uint32_t U2;
    uint32_t U3;

    uint64_t Low64() const { return (uint64_t)U1 << 32 | U0; }
    void SetLow64(uint64_t v)
    {
        U0 = (uint32_t)v;
        U1 = (uint32_t)(v >> 32);
    }
    uint64_t High64() const { return (uint64_t)U3 << 32 | U2; }
    void SetHigh64(uint64_t v)
    {
        U2 = (uint32_t)v;
        U3 = (uint32_t)(v >> 32);
    }
    Buf12& Low96() { return *reinterpret_cast<Buf12*>(this); }
    Buf12& High96() { return *reinterpret_cast<Buf12*>(&U1); }
};

struct Buf24
{
    uint32_t U0;
    uint32_t U1;
    uint32_t U2;
    uint32_t U3;
    uint32_t U4;
    uint32_t U5;

    uint64_t Low64() const { return (uint64_t)U1 << 32 | U0; }
    void SetLow64(uint64_t v)
    {
        U0 = (uint32_t)v;
        U1 = (uint32_t)(v >> 32);
    }
    uint64_t Mid64() const { return (uint64_t)U3 << 32 | U2; }
    void SetMid64(uint64_t v)
    {
        U2 = (uint32_t)v;
        U3 = (uint32_t)(v >> 32);
    }
    uint64_t High64() const { return (uint64_t)U5 << 32 | U4; }
    void SetHigh64(uint64_t v)
    {
        U4 = (uint32_t)v;
        U5 = (uint32_t)(v >> 32);
    }
};

struct Buf28
{
    Buf24 Buf24v;
    uint32_t U6;
};

// DecCalc mutable view over the 16-byte decimal image (DecCalc.cs:42-91):
// uflags@0 / uhi@4 / ulo@8 / umid@12 -- identical layout to Il2CppDecimal,
// moved in/out at the icall boundary with memcpy.
struct DecCalc
{
    uint32_t uflags;
    uint32_t uhi;
    uint32_t ulo;
    uint32_t umid;

    uint64_t Low64() const { return (uint64_t)umid << 32 | ulo; }
    void SetLow64(uint64_t v)
    {
        ulo = (uint32_t)v;
        umid = (uint32_t)(v >> 32);
    }
    uint32_t Low() const { return ulo; }
    uint32_t Mid() const { return umid; }
    uint32_t High() const { return uhi; }
    bool IsNegative() const { return (int32_t)uflags < 0; }
    int32_t Scale() const { return (int32_t)((uflags >> kScaleShift) & 0xFF); }
};

static_assert(sizeof(DecCalc) == 16, "DecCalc must overlay the 16-byte decimal");

// Math.BigMul(ulong, ulong, out ulong low) -> high (DecCalc.cs:159-166).
uint64_t BigMul(uint64_t a, uint64_t b, uint64_t& low)
{
    const uint64_t aLo = a & 0xFFFFFFFFull;
    const uint64_t aHi = a >> 32;
    const uint64_t bLo = b & 0xFFFFFFFFull;
    const uint64_t bHi = b >> 32;
    const uint64_t t0 = aLo * bLo;
    const uint64_t t1 = aLo * bHi;
    const uint64_t t2 = aHi * bLo;
    const uint64_t t3 = aHi * bHi;
    const uint64_t mid = (t0 >> 32) + (t1 & 0xFFFFFFFFull) + (t2 & 0xFFFFFFFFull);
    low = (mid << 32) | (t0 & 0xFFFFFFFFull);
    return t3 + (t1 >> 32) + (t2 >> 32) + (mid >> 32);
}

// BitOperations.LeadingZeroCount(uint32).
int32_t LzCount(uint32_t value)
{
#if defined(_MSC_VER)
    if (value == 0)
    {
        return 32;
    }
    unsigned long idx;
    _BitScanReverse(&idx, value);
    return 31 - (int32_t)idx;
#else
    return value == 0 ? 32 : __builtin_clz(value);
#endif
}

// ThrowDecimalOverflowException (Decimal.cs:163; net48 OverflowException).
void ThrowDecimalOverflow()
{
    il2cpp_throw_overflow_exception("Value was either too large or too small for a Decimal.");
}

// Div64By32 (DecCalc.cs:169-182): partial divide where (dividend >> 32) < den;
// the quotient fits in 32 bits.
struct DivRem32
{
    uint32_t quo;
    uint32_t rem;
};

DivRem32 Div64By32(uint64_t dividend, uint32_t den)
{
    const uint32_t quo = (uint32_t)(dividend / den);
    return {quo, (uint32_t)(dividend - (uint64_t)quo * den)};
}

// Div96By32 (DecCalc.cs:191-236): full divide, 96-bit dividend, 32-bit
// divisor; quotient overwrites the buffer, remainder is returned.
uint32_t Div96By32(Buf12& bufNum, uint32_t den)
{
    uint64_t tmp, div;
    uint32_t rem;
    if (bufNum.U2 != 0)
    {
        tmp = bufNum.High64();
        div = tmp / den;
        rem = (uint32_t)(tmp - div * den);
        bufNum.SetHigh64(div);
        tmp = ((uint64_t)rem << 32) | bufNum.U0;
        if (tmp == 0)
        {
            return 0;
        }
        div = tmp / den;
        bufNum.U0 = (uint32_t)div;
        return (uint32_t)(tmp - div * den);
    }

    tmp = bufNum.Low64();
    if (tmp == 0)
    {
        return 0;
    }
    div = tmp / den;
    bufNum.SetLow64(div);
    return (uint32_t)(tmp - div * den);
}

// Div96By64 (DecCalc.cs:366-445): partial divide, 32-bit result and 64-bit
// remainder; divisor must be larger than the upper 64 bits of the dividend.
uint32_t Div96By64(Buf12& bufNum, uint64_t den)
{
    uint64_t num;
    const uint32_t num2 = bufNum.U2;
    if (num2 == 0)
    {
        num = bufNum.Low64();
        if (num < den)
        {
            return 0;
        }
        const uint64_t quo64 = num / den;
        bufNum.SetLow64(quo64);
        return (uint32_t)quo64;
    }

    uint32_t quo;
    const uint32_t denHigh32 = (uint32_t)(den >> 32);
    if (num2 >= denHigh32)
    {
        // Divide would overflow. Assume a quotient of 2^32 and set up the
        // remainder accordingly.
        num = bufNum.Low64();
        num -= den << 32;
        quo = 0;
        do
        {
            quo--;
            num += den;
        } while (num >= den);
        bufNum.SetLow64(num);
        return quo;
    }

    // Hardware divide won't overflow.
    const uint64_t num64 = bufNum.High64();
    if (num64 < denHigh32)
    {
        return 0;
    }

    const DivRem32 dr = Div64By32(num64, denHigh32);
    quo = dr.quo;
    num = (uint64_t)bufNum.U0 | ((uint64_t)dr.rem << 32); // remainder

    // Compute full remainder, rem = dividend - (quo * divisor).
    const uint64_t prod = (uint64_t)quo * (uint32_t)den;
    num -= prod;
    if (num > ~prod)
    {
        do
        {
            quo--;
            num += den;
        } while (num >= den);
    }

    bufNum.SetLow64(num);
    return quo;
}

// Div128By64 (DecCalc.cs:341-357): partial divide, 64-bit result and 64-bit
// remainder; divisor must be larger than the upper 64 bits of the dividend.
uint64_t Div128By64(Buf16& bufNum, uint64_t den)
{
    const uint32_t hiBits = Div96By64(bufNum.High96(), den);
    const uint32_t loBits = Div96By64(bufNum.Low96(), den);
    return ((uint64_t)hiBits << 32) | loBits;
}

// Div128By96 (DecCalc.cs:458-517): partial divide, 32-bit result and 96-bit
// remainder; the top divisor uint must be larger than the top dividend uint.
uint32_t Div128By96(Buf16& bufNum, Buf12& bufDen)
{
    const uint64_t dividend = bufNum.High64();
    const uint32_t den = bufDen.U2;
    if (dividend < den)
    {
        return 0;
    }

    const DivRem32 dr = Div64By32(dividend, den);
    uint32_t quo = dr.quo;
    uint32_t remainder = dr.rem;

    // Compute full remainder, rem = dividend - (quo * divisor).
    uint64_t prod1;
    const uint64_t prod2 = BigMul(bufDen.Low64(), quo, prod1);
    uint64_t num = bufNum.Low64() - prod1;
    remainder -= (uint32_t)prod2;

    // Propagate carries.
    if (num > ~prod1)
    {
        remainder--;
        if (remainder < ~(uint32_t)prod2)
        {
            goto PosRem;
        }
    }
    else if (remainder <= ~(uint32_t)prod2)
    {
        goto PosRem;
    }
    {
        // Remainder went negative. Add divisor back in until it's positive,
        // a max of 2 times.
        prod1 = bufDen.Low64();
        while (true)
        {
            quo--;
            num += prod1;
            remainder += den;

            if (num < prod1)
            {
                // Detected carry. Check for carry out of top before adding it in.
                if (remainder++ < den)
                {
                    break;
                }
            }
            if (remainder < den)
            {
                break; // detected carry
            }
        }
    }
PosRem:
    bufNum.SetLow64(num);
    bufNum.U2 = remainder;
    return quo;
}

// IncreaseScale(Buf12) (DecCalc.cs:526-545): 96-bit * 32-bit; the low 96 bits
// overwrite the input, the top 32 bits of the product are returned.
uint32_t IncreaseScale(Buf12& bufNum, uint32_t power)
{
    uint64_t low64;
    uint64_t hi64 = BigMul(bufNum.Low64(), power, low64);
    bufNum.SetLow64(low64);
    hi64 += (uint64_t)bufNum.U2 * power;
    bufNum.U2 = (uint32_t)hi64;
    return (uint32_t)(hi64 >> 32);
}

// IncreaseScale(Buf16) (DecCalc.cs:553-562).
void IncreaseScale(Buf16& bufNum, uint32_t power)
{
    uint64_t low64;
    uint64_t hi64 = BigMul(bufNum.Low64(), power, low64);
    bufNum.SetLow64(low64);
    bufNum.SetHigh64((uint64_t)bufNum.U2 * power + hi64);
}

// IncreaseScale64 (DecCalc.cs:570-574): 64-bit * 32-bit into a 96-bit buffer.
void IncreaseScale64(Buf12& bufNum, uint32_t power)
{
    uint64_t low64;
    bufNum.U2 = (uint32_t)BigMul(bufNum.Low64(), power, low64);
    bufNum.SetLow64(low64);
}

// Div96ByConst (DecCalc.cs:239-303, TARGET_64BIT path).
bool Div96ByConst(uint64_t& high64, uint32_t& low, uint32_t pow)
{
    const uint64_t div64 = high64 / pow;
    const uint32_t div = (uint32_t)((((high64 - div64 * pow) << 32) + low) / pow);
    if (low == div * pow)
    {
        high64 = div64;
        low = div;
        return true;
    }
    return false;
}

// Unscale (DecCalc.cs:310-331, TARGET_64BIT path).
void Unscale(uint32_t& low, uint64_t& high64, int32_t& scale)
{
    while ((uint8_t)low == 0 && scale >= 8 && Div96ByConst(high64, low, 100000000))
    {
        scale -= 8;
    }

    if ((low & 0xF) == 0 && scale >= 4 && Div96ByConst(high64, low, 10000))
    {
        scale -= 4;
    }

    if ((low & 3) == 0 && scale >= 2 && Div96ByConst(high64, low, 100))
    {
        scale -= 2;
    }

    if ((low & 1) == 0 && scale >= 1 && Div96ByConst(high64, low, 10))
    {
        scale--;
    }
}

// DivByConst (DecCalc.cs:741-769, TARGET_64BIT path).
uint32_t DivByConst(uint32_t* result, uint32_t hiRes, uint32_t& quotient,
                    uint32_t& remainder, uint32_t power)
{
    const uint32_t high = result[hiRes];
    quotient = high / power;
    remainder = high - quotient * power;
    for (uint32_t i = hiRes - 1; (int32_t)i >= 0; i--)
    {
        const uint64_t num = result[i] + ((uint64_t)remainder << 32);
        remainder = (uint32_t)num - (result[i] = (uint32_t)(num / power)) * power;
    }
    return power;
}

// ScaleResult (DecCalc.cs:584-738): scale a 192-bit product down to fit 96
// bits with IEEE half-even rounding; returns the new scale.
int32_t ScaleResult(Buf24& bufRes, uint32_t hiRes, int32_t scale)
{
    uint32_t* result = reinterpret_cast<uint32_t*>(&bufRes);

    int32_t newScale = 0;
    if (hiRes > 2)
    {
        newScale = (int32_t)hiRes * 32 - 64 - 1;
        newScale -= LzCount(result[hiRes]);

        // Multiply bit position by log10(2) to figure its power of 10.
        newScale = ((newScale * 77) >> 8) + 1;
        if (newScale > scale)
        {
            ThrowDecimalOverflow();
        }
    }

    if (newScale < scale - kDecScaleMax)
    {
        newScale = scale - kDecScaleMax;
    }

    if (newScale != 0)
    {
        scale -= newScale;
        uint32_t sticky = 0;
        uint32_t quotient, remainder = 0;

        while (true)
        {
            sticky |= remainder; // record remainder as sticky bit

            uint32_t power;
            switch (newScale)
            {
            case 1:
                power = DivByConst(result, hiRes, quotient, remainder, 10);
                break;
            case 2:
                power = DivByConst(result, hiRes, quotient, remainder, 100);
                break;
            case 3:
                power = DivByConst(result, hiRes, quotient, remainder, 1000);
                break;
            case 4:
                power = DivByConst(result, hiRes, quotient, remainder, 10000);
                break;
            case 5:
                power = DivByConst(result, hiRes, quotient, remainder, 100000);
                break;
            case 6:
                power = DivByConst(result, hiRes, quotient, remainder, 1000000);
                break;
            case 7:
                power = DivByConst(result, hiRes, quotient, remainder, 10000000);
                break;
            case 8:
                power = DivByConst(result, hiRes, quotient, remainder, 100000000);
                break;
            default:
                power = DivByConst(result, hiRes, quotient, remainder, kTenToPowerNine);
                break;
            }
            result[hiRes] = quotient;
            // If the first quotient was 0, update hiRes.
            if (quotient == 0 && hiRes != 0)
            {
                hiRes--;
            }

            newScale -= kMaxInt32Scale;
            if (newScale > 0)
            {
                continue; // scale some more
            }

            // If we scaled enough, hiRes would be 2 or less.
            if (hiRes > 2)
            {
                if (scale == 0)
                {
                    ThrowDecimalOverflow();
                }
                newScale = 1;
                scale--;
                continue; // scale by 10
            }

            // Round final result: remainder >= 1/2 divisor; at exactly 1/2,
            // round up if odd or the sticky bit is set.
            power >>= 1; // power of 10 always even
            if (power <= remainder && (power < remainder ||
                                       ((result[0] & 1) | sticky) != 0) &&
                ++result[0] == 0)
            {
                uint32_t cur = 0;
                do
                {
                } while (++result[++cur] == 0);

                if (cur > 2)
                {
                    // The rounding caused a carry beyond 96 bits. Scale by 10 more.
                    if (scale == 0)
                    {
                        ThrowDecimalOverflow();
                    }
                    hiRes = cur;
                    sticky = 0;
                    remainder = 0;
                    newScale = 1;
                    scale--;
                    continue; // scale by 10
                }
            }

            break;
        }
    }
    return scale;
}

// Add32To96 (DecCalc.cs:914-922): false on overflow. Declared before
// OverflowUnscale which uses it.
bool Add32To96(Buf12& bufNum, uint32_t value);

// OverflowUnscale (DecCalc.cs:776-812).
int32_t OverflowUnscale(Buf12& bufQuo, int32_t scale, bool sticky)
{
    if (--scale < 0)
    {
        ThrowDecimalOverflow();
    }

    // We have overflown, so load the high bit with a one.
    const uint64_t highbit = 1ull << 32;
    bufQuo.U2 = (uint32_t)(highbit / 10);

    uint32_t remainder;
    uint64_t tmp = ((highbit % 10) << 32) + bufQuo.U1;
    uint32_t div = (uint32_t)(tmp / 10);
    bufQuo.U1 = div;
    tmp = ((tmp - div * 10) << 32) + bufQuo.U0;
    div = (uint32_t)(tmp / 10);
    bufQuo.U0 = div;
    remainder = (uint32_t)(tmp - div * 10);

    // The remainder is the last digit that does not fit; use it to decide
    // whether to round up.
    if (remainder > 5 || (remainder == 5 && (sticky || (bufQuo.U0 & 1) != 0)))
    {
        Add32To96(bufQuo, 1);
    }
    return scale;
}

// SearchScale (DecCalc.cs:822-908): max power of 10, <= 9, that the quotient
// can be scaled up by and still fit in 96 bits.
int32_t SearchScale(uint64_t resMidLo, uint32_t resHi, int32_t scale)
{
    constexpr uint32_t kOvflMax9Hi = 4;
    constexpr uint32_t kOvflMax8Hi = 42;
    constexpr uint32_t kOvflMax7Hi = 429;
    constexpr uint32_t kOvflMax6Hi = 4294;
    constexpr uint32_t kOvflMax5Hi = 42949;
    constexpr uint32_t kOvflMax4Hi = 429496;
    constexpr uint32_t kOvflMax3Hi = 4294967;
    constexpr uint32_t kOvflMax2Hi = 42949672;
    constexpr uint32_t kOvflMax1Hi = 429496729;
    constexpr uint64_t kOvflMax9MidLo = 5441186219426131129ull;

    int32_t curScale = 0;

    // Quick check to stop us from trying to scale any more.
    if (resHi > kOvflMax1Hi)
    {
        goto HaveScale;
    }

    if (scale > kDecScaleMax - 9)
    {
        // We can't scale by 10^9 without exceeding the max scale factor.
        curScale = kDecScaleMax - scale;
        if (resHi < kPowerOvflValues[curScale - 1].Hi)
        {
            goto HaveScale;
        }
    }
    else if (resHi < kOvflMax9Hi || (resHi == kOvflMax9Hi && resMidLo <= kOvflMax9MidLo))
    {
        return 9;
    }

    // Search for a power to scale by < 9. Binary search.
    if (resHi > kOvflMax5Hi)
    {
        if (resHi > kOvflMax3Hi)
        {
            curScale = 2;
            if (resHi > kOvflMax2Hi)
            {
                curScale--;
            }
        }
        else
        {
            curScale = 4;
            if (resHi > kOvflMax4Hi)
            {
                curScale--;
            }
        }
    }
    else
    {
        if (resHi > kOvflMax7Hi)
        {
            curScale = 6;
            if (resHi > kOvflMax6Hi)
            {
                curScale--;
            }
        }
        else
        {
            curScale = 8;
            if (resHi > kOvflMax8Hi)
            {
                curScale--;
            }
        }
    }

    // We already found we could not use the power one larger, so if we can
    // use this power it is the biggest, and we're done.
    if (resHi == kPowerOvflValues[curScale - 1].Hi &&
        resMidLo > kPowerOvflValues[curScale - 1].MidLo)
    {
        curScale--;
    }

HaveScale:
    // curScale = largest power of 10 we can scale by without overflow.
    if (curScale + scale < 0)
    {
        ThrowDecimalOverflow();
    }

    return curScale;
}

// Add32To96 (DecCalc.cs:914-922): false on overflow.
bool Add32To96(Buf12& bufNum, uint32_t value)
{
    uint64_t low = bufNum.Low64();
    if ((low += value) < value)
    {
        if (++bufNum.U2 == 0)
        {
            return false;
        }
    }
    bufNum.SetLow64(low);
    return true;
}

// DecAddSub (DecCalc.cs:931-1242): d1 = d1 +/- d2; d2 trashed.
void DecAddSub(DecCalc& d1, DecCalc& d2, bool sign)
{
    uint64_t low64 = d1.Low64();
    uint32_t high = d1.uhi, flags = d1.uflags, d2flags = d2.uflags;

    const uint32_t xorflags = d2flags ^ flags;
    sign ^= (xorflags & kSignMask) != 0;

    if ((xorflags & kScaleMask) == 0)
    {
        // Scale factors are equal, no alignment necessary.
        goto AlignedAdd;
    }
    else
    {
        // Scale factors are not equal. Assume that a larger scale factor
        // (more decimal places) means the number is smaller. Start by
        // guessing the right operand has the larger scale factor; the result
        // will have the larger scale factor.
        const uint32_t d1flags = flags;
        flags = (d2flags & kScaleMask) | (flags & kSignMask); // scale of "smaller", sign of "larger"
        int32_t scale = (int32_t)(flags - d1flags) >> kScaleShift;

        if (scale < 0)
        {
            // Guessed scale factor wrong. Swap operands.
            scale = -scale;
            flags = d1flags;
            if (sign)
            {
                flags ^= kSignMask;
            }
            low64 = d2.Low64();
            high = d2.uhi;
            d2 = d1;
        }

        uint32_t power;
        uint64_t tmp64;

        // d1 will need to be multiplied by 10^scale so it will have the same
        // scale as d2. It may extend to 192 bits of precision.

        // Scan for zeros in the upper words.
        if (high == 0)
        {
            if (low64 <= 0xFFFFFFFFull)
            {
                if ((uint32_t)low64 == 0)
                {
                    // Left arg is zero, return right.
                    uint32_t signFlags = flags & kSignMask;
                    if (sign)
                    {
                        signFlags ^= kSignMask;
                    }
                    d1 = d2;
                    d1.uflags = (d2.uflags & kScaleMask) | signFlags;
                    return;
                }

                do
                {
                    if ((uint32_t)scale <= kMaxInt32Scale)
                    {
                        low64 = (uint64_t)(uint32_t)low64 * kUInt32Powers10[scale];
                        goto AlignedAdd;
                    }
                    scale -= kMaxInt32Scale;
                    low64 = (uint64_t)(uint32_t)low64 * kTenToPowerNine;
                } while (low64 <= 0xFFFFFFFFull);
            }

            do
            {
                power = kTenToPowerNine;
                if ((uint32_t)scale < kMaxInt32Scale)
                {
                    power = kUInt32Powers10[scale];
                }
                high = (uint32_t)BigMul(low64, power, low64);
                if ((scale -= kMaxInt32Scale) <= 0)
                {
                    goto AlignedAdd;
                }
            } while (high == 0);
        }

        while (true)
        {
            // Scaling won't make it larger than 4 uints.
            power = kTenToPowerNine;
            if ((uint32_t)scale < kMaxInt32Scale)
            {
                power = kUInt32Powers10[scale];
            }
            tmp64 = BigMul(low64, power, low64);
            tmp64 += (uint64_t)high * power;

            scale -= kMaxInt32Scale;
            if (tmp64 > 0xFFFFFFFFull)
            {
                break;
            }

            high = (uint32_t)tmp64;
            // Result fits in 96 bits. Use standard aligned add.
            if (scale <= 0)
            {
                goto AlignedAdd;
            }
        }

        // Have to scale by a bunch. Move the number to a buffer where it has
        // room to grow as it's scaled.
        Buf24 bufNum;
        bufNum.SetLow64(low64);
        bufNum.SetMid64(tmp64);
        uint32_t hiProd = 3;

        // Scaling loop, up to 10^9 at a time; hiProd tracks the index of the
        // highest non-zero uint.
        for (; scale > 0; scale -= kMaxInt32Scale)
        {
            power = kTenToPowerNine;
            if ((uint32_t)scale < kMaxInt32Scale)
            {
                power = kUInt32Powers10[scale];
            }
            tmp64 = 0;
            uint32_t* rgulNum = reinterpret_cast<uint32_t*>(&bufNum);
            for (uint32_t cur = 0;;)
            {
                tmp64 += (uint64_t)rgulNum[cur] * power;
                rgulNum[cur] = (uint32_t)tmp64;
                cur++;
                tmp64 >>= 32;
                if (cur > hiProd)
                {
                    break;
                }
            }

            if (tmp64 != 0)
            {
                // We're extending the result by another uint.
                rgulNum[++hiProd] = (uint32_t)tmp64;
            }
        }

        // Scaling complete, do the add. Could be subtract if signs differ.
        tmp64 = bufNum.Low64();
        low64 = d2.Low64();
        const uint32_t tmpHigh = bufNum.U2;
        high = d2.uhi;

        if (sign)
        {
            // Signs differ, subtract.
            low64 = tmp64 - low64;
            high = tmpHigh - high;

            // Propagate carry.
            if (low64 > tmp64)
            {
                high--;
                if (high < tmpHigh)
                {
                    goto NoCarry;
                }
            }
            else if (high <= tmpHigh)
            {
                goto NoCarry;
            }

            // Carry the subtraction into the higher bits.
            uint32_t* number = reinterpret_cast<uint32_t*>(&bufNum);
            uint32_t cur = 3;
            do
            {
            } while (number[cur++]-- == 0);
            if (number[hiProd] == 0 && --hiProd <= 2)
            {
                goto ReturnResult;
            }
        }
        else
        {
            // Signs the same, add.
            low64 += tmp64;
            high += tmpHigh;

            // Propagate carry.
            if (low64 < tmp64)
            {
                high++;
                if (high > tmpHigh)
                {
                    goto NoCarry;
                }
            }
            else if (high >= tmpHigh)
            {
                goto NoCarry;
            }

            uint32_t* number = reinterpret_cast<uint32_t*>(&bufNum);
            for (uint32_t cur = 3; ++number[cur++] == 0;)
            {
                if (hiProd < cur)
                {
                    number[cur] = 1;
                    hiProd = cur;
                    break;
                }
            }
        }
    NoCarry:

        bufNum.SetLow64(low64);
        bufNum.U2 = high;
        scale = ScaleResult(bufNum, hiProd, (flags >> kScaleShift) & 0xFF);
        flags = (flags & ~kScaleMask) | ((uint32_t)scale << kScaleShift);
        low64 = bufNum.Low64();
        high = bufNum.U2;
        goto ReturnResult;
    }

SignFlip:
    {
        // Got negative result. Flip its sign.
        flags ^= kSignMask;
        high = ~high;
        low64 = (uint64_t)-(int64_t)low64;
        if (low64 == 0)
        {
            high++;
        }
        goto ReturnResult;
    }

AlignedScale:
    {
        // The addition carried above 96 bits. Divide the value by 10, dropping
        // the scale factor.
        if ((flags & kScaleMask) == 0)
        {
            ThrowDecimalOverflow();
        }
        flags -= 1 << kScaleShift;

        const uint32_t den = 10;
        uint64_t num = high + (1ull << 32);
        high = (uint32_t)(num / den);
        num = ((num - high * den) << 32) + (low64 >> 32);
        uint32_t div = (uint32_t)(num / den);
        num = ((num - div * den) << 32) + (uint32_t)low64;
        low64 = div;
        low64 <<= 32;
        div = (uint32_t)(num / den);
        low64 += div;
        div = (uint32_t)num - div * den;

        // See if we need to round up.
        if (div >= 5 && (div > 5 || (low64 & 1) != 0))
        {
            if (++low64 == 0)
            {
                high++;
            }
        }
        goto ReturnResult;
    }

AlignedAdd:
    {
        const uint64_t d1Low64 = low64;
        const uint32_t d1High = high;
        if (sign)
        {
            // Signs differ - subtract.
            low64 = d1Low64 - d2.Low64();
            high = d1High - d2.uhi;

            // Propagate carry.
            if (low64 > d1Low64)
            {
                high--;
                if (high >= d1High)
                {
                    goto SignFlip;
                }
            }
            else if (high > d1High)
            {
                goto SignFlip;
            }
        }
        else
        {
            // Signs are the same - add.
            low64 = d1Low64 + d2.Low64();
            high = d1High + d2.uhi;

            // Propagate carry.
            if (low64 < d1Low64)
            {
                high++;
                if (high <= d1High)
                {
                    goto AlignedScale;
                }
            }
            else if (high < d1High)
            {
                goto AlignedScale;
            }
        }
        goto ReturnResult;
    }

ReturnResult:
    d1.uflags = flags;
    d1.uhi = high;
    d1.SetLow64(low64);
}

// VarDecCmpSub (DecCalc.cs:1321-1384): magnitude compare of same-sign values.
int32_t VarDecCmpSub(const DecCalc& d1, const DecCalc& d2)
{
    int32_t flags = (int32_t)d2.uflags;
    int32_t sign = (flags >> 31) | 1;
    int32_t scale = flags - (int32_t)d1.uflags;

    uint64_t low64 = d1.Low64();
    uint32_t high = d1.uhi;

    uint64_t d2Low64 = d2.Low64();
    uint32_t d2High = d2.uhi;

    if (scale != 0)
    {
        scale >>= kScaleShift;

        // Scale factors are not equal. Assume the right operand has the
        // larger scale factor; swap operands if the guess is wrong.
        if (scale < 0)
        {
            scale = -scale;
            sign = -sign;

            const uint64_t tmp64 = low64;
            low64 = d2Low64;
            d2Low64 = tmp64;

            const uint32_t tmp = high;
            high = d2High;
            d2High = tmp;
        }

        // d1 will need to be multiplied by 10^scale so it will have the same
        // scale as d2. Scaling loop, up to 10^9 at a time.
        do
        {
            const uint32_t power = (uint32_t)scale >= kMaxInt32Scale
                                       ? kTenToPowerNine
                                       : kUInt32Powers10[scale];
            const uint64_t tmp = BigMul(low64, power, low64);
            const uint64_t big = tmp + (uint64_t)high * power;
            // If the scaled value has more than 96 significant bits then it's
            // greater than d2.
            if (big > 0xFFFFFFFFull)
            {
                return sign;
            }
            high = (uint32_t)big;
        } while ((scale -= kMaxInt32Scale) > 0);
    }

    const uint32_t cmpHigh = high - d2High;
    if (cmpHigh != 0)
    {
        // Check for overflow.
        if (cmpHigh > high)
        {
            sign = -sign;
        }
        return sign;
    }

    const uint64_t cmpLow64 = low64 - d2Low64;
    if (cmpLow64 == 0)
    {
        sign = 0;
    }
    // Check for overflow.
    else if (cmpLow64 > low64)
    {
        sign = -sign;
    }
    return sign;
}

// VarDecCmp (DecCalc.cs:1304-1319): -1 / 0 / 1.
int32_t VarDecCmp(const DecCalc& d1, const DecCalc& d2)
{
    if ((d2.Low64() | d2.uhi) == 0)
    {
        if ((d1.Low64() | d1.uhi) == 0)
        {
            return 0;
        }
        return ((int32_t)d1.uflags >> 31) | 1;
    }
    if ((d1.Low64() | d1.uhi) == 0)
    {
        return -(((int32_t)d2.uflags >> 31) | 1);
    }

    const int32_t sign = ((int32_t)d1.uflags >> 31) - ((int32_t)d2.uflags >> 31);
    if (sign != 0)
    {
        return sign;
    }
    return VarDecCmpSub(d1, d2);
}

// DecEquals (DecCalc.cs:1287-1299): numerically equal (scales may differ).
bool DecEquals(const DecCalc& d1, const DecCalc& d2)
{
    if ((d2.Low64() | d2.uhi) == 0)
    {
        return (d1.Low64() | d1.uhi) == 0;
    }
    if ((d1.Low64() | d1.uhi) == 0)
    {
        return false;
    }
    if ((int32_t)(d1.uflags ^ d2.uflags) < 0)
    {
        return false;
    }
    return VarDecCmpSub(d1, d2) == 0;
}

// VarDecMul (DecCalc.cs:1389-1543): d1 = d1 * d2.
void VarDecMul(DecCalc& d1, DecCalc& d2)
{
    int32_t scale = (uint8_t)((d1.uflags + d2.uflags) >> kScaleShift);

    uint64_t tmp;
    uint32_t hiProd;
    Buf24 bufProd;

    if ((d1.uhi | d1.umid) == 0)
    {
        if ((d2.uhi | d2.umid) == 0)
        {
            // Upper 64 bits are zero.
            uint64_t low64 = (uint64_t)d1.ulo * d2.ulo;
            if (scale > kDecScaleMax)
            {
                // Result scale is too big. Divide result by a power of 10 to
                // reduce it. If the amount to divide by is > 19 the result is
                // guaranteed less than 1/2 [max value in 64 bits = 1.84E19].
                if (scale > kDecScaleMax + kMaxInt64Scale)
                {
                    goto ReturnZero;
                }

                scale -= kDecScaleMax + 1;
                const uint64_t power = kUInt64Powers10[scale];

                const uint64_t q = low64 / power;
                uint64_t remainder = low64 - q * power;
                low64 = q;

                // Round result: remainder >= 1/2 of divisor. The divisor is a
                // power of 10, so it is always even.
                if (remainder >= (power >> 1) &&
                    (remainder > (power >> 1) || ((uint32_t)low64 & 1) > 0))
                {
                    low64++;
                }

                scale = kDecScaleMax;
            }
            d1.SetLow64(low64);
            d1.uflags = ((d2.uflags ^ d1.uflags) & kSignMask) | ((uint32_t)scale << kScaleShift);
            return;
        }
        else
        {
            // Left value is 32-bit, result fits in 4 uints.
            uint64_t lo;
            tmp = BigMul(d1.ulo, d2.Low64(), lo);
            bufProd.SetLow64(lo);

            if (d2.uhi != 0)
            {
                tmp += (uint64_t)d1.ulo * d2.uhi;
                if (tmp > 0xFFFFFFFFull)
                {
                    bufProd.SetMid64(tmp);
                    hiProd = 3;
                    goto SkipScan;
                }
            }
            bufProd.U2 = (uint32_t)tmp;
            hiProd = 2;
        }
    }
    else if ((d2.uhi | d2.umid) == 0)
    {
        // Right value is 32-bit, result fits in 4 uints.
        uint64_t lo;
        tmp = BigMul(d1.Low64(), d2.ulo, lo);
        bufProd.SetLow64(lo);

        if (d1.uhi != 0)
        {
            tmp += (uint64_t)d2.ulo * d1.uhi;
            if (tmp > 0xFFFFFFFFull)
            {
                bufProd.SetMid64(tmp);
                hiProd = 3;
                goto SkipScan;
            }
        }
        bufProd.U2 = (uint32_t)tmp;
        hiProd = 2;
    }
    else
    {
        // At least one operand has bits set in the upper 64 bits. Compute and
        // accumulate the partial products into a 192-bit (3x64) result.
        uint64_t lo;
        uint64_t mid64 = BigMul(d1.Low64(), d2.Low64(), lo);
        bufProd.SetLow64(lo);

        if ((d1.uhi | d2.uhi) != 0)
        {
            // hi64 will never overflow since the result always fits in
            // 192 (2*96) bits.
            uint64_t hi64 = (uint64_t)d1.uhi * d2.uhi;

            // Do crosswise multiplications between the upper 32 bits and the
            // lower 64 bits.
            hi64 += BigMul(d1.Low64(), d2.uhi, tmp);
            mid64 += tmp;
            if (mid64 < tmp)
            {
                ++hi64;
            }

            hi64 += BigMul(d2.Low64(), d1.uhi, tmp);
            mid64 += tmp;
            if (mid64 < tmp)
            {
                ++hi64;
            }

            bufProd.SetMid64(mid64);
            bufProd.SetHigh64(hi64);
            hiProd = 5;
        }
        else
        {
            bufProd.SetMid64(mid64);
            hiProd = 3;
        }
    }

    // Check for leading zero uints on the product.
    {
        uint32_t* product = reinterpret_cast<uint32_t*>(&bufProd);
        while (product[hiProd] == 0)
        {
            if (hiProd == 0)
            {
                goto ReturnZero;
            }
            hiProd--;
        }
    }

SkipScan:
    if (hiProd > 2 || scale > kDecScaleMax)
    {
        scale = ScaleResult(bufProd, hiProd, scale);
    }

    d1.SetLow64(bufProd.Low64());
    d1.uhi = bufProd.U2;
    d1.uflags = ((d2.uflags ^ d1.uflags) & kSignMask) | ((uint32_t)scale << kScaleShift);
    return;

ReturnZero:
    d1.uflags = 0;
    d1.uhi = 0;
    d1.SetLow64(0);
}

// VarDecDiv (DecCalc.cs:1877-2137): d1 = d1 / d2.
void VarDecDiv(DecCalc& d1, DecCalc& d2)
{
    Buf12 bufQuo;

    uint32_t power;
    int32_t curScale;

    int32_t scale = (int8_t)((d1.uflags - d2.uflags) >> kScaleShift);
    bool unscale = false;
    uint32_t tmp;

    if ((d2.uhi | d2.umid) == 0)
    {
        // Divisor is only 32 bits. Easy divide.
        const uint32_t den = d2.ulo;
        if (den == 0)
        {
            il2cpp_throw_divide_by_zero_exception(nullptr);
            return;
        }

        bufQuo.SetLow64(d1.Low64());
        bufQuo.U2 = d1.uhi;
        uint32_t remainder = Div96By32(bufQuo, den);

        while (true)
        {
            if (remainder == 0)
            {
                if (scale < 0)
                {
                    curScale = kMaxInt32Scale < -scale ? kMaxInt32Scale : -scale;
                    goto HaveScale;
                }
                break;
            }

            // We need to unscale if and only if we have a non-zero remainder.
            unscale = true;

            // We have computed a quotient based on the natural scale
            // (<dividend scale> - <divisor scale>). We have a non-zero
            // remainder, so now we should increase the scale if possible to
            // include more quotient bits.
            if (scale == kDecScaleMax ||
                (curScale = SearchScale(bufQuo.Low64(), bufQuo.U2, scale)) == 0)
            {
                // No more scaling to be done, but remainder is non-zero.
                // Round quotient.
                tmp = remainder << 1;
                if (tmp < remainder ||
                    (tmp >= den && (tmp > den || (bufQuo.U0 & 1) != 0)))
                {
                    goto RoundUp;
                }
                break;
            }

        HaveScale:
            power = kUInt32Powers10[curScale];
            scale += curScale;

            if (IncreaseScale(bufQuo, power) != 0)
            {
                ThrowDecimalOverflow();
            }

            const uint64_t num = (uint64_t)remainder * power;
            const DivRem32 dr = Div64By32(num, den);
            uint32_t div = dr.quo;
            remainder = dr.rem;

            if (!Add32To96(bufQuo, div))
            {
                scale = OverflowUnscale(bufQuo, scale, remainder != 0);
                break;
            }
        }
    }
    else
    {
        // Divisor has bits set in the upper 64 bits.
        //
        // Divisor must be fully normalized (shifted so bit 31 of the most
        // significant uint is 1). Locate the MSB so we know how much to
        // normalize by. The dividend will be shifted by the same amount so the
        // quotient is not changed.
        tmp = d2.uhi;
        if (tmp == 0)
        {
            tmp = d2.umid;
        }

        curScale = LzCount(tmp);

        // Shift both dividend and divisor left by curScale.
        Buf16 bufRem;
        bufRem.SetLow64(d1.Low64() << curScale);
        bufRem.SetHigh64((d1.umid + ((uint64_t)d1.uhi << 32)) >> (32 - curScale));

        const uint64_t divisor = d2.Low64() << curScale;

        if (d2.uhi == 0)
        {
            // Have a 64-bit divisor in divisor. The remainder (currently 96
            // bits spread over 4 uints) will be < divisor.
            bufQuo.U2 = 0;
            bufQuo.SetLow64(Div128By64(bufRem, divisor));
            while (true)
            {
                if (bufRem.Low64() == 0)
                {
                    if (scale < 0)
                    {
                        curScale = kMaxInt32Scale < -scale ? kMaxInt32Scale : -scale;
                        goto HaveScale64;
                    }
                    break;
                }

                // We need to unscale if and only if we have a non-zero remainder.
                unscale = true;

                // Remainder is non-zero. Scale up quotient and remainder by
                // powers of 10 so we can compute more significant bits.
                if (scale == kDecScaleMax ||
                    (curScale = SearchScale(bufQuo.Low64(), bufQuo.U2, scale)) == 0)
                {
                    // No more scaling to be done, but remainder is non-zero.
                    // Round quotient.
                    uint64_t tmp64 = bufRem.Low64();
                    if ((int64_t)tmp64 < 0 || (tmp64 <<= 1) > divisor ||
                        (tmp64 == divisor && (bufQuo.U0 & 1) != 0))
                    {
                        goto RoundUp;
                    }
                    break;
                }

            HaveScale64:
                power = kUInt32Powers10[curScale];
                scale += curScale;

                if (IncreaseScale(bufQuo, power) != 0)
                {
                    ThrowDecimalOverflow();
                }

                IncreaseScale64(bufRem.Low96(), power);
                tmp = Div96By64(bufRem.Low96(), divisor);
                if (!Add32To96(bufQuo, tmp))
                {
                    scale = OverflowUnscale(bufQuo, scale, bufRem.Low64() != 0);
                    break;
                }
            }
        }
        else
        {
            // Have a 96-bit divisor in bufDivisor.
            //
            // Start by finishing the shift left by curScale.
            Buf12 bufDivisor;
            bufDivisor.SetLow64(divisor);
            bufDivisor.U2 = (uint32_t)((d2.umid + ((uint64_t)d2.uhi << 32)) >> (32 - curScale));

            // The remainder (currently 96 bits spread over 4 uints) will be
            // < divisor.
            bufQuo.SetLow64(Div128By96(bufRem, bufDivisor));
            bufQuo.U2 = 0;

            while (true)
            {
                if ((bufRem.Low64() | bufRem.U2) == 0)
                {
                    if (scale < 0)
                    {
                        curScale = kMaxInt32Scale < -scale ? kMaxInt32Scale : -scale;
                        goto HaveScale96;
                    }
                    break;
                }

                // We need to unscale if and only if we have a non-zero remainder.
                unscale = true;

                // Remainder is non-zero. Scale up quotient and remainder by
                // powers of 10 so we can compute more significant bits.
                if (scale == kDecScaleMax ||
                    (curScale = SearchScale(bufQuo.Low64(), bufQuo.U2, scale)) == 0)
                {
                    // No more scaling to be done, but remainder is non-zero.
                    // Round quotient.
                    if ((int32_t)bufRem.U2 < 0)
                    {
                        goto RoundUp;
                    }

                    tmp = bufRem.U1 >> 31;
                    bufRem.SetLow64(bufRem.Low64() << 1);
                    bufRem.U2 = (bufRem.U2 << 1) + tmp;

                    if (bufRem.U2 > bufDivisor.U2 ||
                        (bufRem.U2 == bufDivisor.U2 &&
                         (bufRem.Low64() > bufDivisor.Low64() ||
                          (bufRem.Low64() == bufDivisor.Low64() && (bufQuo.U0 & 1) != 0))))
                    {
                        goto RoundUp;
                    }
                    break;
                }

            HaveScale96:
                power = kUInt32Powers10[curScale];
                scale += curScale;

                if (IncreaseScale(bufQuo, power) != 0)
                {
                    ThrowDecimalOverflow();
                }

                IncreaseScale(bufRem, power);
                tmp = Div128By96(bufRem, bufDivisor);
                if (!Add32To96(bufQuo, tmp))
                {
                    scale = OverflowUnscale(bufQuo, scale, (bufRem.Low64() | bufRem.High64()) != 0);
                    break;
                }
            }
        }
    }

Unscale:
    if (unscale)
    {
        uint32_t low = bufQuo.U0;
        uint64_t high64 = bufQuo.High64();
        Unscale(low, high64, scale);
        d1.ulo = low;
        d1.umid = (uint32_t)high64;
        d1.uhi = (uint32_t)(high64 >> 32);
    }
    else
    {
        d1.SetLow64(bufQuo.Low64());
        d1.uhi = bufQuo.U2;
    }

    d1.uflags = ((d1.uflags ^ d2.uflags) & kSignMask) | ((uint32_t)scale << kScaleShift);
    return;

RoundUp:
    {
        const uint64_t lo = bufQuo.Low64() + 1;
        bufQuo.SetLow64(lo);
        if (lo == 0 && ++bufQuo.U2 == 0)
        {
            scale = OverflowUnscale(bufQuo, scale, true);
        }
    }
    goto Unscale;
}

// VarDecModFull (DecCalc.cs:2230-2319): full 96-bit-divisor remainder path.
void VarDecModFull(DecCalc& d1, DecCalc& d2, int32_t scale)
{
    // Divisor has bits set in the upper 64 bits.
    //
    // Divisor must be fully normalized (shifted so bit 31 of the most
    // significant uint is 1). The dividend is shifted by the same amount so
    // the quotient is not changed.
    uint32_t tmp = d2.uhi;
    if (tmp == 0)
    {
        tmp = d2.umid;
    }
    const int32_t shift = LzCount(tmp);

    Buf28 b;
    b.Buf24v.SetLow64(d1.Low64() << shift);
    b.Buf24v.SetMid64((d1.umid + ((uint64_t)d1.uhi << 32)) >> (32 - shift));

    // The dividend might need to be scaled up to 221 significant bits.
    uint32_t high = 3;
    while (scale < 0)
    {
        const uint32_t power = scale <= -kMaxInt32Scale ? kTenToPowerNine
                                                        : kUInt32Powers10[-scale];
        uint32_t* buf = reinterpret_cast<uint32_t*>(&b);
        uint64_t tmp64 = (uint64_t)b.Buf24v.U0 * power;
        b.Buf24v.U0 = (uint32_t)tmp64;
        for (int32_t i = 1; i <= (int32_t)high; i++)
        {
            tmp64 >>= 32;
            tmp64 += (uint64_t)buf[i] * power;
            buf[i] = (uint32_t)tmp64;
        }
        // The high bit of the dividend must not be set.
        if (tmp64 > 0x7FFFFFFFull)
        {
            buf[++high] = (uint32_t)(tmp64 >> 32);
        }

        scale += kMaxInt32Scale;
    }

    if (d2.uhi == 0)
    {
        const uint64_t divisor = d2.Low64() << shift;
        switch (high)
        {
        case 6:
            Div96By64(*reinterpret_cast<Buf12*>(&b.Buf24v.U4), divisor);
            [[fallthrough]];
        case 5:
            Div96By64(*reinterpret_cast<Buf12*>(&b.Buf24v.U3), divisor);
            [[fallthrough]];
        case 4:
            Div96By64(*reinterpret_cast<Buf12*>(&b.Buf24v.U2), divisor);
            break;
        }
        Div96By64(*reinterpret_cast<Buf12*>(&b.Buf24v.U1), divisor);
        Div96By64(*reinterpret_cast<Buf12*>(&b.Buf24v.U0), divisor);

        d1.SetLow64(b.Buf24v.Low64() >> shift);
        d1.uhi = 0;
    }
    else
    {
        Buf12 bufDivisor;
        bufDivisor.SetLow64(d2.Low64() << shift);
        bufDivisor.U2 = (uint32_t)((d2.umid + ((uint64_t)d2.uhi << 32)) >> (32 - shift));

        switch (high)
        {
        case 6:
            Div128By96(*reinterpret_cast<Buf16*>(&b.Buf24v.U3), bufDivisor);
            [[fallthrough]];
        case 5:
            Div128By96(*reinterpret_cast<Buf16*>(&b.Buf24v.U2), bufDivisor);
            [[fallthrough]];
        case 4:
            Div128By96(*reinterpret_cast<Buf16*>(&b.Buf24v.U1), bufDivisor);
            break;
        }
        Div128By96(*reinterpret_cast<Buf16*>(&b.Buf24v.U0), bufDivisor);

        d1.SetLow64((b.Buf24v.Low64() >> shift) +
                    ((uint64_t)b.Buf24v.U2 << (32 - shift) << 32));
        d1.uhi = b.Buf24v.U2 >> shift;
    }
}

// VarDecMod (DecCalc.cs:2143-2228): d1 = d1 % d2; d2 trashed.
void VarDecMod(DecCalc& d1, DecCalc& d2)
{
    if ((d2.Low64() | d2.uhi) == 0)
    {
        il2cpp_throw_divide_by_zero_exception(nullptr);
        return;
    }

    if ((d1.Low64() | d1.uhi) == 0)
    {
        return;
    }

    // In the operation x % y the sign of y does not matter. The result has the
    // sign of x.
    d2.uflags = (d2.uflags & ~kSignMask) | (d1.uflags & kSignMask);

    const int32_t cmp = VarDecCmpSub(d1, d2);
    if (cmp == 0)
    {
        d1.SetLow64(0);
        d1.uhi = 0;
        if (d2.uflags > d1.uflags)
        {
            d1.uflags = d2.uflags;
        }
        return;
    }
    if ((cmp ^ (int32_t)(d1.uflags & kSignMask)) < 0)
    {
        return;
    }

    // The divisor is smaller than the dividend and both are non-zero.
    // Calculate the integer remainder using the larger scaling factor.
    int32_t scale = (int8_t)((d1.uflags - d2.uflags) >> kScaleShift);
    if (scale > 0)
    {
        // Divisor scale can always be increased to dividend scale for
        // remainder calculation.
        do
        {
            const uint32_t power = (uint32_t)scale >= kMaxInt32Scale
                                       ? kTenToPowerNine
                                       : kUInt32Powers10[scale];
            uint64_t lo;
            const uint32_t hi32 = (uint32_t)BigMul(d2.Low64(), power, lo);
            d2.SetLow64(lo);
            d2.uhi = hi32 + d2.uhi * power;
        } while ((scale -= kMaxInt32Scale) > 0);
        scale = 0;
    }

    do
    {
        if (scale < 0)
        {
            d1.uflags = d2.uflags;
            // Try to scale up the dividend to match the divisor.
            Buf12 bufQuo = {};
            bufQuo.SetLow64(d1.Low64());
            bufQuo.U2 = d1.uhi;
            do
            {
                const int32_t iCurScale = SearchScale(bufQuo.Low64(), bufQuo.U2,
                                                      kDecScaleMax + scale);
                if (iCurScale == 0)
                {
                    break;
                }
                const uint32_t power = (uint32_t)iCurScale >= kMaxInt32Scale
                                           ? kTenToPowerNine
                                           : kUInt32Powers10[iCurScale];
                scale += iCurScale;
                IncreaseScale(bufQuo, power);
                if (power != kTenToPowerNine)
                {
                    break;
                }
            } while (scale < 0);
            d1.SetLow64(bufQuo.Low64());
            d1.uhi = bufQuo.U2;
        }

        if (d1.uhi == 0)
        {
            d1.SetLow64(d1.Low64() % d2.Low64());
            return;
        }
        else if ((d2.uhi | d2.umid) == 0)
        {
            const uint32_t den = d2.ulo;
            uint64_t tmp = ((uint64_t)d1.uhi << 32) | d1.umid;
            tmp = ((tmp % den) << 32) | d1.ulo;
            d1.SetLow64(tmp % den);
            d1.uhi = 0;
        }
        else
        {
            VarDecModFull(d1, d2, scale);
            return;
        }
    } while (scale < 0);
}

// InternalRound (DecCalc.cs:2322-2438): in-place round to 'scale' fewer
// decimal places with the given mode (0=ToEven, 1=AwayFromZero, 2=ToZero,
// 3=ToNegativeInfinity, 4=ToPositiveInfinity -- the MidpointRounding enum).
void InternalRound(DecCalc& d, uint32_t scale, int32_t mode)
{
    // The scale becomes the desired decimal count.
    d.uflags -= scale << kScaleShift;

    uint32_t remainder = 0, sticky = 0, power;
    // First divide the value by constant 10^9 up to three times.
    while (scale >= kMaxInt32Scale)
    {
        scale -= kMaxInt32Scale;

        const uint32_t divisor = kTenToPowerNine;
        const uint32_t n = d.uhi;
        if (n == 0)
        {
            const uint64_t tmp = d.Low64();
            const uint64_t div = tmp / divisor;
            d.SetLow64(div);
            remainder = (uint32_t)(tmp - div * divisor);
        }
        else
        {
            uint32_t q;
            d.uhi = n / divisor;
            remainder = n - d.uhi * divisor;
            uint32_t m = d.umid;
            if ((m | remainder) != 0)
            {
                q = (uint32_t)((((uint64_t)remainder << 32) | m) / divisor);
                d.umid = q;
                remainder = m - q * divisor;
            }
            m = d.ulo;
            if ((m | remainder) != 0)
            {
                q = (uint32_t)((((uint64_t)remainder << 32) | m) / divisor);
                d.ulo = q;
                remainder = m - q * divisor;
            }
        }
        power = divisor;
        if (scale == 0)
        {
            goto checkRemainder;
        }
        sticky |= remainder;
    }

    {
        power = kUInt32Powers10[scale];
        const uint32_t n = d.uhi;
        if (n == 0)
        {
            uint64_t tmp = d.Low64();
            if (tmp == 0)
            {
                if (mode <= 2) // ToEven / AwayFromZero / ToZero: zero stays
                {
                    goto done;
                }
                remainder = 0;
                goto checkRemainder;
            }
            const uint64_t div = tmp / power;
            d.SetLow64(div);
            remainder = (uint32_t)(tmp - div * power);
        }
        else
        {
            uint32_t q;
            d.uhi = n / power;
            remainder = n - d.uhi * power;
            uint32_t m = d.umid;
            if ((m | remainder) != 0)
            {
                q = (uint32_t)((((uint64_t)remainder << 32) | m) / power);
                d.umid = q;
                remainder = m - q * power;
            }
            m = d.ulo;
            if ((m | remainder) != 0)
            {
                q = (uint32_t)((((uint64_t)remainder << 32) | m) / power);
                d.ulo = q;
                remainder = m - q * power;
            }
        }
    }

checkRemainder:
    if (mode == 2) // ToZero
    {
        goto done;
    }
    else if (mode == 0) // ToEven
    {
        // To do IEEE rounding, add LSB of result to sticky bits so either
        // causes round up if remainder * 2 == last divisor.
        remainder <<= 1;
        if ((sticky | (d.ulo & 1)) != 0)
        {
            remainder++;
        }
        if (power >= remainder)
        {
            goto done;
        }
    }
    else if (mode == 1) // AwayFromZero
    {
        // Round away from zero at the mid point.
        remainder <<= 1;
        if (power > remainder)
        {
            goto done;
        }
    }
    else if (mode == 3) // ToNegativeInfinity
    {
        // Round toward -infinity if we have chopped off a non-zero amount
        // from a negative value.
        if ((remainder | sticky) == 0 || !d.IsNegative())
        {
            goto done;
        }
    }
    else // ToPositiveInfinity
    {
        // Round toward infinity if we have chopped off a non-zero amount from
        // a positive value.
        if ((remainder | sticky) == 0 || d.IsNegative())
        {
            goto done;
        }
    }
    {
        const uint64_t v = d.Low64() + 1;
        if (v == 0)
        {
            d.uhi++;
        }
        d.SetLow64(v);
    }
done:
    return;
}

// DecCalc.GetHashCode (DecCalc.cs:1854-1871) was DELETED here. It is the .NET
// CORE algorithm and is not what net48 returns; its only caller
// (icall_decimal_get_hash_code) now uses DecimalGetHashCodeNet48 instead.
// Deleting rather than keeping it unused also keeps -Wall -Wextra clean.

// Truncate toward zero (Decimal.cs:888-893).
void Truncate(DecCalc& d)
{
    const int32_t flags = (int32_t)d.uflags;
    if ((flags & kScaleMask) != 0)
    {
        InternalRound(d, (uint32_t)((uint8_t)(flags >> kScaleShift)), 2); // ToZero
    }
}

// Decimal.Round private core (Decimal.cs:678-689). D-DR-1 fix (2026-08-13):
// the net48 decimal.Round accepts ONLY MidpointRounding 0 (ToEven) and 1
// (AwayFromZero); 2/3/4 (ToZero/ToNegativeInfinity/ToPositiveInfinity) throw
// ArgumentException -- probe-verified on the reference runtime. The netcore
// modes stay reachable only via Truncate (2) / Floor (3) / Ceiling (4), which
// call InternalRound directly and are untouched.
void RoundCore(DecCalc& d, int32_t decimals, int32_t mode)
{
    if ((uint32_t)decimals > 28)
    {
        il2cpp_throw_argument_out_of_range_exception("decimals", "Value was either too large or too small for an Int32.");
    }
    if ((uint32_t)mode > 1)
    {
        il2cpp_throw_argument_exception("MidpointRounding was not a valid value.");
    }

    const int32_t scale = d.Scale() - decimals;
    if (scale > 0)
    {
        InternalRound(d, (uint32_t)scale, mode);
    }
}

// ToInt32/ToInt64/ToUInt32/ToUInt64 (Decimal.cs:772-867): truncate then range
// check; out of range -> OverflowException.
int32_t ToInt32Core(DecCalc d)
{
    Truncate(d);
    if ((d.uhi | d.umid) == 0)
    {
        if (!d.IsNegative())
        {
            const int32_t i = (int32_t)d.ulo;
            if (i >= 0)
            {
                return i;
            }
        }
        else
        {
            // C#: i = -i; if (i <= 0) return i; -- INT_MIN maps to 0x80000000
            // (unchecked negation). 0u - ulo == the two's-complement of ulo,
            // which is <= 0x80000000 exactly when the wrapped negation is
            // still <= 0.
            if (d.ulo <= 0x80000000u)
            {
                return (int32_t)(0u - d.ulo);
            }
        }
    }
    il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    return 0;
}

int64_t ToInt64Core(DecCalc d)
{
    Truncate(d);
    if (d.uhi == 0)
    {
        if (!d.IsNegative())
        {
            const int64_t l = (int64_t)d.Low64();
            if (l >= 0)
            {
                return l;
            }
        }
        else
        {
            // C#: l = -l; if (l <= 0) return l; -- INT64_MIN maps to
            // 0x8000000000000000 (unchecked negation).
            if (d.Low64() <= 0x8000000000000000ull)
            {
                return (int64_t)(0ull - d.Low64());
            }
        }
    }
    il2cpp_throw_overflow_exception("Value was either too large or too small for an Int64.");
    return 0;
}

uint32_t ToUInt32Core(DecCalc d)
{
    Truncate(d);
    if ((d.uhi | d.umid) == 0)
    {
        const uint32_t i = d.ulo;
        if (!d.IsNegative() || i == 0)
        {
            return i;
        }
    }
    il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt32.");
    return 0;
}

uint64_t ToUInt64Core(DecCalc d)
{
    Truncate(d);
    if (d.uhi == 0)
    {
        const uint64_t l = d.Low64();
        if (!d.IsNegative() || l == 0)
        {
            return l;
        }
    }
    il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt64.");
    return 0;
}

void Load(DecCalc& v, const Il2CppDecimal* d)
{
    std::memcpy(&v, d, sizeof(Il2CppDecimal));
}

void Store(Il2CppDecimal* d, const DecCalc& v)
{
    std::memcpy(d, &v, sizeof(Il2CppDecimal));
}

} // namespace

bool il2cpp_decimal_is_valid_flags(int32_t flags)
{
    return (flags & ~(kDecimalSignMask | kDecimalScaleMask)) == 0 &&
           ((uint32_t)(flags & kDecimalScaleMask) <= (28u << kDecimalScaleShift));
}

void icall_decimal_add(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    DecAddSub(x, y, false);
    Store(result, x);
}

void icall_decimal_sub(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    DecAddSub(x, y, true);
    Store(result, x);
}

void icall_decimal_mul(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    VarDecMul(x, y);
    Store(result, x);
}

void icall_decimal_div(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    VarDecDiv(x, y);
    Store(result, x);
}

void icall_decimal_fcall_addsub(Il2CppDecimal* d1, const Il2CppDecimal* d2, int32_t b)
{
    if ((b & 0x80) != 0)
        icall_decimal_sub(d1, d1, d2);
    else
        icall_decimal_add(d1, d1, d2);
}

void icall_decimal_fcall_multiply(Il2CppDecimal* d1, const Il2CppDecimal* d2)
{
    icall_decimal_mul(d1, d1, d2);
}

void icall_decimal_fcall_divide(Il2CppDecimal* d1, const Il2CppDecimal* d2)
{
    icall_decimal_div(d1, d1, d2);
}

void icall_decimal_rem(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    VarDecMod(x, y);
    Store(result, x);
}

int32_t icall_decimal_compare(const Il2CppDecimal* d1, const Il2CppDecimal* d2)
{
    DecCalc a, b;
    Load(a, d1);
    Load(b, d2);
    return VarDecCmp(a, b);
}

bool icall_decimal_equals(const Il2CppDecimal* d1, const Il2CppDecimal* d2)
{
    DecCalc a, b;
    Load(a, d1);
    Load(b, d2);
    return DecEquals(a, b);
}

void icall_decimal_negate(Il2CppDecimal* result, const Il2CppDecimal* a)
{
    DecCalc x;
    Load(x, a);
    x.uflags ^= kDecimalSignMask;
    Store(result, x);
}

// p0conv2 (D1): Convert.ToDecimal(decimal) is the IDENTITY (net48 returns the
// operand unchanged -- scale and sign preserved, no rounding, no throw).
void icall_decimal_copy(Il2CppDecimal* result, const Il2CppDecimal* a)
{
    result->flags = a->flags;
    result->hi32 = a->hi32;
    result->lo64 = a->lo64;
}

// LOUD P0-19: System.Math's decimal overloads (Abs/Max/Min/Sign). Abs is the
// net48 Decimal.Abs (sign flag cleared, scale/mantissa untouched). Max/Min
// follow net48's `val1 < val2 ? val2 : val1` / `val1 < val2 ? val1 : val2`
// exactly: the winner's SCALE is preserved, so a NUMERIC tie still has an
// observable representation -- Max returns the FIRST operand, Min the second
// (probe-verified against the net48 oracle: Max(1.0m,1.00m)=1.0m,
// Max(1.00m,1.0m)=1.00m, Min(1.0m,1.00m)=1.00m, Min(1.00m,1.0m)=1.0m).
void icall_decimal_abs(Il2CppDecimal* result, const Il2CppDecimal* a)
{
    DecCalc x;
    Load(x, a);
    x.uflags &= ~kSignMask;
    Store(result, x);
}

void icall_decimal_max(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    if (VarDecCmp(x, y) >= 0)
        Store(result, x);
    else
        Store(result, y);
}

void icall_decimal_min(Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
{
    DecCalc x, y;
    Load(x, a);
    Load(y, b);
    if (VarDecCmp(x, y) < 0)
        Store(result, x);
    else
        Store(result, y);
}

// Math.Sign(decimal): 0 for a zero mantissa (signed zero included), else the
// sign flag's -1/+1.
int32_t icall_decimal_sign(const Il2CppDecimal* a)
{
    DecCalc x;
    Load(x, a);
    if ((x.Low64() | x.uhi) == 0)
        return 0;
    return ((int32_t)x.uflags >> 31) | 1;
}

void icall_decimal_round(Il2CppDecimal* result, const Il2CppDecimal* d, int32_t decimals, int32_t mode)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, decimals, mode);
    Store(result, v);
}

// Math.Round(decimal, MidpointRounding): decimals defaults to 0 (net48
// Math.Round forwards decimal.Round(d, 0, mode)). RoundCore keeps the mode
// contract: 0 ToEven / 1 AwayFromZero, anything else ArgumentException.
void icall_decimal_round_mode(Il2CppDecimal* result, const Il2CppDecimal* d, int32_t mode)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, mode);
    Store(result, v);
}

void icall_decimal_truncate(Il2CppDecimal* result, const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    Truncate(v);
    Store(result, v);
}

void icall_decimal_floor(Il2CppDecimal* result, const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    if ((v.uflags & kScaleMask) != 0)
    {
        InternalRound(v, (uint32_t)v.Scale(), 3); // ToNegativeInfinity
    }
    Store(result, v);
}

void icall_decimal_ceiling(Il2CppDecimal* result, const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    if ((v.uflags & kScaleMask) != 0)
    {
        InternalRound(v, (uint32_t)v.Scale(), 4); // ToPositiveInfinity
    }
    Store(result, v);
}

void icall_decimal_ctor_int32(Il2CppDecimal* out, int32_t value)
{
    if (value >= 0)
    {
        out->flags = 0;
        out->lo64 = (uint32_t)value;
    }
    else
    {
        out->flags = kDecimalSignMask;
        out->lo64 = 0u - (uint32_t)value;
    }
    out->hi32 = 0;
}

void icall_decimal_ctor_uint32(Il2CppDecimal* out, uint32_t value)
{
    out->flags = 0;
    out->lo64 = value;
    out->hi32 = 0;
}

void icall_decimal_ctor_int64(Il2CppDecimal* out, int64_t value)
{
    if (value >= 0)
    {
        out->flags = 0;
        out->lo64 = (uint64_t)value;
    }
    else
    {
        out->flags = kDecimalSignMask;
        out->lo64 = 0ull - (uint64_t)value;
    }
    out->hi32 = 0;
}

void icall_decimal_ctor_uint64(Il2CppDecimal* out, uint64_t value)
{
    out->flags = 0;
    out->lo64 = value;
    out->hi32 = 0;
}

void icall_decimal_ctor_5(Il2CppDecimal* out, int32_t lo, int32_t mid,
                          int32_t hi, bool isNegative, uint8_t scale)
{
    if (scale > 28)
    {
        il2cpp_throw_argument_out_of_range_exception("scale", "scale must be between 0 and 28.");
        return;
    }
    out->lo64 = (uint32_t)lo | ((uint64_t)(uint32_t)mid << 32);
    out->hi32 = (uint32_t)hi;
    out->flags = ((int32_t)scale) << 16;
    if (isNegative)
    {
        out->flags |= kDecimalSignMask;
    }
}

int32_t icall_decimal_to_int32(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    return ToInt32Core(v);
}

int64_t icall_decimal_to_int64(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    return ToInt64Core(v);
}

uint32_t icall_decimal_to_uint32(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    return ToUInt32Core(v);
}

uint64_t icall_decimal_to_uint64(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    return ToUInt64Core(v);
}

//------------------------------------------------------------------------------
// p0conv2 (D2): System.Convert's ROUNDING decimal -> integer conversions.
//
// Convert.ToInt32/ToInt64/ToUInt32/ToUInt64(decimal) round to the nearest
// integer with MIDPOINT TO EVEN, then range-check the ROUNDED value -- they are
// NOT the truncating explicit-cast (op_Explicit) conversions above. Probe-pinned
// on the net48 oracle (see decimal.h):
//   ToInt32(3.5m) == 4 (truncation: 3), ToInt32(12.9m) == 13 (truncation: 12),
//   ToInt32(-12.5m) == -12, ToInt32(-0.9m) == -1,
//   ToInt32(2147483647.6m) -> OverflowException (the ROUNDED value is 2^31),
//   ToInt32(-2147483647.5m) == -2147483648 (rounds INTO range),
//   ToUInt32(-0.5m) == 0 (rounds to a negative zero -> zero, no throw).
//
// RoundCore(v, 0, 0) is exactly DecCalc.InternalRound(mode ToEven) down to zero
// decimals, which is what net48's Convert path performs through
// Decimal.Round(value, 0) before the range test; the ToXxxCore helpers then
// apply the same range checks (and the same OverflowException) the truncating
// entries use.
//------------------------------------------------------------------------------
int32_t icall_decimal_to_int32_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0); // 0 decimals, MidpointRounding.ToEven
    return ToInt32Core(v);
}

int64_t icall_decimal_to_int64_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    return ToInt64Core(v);
}

uint32_t icall_decimal_to_uint32_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    return ToUInt32Core(v);
}

uint64_t icall_decimal_to_uint64_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    return ToUInt64Core(v);
}

// Batch 6 (conv-narrow): the narrow integral Convert targets. Round half-to-even
// to an integer first (RoundCore), then range-check the NARROW target. The
// signed forms range-check the rounded int64; the unsigned forms use
// ToUInt64Core (a negative value throws OverflowException there).
int32_t icall_decimal_to_byte_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    const uint64_t r = ToUInt64Core(v);
    if (r > 255)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a Byte.");
    }
    return static_cast<int32_t>(static_cast<uint8_t>(r));
}

int32_t icall_decimal_to_sbyte_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    const int64_t r = ToInt64Core(v);
    if (r < -128 || r > 127)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a SByte.");
    }
    return static_cast<int32_t>(static_cast<int8_t>(r));
}

int32_t icall_decimal_to_int16_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    const int64_t r = ToInt64Core(v);
    if (r < -32768 || r > 32767)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int16.");
    }
    return static_cast<int32_t>(static_cast<int16_t>(r));
}

int32_t icall_decimal_to_uint16_round(const Il2CppDecimal* d)
{
    DecCalc v;
    Load(v, d);
    RoundCore(v, 0, 0);
    const uint64_t r = ToUInt64Core(v);
    if (r > 65535)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for a UInt16.");
    }
    return static_cast<int32_t>(static_cast<uint16_t>(r));
}

// Forward declarations: both live in the floating-point section further down
// (the net48 conversion + hash pair), but the hash icall above them needs them.
double DecimalToDoubleBits(const Il2CppDecimal* d);
int32_t DecimalGetHashCodeNet48(const Il2CppDecimal* d);

// net48 GetHashCode. The DecCalc.GetHashCode algorithm that used to live here
// (DecCalc.cs:1854-1871: Unscale canonicalization + xor of the four dwords) is
// the .NET CORE algorithm and produced values that disagree with .NET
// Framework 4.8 (e.g. 1.5m: 65551 here vs 1073217536 on net48), which silently
// broke Dictionary<decimal,T> / HashSet<decimal> lookups. The port now follows
// the net48 algorithm -- see DecimalGetHashCodeNet48 (decimal.cpp) for the
// reverse-engineered definition and the probe evidence. Deliberately NOT
// canonicalizing the scale: net48 does not either.
int32_t icall_decimal_get_hash_code(const Il2CppDecimal* d)
{
    return DecimalGetHashCodeNet48(d);
}

//------------------------------------------------------------------------------
// Floating-point conversions (the netcore21+ audit closure -- the v1-deferred
// VarDecFromR4/R8 + VarR4/R8FromDec pair).
//
// double -> decimal: the VarDecFromR8 contract rounds to 15 significant
// decimal digits, then expresses the value as mantissa * 10^-scale with
// trailing zeros stripped (so (decimal)2.5 has scale 1, not 14). Values whose
// rounded magnitude exceeds the 96-bit/scale-28 envelope throw
// OverflowException (the oleaut contract); NaN/Infinity likewise.
//
// float -> decimal: the VarDecFromR4 contract rounds to 7 significant decimal
// digits (FLT_DIG), NOT 15. p0conv2 probe-pinned on net48:
//   Convert.ToDecimal(0.1f)      == 0.1m          (scale 1)
//   Convert.ToDecimal(1f/3f)     == 0.3333333m    (scale 7)
//   Convert.ToDecimal(16777216f) == 16777220m     (7 digits)
//   Convert.ToDecimal(1e-28f)    == 1E-28m        (scale 28)
//   Convert.ToDecimal(1e30f)     -> OverflowException
// Routing the float through the double path (15 digits) would return
// 0.100000001490116m -- a silently different VALUE, so ctor_float has its own
// digit budget.
//
// decimal -> double/float: exact decomposition hi32*2^64 + lo64 (both terms
// IEEE-exact) divided by 10^scale -- nearest-double within <=1 ulp of the
// true quotient (the documented VarR8FromDec precision envelope).
//------------------------------------------------------------------------------
void CtorFromFloating(Il2CppDecimal* out, double value, int significantDigits)
{
    std::memset(out, 0, sizeof(Il2CppDecimal));
    if (value == 0.0)
    {
        if (std::signbit(value)) out->flags = kDecimalSignMask;
        return;
    }
    if (std::isnan(value) || std::isinf(value))
    {
        ThrowDecimalOverflow();
    }
    const bool neg = std::signbit(value) != 0;
    const double a = std::fabs(value);

    // Round to 'significantDigits' significant digits ("%.<n-1>E" == one
    // leading digit + n-1 fraction digits), then decompose into an integer
    // digit string and a power-of-ten scale.
    char fmt[8];
    std::snprintf(fmt, sizeof fmt, "%%.%dE", significantDigits - 1);
    char buf[48];
    std::snprintf(buf, sizeof buf, fmt, a);
    char digits[24];
    digits[0] = buf[0];                       // the leading digit
    for (int i = 2; i < significantDigits + 1; ++i) digits[i - 1] = buf[i]; // skip the dot at [1]
    digits[significantDigits] = 0;
    const int e10 = std::atoi(buf + significantDigits + 2); // after 'E'

    int32_t scale = (significantDigits - 1) - e10; // value = DigitsInt * 10^-scale
    int di = significantDigits;
    while (scale > 0 && di > 1 && digits[di - 1] == '0') { digits[--di] = 0; --scale; }

    // Build the canonical fixed-point form for the parser.
    char plain[64];
    int p = 0;
    if (neg) plain[p++] = '-';
    if (scale <= 0)
    {
        std::memcpy(plain + p, digits, static_cast<size_t>(di)); p += di;
        for (int z = 0; z < -scale; ++z) plain[p++] = '0';
        plain[p] = 0;
    }
    else if (di <= scale)
    {
        plain[p++] = '0'; plain[p++] = '.';
        for (int z = 0; z < scale - di; ++z) plain[p++] = '0';
        std::memcpy(plain + p, digits, static_cast<size_t>(di)); p += di;
        plain[p] = 0;
    }
    else
    {
        std::memcpy(plain + p, digits, static_cast<size_t>(di - scale)); p += di - scale;
        plain[p++] = '.';
        std::memcpy(plain + p, digits + di - scale, static_cast<size_t>(scale)); p += scale;
        plain[p] = 0;
    }
    icall_decimal_parse(out, il2cpp_string_new_from_utf8(plain));
}

void icall_decimal_ctor_double(Il2CppDecimal* out, double value)
{
    CtorFromFloating(out, value, 15); // VarDecFromR8: 15 significant digits
}

void icall_decimal_ctor_float(Il2CppDecimal* out, float value)
{
    // VarDecFromR4: 7 significant digits (FLT_DIG) -- see the block comment
    // above CtorFromFloating. Widening the float to double and taking 15
    // digits would expose the binary expansion (0.1f -> 0.100000001490116m).
    CtorFromFloating(out, static_cast<double>(value), 7);
}


//------------------------------------------------------------------------------
// net48 decimal -> double / decimal -> float (VarR8FromDec / VarR4FromDec).
//
// SEMANTIC BASELINE IS .NET FRAMEWORK 4.8, and its conversion is NOT the
// "exact decomposition / 10^scale" form the previous implementation used:
//
//   M = (double)hi32 * 2^64 + (double)lo64   // exact: both terms are exact
//                                            // (hi32 <= 2^32-1, lo64 < 2^64),
//                                            // so this is ONE rounding
//   d = M / T10[scale]                       // ONE more rounding
//   d = -d when the sign bit is set          // -0.0 for negative zero
//
// T10[] holds the CORRECTLY ROUNDED doubles of 10^scale. It is a hardcoded
// table on purpose: neither the platform libm nor .NET's Math.Pow is
// correctly rounded at every scale (Math.Pow(10,23) is 1 ulp HIGH, and
// MinGW's std::pow disagrees with .NET's at several scales), so computing
// the power at run time would make the result compiler-dependent -- exactly
// the class of silent drift this port must not have.
//
// Evidence (probe: tests/cases/dhashprobe/dhashfit9.cs, net48 oracle):
//   * 2,000,000 adversarial (lo,mid,hi,scale,sign) samples:
//     doubleBad=0 floatBad=0.
//   * the previous form (M / std::pow(10, scale)) was wrong for 65,136 of
//     2,000,000 samples (3.26%), always by 1 ulp, and concentrated at
//     scale >= 23 where pow(10,scale) itself stops being exact.
//   * decimal -> float needs no separate path: net48's (float)d is
//     bit-identical to (float)of this double on 2.5M samples (the float
//     conversion is one rounding from the same value), so to_float stays a
//     narrowing of to_double.
//------------------------------------------------------------------------------
constexpr uint64_t kPow10Bits[29] = {
    0x3FF0000000000000ull, 0x4024000000000000ull, 0x4059000000000000ull,
    0x408F400000000000ull, 0x40C3880000000000ull, 0x40F86A0000000000ull,
    0x412E848000000000ull, 0x416312D000000000ull, 0x4197D78400000000ull,
    0x41CDCD6500000000ull, 0x4202A05F20000000ull, 0x42374876E8000000ull,
    0x426D1A94A2000000ull, 0x42A2309CE5400000ull, 0x42D6BCC41E900000ull,
    0x430C6BF526340000ull, 0x4341C37937E08000ull, 0x4376345785D8A000ull,
    0x43ABC16D674EC800ull, 0x43E158E460913D00ull, 0x4415AF1D78B58C40ull,
    0x444B1AE4D6E2EF50ull, 0x4480F0CF064DD592ull, 0x44B52D02C7E14AF6ull,
    0x44EA784379D99DB4ull, 0x45208B2A2C280291ull, 0x4554ADF4B7320335ull,
    0x4589D971E4FE8402ull, 0x45C027E72F1F1281ull,
};

double DecimalToDoubleBits(const Il2CppDecimal* d)
{
    const int32_t scale = (d->flags >> kDecimalScaleShift) & 0xFF;
    const bool neg = (d->flags & kDecimalSignMask) != 0;
    // hi32*2^64 is exact (a 32-bit integer scaled by a power of two);
    // (double)lo64 rounds once; the sum is exact because the two exponents are
    // far enough apart that the significands cannot overlap. This is the net48
    // shape: ONE rounding for the mantissa, ONE for the divide.
    double m = std::ldexp(static_cast<double>(d->hi32), 64) +
               static_cast<double>(d->lo64);
    if (scale > 0)
    {
        double divisor = 0.0;
        std::memcpy(&divisor, &kPow10Bits[scale], sizeof divisor);
        m /= divisor;
    }
    return neg ? -m : m;
}

// net48 GetHashCode (the native CLR's Decimal.GetHashCode, NOT .NET Core's
// DecCalc.GetHashCode). Reverse-engineered with a net48 oracle over 54 named
// values + 400,000 per-scale + 2,000,000 adversarial samples:
//
//   bits = IEEE-754 bits of (double)d
//   hash = mantissa == 0 ? 0 : (int)((bits >> 32) ^ (int)(bits & 0xFFFFFFF0))
//
// Notes on the exact shape (each one is probe-pinned):
//   * The 0xFFFFFFF0 mask on the LOW dword is what makes this decimal-specific.
//     Without the mask it degenerates to Double.GetHashCode(), which disagrees
//     with net48 on 27 of the 54 named values (e.g. 0.1m: -1507852279 vs
//     -1507852285).
//   * The mantissa-zero test comes FIRST, so -0.0m / decimal.Negate(0m) /
//     new decimal(0,0,0,true,0) hash to 0 rather than to (int)0x80000000.
//   * The hash is NOT value-canonical. net48 hashes the ROUNDED DOUBLE of the
//     stored (mantissa, scale) pair, so a large mantissa written with one extra
//     trailing zero (mathematically the same value) can hash differently:
//     m@scale14 -> -1803220006 but (m*10)@scale15 -> -1803220054,
//     probe-verified. We reproduce that defect verbatim; "canonicalizing" it
//     with the DecCalc Unscale step would be a silent wrong value, and
//     Dictionary<decimal,T> / HashSet<decimal> bucket on this number.
//   * .NET Core's DecCalc.GetHashCode (unscale + xor of the four dwords) is a
//     DIFFERENT algorithm and is deliberately not used: the project's semantic
//     baseline is net48.
int32_t DecimalGetHashCodeNet48(const Il2CppDecimal* d)
{
    if (d->hi32 == 0 && d->lo64 == 0)
    {
        return 0;
    }
    const double value = DecimalToDoubleBits(d);
    uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof bits);
    return (int32_t)((uint32_t)(bits >> 32) ^ ((uint32_t)bits & 0xFFFFFFF0u));
}

double icall_decimal_to_double(const Il2CppDecimal* d)
{
    return DecimalToDoubleBits(d);
}

float icall_decimal_to_float(const Il2CppDecimal* d)
{
    // net48's decimal->float is bit-identical to narrowing this double
    // (probe: 2,500,000 samples, 0 mismatches), so there is no separate
    // VarR4FromDec path to reproduce.
    return static_cast<float>(icall_decimal_to_double(d));
}

} // namespace runtime
} // namespace il2cpp
