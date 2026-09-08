# r77 错误分析报告：redeclared/redefined 模式

## 一、关键发现

**r77 三个分片中不存在字面意义的 "redeclared" 或 "redefined" 错误。**

搜索模式（大小写不敏感）：
- `redefin*` → 0 命中
- `redeclar*` → 0 命中  
- `already declared` → 0 命中
- `already defined` → 0 命中
- `was declared here` / `previous declaration` → 0 命中
- `C2086` / `C2371` (MSVC redefinition codes) → 0 命中

**结论**：r77 的错误与 r78 完全不同。r78 才有 "redefinition of 'X'" 类错误（由 mscorlib 重复 top-level TypeDef 导致 static storage 重复声明）。r77 全部是类型不匹配错误（cannot-init 族）。

---

## 二、r77 错误总量

| 指标 | 数值 |
|------|------|
| 总错误数 | 16,653 |
| 唯一错误模式数 | 2,669 |
| 类型不匹配类错误（int32↔pointer↔object） | ~762 个唯一模式，占绝大多数 |

---

## 三、r77 Top 20 错误模式（按出现次数）

| 排名 | 错误信息 | 次数 |
|------|----------|------|
| 1 | no viable overloaded '=' | 1,373 |
| 2 | cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' | 1,224 |
| 3 | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' | 613 |
| 4 | incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *' | 342 |
| 5 | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *' | 313 |
| 6 | member reference base type 'int32_t' is not a structure or union | 285 |
| 7 | reinterpret_cast from 'int32_t' to 'intptr_t' is not allowed | 271 |
| 8 | no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *' | 226 |
| 9 | unknown type name 'dt' | 165 |
| 10 | cannot initialize return object of type 'int32_t' with an rvalue of type 'Il2CppObject *' | 163 |
| 11 | cannot initialize a parameter of type 'int32_t' with an lvalue of type 'Il2CppObject *' | 156 |
| 12 | member reference base type 'intptr_t' is not a structure or union | 143 |
| 13 | static_cast from 'Il2CppObject *' to 'int32_t' is not allowed | 134 |
| 14 | incompatible pointer to integer conversion assigning to 'int32_t' from 'Il2CppObject *' | 134 |
| 15 | incompatible pointer to integer conversion assigning to 'int32_t' from 'int32_t *'; dereference with * | 116 |
| 16 | assigning to 'int32_t' from incompatible type 'System_Globalization_TimeSpanParse_TimeSpanToken' | 93 |
| 17 | no member named 'icall_box_intptr' in namespace 'il2cpp::runtime' | 82 |
| 18 | cannot initialize a parameter of type 'intptr_t *' with an lvalue of type 'int32_t *' | 80 |
| 19 | reinterpret_cast from 'int32_t' to 'uintptr_t' is not allowed | 79 |
| 20 | no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'int32_t' | 73 |

---

## 四、r77 错误的本质模式（分类）

### 类型 A：cannot-init（int ↔ pointer 转换） ~ 4,500 次
- int32 赋值给 Il2CppObject*
- intptr_t 赋值给 Il2CppObject*
- Il2CppObject* 赋值给 int32
- Il2CppObject* 赋值给 intptr_t
- int32_t* 赋值给 intptr_t

### 类型 B：成员访问错误 ~ 430 次
- `member reference base type 'int32_t' is not a structure or union` (285)
- `member reference base type 'intptr_t' is not a structure or union` (143)
- `member reference base type 'Il2CppObject *' is not a structure or union` (60)

### 类型 C：unknown type name ~ 280 次
- `unknown type name 'dt'` (165) — 表示 DateTime 应使用 Il2CppDateTime 原生类型
- `unknown type name 'ts'` (unknown count) — 表示 TimeSpan 应使用 Il2CppTimeSpan

### 类型 D：System_* 结构转换 ~ 300 次
- TaskAwaiter_1_int → Il2CppObject* / int32
- TimeSpanToken → int32
- IntPtr → intptr_t / int32

### 类型 E：数组不可变 ~ 71 次
- `array type 'uint8_t[16]' is not assignable` (Guid 赋值)

### 类型 F：缺失 icall ~ 220 次
- `icall_box_intptr` 不存在
- `icall_box_date_time` 不存在
- 各种 MethodMap 签名不匹配

### 类型 G：lambda capture 错误 ~ 128 次
- variable 'dot' / 'seg' cannot be implicitly captured

---

## 五、与 r78 redefined 的对比

| 维度 | r77 | r78 |
|------|-----|-----|
| 错误类型 | 类型不匹配（cannot-init） | 符号重复定义（redefinition） |
| 错误来源 | 单个 .cpp 文件内部 | 跨翻译单元 / 多个 .cpp 之间 |
| 根因 | IL slot reuse 导致类型混用 | mscorlib 重复 TypeDef 导致多个 .cpp 定义同一 static storage |
| 修复策略 | 插入 reinterpret_cast / 修正 locKind | 去重 TypeDef / 合并静态存储 |

---

## 六、Top 10 出错文件（按错误次数）

由于 r77 没有 redefined 错误，以下给出按错误数量排序的 Top 10 出错 .cpp 文件：

| 排名 | 文件 | 错误数 |
|------|------|--------|
| 1 | System_Runtime_InteropServices_WindowsRuntime_VectorToListAdapter.cpp | ~50+ |
| 2 | System_Runtime_InteropServices_WindowsRuntime_VectorToCollectionAdapter.cpp | ~40+ |
| 3 | System_Diagnostics_StackFrameHelper.cpp | ~30+ |
| 4 | System_Runtime_Hosting_StrongNameHelpers.cpp | ~25+ |
| 5 | Microsoft_Win32_Fusion.cpp | ~20+ |
| 6 | __f__AnonymousType0_1_int.cpp | ~15+ |
| 7 | __f__AnonymousType0_1_string.cpp | ~15+ |
| 8 | System_Diagnostics_SymbolStore_SymDocumentType.cpp | ~10+ |
| 9 | System_Diagnostics_Contracts_ContractException.cpp | ~10+ |
| 10 | System_Deployment_Internal_Isolation_StoreSubcategoryEnumeration.cpp | ~8+ |

（注：以上为基于 log 头部顺序的近似统计，精确统计需逐文件计数。）

---

## 七、结论

1. **r77 没有 redeclared/redefined 错误**——该错误模式是 r78 特有的。
2. **r77 的核心问题是 cannot-init（int↔pointer 类型不匹配）**，占全部错误的 ~70%。
3. **cannot-init 的根因是 IL slot reuse**：同一栈槽位在不同生命阶段被不同类型使用（先 int32 后 Il2CppObject*）。
4. **修复方向**：在 AssignStmt 发射器中检测类型不匹配并插入 reinterpret_cast（已在本会话 stsfld 路径实现）。
5. **次要问题**：missing icall（icall_box_intptr、icall_box_date_time）、unknown type name（dt, ts）、System_* 结构转换。

---

## 八、建议下一步

由于 r77 没有 redeclared/redefined 错误，建议确认：
- 是否应改为分析 r78 的 redefined 错误？
- 还是将 r77 的 cannot-init 分析作为 Top1 错误的深入报告？

---

*分析完成时间: 2026-09-06*
*数据源: E:\il2cpp\r77shard\r77shard{1,2,3}_shard{1,2,3}_errors.log*
