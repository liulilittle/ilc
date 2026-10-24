#include "il2cpp_math.h"
#include "il2cpp_exception_ext.h"

#include "il2cpp_math_c99.h" // GCC 8.1 libstdc++ C99-math <cmath> shim

#include <cmath>
#include <cstring>

#if defined(_WIN32)
#include <windows.h> // GetProcAddress / LoadLibraryA (the real CRT pow)
#endif

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Math.Abs(int32) / Math.Abs(int64).
//
// .NET contract: the minimum twos-complement value cannot be negated, so
// Abs(int.MinValue) / Abs(long.MinValue) throw OverflowException (with the
// net48 message text); every other value returns its magnitude. The check is
// arithmetic-exact (no UB): INT32_MIN is compared, never negated. The throw
// goes through the canonical factory (il2cpp_exception_ext.h) -- its chain
// repeats the exact type so catch(OverflowException) matches, then lists the
// .NET bases (ArithmeticException, SystemException, Exception).
//------------------------------------------------------------------------------
// net48 renders this OverflowException message from the CLR resource in the
// OS UI culture. On the project's zh-CN oracle host the text is
// "\u5BF9 2 \u7684\u8865\u6570\u7684\u6700\u5C0F\u503C\u6C42\u53CD\u7684\u64CD\u4F5C\u65E0\u6548\u3002"
// ("Negating the minimum value of a twos complement number is invalid.",
// UTF-8 bytes E5AFB9 20 32 20 ... E38082 -- pinned by the mathabs1 oracle
// run). Encoded as UTF-8 byte escapes so the source stays
// encoding-independent, the exceptionext.cpp convention.
static const char* kNegateTwosComplementMessage =
    "\xE5\xAF\xB9\x20\x32\x20\xE7\x9A\x84\xE8\xA1\xA5\xE6\x95\xB0\xE7\x9A\x84"
    "\xE6\x9C\x80\xE5\xB0\x8F\xE5\x80\xBC\xE6\xB1\x82\xE5\x8F\x8D\xE7\x9A\x84"
    "\xE6\x93\x8D\xE4\xBD\x9C\xE6\x97\xA0\xE6\x95\x88\xE3\x80\x82";

int32_t icall_math_abs_i(int32_t value)
{
    if (value == INT32_MIN)
    {
        il2cpp_throw_overflow_exception(kNegateTwosComplementMessage);
    }
    return (value < 0) ? -value : value;
}

int64_t icall_math_abs_l(int64_t value)
{
    if (value == INT64_MIN)
    {
        il2cpp_throw_overflow_exception(kNegateTwosComplementMessage);
    }
    return (value < 0) ? -value : value;
}

// Math.Abs(sbyte) / Math.Abs(short): the net48 narrow overloads.
//
// The old mapping routed every non-int32/non-int64 width to
// icall_math_abs_d, which read the int32 argument slot through a `double`
// parameter -- the mathabs1 oracle probe printed Abs((sbyte)-127) == 0 in the
// runtime against 127 on net48 (and Abs((short)-32767) == 0 against 32767).
// net48 implements the narrow overloads by sign-extending to int32 and calling
// Abs(int32), so the minimum values throw the SAME OverflowException
// (mathabs1: Abs(sbyte.MinValue)/Abs(short.MinValue) both throw).
int32_t icall_math_abs_sb(int32_t value)
{
    if (value == INT8_MIN)
    {
        il2cpp_throw_overflow_exception(kNegateTwosComplementMessage);
    }
    return (value < 0) ? -value : value;
}

int32_t icall_math_abs_sh(int32_t value)
{
    if (value == INT16_MIN)
    {
        il2cpp_throw_overflow_exception(kNegateTwosComplementMessage);
    }
    return (value < 0) ? -value : value;
}

//------------------------------------------------------------------------------
// Math.Max / Math.Min (int32 and int64).
//
// net48 contract: direct comparison, first operand wins ties.
//   Max(a,b) = (a >= b) ? a : b      Min(a,b) = (a <= b) ? a : b
// Identical results for both widths; only the parameter/return width differs.
//------------------------------------------------------------------------------
int32_t icall_math_max_i(int32_t a, int32_t b)
{
    return (a >= b) ? a : b;
}

int32_t icall_math_min_i(int32_t a, int32_t b)
{
    return (a <= b) ? a : b;
}

int64_t icall_math_max_l(int64_t a, int64_t b)
{
    return (a >= b) ? a : b;
}

int64_t icall_math_min_l(int64_t a, int64_t b)
{
    return (a <= b) ? a : b;
}

//------------------------------------------------------------------------------
// Math.Max / Math.Min (uint / ulong).
//
// The transpiler carries uint32 in an int32 slot and uint64 in an int64 slot
// (the M4 ABI), so the icalls take the SIGNED carrier and compare the
// UNSIGNED reinterpretation. The result is returned in the same carrier; the
// call-site consumer applies the unsigned interpretation (the same pattern as
// icall_unbox_uint / icall_unbox_ulong). net48: Max(0u, uint.MaxValue) ==
// uint.MaxValue (a signed compare would return 0).
//------------------------------------------------------------------------------
int32_t icall_math_max_u(int32_t a, int32_t b)
{
    return (static_cast<uint32_t>(a) >= static_cast<uint32_t>(b)) ? a : b;
}

int32_t icall_math_min_u(int32_t a, int32_t b)
{
    return (static_cast<uint32_t>(a) <= static_cast<uint32_t>(b)) ? a : b;
}

int64_t icall_math_max_ul(int64_t a, int64_t b)
{
    return (static_cast<uint64_t>(a) >= static_cast<uint64_t>(b)) ? a : b;
}

int64_t icall_math_min_ul(int64_t a, int64_t b)
{
    return (static_cast<uint64_t>(a) <= static_cast<uint64_t>(b)) ? a : b;
}

//------------------------------------------------------------------------------
// Math.Sign(int32) / Math.Sign(int64).
//
// .NET contract: -1 for negatives, 1 for positives, 0 for zero. NEVER throws
// -- unlike Abs, Sign of the minimum value is defined (-1). The .NET return
// type is int32 for BOTH overloads (long input still yields int).
//------------------------------------------------------------------------------
int32_t icall_math_sign_i(int32_t value)
{
    return (value < 0) ? -1 : (value > 0) ? 1 : 0;
}

int32_t icall_math_sign_l(int64_t value)
{
    return (value < 0) ? -1 : (value > 0) ? 1 : 0;
}

// Math.Sign(double/float): the old mapping routed R4/R8 to sign_l,
// TRUNCATING the fraction before the sign test (Sign(-0.5) wrongly 0). The
// net48 Reference Source is:
//   if (value < 0) return -1;
//   if (value > 0) return 1;
//   if (value == 0) return 0;      // signed zeros both compare equal
//   throw new ArithmeticException("Function does not accept floating point
//                                 Not-a-Number values.");
// (mathminmax1 oracle: Sign(double.NaN)/Sign(float.NaN) throw
// ArithmeticException; the message matches the MathF.Sign icall.)
int32_t icall_math_sign_d(double value)
{
    if (value < 0.0)
    {
        return -1;
    }
    if (value > 0.0)
    {
        return 1;
    }
    if (value == 0.0)
    {
        return 0;
    }
    il2cpp_throw_new("System.ArithmeticException,System.ArithmeticException,System.SystemException,System.Exception",
                     "Function does not accept floating point Not-a-Number values.");
}

//------------------------------------------------------------------------------
// Math.Clamp(int32,int32,int32).
//
// .NET contract: value coerced into [min, max]; min > max is a programming
// error and throws ArgumentException (net48 has no Math.Clamp -- added in
// .NET Core 2.0 -- so this follows the .NET Core contract; the net48 e2e
// case emulates it with a private helper for the reference baseline).
//------------------------------------------------------------------------------

// net9/10 E2E campaign (math sweep): Clamp for int64/double -- same
// contract as the int32 form (min > max -> ArgumentException; NaN value
// returns NaN for the double form per .NET).
int64_t icall_math_clamp_l(int64_t value, int64_t min, int64_t max)
{
    if (min > max)
    {
        il2cpp_throw_argument_exception("min cannot be greater than max");
    }
    if (value < min) return min;
    if (value > max) return max;
    return value;
}

double icall_math_clamp_d(double value, double min, double max)
{
    if (min > max)
    {
        il2cpp_throw_argument_exception("min cannot be greater than max");
    }
    if (value < min) return min;
    if (value > max) return max;
    return value;
}
int32_t icall_math_clamp_i(int32_t value, int32_t min, int32_t max)
{
    if (min > max)
    {
        il2cpp_throw_argument_exception("min cannot be greater than max");
    }
    if (value < min)
    {
        return min;
    }
    if (value > max)
    {
        return max;
    }
    return value;
}

//------------------------------------------------------------------------------
// Math.Sqrt / Math.Pow: direct libm delegation. std::sqrt of a negative
// operand yields NaN (net48 contract: no exception); std::pow follows
// IEEE-754 for NaN/Infinity operands and satisfies Pow(0,0) == 1 (net48).
//------------------------------------------------------------------------------
double icall_math_sqrt_d(double value)
{
    return std::sqrt(value);
}

//------------------------------------------------------------------------------
// Math.Pow -- the net48 Reference Source contract on top of the Windows CRT
// pow.
//
// The net48 check order (mathfunc1 oracle):
//   if (IsNaN(x) || IsNaN(y)) return NaN;   // BEFORE the y==0 / x==1 exits:
//                                           // Pow(NaN,0) and Pow(1,NaN) are
//                                           // NaN, unlike IEEE pow
//   if (y == 0) return 1;
//   if (x == 1) return 1;
//   if (x == -1 && IsInfinity(y)) return NaN;
//   return CRT pow(x, y);
//
// The CRT pow is NOT std::pow on MinGW: libmsvcrt.a ships an exp/log
// emulation that differs by 1 ulp from the real msvcrt.dll pow on oracle
// probes (pow(10,23) ...671 vs ...670; pow(1.5,1000) ...457 vs ...456).
// Resolve the exported msvcrt.dll pow at first use on MinGW. MSVC's
// std::pow already IS the UCRT pow; non-Windows builds keep std::pow.
//------------------------------------------------------------------------------
#if defined(_WIN32) && defined(__GNUC__)
typedef double (*MsvcrtPowFn)(double, double);

static MsvcrtPowFn ResolveMsvcrtPow()
{
    static MsvcrtPowFn fn = nullptr;
    static bool resolved = false;
    if (!resolved)
    {
        HMODULE module = ::LoadLibraryA("msvcrt.dll");
        if (module != nullptr)
        {
            FARPROC proc = ::GetProcAddress(module, "pow");
            // FARPROC -> typed function pointer: a direct cast trips
            // -Wcast-function-type on GCC, so copy the representation
            // (both are function pointers of identical size on Win32/x64).
            static_assert(sizeof(fn) == sizeof(proc), "function pointer size mismatch");
            std::memcpy(&fn, &proc, sizeof(fn));
        }
        resolved = true;
    }
    return fn;
}
#endif

static double PlatformPow(double x, double y)
{
#if defined(_WIN32) && defined(__GNUC__)
    MsvcrtPowFn fn = ResolveMsvcrtPow();
    if (fn != nullptr)
    {
        return fn(x, y);
    }
#endif
    return std::pow(x, y);
}

// The net48 negative quiet NaN (the same bit pattern NormalizeNaN in
// mathext.cpp produces; math.cpp does not include that TU's helper).
static double MathNegativeNaN()
{
    const uint64_t kNetNaN = 0xFFF8000000000000ULL;
    double result;
    std::memcpy(&result, &kNetNaN, sizeof(result));
    return result;
}

double icall_math_pow_d(double x, double y)
{
    if (std::isnan(x) || std::isnan(y))
    {
        return MathNegativeNaN();
    }
    if (y == 0.0)
    {
        return 1.0;
    }
    if (x == 1.0)
    {
        return 1.0;
    }
    if (x == -1.0 && std::isinf(y))
    {
        return MathNegativeNaN();
    }
    return PlatformPow(x, y);
}

//------------------------------------------------------------------------------
// Math.Floor: direct libm delegation (std::floor).
//------------------------------------------------------------------------------
double icall_math_floor_d(double value)
{
    return std::floor(value);
}

//------------------------------------------------------------------------------
// Math.Ceiling: the net48 Reference Source form
//
//   double temp = Math.Floor(value);
//   if (temp != value) temp += 1.0;
//   return temp;
//
// NOT std::ceil: for a negative input that rounds up to zero std::ceil returns
// -0.0, while net48's floor+increment produces +0.0 (mathfunc1 ceil_3:
// Ceiling(-0.5) is +0.0). An INTEGRAL input (including -0.0) passes through
// unchanged, so Ceiling(-0.0) stays -0.0. NaN: floor(NaN)=NaN, NaN != NaN ->
// NaN+1 = NaN. Infinities: floor(inf)==inf -> passthrough.
//------------------------------------------------------------------------------
double icall_math_ceiling_d(double value)
{
    const double floored = std::floor(value);
    if (floored != value)
    {
        return floored + 1.0;
    }
    return floored;
}

//------------------------------------------------------------------------------
// Math.Round(double) -- MidpointRounding.ToEven (banker's rounding).
//
// std::round rounds halves AWAY from zero (Round(1.5)=2, Round(2.5)=3) which
// is NOT the .NET default: .NET rounds halves to the nearest EVEN integer
// (Round(2.5)=2, Round(-1.5)=-2). Implemented explicitly:
//
//   fl = floor(value), cl = ceil(value).
//   fl == cl                -> value is integral; return it unchanged.
//   value - fl <  cl - value -> nearer to fl.
//   value - fl >  cl - value -> nearer to cl.
//   equal (exact midpoint)  -> pick the EVEN of the two neighbours
//                              (fmod(fl, 2) == 0 -> fl, else cl).
//
// The fractional comparisons are exact for doubles: for |value| >= 1 the
// subtractions satisfy Sterbenz's lemma (floor(value) <= value <=
// 2*floor(value)), and midpoints are exactly representable, so "equal"
// detects true halves -- never a round-off coincidence. NaN/Infinity flow
// through: NaN fails every comparison and returns ceil(NaN) = NaN;
// infinities hit fl == cl and return the infinity.
//------------------------------------------------------------------------------
double icall_math_round_d(double value)
{
    const double fl = std::floor(value);
    const double cl = std::ceil(value);
    if (fl == cl)
    {
        return fl;
    }
    const double lower = value - fl;
    const double upper = cl - value;
    if (lower < upper)
    {
        return fl;
    }
    if (lower > upper)
    {
        return cl;
    }
    // Exact midpoint: banker's rule -- round to the even neighbour.
    return (std::fmod(fl, 2.0) == 0.0) ? fl : cl;
}

//------------------------------------------------------------------------------
// Math.Abs(double): std::fabs. IEEE-754 conformant: NaN -> NaN,
// -0.0 -> +0.0. (The int32/int64 abs helpers above need the explicit
// overflow check; floating-point abs never overflows.)
//------------------------------------------------------------------------------
double icall_math_abs_d(double value)
{
    return std::fabs(value);
}

//------------------------------------------------------------------------------
// Math.Max / Math.Min (double) -- the net48 Reference Source contract:
//
//   Max(a,b) { if (a > b) return a; if (IsNaN(a)) return a; return b; }
//   Min(a,b) { if (a < b) return a; if (IsNaN(a)) return a; return b; }
//
// The old (a >= b) ? a : b form returned the FIRST operand on a tie, so
// Max(-0.0,+0.0) was -0.0 and Min(+0.0,-0.0) was +0.0 -- the mathminmax1
// oracle shows net48 returns the SECOND operand on ties (+0.0 / -0.0
// respectively). A NaN FIRST operand is returned explicitly (Max(NaN,5) ==
// NaN); a NaN second operand still wins through the fallthrough (Max(5,NaN)
// == NaN). This is .NET Framework 4.8 behaviour; .NET Core 3+ changed to
// IEEE-754-2019 NaN-propagation, which we deliberately do NOT follow because
// the project's reference baseline is net48.
//------------------------------------------------------------------------------
double icall_math_max_d(double a, double b)
{
    if (a > b)
    {
        return a;
    }
    if (a != a) // NaN first operand: net48 returns it
    {
        return a;
    }
    return b;
}

double icall_math_min_d(double a, double b)
{
    if (a < b)
    {
        return a;
    }
    if (a != a) // NaN first operand: net48 returns it
    {
        return a;
    }
    return b;
}

//------------------------------------------------------------------------------
// Shared guarded integer division / remainder core for the DivRem and
// DivModInt icalls. C++ signed division by zero is UB and INT_MIN / -1 is UB
// (the quotient is not representable); .NET throws managed exceptions for
// both -- the net48 oracle (divremprobe) confirms:
//   DivRem(5, 0) -> DivideByZeroException
//   DivRem(int.MinValue, -1) -> OverflowException ("Arithmetic operation
//   resulted in an overflow." -- the canonical factory text).
// The signed divide/mod loop pair is wrapped so every div/mod icall shares
// the SAME check order (divisor == 0 first, then the min/-1 pair) -- the
// .NET order (Math.DivModInt.cs DivInt32 checks divisor first).
//------------------------------------------------------------------------------
static int32_t checked_div_i32(int32_t dividend, int32_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    if (dividend == INT32_MIN && divisor == -1)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return dividend / divisor;
}

static int64_t checked_div_i64(int64_t dividend, int64_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    if (dividend == INT64_MIN && divisor == -1)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return dividend / divisor;
}

static int32_t checked_mod_i32(int32_t dividend, int32_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    if (dividend == INT32_MIN && divisor == -1)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return dividend % divisor;
}

static int64_t checked_mod_i64(int64_t dividend, int64_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    if (dividend == INT64_MIN && divisor == -1)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return dividend % divisor;
}

//------------------------------------------------------------------------------
// Math.DivRem(int,int,out int) / Math.DivRem(long,long,out long): the net48
// public API (returns the quotient, writes the remainder). C++ / and % on the
// signed widths truncate toward zero with the remainder taking the dividend
// sign -- EXACTLY the net48 C# contract (probe: DivRem(-7,3) -> q -2, r -1;
// DivRem(7,-3) -> q -2, r +1). The result slot (remainder) is a pointer
// parameter per the result-first contract; the quotient rides the value
// channel.
//------------------------------------------------------------------------------
int32_t icall_math_divrem_i(int32_t dividend, int32_t divisor, int32_t* remainder)
{
    const int32_t q = checked_div_i32(dividend, divisor);
    *remainder = checked_mod_i32(dividend, divisor);
    return q;
}

int64_t icall_math_divrem_l(int64_t dividend, int64_t divisor, int64_t* remainder)
{
    const int64_t q = checked_div_i64(dividend, divisor);
    *remainder = checked_mod_i64(dividend, divisor);
    return q;
}

//------------------------------------------------------------------------------
// The corert Math.DivModInt internal helpers (div/mod for the four integer
// widths) -- the FCall surface (DivInt32Internal/DivUInt32Internal/
// DivInt64Internal/DivUInt64Internal/ModInt32Internal/ModUInt32Internal/
// ModInt64Internal/ModUInt64Internal in Math.DivModInt.cs, native reference
// nativeaot/Runtime/MathHelpers.cpp). Same guarded checks as the DivRem pair;
// the unsigned forms never overflow (checked_div_u/_mod_u only guard 0).
//------------------------------------------------------------------------------
int32_t icall_math_div_i(int32_t dividend, int32_t divisor)
{
    return checked_div_i32(dividend, divisor);
}

uint32_t icall_math_div_u(uint32_t dividend, uint32_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    return dividend / divisor;
}

int64_t icall_math_div_l(int64_t dividend, int64_t divisor)
{
    return checked_div_i64(dividend, divisor);
}

uint64_t icall_math_div_ul(uint64_t dividend, uint64_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    return dividend / divisor;
}

int32_t icall_math_mod_i(int32_t dividend, int32_t divisor)
{
    return checked_mod_i32(dividend, divisor);
}

uint32_t icall_math_mod_u(uint32_t dividend, uint32_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    return dividend % divisor;
}

int64_t icall_math_mod_l(int64_t dividend, int64_t divisor)
{
    return checked_mod_i64(dividend, divisor);
}

uint64_t icall_math_mod_ul(uint64_t dividend, uint64_t divisor)
{
    if (divisor == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    return dividend % divisor;
}

}} // namespace il2cpp::runtime
