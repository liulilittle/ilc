//------------------------------------------------------------------------------
// test_interlocked_fp.cpp -- P6 Interlocked float/double atomics unit tests.
//
// Coverage mirrors the P6 family declared in il2cpp_interlocked.h (see
// interlocked.cpp for the bit-reinterpretation implementation over the
// int32/int64 atomics):
//   1. Exchange: returns the OLD value and unconditionally stores the new
//      one -- bit-exact round trips on ordinary values and on NaN payload /
//      -0.0 / +-inf patterns (the bit pattern is preserved end to end).
//   2. CompareExchange is BIT-exact: the store happens only when the current
//      bits equal the comparand's bits. -0.0 does NOT match +0.0 (numerically
//      equal, different encodings) and a NaN matches only its own exact
//      payload+sign word -- a float == comparison would get both wrong.
//   3. Add is a CAS loop: repeated adds converge to the exact sequential-
//      summation result (bit-identical), overflow at FLT_MAX/DBL_MAX lands on
//      +inf, and 8-thread contention converges with no lost updates.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_interlocked.h"

#include <cfloat>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>

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

// Bit-level reinterpretation via memcpy: keeps -Wall/-Wextra clean (no
// strict-aliasing games in the test) while still asserting the EXACT bit
// contracts the implementation promises.
uint32_t BitsOf(float f)
{
    uint32_t bits = 0;
    static_assert(sizeof(bits) == sizeof(f), "float is a 32-bit IEEE-754 value");
    std::memcpy(&bits, &f, sizeof(bits));
    return bits;
}

float FloatOf(uint32_t bits)
{
    float f = 0.0f;
    std::memcpy(&f, &bits, sizeof(f));
    return f;
}

uint64_t BitsOf(double d)
{
    uint64_t bits = 0;
    static_assert(sizeof(bits) == sizeof(d), "double is a 64-bit IEEE-754 value");
    std::memcpy(&bits, &d, sizeof(bits));
    return bits;
}

double DoubleOf(uint64_t bits)
{
    double d = 0.0;
    std::memcpy(&d, &bits, sizeof(d));
    return d;
}

// Exact IEEE-754 encodings used as exchange/CAS patterns.
constexpr uint32_t kFpNanQPayloadBits = 0x7FC00001u; // quiet NaN, payload lsb set
constexpr uint32_t kFpNanQBits        = 0x7FC00000u; // quiet NaN, canonical
constexpr uint32_t kFpNanSignBits     = 0xFFC00000u; // quiet NaN, sign bit set
constexpr uint32_t kFpNegZeroBits     = 0x80000000u;
constexpr uint32_t kFpPosInfBits      = 0x7F800000u;
constexpr uint32_t kFpNegInfBits      = 0xFF800000u;

constexpr uint64_t kDpNanQPayloadBits = 0x7FF8000000000001ull;
constexpr uint64_t kDpNanQBits        = 0x7FF8000000000000ull;
constexpr uint64_t kDpNanSignBits     = 0xFFF8000000000000ull;
constexpr uint64_t kDpNegZeroBits     = 0x8000000000000000ull;
constexpr uint64_t kDpPosInfBits      = 0x7FF0000000000000ull;
constexpr uint64_t kDpNegInfBits      = 0xFFF0000000000000ull;

} // namespace

int main()
{
    using il2cpp::runtime::il2cpp_interlocked_add_double;
    using il2cpp::runtime::il2cpp_interlocked_add_float;
    using il2cpp::runtime::il2cpp_interlocked_compare_exchange_double;
    using il2cpp::runtime::il2cpp_interlocked_compare_exchange_float;
    using il2cpp::runtime::il2cpp_interlocked_exchange_double;
    using il2cpp::runtime::il2cpp_interlocked_exchange_float;

    // --- 1. Exchange: returns the OLD value, stores the new one. ---
    {
        float loc = 1.5f;
        const float old = il2cpp_interlocked_exchange_float(&loc, 2.5f);
        Expect(BitsOf(old) == BitsOf(1.5f), "exchange_f returns the OLD value (1.5)");
        Expect(BitsOf(loc) == BitsOf(2.5f), "exchange_f stores the new value (2.5)");

        double dloc = 3.25;
        const double dold = il2cpp_interlocked_exchange_double(&dloc, -0.5);
        Expect(BitsOf(dold) == BitsOf(3.25), "exchange_d returns the OLD value (3.25)");
        Expect(BitsOf(dloc) == BitsOf(-0.5), "exchange_d stores the new value (-0.5)");
    }

    // --- 2. Exchange is bit-exact on special patterns (no FP conversion). ---
    {
        // OLD side: a payload-carrying NaN comes back with its exact bits.
        float loc = FloatOf(kFpNanQPayloadBits);
        const float old = il2cpp_interlocked_exchange_float(&loc, 1.0f);
        Expect(BitsOf(old) == kFpNanQPayloadBits,
               "exchange_f returns the OLD NaN bit-exact (payload preserved)");
        Expect(BitsOf(loc) == BitsOf(1.0f), "exchange_f overwrites NaN with 1.0");

        // NEW side: storing a special pattern lands bit-exactly.
        loc = FloatOf(kFpNegZeroBits);
        const float oldz = il2cpp_interlocked_exchange_float(&loc, FloatOf(kFpPosInfBits));
        Expect(BitsOf(oldz) == kFpNegZeroBits, "exchange_f returns OLD -0.0 bit-exact");
        Expect(BitsOf(loc) == kFpPosInfBits, "exchange_f stores +inf bit-exact");

        double dloc = DoubleOf(kDpNanSignBits);
        const double dold = il2cpp_interlocked_exchange_double(&dloc, 2.0);
        Expect(BitsOf(dold) == kDpNanSignBits,
               "exchange_d returns the OLD sign-bit NaN bit-exact");
        Expect(BitsOf(dloc) == BitsOf(2.0), "exchange_d stores 2.0 over NaN");

        dloc = 4.0;
        const double doldi = il2cpp_interlocked_exchange_double(&dloc, DoubleOf(kDpNegInfBits));
        Expect(BitsOf(doldi) == BitsOf(4.0), "exchange_d returns OLD 4.0");
        Expect(BitsOf(dloc) == kDpNegInfBits, "exchange_d stores -inf bit-exact");
    }

    // --- 3. CompareExchange HIT: bit-equal comparand stores, returns OLD. ---
    {
        // A NaN hits only its own exact encoding.
        float loc = FloatOf(kFpNanQBits);
        const float old =
            il2cpp_interlocked_compare_exchange_float(&loc, 7.5f, FloatOf(kFpNanQBits));
        Expect(BitsOf(old) == kFpNanQBits, "CAS_f hit on NaN returns the OLD NaN bits");
        Expect(BitsOf(loc) == BitsOf(7.5f), "CAS_f hit on NaN stores the new value");

        // -0.0 hits its own encoding (bit equality, not numeric equality).
        loc = FloatOf(kFpNegZeroBits);
        const float oldz =
            il2cpp_interlocked_compare_exchange_float(&loc, 1.0f, FloatOf(kFpNegZeroBits));
        Expect(BitsOf(oldz) == kFpNegZeroBits, "CAS_f hit on -0.0 returns OLD -0.0");
        Expect(BitsOf(loc) == BitsOf(1.0f), "CAS_f hit on -0.0 stores the new value");

        // Both infinities hit their own encodings.
        loc = FloatOf(kFpPosInfBits);
        const float oldi =
            il2cpp_interlocked_compare_exchange_float(&loc, 2.0f, FloatOf(kFpPosInfBits));
        Expect(BitsOf(oldi) == kFpPosInfBits, "CAS_f hit on +inf returns OLD +inf");
        Expect(BitsOf(loc) == BitsOf(2.0f), "CAS_f hit on +inf stores the new value");

        loc = FloatOf(kFpNegInfBits);
        const float oldni =
            il2cpp_interlocked_compare_exchange_float(&loc, 3.0f, FloatOf(kFpNegInfBits));
        Expect(BitsOf(oldni) == kFpNegInfBits, "CAS_f hit on -inf returns OLD -inf");
        Expect(BitsOf(loc) == BitsOf(3.0f), "CAS_f hit on -inf stores the new value");

        // The stored VALUE side is also bit-exact: a NaN lands verbatim.
        loc = 1.0f;
        const float oldn =
            il2cpp_interlocked_compare_exchange_float(&loc, FloatOf(kFpNanQPayloadBits), 1.0f);
        Expect(BitsOf(oldn) == BitsOf(1.0f), "CAS_f hit on 1.0 returns OLD 1.0");
        Expect(BitsOf(loc) == kFpNanQPayloadBits,
               "CAS_f stores the NaN value bit-exact (payload preserved)");

        double dloc = DoubleOf(kDpNanQBits);
        const double dold =
            il2cpp_interlocked_compare_exchange_double(&dloc, 8.5, DoubleOf(kDpNanQBits));
        Expect(BitsOf(dold) == kDpNanQBits, "CAS_d hit on NaN returns the OLD NaN bits");
        Expect(BitsOf(dloc) == BitsOf(8.5), "CAS_d hit on NaN stores the new value");

        dloc = DoubleOf(kDpNegZeroBits);
        const double doldz =
            il2cpp_interlocked_compare_exchange_double(&dloc, 1.0, DoubleOf(kDpNegZeroBits));
        Expect(BitsOf(doldz) == kDpNegZeroBits, "CAS_d hit on -0.0 returns OLD -0.0");
        Expect(BitsOf(dloc) == BitsOf(1.0), "CAS_d hit on -0.0 stores the new value");

        dloc = DoubleOf(kDpPosInfBits);
        const double doldi =
            il2cpp_interlocked_compare_exchange_double(&dloc, 2.0, DoubleOf(kDpPosInfBits));
        Expect(BitsOf(doldi) == kDpPosInfBits, "CAS_d hit on +inf returns OLD +inf");
        Expect(BitsOf(dloc) == BitsOf(2.0), "CAS_d hit on +inf stores the new value");
    }

    // --- 4. CompareExchange MISS: ANY bit difference rejects the store. ---
    {
        // -0.0 == +0.0 numerically, but CAS is BIT-exact: must NOT store.
        float loc = FloatOf(kFpNegZeroBits);
        const float old = il2cpp_interlocked_compare_exchange_float(&loc, 9.0f, 0.0f);
        Expect(BitsOf(old) == kFpNegZeroBits,
               "CAS_f miss (-0.0 vs +0.0) returns OLD -0.0 bit-exact");
        Expect(BitsOf(loc) == kFpNegZeroBits,
               "CAS_f miss (-0.0 vs +0.0) leaves -0.0 stored");

        // Reverse direction: a +0.0 location rejects a -0.0 comparand.
        loc = 0.0f;
        const float oldr =
            il2cpp_interlocked_compare_exchange_float(&loc, 9.0f, FloatOf(kFpNegZeroBits));
        Expect(BitsOf(oldr) == BitsOf(0.0f), "CAS_f miss (+0.0 vs -0.0) returns OLD +0.0");
        Expect(BitsOf(loc) == BitsOf(0.0f), "CAS_f miss (+0.0 vs -0.0) leaves +0.0 stored");

        // NaN payload mismatch: numerically both are NaN, bitwise different.
        loc = FloatOf(kFpNanQBits);
        const float oldp =
            il2cpp_interlocked_compare_exchange_float(&loc, 9.0f, FloatOf(kFpNanQPayloadBits));
        Expect(BitsOf(oldp) == kFpNanQBits, "CAS_f miss (NaN payload) returns the OLD NaN bits");
        Expect(BitsOf(loc) == kFpNanQBits, "CAS_f miss (NaN payload) leaves the NaN stored");

        // NaN sign mismatch.
        loc = FloatOf(kFpNanQBits);
        const float olds =
            il2cpp_interlocked_compare_exchange_float(&loc, 9.0f, FloatOf(kFpNanSignBits));
        Expect(BitsOf(olds) == kFpNanQBits, "CAS_f miss (NaN sign) returns the OLD NaN bits");
        Expect(BitsOf(loc) == kFpNanQBits, "CAS_f miss (NaN sign) leaves the NaN stored");

        // +inf vs the largest finite float: different bits, no store.
        loc = FloatOf(kFpPosInfBits);
        const float oldf = il2cpp_interlocked_compare_exchange_float(&loc, 9.0f, FLT_MAX);
        Expect(BitsOf(oldf) == kFpPosInfBits, "CAS_f miss (+inf vs FLT_MAX) returns OLD +inf");
        Expect(BitsOf(loc) == kFpPosInfBits, "CAS_f miss (+inf vs FLT_MAX) leaves +inf stored");

        double dloc = DoubleOf(kDpNegZeroBits);
        const double dold = il2cpp_interlocked_compare_exchange_double(&dloc, 9.0, 0.0);
        Expect(BitsOf(dold) == kDpNegZeroBits,
               "CAS_d miss (-0.0 vs +0.0) returns OLD -0.0 bit-exact");
        Expect(BitsOf(dloc) == kDpNegZeroBits, "CAS_d miss (-0.0 vs +0.0) leaves -0.0 stored");

        dloc = DoubleOf(kDpNanQBits);
        const double doldp =
            il2cpp_interlocked_compare_exchange_double(&dloc, 9.0, DoubleOf(kDpNanQPayloadBits));
        Expect(BitsOf(doldp) == kDpNanQBits, "CAS_d miss (NaN payload) returns the OLD NaN bits");
        Expect(BitsOf(dloc) == kDpNanQBits, "CAS_d miss (NaN payload) leaves the NaN stored");

        dloc = DoubleOf(kDpPosInfBits);
        const double doldf = il2cpp_interlocked_compare_exchange_double(&dloc, 9.0, DBL_MAX);
        Expect(BitsOf(doldf) == kDpPosInfBits, "CAS_d miss (+inf vs DBL_MAX) returns OLD +inf");
        Expect(BitsOf(dloc) == kDpPosInfBits, "CAS_d miss (+inf vs DBL_MAX) leaves +inf stored");
    }

    // --- 5. Add: returns the NEW value; single-thread CAS-loop convergence. ---
    {
        float loc = 0.0f;
        const float r1 = il2cpp_interlocked_add_float(&loc, 2.5f);
        Expect(BitsOf(r1) == BitsOf(2.5f) && BitsOf(loc) == BitsOf(2.5f),
               "add_f returns the NEW value (first add)");
        const float r2 = il2cpp_interlocked_add_float(&loc, 0.5f);
        Expect(BitsOf(r2) == BitsOf(3.0f) && BitsOf(loc) == BitsOf(3.0f),
               "add_f returns the NEW accumulated value");

        // Power-of-two step: every partial sum is exact -> exactly 500.0f.
        float acc = 0.0f;
        for (int i = 0; i < 1000; ++i)
        {
            (void)il2cpp_interlocked_add_float(&acc, 0.5f);
        }
        Expect(BitsOf(acc) == BitsOf(500.0f), "add_f 1000 x 0.5 converges to exactly 500.0");

        // Non-representable step: the CAS loop must reproduce the EXACT
        // sequential float summation (same order, same rounding).
        float seq = 0.0f;
        for (int i = 0; i < 1000; ++i)
        {
            seq += 0.1f;
        }
        float acc2 = 0.0f;
        for (int i = 0; i < 1000; ++i)
        {
            (void)il2cpp_interlocked_add_float(&acc2, 0.1f);
        }
        Expect(BitsOf(acc2) == BitsOf(seq),
               "add_f 1000 x 0.1 is bit-identical to sequential summation");

        // Overflow edge: FLT_MAX + FLT_MAX == +inf (IEEE-deterministic).
        float ovf = FLT_MAX;
        const float rovf = il2cpp_interlocked_add_float(&ovf, FLT_MAX);
        Expect(BitsOf(rovf) == kFpPosInfBits && BitsOf(ovf) == kFpPosInfBits,
               "add_f FLT_MAX+FLT_MAX overflows to exactly +inf");

        double dloc = 0.0;
        const double dr1 = il2cpp_interlocked_add_double(&dloc, 1.25);
        Expect(BitsOf(dr1) == BitsOf(1.25) && BitsOf(dloc) == BitsOf(1.25),
               "add_d returns the NEW value (first add)");

        double dseq = 0.0;
        for (int i = 0; i < 2000; ++i)
        {
            dseq += 0.1;
        }
        double dacc = 0.0;
        for (int i = 0; i < 2000; ++i)
        {
            (void)il2cpp_interlocked_add_double(&dacc, 0.1);
        }
        Expect(BitsOf(dacc) == BitsOf(dseq),
               "add_d 2000 x 0.1 is bit-identical to sequential summation");

        double dovf = DBL_MAX;
        const double drovf = il2cpp_interlocked_add_double(&dovf, DBL_MAX);
        Expect(BitsOf(drovf) == kDpPosInfBits && BitsOf(dovf) == kDpPosInfBits,
               "add_d DBL_MAX+DBL_MAX overflows to exactly +inf");
    }

    // --- 6. Add under contention: the CAS loop converges across threads. ---
    {
        // Every intermediate sum is an exact integer (< 2^24), so the final
        // value is uniquely determined: 8 threads x 10000 adds of 1.0f ==
        // exactly 80000.0f. Any lost update (a broken CAS loop) shows up as
        // a shortfall.
        float floc = 0.0f;
        {
            std::thread workers[8];
            for (unsigned t = 0; t < 8; ++t)
            {
                workers[t] = std::thread([&floc]
                {
                    for (int i = 0; i < 10000; ++i)
                    {
                        (void)il2cpp::runtime::il2cpp_interlocked_add_float(&floc, 1.0f);
                    }
                });
            }
            for (std::thread& w : workers)
            {
                w.join();
            }
        }
        Expect(BitsOf(floc) == BitsOf(80000.0f),
               "add_f 8 threads x 10000 x 1.0 converges to exactly 80000.0 (no lost updates)");

        double dloc = 0.0;
        {
            std::thread workers[8];
            for (unsigned t = 0; t < 8; ++t)
            {
                workers[t] = std::thread([&dloc]
                {
                    for (int i = 0; i < 5000; ++i)
                    {
                        (void)il2cpp::runtime::il2cpp_interlocked_add_double(&dloc, 2.0);
                    }
                });
            }
            for (std::thread& w : workers)
            {
                w.join();
            }
        }
        Expect(BitsOf(dloc) == BitsOf(80000.0),
               "add_d 8 threads x 5000 x 2.0 converges to exactly 80000.0 (no lost updates)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d interlocked fp assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime interlocked fp tests passed (P6 float/double atomics)\n");
    return 0;
}
