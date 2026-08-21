# GAPS.md -- convertfull recorded findings (e2e batch C74, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 118-line byte-exact;
expected.txt == oracle).

## D-CF-1 -- ASCII "Infinity"/"-Infinity" ToDouble words: culture divergence on the zh-CN net48 host

- **Phenomenon**: the first draft probed `Convert.ToDouble("Infinity")` and
  `Convert.ToDouble("-Infinity")`. On this zh-CN net48 host the parse
  **throws FormatException** (the .NET positive/negative infinity *words* are
  the culture symbol U+221E, so ASCII "Infinity" is not legal .NET text under
  the zh-CN NumberFormatInfo), while the runtime's grammar accepts the ASCII
  words (culture-invariant; `IsValidDoubleText` + strtod in format.cpp).
  The oracle crashed mid-run at the first such probe (exit -532462766).
- **Evidence**: isolated probe (net48, zh-CN host) prints
  `a_ex=FormatException b_ex=FormatException` for `Convert.ToDouble("Infinity")`
  / `("-Infinity")`; the runtime accepts both.
- **Disposition**: recorded. The case drops the ASCII-word probes and instead
  covers the infinity path through the shared decimal-overflow probe
  (`Convert.ToDouble("1e999")` -> OverflowException, byte-exact on both sides).
  The runtime keeps the ASCII word acceptance (deliberate culture-invariant
  design, documented in il2cpp_format.h); no fix scheduled.

## D-CF-2 (n/a) -- all other probes shared-behavior, no divergence found

- Every remaining probe in convertfull (checked narrowing lattice, G15
  ToString layout, grammar specials, shared-radix rejection) matched net48
  byte-for-byte on both compilers.
