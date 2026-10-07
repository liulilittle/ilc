#pragma once
//------------------------------------------------------------------------------
// il2cpp_gc.h -- mark-sweep garbage collector contract.
//
// The collector is the single owner of managed heap memory. Every object that
// reaches the heap goes through il2cpp_gc_alloc (and, transitively, through
// il2cpp_object_new, which delegates here). Memory is reclaimed only by the
// mark-sweep cycle -- never by explicit free, never by reference counting
// (bare GC pointers are the one and only managed reference representation).
//
// M2 collector shape (see gc.cpp for the full design):
//   - Heap: a linked list of 64KB segments; objects are bump-allocated
//     (zero-initialized) within the current segment.
//   - Mark: iterative worklist rooted at the registered static roots plus the
//     registered root ranges. Each object is scanned using the reference-field
//     bitmap on its Il2CppClass (slot i <=> byte offset i*sizeof(void*)).
//     Mark state lives in a side table -- the object header never carries it
//     (header layout is fixed to a single klass pointer for M2; a generational
//     header redesign is M3 scope).
//   - Sweep: unreachable objects are linked into a free list; the collector is
//     non-moving, so subsequent allocations reuse those exact blocks (M3 adds
//     compaction inside generational collection).
//   - Trigger: allocation fails to find a free block AND the current segment
//     cannot satisfy the request -> collect, then retry; il2cpp_gc_collect
//     also runs a full cycle on demand.
//
// Root registration is a compile-time contract with the generated code:
//   - il2cpp_gc_add_static_root registers the address OF a static reference
//     field (so the collector can read *addr to reach the live object).
//   - il2cpp_gc_add_root_range registers a contiguous array of reference
//     slots (used later for managed frame stacks, Task 9+).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"

namespace il2cpp {

// Initialize the heap. Idempotent: safe to call more than once (later calls
// are no-ops). The heap also initializes lazily on first allocation, so
// explicit startup code may or may not call this -- both paths are safe.
void il2cpp_gc_init();

// Allocate a zero-initialized instance of 'klass' on the managed heap.
// Returns nullptr only if 'klass' is null (a caller bug); allocation failure
// aborts the process (see gc.cpp). The returned object's klass header points
// at 'klass' and every payload byte is 0 (.NET default-value semantics).
Il2CppObject* il2cpp_gc_alloc(Il2CppClass* klass);

// Allocate a zero-initialized block whose TOTAL size is 'totalSize' bytes
// (>= klass->instanceSize). Used by variable-size managed types (string:
// header + payload; arrays: header + elements) whose extent exceeds the
// descriptor's fixed instanceSize. The block table records 'totalSize' as
// the true physical extent, so sweep/marking stay exact. totalSize must be
// pointer-aligned; the allocator rounds up defensively.
Il2CppObject* il2cpp_gc_alloc_size(Il2CppClass* klass, size_t totalSize);

// Run a full mark-sweep cycle: trace from the current root set, reclaim every
// unreachable object. Safe to call at any time; a subsequent allocation may
// reuse the reclaimed blocks.
void il2cpp_gc_collect();

// Register a static reference field as a GC root. 'addr' must be the address
// of a pointer-sized location that stores an Il2CppObject* (e.g. a generated
// static field). The collector reads *addr during mark. Registering the same
// address twice is idempotent. Passing null is a caller bug and is ignored.
void il2cpp_gc_add_static_root(void** addr);

// Unregister a static root previously added by il2cpp_gc_add_static_root
// (must pass the SAME 'addr'). Removal is a no-op for unknown addresses.
// Paired with add -- a root whose storage goes out of scope MUST be removed
// (with the M3 compacting collection, a stale root pointing at dead stack
// memory is scanned as a garbage reference).
void il2cpp_gc_remove_static_root(void** addr);

// Register a contiguous run of reference slots (an array of count Il2CppObject*
// values) as GC roots. Used for managed frame stacks and similar external root
// regions. 'begin' is the address of the first slot; count must be >= 0.
void il2cpp_gc_add_root_range(void** begin, int32_t count);

// Unregister a root range previously added by il2cpp_gc_add_root_range (must
// pass the SAME 'begin' address). Used by the managed frame prologue/epilogue
// pairing (Task 10): the frame's reference slots are roots only while the
// method is on the call stack. Removing a range that was never added (or was
// already removed) is a no-op. Passing null is a caller bug and is ignored.
void il2cpp_gc_remove_root_range(void** begin);

//------------------------------------------------------------------------------
// Generational GC (M3).
//
// Generations: 0 = youngest (bump-allocated), 1/2 = promoted survivors,
// 3 = large-object heap (LOH: objects >= 85000 bytes, never promoted/moved).
// il2cpp_gc_collect promotes every surviving block one generation (0->1->2,
// LOH stays 3) -- M3's first generational model is NON-MOVING (the physical
// block stays in place; only its recorded generation advances). Compaction
// (moving collection with full reference update) arrives as a later M3 task.
//------------------------------------------------------------------------------

// Generation constants (see the header comment).
constexpr int32_t kGcGeneration0 = 0; // youngest: fresh bump allocations
constexpr int32_t kGcGeneration1 = 1; // survived one collection
constexpr int32_t kGcGeneration2 = 2; // survived two collections (oldest small-object gen)
constexpr int32_t kGcGenerationLoh = 3; // large-object heap (>= 85000 bytes)

// Large-object threshold (aligned with .NET's 85000-byte LOH cutoff).
constexpr int32_t kGcLohThreshold = 85000;

// Query the current generation of a managed object's block (0/1/2/3).
// Returns -1 for null/unknown pointers (test/diagnostic helper).
int32_t il2cpp_gc_get_generation(Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_write_barrier -- write barrier for reference-field stores.
//
//   Called by generated code BEFORE every reference-field assignment:
//     il2cpp::il2cpp_write_barrier(&obj->field, value);
//     obj->field = value;
//
//   Marks the card covering the STORING object when it is in an old
//   generation (>= 1): the card table records potential old->young
//   references for the young-only collection optimization. Young (Gen0)
//   stores are free (no card mark). See gc.cpp for the full contract.
//------------------------------------------------------------------------------
void il2cpp_write_barrier(void** slot, Il2CppObject* value);

// Test/diagnostic helper: is the card covering 'addr' marked (dirty)?
// Returns true for marked cards, false otherwise (out-of-range -> false).
bool il2cpp_gc_is_card_marked(const void* addr);

//------------------------------------------------------------------------------
// Heap statistics (M-phase GC extension).
//------------------------------------------------------------------------------

// Sum of the sizes of all LIVE blocks in the block table -- bytes of managed
// heap currently occupied by live objects, INCLUDING objects retained for
// pending finalization (their memory is still allocated). 0 on an empty heap.
// The value is a snapshot: it changes with every allocation and collection.
int64_t il2cpp_gc_get_total_memory();

// Cumulative bytes handed to objects by the allocator since process start
// (the block's true physical extent, incremented in the allocation core).
// Monotonic: never decreases, unaffected by collections.
int64_t il2cpp_gc_get_allocated_bytes();

//------------------------------------------------------------------------------
// Weak references (M-phase GC extension).
//
// A weak reference is a CLIENT-OWNED pointer slot: 'slot' is the ADDRESS of a
// memory location holding an Il2CppObject* (e.g. a C++ field or a test
// global). The collector never marks through a weak slot -- weakness is the
// point -- but it re-examines every registered slot during each collection:
//   * the pointed-to object is dead (not marked) -> the slot is cleared to
//     null;
//   * the pointed-to object is alive and was MOVED by the Gen0 compaction ->
//     the slot is rewritten to the object's new address (weak slots follow
//     compaction, exactly like roots).
// The check runs AFTER the sweep and BEFORE the reference-update phase of
// each collection (see gc.cpp: UpdateExternalReferences). The slot storage
// must remain valid while registered; unregister before it goes out of scope
// (same contract as static roots).
//------------------------------------------------------------------------------

// Register a weak-reference slot. Passing null is a caller bug and is ignored;
// registering the same address twice is idempotent. Overflow aborts the
// process (fixed-capacity static table, like the block table).
void il2cpp_gc_add_weak_ref(void** slot);

// Unregister a weak slot previously added by il2cpp_gc_add_weak_ref (must
// pass the SAME address). Removal of an unknown address is a no-op.
void il2cpp_gc_remove_weak_ref(void** slot);

//------------------------------------------------------------------------------
// Pinned references (GCHandle.Alloc(obj, Pinned) support).
//
// A pinned reference is a CLIENT-OWNED pointer slot (the address of a memory
// location holding an Il2CppObject*), exactly like a weak slot -- but the
// collector treats the pointed-to object as IMMOVABLE: the Gen0 compaction
// promotes a pinned survivor NON-MOVING (generation advances in place, the
// object keeps its address). This is the contract AddrOfPinnedObject relies
// on: the address returned at pin time stays valid across collections until
// the pin is released. The slot is also a strong root (the pinned object is
// kept alive), so the caller must NOT separately root it.
//
// Registering the same address twice is idempotent; overflow aborts the
// process (fixed-capacity static table). Unregister with the SAME address
// before the slot goes out of scope; removal of an unknown address is a no-op.
//------------------------------------------------------------------------------
void il2cpp_gc_add_pinned(void** slot);
void il2cpp_gc_remove_pinned(void** slot);

//------------------------------------------------------------------------------
// Finalization (simplified coreclr CFinalize; M-phase GC extension).
//
// il2cpp_gc_register_finalizer arms a callback for 'obj'. The callback runs
// exactly once per registration and ALWAYS before the object's memory is
// reclaimed. Two-collection .NET semantics:
//   collect N:   the object dies -> the block is RETAINED (kept in the block
//                table, memory NOT freed) and the entry becomes "pending".
//   collect N+1: pending finalizers run at the START of the collection (the
//                callback observes valid, not-yet-reclaimed memory); the
//                sweep of the SAME collection then reclaims the block.
// A callback that makes its object reachable again (resurrection, e.g. by
// storing it into a root) is honored: the object survives and the entry is
// dropped -- the finalizer does not run a second time (.NET semantics). A
// callback that ALLOCATES may trigger a nested collection; a reentrancy
// guard protects the in-flight object from being reclaimed early.
// Re-registering the same object REPLACES the callback (and re-arms it, the
// analogue of GC.ReRegisterForFinalize).
//------------------------------------------------------------------------------

// Register a finalizer callback for 'obj'. Null object or null callback are
// caller bugs and are ignored. Overflow aborts the process (fixed-capacity
// static table).
void il2cpp_gc_register_finalizer(Il2CppObject* obj, void (*fn)(Il2CppObject*));

// Register a TYPE-level finalizer: the type's Finalize method pointer, armed
// on EVERY instance of 'klass' at allocation (see gc.cpp AllocCore). This is
// the runtime half of user-finalizer support -- generated code registers each
// finalizable type once, and every subsequent allocation of that type becomes
// finalizable with the same two-collection semantics as the per-object
// registration above (the callback runs before the instance's memory is
// reclaimed). Re-registering a type REPLACES the callback. Null klass or null
// callback are caller bugs and are ignored; overflow aborts the process.
void il2cpp_gc_register_type_finalizer(Il2CppClass* klass, void (*fn)(Il2CppObject*));

// Run all due (pending) finalizers now. Normally they run automatically at
// the start of the next collection; this is the direct trigger (tests,
// future finalizer-thread emulation). Idempotent: safe to call at any time.
void il2cpp_gc_run_finalizers();

//------------------------------------------------------------------------------
// GC statistics + notification + GCSettings (Core FCall gGCInterfaceFuncs /
// gGCSettingsFuncs domain -- icall-inventory-core.md section 8.1 GC 13 gaps).
//
// The collector is a single-threaded, non-moving-until-M3 FULL-heap
// mark-sweep cycle, so every "generation" below is the generation recorded on
// the surviving block AFTER a collection (0 -> 1 -> 2, LOH stays 3). All
// values are snapshots over the fixed static bookkeeping tables (constraint
// #12: no C++ heap in the collector).
//------------------------------------------------------------------------------

// Per-generation collection counts. The M3 collector only runs FULL-heap
// cycles, so one collection increments the count of every small-object
// generation exactly once (the .NET analogue: an explicit GC.Collect()
// counts once for each generation). 'generation' must be 0/1/2 (3/LOH and
// out-of-range return 0 -- LOH has no collection count of its own).
int64_t il2cpp_gc_get_collection_count(int32_t generation);

// Total wall-clock time spent inside collections (GC pause) since process
// start, in 100ns TimeSpan ticks (the unit System.TimeSpan uses). Monotonic.
int64_t il2cpp_gc_get_total_pause_ticks();

// Percentage (0..100) of wall-clock time spent in the collector since the
// last collection -- the analogue of GCInterface::GetLastGCPercentTimeInGC.
int32_t il2cpp_gc_get_last_gc_percent_time_in_gc();

// Current memory load as a percentage (0..100), computed from the managed
// heap's committed bytes against a fixed runtime budget (no OS-specific
// calls -- cross-platform by design). The analogue of GCInterface::
// GetMemoryLoad.
int32_t il2cpp_gc_get_memory_load();

// Total managed-heap bytes committed to segments (the sum of every segment's
// payload capacity). Monotonic until segments are freed (M2/M3: never).
int64_t il2cpp_gc_get_total_committed_bytes();

// The collector's segment payload size in bytes (64 KB -- the per-segment
// object area a fresh Gen0 bump segment provides). The analogue of the
// internal GC.GetSegmentSize() (ulong).
uint64_t il2cpp_gc_get_segment_size();

// Managed-heap bytes currently reclaimable on the free list (the analogue of
// fragmentation: blocks swept but not yet reused).
int64_t il2cpp_gc_get_fragmented_bytes();

// Sum of the sizes of all LIVE blocks in one generation (0/1/2/3). The
// analogue of GCInterface::GetGenerationSize (the SizeAfterBytes of the
// reported collection). Out-of-range generations return 0.
int64_t il2cpp_gc_get_generation_size(int32_t generation);

// The maximum small-object generation number (GC.MaxGeneration). The
// collector's oldest small-object generation is Gen2 (LOH objects are 3, but
// .NET's MaxGeneration reports the small-object maximum == 2).
int32_t il2cpp_gc_get_max_generation();

// Register/cancel full-GC notification thresholds. The M3 collector is a
// BLOCKING (non-concurrent) collector, so registration always succeeds and
// returns true (.NET: registration returns false when concurrent GC is
// enabled, which this collector never is). Thresholds are stored so a future
// notification mechanism can honour them.
bool il2cpp_gc_register_for_full_gc_notification(int32_t maxGenerationThreshold,
                                                 int32_t largeObjectHeapThreshold);
bool il2cpp_gc_cancel_full_gc_notification();

// Cumulative allocation counter for the CURRENT THREAD. The M3 collector is
// single-threaded, so "current thread" == the process-wide cumulative
// counter (identical to il2cpp_gc_get_allocated_bytes).
int64_t il2cpp_gc_get_allocated_bytes_for_current_thread();

// Approximate total bytes ever allocated (the same cumulative counter; the
// analogue of GCInterface::GetTotalAllocatedBytesApproximate -- the exact
// value is not tracked per-thread, only the total).
int64_t il2cpp_gc_get_total_allocated_bytes_approximate();

// GCSettings (System.Runtime.GCSettings) constants -- match the netcore
// enums (GCLatencyMode / GCLargeObjectHeapCompactionMode / the internal
// SetLatencyModeStatus).
constexpr int32_t kGcLatencyModeBatch = 0;
constexpr int32_t kGcLatencyModeInteractive = 1; // the collector's default
constexpr int32_t kGcLatencyModeLowLatency = 2;
constexpr int32_t kGcLatencyModeSustainedLowLatency = 3;

constexpr int32_t kGcLohCompactionModeDefault = 1;   // the collector's default
constexpr int32_t kGcLohCompactionModeCompactOnce = 2;

constexpr int32_t kGcSetLatencyStatusSucceeded = 0;
constexpr int32_t kGcSetLatencyStatusNoGcInProgress = 1;

// GCSettings.IsServerGC. Always false: the M3 collector is a workstation
// (non-concurrent, single-heap) collector.
bool il2cpp_gc_get_is_server_gc();

// GCSettings.LatencyMode getter/setter. The latency mode is a stored policy
// hint: the collector always runs full blocking cycles, so setting a mode
// records it and returns kGcSetLatencyStatusSucceeded (there is no
// NoGCRegion to refuse a change).
int32_t il2cpp_gc_get_latency_mode();
int32_t il2cpp_gc_set_latency_mode(int32_t mode);

// GCSettings.LargeObjectHeapCompactionMode getter/setter. Stored policy
// hint: the LOH is non-moving in M3, so the mode is recorded but not acted
// on until a compacting LOH path exists.
int32_t il2cpp_gc_get_loh_compaction_mode();
void il2cpp_gc_set_loh_compaction_mode(int32_t mode);

// GetMemoryInfo data surface (the analogue of GCInterface::GetMemoryInfo).
// The icall layer assembles the managed GCMemoryInfo object from these
// fields. Field meanings follow the netcore GCMemoryInfoData layout:
//   highMemoryLoadThresholdBytes -- the runtime's fixed memory budget
//   totalAvailableMemoryBytes    -- same budget (the maximum the collector
//                                    addresses)
//   memoryLoadBytes              -- memory load percentage scaled against
//                                    the budget
//   heapSizeBytes                -- live managed-heap bytes
//   fragmentedBytes              -- reclaimable free-list bytes
//   totalCommittedBytes          -- committed segment bytes
//   promotedBytes                -- bytes promoted by the last collection
//   pinnedObjectsCount           -- always 0 (no pinning in M3)
//   finalizationPendingCount     -- entries awaiting their finalizer run
//   index                        -- collection sequence number (1-based)
//   generation                   -- the generation collected last (2)
//   pauseTimePercentage          -- il2cpp_gc_get_last_gc_percent_time_in_gc
//   compacted                    -- true (Gen0 survivors are compacted)
//   concurrent                   -- false (no background collection)
struct GcMemoryInfoData
{
    int64_t highMemoryLoadThresholdBytes;
    int64_t totalAvailableMemoryBytes;
    int64_t memoryLoadBytes;
    int64_t heapSizeBytes;
    int64_t fragmentedBytes;
    int64_t totalCommittedBytes;
    int64_t promotedBytes;
    int64_t pinnedObjectsCount;
    int64_t finalizationPendingCount;
    int64_t index;
    int32_t generation;
    int32_t pauseTimePercentage;
    bool    compacted;
    bool    concurrent;
};

void il2cpp_gc_get_memory_info(GcMemoryInfoData& out);

} // namespace il2cpp
