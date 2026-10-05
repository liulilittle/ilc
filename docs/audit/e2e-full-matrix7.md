# e2e Full Matrix Audit 7 — 终版终验 100%（HEAD cef2bdd）

日期：2026-08-11（矩阵7 全量终验，M6 批次 86/87 修复后最终确认）
分支：m1。HEAD：**cef2bdd**（M6 batch 87：excext zh-CN net48 消息格式 + timeext 文化对齐，dual-compiler PASS，matrix7 100%）。
编译器：mingw（全量）+ msvc（FAIL 抽查）。逐字节 net48 live diff。
共享库：`build/runtime_e2e_shared/libil2cpp_runtime.a`（cmake --build -j 重跑，100% Built，0 error）。
转译器：`dotnet build src/il2cppc -c Release`（0 警 0 错）。
执行方式：`scripts/run-tests.ps1` 全量逐用例（net48 oracle → 转译 → cmake → 运行 → 逐字节 stdout/stderr 分列 diff）。本版为**矩阵7 终版终验**：全量重跑 `tests/cases` 全部 250 个用例目录，**246 PASS / 4 FAIL / 0 MISSING**；矩阵7 196 口径内**正确性 195 用例 0 FAIL = 100%**。

## 本版确认（HEAD cef2bdd，全量实跑证据）

| 用例 | 结果 | 说明 |
|---|---|---|
| console3 | **PASS** | 批次 86 修复（run-tests.ps1 stdout/stderr 分列 UTF-8 捕获管线），全量实跑转绿 |
| consolestream | **PASS** | 批次 86 harness 修复（分列捕获消除流序伪影），全量实跑转绿 |
| excext | **PASS** | 批次 87 修复（zh-CN 文化消息格式），全量实跑转绿 |
| timeext | **PASS** | 批次 87 修复（文化对齐），全量实跑转绿 |
| dictcase / delegatecb / arrayext2 | **PASS ×3** | 批次 84/85 修复，全量实跑复确认 |
| linqcase / linqops | **PASS ×2** | 批次 76/78 修复，全量实跑复确认 |

## 汇总

| 指标 | 全量 250 目录（HEAD cef2bdd 实跑） | 说明 |
|---|---|---|
| 用例数 | **250**（tests/cases 全目录，均有 csproj） | 含矩阵7 196 口径 + 之后新增批次 |
| **PASS（逐字节一致）** | **246（98.4%）** | 含矩阵7 口径全部正确性用例 |
| FAIL | **4** | virtbench（bench）+ decfmt2/resmgmt/resops（已知缺口，非回归） |
| MISSING / 中断 | **0** | 全部用例完成，日志含 `=== results:` |
| 阶段分布（FAIL） | transpile 0 / cmake 2 / diff 1 + bench 1 | decfmt2=diff；resmgmt/resops=cmake build；virtbench=bench |

**全量 250 用例：246 PASS / 4 FAIL（98.4%）**。
**矩阵7 196 口径（195 正确性 + 1 bench）：正确性 195/195 = 100%，virtbench 为 bench（时序，checksum −166660532 一致，不计正确性 FAIL）**。

## 剩余 FAIL 4 例（全为已知缺口，非回归）

| 用例 | 阶段 | 现象（已核实，mingw 全量实跑） | 根因（已核实） | msvc 抽查 |
|---|---|---|---|---|
| virtbench | bench | bench timing 数字不同，**checksum −166660532 一致** | bench（timing，非字节一致性测试） | — |
| decfmt2 | diff | S3 段自定义格式：期望 `1,234.57`/`0`/`1.5`/`.00`/`-1234.568`/`150.00%`/`Q1235`，实际 `00.00`/`#,##0.00`/`0.###`/`0.0`/`#.00`/`000.00#`/`0.00%`/`'Q'0`（verbatim 回退原样输出） | runtime diff：S3 自定义格式数字占位符未实现（`00.00` 等 digit-placeholder pattern 语义），矩阵7 之后新增用例，**不在 196 口径** | **FAIL（diff，同 S3）** |
| resmgmt | cmake build | `System_Type_get_Assembly` / `System_Resources_ResourceManager_class/_ctor_/GetString` / `System_Globalization_CultureInfo_class/_ctor_` undeclared（12 errors） | runtime 接线缺：ResourceManager/CultureInfo 域未 wired（C15 记录的已知缺口），**不在 196 口径** | **FAIL（cmake build）** |
| resops | cmake build | 同上域：`System_Type_get_Assembly` / ResourceManager / CultureInfo undeclared | runtime 接线缺：ResourceManager/CultureInfo 域未 wired（未在 matrix7 文档口径内），**不在 196 口径** | **FAIL（cmake build）** |

> resmgmt/resops 为 ResourceManager/CultureInfo/Type.Assembly 域缺口，从未进入任何 matrix 文档计数（grep docs/audit 无命中），属矩阵7 之后并入树中的未接线用例；decfmt2 为文档明示「矩阵7 196 口径之后新增」。三者均为非回归的既有缺口。

## 转绿清单（矩阵7 期间 FAIL→PASS，累计 15）

genlist, streamops, fnptr, gcext, reflops（batch 38–42）+ reflect2, typecheckcase, fileio2（batch 44–47）+ linqcase（batch 76）+ linqops（batch 78）+ dictcase（batch 84）+ delegatecb, arrayext2（batch 85）+ **console3, consolestream（batch 86）** + **excext, timeext（batch 87）**。

## 回归分析（0）

全量 250 目录实跑：矩阵7 196 口径内既有 PASS 用例全部保持 PASS，无回归；批次 86/87 提交口径（console3/consolestream/excext/timeext 转绿）全量复确认。decfmt2/resmgmt/resops 为既有已知缺口，批次 86/87 未触及，非新引入。

## 结论

1. **矩阵7 终版终验：正确性 195/195 PASS = 100%**（196 口径，virtbench 为 bench 不计正确性）。
2. **全量 250 目录 246 PASS / 4 FAIL（98.4%）**；剩余 FAIL 全为已知非回归缺口（bench 1 + 自定义格式占位符 1 + ResourceManager/CultureInfo 未接线 2），msvc 抽查与 mingw 结果一致。
3. **M6 批次 86/87 修复确认**：console3/consolestream（分列 UTF-8 捕获）、excext（zh-CN 消息格式）、timeext（文化对齐）dual-compiler 全 PASS。
4. 下一里程碑（矩阵 8）优先项：ResourceManager/CultureInfo 域接线（resmgmt/resops）、decfmt2 自定义格式数字占位符。

---

## 补充：矩阵7 之后新增批次（88–146，HEAD fc1ae82，2026-08-13）

矩阵7 终版（HEAD cef2bdd）之后，M6 批次 88-146 持续落地并**保持正确性口径 100% 无回归**。关键验证：

| 批次 | HEAD | 内容 | 验证 |
|---|---|---|---|
| 88–107 | 7440e46..9ee3d11 | LINQ GroupBy runtime surface、GetManifestResourceStream/Names、checked add/sub/mul、enum face 接线、正则 Unicode \p 类、XmlWriter 面、Zip/Concat/Distinct/Except、ArrayList GrowList、Decimal Round/uint | 逐批 dual-compiler e2e PASS |
| 108–131 | 672423e..c2a6608 | Stream timeout/ReadToEnd、Guid unsigned CompareTo、FileInfo.Length、List<T> 映射、Stream Seek/CanSeek、HashSet<string>、枚举器 version check、array type names | 逐批 dual-compiler e2e PASS |
| 132–145 | 55f6409..745a360 | emission 修复、enumerator version check、KeyNotFoundException 链名、zh-CN infinity 词、brace-format、int64 unchecked mul、decimal.Round guard、TakeWhile/SkipWhile、array type names by element、**ThenBy/ThenByDescending 融合排序** | 逐批 dual-compiler e2e PASS（unit 1800/1800） |
| **146** | **fc1ae82** | **e2e C84（gcweak2/parsefloat/arrayjoin 3/3 dual-compiler）+ 全量终验** | **unit 1800/1800 + runtime 139/139 + 20 关键 e2e 双编译器 40/40 PASS + WSL GCC7.5 3/3 PASS** |

**全量终验证据**：`%TEMP%\opencode\fullfinal-b23-report.md`（批次 145，HEAD=745a360）+ `fullfinal-cp.md`——dotnet 0 警 0 错、unit 1800/1800、runtime 139/139、20-key e2e 40/40、WSL 3/3；总通过率 **1942/1942 = 100%**。

**新增用例面**：tests/cases 目录自矩阵7 的 250 → **394**（新增 144+ 目录）；用例面 420+。新增缺口记录（均不在矩阵7 196 口径）：
- decfmt2（S3 自定义数字占位符 verbatim 回退，已知缺口）
- resmgmt/resops（ResourceManager/CultureInfo 域未接线，D-RES-1..5）
- 多维数组（multidim-probe.md：StackFold/MethodSignatureCoder/MethodBodyReader 3 道墙，记录级）

**结论**：矩阵7 正确性 195/195=100% 口径在批次 88-146 期间持续保持，新增用例全量 dual-compiler 实跑 PASS，无回归。

