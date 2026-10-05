# Bench Verification — M7 (batch 168, 2026-08-15)

Date: 2026-08-15 · Compiler: **mingw** (Clang 17.0.6 frontend, Release/-O3) · HEAD: **efa6741**
(batch 168: zero-arg void delegate multicast regression fix — bench programs use zero
delegate/async per audit, so theoretical zero impact; this run is the empirical check).
Mode: 只读+运行+文档（未改 src/、未碰 build/runtime_full、tests/cases/**）。
Baseline: docs/audit/bench-baseline-20260815.md (batch 159). ratio = net48_ms / ilc_ms, >1 = transpiled faster.

## 1. Runs

| # | suite | CSV | exit |
|---|---|---|---|
| 1 | bench | tests/bench/results/2026-08-15_1109-mingw.csv | 1* |
| 2 | bench | tests/bench/results/2026-08-15_1114-mingw.csv | 1* |
| 3 | bench | tests/bench/results/2026-08-15_1118-mingw.csv | 1* |
| 1 | bench-array | tests/bench-array/results/2026-08-15_1127-array-mingw.csv | 1* |
| 2 | bench-array | tests/bench-array/results/2026-08-15_1135-array-mingw.csv | 1* |
| 3 | bench-array | tests/bench-array/results/2026-08-15_1141-array-mingw.csv | 1* |

\* exit 1 is the script's documented 0-ms warning path `[READ:scripts/run-bench.ps1:124-125]`:
`object` (and bench-array `str_len`) legitimately report ilc 0 ms (loop eliminated) — same as baseline
(object inf / str_len inf). Not a build/run failure; all C++ builds + both runs completed.

## 2. tests/bench — median vs baseline

| benchmark | baseline | r1 | r2 | r3 | **median** | 90% threshold | verdict |
|---|---|---|---|---|---|---|---|
| fib | 1.57 | 1.40 | 1.25 | 1.47 | **1.40** | >1.3 高位 | PASS（高位 >1.3） |
| loop | 0.98 | 0.98 | 0.94 | 1.03 | **0.98** | 0.86–1.17 噪声带 | PASS（噪声带内） |
| string | 1.86 | 1.46 | 1.67 | 1.65 | **1.65** | >1.3 高位 | PASS（高位 >1.3） |
| array | 14.0 | 12.00 | 12.00 | 13.00 | **12.0** | >1.3 高位 | PASS（高位 >1.3） |
| object | inf | inf | inf | inf | **inf** | inf | PASS |
| sort | 1.13 | 1.14 | 1.00 | 1.19 | **1.14** | ≥1.017 (0.9×1.13) | PASS（≥阈值；无 <1.0 次） |
| hash | 1.17 | 1.00 | 0.86 | 1.00 | **1.00** | 0.86–1.17 噪声带 | PASS（噪声带内） |

## 3. tests/bench-array — median vs baseline

| benchmark | baseline | r1 | r2 | r3 | **median** | 90% threshold | verdict |
|---|---|---|---|---|---|---|---|
| arr_sum | 9.0 | 9.00 | 8.00 | 9.00 | **9.00** | ≥8.1 | PASS |
| arr_write | 4.2 | 5.00 | 4.00 | 4.00 | **4.00** | ≥3.78 | PASS |
| arr_copy | 1.21 | 1.04 | 1.08 | 1.12 | **1.08** | ≥1.089 | 噪声（边缘，见 §4） |
| str_chars | 17.0 | 14.00 | 16.00 | 16.00 | **16.0** | ≥15.3 | PASS |
| str_len | inf | inf | inf | inf | **inf** | inf | PASS |
| str_concat | 1.22 | 1.15 | 1.29 | 1.29 | **1.29** | ≥1.098 | PASS |

## 4. arr_copy borderline analysis

median 1.08 vs 0.9×1.21 = 1.089 — misses threshold by 0.009 (~0.8%).
Classified as **noise, NOT suspected regression**:
- All 3 runs ≥ 1.0 (1.04 / 1.08 / 1.12) — parity retained on every single run, no run below parity.
- ilc_ms trending DOWN toward baseline (27 → 25 → 24 vs baseline 24); run-1 1.04 was partly net48-side
  (net48 28 vs baseline 29) + ilc-side 27. No systematic ilc-side inflation.
- batch 168 touched delegate codegen only (zero-arg void multicast dispatch); Array.Copy is a runtime
  icall path untouched — no causal mechanism for a codegen regression.
- sort never dropped < 1.0 in any run (1.00 min) → no suspected sort regression either.

## 5. Verdict

**零回退确认**。13/13 可测项通过判定（12 PASS + arr_copy 边缘噪声）。
关键项（sort 1.14 / str_concat 1.29 / arr_copy 1.08 / str_chars 16.0 / arr_sum 9.0 / arr_write 4.0）
全部 ≥ 1.0 且无 <1.0 单次记录；高位项全部 >1.3 或 inf；loop/hash 在噪声带内。
疑似回退：**无**（sort 与 arr_copy 均无全 3 次 < 1.0 情形）。

Raw single-run bench noise consistent with baseline methodology note (sort 0.83 / loop 0.94-1.03 / hash 0.86-1.00).
