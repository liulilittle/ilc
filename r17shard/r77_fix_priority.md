# r77 修复优先级清单

**生成时间:** 2026-09-06
**数据来源:** r77_gcc_scan_results.md, r77_cannot_init_breakdown.md, r77_static_cast_analysis.md, r77_membref_analysis.md, r77_undeclared_analysis.md, r77_nv_analysis.md, r77_unknown_type_analysis.md, r77_guid_intptr_valuetype_analysis.md, r77_generic_instantiation_gaps.md, cannot_init_fix.md, r77_undefined_reference_analysis.md

---

## 总览

| 优先级 | 错误行数 | 根因数 |
|--------|----------|--------|
| **P0** | ~19,000+ | 3 |
| **P1** | ~5,500+ | 4 |
| **P2** | ~500+ | 6 |

---

## P0: 影响 >500 行错误的根因

### P0-1: int↔pointer 类型不匹配 (cannot-init)

**影响行数:** ~3,500 行

**根因描述:** IL slot-reuse artifact — C# 编译器复用 object-typed 临时槽存储 int32/bool 值。局部变量声明为 `Il2CppObject*` 但赋值是 `int32_t`，C++ 禁止隐式转换。

**错误分布:**
| 模式 | 行数 |
|------|------|
| no viable overloaded '=' | ~2,746 |
| no viable conversion | ~1,558 |
| cannot cast to pointer | ~1,953 |
| cannot initialize parameter | ~1,995 |
| incompatible pointer to integer | ~983 |
| reinterpret_cast not allowed | ~716 |
| cannot initialize return | ~163 |

**已修复:**
- ✅ AssignStmt 局部变量 ref bridge (~2490-2502行)
- ✅ AssignStmt 静态字段 stsfld bridge (~2478-2493行)
- ✅ FieldAssignStmt 实例字段 bridge (~2522-2528行)
- ✅ RetStmt 返回值 bridge (~2551-2554行)
- ✅ CallExpr 返回值 reinterpret (~6317行)

**未修复:**
- ❌ **EmitIcallCallArgs 参数传递桥** (~602行) — 参数位置传入 int32 到 Il2CppObject* 参数
- ❌ **reinterpret_cast int32_t→intptr_t** (~716行) — g++ 不允许 reinterpret_cast 整数到指针宽度

**补充修复方案:**
1. 在 `EmitIcallCallArgs` (6506行起) 的 cast switch 中新增 `"r"` cast 码，或自动检测 Il2CppObject* param 的 int 值并插入 `reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(...))`
2. 将 `reinterpret_cast<intptr_t>(int32_val)` 替换为 `static_cast<intptr_t>(int32_val)`

**预期消除行数:** ~1,300 行

---

### P0-2: 泛型方法实例化缺失

**影响行数:** ~872 行 (undeclared) + 大量 no matching function (~4,160行)

**根因描述:** 转译器只 emit `int`（默认）实例化的泛型方法，但 mscorlib IL 大量引用 `object`、`string`、`VoidTaskResult`、`Task[]` 等其他实例化。

**Top 泛型类型（按缺失实例化错误数排序）:**
| 排名 | 泛型基类型 | 缺失实例化错误数 | 主要缺失的实例化 |
|------|-----------|----------------|----------------|
| 1 | System_Threading_Tasks_TaskFactory | 167 | `_int_`, `_string_`, `_object_`, `_VoidTaskResult_`, `_Task_array_` |
| 2 | System_RuntimeType_ListBuilder | 100 | `_object_` (79x) |
| 3 | System_Nullable | 36 | `_ReadOnlyMemory_1_byte_` (26x), `_ArraySegment_1_byte_` (4x) |
| 4 | System_Array_FunctorComparer | 29 | `_ReadOnlyMemory_1_int_`, `_Decimal_`, `_EventRegistrationToken_` |
| 5 | System_Diagnostics_Tracing_TraceLoggingTypeInfo | 20 | `_array_` (14x), `_intptr_`, `_uintptr_` |
| 6 | System_Runtime_CompilerServices_ConditionalWeakTable | 19 | `_int_`, `_object_`, `_object_Dictionary<...>_` |
| 7 | System_Threading_Tasks_Task | 16 | `_array_`, `_object_`, `_string_`, `_Task_array_` |
| 8 | V_System_Collections_Generic_KeyValuePair | 12 | `_Int32_ChannelInfo_`, `_Object_Object_` |
| 9 | System_Threading_Tasks_GenericDelegateCache | 10 | `_int_`, `_EventRegistrationToken_int_` |
| 10 | System_Threading_Tasks_ContinuationResultTaskFromResultTask | 10 | `_object_array_int_`, `_object_int_` |

**修复方案:**
1. 在 Generate 前扫描所有 MethodSpec/TypeSpec 引用，为每个被引用的泛型方法 emit 对应实例化
2. 对 TaskFactory/ListBuilder/Nullable/ConditionalWeakTable/Task 优先实现完整实例化展开

**预期消除行数:** ~872 行 (undeclared) + ~2,000 行 (no matching function 中泛型相关部分) = ~2,800 行

---

### P0-3: 结构体字段不匹配 (no member named)

**影响行数:** ~12,250 行

**根因描述:** C++ struct 布局与 C# struct 字段不匹配。生成的代码引用了不存在的字段名（如 `m_firstChar`、`m_state`、`m_buckets`）。

**Top 缺失字段:**
| 排名 | 字段名 | 次数 | 涉及结构体 |
|------|--------|------|-----------|
| 1 | `icall_box_intptr` | 186 | 运行时 icall 未注册 |
| 2 | `m_state` | 120 | async state machine |
| 3 | `m_next` | 120 | async state machine / 链表节点 |
| 4 | `icall_box_date_time` | 98 | 运行时 icall 未注册 |
| 5 | `m_firstChar` | 90 | System.String |
| 6 | `promise` | 84 | async builder |
| 7 | `icall_unbox_intptr` | 78 | 运行时 icall 未注册 |
| 8 | `next` | 74 | 链表节点 |
| 9 | `Item1` | 62 | ValueTuple |
| 10 | `m_buckets` | 60 | ConcurrentDictionary |

**修复方案:**
1. 注册缺失的运行时 icall: `icall_box_intptr`, `icall_unbox_intptr`, `icall_box_date_time`, `icall_unbox_date_time`, `icall_box_time_span`
2. 修正 async state machine struct 字段名: `m_state` → 实际字段名
3. 修正 String struct 字段: `m_firstChar` → 实际字段名
4. 修正 ConcurrentDictionary struct 字段: `m_buckets`, `m_locks`, `m_value`, `m_key`
5. 修正 ValueTuple struct 字段: `Item1`, `Item2`, `Item3`

**预期消除行数:** ~12,000 行

---

## P1: 影响 100-500 行错误的根因

### P1-1: 未知类型名 (Carrier Kind 泄漏)

**影响行数:** ~512 行

**根因描述:** 发射器将 carrier kind tag (`dt`, `ts`, `dec`) 或 `V:` 前缀直接作为 C++ 类型名输出。

**错误分布:**
| 类别 | 行数 | 示例 |
|------|------|------|
| Carrier kind 泄漏 | 216 | `dt` (165), `ts` (48), `dec` (3) |
| V: 前缀泄漏 | 221 | `V_System_Guid`, `V_System_DateTime` |
| 尾部下划线泄漏 | 58 | `ReadOnlySpan_1_u1_` |
| 缺失类型 | 17 | `System_Threading_IThreadPoolWorkItem` |

**修复方案:**
1. 修复 `TypeKindResolver.ResolveParamEncoding` 返回实际 C++ 类型名
2. 在 `TypeCppName` 中剥离 `V:` 前缀
3. 确保 byref-value alias 在使用点可见

**预期消除行数:** ~512 行

---

### P1-2: 值类型↔引用类型转换失败

**影响行数:** ~4,300 行

**根因描述:** 值类型结构体无法直接转换为 `Il2CppObject*`，需要显式装箱。

**错误分布:**
| 模式 | 行数 |
|------|------|
| no viable conversion | ~1,558 |
| no viable overloaded '=' | ~2,746 |

**Top 转换失败类型:**
| 来源类型 | 目标类型 | 次数 |
|----------|----------|------|
| TaskAwaiter_1_int | Il2CppObject* | 226 |
| TaskAwaiter_1_int | int32_t | 73 |
| Il2CppObject* | System_Guid | 33 |
| Il2CppTimeSpan | Il2CppDateTime | 17 |
| EventRegistrationToken | Il2CppObject* | 12 |
| DictionaryEntry | Il2CppObject* | 8 |
| VoidTaskResult | int32_t | 7 |

**修复方案:**
1. 识别 TaskAwaiter 作为参数/返回值的调用点，插入 `.GetResult()` 解包
2. 对 DateTime/Decimal/TimeSpan/DateTimeOffset 插入显式 box icall
3. 对 Guid/DictionaryEntry/VoidTaskResult 插入显式装箱

**预期消除行数:** ~1,500 行

---

### P1-3: Il2CppArray* ← Il2CppObject* 参数传递

**影响行数:** ~314 行

**根因描述:** 数组类型参数需要显式 cast 从 `Il2CppObject*` 到 `Il2CppArray*`。

**修复方案:**
在 `EmitIcallCallArgs` 和通用参数发射路径中，检测到目标类型是 `Il2CppArray*` 且源是 `Il2CppObject*` 时，插入 `reinterpret_cast<Il2CppArray*>(...)`。

**预期消除行数:** ~314 行

---

### P1-4: 引用歧义 (类描述符重复定义)

**影响行数:** ~84 行

**根因描述:** `System_Delegate_class` 等类描述符被多个 .cpp 文件重复定义。

**错误分布:**
| 符号 | 次数 |
|------|------|
| System_Delegate_class | 62 |
| System_Collections_Generic_Dictionary_2_Enumerator_string_int_class | 8 |
| System_Collections_Generic_KeyValuePair_2_string_int_class | 8 |
| System_Collections_Generic_Dictionary_2_string_int_class | 4 |

**修复方案:**
改为 extern 声明 + 单一定义模式。

**预期消除行数:** ~84 行

---

## P2: 影响 <100 行错误的根因

### P2-1: 局部/异常变量未声明

**影响行数:** ~272 行

**根因描述:** 局部变量 (`loc0`, `loc1`, `loc2`, `loc3`, `loc6`, `t0`, `t5`) 和异常变量 (`__il2cpp_ex`) 在使用处未声明。

**修复方案:**
1. 审计局部变量声明提升逻辑
2. 确保 catch 块变量 `__il2cpp_ex` 在正确作用域内声明

**预期消除行数:** ~272 行

---

### P2-2: Guid 指针桥接

**影响行数:** ~45 行

**根因描述:** `System_Guid` 是 16 字节值类型，不能隐式转换为 `const void*` 或 `Il2CppObject*`。

**修复方案:**
在需要指针的场景插入 `&guid` 取地址操作。

**预期消除行数:** ~45 行

---

### P2-3: 运行时值类型装箱

**影响行数:** ~50 行

**根因描述:** `Il2CppDateTime`、`Il2CppDecimal`、`Il2CppTimeSpan`、`Il2CppDateTimeOffset` 需要显式装箱才能作为 `Il2CppObject*`。

**修复方案:**
插入 `icall_box_datetime`、`icall_box_decimal`、`icall_box_timespan`、`icall_box_datetimeoffset`。

**预期消除行数:** ~50 行

---

### P2-4: Il2CppClass* ← int64_t 参数不匹配

**影响行数:** ~33 行

**根因描述:** 泛型 type-handle resolution 产生 int64_t 但期望 Il2CppClass*。

**修复方案:**
插入 `reinterpret_cast<Il2CppClass*>(...)` 桥接。

**预期消除行数:** ~33 行

---

### P2-5: EventRegistrationToken 转换

**影响行数:** ~22 行

**根因描述:** `EventRegistrationToken` 是值类型，不能隐式转换为 `Il2CppObject*` 或 `int32_t`。

**修复方案:**
插入显式装箱或解包逻辑。

**预期消除行数:** ~22 行

---

### P2-6: 接口/缺失类型

**影响行数:** ~17 行

**根因描述:** `System_Threading_IThreadPoolWorkItem` 等接口/类型没有生成 C++ struct。

**修复方案:**
为缺失类型添加运行时映射或生成空 struct。

**预期消除行数:** ~17 行

---

## 修复优先级总结

| 优先级 | 根因 | 影响行数 | 修复状态 | 预期消除 |
|--------|------|----------|----------|----------|
| **P0-1** | int↔pointer cannot-init | ~3,500 | 部分修复 (4/6) | ~1,300 |
| **P0-2** | 泛型实例化缺失 | ~2,800+ | ❌ 未修复 | ~2,800 |
| **P0-3** | 结构体字段不匹配 | ~12,250 | ❌ 未修复 | ~12,000 |
| **P1-1** | 未知类型名泄漏 | ~512 | ❌ 未修复 | ~512 |
| **P1-2** | 值类型↔引用类型转换 | ~4,300 | ❌ 未修复 | ~1,500 |
| **P1-3** | Il2CppArray* 参数传递 | ~314 | ❌ 未修复 | ~314 |
| **P1-4** | 类描述符重复定义 | ~84 | ❌ 未修复 | ~84 |
| **P2-1** | 局部/异常变量未声明 | ~272 | ❌ 未修复 | ~272 |
| **P2-2** | Guid 指针桥接 | ~45 | ❌ 未修复 | ~45 |
| **P2-3** | 运行时值类型装箱 | ~50 | ❌ 未修复 | ~50 |
| **P2-4** | Il2CppClass* 参数不匹配 | ~33 | ❌ 未修复 | ~33 |
| **P2-5** | EventRegistrationToken | ~22 | ❌ 未修复 | ~22 |
| **P2-6** | 接口/缺失类型 | ~17 | ❌ 未修复 | ~17 |

**总计预期消除:** ~18,900 行 (占 r77 总错误 ~75%)

---

## 推荐执行顺序

1. **立即执行:** P0-1 补充修复（EmitIcallCallArgs 参数传递桥）— 低风险、高回报
2. **重点投入:** P0-3 结构体字段修正 — 最大错误源、影响最广
3. **重点投入:** P0-2 泛型实例化展开 — 系统性问题、影响深远
4. **批量修复:** P1-1 + P1-2 + P1-3 + P1-4 — 中等复杂度、中等回报
5. **收尾清理:** P2-1 ~ P2-6 — 低优先级、边缘场景
