//------------------------------------------------------------------------------
// test_random.cpp -- System.Random runtime unit tests (il2cpp_random.h).
//
// Coverage (normal + edge + error paths):
//   1. Same seed -> identical Next() sequences (determinism contract).
//   2. Next(int max) stays in [0, max-1] over a large sample.
//   3. Next(int min, int max) stays in [min, max-1]; extreme int32 range is
//      handled without overflow (range spans the full int32 width).
//   4. NextDouble() stays in [0, 1).
//   5. NextBytes fills every element of a 1-byte-stride byte array with the
//      low 8 bits of consecutive samples; the 4-byte-stride storage layout
//      (M4 type system: byte values live in int32 slots) is also covered.
//   6. Argument validation: Next(max<=0), Next(min>max) and NextBytes(null)
//      raise the ArgumentOutOfRange/ArgumentNull managed exception carrier.
//   7. The parameterless ctor (time-based seed) produces in-range values.
//   8. A rooted Random instance survives a GC collection with its state
//      intact (the state field lives in managed memory and is not lost).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_random.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "random test fixture assumes 8-byte pointers");

int g_failures = 0;

// Canonical exception chains (il2cpp_exception_ext.h factory convention):
// {Type},{Type},{Base...} -- the repeated type makes a catch of the thrown
// type itself match, base segments make base catches match.
const char kAoore[] = "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
const char kAnnee[] = "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char kNree[] = "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Create a Random instance through the icall path (managed allocation +
// seeded constructor), mirroring what generated newobj/ctor code does.
il2cpp::Il2CppRandom* MakeRandom(int32_t seed)
{
    il2cpp::Il2CppRandom* r = reinterpret_cast<il2cpp::Il2CppRandom*>(
        il2cpp::il2cpp_object_new(il2cpp::il2cpp_random_class()));
    il2cpp::runtime::icall_random_ctor(reinterpret_cast<il2cpp::Il2CppObject*>(r), seed);
    return r;
}

// Raise-and-catch helper: run 'fn', capture the managed exception carrier.
// Returns true when a DotNetException was thrown (the icall raised it).
template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedType)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
}

} // namespace

int main()
{
    // --- 1. Same seed -> identical sequence (determinism) ---
    {
        il2cpp::Il2CppRandom* a = MakeRandom(42);
        il2cpp::Il2CppRandom* b = MakeRandom(42);
        bool seqEqual = true;
        for (int32_t i = 0; i < 10; ++i)
        {
            const int32_t va = il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(a));
            const int32_t vb = il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(b));
            if (va != vb) seqEqual = false;
        }
        Expect(seqEqual, "two Random(42) instances produce identical Next() sequences");
    }

    // --- 1b. Different seeds diverge (sanity) ---
    {
        il2cpp::Il2CppRandom* a = MakeRandom(1);
        il2cpp::Il2CppRandom* b = MakeRandom(2);
        Expect(il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(a)) !=
               il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(b)),
               "different seeds produce different first Next() values");
    }

    // --- 2. Next(max) stays in [0, max-1] ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(7);
        bool inRange = true;
        for (int32_t i = 0; i < 500; ++i)
        {
            const int32_t v = il2cpp::runtime::icall_random_next_max(reinterpret_cast<il2cpp::Il2CppObject*>(r), 5);
            if (v < 0 || v > 4) inRange = false;
        }
        Expect(inRange, "Next(5) stays within [0, 4] over 500 draws");
    }

    // --- 3. Next(min, max) stays in [min, max-1]; wide range no overflow ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(3);
        bool inRange = true;
        for (int32_t i = 0; i < 500; ++i)
        {
            const int32_t v = il2cpp::runtime::icall_random_next_minmax(reinterpret_cast<il2cpp::Il2CppObject*>(r), 2, 7);
            if (v < 2 || v > 6) inRange = false;
        }
        Expect(inRange, "Next(2, 7) stays within [2, 6] over 500 draws");

        bool wideOk = true;
        for (int32_t i = 0; i < 200; ++i)
        {
            const int32_t v = il2cpp::runtime::icall_random_next_minmax(
                reinterpret_cast<il2cpp::Il2CppObject*>(r), INT32_MIN, INT32_MAX);
            if (v < INT32_MIN || v >= INT32_MAX) wideOk = false;
        }
        Expect(wideOk, "Next(Int32.MinValue, Int32.MaxValue) never overflows (full int32 range)");
    }

    // --- 4. NextDouble() stays in [0, 1) ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(11);
        bool inRange = true;
        for (int32_t i = 0; i < 500; ++i)
        {
            const double v = il2cpp::runtime::icall_random_next_double(reinterpret_cast<il2cpp::Il2CppObject*>(r));
            if (!(v >= 0.0 && v < 1.0)) inRange = false;
        }
        Expect(inRange, "NextDouble() stays within [0, 1) over 500 draws");
    }

    // --- 5. NextBytes fills every element (1-byte stride) ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(99);
        il2cpp::Il2CppArray* buf = il2cpp::il2cpp_array_new(nullptr, 1, false, 8);
        il2cpp::runtime::icall_random_next_bytes(reinterpret_cast<il2cpp::Il2CppObject*>(r),
                                                 reinterpret_cast<il2cpp::Il2CppObject*>(buf));
        char* bytes = buf->data;
        bool valuesOk = true;
        bool notAllZero = false;
        for (int32_t i = 0; i < 8; ++i)
        {
            const int32_t v = static_cast<int32_t>(static_cast<unsigned char>(bytes[i]));
            if (v < 0 || v > 255) valuesOk = false;
            if (v != 0) notAllZero = true;
        }
        Expect(valuesOk, "NextBytes fills each element with a byte value (0..255)");
        Expect(notAllZero, "NextBytes(seed 99) produces non-zero bytes (real fill, not no-op)");
    }

    // --- 5b. NextBytes with the M4 int32-slot byte storage (stride 4) ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(100);
        il2cpp::Il2CppArray* buf = il2cpp::il2cpp_array_new(nullptr, 4, false, 3);
        il2cpp::runtime::icall_random_next_bytes(reinterpret_cast<il2cpp::Il2CppObject*>(r),
                                                 reinterpret_cast<il2cpp::Il2CppObject*>(buf));
        int32_t* slots = reinterpret_cast<int32_t*>(buf->data);
        Expect(slots[0] >= 0 && slots[0] <= 255 &&
               slots[1] >= 0 && slots[1] <= 255 &&
               slots[2] >= 0 && slots[2] <= 255,
               "NextBytes writes only the low 8 bits into 4-byte byte slots (M4 layout)");
    }

    // --- 6. Argument validation raises the managed exception carrier ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(5);
        il2cpp::Il2CppObject* obj = reinterpret_cast<il2cpp::Il2CppObject*>(r);
        bool o2 = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_max(obj, -1); },
                               kAoore);
        bool o3 = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_minmax(obj, 7, 2); },
                               kAoore);
        bool o4 = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_bytes(obj, nullptr); },
                               kAnnee);
        Expect(o2 && o3 && o4, "Next(-1)/Next(7,2)/NextBytes(null) raise the managed exceptions");

        // net48: Next(0) == 0 (NO exception -- maxValue < 0 is the only error).
        il2cpp::Il2CppRandom* r0 = MakeRandom(5);
        Expect(il2cpp::runtime::icall_random_next_max(
                   reinterpret_cast<il2cpp::Il2CppObject*>(r0), 0) == 0,
               "Next(0) == 0 (net48, no exception)");
    }

    // --- 6a. Null receiver raises NullReferenceException (net48 semantics,
    // batch-22a audit convention: generated code null-checks first, but the
    // icall must never dereference null -- arraylist/streamio/stringext
    // precedent). Covers both ctors and every sample icall.
    {
        bool ctorNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_ctor(nullptr, 42); }, kNree);
        bool ctorDefNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_ctor_default(nullptr); }, kNree);
        bool nextNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next(nullptr); }, kNree);
        bool nextMaxNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_max(nullptr, 10); }, kNree);
        bool nextMinMaxNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_minmax(nullptr, 0, 10); }, kNree);
        bool nextDblNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_double(nullptr); }, kNree);
        bool nextBytesNre = ThrowsDotNet([&]() { il2cpp::runtime::icall_random_next_bytes(nullptr, nullptr); }, kNree);
        Expect(ctorNre && ctorDefNre && nextNre && nextMaxNre && nextMinMaxNre && nextDblNre && nextBytesNre,
               "null receiver on ctor/Next/Next(max)/Next(min,max)/NextDouble/NextBytes raises NRE");
    }

    // --- 6c. Boundary forms (net48 distribution edges):
    //   * Next(1) is always 0 (the only in-range value).
    //   * Next(min, max) with min == max returns min (range 0).
    //   * Next(max) with max == Int32.MaxValue stays in [0, Int32.MaxValue-1]
    //     (the +1 boundary is never produced -- net48 formula).
    //   * Next(min, max) with a negative min/max pair stays in [min, max-1].
    {
        il2cpp::Il2CppRandom* r = MakeRandom(31);
        il2cpp::Il2CppObject* obj = reinterpret_cast<il2cpp::Il2CppObject*>(r);
        bool oneOk = true;
        for (int32_t i = 0; i < 50; ++i)
        {
            if (il2cpp::runtime::icall_random_next_max(obj, 1) != 0) oneOk = false;
        }
        Expect(oneOk, "Next(1) is always 0 over 50 draws");

        Expect(il2cpp::runtime::icall_random_next_minmax(obj, 7, 7) == 7,
               "Next(7, 7) == 7 (range 0 -> min, net48)");

        bool maxEdgeOk = true;
        for (int32_t i = 0; i < 200; ++i)
        {
            const int32_t v = il2cpp::runtime::icall_random_next_max(obj, INT32_MAX);
            if (v < 0 || v >= INT32_MAX) maxEdgeOk = false;
        }
        Expect(maxEdgeOk, "Next(Int32.MaxValue) never yields Int32.MaxValue over 200 draws");

        bool negOk = true;
        for (int32_t i = 0; i < 200; ++i)
        {
            const int32_t v = il2cpp::runtime::icall_random_next_minmax(obj, -10, -1);
            if (v < -10 || v >= -1) negOk = false;
        }
        Expect(negOk, "Next(-10, -1) stays within [-10, -2] over 200 draws");
    }

    // --- 6d. NextBytes on an empty array is a silent no-op (net48: no
    // samples drawn, no throw).
    {
        il2cpp::Il2CppRandom* r = MakeRandom(55);
        il2cpp::Il2CppArray* empty = il2cpp::il2cpp_array_new(nullptr, 1, false, 0);
        bool threw = false;
        try
        {
            il2cpp::runtime::icall_random_next_bytes(reinterpret_cast<il2cpp::Il2CppObject*>(r),
                                                     reinterpret_cast<il2cpp::Il2CppObject*>(empty));
        }
        catch (const il2cpp::DotNetException&)
        {
            threw = true;
        }
        Expect(!threw, "NextBytes(empty array) is a silent no-op (net48)");
    }

    // --- 6b. Fixed-seed sequence pins the net48 oracle exactly (Knuth
    // subtractive generator, corert compat copy). Random(42) must print:
    // 1434747710 / 302596119 / 269548474 / 5 / 5 / 0.262593 / 205,59,153,134.
    {
        il2cpp::Il2CppRandom* r = MakeRandom(42);
        Expect(il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(r)) == 1434747710,
               "Random(42).Next() #1 == 1434747710 (net48 oracle)");
        Expect(il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(r)) == 302596119,
               "Random(42).Next() #2 == 302596119 (net48 oracle)");
        Expect(il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(r)) == 269548474,
               "Random(42).Next() #3 == 269548474 (net48 oracle)");
        Expect(il2cpp::runtime::icall_random_next_max(reinterpret_cast<il2cpp::Il2CppObject*>(r), 10) == 5,
               "Random(42).Next(10) == 5 (net48 oracle)");
        Expect(il2cpp::runtime::icall_random_next_minmax(reinterpret_cast<il2cpp::Il2CppObject*>(r), 5, 10) == 5,
               "Random(42).Next(5,10) == 5 (net48 oracle)");
        Expect(il2cpp::runtime::icall_random_next_double(reinterpret_cast<il2cpp::Il2CppObject*>(r)) == 0.26259267528662117,
               "Random(42).NextDouble() == 0.26259267528662117 (net48 oracle, F6 == 0.262593)");
        il2cpp::Il2CppArray* buf = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        il2cpp::runtime::icall_random_next_bytes(reinterpret_cast<il2cpp::Il2CppObject*>(r),
                                                 reinterpret_cast<il2cpp::Il2CppObject*>(buf));
        const unsigned char* b = reinterpret_cast<const unsigned char*>(buf->data);
        Expect(b[0] == 205 && b[1] == 59 && b[2] == 153 && b[3] == 134,
               "Random(42).NextBytes == 205,59,153,134 (net48 oracle)");
    }

    // --- 7. Parameterless ctor (time seed): usable, in-range values ---
    {
        il2cpp::Il2CppRandom* r = reinterpret_cast<il2cpp::Il2CppRandom*>(
            il2cpp::il2cpp_object_new(il2cpp::il2cpp_random_class()));
        il2cpp::runtime::icall_random_ctor_default(reinterpret_cast<il2cpp::Il2CppObject*>(r));
        const int32_t v = il2cpp::runtime::icall_random_next(reinterpret_cast<il2cpp::Il2CppObject*>(r));
        Expect(v >= 0 && v <= INT32_MAX, "parameterless ctor yields an in-range Next() value");
    }

    // --- 8. Rooted Random survives collection with state intact ---
    {
        il2cpp::Il2CppRandom* r = MakeRandom(2024);
        il2cpp::Il2CppObject* obj = reinterpret_cast<il2cpp::Il2CppObject*>(r);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&obj));
        const int32_t before = il2cpp::runtime::icall_random_next(obj);
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_generation(obj) >= il2cpp::kGcGeneration1,
               "rooted Random survives collection");
        const int32_t after = il2cpp::runtime::icall_random_next(obj);
        const int32_t next2 = il2cpp::runtime::icall_random_next(obj);
        Expect(before != after, "state advances across the collection (state field intact)");
        Expect(next2 >= 0 && next2 <= INT32_MAX, "post-collection draws stay in range");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&obj));
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d random assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime random tests passed\n");
    return 0;
}
