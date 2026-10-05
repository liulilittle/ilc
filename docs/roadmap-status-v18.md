# ILC Roadmap Status — v18 (batches 256-266, winforms1 Phase 100 milestone)

Snapshot: HEAD `0335442` (batch 266). Continuation of v17 (full-matrix rescan).

## Completed since v17

### winforms1 Phase 100 milestone — 100 phases / 1001 lines byte-exact
- `35d6719` (batch 256): Phase 90 ListView label-edit notification
  (LVN_BEGINLABELEDITW, -105) via SYNCHRONOUS SendMessageW — replaces the
  abandoned Phase-64 PostMessage+pump approach (3 hangs); the synchronous
  notification path has no message pump, no hang risk. Marshal.ReadInt32/
  WriteInt32 are NOT on the transpiled Marshal surface — Marshal.Copy is used.
- `c9327fe` (batch 257): Phase 91 LVN_COLUMNCLICK (-108) synchronous.
- `92a1e93` (batch 258): Phase 92 LVN_ITEMACTIVATE (-114) synchronous.
- `5588180` (batch 258b): LVN code clarification — LVN_DBLCLK does NOT exist
  (real double-click = LVN_ITEMACTIVATE); the -105 used in Phase 90 is
  LVN_BEGINLABELEDITW. Comment-only.
- `9d71552` (batch 259): Phase 93 LVN_ENDLABELEDITW (-106) synchronous.
- `86d838e` (batch 260): Phase 94 LVN_BEGINDRAG (-109) synchronous.
- `a491dd6` (batch 261): Phase 95 LVN_BEGINRDRAG (-110) synchronous.
- `3e6f02a` (batch 262): Phase 96 LVN_KEYDOWN (-155) synchronous. Replaced the
  abandoned virtual-list LVN_GETDISPINFO approach (WndProc pointer-write via
  copy_to_unmanaged crashed the transpiled build — see discover).
- `ab41612` (batch 263): Phase 97 LVN_INSERTITEM (-102) synchronous.
- `342db57` (batch 264): Phase 98 LVN_DELETEITEM (-103) synchronous.
- `8f17d62` (batch 265): Phase 99 LVN_DELETEALLITEMS (-104) synchronous.
- `0335442` (batch 266): Phase 100 LVN_ITEMCHANGED (-101) synchronous (the
  synchronous variant of the Phase-21 SelectionChanged) — PHASE 100 milestone,
  1001 lines byte-exact.

### Abandoned approaches (documented in discover.md)
- Phase 96 virtual-list LVN_GETDISPINFO: WndProc pointer-write via
  Marshal.Copy(wbuf, 0, IntPtr, n) [copy_to_unmanaged] crashed the transpiled
  build (cdb: il2cpp_entry_main in WndProc, WM_NOTIFY). Rolled back; the
  notification faces use read-only NMHDR (copy_from_unmanaged) — safe.

### Verification waves (main-agent direct)
- test_com PASS, runtime 139/139, unit 1807/1807, winforms1 e2e 1001 byte-exact
  — green after batches 256-266. Intermittent p10_drain_count 4vs3
  message-pump timing artifact (re-run stable, documented in discover.md).
- Full-matrix 415/415 effective PASS (v17) still the reference sweep.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. winforms1 stage-3 continuation (Phase 101+; the synchronous notification
   pattern is proven — LVN_ITEMCHANGING, TVN_* notifications next; WebBrowser
   is a larger effort, COM stage-18 IDispatch infra ready).
4. docs/roadmap-status v19 on the next snapshot.
