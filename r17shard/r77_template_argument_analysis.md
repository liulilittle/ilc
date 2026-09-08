# r77 Template Argument 错误分析报告

**生成时间**: 2026-09-06  
**数据来源**: r77 三片错误日志 (shard1/shard2/shard3)

---

## 关键发现: r77 中无 Template Argument/Parameter 错误

经搜索，r77 错误日志中：
- "template argument" 命中: **0**
- "template parameter" 命中: **0**  
- "template instantiation" 命中: **0**

**结论**: r77 的主要错误不是模板参数/实参推导失败，而是**类型转换和重载解析**失败。

---

## 替代分析: "No Matching Function" 错误 (3188 errors)

这是最接近"模板"模式的错误类别——C++ 函数重载解析失败（模板函数参与重载时会产生类似错误）。

### 按文件统计 Top 20

1. System_Security_Cryptography_X509Certificates_AsnDecoder.cpp: 146 errors
2. System_Security_Cryptography_X509Certificates_PasswordBasedEncryption.cpp: 127 errors
3. System_Diagnostics_Tracing_EventSource.cpp: 91 errors
4. System_Globalization_TimeSpanParse.cpp: 64 errors
5. System_Security_Cryptography_X509Certificates_AsnReader.cpp: 60 errors
6. System_Security_Cryptography_X509Certificates_Pkcs12Kdf.cpp: 52 errors
7. System_Security_Cryptography_X509Certificates_Span_1_int.cpp: 41 errors
8. Microsoft_Win32_RegistryKey.cpp: 38 errors
9. System_Security_Cryptography_X509Certificates_PfxAsn.cpp: 36 errors
10. System_Security_Principal_WindowsIdentity.cpp: 36 errors
11. System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int.cpp: 34 errors
12. System_Reflection_Emit_ModuleBuilder.cpp: 33 errors
13. System_Security_Cryptography_X509Certificates_AsnValueReader.cpp: 32 errors
14. System_Reflection_RuntimeAssembly.cpp: 31 errors
15. System_Number.cpp: 31 errors
16. System_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int.cpp: 31 errors
17. System_Collections_Concurrent_ConcurrentDictionary_2_object_object.cpp: 30 errors
18. System_Collections_Concurrent_ConcurrentDictionary_2_string_object.cpp: 30 errors
19. System_Collections_Concurrent_ConcurrentDictionary_2_System_Guid_System_Tuple_2_System_Guid_int.cpp: 30 errors
20. System_Collections_Concurrent_ConcurrentDictionary_2_System_Guid_object.cpp: 30 errors
---

## Top 10 整体错误模式 (12604 total errors)

1. cannot cast from type 'int32_t' (aka 'int') to pointer type 'Il2CppObject *': 1120 errors
2. no viable overloaded '=': 879 errors
3. cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (aka 'int'): 586 errors
4. cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *': 307 errors
5. reinterpret_cast from 'int32_t' (aka 'int') to 'intptr_t' (aka 'long long') is not allowed: 268 errors
6. no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *': 226 errors
7. cannot initialize return object of type 'int32_t' (aka 'int') with an rvalue of type 'Il2CppObject *': 148 errors
8. static_cast from 'Il2CppObject *' to 'int32_t' (aka 'int') is not allowed: 126 errors
9. member reference base type 'int32_t' (aka 'int') is not a structure or union: 120 errors
10. member reference base type 'intptr_t' (aka 'long long') is not a structure or union: 119 errors
---

## 详细分析

### 1. cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (1120)

**根因**: 尝试将 int32_t 值直接赋给 Il2CppObject* 变量，缺少 reinterpret_cast。

**修复**: 插入 `reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(...))`。

### 2. no viable overloaded '=' (879)

**根因**: 赋值操作符重载解析失败，通常是左值类型与右值类型不兼容。

### 3. cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (586)

**根因**: 函数参数类型不匹配，传入 int32_t 但期望 Il2CppObject*。

### 4. reinterpret_cast from 'int32_t' to 'intptr_t' is not allowed (268)

**根因**: reinterpret_cast 不能用于整数类型之间的转换，应使用 static_cast。

### 5. no viable conversion from 'TaskAwaiter_1_int' to 'Il2CppObject *' (226)

**根因**: 值类型不能隐式转换为引用类型，需要装箱操作。

---

## 结论

**r77 中不存在 template argument 错误**。

主导错误是：
1. 类型转换失败（int32 ↔ 指针）
2. 赋值/初始化类型不匹配
3. 重载函数解析失败

建议: 修复应聚焦于类型转换和重载决议，而非模板参数推导。

---

*报告结束*
