# r77 TimeSpan 错误分析报告

## 1. 统计总览

| 维度 | 数值 |
|------|------|
| TimeSpan 相关错误文件数 | 15 |
| TimeSpan 相关错误行数 | ~800+ |
| 涉及的主要程序集 | mscorlib（System、System.Collections.Generic、System.Globalization） |

---

## 2. 错误类型分组（按频次）

| 排名 | 错误模式 | 频次 | 根因 |
|------|----------|------|------|
| 1 | `assigning to 'int32_t' from incompatible type 'System_Globalization_TimeSpanParse_*'` | 186 | TimeSpan 解析器内部类型不匹配 |
| 2 | `no matching function for call to 'System_Globalization_TimeSpanParse_TryTimeToTicks_*'` | 54 | 函数签名不匹配 |
| 3 | `no viable overloaded '='` | 38 | 赋值类型不兼容 |
| 4 | `no viable conversion from 'il2cpp::runtime::Il2CppTimeSpan' to 'Il2CppDateTime'` | 34 | TimeSpan ↔ DateTime 隐式转换 |
| 5 | `cannot cast from type 'il2cpp::runtime::Il2CppTimeSpan' to pointer type 'Il2CppObject *'` | 28 | 值类型 → 引用指针 |
| 6 | `unknown type name 'V_System_TimeSpan'` | 20 | 泛型 V_ 类型未生成 |
| 7 | `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'` | 18 | 整数 → 指针 |
| 8 | `assigning to 'int32_t' from incompatible type 'il2cpp::runtime::Il2CppTimeSpan'` | 16 | 结构体 → 整数 |
| 9 | `undeclared identifier 'V_System_TimeSpan_class'` | 12 | 泛型 V_ 类型类未声明 |
| 10 | `no viable conversion from 'il2cpp::runtime::Il2CppTimeSpan' to 'Il2CppObject *'` | 12 | 值类型 → 引用 |
| 11 | `no viable conversion from returned value of type 'Il2CppObject *' to function return type 'il2cpp::runtime::Il2CppTimeSpan'` | ~10 | 返回类型不匹配 |
| 12 | `no matching function for call to 'System_TimeSpan__ctor_*'` | 10 | 构造函数签名不匹配 |

---

## 3. 出错文件 Top 15

| 排名 | 文件 | 错误数 | 说明 |
|------|------|--------|------|
| 1 | System_Globalization_TimeSpanParse.cpp | 386 | 主解析器 |
| 2 | System_Collections_Generic_List_1_System_TimeSpan.cpp | 68 | List<TimeSpan> |
| 3 | System_Collections_Generic_ObjectEqualityComparer_1_System_TimeSpan.cpp | 34 | 相等比较器 |
| 4 | System_TimeSpan.cpp | 28 | TimeSpan 本体 |
| 5 | System_TimeZoneInfo.cpp | 24 | 时区信息 |
| 6 | System_Collections_ObjectModel_ReadOnlyCollection_1_System_TimeSpan.cpp | 22 | 只读集合 |
| 7 | System_Runtime_InteropServices_WindowsRuntime_CLRIReferenceImpl_1_System_TimeSpan.cpp | 16 | WinRT 互操作 |
| 8 | System_Collections_Generic_EqualityComparer_1_System_TimeSpan.cpp | 14 | 相等比较器 |
| 9 | System_Collections_Generic_List_1_SynchronizedList_System_TimeSpan.cpp | 14 | 同步列表 |
| 10 | System_CurrentSystemTimeZone.cpp | 14 | 当前时区 |
| 11 | System_Diagnostics_Tracing_TraceLoggingTypeInfo_1_System_TimeSpan.cpp | 10 | 跟踪日志 |
| 12 | System_DateTimeOffset.cpp | 10 | DateTimeOffset |
| 13 | System_DateTimeParse.cpp | 8 | DateTime 解析 |
| 14 | System_Collections_Generic_List_1_Enumerator_System_TimeSpan.cpp | 8 | 枚举器 |
| 15 | System_Runtime_Remoting_Lifetime_LeaseLifeTimeServiceProperty.cpp | 8 | 租约生命周期 |

---

## 4. 根因分析

### 4.1 核心问题：Il2CppTimeSpan 是值类型

```
TypeCppName("System.TimeSpan") → "il2cpp::runtime::Il2CppTimeSpan"
```

`Il2CppTimeSpan` 定义（il2cpp_time.h）：
```cpp
struct Il2CppTimeSpan {
    int64_t ticks;
};
```

它是**值类型结构体**，不是指针。但 IR 中 TimeSpan 参数/字段在泛型上下文中常以 `Il2CppObject*` 表示。

### 4.2 三大错误家族

#### 家族 A：值类型 ↔ 引用类型转换（~100 错误）

```cpp
// 错误 1：Il2CppTimeSpan → Il2CppObject*（需要 boxed/reinterpret_cast）
return Il2CppTimeSpan_value;  // 但函数签名返回 Il2CppObject*

// 错误 2：Il2CppObject* → Il2CppTimeSpan（需要 unboxed/static_cast）
Il2CppTimeSpan val = object_ptr;  // 不能隐式转换

// 错误 3：Il2CppTimeSpan* 不能作为 Il2CppObject*
reinterpret_cast<Il2CppObject*>(timespan_ptr);  // 编译错误
```

**根因**：泛型容器 `List<TimeSpan>` 的 IR 把 T 当作 `Il2CppObject*` 处理，但实际 C++ 签名是 `Il2CppTimeSpan`。

#### 家族 B：V_System_TimeSpan 缺失（~32 错误）

```cpp
// 错误：unknown type name 'V_System_TimeSpan'
// 错误：undeclared identifier 'V_System_TimeSpan_class'
```

**根因**：泛型按实参特化（R1-R6 阶段任务）生成的 V_ 前缀值类型名。System.TimeSpan 的 V_ 类型未被生成。

#### 家族 C：TimeSpan ↔ DateTime 混淆（~34 错误）

```cpp
// 错误：no viable conversion from 'Il2CppTimeSpan' to 'Il2CppDateTime'
Il2CppDateTime dt = timespan_value;  // 结构体不同，不能隐式转换
```

**根因**：两者都是 int64 值类型但结构不同，需要显式 `.ticks` 访问。

---

## 5. 典型错误代码示例

### 示例 1：List<Enumerator>.current 赋值

**文件**：System_Collections_Generic_List_1_Enumerator_System_TimeSpan.cpp:155

```cpp
// 错误：unknown type name 'V_System_TimeSpan'
arg0->current = reinterpret_cast<V_System_TimeSpan*>(
    *reinterpret_cast<int32_t*>(...)
);
```

**问题**：V_System_TimeSpan 类型未生成，应为 `il2cpp::runtime::Il2CppTimeSpan`。

### 示例 2：返回类型不匹配

**文件**：System_Collections_Generic_List_1_Enumerator_System_TimeSpan.cpp:204

```cpp
// 错误：no viable conversion from 'Il2CppObject *' to function return type 'il2cpp::runtime::Il2CppTimeSpan'
return Il2CppObject*_value;
```

**问题**：函数签名返回值类型，但返回了引用类型。

### 示例 3：TimeSpan ↔ DateTime 转换

**文件**：System_DateTime.cpp（多处）

```cpp
// 错误：no viable conversion from 'il2cpp::runtime::Il2CppTimeSpan' to 'Il2CppDateTime'
Il2CppDateTime dt = ...;  // 源是 Il2CppTimeSpan
```

**问题**：两个结构体都是 int64 成员但类型名不同。

---

## 6. 修复建议

### 6.1 短期（补丁模式）

1. **在 TypeCppName 中为 TimeSpan 添加 V_ 前缀支持**
   - 当泛型参数是 TimeSpan 时，V_System_TimeSpan 应解析为 Il2CppTimeSpan

2. **在泛型方法发射中插入 reinterpret_cast**
   - 当参数/字段类型是 Il2CppTimeSpan 但 IR 期望 Il2CppObject* 时
   - 发射 `reinterpret_cast<Il2CppTimeSpan>(object_ptr)` 的 boxed/unboxed 桥接

3. **TimeSpan ↔ DateTime 显式转换**
   - 需要生成 `Il2CppDateTime{dt.ticks}` 或 `Il2CppTimeSpan{ts.ticks}` 显式构造

### 6.2 长期（根治模式）

1. **完成泛型按实参特化（R1-R6）**
   - 每个泛型实例生成独立类型，T=TimeSpan 时直接使用 Il2CppTimeSpan
   - 消除 Il2CppObject* 统一表示导致的类型擦除

2. **值类型字段/参数的独立代码路径**
   - 区分 `Il2CppObject*` 引用类型和 `Il2CppTimeSpan` 值类型
   - 值类型走 by-value 路径，引用走指针路径

---

## 7. 与 cast-2 的关系

TimeSpan 错误是 **cast-2 的子集**：
- cast-2 泛指值类型 owner 的 reinterpret_cast 失败
- TimeSpan 是 cast-2 在 `System.TimeSpan` 上的具体表现
- 修复 cast-2 时需特别处理 TimeSpan/DateTime/Decimal 等 BCL 值类型

---

## 8. 结论

r77 中 TimeSpan 错误的核心是 **Il2CppTimeSpan 作为值类型与 IR 中 Il2CppObject* 引用表示之间的转换缺失**。这不是运行时崩溃，而是编译期类型系统不匹配。修复需要：

1. 泛型 V_ 类型生成（V_System_TimeSpan）
2. 值类型 ↔ 引用类型的显式 cast 桥接
3. 完成泛型按实参特化以消除类型擦除
