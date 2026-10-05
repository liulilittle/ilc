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
// EMPTY implementation (audit-approved): net48 semantics is a hardware
// speculation fence; every operation in this module is a sequentially-
// consistent atomic (Win32 Interlocked* or __ATOMIC_SEQ_CST), which the
// compiler/hardware already prevents from being speculated or reordered
// across -- there is no speculative-load window for a barrier to close.
//------------------------------------------------------------------------------
void il2cpp_interlocked_speculation_barrier()
{
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

}} // namespace il2cpp::runtime
