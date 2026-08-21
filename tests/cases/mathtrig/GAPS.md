# GAPS.md -- mathtrig recorded findings (e2e batch C80, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 94-line
byte-exact). Active probes cover the Math trig depth: the 15-degree GRID
lattice 0..360 (sin/cos/tan at 0,15,30,45,60,75,90,120,135,150,180,210,225,
240,270,300,315,330,360 -- bit-exact via BitConverter.DoubleToInt64Bits), a
precision SCATTER at arbitrary points (0.1, 0.5, 1.0, 2.0, pi-double,
pi/2-double, 2pi-double for sin/cos/tan/atan/atan2), and the Asin/Acos
lattice + relation identities (sin(asin x)==x on lattice, sin odd / cos
even / tan odd, atan2 quadrant axes). Gaps below are RECORDED, NOT FIXED
(this batch creates test cases only).

## D-MT-1 -- C-library last-ulp disagreement at 17 of 92 trig grid points

- **Phenomenon**: a 3-way harness (net48 vs mingw clang-17 std::sin/cos/tan
  vs msvc v17.9) showed BIT-DIFFERENT results at 17 of 92 probe points:
  tan at 90/180/270/330/360 degrees, sin at 45/180/240/360 degrees, cos at
  90/270 degrees, plus sin(pi-double), tan(pi/2-double), etc. These are
  exactly the points where the argument reduction / last-ulp rounding of
  the two C library implementations disagree with net48's CRT. All 75
  other points are bit-identical across all three.
- **Worked around**: the case embeds ONLY the 75 cross-implementation-
  stable constants (empirically verified before writing). The divergent
  points are NOT probed, so the case stays byte-exact on both compilers.
- **Suspected root file**: none -- this is a platform C library property
  (net48's CRT sin vs MinGW/MSVC libm), not a transpiler defect.

## D-MT-2 -- local functions produce `|` in the mangled method name (build blocker)

- **Phenomenon**: mathtrig.cs originally declared `bool BitEq(...)` /
  `bool BitVal(...)` as C# 7.3 LOCAL FUNCTIONS inside Main. The generated
  C++ named them `Cases_MathTrig__Main_g__BitEq|0_0_8e366b4892bd9483` --
  the `|` (from Roslyn's `<Main>g__BitEq|0_0` mangling) is an illegal
  identifier character, so both clang and msvc failed to compile
  (`expected ';' after top level declarator`, `no matching literal
  operator`). Rewriting the two helpers as STATIC class methods removed
  the `|` and the build passed.
- **Confirmed**: static-method helpers compile clean; all 94 lines pass.
- **Suspected root file**: `src/il2cppc/Emit/` name mangling for local
  functions (the `|` in `<Main>g__X|0_N` must be sanitized).
