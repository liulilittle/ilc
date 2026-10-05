# GAPS.md — reflprop2 unwired/divering surface (e2e batch C17, 2026-08-10)

Case status: **PASS 23/23 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the property
subtable (static / indexer-"Item" / inherited / `new`-hidden resolution /
coexistence) plus the DIRECT accessor value round-trip (static, instance,
indexer, inherited) and is byte-identical to net48. No product code modified;
no commit made.

## D-PRP2-1 — PropertyInfo.GetValue / SetValue (reflection value round-trip) is UNWIRED

- **Phenomenon**: `System.Reflection.PropertyInfo` has NO runtime invoker in
  this build — the property subtable stores only accessor NAMES (typeinfo.h
  Il2CppPropertyInfo.getterName/setterName, resolved by name through the SAME
  methods table, "no duplicated fnPtrs"), and the runtime emits NO
  PropertyInfo wrapper objects. `pi.GetValue(obj)` / `pi.SetValue(obj, v)`
  (also GetGetMethod / GetIndexParameters) are absent from every MethodMap
  domain (grep: zero GetValue/SetValue/PropertyInfo matches in `src/il2cppc`
  beyond dictionary TryGetValue noise; zero in `src/runtime/src`). The
  transpiler therefore falls through BclResolve to the generated-name path and
  emits undefined C++ symbols (the same failure mode as C15 D-RES-1
  `Type.get_Assembly`).
- **Impact**: the reflection-side value round-trip is PARKED in the case
  source (section I). The ACTIVE surface covers the value semantics through
  the DIRECT accessors (sections A/B/C — static, instance, indexer, inherited
  set->read-back round-trips, byte-exact) and the subtable NAME queries
  (sections D-H).
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.*.cs` (no PropertyInfo
  domain) + `src/runtime` typeinfo (no invoker/wrapper emission).

## D-PRP2-2 — an ARRAY instance field aborts the whole assembly load at transpile

- **Phenomenon**: reflprop2's first draft used `private int[] _items;` as the
  indexer backing store. `il2cppc` refused to LOAD the assembly:
  `il2cppc error: field type byte 0x1D (array) not supported in M2` at
  MetadataLoader.cs:652 (DecodeFieldSignature), so the case never reached
  transpile. Any array field on ANY translated user type aborts the run.
- **Worked around in the case**: the indexer's backing store is FOUR plain int
  fields (`_d0.._d3`) selected by an index `if/else` chain in the accessors —
  the indexer semantics (get/set round-trip, Sum aggregate) are identical.
- **Suspected root file**: `src/il2cppc/MetadataLoader.cs:652`
  DecodeFieldSignature (SZARRAY 0x1D rejected for instance fields).

## D-PRP2-3 — folded auto-property accessor reads the WRONG backing field via a base-typed reference

- **Phenomenon**: `PropDerived2` declares `new int BaseProp` (hides
  `PropBase2.BaseProp`). `pd.BaseProp = 11` (derived-typed) sets the DERIVED
  backing field. `((PropBase2)pd).BaseProp` — a BASE-typed reference — reads
  `PropBase2::get_BaseProp` (compile-time bound; BaseProp is NOT virtual) = the
  BASE's own backing field = 0 on net48, but ILC printed the DERIVED field
  value (11): the folded auto-property accessor was resolved to the derived
  backing field through the base-ref shape. Live divergence:
  `rt_baseview` expected True, ILC False.
- **Worked around in the case**: the probe is PARKED (comment in section C).
  The shadowing METADATA probe (`gp_derived_new` — GetProperty("BaseProp") on
  PropDerived2 -> non-null, own table first) and the DERIVED-typed value
  round-trip (`rt_inherit`) both pass byte-exact; only the base-ref folded-read
  shape is removed from the active surface.
- **Suspected root file**: `src/il2cppc` AccessorAnalyzer / auto-property
  folding (the folded accessor's backing-field binding does not honor the
  declaring type of a `new`-hidden property when reached through a base
  reference).

## Notes

- net48 oracle evidence: `out/reflprop2.exe` exit=0, 23 lines captured
  byte-for-byte; harness diff PASS (mingw + msvc), see the C17 report.
