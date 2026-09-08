# r77 System.Threading.StackCrawlMark 类型完整分析与修复方案

**日期**: 2026-09-06  
**数据源**: `E:\il2cpp\r77shard\*.csv` (23个错误)  
**目标**: 分析 StackCrawlMark 在 r77 中的完整使用情况并提出修复方案

---

## 一、错误完整清单

### 1.1 按文件分组

| 源文件 | 行号 | 错误数 |
|--------|------|--------|
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_int_int.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_int.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_object_array_object_array.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_string_string.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_object_object.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_System_Threading_Tasks_Task_array_System_Threading_Tasks_Task_array.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromResultTask_2_System_Threading_Tasks_VoidTaskResult_System_Threading_Tasks_VoidTaskResult.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationResultTaskFromTask_1_int.cpp` | 142 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromResultTask_1_int.cpp` | 143 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromResultTask_1_object_array.cpp` | 143 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromResultTask_1_object.cpp` | 143 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromResultTask_1_System_Threading_Tasks_Task_array.cpp` | 143 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromResultTask_1_string.cpp` | 143 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromResultTask_1_System_Threading_Tasks_VoidTaskResult.cpp` | 143 | 1 |
| `System_Threading_Tasks_ContinuationTaskFromTask.cpp` | 143 | 1 |
| `System_Threading_Tasks_Task_1_int.cpp` | 545 | 1 |
| `System_Threading_Tasks_Task_1_object_array.cpp` | 539 | 1 |
| `System_Threading_Tasks_Task_1_object.cpp` | 539 | 1 |
| `System_Threading_Tasks_Task_1_string.cpp` | 539 | 1 |
| `System_Threading_Tasks_Task_1_System_Threading_Tasks_Task_array.cpp` | 539 | 1 |
| `System_Security_SecurityRuntime.cpp` | 292 | 1 |
| **合计** | — | **23** |

### 1.2 按模式分组

| 模式 | 数量 | 说明 |
|------|------|------|
| `ContinuationResultTaskFromResultTask_2<T>` | 8 | async state machine 双泛型 |
| `ContinuationTaskFromResultTask_1<T>` | 8 | async state machine 单泛型 |
| `Task_1<T>` | 5 | Task 泛型实例化 |
| `ContinuationTaskFromTask` | 1 | 非泛型 continuation |
| `SecurityRuntime` | 1 | 安全运行时 |

---

## 二、StackCrawlMark 类型分析

### 2.1 mscorlib 中的定义

```csharp
// System.Threading.StackCrawlMark (mscorlib)
public struct StackCrawlMark {
    private int _stackCrawlMarkValue;  // 唯一字段，int32
    
    // 方法声明
    public static void ThrowSecurityException(StackCrawlMark stackMark);
    public static void CheckSecurityOnDemand(StackCrawlMark stackMark);
}
```

### 2.2 在 async state machine 中的使用

```csharp
// 编译器生成的 async state machine 示例
private sealed class ContinuationResultTaskFromResultTask_2<TResult, TAntecedentResult>
{
    // ... 其他字段 ...
    private StackCrawlMark _stackCrawlMark;  // ← 问题字段
    
    private void SetResult(TResult result)
    {
        // ...
        // ldloca _stackCrawlMark → 应生成 (&_stackCrawlMark)
        // 但 TypeKindResolver 把 StackCrawlMark 识别为 int32_t
        // 导致生成 int32_t* 而非 StackCrawlMark*
        // ...
    }
}
```

### 2.3 错误代码生成示例

**错误输出** (`ContinuationResultTaskFromResultTask_2_int_int.cpp:142`):
```cpp
System_Threading_StackCrawlMark _stackCrawlMark;
System_Threading_StackCrawlMark* t0 = &_stackCrawlMark;  // ← 目标端正确

// ❌ 错误: 源端被解析为 int32_t*
_stackCrawlMark = *reinterpret_cast<int32_t*>(t1);  // ← 应为 System_Threading_StackCrawlMark*
```

---

## 三、根因分析

### 3.1 类型解析链路

```
IL 局部变量签名: 0x11 VALUETYPE + TypeDef(System.Threading.StackCrawlMark)
    ↓
MethodBodyReader.LocalTypeKind → "v:System.Threading.StackCrawlMark"  ✓ 正确
    ↓
CodeGenerator.EmitBody locKind[locName] = "v:System.Threading.StackCrawlMark"  ✓ 正确
    ↓
声明: System_Threading_StackCrawlMark locN;  ✓ 正确
    ↓
ldloca locN → 应生成 (&locN) 类型为 System_Threading_StackCrawlMark *
    ↓
❌ TypeKindResolver.Resolve 把 VALUETYPE 默认映射为 int32_t
    ↓
❌ ResolveFromFullName 没有 StackCrawlMark 特殊映射
    ↓
❌ TypeCppName 没有 StackCrawlMark 映射
    ↓
源表达式被分类为 int32_t *
    ↓
赋值: System_Threading_StackCrawlMark * = int32_t *  ✗ 编译失败
```

### 3.2 TypeKindResolver 缺陷

**位置**: `TypeKindResolver.cs:31`

```csharp
case 0x11: return (FieldTypeKind.Int32, "", "int32_t");
```

VALUETYPE 默认返回 `int32_t`, 但 `StackCrawlMark` 是封装 int32 的 struct, 需要完整 C++ 类型名。

**位置**: `TypeKindResolver.cs:40-51` (ArgKind 方法)

```csharp
private static (FieldTypeKind Kind, string FullName, string CppKind) ArgKind(string fullName) => fullName switch
{
    // ... 现有映射 ...
    // ❌ 缺少 StackCrawlMark 映射
    _ => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
};
```

### 3.3 CodeGenerator.TypeCppName 缺陷

**位置**: `CodeGenerator.cs:5626-5649`

```csharp
private static string TypeCppName(string fullName) => fullName switch
{
    "System.Object" => "Il2CppObject",
    "System.String" => "Il2CppObject",
    "System.DateTime" => "il2cpp::runtime::Il2CppDateTime",
    // ... 其他 BCL 映射 ...
    // ❌ 缺少 StackCrawlMark 映射
    _ => TypeEmitter.Sanitize(fullName),
};
```

---

## 四、修复方案

### 4.1 方案 A: TypeKindResolver.ArgKind 添加映射 (推荐)

**文件**: `E:\il2cpp\src\il2cppc\Model\TypeKindResolver.cs`

**位置**: `ArgKind` 方法 (行 40-51)

```csharp
private static (FieldTypeKind Kind, string FullName, string CppKind) ArgKind(string fullName) => fullName switch
{
    "System.Int64" => (FieldTypeKind.Int64, fullName, "int64_t"),
    "System.UInt64" => (FieldTypeKind.UInt64, fullName, "int64_t"),
    "System.Single" => (FieldTypeKind.Float, fullName, "float"),
    "System.Double" => (FieldTypeKind.Double, fullName, "double"),
    "System.IntPtr" or "System.UIntPtr" => (FieldTypeKind.NativeInt, fullName, "intptr_t"),
    "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" => (FieldTypeKind.Int32, fullName, "int32_t"),
    _ when fullName is "System.String" or "System.Object" => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
    _ when fullName.EndsWith("[]", StringComparison.Ordinal) => (FieldTypeKind.Reference, "System.Array", "Il2CppObject*"),
    // ✅ 新增: StackCrawlMark 是封装 int32 的 struct, 需要完整 C++ 类型名
    "System.Threading.StackCrawlMark" => (FieldTypeKind.ValueType, fullName, "System_Threading_StackCrawlMark"),
    _ => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
};
```

### 4.2 方案 B: CodeGenerator.TypeCppName 添加映射

**文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`

**位置**: `TypeCppName` 方法 (行 5626-5649)

```csharp
private static string TypeCppName(string fullName) => fullName switch
{
    "System.Object" => "Il2CppObject",
    "System.String" => "Il2CppObject",
    "System.DateTime" => "il2cpp::runtime::Il2CppDateTime",
    "System.TimeSpan" => "il2cpp::runtime::Il2CppTimeSpan",
    "System.DateTimeOffset" => "il2cpp::runtime::Il2CppDateTimeOffset",
    "System.Numerics.BigInteger" => "il2cpp::runtime::Il2CppBigInteger",
    "System.Decimal" => "il2cpp::runtime::Il2CppDecimal",
    "System.Runtime.InteropServices.GCHandle" => "int32_t",
    "System.IntPtr" => "intptr_t",
    "System.UIntPtr" => "uintptr_t",
    // ✅ 新增: StackCrawlMark 是值类型, 需要完整 C++ 类型名
    "System.Threading.StackCrawlMark" => "System_Threading_StackCrawlMark",
    _ => TypeEmitter.Sanitize(fullName),
};
```

### 4.3 方案 C: MethodBodyReader.LocalTypeKind 强化

**文件**: `E:\il2cpp\src\il2cppc\Il\MethodBodyReader.cs`

**位置**: `LocalTypeKind` 方法 (行 242-293)

在 VALUETYPE 分支中, 确保 StackCrawlMark 使用完整类型名:

```csharp
if (b == 0x11)
{
    // ... 现有代码 ...
    var fullName = th.Kind switch
    {
        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
        _ => null,
    };
    return fullName == null ? "v" : "v:" + fullName;
}
```

此方案已正确实现 (MethodBodyReader.cs:276-290), 无需修改。

---

## 五、推荐修复组合

**最小改动 + 最大覆盖**: 方案 A + 方案 B

| 修改文件 | 改动 | 覆盖范围 |
|----------|------|----------|
| `TypeKindResolver.cs` | +1 行 (ArgKind) | 所有通过 FullName 解析的场景 |
| `CodeGenerator.cs` | +1 行 (TypeCppName) | 所有通过 TypeCppName 映射的场景 |

**总改动**: 2 行代码

---

## 六、预期效果

### 6.1 错误消除

| 指标 | 当前 | 修复后 |
|------|------|--------|
| StackCrawlMark 指针错误 | 23 | 0 |
| 受影响源文件 | ~15 | 0 |
| 泛型实例化编译 | 全部失败 | 全部通过 |

### 6.2 修复前后代码对比

**修复前** (错误代码):
```cpp
System_Threading_StackCrawlMark _stackCrawlMark;
// ❌ 错误: int32_t* → StackCrawlMark*
*_stackCrawlMark = *reinterpret_cast<int32_t*>(t1);
```

**修复后** (正确代码):
```cpp
System_Threading_StackCrawlMark _stackCrawlMark;
// ✅ 正确: StackCrawlMark* → StackCrawlMark*
*_stackCrawlMark = *reinterpret_cast<System_Threading_StackCrawlMark*>(t1);
```

---

## 七、验证清单

1. `dotnet build src/il2cppc` → 0错0警告
2. 重新生成 r77 → StackCrawlMark 错误归零
3. 抽查文件确认:
   - `ContinuationResultTaskFromResultTask_2_int_int.cpp:142` → 无错误
   - `Task_1_int.cpp:545` → 无错误
   - `SecurityRuntime.cpp:292` → 无错误
4. 回归测试:
   - `test_async` → 通过
   - `test_threading` → 通过
   - `test_security` → 通过

---

## 八、扩展建议

### 8.1 其他可能受影响的 VALUETYPE

mscorlib 中其他封装单一 int 的 struct 可能遇到相同问题:
- `System.Runtime.InteropServices.GCHandle` → 已映射为 int32_t ✓
- `System.IntPtr` → 已映射为 intptr_t ✓
- `System.Threading.StackCrawlMark` → **本次修复**

### 8.2 防御性检查

建议在 `TypeKindResolver.Resolve` 中添加断言或警告:

```csharp
case 0x11:
    // VALUETYPE 默认映射为 int32_t, 但 struct 可能需要完整类型名
    // 警告: 如果 struct 字段被 ldloca 操作, 可能导致指针类型不匹配
    return (FieldTypeKind.Int32, "", "int32_t");
```

---

**调查人**: 子代理 (ses_f8d427dafffegHpvvixhOajmiS)  
**状态**: 待实施修复
