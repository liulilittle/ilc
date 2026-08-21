# e2e Recheck 4 — 新接线域用例复验（2026-08-04）

任务：e2e 复验 8 个新接线域用例（interlockedcase / versioncase / base64case / guidcase / encodingcase / bitconvcase / collchain / math2）。只读用例 + 写文档，不修、不 commit。

## 前置状态核查

| 检查项 | 结果 |
|---|---|
| il2cppc.dll 新鲜度 | **旧**（11:22:35）vs 源（CodeGenerator.cs 13:33、StackFold/IlDecoder 13:16、MethodMap.Interlocked 12:17）→ **已 dotnet build -c Release 重建** → 13:44:51，0 警 0 错 |
| MethodMap 文件 mtime | 相关 MethodMap 全部 ≥30 分钟稳定（最新 Interlocked 12:17=85min；CodeGenerator.cs 13:33 为并行代理在途，构建时已稳定） |
| 用例存在性 | 8/8 glob 确认存在（.cs + .csproj + expected.txt）；**math2 例外：expected.txt 不在 git（ls-files 仅 .cs/.csproj）** |
| encodingcase 判定 | Encoding 接线**未收工**：dotnet test --filter Encoding → **5 失败/8**（MethodMapTests.Encoding.cs 红）→ 按任务规则**跳过并记录** |

## 状态表（7 跑 + 1 跳过）

| 用例 | 转译 | 构建(MinGW) | 运行 | diff expected.txt | 结论 |
|---|---|---|---|---|---|
| interlockedcase | PASS | **FAIL**（2 err） | — | — | FAIL |
| versioncase | PASS | **FAIL**（3 err） | — | — | FAIL |
| base64case | **FAIL**（1 err） | — | — | — | FAIL |
| guidcase | **FAIL**（1 err） | — | — | — | FAIL |
| encodingcase | — | — | — | — | SKIP（Encoding 5/8 单测红，接线在途） |
| bitconvcase | **FAIL**（1 err） | — | — | — | FAIL |
| collchain | **FAIL**（1 err） | — | — | — | FAIL |
| math2 | PASS | PASS | **FAIL**（exit 1，6 行后崩） | NO_EXPECTED | FAIL |

汇总：转译 3/7 过；构建 1/3 过；运行 0/1 过；**最终 PASS 0/7，FAIL 7/7（+1 skip）**。
net48 参考运行 8/8 exit=0（用例源侧健康；math2 参考输出 60 行完整）。

## 缺口清单（每 FAIL 精确原因）

| # | 用例 | 阶段 | 精确原因（证据） |
|---|---|---|---|
| G1 | base64case | 转译 | `ldtoken FIELD token 0x04000002 at offset 0x0007` → RuntimeHelpers.InitializeArray 数组初始化器模式无 M3 fold（StackFold.cs:735 抛）。net48 `new byte[]{...}` 生成 ldtoken+InitializeArray，已知最大 M3 缺口 |
| G2 | bitconvcase | 转译 | `System.BitConverter::GetBytes: RawSignature leaks the byte[] return element byte into ps[0] (SZARRAY ret 0x1D does not consume its 0x05 element), so every overload resolves identically`（MethodRefResolver.cs:366 LOUD 守卫触发）——GetBytes 重载（byte[]/int/…）全部同形不可分派 |
| G3 | guidcase | 转译 | `IL opcode 0xFE16 at offset 14 not supported in M1`（IlDecoder.cs:251）——0xFE16=**constrained.** 前缀（Guid 为 struct，`g.ToString()` 等 callvirt 带 constrained 前缀） |
| G4 | collchain | 转译 | `callvirt on non-instance method (token 167772198)`（StackFold.cs:1035）——token 0x0A000026 = **System.Collections.ArrayList::get_Count**（ildump 实证）。ArrayList 域未接线：无 MethodMap.ArrayList.cs、无 runtime arraylist icall、无 BclAllocExpr；用例头注释预告"fallback → C++ 构建错"，实际失败形态为转译期 LOUD abort（Add 走 fallback 未炸，get_Count 触发非实例分类错） |
| G5 | interlockedcase | 构建 | 2 err：`gen/src/InterlockedCase.cpp:64,67 cannot initialize a parameter of type 'int32_t *' with an rvalue of type 'int64_t *'` —— ref-long Increment/Decrement 落 int32 icall（RawSignature 不消费 BYREF 元素字节，ps=[0x10] 歧义 → 默认 int32）。已知缺口，C++ 编译期 LOUD 不静默 |
| G6 | versioncase | 构建 | 3 err（gen/src/Cases_VersionCase.cpp）：(a) :59 `System_Version_class` undeclared —— BclAllocExpr 无 System.Version 条目；(b) :60 `il2cpp_version_parse(t0, (Il2CppString*)(...))` 参 1 收 const char* 收到 Il2CppObject*；(c) :62 `il2cpp_version_parse(loc0)` expected 2 args have 1。**根因 = MethodMap.Version.cs:8-35 文档化 RUNTIME OBJECT GAP**：versionuri.h 面是 `il2cpp_version_parse(const char*, int32_t*)` 纯 C++ 函数（versionuri.h:49-53），非对象 icall，映射按"路由 fold"设计，发射 ABI 必然不匹配 → 按设计 LOUD |
| G7 | math2 | 运行 | 前 6 行与 net48 逐字节一致，第 7 行起 `Unhandled managed exception: Negating the minimum value of a twos complement number is invalid.` 未处理退出（exit 1）——`try { Math.Abs(int.MinValue) } catch (OverflowException)` 的 catch 未命中/未发射（net48 打印 OverflowException×2 后继续 54 行）。+ **用例缺口：expected.txt 不在仓库**（git ls-files 仅 .cs/.csproj），diff 无可比基准 |
| G8 | encodingcase | 跳过 | Encoding 域未收工：MethodMapEncodingTests 5/8 红（GetChars/GetString ThrowsLoud + StaticGetter 形态漂移为 BadImageFormatException Read-out-of-bounds，MethodSignatureCoder.cs:54） |

## 施工单（按解除阻塞面排序）

1. **M3 ldtoken FIELD fold**（StackFold.cs:715）→ 解 base64case（兼最大 e2e 覆盖面缺口，byte[] 初始化器模式）
2. **RawSignature SZARRAY-ret 元素字节消费**（MethodRefResolver.cs ReadParamByte/签名解码）→ 解 bitconvcase GetBytes 重载分派 + 兼 G5
3. **IlDecoder M1 0xFE16 constrained. 前缀** → 解 guidcase（struct 实例方法 callvirt）
4. **ArrayList 域**（MethodMap.ArrayList.cs + arraylist icall + BclAllocExpr + class descriptor）→ 解 collchain
5. **Interlocked ref-long 接线**（BYREF 元素消费或 local-kind 跟踪）→ 解 interlockedcase 剩余 2 行
6. **Version 对象化 icall**（icall_version_ctor 存组件 + getter + BclAllocExpr System.Version + 类描述符；MethodMap.Version.cs:32-35 已写清要求）→ 解 versioncase
7. **Math Abs(int.MinValue) EH**：runtime math.cpp 取负守卫抛出的 OverflowException 未被 catch 命中（il2cpp_exception_matches/类型链）→ 解 math2 运行；补 math2 expected.txt 入库
8. **Encoding 接线收工**（MethodSignatureCoder 越界 + GetChars/GetString/静态 getter）→ 解 encodingcase

## 方法记录（可复用）

- 脚本 `%TEMP%\opencode\run-e2e-recheck4.ps1`：ForEach-Object -Parallel 7 路，逐用例：net48 build → 参考运行 → `dotnet <il2cppc.dll> out/<name>.exe gen`（直跑）→ cmake MinGW 独立 `build_recheck4_<name>` → 运行 → diff expected.txt + diff vs net48 live；per-case CSV 增量落盘（recheck4/*.csv）。
- diff 为 LF-join 逐字节（run-tests.ps1 同法）；FAIL 记首差异行号 + exp/act 行数。
- 转译失败详情重跑捕获消息首行（stack 尾 3 行不足以定位）；collchain token→名字用 ildump 探针（%TEMP%\opencode\ildump，本任务加了 token 打印）。
- 并行代理在途 CodeGenerator.cs 13:33 已含于本次重建 DLL（0 警 0 错，编译通过即稳定）。

## 证据

- [BASH] dotnet build src/il2cppc -c Release → 0 警 0 错，dll 13:44:51
- [BASH] dotnet test --filter Encoding → 失败 5，通过 3（总计 8）
- [BASH] recheck4/*.csv 7 份（transpile/build/run/diff 逐阶段退出码）
- [BASH] 4 用例转译错误消息首行（ldtoken FIELD / RawSignature SZARRAY / 0xFE16 / callvirt non-instance）
- [BASH] ildump collchain.exe → `0x0A000026 -> System.Collections.ArrayList::get_Count`
- [READ] MethodMap.Version.cs:8-35（文档化 object gap）、versionuri.h:49-53（真签名）
- [BASH] math2 actual vs net48 live（6 行一致后 unhandled negation 异常）
- [BASH] git ls-files tests/cases/math2 → 仅 math2.cs/.csproj（无 expected.txt）
