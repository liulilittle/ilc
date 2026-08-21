# GAPS.md -- arrayslice recorded findings (e2e batch C72, 2026-08-12)

Case status: net48 oracle 29-line byte-exact reference written (expected.txt);
dual-compiler PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## Verified byte-exact contract (this case pins, no gap)

- int32 window extraction: head/tail/mid windows from a 6-element source into
  fresh destinations; dest placement at an offset (leading zeros preserved);
  full-length copy.
- Boundary shapes: one-element first/last windows, zero-length validated
  no-ops at the head AND at one-past-end (index == Length is legal for
  length 0), dstIndex + length == dst.Length fills the destination.
- Self / overlapping copies (memmove): right shift (src 0.. -> dst 1..),
  left shift (src 1.. -> dst 0..), same-slot no-op.
- Per-element-kind value slices: byte[] and long[] windows (elemSize 1 / 8
  raw-byte copy), string[] reference slice (identity move, null padding).
- Error matrix: negative srcIndex / dstIndex / length -> ArgumentOutOfRange,
  source/dest too short + srcIndex > Length -> ArgumentException, int->string
  kind mismatch -> ArrayTypeMismatchException.
- CopyTo instance: full, offset placement, too-small dst -> ArgumentException,
  negative dstIndex -> ArgumentOutOfRange, dstIndex+length > dst.Length ->
  ArgumentException, byte[] CopyTo.

## Recorded defects (NOT fixed -- batch rule)

- **D-ARR-1** cross-kind WIDENING Copy: net48 Array.Copy performs element
  conversion for compatible-but-different element sizes -- int[] -> long[]
  and byte[] -> int[] COPY SUCCEED by widening (probe-verified net48), while
  the runtime icall_array_copy (arrayext.cpp:185) requires equal elemSize AND
  equal isRefElem and throws ArrayTypeMismatchException. The case probes only
  same-type copies + the int->string mismatch (rejected by both sides); a
  widening probe would byte-diff. Fix direction (future): a widening element-
  conversion path in icall_array_copy for int32->int64 / byte->int32 pairs.
- **D-ARR-2** 3-arg Array.Copy(src, dst, length) and the 2-arg long-index
  overloads are not wired (MethodMap.Array.cs: Copy accepts only the 5-arg
  form -> NotSupportedException at transpile). NOT probed -- a 3-arg call
  would fail the C++ build, not byte-diff.
