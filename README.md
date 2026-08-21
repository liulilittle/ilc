# IL2CPP（自研，迷你版）

**C# IL → C++ 静态转译器**：读取 .NET 程序集的 PE/IL，把 IL 指令翻译为 C++ 源码，配合自研 C++ 运行时，由 MSVC / MinGW / WSL GCC 编译为原生可执行文件。零 JIT、零第三方依赖、三编译器验证。

> 系统性工程研究项目，分层复刻 Unity IL2CPP 的思路，用于深挖 .NET 运行机制。

## 当前状态（批次 147 工作区，基线 HEAD=fc1ae82，2026-08-13）

| 指标 | 数值 |
|---|---|
| 转译器 unit | **1800/1800（100%）** |
| runtime 测试 | **139/139（100%）** |
| e2e 矩阵7 | **195/195 = 100%**（196 口径，virtbench 为 bench 不计正确性） |
| e2e 矩阵8 | **批次 147 全量重验证 397 用例**：stackqueue2 真缺陷（GC 压缩下 icall 裸指针分裂）**已修复**，修复后正确性 100%（剩余 build_asan 伪用例 + virtbench bench 不计）；详见 `docs/audit/e2e-full-matrix8.md` |
| e2e 用例 | **397 目录 / 420+ 用例**（新增 comparerdeep/stackqueue2/stringreplace） |
| 性能 bench v8 | **零回退**（object/str_len inf、array 12-16×、string 1.8-2.0×、fib 1.8×） |
| 编译器 | **三编译器全绿**（MinGW/MSVC/WSL GCC7.5）+ NDK 链接级 + Apple/OpenBSD 语法级 |
| 提交批次 | 146+ 批次，172 commits（f36f386..HEAD）；批次 147 待提交 |

> 权威状态快照：`docs/project-state-20260813.md`（本批次更新）。历史快照：
> `docs/project-state-20260810.md`（HEAD 8d865d2 / 批次 69）。

## 架构概述

```
C# 源码 ──dotnet build──▶ net48 程序集（PE/IL）
                          │
                          ▼
                  src/il2cppc（转译器，C#）
   PE ─▶ 元数据模型 ─▶ IL 解码 ─▶ 栈→IR 折叠 ─▶ C++ 代码生成
                          │
                          ▼
       可编译 CMake 工程（CMakeLists.txt + src/*.cpp + include/internal/*.h）
                          │  cmake + 三编译器之一
                          ▼
       原生可执行文件 ──与 .NET Framework 4.8 参考输出逐字节对比──▶ 通过
```

- **转译器（`src/il2cppc`）**：C# + `System.Reflection.Metadata`。流水线为 `PE → 元数据模型 → IL 解码 → 栈→IR 折叠 → C++ 代码生成`，输出自包含的 CMake 工程（含自动生成的 `CMakeLists.txt`、`src/*.cpp`、`include/internal/*.h`）。
- **运行时（`src/runtime`）**：自研 C++ 运行时（`il2cpp::runtime`），以静态库形式链接进最终产物。当前 M1 提供 Console icall 支撑。
- **测试（`tests/`）**：
  - `tests/unit`：转译器单元测试（xunit，**1800/1800 全绿**）。
  - `tests/fixtures`：`calc` / `convw` 参考程序集（net48，元数据/icall 解析测试用）。
  - `tests/cases`：端到端用例（**394 目录 / 420+ 用例**），逐字节对比 .NET Framework 4.8 参考输出与三编译器转译产物。
  - `tests/runtime`：运行时 C++ 测试（**139/139 PASS**）。

## 快速开始

环境要求：dotnet SDK 8.0+、CMake 3.10+、MSVC（VS2022）、MinGW、WSL（Ubuntu 1804，GCC 7.5）、PowerShell 7（pwsh）。

```powershell
# 1. 构建转译器
pwsh -NoProfile -File scripts/build-transpiler.ps1

# 2. 构建测试 fixture（tests/fixtures/out）
pwsh -NoProfile -File scripts/build-fixtures.ps1

# 3. 端到端测试（6 用例，三编译器任选，net48 参考 vs 转译产物逐字节 diff）
pwsh -NoProfile -File scripts/run-tests.ps1 -Compiler mingw   # 或 msvc / wsl

# 4. 一键全量 CI：构建转译器 + 单元测试 + 三编译器端到端（全绿输出 "=== CI PASS ==="）
pwsh -NoProfile -File scripts/ci.ps1
```

单用例手跑：`tests/cases/<name>/` 内的 C# 程序经 `run-tests.ps1` 自动完成
「net48 编译 → 参考运行 → il2cppc 转译 → 三编译器构建 → 逐字节 diff」。

## 里程碑状态

| 里程碑 | 状态 | 内容 | 验收 |
|---|---|---|---|
| M1 转译器骨架 | [x] | PE→IL→纯计算程序（icall 支撑 Console），三编译器 e2e 全绿 | Hello World 级正确输出 |
| M2 对象模型+GC | [x] | 对象模型+类型表+精确根扫描+标记清除 GC+类/继承/接口/虚方法/装箱+帧根协议 | 面向对象程序双跑一致 |
| M3 内存+异常 | [x] | 分代 GC（Gen0/1/2+LOH+压缩+写屏障卡表）+ 异常体系+帧记录+string/array/泛型/委托/值类型 | 异常/泛型/GC 行为测试全过 |
| M4 互操作+产物 | [x] | P/Invoke+Marshal 全套+icall 平台层+C API（WinForms 阶段 1-13 + COM stage 1-6 双编译器 byte-exact） | C++ 调用库演示通过 |
| M5 BCL 全量 | [x] | **矩阵7 195/195 = 100%**（HEAD cef2bdd，decfmt2/resmgmt/resops 3 项为既有已知缺口不在 196 口径） | BCL 行为测试全绿 |
| M5.5 裁剪验证 | [ ] | L1 闭合证明+双模式测试；L2 按需开放 | 裁剪开/关测试一致 |
| M6 测试+CI+文档 | [x] | **unit 1800/1800 + runtime 139/139 + e2e 394 目录全量实跑 246 PASS/4 FAIL（98.4%，FAIL 全为已知非回归缺口）+ 三编译器（MinGW/MSVC/WSL GCC7.5）全绿** | 验收完成 |
| M6.5 跨平台 | [x] | **四平台零告警（MinGW/MSVC/WSL/NDK）+ Apple/OpenBSD 交叉语法 121×2 全 PASS** | 多平台编译通过 |
| M7 性能 | [x] | **bench v8 零回退**：object/str_len inf（循环消除）+ array 12-16× + string 1.8-2.0× + fib 1.8× + 去虚化 S1-S3 | 性能超越 net48 |
| M8 全球化/COM（远期） | [ ] | 完整 CLDR 全球化 / COM 互操作；S1+S2 大部分已完成（NDK-arm64 121/121 + 135/135 link + WSL dlopen PASS）——**剩余 macOS/OpenBSD 实机编译链接验证（外部依赖）** | 对应域测试通过 |

**M1 范围（已完成）**：纯计算 C# 子集 —— 基础类型算术/比较/位运算、if/switch、for/while/do-while/continue/break、静态方法调用与递归、静态字段 `.cctor` 初始化、`unchecked` 环绕。

**M2 范围（已完成）**：对象模型（`Il2CppObject` 对象头 + `Il2CppClass` 类型描述符：name/instanceSize/refFieldBitmap/baseClass/vtable/interfaces）+ 标记清除 GC（段分配/自由链复用/精确位图扫描/静态根/根区间/帧根协议）+ 类发射（struct 布局/实例字段/构造/newobj/引用字段跨类型）+ 惰性 cctor（beforefieldinit 守卫）+ 虚方法 vtable 分派 + 接口映射分派 + 装箱/拆箱 + isinst/castclass + null 检查。

**M3 范围（已完成）**：分代 GC（Gen0/1/2 晋升 + LOH 独立段 + Gen0 压缩收集 + 写屏障卡表）+ 托管 string（UTF-16 布局）+ 数组（边界检查/引用元素 GC 扫描）+ 异常体系（DotNetException/try-catch-finally/除零检查/顶层防护/帧栈报告）+ 帧记录（帧栈 = GC 安全点 = 根扫描 + RAII pop）+ 泛型值类型实例化（Box_int，零 C++ template）+ 委托（Func 单播）+ 值类型（struct 无头/原地构造/值拷贝）。**不含**（M3.5+）：值类型 box/unbox/Nullable、引用共享泛型、实例方法委托/多播、完整 StackTrace/反射元数据。

## 约束摘要

- **C++17 仅，兼容基线 GCC 7.5**：运行时与生成代码全部 C++17（`cxx_std_17`），禁止 C++20/23 特性；代码必须可被 GCC 7.5 编译运行正确（WSL Ubuntu 1804 为基线发行版）。
- **CMake 3.10 最低版本**：全部 CMakeLists 用 `cmake_minimum_required(VERSION 3.10)`；C++ 构建统一走 cmake（唯一构建系统，目标平台 = 换生成器），转译器用 `dotnet build`。
- **三编译器验证**：MSVC（VS2022）/ MinGW / WSL GCC 7.5 全链路构建 + 运行逐字节一致（`scripts/ci.ps1` 一键全绿）。当前批次（146，HEAD=fc1ae82）：全量终验 unit 1800/1800 + runtime 139/139 + 20 关键 e2e 双编译器 40/40 PASS + WSL GCC7.5 抽查 3/3 PASS。
- **零 JIT / 零第三方依赖**：全链路纯 AOT，无动态代码生成；GC/元数据/string/集合全部自研。
- **生成代码风格规范**：`docs/superpowers/specs/2026-08-02-generated-code-style.md`（4 空格缩进、注释右对齐 100 列、全英语超详细注释、确定性输出，CI 质量门）。

## 相关文档

- 当前权威状态快照：`docs/project-state-20260813.md`（批次 146）/ 历史快照 `docs/project-state-20260810.md`
- 剩余缺口记录：`docs/audit/remaining-gaps.md`；多维数组：`docs/audit/multidim-probe.md`；M8 实机计划：`docs/plans/m8-real-machine-plan.md`
- 矩阵7 终版终验：`docs/audit/e2e-full-matrix7.md`（195/195=100%）
- 设计文档：`docs/superpowers/specs/2026-08-02-il2cpp-design.md`
- 生成代码风格规范：`docs/superpowers/specs/2026-08-02-generated-code-style.md`
- 实现计划：`docs/superpowers/plans/2026-08-02-m1-transpiler-skeleton.md`
- 端到端用例说明：`tests/cases/README.md`
