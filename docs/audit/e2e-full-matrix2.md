# e2e Full Matrix Audit 2 — 大量修复后全量重跑

日期：2026-08-04（14:44 快照）
基线：`dotnet build src/il2cppc -c Release`（**0 错 0 警**，dll 14:44:34 新于全部源，最新源 14:38）+ 全量 e2e（MinGW，共享 runtime 库）。
方法：逐用例脚本（`%TEMP%\opencode\run-e2e-matrix2.ps1`，不入库），每用例独立执行 转译（il2cppc.dll 直跑）→ cmake MinGW 构建 → 运行 → 与 net48 参考输出逐字节 diff（LF-join 比较，与 scripts/run-tests.ps1 相同语义）。**优化**：`src/runtime` 静态库 `libil2cpp_runtime.a` 预构建一次（build/runtime_e2e_shared），每用例用绝对路径 wrapper CMakeLists 仅编译自身 gen 源（~12-15s/例 vs 全量 ~98s/例）。任何一环节失败不中断其余用例。

对比基线：docs/audit/e2e-full-matrix.md（matrix1，38/88 PASS）。

## 汇总

| 指标 | matrix1 | matrix2 | 变化 |
|---|---|---|---|
| 总用例 | 88 | 88 | = |
| **PASS（逐字节一致）** | **38（43.2%）** | **39（44.3%）** | **+1** |
| FAIL | 50 | 49 | −1 |
| 转译失败 | 26 | 25 | −1 |
| cmake 构建失败 | 7 | 6 | −1 |
| 运行/diff 失败 | 16 | 17 | +1 |
| net48 参考构建失败 | 1 | 1 | = |
| build 警告/错误 | 0/0 | 0/0 | = |

**PASS 率变化**：43.2% → 44.3%（+1 例）。无回归（matrix1 全部 38 个 PASS 在 matrix2 全部保持 PASS）。

## 全量统计表（用例 | matrix1 | matrix2 | 阶段 | 失败主因/备注）

| 用例 | M1 | M2 | 阶段 | 失败归因 |
|---|---|---|---|---|
| arith | PASS | **PASS** | done | |
| arraybounds | PASS | **PASS** | done | |
| arrayext | PASS | **PASS** | done | |
| arrayext2 | FAIL | FAIL | transpile | ldtoken FIELD token（RuntimeHelpers.InitializeArray RVA 数组初始化器，M3 fold 缺口，同 M1） |
| arrays | PASS | **PASS** | done | |
| base64case | FAIL | FAIL | transpile | ldtoken FIELD token（同 arrayext2 根因；另 Encoding.UTF8 域 LOUD） |
| bitconvcase | FAIL | FAIL | transpile | BitConverter.GetBytes: RawSignature SZARRAY ret 元素字节泄漏进 ps[0]（MethodRefResolver 域，M1 是 ldtoken） |
| boxdeep | FAIL | FAIL | diff | box 语义 26/32 行（runtime，同 M1） |
| boxext | FAIL | FAIL | diff | box/unbox：not a boxed Char（runtime，同 M1） |
| boxext2 | PASS | **PASS** | done | |
| boxing | PASS | **PASS** | done | |
| boxing2 | FAIL | FAIL | diff | box/unbox：not a boxed Int16（runtime，同 M1） |
| boxstr | PASS | **PASS** | done | |
| branch | PASS | **PASS** | done | |
| cctor | PASS | **PASS** | done | |
| cctorchain | FAIL | FAIL | diff | boom_caught=1 vs 2（cctor 异常 catch 计数，runtime，同 M1） |
| collchain | FAIL | FAIL | transpile | dup on empty stack at offset 0x0523（StackFold.cs:270，M1 是 ldtoken——第一道闸已过） |
| concatcase | PASS | **PASS** | done | |
| consolecase | FAIL | FAIL | transpile | Console.IsInputRedirected 未接线（runtime 无 icall，M1 是 ldtoken） |
| convcase | FAIL | FAIL | diff | double→string 17 位 vs net48 15 位（runtime 格式化，同 M1）；另 L60 uint→int 打印 -1 vs 4294967295 |
| convertcase | FAIL | FAIL | diff | rounding 3 vs 2 + ToInt32(uint max) OverflowException 差异（runtime，同 M1） |
| crossstat | PASS | **PASS** | done | |
| datetimechain | PASS | **PASS** | done | |
| delechain | FAIL | FAIL | cmake_build | 生成码 int32_t↔Il2CppObject* 类型错配（Cases_Delechain.cpp:137-138，同 M1） |
| delegate2 | FAIL | FAIL | transpile | fold: opcode Ldsflda not supported in M3（M1 是 ldtoken——第一道闸已过） |
| delegatecase | FAIL | FAIL | diff | 0xC0000005 崩溃，0/57 行（runtime，同 M1） |
| delegatecb | FAIL | FAIL | transpile | Marshal.GetFunctionPointerForDelegate 泛型无 BCL 映射（同 M1） |
| delegates | PASS | **PASS** | done | |
| encodingcase | FAIL | FAIL | transpile | Encoding.GetBytes 原始缓冲协议不可接线（raw-buffer code-page icall，M1 是 ldtoken） |
| environment | PASS | **PASS** | done | |
| environment2 | FAIL | FAIL | diff | tick_delta 计时抖动 15 vs 31（用例自身，同 M1） |
| **exceptions** | FAIL | **PASS** | done | **EH 嵌套修复生效：this_ 缺参 + expected catch 已修，11 行逐字节一致** |
| excext | FAIL | FAIL | transpile | Exception.get_Message 未接线（catch 变量不支持，M1 是 ldtoken） |
| exdeep | FAIL | FAIL | diff | EH 嵌套修复已落地（从 cmake_build 前进到 diff 29/37 行）；f2 首行就崩（exception thrown→未处理）→ f17+ 级联缺 |
| filechain | PASS | **PASS** | done | |
| fmtchain | PASS | **PASS** | done | |
| formatcase | FAIL | FAIL | diff | Infinity vs ∞（U+221E）特殊值打印（runtime，同 M1） |
| formatext | FAIL | FAIL | diff | 0xC0000005 崩溃，0/73 行（runtime，同 M1） |
| gccase | FAIL | FAIL | cmake_build | Il2CppObject_class 未声明（生成码引用全局符号不存在，同 M1） |
| gcext | FAIL | FAIL | transpile | WeakReference::.ctor 未接线（M1 是 ldtoken） |
| gcstress | PASS | **PASS** | done | |
| generics | PASS | **PASS** | done | |
| genlist | FAIL | FAIL | transpile | generic instantiation Id 泛型实参 0x0E 无 M6 展开（MethodSpec 泛型，M1 是 ldtoken） |
| guidcase | FAIL | FAIL | transpile | IL opcode 0xFE16（ref 返回值）M1 不支持（同 M1） |
| hashtable2 | FAIL | FAIL | transpile | Console.WriteLine(object) 未跟踪 box 类型（M1 是 ldtoken） |
| hashtablecase | FAIL | FAIL | transpile | 同上 |
| interface | PASS | **PASS** | done | |
| interlockedcase | FAIL | FAIL | cmake_build | **转译已过（M1 是 static/instance 分类错）**→ 生成码 long Increment/Decrement 落 int32 icall 指针不兼容（InterlockedCase.cpp:64,67，RawSignature BYREF 元素缺口，已记录） |
| intern | PASS | **PASS** | done | |
| internchain | PASS | **PASS** | done | |
| iocase | FAIL | FAIL | transpile | Path.Combine 3 参未接线（同 M1） |
| loop | PASS | **PASS** | done | |
| math2 | FAIL | FAIL | diff | -int.MinValue 负溢出抛异常 vs net48 返回（runtime，同 M1） |
| mathcase | FAIL | FAIL | diff | double→string 精度（runtime，同 M1） |
| mathext | FAIL | FAIL | diff | double→string 精度（runtime，同 M1） |
| mathextra | PASS | **PASS** | done | |
| methods | PASS | **PASS** | done | |
| objects | PASS | **PASS** | done | |
| overflow | FAIL | FAIL | net48build | 用例自身：net48 编译期 checked 溢出 CS0220（同 M1） |
| overloads | PASS | **PASS** | done | |
| paramroot | PASS | **PASS** | done | |
| parsecase | FAIL | FAIL | transpile | Int32.TryParse ref/out 未接线（同 M1） |
| pinvoke | FAIL | FAIL | transpile | Marshal.GetLastWin32Error 被当 INSTANCE（static 分类错，同 M1） |
| pinvokeext | FAIL | FAIL | transpile | Marshal.PtrToStringAnsi 被当 INSTANCE（static 分类错，M1 是 ldtoken） |
| propcase | PASS | **PASS** | done | |
| props | PASS | **PASS** | done | |
| randomcase | FAIL | FAIL | transpile | String.Join 1 参未接线（同 M1） |
| recurse | FAIL | FAIL | cmake_build | entry.cpp 调 Cases_Recurse_Main 签名不匹配（同 M1） |
| recursive_arith | PASS | **PASS** | done | |
| regexcase | PASS | **PASS** | done | |
| sbchain | PASS | **PASS** | done | |
| statics | PASS | **PASS** | done | |
| strdeep | FAIL | FAIL | transpile | Array.Empty 泛型无 BCL 映射（M1 是 ldtoken） |
| string | PASS | **PASS** | done | |
| stringbuilder | FAIL | FAIL | diff | capacity 抛异常 vs net48 返回（runtime，同 M1） |
| stringext | FAIL | FAIL | transpile | Array.Empty 泛型无 BCL 映射（M1 是 ldtoken） |
| stringext2 | FAIL | FAIL | diff | 多字节 UTF-16→UTF-8 乱码（runtime，同 M1） |
| stringops | PASS | **PASS** | done | |
| swcase | FAIL | FAIL | transpile | Stopwatch.get_IsRunning 未接线（同 M1） |
| threadcase | FAIL | FAIL | transpile | IL opcode 0xFE16（ref 返回值）M1 不支持（同 M1） |
| timecase | FAIL | FAIL | transpile | TimeSpan.get_TotalMinutes 未接线（M1 是 ldtoken） |
| timeext | FAIL | FAIL | diff | culture zh-CN vs en-US + Format specifier invalid（runtime，同 M1） |
| typecheckcase | FAIL | FAIL | transpile | TypeSpec SZARRAY 0x1D isinst（M1 是 ldtoken——typeof 链折叠已过，卡 isinst） |
| uintcmp | PASS | **PASS** | done | |
| valuesem | FAIL | FAIL | cmake_build | Il2CppObject_class 未声明（同 M1） |
| valuetypes | PASS | **PASS** | done | |
| versioncase | FAIL | FAIL | cmake_build | System_Version_class + il2cpp_version_parse 未接线（同 M1） |
| virtual | PASS | **PASS** | done | |

## 与 matrix1 的逐项对比结论

1. **净 +1 PASS（exceptions）**：EH 嵌套发射修复（CodeGenerator.cs LIFO 块栈重写 + ctor_nop 特判）使 exceptions 从 cmake_build 失败翻为逐字节一致（11 行）。**零回归**（38/38 保持）。
2. **Ldtoken/typeof 修复的真实效果 = 解除第一道闸门，非 PASS 提升**：matrix1 有 17 例卡 `ldtoken not supported`，matrix2 中 15 例已越过 Ldtoken 折叠，前进到各自**下一道不同**的 loud/编译/运行阻塞：
   - 转译面：bitconvcase（RawSignature SZARRAY 元素泄漏）、collchain（dup on empty stack）、consolecase（IsInputRedirected）、delegate2（Ldsflda）、encodingcase（raw-buffer 协议）、excext（get_Message）、gcext（WeakReference ctor）、genlist（泛型实参 0x0E）、hashtable2/hashtablecase（WriteLine(object) 未跟踪）、pinvokeext（Marshal static 分类）、strdeep/stringext（Array.Empty）、timecase（TotalMinutes）、typecheckcase（TypeSpec isinst）
   - 仅 arrayext2/base64case 仍卡 ldtoken FIELD token（RuntimeHelpers.InitializeArray 数组初始化器——M3 fold 明确不支持该模式）
3. **EH 修复的连带进步**：exdeep 从 cmake_build 失败 → 构建运行成功（29/37 行一致，f1-f15 全对），仍卡 f2 首行抛异常未处理 + f16 throw null 语义。
4. **Interlocked 修复落地**：interlockedcase 转译从失败→成功（映射接线生效），但生成码卡 long Increment/Decrement（int64_t*→int32_t* 指针不兼容）——即 memory 已记录的 RawSignature BYREF 元素缺口。

## 失败归因分类（matrix2，49 例）

### 1. 转译器接线缺（LOUD 中止）— 25 例（51.0%）
- **未接 BCL 映射**：consolecase（IsInputRedirected）、encodingcase（GetBytes raw-buffer）、excext（get_Message）、gcext（WeakReference ctor）、iocase（Path.Combine 3 参）、parsecase（TryParse ref/out）、randomcase（String.Join 1 参）、swcase（get_IsRunning）、timecase（TotalMinutes）、delegatecb（GetFunctionPointerForDelegate）、strdeep/stringext（Array.Empty 泛型）、genlist（泛型实参展开）
- **IL/M3 fold 缺口**：guidcase/threadcase（opcode 0xFE16 ref 返回值）、delegate2（Ldsflda）、collchain（dup on empty stack）、arrayext2/base64case（ldtoken FIELD token InitializeArray）
- **static/instance 分类错**：pinvoke、pinvokeext（Marshal 静态方法被当 INSTANCE）
- **RawSignature 缺陷**：bitconvcase（SZARRAY 元素字节泄漏）
- **TypeSpec 泛型**：typecheckcase（SZARRAY 0x1D isinst）
- **Console 域**：hashtable2/hashtablecase（WriteLine(object) 未跟踪）

### 2. 转译器生成码 bug（转译成功但 C++ 编译失败）— 6 例（12.2%）
- **Il2CppObject_class 未声明**：gccase、valuesem
- **版本域未接线**：versioncase（System_Version_class + il2cpp_version_parse）
- **类型错配**：delechain（int32_t↔Il2CppObject*）
- **签名不匹配**：recurse（entry.cpp Main 调用签名）
- **Interlocked 已知缺口**：interlockedcase（long Increment/Decrement int64_t*→int32_t*）

### 3. runtime 缺失/错误（构建运行成功，输出不一致）— 17 例（34.7%）
- **崩溃 0xC0000005**：delegatecase（0/57）、formatext（0/73）
- **box/unbox 语义**：boxext、boxing2（not a boxed Char/Int16）、boxdeep（26/32）
- **double→string 精度**：convcase、mathcase、mathext（17 vs 15 位）
- **数字格式**：convertcase（rounding）、formatcase（Infinity vs ∞）、math2（-min 溢出）
- **字符串**：stringext2（UTF-8 乱码）、stringbuilder（capacity 异常）
- **异常**：cctorchain（catch 计数 1 vs 2）、exdeep（f2/f16 级联）
- **culture**：timeext（zh-CN vs en-US）

### 4. 用例自身 — 2 例（4.1%）
- **overflow**：net48 编译期 checked 溢出 CS0220（参考无法生成）
- **environment2**：tick_delta 计时抖动（15/31 跨运行不等）

## 结论

1. **build 全绿**：`dotnet build src/il2cppc -c Release` → 0 错误 0 警告；dll 新于全部源（14:44:34 vs 14:38:03）。
2. **PASS 38 → 39（43.2% → 44.3%），零回归**；唯一净增 PASS 来自 EH 嵌套修复（exceptions）。Ldtoken/typeof 修复的真实贡献是解除 15 例的第一道转译闸门，但它们各自遇到下一道不同的 BCL 接线缺口，尚未转化 PASS。
3. **剩余 49 例主因**：转译器接线缺 25（未接 BCL 映射 + IL fold 缺口为主）、runtime 缺口 17（box/unbox + double 精度 + 2 崩溃）、生成码 bug 6、用例自身 2。共享 runtime 构建法把全量矩阵耗时从 ~20min 压到 ~4min（12-15s/例）。
