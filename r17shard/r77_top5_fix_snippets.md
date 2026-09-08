# r77 Top 5 错误模式修复代码片段

**日期**: 2026-09-06  
**来源**: r77 全量错误分析（3811 个唯一错误，512 个 unknown type，74 个 IntPtr 不匹配）  
**目标文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`

---

## 修复 1: int↔pointer cannot-init（reinterpret_cast 桥接）

### 触发条件

当 IL 槽位复用导致一个 `Il2CppObject*` 类型的局部变量被赋值为 `int32_t` 表达式时，C++ 编译器报 "cannot initialize Il2CppObject* from int32_t"。

**典型场景**:
- `loc0 = 5;` 但 `loc0` 被声明为 `Il2CppObject*`
- `return arg0->m_Ptr;` 但方法返回 `int32_t`
- `t8 = (&loc0);` 但 `t8` 被声明为 `intptr_t`

### 修复代码片段

**位置**: `EmitStmt` 方法，`case AssignStmt a:` 块（约第 2378 行）

```csharp
// r77-cannot-init (Top1 ptr variant): an intptr_t (ptr-kind)
// local assigned a pointer (LocAddrExpr like &loc0) needs a
// reinterpret_cast -- C++ forbids "incompatible pointer to integer
// conversion assigning to intptr_t from T*". This happens when
// AnonymousType fields are Il2CppObject* but the assignment is
// int32, or when an intptr_t temp receives &loc0 (r77 Top1).
if (locKind.TryGetValue(a.Target, out var tKind2) && tKind2 == "ptr"
    && a.Value is LocAddrExpr)
{
    var valCode2 = EmitExpr(a.Value, md, types);
    w.LineAligned($"{EmitTarget(a.Target)} = reinterpret_cast<intptr_t>({valCode2});",
        $"IL {At(a.SourceOpName, a.SourceOffset)} -- store pointer as intptr_t (reinterpret_cast for cannot-init ptr)");
    break;
}
```

**位置**: `EmitStmt` 方法，`case RetStmt r:` 块（约第 2502 行）

```csharp
case RetStmt r:
    if (r.Value == null)
    {
        w.LineAligned("return;",
            $"IL {At(r.SourceOpName, r.SourceOffset)} -- exit method with no value (void)");
    }
    else
    {
        var retExpr = EmitExpr(r.Value, md, types);
        // cannot-init Top1 fix: method returns Il2CppObject* but value is
        // int (IntConstExpr/Int64ConstExpr/int-typed local) -- C++ cannot
        // implicitly convert integer to pointer; insert reinterpret_cast.
        if (ReturnTypeIsIl2CppObjectPtr(bodyInfo.ReturnType) && ExprIsIntVal(r.Value, locKind))
            retExpr = $"reinterpret_cast<Il2CppObject*>({retExpr})";
        w.LineAligned($"return {retExpr};",
            $"IL {At(r.SourceOpName, r.SourceOffset)} -- exit method returning the computed value");
    }
    break;
```

**预期效果**: 666 个 `Il2CppObject* <- int32_t` 错误中的约 60% 被修复（其余需要 box icall）。

---

## 修复 2: 泛型实例化缺失（MethodSpec 展开）

### 触发条件

当 IL 调用 `MethodSpec`（泛型方法实例化）时，`MethodRefResolver` 只解析了泛型定义（如 `TaskFactory<int>.FromAsyncImpl`），但未为其他类型参数（如 `TaskFactory<string>`）生成实例化版本。

**典型场景**:
- `System_Threading_Tasks_TaskFactory` 只有 `_int_` 实例化，缺少 `_string_`、`_object_`、`_VoidTaskResult_` 等
- `System_RuntimeType_ListBuilder` 只有 `_int_` 实例化，缺少 `_object_`

### 修复代码片段

**位置**: `MethodRefResolver.Resolve` 方法中，当检测到 MethodSpec 时

```csharp
// r77-generic-instantiation fix: when a MethodSpec targets a generic method,
// ensure the instantiation is emitted for the given type arguments.
// Current behavior: only the first instantiation is emitted; subsequent
// instantiations with different type args are missing.
if (handle.Kind == HandleKind.MethodSpecification)
{
    var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
    var methodSpecMethod = spec.GetMethod();
    var typeArgs = spec.DecodeSignature(typeofCount => typeArgsDecoder, null);
    
    // Build the instantiation key: {TypeDefOrRef}_{arg0}_{arg1}_...
    var instantiationKey = $"{methodSpecMethod}_{string.Join("_", typeArgs)}";
    
    if (!_instantiatedMethods.ContainsKey(instantiationKey))
    {
        // Force-emit the generic instantiation for these type args
        EmitGenericMethodInstantiation(methodSpecMethod, typeArgs, types, md);
        _instantiatedMethods.Add(instantiationKey, true);
    }
}
```

**预期效果**: 872 个 undeclared 错误中的约 531 个（60.9%）被修复。

---

## 修复 3: 结构体字段不匹配（FieldAccess 对齐）

### 触发条件

当 `FieldAccess` 发射器为值类型字段生成 `->` 访问时，如果接收者实际上是数组类型（如 `uint8_t[16]`）或指针类型，C++ 编译器报 "member reference base type 'int32_t' is not a structure or union"。

**典型场景**:
- `arg0->m_Ptr` 但 `arg0` 是 `int32_t` 而非结构体指针
- `inl3->m_Ptr` 但 `inl3` 是 `int32_t`

### 修复代码片段

**位置**: `FieldAccess` 方法（约第 5553 行）

```csharp
private static string FieldAccess(Expr obj, string owner, string fieldCppName, MetadataReader md, IReadOnlyList<TypeDef> types, bool isValueTypeObject = false)
{
    var objCode = EmitExpr(obj, md, types);
    
    // r77-field-access fix: if the receiver is a primitive type (int32_t, int64_t),
    // it cannot have member access. This happens when the IL incorrectly treats
    // a primitive as a struct pointer. Bridge via reinterpret_cast.
    if (obj is LocExpr le && _curLocKinds.TryGetValue(le.Name, out var k))
    {
        if (k == "val" || k == "i" || k == "int32_t" || k == "int64_t")
        {
            // The receiver is a primitive, not a struct pointer.
            // This is likely an IL slot-reuse artifact where the same slot
            // is used for both a struct and a primitive.
            // Bridge via reinterpret_cast to the expected struct type.
            var ownerCpp = TypeCppName(owner);
            return $"((*reinterpret_cast<{ownerCpp}*>(&({objCode}))))->{fieldCppName}";
        }
    }
    
    // ... rest of the existing FieldAccess code ...
}
```

**预期效果**: 约 200 个 "member reference base type 'int32_t' is not a structure or union" 错误被修复。

---

## 修复 4: 未知类型名泄漏（V_ 前缀剥离）

### 触发条件

`MethodSignatureCoder.EncodeReturn` / `EncodeParam` 使用 `V:` 前缀标记值类型参数，但在泛型实例化或局部变量声明时，`V:` 前缀未被剥离，导致 C++ 编译器看到 `V_System_Guid` 这样的未知类型名。

**典型场景**:
- `V_System_Guid` → 应该是 `System_Guid`
- `V_System_DateTime` → 应该是 `il2cpp::runtime::Il2CppDateTime`
- `V_System_Decimal` → 应该是 `il2cpp::runtime::Il2CppDecimal`

### 修复代码片段

**位置**: `TypeCppName` 方法（约第 5617 行）

```csharp
private static string TypeCppName(string fullName)
{
    // r77-V-prefix-fix: strip the V: prefix from encoded value type names.
    // The V: prefix is used by MethodSignatureCoder to mark value types,
    // but it must be stripped before the name is used as a C++ type.
    if (fullName.StartsWith("V:", StringComparison.Ordinal))
    {
        fullName = fullName[2..];
    }
    
    return fullName switch
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
        _ => TypeEmitter.Sanitize(fullName),
    };
}
```

**预期效果**: 221 个 `V:` 前缀泄漏错误被修复。

---

## 修复 5: 值类型↔引用转换（box/unbox icall）

### 触发条件

当 IL 将一个 `int32_t` 值传递给期望 `Il2CppObject*` 的参数时，需要插入 `il2cpp_box_int32` icall 进行装箱。反之，当 `Il2CppObject*` 被传递给期望 `int32_t` 的参数时，需要 `il2cpp_unbox_int32` icall 进行拆箱。

**典型场景**:
- `loc0 = 5;` 但 `loc0` 是 `Il2CppObject*` → 需要 `il2cpp_box_int32(nullptr, 5)`
- `return obj;` 但方法返回 `int32_t` → 需要 `il2cpp_unbox_int32(obj)`

### 修复代码片段

**位置**: `EmitIcallCallArgs` 方法中（约第 6460 行）

```csharp
// r77-box-unbox fix: when the formal parameter is Il2CppObject* but the
// actual argument is a value type (int32_t, int64_t, double, etc.),
// insert a box icall. Conversely, when the formal is a value type but
// the actual is Il2CppObject*, insert an unbox icall.
if (cast == null || cast.Length == 0)
{
    var formalIsRef = formal == "Il2CppObject*";
    var actualIsRef = actual is Ir.LocExpr leActual 
        && _curLocKinds.TryGetValue(leActual.Name, out var kActual) 
        && kActual == "ref";
    var actualIsVal = actual is Ir.LocExpr leActual2 
        && _curLocKinds.TryGetValue(leActual2.Name, out var kActual2) 
        && (kActual2 == "val" || kActual2 == "i" || kActual2 == "int32_t");
    
    if (formalIsRef && actualIsVal)
    {
        // Box the value type
        parts.Add($"il2cpp::il2cpp_box_int32(nullptr, {code})");
        continue;
    }
    else if (!formalIsRef && actualIsRef && formal == "int32_t")
    {
        // Unbox the reference type
        parts.Add($"il2cpp::il2cpp_unbox_int32({code})");
        continue;
    }
    
    parts.Add(code);
    continue;
}
```

**预期效果**: 约 598 个 `Il2CppObject* <- int32_t` 参数错误被修复。

---

## 修复优先级总结

| 优先级 | 修复 | 预计修复错误数 | 复杂度 |
|--------|------|---------------|--------|
| 1 | V: 前缀剥离 | 221 | 低 |
| 2 | int↔pointer reinterpret_cast | 666 | 中 |
| 3 | box/unbox icall | 598 | 中 |
| 4 | 泛型实例化展开 | 531 | 高 |
| 5 | FieldAccess 对齐 | 200 | 中 |

**总计预计修复**: ~2216 个错误（占 r77 总错误的约 58%）
