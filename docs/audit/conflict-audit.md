# 两会话冲突审计报告（conflict audit）

- 审计时间：2026-08-03 22:49（本地）
- 基线 commit：`4673de0`（docs: M6 architecture/audit/research notes + gitignore test artifacts）
- 审计方法：`git status --short` 全量 + `git diff --stat` + `git diff HEAD --name-status` + `git log --oneline -15`；损坏检测（0 字节/极小文件/半截尾字符/缺 include/近 30 分钟 mtime）；`.opencode/` 双写检查；`dotnet build` 双项目验证；docs/tests/runtime 成果核对
- 工作树状态快照：24 个 M（已跟踪修改）+ 大量 ??（未跟踪新增，~300 项，其中绝大多数为并行代理新增源码/用例/文档），**0 个 D（无删除）**

## 结论（TL;DR）

**未发现"冲突损坏"类问题**（半截文件/空文件/双写乱码 = 0），无需修复任何文件。
工作树健康度：高。4 项在途代理 WIP（只记录），2 项成果缺失（记录），4 个根目录调试残留（建议清理，不属损坏）。

## 冲突清单表

| # | 文件 | 症状 | 判定 | 处理 |
|---|---|---|---|---|
| 1 | `.opencode/plan.md` | mtime 22:30:15（并行会话写入"MethodMap.Attribute.cs BCL 接线"任务计划）；2115 B 与备份 `plan.md.20260803-223017` 同尺寸 | 双写发生（两会话共写 .opencode/），内容完整无乱码/重复段落 | 在途 WIP：只记录，不修改 |
| 2 | `src/runtime/src/encoding.cpp` | mtime 22:27:23，15805 B，头注释与历史版本不同（Win32 UTF-8 委派描述），尾部 `}} // namespace il2cpp::runtime` 正常闭合 | 在途代理 WIP（与 encoding.h 22:28:55 配套修改） | 只记录 |
| 3 | `src/runtime/include/internal/il2cpp_encoding.h` | mtime 22:28:55，4714 B，尾部完整 | 在途代理 WIP | 只记录 |
| 4 | `tests/unit/MethodMapTests.Base64.cs` | mtime 22:11:53，8701 B | 在途代理 WIP（MethodMap.Base64 域测试） | 只记录 |
| 5 | 根目录 `console_capture.txt` (10 B) / `console_input.txt` (13 B) / `debugext_capture.txt` (0 B) / `debugext_stderr.txt` (26 B) | 19:41:34 创建后 3 小时未变；内容为调试输出（"Falsex2.25"、"Assertion Failed: (null)"） | Console/Debug 域调试残留，非双写截断（0 字节文件为捕获无输出所致） | 建议清理（rm 4 个 txt），未删（不属冲突损坏） |
| 6 | `docs/audit/performance-summary.md` | 缺失；`git log --all` 全历史无此文件 | 成果未产出（非损坏） | 记录为成果缺口 |
| 7 | `docs/audit/e2e-coverage.md` | 缺失；`git log --all` 全历史无此文件 | 成果未产出（非损坏） | 记录为成果缺口 |
| 8 | `docs/audit/alignment-tracker.md` / `docs/audit/roadmap-status.md` / `docs/architecture/regex-domain.md` / `docs/architecture/linq-domain.md` | 任务清单路径不存在 | **路径差异，文件均存在**：`docs/alignment-tracker.md`、`docs/roadmap-status.md`、`docs/design/regex-domain.md`、`docs/design/linq-domain.md` | 无处理 |
| 9 | `tests/cases/*`（60 个老用例无 expected.txt） | 初判缺失 | **正常设计**：run-tests.ps1:41 为动态参考机制（net48 exe 输出即 expected），expected.txt 非必需；28 个新 expected.txt 为并行代理静态快照增强 | 无处理 |
| 10 | `tests/unit/TestResults/`（result.trx） | 未跟踪产物，不在 .gitignore | 小项 | 建议 .gitignore 追加 `tests/unit/TestResults/` |
| 11 | `src/il2cppc/il2cppc.csproj` | memory.md 22:13 记录"git D 被删" | **已恢复**（Test-Path = True，status 无 D 条目） | 无处理（并行代理已修复） |

## 损坏检测明细（全部通过）

| 检测项 | 结果 | 证据 |
|---|---|---|
| ?? 中 0 字节源码文件 | **0 个**（仅 build/node_modules 中间产物与 debugext_capture.txt 调试残留） | 全库扫 0 字节（排除 obj/bin/build/gen） |
| ?? 中 <50 B 文件 | 36 B 头全部为 `gen/` 生成骨架（`#pragma once` + `#include "generated.h"`，标准）；`genlist/expected.txt` 26 B 内容完整（6 行）；CMake `Progress/count.txt` 4 B 正常 | 抽样读 Cases_Arith.h / genlist expected |
| .cpp 缺 #include | 0（前 6 行为文件头块注释，include 在后——项目惯例） | array.cpp/encoding.cpp 头部读取 |
| .cs 半截语法 | 0（尾字符检查：仅 obj/ MSBuild 自动生成 AssemblyInfo 文件以 `)]` 结尾——正常形态） | 全用例 .cs 尾字符扫描 |
| .cpp/.h 半截 | 0（尾部闭合字符检查通过） | src/ 全量扫描 |
| .opencode/ 4 文件双写 | 完整无乱码：memory.md 76 行 / plan.md 31 行 / process.md 38 行 / discover.md 35 行，结构均正常 | 本审计读取 |
| 转译器编译 | **dotnet build src/il2cppc：0 错误 0 警告** | build 输出 22:46 |
| 单元测试项目编译 | **dotnet build tests/unit：0 错误 0 警告** | build 输出 22:47 |
| C++ runtime 编译 | 未重跑（encoding.cpp/encoding.h 22:27-22:28 在途修改，重编译会撞在途编辑）；22:11 的 gccase build_mingw 已成功编译含全部新域 .obj | gccase/build_mingw obj 列表 |

## 成果核对

| 项 | 结果 |
|---|---|
| docs 关键报告 | 14/16 存在（2 项缺失：performance-summary / e2e-coverage，见清单 #6 #7；4 项路径差异已定位，见 #8） |
| tests/cases 用例目录 | **87** 个，.cs/.csproj 齐全，28 个含 expected.txt |
| runtime 头文件 | **80**（src/runtime/include/internal/） |
| runtime 实现 | 81 个 .cpp（src/runtime/src/） |

## 在途代理 WIP 记录（本审计不触碰）

- `.opencode/plan.md`（22:30:15）——并行会话 Attribute 接线任务计划
- `src/runtime/src/encoding.cpp`（22:27:23）+ `include/internal/il2cpp_encoding.h`（22:28:55）——Encoding 域在途
- `tests/unit/MethodMapTests.Base64.cs`（22:11:53）——Base64 域在途
- 建议：后续编辑 `src/runtime` 下文件前复查 mtime（memory.md 既有铁律）

## 建议（非阻塞）

1. 删除根目录 4 个调试残留 txt（console_capture/console_input/debugext_capture/debugext_stderr）
2. `.gitignore` 追加 `tests/unit/TestResults/`
3. 成果缺口：performance-summary.md 与 e2e-coverage.md 若在任务清单内，需由对应代理补产
