# r77 TaskFactory+<>c 闭包类错误完整修复方案

**生成时间:** 2026-09-06
**数据来源:** r77 三片错误日志 + r77_qualified_name_analysis.md + r77_generic_instantiation_gaps.md + r77_taskawaiter_deep_analysis.md

---

## 1. 错误统计

| 错误类别 | 行数 | 唯一符号数 |
|----------|------|-----------|
| TaskFactory_1___c__DisplayClass (entry.cpp 引用) | 110 | 50+ |
| TaskFactory_1___c_*_sf___9 (静态字段) | 850 | 10 |
| **合计** | **~960** | **60+** |

---

## 2. 错误模式分析

### 2.1 DisplayClass 闭包类 (entry.cpp)

**症状:** `entry.cpp` 中的 `il2cpp_delegate_register_method` 调用引用了 `TaskFactory_1___c__DisplayClass*` 闭包方法，但只有 `VoidTaskResult` 特化存在。

**示例:**
```
error: use of undeclared identifier 'TaskFactory_1___c__DisplayClass32_0_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken__FromAsyncImpl_b__0_f5f366aaeb2eb86f'
did you mean: 'TaskFactory_1___c__DisplayClass32_0_System_Threading_Tasks_VoidTaskResult__FromAsyncImpl_b__0_f5f366aaeb2eb86f'?
```

**涉及的 DisplayClass:**
| 类名 | 缺失的特化 | 存在的特化 |
|------|-----------|-----------|
| DisplayClass32_0 | EventRegistrationToken | VoidTaskResult |
| DisplayClass35_0 | EventRegistrationToken | VoidTaskResult |
| DisplayClass38_0_1 | EventRegistrationToken_int | VoidTaskResult |
| DisplayClass41_0_2 | EventRegistrationToken_int_int | VoidTaskResult |
| DisplayClass44_0_3 | EventRegistrationToken_int_int_int | VoidTaskResult |

**根因:** `TaskFactory<T>.FromAsyncImpl` 是泛型方法，闭包类 `<>c__DisplayClass*` 也携带泛型参数。转译器只 emit 了 `VoidTaskResult` 特化（因为 `Task.FromAsync` 的默认返回类型是 `Task<VoidTaskResult>`），但 IL 中有 `EventRegistrationToken` 特化的调用。

### 2.2 静态字段闭包 (sf___9)

**症状:** `TaskFactory_1___c_*_sf___9` 静态字段被赋值/引用，但只有基础特化存在，缺少 `_int` 特化。

**示例:**
```
error: use of undeclared identifier 'TaskFactory_1___c_object_array_int_sf___9'
did you mean: 'TaskFactory_1___c_object_array_sf___9'?
```

**涉及的静态字段:**
| 缺失符号 | 存在符号 |
|----------|----------|
| TaskFactory_1___c_object_array_int_sf___9 | TaskFactory_1___c_object_array_sf___9 |
| TaskFactory_1___c_object_int_sf___9 | TaskFactory_1___c_object_sf___9 |
| TaskFactory_1___c_string_int_sf___9 | TaskFactory_1___c_string_sf___9 |
| TaskFactory_1___c_System_Threading_Tasks_Task_array_int_sf___9 | TaskFactory_1___c_System_Threading_Tasks_Task_array_sf___9 |
| TaskFactory_1___c_System_Threading_Tasks_VoidTaskResult_int_sf___9 | TaskFactory_1___c_System_Threading_Tasks_VoidTaskResult_sf___9 |

**根因:** `TaskFactory<T>.FromAsyncImpl` 的静态字段 `s_completeFromAsyncResult` 是泛型静态字段。转译器只 emit 了 `TaskFactory.s_completeFromAsyncResult`（即 `sf___9` 无后缀），但 IL 中有 `TaskFactory<T>.s_completeFromAsyncResult` 的泛型特化引用。

---

## 3. 根因总结

### 3.1 泛型特化缺失（核心根因）

转译器只 emit 了以下 TaskFactory 闭包特化:
- `TaskFactory<VoidTaskResult>.<>c__DisplayClass*` (FromAsyncImpl 闭包)
- `TaskFactory<object>.<>c__DisplayClass*` (部分)
- `TaskFactory<object[]>.<>c__DisplayClass*` (部分)
- `TaskFactory<string>.<>c__DisplayClass*` (部分)

但 IL 中引用了:
- `TaskFactory<EventRegistrationToken>.<>c__DisplayClass*` (全部 5 个 DisplayClass)
- `TaskFactory<Task[]>.<>c__DisplayClass*` (部分)
- `TaskFactory<T>.s_completeFromAsyncResult` (泛型静态字段)

### 3.2 编译器生成类型无 TypeDef

`<>` 前缀的闭包类没有 TypeDef，但转译器生成了它们的 class 描述符引用。这些类型的 metadata 名称包含泛型参数（如 `System.Threading.Tasks.TaskFactory`1+<>c__DisplayClass32_0[[System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken]]`）。

---

## 4. 修复方案

### 方案 A: 泛型特化展开（推荐）

**目标:** 为 TaskFactory 的所有引用特化生成闭包类和静态字段

**步骤:**
1. 在 Generate 前扫描所有 MethodSpec 引用，识别 TaskFactory<> 的闭合特化
2. 为每个闭合特化生成:
   - 闭包类 struct 定义 (`TaskFactory_1___c__DisplayClass32_0_EventRegistrationToken`)
   - 闭包方法体 (`FromAsyncImpl_b__0`, `FromAsyncImpl_b__1`)
   - 静态字段 (`s_completeFromAsyncResult`)

**预期消除:** ~960 行

**复杂度:** 高 — 需要完整的 MethodSpec 扫描和泛型展开管线

### 方案 B: 回退到默认特化（最小改动）

**目标:** 当闭合特化不存在时，回退到已存在的默认特化

**步骤:**
1. 在 `MethodCppNameFromToken` 中增加泛型闭合类型检测
2. 当 `TaskFactory<EventRegistrationToken>.FromAsyncImpl` 的闭合方法不存在时，回退到 `TaskFactory<VoidTaskResult>.FromAsyncImpl`
3. 在 entry.cpp 的 `il2cpp_delegate_register_method` 调用中，当闭包方法不存在时跳过注册

**预期消除:** ~110 行 (entry.cpp 部分)

**复杂度:** 低 — 仅增加回退逻辑

### 方案 C: 混合方案（推荐）

**短期 (立即):**
1. 在 entry.cpp 生成中增加闭包方法存在性检查，不存在则跳过注册 → 消除 110 行
2. 在静态字段引用中增加特化后缀剥离，`TaskFactory_1___c_object_int_sf___9` → `TaskFactory_1___c_object_sf___9` → 消除 850 行

**中期 (后续):**
1. 实现 TaskFactory 泛型特化完整展开
2. 为 Parallel/Lazy/GenericDelegateCache 等其他闭包类增加类似处理

**预期消除:** ~960 行 (短期) + 更多 (中期)

---

## 5. 修复优先级

| 优先级 | 方案 | 影响行数 | 风险 |
|--------|------|----------|------|
| **P0** | 方案 C 短期 (entry.cpp 跳过 + 静态字段后缀剥离) | ~960 | 低 |
| **P1** | 方案 B (泛型回退) | ~110 | 低 |
| **P2** | 方案 A (完整泛型展开) | ~960+ | 高 |

---

## 6. 关联错误

TaskFactory+<>c 闭包类错误与以下错误同源:
- `System_RuntimeType_ListBuilder_1_object_*` (100 错误) — 同属泛型特化缺失
- `System_Nullable_1_..._ReadOnlyMemory_1_byte_*` (36 错误) — 同属泛型特化缺失
- `System_Threading_Tasks_Parallel___c__DisplayClass*` (71 错误) — 同属闭包类泛型特化缺失
- `System_Lazy_1___c*` (24 错误) — 同属闭包类泛型特化缺失

**总计关联错误:** ~1,191 行

修复 TaskFactory 闭包类错误的技术方案可复用到上述所有关联错误。
