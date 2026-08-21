# GAPS.md -- monitordeep defects (e2e batch C64, 2026-08-12)

Case status: **PASS** (net48 oracle 26 lines, exit 0; dual-compiler PASS via
run-tests.ps1). Active probes cover the WIRED Monitor surface (MethodMap.
Monitor.cs + monitor.cpp / monitor.h side table): 8-level nesting, two-gate
interleave, helper-call nesting, recursion 12, reentrancy state probe,
wait-timeout reacquire, single-waiter Pulse / PulseAll round-trips,
recursion-preservation across Wait, the SyncLock/ANE/Argument exceptions, and
manual Enter(object, ref bool) lock-lowering. Gaps below are RECORDED, NOT
FIXED (this batch creates test cases only).

## D-MON-1 -- multi-waiter Pulse / PulseAll DIVERGES (net48 FIFO vs runtime broadcast)

- **Phenomenon**: the runtime's SIMPLIFIED Pulse wakes ALL waiters
  (monitor.h / monitor.cpp: a Pulse bumps the generation that every polling
  waiter observes), while net48 wakes a SINGLE FIFO waiter. With ONE waiter
  both wake exactly one (byte-exact, probed here); with TWO+ waiters net48
  wakes 1 and the runtime wakes N -- a recorded divergence. This case uses
  ONLY single-waiter Pulse/PulseAll probes.
- **Suspected root file**: `src/runtime/src/monitor.cpp` (il2cpp_monitor_pulse
  / the wait loop -- would need a FIFO waiter queue to match net48).

## D-MON-2 -- unwired Monitor overloads (LOUD at transpile, not probed)

- **Phenomenon**: `Monitor.TryEnter(object, int timeout)`,
  `Monitor.Wait(object)`, `Monitor.Wait(object, TimeSpan)` and
  `Monitor.Wait(object, int, bool exitContext)` throw NotSupportedException at
  transpile (MethodMap.Monitor.cs -- no emitter mechanism for Timeout.Infinite
  / TimeSpan conversion / exitContext). None are probed in this case; the
  wired int-form surface is what drives every probe.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Monitor.cs` (add the
  overload cases) + the runtime `il2cpp_monitor_try_enter(obj, timeoutMs)` /
  `il2cpp_monitor_wait(obj, -1)` entries already exist in monitor.h.

## D-MON-3 -- SyncLock exception chain uses the WRONG type name (catch mismatch)

- **Phenomenon**: `ThrowSynchronizationLockException` (monitor.cpp:257-265)
  throws the chain `"System.SynchronizationLockException,System.SynchronizationLockException,System.SystemException,System.Object,System.Exception"`. The
  real .NET type is **`System.Threading.SynchronizationLockException`** (the
  `System.SynchronizationLockException` string is not a valid full name -- no
  such type). The transpiler's catch emission (OpenCatch, CodeGenerator.cs:
  1992) uses the METADATA full name `System.Threading.SynchronizationLockException`, which `il2cpp_exception_matches` NEVER matches against the
  runtime chain. PROBE-VERIFIED: a `catch (SynchronizationLockException)`
  around a third Exit leaves the exception UNHANDLED (the C++ catch guard
  `if (!matches(...)) throw;` rethrows), aborting the case.
- **Case adaptation**: the four SyncLock probes (4a/4b/4c + the fourth-Exit
  probe) catch **SystemException** instead -- the runtime chain contains
  `System.SystemException` and `System.Exception`, so the catch matches on
  the ILC side while net48's `catch (SystemException)` matches its real
  SynchronizationLockException (which derives from SystemException). The
  probes still assert that the unowned-monitor operation throws, byte-exact
  on both runtimes.
- **Suspected root file**: `src/runtime/src/monitor.cpp:260` (change the
  first two chain segments to `System.Threading.SynchronizationLockException`).
