# GAPS.md -- linqtake recorded findings (e2e batch C80, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 83-line
byte-exact). Active probes cover the LINQ Take/Skip depth: the boundary
lattice (count in {-5,-2,-1,0,1,2,3,4,5,6,7,8,9,10,11,100,int.MaxValue}
against a 9-element source, result VALUES not just lengths), chain
composition (Take->Skip / Skip->Take / Take->Take / Skip->Skip / Take->
Where / Skip->Where / Where->Take / Select->Take / Take->Select / Skip->
Select / Select->Skip / Where->Skip / the Where->Skip->Take->Select
pipeline / a negated pipeline), Take/Skip on shapes (empty / single /
all-negative / duplicate-heavy / unsorted), aggregates over slices
(Count/Sum/Min/Max/First/FirstOrDefault/Any/All/ToArray/ToList + the empty
First/Min throw probes), and the slice partition identity
Take(k).Concat(Skip(k)) for k in {0,1,4,8,9,10}. Gaps below are RECORDED,
NOT FIXED (this batch creates test cases only).

## D-LT-1 -- generated code emits benign `-Wself-assign` warnings on bne.un.s

- **Phenomenon**: the transpiled C++ for the bool-expression comparisons
  (`label=" + (cond)`) emits lines like `t654 = t654;` (the
  store-evaluated-value idiom after a branch) that trigger clang's
  `-Wself-assign` warning (45 warnings for linqtake, all in
  Cases_LinqTake.cpp). Warnings only -- the build succeeds and every probe
  passes byte-exact.
- **Causal chain**: the CodeGenerator's "store evaluated value into tN"
  emission after a bne.un.s branch assigns the already-assigned temp to
  itself; harmless on all three compilers (mingw/msvc/wsl), but noisy.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (the
  store-evaluated-value emission around branch handling).

## D-LT-2 -- Take/Skip are WIRED only in the single-arity form

- **Phenomenon**: Enumerable.Take(TSource,int)/Skip(TSource,int) map to
  il2cpp_linq_take_i4 / il2cpp_linq_skip_i4 (MethodMap.Linq.cs:212-228).
  The predicate-adjacent overloads (TakeWhile/SkipWhile) are NOT wired --
  they would resolve through the generated-name path and fail the C++
  build. This case therefore probes only the int-count Take/Skip surface.
- **Confirmed**: the runtime materializes a fresh int32[] for each Take/
  Skip, so chained operators see a real array (the D-LINQZ tracked-array
  contract). No probe removed.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Linq.cs` (Take/Skip
  cases -- TakeWhile/SkipWhile absent).
