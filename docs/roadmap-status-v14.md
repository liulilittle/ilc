# ILC Roadmap Status — v14 (batches 209-220)

Snapshot: HEAD `ae2b9b9` (batch 220). Continuation of v13 (batches 197-208).

## Completed since v13

### WinForms stage 3 (comctl32 deep surface) — winforms1 1-55, 852 lines byte-exact
- `c959cf5` (batch 209): Phase 46 OLE drag-drop P/Invoke surface (ole32 shim,
  OleInitialize/RegisterDragDrop/RevokeDragDrop, CoInitializeEx MTA alignment;
  786 lines).
- `e54ecd8` (batch 210): Phase 47 managed drag-drop event chain (pure-managed
  IDropTarget47 + WfDropSink47; 797 lines).
- `8f03323` (batch 211): COM stage 18 IDispatch::Invoke -> managed method
  dispatch (register_invoke_dispatch + DispatchInvokeManaged; 'i'->VT_I4 /
  'v'->VT_EMPTY; is_instance gate; per-CCW dispIds; test_com 570 assertions).
- `5bc0e04` (batch 211b): COM stage 19 runtime — typed-argument decode
  (VT_R8 double + VT_BSTR string params, signature-driven call shapes,
  double/string returns).
- `a7a0704` (batch 212): COM stage 19 typed-argument TESTS — IAutoScale
  (Scale(double,string) void + Rate(double) double) + WfAutoScale in
  com17type, gen_v4; the (double,string) call shape fix; test_com 577
  assertions. **COM stages 1-19 complete (automation int32/double/string
  parameter surface).**
- `f5250b9` (batch 213/213b): Phase 48 ListView column sort — LVM_SORTITEMS
  (0x1030) + PFNLVCOMPARE callback (static SortByKey over SortKeys array,
  GetFunctionPointerForDelegate; 3 callbacks fired, lp_after=1/2/0 for
  SortKeys {2,0,1}); HDM_SORTITEMS does NOT fire the callback on v5.82
  (probed). 807 lines. Also fixed the -NoNewline expected.txt flattening.
- `759d4fc` (batch 214): Phase 49 ListView find + auto column width —
  LVM_FINDITEMW (LVFI_STRING = EXACT match, Find(Beta)=1 / Find(Be)=-1
  probed) + LVSCW_AUTOSIZE (60->53). 816 lines.
- `2f8567b` (batch 215): Phase 50 ListView image list — ImageList_Create/Add/
  Destroy + LVM_SET/GETIMAGELIST (LVSIL_SMALL round-trip, prev NULL). 822 lines.
- `5da7906` (batch 216): Phase 51 ComboBox dropdown state — CB_SHOWDROPDOWN +
  GETDROPPEDSTATE + GETDROPPEDCONTROLRECT (closed->open->closed; rect
  relative-only); fixed the missing CB_GETCURSEL constant. 829 lines.
- `ff66ecb` (batch 217): Phase 52 ListView colors + per-page count —
  LVM_SET/GETBKCOLOR round-trip (0xFFFFFF), setter returns prev CLR_NONE=1;
  PROBED report-view TEXTBKCOLOR reads back 0; LVM_GETCOUNTPERPAGE=7. 837 lines.
- `0734922` (batch 218): Phase 53 ListView item rect + ensure visible —
  LVM_GETITEMRECT (rect0 non-empty + rect1 below + positive height) +
  LVM_ENSUREVISIBLE. 843 lines.
- `3d4d5e8` (batch 219): Phase 54 ListBox caret + item rect — LB_GETCARETINDEX
  (headless stays 0 after set) + LB_GETITEMRECT (non-empty). Replaced the
  dropped TVM_SORTCHILDREN approach (0x1115 crashed comctl32!TV_SortCB on
  v5.82 headless — see discover). 847 lines.
- `ae2b9b9` (batch 220): Phase 55 ListBox extended multi-select range —
  LB_SELITEMRANGEEX (MAKELPARAM 1..2) on LBS_EXTENDEDSEL: GETSELCOUNT=2,
  GETSELITEMS={1,2}; PROBED plain LB_SELITEMRANGE(0x199) headless no-op,
  RANGEEX return 0 on success (not BOOL). 852 lines.

### Verification waves (main-agent direct — subagents quota-limited)
- runtime 139/139, unit 1807/1807, test_com 577, pin 3/3 True — green after
  batches 209-220 (winforms1 is e2e-only; no transpiler/runtime changes).
- Full-matrix 19-case sample (post-213..220): 19/19 effective PASS
  (virtbench checksum -166660532 identical — the iface/poly/sealed/imulti
  loop-count differences are performance counters, not correctness).

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. winforms1 stage-3 continuation (Phase 56+; WebBrowser re-evaluation with
   COM stage-18 IDispatch infrastructure — a larger effort).
4. Full-matrix rescan (19-case sample PASS; the full 403-case sweep deferred
   to a direct-run pass).
5. docs/roadmap-status v15 on the next snapshot.
