# GAPS.md -- linqzip defects (e2e batch C64, 2026-08-12)

Case status: **FORWARD GREEN GATE -- NOT PASSING at HEAD 4c2dd91** (transpile
aborts; see D-LINQZ-1). The net48 oracle (expected.txt, 62 lines, exit 0) is
frozen and documents the INTENDED semantics of Zip/Concat/Distinct/Except.
Per the batch iron rule (缺陷记录不修 / record defects, DO NOT fix), the gaps
below are recorded, not wired.

## D-LINQZ-1 -- Zip/Concat/Distinct/Except have NO BCL mapping (transpile abort)

- **Phenomenon**: ANY generic instantiation of the four operators aborts the
  transpiler. Probe-verified this batch (both abort at the same line):
  - `System.Linq.Enumerable.Zip` -> `il2cppc error: generic instantiation of
    System.Linq.Enumerable.Zip has no BCL mapping in M5`
  - `System.Linq.Enumerable.Distinct` -> `il2cppc error: generic instantiation
    of System.Linq.Enumerable.Distinct has no BCL mapping in M5`
  - both at `src/il2cppc/Emit/MethodRefResolver.cs:296`
- **Causal chain**: `TryMapLinq` (MethodMap.Linq.cs) has cases only for
  ToList/Count/Any/All/Sum/Min/Max/Where/Select/ToArray/First/FirstOrDefault/
  Take/Skip/OrderBy/GroupBy; every other name falls through to
  `default: return null` (MethodMap.Linq.cs:246-247). A MethodSpec generic
  instantiation that resolves to null then hits the M5 gate
  `MethodRefResolver.cs:296` (`generic instantiation of {owner}.{name} has no
  BCL mapping in M5`), which throws -- a hard transpile abort, NOT the
  generated-name fallback.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Linq.cs` (add the four
  cases) + `src/runtime/src/linq_map.cpp` (add set-family runtime entries --
  zip_pair_i4 / concat_i4 / distinct_i4 / except_i4). Future batch work.

## D-LINQZ-2 -- (informational) net48 Zip selector-evaluation count

- The net48 `Zip` evaluates the selector exactly once per output element
  (result length = min of the two operand lengths); this case's expected.txt
  is insensitive to evaluation-count (all probes compare VALUES), so no
  divergence risk if the future runtime evaluates per-element.
