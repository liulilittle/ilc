# JIT 预留接口清单（现状审计 + 要求）

> 依据：docs/REQUIREMENTS.md 铁律 2j——JIT/动态编程是暂定计划，当前推进 AOT，
> 但设计不得堵死 JIT 路径：① 方法调用经函数指针表（可替换/可插桩）；
> ② 类型描述符可扩展（运行时类型注册已有）；③ 内存分配走运行时分配器（GC 已知）；
> ④ 元数据（方法/签名）运行时可达（M4 导出表 + 类型注册是第一步）。
>
> 审计基线：HEAD d3a8e9b（M4 核心完成：C API/四态产物）；M4 并行扩展域与
> M5 BCL icall 域（stringext/boxext/io/math 等）进行中（plan.md）。
> 标注约定：【已具备】= 现状已满足 JIT 复用；【需改造】= 现状存在但需演进；
> 【未来新增】= 当前不存在，属 JIT 阶段交付。
>
> 覆盖文件：src/runtime/include/internal/il2cpp_metadata.h、il2cpp_runtime.h、
> src/runtime/src/{metadata,api,gc,delegate,pinvoke,framestack,exception}.cpp、
> src/runtime/include/il2cpp_api.h、src/il2cppc/Emit/{MethodRefResolver,CodeGenerator,TypeEmitter}.cs。

---

## 1. 审计总表

| # | 预留项（铁律 2j 四方向） | 结论 | 现状要点 | 演进要求 |
|---|---|---|---|---|
| 1 | 类型描述符可扩展 | 【已具备】 | `Il2CppClass` 纯字段结构，可增字段；注册 API 通用 | 结构体只增不改，布局兼容 |
| 2 | 类型注册 API | 【已具备】 | `il2cpp_class_register` 接受任意 `Il2CppClass*`，幂等 | 容量需扩大（见 #6） |
| 3 | 内存分配走运行时分配器 | 【已具备】 | `il2cpp_object_new`/`il2cpp_array_new`/`il2cpp_string_new` 全托管分配统一入口 | JIT 代码调用同一 API |
| 4 | vtable 函数指针数组 | 【已具备】 | 每虚槽 = 函数指针，编译期生成（TypeEmitter.cs:220-234） | 槽位可指向 JIT 编译产物（ABI 一致即可） |
| 5 | 接口分派函数指针 | 【已具备】 | 接口映射表 + `il2cpp_interface_dispatch`（metadata.cpp:116） | 同上，slotMap 槽位可替换 |
| 6 | 委托 methodPtr 间接调用 | 【已具备】 | M3 委托：调用经对象内 methodPtr（delegate.cpp，StackFold DelegateInvokeExpr） | JIT 方法指针可直接入委托 |
| 7 | C API 导出表 = 方法指针表雏形 | 【已具备】 | `ExportEntry{typeName,methodName,fn}` 表（api.cpp:40-49）+ 注册 API（api.cpp:60-72） | 泛化为通用方法表（见 #8） |
| 8 | 通用方法表（全方法函数指针） | 【需改造】 | 直接调用 = 编译期符号 `{Type}_{Name}_{hash}()`（MethodRefResolver.cs:53；CodeGenerator.cs:178），运行时不查表 | 新增"方法表"登记全部（或动态入口）方法：方法 id → fnPtr；AOT 直接调用保留，JIT 调用点走表 |
| 9 | icall 表（.NET 方法名 → C++ 函数指针） | 【需改造】 | icall 声明在 il2cpp_runtime.h:104-134；**映射硬编码在转译器 C#**（MethodRefResolver.cs:72-111 特判 Console/Marshal/Environment/String/Object/Exception） | 运行时建 icall 注册表（方法名/签名 → 函数指针）；转译器改为查表（或生成绑定代码）。JIT 生成的代码才能在运行时解析 icall |
| 10 | 方法元数据运行时可达（签名/参数/IL） | 【需改造】 | 签名/IL 只在转译器 C# 侧（MethodBodyInfo/IlDecoder）；运行时 `Il2CppClass` 无方法描述符 | 元数据静态固化（紧凑二进制，只读段）→ 运行时方法描述符表（name/签名/fnPtr/IL 引用） |
| 11 | 类型注册表容量 | 【需改造】 | 静态数组 `kMaxRegisteredClasses=256`（metadata.cpp:38），满则 abort | BCL 全量（L1 裁剪）与动态类型需扩容（配置化容量或分段表/哈希表），fail-loud 保留 |
| 12 | 描述符运行时构造 | 【需改造】 | 描述符全部由转译器静态生成（TypeEmitter.cs:142-170），运行时只登记 | JIT 动态类型需运行时构造描述符（字段值可写，无需改结构；需分配策略：固定池或 GC 管理） |
| 13 | 导出表签名通用化 | 【需改造】 | 导出/调用仅支持 `int32(int32,int32,int32,int32)`（il2cpp_api.h:53；api.cpp:44） | 统一 Invoker ABI（任意签名，参数数组 marshalling，铁律 2g-2 已有设计） |
| 14 | 反射元数据表（typeof/GetType/Invoke/特性） | 【未来新增】 | 无运行时反射代码；M3 设计已定（元数据静态固化 + 保留清单） | "元数据运行时化"阶段交付 |
| 15 | 统一 Invoker 动态调用 ABI | 【未来新增】 | 无 | 反射与 JIT 共用（方法句柄 + 参数数组） |
| 16 | 泛型运行时预实例化清单 | 【未来新增】 | 转译器已支持泛型值类型实例化（StackFold GenericInstantiationName）；运行时泛型元数据无 | 编译期封闭世界分析 + 预实例化清单驱动 |
| 17 | 解释器兜底（M7） | 【未来新增】 | 无（铁律 1 主路径全 AOT；M7 为元数据/动态场景辅助） | iOS 合规的动态执行替代；共享元数据表 |
| 18 | 自研 x64 代码生成后端 | 【未来新增】 | 无 | JIT 核心：IR→机器码发射（覆盖 IR 子集，随需扩展） |
| 19 | 字符串池 interning | 【未来新增】 | ldstr 每次运行时构造（`il2cpp_string_new_from_cs`） | 编译期字符串池/运行时间表（性能路线图阶段 2 亦可先行） |
| 20 | 帧栈协议对 JIT 方法开放 | 【已具备】 | framestack.cpp 帧记录 API 与方法无关（方法名/类型名传参） | JIT 方法序言/尾声以同一协议注册 |

---

## 2. 逐项审计（证据 + 演进要求）

### 2.1 类型描述符可扩展 ——【已具备】

- `Il2CppClass`（src/runtime/include/internal/il2cpp_metadata.h:34-58）：纯字段结构
  `{name, instanceSize, refFieldBitmap, refFieldCount, baseClass, vtable, vtableSize,
  interfaces, interfaceCount}`。头部注释明确"one static instance per translated type…
  extensible"意图（metadata.h:4-20：M2 scope note，后续里程碑追加字段）。
- 注册 API 通用：`il2cpp_class_register(Il2CppClass*)`（metadata.cpp:55-75）接受任意
  指针，幂等（同指针二次注册 no-op）。
- **演进要求**：新增字段一律追加在结构尾部（不重排既有字段，保持
  AOT 生成代码与运行时 ABI 一致）；JIT 动态类型复用同一结构 + 同一注册 API。

### 2.2 内存分配走运行时分配器 ——【已具备】

- 生成代码全部托管分配路径都经运行时分配器：
  - 对象：`NewObjExpr → il2cpp_object_new(klass)`（StackFold.cs:714-750；il2cpp_runtime.h:43）
  - 数组：`newarr_ → il2cpp_array_new`（CodeGenerator.cs:1291-1296）
  - 字符串：`StringConstExpr → il2cpp_string_new_from_cs`（string.cpp）
  - 装箱：`il2cpp_box_int32`（boxing.cpp）
- GC 分配器：il2cpp_gc_alloc（gc.cpp，分代 Gen0/1/2+LOH+写屏障卡表，M3）。
- **演进要求**：JIT 生成的代码必须沿用同一分配 API（分配器互换只发生在运行时
  内部，API 稳定）。禁止任何生成路径引入裸 new/malloc 分配托管对象。

### 2.3 方法调用经函数指针表 ——【已具备部分，核心改造在 2.4】

- 虚调用：`obj->klass->vtable[slot]` 函数指针数组（TypeEmitter.cs:220-234 生成
  `{Type}_vtable[]`；CodeGenerator.cs:768-810 发射 `(…->klass->vtable[slot])(args)`）。
- 接口调用：`il2cpp_interface_dispatch` 返回 `&klass->vtable[typeSlot]`
  （metadata.cpp:116-154），调用方 cast 后调用。
- 委托：`DelegateInvokeExpr` 经委托对象 methodPtr（StackFold.cs:556-580；delegate.cpp）。
- 以上三处槽位都是"函数指针"——JIT 编译产物只要 ABI 一致即可替换。
- **演进要求**：vtable/接口映射表/委托 methodPtr 保持"槽位=函数指针"形态，
  新增的 ABI 约定（如 this 传递、返回值、异常）必须与 AOT 生成函数一致
  （统一 ABI 文档化，JIT 阶段强制）。

### 2.4 通用方法表（直接调用函数指针化）——【需改造】

现状（AOT 最快路径）：

- 直接调用（call/callvirt 非虚）解析为编译期符号
  `{Sanitize(owner)}_{Sanitize(name)}_{SigHash}`（MethodRefResolver.cs:52-55；
  发射于 CodeGenerator.cs:178 `MethodEmitter.EmitMethod`）。
- C API 侧已有"按名字查函数指针"的先例：导出表 `ExportEntry{typeName, methodName, fn}`
  （api.cpp:40-49），`il2cpp_method_invoke_i4` 线性扫描按名调用（api.cpp:101-125）。

改造方向（JIT 前置，非本阶段实现）：

1. **方法表数据结构**：全局 `MethodEntry{ const char* name; Signature sig; void* fnPtr; }`
   表（或 per-type 方法数组挂入 `Il2CppClass`，与 vtable 并列）。
   登记 API：`il2cpp_method_register(typeName, methodName, fnPtr)`——与既有
   `il2cpp_runtime_register_export`（api.cpp:60-72）同构，可先扩展后者语义。
2. **调用点策略（性能优先）**：
   - AOT 直接调用保持符号调用（零开销，铁律 2e）；
   - JIT 引入时，JIT 方法的调用点发射为"查方法表 → 间接调用"
     （或 AOT 侧 trampoline：调用点指向表槽，首次解析后回填）；
   - 导出表 → 方法表统一为一个表（避免双份登记）。
3. **不变量**：方法表是"唯一权威 fnPtr 源"；vtable 槽/接口槽/委托 methodPtr
   都可以从方法表槽位解析（或登记时互相同步）。禁止多个表各自维护
   同一方法的函数指针（D1 单一权威写入者）。

### 2.5 icall 表演进（.NET 方法名 → C++ 函数指针）——【需改造】

现状：

- 运行时侧：icall 以 C++ 符号声明（il2cpp_runtime.h:104-134），实现分散在
  runtime.cpp/string.cpp/array.cpp/pinvoke.cpp 等；**没有运行时 icall 注册表**。
- 转译器侧：调用到 icall 的映射**硬编码在 C#**：
  - `System.Console::WriteLine/Write` → `icall_console_write_line_*`（MethodRefResolver.cs:72-79, 141-172）
  - `Marshal.GetLastWin32Error` → `il2cpp_pinvoke_get_last_error`（:81-84）
  - `Environment.get_NewLine` → `icall_environment_get_newline`（:86-89）
  - `String.get_Length` → `il2cpp_string_get_length`（:93-96）
  - `Object/Exception::.ctor` → `il2cpp_object_ctor_nop`（:102-112）

问题：JIT 在运行时编译用户代码时，同样会发出 `call System.Console::WriteLine(int)`，
但 JIT 侧没有 C# 的 MethodRefResolver——**运行时必须能自己解析 icall**。

演进要求：

1. **运行时 icall 注册表**（【需改造】的最小形态）：
   `il2cpp_icall_register(const char* dotnetName /* "System.Console::WriteLine(int32)" */, void* fnPtr)`
   —— 运行时维护静态表（同导出表模式，kMax 常量 + fail-loud）。
2. **转译器行为不变**：AOT 生成的调用仍按 MethodRefResolver 静态解析
   （转译器只是"预先知道答案"）；但为将来统一，icall 注册语句可由生成代码
   （entry.cpp）发出，运行时表成为唯一真相（转译器特判可作为"编译期优化"保留）。
3. **签名编码复用**：注册名编码直接用 `MethodSignatureCoder` 的编码串
   （`i` / `r:System.String` / `V:...`，src/il2cppc/Emit/MethodSignatureCoder.cs），
   运行时与转译器共用同一编码约定（哈希与匹配两端一致）。

### 2.6 元数据运行时可达 ——【需改造】

现状：

- **类型级已具备**：`il2cpp_class_by_name`（metadata.cpp:87-101）运行时可查描述符。
- **方法级未具备**：方法签名（ParamTypes/ReturnType）与方法体 IL 只存在于
  转译器 C# 侧（MethodBodyInfo，src/il2cppc/Il/MethodBodyReader.cs:10-21）；
  运行时 `Il2CppClass` 无方法描述符；导出表只覆盖显式导出的方法。

演进要求（"元数据运行时化"阶段，见 路线图.md）：

1. 转译器把全量方法元数据序列化为紧凑二进制（静态 const 数组，只读段）：
   per-type 方法条目 `{name, 签名编码, RVA/IL 引用, fnPtr}`——挂入
   `Il2CppClass`（新增字段 `MethodEntry* methods; int32_t methodCount;`，追加在尾部）。
2. JIT 编译一个方法时：查该表取签名 + IL 体 → 编译 → **回填 fnPtr 槽位**。
3. 反射（铁律 2g-2：Invoke/GetValue/SetValue）与 JIT 共用同一表 +
   统一 Invoker（#15）。

### 2.7 容量与动态注册 ——【需改造】

- 类型注册表：`kMaxRegisteredClasses=256`（metadata.cpp:38），溢出 `abort`。
  BCL L1 裁剪后单程序集类型数可能远超 256 → 必须扩容（配置化常量或
  分段静态表），**保留 fail-loud**（铁律：GC 元数据不依赖 C++ 堆）。
- 导出表：`kMaxExports=256`（api.cpp:47），同上策略。
- 描述符运行时构造：JIT 动态类型需要"分配 + 填充 + 注册"路径。结构已支持
  （字段可写），需决定存储：固定池（静态数组 of Il2CppClass，环形分配）或
  GC 管理（描述符本身不可移动，需 pin 语义——建议固定池先行）。

### 2.8 已具备项的 JIT 复用清单（无需任何改动）

1. 对象模型 ABI：`Il2CppObject{klass}` 头 + 字段槽网格（instanceSize/refFieldBitmap
   驱动精确 GC）——JIT 类型只要填充同一描述符即可被现有 GC 扫描。
2. 分配 API 三件套：object_new / array_new / string_new。
3. vtable/接口映射/委托 methodPtr 槽位函数指针化。
4. 异常协议：`DotNetException`（exception.cpp）——JIT 方法抛/接同一类型。
5. 帧栈协议（framestack.cpp）——JIT 方法序言/尾声同一 API。
6. P/Invoke 动态解析（pinvoke.cpp：LoadLibrary/dlsym + 缓存）——JIT 动态加载
   原生库的同款模式（动态加载路径可直接参考）。
7. C API 边界防护（api.cpp try/catch 包裹）——JIT 方法经 C API 调用同样被防护。

---

## 3. 实施顺序建议（不阻塞当前 AOT 里程碑）

| 顺序 | 改造项 | 触发时机 |
|---|---|---|
| 1 | 类型注册表/导出表容量扩容 | M5 BCL 全量转译前（编译期即可验证容量） |
| 2 | icall 注册表 + 生成绑定代码 | M5 过程中（BCL icall 数量大增，硬编码特判不可维护） |
| 3 | 方法表（fnPtr 登记）+ 统一 ABI 文档化 | M5.5-M6（测试套件稳定后） |
| 4 | 元数据静态固化 + 运行时方法描述符 | 路线图"元数据运行时化"阶段 |
| 5 | 统一 Invoker + 反射表 | 与 4 同阶段（反射需求 M3 已设计） |
| 6 | 解释器（M7）/ x64 后端（JIT） | 路线图"代码生成器 JIT 化"阶段 |

每项改造完成标准：既有 AOT e2e 双跑（net48 参考 vs 产物）逐字节一致 +
三编译器零警告 + CI 全绿（docs/REQUIREMENTS.md 质量门）。
