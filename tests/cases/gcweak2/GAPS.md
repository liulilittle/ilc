# GAPS.md -- gcweak2 recorded findings (e2e batch C84, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 33-line byte-exact).

## D-WR-1 -- M3 user-method SZARRAY returns unsupported ("return type SZArray")

- **Phenomenon**: a user helper `static WeakReference[] DeadMany()` threw
  `NotSupportedException: return type SZArray is not supported by M3`
  (CodeGenerator.ReturnTypeCpp). The transpiler has no ReturnTypeCpp arm for
  a user method returning an array.
- **Disposition**: the collection-sweep probe calls three separate
  one-WR-returning helpers instead of one array-returning helper.

## D-WR-2 -- WeakReference(object, bool) / TrackResurrection LOUD-unwired

- The handle-table object model does not track resurrection; the two-arg
  ctor and get_TrackResurrection throw NotSupportedException. Not probed.

## D-WR-3 -- dead-target helpers must return ONE weak ref each

- Same-frame dead-target probes rely on the helper-frame-gone pattern
  (weakrefcase precedent). The net48 JIT may root locals until end of
  method; every doomed target is created inside a helper whose frame is
  gone before the collect.
