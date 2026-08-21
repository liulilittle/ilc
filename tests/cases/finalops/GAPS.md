# GAPS.md — finalops unwired/divering surface (e2e batch C13, 2026-08-09)

Case status: **PASS 27/27 byte-exact** (net48 oracle vs ILC mingw transpiled run,
LF-join diff). The active probes cover only the wired finalizer surface. Every
gap below was VERIFIED empirically (transpile/build/runtime) before being
parked; each entry records the exact phenomenon and suspected root file.

## GAP-FIN-1 — discarded `new T()` with an EMPTY ctor is DCE'd

- **Phenomenon**: a helper `static void MakeDeadSilent(int n) { for (...) new
  SilentTrack(); }` where SilentTrack has only the implicit ctor (no field
  stores) transpiles to a loop that does NOTHING — the `il2cpp_object_new` is
  eliminated. net48 allocates the N objects (and their finalizers run);
  ILC allocates 0. Verified: first finalops driver run showed
  `SilentTrack.Finalized == 0` on ILC vs `3` on net48; generated code
  `Cases_FinalOps.cpp MakeDeadSilent` contains only `loc0 = loc0 + 1`.
- **Causal chain**: the transpiler DCEs a newobj whose result is unused AND
  whose ctor has no observable store. A ctor that STORES a field (TrackA's
  `Id = id`) survives DCE (the store is an observable side effect). The
  finalizer's own side effect is invisible to the DCE (it fires on GC, not at
  the newobj site). net48 never DCEs object allocation.
- **Worked around in the case**: SilentTrack's ctor stores `Id` (a real field),
  so the discarded allocation survives and the aggregate counter is real.
- **Suspected root file**: `src/il2cppc` DCE/dead-store pass (discarded-new
  elimination) — a `new` with an empty ctor is treated as side-effect-free.

## GAP-FIN-2 — an object passed to an icall in Main lands in a ROOTED temp and never dies

- **Phenomenon**: `WeakReference wd = new WeakReference(new object());` inline
  in Main: on ILC the target is a frame-rooted temp (`__frameRefs`), so
  `wd.IsAlive` stays True after GC.Collect; net48 collects it -> False.
  Same for `new WeakReference(new TrackA(21))` in Main: the TrackA never dies
  on ILC (finalizer never runs); net48 runs it. Verified in the first driver
  run (expected `False`/`fa 21`+count vs ILC `True`/no finalizer) and in the
  generated frame refs (`t0`/`inl2` in `__frameRefs`).
- **Causal chain**: M3 Task 9 roots every reference local/temp of the CURRENT
  frame for the whole method ("reference locals are GC roots while it is on
  the call stack"). net48 JIT uses precise liveness (an arg/temp dead after
  its last use). An object handed to a ctor/icall inside Main therefore
  outlives its last use on ILC.
- **Worked around in the case**: every must-die object is created inside a
  HELPER whose frame pops before the next collection — the temp is rooted
  only while the helper is on the stack, so the object is unreachable the
  moment the helper returns (the gcext pattern).
- **Suspected root file**: `src/il2cppc` frame-root emission (M3 Task 9),
  vs net48 precise liveness.

## GAP-FIN-3 — GC.SuppressFinalize / ReRegisterForFinalize are placeholder no-ops

- **Phenomenon**: `gcapi.cpp` icall_gc_suppress_finalize / re_register_for_
  finalize only null-check and return (source-verified: `icall_gc_suppress_
  finalize` body is `if (obj == nullptr) throw ANE;` only). net48 genuinely
  suppresses: `MakeSuppressed() { GC.SuppressFinalize(new Tracked()); }` ->
  net48 finalizer does NOT run (probe N=0 vs unsuppressed control N=1).
- **Isolated byte divergence NOT provable through the e2e harness**: any
  probe that hands the object to `GC.SuppressFinalize(obj)` roots `obj` in a
  Main-frame temp on ILC (GAP-FIN-2), so the object never dies and the
  finalizer never runs regardless of suppression — the two runtimes
  coincidentally agree (both N=0). The suppression gap is therefore recorded
  from source + the net48 semantic probe, not from a live ILC diff.
- **Suspected root file**: `src/runtime/src/gcapi.cpp:83-89,100-106`
  (no-op bodies) + `src/il2cppc` frame-root liveness (GAP-FIN-2).

## GAP-FIN-4 — STATIC reference fields are NOT GC roots on ILC

- **Phenomenon**: `Survivor = new TrackA(42)` (a static reference field),
  then GC.Collect: on ILC the object dies and its finalizer runs (verified
  in the second driver run: ILC printed `fa 42` early); net48 keeps it alive
  (static fields are always roots; finalizer only at process exit).
- **Causal chain**: the transpiler emits NO `il2cpp_gc_add_static_root` for
  static reference fields (grep: zero occurrences in the transpiler), and the
  generated static storage is a plain `Il2CppObject*` global the GC never
  scans. Only frame refs are roots.
- **Worked around in the case**: H keeps the survivor via a LOCAL extended
  with GC.KeepAlive + reads after every collect (net48 precise-liveness
  parity) and uses a SILENT finalizer so the net48 process-exit run of the
  still-pinned survivor is unobservable (byte-stable).
- **Suspected root file**: `src/il2cppc` static-field emission (no
  add_static_root) / `src/runtime` GC root-scan set (il2cpp_gc.h).

## Notes

- All active lines use ONLY the wired surface (GC.Collect/GetTotalMemory/
  GetGeneration/KeepAlive/WaitForPendingFinalizers/WeakReference/type-level
  ~Finalize). No product code was modified; no commit was made.
- net48 oracle evidence: `out/finalops.exe` exit=0, 27 lines captured
  byte-for-byte; harness diff PASS (see the C13 report).
