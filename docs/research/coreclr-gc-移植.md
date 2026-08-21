# coreclr GC 移植研究文档

- 日期：2026-08-03
- 任务：研读 dotnet/runtime 开源 coreclr GC（src/coreclr/gc/），产出移植设计（用户铁律 2l）
- 源码基线：dotnet/runtime main 分支（GC 已拆分为多文件架构，gc.cpp 149,797 字节为主入口）
- 源码摘录：见 `docs/research/coreclr-gc-源码摘录.md`（关键函数节选）
- 本项目现状基线：`src/runtime/src/gc.cpp`（1110 行）+ `src/runtime/include/internal/il2cpp_gc.h`（131 行）

---

## 0. 一句话结论

coreclr GC 最值得移植的 5 个设计（按价值排序）：

1. **O(1) 写屏障 + 细粒度卡表 + 卡"老化"（扫描时清除）**——替代本项目每次引用赋值 O(n) 线性查块表 + 每次 GC 全表 4MB memset 的现状
2. **桶化代空闲列表（log2 分桶 + BitScanReverse 选桶）**——替代线性 first-fit 扫描
3. **句柄表（normal/weak/pinned/dependent 四类）**——GCHandle 弱引用/固定/条件弱表的地基，本项目完全没有
4. **finalization 队列（registered/unreachable 双区旋转数组 + 二次回收晋升）**——终结器语义的成熟实现
5. **GCDesc 系列式引用描述（series 运行 + 值类型数组重复模式）**——替代 32 位 refFieldBitmap 的 256 字节上限

---

## 1. 分代模型与晋升策略

### 1.1 coreclr 设计

**代的定义**（`gc.h:109-133`）：SOH 三代 soh_gen0/1/2（max_generation=2），LOH=3，POH=4（pinned object heap，.NET 5 加入）。`ephemeral_generation_count = max_generation`——"短暂代"（ephemeral）指 Gen0+Gen1，它们是唯一会被压缩/搬迁的代。

**关键概念：分配预算（budget）**。每代有 `dynamic_data` 记录的 `dd_desired_allocation`（期望分配量，即该代下一次 GC 前的分配预算），GC 结束时 `compute_new_dynamic_data` 根据**存活率**（survived ratio）动态调整下轮预算（存活率低→预算增大；高→减小）。分配线程在 `try_allocate_more_space`（allocation.cpp:3907）检查预算，超预算触发该代 GC（`trigger_gc_for_alloc`）。GC 该收集哪一代由 `generation_to_condemn`（collect.cpp:879/933）决定，最高 condemned 代 = 被收集的最老代。

**晋升策略**：coreclr 不是"每次 GC 都 0→1→2 晋升"。`settings.promotion` 在 mark 之后由 `decide_on_promotion_surv`（mark_phase.cpp:3396）按存活量决策——**存活少时甚至可以 demote（降代）**（plan_phase.cpp `advance_pins_for_demotion` :1895、`decide_on_demotion_pin_surv` :2606）。非压缩 GC 中晋升 = 把存活对象重新链入新代 free list；压缩 GC 中晋升 = 对象搬进更高代的"consing generation"（`plan_generation_starts` :1872）。Gen1 的收集通常与 Gen0 合并（ephemeral GC = Gen0+Gen1 一起收，condemned=1），代价收益由预算机制权衡。

**段与代的关系**（非 regions 模式）：SOH 物理上在一个"ephemeral segment"（短暂段）+ 若干 Gen2 段中；段的起始对象偏移 `heap_segment_mem` + `generation_allocation_start` 界定代边界。**代是段内的地址区间概念，不是对象上的字段**——对象代龄 = 对象地址落在哪个代区间。GC 范围：ephemeral GC 时 `gc_low = generation_allocation_start(generation_of(n))`，`gc_high = heap_segment_reserved(ephemeral_heap_segment)`（collect.cpp:134-145），即整个 ephemeral 段尾部全部收。

### 1.2 本项目现状

`gc.cpp` 采用**块级代龄**：BlockEntry.generation（gc.cpp:131）记录每块代龄，`il2cpp_gc_collect` 无条件全量收集 + 全部存活晋升（0→1→2）（gc.cpp:742-779）。无预算概念，无代级 GC 选择，LOH 阈值 85000 已对齐（il2cpp_gc.h:107）。

### 1.3 差距与移植价值

| 项 | coreclr | 本项目 | 差距 |
|---|---|---|---|
| 代 GC 选择 | 预算驱动 condemned 代 | 全量 GC | 高（性能） |
| 晋升 | 按存活率 promotion/demotion | 无条件晋升 | 中 |
| 对象代龄存储 | 地址区间推导 O(1) | 块表线性查 O(n) | 高 |
| POH | 独立第 5 代 | 无 | 中（pinning 时） |

---

## 2. 标记-清扫与压缩的协调

### 2.1 coreclr 设计

coreclr 的完整 GC 流程（`gc1`, collect.cpp:96）：`mark_phase` → `plan_phase` →（若压缩）`relocate_phase` + `compact_phase`。**plan_phase 是决策中枢**：它决定哪些代 sweep（非移动）、哪些代 compact（移动），并产出全部"存活块（plug）"清单。

**标记**（mark_phase.cpp:2763 `mark_phase`）：
1. 扫描根：`GCScan::GcScanRoots`（EE 线程栈/寄存器根）+ `GCScan::GcScanHandles`（句柄表，gcscan.cpp:162）
2. **卡片驱动的老代→短暂代引用发现**：`mark_through_cards_for_segments`（mark_phase.cpp:3579）遍历老代段，对每个被置位的卡，扫描卡内对象，把指向 ephemeral 范围（gc_low..gc_high）的引用标记入队；**卡内无 ephemeral 指针 → 立即 clear_cards（卡老化！mark_phase.cpp:3675）**
3. 短弱引用清空 `GCScan::GcShortWeakPtrScan`（:3295，弱引用指向未晋升对象 → 置 null）
4. 终结器晋升 `finalize_queue->ScanForFinalization`（:3319，见 §6）
5. 依赖句柄（ConditionalWeakTable）`scan_dependent_handles`（:3326）
6. 长弱引用清空 `GCScan::GcWeakPtrScan`（:3360）
7. 标记栈溢出 → `process_mark_overflow`（:2432）动态扩容 + 溢出段重扫

**计划**（plan_phase.cpp:3314 `plan_phase`）：
- **压缩决策**：`decide_on_compaction_space`（plan_phase.cpp:7829）+ `is_full_compacting_gc_productive`（:7884）——按碎片量阈值（`min_reclaim_fragmentation_threshold` :1488）与 GCCompactRatio 配置决定；Gen0/Gen1 压缩是常态，Gen2/LOH 默认非压缩（与 .NET 行为一致）
- **brick table**（8KB 粒度）：`update_brick_table`（plan_phase.cpp:1747）为每个 plug 记录起始位置偏移，**让 relocate/compact 阶段无需线性重扫内存即可在 O(1) 定位任意对象所属 plug**（类似"分页索引"）
- 存活对象形成 plug 列表（`count_plugs_in_brick` :6119），为每个代构建有序 plug 数组

**非压缩**（sweep.cpp:61 `make_free_lists`）：按代扫描，死对象按代链入对应代 free list 桶；存活对象晋升。

**压缩**（relocate_compact.cpp）：
- `relocate_phase`（:1427）：先更新全部引用——根（`Ref_UpdatePointers`）+ 每个存活对象引用字段（walk_relocation 按 brick 遍历），被移动对象的旧地址通过 plug 头部记录的新地址表换算
- `compact_phase`（:2024）：`gcmemcopy`/`compact_plug`（:1755）逐 plug 顺序搬移，就地收紧
- 引脚（pinning）：`pin_by_gc`（:465）——被 pin 的 plug 不搬，形成"pinned plug"空隙，`convert_to_pinned_plug`（plan_phase.cpp:2459）记录
- LOH 压缩 `compact_loh`（relocate_compact.cpp:87）按 GCLOHCompact 配置可选

### 2.2 本项目现状

`Collect()`（gc.cpp:651）已实现简化版三阶段：Phase A 复制 Gen0 存活到新 Gen1 段（= 压缩搬移，gc.cpp:742-775）+ forward map；Phase B sweep 非存活入自由链（gc.cpp:732-739）；Phase C 引用更新 `UpdateReferencesAfterCompaction`（gc.cpp:552，静态根+根区间+帧栈+全对象图）。**这与 coreclr 的 relocate/compact 思路同构**，但：
- 无压缩决策——Gen0 每次必压缩
- forward map 是线性表 + 线性查找（gc.cpp:525-539，O(n) 每引用！），coreclr 用 plug 头内嵌新地址 O(1)
- 无 brick table——Phase C 重扫全对象图（gc.cpp:602-644，本来就全扫）
- 无 pinning
- Gen2/LOH 永不压缩（与 coreclr 默认一致 ✓）

### 2.3 差距与移植价值

| 项 | coreclr | 本项目 | 差距 |
|---|---|---|---|
| 压缩决策 | 碎片阈值+配置 | 恒压缩 Gen0 | 低（正确性无碍） |
| 引用更新 | plug 头新址 O(1) | forward map 线性查 O(n) | **高**（Gen0 存活多时 O(n²)） |
| pinning | pinned plug | 无 | **高**（P/Invoke 固定依赖） |
| brick table | 8KB 索引 | 无 | 中（大堆扫描时） |

---

## 3. 卡片表 + 写屏障

### 3.1 coreclr 设计

**粒度**（gcpriv.h:6765-6777）：`card_size = 2*GC_PAGE_SIZE/card_word_width` = 2*4096/32 = **256 字节**（64 位；32 位为 128B）。卡表是 `uint32_t` 数组，**每个 uint32 承载 32 个卡位**（bit 粒度，`card_word`/`card_bit` 换算 :6780-6795）。

**写屏障**（JIT 生成内联代码，非函数调用）：对 `addr` 直接 `card_table[addr >> 8] |= bit`（64 位下卡位移=8）。**无条件置位——不查存储对象代龄**（检查由 GCWriteBarrier 配置的可选精确屏障完成）。成本 = 2-3 条指令。

**卡清除 = 老化（aging）机制，不是全表清**：
- ephemeral GC 的 `mark_through_cards_for_segments` 在扫描老代卡时，**发现卡内无 ephemeral 引用就当场 clear_cards**（mark_phase.cpp:3672-3675）
- 有 ephemeral 引用的卡保持置位——下次 GC 还要重扫（引用可能仍指向新晋升对象）
- 只有被收集代范围相关的卡才可能被清（`clear_cards` card_table.cpp:1297 支持任意区间位清除）
- **效果**：老对象写一次置卡后，若引用目标长期存活，卡位持续生效无需重写——"脏卡"集合随 GC 收敛，扫描成本单调下降

**加速装置**：
- **card bundle**（card_table.cpp:1230 `update_card_table_bundle`）：2MB 粒度的"卡之卡"，配合 OS `GetWriteWatch`（硬件页写监视）只处理实际写过的页——跳过完全没写过的卡表区域
- **brick table**：8KB 粒度，GC 内部用（非写屏障用）
- **mark_array**（BGC 专用，background.cpp:1297-1440）：后台 GC 的每对象标记位图

### 3.2 本项目现状

- 卡粒度 **4KB**（`kCardShift=12`, gc.cpp:281），4MB 哈希索引表（gc.cpp:288-289，`index = (addr>>12) & (4MB-1)` 允许别名）
- 写屏障 `il2cpp_write_barrier`（gc.cpp:822-853）**每次调用线性扫块表找存储对象 + 再查代龄**（gc.cpp:832-842）——O(n) per store，**热路径灾难**（生成代码每次引用字段赋值都调）
- `ClearCardTable` 每次 GC 全表 4MB memset（gc.cpp:308-311）
- 卡表尚未参与任何扫描（young-only 未实现，全量 GC 用不上它）

### 3.3 差距与移植价值

| 项 | coreclr | 本项目 | 差距 |
|---|---|---|---|
| 屏障成本 | 2-3 条内联指令 | 函数调用 + O(n) 查表 | **最高**（每引用赋值一次） |
| 卡粒度 | 256B | 4KB | 高（16 倍过扫） |
| 卡清除 | 扫描中老化 | 全表 4MB memset | **高**（每 GC 一次） |
| 卡表索引 | 真地址直接 | 哈希别名（4MB 上限） | 中（堆 >4GB 时错误） |
| young-only 扫描 | 已集成 | 未集成 | 高 |

**注意**：coreclr 的"无条件置卡"策略依赖"卡位=老代对象写的"语义：新代对象（Gen0）写的卡无需置位，但 Gen0 对象本身每次 GC 被全扫，所以**无条件置卡的正确性**成立。本项目同策略可落地，只是多扫一点卡（保守）。

---

## 4. 根枚举

### 4.1 coreclr 设计

根分三层：
1. **线程栈/寄存器根**：`GCToEEInterface::GcScanRoots`（gcscan.cpp:151）→ 每线程栈枚举，靠 JIT 编译期为每个方法生成 GC 描述（安全点 + 栈槽引用位图），GC 暂停时逐帧遍历
2. **句柄表（handle table）**（handletable.cpp + objecthandle.cpp + handletablecore.cpp）：
   - 五类句柄：**HNDTYPE_STRONG（强根）、WEAK_SHORT（短弱）、WEAK_LONG（长弱，跨代跟踪）、PINNED、DEPENDENT（ConditionalWeakTable）**（objecthandle.h）
   - 8 字节对齐句柄槽；**句柄带代龄**——`Ref_AgeHandles`（gcscan.cpp:246）每 GC 为句柄引用对象记龄，`Ref_RejuvenateHandles`（:239）demote 时回退，用于"句柄目标被晋升但句柄本身要按代龄扫描"的精确性
   - 扫描：`Ref_TraceNormalRoots`/`Ref_TracePinningRoots`（mark 期，gcscan.cpp:168-169）、`Ref_UpdatePointers`/`Ref_UpdatePinnedPointers`（relocate 期，:173-174）、`Ref_ScanDependentHandlesForRelocation`（:175）
3. **静态根**：由 EE 的模块级静态字段枚举提供（GcEnumObject 回调进 handle table 或直接枚举）

### 4.2 本项目现状

`il2cpp_gc_add_static_root`（gc.cpp:1017）+ `il2cpp_gc_add_root_range`（gc.cpp:1063）+ 帧栈根（gc.cpp:682-692，il2cpp_frame）。**无句柄表、无弱引用、无 pinning、无 dependent handle**。静态根表容量 128 固定（gc.cpp:241）。

### 4.3 差距与移植价值

| 项 | coreclr | 本项目 | 差距 |
|---|---|---|---|
| 弱引用 | WEAK_SHORT/LONG | **无** | **最高**（WeakReference/GCHandle 语义缺失） |
| 强句柄 | HNDTYPE_STRONG | 静态根表 | 中 |
| 固定 | PINNED | **无** | **高**（P/Invoke 固定 + 压缩互操作） |
| dependent | ConditionalWeakTable | **无** | 中（BCL 依赖） |
| 根容量 | 动态 | 128/64 固定 | 低 |

---

## 5. 空闲列表、段管理、碎片整理

### 5.1 coreclr 设计

**桶化空闲列表**（gcpriv.h:763-803 `alloc_list` + :835 `allocator` 类）：
- 每代一个 allocator，13 桶（MAX_SOH_BUCKET_COUNT=13, gcpriv.h:767）：**桶 0 装 < 2^(first_bucket_bits) 的小块，之后每桶尺寸翻倍，最后一桶无上限**（:764-765）
- 选桶 O(1)：`first_suitable_bucket`（gcpriv.h:865-879）用 `BitScanReverse`（64 位 CLZ）一次定位
- 分配：桶头取块；**块过大 → 分割（split）**，剩余回链小桶（plan_phase 的 free space 排序 `build_ordered_free_spaces` :6405 + `trim_free_spaces_indices` :6236 控制）
- 双链表（DOUBLY_LINKED_FL）：added 队列分离——GC 线程整理的自由块与分配线程消费分离

**段管理**（memory.cpp + regions_segments.cpp）：
- 段 = VirtualAlloc/mmap 的大块（默认小段 16MB 级、LOH 段按需），**commit 按需**（`virtual_commit` memory.cpp:31，有硬限制账户 `heap_hard_limit`）
- 空段返还 OS：`release_segment`（regions_segments.cpp:604-621）→ virtual_free；`delay_free_segments`（:1484）延迟释放（段刚空可能马上又要）
- 段复用：SOH 段 = "ephemeral segment" 持续复用，Gen2 段列表可增删
- 物理内存账户：committed_by_oh 分桶记账，heap_hard_limit 可配置硬上限

**碎片整理**：核心是"**代 free list 桶化 + 压缩决策 + 大块分割**"三件套——非压缩 GC 靠桶化列表让碎片可复用；压缩 GC 靠 plug 顺序搬移消除碎片；LOH 专用策略（见 §6）。

### 5.2 本项目现状

- 单一线性 free list（`g_freeHead`，gc.cpp:237）first-fit 整块复用（`PopFreeBlock` gc.cpp:408-424），**不分割**（gc.cpp:41 注释明示 M2 接受内部碎片）
- 段：64KB（LOH 1MB）固定，malloc 分配（gc.cpp:327-359），**永不返还 OS**（gc.cpp:13 注释）
- 无 commit/记账/硬上限

### 5.3 差距与移植价值

| 项 | coreclr | 本项目 | 差距 |
|---|---|---|---|
| free list | 13 桶 + BitScan O(1) | 线性 first-fit O(n) | **高**（碎片多时分配退化） |
| 块分割 | 大块分割回链 | 整块复用 | 中（内部碎片） |
| 段返还 | 空段 VirtualFree | 永不 | 中（长驻进程 RSS） |
| 段大小 | 16MB 级可配 | 64KB | 中（段链表遍历） |

---

## 6. LOH 与 finalization

### 6.1 LOH

coreclr：LOH 对象（≥85000B，`LARGE_OBJECT_SIZE` gc.h:107，阈值可配 GCLOHThreshold）独立段（`get_segment_for_uoh` regions_segments.cpp:632），分配走 uoh 路径（`allocate_uoh`，无 bump——以 free list + **相邻合并**为主，allocation.cpp `bgc_uoh_alloc_clr` 等），不晋升、默认不压缩（`compact_loh` relocate_compact.cpp:87 按 GCLOHCompact 配置）；.NET 8 后还有 POH（pinned object heap，第 4 代）收纳固定大对象。BGC 期间 LOH 分配不阻塞（`bgc_uoh_alloc_clr`）。

本项目：LOH 段 1MB bump 分配（gc.cpp:101/919-923），不晋升 ✓、不压缩 ✓、阈值 85000 ✓；**无 LOH 相邻合并**（整块复用，与 SOH 同 free list）。对齐良好。

### 6.2 finalization

coreclr `CFinalize`（finalization.cpp）：
- **registered 与 unreachable 是同一数组的两个逻辑区段**，边界指针旋转：注册新终结器 = 从 unreachable 端取槽 → 边界外扩（finalization.cpp:81-118 注释详述）——O(1) 注册，锁只护短临界区（FinalizeLock）
- GC 中 `ScanForFinalization`（mark_phase.cpp:3319）：**不可达但有终结器的对象被晋升（复活）+ 移入 unreachable 区**，其引用子图随之再标记（drain_mark_queue :3320）——这就是 .NET"终结器对象至少两次 GC 才回收"语义的出处
- 终结器线程消费 unreachable 区执行 Finalize()（`GetNextFinalizableObject` gcimpl.h:219）
- `RegisterForFinalization`（gcimpl.h:149）在对象分配时（含 ctor 内含 Finalize 方法检查）由 EE 调用
- 两遍扫描顺序：短弱引用清空 → finalization 晋升 → 依赖句柄 → 长弱引用清空（mark_phase.cpp:3295-3360）

本项目：**无 finalization 队列**（gc.cpp 无任何终结器概念）。M2 需求文档（REQUIREMENTS 深水区）明确要求"终结器+弱引用+GCHandle（两次回收晋升）"，尚未实现。

### 6.3 差距

| 项 | coreclr | 本项目 | 差距 |
|---|---|---|---|
| finalization 队列 | 双区旋转数组 | **无** | **最高**（.NET 语义缺口） |
| 二次回收晋升 | ScanForFinalization | **无** | **最高** |
| LOH 合并 | free list + 相邻合并 | bump + 整块 | 低 |

---

## 7. 其他值得注意的 coreclr 设计（本项目可借鉴）

1. **引用描述 GCDesc**（gcdesc.h）：CGCDescSeries{seriessize, startoffset} 运行系列，值类型数组用负数 NumSeries + {nptrs, skip} 重复模式（gcdesc.h:139-156）。**本项目 refFieldBitmap 32 位 = 最多 32 槽 = 256 字节对象引用上限**——大对象多引用字段时越界（gc.cpp:510 循环 `slot < 32` 即上限）。GCDesc 系列可表达任意大小 + 内嵌值类型数组，且扫描无位循环（直接步进系列）。
2. **代龄 O(1) 推导**：对象代 = 地址区间（gc_low/gc_high/代边界），不查表——本项目块表线性查代龄（LookupGeneration gc.cpp:858）应改为段/区间二分或对象头标记。
3. **暂停协议**（collect.cpp do_pre_gc + GcSuspendEE）：协作式——置 SuspensionPending 标志 + 每线程安全点自检；本项目帧栈=安全点已同构（三合一），未来多线程时直接照搬。
4. **HeapVerify**（gcconfig.h:80, 190-204）：配置级堆完整性校验（GC 前后验证），本项目零校验——移植期调试利器。
5. **GCConfig 表驱动配置**（gcconfig.h:78-151）：全部可调项（SegmentSize/Gen0Size/CompactRatio/LOHThreshold/HeapHardLimit/ConserveMemory/GCWriteBarrier 选择）集中声明 + 环境变量注入。本项目配置为编译期常量。
6. **触发链**（allocation.cpp:2101 `check_for_full_gc`）：分配超过 gen0 预算比例 → 升级为全 GC 检查（fgn_maxgen_percent），防"反复小 GC 压不死大对象"。
7. **延迟段释放**（delay_free_segments）：刚腾空的段不立即还 OS，防抖动。

---

## 8. 移植建议（按优先级）

### P0 —— 正确性/语义缺口（不移植则 .NET 语义不完整）

**P0-1 终结器 + 二次回收晋升（finalization queue）**
- 改什么：新增 finalization 队列（registered/unreachable 双区数组，对齐 CFinalize）；`il2cpp_gc_collect` 在标记后按 ScanForFinalization 模式：不可达但有终结器的对象晋升 + 其子图再标记；提供 `il2cpp_gc_register_for_finalization`；终结器运行 API
- 为什么：REQUIREMENTS 明确要求；System.Object.Finalize/GC.SuppressFinalize 语义完全缺失；当前终结器对象会被直接回收
- 风险：低（纯新增；注意 finalizer 运行线程与 GC 互斥——本项目单线程期可在 collect 后同步跑）
- 参考：finalization.cpp:36-118（队列+锁）、mark_phase.cpp:3319-3326（晋升集成点）、mark_phase.cpp:3295/3360（弱引用顺序）

**P0-2 句柄表 + 弱引用（handle table 最小集）**
- 改什么：新增固定容量句柄表（强/弱短/弱长/固定四类，8 字节对齐槽）；WeakReference/GCHandle icall 接线；标记期弱引用清空（对齐 GcShortWeakPtrScan/GcWeakPtrScan 顺序）
- 为什么：WeakReference 是 BCL 基础；GCHandle 是 pinning 唯一正路；弱引用清空顺序错误会复活死对象
- 风险：中（根集合新增一类根，需进 Phase C 引用更新与标记双路径）
- 参考：gcscan.cpp:142-178（弱引用两遍序）、handletable.cpp:62-75、gcscan.cpp:244-249（AgeHandles 代龄）

**P0-3 写屏障 O(1) 化 + 卡表细化**
- 改什么：① 删除 il2cpp_write_barrier 的块表线性查找（gc.cpp:832-842）——改为**无条件置卡**（对齐 coreclr 语义：Gen0 对象被全扫，多置无害）；② 卡粒度 4KB→256B（card = addr>>8，uint32 位承载）；③ 卡表改真地址索引（去掉 4MB 哈希别名）；④ 删除全表 ClearCardTable，改为**扫描中老化清除**（对齐 mark_phase.cpp:3675）
- 为什么：这是热路径——每次引用字段赋值 1 次函数调用 + O(n) 扫描；全表 4MB memset 每次 GC 一次；256B 卡粒度让 young-only 扫描精确 16 倍
- 风险：中（无条件置卡需生成代码全量走屏障——audit C 系列已确认生成代码 Stfld/数组写均需屏障；改卡粒度影响 il2cpp_gc_is_card_marked 测试）
- 参考：gcpriv.h:6765-6795（粒度与换算）、mark_phase.cpp:3579-3698（卡扫描+老化）、card_table.cpp:1297-1346（区间清卡）

### P1 —— 性能/规模

**P1-1 桶化空闲列表**
- 改什么：g_freeHead 单链 → 每代 13 桶（log2 分桶 + BitScanReverse 选桶，`first_suitable_bucket` gcpriv.h:865）；大块分割回链
- 为什么：碎片场景下 first-fit 退化为 O(碎片数)；桶化后分配 O(1) 期望
- 风险：低（数据结构替换，sweep 与分配两处适配）；注意"块登记表=唯一权威"不变式保持
- 参考：gcpriv.h:763-878、sweep.cpp:61-120

**P1-2 压缩引用更新 O(1)（plug 头内嵌新地址）**
- 改什么：forward map 线性查（gc.cpp:525-539）→ 复制时在目标 plug 头写入新地址标记，更新引用时按对象所属 plug 直接取新址
- 为什么：当前 Gen0 压缩 + N 引用 = O(N·存活数)；大图下 O(n²)
- 风险：中（对象头布局约束——klass 头不能动；需侧表或对象头预留位）
- 参考：relocate_compact.cpp:1427-1662（relocate_phase）

**P1-3 young-only 收集（预算 + condemned 代选择）**
- 改什么：GC 触发改预算驱动（dd_desired_allocation 模式）；支持 condemned=0/1（ephemeral 收集，卡表扫描替代全堆）；Gen0/1 存活不晋升（demote 决策）
- 为什么：全量 GC 随堆增长线性恶化；ephemeral GC 是 .NET 吞吐的关键
- 风险：高（需 P0-3 卡表先行；需块级代龄改区间推导或保留块表但触发逻辑重写）
- 参考：collect.cpp:812-980（garbage_collect/condemned）、allocation.cpp:3889-4008（预算触发）

**P1-4 pinning 支持**
- 改什么：句柄表 PINNED 类（P0-2 内）+ 压缩跳过 pinned plug（convert_to_pinned_plug 模式）
- 为什么：P/Invoke 固定 + Marshal 需要；当前压缩会搬走被固定对象
- 风险：中（压缩器加 plug 跳过分支）
- 参考：plan_phase.cpp:2459-2473、relocate_compact.cpp:465-482

### P2 —— 打磨/长期

**P2-1 段返还与复用**：空段 VirtualFree/延迟释放（regions_segments.cpp:604/1484）；段大小 64KB→可配大段
**P2-2 GCDesc 式引用描述**：refFieldBitmap 32 槽上限 → series 描述（gcdesc.h 全文）；值类型数组扫描（{nptrs,skip} 重复模式）
**P2-3 堆验证模式**：GC 前后完整性校验开关（gcconfig.h:190-204 HeapVerify）
**P2-4 GCConfig 表驱动配置**：全部常量 → 环境变量可调（gcconfig.h:78-151）
**P2-5 依赖句柄（ConditionalWeakTable）**：P0-2 句柄表扩展 DEPENDENT 类 + 两遍扫描（gcscan.cpp:47-94）
**P2-6 card bundle + 硬件写监视**：GetWriteWatch 跳过未写页（card_table.cpp:1230-1291）
**P2-7 多线程 + 暂停协议**：SuspensionPending 协作式暂停（collect.cpp do_pre_gc）——帧栈三合一机制已备

---

## 9. 移植依赖顺序

```
P0-1 finalization ──────┐
P0-2 句柄表/弱引用 ──────┼──→ P1-3 young-only 收集 ──→ P1-4 pinning
P0-3 写屏障/卡表 ────────┘          （依赖 P0-3 卡表扫描）      （依赖 P0-2 PINNED）
         │
         └──→ P1-1 桶化 free list（独立）→ P1-2 压缩更新（独立）
```

P0 三项相互独立可并行；P0-3 是 P1-3 的前置；P0-2 的 PINNED 类是 P1-4 的前置。

---

## 10. 参考源码文件清单（已本地持久化）

`E:\il2cpp\reference\coreclr\src\coreclr\gc\`（dotnet/runtime main，2026-08-05 补拉；原 `C:\Users\windo\AppData\Local\Temp\opencode\coreclr-gc\` 临时缓存已废弃）——63 文件全量在盘，下表大小与引文行号对应拉取时点：

| 文件 | 大小 | 本研究中引用主题 |
|---|---|---|
| gc.cpp | 149,797B | 主入口、段释放 |
| gc.h | 15,240B | 代枚举、alloc_context |
| gcpriv.h | 280,082B | allocator/alloc_list、卡表常量、card_marking_enumerator |
| gcconfig.h/gcconfig.cpp | 24,910B/9,732B | GCConfig 表驱动配置 |
| card_table.cpp | 71,938B | 卡 bundle、clear_cards、老化 |
| mark_phase.cpp | 148,118B | mark_phase、卡扫描、finalization/weak 集成 |
| plan_phase.cpp | 314,569B | plan_phase、brick table、压缩决策、pinned plug |
| relocate_compact.cpp | 77,215B | relocate_phase/compact_phase、LOH 压缩 |
| collect.cpp | 64,309B | gc1、garbage_collect、condemned 决策 |
| allocation.cpp | 213,126B | 预算触发、try_allocate_more_space、free list 操作 |
| finalization.cpp | 22,400B | CFinalize 双区队列 |
| sweep.cpp | 20,476B | make_free_lists |
| memory.cpp | 20,194B | virtual_commit 账户、硬限制 |
| regions_segments.cpp | 89,532B | release_segment、delay_free_segments、段获取 |
| gcscan.cpp | 9,274B | 根扫描编排、弱引用两遍序、AgeHandles |
| gcdesc.h | 6,808B | GCDesc 系列引用描述 |
| handletable.cpp | 36,636B | 句柄表入口层 |
| gcimpl.h | 11,491B | IGCHeap 接口（finalization 注册 API） |
| gcinterface.h | 46,155B | gc_alloc_context、segment_info |
