# 反射支持现状审计报告（模式A 全量编译路径）

- 审计日期：2026-08-03
- 审计性质：只读审计（未修改任何代码、未 git commit）
- 审计范围：转译器 src/il2cppc/**（Ir/StackFold.cs、Emit/MethodRefResolver.cs、Emit/CodeGenerator.cs、
  Emit/TypeEmitter.cs、Emit/VTablePlanner.cs、Il/IlDecoder.cs、Program.cs、Model/*）与运行时
  src/runtime/**（src/typecheck.cpp、src/metadata.cpp、include/internal/il2cpp_metadata.h、
  include/internal/il2cpp_typeext.h）
- 审计标准：docs/REQUIREMENTS.md 铁律 2m（反射裁剪绝不可废：模式A 全量 / 模式B 用户清单 /
  静态可知反射自动分析）+ 铁律 2g-2（反射 = 完整运行时操作：Invoke/SetValue/GetValue）
- 参考设计：docs/architecture/路线图.md、jit-预留.md（#14 反射元数据表、#15 统一 Invoker）、
  spec 2026-08-02-il2cpp-design.md §6.3

---

## 1. 支持程度矩阵

| 能力 | 支持度 | 位置 | 机制说明 |
|---|---|---|---|
| `typeof(T)`（ldtoken + GetTypeFromHandle） | ❌ | IlDecoder.cs:186-187；StackFold.cs:677-678 | Ldtoken 被**误分类为 FieldToken**（解码层），StackFold 无 case → default 抛 `NotSupportedException`。GetTypeFromHandle 在 resolver 无接线（BclResolveCore 30 域无 "System.Type" 域，MethodRefResolver.cs:222-257） |
| `obj.GetType()` | 部分✅ | MethodRefResolver.cs:669-670 → `icall_object_get_type`；runtime typecheck.cpp:220-233 | 返回**类型全名（托管字符串）**——简化 Type 模型（无托管 Type 对象）。结果经 objTypes 跟踪为 "System.Type"（StackFold.cs:940-941）；catch 变量条目上的 GetType 折叠为恒等（MethodRefResolver.cs:962-973） |
| `Type.Name` / MemberInfo::get_Name | 部分✅ | MethodRefResolver.cs:708-713（IdentityMarker）；StackFold.cs:916-923 | 仅 `System.Reflection.MemberInfo::get_Name` 且 receiver 被跟踪为 "System.Type" 时折叠为恒等（因为 GetType 返回的本身就是名字符串）。`Type.Name` 静态成员无接线 |
| `Type.GetTypeCode` | 部分✅ | MethodRefResolver.cs:703-704 → `icall_object_get_type_code`；runtime typecheck.cpp:181-215 | net48 经验值：Int32=9 / Boolean=3 / Char=4 / Double=14 / String=18，其余回退 Object=1（typecheckcase.cs:36-47 为对照） |
| `Type.GetType(string)` | ❌ | — | 无 "System.Type" 域；runtime 也无实现（仅 il2cpp_class_by_name 注册表存在，metadata.cpp:87-101） |
| `Type.GetTypeFromHandle` 等静态原语 | ❌（runtime 就绪） | il2cpp_typeext.h:55/75/92/106 已声明 4 个原语 | **runtime 侧已按前向 ABI 实现**（GetTypeFromHandle/IsAssignableFrom/GetTypeCode/FullName），头文件明注"transpiler has no typeof/GetTypeFromHandle emission yet"（il2cpp_typeext.h:14）——只差转译器接线 |
| `Type.GetMethod/GetField/GetProperty` | ❌ | — | 无元数据表、无字符串查找索引，resolver 无 System.Reflection 域 |
| `MethodInfo.Invoke` / `PropertyInfo.SetValue/GetValue` / `FieldInfo` | ❌ | — | 全转译器仅 DelegateInvokeExpr（委托 methodPtr 分派，Expr.cs:151-155）与 P/Invoke 两种"Invoke"，均与反射 Invoker 无关 |
| 虚表（callvirt 分派） | ✅ | VTablePlanner.cs 全量规划；CodeGenerator.cs:93-96 调用 PlanAll；TypeEmitter.cs:232-259 EmitVTable；CodeGenerator.cs:1112-1165 EmitVirtualCall | 基类优先槽布局、override 替换槽（MethodDef.cs:9-17/39）；class 分派 `recv->klass->vtable[slot]`（CodeGenerator.cs:1146）；BCL 实例方法走 icall 直连（StackFold.cs:926-952 IsRuntimeDirect） |
| 接口分派 | ✅ | TypeEmitter.cs:193-229 EmitInterfaceMaps；runtime metadata.cpp:124-170 | 每实现类型发射 iface→vtable 槽映射表；`il2cpp_interface_dispatch` 线性扫基类链（H7：继承接口）；接口槽冲突编译期报错（VTablePlanner.cs:89） |
| cctor 守卫 | ✅ | CodeGenerator.cs:141-164 发射；StackFold.cs:1480-1503 注入；StackFold.cs:1796-1946 循环提升；CodeGenerator.cs:2047-2053 entry 兜底 | beforefieldinit 惰性语义：文件作用域 `{T}_cctor_done` + `ensure_cctor_{T}()`（NOINLINE）；.cctor 内部自守卫消除；循环 preheader 提升（保守子集：仅循环体无可触发 cctor 的调用时）；entry 对 Main 类型显式 ensure 幂等 |
| 类型元数据（Il2CppClass） | 部分✅ | il2cpp_metadata.h:34-58；TypeEmitter.cs:149-190 EmitClassDescriptor；metadata.cpp:42-101 注册表 | 每类型静态描述符：name/instanceSize/refFieldBitmap/baseClass/vtable/interfaces；注册表固定 256 槽（overflow abort）；**无方法/字段/属性/特性元数据**（api.cpp:33 注"reflection metadata is a later milestone"；exception.cpp:12 注运行时不走元数据图） |

**汇总：7 项中 ✅2（虚表/接口分派、cctor 守卫）、部分✅2（GetType、类型描述符）、❌3（typeof、GetMethod/Invoke、字符串驱动反射）。**

---

## 2. 关键发现

### F1 — Ldtoken 解码误分类（typeof 支持的第一道障碍）
`IlDecoder.cs:186-187` 把 Ldtoken 归入 `IlOperandKind.FieldToken` 组。Ldtoken 的 token 实际可为
TypeDefOrRef / FieldDef / MethodDef 三种（`typeof(X)` = 类型 token；`ldtoken` 字段 = 字段 token）。
当前分类会导致后续按字段语义解析，即使 StackFold 补 case 也会拿错 handle kind。
修复：Ldtoken 独立为一种 operand kind，运行时再按 handle.Kind 分流。

### F2 — 简化的 Type 模型：GetType() 返回"类型名字符串"而非 Type 对象
`typecheck.cpp:220-233`（GetType）、il2cpp_typeext.h:13-23（Type 静态原语）统一约定：
**"A Type" = 类型全名的托管字符串**。这使 `GetType().Name` 折叠为恒等（StackFold.cs:916-923），
零反射开销；但对 Invoke/GetMethod 这类需要"Type 句柄"的反射操作，字符串表示不携带
`Il2CppClass*`，未来要么升级为"名字符串 + 隐藏描述符指针"双表示，要么引入真正的 Type 对象。

### F3 — 转译器/运行时不对称：runtime 已就绪，resolver 未接线
il2cpp_typeext.h 的 4 个原语（GetTypeFromHandle/IsAssignableFrom/GetTypeCode/FullName）与
metadata.cpp 注册表（il2cpp_class_by_name）都是按 typeof 落地的前向准备，但 MethodRefResolver
的 BclResolveCore switch（MethodRefResolver.cs:222-257，30 个域）没有 "System.Type" 域，
StackFold 没有 Ldtoken case。**typeof 落地 = 纯转译器侧工作（约 2 处修改 + 1 个新域），runtime 零改动。**

---

## 3. 模式A（全量编译）已具备条件分析

### 已具备
1. **类型描述符全覆盖**：每转译类型发射静态 `{Type}_class`（TypeEmitter.cs:149-190），含全名/
   基类链/接口表/虚表——`typeof` 所需的"句柄"基础设施已存在，`&{Type}_class` 可直接作为
   GetTypeFromHandle 的实参（与 il2cpp_typeext.h 的 Il2CppClass* ABI 完全吻合）。
2. **运行时查找**：il2cpp_class_by_name（metadata.cpp:87-101）支持字符串→描述符。
3. **编译期素材**：token→全名解析（StackFold.cs:1390-1416 TypeFullNameFromToken/CtorTypeFullName）、
   方法签名哈希（SigHash）、属性 accessor 形态分析（AccessorAnalyzer.cs:35-39）——生成
   方法/字段/属性描述符表所需的解析管线已全部存在。
4. **cctor 语义**：反射路径实例化类型时 beforefieldinit 保证已由 ensure_cctor_* 机制覆盖。
5. **虚表/接口表**：动态分派基础设施完整，统一 Invoker 可复用 vtable 槽函数指针。

### 缺口（按依赖顺序）
| # | 缺口 | 说明 |
|---|---|---|
| 1 | Ldtoken 折叠 | 修 IlDecoder.cs:186-187 误分类 + StackFold 新增 TypeConstExpr（携带 `&{Type}_class` + 全名） |
| 2 | "System.Type" 域 | MethodRefResolver 新增 TypeDispatch：GetTypeFromHandle/GetType(string)/GetTypeCode/FullName/Name/IsAssignableFrom → 复用 il2cpp_typeext.h 既有原语（F3） |
| 3 | 反射元数据表生成 | 每类型 static const 方法/字段/属性描述符数组（名称、签名 key、vtable 槽或函数指针、字段偏移/引用位）；素材见缺口清单"已具备 3"，需新增发射器 + C++ 侧结构定义 |
| 4 | 按名索引 | GetMethod/GetField 的字符串查找：每类型哈希表或线性数组（模式B 的入口点） |
| 5 | 统一 Invoker 调度器 | 方法句柄（描述符指针）+ 参数数组 marshalling（box/unbox、ref 槽）→ vtable 槽/直调/icall 三类目标统一入口；铁律 2g-2 要求的 Invoke/SetValue/GetValue 全依赖此 |
| 6 | MethodInfo/PropertyInfo/FieldInfo 对象模型 | 当前 Type = 名字符串（F2），需句柄化表示才能承载 Invoke 语义 |

结论：**模式A 的"类型侧"已 90% 具备（描述符/虚表/cctor），缺口集中在"成员侧"（元数据表 +
Invoker + 信息对象）——恰是 jit-预留.md #14/#15 与 REQUIREMENTS.md 铁律 2m 第 6 条标注的
M5.5+ 落地范围。**

---

## 4. 模式B（用户反射清单）接口建议

当前 CLI 形状：`il2cppc <input-assembly> <output-dir>`（Program.cs:12-14，args.Length != 2
即打印 usage）。建议扩展为：

```
il2cppc --reflection-list <manifest> [--reflection-granularity all|members]
        <input-assembly> <output-dir>
```

- `--reflection-list <manifest>`：清单文件路径；缺省 = 纯静态模式（现状）。
- `--reflection-granularity all|members`（缺省 all）：全量 = 类型及其全部成员发射元数据；
  `members` 对应铁律 2m 第 5 条"按需粒度"（如只反射属性则仅发射属性描述符，字段侧跳过）。
- 清单格式（行式，`#` 注释，与运行时代码注释同用 ASCII）：
  ```
  # 反射保留清单
  Calc.Person                # 整类型（含成员）
  Calc.Person::get_Name      # 单成员（签名 key 复用 MethodSignatureCoder 编码）
  Calc.Person::#ctor
  ```
- 实现落点：CodeGenerator.Generate 之前读入 → 与现有"已知类型全量编译"合并为保留集：
  命中类型保证发射完整描述符 + 成员表；未命中类型仍按既有规则（静态可达）编译但不发射
  反射元数据——与 spec §5 保留集（静态可达图 ∪ 动态保留清单）一致。
- 与模式C 的关系：模式C 自动分析产出的 typeof 常量自动归入保留集，二者互补、同属
  编译期决策（铁律 2e：编译期定死，运行时只查表）。

---

## 5. 模式C（静态可知反射自动分析）可行性结论

**结论：✅ 可行，且是三种模式中成本最低的一条——现有 IR 分析已具备全部基础。**

| 静态反射形态 | 编译产物 | 现状基础 |
|---|---|---|
| `typeof(X)` | `ldtoken X; call Type::GetTypeFromHandle` → 折叠为 `&{X}_class`（+ 名字符串） | token→全名解析已存在（StackFold.cs:1390-1416）；描述符句柄已存在（TypeEmitter.cs:167）；**缺**：Ldtoken case + Type 域接线（缺口 1/2） |
| `nameof(X)` | `ldstr "X"` | ✅ 天然支持（Ldstr → StringConstExpr，StackFold.cs:196-198） |
| `obj.GetType()` 常量化 | 对象经 newobj/跟踪后 GetType 折叠为类型全名 | objTypes 类型跟踪已存在（StackFold.cs:895-903 读取、940-943 记录）——`new Foo().GetType()` 路径已可折叠，无需新机制 |
| 直接成员访问（`Type.GetMethod` 前的属性/字段直访） | 属性折叠（AccessorAnalyzer）+ 直调解析 | ✅ 已实现（属性折叠设计.md §1） |

局限（必须由模式A/B 兜底，不可静态推断的形态）：
- `Type.GetType("字符串")`、`GetMethod("名字")` —— 字符串驱动，编译期不可定；
- GetType() 经参数传递/虚分派后的运行时类型 —— objTypes 跟踪断链（StackFold.cs:895-903
  只跟踪 LocExpr/StringConst/ExceptionEntry 三种接收者形态）；
- Invoke 的动态目标 —— 需统一 Invoker（模式A 缺口 5）。

落地建议：在 StackFold 中识别 `ldtoken; call Type::GetTypeFromHandle` 指令对并折叠（常量
折叠基础设施现成），同时把折叠出的类型名自动登记进保留集（天然支持铁律 2m 第 4 条）。
typecheckcase.cs:46-47 已有 `Type.GetTypeCode(typeof(...))` 的 net48 对照用例可直接复用验证。

---

## 6. 证据索引

| 主题 | 证据 |
|---|---|
| Ldtoken 解码 | READ:src/il2cppc/Il/IlDecoder.cs:186-187（归入 FieldToken 组） |
| Ldtoken 折叠缺失 | READ:src/il2cppc/Ir/StackFold.cs:677-678（default → NotSupportedException） |
| GetType icall 接线 | READ:src/il2cppc/Emit/MethodRefResolver.cs:669-670 |
| GetType 返回名字符串 | READ:src/runtime/src/typecheck.cpp:220-233 |
| GetType 结果类型跟踪 | READ:src/il2cppc/Ir/StackFold.cs:937-943 |
| get_Name 恒等折叠 | READ:src/il2cppc/Emit/MethodRefResolver.cs:708-713；StackFold.cs:916-923 |
| GetTypeCode 接线 | READ:src/il2cppc/Emit/MethodRefResolver.cs:703-704；typecheck.cpp:181-215 |
| Type 静态原语（runtime 就绪未接线） | READ:src/runtime/include/internal/il2cpp_typeext.h:14,55,75,92,106 |
| 无 System.Type 域 | READ:src/il2cppc/Emit/MethodRefResolver.cs:222-257（30 域清单） |
| 无 MethodInfo/Invoke 接线 | BASH:grep MethodInfo/Invoke → 77 匹配全为 DelegateInvokeExpr/P-Invoke |
| 虚表规划与发射 | READ:CodeGenerator.cs:93-96；TypeEmitter.cs:232-259；VTablePlanner.cs |
| 虚表分派 | READ:CodeGenerator.cs:1112-1165（vtable[slot]，接口走 il2cpp_interface_dispatch） |
| 接口分派运行时 | READ:src/runtime/src/metadata.cpp:124-170 |
| cctor 守卫 | READ:CodeGenerator.cs:141-164；StackFold.cs:1480-1503,1796-1946；CodeGenerator.cs:2047-2053 |
| 类型描述符结构 | READ:src/runtime/include/internal/il2cpp_metadata.h:34-58 |
| 注册表 | READ:src/runtime/src/metadata.cpp:42-101 |
| 描述符发射 | READ:src/il2cppc/Emit/TypeEmitter.cs:149-190 |
| CLI 现状 | READ:src/il2cppc/Program.cs:12-14 |
| 铁律 2m 模式定义 | READ:docs/REQUIREMENTS.md:99-106 |
| 铁律 2g-2 反射能力 | READ:docs/REQUIREMENTS.md:150-160 |
| 类型注册表 M2 简化注记 | READ:src/runtime/src/metadata.cpp:13-14（"Full reflection … M3"） |
