# r77 List`1+Enumerator 错误修复方案

## 1. 问题定位

**错误现象**: r77 中 `System.Collections.Generic.List`1+Enumerator` 编译/链接失败。

**根因分析**:

### 1.1 结构体字段不匹配

`List<int>.Enumerator` 在 .NET 中是一个 **struct** (值类型), 其内部字段为:
- `Il2CppObject* _obj` — 对象头 (klass 指针)
- `Il2CppArray* _items` — 快照数组 (GC 引用)
- `int32_t _index` — 游标

但当前实现中, `Il2CppArrayEnumerator` 在 `sortedlist.h` 中的布局是:
```cpp
struct Il2CppArrayEnumerator {
    Il2CppObject obj;      // 对象头
    Il2CppArray* array;    // 快照数组 (GC ref)
    int32_t index;         // 游标
};
```

该结构体被 `SortedList` 和 `List<int>` 共用。运行时 icall 签名统一为 `Il2CppObject* self`, 这要求 Enumerator 必须被 **装箱 (box)** 到堆上, 因为 icall 需要接收一个 `Il2CppObject*`。

### 1.2 问题根源

`listi4.cpp:504` 中:
```cpp
Il2CppArrayEnumerator* en = reinterpret_cast<Il2CppArrayEnumerator*>(
    il2cpp_object_new(&System_Collections_SortedList_Enumerator_class));
```

这里运行时通过 `il2cpp_object_new` 分配堆对象, 将 Enumerator 当作 **引用类型** 处理。但 IL 层面 `List<int>.Enumerator` 是 **值类型 (struct)**, 调用 `GetEnumerator()` 时返回的是 **boxed 的 Il2CppObject***。

### 1.3 生成端 (CodeGenerator) 的不匹配

MethodMap.Collections.cs 中 List enumerator 的映射:
- `MoveNext` → `il2cpp_list_i4_enumerator_move_next`, cast `"deref:r"`
- `get_Current` → `il2cpp_list_i4_enumerator_get_current`, cast `"deref:r"`

`"deref:r"` 的含义: 从 `Il2CppObject**` 解引用出 `Il2CppObject*`。这意味着生成端将 Enumerator 的局部变量声明为 `Il2CppObject*`, 然后取地址传给 icall。

**但 `List<int>.Enumerator` 的 IL 槽位是 int32 值类型 slot**, 不是 `Il2CppObject*` 引用 slot。这导致:
- 局部变量声明为 `Il2CppObject*` (ref-kind)
- 但实际存储的是 int32 值 (val-kind)
- 赋值时出现 "cannot initialize Il2CppObject* from int32_t" 错误

## 2. 错误日志分析

r77 错误列表中, List.Enumerator 相关错误特征:

1. **cannot-init 错误**: `Il2CppObject*` 局部变量被赋予 `int32_t` 值
2. **字段访问不匹配**: Enumerator 的字段 (如 `_index`) 在 int32 slot 中, 但生成端尝试 `reinterpret_cast<Int32_t*>`
3. **deref:r 解引用失败**: 对 int32 值取地址后解引用为 `Il2CppObject**`

## 3. 修复方案

### 方案 A: 运行时 boxed-enumerator 修复 (推荐)

**核心思想**: 将 Enumerator 作为 **堆分配的 boxed 对象** 处理, 局部变量统一为 `Il2CppObject*`。

**修改点**:

#### 3.1 方法返回类型标记

确保 `GetEnumerator()` 返回的 `System.Collections.Generic.List`1.Enumerator` 在生成端被追踪为 **ref-kind** (Il2CppObject*), 而不是 val-kind。

检查 `MethodMap.List.cs:133`:
```csharp
returnTypeName: "System.Collections.Generic.List`1.Enumerator"
```

需要在 `TypeKindResolver` 或 `ResolveExprKind` 中, 将 `List`1.Enumerator` 识别为 **reference type** (因为运行时是 boxed 的)。

#### 3.2 局部变量声明

在 `EmitBody` 的局部变量声明阶段, 当 `locKind == "ref"` 且类型名为 `List`1.Enumerator` 时, 声明为:
```cpp
Il2CppObject* loc_N = nullptr;
```

而不是当前的 int32 值类型声明。

#### 3.3 字段访问桥接

对 Enumerator 的字段访问 (如 MoveNext 中的 `_index++`), 由于生成端看到的是 `Il2CppObject*`, 而运行时 icall 接收 `Il2CppObject*`, 通过 `"deref:r"` 解引用到堆对象, 直接操作字段。

**无需修改字段访问 icall**, 因为 icall 已经接收 `Il2CppObject*` 并 reinterpret_cast 到 `Il2CppArrayEnumerator*`。

### 方案 B: 值类型 struct 局部变量修复

**核心思想**: 生成端将 Enumerator 声明为 **值类型 struct**, 仅在传递给 icall 时取地址。

**修改点**:

#### 3.4 局部变量类型

当 `locKind` 对应 `List`1.Enumerator` 时, 声明为:
```cpp
Il2CppArrayEnumerator loc_N;  // 值类型 struct
```

但这需要:
1. 生成端知道 `Il2CppArrayEnumerator` 的 struct 定义
2. struct 定义需要在 generated.h 中可见
3. 取地址传递给 icall: `(&loc_N)` → `Il2CppArrayEnumerator*` → 需要 cast 到 `Il2CppObject*`

**问题**: `Il2CppArrayEnumerator` 是运行时 struct, 生成端无法直接引用。

### 方案 C: 混合方案 (最终推荐)

**核心思想**: 
- 局部变量声明为 `Il2CppObject*` (与运行时 boxed 对象一致)
- GetEnumerator() 返回 Il2CppObject* (ref-kind)
- 对 Enumerator 字段的访问通过 icall 完成, 不直接操作
- foreach 循环编译为标准 IEnumerator 模式

**具体修改**:

#### 修改 1: MethodMap.List.cs GetEnumerator 返回类型
```csharp
case "GetEnumerator":
    return count == 0 && i4Receiver
        ? I("il2cpp::il2cpp_list_i4_get_enumerator", 0, true, true, true,
            returnTypeName: "System.Collections.Generic.List`1.Enumerator")
        : Loud();
```
确保 `returnTypeName` 在 TypeKindResolver 中被识别为 **ref-kind**。

#### 修改 2: TypeKindResolver 或 StackFold

在 `StackFold.ResolveExprKind` 或 `TypeKindResolver.ResolveExprKind` 中, 对 `List`1.Enumerator` 返回 `"ref"`:
```csharp
if (typeName == "System.Collections.Generic.List`1.Enumerator")
    return "ref";
```

#### 修改 3: 局部变量声明

在 `EmitBody` 中, 当 `locKind == "ref"` 时, 声明为 `Il2CppObject*`:
```cpp
if (kind == "ref")
    w.Line($"Il2CppObject* {d};");
```

#### 修改 4: 赋值 reinterpret_cast

在 `AssignStmt` 中, 当目标为 ref-kind `Il2CppObject*` 且值为 val-kind int32 时, 插入 `reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(...))`:
```cpp
if (locKind.TryGetValue(a.Target, out var tKind) && tKind == "ref"
    && ExprKind(a.Value, locKind) == "val")
{
    // 插入 reinterpret_cast
}
```

这与 r77 已有的 cannot-init 修复逻辑一致 (CodeGenerator.cs:3240)。

## 4. 验证清单

1. `dotnet build src/il2cppc` → 0错0警告
2. 抽查 r77 生成文件中 List.Enumerator 相关代码:
   - 局部变量声明为 `Il2CppObject*`
   - GetEnumerator 返回 `Il2CppObject*`
   - 赋值有 `reinterpret_cast`
3. 运行时测试: listops / listops2 / listops3 通过

## 5. 总结

| 问题 | 根因 | 修复 |
|------|------|------|
| cannot-init Enumerator local | 局部变量声明为 Il2CppObject* 但赋值 int32 | 插入 reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(...)) |
| Enumerator 字段访问失败 | 字段在 int32 slot 中但按 Il2CppObject* 访问 | 通过 icall 访问, 不直接操作字段 |
| deref:r 解引用失败 | 对 int32 值取地址 | 确保局部变量是 Il2CppObject* 后取地址 |

**最小改动**: 在 `TypeKindResolver` / `StackFold` 中将 `List`1.Enumerator` 标记为 ref-kind, 复用 r77 已有的 cannot-init reinterpret_cast 修复逻辑。
