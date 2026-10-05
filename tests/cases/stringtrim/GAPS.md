# GAPS.md -- stringtrim recorded findings (e2e batch C82, 2026-08-13)

Case status: **PASS** (net48 oracle 29 lines, exit 0; dual-compiler PASS via
`pwsh scripts/run-tests.ps1 -Compiler mingw/msvc`). Active probes cover the
wired no-arg Trim family (MethodMap.String.cs:111-128 ->
icall_string_trim/trim_start/trim_end, il2cpp_string_ext.h:112-121): the FULL
25-code-unit .NET Char.IsWhiteSpace set, each unit as leading / trailing / both
/ all-whitespace; the NON-whitespace boundary units (U+200B/200C/200D/2060/
FEFF/180E/00AD) preserved; mixed runs; inner-whitespace preservation; identity
(ReferenceEquals) + new-instance allocation; Trim() == TrimStart().TrimEnd()
composition battery; downstream chains (Length/indexer/Substring/ToUpper/eq).
All probes byte-exact vs net48. Gaps below are RECORDED, NOT FIXED (this batch
creates test cases only).

## D-STR3-1 (xref, stringops3) -- non-empty char[] trim set is dropped

- **Phenomenon**: net48 Trim(char[]) / TrimStart(char[]) / TrimEnd(char[])
  (params) trim EXACTLY the given chars; the runtime icalls are whitespace-only
  (MethodMap.String.cs TrimStart/TrimEnd emit the char[] argument with a "drop"
  cast, so a non-empty set is silently ignored -- documented deviation, recorded
  as D-STR3-1 in stringops3/GAPS.md). `Trim` itself maps only the 0-arg form.
- **Case adaptation**: this case probes ONLY the no-arg (empty-set) shapes
  (Roslyn lowers a no-arg call to TrimStart(Array.Empty<char>()) -- the wired
  "drop" form), so every probe is byte-exact. No non-empty trim-set probe is
  emitted; a divergence would otherwise show a False label.

## D-STRT-1 (new) -- Trim/TrimStart/TrimEnd char[]-set overloads unreachable from ILC

- **Phenomenon**: the runtime exposes no char-set trim (the icalls are
  whitespace-only by design); MethodMap.String.cs "Trim" has no 1-arg arm at all
  (a `s.Trim('x')` call would hit the generated-name path and fail the build),
  and TrimStart/TrimEnd "drop" the set. The full net48 char-set API surface is
  therefore untestable byte-exact from transpiled code.
- **Status**: recorded -- needs a runtime char-set trim icall + wiring to
  probe; out of scope for a test-only batch.
