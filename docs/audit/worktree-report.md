# Worktree 审计报告（提交代理专用）

- 审计时间：2026-08-03（本地快照，持续约 20 分钟）
- 基线 commit：`f1db70d`（M5 BCL runtime domains）
- **⚠ 重要**：审计期间工作树仍在变化（并行代理活跃）。未跟踪文件计数在审计期间从 277 → 295 → 322 → 330 持续增长。**提交前必须重跑 `git status --short` 获取最新清单**，本报告的文件清单为快照时刻捕获。

## 统计（最后一次快照）

| 类别 | 修改（已跟踪） | 新增（未跟踪） |
|---|---|---|
| A 转译器 src/il2cppc | 13 | 2 |
| B runtime 新域 | — | 44 头 + 42 源 |
| C runtime 修改 | 12 | — |
| D 测试 | 15 | 46 runtime + 2 unit + 79 cases + 8 fixtures + 12 bench + 31 bench-array + 48 research |
| E 文档 | 2 | 10 |
| G 脚本 | 1 | 2 |
| F 杂项（不提交） | — | 4 root txt + 产物 ~50 |

- 已跟踪修改文件：43；未跟踪展开文件：330；diff 行数：+4973 / -302（43 个已跟踪文件）

---

## A 类：转译器 src/il2cppc（13 修改 + 2 新增）

| 文件 | 改动性质（依据 diff hunk 与新增符号） |
|---|---|
| Emit/CodeGenerator.cs | +691；新增 `BclAllocExpr`（BCL 类型分配）、`ExceptionChain`/`BclExceptionBases`（异常链发射）、`TryFoldInt32`（常量折叠）、`EmitIcallCallArgs`、`GenericTypeSpecDefinitionName`；全文件 50+ 处发射逻辑扩展 |
| Emit/MethodRefResolver.cs | +1213；BclResolveCore 重构为按 owner 全名分派：新增 ~30 个 Dispatch（Console/Math/Convert/Int32…UInt64/String/Object/IConvertible/MemberInfo/StringBuilder/Hashtable/Random/Environment/File/Directory/Path/GC/Delegate/Array/BitConverter） |
| Emit/MethodSignatureCoder.cs | +34；新增签名编码（新 icall 参数类型/返回类型） |
| Emit/TypeEmitter.cs | +16；类型发射小改（2 处） |
| Emit/VTablePlanner.cs | +17；vtable 规划调整（1 处 +16） |
| Emit/AccessorAnalyzer.cs | **新增**；属性/字段访问器分析模块 |
| Il/IlDecoder.cs | +1；一行改动 |
| Il/MethodBodyReader.cs | +64；方法体读取扩展（新指令/新 MethodBodyInfo 字段） |
| Ir/Expr.cs | +68；新增 IR 表达式节点（LocExpr/StaticFieldExpr 扩展、CallExpr 新字段） |
| Ir/Normalize.cs | **新增**；IR 规范化 pass 模块 |
| Ir/StackFold.cs | +913；栈折叠大幅扩展（~70 处 hunk，新 opcode 折叠规则 +155 大块） |
| Ir/Stmt.cs | +12；新语句形式（RetStmt 扩展等） |
| MetadataLoader.cs | +10；元数据加载调整 |
| Model/TypeDef.cs | +8；TypeDef 新字段 |
| Program.cs | +1；入口一行 |

---

## B 类：runtime 新域（未跟踪新文件，按域分组）

### 快照时刻捕获清单（44 头 / 42 源；捕获后并行代理又新增了少量文件，以提交时 git status 为准）

**数值/转换（6 头 6 源）**：`conv.cpp`+`il2cpp_conv.h`、`convert.cpp`+`il2cpp_convert.h`、`convertbase64.cpp`+`il2cpp_convertbase64.h`、`bitconverter.cpp`+`bitconverter.h`、`intptr.cpp`+`intptr.h`、`mathext.cpp`+`il2cpp_math_ext.h`、`numberformat.h`（仅头，无对应 cpp）

**字符串/格式化/解析（7 头 7 源）**：`stringext2.cpp`+`il2cpp_string_ext2.h`、`stringintern.cpp`+`il2cpp_string_intern.h`、`stringbuilder.cpp`+`il2cpp_stringbuilder.h`、`concat.cpp`+`il2cpp_concat.h`、`formatext.cpp`+`il2cpp_format_ext.h`、`encoding.cpp`+`il2cpp_encoding.h`、`parse.cpp`+`il2cpp_parse.h`、`versionuri.cpp`+`versionuri.h`

**集合/数组/内存（6 头 6 源）**：`hashtable.cpp`+`il2cpp_hashtable.h`、`stackqueue.cpp`+`stackqueue.h`、`arraysort.cpp`+`arraysort.h`、`arrayext2.cpp`+`il2cpp_array_ext2.h`、`boxingext2.cpp`、`buffer.cpp`+`buffer.h`

**GC/内存（2 头 2 源）**：`gcapi.cpp`+`il2cpp_gc_api.h`、`gcext.cpp`+`il2cpp_gc_ext.h`

**反射/类型/委托/枚举（7 头 7 源）**：`attribute.cpp`+`attribute.h`、`typeext.cpp`+`il2cpp_typeext.h`、`typecheck.cpp`+`il2cpp_typecheck.h`、`valuesemantics.cpp`+`il2cpp_value_semantics.h`、`enumext.cpp`+`enumext.h`、`guid.cpp`+`guid.h`、`delegateext.cpp`+`il2cpp_delegate_ext.h`

**线程/同步（3 头 3 源）**：`thread.cpp`+`il2cpp_thread.h`、`interlocked.cpp`+`il2cpp_interlocked.h`、`monitor.cpp`+`monitor.h`

**IO/环境/时间（7 头 7 源）**：`streamio.cpp`+`streamio.h`、`binaryio.h`（仅头，无对应 cpp）、`environment.cpp`+`il2cpp_environment.h`、`cultureinfo.cpp`+`cultureinfo.h`、`timeext.cpp`+`il2cpp_time_ext.h`、`timeext2.cpp`+`timeext2.h`、`stopwatch.cpp`+`stopwatch.h`

**互操作/调试（2 头 2 源）**：`marshalext.cpp`+`il2cpp_marshal_ext.h`、`debugext.cpp`+`debugext.h`

---

## C 类：runtime 修改（12 个已跟踪文件）

| 文件 | 改动性质 |
|---|---|
| src/runtime/src/gc.cpp | **+357（最大）**；`UpdateReferencesAfterCompaction`（+191）、`Collect` 扩展（3 处 +40）、`AllocCore` 调整、新增 `il2cpp_gc_remove_root_range`（+114） |
| src/runtime/src/metadata.cpp | +48；`il2cpp_interface_dispatch` 重写（泛型/vtable 接口分派）+ `il2cpp_class_by_name` 扩展 |
| src/runtime/src/array.cpp | +25；`il2cpp_array_new` 重构 + `get_elem_ptr` 调整 |
| src/runtime/src/object.cpp | -7；`il2cpp_object_new`/`il2cpp_null_check` 精简 |
| src/runtime/src/exception.cpp | +29；新增 `il2cpp_checked_div`/`il2cpp_checked_rem`、unhandled 报告调整 |
| src/runtime/src/string.cpp | -9；移除 `il2cpp_string_class` 初始化段 |
| src/runtime/src/delegate.cpp | +2；`g_delegateClass` 字段微调 |
| include/internal/il2cpp_gc.h | +72；GC API 声明扩展（卡片标记、root 范围） |
| include/internal/il2cpp_array.h | +33；数组 API 声明扩展 |
| include/internal/il2cpp_object.h | +28；`Il2CppObject` 结构扩展（+28） |
| include/internal/il2cpp_string.h | +8；string API 声明 |
| include/internal/il2cpp_runtime.h | +1；一行 |

---

## D 类：测试

### 修改（15 个已跟踪文件）
- `tests/unit/CodeGeneratorTests.cs` +307、`tests/unit/StackFoldTests.cs` +442、`tests/unit/TypeEmitterTests.cs` +31（对应转译器新功能）
- `tests/runtime/test_exception.cpp` +30（checked_div/rem）、`tests/runtime/test_gcstress.cpp` +47（root range/压缩测试）
- `tests/capi/main.cpp` +52
- 用例：`cases/convcase/convcase.cs` +104（转换大扩展）、`cases/exceptions/exceptions.cs` +38、`cases/overflow/overflow.cs` +14、`cases/boxing/boxing.cs` +14、`cases/interface/interface.cs` +14、`cases/arrays/arrays.cs` +11、`cases/pinvoke/pinvoke.cs` +8、`cases/pinvoke/mylib/mylib.cpp` +9、`fixtures/capicalc/capicalc.cs` +24

### 新增（未跟踪）
- **tests/runtime/**（46 个 test_*.cpp）：test_arrayext2/attribute/bench_inline/binaryio/bitconverter/boxingext2/buffer/concat/conv/convert/convertbase64/cultureinfo/debugext/delegate_cb/delegateext/encoding/enumext/environment/formatext/frame_deep/gcapi/gcext/hashtable/interlocked/intptr/marshalext/math_extra/mathext/parse/stackqueue/stopwatch/streamio/stringbuilder/stringext2/stringintern/thread/timeext/typecheck/typeext/valuesemantics/versionuri
- **tests/unit/**：`AccessorAnalyzerTests.cs`、`NormalizeTests.cs`
- **tests/cases/**：31 个新用例目录（每目录 .cs + .csproj）：arrayext2/boxext2/boxing2/concatcase/delegate2/delegatecase/delegatecb(+mylib.cpp)/environment2/exdeep/formatext/gccase/gcext/hashtable2/hashtablecase/intern/math2/mathext/paramroot/parsecase/props/recurse/stringext2/stringops/threadcase/timeext/typecheckcase/uintcmp/valuesem
- **tests/fixtures/**：boxlong/props/refarr/uintcmp（.cs + .csproj）
- **tests/bench/**（源码 6）：bench.cs、bench.csproj、CMakeLists.txt、README.md、benchlib/benchlib.cpp、benchlib/CMakeLists.txt
- **tests/bench-array/**（源码 8）：bench_array.cs(.csproj)、bench_array_core.cs(.csproj)、CMakeLists.txt、README-array.md、benchlib/*
- **tests/research/**（源码 16）：8 个探针（arrprobe/bitprobe/boxprobe/cmpprobe/convprobe/exprobe/fieldprobe/swprobe 的 .cs + .csproj）——探索性脚本，提交代理可酌情排除

---

## E 类：文档

- 修改（2）：`docs/REQUIREMENTS.md` +22、`docs/research/il-semantics-reference.md` +407
- 新增（10）：`docs/architecture/int64-支持设计.md`、`docs/architecture/代数优化设计.md`、`docs/architecture/帧根优化设计.md`、`docs/audit/generated-code-audit.md`、`docs/audit/transpiler-audit-fixes.md`、`docs/research/coreclr-gc-源码摘录.md`、`docs/research/coreclr-gc-移植.md`、`docs/research/coreclr-jit-优化.md`（+2 个审计后新增）

## G 类：脚本

- 修改：`scripts/build-fixtures.ps1` +16（fixtures 拷贝扩展）
- 新增：`scripts/run-bench.ps1`、`scripts/run-bench-array.ps1`

---

## F 类：**不应提交**（杂项/构建产物）

**根目录杂项**：`console_capture.txt`（10B）、`console_input.txt`（13B）、`debugext_capture.txt`、`debugext_stderr.txt`（测试运行捕获物）

**构建产物（git 可见，违反仓库"不提交产物"约定）**：
- `tests/bench/out/`（bench.exe/.config/.pdb、benchlib.dll ×4）与 `tests/bench/results/*.csv`（×2）
- `tests/bench-array/out/`（×7）与 `tests/bench-array/results/*`（×4）
- `tests/research/out/`（exe/pdb/out.txt/config ×32）
- 生成 C++（仓库约定 gen/ 不提交，但 `gen_*`/`gen-*` 逃过了 .gitignore 的 `gen/` 规则）：`tests/bench-array/gen_array/`（×6）、`gen_array_core/`（×6）、`tests/cases/formatcase/gen-check/`（×6）
- 测试运行输出（仓库无任何已跟踪 expected.txt/oracle.txt 先例）：`tests/cases/{boxing2,delegate2,exdeep,formatext,hashtable2,recurse}/expected.txt`、`tests/cases/formatext/oracle.txt`

**建议**：向 .gitignore 增补 `gen_*/`、`gen-*/`、`tests/bench*/out/`、`tests/bench*/results/`、`tests/research/out/`、`tests/cases/*/expected.txt`（可单独作为 commit5 的一部分或独立提交）。

已验证：`.opencode/`、`.superpowers/`、`obj/`、`bin/` 均被 .gitignore 覆盖且**未被 git 跟踪**（`git ls-files` 无匹配）；无 build 目录误入。

---

## 静态损坏检查（全部通过，无异常）

- 121+ 个新增 .cpp/.h：**无空文件**、无缺失首行（全部以 `#include`/`#pragma`/注释开头）
- 48 个新增 .cs：**全部正常**（以 using/namespace/类声明等开头，无空文件、无残缺语法迹象）
- 42 个新增 runtime .cpp 的 `#include` 全部解析到已存在头文件（已跟踪 + 新增）；12 个非 `il2cpp_<stem>.h` 命名文件（arrayext2/boxingext2/delegateext/formatext/gcapi/gcext/marshalext/mathext/stringext2/stringintern/timeext/valuesemantics）逐一验证 include 目标存在
- 异常文件清单：**无**（除 F 类产物文件本身属于不应提交项）

---

## 建议 Commit 分组（5 个 commit，含确切文件清单）

### commit1 — 转译器（15 文件）
- 修改 13：`src/il2cppc/Emit/CodeGenerator.cs`、`Emit/MethodRefResolver.cs`、`Emit/MethodSignatureCoder.cs`、`Emit/TypeEmitter.cs`、`Emit/VTablePlanner.cs`、`Il/IlDecoder.cs`、`Il/MethodBodyReader.cs`、`Ir/Expr.cs`、`Ir/StackFold.cs`、`Ir/Stmt.cs`、`MetadataLoader.cs`、`Model/TypeDef.cs`、`Program.cs`
- 新增 2：`src/il2cppc/Emit/AccessorAnalyzer.cs`、`src/il2cppc/Ir/Normalize.cs`
- 备注：转译器单测（CodeGeneratorTests/StackFoldTests/TypeEmitterTests/AccessorAnalyzerTests/NormalizeTests）按任务划分归 commit4；如需原子性可并入本 commit

### commit2 — runtime 新域（快照 ~86 文件）
- `src/runtime/include/internal/` 下 44 个新 .h + `src/runtime/src/` 下 42 个新 .cpp（精确清单见本报告 B 类分组；含审计后新增文件，提交时以 git status 为准）
- 备注：`binaryio.h`、`numberformat.h` 快照时刻无对应 .cpp（并行代理可能补上）

### commit3 — runtime 修改 + GC（12 文件）
- `src/runtime/src/gc.cpp`、`metadata.cpp`、`array.cpp`、`object.cpp`、`exception.cpp`、`string.cpp`、`delegate.cpp`
- `src/runtime/include/internal/il2cpp_gc.h`、`il2cpp_array.h`、`il2cpp_object.h`、`il2cpp_string.h`、`il2cpp_runtime.h`

### commit4 — 测试 + 脚本（约 140 文件）
- 修改 15：`tests/unit/CodeGeneratorTests.cs`、`StackFoldTests.cs`、`TypeEmitterTests.cs`、`tests/runtime/test_exception.cpp`、`test_gcstress.cpp`、`tests/capi/main.cpp`、`tests/cases/{arrays,boxing,convcase,exceptions,interface,overflow}/*.cs`、`tests/cases/pinvoke/{pinvoke.cs,mylib/mylib.cpp}`、`tests/fixtures/capicalc/capicalc.cs`
- 新增：`tests/runtime/` 46 个 test_*.cpp、`tests/unit/` 2 个、`tests/cases/` 30 个新用例目录源码（**不含** formatcase/gen-check/ 与所有 expected.txt/oracle.txt）、`tests/fixtures/` 4 个新目录、`tests/bench/` 与 `tests/bench-array/` 源码（**不含** out/results/gen_*）、`tests/research/` 源码 16（可酌情排除）
- 脚本：`scripts/build-fixtures.ps1`（修改）、`scripts/run-bench.ps1`、`scripts/run-bench-array.ps1`（新增）

### commit5 — 文档（12 文件）
- 修改 2：`docs/REQUIREMENTS.md`、`docs/research/il-semantics-reference.md`
- 新增 10：`docs/architecture/` ×3、`docs/audit/` ×2（含本报告）、`docs/research/` ×3（+审计后新增 ×2）

### 明确不提交
- F 类全部（见上）；如需防止误加，可先提交 .gitignore 增补或改用显式 `git add <paths>` 白名单
