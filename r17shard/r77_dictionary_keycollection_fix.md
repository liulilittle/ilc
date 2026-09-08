# r77 Dictionary KeyCollection 错误分析报告

## 一、错误统计（r77 shard1 全部 269 个 KeyCollection 错误）

| 错误类型 | 数量 | 典型位置 |
|---------|------|---------|
| `cannot initialize a parameter of type 'Il2CppArray*' with an lvalue of type 'Il2CppObject*'` | 128 | `CopyTo` 方法 L510/L514 |
| `cannot initialize a parameter of type 'Il2CppObject*' with an lvalue of type 'int32_t'` | 64 | `Contains` 方法 L350 |
| `no member named 'count'/'entries'/'key'/'hashCode' in KeyCollection` | 36 | `CopyTo`/`get_Current` |
| `cannot initialize return object of type 'int32_t' with rvalue of type 'Il2CppObject*'` | 46 | `get_Current` L184 |
| **总计** | **274** | 64 个不同文件 |

## 二、根因分析

### 2.1 Il2CppArray* 转型错误（128 个）

**问题代码**（`Dictionary_2_KeyCollection_int_int.cpp:510`）：
```cpp
IL2CPP_NOINLINE void CopyTo(Il2CppObject* arg0, Il2CppObject* arg1, int32_t arg2)
{
    // arg1 声明为 Il2CppObject*，但 icall 需要 Il2CppArray*
    if((arg2 > il2cpp::il2cpp_array_get_length(arg1)) == 0LL) goto L_60;  // ERROR
    t3 = il2cpp::il2cpp_array_get_length(arg1);  // ERROR
}
```

**根因**：`ICollection.CopyTo(Array array, int index)` 的 `array` 参数在方法签名中编码为 `r:System.Array`（→ `Il2CppObject*`），但 `il2cpp_array_get_length`/`il2cpp_array_get_rank`/`il2cpp_array_get_lower_bound` 的运行时签名要求 `Il2CppArray*`。

**正确做法**：在方法入口处插入 `static_cast<Il2CppArray*>(arg1)`。

### 2.2 Contains 方法 int32_t vs Il2CppObject* 错误（64 个）

**问题代码**（`Dictionary_2_KeyCollection_int_int.cpp:350`）：
```cpp
IL2CPP_NOINLINE int32_t Contains(Il2CppObject* arg0, int32_t arg1)
{
    // arg1 是 int32_t，但 icall_dict_si_contains_key 第二个参数需要 Il2CppObject*
    return il2cpp::runtime::icall_dict_si_contains_key(
        reinterpret_cast<Il2CppObject*>(...->dictionary), arg1);  // ERROR
}
```

**根因**：`Contains(TKey key)` 的 `key` 参数是泛型参数 `TKey`（此处 `int` → `int32_t`），但 `ICollection<TKey>.Contains` 的运行时 icall `icall_dict_si_contains_key` 期望 `Il2CppObject*`（因为字典的 key 可能是引用类型）。

### 2.3 no member named 错误（36 个）

**问题代码**（`Dictionary_2_KeyCollection_int.cpp:537-543`）：
```cpp
// KeyCollection 结构体只有 dictionary 字段，没有 entries/count/key/hashCode
loc2 = (...)->count;      // ERROR: no member named 'count'
loc3 = (...)->entries;    // ERROR: no member named 'entries'
```

**根因**：代码直接在 `KeyCollection` 上访问 `count`/`entries`，但这些字段属于 `Dictionary` 类型。正确路径是 `keyCollection->dictionary->count`。

### 2.4 get_Current 返回类型错误（46 个）

**问题代码**（`Dictionary_2_KeyCollection_Enumerator_int.cpp:184`）：
```cpp
IL2CPP_NOINLINE int32_t get_Current(System_Collections_Generic_Dictionary_2_KeyCollection_Enumerator_int* arg0)
{
    return arg0->currentKey;  // ERROR: currentKey 是 Il2CppObject*，函数返回 int32_t
}
```

**根因**：`IEnumerator.get_Current()` 返回 `Il2CppObject*`，但 `IEnumerator<TKey>.get_Current()` 返回 `TKey`。当 `TKey` 是引用类型时，函数签名应为 `Il2CppObject*`，但生成器错误地使用了 `int32_t`。

## 三、修复方案

### 3.1 CopyTo 方法 Il2CppArray* 转型（优先级 P0）

**位置**：`CodeGenerator.cs` `EmitIcallCallArgs` 或 `ParamTypesCpp`

**方案 A（推荐）**：在 `ParamTypesCpp` 中检测 `System.Array` 参数，直接声明为 `Il2CppArray*`：
```csharp
// ParamTypesCpp 中增加：
var arr when arr.StartsWith("r:System.Array", StringComparison.Ordinal) => "Il2CppArray*",
```

**方案 B**：在 `EmitIcallCallArgs` 中对 `il2cpp_array_*` 系列 icall 的参数插入 `static_cast<Il2CppArray*>`。

**方案 C**：在方法体入口处插入转型语句（类似 `NullSafePtrCast` 模式）。

### 3.2 Contains 方法参数转型（优先级 P0）

**位置**：`MethodMap.Collections.cs` 或 `MethodRefResolver`

**方案**：为 `ICollection<TKey>.Contains` 映射的 icall 参数添加 `"a"` cast code（→ `Il2CppArray*`），或对值类型 key 插入 `static_cast<Il2CppObject*>`。

### 3.3 no member named 修复（优先级 P1）

**位置**：`StackFold.cs` 或 `CodeGenerator.cs` FieldAccess

**方案**：修正 `KeyCollection` 的字段访问路径，确保通过 `dictionary` 字段间接访问 `count`/`entries`。

### 3.4 get_Current 返回类型修复（优先级 P1）

**位置**：`CodeGenerator.cs` 返回类型推断

**方案**：当 `TKey` 是引用类型时，`IEnumerator.get_Current()` 应返回 `Il2CppObject*` 而非 `int32_t`。

## 四、影响范围

- **64 个不同文件**受影响（所有 `Dictionary`2_KeyCollection_*` 泛型实例）
- 涉及 `int`, `long`, `string`, `object`, `Guid`, `IntPtr` 等 32+ 种泛型参数组合
- `CopyTo` 方法签名统一，可一次性修复全部 128 个 Il2CppArray* 错误

## 五、验证方法

1. `dotnet build src/il2cppc` → 0 错 0 警告
2. 重新生成 r77 并扫描 KeyCollection 相关错误 → 预期减少 128 个
3. 抽查 `Dictionary_2_KeyCollection_int_int.cpp` 确认 `static_cast<Il2CppArray*>(arg1)` 已插入
