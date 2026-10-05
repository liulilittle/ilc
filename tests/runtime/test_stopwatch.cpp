//------------------------------------------------------------------------------
// test_stopwatch.cpp -- System.Diagnostics.Stopwatch runtime unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in stopwatch.h:
//   1. il2cpp_stopwatch_get_frequency: positive, stable across calls
//      (Stopwatch.Frequency is fixed for the process lifetime).
//   2. il2cpp_stopwatch_get_timestamp: nonzero raw reading, strictly
//      monotonic across a burst of consecutive calls (never goes backwards),
//      and strictly increasing across an actual sleep (a 200 ms sleep must
//      advance the counter).
//   3. il2cpp_stopwatch_get_elapsed_ticks: zero for equal timestamps,
//      exact end - start for arbitrary pairs, non-negative after sleep.
//   4. Precision (the core Stopwatch guarantee): a measured 200 ms sleep
//      must report elapsed time tracking the MEASURED steady_clock wall
//      duration (within a 5% relative + 5 ms slack). The check anchors on
//      the actual duration, not the nominal 200 ms -- a loaded CI box can
//      legitimately oversleep far past 220 ms, and the old absolute
//      [180, 220] window around the nominal sleep was the flake source.
//   4b. Conversion arithmetic is exact for whole-second synthetic tick
//      deltas (deterministic, sleep-free -- cannot flake).
//   5. ms/us consistency: both conversions agree on the same measurement
//      (us == ms * 1000 within a 1 us rounding slack).
//   6. Boundary: elapsed of a zero-width interval is exactly 0; elapsed
//      between the timestamp and itself is exactly 0.
//   7. Object model: System_Diagnostics_Stopwatch_class descriptor identity, registration
//      by name, baseClass chain, instanceSize layout.
//   8-14. Object-state roundtrip (net48 oracle): ctor_default zero state;
//      StartNew running + monotonic elapsed; Stop freezes the elapsed (stable
//      across reads, Stop-on-stopped no-op); Restart resets to a fresh run;
//      ms/us/TimeSpan conversions agree; Start-after-Stop accumulates; Start
//      on a running watch is a no-op; Reset zeroes everything.
//   15. Precision through the object surface: a measured 200 ms sleep must
//      report elapsed ms tracking the measured wall duration through the
//      icall chain (same measured-anchor as section 4).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "stopwatch.h"
#include "il2cpp_thread.h"
#include "il2cpp_runtime.h" // il2cpp_object_new / il2cpp_class_by_name / il2cpp_is_instance

#include <chrono>
#include <cstdint>
#include <cstdio>

using namespace il2cpp; // Il2CppObject / Il2CppStopwatchObject / il2cpp_stopwatch_class

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// The Stopwatch reading and the steady_clock wall reference measure the SAME
// physical interval (QPC on Windows, steady_clock on POSIX -- the same
// monotonic family), so they must agree to within a small tolerance. The
// slack is generous for a sub-ms-accurate conversion: 5% of the measured
// duration with a 5 ms absolute floor (covers the <1 ms read skew between
// the two references). Because the comparison is relative to what the sleep
// ACTUALLY took -- not its nominal 200 ms -- the check cannot flake when a
// loaded machine oversleeps.
bool ElapsedMatches(int64_t stopwatchMs, int64_t actualMs)
{
    const int64_t slack = actualMs / 20 + 5; // 5% relative, 5 ms absolute floor
    const int64_t diff = stopwatchMs > actualMs ? stopwatchMs - actualMs
                                                : actualMs - stopwatchMs;
    return diff <= slack;
}

} // namespace

int main()
{
    // --- 1. Frequency: positive and stable for the process lifetime ---
    const int64_t frequency = il2cpp::runtime::il2cpp_stopwatch_get_frequency();
    Expect(frequency > 0, "frequency is positive");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_frequency() == frequency,
           "frequency is stable across calls");

    // --- 1b. Static-field dedup: the ldsfld storage slot and the
    // get_Frequency icall expose the SAME value. The transpiler reads
    // Stopwatch.Frequency as a direct ldsfld of
    // System_Diagnostics_Stopwatch_sf_Frequency (the single global slot in
    // stopwatch.cpp), while the getter-call form routes to
    // il2cpp_stopwatch_get_frequency(). A duplicated or divergent slot would
    // silently split the field between the two generated code paths.
    Expect(System_Diagnostics_Stopwatch_sf_Frequency == frequency,
           "sf_Frequency slot matches the get_Frequency icall (single definition)");

    // --- 2a. Timestamp: nonzero and monotonic across a burst ---
    const int64_t first = il2cpp::runtime::il2cpp_stopwatch_get_timestamp();
    Expect(first != 0, "timestamp is nonzero");
    int64_t previous = first;
    bool nonDecreasing = true;
    for (int i = 0; i < 1000; ++i)
    {
        const int64_t current = il2cpp::runtime::il2cpp_stopwatch_get_timestamp();
        if (current < previous)
        {
            nonDecreasing = false;
            break;
        }
        previous = current;
    }
    Expect(nonDecreasing, "timestamps never go backwards (monotonic)");

    // --- 3. Elapsed ticks: exact deltas, zero-width boundaries ---
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_ticks(first, first) == 0,
           "elapsed ticks of a zero-width interval is 0");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_ticks(first, previous) ==
               (previous - first),
           "elapsed ticks is exactly end - start");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_ticks(previous, first) ==
               (first - previous),
           "elapsed ticks handles reversed pairs (end - start, signed)");

    // --- 2b + 4. Precision: the Stopwatch tracks the MEASURED wall duration.
    // The wall reference is steady_clock -- the same monotonic family as QPC
    // (Windows) / steady_clock (POSIX) -- so both read the same physical
    // interval. Anchoring on what the sleep actually took (not the nominal
    // 200 ms) removes the flake: the check passes no matter how long the
    // loaded machine let the thread sleep, because Stopwatch and the wall
    // reference still agree to within a few ms. ---
    const int64_t start = il2cpp::runtime::il2cpp_stopwatch_get_timestamp();
    const auto wallStart = std::chrono::steady_clock::now();
    il2cpp::runtime::icall_thread_sleep(200);
    const int64_t end = il2cpp::runtime::il2cpp_stopwatch_get_timestamp();
    const auto wallEnd = std::chrono::steady_clock::now();
    Expect(end > start, "timestamp advanced across the sleep");

    const int64_t elapsedTicks =
        il2cpp::runtime::il2cpp_stopwatch_get_elapsed_ticks(start, end);
    Expect(elapsedTicks > 0, "elapsed ticks after sleep is positive");

    const int64_t actualMs = std::chrono::duration_cast<std::chrono::milliseconds>(
        wallEnd - wallStart).count();
    Expect(actualMs >= 100, "the reference sleep actually took ~200 ms (coarse floor)");

    const int64_t elapsedMs =
        il2cpp::runtime::il2cpp_stopwatch_get_elapsed_milliseconds(start, end);
    Expect(ElapsedMatches(elapsedMs, actualMs),
           "elapsed ms tracks the measured wall duration (stable under load)");

    const int64_t elapsedUs =
        il2cpp::runtime::il2cpp_stopwatch_get_elapsed_microseconds(start, end);

    // --- 5. ms/us consistency on the same measurement ---
    // Both conversions truncate the exact rational independently, so the
    // difference between us and ms*1000 is bounded by one millisecond tick
    // (the us conversion's fractional part leaks at most ~999 us).
    const int64_t usFromMs = elapsedMs * 1000;
    const int64_t usSlack = usFromMs > elapsedUs ? usFromMs - elapsedUs
                                                 : elapsedUs - usFromMs;
    Expect(usSlack < 1000, "microsecond and millisecond conversions agree");

    // --- 6. Boundary: zero-width interval converts to exactly 0 ---
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_milliseconds(first, first) == 0,
           "zero-width interval is 0 ms");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_microseconds(first, first) == 0,
           "zero-width interval is 0 us");

    // --- 6b. Conversion arithmetic is exact for whole-second tick deltas:
    // deterministic synthetic tick counts, no sleep -- cannot flake. The
    // net48 formula is (double)delta * unit / Frequency, truncated toward
    // zero (GetElapsedDateTimeTicks then integer-divided by the unit).
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_ticks(0, frequency) == frequency,
           "1 s of ticks == the raw frequency (ElapsedTicks is un-converted)");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_milliseconds(0, frequency) == 1000,
           "1 s of ticks == exactly 1000 ms");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_milliseconds(0, 2 * frequency) == 2000,
           "2 s of ticks == exactly 2000 ms");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_microseconds(0, frequency) == 1000000,
           "1 s of ticks == exactly 1000000 us");
    Expect(il2cpp::runtime::il2cpp_stopwatch_get_elapsed_milliseconds(frequency, 0) == -1000,
           "reversed 1 s delta == exactly -1000 ms (signed, truncation toward zero)");

    // --- 7. Object model: descriptor identity + allocation ---
    Expect(il2cpp_stopwatch_class() == &System_Diagnostics_Stopwatch_class,
           "il2cpp_stopwatch_class() returns &System_Diagnostics_Stopwatch_class");
    Expect(System_Diagnostics_Stopwatch_class.instanceSize == sizeof(Il2CppStopwatchObject),
           "System_Diagnostics_Stopwatch_class.instanceSize == sizeof(Il2CppStopwatchObject)");
    Expect(System_Diagnostics_Stopwatch_class.baseClass == &System_Object_class,
           "System_Diagnostics_Stopwatch_class.baseClass == System_Object_class");
    Expect(il2cpp_class_by_name("System.Diagnostics.Stopwatch") == &System_Diagnostics_Stopwatch_class,
           "System.Diagnostics.Stopwatch resolves by name (registered)");

    // --- 8. ctor_default: zero state, not running, elapsed 0 ---
    Il2CppStopwatchObject* idle = reinterpret_cast<Il2CppStopwatchObject*>(
        il2cpp_object_new(&System_Diagnostics_Stopwatch_class));
    Expect(idle->obj.klass == &System_Diagnostics_Stopwatch_class, "default object klass stamped");
    il2cpp::runtime::icall_stopwatch_ctor_default(reinterpret_cast<Il2CppObject*>(idle));
    Expect(!il2cpp::runtime::icall_stopwatch_get_is_running(reinterpret_cast<Il2CppObject*>(idle)),
           "default ctor: not running");
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(reinterpret_cast<Il2CppObject*>(idle)) == 0,
           "default ctor: elapsed ticks 0");
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed(reinterpret_cast<Il2CppObject*>(idle)).ticks == 0,
           "default ctor: elapsed TimeSpan 0");

    // --- 9. StartNew -> running, elapsed advances monotonically ---
    Il2CppObject* sw = il2cpp::runtime::icall_stopwatch_start_new();
    Expect(il2cpp::runtime::icall_stopwatch_get_is_running(sw),
           "StartNew: running");
    const int64_t runningT0 = il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw);
    Expect(runningT0 >= 0, "StartNew: elapsed ticks never negative");
    il2cpp::runtime::icall_thread_sleep(50);
    const int64_t runningT1 = il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw);
    Expect(runningT1 >= runningT0, "running elapsed is monotonic");
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed_milliseconds(sw) >= 0,
           "running elapsed ms never negative");
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed_microseconds(sw) >= 0,
           "running elapsed us never negative");
    const int64_t runningTsTicks = il2cpp::runtime::icall_stopwatch_get_elapsed(sw).ticks;
    Expect(runningTsTicks >= 0, "running elapsed TimeSpan ticks never negative");
    Expect(il2cpp_is_instance(sw, &System_Diagnostics_Stopwatch_class),
           "a Stopwatch IS-A System.Diagnostics.Stopwatch");

    // --- 10. Stop: freezes the elapsed (stable across reads) ---
    il2cpp::runtime::icall_stopwatch_stop(sw);
    Expect(!il2cpp::runtime::icall_stopwatch_get_is_running(sw),
           "Stop: not running");
    const int64_t frozen = il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw);
    Expect(frozen >= runningT1, "stopped elapsed is monotonic vs the last running sample");
    il2cpp::runtime::icall_thread_sleep(20);
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw) == frozen,
           "stopped elapsed is stable (frozen across time)");
    // Stop on a stopped watch is a no-op: elapsed stays frozen.
    il2cpp::runtime::icall_stopwatch_stop(sw);
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw) == frozen,
           "Stop on a stopped watch is a no-op (elapsed still frozen)");

    // --- 11. Restart: reset then start (elapsed back near zero, running) ---
    il2cpp::runtime::icall_stopwatch_restart(sw);
    Expect(il2cpp::runtime::icall_stopwatch_get_is_running(sw),
           "Restart: running again");
    const int64_t restarted = il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw);
    Expect(restarted < frozen,
           "Restart reset the accumulated elapsed (fresh run starts below the frozen value)");

    // --- 12. ms/us/TimeSpan conversions agree on the same frozen sample ---
    il2cpp::runtime::icall_stopwatch_stop(sw);
    const int64_t fMs = il2cpp::runtime::icall_stopwatch_get_elapsed_milliseconds(sw);
    const int64_t fUs = il2cpp::runtime::icall_stopwatch_get_elapsed_microseconds(sw);
    const int64_t fTs = il2cpp::runtime::icall_stopwatch_get_elapsed(sw).ticks;
    const int64_t usFromMs2 = fMs * 1000;
    const int64_t usSlack2 = usFromMs2 > fUs ? usFromMs2 - fUs : fUs - usFromMs2;
    Expect(usSlack2 < 1000, "object ms/us conversions agree");
    // TimeSpan ticks == ms * 1e4 in double for ANY frequency (both divide by
    // the same frequency; the 1e7/1e3 unit ratio is frequency-independent).
    const int64_t tsFromMs = static_cast<int64_t>(static_cast<double>(fMs) * 10000.0);
    const int64_t tsSlack = tsFromMs > fTs ? tsFromMs - fTs : fTs - tsFromMs;
    Expect(tsSlack < 10000, "TimeSpan ticks agree with the ms conversion (within 1 ms)");

    // --- 13. Start-after-Stop accumulates (segment 2 adds to the frozen value) ---
    const int64_t beforeSeg2 = il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw);
    il2cpp::runtime::icall_stopwatch_start(sw);
    Expect(il2cpp::runtime::icall_stopwatch_get_is_running(sw), "Start after Stop: running");
    il2cpp::runtime::icall_thread_sleep(20);
    il2cpp::runtime::icall_stopwatch_stop(sw);
    const int64_t afterSeg2 = il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw);
    Expect(afterSeg2 > beforeSeg2, "second segment accumulates onto the frozen elapsed");
    // Start on a running watch is a no-op (still running after the double Start).
    il2cpp::runtime::icall_stopwatch_start(sw);
    il2cpp::runtime::icall_stopwatch_start(sw);
    Expect(il2cpp::runtime::icall_stopwatch_get_is_running(sw),
           "Start on a running watch is a no-op (still running)");

    // --- 14. Reset: zero state, not running ---
    il2cpp::runtime::icall_stopwatch_reset(sw);
    Expect(!il2cpp::runtime::icall_stopwatch_get_is_running(sw), "Reset: not running");
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed_ticks(sw) == 0,
           "Reset: elapsed ticks 0");
    Expect(il2cpp::runtime::icall_stopwatch_get_elapsed(sw).ticks == 0,
           "Reset: elapsed TimeSpan 0");

    // --- 15. Precision through the object surface: Stopwatch tracks the
    // measured wall duration through the icall chain (measured-anchor, same
    // as section 4 -- robust under load) ---
    il2cpp::runtime::icall_stopwatch_restart(sw);
    const auto wallStart2 = std::chrono::steady_clock::now();
    il2cpp::runtime::icall_thread_sleep(200);
    const auto wallEnd2 = std::chrono::steady_clock::now();
    il2cpp::runtime::icall_stopwatch_stop(sw);
    const int64_t actualMs2 = std::chrono::duration_cast<std::chrono::milliseconds>(
        wallEnd2 - wallStart2).count();
    const int64_t objMs = il2cpp::runtime::icall_stopwatch_get_elapsed_milliseconds(sw);
    Expect(ElapsedMatches(objMs, actualMs2),
           "object elapsed ms tracks the measured wall duration");
    const int64_t objUs = il2cpp::runtime::icall_stopwatch_get_elapsed_microseconds(sw);
    const int64_t objUsSlack = objMs * 1000 > objUs ? objMs * 1000 - objUs
                                                    : objUs - objMs * 1000;
    Expect(objUsSlack < 1000, "object elapsed us agrees with the object elapsed ms");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d stopwatch assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stopwatch tests passed\n");
    return 0;
}
