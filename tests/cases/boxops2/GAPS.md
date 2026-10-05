# GAPS.md -- boxops2 recorded findings (e2e batch C77, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 59-line
byte-exact). Active probes cover the boxed COMPARISON dimension: the default
comparer lattice (D-ICMP-2 ComparerCompareCore) over int/uint/long/ulong/
double/single/char/bool/string boxes + null-first, the boxed GetHashCode VALUE
patterns (GAP-BOX-6 fill consistency + exact values), cross-type boxed Equals
combos, comparer-driven object[] sort, and unbox-then-compare. Gaps below are
RECORDED, NOT FIXED (this batch creates test cases only).

## D-BX2-1 -- Comparer.Compare has no Byte/SByte/Int16/UInt16 box arms

- **Phenomenon**: `Comparer.Default.Compare((object)(byte)5, (object)(byte)200)`
  crashes the runtime with "IComparable.CompareTo dispatch: the receiver type
  has no reflected virtual CompareTo slot" (net48 returns -1). The default
  comparer (arraysort.cpp ComparerCompareCore) special-cases only Int32/UInt32/
  Int64/UInt64/Double/Single/Char/Boolean/String boxes; byte/sbyte/short/ushort
  boxes fall through to the IComparable.CompareTo fallback, and those box
  classes expose no reflected virtual CompareTo slot -> NotSupportedException.
- **Confirmed**: probes removed (cmp_byte_lt / cmp_sbyte_lt / cmp_short_eq /
  cmp_ushort_gt). The HashCode/Equals/unbox surfaces for those types pass.
- **Suspected root file**: `src/runtime/src/arraysort.cpp` ComparerCompareCore
  (needs Byte/SByte/Int16/UInt16 arms reading the int32 payload slot).

## D-BX2-2 -- Array.Reverse rejects a boxed object[]

- **Phenomenon**: `Array.Reverse(object[])` on an object[] of boxed ints throws
  "Only one-dimensional System.Int32 arrays are supported" (net48 reverses the
  boxes). The comparer-driven object[] SORT (il2cpp_array_sort_icomparer) works
  (ref-array arm), but icall_array_reverse (arrayext.cpp) calls RequireInt32Array
  unconditionally -- there is no reference-element reversal arm.
- **Confirmed**: probe removed (arr_desc). Reverse-of-boxes is not reachable;
  the int32 Reverse surface is covered by arrayfuncs2.
- **Suspected root file**: `src/runtime/src/arrayext.cpp` icall_array_reverse /
  `src/runtime/src/arrayext2.cpp` icall_array_reverse_range.
