# DCE-1 施工单：生成代码死代码消除（DCE）— 调查报告 + 实施方案

- 日期：2026-08-06；分支 m1；调查代理（只读，未改任何 src/）
- 用户硬性原则 #4：**生成代码无死代码**，保质保量、清晰明了
- 关联：memory.md 硬性原则 4【DCE-1 施工单】；docs/audit/stackframe-audit.md（P0/P1-1 前作）

---

## 0. 执行摘要

1. 从 6 个真实生成文件收集了 5 类死代码形态样本（见 §1），其中 **sbchain t4（声明+GC 根注册+赋值、从不读取）** 是最典型的未使用槽样本，**exceptions ctor_nop 对** 是最典型的死语句样本。
2. 管线现状（§2）：**不存在基于活跃性的 DCE pass**。已有的死代码机制是三个局部件：StackFold.OptimizeTemps 单赋值+单使用+下一条语句 peephole、Normalize 常量折叠、发射期 reachability（死分支/死块）。缺口 = 零使用槽的删除、复制链的传播。
3. 调查附带发现 **2 个真实 bug**（§3）：
   - delegateinst 生成码引用未声明 `t4`/`t31` —— 根因 **TempSlotPass.RewriteExpr 未处理 DelegateNewExpr.Target**（`default: return e`），rename 后 Target 保留旧名。
   - numchain 生成码引用未声明 `loc12`~`loc22` —— 根因 **CodeGenerator 局部声明扫描只扫 CallStmt 顶层 LocAddrExpr，漏嵌套 LocAddrExpr 与 AssignStmt.Value 里的 LocExpr 读**。
4. 实施方案（§4）：新建 `src/il2cppc/Opt/DcePass.cs`，两阶段（死存储消除 DSE + 纯值复制传播）迭代到不动点，插在 `StackFold.Fold` 之后、`Normalize.FoldStmts` 之前（CodeGenerator.cs:413 区域，一行接线）。边界清单见 §4.4（GC 根槽、地址取址、join 槽、guid/dec 槽、cstr 配对 free、Div/Rem 抛出等**不可删**项）。

---

## 1. 死代码形态清单（真实生成码样本）

样本来自 `tests/cases/<case>/gen/src/*.cpp`（编译器实际输出；注意各 gen 目录为不同构建时刻产物，接线上后需复跑验证）。

### 1a. 未使用的局部变量/槽（声明了从不读）

**样本 A — sbchain `t4`（最典型）**：`tests/cases/sbchain/gen/src/Cases_SbChainCase.cpp`

```
:26  Il2CppObject* t4 = nullptr;                    // 声明
:30  void* __frameRefs[] = { ..., (void*)&t4 };     // 注册为整方法 GC 根（refCount=5）
:84  t4 = (Il2CppObject*)(icall_stringbuilder_append_format_2(t1, g_strPool[14], t2, t3)); // 唯一赋值
```
`t4` 全文 0 次读取（已 grep 验证：仅 26/30/84 三行）。来源：C# `sb.AppendFormat(...)` 的返回值被丢弃（IL `pop`）——StackFold 仍物化 `t4 = <call>`。**调用有副作用必须保留，但存储、槽声明、GC 根条目全部是死的**。

**样本 B — exceptions 死赋值对**：`tests/cases/exceptions/gen/src/Cases_Exceptions.cpp:36-37`（同形 52-53/61-62/89-90/168-169）
```
:36  t0 = nullptr;                                   // IL newobj —— 折叠成 null（不分配）
:37  il2cpp::runtime::il2cpp_object_ctor_nop(t0);    // 对 null 的无操作调用
```
即 `throw new X()` 的折叠产物（audit generated-code-audit.md §3.10 已记录）。`t0` 仅被 ctor_nop 消费、ctor_nop 是运行时 no-op：**整对可删**（赋值删、nop 调用按"参数全静态已知 + 无副作用白名单"删）。

### 1b. 不可达语句

- 发射期 reachability（CodeGenerator.cs:534-558）已处理 goto/ret/常量真分支后的不可达语句与死 label——生成码层面基本无残留。**例外**：死块内语句仍留在 IR 列表并被 emit 跳过，但**发射期声明扫描只统计 reachable 语句**（CodeGenerator.cs:565），所以死块不产生声明泄漏。
- 常量假分支消除后**残留的供给赋值**（branch 样本见下）：分支被折掉，喂给它的 `t0 = 5LL` 仍发射（t0 尚被复制链消费 → 这是 1c 的问题）。

### 1c. 冗余中间槽（写读往返）

**样本 C — branch 复制链**：`tests/cases/branch/gen/src/Cases_Branch.cpp`
```
:38  t0 = 5LL;                 // 原始常量（其常量假分支已消除，:39 注释）
:41  t1 = t0;                  // br.s 拷贝
:44  t2 = t1;                  // dup 拷贝
:45  if((t1 == 5LL) == 0LL) goto L_43;   // 条件用 t1
:47  t3 = t2;  :51  t3 = t2;   // dup 拷贝
:53  t4 = t3;  :56  t5 = t4;  :60  t5 = t4;
:62  t6 = t5;  :64  t7 = t6;  :68  t7 = t6;
:71  write_line_i(t8);  :72  if((t7 < 5LL) == 0LL) goto L_107;
```
9 个 int32 槽（t0-t8）+ 8 条拷贝，实际只是一个值（5→t8 分支常量）。StackFold.OptimizeTemps 的复制规则要求"单赋值+**单使用**+下一条语句消费"——这里 t1 用了 2 次（t2 拷贝 + 条件），规则不触发。**纯值复制传播 + 常量传播后，Normalize 能把 `(5==5)==0` 折成常量 → 死分支消除**。

**样本 D — delegateinst 立即拷贝**：`Cases_Delegateinst.cpp:139-140`（同形 153-154/171-172/184-185/196-197）
```
t1 = icall_delegate_combine(loc6, t0);
loc6 = (Il2CppObject*)(t1);        // 单使用立即拷贝（带 cast 包装）
```
t1 单赋值单使用 → OptimizeTemps 规则 2/3 应触发？规则 3 只处理值类型 CallExpr 结果（HasRefResult 排除 ref 结果），规则 2 要求 `copy.Value is LocExpr`——这里是 `(Il2CppObject*)(t1)` cast 包装？IR 里 cast 是... 生成码显示 cast 是发射期加的。IR 中 `loc6 = t1` 应为纯 LocExpr → 规则 2 需"下一条语句消费"（use == i+1）——139 与 140 相邻，应命中。**需复跑确认该样本是否已被 OptimizeTemps 消掉**（gen 目录可能是旧产物；接线上后验证）。

### 1d. 死帧（refs 数组含从不跨分配点的槽）

**样本 E — branch `t9`**：`Cases_Branch.cpp:28,31,73-78`
```
:28  Il2CppObject* t9 = nullptr;
:31  void* __frameRefs[] = { (void*)&t9 };   // 整方法根
:73  t9 = g_strPool[6];  :76  t9 = g_strPool[7];
:78  write_line_s(t9);                        // 唯一读（在调用点读）
```
t9 活区间 = 语句 73..78，仅跨 1 个调用（:78 读取发生在调用参数求值 → 必须保留根）。**真正可退的根**：sbchain t4 类（活区间为空）——DCE 删掉死存储后根条目自动消失（refLocals 来自声明扫描）。更进一步的"只注册跨分配点的根"（GC-Root-1 全量）见 §4.4 边界 7——本单不做，保守整方法根是运行时契约。

### 1e. 其他异味

1. **`(x == c) == 0LL` 双重否定包装**：`Cases_Branch.cpp:45,72`、`Cases_Loop.cpp:33`（`if((loc4 > 100LL) == 0LL) goto L_7;`）、`numchain Program.cpp` 大量（`== 0LL) goto`）。Normalize 只折叠双常量；`(x==c)==0` 的 Ceq+const 折叠未覆盖。**DCE 不解决**（属 Normalize 增强：Ceq/Clt 等与常量 0 的复合折叠），列为可选后续。
2. **goto 紧跟目标 label**（`goto L_27; L_27:`，Cases_Branch.cpp:42-43）：冗余无条件跳转。发射可达性不删（label 被引用则重置可达）。DCE 可顺手删"无条件 goto 目标 = 下一条语句"——安全（CFG 等价）。列为 Phase 2 可选。
3. **重复表达式（源级）**：numchain `icall_string_parse_int(pool[44])` 连续语句重复求值（Program.cpp:379-380 等）、`loc4+loc5` 三连（:97-99）、`icall_delegate_equals(loc1, loc1)` 自比较（Cases_Delegateinst.cpp:117）。均为源级独立 IL 调用序列（C# 两个 WriteLine），**非转译器死代码**；CSE 需 icall 纯性表，超出本单。
4. **静态字段守卫 `if (X_cctor_done == false) ensure_cctor_X();` 重复**（Cases_Cctor.cpp:29-36）：语义正确，非死代码。

### 死代码形态结论表

| 类别 | 样本（文件:行） | 现机制 | DCE 动作 |
|---|---|---|---|
| a 未使用槽 | sbchain:26,30,84；exceptions:36-37 | 无 | 删存储/降级 ExprStmt，槽+根自动消失 |
| b 不可达语句 | 发射期已处理（无 IR 残留） | reachability | 无需（emit 已做） |
| c 冗余中间槽 | branch:38-72；delegateinst:139-140 | OptimizeTemps 单使用 peephole | 纯值复制传播+常量折叠 |
| d 死帧 | sbchain t4 根条目；branch t9（保留） | 无 | 删死存储后根自动收缩；整方法根保留 |
| e 异味 | (x==c)==0；goto-next；重复表达式 | 无 | Phase2 可选：goto-next 删除；其余不属 DCE |

---

## 2. IR 管线现状（插入点分析）

### 2.1 每方法管线（CodeGenerator.Generate，CodeGenerator.cs:397-433）

```
StackFold.Fold(insns, ctx, ...)                       // :397  IL→IR（含内部优化，见下）
  └─ 内部：OptimizeTemps(stmts)                       // StackFold.cs:2983 3 条 peephole
  └─ 内部：HoistLoopGuards / ApplyTailCallLoop        // :997
Normalize.FoldStmts(stmts, regions)                   // :417  代数恒等+常量折叠+强度削减
StringPoolPass.FoldConcatConstants / Collect          // :423-426
TempSlotPass.Rename(stmts)                            // :431  槽复用（已接线）
EmitBody(...)                                         // :433
  ├─ reachability：goto/ret/常量真分支后不可达、死 label 不发射  // :534-558
  ├─ 局部声明扫描（含 LocAddrExpr 特例）              // :563-579
  ├─ (void)argN 消未用参数警告                        // :458-464
  └─ locKind 逐名类型推断（temp 声明/GC 根依据）       // :584-671
WrapFrameRoots(body, refLocals, ...)                  // :474  ref 槽整方法 GC 根
```

### 2.2 已有死代码机制盘点（与 DCE 的关系）

| 机制 | 位置 | 覆盖 | 缺口 |
|---|---|---|---|
| OptimizeTemps 规则 1 box→unbox 恒等 | StackFold.cs:2999-3014 | 单赋值+单使用+下一条 | 多使用链不覆盖 |
| 规则 2 纯拷贝重命名 | :3017-3029 | 同上 | 同左（branch 链因 t1 双使用不触发） |
| 规则 3 值类型 call 前向替换 | :3032-3039 | 同上 | ref 结果/多使用不覆盖 |
| P0-2 死存储删除 | :970-983 | 仅常量传播过的**单赋值**局部 | 零使用 temp 全覆盖缺口 |
| Normalize 常量折叠 | Normalize.cs:306-428 | 双常量/恒等/已知非空 | 无 `(x==c)==0` 复合；无复制传播 |
| 发射期 reachability | CodeGenerator.cs:534-558 | goto/ret/常量真分支死块、死 label | 无存储级死代码 |
| TempSlotPass.Rename | TempSlotPass.cs:77 | 槽复用（活跃区间着色） | **零使用槽仍被着色保留**（spanEnd=-inf 也进候选池） |

**结论：无基于活跃性的 DCE pass**（stackframe-audit.md:107 记载"temp 生命周期分析 / 死 temp 消除 ❌ 不存在"——槽复用已由 TempSlotPass 落地，前两项仍缺）。IR 已具备 DCE 所需全部信息：AssignStmt/ExprStmt/RetStmt/BranchStmt/CallStmt/FieldAssignStmt 六类语句 + LocExpr/LocAddrExpr 读/取址区分（TempSlotPass.CollectUses 就是现成的 def/use 收集器，可镜像）。

### 2.3 插入点

**`CodeGenerator.cs:413` 之后、`Normalize.FoldStmts`（:417）之前**，一行：
```csharp
stmts = DcePass.Run(stmts).Stmts;   // 插在 :417 前
```
理由：
- 复制传播必须先于 Normalize——传播后的常量（branch 样本 `5`）喂给代数折叠 → 折叠出常量条件 → 发射期 reachability 消除死分支（折叠链闭环）。
- DSE 在本 pass 内部与传播迭代到不动点，无需依赖 Normalize 之后的第二轮。
- 在 StringPool/TempSlot 之前 → 池扫描与槽复用的输入更小（t4 不再占槽）。

接线代价：CodeGenerator.cs 一行（该文件在 plan.md 域规则中标注共享/只读，本施工单需显式包含这一行，或由 CodeGenerator 域代理代接）。备选：在 StackFold.cs:997 返回处包装（StackFold 在可写域），但破坏"Opt pass 独立成文件 + 单点接线"惯例，不推荐。

### 2.4 与 TempSlotPass 的交互（关键）

- DCE 先删死存储 → Rename 的候选集不含死槽（现在 spanEnd=int.MinValue 的死槽仍被着色保留，浪费槽号）。
- **必须同时修复 TempSlotPass.RewriteExpr 的 DelegateNewExpr 缺口**（§3.1）——否则 Rename 继续产生未声明引用，DCE 的完整性检查会先行炸掉。

---

## 3. 附带发现：两个真实 bug（DCE 完整性检查的动机）

### 3.1 delegateinst 未声明 t4/t31 —— TempSlotPass.RewriteExpr 缺 DelegateNewExpr 分支

生成码：`Cases_Delegateinst.cpp:91`（`il2cpp_delegate_new(..., t4)`）、`:131`（`...t31)`）——t4/t31 无声明（声明表 :58-71 只有 t0/t1）。

根因链：
1. StackFold 折叠 delegate ctor 时创建 `DelegateNewExpr { MethodToken, Target }`（StackFold.cs:2041-2042；Expr.cs:197-200 `Target` 为 `required Expr`，即目标 temp 名）。
2. `TempSlotPass.Rename` 把目标 temp（原名 t4/t31）着色复用为 t0/t1——defs/普通 LocExpr 引用全部改名。
3. **`RewriteExpr` 的 switch 无 DelegateNewExpr 分支，落 `default: return e`**（TempSlotPass.cs:376-377）→ Target 保留旧名 t4/t31 → 生成码引用未声明变量 → C++ 编译失败（discover.md #12 记载的"temp 槽错位"）。

修复（5 行）：RewriteExpr 增加
```csharp
case DelegateNewExpr d when rename.Count > 0:
    return new DelegateNewExpr { MethodToken = d.MethodToken, Target = RewriteExpr(d.Target, rename) };
```
（注意 DelegateNewExpr 构造——含 MethodToken；需核对 Expr.cs:197 全部字段。）

### 3.2 numchain 未声明 loc12-loc22 —— 局部声明扫描漏嵌套

生成码：`numchain/gen/src/Program.cpp:350-378`（`(int32_t*)(&loc12)` 等 out 参数 + :343 `t125 = (loc10 == -42LL)` 读），声明表 :36-62 无 loc12-loc22。

根因：CodeGenerator.cs:563-579 声明扫描只处理：
- `AssignStmt a` 的 `a.Target`（:566）
- `AssignStmt.Value is LocAddrExpr`（:571）
- `CallStmt vc.Args` **顶层** LocAddrExpr（:573-577）

漏掉：**(a) CallExpr 参数里的嵌套 LocAddrExpr**（如 `t0 = icall_format_int32_custom((*(int32_t*)(&loc27)), ...)` 是 AssignStmt→CallExpr→LocAddrExpr）；**(b) AssignStmt.Value 里 LocExpr 读**（`t125 = (loc10 == ...)`）。两者都导致引用未声明局部。

修复：声明扫描改为**递归表达式遍历**（对每个 AssignStmt.Value / CallStmt 参数 / BranchStmt.Condition / RetStmt.Value / FieldAssignStmt 递归收集 LocExpr 读 + LocAddrExpr）。注意与 DCE 共用同一递归遍历器（DRY）。

> 这两个 bug 是"未声明引用"而非死代码，但 **DCE 的完整性检查（§4.3 检查 2：每个 LocExpr/LocAddrExpr 名必须有 ≥1 def）** 会在转译期捕获这一类问题——这正是把完整性检查纳入 DCE 的最大价值。

---

## 4. DCE pass 实施方案

### 4.1 文件与签名

新建 `src/il2cppc/Opt/DcePass.cs`（仿 TempSlotPass/LicmPass 惯例：pass-only、不碰 StackFold/CodeGenerator 内部、头注释英文、返回"无变化则返回同一列表实例"）：

```csharp
public static class DcePass
{
    // Run: dead-store elimination + pure-value copy propagation, iterated to
    // a fixpoint. Returns the SAME list instance when nothing changed.
    public static (List<Stmt> Stmts, int StoresRemoved, int CopiesPropagated) Run(List<Stmt> stmts)
}
```
可选：`ScanAssembly(types, assemblyPath)` 预扫描报告（Program.cs 加 WriteDceReport，同 LicmPass 惯例）——统计每方法可删存储/可传播拷贝数。

### 4.2 算法（两阶段迭代到不动点）

**Pass 0 — 收集（镜像 TempSlotPass.CollectUses，补齐表达式种类）**
- defs：`AssignStmt.Target`（排除含 `.` 的静态字段目标）
- uses：`LocExpr`（读）；`LocAddrExpr`（取址 → 名进 poison 集）
- 递归遍历**全部** Expr 种类：BinaryExpr/UnaryExpr/CallExpr/VirtualCallExpr/DelegateInvokeExpr/FieldExpr/FieldAddrExpr/MarshalUtf8Expr/**DelegateNewExpr.Target**（教训 §3.1）/ValueTypeNewExpr
- 遍历全部语句种类：AssignStmt/ExprStmt/RetStmt/BranchStmt.Condition/CallStmt/FieldAssignStmt

**Pass 1 — 死存储消除（DSE）**
对每条 `AssignStmt X = V`（X 非静态字段目标、X ∉ poison、**X 全方法零 LocExpr 使用**）：
- V 纯（IntConstExpr/Int64ConstExpr/DoubleConstExpr/StringConstExpr/NullExpr/LocExpr/ValueTypeNewExpr/无 Div·Rem·FieldExpr 的算术，语义同 Normalize.IsPureArithmetic）→ **删除整条语句**
- V 含调用（CallExpr/VirtualCallExpr/DelegateInvokeExpr/NewObjExpr）→ **降级为 ExprStmt**（副作用/异常保留）
- V 是 FieldExpr/FieldAddrExpr → **跳过**（字段读可抛 NRE，删除改变行为；降级 `(void)` 也保留求值——保守跳过，样本罕见）
- 排除：ResultKind "guid"/"dec" 的 CallExpr 赋值（发射为"结果槽作输出缓冲"的语句形，CodeGenerator.cs:1276-1298，降级会丢缓冲）；MarshalUtf8Expr（cstr，StackFold 配对的 pinvoke_utf8_free CallStmt 引用该名——删存储须连带删配对 free，保守跳过，见 §4.4 边界 6）
- 零使用与 CFG 无关（任何路径都不读），**无需运行分析**——多 def 名（join 槽）零使用时全部 def 都可删（无读即无路径语义）

**Pass 2 — 纯值复制传播**
对每条 `AssignStmt T = V`：T 为 temp（`t\d+`）、**全方法恰好 1 个 def**、T ∉ poison、V 纯（同上纯性集）、**T 的 def+全部 use 落在同一条最大直线运行（run）内**（复用 TempSlotPass 的 run 划分：Label/Branch/Ret 为边界——回边只能重入 run 起点，run 内顺序执行保证 def 先于读）：
- 把每个 `LocExpr{T}` 替换为 **V 的深拷贝**
- **V 内操作数在 def 与 use 之间不得被重新赋值**：run 内线性扫描，若 V 引用的任何名字在区间内另有 AssignStmt → 放弃该传播（保守）
- 传播后 T 零使用 → 回 Pass 1 删掉拷贝定义

迭代 Pass1↔Pass2 至无变化。终止性：每轮至少删 1 语句或替换 1 引用，语句数有限。

**Pass 3（可选，Phase 2）— 冗余 goto**
`BranchStmt{TargetLabel=L, Condition=null}` 且下一条语句是 `LabelStmt{Name=L}` → 删 goto。CFG 等价（跳转目标即顺序后继）。

### 4.3 验证方式

**单元测试** `tests/unit/DcePassTests.cs`（仿 TempSlotPassTests：IR 级构造器 + 语义不变式断言）：
1. 零使用纯存储被删（`t0 = 5LL;` 无读 → 消失）
2. 零使用调用存储降级 ExprStmt（`t4 = append(...)` → `append(...)` 语句保留，无 t4）
3. 有使用存储保留；取址名（LocAddrExpr）不删；join 多 def 名有读不删、零读全删
4. 复制传播：单 def 常量 temp 多使用全替换（branch 链 9 槽 → 常数折叠后剩 1-2）；跨 label 不传播（run 规则）；区间内操作数重赋值不传播
5. Div/Rem/FieldExpr RHS 不删；guid/dec 槽不删
6. **完整性不变式**：Run 后每个 LocExpr/LocAddrExpr 名都有 def（§3 bug 的回归捕获）；传播后"每个读看到自己的写"（仿 AssertReadsSeeOwnWrites 最近前驱 def 检查）
7. TempSlotPassTests 补回归：DelegateNewExpr.Target 被 rename（t4/t31 场景）
8. NormalizeTests 补：`(x==5)==0` 折叠（可选 Phase 2 项）

**e2e 验证**：
- 复跑 branch/sbchain/exceptions/delegateinst/numchain/loop 的 gen：branch 槽数 9→~2；sbchain 无 t4、refCount 5→4；exceptions 无 ctor_nop 对；delegateinst/numchain 无未声明引用
- 全量 unit（现 1093）+ 矩阵 155 用例无回归；-Wall -Wextra 未用变量警告减少
- 生成码目视抽查 + 逐字节 diff 语义关键行（write_line 参数序列不变）

**验证门槛**（主代理派实施代理时写明）：`dotnet build src/il2cppc -c Release` 0 警 0 错；`dotnet test tests/unit -c Release` 全绿；3-5 个代表性用例 e2e PASS。

### 4.4 边界清单（不可删 / 必须降级）

| # | 边界 | 规则 | 依据 |
|---|---|---|---|
| 1 | 有副作用调用 | 降级 ExprStmt，不删 | .NET 语义（sbchain t4 的 append 必须执行） |
| 2 | 可抛异常求值 | Div/Rem/FieldExpr 不删不降级（保守跳过） | Normalize.IsPureArithmetic 同款论证 |
| 3 | GC 根槽（ref temp） | 死存储可删（槽零初始化，保守根契约不变）；**从不删活存储** | TempSlotPass.cs:50-53 契约 |
| 4 | 地址取址名（LocAddrExpr） | 整个名 poison，不传播不删 | 别名不可判定（TempSlotPass 同款） |
| 5 | join 槽（多 def 名） | 有读不删；**零读全删**（无路径语义） | RegisterJoin 语义 |
| 6 | guid/dec 结果槽、cstr marshal temp | 排除（发射特殊形/配对 free 调用） | CodeGenerator.cs:1276-1298；StackFold.cs:1887-1902 |
| 7 | 整方法帧根（GC-Root-1 全量） | **本单不做**注册侧优化；仅靠死存储删除自动收缩根数组 | 保守根 = 运行时契约；跨分配点注册需分配点分析，留 Phase 3 |
| 8 | 静态字段目标（Target 含 `.`） | 永不删（stsfld 语义） | Stmt.cs:13 |
| 9 | volatile/`(void)argN` | 不涉及（发射器已有未用参数处理，CodeGenerator.cs:458-464） | — |

### 4.5 实施顺序建议（供主代理拆单）

- **P0**（本施工单主体）：DcePass.cs Pass 0+1+2 + CodeGenerator.cs:417 前一行接线 + DcePassTests + e2e 验证。同时修 §3.1 TempSlotPass DelegateNewExpr（5 行，否则 DCE 完整性检查会先触发）。
- **P1**：完整性检查 fail-loud（§3 两 bug 修复后把"未声明引用"从警告升为硬错误；修复前建议 warning+跳过该方法，避免改变现有用例的 transpile 状态）。
- **P2**（可选）：Pass 3 goto-next 删除；§3.2 声明扫描递归化（CodeGenerator 域）；Normalize `(x==c)==0` 复合折叠（异味 1）。
- **Phase 3**（独立后续）：GC-Root-1 分配点感知根注册。

### 4.6 决策点（需主代理确认）

1. 完整性检查在 §3 两 bug 修复前：warning+跳过（保守，不改变现有用例状态）vs 硬错误（LOUD 政策，delegateinst/numchain 转译期即失败）。**建议前者**，bug 修复后升硬错误。
2. CodeGenerator.cs 一行接线：本施工单自带 vs 由 CodeGenerator 域代理代接（plan.md 域规则该文件标注共享）。
3. `(x==c)==0` 折叠是否纳入本单（建议 P2 单独做，不混入 DCE）。

---

## 5. 参考资料

- 管线调用点：CodeGenerator.cs:397-433（Fold→Normalize→StringPool→TempSlot→EmitBody）
- 发射期 reachability：CodeGenerator.cs:534-558；声明扫描（缺口）：:563-579
- OptimizeTemps peephole：StackFold.cs:2983-3042；P0-2 死存储：:970-983
- TempSlotPass（def/use 收集 + run 划分 + Rename）：TempSlotPass.cs:77-222；RewriteExpr 缺口：:376-377
- 既有审计：docs/audit/stackframe-audit.md（P0/P1-1）、docs/audit/generated-code-audit.md（§3.10 exceptions 死代码）
- 相关 bug 记录：discover.md #12（delegateinst t4/t31）、#20（numchain loc12-14 声明扫描）
