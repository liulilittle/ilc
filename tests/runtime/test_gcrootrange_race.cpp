//------------------------------------------------------------------------------
// test_gcrootrange_race.cpp -- root-range table: concurrency + capacity probe.
//
// RED/GREEN probe for the root-range table in gc.cpp (R0 of the gc-roots task).
// The table (g_rootRanges / g_rootRangeCount) is process-global mutable state
// that the collector ENUMERATES during every mark and every compaction
// reference-update, while managed code registers/unregisters ranges:
//   * il2cpp_handleref.h:110  -- one range per HandleRef P/Invoke argument
//   * stringext2.cpp:1141     -- String.Concat(IEnumerable) (per call)
//   * xml_write.cpp:241/517   -- one range per live XmlWriter
//   * dependhandle.cpp:63     -- one shared range (add-only)
//   * appdomain.cpp:383       -- one process-wide range (add-only)
//
// Sections
//   1. capacity / nesting: a recursive HandleRef-style nesting (one range per
//      level) must not hit the table bound. The old kMaxRootRanges = 1024
//      aborts the process ("root range table overflow") at depth 1025, which
//      is reachable by an ordinary deep recursive call with a HandleRef
//      argument (the native stack allows several thousand levels).
//   2. race: 8 worker threads hammer add/remove of their own 1-slot range
//      while one thread runs full collections. A keeper range (registered
//      before the phase, removed after it) is the ONLY root of a finalizable
//      victim object. Two invariants are asserted:
//        - every worker's add/remove pair is balanced: after the joins the
//          count is exactly the keeper (1), then 0 after removing it, and the
//          count is never negative / never above #threads + keeper;
//        - no root is LOST: the victim is never finalized while the keeper
//          range is registered (the finalizer flag stays 0). A swap-remove
//          that runs on a stale count drops the keeper entry and the victim
//          then dies -- the exact "silently lost root -> premature reclaim"
//          failure the fix must eliminate.
//      Control: after the keeper is removed, two further collections MUST run
//      the victim's finalizer -- proving the detector itself is live.
//   3. ordering: swap-remove must keep 1:1 pairing for non-LIFO removal
//      (add A,B,C; remove A,C,B).
//
// Run with no arguments for every section. `--race` / `--capacity` select one
// section (used to capture the two independent RED signals on the baseline,
// because the capacity section aborts the process by design).
//------------------------------------------------------------------------------

#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>
#include <vector>

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

// Fixture: 24-byte object with no reference fields (a leaf; the probe only
// needs finalizability, not object-graph scanning).
il2cpp::Il2CppClass g_classLeaf = {
    "Test.GcRootRangeLeaf",
    24,
    0u,
    0,
    nullptr,
    nullptr,
    0,
    nullptr,
    0,
};

std::atomic<int> g_finalized{0};

void OnFinalized(il2cpp::Il2CppObject* /*obj*/)
{
    g_finalized.fetch_add(1, std::memory_order_relaxed);
}

//------------------------------------------------------------------------------
// Section 1: recursive nesting (one registered range per level).
//
// Mirrors a recursive managed method that passes a HandleRef argument at every
// level: Il2CppHandleRefArg registers one slot until the end of the full
// expression, so the live range count equals the native recursion depth.
// 'base' is the address of the caller's probe local; the deepest level records
// its own local address so the per-level native stack cost can be measured.
//------------------------------------------------------------------------------
constexpr int kNestingDepth = 2048;
std::uintptr_t g_deepestLocal = 0;

void RecurseRegister(int depth)
{
    volatile char pad[64];
    pad[0] = 0;
    (void)pad;
    void* slot = nullptr;
    il2cpp::il2cpp_gc_add_root_range(&slot, 1);
    if (depth == 0)
    {
        g_deepestLocal = reinterpret_cast<std::uintptr_t>(&slot);
    }
    if (depth > 0)
    {
        RecurseRegister(depth - 1);
    }
    // At this point every ancestor's range is still registered: the depth+1
    // ranges are all live. The deepest frame checks the count.
    if (depth == 0)
    {
        const int32_t count = il2cpp::il2cpp_gc_get_root_range_count();
        if (count != kNestingDepth + 1)
        {
            std::printf("FAIL: nested range count = %d, expected %d\n",
                        static_cast<int>(count), kNestingDepth + 1);
            ++g_failures;
        }
    }
    il2cpp::il2cpp_gc_remove_root_range(&slot);
}

void RunCapacitySection()
{
    std::printf("== section 1: capacity / nesting ==\n");
    const int32_t capacity = il2cpp::il2cpp_gc_get_root_range_capacity();
    std::printf("root-range capacity = %d\n", static_cast<int>(capacity));
    Expect(capacity >= kNestingDepth + 1,
           "capacity covers a realistic recursive nesting depth");

    volatile char anchor = 0;
    const std::uintptr_t baseLocal = reinterpret_cast<std::uintptr_t>(&anchor);
    (void)anchor;
    RecurseRegister(kNestingDepth);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 0,
           "every nested range was removed (1:1 pairing)");

    if (g_deepestLocal != 0 && baseLocal > g_deepestLocal)
    {
        const double bytesPerLevel =
            static_cast<double>(baseLocal - g_deepestLocal) / kNestingDepth;
        std::printf("native stack cost = %.1f bytes/level; 1 MiB stack => ~%.0f levels\n",
                    bytesPerLevel, (1024.0 * 1024.0) / bytesPerLevel);
    }
}

//------------------------------------------------------------------------------
// Section 2: concurrent add/remove vs a collecting thread.
//------------------------------------------------------------------------------
constexpr int kWorkers = 8;
constexpr int kIterations = 200000;
constexpr int kMaxCollections = 2000;

std::atomic<bool> g_stop{false};
std::atomic<int> g_negativeObserved{0};
std::atomic<int> g_overCount{0};

void CollectorThread()
{
    for (int n = 0; n < kMaxCollections && !g_stop.load(std::memory_order_relaxed); ++n)
    {
        il2cpp::il2cpp_gc_collect();
        const int32_t count = il2cpp::il2cpp_gc_get_root_range_count();
        if (count < 0)
        {
            g_negativeObserved.store(1, std::memory_order_relaxed);
        }
        if (count > kWorkers + 1)
        {
            g_overCount.store(count, std::memory_order_relaxed);
        }
    }
}

void WorkerThread()
{
    // The worker's own 1-slot range holds null: the sole root of the victim is
    // the keeper, so this section only stresses table bookkeeping.
    void* slot = nullptr;
    for (int i = 0; i < kIterations; ++i)
    {
        il2cpp::il2cpp_gc_add_root_range(&slot, 1);
        il2cpp::il2cpp_gc_remove_root_range(&slot);
    }
}

void RunRaceSection()
{
    std::printf("== section 2: concurrent add/remove vs collect ==\n");

    il2cpp::Il2CppObject* victim = il2cpp::il2cpp_gc_alloc(&g_classLeaf);    il2cpp::il2cpp_gc_register_finalizer(victim, &OnFinalized);

    // Keeper: the ONLY root of the victim for the whole concurrent phase.
    void* keeper[1] = { victim };
    il2cpp::il2cpp_gc_add_root_range(keeper, 1);
    // Promote the victim out of Gen0 so it cannot be moved (the keeper slot
    // must stay the single source of truth for its address).
    il2cpp::il2cpp_gc_collect();
    il2cpp::il2cpp_gc_collect();
    victim = static_cast<il2cpp::Il2CppObject*>(keeper[0]);
    Expect(victim != nullptr && victim->klass == &g_classLeaf,
           "victim survives the two setup collections");
    Expect(g_finalized.load(std::memory_order_relaxed) == 0,
           "victim is not finalized while the keeper range is registered");

    g_stop.store(false, std::memory_order_relaxed);
    std::printf("starting %d workers x %d iterations + collector\n", kWorkers, kIterations);
    std::vector<std::thread> workers;
    workers.reserve(kWorkers);
    for (int i = 0; i < kWorkers; ++i)
    {
        workers.emplace_back(WorkerThread);
    }
    std::thread collector(CollectorThread);
    for (std::thread& w : workers)
    {
        w.join();
    }
    g_stop.store(true, std::memory_order_relaxed);
    collector.join();
    std::printf("concurrent phase finished\n");

    // 1:1 pairing: the keeper is the only range left.
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 1,
           "after the joins the count is the keeper range only (add/remove balanced)");
    Expect(g_negativeObserved.load(std::memory_order_relaxed) == 0,
           "the root-range count never went negative during the race");
    Expect(g_overCount.load(std::memory_order_relaxed) == 0,
           "the root-range count never exceeded the number of live registrations");

    // Lost-root detector: the keeper is still registered, so the victim must
    // still be reachable. A collection here promotes/keeps it and must NOT
    // finalize it.
    il2cpp::il2cpp_gc_collect();
    Expect(g_finalized.load(std::memory_order_relaxed) == 0,
           "no root was lost: the keeper-rooted victim survived the concurrent phase");

    il2cpp::il2cpp_gc_remove_root_range(keeper);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 0,
           "removing the keeper leaves the table empty");

    // Control: the detector works -- with no root at all, two collections must
    // run the victim's finalizer.
    il2cpp::il2cpp_gc_collect();
    il2cpp::il2cpp_gc_collect();
    Expect(g_finalized.load(std::memory_order_relaxed) == 1,
           "control: the unrooted victim is finalized (detector is live)");
}

//------------------------------------------------------------------------------
// Section 3: non-LIFO swap-remove ordering.
//------------------------------------------------------------------------------
void RunOrderingSection()
{
    std::printf("== section 3: swap-remove ordering ==\n");
    void* a[1] = { nullptr };
    void* b[1] = { nullptr };
    void* c[1] = { nullptr };
    il2cpp::il2cpp_gc_add_root_range(a, 1);
    il2cpp::il2cpp_gc_add_root_range(b, 1);
    il2cpp::il2cpp_gc_add_root_range(c, 1);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 3, "three nested ranges registered");
    il2cpp::il2cpp_gc_remove_root_range(a);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 2, "removing the first range leaves two");
    il2cpp::il2cpp_gc_remove_root_range(c);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 1, "removing the swapped range leaves one");
    il2cpp::il2cpp_gc_remove_root_range(b);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 0, "removing the last range empties the table");
    // Removing an unknown range is a documented no-op.
    il2cpp::il2cpp_gc_remove_root_range(b);
    Expect(il2cpp::il2cpp_gc_get_root_range_count() == 0, "removing an unknown range is a no-op");
}

} // namespace

int main(int argc, char** argv)
{
    bool runCapacity = true;
    bool runRace = true;
    bool runOrdering = true;
    for (int i = 1; i < argc; ++i)
    {
        if (std::strcmp(argv[i], "--race") == 0)
        {
            runCapacity = false;
            runOrdering = false;
        }
        else if (std::strcmp(argv[i], "--capacity") == 0)
        {
            runRace = false;
            runOrdering = false;
        }
        else if (std::strcmp(argv[i], "--ordering") == 0)
        {
            runCapacity = false;
            runRace = false;
        }
    }

    il2cpp::il2cpp_gc_init();

    // Unbuffered stdout: a probe crash (the RED signal for a corrupted table)
    // must not swallow the progress lines that locate the failure.
    (void)setvbuf(stdout, nullptr, _IONBF, 0);

    if (runCapacity)
    {
        RunCapacitySection();
    }
    if (runOrdering)
    {
        RunOrderingSection();
    }
    if (runRace)
    {
        RunRaceSection();
    }

    if (g_failures != 0)
    {
        std::printf("FAILED: %d assertion(s)\n", g_failures);
        return 1;
    }
    std::printf("PASS: root-range table probe\n");
    return 0;
}
