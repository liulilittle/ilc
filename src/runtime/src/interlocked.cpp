//------------------------------------------------------------------------------
// interlocked.cpp -- System.Threading.Interlocked icall implementations (see
// il2cpp_interlocked.h for the contract).
//
// Portability strategy: the Windows branch uses the Win32 Interlocked*
// primitives directly (all available in MSVC x64 and MinGW-w64; on 32-bit
// MSVC the 64-bit/Pointer names would need <intrin.h> intrinsics -- out of
// scope, the M-stage targets are x64 MinGW-w64 and x64 MSVC). The POSIX
// branch uses the GCC/Clang __atomic builtins (GCC 7.5 baseline on WSL);
// __atomic_compare_exchange_n with a temp 'expected' slot gives the exact
// Interlocked.CompareExchange old-value return contract.
//
// All operations use sequential-consistency ordering (__ATOMIC_SEQ_CST on
// POSIX; the Win32 Interlocked* are fully ordered), which is what .NET's
// Interlocked promises: the value is visible to every other thread and
// cannot be reordered across the operation.
//------------------------------------------------------------------------------

#include "il2cpp_interlocked.h"
#include "il2cpp_gc.h" // il2cpp_write_barrier (reference stores in _write_obj)

#include <atomic> // std::atomic_thread_fence (SpeculationBarrier)

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#endif

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// int32 family
//------------------------------------------------------------------------------
int32_t il2cpp_interlocked_increment(int32_t* location)
{
#if defined(_WIN32)
    return static_cast<int32_t>(::InterlockedIncrement(reinterpret_cast<volatile LONG*>(location)));
#else
    return __atomic_add_fetch(location, 1, __ATOMIC_SEQ_CST);
#endif
}

int32_t il2cpp_interlocked_decrement(int32_t* location)
{
#if defined(_WIN32)
    return static_cast<int32_t>(::InterlockedDecrement(reinterpret_cast<volatile LONG*>(location)));
#else
    return __atomic_sub_fetch(location, 1, __ATOMIC_SEQ_CST);
#endif
}

int32_t il2cpp_interlocked_exchange(int32_t* location, int32_t value)
{
#if defined(_WIN32)
    return static_cast<int32_t>(::InterlockedExchange(reinterpret_cast<volatile LONG*>(location), static_cast<LONG>(value)));
#else
    return __atomic_exchange_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

int32_t il2cpp_interlocked_add(int32_t* location, int32_t value)
{
#if defined(_WIN32)
    // InterlockedExchangeAdd stores (old + value) with two's-complement
    // wrap-around and returns the OLD value. Recover the new value with
    // unsigned arithmetic so the net48 wrap-on-overflow semantics are exact:
    // a signed old + value would be undefined behavior past INT32_MAX.
    return static_cast<int32_t>(
        static_cast<uint32_t>(::InterlockedExchangeAdd(reinterpret_cast<volatile LONG*>(location), static_cast<LONG>(value)))
        + static_cast<uint32_t>(value));
#else
    return __atomic_add_fetch(location, value, __ATOMIC_SEQ_CST);
#endif
}

int32_t il2cpp_interlocked_compare_exchange(int32_t* location, int32_t value, int32_t comparand)
{
#if defined(_WIN32)
    return static_cast<int32_t>(::InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(location), static_cast<LONG>(value), static_cast<LONG>(comparand)));
#else
    int32_t expected = comparand;
    __atomic_compare_exchange_n(location, &expected, value, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
    return expected;
#endif
}

//------------------------------------------------------------------------------
// int64 family
//------------------------------------------------------------------------------
int64_t il2cpp_interlocked_increment64(int64_t* location)
{
#if defined(_WIN32)
    return static_cast<int64_t>(::InterlockedIncrement64(reinterpret_cast<volatile LONGLONG*>(location)));
#else
    return __atomic_add_fetch(location, 1, __ATOMIC_SEQ_CST);
#endif
}

int64_t il2cpp_interlocked_decrement64(int64_t* location)
{
#if defined(_WIN32)
    return static_cast<int64_t>(::InterlockedDecrement64(reinterpret_cast<volatile LONGLONG*>(location)));
#else
    return __atomic_sub_fetch(location, 1, __ATOMIC_SEQ_CST);
#endif
}

int64_t il2cpp_interlocked_exchange64(int64_t* location, int64_t value)
{
#if defined(_WIN32)
    return static_cast<int64_t>(::InterlockedExchange64(reinterpret_cast<volatile LONGLONG*>(location), static_cast<LONGLONG>(value)));
#else
    return __atomic_exchange_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

// Interlocked.Read(ref int64) -> int64 (net48: the current value as an
// atomic load; on 64-bit targets a plain seq_cst load is the exact
// semantics, on 32-bit CompareExchange64(location,0,0) is the reference
// implementation).
int64_t il2cpp_interlocked_read64(int64_t* location)
{
#if defined(_WIN32)
    return static_cast<int64_t>(::InterlockedCompareExchange64(reinterpret_cast<volatile LONGLONG*>(location), 0LL, 0LL));
#else
    return __atomic_load_n(location, __ATOMIC_SEQ_CST);
#endif
}

int64_t il2cpp_interlocked_add64(int64_t* location, int64_t value)
{
#if defined(_WIN32)
    // Same wrap-recovery as il2cpp_interlocked_add (int32): unsigned
    // arithmetic keeps the net48 wrap-on-overflow semantics well-defined.
    return static_cast<int64_t>(
        static_cast<uint64_t>(::InterlockedExchangeAdd64(reinterpret_cast<volatile LONGLONG*>(location), static_cast<LONGLONG>(value)))
        + static_cast<uint64_t>(value));
#else
    return __atomic_add_fetch(location, value, __ATOMIC_SEQ_CST);
#endif
}

int64_t il2cpp_interlocked_compare_exchange64(int64_t* location, int64_t value, int64_t comparand)
{
#if defined(_WIN32)
    return static_cast<int64_t>(::InterlockedCompareExchange64(reinterpret_cast<volatile LONGLONG*>(location), static_cast<LONGLONG>(value), static_cast<LONGLONG>(comparand)));
#else
    int64_t expected = comparand;
    __atomic_compare_exchange_n(location, &expected, value, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
    return expected;
#endif
}

//------------------------------------------------------------------------------
// pointer (object reference) family
//------------------------------------------------------------------------------
void* il2cpp_interlocked_exchange_pointer(void** location, void* value)
{
#if defined(_WIN32)
    return ::InterlockedExchangePointer(reinterpret_cast<void* volatile*>(location), value);
#else
    return __atomic_exchange_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

void* il2cpp_interlocked_compare_exchange_pointer(void** location, void* value, void* comparand)
{
#if defined(_WIN32)
    return ::InterlockedCompareExchangePointer(reinterpret_cast<void* volatile*>(location), value, comparand);
#else
    void* expected = comparand;
    __atomic_compare_exchange_n(location, &expected, value, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);
    return expected;
#endif
}

//------------------------------------------------------------------------------
// il2cpp_interlocked_speculation_barrier -- Interlocked.SpeculationBarrier.
//
// net48 implements this as a hardware speculation barrier
// (__faststorefence()/MemoryBarrier()). The old empty body argued "there is
// no speculative-load window to close" -- that was an assumption, not a
// measurement. The contract the managed API states is a full reordering
// barrier at the call site, and the platform-portable way to express it is
// C++11 std::atomic_thread_fence(seq_cst) (the SAME primitive
// icall_thread_memory_barrier already uses). Cost is a single compiler/hardware
// fence, so the real fence is strictly safer than an empty body.
//------------------------------------------------------------------------------
void il2cpp_interlocked_speculation_barrier()
{
    std::atomic_thread_fence(std::memory_order_seq_cst);
}

//------------------------------------------------------------------------------
// P6: Interlocked float/double atomics via bit-reinterpretation.
// IEEE 754 float == 4 bytes == int32; double == 8 bytes == int64.
// The underlying int32/int64 atomic is safe for bitwise exchange/compare
// because the bit pattern is preserved (no arithmetic conversion).
//------------------------------------------------------------------------------
float il2cpp_interlocked_exchange_float(float* location, float value)
{
    int32_t old = il2cpp_interlocked_exchange(
        reinterpret_cast<int32_t*>(location),
        *reinterpret_cast<int32_t*>(&value));
    return *reinterpret_cast<float*>(&old);
}

double il2cpp_interlocked_exchange_double(double* location, double value)
{
    int64_t old = il2cpp_interlocked_exchange64(
        reinterpret_cast<int64_t*>(location),
        *reinterpret_cast<int64_t*>(&value));
    return *reinterpret_cast<double*>(&old);
}

float il2cpp_interlocked_compare_exchange_float(float* location, float value, float comparand)
{
    int32_t old = il2cpp_interlocked_compare_exchange(
        reinterpret_cast<int32_t*>(location),
        *reinterpret_cast<int32_t*>(&value),
        *reinterpret_cast<int32_t*>(&comparand));
    return *reinterpret_cast<float*>(&old);
}

double il2cpp_interlocked_compare_exchange_double(double* location, double value, double comparand)
{
    int64_t old = il2cpp_interlocked_compare_exchange64(
        reinterpret_cast<int64_t*>(location),
        *reinterpret_cast<int64_t*>(&value),
        *reinterpret_cast<int64_t*>(&comparand));
    return *reinterpret_cast<double*>(&old);
}

float il2cpp_interlocked_add_float(float* location, float value)
{
    // ExchangeAdd for float: atomic add using CAS loop.
    for (;;)
    {
        float old_val = *location;
        float new_val = old_val + value;
        float prev = il2cpp_interlocked_compare_exchange_float(location, new_val, old_val);
        if (*reinterpret_cast<int32_t*>(&prev) == *reinterpret_cast<int32_t*>(&old_val))
            return new_val;
    }
}

double il2cpp_interlocked_add_double(double* location, double value)
{
    // ExchangeAdd for double: atomic add using CAS loop.
    for (;;)
    {
        double old_val = *location;
        double new_val = old_val + value;
        double prev = il2cpp_interlocked_compare_exchange_double(location, new_val, old_val);
        if (*reinterpret_cast<int64_t*>(&prev) == *reinterpret_cast<int64_t*>(&old_val))
            return new_val;
    }
}

//------------------------------------------------------------------------------
// Volatile.Read/Volatile.Write family (nb4 GC-SPOT) -- SEQ_CST fence
// atomics. Read = an atomic load through a compare-exchange-with-0 trick on
// Windows (InterlockedCompareExchange on a volatile LONG reads the slot
// atomically and is freely reorderable); Write = InterlockedExchange (a full
// fence store). The POSIX branch uses the __atomic load/store builtins with
// __ATOMIC_SEQ_CST.
//------------------------------------------------------------------------------
int32_t il2cpp_volatile_read_i4(int32_t* location)
{
#if defined(_WIN32)
    return static_cast<int32_t>(::InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(location), 0, 0));
#else
    return __atomic_load_n(location, __ATOMIC_SEQ_CST);
#endif
}

void il2cpp_volatile_write_i4(int32_t* location, int32_t value)
{
#if defined(_WIN32)
    ::InterlockedExchange(reinterpret_cast<volatile LONG*>(location), static_cast<LONG>(value));
#else
    __atomic_store_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

// Interlocked.And / Interlocked.Or: read-modify-write with a bitwise op.
// Neither Windows Interlocked* nor the __atomic builtins expose an AND/OR
// primitive, so each is a CAS retry loop -- the standard construction, and
// the reason these cannot be a single intrinsic call.
int32_t il2cpp_interlocked_and(int32_t* location, int32_t value)
{
    int32_t old = __atomic_load_n(location, __ATOMIC_SEQ_CST);
    while (!__atomic_compare_exchange_n(location, &old, old & value, false,
                                         __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST))
    {
    }
    return old;
}

int64_t il2cpp_interlocked_and64(int64_t* location, int64_t value)
{
    int64_t old = __atomic_load_n(location, __ATOMIC_SEQ_CST);
    while (!__atomic_compare_exchange_n(location, &old, old & value, false,
                                         __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST))
    {
    }
    return old;
}

int32_t il2cpp_interlocked_or(int32_t* location, int32_t value)
{
    int32_t old = __atomic_load_n(location, __ATOMIC_SEQ_CST);
    while (!__atomic_compare_exchange_n(location, &old, old | value, false,
                                         __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST))
    {
    }
    return old;
}

int64_t il2cpp_interlocked_or64(int64_t* location, int64_t value)
{
    int64_t old = __atomic_load_n(location, __ATOMIC_SEQ_CST);
    while (!__atomic_compare_exchange_n(location, &old, old | value, false,
                                         __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST))
    {
    }
    return old;
}
int64_t il2cpp_volatile_read_i8(int64_t* location)
{
#if defined(_WIN32)
    return static_cast<int64_t>(::InterlockedCompareExchange64(reinterpret_cast<volatile LONG64*>(location), 0, 0));
#else
    return __atomic_load_n(location, __ATOMIC_SEQ_CST);
#endif
}

void il2cpp_volatile_write_i8(int64_t* location, int64_t value)
{
#if defined(_WIN32)
    ::InterlockedExchange64(reinterpret_cast<volatile LONG64*>(location), static_cast<LONG64>(value));
#else
    __atomic_store_n(location, value, __ATOMIC_SEQ_CST);
#endif
}

//------------------------------------------------------------------------------
// Volatile widths beyond i4/i8.
//
// net48's Thread.VolatileRead/VolatileWrite declare 13 overloads each and
// System.Threading.Volatile declares 14 (the extra one is generic T). Only
// int and long had a runtime entry, so the other widths -- byte, sbyte, short,
// ushort, uint, ulong, float, double, IntPtr, UIntPtr, object -- failed to
// transpile with "no BCL mapping in M5".
//
// SLOT MODEL (why the narrow entries take a 4-byte pointer): the transpiler
// models every scalar local as an int32_t slot (an int64_t slot for the 8-byte
// family), so a byte/sbyte/short/ushort/char/bool managed local is ADDRESSED
// as int32_t* at the ABI. The width is therefore a property of the ACCESS, not
// of the pointer: each entry reinterprets the slot to its true width, touches
// exactly that many bytes, and returns a value the int32 slot can hold. This
// is also why the write entries take int32_t rather than a narrow value: the
// caller's expression is the same int32 carrier the assignment used.
//
// 1-byte and 2-byte accesses cannot use the 4-byte InterlockedCompareExchange
// primitives (they would read/write past the intended width), so they use
// __atomic builtins on the narrow view on both toolchains; the i4/i8 entries
// keep the Windows/POSIX split above.
// The obj variant is an atomic load plus a write barrier on the store, which
// is the reference-store contract every other reference field assignment uses.
//------------------------------------------------------------------------------
int32_t il2cpp_volatile_read_i1(int32_t* location)
{
    return static_cast<int32_t>(__atomic_load_n(reinterpret_cast<int8_t*>(location), __ATOMIC_SEQ_CST));
}

void il2cpp_volatile_write_i1(int32_t* location, int32_t value)
{
    __atomic_store_n(reinterpret_cast<int8_t*>(location), static_cast<int8_t>(value), __ATOMIC_SEQ_CST);
}

int32_t il2cpp_volatile_read_i2(int32_t* location)
{
    return static_cast<int32_t>(__atomic_load_n(reinterpret_cast<int16_t*>(location), __ATOMIC_SEQ_CST));
}

void il2cpp_volatile_write_i2(int32_t* location, int32_t value)
{
    __atomic_store_n(reinterpret_cast<int16_t*>(location), static_cast<int16_t>(value), __ATOMIC_SEQ_CST);
}

// float rides the DOUBLE slot: the transpiler models every floating local as a
// double (locKind "d"), so a float local is ADDRESSED as double*. The float
// entries therefore take double* and do the atomic access on the low 4 bytes,
// then widen the float answer into the double slot. Going through the i4 entry
// would be a type error (double* -> int32_t*) and would read the wrong half.
//
// __atomic_load_n does NOT accept float/double pointers (measured: g++ 16.1.0
// rejects both with "operand type 'float*' is incompatible"), and the generic
// __atomic_load/__atomic_store form is 3-argument on this toolchain (the
// 4-argument form with an explicit memory model is a hard error). The
// workaround is the standard bit-pattern carrier: an IEEE float is 4 bytes and
// a double 8, so the atomic access is done on the SAME WIDTH integer and the
// bits are memcpy'd across. This is exact -- no numeric conversion, no
// reinterpretation UB -- and it is the same technique the Interlocked float/
// double exchange already uses.
float il2cpp_volatile_read_float(double* location)
{
    int32_t bits = __atomic_load_n(reinterpret_cast<int32_t*>(location), __ATOMIC_SEQ_CST);
    float f;
    __builtin_memcpy(&f, &bits, sizeof(f));
    return f;
}

void il2cpp_volatile_write_float(double* location, float value)
{
    int32_t bits;
    __builtin_memcpy(&bits, &value, sizeof(bits));
    __atomic_store_n(reinterpret_cast<int32_t*>(location), bits, __ATOMIC_SEQ_CST);
}

double il2cpp_volatile_read_double(double* location)
{
    int64_t bits = __atomic_load_n(reinterpret_cast<int64_t*>(location), __ATOMIC_SEQ_CST);
    double d;
    __builtin_memcpy(&d, &bits, sizeof(d));
    return d;
}

void il2cpp_volatile_write_double(double* location, double value)
{
    int64_t bits;
    __builtin_memcpy(&bits, &value, sizeof(bits));
    __atomic_store_n(reinterpret_cast<int64_t*>(location), bits, __ATOMIC_SEQ_CST);
}

intptr_t il2cpp_volatile_read_ip(intptr_t* location)
{
    return __atomic_load_n(location, __ATOMIC_SEQ_CST);
}

void il2cpp_volatile_write_ip(intptr_t* location, intptr_t value)
{
    __atomic_store_n(location, value, __ATOMIC_SEQ_CST);
}

// UIntPtr rides its OWN unsigned pointer-width slot (the transpiler declares the
// local uintptr_t), so it needs a separate entry: an intptr_t* parameter would
// reject the uintptr_t* address at compile time (measured C2440-class error).
uintptr_t il2cpp_volatile_read_up(uintptr_t* location)
{
    return __atomic_load_n(location, __ATOMIC_SEQ_CST);
}

void il2cpp_volatile_write_up(uintptr_t* location, uintptr_t value)
{
    __atomic_store_n(location, value, __ATOMIC_SEQ_CST);
}

Il2CppObject* il2cpp_volatile_read_obj(Il2CppObject** location)
{
    return __atomic_load_n(location, __ATOMIC_SEQ_CST);
}

void il2cpp_volatile_write_obj(Il2CppObject** location, Il2CppObject* value)
{
    __atomic_store_n(location, value, __ATOMIC_SEQ_CST);
    il2cpp_write_barrier(reinterpret_cast<void**>(location), value);
}

}} // namespace il2cpp::runtime
