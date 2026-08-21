//------------------------------------------------------------------------------
// random.cpp -- System.Random icall implementations (see il2cpp_random.h).
//
// PRNG: .NET Framework 4.8 System.Random -- the Knuth SUBTRACTIVE generator
// (MSEED 161803398, MBIG int.MaxValue, a 56-slot SeedArray, indices 1..55
// used). The implementation below is a faithful port of mscorlib / the
// CoreRT compatibility copy (reference/corert/.../System/Random.cs), so a
// fixed seed produces the EXACT net48 sequence -- the e2e determinism
// contract (verified against the net48 oracle: Random(42) prints
// 1434747710 / 302596119 / 269548474 / 5 / 5 / 0.262593 / 205,59,153,134).
//
// Method semantics (net48-aligned, corert reference):
//   Next()           = InternalSample()                       [0, MBIG]
//   Next(max)        = (int)(Sample() * max)  for max >= 0    [0, max-1]
//                      (max < 0 -> ArgumentOutOfRangeException; Next(0)==0)
//   Next(min, max)   = range <= int.MaxValue:
//                          (int)(Sample() * range) + min
//                      range >  int.MaxValue (full int32 span):
//                          (int)(GetSampleForLargeRange() * range) + min
//   NextDouble()     = Sample()                                [0, 1)
//   NextBytes(array) = (byte)InternalSample() per element
//
//   Sample() = InternalSample() * (1.0 / MBIG).
//   InternalSample: subtractive step seedArray[inext] -= seedArray[inextp]
//   (indices wrap mod 55 at 1..55), == MBIG -> MBIG-1, < 0 -> += MBIG.
//   GetSampleForLargeRange: sign from a second sample, then
//   (result + MBIG-1) / (2*MBIG-1) -- net48's large-range double.
//
// Argument validation raises the managed exception carrier (DotNetException,
// il2cpp_exception_ext.h canonical factories) so generated try/catch code
// catches them with .NET exception semantics.
//------------------------------------------------------------------------------

#include "il2cpp_random.h"

#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h"

#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>

namespace il2cpp {

namespace {

// The System.Random runtime descriptor. Registered lazily (constraint #12:
// no global-constructor allocation). One instance holds the object header +
// the Knuth generator state (inext/inextp indices + 56-slot SeedArray);
// there are no reference fields, so the collector scans nothing beyond the
// header.
Il2CppClass g_randomClass = {
    "System.Random",               // name
    static_cast<int32_t>(sizeof(Il2CppRandom)), // instanceSize (header + generator state)
    0u,                            // refFieldBitmap: no reference fields
    0,                             // refFieldCount
    nullptr,                       // baseClass
    nullptr,                       // vtable
    0,                             // vtableSize
    nullptr,                       // interfaces
    0,                             // interfaceCount
};

bool g_randomRegistered = false;

void EnsureRandomRegistered()
{
    if (g_randomRegistered) return;
    il2cpp_class_register(&g_randomClass);
    g_randomRegistered = true;
}

// net48 Random constants (mscorlib / corert compat copy).
constexpr int32_t kMBig  = 2147483647; // int.MaxValue
constexpr int32_t kMSeed = 161803398;

// InternalSample -- one subtractive step (corert Random.InternalSample).
// Range [0, MBIG]; the == MBIG and < 0 corrections are applied exactly as
// net48 does, so the sequence is bit-identical to net48 for every seed.
int32_t InternalSample(Il2CppRandom* self)
{
    int32_t locINext = self->inext;
    int32_t locINextp = self->inextp;
    if (++locINext >= 56) locINext = 1;
    if (++locINextp >= 56) locINextp = 1;
    int32_t retVal = self->seedArray[locINext] - self->seedArray[locINextp];
    if (retVal == kMBig) retVal--;
    if (retVal < 0) retVal += kMBig;
    self->seedArray[locINext] = retVal;
    self->inext = locINext;
    self->inextp = locINextp;
    return retVal;
}

// Sample -- InternalSample scaled to [0, 1).
double Sample(Il2CppRandom* self)
{
    return InternalSample(self) * (1.0 / static_cast<double>(kMBig));
}

// GetSampleForLargeRange -- net48's large-range double for Next(min,max)
// when the range spans more than int.MaxValue (corert Random.cs).
double GetSampleForLargeRange(Il2CppRandom* self)
{
    const int32_t result = InternalSample(self);
    const bool negative = (InternalSample(self) % 2 == 0);
    int32_t r = result;
    if (negative)
    {
        r = -r;
    }
    double d = static_cast<double>(r);
    d += static_cast<double>(kMBig - 1); // [0 .. 2*MBIG - 1)
    d /= static_cast<double>(2 * static_cast<uint32_t>(kMBig) - 1);
    return d;
}

// Throws the managed ArgumentOutOfRangeException via the canonical factory
// (il2cpp_exception_ext.h) -- the full {Type},{Type},{Base...} chain so base
// catches match. An explicit message wins; never returns.
void ThrowArgOutOfRange(const char* message)
{
    il2cpp_throw_argument_out_of_range_exception(nullptr, message);
}

// Defensive null-receiver guard (batch-22a audit convention, mirrors
// arraylist.cpp / streamio.cpp / stringext.cpp): generated code null-checks
// first, but the icall contract is net48 NRE semantics -- a null receiver
// throws NullReferenceException through the canonical factory, never a
// crash.
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_random_class -- the System.Random descriptor (lazily registered).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_random_class()
{
    EnsureRandomRegistered();
    return &g_randomClass;
}

namespace runtime {

//------------------------------------------------------------------------------
// icall_random_ctor -- Random(int seed): the net48 Knuth seed fill (corert
// Random.cs:44-74). The SeedArray is filled from MSEED - |seed|, then two
// passes scramble it; indices start at inext=0 / inextp=21. Identical seeds
// produce the identical net48 sequence.
//------------------------------------------------------------------------------
void icall_random_ctor(Il2CppObject* self, int32_t seed)
{
    ThrowIfNullReceiver(self);
    Il2CppRandom* r = reinterpret_cast<Il2CppRandom*>(self);
    const int32_t subtraction = (seed == INT32_MIN) ? kMBig
                                : (seed < 0 ? -seed : seed);
    int32_t mj = kMSeed - subtraction;
    r->seedArray[55] = mj;
    int32_t mk = 1;
    int32_t ii = 0;
    for (int32_t i = 1; i < 55; ++i)
    {
        ii += 21;
        if (ii >= 55) ii -= 55;
        r->seedArray[ii] = mk;
        mk = mj - mk;
        if (mk < 0) mk += kMBig;
        mj = r->seedArray[ii];
    }
    for (int32_t k = 1; k < 5; ++k)
    {
        for (int32_t i = 1; i < 56; ++i)
        {
            int32_t n = i + 30;
            if (n >= 55) n -= 55;
            r->seedArray[i] -= r->seedArray[1 + n];
            if (r->seedArray[i] < 0) r->seedArray[i] += kMBig;
        }
    }
    r->inext = 0;
    r->inextp = 21;
}

//------------------------------------------------------------------------------
// icall_random_ctor_default -- Random(). Seeds from a high-resolution time
// source (steady clock nanosecond count), mirroring .NET's time-based
// default seed. Non-deterministic by design.
//------------------------------------------------------------------------------
void icall_random_ctor_default(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const auto now = std::chrono::steady_clock::now();
    const uint64_t ns = static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(now.time_since_epoch()).count());
    const uint64_t seed = (ns ^ (ns >> 32)) & 0xFFFFFFFFu;
    icall_random_ctor(self, static_cast<int32_t>(seed));
}

//------------------------------------------------------------------------------
// icall_random_next -- Next(): [0, int.MaxValue] (InternalSample, net48).
//------------------------------------------------------------------------------
int32_t icall_random_next(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return InternalSample(reinterpret_cast<Il2CppRandom*>(self));
}

//------------------------------------------------------------------------------
// icall_random_next_max -- Next(int maxValue): [0, maxValue-1] via
// (int)(Sample() * maxValue) -- the net48 formula (no rejection, the same
// modulo bias .NET exhibits). maxValue < 0 raises ArgumentOutOfRangeException;
// Next(0) == 0 (net48).
//------------------------------------------------------------------------------
int32_t icall_random_next_max(Il2CppObject* self, int32_t maxValue)
{
    ThrowIfNullReceiver(self);
    if (maxValue < 0)
    {
        ThrowArgOutOfRange("Random.Next(maxValue): maxValue must not be negative.");
    }
    return static_cast<int32_t>(
        Sample(reinterpret_cast<Il2CppRandom*>(self)) * static_cast<double>(maxValue));
}

//------------------------------------------------------------------------------
// icall_random_next_minmax -- Next(int minValue, int maxValue):
// [minValue, maxValue-1]. The net48 formula:
//   range <= int.MaxValue -> (int)(Sample() * range) + min
//   range >  int.MaxValue (full int32 span) -> GetSampleForLargeRange path.
// minValue > maxValue raises ArgumentOutOfRangeException.
//------------------------------------------------------------------------------
int32_t icall_random_next_minmax(Il2CppObject* self, int32_t minValue, int32_t maxValue)
{
    ThrowIfNullReceiver(self);
    if (minValue > maxValue)
    {
        ThrowArgOutOfRange("Random.Next(minValue, maxValue): minValue must be <= maxValue.");
    }
    Il2CppRandom* r = reinterpret_cast<Il2CppRandom*>(self);
    const int64_t range = static_cast<int64_t>(maxValue) - static_cast<int64_t>(minValue);
    if (range <= static_cast<int64_t>(kMBig))
    {
        return static_cast<int32_t>(Sample(r) * static_cast<double>(range))
             + minValue;
    }
    return static_cast<int32_t>(
        static_cast<int64_t>(GetSampleForLargeRange(r) * static_cast<double>(range))
        + static_cast<int64_t>(minValue));
}

//------------------------------------------------------------------------------
// icall_random_next_double -- NextDouble(): [0, 1) (Sample, net48).
//------------------------------------------------------------------------------
double icall_random_next_double(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return Sample(reinterpret_cast<Il2CppRandom*>(self));
}

//------------------------------------------------------------------------------
// icall_random_next_bytes -- NextBytes(byte[] array). Fills every element
// with the low 8 bits of a fresh InternalSample (net48: (byte)InternalSample()
// per element). Two storage layouts are supported:
//   * 1-byte stride (elemSize == 1): the canonical byte[] payload.
//   * 4-byte stride (elemSize == 4): the M4 type-system layout, where byte
//     values live in int32 slots -- only the low 8 bits of each slot are
//     written (upper bits stay 0, matching byte semantics).
// A null array raises ArgumentNullException; out-of-range is impossible
// (bounds are pinned to the array's recorded length).
//------------------------------------------------------------------------------
void icall_random_next_bytes(Il2CppObject* self, Il2CppObject* array)
{
    ThrowIfNullReceiver(self);
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(array);
    if (arr == nullptr)
    {
        il2cpp_throw_argument_null_exception("array");
    }
    Il2CppRandom* r = reinterpret_cast<Il2CppRandom*>(self);
    for (int32_t i = 0; i < arr->length; ++i)
    {
        const uint8_t byte = static_cast<uint8_t>(InternalSample(r) & 0xFFu);
        if (arr->elemSize == 1)
        {
            arr->data[i] = static_cast<char>(byte);
        }
        else if (arr->elemSize == 4)
        {
            reinterpret_cast<int32_t*>(arr->data)[i] = static_cast<int32_t>(byte);
        }
        // else: unsupported stride -- skip defensively (never write out of bounds)
    }
}

} // namespace runtime

} // namespace il2cpp
