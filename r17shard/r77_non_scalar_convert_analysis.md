# r77 "cannot convert" 错误分析

## 统计

| 项目 | 数量 |
|------|------|
| shard1 | 75 |
| shard2 | 14 |
| shard3 | 54 |
| **总计** | **143** |

## Top 10 出错的转换类型组合

| 排名 | 源类型 | 目标类型 | 出现次数 | 占总量 |
|------|--------|----------|----------|--------|
| 1 | System_Collections_DictionaryEntry | int32_t | 44 | 30.8% |
| 2 | Il2CppObject | int32_t | 12 | 8.4% |
| 3 | System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken | int32_t | 8 | 5.6% |
| 4 | System_Security_Cryptography_X509Certificates_AttributeAsn | int32_t | 7 | 4.9% |
| 5 | System_Diagnostics_Tracing_EventProvider_SessionInfo | int32_t | 7 | 4.9% |
| 6 | System_Reflection_Emit_MethodBuilder_SymCustomAttr | int32_t | 6 | 4.2% |
| 7 | System_UIntPtr | int32_t | 6 | 4.2% |
| 8 | System_Guid | int32_t | 5 | 3.5% |
| 9 | System_Resources_ResourceLocator | int32_t | 5 | 3.5% |
| 10 | il2cpp::runtime::Il2CppDateTime | int32_t | 4 | 2.8% |

## 错误模式

### 核心模式：stelem/stobj 泛型槽位 int32_t 写回

所有 143 个错误共享同一模式——**泛型方法 List<T> / ConcurrentQueue<T> 等的 set_Item(T value) / Enqueue(T item) 等方法在 T 为值类型时发生转换失败**。

#### 典型错误行（System_Collections_Generic_List_1_System_Collections_DictionaryEntry.cpp:564）：
`cpp
(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(
    reinterpret_cast<Il2CppArray*>(..._items), static_cast<int32_t>(arg1)))
) = static_cast<int32_t>(arg2));   // ← 错误：arg2 是 DictionaryEntry 结构体
`

#### 根因：

1. **泛型共享代码中，值类型 T 的局部变量被声明为 Il2CppObject***（因为泛型实例共享代码，T 在没有特化时被当作引用类型处理）。
2. 但 **数组的 elemSize 按 T 的实际大小分配**（如 DictionaryEntry 占 16 字节，但数组声明为 int32_t 类型的 slot）。
3. stobj IL 指令生成 static_cast<int32_t>(valueTypeVar)，但 valueTypeVar 是结构体类型，编译器无法隐式转换为 int32_t。

#### 另一类（Il2CppObject -> int32_t）：

`cpp
// System_Collections_Concurrent_ConcurrentQueue_1_Segment_object.cpp:275
(*reinterpret_cast<int32_t*>(array_get_elem_ptr(arr, loc1))) 
    = static_cast<int32_t>(loc4);  // loc4 是 Il2CppObject*
`

这里 Il2CppObject* 被传给 static_cast<int32_t>——本质上是把指针值截断为 int32，C++ 编译器拒绝。

### 受影响文件模式

- System_Collections_Generic_List_1_<ValueType>.cpp — set_Item, Insert, Contains 等
- System_Collections_Concurrent_ConcurrentQueue_1_Segment_<T>.cpp — TryDequeue 内部
- System_Collections_Generic_Dictionary_2_<TKey,TValue>.cpp — set_Item
- System_Collections_Generic_ArraySortHelper_2_<T>.cpp — 比较器路径

### 修复方向

需要在 stobj/stelem 发射器中：
1. **识别目标数组 slot 是 int32_t 指针但源是值类型结构体** → 用 memcpy 替代 static_cast。
2. **当源是 Il2CppObject* 但目标是 int32_t** → 按指针截断语义用 einterpret_cast<int32_t>(reinterpret_cast<intptr_t>(ptr))。

