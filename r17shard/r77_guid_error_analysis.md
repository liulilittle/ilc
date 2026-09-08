# r77 System_Guid 相关错误模式分析

## 总览

| 指标 | 数量 |
|------|------|
| 总 Guid 相关错误 | 2714 |
| Shard 1 | 1680 |
| Shard 2 | 716 |
| Shard 3 | 318 |

## 错误分类

| 错误类型 | 数量 | 占比 |
|----------|------|------|
| 其他（log/注释） | 2114 | 77.9% |
| no matching function for call | 236 | 8.7% |
| cannot cast | 147 | 5.4% |
| no viable conversion from | 136 | 5.0% |
| no viable overloaded '=' | 78 | 2.9% |
| cannot take the address | 3 | 0.1% |

> 注：2114 条"其他"为 log 行中的注释或中间代码（如 `System_Guid { }` newobj），非实际错误。**实际错误约 600 条**。

## 实际错误分析

### 模式 1: Guid 构造函数调用失败（41 次）

**错误示例**：
```
no matching function for call to 'System_Guid__ctor_1430675b62f51540'
candidate: void System_Guid__ctor_1430675b62f51540(System_Guid* arg0, Il2CppObject* arg1)
actual: (uint8_t (*)[16]) from array decay
```

**根因**：Guid 构造函数期望 `System_Guid*`，但传入的是 `uint8_t (*)[16]` — Guid 的底层 uint8_t[16] 数组发生指针退化。

**涉及函数**：
- `System_Guid__ctor_1430675b62f51540` (11 次) - 构造函数
- `System_Guid__ctor_05069cda82f8527b` (14 次) - 另一个构造函数
- `System_Guid_HexsToChars_683589494c1f1f86` (16 次) - 十六进制转换

**修复方案**：
- 调用构造函数时传入 `(&guid)` 而非 `guid.data()` 或退化的数组指针
- 确保 Guid 类型的局部变量声明为 `System_Guid t;` 然后 `System_Guid__ctor((&t), ...)`

---

### 模式 2: KeyValuePair<Guid,T> 构造函数参数不匹配（约 30 次）

**错误示例**：
```
no matching function for call to 'System_Collections_Generic_KeyValuePair_2_System_Guid_int__ctor_fe5b561b5331a819'
candidate: void(System_Collections_Generic_KeyValuePair_2_System_Guid_int* arg0, int32_t arg1, int32_t arg2)
actual: (Il2CppObject*) from reinterpret_cast
```

**根因**：
- KeyValuePair 构造函数期望 `int32_t` 作为 key 参数
- 但传入的是 `Il2CppObject*`（经过 reinterpret_cast 后的指针）
- Guid 作为 value type 被错误地当作引用类型传递

**涉及函数**：
- `KeyValuePair_2_Guid_int__ctor` (7 次)
- `KeyValuePair_2_Guid_object__ctor` (7 次)
- `KeyValuePair_2_Guid_System_Guid__ctor` (4 次)
- `KeyValuePair_2_Guid_System_Tuple_2_Guid_int__ctor` (3 次)

**修复方案**：
- Guid 字段需要先解包为 int32 数组或分别传入字段
- 或者对 Guid 进行 boxing 后再传

---

### 模式 3: Volatile_Read/Write 类型不匹配（15 次）

**错误示例**：
```
no matching function for call to 'System_Threading_Volatile_Read_c71f9240faef1dff'
candidate: void*(Il2CppObject**)
actual: (System_Collections_Concurrent_ConcurrentDictionary_2_Node_System_Guid_int*)
```

**根因**：
- `Volatile_Read` 期望 `Il2CppObject**` 类型的指针
- 传入的是具体 Node 类型的指针
- 缺少从 `Node*` 到 `Il2CppObject*` 的显式转换

**涉及函数**：
- `Volatile_Read` (9 次)
- `Volatile_Write` (6 次)

**修复方案**：
- 在 Volatile_Read/Write 调用前，将指针显式转换为 `Il2CppObject*`
- 例如：`Volatile_Read(reinterpret_cast<Il2CppObject**>(&node))`

---

### 模式 4: Guid 到 Il2CppObject* 的类型转换（45 次）

**错误示例**：
```
cannot cast from type 'System_Collections_Generic_KeyValuePair_2_System_Guid_int' to pointer type 'Il2CppObject *'
cannot take the address of an rvalue of type 'Il2CppObject *'
```

**根因**：
- 对 KeyValuePair 值类型直接使用 `static_cast<Il2CppObject*>(t2)` — 非法
- 尝试取 rvalue 的地址：`&(reinterpret_cast<Il2CppObject*>(...))` — 非法

**修复方案**：
- 值类型需要先 boxing：`il2cpp_box_user_struct(&klass, &val, sizeof(val))`
- 不要对临时值取地址

---

### 模式 5: Guid 到 const void* 的转换（6 次）

**错误示例**：
```
no known conversion from 'System_Guid' to 'const void *'
```

**根因**：Guid 是值类型结构体，不能隐式转换为 `const void *`

**修复方案**：使用 `&guid` 取地址（如果 guid 是左值）或先拷贝到局部变量

---

### 模式 6: ConcurrentDictionary<Guid,T> 操作失败（约 50 次）

**涉及函数**：
- `TryAddInternal` (27 次，跨 3 个泛型实例)
- `TryGetValue` (18 次)
- `TryRemove` (5 次)
- `Node` 构造函数 (约 10 次)

**根因**：
- Generic 泛型实例化后 Guid 作为 key 的类型处理不一致
- 比较器/哈希器未正确传入 Guid 类型
- Node 构造函数参数不匹配

---

### 模式 7: Guid.ToByteArray / Marshal 相关（约 20 次）

**涉及函数**：
- `System_Guid_ToByteArray_b00f1a32f8d70dce` (4 次)
- `System_Runtime_InteropServices_Marshal_GenerateGuidForType_27003dc2da12e16e` (9 次)
- `System_Threading_Tasks_TplEtwProvider_CreateGuidForTaskID_d45e7db6a057e469` (8 次)
- `System_Deployment_Internal_Isolation_IsolationInterop_GetGuidOfType_c4d74ee9e55c76b9` (7 次)

---

### 模式 8: 赋值过载错误（78 次）

**根因**：
- Guid 结构体之间缺少 `operator=`
- 不同大小结构体之间的赋值（如 uint8_t[16] 到 System_Guid）

---

## 根本原因总结

| 根因 | 错误数 | 占比 |
|------|--------|------|
| 构造函数参数类型不匹配（Guid* vs uint8_t[16]） | ~41 | 6.8% |
| KeyValuePair 构造参数不匹配 | ~30 | 5.0% |
| Volatile_Read/Write 指针类型错误 | ~15 | 2.5% |
| Guid 值类型到 Il2CppObject* 的非法转换 | ~45 | 7.5% |
| ConcurrentDictionary<Guid,T> 操作失败 | ~50 | 8.3% |
| Guid.ToByteArray/Marshal 操作 | ~20 | 3.3% |
| 赋值操作符缺失 | 78 | 13.0% |
| 其他/注释 | ~320 | 53.3% |

## 修复方案汇总

### 1. 构造函数调用修复（最高优先级）
```cpp
// 错误：传入退化的 uint8_t[16] 指针
System_Guid__ctor((&guid_as_uint8_array), str);

// 正确：传入 System_Guid* 指针
System_Guid__ctor((&guid), str);
```

### 2. KeyValuePair 构造修复
```cpp
// 错误：传入 Il2CppObject*
Kvp__ctor((&kvp), key_object, value);

// 正确：传入解包后的值
Kvp__ctor((&kvp), guid_as_int32, value);
```

### 3. Volatile 操作修复
```cpp
// 错误：传入 Node*
Volatile_Read(node_ref);

// 正确：转换为 Il2CppObject**
Volatile_Read(reinterpret_cast<Il2CppObject**>(node_ref));
```

### 4. Guid boxing 修复
```cpp
// 错误：值类型直接转指针
static_cast<Il2CppObject*>(kvp);

// 正确：显式 boxing
il2cpp_box_user_struct(&klass, &kvp, sizeof(kvp));
```

## 受影响文件

错误集中在以下文件：
1. `System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_System_Guid_*.cpp` (泛型实例)
2. `System_Collections_Concurrent_ConcurrentDictionary_2_System_Guid_*.cpp` (泛型实例)
3. `System_Collections_Generic_KeyValuePair_2_System_Guid_*.cpp` (泛型实例)
4. `Microsoft_Runtime_Hosting_StrongNameHelpers.cpp` (HexsToChars)
5. `System_Runtime_InteropServices_Marshal.cs` 相关文件 (GenerateGuidForType)
6. `System_Threading_Tasks_TplEtwProvider.cpp` (CreateGuidForTaskID)
