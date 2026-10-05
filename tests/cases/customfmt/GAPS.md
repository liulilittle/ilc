# GAPS.md -- customfmt documented exclusions (e2e batch C15, 2026-08-09)

Case status: **PASS 91-line byte-exact** (net48 oracle vs ILC transpiled run,
mingw + msvc, LF-join diff). The active probes cover the committed custom
engine subset. The following surfaces are deliberately parked (engine
documentation, verified in %TEMP%\opencode\cstmprobe oracle1..9).

## D-FMT-1 -- scientific-notation custom patterns throw FormatException

- `"0.00E+00"` / `"0E0"` / `"0.##e-0"` (an 'E'/'e' followed by '+/-' and a
  '0') is recognized as out-of-subset in ParseCustomPattern
  (formatext.cpp:930-937) -> FormatException. net48 formats these
  scientifically. Not in the committed custom-engine subset.
- **Suspected root file**: src/runtime/src/formatext.cpp ParseCustomPattern.

## D-FMT-2 -- ';' section separators throw FormatException

- A ';' in the pattern (positive/negative/zero sections) is rejected at
  ParseCustomPattern (formatext.cpp:900) -> FormatException. net48 uses
  section-aware formatting. Not in the committed subset.
- **Suspected root file**: src/runtime/src/formatext.cpp ParseCustomPattern.
