# e2e Full Matrix Audit 6 — 全量 155 用例（批次 1/2/3 合并 + matrix5 对比）

日期：2026-08-06（批次1 14:28-15:16、批次2 15:25-15:39、批次3 15:20-15:55；权威全量 run 15:16 完成）
分支：m1。编译器：mingw。逐字节 net48 live diff。
共享库：`build/runtime_e2e_shared/libil2cpp_runtime.a`（14:20:48）新于全部 runtime 源（13:07:18），`cmake --build` 0 改动，直接复用。
数据源：`%TEMP%\opencode\matrix6\results.tsv`（权威全量 155 例）+ 三份批次报告（`docs/audit/e2e-matrix6-batch1/2/3.md`）+ 逐用例 checkpoint（`e2e-matrix6-checkpoint-1/2/3.md`）。

## 汇总

| 指标 | matrix5 | **matrix6** | M5→M6 变化 |
|---|---|---|---|
| 总用例 | 131 | **155**（新增 23 例 + ifcase 纳入计数） | +24 |
| **PASS（逐字节一致）** | 92（70.2%） | **108（69.7%）** | +16 |
| FAIL | 39 | **47** | +8 |
| 转译失败 | 18 | **18** | = |
| cmake 构建失败 | 21 | **21** | = |
| 运行/diff 失败 | 7 | **7** | = |
| 环境缺失（ifcase 无 csproj） | 0 | **1** | +1 |
| build 警告/错误 | 0/0 | **0/0** | = |

**131 例交集集（可比口径）**：matrix5 92/131（70.2%）→ **matrix6 97/131（74.0%，+5）**——净增 5（8 例新绿 − 3 例回归）。

**PASS 率变化**：70.2% → 69.7%（全量，新增 23 例中 12 FAIL + ifcase 拉低）/ **74.0%**（131 交集）。8 例新绿全部 net48 live 逐字节确认；**3 例回归全部为 M6 include 表重建后 generated.h 缺 runtime 头**（crypto_obj.h×2 + timezone.h×1，见 §回归分析）。

## 批次分配

| 批次 | 范围 | 用例 | PASS | FAIL |
|---|---|---|---|---|
| 批次 1（batch1.md） | #1-40 aescase→delegate2 | 40 | 29（72.5%） | 11 |
| 批次 2（batch2.md） | #41-80 delegatecase→listops | 40 | 24（60.0%） | 16 |
| 批次 3（batch3.md） | #81-155 lockcase→xmlcase | 75 | 55（73.3%） | 20 |

三批结果与权威全量 run（results.tsv）逐行核对 100% 一致。

## 全量统计表（155 用例）

### FAIL 47 例（按阶段）

**transpile 18**：arrayext2、collchain、delecb2、delegatecb、dictcase、encodingcase、excext、genlist、hashtable2、hashtablecase、iocase、linqcase、parsecase、strcomp2、streamcase、typecheckcase、webcase、xmlcase

**cmake build 21**：aescase、boxdeep2、cryptocase、decimalcase、delechain、delegate2、delegateinst、enumdeep、gcext、hashcase、hashchain、listops、lockcase、netcase、numchain、recurse、refinvoke、reflectcase、socketcase、stackq、timezonecase

**diff 7**：bitopcase、datetime2、mathdeep、randomcase、strcompare、strjoin、timeext

**环境缺失 1**：ifcase（无 csproj，非转译器问题）

### PASS 108 例（按字母序）

arith、arraybounds、arraydeep、arrayext、arrays、arrcomp、arrdeep、base64case、bitconvcase、boxdeep、boxext、boxext2、boxing、boxing2、boxstr、boxunbox、branch、cctor、cctorchain、charstr、concatcase、consolecase、consoleio、convcase、convdeep、convertcase、crossstat、culturecase、datetimechain、delegatecase、delegates、enumflags、enumops、environment、environment2、exceptionchain2、exceptions、exdeep、filechain、filestreamcase、fmtchain、formatcase、formatext、gccase、gcstress、generics、guidcase、hexcase、interface、interlockedcase、intern、internchain、iofmt、loop、loopdeep、marshalcase、math2、mathcase、mathext、mathextra、matrixcase、methods、objects、overflow、overloads、paramroot、pathext、pinvoke、pinvokeext、processcase、propcase、props、recurse2、recursive_arith、regdeep、regexcase、regexcase2、sbchain、sblong、sortcase2、sortdeep、statics、strdeep、strfmt、string、stringbuilder、stringext、stringext2、stringops、stringrwcase、strpath、swcase、swcase2、threadcase、threadcase2、threadpoolcase、timecase、timeops、typechain、uintcase、uintcmp、uricase、valdeep、valuesem、valuetypes、versioncase、virtual、weakrefcase

## 新增用例（23 例，matrix5 无此目录；另有 ifcase 在 matrix5 已存在但无 csproj 未计数）

| 用例 | 结果 | 阶段 | 现象 |
|---|---|---|---|
| arrcomp | **PASS** | | |
| arrdeep | **PASS** | | |
| boxunbox | **PASS** | | |
| charstr | **PASS** | | |
| consoleio | **PASS** | | |
| delecb2 | FAIL | transpile | 返回类型 GenericTypeInstance 不受 M3 支持 |
| enumops | **PASS** | | |
| hashchain | FAIL | build | generated.h 缺 crypto_obj.h → icall_md5/sha1_create undeclared |
| iofmt | **PASS** | | |
| listops | FAIL | build | ArrayList_class 未声明 + arraylist create/get_count/add 无成员（D-AL-1/2） |
| mathdeep | FAIL | diff | 19 处 NaN/Inf 边界 expected False actual True |
| numchain | FAIL | build | try_parse temp loc12-14 未声明 + icall_try_parse_double 不存在 |
| pathext | **PASS** | | |
| recurse2 | **PASS** | | |
| refinvoke | FAIL | build | il2cpp_typeinfo_get_method 未映射 |
| reflectcase | FAIL | build | il2cpp_type_get_type 未映射 |
| stackq | FAIL | build | Stack_class 未声明 + stack create/push/peek 缺（D-STK-1/2） |
| strcomp2 | FAIL | transpile | newarr element type System.Char not supported |
| streamcase | FAIL | transpile | FileStream ctor 未接线（handle factory vs object ctor） |
| strjoin | FAIL | diff | 生成 exe 崩 0xC0000374 heap corruption，0 输出 vs 30 True |
| strpath | **PASS** | | |
| timeops | **PASS** | | |
| webcase | FAIL | transpile | WebClient.DownloadString 缺 managed 单调用 wrapper |

> 新增用例 PASS 11：arrcomp/arrdeep/boxunbox/charstr/consoleio/enumops/iofmt/pathext/recurse2/strpath/timeops。新增 FAIL 12：delecb2/hashchain/listops/mathdeep/numchain/refinvoke/reflectcase/stackq/strcomp2/streamcase/strjoin/webcase。

## 本轮净增 PASS（8 例，matrix5 FAIL→PASS，全部 net48 live 逐字节确认）

1. **cctorchain** — matrix5 diff（boom_caught 1 vs 2）→ PASS（cctor 守卫接线落地，批次15 CodeGenerator 改动）
2. **culturecase** — matrix5 cmake_build（`::lit::` 折叠残留）→ PASS
3. **internchain** — matrix5 diff（StringPool T3 Concat 池化回归）→ PASS（**matrix5 唯一回归已修复**）
4. **pinvoke** — matrix5 cmake_build（链接 undefined mylib 符号）→ PASS
5. **pinvokeext** — matrix5 cmake_build（同）→ PASS
6. **sblong** — matrix5 diff（L2 越界崩 6/31）→ PASS
7. **typechain** — matrix5 transpile（callvirt on non-instance）→ PASS
8. **weakrefcase** — matrix5 diff（0xC0000005 崩 0/9）→ PASS

## 回归分析（3 例，matrix5 PASS→matrix6 FAIL，全部同一根因）

**全部为 M6 generated.h include 表重建后缺失 runtime 扩展头**——matrix5 曾修复（补 include 行）但未进 git，M6 include 表重建覆盖丢失：

1. **cryptocase**（build）— generated.h 缺 `crypto_obj.h`：icall_md5/sha1/sha256_create undeclared。
2. **hashcase**（build）— 同 crypto_obj.h 缺失（matrix5 时该 include 修复后 PASS）。
3. **timezonecase**（build）— generated.h 缺 `timezone.h`：MethodMap.TimeZone 域（batch-14 在途）发射 il2cpp_timezone_get_utc_offset/datetime_to_local/from_local/is_daylight_saving 4 符号，全 undeclared；`git log -S 'timezone.h'` 全程无提交、`git log -S 'crypto_obj.h'` 也无提交——两次 include 修复均只在工作树存在。

> **修正批次2 报告**：batch2 称 hashchain 为 matrix5 回归，但 `git log/grep` 证实 **hashchain 在 matrix5 中不存在**（新增用例），仅 hashcase 是真回归。hashchain 归入新增 FAIL。

## 失败归因分类（matrix6，47 例）

### 1. 转译器接线缺（LOUD 中止）— 18 例（38.3%）
- 未接 BCL 映射：arrayext2（Array.FindIndex）、collchain（ArrayList.Remove）、delegatecb（GetFunctionPointerForDelegate）、encodingcase（GetChars 分配形）、excext（get_Message）、genlist（泛型 0x0E）、hashtable2/hashtablecase（WriteLine(object) box）、iocase（Path.Combine 3 参）、parsecase（Convert.ToInt32 fromBase）、streamcase（FileStream ctor）、webcase（WebClient.DownloadString）、strcomp2（newarr Char）
- 泛型/TypeSpec 缺口：dictcase/linqcase（泛型实参 0x0E/0x02）、typecheckcase（TypeSpec 0x1D）
- 对象域未映射：delecb2（GenericTypeInstance 返回）、xmlcase（arg cast 'node'）

### 2. 转译器生成码 bug（转译成功但 C++ 编译失败）— 21 例（44.7%）
- **include 表回归 3**：cryptocase/hashcase/hashchain（crypto_obj.h）、timezonecase（timezone.h）
- 类描述符/命名空间缺：boxdeep2（ToString/UInt32_class）、decimalcase（Decimal_sf_One + arity）、enumdeep（ToString_class + enum type）、gcext（WaitForPendingFinalizers 分类）、listops（ArrayList_class）、stackq（Stack_class）
- 类型错配/未声明符号：delechain（int32↔object）、delegate2（Delegate get_Method）、delegateinst（temp 槽错位）、lockcase（monitor int32↔object + label 重定义）、netcase/socketcase（il2cpp_ipaddress_parse 不存在）、numchain（try_parse temp + 符号缺）、recurse（entry 数组元素类型）、refinvoke（typeinfo_get_method）、reflectcase（type_get_type）、aescase（crypto icalls 缺 include）

### 3. runtime 缺失/错误（构建运行成功，输出不一致）— 7 例（14.9%）
- **崩溃**：strjoin（0xC0000374 heap corruption，0/30）
- **格式/语义**：bitopcase（~10 处 False）、datetime2（5 处 False）、mathdeep（19 处 NaN/Inf 边界 False）、randomcase（RNG 时序值 diff）、strcompare（4 处 False）、timeext（文化 + "y" 说明符崩）

### 4. 环境缺失 — 1 例（2.1%）
- ifcase（无 csproj fixture）

## 结论

1. **matrix6 全量 108/155 PASS（69.7%）**；131 交集 97/131（74.0%，**+5** vs matrix5 92/131）。8 例新绿全 net48 live 确认，**matrix5 唯一回归 internchain 已修复**。
2. **3 例回归同一根因**：M6 include 表重建丢失 crypto_obj.h（cryptocase/hashcase）+ timezone.h（timezonecase）——matrix5 的 include 修复只在工作树、未 commit，被重建覆盖。**最高优先修复项**：CodeGenerator.cs generated.h include 表补 `crypto_obj.h` 与 `timezone.h`（两行）。
3. 剩余 47 例主因：生成码 bug/build 21（44.7%，含 include 回归 3 + 类描述符缺 4）、转译器接线缺 18（38.3%）、runtime 输出不一致 7（14.9%，含 strjoin 新 heap 崩溃）、环境 1。
4. 与 matrix5 相比净推进：新增用例 PASS 11/24；新绿 8（cctorchain/culturecase/internchain/pinvoke/pinvokeext/sblong/typechain/weakrefcase）全部落在批次 15 修复面内。
