# r77 qualified name 错误分析报告

## 总量统计

| 类别 | 数量 | 说明 |
|------|------|------|
| Total errors | 16653 | 三片 shard 错误行数 |
| undeclared identifier | 872 | "use of undeclared identifier" |
| did you mean | 981 | 编译器建议同名 symbol |
| no known conversion | 7335 | 类型不匹配（int* ↔ struct*） |
| no matching function | 4160 | 函数签名不匹配 |

**三种错误日志文件**：
- `r77shard1_shard1_errors.log` — 6593 errors
- `r77shard2_shard2_errors.log` — 3477 errors
- `r77shard3_shard3_errors.log` — 6583 errors

---

## 错误模式分组统计

### 按类型名后缀分组 (Top 15)

| 排名 | 类型/模式 | 出现次数 | 根因 |
|------|-----------|----------|------|
| 1 | `System_Nullable_1_*` | **1053** | 可空泛型，无 TypeDef，_class 未生成 |
| 2 | `System_RuntimeType_ListBuilder_1_object_*` | **638** | 运行时内部 ListBuilder 类型 |
| 3 | `System_Threading_Tasks_TaskFactory_1___c*` | **752** | TaskFactory 闭包类 (`<>c`) |
| 4 | `V_*_class` | **90** | value-type class 描述符前缀 V_ |
| 5 | `System_Threading_Tasks_Parallel___c__DisplayClass*` | **71** | Parallel 闭包类 |
| 6 | `System_Tuple_2_*` | **36** | Tuple 泛型方法未特化 |
| 7 | `System_Threading_Tasks_GenericDelegateCache*` | **38** | 委托缓存类型 |
| 8 | `System_Lazy_1___c*` | **24** | Lazy 闭包类 |
| 9 | `System_Collections_Concurrent_Partitioner_1_System_Tuple_*` | **4** | 并发分区器 |
| 10 | `SZARRAY_class` | **15** | 单维数组类型描述符 |
| 11 | `System_Object___class` | **12** | 基础类描述符 |
| 12 | `System_Reflection_MemberInfo___class` | **10** | 反射基础类型 |
| 13 | `System_EventHandler_1_int_class` | **8** | 泛型委托 |
| 14 | `System_Collections_Generic_IEnumerable_1_*` | **7** | 泛型接口 |
| 15 | `System_Collections_Generic_KeyValuePair_2_*` | **6** | KVP 泛型 |

---

## Top 10 出错的命名空间/类型名

### 1. `System.Nullable<T>` (1053 处)
```
System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte_get_Value_1fa83d49461a09e3
System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte_get_HasValue_495fb3b18c2b35b9
System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte__ctor_489389d8a76bef54
```
**根因**: `Nullable<T>` 是 BCL 内部类型，无 TypeDef，其 class 描述符 `System_Nullable_1_*_class` 未在 generated.h 中定义。

### 2. `System.RuntimeType+ListBuilder<T>` (638 处)
```
System_RuntimeType_ListBuilder_1_object_get_Count_46255826a76b6404
System_RuntimeType_ListBuilder_1_object_ToArray_ce459a06e5466806
System_RuntimeType_ListBuilder_1_object_Add_c10f6a0e5679f7f2
System_RuntimeType_ListBuilder_1_object_CopyTo_cd64f8a5d3ac9047
System_RuntimeType_ListBuilder_1_object_get_Item_ba00cde5e855f137
```
**根因**: `ListBuilder<T>` 是 `System.RuntimeType` 的嵌套私有类型，用于 `GetTypeArray` 等场景，无 TypeDef。

### 3. `System.Threading.Tasks.TaskFactory+<>c` (752 处)
```
System_Threading_Tasks_TaskFactory_1___c_string_int_sf___9
System_Threading_Tasks_TaskFactory_1___c_object_array_int_sf___9
System_Threading_Tasks_TaskFactory_1___c_object_int_sf___9
```
**根因**: 闭包类 (`<>c`) 是编译器生成的，无 TypeDef。

### 4. `System.Threading.Tasks.Parallel+<>c__DisplayClass*` (71 处)
```
System_Threading_Tasks_Parallel___c__DisplayClass30_0_2_int_int__ForEachWorker_b__0_946714f5294b994d
System_Threading_Tasks_Parallel___c__DisplayClass30_0_2_int_int__ForEachWorker_b__1_2899c10fd4b9b503
System_Threading_Tasks_Parallel___c__DisplayClass31_0_2_int_int__ForEachWorker_b__0_3ee6402898e03278
```
**根因**: Parallel 闭包类有泛型参数，名称中含 `int_int`，但只有 `int` 特化存在。

### 5. `V_*_class` (90 处) — value-type class 描述符
```
V_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode_class
V_System_Collections_DictionaryEntry_class
V_System_Collections_Generic_KeyValuePair_2_System_Int32_System_Diagnostics_Tracing_ManifestBuilder_ChannelInfo__class
V_System_DateTime_class
V_System_DateTimeOffset_class
V_System_Decimal_class
V_System_Diagnostics_Tracing_EventProvider_SessionInfo_class
V_System_Guid_class
V_System_Reflection_Emit_MethodBuilder_SymCustomAttr_class
V_System_Runtime_InteropServices_GCHandle_class
V_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_class
V_System_Security_Cryptography_X509Certificates_AttributeAsn_class
V_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_System_Byte__class
V_System_TimeSpan_class
```
**根因**: `V_` 前缀是 il2cppc 为 value-type class 描述符生成的前缀，但这些类型没有 TypeDef，class 结构体未定义。

### 6. `System.Lazy<T>+<>c` (24 处)
```
System_Lazy_1___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken___cctor_b__27_0_94d7be1dc98cbead
System_Progress_1_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_InvokeHandlers_012a3cd5632cc462
```
**根因**: Lazy 闭包类，无 TypeDef。

### 7. `System.Tuple<T1,T2>` (36 处)
```
System_Tuple_2_int_int_get_Item1_0dd84af6f1518716
System_Collections_Concurrent_OrderablePartitioner_1_System_Tuple_2_int_int__ctor_91c461aebebdb8b7
System_Collections_Concurrent_Partitioner_1_System_Tuple_2_int_int__ctor_91c461aebebdb8b7
System_Collections_Concurrent_OrderablePartitioner_1_System_Tuple_2_long_long__ctor_91c461aebebdb8b7
```
**根因**: `Tuple` 方法名包含泛型参数（如 `System_Tuple_2_int_int`），但 il2cppc 只生成了默认 int 特化（`System_Tuple_2_int`）。

### 8. `System.Threading.Tasks.GenericDelegateCache` (38 处)
```
System_Threading_Tasks_GenericDelegateCache_2___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_int___cctor_b__4_2_f4cd80e72b1137f0
System_Threading_Tasks_GenericDelegateCache_2___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_int___cctor_b__4_3_db5edbe32e14e6c5
```
**根因**: 泛型委托缓存，无 TypeDef。

### 9. `SZARRAY_class` (15 处)
```
SZARRAY_class
array_tolerant_Get_05d667f87c7cc383
array_tolerant_class
```
**根因**: 单维数组类型描述符未定义。

### 10. `System.Collections.Concurrent.Partitioner+DynamicPartitionerForArray` (4 处)
```
System_Collections_Concurrent_Partitioner_DynamicPartitionerForIndexRange_Abstract_2_int_int__ctor_32c6b413cff1025d
System_Collections_Concurrent_Partitioner_DynamicPartitionEnumeratorForIndexRange_Abstract_2_int_int__ctor_8f3b5920a334bb04
```
**根因**: Partitioner 泛型方法/类型只有 int 特化，无 int_int 特化。

---

## 共性根因模式

### 模式 A: 泛型类型/方法特化缺失 (占 ~80%)

IL 中的泛型方法/类型在调用时带有具体的类型参数（如 `Tuple<int,int>`），但 il2cppc 只生成了默认的 `int` 特化，没有生成对应闭合类型的代码。

**典型 signature**:
```
System_Tuple_2_int_int_get_Item1_0dd84af6f1518716    ← 期望
System_Tuple_2_int_get_Item1_0dd84af6f1518716        ← 实际存在
```

**修复方向**:
- 在 `MethodCppNameFromToken` / `TypeCppName` 中增加泛型闭合类型匹配
- 或在 `VirtualTarget` / `MethodRefResolver` 中回退到默认特化

### 模式 B: 编译器生成类型 (无 TypeDef) (占 ~15%)

`<>` 前缀的闭包类（`<DisplayClass>`, `<>c`, `<GetFilterAsTuple>d__7`）没有 TypeDef，但 il2cppc 生成了它们的 class 描述符引用。

**修复方向**:
- 为这些编译器生成类型生成 class 描述符
- 或在 emitter 中识别这些类型并跳过 class 引用

### 模式 C: value-type class 前缀 V_ (占 ~5%)

`V_*_class` 是 il2cppc 为没有 TypeDef 的 value-type 生成的前缀，但 class 结构体定义缺失。

**修复方向**:
- 在 TypeEmitter 中为所有已知 value-type 生成 class 描述符
- 或在 emitter 中回退到 `Il2CppObject*` 当 class 不存在

---

## 受影响文件示例

| 文件 | 错误类型 | 数量 |
|------|----------|------|
| `System_RuntimeType.cpp` | ListBuilder | ~600 |
| `System_Threading_Tasks_Task.cpp` | TaskFactory | ~500 |
| `entry.cpp` | Lazy/Parallel/GenericDelegate | ~200 |
| `System_Environment.cpp` | SZARRAY | ~15 |
| `System_AppDomain.cpp` | EventHandler | ~8 |
| `System_Collections_Concurrent_*.cs` | Partitioner | ~10 |

---

## 修复策略建议

### 短期 (最小改动)
1. 对泛型方法/类型：在 `MethodCppName` 中增加回退逻辑，当闭合类型不存在时使用默认 int 特化
2. 对 V_ class 前缀：生成 stub class 描述符或回退到 `Il2CppObject*`
3. 对 SZARRAY：生成 `SZARRAY_class` 描述符

### 中期
1. 增加泛型闭合类型检测，自动生成对应特化
2. 增加编译器生成类型（`<>` 前缀）的 class 描述符生成

### 长期
1. 完整的泛型闭合类型/方法生成管线
2. 所有 BCL 内部类型（Nullable, Tuple, Lazy 等）的 TypeDef 生成

---

## 结论

r77 的 qualified name 错误 **主要根因** 是：
1. **泛型特化不完整** — 只生成了默认 int 特化，缺少其他闭合类型
2. **编译器生成类型无 TypeDef** — 闭包类、委托缓存等缺少 class 描述符
3. **value-type class 描述符缺失** — V_ 前缀类型未生成 class 结构体

这些错误不影响 il2cppc 编译器本身（编译通过），只影响生成 C++ 代码的编译。修复需要在 `TypeEmitter` / `MethodCppName` / `VirtualTarget` 中增加泛型闭合类型匹配和回退逻辑。
