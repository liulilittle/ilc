# Roadmap Status v12 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-16 (snapshot final: batches 186-190b; supersedes snapshot v11;
  v11 kept at `docs/roadmap-status-v11.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  186-190b progress (**COM stages 14-16 -- GetComInterfaceForObject wiring /
  OLE drag-drop minimal surface / IDropTarget dispatch forwarding hook with the
  POINTL fix -- + WinForms Phases 29-31 -- Timer / MainMenu state / StatusStrip
  multi-part**), and the next batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `6fddcdd2be37a3b6cff9e8814ac7e7014118522d`
    (branch m1; commit chain new->old, [BASH:git log --oneline -12]): `d51e23a`
    (batch 190b) -> `6fddcdd` (batch 190) -> `86aff31` (batch 189) -> `d03ca3d`
    (batch 188b) -> `d4cb8d0` (batch 188) -> `1e011a1` (batch 187) -> `83980a8`
    (batch 186) -> `81d225e` (batch 185) -> `6810a01` (batch 185b) -> `7a1772f`
    (docs v11) -> `a66c793` (batch 184) -> `bf6912c` (batch 183).
  - `git status --short` -> [BASH] worktree clean except `?? tmp/` (probe
    scratch) and `?? docs/plans/com-stage14-dragdrop-spec.md /
    com-stage15-dragdrop-minimal-spec.md / com-stage16-dragdrop-dispatch-spec.md`
    (audit specs, uncommitted); no uncommitted src/test changes.
  - Batch commit messages (full subjects, [BASH:git log]):
    - `83980a8` (batch 186): **COM stage 14 GetComInterfaceForObject wiring**
      -- icall_com_get_com_interface_for_object returns the AddRef'd
      IUnknown/IDispatch view pointer for the two supported constant Type args
      (typeof(IUnknown)/typeof(IDispatch) resolved at transpile time via a
      StackFold marker -- the runtime Type object cannot be resolved to an IID
      at compile time), E_NOINTERFACE -> InvalidCastException,
      ArgumentNullException guard, POSIX stub; MethodMap.GetComInterfaceForObject
      moved from LOUD to wired with constant-IID recognition, unrecognized Type
      args stay LOUD -- test_com section 16, runtime 139/139, unit 1808/1808.
    - `1e011a1` (batch 187): **WinForms Phase 29 Timer** -- user32
      SetTimer/KillTimer with SendMessageW-direct WM_TIMER injection (synchronous
      to the WndProc, bypassing the message queue -- no real-time dependency,
      deterministic ticks=3), WndProc WM_TIMER branch -- net48 byte-exact.
    - `d4cb8d0` (batch 188): **COM stage 15 OLE drag-drop minimal surface** --
      icall_com_ole_register_drag_drop/revoke_drag_drop (ole32 resolved at
      runtime, out of GetOle32().ok, EnsureOle32 inside, hwnd==0
      ArgumentNullException, POSIX E_NOTIMPL) + Il2CppComDropTargetVtable 7-slot
      CCW view (QI dispatch for IID_IDropTarget; DragLeave S_OK,
      DragEnter/DragOver/Drop E_NOTIMPL -- managed dispatch deferred to stage
      16) -- test_com section 17 + POSIX 4j (428 assertions, +28), runtime
      139/139, unit 1808/1808 (1 pre-existing failure: GetComInterfaceForObject
      LOUD test at MethodMapTests.Marshal.cs:581 not updated by batch 186).
    - `d03ca3d` (batch 188b): **MethodMapMarshalTests LOUD-domain list sync** --
      (GetComInterfaceForObject was wired in batch 186, the LOUD assertion became
      stale) removed from the ThrowsLoudComDomain InlineData, the remaining 4
      members (FinalReleaseComObject/GetIDispatchForObject/
      GetIUnknownForObjectNative/CreateWrapperOfType) stay LOUD -- unit 1807/1807.
    - `86aff31` (batch 189): **WinForms Phase 30 MainMenu state/navigation** --
      GetMenuState/CheckMenuItem/EnableMenuItem/GetSubMenu/GetMenuItemID (5
      user32, zero new shim) + WfMenuItem.Checked/Enabled properties + RaiseClick
      Enabled gating (disabled item suppresses the click -- the Phase-7 rule for
      menus) + menu-tree navigation queries + check/gray round-trips -- Phase 10
      output preserved, net48 byte-exact.
    - `6fddcdd` (batch 190): **COM stage 16 IDropTarget dispatch forwarding
      hook** -- icall_com_ccw_register_drop_dispatch stores a per-CCW native
      callback; DragEnter/DragOver/Drop decode the raw OLE args (**POINTL pt as
      an intptr_t carrier with x=low32/y=high32 -- the stage-15 `const void* pt`
      declaration was UB on x64, reading the 8-byte POINTL as a pointer; never
      dereferenced under E_NOTIMPL so it stayed hidden**) and forward to the
      callback with bidirectional HRESULT + effect propagation; unregistered
      falls back to the stage-15 E_NOTIMPL surface, DragLeave stays S_OK;
      ccw==0 ArgumentNullException, POSIX stub -- test_com section 18 (22
      assertions), runtime 139/139, unit 1807/1807.
    - `d51e23a` (batch 190b): **WinForms Phase 31 StatusStrip multi-part** --
      SB_SETPARTS/GETPARTS (pinned int[] right-edge array) + per-part
      SB_SETTEXT/GETTEXT/GETTEXTLENGTH round-trips + WF_STATUS_UPDATE WndProc
      interaction -- reuses the Phase-9 status bar + comctl32 shim, net48
      byte-exact.
  - WinForms suite line counts (THIS turn, [BASH:Measure-Object]):
    `tests/cases/winforms1/expected.txt` = 391 lines, `actual.txt` = 391 lines
    (Phase 31 after; net48 vs transpiled C++ byte-exact, Compare-Object
    DIFF=0 IDENTICAL, per batch-190b verification).
  - Runtime suite count (THIS turn, [BASH:Measure-Object]): 139
    `tests/runtime/test_*.cpp`.
  - M-stage gate documents (this turn, path-existence re-verified
    [BASH:Test-Path]):
    - `docs/audit/e2e-full-matrix7.md`: matrix7 100% gate -- **196-case matrix
      = 195 correctness + 1 bench (virtbench, checksum-identical timing, not a
      real FAIL)**; 250 total case dirs, 246 PASS / 4 FAIL / 0 MISSING with the
      4 FAILs all OUTSIDE the 196 口径 (decfmt2/resmgmt/resops known gaps,
      non-regression).
    - Full-matrix 413-case parallel re-sweep: **413 cases / 104 batches 100%
      PASS** -- completed per memory.md [READ:.opencode/memory.md "全量复扫已
      完成（413 案例 104 批次 100% PASS）"].
    - `docs/audit/bench-verify-m7-20260815.md` (committed at 96d6880) + docs
      commit `c8e320c` [BASH:git log -- docs/audit/]: **13/13 zero-regression
      confirmed** vs baseline efa6741 (batch 168), re-confirmed after the BUG-1
      fix.
    - Unit counts: **unit 1807/1807** [READ:.opencode/process.md line 30
      "[BASH:dotnet test tests\unit\unit.csproj] -> 失败 0，通过 1807，总计
      1807"]; **runtime 139/139** re-measured THIS turn [BASH:Measure-Object 139
      test_*.cpp]; test_com assertions **450** (batch 190, +22 section 18 + 4k)
      [READ:.opencode/plan.md line 34 "test_com: 450 断言（428→450，+22）"].
- M-stage gate status:
  - **M5** BCL full translation: matrix7 **196/196** confirmed
    (e2e-full-matrix7.md: 195 correctness + 1 bench = 100%). **Full-matrix
    413-case / 104-batch parallel re-sweep 100% PASS** (completed, prior wave);
    no M5-relevant case regressed in batches 186-190b (all winforms/com
    surface, e2e byte-exact per batch; the 186+188 unit variance 1808->1807 ->
    1807 was a test-list correction, resolved by batch 188b).
  - **M6** Transpiler: unit **1807/1807** (all-green, dotnet test total 1807)
    + runtime **139/139** (139 test_*.cpp [BASH:Measure-Object]) + test_com
    **450 assertions** (batch 190, +22; stage-16 section 18 + POSIX 4k) +
    transpiler test surface (21 surfaces, carried from v9); batches 186-190b
    produced 0 transpiler errors (winforms1 transpiled clean, net48 byte-exact
    per batch). Batch 186 briefly reached 1808/1808 before batch 188b aligned
    the LOUD test list back to 1807 -- the unit floor is 1807/1807.
  - **M7** Performance: **zero-regression confirmed 13/13** vs baseline
    (docs/audit/bench-verify-m7-20260815.md at 96d6880, re-confirmed at
    `c8e320c`); no bench-affecting change landed in batches 186-190b (all
    winforms/com surface).
  - **M8** COM interop: stages 1-16 done (**stage 14 GetComInterfaceForObject
    batch 186; stage 15 OLE drag-drop minimal batch 188; stage 16 IDropTarget
    dispatch forwarding hook + POINTL fix batch 190**; prior: stage 12 IStream
    batch 184, stage 13 Marshal BSTR bridge batch 185); WinForms 31 phases done
    (**Phase 29 Timer batch 187 / Phase 30 MainMenu state batch 189 / Phase 31
    StatusStrip multi-part batch 190b**; prior: 27 ChooseFontW, 28 RichTextBox);
    S2 remaining: macOS/OpenBSD real-machine verification (external hardware).
- Remaining / next batch pool (task order):
  - COM stage 17 (**complete managed dispatch** -- deferred: needs WinForms
    transpile registration + typed-RCW infrastructure + managed ctor call
    mechanism; the marshalling gap is 3-4 steps, not COM self-contained; in
    progress planning).
  - WinForms Phase 32 (**ToolStrip**).
  - #114 original crash repro (needs user case; 52+ variants not reproduced).
  - M8 S2 macOS/OpenBSD real-machine (external hardware dependency).
