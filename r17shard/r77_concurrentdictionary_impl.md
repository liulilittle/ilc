# r77 ConcurrentDictionary`2 membref 错误修复方案

**生成时间**: 2026-09-06  
**目标**: 修复 `ConcurrentDictionary<TKey, TValue>` 的 `no member named` 错误

---

## 1. 核心问题

r77 中 ConcurrentDictionary 相关 membref 错误 112 个：

| 错误 | 数量 |
|------|------|
| `no member named 'm_buckets'` | 18 |
| `no member named 'm_locks'` | 26 |
| `no member named 'm_countPerLock'` | 13 |
| `no member named 'm_key'` | 12 |
| `no member named 'm_value'` | 12 |
| `no member named 'm_next'` | 21 |

---

## 2. 根因

### 2.1 m_tables 类型错误

```cpp
// 当前 generated.h
struct ConcurrentDictionary_2_int {
    Il2CppObject _obj;
    Il2CppObject* m_tables;  // ← 错误！应该是 Tables_int_int*
    // ...
};
```

但生成代码访问：

```cpp
// 生成的方法体
this->m_tables->m_buckets   // ← m_tables 是 Il2CppObject*，没有 m_buckets
this->m_tables->m_locks     // ← 同上
this->m_tables->m_countPerLock // ← 同上
```

### 2.2 实际情况

`m_tables` 在 .NET 中类型是 `Tables<TKey, TValue>`，包含：
- `Node<TKey, TValue>[] m_buckets`
- `int[] m_locks`  
- `int[] m_countPerLock`

所以生成代码假设 `m_tables` 指向一个具体的 `Tables` 结构体，但声明为 `Il2CppObject*`。

---

## 3. 修复方案

### 方案 A: 将 m_tables 声明为具体 Tables 类型（推荐）

```csharp
// TypeEmitter 中：检测 ConcurrentDictionary 并添加 Tables 类型引用
// 生成:
struct ConcurrentDictionary_2_Tables_int_int; // forward decl

struct ConcurrentDictionary_2_int {
    Il2CppObject _obj;
    ConcurrentDictionary_2_Tables_int_int* m_tables; // 具体类型
    // ...
};

struct ConcurrentDictionary_2_Tables_int_int {
    Il2CppObject _obj;
    ConcurrentDictionary_2_Node_int_int* m_buckets; // 或 Il2CppArray*
    int32_t* m_locks;         // 或 Il2CppArray*
    int32_t* m_countPerLock;  // 或 Il2CppArray*
};
```

### 方案 B: 使用 Il2CppArray* 替代（简单修复）

如果 `m_buckets`, `m_locks`, `m_countPerLock` 实际都是数组：

```cpp
struct ConcurrentDictionary_2_int {
    Il2CppObject _obj;
    Il2CppArray* m_tables;   // 改为 Il2CppArray*
    // ...
};
```

生成代码中 `m_tables->m_buckets` 改为 `static_cast<Il2CppArray*>(m_tables)`，通过 `il2cpp_array_get_elem_ptr` 访问。

### 方案 C: 添加类型转换辅助（最小改动）

在生成 `this->m_tables->m_buckets` 时，将 `m_tables` 强转为正确的类型：

```cpp
// 生成:
static_cast<ConcurrentDictionary_2_Tables_int_int*>(
    static_cast<Il2CppObject*>(this->m_tables)
)->m_buckets
```

---

## 4. 推荐修复

**方案 A 是最正确的修复**，因为：
1. 保持类型安全
2. 与 .NET 实际布局一致
3. 不需要运行时类型转换

**实现步骤**:
1. 在 `TypeEmitter.ExpandNestedTypes` 中检测 `ConcurrentDictionary_2_*`
2. 为每个实例生成 `ConcurrentDictionary_2_Tables_*` 结构体
3. 将 `m_tables` 字段类型从 `Il2CppObject*` 改为 `ConcurrentDictionary_2_Tables_*`
4. 同样处理 `Node<TKey, TValue>` 类型

---

*报告结束*
