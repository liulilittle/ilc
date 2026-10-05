# GAPS.md — charconv unwired surface (e2e batch C68, 2026-08-12)

The case probes ONLY the wired conversion surface (87 lines PASS vs net48,
dual-compiler mingw+msvc). Gaps below were verified before being excluded.

## D-CHC-1 — char Unicode CODE-POINT statics unwired (ConvertToUtf32 / ConvertFromUtf32 / GetUnicodeCategory / IsSurrogate / GetNumericValue)

- **Phenomenon**: `char.ConvertToUtf32`, `char.ConvertFromUtf32`,
  `char.GetUnicodeCategory`, `char.IsSurrogate`/`IsHighSurrogate`/
  `IsLowSurrogate`, `char.GetNumericValue` have NO MethodMap arm (grep over
  src/il2cppc/Emit for ConvertToUtf32/GetUnicodeCategory/IsSurrogate -> 0
  hits) and no runtime icall in il2cpp_char.h -- the System.Char STATIC
  dispatch (MethodMap.Char.cs) covers only IsDigit/IsLetter/IsLetterOrDigit/
  IsLower/IsUpper/IsWhiteSpace/ToUpper/ToLower(+Invariant).
- **Causal chain**: the BCL resolver falls through to the generated-name
  emission path for the unwired statics (undefined extern -> cmake build
  abort). The charconv case therefore expresses the UNICODE SURFACE with pure
  int casts / surrogate arithmetic (code-unit casts, hi/lo range tests, pair
  combine/decompose) -- no icall involved.
- **Suspected root file**: a future MethodMap.Char.cs extension +
  il2cpp_char.h/runtime (ConvertToUtf32 pair arithmetic is documented in the
  charstr D-CHA-2 GAP).

## D-CHC-2 — surrogate PAIR string literals are the only safe astral spellings

- **Phenomenon** (carried from charstr D-CHA-1, unchanged): a string literal
  containing an UNPAIRED surrogate aborts the string-pool emission (empty
  string-pool.h -> build abort). charconv uses valid `\uD83D\uDE00` pairs and
  `(char)0xD83D`-style single-unit casts only.

## D-CHC-3 — char.ToString of astral code points is out of the wired domain

- **Phenomenon**: the wired char->string surface (char.ToString /
  StringBuilder.Append(char) / concat) emits a single UTF-16 CODE UNIT --
  net48 semantics for a lone surrogate unit. No code-point-level char
  formatting is probed (out of the char domain's ASCII/BMP scope); the
  UTF-8/UTF-16 ENCODING round trips (S5/S6) carry the astral pairs instead.
