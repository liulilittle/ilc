# r77 GenericDelegateCache 38错误完整修复方案

## 1. 错误日志分析

### 1.1 错误分布

| 错误类型 | 数量 | 来源 |
|---------|------|------|
| `GenericDelegateCache`2___c_*` static method 未声明 | 4 | entry.cpp (delegate_register_method) |
| `GenericDelegateCache`2___c_int_int` class 不存在(newobj) | 1 | GenericDelegateCache`2___c_int.cpp |
| `GenericDelegateCache`2___c_int_int_sf___9` 字段未声明 | 1 | GenericDelegateCache`2___c_int.cpp |
| `GenericDelegateCache`2___c_int_int_sf___9` 跨文件未声明 | 4 | GenericDelegateCache`2_int.cpp (stsfld) |
| **总计** | **10处错误(涉及38行)** | 2个文件集群 |

### 1.2 根因链

```
mscorlib 中的类型层次:
  System.Threading.Tasks.GenericDelegateCache`2<TKey, TValue>  (泛型定义)
    └── <>c__DisplayClass4_0<T>  (嵌套类，T是泛型参数)
        └── <.cctor>b__4_0/1/2/3  (静态方法，注册为delegate target)

r77 已生成的类型 (from generated.h):
  ✓ System_Threading_Tasks_GenericDelegateCache_2_int           (泛型实例 int)
  ✓ System_Threading_Tasks_GenericDelegateCache_2___c_int       (嵌套类实例 int)
  ✗ System_Threading_Tasks_GenericDelegateCache_2___c_int_int   (嵌套类实例 int,int) ← MISSING!
  ✗ System_Threading_Tasks_GenericDelegateCache_2___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_int ← MISSING!
```

### 1.3 具体错误映射

**错误组1: entry.cpp delegate_register_method (4 errors)**
```
System_Threading_Tasks_GenericDelegateCache_2___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_int___cctor_b__4_0_...
```
根因: `GenericDelegateCache`2<System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken, int>` 的嵌套类 `<>c__DisplayClass4_0` 未被展开。
- `<.cctor>b__4_0` 等方法被 `ldftn` 指令引用
- CodeGenerator 将 `ldftn` 方法地址注册到 `_delegateMethodTokens`
- `entry.cpp` 为这些方法生成 `il2cpp_delegate_register_method` 调用
- 但 `<>c__DisplayClass4_0<EventRegistrationToken,int>` 类型从未被展开为 TypeDef
- 因此这些静态方法的 C++ 符号从未被声明/定义

**错误组2: GenericDelegateCache`2___c_int.cpp (2 errors)**
```
System_Threading_Tasks_GenericDelegateCache_2_int_class           ← 应为 ___c_int_class
System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9  ← 字段在 ___c_int_int 上，未生成
```
根因: `<>.cctor>b__4_0` 等静态方法引用了 `GenericDelegateCache`2<int,int>` 类型的静态字段 `<>9`（缓存的单例实例）。
- IL 指令: `newobj GenericDelegateCache`2<<TKey>, <TValue>>.<>c__DisplayClass4_0<TKey,TValue>::.ctor()`
- 折叠器将 `TKey=int, TValue=int` 解析为 `System_Threading_Tasks_GenericDelegateCache_2___c_int_int`
- 但 `ExpandNestedTypes` 只处理 **当前m中已加载的** 嵌套类
- `GenericDelegateCache`2<int,int>` 这个**泛型实例**从未被创建为 TypeDef
- 所以 `___c_int_int` 类型的 struct、descriptor、静态字段都不存在

**错误组3: GenericDelegateCache`2_int.cpp stsfld (4 errors)**
```
System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9
```
根因: 同一字段的跨文件引用。`GenericDelegateCache`2<int,int>` 的 .cctor 需要初始化 `sf___9` 字段，但该字段所属类型未生成。

---

## 2. 根因定位

### 2.1 元数据加载层 (MetadataLoader.cs:34)
```csharp
foreach (var tdHandle in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(tdHandle);
    if (td.IsNested) continue; // ← M2 跳过所有嵌套类型
    ...
}
```
- `ExpandNestedTypes` (TypeEmitter.cs:90) 后续展开嵌套类
- 但**只展开当前 m已加载的** 嵌套类 (非泛型实例化的)
- 对于 `GenericDelegateCache`2<int>` 这种泛型实例，元数据中**不存在**对应的 TypeDefinitionHandle
- 它只在 `GenericDelegateCache`2`2` 的方法体中被引用 (通过 TypeSpec)

### 2.2 泛型实例化层 (缺失)
当前 pipeline:
```
MetadataLoader.LoadAssembly  →  只加载 TypeDef，不展开泛型实例
ExpandNestedTypes            →  展开已加载 TypeDef 的嵌套类
CodeGenerator.Generate       →  为每个 TypeDef 发射 struct + descriptor
```

缺失步骤: **泛型实例展开**
```
当 TypeSpec 引用 `GenericDelegateCache`2<int>` 时:
  - MetadataLoader 只解析名称，不创建 TypeDef
  - ExpandNestedTypes 不会遍历 TypeSpec
  - CodeGenerator 永远看不到 `GenericDelegateCache`2<int>` 这个 TypeDef
```

### 2.3 为什么 `int` 实例生成了但 `int,int` 没有？

看错误日志中的模式：
- `GenericDelegateCache`2<int>` ✅ 有 struct, descriptor, cctor
- `GenericDelegateCache`2<int,int>` ❌ 无 struct, descriptor, static fields

差异在于：
- `GenericDelegateCache`2<int>` 的 <>c 嵌套类在元数据中作为 TypeDef 存在（某些 Roslyn 版本会为单 arg 泛型生成嵌套类 TypeDef）
- `GenericDelegateCache`2<int,int>` 的 <>c 嵌套类**不在元数据中作为独立 TypeDef 存在**
- 它只在 `GenericDelegateCache`2<int,int>` 方法的 TypeSpec 引用中间接出现

---

## 3. 修复方案

### 3.1 方案名称: ExpandGenericInstantiations

在 `MetadataLoader.cs` 中新增一个 pass，在 `LoadAssembly` 完成后扫描所有方法体中的 TypeSpec 引用，自动创建缺失的泛型实例 TypeDef。

### 3.2 实现步骤

#### Step 1: 在 MetadataLoader.cs 中添加新 pass

```csharp
// LoadAssembly 完成后调用
public static void ExpandGenericInstantiations(MetadataReader md, List<TypeDef> types)
{
    var added = new List<TypeDef>();
    var byName = types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
    
    foreach (var t in types.ToList())
    {
        foreach (var m in t.Methods)
        {
            // 扫描方法体中的 TypeSpec 引用
            foreach (var typeName in CollectMethodGenericTypeSpecs(m))
            {
                if (byName.ContainsKey(typeName)) continue; // 已存在
                if (!IsGenericInstantiation(typeName)) continue;
                
                var (defName, argNames) = ParseGenericInstantiation(typeName);
                if (defName == null) continue;
                
                // 检查定义类型是否存在
                if (!byName.TryGetValue(defName, out var defType)) continue;
                
                // 创建泛型实例 TypeDef
                var instance = CreateGenericInstanceType(defType, argNames, md);
                if (instance == null) continue;
                
                added.Add(instance);
                byName[instance.FullName] = instance;
            }
        }
    }
    
    types.AddRange(added);
}
```

#### Step 2: 实现辅助方法

```csharp
/// <summary>
/// Collect all TypeSpec type names referenced in a method body.
/// This scans the method's IL for ldtoken, newobj, call, callvirt, isinst, castclass
/// instructions that reference TypeSpec handles.
/// </summary>
private static IEnumerable<string> CollectMethodGenericTypeSpecs(MethodDef m)
{
    // Read the method body
    var body = m.Owner?.Module?.Metadata?.GetMethodBody(m.Token);
    if (body == null) yield break;
    
    var ilReader = body.GetILReader();
    while (ilReader.Read())
    {
        var op = ilReader.OpCode;
        // Type-related opcodes: ldtoken(0xD0), newobj(0x73), call(0x28), callvirt(0x6F),
        // isinst(0x75), castclass(0x76), newarr(0x8D), ldelem(0xA3), stelem(0xAA)
        // For generic instantiation, we mainly care about newobj, call, callvirt, ldtoken
        
        // Decode operand and check for TypeSpec
        if (op.OperandType == OperandType.InlineMethod ||
            op.OperandType == OperandType.InlineType ||
            op.OperandType == OperandType.InlineTok)
        {
            // Resolve token and check if TypeSpec
            var handle = ilReader.ReadToken();
            if (handle.IsNil) continue;
            
            var h = MetadataTokens.Handle(handle.Handle);
            if (h.Kind == HandleKind.TypeSpecification)
            {
                var spec = md.GetTypeSpecification((TypeSpecificationHandle)h);
                var sigReader = md.GetBlobReader(spec.Signature);
                var typeByte = sigReader.ReadByte();
                if (typeByte == 0x15) // GENERICINST
                {
                    var name = GenericInstantiationName(md, ref sigReader);
                    yield return name;
                }
            }
        }
    }
}

/// <summary>
/// Parse "System.Threading.Tasks.GenericDelegateCache`2_int_int" into 
/// ("System.Threading.Tasks.GenericDelegateCache`2", ["int", "int"])
/// </summary>
private static (string? defName, string[]? argNames) ParseGenericInstantiation(string fullName)
{
    var tickIdx = fullName.IndexOf('`');
    if (tickIdx < 0) return (null, null);
    
    // Extract definition name (before `_`)
    var defName = fullName.Substring(0, tickIdx + 2); // include `2
    
    // Count generic arity
    var arityStr = "";
    var i = tickIdx + 2;
    while (i < fullName.Length && char.IsDigit(fullName[i]))
    {
        arityStr += fullName[i];
        i++;
    }
    if (!int.TryParse(arityStr, out var arity)) return (null, null);
    
    // Parse arguments (split by `_`)
    var argsPart = fullName.Substring(i);
    var argNames = argsPart.Split('_');
    if (argNames.Length != arity) return (null, null);
    
    return (defName, argNames);
}

/// <summary>
/// Create a synthetic TypeDef for a generic instantiation.
/// Copies methods and fields from the definition type, substituting type arguments.
/// </summary>
private static TypeDef? CreateGenericInstanceType(TypeDef defType, string[] argNames, MetadataReader md)
{
    var instanceName = defType.FullName + "_" + string.Join("_", argNames);
    
    var instance = new TypeDef
    {
        Name = instanceName,
        Namespace = "",
        IsGenericClosure = true,
        IsNested = false,
        IsInterface = defType.IsInterface,
        IsValueType = defType.IsValueType,
        BaseTypeName = defType.BaseTypeName,
        GenericArity = 0, // This is a concrete instantiation
        GenericArgs = argNames,
        GenericDefName = defType.Name,
        GenericDefNamespace = defType.Namespace,
    };
    
    // Copy instance fields (substitute type args)
    foreach (var f in defType.InstanceFields)
    {
        var newField = new FieldDef
        {
            Name = f.Name,
            Owner = instance,
            IsStatic = false,
            IsLiteral = false,
            ConstantValue = null,
            TypeKind = f.TypeKind,
            FieldTypeName = SubstituteTypeArgs(f.FieldTypeName, defType.GenericArgNames, argNames),
            SlotIndex = f.SlotIndex,
        };
        instance.InstanceFields.Add(newField);
    }
    
    // Copy static fields
    foreach (var f in defType.StaticFields)
    {
        var newField = new FieldDef
        {
            Name = f.Name,
            Owner = instance,
            IsStatic = true,
            IsLiteral = f.IsLiteral,
            ConstantValue = f.ConstantValue,
            TypeKind = f.TypeKind,
            FieldTypeName = f.FieldTypeName,
        };
        instance.StaticFields.Add(newField);
    }
    
    // Copy methods (substitute type args in signatures)
    foreach (var m in defType.Methods)
    {
        var newMethod = new MethodDef
        {
            Name = m.Name,
            Owner = instance,
            IsStatic = m.IsStatic,
            IsPublic = m.IsPublic,
            ParamCount = m.ParamCount,
            ParamTypes = SubstituteTypeArgsInSignature(m, defType.GenericArgNames, argNames),
            ReturnType = m.ReturnType,
            ReturnTypeName = m.ReturnTypeName,
            IsVirtual = m.IsVirtual,
            IsOverride = m.IsOverride,
            IsAbstract = m.IsAbstract,
            IsFinal = m.IsFinal,
            IsPInvoke = m.IsPInvoke,
            PinvokeModule = m.PinvokeModule,
            PinvokeEntryName = m.PinvokeEntryName,
            PinvokeFlags = m.PinvokeFlags,
            PinvokeSetLastError = m.PinvokeSetLastError,
            GenericArity = 0,
            Token = m.Token,
        };
        instance.Methods.Add(newMethod);
    }
    
    // Expand nested types (like <>c__DisplayClass4_0)
    foreach (var nested in defType.NestedTypes)
    {
        var nestedInstance = CreateNestedTypeInstance(nested, defType, argNames);
        if (nestedInstance != null)
        {
            instance.NestedTypes.Add(nestedInstance);
        }
    }
    
    return instance;
}
```

#### Step 3: 在 Program.cs 中调用新 pass

```csharp
// After ExpandNestedTypes, before CodeGenerator.Generate
TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
TypeEmitter.ExpandGenericInstantiations(pe.GetMetadataReader(), types); // NEW
```

#### Step 4: 修改 MetadataLoader.cs 不跳过泛型实例化

```csharp
// Line 34: 修改条件
if (td.IsNested && !td.BaseType.IsNil) continue;
// 改为: 也跳过已知的BCL泛型定义 (它们的实例由 ExpandGenericInstantiations 处理)
if (td.IsNested && !td.BaseType.IsNil) continue;
```

#### Step 5: 修改 ExpandNestedTypes 以处理泛型实例中的嵌套类

```csharp
// 在 ExpandNestedTypes 中添加:
// 对于泛型实例化类型 (IsGenericClosure = true)，
// 其嵌套类需要展开为独立的 TypeDef
if (nested.DeclaringTypeName != null && nested.DeclaringTypeName.Contains("_int_"))
{
    // 这是一个泛型实例化的嵌套类，需要展开
    var expanded = ExpandNestedGenericType(nested, types);
    if (expanded != null) added.Add(expanded);
}
```

### 3.3 关键数据结构修改

#### TypeDef.cs 添加新属性
```csharp
/// <summary>
/// For generic instance types: the type arguments.
/// e.g., GenericDelegateCache`2<int,int> has GenericArgs = ["int", "int"]
/// </summary>
public string[]? GenericArgs { get; set; }

/// <summary>
/// For generic instance types: the definition type name.
/// </summary>
public string? GenericDefName { get; init; }
public string? GenericDefNamespace { get; init; }

/// <summary>
/// The generic parameter names from the definition.
/// e.g., GenericDelegateCache`2 has GenericArgNames = ["TKey", "TValue"]
/// </summary>
public string[]? GenericArgNames { get; set; } = Array.Empty<string>();
```

### 3.4 类型替换逻辑

```csharp
/// <summary>
/// Substitute type argument names in a type reference.
/// e.g., "TKey" → "int" when TKey maps to "int"
/// </summary>
private static string SubstituteTypeArgs(string? typeName, string[]? defArgs, string[] argNames)
{
    if (string.IsNullOrEmpty(typeName)) return typeName ?? "";
    if (defArgs == null || argNames == null) return typeName;
    
    for (int i = 0; i < defArgs.Length && i < argNames.Length; i++)
    {
        if (typeName == defArgs[i]) return argNames[i];
    }
    
    // Handle GenericInst patterns like `1<TKEY>
    if (typeName.Contains("`"))
    {
        // Recursively substitute in generic instantiation names
        // e.g., `1[TKey] → `1[int]
    }
    
    return typeName;
}

private static string SubstituteTypeArgsInSignature(MethodDef m, string[]? defArgs, string[] argNames)
{
    // Parse ParamTypes, substitute, and rejoin
    var parts = m.ParamTypes.Split(',');
    var result = new List<string>();
    foreach (var part in parts)
    {
        if (part.StartsWith("r:"))
        {
            result.Add("r:" + SubstituteTypeArgs(part[2..], defArgs, argNames));
        }
        else
        {
            result.Add(SubstituteTypeArgs(part, defArgs, argNames));
        }
    }
    return string.Join(",", result);
}
```

---

## 4. 修复涉及的文件

| 文件 | 行号 | 修改 |
|------|------|------|
| `MetadataLoader.cs` | ~Line 34 | 修改 IsNested 跳过逻辑，允许泛型实例化嵌套类 |
| `MetadataLoader.cs` | ~Line 111 | 添加 `ExpandGenericInstantiations` 方法 |
| `TypeEmitter.cs` | ~Line 48 | 添加 `ExpandGenericInstantiations` 公共入口 |
| `TypeEmitter.cs` | ~Line 90 | 修改 `ExpandNestedTypes` 以处理泛型实例 |
| `TypeDef.cs` | ~Line 95 | 添加 `GenericArgs`, `GenericDefName`, `GenericArgNames` 属性 |
| `Program.cs` | ~Line 43 | 在 `ExpandNestedTypes` 后调用新 pass |

---

## 5. 验证计划

1. **单元测试**: 验证 `GenericDelegateCache`2<int,int>` TypeDef 被正确创建
2. **编译测试**: 验证 `System_Threading_Tasks_GenericDelegateCache_2___c_int_int` struct 和 descriptor 被生成
3. **r77回归**: 重新运行 r77 编译，确认 GenericDelegateCache 错误归零
4. **边界测试**: 验证其他泛型嵌套类 (如 `Lazy`1<ConcurrentDictionary`2<int,int>>`) 不受影响

---

## 6. 实施优先级

| 步骤 | 依赖 | 预计工作量 |
|------|------|-----------|
| 1. TypeDef.cs 添加属性 | 无 | 0.5h |
| 2. MetadataLoader 泛型实例扫描 | Step 1 | 2h |
| 3. TypeEmitter ExpandGenericInstantiations | Step 2 | 1.5h |
| 4. Program.cs 调用新 pass | Step 3 | 0.5h |
| 5. 类型替换逻辑 | Step 1 | 1h |
| 6. 测试验证 | All | 1h |
| **总计** | | **6.5h** |

---

## 7. 备选方案 (如果上述太复杂)

### 方案B: 最小补丁法

直接为 r77 中缺失的类型手动添加硬编码的 TypeDef 创建:

```csharp
// 在 ExpandNestedTypes 末尾添加:
var hardcodedTypes = new Dictionary<string, string[]>
{
    ["System_Threading_Tasks_GenericDelegateCache_2___c_int_int"] = 
        new[] { "System.Threading.Tasks.GenericDelegateCache`2", "int_int" },
    ["System_Threading_Tasks_GenericDelegateCache_2___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_int"] = 
        new[] { "System.Threading.Tasks.GenericDelegateCache`2", "System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken_int" },
};

foreach (var kv in hardcodedTypes)
{
    // 手动创建 TypeDef...
}
```

优点: 快速修复 r77
缺点: 不通用，其他泛型嵌套类仍有同样问题

---

## 8. 推荐方案

**推荐方案A (ExpandGenericInstantiations)**，原因:
1. 通用: 处理所有泛型嵌套类实例化问题
2. 不侵入: 新 pass 独立，不影响现有逻辑
3. 可扩展: 未来可用于其他泛型场景
4. 类型安全: 完整替换类型参数，避免签名漂移
