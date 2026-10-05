# GAPS.md — stackops2 unwired surface (e2e batch C68, 2026-08-12)

The case probes ONLY the wired Stack/Queue surface (38 lines PASS vs net48,
dual-compiler mingw+msvc). Gaps below were verified before being excluded.

## D-STK-1 — Stack/Queue enumerators: runtime FROZEN snapshot vs net48 VERSION-check

- **Phenomenon (empirically verified against the net48 oracle)**: net48's
  non-generic Stack/Queue enumerators are VERSION-checked -- mutating the
  container after `GetEnumerator()` throws
  `System.InvalidOperationException` ("Collection was modified...") on the
  next `MoveNext`. The runtime enumerator (C42 sqenum-b23, stackqueue.cpp
  Il2CppStackQueueEnumerator) has NO version field (items/head/count/index
  only) and instead FREEZES the container's items array + count at
  GetEnumerator time, then walks the frozen state silently.
- **Causal chain**: the runtime snapshot design was chosen for the ToArray
  order (sqenum-b23); a mutation after GetEnumerator is therefore a silent
  snapshot walk instead of net48's throw. The stackops2 S1 probes were
  written against the frozen-snapshot assumption, the oracle ABORTED (exit
  -532462766) proving the net48 throw -- the probes were removed and the
  divergence recorded (no probe mutates between GetEnumerator and the walk).
- **Suspected root file**: stackqueue.cpp il2cpp_stack_get_enumerator /
  il2cpp_queue_get_enumerator + Il2CppStackQueueEnumerator (stackqueue.h) --
  would need a version int + a container version bump on every mutator to
  match net48.

## D-STK-2 — Stack/Queue Clone / Synchronized / SyncRoot / Contains unwired

- **Phenomenon**: `Stack.Clone`, `Queue.Clone`, `Synchronized`, `SyncRoot`,
  `Contains`, the capacity ctors (`Stack(int)` / `Queue(int)` /
  `Stack(ICollection)` / `Queue(ICollection)`) have NO MethodMap arm
  (MethodMap.Stack.cs / MethodMap.Queue.cs map only ctor()/Push|Enqueue/
  Pop|Dequeue/Peek/get_Count/Clear/ToArray/GetEnumerator; any capacity ctor
  throws NotSupportedException LOUD). stackops2 therefore builds "capacity"
  coverage through observable growth (doubling, ring wrap, re-layout) rather
  than the unwired capacity ctors.
- **Suspected root file**: MethodMap.Stack.cs / MethodMap.Queue.cs.
