# r77 "invalid use of incomplete type" 错误分析

**分析范围:** mscorlib_gen_r77 shard1-3 全部错误日志
**分析方法:** 全文搜索 `incomplete type`, `forward declaration`, `unknown type name`, `undeclared identifier`
**分析时间:** 2026-09-06

---

## 一、结论摘要

r77 中**不存在标准 "invalid use of incomplete type" 错误**。GCC 的 incomplete type 报错被归类为 `unknown type name`。

| 错误类别 | 数量 |
|----------|------|
| `unknown type name 'V_X'` | 179 |
| `unknown type name 'dt'` | 51 |
| `unknown type name 'ts'` | 12 |
| `unknown type name 'X'` (非V前缀) | 51 |
| `use of undeclared identifier` | 388 |
| **合计 incomplete 相关** | **681** |

---

## 二、根因分类

### 类别 1: `V_` 前缀类型未映射 (179处)

**根因:** `ParamTypesCpp` (CodeGenerator.cs:7263) 对 `V:` 编码的值类型参数生成 `TypeCppName(v[2..])`。当值类型是 GenericInst（如 `System.Guid`）时，`TypeCppName` 返回 `System_Guid`，但实际声明可能使用 `V_System_Guid` 前缀。

**Top V_ 类型:**

| 类型名 | 次数 | 典型文件 |
|--------|------|----------|
| V_System_Guid | 36 | Dictionary, List, Hashtable |
| V_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode | 16 | ConcurrentDictionary |
| V_System_Collections_Generic_KeyValuePair_2_int | 15 | Dictionary |
| V_System_TimeSpan | 9 | DateTime/TimeSpan ops |
| V_System_DateTime | 9 | Calendar, Globalization |
| V_System_DateTimeOffset | 9 | Diagnostics Tracing |
| V_System_Decimal | 9 | Decimal ops |
| V_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken | 9 | WindowsRuntimeMarshal |
| V_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int | 7 | X509Certificates |
| V_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventCacheKey | 7 | WindowsRuntimeMarshal |

**证据:** `CodeGenerator.cs:7263`
```csharp
var v when v.StartsWith("V:", StringComparison.Ordinal) => TypeCppName(v[2..]),
```
`TypeCppName` 对某些 GenericInst 返回带 `V_` 前缀的名称，但 `generated.h` 中只声明了无前缀的结构体。

### 类别 2: `dt`/`ts` 未知类型名 (63处)

**根因:** `TypeKindResolver.ResolveExprKind` (TypeKindResolver.cs:70-71) 将 `ValueTypeNewExpr` 的 `System.DateTime` 映射为 `"dt"`，`System.TimeSpan` 映射为 `"ts"`。这些 short-kind 在 `EmitBody` 的本地声明中用于类型推断，但 short-kind 本身不是有效的 C++ 类型名。

当 short-kind `dt`/`ts` 被用于局部变量声明（如 `ts loc1;`）时，GCC 报告 `unknown type name 'dt'`。

**Top 文件:**

| 文件 | dt次数 | ts次数 |
|------|--------|--------|
| System_Globalization_* | 12 | 0 |
| System_Diagnostics_Tracing_EventProvider.cpp | 5 | 0 |
| System_Diagnostics_Tracing_DateTimeOffsetTypeInfo.cpp | 0 | 4 |
| System_Globalization_Calendar.cpp | 3 | 0 |
| System_Globalization_ChineseLunisolarCalendar.cpp | 3 | 0 |
| System_Globalization_CalendricalCalculationsHelper.cpp | 3 | 0 |

**证据:** `TypeKindResolver.cs:70-71`
```csharp
"System.DateTime" => "dt",
"System.TimeSpan" => "ts",
```
这些 short-kind 在 `CodeGenerator.cs` 的本地声明中应被展开为 `il2cpp::runtime::Il2CppDateTime` / `il2cpp::runtime::Il2CppTimeSpan`，但展开逻辑缺失。

### 类别 3: `ReadOnlySpan_1_u1_` / `ReadOnlySpan_1_c_` 等未完成类型 (40处)

**根因:** `TypeCppName` 对 `ReadOnlySpan`1<T>` 类型生成 `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_` 等名称，但这些类型没有对应的 struct 定义。它们来自 BCL 内部泛型实例化，但 `generated.h` 的 forward-decl 列表未覆盖。

**类型分布:**

| 类型名 | 次数 |
|--------|------|
| System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_ | 31 |
| System_Nullable_1_V_System_Security_Cryptography_X509Certificates_Asn1Tag_ | 15 |
| System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_c_ | 5 |
| System_Security_Cryptography_X509Certificates_Span_1_u1_ | 4 |

### 类别 4: `use of undeclared identifier` (388处)

**Top 未标识符:**

| 标识符 | 次数 | 原因 |
|--------|------|------|
| System_RuntimeType_ListBuilder_1_object_get_Count_* | 20 | ListBuilder 泛型方法未生成 |
| System_RuntimeType_ListBuilder_1_object_Add_* | 18 | 同上 |
| System_RuntimeType_ListBuilder_1_object_ToArray_* | 17 | 同上 |
| SZARRAY_class | 16 | 数组类 descriptor 未声明 |
| __il2cpp_ex | 12 | 异常变量未声明 |
| loc0/t0 | 38 | 变量声明缺失 |

---

## 三、Top 10 出错类型名

| 排名 | 类型名 | 错误次数 | 类别 |
|------|--------|----------|------|
| 1 | dt | 51 | short-kind 未展开 |
| 2 | V_System_Guid | 36 | V_ 前缀未映射 |
| 3 | System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_ | 31 | 缺少 struct 定义 |
| 4 | V_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode | 16 | V_ 前缀未映射 |
| 5 | V_System_Collections_Generic_KeyValuePair_2_int | 15 | V_ 前缀未映射 |
| 6 | System_Nullable_1_V_System_Security_Cryptography_X509Certificates_Asn1Tag_ | 15 | V_ 前缀未映射 |
| 7 | ts | 12 | short-kind 未展开 |
| 8 | V_System_TimeSpan | 9 | V_ 前缀未映射 |
| 9 | V_System_DateTime | 9 | V_ 前缀未映射 |
| 10 | V_System_DateTimeOffset | 9 | V_ 前缀未映射 |

---

## 四、修复建议

### 修复 1: short-kind 展开 (dt/ts/dec/dto/guid/bi)

在 `EmitBody` 的局部变量声明处，当 `locKind[name]` 是 short-kind 时，应展开为完整 C++ 类型名：

```csharp
var dk = kind switch
{
    "dt" => "il2cpp::runtime::Il2CppDateTime",
    "ts" => "il2cpp::runtime::Il2CppTimeSpan",
    "dec" => "il2cpp::runtime::Il2CppDecimal",
    "dto" => "il2cpp::runtime::Il2CppDateTimeOffset",
    "guid" => "uint8_t[16]",
    "bi" => "il2cpp::runtime::Il2CppBigInteger",
    _ => kind
};
```

**影响:** 消除 63 处 `unknown type name 'dt'/'ts'` 错误。

### 修复 2: V_ 前缀类型 forward decl

在 `generated.h` 的 forward-decl 循环中，对已知 BCL 值类型（System.Guid, System.DateTime, System.Decimal, System.TimeSpan, System.DateTimeOffset, System.Runtime.InteropServices.GCHandle, System.Collections.DictionaryEntry, System.Threading.Tasks.VoidTaskResult, System.AppContext.SwitchValueState, System.Resources.ResourceLocator 等）生成 `V_X` 到 `X` 的 alias：

```cpp
using V_System_Guid = System_Guid;
using V_System_DateTime = System_DateTime;
// ...
```

**影响:** 消除 179 处 `unknown type name 'V_X'` 错误。

### 修复 3: ReadOnlySpan/Span 类型定义

为 `System.S.ReadOnlySpan`1<T>` 和 `System.S.Span`1<T>` 生成 minimal struct 定义：

```cpp
struct ReadOnlySpan_1_u1_ { uint8_t* _ptr; int32_t _length; };
```

**影响:** 消除 40 处 `unknown type name 'ReadOnlySpan_X'` 错误。

### 修复 4: SZARRAY_class 声明

在 `generated.h` 中添加数组类的前向声明或 extern 声明。

**影响:** 消除 16 处 `undeclared identifier 'SZARRAY_class'` 错误。

---

## 五、预期修复效果

| 修复 | 消除错误数 |
|------|-----------|
| 修复1 (short-kind展开) | 63 |
| 修复2 (V_ alias) | 179 |
| 修复3 (ReadOnlySpan定义) | 40 |
| 修复4 (SZARRAY_class) | 16 |
| **合计** | **298** |

综合实施后 r77 的 incomplete/unknown 类错误可从 681 降至约 383。
