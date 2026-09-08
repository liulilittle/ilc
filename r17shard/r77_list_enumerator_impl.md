# r77 System.Collections.Generic.List`1+Enumerator 修复方案

## 根因分析

### 数据流

```
IL: ldfld !0::_version (on Enumerator<T>)
    ↓
StackFold: SanitizeField("_version") → "_version"
    ↓
CodeGenerator: ((reinterpret_cast<Enumerator<T>*>(ptr)))->_version
    ↓
C++ 编译错误: no member named '_version' in 'Enumerator<T>'
```

### C++ 结构体定义 (generated.h)

```cpp
// Enumerator<int> — 字段名无前缀
struct System_Collections_Generic_List_1_Enumerator_int {
    Il2CppObject* list;    // offset 0
    int32_t index;         // offset 8
    int32_t version;       // offset 16  ← 没有下划线前缀
    int32_t current;       // offset 24  ← 没有下划线前缀
};

// List<int> — 字段名有下划线前缀
struct System_Collections_Generic_List_1_int {
    Il2CppObject** _items;  // offset 0
    int32_t _size;          // offset 8
    int32_t _version;       // offset 12
    // ...
};
```

### 核心问题

StackFold 的 `SanitizeField` 保留字段原名（如 `_version`），但 TypeEmitter 生成 C++ struct 时对 `version`/`index`/`current` 去除了下划线前缀。

**两个独立问题**：

| 问题 | IL 字段 | C++ 目标 | 错误数 |
|------|---------|----------|--------|
| A: 字段名不匹配 | `_version` / `_index` / `_current` | `version` / `index` / `current` | ~60 |
| B: 跨结构体访问 | `_size` / `_items` 在 Enumerator 上访问 | 这些字段属于 List<T> | ~30 |

---

## 修复方案

### 修复 1: StackFold 字段名映射（解决问题 A）

**文件**: `src/il2cppc/Ir/StackFold.cs`
**位置**: `SanitizeField` 方法 (line 4048)
**类型**: 单函数修改

**修改前**:
```csharp
static string SanitizeField(string name) => string.Join("_", name.Split('.', '+', '`', '<', '>', ',', '[', ']'));
```

**修改后**:
```csharp
static string SanitizeField(string name)
{
    var parts = name.Split('.', '+', '`', '<', '>', ',', '[', ']');
    var result = string.Join("_", parts);
    // Enumerator struct fields are emitted without underscore prefix
    // by TypeEmitter: _version -> version, _index -> index, _current -> current
    if (result == "_version") return "version";
    if (result == "_index") return "index";
    if (result == "_current") return "current";
    return result;
}
```

**验证**: 确保 `Enumerator<int>`, `Enumerator<string>`, `Enumerator<object>` 等所有特化的 `version`/`index`/`current` 字段都正确映射。

---

### 修复 2: 跨结构体字段间接访问（解决问题 B）

**文件**: `src/il2cppc/Ir/StackFold.cs`
**位置**: `Ldfld`/`Stfld` case (line 407-448)
**类型**: 增加类型感知逻辑

**修改前**:
```csharp
case ILOpCode.Ldfld:
    Push(new FieldExpr
    {
        Object = stack.Pop(),
        FieldCppName = SanitizeField(FieldToken(md, ins)),
        OwnerFullName = OwnerFullName(md, ins),
        IsRefField = IsRefField(md, ins),
        IsValueTypeObject = types != null && IsValueTypeOwner(OwnerFullName(md, ins), types)
    }, ins);
    break;
```

**修改后**:
```csharp
case ILOpCode.Ldfld:
{
    var ldfldObj = stack.Pop();
    var rawFieldName = FieldToken(md, ins);
    var sanitizedName = SanitizeField(rawFieldName);
    var ownerFullName = OwnerFullName(md, ins);
    
    // 检测跨结构体字段访问: Enumerator 方法体中访问 List<T> 的字段
    // 例如: Enumerator<int>.get_Current() 访问 _items/_size（属于 List<int>）
    var isEnumeratorAccessingListField = 
        ldfldObj is LocExpr leObj && 
        leObj.Name.StartsWith("arg") &&  // this pointer
        sanitizedName is "_size" or "_items" &&
        IsEnumeratorType(ownerFullName, types);
    
    string finalFieldName;
    if (isEnumeratorAccessingListField)
    {
        // 需要生成 obj->list->field 的形式
        // 这里先标记，在 CodeGenerator.EmitExpr(FieldExpr) 中处理
        finalFieldName = sanitizedName;
    }
    else
    {
        finalFieldName = sanitizedName;
    }
    
    Push(new FieldExpr
    {
        Object = ldfldObj,
        FieldCppName = finalFieldName,
        OwnerFullName = ownerFullName,
        IsRefField = IsRefField(md, ins),
        IsValueTypeObject = types != null && IsValueTypeOwner(ownerFullName, types)
    }, ins);
    break;
}

// 新增辅助方法
private static bool IsEnumeratorType(string fullName, List<TypeDef> types)
{
    return fullName.Contains("Enumerator") || 
           types.Any(t => t.FullName.Contains("Enumerator") && fullName.Contains(t.Name));
}
```

**说明**: 这个修改保留了跨结构体访问的标记，实际字段名转换在 CodeGenerator 的 `EmitExpr(FieldExpr)` 中完成。

---

### 修复 3: CodeGenerator 跨结构体字段发射（解决问题 B）

**文件**: `src\il2cppc\Emit\CodeGenerator.cs`
**位置**: `case FieldExpr f:` 分支 (line ~3341)
**类型**: 增加类型感知逻辑

**新增代码** (在 `case FieldExpr f:` 之前):
```csharp
case FieldExpr f:
{
    var objCode = EmitExpr(f.Object, md, types);
    var objType = ResolveExprCppType(f.Object, _curLocKinds, _curDeclaredCppType);
    
    // 检测跨结构体字段访问: 当字段不在当前结构体中时，通过 list 间接访问
    if (IsEnumeratorType(objType) && (f.FieldCppName == "_size" || f.FieldCppName == "_items"))
    {
        // Enumerator 的 _size/_items 属于 List<T>，需要 obj->list->field
        return $"(({objCode})->list->{f.FieldCppName})";
    }
    
    var baseAccess = $"(({objCode})->{f.FieldCppName})";
    
    if (f.IsRefField)
        return $"static_cast<Il2CppObject*>({baseAccess})";
    
    return baseAccess;
}
```

**新增辅助方法**:
```csharp
/// <summary>
/// 判断类型名是否为 Enumerator 类型
/// </summary>
private static bool IsEnumeratorType(string cppType)
{
    return cppType.Contains("Enumerator");
}

/// <summary>
/// 解析表达式的 C++ 类型（简化版，基于已知类型信息）
/// </summary>
private static string ResolveExprCppType(Expr e, 
    IReadOnlyDictionary<string, string> locKinds,
    IReadOnlyDictionary<string, string> declTypes)
{
    return e switch
    {
        LocExpr le when declTypes.TryGetValue(le.Name, out var cpp) => cpp,
        LocExpr le when le.Name.StartsWith("arg") => "Il2CppObject*",
        _ => "unknown"
    };
}
```

---

### 修复 4: 构造函数签名修正

**文件**: `src\il2cppc\Emit\CodeGenerator.cs`
**位置**: `EmitNewObj` 中 Enumerator 构造函数处理

**新增代码**:
```csharp
// 在 EmitNewObj 中检测 Enumerator 构造函数
if (cppName.Contains("Enumerator_") && cppName.Contains("__ctor"))
{
    // Enumerator 构造函数: void .ctor(List<T>* list)
    // 但 args[0] 是 Il2CppObject*（因为 this 是 Il2CppObject* 类型）
    // 需要 reinterpret_cast<List<T>*>
    var elemType = ExtractElementType(cppName);  // 从 cppName 提取 int/string/object 等
    var listType = $"System_Collections_Generic_List_1_{elemType}_*";
    if (callArgs.Count > 1)
    {
        callArgs[1] = $"reinterpret_cast<{listType}>({callArgs[1]})";
    }
}

// 新增辅助方法
private static string ExtractElementType(string enumeratorCppName)
{
    // 从 "System_Collections_Generic_List_1_Enumerator_int__ctor_xxx" 提取 "int"
    var match = System.Text.RegularExpressions.Regex.Match(
        enumeratorCppName, 
        @"Enumerator_([a-zA-Z0-9_]+?)__ctor");
    return match.Success ? match.Groups[1].Value : "object";
}
```

---

### 修复 5: Dispose 方法返回类型修正

**文件**: `src\il2cppc\Emit\CodeGenerator.cs`
**位置**: `VirtualReturnTypeCpp` 方法

**新增代码**:
```csharp
// 在 VirtualReturnTypeCpp 开头增加
var ownerName = GetOwnerFullName(md, token);
var methodName = GetMethodName(md, token);

if (ownerName.Contains("Enumerator") && methodName == "Dispose")
    return "void";
if (ownerName.Contains("Enumerator") && (methodName == "MoveNext" || methodName == "MoveNextRare"))
    return "int32_t";
```

---

## 修改文件清单

| 文件 | 修改点 | 行号 | 影响范围 |
|------|--------|------|----------|
| `StackFold.cs` | `SanitizeField` | 4048 | 所有 Enumerator 字段访问 |
| `StackFold.cs` | `Ldfld`/`Stfld` case | 407-448 | 跨结构体字段检测 |
| `CodeGenerator.cs` | `FieldExpr` 分支 | ~3341 | 跨结构体字段发射 |
| `CodeGenerator.cs` | `EmitNewObj` | ~3660 | 构造函数签名 |
| `CodeGenerator.cs` | `VirtualReturnTypeCpp` | ~4855 | 返回类型修正 |

---

## 修改优先级

| 优先级 | 修复 | 消除错误数 | 风险 |
|--------|------|------------|------|
| P0 | 修复 1: SanitizeField | ~60 | 低（只影响 Enumerator 字段名） |
| P0 | 修复 3: FieldExpr 间接访问 | ~30 | 中（需要类型推断） |
| P1 | 修复 4: 构造函数签名 | ~12 | 低 |
| P1 | 修复 5: 返回类型 | ~20 | 低 |
| P2 | 修复 2: Ldfld 类型感知 | 0（辅助） | 低 |

---

## 验证步骤

```bash
# 1. 编译验证
dotnet build src/il2cppc

# 2. 重新生成
dotnet run --project src/il2cppc -- mscorlib.dll out/mscorlib_gen_r78

# 3. 统计修复后 List.Enumerator 错误数
Select-String -Path "out/mscorlib_gen_r78/src/*.cpp" -Pattern "List_1_Enumerator" | Where-Object { $_.Line -match "error:" } | Measure-Object

# 4. 验证字段名正确性
Select-String -Path "out/mscorlib_gen_r78/src/System_Collections_Generic_List_1_Enumerator_int.cpp" -Pattern "->_version" | Should be 0
Select-String -Path "out/mscorlib_gen_r78/src/System_Collections_Generic_List_1_Enumerator_int.cpp" -Pattern "->version" | Should be > 0
```

---

## 边界情况

### 1. 嵌套泛型 Enumerator
```cpp
// List<List<int>>.Enumerator
struct System_Collections_Generic_List_1_Enumerator_System_Collections_Generic_List_1_int {
    Il2CppObject* list;
    int32_t index;
    int32_t version;
    Il2CppObject* current;  // List<int> as object
};
```
修复仍然适用：`_version` → `version`。

### 2. 自定义类型 Enumerator
```csharp
class MyList<T> { public Enumerator GetEnumerator(); }
```
如果 IL 中 `ldfld !0::_version` 在 `MyList<T>.Enumerator` 上，同样需要映射。

### 3. 跨结构体访问的替代方案
如果无法在 CodeGenerator 中推断类型，可以生成防御性代码：
```cpp
// 生成: (obj->list ? obj->list->_size : fallback)
// 或者: (static_cast<List_1_int*>(obj->list))->_size
```

---

## 预期效果

| 错误类型 | 修复前 | 修复后 |
|----------|--------|--------|
| `_version`/`_index`/`_current` 字段名不匹配 | ~60 | 0 |
| `_size`/`_items` 跨结构体访问 | ~30 | 0 |
| 构造函数签名不匹配 | ~12 | 0 |
| 返回类型不匹配 | ~20 | 0 |
| **总计** | **~122** | **0** |

---

## 备选方案（如果类型推断不可行）

如果无法在 StackFold 中准确推断类型，可以使用全局字段名去重策略：

```csharp
// 在 SanitizeField 中，对于所有已知歧义字段，统一去除下划线前缀
static string SanitizeField(string name)
{
    var result = string.Join("_", name.Split('.', '+', '`', '<', '>', ',', '[', ']'));
    // 这些字段在 TypeEmitter 中统一去除了下划线前缀
    return result switch
    {
        "_version" => "version",
        "_index" => "index", 
        "_current" => "current",
        "_size" => "size",      // 注意：只在 Enumerator 上下文中
        "_items" => "items",    // 注意：只在 Enumerator 上下文中
        _ => result
    };
}
```

**风险**: `_size` 和 `_items` 在其他类型中可能保留下划线前缀。需要确认 TypeEmitter 是否对所有类型都去除了前缀。
