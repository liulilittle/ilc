# e2e Recheck 9 — 批次 13-14 修复后 15 用例增量复验（2026-08-05 09:5x–10:0x，完成）

只读用例 + 运行 + 写文档；未修任何代码；未 commit。

## 前置核查（任务门槛）

| 项目 | 结果 | 证据 |
|------|------|------|
| 转译器相关源稳定（mtime ≥30min） | 最新源 CodeGenerator.cs 09:22:38（恰 30min）/ StackFold.cs 09:21:13（32min）/ MethodMap.Console.cs 09:07:44（45min）/ MethodRefResolver.cs 08:41:59（71min），**全部 ≥30min** | [BASH:Get-Date 09:51:56 + 源 mtime 全量表] |
| il2cppc.dll 重建 | `bin/Release/net8.0/il2cppc.dll` **09:49:08** > 全部转译器源（最新 09:22:38）✓ | [BASH:il2cppc.dll LastWriteTime=09:49:08] |
| runtime 相关源稳定（mtime ≥30min） | 最新 runtime 源 console.cpp 09:08:52（49min）/ il2cpp_console.h 09:07:46（50min）/ threadpool.cpp 08:33:27（84min），**全部 ≥30min** | [BASH:src/runtime 源 mtime 全量表] |
| runtime 库新鲜度 | e2e 经 gen CMakeLists `add_subdirectory(src/runtime)` **现构建**（[READ:CodeGenerator.cs:2920]），无预编译 .a 过期风险；`libil2cpp_runtime.a` 09:14:30 > 最新 runtime 源 ✓ | [BASH:libil2cpp_runtime.a=09:14:30] |
| 用例 oracle 存在性 | **convcase/stringbuilder/mathext/formatcase 缺 expected.txt**（oracle=net48 live，run-tests.ps1 默认行为）；**stringbuildercase 不存在**，实际用例名 = stringbuilder（本批以 stringbuilder 代验并记录） | [BASH:expected.txt 清单 + glob tests/cases/stringbuildercase → 空] |

**关键过程发现**：任务启动时 il2cppc.dll（09:49）与 CodeGenerator.cs（09:22）已稳定，首轮即终态，无需像 recheck7 那样等待收敛轮询；唯一略紧的是 CodeGenerator.cs 恰 30min。

## 状态表（15 用例，mingw，终态）

| 用例 | 转译 | 构建 | 运行 | diff | 状态 | 精确缺口 |
|------|------|------|------|------|------|---------|
| convcase | OK | OK | EXIT=0 | **79/79**（net48 live，无 expected.txt） | **PASS** | 无（L60/L61 uint32 打印全对，批次 13-14 修复落地） |
| environment2 | OK | OK | EXIT=0 | **10/10** 逐字节 | **PASS** | 无 |
| threadpoolcase | OK | OK | EXIT=0 | **4/4** 逐字节 | **PASS** | 无（queued 2-arg/1-arg: True, all ran: True, done count: 8） |
| hexcase | OK | OK | EXIT=0 | **30/30** 逐字节 | **PASS** | 无 |
| datetime2 | **FAIL(2)** | – | – | – | **FAIL** | **DateTime.ParseExact(string,string,IFormatProvider) LOUD**（MethodRefResolver.cs:376：runtime 只有 2 参 `il2cpp_datetime_parse_exact`，MethodMap.DateTime.cs 无 ParseExact 映射）——58 行 oracle 未触达。**已修复（2026-08-05 11:1x，R1）**：转译推进到首个 `DateTime.Parse` LOUD（W2 通用 Parse 未接线，datetime2.cs:83），ParseExact 全段 25 探针通过 |
| sortdeep | OK | **FAIL** | – | – | **FAIL** | 生成 Program.cpp 7 处 `il2cpp::runtime::il2cpp_array_copy_to` 编译失败：**arraysort.h 声明在单层 `namespace il2cpp`**（尾部 `} // namespace il2cpp`），而其它 runtime 头均为 `il2cpp::runtime`（il2cpp_array_ext.h / il2cpp_string_ext2.h 尾部 `} // namespace runtime } // namespace il2cpp`）；MethodMap.ArraySort.cs:71 发射 `il2cpp::runtime::` 限定 → 符号不可见 |
| stringops | OK | OK | EXIT=0 | **27/27** 逐字节 | **PASS** | 无 |
| stringbuilder | OK | OK | EXIT=0 | **40/40**（net48 live，无 expected.txt） | **PASS** | 无（matrix3 旧缺 capacity 行 36/40 已消） |
| mathext | OK | OK | EXIT=0 | 92/100 | **FAIL** | **8 行 ∞/−∞ 编码分叉**（L22/28/33/45/53/89/95/97）：il2cpp 输出规范 UTF-8 ∞（`E2 88 9E`），net48 oracle 输出 U+FFFD×2（`EF BF BD EF BF BD`，控制台编码伪影）——oracle 侧损坏，需规范化 oracle 仲裁 |
| formatcase | OK | OK | EXIT=0 | 35/37 | **FAIL** | **2 行 ∞/−∞ 编码分叉**（L24/25），同 mathext（il2cpp `E2 88 9E` vs oracle `EF BF BD EF BF BD`） |
| exdeep | OK | OK | EXIT=**1** | stdout 28/37 | **FAIL** | **f18 throw InvalidOperationException 未捕获 → unhandled**（stderr：`Unhandled managed exception: exception thrown` + `(no managed frames on the stack)`）→ L29-L37（f18-f23 + 104）缺失；f1-f17 共 28 行逐字节一致 |
| exceptionchain2 | OK | OK | EXIT=0 | **17/17** 逐字节 | **PASS** | 无 |
| valdeep | OK | OK | EXIT=0 | 29/30 | **FAIL** | **L26 `(ulong)u1 == 4294967295UL` 期望 True 实际 False**（srcL81，uint→ulong 加宽比较路径），其余 29 行逐字节一致 |
| regexcase2 | OK | OK | EXIT=0 | **18/18** 逐字节 | **PASS** | 无 |
| uintcase | OK | OK | EXIT=0 | **52/52** 逐字节 | **PASS** | 无 |

**汇总：PASS 9/15**（convcase/environment2/threadpoolcase/hexcase/stringops/stringbuilder/exceptionchain2/regexcase2/uintcase），
FAIL 6/15：datetime2（转译期 LOUD）、sortdeep（构建期符号缺失）、mathext/formatcase（∞ 行 oracle 编码伪影）、
exdeep（f18 未捕获崩）、valdeep（L26 单行语义差）。

## 对比基线（matrix3 / recheck7 / worklist）

| 用例 | 基线 | 本批 | 变化 |
|------|------|------|------|
| convcase | matrix3 FAIL diff（L60 uint32） | **79/79 PASS** | **FAIL→PASS**（批次 13-14 修复落地，worklist §6 相符） |
| environment2 | matrix3 FAIL diff（tick_delta 时序） | **10/10 PASS** | **FAIL→PASS** |
| stringbuilder | matrix3 FAIL diff（36/40 capacity 行） | **40/40 PASS** | **FAIL→PASS** |
| stringops | matrix3 **PASS** | 27/27 PASS | 持平 |
| exdeep | matrix3 31/37 → recheck7 26/37（f16 throw null 崩） | **28/37**（f16/f17 已过，崩点移至 f18） | **前进 +2**（较 recheck7） |
| formatcase | matrix3 FAIL diff（L24 Infinity vs ∞） | 35/37（2 行 ∞ 编码分叉） | 同类缺口收窄（其余 35 行全过） |
| mathext | matrix3 FAIL diff（L22 Infinity vs ∞） | 92/100（8 行 ∞ 编码分叉） | 同类缺口收窄（其余 92 行全过） |
| datetime2/sortdeep/hexcase/exceptionchain2/valdeep/regexcase2/uintcase | 未入 matrix3 表 | 见状态表 | 新覆盖：datetime2/sortdeep/valdeep FAIL，hexcase/exceptionchain2/regexcase2/uintcase PASS |

净增 PASS（对 matrix3 重叠集）：**+3**（convcase/environment2/stringbuilder），+ exdeep 前进 2 行。

## 施工单（只记录，不修）

| # | 用例 | 缺口 | 归属域 | 修复方向 |
|---|------|------|--------|---------|
| R1 | datetime2 | ParseExact 3 参 LOUD（MethodRefResolver.cs:376） | DateTime/transpiler 域 | **已修复（2026-08-05 11:1x）**：实际落在 MethodMap.TimeExt2.cs（ParseExact 的既有归属域，施工单里写的 MethodMap.DateTime.cs 有误）——`ps.Length==3` → 2 参 `il2cpp_datetime_parse_exact` + 第三参 `"drop"`（IFormatProvider 忽略，String.Compare 3 参先例）。单测 11/11、全量无回归；转译越过 ParseExact 段 25 探针，止于 W2 通用 Parse LOUD。剩余：R1 的 W2 部分（`il2cpp_datetime_parse` 映射） |
| R2 | sortdeep | arraysort.h 单层 `namespace il2cpp` vs 发射器 `il2cpp::runtime::` | arraysort/runtime 域 | arraysort.h API 面包进 `namespace il2cpp { namespace runtime {`（与其它 runtime 头一致）；落地后 sortdeep 7 处调用可编译 |
| R3 | exdeep | f18 InvalidOperationException 未被匹配 catch 捕获 → unhandled（exit=1） | exception/catch 域 | 多 catch 子句的类型分派：兄弟类型首个不匹配后应继续尝试后续子句（现疑似只判首子句/匹配失败） |
| R4 | valdeep | L26 `(ulong)u1 == 4294967295UL` False（uint→ulong 加宽比较） | conv/valuesemantics 域 | **已修复（2026-08-05，valdeep 会话）**：根因 = StackFold Conv_u8 只折叠 IntConstExpr，非 const 32 位源（uint 局部/参数/temp）被当恒等——0xFFFFFFFF 留在 int32 槽（-1），比较 4294967295LL → False。修复 = 新 runtime icall `icall_conv_u8_i4`（int32 槽按 uint32 位型零扩展）+ StackFold Conv_u8 对 32 位非 const 源接线（tempWidths 记录 temp 宽度区分 int64 恒等；arg 按 ParamTypeNames 判宽）。conv.u8 const 路径不变。单测 ConvU8 8/8 + runtime test_conv 4 新 pin；e2e valdeep **30/30 PASS**；convertcase/boxing2/uintcase/uintcmp/convcase 零回归 |
| R5 | mathext/formatcase | ∞ 行 oracle 编码伪影（U+FFFD×2） | 测试 oracle/编码域 | 捕获规范 expected.txt（∞=U+221E）或用编码归一化 oracle 仲裁；il2cpp 侧 `E2 88 9E` 本身正确 |

## 验证证据（终态）

- [BASH:run-e2e-recheck9.ps1 15 例全跑 elapsed=743s，recheck9_summary.tsv]
- [BASH:recheck9_*.res ×15]：9 PASS / 6 FAIL；stage=transpile(datetime2) / cmake build(sortdeep) / diff(mathext,formatcase,exdeep,valdeep)
- [BASH:raw bytes]：mathext L22 net48=`EF BF BD EF BF BD 0D` vs il2cpp=`E2 88 9E 0D`；formatcase L24 同
- [BASH:stdout-only 逐字节]：valdeep 唯一差 L26 True vs False；exdeep f1-f17=28 行一致、L29 起缺失 + exit=1
- [BASH:exit codes]：mathext/formatcase/valdeep/convcase/stringbuilder il2cpp exe=0；exdeep=1
- [READ:MethodRefResolver.cs:376 LOUD]；[READ:arraysort.h 尾部 `} // namespace il2cpp`]；[READ:il2cpp_array_ext.h 尾部 `} // namespace runtime } // namespace il2cpp`]；[READ:MethodMap.ArraySort.cs:71]；[READ:timeext2.h:73 2 参 parse_exact]；[READ:valdeep.cs:81]

## 方法（复用 recheck7）

- runner：%TEMP%\opencode\run-e2e-recheck9.ps1（循环 run-tests.ps1 -Case -ContinueOnError -ResultsFile，
  逐用例 log 落盘；另做 stdout-only + CRLF 归一化 + 大小写敏感逐字节 secondary check vs expected.txt）。
- 教训：**build 失败用例的 build_mingw 里残留旧 exe**，secondary byte-check 会误读陈旧产物（sortdeep actual=13 行即此类）——凡 cmake 失败一律以 stage=FAIL 为准，忽略 byte-check。
- exdeep 的 stdout/stderr 双管道合并乱序（recheck7 同）：byte-check 一律 stdout-only（2>$null）。
