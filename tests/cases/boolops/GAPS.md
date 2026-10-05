# GAPS.md — boolops recorded findings (e2e batch C81, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle byte-exact,
66 lines). Active probes cover the boolean-operation depth: && / || truth
tables over plain bool operands, short-circuit EXCEPTION avoidance
(`false && (100/0)` must not throw), short-circuit ordering with method-call
side effects, the ternary value-type matrix (int/string/double/bool arms),
nested ternary chains, negation, bool as field/param/return data, and the
&&-binds-tighter-than-|| precedence lattice. Gaps below are RECORDED, NOT
FIXED (this batch creates test cases only).

## D-BOOL-1 — bool[] newarr element type is not wired

- **Phenomenon**: `new bool[] { true, false }` aborts the transpile with
  `il2cppc error: newarr of value type System.Boolean not supported (audit
  C4)` (CodeGenerator.cs ArrayElementType: only int32/byte/char/int64/
  uint64/string/object elements allocate; `System.Boolean` falls through to
  the IsKnownBclValueType rejection).
- **Confirmed**: the probe was rewritten to store bool FLAGS in an int[] (0/1
  — the bool ABI in a supported element type) and compare `flags[i] == 1`.
  bool-as-DATA is still probed through fields / params / returns.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` ArrayElementType
  (no `System.Boolean` case).

## D-BOOL-2 — emitted C++ produces unused-label / self-assign warnings (cosmetic)

- **Phenomenon**: the transpiled C++ for short-circuit branch chains emits
  `unused label 'L_240'` and `t197 = t197; // store evaluated value` (mingw
  -Wall). These are warnings, not errors; the case builds and runs
  byte-identical. Cosmetic only — recorded for completeness.
- **Suspected root file**: CodeGenerator / StackFold branch-label emission
  (no correctness impact observed).
