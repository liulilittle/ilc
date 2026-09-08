# r77 No-Viable (nv) 错误模式分析

## 总览

| 指标 | 数量 |
|------|------|
| 总 nv 错误 | 2420 |
| Shard 1 | 1526 |
| Shard 2 | 300 |
| Shard 3 | 594 |
| 类型转换错误 | 1047 |
| 赋值过载错误 | 1373 |

## Top 20 类型转换不匹配

| 来源类型 -> 目标类型 | 出现次数 |
|---|---|
| System_Runtime_CompilerServices_TaskAwaiter_1_int -> Il2CppObject * | 226 |
| System_Runtime_CompilerServices_TaskAwaiter_1_int -> int32_t | 73 |
| Il2CppObject * -> System_Guid | 33 |
| il2cpp::runtime::Il2CppTimeSpan -> Il2CppDateTime | 17 |
| System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken -> Il2CppObject * | 12 |
| System_Collections_DictionaryEntry -> Il2CppObject * | 8 |
| System_Threading_Tasks_VoidTaskResult -> int32_t | 7 |
| System_Guid -> const void * | 6 |
| il2cpp::runtime::Il2CppDateTimeOffset -> Il2CppObject * | 6 |
| il2cpp::runtime::Il2CppDateTime -> Il2CppObject * | 6 |
| il2cpp::runtime::Il2CppDecimal -> Il2CppObject * | 6 |
| System_Guid -> Il2CppObject * | 6 |
| il2cpp::runtime::Il2CppTimeSpan -> Il2CppObject * | 6 |
| System_Runtime_Serialization_StreamingContext -> int32_t | 6 |
| System_Reflection_Emit_MethodBuilder_SymCustomAttr -> Il2CppObject * | 6 |
| System_Security_Cryptography_X509Certificates_AttributeAsn -> Il2CppObject * | 6 |
| System_Diagnostics_Tracing_EventProvider_SessionInfo -> Il2CppObject * | 6 |
| System_Collections_Generic_KeyValuePair_2_int -> int32_t | 5 |
| Il2CppObject * -> WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventCacheKey | 4 |
| System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken -> int32_t | 4 |

## 核心模式分析

### 模式 1: TaskAwaiter<T> 转换失败（304 次，占 12.6%）

**根因**：`TaskAwaiter<int>` 是值类型结构体，无法直接转换为 `Il2CppObject*` 或 `int32_t`。
- 编译器无法从 `TaskAwaiter_1_int` 转换到 `Il2CppObject*`（226 次）
- 编译器无法从 `TaskAwaiter_1_int` 转换到 `int32_t`（73 次）

**问题**：在 async/await 状态机中，TaskAwaiter 作为值类型被存储，但某些调用点期望 `Il2CppObject*`。

### 模式 2: 运行时值类型到 Il2CppObject* 的转换（约 50 次）

涉及类型：
- `Il2CppDateTimeOffset`
- `Il2CppDateTime`
- `Il2CppDecimal`
- `Il2CppTimeSpan`

**根因**：这些运行时值类型需要显式装箱（boxing）才能作为 `Il2CppObject*` 使用。

### 模式 3: Guid 相关转换（约 45 次）

- `System_Guid -> const void *`（6 次）
- `System_Guid -> Il2CppObject *`（6 次）
- `Il2CppObject * -> System_Guid`（33 次）

**根因**：Guid 是 16 字节值类型，不能隐式转换为指针。

### 模式 4: 枚举/Token 类型转换（约 22 次）

- `EventRegistrationToken -> Il2CppObject *`（12 次）
- `EventRegistrationToken -> int32_t`（4 次）

**根因**：EventRegistrationToken 是值类型，不能隐式转换为指针或 int32。

### 模式 5: DictionaryEntry 转换（8 次）

- `System_Collections_DictionaryEntry -> Il2CppObject *`

**根因**：DictionaryEntry 是值类型结构体。

### 模式 6: VoidTaskResult 转换（7 次）

- `System_Threading_Tasks_VoidTaskResult -> int32_t`

**根因**：VoidTaskResult 是值类型包装器。

## 赋值过载错误 (1373 次)

大多数赋值过载错误发生在以下场景：
1. 结构体类型之间的赋值没有定义 `operator=`
2. 值类型到指针类型的赋值
3. 不同大小的结构体之间的赋值

## 修复建议

### 1. TaskAwaiter 桥接（最高优先级，影响 304 个错误）

需要识别 TaskAwaiter 作为参数/返回值的调用点，并插入正确的解包逻辑：
- `TaskAwaiter_1_int -> int32_t`：调用 `.GetResult()` 或等价方法
- `TaskAwaiter_1_int -> Il2CppObject*`：需要先解包再装箱

### 2. 运行时值类型装箱（影响约 50 个错误）

对 DateTime/Decimal/TimeSpan/DateTimeOffset 的装箱调用：
- `Il2CppDateTime` 需要调用 `icall_box_datetime`
- `Il2CppDecimal` 需要调用 `icall_box_decimal`
- `Il2CppTimeSpan` 需要调用 `icall_box_timespan`

### 3. Guid 指针桥接（影响约 45 个错误）

Guid 不能直接转为 `const void *`，需要取地址 `&guid`。

### 4. DictionaryEntry 装箱（影响 8 个错误）

DictionaryEntry 需要显式装箱为 Il2CppObject*。

## 文件分布

nv 错误集中在以下几个文件（按出现次数）：
- System_Threading_Tasks_Task_1_*.cpp（TaskAwaiter 相关）
- System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_*.cpp
- System_DateTime_*.cpp
- System_Decimal_*.cpp

## 总结

r77 的 2420 个 nv 错误中，约 90% 可以归为以下几类：
1. **TaskAwaiter<T> 转换失败**（304 次，12.6%）— 需要专门处理 awaiter 解包
2. **运行时值类型（DateTime/Decimal/TimeSpan）装箱**（约 50 次，2%）— 需要插入显式 box icall
3. **Guid 指针桥接**（约 45 次，1.9%）— 需要取地址操作
4. **DictionaryEntry/VoidTaskResult/EventRegistrationToken 等值类型转换**（约 40 次，1.6%）
5. **其他结构体赋值/转换失败**（约 1981 次，82%）— 需要逐个分析具体调用签名

修复优先级：TaskAwaiter > 运行时值类型 > Guid > 其他值类型
