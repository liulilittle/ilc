#pragma once
//------------------------------------------------------------------------------
// il2cpp_random.h -- managed System.Random type (M4 icall domain).
//
// Layout (aligned with the object-model convention):
//
//   struct Il2CppRandom
//   {
//       Il2CppObject base;     // object header: klass pointer (System.Random descriptor)
//       int32_t      inext;    // Knuth subtractive generator: current index
//       int32_t      inextp;   // Knuth subtractive generator: 21-ahead index
//       int32_t      seedArray[56]; // the 56-slot SeedArray (indices 1..55 used)
//   };
//
//   The state matches .NET Framework 4.8 System.Random exactly: the Knuth
//   subtractive algorithm (MSEED 161803398, MBIG int.MaxValue) as
//   implemented in mscorlib / the CoreRT compat copy
//   (reference/corert/.../System/Random.cs). Same seed -> same sequence
//   byte-for-byte with net48 -- the e2e determinism contract.
//
//   The instance is a regular GC object: generated newobj code allocates it
//   through il2cpp_object_new(il2cpp_random_class()) and then calls the
//   ctor icall to seed the state (managed objects must never be stack
//   allocated -- the collector owns the heap).
//
//   Method mapping (the transpiler routes System.Random methods here):
//     Random()        -> icall_random_ctor_default (time-based seed)
//     Random(int)     -> icall_random_ctor        (fixed seed, deterministic)
//     Next()          -> icall_random_next         (0..int.MaxValue)
//     Next(int max)   -> icall_random_next_max     (0..max-1)
//     Next(min, max)  -> icall_random_next_minmax  (min..max-1)
//     NextDouble()    -> icall_random_next_double  ([0, 1))
//     NextBytes(byte[]) -> icall_random_next_bytes (fills each element's low 8 bits)
//
//   Argument validation follows net48: Next(max<0) throws
//   ArgumentOutOfRangeException (Next(0) returns 0), Next(min>max) throws,
//   NextBytes(null) throws ArgumentNullException, and a null RECEIVER
//   (self == nullptr) throws NullReferenceException -- all transported
//   through the DotNetException carrier (il2cpp_exception.h) so generated
//   try/catch code can catch them.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed System.Random: object header + Knuth subtractive generator state
// (see header comment). No reference fields.
struct Il2CppRandom
{
    Il2CppObject base;       // object header (klass pointer)
    int32_t      inext;      // Knuth subtractive: current index
    int32_t      inextp;     // Knuth subtractive: 21-ahead index
    int32_t      seedArray[56]; // the 56-slot SeedArray (indices 1..55 used)
};

// The System.Random runtime descriptor (registered lazily on first use).
// instanceSize covers header + generator state; no reference fields.
Il2CppClass* il2cpp_random_class();

namespace runtime {

// Random() -- parameterless ctor: seeds from a time source (non-deterministic).
void icall_random_ctor_default(Il2CppObject* self);

// Random(int seed) -- fixed-seed ctor: identical seeds produce identical
// sequences (determinism contract for tests and reproducible output).
void icall_random_ctor(Il2CppObject* self, int32_t seed);

// Next() -- 32-bit value in [0, int.MaxValue].
int32_t icall_random_next(Il2CppObject* self);

// Next(int maxValue) -- value in [0, maxValue-1]; maxValue < 0 raises
// ArgumentOutOfRangeException (Next(0) == 0, net48).
int32_t icall_random_next_max(Il2CppObject* self, int32_t maxValue);

// Next(int minValue, int maxValue) -- value in [minValue, maxValue-1];
// minValue > maxValue raises ArgumentOutOfRangeException. The full int32
// width (min=Int32.MinValue, max=Int32.MaxValue) is handled without overflow.
int32_t icall_random_next_minmax(Il2CppObject* self, int32_t minValue, int32_t maxValue);

// NextDouble() -- double value in [0, 1).
double icall_random_next_double(Il2CppObject* self);

// NextBytes(byte[] array) -- fills the array's elements with random bytes
// (each element receives the low 8 bits of a fresh sample). Supports both
// the 1-byte-stride byte array and the M4 int32-slot byte storage layout
// (stride 4, low 8 bits per slot). A null array raises
// ArgumentNullException.
void icall_random_next_bytes(Il2CppObject* self, Il2CppObject* array);

} // namespace runtime

} // namespace il2cpp
