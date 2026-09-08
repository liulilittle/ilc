# r77 "cannot convert to base class" 错误模式分析报告

**日期**: 2026-09-06
**数据源**: E:\il2cpp\r77shard\r77shard{1,2,3}_shard{1,2,3}_errors.log + clusters.txt
**全量 verdict**: E:\il2cpp\r17shard\r77_full_verdict.md

---

## 一、核心发现：r77 不存在 "cannot convert to base class" 字面错误

r77 三片错误日志中，以下模式**计数为 0**:
- `base class` (作为错误消息) — 0 次
- `inheritance` (作为错误消息) — 0 次
- `derived` (作为错误消息) — 0 次

r77 中与 "base class / inheritance" 相关的错误仅有一条消息匹配:
```
static_cast from 'System_Security_Principal_WindowsIdentity *' to 'Il2CppObject *', which are not related by inheritance, is not allowed
```
这属于 **L2 命名碰撞的下游症状**（同一名字对应不同类型，类型系统识别为不相关的类），不是独立的 "cannot convert to base class" 错误族。

---

## 二、r77 错误分布（按 cannot-convert 相关形态分组）

### 2.1 全量统计（r77 vs r75 对比）

| 错误族 | r75 | r77 | Δ | 说明 |
|--------|-----|-----|---|------|
| cannot-init (全部) | 2296 | 2284 | -12 | 三分支闭合微弱有利 |
| no-matching | 4154 | 4160 | +6 | 哈希抖动/StubHelpers 翻转 |
| no-viable | 2420 | 2420 | 0 | 持平 |
| static-cast | 222 | 222 | 0 | 持平 |
| membref | 1632 | 1632 | 0 | 持平（三分支未修复） |
| undeclared | 1384 | 872 | **-512** | r78 三分支 i→o 有利向 |
| Top1 Il2CppObject*←int32 | 613 | 613 | 0 | 持平 |
| **fail** | **1440** | **1441** | **+1** | StubHelpers 单点真翻转 |
| **err** | **16647** | **16653** | **+6** | 既有失败文件过载 |

### 2.2 cannot-convert 相关子形态 Top 10

| 排名 | 子形态 | 计数 | 典型示例 |
|------|--------|------|---------|
| 1 | `cannot initialize a parameter of type 'Il2CppObject*'` | 1107 | 引用类型实参传递给 Il2CppObject* 参数 |
| 2 | `static_cast from 'Il2CppObject*' to 'int32_t'` | ~50 | 拆箱/类型转换失败 |
| 3 | `cannot cast from type 'int32_t' to pointer type` | ~63 | int32 左值当指针传递 |
| 4 | `reinterpret_cast from 'int32_t' to 'intptr_t'` | ~33 | IntPtr/UIntPtr 双向转换 |
| 5 | `no viable conversion from 'System_IntPtr' to 'int32_t'` | ~6 | 嵌套值类型不可隐式转换 |
| 6 | `incompatible pointer to integer conversion assigning to 'intptr_t'` | ~6 | 指针赋给整数槽 |
| 7 | `incompatible pointer types assigning to 'X*' from 'Il2CppObject*'` | ~8 | 字段类型与 Il2CppObject* 不匹配 |
| 8 | `reinterpret_cast from 'System_IntPtr' to 'const char16_t*'` | ~1 | IntPtr 到字符串指针 |
| 9 | `cannot convert 'Il2CppObject' to 'int32_t' without conversion operator` | ~3 | 表达式类型不兼容 |
| 10 | `static_cast from '...*' to 'Il2CppObject*', which are not related by inheritance` | ~3 | L2 命名碰撞下游（同名不同类型） |

---

## 三、与 "base class / inheritance" 相关的 3 条关键错误

r77 中唯一匹配 "inheritance" 字样的错误来自 **L2 命名碰撞** 的下游：

### 3.1 System_Security_SecurityContext.cpp

```
error: static_cast from 'System_Security_Principal_WindowsIdentity *' to 'Il2CppObject *', which are not related by inheritance, is not allowed
```

**证据**:
- [READ: E:\il2cpp\r77shard\r77shard1_shard1_errors.log] — 匹配行出现 3 次（:721, :774, :1092）
- 同文件还有 `static_cast from 'System_Threading_CompressedStack *' to 'Il2CppObject *'` 同类错误

**根因**: L2 命名碰撞 → `System_Security_Principal_WindowsIdentity` 与另一个同名但不同类型共享 C++ 名字 → 编译器看到的 `WindowsIdentity` 定义与 `Il2CppObject` 无继承关系 → static_cast 失败。

### 3.2 碰撞名字的 vtable 引用

r77 错误日志中大量出现 `->klass->vtable[N]` 和 `il2cpp_interface_dispatch(...)` 调用。这些调用本身不报错，但其引用的 class 名字可能因 L2 碰撞指向错误的定义。例如：

```
&System_Collections_Generic_IDictionary_2_int_int_class
&System_Collections_Generic_IDictionary_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode_class
```

这些带完整 arg-suffix 的 descriptor 名字是 r78 三分支 i→o 的修复产物，在 r77 中已存在但未完全对齐。

---

## 四、L2 命名碰撞与 "inheritance" 错误的因果链

```
[根因] TypeCppName 折叠多个 .NET 类型到同一 C++ 名
    ↓
[中间] generated.h 内同名 struct 多份定义（redefinition 3× each）
    ↓
[下游1] 编译器保留第一份定义，后续同名 struct 被丢弃
    ↓
[下游2] 对第二份 struct 的 static_cast 看到的是第一份定义
    ↓
[症状] "static_cast from 'X*' to 'Il2CppObject*', which are not related by inheritance"
```

**关键证据**:
- r77 全量 verdict (E:\il2cpp\r17shard\r77_full_verdict.md:4.1) 明确记录：
  > 新增失败: Microsoft.Runtime.Hosting.StrongNameHelpers.cpp — Guid ctor 签名变化（uint8_t[16] 不可 assignable）
- r77 shard1 clusters 第 27 行: `cannot cast from type 'System_Collections_Generic_KeyValuePa...'` — KeyValuePair 闭包被 L2 折叠后字段错位

---

## 五、r77 Top 10 出错的 inheritance-adjacent 错误

| 排名 | 错误消息 | 文件 | 行号 | 计数 | 根因 |
|------|---------|------|------|------|------|
| 1 | static_cast from 'WindowsIdentity*' to 'Il2CppObject*', not related by inheritance | System_Security_SecurityContext.cpp | 721,774,1092 | 3 | L2 碰撞：WindowsIdentity 同名不同类型 |
| 2 | static_cast from 'CompressedStack*' to 'Il2CppObject*', not related by inheritance | System_Security_SecurityContext.cpp | （同上） | （含上） | L2 碰撞：CompressedStack 同名 |
| 3 | cannot cast from type 'int32_t' to pointer type 'Il2CppObject*' | System_AggregateException.cpp | 610,640,899 | 3 | int32 左值当指针（L1 kind 错位） |
| 4 | cannot cast from type 'System_ArraySegment_1_int' to pointer type 'Il2CppObject*' | System_ArraySegment_1_ArraySegmentEnumerator_int.cpp | 110,111 | 2 | 值类型不可隐式转引用 |
| 5 | cannot cast from type 'System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken' to pointer | System_Lazy_1_int.cpp | （多行） | 3 | 结构体字段类型错配 |
| 6 | cannot cast from type 'System_Collections_Generic_KeyValuePair_2_object_object' to pointer | System_Collections_Generic_Dictionary_2_object_...cpp | （多行） | 4 | KeyValuePair 字段布局因 L2 错位 |
| 7 | no viable conversion from 'System_IntPtr' to 'int32_t' | Microsoft_Win32_Fusion.cpp | 160 | 1 | 嵌套值类型不可隐式转换 |
| 8 | reinterpret_cast from 'System_IntPtr' to 'const char16_t*' | Microsoft_Win32_Fusion.cpp | 162 | 1 | IntPtr→char16_t* 需中间转换 |
| 9 | static_cast from 'Il2CppObject*' to 'int32_t' | System_Collections_Concurrent_ConcurrentDictionary_2_int.cpp | 1252 | 1 | 拆箱失败 |
| 10 | reinterpret_cast from 'System_Guid' to 'uint8_t*' | （多个文件） | （多行） | 3 | Guid 字段类型不匹配 |

---

## 六、r77 与 r78 修复方向的关联

r77 验证了以下修复方向（基于 r75→r77 对比）:

| 方向 | r77 证据 | 预期 |
|------|---------|------|
| 三分支 i→o (undeclared 减少) | undeclared 1384→872 (-512) | r78 继续扩大 |
| membref 修复 | 持平 1632 | 待方案 A+B 落地 |
| L2 命名碰撞消除 | inheritance 错误仅 3 条（下游症状） | 需 TypeCppName 编码实参 |
| no-matching 扩大 wiring | +6 抖动 | 每批减少 50-100 |

---

## 七、结论

1. **r77 不存在独立的 "cannot convert to base class" 错误族**。所有与 inheritance 相关的 static_cast 错误（共 3 条）均为 **L2 命名碰撞的下游症状**。

2. **真正的 "base class" 错误隐藏在 redefinition 中**：r77 的 18 个重复 struct 名（Dictionary_2_object_object ×3, Node_int ×2 等）是同一 C++ 名被多份定义，TU 编译器报错为 redefinition 而非 "cannot convert to base class"。

3. **修复优先级**: 
   - P0: 消除 redefinition（TypeCppName 唯一化）→ 消除 18 个重复 struct
   - P1: 消除 membref（FieldAccess EnsureReceiverBcl）→ 消除 1632 条 membref
   - P2: 消除 cannot-init i→o（reinterpret_cast int→intptr 簇）→ 消除 ~445 条

---

## 八、原始证据索引

- E:\il2cpp\r77shard\r77shard1_shard1_clusters.txt (34 行簇)
- E:\il2cpp\r77shard\r77shard2_shard2_clusters.txt (34 行簇)
- E:\il2cpp\r77shard\r77shard3_shard3_clusters.txt (33 行簇)
- E:\il2cpp\r17shard\r77_full_verdict.md (142 行 verdict)
- E:\il2cpp\r77shard\r77shard1_shard1_errors.log (34068 行原始错误)
- E:\il2cpp\r77shard\r77shard2_shard2_errors.log
- E:\il2cpp\r77shard\r77shard3_shard3_errors.log
- E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:602-609 (TypeCppName)
- E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:637 (Sanitize)
- E:\il2cpp\src\il2cppc\MetadataLoader.cs:1765-1791 (ClosureArgShortName)
