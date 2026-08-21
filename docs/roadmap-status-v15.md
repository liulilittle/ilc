# ILC Roadmap Status — v15 (batches 221-230)

Snapshot: HEAD `b60065b` (batch 230). Continuation of v14 (batches 209-220).

## Completed since v14

### WinForms stage 3 (comctl32 deep surface) — winforms1 1-65, 887 lines byte-exact
- `374476f` (batch 221): Phase 56 RichTextBox character format —
  EM_SETCHARFORMAT/GETCHARFORMAT with CHARFORMAT2W (116-byte pinned int[29],
  Phase-28 probed layout): bold + red 0xFF0000 round-trip; corrected from the
  54-byte CHARFORMATW (cbSize mismatch -> set returns 0).
- `e7d3079` (batch 222): Phase 57 StatusBar part rect — SB_GETRECT through a
  pinned int[4] (part0 non-empty + part1 right + positive width, relative-only).
- `392028c` (batch 223): Phase 58 ProgressBar state — PBM_GETSTATE 0 fresh,
  PBM_SETSTATE writes not persisted headless (observed values). Replaced two
  crashed attempts (CBS_DROPDOWN edit CB_ERR; TCM_SETCURFOCUS Tab_WndProc AV).
- `eb71e0f` (batch 224): Phase 59 ListView top index + scroll — LVM_GETTOPINDEX
  0 -> 13 after LVM_ENSUREVISIBLE(19) on a 20-row list.
- `c68721a` (batch 225): Phase 60 ListView text color — LVM_SETTEXTCOLOR/
  GETTEXTCOLOR not round-tripped on an empty list headless (observed values).
- `4c745c0` (batch 226): Phase 61 ListBox select string — LB_SELECTSTRING
  (find + select combined) returns 1 + LB_GETCURSEL=1 for "Be".
- `caa6252` (batch 227): Phase 62 ListBox multi-column width —
  LB_GETCOLUMNWIDTH -1 before/after LB_SETCOLUMNWIDTH (not stored headless).
- `0edaebe` (batch 228): Phase 63 ListView selection mark — LVM_GETSELECTIONMARK
  0 before/after SETSELECTIONMARK(2) (not stored headless).
- `32ab605` (batch 229): Phase 64 ComboBox dropped width —
  CB_SETDROPPEDWIDTH/GETDROPPEDWIDTH round-trip (160 -> 200). Replaced the
  abandoned LVN_DBLCLK message-pump approach (3 hangs — see discover).
- `b60065b` (batch 230): Phase 65 ComboBox item height — CB_SETITEMHEIGHT/
  GETITEMHEIGHT round-trip (16 -> 24).
- `b0543ea` (chore): com17type expected.txt oracle committed (was untracked).

### Abandoned approaches (documented in discover.md)
- TVM_SORTCHILDREN (0x1115) crashed comctl32!TV_SortCB on v5.82 headless.
- CBS_DROPDOWN edit text (CB_GETEDITTEXT CB_ERR headless).
- TCM_SETCURFOCUS (0x130D) crashed comctl32!Tab_WndProc+0xa4d on v5.82.
- ListView LVN_DBLCLK via PostMessage + embedded PeekMessageW pump: 3 hangs.
- LB_SETITEMHEIGHT (0x1A2) crashed USER32!xxxFindString on wParam=0.

### Verification waves (main-agent direct)
- test_com PASS, runtime 139/139, unit 1807/1807, winforms1 e2e 887 byte-exact
  — green after batches 221-230 (winforms1 e2e-only; no transpiler/runtime changes).
- 19-case full-matrix sample PASS (post-213..220) with virtbench checksum
  -166660532 identical (loop counts differ — performance, not correctness).

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. winforms1 stage-3 continuation (Phase 66+; notification-event faces need
   careful design after the message-pump hangs; WebBrowser re-evaluation with
   COM stage-18 IDispatch infra is a larger effort).
4. Full-matrix rescan (19-case sample PASS; full 403-case sweep deferred).
5. docs/roadmap-status v16 on the next snapshot.
