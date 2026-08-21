# Roadmap Status v3 — ILC (IL→C++ AOT Compiler)

- Date: 2026-08-05 (snapshot 5 — supersedes snapshot 4; snapshot 4 kept at
  `docs/roadmap-status-v2.md`, snapshot 3 at `docs/roadmap-status.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, the batch
  8-12 progress, the e2e trend + sprint work order (P0 Top-5 → 预计 55/88),
  and the next batch (批次 13+) task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git log --oneline -15` — [BASH] HEAD = `d670a84`; **28 commits since
    snapshot 4** (`67eb48c`): labeled stable batches 6/7/8/9/9b/10 +
    audit/test commits (full list in §2.1).
  - `git status --porcelain` — [BASH] **62 entries = 30 modified + 32
    untracked** (in-flight parallel-agent work; counts shift between calls —
    re-verify before scheduling).
  - `git ls-files src/il2cppc/Emit/MethodMap*.cs` — [BASH] **55 files**
    (snapshot 4: 54; +`MethodMap.ArrayList.cs` — ArrayList wiring LANDED at
    `01c1f14`).
  - `tests/unit` — [BASH] **639 `[Fact]`** (snapshot 4: 538).
  - `tests/runtime` — [BASH] **95 `test_*.cpp`** (snapshot 4: 88).
  - `tests/cases` — [BASH] **88 e2e case dirs**.
  - `docs/audit/e2e-full-matrix3.md` — [READ] matrix3 全量重跑 **PASS 47/88
    (53.4%)**, +8 vs matrix2, 零回归; 41 FAIL 分类 (transpile 26 / diff 12 /
    build 2 / case 2).
  - `docs/audit/bench-regression-check.md` — [READ] **fib 1.50× / array
    1.33× 超 net48**, 6/6 受测基准 ≥ 持平, 零退化.
  - `docs/audit/three-compiler-matrix.md` — [READ:106] 三轴编译结论:
    MinGW (clang 17) 零警告零错误 / MSVC (19.39) 88 目标全编译 / WSL
    (GCC 7.5) 全量 0 error.
  - `docs/audit/{msvc,wsl}-probe.md`, `ndk-probe.md` — [READ] 平台轴细节.
  - `docs/audit/version-object-closure.md` — [READ] Version 对象闭环完成
    (matrix3 之后落地 — versioncase 待 matrix4 确认).
- Legend: ✅ = committed and verified; 🟡 = in-flight (committed but chain
  incomplete, or worktree-modified); ⬜ = not started.
- ⚠️ Snapshot warning: parallel agents (铁律 2k, ≥24) continuously modify
  files; all counts are measurement-time values. Re-verify `git status`
  before scheduling. genlist 已入版 (v2 的 D2 已解, [BASH: git ls-files
  tests/cases/genlist = 3]).

---

## 1. Milestone chain M1-M8

| Milestone | Status | Latest completion evidence (this turn) |
|-----------|:------:|----------------------------------------|
| **M1** Transpiler skeleton (IL decode → IR → C++ emit) | ✅ | Baseline per 路线图.md:13. |
| **M2** Object model + mark-sweep GC + inheritance/interface/virtual/boxing | ✅ | 路线图.md:13-14. |
| **M3** Generational GC + string/array/EH + generics + delegates + frame records | ✅ | Snapshot-4 evidence stands. |
| **M4** P/Invoke + Marshal icalls + icall platform layer + C API + 4-state artifacts | ✅ | Snapshot-4 evidence stands. |
| **M5** BCL full translation (wiring, L1 trimming default) | 🟡 | **32/34 域完成** — 55 `MethodMap.*.cs` committed [BASH]. Batch 6-10 新增: ArrayList 域 (ctor/Add/get_Item/Count/Clear/RemoveAt/IndexOf/Contains, `01c1f14`), Guid op_Equality/op_Inequality + Parse strict (`eebb1a2`/`16dff4f`), Console WriteLine(ulong) 全范围 + UTF-16→UTF-8 (`18341a5`/`16dff4f`), Type.Name 短名 (`ac30d01`), Math.Round digits (`ac30d01`). **剩余 3 缺 (全 LOUD, 已逐行验证)**: ① Thread 实例态 — 无 Thread 对象模型 (ManagedThreadId/Priority 不可达), `CurrentThread` 抛 NotSupported [READ:MethodMap.Thread.cs:6,29,32]; ② Stopwatch 对象态 — 实例成员全 LOUD, 状态载体需 runtime 对象 [READ:MethodMap.Stopwatch.cs:30,62]; ③ Encoding 分配 — 剩余形态 (含实例/分配路径) LOUD [READ:MethodMap.Encoding.cs:36,77-111]. |
| **M5.5** Reflection (trimming-level verification L0/L1/L2) | 🟡 | **typeof/GetType 短名/typeinfo 域** [BASH: git log `ac30d01`] — typeof 链 StackFold 折叠 + `il2cpp_type_get_short_name` runtime 截短 (full→short, boxing2/boxdeep GetType 匹配) + MemberInfo::get_Name 映射; typeinfo 域已建 + `test_typeinfo` 绿 [BASH: 95 runtime tests]. L0/L1/L2 验证仍待 T301-T310. |
| **M6** Transpiler (test suites all-green + 3-compiler CI + bench base) | 🟡 | **泛型/嵌套/委托/EH 全落地**: GenericMethodParameter/MethodSpec 展开 (`0d28414`/`f75e6c0`), DecodeNestedBaseType, delegate multicast invoke_i4 (`8ae3c04`), EH-region LIFO 嵌套发射 + outer-first 排序 + catch-all 归一 + finally 中 rethrow (`f75e6c0`/`0d28414`). **尾调用: 进行中未落地** — src/il2cppc 无 tail-call 实现 (仅 CodeGenerator.cs:810 注释提及) [BASH: Select-String tail/Tail = 1 注释命中]. **字符串池 T3**: StringPoolPass layer1/2 诊断 (`0d28414`) + G1 InitializeArray RVA-blob fold (ldtoken FIELD→stelem, PEReader 接入, `8ae3c04`). **LICM: 未落地** (Normalize.cs 值域/逃逸扩展为下批 M7 优化器工作 [READ:bench-regression-check.md:84]). **边界检查**: 部分 — 常量边界折叠已发射 (newarr 已知长度 vs 循环归纳变量, CodeGenerator.cs:1704-1715,2170), 逐元素检查仍保留 (arr_write 0.83 待无检查快路径 [READ:bench-regression-check.md:85-87]). 单元 `[Fact]` = **639** [BASH], runtime = **95** [BASH], e2e = **88** [BASH]. |
| **M6.5** Cross-platform (三轴绿 + platform layers + NDK) | 🟡 | **三轴绿** [READ:three-compiler-matrix.md:106]: MinGW (clang 17) 零警告零错误; MSVC (VS2022 19.39) 88 目标全编译含测试 (C4996=0, 唯一噪音 platform_win.cpp:273); WSL (GCC 7.5) 全量 87 单元 0 error. **平台层**: pathproc POSIX 支持 (realpath/lexical fallback/TMPDIR/gettid, `eb57941`) + platform.h/win/posix 8 域 31-API + 17/17 首批双分支文件 [READ:cross-platform-design.md]. **NDK 探通**: NDK 26.1.10909125 已装, arm64-v8a 交叉编译可行, env 未设 [READ:ndk-probe.md]. **残项**: WSL pathproc.cpp 硬缺口 (86/87, +2 链接缺 Threads/-pthread) [READ:wsl-fullcheck.md:26,98]; MSVC 2 测试 exe 未编译 (test_numberformat/test_formatext, `msvc-probe.md` §2). |
| **M7** Performance (hotpath + conformance vs net48 oracle) | 🟡 | **性能铁律里程碑达成 — fib 1.50× / array 1.33× 超 net48** [READ:bench-regression-check.md:22-55]: fib 0.61→**1.50** (纯递归反超 JIT), array 0.19→**1.33** (2.4×慢→快), arr_sum 0.42→1.00, str_len 0.25→1.00, hash 0.40→1.00; loop 0.97 持平; sort 0.56, string 0.34, object 0.02, arr_write 0.83 (已知优化器差距). **零退化 6/6**. 根因: P0-1 内联三元式直访 `->data` (去 out-of-line `il2cpp_array_get_elem_ptr`), P0-2 Len 叶子帧裸体化 [READ:bench-regression-check.md:61-75]. M6 出口残项: bench 基线入库 + 3-compiler CI routine 仍开. |
| **M8** Full CLDR / COM interop (long-term) | ⬜ | 路线图.md:37. |

Count: 4/8 completed (M1-M4), 4/8 in-flight (M5, M5.5, M6, M6.5, M7 — 5 lines
marked 🟡), 1/8 not started (M8).

---

## 2. e2e 趋势 + 当前冲刺

### 2.1 28 commits since snapshot 4 (batches 6-10)

`git rev-list --count 67eb48c..HEAD` = **28** [BASH]. 批次标记:
- **batch 6**: ArrayList 域接线 + typeof-chain folds + Encoding sig-blob
  (`01c1f14`); pathproc POSIX + test (`eb57941`/`16cd109`); matrix2 + 三轴矩阵
  (`837ac4c`).
- **batch 7**: constrained. 前缀 0xFE16 + Version/Uri 全 LOUD + BclAllocExpr
  Object (`f30db55`); math.cpp Abs/Clamp 工厂链 + string get_length NRE
  (`2b19000`); e2e-recheck5 + 域测试 (`b034fda`).
- **batch 8**: G1 InitializeArray RVA-blob fold + G2 RawSignature BYREF +
  String op_Inequality + castclass 跟踪 + multicast invoke_i4 (`8ae3c04`);
  boxing 规范描述符 + unbox 托管理载器 (`823d105`/`bb43738`).
- **batch 9**: Guid.Parse strict + Math.Round digits + string.Join(object[])
  + console UTF-16→UTF-8 + System_String_class 规范 + Type.Name 短名
  (`16dff4f`/`ac30d01`); mathext Round oracle + join 元素 + canonical 断言
  (`893eac2`); e2e-recheck7 + 参考源 fetch 审计 (`9f3d4ac`).
- **batch 9b**: UInt32 box/unbox 全 32 位位型 + g_uintBoxClass + boxed_equals
  uint arm (`89a44e8`/`7091ead`).
- **batch 10**: Guid op_Equality/op_Inequality + WriteLine(ulong) 0x0B +
  throw null 类型化 NRE (`eebb1a2`); exception/concat/time 规范工厂 +
  formatext PadIntegerPart 防 UB + console UTF-8 编码 + G15 double 默认
  (`18341a5`); test_formatext '0' 格式 14 断言 + G15 pinning + u64 全范围
  (`bbc24b2`); **e2e-full-matrix3 (47/88) + boxing2-recheck + guid-op-equality**
  (`d670a84` = HEAD).

### 2.2 e2e 趋势: 39 → 47/88 (+8, 零回归)

[READ:e2e-full-matrix3.md:12-26] matrix2 39/88 (44.3%) → matrix3 **47/88
(53.4%)**, **+8 净增**, **零回归** (matrix2 全部 39 个 PASS 保持). recheck7
推估 44 (39+5) 被全量证实并**多 3**: boxing2 / interlockedcase / mathcase.

本轮 8 例 FAIL→PASS: boxing2, boxdeep, boxext, delegatecase
(0xC0000005 崩溃修复 0/57→57/57), interlockedcase, math2 (64/64),
mathcase, stringext2 (42/42) — 对应 boxing/delegate/math/Interlocked/
utf8 五批 runtime 修复 (`823d105`/`2b19000`/`93cb337`/`8ae3c04`).

**41 FAIL 归因分类** [READ:e2e-full-matrix3.md:133-154]:

| 类 | 例数 | 主因 |
|---|---|---|
| 转译器接线缺 (LOUD) | 26 (63.4%) | 未接 BCL 映射 (consolecase/excext/gcext/iocase/parsecase/randomcase/swcase/timecase/threadcase/delegatecb/strdeep/stringext/genlist/versioncase/encodingcase/base64case) + IL/fold 缺口 (delegate2/arrayext2/bitconvcase/guidcase/collchain) + static/instance 分类错 (pinvoke/pinvokeext) + TypeSpec (typecheckcase) + WriteLine(object) (hashtable2/hashtablecase) |
| runtime 输出不一致 (diff) | 12 (29.3%) | 数字/字符串格式 (convcase/convertcase/formatcase/mathext/formatext/stringbuilder) + hash (valuesem/gccase) + 异常 (cctorchain/exdeep) + culture (timeext) |
| 生成码 bug | 2 (4.9%) | delechain (类型错配), recurse (Main 签名) |
| 用例自身 | 2 (4.9%) | overflow (net48 CS0220), environment2 (计时抖动) |

**7 例前进到更深处** (matrix3 已越过旧闸门): gccase/valuesem 进入运行,
formatext 崩溃→全行输出, arrayext2/bitconvcase/collchain/guidcase/
threadcase/versioncase 越过早期闸门 — 下批修复面清晰.

### 2.3 冲刺施工单 — P0 Top-5 (预计 → 55/88)

| # | 卡片 | 修复内容 | 预计 |
|---|---|---|---|
| P0-1 | **versioncase** | **Version 对象闭环已落地** (matrix3 之后: versionuri.cpp/h 13 icall + MethodMap.Version.cs + MethodMap.Object.cs 1-line; versioncase 25/25 + net48 live diff PASS [READ:version-object-closure.md]) — 待 matrix4 全量确认 | 48/88 |
| P0-2 | **exdeep f14b + guidcase (callvirt System.Object 族, 同根)** | objTypes 注入局部变量声明类型 (LocNames/LocalKinds) → MethodMap.Object.cs System.Object 分支 → icall_object_to_string + null→NRE [READ:recurse-recheck.md:71-86]; guidcase 同根 + constrained callvirt ToString + guid 值槽 (op_Equality 已落地) | 50/88 |
| P0-3 | **diff 批 A — 数字/字符串格式 (5 例)** | convcase (uint→int -1 vs 4294967295), convertcase (rounding 2/3), formatcase+mathext (Infinity vs ∞ U+221E), formatext (货币符号 � vs ¥ — 73/73 行已全出) | 55/88 |
| P0-4 | **diff 批 B — hash/异常/容量 (4 例)** | valuesem (double NaN hash -524288 vs 2146959360), gccase (strhash same literal False), cctorchain (catch 计数 1 vs 2), stringbuilder (capacity 异常 vs 16) | +2 |
| P0-5 | **接线缺口批 (5 卡)** | TimeSpan.get_TotalMinutes, Stopwatch.get_IsRunning, Int32.TryParse ref/out, Path.Combine 3 参, String.Join 1 参 | +1~2 |

(预计合计: 47 + 1 + 2 + 5 + 2 + 2 ≈ **55/88** — 以 matrix4 全量为准.)

---

## 3. Critical-path analysis

**Chain A — e2e 转译器接线 (26 例, 最重):**
1. **callvirt System.Object 族 (exdeep f14b + guidcase)** — 同根: objTypes 只记
   newobj/box/GetType 结果, 普通局部变量 (声明类型 System.Object) 从不进入 →
   声明类型注入 + MethodMap.Object.cs 分支 + 空接收者 NRE [READ:recurse-recheck.md:71-86].
2. **Thread 对象模型 (threadcase)** — 已过 0xFE16 constrained 闸门, 卡
   `Thread.get_CurrentThread` 无对象模型 [READ:MethodMap.Thread.cs:6,29,32];
   M5 收口卡之一.
3. **Encoding raw-buffer (encodingcase/base64case)** — GetBytes raw-buffer LOUD;
   Encoding 分配路径 = M5 收口卡之一.
4. **UInt32 box 已落地** (batch 9b, `89a44e8`) — 非 Int32 box 支持实质完成,
   剩余 UInt32 打印路径 (convcase 属 diff 类).

**Chain B — diff 类 (12 例, 最易清零):** 数字格式 5 例 + hash 2 例 + 异常 2 例 +
culture 1 例 + capacity 1 例 — 全部有 net48 oracle, 单卡可修 [READ:matrix3 §3].

**Chain C — 基础设施:**
- **T101 MethodMapHub 仍未建** [BASH: git ls-files | Select-String MethodMapHub
  = 0] — MethodRefResolver.cs 单 switch 分发 (src/il2cppc/Emit/MethodRefResolver.cs),
  接线体量增长下的序列化瓶颈.
- **genlist gitignore 问题已解** (v2 的 D2 CLOSED): genlist 已入版 (3 文件,
  `8479991`) [BASH: git ls-files tests/cases/genlist = 3].

**Chain D — M6.5 残项:**
- WSL: pathproc.cpp POSIX 硬缺口 (86/87) + CMake `find_package(Threads)` /
  `-pthread` 链接缺 (glibc 2.27 需显式) [READ:wsl-fullcheck.md:26,42,75-98].
- MSVC: test_numberformat + test_formatext 2 exe 未编译 [READ:msvc-probe.md §2].
- P/Invoke mylib.dll vs libmylib.so 命名分歧; POSIX stubs (pathproc 全路径
  nullptr, temp /tmp, thread tid 占位) [READ:cross-platform-design.md §0.5-0.6].

**性能差距 (非退化, 已知优化器缺口)** [READ:bench-regression-check.md §5]:
- object 0.02 / string 0.34 — net48 JIT 逃逸分析 + 分配消除/下沉, ILC 每次迭代
  真实分配 → 需转译器逃逸分析 + LICM.
- arr_write 0.83 — 写循环每元素边界检查三元式; Normalize 已知 newarr 长度与
  循环归纳变量, 可证 `0<=idx<len` → 无检查快路径.
- int64 常量噪声: `(int32_t)(100000LL)` / `(loc2 < 100000LL)` 模式 [READ:
  bench-array gen 源码 :41,47].

---

## 4. 下一批 (批次 13+) 建议

### 4.1 冲刺施工单执行 (P0 Top-5, §2.3) — 预计 55/88

| # | 卡片 | 说明 |
|---|---|---|
| 1 | **matrix4 全量重跑** (执行后立证) | 确认 versioncase 闭环 + boxing 系零回归 + 47 保持; 产物 docs/audit/e2e-full-matrix4.md |
| 2 | **callvirt System.Object 统一修复** (exdeep f14b + guidcase) | 声明类型注入 → 同批双卡; guidcase 补 constrained ToString + 值槽 |
| 3 | **diff 批 A** (convcase/convertcase/formatcase/mathext/formatext) | 全部 net48 oracle 单卡 |
| 4 | **diff 批 B** (valuesem/gccase/cctorchain/stringbuilder) | hash 域 + 异常计数 + capacity 语义 |
| 5 | **接线缺口批** (TimeSpan.get_TotalMinutes / Stopwatch.get_IsRunning / TryParse ref-out / Path.Combine 3 参 / String.Join 1 参) | 5 短卡并行 |

### 4.2 M5 收口 (3 卡, 与 4.1 并行)

| 卡 | 内容 |
|---|---|
| Thread 实例态 | runtime Thread 对象模型 (CurrentThread + ManagedThreadId/Priority) + MethodMap.Thread.cs 实例映射 + threadcase 转 net48 live 对照 |
| Stopwatch 对象态 | runtime 状态载体 (开始时间/运行标志) + 实例成员映射 + swcase 转正 |
| Encoding 分配 | Encoding.GetBytes raw-buffer 路径 + 剩余形态 + encodingcase/base64case |

### 4.3 M6/M6.5/M7 出口

| 优先级 | 任务 |
|---|---|
| P1 | **尾调用 (tail call) 转译器支持** — 从零落地 (当前 src/il2cppc 无实现) |
| P1 | **LICM + 可证边界检查消除** — Normalize.cs 值域分析扩展; arr_write 0.83 → ≥1.0 |
| P1 | **逃逸分析 + 分配下沉** — object 0.02 / string 0.34 的优化器路径 |
| P1 | **T101 MethodMapHub** (registry + tail-chain) — 接线体量增长前的结构卡 |
| P1 | **bench 基线入库** (T605/T702/T707, M6 出口铁律 2e) — 08-05 数字为最新基线 (fib 1.50/array 1.33) |
| P1 | **3-compiler CI routine** (T701/T706) — 三轴矩阵固化到脚本 |
| P2 | M6.5 残项: pathproc.cpp + `find_package(Threads)`/`-pthread` + MSVC 2 exe + C4996 清点 + P/Invoke 命名 |
| P2 | M5.5 trimming 验证 (L0/L1/L2, T301-T310) |
| P3 | M6.5 DLR → M7 解释器 → M8 CLDR/COM |

### 4.4 Batch discipline (taskcards-128 §0, 铁律 2k)

Writer-uniqueness per file; GAPS.md for non-owned files; regression after each
wave: `dotnet build src/il2cppc/il2cppc.csproj` + `dotnet test
tests/unit/unit.csproj` + e2e runner (matrix4); 无 git commit 除每-里程碑提交
(铁律 2m). 工作区当前 30M/32U (平行代理活跃) — 编辑前先查 mtime/属主.

---

## 5. One-line summary

批次 8-12 后 (28 commits, HEAD `d670a84`): **M5 32/34 域完成** (55 MethodMap
文件; 剩 Thread 实例态/Stopwatch 对象态/Encoding 分配 3 卡 LOUD), M5.5
typeof/GetType 短名/typeinfo 域落地, M6 泛型/嵌套/委托/EH/字符串池 T3
(RVA-blob fold)/常量边界折叠落地 (尾调用/LICM 进行中未落地), M6.5 三轴绿
(MinGW 零警/MSVC 88 目标/WSL 0-error) + NDK 探通, **M7 性能铁律里程碑达成 —
fib 1.50× / array 1.33× 超 net48 (零退化 6/6)**; e2e 39→47/88 (+8 零回归),
Version 对象闭环在 matrix3 后落地待确认. 工作区 30M/32U (代理活跃).
**下一批 (批次 13+)**: P0 Top-5 施工单 (versioncase 确认/callvirt
System.Object 族/diff 双批/接线缺口批) → 预计 **55/88**; M5 收口 3 卡;
M6/M7 出口 (尾调用/LICM/逃逸分析/MethodMapHub/bench 入库/CI routine).
