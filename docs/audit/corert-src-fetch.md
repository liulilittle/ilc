# CoreRT 参考源码拉取报告 — reference/corert/

- 日期：2026-08-05
- 任务：拉取 CoreRT（.NET Native ahead-of-time compiler）参考源码到本地持久目录 `reference/corert/`（用户要求：参考项目放本地目录，不放 temp——重启丢失）
- 方式：`git clone --depth 1`（仓库仅 ~49.5 MB，未触发降级路径；archive 仓库只读可用）
- 约束：不 commit（`reference/` 已 .gitignore）；不改任何现有代码

## 1. 现状核查（拉取前）

| 项 | 值 |
|---|---|
| `reference/` 存在 | True（既有 `reference/netcore/` sparse clone） |
| 拉取前 `reference/` | 31.0 MB，1829 文件（netcore/src/libraries） |
| gitignore 排除 `reference/` | True（`git check-ignore reference/` → exit 0） |
| 源仓库 | `https://github.com/dotnet/corert.git`（master） |
| 仓库状态 | archived（archive 只读可用），size ~49.5 MB（GitHub API） |

## 2. 拉取操作

- 命令：`git clone --depth 1 https://github.com/dotnet/corert reference/corert`（在 `E:\il2cpp` 内执行）
- 结果：exit 0；检出 4412 文件。仓库体积评估 49.5 MB（< 数百 MB 阈值），故采用全量 depth-1 clone，未用 `--filter=blob:none --sparse` 降级路径。

## 3. 拉取后结果

| 项 | 值 |
|---|---|
| 仓库合计 | **35.3 MB，4412 文件** |
| commit | `c6af4cf`（2020-10-31 "Add link to NativeAOT experiment"）——CoreRT 归档前最后一次提交 |
| depth | 1（`git rev-list --count HEAD` = 1） |
| 拉取后工作树 | clean（无脏改动，纯拉取） |

### 关键子目录规模

| 子目录 | 文件数 | 说明 |
|---|---|---|
| `src/Common/src/TypeSystem/` | 296 | **类型系统核心**（13 子目录） |
| `src/Native/` | 482 | AOT runtime（C++：Runtime/gc/jitinterface） |
| `src/Native/Runtime/` | 274 | runtime 主实现 |
| `src/Native/gc/` | 72 | 自研 GC |
| `src/ILCompiler.Compiler/` | 279 | AOT 编译流水线（依赖分析/对象图） |
| `src/ILCompiler.MetadataTransform/` | 39 | 元数据裁剪/反射元数据转换 |
| `src/System.Private.Reflection.Execution/` | 43 | 反射执行（反射裁剪运行时侧） |
| `src/ILCompiler.CppCodeGen/` | 13 | C++ 代码生成后端（与 ILC 思路最接近） |
| `src/JitInterface/` | 18 | JIT 接口（CorInfoImpl） |

## 4. 关键文件清单

### 类型系统（`src/Common/src/TypeSystem/`）— 设计核心

| 文件 | 说明 |
|---|---|
| `Common/TypeDesc.cs` / `Common/MetadataType.cs` / `Common/MethodDesc.cs` / `Common/FieldDesc.cs` | 类型/方法/字段描述基类（MethodSignature 定义于 `Common/MethodDesc.cs:30`） |
| `Common/TypeSystemContext.cs` | 类型系统上下文（类型规范化/解析入口） |
| `Common/TypeSystemHelpers.cs` / `Common/TypeHashingAlgorithms.cs` | 类型辅助/哈希算法 |
| `Common/MetadataVirtualMethodAlgorithm.cs` / `VirtualMethodAlgorithm.cs` | 虚方法解析算法 |
| `Common/MetadataFieldLayoutAlgorithm.cs` / `FieldLayoutAlgorithm.cs` | 字段布局算法 |
| `Common/WellKnownType.cs` / `TypeFlags.cs` | 内置类型枚举 |
| `Ecma/EcmaType.cs` / `EcmaMethod.cs` / `EcmaField.cs` / `EcmaModule.cs` | ECMA 元数据读取集成（System.Reflection.Metadata） |
| `Ecma/EcmaSignatureParser.cs` | 元数据签名解析 |
| `IL/ILImporter.cs` / `IL/ILOpcode.cs` / `IL/ILOpcodeHelper.cs` | IL 导入/opcode 定义 |
| `Canon/`（21 文件） | 泛型 Canon 化 |
| `RuntimeDetermined/`（21 文件） | 泛型运行时确定类型 |
| `Interop/`（24 文件） | 互操作类型 |
| `NativeFormat/`（11 文件） | Native 元数据格式（NDirect/内建布局） |
| `Sorting/`（14 文件） | 类型/方法排序比较器 |

### AOT 编译（`src/ILCompiler.Compiler/`）

| 文件 | 说明 |
|---|---|
| `src/Compiler/Compilation.cs` / `CompilationBuilder.cs` | 编译主流程 |
| `src/Compiler/ILScanner.cs` | **IL 扫描分析器**——编译前保守分析需生成的方法/类型/运行时构件（ILC 裁剪扫描策略的直接参考） |
| `src/Compiler/TypePreinit.cs` | 类型预初始化 |
| `src/Compiler/CompilerTypeSystemContext.cs` | 编译期类型系统上下文 |
| `src/Compiler/DependencyAnalysis/NodeFactory.cs` / `EETypeNode.cs` / `ConstructedEETypeNode.cs` | 依赖分析对象图 |
| `src/Compiler/DependencyAnalysis/CodeBasedDependencyAlgorithm.cs` / `CustomAttributeBasedDependencyAlgorithm.cs` | 依赖枚举算法（代码驱动 vs 反射裁剪驱动） |
| `src/Compiler/DependencyAnalysis/BlockReflectionTypeMapNode.cs` | 反射类型映射（裁剪策略） |
| `src/Compiler/DependencyAnalysis/GenericDictionaryNode.cs` / `DynamicInvokeTemplateDataNode.cs` | 泛型字典/动态调用模板 |

### AOT runtime（`src/Native/`）

| 文件 | 说明 |
|---|---|
| `Runtime/portable.cpp` | 运行时主入口（可移植实现） |
| `Runtime/rhassert.h` | 运行时断言 |
| `Runtime/`（274 文件） | EEType 布局/GC 交互/类型加载 |
| `gc/`（72 文件） | 自研 GC |
| `Bootstrap/main.cpp` | 启动入口 |
| `System.Private.TypeLoader.Native/` | 类型加载器原生侧 |

### 反射裁剪（运行时侧）

| 文件 | 说明 |
|---|---|
| `src/System.Private.Reflection.Core/src/Internal/Reflection/Core/Execution/ExecutionEnvironment.cs` | 反射执行环境（裁剪后反射元数据访问） |
| `src/System.Private.Reflection.Core/src/System/Reflection/Runtime/TypeInfos/RuntimeTypeInfo.cs` | 运行时类型信息 |
| `src/System.Private.CoreLib/src/Internal/Runtime/Augments/RuntimeAugments.cs` | 运行时增强点 |

### 其他（与 ILC 思路对照）

| 文件 | 说明 |
|---|---|
| `src/ILCompiler.CppCodeGen/src/CppCodeGen/CppWriter.cs` | C++ 代码生成后端——IL→C++ 翻译思路与 ILC transpiler 最接近 |
| `src/JitInterface/src/CorInfoImpl.cs` | JIT 接口实现 |
| `src/ILCompiler.RyuJit/src/Compiler/DependencyAnalysis/MethodCodeNode.cs` | 方法代码节点（RyuJit 侧） |
| `src/ILCompiler.WebAssembly/src/Compiler/WebAssemblyCodegenCompilation.cs` | Wasm 后端 |

## 5. 失败清单

无。单次 clone 成功（4412 文件全部检出），0 失败。仓库 49.5 MB 未超阈值，未触发 sparse/逐文件降级路径。

## 6. 过程备注

1. CoreRT 为 archive 仓库，master 分支最后提交 `c6af4cf`（2020-10-31），只读可用。depth 1 足够——无需历史，工作树为参考唯一载体。
2. `reference/corert/` 与既有 `reference/netcore/` 并存于 `reference/` 下；两者均已 .gitignore（`git check-ignore reference/` exit 0），不进入主仓库提交。
3. 本任务仅拉取，未改动任何 ILC 源码/测试（工作树脏改动均为既有 in-flight 文件，与本任务无关）。

## 7. 后续建议

1. **参考路径**：
   - AOT 类型系统设计 → `reference/corert/src/Common/src/TypeSystem/`（TypeDesc/MethodDesc/Ecma/Canon/RuntimeDetermined）
   - 反射裁剪扫描策略 → `reference/corert/src/ILCompiler.Compiler/src/Compiler/ILScanner.cs` + `DependencyAnalysis/`（CodeBased/CustomAttributeBasedDependencyAlgorithm）
   - IL→C++ 翻译思路 → `reference/corert/src/ILCompiler.CppCodeGen/src/CppCodeGen/CppWriter.cs`
   - AOT runtime → `reference/corert/src/Native/Runtime/`（portable.cpp、EEType）
2. **版本锁定**：以 `c6af4cf` 为准。CoreRT 已归档不再演进，如需 NativeAOT（其后继）可另拉 `dotnet/runtime` 的 `src/coreclr/nativeaot/`（netcore 仓库内已有 coreclr sparse 集，可增量 add）。
3. **仓库体积**：corert 35.3 MB + netcore 77.8 MB 共约 113 MB 本地占用；`reference/` 已 gitignore，不提交主仓库。
