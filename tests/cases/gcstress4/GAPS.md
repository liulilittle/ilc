# GAPS.md -- gcstress4 recorded findings (e2e batch C59, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 21-line byte-exact).

## D-GC-4 -- castclass to SZARRAY from a WeakReference.Target fails

- **Phenomenon**: casting `(byte[])weakRef.Target` throws at the transpiled
  run: `Unhandled managed exception: Unable to cast object to the target
  type` (the FIRST gcstress4 build FAIL'd on the very first such cast, line
  `((byte[])wb.Target)[119999] == 42`). The first two lines
  (`wb.IsAlive`, `ReferenceEquals(wb.Target, big)`) printed True, then the
  cast threw.
- **Causal chain**: the runtime's castclass cannot prove a WeakReference
  Target -- an `Il2CppObject*` held by the weak-ref table -- IS the
  SZARRAY `System.Byte[]` type; the cast path for array element types is
  not satisfied. Object-typed and scalar casts (the gcstress3 patterns)
  work; SZARRAY-target casts do not.
- **Disposition**: the case avoids casting `Target` to an array; content is
  verified through the STRONG reference (ReferenceEquals proves identity,
  the strong ref proves content) -- same net48 semantics, no castclass.
- **Suspect**: runtime castclass (the `castclass` icall / isinst path for
  `System.Byte[]` element-type targets).

## Inherited from gcstress3 (unchanged)

- D-GC-1 (array instance fields in nested types unsupported by the M2 field
  decoder) and D-GC-2 (newarr element whitelist) -- both prior-known; the
  case keeps arrays as locals and reference-element arrays out.
