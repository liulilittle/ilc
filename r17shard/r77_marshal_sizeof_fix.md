# r77 Marshal.SizeOf 错误修复方案

## 问题分析

r77 中涉及 sizeof 的错误共 118 种。核心问题: 泛型类型的 sizeof 在编译期无法确定大小,因为泛型类型参数在运行时才能确定。

### 错误模式分类

模式 1: sizeof(System_Collections_Generic_KeyValuePair_2_xxx_yyy) — 泛型结构体大小未知
- 出现次数: ~93
-  KeyValuePair 2.x 占大多数 (object-object, string-int, int-int 等)
-  Dictionary 2.x Enumerator 也有 3 次

模式 2: 事件/诊断结构体 sizeof (非泛型)
- EventSource EventData
- ManifestEtw TRACE_ENABLE_INFO

模式 3: 其他非泛型 sizeof
- int32_t / intptr_t / Il2CppDecimal

## 修复方案

在 MethodRefResolver 或 CodeGenerator 中，当遇到 sizeof(T) 且 T 是未特化的泛型类型时，需要生成运行时确定大小的代码。

### 方案: 用 il2cpp_sizeof 辅助函数替代 sizeof

#### Step 1: 在 runtime 中添加泛型 sizeof 辅助

`cpp
// il2cpp_runtime.h 或新建 il2cpp_sizeof.h
template<typename T>
inline size_t il2cpp_sizeof() { return sizeof(T); }

// 为已知泛型实例化提供特化
template<> inline size_t il2cpp_sizeof<System_Collections_Generic_KeyValuePair_2<int32_t,int32_t>>() { return sizeof(System_Collections_Generic_KeyValuePair_2_int_int); }
`

#### Step 2: 在 MethodRefResolver 中拦截 sizeof

当 IL 指令为 sizeof 操作码时:
1. 如果类型是完全实例化的非泛型类型 → 保持 sizeof(T)
2. 如果类型是泛型实例化 (如 KeyValuePair<int,int>) → 生成 il2cpp_sizeof<ConcretType>()
3. 如果类型包含未特化的泛型参数 → 使用运行时 il2cpp_type_get_size 函数

#### Step 3: CodeGenerator 中修改 BOX 操作码处理

当前生成:
`cpp
il2cpp::runtime::icall_box_user_struct(&XXX_class, &ptr, sizeof(XXX));
`

修复后:
`cpp
il2cpp::runtime::icall_box_user_struct(&XXX_class, &ptr, il2cpp_sizeof<XXX>());
`

或者更通用: 添加 il2cpp_type_size_of(Il2CppClass*) 运行时函数,通过 klass->instanceSize 获取。

## 具体修复步骤

1. **新建 il2cpp_sizeof.h runtime 头文件**
   - 提供模板化的 il2cpp_sizeof<T>() 
   - 为常见泛型实例化提供特化

2. **修改 CodeGenerator 的 sizeof 处理**
   - 当 sizeof 操作码的 token 是泛型实例化时，生成 il2cpp_sizeof<ConcretType>()
   - 或者统一用 il2cpp_type_size_of(type klass) 运行时调用

3. **在 MethodRefResolver 中添加 sizeof 的识别**
   - 识别 Marshal.SizeOf 调用
   - 映射到内置的 il2cpp_sizeof icall

## 预期效果

修复后 93 个 KeyValuePair/Enumerator sizeof 错误将消除,其余非泛型 sizeof 保持不变。

---

生成时间: 2026-09-06 10:43:30
