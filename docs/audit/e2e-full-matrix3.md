# e2e Full Matrix Audit 3 — 多批修复后全量重跑（2026-08-05）

日期：2026-08-05（01:57 快照；矩阵运行约 01:53 完成）
基线：`dotnet build src/il2cppc -c Release` → **0 错 0 警**（dll 01:45:21 新于全部真实源，最新源 StackFold.cs 01:20:29）；共享 runtime 库 01:45:30（新于 runtime 源 console.cpp 01:11:39 / il2cpp_console.h 01:13:24）。
前置门槛：**在途代理已收工确认**——任务启动时代理仍在活跃编辑（最后一笔 src 写 StackFold.cs 01:20:29），轮询 5 轮至 01:51:29 达 **≥30 分钟稳定**后重跑；运行中的 dotnet 进程 01:31 属 D:\svn\ucp（Ucp.Tests），与 il2cpp 无关。
方法：复用 e2e-full-matrix2 脚本法（`%TEMP%\opencode\run-e2e-matrix3.ps1`，8 并行，每用例 转译 → 共享 runtime 库 + 独立 gen 目录 cmake MinGW 构建 → 运行 → net48 live 参考输出逐字节 diff，LF-join 比较）。

对比基线：docs/audit/e2e-full-matrix2.md（matrix2，39/88 PASS）。

## 汇总

| 指标 | matrix1 | matrix2 | **matrix3** | M2→M3 变化 |
|---|---|---|---|---|
| 总用例 | 88 | 88 | **88** | = |
| **PASS（逐字节一致）** | **38（43.2%）** | **39（44.3%）** | **47（53.4%）** | **+8** |
| FAIL | 50 | 49 | **41** | −8 |
| 转译失败 | 26 | 25 | **26** | +1 |
| cmake 构建失败 | 7 | 6 | **2** | −4 |
| 运行/diff 失败 | 16 | 17 | **12** | −5 |
| net48 参考构建失败 | 1 | 1 | **1** | = |
| build 警告/错误 | 0/0 | 0/0 | **0/0** | = |

**PASS 率变化**：44.3% → **53.4%（+8 例）**。**零回归**（matrix2 全部 39 个 PASS 在 matrix3 全部保持 PASS）。

> 注：e2e-recheck7 曾按"39 + 5（本批 10 例内 5 例翻 PASS）"推估聚合为 44/88。全量重跑验证该推估并**多出 3 例**（boxing2 / interlockedcase / mathcase），实得 **47/88**，且该 5 例（math2/boxext/boxdeep/delegatecase/stringext2）全量确认保持 PASS。

## 全量统计表（用例 | matrix2 | matrix3 | 阶段 | 失败主因/备注）

| 用例 | M2 | M3 | 阶段 | 失败归因 |
|---|---|---|---|---|
| arith | PASS | **PASS** | done | |
| arraybounds | PASS | **PASS** | done | |
| arrayext | PASS | **PASS** | done | |
| arrayext2 | FAIL | FAIL | transpile | **前进**：ldtoken FIELD 已过 → 卡 `Array.FindIndex`（icall 取 (array,start,count) 但 .NET Predicate 重载不供给） |
| arrays | PASS | **PASS** | done | |
| base64case | FAIL | FAIL | transpile | Encoding.GetBytes raw-buffer LOUD（同 M2） |
| bitconvcase | FAIL | FAIL | transpile | **前进**：GetBytes 已接 → 卡 `BitConverter.ToString(byte[])` 无 hex 格式化面 |
| boxdeep | FAIL | **PASS** | done | **box 语义修复落地**（接口表 is/字符串描述符/catch 全对） |
| boxext | FAIL | **PASS** | done | **box/unbox 修复落地**（not-a-boxed 消除） |
| boxext2 | PASS | **PASS** | done | |
| boxing | PASS | **PASS** | done | |
| boxing2 | FAIL | **PASS** | done | **box 修复落地**（recheck7 时 console ulong LOUD 门已随代理补齐拆除） |
| boxstr | PASS | **PASS** | done | |
| branch | PASS | **PASS** | done | |
| cctor | PASS | **PASS** | done | |
| cctorchain | FAIL | FAIL | diff | boom_caught=1 vs 2（cctor 异常 catch 计数，15/15 行） |
| collchain | FAIL | FAIL | transpile | **前进**：dup 失步已过 → 卡 `ArrayList.Remove(object)` 无 icall |
| concatcase | PASS | **PASS** | done | |
| consolecase | FAIL | FAIL | transpile | Console.IsInputRedirected 未接线（同 M2） |
| convcase | FAIL | FAIL | diff | L60 uint→int 打印 -1 vs 4294967295（79/79 行） |
| convertcase | FAIL | FAIL | diff | L11 rounding 2 vs 3（30/30 行） |
| crossstat | PASS | **PASS** | done | |
| datetimechain | PASS | **PASS** | done | |
| delechain | FAIL | FAIL | cmake_build | 生成码 int32↔Il2CppObject* 类型错配（同 M2） |
| delegate2 | FAIL | FAIL | transpile | Ldsflda M3 fold 缺（同 M2） |
| delegatecase | FAIL | **PASS** | done | **0xC0000005 崩溃修复**（57/57 逐字节，委托修复落地） |
| delegatecb | FAIL | FAIL | transpile | Marshal.GetFunctionPointerForDelegate 泛型无映射（同 M2） |
| delegates | PASS | **PASS** | done | |
| encodingcase | FAIL | FAIL | transpile | Encoding.GetBytes raw-buffer LOUD（同 M2） |
| environment | PASS | **PASS** | done | |
| environment2 | FAIL | FAIL | diff | tick_delta 计时抖动 31 vs 32（用例自身，同 M2） |
| exceptions | PASS | **PASS** | done | EH 嵌套修复保持 |
| excext | FAIL | FAIL | transpile | Exception.get_Message 未接线（同 M2） |
| exdeep | FAIL | FAIL | diff | 31/37 行一致（M2 29/37 **+2**）；L29 f18 throw null→未处理崩级联 |
| filechain | PASS | **PASS** | done | |
| fmtchain | PASS | **PASS** | done | |
| formatcase | FAIL | FAIL | diff | L24 Infinity vs ∞（U+221E）（37/37 行） |
| formatext | FAIL | FAIL | diff | **重大进步**：0xC0000005 崩溃(0/73) → 73/73 行全出，L11 货币符号 � vs ¥ |
| gccase | FAIL | FAIL | diff | **前进**：cmake_build → 运行成功 4/4，L1 strhash same literal False vs True |
| gcext | FAIL | FAIL | transpile | WeakReference ctor 未接线（同 M2） |
| gcstress | PASS | **PASS** | done | |
| generics | PASS | **PASS** | done | |
| genlist | FAIL | FAIL | transpile | 泛型实参 0x0E 无 M6 展开（同 M2） |
| guidcase | FAIL | FAIL | transpile | **前进**：op_Equality 分类已过 → 卡 `callvirt owner System.Object` 未转译 |
| hashtable2 | FAIL | FAIL | transpile | WriteLine(object) 未跟踪 box 类型（同 M2） |
| hashtablecase | FAIL | FAIL | transpile | 同上 |
| interface | PASS | **PASS** | done | |
| interlockedcase | FAIL | **PASS** | done | **RawSignature BYREF 修复落地**（long Increment/Decrement 已接） |
| intern | PASS | **PASS** | done | |
| internchain | PASS | **PASS** | done | |
| iocase | FAIL | FAIL | transpile | Path.Combine 3 参未接线（同 M2） |
| loop | PASS | **PASS** | done | |
| math2 | FAIL | **PASS** | done | **-int.MinValue 溢出修复落地**（Round/Truncate 64/64 逐字节） |
| mathcase | FAIL | **PASS** | done | **double→string 精度修复落地** |
| mathext | FAIL | FAIL | diff | L22 Infinity vs ∞（100/100 行） |
| mathextra | PASS | **PASS** | done | |
| methods | PASS | **PASS** | done | |
| objects | PASS | **PASS** | done | |
| overflow | FAIL | FAIL | net48build | 用例自身：net48 编译期 checked 溢出 CS0220（同 M2） |
| overloads | PASS | **PASS** | done | |
| paramroot | PASS | **PASS** | done | |
| parsecase | FAIL | FAIL | transpile | Int32.TryParse ref/out 未接线（同 M2） |
| pinvoke | FAIL | FAIL | transpile | Marshal.GetLastWin32Error 静态/实例分类错（同 M2） |
| pinvokeext | FAIL | FAIL | transpile | Marshal.PtrToStringAnsi 静态/实例分类错（同 M2） |
| propcase | PASS | **PASS** | done | |
| props | PASS | **PASS** | done | |
| randomcase | FAIL | FAIL | transpile | String.Join 1 参未接线（同 M2） |
| recurse | FAIL | FAIL | cmake_build | entry.cpp Main 签名不匹配（同 M2） |
| recursive_arith | PASS | **PASS** | done | |
| regexcase | PASS | **PASS** | done | |
| sbchain | PASS | **PASS** | done | |
| statics | PASS | **PASS** | done | |
| strdeep | FAIL | FAIL | transpile | Array.Empty 泛型无映射（同 M2） |
| string | PASS | **PASS** | done | |
| stringbuilder | FAIL | FAIL | diff | L2 capacity 异常 vs 返回 16（36/40 行） |
| stringext | FAIL | FAIL | transpile | Array.Empty 泛型无映射（同 M2） |
| stringext2 | FAIL | **PASS** | done | **UTF-8 join 修复落地**（42/42 逐字节，Join(object[]) 8-byte blob 修复） |
| stringops | PASS | **PASS** | done | |
| swcase | FAIL | FAIL | transpile | Stopwatch.get_IsRunning 未接线（同 M2） |
| threadcase | FAIL | FAIL | transpile | **前进**：0xFE16 constrained 已过 → 卡 Thread.get_CurrentThread 无对象模型 |
| timecase | FAIL | FAIL | transpile | TimeSpan.get_TotalMinutes 未接线（同 M2） |
| timeext | FAIL | FAIL | diff | culture zh-CN vs en-US（22/84 行，同 M2） |
| typecheckcase | FAIL | FAIL | transpile | TypeSpec SZARRAY 0x1D isinst（同 M2） |
| uintcmp | PASS | **PASS** | done | |
| valuesem | FAIL | FAIL | diff | **前进**：cmake_build → 运行成功 43/43，L20 double hash NaN -524288 vs 2146959360 |
| valuetypes | PASS | **PASS** | done | |
| versioncase | FAIL | FAIL | transpile | **前进**：cmake_build → transpile LOUD（Version 对象 gap，无 ctor icall） |
| virtual | PASS | **PASS** | done | |

## 本轮净增 PASS（8 例，FAIL→PASS，全部经 net48 live 逐字节确认）

1. **boxing2** — box/unbox 语义修复（recheck7 时因 console ulong LOUD 暂未翻，全量下已 PASS）
2. **boxdeep** — box 接口表/字符串描述符修复（M2 26/32 → 32/32）
3. **boxext** — box/unbox not-a-boxed 消除（15/15）
4. **delegatecase** — 0xC0000005 崩溃修复（0/57 → 57/57）
5. **interlockedcase** — RawSignature BYREF 元素缺口修复（long Increment/Decrement 落地）
6. **math2** — -int.MinValue 负溢出 + Round/Truncate 修复（64/64）
7. **mathcase** — double→string 精度修复
8. **stringext2** — Join(object[]) 8-byte blob / UTF-8 乱码修复（42/42）

**对应提交**：boxing 系 + delegate 系（823d105 boxing stable batch 8）、math.cpp（2b19000）、stringext2（93cb337 utf8-mojibake）、interlocked/bitconv RawSignature（8ae3c04）。

## 失败归因分类（matrix3，41 例）

### 1. 转译器接线缺（LOUD 中止）— 26 例（63.4%）
- **未接 BCL 映射 / 对象模型缺**：consolecase（IsInputRedirected）、excext（get_Message）、gcext（WeakReference ctor）、iocase（Path.Combine 3 参）、parsecase（TryParse ref/out）、randomcase（String.Join 1 参）、swcase（get_IsRunning）、timecase（get_TotalMinutes）、threadcase（Thread.CurrentThread）、delegatecb（GetFunctionPointerForDelegate）、strdeep/stringext（Array.Empty）、genlist（泛型 0x0E 展开）、versioncase（Version 对象 gap）、encodingcase/base64case（GetBytes raw-buffer）
- **IL/fold 缺口**：delegate2（Ldsflda）、arrayext2（Array.FindIndex）、bitconvcase（ToString hex）、guidcase（callvirt System.Object）、collchain（ArrayList.Remove）
- **static/instance 分类错**：pinvoke、pinvokeext（Marshal 静态方法）
- **TypeSpec 泛型**：typecheckcase（SZARRAY 0x1D isinst）
- **Console 域**：hashtable2/hashtablecase（WriteLine(object) 未跟踪）

### 2. 转译器生成码 bug（转译成功但 C++ 编译失败）— 2 例（4.9%）
- **类型错配**：delechain（int32↔Il2CppObject*）
- **签名不匹配**：recurse（entry.cpp Main 调用签名）

### 3. runtime 缺失/错误（构建运行成功，输出不一致）— 12 例（29.3%）
- **数字/字符串格式**：convcase（uint→int -1）、convertcase（rounding 2/3）、formatcase/mathext（Infinity vs ∞）、formatext（货币符号）、mathext 归上、stringbuilder（capacity 异常 vs 返回）
- **hash**：valuesem（double NaN hash）、gccase（strhash same literal False）
- **异常**：cctorchain（catch 计数 1 vs 2）、exdeep（f18 throw null 级联）
- **culture**：timeext（zh-CN vs en-US）

### 4. 用例自身 — 2 例（4.9%）
- **overflow**：net48 编译期 checked 溢出 CS0220（参考无法生成）
- **environment2**：tick_delta 计时抖动（31/32 跨运行不等）

## 结论

1. **build 全绿**：`dotnet build src/il2cppc -c Release` → 0 错误 0 警告；dll 01:45:21 新于全部源（最新 StackFold.cs 01:20:29）；共享 runtime 库 01:45:30 同步重建。在途代理已收工（src 最后写入 01:20:29，01:51:29 确认 ≥30min 稳定）。
2. **PASS 39 → 47（44.3% → 53.4%），零回归**；净增 8 例 = box/delegate/Interlocked/math 三批 runtime 修复 + stringext2 utf8 修复（recheck7 推估 44 被全量证实并多 3：boxing2/interlockedcase/mathcase）。
3. **剩余 41 例主因**：转译器接线缺 26（未接 BCL 映射 + IL fold 缺口，占比升至 63.4%，因多数 runtime 缺口已修掉）、runtime 输出不一致 12（数字格式/异常/文化为主）、生成码 bug 2、用例自身 2。7 例从早期阶段**前进到更深处**（gccase/valuesem 进入运行、formatext 崩溃修复为全行输出、arrayext2/bitconvcase/collchain/guidcase/threadcase/versioncase 越过旧闸门），下一批修复面清晰（余 12 例 diff 类 + 5 例 fold/分类类）。
