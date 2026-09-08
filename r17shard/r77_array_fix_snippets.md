# r77 Array 方法调用修复代码片段

## 统计总览

| 错误模式 | 出现次数 | 优先级 |
|----------|----------|--------|
| `il2cpp_array_get_length(argN)` Il2CppObject* → Il2CppArray* | **313** | P0 |
| `System_ArraySegment_1_int_*` 泛型特化缺失 | **369** | P0 |
| `System_Nullable_1_*` 无 TypeDef | **1053** | P1 |
| `System_RuntimeType_ListBuilder_1_*` 无 TypeDef | **638** | P1 |
| `System_Threading_Tasks_TaskFactory_1___c*` 闭包类 | **752** | P1 |
| `System_Array_InternalCreate/GetReference/SetValue` 参数类型不匹配 | **17** | P2 |
| `System_Tuple_2_*` 泛型特化缺失 | **36** | P2 |
| `SZARRAY_class` 未定义 | **15** | P2 |

---

## P0: Il2CppArray* 类型转换修复 (313 处)

### 错误模式
```cpp
// System_Array.cpp:2451
return il2cpp::il2cpp_array_get_length(arg0);  // arg0 is Il2CppObject*, needs Il2CppArray*
// System_Array_SorterGenericArray.cpp:406
t0 = il2cpp::il2cpp_array_get_length(reinterpret_cast<Il2CppObject*>(arg0->keys));  // wrong cast target
// System_Array_SZArrayEnumerator.cpp:118
_endIndex = il2cpp::il2cpp_array_get_length(arg1);  // arg1 is Il2CppObject*
```

### 修复方案

#### 方案 A: 在 CodeGenerator.cs 中修复 `ldlen_` 发射器

文件: `src/il2cppc/Emit/CodeGenerator.cs`
行号: 6067-6070

```csharp
// 修改前:
if (c.Function.StartsWith("ldlen_", StringComparison.Ordinal))
{
    return $"il2cpp::il2cpp_array_get_length({NullSafePtrCast("Il2CppArray*", EmitExpr(c.Args[0], md, types))})";
}

// 修改后（增加类型检查和强制转换）:
if (c.Function.StartsWith("ldlen_", StringComparison.Ordinal))
{
    var arrExpr = EmitExpr(c.Args[0], md, types);
    var arrType = ExprCppType(c.Args[0], md, types);  // 新增辅助方法
    // 如果表达式类型是 Il2CppObject*，需要 reinterpret_cast<Il2CppArray*>
    var castNeeded = !arrType.Contains("Il2CppArray*");
    var ptrExpr = castNeeded 
        ? $"reinterpret_cast<Il2CppArray*>({arrExpr})"
        : arrExpr;
    return $"il2cpp::il2cpp_array_get_length({ptrExpr})";
}
```

#### 方案 B: 修复 `EmitIcallCallArgs` 中的数组参数

文件: `src/il2cppc/Emit/CodeGenerator.cs`
行号: ~6515-6540

```csharp
// 在 EmitIcallCallArgs 的循环中增加数组参数类型检测:
var isArrayParam = cast == "a" || cast == "arr" || r.ParamTypeNames[j] == "a";
if (isArrayParam && code.StartsWith("arg", StringComparison.Ordinal))
{
    // argN 是 Il2CppObject*（统一引用类型），需要转为 Il2CppArray*
    parts.Add($"reinterpret_cast<Il2CppArray*>({code})");
    continue;
}
```

#### 方案 C: 修复所有 `il2cpp_array_get_length(argN)` 调用点

对于直接调用 `il2cpp_array_get_length` 的位置（不通过 ldlen_ 函数名），在生成时统一添加转换:

```csharp
// 通用修复模板 - 在 EmitIcallCallArgs 中识别数组操作 icall
var isArrayOp = r.CppName switch
{
    "il2cpp::il2cpp_array_get_length" or
    "il2cpp::il2cpp_array_get_elem_ptr" or
    "il2cpp::runtime::icall_array_copy" or
    "il2cpp::runtime::icall_array_clear_int32" or
    "il2cpp::runtime::icall_array_resize" or
    "il2cpp::runtime::icall_array_new" => true,
    _ => false
};

if (isArrayOp && cast is null or "" && j == 0)  // 第一个参数是数组
{
    parts.Add($"reinterpret_cast<Il2CppArray*>({code})");
    continue;
}
```

---

## P0: System.Array 内部方法参数修复

### System.Array.InternalCreate (int32_t → int32_t*)

```cpp
// 错误:
t11 = reinterpret_cast<Il2CppObject*>(System_Array_InternalCreate_e9fb06daeadd9568(t3, 1LL, (&arg1), 0LL));
// t3 是 int32_t，但函数期望 int32_t*

// 修复:
t11 = reinterpret_cast<Il2CppObject*>(System_Array_InternalCreate_e9fb06daeadd9568((&t3), 1LL, (&arg1), 0LL));
//                                                        加 & ↑
```

```csharp
// CodeGenerator.cs 修复方案:
// 在 EmitIcallCallArgs 中检测 InternalCreate 并添加地址操作
if (r.CppName.Contains("System_Array_InternalCreate"))
{
    // 第一个参数是 int32_t 值类型，需要取地址变为 int32_t*
    if (j == 0 && args[srcIdx] is Ir.LocExpr)
    {
        parts.Add($"(&({code}))");
        continue;
    }
}
```

### System.Array.InternalGetReference (System_TypedReference* → int32_t*)

```cpp
// 错误:
System_Array_InternalGetReference_6daad4bf546dc7a8(arg0, (&loc0), ...);
// (&loc0) 是 System_TypedReference*（当 loc0 是 int 时退化为 int32_t*），但函数期望 int32_t*

// 修复 - 强制 reinterpret:
System_Array_InternalGetReference_6daad4bf546dc7a8(arg0, reinterpret_cast<int32_t*>((&loc0)), ...);
```

```csharp
// CodeGenerator.cs 修复方案:
// 在 EmitIcallCallArgs 中检测 TypedReference 地址退化
if (r.CppName.Contains("System_Array_InternalGetReference") ||
    r.CppName.Contains("System_Array_InternalSetValue"))
{
    if (args[srcIdx] is LocAddrExpr la && cast == "i")
    {
        parts.Add($"reinterpret_cast<int32_t*>({code})");
        continue;
    }
}
```

---

## P1: 泛型类型/方法特化缺失修复

### System.ArraySegment`1<int> 方法名泛型退化 (369 处)

```cpp
// 错误:
return System_ArraySegment_1_int_get_Array_aaceee58dbd184e0(loc0, arg1);
// loc0 是 int32_t，但方法期望 Il2CppObject*（this 指针）

// 修复方案:
// 在 CodeGenerator.cs 的 MethodCppNameFromToken 中增加泛型退化匹配
```

```csharp
// 文件: src/il2cppc/Emit/CodeGenerator.cs
// 在 MethodCppNameFromToken 中增加泛型实例化回退:

var fullName = md.GetFullName(md.GetMethodDefinition(...).GetDeclaringType());
// fullName = "System.ArraySegment`1" (generic def)
// 如果 "System.ArraySegment_1_int" 不存在，回退到 "System.ArraySegment_1"

// 具体实现:
var closedName = TypeCppName(fullName + "_int");  // 尝试 int 特化
if (!TypeExistsInGeneratedTypes(closedName, types))
{
    // 回退到泛型定义名（使用 int 作为默认特化）
    closedName = TypeCppName(fullName);  // 只保留 "System.ArraySegment_1"
}
```

### System.Nullable`1<T> class 描述符 (1053 处)

```cpp
// 错误: System_Nullable_1_byte_get_HasValue_... 调用
// 但 System_Nullable_1_T_class 不存在

// 修复 - 生成 Nullable class 桩:
// 在 TypeEmitter 中为所有 Nullable<T> 生成统一 class 描述符:

// generated.h 中添加:
// Il2CppClass System_Nullable_1_class = {
//     "System.Nullable`1",
//     sizeof(Il2CppObject) + sizeof(int32_t),  // 简化: has_value + value
//     ...
// };
```

### System.RuntimeType+ListBuilder (638 处)

```cpp
// 错误:
System_RuntimeType_ListBuilder_1_object_get_Count_46255826a76b6404(arg0);

// 修复 - 在 TypeEmitter 中生成 ListBuilder class:
// Il2CppClass System_RuntimeType_ListBuilder_1_class = { ... };
```

---

## P2: 其他修复

### SZARRAY_class (15 处)

```cpp
// 在 generated.h 中添加:
Il2CppClass SZARRAY_class = {
    "SZARRAY",  // 单维数组基类
    sizeof(Il2CppArray),
    ...
};
```

### System.Tuple`2 方法名 (36 处)

```cpp
// 错误:
System_Tuple_2_int_int_get_Item1_0dd84af6f1518716(...)
// 实际存在: System_Tuple_2_int_get_Item1_0dd84af6f1518716(...)

// 修复 - 在 MethodCppNameFromToken 中回退:
var mangledName = $"MethodCppName_Tuple_2_{args[0]}_{args[1]}";
if (!MethodExists(mangledName))
    mangledName = $"MethodCppName_Tuple_2_int";  // 默认 int 特化
```

### V_*_class (90 处)

```cpp
// 在 TypeEmitter 中为所有 value-type 生成 class 描述符前缀 V_:
Il2CppClass V_System_Guid_class = { ... };
Il2CppClass V_System_TimeSpan_class = { ... };
Il2CppClass V_System_DateTime_class = { ... };
// 等等...
```

---

## 修复优先级排序

### 第一批 (消除最多错误)
1. **EmitIcallCallArgs 数组参数统一处理** — 修复 313 处 Il2CppArray* 错误
2. **System.Array 内部方法参数适配** — 修复 17 处 InternalCreate/GetReference/SetValue

### 第二批 (泛型特化)
3. **ArraySegment/Nullable/ListBuilder 泛型退化** — 修复 ~2060 处
4. **Tuple 方法名回退** — 修复 36 处

### 第三批 (class 描述符)
5. **V_*_class 生成** — 修复 90 处
6. **SZARRAY_class 生成** — 修复 15 处

---

## 验证命令

```bash
# 编译验证
dotnet build src/il2cppc

# 重新生成并统计 Array 错误减少量
dotnet run --project src/il2cppc -- mscorlib.dll out/mscorlib_gen_r78

# 统计修复后 Array 错误数
Select-String -Path "out/mscorlib_gen_r78/src/*.cpp" -Pattern "error:" | Where-Object { $_.Line -match "Array|array" } | Measure-Object
```

---

## 影响文件清单

| 文件 | 错误类型 | 错误行号 |
|------|----------|----------|
| `System_Array.cpp` | il2cpp_array_get_length 类型不匹配 | 2451, 2623, 2808, 2809, 2832, 2868, 2869, 2883, 2937, 2941, 2954, 2992, 3041, 3129, 3517, 4214, 4217, 4259 |
| `System_Array_SorterGenericArray.cpp` | il2cpp_array_get_length 类型不匹配 | 406 |
| `System_Array_SZArrayEnumerator.cpp` | il2cpp_array_get_length 类型不匹配 | 118, 203 |
| `entry.cpp` | SZARRAY_class 未定义 | ~230 |
| `System_RuntimeType.cpp` | ListBuilder 未定义 | 多处 |
| `System_ArraySegment_1_int.cpp` | 方法签名不匹配 | 多处 |

---

## 修复后预期效果

- Array 相关错误: 313 → ~0 (P0 修复)
- 泛型特化错误: ~1457 → ~0 (P1 修复)
- 内部方法参数错误: 17 → 0 (P2 修复)
- V_class 错误: 90 → 0 (P2 修复)
- **总计消除: ~1877 处编译错误**
