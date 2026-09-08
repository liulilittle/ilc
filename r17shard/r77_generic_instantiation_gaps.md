# r77 Undeclared 错误：泛型实例化缺失分析

## 概述

- **总 undeclared 错误**: 872
- **唯一 undeclared 符号**: 294
- **泛型相关 undeclared 错误**: 531 (占 60.9%)
- **唯一泛型基类型**: 49 个

---

## Top 10 泛型类型（按缺失实例化错误数排序）

| 排名 | 泛型基类型 | 缺失实例化错误数 | 主要缺失的实例化 |
|------|-----------|----------------|----------------|
| 1 | `System_Threading_Tasks_TaskFactory` | 167 | `_int_`, `_string_`, `_object_`, `_VoidTaskResult_`, `_Task_array_` |
| 2 | `System_RuntimeType_ListBuilder` | 100 | `_object_` (79x), `_int_` 已存在 |
| 3 | `System_Nullable` | 36 | `_ReadOnlyMemory_1_byte_` (26x), `_ArraySegment_1_byte_` (4x) |
| 4 | `System_Array_FunctorComparer` | 29 | `_ReadOnlyMemory_1_int_`, `_Decimal_`, `_EventRegistrationToken_`, `_SessionInfo_` |
| 5 | `System_Diagnostics_Tracing_TraceLoggingTypeInfo` | 20 | `_array_` (14x), `_intptr_` (2x), `_uintptr_` (2x), `_Nullable_1_int_`, `_KeyValuePair_2_int_int_` |
| 6 | `System_Runtime_CompilerServices_ConditionalWeakTable` | 19 | `_int_`, `_object_`, `_object_Dictionary<...>_` |
| 7 | `System_Threading_Tasks_Task` | 16 | `_array_`, `_object_`, `_string_`, `_Task_array_` |
| 8 | `V_System_Collections_Generic_KeyValuePair` | 12 | `_Int32_ChannelInfo_`, `_Object_Object_` (class 描述符) |
| 9 | `System_Threading_Tasks_GenericDelegateCache` | 10 | `_int_`, `_EventRegistrationToken_int_` |
| 10 | `System_Threading_Tasks_ContinuationResultTaskFromResultTask` | 10 | `_object_array_int_`, `_object_int_`, `_string_int_`, `_VoidTaskResult_int_` |

---

## 详细分析

### 1. System_Threading_Tasks_TaskFactory (167 错误)

最严重的缺失。编译器建议显示只有 `int` 实例化存在，但代码引用了多种实例化：

| 缺失实例化 | 错误数 | 编译器建议（已存在的实例化） |
|-----------|--------|---------------------------|
| `_int_sf_s_completeFromAsyncResult` | 12 | 仅 int 存在 |
| `_string_int_sf___9` | 5 | 无建议 |
| `_VoidTaskResult_int_sf___9` | 5 | 无建议 |
| `_object_int_sf___9` | 5 | 无建议 |
| `_Task_array_int_sf___9` | 5 | 无建议 |

**根因**: `TaskFactory<T>.FromAsyncImpl` 等方法只在 `T=int` 时 emit，但 IL 引用了 `T=string, object, VoidTaskResult, Task[]` 等情况。

### 2. System_RuntimeType_ListBuilder (100 错误)

全部是 `_object_` 实例化缺失：

| 缺失方法 | 错误数 |
|---------|--------|
| `ListBuilder<object>.get_Count` | 34 |
| `ListBuilder<object>.ToArray` | 25 |
| `ListBuilder<object>.Add` | 18 |
| `ListBuilder<object>.CopyTo` | 12 |
| `ListBuilder<object>..ctor` | 6 |

**根因**: `ListBuilder<int>` 已 emit，但 `ListBuilder<object>` 未 emit。编译器建议总是指向 `ListBuilder<int>`。

### 3. System_Nullable (36 错误)

复杂的嵌套泛型实例化：

| 缺失实例化 | 错误数 |
|-----------|--------|
| `Nullable<ReadOnlyMemory<byte>>.get_Value` | 14 |
| `Nullable<ReadOnlyMemory<byte>>.get_HasValue` | 12 |
| `Nullable<ReadOnlyMemory<byte>>..ctor` | 5 |
| `Nullable<ArraySegment<byte>>.get_HasValue` | 2 |
| `Nullable<ArraySegment<byte>>.get_Value` | 2 |

**根因**: `Nullable<T>` 的 emit 只处理了简单类型（int, DateTime 等），但 IL 引用了 `Nullable<ReadOnlyMemory<byte>>` 这种深层嵌套泛型。

### 4. System_Diagnostics_Tracing_TraceLoggingTypeInfo (20 错误)

数组和非 int 类型实例化缺失：

| 缺失实例化 | 错误数 |
|-----------|--------|
| `TraceLoggingTypeInfo<T[]>` (array of T) | 14 |
| `TraceLoggingTypeInfo<IntPtr>` | 2 |
| `TraceLoggingTypeInfo<UIntPtr>` | 2 |
| `TraceLoggingTypeInfo<Nullable<int>>` | 1 |
| `TraceLoggingTypeInfo<KeyValuePair<int,int>>` | 1 |

### 5. System_RuntimeType_ListBuilder 的 V_ 前缀错误 (90 错误)

`V_` 前缀表示值类型的 class 描述符：

| 缺失 V_ 符号 | 错误数 |
|-------------|--------|
| `V_System_Collections_Generic_KeyValuePair_*` | 12 |
| `V_System_TimeSpan_class` | 6 |
| `V_System_Decimal_class` | 6 |
| `V_System_Collections_DictionaryEntry_class` | 6 |
| `V_System_Diagnostics_Tracing_EventProvider_SessionInfo_class` | 6 |

**根因**: 值类型 `V_` 描述符只在类型是"生成的 struct"时 emit，但 KeyValuePair、TimeSpan 等没有生成 struct 或描述符被跳过。

---

## 根因总结

### 核心问题：默认实例化只生成 int

转译器的泛型处理策略是**只 emit `int`（默认）实例化**，但 mscorlib IL 大量引用其他实例化：

1. **TaskFactory<T>**: 需要 T=int, string, object, VoidTaskResult, Task[] 等
2. **ListBuilder<T>**: 需要 T=int, object
3. **Nullable<T>**: 需要 T=int, DateTime, ReadOnlyMemory<byte>, ArraySegment<byte>
4. **ConditionalWeakTable<TKey,TValue>**: 需要 (int,int), (object,object), (object,Dictionary<...>)
5. **TraceLoggingTypeInfo<T>**: 需要 T=int, T=int[], T=IntPtr, T=Nullable<int>, T=KeyValuePair<int,int>

### 编译器建议的模式

所有 `did you mean` 建议都指向 `_int_` 实例化，证实只有 int 版本被 emit：
- `ListBuilder<object>::Add` → `ListBuilder<int>::Add`
- `TaskFactory<string>::...` → `TaskFactory<int>::...`
- `Nullable<ReadOnlyMemory<byte>>.get_Value` → `Nullable<ReadOnlyMemory<int>>.get_Value`

---

## 修复建议

1. **MethodSpec 实例化扫描**: 在 Generate 之前扫描所有 MethodSpec 引用，为每个被引用的泛型方法 emit 对应实例化的 struct 和方法体。

2. **TypeSpec 实例化扫描**: 扫描所有 TypeSpec 引用（如 `Nullable<ReadOnlyMemory<byte>>`），确保每个被引用的泛型类型都有定义。

3. **V_ 描述符完整 emit**: 为所有值类型 emit V_ 描述符，不仅仅是生成 struct 的类型。

4. **数组实例化**: `TraceLoggingTypeInfo<T[]>` 的 emit 需要特殊处理 SZARRAY 形式。

5. **嵌套泛型展开**: `Nullable<ReadOnlyMemory<byte>>` 需要先 emit `ReadOnlyMemory<byte>` 的定义，再 emit `Nullable<...>` 的定义。

---

## 受影响文件数

泛型实例化缺失影响的文件数量：
- `System_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int.cpp`: 大量 ListBuilder<object> 调用
- `System_Diagnostics_Tracing_*.cpp`: TraceLoggingTypeInfo<T> 各种 T
- `System_Threading_Tasks_*.cpp`: TaskFactory<T>, Task<T>, 各种 T 实例化
- `System_Nullable_*.cpp`: Nullable<复杂嵌套类型>
