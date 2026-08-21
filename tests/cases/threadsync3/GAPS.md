# GAPS.md -- threadsync3 defects (e2e batch C75, 2026-08-13)

Case status: **PASS** (net48 oracle 15 lines, exit 0; dual-compiler PASS via
run-tests.ps1 -Compiler mingw/msvc; determinism re-verified with 3 consecutive
mingw runs). Active probes cover the WIRED Monitor surface (MethodMap.Monitor.cs
+ monitor.cpp side table): Wait timeout faces (0 / 100 / -1 infinite woken by
Pulse / -2 AORE / unowned -2 AORE-before-ownership / unowned 0 and 100 SyncLock),
no-waiter Pulse/PulseAll no-ops + the lost-pulse Wait(gate,0), a THREE-waiter
PulseAll round-trip (all woken, monitor free afterwards), and recursion-depth
restoration across an infinite Wait. Gaps below are RECORDED, NOT FIXED (this
batch creates test cases only).

## D-MON-1 -- single Pulse with TWO+ waiters DIVERGES (net48 FIFO vs runtime broadcast)

- **Phenomenon**: the runtime's SIMPLIFIED pulse wakes ALL waiters (monitor.h
  / monitor.cpp: il2cpp_monitor_pulse bumps a generation every polling waiter
  observes), while net48 Pulse wakes a single FIFO waiter. With ONE waiter
  both wake exactly one (byte-exact -- already probed in monitordeep/
  threadsync2); with TWO+ waiters net48 wakes 1 and the runtime wakes N -- a
  recorded divergence. **This case probes ONLY PulseAll with multiple
  waiters** (net48 PulseAll ALSO wakes all N, so the wake-all count is
  byte-exact), and uses the airtight handshake (main spins on a ready counter,
  then acquires the gate -- the acquisition PROVES every worker is registered
  inside Wait, so the PulseAll cannot miss a waiter on either runtime).
- **Suspected root file**: `src/runtime/src/monitor.cpp` (il2cpp_monitor_pulse
  / the wait loop -- a FIFO waiter queue would be needed to match net48 Pulse).

## D-MON-2 -- unwired Monitor overloads (LOUD at transpile, not probed)

- **Phenomenon**: `Monitor.TryEnter(object, int timeout)`,
  `Monitor.Wait(object)`, `Monitor.Wait(object, TimeSpan)` and
  `Monitor.Wait(object, int, bool exitContext)` throw NotSupportedException at
  transpile (MethodMap.Monitor.cs). None are used here; the wired int-form
  surface drives every probe. Note the runtime monitor.h DOES declare
  il2cpp_monitor_try_enter(obj, timeoutMs) -- the transpiler mapping is the
  missing half.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Monitor.cs` (add the
  timeout overload cases).

## (cross-ref) D-MON-3 -- SyncLock exception chain fix already landed (batch 126)

- monitordeep's D-MON-3 (wrong SyncLock type-name chain) was FIXED in batch
  126 (commit 60a009f); this case's typed `catch (SynchronizationLockException)`
  probes (unowned Wait 0/100) therefore match directly -- no catch widening
  needed here, unlike dictionary4's D-DICT4-2 KeyNotFound chain (still open).
