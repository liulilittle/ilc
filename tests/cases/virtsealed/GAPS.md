# GAPS.md — virtsealed unwired/divering surface (e2e batch C17, 2026-08-10)

Case status: **PASS 27/27 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the sealed-
class + final-slot devirtualization gates (S1 sealed receiver direct call on an
inherited final override, S2 final override freezing the slot, sealed
no-override inheritance, sealed+interface, multi-implementer interface with a
sealed implementation, sealed-override base call) and is behavior-identical to
net48. No product code modified; no commit made.

## Notes

- The virtops D-VIRT-1 shape (`new`-HIDDEN non-virtual method reached through
  callvirt on its declaring type -> transpiler resolves the INHERITED virtual
  slot impl) is NOT reproduced here: virtsealed uses no `new`-hiding anywhere
  (the only shadowing in this batch is reflprop2's `new` property, recorded
  separately as D-PRP2-3). All probes below dispatch through genuinely virtual
  slots, so the resolved implementation is always the correct override.
- net48 oracle evidence: `out/virtsealed.exe` exit=0, 27 lines captured
  byte-for-byte; harness diff PASS (mingw + msvc), see the C17 report.
- Every probe folds to a deterministic int/bool computed from method results
  (no identity of the resolved MethodInfo, no timing) — devirt is internal;
  only the call RESULT is observable, and it matches net48 exactly.
