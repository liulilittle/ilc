# 自研 IL2CPP：C# IL → C++ 静态转译器 + 自研运行时 设计文档

日期：2026-08-02
状态：用户需求确认完毕，待用户审阅 spec

## 1. 背景与目标

将 C# 编译器产物（.NET Framework 4.8 IL 程序集）完整静态转译为 C++ 源码，链接自研 C++ 运行时（FCL 静态库），再用 VC++/MinGW GCC 编译为原生产物。目标是对齐 .NET 官方行为，以完整回归测试套件验收。工程级完整实现，系统性工程，非玩具。

## 2. 需求决策（用户确认记录）

| # | 决策 | 内容 |
|---|---|---|
| 1 | 项目定性 | 工程级完整实现，非玩具；长期项目，逐步推进，不以短期完成为目标 |
| 2 | 运行时 | 自研 C++ 运行时（Unity libil2cpp 风格），零第三方依赖 |
| 3 | BCL | 全转译，含官方 BCL；基线 .NET Framework 4.8（参考程序集取自 NuGet 官方包） |
| 4 | 转译器语言 | C#（.NET 8），System.Reflection.Metadata |
| 5 | 平台 | Windows 双编译器验证：MSVC cl + MinGW g++ |
| 6 | 验收 | 完整回归测试套件，对齐 .NET 官方行为（双跑逐字节对比） |
| 7 | 静态编译 | 硬约束：转译产物 + 运行时全静态链接（/MT、-static），单 exe 零运行时依赖；泛型编译期展开、vtable 编译期生成 |
| 8 | FCL 形态 | 自研框架类库静态库，帮助链接进最终产物 |
| 9 | P/Invoke+Marshal | 完整支持（重要） |
| 10 | 产物形态 | EXE / DLL / SO / 静态库四态；导出 C API 供外部 C++ 直接调用我们写的库 |
| 11 | 依赖最小化 | 能自造就自造：GC/元数据/string/集合全部自研，产物仅依赖 OS 固有 API |
| 12 | GC | 实现 .NET 分代 GC（Gen0/1/2 + LOH），精确 GC（编译期生成根集合/字段位图/写屏障） |
| 13 | 异常 | .NET 异常语义专门设计（继承匹配/filter/finally/fault/跨 C 边界/资源安全） |
| 14 | StackTrace | 完整托管栈帧记录（编译期 MethodInfo + 运行期帧栈 + 异常回溯），不依赖 C++ 调试符号 |
| 15 | 动态运行时 | 反射全套 + 高度泛型支持；编译时预测分析（封闭世界）+ 元数据全量内嵌 |
| 16 | 无 JIT | 硬约束：iOS 禁止动态代码生成；全链路纯 AOT，M7 解释器兜底 ≠ JIT |
| 17 | 执行策略 | 24 个子代理并行分派，模块边界共享契约（运行时头文件）先定稿 |
| 18 | Emit 不支持 | System.Reflection.Emit（运行时动态生成 IL/新类型）明确不支持：静态架构下执行动态 IL 需 JIT（禁止）或解释器 + 运行时类型系统扩展（与编译期固化的类型表冲突）。M7 解释器即使实现也不含 Emit。动态入口分析器对 Emit 调用直接拒绝/告警 |
| 19 | 全球化 | 完整 CLDR 支持（全部语言/排序/日历/格式化）。数据策略：CLDR 数据（Unicode 联盟开源数据）编译期提取为 C++ 静态表内嵌（数据非代码，不算第三方依赖），算法自研。工作量巨大，独立远期里程碑 |
| 20 | dynamic/DLR | 完整支持。**关键认知：dynamic 不是运行时 JIT**——C# 编译器将 dynamic 用法编译为静态 CallSite\<T\> 对象 + Binder 调用（普通 IL 静态代码，可全转译）。.NET 中 binder 用 Expression.Compile(=JIT) 缓存调用 plan，我们替代为：绑定结果 = 元数据表方法句柄 + Invoker 调用，缓存的是数据结构非机器码 → 零 JIT 成立。归入主线（6.3 动态运行时域），依赖 Invoker + 反射元数据表就绪后实施 |
| 21 | COM 互操作 | 支持（[ComVisible]/RCW/CCW/IDispatch/vtable 绑定/HRESULT↔异常映射）。COM 是 Windows 系统组件（OS 固有，不算第三方依赖）。独立远期里程碑 |

## 3. 架构总览

**架构本质（一句话）：把 .NET 运行时"编译进产物"——转译代码 + 自研运行时（异常/GC/崩溃防护/StackTrace 系统）静态链接为一个整体，全链路 AOT，零 JIT。程序内的崩溃防护与托管语义由内嵌运行时兜底，与 CLR 之于托管程序的关系同构；C++ 仅是实现载体，程序层面行为与 .NET 一致。**

```
输入（编译期固定）：用户程序集 + 官方 BCL 程序集（.NET Framework 4.8）
  ↓
[1] 转译器 (C#, .NET 8)
  ├─ ① 元数据层: System.Reflection.Metadata 读 PE/类型/方法/签名/泛型/特性
  ├─ ② 分析器:   扫描动态入口 → 保留清单（类型/方法/泛型实例/Invoker/元数据表）
  ├─ ③ IL 层:    指令流 → 基本块 → 栈机折叠为表达式树 → IR
  └─ ④ 代码生成: IR → 静态 C++（类型/方法/泛型实例/异常/帧记录/写屏障/元数据表）
  ↓
[2] 生成产物: *.cpp/*.h（可读可审计可再生）
  + [3] 自研运行时静态库（FCL 静态库，纯 C++，零依赖）
  ↓
[4] 构建: cmake → MSVC cl / MinGW g++ 双编译
  ↓
[5] 产物四态: EXE（单文件静态） / DLL / SO / 静态库；C API 导出（il2cpp_*）
```

## 4. 转译器设计

### 4.1 元数据层
System.Reflection.Metadata 解析全输入程序集 → 全量类型图（类型/方法/字段/继承/接口实现/泛型参数约束/特性/PInvoke 声明/异常处理表），代码生成与元数据内嵌的查表基础。

### 4.2 分析器（编译时预测用户动态需求 + 裁剪）
- 扫描全部动态入口：typeof / GetType(string) / Activator.CreateInstance / MakeGenericType / MakeGenericMethod / MethodInfo.Invoke / 特性查询 / 序列化（Xml/Json/Soap）/ 委托动态绑定 / BindingFlags 枚举；Emit 调用直接拒绝/告警
- 输出保留清单：动态路径可能触及的类型/方法/泛型实例 → 强制预编译 + 元数据全量保留
- 无法静态解析的动态点 → 分析器告警 + 用户声明清单补全
- 统一驱动：保留清单 → 代码生成补全（类型/方法/泛型实例/Invoker/元数据表）

#### 4.2.1 裁剪分析（安全裁剪，裁剪不出错）
- **保留集 = 静态可达图 ∪ 动态保留清单**（双层保守合并）：静态可达图从入口点（Main + 导出 C API）沿方法调用/虚调用/接口分派/委托构造/泛型实例化传播；动态侧由反射入口分析驱动
- **保守规则**：保留类型 → 虚方法表全留（虚调用目标静态不可定）；公共成员默认全留（反射字符串/枚举可能触达）；特性实例全留；序列化器（Xml/Json）引用的类型全留；判定不出的继承链全留
- **编译期闭合证明**：所有动态查找入口的目标必须在保留集中；不在 → 编译失败（问题拦截在编译期，运行期绝不空手）
- **运行期兜底**：缺目标抛明确异常（.NET MissingMethodException 语义），不静默 null
- **双模式测试**：每个测试裁剪开/关双跑，行为必须一致
- **分级**：L0 不裁剪（全保留，仅用于小规模正确性验证）/ L1 代码级裁剪（**默认架构模式**，闭合证明通过才放行）/ L2 成员级深度裁剪（需用户清单背书，高危）
- **裁剪是默认管线，不是优化项**：转译器只生成保留集内代码；BCL 全量转译（M5）以 L1 为默认——从用户程序出发，分析驱动 BCL 类型按需进入保留集（IL2CPP 同款做法）。全量 L0 编译 BCL 物理不可行（编译时间爆炸、产物臃肿），仅用于小测试集验证正确性

### 4.3 IL 层（IR）
逐方法：基本块划分 → 控制流图 → 栈机指令折叠为表达式树（ldloc/ldc/stloc 序列 → 表达式）；折叠不了的（异常路径/复杂状态）退化为显式临时变量。保留异常处理信息（try/catch/finally 边界）供生成映射。

### 4.4 代码生成
- 类型 → struct + 编译期方法表（vtable + 接口映射表），字段布局按 .NET 规则计算
- 方法 → 确定性编码的静态函数（含泛型参数编码），noinline 禁止内联
- 泛型 → 按保留清单实例化展开；引用类型实参共享方法体（.NET 语义）
- 异常 → try/catch 映射 + RAII finally + 类型匹配重抛（见 6.3）
- 帧记录 → 方法序言压栈 MethodInfo（见 6.4）
- 写屏障 → 引用字段赋值处插入（见 5.2）
- 字符串池 → 字面量编译期固化只读
- 输出：N 个 .cpp/.h + 类型注册表 + 元数据表

#### 4.4.1 生成产物工程结构（拆分策略）- **拆分文件（规避 bigobj）**：按"程序集 → 命名空间族 → 类型"分层拆 .cpp，每文件目标大小可配；超大型类型/巨方法独立文件；MSVC /bigobj 照加作保险兜底 + /MP 并行编译；MinGW 同样拆分（内存/编译速度）。单文件爆大会导致 bigobj 超限（64K 段）+ 编译器内存爆炸 + 无法并行编译
- **生成 CMakeLists.txt**：转译器自动生成完整工程配置（target 名/产物类型 EXE|DLL|SO|STATIC 可配、/MT 静态链接、/EHsc、-fexceptions、C++17、include 路径、导出可见性）
- **include 双目录**：
  - `include/public/`：对外 API —— il2cpp.h（C API）+ 导出函数声明头，外部 C++ 程序 include 的唯一入口
  - `include/internal/`：项目内部 —— 对象模型/类型表/元数据表/icall 声明/注册表等生成代码与运行时内部契约头，不对外
- 目录布局：gen/（CMakeLists.txt + src/ + include/（public|internal）+ runtime/）
- 静态初始化顺序安全：类型注册表按依赖拓扑分批，cctor 守卫保证跨文件注册序不敏感

#### 4.4.2 生成代码风格规范（超高质量标准）
- 生成代码必须达到手写高质量工程代码水准（可读/可审计/确定性/规范统一）
- **高度缩进与列对齐（参考 D:\dd\openppp2 风格）**：4 空格缩进禁 tab；行尾注释右对齐第 100 列；声明区类型/名称/注释列对齐；连续逻辑行列对齐
- 完整规范见 `docs/superpowers/specs/2026-08-02-generated-code-style.md`（命名 sanitize/注释规范/表达式多括号/确定性/质量门零警告/审查要求）
- 确定性：同输入同输出（无时间戳/绝对路径/随机顺序）；CI 质量门：生成代码双编译器零警告

## 5. 运行时设计（FCL 静态库主体，纯 C++ 零第三方依赖）

### 5.1 对象模型
- GC 对象统一布局：对象头（类型指针 + GC 标记）+ 数据
- 类型表 TypeMetadata：字段偏移/大小/基类/接口表/vtable/方法指针表/反射元数据
- string（长度+UTF16）、array（元素类型+长度+数据）专用布局；字面量编译期字符串池
- 值类型 C++ 值语义，装箱复制进堆

### 5.2 分代精确 GC（Gen0/1/2 + LOH）
- 精确性来源（编译期生成，运行时零反射）：类型级引用字段位图；方法级根集合表；栈扫描按帧根表
- 分代：Gen0 快分配频繁回收 / Gen1 / Gen2 + LOH 单独；存活晋升 Gen0→1→2
- 写屏障 + 卡表：老→新引用标记卡片，避免全堆扫描
- 压缩：Gen0/1 回收带压缩，Gen2 标记-清除，LOH 不压缩（对齐 .NET）
- 实现节奏：M2 精确标记-清除（不分代）→ M3 分代+卡表+写屏障+压缩；GC 接口抽象（Alloc/Collect/AddRoot/WriteBarrier）M2 定死
- P/Invoke pinning 支持

#### 5.2.1 内存管理子系统（GC 物理层，独立工程）
- **段管理**：OS 大块申请（Windows VirtualAlloc / Linux mmap）→ 按代划分（gen0/1 共段、gen2 段、LOH 段）；段头元数据；空闲段池复用、整段归还
- **三层分配器**：Gen0/1 bump-pointer（最快路径）；Gen2 free-list；LOH 独立分配器（85000 字节阈值，对齐 .NET）
- **碎片整理**：Gen0/1 压缩回收（移动对象 + 全图引用更新：对象图/卡表/弱引用表/GCHandle 表）；Gen2 标记-清除 + 空闲块合并（coalescing），不移动，压缩模式可配置（GCSettings）；LOH 标记-清除 + 合并，压缩模式可配置（GCLargeObjectHeapCompactionMode 对齐 4.8）；碎片率统计 → GC.GetTotalMemory/内存报告
- **GC Pending 状态机**：GC 请求（显式 GC.Collect / 隐式分配压力与代满）→ 串行 GCRequest 队列；线程挂起 = stop-the-world + 协作式安全点；finalization pending（f-reachable 队列，两次回收语义）；弱引用 pending（回收时清理）；分配 pending（触发回收后重试）；GC 期间禁止托管分配（死锁安全）
- **三合一机制**：方法序言帧记录栈 = GC 安全点 = 栈根扫描信息（一个机制服务 StackTrace / GC 挂起 / 精确根扫描）

### 5.3 异常体系
- 载体：C++ 原生异常（非 setjmp/longjmp），统一类型 DotNetException（持有托管异常 GC 引用）
- catch 子句 → catch(DotNetException&) + 运行时继承类型匹配，不匹配重抛
- finally → RAII 守卫（析构执行），嵌套各自守卫；finally 内抛异常按 .NET 规则替代原异常
- filter → catch(...) + 过滤表达式执行，false 重抛
- 跨 C 边界（C API/P/Invoke 回调）：所有导出边界 try/catch 防护层，捕获 → 错误码 + 挂起异常对象（il2cpp_get_last_exception）
- 转译代码资源全 RAII；MSVC /EHsc、MinGW -fexceptions 全链开启
- 异常对象是 GC 对象（System.Exception 派生链）

### 5.4 托管栈帧记录（完整 StackTrace）
- 编译期：每方法静态 MethodInfo（类型名/方法名/签名/IL 序列点行号表）
- 运行期：线程局部托管帧栈；序言压栈（~1 次内存写）、返回弹出
- 异常抛出 → 回溯帧栈 → 重建 StackTrace（格式对齐 .NET）
- 铁律：禁内联（noinline）、禁尾调用优化、导出边界帧记录照常
- 与 C++ 调试符号完全解耦，Release 信息不减

### 5.5 委托 / 装箱 / icall
- 委托 = 对象（方法指针 + 目标实例 + 组合链），invoke 编译期生成；C 回调 thunk 带 GC 根保持
- icall 平台层：BCL OS 交互方法（Console/File/Environment/Thread/加密等）→ C++ 实现映射表；每个 icall 是测试点

## 6. P/Invoke / Marshal / 反射 / 动态运行时

### 6.1 P/Invoke（DllImport）
- 转译器识别 [DllImport] → 生成原生导入声明（不翻译 stub IL）→ 运行时 P/Invoke 层
- 静态解析：DllName 指向自研库/已知系统库 → 编译期直接绑定（零开销）
- 动态解析：LoadLibrary/dlopen + CallingConvention/CharSet/SetLastError 元数据驱动
- Marshalling 编译期生成：blittable 直传；string 按 CharSet 转缓冲；array pin 后传指针（保守语义天然支持）；委托 → 函数指针 thunk；[StructLayout(Explicit)] 按 FieldOffset
- SetLastError/GetLastError → 线程本地错误码槽
- Marshal 类照常转译（有 IL），OS 交互走 icall

### 6.2 产物形态与 C API
- 四态：① EXE 单文件静态链接；② DLL/SO；③ 静态库 .lib/.a；④ C++ 直接调用
- 导出 C 兼容 API（复用 il2cpp_* 形态）：il2cpp_runtime_init / il2cpp_assembly_load / il2cpp_type_from_name / il2cpp_method_invoke / il2cpp_gc_alloc / il2cpp_gc_collect / il2cpp_get_last_exception
- 依赖清单：零第三方；产物仅依赖 OS 固有 API；运行库 /MT / -static

#### 6.2.1 托管方法原生导出（DLL 函数导出）
- 任意 C# 方法 → 原生导出函数（DllExport 模式），四态产物全覆盖
- **导出契约（硬性限制，违反即编译拒绝）**：① 仅静态方法（禁止实例/虚方法导出）；② 参数与返回值仅限基础类型（blittable 标量：整数/浮点/布尔/指针/char 等）或 **buffer 传递**（`byte* + 长度` 或等价的指针+大小对）；③ 对象/string/数组/struct 一律禁止直接跨边界——必须经 buffer 显式传递，托管侧用 Marshal.Copy 等编解码。理由：跨边界对象引用携带 GC 根生命周期问题（悬挂引用/根泄漏）、struct 布局对齐风险、ABI 不稳定；标量+buffer 是 C ABI 最小公分母，边界语义干净
- 声明机制：[UnmanagedExport(name, callingConvention)] 源特性 或 转译期导出清单（两种都支持）
- 生成 C 兼容导出 thunk（编译期）：MSVC __declspec(dllexport) / MinGW __attribute__((visibility("default")))；cdecl/stdcall/fastcall 可配（Windows stdcall 命名修饰对齐）
- 异常边界：thunk 内 try/catch → 错误码 + il2cpp_get_last_exception()（复用 6.2 防护层，C++ 异常不穿 extern "C"）
- 运行时自举：惰性 init（首调自动初始化）或宿主显式 il2cpp_runtime_init()
- 与 C API 同一机制：导出 thunk = il2cpp_method_invoke 的静态类型化封装

### 6.3 反射与动态运行时
- 反射元数据静态固化：类型表扩展为完整反射元数据（方法/字段/属性/事件/特性实例/泛型参数/基类链/接口表），编译期序列化 static const 数组 → 只读段
- System.Reflection 类型照常全转译，底层查询走静态表（icall 化），零运行时解析
- 统一 Invoker ABI：Il2CppObject* invoke(void* argbuf, int argc)；MethodInfo.Invoke/委托动态绑定/泛型方法反射 = 查表调用
- 泛型运行时分层：静态实例化 + 封闭世界分析（保留清单）+ 共享代码 + 用户清单补全 + M7 可选解释器兜底
- 边界（诚实声明）：不能运行时加载新程序集/新类型；未预见的泛型实参组合需解释器兜底（M7）；**不支持 System.Reflection.Emit（运行时动态生成 IL/新类型）**——执行动态 IL 需 JIT（禁止）或解释器+运行时类型系统扩展（与静态架构冲突），Emit 调用由分析器拒绝

## 7. BCL 全转译策略
- 输入 .NET Framework 4.8 官方程序集全集（NuGet Microsoft.NETFramework.ReferenceAssemblies）
- 转译优先级：core → Collections → IO/Text → Threading → Reflection → InteropServices → Globalization → Net → Xml → 其余
- icall 边界：一切 OS 交互方法在 BCL 侧只留签名，运行时 icall 实现
- 元数据全量内嵌：自定义紧凑二进制（非 PE 直拷），静态只读段，体积数十 MB 级（按需裁剪列为优化项）

## 8. 测试套件（验收标准）
- 每测试 = 一个 C# 程序（stdout + 格式化结果）；双跑对比：dotnet 运行 vs 转译 exe，输出逐字节一致
- 覆盖矩阵：语义单元 / 异常矩阵（catch/finally/嵌套/重抛/filter/StackTrace 格式）/ GC 行为（晋升/代回收/弱引用/终结器）/ P/Invoke / Marshal 全套 / BCL 各命名空间 / 反射与动态调用 / 泛型
- 基础设施：测试驱动 + diff + 覆盖报告，M1 起架设
- 双编译器验证：MSVC + MinGW 各自全跑

### 8.1 测试工程体系（长期流程铁律）
- **差分测试（主武器）**：同一程序双跑逐字节对比，全量覆盖
- **测试程序生成器（模糊差分）**：随机生成 C# 程序（随机表达式/类型/控制流/泛型组合）→ 双跑对比，大规模刁钻覆盖
- **定向刁钻用例矩阵**：cctor 重入 / Nullable 装箱 / finally 嵌套重抛 / 弱引用时序 / 终结器两次回收 / GC 压力与碎片 / 浮点 NaN-Inf-负零 / decimal 边界 / 字符串代理对 / 文化格式化 / 溢出边界 / LOH 大数组 / 泛型共享代码 / 静态初始化拓扑
- **属性测试**：数学恒等验证随机输入
- **回归防退化铁律**：每个 bug = 先写失败测试 → 修复 → 用例永久入库（永不再现）
- **CI 体系**：双编译器全量测试，每提交/每日跑，失败阻断
- **分层测试**：单元（运行时组件）/ 集成（转译端到端）/ 系统（BCL 域行为）/ 性能基准（分配率/GC 压力/编译时间）
- **转译器健壮性**：任意合法输入不崩溃、错误报告友好（分析器/闭合证明失败给可行动信息）
- **长期迭代流程（方法论）**：测试发现 → 根因分析 → 修复 → 回归用例入库 → 全量重跑；无限循环直到套件全绿（项目周期长的内生机制）

## 9. 里程碑

| 里程碑 | 内容 | 验收 |
|---|---|---|
| M1 | 转译器骨架：PE→IL→纯计算程序（icall 支撑 Console） | Hello World 级正确输出 |
| M2 | 对象模型+类型表+精确根扫描+标记清除 GC+类/继承/接口/虚方法/装箱 | 面向对象程序双跑一致 |
| M3 | 分代 GC（卡表/写屏障/压缩 + 内存管理子系统：段/分配器/碎片/GC Pending 状态机）+异常体系（语义矩阵+帧记录+StackTrace）+string/array/泛型/委托 | 异常/泛型/GC 行为测试全过 |
| M4 | P/Invoke+Marshal 全套+icall 平台层+C API+四态产物 | C++ 调用库演示通过 |
| M5 | BCL 全量转译，L1 裁剪为默认管线（子代理主战场，按命名空间族分批） | BCL 行为测试逐步全绿 |
| M5.5 | 裁剪分级验证：L1 闭合证明+双模式测试；L2 按需开放 | 裁剪开/关测试一致 |
| M6 | 测试套件全绿对齐+双编译器 CI+文档 | 验收完成 |
| M7（可选） | 解释器兜底（元数据驱动执行未预生成泛型实例的 IL，非 JIT） | 动态泛型场景通过 |
| M8（远期） | 完整 CLDR 全球化（数据内嵌+算法自研） / COM 互操作（RCW/CCW/IDispatch/HRESULT↔异常） | 对应域测试通过 |
| M6.5 | DLR：CallSite 静态转译 + 自定义绑定器（元数据查表 + Invoker，零 Emit 零 JIT） | dynamic 测试通过 |

## 10. 24 子代理模块划分（共享契约 = 运行时头文件，M1-M4 定稿后放开）

| 域 | 数 | 任务 |
|---|---|---|
| 转译器 | 5 | 元数据层 / IR 折叠 / 类型代码生成 / 方法代码生成 / 泛型+异常+分析器 |
| 运行时核心 | 8 | 对象模型+类型表 / GC-根扫描+标记清除 / GC-分配器+段+碎片 / GC-分代+卡表+压缩 / 异常载体+展开 / 栈帧记录+StackTrace / string+char / array+委托+装箱 |
| BCL 族 | 9 | Collections / IO+Text / Threading / Reflection / InteropServices(PInvoke+Marshal) / Globalization / Net / Xml / Numerics+Math |
| 平台层+测试 | 3 | icall 平台层 / PInvoke marshalling / 测试框架+双跑对比 |

每子代理产出 = 可编译单元 + 自带测试；主线程集成验证。

## 11. 硬性约束清单（不可违反）
0. **C++ 标准 = C++17 仅**：运行时与生成代码全部 C++17（cxx_std_17），**禁止 C++20/23 及以上特性**（concepts/ranges/coroutines/modules/三路比较/设计空间 等一律禁用），保证跨编译器/跨环境最大兼容
1. 零 JIT：全链路纯 AOT，无 mmap 可执行内存/动态机器码生成路径
2. 静态编译：泛型编译期展开、vtable/类型表编译期生成、元数据只读内嵌
3. 零第三方依赖：GC/元数据/string/集合全部自研；产物仅依赖 OS 固有 API
4. 异常跨 C 边界必须防护层转换（C++ 异常不得穿过 extern "C"）
5. 禁内联/禁尾调用（栈帧语义完整性）
6. 转译方法与类型命名确定性编码，杜绝符号冲突
7. 反射看到的必须与编译时真实一致（元数据同源同构）
8. **引用语义 = 裸 GC 指针（对齐 .NET CLR 原生分代 GC 语义 + Go 风格低延迟追踪式 GC）**：.NET 引用严禁转译为 std::shared_ptr/引用计数/手写 free（原子计数开销、循环引用泄漏、终结器时序失真、GC 不可控）。生命周期由分代追踪式 GC 统一管理（Gen0/1/2 + LOH + 卡表 + 写屏障 + 压缩，对齐 CLR 语义），后台并发标记为性能演进项（M3 后）
9. **内存零泄露 + 资源访问安全（硬性底线）**：① 运行时 init/shutdown 全对称（可反复循环，库形态加载/卸载零残留），内部 C++ 资源全量 RAII（含错误路径），icall/Marshal 原生内存登记 + 托管侧确定性释放；② 转译代码编译期插入空引用检查（→NullReferenceException）与边界检查（→IndexOutOfRangeException），对齐 .NET 语义；③ 开发期 ASan/UBSan 双编译器全跑 + 泄漏检测器（分配簿记+退出报告）+ 泄漏测试专项（init/shutdown 循环零残留断言、长期压力 RSS 稳定）
10. **程序不裸崩（崩溃防护 = 编译器生成检查，不引入系统防崩溃）**：① 崩溃预防走正路——转译代码编译期插入 null 检查（→NullReferenceException）、边界检查（→IndexOutOfRangeException）、栈底哨兵（→StackOverflowException，4.8 语义），全部托管异常语义、编译器生成，非系统钩子；② 未处理异常 → 顶层语言级 catch-all（main/导出 thunk 内 try/catch）→ stderr 打印异常+完整托管 StackTrace+非零退出码（对齐 .NET 控制台行为）；③ 访问违例/堆损坏（真 bug 或灾难态）→ **不拦截**，OS 默认崩溃处理，崩溃 = 真 bug 的可诊断信号（保证：正确程序不崩，因编译期检查兜底）；④ **明确排除**：VEH/SEH 挂钩、崩溃捕获/转储库（crashpad/breakpad/minidump）、任何系统异常链干预；⑤ 异常路径无泄露保证：C++ 展开器保证 RAII 析构必执行（与是否抛异常无关），配合全 RAII 铁律（约束 #9），try/catch 路径零泄露；⑥ 诊断信息完整：MethodInfo 编译期固化类型名.方法名(签名) + IL SequencePoint 源文件名/行号，异常输出 `at Foo.Bar() in foo.cs:line 42`（与 .NET 格式一致）

## 12. 风险与边界
- 工程量巨大（BCL 全转译 + 分代精确 GC + 异常语义矩阵）→ 里程碑递进，逐步验证
- 动态边界：新程序集加载/新类型创建不可行；未预见泛型实例走解释器兜底（M7）；**System.Reflection.Emit 不支持**（见需求决策 #18）
- 反射元数据体积（数十 MB 级）→ 按需裁剪优化（L1/L2 分级）
- BCL 全转译的边角行为（遗留 API、文化/全球化）→ 测试套件驱动渐进覆盖

## 12.5 .NET 深水区已设计项（补齐核查）
- **静态构造函数（cctor）**：beforefieldinit 触发时机 + 每类型一次线程安全守卫（双检锁）+ 重入语义，转译器专门生成
- **终结器/弱引用/GCHandle**：终结器线程、f-reachable 队列、"终结器对象两次回收才释放"晋升语义、弱引用表
- **Nullable\<T\> 装箱**：有值装箱为值、无值装箱为 null
- **decimal**：128 位格式（96 位整数 + 比例因子 + 符号），IConvertible 全套
- **内存模型**：volatile/Interlocked → C++ 原子映射（memory order 对齐 .NET 承诺语义）
- **string.GetHashCode 随机化**：.NET Framework 随机 seed，跨进程不保证一致 → 测试套件不得依赖跨进程 hash 相等
- **GC 手动接口**：GC.Collect/WaitForPendingFinalizers/SuppressFinalize/LOH 阈值（85000 字节）

## 12.6 明确不支持清单（诚实边界）
- 多 AppDomain（单 AppDomain 语义，AppDomain.CurrentDomain 单例）
- .NET Remoting（已废弃）
- Assembly.Load(byte[]) 运行时动态加载程序集
- 托管调试器/分析器接口（调试 = C++ 调试器 + 转译符号映射）
- 语法糖类（lambda/闭包/yield/async 状态机/属性/事件/运算符重载/ref-out/unsafe/enum/Regex 解释模式）均为普通方法/类转译，无风险
- **无栈协程唯一模式**：.NET async/await = 编译器无栈状态机（状态机类 + MoveNext + Task 延续），全转译后天然支持，跑在真 C++ 栈上。**明确不引入有栈协程库（boost.context/make_fcontext/jump_fcontext 等）**：① 破坏三合一机制（协程私有栈帧无法入线程托管帧记录栈 → 精确根扫描断链/StackTrace 断裂/安全点失效）；② 违反零第三方铁律；③ .NET 无此语义，偏离对齐目标。M7 解释器如需独立执行栈用自研实现
