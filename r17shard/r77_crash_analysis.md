# r77 运行时崩溃分析报告

## 结论摘要

**r77 扫描中不存在 SIGSEGV / SIGABRT / Segmentation fault / bus error / assertion failed 等运行时崩溃。**

r77 的全部 16653 行错误均为 **C++ 编译期类型错误**（g++ compile-time errors），不是运行时信号。

---

## 1. 三片错误日志来源

| 分片 | 文件 | Total | Fail | Errors |
|------|------|-------|------|--------|
| shard1 | r77shard1_shard1_errors.log | 1543 | 601 | 6593 |
| shard2 | r77shard2_shard2_errors.log | 1543 | 356 | 3477 |
| shard3 | r77shard3_shard3_errors.log | 1542 | 484 | 6583 |
| **合计** | | **4628** | **1441** | **16653** |

来源：[E:\il2cpp\out\r77_verdict.md]

---

## 2. 运行时信号搜索结果

| 搜索模式 | 命中数 | 备注 |
|----------|--------|------|
| SIGSEGV | 0 | — |
| SIGABRT | 0 | — |
| SIGBUS | 0 | — |
| segmentation fault | 0 | — |
| bus error | 0 | — |
| assertion failed | 0 | （运行时日志中无） |
| core dumped | 0 | — |

搜索范围：
- `E:\il2cpp\r77shard\*.log`（三片错误+摘要）
- `E:\il2cpp\out\r77shard\*.log`
- `E:\il2cpp\out_*.log`（生成/转译日志）
- `E:\il2cpp\test_*.out*`（运行时测试输出）

---

## 3. r77 实际错误类型（编译期）

### 3.1 主要错误簇

| 簇 | 典型错误 | 代表文件 |
|----|----------|----------|
| cannot-init | `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'` | System_AppDomain.cpp, System_AggregateException.cpp |
| no-matching | `no matching function for call to '...'` | 多文件 |
| no-viable | `no viable conversion from 'System_IntPtr' to 'int32_t'` | Microsoft_Win32_Fusion.cpp |
| static-cast | `static_cast from 'System_Nullable_1_int *' to 'Il2CppObject *' is not allowed` | NullableComparer_1_int.cpp |
| membref | `member reference base type 'int32_t' ... is not a structure or union` | System_Reflection_Emit_*.cpp |
| undeclared | `use of undeclared identifier '...'` | entry.cpp |

### 3.2 Top 6 编译错误（按唯一错误行统计）

| # | 错误模式 | 前 3 个出错文件 |
|---|----------|-----------------|
| 1 | `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'` | System_AppDomain_NamespaceResolverForIntrospection.cpp:104, System_AggregateException.cpp:835, System_AppDomain.cpp:4315 |
| 2 | `incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *'` | __f__AnonymousType0_1_int.cpp:194, System_AppDomain.cpp:4448, System_CurrentSystemTimeZone.cpp:557 |
| 3 | `use of undeclared identifier '...'` | entry.cpp:230-350 (多个闭包符号), System_DefaultBinder.cpp:253 |
| 4 | `no matching function for call to '...'` | System_AppContext.cpp:192, System_DateTimeParse.cpp:4559, System_Runtime_Hosting_StrongNameHelpers.cpp:95 |
| 5 | `reinterpret_cast from 'System_IntPtr' to 'const char16_t *' is not allowed` | Microsoft_Win32_Fusion.cpp:162 |
| 6 | `assigning to 'intptr_t' from incompatible type 'System_IntPtr'` | Microsoft_Win32_RegistryKey.cpp:1340, Microsoft_Win32_SafeHandles_*.cpp |

---

## 4. 唯一运行时断言（与 r77 无关）

搜索全部运行时测试输出，唯一命中：

```
test_math.out.txt: 2 math assertion(s) failed
test_math.out.txt: FAIL: max_d(-0.0,0.0) == -0.0 (first operand wins the tie)
test_math.out.txt: FAIL: min_d(0.0,-0.0) == +0.0 (first operand wins the tie)
```

这是 IEEE 754 符号零比较的预存问题，与 r77 编译错误无关。

---

## 5. 判定

**r77 编译错误是 C++ 类型系统不匹配（cast-2 / cannot-init / undeclared 等），全部可在编译阶段捕获。**

没有任何证据表明 r77 生成的可执行文件在运行时触发 SIGSEGV / SIGABRT / SIGBUS / assertion。

---

## 6. 建议

若需运行时崩溃分析：
1. 先修复 r77 编译错误（1441 fail / 16653 err）
2. 编译通过后运行运行时测试套件
3. 再对 runtime test 输出做 SIGSEGV/abort 扫描
