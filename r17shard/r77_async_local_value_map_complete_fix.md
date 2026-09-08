# r77 AsyncLocalValueMap 错误修复分析

## 问题概述

r77 转译 mscorlib 中，`System.Threading.AsyncLocalValueMap` 的嵌套类型存在 **172 行错误**，影响 3 个文件：
- `System_Threading_AsyncLocalValueMap_ManyElementAsyncLocalValueMap.cpp`
- `System_Threading_AsyncLocalValueMap_MultiElementAsyncLocalValueMap.cpp`
- `System_Threading_AsyncLocalValueMap_ThreeElementAsyncLocalValueMap.cpp`

## 根因分析

### 缺失的泛型实例化

转译器未生成 `KeyValuePair<System.Threading.IAsyncLocal, object>` 的具体实例化，导致：

1. **未声明标识符**：`System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object_class`
2. **构造函数不匹配**：尝试调用 `KeyValuePair_2_object_object__ctor` 但类型不兼容
3. **类型转换错误**：`Il2CppObject*` 与 `int32_t` 之间的非法 cast

### 错误模式详解

#### 模式 1：KeyValuePair 缺失（核心问题）
```
error: use of undeclared identifier 'System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object_class'
```
- 调用点：`il2cpp_array_new(&System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object_class, ...)`
- 调用点：`il2cpp_object_new(&System_Collections_Generic_KeyValuePair_2_int_class)` — 错误地使用了 int 版本

#### 模式 2：构造函数签名不匹配
```
error: no matching function for call to 'System_Collections_Generic_KeyValuePair_2_object_object__ctor_fe5b561b5331a819'
```
- 期望：`KeyValuePair<IAsyncLocal, object>`
- 实际生成：`KeyValuePair<object, object>` — 签名不匹配

#### 模式 3：static_cast Il2CppObject* → int32_t
```
error: static_cast from 'Il2CppObject *' to 'int32_t' (aka 'int') is not allowed
```
- 代码：`(*reinterpret_cast<int32_t*>(...)) = static_cast<int32_t>(t0)`
- 原因：`t0` 是 `Il2CppObject*`（KeyValuePair 实例），不能 static_cast 为 int32_t

#### 模式 4：reinterpret_cast int32_t → intptr_t
```
error: reinterpret_cast from 'int32_t' (aka 'int') to 'intptr_t' (aka 'long long') is not allowed
```
- 代码：`static_cast<int32_t>(reinterpret_cast<intptr_t>(...))`
- 原因：中间层 `reinterpret_cast<intptr_t>` 不合法

#### 模式 5：cannot cast int32_t → Il2CppObject*
```
error: cannot cast from type 'int32_t' (aka 'int') to pointer type 'Il2CppObject *'
```
- 代码：`static_cast<Il2CppObject*>(inl6)`
- 原因：`inl6` 是 `int32_t` 类型局部变量，不能转为指针

## 修复方案

### 方案 A：生成 KeyValuePair<IAsyncLocal, object> 实例化（推荐）

在 `System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object.cpp` 中生成：

1. **Struct 定义**：
   ```cpp
   struct System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object {
       Il2CppObject _obj;           // 对象头
       Il2CppObject* key;           // System.Threading.IAsyncLocal (ref field)
       Il2CppObject* value;         // System.Object (ref field)
   };
   ```

2. **Descriptor**：
   ```cpp
   Il2CppClass System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object_class = {
       "System.Collections.Generic.KeyValuePair`2",
       sizeof(System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object),
       0b110ULL,  // refFieldBitmap: key, value 是引用
       2,         // refFieldCount
       ...
   };
   ```

3. **方法**：
   - `.ctor(IAsyncLocal, object)`
   - `get_Key()`
   - `get_Value()`

### 方案 B：修改调用点（不推荐）

将所有 `KeyValuePair<IAsyncLocal, object>` 调用改为使用 `KeyValuePair<object, object>`。

**问题**：`IAsyncLocal` 是接口类型，实际传入的是接口指针，与 `object` 类型布局相同（都是 `Il2CppObject*`），但类型签名不同，会导致：
- `il2cpp_array_new` 使用错误的 class 描述符
- 运行时类型检查失败

## 实施步骤

1. 在 `CodeGenerator.cs` 的泛型实例化 pass 中，确保 `KeyValuePair<T1, T2>` 对 **所有** 使用到的类型参数组合都生成实例化
2. 特别检查 `KeyValuePair<System.Threading.IAsyncLocal, object>` 是否被正确识别为需要生成的实例化
3. 重新生成 mscorlib，验证 0 error

## 验证

```bash
dotnet build src/il2cppc
# 重新生成 mscorlib
# 检查 System_Collections_Generic_KeyValuePair_2_System_Threading_IAsyncLocal_object.cpp 存在
# 检查 0 cannot-cast 错误
```

## 引用证据

- [READ:E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_AsyncLocalValueMap_ManyElementAsyncLocalValueMap.cpp:194] — 缺失 KeyValuePair 引用
- [READ:E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_AsyncLocalValueMap_MultiElementAsyncLocalValueMap.cpp:131] — static_cast 错误
- [READ:E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_AsyncLocalValueMap_ThreeElementAsyncLocalValueMap.cpp:277] — 构造函数不匹配
- [READ:E:\il2cpp\r77shard\r77shard3_shard3_errors.log:17386-17514] — 完整错误日志
