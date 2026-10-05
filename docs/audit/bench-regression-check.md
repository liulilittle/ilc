# Bench Regression Check — 2026-08-05

Date: 2026-08-05 · Mode: 只读+运行+文档（未改 src/，未 commit）· Compiler: **mingw** (Clang 17.0.6 frontend, Release/-O3)
Toolchain: MinGW Makefiles, Release · Transpiler: source build (`dotnet run --project il2cppc`) · Runtime: fresh full compile of `src/runtime` (both suites)
Baseline: docs/audit/hotpath-audit-2.md §3 (2026-08-03) — ratio = net48_ms / ilc_ms, **>1 = transpiled faster**

## 1. Status summary

| Item | Status | Evidence |
|---|---|---|
| tests/bench suite (7 benchmarks) | PASS | `[BASH:pwsh scripts/run-bench.ps1 -Compiler mingw]` → full table, CSV `tests/bench/results/2026-08-05_0205-mingw.csv` |
| tests/bench-array suite (3 transpilable) | PASS | `[BASH:pwsh scripts/run-bench-array.ps1 -Compiler mingw]` → full table, CSV `tests/bench-array/results/2026-08-05_0208-array-mingw.csv` |
| net48 reference (fresh, both suites) | PASS | both scripts rebuild + run net48 first; repeated re-runs of both exes confirm stability |
| Repeatability | PASS | 3x re-run bench exe + 7x re-run array-core exe + 2x re-run net48 exes — all within noise |

## 2. Result tables

### 2.1 tests/bench (general suite, mingw)

| benchmark | baseline net48/ilc (08-03) | baseline ratio | **new net48/ilc (08-05)** | **new ratio** | delta |
|---|---|---|---|---|---|
| fib | 20/33 | 0.61 | 21/14 | **1.50** | ▲ +0.89 |
| loop | 58/59 | 0.98 | 60/62 | **0.97** | ~ 持平 |
| array | 12/62 | 0.19 | 12/9 | **1.33** | ▲ +1.14 |
| string | 48/3381 | 0.01 | 51/149 | **0.34** | ▲ +0.33 |
| object | 9/1172 | 0.01 | 10/402 | **0.02** | ~ 持平 |
| sort | 15/141 | 0.11 | 15/27 | **0.56** | ▲ +0.45 |
| hash | 6/15 | 0.40 | 7/7 | **1.00** | ▲ +0.60 |

### 2.2 tests/bench-array (core program — same binary both sides, mingw)

| benchmark | baseline net48/ilc (08-03 1607) | baseline ratio | **new net48/ilc (median of 7 runs)** | **new ratio** | delta |
|---|---|---|---|---|---|
| arr_sum | 8/19 | 0.42 | 8/8 | **1.00** | ▲ +0.58 |
| arr_write | 19/42 | 0.45 | 20/24 | **0.83** | ▲ +0.38 |
| str_len | 19/76 | 0.25 | 20/20 | **1.00** | ▲ +0.75 |

Median source `[BASH: 7x both exes]`:
- net48 core: arr_sum 8,8,8,8,8,8,8 · arr_write 20,20,21,20,20,20,20 · str_len 20×7
- ilc core: arr_sum 8×7 · arr_write 24,24,25,24,24,24,24 · str_len 20,20,20,22,20,20,20

net48-only FULL reference (fresh): arr_sum 21, arr_write 27, arr_copy 31, str_chars 14, str_len 22, str_concat 25 ms.

## 3. Verdict: NO regression — six-of-six improved or flat

| requirement metric | baseline (08-03) | new (08-05) | verdict |
|---|---|---|---|
| fib | 0.61 | 1.50 | **提升 2.5x**（纯递归已反超 net48 JIT） |
| loop | 0.98 | 0.97 | 持平（-1%，噪声内） |
| array | 0.19 | 1.33 | **提升 7x**（2.4x慢 → 快 1.33x） |
| arr_sum | 0.42 | 1.00 | **提升**（2.4x慢 → 打平） |
| arr_write | 0.45 | 0.83 | **提升**（2.2x慢 → 1.2x慢） |
| str_len | 0.25 | 1.00 | **提升 4x**（4.0x慢 → 打平） |

**零退化**。全部 6 个受测基准 ≥ 持平，4 个从"慢于 net48"翻转为"≥ 打平/反超"。

## 4. Regression analysis

无退化项。所有改进均可由生成代码形态变化解释（非测量噪声 — 重复运行稳定）：

1. **P0-1 修复落地（array 热路径）**：08-03 审计记录的逐元素 out-of-line 调用
   `il2cpp::il2cpp_array_get_elem_ptr(...)`（`[READ:hotpath-audit-2.md:51-52]`）已消失。
   现生成内联三元式直访 `->data`：
   `(*(int32_t*)(((int32_t)(loc2) >= 0 && ... < 100000LL) ? ((Il2CppArray*)(loc0))->data + ... : (il2cpp::il2cpp_array_fail(...), nullptr))`
   `[READ: tests/bench-array/gen_array_core/src/BenchArr_BenchArr.cpp:41,52,91,95]`。
   内联 + 直接地址运算使 -O3 能消除死分支并向量化。arr_sum 0.42→1.00、arr_write 0.45→0.83、array 0.19→1.33。

2. **P0-2 修复落地（str_len / Len 叶子帧消除）**：08-03 审计记录的
   `Len` 内每调用一个完整 GC 帧（frame push + pop + NOINLINE 调用，4.0x 慢）
   `[READ:hotpath-audit-2.md:65-67]` 已消失。现 `Len` 为无帧裸体：
   `IL2CPP_NOINLINE int32_t BenchArr_BenchArr_Len_...(Il2CppObject* arg0) { return il2cpp::il2cpp_string_get_length(arg0); }`
   `[READ: tests/bench-array/gen_array_core/src/BenchArr_BenchArr.cpp:108-111]`。
   每 20M 次迭代省掉 2 次 out-of-line 帧调用 → str_len 0.25→1.00。

3. **fib 1.50**：纯 int32 递归帧成本下降（与 str_len 同因 — 叶子帧协议瘦身），已反超 net48。

4. **string/sort/hash** 一并受益（0.01→0.34 / 0.11→0.56 / 0.40→1.00）：string 仍受
   net48 的循环不变量提升 vs ILC 每次分配影响（README 已记录为真实优化器差距，非人工产物）。

## 5. Remaining performance gaps (next-step suggestions)

1. **object 0.02 / string 0.34（已知 optimizer-gap 探测，非退化）**：net48 JIT 证明对象
   不逃逸并消除分配/提升循环不变量；ILC 每次迭代真实分配。转译器尚无逃逸分析 +
   分配下沉/LICM。目标：M7 优化器（Normalize.cs 扩展为值域+逃逸分析）。
2. **arr_write 0.83（1.2x 慢，仅剩的 <1 项）**：写循环仍带每元素边界检查三元式；
   Normalize 已知 newarr 长度（100000LL）与循环归纳变量（0 起，`< 100000LL` 止）——
   可证明 `0<=idx<len`，为可证循环发射无检查快路径（hotpath-audit-2 P0-1 建议未全落地）。
3. **int64 噪声**（hotpath-audit-2 P2）：`(int32_t)(100000LL)` / `(loc2 < 100000LL)` 模式仍在
   `[READ:BenchArr_BenchArr.cpp:41,47]`；-O3 能折叠，但影响审计可读性。常量适配 int32 即可。
4. **string 基准的 str_concat/get_Chars/Array.Copy 仍为 net48-only**（wiring gap），
   bench-array FULL 转译仍 FAIL（`[READ:README-array.md:110-125]`）— 落地后 string 基准可回归完整形态。

## 6. Method note

- 测量协议：固定工作量基准 + P/Invoke 时钟 + checksum 沉汇；Release/-O3 双方；同一 exe 重复运行验证稳定性。
- bench-array 比值用 CORE 程序双侧（同二进制）— FULL 程序 net48 数字仅作 6 项参考表。
- 08-03 晚间 results 的 str_len ilc=0ms 记录（2023/2207 CSV）为循环被消除的无效测量；
  本日 str_len 重新测得真实 20ms — 有效（有分配依赖，loop 不可消除）。
- 本会话未改 src/、未 commit；在途 diff（console/guid/concat/formatext/time/StackFold）未受影响。
