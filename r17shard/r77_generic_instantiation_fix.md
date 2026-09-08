# r77 泛型实例化缺失修复方案

## 问题根因

转译器当前的泛型处理策略是**只 emit `int`（默认）实例化**：

```csharp
// TypeEmitter.cs:410-417
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    // M3 generics (Task 7): a generic definition emits ONE instantiation
    // with T -> int32_t. The instantiation name appends "_int"
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```

这导致：
- `ListBuilder<object>` → 引用 `ListBuilder_1_object`，但只生成了 `ListBuilder_1_int`
- `TaskFactory<string>` → 引用 `TaskFactory_1_string`，但只生成了 `TaskFactory_1_int`
- `Nullable<ReadOnlyMemory<byte>>` → 引用 `Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte`，但只生成了基础类型版本

---

## 修复方案：三阶段泛型实例化展开

### 阶段 1：MetadataLoader 扫描 MethodSpec/TypeSpec

在 `LoadAssembly` 的第二 pass 之后，添加第三 pass：扫描 MethodSpec 和 TypeSpec 表，为每个被引用的泛型实例化创建 TypeDef。

```csharp
// MetadataLoader.cs - 新增方法

/// <summary>
/// r78 fix: Scan MethodSpec and TypeSpec tables to discover all generic
/// instantiations referenced by the assembly. For each unique instantiation,
/// create a TypeDef entry so CodeGenerator emits the corresponding struct
/// and method bodies.
/// </summary>
private static void ExpandGenericInstantiations(MetadataReader md, List<TypeDef> types)
{
    var byName = types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
    var added = new HashSet<string>(StringComparer.Ordinal);
    
    // Scan MethodSpec table: each row is a generic method instantiation
    foreach (var msHandle in md.MethodSpecifications)
    {
        var ms = md.GetMethodSpecification(msHandle);
        var method = md.GetMethodDefinition(ms.Method);
        var owner = method.GetDeclaringType();
        var ownerName = md.GetFullName(owner);
        
        // Parse the generic args from the MethodSpec signature
        var sig = md.GetBlobReader(ms.Signature);
        var header = sig.ReadByte();
        if (header != 0x0A) continue; // MethodSpec header
        var argCount = sig.ReadCompressedInteger();
        var argNames = new List<string>();
        for (var i = 0; i < argCount; i++)
        {
            argNames.Add(ReadTypeArgName(md, ref sig));
        }
        
        // Build the instantiation name: e.g. "System.Collections.Generic.List`1<object>"
        var baseName = ownerName;
        var instantiationName = baseName + "<" + string.Join(",", argNames) + ">";
        
        if (added.Contains(instantiationName)) continue;
        if (byName.ContainsKey(instantiationName)) continue;
        
        // Create a TypeDef for this instantiation
        var typeDef = new TypeDef
        {
            Name = ownerName, // Same name, different FullName
            FullName = instantiationName,
            GenericArity = 0, // This is a concrete instantiation, not a definition
            IsValueType = false, // Will be resolved from base type
            BaseTypeName = "System.Object",
        };
        
        types.Add(typeDef);
        byName[instantiationName] = typeDef;
        added.Add(instantiationName);
    }
    
    // Scan TypeSpec table: each row is a type instantiation (Nullable<T>, List<T>, etc.)
    foreach (var tsHandle in md.TypeSpecifications)
    {
        var ts = md.GetTypeSpecification(tsHandle);
        var sig = md.GetBlobReader(ts.Signature);
        var header = sig.ReadByte();
        
        if (header == 0x15) // GENERICINST
        {
            var classOrValueType = sig.ReadByte(); // 0x11=VALUETYPE, 0x12=CLASS
            var typeHandle = sig.ReadTypeHandle();
            var defName = md.GetFullName(typeHandle);
            var argCount = sig.ReadCompressedInteger();
            var argNames = new List<string>();
            for (var i = 0; i < argCount; i++)
            {
                argNames.Add(ReadTypeArgName(md, ref sig));
            }
            
            var instantiationName = defName + "<" + string.Join(",", argNames) + ">";
            
            if (added.Contains(instantiationName)) continue;
            if (byName.ContainsKey(instantiationName)) continue;
            
            var typeDef = new TypeDef
            {
                Name = defName,
                FullName = instantiationName,
                GenericArity = 0,
                IsValueType = classOrValueType == 0x11,
                BaseTypeName = classOrValueType == 0x11 ? "System.ValueType" : "System.Object",
            };
            
            types.Add(typeDef);
            byName[instantiationName] = typeDef;
            added.Add(instantiationName);
        }
    }
}

private static string ReadTypeArgName(MetadataReader md, ref BlobReader sig)
{
    var b = sig.ReadByte();
    return b switch
    {
        0x02 => "System.Boolean",
        0x03 => "System.Char",
        0x04 => "System.SByte",
        0x05 => "System.Byte",
        0x06 => "System.Int16",
        0x07 => "System.UInt16",
        0x08 => "System.Int32",
        0x09 => "System.UInt32",
        0x0A => "System.Int64",
        0x0B => "System.UInt64",
        0x0C => "System.Single",
        0x0D => "System.Double",
        0x0E => "System.String",
        0x1C => "System.Object",
        0x11 or 0x12 => md.GetFullName(sig.ReadTypeHandle()),
        0x13 or 0x1E => "T", // VAR/MVAR
        0x1D or 0x1F => ReadTypeArgName(md, ref sig) + "[]",
        0x15 => GenericInstArgName(md, ref sig),
        _ => "System.Object",
    };
}

private static string GenericInstArgName(MetadataReader md, ref BlobReader sig)
{
    sig.ReadByte(); // 0x11 or 0x12
    var defName = md.GetFullName(sig.ReadTypeHandle());
    var argCount = sig.ReadCompressedInteger();
    var args = new List<string>();
    for (var i = 0; i < argCount; i++)
        args.Add(ReadTypeArgName(md, ref sig));
    return defName + "<" + string.Join(",", args) + ">";
}
```

### 阶段 2：TypeEmitter.TypeCppName 支持非 int 实例化

```csharp
// TypeEmitter.cs - 修改 TypeCppName

public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    
    // r78 fix: For generic instantiations (FullName contains '<'), use the
    // full instantiation name instead of appending "_int"
    if (t.FullName.Contains('<'))
    {
        // Convert "System.Collections.Generic.List`1<T>" to List_1_T
        // The FullName already includes the generic args
        return Sanitize(t.FullName);
    }
    
    // M3 generics (Task 7): a generic definition emits ONE instantiation
    // with T -> int32_t. The instantiation name appends "_int"
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```

### 阶段 3：CodeGenerator 为实例化类型 emit 方法体

在 `GenerateCore` 的类型扫描循环中，为每个泛型实例化类型生成对应的方法体：

```csharp
// CodeGenerator.cs - 在类型 emission 循环之后添加

// r78 fix: Emit method bodies for generic instantiations
foreach (var t in types.Where(t => t.FullName.Contains('<')))
{
    var cppName = TypeEmitter.TypeCppName(t);
    var w = new CppWriter();
    w.Section("generic instantiation");
    w.Line($"// instantiation: {t.FullName}");
    w.Line("#include \"generated.h\"");
    w.Line();
    
    // Emit methods for this instantiation
    foreach (var m in t.Methods)
    {
        if (m.IsPInvoke || m.IsAbstract) continue;
        if (IsDelegateTypeDef(t)) continue;
        
        try
        {
            var bodyInfo = MethodBodyReader.Read(pe, md, t, m);
            // ... emit method body ...
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: generic instantiation method emit failed for {t.FullName}::{m.Name} -> skipped");
        }
    }
    
    // Write the .cpp file
    File.WriteAllText(Path.Combine(outDir, "src", cppName + ".cpp"), w.ToString());
    allSrc.Add(cppName);
}
```

---

## 修复优先级

### P0：修复 V_ 描述符缺失（90 错误）

在 `CodeGenerator.GenerateCore` 的类型扫描中，为所有值类型 emit V_ 描述符：

```csharp
// 在类型 emission 循环中添加
foreach (var t in types.Where(t => t.IsValueType))
{
    var cppName = TypeEmitter.TypeCppName(t);
    if (!generatedHeader.ToString().Contains($"{cppName}_class"))
    {
        generatedHeader.Line($"extern Il2CppClass {cppName}_class;");
    }
}
```

### P1：修复 ListBuilder<object> 实例化（100 错误）

在 `ExpandGenericInstantiations` 中，确保 `ListBuilder` 的 `object` 实例化被创建。

### P2：修复 TaskFactory<T> 实例化（167 错误）

需要为 `TaskFactory<string>`, `TaskFactory<object>`, `TaskFactory<VoidTaskResult>` 等创建 TypeDef。

### P3：修复 Nullable<复杂类型> 实例化（36 错误）

需要递归展开嵌套泛型：`Nullable<ReadOnlyMemory<byte>>` 需要先创建 `ReadOnlyMemory<byte>` 的 TypeDef。

---

## 影响评估

| 修复项 | 错误减少量 | 复杂度 | 风险 |
|--------|-----------|--------|------|
| V_ 描述符 | ~90 | 低 | 低 |
| ListBuilder<object> | ~100 | 中 | 中 |
| TaskFactory<T> | ~167 | 高 | 中 |
| Nullable<复杂类型> | ~36 | 高 | 高 |
| TraceLoggingTypeInfo<T> | ~20 | 中 | 中 |

**总计潜在修复**: ~413 个泛型相关 undeclared 错误（占 47%）

---

## 实施步骤

1. **添加 `ExpandGenericInstantiations` 方法**到 MetadataLoader.cs
2. **修改 `TypeCppName`** 支持非 int 实例化
3. **在 CodeGenerator 中添加泛型实例化方法体 emission**
4. **添加 V_ 描述符完整 emit 逻辑**
5. **重新运行 r77 生成**，验证 undeclared 错误减少

---

## 注意事项

- 泛型实例化可能递归嵌套（`Nullable<ReadOnlyMemory<byte>>`），需要递归展开
- 某些实例化可能引用 BCL 类型（如 `System.Threading.Tasks.VoidTaskResult`），需要确保这些类型也存在
- MethodSpec 扫描需要处理 generic method 的实例化（如 `Array.Sort<T>`）
- 需要避免重复添加已存在的 TypeDef
