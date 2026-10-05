# Roadmap Status v4 — ILC (IL→C++ AOT Compiler)

- Date: 2026-08-05 (snapshot 6 — supersedes snapshot 5; snapshot 5 kept at
  `docs/roadmap-status-v3.md`, snapshot 4 at `docs/roadmap-status-v2.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, the batch
  11-12 progress, the e2e trend (matrix4 全量 72/106) + 34 剩余 FAIL 归因 +
  3 回归施工单, and the next batch (批次 16) task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git log --oneline -15` — [BASH] HEAD = `69510bf` (branch m1); **5 commits
    since snapshot 5** (`d670a84`): labeled stable batches 11/12 +
    audit batch 12 (full list in §2.1).
  - `git status --porcelain` — [BASH] **126 entries = 51 modified + 75
    untracked** — 并行代理仍活跃 (CodeGenerator.cs / StackFold.cs /
    MethodMap.{Encoding,String,Thread,TimeExt2,TimeSpan}.cs / MethodRefResolver.cs
    均在 M 列表; StackFold.cs +270 行未提交 = 尾调用循环在途). 所有计数均为测量时刻值,
    排批前须重新核对 mtime/属主.
  - `git ls-files src/il2cppc/Emit/MethodMap*.cs` — [BASH] **57 files**
    (snapshot 5: 55; +2 = batch 12 域新增).
  - `tests/unit` — [BASH] **751 `[Fact]`** (snapshot 5: 639; +112 — 含
    工作树在途单元, 提交态更低).
  - `tests/runtime` — [BASH] **96 `test_*.cpp`** (snapshot 5: 95; +1 =
    test_typeinfo).
  - `tests/cases` — [BASH] **98 e2e case dirs** (snapshot 5: 88; +10;
    matrix4 运行面 = 106 用例/audit 口径).
  - `docs/audit/e2e-full-matrix4.md` — [READ] matrix4 全量重跑 **PASS 72/106
    (67.9%)**; 88 交集 **59/88 (67.0%, +12)**; 15 新绿 + 3 回归
    (generics/interlockedcase/valuetypes, 均 cmake_build); 34 FAIL 四类归因
    (transpile 17 / cmake_build 10 / diff 7 / case 0); 回归根因 = b59c6d9
    CodeGenerator.cs:2811 Guid cast 过广 + 泛型实例 ctor 命名.
  - `git show HEAD:src/il2cppc/Ir/StackFold.cs` — [BASH] 提交态**无**
    `ApplyTailCallLoop`; 工作树 +270 行含之 (StackFold.cs:2890) — 尾调用
    **在途未提交**.
  - `docs/plans/e2e-sprint-worklist.md` — [READ] 41 例施工表 (40 真实 FAIL),
    P0 已闭环至 **48/88** 起步基线 (convertcase, commit `69510bf` 报告);
    per-case 修复记录 (§5-13: timecase/stopwatch/threadpool/bitconv/convert/
    exdeep/sortdeep 等).
  - `docs/audit/e2e-recheck9.md` — [READ:1] 批次 13-14 修复后 15 例复验
    (9 PASS/6 FAIL) — 用户批次号口径: 批次 13-14 = P0 修复波, matrix4 =
    批次 15 验证波, 下一批 = **批次 16**.
  - `docs/audit/bench-regression-check.md` — [BASH: Test-Path] 存在;
    M7 证据沿用 snapshot 5 (fib 1.50× / array 1.33× 超 net48, 零退化 6/6).
  - `docs/audit/three-compiler-matrix.md` — [BASH: Test-Path] 存在;
    M6.5 三轴绿证据沿用 snapshot 5.
- Legend: ✅ = committed and verified; 🟡 = in-flight (committed but chain
  incomplete, or worktree-modified); ⬜ = not started.
- ⚠️ Snapshot warning: parallel agents (铁律 2k, ≥24) continuously modify
  files; all counts are measurement-time values. Re-verify `git status`
  before scheduling. 回归检测网 = 矩阵 cmake_build 阶段 (matrix4 三例回归全
  在构建期被拦 — 见 §2.3).

---

## 1. Milestone chain M1-M8

| Milestone | Status | Latest completion evidence (this turn) |
|-----------|:------:|----------------------------------------|
| **M1** Transpiler skeleton (IL decode → IR → C++ emit) | ✅ | Baseline per 路线图.md:13. |
| **M2** Object model + mark-sweep GC + inheritance/interface/virtual/boxing | ✅ | 路线图.md:13-14. |
| **M3** Generational GC + string/array/EH + generics + delegates + frame records | ✅ | Snapshot-5 evidence stands. |
| **M4** P/Invoke + Marshal icalls + icall platform layer + C API + 4-state artifacts | ✅ | Snapshot-5 evidence stands. |
| **M5** BCL full translation (wiring, L1 trimming default) | 🟡 | **33/34+ 域完成** — 57 `MethodMap.*.cs` committed [BASH]. Batch 11-12 新增: **Thread 实例态落地** (threadcase 新绿 — CurrentThread 对象模型, MethodMap.Thread.cs), **Stopwatch 对象态落地** (swcase 新绿 — System_Diagnostics_Stopwatch_class + 11 对象 icall), **Encoding 分配部分落地** (base64case 新绿 — GetBytes raw-buffer + FromBase64String), **Version/Uri 对象闭环** (versioncase/uricase 新绿 — versionuri.cpp 13 icall rebuild-from-slots), StringIO read/write icall map (stringrwcase 新绿), Array 域释放 (Array.Empty + arraysort direct calls, 04d73d7), Guid 值槽 (b59c6d9), BitConverter hex (hexcase 新绿), crypto hash one-shot (hashcase 新绿), enum box Console 臂 (04d73d7), Stopwatch get_IsRunning (1175b3a). **剩余 1 缺**: Encoding **GetChars 分配形** (char[]-result icall 不存在, encodingcase 仍 transpile) [READ:e2e-full-matrix4.md:62]; 杂项接线 (collchain/randomcase/iocase/parsecase 等) 属 M5 收尾面, 归 §3 Chain A. |
| **M5.5** Reflection (trimming-level verification L0/L1/L2) | 🟡 | typeof/GetType 短名/typeinfo 域 落地状态延续 [READ:roadmap-status-v3.md:50]; `tests/runtime/test_typeinfo.cpp` 已提交 [BASH: git ls-files → test_typeinfo.cpp]; runtime tests **96** [BASH]. **L0/L1/L2 trimming 验证 (T301-T310) 仍未做** — snapshot 5 结论延续. |
| **M6** Transpiler (test suites all-green + 3-compiler CI + bench base) | 🟡 | **转译器优化进行中**: batch 11-12 落地 — Guid 值槽 (uint8_t[16] locals + statement-form icalls + memcpy 拷贝 + LOUD 守卫, b59c6d9), constrained. 值类型 recvType fold (TypeToken → il2cpp_guid_to_string/icall_datetime_to_string), double NaN bit-exact (il2cpp_double_from_bits, net48 0xFFF8000000000000), Encoding null-fold 接收者类型, Array.Empty + arraysort direct calls, 对象域接线 (Stopwatch/Uri/Version/StringIO/ThreadPool bool 返回), crypto hash_bytes array 形, bitconverter hex, console enum box 臂 (04d73d7/1175b3a). **尾调用: 在途未提交** — 提交态 StackFold.cs 无 ApplyTailCallLoop [BASH: git show HEAD → 无命中]; 工作树 +270 行含之 (StackFold.cs:2890, 含 arg-slot 弹出校验 :2952), 属代理在途, 待提交+复验. **LICM/逃逸分析: 未落地** (M7 优化器路径). 单元 `[Fact]` = **751** [BASH], runtime = **96** [BASH], e2e = **106 用例/matrix4** [READ:e2e-full-matrix4.md:15]. |
| **M6.5** Cross-platform (三轴绿 + platform layers + NDK) | 🟡 | 三轴绿证据沿用 snapshot 5 [READ:roadmap-status-v3.md:52]: MinGW (clang 17) 零警告零错误 / MSVC 88 目标全编译 / WSL 全量 0 error. NDK 探通 (26.1.10909125, arm64-v8a 可行, env 未设). **残项未动 (批次 11-12 无平台层提交)**: WSL pathproc.cpp 硬缺口 + `find_package(Threads)`/`-pthread` 链接缺; MSVC test_numberformat/test_formatext 2 exe 未编译. |
| **M7** Performance (hotpath + conformance vs net48 oracle) | 🟡 | **性能铁律里程碑达成 — fib 1.50× / array 1.33× 超 net48** (零退化 6/6) — snapshot 5 实测证据沿用 [READ:bench-regression-check.md:22-55 via v3]. 批次 11-12 无新 bench 提交. M6 出口残项: bench 基线入库 + 3-compiler CI routine 仍开. 剩余优化器缺口: object 0.02 / string 0.34 (逃逸分析/分配下沉), arr_write 0.83 (可证边界检查消除). |
| **M8** Full CLDR / COM interop (long-term) | ⬜ | 路线图.md:37. |

Count: 4/8 completed (M1-M4), 5 milestones in-flight (M5, M5.5, M6, M6.5, M7),
1/8 not started (M8).

---

## 2. e2e 趋势 + 当前冲刺

### 2.1 5 commits since snapshot 5 (batches 11-12)

`git rev-list --count d670a84..HEAD` = **5** [BASH]. 批次标记:
- **batch 11** (`b59c6d9`): Guid 值槽接线 (uint8_t[16] locals + NewGuid/Parse
  statement-form write-through + memcpy 值拷贝 + 表达式位 LOUD 守卫) +
  constrained. 值类型 recvType fold + double NaN bit-exact 发射 +
  Encoding singleton null-fold 接收者类型 + gccase GC-priming.
  ⚠️ 此批引入 **2 例回归** (interlockedcase/valuetypes — CodeGenerator.cs:2811
  blanket `(uint8_t*)` cast 过广, 见 §2.3).
- **batch 12 transpiler** (`04d73d7`): Array 域释放 (Array.Empty +
  uint32/int64 sort yield → arraysort.h direct calls) + enum box Console 臂 +
  bitconverter hex + Stopwatch 对象接线 + Uri/Version 对象接线 +
  StringIO read/write icall map + crypto hash one-shot array map + 5 单元套件.
- **batch 12 runtime** (`1175b3a`): convert round-half-to-even
  (nearbyint FE_TONEAREST + [INT32_MIN,2^31) 范围检查) + stopwatch/version/uri
  对象 icall (versionuri.cpp rebuild-from-slots) + stringrw 对象 icall +
  crypto hash_bytes (null→ANE) + bitconverter ToString hex +
  delegate 4-segment 异常链 + Array.Empty ext2 + console enum box arm.
- **batch 12 test** (`4abcae3`): convert 边界探针 (+10 行) + 新 runtime 套件
  (convert/stopwatch/bitconverter/console enum-box/delegateext/arrayext2/
  md5-sha1-sha256/stringrwobj/versionobj) + 9 新 e2e case + threadcase 对象模型
  expected.txt.
- **batch 12 audit** (`69510bf` = HEAD): e2e-sprint-worklist (convertcase +1
  P0 closed → **48/88**) + crypto-domain design + uri/version-object-closure +
  e2e-recheck9 矩阵 (15-case: 9 PASS/6 FAIL).

### 2.2 e2e 趋势: 39 → 44 → 47 → 72

[READ:e2e-full-matrix4.md:13-26] matrix 全量 PASS 轨迹: matrix1 38/88 →
matrix2 39/88 (44.3%) → recheck7 推估 44 (39+5) → matrix3 **47/88 (53.4%)**
→ **matrix4 72/106 (67.9%)**.

**88 例交集口径 (可比)**: matrix3 47/88 → matrix4 **59/88 (67.0%, +12)**
= 15 例新绿 − 3 例回归. 若 3 回归修复 → **62/88 (70.5%)**, 达成 snapshot 5
的 60+/88 预期 [READ:e2e-full-matrix4.md:24,174].

**15 例新绿** (全部 net48 live 逐字节确认): base64case (Encoding/Base64 接线),
convcase (uint32 打印 79/79), convertcase (half-to-even 41/41), environment2
(tick 时序 10/10), exdeep (f18 多 catch 链式分派 37/37), formatcase (∞ 编码
仲裁), gccase (strhash same-literal + GC-priming), guidcase (Guid 值槽),
mathext (∞ 仲裁 100/100), overflow (用例自身改造), stringbuilder (capacity
40/40), swcase (Stopwatch.get_IsRunning), threadcase (Thread.CurrentThread
对象模型), valuesem (double NaN hash), versioncase (Version 对象 gap)
[READ:e2e-full-matrix4.md:148-164].

**新增 18 例 PASS 13/18**; recheck9 9 例 PASS 全部保持; recheck9 6 例 FAIL 中
5 例 (sortdeep/mathext/formatcase/exdeep/valdeep) 已翻 PASS, 仅 datetime2 仍
FAIL (DateTime.Parse 转译缺口) [READ:e2e-full-matrix4.md:166].

### 2.3 3 例回归 (matrix3 PASS → matrix4 FAIL, 均 cmake_build) — P0 施工单

| 用例 | 根因 | 施工单 |
|---|---|---|
| interlockedcase | 生成码 `(uint8_t*)(&loc)` 传 `int32_t*`/`int64_t*` 参数 — **b59c6d9 CodeGenerator.cs:2811** 为 Guid uint8_t[16] 槽把「空 cast + LocAddrExpr」一律加 `(uint8_t*)` cast, 注释自述 "the only ""-cast + ldloca-address combination today is Guid's" — 被 Interlocked 同形态调用打破 [READ:e2e-full-matrix4.md:170] | **R-A1**: cast 判定收窄 — 携带目标签名类型 (仅 uint8_t* 目标 cast; int32_t*/int64_t*/Point* 不 cast) |
| valuetypes | 同根因: `Point__ctor((uint8_t*)(&loc0),…)` 破坏 `Point*` 形参 [READ:e2e-full-matrix4.md:171] | 同 R-A1 (一修双卡) |
| generics | 生成码调用 `Box_1_int__ctor_…` 但声明 `Box_1__ctor_…` — 泛型实例 ctor 展开名与声明名分叉 (非 Guid cast) [READ:e2e-full-matrix4.md:172] | **R-A2**: 泛型实例展开 ctor 命名对齐 (UserMethodCppName/GenericInstantiationName vs 发射侧) |

> 三者均由官方 run-tests.ps1 复跑确认 (非 wrapper 假象) [READ:e2e-full-matrix4.md:174].
> 回归检测网 = 矩阵 cmake_build 阶段: 三例全部在构建期被拦, 而非 diff 阶段
> [READ:roadmap-status-v3.md:52 同结论, matrix4 实证]. 修复后 88 交集 62/88.

### 2.4 34 例剩余 FAIL 归因 (matrix4 全量)

[READ:e2e-full-matrix4.md:176-191]

| 类 | 例数 | 占比 | 主因 |
|---|---|---|---|
| 转译器接线缺 (LOUD 中止) | 17 | 50.0% | 未接 BCL 映射 12 (arrayext2 FindIndex / collchain ArrayList.Remove / consolecase IsInputRedirected / datetime2 DateTime.Parse / delegatecb GetFunctionPointerForDelegate / encodingcase GetChars 分配形 / excext get_Message / gcext WeakReference ctor / genlist 泛型 0x0E / iocase Path.Combine 3 参 / parsecase TryParse ref-out / randomcase Join 1 参) + IL/fold 缺口 3 (delegate2 Ldsflda / enumdeep switch 0x45 / typecheckcase SZARRAY 0x1D) + WriteLine(object) box 类型跟踪 2 (hashtable2/hashtablecase) |
| 转译器生成码 bug (C++ 编译失败) | 10 | 29.4% | 3 回归 (上表: b59c6d9 ×2 + 泛型 ctor) + 7 生成码缺: delechain (int32↔Il2CppObject*), bitconvcase (Il2CppArray* vs 字面量 + IsLittleEndian 未声明), boxdeep2 (ToString/UInt32_class 未声明), strdeep (PadLeft 未声明), pinvokeext (const char* vs lvalue), recurse (entry Main 签名), pinvoke (链接 undefined mylib 符号) |
| runtime 输出不一致 (diff) | 7 | 20.6% | 异常/崩溃 3: strcompare (L3 比较错 + L17 unhandled "value"), stringext (L1 后 unhandled "value", 41/42), delegateinst (0xC0000005, 0/24) + 格式/文化 4: timeext (zh-CN vs en-US + FormatException), formatext (货币符号 oracle 伪影 + L73), cctorchain (boom_caught 1 vs 2), timecase (DayOfWeek 枚举→字符串未映射, Monday vs 1) |
| 用例自身 | 0 | — | matrix3 的 overflow/environment2 均已翻 PASS (用例/harness 级清理完成) |

**8 例从早期阶段前进到更深处** (bitconvcase/pinvoke/pinvokeext/strdeep/
stringext/timecase 越过转译闸门进入构建/运行/diff; 6 例越过 matrix3 旧闸门
含 guidcase/versioncase 已翻绿) — 下批修复面清晰 [READ:e2e-full-matrix4.md:197].

### 2.5 P0 冲刺状态 (自 snapshot 5 推进)

snapshot 5 P0 Top-5 施工单执行情况:
- P0-1 versioncase 闭环: **已落地并全量确认** (matrix4 新绿) ✅
- P0-2 callvirt System.Object 族 (exdeep f14b + guidcase): **exdeep 37/37 新绿**
  (f18 多 catch 分派链, CodeGenerator EH dance) + **guidcase 新绿** (Guid 值槽
  + constrained fold) — 双双闭环 ✅ (但 b59c6d9 引入 2 例新回归 ⚠️)
- P0-3 diff 批 A (convcase/convertcase/formatcase/mathext/formatext):
  conv/convert/format/mathext **4/5 翻绿** (formatext 仅剩 oracle 伪影 + L73
  D100 回退) ✅→⚠️
- P0-4 diff 批 B (valuesem/gccase/cctorchain/stringbuilder): valuesem/gccase/
  stringbuilder **3/4 翻绿** (cctorchain boom 计数仍差) ✅→⚠️
- P0-5 接线缺口批: swcase/threadcase **2/2 落地** (Stopwatch/Thread 对象模型) ✅

worklist P0 口径: convertcase 闭环 → **48/88 起步基线** (commit `69510bf` 报告).

---

## 3. Critical-path analysis

**Chain A — e2e 转译器接线 (17 例, 最重):**
1. **delegateinst (0xC0000005)** — StackFold ~:1704 delegate newobj 代理只取
   FuncPtrExpr, 静默丢弃 target 参数; CodeGenerator.cs:1333 只发 1-arg
   `il2cpp_delegate_new` — runtime 侧已完整支持 (双参 + 逐叶分发, 探针 20/20)
   [READ:e2e-sprint-worklist.md:319-327]. 修复 = DelegateNewExpr 加 Target 字段
   → 2-arg 发射. 精确施工单已就绪.
2. **enumdeep switch (IL 0x45)** — 转译器无 switch 支持 (IlDecoder.cs:265
   default throw); enum 用例覆盖 43/43 待 switch 落地 [READ:e2e-sprint-worklist.md:244-257].
3. **WriteLine(object) box 类型跟踪 (hashtable2/hashtablecase)** — StackFold
   box 表达式携带类型信息 + 按 box 类型分发 [READ:e2e-full-matrix4.md:181].
4. **Encoding 分配收口** — GetBytes 已绿 (base64case), GetChars 分配形
   char[]-result icall 不存在 = M5 剩余唯一对象态卡 [READ:e2e-full-matrix4.md:62].
5. **Thread/Stopwatch 已落地** (threadcase/swcase 新绿) — snapshot 5 的
   M5 收口 2 卡关闭; Version/Uri/StringIO/crypto 对象域同样落地 (stringrwcase/
   uricase/hashcase 新绿).

**Chain B — diff 类 (7 例):** 异常/崩溃 3 (strcompare/stringext unhandled
"value" = 可能是 ArgumentNullException/AOR 的 message "value"; delegateinst
0xC0000005 见 Chain A-1) + 格式/文化 4 (timecase DayOfWeek 枚举→字符串 =
enum box 命名路径; timeext culture + "y" 说明符; formatext 伪影属 harness 域;
cctorchain 异常缓存语义) — 除 timeext 外均有 net48 oracle 单卡可修
[READ:roadmap-status-v3.md:140-142, e2e-full-matrix4.md:187-189].

**Chain C — 基础设施:**
- **T101 MethodMapHub 仍未建** — 接线体量 57 文件下 MethodRefResolver.cs 单
  switch 分发的序列化瓶颈 (snapshot 5 结论延续).
- **回归管控**: b59c6d9 暴露「为单一域加发射规则按参数形态判定」= 回归模式;
  修复后需 matrix5 全量复验 3 回归 + 62/88 预期.

**Chain D — M6 出口 + M6.5 残项:**
- **尾调用**: 提交态无; 工作树在途 (+270 行 StackFold.cs, ApplyTailCallLoop
  :2890) — 提交 + 单元 + e2e 复验后再关 M6 卡.
- **LICM / 逃逸分析 / 可证边界检查消除**: 未落地 (arr_write 0.83 待无检查
  快路径) [READ:roadmap-status-v3.md:157-163].
- **WSL pathproc 硬缺口 + Threads/-pthread**; **MSVC 2 test exe 未编译**
  [READ:roadmap-status-v3.md:150-155].
- **bench 基线入库 + 3-compiler CI routine**: M6 出口铁律仍开.

**性能差距 (非退化, 已知优化器缺口)** [READ:bench-regression-check.md §5 via v3]:
object 0.02 / string 0.34 (JIT 逃逸分析 vs ILC 每迭代真实分配), arr_write
0.83 (边界检查三元式) — 均为 M7 后续优化器工作面.

---

## 4. 下一批 (批次 16) 建议

### 4.1 回归优先 — 3 卡 (最高优先, 修复后 88 交集 → 62/88 达 70.5%)

| # | 卡片 | 说明 |
|---|---|---|
| R-A1 | **CodeGenerator.cs:2811 cast 收窄** (interlockedcase + valuetypes 一修双卡) | 空 cast + LocAddrExpr 的 blanket `(uint8_t*)` 改为按目标签名判定: 仅 uint8_t* 目标 cast; int32_t*/int64_t*/Point* 等原样透传. 回归网 = matrix5 的 cmake_build 阶段 |
| R-A2 | **泛型实例 ctor 命名对齐** (generics) | 泛型实例展开 ctor 调用名 (`Box_1_int__ctor_…`) 与声明名 (`Box_1__ctor_…`) 分叉 — 对齐 UserMethodCppName/GenericInstantiationName 与发射侧命名 |
| V-1 | **matrix5 全量复验** (回归修复后立证) | 确认 3 回归转绿 + 72 保持零回归; 产物 docs/audit/e2e-full-matrix5.md; 预期 75/106 全量 / 62/88 交集 |

### 4.2 diff 批 C (7 例, 除 timeext 外单卡可修)

| 卡 | 内容 |
|---|---|
| delegateinst (0xC0000005) | DelegateNewExpr + Target 字段 → 2-arg il2cpp_delegate_new (worklist §9.5 施工单精确就绪) |
| strcompare + stringext (unhandled "value") | 同根: 异常 message "value" (ArgumentNullException/AOR paramName) 未处理 — 确认 chain 匹配后单修双卡 |
| timecase (Monday vs 1) | DayOfWeek 枚举→字符串: boxed enum 命名路径 (il2cpp_enum_to_string + 名字类描述符 + register 发射) — worklist §5/§9 施工单链 |
| cctorchain (boom 1 vs 2) | .cctor 失败缓存 TypeInitializationException 并在后续访问重抛 (net48 语义) |
| formatext (L73) | D100 自定义回退 vs FormatException — harness UTF-8 捕获已解 L11-66 伪影, 仅剩此真实分歧 |
| timeext (zh-CN + "y" 说明符) | DateTime 格式器 "y" 年月说明符 + 文化数据 (P2 可选) |

### 4.3 转译器接线批 (按 worklist 精确施工单)

| 优先级 | 卡 |
|---|---|
| P1 | delegate2 Ldsflda (StackFold fold: ldsflda+Int32::ToString 模式) — worklist #10 |
| P1 | bitconvcase byte[] newarr (CodeGenerator.cs:2691 ArrayElementType 加 System.Byte) — worklist §10 施工单 #42 |
| P1 | arrayext2 FindIndex (icall_array_find_index + Predicate 形参映射) — worklist #1 |
| P1 | enumdeep switch (IL 0x45) — 新缺口, IlDecoder/StackFold/CodeGenerator 三件套 |
| P2 | 接线缺口批: collchain Remove(object) / consolecase IsInputRedirected / iocase Path.Combine 3 参 / parsecase TryParse ref-out / randomcase Join 1 参 / encodingcase GetChars 分配形 / datetime2 DateTime.Parse / excext get_Message / gcext WeakReference ctor / delegatecb GetFunctionPointerForDelegate |

### 4.4 M6/M6.5/M7 出口

| 优先级 | 任务 |
|---|---|
| P1 | **尾调用收口** — 工作树 ApplyTailCallLoop 在途 → 提交 + 单元 + 复验 |
| P1 | **LICM + 可证边界检查消除** — Normalize.cs 值域分析扩展; arr_write 0.83 → ≥1.0 |
| P1 | **逃逸分析 + 分配下沉** — object 0.02 / string 0.34 优化器路径 |
| P1 | **T101 MethodMapHub** (registry + tail-chain) — 57 文件接线体量下的结构卡 |
| P1 | **bench 基线入库 + 3-compiler CI routine** (M6 出口铁律 2e) |
| P2 | M6.5 残项: pathproc.cpp + `find_package(Threads)`/`-pthread` + MSVC 2 exe |
| P2 | M5.5 trimming 验证 (L0/L1/L2, T301-T310) |
| P3 | M6.5 DLR → M7 解释器 → M8 CLDR/COM |

### 4.5 Batch discipline (taskcards-128 §0, 铁律 2k)

Writer-uniqueness per file; GAPS.md for non-owned files; regression after each
wave: `dotnet build src/il2cppc/il2cppc.csproj` + `dotnet test
tests/unit/unit.csproj` + e2e runner (matrix5). 工作区当前 51M/75U (平行代理
活跃 — StackFold.cs +270 行在途, 尾调用属其域, R-A1 涉及 CodeGenerator.cs
须先对账 mtime/属主再动). 无 git commit 除每-里程碑提交 (铁律 2m).

---

## 5. One-line summary

批次 11-12 后 (5 commits, HEAD `69510bf`): **matrix4 全量 72/106 (67.9%) /
88 交集 59/88 (+12)** — 15 新绿 (含 Thread/Stopwatch 对象模型、Guid 值槽、
Version/Uri/StringIO/crypto 对象闭环、exdeep f18 EH 链、uint32 打印、half-
to-even 舍入、NaN hash) + 新增 18 例 13 PASS + **3 回归** (b59c6d9 Guid cast
过广 ×2 + 泛型 ctor 命名, 均 cmake_build, 官方 runner 复验) → 修复后 62/88
(70.5%) 达预期. **M5 33/34+** (仅 Encoding GetChars 分配形 1 缺), M5.5
typeinfo 域绿, **M6 转译器优化进行中** (尾调用在途未提交 +270 行; LICM 未
落地), M6.5 三轴绿延续 (WSL/MSVC 残项未动), **M7 fib 1.50×/array 1.33×
超 net48 保持**. 34 例剩余 FAIL = 转译 17 / 生成码 10 (含 3 回归) / diff 7 /
case 0. 工作区 51M/75U (代理活跃). **下一批 (批次 16)**: 3 回归修复 (R-A1
cast 收窄 / R-A2 泛型 ctor 命名) → matrix5 复验 62/88; diff 批 C 7 例; 接线
批 (Ldsflda/byte[] newarr/switch 0x45/FindIndex); M6 出口 (尾调用提交/LICM/
MethodMapHub/bench 入库/CI routine).
