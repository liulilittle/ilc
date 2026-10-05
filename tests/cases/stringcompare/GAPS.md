# GAPS.md -- stringcompare recorded findings (e2e batch C67, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 57-line byte-exact).

## D-SC-1 -- Comparer.DefaultInvariant static FIELD has no runtime symbol

- **Phenomenon (probed at build)**: `Comparer cdi = Comparer.DefaultInvariant;` emits
  `ldsfld System.Collections.Comparer::DefaultInvariant` (it is a static readonly
  FIELD on net48, not a get_ call). The transpiler's static-field path references
  `System_Collections_Comparer_sf_DefaultInvariant`, which is NOT defined in the
  runtime -- arraysort.cpp defines only `System_Collections_Comparer_sf_Default`
  (the singleton, arraysort.cpp:58). The Default field works (the case uses it
  inline, the icomparedeep pattern); DefaultInvariant fails the C++ build.
- **Disposition**: recorded, NOT fixed. The case asserts the Comparer.Default
  surface only; DefaultInvariant is the documented divergence. (The resolver maps
  `get_Default` / `get_DefaultInvariant` as METHOD calls, but net48's Comparer
  exposes both as FIELDS -- only the Default field symbol exists in the runtime.)

## D-SC-2 -- storing the Comparer singleton in a LOCAL types the slot int32_t

- **Phenomenon (probed at build)**: `Comparer cd = Comparer.Default;` then
  `cd.Compare(...)` generates a local `locN = System_Collections_Comparer_sf_Default`
  whose declared type came out `int32_t` (not Il2CppObject*), so the subsequent
  `cd.Compare(...)` receiver fails to compile ("cannot initialize a parameter of
  type Il2CppObject* with an lvalue of type int32_t"). The INLINE form
  (`Comparer.Default.Compare(...)`, the icomparedeep pattern) folds the field
  directly into the call and compiles clean.
- **Disposition**: recorded, NOT fixed. The case uses the inline form.