# r77 "no matching function" 错误模式分析

源: `E:\il2cpp\r77shard\r77shard{1,2,3}_shard{1,2,3}_errors.log`
分析时间: 2026-09-06
原始行数: 34068 / 28000 / 37000 (三片)
nmf 总行数: 4160 | 唯一函数数: 1349

## 三片分布

| Shard | nmf 行数 |
|-------|---------|
| shard1 | 993 |
| shard2 | 1007 |
| shard3 | 2160 |
| **合计** | **4160** |

## Top 10 出错函数

| 排名 | 函数名 | 次数 | 声明 (generated.h) | 典型调用 | 失配类型 |
|------|--------|------|---------------------|----------|----------|
| 1 | System_IntPtr_op_Explicit_b87f01d65c5fb32c | 69 | `intptr_t(int32_t* arg0)` | `fn(loc1)` loc1=int64; `fn(arg0->scratch)` | 实参非 int32_t* |
| 2 | System_Tuple_8_int_get_Rest_b3ab99604a10375a | 56 | `int32_t(Il2CppObject* arg0)` | `fn(t0)` t0=int32_t (链式) | int32_t → Il2CppObject* |
| 3 | System_Threading_Volatile_Read_c71f9240faef1dff | 49 | `int32_t(Il2CppObject** arg0)` | `fn(&arg0)` 或闭包变量 | 实参非 Il2CppObject** |
| 4 | System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_Length_aa95fb2d151867d3 | 49 | (ReadOnlySpan 方法) | 闭合类型调用 | 签名含 int32_t 回落 |
| 5 | System_ArraySegment_1_int_get_Array_aaceee58dbd184e0 | 42 | `Il2CppObject*(System_ArraySegment_1_int* arg0)` | `fn(&loc0)` loc0=Il2CppObject* | Il2CppObject** ≠ System_ArraySegment_1_int* |
| 6 | System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2 | 42 | `int32_t(System_ArraySegment_1_int* arg0)` | `fn(&loc0)` loc0=Il2CppObject* | 同上 |
| 7 | System_Threading_Volatile_Write_b1c41837d78588c7 | 40 | `void(Il2CppObject** arg0, int32_t arg1)` | 闭包/并发字典 | 实参类型不匹配 |
| 8 | System_ArraySegment_1_int_get_Count_1a39335e3a4abf36 | 39 | `int32_t(System_ArraySegment_1_int* arg0)` | `fn(&loc0)` loc0=Il2CppObject* | 同上 |
| 9 | System_Runtime_InteropServices_SafeBuffer_AcquirePointer_0d7dbe4b8cbf2e0e | 39 | (SafeBuffer 方法) | 闭合类型调用 | 签名含 int32_t 回落 |
| 10 | System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1 | 37 | `void(Il2CppObject* arg0, int32_t arg1, int32_t arg2)` | `fn(t0, arg0, arg1)` arg0/arg1 非 int32_t | 实参非 int32_t |

## Top 10 出错文件

| 文件 | nmf 行数 |
|------|---------|
| System_TupleExtensions.cpp | 302 |
| System_Security_Cryptography_X509Certificates_AsnDecoder.cpp | 146 |
| System_Security_Cryptography_X509Certificates_PasswordBasedEncryption.cpp | 127 |
| System_Diagnostics_Tracing_EventSource.cpp | 91 |
| System_Globalization_TimeSpanParse.cpp | 64 |
| System_Security_Cryptography_X509Certificates_AsnReader.cpp | 60 |
| System_Security_Cryptography_X509Certificates_Pkcs12Kdf.cpp | 52 |
| System_Security_Cryptography_X509Certificates_Span_1_int.cpp | 41 |
| Microsoft_Win32_RegistryKey.cpp | 38 |
| System_Security_Principal_WindowsIdentity.cpp | 36 |

## 典型错误样本

### 1. System_IntPtr_op_Explicit (int32_t* vs int64/int32_t)
```
System_Diagnostics_Tracing_DataCollector.cpp:249: error: no matching function for call to 'System_IntPtr_op_Explicit_b87f01d65c5fb32c'
    t0 = System_IntPtr_op_Explicit_b87f01d65c5fb32c(loc1);   // loc1 is int64_t
```
声明: `intptr_t System_IntPtr_op_Explicit_b87f01d65c5fb32c(int32_t* arg0);`
原因: loc1 是 int64_t (或 int32_t)，但声明要求 int32_t* — 签名声明本身错误（把值类型声明为指针）。

### 2. System_Tuple_8_int_get_Rest (链式调用, int32_t → Il2CppObject*)
```
System_TupleExtensions.cpp:559: error: no matching function for call to 'System_Tuple_8_int_get_Rest_b3ab99604a10375a'
    t0 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(arg0);   // arg0=Il2CppObject* ✓
    ...
    t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(t0);     // t0=int32_t ✗
```
声明: `int32_t System_Tuple_8_int_get_Rest_b3ab99604a10375a(Il2CppObject* arg0);`
原因: 链式调用把 int32_t 返回值直接传给下一层，但下一层期望 Il2CppObject*。

### 3. System_ArraySegment_1_int_get_Offset (Il2CppObject** vs System_ArraySegment_1_int*)
```
System_Security_Cryptography_X509Certificates_IncrementalHash.cpp:263: error: no matching function for call to 'System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2'
    t1 = System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2((&loc0));   // loc0=Il2CppObject*
```
声明: `int32_t System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2(System_ArraySegment_1_int* arg0);`
原因: loc0 是 Il2CppObject*，&loc0 是 Il2CppObject**，但声明要求 System_ArraySegment_1_int*。
根因: loc0 的声明类型被错误地设为 Il2CppObject* 而非 System_ArraySegment_1_int（值类型应声明为 struct 而非 Il2CppObject*）。

### 4. System_Threading_Volatile_Read (Il2CppObject** vs int32_t*)
```
System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_int.cpp:164: error: no matching function for call to 'System_Threading_Volatile_Read_c71f9240faef1dff'
```
声明: `int32_t System_Threading_Volatile_Read_c71f9240faef1dff(Il2CppObject** arg0);`
原因: 调用侧传入 int32_t*（或 intptr_t*），但声明要求 Il2CppObject**。

### 5. ConditionalWeakTable_2_int_Add (int32_t vs intptr_t)
```
System_Collections_Generic_Dictionary_2_int_int.cpp:448: error: no matching function for call to 'System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1'
    System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1(t0, arg0, arg1);
```
声明: `void System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1(Il2CppObject* arg0, int32_t arg1, int32_t arg2);`
原因: arg0/arg1 实际为 intptr_t（或 int64_t），但声明要求 int32_t — 这是 ClosedParamTypes 末端 p==null 回落 int32_t 的典型表现。

## 根因分类

### A. ClosedParamTypes 末端 p==null 回落 int32_t (T1-T6 核心)
当闭合类型方法的某个 VAR/MVAR 参数在 ClosedParamTypes 数组中为 null 时，CodeGenerator 的 ParamTypesCpp / VirtualParamTypesCpp 路径会回落到默认 `"int32_t"`，但函数体实际使用更宽的类型（intptr_t / int64_t / Il2CppObject*）。
- 典型: ConditionalWeakTable_2_int_Add, Volatile_Read/Write, Nullable_1_int_get_HasValue
- 预期消除: ~993 行中约 30-40% 属此类

### B. 值类型局部变量被声明为 Il2CppObject*
System_ArraySegment_1_int / System_Nullable_1_int 等值类型在闭包方法体内被当作引用类型声明（Il2CppObject*），导致 &loc 地址类型错误。
- 典型: ArraySegment_1_int_get_Array/Offset/Count
- 与 A 同源: ClosedParamTypes 回落 int32_t 后，值类型被错误地按引用处理

### C. 链式调用返回值直传
Tuple_8_int_get_Rest 等方法的返回值 (int32_t) 被直接传给同方法的下一个调用（期望 Il2CppObject*）。
- 这是 A/B 的下游表现: 返回类型被声明为 int32_t 而非 Il2CppObject*

### D. IntPtr.op_Explicit 签名反向
声明为 `intptr_t(int32_t*)` 但调用侧传入 int64_t 值 — 声明方向反了（应接受 int32_t 返回 intptr_t* 或类似）。
- 典型: System_IntPtr_op_Explicit_b87f01d65c5fb32c (69 次)

## 修复方向

1. **消除 int32_t 缺省回落**: 在 ParamTypesCpp / VirtualParamTypesCpp 中，当 ClosedParamTypes[i]==null 时，不能简单回落 int32_t，必须根据原型签名解码真实类型（与 FunctionParamType 一致）。
2. **值类型局部声明**: 闭包体内的值类型局部变量必须声明为 `{StructName}` 而非 `Il2CppObject*`，&loc 才能匹配 `System_ArraySegment_1_int*`。
3. **链式调用临时变量**: 当返回值类型与下一层参数类型不同时，需插入 reinterpret_cast 或拆分临时。

## 预期效果

修复 A 后，预期消除:
- no matching function: ~400-600 行 (约 30-40%)
- cannot initialize: ~300-500 行 (下游)
- 合计: ~700-1100 行
