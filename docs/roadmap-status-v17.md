# ILC Roadmap Status — v17 (full-matrix complete rescan)

Snapshot: HEAD `bbc55a9` (batch 254 + roadmap v16). No code changes since v16.

## Completed since v16

### Full-matrix COMPLETE rescan (415/415 effective PASS) — first full sweep
- All 415 e2e cases (net48 oracle -> transpile -> MinGW build -> Compare-Object)
  run by the main agent directly, in ~15 batches of ~25 (log files persisted at
  tmp/matrix_A.log, tmp/matrix_B.log, tmp/matrix_C.log; resumable after each
  timeout).
- Result: 414 direct PASS + virtbench (checksum -166660532 IDENTICAL; the
  iface/poly/sealed/imulti loop counts are performance counters, not
  correctness — consistent with the batch-197 record).
- 0 real failures. (The only FAILs logged were invalid case NAMES — guessed
  directories that do not exist; the real case list is filtered by *.csproj.)
- This replaces the earlier 19-case sample and the 403-case deferred entry.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. winforms1 stage-3 continuation (Phase 90+; the headless-safe scalar/rect/
   enum surfaces are largely exhausted — remaining faces are notification
   events (message-pump hangs risk) and WebBrowser (larger effort, COM
   stage-18 IDispatch infra ready).
4. docs/roadmap-status v18 on the next snapshot.
