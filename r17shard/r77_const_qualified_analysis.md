# r77 const 错误模式分析

**日期**: 2026-09-06
**来源**: r77 三片错误日志 (shard1/shard2/shard3)
**总错误数**: 16653
**const 相关错误数**: 28

## 1. 按错误模式分组统计

| 模式 | 次数 |
|------|------|
| int64_t -> const Il2CppMethodInfo* | 11 |
| int64_t -> const Il2CppFieldInfo* | 4 |
| System_RuntimeFieldHandle* -> const Il2CppFieldInfo* | 3 |
| System_RuntimeMethodHandle* -> const Il2CppMethodInfo* | 3 |
| System_Guid -> const void* | 6 |
| System_IntPtr -> const char16_t* (reinterpret_cast) | 1 |

## 2. 按出错文件统计 (Top 10)

| 文件 | const 错误数 |
|------|-------------|
| System_Reflection_Emit_DynamicResolver.cpp | 7 |
| System_RuntimeMethodHandle.cpp | 6 |
| System_RuntimeFieldHandle.cpp | 5 |
| System_Security_AccessControl_CommonAcl.cpp | 4 |
| System_Diagnostics_Tracing_EventSource.cpp | 2 |
| System_Diagnostics_StackFrameHelper.cpp | 1 |
| System_Runtime_Remoting_Messaging_StackBuilderSink.cpp | 1 |
| Microsoft_Win32_Fusion.cpp | 1 |
| System_Reflection_MethodBase.cpp | 1 |

## 3. 按目标类型分组

| 目标类型 | 次数 |
|----------|------|
| const Il2CppMethodInfo* | 14 |
| const Il2CppFieldInfo* | 7 |
| const void* | 6 |
| const char16_t* | 1 |

## 4. 按源类型分组

| 源类型 | 次数 |
|--------|------|
| int64_t | 15 |
| System_RuntimeMethodHandle* | 3 |
| System_RuntimeFieldHandle* | 3 |
| System_Guid | 6 |
| System_IntPtr | 1 |

## 5. Top 10 出错的 const 相关错误

1. **int64_t -> const Il2CppMethodInfo*** (11次) - DynamicResolver/MethodBase/RuntimeMethodHandle 等文件中的 int64 值无法直接传递给 const Il2CppMethodInfo* 参数
2. **int64_t -> const Il2CppFieldInfo*** (4次) - DynamicResolver/RuntimeFieldHandle 中的 int64 值无法直接传递给 const Il2CppFieldInfo* 参数
3. **System_Guid -> const void*** (6次) - EventSource/CommonAcl 中的 System_Guid 无法转换为 const void*
4. **System_RuntimeMethodHandle* -> const Il2CppMethodInfo*** (3次) - RuntimeMethodHandle 中的 RuntimeMethodHandle* 无法传递给 const Il2CppMethodInfo* 参数
5. **System_RuntimeFieldHandle* -> const Il2CppFieldInfo*** (3次) - RuntimeFieldHandle 中的 RuntimeFieldHandle* 无法传递给 const Il2CppFieldInfo* 参数
6. **System_IntPtr -> const char16_t*** (1次) - Fusion.cpp 中的 reinterpret_cast 不被允许

## 6. 根因分析

### 6.1 int64_t -> const Il2CppMethodInfo* / const Il2CppFieldInfo* (15次)
- 这些错误集中在 Reflection.Emit.DynamicResolver、RuntimeMethodHandle、RuntimeFieldHandle 等文件
- 根本原因是 icall 函数签名期望 const Il2CppMethodInfo* 或 const Il2CppFieldInfo* 参数
- 但传入的是 int64_t (指针的整数表示)
- 需要显式 reinterpret_cast 将 int64_t 转换为正确的指针类型

### 6.2 System_Guid -> const void* (6次)
- 这些错误集中在 Diagnostics.Tracing.EventSource 和 Security.AccessControl.CommonAcl
- System_Guid 是一个结构体，不能隐式转换为 const void*
- 需要显式取地址或 reinterpret_cast

### 6.3 System_RuntimeMethodHandle* / System_RuntimeFieldHandle* (6次)
- RuntimeMethodHandle* 不能隐式转换为 const Il2CppMethodInfo*
- RuntimeFieldHandle* 不能隐式转换为 const Il2CppFieldInfo*
- 需要正确的类型转换逻辑

## 7. 修复建议

1. **int64_t -> 指针类型**: 在 CodeGenerator 的 EmitIcallCallArgs 中，当目标参数是 const Il2CppMethodInfo* 或 const Il2CppFieldInfo* 时，如果源是 int64_t，需要生成 einterpret_cast<const Il2CppMethodInfo*>(static_cast<intptr_t>(val))
2. **Guid -> const void***: 生成 &guid 或 einterpret_cast<const void*>(&guid)
3. **Handle* -> const Info***: 需要运行时类型信息来正确转换，或者生成辅助函数来处理
