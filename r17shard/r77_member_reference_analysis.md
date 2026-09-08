# r77 member reference / no member 错误模式分析

**日期**：2026-09-06
**数据源**：r77shard/r77shard{1,2,3}_shard{1,2,3}_errors.log
**总计**：1632 条（与 r75 持平，Δ=0）

---

## 一、按接收者类型分组统计

| 接收者类型 | 计数 | 错误形态 | 占比 |
|---|---|---|---|
| `int32_t` | 285 | member reference base type 'int32_t' | 17.5% |
| `intptr_t` | 143 | member reference base type 'intptr_t' | 8.8% |
| `Il2CppObject*` | 60 | member reference base type 'Il2CppObject*' | 3.7% |
| `uint8_t[16]` | 33 | member reference base type 'uint8_t[16]' | 2.0% |
| `il2cpp::runtime` 命名空间 | ~291 | no member named 'icall_*' | 17.8% |
| `System_Runtime_CompilerServices_TaskAwaiter_1_int` | ~147 | no member named 'ItemN' | 9.0% |
| `System_Collections_Concurrent_ConcurrentDictionary_2_int` | ~112 | no member named 'm_*' | 6.9% |
| `System_Threading_Tasks_SingleProducerSingleConsumerQueue_1_int` | ~105 | no member named 'm_*' | 6.4% |
| `System_Threading_Tasks_TaskFactory_1_int` | ~80 | no member named 'promise/endAction/endFunction' | 4.9% |
| `il2cpp::runtime::Il2CppDecimal` | ~72 | no member named 'mid/lo/hi' | 4.4% |
| `System_Threading_ThreadLocal_1_int` | ~16 | no member named 'Value' | 1.0% |
| `System_Collections_Generic_Dictionary_2_int` | ~13 | no member named 'hashCode' | 0.8% |
| 其它（100+ 类型） | ~276 | mixed | 16.9% |

---

## 二、Top 10 出错的接收者类型 + 缺失成员

### 1. `il2cpp::runtime` 命名空间（~291 条，最高频）

| 缺失成员 | 计数 | 说明 |
|---|---|---|
| `icall_box_intptr` | 93 | 应路由到 `icall_box_uint` / 扩展 box 族 |
| `icall_box_date_time` | 49 | 应路由到 `icall_box` + 类型特化 |
| `icall_unbox_intptr` | 39 | 应路由到 `icall_unbox` + 类型特化 |
| `icall_unbox_date_time` | 15 | 同上 |
| `icall_box_time_span` | 14 | 同上 |
| 其它 `icall_*` | ~81 | 运行时 icall 未注册 |

**根因**：MethodMap 仅注册了 `icall_box_int32/int64` 等常见路径，IntPtr/DateTime/TimeSpan 的 box/unbox 路由未 wiring。

---

### 2. `int32_t` 标量接收者（285 条）

| 出错文件 | 出错模式 | 缺失成员 |
|---|---|---|
| System_TupleExtensions.cpp | `(&arg0.Rest)->Item1` | arg0 声明为 int32_t，应为 Il2CppObject* |
| System_Diagnostics_Tracing_EventSource.cpp | `t15->field1` | temp 被推断为 int32_t |
| System_Threading_Tasks_TplEtwProvider.cpp | `inl0->m_item` | temp 被推断为 int32_t |
| System_Diagnostics_Tracing_FrameworkEventSource.cpp | `t0->field1` | temp 被推断为 int32_t |
| System_Reflection_Emit_DynamicResolver.cpp | `t0->field1` | temp 被推断为 int32_t |
| System_Console.cpp | `(&(inl2))->dwSize` | inl2 声明为 int32_t，应为 CONSOLE_SCREEN_BUFFER_INFO |
| System_DateTimeParse.cpp | `inl0->Index` | inl0 声明为 int32_t，应为 __DTString |
| System_Runtime_InteropServices_GCHandle.cpp | `t0->handle` | temp 被推断为 int32_t |
| System_Runtime_Remoting_Channels_ChannelServices.cpp | `t0->field1` | temp 被推断为 int32_t |
| System_Reflection_Emit_SignatureHelper.cpp | `t0->field1` | temp 被推断为 int32_t |

**根因**：
- `System_TupleExtensions_CreateLongRef_5449fbd8810b52be` 参数签名被错误解码：`arg0` 声明为 `int32_t`，但语义应为 `Il2CppObject*`（System.Tuple 是引用类型）。
- `System_Console_GetBufferInfo_` 返回 `CONSOLE_SCREEN_BUFFER_INFO` 结构体，但 locKind 未播种，inl2 退化 int32_t。

---

### 3. `System_Runtime_CompilerServices_TaskAwaiter_1_int`（~147 条）

| 缺失成员 | 计数 |
|---|---|
| Item1 | 31 |
| Item2 | 28 |
| Item3 | 28 |
| Item4 | 24 |
| Item5 | 20 |
| 其它 | 16 |

**根因**：**ValueTuple 字段误投递到 TaskAwaiter**。TaskAwaiter 是 awaiter 状态机的一部分，不包含 Item1-Item7 字段。IR 折叠阶段错误地将 ValueTuple 的字段访问重写到 TaskAwaiter 类型上（可能是 generic T 字段闭包参数丢失，行 6874 的 `_int` 后缀 fallback 将 Item 字段误识别为 TaskAwaiter 成员）。

---

### 4. `System_Collections_Concurrent_ConcurrentDictionary_2_int`（~112 条）

| 缺失成员 | 计数 | 建议 |
|---|---|---|
| m_buckets | 28 | did you mean 'm_budget'? |
| m_locks | 26 | 字段不存在 |
| m_next | 21 | 链表节点字段 |
| m_countPerLock | 13 | 字段不存在 |
| m_key | 12 | 键值字段 |
| m_value | 12 | 键值字段 |
| m_current | ~5 | 游标字段 |

**根因**：**字段名映射缺失**。`ConcurrentDictionary` 的内部字段（`m_buckets`, `m_locks`, `m_next` 等）在 TypeEmitter 生成的 struct 中使用了不同的命名（可能是 `buckets`, `locks` 无 `m_` 前缀），或这些字段根本未在 TypeDef 中展开（BCL 并发字典的字段可能被 MetaData 隐藏）。clang 建议 `m_budget` 而非 `m_buckets` 说明字段名存在但语义偏移。

---

### 5. `System_Threading_Tasks_SingleProducerSingleConsumerQueue_1_int`（~105 条）

| 缺失成员 | 计数 |
|---|---|
| m_state | 54 |
| m_first | 19 |
| m_last | 19 |
| m_array | 12 |
| m_lastCopy | 13 |

**根因**：与 ConcurrentDictionary 同族——BCL 内部类型的字段名与 C# 元数据名称不一致，或字段未在 TypeDef 模型中展开。

---

### 6. `intptr_t` 标量接收者（143 条）

| 出错文件 | 出错模式 | 缺失成员 |
|---|---|---|
| System_Diagnostics_Tracing_EventSource.cpp | `t15->field1` | temp 推断为 intptr_t |
| System_Diagnostics_Tracing_EventProvider.cpp | `t0->field1` | temp 推断为 intptr_t |
| System_Threading_Tasks_TplEtwProvider.cpp | `inl0->m_item` | temp 推断为 intptr_t |
| System_Diagnostics_Tracing_FrameworkEventSource.cpp | `t0->field1` | temp 推断为 intptr_t |
| System_IntPtr.cpp | `t0->field1` | temp 推断为 intptr_t |
| System_IO_FileStream.cpp | `t0->field1` | temp 推断为 intptr_t |
| System_Text_DBCSCodePageEncoding.cpp | `t0->field1` | temp 推断为 intptr_t |
| System_Text_SBCSCodePageEncoding.cpp | `t0->field1` | temp 推断为 intptr_t |
| System_DateTimeParse.cpp | `inl0->Index` | temp 推断为 intptr_t |
| System_Security_Cryptography_NativeHmac.cpp | `t0->field1` | temp 推断为 intptr_t |

**根因**：与 int32_t 同族——返回结构体的 user-method 未播种 locKind，temp 退化 intptr_t。

---

### 7. `System_Threading_Tasks_TaskFactory_1_int`（~80 条）

| 缺失成员 | 计数 |
|---|---|
| promise | 42 |
| endFunction | 19 |
| endAction | 19 |

**根因**：TaskFactory 的 continuation 字段（promise/endAction/endFunction）在 TypeDef 中未展开或命名不同。

---

### 8. `il2cpp::runtime::Il2CppDecimal`（~72 条）

| 缺失成员 | 计数 |
|---|---|
| mid | 24 |
| lo | 24 |
| hi | 24 |

**根因**：**Decimal 结构体字段名不匹配**。Il2CppDecimal 的 C++ 定义使用 `lo/mid/hi`，但代码生成器期望的字段名与实际 struct 布局不符（可能是 `flags/lo/mid/hi` 的偏移差异，或 decimal.h 的 struct 定义与预期不同）。

---

### 9. `Il2CppObject*` 标量接收者（60 条）

| 出错文件 | 出错模式 |
|---|---|
| System_String.cpp | `str->m_firstChar` | 应为 `m_firstChar` 但 Il2CppObject 无此字段 |
| System_Collections_Generic_Dictionary_2_int.cpp | `entries->hashCode` | 应为 entry 的字段 |

**根因**：Il2CppObject* 是通用引用槽，字段访问必须通过具体 struct 的 reinterpret_cast。当接收者类型跟踪丢失（objTypes 未记录），field access 直接对标量 Il2CppObject* 做成员访问。

---

### 10. `uint8_t[16]` 标量接收者（33 条）

| 出错文件 | 出错模式 |
|---|---|
| Microsoft_Runtime_Hosting_StrongNameHelpers.cpp | `t0 = System_Guid { }` | 数组不可 assignable |
| System_Security_Cryptography_NativeHmac.cpp | `t0->field1` | Guid 数组字段 |

**根因**：Guid 的 `uint8_t[16]` 数组类型在赋值时不可 assignable（C++ 数组限制），且字段访问被错误地发射到数组标量上。

---

## 三、六分型模式总结

| # | 模式 | 计数 | 根因 | 修复入口 |
|---|---|---|---|---|
| 1 | 返回结构体的 user-method 未播种 locKind | ~428 (int32_t+intptr_t) | MethodRefResolver 未设 ReturnTypeName | MethodMap.*.cs + BclResolve 后处理 |
| 2 | 运行时 icall 未注册 | ~291 | MethodMap 未 wiring box/unbox 特化路径 | MethodMap.Base64.cs + 扩展 |
| 3 | ValueTuple 字段误投递到 TaskAwaiter | ~147 | generic T 字段闭包参数丢失 | StackFold._curClosureArgs 作用域 |
| 4 | BCL 内部类型字段名不匹配 | ~217 | TypeDef 字段展开不完整 | MetadataLoader.ExpandNestedTypes |
| 5 | Decimal 结构体字段偏移不匹配 | ~72 | decimal.h 布局与预期不同 | 验证 decimal.h struct 定义 |
| 6 | 参数签名解码错误（arg0=int32_t） | ~105 | ParamTypesCpp 未识别 ref 参数 | MethodSignatureCoder + ParamTypesCpp |

---

## 四、关键证据

### 4.1 System_TupleExtensions_CreateLongRef 参数错误签名

[System_TupleExtensions.cpp:2294]
```cpp
IL2CPP_NOINLINE Il2CppObject* System_TupleExtensions_CreateLongRef_5449fbd8810b52be(
    int32_t arg0,   // ← 错误：应为 Il2CppObject*（System.Tuple 是引用类型）
    int32_t arg1, int32_t arg2, int32_t arg3,
    int32_t arg4, int32_t arg5, int32_t arg6, int32_t arg7)
```

[System_TupleExtensions.cpp:2061]
```cpp
t0 = reinterpret_cast<Il2CppObject*>(System_Tuple_Create_dc87324f97a1a658(
    (&(arg0.Rest))->Item1));   // ← arg0 是 int32_t，.Rest 失败
```

**根因**：`System.Tuple<T1>.Rest` 字段是 Tuple 引用类型本身，但 `arg0` 被解码为 int32_t。这是 `ParamTypesCpp` 对 `this` 参数的识别失败——`System_TupleExtensions_CreateLongRef` 是 extension method，`arg0` 是 `this` 参数，应被识别为 `Il2CppObject*`。

### 4.2 System_Console_GetBufferInfo 返回未播种

[System_Console.cpp:2175-2176]
```cpp
int32_t inl2;   // ← 错误：应为 Microsoft_Win32_Win32Native_CONSOLE_SCREEN_BUFFER_INFO
inl2 = System_Console_GetBufferInfo_ef2b53c92c91357c();
inl0 = (&((&(inl2))->dwSize))->X;   // ← int32_t 没有 dwSize
```

**根因**：`GetBufferInfo` 是 user-method（非 icall），MethodRefResolver 未设 ReturnTypeName，locKind 不播种。

### 4.3 ConcurrentDictionary 字段名建议

[System_Collections_Concurrent_ConcurrentDictionary_2_int.cpp:567]
```
error: no member named 'm_buckets' in 'System_Collections_Concurrent_ConcurrentDictionary_2_int';
      did you mean 'm_budget'?
```

**根因**：TypeEmitter 生成的 struct 中字段名为 `m_budget`（BCL 内部命名），但 IL 代码使用 `m_buckets`（C# 源命名）。字段名映射表不完整。

---

## 五、修复优先级

| 优先级 | 模式 | 预期减少 | 修复方案 |
|---|---|---|---|
| P0 | 返回结构体的 user-method 未播种 | ~428 | 方案 A：FieldAccess 入口 EnsureReceiverType pass |
| P1 | 运行时 icall 未注册 | ~291 | 扩展 MethodMap.Base64.cs + 自动路由 |
| P2 | ValueTuple 字段误投递 | ~147 | 修复 StackFold._curClosureArgs 作用域 |
| P3 | BCL 内部类型字段名不匹配 | ~217 | 补全字段名映射表 |
| P4 | Decimal 结构体字段偏移 | ~72 | 验证 decimal.h 布局 |
| P5 | 参数签名解码错误 | ~105 | 修复 ParamTypesCpp this 参数识别 |

**预期总减少**：≥ 1160 条（membref 1632 → < 500，减少 ≥ 70%）

---

## 六、关键代码坐标

| 位置 | 行 | 作用 |
|---|---|---|
| `CodeGenerator.FieldAccess` | 5562 | field access 发射入口 |
| `CodeGenerator.EmitBody` locKind scan | 1149-1185 | ctor/initobj 播种 |
| `CodeGenerator.EmitBody` return scan | 1476-1509 | value-type 返回播种 |
| `CodeGenerator.ParamTypesCpp` | 7190 | 参数 C++ 类型解码 |
| `MethodRefResolver.TryMapConsole` | 9-144 | Console 域 icall 映射 |
| `MethodRefResolver.BclResolve` | ~1400 | BCL 域分派主入口 |
| `TypeEmitter.TypeCppName` | 417 | TypeDef → C++ 名 |
| `MetadataLoader.ExpandNestedTypes` | - | 嵌套类型展开 |
