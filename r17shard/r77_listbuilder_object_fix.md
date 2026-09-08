# r77 ListBuilder Object 实例化修复方案

生成时间: 2026/09/06 11:02:15

## 问题概述

r77 中存在约 100 个 System_RuntimeType_ListBuilder_1_object 相关错误，根因是 **泛型类型实例化时只生成 _int 实例化版本**，而 ListBuilder<object> 需要独立的 _object 实例化。

## 错误模式分析

### 典型错误

`
error: use of undeclared identifier 'System_RuntimeType_ListBuilder_1_object_Add_c10f6a0e5679f7f2'
error: cannot initialize a parameter of type 'System_RuntimeType_ListBuilder_1_int *' 
       with an rvalue of type 'Il2CppObject **'
error: no matching function for call to 'System_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int_PopulateRtFields_709a55d77c4a1a93'
`

### 根本原因

| 组件 | 当前行为 | 期望行为 |
|------|---------|---------|
| TypeEmitter.TypeCppName | 所有泛型类型统一追加 _int | 根据泛型参数追加 _int、_object 等 |
| 局部变量声明 | ListBuilder 局部变量声明为 ListBuilder_1_int | 应声明为 ListBuilder_1_object |
| 方法调用生成 | 调用 ListBuilder_1_int_Add_... | 应调用 ListBuilder_1_object_Add_... |

### 代码路径分析

`
TypeCppName(TypeDef t):
    if t.GenericArity > 0:
        return baseName + "_int"  // ← 问题：始终 _int
`

而 MethodRefResolver.GenericInstantiationName 已正确生成 _object 后缀：
`
args[i] = b switch {
    0x0E => "string",  // 正确映射
    0x08 => "int",
    // ...
}
`

## 修复方案

### 方案 A：修改 TypeCppName 接受泛型参数类型（推荐）

**位置**: src/il2cppc/Emit/TypeEmitter.cs:410-416

`csharp
// 修改前:
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}

// 修改后:
public static string TypeCppName(TypeDef t, string? genericArgSuffix = null)
{
    var baseName = Sanitize(t.FullName);
    if (t.GenericArity == 0) return baseName;
    // 使用后缀：_int、_object、_byte 等
    return baseName + "_" + (genericArgSuffix ?? "int");
}
`

### 方案 B：在 MetadataLoader 中添加多实例化支持

**位置**: src/il2cppc/MetadataLoader.cs

为 ListBuilder<T> 等泛型类型在加载时即生成所有实例化版本：

`csharp
// 检测到 ListBuilder<T> 类型时，同时生成：
// - System_RuntimeType_ListBuilder_1_int
// - System_RuntimeType_ListBuilder_1_object
// - 可能的其他实例化（byte, long 等）
`

### 方案 C：在 StackFold 中解析实际泛型参数

**位置**: src/il2cppc/Ir/StackFold.cs

当解析到泛型类型局部变量时，从 IL 签名中提取实际泛型参数类型：

`csharp
// 从局部变量的 TypeSpec 签名中提取泛型参数
// 例如 ListBuilder<object> → 参数 0x12 (CLASS) → System.Object → "_object"
`

## 推荐实施步骤

1. **修改 TypeEmitter.TypeCppName**：接受可选的泛型参数后缀
2. **修改局部变量声明路径**：在 CodeGenerator.EmitBody 中，当声明泛型类型局部变量时，从 LocalKind 中提取实际类型信息
3. **修改方法调用生成路径**：当生成泛型类型方法调用时，使用实际泛型参数后缀而非默认 _int
4. **添加 _object 实例化类型定义**：确保 generated.h 中生成 System_RuntimeType_ListBuilder_1_object 的 struct 定义

## 影响范围

| 类型 | 当前实例化 | 需要新增 |
|------|-----------|---------|
| ListBuilder<T> | _int | _object |
| Nullable<T> | _int | _byte, _object 等 |
| IEnumerator<T> | _int | _object |
| TaskAwaiter<T> | _int | _object |

## 风险评估

- **编译时间**：生成多个实例化版本会增加编译时间
- **二进制大小**：会增加生成的 C++ 代码量
- **兼容性**：需要确保方法调用和类型声明使用一致的后缀

## 验证方法

修复后应满足：
1. MemberInfoCache_1_int.cpp 中的 ListBuilder_1_object 方法调用正确链接
2. generated.h 包含 System_RuntimeType_ListBuilder_1_object struct 定义
3. 无新的 cannot-init 错误产生
