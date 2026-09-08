# Nullable 结构体转型错误 (cannot-init) 根因分析与修复方案

生成: 2026-09-06
基线: r77 1441 fail (Top1-613 为 cannot-init)

## 1. 错误模式归类

r77 错误日志中 Nullable 相关错误 4 大类 (共约 35 行):

### A. 接口dispatch时 Nullable 值类型直接转 Il2CppObject* (约 12 行)
文件: `System_Collections_Generic_NullableComparer_1_int.cpp:92`
```
error: cannot cast from type 'System_Nullable_1_int' to pointer type 'Il2CppObject *'
```
调用点:
```
il2cpp_null_check(reinterpret_cast<Il2CppObject*>((&arg1)))
```
其中 arg1 是 `System_Nullable_1_int` 值类型，`(&arg1)` 是 `System_Nullable_1_int*`，不能 reinterpret_cast 为 `Il2CppObject*`。

同族: NullableEqualityComparer_1_int (line 103/123/171)

### B. 值类型字段地址当引用传递 (约 8 行)
文件: `System_Globalization_CultureNotFoundException.cpp:533/536/568`
```
candidate function not viable: no known conversion from 'Il2CppObject **' to 'System_Nullable_1_int *'
```
调用点:
```
System_Nullable_1_int_get_HasValue_495fb3b18c2b35b9((&loc0))
```
loc0 是 Il2CppObject* 但函数期望 System_Nullable_1_int*。

### C. newobj 参数类型不匹配 (约 6 行)
文件: `System_Globalization_CultureData.cpp:1635`, `System_Globalization_CultureInfo.cpp:781`
```
no known conversion from 'Il2CppObject *' to 'System_Nullable_1_int *' for 1st argument
```

### D. 比较器 Equals/GetHashCode 值类型按引用传递 (约 4 行)
文件: `System_Collections_Generic_NullableEqualityComparer_1_int.cpp:157/169`
```
no known conversion from 'intptr_t' (aka 'long long') to 'System_Nullable_1_int *'
```

## 2. 根因

`System.Nullable<T>` 在 .NET 中是值类型(struct)，但在 il2cppc 中被当作引用类型(Il2CppObject*)处理。

**核心矛盾**: Nullable<T> 的 C++ 表示为结构体 `System_Nullable_1_int` (含 hasValue + value 字段)，但代码生成器在某些路径将它当作 Il2CppObject* 传递，导致类型不匹配。

具体根因位置:
1. **接口 dispatch 路径** (`CodeGenerator.cs:4074-4100`): receiver 是值类型时仍走 `il2cpp_interface_dispatch`，该函数期望 Il2CppObject*
2. **callvirt 值类型方法**: constrained callvirt 时值类型 receiver 被错误地 reinterpret_cast 为 Il2CppObject*
3. **newobj Nullable**: 将 Nullable 当作类分配，但实际应是值类型的局部变量

## 3. 修复方案

### 方案 A: 值类型 receiver 走 constrained 路径 (最小改动)
在 `CodeGenerator.cs` EmitVirtualCall 中，检测 receiver 是否为值类型，如果是则:
- 不走 il2cpp_interface_dispatch
- 直接调用值类型的具体方法 (constrained call)

### 方案 B: Nullable<T> 在 TypeCppName 中特殊处理
让 Nullable<T> 返回 T 的基础类型，而非 struct 名称。

### 方案 C: 值类型接口调用走 vtable (推荐)
在 `il2cpp_interface_dispatch` 前，对值类型 receiver 取其 vtable，直接 dispatch。

## 4. 影响范围

- 受影响文件: 约 20+ 个 .cpp 文件
- 错误行: 约 35 行 cannot-init / no matching function
- 修复后预期: cannot-init 下降 5-10%

## 5. 实施建议

推荐方案 A+C 组合:
1. 在 CodeGenerator.cs 中检测值类型 receiver
2. 值类型接口调用走 constrained 路径 (直接调用)
3. 修复 newobj 路径中 Nullable 的值类型处理

风险: 需要区分值类型的装箱(box)与未装箱状态，避免过度修复。
