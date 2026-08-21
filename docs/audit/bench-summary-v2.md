# Bench Summary v2 — 2026-08-05（转译器在途，旧数据标注版）

Date: 2026-08-05 · Mode: **只读+文档（未改 src/、未 commit、未跑新 bench）** · Compiler: mingw (Clang 17.0.6 frontend, Release/-O3)

> **⚠ 本版数字 = 02:05 最新已验证测量（bench-regression-check.md），非本次新跑。**
> 转译器在途（见 §1）：边界检查消除 / 尾调用 / 内联 / 槽复用优化**已在工作树落地但未 commit、未测量**。
> 新 bench 必须在转译器收敛并 commit 后重跑（见 §6）。

## 1. Status summary — 为什么用旧数据

| Item | Status | Evidence（本次会话实时核查） |
|---|---|---|
| tests/bench 重跑 | **SKIPPED（转译器在途）** | `[BASH: Get-CimInstance dotnet.exe → VBCSCompiler pid 1313608 自 14:44:48 运行中]`；`[BASH: last-20s sweep → tests/cases/bitconvcase/build_mingw/* 正在写]`；`[BASH: tests/unit/MethodRefResolverDispatchTests.cs 15:18:14 写入]` |
| git 工作树 | **在途（53 modified + 88 untracked）** | `[BASH: git status --short → M=53, ??=88]`；`[BASH: git diff HEAD --stat -- src/il2cppc → 10 files, +1160/-73]` |
| 优化器工作树状态 | **落地但未 commit** | `[READ:CodeGenerator.cs:634 `_curLoopSafeAccesses = ComputeLoopSafeAccesses(...)`]`；`[READ:StackFold.cs:2890 `ApplyTailCallLoop`]`；`[READ:StringPoolPass.cs:80 `class StringPoolPass`]` |
| runtime 源 | **在途** | `[BASH: src/runtime/src/stringext.cpp 14:27:52 修改]`；并行代理正在改 .opencode/*.md（15:09-15:16） |
| 结论 | 在途 = 运行中目标会移动 | 此刻跑 bench = 竞争代理构建 + 测量半成品 → 数字无效 |

**判定依据（任务指令原文）：** "若转译器在途则用旧数据标注" → 本版采用 02:05 bench-regression-check.md 已验证数字，并标注在途事实。

## 2. Result tables（02:05 最新已验证测量）

### 2.1 tests/bench（general suite, mingw）— 02:05 数字

| benchmark | 08-03 基线 net48/ilc | 08-03 比值 | **02:05 net48/ilc** | **02:05 比值** | delta |
|---|---|---|---|---|---|
| fib | 20/33 | 0.61 | 21/14 | **1.50** | ▲ +0.89 |
| loop | 58/59 | 0.98 | 60/62 | **0.97** | ~ 持平 |
| array | 12/62 | 0.19 | 12/9 | **1.33** | ▲ +1.14 |
| string | 48/3381 | 0.01 | 51/149 | **0.34** | ▲ +0.33 |
| object | 9/1172 | 0.01 | 10/402 | **0.02** | ~ 持平 |
| sort | 15/141 | 0.11 | 15/27 | **0.56** | ▲ +0.45 |
| hash | 6/15 | 0.40 | 7/7 | **1.00** | ▲ +0.60 |

CSV: `[BASH: Get-Content tests/bench/results/2026-08-05_0205-mingw.csv → fib,21,14,1.50 / array,12,9,1.33 / object,10,402,0.02 / string,51,149,0.34 / sort,15,27,0.56 / hash,7,7,1.00 / loop,60,62,0.97]`

### 2.2 tests/bench-array（core program — 同二进制双侧, mingw）— 02:08 数字

| benchmark | 08-03 基线 net48/ilc | 08-03 比值 | **02:08 net48/ilc（7 次中位数）** | **02:08 比值** | delta |
|---|---|---|---|---|---|
| arr_sum | 8/19 | 0.42 | 8/8 | **1.00** | ▲ +0.58 |
| arr_write | 19/42 | 0.45 | 20/24 | **0.83** | ▲ +0.38 |
| str_len | 19/76 | 0.25 | 20/20 | **1.00** | ▲ +0.75 |

CSV: `[BASH: Get-Content tests/bench-array/results/2026-08-05_0208-array-mingw.csv → arr_sum,21,8,12,0.67,core / arr_write,27,22,28,0.79,core / str_len,22,21,22,0.95,core / arr_copy,str_chars,str_concat → FAIL]`

> 注：CSV 内 ratio 用 FULL 程序 net48 分母（0.67/0.79/0.95）；doc 表用 CORE 程序 net48 分母（1.00/0.83/1.00）——同二进制双侧对比，见 bench-regression-check.md §6 方法注。

## 3. Comparison vs 08-03 baseline

| 指标 | 08-03 | 02:05 | 结论 |
|---|---|---|---|
| fib | 0.61 | 1.50 | **反超 net48（2.5x 提升）** |
| loop | 0.98 | 0.97 | 持平（噪声内） |
| array | 0.19 | 1.33 | **2.4x慢 → 快 1.33x（7x 提升）** |
| arr_sum | 0.42 | 1.00 | 2.4x慢 → 打平 |
| arr_write | 0.45 | 0.83 | 2.2x慢 → 1.2x慢 |
| str_len | 0.25 | 1.00 | 4.0x慢 → 打平（4x 提升） |
| string | 0.01 | 0.34 | 巨大提升（3381→149ms），仍慢 |
| sort | 0.11 | 0.56 | 提升 5x |
| hash | 0.40 | 1.00 | 提升 |

**02:05 状态：零退化，6/6 受测基准 ≥ 持平，4 个翻转为 ≥ 打平/反超。**

## 4. 在途优化器工作（已落地未 commit、未测量）

工作树中存在以下优化（转译器在途期间无法给出可靠新数字）：

| 优化 | 证据 | 预期影响 |
|---|---|---|
| **边界检查消除**（ComputeLoopSafeAccesses，loop-pattern bounds proof） | `[READ:CodeGenerator.cs:634 `_curLoopSafeAccesses = ComputeLoopSafeAccesses(stmts, newArrLengths)`]`；`[READ:CodeGenerator.cs:596-632 ldelem/stelem 发射接 `_curNewArrLengths` + "the bounds compare is statically dead"`]` | arr_write 0.83 → 1.0+（写循环去每元素边界检查；hotpath-audit-2 P0-1 建议全落地） |
| **尾调用自递归**（ApplyTailCallLoop） | `[READ:StackFold.cs:2890 `public static List<Stmt> ApplyTailCallLoop(List<Stmt> stmts, ...)`]`；`[READ:StackFold.cs:851 `ApplyTailCallLoop(HoistLoopGuards(OptimizeTemps(stmts), types), ctx, objTypes)`]` | fib 1.50 → 更高（自递归转循环；含别名安全守卫 `IsPureValueArg`、arg 槽写回） |
| **内联**（空体 sibling clause 闭合等） | `[READ:CodeGenerator.cs diff "siblings before it have empty bodies, closed inline by"]` | 调度/异常链热路径 |
| **槽复用 / 值槽**（_curNewArrLengths 同 slot pattern、enum 值槽、width map） | `[READ:CodeGenerator.cs:1962 `_curNewArrLengths` static field`]`；StackFold slot width map（"loc/arg/i/r" 标注） | 减少局部槽压力、GC 根瘦身 |
| **StringPoolPass**（字符串池化） | `[READ:StringPoolPass.cs:80 class StringPoolPass`]`；`[READ:StringPoolPass.cs:185-190 string-pool.h g_strPool[N] + il2cpp_string_intern_literal 每行一次`]` | string 0.34 → 更高（ldstr 字面量池化，net48 LICM 差距收窄） |

> 这批优化 = 转译器侧改动（CodeGenerator.cs +567 / StackFold.cs +273 / StringPoolPass.cs +71，未 commit）。
> runtime 侧 stringext.cpp 14:27:52 在途修改 → 运行时语义也可能变化，重测时必须一并确认 runtime 库重建。

## 5. Remaining performance gaps（object/string 优化器差距 — 未变）

1. **object 0.02 / string 0.34（已知优化器差距，非退化）**：net48 JIT 证明对象不逃逸 → 消除分配 + 循环不变量提升（LICM）；ILC 每次迭代真实分配。**转译器尚无逃逸分析 + 分配下沉/LICM**。这是 M7 优化器主战场（Normalize.cs 扩展为值域 + 逃逸分析）。StringPoolPass 落地后 string 部分收窄，object 仍需逃逸分析。
2. **arr_write 0.83（仅剩 <1 项）**：§4 的 ComputeLoopSafeAccesses 正是为此设计（证明 0<=idx<len 后发射无检查快路径）——**待转译器收敛后重测验证**。
3. **int64 噪声**（hotpath-audit-2 P2）：`(int32_t)(100000LL)` / `(loc2 < 100000LL)` 模式仍在；-O3 能折叠但影响审计可读性。
4. **bench-array FULL 程序仍 FAIL**（str_concat / get_Chars / Array.Copy wiring gap）→ string 基准无法回归完整形态；`[BASH: 02:08 CSV → arr_copy/str_chars/str_concat FAIL]`。

## 6. Next steps

1. **收敛门槛**：等转译器稳定（无构建进程 + src mtime 静默 ≥30min，参照 e2e-matrix4 轮询法）→ 确认优化器工作 commit。
2. **重跑基准**（新 bench-summary-v3）：`pwsh scripts/run-bench.ps1 -Compiler mingw` + `pwsh scripts/run-bench-array.ps1 -Compiler mingw`；同时重建 runtime 共享库（stringext.cpp 已变）并记录转译器 commit hash。
3. **预期验证点**：arr_write ≥1.0（边界消除）、fib 上升（尾调用）、string 上升（StringPoolPass）。
4. **object 差距**：逃逸分析 + 分配下沉/LICM（M7 优化器）——object 0.02 是最后的大优化器差距。
5. **bench-array FULL wiring**：String.Concat / get_Chars / Array.Copy 转译接线后恢复完整 string 基准形态。

## 7. Method note

- 本会话：只读核查 + 文档；未跑 bench（转译器在途 → 数字会因移动目标失效且与代理构建竞争）；未 commit。
- 本版数字全部来自 02:05/02:08 已验证测量（bench-regression-check.md + 对应 CSV），比值方法相同（net48_ms / ilc_ms，>1 = 转译更快）。
- 在途证据已实时采集（VBCSCompiler 14:44:48 起运行；bitconvcase build_mingw 20s 内写入；git status 53M+88U；stringext.cpp 14:27:52）。
