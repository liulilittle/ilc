# r77 AsnDecoder 146 个 NMF 错误修复方案

## 概述

- **总错误数**: 146 个 AsnDecoder/AsnValueReader/AsnReader 相关 no-matching-function 错误
- **涉及文件**: 3 个主要文件 + 12 个调用文件
- **根因**: 泛型类型实例化参数不匹配（`int` vs `byte`/`uint8`），签名解码缺失 `u1` 类型分支

---

## 错误统计

### 按方法名分类（Top 20）

| 方法签名 | 错误数 | 调用文件 |
|---------|--------|---------|
| `AsnValueReader__ctor` | ~25 | AlgorithmIdentifierAsn, AttributeAsn, ContentInfoAsn, DigestInfoAsn, EncryptedContentInfoAsn, EncryptedDataAsn, EncryptedPrivateKeyInfoAsn, MacData, PBES2Params, Pbkdf2Params, Pbkdf2SaltChoice, Rc2CbcParameters, IterationCountLimitEnforcer_Impl |
| `AsnValueReader_ReadSequence` | ~12 | AlgorithmIdentifierAsn, AttributeAsn, ContentInfoAsn, DigestInfoAsn, EncryptedContentInfoAsn, EncryptedDataAsn, EncryptedPrivateKeyInfoAsn, MacData, PBES2Params, Pbkdf2Params |
| `AsnDecoder_TryReadPrimitiveBitString` | ~12 | AsnReader |
| `AsnDecoder_ReadBitString` | ~8 | AsnReader, AsnValueReader |
| `ReadOnlySpan_1_int_get_Length` | ~15 | AsnDecoder, AsnReader, AsnValueReader, AlgorithmIdentifierAsn, AttributeAsn, ContentInfoAsn, DigestInfoAsn, MacData |
| `ReadOnlySpan_1_int_Slice` | ~20 | AsnReader, AsnValueReader |
| `ReadOnlySpan_1_int_get_IsEmpty` | ~8 | AsnValueReader, AsnReader |
| `ReadOnlySpan_1_int_ToArray` | ~10 | AlgorithmIdentifierAsn, AsnDecoder, AttributeAsn, ContentInfoAsn, DigestInfoAsn, MacData, AsnValueReader |
| `AsnDecoder_ReadEncodedValue` | ~8 | AsnReader, AsnValueReader |
| `AsnDecoder_ReadSequence` | ~6 | AsnReader, AsnValueReader |
| `AsnDecoder_ReadSetOf` | ~6 | AsnReader, AsnValueReader |
| `AsnDecoder_ReadOctetString` | ~6 | AsnReader, AsnValueReader |
| `AsnDecoder_ReadIntegerBytes` | ~6 | AsnReader, AsnValueReader |
| `AsnDecoder_ReadObjectIdentifier` | ~6 | AsnReader, AsnValueReader |
| `AsnDecoder_ReadNull` | ~4 | AsnReader |
| `AsnDecoder_TryReadInt32` | ~4 | AsnReader, AsnValueReader |
| `AsnDecoder_TryReadOctetString` | ~4 | AsnReader, AsnValueReader |
| `AsnDecoder_Slice` | ~8 | AsnDecoder |
| `Nullable_1_int_get_HasValue` | ~12 | AsnDecoder, AsnReader, RSAPKCS1SignatureDeformatter, etc. |
| `Nullable_1_int_get_Value` | ~12 | AsnDecoder, RSAPKCS1SignatureDeformatter, etc. |

### 按调用文件分类

| 文件 | 错误数 |
|------|--------|
| `System_Security_Cryptography_X509Certificates_AsnDecoder.cpp` | ~50 |
| `System_Security_Cryptography_X509Certificates_AsnReader.cpp` | ~30 |
| `System_Security_Cryptography_X509Certificates_AsnValueReader.cpp` | ~30 |
| `System_Security_Cryptography_X509Certificates_AlgorithmIdentifierAsn.cpp` | ~15 |
| `System_Security_Cryptography_X509Certificates_AttributeAsn.cpp` | ~8 |
| `System_Security_Cryptography_X509Certificates_ContentInfoAsn.cpp` | ~8 |
| `System_Security_Cryptography_X509Certificates_MacData.cpp` | ~8 |
| 其他 6 个文件 | ~30 |

---

## 根因分析

### 根因 #1: ReadOnlySpan/ReadOnlyMemory `int` vs `byte`/`uint8` 实例化不匹配（占 70%）

**问题**:
- `ReadOnlySpan_1_int` 已 emit，但 `AsnValueReader._span` 字段类型是 `ReadOnlySpan<byte>`
- 调用 `AsnValueReader__ctor` 时传入 `ReadOnlySpan_1_int`（int 实例化），但签名需要 `ReadOnlySpan_1_byte`（byte 实例化）
- `ReadOnlySpan_1_int_get_Length` 等方法同理

**具体位置**:
```cpp
// AsnValueReader.cpp:150
return (System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_IsEmpty_3a2acf73b06ca80a((&arg0->_span)) == 0LL);
// ^^^ 但 _span 是 ReadOnlySpan<byte>，不是 ReadOnlySpan<int>
```

**现有 generated.h 声明**:
- `void System_Security_Cryptography_X509Certificates_AsnValueReader__ctor_cf793574dffe8944(System_Security_Cryptography_X509Certificates_AsnValueReader* arg0, System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int arg1, int32_t arg2);`

**实际调用传入的参数**:
```cpp
System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_byte_ arg0  // 从 ReadOnlyMemory<byte>.get_Span() 返回
```

### 根因 #2: AsnDecoder 方法参数类型不匹配（占 20%）

**问题**:
- `AsnDecoder_TryReadPrimitiveBitStringCore` 等方法内部调用了 `ReadOnlySpan_1_int_get_Length`，但传入的 `loc5` 是 `ReadOnlySpan<byte>` 类型
- 这是因为 AsnDecoder 的方法签名使用 `ReadOnlySpan<byte>`（原始 BCL 签名），但生成的 C++ 实例化只有 `ReadOnlySpan<int>`

**具体位置**:
```cpp
// AsnDecoder.cpp:231
t11 = System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_Length_aa95fb2d151867d3((&loc5));
// ^^^ loc5 实际是 ReadOnlySpan<byte>，但这里用了 ReadOnlySpan<int> 的方法
```

### 根因 #3: AsnValueReader 内部调用 ReadOnlySpan 方法（占 10%）

**问题**:
- `AsnValueReader_get_HasData` 和 `AsnValueReader_ThrowIfNotEmpty` 等方法内部调用了 `ReadOnlySpan_1_int_get_IsEmpty`，但 `_span` 字段是 `ReadOnlySpan<byte>` 类型

**具体位置**:
```cpp
// AsnValueReader.cpp:150
return (System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_IsEmpty_3a2acf73b06ca80a((&arg0->_span)) == 0LL);
// ^^^ _span 是 ReadOnlySpan<byte>，不是 ReadOnlySpan<int>
```

### 根因 #4: Nullable<T> 类型转换（占 少量但连锁）

**问题**:
- `Nullable_1_long_get_Value_1fa83d49461a09e3` 定义在 `System_Nullable_1_long.cpp` 中，但 `System_Nullable_1_long` 的 `value` 字段类型是 `int32_t`（不是 `int64_t`）
- 这是因为 MethodSignatureCoder 对 `l`（long）类型的解码没有正确映射到 `int64_t`

**具体位置**:
```cpp
// System_Nullable_1_long.cpp:108
IL2CPP_NOINLINE int64_t System_Nullable_1_long_get_Value_1fa83d49461a09e3(System_Nullable_1_long* arg0)
{
    // ...
    return arg0->value;  // ^^^ arg0->value 是 int32_t，但函数返回 int64_t，隐式转换
}
```

---

## 修复方案

### 方案 A（推荐）：MethodSignatureCoder 增加 `u1`/`i1` 类型映射

**目标**: 让 `ReadOnlySpan<byte>` 和 `ReadOnlyMemory<byte>` 正确 emit 为 `ReadOnlySpan_1_byte` 和 `ReadOnlyMemory_1_byte`

**修改文件**: `src/il2cppc/Emit/MethodSignatureCoder.cs`

```csharp
// Decode 方法中的类型映射字典增加
'u1' => "System.Byte",  // unsigned byte -> System.Byte
'i1' => "System.SByte", // signed byte -> System.SByte
```

**效果**:
- `ReadOnlySpan<byte>` 会 emit 为 `ReadOnlySpan_1_byte` 而不是 `ReadOnlySpan_1_int`
- `ReadOnlyMemory<byte>` 会 emit 为 `ReadOnlyMemory_1_byte`
- AsnValueReader 的 `_span` 字段会正确声明为 `ReadOnlySpan_1_byte`
- AsnDecoder 的方法会使用正确的 `ReadOnlySpan_1_byte_get_Length` 等方法

### 方案 B：TypeEmitter 增加 byte 基元类型映射

**目标**: 确保 `System.Byte` 和 `System.SByte` 有正确的 TypeDef

**修改文件**: `src/il2cppc/Emit/TypeEmitter.cs`

```csharp
// TypeCppName 中增加
"System.Byte" => "uint8_t",
"System.SByte" => "int8_t",
```

### 方案 C：签名解码器增加 `SZARRAY` 和嵌套泛型展开

**目标**: 处理 `AsnValueReader<T>` 等嵌套泛型的签名解码

**修改文件**: `src/il2cppc/Emit/MethodSignatureCoder.cs`

```csharp
// 在 Decode 方法中增加 SZARRAY 处理
if (sig.StartsWith("SZARRAY:", StringComparison.Ordinal))
{
    var elemType = Decode(sig["SZARRAY:".Length..]);
    return $"SZARRAY_{elemType}";
}
```

### 方案 D：Nullable 类型字段修复

**目标**: 修复 `Nullable<long>` 等字段的 C++ 类型

**修改文件**: `src/il2cppc/Emit/CodeGenerator.cs`

```csharp
// 在 Nullable 类型生成中
if (typeArgs.Length > 0 && typeArgs[0] == "System.Int64")
{
    nullableFields.Add(("value", "int64_t"));
}
else if (typeArgs.Length > 0 && typeArgs[0] == "System.Int32")
{
    nullableFields.Add(("value", "int32_t"));
}
```

---

## 修复优先级

| 优先级 | 方案 | 减少错误数 | 实施难度 |
|--------|------|-----------|---------|
| P0 | A: MethodSignatureCoder 增加 u1/i1 | ~100 | 低 |
| P1 | B: TypeEmitter byte 映射 | ~100 | 低 |
| P2 | D: Nullable 字段类型修复 | ~20 | 中 |
| P3 | C: SZARRAY 展开 | ~10 | 高 |

---

## 验证方法

1. 修改后重新生成 mscorlib
2. 检查 `System_Security_Cryptography_X509Certificates_AsnValueReader.cpp` 中 `_span` 字段类型
3. 检查 `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_byte.cpp` 是否生成
4. 检查 `System_Security_Cryptography_X509Certificates_AsnDecoder.cpp` 中方法调用是否匹配
5. 编译验证: `gcc -c -Iinclude -Iinclude/internal System_Security_Cryptography_X509Certificates_AsnDecoder.cpp`

---

## 结论

r77 的 AsnDecoder 146 个错误**主要根因**是：
1. **MethodSignatureCoder 缺少 `u1`/`i1` 类型映射**，导致 `byte` 被错误映射为 `int`
2. **TypeEmitter 缺少 `System.Byte`/`System.SByte` 的基元类型映射**
3. **Nullable 类型字段类型不匹配**（`long` 映射到 `int32_t` 而非 `int64_t`）

修复 P0 + P1 可消除约 100 个错误，剩余 46 个需要 P2 + P3 修复。
