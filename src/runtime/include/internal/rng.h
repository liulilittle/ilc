#pragma once
//------------------------------------------------------------------------------
// rng.h -- cryptographically secure random number generation (CSPRNG).
//
// Pure C++ -- no managed object / icall ABI involved: the helper backs BCL
// random APIs (e.g. System.Security.Cryptography.RNGCryptoServiceProvider)
// later without object interop (the same pattern sha256.h/aes.h use).
//
// SOURCE OF SEMANTICS: the OS CSPRNG. On Windows the backend is
// BCryptGenRandom (bcrypt.dll, Windows Vista+), resolved at runtime through
// LoadLibrary + GetProcAddress so no link-time -lbcrypt is needed (the
// runtime library's shared CMakeLists must not change for this module --
// guid.cpp precedent); the fallback is RtlGenRandom / SystemFunction036
// (advapi32.dll). On POSIX the backend is getrandom(2) (Linux, excluding
// Android -- see rng.cpp for the guard) with an arc4random_buf tier for the
// Apple + BSD family (macOS/iOS/FreeBSD/OpenBSD/NetBSD/DragonFlyBSD) and a
// /dev/urandom read loop fallback everywhere else. The design red line
// (docs/design/crypto-domain.md §2.5): rand() / any LCG (including
// il2cpp_random.h's 64-bit LCG, which exists ONLY to serve System.Random's
// deterministic contract) is NEVER used here -- no algorithmic RNG output is
// acceptable for this API.
//
// SEMANTIC BASELINE: net48 RNGCryptoServiceProvider.GetBytes(byte[]).
//   * il2cpp_rng_fill writes 'len' fresh CSPRNG bytes at 'bytes'.
//   * 'bytes' null with 'len' > 0 -> ArgumentNullException (the .NET
//     GetBytes(null) contract); 'len' == 0 is a no-op (never fails).
//   * Backend failure (both OS providers unavailable or failing) ->
//     CryptographicException (loud -- no silent fallback to a weak RNG).
//
// TEST PROPERTIES (tests/runtime/test_rng.cpp): the output is not
// reproducible, so tests assert structure only: length is honored, two
// fills differ, and a 256-byte fill is not all-zero (the all-zero outcome
// has probability 2^-2048). Deterministic API-shape checks cover the null
// and zero-length edges.
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>

namespace il2cpp { namespace runtime {

// Fill 'len' bytes at 'bytes' with cryptographically secure random bytes
// from the operating system CSPRNG. See the header contract above.
void il2cpp_rng_fill(uint8_t* bytes, size_t len);

}} // namespace il2cpp::runtime
