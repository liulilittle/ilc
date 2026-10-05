# Roadmap Status v10 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-16 (snapshot final: batches 176-182 progress; supersedes snapshot
  v9; v9 kept at `docs/roadmap-status-v9.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  176-182 progress (**WinForms Phase 22 TreeView + COM stage 9 multi-sink
  broadcast + user-method IntPtr return ResultKind + delegateext BUG-2/BUG-3
  typeId/combine-guard/FNV-1a hash + COM stage 10 IEnumVARIANT enumerator +
  WinForms Phase 23 PictureBox + Phase 24 NotifyIcon with the shell32 shim +
  Phase 25 file dialog with the comdlg32 shim + unit 1808/1808 first-ever
  all-green (batch 180b) + COM stage 11 IEnumVARIANT BSTR items + WinForms
  Phase 26 ColorDialog**), and the next batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `c8507df7f1ece46496cd74bf3aec5356da7e9a60`
    (branch m1; commit chain new->old, [BASH:git log --oneline -18]): `c8507df`
    (batch 182) -> `24da086` (batch 181) -> `035ef71` (batch 180b) -> `c79ef30`
    (batch 180) -> `3f79a12` (batch 179) -> `63b5ab8` (batch 178) -> `77d1d42`
    (batch 178b) -> `399865a` (batch 177c) -> `6f50dbe` (batch 177) ->
    `73f9389` (batch 177b) -> `0e646e6` (batch 176b) -> `7919a4d` (batch 176) ->
    `c8e320c` (docs bench verify M7 after BUG-1) -> `5692404` (batch 175b) ->
    `f9a96fa` (batch 175) -> `f8ce1ce` (batch 172) -> `acc0398` (batch 174) ->
    `af29562` (docs v9).
  - `git status --short` -> [BASH] worktree clean except `?? tmp/` (probe
    scratch only); no uncommitted src/test changes.
  - Batch commit messages (full subjects, [BASH:git log]):
    - `7919a4d` (batch 176): **WinForms Phase 22 TreeView** -- SysTreeView32
      TVM_INSERTITEMW (TVIF_TEXT|TVIF_PARAM, root + child), TVM_GETCOUNT,
      TVM_EXPAND (TVE_EXPAND), TVM_DELETEITEM -- TVITEMW via GCHandle-pinned
      int[] local layout, net48 byte-exact. (comctl32 v5.82 HTREEITEM is a
      non-deterministic heap address, 5 runs 5 values -- assertions
      booleanized, discover 181.)
    - `0e646e6` (batch 176b): **COM stage 9 IConnectionPoint multi-sink
      broadcast** -- each connection point holds up to kCcwMaxSinksPerCp=4
      sinks (the phase-5 single-sink ADVISELIMIT surface was the audit-flagged
      special case); Advise connects every sink with its own monotonic cookie
      (capacity exhaustion -> CONNECT_E_ADVISELIMIT), Unadvise releases
      exactly the cookie's connection, CCW destructor releases every remaining
      connection, both fire paths broadcast to ALL connected sinks surfacing
      the first non-S_OK HRESULT (all-S_OK -> S_OK, no listener -> S_FALSE).
      test_com section 10 updated + new section 11c (3 sinks on one CP each
      receive every fire byte-exact, per-cookie Unadvise, dispatch sink on its
      own event interface, 4-sink capacity + ADVISELIMIT overflow, all-
      unadvised -> S_FALSE): 223->255 assertions. runtime 139/139, build 0
      error/0 warning.
    - `6f50dbe` (batch 177): **user-method IntPtr return ResultKind (x64 handle
      truncation)** -- ResolveMethodDefinition/ResolveMemberReference user
      paths left ResultKind null for SignatureTypeCode.IntPtr/UIntPtr
      (0x18/0x19); the P/Invoke path sets 'ptr' (MethodRefResolver.cs:385) but
      user methods did not, so the caller temp was declared int32_t and the
      64-bit handle truncated (Phase 22 TreeView child insert returned NULL);
      user-method IntPtr/UIntPtr returns now set ResultKind 'ptr' like the
      P/Invoke path, and IntPtr::op_Explicit ('(IntPtr)long') maps to
      il2cpp_intptr_ctor instead of an undefined generated-name call --
      probe byte-exact, winforms1/pinvoke suite/asynccase PASS, unit 1804.
    - `73f9389` (batch 177b): **delegateext BUG-2/BUG-3 (net48 probes:
      heterogeneous Combine Func<int,int>+Action<int> -> System.ArgumentException,
      same-type succeeds; Delegate.GetHashCode is GetType()-derived -- same
      method+target -> same hash, stable across GC, target NOT part of the
      hash)** -- BUG-2: every delegate carries its type ID (il2cpp_delegate_new's
      new typeId arg = FNV-1a of the full type name, stamped by the transpiler
      at DelegateNewExpr and preserved through all optimizer rewrites);
      icall_delegate_combine compares operands' ids (containers store their
      validated common id, O(1)) with net48's null-first order and raises
      ArgumentException on differing non-zero ids; 0 = untracked creation path
      (BCL ctor forms) skips conservatively. BUG-3: new
      icall_delegate_get_hash_code = FNV-1a over the flattened leaf methodPtr
      list (GC-stable, Equal-consistent); Object.GetHashCode dispatches
      delegates through the new valuesemantics branch. test_delegateext
      section 18: 153->173 assertions; runtime 139/139, transpiler 0 errors.
    - `399865a` (batch 177c): **DelegateSupportTests sync to batch-177b 3-arg
      il2cpp_delegate_new emission** (methodPtr, target, typeId) -- the
      heterogeneous-Combine guard added the type-id argument; the instance-
      bound and static-bound newobj assertions matched the old 2-arg shape --
      updated to the [0-9]+ULL type-id tail, unit 1806/1808 (2 pre-existing
      TimeExt2/IntPtr).
    - `63b5ab8` (batch 178): **COM stage 10 IEnumVARIANT enumerator** --
      per-CCW enumeration registry (icall_com_ccw_enum_register_item, value-
      copy VARIANT subset VT_EMPTY/VT_NULL/VT_I4/VT_R8, kCcwMaxEnumItems=8,
      E_INVALIDARG for VT_BSTR, E_OUTOFMEMORY at capacity); Il2CppComEnumView
      7-slot vtable (Next/Skip/Reset/Clone: S_FALSE still writes pceltFetched,
      Clone heap view with shared refcount + dedicated release path, main view
      rides CcwReleaseImpl); QI dispatch outside the Windows gate (every-
      platform); OOB guard learns the 7-slot enum vtable; RCW direction drives
      the same view through icall_com_call_vtable_slot with zero new icalls.
      test_com section 12 (QI identity/OOB/registry/capacity/Next exhaust
      partial/Skip/Reset/Clone cursor independence/RCW direction/teardown
      refcounts/POSIX): 301 assertions. runtime 139/139.
      (Test crash 0xC0000005 during dev was a TEST logic bug -- stack buffer
      overflow from Next(8) into a 64B buffer after capacity pollution, not an
      implementation defect; capacity test moved to its own CCW, discover 193.)
    - `77d1d42` (batch 178b): **WinForms Phase 23 PictureBox** -- STATIC
      SS_BITMAP + LoadImageW (OBM_CLOSE ordinal, LR_SHARED) + STM_SETIMAGE/
      STM_GETIMAGE HBITMAP round-trip (set returns previous, get returns the
      set handle), GWL_STYLE style-bit readback, GetParent ownership -- zero
      new shim/struct (user32 only), net48 byte-exact. (Two transpiler blocks
      adapted without touching src/: property type encoded as long since refl3
      rejects ELEMENT_TYPE_I 0x18, instance field stays IntPtr since M2 field
      decoder rejects long 0x0A -- property long + field IntPtr, discover
      190/191.)
    - `3f79a12` (batch 179): **WinForms Phase 24 NotifyIcon** -- shell32
      Shell_NotifyIconW NIM_ADD/MODIFY/DELETE with the x64 NOTIFYICONDATAW
      (pinned int[244] = 976B MEASURED, NOT the audited 984B: cbSize/hWnd/uID/
      uFlags/uCallbackMessage/hIcon + NIF_STATE/NIS_HIDDEN modify),
      LoadIconW(IDI_APPLICATION) icon, callback message P24_CB_MSG self-posted
      to the WndProc (count=1) -- new shell32 shim (comctl32 pattern), zero
      transpiler gaps, net48 byte-exact. (Layout must be probe-verified, not
      trusted from the audit draft -- sizeof 976 vs claimed 984, discover 200.)
    - `c79ef30` (batch 180): **WinForms Phase 25 file dialog** -- comdlg32
      GetOpenFileNameW with the x64 OPENFILENAMEW (pinned int[38] = 152B,
      offsetof-probed: lStructSize@0=152/hwndOwner@8/lpstrFilter@24/
      lpstrFile@48/nMaxFile@56=128/lpstrTitle@88/Flags@96), worker-thread
      modal + title-matched FindWindowW(#32770, Wf25Open) + GetDlgItem(IDOK)
      + synchronous SendMessageW(BM_CLICK) close, lpstrFile pre-filled via
      GetModuleFileNameW (empty name fails the OK click, probed) -- new
      comdlg32 shim (comctl32 pattern), net48 byte-exact. Measured deviations
      from the audit design (net48 oracle, probe-verified): hwndOwner=NULL (a
      hidden owner suppresses the dialog entirely, shown=0), lpstrTitle@88
      (NOT @72 = nMaxFileTitle), Flags=0 (OFN_FILEMUSTEXIST|OFN_PATHMUSTEXIST
      pop an existence-error box -- same #32770+title -- that deadlocks the
      synchronous BM_CLICK), suffix derived from the returned path string (the
      nFileExtension writeback stays 0 when the dialog returns the unchanged
      pre-filled name).
    - `035ef71` (batch 180b): **unit pre-existing failures (stale test
      assertions) -- unit 1808/1808 FIRST-EVER ALL-GREEN** -- 2 tests asserted
      the OLD pre-batch-151b/155b behavior: (1) Resolve_DateTime_GetTicks_
      ThrowsLoud expected a loud rejection, but get_Ticks was wired in the
      PRIMARY DateTime domain (MethodMap.DateTime.cs:38 -> icall_datetime_
      get_ticks, runtime time.cpp:339) so it maps and never reaches
      DateTimeExtra; replaced with Resolve_DateTime_GetTicks_MapsToTicksIcall
      pinning the mapping (ResultKind l, deref:dt) -- (2) Resolve_IntPtr_
      ToInt64_AddressReceiver_DerefsInt32Slot expected deref:i, but batch
      155b's 'ToInt64 deref width fix (int64)' correctly derefs the full-width
      intptr_t slot (deref:l; deref:i truncates x64 pointers); renamed
      DerefsFullWidthSlot and asserted deref:l -- both implementation states
      verified correct, tests were stale (zero src changes).
    - `24da086` (batch 181): **COM stage 11 IEnumVARIANT BSTR items** --
      icall_com_ccw_enum_register_bstr_item takes ownership of a
      SysAllocStringLen BSTR (oleaut32 resolved at runtime, fn-null ->
      NotSupportedException, kept OUT of GetOle32().ok), Next hands the caller
      a FRESH SysAllocStringLen copy (the OLE contract -- caller VariantClear's
      returned elements, the stored master survives), CCW teardown
      SysFreeString's every owned BSTR (no double-free, verified), shared
      kCcwMaxEnumItems capacity, ArgumentNullException/E_OUTOFMEMORY guards,
      POSIX E_NOTIMPL (BSTR is Windows-only -- the stage-10 every-platform
      claim carved out); the raw register_item still rejects VT_BSTR
      (E_INVALIDARG pin). test_com section 13 (23 assertions), runtime 139/139,
      POSIX (WSL Ubuntu2404 g++) dual-TU compile 0 error/0 warning,
      301->324 assertions.
    - `c8507df` (batch 182): **WinForms Phase 26 ColorDialog** -- comdlg32
      ChooseColorW with the x64 CHOOSECOLORW (pinned int[18]: lStructSize=72/
      hwndOwner=NULL/rgbResult preset 0xFF/lpCustColors pinned int[16]/
      Flags=CC_RGBINIT), worker-thread modal + thread-id-filtered
      FindWindowExW(#32770 walk, GetWindowThreadProcessId == worker tid -- the
      localized title is unsearchable) + GetDlgItem(IDOK) + synchronous
      SendMessageW(BM_CLICK), rgbResult readback == preset -- reuses the
      Phase-25 worker pattern and the existing comdlg32 shim, net48 byte-exact.
      (Title is a localized system string and cannot be set -- Phase 25's
      title-match FindWindowW approach fails; thread-id filtering via
      GetWindowThreadProcessId is the deterministic alternative, discover
      214.)
  - WinForms suite line counts (THIS turn, [BASH:Measure-Object]):
    `tests/cases/winforms1/expected.txt` = 355 lines, `actual.txt` = 355 lines
    (Phase 26 after; net48 vs transpiled C++ byte-exact, Compare-Object
    DIFF=0 IDENTICAL).
  - Runtime suite count (THIS turn, [BASH:Measure-Object]): 139
    `tests/runtime/test_*.cpp`.
  - M-stage gate documents (this turn):
    - `docs/audit/e2e-full-matrix7.md` [READ:lines 1-40]: matrix7 100% gate at
      HEAD cef2bdd -- **196-case matrix = 195 correctness + 1 bench (virtbench,
      checksum-identical timing, not a real FAIL)**; 250 total case dirs, 246
      PASS / 4 FAIL / 0 MISSING with the 4 FAILs all OUTSIDE the 196 口径
      (decfmt2/resmgmt/resops known gaps, non-regression).
    - Full-matrix 413-case parallel re-sweep (batch 175, prior wave):
      **413 cases / 104 batches 100% PASS** -- completed per plan.md [READ:
      .opencode/plan.md line 6 "[x] 全矩阵 413 案例 104 批次并行复扫：100% PASS"]
      and memory.md [READ:.opencode/memory.md "全量复扫已完成（413 案例）"].
    - `docs/audit/bench-verify-m7-20260815.md` (committed at 96d6880) + docs
      commit `c8e320c` (in the git log THIS turn): **13/13 zero-regression
      confirmed** vs baseline efa6741 (batch 168) -- bench verify re-confirmed
      after the BUG-1 fix (fib 1.53/string 1.79/array 14/sort 1.19/str_chars
      17/str_concat 1.39 medians of 3; single-run dips sort 0.61/arr_copy 0.91
      classified noise by run-3 recovery).
    - Unit counts: **unit 1808/1808** (first-ever all-green, batch 180b
      `035ef71` commit message [BASH:git log]; 2 stale-test fixes, zero src
      changes); **runtime 139/139** re-measured [BASH:Measure-Object 139
      test_*.cpp]; test_com assertions 324 (batch 181, +23 section 13) /
      test_delegateext 173 (batch 177b, +20 section 18).
- M-stage gate status:
  - **M5** BCL full translation: matrix7 **196/196** confirmed
    (e2e-full-matrix7.md: 195 correctness + 1 bench = 100%). **Full-matrix
    413-case / 104-batch parallel re-sweep 100% PASS** (completed, prior
    wave, plan.md line 6); no M5-relevant case regressed in batches 176-182
    (all winforms/com/delegate surface, e2e byte-exact per batch).
  - **M6** Transpiler: unit **1808/1808** (first-ever all-green; 2 stale-test
    fixes in batch 180b, zero src changes) + runtime **139/139** (139
    test_*.cpp [BASH:Measure-Object]) + transpiler test surface (21 surfaces,
    carried from v9); batch 177c synced DelegateSupportTests to the 3-arg
    il2cpp_delegate_new shape; 177b transpiler changes produced 0 errors.
  - **M7** Performance: **zero-regression confirmed 13/13** vs baseline
    (docs/audit/bench-verify-m7-20260815.md at 96d6880, re-confirmed at
    `c8e320c` in the git log THIS turn); no bench-affecting change landed in
    batches 176-182 (all winforms/com/delegate surface).
  - **M8** COM interop: stages 1-11 done (**stage 9 multi-sink broadcast
    batch 176b, stage 10 IEnumVARIANT batch 178, stage 11 BSTR items batch
    181**); WinForms 26 phases done (**Phase 22 TreeView / 23 PictureBox /
    24 NotifyIcon / 25 file dialog / 26 ColorDialog**, batches 176/178b/179/
    180/182); S2 remaining: macOS/OpenBSD real-machine verification (external
    hardware).
- Remaining / next batch pool (task order):
  - COM stage 12 (**IStream**).
  - WinForms Phase 27+.
  - #114 original crash repro (needs user case).
  - M8 S2 macOS/OpenBSD real-machine (external hardware dependency).
