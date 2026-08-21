# GAPS.md -- formatchain recorded findings (e2e batch C71, 2026-08-12)

Case status: net48 oracle 36-line byte-exact reference written (expected.txt);
dual-compiler PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## Verified byte-exact contract (this case pins, no gap)

- Multi-level Format nesting (2-3 levels): Format result as an ARG of another
  Format, recursive {0:D2}/{0:X2} inside outer placeholders; reordered inner
  results ({1}/{0}); a Format result INSIDE an escaped-brace placeholder.
- 2-level alignment stacks: inner {0,4} -> outer {0,8} (right), inner {0,-4}
  -> outer {0,-8}/{0,8} (left/right mixes), 3-level {0,6}->{0,10} over a D2
  result; alignment + N2 over a {0,5}-formatted double.
- Alignment + precision combined in-chain: {0,10:D3}, {0,8:f2}, {0,-10:X4},
  {0,6:000}, {0,12:N2}, {0,8:00.00}, {0,9:#,##0.00}.
- Format-string chain: a format string built by string.Format reused as the
  format of another Format (placeholders expand at build time, e.g. "{0:D2} !"
  -> "07 !"); escaping inside the built format.
- StringBuilder.AppendFormat chains feeding string.Format (builder ToString as
  a nested arg, aligned/padded), and builders whose AppendFormat payload is a
  string.Format result; mixed x/X4 with alignment in one builder.
- Escaping interleaved with nesting: "{{0}}" literal arg, {{ }}{{}} sequences.

## Notes (no defect exposed by this case)

- All probes stayed on the fully-wired composite surface (MethodMap.String.cs
  Format arity 0/1/2/3 + params object[]; MethodMap.StringBuilder.cs
  AppendFormat -> AppendFormatCore). No defect recorded -- the nested / chained
  composite grammar is byte-exact on both compilers.
