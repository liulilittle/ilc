# r77 DateTimeOffset 错误模式分析

**日期**: 2026-09-06
**数据源**: r77 三片错误日志
**总 DateTimeOffset 相关错误**: **232**

---

## 1. 错误模式统计

### 1.1 按文件分布

| 文件 | 错误数 | 说明 |
|------|--------|------|
| `System_Collections_Generic_EqualityComparer_1_System_DateTimeOffset.cpp` | ~24 | EqualityComparer 的 Equals/GetHashCode 类型不匹配 |
| `System_Collections_Generic_List_1_Enumerator_System_DateTimeOffset.cpp` | ~6 | Enumerator 的 current 字段赋值类型不匹配 |
| `System_Collections_Generic_List_1_System_DateTimeOffset.cpp` | ~12 | List<T> 的返回值、newarr 类型缺失 |
| `System_Collections_Generic_List_1_SynchronizedList_System_DateTimeOffset.cpp` | ~14 | SynchronizedList 方法参数类型不匹配 |
| `System_Array_FunctorComparer_1_System_DateTimeOffset_.cpp` | ~1 | 未声明标识符 |
| `System_Array_FunctorComparer_1_System_DateTimeOffset__.cpp` | ~1 | 未声明标识符 |
| `System_Array_FixedSizeComparer_1_System_DateTimeOffset_.cpp` | ~1 | 未声明标识符 |
| `System_Collections_Generic_ArraySort____DebugViewer_System_DateTimeOffset.cpp` | ~1 | 未声明标识符 |
| `System_Collections_Generic_Dictionary_2_System_DateTimeOffset_System_Object.cpp` | ~1 | 未声明标识符 |
| `System_Collections_Generic_Dictionary_2_object_System_DateTimeOffset.cpp` | ~1 | 未声明标识符 |
| `System_Collections_Generic_List_1_SynchronizedList____DebugViewer_System_DateTimeOffset.cpp` | ~1 | 未声明标识符 |
| `System_DateTimeOffset.cpp` | ~1 | 未声明标识符 |

### 1.2 按错误类型分组

| 错误类型 | 计数 | 典型模式 |
|----------|------|----------|
| **unknown type name 'V_System_DateTimeOffset'** | ~47 | 闭包泛型展开中 `V_DateTimeOffset` 未定义 |
| **unknown type name 'dt'/'ts'/'dto'** | ~19 | 局部变量类型缩写未展开 |
| **undeclared identifier 'V_System_DateTimeOffset_class'** | ~6 | newarr 需要的类描述符未声明 |
| **Il2CppDateTimeOffset → Il2CppObject*** | ~6 | list_obj_contains/remove 返回值类型不匹配 |
| **Il2CppDateTimeOffset → int32_t** | ~4 | 值类型→int 转换失败 |
| **int32_t → Il2CppDateTimeOffset** | ~3 | 数组元素读取返回类型不匹配 |
| **TaskAwaiter_1_int → Il2CppDateTimeOffset** | ~2 | 异步尾调用类型不匹配 |
| **Il2CppTimeSpan → Il2CppDateTime** | ~2 | 时间类型间赋值不匹配 |
| **Il2CppObject* → Il2CppDateTimeOffset** | ~1 | 返回值类型不匹配 |
| **指针类型不匹配** | ~1 | reinterpret_cast 指针层级不匹配 |
| **其他** | ~12 | 未声明标识符、FunctorComparer 等 |

---

## 2. 根因分析

### 2.1 核心问题：闭包泛型展开中的 V_ 前缀缺失

错误日志中大量出现 `unknown type name 'V_System_DateTimeOffset'`，这是泛型闭包展开的命名问题。

**典型场景**：
```cpp
// 错误代码
arg0->current = reinterpret_cast<V_System_DateTimeOffset*>((*reinterpret_cast<int32_t*>(...)));
// 应为
arg0->current = reinterpret_cast<System_DateTimeOffset*>((*reinterpret_cast<int32_t*>(...)));
```

**V_ 前缀** 通常表示泛型闭包中的类型参数。在 `EqualityComparer<System.DateTimeOffset>` 的闭包展开中，`V_` 前缀未被正确替换为实际类型名 `System_DateTimeOffset`。

### 2.2 V_System_DateTimeOffset_class 未声明

```cpp
// 错误
t0 = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(&V_System_DateTimeOffset_class, 8, true, ...));
// 应为
t0 = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new(&System_DateTimeOffset_class, 8, true, ...));
```

这是 `newarr` 指令的元素类型描述符生成错误：`ArrayElementClassCpp` 对 `DateTimeOffset` 泛型实例返回了 `V_System_DateTimeOffset_class` 而非 `System_DateTimeOffset_class`。

### 2.3 值类型 struct 无隐式转换

```cpp
// 错误
il2cpp::runtime::Il2CppDateTimeOffset loc2 = ...;
loc2 = il2cpp::runtime::icall_list_obj_contains(..., arg1);  // 返回 Il2CppObject*
```

`Il2CppDateTimeOffset` 是 struct 类型，但没有定义从 `Il2CppObject*` 或 `int32_t` 的隐式转换运算符。

### 2.4 局部变量类型缩写未展开

```cpp
// 错误
dto t3;  // 'dto' 未定义
dt t0;   // 'dt' 未定义
ts t1;   // 'ts' 未定义
```

这些是 `TypeCppName` 或 `TypeKindResolver` 的缩写生成逻辑缺陷：`DateTimeOffset` → `dto`、`DateTime` → `dt`、`TimeSpan` → `ts` 未被正确展开为完整类型名。

---

## 3. 与 cannot-init 的关系

DateTimeOffset 错误与 cannot-init 共享根因：

| 问题 | cannot-init | DateTimeOffset |
|------|-------------|----------------|
| ref-val mismatch | ✅ Il2CppObject* → int32_t | ✅ Il2CppObject* → Il2CppDateTimeOffset |
| 值类型 struct 转换 | ✅ 部分覆盖 | ❌ 无隐式转换 |
| 闭包泛型 V_ 前缀 | — | ✅ 主要问题 |
| 局部变量缩写 | — | ✅ dt/ts/dto 未展开 |

---

## 4. 修复优先级建议

| 优先级 | 问题 | 影响行数 | 修复策略 |
|--------|------|----------|----------|
| **P0** | `V_System_DateTimeOffset` 未定义 | ~47 | 修复闭包泛型展开中的 V_ 前缀替换逻辑 |
| **P0** | `V_System_DateTimeOffset_class` 未声明 | ~6 | 修复 `ArrayElementClassCpp` 对 DateTimeOffset 的处理 |
| **P1** | `dt`/`ts`/`dto` 局部变量类型未展开 | ~19 | 修复 TypeKindResolver 或 TypeCppName 的缩写展开 |
| **P1** | Il2CppDateTimeOffset ↔ Il2CppObject* 转换 | ~7 | 添加 reinterpret_cast 桥接 |
| **P2** | int32_t ↔ Il2CppDateTimeOffset 转换 | ~7 | 添加值类型转换运算符或 reinterpret_cast |
| **P3** | TaskAwaiter_1_int → Il2CppDateTimeOffset | ~2 | 异步尾调用特殊处理 |

---

## 5. 修复方案草案

### 5.1 V_ 前缀修复

在 `MetadataLoader` 或 `TypeEmitter` 的闭包类型展开逻辑中，确保 `V_` 前缀被替换为实际类型名：

```csharp
// 当前（错误）
var closureName = $"V_{typeCppName}";

// 修复后
var closureName = typeCppName;  // 去掉 V_ 前缀
```

### 5.2 ArrayElementClassCpp 修复

在 `CodeGenerator.ArrayElementClassCpp` 中，对泛型实例使用实际类名而非 V_ 前缀：

```csharp
private static string ArrayElementClassCpp(string typeName) =>
    typeName.StartsWith("V_") ? typeName[2..] + "_class" : typeName + "_class";
```

### 5.3 值类型转换桥接

在 `CodeGenerator` 的 `EmitExpr` 中，对 `Il2CppDateTimeOffset`/`Il2CppDateTime`/`Il2CppTimeSpan` 等值类型，添加从 `Il2CppObject*` 和 `int32_t` 的 `reinterpret_cast` 桥接。

---

## 附录：证据清单

| 证据 | 来源 |
|------|------|
| 总错误数 232 | [BASH:Select-String -Pattern "Il2CppDateTimeOffset\|DateTimeOffset" -Context 0,0 \| Where error:] |
| V_System_DateTimeOffset 未定义样本 | [读:r77shard/r77shard1_shard1_errors.log:18405-18412] |
| V_System_DateTimeOffset_class 未声明样本 | [读:r77shard/r77shard1_shard1_errors.log:24314-24319] |
| dt/ts/dto 局部变量未展开样本 | [读:r77shard/r77shard1_shard1_errors.log:21219-21227] |
| Il2CppDateTimeOffset → Il2CppObject* 转换失败样本 | [读:r77shard/r77shard1_shard1_errors.log:22773-22792] |
| EqualityComparer_1_System_DateTimeOffset 文件 | [读:r77shard/r77shard1_shard1_errors.log:18405-18466] |
| List_1_Enumerator_DateTimeOffset 文件 | [读:r77shard/r77shard1_shard1_errors.log:21219-21238] |
| List_1_DateTimeOffset 文件 | [读:r77shard/r77shard1_shard1_errors.log:24314-24345] |
| il2cpp_dtoffset.h 定义 | [读:r77shard/r77shard1_shard1_errors.log:18408-18409] |
