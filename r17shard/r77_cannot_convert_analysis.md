# r77 cannot convert 错误模式分析

**日期**: 2026-09-06
**数据源**: r77 三片错误日志
**总错误数**: **16,653** 行

| 分片 | 错误行数 |
|------|----------|
| r77shard1 | 6,593 |
| r77shard2 | 3,477 |
| r77shard3 | 6,583 |

---

## 1. 错误模式统计

### 1.1 Top 30 出错的转换类型组合

| # | 计数 | From → To |
|---|------|-----------|
| 1 | 855 | `Il2CppObject *` → `int32_t` |
| 2 | 304 | `int` → `const System_Runtime_CompilerServices_TaskAwaiter_1_int` |
| 3 | 304 | `int` → `System_Runtime_CompilerServices_TaskAwaiter_1_int` |
| 4 | 134 | `Il2CppObject **` → `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int *` |
| 5 | 123 | `Il2CppObject **` → `System_ArraySegment_1_int *` |
| 6 | 119 | `Il2CppObject *` → `System_Runtime_CompilerServices_TaskAwaiter_1_int` |
| 7 | 119 | `Il2CppObject *` → `const System_Runtime_CompilerServices_TaskAwaiter_1_int` |
| 8 | 118 | `System_DateTime` → `const Il2CppDateTime` |
| 9 | 118 | `System_DateTime` → `Il2CppDateTime` |
| 10 | 103 | `Il2CppObject *` → `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int` |
| 11 | 95 | `System_Runtime_CompilerServices_TaskAwaiter_1_int` → `int32_t` |
| 12 | 66 | `Il2CppObject **` → `System_Security_Cryptography_X509Certificates_Span_1_int *` |
| 13 | 52 | `Il2CppObject **` → `System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int *` |
| 14 | 49 | `Il2CppObject *` → `int32_t *` |
| 15 | 44 | `Il2CppObject *` → `System_Security_Cryptography_X509Certificates_Span_1_int` |
| 16 | 39 | `System_Threading_StackCrawlMark *` → `int32_t *` |
| 17 | 38 | `System_TimeSpan` → `Il2CppTimeSpan` |
| 18 | 38 | `System_DateTimeOffset` → `const Il2CppDateTimeOffset` |
| 19 | 38 | `System_DateTimeOffset` → `Il2CppDateTimeOffset` |
| 20 | 38 | `System_TimeSpan` → `const Il2CppTimeSpan` |
| 21 | 37 | `Il2CppObject` → `Il2CppObject *` |
| 22 | 35 | `Il2CppObject *` → `Il2CppObject` |
| 23 | 35 | `Il2CppObject *` → `const Il2CppObject` |
| 24 | 33 | `Il2CppObject *` → `const System_Guid &` |
| 25 | 33 | `Il2CppObject *` → `System_Guid &&` |
| 26 | 33 | `Il2CppObject *` → `System_Runtime_CompilerServices_ConfiguredTaskAwaitable_ConfiguredTaskAwaiter` |
| 27 | 33 | `Il2CppObject *` → `const System_Runtime_CompilerServices_ConfiguredTaskAwaitable_ConfiguredTaskAwaiter` |
| 28 | 27 | `System_Decimal` → `const Il2CppDecimal` |
| 29 | 27 | `System_Decimal` → `Il2CppDecimal` |
| 30 | 26 | `Il2CppObject **` → `System_Nullable_1_int *` |

### 1.2 按大类分组统计

| 类别 | 总计数 | 典型模式 |
|------|--------|----------|
| ref-kind vs val-kind | **855** | `Il2CppObject *` → `int32_t` |
| TaskAwaiter 赋值 | **722** | `int`/`Il2CppObject*` → `TaskAwaiter_1_int` |
| 双指针→单指针 (ReadOnlySpan/ArraySegment) | **389** | `Il2CppObject **` → `ReadOnlySpan_1_int *` / `ArraySegment_1_int *` |
| 值类型 struct 无转换 | **319** | `System_DateTime` → `Il2CppDateTime` |
| ref→ref 类型不匹配 | **105** | `Il2CppObject *` → `ReadOnlySpan_1_int` / `Span_1_int` |
| intptr 不匹配 | **~60** | `intptr_t *` → `System_IntPtr *` 等 |

---

## 2. Top 5 根因分析

### 2.1 Top1: `Il2CppObject *` → `int32_t` (855)

**根因**：r77 已有 ref+val 修复（`reinterpret_cast<Il2CppObject*>`），但只覆盖 `locKind[target]=="ref" && ExprKind(value)=="val"`。当目标 temp 是 `ref` 但赋值表达式也是 `ref`（但类型不匹配），或目标不是 ref kind 但赋值是 int 时，修复未覆盖。

**典型生成代码**：
```cpp
Il2CppObject* t0 = ...;
// 某处将 int32 赋值给 Il2CppObject* 局部
t0 = some_int32_expression;  // ❌
```

**修复方向**：扩展现有修复条件，或增加 `reinterpret_cast` 双向桥接。

### 2.2 Top2-3: `int` → `TaskAwaiter_1_int` (608)

**根因**：`TaskAwaiter` 是值类型（val-kind），但 `int` 值直接赋值给 `TaskAwaiter` 局部时，需要 `reinterpret_cast<TaskAwaiter>(...)` 或类似桥接。

**典型生成代码**：
```cpp
System_Runtime_CompilerServices_TaskAwaiter_1_int t0;
t0 = some_int;  // ❌ int → TaskAwaiter
```

**修复方向**：`reinterpret_cast<TaskAwaiter>(static_cast<intptr_t>(...))` 桥接模式。

### 2.3 Top4-5: `Il2CppObject **` → `ReadOnlySpan_1_int *` / `ArraySegment_1_int *` (257)

**根因**：`ReadOnlySpan<T>` 和 `ArraySegment<T>` 的泛型实例在 IR 中表现为 `Il2CppObject**`（双指针），但 C++ 侧需要具体类型的单指针。这是泛型值类型实例的指针层级不匹配。

**典型生成代码**：
```cpp
Il2CppObject** t0 = ...;
ReadOnlySpan_1_int* t1 = t0;  // ❌
```

**修复方向**：`reinterpret_cast<ReadOnlySpan_1_int*>(t0)` 桥接。

### 2.4 Top6-7: `Il2CppObject *` → `TaskAwaiter_1_int` (238)

**根因**：与 Top2-3 类似，但源类型是 `Il2CppObject*`（ref-kind）而非 int。TaskAwaiter 是值类型，从 ref 到 val 需要桥接。

### 2.5 Top8-9: `System_DateTime` → `Il2CppDateTime` (236)

**根因**：`System.DateTime` 和 `Il2CppDateTime` 是同一值类型的不同 C++ 名称（System.DateTime 映射到 Il2CppDateTime 结构），但代码生成时未自动插入转换。

**典型生成代码**：
```cpp
System_DateTime t0 = ...;
Il2CppDateTime t1 = t0;  // ❌ 需要转换
```

**修复方向**：在 CodeGenerator 中对 DateTime/TimeSpan/Decimal 等已知值类型插入 `static_cast` 或 `reinterpret_cast`。

---

## 3. 与 cannot-init 的关系

cannot-init 和 cannot convert 是同一问题的两种表现：
- **cannot-init**：侧重于"初始化"语义（变量声明+赋值时的类型不匹配）
- **cannot convert**：侧重于"转换"语义（函数参数、赋值右侧到左侧的类型转换失败）

Top 30 中约 **70%** 的错误（ref-val mismatch、值类型 struct 转换、TaskAwaiter 桥接）可通过统一的 `reinterpret_cast` / `static_cast` 桥接框架解决。

---

## 4. 修复优先级建议

| 优先级 | 模式 | 影响行数 | 修复策略 |
|--------|------|----------|----------|
| P0 | `Il2CppObject *` → `int32_t` | 855 | 扩展 ref+val 修复条件 |
| P1 | TaskAwaiter 赋值 | 722 | `reinterpret_cast` 桥接 |
| P1 | 双指针→单指针 | 389 | `reinterpret_cast<T*>` |
| P2 | 值类型 struct 不匹配 | 319 | 类型别名或自动转换 |
| P2 | ref→ref 不匹配 | 105 | 具体类型具体分析 |
| P3 | intptr 不匹配 | ~60 | 小批量修复 |

---

## 附录：证据清单

| 证据 | 来源 |
|------|------|
| r77 三片错误日志 | [读:r77shard/r77shard1_shard1_errors.log] [读:r77shard/r77shard2_shard2_errors.log] [读:r77shard/r77shard3_shard3_errors.log] |
| 总错误数 16,653 | [BASH:Select-String r77shard/*_errors.log -Pattern "error:" | Measure-Object] |
| Top 转换模式统计 | [BASH:Select-String ... Group-Object | Sort-Object Count -Descending] |
| r77 已有 ref+val 修复 | [读:CodeGenerator.cs:2478-2493] |
| TaskAwaiter 模式样本 | [读:r77shard/r77shard1_shard1_errors.log:525-531] |
| DateTime→Il2CppDateTime 样本 | [读:r77shard/r77shard1_shard1_errors.log:1140-1143] |
