# r77 PasswordBasedEncryption 127 个 nmf 错误修复方案

**Date**: 2026-09-06  
**Source**: r77shard 三片错误日志 (`r77shard1_errors.log`, `r77shard2_errors.log`, `r77shard3_errors.log`)  
**Total PasswordBasedEncryption errors**: 173 (含 PfxAsn 跨文件调用)

---

## 1. Shard Distribution

| Shard | Errors |
|-------|--------|
| r77shard1 | 124 |
| r77shard2 | 24 |
| r77shard3 | 25 |
| **Total** | **173** |

---

## 2. Top 15 错误模式 (按根因分组)

| # | 错误模式 | Count | 分组 |
|---|---------|-------|------|
| 1 | `no matching function for call to 'Span_1_int_Clear_e0400568017a2a6f'` | 20 | **A. ReadOnlySpan→Span 转换缺失** |
| 2 | `no matching function for call to 'Span_1_int__ctor_7cc9132536000361'` | 10 | **A** |
| 3 | `cannot initialize parameter 'Nullable_1<ReadOnlyMemory_1_int> *' from 'Nullable_1_int *'` | 14 | **B. Nullable<T> 类型坍缩** |
| 4 | `no matching function for call to 'CryptographicOperations_ZeroMemory_83b23cd241d324bd'` | 8 | **A** |
| 5 | `no matching function for call to 'Span_1_int_op_Implicit_47310c4b54bdf3fe'` | 8 | **A** |
| 6 | `no matching function for call to 'Span_1_int_Slice_8d80c0d429d2b07a'` | 8 | **A** |
| 7 | `use of undeclared identifier 'Nullable_1<ReadOnlyMemory_1_byte>_get_HasValue'` | 7 | **B** |
| 8 | `no matching function for call to 'ArraySegment_1_int_get_Array/Offset/Count'` | 19 | **C. Span op_Implicit 返回值类型不匹配** |
| 9 | `no matching function for call to 'Span_1_int_get_Length_58c601a09e1ad1e5'` | 6 | **A** |
| 10 | `no matching function for call to 'ReadOnlySpan_1_int_CopyTo_d2ca1a9ee86e49fb'` | 6 | **A** |
| 11 | `no matching function for call to 'ReadOnlyMemory_1_int_get_Span/get_Length'` | 8 | **D. ReadOnlyMemory 方法签名不匹配** |
| 12 | `unknown type name 'ReadOnlySpan_1_c_' / 'ReadOnlySpan_1_u1_'` | 6 | **E. 缺失泛型实例声明** |
| 13 | `assigning to 'int32_t' from 'AlgorithmIdentifierAsn'` | 2 | **F. 结构体类型未定义/不可转换** |
| 14 | `assigning to 'Il2CppObject*' from 'ReadOnlySpan_1_int'` | 2 | **A. 值类型→指针隐式转换** |
| 15 | `no matching function for call to 'PasswordBasedEncryption_*'` | 若干 | **G. 参数签名不匹配** |

---

## 3. 根因分析

### 根因 A: `ReadOnlySpan<T>` → `Span<T>` 隐式转换缺失 (24 errors)

**症状**:
```
error: no matching function for call to 'Span_1_int_Clear_e0400568017a2a6f'  // 参数类型: ReadOnlySpan_1_int
error: no matching function for call to 'Span_1_int__ctor_7cc9132536000361'  // 参数: ReadOnlySpan_1_int
error: no matching function for call to 'Span_1_int_op_Implicit_47310c4b54bdf3fe'
error: no matching function for call to 'Span_1_int_Slice_8d80c0d429d2b07a'
```

**定义侧签名** (`generated.h`):
```cpp
void Span_1_int_Clear_e0400568017a2a6f(System_Security_Cryptography_X509Certificates_Span_1_int* arg0);
void Span_1_int__ctor_7cc9132536000361(Span_1_int* arg0, Il2CppObject* arg1, int32_t arg2, int32_t arg3);
Il2CppObject* Span_1_int_op_Implicit_47310c4b54bdf3fe(System_Security_Cryptography_X509Certificates_Span_1_int arg0);
Il2CppObject* Span_1_int_Slice_8d80c0d429d2b07a(System_Security_Cryptography_X509Certificates_Span_1_int* arg0, int32_t arg1, int32_t arg2);
```

**调用侧实际类型** (`PasswordBasedEncryption.cpp`):
```cpp
System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int inl15;
Span_1_int_Clear_e0400568017a2a6f((&inl15));  //  ReadOnlySpan_1_int* vs Span_1_int* — 不匹配
```

**错误位置**:
- `PasswordBasedEncryption.cpp:313,318,319,325,331,332,465,466,475,476,1179,1181,1186,1189,1314,1336,1339,1344,1349,1355,1359,1365,1371`

**根因**: .NET 中 `ReadOnlySpan<T>` 可以隐式转换为 `Span<T>` (仅对 `in`/只读参数安全),但 C++ 生成器没有实现该隐式转换。`CodeGenerator` 中 `MethodRefResolver` 的方法重载解析选择了 `.ctor(Span_1_int)` 但调用点传入的是 `ReadOnlySpan_1_int` 的地址。

**修复方案**: 在 `CodeGenerator.cs` 的 `EmitCall` / `EmitIcallCallArgs` 中增加一个 `in` 参数方向的隐式转换层:
- 对于 `ReadOnlySpan<T>` → `Span<T>` 的隐式转换,在调用点插入 `reinterpret_cast<Span_1_int&>(roSpan)` (二进制布局相同,均为 `{Il2CppObject* _Segment}`)
- 或者,更安全的做法:在定义侧为 `Span_1_int` 的常用方法生成接受 `ReadOnlySpan_1_int` 的重载

### 根因 B: `Nullable<T>` 类型坍缩 (21 errors)

**症状**:
```
error: use of undeclared identifier 'Nullable_1<ReadOnlyMemory_1_byte>_get_HasValue_...'; did you mean 'Nullable_1<ReadOnlyMemory_1_int>_get_HasValue_...'?
error: cannot initialize parameter of type 'Nullable_1<ReadOnlyMemory_1_int> *' with 'Nullable_1_int *'
```

**定义侧**:
```cpp
// 只有 Nullable_1<ReadOnlyMemory_1_int> 被生成 (ReadOnlyMemory_1_int = Span_1_int 的别名)
extern void Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int_get_HasValue(...);
```

**调用侧**:
```cpp
System_Nullable_1_int arg0;  // 泛型闭合为 Nullable<int> → 应为 Nullable<ReadOnlyMemory<byte>>
Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte_get_HasValue((&arg0));
//                                  ^^^^ 应为 int,不是 byte
```

**错误位置**:
- `PasswordBasedEncryption.cpp:523,532` (跨 7 种方法调用变体)

**根因**: `MetadataLoader.ClosureArgShortName` 对 `ReadOnlyMemory<T>` 类型名中的元素类型 `T` 闭合时,将 `System.Byte` 错误地坍缩为 `int`(因为 `byte` 在 IL2CPP generic sharing 中映射到 `int` 槽位),导致 `Nullable<ReadOnlyMemory<byte>>` 的 C++ 名被生成 `Nullable_1_ReadOnlyMemory_1_byte`,但实际闭包克隆只生成了 `Nullable_1_ReadOnlyMemory_1_int`。

**修复方案**: 在 `MetadataLoader.CloneClosedType` 中,对泛型实参类型为 `ReadOnlyMemory<T>` / `ReadOnlySpan<T>` / `Span<T>` 时,**元素类型 T 必须保留原始元数据名**(不做 ClosureArgShortName 坍缩)。具体:
- 在 `ClosureArgShortName` 中增加一条前置规则:当 arg 包含 `` `1< `` 且元素类型为一个未坍缩的元数据名时,递归处理内部但不应用 32-bit 族归一
- 或更简单:在闭包收集阶段,`ExpandOpenInstances` 遇到 `ReadOnlyMemory<byte>` 时,直接注册 `(ReadOnlyMemory`1, ["System.Byte"])` 而不是 `["int"]`

### 根因 C: `Span<T>.op_Implicit` 返回值类型不匹配 (19 errors)

**症状**:
```
error: no matching function for call to 'ArraySegment_1_int_get_Array_aaceee58dbd184e0'
```

**定义侧**:
```cpp
Il2CppObject* ArraySegment_1_int_get_Array_aaceee58dbd14e0(System_ArraySegment_1_int* arg0);
```

**调用侧** (`PasswordBasedEncryption.cpp:298,301,447,450,1434,1435,1436,1442,1443,1444,1467,1468,1469`):
```cpp
inl10 = reinterpret_cast<Il2CppObject*>(ArraySegment_1_int_get_Array_aaceee58dbd184e0((&inl9)));
// inl9 是 ReadOnlySpan_1_int 类型,但 DangerousGetArraySegment 返回 Il2CppObject*(即 ArraySegment_1_int),
// 而 get_Array 期望的是 ArraySegment_1_int*
```

**根因**: `Span_1_int_DangerousGetArraySegment` 返回 `Il2CppObject*`,而 `ArraySegment_1_int_get_Array` 期望 `ArraySegment_1_int*`。调用侧需要 `reinterpret_cast`。

**修复方案**: 在 `CodeGenerator` 的 `EmitCall` 中,当返回类型为 `Il2CppObject*` 但目标参数类型为泛型值类型指针时,自动插入 `reinterpret_cast`。

### 根因 D: `ReadOnlyMemory<T>.get_Span` / `get_Length` 签名不匹配 (8 errors)

**症状**:
```
error: no matching function for call to 'ReadOnlyMemory_1_int_get_Span_11732cf97e0d8826'
error: no matching function for call to 'ReadOnlyMemory_1_int_get_Length_b4506d2008553d4a'
```

**根因**: `ReadOnlyMemory<T>` 的 getter 在元数据中定义为 `instance !0 get_Param()` (返回泛型参数),但 CodeGenerator 将其解码为 `ReadOnlySpan_1_int get_Span()`,而调用侧传入的 `ReadOnlyMemory_1_int` 实参类型与签名不匹配。

**修复方案**: 与根因 A 相同,需要在泛型值类型的参数传递层加隐式转换。

### 根因 E: 缺失泛型实例声明 (6 errors)

**症状**:
```
error: unknown type name 'System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_c_'
error: unknown type name 'System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_'
```

**根因**: `ReadOnlySpan`1<char>` 和 `ReadOnlySpan`1<byte>` 在 mscorlib 元数据中未被任何 TypeSpec 直接实例化(仅通过 `PasswordBasedEncryption.Decrypt` 的参数类型引用),但 R4 闭包克隆只克隆了 `int`/`object` 实参变体,未克隆 `char`/`byte` 变体。

**修复方案**: 在 `MetadataLoader.ExpandOpenInstances` 的 worklist fixpoint 中,增加对方法签名中泛型参数的扫描:
- 扫描 `PasswordBasedEncryption.Decrypt` 的参数类型 `ReadOnlySpan`1<c>` / `ReadOnlySpan`1<u1>` / `Span`1<u1>`
- 自动注册 `(ReadOnlySpan`1, ["System.Char"])` 和 `(ReadOnlySpan`1, ["System.Byte"])` 到 `genericInsts`

### 根因 F: 结构体类型 `AlgorithmIdentifierAsn` 不可转换 (2 errors)

**症状**:
```cpp
error: assigning to 'int32_t' (aka 'int') from incompatible type 'System_Security_Cryptography_X509Certificates_AlgorithmIdentifierAsn'
```

**位置**: `PasswordBasedEncryption.cpp:254,256`

**根因**: `arg0->Parameters` 在元数据中类型为 `Asn1OctetString`(引用类型),但 CodeGenerator 将其解码为值类型 `AlgorithmIdentifierAsn`(结构体)。赋值目标 `t1` 声明为 `int32_t` 是因为前一行的 `t1 = (*reinterpret_cast<AlgorithmIdentifierAsn*>(arg0))` 触发了 ldobj,而 ldobj 解码错误地将结构体按 int32 处理。

**修复方案**: 检查 `CodeGenerator.Ldobj` 对结构体的解码路径,确保结构体字段读取不被错误折叠为 int32。

### 根因 G: 跨文件调用签名不匹配 (若干)

**位置**: `PfxAsn.cpp:446,464`

**根因**: `PasswordBasedEncryption_Decrypt_1764f3075b84ba77` 的签名在 `generated.h` 中声明为接受 `ReadOnlySpan_1_int` 参数,但 `PfxAsn.cpp` 中调用时传入的是 `ReadOnlySpan_1_int` 的指针(取地址),导致不匹配。

---

## 4. 修复优先级

| 优先级 | 根因 | 影响行数 | 修复复杂度 |
|--------|------|----------|-----------|
| P0 | A (ReadOnlySpan→Span) | 24 | 低 — 插入 reinterpret_cast |
| P0 | B (Nullable 类型坍缩) | 21 | 中 — 需修改 ClosureArgShortName |
| P1 | C (op_Implicit 返回) | 19 | 低 — 插入 reinterpret_cast |
| P1 | D (ReadOnlyMemory 方法) | 8 | 中 — 同 A |
| P2 | E (缺失泛型实例) | 6 | 中 — 修改 ExpandOpenInstances |
| P2 | F (结构体 ldobj) | 2 | 中 — 修改 Ldobj 解码 |
| P3 | G (跨文件调用) | 2 | 低 — 同 A |

---

## 5. 推荐修复步骤

### Step 1: 修复 ReadOnlySpan→Span 隐式转换 (解决 A + D + G)

在 `CodeGenerator.cs` 的 `EmitIcallCallArgs` 方法中,增加一个 `in` 参数方向的隐式转换:

```csharp
// 在 EmitIcallCallArgs 中，对 ReadOnlySpan<T> → Span<T> 的转换
// 当目标参数类型为 Span<T>* 但实际参数类型为 ReadOnlySpan<T>* 时:
if (paramTypeIsSpan && argTypeIsReadOnlySpanOfSameT)
{
    sb.Append($"reinterpret_cast<{spanType}&>({argExpr})");
}
```

此修改影响 `PasswordBasedEncryption.cpp` 中所有 24 个根因 A 错误和 8 个根因 D 错误。

### Step 2: 修复 Nullable<T> 类型坍缩 (解决 B)

在 `MetadataLoader.ClosureArgShortName` 中增加前置规则:

```csharp
// 当 arg 是 ReadOnlyMemory<T> / ReadOnlySpan<T> / Span<T> 时
// 元素类型 T 必须保留原始元数据名（不做 32-bit 族归一）
if (arg.Contains("ReadOnlyMemory`1<") || arg.Contains("ReadOnlySpan`1<") || arg.Contains("Span`1<"))
{
    return SanitizeGenericArg(arg);  // 保留 System.Byte 而不是坍缩为 int
}
```

并在 `ExpandOpenInstances` 的 worklist 中增加对方法签名中泛型参数的扫描。

### Step 3: 修复 op_Implicit 返回值 (解决 C)

在 `CodeGenerator.cs` 的 `EmitCall` 中,当返回类型为 `Il2CppObject*` 但目标参数类型为泛型值类型指针时,自动插入 `reinterpret_cast`:

```csharp
// 在 EmitCall 中
if (returnType == "Il2CppObject*" && paramTypeIsGenericValueTypePtr)
{
    sb.Append($"reinterpret_cast<{paramType}>(...");
}
```

### Step 4: 修复缺失泛型实例 (解决 E)

在 `MetadataLoader.ExpandOpenInstances` 的 worklist fixpoint 中,增加对方法签名中泛型参数的扫描,自动注册 `(ReadOnlySpan`1, ["System.Char"])` 和 `(ReadOnlySpan`1, ["System.Byte"])` 到 `genericInsts`。

---

## 6. 预期修复后错误下降

| 修复步骤 | 预期解决错误数 | 预期剩余错误数 |
|----------|---------------|---------------|
| 初始 | — | 173 |
| Step 1 后 | -32 | 141 |
| Step 2 后 | -21 | 120 |
| Step 3 后 | -19 | 101 |
| Step 4 后 | -6 | 95 |

预期修复后 PasswordBasedEncryption 相关错误从 173 下降至约 95(剩余为需要单独处理的边界情况)。

---

*Report generated from r77 generation artifacts. All paths relative to `E:\il2cpp`.*
