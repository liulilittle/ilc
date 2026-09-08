# r77 System.Collections.Generic.List`1+Enumerator 错误修复方案

## 错误统计

| 错误类型 | 数量 |
|----------|------|
| List.Enumerator 总错误 | **122** |
| 字段名不匹配 (`_version`/`_size`/`_items` vs `version`/`index`/`current`) | **~60** |
| 返回类型不匹配 (`List<T>+Enumerator` → `Il2CppObject*`) | **~30** |
| V_ 前缀 value-type 引用 | **~20** |
| 构造函数签名不匹配 | **~12** |

---

## 根因分析

### 结构体定义 (generated.h:54374-54381)

```cpp
struct System_Collections_Generic_List_1_Enumerator_int {
    Il2CppObject* list;   // slot 0, offset 0
    int32_t index;        // slot 1, offset 8
    int32_t version;      // slot 2, offset 16
    int32_t current;      // slot 3, offset 24
};
```

### 生成的错误代码 (System_Collections_Generic_List_1_Enumerator_int.cpp)

```cpp
// 错误1: _version 不存在，应为 version
if((((reinterpret_cast<...*>(...)))->version == ((reinterpret_cast<...*>(...)))->_version) == 0LL) goto L_74;

// 错误2: _size 不存在（Enumerator 没有 _size，只有 List_1_int 有）
if((static_cast<uint32_t>(((reinterpret_cast<...*>(...)))->index) < static_cast<uint32_t>(((reinterpret_cast<...*>(...)))->_size)) == 0LL) goto L_74;

// 错误3: _items 不存在（Enumerator 没有 _items，只有 List_1_int 有）
((reinterpret_cast<...*>(...)))->current = (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(reinterpret_cast<Il2CppObject*>(((reinterpret_cast<...*>(...)))->_items)), ...));

// 错误4: 返回值类型不匹配
return System_Collections_Generic_List_1_Enumerator_int_Dispose_47b539e55adcc52c(arg0);  // 期望返回 Il2CppObject*

// 错误5: 构造函数参数不匹配
System_Collections_Generic_List_1_Enumerator_int__ctor_b9a5f5314a395852(arg0, arg1);  // arg1 是 Il2CppObject*，期望 System_Collections_Generic_List_1_int*
```

---

## 修复方案

### 方案 A: 修复字段名映射 (StackFold/CodeGenerator)

**文件**: `src/il2cppc/Ir/StackFold.cs` 或 `src/il2cppc/Emit/CodeGenerator.cs`

**问题**: 当 IL 指令 `ldfld !0::_version` 在 Enumerator 类型上执行时，StackFold 没有正确映射到 Enumerator 的 `version` 字段。

**修复**: 在 StackFold 的字段访问生成中，基于实际接收者类型而非原始声明类型解析字段。

```csharp
// 在 StackFold 中，当生成 ldfld/stfld 时:
// 错误做法: 总是生成 `_version`（来自 List_1_int 的字段名）
// 正确做法: 根据当前接收者类型解析字段名
//   如果接收者是 List_1_Enumerator_int → 使用 `version`
//   如果接收者是 List_1_int → 使用 `_version`
```

**修改点**: StackFold 中的字段访问重命名逻辑，增加类型感知。

---

### 方案 B: 修复 `_size`/`_items` 跨结构体访问

**问题**: Enumerator 方法体直接访问 `_size`/`_items`，但这些字段属于 `List_1_int`，不属于 Enumerator。

**修复**: 当 Enumerator 方法需要访问 `List_1_int` 的字段时，应该通过 `list` 字段间接访问：

```cpp
// 错误:
((Enumerator*)ptr)->_size
((Enumerator*)ptr)->_items

// 正确:
((Enumerator*)ptr)->list->_size
((Enumerator*)ptr)->list->_items
```

**修改点**: StackFold 中生成字段访问时，检查字段是否属于当前结构体，如果不属于，通过 `list` 字段间接访问。

---

### 方案 C: 修复返回类型不匹配

**问题**: `Dispose` 方法生成返回 `Il2CppObject*` 的签名，但实际返回 `void`。

**修复**: 在 TypeEmitter 中修正 Enumerator 方法的返回类型签名。

```cpp
// 错误:
Il2CppObject* System_Collections_Generic_List_1_Enumerator_int_Dispose_47b539e55adcc52c(System_Collections_Generic_List_1_Enumerator_int* arg0);

// 正确:
void System_Collections_Generic_List_1_Enumerator_int_Dispose_47b539e55adcc52c(System_Collections_Generic_List_1_Enumerator_int* arg0);
```

**修改点**: `TypeEmitter.EmitMethodReturnType` 中对 Enumerator 方法的返回类型处理。

---

### 方案 D: 修复构造函数签名

**问题**: Enumerator 构造函数期望 `List_1_int*` 作为第二个参数，但调用点传入 `Il2CppObject*`。

**修复**: 在 CodeGenerator 中生成构造函数调用时，对 Enumerator 的构造函数特殊处理。

```csharp
// 在 EmitNewObj 或 EmitCallStmt 中:
if (cppName.Contains("Enumerator_") && cppName.Contains("__ctor"))
{
    // Enumerator 构造函数需要 List<T>*，但 args[0] 是 Il2CppObject*
    // 需要 reinterpret_cast<List<T>*>
    var listType = "System_Collections_Generic_List_1_" + elemType + "*";
    callArgs[1] = $"reinterpret_cast<{listType}>({callArgs[1]})";
}
```

---

## 修复代码片段

### 片段 1: StackFold 字段访问类型感知

```csharp
// 文件: src/il2cppc/Ir/StackFold.cs
// 在生成 ldfld/stfld 时，增加类型感知

private string EmitFieldAccess(Expr objExpr, string fieldName)
{
    var objType = ResolveExprType(objExpr);
    
    // 如果接收者是 Enumerator，映射字段名
    if (objType.Contains("Enumerator"))
    {
        fieldName = fieldName switch
        {
            "_version" => "version",
            "_index" => "index",
            "_current" => "current",
            "_list" => "list",
            _ => fieldName
        };
        
        // 如果字段不在 Enumerator 中（如 _size/_items），通过 list 间接访问
        if (fieldName is "_size" or "_items")
        {
            // 返回 obj->list->field 的形式
            return $"({EmitFieldAccess(objExpr, "list")}->{fieldName})";
        }
    }
    
    return $"({EmitExpr(objExpr)})->{fieldName}";
}
```

### 片段 2: CodeGenerator Enumerator 返回类型修正

```csharp
// 文件: src/il2cppc/Emit/CodeGenerator.cs
// 在 VirtualReturnTypeCpp 或类似方法中

private static string VirtualReturnTypeCpp(MetadataReader md, int token)
{
    // 检测 Enumerator 方法
    var ownerName = GetOwnerFullName(md, token);
    if (ownerName.Contains("Enumerator"))
    {
        // Enumerator 方法的返回类型应该是其自身的值类型，不是 Il2CppObject*
        var methodName = GetMethodName(md, token);
        if (methodName == "Dispose")
            return "void";
        if (methodName == "MoveNext" || methodName == "MoveNextRare")
            return "int32_t";
        if (methodName == "get_Current")
            return "int32_t";  // 对于 List<int>
    }
    
    return VirtualReturnTypeCpp_original(md, token);
}
```

### 片段 3: 构造函数参数类型修正

```csharp
// 文件: src/il2cppc/Emit/CodeGenerator.cs
// 在 EmitNewObj 中

if (cppName.Contains("Enumerator_") && cppName.Contains("__ctor"))
{
    // Enumerator 构造函数: void .ctor(List<T>* list)
    // 但 args[0] 是 Il2CppObject*
    // 需要 reinterpret_cast
    var listCppType = $"System_Collections_Generic_List_1_{elemType}_*";
    return $"reinterpret_cast<{listCppType}>({callArgs[1]})";
}
```

### 片段 4: 间接字段访问

```csharp
// 文件: src/il2cppc/Emit/CodeGenerator.cs
// 在 EmitExpr(FieldExpr) 中

case FieldExpr f:
{
    var objCppType = GetExprCppType(f.Object);
    var fieldCppName = f.FieldCppName;
    
    // 如果字段不在当前结构体中，通过 list 间接访问
    if (!HasField(objCppType, fieldCppName) && HasField(objCppType, "list"))
    {
        var listFieldType = GetFieldCppType(objCppType, "list");
        if (HasField(listFieldType, fieldCppName))
        {
            return $"(({EmitFieldAccess(f.Object, "list")})->{fieldCppName})";
        }
    }
    
    return base.EmitExpr(f);
}
```

---

## 修复优先级

1. **P0**: 片段 1 (StackFold 字段映射) — 修复 60+ 处字段名错误
2. **P0**: 片段 4 (间接字段访问) — 修复 `_size`/`_items` 跨结构体访问
3. **P1**: 片段 2 (返回类型修正) — 修复 Dispose/MoveNext 返回类型
4. **P1**: 片段 3 (构造函数参数) — 修复 Enumerator 构造函数调用

---

## 预期修复效果

- 字段名错误: ~60 → 0
- 跨结构体访问错误: ~30 → 0
- 返回类型错误: ~20 → 0
- 构造函数错误: ~12 → 0
- **总计消除: ~122 处 List.Enumerator 编译错误**

---

## 受影响文件

| 文件 | 错误类型 | 数量 |
|------|----------|------|
| `System_Collections_Generic_List_1_Enumerator_int.cpp` | 字段名不匹配 | ~60 |
| `System_Collections_Generic_List_1_int.cpp` | 构造函数调用 | ~12 |
| `System_Collections_Generic_List_1_Byte.cpp` | 返回类型不匹配 | ~5 |
| `System_Collections_Generic_List_1_double.cpp` | 返回类型不匹配 | ~5 |
| `System_Collections_Generic_List_1_Enumerator_*.cpp` | V_ 前缀错误 | ~20 |
| `System_Collections_Generic_List_1_*.cpp` | 返回类型不匹配 | ~20 |
