# ILC Roadmap Status — v16 (batches 231-254)

Snapshot: HEAD `84ccbe0` (batch 254). Continuation of v15 (batches 221-230).

## Completed since v15

### WinForms stage 3 (comctl32 deep surface) — winforms1 1-89, 968 lines byte-exact
- `5c749ed` (batch 231): Phase 66 ListView header column rect —
  HDM_GETITEMRECT (0x1207) col0 non-empty + col1 right (relative-only).
- `aad4f02` (batch 232): Phase 67 header sort arrow — HDM_SETITEM fmt
  HDF_SORTUP does NOT persist headless (fmt 0 observed).
- `19a425c` (batch 233): Phase 68 Edit first visible line —
  EM_GETFIRSTVISIBLELINE 0 (at_top).
- `64a68ae` (batch 234): Phase 69 ListView double-buffer style —
  LVS_EX_DOUBLEBUFFER round-trip (0 -> 65536).
- `f22ce9c` (batch 235): Phase 70 Edit password char — EM_GETPASSWORDCHAR
  default 42 (asterisk), SET keeps 42.
- `467fb19` (batch 236): Phase 71 Edit read-only toggle — EM_SETREADONLY
  set/clear both return 1 (observed).
- `0be0823` (batch 237): Phase 72 ListView delete items —
  LVM_DELETEITEM/DELETEALLITEMS (3->2->0).
- `ab99a41` (batch 238): Phase 73 ComboBox delete string —
  CB_DELETESTRING (3 -> remaining 2).
- `d269c19` (batch 239): Phase 74 ListBox delete string —
  LB_DELETESTRING (3 -> remaining 2).
- `5b12f39` (batch 240): Phase 75 ComboBox/ListBox reset content —
  CB/LB_RESETCONTENT (3->0).
- `a461e2e` (batch 241): Phase 76 Edit margins — EM_SET/GETMARGINS round-trip
  (20/30).
- `08862ed` (batch 242): Phase 77 Edit format rect — EM_SETRECT ignored on
  single-line edit (GETRECT client rect, non-empty only).
- `37c02d8` (batch 243): Phase 78 RichTextBox default char format —
  SCF_DEFAULT readback CFE_BOLD=True (observed) + cbSize 116.
- `0e77db3` (batch 244): Phase 79 StatusBar text readback — SB_GETTEXT
  "Readback" (len 8, ret 8, match).
- `955507d` (batch 245): Phase 80 StatusBar icon — SB_GETICON 0 after
  SB_SETICON (not persisted, observed).
- `35caa89` (batch 246): Phase 81 StatusBar borders — SB_GETBORDERS 0/2/2
  (vb_positive, raw observed).
- `09a111b` (batch 247): Phase 82 StatusBar Unicode format —
  SB_GETUNICODEFORMAT 1/1 (Unicode default).
- `5d3a865` (batch 248): Phase 83 ListView column header autosize —
  LVSCW_AUTOSIZE_USEHEADER -> 300.
- `d944d55` (batch 249): Phase 84 ListView select all — LVM_SETITEMSTATE
  LVIS_SELECTED on 5 rows -> GETSELECTEDCOUNT 5 (0x1032 = GETSELECTEDCOUNT/
  GETSELECTIONMARK same message, clarified).
- `20ad7db` (batch 250): Phase 85 ListView focus item — LVNI_FOCUSED -> -1
  (no-focus sentinel).
- `d61d4a5` (batch 251): Phase 86 StatusBar parts count — SB_GETPARTS
  3/150/300/-1 round-trip.
- `f8e85cf` (batch 252): Phase 87 Edit line scroll — EM_LINESCROLL 0->2.
- `0c68955` (batch 253): Phase 88 RichTextBox background color —
  EM_GETBKGNDCOLOR 0 (not persisted, observed).
- `84ccbe0` (batch 254): Phase 89 StatusBar simple mode — SB_SIMPLE 0/0
  (observed).

### Verification waves (main-agent direct)
- test_com PASS, runtime 139/139, unit 1807/1807, winforms1 e2e 968 byte-exact
  — green after batches 231-254 (winforms1 e2e-only; no transpiler/runtime
  changes). Intermittent p10_drain_count 4vs3 message-pump timing artifact
  (re-run stable, documented in discover.md).

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. winforms1 stage-3 continuation (Phase 90+; the headless-safe scalar/rect/
   enum surfaces are largely exhausted -- remaining faces are notification
   events (message-pump hangs risk) and WebBrowser (larger effort, COM
   stage-18 IDispatch infra ready).
4. Full-matrix rescan (19-case sample PASS; full 403-case sweep deferred).
5. docs/roadmap-status v17 on the next snapshot.
