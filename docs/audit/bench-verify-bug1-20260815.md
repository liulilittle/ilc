# Bench Verification — BUG-1 (batch 172, 2026-08-15)

Date: 2026-08-15 · Compiler: **mingw** (Clang 17.0.6 frontend, Release/-O3) · HEAD: **f8ce1cef3**
(batch 172: BUG-1 void-N-int-arg multicast delegate fix — DelegateSignature/i4 routing. Bench
programs use zero delegates per audit, theoretical zero impact; this run is the empirical check).
Mode: 只读+运行+文档（未改 src/、未碰 build/runtime_full、tests/cases/**）。
Baseline: docs/audit/bench-baseline-20260815.md (batch 159) · 上次验证: docs/audit/bench-verify-m7-20260815.md.
ratio = net48_ms / ilc_ms, >1 = transpiled faster.

## 1. Runs

| # | suite | CSV | exit |
|---|---|---|---|
| 1 | bench | tests/bench/results/2026-08-15_1502-mingw.csv | 1* |
| 2 | bench | tests/bench/results/2026-08-15_1504-mingw.csv | 1* |
| 3 | bench (confirm) | tests/bench/results/2026-08-15_1513-mingw.csv | 1* |
| 1 | bench-array | tests/bench-array/results/2026-08-15_1508-array-mingw.csv | 1* |
| 2 | bench-array | tests/bench-array/results/2026-08-15_1511-array-mingw.csv | 1* |
| 3 | bench-array (confirm) | tests/bench-array/results/2026-08-15_1517-array-mingw.csv | 1* |

\* exit 1 is the script's documented 0-ms warning path `[READ:scripts/run-bench.ps1:124-125]`:
`object` (and bench-array `str_len`) legitimately report ilc 0 ms (loop eliminated) — same as baseline
(object inf / str_len inf). Not a build/run failure. Confirmation runs 3 were added because run-2
showed single-run dips (sort 0.61 / arr_copy 0.91 / str_chars 8.0) that needed a fluke-vs-systematic
distinction — run-3 restored typical values in all three (see §4).

## 2. tests/bench — median vs 上次验证 (m7)

| benchmark | baseline | 上次验证(m7) | r1 | r2 | r3 | **median** | threshold | verdict |
|---|---|---|---|---|---|---|---|---|
| fib | 1.57 | 1.40 | 2.14 | 1.43 | 1.53 | **1.53** | >1.3 高位 | PASS（高位 >1.3） |
| loop | 0.98 | 0.98 | 1.06 | 0.93 | 1.02 | **1.02** | 0.86–1.17 噪声带 | PASS（噪声带内） |
| string | 1.86 | 1.65 | 2.06 | 1.63 | 1.79 | **1.79** | >1.3 高位 | PASS（高位 >1.3） |
| array | 14.0 | 12.0 | 14.00 | 16.00 | 12.00 | **14.0** | >1.3 高位 | PASS（高位 >1.3） |
| object | inf | inf | inf | inf | inf | **inf** | inf | PASS |
| sort | 1.13 | 1.14 | 1.64 | 0.61 | 1.19 | **1.19** | ≥1.0（无系统性 <1.0） | PASS（median ≥1.0；r2 单次 fluke） |
| hash | 1.17 | 1.00 | 1.17 | 1.00 | 1.00 | **1.00** | 0.86–1.17 噪声带 | PASS（噪声带内） |

## 3. tests/bench-array — median vs 上次验证 (m7)

| benchmark | baseline | 上次验证(m7) | r1 | r2 | r3 | **median** | threshold | verdict |
|---|---|---|---|---|---|---|---|---|
| arr_sum | 9.0 | 9.00 | 10.00 | 9.00 | 9.00 | **9.00** | ≥8.1 (0.9×9.0) | PASS |
| arr_write | 4.2 | 4.00 | 4.60 | 5.00 | 4.40 | **4.60** | ≥3.6 (0.9×4.0) | PASS |
| arr_copy | 1.21 | 1.08 | 1.23 | 0.91 | 1.21 | **1.21** | ≥0.972 (0.9×1.08) | PASS（median 回升到 ≥ 上次） |
| str_chars | 17.0 | 16.0 | 18.00 | 8.00 | 17.00 | **17.0** | >1.3 高位 | PASS（高位 >1.3） |
| str_len | inf | inf | inf | inf | inf | **inf** | inf | PASS |
| str_concat | 1.22 | 1.29 | 1.39 | 1.27 | 1.47 | **1.39** | ≥1.161 (0.9×1.29) | PASS |

## 4. 单次 dip 分析（sort r2 / arr_copy r2 / str_chars r2）

r2 的三个 dip 均为**单次噪声，非系统性回退**，r3 立即回到典型值：
- **sort 0.61** (r2: net48 19 / ilc 31): r1=1.64, r3=1.19 — ilc_ms 31→16 回落；median 1.19 ≥ 1.0。
  batch 172 只改委托 i4 路由，sort 无委托/无该路径，无因果机制（`[BASH:git log]` f8ce1cef3）。
- **arr_copy 0.91** (r2: net48 29 / ilc 32): r1=1.23, r3=1.21 — ilc_ms 32→24 回落；median 1.21。
  Array.Copy 是 runtime icall 路径，委托改动不触达。
- **str_chars 8.0** (r2: net48 16 / ilc 2): r1=18, r3=17 — ilc_ms 2→1 回落；median 17.0。
  3/4 次 = 17-18；r2 是 ilc 侧单次 2ms 异常。

## 5. Verdict

**零回退确认**。13/13 可测项全部通过判定（全部 PASS，无边缘项）。
- 高位项：fib 1.53 / string 1.79 / array 14.0 / object inf / str_len inf — 全部 >1.3 或 inf。
- 关键项：sort 1.19（median ≥1.0，仅 r2 单次 fluke）/ str_concat 1.39 / arr_copy 1.21（≥ 上次的 90%）/
  arr_sum 9.00 / arr_write 4.60 — 全部达标且 ≥1.0。
- 噪声带：loop 1.02 / hash 1.00 — 均落在 0.86–1.17。
- BUG-1 (batch 172) 委托路由改动**无性能回退**（理论零影响获实测确认：全部 13 项 median 均 ≥ 上次验证或高位达标）。
疑似回退：**无**。
