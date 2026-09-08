# r77 DynamicPartitioner 错误分析与修复方案

## 1. 错误日志聚合

r77 三片 shard1 中 DynamicPartitioner 相关错误共涉及 **4 类嵌套类型**（全部源于同一根因）：

| 文件 | 关键错误 |
|---|---|
| `Partitioner_DynamicPartitionerForArray_1_int.cpp` | `undeclared identifier ...DynamicPartitionerForIndexRange_Abstract_2_int_int__ctor_...`（应只有 `_2_int`） |
| `Partitioner_DynamicPartitionerForArray_1_InternalPartitionEnumerator_int.cpp` | `undeclared identifier ...DynamicPartitionEnumeratorForIndexRange_Abstract_2_int_int__ctor_...` + `KeyValuePair<long,int>` 无法转 `Il2CppObject*` |
| `Partitioner_DynamicPartitionerForIEnumerable_1_int.cpp` | `undeclared identifier ...IEnumerator_1_KeyValuePair_2_long_int_class` |
| `Partitioner_DynamicPartitionerForIList_1_int.cpp` | `undeclared identifier ...DynamicPartitionerForIndexRange_Abstract_2_int_System_Collections_Generic_IList_1_int__ctor_...`（混入了泛型参数） |

## 2. 根因分析

### 2.1 嵌套类型未加载

`MetadataLoader.cs:34`：

```csharp
if (td.IsNested) continue; // M2 processes only top-level types (nested types in M3+)
```

`Partitioner.DynamicPartitionerForArray`1<T>`、`Partitioner.DynamicPartitionerForIEnumerable`1<T>`、`Partitioner.DynamicPartitionerForIList`1<T>` 及其 `InternalPartitionEnumerator` 嵌套类全部被跳过，导致 `TypeDef` 集合中不存在这些类型。

### 2.2 构造函数签名错位（2-arg vs 1-arg）

- `DynamicPartitionerForIndexRange_Abstract`2<T,TOnly>` 实际只定义了 `.ctor(int)`（单参数 `m_data`）。
- 其子类 `DynamicPartitionerForArray`1<T>` 的 `.ctor(i[])` IL 调用的是 **自己的** 2-arg `.ctor(int,int)`（`startIndex`, `count`），但这个 `.ctor` 在元数据中属于子类自身，却未被生成。
- 结果：`MethodRefResolver` 回退到基类的 `.ctor(int)`，把 2 个 int 参数全部传过去 → C++ 端 `cannot initialize int32_t from Il2CppObject*`（第二个 arg 被当作 ref）。

同样 `InternalPartitionEnumerator` 的 3-arg `.ctor(int,int,int)` 也调用不存在的基类 2-arg 版本。

### 2.3 `KeyValuePair<long,int>` 未生成

`GetOrderableDynamicPartitions_Factory` 返回 `IEnumerable<KeyValuePair<long,int>>`，但模型只为 `KeyValuePair<int,object>` 等生成了具体类型。`KeyValuePair<long,int>` 是 16 字节结构（8+8），而方法期望返回 `Il2CppObject*` → 没有 boxing 路径。

## 3. 修复方案

### 方案 A（最小改动，推荐）

**Step 1** — 在 `MetadataLoader.cs` 中保留嵌套类型加载路径（去掉 `td.IsNested` 跳过），但仅在父类型已被加载时才加入：

```csharp
if (td.IsNested)
{
    var enclosing = md.GetDefinitionHandle(td.GetDeclaringType()); // ECMA-335 II.23.2.6
    if (!enclosing.IsNil && knownEnclosing.Add(GetEnclosingName(md, td)))
        LoadNestedType(md, td, types);
    continue;
}
```

**Step 2** — `MethodRefResolver` 中 `ResolveMemberReference` 当 parent 是 nested type 时，用 `TypeDefinition` 的短名（`DynamicPartitionerForArray`1`）+ 父名前缀拼接。

**Step 3** — `EnsureInt32DefaultInstantiation` 对 `DynamicPartitionerForArray`1<T>` 的构造函数调用，在 `genArgs` 中插入泛型实参（`T→int`）后再解析基类 `.ctor`。

**Step 4** — 对 `KeyValuePair<long,int>` 按现有 `KeyValuePair` 模板实例化规则补全（`MethodRefResolver` 的 `GenericInst` 路径已支持 0x0A int64 + 0x08 int32 组合）。

### 方案 B（补丁式，不动 MetadataLoader）

在 `TypeEmitter.EmitClassStruct` 之后添加 **nested-type 二次扫描**：对每个已加载顶层类型的 `TypeDef`，调用 `md.GetTypeDefinition(tdh).GetNestedTypes()` 补录模型，再执行 `LayoutInstanceFields` 与 `CodeGenerator.Generate`。

### 方案 C（仅修 C++ 端）

- 在 `MethodRefResolver` 找不到 `.ctor(int,int)` 时，自动折叠为 `.ctor(int)` 并丢弃多余参数（类似 base-call 的 this-only 形式）。
- 对 `KeyValuePair<long,int>` 返回值，在 `CodeGenerator.EmitIcallCallArgs` 添加 `boxed` cast 包装，等价于 `il2cpp_box_pointer(&t3)`。

## 4. 影响评估

- 方案 A 最彻底，但影响面大（所有嵌套类型都会加载，包括状态机类型）。
- 方案 B 是 A 的保守版，只补 mscorlib 用到的 nested partitioner。
- 方案 C 能快速让 DynamicPartitioner 编译通过，但会让基类 `.ctor` 参数不匹配问题隐藏（可能运行时错误）。

## 5. 推荐步骤

1. 先执行 **方案 C** 让 DynamicPartitioner 四个文件编译通过（快速止血）。
2. 再执行 **方案 A** 全面解决嵌套类型加载。
3. 回归测试：`System.Collections.Concurrent.Partitioner` 在 .NET 中Dynamic partitioner 的 `GetOrderableDynamicPartitions` 行为。

## 6. 预期收益

- `Partitioner.DynamicPartitionerForArray/IEnumerable/IList` 全部生成正确构造函数签名。
- `InternalPartitionEnumerator` 的 3-arg `.ctor` 正确调用 3-arg 基类版本。
- `KeyValuePair<long,int>` 作为值类型被 boxed 后返回，消除 `cannot initialize Il2CppObject* from int/struct` 错误。

## 7. 实施文件

- `src/il2cppc/MetadataLoader.cs` — 嵌套类型加载。
- `src/il2cppc/Emit/MethodRefResolver.cs` — 构造函数签名修正 + 泛型参数传递。
- `src/il2cppc/Emit/CodeGenerator.cs` — `EmitIcallCallArgs` boxed cast 包装。
- `.opencode/backups/` — 修改前备份（W3）。

---

**生成**: 2026-09-06 子代理分析
**引用**: `r77shard/shard1_errors.log` L1485-1518, `MetadataLoader.cs:34`, `MethodRefResolver.cs:250-335`
