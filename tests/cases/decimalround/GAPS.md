# GAPS.md -- decimalround recorded findings (e2e batch C79, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 69-line
byte-exact). Active probes cover the decimal ROUNDING depth: the ToEven
midpoint lattice at decimals=0 (banker's rounding, 1-arg/2-arg overloads),
the AwayFromZero midpoint lattice (3-arg), the decimals boundary (0..28
accepted, 29/-1 -> ArgumentOutOfRangeException), the mode boundary beyond
the valid pair (5/-1 -> ArgumentException on both sides), scale-boundary
rounding (1e-28 collapse, MaxValue/MinValue identity), multi-digit carry
propagation, and value-output round trips. Gaps below are RECORDED, NOT
FIXED (this batch creates test cases only).

## D-DR-1 -- net48 decimal.Round accepts ONLY modes 0/1; runtime maps 2/3/4

- **Phenomenon**: the net48 oracle REJECTS every MidpointRounding value
  outside 0 (ToEven) / 1 (AwayFromZero): `decimal.Round(2.7m, 0,
  (MidpointRounding)2/3/4)` all throw ArgumentException ("invalid
  MidpointRounding ... value") -- probe-verified on the reference runtime
  (%TEMP%\opencode\mrprobe, exit=0, m0=3 m1=3 m2..m4=ARG). The RUNTIME's
  RoundCore/InternalRound (decimal.cpp:1927-1971) instead maps mode 2 ->
  ToZero, 3 -> ToNegativeInfinity, 4 -> ToPositiveInfinity (the netcore
  MidpointRounding extension net48 never exposes through Round).
- **Confirmed**: probes of (MidpointRounding)2/3/4 removed -- they would
  throw on net48 and return a value on the runtime (guaranteed byte diff).
  The mode lattice stays on the valid pair 0/1; mode 5/-1 throw
  ArgumentException on BOTH sides and ARE probed (mb_5_throw /
  mb_neg1_throw). The runtime's ToZero/ToNegativeInfinity/ToPositiveInfinity
  rounding semantics are NOT reachable through any net48-compatible probe.
- **Suspected root file**: `src/runtime/src/decimal.cpp` RoundCore/InternalRound
  (netcore-mode extension vs net48's strict 0/1-only validation).

## D-DR-2 -- decimal.Add has no MethodMap mapping (LOUD at transpile)

- **Phenomenon**: `decimal.Add(tenth, 0.5m)` emits an unmapped generated
  symbol `System_Decimal_Add_<hash>` (undefined at link) -- the static
  decimal.Add(decimal, decimal) has no case in MethodMap.Decimal.cs, so the
  fallback generated-name path emits a call to a symbol the runtime never
  declares. The instance/operator surfaces (`+`, op_Addition) map correctly.
- **Confirmed**: probe rewritten to `tenth + 0.5m` (op_Addition), which
  transpiles to icall_decimal_add and PASSES. decimal.Add is simply a
  never-mapped BCL convenience alias of op_Addition.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Decimal.cs` (no
  "Add" case; op_Addition covers the same icall).
