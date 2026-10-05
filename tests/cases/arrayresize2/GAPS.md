# GAPS.md -- arrayresize2 recorded findings (e2e batch C54, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 28-line byte-exact).

## D-AR2-1 -- Array.Resize<T> is wired for int32 ONLY

- **Phenomenon**: `MethodMap.Array.cs` "Resize" throws
  `NotSupportedException` for any generic argument other than `0x08`
  (int32) -- only `icall_array_resize_i4` exists (il2cpp_array_ext2.h:202).
  The e2e therefore exercises `Array.Resize(ref int[])` exclusively; there is
  NO `Resize` for int64/double/string/reference elements.
- **Disposition**: not probed; recorded so maintainers know the e2e cannot
  guard non-int32 resize shapes.

## D-AR2-2 -- generated code: self-assignment warnings from bne.un lowering

- **Phenomenon**: the transpiled C++ for the loop-carried reference
  comparisons emits `t104 = t104;` (a `-Wself-assign` warning) for the
  `bne.un` pattern. Compiles cleanly on both mingw and msvc (warning only);
  no correctness impact.
- **Disposition**: left as-is (cosmetic code-gen artifact, not a behavior
  defect).
