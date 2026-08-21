# GAPS.md — bgthread unwired/divering surface (e2e batch C19, 2026-08-10)

Case status: **PASS 12/12 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; compiler=mingw). The active surface drives the post-guard
Thread background surface: fg/bg flag roundtrips on unstarted / current /
RUNNING threads, the STOPPED setter guard (ThreadStateException after Join,
exact type + base catch), and the sibling guards (Join on an unstarted thread,
Start twice). No product code modified; no commit made.
(compiler=msvc unverifiable on this machine -- no VS2022; compiler=wsl
BLOCKED by the C18 report's shared-runtime build defect D-C18-WSL-1.)

## D-BG-1 — the IsBackground GETTER on a STOPPED thread also throws on net48

- **Phenomenon (recorded, probe ABSENT)**: net48's `Thread.IsBackground`
  GETTER on a thread whose callback has FINISHED throws ThreadStateException
  ("线程已终止，无法访问其状态。" -- localized) -- not just the setter. The
  ILC getter (threadobj.cpp icall_threadobj_get_is_background:322-326) has NO
  state guard and returns the stored flag. A "flag unchanged after the failed
  STOPPED set" probe therefore DIVERGES: net48 throws on the read, ILC prints
  the old flag. The flag-preservation semantics (the setter throws BEFORE the
  mutation on both -- threadobj.cpp:351 throw precedes :354 store) are pinned
  here; the read side is not probeable until the ILC getter gains the guard.
- **net48 oracle (csc-probe-verified)**: after `t.Join()`, `t.IsBackground`
  (getter) -> ThreadStateException "Thread is dead; state cannot be accessed."
- **Suspected root file**: `src/runtime/src/threadobj.cpp:322`
  icall_threadobj_get_is_background (add the same finished-handle guard as the
  setter at :342-355).

## D-BG-2 — catch handler STORING an ex-member read to a local fails the fold

- **Phenomenon (recorded, worked around in the case)**: a catch handler that
  contains TWO statements where one STORES an `ex.<member>` read
  (`ex.GetType().Name` / `ex.Message`) into a local fails TRANSPILE with
  "callvirt ... stack empty when popping this -- likely a catch-handler
  variable binding" (StackFold.cs:1977). Empirically isolated (minimal
  reproductions, ThreadStateException + `t.IsBackground` in the try):
    * `{ flag = true; }` (one plain store) -> OK
    * `{ s = ex.GetType().Name; }` (one ex-read store) -> OK
    * `{ flag = true; s = ex.GetType().Name; }` -> FAIL (any statement order)
    * `{ s = "lit"; s2 = ex.Message; }` -> FAIL
    * `{ flag = true; Console.WriteLine(ex.Message); }` (ex-read flows into a
      CALL) -> OK
  So the fragile shape is specifically the ex-member read whose RESULT feeds a
  LOCAL STORE when the handler has a second statement. exops2's handlers pass
  because every ex-read there flows into a Console.WriteLine/Concat call.
- **Workaround used**: the `stopped_bg_type` probe emits the type into
  Console.WriteLine directly (`Console.WriteLine("stopped_bg_type=" +
  ex.GetType().Name)`) inside the same handler -- proven-good shape; the flag
  is printed from the OUTSIDE.
- **Suspected root file**: `src/il2cppc/Ir/StackFold.cs` (the callvirt
  handler-store path around :1977 / the get_Message "drop"-arg kind);
  reproduce with the minimal case before fixing.

## Notes

- net48 oracle evidence: `out/bgthread.exe` exit=0, 12 lines byte-exact (all
  ASCII True/False flags + the type name; ThreadStateException's localized
  message is never printed). Harness diff PASS (mingw), 1/1.
- Determinism: the RUNNING-state set is gated on a single-writer child-set
  static flag (D1) + a bounded poll (Sleep(10) x1000); the child then sleeps
  200 ms so the set lands while the thread is RUNNING. Every probe is a
  synchronous flag/type read -- no time-dependent text.
