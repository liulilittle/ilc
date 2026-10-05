//------------------------------------------------------------------------------
// stopwatch.cpp -- System.Diagnostics.Stopwatch icall implementations (see
// stopwatch.h for the contract).
//
// Portability strategy (mirrors thread.cpp):
//   Windows:  ::QueryPerformanceFrequency / ::QueryPerformanceCounter --
//             the canonical high-resolution monotonic counter (available in
//             both MSVC and MinGW-w64). Frequency is read once and cached.
//   POSIX:    std::chrono::steady_clock::now() converted to nanoseconds --
//             CLOCK_MONOTONIC on Linux, guaranteed monotonic and
//             non-decreasing; the frequency is 1,000,000,000.
//
// Elapsed conversions are computed as the exact rational
// (end - start) * unit / frequency in double precision: int64 multiplication
// would overflow for long-running measurements (e.g. a week at 10 MHz is
// ~6e12 ticks, times 1e6 microseconds exceeds int64), while double holds
// the 53-bit mantissa comfortably for any realistic duration and the
// truncation toward zero matches the net48 Stopwatch integer semantics.
//------------------------------------------------------------------------------

#include "stopwatch.h"

#include <chrono>

#include "il2cpp_exception_ext.h" // il2cpp_throw_null_reference_exception
#include "il2cpp_runtime.h"       // il2cpp_object_new / il2cpp_class_register

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#endif

//------------------------------------------------------------------------------
// System.Diagnostics.Stopwatch runtime descriptor (GLOBAL scope -- the
// transpiler emits &System_Diagnostics_Stopwatch_class for newobj/isinst/castclass, the
// same contract as System_String_class in metadata.cpp). instanceSize covers
// the header + start timestamp + accumulated ticks + running flag; there are
// NO reference fields, so the collector never scans past the header
// (refFieldBitmap 0).
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Diagnostics_Stopwatch_class = {
    "System.Diagnostics.Stopwatch",          // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStopwatchObject)), // instanceSize
    0u,                                      // refFieldBitmap: no reference fields
    0,                                       // refFieldCount
    &System_Object_class,                    // baseClass (a Stopwatch IS-A object)
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

// Stopwatch.Frequency static field slot (see stopwatch.h). GLOBAL scope --
// the transpiler emits ldsfld as a direct read of the unqualified
// {TypeCppName}_sf_{FieldName} symbol. The value is process-stable
// (QueryPerformanceFrequency / the POSIX constant never change after load),
// so a dynamic initializer (not a cctor) is faithful.
int64_t System_Diagnostics_Stopwatch_sf_Frequency =
    il2cpp::runtime::il2cpp_stopwatch_get_frequency();

namespace il2cpp {

namespace {

// Layout pin: the object icalls read the state at fixed offsets.
static_assert(offsetof(Il2CppStopwatchObject, startTimestamp) == 1 * sizeof(void*),
              "Il2CppStopwatchObject: startTimestamp must sit at slot 1 (offset 8)");
static_assert(offsetof(Il2CppStopwatchObject, accumulatedTicks) == 2 * sizeof(void*),
              "Il2CppStopwatchObject: accumulatedTicks must sit at slot 2 (offset 16)");

bool g_stopwatchClassRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_stopwatch_class -- the System.Diagnostics.Stopwatch descriptor
// (lazily registered on first use; idempotent -- il2cpp_class_register
// dedupes by pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_stopwatch_class()
{
    if (!g_stopwatchClassRegistered)
    {
        il2cpp_class_register(&System_Diagnostics_Stopwatch_class);
        g_stopwatchClassRegistered = true;
    }
    return &System_Diagnostics_Stopwatch_class;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

#if defined(_WIN32)

//------------------------------------------------------------------------------
// il2cpp_stopwatch_get_frequency -- Stopwatch.Frequency: ticks per second of
// the high-resolution performance counter (QueryPerformanceFrequency). Read
// once and cached: the Windows contract guarantees a stable value for the
// process lifetime, and net48 Stopwatch exposes Frequency as readonly.
//------------------------------------------------------------------------------
int64_t il2cpp_stopwatch_get_frequency()
{
    static const int64_t cached = [] {
        LARGE_INTEGER freq{};
        ::QueryPerformanceFrequency(&freq);
        return static_cast<int64_t>(freq.QuadPart);
    }();
    return cached;
}

//------------------------------------------------------------------------------
// il2cpp_stopwatch_get_timestamp -- Stopwatch.GetTimestamp(): current raw
// counter reading. QueryPerformanceCounter is monotonic (non-decreasing
// across calls on any consistent hardware/OS pairing).
//------------------------------------------------------------------------------
int64_t il2cpp_stopwatch_get_timestamp()
{
    LARGE_INTEGER count{};
    ::QueryPerformanceCounter(&count);
    return static_cast<int64_t>(count.QuadPart);
}

#else // POSIX

//------------------------------------------------------------------------------
// Monotonic clock in nanoseconds: steady_clock is CLOCK_MONOTONIC on Linux
// (the M-stage reference platform); the frequency is therefore 1e9 ticks/s.
//------------------------------------------------------------------------------
int64_t il2cpp_stopwatch_get_frequency()
{
    return 1000000000LL;
}

int64_t il2cpp_stopwatch_get_timestamp()
{
    const auto now = std::chrono::steady_clock::now().time_since_epoch();
    return static_cast<int64_t>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(now).count());
}

#endif

//------------------------------------------------------------------------------
// il2cpp_stopwatch_get_elapsed_ticks -- Stopwatch.ElapsedTicks contract:
// raw delta in counter units, exactly end - start (no conversion -- the
// net48 semantics for a start/end pair read from GetTimestamp()).
//------------------------------------------------------------------------------
int64_t il2cpp_stopwatch_get_elapsed_ticks(int64_t startTimestamp,
                                           int64_t endTimestamp)
{
    return endTimestamp - startTimestamp;
}

//------------------------------------------------------------------------------
// Elapsed conversions: exact rational (end - start) * unit / frequency in
// double, truncated toward zero (see the file header for the overflow
// rationale). Both helpers share the same shape; only the unit multiplier
// differs (1000 for ms, 1000000 for us).
//------------------------------------------------------------------------------
int64_t il2cpp_stopwatch_get_elapsed_milliseconds(int64_t startTimestamp,
                                                  int64_t endTimestamp)
{
    const double delta = static_cast<double>(endTimestamp - startTimestamp);
    const double frequency =
        static_cast<double>(il2cpp_stopwatch_get_frequency());
    return static_cast<int64_t>((delta * 1000.0) / frequency);
}

int64_t il2cpp_stopwatch_get_elapsed_microseconds(int64_t startTimestamp,
                                                  int64_t endTimestamp)
{
    const double delta = static_cast<double>(endTimestamp - startTimestamp);
    const double frequency =
        static_cast<double>(il2cpp_stopwatch_get_frequency());
    return static_cast<int64_t>((delta * 1000000.0) / frequency);
}

//------------------------------------------------------------------------------
// Stopwatch object state.
//------------------------------------------------------------------------------

namespace {

Il2CppStopwatchObject* AsStopwatch(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppStopwatchObject*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors versionuri.cpp / arraylist.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// The live elapsed in raw counter ticks: the banked (frozen) ticks plus the
// current run's delta when running -- net48's GetRawElapsedTicks.
int64_t RawElapsedTicks(const Il2CppStopwatchObject* sw)
{
    return sw->accumulatedTicks +
        (sw->isRunning
             ? il2cpp_stopwatch_get_timestamp() - sw->startTimestamp
             : 0);
}

} // namespace

Il2CppObject* icall_stopwatch_start_new()
{
    Il2CppStopwatchObject* sw = reinterpret_cast<Il2CppStopwatchObject*>(
        il2cpp_object_new(&System_Diagnostics_Stopwatch_class));
    sw->accumulatedTicks = 0;
    sw->startTimestamp = il2cpp_stopwatch_get_timestamp();
    sw->isRunning = true;
    return reinterpret_cast<Il2CppObject*>(sw);
}

void icall_stopwatch_ctor_default(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStopwatchObject* sw = AsStopwatch(self);
    sw->startTimestamp = 0;
    sw->accumulatedTicks = 0;
    sw->isRunning = false;
}

void icall_stopwatch_start(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStopwatchObject* sw = AsStopwatch(self);
    if (sw->isRunning)
    {
        return; // net48: Start on a running Stopwatch is a no-op
    }
    sw->startTimestamp = il2cpp_stopwatch_get_timestamp();
    sw->isRunning = true;
}

void icall_stopwatch_stop(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStopwatchObject* sw = AsStopwatch(self);
    if (!sw->isRunning)
    {
        return; // net48: Stop on a stopped Stopwatch is a no-op
    }
    sw->accumulatedTicks +=
        il2cpp_stopwatch_get_timestamp() - sw->startTimestamp;
    sw->isRunning = false;
}

void icall_stopwatch_restart(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStopwatchObject* sw = AsStopwatch(self);
    sw->accumulatedTicks = 0;
    sw->startTimestamp = il2cpp_stopwatch_get_timestamp();
    sw->isRunning = true;
}

void icall_stopwatch_reset(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStopwatchObject* sw = AsStopwatch(self);
    sw->startTimestamp = 0;
    sw->accumulatedTicks = 0;
    sw->isRunning = false;
}

bool icall_stopwatch_get_is_running(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsStopwatch(self)->isRunning;
}

int64_t icall_stopwatch_get_elapsed_ticks(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return RawElapsedTicks(AsStopwatch(self));
}

int64_t icall_stopwatch_get_elapsed_milliseconds(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_stopwatch_get_elapsed_milliseconds(
        0, RawElapsedTicks(AsStopwatch(self)));
}

int64_t icall_stopwatch_get_elapsed_microseconds(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_stopwatch_get_elapsed_microseconds(
        0, RawElapsedTicks(AsStopwatch(self)));
}

Il2CppTimeSpan icall_stopwatch_get_elapsed(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // net48 GetElapsedDateTimeTicks: (double)rawElapsed * 1e7 / frequency in
    // double (same overflow rationale as the ms/us conversions).
    const double ticks = static_cast<double>(RawElapsedTicks(AsStopwatch(self)));
    const double frequency =
        static_cast<double>(il2cpp_stopwatch_get_frequency());
    return Il2CppTimeSpan(static_cast<int64_t>((ticks * 10000000.0) / frequency));
}

}} // namespace il2cpp::runtime
