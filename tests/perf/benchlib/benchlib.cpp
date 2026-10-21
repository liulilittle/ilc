//------------------------------------------------------------------------------
// perf/benchlib.cpp -- native timing source for the perf suite.
//
// Same clock contract as tests/bench/benchlib/benchlib.cpp (kept as a separate
// copy so the perf suite never touches the existing bench tree):
//
//   int now_us() -- monotonic steady-clock microseconds since an arbitrary
//                   epoch, wrapped to [0, 2^31). 31 bits cover ~35 minutes;
//                   elapsed = now_us() - start (int32 subtraction).
//
// The SAME source is built two ways:
//   * net48 reference run: shared DLL next to perf.exe ([DllImport("benchlib")]).
//   * transpiled run: STATIC target "benchlib" (add_subdirectory'd by the
//     generated project, the mylib pattern) linked into il2cpp_gen_exe.
//
// PERF_JITTER_MS (red-proof hook, both sides read the same env var):
//   when set to N > 0, every now_us() call adds a pseudo-random 0..N ms
//   delay to its reading. The perturbation is symmetric between the start and
//   end reading, so it inflates the per-run variance (CV gate) without moving
//   the median ratio. It is read once per process (getenv), so the net48 DLL
//   and the statically linked transpiled exe behave identically.
//   N must stay well below the smallest timed workload, otherwise a reading
//   difference can go negative.
//------------------------------------------------------------------------------
#include <chrono>
#include <cstdint>
#include <cstdlib>

namespace {

unsigned g_jitterState = 0;

int JitterUs()
{
    static int maxUs = -1;
    if (maxUs < 0)
    {
        const char* v = std::getenv("PERF_JITTER_MS");
        long n = v ? std::strtol(v, nullptr, 10) : 0;
        if (n < 0) n = 0;
        if (n > 60000) n = 60000;
        maxUs = static_cast<int>(n * 1000);
        // Per-process seed: a constant seed would produce the SAME jitter pair
        // in every run, which cancels out in elapsed = t1 - t0 and would make
        // the CV red proof a no-op. Seed from the clock + the state address.
        const long long now = std::chrono::duration_cast<std::chrono::nanoseconds>(
            std::chrono::high_resolution_clock::now().time_since_epoch()).count();
        g_jitterState = static_cast<unsigned>(now) ^
                        static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(&g_jitterState));
        if (g_jitterState == 0) g_jitterState = 0x9E3779B9u;
    }
    if (maxUs == 0) return 0;
    g_jitterState ^= g_jitterState << 13;
    g_jitterState ^= g_jitterState >> 17;
    g_jitterState ^= g_jitterState << 5;
    return static_cast<int>(g_jitterState % static_cast<unsigned>(maxUs + 1));
}

} // namespace

extern "C" int now_us()
{
    const long long us = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
    return static_cast<int>((us + JitterUs()) & 0x7FFFFFFFLL);
}
