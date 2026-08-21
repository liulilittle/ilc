#pragma once
//------------------------------------------------------------------------------
// il2cpp_interlocked.h -- System.Threading.Interlocked icall contract (see
// interlocked.cpp for the implementation).
//
// Atomic operations over caller-supplied memory (the same location
// semantics as .NET's Interlocked): every function is safe to call
// concurrently from multiple threads, and the results are globally visible
// (sequentially consistent; the compiler barriers the operations like the
// .NET memory model).
//
// Return-value semantics (identical to the .NET API):
//   * increment / decrement / add : the NEW value stored at *location
//   * exchange                  : the OLD value that was at *location
//   * compare_exchange          : the OLD value that was at *location (the
//     store to *location happens ONLY when old == comparand)
//
// Arithmetic wrap-around: like .NET's Interlocked, increment/decrement/add
// use unchecked two's-complement arithmetic -- the result wraps at the width
// boundary (INT32_MAX + 1 -> INT32_MIN, and so on) without overflow traps.
//
// Location alignment: as in .NET, *location must be naturally aligned for its
// width (the managed allocator guarantees this for ref parameters); the
// atomic hardware instructions on some targets fault on misaligned access.
//
// Overload families:
//   * (no suffix)      int32  -- Interlocked.Increment(int32&) etc.
//   * 64 suffix        int64  -- Interlocked.Increment(int64&) etc.
//   * pointer suffix   void*  -- the object-reference forms; in the AOT
//     object model a managed reference is a raw pointer, so the reference
//     overloads exchange/compare raw pointers. int32_t compare_exchange is
//     the primitive used to build spin locks (see test_interlocked.cpp for
//     a complete lock built from it).
//
// Platform mapping (see interlocked.cpp):
//   Windows:  ::InterlockedIncrement/Decrement/Exchange/ExchangeAdd/
//             CompareExchange (+ 64-bit and Pointer variants)
//   POSIX:    GCC/Clang __atomic builtins with __ATOMIC_SEQ_CST
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// Interlocked.Increment(ref int32) -> int32 (returns the NEW value).
int32_t il2cpp_interlocked_increment(int32_t* location);

// Interlocked.Increment(ref int64) -> int64 (returns the NEW value).
int64_t il2cpp_interlocked_increment64(int64_t* location);

// Interlocked.Decrement(ref int32) -> int32 (returns the NEW value).
int32_t il2cpp_interlocked_decrement(int32_t* location);

// Interlocked.Decrement(ref int64) -> int64 (returns the NEW value).
int64_t il2cpp_interlocked_decrement64(int64_t* location);

// Interlocked.Exchange(ref int32, int32) -> int32 (returns the OLD value).
int32_t il2cpp_interlocked_exchange(int32_t* location, int32_t value);

// Interlocked.Exchange(ref int64, int64) -> int64 (returns the OLD value).
int64_t il2cpp_interlocked_exchange64(int64_t* location, int64_t value);

// Interlocked.Exchange(ref object, object) -> object; exchanges raw
// pointers (returns the OLD pointer).
void* il2cpp_interlocked_exchange_pointer(void** location, void* value);

// Interlocked.Add(ref int32, int32) -> int32 (returns the NEW sum).
int32_t il2cpp_interlocked_add(int32_t* location, int32_t value);

// Interlocked.Add(ref int64, int64) -> int64 (returns the NEW sum).
int64_t il2cpp_interlocked_add64(int64_t* location, int64_t value);

// Interlocked.CompareExchange(ref int32, int32, int32) -> int32 (returns
// the OLD value; stores 'value' only when old == comparand).
int32_t il2cpp_interlocked_compare_exchange(int32_t* location, int32_t value, int32_t comparand);

// Interlocked.CompareExchange(ref int64, int64, int64) -> int64 (returns
// the OLD value; stores 'value' only when old == comparand).
int64_t il2cpp_interlocked_compare_exchange64(int64_t* location, int64_t value, int64_t comparand);

// Interlocked.CompareExchange(ref object, object, object) -> object; raw
// pointer version (returns the OLD pointer; stores 'value' only when
// old == comparand).
void* il2cpp_interlocked_compare_exchange_pointer(void** location, void* value, void* comparand);

// Interlocked.SpeculationBarrier() -- the net48 hardware speculation fence.
// EMPTY implementation (audit-approved): every Interlocked operation in this
// runtime is a sequentially-consistent atomic (Win32 Interlocked* or
// __ATOMIC_SEQ_CST), which the compiler already prevents from being
// speculated/reordered across, so there is no speculative-load window for a
// barrier to close.
void il2cpp_interlocked_speculation_barrier();

}} // namespace il2cpp::runtime
