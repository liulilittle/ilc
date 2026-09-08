# r77 Partitioner 闭包类与泛型实例化修复方案

**日期**: 2026-09-06  
**分析范围**: `E:\il2cpp\r77shard` 错误日志中所有 `Partitioner` 相关错误  
**错误总数**: 36 (去重后 28 行唯一错误)  
**影响文件**: 19 个 .cpp 文件

---

## 1. 错误分类

### 1.1 泛型实例化命名不匹配 (12 错误)

**根因**: 代码生成器只生成默认 `_int` 实例化，但引用点需要其他泛型参数组合。

| 错误标识 | 实际生成 | 引用期望 |
|---------|---------|---------|
| `Partitioner_1_System_Tuple_2_int_int__ctor_91c461aebebdb8b7` | `Partitioner_1_int__ctor_91c461aebebdb8b7` | 双 int 参数 |
| `Partitioner_1_System_Tuple_2_long_long__ctor_91c461aebebdb8b7` | `Partitioner_1_int__ctor_91c461aebebdb8b7` | long+long 参数 |
| `DynamicPartitionerForIndexRange_Abstract_2_int_int__ctor_32c6b413cff1025d` | `DynamicPartitionerForIndexRange_Abstract_2_int__ctor_32c6b413cff1025d` | 双 int 参数 |
| `DynamicPartitionEnumeratorForIndexRange_Abstract_2_int_int__ctor_8f3b5920a334bb04` | `DynamicPartitionEnumeratorForIndexRange_Abstract_2_int__ctor_8f3b5920a334bb04` | 双 int 参数 |
| `DynamicPartitionEnumerator_Abstract_2_int_System_Collections_Generic_IEnumerator_1_int__ctor_4deb0357eb272492` | `DynamicPartitionEnumerator_Abstract_2_int__ctor_4deb0357eb272492` | IEnumerator 参数 |
| `StaticIndexRangePartitioner_2_int_int__ctor_f1c58f886ae60f11` | `StaticIndexRangePartitioner_2_int__ctor_f1c58f886ae60f11` | 双 int 参数 |
| `StaticIndexRangePartitioner_2_int_System_Collections_Generic_IList_1_int__ctor_f1c58f886ae60f11` | `StaticIndexRangePartition_1_int_System_Collections_IEnumerator_get_Current_b05d866cba44c78e` | IList 参数 |
| `IEnumerator_1_System_Collections_Generic_KeyValuePair_2_long_int_class` | `IEnumerator_1_System_Collections_Generic_KeyValuePair_2_int_object_class` | long+int 参数 |
| `DisplayClass42_0_2_int_int__PartitionerForEachWorker_b__0_cab179655028cbaa` | `DisplayClass42_0_2_int__PartitionerForEachWorker_b__0_cab179655028cbaa` | 双 int 闭包 |
| `DisplayClass42_0_2_int_int__PartitionerForEachWorker_b__1_1aef8866fe43b0e5` | `DisplayClass42_0_2_int__PartitionerForEachWorker_b__1_1aef8866fe43b0e5` | 双 int 闭包 |

### 1.2 类型转换与参数不匹配 (24 错误)

**根因**: 由于类型名不匹配导致的连锁编译错误。

| 错误类型 | 次数 | 根因 |
|---------|------|------|
| `cannot initialize a parameter of type 'int32_t' with an lvalue of type 'Il2CppObject *'` | 5 | 泛型参数错位 |
| `no viable conversion from returned value of type 'KeyValuePair_2_long_int'` | 5 | 返回类型不匹配 |
| `undeclared identifier 'IEnumerator_1_KeyValuePair_2_long_int_class'` | 3 | 泛型实例缺失 |
| `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'` | 2 | int/ref 混淆 |
| `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'` | 2 | ref/int 混淆 |
| `cannot initialize return object of type 'Il2CppObject *' with an rvalue of type 'int32_t'` | 2 | 返回类型错误 |
| `incompatible pointer to integer conversion` | 2 | 指针/int 转换 |
| `no matching function for call to 'OrderablePartitioner_1_EnumeratorDropIndices_*'` | 3 | 构造函数签名不匹配 |

---

## 2. 根因分析

### 2.1 代码层面根因

**文件: `MetadataLoader.cs:34`**
```csharp
if (td.IsNested) continue; // M2 processes only top-level types (nested types in M3+)
```
- **问题**: 所有嵌套类被跳过，包括闭包类 (`<>c__DisplayClass*`)
- **影响**: 闭包类完全没有被加载到 TypeDef 模型中

**文件: `TypeEmitter.cs:410-416`**
```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```
- **问题**: 只生成默认 `_int` 实例化
- **影响**: `Partitioner<System.Tuple<int,int>>` 生成的引用期望 `_int_int` 但只得到 `_int`

**文件: `MetadataLoader.cs` (LoadAssembly 方法)**
- 缺少对泛型嵌套类的展开逻辑
- 缺少对闭包类 (`<>c__DisplayClass*`) 的特殊处理

### 2.2 架构层面根因

**M3+ 设计缺陷**: 
- M3 任务 7 引入了泛型类型定义，但没有处理嵌套在泛型类中的闭包类
- `TypeCppName` 假设所有泛型类型只需要一个默认实例化 (`_int`)
- 对于 `Partitioner<T>` 这类嵌套类，T 可以是任意类型参数组合，每种组合都需要生成独立的 C++ 代码

**具体案例**:
- `Parallel.PartitionerForEachWorker<TLocalState>` 是泛型方法
- 闭包类 `<>c__DisplayClass42_0_2<TRangeState,TLocalState>` 嵌套在 `Parallel` 中
- 当 `TRangeState=int, TLocalState=int` 时，需要生成 `_int_int` 变体
- 当前只生成 `_int`（单参数默认），导致链接失败

---

## 3. 修复方案

### 3.1 短期修复 (r77 范围内)

#### 方案 A: 扩展 LoadAssembly 处理嵌套类

**位置**: `MetadataLoader.cs:34`

```csharp
// 修改前:
if (td.IsNested) continue;

// 修改后:
if (td.IsNested)
{
    // 闭包类和泛型嵌套类需要加载
    var nestedName = md.GetString(td.Name);
    if (nestedName.StartsWith("<>c__DisplayClass") || 
        nestedName.StartsWith("<>c__Iterator") ||
        IsGenericNestedClosure(td, md))
    {
        // 加载到 DeclaringType 的 NestedTypes 列表
    }
    continue;
}
```

#### 方案 B: 扩展 TypeCppName 支持多参数泛型实例化

**位置**: `TypeEmitter.cs:410-416`

```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    if (t.GenericArity > 0)
    {
        // 如果有 GenericArgs 列表，使用实际参数
        if (t.GenericArgs != null && t.GenericArgs.Length > 0)
        {
            return baseName + "_" + string.Join("_", t.GenericArgs.Select(a => SanitizeGenericArg(a)));
        }
        // 默认 int 实例化
        return baseName + "_int";
    }
    return baseName;
}

private static string SanitizeGenericArg(string arg)
{
    // System.Tuple<int,int> -> int_int
    if (arg.Contains("<")) return Sanitize(arg);
    // 映射到短名称
    return arg switch
    {
        "System.Int32" => "int",
        "System.Int64" => "long",
        "System.String" => "string",
        _ => Sanitize(arg)
    };
}
```

#### 方案 C: 为闭包类生成独立文件

**位置**: `CodeGenerator.cs` (方法循环中)

```csharp
// 在 foreach (var t in types) 循环内
// 为每个闭包类生成独立的 .cpp 文件
foreach (var closureType in t.NestedTypes.Where(n => n.IsCompilerGenerated))
{
    GenerateTypeFile(closureType, outDir, pe, md, types, accessors, ...);
}
```

### 3.2 中期修复 (r78-r79)

#### 方案 D: 完整的泛型闭包类展开

1. 在 `MetadataLoader` 中实现 `ExpandGenericClosure` 方法
2. 在 `TypeDef` 中添加 `NestedTypes` 和 `IsCompilerGenerated` 属性
3. 在 `CodeGenerator` 中为每个闭包实例化生成独立的函数和类

#### 方案 E: TypeCppName 完整重写

```csharp
public static string TypeCppName(TypeDef t)
{
    if (t.GenericArity == 0 && !t.IsGenericClosure) 
        return Sanitize(t.FullName);
    
    var baseName = Sanitize(t.GenericDefName ?? t.FullName);
    
    // 闭包类使用 GenericArgs 中的实际参数
    var args = t.GenericArgs ?? t.GenericDefinitionArgs;
    if (args != null && args.Length > 0)
    {
        var argNames = args.Select(a => MapGenericArgToCppName(a));
        return baseName + "_" + string.Join("_", argNames);
    }
    
    return baseName + "_int"; // 默认值
}
```

### 3.3 长期修复 (r80+)

#### 方案 F: 泛型闭包类的完全元数据驱动

1. 扫描所有 MethodDef 的 IL 代码，识别闭包类引用
2. 按需生成闭包类的实例化变体
3. 建立闭包类到宿主类的映射关系
4. 在 TypeCppName 中查询该映射表

---

## 4. 推荐执行步骤

### 优先级 P0 (立即修复)

1. **修改 `MetadataLoader.cs:34`** — 允许泛型嵌套类和闭包类加载
2. **扩展 `TypeCppName`** — 支持多参数泛型实例化命名
3. **在 `CodeGenerator` 中为闭包类生成独立文件**

### 优先级 P1 (r78)

4. **添加 `TypeDef.NestedTypes` 集合**
5. **添加 `TypeDef.IsCompilerGenerated` 标志**
6. **在 `LoadAssembly` 中递归处理嵌套类**

### 优先级 P2 (r79+)

7. **实现按需泛型实例化展开**
8. **建立闭包类引用扫描机制**

---

## 5. 预期效果

- **r77 Partitioner 错误**: 36 → 0
- **泛型嵌套类覆盖**: 支持 `<TRangeState,TLocalState>` 双参数
- **闭包类生成**: 自动为 `<>c__DisplayClass*` 生成实例化代码

---

## 6. 受影响文件清单

```
src/il2cppc/MetadataLoader.cs    — 嵌套类加载逻辑
src/il2cppc/Emit/TypeEmitter.cs  — TypeCppName 命名
src/il2cppc/Emit/CodeGenerator.cs — 类型文件生成
src/il2cppc/Model/TypeDef.cs     — 添加 NestedTypes 属性
```

---

## 7. 附录: 完整错误列表

[见上文错误分类表]
