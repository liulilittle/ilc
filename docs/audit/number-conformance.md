# Number-Formatting Conformance Audit — net48 oracle + .NET Core Number.cs

Date: 2026-08-04 · Domain: `src/runtime/src/numberformat.cpp` (invariant),
`src/runtime/src/formatext.cpp` (zh-CN), tests. · Reference: .NET Core
`Number.Formatting.cs` (release/8.0) + `Number.NumberBuffer.cs` +
`Number.Dragon4.cs` (pulled to `reference/netcore/System.Private.CoreLib/`).

Baseline principle (user rule): **the Microsoft implementation wins**. The
empirically runnable ground truth is the net48 oracle (`csc.exe` probes in
`%TEMP%\opencode\numprobe\`); .NET Core `Number.cs` documents the semantics
and the places where net48 and Core diverge (noted below).

---

## 1. How .NET formats numbers (source-verified, Number.Formatting.cs)

- **Integer paths** (`FormatInt32/64Slow`): D = decimal pad; X = two's
  complement of the type width, zero-pad to `max(digits, width)`; G/D with
  `digits < 1` = plain decimal; N/C/E/G-with-precision go through
  `NumberBuffer` (`Int32ToNumber`) — EXACT digits, no double round-trip.
- **Double path** (`FormatDecimalIeee754`): non-finite values return the
  culture symbols **before any format work** (no percent/currency
  decoration). Finite values become a `NumberBuffer` of correctly-rounded
  significant digits (Dragon4 in Core; net48 uses the msvcrt-class CRT
  converter, empirically **15 significant digits** — see §3).
- **Rounding** (`RoundNumber` + `ShouldRoundUp`): half AWAY FROM ZERO
  (`digit >= '5'` rounds up; ties round away — Core's comment explicitly
  notes this is NOT IEEE half-even).
- **F/N**: `RoundNumber(scale + precision)` then fixed emission; N groups
  every 3 digits.
- **E**: rounds to `precision + 1` significant digits; exponent = scale-1,
  sign always, min **3** digits (`FormatExponent(minDigits: 3)`); E0 omits
  the decimal point.
- **G**: `RoundNumber(precision)`; scientific iff `scale > precision ||
  scale - 1 < -4` (i.e. scale-1 < -4); exponent min **2** digits; trailing
  zeros stripped; `digits < 1` = default precision (15 for net48 doubles).
- **P**: `scale += 2` (exact for integers) then N-style; double P multiplies
  the VALUE by 100.0 in double first (net48 observable — see §4).

## 2. net48 oracle — method

`%TEMP%\opencode\numprobe\probe{1..4}.cs` (csc.exe, .NET Framework 4.8
runtime on the zh-CN verification host): the boundary matrix
`{-0.0, -0.5, 0.5, 1.5, 2.5, 1234567.89, 0.00123, 2.675, 0.15, 1.45,
9999.99, -0.4, 10.5, NaN, ±Inf, 0.1234, 1234.5, int.MaxValue,
long.MaxValue, 255, -1, 0, 12345, ...} × {N0, N2, N3, X, X4, E2, E4,
G, G15, P1, P2, F0, F2, G1..G8, custom "0.00", e2, x4, D100, ...}` under
InvariantCulture and zh-CN. Captures: `oracle.txt` (432 lines),
`oracle2.txt` (228, near-tie battery), `probe3/4` (zh-CN, codepoint-exact
U+221E evidence).

## 3. The key empirical finding — net48's 15-digit base

net48 does **not** round the exact binary value: its converter (msvcrt-class
`_ecvt`) produces a correctly-rounded **15-significant-digit** decimal, and
`RoundNumber` rounds THAT (half away from zero). Discriminating oracle rows:

| value | format | net48 | 15-digit base explains |
|---|---|---|---|
| 2.675 | F2/N2 | `2.68` | base `2.67500000000000` → tie → up (Core Dragon4 gives `2.67` — **net48 vs Core divergence**, net48 wins here) |
| 1.23499999 | F2 | `1.23` | base `1.23499999000000` → dropped `4` |
| 1.235 | F2 | `1.24` | base `1.23500000000000` → tie → up |
| 0.1499999999 | F1 | `0.1` | base `0.149999999900000` |
| 0.15 | F1 | `0.2` | base `0.150000000000000` → tie → up |
| 123456789012345678.0 | F2 | `123456789012346000.00` | 15-digit base truncates beyond digit 15 |

The old implementation (snprintf `%.{k+4}f` + manual round) double-rounded:
`1.23499999 F2 -> "1.24"` and `0.1499999999 F1 -> "0.2"` — both wrong vs
net48. **Fix: base = `snprintf("%.14e")` (15 significant digits, correctly
rounded) in both modules.** Same base class for the E mantissa, the G
precision rounding, and the custom "0.00" path. `format.cpp`'s G15 already
uses `%.15g` — same base, consistent.

## 4. Differences found and fixed (before → after)

| # | area | before (both modules) | net48 oracle | status |
|---|---|---|---|---|
| 1 | double F/N/E/P rounding | `%.{k+4}f` guard digits; near-tie double rounding (`1.23499999 F2 -> "1.24"`) | 15-digit base (`-> "1.23"`) | **FIXED** both modules |
| 2 | P of NaN/±Infinity (invariant) | `"NaN %"` / `"Infinity %"` / `"-Infinity %"` | `"NaN"` / `"Infinity"` / `"-Infinity"` (no decoration) | **FIXED** numberformat.cpp + tests |
| 3 | P of NaN/±Infinity (zh-CN) | `"NaN%"` / `"Infinity%"` (ASCII) | `"NaN"` / `"∞"` / `"-∞"` (U+221E, no %) | **FIXED** formatext.cpp + tests |
| 4 | zh-CN specials in F/N/E/G/custom | ASCII `"Infinity"` (format.cpp precedent) | U+221E `∞` in EVERY format (probe4 codepoints) | **FIXED** formatext.cpp + tests |
| 5 | G with precision (double) | threw / delegated G15 ignoring precision | `G2(1234567.89)="1.2E+06"`, `G7="1234568"`, `G2(0.000123)="0.00012"`, `G1(1e-6)="1E-06"` | **FIXED** formatext.cpp (new FormatGeneralDouble, min-2 exponent) |
| 6 | G with precision (int) | ignored precision | `G2(12345)="1.2E+04"`, `G15(long.MaxValue)="9.22337203685478E+18"` | **FIXED** formatext.cpp (new FormatGeneralInt) |
| 7 | F on integers | FormatException (out of subset) | `F2(255)="255.00"` | **FIXED** formatext.cpp |
| 8 | P on integers | FormatException | `P1(255)="25,500.0%"` (zh-CN), exact digit shift (no overflow: `long.MaxValue P1` OK) | **FIXED** formatext.cpp |
| 9 | lowercase `x`/`e` | normalized to uppercase | `x4(255)="00ff"`, `e2(255)="2.55e+002"` | **FIXED** formatext.cpp (letter case preserved) |
| 10 | double P scaling | `value * 100.0` (double) | same — net48 multiplies in double (`P1(123456789012345678.0)="12,345,678,901,234,600,000.0 %"`) — digit-shift would be WRONG | unchanged (verified) |
| 11 | int64 E / X / D / N | exact decimal digits, two's complement, pad-to-max | `E2(995)="9.95E+002"` (no round when digits exhausted), `E2(9999)="1.00E+004"` | unchanged (already conformant) |
| 12 | -0.0 / rounding-to-zero sign | no sign when rounded magnitude is zero | same (`N0(-0.4)="0"`, `F0(-0.4)="0"`, `N2(-0.0)="0.00"`) | unchanged (verified) |

## 5. Known residual divergences (documented, not fixed)

- **D100-style custom fallback**: net48 treats `"D100"` as a custom format
  with literals (`"D112345"`); this runtime throws FormatException (custom
  subset is `'0'/'.'` only). Pre-existing, documented in
  `il2cpp_format_ext.h` — out of the 50-min scope (full custom-format
  engine required).
- **format.cpp G15** (`icall_double_to_string`) still emits ASCII
  `"Infinity"` for infinities (net48 zh-CN: U+221E); format.cpp is OUT of
  this task's file domain (read-only). The formatext G/G2 paths now return
  `∞` themselves. Default `double.ToString()` (via format.cpp) keeps the
  documented `"Infinity"` divergence.
- **int32 hex width**: the invariant `numberformat.h` hex API is int64-based
  (16 digits for negatives); the 8-digit int32 two's-complement lives in
  `icall_format_int32_custom` (formatext, matching net48).
- **Header staleness**: `il2cpp_format_ext.h` "KNOWN DIVERGENCES" still
  claims int-F/P throw and 4-guard-digit rounding — now stale; headers are
  outside the file domain (read-only), so this audit doc is the record.

## 6. Fix list (files + tests)

- `src/runtime/src/numberformat.cpp` — 15-digit base (GetDoubleDigits +
  FormatFixed + FormatSciFinite), P-specials no `" %"`.
- `src/runtime/src/formatext.cpp` — 15-digit base; U+221E specials for every
  format (MakeSpecialString); G-with-precision (double + int, FormatGeneral
  with min-2 exponent); F/P on int; lowercase x/e; P-specials no `%`.
- `tests/runtime/test_numberformat.cpp` — P-specials rows corrected; 15-digit
  near-tie battery; big-value P; G15 of -0.0.
- `tests/runtime/test_formatext.cpp` — specials corrected (∞, no %);
  G-precision rows; int F/P rows; lowercase rows; near-tie battery.
- Reference: `reference/netcore/System.Private.CoreLib/src/System/
  Number.Formatting.cs` (net8), `Number.NumberBuffer.cs`, `Number.Dragon4.cs`.

## 7. Oracle comparison results

- **Invariant module** (`numberformat.h`): comparator `cmp.exe` replays the
  net48 matrix through the icalls — **251/251 result rows match** (the one
  excluded row, `long.MaxValue G15` = precision-G, is formatext-module
  coverage; the invariant module exposes G/G15 without precision).
- **zh-CN module** (`formatext.cpp`): probe3/probe4 rows asserted in
  `test_formatext.cpp` (P2 NaN "NaN", F2 Inf "∞" codepoint-exact, int
  F2/P1, G-precision, e2/x4 lowercase, E0 rounding) — all pass.
- Build: `cmake --build build/runtime_numberformat --target test_numberformat
  test_formatext` (full `il2cpp_runtime` lib) → both suites pass, exit 0.
  (Earlier g++-standalone build with stubs also passed — the full-lib build
  was briefly blocked by an in-flight agent's timeext2.cpp breakage.)
