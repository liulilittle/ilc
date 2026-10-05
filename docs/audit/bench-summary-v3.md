# Bench Summary v3 — 2026-08-06（转译器在途，旧数据标注版）

Date: 2026-08-06 · Mode: **只读核查+文档（未改 src/、未 commit、未跑新 bench）** · Compiler: mingw (Clang 17.0.6 frontend, Release/-O3)

> **⚠ 本版数字 = 08-05 最新已验证测量（磁盘 CSV 现状），非本次新跑。**
> 转译器在途（见 §1）：**batch-13 优化器工作已 commit（81d25b6）但从未测量**；
> 新一轮 batch（switch 跳表 Ceq 链、StringPoolPass 层重构）**在飞**。新 bench 必须在
> 转译器收敛并 commit 后重跑（见 §6）。

## 1. Status summary — 为什么仍用旧数据（本次会话实时核查）

| Item | Status | Evidence（本次会话 10:20-10:30 实时核查） |
|---|---|---|
| tests/bench 重跑 | **SKIPPED（转译器在途）** | `[BASH:Get-Process dotnet → PID 59340 存活]`；`[BASH:Get-CimInstance → VBCSCompiler dotnet.exe 698528 exec VBCSCompiler.dll 运行中]`；`[BASH:il2cppc.dll bin/Release LastWriteTime 2026/8/6 10:25:32（本会话进行中重建）]` |
| git 工作树 | **在途（53 modified + 88 untracked）** | `[BASH:git status --short → M=53, ??=88]`；`[BASH:git diff --stat -- src/il2cppc → 7 files, +462/-109]` |
| **batch-13 优化器** | **已 commit（81d25b6, 08-06 02:50）** | `[BASH:git grep ComputeLoopSafeAccesses HEAD:CodeGenerator.cs → 6 处]`；`[BASH:git grep ApplyTailCallLoop HEAD:StackFold.cs → 2 处]`；`[BASH:git grep class StringPoolPass HEAD:StringPoolPass.cs → 1 处]` |
| **新 batch 在途（switch 等）** | **落地未 commit** | `[BASH:git status ?? tests/unit/StackFoldTests.Switch.cs（8/6 08:47 新建）]`；`[BASH:git diff StackFold.cs → switchDefault Dictionary + SwitchTargets + "switch（0x45）的跳表目标"]`；StringPoolPass.cs -72/+33（层 2/3 concat 折叠移除重构） |
| runtime 源 | **在途** | `[BASH:src/runtime/src/io.cpp 10:16:45 修改]`；`[BASH:build/runtime_fullcheck7/runtime/libil2cpp_runtime.a 10:23:07 重建]`；git status 13 runtime 文件 modified |
| bench 构建产物 | **陈旧（8/5）** | `[BASH:build/bench_mingw/runtime/libil2cpp_runtime.a 08-05 02:05:12]`；`[BASH:tests/bench/gen/src/* 08-05 02:03:59]` |
| 结论 | 在途 = 运行中目标会移动 | 此刻跑 bench = 竞争代理构建 + 测量半成品（switch 重构中途态）→ 数字无效 |

**判定依据（任务指令原文）：** "若转译器在途则用旧数据标注" → 本版采用磁盘现存最新已验证 CSV
（08-05 02:05 通用套件 / 08-05 15:26 数组套件），并标注在途事实。

## 2. Result tables（08-05 最新已验证测量）

### 2.1 tests/bench（general suite, mingw）— 08-05 02:05 数字

| benchmark | 08-03 基线 net48/ilc | 08-03 比值 | **08-05 net48/ilc** | **08-05 比值** | delta vs 08-03 |
|---|---|---|---|---|---|
| fib | 20/33 | 0.61 | 21/14 | **1.50** | ▲ +0.89 |
| loop | 58/59 | 0.98 | 60/62 | **0.97** | ~ 持平 |
| array | 12/62 | 0.19 | 12/9 | **1.33** | ▲ +1.14 |
| string | 48/3381 | 0.01 | 51/149 | **0.34** | ▲ +0.33 |
| object | 9/1172 | 0.01 | 10/402 | **0.02** | ~ 持平 |
| sort | 15/141 | 0.11 | 15/27 | **0.56** | ▲ +0.45 |
| hash | 6/15 | 0.40 | 7/7 | **1.00** | ▲ +0.60 |

CSV: `[BASH:Get-Content tests/bench/results/2026-08-05_0205-mingw.csv → fib,21,14,1.50 / loop,60,62,0.97 / array,12,9,1.33 / string,51,149,0.34 / object,10,402,0.02 / sort,15,27,0.56 / hash,7,7,1.00]`

### 2.2 tests/bench-array（core program — 同二进制双侧, mingw）— 08-05 15:26 数字（最新磁盘 CSV）

| benchmark | 08-03 基线 net48/ilc | 08-03 比值 | **08-05 net48_core/ilc** | **08-05 比值** | delta |
|---|---|---|---|---|---|
| arr_sum | 8/19 | 0.42 | 8/6 | **1.33** | ▲ +0.91 |
| arr_write | 19/42 | 0.45 | 21/25 | **0.84** | ▲ +0.39 |
| str_len | 19/76 | 0.25 | 21/22 | **0.95** | ▲ +0.70 |

CSV: `[BASH:Get-Content tests/bench-array/results/2026-08-05_1526-array-mingw.csv → arr_sum,16,8,6,1.33,core / arr_write,22,21,25,0.84,core / str_len,21,21,22,0.95,core / arr_copy,str_chars,str_concat → FAIL]`
（08-05 另有 0208/0924 两轮：arr_write 0.83-0.84、str_len 0.95-1.00、arr_sum 1.33 —— 各轮均在 1-2ms 噪声内；v2 表用 7 次中位数 8/8、20/24、20/20 = 1.00/0.83/1.00，与最新轮 1.33/0.84/0.95 同噪声带。）

> 注：CSV ratio 列用 FULL 程序 net48 分母（0.67/0.79/0.95 旧值已过时，磁盘现状为 core 分母
> 1.33/0.84/0.95）；doc 表用 CORE 程序 net48 分母（同二进制双侧对比，见 bench-regression-check.md §6）。

## 3. Comparison vs v2（数字未变 — 无新跑；结构性变化在 §4）

| 指标 | v2 报告 (08-05) | v3 磁盘最新 (08-05) | 结论 |
|---|---|---|---|
| fib | 1.50 | 1.50 | 持平（未新跑） |
| loop | 0.97 | 0.97 | 持平 |
| array | 1.33 | 1.33 | 持平 |
| arr_sum | 1.00（7 次中位数） | 1.33（最新轮） | 噪声带内 |
| arr_write | 0.83 | 0.84 | 持平（噪声内） |
| str_len | 1.00 | 0.95 | 持平（噪声内） |
| string | 0.34 | 0.34 | 持平 |
| sort | 0.56 | 0.56 | 持平 |
| hash | 1.00 | 1.00 | 持平 |
| object | 0.02 | 0.02 | 持平（最后大差距） |

**重要：全部数字均为 batch-13 优化器 commit 之前（08-05）的测量。** 81d25b6（08-06 02:50）
把 v2 §4 的"落地未 commit"优化器工作全部 commit 进 HEAD，但 **commit 后从未跑过 bench** ——
08-05 15:26 之后无任何新 CSV（`[BASH:results 目录无 08-06 文件]`）。

## 4. 自 v2 的结构性变化：优化器 batch-13 已 commit，但未测量

| 优化（v2 §4 列表） | v2 状态 | **v3 状态** | 证据（HEAD 核查） |
|---|---|---|---|
| **边界检查消除**（ComputeLoopSafeAccesses） | 落地未 commit | **已 commit 未测量** | `[BASH:git grep HEAD:CodeGenerator.cs → 6 处]` |
| **尾调用自递归**（ApplyTailCallLoop） | 落地未 commit | **已 commit 未测量** | `[BASH:git grep HEAD:StackFold.cs → 2 处]` |
| **内联**（InlinePass）+ LICM（LicmPass） | 落地未 commit | **已 commit 未测量** | commit 81d25b6 message: "+ TempSlotPass + InlinePass + LicmPass" |
| **槽复用**（TempSlotPass） | 落地未 commit | **已 commit 未测量** | 同上；工作树另有 ptr 宽度补丁在飞（`[BASH:git diff TempSlotPass.cs → +"ptr" => c.ResultKind]`） |
| **StringPool T3**（g_strPool 静态表） | 落地未 commit | **已 commit 未测量** | `[BASH:git grep HEAD:StringPoolPass.cs → 1 处]` |

**新在途 batch（尚未 commit，继续阻塞重测）：**
| 工作 | 证据 |
|---|---|
| **switch 跳表发射**（StackFold 0x45 Ceq 比较链 + switchDefault 字典 + 新单测） | `[BASH:git diff StackFold.cs → switchDefault/SwitchTargets/switch（0x45）]`；`[BASH:?? tests/unit/StackFoldTests.Switch.cs 08-06 08:47]` |
| **StringPoolPass 层重构**（移除层 2 concat 常量折叠，72 删 33 增） | `[BASH:git diff StringPoolPass.cs → 头注释 "layer 1" 保留、FoldConcatConstants 移除]` |
| **cctorchain 守卫 + 静态 ref 槽修复**（CodeGenerator.cs +293/-32，本域 8/6 上午） | `[BASH:git diff --numstat CodeGenerator.cs → 293 32]` |
| **MethodMap 补充**（Crypto/Type/RefResolver） | `[BASH:git status → MethodMap.Crypto.cs/Type.cs/MethodRefResolver.cs M]` |
| **runtime 在途**（io.cpp 10:16:45、gc.cpp、stringbuilder.cpp 等 13 文件） | `[BASH:git status -- src/runtime → 13 M]` |

**共享库重建现状**：runtime 共享库今天已重建（`[BASH:build/runtime_fullcheck7/runtime/libil2cpp_runtime.a 10:23:07]`，
含 io.cpp 10:16:45 后的版本），但 **bench 专用构建目录（build/bench_mingw）仍是 08-05 产物** ——
bench 重跑时会经 cmake 自动从 src/runtime 重建自己的 runtime 副本，无需手工步骤；
真正的前置条件是**转译器收敛**（§6 门槛 1）。

## 5. Remaining performance gaps（object 逃逸分析 — 未变，且仍是最后大项）

1. **object 0.02 / string 0.34（已知优化器差距，非退化）**：net48 JIT 证明对象不逃逸 →
   消除分配 + 循环不变量提升（LICM）；ILC 每次迭代真实分配。**转译器尚无逃逸分析 +
   分配下沉/LICM**（Normalize.cs 扩展为值域 + 逃逸分析 = M7 优化器主战场）。StringPool T3
   已 commit 但未测量 —— string 预计收窄，object 必须靠逃逸分析。
2. **arr_write 0.84（08-05 数字，边界检查消除 commit 前）**：81d25b6 的 ComputeLoopSafeAccesses
   正是为此设计（证明 0<=idx<len 后发射无检查快路径）——**待收敛后重测验证，预期 ≥1.0**。
3. **int64 噪声**（hotpath-audit-2 P2）：`(int32_t)(100000LL)` / `(loc2 < 100000LL)` 模式仍在；
   -O3 能折叠但影响审计可读性。
4. **bench-array FULL 程序仍 FAIL**（str_concat / get_Chars / Array.Copy wiring gap）→ string
   基准无法回归完整形态；`[BASH:08-05 1526 CSV → arr_copy/str_chars/str_concat FAIL]`。
5. **switch 基准缺失**：switch 优化在飞，bench.cs 尚无 switch 基准项 —— 落地后可加
   （如 switch-case 分派 workload）以量化 0x45 跳表收益。

## 6. Next steps

1. **收敛门槛**：等 switch batch + StringPoolPass 重构 commit（无构建进程 + src mtime 静默 ≥30min，
   参照 e2e-matrix4 轮询法）。**当前不满足：10:25:32 仍有构建活动。**
2. **重跑基准**（新 bench-summary-v4）：`pwsh scripts/run-bench.ps1 -Compiler mingw` +
   `pwsh scripts/run-bench-array.ps1 -Compiler mingw`；记录转译器 commit hash（应 ≥81d25b6）
   与 runtime 重建时刻。**这是 batch-13 优化器的首次测量。**
3. **预期验证点**（v2 §6 原预测，至今未测）：arr_write ≥1.0（边界消除）、fib 上升（尾调用，
   v2 时 1.50 已反超，自递归转循环应更高）、string 上升（StringPool T3 池化）。
4. **object 差距**：逃逸分析 + 分配下沉/LICM（M7 优化器）——object 0.02 是最后的大优化器差距。
5. **bench-array FULL wiring**：String.Concat / get_Chars / Array.Copy 转译接线后恢复完整 string
   基准形态；可选新增 switch 基准项。

## 7. Method note

- 本会话：只读核查 + 文档；未跑 bench（转译器在途，10:25:32 仍有构建活动 → 数字会因移动目标失效
  且与代理构建竞争）；未 commit。
- 本版数字全部来自磁盘现存最新已验证 CSV（通用 08-05 02:05、数组 08-05 15:26），比值方法相同
  （net48_ms / ilc_ms，>1 = 转译更快）；数组套件用 CORE 程序双侧。
- 在途证据本次会话实时采集：VBCSCompiler 698528 存活、il2cppc.dll 10:25:32 重建、io.cpp 10:16:45、
  StackFoldTests.Switch.cs 08:47 新建、git 53M+88U、src/il2cppc 7 文件 +462/-109。
- 与 v2 的关键差异：v2 报告"优化器落地未 commit"；**v3 确认其已 commit（81d25b6）但仍未测量**，
  且新一批 switch/StringPoolPass 重构在飞 —— 重测条件从"等 commit"变为"等 switch batch commit"。
