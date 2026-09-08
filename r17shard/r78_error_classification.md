# r78 错误日志扫描与分类报告

**生成时间**: 2026-09-06
**数据源**: `E:\il2cpp\out\r78shard\shard1_errors.log`
**总行数**: 9019 行（其中错误行 8213 行）

---

## 一、错误类型分类统计

| 排名 | 错误类型 | 数量 | 占比 | 典型示例 |
|------|----------|------|------|----------|
| 1 | **redefinition** | 4836 | 58.9% | `redefinition of 'InternalPartitionEnumerable_int'` |
| 2 | **no-matching-function** | 718 | 8.7% | `no matching function for call to 'System_Collections_DictionaryEntry__ctor_...'` |
| 3 | **no-viable-conversion** | 668 | 8.1% | `no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *'` |
| 4 | **cannot-initialize-parameter** | 453 | 5.5% | `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'` |
| 5 | **no-viable-overloaded** | 413 | 5.0% | `no viable overloaded '='` |
| 6 | **no-member-named** | 307 | 3.7% | `no member named 'icall_box_intptr' in namespace 'il2cpp::runtime'` |
| 7 | **cannot-cast** | 153 | 1.9% | `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'` |
| 8 | **cannot-initialize-return** | 133 | 1.6% | `cannot initialize return object of type 'int32_t' with an rvalue of type 'Il2CppObject *'` |
| 9 | **too-many-arguments** | 106 | 1.3% | `too many arguments to function call, expected 2, have 3` |
| 10 | **static_cast-error** | 67 | 0.8% | `static_cast from 'Il2CppObject *' to 'int32_t' is not allowed` |
| 11 | **other** | 359 | 4.4% | `use of undeclared identifier` 等 |

---

## 二、Top 3 高频错误模式

### 🔴 Top 1: redefinition（4836 行，58.9%）

**根因**: `generated.h` 中泛型实例化类型重复定义。M6 模型仅支持 `T->int32/uint32` 的泛型实例化，其他实参类型的泛型实例被跳过，但 `generated.h` 中仍生成了重复的结构声明。

**典型错误**:
```
error: redefinition of 'InternalPartitionEnumerable_int'
error: redefinition of 'InternalPartitionEnumerator_int'
error: redefinition of 'DebugView'
error: redefinition of 'Node_int'
```

**涉及文件**: `generated.h`（头文件重复定义）

**修复方向**: 
- 在 `generated.h` 生成阶段添加去重护栏（`#ifdef` 或 HashSet 去重）
- 或跳过已存在的泛型实例声明

---

### 🟠 Top 2: no-matching-function（718 行，8.7%）

**根因**: 函数调用时参数类型不匹配，C++ 找不到匹配的重载。常见于：
- 值类型（如 `DictionaryEntry`）构造函数参数顺序/类型不匹配
- 泛型实例化后的方法签名与调用点不一致

**典型错误**:
```
error: no matching function for call to 'System_Collections_DictionaryEntry__ctor_e40cd54d123fca5e'
```

**涉及文件**: `Microsoft_Runtime_Hosting_StrongNameHelpers.cpp`, `System_Collections_*.cpp`

**修复方向**: 
- 检查 `DictionaryEntry` 构造函数的参数类型映射
- 确保泛型实例化后的方法签名与调用点一致

---

### 🟡 Top 3: no-viable-conversion（668 行，8.1%）

**根因**: 类型之间无合法转换路径。常见于：
- `TaskAwaiter_1_int` → `Il2CppObject*`（值类型到引用类型无隐式转换）
- `Il2CppObject*` → `System_Guid`（引用类型到值类型无隐式转换）

**典型错误**:
```
error: no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *'
error: no viable conversion from 'Il2CppObject *' to 'System_Guid'
```

**涉及文件**: `System_Runtime_CompilerServices_*.cpp`, `System_Guid.cpp`

**修复方向**: 
- 对 `TaskAwaiter` 等值类型添加显式 `reinterpret_cast<Il2CppObject*>` 桥接
- 对 `System_Guid` 等值类型添加显式 `static_cast` 或 `reinterpret_cast`

---

## 三、高频出错文件 Top 10

| 排名 | 文件 | 错误数 |
|------|------|--------|
| 1 | Microsoft_Win32_Win32Native_DynamicTimeZoneInformation.cpp | 较多 |
| 2 | System_Collections_ArrayList_ReadOnlyList.cpp | 较多 |
| 3 | System_Collections_Concurrent_ConcurrentDictionary_2_Tables_object_object_array.cpp | 较多 |
| 4 | System_Collections_Generic_Dictionary_2_Entry_*.cpp | 较多 |
| 5 | System_Collections_Generic_Dictionary_2_ValueCollection_System_IntPtr_int.cpp | 较多 |
| 6 | System_Collections_Generic_ArraySortHelper_2_int_int.cpp | 较多 |
| 7 | System_Collections_Generic_Comparer_1_long.cpp | 较多 |
| 8 | Microsoft_Win32_SafeHandles_SafeLocalAllocHandle.cpp | 较多 |
| 9 | System_Collections_Generic_Dictionary_2_object_object.cpp | 较多 |
| 10 | System_Action_7_int.cpp | 较多 |

---

## 四、修复优先级建议

| 优先级 | 错误类型 | 预期消除行数 | 修复难度 |
|--------|----------|-------------|----------|
| **P0** | redefinition | ~4836 | 中 — 需修改 generated.h 生成逻辑 |
| **P1** | no-viable-conversion | ~668 | 低 — 添加显式类型转换桥接 |
| **P1** | cannot-initialize-parameter | ~453 | 低 — 添加 reinterpret_cast 桥接 |
| **P2** | no-matching-function | ~718 | 高 — 需逐案分析函数签名 |
| **P2** | no-viable-overloaded | ~413 | 高 — 需逐案分析重载决议 |
| **P3** | no-member-named | ~307 | 中 — 可能需补充 icall 映射 |

---

## 五、与 r77 对比

| 指标 | r77 | r78 | 变化 |
|------|-----|-----|------|
| 总错误行 | ~1441 (shard1) | 8213 | +470% ⚠️ |
| redefinition | 未统计 | 4836 | 新增主导错误 |
| cannot-init | 613 (Top1) | 453 | ↓ 26% ✓ |

**结论**: r78 因泛型实例化生成逻辑变更，引入了大量 `redefinition` 错误。需优先修复 generated.h 去重问题。

---

## 六、证据链

- [READ:E:\il2cpp\out\r78shard\shard1_errors.log:1-9019] 完整错误日志
- [BASH:Select-String error: 分类统计] 各类错误计数
- [BASH:Select-String FILE: 文件统计] 高频出错文件
