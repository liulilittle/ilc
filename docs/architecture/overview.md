# 混合架构总览：AOT 主路径 + JIT 预留（暂定）

> 定位：本文档描述 ILC 的总体架构——当前 AOT（IL→C++→原生）主路径，
> 以及未来 JIT/动态编程（用户运行时动态写代码执行）与 AOT 如何共存。
> JIT 是**暂定计划**（docs/REQUIREMENTS.md 铁律 2j），当前只推进 AOT，
> 但所有架构决策不得堵死 JIT 路径。
>
> 权威要求：docs/REQUIREMENTS.md 铁律 1（AOT 静态转译）、铁律 2e（性能 >= 官方 JIT）、
> 铁律 2j（性能优先 + JIT 预留 + 属性折叠）、铁律 13（iOS 无动态代码生成，M7 解释器兜底）。
>
> 配套文档：`docs/architecture/jit-预留.md`（接口审计）、`docs/architecture/属性折叠设计.md`、
> `docs/architecture/性能路线图.md`、`docs/architecture/路线图.md`。

---

## 1. 系统定位与现状快照

本项目 = **ILC（IL Compiler）**：把 .NET IL 静态编译为 C++ 源码，再由原生工具链
（MSVC / MinGW clang / WSL GCC 7.5）编译成原生产物（EXE/DLL/SO/静态库）。
"il2cpp" 仅为形象比喻（同为 IL→C++），正式定位是 ILC（铁律 2i）。

当前执行路径（M1-M4 已实证，全部为真实代码）：

```
C# 源码 (net48)
   │  csc
   ▼
程序集 (PE + IL + 元数据)
   │  src/il2cppc/MetadataLoader.cs（类型/方法/字段/签名/PInvoke/ImplMap 解码）
   ▼
TypeDef / MethodDef / FieldDef 模型（src/il2cppc/Model/）
   │  src/il2cppc/Il/MethodBodyReader.cs（方法体定位）+ Il/IlDecoder.cs（IL 解码）
   ▼
IlInstruction 序列
   │  src/il2cppc/Ir/StackFold.cs（栈机→表达式树折叠，最大化折叠深度，铁律 2f-2）
   ▼
IR 语句（Stmt/Expr，src/il2cppc/Ir/）
   │  src/il2cppc/Emit/CodeGenerator.cs（类型/方法/vtable/接口表/descriptor/entry 发射）
   │  + Emit/MethodRefResolver.cs（调用目标→C++ 函数名/icall 名）
   ▼
C++ 源码工程（generated.h + 每类型 .cpp + entry.cpp + CMakeLists.txt）
   │  MSVC / MinGW / WSL GCC 7.5（-O2，三编译器零警告质量门）
   ▼
原生产物（当前：EXE 与静态库/动态库，M4 四态产物）
   │  静态链接 src/runtime 运行时核心
   ▼
执行：il2cpp_runtime_init（生成代码）→ 类型注册 → .cctor → 托管 Main
```

关键实现点（证据）：

- **转译器流水线**：`CodeGenerator.Generate`（src/il2cppc/Emit/CodeGenerator.cs:100-205）
  每类型发射 vtable 规划 → 接口映射表 → 类型描述符 → 静态存储 → 方法体
  （`MethodBodyReader.Read` → `IlDecoder.DecodeMethod` → `StackFold.Fold` → `EmitBody`）。
- **方法命名**：`MethodRefResolver.Resolve`（src/il2cppc/Emit/MethodRefResolver.cs:30）
  产出确定性 C++ 函数名 `{Type}_{Name}_{SigHash}`（含参数签名哈希，重载安全）。
- **调用形态**：直接调用 = 编译期符号调用；虚调用 = `obj->klass->vtable[slot]`
  （CodeGenerator.cs:768-810）；接口调用 = `il2cpp_interface_dispatch`（src/runtime/src/metadata.cpp:116）。
- **运行时**：`src/runtime/`（对象模型/GC/类型注册/icall/string/array/delegate/boxing/
  exception/framestack/pinvoke/api，见下节）。

---

## 2. 组件图

### 2.1 当前（AOT-only）组件图

```
┌────────────────────────────────────────────────────────────────────┐
│ 转译器 il2cppc（C#，编译期，一次性）                                 │
│   MetadataLoader → MethodBodyReader → IlDecoder → StackFold         │
│   → CodeGenerator（TypeEmitter / MethodEmitter / MethodRefResolver   │
│     / VTablePlanner / CppWriter / SigHash）                          │
│   → 生成 C++ 工程（generated.h / <Type>.cpp / entry.cpp / CMakeLists）│
└──────────────────────────────┬─────────────────────────────────────┘
                               │ 静态生成（确定性、可审计、可再生，铁律 1）
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│ 原生工具链（三编译器，-O2）                                           │
└──────────────────────────────┬─────────────────────────────────────┘
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│ 运行时核心 src/runtime（C++，AOT 与未来 JIT 共享）                    │
│                                                                    │
│  ┌──────────────┐ ┌────────────────┐ ┌──────────────────────────┐  │
│  │ 对象模型      │ │ GC              │ │ 类型注册表               │  │
│  │ Il2CppObject  │ │ 分代 Gen0/1/2   │ │ metadata.cpp            │  │
│  │ Il2CppClass   │ │ +LOH+写屏障卡表  │ │ name→descriptor 线性表   │  │
│  │ (il2cpp_     │ │ (il2cpp_gc.*)   │ │ il2cpp_class_register / │  │
│  │  metadata.h)  │ │                │ │ il2cpp_class_by_name    │  │
│  └──────┬───────┘ └───────┬────────┘ └───────────┬──────────────┘  │
│         │                 │                      │                 │
│  ┌──────▼─────────────────▼──────────────────────▼──────────────┐  │
│  │ 托管类型支撑：string / array / delegate / boxing / exception / │  │
│  │ framestack（帧记录=GC 安全点=栈根扫描 三合一）/ pinvoke         │  │
│  └──────────────────────────┬───────────────────────────────────┘  │
│                             │                                      │
│  ┌──────────────────────────▼───────────────────────────────────┐  │
│  │ icall 层：il2cpp::runtime::icall_*（Console/Marshal/         │  │
│  │ Environment/String 内部支撑，il2cpp_runtime.h 声明）          │  │
│  └──────────────────────────┬───────────────────────────────────┘  │
│                             │                                      │
│  ┌──────────────────────────▼───────────────────────────────────┐  │
│  │ C API 边界（对外）：il2cpp_api.h / api.cpp                    │  │
│  │   runtime_init / gc_collect / type_from_name /                │  │
│  │   method_invoke_i4 / get_last_exception + 导出表 ExportEntry  │  │
│  └──────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────┘
                               │
                               ▼
             最终产物：EXE / DLL / SO / 静态库（M4 四态）
```

### 2.2 未来（AOT + JIT 共存）组件图（暂定设计方向）

```
┌────────────────────────────────────────────────────────────────────┐
│ 转译器 il2cppc（AOT 路径不变）                                      │
│   + 新增：动态入口分析（typeof/GetType/Invoke/泛型实例 保留清单）      │
│   + 新增：元数据全量静态固化（紧凑二进制表，只读段）                  │
└──────────────────────────────┬─────────────────────────────────────┘
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│ 运行时核心（AOT/JIT 共享，唯一真理源）                                │
│   对象模型 | 分代 GC | 类型注册表 | icall 表 | 方法表(函数指针) |       │
│   元数据表(签名/IL/字符串池) | 帧栈三合一 | C API                     │
└───────┬───────────────────────────────────┬────────────────────────┘
        │ AOT 方法（编译期生成）              │ JIT 方法（未来，运行时生成）
        ▼                                   ▼
   {Type}_{Name}_{hash}() 静态函数    自研小 x64 后端（IR→机器码）
        └───────────────┬───────────────┘
                        ▼
          统一调用入口：方法表 / vtable / 委托 methodPtr
          （AOT 槽位注册静态函数，JIT 槽位注册编译产物，
            调用方无感知 —— 可替换/可插桩，铁律 2j①）
```

共存原则：

1. **运行时核心不区分调用方**：JIT 编译的方法与 AOT 方法共享同一对象模型、
   同一 GC（分配走 `il2cpp_object_new` / `il2cpp_array_new` / `il2cpp_string_new`）、
   同一类型注册表、同一 icall 表、同一帧栈协议（GC 根扫描/StackTrace）。
2. **调用边界统一**：方法调用最终都落到函数指针（vtable 槽、接口映射槽、
   委托 methodPtr、未来方法表槽）或直接符号——JIT 引入后，**新增的调用点**
   一律走方法表；既有 AOT 直接调用保留（性能），由"元数据运行时化"阶段统一。
3. **iOS 合规（铁律 13）**：动态代码生成受 App Store 政策限制。JIT 定位为
   桌面/服务器形态的可选能力；移动端动态场景走 M7 解释器（读元数据+IL 字节码
   执行，零机器码生成）。两者共享同一套运行时元数据表。
4. **JIT 不破坏 AOT 性能**：JIT 是增量能力——用户程序全部静态可达时
   （封闭世界，铁律 1 主路径）零 JIT 开销；JIT 只在动态入口（编译期分析
   分析不到的用户运行时写代码场景）才介入。

---

## 3. AOT 与 JIT 共享的运行时核心（现状盘点）

| 核心组件 | 现状文件 | 内容 | JIT 复用的方式 |
|---|---|---|---|
| 对象模型 | src/runtime/include/internal/il2cpp_object.h + il2cpp_metadata.h | `Il2CppObject{klass}`、`Il2CppClass` 类型描述符 | 原样复用；JIT 方法按同一 ABI 收发对象 |
| GC | src/runtime/src/gc.cpp（分代 Gen0/1/2+LOH+写屏障卡表，M3） | 分配/标记/清扫/压缩；静态根+帧根 | JIT 代码经同一分配 API；JIT 方法序言/尾声走同一帧根协议 |
| 类型注册表 | src/runtime/src/metadata.cpp | `il2cpp_class_register` / `il2cpp_class_by_name` | 动态类型注册到同一表（容量需改造，见 jit-预留.md） |
| 类型描述符 | src/il2cppc/Emit/TypeEmitter.cs:142-170（生成） | vtable、接口映射、refFieldBitmap、instanceSize | 可扩展结构；JIT 类型运行时构造描述符 |
| icall 层 | src/runtime/include/internal/il2cpp_runtime.h:104-134 | `icall_console_write_line_*`、`il2cpp_object_ctor_nop`、`icall_environment_get_newline`、Marshal icalls | JIT 代码需要运行时可解析的 icall 表（现状映射在转译器 C# 侧，需改造） |
| 字符串/数组/委托/装箱 | string.cpp / array.cpp / delegate.cpp / boxing.cpp | 分配与基础操作 | 原样复用 |
| 异常 | src/runtime/src/exception.cpp（DotNetException + 顶层防护 + 帧栈报告） | try/catch/finally、跨 C 边界防护 | JIT 代码抛同一异常类型/throw helper |
| 帧栈三合一 | src/runtime/src/framestack.cpp | 托管帧记录 = GC 安全点 = 栈根扫描 | JIT 方法序言注册同一帧记录（API 已具备） |
| C API | src/runtime/include/il2cpp_api.h + src/api.cpp | `il2cpp_*` 导出 + 导出表（ExportEntry[256]） | 方法指针表雏形（见 jit-预留.md 审计） |
| 方法解析 | src/il2cppc/Emit/MethodRefResolver.cs | 调用 token → C++ 函数名 / icall 名 | 需演进为运行时方法表（转译器负责"登记"，运行时负责"解析"） |

---

## 4. JIT 暂定计划的边界（当前只做 AOT）

**范围声明（本阶段）**：以下内容**不在**当前任何里程碑内实现，仅作为
架构约束存在——即"任何新代码不得使以下目标无法达成"：

1. **方法调用可替换**：vtable/接口映射已是函数指针数组（已具备）；
   直接调用暂保持编译期符号（最快），但方法命名与注册路径
   （导出表、未来方法表）不得设计成"函数指针不可插入"。
2. **类型描述符可扩展**：`Il2CppClass` 结构体只增不改（字段追加、布局
   兼容）；已有多数组（类型注册表、导出表、vtable 数组）与描述符字段
   （metadata.h:34-58）均按此原则维护。
3. **内存分配走运行时分配器**：生成代码禁止裸 `new`/`malloc` 分配托管对象，
   一律 `il2cpp_object_new` / `il2cpp_array_new` / `il2cpp_string_new`
   （现已在 StackFold 的 NewObjExpr / Newarr / Ldstr 路径强制）。
4. **元数据运行时可达**：M4 导出表 + 类型注册是第一步（方法指针可按
   typeName+methodName 查到）；完整方法元数据（签名/IL/参数）运行时化是
   后续"元数据运行时化"阶段，当前只要求"转译器生成路径不堵死"。
5. **帧协议不假设"只有静态函数"**：帧栈记录 API（framestack.cpp）按
   方法指针级协议设计，未来 JIT 方法以同样协议登记。
6. **iOS 约束优先**：任何组件不得有 mmap 可执行内存 / 动态生成机器码路径
   （铁律 13）。JIT 是独立可选组件，不进入移动端默认产物。

边界检查清单（每次架构决策自问，铁律 2j）：
> "JIT 时代还能工作吗？"——调用点是否可替换？类型描述符是否可扩展？
> 分配是否走运行时分配器？元数据是否运行时可达？

---

## 5. 相关文档索引

- 用户铁律：docs/REQUIREMENTS.md（铁律 1/2e/2f-2/2j/2k/13）
- 设计规格：docs/superpowers/specs/2026-08-02-il2cpp-design.md
- IL 语义研究：docs/research/il-semantics-reference.md
- 本目录：jit-预留.md（审计清单）、属性折叠设计.md、性能路线图.md、路线图.md
