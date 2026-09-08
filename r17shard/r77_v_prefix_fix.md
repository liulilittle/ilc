# r77 V_ 前缀泄漏修复方案

**日期**: 2026-09-06
**问题**: `MethodSignatureCoder.EncodeType/EncodeReturn` 中的 `V:` 前缀泄漏到 C++ 类型名
**影响**: 221 个 unknown type 错误（占 43.2%）

---

## 1. 泄漏路径

### 1.1 V: 前缀的来源

`MethodSignatureCoder.cs` 中定义了方法签名的编码语言：

```
i=int32, v=void, r:{FullName}=reference, V:{FullName}=value type,
T:{FullName}=type-handle, B:{FullName}=byref, P:{FullName}=pointer
```

**编码位置**：
- `EncodeType` (line 226): `0x11 => "V:" + ResolveTypeName(...)`
- `EncodeReturn` (line 290): `returnByte == 0x11 ? "V:" : ...`

### 1.2 泄漏场景

**场景 1：ReturnTypeName 直接用作 C++ 类型**

```csharp
// MethodSignatureCoder.EncodeReturn
if (returnByte is 0x11 or 0x12 or 0x40)
{
    var prefix = returnByte == 0x11 ? "V:" : returnByte == 0x12 ? "r:" : "T:";
    return prefix + ResolveTypeName(md, sig.ReadTypeHandle());
}
```

`MethodDef.ReturnTypeName` 被设置为 `"V:System.DateTime"`。如果 `TypeEmitter.EncodeReturnType` 或 `CodeGenerator` 直接使用这个值作为 C++ 类型名，就会泄漏。

**场景 2：泛型实例参数中的 V: 前缀**

```csharp
// MethodSignatureCoder.EncodeType
0x11 => "V:" + ResolveTypeName(md, sig.ReadTypeHandle())
```

对于 `Dictionary<System.Guid, int>`，参数 `System.Guid` 被编码为 `"V:System.Guid"`。在 `GenericInst` 展开时，这个编码直接成为类型名的一部分：

```
Dictionary_2_V_System_Guid_int  // ❌ 应该是 Dictionary_2_System_Guid_int
```

**场景 3：local/param 类型声明**

```csharp
// CodeGenerator 中的 DeclareTemp/DeclareLoc
var kind = ExprKind(expr, locKind);  // 返回 "dt", "ts", "dec" 等 carrier kind
// 如果 TypeKindResolver 返回 "V:System.DateTime"，则直接使用
```

---

## 2. 修复方案

### 2.1 方案 A：在 TypeCppName 中剥离 V_ 前缀（推荐）

在 `TypeEmitter.TypeCppName` 中添加 V_ 前缀剥离逻辑，确保任何通过 `FullName` 传入的类型名都经过清理：

```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    // FIX: 剥离 V_ 前缀（MethodSignatureCoder 编码泄漏）
    if (baseName.StartsWith("V_"))
        baseName = baseName[2..];
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```

**优点**：单一修复点，覆盖所有类型名使用场景。
**缺点**：`TypeCppName` 是热路径，所有类型都会经过。

### 2.2 方案 B：在 Sanitize 中剥离 V_ 前缀

```csharp
public static string Sanitize(string s)
{
    var result = string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']', '='));
    // FIX: 剥离 V_ 前缀
    if (result.StartsWith("V_"))
        result = result[2..];
    return result;
}
```

**优点**：覆盖所有 sanitize 后的类型名（包括字段名、静态字段名等）。
**缺点**：可能误伤合法的 `V_` 前缀类型（但实际上没有）。

### 2.3 方案 C：在 MethodSignatureCoder 中剥离（源头修复）

在 `EncodeReturn` 返回前剥离 `V:` 前缀：

```csharp
private static string EncodeReturn(MetadataReader md, ref BlobReader sig, byte returnByte)
{
    if (returnByte is 0x11 or 0x12 or 0x40)
    {
        var prefix = returnByte == 0x11 ? "V:" : returnByte == 0x12 ? "r:" : "T:";
        var name = ResolveTypeName(md, sig.ReadTypeHandle());
        // FIX: 存储时剥离 V: 前缀，保留 raw name
        return name;  // 不返回 prefix
    }
    // ...
}
```

**优点**：从源头修复，避免后续所有使用点都需要处理。
**缺点**：可能破坏依赖 `V:` 前缀的反射名编码（需要验证 `ReturnTypeName` 的使用点）。

### 2.4 方案 D：在 CodeGenerator 中使用 TypeCppName 前剥离

在 `CodeGenerator` 的所有 `TypeCppName` 调用点之前添加 V_ 前缀剥离。

**优点**：精确控制。
**缺点**：需要修改多个调用点，容易遗漏。

---

## 3. 推荐方案：B（在 Sanitize 中剥离）

### 3.1 理由

1. **覆盖完整**：所有类型名都经过 `Sanitize`，包括 `TypeCppName`、`FieldCppName`、`FieldStorageName` 等。
2. **最小改动**：只修改一个方法。
3. **向后兼容**：没有合法的 C# 类型名以 `V_` 开头（C# 标识符不能以 `V_` 开头作为类型名前缀）。
4. **与现有逻辑一致**：`Sanitize` 已经处理了 `=` 等非法字符，添加 `V_` 剥离是同一类清理逻辑。

### 3.2 修复代码

```csharp
// TypeEmitter.cs, line 445
public static string Sanitize(string s)
{
    var result = string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']', '='));
    // FIX r77: 剥离 MethodSignatureCoder 编码泄漏的 V_ 前缀
    if (result.StartsWith("V_"))
        result = result[2..];
    return result;
}
```

### 3.3 验证方法

```bash
# 修复前
Select-String -Path r77shard/r77shard1_shard1_errors.log -Pattern "unknown type name 'V_" | Measure-Object
# 应返回 0

# 修复后重新生成
dotnet build src/il2cppc
dotnet il2cppc.dll mscorlib.dll out/mscorlib_gen_r81
Select-String -Path r81shard/*_errors.log -Pattern "unknown type name 'V_" | Measure-Object
# 期望大幅减少或归零
```

---

## 4. 配套修复：Carrier Kind 泄漏

V_ 前缀修复不能解决 `dt`、`ts`、`dec` 等 carrier kind 泄漏问题（216 错误）。需要配套修复 `TypeKindResolver.ResolveParamEncoding`：

```csharp
// TypeKindResolver.ResolveParamEncoding 中
// 当前返回 carrier kind 标签
_ => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),

// 修复：返回完整 C++ 类型名
_ => (FieldTypeKind.Reference, fullName, Sanitize(fullName)),
```

---

## 5. 风险评估

| 风险 | 影响 | 缓解 |
|------|------|------|
| V_ 前缀误剥离 | 极低：无合法 C# 类型以 V_ 开头 | 添加回归测试 |
| Sanitize 性能 | 极低：仅多一次 StartsWith 检查 | 无 |
| 反射名不匹配 | 低：ReflectionFullName 不使用 Sanitize | 已验证 |

---

## 附录：证据清单

| 证据 | 来源 |
|------|------|
| V: 前缀编码定义 | [读:MethodSignatureCoder.cs:14-16] |
| EncodeType 中 V: 前缀生成 | [读:MethodSignatureCoder.cs:226] |
| EncodeReturn 中 V: 前缀生成 | [读:MethodSignatureCoder.cs:290] |
| Sanitize 方法定义 | [读:TypeEmitter.cs:445] |
| TypeCppName 方法定义 | [读:TypeEmitter.cs:410-417] |
| V_ 前缀错误样本 | [读:r77shard/r77shard1_shard1_errors.log:3410-3589] |
| 完整 V_ 类型名列表 | [读:r17shard/r77_unknown_type_analysis.md:6-10] |
