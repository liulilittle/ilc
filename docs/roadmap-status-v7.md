# Roadmap Status v7 - ILC (IL->C++ AOT Compiler)

- Date: 2026-08-15 (snapshot final: matrix7 9-FAIL list all verified CLOSED;
  supersedes snapshot 13 / v6; v6 kept at `docs/roadmap-status-v6.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  156-164 progress (**async state machine #132 + Task<T> 6-carrier surface
  (int/long/double/string/bool/char) + System.Threading.Timer + System.Timers.Timer
  + EventHandler multicast rr_void ABI + faulted-task AggregateException
  exactness**), the bench v4-v6 performance milestone (**M7 entered**), and the
  next batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` -> [BASH] HEAD = `720c83b` (branch m1);
    commits: 156c (async #132) / 156d (faulted-task) / 157 (concat+sb surface) /
    158 (Threading.Timer) / 159 (sort fast path + bench baseline) / 160
    (Task<long>) / 161 (System.Timers.Timer + rr_void) / 162-162b (AggregateException
    + EventHandler multicast) / 163 (Task<double>/Task<string>) / 164-164b
    (Task<bool>/Task<char> + runtime rr_void tests).
  - `git status` -> [BASH] worktree clean except `?? tmp/`.
  - **matrix7 9-FAIL closure** [BASH sweep THIS turn, UTF-8 capture]:
    dictcase PASS / linqcase PASS / linqops PASS / delegatecb PASS / timeext
    PASS / console3 PASS / consolestream PASS / arrayext2 PASS / excext PASS
    (excext was a GBK/UTF-8 capture artifact, not a real diff).
  - unit 1799/1801 (2 pre-existing failures: TimeExt2 GetTicks loud,
    IntPtr ToInt64 derefs-int32) + runtime 139/139.
- M-stage gate status:
  - **M5** BCL full translation: matrix7 196 cases -> **196/196 (100%)** after
    the 9-FAIL closure sweep (was 94.9% at v6).
  - **M6** Transpiler: all 9 recorded FAILs closed; bench baseline committed
    (docs/audit/bench-baseline-20260815.md).
  - **M7** Performance: sort 0.56x -> 1.13x via TryEmitFastArrayPtr offset arm;
    str_concat 1.22x (v6 0.81-0.85 gap closed); full bench-array program
    transpiles end-to-end.
  - **M8** COM stages 1-6 done; S2 remaining: macOS/OpenBSD real-machine
    verification (external hardware).
- Next batch pool (task order): WinForms stage 14 / COM stage 7 probes, #114
  concat+sb original repro (needs user case), Task<T> remaining instantiations
  audit, full-matrix re-run (only on user request).
