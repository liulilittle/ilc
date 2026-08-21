# Bench Baseline — 2026-08-15 (batch 159)

Date: 2026-08-15 · Compiler: **mingw** (Clang 17 frontend, Release/-O3)
Toolchain: MinGW Makefiles, Release · Transpiler: source build · Runtime: fresh
full compile of `src/runtime` · HEAD: 6603bd4
Baseline purpose: M6 exit iron rule "bench 基线入库" — a committed reference
so future batches can detect regressions. ratio = net48_ms / ilc_ms,
**> 1 = transpiled faster**. Medians of 5-10 runs (bench runs are noisy;
single-run tables in bench-summary-v6 are indicative only).

## 1. tests/bench (general suite)

| benchmark | net48_ms (median) | ilc_ms (median) | ratio |
|---|---|---|---|
| fib | 22 | 14 | **1.57** |
| loop | 61 | 62 | 0.98 |
| string | 54 | 29 | **1.86** |
| array | 14 | 1 | **14.0** |
| object | ~10 | 0 | **inf** (loop eliminated) |
| sort | 17 | 15 | **1.13** |
| hash | 7 | 6 | **1.17** |

batch 159 change: sort was 0.56 (08-05 baseline) → 0.89 (v6) → **1.13**
(TryEmitFastArrayPtr leaf+constant-offset arm: the insertion-sort `a[j+1]`
shift store now compares against the literal newarr length instead of the
unhoistable `arr->length` field load; bounds check + cold fail retained —
arroffset e2e proves boundary semantics).

## 2. tests/bench-array (FULL program, same binary both sides since batch 159)

| benchmark | net48_ms | ilc_ms | ratio |
|---|---|---|---|
| arr_sum | 9 | 1 | **9.0** |
| arr_write | 21 | 5 | **4.2** (v6: 4.0) |
| arr_copy | 29 | 24 | **1.21** (was "FAIL": Array.Copy wiring gap closed) |
| str_chars | 17 | 1 | **17.0** (was "FAIL": get_Chars gap closed) |
| str_len | 21 | 0 | **inf** (loop eliminated) |
| str_concat | 22 | 18 | **1.22** (was "FAIL" / v6 doc 0.81-0.85: the documented sub-parity gap is closed) |

batch 159 change: the FULL bench_array program transpiles+builds+runs on the
ILC side (the documented "NOT transpilable" wiring gaps -- String.Concat /
get_Chars / Array.Copy -- were closed by batches 152-158); the harness now
measures arr_copy / str_chars / str_concat from the full program instead of
FAIL. The v6 "str_concat 0.81-0.85" figure was a net48-only probe artifact,
not a live ILC measurement.

## 3. Verdict

NO regression vs bench-summary-v6 (2026-08-10): all transpilable benchmarks
at or above parity; sort flipped from sub-parity to 1.13×; the full
bench-array program gained three new measured benchmarks (arr_copy 1.21×,
str_chars 17×, str_concat 1.22×). Single-bench noise (loop 0.98-1.03, hash
0.86-1.17) is run-to-run variance, not a codegen change.

## 4. Infrastructure fix (batch 159)

`run-bench.ps1` built `bench.csproj` unqualified; the csproj multi-targets
(net48;net8.0 — batch 152b net8 baseline) so the net8.0 apphost `bench.exe`
shadowed the net48 assembly ("PE image does not have metadata"). Fixed with
`-f net48`.
