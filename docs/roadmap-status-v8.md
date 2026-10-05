# Roadmap Status v8 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-15 (snapshot final: batches 168-170 progress; supersedes snapshot
  v7; v7 kept at `docs/roadmap-status-v7.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  168-170 progress (**DelegateSignature void-0-arg multicast regression fix +
  COM stage 7 IConnectionPoint event-source fire + WinForms Phase 19 GroupBox +
  asynccase small-integer carriers + M7 bench zero-regression confirmed 13/13 +
  #114 52-variant deep-dive + StringBuilder.Insert x2 bugs found (batch 170 in
  progress)**), and the next batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `96d6880` (branch m1; commit chain
    new->old): `96d6880` (docs: bench verify M7) -> `f6d98f6` (batch 169b) ->
    `cd6352f` (batch 169) -> `885251a` (batch 169d) -> `6e1511a` (batch 169c) ->
    `efa6741` (batch 168) -> `c966860` (batch 167) -> `e6a8a47` (batch 166) ->
    `daf8e94` (batch 165b) -> `ae27f6e` (batch 165).
  - `git status --short` -> [BASH] worktree clean except `?? tests/cases/
    winforms1/gen_swap/` + `?? tmp/` (untracked audit/dump dirs, not sources).
  - Batch commit messages (full subjects, [BASH:git log]):
    - `efa6741` (batch 168): **DelegateSignature void-0-arg multicast regression
      fix** — a void-returning ZERO-ARG delegate (ClickHandler, += multicast-
      capable) decoded to retCpp 'void' and skipped `icall_multicast_invoke_i4`;
      the raw methodPtr dispatch read a multicast container's first field as
      code -> AV at startup (winforms1 0xC0000005). Fix: zero-arg void delegates
      keep the int32 ABI (i4 multicast path, void return discarded); 2-ref-arg
      void EventHandler still routes rr_void. winforms1 278-line byte-exact
      (incl Phase 18 radio group), unit 1802/1804 (2 pre-existing).
    - `cd6352f` (batch 169): **COM stage 7** — `icall_com_ccw_fire_event`
      (IConnectionPoint event-source fire): invokes the Advise'd sink's
      event-interface method (vtable slot 3+methodIndex) with intptr carriers;
      S_FALSE for no-listener paths, sink HRESULT on delivery, no AddRef
      (observation only), OOB/null guards, POSIX E_NOTIMPL stub. test_com 13
      new assertions (routing isolation, arg passthrough, ref-count stability,
      dual-CCW isolation, unadvise).
    - `f6d98f6` (batch 169b): **WinForms Phase 19 GroupBox** — BS_GROUPBOX static
      frame + WS_GROUP group-start, managed WfGroupBox (Text property, GetParent
      ownership, subclassed WndProc container event surface with
      WF_GROUP_MSG->WF_GROUP_NOTIFY bubbling), SetGroupChecked scoped
      exclusivity — two radio groups prove intra-group exclusivity AND inter-
      group independence. 294-line byte-exact.
    - `6e1511a` (batch 169c): **asynccase small-integer carriers** —
      byte/sbyte/short/ushort/uint via int32 carrier + string[] via _s ref
      carrier (locked from the Task<T> audit), net48 byte-exact incl
      GetType().Name precision and uint >2^31 bit pattern.
    - `885251a` (batch 169d): **unit regression for the batch-168 fix** —
      winforms1 ClickHandler invokes must route through
      `icall_multicast_invoke_i4` (never a raw `(void(*)())` methodPtr call);
      eventcase EventHandler invokes must route `icall_multicast_invoke_rr_void`.
    - `96d6880` (docs: bench verify M7): **13/13 zero-regression gate** —
      fib 1.40 / string 1.65 / array 12 / object inf / sort 1.14 / str_chars 16
      / str_concat 1.29 (medians of 3); arr_copy 1.08 edge is noise (all runs
      >=1.0, no <1.0) not regression; batch 168 delegate fix has no bench impact.
  - `docs/audit/bench-verify-m7-20260815.md` [READ:lines 26-63]: full 13-item
    table (7 tests/bench + 6 tests/bench-array), all >=1.0, no <1.0 single-run,
    verdict **零回退确认** (12 PASS + arr_copy borderline noise, §4 analysis:
    no causal mechanism — batch 168 touched delegate codegen only).
  - `tmp/concat114x/` [BASH:Get-ChildItem] 52-variant deep-dive artifacts
    (b2..h8 probes + concat114x.cs + expected/actual pairs): **crash shape still
    not reproduced**; discovered **2 real StringBuilder.Insert bugs** (numeric
    overload uncompilable / char[] silent data corruption / 3-arg receiver
    lost) -> in batch 170 fix (NOT yet committed).
  - `tmp/taskaudit/REPORT.md` [READ:lines 9-17, 50-54]: Task<T> carrier audit —
    byte/sbyte/short/ushort/uint all PASS (int32 carrier), decimal/DateTime
    LOUD-reject (dual channel: instName `_valuetype` suffix + genArgs==0x11),
    string[]/custom class _s carrier. **Silent errors: zero**.
  - Concurrent verification (git log subjects + case dirs [BASH:Test-Path]):
    7 cases timercase/timerscase/eventcase/taskcase/sbappend/arroffset/asynccase
    + matrix7 9 cases dictcase/linqcase/linqops/delegatecb/timeext/console3/
    consolestream/arrayext2/excext all PASS (UTF-8 capture).
  - `git log --grep=170/Insert` [BASH] -> **no batch 170 commit exists**;
    StringBuilder.Insert fixes are in-progress, recorded as "修复中" not "已修复".
- M-stage gate status:
  - **M5** BCL full translation: matrix7 196/196 — the v7 9-FAIL list
    (dictcase/linqcase/linqops/delegatecb/timeext/console3/consolestream/
    arrayext2/excext) re-confirmed CLOSED; excext was a GBK/UTF-8 capture
    artifact (docs/audit/e2e-full-matrix7.md: 196-口径 195 正确性 + 1 bench).
  - **M6** Transpiler: unit **1804/1806** (2 pre-existing: TimeExt2 GetTicks
    loud, IntPtr ToInt64 derefs-int32) + runtime **139/139** (139 test_*.cpp
    [BASH:Measure-Object]) + 21 transpiler test surfaces; batch 169d unit
    regression locks the batch-168 routing ABI.
  - **M7** Performance: **zero-regression confirmed 13/13** vs baseline
    (docs/audit/bench-verify-m7-20260815.md, batch 168 delegate fix no bench
    impact); arr_copy 1.08 edge classified noise not regression.
  - **M8** COM interop: stages 1-7 done (**stage 7 IConnectionPoint fire
    landed** in batch 169); S2 remaining: macOS/OpenBSD real-machine
    verification (external hardware).
- Next batch pool (task order): COM stage 8 (IDispatch event-sink VARIANT fire
  variants) / WinForms Phase 20+ (TrackBar in progress; later ListView/TreeView/
  NotifyIcon/comdlg32 need new shims) / #114 original crash repro (needs user
  case) / StringBuilder.Insert fix verification (batch 170, in progress) /
  M8 S2 macOS/OpenBSD real-machine.
