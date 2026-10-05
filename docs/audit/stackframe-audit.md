# 栈帧/临时变量管理审计（stackframe-audit，2026-08-03，只读）

> 范围：转译器（src/il2cppc）栈折叠（StackFold）产出的 C++ 局部变量（temp）管理。
> 方法：现版转译器（dotnet run --project src/il2cppc，构建于 19:03）重新转译
> stringops / boxing / arith / loop / valuetypes 五个用例到临时目录（未触碰仓库任何
> 文件），统计生成 C++ 的局部变量与 IL 局部之比（临时膨胀率），抽查可优化实例，
> grep 源码确认已有优化机制现状。
> 关联文档：docs/architecture/帧根优化设计.md（O1-O4，设计态）、docs/audit/generated-code-audit.md。

---

## 1. 机制：StackFold 的 temp 分配规则

- **每调用一 temp（M2 简化）**：`Push()` 对每个 `CallExpr`/`VirtualCallExpr`/`NewObjExpr`
  结果分配全新临时名 `t{count}`，单调递增，**永不复用、永不移除**（src/il2cppc/Ir/StackFold.cs:709-734，
  注释 :706 "M2 简化：每调用一个 temp"）。
- 同类分配点：分支 join 槽（StackFold.cs:751-771 `RegisterJoin`，同 label **共享**槽名——
  这是现存唯一的复用）；stloc 快照（StackFold.cs:786-810 `EmitStore`）；P/Invoke 字符串
  marshal temp（:1112）；值类型 newobj temp（:1233）。
- 发射端：全部 temp 在**函数顶部**统一声明（CodeGenerator.cs:398-483），引用 temp 一律
  零初始化并**整方法注册为 GC 根**（`WrapFrameRoots`，CodeGenerator.cs:1266-1307：
  `void* __frameRefs[] = {...}` 含全部引用局部，`refCount = N`，序言 5 行 + push）。
- 类型推断按赋值传播（`ExprKind`，CodeGenerator.cs:1053+），int64/double/值类型 struct
  各有专用声明（:457-482）。

## 2. 膨胀率实测（现版转译器新鲜产出）

| 用例方法 | IL 指令数 | IL 局部 | 生成 C++ 局部 | 临时膨胀率 | 引用 temp | GC 根槽 | 备注 |
|---|---|---|---|---|---|---|---|
| stringops `Program::Main` | 51 | 1 (loc0) | **33**（t0-t31 + loc0） | **33× IL 局部**；0.63 temp/指令 | 17 | **18** | 90.6%（29/32）temp 单用、下一条语句即消费 |
| boxing `Cases.Boxing::Main` | ~30 | 2 | 17（t0-t16） | ~8.5× | 11 | 13 | 同时活跃引用 ≤3，注册 13 |
| arith `Cases.Arith::Main` | 13 | 0 | **0** | — | — | 0 | 常量折叠全部消掉 |
| loop `Cases.Loop::Main` | ~24 | 6 | 6 | 1× | 0 | 0 | 循环全用 IL 局部，零 temp |
| valuetypes `Cases.ValueTypes::Main` | ~20 | 3 | 3 | 1× | 0 | 0 | 值类型 ctor 经 ldloca 就地，零 temp 零拷贝 |

**结论：**
- 膨胀率与"调用密度"成正比：`call/box/newarr/callvirt` 密集的方法（stringops/boxing）
  局部数膨胀到 IL 局部的 8~33 倍；纯算术（arith）与纯局部循环（loop）零膨胀（常量
  折叠 + 传播已覆盖）。
- **GC 根注册是更重的隐性成本**：stringops Main 同时活跃的引用 ≤3，却注册 18 个根槽
  （每引用 temp 整方法生命期）；boxing 同时活跃 ≤3、注册 13。根数组大小与全方法扫描
  成本正比于 temp 总数而非真实活跃度。
- 序言额外成本：16 个 `= nullptr` 零初始化 + 5 行帧序言（stringops Main）。

## 3. 三处可优化实例（生成代码实证）

### 实例 1：box→unbox 往返，t 单用死分配（P0）
boxing `Cases_Boxing.cpp:47-48`（同形重复 3 次，:50-55；stringops 亦有 t10/t11、t12/t13）：
```cpp
t0 = il2cpp::il2cpp_box_int32(nullptr, 5LL);   // IL box @0x0001
t1 = il2cpp::il2cpp_unbox_int32(t0);           // IL unbox.any @0x0006
il2cpp::runtime::icall_console_write_line_i(t1);
```
t0 的唯一消费是紧随其后的 unbox——新分配的对象被立即读回，**box 分配本身是死工作**
（int32 box→unbox 恒等）。折叠为 `t1 = 5LL` 可省：1 次 GC 分配 + 1 个 temp + 1 个根槽，
共 3 处同形（boxing）+ stringops 1 处（box 42 被 concat_int 内 unbox）。

### 实例 2：分支臂 join 拷贝链（单用源臂拷贝消除）（P1）
boxing `Cases_Boxing.cpp:67-73`（`d is Dog ? d.Speak() : 0` 三元）：
```cpp
t10 = vcall(loc1)      // 真臂 @0x0067
t11 = t10;             // ← 冗余：t10 单用，仅被 join 拷贝消费
goto L_111;
L_110:
t11 = 0LL;             // 假臂混合源
L_111:
il2cpp::runtime::icall_console_write_line_i(t11);
```
`RegisterJoin` 已共享 t11 槽（两臂归一化正确），但真臂仍经中间 temp t10 拷贝——
可直接 `t11 = vcall(loc1)`，省 1 temp + 1 拷贝。这是帧根优化设计 §4.2 Merge B 的推广
（B 只覆盖"全臂同源"；本例是"混合源 + 单用源臂"，需单用源规则）。

### 实例 3：单用 isinst/调用 temp 前向替换 + 拷贝链合并（P0）
boxing `Cases_Boxing.cpp:59` 与 stringops 全篇（29/32 temp 同形）：
```cpp
t7 = isinst(loc0, &Dog_class);                 // 单用
il2cpp::runtime::icall_console_write_line_i((t7 != nullptr));   // 下一条即消费
```
及 stringops `Program.cpp:136-137` 拷贝链：
```cpp
t30 = (Il2CppObject*)(il2cpp::runtime::icall_string_copy(...));  // @0x01FA
loc0 = t30;                                    // @0x01FF  ← 仅拷贝，可合并
```
`t = f(...); 下一语句用(t)` 与 `t = f(); locY = t;` 均可做**单用前向替换/目标重命名**
（t 恰 1 次使用 → 直接把 f 内联到使用点，或赋值目标改为 locY）。实测 32 个 temp 中
29 个单用（90.6%），其中 15 个是纯 int32 标量（t0-t5、t22-t29、t31——内联无 GC
风险），3 个是 box temp（t10/t12/t14——P0-2 消除），1 个是拷贝链源（t30→loc0——
重命名即消除，无内联风险）。P0-1+P0-2 合计：stringops Main 32 temp → ~13，
根槽 18 → ~14。

**结构体复制**（任务第 3 点）：valuetypes `Cases_ValueTypes.cpp:27` `loc1 = loc0;` 是
整结构（Point=8B）拷贝——这是 .NET 值语义**必需**的快照（随后 `(&loc1)->X = 100` 改
副本），不可改引用；小 struct（≤16B）在 -O2 下为寄存器级拷贝，开销可忽略。DateTime/
TimeSpan（8B）同理。**结论：struct 复制现状无显著问题，不建议动**（大 struct 场景
尚未出现，见 P2-1）。

## 4. 已有机制现状（grep 实证）

| 机制 | 状态 | 证据 |
|---|---|---|
| 常量折叠（int32 wrap 语义）+ 代数恒等 | ✅ 已实现 | `TryFoldInt32`（CodeGenerator.cs:1484+）；Normalize.cs 全篇；arith 实测 0 temp |
| 常量局部传播 + **死存储删除** | ✅ 已实现（局限：仅 `IntConstExpr` 值的局部） | StackFold.cs:684-701（P0-2） |
| join 槽跨臂共享 | ✅ 已实现 | StackFold.cs:751-771（同 label 复用槽名） |
| 值类型 ctor 就地初始化（ldloca 路径） | ✅ 已实现（零 temp 零拷贝） | valuetypes 实测；CodeGenerator.cs:757-781 |
| 死分支/死块消除（发射期 reachability） | ✅ 已实现 | CodeGenerator.cs:239/322/713 |
| null 比较常量折叠（knownNonNull） | ✅ 部分（只折常量结果，不消 `il2cpp_null_check` 调用） | Normalize.cs:163-174 |
| **temp 生命周期分析 / 死 temp 消除 / 跨语句槽复用** | ❌ **不存在** | StackFold Push :713 单调 `t{count}`；grep `liveness|LiveRange` 零命中 |
| box→unbox 往返折叠 | ❌ 不存在 | — |
| 单用 temp 前向替换 | ❌ 不存在 | — |
| 帧根跳过（O1）/ Merge A/B（O2）/ K 集 null_check 消除（O3） | ⚠️ **仅设计，未实施**（无对应测试） | docs/architecture/帧根优化设计.md §1；tests/unit 无 Merge/JointSlot 测试 |

## 5. 优化建议清单

**P0（高收益低风险，独立小 pass）**
- **P0-1 单用 temp 前向替换/目标重命名**（StackFold 尾后 pass 或发射前）：
  `t = f(...)` 且 t 恰 1 次使用 → 内联/重命名，删 temp 与声明。值类型无条件；
  **引用类型需 GC 安全论证**——内联后值不再位于被注册的栈槽，若消费点是运行时调用
  且 callee 内分配触发 GC，对象可能不可达。保守子集：仅当 t 与消费语句之间无分配
  点（复用帧根优化设计 §3.2 的 MayTriggerGc 白名单思路）；或与 O1（GC-free 方法）
  联动。实测 29/32 单用 temp 中：15 标量无条件 + t30 拷贝链重命名（无内联）先行；
  引用 temp 内联留待 P2-3 白名单。预期：stringops 32→~17 temp、根槽 18→17（标量
  不注册根，根槽压缩主要靠 P1-1 槽复用）。
- **P0-2 box→unbox 往返折叠**：`t=box(c); use(unbox(t))` 且 t 单用 → `use(c)`。
  省分配 + 根槽 + temp 三合一（box 结果是引用 temp，消除即减 1 根槽）。3+1 处实测
  同形；stringops t10/t12/t14 一并消除（含根槽 18→15）。

**P1（中收益）**
- **P1-1 栈槽复用（活跃区间重命名）**：块内做简单活跃分析，按语句序复用槽名
  （t6 死后 t7 复用 t6 槽）。**引用 temp 槽复用自动压缩帧根数组**（stringops 18→~4，
  与注册机制天然兼容：根按槽注册）。落点：StackFold 尾后重命名 pass 或发射端
  `locKind/declared` 处按区间重新编号。
- **P1-2 单用源臂 join 拷贝消除**（Merge B 推广，实例 2）：混合源 join 中"单用源臂"
  的 `tX = vcall(...); tY = tX` → `tY = vcall(...)`。
- **P1-3 实施已设计的 O1/O2**（帧根优化设计.md §3-4）：O2 Merge A（`t=newobj; ctor(t);
  loc0=t` 两代拷贝，boxing:56-58 实测同形）；O1 GC-free 方法跳过帧——两者直接减少
  根槽与序言。

**P2（低优先级/需更多用例）**
- **P2-1 struct 复制**：维持现状（IL 值语义要求快照；当前 struct ≤8B 无成本）。
  若未来出现大 struct（含引用字段），做"拷贝后未读字段即覆写"的部分拷贝分析，
  并保证写屏障语义不丢。
- **P2-2 声明位置局部化**：temp 声明靠近使用点有助于 C++ 编译器寄存器分配；但
  与帧根数组（地址稳定）和 RAII 帧析构时机耦合，需与 O1 帧跳过同时设计。
- **P2-3 引用 temp 内联白名单扩展**：把 isinst/castclass/ldlen 等 GC-free 调用结果
  的内联子集逐步放开（每项配 MayTriggerGc 证明）。

## 6. 一句话结论

栈折叠当前"每调用一 temp、函数顶全声明、引用 temp 整方法根化"；膨胀率在调用密集
方法达 8~33× IL 局部（0.63 temp/指令），且 GC 根槽数 ~6× 真实活跃度；最直接的修复
是 P0-1 单用 temp 替换 + P0-2 box→unbox 折叠（纯 IR pass，可测试、无 GC 风险面需
白名单约束），随后 P1-1 槽复用把根数组压缩到活跃度量级。
