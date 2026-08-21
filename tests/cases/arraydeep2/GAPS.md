# GAPS.md — arraydeep2 unwired / diverging surface (e2e batch C63, 2026-08-12)

Case status: **PASS 33/33 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the
dimension-boundary API (GetLength/GetUpperBound/GetLowerBound dimension
validation), get_Rank / get_LongLength / LongLength==Length, the full
Array.Copy 5-arg validation matrix, Array.CopyTo 2-arg, Array.Initialize
(string[] no-op), Array.Empty<char/int/string> and Array.Clear range. No
product code modified; no commit made.

## D-ARR-C63-1 — Array.Initialize on a VALUE-element (int32) array DIVERGES

- **Phenomenon**: `int[] a = {1,2,3,4}; a.Initialize();` — net48 oracle keeps
  the values `1,2,3,4` (Initialize is a NO-OP for int32 elements: a value
  type's parameterless ctor produces the all-zero value only for a NEW
  instance, it does not rewrite existing storage), while ILC's
  `icall_array_initialize` (arrayext.cpp:359-367) `std::memset`s the whole
  payload -> all zeros. Verified: a bare net48 probe prints
  `zeroed=False` / `1,2,3,4`; the runtime memset would print `True`/`0,0,0,0`.
- **Causal chain**: the runtime's Initialize implements "value types have a
  default ctor producing the all-zero value" by zeroing the array storage
  wholesale — which is only correct for the FRESH-allocation observable
  (where the storage is already zero), not for a MODIFIED array. net48's
  Initialize never rewrites existing elements.
- **Suspected root file**: `src/runtime/src/arrayext.cpp` (icall_array_initialize).
- **Case handling**: the int32-Initialize probe is REMOVED from the case (it
  would diff FAIL against the net48 oracle). The string[] no-op probe stays
  (reference elements are untouched on BOTH sides -> PASS).

## Recorded, NOT probed (would break the transpile)

- **multidim NEWARR** `int[,] m = new int[2,3]` — D-ARR-1 (MethodBodyReader
  rejects the 0x14 multidim local: "multidim-array/function-pointer local ...
  not supported"). The dimension-ARGUMENT surface (GetLength(1) etc.) is the
  reachable boundary API; the allocator path is future work.
- **Array.Clone()** — no "Clone" case in MethodMap.Array.cs / ArraySort.cs ->
  generated-name undefined symbol -> link failure (arrcomp record).
- **Array.GetLongLength(int)** — only the `get_LongLength` PROPERTY is wired
  (MethodMap.Array.cs:244); the dimension-argument method is unmapped. The
  case uses `a.LongLength` instead.
