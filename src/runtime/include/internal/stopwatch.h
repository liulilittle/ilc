#pragma once
//------------------------------------------------------------------------------
// stopwatch.h -- System.Diagnostics.Stopwatch runtime support.
//
// Stopwatch measures wall time with the platform's high-resolution counter.
// On .NET the static entry points are:
//
//   * Stopwatch.Frequency  (static readonly long)  -- counter ticks per second
//   * Stopwatch.GetTimestamp() (static long)       -- raw counter reading
//   * Stopwatch.ElapsedTicks (instance long)       -- raw delta in counter
//     units (net48: (GetTimestamp() - StartTimestamp) in Stopwatch ticks)
//
// The runtime surface is the three primitives above plus elapsed conversions
// (milliseconds / microseconds), which Stopwatch computes internally from
// raw deltas and the frequency.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour:
//   * Frequency is stable for the process lifetime (Windows
//     QueryPerformanceFrequency, POSIX monotonic clock in nanoseconds).
//   * GetTimestamp() is strictly monotonic (never goes backwards while the
//     machine runs) and has sub-millisecond resolution.
//   * ElapsedTicks(start, end) == end - start: raw counter units, no
//     conversion -- this is exactly the net48 Stopwatch.ElapsedTicks
//     contract for a start/end pair read from GetTimestamp().
//   * Elapsed conversions round toward zero from the exact rational
//     (delta * unit / frequency), computed in double to avoid int64
//     overflow for long-running measurements.
//
// Platform mapping (see stopwatch.cpp):
//   Windows: ::QueryPerformanceFrequency / ::QueryPerformanceCounter
//   POSIX:   std::chrono::steady_clock (CLOCK_MONOTONIC on Linux) in
//            nanoseconds, so Frequency == 1,000,000,000.
//
// OBJECT MODEL -- System.Diagnostics.Stopwatch (M6 object closure): an
// instance is a managed object allocated through the System_Diagnostics_Stopwatch_class
// descriptor (Il2CppStopwatchObject below): header + start timestamp +
// accumulated (frozen) ticks + isRunning flag, NO GC reference fields
// (refFieldBitmap 0). The object memory stores the state directly (the
// Il2CppVersionObject precedent -- versionuri.h). Generated newobj allocates
// via il2cpp_object_new(&System_Diagnostics_Stopwatch_class) (the transpiler's
// BclAllocExpr fallback emits this mangled global symbol) and the object
// icalls below (il2cpp::runtime::icall_stopwatch_*) own the read/write ABI;
// StartNew allocates internally. System_Diagnostics_Stopwatch_class is a GLOBAL-scope
// descriptor (the same contract as System_String_class in il2cpp_metadata.h)
// because generated isinst/castclass code references &System_Diagnostics_Stopwatch_class
// directly.
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)
#include "il2cpp_time.h"     // Il2CppTimeSpan (get_Elapsed result)

// System.Diagnostics.Stopwatch runtime descriptor (GLOBAL scope -- the
// transpiler emits &System_Diagnostics_Stopwatch_class for newobj/isinst/castclass;
// defined in stopwatch.cpp, registered lazily through
// il2cpp_stopwatch_class()).
extern il2cpp::Il2CppClass System_Diagnostics_Stopwatch_class;

// System.Diagnostics.Stopwatch.Frequency (static readonly long) -- the
// process-wide static field slot. The transpiler emits ldsfld as a direct
// read of {TypeCppName}_sf_{FieldName} (CodeGenerator StaticFieldExpr /
// TypeEmitter.FieldCppName); for BCL types the runtime owns the slot
// (defined in stopwatch.cpp, initialized from
// il2cpp_stopwatch_get_frequency()). Declared here so the generated code
// (generated.h includes this header) sees it; the get_Frequency icall
// remains the mapping for the getter-call form of the same field.
extern int64_t System_Diagnostics_Stopwatch_sf_Frequency;

namespace il2cpp {

// Managed System.Diagnostics.Stopwatch layout: object header + the raw
// timestamp of the current run + raw ticks banked while stopped + the
// running flag. While running, the elapsed is
// accumulatedTicks + (now - startTimestamp); while stopped it is exactly
// accumulatedTicks (frozen -- net48). There are NO reference fields: the
// collector never scans past the header.
struct Il2CppStopwatchObject
{
    Il2CppObject obj;           // object header (klass pointer, offset 0)
    int64_t startTimestamp;     // GetTimestamp() at the last Start (valid while running)
    int64_t accumulatedTicks;   // raw ticks banked by Stop() calls (frozen)
    bool    isRunning;          // true between Start and Stop
};

// The System.Diagnostics.Stopwatch descriptor (lazily registered on first
// use; generated code allocates through &System_Diagnostics_Stopwatch_class directly).
Il2CppClass* il2cpp_stopwatch_class();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// Stopwatch.Frequency -> int64: high-resolution counter ticks per second.
// Stable for the process lifetime; always > 0.
int64_t il2cpp_stopwatch_get_frequency();

// Stopwatch.GetTimestamp() -> int64: current raw counter reading. Monotonic
// (non-decreasing between calls); the unit is the frequency tick.
int64_t il2cpp_stopwatch_get_timestamp();

// Stopwatch.ElapsedTicks(start, end) -> int64: end - start in raw counter
// units (Stopwatch ticks). Zero for equal timestamps, positive whenever end
// was read after start.
int64_t il2cpp_stopwatch_get_elapsed_ticks(int64_t startTimestamp,
                                           int64_t endTimestamp);

// Elapsed milliseconds from a raw start/end pair (Stopwatch.ElapsedMilliseconds
// contract): exact rational (end - start) * 1000 / frequency, truncated
// toward zero. Computed in double; cannot overflow int64.
int64_t il2cpp_stopwatch_get_elapsed_milliseconds(int64_t startTimestamp,
                                                  int64_t endTimestamp);

// Elapsed microseconds from a raw start/end pair (same contract as the
// milliseconds helper with a 1,000,000 multiplier).
int64_t il2cpp_stopwatch_get_elapsed_microseconds(int64_t startTimestamp,
                                                  int64_t endTimestamp);

//------------------------------------------------------------------------------
// Stopwatch object icalls (see the OBJECT MODEL block at the top).
//------------------------------------------------------------------------------

// Stopwatch.StartNew() -- static factory: allocates a Stopwatch object
// through System_Diagnostics_Stopwatch_class, starts it (running, elapsed 0) and returns
// it. This is the ONLY allocation entry point the generated code needs for
// StartNew; plain new Stopwatch() falls back to the transpiler's
// il2cpp_object_new(&System_Diagnostics_Stopwatch_class) + ctor_default path.
Il2CppObject* icall_stopwatch_start_new();

// Stopwatch() -- the default ctor: zero state (not running, elapsed 0).
// Explicit (mirrors icall_version_ctor_default) though il2cpp_object_new
// already zero-initializes.
void icall_stopwatch_ctor_default(Il2CppObject* self);

// Start(): begins timing. No-op when already running (net48). The
// accumulated ticks stay banked (they are NOT reset).
void icall_stopwatch_start(Il2CppObject* self);

// Stop(): freezes the elapsed at the current value (accumulated += now -
// start). No-op when not running (net48).
void icall_stopwatch_stop(Il2CppObject* self);

// Restart(): reset then start -- a fresh run from zero, running. net48
// semantics: Reset() followed by Start().
void icall_stopwatch_restart(Il2CppObject* self);

// Reset(): zeroes the elapsed and stops timing (not running, elapsed 0).
void icall_stopwatch_reset(Il2CppObject* self);

// IsRunning: true between Start and Stop.
bool icall_stopwatch_get_is_running(Il2CppObject* self);

// ElapsedTicks: accumulatedTicks while stopped; accumulatedTicks + (now -
// start) while running (raw counter units, net48).
int64_t icall_stopwatch_get_elapsed_ticks(Il2CppObject* self);

// ElapsedMilliseconds / ElapsedMicroseconds: the elapsed ticks converted
// with the same exact-rational truncation as the static (start, end)
// helpers above (they reuse them with a zero start).
int64_t icall_stopwatch_get_elapsed_milliseconds(Il2CppObject* self);
int64_t icall_stopwatch_get_elapsed_microseconds(Il2CppObject* self);

// Elapsed: the elapsed ticks converted to a TimeSpan (100 ns units,
// (double)ticks * 1e7 / frequency -- the net48 GetElapsedDateTimeTicks
// formula).
Il2CppTimeSpan icall_stopwatch_get_elapsed(Il2CppObject* self);

}} // namespace il2cpp::runtime
