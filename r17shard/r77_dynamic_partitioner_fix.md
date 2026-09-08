# r77 DynamicPartitioner 错误修复方案

## 1. 错误日志分析

### 1.1 所有 DynamicPartitioner 相关错误 (r77 shard1)

共涉及 **12 个文件**，**19 个错误**，分属 3 类根因：

| 类别 | 错误模式 | 文件数 | 错误数 |
|------|----------|--------|--------|
| A - 缺失 `_int` 实例化的嵌套泛型基类 | `undeclared identifier '..._2_int_int__ctor_...'; did you mean '..._2_int__ctor_...'` | 6 | 6 |
| B - `cannot initialize int32_t with Il2CppObject*` | 参数类型不匹配 | 6 | 6 |
| C - `KeyValuePair_2_long_int` 无法转 `Il2CppObject*` | 返回类型不匹配 | 4 | 4 |
| D - `IEnumerator_1_KeyValuePair_2_long_int_class` 未声明 | 缺失泛型实例类 | 2 | 2 |
| E - `int32_t*` 转 `int32_t` | 指针未解引用 | 1 | 1 |

### 1.2 具体错误清单

```
A 类 (缺失泛型实例):
1. DynamicPartitionerForArray_1_int.cpp:71  - DynamicPartitionerForIndexRange_Abstract_2_int_int 缺失
2. DynamicPartitionerForArray_1_InternalPartitionEnumerator_int.cpp:73 - DynamicPartitionEnumeratorForIndexRange_Abstract_2_int_int 缺失
3. DynamicPartitionerForIList_1_int.cpp:71 - DynamicPartitionerForIndexRange_Abstract_2_int_System_Collections_Generic_IList_1_int 缺失
4. DynamicPartitionerForIList_1_InternalPartitionEnumerator_int.cpp:73 - DynamicPartitionEnumeratorForIndexRange_Abstract_2_int_System_Collections_Generic_IList_1_int 缺失
5. DynamicPartitionerForIEnumerable_1_InternalPartitionEnumerator_int.cpp:95 - DynamicPartitionEnumerator_Abstract_2_int_System_Collections_Generic_IEnumerator_1_int 缺失
6. DynamicPartitionerForIEnumerable_1_int.cpp:130 - IEnumerator_1_KeyValuePair_2_long_int_class 缺失

B 类 (cannot init int32_t with Il2CppObject*):
7-12. 各 DynamicPartitioner* .ctor 中传递 Il2CppObject* 给 int32_t 基类参数

C 类 (KeyValuePair 返回类型):
13-16. InternalPartitionEnumerator::get_Current 返回 KeyValuePair_2_long_int 而非 Il2CppObject*

D 类 (IEnumerator class 缺失):
17-18. IEnumerator_1_KeyValuePair_2_long_int_class 未声明

E 类 (指针未解引用):
19. InternalPartitionEnumerable_int::loc0 赋值 int32_t* -> int32_t
```

## 2. 根因分析

### 2.1 根因 A：泛型嵌套类型只生成单一 `_int` 实例化

**问题本质**：
`DynamicPartitionerForIndexRange_Abstract` 是 **泛型嵌套类型**（`Partitioner+DynamicPartitionerForIndexRange_Abstract<TKey, TSource>`），有 2 个泛型参数。当前 M3 实现只为每个泛型定义生成 **单一默认实例化** `*_int`（T -> int32_t）。

**错误机制**：
- 基类 `DynamicPartitionerForIndexRange_Abstract` 生成为 `*_2_int`（只有一个 int 参数）
- 但派生类 `DynamicPartitionerForArray_1_int` 的 ctor 调用的是 `*_2_int_int`（两个 int 参数：TKey=int, TSource=int）
- 系统找不到 `*_2_int_int` 这个实例化

**核心矛盾**：
- 泛型定义 `DynamicPartitionerForIndexRange_Abstract<TKey, TSource>` 被多个派生类用不同实参实例化：
  - `<int, int>` (ForArray, ForIList)
  - `<int, IList<int>>` (ForIList)
  - `<int, IEnumerator<int>>` (ForIEnumerable)
- 但 M3 只为定义生成一个 `*_int` 实例（TKey->int, TSource->int）
- 需要 `ExpandOpenInstances` 机制来为每个不同的实参组合生成独立实例化

### 2.2 根因 B：基类 ctor 参数类型不匹配

**问题本质**：
派生类 `.ctor(Il2CppObject* arg0, Il2CppObject* arg1)` 调用基类 ctor 时，基类 ctor 签名是 `.ctor(int32_t, int32_t)`，但传入的是 `Il2CppObject*`。

**错误机制**：
- 生成的代码: `BaseClass__ctor_xxx(arg0, arg1)` 其中 arg0/arg1 是 `Il2CppObject*`
- 但基类 ctor 期望 `int32_t` 参数
- 需要将 `Il2CppObject*` 解箱/转换为 `int32_t`

### 2.3 根因 C：嵌套泛型返回类型未装箱

**问题本质**：
`InternalPartitionEnumerator<TKey, TSource>.get_Current()` 返回 `KeyValuePair<long, int>`（值类型），但重写基类 `IEnumerator.Current` 需要 `Il2CppObject*`。

**错误机制**：
- 基类声明: `virtual Il2CppObject* get_Current()`
- 重写尝试返回: `KeyValuePair_2_long_int`（未装箱）
- 缺少值类型到 Il2CppObject* 的装箱步骤

### 2.4 根因 D：泛型接口实例的 class 未生成

**问题本质**：
`IEnumerator<KeyValuePair<long, int>>` 是泛型接口的实例化，需要生成 `IEnumerator_1_KeyValuePair_2_long_int_class`。

**错误机制**：
- 引用了 `&System_Collections_Generic_IEnumerator_1_System_Collections_Generic_KeyValuePair_2_long_int_class`
- 但这个实例化的 class  descriptor 从未被 emit

## 3. 修复方案

### 3.1 核心修复：实现 ExpandOpenInstances (扩展开放实例化)

**文件**: `src/il2cppc/Emit/TypeEmitter.cs`

**方案**：新增 `ExpandOpenInstances` 方法，扫描所有泛型嵌套类型，为每个不同的实例化实参组合生成独立的 TypeDef。

**实现步骤**：

```csharp
// 1. 在 TypeEmitter.cs 中新增方法
public static void ExpandOpenInstances(MetadataReader md, List<TypeDef> types)
{
    // 扫描所有已展开的嵌套泛型类型
    // 收集它们被实例化的所有实参组合
    // 为每个唯一实例化组合生成独立 TypeDef
}

// 核心算法：
// a. 遍历 types 列表，找出所有 IsNested && GenericArity > 0 的类型
// b. 对每个泛型定义，扫描 metadata 中 MethodSpec/TypeSpec 收集实参组合
// c. 对每个唯一实参组合 (如 <int,int>, <int,IList<int>>)：
//    - 克隆原始 TypeDef
//    - 修改 Name/FullName 为实例化名 (DynamicPartitionerForIndexRange_Abstract_2_int_int)
//    - 解码泛型参数 TKey/TSource 的具体类型
//    - 添加 GenericArgs 属性记录实参类型名
// d. 添加到 types 列表
```

**2. 关键修改：TypeCppName 泛型实例命名**

当前代码 (`TypeEmitter.cs:410-417`):
```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```

需要扩展为支持多参数实例化：
```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    if (t.GenericArity == 0) return baseName;
    // 多参数泛型：使用 GenericArgs 生成 _T1_T2_... 后缀
    if (t.GenericArgs != null && t.GenericArgs.Count > 0)
        return baseName + "_" + string.Join("_", t.GenericArgs.Select(GenericArgTypeName));
    return baseName + "_int"; // 单参数默认
}

private static string GenericArgTypeName(string typeName) => typeName switch
{
    "System.Int32" => "int",
    "System.Int64" => "long",
    "System.String" => "string",
    "System.Object" => "object",
    // ... 其他基本类型
    _ when typeName.StartsWith("System.Collections.Generic.IList`1") => "object",
    _ when typeName.StartsWith("System.Collections.Generic.IEnumerator`1") => "object",
    _ => Sanitize(typeName)
};
```

**3. 关键修改：TypeDef 新增 GenericArgs 属性**

文件: `src/il2cppc/ModelTypeDef.cs`

```csharp
// 在 TypeDef 类中添加:
public List<string>? GenericArgs { get; set; } = null;
// 如果非空，表示这是一个泛型实例化类型，列表中的字符串是实参类型名
```

### 3.2 辅助修复：基类 ctor 参数类型转换

**文件**: `src/il2cppc/Emit/CodeGenerator.cs`

**方案**：在 `EmitIcallCallArgs` 的调用点检测基类 ctor 模式，当检测到：
- 方法名包含 `__ctor_`
- 参数是 `Il2CppObject*` 但目标期望 `int32_t`
- 是 `call` 到非虚方法（基类 ctor 调用）

自动插入 `static_cast<int32_t>(reinterpret_cast<intptr_t>(arg))` 转换。

**实现** (在 `EmitCallStmt` 或 `EmitCall` 中添加检测):

```csharp
// 检测基类 ctor 调用：call SomeType__ctor_xxx(arg0, arg1)
// 其中 arg 是 Il2CppObject* 但目标签名期望 int32_t
if (resolved.CppName.Contains("__ctor_") && args.Count >= 1)
{
    // 检查第一个显式参数（跳过 this）
    var argExpr = args.Count > 1 ? args[1] : args[0];
    var argCode = EmitExpr(argExpr, md, types);
    // 如果参数是 int 常量或 int 局部变量，直接传
    // 如果参数是 Il2CppObject*，需要 unbox
    // 注意：基类 ctor 的 this 是 args[0]，显式参数从 args[1] 开始
}
```

### 3.3 辅助修复：嵌套泛型返回类型装箱

**文件**: `src/il2cppc/Emit/CodeGenerator.cs`

**方案**：在返回值生成时检测：
- 方法签名返回 `Il2CppObject*`
- 实际返回值类型是值类型（KeyValuePair<,> 等）
- 自动插入装箱调用 `il2cpp_box_*(value)`

**实现** (在 `EmitReturn` 或方法体末尾):

```csharp
// 当方法返回 Il2CppObject* 但 return 语句返回值类型时
if (methodReturnType == "Il2CppObject*" && actualReturnType.IsValueType)
{
    // 生成 il2cpp_box_*(value) 替代直接 return value
}
```

### 3.4 辅助修复：泛型接口实例的 class descriptor

**文件**: `src/il2cppc/Emit/TypeEmitter.cs`

**方案**：在 `EmitClassStruct` 中检测泛型接口实例类型，生成对应的 class descriptor（vtable + class 结构）。

**实现**：

```csharp
// 在 GenerateType/GenerateAll 中：
// 如果类型是泛型接口实例（如 IEnumerator<KeyValuePair<long,int>>）
// 生成 class descriptor 供 il2cpp_object_new 使用
if (t.IsGenericTypeInstance && t.IsInterface)
{
    EmitGenericInstanceClassDescriptor(t);
}
```

## 4. 实施优先级

| 优先级 | 修复项 | 预计消除错误数 | 复杂度 |
|--------|--------|----------------|--------|
| P0 | ExpandOpenInstances | 6 (A 类) | 高 |
| P1 | 基类 ctor 参数转换 | 6 (B 类) | 中 |
| P2 | 返回类型装箱 | 4 (C 类) | 中 |
| P3 | 泛型接口 class | 2 (D 类) | 低 |
| P4 | 指针解引用 | 1 (E 类) | 低 |

## 5. 推荐实施顺序

1. **P0 → P3 → P1 → P2 → P4** (按依赖关系)
2. P0 是核心，消除 A 类错误后，B/C 会连带解决（因为正确的实例化类型有正确的签名）
3. P3 (class descriptor) 需要与 P0 配合
4. P1/P2 是通用修复，可能同时解决其他非 DynamicPartitioner 的错误

## 6. 风险与注意事项

1. **命名碰撞**：ExpandOpenInstances 生成的实例化名必须与 MethodRefResolver 中的 `GenericInstantiationName` 一致
2. **循环依赖**：泛型参数可能引用自身（如 `IEnumerator<T>`），需要处理递归
3. **class descriptor 膨胀**：每个泛型实例都需要独立的 class，可能增加 binary 大小
4. **回归风险**：现有 `_int` 单实例化依赖者需要能正确解析到 `*_int` 而非 `*_int_int`

## 7. 验证方案

1. `dotnet build src/il2cppc` → 0 错 0 警告
2. `g++ -fsyntax-only` 扫描 DynamicPartitioner 文件 → 0 不能初始化错误
3. 检查生成的 `*_2_int_int.cpp` 文件存在且包含正确的 class descriptor
4. 全量 mscorlib 构建 → cannot-init 总数下降
