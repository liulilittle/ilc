# r77 Dictionary ValueCollection Il2CppArray* 转型错误分析

## 1. 错误概览

r77 中 `System.Collections.Generic.Dictionary`2+ValueCollection` 相关错误共 **223 行**（跨 shard1/2/3），集中于两类文件：

| 类别 | 错误数 | 代表文件 |
|------|--------|----------|
| ValueCollection 主类 | ~60 | `Dictionary_2_ValueCollection_*.cpp` |
| ValueCollection.Enumerator | ~160 | `Dictionary_2_ValueCollection_Enumerator_*.cpp` |

## 2. 错误分类（5 类）

### 类 A：`cannot initialize return object of type 'int32_t' with an rvalue of type 'Il2CppObject *'`

**位置**：`Dictionary_2_ValueCollection_Enumerator_<K,V>.cpp:184`

**根因**：`get_Current()` 方法返回 `int32_t`（ValueType 的 `currentValue` 字段），但方法签名要求返回 `Il2CppObject*`。

```cpp
// 生成的代码（错误）
IL2CPP_NOINLINE int32_t get_Current_6efc183d11b1eaee(
    System_Collections_Generic_Dictionary_2_ValueCollection_Enumerator_int_int* arg0)
{
    return arg0->currentValue;  // int32_t → Il2CppObject* 失败
}
```

**结构定义**（generated.h:65139）：
```cpp
struct ..._Enumerator_int_int {
    Il2CppObject* dictionary;
    int32_t index;
    int32_t version;
    int32_t currentValue;  // ← 应该是 Il2CppObject*
};
```

**对比正确实现**（List.Enumerator）：`current` 字段为 `Il2CppObject*`，因为 `IEnumerator<T>.Current` 需要装箱返回。

### 类 B：`cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *'`

**位置**：`Dictionary_2_ValueCollection_<K,V>.cpp:502,506`

**根因**：`CopyTo` 方法中 `arg1` 声明为 `Il2CppObject*`（来自 `System.Collections.ICollection.CopyTo(r:System.Array,i)`），但传给 `il2cpp_array_get_length` / `il2cpp_array_get_elem_ptr` 时需要 `Il2CppArray*`。

```cpp
// 生成的代码（错误）
if((arg2 > il2cpp::il2cpp_array_get_length(arg1)) == 0LL)  // arg1 是 Il2CppObject*
```

**对比正确实现**：同文件内 `CopyTo_d1f87ecd330a510f` 将 `arg1` 显式转为 `reinterpret_cast<Il2CppArray*>(arg1)`。

### 类 C：`no member named 'entries'/'count'/'hashCode'/'value' in '...ValueCollection_Enumerator_int'`

**位置**：`Dictionary_2_ValueCollection_Enumerator_int.cpp:163-210`

**根因**：泛型特化时，代码生成器用 `..._Enumerator_int*` 对 `dictionary` 字段做 `reinterpret_cast`，但 `entries`/`count` 是 `Dictionary`2_int_int` 的成员，不是 Enumerator 的成员。

```cpp
// 生成的代码（错误）— 把 dictionary 当成 Enumerator 来访问
reinterpret_cast<..._Enumerator_int*>(...->dictionary)->entries
```

应该是：
```cpp
reinterpret_cast<..._Dictionary_2_int_int*>(...->dictionary)->entries
```

这是 StackFold/FieldAccess 解析泛型特化时的指针链断裂。

### 类 D：`incompatible pointer types assigning to 'Il2CppObject *' from 'System_Xxx *'`

**位置**：`Dictionary_2_ValueCollection_Enumerator_object_System_Guid.cpp:164` 等

**根因**：`V:` 值类型字段在泛型特化中使用了未定义的前缀类型名 `V_System_Guid`，而 C++ 中不存在该类型。代码生成器将 `V:` 前缀直接拼接成类型名，但 `V_` 前缀不是合法的 C++ 类型。

```cpp
// 生成的代码（错误）
arg0->currentValue = reinterpret_cast<V_System_Guid*>(...)->value;
// V_System_Guid 不存在，应该是 System_Guid
```

### 类 E：`static_cast from 'Il2CppObject *' to 'int32_t' is not allowed`

**位置**：`Dictionary_2_ValueCollection_int_object.cpp:228` 等

**根因**：`System.Collections.ICollection.CopyTo` 的第二个参数 `arg1`（`System.Array`）被 `il2cpp_is_instance` 检测后赋给 `loc0`，但后续 `loc0` 被当作 `int32_t` 使用（应为 `Il2CppArray*`）。

## 3. 根因分析

### 3.1 核心问题：泛型特化 + ValueCollection 特殊结构

`Dictionary`2+ValueCollection` 是一个嵌套类，其 `Enumerator` 是一个 **ValueType 结构**，包含：
- `dictionary` 字段（引用父 Dictionary）
- `currentValue` 字段（值类型或引用类型）

代码生成器在泛型特化时：

1. **字段类型推断失败**：`currentValue` 的 IL 类型是 `!0`（泛型参数 T），但特化 `Dictionary<int,int>` 时，生成器错误地推断为 `int32_t` 而非 `Il2CppObject*`（因为 `IEnumerator<T>.Current` 在 .NET 中需要装箱）。

2. **字典字段链访问失败**：`arg0->dictionary->entries` 中，`dictionary` 是 `Il2CppObject*`（引用父对象），需要 `reinterpret_cast<Dictionary_2<K,V>*>` 才能访问 `entries`。但生成器使用了 `reinterpret_cast<Enumerator*>`，导致成员查找失败。

3. **值类型前缀 `V:` 未正确展开**：`V:` 是 MethodBodyReader 的值类型局部变量前缀，但在 `reinterpret_cast<V_Xxx*>` 中，`V_Xxx` 不是合法类型名。

### 3.2 影响范围

- 所有 `Dictionary<K,V>` 特化中 `K` 或 `V` 为值类型的组合
- 约 150 个文件受影响（每个特化一个 .cpp 文件）
- 每个文件 1-5 个错误

## 4. 修复方案

### 方案 A（推荐）：修改 CodeGenerator 的泛型特化逻辑

**目标**：让 ValueCollection.Enumerator 的 `currentValue` 字段统一为 `Il2CppObject*`

**位置**：`src/il2cppc/Emit/CodeGenerator.cs` 中生成 ValueType 字段类型推断的部分

**逻辑**：
1. 检测当前类型是否为 `Dictionary`2+ValueCollection+Enumerator<,>`
2. 如果是，将 `currentValue` 字段类型强制设为 `Il2CppObject*`
3. 在 `get_Current()` 中如果 T 是值类型，生成装箱调用 `il2cpp::il2cpp_box_T(...)`

**优点**：从源头修复，覆盖所有泛型特化
**缺点**：需要识别嵌套泛型类型名，逻辑较复杂

### 方案 B：修改 StackFold 的字段链解析

**目标**：修复 `dictionary->entries` 的 `reinterpret_cast` 链

**位置**：`src/il2cppc/Ir/StackFold.cs` 中处理 `ldfld` 的部分

**逻辑**：
1. 当访问 `dictionary` 字段时，记录其实际类型为 `Dictionary<K,V>`
2. 当通过 `dictionary` 访问 `entries` 时，生成 `reinterpret_cast<Dictionary<K,V>*>` 而非 `reinterpret_cast<Enumerator*>`

### 方案 C：修复 `V:` 前缀的类型名展开

**目标**：将 `V_Xxx` 展开为正确的 C++ 类型名（如 `System_Xxx`）

**位置**：`src/il2cppc/Emit/TypeEmitter.cs` 中 `ReferenceCppType` 或 `TypeCppName`

**逻辑**：当类型名前出现 `V_` 前缀时，查找对应的 `System_Xxx` 类型定义

## 5. 建议优先级

1. **P0**：修复 `currentValue` 字段类型（类 A 错误）— 影响 ~60 个文件
2. **P1**：修复 `Il2CppArray*` 转型（类 B 错误）— 影响 ~60 个文件
3. **P1**：修复字段链解析（类 C 错误）— 影响 ~50 个文件
4. **P2**：修复 `V:` 前缀展开（类 D 错误）— 影响 ~30 个文件
5. **P2**：修复 `static_cast` 类型（类 E 错误）— 影响 ~30 个文件

## 6. 证据引用

- [READ:E:\il2cpp\r77shard\r77shard1_shard1_errors.log:16755] 类 A 错误示例
- [READ:E:\il2cpp\r77shard\r77shard1_shard1_errors.log:16984] 类 B 错误示例
- [READ:E:\il2cpp\r77shard\r77shard1_shard1_errors.log:16774] 类 C 错误示例
- [READ:E:\il2cpp\r77shard\r77shard1_shard1_errors.log:16836] 类 D 错误示例
- [READ:E:\il2cpp\r77shard\r77shard1_shard1_errors.log:17001] 类 E 错误示例
- [READ:E:\il2cpp\out\mscorlib_gen_r77\include\internal\generated.h:65139] Enumerator struct 定义
- [READ:E:\il2cpp\out\mscorlib_gen_r77\include\internal\generated.h:24417] Dictionary struct 定义（entries 字段）
