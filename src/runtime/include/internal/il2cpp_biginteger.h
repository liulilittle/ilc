#pragma once
//------------------------------------------------------------------------------
// il2cpp_biginteger.h -- System.Numerics.BigInteger runtime support
// (biginteger.cpp).
//
// BigInteger is a .NET VALUE type with VARIABLE-LENGTH storage (an internal
// sign + limb array). This runtime represents it as an 8-byte value slot
// holding a POINTER to a heap-allocated native state:
//
//   struct Il2CppBigInteger { BigIntegerState* state; };  // 8 bytes
//
//   struct BigIntegerState {                              // native, heap
//       bool negative;                                    // sign
//       std::vector<uint32_t> limbs;                      // little-endian
//   };
//
// The value slot is a native pointer (NOT a GC reference): the collector
// never scans it, and the native state is never freed (no finalizer in this
// runtime -- the wait-handle/stream-handle precedent). BigInteger is
// IMMUTABLE (net48 semantics): every operation allocates a NEW state and
// never mutates its inputs, so pointer-copied values (BigInteger b = a) stay
// correct. The state is heap-allocated by the construction icalls.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48), the project's reference
// baseline:
//   * Value = (-1)^negative * sum(limbs[i] * 2^(32*i)) -- little-endian u32
//     limbs, no leading zero limbs (zero is { negative=false, limbs={} }).
//   * op_Addition/Subtraction/Multiply/Division/Remainder/UnaryNegation:
//     arbitrary-precision arithmetic (schoolbook mul, long division).
//   * op_Equality/Inequality/LessThan/GreaterThan/LessThanOrEqual/
//     GreaterThanOrEqual + CompareTo/Equals: sign + magnitude comparison.
//   * ToString(): decimal representation (repeated division by 10).
//   * op_Implicit(int32/int64) / .ctor(int32/int64): construction.
//   * Division/Remainder by zero throws DivideByZeroException.
//
// The icalls take Il2CppBigInteger BY VALUE (8 bytes = one register on x64 --
// the managed value-type ABI, like Il2CppDateTime). The result-slot-first
// shape (the decimal ctor precedent): value-PRODUCING icalls take the result
// slot as their FIRST parameter -- icall_biginteger_add(out, a, b).
//------------------------------------------------------------------------------

#include "il2cpp_object.h" // Il2CppObject (for the managed-object ABI)

#include <cstdint>

namespace il2cpp { namespace runtime {

// Native big-integer state (forward; defined in biginteger.cpp).
struct BigIntegerState;

// System.Numerics.BigInteger's 8-byte value image: a pointer to the native
// state (see header comment). NOT a GC reference.
struct Il2CppBigInteger
{
    BigIntegerState* state;
};

// --- construction -----------------------------------------------------------

// BigInteger(int32 value) / BigInteger(int64 value): allocate a state for the
// value. Result-slot-first shape.
void icall_biginteger_from_int32(Il2CppBigInteger* out, int32_t value);
void icall_biginteger_from_int64(Il2CppBigInteger* out, int64_t value);

// --- arithmetic (result-slot-first; inputs never mutated) -------------------

// out = a + b / a - b / a * b / a / b / a % b. Division/Remainder by zero
// throws DivideByZeroException.
void icall_biginteger_add(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b);
void icall_biginteger_sub(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b);
void icall_biginteger_mul(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b);
void icall_biginteger_div(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b);
void icall_biginteger_rem(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b);

// out = -a.
void icall_biginteger_negate(Il2CppBigInteger* out, Il2CppBigInteger a);

// --- comparison / equality --------------------------------------------------

// CompareTo: -1 / 0 / 1 (sign + magnitude).
int32_t icall_biginteger_compare(Il2CppBigInteger a, Il2CppBigInteger b);

// Equals: true iff equal (sign + magnitude).
bool icall_biginteger_equals(Il2CppBigInteger a, Il2CppBigInteger b);

// --- conversion -------------------------------------------------------------

// (int32)BigInteger: truncate toward zero; out of int32 range throws
// OverflowException.
int32_t icall_biginteger_to_int32(Il2CppBigInteger a);

// --- formatting -------------------------------------------------------------

// ToString(): the decimal representation (with a leading '-' for negatives).
Il2CppObject* icall_biginteger_to_string(Il2CppBigInteger a);

}} // namespace il2cpp::runtime
