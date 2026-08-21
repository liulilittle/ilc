# GAPS.md -- arraychain recorded findings (e2e batch C60, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 43-line
byte-exact, all True). Active probes drive the FULL-DOMAIN chains:
ConvertAll -> Find, ConvertAll -> Sort -> BinarySearch/IndexOf, nested
ConvertAll, FindIndex 4-arg windows, Resize -> Sort, Sort -> Reverse,
Sort(Comparison<T>), empty/negative arrays through the whole pipeline.

## D-ACH-1 -- int.CompareTo(int) is NOT wired (use relational operators)

- **Phenomenon**: the first draft's comparison delegate
  `private static int Desc(int a, int b) { return b.CompareTo(a); }` resolved
  `int.CompareTo` to the undeclared C++ identifier
  `System_Int32_CompareTo_49124a32406e0a90` (mingw build error). No
  MethodMap.Double/Int32 instance-CompareTo mapping exists in the M-stage
  transpiler (the icomparedeep precedent uses relational operators in its
  Comparison delegates, e.g. `a < b ? -1 : (a > b ? 1 : 0)`).
- **Impact**: fixed in the case SOURCE -- the Desc delegate now returns
  `a < b ? 1 : (a > b ? -1 : 0)` (the Descending comparison). Not a runtime
  defect; recorded so future delegate cases use the operator form.
- **Fix path (future)**: a MethodMap for Int32.CompareTo (instance icall) if
  the value-type instance-method milestone ever needs it.

## D-ACH-2 -- transpiled bne.un self-assignment warnings (cosmetic)

- **Phenomenon**: the transpiled C++ for loop-carried reference comparisons
  emits `t212 = t212;` (-Wself-assign warnings; same artifact already
  recorded in arrayresize2/GAPS.md D-AR2-2). 31 warnings on mingw, none on
  msvc, all cosmetic -- both builds produce byte-identical output.
- **Impact**: none (warning only); left as-is per the batch discipline.
