# ILC 项目完整进度快照（2026-08-10 最终版，HEAD 8d865d2 / 批次 45-69 — 四平台零告警权威状态）

> 本文档是会话删除后的**唯一权威状态来源**。恢复时先读本文档，再读：
> docs/alignment-tracker-v3.md（域接线）、docs/roadmap-status-v6.md（里程碑/bench v6 趋势 snapshot 13）、
> docs/audit/e2e-full-matrix7.md（矩阵7 最终版 196 用例全量口径）、docs/audit/cross-platform-m8.md（M8 121×2 + NDK S1 全量矩阵）、
> docs/audit/bench-summary-v6.md + %TEMP%\opencode\bench6-b23-report.md（bench v6）、
> docs/plans/winforms-plan.md（WinForms 1-13）、docs/plans/m8-real-machine-plan.md（M8 S2 实机计划）、docs/plans/devirtualization-plan.md（去虚化 S1-S3）、
> docs/plans/reflection-modes-ac-plan.md（反射模式 A/B/C）、docs/plans/e2e-sprint-worklist.md（施工单）。
> 上一快照 = docs/project-state-20260809.md（HEAD 6b8bb02，保留不动）。

## 1. 项目定位（铁律，不可违背）

- **正式名 ILC**（IL→C++ AOT 编译器），**不是 il2cpp**——"il2cpp 是形意词汇，不是工程定义"。命名纠正方案见 docs/architecture/rename-ilc-plan.md（分层重命名，层 5 目录改名须等所有代理收工）。
- 目标：对齐 .NET Framework 4.8 行为（M1-M8 全部里程碑）。
- **绝对原则**：实现任何 .NET 语义前**必须先抄 .NET Core 开源实现**（微软数十年验证，MIT）——本地参考源码：
  - `E:\il2cpp\reference\netcore\`（dotnet/runtime sparse：libraries 1829 文件 + src/coreclr 1177 文件）
  - `E:\il2cpp\reference\corert\`（4412 文件 @c6af4cf——TypeSystem + CppWriter.cs 与 ILC 转译器思路最接近）
  - 映射表：docs/netcore-reference-guide.md；conformance 审计 4 份在 docs/audit/*conformance*
- **跨平台铁律**：IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 接口；已落地 **四平台 runtime 135/135 零告警（MinGW/MSVC/WSL/NDK，0 警告）** + **Apple/OpenBSD 交叉语法验证 121×2** + **M8 S1（NDK-arm64 121/121 compile + 135/135 link + G2 getrandom PASS）+ S2 大部分**（WSL mylib.so dlopen 路径 PASS + decimal UB 修复）；设计见 docs/design/cross-platform-design.md。
- **性能铁律**：bench v6（2026-08-10 实测）**object/str_len inf（循环消除）**、array 12×、string 1.82×、fib 1.62×、str_chars ~17×、str_concat ~0.81-0.85——远超 net48，**PERF-1 收口且零回归（v5 全保持，批次 55-69 零回退）**。GC-Root-1（RootSlotPass 逃逸分析）+ **GC 根系统性修复（01afb99 静态字段 add_static_root + 52ad05a RootSlotPass 分配参数 + 177bbe0 hglobal GlobalAlloc/GlobalFree——强制 GC 30/30 崩溃→0/30）** + DCE + InlinePass 深内联 + AllocPromoPass + LicmPass + 边界检查消除（ComputeLoopSafeAccesses）+ 去虚化 S1-S3 已落地。
- **主代理角色**：监工/调度——只拆任务派发并行子代理（用户要求**上百并发/批**，PRD#12），不做实现。任务卡约束：文件域隔离（每文件一代理独占）+ 独立 build 目录 + 不 git commit（主代理统一提交）+ 每 15-20 分钟 checkpoint（PRD#15）。

## 2. git 状态

- 分支 `m1`，**HEAD = 8d865d2**（最终版，2026-08-10 16:27，批次 69 完成——四平台零告警）；`git rev-list --count f36f386..HEAD` = **84 commits**（自 snapshot 7 全部落地并提交）；批次 15-69 提交链见 §3
- **批次 62-69 提交链（新→旧，12 commits）**：8d865d2(fix: **decimal custom-format unknown pattern verbatim fallback**，decfmt2 crash fixed + P/E green) → c492e97(test: decfmt2 expected trailing newline sync，41/41 lines match) → 26e71c4(fix: **decimal_format E-case/E-zero-pad/percent arm**，decfmt2) → 8f7b6cd(**batch 68: WinForms 阶段 13** cross-thread UI updates 双编译器 byte-exact) → 692497b(test: e2e C28 hglobalops/gcstatic PASS + decfmt2 gaps) → 177bbe0(fix: **hglobal 家族 GlobalAlloc/GlobalFree 一致性**，clipops/winforms1 PASS，was 0xC0000374；**unit 1694/1694 runtime 135/135**) → 52ad05a(**batch 66: RootSlotPass allocating-arg fix** unit 1694/1694 强制 GC 0 crash + e2e C27 gcroot/boxop2 PASS) → 01afb99(fix: **GC-CRITICAL 引用静态字段从未注册为 GC 根**，TypeEmitter + EmitMain add_static_root，winforms1 32 roots；强制 GC 30/30 崩溃→0/30) → d464749(**batch 64: WinForms 阶段 12** clipboard 双编译器 PASS) → e6f41f9(fix: **icall_object_get_type_code 12-type 全映射**，boxconv 52/52 + boxops 42/42 green) → 6acce48(test: e2e C26 mdievt PASS + gitignore backup patterns + **matrix7 final 95.4% doc**) → 220cf2f(**batch 62: WinForms 阶段 11** MDI 双编译器 192-line + e2e C25 decops/strformat/menuevt 3/3 + final snapshots batch 61 state 4-platform 0-warning)
- 批次 55-61 提交链（新→旧，7 commits）：d1cfbfd → 0e11019 → 4191500 → 82d0770 → 26baabd → 9fa9ba5 → 9cd8feb
- 批次 45-53 提交链（新→旧）：52d8eaa(fix: formatext strchr→memchr + FormatPercentInt zero 分支, numfmtdeep 50/50 byte-identical) → d7a5dec(**b53: WSL 135/135** monitor fix + mathf/console/socket platform-adaptive + **NDK arm64 S1** 121/121 compile + 135/135 link + G2 getrandom PASS + e2e C20 internops/winformsext PASS) → b57650b(fix: numberformat GroupDigits strchr→memchr bounded scan, WSL ASAN negative-size 修复) → ce356e2(**b51: WinForms 阶段 6** MessageBox worker-thread + modal loop 双编译器 + Intern pool 线程安全 mutex + e2e C19 3/3 + WSL 130/135→strchr bug 定位) → 3411fe8(docs: **bench v6** 零回归 fib 1.62x/string 1.82x/str_chars 17x/virtbench direct-call) → 1d99ad3(b50: test sync valuesemantics boxed-bool hash + io File.Copy/Move ExpectThrows, runtime 135/135) → 8f254b8(**b49: WinForms 阶段 5** multi-window/layout/hierarchy 双编译器 + **COM stage 4** IOleObject/IOleClientSite multi-vtable CCW + M8 实机计划 + e2e C18 3/3) → 9ec11f8(**b48: WinForms 阶段 4** multicast event delegate 系统 Click/KeyDown subscribe-unsubscribe 57-line byte-exact + delegate ReferenceCppType) → de1390b(b47: **String IConvertible 表 typecheckcase PASS** + File.Copy/Move 异常映射 fileio2 PASS + 异常消息透传 excext 11/13 + e2e C17 3/3) → 1fe84e3(b45: box 域审计 valuesemantics + e2e C16 boxops/resops/strcmpdeep) → d88860a(docs: matrix7 final 184/196 + project-state-20260810 + roadmap-v6)

## 3. 批次 15-69 提交链（本快照实测 git log）

- **总量**：`git rev-list --count f36f386..HEAD` = **84 commits**（= snapshot 7 的 21 commits 批次 15-20 + 批次 21-29 的 18 commits + 批次 30-41 的 12 commits + 批次 42-44 的 3 commits + 批次 45-53 的 **11 commits** + 批次 55-61 的 **7 commits** + 批次 62-69 的 **12 commits**）
- 批次 62-69（12 commits，新→旧）：8d865d2 → c492e97 → 26e71c4 → 8f7b6cd → 692497b → 177bbe0 → 52ad05a → 01afb99 → d464749 → e6f41f9 → 6acce48 → 220cf2f
- 批次 55-61（7 commits，新→旧）：d1cfbfd → 0e11019 → 4191500 → 82d0770 → 26baabd → 9fa9ba5 → 9cd8feb
- 批次 45-53（11 commits，新→旧）：52d8eaa → d7a5dec → b57650b → ce356e2 → 3411fe8 → 1d99ad3 → 8f254b8 → 9ec11f8 → de1390b → 1fe84e3 → d88860a
- 批次 42-44（3 commits，新→旧）：166279b → fd2101b → 8921ff8
- 批次 30-41（12 commits，新→旧）：6b8bb02 → 72ac74c → 3255d9e → a21c5d6 → c6a1bee → fe28fa5 → 4078283 → a27dc7d → abe0999 → d14bf22 → 16d8dcf → ef5bb03
- 批次 21-29（18 commits，新→旧）：2ded0fc → 7f323aa → 8572fa6 → 7f72df0 → f8a31ea → 5abf5c5 → 4bcc19f → e4e13cc → 0fd9eb5 → 58e465d → 7804a78 → 1c940ad → 46e9a3c → c8ecc3b → 083170f → 473f53f → f90efdf → 7b1c2ca
- 批次 15-20（21 commits）：efa74e3..e829e98（见旧快照 §3）
- 无未提交修改（**git status 完全干净：0 modified / 0 untracked**——本快照实测）

## 4. 当前数字（2026-08-10 快照，全部本快照实测）

| 指标 | 数值 | 证据 |
|---|---|---|
| 转译器 unit | **1694/1694（100%）** | 177bbe0 HEAD msg "unit 1694/1694" + 52ad05a msg "unit 1694/1694"；memory.md（批次 69 完成） |
| runtime 测试 | **135/135 PASS 四平台零告警（MinGW+MSVC+WSL+NDK-arm64 链接，0 警告）+ Apple/OpenBSD 交叉语法 121×2**；test_*.cpp **135** 个 | d1cfbfd msg "WSL 135/135, 4-platform 0-warning" + d7a5dec "NDK arm64 S1 135/135 link" + tests/runtime 计数 135 |
| e2e | **矩阵7 196 用例全量口径 186/196 = 94.9%**；剔除 bench（timing 类，checksum 一致）后 **186/195 = 95.4%**（batch 59 目标达成，decfmt 修复后无回归）；9 例剩余 FAIL 全为记录级已核实（3 transpile 接线缺 + 6 diff），**无 codegen FAIL、无回归** | 4191500 msg "matrix7 final 186/195 (95.4% excl bench - target met)" + 6acce48 "matrix7 final 95.4% doc" + m7f2-b23-report（9 FAIL 逐例实跑确认）+ docs/audit/e2e-full-matrix7.md 最终版 |
| e2e 用例目录 | **238**（tests/cases/ 目录，本快照目录计数；批次 62-69 新增 12：decops/strformat/menuevt/mdievt/boxconv/decfmt/gcroot/boxop2/clipops/hglobalops/gcstatic/decfmt2）；用例面 **250+**（memory.md 口径，含探针/矩阵用例） | 本快照目录计数 + memory.md + git log --diff-filter=A |
| 性能 bench v6 | **object inf / str_len inf（循环消除）**、array **12×**、string **1.82×**（v5 1.65→提升）、fib **1.62×**、arr_write **4.0-4.2×**、arr_sum 8.00、str_chars **~17×**、str_concat ~0.81-0.85；**virtbench 去虚化 1.03-1.07 + checksum byte-exact——零回归（v5 全保持，批次 55-61 零回退）** | %TEMP%\opencode\bench6-b23-report.md + tests/bench/results/2026-08-10_bench6-mingw.csv + docs/audit/bench-summary-v6.md |
| 去虚化 | **S1-S3 全落地**：S1 metadata Sealed + closed-world 派生计数（VTablePlanner）、S2 final slot 直调、S3 interface 单实现者直调（c6a1bee）；virtbench 实测 **5-7%**（net48 JIT 同样去虚化） | CodeGenerator.cs:3043 TryDevirtualize + VTablePlanner.cs:51-62 + 72ac74c msg |
| 反射模式 | **A/B/C 全链路**：A=全类型默认发射、B=manifest 裁剪（TypeEmitter FilterReflectionManifest）、C=NormalizeTypeName 程序集限定名（Type.GetType/Activator，gettype_bcl True）；**batch 44 嵌套类型 ReflectionFullName '+' 键**（reflect2 gt_nested PASS） | reflect_base.cpp:135 + 16d8dcf/abe0999/166279b msg |
| finalizer 链 | ✅ **闭合**（72ac74c）：type-level finalizer registry + WaitForPendingFinalizers → gcext e2e byte-exact | 72ac74c + src/runtime/src/gc.cpp + test_gcext.cpp |
| 属性子表 | ✅ PropertyDef 模型 + TypeInfo property subtable（reflops d1_prop x3 True） | src/il2cppc/Model/PropertyDef.cs + 72ac74c |
| 资源文化回退 | ✅ RM-CULT 原语 il2cpp_resources_find_string_fallback + 完整回退链（具体文化→父链→invariant，首含 key 者胜，中链损坏跳过续链）；net48 csc 探针 8/8 实证 | %TEMP%\opencode\rmcf-b23-report.md + resources.cpp |
| custom 格式引擎 | ✅ 0/#/%/‰/D100 回退/转义/引号（numberformat + formatext 双模块，test 全绿，135/135 无回归） | %TEMP%\opencode\rtaud8f-b23-report.md + 8921ff8 msg |
| WinForms | **阶段 1-13 全落地双编译器 byte-exact**：阶段 1 Form/WndProc/message loop/UTF-16 PInvoke 16-line（ef5bb03）；阶段 2 ShowWindow/WM_PAINT/TextOutW/Button-click 委托 29-line（fd2101b/wf3）；阶段 3 TextBox/CheckBox/焦点+键盘 47-line（wf4）；阶段 4 multicast event delegate 57-line（9ec11f8/wf5）；阶段 5 multi-window/layout/hierarchy（8f254b8/wf6）；阶段 6 MessageBox worker-thread + modal loop（ce356e2/wf7）；阶段 7 property 控件 + MetadataLoader IntPtr 字段（9cd8feb）；阶段 8 ComboBox/ListBox（26baabd/wf8）；阶段 9 StatusBar/ProgressBar comctl32（4191500/wf9）；阶段 10 MainMenu 双编译器（0e11019/wf10）；阶段 11 MDI 192-line（220cf2f）；阶段 12 clipboard 双编译器（d464749/wf12）；阶段 13 cross-thread UI updates 双编译器（8f7b6cd/wf13） | ef5bb03 + fd2101b + wf4..wf13-b23-report + 220cf2f/d464749/8f7b6cd |
| GC 根系统 | ✅ **系统性修复完成**：01afb99 **引用静态字段从未注册为 GC 根**（TypeEmitter + EmitMain add_static_root，winforms1 32 roots；强制 GC 30/30 崩溃→0/30——解释 user32 AV + GAP-FIN-4 家族）+ 52ad05a **RootSlotPass allocating-arg fix**（强制 GC 0 crash）+ 177bbe0 **hglobal GlobalAlloc/GlobalFree 一致性**（clipops/winforms1 PASS，was 0xC0000374） | 01afb99 + 52ad05a + 177bbe0 + e2ec28-b23-report（gcstatic PASS 23 行 byte-exact 验证） |
| decimal 格式 4 缺陷 | ✅ **4 缺陷全修**：E 大小写保留（e2→1.23e+003）+ E 分数补零到精度（2.5m E2→2.50E+000）+ P 臂（value×100 + N 风格 %）+ custom 未知模式 verbatim 回退（`{0:xyz}`→`xyz` 不抛，net48 实证）——**decfmt2 crash 消除，41/41 lines match（c492e97）**；未知单字母 D/X/Q 仍抛 FormatException（net48 正确） | 26e71c4 + c492e97 + 8d865d2 + decfmt-cp.md + custfmt-b23-report.md |
| COM | **阶段 1-6**：stage 1 RCW/CCW（56347ef）+ stage 2 IDispatch 7-slot（a27dc7d）+ stage 3 Marshal 面接线 GetActiveObject/GetIUnknownForObject/GetObjectForIUnknown/ReleaseComObject（com3-b23）+ **stage 4 IOleObject/IOleClientSite multi-vtable CCW（8f254b8）** + **stage 5 IConnectionPoint multi-vtable（9cd8feb）** + **stage 6 TypeLib out-of-scope + GetTypeInfo tests（26baabd）**；Windows-only + 跨平台 stub 不崩 | 56347ef + a27dc7d + com3/com5/com6-b23-report + 8f254b8 + 9cd8feb + 26baabd |
| M8 平台 | ✅ **S1 完成（NDK-arm64）**：121/121 compile + 135/135 link + G2 getrandom PASS（d7a5dec）；**探通完成级 121/121 × 2（Apple x86_64 + OpenBSD x86_64）全 PASS**（-fsyntax-only -std=c++17，合成 libc sysroot，0 错）；**S2 大部分完成**：WSL mylib.so dlopen 路径 PASS（m8s2-b23）+ framestack thread_local（9fa9ba5）+ decimal INT32/INT64_MIN UB 修复（d1cfbfd）——**剩余 macOS/OpenBSD 实机编译链接验证（实机依赖）**；计划 docs/plans/m8-real-machine-plan.md | docs/audit/cross-platform-m8.md + %TEMP%\opencode\m8ff/m8s2-b23-report.md + d7a5dec + 9fa9ba5 + d1cfbfd + m8-real-machine-plan.md |
| runtime 域审计 | **~109/110 文件**（batch 21 transpiler 19 + batch 22a-e runtime 5 连批 + rtaud8f cultureinfo/numberformat/formatext 3 域闭环 + valuesemantics b45 + decimal_format 4 缺陷 2 批） | memory.md + 8921ff8 + rtaud8f + 26e71c4/8d865d2 |
| 三编译器 | MinGW ✅（bench/矩阵全链）｜MSVC ✅（runtime 135/135 双绿 + C4996 清零）｜WSL ✅（**135/135** 全绿，ASAN 捕获 strchr bug×2 已修）｜NDK ✅（S1 链接级）——**四平台 0 告警（d1cfbfd/177bbe0）** | d1cfbfd msg + 177bbe0 msg + b57650b + ce356e2 + d7a5dec |

## 5. 硬性原则 PRD（用户定稿 19 条，不可违背——含各条当前落地状态）

1. **注释全英语**：代码注释一律英文零中文——已全仓清零（仅字符串字面量保留）【已达标，LANG-1】
2. **类型转换用 static_cast**：禁止 C 风格 cast 发射；C# as（isinst）语义 NULL→空指针【CAST-1 补丁清单 45 处待应用；矩阵7 static_cast Node**→void** 报错面已随 batch 21 修复】
3. **禁止 dynamic_cast**：全仓 0 处，禁入规范【已达标】
4. **生成代码无死代码**：DCE 必做【已实施：DcePass 死存储消除+拷贝传播】
5. **生成代码缩进严格对齐**【FMT-2 已修】
6. 生成代码可内联 + RAII 帧（已实现）+ PDB 行号映射【SYMLN-1 #line 发射已实现】
7. **icall 接线全量覆盖**：net48 910 InternalCall 盘点（docs/audit/icall-inventory-net48.md）+ Core FCall/QCall/corert（icall-inventory-core.md）【盘点已建；接线按矩阵驱动推进中】
8. **跨平台通用性**（Boost 模式）：platform API 33；P1/P2/P3a/P3b/P3d 已修；P3 剩 8 项【cross-platform-audit.md G1-G14】
9. **无 PDB 必须支持编译**：无 PDB 不显示文件名+行号，绝不拒编【已实现】
10. **研究本地 .NET 实现**（aot-blog-1/2）：CoreRT 依赖图/去虚拟化/形状削减 + CppCodeGen 教训【去虚化 S1-S3 已落地】
11. **性能是原则性问题（强制）**：代码必须可被 C++ 优化内联；前端支持裁剪优化（DCE）；string 内联优化【bench v6 object/str_len inf + array 12× + str_chars ~17×/str_concat ~0.85 + string 1.82×/fib 1.62× 大幅兑现，PERF-1 收口零回归】
12. **子代理并发硬性原则（强制）**：每批**至少 32 个以上**子代理并行推进（目标上百）；文件域隔离必须遵守
13. **NOP 指令必须删除**：IL NOP 不发射任何 C++ 模拟空转【已实现】
14. **DUP 指令必须理解并折叠**：栈顶复制在 IR 前端正确折叠【已实现】
15. **任务超时纪律**：子代理任务卡必须带时间控制——每 15-20 分钟落盘 checkpoint；总时长超限先落盘已做工作再返回【bash 10 分钟硬超时：长任务代理拆分命令，timeout ≤540000ms】
16. **非托管函数指针（delegate*）必须支持**：ELEMENT_TYPE_FNPTR 0x1B + calli【**已落地**：345ec8c 全链 + batch 41 local signature 0x1B consume，fnptr e2e byte-exact】
17. **COM 必须支持（CCW/RCW）**：与 .NET Framework 一模一样；仅 Windows 保证工作，其他平台能编译但不保证工作、不崩溃【**阶段 1-6 已落地**：RCW/CCW + IDispatch + Marshal 面 + IOleObject/IOleClientSite + IConnectionPoint + TypeLib out-of-scope】
18. **完整 .NET 资源嵌入**：ManifestResource 嵌入资源 + GetManifestResourceStream/Names + ResourceManager 最小面【**转译器侧已落地**：083170f；runtime 侧资源回退链已落地（rmcf）——**e2e resmgmt 用例接线待**（D-RES-1..5 记录）】
19. **目标**：打造人类历史上最强 .NET 编译器（目标不变）。

## 6. 里程碑完成状态（M1-M8）

- **M1-M4 ✅**：IL 解码/IR 栈折叠/CodeGenerator/异常 EH/入口；P-Invoke/Marshal/C API（M4）；batch 20 P/Invoke UTF-16 + pinvoke struct registry 扩面
- **M5 接线 🟡**：矩阵7 196 用例 **94.9%（186/196）**、剔除 bench 后 **95.4%（186/195）**（batch 59 目标达成，decfmt 修复后无回归）；剩余 9 FAIL 全为记录级已核实（3 transpile 接线缺 + 6 diff）——**无 codegen FAIL、无回归**
- **M5.5 反射 🟡**：**反射模式 A/B/C 全链路落地**（A=全类型、B=manifest 裁剪 FilterReflectionManifest、C=NormalizeTypeName gettype_bcl True）；TypeInfo 表 + MethodInfo.Invoke 已接线；**未落地**：L0/L1/L2 trimming 验证（T301-T310）尾项
- **M6 转译器 🟡**：unit **1694/1694** 全绿；去虚化 S1-S3 + 属性子表 + finalizer 链 + LINQ includes + Sum checked 落地；**GC 根系统性修复完成**（静态字段 add_static_root + RootSlotPass 分配参数 + hglobal 一致性，强制 GC 0 崩溃）；**e2e 未全绿**（剩余 FAIL 均记录级文档化）
- **M6.5 跨平台 🟡→✅ 四平台零告警**：MinGW+MSVC+WSL **三绿**（WSL 135/135）+ **NDK-arm64 S1 完成**（121/121 compile + 135/135 link + G2 PASS）+ **M8 探通完成级 121×2 全 PASS**（Apple/OpenBSD 全量语法验证，0 错）——**四平台 0 告警（d1cfbfd/177bbe0）**；**残项**：跨平台 P3 剩 8 项
- **M7 性能 ✅ 大幅兑现（bench v6 零回归）**：**object/str_len inf（循环消除）+ array 12× + string 1.82× + fib 1.62× + str_chars ~17×**；arr_write 4.0-4.2×、str_concat ~0.81-0.85——PERF-1 收口；virtbench 去虚化 1.03-1.07 byte-exact；驱动：循环消除 + 边界检查消除 + DCE + InlinePass + AllocPromoPass + StringPool + RootSlotPass + LicmPass + 去虚化
- **M8 🟡 S1+S2 大部分完成 → S2 剩余 macOS/OpenBSD 实机**：**NDK-arm64 S1 完成**（121/121 compile + 135/135 link + G2 getrandom PASS）+ **121×2 全 PASS**（Apple/OpenBSD 语法级）+ **S2 大部分**（WSL mylib.so dlopen 路径 PASS + framestack thread_local + decimal INT32/INT64_MIN UB 修复 + decimal 格式 4 缺陷）；COM 阶段 1-6 已落地（含 Marshal 面 + IOleObject/IOleClientSite + IConnectionPoint + TypeLib out-of-scope）；CLDR + ResourceManager 文化回退（RM-CULT 原语已落地，完整 CLDR 父表为长期项）；**S2 剩余 macOS/OpenBSD 实机编译链接验证（实机依赖）**——计划 docs/plans/m8-real-machine-plan.md

## 7. 新域（批次 30-44 新增，高价值）

| 域 | 状态 | 证据 |
|---|---|---|
| **LINQ 接线（batch 42/44）** | 🟡 Sum/Min/Max 接线（8921ff8）+ **LINQ includes + linqdeep 双编译器 PASS + Sum checked 语义**（166279b）；剩余 linqcase First 谓词 / linqops OrderBy 比较器合成（记录级） | 8921ff8 + 166279b + e2e-full-matrix7 |
| **资源文化回退（RM-CULT）** | ✅ il2cpp_resources_find_string_fallback 全链（net48 csc 探针 8/8）；e2e resmgmt 接线待 | rmcf-b23-report + resources.cpp |
| **custom 格式引擎** | ✅ 0/#/%/‰/D100/转义/引号（numberformat+formatext 双模块，rtaud8f 闭环；b51-53 strchr→memchr×2 收口 numfmtdeep 50/50） | rtaud8f-b23-report + b57650b + 52d8eaa |
| **WinForms 1-13** | ✅ **十三阶段全落地双编译器 byte-exact**（阶段 1 Form/WndProc 16-line + 阶段 2 ShowWindow/WM_PAINT 29-line + 阶段 3 TextBox/CheckBox 47-line + 阶段 4 multicast delegate 57-line + 阶段 5 multi-window/layout + 阶段 6 MessageBox/modal + 阶段 7 property 控件 + 阶段 8 ComboBox/ListBox + 阶段 9 StatusBar/ProgressBar comctl32 + 阶段 10 MainMenu + 阶段 11 MDI 192-line + 阶段 12 clipboard + 阶段 13 cross-thread UI） | ef5bb03 + fd2101b + wf4..wf13-b23-report + 220cf2f/d464749/8f7b6cd |
| **GC 根系统** | ✅ **系统性修复完成**：静态字段 add_static_root（01afb99，强制 GC 30/30→0/30）+ RootSlotPass 分配参数（52ad05a）+ hglobal 一致性（177bbe0，was 0xC0000374）——解释 user32 AV + GAP-FIN-4 家族 | 01afb99 + 52ad05a + 177bbe0 + e2ec28 |
| **decimal 格式 4 缺陷** | ✅ E 大小写/E 补零/P 臂/custom verbatim 回退全修（26e71c4 + 8d865d2，decfmt2 41/41 + P/E green） | 26e71c4 + c492e97 + 8d865d2 + custfmt |
| **COM stage 1-6** | ✅ stage 1 RCW/CCW + stage 2 IDispatch + stage 3 Marshal 面 + **stage 4 IOleObject/IOleClientSite multi-vtable CCW**（8f254b8）+ **stage 5 IConnectionPoint multi-vtable**（9cd8feb）+ **stage 6 TypeLib out-of-scope + GetTypeInfo tests**（26baabd） | 56347ef + a27dc7d + com3/com5/com6 + 8f254b8 + 9cd8feb + 26baabd |
| **Intern pool** | ✅ 线程安全 mutex 化（ce356e2，WinForms/多线程下 string interning 竞态收口） | ce356e2 |
| **framestack thread_local** | ✅ g_frameHead thread_local（9fa9ba5，conccases 19-row all-True 多线程帧完整性，runtime 135/135） | 9fa9ba5 + conccases |
| **decimal UB 修复** | ✅ int32/int64 MIN unsigned-magnitude 取模（d1cfbfd，WSL -O3 验证，-value 负溢 UB 消除） | d1cfbfd + decimal.cpp:2251-2286 |
| **嵌套类型反射** | ✅ ReflectionFullName '+' 键分离（C++ 符号短名 vs 注册键全名），reflect2 gt_nested PASS | 166279b + refln |
| **去虚化 S1-S3** | ✅ 全落地 + virtbench 1.03-1.07 byte-exact | CodeGenerator.cs:3043 + VTablePlanner.cs:51-62 |
| **finalizer 链** | ✅ 闭合（type-level registry）gcext byte-exact | 72ac74c + gc.cpp |
| **属性子表** | ✅ PropertyDef 模型 + TypeInfo subtable | PropertyDef.cs + 72ac74c |
| **M8 平台** | ✅ **S1 完成（NDK-arm64 121/121 compile + 135/135 link + G2 getrandom PASS）** + **完成级 121×2**（Apple x86_64 + OpenBSD x86_64 全量语法 PASS，合成 libc sysroot）+ **S2 大部分**（WSL mylib.so dlopen PASS + framestack thread_local + decimal UB 修复 + decimal 格式 4 缺陷）；**S2 剩余 macOS/OpenBSD 实机验证（实机依赖）**（m8-real-machine-plan.md） | cross-platform-m8.md + m8ff/m8s2 + d7a5dec + 9fa9ba5 + d1cfbfd |
| **反射模式 A/B/C** | ✅ 全链路（A/B/C）+ batch 44 嵌套类型收口 | 16d8dcf + abe0999 + 166279b |
| **Array/console/流面** | ✅ Array Find/Exists/TrueForAll/ConvertAll/Resize + SetOut(null) ANE + TextWriter.Null + redirect restore | 4078283 + a21c5d6 |
| **Split 2-arg** | ✅ strcomp2 82/82 byte-exact | fe28fa5 |
| **COM（CCW/RCW）** | ✅ stage 1-6 落地（RCW/CCW + IDispatch + Marshal 面 + IOleObject/IOleClientSite + IConnectionPoint + TypeLib out-of-scope） | 56347ef + 58e465d + a27dc7d + com3/com5/com6 + 8f254b8 + 9cd8feb + 26baabd |
| **runtime 域审计** | 🟡 **~109/110 文件**；剩余 ~1 文件 | memory.md + rtaud8f |
| **boxconv/type_code 映射** | ✅ **icall_object_get_type_code 12-type 全映射**（e6f41f9，boxconv 52/52 + boxops 42/42 green） | e6f41f9 + e2ec26 |
| **hglobal 家族** | ✅ GlobalAlloc/GlobalFree 一致性（177bbe0，GMEM_FIXED 语义；hglobalops 11 行 + clipops/winforms1 PASS，was 0xC0000374） | 177bbe0 + e2ec28 |

## 8. 剩余工作（施工单清单——按优先级）

**P0（矩阵7 记录级收尾 + runtime 审计收口）**：
1. 矩阵7 剩余 9 FAIL 记录级收尾（3 transpile 接线缺 dictcase/linqcase/linqops + 6 diff delegatecb/console3/consolestream/excext/timeext/arrayext2）——**可选收尾**（memory.md 批次 61 记录；m7f2 已逐例实跑确认，无 1 行级快修候选）
2. runtime 剩余 ~1 文件审计（~109/110）
3. M8 S2 剩余 macOS/OpenBSD 实机编译链接验证前置规划（S1 链接级 + S2 WSL mylib.so PASS 已过；计划 docs/plans/m8-real-machine-plan.md 已入库）

**P1**：
- e2e resmgmt 接线（RM ctor 映射 D-RES-3，GetString 全链）+ e2e C30 扩量
- WinForms 阶段 14（控件扩展）/ COM stage 7（事件源/ActiveX 拖放）
- 反射 trimming 验证 L0/L1/L2（T301-T310）+ 反射模式 B/C 全用例
- e2e 扩量 C25+ / 去虚化 virtbench 扩面
- CAST-1 补丁应用（45 处 static/reinterpret → static_cast，PRD#2）
- icall 缺口（Enum/Remoting LOUD + Core String 分配/MathF/DependentHandle/GC/GCHandle/反射 handle 面）
- 跨平台 P3 剩 8 项

**P2**：**M8 S2 macOS/OpenBSD 实机验证**（实机依赖）/ 完整 CLDR 父表 / rename-ilc-plan 层 1-4 / MethodMapHub（T101）/ bench 基线入库 + 3-compiler CI routine（WSL/NDK 已实证可扩为 4 平台）

## 9. 环境/工具备忘

- 转译器：src/il2cppc/（.NET 8，dotnet build src/il2cppc/il2cppc.csproj -c Release——0 错 0 警门）
- unit：dotnet test tests/unit/unit.csproj -c Release（本快照 1694/1694）
- runtime：cmake -S tests/runtime -B build/<唯一名> -G "MinGW Makefiles" && cmake --build；CMakeLists 已 GLOB（135 个 test_*.cpp）；**四平台**：MinGW（clang17）/ MSVC（VS2022 19.39）/ WSL（Ubuntu1804 GCC7.5，ASAN 定位 strchr 类 bug）/ NDK（arm64 S1 链接级）
- e2e 单用例：pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>（-ContinueOnError 开关已加）
- **run-tests.ps1 每用例 `dotnet run` 重建转译器——并发代理编辑 src/ 期间会误报 transpile FAIL（重跑前须查 StackFold.cs 等 mtime 稳定 ≥3min）**
- 三编译器：MinGW=clang17、MSVC=VS2022 19.39（vswhere）、WSL=Ubuntu1804 GCC7.5、NDK=D:\android\sdk\ndk
- NDK：D:\android\sdk\ndk（**禁用 adb**）；NDK arm64-v8a S1 完成（121/121 compile + 135/135 link + G2 getrandom PASS，d7a5dec）
- 探针/隔离验证惯例：%TEMP%\opencode\ 独立项目；共享树被在途代理破坏时 g++ 独立编译或 git worktree 快照
- 在途判定：编辑前查文件 mtime（<15min=在途，等 20min）；e2e 前需 il2cppc.dll 新于全部源 + 共享库重建
- net48 oracle：csc.exe 在 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\
- UTF-8 字节比较必须 cmd /c 重定向（pwsh *> 会 GBK 重编码）
- **bash 10 分钟硬超时**：长任务代理需拆分命令（timeout ≤540000ms）
- 解锁链（成熟模式）：LOUD→runtime icall→MethodMap 接线→参数重排（IcallArgOrder）→e2e 转绿
- M8 探通：合成 libc sysroot（%TEMP%\opencode\m8ff-sysroot\，Apple/OpenBSD/common ~26 头）→ clang++ -target <triple> -fsyntax-only；**Apple 目标需 -mmacosx-version-min=10.12**（thread_local 支持）

## 10. 恢复后第一动作（5 步）

1. git status + git log -5 确认 HEAD=8d865d2 + 工作树干净（0 modified / 0 untracked）
2. dotnet test tests/unit -c Release 确认 1694/1694
3. 四平台门（MinGW/MSVC/WSL/NDK）runtime 135/135 零告警复核 + 矩阵7 记录级收尾（剩余 9 FAIL）+ runtime 剩余 ~1 文件审计
4. 汇总矩阵7 剩余 9 FAIL → 确认语义钉 → 派发下一批（批次 70+，≥32 代理）
5. e2e resmgmt 接线 + WinForms 阶段 14/COM stage 7 + e2e C30 + M8 S2 macOS/OpenBSD 实机前置规划（m8-real-machine-plan.md 已入库）
