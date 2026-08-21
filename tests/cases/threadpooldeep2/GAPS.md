# GAPS.md -- threadpooldeep2 recorded findings (e2e batch C57, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 16-line
byte-exact; expected.txt == oracle).

## D-TP2-1 -- fixed pool (N = min(CPU,4)) vs net48 on-demand growth: the peak concurrency VALUE is not assertable

- **Phenomenon**: the runtime pool is fixed at N = min(CPU core count, 4)
  (threadpool.cpp), while net48 grows on demand (default min = #processors).
  A numeric peak-concurrency equality (e.g. `peak == 4`) would FAIL on net48
  when it runs more workers; a `peak == N` equality would be machine-dependent.
- **Disposition**: recorded. Only the scheduling-robust boolean `peak >= 2` is
  asserted (both engines overlap busy workers on any >= 2-core host). The pool
  serves 64 queued items exactly once (queue depth), fans out over >= 2 distinct
  workers (round-robin), and passes state correctly.

## D-TP2-2 -- the pool does NOT root queued work items / delegates / state

- **Phenomenon**: threadpool.h documents "the caller keeps `state` alive: the
  pool does not root it". A queued delegate/boxed-int becomes unreachable once
  the main thread's frame no longer references it; a GC during the queue window
  could reclaim it before the worker runs.
- **Disposition**: recorded. This case follows the threadpoolcase/conccases
  contract -- all delegates/boxed ints are constructed on the MAIN thread; the
  re-entrant inner delegate is held in a STATIC field (GC root) so the worker's
  enqueue is safe; total allocations stay far below the 64KB Gen0 segment so no
  collection can trigger mid-test.

## D-TP2-3 -- worker-thread allocation is only safe below the collection threshold

- **Phenomenon**: the collector is single-threaded and walks only the calling
  thread's frame chain (framestack.cpp); `Thread.CurrentThread` allocates a
  fresh Thread object per call (threadobj.cpp:465). If a worker allocation
  triggered a Gen0 collection while the worker held unrooted mid-expression
  objects, the collector would not see them.
- **Disposition**: recorded. Workers are allocation-free except the
  Thread.CurrentThread probe (16 objects total, rootable via a static array);
  the whole test allocates ~150 small objects vs the 64KB Gen0 segment, so no
  collection fires mid-test.

## D-TP2-4 -- GetMaxThreads / SetMinThreads not wired; SetMaxThreads is a fixed-pool no-op

- **Phenomenon**: `GetMaxThreads` is LOUD-rejected (out-param write-back not
  expressible); `SetMinThreads` has no mapping; `SetMaxThreads` maps to a
  NO-OP `il2cpp_threadpool_set_max_threads` (the pool size is fixed at first
  use) with ReturnsValue=false.
- **Disposition**: recorded. SetMaxThreads is exercised as a bare statement
  (no return capture) followed by a queue that still runs -- the pool survives.
