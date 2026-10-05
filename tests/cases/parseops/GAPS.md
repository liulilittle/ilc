# GAPS.md — parseops unwired surface (2026-08-07)

The case probes ONLY the wired surface (34 lines PASS vs net48, mingw:
`pwsh scripts/run-tests.ps1 -Compiler mingw -Case parseops`). Everything below
is deliberately NOT called from `parseops.cs` — each entry aborts the
transpile (LOUD), emits an undefined extern, or is a verified net48/runtime
divergence.

## D-PAR-1 — int.Parse 各进制: Convert.ToInt32(string, fromBase) LOUD

- **Phenomenon**: `Convert.ToInt32(s, 2/8/16)` aborts the transpile with
  `Convert.ToInt32(string, fromBase) not wired: the runtime has no throwing
  fromBase icall (only icall_try_parse_int32_base, TryParse contract)`.
- **Causal chain**: the runtime DOES have `icall_try_parse_int32_base`
  (il2cpp_parse.h:93, parse.cpp) but it is a NO-THROW TryParse-shaped icall
  (returns 1/0 + out slot); net48's Convert.ToInt32(s, fromBase) is a THROWING
  ParseNumbers.StringToInt (IsTight|NoSpace) with FormatException /
  OverflowException / ArgumentException per-radix rules, so wiring it directly
  would silently miscompile (parsecase expects "threw").
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Convert.cs:37`
  (construction ticket: add throwing `icall_convert_to_int32_string_base`).

## D-PAR-2 — long.Parse: LOUD

- **Phenomenon**: `long.Parse("123")` aborts the transpile with
  `Int64.Parse not wired: the runtime has no int64 Parse icall (only TryParse,
  which needs an out result -- ref/out milestone item)`.
- **Causal chain**: the runtime has only `icall_try_parse_int64` (il2cpp_parse.h:78,
  no-throw + out slot); no throwing int64 Parse icall exists. The task's
  "long.Parse" surface is therefore exercised through long.TryParse (wired,
  MinValue/MaxValue/overflow probed).
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.ValueType.cs:65`.

## D-PAR-3 — int.TryParse 4-arg (NumberStyles, IFormatProvider): undefined extern

- `int.TryParse(s, NumberStyles.Integer, null, out v)` has no MethodMap case
  (only the 2-arg 0x0E/0x08 shape, MethodMap.ValueType.cs:46) → falls through
  to the generated-name path → undefined `System_Int32_TryParse_<hash>` extern
  at C++ link. Same wall as D-PAR-1: the net48 styles/provider surface needs
  the out-slot + NumberStyles semantics.

## D-PAR-4 — int.Parse(null) / double.Parse(null): FormatException vs ArgumentNullException

- The runtime's throwing Parse family rejects a null text with **FormatException**
  ("String reference not set to an instance of a String.",
  il2cpp_format.h icall_string_parse_int line 321 / icall_string_parse_double
  line 395) while net48 throws **ArgumentNullException**. An uncaught probe
  would abort the transpiled process, so null is exercised through TryParse
  only (returns false on both sides — the `pair_null=fail` line).

## D-PAR-5 — Int64.MinValue ldc.i8 codegen: MinGW sign-compare warning (observation)

- `l == long.MinValue` emits `t59 = (loc2 == -9223372036854775808LL);` — the
  literal `9223372036854775808` overflows signed long long, so the C++ compiler
  interprets it as unsigned and warns `comparison of integers of different
  signs` (observed in the mingw build; harmless — the bit pattern comparison is
  still exact). Suspected root: `src/il2cppc` ldc.i8 constant emission does not
  special-case Int64.MinValue. Not a functional divergence; a warning-level
  polish item.

## N-PAR-1 — double "Infinity"/"-Infinity" words (documented deviation)

- zh-CN net48 rejects the ASCII "Infinity" spelling (its symbol is U+221E ∞);
  the runtime accepts ASCII "Infinity"/"-Infinity" (InvariantCulture
  simplification, il2cpp_parse.h:36-45). Also, net48 prints infinities as ∞
  while the runtime prints "Infinity". The words are NOT probed (NaN is
  probed — "NaN" is culture-stable on zh-CN).
