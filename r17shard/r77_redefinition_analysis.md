# r77 生成树 redefinition 错误分析

## 核心发现

**r77 本身无 redefinition 错误**。三片错误日志中 `redefinition` 出现 0 次。

redefinition 错误集中在 **r78**（最新一代），其 generated.h 中嵌套类型折叠到同一 C++ 名导致 ODR 冲突。

---

## r78 redefinition 统计

| 结构体名 | 出现次数 | 根因 |
|----------|----------|------|
| `InternalPartitionEnumerable_int` | 18,636 | 多个外层类嵌套同名类型折叠到同一 C++ 名 |
| `InternalPartitionEnumerator_int` | 18,636 | 同上 |
| `Node_int` | 9,318 | `System.Collections.Concurrent.ConcurrentDictionary` 等嵌套 `Node<int>` |
| `DebugView` | 9,318 | 多个集合类型嵌套同名 `DebugView` 类 |

**总计**: 55,908 行 redefinition 错误。

---

## 根因分析

### 问题机制

TypeEmitter 对嵌套类型使用短名而非全名：

```
System.Collections.Concurrent.ConcurrentDictionary`2<int,int>+Node → Node_int
System.Collections.Generic.SortedSet`1<int>+Node                → Node_int  ← 冲突！
```

### 同类模式

- `DebugView`：几乎所有 BCL 集合都嵌套同名 `DebugView` 类
- `InternalPartitionEnumerable/Enumerator`：PLINQ 分区嵌套类型
- `Node_int`：树状集合（SortedSet、ConcurrentDictionary）的嵌套节点

### r77 vs r78 差异

| 版本 | 嵌套类型处理 | redefinition |
|------|-------------|--------------|
| r77 | 嵌套类型作为外层 struct 的成员内联 | 0 |
| r78 | 嵌套类型提升为 generated.h 顶层 struct | 55,908 |

r78 引入的「嵌套类型独立 struct」优化，对同名嵌套类型未做消歧义，导致 ODR 违反。

---

## 建议修复方向

1. **C++ 命名消歧义**：嵌套类型全名化（`ConcurrentDictionary_2_int_int_Node`）或外层前缀
2. **保持内联**：不要将嵌套类型提升为 generated.h 顶层 struct，继续作为外层 struct 的成员
3. **#pragma once + 头文件守卫**：确保每个嵌套类型只在一个 .cpp 中定义

---

## 抽样证据

```
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:48588:8: error: redefinition of 'Node_int'
 48588 | struct Node_int

E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:49125:8: error: redefinition of 'InternalPartitionEnumerable_int'
 49125 | struct InternalPartitionEnumerable_int
```

---

分析人：子代理
日期：2026-09-06
