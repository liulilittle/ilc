# r77 DynamicMethod 错误完整修复方案

**分析范围**: r77 shard2 错误日志 (11 条 DynamicMethod 相关错误)
**分析日期**: 2026-09-06
**生成文件**: `System_Reflection_Emit_DynamicMethod.cpp`

---

## 1. 错误清单 (11 条，全部位于 shard2)

| # | 行号 | 错误描述 | 方法 |
|---|------|----------|------|
| 1 | 761:16 | incompatible pointer to integer conversion assigning to int32_t from Il2CppObject* | GetDynamicMethodsModule |
| 2 | 767:9 | no matching function for call to InitCustomAttributeBuilder | GetDynamicMethodsModule |
| 3 | 1308:5 | no matching function for call to _CompileMethod | CreateDelegate |
| 4 | 1311:10 | assigning to int32_t from incompatible type System_RuntimeMethodHandle | CreateDelegate |
| 5 | 1312:42 | no matching function for call to CreateDelegateNoSecurityCheck | CreateDelegate |
| 6 | 1316:5 | use of undeclared identifier System_MulticastDelegate_StoreDynamicMethod_6b68d461b2de11b6 | CreateDelegate |
| 7 | 1343:5 | no matching function for call to _CompileMethod | CreateDelegate (2-arg) |
| 8 | 1346:10 | assigning to int32_t from incompatible type System_RuntimeMethodHandle | CreateDelegate (2-arg) |
| 9 | 1347:42 | no matching function for call to CreateDelegateNoSecurityCheck | CreateDelegate (2-arg) |
| 10 | 1351:5 | use of undeclared identifier System_MulticastDelegate_StoreDynamicMethod_6b68d461b2de11b6 | CreateDelegate (2-arg) |

注：#1 和 #2 是独立的字段赋值错误，#3-#6 和 #7-#10 是同一模式在两个 CreateDelegate 重载中的重复。

---

## 2. 根因分析

### 2.1 问题 #1: m_methodHandle 字段类型不匹配 (错误 #4, #8)

**现象**: `GetMethodDescriptor` 返回 `Il2CppObject*`，但赋值目标 `t0` 被推断为 `int32_t`。

```cpp
// 行 1311 / 1346:
t0 = System_Reflection_Emit_DynamicMethod_GetMethodDescriptor_2f4480f67c5e6ecb(arg0);
//    ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ 返回 Il2CppObject*
// 但 t0 被推断为 int32_t (因为 DynamicMethod 的 m_methodHandle 字段是 int32_t)
```

**根因**: `m_methodHandle` 字段在 DynamicMethod 结构体中是 `int32_t`（MethodHandle 的 MetadataToken），但 IL 调用 `GetMethodDescriptor` 返回的是 `System.RuntimeMethodHandle`（即 `Il2CppObject*` 包装）。类型桥接缺失。

**修复方案**: 在 GetMethodDescriptor 返回后、赋值前插入 reinterpret_cast:
```cpp
t0 = reinterpret_cast<int32_t>(System_Reflection_Emit_DynamicMethod_GetMethodDescriptor_2f4480f67c5e6ecb(arg0));
```

或更安全的 intptr_t 中间转换:
```cpp
t0 = static_cast<int32_t>(reinterpret_cast<intptr_t>(System_Reflection_Emit_DynamicMethod_GetMethodDescriptor_2f4480f67c5e6ecb(arg0)));
```

### 2.2 问题 #2: _CompileMethod 参数类型不匹配 (错误 #3, #7)

**现象**: `_CompileMethod` 期望 `Il2CppObject*`，但传入的是 `reinterpret_cast<Il2CppObject*>(...->m_methodHandle)`，其中 `m_methodHandle` 是 `int32_t`。

```cpp
// 行 1308 / 1343:
_CompileMethod(reinterpret_cast<Il2CppObject*>(((reinterpret_cast<System_Reflection_Emit_DynamicMethod*>(...))->m_methodHandle)));
//                                                                                     ^^^^^^^^^^^^^^^^^^^ int32_t
```

**根因**: `m_methodHandle` 字段是 `int32_t`，直接 reinterpret_cast 到指针被 Clang/GCC 拒绝（int → pointer 需要 static_cast 到 intptr_t 先）。

**修复方案**: 通过 intptr_t 桥接:
```cpp
_CompileMethod(reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(((reinterpret_cast<System_Reflection_Emit_DynamicMethod*>(...))->m_methodHandle))));
```

### 2.3 问题 #3: CreateDelegateNoSecurityCheck 参数类型不匹配 (错误 #5, #9)

**现象**: 第三个参数 `t0` 是 `int32_t`（来自 2.1），但函数期望 `Il2CppObject*`。

```cpp
// 行 1312 / 1347:
System_Delegate_CreateDelegateNoSecurityCheck_a6490e34d39eda35(arg1, nullptr, t0);
//                                                                        ^^ int32_t
```

**修复方案**: 桥接 t0 到指针:
```cpp
reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(t0))
```

### 2.4 问题 #4: StoreDynamicMethod 未声明 (错误 #6, #10)

**现象**: `System_MulticastDelegate_StoreDynamicMethod_6b68d461b2de11b6` 未声明。

**根因**: `System_MulticastDelegate` 类型未生成或未内联生成。该辅助函数本应在 `System_MulticastDelegate.cpp` 中生成，但因类型未翻译（Translate 条件不满足）而缺失。

**修复方案**: 在 DynamicMethod.cpp 中添加内联转发:
```cpp
// 在文件顶部添加:
inline void System_MulticastDelegate_StoreDynamicMethod_6b68d461b2de11b6(Il2CppObject* arg0, Il2CppObject* arg1) {
    reinterpret_cast<System_MulticastDelegate*>(arg1)->StoreDynamicMethod(
        reinterpret_cast<System_Reflection_MethodInfo*>(arg0));
}
```

或更直接的修复：在 MethodMulticastDelegate.cs 中补全 StoreDynamicMethod 映射。

### 2.5 问题 #1 (行 761): EmptyArray 类型不匹配

**现象**: `EmptyArray_1_int_sf_Value` 赋值给 `Il2CppObject*` 字段 `inl2`，但前者类型不兼容。

**根因**: `EmptyArray<int>` 的静态字段返回类型与 `Il2CppObject*` 不兼容。

**修复方案**: 在 MethodEmitter.cs 中为 EmptyArray 静态字段添加 reinterpret_cast 桥接，或修正字段类型声明。

### 2.6 问题 #2 (行 767): InitCustomAttributeBuilder 参数类型不匹配

**现象**: 参数类型不匹配。传入 7 个参数但函数签名不符。

**根因**: `InitCustomAttributeBuilder` 的 StackFold 生成使用了错误的参数数量/类型。

**修复方案**: 检查 MethodCustomAttributeBuilder.cs 中 InitCustomAttributeBuilder 的映射，确保参数类型与运行时 icall 签名一致。

---

## 3. 修复方案汇总

### 方案 A: CodeGenerator 级别修复 (通用桥接)

在 CodeGenerator 中添加针对 int32_t ↔ Il2CppObject* 的 reinterpret_cast 桥接（类似 cannot-init 修复）。

```csharp
// 在 EmitIcallCallArgs 中添加:
if (expectedArgCpp[i] == "Il2CppObject*" && IsCalliInt32Arg(arg))
    code = $"reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>({code}))";
```

此方案已在 calli 路径中实现（r78 fix），但 MethodMap 的 `GetMethodDescriptor` 和 `_CompileMethod` 调用未覆盖。

### 方案 B: StackFold 级别修复

在 StackFold 中识别以下模式并插入桥接:
- `MethodInfo.GetMethodDescriptor` 返回值需要 reinterpret_cast 到 intptr_t/int32_t
- `_CompileMethod` 调用时 m_methodHandle 需要 intptr_t 桥接
- `CreateDelegateNoSecurityCheck` 的 methodHandle 参数需要 intptr_t 桥接

### 方案 C: 手动补丁 (最小改动)

直接修改生成的 `System_Reflection_Emit_DynamicMethod.cpp`:

```diff
- t0 = System_Reflection_Emit_DynamicMethod_GetMethodDescriptor_...;
+ t0 = static_cast<int32_t>(reinterpret_cast<intptr_t>(System_Reflection_Emit_DynamicMethod_GetMethodDescriptor_...));

- _CompileMethod(reinterpret_cast<Il2CppObject*>(...->m_methodHandle));
+ _CompileMethod(reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(...->m_methodHandle)));

- System_Delegate_CreateDelegateNoSecurityCheck_...(arg1, nullptr, t0);
+ System_Delegate_CreateDelegateNoSecurityCheck_...(arg1, nullptr, reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(t0)));
```

---

## 4. 优先级

| 优先级 | 错误类型 | 数量 | 修复方式 |
|--------|----------|------|----------|
| P0 | CreateDelegate 调用链 (错误 #3-6, #7-10) | 8 | StackFold/CodeGenerator 桥接 |
| P1 | GetMethodDescriptor 返回类型 (错误 #4, #8) | 2 | StackFold 返回类型推断 |
| P2 | _CompileMethod 参数桥接 (错误 #3, #7) | 2 | StackFold 字段访问桥接 |
| P3 | StoreDynamicMethod 未声明 (错误 #6, #10) | 2 | MethodMap/内联转发 |
| P4 | InitCustomAttributeBuilder 参数 (错误 #2) | 1 | MethodMap 签名修正 |
| P5 | EmptyArray 类型不匹配 (错误 #1) | 1 | 字段类型修正 |

---

## 5. 关联影响

此 11 条错误属于 r77 shard2 (System.Reflection.Emit 命名空间)。修复后预计可消除:
- 2 条 m_methodHandle 类型桥接错误
- 2 条 _CompileMethod 桥接错误
- 2 条 CreateDelegateNoSecurityCheck 桥接错误
- 2 条 StoreDynamicMethod 未声明错误（需额外处理）
- 1 条 InitCustomAttributeBuilder 签名错误
- 1 条 EmptyArray 类型错误

总计约 10 条错误可被系统性修复（1 条 StoreDynamicMethod 需额外内联声明）。