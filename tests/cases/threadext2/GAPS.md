# GAPS.md -- threadext2 recorded findings (e2e batch C39, 2026-08-11)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 14-line
byte-exact). Active probes: 1-arg Monitor.TryEnter contention under a depth-2
holder, Join() post-work semantics, main+pool lock-protected interleave
(8*200+500 exact), pool nested-lock reentrancy, pool racing TryEnter
convergence (gate free after), PulseAll wake-all for two waiters.

## D-THR3-1 -- Thread.Join(int) timeout overload unwired (transpiler)

- **Phenomenon**: `Thread.Join(int32)` has no MethodMap case -- a probe would
  transpile to the generated-name extern `System_Threading_Thread_Join_<hash>`
  (a C++ link failure). Parked (the blocked probe stays a comment in
  threadext2.cs), like threadops2 parked Thread.Name (D-THR2-1).
- **Causal chain**: `MethodMap.Thread.cs:96` maps only `Join()` (count == 0);
  `return null` for count == 1 falls to the default generated-name path. The
  runtime threadobj has a `join(timeoutMs)`-style entry point pending the
  timeout-return shape (net48 returns bool).
- **Suspect**: `src/il2cppc/Emit/MethodMap.Thread.cs` (add a `Join(int)` case
  mapping to a runtime join-with-timeout icall).

## D-THR3-2 -- Monitor.TryEnter(obj, ref bool) flag arg lacks the bool* cast (transpiler)

- **Phenomenon**: `bool taken = false; Monitor.TryEnter(gate, ref taken);`
  fails the C++ build:
  `error: no matching function for call to 'il2cpp_monitor_try_enter'` --
  the generated call is `il2cpp_monitor_try_enter(ThreadExt2_sf_gate, (&loc1))`
  with `loc1` an `int32_t` (the ref-bool flag local), so `int32_t*` matches
  neither the 2-arg `(Il2CppObject*, bool*)` overload (monitor.h:94) nor the
  3-arg timeout form.
- **Causal chain**: the `lock` lowering's `Monitor.Enter(obj, ref lockTaken)`
  emits the flag as `reinterpret_cast<bool*>(&loc)` (that path compiles), but
  the 2-arg TryEnter case (`MethodMap.Monitor.cs:79`, comment "cast "" ->
  &lockTaken") emits the flag as a BARE `&loc` with no
  `reinterpret_cast<bool*>` -- an int32_t* mismatched against bool*. The
  MethodMap.Monitor.cs:80-81 timeout case is a separate LOUD guard and is not
  reached.
- **Worked around**: the case uses the 1-arg `Monitor.TryEnter(obj)` form
  (the proven conccases surface).
- **Suspect**: `src/il2cppc/Emit/MethodMap.Monitor.cs` TryEnter 2-arg case
  (must carry the same flag-cast emission as the Enter 2-arg case).
