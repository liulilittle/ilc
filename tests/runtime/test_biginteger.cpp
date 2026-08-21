//------------------------------------------------------------------------------
// test_biginteger.cpp -- System.Numerics.BigInteger runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in il2cpp_biginteger.h:
//   1. Construction: from_int32 / from_int64 (positive, negative, zero,
//      INT32_MIN / INT64_MIN).
//   2. Addition / subtraction: same-sign, opposite-sign, zero, negative
//      results, magnitude crossing.
//   3. Multiplication: schoolbook (123456789 * 987654321), negative signs,
//      zero.
//   4. Division / remainder: 100 / 7 = 14 rem 2, negative dividend sign,
//      divide by zero (DivideByZeroException).
//   5. Negation.
//   6. Comparison / equality: sign + magnitude ordering, antisymmetry.
//   7. ToString: decimal representation of known values (2^100, 50!).
//   8. to_int32: in-range truncation, out-of-range (OverflowException).
//   9. Factorial 50! (the taskcard known value): 50! ==
//      30414093201713378043612608166064768844377641568960512000000000000.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_biginteger.h"
#include "il2cpp_exception.h" // DotNetException
#include "il2cpp_string.h"    // Il2CppString

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Runs fn; returns true iff it throws a DotNetException whose type chain
// contains 'typeFragment'.
template <typename Fn>
bool ThrowsWithType(Fn fn, const char* typeFragment)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strstr(e.type_name(), typeFragment) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

il2cpp::runtime::Il2CppBigInteger FromI32(int32_t v)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_from_int32(&r, v);
    return r;
}

il2cpp::runtime::Il2CppBigInteger FromI64(int64_t v)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_from_int64(&r, v);
    return r;
}

il2cpp::runtime::Il2CppBigInteger Add(il2cpp::runtime::Il2CppBigInteger a,
                                      il2cpp::runtime::Il2CppBigInteger b)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_add(&r, a, b);
    return r;
}

il2cpp::runtime::Il2CppBigInteger Sub(il2cpp::runtime::Il2CppBigInteger a,
                                      il2cpp::runtime::Il2CppBigInteger b)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_sub(&r, a, b);
    return r;
}

il2cpp::runtime::Il2CppBigInteger Mul(il2cpp::runtime::Il2CppBigInteger a,
                                      il2cpp::runtime::Il2CppBigInteger b)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_mul(&r, a, b);
    return r;
}

il2cpp::runtime::Il2CppBigInteger Div(il2cpp::runtime::Il2CppBigInteger a,
                                      il2cpp::runtime::Il2CppBigInteger b)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_div(&r, a, b);
    return r;
}

il2cpp::runtime::Il2CppBigInteger Rem(il2cpp::runtime::Il2CppBigInteger a,
                                      il2cpp::runtime::Il2CppBigInteger b)
{
    il2cpp::runtime::Il2CppBigInteger r;
    il2cpp::runtime::icall_biginteger_rem(&r, a, b);
    return r;
}

// The managed string's UTF-16 chars equal the ASCII literal.
bool TextEquals(il2cpp::Il2CppObject* s, const char* expected)
{
    auto* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(expected);
    if (str == nullptr || str->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(expected[i]))
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    using il2cpp::runtime::Il2CppBigInteger;

    // --- 1. Construction ---
    {
        Expect(il2cpp::runtime::icall_biginteger_to_int32(FromI32(0)) == 0,
               "from_int32(0) -> 0");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(FromI32(42)) == 42,
               "from_int32(42) -> 42");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(FromI32(-42)) == -42,
               "from_int32(-42) -> -42");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(FromI32(INT32_MIN)) == INT32_MIN,
               "from_int32(INT32_MIN) round-trips");
        Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(FromI64(INT64_MIN)),
                          "-9223372036854775808"),
               "from_int64(INT64_MIN) constructs as -9223372036854775808");
        Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(FromI64(1234567890123LL)),
                          "1234567890123"),
               "from_int64 large value constructs as 1234567890123");
    }

    // --- 2. Addition / subtraction ---
    {
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Add(FromI32(5), FromI32(3))) == 8,
               "5 + 3 = 8");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Add(FromI32(-5), FromI32(3))) == -2,
               "-5 + 3 = -2");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Add(FromI32(5), FromI32(-8))) == -3,
               "5 + (-8) = -3");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Add(FromI32(0), FromI32(0))) == 0,
               "0 + 0 = 0");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Sub(FromI32(5), FromI32(3))) == 2,
               "5 - 3 = 2");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Sub(FromI32(3), FromI32(5))) == -2,
               "3 - 5 = -2");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Sub(FromI32(-5), FromI32(-3))) == -2,
               "-5 - (-3) = -2");
    }

    // --- 3. Multiplication ---
    {
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Mul(FromI32(6), FromI32(7))) == 42,
               "6 * 7 = 42");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Mul(FromI32(-3), FromI32(4))) == -12,
               "-3 * 4 = -12");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Mul(FromI32(-3), FromI32(-4))) == 12,
               "-3 * -4 = 12");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Mul(FromI32(0), FromI32(5))) == 0,
               "0 * 5 = 0");
        // 123456789 * 987654321 = 121932631112635269 (fits int64).
        {
            const Il2CppBigInteger p = Mul(FromI32(123456789), FromI32(987654321));
            Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(p),
                              "121932631112635269"),
                   "123456789 * 987654321 = 121932631112635269");
        }
    }

    // --- 4. Division / remainder ---
    {
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Div(FromI32(100), FromI32(7))) == 14,
               "100 / 7 = 14");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Rem(FromI32(100), FromI32(7))) == 2,
               "100 % 7 = 2");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Div(FromI32(-100), FromI32(7))) == -14,
               "-100 / 7 = -14 (truncation toward zero)");
        Expect(il2cpp::runtime::icall_biginteger_to_int32(
                   Rem(FromI32(-100), FromI32(7))) == -2,
               "-100 % 7 = -2 (remainder takes the dividend's sign)");
        Expect(ThrowsWithType([&] {
                   Div(FromI32(1), FromI32(0));
               }, "System.DivideByZeroException"),
               "division by zero throws DivideByZeroException");
        Expect(ThrowsWithType([&] {
                   Rem(FromI32(1), FromI32(0));
               }, "System.DivideByZeroException"),
               "remainder by zero throws DivideByZeroException");
    }

    // --- 5. Negation ---
    {
        il2cpp::runtime::Il2CppBigInteger n;
        il2cpp::runtime::icall_biginteger_negate(&n, FromI32(5));
        Expect(il2cpp::runtime::icall_biginteger_to_int32(n) == -5,
               "-(5) = -5");
        il2cpp::runtime::icall_biginteger_negate(&n, FromI32(-5));
        Expect(il2cpp::runtime::icall_biginteger_to_int32(n) == 5,
               "-(-5) = 5");
    }

    // --- 6. Comparison / equality ---
    {
        Expect(il2cpp::runtime::icall_biginteger_compare(FromI32(5), FromI32(3)) == 1,
               "5 > 3");
        Expect(il2cpp::runtime::icall_biginteger_compare(FromI32(3), FromI32(5)) == -1,
               "3 < 5");
        Expect(il2cpp::runtime::icall_biginteger_compare(FromI32(5), FromI32(5)) == 0,
               "5 == 5");
        Expect(il2cpp::runtime::icall_biginteger_compare(FromI32(-5), FromI32(3)) == -1,
               "-5 < 3");
        Expect(il2cpp::runtime::icall_biginteger_compare(FromI32(-5), FromI32(-3)) == -1,
               "-5 < -3");
        Expect(il2cpp::runtime::icall_biginteger_equals(FromI32(5), FromI32(5)),
               "5 equals 5");
        Expect(!il2cpp::runtime::icall_biginteger_equals(FromI32(5), FromI32(6)),
               "5 does not equal 6");
        // 2^100 > 2^99 (magnitude comparison across limb counts).
        {
            Il2CppBigInteger p100 = FromI32(1);
            Il2CppBigInteger p99 = FromI32(1);
            for (int32_t i = 0; i < 100; ++i) { p100 = Mul(p100, FromI32(2)); }
            for (int32_t i = 0; i < 99; ++i) { p99 = Mul(p99, FromI32(2)); }
            Expect(il2cpp::runtime::icall_biginteger_compare(p100, p99) == 1,
                   "2^100 > 2^99");
        }
    }

    // --- 7. ToString ---
    {
        Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(FromI32(0)), "0"),
               "ToString(0) = \"0\"");
        Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(FromI32(-12345)),
                          "-12345"),
               "ToString(-12345) = \"-12345\"");
        // 2^100 = 1267650600228229401496703205376.
        {
            Il2CppBigInteger p = FromI32(1);
            for (int32_t i = 0; i < 100; ++i) { p = Mul(p, FromI32(2)); }
            Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(p),
                              "1267650600228229401496703205376"),
                   "ToString(2^100) = \"1267650600228229401496703205376\"");
        }
    }

    // --- 8. to_int32 overflow ---
    {
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::icall_biginteger_to_int32(FromI64(2147483648LL));
               }, "System.OverflowException"),
               "to_int32(2^31) throws OverflowException");
        Expect(ThrowsWithType([&] {
                   il2cpp::runtime::icall_biginteger_to_int32(FromI64(-2147483649LL));
               }, "System.OverflowException"),
               "to_int32(-2^31 - 1) throws OverflowException");
    }

    // --- 9. Factorial 50! (the taskcard known value) ---
    {
        Il2CppBigInteger fact = FromI32(1);
        for (int32_t i = 2; i <= 50; ++i)
        {
            fact = Mul(fact, FromI32(i));
        }
        Expect(TextEquals(il2cpp::runtime::icall_biginteger_to_string(fact),
                          "30414093201713378043612608166064768844377641568960512000000000000"),
               "50! = 30414093201713378043612608166064768844377641568960512000000000000");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d biginteger assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime biginteger tests passed\n");
    return 0;
}
