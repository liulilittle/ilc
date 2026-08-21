# Roadmap Status v9 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-15 (snapshot final: batches 170-175 progress; supersedes snapshot
  v8; v8 kept at `docs/roadmap-status-v8.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  170-175 progress (**StringBuilder.Insert overload family fix + WinForms Phase
  20 TrackBar + COM stage 8 IDispatch event-sink VARIANT fire + BUG-1 void-N-int
  multicast delegate crash fix (batch 172, in worktree uncommitted) + WinForms
  Phase 21 ListView with the comctl32 v5.82 iItem probe + full-matrix 413-case
  parallel re-sweep in flight**), and the next batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `c188b7c0512a8d3753942087f7e5bc2d88a4e515`
    (branch m1; commit chain new->old): `c188b7c` (batch 173) -> `820658f`
    (batch 171) -> `a9aadeb` (batch 170) -> `c798e6f` (batch 170b) -> `a4fa68d`
    (docs v8) -> `96d6880` (docs bench verify M7) -> `f6d98f6` (batch 169b) ->
    `cd6352f` (batch 169) -> `885251a` (batch 169d) -> `6e1511a` (batch 169c) ->
    `efa6741` (batch 168).
  - `git status --short` -> [BASH] worktree has **batch 172 UNCOMMITTED**:
    `M src/il2cppc/Emit/CodeGenerator.cs` (+250/-23), `M src/runtime/include/
    internal/il2cpp_delegate_ext.h` (+5), `M src/runtime/src/delegateext.cpp`
    (+29), and **batch 174 in progress**: `M tests/runtime/test_delegateext.cpp`;
    plus ~23 untracked `tests/cases/*/actual.txt` + `expected.txt` artifacts
    (boxconv/boxdeep/boxdeep2/boxops/concatcase/delegatechain/delegops/gccase/
    gchandledeep/gcroot/gcstress/internconc/internops/multicast2/strdeep/
    stringbuilddeep/stringext/winevent/winformsext) written by the in-flight
    full-matrix re-sweep (batch 175) -- NOT sources, regenerated e2e outputs.
  - Batch commit messages (full subjects, [BASH:git log]):
    - `a9aadeb` (batch 170): **StringBuilder.Insert overload family fix** -- 3
      real bugs found by the #114 deep-dive: numeric overloads emitted raw
      int64/double as Il2CppObject* (uncompilable C++), char[] silently read
      the array header as a string (data corruption), 3-arg (index,string,count)
      lost the receiver. Now gated per ps[0] type like the Append case (numeric
      -> ToString-then-insert / per-type insert icalls, char[] loud, unmapped
      loud NotSupportedException). net48 byte-exact. Probe case `sbinsertcase`:
      20 Insert probe blocks -> 40-line expected.txt [READ:sbinsertcase.cs
      probes a..t, [BASH:expected.txt 40 lines]; expected committed with the
      fix [BASH:git log -- sbinsertcase -> a9aadeb].
    - `c798e6f` (batch 170b): **WinForms Phase 20 TrackBar** -- comctl32
      msctls_trackbar32 range 0..100 (TBM_SETRANGE/GETRANGEMIN/MAX), SETPOS/
      GETPOS round-trip, clamp (200->100, -5->0), TBM_SETTICFREQ(10)->
      GETNUMTICS, WM_HSCROLL injection (thumb track/position) + status-bar
      linkage `Track: <pos>`, net48 byte-exact.
    - `820658f` (batch 171): **COM stage 8** -- `icall_com_ccw_fire_dispatch_
      event(ccw, iid, dispId, pDispParams, pVarResult)` delivers an event to an
      Advise'd IDispatch-shaped automation sink via IDispatch::Invoke (vtable
      slot 6) with DISPATCH_METHOD and the caller's DISPPARAMS/VARIANT carriers
      (WinForms AxHost automation-sink shape, DISPID_FIRST+n); reuses stage-7
      connection-point sink storage (view->sink); audit proved idispatch_invoke
      cannot cover the path; S_FALSE when no CP/no connected sink, no-AddRef
      observation-only, sink authoritative for dispId/VARIANT validation,
      ArgumentNullException guards, POSIX E_NOTIMPL stub; oaidl DISPID_FIRST=2
      + DISPATCH_METHOD=1 constants. test_com 11b section (7-slot
      TestDispatchSink: DISPID_FIRST/+1 delivery, VT_I4 + two-arg reverse-order
      DISPPARAMS byte-exact passthrough, pVarResult forwarding, IID routing
      isolation, Unadvise->S_FALSE, null guards, POSIX stub).
    - `c188b7c` (batch 173): **WinForms Phase 21 ListView** -- SysListView32
      with EXPLICIT-index inserts: **comctl32 v5.82 LVM_INSERTITEMW rejects
      iItem=-1 append (probed, returns -1; v6-only semantic), so Insert uses
      the current count as iItem**; LVITEMW via GCHandle-pinned int[22] local
      layout; GETITEMCOUNT/GETITEMTEXTW/SETITEMTEXTW/SETITEMCOUNT/SETITEMSTATE/
      GETITEMSTATE round-trips; WM_NOTIFY LVN_ITEMCHANGED -> SelectionChanged
      re-reading real selection -- net48 byte-exact. winforms1 expected now
      324 lines [BASH:Measure-Object].
  - **Batch 172 -- BUG-1 void-N-int multicast delegate crash fix (UNCOMMITTED,
    in worktree)**: `git diff src/il2cppc/Emit/CodeGenerator.cs` [BASH] shows
    the i4 multicast route now ALSO accepts a VOID-returning delegate with
    <=4 all-int32 params (Action<int>, Action<int,int>, custom void M(int), ...)
    -- previously those decoded to retCpp "void" and fell through to the raw
    methodPtr dispatch, which reads a multicast container's `first` field as
    code -> 0xC0000005; plus a new `icall_multicast_invoke_r_void` for the
    one-managed-ref-arg void ABI (Action<string>/Action<object>), and every
    other void shape is LOUDLY rejected (NotSupportedException) instead of a
    raw methodPtr call. **delegops regression verified PASS**: actual.txt (28
    lines) vs expected.txt (28 lines) -> [BASH:Compare-Object] diff count 0.
  - **Batch 174 in progress (UNCOMMITTED)**: delegateext rr_void deep-coverage
    re-test -- `M tests/runtime/test_delegateext.cpp` [BASH:git status];
    test_delegateext.cpp exists (commit 720c83b originally added runtime rr_void
    multicast coverage).
  - **Batch 175 in progress**: full-matrix e2e parallel re-sweep -- 100+
    parallel agents over 104 batches x 413 cases (batch-pool scope; measured on
    disk: 414 case dirs with a .csproj / 415 total dirs [BASH:Measure-Object],
    runtime test count 139 test_*.cpp [BASH:Measure-Object]); untracked
    actual/expected pairs already written to ~23 case dirs [BASH:git status],
    delegops among the swept PASS.
  - M-stage gate documents (this turn):
    - `docs/audit/e2e-full-matrix7.md` [READ:lines 1-40]: matrix7 100% gate at
      HEAD cef2bdd -- **196-case matrix = 195 correctness + 1 bench (virtbench,
      checksum-identical timing, not a real FAIL)**.
    - `docs/audit/bench-verify-m7-20260815.md` [READ:lines 1-35]: **13/13
      zero-regression** vs baseline at HEAD efa6741 (batch 168) -- tests/bench
      medians fib 1.40 / string 1.65 / array 12.0 / object inf / sort 1.14 /
      hash 1.00 + bench-array; arr_copy 1.08 classified noise, not regression.
    - Unit counts carried from v8 (no tests/unit commits since batch 169d
      [BASH:git log -- tests/unit -> 885251a latest]): **unit 1804/1806
      (2 pre-existing: TimeExt2 GetTicks loud, IntPtr ToInt64 derefs-int32)**;
      runtime 139/139 re-measured [BASH:Measure-Object].
- M-stage gate status:
  - **M5** BCL full translation: matrix7 196/196 confirmed (e2e-full-matrix7.md:
    195 correctness + 1 bench = 100%). **Full-matrix 413-case parallel re-sweep
    IN PROGRESS (batch 175)** -- delegops already swept byte-exact 28/28.
  - **M6** Transpiler: unit **1804/1806** (2 pre-existing, carried from v8) +
    runtime **139/139** (139 test_*.cpp [BASH:Measure-Object]) + transpiler test
    surface (21 surfaces, carried from v8); batch 174 extending
    test_delegateext rr_void deep coverage IN PROGRESS (worktree-uncommitted).
  - **M7** Performance: **zero-regression confirmed 13/13** vs baseline
    (docs/audit/bench-verify-m7-20260815.md, committed at 96d6880); arr_copy
    1.08 edge classified noise not regression; no bench-affecting change landed
    in batches 170-173 (all stringbuilder/winforms/com/delegate surface).
  - **M8** COM interop: stages 1-8 done (**stage 8 IDispatch event-sink VARIANT
    fire landed** in batch 171); S2 remaining: macOS/OpenBSD real-machine
    verification (external hardware).
- Remaining / next batch pool (task order):
  - Batch 172 commit: BUG-1 void-N-int multicast fix verified (delegops 28/28)
    but **still uncommitted** in the worktree.
  - Batch 174: finish delegateext rr_void deep-coverage re-test.
  - Batch 175: full-matrix 413-case re-sweep (in flight).
  - COM stage 9+ (post-8 dispatch surface) / WinForms Phase 22+ (**TreeView /
    NotifyIcon / comdlg32 need new shims**).
  - #114 original crash repro (needs user case).
  - M8 S2 macOS/OpenBSD real-machine.
