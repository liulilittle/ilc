# e2e Full Matrix Audit 8 — 批次 147 全量重验证 + stackqueue2 GC 安全缺陷修复（HEAD fc1ae82 工作区，2026-08-13）

日期：2026-08-13（批次 147）
基线：HEAD **fc1ae82**（批次 146 全量终验）之上的工作区（未提交）。
编译器：mingw（全量 397 用例 sweep）。
执行方式：`scripts/run-tests.ps1` 逐用例（net48 oracle → 转译 → cmake → 运行 → 逐字节 stdout/stderr 分列 diff）；新用例（comparerdeep/stackqueue2/stringreplace）离线验证（见 §5 环境事件）。

## 1. 全量 sweep 结果（397 用例）

| 指标 | 数值 | 说明 |
|---|---|---|
| 用例数 | **397**（394 committed + 3 新增：comparerdeep/stackqueue2/stringreplace） | sqprobe（调试探针）已删除，不计 |
| **PASS** | **394** | 逐字节一致 |
| FAIL | **3** | build_asan（伪用例）+ virtbench（bench）+ stackqueue2（真缺陷） |
| 阶段分布（FAIL） | 转译 0 / cmake 1（build_asan）/ diff 2（virtbench + stackqueue2） | build_asan 无 .cs/.csproj（git 全历史无），确定性 missing csproj FAIL，伪用例不计 |

## 2. FAIL 逐例判定

| 用例 | 阶段 | 判定 | 处理 |
|---|---|---|---|
| build_asan | missing csproj | **伪用例**：目录仅 CMake 产物，git 全历史无 .cs/.csproj；隔离重跑确定性 FAIL | 不计为真失败（清单数据问题，sliceA.txt 含该名） |
| virtbench | diff | **bench 口径**：iface/poly/sealed/imulti 计时数字 net48 vs 转译必然不同，checksum 一致；隔离重跑仍 FAIL | 不计正确性（与矩阵7 同判定） |
| stackqueue2 | diff（原）→ **已修复 PASS** | **真缺陷**：GC 压缩收集下 icall 裸指针分裂（se_len/gq_fifo/gq_empty=False，20000 enqueue 后 count=19999） | **本轮修复**（§3） |

## 3. stackqueue2 真缺陷修复（GC 安全，src/runtime 三文件 + 新共享头）

### 3.1 根因（G2 根因链，全部证据化）
1. **压缩式 GC 移动 Gen0 存活对象**：分配触发收集时，存活对象被复制到新段，Phase C 把所有 GC 管理引用（帧槽、对象字段）rewrite 到新地址；旧地址内存按设计 leak（不释放）。
2. **icall 裸 C++ 参数不在根集合**：`push/enqueue` 等的 grow 路径分配后继续用参数指针——参数仍指旧地址。旧内存未释放所以读写"看似正常"，但**调用者帧槽已被 rewrite**——icall 写旧地址（`++count`）、调用者读新地址 → 读写分裂（20000 次 enqueue 丢 1 次 `++count`）。
3. **`EnsureItems` 写丢失（次级 bug）**：按值传容器指针的 ensure 辅助函数在分配后写旧地址的 `items`，调用者重读新地址后 `items` 仍 null → `get_enumerator` 的 ensure 分配触发收集 → `en->items = null` → 0xC0000005 崩溃。
4. **GcRoot 初版协议错误（debug 探针阶段发现）**：`frame_.refs = &slot_` 而 `slot_ = &ref` → refs[0] 指向 slot_ 变量（B 地址），GC 把 `*B`（= 参数变量地址）当对象引用 → 收集后 `Get()` 解引用对象头部 → 垃圾。

### 3.2 修复
1. **新共享头 `src/runtime/include/internal/il2cpp_gcroot.h`**：`GcRoot<T>` RAII 模板——`refs_[0] = &ref`（被登记变量的地址，符合 gc.cpp 帧扫描协议 `frame->refs[i]` = 槽地址），`Get()` 强制内存重读；`Il2CppFrame` RAII 析构自动 pop（幂等）。
2. **stackqueue.cpp**：push/enqueue/get_enumerator×2 的 ensure 分支改为**内联"分配后 `root.Get()` 重读再写"**（删除按值 `EnsureItems` 两个重载）；create×2 分配后重读再写字段再返回。
3. **arraylist.cpp**（同类潜伏 bug）：create/Add/Insert/GrowList/to_array 全加 root + 重读；删除按值 `EnsureItems`。
4. **sortedlist.cpp**（同类潜伏 bug）：create/Insert（含 ensure 内联 + Grow 后重读）/Grow/SnapshotKeysOrValues/snapshot_get_enumerator 全加 root + 重读；删除按值 `EnsureItems`。
5. **hashtable.cpp**（批次 147b，同类）：EnsureBuckets 删除（Insert 内联）/Resize/Insert（Resize 后 + entry 分配后重读）/ctor/snapshot_get_keys_get_values（SnapshotEntries）全加 root + 重读。
6. **versionuri.cpp**（批次 147c，同类）：`icall_uri_ctor` 的 4 个 string 字段 store 全部"分配后重读再写"（原注释只论证了部分初始化字段扫描安全，未覆盖对象移动后的写丢失）。
7. **gc.cpp 保留并行批次的扩容改动**（kMaxForwardEntries/kMaxBlocks/kMaxWorklist 16384→65536、MarkTable 1<<16→1<<17，stackqueue2 S6 探针 20000 存活对象超旧界），与本次修复兼容。

### 3.2b 排查结论（GC-safety 模式全仓审计）
- **已修（6 文件）**：stackqueue/arraylist/sortedlist/hashtable/versionuri + 共享头。
- **已正确（对照确认）**：listi4（`EnsureItems(Il2CppObject** selfSlot)` 根槽 + 分配后重读）、stringbuilder（RootScope 静态根 + `re-derive` 注释模式）、enumext（两阶段静态根）、linq/linq_map/arrayext2/arraysort/ipaddr（RootScope 先例）。
- **天然安全**：dictsi/hashset（buckets 为 C++ 堆 `new[]`，不触发 GC 收集）、stringrw/stopwatch（C++ `new` 状态对象）。
- **反模式（已消除）**：按值容器指针的 ensure/grow 辅助函数 + 分配后直接写参数派生对象字段。

### 3.3 修复后验证（全部实跑证据）

| 验证 | 结果 |
|---|---|
| C0 编译（tests_runtime Release） | 零错误 |
| unit | **1800/1800 PASS**（离线 `dotnet test --no-restore --no-build`） |
| runtime sweep | **139/139 PASS**（build/tests_runtime test_*.exe，两轮） |
| frame_deep（GC 压缩契约） | PASS（generation/移动断言） |
| e2e stackqueue2 | **PASS ×4**（含修复后重建 3 次连跑） |
| e2e 容器相关 | stackq / stackqdeep / stackops2 / arraylistdeep / hashtablecase / hashtable2 **7/7 PASS**（hashtable 修复后：hashtablecase/hashtable2/hashtabledeep/dictcase/linqcase **5/5 PASS**） |
| e2e Uri/Version | uricase / uriparse / versioncase **3/3 PASS**（versionuri 修复后） |
| e2e 新用例 | comparerdeep / stringreplace **2/2 PASS** |
| e2e 离线批量复核 | **57/57 PASS**（复用 gen 增量构建，第二批 28 用例因**外部进程清空 out 目录**未完成——非真失败，见 §5.2；剩余用例待网络恢复后全量补跑） |
| **e2e 全量复核（网络恢复后，2026-08-13 23:1x）** | **396/396 跟踪用例：395 PASS / 1 FAIL（virtbench bench 口径，checksum 一致不计正确性）→ 正确性 100%，零回归**（`dotnet build` 重建 out + il2cppc.dll 直跑转译 + cmake 增量 + 分列 diff；4 路并行） |
| **msvc 抽查（修复相关，2026-08-13 23:2x）** | **15/15 PASS**（stackqueue2/stackq/stackqdeep/stackops2/arraylistdeep/hashtablecase/hashtable2/hashtabledeep/dictcase/uricase/uriparse/versioncase/stringreplace/comparerdeep/linqcase）——GcRoot 模板 + 全部修复在 MSVC 编译运行正确 |
| **批次 148-149 后全量复核（2026-08-14 03:2x）** | **400/400 跟踪用例：399 PASS / 1 FAIL（virtbench bench 口径）→ 正确性 100%，零回归**（Enum 面 100% 接线 + Math.Round modes + Interlocked ref-object + Normalize knownNonNull 修复 + StringComparison 搜索后） |

## 4. 结论

1. **批次 147 全量重验证**：394 真用例 PASS；stackqueue2 真缺陷修复后**正确性 100%**（仅剩 build_asan 伪用例 + virtbench bench 不计）。
2. **既有缺口复核（2026-08-13 全量复核）**：decfmt2（S3 自定义占位符）与 resmgmt/resops（ResourceManager/CultureInfo 接线）**全部转绿**（隔离重跑 PASS ×3）——批次 88-146 期间已修复，矩阵7 缺口记录更新为已解决（见 remaining-gaps.md）。
3. **GC 安全通用缺陷族已修（6 文件）**：icall 裸指针跨分配（create/ensure/grow/to_array/enumerator/ctor 字段写）——stackqueue/arraylist/sortedlist/hashtable/versionuri 全覆盖；GcRoot 协议与生成代码帧协议一致；全仓审计确认其余容器/模块采用正确登记模式（listi4 根槽 / stringbuilder+linq 族 RootScope）或 C++ 堆天然安全（dictsi/hashset/stringrw）。
4. **回归 0**：runtime 139/139（多轮）+ unit 1800/1800 + e2e 全量复核 396/396（395 PASS + virtbench bench）+ msvc 15/15 全绿。

## 5. 环境事件记录

- **NuGet 全局包缓存被清空**（`C:\Users\windo\.nuget\packages` 空）+ **出站 HTTPS 全部阻断**（api.nuget.org/bing/github 均超时，DNS 正常）→ `dotnet build`（隐式 restore）不可用。
- **离线验证路径**（已验证可用）：用例 out/ 下现成 net48 exe（源未变时有效）→ `dotnet <il2cppc.dll> <exe> <gen>` 直跑转译（il2cppc 已构建，`dotnet build --no-restore` 增量 OK）→ cmake 构建 → 运行 → 分列 diff。
- unit 用 `dotnet test --no-restore --no-build` 离线运行成功（bin 完整）。
- 网络恢复后应重跑 `run-tests.ps1` 全量（含 dotnet build 步骤）复核。

### 5.2 外部并发进程检测（2026-08-13 21:3x）

- 检测到**外部活跃进程**（非本批次）：21:15/21:33 启动 30+ MSBuild 节点（/nodemode:1）+ 21:41 正在构建 `test_async_qdisc_threads`（android cmake）+ **清空多个用例 out/ 目录、删除未跟踪用例目录**（如 tests/cases/attribute——git 从未跟踪，外部批次产物）。
- **处置**：立即停止离线批量复核（避免互相破坏），已完成的 57 用例 PASS 记录在案；28 个 "missing out exe" 为外部干扰非真失败。剩余 ~300 用例复核推迟到外部进程收工/网络恢复后（走 run-tests.ps1 标准流程）。
- **教训**：批量验证前先查进程表（Get-CimInstance Win32_Process 过滤 dotnet/MSBuild/cmake）确认无外部并发。修复本身验证（runtime 139/139 + unit 1800 + 9 e2e + msvc 2/2）不受影响（tests_runtime/unit bin 完整）。
