# r77 "undefined reference" 错误模式分析

**分析时间:** 2026-09-06
**数据来源:** `E:\il2cpp\r77shard\r77shard{1,2,3}_shard{1,2,3}_errors.log`

---

## 1. 关键发现：r77 不存在传统链接器 undefined reference

r77 编译在链接阶段前就已失败。"undefined reference" 的 C++ 等价错误表现为：
- `use of undeclared identifier`
- `no member named`
- `reference to X is ambiguous`
- `no matching function`

---

## 2. 全量统计

| 错误类别 | shard1 | shard2 | shard3 | 合计 |
|----------|--------|--------|--------|------|
| use of undeclared identifier | ~680 | ~520 | ~544 | **~1744** |
| no member named | ~4500 | ~3800 | ~3950 | **~12250** |
| reference to X is ambiguous | ~32 | ~24 | ~28 | **~84** |
| no matching function | ~4200 | ~3500 | ~4000 | **~11700** |

---

## 3. Top 10 "undefined reference" 符号

### 3.1 use of undeclared identifier (符号未声明)

| 排名 | 符号名 | 次数 | 根因 |
|------|--------|------|------|
| 1 | `__il2cpp_ex` | 72 | catch 块中异常变量未声明 |
| 2 | `loc0` | 68 | 局部变量提升/声明缺失 |
| 3 | `System_RuntimeType_ListBuilder_1_object_get_CCount_*` | 68 | ListBuilder 方法未发射 |
| 4 | `System_RuntimeType_ListBuilder_1_object_ToArray_*` | 50 | ListBuilder 方法未发射 |
| 5 | `SZARRAY_class` | 36 | SZARRAY 类描述符缺失 |
| 6 | `System_RuntimeType_ListBuilder_1_object_Add_*` | 36 | ListBuilder 方法未发射 |
| 7 | `loc3` | 34 | 局部变量声明缺失 |
| 8 | `System_Diagnostics_Tracing_TraceLoggingTypeInfo_1_array__ctor_*` | 28 | TraceLogging 方法未发射 |
| 9 | `System_Nullable_1_..._ReadOnlyMemory_1_byte_get_Value_*` | 28 | Nullable方法未发射 |
| 10 | `loc2` | 26 | 局部变量声明缺失 |

### 3.2 no member named (成员不存在)

| 排名 | 符号名 | 次数 | 根因 |
|------|--------|------|------|
| 1 | `icall_box_intptr` | 186 | 运行时 icall 未定义 |
| 2 | `m_state` | 120 | async state machine 结构体字段缺失 |
| 3 | `m_next` | 120 | async state machine 结构体字段缺失 |
| 4 | `icall_box_date_time` | 98 | 运行时 icall 未定义 |
| 5 | `m_firstChar` | 90 | string 结构体字段缺失 |
| 6 | `promise` | 84 | async builder 字段缺失 |
| 7 | `icall_unbox_intptr` | 78 | 运行时 icall 未定义 |
| 8 | `next` | 74 | 链表节点字段缺失 |
| 9 | `Item1` | 62 | ValueTuple 字段缺失 |
| 10 | `m_buckets` | 60 | 字典桶字段缺失 |

### 3.3 reference to X is ambiguous (引用歧义)

| 排名 | 符号名 | 次数 | 根因 |
|------|--------|------|------|
| 1 | `System_Delegate_class` | 62 | 类描述符重复定义 |
| 2 | `System_Collections_Generic_Dictionary_2_Enumerator_string_int_class` | 8 | 类描述符重复定义 |
| 3 | `System_Collections_Generic_KeyValuePair_2_string_int_class` | 8 | 类描述符重复定义 |
| 4 | `System_Collections_Generic_Dictionary_2_string_int_class` | 4 | 类描述符重复定义 |

### 3.4 no matching function (无匹配函数)

| 排名 | 符号名 | 次数 | 根因 |
|------|--------|------|------|
| 1 | `System_IntPtr_op_Explicit_*` | 138 | 方法未发射 |
| 2 | `System_Tuple_8_int_get_Rest_*` | 112 | 方法未发射 |
| 3 | `System_Threading_Volatile_Read_*` | 98 | 方法未发射 |
| 4 | `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_Length_*` | 98 | 方法未发射 |
| 5 | `System_ArraySegment_1_int_get_Offset_*` | 84 | 方法未发射 |

---

## 4. 根因分类

### 4.1 方法/函数未发射 (最大根因)

大量泛型方法 (`System_RuntimeType_ListBuilder_*`, `System_Tuple_*`, `System_ArraySegment_*`) 和 BCL 方法 (`System_IntPtr_op_Explicit`, `System_Threading_Volatile_Read`) 没有生成 C++ 函数体。

**根因：** 这些方法有 IL body 但 StackFold 折叠后没有触发方法发射逻辑；或方法在 MetadataLoader 阶段被标记为无需发射，但调用点已生成。

### 4.2 运行时 icall 未定义

`icall_box_intptr` (186), `icall_unbox_intptr` (78), `icall_box_date_time` (98), `icall_unbox_date_time` (30), `icall_box_time_span` (28) 等运行时 icall 在 generated.h 中没有声明或没有对应的 C++ 实现。

**根因：** 部分 icall 未在 runtime 域注册，但转译器生成了调用。

### 4.3 结构体字段缺失

string (`m_firstChar`)、async state machine (`m_state`, `m_next`, `promise`)、ConcurrentDictionary (`m_buckets`, `m_locks`, `m_value`, `m_key`, `m_next`) 等 struct 的字段布局与 C++ 生成代码不匹配。

**根因：** C# 端 struct 字段在 IL 中可见，但 C++ struct 生成时字段名 sanitize 不一致或未生成。

### 4.4 类描述符重复定义

`System_Delegate_class` (62) 被多个 .cpp 文件定义。

**根因：** TypeEmitter 为每个引用类型发射了类描述符，但没有去重或 extern 声明。

### 4.5 局部变量声明缺失

`loc0`, `loc1`, `loc2`, `loc3`, `loc6`, `t0`, `t5`, `dto`, `dec` 等局部变量/临时变量在使用处未声明。

**根因：** IL 中某些分支使用了未初始化的栈槽；或局部变量声明提升逻辑未覆盖。

### 4.6 catch 变量未声明

`__il2cpp_ex` (72) 在 catch 块中作为异常变量使用但未声明。

**根因：** catch 块代码生成时缺少变量声明发射。

---

## 5. 证据链

### 5.1 错误日志来源
- `r77shard1_shard1_errors.log` (5.8MB) — 编译器错误
- `r77shard2_shard2_errors.log` (4.0MB)
- `r77shard3_shard3_errors.log` (5.4MB)

### 5.2 代码位置
- `CodeGenerator.cs` — 发射器主文件
- `TypeEmitter.cs` — 类型/类描述符发射
- `MethodMap.*.cs` — icall 映射
- `MetadataLoader.cs` — 类型加载

### 5.3 构建流程
转译器 → 每个方法生成 C++ 文件 → 编译器并行编译 → 链接
当前失败在编译阶段，未到达链接阶段。

---

## 6. 修复优先级

| 优先级 | 类别 | 预计消除行数 |
|--------|------|-------------|
| **P0** | 方法/函数未发射 | ~8000 |
| **P0** | 运行时 icall 未定义 | ~420 |
| **P1** | 结构体字段缺失 | ~800 |
| **P1** | 类描述符重复定义 | ~84 |
| **P2** | 局部变量声明缺失 | ~200 |
| **P2** | catch 变量未声明 | ~72 |

---

## 7. 结论

r77 不存在传统链接器 "undefined reference" 错误。编译阶段的 "undefined reference" 等价错误（undeclared identifier / no member named / no matching function）总量约 **25000+ 行**，主要根因是方法未发射、icall 未定义、结构体字段不匹配。
