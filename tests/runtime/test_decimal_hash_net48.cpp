//------------------------------------------------------------------------------
// test_decimal_hash_net48.cpp -- net48 bit-exactness of the decimal
// GetHashCode / decimal->double / decimal->float surface.
//
// WHY THIS SUITE EXISTS (defect: silent wrong value, HIGH):
//   The old implementation ported .NET Core's DecCalc.GetHashCode
//   (unscale + xor of the four dwords) and produced, e.g., 65551 for 1.5m
//   where .NET Framework 4.8 returns 1073217536 (0x3FF80000). Because
//   Dictionary<decimal,T> / HashSet<decimal> / Hashtable bucket on this hash,
//   the mismatch was user-visible: net48-equal keys landed in different
//   buckets and lookups missed.
//
// THE NET48 ALGORITHM (reverse-engineered; 54-value + 400k per-scale + 2M
// adversarial probe evidence, tests/cases/dhashprobe/):
//
//     bits = the IEEE-754 bits of (double)d
//     return mantissa == 0 ? 0 : (int)((bits >> 32) ^ (int)(bits & 0xFFFFFFF0));
//
//   * The 0xFFFFFFF0 mask on the low dword is what distinguishes this from
//     Double.GetHashCode() -- they agree only when the low nibble is zero.
//   * Negative zero hashes to 0 (mantissa zero), NOT to (int)0x80000000.
//     Probe: -0.0m, decimal.Negate(0m), new decimal(0,0,0,true,0) -> all 0.
//   * The hash is NOT value-canonical for large mantissas: net48 hashes the
//     ROUNDED DOUBLE, so appending a trailing zero to a 96-bit mantissa can
//     change the result. This is a net48 defect reproduced verbatim
//     (section 3) -- canonicalizing it would be a silent wrong value.
//
// decimal -> double (also reverse-engineered; the OLD code divided by
// std::pow(10, scale), which is wrong for 3.26% of random values):
//
//     M = (double)hi32 * 2^64 + (double)lo64     // exact two-term sum
//     d = M / T10[scale]                         // T10 = correctly rounded 10^scale
//     d = -d when the sign bit is set            // yields -0.0 for -0m
//
//   * std::pow / Math.Pow(10, s) is NOT correctly rounded at s = 23, and the
//     MinGW and .NET libms disagree with each other, so T10 is a hardcoded
//     table of correctly rounded bit patterns. That is what makes the port
//     portable across compilers.
//   * decimal -> float needs no special case: net48's (float)d is bit-identical
//     to (float)of the correctly converted double on 2.5M probe samples.
//
// Oracle: every literal below was produced by the .NET Framework 4.8 probe
// tests/cases/dhashprobe/dhashoracle.cs (70 values: scale 0..28, exact and
// inexact, negative, zero, max/min, 96-bit mantissas, trailing-zero variants).
//------------------------------------------------------------------------------

#include "decimal.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

using il2cpp::runtime::Il2CppDecimal;
using il2cpp::runtime::icall_decimal_equals;
using il2cpp::runtime::icall_decimal_get_hash_code;
using il2cpp::runtime::icall_decimal_to_double;
using il2cpp::runtime::icall_decimal_to_float;

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

uint64_t DoubleBits(double d)
{
    uint64_t b = 0;
    std::memcpy(&b, &d, sizeof b);
    return b;
}

uint32_t FloatBits(float f)
{
    uint32_t b = 0;
    std::memcpy(&b, &f, sizeof b);
    return b;
}

struct OracleRow
{
    uint32_t lo, mid, hi, flags;
    int32_t hash;
    uint64_t doubleBits;
    const char* text;
};

const OracleRow kRows[] = {    { 0u, 0u, 0u, 0u, 0, 0x0000000000000000ULL, "0" },
    { 1u, 0u, 0u, 0u, 1072693248, 0x3FF0000000000000ULL, "1" },
    { 1u, 0u, 0u, -2147483648u, -1074790400, 0xBFF0000000000000ULL, "-1" },
    { 2u, 0u, 0u, 0u, 1073741824, 0x4000000000000000ULL, "2" },
    { 7u, 0u, 0u, 0u, 1075576832, 0x401C000000000000ULL, "7" },
    { 10u, 0u, 0u, 0u, 1076101120, 0x4024000000000000ULL, "10" },
    { 100u, 0u, 0u, 0u, 1079574528, 0x4059000000000000ULL, "100" },
    { 1234u, 0u, 0u, 0u, 1083394048, 0x4093480000000000ULL, "1234" },
    { 0u, 2097152u, 0u, 0u, 1128267776, 0x4340000000000000ULL, "9007199254740992" },
    { 1u, 2097152u, 0u, 0u, 1128267776, 0x4340000000000000ULL, "9007199254740993" },
    { 0u, 1u, 0u, 0u, 1106247680, 0x41F0000000000000ULL, "4294967296" },
    { 0u, 0u, 1u, 0u, 1139802112, 0x43F0000000000000ULL, "18446744073709551616" },
    { 15u, 0u, 0u, 65536u, 1073217536, 0x3FF8000000000000ULL, "1.5" },
    { 25u, 0u, 0u, 65536u, 1074003968, 0x4004000000000000ULL, "2.5" },
    { 15u, 0u, 0u, -2147418112u, -1074266112, 0xBFF8000000000000ULL, "-1.5" },
    { 5u, 0u, 0u, 65536u, 1071644672, 0x3FE0000000000000ULL, "0.5" },
    { 25u, 0u, 0u, 131072u, 1070596096, 0x3FD0000000000000ULL, "0.25" },
    { 125u, 0u, 0u, 196608u, 1069547520, 0x3FC0000000000000ULL, "0.125" },
    { 10u, 0u, 0u, 65536u, 1072693248, 0x3FF0000000000000ULL, "1.0" },
    { 100u, 0u, 0u, 131072u, 1072693248, 0x3FF0000000000000ULL, "1.00" },
    { 10000u, 0u, 0u, 131072u, 1079574528, 0x4059000000000000ULL, "100.00" },
    { 1u, 0u, 0u, 65536u, -1507852279, 0x3FB999999999999AULL, "0.1" },
    { 11u, 0u, 0u, 65536u, -1503133687, 0x3FF199999999999AULL, "1.1" },
    { 3u, 0u, 0u, 65536u, 216006659, 0x3FD3333333333333ULL, "0.3" },
    { 7u, 0u, 0u, 65536u, 1501560838, 0x3FE6666666666666ULL, "0.7" },
    { 2u, 0u, 0u, 65536u, -1504706551, 0x3FC999999999999AULL, "0.2" },
    { 4u, 0u, 0u, 65536u, -1505755127, 0x3FD999999999999AULL, "0.4" },
    { 6u, 0u, 0u, 65536u, 214958083, 0x3FE3333333333333ULL, "0.6" },
    { 8u, 0u, 0u, 65536u, -1502609399, 0x3FE999999999999AULL, "0.8" },
    { 9u, 0u, 0u, 65536u, -216006644, 0x3FECCCCCCCCCCCCDULL, "0.9" },
    { 1u, 0u, 0u, -2147418112u, 639631369, 0xBFB999999999999AULL, "-0.1" },
    { 12345678u, 0u, 0u, 262144u, 768585957, 0x40934A456D5CFAADULL, "1234.5678" },
    { 12345678u, 0u, 0u, -2147221504u, -1378897691, 0xC0934A456D5CFAADULL, "-1234.5678" },
    { 123456u, 0u, 0u, 196608u, 1522623327, 0x405EDD2F1A9FBE77ULL, "123.456" },
    { 110u, 0u, 0u, 131072u, -1503133687, 0x3FF199999999999AULL, "1.10" },
    { 1100u, 0u, 0u, 196608u, -1503133687, 0x3FF199999999999AULL, "1.100" },
    { 11000u, 0u, 0u, 262144u, -1503133687, 0x3FF199999999999AULL, "1.1000" },
    { 250u, 0u, 0u, 131072u, 1074003968, 0x4004000000000000ULL, "2.50" },
    { 2500u, 0u, 0u, 196608u, 1074003968, 0x4004000000000000ULL, "2.500" },
    { 10u, 0u, 0u, 131072u, -1507852279, 0x3FB999999999999AULL, "0.10" },
    { 100u, 0u, 0u, 196608u, -1507852279, 0x3FB999999999999AULL, "0.100" },
    { 1u, 0u, 0u, 655360u, -468926097, 0x3DDB7CDFD9D7BDBBULL, "0.0000000001" },
    { 1u, 0u, 0u, 1835008u, -2075143962, 0x3A1FB0F6BE50601AULL, "0.0000000000000000000000000001" },
    { 2u, 0u, 0u, 1835008u, -2071998234, 0x3A2FB0F6BE50601AULL, "0.0000000000000000000000000002" },
    { 3u, 0u, 0u, 1835008u, 881560745, 0x3A37C4B90EBC4813ULL, "0.0000000000000000000000000003" },
    { 1u, 0u, 0u, -2145648640u, 72339686, 0xBA1FB0F6BE50601AULL, "-0.0000000000000000000000000001" },
    { 268435457u, 1042612833u, 542101086u, 1835008u, 1072693248, 0x3FF0000000000000ULL, "1.0000000000000000000000000001" },
    { -1u, -1u, -1u, 0u, 1173356544, 0x45F0000000000000ULL, "79228162514264337593543950335" },
    { -1u, -1u, -1u, -2147483648u, -974127104, 0xC5F0000000000000ULL, "-79228162514264337593543950335" },
    { 748637063u, -245828952u, 6692605u, 0u, 957332255, 0x455987BF7C563CAAULL, "123456789012345678901234567" },
    { -1103563954u, 1836677777u, 66926059u, 0u, 518267519, 0x458FE9AF5B6BCBD5ULL, "1234567890123456789012345678" },
    { 1849262357u, 1186908593u, 669260594u, 0u, -589255315, 0x45C3F20D99235F65ULL, "12345678901234567890123456789" },
    { 268435456u, 1042612833u, 542101086u, 0u, 1793013095, 0x45C027E72F1F1281ULL, "10000000000000000000000000000" },
    { 1102470952u, 185874565u, 1703060790u, 1835008u, 340593899, 0x400921FB54442D18ULL, "3.1415926535897932384626433832" },
    { -2055541415u, -336798155u, 1473583531u, 1835008u, -888018838, 0x4005BF0A8B14576AULL, "2.7182818284590452353602874713" },
    { 89478485u, 347537611u, 180700362u, 1835008u, 1786773509, 0x3FD5555555555556ULL, "0.3333333333333333333333333333" },
    { 1849262357u, 1186908593u, 669260594u, 1835008u, 2105514298, 0x3FF3C0CA428C59FCULL, "1.2345678901234567890123456789" },
    { 1821814770u, -1692462944u, 6692u, 1507328u, 2105514298, 0x3FF3C0CA428C59FCULL, "1.23456789012345678901234" },
    { 1038278521u, 255239748u, 66926u, 1572864u, 2105514298, 0x3FF3C0CA428C59FCULL, "1.234567890123456789012345" },
    { 1792850624u, -1742569814u, 669260u, 1638400u, 2105514298, 0x3FF3C0CA428C59FBULL, "1.2345678901234567890123456" },
    { -1508040570u, -846109042u, 5354084u, 1638400u, 95597274, 0x4023C0CA45917213ULL, "9.8765432109876543210987654" },
    { 0u, 0u, 0u, -2147418112u, 0, 0x8000000000000000ULL, "0.0" },
    { 0u, 0u, 0u, 131072u, 0, 0x0000000000000000ULL, "0.00" },
    { 0u, 0u, 0u, -2147483648u, 0, 0x8000000000000000ULL, "0" },
    { 5u, 0u, 0u, 1835008u, 212988554, 0x3A43CE9A36F23C10ULL, "0.0000000000000000000000000005" },
    { -1717986919u, -1717986919u, 429496729u, 0u, -601882615, 0x45B999999999999AULL, "7922816251426433759354395033" },
    { 16777216u, 0u, 0u, 0u, 1097859072, 0x4170000000000000ULL, "16777216" },
    { 16777217u, 0u, 0u, 0u, 1366294528, 0x4170000010000000ULL, "16777217" },
    { 16777215u, 0u, 0u, 0u, -1586495489, 0x416FFFFFE0000000ULL, "16777215" },
    { 4u, 0u, 0u, 1835008u, -2073046810, 0x3A3FB0F6BE50601AULL, "0.0000000000000000000000000004" },
};

// net48 bits of (double)(1 / 10^s) for s = 0..28 (probe: dhashgen.cs).
const uint64_t kOneOverPow10[29] = {
    0x3FF0000000000000ull, 0x3FB999999999999Aull, 0x3F847AE147AE147Bull,
    0x3F50624DD2F1A9FCull, 0x3F1A36E2EB1C432Dull, 0x3EE4F8B588E368F1ull,
    0x3EB0C6F7A0B5ED8Dull, 0x3E7AD7F29ABCAF48ull, 0x3E45798EE2308C3Aull,
    0x3E112E0BE826D695ull, 0x3DDB7CDFD9D7BDBBull, 0x3DA5FD7FE1796495ull,
    0x3D719799812DEA11ull, 0x3D3C25C268497682ull, 0x3D06849B86A12B9Bull,
    0x3CD203AF9EE75616ull, 0x3C9CD2B297D889BCull, 0x3C670EF54646D497ull,
    0x3C32725DD1D243ACull, 0x3BFD83C94FB6D2ACull, 0x3BC79CA10C924223ull,
    0x3B92E3B40A0E9B4Full, 0x3B5E392010175EE6ull, 0x3B282DB34012B252ull,
    0x3AF357C299A88EA8ull, 0x3ABEF2D0F5DA7DD8ull, 0x3A88C240C4AECB13ull,
    0x3A53CE9A36F23C10ull, 0x3A1FB0F6BE50601Aull,
};

} // namespace

int main()
{
    // --- 1. net48 oracle table: hash / double bits / float bits ---
    {
        const int n = (int)(sizeof(kRows) / sizeof(kRows[0]));
        char what[192];
        for (int i = 0; i < n; i++)
        {
            const OracleRow& r = kRows[i];
            Il2CppDecimal d;
            d.flags = (int32_t)r.flags;
            d.hi32 = r.hi;
            d.lo64 = (uint64_t)r.lo | ((uint64_t)r.mid << 32);

            std::snprintf(what, sizeof what, "net48 hash(%s)", r.text);
            Expect(icall_decimal_get_hash_code(&d) == r.hash, what);

            const double dv = icall_decimal_to_double(&d);
            std::snprintf(what, sizeof what, "net48 (double)(%s)", r.text);
            Expect(DoubleBits(dv) == r.doubleBits, what);

            const float fv = icall_decimal_to_float(&d);
            std::snprintf(what, sizeof what, "net48 (float)(%s)", r.text);
            Expect(FloatBits(fv) == FloatBits(static_cast<float>(dv)), what);
        }
    }

    // --- 2. the low-dword mask is what makes this decimal-specific ---
    {
        Il2CppDecimal d;
        d.flags = 0x00010000; // 0.1m
        d.hi32 = 0;
        d.lo64 = 1;
        // net48 Double.GetHashCode(0.1) == -1507852285; decimal == -1507852279.
        Expect(icall_decimal_get_hash_code(&d) == -1507852279,
               "hash(0.1m) == -1507852279 (NOT Double.GetHashCode -1507852285)");
        Expect(icall_decimal_get_hash_code(&d) != -1507852285,
               "hash(0.1m) != Double.GetHashCode(0.1)");
    }

    // --- 3. the hash is NOT value-canonical at large mantissas (net48 defect) ---
    {
        // Two spellings of the SAME value: m @ scale 14 vs (m*10) @ scale 15.
        Il2CppDecimal a;
        a.flags = 917504; // 0x000E0000
        a.hi32 = 289413371u;
        a.lo64 = 0xB474A1DC879202E1ull;

        Il2CppDecimal b;
        b.flags = 983040; // 0x000F0000
        b.hi32 = 2894133717u; // the same uint as the probe's -1400833579 int
        b.lo64 = 0x0C8E529D4BB41CCAull;

        Expect(icall_decimal_equals(&a, &b), "canonical pair: equal values");
        Expect(icall_decimal_get_hash_code(&a) == -1803220006, "net48 hash(m@14)");
        Expect(icall_decimal_get_hash_code(&b) == -1803220054, "net48 hash(m*10@15)");
        Expect(icall_decimal_get_hash_code(&a) != icall_decimal_get_hash_code(&b),
               "net48: trailing zero on a 96-bit mantissa CHANGES the hash");
    }

    // --- 4. zero and negative zero both hash to 0 ---
    {
        const uint32_t flagsSet[] = { 0x00000000u, 0x80000000u, 0x00010000u,
                                      0x80010000u, 0x001C0000u, 0x801C0000u };
        char what[96];
        for (int i = 0; i < (int)(sizeof flagsSet / sizeof flagsSet[0]); i++)
        {
            Il2CppDecimal z;
            z.flags = (int32_t)flagsSet[i];
            z.hi32 = 0;
            z.lo64 = 0;
            std::snprintf(what, sizeof what, "hash(zero with flags 0x%08X) == 0",
                          (unsigned)flagsSet[i]);
            Expect(icall_decimal_get_hash_code(&z) == 0, what);
            const bool wantNegZero = (flagsSet[i] & 0x80000000u) != 0;
            std::snprintf(what, sizeof what,
                          "(double)(zero with flags 0x%08X) negative-zero bit",
                          (unsigned)flagsSet[i]);
            Expect((DoubleBits(icall_decimal_to_double(&z)) == 0x8000000000000000ull) ==
                       wantNegZero,
                   what);
        }
    }

    // --- 5. equal small values written with extra zeros hash alike ---
    {
        Il2CppDecimal a, b, c;
        a.flags = 0x00010000; a.hi32 = 0; a.lo64 = 15;   // 1.5m
        b.flags = 0x00020000; b.hi32 = 0; b.lo64 = 150;  // 1.50m
        c.flags = 0x00030000; c.hi32 = 0; c.lo64 = 1500; // 1.500m
        Expect(icall_decimal_get_hash_code(&a) == 1073217536, "net48 hash(1.5m)");
        Expect(icall_decimal_get_hash_code(&b) == 1073217536, "net48 hash(1.50m)");
        Expect(icall_decimal_get_hash_code(&c) == 1073217536, "net48 hash(1.500m)");
    }

    // --- 6. every scale 0..28 converts like net48 (T10 table coverage) ---
    {
        char what[96];
        for (int s = 0; s <= 28; s++)
        {
            Il2CppDecimal d;
            d.flags = (int32_t)((uint32_t)s << 16);
            d.hi32 = 0;
            d.lo64 = 1;
            std::snprintf(what, sizeof what, "net48 (double)(1e-%d)", s);
            Expect(DoubleBits(icall_decimal_to_double(&d)) == kOneOverPow10[s], what);
        }
    }

    if (g_failures == 0)
    {
        std::printf("all net48 decimal hash/conversion tests passed\n");
        return 0;
    }
    std::printf("%d net48 decimal hash/conversion test(s) failed\n", g_failures);
    return 1;
}