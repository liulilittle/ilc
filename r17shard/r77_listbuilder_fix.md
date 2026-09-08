# r77 ListBuilder`1 错误修复分析

## 问题概述

r77 转译 mscorlib 中，`System.RuntimeType+ListBuilder`1<T>` 的实例化类型缺失，导致 **1277 行错误日志**，影响 2 个文件：
- `System_RuntimeType.cpp`
- `System_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int.cpp`

## 根因分析

### 已生成实例
转译器仅生成了 **int32 实例化**：
- `System_RuntimeType_ListBuilder_1_int` — 完整生成（struct + descriptor + typeinfo + 6 方法）
- 文件：`System_RuntimeType_ListBuilder_1_int.cpp`

### 缺失实例
调用点引用了 **object 实例化** `ListBuilder_1_object`，但转译器未生成：

| 缺失符号 | 调用方法 |
|---|---|
| `__ctor_d2a6d11365a3a733` | 构造函数 |
| `_Add_c10f6a0e5679f7f2` | Add(v(i)) |
| `_ToArray_ce459a06e5466806` | ToArray() |
| `_CopyTo_cd64f8a5d3ac9047` | CopyTo(v(r:System.Object[],i)) |
| `_get_Count_46255826a76b6404` | get_Count() |
| `_get_Item_ba00cde5e855f137` | get_Item(i) |

### 错误模式

**模式 1：未声明标识符 + 提示 int 版本**
```
error: use of undeclared identifier 'System_RuntimeType_ListBuilder_1_object_Add_c10f6a0e5679f7f2';
  did you mean 'System_RuntimeType_ListBuilder_1_int_Add_c10f6a0e5679f7f2'?
```

**模式 2：参数类型不匹配**
```
error: cannot initialize a parameter of type 'System_RuntimeType_ListBuilder_1_int *'
  with an rvalue of type 'Il2CppObject **'
```

## 修复方案

### 方案 A：生成缺失的 object 实例化（推荐）

在 `System_RuntimeType_ListBuilder_1_int.cpp` 旁，新增 `System_RuntimeType_ListBuilder_1_object.cpp`，生成：

1. **Struct 定义**：
   ```cpp
   struct System_RuntimeType_ListBuilder_1_object {
       Il2CppObject _obj;        // 对象头
       Il2CppObject* _items;     // System.Array (ref field)
       Il2CppObject* _item;      // System.Object (ref field)
       int32_t _count;
       int32_t _capacity;
   };
   ```

2. **Descriptor**：
   ```cpp
   Il2CppClass System_RuntimeType_ListBuilder_1_object_class = {
       "System.RuntimeType+ListBuilder`1",
       sizeof(System_RuntimeType_ListBuilder_1_object),
       0b01110ULL,  // refFieldBitmap: _items, _item 是引用
       2,           // refFieldCount
       &System_ValueType_class,
       ...
   };
   ```

3. **6 个方法**：
   - `__ctor` — 构造函数
   - `Add` — 添加元素
   - `ToArray` — 转数组
   - `CopyTo` — 拷贝到数组
   - `get_Count` — 获取计数
   - `get_Item` — 索引器

4. **TypeInfo 表**：`System_RuntimeType_ListBuilder_1_object_typeinfo`

### 方案 B：修改调用点指向 int 实例化（不推荐）

将 `System_RuntimeType.cpp` 和 `MemberInfoCache_1_int.cpp` 中所有 `ListBuilder_1_object_*` 调用替换为 `ListBuilder_1_int_*`。

**问题**：`ListBuilder`1<object>` 与 `ListBuilder`1<int>` 的 struct 布局不同（引用字段 vs 值字段），不能混用。此方案会导致运行时内存错误。

## 实施步骤

1. 在 `CodeGenerator.cs` 的泛型实例化 pass 中，确保 `ListBuilder`1<T>` 对 **所有** 使用到的类型参数都生成实例化（不仅是 int，还有 object）
2. 或在 `MetadataLoader` 中显式添加 `ListBuilder`1<object>` 到类型列表
3. 重新生成 mscorlib，验证 0 error

## 验证

```bash
dotnet build src/il2cppc
# 重新生成 mscorlib
# 检查 System_RuntimeType_ListBuilder_1_object.cpp 存在
# 检查 0 cannot-init 错误
```

## 引用证据

- [READ:E:\il2cpp\out\mscorlib_gen_r77\src\System_RuntimeType_ListBuilder_1_int.cpp:1-80] — 已生成 int 实例化
- [READ:E:\il2cpp\out\mscorlib_gen_r77\src\System_RuntimeType.cpp:2304-2315] — 调用点引用 object 实例化
- [READ:E:\il2cpp\r77shard\r77shard3_shard3_errors.log:3597-3700] — 错误日志样本
