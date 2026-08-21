# e2e Full Matrix Audit — 全量 88 用例

日期：2026-08-04
基线：`dotnet build src/il2cppc -c Release`（0 错 0 警）+ `dotnet test tests/unit`（589/600）+ 全量 e2e（MinGW）。
方法：逐用例脚本（`%TEMP%\opencode\run-e2e-matrix.ps1`，不入库），每用例独立执行 转译（il2cppc.dll）→ cmake MinGW 构建 → 运行 → 与 net48 参考输出逐字节 diff（LF-join 比较，与 scripts/run-tests.ps1 相同语义）。任何一环节失败不中断其余用例。

## 汇总

| 指标 | 数值 |
|---|---|
| 总用例 | 88 |
| **PASS（逐字节一致）** | **38 / 88（43.2%）** |
| FAIL | 50 |
| 转译通过 | 61（26 例转译即失败 + 1 例 net48 参考构建失败） |
| runtime cmake 构建通过 | 54（61 − 7 例生成代码编译失败） |
| 运行产出可比较 | 54 |
| unit 测试 | 589/600（11 失败 = Base64×5 / Encoding×5 / Generics×1，并行代理在途域） |

## 全量统计表（用例 | 转译 | 构建 | 运行 | diff）

| 用例 | 转译 | 构建 | 运行 | diff | 缺口类型 |
|---|---|---|---|---|---|
| arith | OK | OK | OK | **PASS** | |
| arraybounds | OK | OK | OK | **PASS** | |
| arrayext | OK | OK | OK | **PASS** | |
| arrayext2 | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| arrays | OK | OK | OK | **PASS** | |
| base64case | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| bitconvcase | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| boxdeep | OK | OK | OK | **FAIL** | runtime（box 语义 26/32） |
| boxext | OK | OK | OK | **FAIL** | runtime（box/unbox：not a boxed Char） |
| boxext2 | OK | OK | OK | **PASS** | |
| boxing | OK | OK | OK | **PASS** | |
| boxing2 | OK | OK | OK | **FAIL** | runtime（box/unbox：not a boxed Int16） |
| boxstr | OK | OK | OK | **PASS** | |
| branch | OK | OK | OK | **PASS** | |
| cctor | OK | OK | OK | **PASS** | |
| cctorchain | OK | OK | OK | **FAIL** | runtime（cctor 异常 catch 计数 1 vs 2） |
| collchain | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| concatcase | OK | OK | OK | **PASS** | |
| consolecase | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| convcase | OK | OK | OK | **FAIL** | runtime（double→string 精度 17 位 vs 15 位） |
| convertcase | OK | OK | OK | **FAIL** | runtime（rounding 3 vs 2） |
| crossstat | OK | OK | OK | **PASS** | |
| datetimechain | OK | OK | OK | **PASS** | |
| delechain | OK | **FAIL** | - | - | 转译器（生成码 int/Il2CppObject* 类型错） |
| delegate2 | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| delegatecase | OK | OK | OK | **FAIL** | runtime（0xC0000005 崩溃，0/57 行） |
| delegatecb | **FAIL** | - | - | - | 转译器（Marshal.GetFunctionPointerForDelegate 无 BCL 映射） |
| delegates | OK | OK | OK | **PASS** | |
| encodingcase | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| environment | OK | OK | OK | **PASS** | |
| environment2 | OK | OK | OK | **FAIL** | 用例自身（tick_delta 计时敏感，跨运行不稳定） |
| exceptions | OK | **FAIL** | - | - | 转译器（生成码 this_ 缺参 + expected catch） |
| excext | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| exdeep | OK | **FAIL** | - | - | 转译器（生成码 expected catch） |
| filechain | OK | OK | OK | **PASS** | |
| fmtchain | OK | OK | OK | **PASS** | |
| formatcase | OK | OK | OK | **FAIL** | runtime（Infinity 打印 NaN/±∞ 差异） |
| formatext | OK | OK | OK | **FAIL** | runtime（0xC0000005 崩溃，0/73 行） |
| gccase | OK | **FAIL** | - | - | 转译器（生成码 Il2CppObject_class 未声明） |
| gcext | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| gcstress | OK | OK | OK | **PASS** | |
| generics | OK | OK | OK | **PASS** | |
| genlist | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| guidcase | **FAIL** | - | - | - | 转译器（IL opcode 0xFE16 M1 不支持） |
| hashtable2 | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| hashtablecase | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| interface | OK | OK | OK | **PASS** | |
| interlockedcase | **FAIL** | - | - | - | 转译器（static/instance 分类错） |
| intern | OK | OK | OK | **PASS** | |
| internchain | OK | OK | OK | **PASS** | |
| iocase | **FAIL** | - | - | - | 转译器（Path.Combine 3 参未接） |
| loop | OK | OK | OK | **PASS** | |
| math2 | OK | OK | OK | **FAIL** | runtime（-min 溢出抛异常 vs net48 返回） |
| mathcase | OK | OK | OK | **FAIL** | runtime（double→string 精度 17 位 vs 15 位） |
| mathext | OK | OK | OK | **FAIL** | runtime（double→string 精度 17 位 vs 15 位） |
| mathextra | OK | OK | OK | **PASS** | |
| methods | OK | OK | OK | **PASS** | |
| objects | OK | OK | OK | **PASS** | |
| overflow | **FAIL** | - | - | - | 用例自身（net48 编译期 checked 溢出 CS0220） |
| overloads | OK | OK | OK | **PASS** | |
| paramroot | OK | OK | OK | **PASS** | |
| parsecase | **FAIL** | - | - | - | 转译器（Int32.TryParse ref/out 未接） |
| pinvoke | **FAIL** | - | - | - | 转译器（Marshal.GetLastWin32Error static 分类错） |
| pinvokeext | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| propcase | OK | OK | OK | **PASS** | |
| props | OK | OK | OK | **PASS** | |
| randomcase | **FAIL** | - | - | - | 转译器（String.Join 1 参未接） |
| recurse | OK | **FAIL** | - | - | 转译器（生成码 entry.cpp Main 签名不匹配） |
| recursive_arith | OK | OK | OK | **PASS** | |
| regexcase | OK | OK | OK | **PASS** | |
| sbchain | OK | OK | OK | **PASS** | |
| statics | OK | OK | OK | **PASS** | |
| strdeep | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| string | OK | OK | OK | **PASS** | |
| stringbuilder | OK | OK | OK | **FAIL** | runtime（capacity 异常） |
| stringext | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| stringext2 | OK | OK | OK | **FAIL** | runtime（多字节 UTF-16→UTF-8 乱码） |
| stringops | OK | OK | OK | **PASS** | |
| swcase | **FAIL** | - | - | - | 转译器（Stopwatch.get_IsRunning 未接） |
| threadcase | **FAIL** | - | - | - | 转译器（IL opcode 0xFE16 M1 不支持） |
| timecase | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| timeext | OK | OK | OK | **FAIL** | runtime（culture zh-CN vs en-US + format 异常） |
| typecheckcase | **FAIL** | - | - | - | 转译器（M3 Ldtoken 不支持） |
| uintcmp | OK | OK | OK | **PASS** | |
| valuesem | OK | **FAIL** | - | - | 转译器（生成码 Il2CppObject_class 未声明） |
| valuetypes | OK | OK | OK | **PASS** | |
| versioncase | OK | **FAIL** | - | - | 转译器（System_Version_class + il2cpp_version_parse 未接线） |
| virtual | OK | OK | OK | **PASS** | |

## 失败归因分类

### 1. 转译器接线缺（M-stage 未实现）— 26 例（52.0%）
`il2cppc error:` LOUD 中止，转译即失败（il2cppc exit=2）。样本根因（逐一采样验证）：
- **M3 Ldtoken 不支持**（StackFold.cs:715）：arrayext2、base64case、bitconvcase、collchain、consolecase、delegate2、encodingcase、excext、gcext、genlist、hashtable2、hashtablecase、pinvokeext、strdeep、stringext、timecase、typecheckcase
- **M1 IL opcode 0xFE16 不支持**：guidcase、threadcase（ref 返回值 IL 特征）
- **BCL 映射未接（MethodRefResolver.cs:366 LOUD）**：randomcase（String.Join 1 参）、iocase（Path.Combine 3 参）、parsecase（Int32.TryParse ref/out）、swcase（Stopwatch.get_IsRunning）、delegatecb（Marshal.GetFunctionPointerForDelegate）
- **static/instance 分类错**：interlockedcase、pinvoke（Marshal 静态方法被当 INSTANCE）
- 共同点：用例使用超出 M-stage 当前覆盖面的 BCL 表面（Ldtoken/ref 返回值/未映射静态重载）。**非 runtime 缺口，纯转译器待实现面。**

### 2. 转译器生成码 bug（能转译但生成 C++ 编译失败）— 7 例（14.0%）
- **Il2CppObject_class 未声明**：gccase、valuesem（CodeGenerator 引用 il2cpp_object_new(&Il2CppObject_class) 但 runtime 无该全局符号）
- **versioncase**：System_Version_class + `il2cpp::runtime::il2cpp_version_parse` 不存在（versionuri.h 不在 generated.h include 列表——已知遗留）
- **exdeep / exceptions**：生成 catch 语句缺 `expected catch`（EH 发射错误）
- **delechain**：生成码 int32_t ↔ Il2CppObject* 类型错配
- **recurse**：entry.cpp 调用 `Cases_Recurse_Main_...` 签名与生成定义不匹配

### 3. runtime 缺失/错误（转译+构建+运行成功，输出与 net48 不一致）— 16 例（32.0%）
- **崩溃 0xC0000005**：delegatecase、formatext（访问违例，0 行输出）
- **box/unbox 语义**：boxext、boxing2（Specified cast: not a boxed Char/Int16）、boxdeep（26/32 行）
- **double→string 精度**：convcase、mathcase、mathext（17 位有效数字 vs net48 15 位）
- **数字格式**：convertcase（rounding 3 vs 2）、formatcase（Infinity/±∞ 特殊值打印）、math2（-int.MinValue 溢出抛异常 vs net48 不抛）
- **字符串**：stringext2（UTF-16→UTF-8 多字节乱码）、stringbuilder（capacity 抛异常）
- **cctor/异常**：cctorchain（catch 计数 1 vs 2）
- **culture**：timeext（zh-CN vs en-US + "Format specifier was invalid"）

### 4. 用例自身 — 2 例（4.0%）
- **overflow**：net48 编译期 checked 溢出（overflow.cs:14 `int.MinValue / -1` 常量→ CS0220），参考运行无法生成 → 整链不可比较
- **environment2**：tick_delta 计时输出跨运行不稳定（实测 16/31/32 抖动），diff 结果不可靠

## 结论

1. **转译器构建全绿**：`dotnet build src/il2cppc -c Release` → 0 错误 0 警告。
2. **unit**：589/600 通过（11 失败均为并行代理在途域：Base64×5、Encoding×5、Generics×1）。
3. **e2e PASS 率 38/88（43.2%）**；失败 50 例归因：转译器接线缺 26、runtime 缺口 16、转译器生成码 bug 7、用例自身 2。metadata.cpp 修复使 54/61 转译成功用例可完整构建运行（88.5%），此前受 test_typeinfo 编译错阻塞的全量 runtime 构建已不再阻塞 e2e 链。
