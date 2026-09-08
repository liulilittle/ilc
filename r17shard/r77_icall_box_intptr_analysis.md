# r77 "icall_box_intptr" 错误分析

**日期**: 2026-09-06  
**数据源**: r77shard1/2/3_shard*_errors.log  
**错误总数**: 186 (三片合计) / 93 唯一源行  
**错误类型**: `no member named 'icall_box_intptr' in namespace 'il2cpp::runtime'`

---

## 一、根因

### 1.1 直接根因

**`il2cpp::runtime::icall_box_intptr` 未在运行时实现，CodeGenerator.cs:5885-5917 的 box_ 分发 switch 缺少 `System.IntPtr` 分支。**

CodeGenerator  EmitExpr 的 `box_{token}` 分支（5865-5917）按 `TypeTokenFullName` 分派：
- System.Int32 → `il2cpp::il2cpp_box_int32`
- System.Int64 → `il2cpp::runtime::icall_box_long`
- System.Boolean/Char/Double/Byte/SByte/Int16/UInt16/UInt32/UInt64/Single/Decimal → 各有对应 icall
- **System.IntPtr → 落入默认分支 → 抛出 NotSupportedException → 生成 `il2cpp::runtime::icall_box_intptr` 占位符**

### 1.2 证据链

**CodeGenerator.cs:5885-5917 (box_ switch)**:
```csharp
return boxType switch
{
    "System.Int32" => $"il2cpp::il2cpp_box_int32(nullptr, {boxVal})",
    "System.Boolean" => $"il2cpp::runtime::icall_box_bool(static_cast<bool>({boxVal}))",
    // ... 覆盖全部 BCL 数值类型 ...
    "System.Decimal" => $"il2cpp::runtime::icall_box_decimal(...)",
    _ when boxTypeDef != null && boxTypeDef.IsValueType => /* user struct */,
    _ => throw new NotSupportedException($"box of {boxType} not supported -- no runtime box helper")
};
```

**System.IntPtr 不在 switch 中**，默认路径生成 `icall_box_intptr` 占位符，但运行时未提供。

**运行时头文件 (il2cpp_boxing_ext.h:46-48)**:
```cpp
Il2CppObject* icall_box_bool(bool value);
Il2CppObject* icall_box_char(int32_t utf16);
Il2CppObject* icall_box_double(double value);
// ... 无 icall_box_intptr 声明 ...
```

---

## 二、错误分布

### 2.1 三片统计

| 分片 | 错误数 |
|------|--------|
| shard1 | 93 |
| shard2 | 47 |
| shard3 | 46 |
| **合计** | **186** |

### 2.2 Top 出错文件

| 次数 | 文件 |
|------|------|
| 15 | System_Collections_Generic_ObjectEqualityComparer_1_System_IntPtr.cpp |
| 9 | System_Collections_Generic_Dictionary_2_System_IntPtr_System_IntPtr.cpp |
| 7 | System_Security_PermissionTokenFactory.cpp |
| 6 | System_Collections_Generic_Dictionary_2_Enumerator_System_IntPtr_System_IntPtr.cpp |
| 5 | System_Collections_Generic_Dictionary_2_System_IntPtr_int.cpp |
| 5 | System_Runtime_Remoting_Proxies_RealProxy.cpp |
| 4 | System_Collections_Generic_Dictionary_2_int_System_IntPtr.cpp |
| 4 | System_Collections_Generic_List_1_System_IntPtr.cpp |
| 3 | System_Collections_Generic_Dictionary_2_Enumerator_int_System_IntPtr.cpp |
| 3 | System_Collections_Generic_Dictionary_2_Enumerator_System_IntPtr_int.cpp |

### 2.3 错误模式

所有错误均为同一模式：
```
error: no member named 'icall_box_intptr' in namespace 'il2cpp::runtime'; did you mean 'icall_box_uint'?
```

反向对称错误 `icall_unbox_intptr` 也出现（39次），同样缺少实现。

---

## 三、触发条件

该错误在以下条件同时满足时触发：
1. **IL 存在 `box` 指令**，操作数为 `System.IntPtr` 类型 token
2. **泛型类型参数为 `System.IntPtr`** 的方法实例化（如 `Dictionary<IntPtr, IntPtr>`、`List<IntPtr>`、`EqualityComparer<IntPtr>`）
3. **方法体内需要对 IntPtr 值做装箱**（如添加到 `List<object>`、`Dictionary<object, object>`）

典型触发场景：
- `Dictionary<IntPtr, IntPtr>.Add(...)` → 键/值需要装箱为 object
- `EqualityComparer<IntPtr>.GetHashCode(...)` → IntPtr 装箱后调 object.GetHashCode
- `List<IntPtr>` → 某些路径需要 object 引用

---

## 四、修复方案

### 4.1 方案 A：实现 icall_box_intptr（推荐）

**步骤 1**: 在 `il2cpp_boxing_ext.h` 添加声明：
```cpp
Il2CppObject* icall_box_intptr(intptr_t value);
Il2CppObject* icall_unbox_intptr(Il2CppObject* obj);
```

**步骤 2**: 在 `boxingext.cpp` 添加实现（使用 IntPtr 的 box class 描述符）：
```cpp
Il2CppObject* icall_box_intptr(intptr_t value) {
    // IntPtr 的 box class 描述符
    return il2cpp_box_value(&System_IntPtr_class, &value, sizeof(intptr_t));
}
intptr_t icall_unbox_intptr(Il2CppObject* obj) {
    intptr_t value;
    il2cpp_unbox_value(obj, &value, sizeof(intptr_t));
    return value;
}
```

**步骤 3**: 在 CodeGenerator.cs:5885 添加分支：
```csharp
"System.IntPtr" => $"il2cpp::runtime::icall_box_intptr(static_cast<intptr_t>({boxVal}))",
"System.UIntPtr" => $"il2cpp::runtime::icall_box_uintptr(static_cast<uintptr_t>({boxVal}))",
```

### 4.2 方案 B：复用 long/int32 box（兼容性回退）

**x86**: IntPtr 4 字节 → 复用 `il2cpp_box_int32` + 显式转换  
**x64**: IntPtr 8 字节 → 复用 `icall_box_long` + 显式转换

```csharp
"System.IntPtr" => sizeof(IntPtr) == 8
    ? $"il2cpp::runtime::icall_box_long(static_cast<int64_t>({boxVal}))"
    : $"il2cpp::il2cpp_box_int32(nullptr, static_cast<int32_t>({boxVal}))",
```

**缺点**: 装箱后的对象类型为 Int32/Int64 box class，不是 IntPtr box class，可能导致 `is IntPtr` 判断失败或反射异常。

---

## 五、影响范围

- **错误数**: 186 处（直接编译失败）
- **受影响类型**: 所有以 System.IntPtr/UIntPtr 为泛型参数的 BCL 集合
- **受影响 domain**: Collections.Generic、Security、Remoting、Reflection、Threading
- **风险等级**: 高 — 影响 IntPtr 作为泛型参数的所有场景

---

## 六、结论

**根因**: CodeGenerator 的 box_ 分发缺少 System.IntPtr/UIntPtr 分支，运行时也未提供对应的 icall_box_intptr/unbox_intptr 实现。

**推荐修复**: 采用方案 A，补充 icall_box_intptr/icall_unbox_intptr 的运行时声明与实现，并在 CodeGenerator box_ switch 中增加 IntPtr/UIntPtr 分支。这能正确维护 IntPtr 的 box class 身份，保证 `is IntPtr` 和反射语义正确。

**验证**: 修复后 r77 的 icall_box_intptr 错误应从 186 降为 0，对应 Top 文件（ObjectEqualityComparer_1_IntPtr、Dictionary_2_IntPtr_IntPtr 等）抽查确认。
