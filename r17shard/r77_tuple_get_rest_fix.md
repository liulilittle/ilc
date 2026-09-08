# r77 System.Tuple`8.get_Rest 56 nmf 错误修复方案

源: `E:\il2cpp\r17shard\nmf_raw_lines.log` (56 行 get_Rest 相关 nmf)
分析时间: 2026-09-06

## 一、错误实例

全部 56 个错误都指向同一函数:

```
System_Tuple_8_int_get_Rest_b3ab99604a10375a
```

错误位置: `System_TupleExtensions.cpp` 第 559, 605, 608, 654, 657, 660, 706, 709, 712, 715, 761, 764, 767, 770, 773, 819, 822, 825, 828, 831, 834, 837, 840, 843, 879, 882, 885, 888, 891, 894, 897, 900, 903, 906, 909, 912, 915, 918, 921, 924, 927, 930, 933, 936, 939, 942, 945, 948, 951, 954, 957, 960, 963, 966, 969, 972 行

## 二、根因分析

### 2.1 错误声明 (generated.h:66930)

```cpp
int32_t System_Tuple_8_int_get_Rest_b3ab99604a10375a(Il2CppObject* arg0);
```

### 2.2 实际调用 (System_TupleExtensions.cpp:559-562)

```cpp
// 第一层: arg0 是 Il2CppObject* (正确)
t0 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(arg0);

// 第二层: t0 是 int32_t (返回值类型), 但函数期望 Il2CppObject* → nmf!
t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(t0);
```

### 2.3 .NET 语义

`System.Tuple<T1..TRest>.Rest` 返回的是 **`System.Tuple`** (引用类型), 不是 `int32`。

- `System.Tuple` 继承自 `System.Object` → 引用类型
- `System.Tuple<T1..T7>.Rest` 返回剩余的 `System.Tuple` 实例
- 正确 C++ 签名: `Il2CppObject* get_Rest(Il2CppObject* arg0)`

### 2.4 错误传播链

```
System.Tuple`8<int>.get_Rest() → 声明返回 int32_t
  ↓
返回值被赋值给 int32_t 局部变量 t0
  ↓
下一层调用 System.Tuple`8<int>.get_Rest(t0) 期望 Il2CppObject* 参数
  ↓
g++ 报错: no matching function for call to '...' (int32_t vs Il2CppObject*)
```

## 三、修复方案

### 方案 A: MethodRefResolver 中检测 System.Tuple (推荐)

在 `MethodRefResolver.ResolveMethodDefinition` 中，当检测到 `System.Tuple` + `get_Rest`/`get_Item` 时，强制返回 `Il2CppObject*`。

```csharp
// MethodRefResolver.cs - ResolveMethodDefinition 方法中
private static ResolvedMethod ResolveMethodDefinition(MetadataReader md, MethodDefinitionHandle handle)
{
    var m = md.GetMethodDefinition(handle);
    var name = md.GetString(m.Name);
    var ownerFullName = md.GetFullName(m.GetDeclaringType());
    var (paramCount, returnType, paramTypes, returnTypeName) = MethodSignatureCoder.Decode(md, m.Signature);
    
    // FIX: System.Tuple<T1..T8>.get_Rest 和 get_Item 返回引用类型
    if (ownerFullName.StartsWith("System.Tuple`") && 
        (name == "get_Rest" || name.StartsWith("get_Item")))
    {
        // 强制返回 Il2CppObject* 而非 int32_t
        return new ResolvedMethod(
            UserMethodCppName(ownerFullName, name, paramTypes),
            paramCount, false, true, // ReturnsRef = true
            true, // IsRefType = true
            ParamRefFlags(paramTypes), isInstance,
            ResultKind: null,
            ReturnTypeName: ownerFullName); // 返回类型是 Tuple 自身
    }
    
    // ... 原有逻辑
}
```

**预期消除**: 56 行 (100%)

### 方案 B: TypeEmitter.EmitMethodSignature 中修正

在 `TypeEmitter.EmitMethodSignature` 中，当检测到 `System.Tuple` + `get_Rest`/`get_Item` 时，将返回类型从 `int32_t` 改为 `Il2CppObject*`。

```csharp
// TypeEmitter.cs - EmitMethodSignature 方法中
if (ownerFullName.StartsWith("System.Tuple`") && 
    (methodName == "get_Rest" || methodName.StartsWith("get_Item")))
{
    retCpp = "Il2CppObject*";
}
```

### 方案 C: 手动修正 generated.h (快速验证)

直接在 `generated.h` 中修改:

```cpp
// 修改前
int32_t System_Tuple_8_int_get_Rest_b3ab99604a10375a(Il2CppObject* arg0);

// 修改后
Il2CppObject* System_Tuple_8_int_get_Rest_b3ab99604a10375a(Il2CppObject* arg0);
```

## 四、推荐方案

**方案 A** — 在 `MethodRefResolver` 中检测 `System.Tuple` + `get_Rest`/`get_Item` 并强制返回 `Il2CppObject*`。

理由:
1. 集中处理，不分散到多个文件
2. 同时覆盖 `get_Item` 和 `get_Rest` (共 266 行 Pattern A)
3. 不影响 `System.ValueTuple` (值类型, 返回 int32_t 正确)
4. 不影响 `System.Tuple` 的 `get_Item` 当 T 是 int32 时

## 五、影响范围

- 仅影响 `System.Tuple<T1..T8>` 的 `get_Item1~7` 和 `get_Rest` 方法
- 不影响 `System.ValueTuple<T1..T8>` (值类型)
- 不影响 `System.Tuple` 的 `Equals`, `GetHashCode` 等方法
- 不影响非 Tuple 类型的 `get_Item` 方法

## 六、验证方法

1. 修改 `MethodRefResolver.cs` 添加 Tuple 检测
2. 重新生成 mscorlib_gen_r77
3. 编译 System_TupleExtensions.cpp
4. 验证 56 个 get_Rest nmf 错误全部消除
5. 同时检查 Tuple`1-8 的其他调用点不受影响
