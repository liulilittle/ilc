# GAPS.md — threadops2 unwired/divering surface (e2e batch C18, 2026-08-10)

Case status: **PASS 15/15 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the thread
pool worker (QueueUserWorkItem 1-arg/2-arg + main-thread IsThreadPoolThread),
the foreground/background flag roundtrips and Thread.MemoryBarrier, and is
byte-identical to net48. No product code modified; no commit made.
(compiler=wsl is BLOCKED at HEAD by a SHARED-RUNTIME build error unrelated to
this case -- D-C18-WSL-1 in the batch report: delegateext.cpp:158 uses
std::abort() without <cstdlib>, GCC 7.5 rejects; the C17 case virtsealed
fails identically.)

## D-THR2-1 — Thread.Name get/set is NOT mapped in the transpiler (HEAD 9ec11f8)

- **Phenomenon (recorded, probes PARKED)**: the RUNTIME icalls exist and are
  fully implemented -- threadobj.cpp `icall_threadobj_get_name` (:346) and
  `icall_threadobj_set_name` (:364, net48 reference semantics: null/empty
  CLEARS without throwing, non-empty set on an ALREADY-NAMED thread throws
  InvalidOperationException "A thread's Name may only be set once."). The
  object even carries the name as a GC reference (refFieldBitmap bit 5,
  threadobj.cpp:49). BUT `MethodMap.Thread.cs` (TryMapThread, 132 lines) has
  NO `get_Name` / `set_Name` case at HEAD -> an unmapped member falls to the
  generated-name path -> transpiles to an undefined extern
  `System_Threading_Thread_get_Name_<hash>` -> C++ link failure (the exact
  prediction in threadops GAPS.md D-THR-1). The unit suite has no
  MethodMapTests.Thread Name case either.
- **Why parked rather than byte-diffed**: an active Name probe kills the WHOLE
  case at the cmake-build stage (zero output produced), hiding the 15 passing
  pool/foreground-bg/MemoryBarrier probes. The probes are written (BLOCKED) at
  the end of threadops2.cs with the net48 oracle values per the threadobj.cpp
  setter contract; uncomment + rerun once the mapping lands.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Thread.cs` TryMapThread
  (add `case "get_Name"` -> icall_threadobj_get_name and `case "set_Name"` ->
  icall_threadobj_set_name). One case each; the runtime side needs no change.

## D-THR2-2 — in-worker IsThreadPoolThread probe is a net48/ILC DIVERGENCE

- **Phenomenon (recorded, probe ABSENT)**: a pool worker callback cannot probe
  `Thread.CurrentThread.IsThreadPoolThread` because (a) CurrentThread allocates
  a fresh Thread object and the M-stage managed allocator is SINGLE-THREADED
  (worker callbacks must be allocation-free -- threadcase2 contract), and (b)
  even with a PREALLOCATED Thread object passed as the queue state, net48
  resolves the flag PER-THREAD-OBJECT (the state object is not the current
  worker's Thread -> false) while the ILC icall is CURRENT-THREAD-scoped
  (thread.cpp `icall_thread_get_is_thread_pool_thread` ignores self and reads
  the thread_local worker flag -> true). Documented deviation; probing it would
  diverge the byte diff. The MAIN-thread probe (`pool_main_ispool=False`) is
  active and byte-exact.
- **Suspected root file**: `src/runtime/src/thread.cpp:149` +
  `src/runtime/src/threadpool.cpp:316` (thread_local g_isPoolWorker).

## D-THR2-3 — IsBackground set on a STOPPED thread (after Join) diverges

- **Phenomenon (recorded, probe PARKED)**: net48 throws ThreadStateException
  setting IsBackground on a joined/stopped thread; the ILC model accepts the
  set on any state (threadobj.cpp:331-338, documented deviation). All ACTIVE
  probes set the flag before Start / on the running CurrentThread (net48-legal)
  and are byte-exact. A post-Join set probe would diverge.
- **Suspected root file**: `src/runtime/src/threadobj.cpp:335`.

## Notes

- net48 oracle evidence: `out/threadops2.exe` exit=0, 15 lines captured
  byte-for-byte; harness diff PASS (mingw + msvc), see the C18 report.
- Pool determinism: four DISTINCT single-writer static flags (one per queued
  item, D1) + a bounded poll (Sleep(10) x1000) -- no shared-counter race.
