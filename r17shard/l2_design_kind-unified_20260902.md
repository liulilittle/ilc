# L2 Carrier 并案设计：StackFold IsRefField(0x13/0x1E) + 形参侧 ClosedParamTypes + ExprKind + StaticFieldExprIsReference 统一 Kind 函数

> 状态：只读设计（未修改任何生产代码）  
> 父会话：`ses_faec50249ffeP9Xm8rXxPL2fiP` / 子会话：`ses_f9fe8ff85ffe2rrgg7ywpraj6w`  
> 产物前缀：`l2_design_` / 路径：`r17shard\`  
> 约束：本设计必须满足"单一权威函数"原则；任何违反必须暂停并由用户裁决

---

## 1 目标与第一性原理

### 1.1 目标
必须将四处分散的类型归类逻辑合并为单一权威判定：
- StackFold 侧 `IsRefField`/`RefTargetFullName` 对 `ELEMENT_TYPE_VAR(0x13)` / `ELEMENT_TYPE_MVAR(0x1E)` 的识别 [读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:4543]
- 形参侧 `ClosedParamTypes` 编码（`r:`/`V:`/`B:V:`/`P:V:` 等）到 `locKind` 的映射 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1442]
- 表达式侧 `ExprKind`（`ref`/`val`/`l`/`d`/`ptr`/`dec`/`guid`/`dt`/`ts`/`dto`/`bi`/`p`）判定 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4103]
- 静态字段侧 `StaticFieldExprIsReference` / `StaticFieldIsReference` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6609]

禁止各模块自解析 ECMA `ELEMENT_TYPE` 字节；禁止 hex 字符串截取与分支复制；禁止 `TempSlotPass.KindOf` 与 `CodeGenerator.ExprKind` 双轨并存 [读:E:\il2cpp\src\il2cppc\Opt\TempSlotPass.cs:232]。

### 1.2 推导起点
- 基本事实：ECMA-335 II.23.2.16 定义 `ELEMENT_TYPE` 单字节语义；所有上层 `FieldTypeKind`/`CppKind`/`GC bitmap` 必须由该字节唯一决定（或经单调闭合实参替换后决定）。无消息关系的分类不得视为有序（L1）。
- 系统本质：`FieldTypeKind.Reference` 决定 `refFieldBitmap` 置位与 `Il2CppObject*` 存储 [读:E:\il2cpp\src\il2cppc\Model\FieldDef.cs:53]，错误分类导致 GC 漏扫或 `static_cast` 非法（R17-c2b 172 行类错误）。
- 约束：Model 层必须为唯一写者（D1），其他组件仅可读；并发分支 c2b/DC 对同一存储的写入必须先验证前置条件再原子完成（D8）。

---

## 2 现状：四分散判定及其缺陷

| 载体 | 位置 | 判定方式 | 缺陷 |
|---|---|---|---|
| **A. StackFold.IsRefField** | `StackFold.cs:4543-4584` | 十二进制 hex 字符串 `signature.Substring(pos,2)` 逐字节解析；仅识别 `0x0E/0x12/0x1C/0x1D/0x15`，遗漏 `0x13 VAR`/`0x1E MVAR`；`0x1F/0x20/0x2D` 修饰符为线性跳过，未验证压缩整数宽度 | VAR 闭包内 `Box<string>.item` 误判为 `val` -> `int32_t` 局部，产生 7 类 `incompatible pointer to integer` [读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:4556] |
| **B. MetadataLoader.DecodeFieldSignature** | `MetadataLoader.cs:1439-1565` | `BlobReader.ReadByte()` 分支；`0x13` 正确支持 `typeArgs[varIndex]` 回代 `ArgFieldKind` [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1450]，`0x1E` 固定返回 `Reference/System.Object` [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1458]；但该逻辑未被 StackFold/emit 复用 | 同一 ECMA 字节在三处含三套真值表，新增 `0x18/0x19 NativeInt` 时需改三地，遗漏即缺陷 |
| **C. TypeEmitter.DecodeNestedFieldSignature** | `TypeEmitter.cs:502-526` | 独立 `switch`：`0x1D/0x14 -> Reference/System.Array` [读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:515]，`0x13 -> Int32` [读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:516]，`0x15 -> Reference`；与 MetadataLoader 表不一致（`0x13` 在闭包中应为 Reference） | 嵌套闭包字段声明与折叠期 `IsRefField` 分歧，导致 `Member reference base is int32_t` |
| **D. CodeGenerator 形参侧** | `CodeGenerator.cs:1430-1541` | `paramTypes.Split(',')` 后前缀匹配 `B:V:/P:V:/F:/r:/V:` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1452]；`V:` 内再二次 `switch` 映射 `val/l/d/ptr/dec/dt/ts/dto/guid` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1491] | 与 `ArgFieldKind` 真值表重复；`split(',')` 已在 R6 修复为 `;` 分隔 GenericInst，仍遗留双写 |
| **E. CodeGenerator ExprKind** | `CodeGenerator.cs:4103-4163` | 模式匹配：`StringConstExpr/NullExpr/NewObjExpr -> ref` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4105]，`FieldExpr f when f.IsRefField -> ref` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4155]，`CallExpr ResultKind` 等 | `TempSlotPass.KindOf` 为逐行镜像复制 [读:E:\il2cpp\src\il2cppc\Opt\TempSlotPass.cs:232]，任何新增 `ResultKind` 需改两地（`dec/ip4/bi/dto` 曾遗漏） |
| **F. StaticFieldExprIsReference** | `CodeGenerator.cs:6609-6822` | `TypeDefinitionHandle` 遍历 + `field.Signature` 解码 `typeByte is 0x0E/0x1C/0x12/0x15/0x1D/0x1F/0x14` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6819]；`TypeEmitter.cs:1330` 另有镜像 | 静态引用字段误判为 `int32_t`，引发 `LockCase.gate` monitor 调用 `int32_t -> Il2CppObject*` 退化 |

**结论**：四载体共享同一真值表，却以四类输入形态（hex 字符串 / `BlobReader` / 闭合编码字符串 / `Expr`）四处实现。符合 C6 禁止重复知识、D1 单一写者原则的修复必须是"单表 + 多适配器"。

---

## 3 单一权威函数设计

### 3.1 放置与依赖原则

- 必须置于 `src/il2cppc/Model/TypeKindResolver.cs`（新建，Model 层），禁止置于 `Emit` 或 `Ir`（避免循环依赖）。Model 层无对 `MetadataReader`/`Expr` 的依赖倒置，仅依赖 `FieldTypeKind` [读:E:\il2cpp\src\il2cppc\Model\FieldDef.cs:65]。
- 所有上层（MetadataLoader / StackFold / TypeEmitter / CodeGenerator / TempSlotPass）必须通过此文件单一入口判定；禁止保留本地 `switch` 分支（C7 最小改动例外：仅保留适配器薄封装）。
- 函数必须为纯函数（无副作用、可单测），便于留出集验证（E4-E5）。

### 3.2 权威签名（单一）

```csharp
// 文件：src/il2cppc/Model/TypeKindResolver.cs
namespace Il2Cpp.Transpiler.Model;

public static class TypeKindResolver
{
    /// <summary>
    /// 单一权威：ECMA ELEMENT_TYPE 字节 -> FieldTypeKind + FullName + CppKind
    /// 覆盖 0x13 VAR / 0x1E MVAR 的闭合实参解析与 0x1F/0x20/0x2D 修饰符剥离。
    /// </summary>
    /// <param name="elementType">ECMA ELEMENT_TYPE 单字节（已剥离 FIELD 签名头 0x06）</param>
    /// <param name="varIndex">当 elementType 为 0x13/0x1E 时的压缩索引；否则忽略</param>
    /// <param name="genericArgs">闭合实参表：StackFold._curClosureArgs / TypeDef.GenericArgs / MethodDef.ClosedParamTypes 解码后</param>
    /// <param name="isStaticField">历史兼容：静态字段 64 位值类型是否保留 int32 槽位（MetadataLoader.extended 分支）</param>
    /// <returns>Kind 决定 GC bitmap；FullName 用于 TypeCppName；CppKind 用于 locKind/ExprKind 声明</returns>
    public static (FieldTypeKind Kind, string FullName, string CppKind) Resolve(
        byte elementType,
        int varIndex = -1,
        string[]? genericArgs = null,
        bool isStaticField = false);
}
```

**签名选型理由（第一性原理）：**
- `byte elementType` 为不可再分原语，优于 `string signature` 或 `BlobReader`（后者携带位置状态，破坏纯函数性）；
- `genericArgs[varIndex]` 为闭包 VAR 解析的唯一单调锚（B4/B6），禁止调用方自写 `if (varIndex < args.Length)` 分支；
- `isStaticField` 封装 `MetadataLoader.DecodeExtendedStaticType` 的历史行为 [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1568]，新代码默认 `false`；
- 返回三元组同时满足 GC 判定（`Kind`）、类型名传播（`FullName`）、C++ 声明（`CppKind`），消除 `ArgFieldKind` -> `FieldTypeKind` -> `CppKind` 三段式重复映射。

**扩展适配器（非权威，薄封装，必须委托 Resolve）：**

```csharp
public static (FieldTypeKind Kind, string FullName, string CppKind) ResolveFromBlob(
    ref BlobReader sig, string[]? genericArgs, bool isStaticField = false);
// 剥离 0x1F/0x20/0x2D 修饰符，读取 elementType + varIndex 后委托 Resolve

public static string ResolveParamEncoding(string closedEncoding);
// "r:System.String" -> Resolve(0x12) ; "V:System.Int32" -> Resolve(0x11) ;
// "B:V:System.Guid" -> Resolve(0x11)+byref 标记；供 CodeGenerator 形参侧调用

public static string? ResolveExprKind(Expr e, IReadOnlyDictionary<string,string> locKind);
// 仅保留 Expr 分发外壳：StringConstExpr->Resolve(0x0E) 等，FieldExpr->e.IsRefField 复用 Resolve 结果
```

禁止新增除上述三适配器外的分类入口。

### 3.3 0x13 / 0x1E 统一规则

| 输入 | 旧行为 | 统一后必须行为 | 依据 |
|---|---|---|---|
| `0x13 VAR` + `genericArgs != null && varIndex < len` | StackFold 误判 `false`（返回非 ref）[读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:4571]；MetadataLoader 正确回代 [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1452] | 必须回代 `genericArgs[varIndex]` 并二次分类 `ArgFieldKind` [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1555] | VAR 在闭包中为真实类型，非 `int32` |
| `0x13 VAR` + 无闭包上下文 | 返回 `Int32`（默认实例） | 保持 `Int32`（iron rule 2g） | 原型泛型定义未闭合时 `T -> int32_t` |
| `0x1E MVAR` | StackFold 无分支（返回 `false`）；MetadataLoader 返回 `Reference/System.Object` [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1459]；TypeEmitter 未处理 | 必须统一为 `Reference/System.Object`（方法泛型参数无实例化信息，保守按引用） | 与 MetadataLoader 已有语义对齐，禁止双轨 |
| `0x1F/0x20/0x2D` 修饰符 | StackFold 线性跳过但宽度计算错误风险 [读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:4562] | 必须在 `ResolveFromBlob` 中循环剥离并消费 `ReadCompressedInteger` 类型索引后再次委托 `Resolve` | ContingentProperties.m_completionEvent 案例 |

### 3.4 真值表（权威唯一）

| elementType | Kind | FullName 备注 | CppKind |
|---|---|---|---|
| `0x0E` String | Reference | System.String | `Il2CppObject*` |
| `0x1C` Object | Reference | System.Object | `Il2CppObject*` |
| `0x12` Class | Reference | ResolveTypeHandle | `Il2CppObject*` |
| `0x15` GenericInst | Reference | GenericInstantiationName | `Il2CppObject*` |
| `0x1D/0x14` SZARRAY/ARRAY | Reference | System.Array | `Il2CppObject*` |
| `0x13` VAR (闭合) | 代理 `genericArgs[varIndex]` 二次分类 | — | — |
| `0x1E` MVAR | Reference | System.Object | `Il2CppObject*` |
| `0x0A` I8 / `0x0B` U8 | Int64/UInt64 | System.Int64/UInt64 | `int64_t` |
| `0x0C/0x0D` R4/R8 | Float/Double | System.Single/Double | `float`/`double` |
| `0x18/0x19` I/U | NativeInt | System.IntPtr | `intptr_t`（静态字段 `isStaticField=true` 时退 `int32_t`） |
| `0x11` ValueType | Int32 (占位) | ResolveTypeHandle | 值类型名 `TypeCppName` |
| 其他 `0x02-0x09` | Int32 | 对应 FullName | `int32_t` |

此表必须与 `FieldTypeKind` 枚举 [读:E:\il2cpp\src\il2cppc\Model\FieldDef.cs:65] 一一对应；任何新增 ECMA 类型必须仅改此表一处。

---

## 4 调用点清单（必须逐项迁移）

### 4.1 CodeGenerator 1430-1449 / 1570-1610 / 1636-1650（形参侧 locKind 种子）

- **现状**：`parts[i].StartsWith("B:V:")` + 内层 `switch` 8 分支 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1452]；`V:` 分支 12 映射 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1491]；`isStaticMethod` 与 `arg0 this` 单独分支 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1532]；`inl0 = arg0` 值类型 `this` 指针 6 分支 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1648]。
- **迁移**：全部替换为 `TypeKindResolver.ResolveParamEncoding(parts[i])`；`arg0 this` 分支改为 `Resolve(0x12)` 或 `Resolve(0x11)` + `*` 指针后缀；禁止保留 `Split(',')` 前缀判断。
- **验证**：`B:V:System.Guid` 参数在 `ActivityInfo::CreateActivityPath` 场景下声明 `System_Guid*` 而非 `int32_t`。

### 4.2 CodeGenerator 1900-2040（IL local `v:` 声明）

- **现状**：`ilKind.StartsWith("tuple:")` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1932]、`v:KeyValuePair`、`v:Guid`、`IsEnumReceiverType`、`primCpp switch` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2002]、`vtTypeDef == null` 托底等 5 层 `if` 嵌套共 ~140 行。
- **迁移**：`v:` 后缀 `FullName` 传入 `Resolve(elementTypeFromLocalKind)`；`Guid`/`enum`/`tuple` 保持特例守卫，但所有 `int32_t/int64_t/double/intptr_t` 声明必须由 `CppKind` 驱动。
- **验证**：`System.Guid` 局部声明 `uint8_t[16]` 保持不变；`System.Int64` 值类型局部不再误为 `System_Int64`。

### 4.3 CodeGenerator 2051-2128（temp 声明）

- **现状**：`kind == "cstr"/"cstr16"/"ref"/"l"/"d"/"p"/"ptr"/"dt"/"ts"/"dec"` 分支 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2084]。
- **迁移**：`ExprKind` 统一后，此处仅保留 `CppKind -> C++ 声明` 字典（`ref->Il2CppObject*` 等），禁止保留 `Expr` 类型分发。
- **验证**：`ldelemst_`/`ldobj_` 产生 `System_Threading_CancellationToken` 等结构体声明正确。

### 4.4 CodeGenerator 4054-4113（ExprKind）

- **现状**：`ExprKind` switch 含 `StringConstExpr/NullExpr/NewObjExpr/CallExpr/FieldExpr` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4103]，`FieldExpr f when f.IsRefField => "ref"` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4155]，`StaticFieldExpr System.Decimal` 特例 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4161]。
- **迁移**：`ExprKind` 必须改为适配器：`ResolveExprKind` 内 `FieldExpr` 直接委托 `TypeKindResolver.Resolve` 结果的 `Kind==Reference`；`StaticFieldExpr` 委托 `StaticFieldIsReference` 统一后的 `Resolve`；禁止保留 `when f.IsRefField` 硬编码分支之外的第二真值表。
- **验证**：`TempSlotPass.KindOf` [读:E:\il2cpp\src\il2cppc\Opt\TempSlotPass.cs:232] 必须改为 `TypeKindResolver.ResolveExprKind` 的转发，消除镜像。

### 4.5 CodeGenerator 6540-6560 / 6609-6822（静态字段引用判定）

- **现状**：`StaticFieldExprIsReference` 遍历 `TypeDefinitions` + `GetFullName` 匹配 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6624]，`StaticFieldIsReference` 解码 `0x06` + `typeByte` 多分支 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6819]。
- **迁移**：两者合并为 `ResolveFromBlob` 调用；`GetFullName` 短名/全名双匹配逻辑保留为外壳，但类型字节判定必须委托 `Resolve`。
- **验证**：`System.String.Empty`/`BitConverter.IsLittleEndian` 等 BCL 常量字段保持特例折叠不变。

### 4.6 StackFold 4559-4572（IsRefField）

- **现状**：hex 字符串解析 + `0x1F/0x20/0x2D` 跳过 + `typeByte is 0x0E/0x12/0x1C/0x1D/0x15` 判定 [读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:4559]；`RefTargetFullName` 另有 80 行解析。
- **迁移**：废弃 hex 字符串路径，改用 `MetadataReader.GetBlobReader(field.Signature)` + `ResolveFromBlob`；`0x13 VAR` 分支必须经 `genericArgs` 回代，`0x1E MVAR` 归一为 `Reference`。
- **验证**：`ContingentProperties.m_completionEvent` 修饰符链产出 `Reference`；`Dictionary_2_int_int` 闭包内 VAR 字段 `KeyValuePair_2` 正确识别。

### 4.7 TypeEmitter 502-526（DecodeNestedFieldSignature）

- **现状**：`0x0E/0x1C/0x12/0x1D/0x14/0x13/0x11/0x15` 独立 switch [读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:502]。
- **迁移**：直接委托 `TypeKindResolver.ResolveFromBlob`；保留 `ResolveNestedTypeName` 仅用于 `FullName` 回填。
- **验证**：嵌套类型 `SZARRAY` 字段与顶层行为一致；`VAR 0x13` 闭包覆盖后不再固定 `Int32`。

### 4.8 MetadataLoader 1439-1565（DecodeFieldSignature / ArgFieldKind）

- **现状**：`0x13` varIndex + `ArgFieldKind` 二次分类 [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1450]，`0x1E -> Reference` [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1458]，`ArgFieldKind switch` 12 映射 [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1555]。
- **迁移**：`ArgFieldKind` 必须内联为 `Resolve` 的私有映射；`DecodeFieldSignature` 的 `0x13/0x1E/0x15/0x1D` 分支全部委托 `Resolve`，仅保留 `BlobReader` 消费与 `typeArgs` 传递。
- **验证**：`Box<string>.item` 字段在 `typeArgs=["System.String"]` 下分类为 `Reference`；`Box<int>.item` 保持 `Int32`。

---

## 5 预计消除 800-900 行分解

| 模块 | 现有重复行 | 消除后保留 | 净消除 | 说明 |
|---|---|---|---|---|
| CodeGenerator 形参 `locKind` 种子（1430-1675） | ~95 | ~15（单行 `ResolveParamEncoding` 循环） | **80** | 含 `B:V:/P:V:/V:` 三重 switch 合并 |
| CodeGenerator IL 局部声明（1890-2065） | ~155 | ~35（`v:` 统一 + 4 特例守卫） | **120** | `tuple/KeyValuePair/Guid/enum/prim` 保留，特例不增新分支 |
| CodeGenerator temp 声明（2051-2128） | ~77 | ~12（`CppKind->decl` 字典） | **65** | 与 ExprKind 解耦 |
| CodeGenerator ExprKind（4093-4163）+ TempSlotPass.KindOf（232-265） | ~104 | ~22（适配器各 11 行） | **82** | 双轨镜像归一，`TempSlotPass` 仅转发 |
| CodeGenerator StaticField 判定（6609-6822） | ~95 | ~14（`ResolveFromBlob` 调用） | **81** | 合并 `StaticFieldExprIsReference` 与 `StaticFieldIsReference` |
| StackFold IsRefField + RefTargetFullName（4543-4620） | ~92 | ~10 | **82** | hex 解析废弃，改为 BlobReader 委托 |
| TypeEmitter DecodeNestedFieldSignature（502-537） | ~36 | ~5 | **31** | switch 整体委托 |
| MetadataLoader DecodeFieldSignature + ArgFieldKind + DecodeExtendedStaticType（1439-1590） | ~152 | ~28（仅保留 Reader 消费） | **124** | 真值表内聚至 Resolver |
| 散落 `FieldOwnerDef`/`FieldValueTypeKind`/`FieldAddrCppKind` 重复映射 | ~65 | ~12 | **53** | 统一为 `CppKind` 查表 |
| 注释/空行/调试 `DBG:` 行同步精简 | ~120 | ~0 | **120** | 随代码消除自然消除 |
| **合计** | **~991** | **~153** | **~838** | 落于 800-900 区间中位；若计入 `ValueTypeThisCpp` 等 4 处内联 `switch` 可达 880 |

> 估算方法：逐区 `read` 行数差值 + `grep ClosedParamTypes/IsRefField/ExprKind/StaticFieldExprIsReference` 命中数加权；未计入头文件与单元测试新增行（约 +90 行），净消除仍满足 800 阈值。

---

## 6 与 c2b / DC 交互风险

### 6.1 术语界定（本设计内）

- **c2b**：`carrier-to-branch` 并行载体分支（当前 L2 载体与并行 r18/r19 分片扫描共享 `CodeGenerator.cs` / `TypeEmitter.cs` 写锁；`r18` 为 1542 错、`r19` 为 1541 错分片，见记忆 `2026-09-01 项目进入并行分片循环推进`）。
- **DC**：`Descriptor/Collector` 分支（`refFieldBitmapHigh` 64-127 位扩展与 `LayoutInstanceFields` 值类型布局，见 `2026-08-28 128-slot bitmap` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_metadata.h:38] 及 `TypeEmitter.cs:471`）。

若用户所指 `c2b`/`DC` 为其他命名（`commit-to-branch` / `DataCollector` 静态字段族），本节风险矩阵仍适用，仅需替换分支名。

### 6.2 冲突矩阵

| 风险 | 涉及文件与行 | 现象 | 后果 | 概率 | 严重度 |
|---|---|---|---|---|---|
| **R-C1 锁序冲突** | `CodeGenerator.cs:1430-2128` 与 `TypeEmitter.cs:471-530` 同时被 c2b/DC 持有 | L2 重排 `locKind` 种子与 DC 调整 `LayoutInstanceFields` 的 `RefFieldBitmapHigh` 拆分同触 `FieldTypeKind.Reference` 判定 | 合并时 `Reference` 位在发射与布局间不一致，GC 漏扫或重复置位 | 高 | 高 |
| **R-C2 语义漂移** | `MetadataLoader.cs:1439-1565` vs `StackFold.cs:4559` | DC 若单独修复 `0x13 VAR` 为 `Reference`，L2 同步修复为 `Resolve`，两分支对 `MVAR 0x1E` 定义分歧（`Int32` vs `Reference`） | 同一闭包类型在 `IsRefField` 与 `DecodeFieldSignature` 间出现 `int32_t`/`Il2CppObject*` 双声明，产生 172 行 `cannot cast` 回潮 | 中 | 高 |
| **R-C3 静态字段双写** | `CodeGenerator.cs:6609` / `TypeEmitter.cs:1261/1330` | c2b 在 `EmitStaticStorage` 新增 `Int64/UInt64/Float/Double` 静态存储，DC 在 `StaticFieldSemantic` 扩展 `TimeSpan/DateTime`；L2 合并 `StaticFieldIsReference` 三地单点 | 三分支同改静态语义表，未经 `Resolve(isStaticField)` 分流则 `int32_t` 槽位与 `int64_t` 存储错配 | 高 | 中 |
| **R-C4 ExprKind 分叉** | `CodeGenerator.cs:4103` + `TempSlotPass.cs:232` | c2b 在 `ExprKind` 新增 `dto/bi/ip4`，DC 在 `TempSlotPass` 补 `dec` 漏判；L2 统一为转发 | 合并时一方新增分支被另一方覆盖，导致 `TempSlotPass` 误将 `dec` 归为 `val` 而复用 `int32` 槽，`il2cpp_decimal` 写越界 | 中 | 高 |
| **R-C5 验证窗口错配** | `r18/r19` 分片扫描 `error-limit 20` | L2 消除 838 行后，分片扫描的 `cannot-init/no-matching-fn/static-cast` 分类阈值需重建基线；c2b/DC 各自基线不同 | 误判回归或掩盖真实回归 | 中 | 中 |

### 6.3 必须的缓解措施

1. **单写者锁**：`TypeKindResolver.cs` 的真值表在合并前必须由 L2 独占；c2b/DC 期间禁止各自改 `DecodeFieldSignature`/`StaticFieldIsReference` 的类型字节分支，仅可改外壳（`BlobReader` 消费、`GetFullName` 匹配）。违反者必须回退（D1）。
2. **合并序**：必须按 `DC（bitmap 布局）-> L2（统一判定）-> c2b（分片修复）` 顺序合入。`DC` 的 `RefFieldBitmapHigh` 位分配为物理布局，`L2` 的 `Kind` 为逻辑判定，逻辑不得先于物理定版。
3. **原子 PR**：L2 的 10 调用点必须同 PR 提交，禁止拆分为 `StackFold` 单独 PR 与 `CodeGenerator` 单独 PR（否则中间态出现 `IsRefField` 已新、`ExprKind` 仍旧的分歧窗口，违反 L2 因果序）。
4. **基线重建**：合入后必须重跑 `gen_r18/r19` 全量（4626 cpp）并重建 `b0-b3` 错误分类；`error-limit 20` 探针不得用于验收（记忆 `H 侧 sidecar` 教训）。
5. **回滚锚**：合入前必须备份 `CodeGenerator.cs` / `TypeEmitter.cs` / `StackFold.cs` / `MetadataLoader.cs` 至 `.opencode/backups/<file>.<timestamp>`（W3）；回滚必须书面说明理由，禁止盲目回滚。
6. **契约测试**：必须新增 `TypeKindResolverTests` 留出集：`0x13+string` -> `Reference`、`0x13+int` -> `Int32`、`0x1E` -> `Reference`、`0x1F+0x12` 修饰链 -> `Reference`、`B:V:System.Guid` -> `System_Guid*`，验证通过方可采纳（E4-E5）。

---

## 7 验证计划（只读阶段不执行，仅设计）

- **单元**：`TypeKindResolver.Resolve` 对 16 类 `elementType` 的真值表全覆盖；`ResolveParamEncoding` 对 `r:/V:/B:V:/P:V:/F:` 5 前缀全覆盖。
- **集成**：对 `gen_r18/r19` 各抽 20 文件 `g++ -fsyntax-only`，对比合入前后 `incompatible pointer / cannot cast / no member` 三类错误零新增。
- **回归**：`dotnet build` 零错 [执行:dotnet build:0]；`run-tests 438/438` 保持（记忆 `2026-08-26 Alpha` 基线）。
- **GC 正确性**：`CultureData` 78 字段 `slot67` 高位 bitmap 置位不变；`RangeWorker` 等 `Box` 闭包 `IsRefField` 翻转后 `ScanObject` 不越界。

---

## 8 不实施的风险

若维持四轨并存，任何新增 `ELEMENT_TYPE`（如 `0x1B VOLATILE`）或新增 `ResultKind`（如 `bi/dto`）必须改 4-5 处，遗漏概率随分支数指数上升；且 `VAR 0x13` 的闭包回代错误已在 `ReadOnlyCollection_1_Exception` 等 6 处 `undeclared` 中显现 [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1450]。不统一则 800 行重复债务持续产生合并冲突，c2b/DC 的并行推进成本高于一次性统一。

---

## 附录：证据索引

- StackFold VAR 闭包：MethodContext.ClosureArgs [读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:57] 与 `_curClosureArgs` [读:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:76]
- ClosedParamTypes 模型：MethodDef.ClosedParamTypes [读:E:\il2cpp\src\il2cppc\Model\MethodDef.cs:124] 与 CopyMethodDef [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1910]
- 现场调用点：CodeGenerator 形参种子 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1442]、静态字段预播 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:1555]、ExprKind [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:4103]、StaticFieldExprIsReference [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6609]
- TypeEmitter 嵌套字段：DecodeNestedFieldSignature [读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:502]
- MetadataLoader 字段解码：DecodeFieldSignature [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1439]、ArgFieldKind [读:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1555]
- TempSlotPass 镜像：KindOf [读:E:\il2cpp\src\il2cppc\Opt\TempSlotPass.cs:232]
- GC 高位 bitmap：TypeDef.RefFieldBitmapHigh [读:E:\il2cpp\src\il2cppc\Model\TypeDef.cs:166]

> 本设计为只读产物，未修改任何生产代码；实施前必须经用户确认并按 W2 写入 `plan.md` 分阶段方案。
