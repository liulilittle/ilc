# GAPS.md -- doublefmt recorded findings (e2e batch C76, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 85-line
byte-exact). Active probes cover the full double G/E/F precision and
rounding lattice: G15 default across magnitudes, G1..G15 with the
fixed/scientific switch, F0..F15 with half-away-from-zero rounding and
carry, E0..E15 with the 3-digit exponent, and the near-tie rounding edges
(2.675, 0.015, 999.995). Gaps below are RECORDED, NOT FIXED (this batch
creates test cases only).

## D-DBLF-1 -- G16/G17 precision ignored (double G path emits 15 digits)

- **Phenomenon**: `(0.1).ToString("G17")` emits the 15-digit text
  ("0.1") instead of net48's "0.10000000000000001" -- the double G
  formatter (FormatGeneralDouble, formatext.cpp) caps the significant
  digits at the 15-digit base (kBaseDigits), so any precision > 15 is
  silently clamped to 15.
- **Case adaptation**: no G16/G17 row appears in doublefmt; the explicit-
  precision probes stop at G15 (which IS the 15-digit text and matches).
- **Suspected root file**: `src/runtime/src/formatext.cpp`
  (FormatGeneralDouble -- the `sigDigits < kBaseDigits` gate).

## D-DBLF-2 -- +-Infinity U+221E console-encoding divergence (excluded)

- **Phenomenon**: net48 renders +-Infinity as the zh-CN symbol U+221E for
  every format; the ILC console byte-encodes the wide symbol differently
  than the net48 oracle (fmtround precedent -- the byte streams differ even
  though the code points match). NaN ("NaN", ASCII) is encoding-safe and
  IS probed (f0_nan).
- **Case adaptation**: no Infinity row appears in doublefmt.
- **Suspected root file**: `src/runtime/src/console.cpp` (wide-symbol
  encoding on the write path).
