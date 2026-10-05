# ILC 项目完整进度快照（2026-08-13 终版，基线 HEAD fc1ae82 / 批次 147 工作区 — 全量终验 + stackqueue2 GC 修复权威状态）

> 本文档是会话删除后的**唯一权威状态来源**。恢复时先读本文档，再读：
> docs/alignment-tracker-v3.md（域接线）、docs/roadmap-status-v6.md（里程碑/bench 趋势）、
> docs/audit/e2e-full-matrix7.md（矩阵7 终版 195/195=100%）、**docs/audit/e2e-full-matrix8.md（批次 147 全量重验证 + stackqueue2 修复）**、
> docs/audit/cross-platform-m8.md（M8 121×2 + NDK S1 全量矩阵）、
> docs/audit/bench-summary-v6.md（bench v6 基线）+ tests/bench/results/2026-08-12_1429-mingw.csv（bench v8 零回退）、
> docs/plans/m8-real-machine-plan.md（M8 S2 实机计划）、docs/audit/multidim-probe.md（多维数组记录级缺口）。
> 上一快照 = docs/project-state-20260810.md（HEAD 8d865d2 / 批次 69，保留不动）。

## 1. 项目定位（铁律，不可违背）

- **正式名 ILC**（IL→C++ AOT 编译器），**不是 il2cpp**。命名纠正方案见 docs/architecture/rename-ilc-plan.md。
- 目标：对齐 .NET Framework 4.8 行为（M1-M8 全部里程碑）。
- **绝对原则**：实现任何 .NET 语义前**必须先抄 .NET Core 开源实现**（本地参考 `E:\il2cpp\reference\netcore\` + `reference\corert\`；映射表 docs/netcore-reference-guide.md）。
- **跨平台铁律**：IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 接口；已落地**四平台零告警**（MinGW/MSVC/WSL/NDK）+ Apple/OpenBSD 交叉语法 121×2 + M8 S1（NDK-arm64）+ S2 大部分。
- **性能铁律**：bench v8（2026-08-12 实测）**object/str_len inf（循环消除）+ array 12-16× + string 1.8-2.0× + fib 1.8×——零回退（v6/v7 全保持）**。
- **主代理角色**：监工/调度——只拆任务派发并行子代理（用户要求**上百并发/批**，PRD#12）。

## 2. git 状态

- 分支 `m1`，**HEAD = fc1ae82**（2026-08-13 07:47，批次 146：e2e C84 gcweak2/parsefloat/arrayjoin 3/3 双编译器 + **全量终验**）；`git rev-list --count f36f386..HEAD` = **172 commits**；仓库总提交 279。
- **批次 147 工作区（未提交）**：stackqueue2 GC 安全缺陷修复（src/runtime/src/stackqueue.cpp + arraylist.cpp + sortedlist.cpp + 新共享头 include/internal/il2cpp_gcroot.h）+ 全量重验证 + 文档（docs/audit/e2e-full-matrix8.md / remaining-gaps.md 更新 / README 更新）。详见 e2e-full-matrix8.md。
- 最近批次链（新→旧）：fc1ae82(批次 146 e2e C84 + full final verification 1800/1800, 139/139, 20-key e2e PASS) → 745a360(批次 145 ThenBy/ThenByDescending fused sort D-LO2-1 + e2e C83) → 2a23926(批次 144 array type names D-TYPG-1 + e2e C82) → 0dc67d5(批次 143 TakeWhile/SkipWhile D-LT-2 + e2e C81) → 2f387f3(批次 142 decimal.Round mode guard D-DR-1 + e2e C80) → … → 7765986(docs: matrix7 FINAL 100% 195/195, HEAD cef2bdd)。
- **矩阵7 终验 HEAD = cef2bdd**（M6 批次 87，excext/timeext 修复后 195/195=100%）；此后批次 88-146 新增 e2e 用例与修复全部保持正确性口径。

## 3. 当前数字（2026-08-13 快照，全部本快照实测或既有权威报告）

| 指标 | 数值 | 证据 |
|---|---|---|
| 转译器 unit | **1800/1800（100%）** | fc1ae82 HEAD msg "1800/1800" + %TEMP%\opencode\fullfinal-b23-report.md §1 dotnet test 1800/1800 |
| runtime 测试 | **139/139 PASS（100%）** | fc1ae82 msg "139/139" + fullfinal §2 sweep 139/139 + tests/runtime test_*.cpp 计数 139 |
| e2e 矩阵7 | **195/195 = 100%**（196 口径，virtbench 为 bench 不计正确性） | docs/audit/e2e-full-matrix7.md + 7765986 "matrix7 FINAL 100%" |
| e2e 矩阵8（批次 147） | **全量重验证 397 用例**：stackqueue2 真缺陷已修复，修复后正确性 100%（build_asan 伪用例 + virtbench bench 不计） | docs/audit/e2e-full-matrix8.md |
| e2e 用例目录 | **397**（tests/cases/）；用例面 **420+** | [BASH:tests/cases 目录计数 397 / csproj 计数 420+] |
| e2e 全量实跑 | **246 PASS / 4 FAIL = 98.4%**（HEAD cef2bdd 口径；FAIL 全为已知非回归缺口：virtbench bench + decfmt2/resmgmt/resops） | e2e-full-matrix7.md 汇总 |
| e2e 关键抽查（批次 146） | **20 case × 2 编译器 = 40/40 PASS** + WSL GCC7.5 3/3（linqcase/dictcase/gcstatic3） | fullfinal-b23-report.md §3/§4 |
| 性能 bench v8 | **零回退**：object/str_len inf（循环消除）、array 12-16×、string 1.69-2.04×、fib 1.77-1.92×、hash 1.14-1.33×、loop 1.02-1.08×、sort 0.89-1.00（计时噪声） | tests/bench/results/2026-08-12_1417 + _1429-mingw.csv + db45871 "bench v8 zero-regression recorded" |
| 三编译器 | **MinGW/MSVC/WSL GCC7.5 全绿**（批次 146 抽查 40/40 + 3/3）；NDK arm64 S1 链接级；Apple/OpenBSD 交叉语法 121×2 | fullfinal §3/§4 + cross-platform-m8.md |
| 提交批次 | 146+ 批次，172 commits（f36f386..HEAD） | [BASH:git rev-list --count f36f386..HEAD = 172] |

## 4. 里程碑完成状态（M1-M8）

- **M1-M4 ✅**：IL 解码/IR 栈折叠/CodeGenerator/异常 EH/入口；P/Invoke/Marshal/C API；WinForms 阶段 1-13 + COM stage 1-6 双编译器 byte-exact。
- **M5 BCL 全量 ✅（矩阵7 100%）**：矩阵7 196 口径**正确性 195/195 = 100%**（HEAD cef2bdd 全量实跑）；全量 250 目录 246 PASS / 4 FAIL（98.4%），剩余 FAIL 全为既有已知缺口（virtbench bench + decfmt2 自定义占位符 + resmgmt/resops 未接线），**无 codegen FAIL、无回归**。
- **M6 测试+CI ✅**：unit **1800/1800** + runtime **139/139** + e2e 全量实跑 + 三编译器 + WSL GCC7.5；去虚化 S1-S3 + GC 根系统（静态字段 add_static_root / RootSlotPass / hglobal）+ decimal 4 缺陷 + LINQ 深层（ThenBy 融合排序）等全部落地。
- **M6.5 跨平台 ✅**：四平台零告警（MinGW/MSVC/WSL/NDK）+ Apple/OpenBSD 121×2 全 PASS；残项 P3 剩 8 项。
- **M7 性能 ✅**：bench v8 零回退（v6/v7 全保持）。
- **M8 🟡（S1+S2 大部分完成）**：NDK-arm64 S1 121/121 compile + 135/135 link + G2 getrandom PASS；WSL mylib.so dlopen PASS；framestack thread_local；decimal UB 修复；COM stage 1-6；CLDR 文化回退原语（RM-CULT）。**剩余 macOS/OpenBSD 实机编译链接验证（外部实机依赖）**——计划 docs/plans/m8-real-machine-plan.md。

## 5. 硬性原则 PRD（19 条，逐条落地状态见 project-state-20260810.md §5——本快照全部保持）

- PRD#11 性能强制：bench v8 零回退兑现。
- PRD#12 子代理并发硬性原则：每批 ≥32 并行；PRD#15 任务超时纪律：bash 10 分钟硬超时。
- PRD#16 fnptr 全链 + PRD#17 COM stage 1-6 + PRD#18 资源嵌入（RM-CULT 回退链）已落地。

## 6. 剩余缺口记录（施工单输入）

**P0（M8 里程碑收口）**：
1. **M8 S2 macOS/OpenBSD 实机编译链接验证**——外部实机依赖（本机仅有 MinGW/MSVC/WSL/NDK 交叉链路；Apple/OpenBSD 目前仅 -fsyntax-only 语法级 121×2）。前置计划 docs/plans/m8-real-machine-plan.md 已入库。
2. **多维数组记录级**——`docs/audit/multidim-probe.md`：3 道墙（StackFold.cs:2170 / MethodSignatureCoder.cs:103 / MethodBodyReader.cs:193）均为记录级已知缺口，非实现级；用户内存安全铁律（2026-08-11）要求仅显式 struct 字段、无 tail blocks（MD2-IMPb 已 revert a52210a）。
3. **记录级缺陷清单**——2026-08-13 批次 147 全量复核后：virtbench（bench，不计正确性）；**decfmt2/resmgmt/resops 已解决**（复核 + 隔离重跑 PASS，批次 88-146 期间修复）；**stackqueue2 已解决（批次 147，见矩阵8）**；跨平台 P3 剩 8 项；CAST-1 补丁 45 处待应用；runtime 域审计 ~109/110 文件。

**P1/P2**：e2e 扩量持续（C84+）；WinForms 阶段 14 / COM stage 7；反射 trimming L0/L1/L2（T301-T310）；M8 S2 实机；完整 CLDR 父表；rename-ilc-plan 层 1-4；bench 基线入库 + 4 平台 CI routine。

## 7. 环境/工具备忘

- 转译器：`dotnet build src/il2cppc/il2cppc.csproj -c Release`（0 错 0 警门）。
- unit：`dotnet test tests/unit -c Release`（1800/1800）。
- runtime：cmake + sweep build/tests_runtime test_*.exe（139 个，exit 0=PASS）。
- e2e 单用例：`pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>`（net48 oracle → 转译 → cmake → 逐字节 diff）；4 组并行可安全批量跑。
- **bash 10 分钟硬超时**：长任务代理拆分命令（timeout ≤540000ms）。
- 在途判定：编辑前查文件 mtime（<15min=在途）；e2e 前需 il2cppc.dll 新于全部源 + 共享库重建。
- **2026-08-13 环境事件（网络恢复前生效）**：NuGet 全局包缓存被清空 + 出站 HTTPS 全阻断 → `dotnet build`（隐式 restore）不可用。**离线验证路径**：用例 out/ 现成 net48 exe → `dotnet src/il2cppc/bin/Release/net8.0/il2cppc.dll <exe> <gen>` 直跑转译 → cmake → 运行 → 分列 diff；unit 用 `dotnet test --no-restore --no-build`。网络恢复后重跑 run-tests.ps1 全量复核。
- 全量终验方法：%TEMP%\opencode\fullfinal-b23-report.md（批次 145，HEAD=745a360）+ fullfinal-cp.md。

## 8. 恢复后第一动作（5 步）

1. `git status` + `git log -5` 确认 HEAD=fc1ae82 + 工作树干净。
2. `dotnet test tests/unit -c Release` 确认 1800/1800。
3. runtime sweep 139/139 复核 + 三编译器抽查。
4. M8 S2 macOS/OpenBSD 实机前置准备（m8-real-machine-plan.md）+ 多维数组施工单排序。
5. 汇总剩余缺口（§6）→ 派发下一批（≥32 代理）。
