# r77 ConcurrentQueue`1 错误修复方案

**日期**: 2026-09-06  
**来源**: r77 shard 1/2/3 错误日志  
**总错误数**: 93 个错误块  
**受影响文件**: 7 个 .cpp 文件

---

## 一、错误统计

### 1.1 按错误类型分组

| 错误类型 | 次数 | 说明 |
|----------|------|------|
| `cannot cast from 'int32_t' to pointer type 'Il2CppObject *'` | 16 | int→指针类型转换失败 |
| `no member named 'm_array' in '__GetEnumerator_d__27_int'` | 8 | 字段名不匹配 |
| `no viable overloaded '='` | 8 | 赋值运算符不匹配 |
| `no member named 'm_index' in 'ConcurrentQueue_1_int'` | 7 | 字段名不匹配 |
| `no member named 'm_state' in '__GetEnumerator_d__27_int'` | 7 | 字段名不匹配 |
| `no matching function for call to 'Enqueue_...'` | 5 | 方法签名不匹配 |
| `no member named 'm_head'/'m_tail' in 'ConcurrentQueue_1_int'` | 4 | 字段名不匹配 |
| `no member named 'head'/'tail' in 'ConcurrentQueue_1_int'` | 4 | 字段名不匹配 |
| `cannot initialize 'Il2CppObject *' with 'int32_t'` | 4 | 类型不匹配 |
| `no member named '__4__this' in 'ConcurrentQueue_1_int'` | 4 | 字段名不匹配 |
| `no member named 'm_tail' in 'Segment_int'` | 4 | 字段名不匹配 |
| `static_cast from 'Il2CppObject *' to 'int32_t' is not allowed` | 2 | 指针→int 转换失败 |
| `cannot convert 'Il2CppObject' to 'int32_t'` | 2 | 类型转换失败 |
| `incompatible pointer to integer conversion` | 2 | 指针→整数转换失败 |
| `no member named 'm_numSnapshotTakers'` | 2 | 字段名不匹配 |

### 1.2 按文件分组

| 文件 | 错误数 | 说明 |
|------|--------|------|
| `ConcurrentQueue_1_int.cpp` | 12 | 主队列操作 |
| `ConcurrentQueue_1__GetEnumerator_d__27_int.cpp` | 9 | 枚举器状态机 |
| `ConcurrentQueue_1__GetEnumerator_d__27_object.cpp` | 8 | 枚举器状态机(object) |
| `ConcurrentQueue_1_Segment_int.cpp` | 8 | 段结构 |
| `ConcurrentQueue_1_Segment_object.cpp` | 5 | 段结构(object) |
| `System_Threading_Tasks_Parallel.cpp` | 2 | Parallel 使用 |
| `System_Threading_Tasks_Parallel___c__DisplayClass4_0.cpp` | 1 | 闭包类 |

---

## 二、根本原因分析

### 2.1 字段名不匹配问题

#### 问题描述

`ConcurrentQueue<int>` 的 C++ 结构体定义为：

```csharp
struct System_Collections_Concurrent_ConcurrentQueue_1_int
{
    Il2CppObject _obj;           // offset 0
    Il2CppObject* m_head;        // offset 8
    Il2CppObject* m_tail;        // offset 16
    Il2CppObject* m_serializationArray; // offset 24
    int32_t m_numSnapshotTakers; // offset 32
};
```

`ConcurrentQueue<int>+<GetEnumerator>d__27` 的 C++ 结构体定义为：

```csharp
struct System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int
{
    Il2CppObject _obj;           // offset 0
    int32_t __1__state;          // offset 8
    int32_t __2__current;        // offset 16
    Il2CppObject* head;          // offset 24 (无 m_ 前缀!)
    Il2CppObject* tail;          // offset 32 (无 m_ 前缀!)
    int32_t headLow;             // offset 40
    int32_t tailHigh;            // offset 48
    Il2CppObject* __4__this;     // offset 56
    System_Threading_SpinWait _spin_5__2; // offset 64
    int32_t _i_5__3;             // offset 72
    Il2CppObject* _curr_5__4;    // offset 80
};
```

**根本原因**: 生成的代码在访问 `GetEnumerator_d__27_int` 时使用了 `->m_array` 和 `->m_state`，但这两个字段属于 `Segment_int` 结构，不属于 `GetEnumerator_d__27_int`。

#### 触发场景

```cpp
// 错误代码 (GetEnumerator_d__27_int.cpp:196)
((reinterpret_cast<...__GetEnumerator_d__27_int*>(...))->__2__current = 
    (*reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(
        reinterpret_cast<Il2CppArray*>(reinterpret_cast<Il2CppObject*>(
            ((reinterpret_cast<...__GetEnumerator_d__27_int*>(...))->head))))->m_array, ...));
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//  head 是 Il2CppObject*，指向 Segment 对象
// 但代码直接在 head 上访问 ->m_array，而 head 的编译时类型是 Il2CppObject*
// 需要: ((Segment_int*)head)->m_array
```

### 2.2 int→指针转换问题

#### 问题描述

IL 代码中存在 `conv.i4` → `stloc` → `ldloc` → `callvirt` 的栈模式，其中 `int32_t` 值被直接传递给期望 `Il2CppObject*` 的参数。

#### 触发场景

```cpp
// 错误代码: cannot cast from 'int32_t' to pointer type 'Il2CppObject *'
loc0 = 5;  // loc0 被声明为 Il2CppObject*
```

### 2.3 方法签名不匹配

#### 问题描述

`ConcurrentQueue_1_int_Enqueue_06955c55bfff106b` 的声明与调用不匹配：

```
// 声明 (generated.h:70090)
void System_Collections_Concurrent_ConcurrentQueue_1_int_Enqueue_06955c55bfff106b(
    System_Collections_Concurrent_ConcurrentQueue_1_int* arg0, 
    int32_t arg1);

// 调用 (错误代码)
System_Collections_Concurrent_ConcurrentQueue_1_int_Enqueue_06955c55bfff106b(
    loc0, loc1);  // loc0 是 Il2CppObject* 而非 ConcurrentQueue_1_int*
```

---

## 三、修复方案

### 修复 A: 字段名映射层 (TypeEmitter 修复)

**触发条件**: 当 IL 字段名与 C++ 结构体成员名不一致时。

**修复位置**: `TypeEmitter.EmitClassStruct` 或 `CodeGenerator.FieldAccess` 方法。

```csharp
// 修复代码片段 (添加到 FieldAccess 方法前)
private static string MapFieldName(string ilFieldName, string structCppName)
{
    // ConcurrentQueue<int>: IL head -> C++ m_head
    if (structCppName == "System_Collections_Concurrent_ConcurrentQueue_1_int")
    {
        return ilFieldName switch
        {
            "head" => "m_head",
            "tail" => "m_tail",
            "m_array" => "m_serializationArray",
            _ => ilFieldName
        };
    }
    
    // Segment<int>: IL m_array -> C++ m_array (无需映射)
    // GetEnumerator<d__27>: IL head -> C++ head (无需映射)
    
    return ilFieldName;
}
```

**预期效果**: 解决 30+ 个 `no member named` 错误。

---

### 修复 B: int→pointer reinterpret_cast 桥接

**触发条件**: 当 `int32_t` 值需要转换为 `Il2CppObject*` 时。

**修复位置**: `CodeGenerator.EmitStmt` → `case AssignStmt a:` 块。

```csharp
// 修复代码片段 (在 AssignStmt 处理中添加)
if (locKind.TryGetValue(a.Target, out var tKind) && tKind == "ref"
    && ExprKind(a.Value, locKind) == "val")
{
    var valCode = EmitExpr(a.Value, md, types);
    w.LineAligned($"{EmitTarget(a.Target)} = reinterpret_cast<Il2CppObject*>({valCode});",
        $"IL {At(a.SourceOpName, a.SourceOffset)} -- store int32 value as Il2CppObject* (reinterpret_cast for cannot-init)");
    break;
}
```

**预期效果**: 解决 16 个 `cannot cast from 'int32_t' to pointer` 错误。

---

### 修复 C: 方法签名对齐 (MethodRefResolver 修复)

**触发条件**: 当方法调用的参数类型与声明不匹配时。

**修复位置**: `MethodRefResolver.Resolve` 方法或 `EmitIcallCallArgs` 方法。

```csharp
// 修复代码片段 (在 EmitIcallCallArgs 中添加类型桥接)
if (cast == null || cast.Length == 0)
{
    // 检查是否需要 int→pointer 桥接
    if (formal == "Il2CppObject*" && actual is Ir.LocExpr leActual 
        && _curLocKinds.TryGetValue(leActual.Name, out var kActual) 
        && (kActual == "val" || kActual == "i"))
    {
        parts.Add($"reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>({code}))");
        continue;
    }
    
    // 检查是否需要 pointer→int 桥接
    if (formal == "int32_t" && actual is Ir.LocExpr leActual2 
        && _curLocKinds.TryGetValue(leActual2.Name, out var kActual2) 
        && kActual2 == "ref")
    {
        parts.Add($"reinterpret_cast<int32_t>(reinterpret_cast<intptr_t>({code}))");
        continue;
    }
    
    parts.Add(code);
    continue;
}
```

**预期效果**: 解决 5 个 `no matching function for call` 错误。

---

### 修复 D: 枚举器字段访问修复 (GetEnumerator 专用)

**触发条件**: 当枚举器代码访问 `Segment` 字段时，`head` 字段的编译时类型为 `Il2CppObject*`。

**修复位置**: `CodeGenerator.EmitExpr` → `case FieldExpr f:` 或 `FieldAccess` 方法。

```csharp
// 修复代码片段: 当访问 head/tail 的字段时，需要先将 Il2CppObject* 转换为 Segment*
// 在 FieldAccess 方法中添加:
if (owner == "System_Collections_Concurrent_ConcurrentQueue_1_Segment_int" 
    || owner == "System_Collections_Concurrent_ConcurrentQueue_1_Segment_object")
{
    // 如果 obj 是 Il2CppObject* (来自 head/tail 字段)，需要 reinterpret_cast
    if (obj is LocExpr le && le.Name == "head" || le.Name == "tail" || le.Name == "_curr_5__4")
    {
        objCode = $"(reinterpret_cast<{TypeCppName(owner)}*>({objCode}))";
    }
}
```

**预期效果**: 解决 8 个 `no member named 'm_array'` 和 7 个 `no member named 'm_state'` 错误。

---

## 四、修复优先级

| 优先级 | 修复 | 预计修复错误数 | 复杂度 |
|--------|------|---------------|--------|
| 1 | B: int→pointer reinterpret_cast | 16 | 低 |
| 2 | A: 字段名映射层 | 30 | 中 |
| 3 | D: 枚举器字段访问修复 | 15 | 中 |
| 4 | C: 方法签名对齐 | 5 | 中 |

**总计预计修复**: ~66 个错误 (占 ConcurrentQueue 总错误的 ~71%)

---

## 五、未修复错误

以下错误需要更深入的分析或运行时支持：

- `no viable overloaded '='` (8个) — 可能是结构体赋值运算符问题
- `cannot convert 'Il2CppObject' to 'int32_t'` (2个) — 需要 unbox icall
- `incompatible pointer to integer conversion` (2个) — 需要 reinterpret_cast

---

## 六、总结

`ConcurrentQueue<int>` 的错误主要由三个因素导致：

1. **字段名映射缺失**: IL 中的 `head`/`tail` 字段在 C++ 中映射为 `m_head`/`m_tail`，但生成代码未做转换
2. **类型桥接缺失**: `int32_t` 与 `Il2CppObject*` 之间的转换需要 `reinterpret_cast`
3. **枚举器状态机复杂性**: `GetEnumerator<d__27>` 访问 `Segment` 字段时需要额外的指针转换

修复方案 A-D 覆盖了大多数错误模式，建议在 `CodeGenerator.cs` 的 `FieldAccess` 和 `EmitStmt` 方法中实施。
