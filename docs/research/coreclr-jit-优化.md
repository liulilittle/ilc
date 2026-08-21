# coreclr JIT 优化技术研读 → 本项目（ILC 转译器）优化清单

> 研究任务（铁律 2l）：用户要求"学习 .NET Core 里面的 IL、JIT 优化技术"。
> 本研究基于 dotnet/runtime **main 分支**（2026-08-03 拉取）`src/coreclr/jit/` 源码，对照本项目转译器 `src/il2cppc/`（AOT：IL→C++，再由 VC++/GCC 编译）逐项分析。
> 定位：本项目的正确做法不是"移植 JIT 优化器"，而是**在 IL 折叠阶段把 C++ 编译器不知道的 .NET 语义信息（unchecked 回绕、null 检查、cctor 守卫、封闭世界类型集）提前消化，并让生成代码的形状对 C++ 优化器友好**。凡 C++ 优化器（-O2 的 GVN/常量传播/寄存器分配/循环优化）已覆盖且生成代码形状不阻碍其发挥的，一律不做（YAGNI + C7）。

## 0. 结论速览（Top 5 可落地优化）

| # | 优化 | 优先级 | 预期收益量级 | 核心依据 |
|---|------|--------|--------------|----------|
| 1 | **属性折叠落地**（get/set → 字段访问） | P0 | 热路径访问器调用 100% 消除（每调用省 call+帧表 push/pop 两三次间接开销） | 铁律 2j + 已有设计 docs/architecture/属性折叠设计.md；IL2CPP_NOINLINE（[本:MethodEmitter.cs:13]）下唯一合法 accessor 消除 |
| 2 | **代数恒等式/规范化 pass**（x+0、x*1、x^0、x-x、常量合并 a+2+3、负负消去） | P0 | 算术密集代码产物体积 -5~15%；-O0 与 -O2 双受益；生成代码美感（铁律） | coreclr fgMorphSmpOp 的简单操作规范化（[coreclr:morph.cpp:6986]） |
| 3 | **常量局部传播**（ldc→stloc→ldloc 链替换为常量） | P0 | 连锁触发分支定死+死块消除；Roslyn 常量模式常见 | coreclr assertionprop 的传播思想（[coreclr:assertionprop.cpp:6651]），本项目已有 TryFoldInt32 基座（[本:CodeGenerator.cs:1133]） |
| 4 | **封闭世界去虚拟化**（非虚 callvirt 直接调用已有；扩展到 final 方法/单实现接口） | P0 | 热路径 callvirt 点省 null check 之外的一次 vtable 加载+间接跳转，接口调用场景 5-20% | 本项目已有非虚直接调用分支（[本:CodeGenerator.cs:806-814]）；coreclr fgResolveGDVs（[coreclr:compiler.cpp:4431]） |
| 5 | **死存储消除 + temp 内联**（未读 stloc、单次使用 temp 展开） | P1 | 产物 -3~8%、消除 -Wunused-but-set-variable 类噪声、temp 数大减 | coreclr optVNBasedDeadStoreRemoval（[coreclr:optimizer.cpp:6015]）；本项目审计已见未用 temp 痕迹 |

> 明确不做（详见 §9）：寄存器分配、循环优化全套、SSA、完整 VN/CSE、完整内联、边界检查消除完整移植、尾调用、GC 轮询。

---

## 1. coreclr JIT 流水线全览（对照本项目的"单遍折叠 + 发射"）

### 1.1 coreclr 主流程（compCompile，[coreclr:compiler.cpp:4279]）

coreclr JIT 是**多遍、多中间表示**流水线，关键 phase 序列（行号均指 compiler.cpp compCompile 内）：

```
PHASE_PRE_IMPORT        (4306)  预检查
PHASE_IMPORTATION       (4341)  IL → 树形 IR（importer.cpp impImportBlockCode:6162，
                                按基本块导入，逐指令压栈/折叠/生成语句树）
PHASE_MORPH_INLINE      (4416)  内联（inline.cpp fgInline）
PHASE_DFS_BLOCKS1       (4434)  第一次 DFS + 不可达块删除（fgDfsBlocksAndRemove:fgopt.cpp:4879）
PHASE_EARLY_UPDATE_FLOW_GRAPH (4490) 控制流图规范化（fgUpdateFlowGraph:fgopt.cpp:4359）
PHASE_PROMOTE_STRUCTS   (4496)  struct 局部变量提升
PHASE_LOCAL_MORPH       (4512)  局部树形化（lclmorph.cpp fgLocalMorph:2369）
PHASE_EARLY_LIVENESS    (4521)  早期活性分析（liveness.cpp fgEarlyLiveness:2852）
PHASE_FWD_SUB           (4525)  前向替换（copyprop.cpp）
PHASE_MORPH_GLOBAL      (4556)  全局树形化（morph.cpp fgMorphBlocks → fgMorphStmts:13614
                                → fgMorphTree:12663 → fgMorphSmpOp:6986 简单操作规范化）
PHASE_OPTIMIZE_FLOW     (4594)  控制流优化（optOptimizeFlow）
PHASE_FIND_LOOPS        (4616)  循环识别（optFindLoopsPhase）
PHASE_INVERT_LOOPS      (4624)  循环逆转（减少条件跳转）
PHASE_UNROLL_LOOPS      (4636)  循环展开
PHASE_COMPUTE_DOMINATORS(4640)  支配树
PHASE_MARK_LOCAL_VARS   (4661)  局部变量引用计数/排序（lclvars.cpp lvaMarkLocalVars:3391）
PHASE_BUILD_SSA         (4733)  SSA 构建（ssabuilder.cpp fgSsaBuild）
PHASE_EARLY_PROP        (4745)  早期常量/拷贝传播（optEarlyProp）
PHASE_VALUE_NUMBER      (4752)  值编号（valuenum.cpp fgValueNumber:11847）——一切全局优化的基础
PHASE_HOIST_LOOP_CODE   (4759)  循环不变量外提（optimizer.cpp:3720）
PHASE_VN_COPY_PROP      (4766)  VN 驱动的拷贝传播
PHASE_OPTIMIZE_BRANCHES (4773)  冗余分支消除（redundantbranchopts.cpp:12，支配树 visitor）
PHASE_OPTIMIZE_VALNUM_CSES (4786)  CSE（optcse.cpp optOptimizeValnumCSEs:5663）
PHASE_BOUNDS_CHECK_COALESCE (4793) 边界检查合并（boundscheckcoalesce.cpp）
PHASE_ASSERTION_PROP_MAIN (4797)  断言传播（assertionprop.cpp optAssertionPropMain:6651）
PHASE_OPTIMIZE_INDEX_CHECKS (4804) 范围检查消除（rangecheck.cpp）
PHASE_OPTIMIZE_INDUCTION_VARIABLES (4811) 归纳变量优化
PHASE_VN_BASED_DEAD_STORE_REMOVAL (4820) 死存储消除（optimizer.cpp:6015）
PHASE_SWITCH_RECOGNITION (4936)  switch 识别
PHASE_OPTIMIZE_BOOLS    (4940)  布尔表达式优化
PHASE_IF_CONVERSION     (4944)  if 转换（cmov）
PHASE_LINEAR_SCAN       (5048)  线性扫描寄存器分配（lsra.cpp）
PHASE_OPTIMIZE_LAYOUT   (5063)  基本块布局优化
```

### 1.2 本项目现状（src/il2cppc/）

本项目是**单遍流水线**（CodeGenerator.Generate，[本:CodeGenerator.cs:14]）：

```
元数据加载（MetadataLoader）→ VTablePlanner.PlanAll（[本:VTablePlanner.cs:36]，编译期 vtable 布局）
→ 每方法：MethodBodyReader 读 IL → IlDecoder 解码 → StackFold.Fold 单遍栈→IR（[本:StackFold.cs:21]）
→ CodeGenerator.EmitBody 可达性分析 + 死块消除（[本:CodeGenerator.cs:215-243]）+ 常量折叠/分支定死（:551-559）
→ MethodEmitter 发射（IL2CPP_NOINLINE 外壳，[本:MethodEmitter.cs:28]）
→ 之后全部交给 C++ 编译器（clang/GCC/MSVC -O2）
```

**核心差异与含义**：
- coreclr 自己做寄存器分配、循环优化、SSA——因为它是机器码生成器；本项目**输出 C++ 源**，这些工作 C++ 编译器以更高成熟度完成（C6 DRY：不重复造轮子）。
- 但有三类信息**只有转译器知道**，C++ 编译器永远学不会，必须由转译器在折叠/发射时消化：
  1. **.NET 语义**：unchecked 整型回绕（TryFoldInt32 已做，[本:CodeGenerator.cs:1125-1187]）、除零抛托管异常（il2cpp_checked_div，:663-666）、null 检查（FieldAccess/il2cpp_null_check）、cctor 惰性守卫（ensure_cctor，[本:StackFold.cs:891-923]）、引用比较语义（null 折叠，:671-690）。
  2. **封闭世界类型集**：所有类型/方法编译期已知 → 去虚拟化、接口分派优化、vtable 布局是编译期决策。
  3. **生成代码形状**：C++ 优化器的上限由输入源码决定——多余的 temp、未折叠的常量链、残留死存储会拉低生成代码质量（铁律 2e/2f-2 性能 >= 官方 JIT）。
- 因此本项目优化的正确顺序：**先把 IL 层可折叠的信息全部折叠干净（P0 组），再谈需要数据流的全局优化（P1 组）**，与 coreclr 的"import 即折叠、后续 phase 锦上添花"哲学一致。

---

## 2. 常量折叠与常量传播

### 2.1 coreclr 做法

1. **导入期即时折叠**：importer 在构建每条语句时对操作数调用 `gtFoldExpr`（[coreclr:importer.cpp:5406] 等 20+ 处），纯常量子表达式当场变成常量节点。
2. **统一折叠器** `gtFoldExpr`（[coreclr:gentree.cpp:15458]），按节点形态分派：
   - `gtFoldExprUnary`（:15512）：取负/取反的常量求值、恒等式（-(-x)、~~x）；
   - `gtFoldExprBinary`（:15577）：算术/逻辑/移位常量求值、交换律规范化（常量右置）、恒等式（x+0、x*1、x&0、x|0、x^0、x-x、x<<0）、同类常量合并（(a+1)+2→a+3）；
   - `gtFoldExprCompare`（:15787）：比较常量求值；
   - 除零不折叠（保留运行时异常）。
3. **常量条件分支折叠**：`fgFoldConditional`（[coreclr:morph.cpp:13214]）把 `if(常量)` 直接定死为无条件跳转/消除，配合死块删除（fgRemoveUnreachableBlocks，[coreclr:fgopt.cpp:79]）。
4. **常量/拷贝传播**：SSA 之后 `optEarlyProp`（[coreclr:compiler.cpp:4745]）+ `fgForwardSub`（:4525）+ 断言传播 `optAssertionPropMain`（[coreclr:assertionprop.cpp:6651]）把"变量恒等于常量/变量"的事实沿支配边传播替换读取点。

### 2.2 本项目现状

- 常量折叠**已有**：`TryFoldInt32`（[本:CodeGenerator.cs:1133-1184]）覆盖全部 int32 二元/一元运算，按 .NET unchecked 回绕语义（Wrap32，:1187），比较类也折叠为 0/1；除零防御不折叠（:1146-1150）。
- 常量条件分支定死**已有**（[本:CodeGenerator.cs:551-559]）→ 与可达性分析联动产生死块消除（:215-243）。
- **缺口**：
  a) **无恒等式/规范化**：`x+0`、`x*1`、`x^0`、`x-x`、`x&0`、`x|0`、`x<<0`、`-(-x)`、`(a+1)+2→a+3` 都不会折叠（TryFoldInt32 只在左右都是常量时成功）。
  b) **无常量局部传播**：`stloc x; ...; ldloc x` 若 x 只被常量赋值一次，不替换；Roslyn /optimize+ 的 `int x = 5;` 驻栈 dup 模式已被 join 溢出机制处理（[本:StackFold.cs:428-461]），但"存局部再读"模式未处理。
  c) **无 int64 折叠**：TryFoldInt32 只处理 int32（Ldc_i8 有 IR 但折叠/发射走 LL，[本:CodeGenerator.cs:640]）；int64 常量运算被当作运行时表达式（审计 C3 亦指出 int64 静默截断风险）。
  d) 字符串常量去重（ldstr intern）未做——每次 ldstr 都生成 `il2cpp_string_new_from_cs` 调用（[本:CodeGenerator.cs:646]）。

### 2.3 建议

- **P0-1 恒等式/规范化 pass**（实现位置：`StackFold.cs` 的 Fold 内 Push 时或发射前新 pass；收益：算术密集产物 -5~15%）。
  安全子集（在 .NET unchecked 回绕语义下恒成立，逐条核对）：
  `x+0→x`、`0+x→x`、`x-0→x`、`x*1→x`、`1*x→x`、`x*0→0`（注意：int 回绕下仍成立）、`x&0→0`、`x|0→x`、`x^0→x`、`x&(-1)→x`、`x<<0→x`、`x>>0→x`、`x-x→0`、`-(-x)→x`、`~~x→x`、`x+1+2→x+3`（同类常量合并，从左侧起结合）、常量右置（`c op x → x op' c` 仅对交换律安全的 Add/Mul/And/Or/Xor）。
  **严禁**：`x*0→0` 对引用指针/含副作用的左操作数、`x-x→0` 对浮点、任何含 CallExpr/FieldExpr 且可能副作用的子树不做代数变换（除非先证明无副作用——保守起见 P0 版只对 LocExpr/IntConstExpr 组成的纯算术子树做）。除零相关恒等一律不做（异常语义）。
- **P0-2 常量局部传播**（实现位置：StackFold.Fold 内维护 `locConst: Dictionary<string,long>`：stloc 若值为 IntConstExpr 记录，之后 ldloc 命中则直接 Push 常量并删除对 loc 的依赖；遇到重写 loc 或方法调用（可能改 loc？不可能——局部变量是方法私有的，但 join 点/分支需保守：只有单赋值且值域内无分支汇合写入才安全）——**保守版**：仅当该 loc 在方法内只有一次赋值（stloc 出现一次）且无 ldloca/ldloca_s（取址逃逸）时传播。收益：常量链折叠、分支定死连锁、死块消除连锁。
- **P1-2 int64 折叠**：把 TryFoldInt32 泛化为 TryFoldInt64（含 Wrap64 回绕 + 64 位除法防御），发射保持 `LL` 后缀（[本:CodeGenerator.cs:640] 已是 `+ "LL"`）。收益：int64 算术产物更小；消除审计 C3 类隐式截断隐患（折叠期完成截断=显式）。
- **P1-3 ldstr intern**：同方法内重复 ldstr 相同字面量 → 复用同一 temp（首个赋值 + 后续引用）；跨方法 intern 需字符串池（运行时侧），暂缓。收益：字符串密集代码产物 -10~30%（字符串字面量重复场景）。

---

## 3. 树形化 / 表达式规范化（IR 结构）

### 3.1 coreclr 做法

`fgMorphBlocks`（PHASE_MORPH_GLOBAL，[coreclr:compiler.cpp:4556]）是全局树形化：逐块 `fgMorphStmts`（[coreclr:morph.cpp:13614]）→ 逐语句 `fgMorphTree`（:12663，自顶向下+自底向上两遍）→ 简单操作 `fgMorphSmpOp`（:6986）+ `fgMorphSmpOpOptional`（:11243）。核心职责：
- 操作数**规范化**：交换律操作数排序（常量右置、更"便宜"的操作数右置）；
- 简单操作**折叠/恒等式**（与 §2 相同集合）；
- 树形**下沉/上浮**：把可交换的二元操作数重组以减少拷贝；
- 把 GT_INDEX/GT_ARR_LENGTH 等高层节点展开为地址算术（对应本项目 ldelem/stelem icall 的展开机会）；
- 语句级：把嵌套调用展开为独立语句（statement 边界）。

### 3.2 本项目现状

- StackFold 单遍栈→IR 已经是"树形"IR（Expr 树，[本:Ir/Expr.cs]），无结构性问题。
- **缺口**：无独立的规范化 pass；EmitCall 参数入栈顺序导致的操作数顺序不规整（`args.Insert(0, ...)`，[本:StackFold.cs:524]）不保证常量右置；无 §2.3 恒等式。

### 3.3 建议

- **P0-3**：恒等式/规范化 pass 同时承担"规范化"职责（与 P0-1 合并实现）：发射前对每条 AssignStmt 的 Value 与 BranchStmt 的 Condition 做一次深度重写（自底向上单遍即可）。实现位置：新 `Ir/Normalize.cs`（或 StackFold 内静态方法），CodeGenerator.EmitBody 发射前调用。
- **P1-4 数组访问展开**（对齐 coreclr fgMorphArrayOps，[coreclr:compiler.cpp:4658]）：ldelem/stelem 在常量索引 + 已知元素大小时展开为 `((int32_t*)arr->data)[idx]` 直接访问（省略 helper 调用，保留边界检查——见 §8 的 icall 边界语义）。收益：数组热循环 -20~40% 单元素开销（视 helper 成本）。

---

## 4. 公共子表达式消除（CSE）

### 4.1 coreclr 做法

`optOptimizeCSEs`（[coreclr:optcse.cpp:5828]）→ `optOptimizeValnumCSEs`（:5663）五步：
1. `optValnumCSE_Locate`：基于 VN（值编号，[coreclr:valuenum.cpp:11847]）找出重复值表达式，注册为 CSE 候选（optCSECandidateCount，:5830 起用 `optCSEstart = lvaCount` 为每个 CSE 分配一个局部变量槽）；
2. `optValnumCSE_InitDataFlow` + `optValnumCSE_DataFlow`：位图数据流（bbCseGen/In/Out），计算每个 CSE 在哪些块可用；
3. `optValnumCSE_Availability`：跨调用可用性（调用会杀掉含调用副作用的候选，getCSEAvailCrossCallBit，:5944 区域）；
4. `optValnumCSE_Heuristic`：**收益模型**——`CSE_Heuristic`（optGetCSEheuristic，:5567）按候选的使用次数、计算成本 vs 寄存器/内存成本、可用性长度、跨调用性决定是否落地；`optIsCSEcandidate`（:5710）公开"该树是否可能被 CSE"（返回语句、异常路径、副作用树被排除）。
- 关键：CSE 结果写入新局部变量（optCSEstart 之后的槽），后续 LSRA 决定放寄存器还是内存。

### 4.2 本项目现状与评估

- **转译器无 CSE**。评估（这是本文档最重要的判断之一）：
  - **AOT→C++ 路径下，C++ 编译器（clang/GCC/MSVC -O2）对纯函数表达式做完整的 GVN/CSE**（LLVM GVN pass、GCC value-numbering）——转译器级全局 CSE 是重复劳动（C6 DRY）。
  - **但有一个前提不成立时会漏**：本项目生成的表达式树若把"本该复用的子表达式"拆成多个独立 CallExpr（helper 调用如 `il2cpp_checked_div`、`il2cpp_null_check`、字段访问的 null check），C++ 编译器不敢合并含未知副作用/调用的表达式——这时转译器级 CSE 有意义。当前 IR 中 CallExpr 全部先落 temp（[本:StackFold.cs:399-424]），天然共享引用，局部重复已经较少。
  - **结论**：转译器级 CSE 只做**基本块内、纯算术子树（无调用、无字段）的重复检测**，收益面窄（此类重复 C++ 编译器已消除）→ **P2 不做，除非未来出现"helper 调用表达式重复"证据**。把精力投入 P0 恒等式/常量传播（它们的收益 C++ 编译器依赖常量知识才会触发，且 -O0 也受益）。
- **连带洞察**：coreclr CSE 的"收益模型"思想可借用于**temp 内联决策**（§6.2）：单次使用的 temp 展开回表达式（若子表达式无副作用且求值顺序安全）——这是"反向 CSE"，对生成代码质量立竿见影。

---

## 5. 内联启发式

### 5.1 coreclr 做法

内联决策在 `fgInline`（PHASE_MORPH_INLINE，[coreclr:compiler.cpp:4416]）→ `inlinepolicy.cpp` 的 `DefaultPolicy::DetermineProfitability`（:501 区域起）：
- **候选分级**（[coreclr:inlinepolicy.cpp:601-640]）：
  1. `CALLEE_IS_FORCE_INLINE`（[MethodImplOptions.AggressiveInlining]）→ 必内联（深度上限内）；
  2. IL 大小 ≤ `ALWAYS_INLINE_SIZE` → 必内联候选；
  3. IL 大小 ≤ `GetMaxInlineILSize` → 视收益（discretionary）；
  4. 超限 → `CALLEE_TOO_MUCH_IL` 永不内联。
- **硬性拒绝条件**（[coreclr:inlinepolicy.cpp:556-600]）：callee 最大栈 > SMALL_STACK_SIZE、单基本块 no-return 方法（throw helper）、基本块数 > MAX_BASIC_BLOCKS、调用点深度 > GetMaxInlineDepth。
- **状态机评估**：`CodeSeqSM`（SM_OPCODE 状态机，[coreclr:inlinepolicy.cpp:641 区域]）按指令序列加权估算内联后代码量（smweights.cpp 训练出的权重），供收益模型使用。
- 内联后接 `fgPostInlineNoReturnCleanup`（compiler.cpp:4426）清理死代码。

### 5.2 本项目现状与约束

- **铁律：禁内联**（用户 2026-08-03 决策：帧记录/StackTrace 完整性要求每个托管方法有独立可寻址符号；`IL2CPP_NOINLINE`，[本:MethodEmitter.cs:13-17]）。注意：IL2CPP_NOINLINE 只防"该方法被内联进别处"，方法体内的调用不受限——但这意味着**被调用的用户方法全部保持真实调用**。
- **既定替代**：属性折叠（get/set 简单 accessor → 字段访问，铁律 2j；设计已写 docs/architecture/属性折叠设计.md；实现位置 = AccessorAnalyzer 预扫描 pass + StackFold.EmitCall 折叠 + CodeGenerator 跳过 accessor 发射）。这是"禁内联"约束下唯一合法的调用消除。
- **缺口**：coreclr 的收益启发式可以借用（小方法展开），但被帧记录铁律阻止——**任何"把被调用方方法体复制进调用点"的方案都会丢失被调方法的帧记录**（展开处无 push/pop）。

### 5.3 建议

- **P0-4**：实现属性折叠设计文档（既有设计，唯一缺实现）。除 get/set 外扩展到：`IsXxx`（返回 `f == null` 的比较）、`ToInt32` 类（返回字段强转）、只读字段包装。判定与安全条件照设计文档（非虚、字段同类内、方法体确认为简单 accessor）。
- **P2-5 小方法展开（有条件版）**：仅当满足以下全部条件才在调用点展开：
  a) 被调方法体 ≤ 3 条 IL 且无分支/无异常区域/无 ref 局部（无帧记录需求——`refLocals.Count == 0` 且方法不会抛托管异常/不访问受保护资源）；
  b) 调用点位于非 try/finally 区域；
  c) 展开复制方法体到调用点（保留 ensure_cctor 语义检查）。
  收益：微型工具方法（如 int 比较包装）调用消除。**风险**：StackTrace 缺帧（a) 条件下无 ref 局部但仍可能被异常展开中断——throw 位于被调方法时缺帧）。**结论：P2 低优先，先做属性折叠，产出数据后评估是否值得**。
- **不做**：完整 coreclr 内联（状态机、收益模型、多级候选）——AOT 封闭世界下属性折叠覆盖了 90% 价值的 accessor 场景，其余小方法收益有限且与帧记录冲突。

---

## 6. 死代码消除 / 可达性 / 死存储

### 6.1 coreclr 做法

- **不可达块删除**：`fgRemoveUnreachableBlocks`（[coreclr:fgopt.cpp:79]，基于可达集/DFS 两阶段：早期靠 fgComputeReachability 集合，晚期从 fgFirstBB 做 DFS 标不可达；BBF_DONT_REMOVE 块转 throw）、`fgDfsBlocksAndRemove`（[coreclr:fgopt.cpp:4879]，DFS 后删除不可达块+空块压缩）、`fgUpdateFlowGraph`（:4359，规范化：合并连续块、消除无条件跳转链）。
- **死语句**：morph 阶段 `fgMorphStmt` 顺带删除无效语句；VN 阶段 `optVNBasedDeadStoreRemoval`（[coreclr:optimizer.cpp:6015]）删除"写后无读"的存储（含冗余覆盖写）。
- **NoReturn 清理**：`fgPostInlineNoReturnCleanup`（compiler.cpp:4426）。

### 6.2 本项目现状

- **已有**（M2 Task 1 达成三编译器零警告的基石，[本:CodeGenerator.cs:215-243]）：常量条件分支定死（:551-559，真→goto/假→注释行）、branchTargets 死 label 排除（:219-222）、可达性传播（goto/ret/常量真后不可达；被引用 label 重置可达，:225-243）、死块不发射（:353）。
- **缺口**：
  a) **无死存储消除**：`stloc` 后该 loc 从未被读（或读前被覆盖）→ 仍发射赋值语句。C++ -O2 会消除，但 -O0 不会；且生成代码含"只写不读"变量触发 -Wunused-but-set-variable（M1 审计记录过此类）。
  b) **无 temp 内联**：StackFold 每 CallExpr 必落 temp（[本:StackFold.cs:399-424]），大量 `t5 = f(a); x = t5;` 形态——t5 若只读一次且相邻，可直接 `x = f(a);`。
  c) **连续 label / 空块合并**：无（相邻 label、label 后立即无条件 goto 的链未压缩）。
  d) 异常区域的 finally 双路发射（[本:CodeGenerator.cs:340-468]）有复制膨胀，无"finally 为空/仅 nop"的剔除。

### 6.3 建议

- **P1-5 死存储消除**（实现位置：StackFold 折叠后、EmitBody 前的 IR pass，或并入 Normalize.cs）：对每个 loc（含 temp tN）统计 {定义点, 读取点}（LocExpr 引用 + FieldExpr.Object 间接引用保守计入读取）；定义点之后无任何读取（沿语句顺序，分支处保守：定义点所在直线流上首个读取之前有分支跳出 → 视为可能被读，不做）→ 删除该 AssignStmt（若 Value 无副作用：纯算术/常量；有副作用则保留调用但丢弃目标）。收益：产物 -3~8%、消警告噪声。
- **P1-6 temp 内联**：对单次读取的 temp 且满足：① 赋值 Value 无副作用（纯算术/字段读/null check——字段读有 null check 副作用？il2cpp_null_check 理论上可能抛 NRE → 视为副作用，仅当读取点与赋值点之间无任何可能抛托管异常/调用的语句时内联——保守：只内联 IntConstExpr/LocExpr/BinaryExpr(纯)/UnaryExpr(纯)）；② 读取点唯一且语句顺序在赋值之后。替换读取点表达式，删除赋值。收益：temp 数大减（-O0 直接受益，-O2 减少 C++ 优化器负担）、生成代码更美（铁律）。
- **P2-6 空块/label 压缩**：连续 LabelStmt 合并、`Lx: goto Ly;` 且 Lx 无其他引用 → 删除 Lx（改引用）；finally 双路发射中空 handler 剔除。

---

## 7. 局部变量提升 / 寄存器分配思想

### 7.1 coreclr 做法

- **局部变量生命周期管理**：`lvaMarkLocalVars`（[coreclr:lclvars.cpp:3391]）引用计数 + 排序；`fgEarlyLiveness`（[coreclr:liveness.cpp:2852]）活性分析（GEN/KILL、live-in/out、last-use 追踪——gentree.cpp IsLastUse，:14376）；活性信息驱动：死存储消除、寄存器分配压力估计、调试范围。
- **struct 提升**：`PHASE_PROMOTE_STRUCTS`（compiler.cpp:4496，fgPromoteStructs）把"整体复制频繁的 struct 局部"拆成字段级局部，消除大量整块拷贝；`PhysicalPromotion`（compiler.cpp:4529）按物理布局二次提升。
- **寄存器分配**：LSRA 线性扫描（[coreclr:compiler.cpp:5049]）+ 目标相关打分（lsra_score.h），按活性区间分配寄存器/栈槽。

### 7.2 本项目现状与评估

- **寄存器分配：明确不做**（§9）——C++ 编译器是这方面的世界级实现，且生成代码是"抽象局部变量"形态，交给 C++ 编译器分配最自然。
- **struct 字段提升：不做**——本项目值类型就是真 C++ struct（[本:TypeEmitter.cs] EmitClassStruct），无"整体复制"问题（C++ 编译器自己做 SROA——scalar replacement of aggregates，LLVM 与 GCC 都有，且比 coreclr 的 promotion 更强）。
- **temp 消除是真正值得做的"提升"**（见 §6.3 P1-6）：每消除一个 temp 就是给 C++ 优化器少一个 SSA 变量/寄存器压力源。
- **活性分析的价值**：本项目没有需要活性的下游（无寄存器分配/无 spill），但**帧根协议**（WrapFrameRoots，[本:CodeGenerator.cs:906-963]）与活性相关：当前所有 `refLocals` 全量入帧（:943-949），若做活性分析可缩小帧内根集合（生命周期不相交的 ref 局部可复用槽位——当前帧表是动态数组，槽位复用 = 帧更小 + GC 扫描更少）。**P2**：帧槽复用（同一方法内活性不相交的 ref 局部共享一个槽）。
- **P2-7 局部变量排序**：coreclr lvaMarkLocalVars 按引用计数排序局部变量（高频靠前）——对 C++ 无意义（C++ 编译器自有布局），不做。

---

## 8. 栈帧布局与 GC 安全点

### 8.1 coreclr 做法

- **GC 安全点/轮询**：`fgInsertGCPolls`（[coreclr:compiler.cpp:4931]）在回边/调用点插入 GC 轮询（patchable 指令）；`PHASE_ASYNC`（:4984）与 TieredCompilation 的 OSR（patchpoint）配合实现安全点挂起。
- **精确栈根**：gcencode/gcdecode 把每处机器码的存活引用位置编码进 GC 信息表（fully interruptible / partially interruptible 模型，fgSetBlockOrder，[coreclr:flowgraph.cpp:4155] 确定中断模型）。
- **GC 类型/写屏障**：对象布局 + 写屏障在代码生成期插入（本项目：il2cpp_write_barrier，[本:CodeGenerator.cs:537]）。

### 8.2 本项目现状与差异（重要）

- 本项目（M3 Task 9 三合一设计，[本:CodeGenerator.cs:906-963] 实现为 RAII 帧）：
  - 序言构造 `__frameRefs` 数组 + `il2cpp_frame_push`（:938-954），RAII 析构弹帧（:950-953，正常返回与异常展开双路径安全）；
  - **协作式安全点语义不同**：本项目 GC 是 stop-the-world + **全量动态根表**（运行时扫描线程帧栈），不需要机器码轮询——任意点挂起都安全。coreclr 的"轮询点"对应物在本项目中是帧栈遍历本身，零额外指令。
- **对照结论**：
  - coreclr 需要 GC 信息表（精确到机器码位置）是因为它生成机器码；本项目生成 C++ 后 GC 信息由**运行时的帧描述符**（Il2CppFrame.refs 数组）承载——等价物已具备。
  - **改进点（P2）**：① 帧表每方法一个（含零 ref 局部时完全跳过，:924 已有）；② 引用局部活性区间缩小后帧槽复用（§7.2）；③ 大数组根（array 元素）暂未建模（仅局部本身为根——M3 语义：数组整体是根，元素随对象图扫描，正确）。
  - **不做**：安全点/轮询插入（架构不兼容且无需求）；OSR/patchpoint（无 JIT 路径，铁律 iOS 无动态代码生成）。

---

## 9. 其余 coreclr 优化：对照结论

| coreclr 优化 | 机制（位置） | 本项目结论 | 理由 |
|---|---|---|---|
| 去虚拟化 GDV | fgResolveGDVs（compiler.cpp:4431） | **P0 做（封闭世界版）** | 已有非虚 callvirt 直接调用（[本:CodeGenerator.cs:806-814]）；扩展：① final 方法/final 类型（元数据可查）直接调用；② 单实现接口 → 直接调用（VTablePlanner 已知全类型集，[本:VTablePlanner.cs:36]）；③ 无 override 的虚方法（vtable 槽 = 声明实现本身）直接调用。安全前提：类型集封闭（裁剪保留集 + 动态保留清单，spec 4.4）。收益：callvirt 热路径省 vtable 加载+间接跳转，接口调用场景 5-20% |
| 冗余分支消除 | optRedundantBranches（redundantbranchopts.cpp:12，支配树） | **P1 做简化版** | 本项目分支形态简单（if(cond) goto / goto，[本:CodeGenerator.cs:548-567]）：① 相邻 label 合并；② `Lx: goto Ly` 链压缩（P2-6）；③ 条件重复（同一条件连续两个分支）合并。无需支配树：线性扫描即可覆盖生成形状 |
| if 转换（cmov） | optIfConversion（compiler.cpp:4945） | **不做** | C++ 编译器对 `(c)?a:b` 三元（本 IR 已折叠为 join 赋值形态）自动做 cmov 选择 |
| switch 识别 | optRecognizeAndOptimizeSwitchJumps（compiler.cpp:4937，switchrecognition.cpp） | **P1 做** | IL switch 指令（0x45）当前是否支持需核实（IlDecoder 子集，M1 范围无 switch）；落地 = 解码 + 发射 C++ switch（C++ 编译器做跳转表/二分）。收益：密集 switch 产物显著变小 |
| 布尔优化 | optOptimizeBools（compiler.cpp:4941） | **P1 简化版** | IR 已有 Not/And/Or/Xor：`~(x==0)` 链、`x==0||y==0` 形态的 C++ 侧已优化——转译器只补 `Not(Not(x))` 恒等（P0-1 已含）与比较取反规范化（Brfalse 已用 Ceq(e,0) 统一，[本:StackFold.cs:235]） |
| 边界检查消除 | rangecheck（rangecheck.cpp，MAX_VISIT_BUDGET :44）+ boundscheckcoalesce | **P2 简化版** | 本项目 ldelem/stelem 走 icall（边界检查在运行时 helper 内）；转译器级：常量索引 + newarr 常量长度 → 静态验证在界 → 生成直访路径（P1-4 已含）。完整数据流版本不做（C++ 侧无隐式检查可消除，收益场景窄） |
| 循环优化（识别/逆转/展开/外提/归纳变量） | optFindLoops/optInvertLoops/optUnrollLoops/optHoistLoopCode（compiler.cpp:4616/4624/4636，optimizer.cpp:3720）/optInductionVariables（compiler.cpp:4812） | **不做** | C++ 编译器全权负责且生成代码循环形状简单（原生 for/while）；转译器参与只会制造形状噪声。唯一例外：**回边上的帧 push/pop 与 ensure_cctor 守卫**需确认不在热路径重复执行（M3 帧协议已保证序言一次） |
| 断言传播 | optAssertionPropMain（assertionprop.cpp:6651） | **P2 不做** | 需要 SSA/数据流基建，收益（null 检查消除、比较简化）在 C++ 侧大部分复现（null check 是 helper 调用，C++ 不敢跨调用传播——但本项目 null check 成本低）；投入产出比差 |
| 值编号 VN | fgValueNumber（valuenum.cpp:11847） | **P2 不做** | 它是 coreclr 多个下游（CSE/断言/死存储）的公共基座，本项目这些下游都砍掉后 VN 无消费者；代数恒等式 P0 组覆盖了 VN 最便宜的 20% 收益 |
| 全局 CSE | optOptimizeValnumCSEs（optcse.cpp:5663） | **P2 不做**（§4.2 论证） | C++ GVN 已覆盖纯表达式；helper 调用重复场景当前无证据 |
| SSA | fgSsaBuild（compiler.cpp:4734） | **不做** | 无下游消费者（本项目不做全局优化） |
| LSRA 寄存器分配 | lsra.cpp（compiler.cpp:5049） | **不做** | C++ 编译器全权负责 |
| struct 提升 | fgPromoteStructs（compiler.cpp:4496） | **不做** | 值类型=真 C++ struct，C++ SROA 更强 |
| 拷贝传播 | fgForwardSub/copyprop（compiler.cpp:4525） | **P1 并入** | temp 内联（P1-6）即拷贝传播的发射期版本 |
| 尾调用优化 | compTailPrefixSeen 相关 | **不做** | 铁律：禁尾调用（帧记录） |
| 栈上分配（小对象） | fgExpandStackArrayAllocations（compiler.cpp:4928） | **P2 评估** | newarr 小数组栈上分配是经典 JIT 优化；本项目 newarr 走 il2cpp_array_new（GC 堆）——转译器可对"newarr 常量小长度 + 无逃逸（不返回、不存字段）"生成栈上数组？**风险**：与 GC 根/帧协议冲突（栈数组非 GC 对象，需按值类型语义建模）→ P2 远期评估，不做承诺 |
| 内联（完整） | fgInline + inlinepolicy | **替代路径**（§5.3） | 帧记录铁律；属性折叠为唯一合法替代 |

---

## 10. 改进清单总表（按优先级）

### P0（立即做：无数据流依赖、低风险、收益明确）

| ID | 优化 | 实现位置（建议） | 做法 | 预期收益 |
|---|---|---|---|---|
| P0-1 | 代数恒等式/规范化 pass | 新 `Ir/Normalize.cs`；CodeGenerator.EmitBody 前调用（[本:CodeGenerator.cs:210]） | §2.3 恒等式集合，自底向上重写 AssignStmt.Value / BranchStmt.Condition / CallStmt.Args | 算术密集产物 -5~15%；-O0/-O2 双受益；生成代码美感 |
| P0-2 | 常量局部传播 | StackFold.Fold（[本:StackFold.cs:41] 主循环内维护 locConst 表） | 单赋值且无取址逃逸的 loc，ldc 常量赋值 → 读取点替换常量；连锁触发 P0-1/分支定死 | 常量链折叠、分支定死、死块消除连锁；Roslyn 常量模式全覆盖 |
| P0-3 | 属性折叠落地 | AccessorAnalyzer.cs（新）+ StackFold.EmitCall（[本:StackFold.cs:516]）+ CodeGenerator 跳过发射（:134-181 方法循环） | 按 docs/architecture/属性折叠设计.md 实现；扩展 IsXxx/只读包装 | accessor 调用 100% 消除（热路径每调用省 call+帧 push/pop） |
| P0-4 | 封闭世界去虚拟化 | CodeGenerator.EmitVirtualCall（[本:CodeGenerator.cs:778-822]）+ VTablePlanner 提供查询 | final 方法/final 类型/单实现接口/无 override 虚方法 → 直接调用（类型集封闭安全） | callvirt 热路径省 vtable 加载+间接跳转；接口调用 5-20% |

### P1（需要局部数据流/更复杂分析）

| ID | 优化 | 实现位置 | 做法 | 预期收益 |
|---|---|---|---|---|
| P1-1 | 死存储消除 | 并入 Normalize.cs | 定义点后无读取（直线流内）且 Value 无副作用 → 删赋值 | 产物 -3~8%；消 -Wunused-but-set-variable 噪声 |
| P1-2 | temp 内联（拷贝传播） | 并入 Normalize.cs | 单次读取 + 纯值子树（无副作用）→ 展开替换 | temp 大减；-O0 直接受益；代码更美 |
| P1-3 | int64 折叠 | TryFoldInt32 泛化（[本:CodeGenerator.cs:1133]） | Wrap64 + 64 位除零防御 | int64 产物更小；消 C3 类截断隐患 |
| P1-4 | ldstr intern（方法内） | StackFold Ldstr（[本:StackFold.cs:94]） | 同方法重复字面量 → 复用 temp | 字符串密集产物 -10~30% |
| P1-5 | 常量索引数组直访 | EmitExpr Ldelem/Stelem（[本:CodeGenerator.cs:350-386]） | 常量索引+在界证明 → 免 helper 直访 | 数组热循环 -20~40% 单元素开销 |
| P1-6 | switch 解码+发射 | IlDecoder + StackFold + EmitStmt | 确认 0x45 支持，发射 C++ switch | 密集 switch 产物显著变小 |
| P1-7 | 冗余分支简化（线性版） | EmitBody 后处理 | 相邻 label 合并、goto 链压缩、条件重复合并 | 产物 -1~3%；控制流更清 |

### P2（远期/有条件/评估后定）

| ID | 优化 | 说明 |
|---|---|---|
| P2-1 | 小方法展开（有条件内联） | 仅无 ref 局部+无分支+非异常区域的小体展开；帧记录风险需评估（§5.3） |
| P2-2 | 帧槽复用（活性不相交 ref 局部共享槽） | 需最简活性分析；帧更小、GC 扫描更少 |
| P2-3 | 空块/label 压缩 + 空 finally 剔除 | 发射层整理 |
| P2-4 | 栈上小数组（无逃逸 newarr） | 与 GC/帧协议冲突点需先建模 |
| P2-5 | 全局 CSE（仅 helper 表达式重复场景） | 先收集证据再定 |

---

## 11. 明确不适合 AOT→C++ 的优化（避免浪费）

1. **寄存器分配（LSRA）**：C++ 编译器（LLVM/GCC 后端）是此领域最成熟实现，产物是抽象局部变量，无移植价值。
2. **循环优化全套**（识别/逆转/展开/不变量外提/归纳变量/强度削减）：生成代码循环为原生 C++ 循环，C++ 优化器全权处理；转译器干预只会破坏形状。
3. **SSA 构建**：本项目不做全局优化，SSA 无消费者。
4. **值编号（完整 VN）**：无下游（CSE/断言/死存储均不完整移植）；P0 恒等式覆盖其最廉价收益。
5. **全局 CSE**：C++ GVN 覆盖纯表达式；helper 重复无证据。
6. **完整内联**：帧记录铁律（StackTrace 完整性）直接禁止方法体复制；属性折叠是唯一合法替代。
7. **尾调用优化**：铁律禁尾调用（帧记录）。
8. **GC 安全点/轮询（机器码形态）**：架构不兼容——本项目协作式 stop-the-world + 动态根表，任意点安全。
9. **OSR/patchpoint/TieredCompilation**：无 JIT 路径（iOS 铁律），完全无关。
10. **完整断言传播**：需数据流+SSA 基建，收益被 C++ 侧大部分覆盖。
11. **profile-guided 优化**（IBC/PGO）：本项目无运行时剖面数据源（AOT 静态）。
12. **struct 字段提升**：值类型即真 C++ struct，C++ SROA 更强。
13. **边界检查消除的完整数据流版**：边界检查封装在 icall 内，C++ 侧无隐式检查可消除；只做常量场景（P1-5）。

---

## 12. 参考资料（证据索引）

### coreclr（dotnet/runtime main，2026-08-05 补拉，本地持久 `E:\il2cpp\reference\coreclr\src\coreclr\jit\`；原 `C:\Users\windo\AppData\Local\Temp\opencode\coreclr\` 临时缓存已废弃）
- 主流程/phase 列表：compiler.cpp:4279（compCompile），phase 序列 :4306-5077
- IL 导入：importer.cpp:6162（impImportBlockCode）；gtFoldExpr 调用 :5406 等
- 树形化：morph.cpp:13614（fgMorphStmts）、:12663（fgMorphTree）、:6986（fgMorphSmpOp）、:11243（fgMorphSmpOpOptional）
- 常量折叠：gentree.cpp:15458（gtFoldExpr）、:15512/:15577/:15639/:15787（Unary/Binary/Call/Compare）；morph.cpp:13214（fgFoldConditional）
- 死块/流图：fgopt.cpp:79（fgRemoveUnreachableBlocks）、:4879（fgDfsBlocksAndRemove）、:4359（fgUpdateFlowGraph）；optimizer.cpp:6015（optVNBasedDeadStoreRemoval）
- 内联：inlinepolicy.cpp:556-660（候选/拒绝/大小分级）、:882（ALWAYS_INLINE_SIZE 断言）；inline.cpp:972（观察点）
- CSE：optcse.cpp:5828（optOptimizeCSEs）、:5663（optOptimizeValnumCSEs 五步）、:5567（optGetCSEheuristic）、:5710（optIsCSEcandidate）、:5830（optCSEstart）
- VN：valuenum.cpp:11847（fgValueNumber）
- 断言传播：assertionprop.cpp:6651（optAssertionPropMain）、:735（optAssertionInit）、:2157（optAssertionGen）、:5792（optAssertionProp）
- 活性：liveness.cpp:2852（fgEarlyLiveness）、compiler.cpp:4562（fgLocalVarLivenessDone）、gentree.cpp:14376（IsLastUse）
- 局部/寄存器：lclvars.cpp:3391（lvaMarkLocalVars）、lclmorph.cpp:2369（fgLocalMorph）、lsra.cpp（PHASE_LINEAR_SCAN compiler.cpp:5049）
- 其他：redundantbranchopts.cpp:12；rangecheck.cpp:40-45（预算）；optimizer.cpp:3720（optHoistLoopCode）

### 本项目（E:\il2cpp）
- StackFold.cs:21（Fold 主循环）、:399-424（Push/temp）、:428-461（join 溢出）、:476-495（EmitStore 快照）、:516-641（EmitCall）、:891-923（ensure_cctor）
- CodeGenerator.cs:14（Generate）、:215-243（可达性）、:551-559（常量分支定死）、:1133-1184（TryFoldInt32）、:1187（Wrap32）、:663-666（checked_div）、:671-690（null 比较）、:778-822（EmitVirtualCall）、:806-814（非虚直接调用）、:906-963（WrapFrameRoots RAII 帧）、:134-181（方法发射循环）
- MethodEmitter.cs:13-17（IL2CPP_NOINLINE 语义）
- VTablePlanner.cs:36（PlanAll）、:63（BuildInterfaceMaps）
- docs/architecture/属性折叠设计.md（P0-3 的既有设计）
- docs/audit/transpiler-audit.md（C1 参数根/C3 int64 截断/C4 引用数组越界——与本文 P1-3/P1-5 关联）
