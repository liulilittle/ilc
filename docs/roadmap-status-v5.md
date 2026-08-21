# Roadmap Status v5 — ILC (IL→C++ AOT Compiler)

- Date: 2026-08-07 (snapshot 8 — supersedes snapshot 7; snapshot 7 kept in git
  at commit `75215ca`, snapshot 6 at `docs/roadmap-status-v4.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  15-20 progress (矩阵6 全量 108/155 + 矩阵7 进行中), the **bench v3**
  performance milestone (M7 大幅推进, 6/10 反超 net48), and the next batch
  task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` — [BASH] HEAD = `efa74e3` (branch m1, M6 batch 20);
    `git rev-list --count f36f386..HEAD` — [BASH] **21 commits** since
    snapshot 7 (batches 15-20 wave).
  - `git status` — [BASH] **working tree clean** (0 modified 0 untracked;
    矩阵7 验证代理 m7a-m7d + 30+ b23 域代理在飞, 产物全落 %TEMP%\opencode\).
  - `dotnet test tests/unit/unit.csproj -c Release` — [BASH] **1369/1369
    PASS, exit 0** ([Fact]=1200 + [Theory]=32, 理论行展开后 1369).
  - `tests/runtime` — [BASH] **127 `test_*.cpp`**; icallc-b23-report 快照
    **123/123 exe PASS** (19:25); HEAD msg MSVC gate **122/122** + POSIX
    gate 0-warn.
  - `tests/bench/results/2026-08-07_1828-mingw.csv` — [BASH] fib 1.50 / loop
    0.94 / string 1.06 / array 6.00 / object 0.04 / sort 0.60 / hash 0.86.
  - `tests/bench-array/results/2026-08-07_1842-array-mingw.csv` — [BASH]
    arr_sum 8.00 / arr_write 4.20 / str_len 1.10 (core); arr_copy/str_chars/
    str_concat FAIL.
  - `docs/audit/e2e-full-matrix6.md` — [READ] matrix6 全量 **PASS 108/155
    (69.7%)**, 131 交集 **97/131 (74.0%, +5 vs matrix5)**; 8 新绿 + 3 回归
    (crypto_obj.h/timezone.h include, 后续已修).
  - `%TEMP%\opencode\m7a-checkpoint.md` + `m7b-checkpoint.md` — [READ]
    matrix7 batch1 **PASS 32** (41 有效结果) / batch2 **PASS 39** (55 例);
    batch3 (m7c) 首跑被 8 并发 il2cppc build 污染, 待重跑.
  - `%TEMP%\opencode\bench3-b23-report.md` — [READ] bench v3 报告 (18:28/
    18:42 实测, HEAD 41072f4, 工作树 clean 无在途).
  - `docs/plans/winforms-plan.md` — [READ] WinForms 可行性: 2442 类型 / 625
    P/Invoke / 198 ComImport / 223 嵌入资源; 3 阶段路线 (41072f4 入库).
  - 批次口径: 批次 15-20 = batch 15-20 代码波 (`e829e98..efa74e3`, 21
    commits); matrix6 = 批次 15 验证波 (75215ca 入库); matrix7 = 批次 20+
    验证波 (进行中, m7a-d).
- Legend: ✅ = committed and verified; 🟡 = in-flight (committed but chain
  incomplete, or worktree-modified); ⬜ = not started.
- ⚠️ Snapshot warning: parallel agents (铁律 PRD#12) may still modify files;
  all counts are measurement-time values. **矩阵7 仍在进行** (batch3 未重跑,
  batch1 14 例 BLOCKED 待复核). Re-verify `git status` before scheduling.

---

## 1. Milestone chain M1-M8

| Milestone | Status | Latest completion evidence (this turn) |
|-----------|:------:|----------------------------------------|
| **M1** Transpiler skeleton (IL decode → IR → C++ emit) | ✅ | Baseline per 路线图.md:13. |
| **M2** Object model + mark-sweep GC + inheritance/interface/virtual/boxing | ✅ | 路线图.md:13-14. |
| **M3** Generational GC + string/array/EH + generics + delegates + frame records | ✅ | Snapshot-7 evidence stands. |
| **M4** P/Invoke + Marshal icalls + icall platform layer + C API + 4-state artifacts | ✅ | Snapshot-7 evidence stands; batch 20 扩面 (UTF-16 + pinvoke struct registry, 744a7c8). |
| **M5** BCL full translation (wiring, L1 trimming default) | 🟡 | **头文件架构大幅合并 (104→27, 批次 15-20)** 后口径重置: icall 声明 **262**, 转译器引用 504 唯一名, 声明∩接线 171 [BASH 本快照实测]. 批次 17-18 对象层大推进: http WebClient / streamio 16 icall 对象层 / gchandle 8 / dependhandle 7 / gc 18 / string allocation 9 / encoding GetChars 3-enc 分配形 / XML 5 / MathF 24 / reflecthandle 10. **矩阵7 进行中** — 剩余 FAIL 归因见 §3 (genlist 泛型 0x0E / iocase Directory.Delete / linqcase 0x02 / parsecase fromBase / collchain Remove / FindIndex 等). |
| **M5.5** Reflection (trimming-level verification L0/L1/L2) | 🟡 | TypeInfo 表发射修复 (bf4e32e: non-const globals + HasClassDescriptor/IsDelegateType) → reflectcase/refinvoke 转绿; MethodInfo.Invoke 接线 (345ec8c). **L0/L1/L2 trimming 验证 (T301-T310) 仍未做** — 结论延续. |
| **M6** Transpiler (test suites all-green + 3-compiler CI + bench base) | 🟡 | **unit 1369/1369 PASS** [BASH]. 批次 15-20 落地: DcePass / AllocPromoPass (SROA) / RootSlotPass / TempSlotPass 深化 / InlinePass 深内联 (nested-call + callvirt-by-token) / **IL 全指令审计 217/218 decode** (arglist/endfilter/typedref LOUD, 345ec8c+ed4b688) / #44 HAS_THIS / multidim 0x14 / cctor 守卫 / Main argv / TypeInfo 表. **M6 出口残项仍开**: e2e 未全绿 (矩阵7 进行中) + bench 基线入库 + MethodMapHub (T101). |
| **M6.5** Cross-platform (三轴绿 + platform layers + NDK) | 🟡 | **POSIX gate 0-warn / MSVC 122/122 (C4996×11 非新域)** [efa74e3 msg]. 批次 18 P3a (Android tmpdir /data/local/tmp) + P3b (guid CSPRNG RFC4122 v4) + P3d (stopwatch 去重 + pathproc plat_ 迁移) 落地. **残项**: 跨平台 P3 剩 8 项 (audit G1-G14). |
| **M7** Performance (hotpath + conformance vs net48 oracle) | 🟡 → **大幅推进** | **bench v3 (2026-08-07 实测) 6/10 反超 net48**: fib 1.50 / **array 6.00** / **string 1.06** / **arr_sum 8.00** / **arr_write 4.20** / **str_len 1.10**; 其余 loop 0.94 / sort 0.60 / hash 0.86 (噪声带) + **object 0.04 (唯一大差距)** [BASH CSV]. 驱动: 边界检查消除 (ComputeLoopSafeAccesses, arr_write 0.84→4.20 首个完整验证) + DcePass + InlinePass 深内联 + AllocPromoPass (object 相对净提升) + StringPool T3 (string 0.34→1.06 反超) + RootSlotPass. **剩余**: object 0.04 需完整逃逸分析 + 分配下沉. |
| **M8** Full CLDR / COM interop (long-term) | ⬜ | 路线图.md:37. COM **stage 1 已提前启动** (56347ef RCW/CCW, 见 §2.5). |

Count: 4/8 completed (M1-M4), 5 milestones in-flight (M5, M5.5, M6, M6.5, M7),
1/8 not started (M8).

---

## 2. e2e 趋势 + 当前冲刺

### 2.1 21 commits since snapshot 7 (batch 15-20 wave)

`git rev-list --count f36f386..HEAD` = **21** [BASH]. 波次标记 (新→旧):
- **batch 20** (`ed4b688`/`744a7c8`/`efa74e3`): IL gap batch 2 (StackFold +520:
  ldind/stind/cpobj 宽度 fold, ldobj/stobj/initobj/sizeof tokens, cpblk/initblk,
  localloc, ckfinite, jmp tailcall, checked ovf 家族 + rem_un; arglist/
  endfilter/typedref LOUD) + WIR-3 接线 (泛型 0x0E/0x02, COM flip A/B, Enum/
  Type/SafeHandle) + **P/Invoke UTF-16 marshal** + **pinvoke struct registry**
  (x64 MSG.pt@36 ABI) + StackFold 30 gap tests + test_pinvoke_utf16/struct +
  bench v3 + MSVC/POSIX gate.
- **batch 19** (`345ec8c`/`56347ef`/`41072f4`): **calli + ELEMENT_TYPE_FNPTR 全链**
  (PRD#16 达成, fnptr e2e PASS) + **IL 全指令审计 217/218** + COM domain
  stage 1 (RCW/CCW, PRD#17 起步) + winforms-plan.md + 接线/CodeGenerator 大补丁
  (WebClient/GCHandle/DependentHandle/MathF/Encoding GetChars/TypeInfo emit/
  MethodInfo.Invoke 等).
- **batch 18** (`bf4e32e`/`d704aed`): TypeInfo 表修复 + MathF 24 (corert float
  语义, soft fma) + reflecthandle 10 + platform P3a/b/d + Encoding GetChars
  3-enc + Thread 5 + XML 5.
- **batch 17** (`2b35d36`/`1066ad7`/`af34594`): **DcePass + AllocPromoPass +
  InlinePass 深化 + RootSlotPass** (OPT 大波; object 0.036→0, string 1.11→1.79
  中间测量) + http/streamio/gchandle/dependhandle/gc/stringalloc 对象层.
- **batch 16** (`252ab4b`/`68172df`/`a7e1496`): icall gap wiring batch 1 +
  FMT-2 + LANG-1 全英语 + cross-platform P1/P2 + icall-inventory-net48/core +
  cross-platform-audit.
- **batch 15** (`e829e98`..`75215ca`): delegate 域 (#42 2-arg / #44 HAS_THIS) +
  multidim + cctor 守卫 + Main argv + **matrix6 全量 108/155** + 上一快照文档波.

### 2.2 e2e 趋势: 47 → 72 → 80 → 97 (131 交集口径)

[READ:e2e-full-matrix6.md + m7a/m7b] PASS 轨迹 (131 交集口径): matrix5
92/131 (70.2%) → **matrix6 97/131 (74.0%, +5)**; 矩阵6 全量 = **108/155
(69.7%)**. **矩阵7 进行中**: batch1 **32 PASS** (55 例 41 有效, 14 例 BLOCKED
= 并发 src 编辑误报) + batch2 **39 PASS** (55 例, transpile 7 / build 8 /
diff 1) + batch3 污染待重跑 [READ:m7a/m7b/m7c].

**矩阵6 的 8 新绿** + **3 回归 (crypto_obj.h/timezone.h include, batch 20 已修 —
hashcase/hashchain 矩阵7 翻 PASS)** [READ:m7b 转绿清单].

### 2.3 回归管控

- matrix6 的 3 例回归 (crypto_obj.h/timezone.h include) 已在批次 19-20 修复,
  矩阵7 实证转绿 (hashcase/hashchain) [READ:m7b].
- matrix5 的 internchain 回归 (StringPool T3 Concat 池化) — **矩阵7 仍绿**
  (internchain PASS, m7b) — 该语义分歧已通过池化范围收窄解决, 不再列为回归.
- 回归网 = 矩阵 diff 阶段拦截 (两阶段实证有效).

### 2.4 矩阵7 剩余 FAIL 归因 (进行中, m7d 汇总)

| 类 | 例数 | 主因 |
|---|---|---|
| transpile (LOUD/接线缺) | 7 | genlist (泛型 0x0E string) / hashtable2+hashtablecase (WriteLine(object) box) / iocase (Directory.Delete recursive) / linqcase+linqops (0x02 byte) / parsecase (fromBase throwing icall) |
| build (生成码 bug) | 8 | System_*_class 未声明 (excext Action/gcext GC/listops ArrayList/collections2 Stack+Queue/delegatecb) / static_cast Node**→void** (gcstress/objects/propcase) / lockcase int→Il2CppObject* / netcase ipaddress + int→object / decimalcase/convertcase/delechain 等 matrix6 遗留 |
| diff | 1 | format2 ({N,width}/{0:f2}/{0:x} AppendFormatCore 格式器不支持) |

### 2.5 新域与里程碑前置

- **COM (PRD#17) stage 1 落地** (56347ef): RCW CoCreateInstance/QueryInterface/
  GetActiveObject (oleaut32), CCW IUnknown vtable; Windows-only 保证 + 跨平台
  stub 不崩; test_com 全平台构建. **M8 的 COM 里程碑已提前启动**.
- **fnptr (PRD#16) 达成** (345ec8c): calli + ELEMENT_TYPE_FNPTR 全链, fnptr
  e2e PASS.
- **WinForms 计划** (41072f4, winforms-plan.md): net48 实测 625 P/Invoke
  (user32 281) / 198 ComImport / 223 嵌入资源; 3 阶段路线; 阶段 1 前置 = 5 个
  P/Invoke marshal 缺口 (UTF-16/结构体/IntPtr 参数/byte[] 参数/委托回调) —
  UTF-16 + 结构体 registry 已在 batch 20 落地.
- **资源嵌入 (PRD#18)**: RES-1 施工单, 未实施 (转译器 ManifestResource 0 匹配).

---

## 3. Critical-path analysis

**Chain A — 矩阵7 收口 (最重):**
1. **batch3 重跑**: m7c 首跑 47 例全 transpile 误报 (8 并发 il2cppc build
   竞争) — 用 concurrency 2 + src mtime 稳定检查重跑 (运行窗口 111-155 例).
2. **m7a 14 例 BLOCKED 复核**: delegate2/delegatecb/dictcase/encodingcase/
   enumdeep 等 (当时 StackFold.cs:1306/CodeGenerator.cs:3314 CS 编译错已随
   batch 20 修复) — 重跑确认.
3. **transpile 缺口 7 卡**: 泛型 0x0E/0x02 展开 (genlist/linqcase/dictcase) +
   Directory.Delete 非递归 icall + fromBase throwing icall + WriteLine(object)
   box 类型跟踪 + FindIndex 委托形参.
4. **build 缺口 8 卡**: System_*_class metadata extern 声明链 (listops/
   collections2/excext/gcext/delegatecb) + static_cast Node**→void** (GC 参数
   引用) + lockcase/netcase int↔Il2CppObject*.

**Chain B — M7 性能 (object 0.04×):**
- 完整逃逸分析 + 分配下沉 (RootSlotPass 已有根集基础; AllocPromoPass SROA
  已相对净提升但未消除分配) — 唯一 ratio<0.1 项, net48 JIT ~1ns/iter vs
  ILC 275ms/10ms.
- bench FULL 恢复: arr_copy/str_chars/str_concat 三基准 wiring gap (String.
  Concat / get_Chars / Array.Copy) 未通 → string 基准无法完整回归.

**Chain C — 基础设施:**
- **T101 MethodMapHub 未建** — MethodMap 文件 **75** 下 MethodRefResolver.cs
  单 switch 序列化瓶颈 (snapshot 7 结论延续).
- **bench 基线入库** (M6 出口铁律) + **3-compiler CI routine** 仍开.
- M5.5 trimming 验证 (L0/L1/L2, T301-T310) 未做.

---

## 4. 下一批 (批次 21) 建议

### 4.1 矩阵7 收口 (P0, 最高优先)

| 卡 | 内容 |
|---|---|
| 重跑 | matrix7 batch3 (m7c 污染批) + m7a 14 BLOCKED 例复核 (concurrency 2) |
| 汇总 | 矩阵7 全量 → 与 matrix6 逐例 diff → 归因剩余 FAIL |

### 4.2 transpile 接线批 (P1, 矩阵7 归因)

| 卡 | 内容 |
|---|---|
| genlist/dictcase/linqcase | 泛型实参 0x0E/0x02 展开 (M3 缺口, MethodRefResolver.cs:853) |
| iocase | Directory.Delete(path, recursive) 非递归 icall 缺口 |
| parsecase | Convert.ToInt32(string, fromBase) throwing icall |
| hashtable2/hashtablecase | WriteLine(object) box 类型跟踪 |
| arrayext2 | Array.FindIndex (Predicate 委托形参 icall) |

### 4.3 生成码 bug 批 (P1, 矩阵7 build FAIL)

| 卡 | 内容 |
|---|---|
| listops/collections2 | System_Collections_Stack/Queue/ArrayList_class 未声明 (metadata extern) |
| excext/gcext/delegatecb | System_Action_class / GC_WaitForPendingFinalizers / icall_delegate_get_fn_ptr void*→intptr_t |
| gcstress/objects/propcase | static_cast Node**→void** (GC 参数引用) |
| lockcase/netcase | int→Il2CppObject* 初始化 / il2cpp_ipaddress_parse |

### 4.4 M7 性能 (P1, 最后大项)

| 卡 | 内容 |
|---|---|
| **逃逸分析 + 分配下沉** | object 0.04× → 目标 ≥1.0; 完整逃逸 (RootSlotPass 基础) + 分配下沉/LICM |
| bench FULL wiring | arr_copy/str_chars/str_concat (String.Concat/get_Chars/Array.Copy) 恢复完整 string 基准 |

### 4.5 M6 出口 + M6.5 + M5.5

| 优先级 | 任务 |
|---|---|
| P1 | **bench 基线入库 + 3-compiler CI routine** (M6 出口铁律) |
| P1 | **T101 MethodMapHub** (75 文件下的序列化结构卡) |
| P2 | M5.5 trimming 验证 (L0/L1/L2, T301-T310) |
| P2 | 跨平台 P3 剩 8 项 (audit G1-G14) |
| P3 | COM stage 2+ (拖放/ActiveX → M8) / 资源嵌入 RES-1 / Apple/OpenBSD |

### 4.6 Batch discipline (PRD#12/15)

每批 ≥32 子代理; 文件域隔离; checkpoint 15-20 分钟; 回归网 = matrix7 diff.
**警告**: run-tests.ps1 每用例重建转译器 — 并发编辑 src/ 期间跑矩阵会全量
误报 transpile (m7a/m7c 教训); 重跑前查 src mtime 稳定 ≥3min. 无 git commit
除每-里程碑提交.

---

## 5. One-line summary

批次 15-20 (21 commits, HEAD `efa74e3`): **unit 1369/1369 PASS**, runtime
**123/123 exe PASS** (MSVC 122/122, POSIX 0-warn), **矩阵6 108/155 (69.7%) /
131 交集 97/131 (74.0%)**, **矩阵7 进行中 (batch1 32 + batch2 39 PASS)**.
**M7 大幅推进 — bench v3 6/10 反超 net48** (array 6.00×/arr_sum 8.00×/
arr_write 4.20×/string 1.06×; 唯一大差距 object 0.04× 待逃逸分析). 新域:
COM stage 1 (RCW/CCW) + calli/FNPTR 全链 + IL 217/218 审计 + WinForms 计划 +
P/Invoke UTF-16/结构体 registry. 头文件架构合并 (104→27). **M8 待** (COM 全量/
CLDR/Apple/OpenBSD). 下一批 (批次 21): 矩阵7 收口 (batch3 重跑 + BLOCKED
复核) → transpile 7 卡 + build 8 卡 → 逃逸分析 (object) → M6 出口
(bench 入库 + CI routine + MethodMapHub).
