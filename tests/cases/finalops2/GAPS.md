# GAPS.md — finalops2 unwired/divering surface (e2e batch C17, 2026-08-10)

Case status: **PASS 42/42 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the type-level
finalizer registry (multi-type order, 3-level chain, instance-field
resurrection, KeepAlive boundary, WeakReference lifetime boundary) and is
byte-identical to net48. No product code modified; no commit made.

## D-FIN2-1 — static-field RESURRECTION is not supported on ILC (GAP-FIN-4 restated)

- **Phenomenon (recorded, probe PARKED)**: a finalizer that stores `this` into
  a STATIC reference field (`ResurrectS.Survivor = this`) resurrects the object
  on net48 (static fields are always roots; the object survives and its
  finalizer never re-runs) but NOT on ILC, where static reference fields are
  NOT GC roots (GAP-FIN-4, empirically verified in finalops C13: the object
  pinned only by a static field was collected and finalized mid-Main). After
  the ILC finalizer runs, the resurrected object is reclaimed by the next
  sweep and `Survivor` becomes a dangling static pointer (reading it is UB).
- **Why parked rather than byte-diffed**: (a) a System.WeakReference is cleared
  at the FIRST death sighting on BOTH runtimes (it does not track resurrection
  — probe-verified in finalops2 probe E: `rr_alive=False` while `rr_held=True`),
  so `wss.IsAlive` reads False on both and does NOT expose the difference;
  (b) reading the resurrected object through the dangling static pointer on
  ILC is use-after-free. The only observable divergence would be a USE of the
  resurrected object via the static field — unsafe on ILC. The instance-field
  resurrection shape (probe E) IS byte-exact, which is the wired surface.
- **Suspected root file**: `src/il2cppc` static-field emission (no
  `il2cpp_gc_add_static_root` for static reference fields; grep: zero
  occurrences) / `src/runtime` GC root-scan set (il2cpp_gc.h) — same root as
  finalops GAP-FIN-4.

## Notes

- net48 oracle evidence: `out/finalops2.exe` exit=0, 42 lines captured
  byte-for-byte; harness diff PASS (mingw + msvc), see the C17 report.
- Resurrection semantics probe-verified on net48: the weakref reads False after
  resurrection while the holder field proves liveness and the exactly-once
  counter proves no re-run — the ACTIVE resurrection probe (E) encodes exactly
  this byte-stable shape.
