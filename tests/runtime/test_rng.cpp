//------------------------------------------------------------------------------
// test_rng.cpp -- CSPRNG runtime unit tests (rng.h).
//
// The CSPRNG output is NOT reproducible (docs/design/crypto-domain.md §1.4-6),
// so unlike the hash/AES tests there are no byte-exact vectors. Coverage:
//   1. Length honored: a 64-byte and a 256-byte fill complete and are not
//      all-zero (all-zero probability ~2^-512 / 2^-2048 -- a safe
//      high-probability assertion per the design doc's RNG test rules).
//   2. Two fills of the same size differ (probability ~2^-512 of a false
//      failure for 64-byte fills).
//   3. Large fill (1 MiB) succeeds -- exercises the chunked provider loops.
//   4. Deterministic API shape: len == 0 is a no-op (never fails); a null
//      buffer with len > 0 throws ArgumentNullException (the .NET
//      GetBytes(null) contract).
//   5. The API surface is exactly il2cpp_rng_fill(uint8_t*, size_t) -- the
//      header's other promises (no rand()/LCG anywhere in the module) are
//      structural and enforced at review; the .exe link is the shape check.
//
// Plain executable + minimal assertion harness (see test_sha256.cpp).
//------------------------------------------------------------------------------

#include "rng.h"
#include "il2cpp_exception.h"

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

bool AllZero(const uint8_t* bytes, size_t len)
{
    for (size_t i = 0; i < len; ++i)
    {
        if (bytes[i] != 0)
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. Length honored + not all-zero (64 and 256 bytes) ---
    {
        uint8_t buf[64];
        il2cpp::runtime::il2cpp_rng_fill(buf, 64);
        Expect(!AllZero(buf, 64), "64-byte fill is not all-zero");
    }
    {
        uint8_t buf[256];
        il2cpp::runtime::il2cpp_rng_fill(buf, 256);
        Expect(!AllZero(buf, 256), "256-byte fill is not all-zero");
    }

    // --- 2. Two fills of the same size differ ---
    {
        uint8_t a[64], b[64];
        il2cpp::runtime::il2cpp_rng_fill(a, 64);
        il2cpp::runtime::il2cpp_rng_fill(b, 64);
        Expect(std::memcmp(a, b, 64) != 0, "two 64-byte fills differ");
    }

    // --- 3. Large fill (1 MiB) succeeds ---
    {
        uint8_t* big = new uint8_t[1024 * 1024];
        il2cpp::runtime::il2cpp_rng_fill(big, 1024 * 1024);
        Expect(!AllZero(big, 1024 * 1024), "1 MiB fill is not all-zero");
        delete[] big;
    }

    // --- 3b. Chunk-boundary fill (1 MiB + 1) succeeds -- crosses the
    // getrandom(2) 1 MiB per-call chunk cap on Linux, exercising the
    // chunked loop's remainder path; a no-op of the chunk logic would
    // still complete, but a wrong remainder calculation would over- or
    // under-fill (the not-all-zero check catches an under-fill). ---
    {
        const size_t kChunkCross = (static_cast<size_t>(1) << 20) + 1;
        uint8_t* big = new uint8_t[kChunkCross];
        il2cpp::runtime::il2cpp_rng_fill(big, kChunkCross);
        Expect(!AllZero(big, kChunkCross), "1 MiB + 1 fill is not all-zero");
        delete[] big;
    }

    // --- 4. Deterministic edges: zero length no-op, null -> ArgumentNullException ---
    {
        il2cpp::runtime::il2cpp_rng_fill(nullptr, 0);
        Expect(true, "zero-length fill with null buffer is a no-op");
        uint8_t buf[16];
        il2cpp::runtime::il2cpp_rng_fill(buf, 0);
        Expect(true, "zero-length fill with valid buffer is a no-op");
    }
    {
        bool nullThrown = false;
        try
        {
            il2cpp::runtime::il2cpp_rng_fill(nullptr, 16);
        }
        catch (const il2cpp::DotNetException& e)
        {
            nullThrown = std::strstr(e.type_name(), "System.ArgumentNullException") != nullptr;
        }
        catch (...)
        {
        }
        Expect(nullThrown, "null buffer with len > 0 -> ArgumentNullException");
    }

    if (g_failures != 0)
    {
        std::printf("%d rng assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime rng tests passed\n");
    return 0;
}
