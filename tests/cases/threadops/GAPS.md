# GAPS.md — threadops unwired surface (2026-08-07)

The case probes ONLY the wired surface (19 lines PASS vs net48, mingw:
`pwsh scripts/run-tests.ps1 -Compiler mingw -Case threadops`). Everything below
is deliberately NOT called from `threadops.cs` — each entry is a verified
transpiler/runtime divergence or a net48-hanging probe.

## D-THR-1 — Thread.Name get/set: undefined generated externs (C++ link failure)

- **Phenomenon**: `t.Name = "worker"` / `t.Name` transpiles WITHOUT a LOUD error
  to generated-name externs
  `System_Threading_Thread_set_Name_2aaaaefe58539aeb(...)` /
  `System_Threading_Thread_get_Name_cbbdb15ee1624613(...)` that NO runtime
  header declares → cmake build fails with an undefined symbol.
- **Evidence**: minimal probe (`new Thread(static W) { Name = "worker" }`, temp
  dir, transpiled at HEAD) — generated `Probe_P.cpp` lines 7-8 call the two
  externs verbatim; `Select-String` over `src/runtime/include/internal/*.h`
  returns NO `System_Threading_Thread_{get,set}_Name` declaration.
- **Causal chain**: `TryMapThread` (MethodMap.Thread.cs) has no `get_Name` /
  `set_Name` case → default generated-name resolution. The runtime object model
  (threadobj.h) has no name icall and its `Il2CppThreadObject` struct has no
  name storage field at all (obj/callbackFn/handle/state/tid/isBackground only).
- **Suspected root files**: `src/il2cppc/Emit/MethodMap.Thread.cs` (add
  get_Name/set_Name) + `src/runtime/include/internal/threadobj.h` +
  `src/runtime/src/threadobj.cpp` (add a name field + icall_threadobj_{get,set}_name).
- **Impact**: the requested "Name" probe is the ONLY unwired Thread object member.

## N-THR-1 — Self-Join: net48 blocks forever, runtime throws (documented deviation)

- `cur.Join()` on the CURRENT thread: net48 .NET Framework **blocks forever**
  (the oracle run HANGS — observed live). The runtime model throws
  ThreadStateException("A thread cannot join itself.") instead
  (threadobj.cpp:228, threadobj.h:76-77 documented deviation). An e2e probe is
  therefore impossible — the net48 oracle would never terminate. The probe is
  excluded; Join on a never-started thread and Start-twice ARE probed (both
  throw ThreadStateException on net48 and the runtime).

## N-THR-2 — ManagedThreadId before Start: 0 until Start (documented deviation)

- The runtime returns 0 until Start for a spawned thread (threadobj.h:67);
  net48 assigns a sequential managed id at construction. A before-Start probe
  would diverge, so it is NOT asserted (only after-Start nonzero + != main id
  are probed).

## N-THR-3 — Priority setter ignored (pinned Normal, documented deviation)

- The runtime accepts and ignores the Priority setter; the getter always
  reports 2 = Normal (threadobj.h:72-76). net48 reflects a set value. Only the
  getter DEFAULT (CurrentThread.Priority == Normal) is asserted; a set-then-get
  roundtrip would diverge.
