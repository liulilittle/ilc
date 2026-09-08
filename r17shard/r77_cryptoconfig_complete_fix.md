# r77 CryptoConfig cannot-cast 错误修复方案

## 错误统计

- **文件**: `System_Security_Cryptography_CryptoConfig.cpp`
- **错误总数**: 24 个 cannot-cast 类错误
  - `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'`: 18 个
  - `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'`: 4 个
  - `invalid operands to binary expression ('int32_t' and 'std::nullptr_t')`: 2 个

## 根因分析

### 错误模式

所有错误集中在两个方法：
1. **`InitializeNameMappings`** (行 932, 934, 937, 939, 1440, 1441, 1446, 1447, 1448, 1462, 1465)
2. **`InitializeOidMappings`** (行 1539, 1542, 1654, 1655, 1698)

### 直接原因

局部变量 `loc10` 被声明为 `int32_t` 类型，但代码中把它当作 `Il2CppObject*` 使用：

```cpp
// 行 858/1610: loc10 被推断为 int32_t
int32_t loc10;

// 行 901: 赋值给 Il2CppObject* loc0（类型不匹配）
loc0 = System_Collections_Generic_Dictionary_2_int_GetValueOrDefault_d171bc0a02c50c8f(..., arg0);

// 行 932: 把 int32_t 当作 Il2CppObject* 传给 is_instance
t11 = (il2cpp::il2Cpp_is_instance(loc10, &System_Type_class) ? static_cast<Il2CppObject*>(loc10) : nullptr);
//                                                                    ^^^^^^^^
// loc10 是 int32_t，不能 static_cast 到 Il2CppObject*

// 行 934: 尝试把 int32_t 当作指针比较
loc0 = (... ((loc10) == nullptr ? ... : ...));
//              ^^^^^^^^
// int32_t == nullptr → invalid operands
```

### 深层原因：局部变量类型推断错误

`loc10` 的类型推断来自 IL 局部变量签名或首次赋值。但这里的问题是：

1. **IL 签名中 loc10 的实际类型是 `System.Int32`（值类型）**
2. **但代码生成器推断为 `Il2CppObject*` 使用**

这说明 **局部变量类型推断逻辑有 bug**：`CodeGenerator.cs` 中的 `locKind` 字典未能正确记录 `loc10` 的类型，或者推断时遗漏了该局部变量。

对比同文件中其他方法（如 `get_DefaultOidHT`，行 270），所有 `loc*` 都是 `Il2CppObject*` 类型，说明 `CryptoConfig` 的特定方法触发了类型推断失败。

### 根因定位

在 `CodeGenerator.cs` 的类型推断逻辑中（行 ~1140-1370），局部变量类型从以下来源推导：
- `bodyInfo.LocalKinds[i]`（IL 声明的局部类型）
- 首次赋值的 `ExprKind`

当 `loc10` 在 IL 中声明为 `System.Int32`（值类型），但首次被赋值一个返回 `Il2CppObject*` 的函数调用结果时，推断逻辑未能正确处理这种类型冲突，导致 `loc10` 既被声明为 `int32_t`，又在后续被当作 `Il2CppObject*` 使用。

## 修复方案

### 方案 A：局部变量类型推断修正（推荐）

在 `CodeGenerator.cs` 的类型推断循环中，增加对 `int32_t` 局部变量的检测：

```csharp
// 在 locKind 推断循环后，添加类型一致性校验
foreach (var kvp in locKind.ToList())
{
    if (kvp.Value == "val" && _curLocKinds.TryGetValue(kvp.Name, out var declared) && declared != "val")
    {
        // IL 声明为值类型但推断为引用类型时，以 IL 声明为准
        // 但需要在使用点插入显式转换
    }
}
```

### 方案 B：赋值点插入显式 box/unbox（最小改动）

对于 `loc10` 被当作 `Il2CppObject*` 使用的场景，在赋值点插入 `il2cpp_box_int32`：

```cpp
// 原始（错误）：
loc0 = System_Collections_Generic_Dictionary_2_int_GetValueOrDefault_d171bc0a02c50c8f(..., arg0);
// loc0 是 Il2CppObject*, 但返回的是 int32_t

// 修复后：
loc0 = reinterpret_cast<Il2CppObject*>(
    System_Collections_Generic_Dictionary_2_int_GetValueOrDefault_d171bc0a02c50c8f(..., arg0));
```

### 方案 C：声明修正（针对此文件快速修复）

直接在 `loc10` 声明处修正类型：

```cpp
// 行 858: 改为
Il2CppObject* loc10 = nullptr;  // 而非 int32_t loc10;

// 行 1610: 改为
Il2CppObject* loc10 = nullptr;  // 而非 int32_t loc10;
```

但这需要修改 `locKind` 推断逻辑，使 `loc10` 在首次赋值 `Dictionary_2_int_GetValueOrDefault`（返回 `Il2CppObject*`）时被正确推断。

### 方案 D：ExprKind 推断修复（根本修复）

在 `ExprKind` 函数中，对 `CallExpr` 的返回类型做严格判断：
- 如果 `CallExpr` 的返回类型是 `int32_t`（来自 `MethodSignatureCoder.Decode`），但目标局部变量被声明为 `Il2CppObject*`，则：
  - 在赋值点插入 `reinterpret_cast<Il2CppObject*>(...)` 或 `il2cpp_box_int32`
  - 或者将局部变量类型降级为 `int32_t`（如果所有使用点都是值语义）

## 推荐实施方案

**方案 C + 局部推断修正**：

1. 在 `CodeGenerator.cs` 的局部变量声明循环中（约行 1260），当 `locKind` 推断为 `"val"` 但 IL 签名声明为 `Il2CppObject*` 时，以 IL 声明为准
2. 同时检查所有使用点，确保类型一致
3. 如果 IL 签名确实是 `int32_t`（值类型），则在使用点插入 `il2cpp_box_int32` 转换

## 影响范围

- 此 bug 影响所有 IL 中声明为值类型局部变量但被当作引用类型使用的场景
- `CryptoConfig` 是典型案例，其他文件可能有相同问题
- 修复后预期减少约 24 个 cannot-cast 错误

## 验证方法

1. 修改后重新生成 `mscorlib_gen_r77`
2. 检查 `System_Security_Cryptography_CryptoConfig.cpp` 编译 0 错误
3. 验证 `InitializeNameMappings` 和 `InitializeOidMappings` 的功能正确性（单元测试）
