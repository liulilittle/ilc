# GAPS.md -- listindex recorded findings (e2e batch C82, 2026-08-13)

Case status: **PASS** (net48 oracle 44 lines, exit 0; dual-compiler PASS via
`pwsh scripts/run-tests.ps1 -Compiler mingw/msvc`). Active probes cover the
FULL wired List<int> INDEX surface (MethodMap.List.cs + listi4.cpp): the 1-arg
`List<int>.IndexOf(T)` across every boundary (head/mid/tail/absent/duplicates
first-match/empty/single/value edges int.MinValue..int.MaxValue/mutation drift
after set_Item/Remove/Insert/RemoveAt/growth/after-Clear/Contains agreement)
and `Array.BinarySearch<int>` (the wired arraysort domain, MethodMap.Array.cs:67
+ MethodMap.ArraySort.cs:88) over a `List.ToArray()` snapshot (found/miss
~insertionPoint/below-first/between/above-last/empty/single/duplicates/value
edges/snapshot-independence). Gaps below are RECORDED, NOT FIXED (this batch
creates test cases only).

## D-LI-1 -- List`1.IndexOf range overloads are NOT wired (only the 1-arg form)

- **Phenomenon**: `MethodMap.List.cs` TryMapList maps ONLY `IndexOf(T)` (the
  count==1 / ps[0]==0x13 arm, MethodMap.List.cs:117-121). The net48 range
  overloads `IndexOf(T, int startIndex)` and `IndexOf(T, int startIndex, int
  count)` have no arm and would fall to the generated-name path -> undefined C++
  symbol -> build fail. The listi4 runtime likewise has no range-index icall.
- **Case adaptation**: every IndexOf probe uses the wired 1-arg form; range
  semantics (search from an offset / bounded window) are not reachable and stay
  recorded. net48 equivalence for the probed surface is exact.

## D-LI-2 -- List`1.BinarySearch is NOT wired (Array.BinarySearch bridge used)

- **Phenomenon**: net48 `List<int>.BinarySearch(item)` has no mapping in
  MethodMap.List.cs (the wired surface ends at ToArray). Calling it would
  generate an undefined symbol (same class as D-LO3-1's List.Sort). The runtime
  listi4 domain has no binary-search entry either.
- **Case adaptation** (the listops3 D-LO3-1 ToArray-bridge precedent):
  `List<int>.ToArray()` -> `Array.BinarySearch<int>(int[], int)` -- the wired
  lower-bound search (MethodMap.Array.cs:67 -> icall_array_binary_search_int32;
  missing -> ~insertionPoint, empty -> -1). Semantics verified byte-exact
  against net48 across the full boundary matrix.
- **Duplicate-note**: for a duplicated value Array.BinarySearch returns SOME
  matching index (net48 makes no which-duplicate guarantee); the probe asserts
  only `result >= 0 && array[result] == value` (deterministic on both).
