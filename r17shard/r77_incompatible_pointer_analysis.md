# r77 Incompatible Pointer 错误分析报告

**日期**: 2026-09-06  
**数据源**: `E:\il2cpp\r77shard\*.csv` (3片, 共95个错误)  
**目标**: 分析指针类型不匹配模式, 定位根因

---

## 一、总体统计

| 类别 | 数量 | 占比 |
|------|------|------|
| **指针-整数转换** (INT_CONV) | 39 | 41% |
| **指针-指针类型不匹配** (PTR_TYPE) | 56 | 59% |
| **合计** | **95** | **100%** |

---

## 二、Top 10 出错指针类型组合

| 排名 | 目标类型 | 源类型 | 次数 | 错误类别 |
|------|----------|--------|------|----------|
| 1 | `System_Threading_StackCrawlMark *` | `int32_t *` | **21** | PTR_TYPE |
| 2 | `int32_t` | `Il2CppObject *` | **20** | INT_CONV |
| 3 | `int32_t` | `int32_t *` | **12** | INT_CONV |
| 4 | `System_Collections_Generic_KeyValuePair_2_object_object *` | `System_Collections_Generic_KeyValuePair_2_int *` | **3** | PTR_TYPE |
| 5 | `System_RuntimeType *` | `Il2CppObject *` | **2** | PTR_TYPE |
| 6 | `Microsoft_Win32_SafeHandles_SafeWaitHandle *` | `Il2CppObject *` | **2** | PTR_TYPE |
| 7 | `System_Threading_ManualResetEvent *` | `Il2CppObject *` | **2** | PTR_TYPE |
| 8 | `System_Threading_ThreadPoolWorkQueue_QueueSegment *` | `Il2CppObject *` | **2** | PTR_TYPE |
| 9 | `System_Threading_Timer *` | `Il2CppObject *` | **2** | PTR_TYPE |
| 10 | `Il2CppObject *` | `System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int *` | **2** | PTR_TYPE |

---

## 三、错误模式深度分析

### 3.1 模式 #1: StackCrawlMark 指针宽度不匹配 (21次)

**错误特征**:
```
assigning to 'System_Threading_StackCrawlMark *' from 'int32_t *'
```

**出现位置**: 
- `System_Security_SecurityRuntime.cpp:292`
- `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_*.cpp:142` (7个泛型实例化)
- `System_Threading_Tasks_ContinuationTaskFromResultTask_1_*.cpp:143` (3个泛型实例化)

**根因**: 
- `StackCrawlMark` 是值类型 (struct), 但其指针字段 `int32_t *` 被错误地赋给 `StackCrawlMark *`
- 泛型方法 `ContinuationResultTaskFromResultTask`2<T>` 默认 T→int, 但实际应为 `T *` 引用
- locKind 分析把 `StackCrawlMark` 局部变量声明为 `int32_t *` 而非 `StackCrawlMark *`

**修复方向**: 在 `EmitBody` 的 locKind 传播中, 对 `StackCrawlMark` 等特殊 struct 类型保持完整类型名

---

### 3.2 模式 #2: Il2CppObject* → int32_t 整数截断 (20次)

**错误特征**:
```
incompatible pointer to integer conversion assigning to 'int32_t' (aka 'int') from 'Il2CppObject *'
```

**典型代码** (`System_IO_Iterator_1_string.cpp:249`):
```cpp
int32_t t0;
Il2CppObject* t1 = nullptr;
t0 = System_IO_Iterator_1_string_get_Current_...(arg0);  // 返回 int32_t
t1 = static_cast<Il2CppObject*>(t0);  // ← 错误: 指针→整数截断
```

**根因**:
- `Iterator<T>.get_Current()` 返回 T, 泛型默认 T→int, 但实际应为 `Il2CppObject *` (boxed)
- 缺少 box 调用: 应该是 `il2cpp::il2cpp_box_int32(nullptr, t0)` 再赋给 `t1`

**修复方向**: 泛型 T 返回值在引用上下文中应自动 box

---

### 3.3 模式 #3: int32_t* → int32_t 解引用缺失 (12次)

**错误特征**:
```
incompatible pointer to integer conversion assigning to 'int32_t' from 'int32_t *'; dereference with *
```

**出现位置**:
- `Microsoft_Win32_Win32Native_WIN32_FIND_DATA.cpp:91`
- `System_AppContextSwitches.cpp:441`
- `System_Buffer.cpp:113`
- `System_Collections_Concurrent_Partitioner_DynamicPartitionerForIEnumerable_1_*.cpp:283`

**根因**:
- `out int` 参数通过 `int32_t *` 传递, 但赋值时未解引用
- 代码生成把 `out` 参数目标直接赋值为指针, 缺少 `*ptr` 解引用

**修复方向**: `out` 参数的 stloc 目标应为 `*ptr` 而非 `ptr`

---

### 3.4 模式 #4: KeyValuePair<T1,T2> 泛型实例化不匹配 (3次)

**错误特征**:
```
assigning to 'System_Collections_Generic_KeyValuePair_2_object_object *' 
from     'System_Collections_Generic_KeyValuePair_2_int *'
```

**出现位置**:
- `System_Collections_Generic_Dictionary_2_KeyCollection_Enumerator_*_object_object.cpp:164`
- `System_Collections_Generic_Dictionary_2_ValueCollection_Enumerator_object_*_object_object.cpp:164`
- `System_Collections_Generic_List_1_Enumerator_*_object_object.cpp:155`

**根因**:
- `Dictionary<TKey,TValue>.KeyCollection.Enumerator` 是嵌套泛型
- 外层 `Dictionary<object,object>` 的 `KeyCollection` 内部使用 `KeyValuePair<object,object>` 作为 currentKey 类型
- 但默认实例化把 T→int, 生成了 `KeyValuePair<int,int>` 类型指针
- `currentKey` 字段类型是 `KeyValuePair<object,object> *`, 但 IR 中 temp 是 `KeyValuePair<int,int> *`

**修复方向**: 嵌套泛型的 "T→int" 默认实例化必须穿透所有层级, 保持一致

---

### 3.5 模式 #5: 引用类型指针向上转换缺失 (15+ 类型, 各1-2次)

**错误特征**:
```
assigning to 'System_RuntimeType *' from 'Il2CppObject *'
assigning to 'Microsoft_Win32_SafeHandles_SafeWaitHandle *' from 'Il2CppObject *'
```

**涉及类型**:
- `System_RuntimeType`, `System_Threading_Timer`, `System_Threading_ManualResetEvent`
- `Microsoft_Win32_SafeHandles_SafeWaitHandle`, `Microsoft_Win32_SafeHandles_SafeRegistryHandle`
- `System_Collections_Hashtable`, `System_TimeZoneInfo`, `System_Security_PermissionSet`

**根因**:
- 这些类型都是类 (reference type), 赋值需要 `static_cast<Derived*>(base)` 或隐式向上转换
- 但代码生成把 `Il2CppObject *` 直接赋给派生类指针, 缺少 `static_cast`

**修复方向**: 当目标类型是类且源类型是 `Il2CppObject *` 时, 插入 `static_cast<T*>(...)`

---

### 3.6 模式 #6: 值类型指针反向赋值 (5+ 类型)

**错误特征**:
```
assigning to 'Il2CppObject *' from 'System_Collections_Generic_List_1_int *'
assigning to 'Il2CppObject *' from 'System_ArraySegment_1_int *'
```

**根因**:
- 值类型指针赋给 `Il2CppObject *` — 缺少 unbox 或 boxed 引用
- 应该是 `reinterpret_cast<Il2CppObject*>(ptr_to_boxed)` 或 boxed 引用

---

### 3.7 模式 #7: 二级指针类型不匹配 (3次)

**错误特征**:
```
assigning to 'Il2CppObject **' from 'System_Threading_ManualResetEvent **'
assigning to 'Il2CppObject **' from 'System_Threading_SemaphoreSlim **'
assigning to 'Il2CppObject **' from 'System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_CompletionState **'
```

**根因**:
- `out object` 参数声明为 `Il2CppObject **`, 但实际传入的是派生类 `**`
- C++ 中 `Derived**` 不能隐式转换为 `Base**`

---

## 四、根因总结

| 根因类别 | 错误数 | 占比 | 修复优先级 |
|----------|--------|------|------------|
| 泛型 T→int 默认实例化穿透不一致 | ~25 | 26% | **P0** |
| 缺少 box/unbox 调用 | ~22 | 23% | **P0** |
| out 参数解引用缺失 | ~12 | 13% | **P1** |
| 值类型指针类型识别错误 (StackCrawlMark等) | ~21 | 22% | **P1** |
| 类指针向上转换缺少 static_cast | ~15 | 16% | **P2** |

---

## 五、修复方案

### 5.1 P0: 泛型实例化一致性

**位置**: `CodeGenerator.EmitBody` locKind 传播逻辑

**问题**: 泛型方法 `M<T>` 默认 T→int, 但嵌套类型 `T[]`, `Tuple<T>`, `KeyValuePair<K,T>` 等未同步

**修复**: 
1. 在 `FindCarrierClosure` 中, 确保所有 T 的引用点一致替换
2. 在 `TypeCppName` 中添加泛型参数替换的单元测试

### 5.2 P0: Box/Unbox 自动插入

**位置**: `CodeGenerator.EmitExpr` 的 `CallExpr` 分支

**问题**: 泛型返回值 T 在引用上下文中未 box

**修复**:
```csharp
// 当 T 的默认实例化为 int, 但目标需要 Il2CppObject* 时
if (targetKind == "ref" && sourceType == "int32_t") {
    return $"il2cpp::il2cpp_box_int32(nullptr, {sourceExpr})";
}
```

### 5.3 P1: Out 参数解引用

**位置**: `CodeGenerator.EmitExpr` 的 `LocAddrExpr` 分支

**问题**: `out int` 参数的目标 slot 被赋值为指针

**修复**: 检测 `out` 参数并在 stloc 位置添加 `*ptr` 解引用

### 5.4 P1: StackCrawlMark 类型识别

**位置**: `MethodBodyReader.LocalTypeKind` + `CodeGenerator.EmitBody`

**问题**: `StackCrawlMark` 被识别为 `int32_t` 而非 struct 类型

**修复**: 在 `LocalTypeKind` 的 VALUETYPE 分支中, 保持完整 struct 名而非降级为 `int`

---

## 六、预期效果

| 修复 | 消除错误数 | 受影响文件 |
|------|-----------|-----------|
| 泛型一致性 | ~25 | ~20 |
| Box/Unbox | ~22 | ~18 |
| Out 参数解引用 | ~12 | ~10 |
| 值类型识别 | ~21 | ~15 |
| 类指针转换 | ~15 | ~12 |
| **合计** | **~95** | **~50** |

---

## 七、验证清单

1. `dotnet build src/il2cppc` → 0错0警告
2. 重新生成 r77 → incompatible pointer 错误归零
3. 单元测试: `test_linq`, `test_dictionary`, `test_iterator` 全部通过
4. 回归: 不影响现有 StackCrawlMark 相关功能

---

**调查人**: 子代理 (ses_f8d427dafffegHpvvixhOajmiS)  
**状态**: 待实施修复
