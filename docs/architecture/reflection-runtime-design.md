# M5.5 反射运行时设计（反射元数据表 + 统一 Invoker + 三模式裁剪）

> 依据：docs/REQUIREMENTS.md 铁律 2g-2（反射 = 完整运行时操作能力：Invoke/SetValue/
> GetValue/CreateInstance，零 JIT）、铁律 2m（反射三模式：A 全量编译 / B 用户清单 /
> C 静态可知自动分析；落地 M5.5+）、铁律 2j（设计不得堵死 JIT 路径）。
> 关联：docs/architecture/jit-预留.md #14/#15（反射元数据表 + 统一 Invoker 为【未来新增】，
> 本文档即为该两项的 M5.5 落地设计）；docs/architecture/路线图.md 阶段 A 出口判据①
> （裁剪 + 反射全链路可用）。
>
> 范围：**设计文档，不实现**。审计基线：M5 BCL icall 并行域进行中（.opencode/plan.md）。
> 与 docs/audit/reflection-audit.md（若有，并行代理产出）互补：audit 描述现状差距，
> 本文档给出目标设计。

---

## 0. 现状基线（审计证据）

| 项 | 现状 | 证据 |
|---|---|---|
| 类型描述符 | `Il2CppClass{name, instanceSize, refFieldBitmap, refFieldCount, baseClass, vtable, vtableSize, interfaces, interfaceCount}`，每类型静态实例，entry.cpp 统一注册 | src/runtime/include/internal/il2cpp_metadata.h:34-58；TypeEmitter.cs:159-190；CodeGenerator.cs:2045 |
| 类型查找 | `il2cpp_class_by_name`（线性表，kMaxRegisteredClasses=256） | il2cpp_runtime.h:47；jit-预留.md #11 |
| 反射类 icall | `icall_object_get_type` 返回类型**名字字符串**（非 Type 对象）；`MemberInfo::get_Name` 在 System.Type 上折叠为 identity | il2cpp_typecheck.h:115-127；MethodRefResolver.cs:669-670, 710-712 |
| typeof 落地 | **未实现**：`Ldtoken` 在 IlDecoder 解码为 FieldToken（IlDecoder.cs:186-187），StackFold 仅有 ldfld/stfld/ldflda 分支，无 Ldtoken case → 落入 NotSupportedException | StackFold.cs:285-329（对照），grep 无 Ldtoken 处理 |
| 方法函数指针 | 直接调用 = 编译期符号 `{Type}_{Name}_{SigHash}`；导出表 `ExportEntry{typeName, methodName, fn}` 已有按名查指针先例 | jit-预留.md #8；api.cpp:40-49 |
| 签名编码 | `MethodSignatureCoder` 编码串（`i`/`V:System.String`/`r:...`），运行时与转译器共用约定 | jit-预留.md 2.5.3 |
| 帧栈 | framestack.cpp 帧记录 API 与方法无关（方法名/类型名传参） | jit-预留.md #20 |

---

## 1. 运行时反射 API 面（M5.5 落地形态）

全部以 icall 形态接线（MethodRefResolver 特判域），返回值为托管对象（GC 管理）。

| API | AOT 落地形态 | 运行时实现 |
|---|---|---|
| `typeof(T)` | **编译期折叠**：`ldtoken T` → 常量 `&{T}_class`（RuntimeTypeHandle = Il2CppClass* 包装）；`Type.GetTypeFromHandle` → 包装 icall（identity 或轻量包装） | `icall_type_from_handle(Il2CppClass*)` → 托管 Type 对象 |
| `Type.GetType(string)` | 字符串驱动（模式 B/C 关键入口） | `icall_type_get_type(Il2CppString*)`：查类型注册表（复用 `il2cpp_class_by_name` 语义）+ TypeInfo 校验；未生成表 → 返回 null（.NET 语义）或 TypeLoadException |
| `GetProperties()` / `GetProperty(name)` | 查该类型 TypeInfo.properties 表 | `icall_type_get_properties(Il2CppClass*, nameOrNull)` → PropertyInfo 对象数组/单个 |
| `GetMethods()` / `GetMethod(name)` | 查 TypeInfo.methods 表 | `icall_type_get_methods(...)` → MethodInfo 数组/单个（BindingFlags 过滤简化版） |
| `GetField(name)` | 查 TypeInfo.fields 表 | `icall_type_get_field(...)` → FieldInfo |
| `MethodInfo.Invoke(instance, args)` | 查方法表 fnPtr → 统一 Invoker | `icall_method_invoke(MethodInfo*, instance, ObjectArray args)` → 结果装箱 |
| `Activator.CreateInstance(Type)` | 查方法表中标志为 ctor 且零参的条目 → Invoker | `icall_activator_create(Il2CppClass*)` → 实例 |
| `FieldInfo.GetValue/SetValue` | 直接按 offset/staticAddr 读写 + box/unbox | 复用 boxing 域 icall（boxing.cpp） |
| `PropertyInfo.GetValue/SetValue` | 经 getter/setter 方法名查方法表 → Invoker | 同上（含索引器参数） |

命名约束（铁律 2m）：新 icall 一律 `il2cpp_`/`icall_` 前缀、`namespace il2cpp`，
不得以 "il2cpp" 命名风格命名新符号（重命名是独立工程任务，本设计按现有契约延续）。

---

## 2. 元数据表设计

### 2.1 C 结构体草案（新头 `src/runtime/include/internal/il2cpp_typeinfo.h` 风格）

`Il2CppClass` 保持**只增不改**（jit-预留.md #1 演进要求：布局兼容），TypeInfo 作为
并列结构，通过注册表关联（`Il2CppClass*` 是天然 join key——`name` 全名唯一）。

```cpp
#pragma once
//------------------------------------------------------------------------------
// il2cpp_typeinfo.h -- M5.5 reflection metadata tables (static const, read-only).
//
// One Il2CppTypeInfo is emitted per REFLECTED type (mode A/B/C decide the set)
// by the transpiler and registered alongside the type descriptor. The tables
// are the runtime's source of truth for reflection queries; direct-call code
// NEVER goes through them (performance, 铁律 2e).
//
// All tables are static const (read-only segment, 铁律 2d/2j design). Method
// fnPtr entries are the transpiler-emitted static functions (or their unified
// invoker wrappers, section 3) -- the same AOT code the compiler already
// emitted, never runtime-generated code (zero JIT, 铁律 2g-2).
//------------------------------------------------------------------------------

#include <cstdint>
#include "il2cpp_metadata.h"

namespace il2cpp {

// One reflected method. signature is the MethodSignatureCoder encoding
// ("i" / "V:System.String" / "r:Calc.Point" ...) so the runtime and the
// transpiler share one encoding (jit-预留.md 2.5.3).
struct Il2CppMethodInfo
{
    const char* name;          // method name, e.g. "Add"
    const char* signature;     // signature encoding (return + params)
    int32_t     paramCount;    // number of parameters (from signature)
    int32_t     flags;         // bit0 public, bit1 static, bit2 ctor, bit3 virtual
    int32_t     virtualSlot;   // vtable slot if virtual, else -1
    const char* declaringType; // declaring type full name (inherited methods
                               // are listed under THEIR declaring type's table)
    void*       fnPtr;         // AOT static function address (or invoker wrapper,
                               // section 3). null = runtime-unreachable method
                               // (excluded by trimming).
};

// One reflected field. Instance fields: offset + isRef (same convention as
// Il2CppClass.refFieldBitmap). Static fields: offset = -1, staticAddr points
// at the generated static storage slot.
struct Il2CppFieldInfo
{
    const char* name;          // field name
    const char* typeName;      // field type full name (for box/unbox checks)
    int32_t     offset;        // instance: byte offset in the instance struct;
                               // static: -1
    int32_t     isRef;         // 1 = GC reference field
    void*       staticAddr;    // static: &StaticStorage slot; instance: null
    int32_t     flags;         // bit0 public, bit1 static, bit2 literal
};

// One reflected property. Accessors are resolved by name through the SAME
// methods table (single source of truth -- no duplicated fnPtrs).
struct Il2CppPropertyInfo
{
    const char* name;          // property name
    const char* typeName;      // property type full name
    const char* getterName;    // accessor method name, null if none
    const char* setterName;    // accessor method name, null if none
    int32_t     indexParams;   // indexer parameter count (0 = non-indexed)
};

// Reflection metadata for one type. klass is the EXISTING runtime descriptor
// (vtable/GC/baseClass are already there -- never duplicated).
struct Il2CppTypeInfo
{
    const char*          name;          // full type name == klass->name
    const Il2CppClass*   klass;         // existing runtime descriptor
    const Il2CppTypeInfo* baseType;     // base type info, null for System.Object
    const Il2CppTypeInfo* const* interfaces; // implemented interface infos
    int32_t              interfaceCount;
    const Il2CppFieldInfo*    fields;   // field table, null = none
    int32_t              fieldCount;
    const Il2CppMethodInfo*   methods;  // method table, null = none
    int32_t              methodCount;
    const Il2CppPropertyInfo* properties; // property table, null = none
    int32_t              propertyCount;
};

// Runtime side: the lookup API (mirrors il2cpp_class_by_name).
const Il2CppTypeInfo* il2cpp_typeinfo_by_name(const char* name);

} // namespace il2cpp
```

要点：

- **不重复**：实例布局/GC 位图/基类指针/接口映射已在 `Il2CppClass` 中，TypeInfo
  只存反射查询需要而现有描述符没有的信息（方法表/字段名+偏移/属性表/签名）。
  `baseType`/`interfaces` 与 `klass->baseClass`/`klass->interfaces` 同构，但用
  TypeInfo 指针对接反射链（Type.BaseType 查询免去表↔类来回跳）。
- **单权威**：属性 getter/setter 只存**方法名**，经方法表解析 fnPtr——不允许
  属性表携带第二份函数指针（D1 单一权威写入者，与 jit-预留.md 2.4 不变量一致）。
- **只读段**：全部 static const，编译期生成（元数据静态固化，铁律 2d/2j）。
- **GC 可见性**：TypeInfo 全部是裸指针+常量（无托管引用字段），GC 无需感知；
  反射返回的 MethodInfo/PropertyInfo/FieldInfo 是运行时构造的托管包装对象。

### 2.2 转译器生成规则（哪个 pass）

| 产出 | 生成位置（pass） | 数据来源 |
|---|---|---|
| `{Type}_typeinfo` 静态表 + 各子表数组 | **TypeEmitter**（与 `EmitClassDescriptor` 同 pass，每类型 .cpp 内，TypeEmitter.cs:159-190 同款 CppWriter 发射） | `TypeDef`（MetadataLoader.cs）：fields/methods/properties/BaseType/InterfaceSlotMaps；签名走 `MethodSignatureCoder` |
| `invoker_{Type}_{Name}_{hash}` 包装函数 | **MethodEmitter.EmitMethod**（与真实方法同 pass，同文件发射） | 方法 ParamTypes/ReturnType（MethodDef） |
| `il2cpp_typeinfo_register(&{Type}_typeinfo)` | **CodeGenerator entry.cpp pass**（CodeGenerator.cs:2003-2076：`il2cpp_entry_main` 内，紧邻 `il2cpp_class_register` 行 2045 追加） | 反射保留集（模式 A/B/C 决定，见第 4 节） |
| 方法 fnPtr 填值 | TypeEmitter 发射时取编译期符号 `&{Type}_{Name}_{SigHash}`（MethodRefResolver.cs:52-55 同款命名） | MethodEmitter 符号表 |

执行顺序约束：模式 C 需要在 StackFold **之前**做 IL 预扫描（收集 typeof 常量），
与 `AccessorAnalyzer` 的预扫描模式同构（AccessorAnalyzer.cs:61 先例）。

---

## 3. 调用调度（反射 Invoke → AOT 静态函数）

### 3.1 统一签名方案（推荐先落地）

```
统一 ABI：Il2CppObject* invoker(void* fn, void* argbuf, int32_t argc)
```

- 每反射方法生成一个**薄包装** `invoker_{Type}_{Name}_{hash}(void* argbuf, int32_t argc)`：
  按方法签名从 argbuf 解包参数（值类型直接拷贝、引用类型取指针、this 为第 0 个
  槽）→ 调用真实静态函数 `{Type}_{Name}_{hash}` → 返回值装箱为 `Il2CppObject*`。
- 函数指针表 = `Il2CppMethodInfo.fnPtr`（统一 `void*` 槽）。
- 运行时 `icall_method_invoke`：方法句柄 → fnPtr → 构造 argbuf（对象数组
  marshalling，box/unbox 复用 boxing.cpp）→ 调用 → 结果 unbox 或直接返回。

**优点**：函数指针表类型统一（void*，无 UB cast）；运行时零签名分发逻辑；
错误（参数个数/类型不匹配）在包装内按签名编码检查，可精确抛 ArgumentException。

**缺点**：每个反射方法多一层包装调用 + 参数打包；反射路径比类型化方案慢
（数量级：一次间接调用 + 逐参搬运，官方 JIT 反射同样付 marshalling 成本）。

### 3.2 类型化签名方案（性能演进）

`Il2CppMethodInfo.fnPtr` 直接存真实签名函数指针（编译期类型安全），运行时按
signature 编码解释 argbuf。**缺点**：运行时需要完整签名解释器（编码 → 解包/装箱
逻辑），且 fnPtr 槽类型无法统一（需要 union 或 void* + 编码双保险）——复杂度
全部转移给运行时，生成量虽小但正确性风险高。

### 3.3 取舍结论

| 维度 | 统一签名（推荐） | 类型化签名 |
|---|---|---|
| 生成量 | 每反射方法 1 薄包装（~15 行） | 零额外生成 |
| 运行时复杂度 | 低（查表+调用） | 高（签名解释器） |
| 性能 | 慢（包装+打包） | 快（直调） |
| 风险 | 低 | 中（UB cast / 解释器 bug） |
| JIT 演进 | jit-预留 #15 统一 Invoker ABI 直接复用 | 同左 |

**推荐：统一签名先落地**（M5.5）。性能约束（铁律 2e）不受影响——反射路径
非热路径，且与官方 JIT 的反射 marshalling 成本同数量级；直接调用保持符号调用
（铁律 2e 调用点策略，jit-预留.md 2.4）。类型化签名作为反射基准数据驱动的
性能演进项（M6 基准体系）。

---

## 4. 模式 A/B/C 落地差异（铁律 2m）

三模式只改变**转译器生成 TypeInfo 表的范围**，运行时 API 零差别（一个运行时，
三套编译策略——这正是 2m"多种编译模式"的落地形态）。

| 模式 | 生成规则 | 成本 | 未覆盖类型的 GetType 行为 |
|---|---|---|---|
| A 全量编译 | 转译器对**全部**类型生成 TypeInfo（含全部方法表；按需粒度：可裁属性表/字段表子集，2m 第 5 点） | 编译时间 + 表体积最大（L1 裁剪默认下仍可能 MB 级） | 无未覆盖（完整） |
| B 用户清单 | 用户提供清单文件（如 `reflection.txt`，每行一个全名）→ 只对清单类型生成表；清单外类型方法表 fnPtr=null | 表体积最小；需用户维护 | `Type.GetType(name)` 返回 null（.NET 语义，不带 throw）；GetMethod/Invoke 抛 TypeLoadException 路径 |
| C 自动分析（默认） | 转译器 IL 预扫描收集：① `ldtoken T` + GetTypeFromHandle（typeof 常量）；② `Type.GetType(ldstr "…")` 的字符串常量；③ nameof（编译期已折叠为字符串常量，无需特殊处理）。命中类型 → 生成表 | 体积 ≈ 实际反射面；扫描 pass 一次全量 IL 遍历 | 分析不到的字符串 GetType → 编译期告警（要求用户转模式 B 或改用 typeof） |

合并规则（2m 第 5 点"按需粒度"贯穿三种模式）：

- 模式 C 默认开启，模式 B 清单与之**合并**（保守并集）；模式 A = 全集（等价于
  对全部类型跑 B）。
- 按需粒度：生成方法表时，静态可达 + 清单命中的成员全留；属性表可只在
  GetProperties 类查询被调用时才全量生成（保守：模式 C 下对 typeof 命中的类型
  生成完整属性表——属性表体积远小于方法表，先全留，后续按 2m 第 5 点细化）。
- 泛型：`typeof(List<>)` 生成泛型定义条目；`typeof(List<int>)` 生成已实例化
  条目（对齐 spec 4.4 共享代码机制：引用类型实参共享描述符）。

成本对比（估算量级，M5 已 L1 裁剪）：A ≈ 全部类型；C ≈ 用户实际 typeof/GetType
面（典型程序 10-100 个类型）；B ≤ C 且用户可控。

---

## 5. 与现有类型系统的衔接

### 5.1 类型检查域（il2cpp_typecheck.h）

- `Il2CppClass` 不动（只增不改）；TypeInfo 经注册表以 `name` 关联（与
  `il2cpp_class_by_name` 同键）。`icall_type_equals`/`icall_type_is_subclass_of`
  （il2cpp_typecheck.h:83-94）继续以 `Il2CppClass*` 工作——Type 对象的
  baseType 链查询改走 TypeInfo.baseType（反射语义），isinst/castclass 走既有
  klass 链（性能路径零改动）。
- `icall_object_get_type`（返回名字字符串）**保留**作为 object.GetType().Name
  的快速路径（M5 接线现状，MethodRefResolver.cs:670），M5.5 新增并行的
  `icall_type_from_handle`（typeof 路径返回托管 Type 对象）。两路径不冲突：
  名字字符串 = Type.ToString() 的折叠，Type 对象 = 完整反射句柄。

### 5.2 typeof 落地（现状缺口 → 目标）

现状：`Ldtoken` 解码为 `IlOperandKind.FieldToken`（IlDecoder.cs:186-187），
StackFold 无 case → NotSupportedException。

M5.5 改造（转译器侧，三处）：
1. **IlDecoder**：`Ldtoken` 按 token 种类分流——TypeDefOrRef 类 token →
   `IlOperandKind.TypeToken`（复用既有 TypeToken 分支线）；Field/Method token
   保持 FieldToken/MethodToken（M5.5 暂不支持，保守报错）。
2. **StackFold**：新增 `LdtokenExpr { TypeFullName }`（TypeToken case）：
   `ldtoken T` 推入类型句柄常量；`call Type::GetTypeFromHandle` 识别后折叠为
   TypeExpr。此即模式 C 的收集点（同一 pass 记录 `typeofSet.Add(T)`）。
3. **CodeGenerator**：发射 `&{Type}_class`（Il2CppClass* 即 RuntimeTypeHandle
   的运行时形态，与 TypeEmitter.cs:175-177 基类引用同款取址语法）。

### 5.3 注册与查找

- 注册：entry.cpp pass 在 `il2cpp_class_register`（CodeGenerator.cs:2045）旁追加
  `il2cpp_typeinfo_register`；运行时旁路表（同 kMax 常量 + fail-loud 模式，
  jit-预留.md #11 容量扩容一并处理——建议直接扩到配置化容量）。
- 查找：`il2cpp_typeinfo_by_name` 与 `il2cpp_class_by_name` 同构。
- 帧栈：Invoke 调用的方法照常进帧记录（framestack.cpp 与方法无关，jit-预留.md
  #20）——反射路径自动获得异常/StackTrace 支持，零改动。
- GC：Invoke 参数 marshalling 期间的临时引用根复用 `WrapFrameRoots` 机制
  （CodeGenerator.cs:274-297 帧根注册），包装内中间值全部走托管分配。

---

## 6. 风险与验收

### 6.1 风险

| 风险 | 缓解 |
|---|---|
| 元数据体积爆炸（模式 A / BCL） | L1 裁剪默认 + 模式 B/C 收敛 + 紧凑编码（jit-预留.md 2.6）；M5.5 只对保留集生成 |
| 统一 Invoker 慢于官方 JIT 反射 | 反射非热路径；M6 基准对比兜底；类型化签名作演进项 |
| 字符串驱动 GetType 分析不到（模式 C 漏） | 编译期告警 + 提示转模式 B（2m 明确要求不能废反射） |
| 并行代理冲突（MethodRefResolver.cs/StackFold.cs/TypeEmitter.cs 可能被改） | 实施前复查 mtime（.opencode/memory.md 在途 watch 规则） |
| Ldtoken 分流改坏既有字段 token 处理 | IlDecoder 只加分流分支不动 FieldToken 语义；e2e 全量双跑回归 |
| 静态字段 SetValue 与 .cctor 顺序 | 复用 EnsureCctor 机制（StackFold.cs:288 先例） |

### 6.2 验收用例（建议 e2e：tests/cases/reflectioncase，双跑 net48 参考一致）

1. **CreateInstance → GetType**：`object o = Activator.CreateInstance(typeof(Foo));`
   `o.GetType().Name == "Foo"`。
2. **GetField + SetValue/GetValue**：`typeof(Foo).GetField("x").SetValue(o, 42)`
   → 直接字段访问 `((Foo)o).x == 42`（铁律 2g-2：观察到程序行为变化）。
3. **GetMethod + Invoke**：`typeof(Foo).GetMethod("Add").Invoke(o, new object[]{1,2})`
   == 3，与直接调用 `o.Add(1,2)` 结果一致（含实例/静态/私有各一）。
4. **GetProperty + GetValue/SetValue**：属性访问器经反射读写与直接访问一致
   （含索引器一例）。
5. **Type.GetType 字符串驱动**（模式 B/C 差异验证）：清单/分析覆盖时返回类型，
   未覆盖时 null（模式 B）——编译期告警列表正确。
6. **质量门**：三编译器（MSVC/MinGW/WSL）零警告 + CI 全绿 +
   `dotnet test tests/unit` + runtime C++ 测试全过。

---

## 7. 实施顺序（M5.5 内，供 plan 引用）

1. 头文件 `il2cpp_typeinfo.h` + 运行时注册/查找（il2cpp_typeinfo.cpp）。
2. 转译器：Ldtoken 分流（IlDecoder）→ LdtokenExpr（StackFold）→ 发射
   `&{Type}_class`（CodeGenerator）；Type.GetTypeFromHandle 接线。
3. 模式 C 预扫描 pass（typeof 常量收集，AccessorAnalyzer 同构）+ TypeEmitter
   TypeInfo 生成 + entry.cpp 注册。
4. 统一 Invoker：per-方法薄包装（MethodEmitter）+ `icall_method_invoke` +
   GetField/GetProperties/GetMethods/Activator icalls（MethodRefResolver 接线）。
5. 模式 B 清单文件解析（合并进模式 C 扫描结果）。
6. 模式 A = 全部类型开关（编译期 flag）。
7. 验收：reflectioncase e2e 双跑 + 质量门。

**核心设计一句话**：每类型静态只读 TypeInfo 表（字段/方法/属性/基类/接口）由
TypeEmitter 与类型描述符同 pass 生成并注册，反射 Invoke 经"统一签名薄包装函数
指针表"映射到 AOT 静态函数，typeof 由转译器折叠为 `&{Type}_class` 常量——模式
A/B/C 只改表生成范围，运行时一套 API 零差别。
