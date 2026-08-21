# ILC Roadmap Status — v19 (batches 267-276, winforms1 Phase 110 cap)

Snapshot: HEAD `f3c8509` (batch 276). Continuation of v18 (Phase 100 milestone).

## Completed since v18

### winforms1 Phases 101-110 (synchronous notification pattern, TVN series) — 110 phases / 1031 lines byte-exact
- `fd153d7` (batch 267): Phase 101 LVN_ITEMCHANGING (-100) synchronous.
- `def83a9` (batch 268): Phase 102 TVN_SELCHANGED (-402) synchronous — the
  first tree-view notification.
- `c46e276` (batch 269): Phase 103 TVN_SELCHANGING (-401) synchronous.
- `1253774` (batch 270): Phase 104 TVN_ITEMEXPANDED (-406) synchronous.
- `669f383` (batch 271): Phase 105 TVN_ITEMEXPANDING (-405) synchronous.
- `97672be` (batch 272): Phase 106 TVN_ITEMCOLLAPSED (-408) synchronous.
- `dc106ee` (batch 273): Phase 107 TVN_ITEMCOLLAPSING (-407) synchronous.
- `e9c3d0b` (batch 274): Phase 108 TVN_BEGINLABELEDIT (-410) synchronous.
- `b3a9a0c` (batch 275): Phase 109 TVN_ENDLABELEDIT (-411) synchronous.
- `f3c8509` (batch 276): Phase 110 TVN_DELETEITEM (-413) synchronous.

### CRITICAL FINDING: winforms1 Main IL 64KB limit — Phase 110 is the cap
- Phase 111 (TVN_BEGINDRAG) made the Main method's IL exceed the 64KB method
  limit → the transpiler truncates the method (only the first 64KB is
  emitted) → the later phases are missing and the EARLY phases throw (abort,
  exit code 3). Verified: transpiled output had NO p90-p110 markers (the Main
  was cut); rolling back Phase 111 restored exp=act=1031 byte-exact.
- Detection rule: search the transpiled output for a LATE phase's marker
  string — absence means the method was truncated.
- Consequence: winforms1 is capped at 110 phases (1031 lines). Adding more
  would require splitting Main (high risk, not done).

### Verification waves (main-agent direct)
- test_com PASS, runtime 139/139, unit 1807/1807, winforms1 e2e 1031 byte-exact
  — green after batches 267-276.
- Full-matrix 415/415 effective PASS (v17) still the reference sweep.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. winforms1 is capped (110 phases / IL 64KB); further WinForms faces would
   need a Main split (deferred, high risk).
4. COM stage 20+ (new interfaces beyond the drag-drop/IDispatch automation
   surface; stage-18 IDispatch infra ready).
5. docs/roadmap-status v20 on the next snapshot.
