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

namespace il2cpp {
struct Il2CppObject; // forward: the reference-slot Volatile entries below

namespace runtime {

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

// Interlocked.Read(ref int64) -> int64 (net48 current value, atomic load).
int64_t il2cpp_interlocked_read64(int64_t* location);

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
// Implemented as a REAL full reordering barrier:
// std::atomic_thread_fence(std::memory_order_seq_cst) (the same primitive as
// Thread.MemoryBarrier). The previous empty body was an assumption, not a
// measured equivalence.
void il2cpp_interlocked_speculation_barrier();

// P6: Interlocked float/double atomics via bit-reinterpretation.
// IEEE 754 float/double have the same size as int32/int64 respectively,
// so the underlying int32/int64 atomic is safe for bitwise exchange/compare.
float il2cpp_interlocked_exchange_float(float* location, float value);
double il2cpp_interlocked_exchange_double(double* location, double value);
float il2cpp_interlocked_compare_exchange_float(float* location, float value, float comparand);
double il2cpp_interlocked_compare_exchange_double(double* location, double value, double comparand);
float il2cpp_interlocked_add_float(float* location, float value);
double il2cpp_interlocked_add_double(double* location, double value);
// Interlocked.And / Interlocked.Or (.NET 9 additions, present in the shipped
// CoreLib). net8 has no managed body for them either -- they are bodiless
// public wrappers -- so an unmapped arm emitted an undefined symbol.
int32_t il2cpp_interlocked_and(int32_t* location, int32_t value);
int64_t il2cpp_interlocked_and64(int64_t* location, int64_t value);
int32_t il2cpp_interlocked_or(int32_t* location, int32_t value);
int64_t il2cpp_interlocked_or64(int64_t* location, int64_t value);

// nb4 GC-SPOT: the Volatile.Read/Volatile.Write family -- SEQ_CST atomic
// loads/stores (the .NET "volatile" semantics: full fence on the operation,
// no reordering across it). Serves BOTH Thread.VolatileRead/VolatileWrite
// and System.Threading.Volatile.Read/Write (net48: both are the same
// fence-atomics). i4 = int32 slot; i8 = int64 slot.
int32_t il2cpp_volatile_read_i4(int32_t* location);
void il2cpp_volatile_write_i4(int32_t* location, int32_t value);
int64_t il2cpp_volatile_read_i8(int64_t* location);
void il2cpp_volatile_write_i8(int64_t* location, int64_t value);

// The remaining widths net48's Thread.VolatileRead/VolatileWrite declare
// (byte/sbyte/bool -> i1, short/ushort/char -> i2, IntPtr/UIntPtr -> ip,
// reference slot -> obj). i4/i8 above already cover int/uint/single and
// long/ulong/double.
//
// NARROW SLOTS TAKE A 4-BYTE POINTER: the transpiler models every scalar local
// as an int32_t slot (int64_t for the 8-byte family), so a byte managed local
// is ADDRESSED as int32_t*. The entry reinterprets to its true width, touches
// exactly that many bytes, and returns a value the int32 slot holds. Changing
// these to int8_t*/int16_t* would not compile against the generated locals.
int32_t il2cpp_volatile_read_i1(int32_t* location);
void il2cpp_volatile_write_i1(int32_t* location, int32_t value);
int32_t il2cpp_volatile_read_i2(int32_t* location);
void il2cpp_volatile_write_i2(int32_t* location, int32_t value);
intptr_t il2cpp_volatile_read_ip(intptr_t* location);
void il2cpp_volatile_write_ip(intptr_t* location, intptr_t value);
// UIntPtr rides its OWN unsigned pointer-width slot (the transpiler declares the
// local uintptr_t), so a separate entry is required -- an intptr_t* parameter
// would reject the uintptr_t* address.
uintptr_t il2cpp_volatile_read_up(uintptr_t* location);
void il2cpp_volatile_write_up(uintptr_t* location, uintptr_t value);
// float and double ride the DOUBLE slot: the transpiler models every floating
// local as a double, so a float local is addressed as double* too. The float
// entries reinterpret to the low 4 bytes; using i4 here would be a type error.
float il2cpp_volatile_read_float(double* location);
void il2cpp_volatile_write_float(double* location, float value);
double il2cpp_volatile_read_double(double* location);
void il2cpp_volatile_write_double(double* location, double value);
// Reference slot: an atomic load/store plus the standard write barrier on the
// store, so a young reference written into an old object marks the card.
Il2CppObject* il2cpp_volatile_read_obj(Il2CppObject** location);
void il2cpp_volatile_write_obj(Il2CppObject** location, Il2CppObject* value);

}} // namespace il2cpp::runtime
