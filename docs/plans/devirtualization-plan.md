# 去虚拟化（Devirtualization）调查报告 + 实施方案

- 日期：2026-08-09；分支 m1（HEAD=abe0999）；调查代理（只读，未改任何 src/）
- 目标：**sealed 类型/方法推断 → callvirt 静态化为直接调用**（S1 sealed 类 → S2 final 方法 → S3 接口单实现）
- 参考：`reference/corert/.../Compiler/DevirtualizationManager.cs` + `ILScanner.cs`（scan sealed 推断）+ 博客/审计 §3.5（`docs/audit/generated-code-audit.md` §3.5 virtual + §6 #2）

---

## 0. 执行摘要

1. **现状**：callvirt 恒走 vtable/接口线性搜索间接调用，**无任何去虚化**。审计实证：`new Square(4)` 后 `.Area()`/`.Sides()` 仍走 `vtable[N]`，且同表达式重复 `il2cpp_null_check`（generated-code-audit.md §3.5）。
2. **关键缺口**（三个）：① `TypeDef` 无 `IsSealed`（`TypeAttributes.Sealed` 0x1 未解析）；② `VirtualCallExpr` 不携带接收者静态类型（StackFold 的 `recvType` 已算出但只用于 BCL icall 重解析，没传到发射器）；③ 无"封闭世界无子类"推断。
3. **方案**：新增 `IsSealed` 解析 + `VirtualCallExpr.ReceiverTypeName` 打通 → 在 `EmitVirtualCall`（CodeGenerator.cs:2838）里当接收者静态类型 = 翻译集内的有效密封类型时，用 VTablePlanner 已算好的槽表直接解析具体实现方法 → 发直接调用。**纯编译期变换，运行时零改动**。
4. **阶段化**：S1 密封类直接调用（含封闭世界无子类扩展）→ S2 final 方法（`MethodDef.IsFinal` 已解析，打通内联闸门）→ S3 接口单实现（免线性搜索）。
5. **性能预期**：虚/接口调用密集代码 1.5-3x（审计 §6 #2 估算），每次调用省 vtable 读+间接跳转（接口调用再省 null_check + 线性搜索）；联动解锁常量传播、访问器折叠、虚方法内联。

---

## 1. 现状：callvirt 发射管线

### 1.1 数据流（grep 实证）

```
IlDecoder.cs:235  case ILOpCode.Callvirt
  → StackFold.cs:448/1775  EmitCall(..., virtualDispatch: true)
    → StackFold.cs:1953-2209  折叠：pop this 作 Args[0]；算 recvType 接收者提示（1971-2017）
    → StackFold.cs:2206  new VirtualCallExpr { MethodToken, ReturnsRef }   // 不含接收者类型！
      → CodeGenerator.cs:2838  EmitVirtualCall(VirtualCallExpr, md, types)
```

### 1.2 两类发射（CodeGenerator.cs:2828-2926）

- **类虚方法**（2902-2908）：
  ```cpp
  ((ret(*)(Il2CppObject*,...))il2cpp_null_check(recv)->klass->vtable[slot])(recv, args...)
  ```
  slot = `owner.VirtualMethods.FindIndex(SigKey == sig)`；vtable 槽表为编译期静态数组（CodeGenerator.cs:503-505 `EmitVTable`），槽布局由 `VTablePlanner.PlanAll` 分配（`MethodDef.VTableSlot`，VTablePlanner.cs:96-152）。
- **接口虚方法**（2894-2901）：
  ```cpp
  *il2cpp_interface_dispatch(recv, &I_class, ifaceSlot)
  ```
  运行时 `il2cpp_interface_dispatch`（runtime/src/metadata.cpp:305-345）对 `klass->interfaces` **线性遍历**接口表 + slotMap 查槽 → 间接调用。`Il2CppClass`（il2cpp_metadata.h:34-58）携带 `vtable/vtableSize/interfaces/slotMap`。
- **非虚 callvirt**（2911-2918）：C# 对每个实例调用都发 callvirt，目标非虚时**已经直接调用** `fnName(receiverCode, args...)` —— 这就是去虚化直接调用的现成先例与命名约定（`MethodCppName(owner, targetMethod)`，CodeGenerator.cs:5556）。

### 1.3 关键缺口（本次调研确认）

| # | 缺口 | 证据 |
|---|---|---|
| G1 | `TypeDef` 无 `IsSealed`——`TypeAttributes.Sealed`(0x1) 未解析 | MetadataLoader.cs:39-55 只解析 Interface/BeforeFieldInit/ValueType/GenericArity |
| G2 | `VirtualCallExpr` 不携带接收者静态类型 | Expr.cs:135-140 只有 MethodToken/ReturnsRef/Args；StackFold.cs:1971-2017 的 `recvType` 只喂 MethodRefResolver（2046-2049） |
| G3 | 无封闭世界推断（无子类 ⇒ 有效密封） | VTablePlanner 只算槽布局，不算派生关系/密封 |
| G4 | 虚方法永不内联（InlinterPass 闸门） | InlinePass.cs:96-102「Virtual methods are NEVER inlined」 |
| G5 | 接收者类型跟踪已存在但不完整 | StackFold `objTypes`（newobj/box/delegate/throw 结果，76/880/2610/2627）只覆盖部分路径 |

### 1.4 既有接收者类型提示（去虚化的现成输入）

StackFold.cs:1971-2017 已算 `recvType`（优先级）：
1. `ExceptionEntryExpr` → "System.Type"
2. `StringConstExpr` → "System.String"
3. `LocExpr` + `objTypes[loc]`（newobj/box/GetType/delegate 结果）→ 具体类型全名
4. `NullExpr.TypeName`（Encoding 单例折叠）
5. `constrained. <TypeToken>` 标记（值类型接收者）
6. `Object::ToString` 无提示时兜底 "System.Object"

其中 **newobj 结果**（EmitNewObj，StackFold.cs:2625-2627 `objTypes[name] = typeFullName`）正是审计 §6 #2 的"newobj 已知类型"场景。

---

## 2. 参考：CoreRT DevirtualizationManager（reference/corert）

### 2.1 核心 API（DevirtualizationManager.cs:15-95）

```csharp
IsEffectivelySealed(TypeDesc type)      // 元数据 IsSealed || IsModuleType || (array/ptr/byref/fnptr)
IsEffectivelySealed(MethodDesc method)  // method.IsFinal || IsEffectivelySealed(method.OwningType)
ResolveVirtualMethod(declMethod, implType)  // 虚方法在具体类型上的解析（类：FindVirtualFunctionTargetMethodOnObjectType；
                                            //   接口：ResolveInterfaceMethodTarget → FindVirtualFunctionTarget...）
```

**模型对照**（本工程等价物）：
- `IsEffectivelySealed(type)` ⇒ 需要 G1（解析 Sealed）+ 派生计数（G3）
- `IsEffectivelySealed(method)` ⇒ `MethodDef.IsFinal` **已解析**（MetadataLoader.cs:140）零成本可用
- `ResolveVirtualMethod(decl, impl)` ⇒ **VTablePlanner 已把覆盖解析进槽表**：`implType.VirtualMethods[slot]` 就是运行时会命中的实现（基类槽表深拷贝 + 签名匹配覆盖，VTablePlanner.cs:103-147）；接口路径 `implType.InterfaceSlotMaps[iface][ifaceSlot]` 已由 BuildInterfaceMaps 填好（VTablePlanner.cs:63-94）

### 2.2 扫描器扩展（ILScanner.cs:403-438）——封闭世界推断

CoreRT 扫描器在**元数据密封之上叠加"分配闭包"推断**：
- `_unsealedTypes`：任何被派生过的类型 ≠ 密封（403-408）
- `_constructedTypes`：从未分配的密封类型也不报密封（410-416，避免对未扫描方法发直接调用）
- 抽象类型不报密封（418-424）

**移植到本工程**：ILC 是**单程序集封闭世界转译**——`types` 列表就是全部类型。`IsEffectivelySealed(t)` 可安全定义为：
```
t.IsSealed (元数据)  ||  ( !t.IsAbstract && !t.IsInterface
                          && 翻译集内无 BaseType == t.FullName 的类型 )
```
（"无子类"在 MetadataLoader 第二遍已解析 BaseType（MetadataLoader.cs:155-165），VTablePlanner.PlanAll 后再加一个派生计数 pass 即可。）

### 2.3 测试资产（DevirtualizationTests.cs / Devirtualization.cs）

- `DevirtualizeWithUnallocatedType`：sealed Derived + 未分配 → 元数据算法报密封、扫描算法不报（防未扫描代码）
- `DevirtualizeSimple`：`Base p = new Derived(); p.Virtual();` → 扫描知道只有 Derived 分配 → 直接绑定
- 本工程的对应验收用例见 §4.4（虚拟用例在 `tests/cases/virtual`、接口在 `tests/cases/interface`）

---

## 3. 方案：sealed 推断 → 直接调用

### 3.1 模型改动（三处）

1. **`TypeDef.IsSealed`**（Model/TypeDef.cs + MetadataLoader.cs:45 区）：
   ```csharp
   IsSealed = (td.Attributes & TypeAttributes.Sealed) != 0,   // 0x1
   ```
2. **`VirtualCallExpr.ReceiverTypeName`**（Ir/Expr.cs:135-140 + StackFold.cs:2206）：
   在 `new VirtualCallExpr` 处把已算好的 `recvType`（1971-2017）写进表达式的**只读可选属性**；IR 各 pass 的克隆点（Expr.cs:427、Normalize.cs:244/397、DcePass.cs:587、AllocPromoPass.cs:414、TempSlotPass.cs:366、InlinePass.cs:879、CodeGenerator.cs:2138/3352）透传该字段（`Expr.Clone` 是统一入口）。
3. **`TypeDef.DerivedCount` / `IsEffectivelySealed`**（新，放 VTablePlanner 或独立 Devirtualizer）：
   VTablePlanner.PlanAll 之后一 pass：`foreach t: for b = t.BaseType..: b.DerivedCount++`；`IsEffectivelySealed(t) = t.IsSealed || (DerivedCount==0 && !IsAbstract && !IsInterface)`。

### 3.2 发射改动（CodeGenerator.cs EmitVirtualCall，纯编译期）

在 `OwnerIsTranslated`（BCL 接收者保持 MethodRefResolver 路径，2846-2884）**之后**、vtable 发射**之前**插入：

```
if (v.ReceiverTypeName 是翻译集内具体类型 T
    && DevirtManager.IsEffectivelySealed(T))
{
    // 解析具体实现（VTablePlanner 槽表 = 运行时命中的实现）
    impl = T.VirtualMethods[owner 的 sig 对应槽]            // 类虚方法
    // 接口路径: ifaceSlot → T.InterfaceSlotMaps[iface][ifaceSlot] → T.VirtualMethods[typeSlot]
    return $"{il2cpp_null_check(recv)} 后直接调用
            {MethodCppName(impl.Owner, impl)}(recvCode, args...)";
}
```

- **槽解析零新增算法**：`owner.VirtualMethods.FindIndex(SigKey == sig)`（2904 现有代码）+ `T.VirtualMethods[slot]`（VTablePlanner 已把覆盖写进 T 的槽表）。接口路径用 `T.InterfaceSlotMaps`（已存在）。
- **语义保真**：callvirt 的 null 检查语义通过 `il2cpp_null_check` 保留（与 vtable 路径 2891 相同）；直接调用形式与现有"非虚 callvirt 直接调用"（2911-2918）同构。
- **安全闸门**：仅当 ① 接收者静态类型在翻译集、② 为有效密封（元数据 sealed 或封闭世界无子类）、③ 目标槽方法为有效密封/`IsFinal` 时才去虚化。泛型实例接收者（"Box`1_int"）沿用 `VirtualTarget` 的泛型定义匹配（3014-3017）。

### 3.3 不做的事（范围边界）

- 不改运行时（`il2cpp_interface_dispatch`、`Il2CppClass`、vtable 表发射全保留——未被去虚化的调用点继续正确工作）。
- 不动 `DelegateInvokeExpr`（委托方法指针分派，独立路径 StackFold.cs:2179-2204）。
- 不动值类型/`constrained.` 路径（无 vtable；已有域映射 2062-2096）。
- 不做 CoreRT 的"分配闭包"全量扫描（本工程单程序集封闭世界，`types` 即全部；G3 的无子类推断已等价）。

---

## 4. 阶段化实施

### S1 — 密封类直接调用（基础，G1+G2+G3）
- **改动**：TypeDef.IsSealed 解析；VirtualCallExpr.ReceiverTypeName 打通（含 IR pass 透传）；EmitVirtualCall 插入去虚化分支；封闭世界无子类推断 pass。
- **命中形态**：`new Square(4).Area()`、`Square s = new Square(); s.Area()`（objTypes 已跟踪）；任意 sealed 类型接收者的类虚方法 callvirt。
- **验收**：新用例 `tests/cases/virtualsealed`（sealed 类 + 继承 + newobj 后调用）输出与 net48 逐字节一致；回归 `tests/cases/virtual`（Square 应变成直接调用 `Square_Area(...)`，对照 generated-code-audit.md §3.5 的 `vtable[0]` 样本）+ `tests/cases/interface` 全绿。
- **联动**：同表达式重复 null_check 消减（审计 #4）可在后续叠加（已知非空集合）。

### S2 — final 方法（`MethodDef.IsFinal` 已就绪，零模型改动）
- **改动**：`DevirtManager.IsEffectivelySealed(method) = method.IsFinal || IsEffectivelySealed(owner)`；发射条件放宽为"接收者静态类型 T 的有效槽方法 IsFinal"（即使 T 未密封——final 方法不可能再被覆盖，槽解析恒定）。
- **命中形态**：`sealed override` 方法（C# final）、不可派生类型上的虚方法。
- **联动**：InlinePass.cs:96-102 闸门——去虚化后的虚调用是编译期直接调用，`IsVirtual` 拒绝逻辑可对"已去虚化"标记放行，虚方法首次可内联（对照 InlinePass.cs:268/618）。
- **验收**：`tests/cases/virtualfinal`；`bench_inline` 相关基准复测。

### S3 — 接口单实现（免线性搜索）
- **改动**：接口路径发射分支——当 ① 接收者静态类型 T 翻译集内且实现该接口、② 接口在封闭世界内**只有这一个实现者**（或 T 有效密封）时，用 `T.InterfaceSlotMaps[iface][ifaceSlot]` 直接解析实现 → 直接调用，跳过 `il2cpp_interface_dispatch` 线性搜索。
- **命中形态**：`new Doubler(); ICalc c = d; c.Calc(...)`（objTypes 已跟踪 Doubler）；审计 §3.6 的 `Cases_Interface.cpp:44` 线性搜索形态。
- **验收**：`tests/cases/interfacesingle`；回归 `tests/cases/interface`（未单实现的接口仍走运行时路径）。
- **可选增强**（不在 S3 必须范围）：运行时接口 dispatch 缓存（槽位缓存），编译期无法单实现时降低线性搜索成本。

---

## 5. 性能预期

| 指标 | 预期 | 依据 |
|---|---|---|
| 虚/接口调用密集代码 | **1.5-3x** | 审计 §6 #2 明确估算（generated-code-audit.md:356-357） |
| 单次类虚调用 | 省 vtable 读（2 次内存读）+ 间接跳转 → 1 次直接调用 | CodeGenerator.cs:2904-2926 vs 2911-2918 直接调用形态 |
| 单次接口调用 | 省 `il2cpp_null_check` 调用 + 接口线性搜索（metadata.cpp:305-345）+ 间接跳转 | §3.6 审计（159-160） |
| 联动收益 | 常量传播/访问器折叠可作用到直接调用；虚方法首次可内联（S2） | 审计 §6 #2 联动说明（356-357）；InlinePass.cs:96-102 |
| 无收益风险 | 未去虚化的调用点零影响（vtable 路径原样保留）；运行时不改 | 3.3 范围边界 |

**成本**：仅编译期 —— 每 callvirt 多一次密封判定（HashSet 查表）+ 一次槽查找；`types` 集合小（单程序集），可忽略。

**风险与边界**：
- null 接收者语义：直接调用必须保留 `il2cpp_null_check`（与 vtable 路径 2891 一致），不能学 2911-2918 省略检查（该处依赖方法体内自检，对不触碰 this 的虚方法语义不精确）。
- 反射接收者（`typeof`/`GetType` → "System.Type"/"System.String"）不在翻译集，天然安全（`types.Any` 匹配失败即回落）。
- 密封判定必须排除 `IsAbstract`/`IsInterface`（对照 ILScanner.cs:418-424）。

---

## 6. 验收清单

1. `dotnet build src/il2cppc` 零错误（C0）。
2. 新用例：`virtualsealed`（S1）/ `virtualfinal`（S2）/ `interfacesingle`（S3）输出与 net48 参考逐字节一致。
3. 回归：`virtual`、`interface`、`boxing`（boxed 虚调用）、`delegate*`（委托不受影响）全绿。
4. 反汇编实证：Square 用例 `vtable[0]` 间接调用 → `call Square_Area` 直接调用（对照 square_audit.s，generated-code-audit.md:138-148）。
5. 静态字段/cctor 路径不受影响（去虚化只动 callvirt 发射）。
