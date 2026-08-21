//------------------------------------------------------------------------------
// test_conv_bool.cpp -- bool-carrier conversion icall unit tests
// (icall_convert_to_int32_bool / icall_convert_to_bool_i4, il2cpp_convert.h).
//
// SEMANTIC BASELINE: .NET Framework 4.8 Convert family (net48 contract,
// never throws):
//   * Convert.ToInt32(bool): true -> 1, false -> 0. The bool rides the int32
//     carrier (M2 slot model), so the carrier input is normalized: 0 -> 0,
//     ANY non-zero (incl. negatives) -> exactly 1.
//   * Convert.ToBoolean(int): 0 -> 0, any non-zero -> 1.
//
// Coverage (normal + edge + boundary):
//   1. to_int32_bool: false(0) -> 0; true(1) -> 1; every non-zero sampled
//      carrier (positive, negative, INT32_MIN/MAX, power-of-two bits)
//      normalizes to EXACTLY 1 (== 1, not merely truthy).
//   2. to_bool_i4: same normalization contract (the inverse direction).
//   3. Round trips close the loop both ways:
//        to_int32_bool o to_bool_i4   (bool -> int32 -> bool carrier)
//        to_bool_i4 o to_int32_bool   (int32 -> bool -> int32)
//      plus idempotence f(f(v)) == f(v) for both, and cross-agreement:
//      both icalls return IDENTICAL results on every sampled input.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_convert.h"

#include <cstdint>
#include <cstdio>

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

} // namespace

int main()
{
    // --- 1. icall_convert_to_int32_bool: false(0) -> 0, true(1) -> 1 ---
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(0) == 0,
           "to_int32_bool(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(1) == 1,
           "to_int32_bool(1) == 1");

    // --- 2. any non-zero carrier -> EXACTLY 1 (net48 ToInt32(bool)) ---
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(2) == 1,
           "to_int32_bool(2) == 1");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(42) == 1,
           "to_int32_bool(42) == 1");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(-1) == 1,
           "to_int32_bool(-1) == 1");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(-42) == 1,
           "to_int32_bool(-42) == 1");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(INT32_MAX) == 1,
           "to_int32_bool(INT32_MAX) == 1");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(INT32_MIN) == 1,
           "to_int32_bool(INT32_MIN) == 1");
    Expect(il2cpp::runtime::icall_convert_to_int32_bool(0x10000) == 1,
           "to_int32_bool(0x10000) == 1");

    // --- 3. icall_convert_to_bool_i4: same normalization contract ---
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(0) == 0,
           "to_bool_i4(0) == 0");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(1) == 1,
           "to_bool_i4(1) == 1");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(42) == 1,
           "to_bool_i4(42) == 1");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(-1) == 1,
           "to_bool_i4(-1) == 1");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(INT32_MAX) == 1,
           "to_bool_i4(INT32_MAX) == 1");
    Expect(il2cpp::runtime::icall_convert_to_bool_i4(INT32_MIN) == 1,
           "to_bool_i4(INT32_MIN) == 1");

    // --- 4. Round trips + idempotence + cross-agreement over a sample set ---
    const int32_t samples[] = {
        INT32_MIN, INT32_MIN + 1, -65536, -257, -256, -2, -1,
        0, 1, 2, 3, 42, 127, 128, 255, 256, 257, 65536, 65537,
        0x12345678, INT32_MAX - 1, INT32_MAX,
    };
    for (const int32_t v : samples)
    {
        const int32_t asBool = il2cpp::runtime::icall_convert_to_bool_i4(v);
        const int32_t asInt = il2cpp::runtime::icall_convert_to_int32_bool(v);

        // Both directions normalize into the {0, 1} carrier alphabet.
        Expect((asBool == 0 || asBool == 1) && asBool == (v != 0 ? 1 : 0),
               "to_bool_i4 normalizes to exact 0/1 matching (v != 0)");
        Expect((asInt == 0 || asInt == 1) && asInt == (v != 0 ? 1 : 0),
               "to_int32_bool normalizes to exact 0/1 matching (v != 0)");

        // Round trip bool -> int32 -> bool and int32 -> bool -> int32.
        Expect(il2cpp::runtime::icall_convert_to_int32_bool(asBool) == asBool,
               "roundtrip to_int32_bool(to_bool_i4(v)) == to_bool_i4(v)");
        Expect(il2cpp::runtime::icall_convert_to_bool_i4(asInt) == asInt,
               "roundtrip to_bool_i4(to_int32_bool(v)) == to_int32_bool(v)");

        // Idempotence: a second application is a fixed point.
        Expect(il2cpp::runtime::icall_convert_to_bool_i4(asBool) == asBool,
               "to_bool_i4 idempotent");
        Expect(il2cpp::runtime::icall_convert_to_int32_bool(asInt) == asInt,
               "to_int32_bool idempotent");

        // Cross-agreement: the two icalls agree on every sampled input.
        Expect(asBool == asInt,
               "to_bool_i4 and to_int32_bool agree on every input");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d conv_bool assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime conv_bool tests passed\n");
    return 0;
}
