# r77 ConcurrentDictionary`2 错误修复方案

**生成时间**: 2026-09-06  
**目标**: `System.Collections.Concurrent.ConcurrentDictionary<,>` 泛型实例

---

## 1. 错误统计

| 错误类型 | 数量 |
|---------|------|
| `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'` | 153 |
| `no viable overloaded '='` | 96 |
| `cannot initialize parameter of type 'Il2CppObject *' with lvalue of type 'int32_t'` | 54 |
| `no matching function for call to 'Volatile_Read/Write_*'` | 51 |
| `no member named 'm_locks'` | 26 |
| `no member named 'm_next'` | 21 |
| `no member named 'm_buckets'` | 18 |
| `no member named 'm_countPerLock'` | 13 |
| `no member named 'm_value'` | 12 |
| `no member named 'm_key'` | 12 |
| `no viable conversion from 'TaskAwaiter_1_int' to 'int32_t'` | 26 |
| **总计** | **947** |

---

## 2. 根因分析

### 2.1 结构体定义不完整

`generated.h` 中 `ConcurrentDictionary_2_int` 只有 6 个字段：

```cpp
struct System_Collections_Concurrent_ConcurrentDictionary_2_int {
    Il2CppObject _obj;
    Il2CppObject* m_tables;      // slot 1
    Il2CppObject* m_comparer;    // slot 2
    int32_t m_growLockArray;     // slot 3
    int32_t m_keyRehashCount;    // slot 4
    int32_t m_budget;            // slot 5
    Il2CppObject* m_serializationArray; // slot 6
    int32_t m_serializationConcurrencyLevel; // slot 7
    int32_t m_serializationCapacity; // slot 8
};
```

但生成的方法体试图访问：`m_locks`, `m_buckets`, `m_countPerLock`, `m_key`, `m_value`, `m_next`。

### 2.2 泛型展开问题

`ConcurrentDictionary<TKey, TValue>` 是复杂泛型类，包含：
- 内部 `Node` 类（链表节点，含 `m_key`, `m_value`, `m_next`, `m_hashcode`）
- `m_buckets` 数组（Node 数组）
- `m_locks` 数组（锁数组）
- `m_countPerLock` 数组

这些内部类型未随泛型实例一起生成。

### 2.3 值类型约束

- `m_key` 字段在 `Node` 结构中，类型应为 `TKey`
- `m_value` 字段在 `Node` 结构中，类型应为 `TValue`
- 当前 `Node` 类型未生成

---

## 3. 修复方案

### 方案 A: 补全内部类型定义（推荐）

为每个 `ConcurrentDictionary<TKey, TValue>` 实例生成：
1. `ConcurrentDictionary_2_KeyCollection<TKey, TValue>` 内部类型
2. `ConcurrentDictionary_2_ValueCollection<TKey, TValue>` 内部类型
3. `ConcurrentDictionary_2_Node<TKey, TValue>` 内部类型（含 m_key, m_value, m_next, m_hashcode）
4. `ConcurrentDictionary_2_DictionaryEnumerator<TKey, TValue>` 内部类型

### 方案 B: 补全字典主体字段

在 `ConcurrentDictionary_2_int` 结构体中添加缺失字段：

```cpp
struct System_Collections_Concurrent_ConcurrentDictionary_2_int {
    Il2CppObject _obj;
    Il2CppObject* m_tables;           // slot 1
    Il2CppObject* m_comparer;         // slot 2
    int32_t m_growLockArray;          // slot 3
    int32_t m_keyRehashCount;         // slot 4
    int32_t m_budget;                 // slot 5
    Il2CppObject* m_serializationArray; // slot 6
    int32_t m_serializationConcurrencyLevel; // slot 7
    int32_t m_serializationCapacity;  // slot 8
    // 新增字段
    Il2CppObject* m_locks;            // slot 9 - 锁数组
    Il2CppObject* m_buckets;          // slot 10 - Node 数组（实际上是 m_tables 的内部字段）
    int32_t* m_countPerLock;          // slot 11 - 每锁计数数组
};
```

**问题**: `m_buckets` 实际是 `m_tables` 的内部字段，不是顶层字段。此方案治标不治本。

### 方案 C: 修复 Volatile_Read/Write 签名

```csharp
// MethodMap.cs 中需要添加 Volatile.Read/Write 的映射
// 当前签名不匹配导致 "no matching function" 错误
("System.Threading.Volatile", "Read", "T&(r:T)") => "il2cpp::il2cpp_volatile_read",
("System.Threading.Volatile", "Write", "v(T&,T)") => "il2cpp::il2cpp_volatile_write",
```

---

## 4. 推荐修复步骤

### 步骤 1: 添加 Node 类型展开

在 `MetadataLoader.cs` 中，当展开 `ConcurrentDictionary` 时，同时展开其嵌套类型：

```csharp
// 展开嵌套类型
var nestedTypes = typeDef.GetNestedTypes();
foreach (var nested in nestedTypes)
{
    // 生成 Node<TKey, TValue>, KeyCollection, ValueCollection 等
}
```

### 步骤 2: 修复字段生成顺序

确保 `m_tables` 类型的内部字段（`m_buckets`, `m_locks`, `m_countPerLock`）也被生成。

### 步骤 3: 修复 Volatile 方法映射

```csharp
// MethodMap.Threading.cs 或 MethodMap.Concurrent.cs
("System.Threading.Volatile", "Read", "T&(r:T)") => "il2cpp::il2cpp_volatile_read_ref",
("System.Threading.Volatile", "Write", "v(T&,T)") => "il2cpp::il2cpp_volatile_write_ref",
```

---

## 5. 影响评估

| 修复项 | 难度 | 影响范围 |
|--------|------|----------|
| Node 类型展开 | 中 | 所有泛型字典类型 |
| 嵌套类型展开 | 中 | 所有含嵌套类型的泛型类 |
| Volatile 方法签名 | 低 | Threading 相关代码 |
| 字段对齐修复 | 低 | 结构体布局 |

---

## 6. 结论

**根因**: `ConcurrentDictionary<TKey, TValue>` 的嵌套类型（Node, KeyCollection, ValueCollection）未随泛型实例一起展开，导致生成的代码引用了不存在的字段。

**修复优先级**:
1. 高: 添加 Node<TKey, TValue> 类型生成
2. 中: 修复 Volatile_Read/Write 方法签名
3. 低: 添加 KeyCollection/ValueCollection 类型

---

*报告结束*
