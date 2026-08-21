# GAPS.md -- linqorder2 recorded findings (e2e batch C82, 2026-08-13)

Case status: **PASS** (net48 oracle 32 lines, exit 0; dual-compiler PASS via
`pwsh scripts/run-tests.ps1 -Compiler mingw/msvc`). Active probes cover the
wired OrderBy surface (MethodMap.Linq.cs:263-283 ->
il2cpp_linq_order_by_key_i4, linq_sort.h:82 / linq_sort.cpp SortInt32KeyCopy:
int32 elements + int32 keys, key cached EXACTLY ONCE, stable merge sort, fresh
result array): the key-cache-once contract (side-effecting counter == source
length), stability under equal keys (index-marked elements), composite-key
ThenBy emulation (tens-then-units lexicographic), key families (identity /
negated descending / modulus ties / negatives / int.MinValue..MaxValue), copy
semantics (source untouched, result fresh), operator composition
(Where/Select/OrderBy in all orders -> First/Take/Sum/Count/Min/Max/Any/All),
and edge inputs (empty/single/already-sorted/reverse/all-equal). Gaps below are
RECORDED, NOT FIXED (this batch creates test cases only).

## D-LO2-1 -- Enumerable.ThenBy / ThenByDescending / OrderByDescending NOT wired

- **Phenomenon**: MethodMap.Linq.cs TryMapLinq wires OrderBy only
  (il2cpp_linq_order_by_key_i4). `ThenBy` / `ThenByDescending` /
  `OrderByDescending` have no arm -- a call would fall to the generated-name
  path and fail the C++ build (verified class of failure at transpile for
  `Enumerable.Last`, see D-LO2-2). The runtime linq_sort.h exposes no secondary
  sorter (only the three per-family order-by entries + the key-selector entry).
- **Case adaptation** (the listops3 D-LO3-1 bridge precedent): the
  OrderBy(primary).ThenBy(secondary) ORDERING is reproduced with a SINGLE
  OrderBy over the composite key `primary*100 + secondary` -- for the bounded
  two-digit keys used, the composite order is byte-identical to net48's
  OrderBy+ThenBy, and the lexicographic result is asserted element-by-element.

## D-LO2-2 -- Enumerable.Last is NOT wired (transpile LOUD)

- **Phenomenon**: `Enumerable.Last()` / `Last(pred)` have no MethodMap.Linq arm
  (only First/FirstOrDefault). The first linqorder2 draft used `.Last()` in two
  edge probes; il2cppc aborted with
  "generic instantiation of System.Linq.Enumerable.Last has no BCL mapping in
  M5" (MethodRefResolver.cs:303). net48 has Last, the runtime has no last_i4.
- **Case adaptation**: `.Last()` replaced by `.ToArray()[n-1]` (the wired
  ToArray materializer, same D-LO3-1 composition family). Probe semantics
  (last element of the sorted result) are preserved byte-exact.
