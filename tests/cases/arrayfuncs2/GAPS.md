# GAPS.md -- arrayfuncs2 recorded findings (e2e batch C77, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 28-line
byte-exact). Active probes cover the array-function COMBINATION dimension:
Reverse whole/range (icall_array_reverse / icall_array_reverse_range),
Sort+Reverse composition, Copy with offsets/overlap/zero-length
(icall_array_copy 5-arg memmove), Copy+Reverse composition, Clear range +
Clear-Sort composition (icall_array_clear_int32), and the error lattice for
Copy (negative/long/dst-short/type-mismatch/null -> the net48-verified
exception types) and Reverse (negative index / index+length overflow). Gaps
below are RECORDED, NOT FIXED (this batch creates test cases only).

## D-AF2-1 -- Array.Clear error exceptions diverge from net48

- **Phenomenon**: `Array.Clear(new int[3], 1, 3)` throws
  IndexOutOfRangeException on net48 (the inlined Array.Clear bounds check),
  while the runtime icall_array_clear_int32 (arrayext2.cpp:554) throws
  ArgumentException ("Offset and length were out of bounds"); a negative
  index gives net48 IndexOutOfRangeException vs the runtime's
  ArgumentOutOfRangeException. The runtime's Clear validation follows the
  Reverse/Copy shape rather than the net48 Array.Clear contract.
- **Confirmed**: probes removed (e_clr_long / e_clr_neg) -- a catch of the
  net48 type would never fire on the runtime. The Clear SUCCESS surface
  (mid-window zeroing, whole-array clear, zero-length no-op, Sort-after-Clear)
  is byte-exact.
- **Suspected root file**: `src/runtime/src/arrayext2.cpp`
  icall_array_clear_int32 (net48 Clear throws IndexOutOfRangeException from
  System.Array.Clear's own bounds check before any public-API validation).
