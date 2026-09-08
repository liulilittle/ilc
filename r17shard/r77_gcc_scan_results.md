# r77 g++ 扫描结果 — Top 10 错误模式

**扫描日期**: 2026-09-06  
**扫描范围**: E:\il2cpp\out\mscorlib_gen_r77\src\*.cpp (4628 个文件)  
**扫描方式**: g++ -fsyntax-only -std=c++17  
**数据来源**: r77shard1/2/3_errors.log (已有扫描日志)

---

## 总览

| 指标 | 值 |
|---|---|
| 总文件数 | 4628 |
| 总错误数 | 16,653 |
| Shard1 Fail | 601 / 1543 |
| Shard2 Fail | 356 / 1543 |
| Shard3 Fail | 484 / 1542 |
| 总失败数 | 1,441 / 4628 (31.1%) |

---

## Top 10 错误模式

### 1. no viable overloaded '=' (1373 次, 8.25%)

**根因**: 生成的 C++ 代码尝试将不兼容类型的值赋给变量。最常见的是将 Il2CppObject* 赋给 int32_t 变量，或将 int32_t 赋给 Il2CppObject* 变量。

**示例文件**:
- `Microsoft_Win32_Fusion.cpp:148` — `no viable overloaded '='`
- `System_AppDomain.cpp:8305` — `no viable overloaded '='`
- `System_ArraySegment_1_int.cpp:317` — `no viable overloaded '='`

---

### 2. cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (1224 次, 7.35%)

**根因**: 生成的代码尝试将 int32_t 值直接 cast 为 Il2CppObject* 指针。这是 cannot-init 错误的变体 — 方法期望 Il2CppObject* 但实际参数是 int32_t。

**示例文件**:
- `System_AggregateException.cpp:610` — `cannot cast from type 'int32_t' (aka 'int') to pointer type 'Il2CppObject *'`
- `System_AggregateException.cpp:640` — 同上
- `System_AggregateException.cpp:899` — 同上

---

### 3. cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (613 次, 3.68%)

**根因**: 函数调用时参数类型不匹配 — 函数期望 Il2CppObject* 但传入 int32_t。这是 cannot-init 簇的典型表现。

**示例文件**:
- `Microsoft_Win32_RegistryKey.cpp:2913` — `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'`
- `System_AggregateException.cpp:835` — 同上
- `System_AggregateException.cpp:899` — 同上

---

### 4. incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *' (342 次, 2.05%)

**根因**: 生成的代码将 int32_t* 指针直接赋给 intptr_t 变量，缺少 reinterpret_cast。

**示例文件**:
- `__f__AnonymousType0_1_int.cpp:194` — `incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *'`
- `System_Runtime_InteropServices_Marshal.cpp:1335` — 同上
- `System_Runtime_InteropServices_Marshal.cpp:1397` — 同上

---

### 5. cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *' (313 次, 1.88%)

**根因**: 函数期望 Il2CppArray* 但传入 Il2CppObject*。数组类型在 C++ 层面需要显式 cast。

**示例文件**:
- `System_Array_SZArrayEnumerator.cpp:118` — `cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *'`
- `System_Array.cpp:2451` — 同上
- `System_Array.cpp:2623` — 同上

---

### 6. member reference base type 'int32_t' is not a structure or union (285 次, 1.71%)

**根因**: 生成的代码尝试访问 int32_t 变量的成员（如 `var.field`），但 int32_t 是原始类型。

**示例文件**:
- `System_Console.cpp:2176` — `member reference base type 'int32_t' is not a structure or union`
- `System_Console.cpp:2211` — 同上
- `System_Console.cpp:2345` — 同上

---

### 7. reinterpret_cast from 'int32_t' to 'intptr_t' is not allowed (271 次, 1.63%)

**根因**: 生成的代码使用 reinterpret_cast 将 int32_t 转为 intptr_t，但 g++ 不允许这种转换（需要 static_cast）。

**示例文件**:
- `Microsoft_Win32_RegistryKey.cpp:691` — `reinterpret_cast from 'int32_t' (aka 'int') to 'intptr_t' (aka 'long long') is not allowed`
- `Microsoft_Win32_RegistryKey.cpp:823` — 同上
- `Microsoft_Win32_RegistryKey.cpp:969` — 同上

---

### 8. no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *' (226 次, 1.36%)

**根因**: 生成的代码尝试将值类型 TaskAwaiter_1_int 直接转为 Il2CppObject* 引用。值类型不能隐式转为引用类型。

**示例文件**:
- `System_Collections_Generic_Comparer_1_object.cpp:276` — `no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *'`
- `System_Collections_Generic_Comparer_1_string.cpp:276` — 同上
- `System_Collections_Generic_Dictionary_2_int_int.cpp:2002` — 同上

---

### 9. unknown type name 'dt' (165 次, 0.99%)

**根因**: 生成的代码使用了未定义的类型名 'dt'。这是 DateTime 类型的占位符，但缺少 typedef 或 using 声明。

**示例文件**:
- `System_CurrentSystemTimeZone.cpp:201` — `unknown type name 'dt'`
- `System_CurrentSystemTimeZone.cpp:203` — 同上
- `System_CurrentSystemTimeZone.cpp:204` — 同上

---

### 10. cannot initialize return object of type 'int32_t' with an rvalue of type 'Il2CppObject *' (163 次, 0.98%)

**根因**: 方法返回类型为 int32_t 但实际返回 Il2CppObject* 值。这是 return 类型不匹配。

**示例文件**:
- `System_Collections_Generic_Dictionary_2_int_object.cpp:658` — `cannot initialize return object of type 'int32_t' with an rvalue of type 'Il2CppObject *'`
- `System_Collections_Generic_Dictionary_2_int_object.cpp:1601` — 同上
- `System_Collections_Generic_Dictionary_2_int_string.cpp:658` — 同上

---

## 错误分类

| 类别 | 错误数 | 占比 |
|---|---|---|
| int↔pointer 类型不匹配 | ~3,500 | ~21% |
| 参数传递类型不匹配 | ~1,100 | ~7% |
| 成员访问错误 | ~430 | ~2.6% |
| 未定义类型/标识符 | ~300 | ~1.8% |
| 其他 | ~11,323 | ~68% |

---

## 结论

r77 生成树中 31% 的文件有 g++ 错误。Top 10 错误模式主要集中在：
1. **int↔pointer 类型不匹配** (cannot cast, cannot initialize parameter, incompatible pointer conversion)
2. **值类型↔引用类型不匹配** (no viable conversion, no viable overloaded =)
3. **未定义类型** (unknown type name 'dt')

这些错误与 cannot-init 簇（r77 verdict 报告 Top1 1242 次）高度相关，说明根因是 CodeGenerator 在方法调用/赋值/return 时未正确处理 int-kind 值到 Il2CppObject* 的桥接。
