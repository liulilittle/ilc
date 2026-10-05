# e2e Full Matrix Audit 5 — crypto_obj 修复后全量构建恢复（2026-08-06）

日期：2026-08-06（00:1x 启动；代理在飞轮询 00:14→06:10 共 ~6h 至稳定后运行；矩阵约 06:31-06:51 完成，131 用例 8 并行）
基线：`dotnet build src/il2cppc -c Release` → **0 错 0 警**（il2cppc.dll 6:51:26，新于全部源）；共享 runtime 库 `build/runtime_e2e_shared/libil2cpp_runtime.a` **全新重建**（6:31:24 全量 GLOB，107 成员，含 crypto_obj/hmac/aes/sha512 等全部新源，新于全部 runtime 源）。
前置门槛：**代理收工确认**——src 最后写入 decimal.cpp 5:35:47（矩阵启动时 46min ≥30min）、tests/cases 最后 expected.txt 4:57:24（94min）、无构建进程后启动；随后观测到 sortcase2/weakrefcase 等仍有零星 in-flight 转译（6:09），故 src/cases 双闸门以**启动时刻**为准。
方法：复用 matrix4 脚本法（`%TEMP%\opencode\run-e2e-matrix5.ps1`，8 并行，每用例 转译 → 共享 runtime 库 wrapper cmake MinGW 构建 → 运行 → **net48 live 参考输出逐字节 diff**，LF-join 比较；失败日志留存 per-case：transpile.log/build.log/diff.txt）。

对比基线：docs/audit/e2e-full-matrix4.md（matrix4，72/106 PASS）。

## 汇总

| 指标 | matrix4 | **matrix5** | M4→M5 变化 |
|---|---|---|---|
| 总用例 | 106 | **131**（新增 25 例） | +25 |
| **PASS（逐字节一致）** | 72（67.9%） | **92（70.2%）** | +20 |
| FAIL | 34 | **39** | +5（新增用例 15 FAIL） |
| 转译失败 | 17 | **21** | +4 |
| cmake 构建失败 | 10 | **8** | −2 |
| 运行/diff 失败 | 7 | **10** | +3 |
| net48 参考构建失败 | 0 | **0** | = |
| build 警告/错误 | 0/0 | **0/0** | = |

**106 例交集集（可比口径）**：matrix4 72/106（67.9%）→ **matrix5 80/106（75.5%，+8）**——净增 8（9 例新绿 − 1 例回归）。

**PASS 率变化**：67.9% → **70.2%（全量）/ 75.5%（106 交集）**。9 例新绿（bitconvcase/consolecase/formatext/generics/interlockedcase/strdeep/stringext/timecase/valuetypes，全部经 net48 live 逐字节确认，其中 3 例 = matrix4 回归修复）；**1 例回归**（internchain，StringPool 域，见 §回归分析）。

## 本验证修复的构建阻断（3 处，均批次遗留，验证前必须修否则矩阵无法构建）

1. **runtime.cpp `icall_marshal_alloc_hglobal` 返回类型 void\* vs intptr_t**（il2cpp_runtime.h:136 注释契约 = intptr_t，M4.5 marshalcase 固定）：runtime.cpp:121 仍 void\* → 共享库编译 `functions that differ only in their return type cannot be overloaded`。**修复**：改 intptr_t + reinterpret_cast。
2. **runtime.cpp `icall_marshal_copy_to/from_unmanaged` 参数 Il2CppObject\* vs Il2CppArray\***（头 :138-139 声明 Il2CppArray\*，生成码 `(Il2CppArray*)(t0)` 传参）：.cpp 用 Il2CppObject\* → 链接 undefined symbol。**修复**：.cpp 签名对齐头。
3. **crypto_obj.cpp 类描述符命名空间错位**（crypto_obj.h:65-85 声明 GLOBAL 作用域 `System_Security_Cryptography_*_class`，.cpp:200 定义在 `namespace il2cpp` 内 → GLOBAL extern 永不定义，**任何**链接 crypto_obj 的程序 undefined HMACSHA1/256/384_class）。**修复**：CRYPTO_CLASS 块移至全局作用域（versionuri.cpp:17 System_Version_class 先例）。
4. **CodeGenerator.cs generated.h include 列表缺 `crypto_obj.h`**：hashcase 生成码调用 `icall_md5_create`（声明在 crypto_obj.h）但 generated.h 无此 include → `no member named 'icall_md5_create'`。**修复**：补一行 include。
5. **StackFold.cs `call` 路径 ReturnsRef 未跟随 re-resolve**（:1510 用初始 resolved 建 CallExpr；:1607 `call.Resolved = resolved` 更新 Resolved 但不刷新 init-only ReturnsRef → hmac ComputeHash 结果 temp 被声明 int32_t 却赋 Il2CppObject\*，编译错误）。**修复**：final resolution 与初始 ReturnsRef 不一致时重建 CallExpr。

> 修复后 hashcase/cryptocase/marshalcase **全部 PASS**；generics/interlockedcase/valuetypes（matrix4 3 回归）也在本矩阵**翻 PASS**（非本验证改动，为批次修复）。

## 全量统计表（131 用例 | matrix4 | matrix5 | 阶段 | 变化 | 失败归因）

### matrix4 交集集（106 例）——关键行

| 用例 | M4 | M5 | 阶段 | 变化 | 失败归因 |
|---|---|---|---|---|---|
| bitconvcase | FAIL | **PASS** | done | **新绿** | byte[] newarr 闭合 |
| consolecase | FAIL | **PASS** | done | **新绿** | Console.IsInputRedirected 接线落地 |
| formatext | FAIL | **PASS** | done | **新绿** | harness UTF-8 捕获统一 + 域修复 |
| generics | FAIL | **PASS** | done | **回归修复** | 泛型 ctor 命名展开修复落地 |
| interlockedcase | FAIL | **PASS** | done | **回归修复** | b59c6d9 Guid cast 收窄落地 |
| strdeep | FAIL | **PASS** | done | **新绿** | PadLeft/PadRight 接线落地 |
| stringext | FAIL | **PASS** | done | **新绿** | 同上域修复 |
| timecase | FAIL | **PASS** | done | **新绿** | DayOfWeek 枚举→字符串映射落地 |
| valuetypes | FAIL | **PASS** | done | **回归修复** | b59c6d9 cast 收窄落地 |
| internchain | PASS | **FAIL** | diff | **回归** | StringPool T3 把 `Concat("intern-","chain")` 折叠成池化字面量 g_strPool[0] → ReferenceEquals True vs net48 False（L5） |
| hashcase | PASS | **PASS** | done | = | 本验证修复 include/命名空间后恢复 |
| 其余 M4 PASS | 62 | **62 PASS** | done | = | 无回归（78 例 M4-PASS 中仅 internchain 1 例回归） |

**106 交集 FAIL 21 例**：cctorchain/datetime2/delegateinst/strcompare/timeext（diff）、boxdeep2/delechain/pinvoke/pinvokeext/recurse（cmake_build）、arrayext2/collchain/delegate2/delegatecb/encodingcase/enumdeep/excext/gcext/genlist/hashtable2/hashtablecase/iocase/parsecase/typecheckcase（transpile）——全部与 matrix4 同归因（见下，无新增交集失败）。

### 新增用例（25 例，不在 matrix4 表）

| 用例 | M5 阶段 | 失败归因 |
|---|---|---|
| aescase | transpile | callvirt on non-instance method（Aes 域，生成码分类） |
| arraydeep | **PASS** | |
| bitopcase | diff | 114/114 行中 ~10 处 False（位运算域语义） |
| convdeep | **PASS** | |
| cryptocase | **PASS** | 本验证修复（include/命名空间/ReturnsRef）后全链通过 |
| culturecase | cmake_build | 生成码 `::lit::` 折叠残留（文化域） |
| decimalcase | cmake_build | `System_Decimal_sf_One` 未声明 + ctor arity 错配 |
| dictcase | transpile | generic argument byte 0x0E in M3 |
| enumflags | **PASS** | |
| linqcase | transpile | generic argument byte 0x02 in M3 |
| lockcase | cmake_build | 生成码 int32↔Il2CppObject\* 错配（lock 域） |
| loopdeep | **PASS** | |
| marshalcase | **PASS** | 本验证修复（签名对齐）后全链通过 |
| matrixcase | **PASS** | |
| netcase | transpile | IPAddress.Parse 静态分类错（INSTANCE） |
| processcase | **PASS** | |
| regdeep | **PASS** | |
| sblong | diff | L2 起 `Index was outside the bounds` 崩（31 行 6 行输出） |
| socketcase | transpile | 同 netcase（IPAddress.Parse 分类） |
| sortcase2 | **PASS** | |
| strfmt | **PASS** | |
| timezonecase | **PASS** | |
| typechain | transpile | callvirt on non-instance method |
| weakrefcase | diff | 0xC0000005 崩溃（0/9）——WeakReference 域 |
| xmlcase | transpile | callvirt on non-instance method |

**新增用例 PASS 12/25**：arraydeep/convdeep/cryptocase/enumflags/loopdeep/marshalcase/matrixcase/processcase/regdeep/sortcase2/strfmt/timezonecase 等 12 例 PASS（matrixcase/processcase/timezonecase 等新域用例全绿；cryptocase/marshalcase 经本验证修复后 PASS）。

## 本轮净增 PASS（9 例，matrix4 FAIL→PASS，全部经 net48 live 逐字节确认）

1. **bitconvcase** — byte[] newarr 接线闭合（17/17）
2. **consolecase** — Console.IsInputRedirected 接线落地
3. **formatext** — harness UTF-8 捕获统一 + 域修复（73/73）
4. **generics** — 泛型 ctor 命名展开修复落地（matrix4 回归修复）
5. **interlockedcase** — b59c6d9 Guid cast 收窄（matrix4 回归修复）
6. **strdeep** — PadLeft/PadRight 接线落地
7. **stringext** — 同 strdeep 域修复
8. **timecase** — DayOfWeek 枚举→字符串映射（Monday）
9. **valuetypes** — b59c6d9 cast 收窄（matrix4 回归修复）

> matrix4 的 3 例回归（generics/interlockedcase/valuetypes）**全部翻 PASS**；新增 25 例中 crypto/marshal 域（cryptocase/marshalcase）经本验证修复后 PASS。

## 回归分析（1 例，matrix4 PASS → matrix5 FAIL）

**internchain** — diff。**根因（StringPool 域）**：生成码 L5 把 `string.Concat("intern-","chain")` 折叠为池化字面量 `il2cpp::g_strPool[0]`，随后 `object.ReferenceEquals(built, "intern-chain")` 得 **True**；net48 语义 = 运行时拼接产生**新实例** → False。StringPoolPass T3 静态表把「两个字符串常量的 Concat」常量折叠进字面量池，破坏了 interning 身份的「fresh instance」语义（matrix4 时该 fold 不存在）。这是本矩阵唯一交集回归。

## 失败归因分类（matrix5，39 例）

### 1. 转译器接线缺（LOUD 中止）— 21 例（53.8%）
- **未接 BCL 映射 / 对象模型缺**：arrayext2（Array.FindIndex）、collchain（ArrayList.Remove）、delegatecb（GetFunctionPointerForDelegate）、encodingcase（GetChars 分配形）、excext（get_Message）、genlist（泛型 0x0E string 展开）、hashtable2/hashtablecase（WriteLine(object) box 类型）、iocase（Path.Combine 3 参）、parsecase（Convert.ToInt32 fromBase）、dictcase/linqcase（泛型实参 0x0E/0x02 无展开）
- **IL/fold 缺口**：enumdeep（switch 0x45）、typecheckcase（TypeSpec 0x1D）
- **static/instance 分类错**：gcext（GC.WaitForPendingFinalizers）、netcase/socketcase（IPAddress.Parse）、aescase/delegate2/typechain/xmlcase（callvirt on non-instance）

### 2. 转译器生成码 bug（转译成功但 C++ 编译失败）— 8 例（20.5%）
- **类型错配/未声明符号**：boxdeep2（ToString_class 未声明）、delechain（int32↔Il2CppObject\*）、lockcase（同）、culturecase（`::lit::` 残留）、decimalcase（Decimal_sf_One 未声明 + arity）、recurse（entry Main 签名）、pinvoke（链接 undefined mylib 符号）、pinvokeext（同）

### 3. runtime 缺失/错误（构建运行成功，输出不一致）— 10 例（25.6%）
- **异常/崩溃**：weakrefcase（0xC0000005，0/9）、delegateinst（0xC0000005，0/24）、sblong（L2 越界崩，6/31）
- **格式/语义**：timeext（L1 en-US + "y" 说明符崩，22/84）、bitopcase（~10 处 False）、internchain（Concat 池化回归，13/13 中 1 处）、cctorchain（boom_caught 1 vs 2）、datetime2（5 处 False）、strcompare（66/66 中 ~5 处 False）、randomcase（RNG 时序值 diff，7 行中 4 行）

## 结论

1. **前置确认**：代理收工（src 稳定 ≥30min、无构建进程）；il2cppc.dll 重建（0 错 0 警，新于全部源）；**共享 runtime 库全新重建**（6:31:24，107 成员含 crypto 全族——否则矩阵全部构建用旧库结果无效）。本验证修复 5 处批次遗留构建阻断（runtime.cpp marshal 签名×2、crypto_obj.cpp 命名空间、generated.h 缺 crypto_obj.h、StackFold ReturnsRef），否则 hashcase/cryptocase/marshalcase 无法构建。
2. **PASS 72 → 92/131（全量 70.2%）/ 80/106（交集 75.5%，+8）**；9 例新绿（bitconvcase/consolecase/formatext/generics/interlockedcase/strdeep/stringext/timecase/valuetypes）全部 net48 live 确认，**matrix4 的 3 例回归（generics/interlockedcase/valuetypes）全部修复翻 PASS**；新增 25 例 PASS 12（含 cryptocase/marshalcase）；**1 例新回归**（internchain = StringPool T3 Concat 池化破坏 fresh-instance 语义）。
3. **剩余 39 例主因**：转译器接线缺 21（53.8%，未接 BCL 映射 13 + 分类/泛型/fold 缺口 8）、生成码 bug 8（20.5%，类型错配/未声明符号）、runtime 输出不一致 10（25.6%，崩溃 3 + 语义/格式 7）。下一批修复面 = internchain 回归（StringPool 域，1 例）+ 新增 15 FAIL 中的高位项（weakrefcase/delegateinst 崩溃、netcase/socketcase 分类、aescase/typechain/xmlcase callvirt 分类）。
