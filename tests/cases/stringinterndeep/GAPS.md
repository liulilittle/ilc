# GAPS.md -- stringinterndeep defects (e2e batch C52, 2026-08-12)

Case status: **PASS dual-compiler (mingw + msvc)** -- the active probes cover
the WIRED String.Intern surface (il2cpp_string_intern + the StringPool ldstr
path): reference identity, content dedup, prefix non-aliasing, pool GROW
(70% load factor rehash with identity survival), and GC survival (the pool
roots its entries; interned strings survive GC.Collect while never-interned
runtime strings are collected). The gaps below are RECORDED, NOT FIXED (this
batch creates test cases only).

## D-INT-1 -- String.IsInterned not wired (no source anywhere)

- **Phenomenon**: any call to `string.IsInterned(s)` would abort at the
  transpile stage (no method mapping). No probe in stringinterndeep.cs uses
  it -- the "intern pool probe" dimension of the batch worklist is partially
  OUT of reach (the identity/dedup/survival half IS probed via Intern).
- **Causal chain**: `grep IsInterned` over src/runtime and src/il2cppc
  returns NOTHING. MethodMap.String.cs maps Intern (line 347-348 ->
  il2cpp_string_intern) but has no IsInterned case; the runtime
  il2cpp_string_intern.h declares only `il2cpp_string_intern` /
  `il2cpp_string_intern_literal`.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.String.cs` (add an
  IsInterned case) + `src/runtime/src/stringintern.cpp` (add a find-only
  entry returning null on a miss).

## D-INT-2 -- (informational) net48 JIT pre-interns method literals

- **Phenomenon**: a string literal used ANYWHERE in a net48 method is
  interned when that method is JIT-compiled, so `string.Intern(fresh)`
  where `fresh`'s content equals a source literal returns the PRE-INTERNED
  literal, NOT `fresh` (RefEq == 0). The case therefore builds its runtime
  contents via `string.Concat` with content that never appears as a literal
  (e.g. "deep-copy"), pinning the MISS-path identity semantics. The ILC
  ldstr path (il2cpp_string_intern_literal) matches on first use.
- **Not a defect** -- net48 behavior; the case is written to be insensitive
  to it (verified byte-exact against the net48 oracle).
