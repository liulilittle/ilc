# Roadmap Status v13 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-17 (snapshot: batches 197-208; supersedes snapshot v12;
  v12 kept at `docs/roadmap-status-v12.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  197-208 progress (**TRUE PINNING runtime fix + WinForms Phases 38-45
  (stage-2 closure) + COM stage 17 full managed drag-drop dispatch closure
  (3 prerequisite batches + the CCW->dispatch bridge)**), and the next batch
  task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `37e441a` (branch m1; commit chain
    new->old, [BASH:git log --oneline -13]): `37e441a` (batch 208) ->
    `bbc6834` (batch 207) -> `9900310` (batch 206) -> `62bcf68` (batch 205) ->
    `8672320` (batch 204) -> `54162e4` (batch 203) -> `3f6415c` (batch 202) ->
    `02a5498` (batch 201) -> `fea69be` (batch 200) -> `8b3e634` (batch 199) ->
    `a10a5c3` (batch 198) -> `d4f7ec5` (batch 197) -> `84b9a14` (batch 196).
  - `git status --short` -> [BASH] worktree clean except untracked
    `docs/plans/*.md` (audit specs) + `tmp/` (probe scratch); no uncommitted
    src/test changes.
  - Batch commit messages (full subjects, [BASH:git log]):
    - `d4f7ec5` (batch 197): **TRUE PINNING for GCHandle.Alloc(obj, Pinned)
      (runtime)** -- the pinned referent is now IMMOVABLE (il2cpp_gc_add_pinned/
      remove_pinned + Gen0 compaction promotes a pinned survivor NON-MOVING),
      so AddrOfPinnedObject addresses stay valid across collections (the
      GCHandle.Pinned contract). Root cause: winforms1 Phase 27 (ChooseFontW)
      intermittently read iPointSize=0 because the font dialog's allocations
      triggered a Gen0 compaction that MOVED the pinned cf27 array out from
      under the P27CfPtr passed to comdlg32. test_gchandle.cpp:
      pinned-address-stability-across-collections test. ALSO batch 197b:
      WinForms Phase 38 ComboBox deep item surface (592 lines byte-exact).
    - `a10a5c3` (batch 198): Phase 39 ListView checkbox + multi-select state
      surface (LVIS_STATEIMAGEMASK channel, v6-only dead-read negative proof,
      string enumeration for the M3 SZArray restriction).
    - `8b3e634` (batch 199): Phase 40 TextBox EDIT deep surface (EM_* line
      family, length-prefix via pinned int[0] low WORD -- no WriteInt16).
    - `fea69be` (batch 200): Phase 41 TreeView readback surface
      (TVM_GETITEMW/SETITEMW 0x113E/0x113F corrected, TVIF_PARAM fixed
      0x0008->0x0004 SDK-verified, zero regression).
    - `02a5498` (batch 201): Phase 42 RichTextBox line surface (EM_* family
      re-verified on RichEdit20W, REAL EN_CHANGE via injected WM_CHAR,
      RichEdit CR-only geometry probe-pinned).
    - `3f6415c` (batch 202): Phase 43 ListView column order
      (LVM_GET/SETCOLUMNORDERARRAY bare int[], DisplayIndex back-computation,
      display-only reorder evidence).
    - `54162e4` (batch 203): Phase 44 window layout surface (WM_GETMINMAXINFO
      + P44Active guard + Marshal.Copy writeback + relative-layout reflow).
    - `8672320` (batch 204): Phase 45 TabControl layout (TCM_SETITEMSIZE
      two-step return-value readback + TCS_MULTILINE/FIXEDWIDTH rows) --
      **WinForms stage-2 closing phase**.
    - `62bcf68` (batch 205): **COM stage 17 batch 1** -- WinForms-mirror
      fixture (DragDropEffects enum + DragEventArgs + IDropTarget 4-method
      interface with DragEventArgs REF params, the first r:-param
      interface-dispatch coverage) + dual implementers, headless
      il2cpp_interface_dispatch slot-by-slot (test_com 474 assertions).
    - `9900310` (batch 206): **COM stage 17 batch 2** -- typed-RCW foundation:
      per-CCW typed interface view registration + 12-slot IDataObject view
      bridging native slots (3..11, oleidl order) to the MANAGED interface BY
      NAME (typeinfo virtualSlot lookup; interface slots are alphabetical) via
      il2cpp_interface_dispatch; undeclared native methods E_NOTIMPL; QI gated
      by il2cpp_is_instance (test_com 504 assertions).
    - `bbc6834` (batch 207): **COM stage 17 batch 3** -- managed ctor
      invocation (il2cpp_object_construct: object_new + typeinfo .ctor fnPtr
      + signature decode r:/i/V: + paramCount switch) + DragEventArgs grows
      IDataObject Data + typed-RCW direction (icall_com_wrap_data_object, 3
      vtable stubs) closing the CCW-loop; transpiler 2 changes (ReferenceCppType
      interface->Il2CppObject* + FieldAssignStmt IsInterfaceType guard)
      (test_com 524 assertions).
    - `37e441a` (batch 208): **COM stage 17 batch 4 (CCW->dispatch bridge)** --
      the full managed drag-drop dispatch: register_drop_dispatch_managed
      (self-validating) + drop_dispatch_managed pump (wrap_data_object ->
      il2cpp_object_construct(DragEventArgs, typedRcw(pDataObj), x, y, effect)
      -> il2cpp_interface_dispatch to the managed OnDragEnter with effect
      write-back; is_instance gate; E_NOTIMPL fallback) wired into
      CcwDropDispatchImpl (managed preferred over the stage-16 native
      callback; DragLeave S_OK-locked); section 22a every-platform pump
      discriminators + 22b Windows view integration (test_com 555
      assertions).

## M-stage gates

| Gate | Status | Evidence |
|---|---|---|
| M1 (transpiler + runtime core) | COMPLETE | prior snapshots; 400+ case matrix |
| M2 (string/char/array deep) | COMPLETE | prior snapshots |
| M3 (SZArray return restriction) | COMPLETE (documented) | SelectedIndices/GetColumnOrder return STRING; arrayjoin GAPS D-AJ-1 |
| M4 (P/Invoke + interop) | COMPLETE | marshalcase/marshaldeep/hglobaldeep et al. |
| M5 (matrix7 + full matrix) | COMPLETE | 104-batch x 413-case sweep 100% PASS (batch 176); post-207 sample sweep 30/30 PASS + batch-208 wave 7/7 |
| M6 (unit + runtime suites) | COMPLETE | unit 1807/1807; runtime 139/139; test_com 555 assertions (test_gchandle pin-stability included) |
| M7 (bench zero regression) | COMPLETE | bench verify 13/13 zero regression (prior) |
| M8 (COM interop chain) | **COMPLETE (stage 17 closure)** | COM stages 1-17: RCW/CCW/IDispatch/Marshal/IOleObject/IConnectionPoint/fire/multi-sink/IEnumVARIANT/BSTR/IStream/GetComInterfaceForObject/drag-drop minimal/dispatch hook/typed-RCW/ctor mechanism/CCW->dispatch bridge -- full managed drag-drop dispatch loop closed (native IDropTarget -> DragEventArgs construction -> managed OnDragEnter) |

## WinForms phase board (winforms1, 778 expected lines byte-exact)

- Phases 1-45 COMPLETE (stage 2 CLOSED): buttons/labels/EDIT/ListBox/
  CheckBox/Radio/GroupBox/ComboBox/TabControl/TreeView/ListView (incl.
  checkbox+multi-select+column-order)/StatusStrip/ProgressBar/ToolStrip/
  RichTextBox/Timer/PictureBox/NotifyIcon/MainMenu state/GroupBox/Panel
  scroll/DataGridView-simplified/font+color+file dialogs/window layout
  (WM_GETMINMAXINFO) 等。
- Stage-3 remaining candidates: printing (printer dependency -- excluded),
  WebBrowser (COM M8 -- now RE-EVALUATE with stage-17 infrastructure),
  DataGridView full (high-cost transpile -- excluded), managed drag-drop in
  winforms1 (COM stage 17 mechanism directly consumable -- candidate).

## COM stage board (test_com, 555 assertions)

- Stages 1-16 COMPLETE (prior snapshots).
- Stage 17 COMPLETE (batches 205-208): r:-param interface dispatch ->
  typed-RCW foundation (IDataObject view by-name dispatch) -> managed ctor
  mechanism (il2cpp_object_construct + typed-RCW direction) -> CCW->dispatch
  bridge (full managed drag-drop dispatch loop closed).

## Remaining / next batch pool

1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro;
   52+ variants not reproduced).
3. winforms1 stage-3 re-evaluation with COM stage-17 infrastructure
   (WebBrowser / managed drag-drop in winforms1).
4. Full-matrix rescan post-transpiler-change (30-case sample PASS + 7-case
   wave PASS this snapshot; full 413-case rescan deferred due to subagent
   quota; direct-run method proven).
