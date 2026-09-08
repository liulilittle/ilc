# TypeEmitter 命名碰撞根因调查报告

**调查日期**: 2026-09-06
**目标文件**: E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs
**调查范围**: TypeCppName、Sanitize、所有引用侧、MethodRefResolver

---

## 1. 根因

`TypeCppName(TypeDef t)` 将多个互异的 .NET 泛型类型折叠到**同一个 C++ 标识符**，在 `generated.h` 里发成多份 `struct X {}` → 全树 TU 级 redefinition 失败。

### 1.1 核心碰撞函数 (TypeEmitter.cs:602-609)

```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```

`Sanitize` 把所有元数据字符 `.` `+` ``` ` ``` `<` `>` `,` `[` `]` `=` `!` `*` `&` `:` 替换为 `_`，但**丢弃了泛型参数信息**。

### 1.2 两类子形

**(A) 真碰撞 — 不同 .NET 类型 → 同名 C++ 名**

证据（r78 构建 shard3 实测）:
- `Node_int(2×)`: `ConcurrentDictionary.Node<int>` vs `Dictionary.Node<int>` → 同名字段布局不同（3 slot vs 4 slot）
- `Dictionary_2_object_object(3×)`: 同一类型被 emit 多次（去重 HashSet 遗漏）
- 其他: `Dictionary_2_Enumerator_object_object`, `Dictionary_2_KeyCollection_object_object`, `Dictionary_2_ValueCollection_object_object`, `KeyValuePair_2_object_object`, `List_1_object(2×)`, `EqualityComparer_1_object`, `ObjectEqualityComparer_1_object`, `InternalPartitionEnumerable_int`, `InternalPartitionEnumerator_int`, `ReadOnlyCollection_1_object`, `DebugView(2×)` 等共 18 个名字重复。

**(B) 引用侧与定义侧命名叉乘**

调用侧 `GenericInstantiationName`（CodeGenerator.cs:4970-4998、MethodRefResolver.cs:1772-1830、StackFold.cs:3729）自己拼名而不委托给 `TypeCppName`：
- 对 `0x02 bool/0x03 char/0x11 valuetype/0x12 class/0x1C object` 写死返回字面量 `"bool"` `"char"` `"valuetype"` `"class"` `"object"`
- 对 `0x0A long/0x0B ulong` 写死 `"long"` `"ulong"`
- 不区分 `0x11 valuetype` 的子类（System.DateTime vs 自定义 struct 都写 `"valuetype"`）

而定义侧 `ClosureArgShortName`（MetadataLoader.cs:1765-1791）:
- 把 `System.Boolean/Char/SByte/Byte/Int16/UInt16/Int32/UInt32` 统一映射为 `"int"`（M3 单实例化约定）
- 把 `System.Int64/UInt64` 映射为 `"long"`
- 把 `System.Single/Double` 映射为 `"double"`
- 把 `System.Object` 映射为 `"object"`，`System.String` → `"string"`

引用侧与定义侧对同一 `0x08 int` 的处理一致，但对 `0x12 class` 的处理叉乘：
- 定义侧: `ClosureArgShortName` 把任何引用类型（`List<MyA>` / `List<MyB>` / `Nullable<int>`）统一映射为 `"object"`
- 引用侧: `ReadM3Arg` 对 `0x12` 调用 `ClosureTypeHandleArg` → 走 `md.GetFullName` → 把 `System.Collections.Generic.List<MyA>` 原样拼出 → `"System_Collections_Generic_List_MyA"`

结果：同一闭包，定义侧声明 `List_1_object_struct`，引用侧使用 `List_1_System_Collections_Generic_List_MyA_struct` → 双方都 undeclared。

---

## 2. 证据链

### 2.1 定义侧 TypeCppName (TypeEmitter.cs:602-609)
[READ:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:602-609]

### 2.2 Sanitize (TypeEmitter.cs:637)
[READ:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:637]

### 2.3 定义侧克隆命名 (MetadataLoader.cs:1596-1618)
`CloneClosedType` 用 `FullTypeName(md, tdHandle) + "_" + string.Join("_", shortArgs)`，shortArgs 走 `ClosureArgShortName`。
[READ:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1596-1618]

### 2.4 ClosureArgShortName (MetadataLoader.cs:1765-1791)
32 位原语家族统一映射为 `"int"`；引用类型统一为 `"object"`。
[READ:E:\il2cpp\src\il2cppc\MetadataLoader.cs:1765-1791]

### 2.5 收集侧标准化 (MetadataLoader.cs:103-116)
把 `0x12 CLASS` 实参标准化为 `"System.Object"`（R1b 2026-08-30）。
[READ:E:\il2cpp\src\il2cppc\MetadataLoader.cs:103-116]

### 2.6 引用侧 ReadM3Arg (CodeGenerator.cs:5003-5025)
对 `0x12 CLASS` 写死返回 `"class"` 而不是委托给 `ClosureArgShortName` — 与定义侧行为分叉。
[READ:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:5003-5025]

### 2.7 引用侧 ClosureTypeHandleArg (CodeGenerator.cs:6201-6217)
对 `0x12 CLASS` 走 `md.GetFullName` 获取全名再 `ClosureArgShortName` — 但该路径仅在 StackFold 的 GenericInstantiationName 中使用，不在 CodeGenerator 自身的 ReadM3Arg 中使用。
[READ:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6201-6217]

### 2.8 MethodRefResolver.GenericInstantiationName (MethodRefResolver.cs:1772-1830)
同样自己拼写死字面量，与定义侧分叉。
[READ:E:\il2cpp\src\il2cppc\Emit\MethodRefResolver.cs:1772-1830]

### 2.9 StackFold.GenericInstantiationName (StackFold.cs:3729-4327)
同样自己拼写死字面量，与定义侧分叉。
[READ:E:\il2cpp\src\il2cppc\Ir\StackFold.cs:3729-4327]

### 2.10 TypeCppName(string) (CodeGenerator.cs:7296-7298)
另一个独立的 `TypeCppName(string fullName)` 函数在 CodeGenerator 内部，对 `0x12 class` 走 `ReadM3Arg` 的同一张表，且对带 ``` ` ``` 的泛型类型走 `FullNameDefWithArity(fullName) + "_int"`，与 `TypeCppName(TypeDef)` 对同一类型的输出不同。
[READ:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7296-7298]

---

## 3. 修复方案

### 方案一：统一命名源（推荐，根治）

把所有 `ReadM3Arg` / `GenericInstantiationName` / `TypeCppName(string)` 中对 `0x02-0x09/0x0A-0x0B/0x0C-0x0D/0x0E/0x11/0x12/0x1C/0x13/0x1E` 的写死字面量，全部改为委托给单一函数：

```csharp
internal static string ClosureArgShortName(string arg)  // MetadataLoader 已有
```

新增强制委托入口：
```csharp
// TypeEmitter.cs 新增
internal static string ArgShortName(byte typeCode, string fullName) => typeCode switch
{
    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "int",
    0x0A or 0x0B => "long",
    0x0C or 0x0D => "double",
    0x0E => "string",
    0x11 or 0x12 or 0x1C => ClosureArgShortName(fullName),  // 引用类型→object, 值类型→sanitized
    0x13 or 0x1E => "int",  // VAR → 默认 int
    _ => throw new NotSupportedException(...)
};
```

并修改 CodeGenerator.cs:5003、MethodRefResolver.cs:1805、StackFold.cs:4357 三处 `ReadM3Arg` 的 0x11/0x12 分支调用此函数。

### 方案二：TypeCppName 编码实参（plan.L2carrier.md 2.1 方案）

在泛型定义的 C++ 名中编码实参，而非无信息的 `_int`：
```
TypeCppName(t) := Sanitize(t.FullName) + AritySuffix + DisambigSuffix
  AritySuffix = t.GenericArity>0 ? $"_{EncGenericArgs(t)}" : ""
  EncGenericArgs = 对每个 GenericArgs[i] 取短码并 "_" 拼接
```

该方案改动大（所有 TypeCppName 调用点 ≥12 处），但可彻底消歧。

### 方案三：生成期护栏 + LOUD 检测（plan.L2carrier.md 2.2，防回归护栏）

最小改动、零破坏：
```csharp
private static readonly HashSet<string> _emittedStructNames = new(StringComparer.Ordinal);
public static string EmitClassStruct(TypeDef t, ...)
{
    var name = TypeCppName(t);
    if (!_emittedStructNames.Add(name))
    {
        Console.Error.WriteLine($"L2 carrier COLLISION: struct {name} already emitted");
        name = $"{name}_{SigHash8(t)}";  // 消歧
    }
    ...
}
```

并在 `EmitClassStruct` 内校验字段布局，真碰撞升级 error。

---

## 4. 落地建议（次序）

1. **方案三 立即落地**（零破坏护栏，防止回归扩大）
2. **方案一 次之**（统一引用侧与定义侧的命名，消除 undeclared 根因）
3. **方案二 远期**（编码实参，彻底消歧，需全量重生）

---

## 5. 预期消除行数（方案一落地后）

| 错误族 | 当前行数 | 消除后 |
|--------|---------|--------|
| redefinition | ~9318 | 0 |
| undeclared (L2-derived) | ~3300 | <50 |
| 全树 1553 TU 失败 | 1553 | <50 |

方案一消除命名叉乘后，定义侧与引用侧符号一致，全树 redefinition 归零；剩余 <50 TU 失败为 per-file 真 bug（与 L2 无关）。

---

## 6. 未验证 / 风险

- 当前 r78 旧树已被主代理重建为 21:54 空树，所有数字为 rebuild 前证据，需 r79+ 新树复测印证
- 方案一修改 ReadM3Arg 的引用类型路径后，需回归 165/175 runtime + 610 icall audit
- MethodMap 专线索引（listi4/listobj/dictsi）是否对 C++ 名长度敏感需验证
