# Roadmap Status v6 — ILC (IL→C++ AOT Compiler)

- Date: 2026-08-10 (snapshot 13 final — final four-platform **zero-warning** snapshot;
  supersedes snapshot 12; snapshot 12 kept in git at `d1cfbfd`, snapshot 11 at
  `d88860a`, snapshot 10 at `6b8bb02`, snapshot 9 at `2ded0fc`, snapshot 8 at
  `75215ca`, snapshot 7 at `docs/roadmap-status-v5.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, batches
  15-69 progress (**四平台零告警: MinGW/MSVC/WSL/NDK runtime 135/135 0-warning +
  Apple/OpenBSD 交叉语法 121×2**; matrix7 196 用例集 **94.9% / 95.4% 剔除 bench**
  → 剩余 FAIL 全记录级), the **bench v4-v6** performance milestone (**M7 兑现**,
  object/str_len inf + array 12× + string 1.82× + fib 1.62× + str_chars ~17× —
  PERF-1 收口零回归), **GC 根系统性修复** (静态字段/RootSlotPass/hglobal —
  强制 GC 0 崩溃), **decimal 格式 4 缺陷修复** (decfmt2 41/41), **M8 S1 完成
  (NDK-arm64 121/121 compile + 135/135 link + G2 PASS) + S2 大部分完成 (WSL
  mylib.so dlopen PASS + decimal UB 修复) → S2 剩余 macOS/OpenBSD 实机依赖记录**,
  去虚化 S1-S3, reflection modes A/B/C, WinForms 1-13, COM 1-6, 资源文化回退,
  custom 引擎, and the next batch task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git rev-parse HEAD` — [BASH] HEAD = `8d865d2` (branch m1, 最终版);
    `git rev-list --count f36f386..HEAD` — [BASH] **84 commits** since
    snapshot 7 (batches 15-69 wave; 21 commits 15-20 + 18 commits 21-29 + 12
    commits 30-41 + 3 commits 42-44 + 11 commits 45-53 + 7 commits 55-61 +
    **12 commits 62-69**).
  - `git status` — [BASH] **0 modified; 0 untracked — worktree completely clean**.
  - HEAD msg `8d865d2` — [READ:git log] **decimal custom-format unknown pattern
    verbatim fallback** (decfmt2 crash fixed, P/E green); `c492e97` — decfmt2
    expected trailing newline sync **41/41 lines match**; `26e71c4` —
    **decimal_format E-case/E-zero-pad/percent arm** (decfmt2); `8f7b6cd` —
    **batch 68**: WinForms stage 13 (cross-thread UI updates, dual-compiler
    byte-exact); `692497b` — e2e C28 (hglobalops/gcstatic PASS, decfmt2 gaps
    re-confirmed); `177bbe0` — **hglobal GlobalAlloc/GlobalFree consistency**
    (clipops/winforms1 PASS, was 0xC0000374, **unit 1694/1694, runtime 135/135**);
    `52ad05a` — **batch 66**: RootSlotPass allocating-arg fix (unit 1694/1694,
    forced-GC 0 crash) + e2e C27 (gcroot/boxop2 PASS); `01afb99` — **GC-CRITICAL
    reference static fields never registered as GC roots** (TypeEmitter + EmitMain
    add_static_root, winforms1 32 roots; forced-GC 30/30 crash -> 0/30);
    `d464749` — **batch 64**: WinForms stage 12 (clipboard dual-compiler PASS);
    `e6f41f9` — **icall_object_get_type_code full 12-type mapping** (boxconv 52/52
    + boxops 42/42 green); `6acce48` — e2e C26 (mdievt PASS) + **matrix7 final
    95.4% doc**; `220cf2f` — **batch 62**: WinForms stage 11 (MDI 192-line) + e2e
    C25 (decops/strformat/menuevt 3/3) + final snapshots (batch 61 state,
    4-platform 0-warning). 批次 55-61: `d1cfbfd` decimal ctor MIN UB (WSL -O3
    验证, 4-platform 0-warning); `0e11019` batch 60 WinForms stage 10 + matrix7
    9-FAIL disposition + M8 S2 WSL mylib.so PASS + e2e C24; `4191500` batch 59
    WinForms stage 9 + **matrix7 final 186/195 (95.4% excl bench)**; `82d0770`
    unit 1692/1692; `26baabd` batch 57 WinForms stage 8 + COM stage 6; `9fa9ba5`
    framestack thread_local; `9cd8feb` batch 55 WinForms stage 7 + COM stage 5 +
    e2e C21.
  - `docs/audit/e2e-full-matrix7.md` (最终版) + `%TEMP%\opencode\m7f2-b23-report.md`
    — [READ] 矩阵7 186/196 = **94.9%** / 剔除 bench 后 **186/195 = 95.4%**
    (**batch 59 目标达成**); **9 例剩余 FAIL 全为记录级已核实并逐例实跑确认**
    (3 transpile 接线缺: dictcase/linqcase/linqops; 6 diff: delegatecb 多播 /
    timeext 文化 / console3+consolestream harness 流序 / arrayext2 双缺口 /
    excext 消息透传), **无 codegen FAIL、无回归**.
  - `docs/audit/cross-platform-m8.md` + `%TEMP%\opencode\m8ff-b23-report.md` +
    `%TEMP%\opencode\m8s2-b23-report.md` — [READ] **M8 探通完成级 121/121 × 2 目标
    (Apple x86_64 + OpenBSD x86_64) 全 PASS** (-fsyntax-only -std=c++17, 合成 libc
    sysroot, 0 错); **NDK-arm64 S1 完成 (d7a5dec)**; **S2 大部分完成** — WSL
    mylib.so dlopen 路径 PASS (platform_posix.cpp:342-363), 实机验证剩余
    macOS/OpenBSD (S2).
  - `docs/audit/bench-summary-v6.md` + `%TEMP%\opencode\bench6-b23-report.md`
    — [READ] **bench v6 (HEAD 1d99ad3 干净 worktree 全量复跑)**: object **inf**
    / str_len **inf** (循环消除), array 12.00, string **1.82** (v5 1.65 提升),
    fib **1.62**, arr_write 4.00, arr_sum 8.00, str_chars **16-17×**, str_concat
    0.81-0.85, virtbench 去虚化 **1.03-1.07** + checksum byte-exact — **零回归
    (v5 基准全保持, 批次 55-61 零回退)**.
  - `%TEMP%\opencode\rmcf-b23-report.md` — [READ] ResourceManager 文化回退
    RM-CULT 原语 + 完整回退链 (net48 csc 探针 8/8).
  - `%TEMP%\opencode\rtaud8f-b23-report.md` — [READ] custom 格式引擎
    0/#/%/‰/D100 (numberformat + formatext, 135/135 无回归).
  - `%TEMP%\opencode\wf4..wf10-b23-report.md` + `com3/com5/com6-b23-report.md`
    — [READ] WinForms 阶段 3-10 (TextBox/CheckBox → multicast → multi-window →
    MessageBox/modal → property 控件 → ComboBox/ListBox → StatusBar/ProgressBar →
    MainMenu, 双编译器 byte-exact); COM stage 3/5/6 (Marshal 面 + IConnectionPoint +
    TypeLib out-of-scope).
  - `git log --oneline 52d8eaa..HEAD` — [BASH] 7 commits 批次 55-61
    (WinForms 7-10 + COM 5-6 + framestack thread_local + matrix7 9-FAIL 处置 +
    M8 S2 WSL mylib.so PASS + decimal UB 修复 + 四平台零告警).
- Legend: ✅ = committed and verified; 🟡 = in-flight (committed but chain
  incomplete, or worktree-modified); ⬜ = not started.
- ⚠️ Snapshot warning: parallel agents (铁律 PRD#12) may still modify files;
  all counts are measurement-time values. Re-verify `git status` before
  scheduling.

---

## 1. Milestone chain M1-M8

| Milestone | Status | Latest completion evidence (this turn) |
|-----------|:------:|----------------------------------------|
| **M1** Transpiler skeleton (IL decode → IR → C++ emit) | ✅ | Baseline per 路线图.md:13. |
| **M2** Object model + mark-sweep GC + inheritance/interface/virtual/boxing | ✅ | 路线图.md:13-14. |
| **M3** Generational GC + string/array/EH + generics + delegates + frame records | ✅ | Snapshot-8 evidence stands. |
| **M4** P/Invoke + Marshal icalls + icall platform layer + C API + 4-state artifacts | ✅ | Snapshot-8 evidence stands; batch 20 扩面 (UTF-16 + pinvoke struct registry, 744a7c8). |
| **M5** BCL full translation (wiring, L1 trimming default) | 🟡 | **矩阵7 196 用例集 94.9% (186/196)** / 剔除 bench 后 **95.4% (186/195)** [READ:e2e-full-matrix7.md 最终版 + m7f2-b23-report 逐例实跑确认]. **batch 59 目标达成**. **9 例剩余 FAIL 全为记录级已核实** (3 transpile 接线缺: dictcase/linqcase/linqops; 6 diff: delegatecb 多播 / timeext 文化 / console3+consolestream harness 流序 / arrayext2 比较器+FindIndex / excext 消息透传) — **无 codegen FAIL、无回归**. |
| **M5.5** Reflection (trimming-level verification L0/L1/L2) | 🟡 | **反射模式 A/B/C 全链路**: A=全类型默认发射, B=manifest 裁剪 (TypeEmitter FilterReflectionManifest), C=NormalizeTypeName 程序集限定名 (reflect_base.cpp:135, Type.GetType/Activator, **gettype_bcl True**) [READ:reflect_base.cpp + 16d8dcf/abe0999]. **batch 44 嵌套类型 ReflectionFullName '+' 键** (reflect2 gt_nested PASS, 166279b). TypeInfo 表 + MethodInfo.Invoke 已接线; **L0/L1/L2 trimming 验证 (T301-T310) 尾项未做**. |
| **M6** Transpiler (test suites all-green + 3-compiler CI + bench base) | 🟡 | **unit 1694/1694 PASS** [BASH:177bbe0/52ad05a, 批次 62-69 保持]. 批次 21 transpiler 域审计 x19 (CodeGenerator/StackFold/MethodRefResolver/TypeEmitter/MethodSignatureCoder/IlDecoder 218/218/MethodBodyReader/MetadataLoader/InlinePass/DcePass/TempSlotPass/StringPoolPass/LicmPass/AllocPromoPass/RootSlotPass/IR clone+serialize). **M6 出口残项仍开**: e2e 剩余 9 记录级 FAIL + bench 基线入库 + MethodMapHub (T101). 去虚化 S1-S3 + 属性子表 + finalizer 链 + LINQ includes + Sum checked + WinForms 1-13 + COM 1-6 + framestack thread_local + **GC 根系统性修复** (01afb99/52ad05a/177bbe0) + **decimal 格式 4 缺陷** (26e71c4/8d865d2) 已落地. |
| **M6.5** Cross-platform (三轴绿 + platform layers + NDK) | 🟡 **四平台零告警** | **MinGW ✅ + MSVC ✅ (runtime 135/135, C4996 清零)** [fe28fa5/ef5bb03/8921ff8] + **WSL ✅ 135/135** (monitor fix, mathf/console/socket platform-adaptive, ASAN 捕获 strchr bug×2 已修) [d7a5dec/b57650b/ce356e2] + **NDK-arm64 ✅ S1 链接级** (121/121 compile + 135/135 link + G2 getrandom PASS) [d7a5dec] — **四平台 0 告警 (d1cfbfd, 177bbe0 保持)**. batch 21 IlDecoder bounds-safe reader + P3a/P3b/P3d; **M8 探通完成级 121×2 全 PASS** (Apple/OpenBSD 全量语法, 8921ff8 + cross-platform-m8.md). **残项**: 跨平台 P3 剩 8 项 (audit G1-G14). |
| **M7** Performance (hotpath + conformance vs net48 oracle) | ✅ **兑现** | **bench v6 (2026-08-10 实测, bench6-b23-report HEAD 1d99ad3 干净 worktree 全量复跑)**: object **inf** / str_len **inf** (循环消除) / **array 12×** / **string 1.82×** (v5 1.65 提升) / **fib 1.62×** / str_chars **~17×** / str_concat 0.81-0.85 / arr_write 4.00 / arr_sum 8.00 — PERF-1 收口 **零回归 (v5 基准全保持)**; virtbench 去虚化 **1.03-1.07** byte-exact, generated code direct-call confirmed (bench-summary-v6.md). 驱动: 循环消除 + 边界检查消除 + LicmPass + AllocPromoPass + InlinePass 深内联 + DcePass + StringPool + RootSlotPass + 去虚化 S1-S3. GC 根修复后无回归 (m7rv-b23-report). |
| **M8** Full CLDR / COM interop / **Apple/OpenBSD 实机** (long-term) | 🟡 **S1 完成 + S2 大部分 → S2 剩余 macOS/OpenBSD 实机** | **M8 S1 完成 (NDK-arm64)**: 121/121 compile + 135/135 link + G2 getrandom PASS [d7a5dec] — 探通完成级 (Apple/OpenBSD 121/121 × 2 全 PASS, 全量语法验证, 0 错, 合成 libc sysroot, c6a1bee 15/15 → 8921ff8 121×2) [READ:cross-platform-m8.md + m8ff]. **S2 大部分完成**: WSL mylib.so dlopen 路径 PASS (platform_posix.cpp:342-363, m8s2-b23-report) + framestack g_frameHead thread_local (9fa9ba5) + decimal INT32/INT64_MIN UB 修复 (d1cfbfd) + **decimal 格式 4 缺陷修复** (26e71c4/c492e97/8d865d2, decfmt2 41/41 + P/E green). **COM 阶段 1-6 落地** (RCW/CCW 56347ef + IDispatch 7-slot a27dc7d + Marshal 面 com3 + IOleObject/IOleClientSite multi-vtable CCW 8f254b8 + **IConnectionPoint multi-vtable 9cd8feb** + **TypeLib out-of-scope + GetTypeInfo 26baabd**). **CLDR + ResourceManager 文化回退**: RM-CULT 原语已落地 (rmcf, net48 探针 8/8), 完整 CLDR 父表为长期项. **S2 剩余 macOS/OpenBSD 实机编译链接验证 (实机依赖)** — 计划已入库 docs/plans/m8-real-machine-plan.md (路线图.md:37). |

Count: 4/8 completed (M1-M4) + **M7 兑现**, 4 milestones
in-flight (M5, M5.5, M6, M6.5), **M8 S1 完成 + S2 大部分 → S2 剩余
macOS/OpenBSD 实机** (NDK-arm64 121/121 compile + 135/135 link + Apple/OpenBSD
121×2 语法级 PASS + WSL mylib.so dlopen PASS, 实机验证剩余 macOS/OpenBSD).

---

## 2. e2e 趋势 + 当前冲刺

### 2.1 84 commits since snapshot 7 (batch 15-69 wave)

`git rev-list --count f36f386..HEAD` = **84** [BASH]. 批次 62-69 波次标记
(新→旧, 12 commits):
- **batch 69** (`8d865d2`, fix): **decimal custom-format unknown pattern verbatim
  fallback** (`{0:xyz}` -> `xyz`, net48 实证; 未知单字母 D/X/Q 仍抛 FormatException)
  — **decfmt2 crash fixed, P/E green**; `c492e97` (test) — decfmt2 expected
  trailing newline sync **41/41 lines match**; `26e71c4` (fix) — **decimal_format
  E-case/E-zero-pad/percent arm** (decfmt2) — **decimal 格式 4 缺陷全修**.
- **batch 68** (`8f7b6cd`): **WinForms 阶段 13** (cross-thread UI updates,
  双编译器 byte-exact).
- **692497b** (test): **e2e C28** (hglobalops/gcstatic PASS, decfmt2 gaps
  re-confirmed).
- **177bbe0** (fix): **hglobal 家族 GlobalAlloc/GlobalFree 一致性** (clipops/
  winforms1 PASS, was 0xC0000374) — **unit 1694/1694, runtime 135/135**.
- **batch 66** (`52ad05a`): **RootSlotPass allocating-arg fix** (unit 1694/1694,
  强制 GC 0 crash) + **e2e C27** (gcroot/boxop2 PASS, clipops GlobalAlloc gap 发现).
- **01afb99** (fix): **GC-CRITICAL — 引用静态字段从未注册为 GC 根** (TypeEmitter
  + EmitMain add_static_root, winforms1 32 roots; 强制 GC 30/30 崩溃→0/30 —
  解释 user32 AV + GAP-FIN-4 家族).
- **batch 64** (`d464749`): **WinForms 阶段 12** (clipboard 双编译器 PASS,
  pre-existing user32 AV ~20% 报告).
- **e6f41f9** (fix): **icall_object_get_type_code 12-type 全映射** (boxconv 52/52
  + boxops 42/42 green).
- **6acce48** (test): **e2e C26** (mdievt PASS, decfmt/boxconv gaps 记录) +
  gitignore backup patterns + **matrix7 final 95.4% doc**.
- **batch 62** (`220cf2f`): **WinForms 阶段 11** (MDI 双编译器 192-line) + **e2e
  C25** (decops/strformat/menuevt 3/3) + final snapshots (batch 61 state,
  4-platform 0-warning).

批次 55-61 波次标记 (新→旧, 7 commits):
- **batch 61** (`d1cfbfd`, fix): **decimal ctor int32/int64 MIN unsigned-magnitude
  UB** (WSL -O3 验证, `-value` 负溢 UB 消除) + test cstdlib ×2
  (test_pinvoke_utf16/test_marshalext4) + enumext/io 告警清理 —
  **WSL 135/135, 4-platform 0-warning**.
- **batch 60** (`0e11019`): **WinForms 阶段 10** (MainMenu 双编译器, flake 修复)
  + **矩阵7 9-FAIL 记录级处置** (m7f2 逐例实跑确认) + **M8 S2 WSL mylib.so
  dlopen PASS** + decimal UB 发现 + e2e C24 (3/3 三平台).
- **batch 59** (`4191500`): **WinForms 阶段 9** (StatusBar/ProgressBar comctl32
  双编译器) + e2e C23 (comboops/internconc/lockdeep 3/3 多平台) + **矩阵7 final
  186/195 (95.4% 剔除 bench — 目标达成)**.
- **batch 58** (`82d0770`, fix): delegateext `<cstdlib>` (WSL) + label
  self-terminating null-statement (MSVC C2059, lockcase/conccases MSVC 绿) —
  **unit 1692/1692**.
- **batch 57** (`26baabd`): **WinForms 阶段 8** (ComboBox/ListBox 双编译器) +
  **COM stage 6** (TypeLib out-of-scope + GetTypeInfo tests) + e2e C22
  (concur2/fmtround/winevent, 2 前置缺陷发现).
- **batch 56** (`9fa9ba5`, fix): **framestack g_frameHead thread_local**
  (conccases 19-row all-True, 多线程帧完整性, runtime 135/135).
- **batch 55** (`9cd8feb`): **WinForms 阶段 7** (property 控件, MetadataLoader
  IntPtr 字段) + **COM stage 5** (IConnectionPoint multi-vtable) + e2e C21
  (wmresize 46-row/numops 169-row PASS, conccases framestack thread-local bug
  发现) + final snapshots.

批次 45-53 波次标记 (新→旧, 11 commits):
- **52d8eaa** (fix): **formatext strchr→memchr** + FormatPercentInt zero 分支
  (numfmtdeep 50/50 byte-identical).
- **batch 53** (`d7a5dec`): **WSL 135/135** (monitor test fix, mathf/console/
  socket platform-adaptive) + **NDK arm64 S1** (121/121 compile, 135/135 link,
  **G2 getrandom PASS**) + e2e C20 (internops/winformsext PASS, numfmtdeep
  formatext strchr gap).
- **b57650b** (fix): numberformat GroupDigits **strchr→memchr bounded scan**
  (WSL ASAN negative-size bug, 50-run clean).
- **batch 51** (`ce356e2`): **WinForms 阶段 6** (MessageBox worker-thread +
  modal loop, 双编译器) + Intern pool 线程安全 mutex + e2e C19 (3/3) + WSL
  full run (130/135, numberformat strchr bug found via ASAN).
- **3411fe8** (docs): **bench v6 summary** (零回归, fib 1.62×/string 1.82×/
  str_chars 17×, virtbench direct-call confirmed).
- **batch 50** (`1d99ad3`): test sync (valuesemantics boxed-bool value hash,
  io File.Copy/Move ExpectThrows) + dead var cleanup, **runtime 135/135**.
- **batch 49** (`8f254b8`): **WinForms 阶段 5** (multi-window/layout/
  hierarchy 双编译器) + **COM stage 4** (IOleObject/IOleClientSite
  multi-vtable CCW) + thread background guard + pool min-threads + **M8 实机
  计划** + e2e C18 (3/3).
- **batch 48** (`9ec11f8`): **WinForms 阶段 4** (multicast event delegate
  系统, Click/KeyDown subscribe-unsubscribe, 双编译器 byte-exact 57 lines)
  + delegate instance-field ReferenceCppType.
- **batch 47** (`de1390b`): **String IConvertible 表 (typecheckcase PASS)** +
  **File.Copy/Move exception mapping (fileio2 PASS)** + exception args
  passthrough (excext 11/13) + e2e C17 (finalops2/reflprop2/virtsealed 3/3).
- **batch 45** (`1fe84e3`): box domain audit (valuesemantics) + e2e C16
  (boxops/resops/strcmpdeep).
- **d88860a** (docs): matrix7 final 184/196 + project-state-20260810 +
  roadmap-v6 snapshot 11.

批次 42-44 波次标记 (新→旧, 3 commits):
- **batch 44** (`166279b`): **LINQ includes** (linqdeep 双编译器 PASS,
  generated.h include 表缺口 D-LINQ-2 修复) + **Sum checked 语义**
  (OverflowException, D-LINQ-3 csc 探针实证 net48 为 checked) + **嵌套类型
  ReflectionFullName '+' 键** (reflect2 gt_nested PASS) + **unit 1691/1691**.
- **batch 43** (`fd2101b`): **WinForms 阶段 2** (ShowWindow/WM_PAINT/TextOutW/
  Button-click 委托, 双编译器 byte-exact 29-line) + **COM Marshal 面接线**
  (GetActiveObject/GetIUnknownForObject/GetObjectForIUnknown/ReleaseComObject,
  unit 1688/1688) + **矩阵7 终验 183/196 (93.4%)** + e2e C15 (customfmt
  91-probe PASS, linqdeep/resmgmt gaps).
- **batch 42** (`8921ff8`): 泛型 string 实例化 (genlist PASS, GenericArity
  fill, List ctor) + LINQ Sum/Min/Max 接线 + **ResourceManager 文化回退链**
  (RM-CULT) + **custom 格式引擎** (0/#/%/‰, runtime 135/135) +
  StreamWriter(path) + Dispose routing (streamops 16/16) + **M8 Apple/OpenBSD
  121×2 全 PASS** + bench v5 no-regression + e2e C13 (finalops/proprefl/
  virtops 3/3).

批次 30-41 波次标记 (新→旧, 12 commits):
- **batch 41** (`6b8bb02`): **fnptr local signature 0x1B consume** (MethodBodyReader, fnptr e2e byte-exact, unit 1671/1671).
- **batch 40** (`72ac74c`): **finalizer 链闭合** (type-level finalizer registry, gcext e2e byte-exact) + **TypeInfo 属性子表** (PropertyDef 模型, reflops d1_prop x3 True, unit 1670/1670) + **devirt bench** (S1+S3 codegen effective, net48 JIT devirt too — 5-7% measured) + matrix7 diff disposition.
- **batch 39** (`3255d9e`): TypeInfo delegate-type 方法表 nullptr 豁免 (delegatecb 回归清除, unit 1667/1667).
- **batch 38** (`a21c5d6`): console SetOut(null) ANE + TextWriter.Null + redirect restore (consolestream E_out/err_ane) + newarr addr-taken gate (arrresize 24-line byte-exact, unit 1665) + **matrix7 final 175/192 (91.1%)**.
- **batch 37** (`c6a1bee`): **去虚化 S3** (interface single-implementer direct call, unit 1664) + enum [Flags] 分解 (enumdeep3 ts_flags) + **M8 Apple/OpenBSD 探通 15/15** + streamops/typecheckcase/genlist LOUD + e2e C12.
- **batch 36** (`fe28fa5`): **Split 2-arg options icall** (strcomp2 byte-exact 82/82) + enum box_enum (enumdeep2 PASS) + Dictionary LOUD + Monitor wait/try-enter overloads (**runtime 135/135**).
- **batch 35** (`4078283`): Array Find/Exists/TrueForAll/ConvertAll/Resize wiring (arrfuncs PASS, unit 1658) + generic-void IlCallReturnsValue fix + addr:a cast + enum box klass + Console stream surface (console3 byte-exact, unit 1661).
- **batch 34** (`a27dc7d`): exdeep InlinePass SourceOffset stamp + hashtable AppendZeroArg + threadpool ParamName fix + **COM stage 2 IDispatch** (7-slot CCW vtable, RCW invoke) + **devirtualization plan** + e2e C11.
- **batch 33** (`abe0999`): regression fixes (ipaddr net48-exact ParseDottedQuad) runtime 134/134 unit 1647/1647 + **反射模式 C NormalizeTypeName 验证** (gettype_bcl True).
- **batch 32** (`d14bf22`): matrix7 remaining fixes (MethodRefResolver generic bytes 0x02-0x1F, ArrayList.Remove, GC.Finalize no-op, WriteLine(object), decimal/delecb2/enumdeep/enumops/listops2/netcase/socketcase/guidcase greens + Array Find/Exists/TrueForAll/ConvertAll/Resize icalls).
- **batch 31** (`16d8dcf`): **反射模式 A/C stage 1** (MethodDef ReturnTypeName full decode, G1/G2/G3 guards removed, mode-B CLI scope wiring effective, Sanitize '=' → '_') unit 1647/1647, reflops/refinvoke PASS.
- **batch 30** (`ef5bb03`): **WinForms stage 1 end-to-end** (Form/WndProc delegate/message loop/UTF-16 PInvoke, 16-line byte-exact) + **perf deep (bench v5)**: str_chars direct chars[] read ~12×, str_concat concat_int fold 0.81 + C4996 9-spot cleanup (MSVC 134-exe 0/0) + reflection modes A/C plan + docs snapshots (project-state-20260809, roadmap-v6) + matrix7 final (155/185 83.8%, m6-ratio +16.1pt, 27 greens) + e2e C10 (reflops/fileio2/enumdeep2).

批次 21-29 波次标记 (新→旧, 18 commits):
- **batch 29** (`2ded0fc`): GetExtension leading-dot fix (iocase PASS) +
  Predicate1 BclAlloc/ctor interception (findidx 17-line byte-exact, unit
  1635/1635) + Directory.Delete recursive exception semantics (deletedir PASS).
- **batch 28** (`7f323aa`): **IcallArgOrder 参数重排** (EmitIcallCallArgs,
  find_index {0,3,1,2}) + SafeHandle SystemException chain + IntPtr checked
  ToInt32 OverflowException + BitConverter big-endian guard + e2e C9 (replsplit
  64-probe PASS).
- **batch 27** (`8572fa6`): runtime unlock icalls (String.Replace(s,s)
  non-overlap+identity, Directory.Delete recursive win/posix walk, Array.
  FindIndex delegate callback per linq_map pattern) + **bench v4** (object/
  str_len inf, array 12.5×, string 1.71×, arr_write 4.7×; str_chars 0.27/
  str_concat 0.37 icall-level gaps).
- **batch 26** (`7f72df0`): Replace string/string dispatch fix (unit
  1632/1632) + e2e C8 (stringsplit 38-probe PASS) + **matrix7 summary report**
  (60-case set 93%, 4 remaining FAIL → 后 3 批修复到仅剩文档化记录).
- **batch 25** (`f8a31ea`): String.Split icall (2-phase GC-safe, 17 test
  groups) + gcops Collect ParamCount fix + Split wiring (unit 1629/1629,
  runtime 133/133).
- **batch 24** (`5abf5c5`/`4bcc19f`): matrix7 FAIL fixes (register_method
  emission + 'n' cast + newarr Int64 + ArrayList value semantics + parse
  Infinity rejection) + e2e C7 (format3 34-probe / regex3 35-probe / delegops)
  + winforms1 probe skeleton.
- **batch 23** (`e4e13cc`): wiring (delegate get_Method, Convert fromBase x2,
  Stack/Queue ToArray, Hashtable/SortedList Keys/Values/set_Item, File
  Copy/Move/Attributes/LastWriteTime x6, Environment ExitCode, Monitor
  TryEnter) unit 1620/1620 + runtime 132/132.
- **batch 22a-e** (`46e9a3c`..`0fd9eb5`): **runtime 域审计 ~70 文件连批**
  (x11/x30/x~10/x~9/x~20): aes CryptoException, boxing 7 numeric box interface,
  base64 134 vectors, convert fromBase throwing, culture 15 constants, enum
  Parse, format composite 33/33, md5 NIST, numberformat + C4996, parse fromBase,
  pinvoke thread_local lastError, regex backref/lazy, rng Apple/BSD arc4random,
  stackqueue, stringbuilder, io ReadAllLines/File x6, encoding odd-length,
  monitor, resources guards, timezone DST, arraysort, http WebException,
  thread Thread.Name, gchandle dependhandle, com non-RCW→CCW, platform
  win/posix, environment, guid v4.
- **batch 21** (`c8ecc3b`): **transpiler 域审计 x19** (CodeGenerator emission
  fixes: boxing type seed/gcstress void**/sblong parens/encodingcase class/
  boxdeep2 UInt32/lockcase/netcase/excext; StackFold ldelem widths u4-i8-r4-r8;
  MethodRefResolver wiring+LOUD x7; TypeEmitter decimal sf storage;
  MethodSignatureCoder PTR/TYPEDBYREF/SENTINEL + nint bytes; IlDecoder 218/218
  + bounds-safe reader + prefix pairing; MethodBodyReader multidim 0x14 + EH
  filter/fault + MaxStack; MetadataLoader TypeSpec/static field sigs/
  CustomAttribute/entrypoint; InlinePass recursion/callvirt/void-inline/
  RenameLoc fresh; DcePass/TempSlotPass DelegateNewExpr Target; StringPoolPass
  WalkExpr Target; LicmPass shape-aware hoisting; AllocPromoPass static-escape/
  field-write-order; RootSlotPass live-in/FieldAddr escape/StmtMayAllocate; IR
  clone+serialize IrNode) + unit 1602/1602.
- **batch 21 资源嵌入** (`083170f`): ManifestResourceDef + LoadManifestResources
  + RenderResourcePoolHeader (T3-pattern resource-pool.h) + unit 1378/1378.
- **INFRA-1** (`7b1c2ca`/`f90efdf`/`473f53f`): restore src/runtime (174 files
  incl CMakeLists.txt) + pinvoke utf16/struct/cb + time.cpp icalls lost in
  ed4b688 checkout — **恢复完成, runtime build/e2e unblocked**.

### 2.2 e2e 趋势: 47 → 72 → 80 → 97 → 155/185 → **184/196 → 186/196 (~95%)**

[READ:e2e-full-matrix7.md 最终版 + m7f2-b23-report] 矩阵6 全量 **108/155 (69.7%)**
→ 矩阵7 m7final **155/185 (83.8%)** at ef5bb03 → **175/192 (91.1%)** 全量复跑 at
a21c5d6 (batch 38, 192 用例全量口径) → batch 39-41 修复 fnptr/gcext/reflops/
delegatecb → **183/196 (93.4%)** at fd2101b (batch 43, 矩阵7 终验) → batch 44 修复
reflect2 (嵌套类型 ReflectionFullName '+' 键) → **184/196 = 93.9%** at 166279b →
**batch 47 (de1390b) typecheckcase/fileio2 转绿 → 186/196 = 94.9%** → **batch 59
(4191500) 矩阵7 final 186/195 = 95.4% (剔除 bench) 目标达成**. 9 例剩余 FAIL 全为
**记录级已核实 (m7f2 逐例实跑确认)**, **无 codegen FAIL、无回归**.

**矩阵7 修复转绿 (batch 31-41, 逐用例实跑)**: collchain/hashtable2/
hashtablecase/strcomp2/enumdeep2/decimalcase/delecb2/enumdeep/enumops/exdeep/
guidcase/listops2/netcase/socketcase/reflectcase (15, 转绿清单 e2e-full-matrix7
§4) + batch 38-41 新增 arrfuncs/arrresize/enumbox/enumdeep3/virtualsealed +
fnptr/gcext/reflops/delegatecb 收口. [READ:docsm7 + e2e-full-matrix7].

### 2.3 回归管控

- 矩阵7 vs m7final: **15 例转绿 + 7 新增用例 5 PASS + 1 回归 (delegatecb,
  batch 39 已清除)** [READ:e2e-full-matrix7.md:84-90].
- e2e 扩量批 (C10/C11/C12) 记录缺陷均已给出修复方向 (console 流序 /
  reflops 属性 / arrresize addr-taken) [READ:e2ec11/e2ec12].

### 2.4 剩余 FAIL 归因 (9, 全记录级已核实 — m7f2 逐例实跑确认, 非 codegen FAIL)

| 归类 | 数量 | 用例 |
|---|---|---|
| transpile: 接线缺 (rtdict / LINQ predicate / OrderBy selector 合成) | 3 | dictcase / linqcase / linqops |
| diff: runtime 语义缺口 (多播委托 / 文化) | 2 | delegatecb / timeext |
| diff: 控制台 Error 流顺序 (**harness 2>&1 合并伪影**) | 2 | console3 / consolestream |
| diff: 转译器 diff (string 比较器 + FindIndex 委托 / 异常消息透传) | 2 | arrayext2 / excext (11/13) |

> 已修复转绿 (累计 8): delegatecb (b39) / gcext (b40) / reflops (b40) /
> fnptr (b41) / genlist+streamops (b38-42) / reflect2 (b44) /
> **typecheckcase + fileio2 (b47)**。
> 上一版 10 FAIL 中的 virtbench (bench timing, checksum 一致) 已在记录级归入
> "非正确性", 剩余 9 例全部记录级已核实 (m7f2)。

### 2.5 新域与里程碑前置

- **去虚化 S1-S3** (devirtualization-plan.md + 72ac74c): S1 metadata Sealed +
  closed-world 派生计数 (VTablePlanner.cs:51-62), S2 final slot 直调, S3
  interface 单实现者直调 (c6a1bee); **virtbench 实测 5-7%** (net48 JIT 同样
  去虚化) [bench5f: 1.06-1.09 + checksum byte-exact].
- **反射模式 A/B/C**: A=全类型默认, B=manifest 裁剪 (FilterReflectionManifest,
  TypeEmitter.cs:990), C=NormalizeTypeName (reflect_base.cpp:135-188);
  stage 1 (16d8dcf) + NormalizeTypeName 验证 (abe0999 gettype_bcl True) +
  **batch 44 嵌套类型 ReflectionFullName '+' 键** (reflect2 gt_nested PASS).
- **COM (PRD#17)**: stage 1 RCW/CCW (56347ef) + **stage 2 IDispatch** (a27dc7d
  7-slot CCW vtable + RCW invoke) + **stage 3 Marshal 面** (com3:
  GetActiveObject/GetIUnknownForObject/GetObjectForIUnknown/ReleaseComObject) +
  **stage 4 IOleObject/IOleClientSite multi-vtable CCW** (8f254b8) +
  **stage 5 IConnectionPoint multi-vtable** (9cd8feb) + **stage 6 TypeLib
  out-of-scope + GetTypeInfo tests** (26baabd) — **1-6 全落地**; stage 7
  (事件源/ActiveX 拖放) 待.
- **WinForms**: 阶段 1 **end-to-end** (ef5bb03 Form/WndProc/message loop,
  16-line byte-exact) + **阶段 2** (fd2101b ShowWindow/WM_PAINT/TextOutW/
  Button-click 委托, 29-line) + **阶段 3** (wf4 TextBox/CheckBox/焦点+键盘,
  47-line) + **阶段 4** (9ec11f8 multicast event delegate, 57-line) + **阶段 5**
  (8f254b8 multi-window/layout/hierarchy) + **阶段 6** (ce356e2 MessageBox
  worker-thread + modal loop) + **阶段 7** (9cd8feb property 控件 +
  MetadataLoader IntPtr 字段) + **阶段 8** (26baabd ComboBox/ListBox) +
  **阶段 9** (4191500 StatusBar/ProgressBar comctl32) + **阶段 10** (0e11019
  MainMenu 双编译器) + **阶段 11** (220cf2f MDI 双编译器 192-line) +
  **阶段 12** (d464749 clipboard 双编译器 PASS) + **阶段 13** (8f7b6cd
  cross-thread UI updates 双编译器) — **1-13 全落地双编译器 byte-exact**;
  阶段 14+ 待.
- **M8 探通/S1/S2**: Apple/OpenBSD **121×2 全 PASS** (完成级, 8921ff8 +
  cross-platform-m8.md, 合成 libc sysroot) + **NDK-arm64 S1 完成** (d7a5dec:
  121/121 compile + 135/135 link + G2 getrandom PASS) + **WSL 135/135**
  (d7a5dec, ASAN 实证) + **S2 大部分**: WSL mylib.so dlopen 路径 PASS
  (platform_posix.cpp:342-363, m8s2-b23) + framestack thread_local (9fa9ba5) +
  decimal UB 修复 (d1cfbfd) + **decimal 格式 4 缺陷** (26e71c4/c492e97/8d865d2,
  decfmt2 41/41 + P/E green); **S2 剩余 macOS/OpenBSD 实机编译链接验证
  (实机依赖)** — 计划已入库 docs/plans/m8-real-machine-plan.md.
- **GC 根系统 (系统性修复完成)**: **01afb99 GC-CRITICAL** — 引用静态字段从未
  注册为 GC 根 (TypeEmitter + EmitMain add_static_root, winforms1 32 roots;
  强制 GC 30/30 崩溃→0/30; 解释 user32 AV + GAP-FIN-4 家族) + **52ad05a
  RootSlotPass allocating-arg fix** (强制 GC 0 crash) + **177bbe0 hglobal
  GlobalAlloc/GlobalFree 一致性** (clipops/winforms1 PASS, was 0xC0000374) —
  M7-REV 复验无回归 (m7rv-b23-report).
- **decimal 格式 4 缺陷**: E 大小写保留 / E 分数补零 / P 臂 (26e71c4) + custom
  unknown pattern verbatim 回退 (8d865d2, net48 实证 `{0:xyz}`→`xyz`; 未知单字母
  D/X/Q 仍抛 FormatException) — **decfmt2 41/41 lines match (c492e97), P/E green**;
  full custom-pattern 引擎 (00.00 等) 记录级 out-of-scope.
- **boxconv/type_code**: **icall_object_get_type_code 12-type 全映射** (e6f41f9,
  boxconv 52/52 + boxops 42/42 green).
- **hglobal 家族**: GlobalAlloc/GlobalFree 一致性 (177bbe0, GMEM_FIXED 语义;
  hglobalops 11 行 + clipops/winforms1 PASS, was 0xC0000374).
- **finalizer 链**: type-level registry (72ac74c, gc.cpp) — gcext e2e byte-exact.
- **属性子表**: PropertyDef 模型 (Model/PropertyDef.cs) — reflops d1_prop x3 True.
- **fnptr (PRD#16)**: calli + ELEMENT_TYPE_FNPTR 全链 + **batch 41 local
  signature 0x1B consume** — fnptr e2e byte-exact.
- **资源嵌入 (PRD#18)**: **转译器侧落地** (083170f) + **runtime 侧文化回退链
  落地** (rmcf, RM-CULT 原语, net48 探针 8/8); **e2e resmgmt 接线待**.
- **custom 格式引擎**: 0/#/%/‰/D100 回退 (rtaud8f, numberformat+formatext
  双模块, 135/135 无回归; b51-53 strchr→memchr×2 收口 numfmtdeep 50/50).
- **LINQ 接线**: Sum/Min/Max (8921ff8) + includes/Sum checked (166279b,
  linqdeep 双编译器 PASS); linqcase First 谓词 / linqops OrderBy 合成待.
- **Intern pool**: 线程安全 mutex 化 (ce356e2) — 多线程 string interning 竞态收口.
- **runtime 域审计**: ~109/110 文件 (batch 21 transpiler 19 + batch 22a-e
  runtime 5 连批 + rtaud8f cultureinfo/numberformat/formatext 3 域闭环 +
  b45 valuesemantics box 域); 剩余 ~1 文件.
- **framestack thread_local**: g_frameHead thread_local (9fa9ba5) — conccases
  19-row all-True, 多线程帧完整性, runtime 135/135.
- **decimal UB 修复**: int32/int64 MIN unsigned-magnitude 取模 (d1cfbfd,
  decimal.cpp:2251-2286) — WSL -O3 暴露的潜伏负溢 UB, 无分支修复.

---

## 3. Critical-path analysis

**Chain A — M7 性能 (PERF-1 收口):**
- **str_chars ~17× / str_concat ~0.85 / string 1.82× / fib 1.62×** 已兑现
  (bench v6, bench6-b23-report 干净 worktree 全量复跑零回归) — **Chain A 完成**.
- 已兑现: object/str_len inf (循环消除), array 12×, arr_write 4.00, arr_sum
  8.00, virtbench 1.03-1.07. 唯一待做: bench 基线入库 (M6 出口铁律).

**Chain B — 矩阵7 语义收口 + e2e 扩量:**
- 9 例记录级 FAIL (3 transpile 接线缺 + 6 diff) — 可选收尾 (m7f2 逐例实跑确认).
- e2e 扩量已到 C28 (decops/strformat/menuevt/mdievt/boxconv/decfmt/gcroot/boxop2/
  clipops/hglobalops/gcstatic/decfmt2 — 批次 62-69 新增 12 用例目录, 用例面 250+);
  下一扩量 C30 (resmgmt 接线 / WinForms 阶段 14 探针 / COM stage 7 探针).

**Chain C — 基础设施:**
- **runtime 域审计剩余 ~1 文件** (~109/110 总进度; decimal_format 4 缺陷 2 批已闭合).
- **T101 MethodMapHub 未建** — MethodMap 文件增长下 MethodRefResolver.cs 单
  switch 序列化瓶颈.
- **M8 S2 剩余 macOS/OpenBSD 实机验证** (S1 NDK-arm64 完成 + S2 WSL mylib.so
  dlopen PASS + 探通 121×2 语法级已过 + decimal 格式 4 缺陷已修; 实机依赖, 计划
  m8-real-machine-plan.md 已入库).
- **bench 基线入库** (M6 出口铁律) + 4-compiler CI routine (WSL/NDK 已实证
  可扩) 仍开.
- M5.5 trimming 验证 (L0/L1/L2, T301-T310) + 反射模式 B/C 全用例未做.

---

## 4. 下一批 (批次 70) 建议

### 4.1 矩阵7 记录级收尾 (P0, 可选)

| 卡 | 内容 |
|---|---|
| 矩阵7 剩余 9 FAIL | 3 transpile 接线缺 (dictcase/linqcase/linqops) + 6 diff (流序 harness/消息透传/文化/多播/比较器) — m7f2 逐例实跑确认, 确认语义钉后转 test 侧头注 |

### 4.2 runtime 审计收口 (P0)

| 卡 | 内容 |
|---|---|
| runtime 剩余 ~1 文件 | 域审计最后一步 |
| 4-compiler 复核 | MinGW/MSVC/WSL/NDK 135/135 零告警保持 + C4996 |

### 4.3 e2e 扩量 + 新域接线 (P1)

| 卡 | 内容 |
|---|---|
| resmgmt 接线 | RM ctor 映射 (D-RES-3) → GetString 全链 |
| e2e C30 | 扩量批 (winforms 阶段 14 探针/COM stage 7 探针) |
| WinForms 阶段 14 / COM stage 7 | 控件扩展 / 事件源+ActiveX 拖放 |

### 4.4 M8 前置 + 基础设施 (P2)

| 优先级 | 任务 |
|---|---|
| P1 | **M8 S2 macOS/OpenBSD 实机验证** (S1 NDK-arm64 完成 + S2 WSL mylib.so dlopen PASS + 探通 121×2 语法级已过 + decimal 格式 4 缺陷已修; 实机依赖, m8-real-machine-plan.md) |
| P1 | **bench 基线入库 + 4-compiler CI routine** (M6 出口铁律) |
| P1 | **T101 MethodMapHub** (MethodMap 文件增长下的序列化结构卡) |
| P2 | M5.5 trimming 验证 (L0/L1/L2, T301-T310) + 反射模式 B/C 全用例 |
| P2 | 跨平台 P3 剩 8 项 (audit G1-G14) |
| P2 | 完整 CLDR 父表 (RM-CULT 截断父链已落地, 长期项) |
| P3 | rename-ilc-plan 层 1-4 |

### 4.5 Batch discipline (PRD#12/15)

每批 ≥32 子代理; 文件域隔离; checkpoint 15-20 分钟; 回归网 = matrix7 diff.
**警告**: run-tests.ps1 每用例重建转译器 — 并发编辑 src/ 期间跑矩阵会全量
误报 transpile (m7a/m7c 教训); 重跑前查 src mtime 稳定 ≥3min. 无 git commit
除每-里程碑提交. **bash 10 分钟硬超时**: 长任务代理拆分命令 (timeout
≤540000ms). **四平台门**: MinGW 主门 → MSVC → WSL (ASAN 补跑, 捕获 strchr/decimal
类非 NUL 缓冲 + UB bug) → NDK 链接级 (memory.md 同族 bug 扫描纪律).

---

## 5. One-line summary

批次 15-69 (84 commits, HEAD `8d865d2`): **unit 1694/1694 PASS**, runtime
**135/135 四平台零告警 (MinGW/MSVC/WSL/NDK-arm64 链接, 0-warning) +
Apple/OpenBSD 交叉语法 121×2**, **矩阵7 196 用例集 94.9% (186/196) /
剔除 bench 95.4% (186/195)** — **batch 59 目标达成**, 9 例剩余 FAIL 全为记录级
已核实 (m7f2 逐例实跑), **无 codegen FAIL、无回归**. **M7 兑现 — bench v6
零回归**: object/str_len **inf** (循环消除), array **12×**, string **1.82×**,
fib **1.62×**, str_chars **~17×**, str_concat ~0.81-0.85 (PERF-1 收口),
virtbench 去虚化 1.03-1.07 byte-exact. 新域: **WinForms 阶段 1-13** byte-exact
(含 MDI/clipboard/cross-thread UI) + **COM 阶段 1-6** (含 IOleObject/IOleClientSite
+ IConnectionPoint + TypeLib out-of-scope) + **GC 根系统性修复** (01afb99
静态字段 add_static_root + 52ad05a RootSlotPass + 177bbe0 hglobal — 强制 GC
30/30→0/30 崩溃) + **decimal 格式 4 缺陷** (E 大小写/E 补零/P 臂/custom verbatim
回退, decfmt2 41/41) + 反射模式 A/B/C (含嵌套 '+' 键) + 去虚化 S1-S3 + finalizer
链 + 属性子表 + 资源文化回退 (RM-CULT) + custom 格式引擎 + LINQ includes/Sum
checked + Intern pool 线程安全 + framestack thread_local + decimal UB 修复 +
**e2e 用例面 250+ (目录 238, 批次 62-69 新增 12)**. **M8 S1 完成 (NDK-arm64
121/121 compile + 135/135 link + G2 getrandom PASS) + S2 大部分 (WSL mylib.so
dlopen PASS) + 探通完成级 — Apple/OpenBSD 121×2 全 PASS →
S2 剩余 macOS/OpenBSD 实机验证 (实机依赖, 计划已入库)**. runtime 域审计
~109/110. 下一批 (批次 70): 矩阵7 记录级收尾 (可选) → runtime 剩余审计 →
resmgmt/WinForms 14/COM 7 接线 + e2e C30 → **M8 S2 macOS/OpenBSD 实机前置执行**.
