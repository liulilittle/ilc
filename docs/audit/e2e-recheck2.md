# Audit: e2e 复验 #2 — datetimechain / stringops / boxstr / sbchain（2026-08-04）

任务：DateTime 对照审计 + console CRLF 修复 + boxed ToString 修复落地后，四用例
应前进。逐一 转译（il2cppc.dll 直跑）→ cmake 构建（独立 build_recheck2 目录）→
运行 → diff expected.txt。
范围：只读用例（tests/cases/{datetimechain,stringops,boxstr,sbchain}/ 未改，git
status 干净），只写本文件；不修；不 commit。

## 0. 环境先验

| 项 | 值 | 证据 |
|---|---|---|
| il2cppc.dll mtime | Release/net8.0 08/04 08:43:19 | [BASH] Get-ChildItem src/il2cppc/bin -Recurse |
| 最新转译器源 | CodeGenerator.cs 08:26:08 | [BASH] 85 个源全量比对 |
| 先验判定 | **FRESH**：dll 新于全部 85 个 .cs/.csproj | [BASH] 全量 mtime 比较输出 "FRESH" |
| 工具链 | cmake 3.22.1 (Android SDK) + MinGW g++/mingw32-make | [BASH] Get-Command |
| 用例 fixture | 四用例 out/*.exe mtime 均新于各自 .cs | [BASH] Get-ChildItem mtime 对照 |
| 用例与 git | 四目录零 diff、零 untracked（build_recheck2/gen 被 gitignore） | [BASH] git status / git check-ignore |

## 1. 状态总表

| 用例 | 转译 | C++ 构建 (mingw) | 运行/diff | 结论 |
|---|---|---|---|---|
| datetimechain | ✅ exit=0 "generated 1 type(s)" | ✅ configure+build exit=0 | ✅ **232 B 逐字节一致** | **PASS** |
| stringops | ✅ exit=0 "generated 1 type(s)" | ✅ exit=0 | ✅ **136 B 逐字节一致** vs net48 直跑（无 expected.txt，参考 = out/oracle.txt，net48==oracle） | **PASS** |
| boxstr | ✅ exit=0 "generated 3 type(s)" | ✅ exit=0 | ✅ **146 B 逐字节一致** | **PASS** |
| sbchain | ✅ exit=0 "generated 1 type(s)" | ✅ exit=0 | ✅ **297 B 逐字节一致** | **PASS** |

net48 参考交叉验证：datetimechain/boxstr/sbchain 的 out/*.exe 本机直跑 == expected.txt
逐字节（232/146/297 B）——expected.txt 未过期，ILC 输出与 net48 参考三方一致。

## 2. datetimechain（PASS）

- 覆盖：DateTime ctor/AddDays/AddHours/Subtract/CompareTo/TimeSpan ctor+Ticks/
  ToString 自定义格式 "yyyy-MM-dd[ HH:mm:ss]"（29 行）。
- 转译产物：Cases_DateTimeChainCase.cpp 29 处 `icall_console_write_line_b`（bool 探针）+
  datetime 域 icall（get_year/month/...）——接线正确。
- 三修复关联：**console CRLF 修复直接命中**（29 行 CRLF 字节级一致，无 `\r\r\n`
  污染）；DateTime 审计的 Parse/ParseExact/f/F 修复本用例不触及（未用通用 Parse）。

## 3. stringops（PASS，fixture 缺口见 §6 G1）

- 覆盖：CompareOrdinal/Concat 2/3/4/Concat(object,object)/Join/IsNullOrEmpty/
  IsNullOrWhiteSpace/get_Chars/Copy/ReferenceEquals/Length（27 行）。
- **无 expected.txt**（目录下只有 out/oracle.txt，27 行）——本复验以 oracle.txt 为参考，
  并额外直跑 net48 交叉验证：ILC 输出 == net48 直跑 == oracle.txt（136 B 三方一致）。
- 转译产物：Program.cpp 1 type；string pool 15 literals / 3 constant concat folded。

## 4. boxstr（PASS）

- 覆盖：boxed int/bool concat、object[] 混合存取+unbox、GetHashCode、is/as 用户接口、
  boxed Int32/Boolean ToString、string identity ToString、错误 unbox 异常路径（24 行）。
- boxed ToString 修复（valuesemantics.cpp `icall_object_to_string`，:246-300+：Int32/
  UInt32/Int64/UInt64/Double/Single/Boolean/SByte/Int16 全覆盖 + null→NRE）在
  boxstr.cs 提交（4b25827, 8/4 00:05）之后落地（57d3350, 8/4 04:14）——用例保持 PASS，
  且 boxstr.cs 头部注释 "boxed long/double ToString -> klass name is a KNOWN runtime gap"
  已过时（长整/双精度 ToString 现已按值格式化）。
- 异常路径（bad unbox 抛链 "System.X,System.Exception"）仍走 catch(Exception) 兜底，
  与注释一致。

## 5. sbchain（PASS）

- 覆盖：StringBuilder Append/AppendLine/AppendFormat 链 + Insert/Remove/Replace +
  数值 Append + 增长边界；**AppendLine 内嵌 "\r\n" 的 5 行多行串整体
  Console.WriteLine 输出**（21 行）。
- **console CRLF 修复的主验证点**：`icall_console_write_line_s`（runtime.cpp:16-27）逐
  char 打印（含串内 `\r\n` 原样）+ `console_write_newline()` 显式补 `\r\n`；stdout 在
  启动时 `_setmode(_fileno(stdout), _O_BINARY)`（console.cpp:43-65）。297 B 逐字节一致
  = 串内 `\r\n` 无被文本模式扩成 `\r\r\n`，行尾恰一个 `\r\n`。

## 6. 三修复在树状态核验

| 修复 | 树内位置 | 本复验证据 |
|---|---|---|
| console CRLF | console.cpp ConsoleStreamInitializer(_O_BINARY) + console_write_newline()（头 il2cpp_console.h:72-79）；runtime.cpp i/s/n + console.cpp b/c/d 六个 write_line icall 全部调 newline | sbchain/datetimechain 字节级 CRLF 正确（§2/§5） |
| DateTime 审计 | timeext2.cpp Parse/ParseExact/yy 展开/字面量/f·F（未提交 diff 在途） | datetimechain PASS；**通用 Parse 仍未接线**（见 G2） |
| boxed ToString | valuesemantics.cpp:246-300 icall_object_to_string 扩展（提交于 boxstr.cs 之后） | boxstr PASS；boxstr.cs 注释已过时 |

## 7. 缺口清单 + 施工单

| # | 缺口 | 精确定位 | 类别 | 影响 |
|---|---|---|---|---|
| G1 | stringops 无 expected.txt（仅有 out/oracle.txt） | tests/cases/stringops/ 目录（8/3 15:45 建 fixture 时即缺） | fixture 缺口 | 本复验以 oracle.txt 为参考（已证 == net48 直跑 136 B）；run-tests.ps1 不用 expected.txt 故无影响 |
| G2 | 通用 `DateTime.Parse` 未接线：MethodMap.TimeExt2.cs 仅映射 `il2cpp_datetime_parse_exact`（:38），无 `il2cpp_datetime_parse` 分支；datetime-conformance.md §遗留项 | src/il2cppc/Emit/MethodMap.TimeExt2.cs | 接线缺 | 转译代码在映射落地前不可达；datetimechain 未用 Parse 故不受阻 |
| G3 | 无 FAIL 用例（四用例全 PASS）——本复验无运行时缺/转译器 bug | — | — | — |
| G4 | PowerShell 原生输出捕获伪影：`& exe 2>&1 \| Set-Content -NoNewline` 得 174 B（换行符被剥离）vs cmd 重定向真值 232 B | — | 过程教训 | 字节级 e2e 必须 cmd 重定向直采；详见 discover.md |

施工单（建议顺序）：
- **W1**：stringops 补 expected.txt（内容 = oracle.txt 27 行），与其余用例 fixture 一致（低优先）。
- **W2**：MethodMap.TimeExt2.cs 加 `il2cpp_datetime_parse` 分支（runtime `il2cpp_datetime_parse` timeext2.cpp:585 已存在）→ datetimechain 或新用例补通用 Parse 探针 → 重跑 e2e。
- **W3**：boxstr.cs 头部 "KNOWN gap" 注释按现行 valuesemantics 覆盖范围更新，并补 boxed long/double ToString 两行探针（修复已落地，注释陈旧，用例未覆盖新能力）。
- **W4**：sbchain 的 build_consolefix/ 旧实验目录与 out/ 重建产物（4:30-4:36，console 修复前尝试）可清理（gitignore 内，无仓库影响）。

## 8. 验证证据汇总

- [BASH] il2cppc.dll 08:43:19 > 全部 85 源（最新 CodeGenerator.cs 08:26:08）→ FRESH
- [BASH] dotnet il2cppc.dll <case>.exe gen → 4 次均 exit=0 "generated N type(s)"
- [BASH] cmake -S gen -B build_recheck2 -G "MinGW Makefiles" + --build → 4 次均 exit=0
- [BASH] cmd /c "il2cpp_gen_exe.exe > raw.bin" → 232/136/146/297 B；[READ] SequenceEqual
  vs expected.txt / oracle.txt / net48 直跑 → 全 PASS
- [BASH] net48 参考直跑（datetimechain/boxstr/sbchain）== expected.txt 逐字节
- [READ] console.cpp:43-65,75-82,124-140 / runtime.cpp:13-29（CRLF 修复在树）
- [READ] valuesemantics.cpp:246-300（boxed ToString 扩展在树）
- [READ] MethodMap.TimeExt2.cs:38（仅 parse_exact；G2 依据）
- [BASH] git status / git log：四用例目录零改动；boxstr.cs 提交 00:05 < boxed ToString
  提交 57d3350 04:14

## 结论

**4/4 全 PASS**（逐字节一致，ILC 输出 == net48 参考 == expected.txt/oracle.txt 三方一致）。
三项修复（console CRLF / DateTime 审计 / boxed ToString）均在树且可运行验证；无
运行时缺、无转译器 bug。剩余 2 项记录性缺口：stringops 缺 expected.txt fixture（G1，
W1）、通用 DateTime.Parse 未接线（G2，W2）——均为任务性施工项，不构成四用例失败。
