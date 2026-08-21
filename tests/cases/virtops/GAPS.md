# GAPS.md — virtops unwired/divering surface (e2e batch C13, 2026-08-09)

Case status: **PASS 22/22 byte-exact** (net48 oracle vs ILC mingw transpiled
run, LF-join diff). The devirtualization surface (S1 sealed receiver, S1
effectively-sealed closed-world, S2 final override, S3 single-implementer
interface) is behavior-identical to net48. One real defect was found and is
recorded below (parked out of the active probe set so the active surface stays
byte-exact).

## DEFECT D-VIRT-1 — callvirt to a `new`-hidden method resolves to the inherited virtual slot impl

- **Phenomenon**: `Square.Tag` is declared `new int Tag()` (hides the
  inherited virtual `Shape.Tag`, whose `Rect` override is `sealed override`
  -> final). `Square sq = new Square(3); sq.Tag();`:
  net48 prints **200** (the `new`-hidden `Square::Tag` body runs);
  ILC prints **100** (the generated code calls `Rect_Tag_be7a2ac3ab01c55e`,
  the INHERITED virtual-slot implementation). Reproduced in the first virtops
  driver run: `sq.Tag()` line diffs 200 vs 100; every other probe matched.
- **IL proof** (net48 exe metadata, System.Reflection.Metadata dump of
  `Cases.VirtOps::Main`): the call is
  `off=122 op=callvirt MD:Square::Tag` — a NON-virtual `new` method reached
  through callvirt on its own declaring type. net48 runs the token's own
  method (Square_Tag); the transpiler's VirtualTarget/callvirt resolution
  instead walks the receiver's vtable slot for the NAME `Tag` and picks the
  inherited slot method (Rect_Tag).
- **Causal chain**: a `new`-hidden method occupies its OWN vtable slot
  distinct from the hidden virtual's slot. The transpiler resolves the
  callvirt by method NAME/signature against the receiver's vtable slot table
  (TryDevirtualize/VirtualTarget), so a hidden non-virtual method is
  conflated with the inherited virtual of the same name. The runtime vtable
  would dispatch the hidden method's own slot on net48.
- **Active-surface impact**: the probe was REMOVED from virtops (the parked
  line stays in the case source as a comment). All virtual-dispatch shapes
  (base ref -> derived override, final override via Shape ref, interface
  single/multi implementer, multi-level chain, base.Method()) pass
  byte-exact.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs`
  (VirtualTarget / TryDevirtualize slot resolution) + `VTablePlanner.cs`
  (`new`-method slot allocation). Fix direction: when the callvirt token
  resolves to a NON-virtual (hide-by-signature) method on the receiver's
  static type, emit a direct call to THAT method, not the inherited
  same-name virtual slot.

## Notes

- net48 oracle evidence: `out/virtops.exe` exit=0, 22 lines captured
  byte-for-byte; harness diff PASS (see the C13 report).
- No product code modified; no commit made.
