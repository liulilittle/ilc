# GAPS.md -- decimalcmp recorded findings (e2e batch C62, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 85-line
byte-exact). Active probes cover the WIRED decimal Compare surface
(icall_decimal_compare / VarDecCmp, -1/0/1).

## D-DEC-1 -- no new gaps encountered

- **Phenomenon**: none. Every probe family (sign grid, scale/magnitude lattice,
  rounding interactions, operator agreement) landed byte-exact on the first
  transpiled run with zero C++ warnings/errors.
- **Notes for future batches**: the `o_ne`/`o_le`/`o_zeroNe` oracle lines are
  legitimately `False` (scale-insensitive `!=`/`<=` on equal-scale values --
  `1m != 1.00m` is False on net48), NOT divergences; the expected.txt records
  the net48 truth table verbatim. The boxed-object Equals/CompareTo(object)
  remain LOUD (MethodMap.Decimal.cs) and are deliberately not probed.
