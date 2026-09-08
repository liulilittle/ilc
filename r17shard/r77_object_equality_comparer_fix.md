# r77 ObjectEqualityComparer`1 cannot-cast 错误修复方案

## 一、错误现状

r77 扫描 shard1 日志中，`System.Collections.Generic.ObjectEqualityComparer`1_*` 共 4 个实例文件，**15 行 cannot-cast 错误**，集中出现在 `IndexOf` / `LastIndexOf` 两个方法。

### 错误按实例分布

| 实例文件 | 错误行数 | 错误模式 |
|---|---|---|
| `ObjectEqualityComparer_1_Byte.cpp` | 15 行 | `int32_t`→`Il2CppObject*` cast + `intptr_t`→`Il2CppObject*` param |
| `ObjectEqualityComparer_1_int.cpp` | 2 行 | `int32_t*`→`Il2CppObject*` param |
| `ObjectEqualityComparer_1_int_array.cpp` | 6 行 | `int32_t`→`Il2CppObject*` cast + `intptr_t`→`Il2CppObject*` param |
| `ObjectEqualityComparer_1_double.cpp` | 2 行 | `double*`→`Il2CppObject*` param |

### 错误的具体位置（以 Byte 为例）

```
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:112:10:   t0 = static_cast<Il2CppObject*>(arg1);             // arg1 int32_t
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:114:10:   t1 = static_cast<Il2CppObject*>(arg2);             // arg2 int32_t
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:116:10:   t2 = static_cast<Il2CppObject*>(arg2);             // arg2 int32_t
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:117:53:   icall_boxed_equals_ext2(static_cast<Il2CppObject*>(arg1), t2);
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:121:10:   t4 = static_cast<Il2CppObject*>(arg2);
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:140:10:   t0 = static_cast<Il2CppObject*>(arg1);             // GetHashCode
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:144:56:   icall_object_get_hash_code(static_cast<Il2CppObject*>(arg1));
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:176:10:   t0 = static_cast<Il2CppObject*>(arg2);             // IndexOf
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:182:10:   t2 = static_cast<Il2CppObject*>(t1);               // box int32 t1
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:195:10:   t4 = static_cast<Il2CppObject*>(t1);               // box int32 t1
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:198:10:   t6 = static_cast<Il2CppObject*>(arg2);
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:199:49:   icall_boxed_equals_ext2(t5, t6);                    // t5 = intptr_t (ldelema)
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:238:10:   t0 = static_cast<Il2CppObject*>(arg2);             // LastIndexOf
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:244:10:   t2 = static_cast<Il2CppObject*>(t1);               // box int32 t1
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:257:10:   t4 = static_cast<Il2CppObject*>(t1);               // box int32 t1
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:260:10:   t6 = static_cast<Il2CppObject*>(arg2);
System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp:261:49:   icall_boxed_equals_ext2(t5, t6);                    // t5 = intptr_t (ldelema)
```

---

## 二、根因分析

### 2.1 直接原因

编译器在 **emit 阶段** 对已知值类型的 box 源（`int32_t` / `intptr_t` / `double*` / `int32_t*`）发射了 `static_cast<Il2CppObject*>` 或裸值传参，而 C++ 不允许非指针算术类型隐式转换为指针类型。

### 2.2 错误的真正来源链

**链 1：整型值 → Il2CppObject* cast（静态类型已知错误）**

```
IL: ldc.i4 int32_value
    box [System.Int32]
```

`ILOpCode.Box` 在 StackFold 里生成 `CallExpr { Function = "box_{token}", ReturnsRef = true }`，源操作数在 stack 上是一个 `IntConstExpr`（整型常量）。

EmitExpr 对 `box_{token}` 的展开（CodeGenerator.cs:5856-5918）：
```csharp
var boxVal = EmitExpr(c.Args[0], md, types);
// boxVal = EmitExpr(IntConstExpr) = 一个整数字面量如 "5" 或 "42"
return $"il2cpp::il2cpp_box_int32(nullptr, {boxVal})";
```

但在某些路径下，折叠阶段（Normalize / FoldStmts）把 `box_{token}(call_expr)` 重写成：
```csharp
t0 = static_cast<Il2CppObject*>(arg)   // 这里 arg 是 int32_t 参数
```

**路径 A — 参数 argN 是 int32_t 时：**

对于 `ObjectEqualityComparer`1_Byte::Equals(Il2CppObject* arg0, int32_t arg1, int32_t arg2)`：

1. `arg1` 是 `int32_t`（值类型参数，metadata 签名里是 int32）
2. IL 里没有显式 `box`，而是编译器自动在传入 Il2CppObject* 参数处要求 box
3. `ObjectEqualityComparer`1<T>::Equals(T value1, T value2)` 的 T=byte（System.Byte），但实际 IL 签名在 `T : class` 约束放松下用了 `int32_t` 参数 + 隐式 box
4. StackFold 给整型 arg 打 `static_cast<Il2CppObject*>` → 编译器报错

**链 2：ldelema 地址 → icall_boxed_equals_ext2 传参错误**

```
t5 = reinterpret_cast<intptr_t>(il2cpp_array_get_elem_ptr(...));
t6 = static_cast<Il2CppObject*>(arg2);
icall_boxed_equals_ext2(t5, t6);   // t5 是 intptr_t，需要 Il2CppObject*
```

原型 icall_boxed_equals_ext2(Il2CppObject* a, Il2CppObject* b)。

在 `IndexOf` / `LastIndexOf` 里，比较数组元素 `arr[i]`（int32 值）与目标值 `arg2`（int32 值）时：
1. 对数组用 ldelema 获取元素地址 → intptr_t t5
2. 对目标值 arg2 做 box → Il2CppObject* t6
3. 调用 icall_boxed_equals_ext2(t5, t6) → intptr_t 不匹配 Il2CppObject*

### 2.3 深一层根因：类型系统的"装箱等价"假设不成立

转译器假设：
- 所有 `box T`（T 为值类型）→ `il2cpp::il2cpp_box_int32(nullptr, value)` → 返回 `Il2CppObject*`
- `box_{token}` 的源 value 一定是"整型值"（int32 / int64 等）

但 `ObjectEqualityComparer`1<T> where T : struct` 的特殊之处在于：
- .NET BCL 实现里 `IndexOf(T[] array, T value)` 的 T 是泛型值类型
- 转译器把 T 实例化为具体类型（Byte、Int32 等）时，方法签名的参数变成了具体 int32_t
- IL 里 box 节点操作的是 int32_t 值（stack 上的 int32_t），但 stack-fold 里某些优化路径（如"已知值的 box→直接 cast"）错误地假设了"值类型变量可以直接 static_cast 为 Il2CppObject*"

这个假设只对已装箱的值（Il2CppObject* 类型）成立，对未装箱的 int32_t 值不成立。

---

## 三、修复方案

### 方案 A：EmitExpr box 路径增加 intptr_t / 整型守卫（最小改动）

**位置**：CodeGenerator.cs:5856 附近，`if (c.Function.StartsWith("box_", ...) ...)` 分支。

**改动**：在 `boxType switch` 中，已知整型值类型的 boxVal 前插入显式地址转换：

```csharp
// 当前代码：
"System.Int32" => $"il2cpp::il2cpp_box_int32(nullptr, {boxVal})",
"System.Byte"  => $"il2cpp::runtime::icall_box_byte({boxVal})",
...

// 问题：boxVal 在某些路径下已经是 int32_t 值的字符串（如 "arg1" = int32_t）
// 但如果在折叠路径里 box_{token} 被重写为 static_cast<Il2CppObject*>(int32_t_val)
// 整型 → 指针直接 cast 是 ill-formed
```

**修复**：在 boxVal 是整型表达式（不是 Il2CppObject*）时，加一步显式 box 调用。实际上当前代码已经用 `il2cpp::il2cpp_box_int32(nullptr, {boxVal})` 做了 box，**错误不在 box 展开路径**，而是在**赋值/传参路径**里 box_{token} 没有被正确展开为 icall，而是被错误折叠为 static_cast。

### 方案 B：Normalize 路径修正（根因修复）

**根因定位**：在 `Normalize.FoldStmts` 或 `StackFold` 后续 pass 里，存在一条优化路径把：
```
t = box_{token}(int_val)  →  t = static_cast<Il2CppObject*>(int_val)
```

这条路径只在 int_val 已经是 `Il2CppObject*` 时合法（比如 unbox 后的值、newobj 分配的对象）。对于 int32_t 值，它错误地应用了"cast 等价 box"。

**修复点 1**：StackFold 里 box 的"cast 优化"分支增加守卫：

```csharp
// 找到：box -> static_cast 优化（在 StackFold.cs 或 Normalize.cs 里）
// 伪代码：
if (a.Value is CallExpr boxCall && boxCall.Function.StartsWith("box_", StringComparison.Ordinal))
{
    // 优化：如果源 value 已经是 Il2CppObject*，省略 box，直接用 static_cast
    var boxArg = boxCall.Args[0];
    // BUG：这里没有检查 boxArg 的类型，直接 static_cast<Il2CppObject*>(EmitExpr(boxArg))
    // 当 boxArg 是 int32_t 时，EmitExpr 返回 "arg1"，cast 失败
}
```

**修复**：检查 boxArg 的 ResultKind：
```csharp
if (boxArg is LocExpr loc && _curLocKinds.TryGetValue(loc.Name, out var kind) && !kind.Contains("ref") && !kind.Contains("ptr"))
{
    // boxArg 是值类型（int32_t / int64_t / double 等）→ 必须调用 box icall，不能 cast
    // 让后续 EmitExpr 的 box_ 分支处理
    continue; // 不要应用 cast 优化
}
```

**修复点 2**：`ldelema_{token}` 的结果传给 `icall_boxed_equals_ext2` 时：

```csharp
// t5 = reinterpret_cast<intptr_t>(il2cpp_array_get_elem_ptr(...));
// t6 = static_cast<Il2CppObject*>(arg2);
// icall_boxed_equals_ext2(t5, t6)  // t5 是 intptr_t 不是 Il2CppObject*
```

在 EmitExpr 的 `ldelema_` 分支里，如果 ldelema 的结果用作 `icall_boxed_equals_ext2` 的参数，需要先显式 box：

```csharp
// ldelema 路径（CodeGenerator.cs:6197-6202）
if (c.Function.StartsWith("ldelema_", StringComparison.Ordinal))
{
    var ldelemaPtr = ...;
    return $"reinterpret_cast<intptr_t>({ldelemaPtr})";
}
```

修复方案：调用点识别 `icall_boxed_equals_ext2` 的参数类型，把 intptr_t 显式转为 Il2CppObject*：

```csharp
// 在 EmitExpr 的 icall_boxed_equals_ext2 展开处（或通用 icall 参数处理处）
if (c.Function == "icall_boxed_equals_ext2" || c.Resolved?.CppName == "il2cpp::runtime::icall_boxed_equals_ext2")
{
    // 强制每个参数都 cast 为 Il2CppObject*（即使已经是 intptr_t）
    var arg0 = c.Args[0] is CallExpr ca0 && ca0.Function.StartsWith("ldelema_")
        ? $"static_cast<Il2CppObject*>(reinterpret_cast<void*>({EmitExpr(c.Args[0], md, types)}))"
        : $"static_cast<Il2CppObject*>({EmitExpr(c.Args[0], md, types)})";
    var arg1 = ... 同上 ...;
    return $"il2cpp::runtime::icall_boxed_equals_ext2({arg0}, {arg1})";
}
```

### 方案 C：ObjectEqualityComparer 专用 LOUD 豁免（临时快速关闭）

如果方案 B 涉及改动太大，可以对 ObjectEqualityComparer`1 的 IndexOf / LastIndexOf / Equals 方法添加 MethodMap 专用映射，让它们绕过泛型 box 路径，直接调用 Equals 的 object 版本。

---

## 四、修复方案推荐

**推荐方案 B（根因修复）**，分两步实施：

### Step 1（Box→Cast 守卫）

文件：`src/il2cppc/StackFold.cs` 或 `src/il2cppc/Ir/Normalize.cs`

找到 box→static_cast 的优化代码，增加 int32_t 类型守卫。

```csharp
// 伪代码（具体定位需 grep static_cast.*Il2CppObject.*box 相关代码）
// 当前位置待定位：在 StackFold.cs 的某个"简化 pass"里
if (boxCall != null && IsIntegerType(boxCall.Args[0]))
{
    // 不要做 static_cast 优化，让后续 box_{token} icall 处理
    continue;
}
```

### Step 2（ldelema 传参 cast）

文件：`src/il2cppc/Emit/CodeGenerator.cs`

在 `EmitExpr` 中 `icall_boxed_equals_ext2` 的参数发射处增加显式 Il2CppObject* cast。

```csharp
// 在 box_{token} 展开之前，对 icall_boxed_equals_ext2 参数做 cast
if (c.Resolved?.CppName == "il2cpp::runtime::icall_boxed_equals_ext2")
{
    var a0 = EmitExpr(c.Args[0], md, types);
    var a1 = EmitExpr(c.Args[1], md, types);
    // ldelema 结果 reinterpret_cast 后是 intptr_t，需要 cast 回 Il2CppObject*
    var cast0 = c.Args[0] is CallExpr ca0 && ca0.Function.StartsWith("ldelema_")
        ? $"reinterpret_cast<Il2CppObject*>(static_cast<void*>({a0}))"
        : $"static_cast<Il2CppObject*>({a0})";
    var cast1 = c.Args[1] is CallExpr ca1 && ca1.Function.StartsWith("ldelema_")
        ? $"reinterpret_cast<Il2CppObject*>(static_cast<void*>({a1}))"
        : $"static_cast<Il2CppObject*>({a1})";
    return $"il2cpp::runtime::icall_boxed_equals_ext2({cast0}, {cast1})";
}
```

### 预期收益

- `ObjectEqualityComparer`1_Byte`：15 行清零
- `ObjectEqualityComparer`1_int`：2 行清零
- `ObjectEqualityComparer`1_int_array`：6 行清零
- `ObjectEqualityComparer`1_double`：2 行清零
- 总计 25 行 cannot-cast 消失，涉及 4 个文件

---

## 五、验证方法

```bash
# 1. 编译验证
dotnet build src/il2cppc
# 预期：0 警告 0 错误

# 2. 生成 r78
dotnet run --project src/il2cppc -- C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll E:\il2cpp\out\mscorlib_gen_r78

# 3. 检查 4 个文件编译
g++ -fsyntax-only -std=c++17 -I E:\il2cpp\out\mscorlib_gen_r78\include\internal ^
    E:\il2cpp\out\mscorlib_gen_r78\src\System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp ^
    E:\il2cpp\out\mscorlib_gen_r78\src\System_Collections_Generic_ObjectEqualityComparer_1_int.cpp ^
    E:\il2cpp\out\mscorlib_gen_r78\src\System_Collections_Generic_ObjectEqualityComparer_1_int_array.cpp ^
    E:\il2cpp\out\mscorlib_gen_r78\src\System_Collections_Generic_ObjectEqualityComparer_1_double.cpp
# 预期：EXIT 0，无 cannot-cast

# 4. 全量三片扫描
# 预期：r78 总 fail < r77 1441，cannot-cast 下降 ≥ 25 行
```

---

## 六、边界风险评估

| 风险 | 影响 | 缓解 |
|---|---|---|
| 方案 B Step 1 改动 box→cast 守卫 | 其他依赖"box→static_cast"优化的泛型路径可能退化（回退到 icall box） | 守卫仅针对值类型（int32_t / int64_t / double 等），不影响 ref 类型 box |
| 方案 B Step 2 改动 icall_boxed_equals_ext2 参数 cast | 现有正确路径（参数已是 Il2CppObject*）加 static_cast 是 identity，无性能损失 | 确认 static_cast<Il2CppObject*>(Il2CppObject*) 编译为 no-op |
| ObjectEqualityComparer 以外其他 EqualityComparer 实例可能仍有同类错误 | 本次仅处理 ObjectEqualityComparer，其他（如 GenericEqualityComparer、自定义 IEqualityComparer）待后续扫描 | 修复后如有同类错误，用相同方案类推 |

---

## 七、实施顺序

1. **定位 box→static_cast 优化代码的精确行号**（grep `static_cast.*Il2CppObject` 在 StackFold.cs / Normalize.cs）
2. **编写守卫条件**：检查 box 源参数是否为值类型（int32_t 等非 ref/ptr）
3. **增加 icall_boxed_equals_ext2 参数 cast 处理**：CodeGenerator.cs EmitExpr 中加专用分支
4. **W3 备份**
5. **C0 验证**：`dotnet build src/il2cppc`
6. **单文件 g++ 验证**
7. **r78 全量生成 + 三片扫描**
