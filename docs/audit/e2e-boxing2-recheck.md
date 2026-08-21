# e2e boxing2 复验 — e2e-boxing2-recheck（2026-08-05 01:4x，完成）

**结论：PASS 34/34（双 oracle 逐行一致）**。boxing2 从 recheck7 的"转译期 Console.WriteLine(ulong) LOUD 阻断"**前进至全量通过**。

只读用例 + 写文档；未修任何代码；未 commit。

## 前置核查（任务门槛）

| 项目 | 结果 | 证据 |
|------|------|------|
| GetType 短名修复落地 | ✅ MethodMap.Type.cs:39 / MethodMap.Object.cs:74 → `il2cpp_type_get_short_name`（runtime typecheck.cpp:245 实现，"System.Int32"→"Int32"） | [READ:MethodMap.Type.cs]、[READ:typecheck.cpp:245] |
| boxingext2 链修复落地 | ✅ boxingext2.cpp（01:06:29）：新增 uint box/unbox（icall_box_uint/icall_unbox_uint + g_uintBoxClass）+ 异常链 4 段格式（NRE/InvalidCast/SystemException/Exception） | [READ:boxingext2.cpp diff]、[BASH:git diff --stat] |
| UInt64 WriteLine 实现落地 | ✅ console.cpp（01:11:39）`icall_console_write_line_u64` + `console_format_u64`（全范围 uint64 十进制，MaxValue→"18446744073709551615"）；MethodMap.Console.cs:54 `0x0B → icall_console_write_line_u64`（q cast）；CodeGenerator.cs:2234 `"q" => (uint64_t)` | [READ:console.cpp:187]、[READ:MethodMap.Console.cs:54]、[READ:CodeGenerator.cs:2234] |
| il2cppc.dll 重建 | `dotnet build src/il2cppc -c Release` → 0 错 0 警；dll 01:45:21 > 全部相关源 | [BASH:dotnet build EXIT=0]、[BASH:dll LastWriteTime 01:45:21] |
| 共享 runtime 库重建 | `cmake --build build/runtime_e2e_shared --target il2cpp_runtime` → 100% Built（lib 01:45:30，含 console.cpp/runtime.cpp 最新 obj） | [BASH:CMAKE_EXIT=0] |
| 相关代理收工（mtime ≥30min） | 终态轮询 01:45:12：console.cpp 01:11:39 / MethodMap.Console.cs 01:13:40 / boxingext2.cpp 01:06:29 / MethodMap.Guid.cs 01:15:01，全部 ≥30min 且 33min 内无新改动 | [BASH:mtime 轮询 01:16→01:45] |

**关键过程发现（必须先记录）**：任务启动时（01:04 首查）相关文件 mtime 看似全部 ≥30min
（boxingext2.cpp 23:58 / console.cpp 23:53 / MethodMap.Console.cs 23:56），但**代理仍在活跃
编辑**——01:06:29 boxingext2.cpp、01:11:39 console.cpp、01:13:40 MethodMap.Console.cs、
01:15:01 MethodMap.Guid.cs 相继被改。首次转译（01:08，用 23:56 版 Console.cs 重建的 dll）
因 `Console.WriteLine(ulong) 0x0B LOUD` 失败——**这是过时 dll 的在途编辑伪影，非真实缺口**。
按 recheck7 方法论等待全部相关文件 ≥30min 稳定后重建重跑，终态 PASS 34/34。

## 结果

| 阶段 | 结果 | 证据 |
|------|------|------|
| 转译 | OK（EXIT=0，01:45:37） | [BASH:TRANSPILE_EXIT=0] |
| 构建 | OK（独立 wrap 目录 + 共享 runtime 库） | [BASH:BUILD_OK] |
| 运行 | EXIT=0 | [BASH:RUN_EXIT=0] |
| diff expected.txt（34 行） | **PASS 34/34**（TrimEnd 后逐字节相等；act=34 / exp=34 行） | [BASH:trimmed-equal=True] |
| diff net48 live | **34/34 行零差异**（line-diff=0） | [BASH:net48-live line-diff=0] |

覆盖的用例点（boxing2.cs）：
- byte/short/long/float/ulong box/unbox 往返（L21/31/39/49/58 等）——含 UInt64.MaxValue `18446744073709551615`
  （新 u64 icall 全范围打印，绝不走 int64 helper 的 -1）
- `GetType().Name` 短名：Byte/Int16/Int64/Single/UInt64（L22/32/40/50/59）——短名截断修复落地生效
- object.Equals 盒值相等语义（同型值等、异型 False，L23-28/33-36/41-46/51-62）
- 跨型比较：Byte vs SByte False（L25-27）、float vs double False（L52）、UInt64 vs Int64 False（L62）
- Single.Equals(NaN,NaN)=True（L54）、vs +Infinity False（L55）
- 静态 object.Equals / ReferenceEquals（L65-66）
- wrong-type unbox → InvalidCastException（L69）；null unbox → NullReferenceException（L72）——
  异常链 4 段格式落地后 catch 匹配正确

## 对比矩阵

| 基线 | 状态 | 本复验 |
|------|------|--------|
| matrix2（39/88 时代） | build+run FAIL 35/34 | **PASS 34/34** |
| recheck7（00:38） | 转译期 FAIL(2)：Console.WriteLine(ulong) LOUD | **PASS 34/34** |

净变化：recheck7 FAIL → PASS（34 行全过）。剩余 LOUD 门 0 个（boxing2 用例内）。

## 施工单

**无剩余施工项（boxing2 用例内）**。三项修复（GetType 短名 / boxingext2 链 / UInt64 WriteLine）
均已落地且经 e2e 验证。此前 recheck7 的 R1 施工单（add il2cpp_console_write_line_u64 +
恢复 0x0B 映射）已由 console UTF-8 域完成并验证。

提示（非本用例范围）：
- 本复验只覆盖 boxing2 单用例；recheck7 其余 FAIL（base64case / guidcase / collchain / exdeep 部分）
  不在本次任务范围，未重验。
- 若后续做全量聚合，boxing2 贡献 +1 PASS（44/88 基线推估 → 45/88），但需全量重跑确认无回归。

## 方法（可复用）

- 复验前置门槛：对**本用例**相关文件逐一 mtime 轮询 ≥30min，再重建 dll + 共享库，再转译。
  首轮结果若与预期不符，先怀疑在途编辑伪影（本会话 boxingext2/console/MethodMap.Console.cs
  均在本会话运行期间被改）。
- 转译脚本：`dotnet il2cppc.dll <net48-exe> <gen>`；构建用 wrapper CMakeLists（gencase 静态库 +
  共享 `libil2cpp_runtime.a`）；运行产物与 expected.txt 做 TrimEnd 逐字节比较 + net48 live 逐行
  双 oracle。脚本存 %TEMP%\opencode\run-boxing2-recheck8.ps1。
- 证据探针：.opencode\probes\boxing2-actual.txt / boxing2-transpile.log / boxing2-build.log /
  boxing2-recheck8-transpile.log（首轮失败日志）。

## 验证证据（终态）

- [BASH:TRANSPILE_EXIT=0 / TRANSPILE_OK]
- [BASH:BUILD_OK]
- [BASH:RUN_EXIT=0]
- [BASH:trimmed-equal=True]（expected.txt 与 actual 逐字节相等，34 vs 34 行）
- [BASH:net48-live line-diff=0]（net48 实跑 34 行 vs actual 34 行，零差异）
- [READ:boxing2-actual.txt 34 行] vs [READ:expected.txt 34 行]——内容逐行一致
- [BASH:mtime 终态轮询 01:45:12]：全部相关文件 ≥30min 稳定
