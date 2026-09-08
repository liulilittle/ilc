# Undeclared T1-T3 根因调查报告

**日期**: 2026-09-06  
**目标文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`  
**数据源**: `E:\il2cpp\r77shard\*.csv` (67个V_错误)  

---

## 一、现象统计

| 错误类型 | 数量 | 示例 |
|---------|------|------|
| `unknown type name 'V_...'` | ~50 | `V_System_Guid`, `V_System_Collections_Generic_KeyValuePair_2_int`, `V_System_DateTime` |
| `undeclared identifier 'V_..._class'` | ~17 | `V_System_DateTime_class`, `V_System_Guid_class`, `V_System_Collections_DictionaryEntry_class` |
| **合计** | **67** | 涉及50+个独立源文件 |

**典型错误位置**:
- `System_Collections_Generic_List_1_object.cpp:310` → `&V_System_Collections_Generic_KeyValuePair_2_System_Object_System_Object__class`
- `System_Collections_Generic_Dictionary_2_KeyCollection_Enumerator_object_object.cpp:164` → `reinterpret_cast<V_System_Collections_Generic_KeyValuePair_2_int*>`
- `System_Collections_Generic_List_1_DateTime.cpp:310` → `&V_System_DateTime_class`

---

## 二、根因分析

### 2.1 编码链路

`MethodSignatureCoder.EncodeType` (MethodSignatureCoder.cs:200-241) 对 VALUETYPE 使用 `"V:" + FullName` 前缀编码:
```csharp
0x11 => "V:" + ResolveTypeName(md, sig.ReadTypeHandle()), // ELEMENT_TYPE_VALUETYPE
0x12 => "r:" + ResolveTypeName(md, sig.ReadTypeHandle()), // ELEMENT_TYPE_CLASS
```

这个编码设计用于方法签名哈希(区分重载), 但在后续**类型解析**时, "V:" 前缀未被正确剥离。

### 2.2 直接根因

`CodeGenerator.TypeCppName(string)` (CodeGenerator.cs:5626-5649) 未处理 "V:" 前缀:

```csharp
private static string TypeCppName(string fullName) => fullName switch
{
    "System.Object" => "Il2CppObject",
    "System.String" => "Il2CppObject",
    "System.DateTime" => "il2cpp::runtime::Il2CppDateTime",
    // ... 其他BCL类型 ...
    _ => TypeEmitter.Sanitize(fullName),  // ← "V:System.Guid" 落入此分支
};
```

当输入为 `"V:System.Guid"`:
1. 不匹配任何硬编码BCL类型名
2. 落入 `_ => TypeEmitter.Sanitize(fullName)`
3. Sanitize 输出 `"V_System_Guid"` (非法C++标识符, 不存在)

### 2.3 传播路径

```
IL newarr <System.Guid> token
    ↓
TypeTokenFullName(md, token) → "System.Guid"  (正确)
    ↓
ArrayElementType → isRefElement=true
    ↓
ArrayElementClassCpp("System.Guid") → BoxClassDescriptorCpp("System.Guid")
    ↓
BoxClassDescriptorCpp 默认分支: $"&{TypeEmitter.Sanitize(typeName)}_class"
    ↓
输出: &System_Guid_class  (正确, 有定义)
```

但对于**KeyValuePair<,>**、**用户自定义结构体**等复杂类型, 存在另一条路径:
- `MethodSignatureCoder` 编码参数为 `"V:System.Collections.Generic.KeyValuePair`2<System.Int32,System.Int32>"`
- 该编码在某些 `CodeGenerator` 路径中被直接传入 `TypeCppName` 或 `Sanitize`
- 输出 `"V_System_Collections_Generic_KeyValuePair_2_int_int"` (无对应C++ struct定义)

### 2.4 证据链

1. **编码器输出**: MethodSignatureCoder.cs:223 生成 `"V:" + FullName`
2. **类型解析未剥离**: CodeGenerator.cs:5626 TypeCppName 无 "V:" 处理
3. **Sanitize保留前缀**: TypeEmitter.cs:452 Sanitize 把 `V:` → `V_`
4. **生成不存在符号**: `V_System_Guid` 等从未在 generated.h 中定义
5. **编译失败**: g++ 报 unknown type name / undeclared identifier

---

## 三、修复方案

### 3.1 最小改动方案 (推荐)

**修改 `CodeGenerator.TypeCppName`** (CodeGenerator.cs:5626):

```csharp
private static string TypeCppName(string fullName) => fullName switch
{
    "System.Object" => "Il2CppObject",
        "System.String" => "Il2CppObject",
        // ... 现有BCL映射 ...
        _ => TypeEmitter.Sanitize(StripSignaturePrefix(fullName)),
};

/// <summary>剥离 MethodSignatureCoder 编码前缀 ("V:" / "r:" / "B:" / "P:" / "T:")</summary>
private static string StripSignaturePrefix(string encoded) =>
    encoded.Length > 2 && encoded[1] == ':' && encoded[0] is 'V' or 'r' or 'B' or 'P' or 'T' or 'F'
        ? encoded[2..]
        : encoded;
```

### 3.2 防御性补充

在以下位置添加前缀剥离调用:
- `ArrayElementClassCpp` 输入
- `BoxClassDescriptorCpp` 输入
- `ParamTypesCpp` 的 "V:" 分支 (已存在, 确认覆盖)

### 3.3 Sanitize 净化增强 (备选)

**修改 `TypeEmitter.Sanitize`** (TypeEmitter.cs:452):

```csharp
public static string Sanitize(string s)
{
    var stripped = StripSignaturePrefix(s);
    return string.Join("_", stripped.Split('.', '+', '`', '<', '>', ',', '[', ']', '='));
}
```

---

## 四、预期效果

| 指标 | 当前 | 修复后 |
|------|------|--------|
| V_ undeclared 错误 | 67 | 0 |
| 受影响的源文件 | ~50 | 0 |
| 生成的C++合法性 | 部分非法 | 全部合法 |

**消除机制**:
- `V_System_Guid` → `System_Guid` (已定义于 guid.h)
- `V_System_DateTime` → 走BCL映射 → `il2cpp::runtime::Il2CppDateTime`
- `V_System_Collections_Generic_KeyValuePair_2_int_int` → 用户struct名 (若已生成)

---

## 五、关联修复参考

- **r30-cannotinit fix** (CodeGenerator.cs:4069): 已处理泛型实例化名称坍缩
- **L2-Z1/Z2** (CodeGenerator.cs:2155-2165): 已处理 primitive kind 解析
- **wf2 (winforms1)** (MethodBodyReader.cs:260-275): 已处理 async locals 的 "v:" 编码

本修复与上述修复互补, 针对的是**签名编码前缀残留**这一独立根因。

---

## 六、验证清单

1. `dotnet build src/il2cppc` → 0错0警告 (编译期验证)
2. 重新生成 r77 → V_ 错误全部消除
3. 抽查文件确认 `V_System_Guid_class` → `System_Guid_class` (或对应的runtime类型)
4. 回归测试: 确认不影响现有 V_ 正确用法 (如有)

---

**调查人**: 子代理 (ses_f8d427dafffegHpvvixhOajmiS)  
**状态**: 待实施修复
