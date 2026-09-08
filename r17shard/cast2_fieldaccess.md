# cast-2 FieldAccess 根因调查报告

## 1. 症状

r77 扫描中 FieldAccess 路径生成的 C++ 代码出现类型不匹配，核心错误模式：

```
error: incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *'
error: cannot initialize a parameter of type 'int32_t' with an lvalue of type 'Il2CppObject *'
error: assigning to 'intptr_t' from incompatible type 'System_IntPtr'
```

代表文件：
- `__f__AnonymousType0_1_int.cpp:194` — `intptr_t = int32_t*` (cannot-cast)
- `System_AppDomain.cpp:4448` — `intptr_t = System_Nullable_1_int*` (cannot-cand
- `System_AppDomain.cpp:6619` — `intptr_t = Il2CppObject*`
- `Microsoft_Win32_RegistryKey.cpp:1340` — `intptr_t = System_IntPtr`
- `System_CurrentSystemTimeZone.cpp:557` — `intptr_t = System_DateTime*`
- `System_DateTime.cpp:2865` — `intptr_t = System_DateTime_FullSystemTime*`

## 2. 根因

### 2.1 CAST-1 已实现

`CodeGenerator.cs:5593` (旧行号 7160) 的 FieldAccess 函数已实现 CAST-1 修复：

```csharp
// CAST-1: reinterpret_cast<T> requires a parenthesized operand
return $"(({(ownerCpp == "Il2CppObject" ? "static_cast" : "reinterpret_cast")}<{ownerCpp}*>(il2cpp_null_check(static_cast<Il2CppObject*>({objCode})))))->{fieldCppName}";
```

CAST-1 处理的是：从 `Il2CppObject*` → 具体结构体指针 的 reinterpret_cast。

### 2.2 cast-2 新问题：反向场景（结构体指针 → 整数类型）

r77 新出现的错误模式是反向的：

```
intptr_t field = <Il2CppObject* 表达式>;   // 不能隐式转换
intptr_t field = <struct指针表达式>;       // 不能隐式转换
int32_t field = <Il2CppObject* 表达式>;   // 不能隐式转换
```

这些错误发生在 **FieldAssignStmt** 路径中，具体是 FieldAccess 返回值被赋给一个整数类型的目标（intptr_t / int32_t）。

### 2.3 问题位置

**发射点 1：FieldExpr 读路径** (`CodeGenerator.cs:3268`)
```csharp
case FieldExpr f:
    var fval = $"{FieldAccess(f.Object, f.OwnerFullName, f.FieldCppName, md, types, f.IsValueTypeObject)}";
    return f.IsRefField ? $"reinterpret_cast<Il2CppObject*>({fval})" : fval;
```

此处 FieldAccess 返回 `((reinterpret_cast<T*>(il2cpp_null_check(...))))->field`，当 field 是 intptr_t 类型但 ownerCpp 生成的结构体字段声明在 C++ 侧为 intptr_t 时，读回时没有问题。**但当 FieldExpr 本身被用于整数上下文中（如 IntPtr 类型字段），上层赋值目标仍是 intptr_t，而 FieldAccess 返回的是 struct member access 表达式，C++ 侧字段类型匹配，无 cast-2 问题。**

**发射点 2：FieldAssignStmt 写路径** (`CodeGenerator.cs:2516+`)
```csharp
var faValueCpp = TypeCppName(fa.RefTargetFullName);
var faValue = fa.IsRefField
    ? (faValueCpp == "Il2CppObject"
        ? $"static_cast<{faValueCpp}*>({EmitExpr(fa.Value, md, types)})"
        : NullSafePtrCast($"{faValueCpp}*", EmitExpr(fa.Value, md, types)))
    : EmitExpr(fa.Value, md, types);
var faTarget = FieldAccess(fa.Object, fa.OwnerFullName, f.FieldCppName, md, types, fa.IsValueTypeObject);
```

**cast-2 根因：FieldAssignStmt 写路径中，目标字段是值类型（如 intptr_t/IntPtr），但 EmitExpr(fa.Value, md, types) 产生 Il2CppObject*。**

关键观察：
- `fa.IsRefField == false` 时走 `EmitExpr(fa.Value, md, types)` 直接值拷贝
- 但 `fa.Value` 可能是引用表达式（如 ldloc 加载了一个引用类型的 temp），而目标字段是 intptr_t
- 这导致 C++ 侧 `int32_t x = <Il2CppObject*>` 或 `intptr_t x = <struct*>`

**但错误日志显示错误发生在 FieldAccess 的结果赋值给 intptr_t 的上下文中。**

实际出错行位于：
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_AppDomain.cpp:4448
```

该行是 FieldAssignStmt 写回路径，其中 FieldAccess 生成的目标指针 `((reinterpret_cast<System_AppDomain*>(...)))->某字段` 是 intptr_t 类型，但右侧 `fa.Value` 的 EmitExpr 生成了 Il2CppObject*。

**真正的 cast-2 根因：**

在 `FieldAccess` 函数中，CAST-1 已经将 `Il2CppObject*` 正确 reinterpret_cast 到目标结构体指针，但当目标结构体本身是**值类型**（如 System.IntPtr → intptr_t, System.DateTime → Il2CppDateTime），字段访问表达式的结果类型是 intptr_t/Il2CppDateTime。

r77 新出现的场景是：**FieldAccess 被用在需要 Il2CppObject* 的上下文中**，或者**值类型的 FieldAccess 结果被赋给 intptr_t 时缺少从结构体指针到整数的 reinterpret_cast**。

具体而言：

1. **System.IntPtr 类型字段的 C++ 表示是 intptr_t**（TypeCppName 返回 "intptr_t"）
2. 当 FieldAccess 访问 System.IntPtr 类型的实例字段时，ownerCpp 变成 "intptr_t"
3. `reinterpret_cast<intptr_t*>(il2cpp_null_check(...))` —— **这是非法的！intptr_t 不是指针类型，不能 reinterpret_cast 到 intptr_t***

**这就是 cast-2 错误：CAST-1 的 `reinterpret_cast<{ownerCpp}*>` 当 ownerCpp 为值类型名称（intptr_t 等）时生成不合法 C++。**

## 3. 证据链

| 证据 | 位置 |
|------|------|
| CAST-1 实现 | `CodeGenerator.cs:5593` |
| CAST-1 注释 | `CodeGenerator.cs:5577-5583` |
| FieldAssignStmt 路径 | `CodeGenerator.cs:2516-2527` |
| FieldExpr 路径 | `CodeGenerator.cs:3268-3269` |
| TypeCppName 映射 intptr_t | `CodeGenerator.cs:5673-5674` |
| TypeCppName 映射 Il2CppDateTime | `CodeGenerator.cs:5667` |
| 错误行：intptr_t = struct* | r77shard1_shard1_errors.log:1 |
| 错误行：intptr_t = Il2CppObject* | r77shard1_shard1_errors.log:60 |

## 4. 修复方案

### 方案 A（推荐）：值类型字段 owner 跳过 CAST-1

在 FieldAccess 中，当 owner 是值类型时，不经过 `reinterpret_cast<T*>` 路径，因为值类型字段不属于引用类型对象布局。

修改 `CodeGenerator.cs:5593`：

```csharp
// CAST-1 + cast-2: reinterpret_cast<T*> requires T to be a pointer-compatible type.
// Value-type owners (System.IntPtr -> intptr_t, System.DateTime -> Il2CppDateTime)
// are NOT pointer types -- reinterpret_cast<intptr_t*> is ill-formed. Skip the
// reference-cast for value-type owners; the field access goes through the
// isValueTypeObject arm or needs a dedicated value-type path.
return $"(({(ownerCpp == "Il2CppObject" ? "static_cast" : "reinterpret_cast")}<{ownerCpp}*>(il2cpp_null_check(static_cast<Il2CppObject*>({objCode})))))->{fieldCppName}";
```

→ 修改为（检测值类型 owner 时走 isValueTypeObject 路径）：

```csharp
if (ownerCpp is "intptr_t" or "uintptr_t" or "int32_t" or "int64_t" or "uint32_t" or "uint64_t" or "float" or "double")
{
    // Value-type owner: the receiver must be boxed/unboxed or carried in a
    // value-type slot. Go through the value-type object path.
    return $"({(obj is LocAddrExpr || (obj is LocExpr le && le.Name.StartsWith("arg", StringComparison.Ordinal))
        ? $"{objCode}->{fieldCppName}"
        : $"(&({objCode}))->{fieldCppName}")}";
}
```

### 方案 B：在 FieldAssignStmt 写路径加 cast-2 转换

在 `CodeGenerator.cs:2520` 之后，检测 faTarget 类型与 faValue 类型是否匹配，不匹配时插入 reinterpret_cast：

```csharp
// cast-2: value-type field assigned from Il2CppObject* needs reinterpret_cast
if (!fa.IsRefField && faValueCpp != "Il2CppObject")
{
    var faValueKind = ExprKind(fa.Value, locKind);
    if (faValueKind == "ref")
    {
        faValue = $"reinterpret_cast<{faValueCpp}>({EmitExpr(fa.Value, md, types)})";
    }
}
```

## 5. 实施建议

1. 先确认 r77 错误中 cast-2 的具体文件（抽查 System_AppDomain.cpp:4448 附近代码）
2. 检查 FieldAccess 的 ownerCpp 在该场景下的值
3. 走方案 A（修改 FieldAccess）更根本，避免值类型 owner 走 reinterpret_cast 指针路径
4. W3 备份 CodeGenerator.cs
5. 修改后 `dotnet build src/il2cppc` 确认 0 错
6. r77 重扫验证 cast-2 错误消失

## 6. 风险

- 方案 A 需要区分"值类型 owner 但引用传递"的特殊场景（如 IntPtr 作为引用对象的字段）
- 若 owner 是值类型但存储在引用对象中（boxed），仍需要 CAST-1 的 reinterpret_cast
- 建议增加 boxed-value-type 检测：owner 是值类型 && obj 是引用类型 → 仍走 CAST-1
