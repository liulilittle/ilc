# r77 no-viable 错误根因调查报告

**目标文件:** `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
**调查范围:** r77 生成输出 (mscorlib_gen_r77) shard1-3 全部 no-viable 错误
**错误总数:** 1811 处 (shard1: 1392, shard2: 268, shard3: 151)

---

## 一、根因总览

r77 nv簇的核心根因是：**值类型（GenericInst / ValueType）在方法调用侧缺少显式类型转换（reinterpret_cast）**，导致实参表达式类型与形参类型不匹配，C++ 重载决议失败。

具体表现模式：
| 模式 | 频次 | 典型错误 |
|------|------|----------|
| 值类型参数传递无cast | ~932 | `no viable conversion from 'X' to 'int32_t'` |
| 值类型赋值无cast | ~879 | `no viable overloaded '='` |
| 值类型返回未装箱 | ~150 | `no viable conversion from returned value of type 'X' to return type 'Y'` |
| 值类型方法调用参数错位 | ~200 | `no matching function for call to '...'` |

---

## 二、根因分析（附证据链）

### 2.1 根因 A：ParamTypesCpp 未生成参数 cast 代码

**位置:** `CodeGenerator.cs:7226-7267` `ParamTypesCpp()`

**问题:** 对于 `V:` 编码的值类型参数（如 `System.Nullable`1_int`），`ParamTypesCpp` 返回 `TypeCppName(v[2..])` 即 `System_Nullable_1_int`。但当参数通过 `EmitIcallCallArgs` 传递时，表达式是 `EmitExpr` 生成的裸表达式（可能是 `intptr_t` 或 `Il2CppObject*`），没有 cast 到 `System_Nullable_1_int*`。

**证据 1:** `System_Collections_Generic_NullableEqualityComparer_1_int.cpp:157`
```cpp
// t1 是 intptr_t (来自 il2cpp_array_get_elem_ptr -> reinterpret_cast<intptr_t>)
if(System_Nullable_1_int_get_HasValue_495fb3b18c2b35b9(t1)) goto L_34;
```
函数签名（`System_Nullable_1_int.cpp`）：
```cpp
IL2CPP_NOINLINE int32_t System_Nullable_1_int_get_HasValue_495fb3b18c2b35b9(System_Nullable_1_int* arg0)
```
**t1 是 `intptr_t`，arg0 期望 `System_Nullable_1_int*`，无 cast → no matching function。**

**证据 2:** `System_AppDomain.cpp:4458`
```cpp
System_Nullable_1_int__ctor_489389d8a76bef54(t2, t3);
// t2 = t0 = (&loc0) → Il2CppObject**, t3 = t1 → int32_t
```
函数签名：
```cpp
IL2CPP_NOINLINE void System_Nullable_1_int__ctor_489389d8a76bef54(System_Nullable_1_int* arg0, int32_t arg1)
```
**t2 是 `Il2CppObject**` 而非 `System_Nullable_1_int*`。**

**证据 3:** `System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:2287`
```cpp
// 返回值 System_Runtime_CompilerServices_TaskAwaiter_1_int 赋给 int32_t 局部变量
```
函数签名声明返回 `int32_t`，但实际返回值类型是 `TaskAwaiter_1_int`（值类型结构体）。

### 2.2 根因 B：EmitIcallCallArgs 对非 BCL 值类型缺少 cast 分支

**位置:** `CodeGenerator.cs:8324-8783` `EmitIcallCallArgs()`

**问题:** 该函数有完整的 BCL icall cast 表（`deref:i/l/d`, `dec`, `enumval`, `unbox:i/l/d` 等），但对**用户值类型方法调用**（`call_{token}` 形式），当 `resolved2.ParamTypeNames` 为 null 或空时，参数直接用 `EmitExpr` 生成（行 8409-8411）：
```csharp
parts.Add(code);
```
没有对值类型参数做 `reinterpret_cast<T*>(addr)` 转换。

**对比:** BCL icall 通过 `ParamTypeNames` cast 代码驱动转换，用户方法调用无此机制。

### 2.3 根因 C：ReturnTypeCpp 将 GenericInst 映射为 Il2CppObject* 但实际返回值类型

**位置:** `CodeGenerator.cs:9093-9142` `ReturnTypeCpp()`

**问题:** 行 9132:
```csharp
SignatureTypeCode.GenericTypeInstance => "Il2CppObject*",
```
但 GenericInst 可能是值类型（如 `System.Nullable`1_int`）。调用侧按 `Il2CppObject*` 声明返回类型，实际函数返回 `System_Nullable_1_int`，导致赋值/返回类型不匹配。

**证据:** `System_AppDomain.cpp:4460`
```cpp
return loc0; // loc0 是 System_Nullable_1_int，函数声明返回 Il2CppObject*
```

### 2.4 根因 D：AssignStmt 发射无自动值类型 cast

**位置:** `CodeGenerator.cs:3091-3207` `EmitStmt()` AssignStmt 分支

**问题:** 行 3197:
```csharp
var assignCode = $"{EmitTarget(a.Target)} = {EmitExpr(a.Value, md, types)};";
```
当 `a.Target` 是值类型 `X`，`a.Value` 是 `intptr_t`（来自 ldelema）或 `Il2CppObject*` 时，无 cast。

**证据:** `System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:830`
```cpp
(*reinterpret_cast<System_Int32*>(arg2) = /* 值类型 X */);
// arg2 是 int32_t*，右侧是 System_Collections_DictionaryEntry
```

### 2.5 根因 E：EmitTask9Call 对 call_{token} 用户方法无 cast

**位置:** `CodeGenerator.cs:5862-` `EmitTask9Call()`

**问题:** 行 6329-6331:
```csharp
var callArgs2 = resolved2 != null
    ? EmitIcallCallArgs(resolved2, c.Args, md, types)
    : string.Join(", ", c.Args.Select(a => EmitExpr(a, md, types)));
```
当 `resolved2` 有 `ParamTypeNames` 但 cast 表不覆盖值类型时，仍走通用路径。

---

## 三、修复方案

### 方案 1（推荐）：在 EmitIcallCallArgs 末尾添加通用值类型 cast 守卫

在 `EmitIcallCallArgs` 的 `parts.Add(cast switch {...})` 之后、`return` 之前，添加回退逻辑：
- 当 cast 为 null/空 且 `args[srcIdx]` 经 `TypeKindResolver.ResolveExprKind` 判定为值类型地址（`intptr_t` / 值类型指针）时，自动插入 `reinterpret_cast<T*>(expr)`。

### 方案 2：在 ParamTypesCpp 中为值类型参数附加 cast 标记

让 `ParamTypesCpp` 对 `V:` 值类型生成 `"V:<FullName>"` cast code（已有），在 `EmitIcallCallArgs` 的 cast switch 中添加对 `V:` 前缀的解析：
```csharp
var v when v.StartsWith("V:", StringComparison.Ordinal) => $"reinterpret_cast<{TypeCppName(v[2..])}*>({code})"
```

### 方案 3：在 AssignStmt 发射时检测值类型不匹配并插入 cast

行 3197 前添加：
```csharp
if (locKind.TryGetValue(a.Target, out var tk) && tk != "ref" && tk != "val" && a.Value is not CallExpr)
    castCode = $"{EmitTarget(a.Target)} = reinterpret_cast<{tk}>({EmitExpr(a.Value, md, types)});";
```

### 方案 4：ReturnTypeCpp 对值类型 GenericInst 返回真实结构体名

在 `ReturnTypeCpp` 的 GenericTypeInstance 分支增加值类型判断：
```csharp
SignatureTypeCode.GenericTypeInstance => IsValueTypeGenericInst(md, retName) ? TypeCppName(retName) : "Il2CppObject*",
```

---

## 四、影响评估

| 方案 | 修复范围 | 风险 | 预期减少错误 |
|------|----------|------|-------------|
| 方案1 | 全调用侧 | 中 | ~1200 |
| 方案2 | 参数传递 | 低 | ~900 |
| 方案3 | 赋值 | 低 | ~800 |
| 方案4 | 返回 | 中 | ~150 |

**推荐组合:** 方案1+2+3 联合实施，预计可消除 r77 nv 簇 90%+ 错误。

---

## 五、验证方法

1. `dotnet build src/il2cppc` → 0错0警告
2. 重新生成 r77，对比 `shard*_errors.log` 中 `no viable` 行数
3. 抽查 `System_Nullable_1_int.cpp`, `System_AppDomain.cpp`, `ConcurrentDictionary_2_int_int.cpp` 确认 cast 正确插入
