# GAPS.md -- stringops3 recorded findings (e2e batch C71, 2026-08-12)

Case status: net48 oracle 52-line byte-exact reference written (expected.txt);
dual-compiler PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## Verified byte-exact contract (this case pins, no gap)

- TrimStart pure leading removal across the full .NET Char.IsWhiteSpace set
  (space / \t / \n / \r / NBSP \u00A0 / ideographic space \u3000 / mixed runs);
  inner whitespace preserved; all-whitespace -> ""; empty stays empty; nothing
  to trim is identity.
- TrimEnd pure trailing removal, symmetric (incl. em space \u2003).
- TrimStart/TrimEnd combinations: TS->TE, TE->TS, idempotent double-TS/double-TE,
  Trim(TS(x)) / Trim(TE(x)), all-ws chains -> "".
- Removal semantics + identity: no-leading-ws TrimStart returns the SAME
  instance (ReferenceEquals), no-trailing-ws TrimEnd the same; a real removal
  returns a NEW instance; post-trim Length checks.
- Combined chains: TrimStart/TrimEnd feeding Substring, Replace(char,char),
  ToUpper/ToLower, PadLeft/PadRight, StartsWith/EndsWith, Contains, IndexOf,
  get_Length, the string indexer, and a nested TS->TE->PadLeft pipeline.

## D-STR3-1 -- non-empty char[] trim set is dropped (documented deviation, NOT probed)

- net48 TrimStart(char[]) / TrimEnd(char[]) (params) trim EXACTLY the given
  chars; the runtime icall_string_trim_start / trim_end are whitespace-only.
  MethodMap.String.cs TrimStart/TrimEnd emit the char[] argument with a "drop"
  cast, so `"xxabc".TrimStart('x')` would silently behave as the whitespace
  overload on the ILC side and diverge from net48.
- This case therefore probes ONLY the no-arg (empty-set) shapes, which Roslyn
  lowers to TrimStart(Array.Empty<char>()) -- the wired "drop" form that is
  byte-exact. A non-empty trim set stays a recorded gap (do not uncomment).
