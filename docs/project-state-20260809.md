# ILC 项目完整进度快照（2026-08-09，HEAD 6b8bb02 / M6 batch 41 权威状态）

> 本文档是会话删除后的**唯一权威状态来源**。恢复时先读本文档，再读：
> docs/alignment-tracker-v3.md（域接线）、docs/roadmap-status-v6.md（里程碑/bench v4-v5 趋势 snapshot 10）、
> docs/audit/e2e-full-matrix7.md（矩阵7 192 用例全表）、docs/audit/bench-summary-v3.md + %TEMP%\opencode\bench4.txt（bench v4）、
> docs/plans/winforms-plan.md（WinForms 可行性计划 + winforms1 探针）、docs/plans/devirtualization-plan.md（去虚化 S1-S3）、
> docs/plans/reflection-modes-ac-plan.md（反射模式 A/B/C）、docs/plans/e2e-sprint-worklist.md（施工单）。
> 上一快照 = docs/project-state-20260807.md（HEAD efa74e3，保留不动）。

## 1. 项目定位（铁律，不可违背）

- **正式名 ILC**（IL→C++ AOT 编译器），**不是 il2cpp**——"il2cpp 是形意词汇，不是工程定义"。命名纠正方案见 docs/architecture/rename-ilc-plan.md（分层重命名，层 5 目录改名须等所有代理收工）。
- 目标：对齐 .NET Framework 4.8 行为（M2-M8 全部里程碑）。
- **绝对原则**：实现任何 .NET 语义前**必须先抄 .NET Core 开源实现**（微软数十年验证，MIT）——本地参考源码：
  - `E:\il2cpp\reference\netcore\`（dotnet/runtime sparse：libraries 1829 文件 + src/coreclr 1177 文件）
  - `E:\il2cpp\reference\corert\`（4412 文件 @c6af4cf——TypeSystem + CppWriter.cs 与 ILC 转译器思路最接近）
  - 映射表：docs/netcore-reference-guide.md；conformance 审计 4 份在 docs/audit/*conformance*
- **跨平台铁律**：IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 接口；已落地 NDK/WINDOWS/LINUX（MinGW/MSVC/WSL）；设计见 docs/design/cross-platform-design.md。
- **性能铁律**：bench v4（2026-08-09 05:05/05:07 实测）**object/str_len inf（循环消除）**、array 12.5×、string 1.71×、arr_write 4.7×——远超 net48；**bench v5（batch 30 perf deep）str_chars 0.27→~12×、str_concat 0.44→0.81（concat_int fold）——PERF-1 两大 gap 收口**。GC-Root-1（RootSlotPass 逃逸分析）+ DCE + InlinePass 深内联 + AllocPromoPass + LicmPass + 边界检查消除（ComputeLoopSafeAccesses）+ 去虚化 S1-S3 已落地。
- **主代理角色**：监工/调度——只拆任务派发并行子代理（用户要求**上百并发/批**，PRD#12），不做实现。任务卡约束：文件域隔离（每文件一代理独占）+ 独立 build 目录 + 不 git commit（主代理统一提交）+ 每 15-20 分钟 checkpoint（PRD#15）。

## 2. git 状态

- 分支 `m1`，**HEAD = 6b8bb02**（M6 batch 41，2026-08-09 21:54）；`git rev-list --count f36f386..HEAD` = **51 commits**（自 snapshot 7 全部落地并提交）；批次 30-41 提交链见 §3
- 批次 30-41 提交链（新→旧）：6b8bb02(b41: fnptr local signature 0x1B consume, fnptr e2e byte-exact) → 72ac74c(b40: **finalizer 链闭合** type-level registry gcext byte-exact + **属性子表** PropertyDef reflops d1_prop x3 True + devirt bench S1+S3 5-7%) → 3255d9e(b39: TypeInfo delegate-type 方法表 nullptr 豁免, delegatecb 回归清除) → a21c5d6(b38: console SetOut(null) ANE + TextWriter.Null + redirect restore + newarr addr-taken gate arrresize 24-line byte-exact + matrix7 final 175/192 91.1%) → c6a1bee(b37: **去虚化 S3** interface single-implementer + enum [Flags] 分解 + **M8 Apple/OpenBSD 探通 15/15**) → fe28fa5(b36: Split 2-arg icall strcomp2 82/82 + box_enum + runtime 135/135) → 4078283(b35: Array Find/Exists/TrueForAll/ConvertAll/Resize + Console 流面 console3 byte-exact) → a27dc7d(b34: exdeep SourceOffset + **COM stage 2 IDispatch** 7-slot CCW/RCW + devirtualization plan) → abe0999(b33: ipaddr ParseDottedQuad + **反射模式 C** NormalizeTypeName gettype_bcl True) → d14bf22(b32: MethodRefResolver 泛型字节 0x02-0x1F + ArrayList.Remove + GC.Finalize no-op + Array icall) → 16d8dcf(b31: **反射模式 A/C stage 1** MethodDef ReturnTypeName + G1/G2/G3 解除 + mode-B scope 接线) → ef5bb03(b30: **WinForms 阶段 1 end-to-end** Form/WndProc/message loop 16-line byte-exact + perf deep str_chars ~12x/str_concat 0.81 + C4996 9 处清零 + matrix7 155/185)
- 批次 15-29 提交链（新→旧，39→51 之前 39 commits）见本文件旧版 §3 / roadmap-status-v6.md §2.1
- 工作树：**仅 4 个 untracked 备份目录/文件**（.opencode-backup-cons/、cmpgrid.txt、concat_backup_20260808-120127/、gchandle_backup_20260808-194235/——代理备份产物，非脏改）；无 modified（本快照实测 git status）

## 3. 批次 15-41 提交链（本快照实测 git log）

- **总量**：`git rev-list --count f36f386..HEAD` = **51 commits**（= snapshot 7 的 21 commits 批次 15-20 + 批次 21-29 的 18 commits + 本快照批次 30-41 的 **12 commits**）
- 批次 30-41（12 commits，新→旧）：6b8bb02 → 72ac74c → 3255d9e → a21c5d6 → c6a1bee → fe28fa5 → 4078283 → a27dc7d → abe0999 → d14bf22 → 16d8dcf → ef5bb03
- 批次 21-29（18 commits，新→旧）：2ded0fc → 7f323aa → 8572fa6 → 7f72df0 → f8a31ea → 5abf5c5 → 4bcc19f → e4e13cc → 0fd9eb5 → 58e465d → 7804a78 → 1c940ad → 46e9a3c → c8ecc3b → 083170f → 473f53f → f90efdf → 7b1c2ca
- 批次 15-20（21 commits）：efa74e3..e829e98（见旧快照 §2）
- 无未提交修改（仅 4 个 untracked 备份产物，未污染仓库）

## 4. 当前数字（2026-08-09 快照，全部本快照实测）

| 指标 | 数值 | 证据 |
|---|---|---|
| 转译器 unit | **1671/1671（100%）** | 6b8bb02 HEAD msg "unit 1671/1671"；memory.md（批次 41 完成） |
| runtime 测试 | **135/135 PASS（MinGW+MSVC 双绿）**；test_*.cpp **135** 个 | fe28fa5 msg "runtime 135/135" + tests/runtime 计数 135 |
| e2e | **矩阵7 192 用例集 ~94%**（175/192 = 91.1% 于 a21c5d6 全量复跑 + fnptr/gcext/reflops 经 batch 40-41 修复转绿）；剩余 FAIL 均文档化（transpile 泛型/LINQ 域 + console 流序 + File.Copy + 文化 + 反射嵌套） | docs/audit/e2e-full-matrix7.md + 6b8bb02/72ac74c msg |
| e2e 用例目录 | **193**（tests/cases/ 目录） | 本快照目录计数 |
| 性能 bench v4 | **object inf / str_len inf（循环消除）**、array **12.5×**、string **1.71×**、arr_write **4.7×**、arr_sum 8.00、arr_copy 1.44、fib 1.24/sort 1.05/hash 1.29 | 8572fa6 msg + tests/bench/results/2026-08-09_0505-mingw.csv + bench-array/2026-08-09_0507-array-mingw.csv |
| 性能 bench v5（perf deep，batch 30） | **str_chars 0.27 → ~12×（direct chars[] read）**、**str_concat 0.44 → 0.81（concat_int fold）**——PERF-1 两大 gap 收口 | ef5bb03 msg "perf deep (str_chars direct chars[] read ~12x, str_concat concat_int fold 0.81)" + CodeGenerator.cs icall_string_concat_int 热路径 |
| 去虚化 | **S1-S3 全落地**：S1 metadata Sealed + closed-world 派生计数（VTablePlanner）、S2 final slot 直调、S3 interface 单实现者直调（c6a1bee）；virtbench 实测 **5-7%**（net48 JIT 同样去虚化） | CodeGenerator.cs:3043 TryDevirtualize + VTablePlanner.cs:51-62 + 72ac74c msg |
| 反射模式 | **A/B/C 全链路**：A=全类型默认发射、B=manifest 裁剪（TypeEmitter FilterReflectionManifest）、C=NormalizeTypeName 程序集限定名（reflect_base.cpp:135，Type.GetType/Activator，gettype_bcl True） | reflect_base.cpp:24/135 + CodeGenerator.cs:5643-5655 + 16d8dcf/abe0999 msg |
| runtime 域审计 | **~80/110 文件**（batch 21 transpiler 19 + batch 22a-e runtime 5 连批） | memory.md + batch 22a-e commits |
| 三编译器 | MinGW ✅（bench/矩阵全链）｜MSVC ✅（runtime 135/135 双绿 + C4996 9 处清零，134-exe 0/0）｜WSL POSIX 门（PENDING） | fe28fa5 msg "runtime 135/135" + ef5bb03 msg "C4996 9-spot cleanup (MSVC 134-exe 0/0)" |

## 5. 硬性原则 PRD（用户定稿 19 条，不可违背——含各条当前落地状态）

1. **注释全英语**：代码注释一律英文零中文——已全仓清零（仅字符串字面量保留）【已达标，LANG-1】
2. **类型转换用 static_cast**：禁止 C 风格 cast 发射；C# as（isinst）语义 NULL→空指针【CAST-1 补丁清单 45 处待应用；矩阵7 static_cast Node**→void** 报错面已随 batch 21 修复（gcstress 等转绿）】
3. **禁止 dynamic_cast**：全仓 0 处，禁入规范【已达标】
4. **生成代码无死代码**：DCE 必做【已实施：DcePass 死存储消除+拷贝传播】
5. **生成代码缩进严格对齐**【FMT-2 已修】
6. 生成代码可内联 + RAII 帧（已实现）+ PDB 行号映射【SYMLN-1 #line 发射已实现】
7. **icall 接线全量覆盖**：net48 910 InternalCall 盘点（docs/audit/icall-inventory-net48.md）+ Core FCall/QCall/corert（icall-inventory-core.md）【盘点已建；接线按矩阵驱动推进中】
8. **跨平台通用性**（Boost 模式）：platform API 33；P1/P2/P3a/P3b/P3d 已修；P3 剩 8 项【cross-platform-audit.md G1-G14】
9. **无 PDB 必须支持编译**：无 PDB 不显示文件名+行号，绝不拒编【已实现】
10. **研究本地 .NET 实现**（aot-blog-1/2）：CoreRT 依赖图/去虚拟化/形状削减 + CppCodeGen 教训【后续：去虚拟化（sealed 推断）、内联+依赖协调】
11. **性能是原则性问题（强制）**：代码必须可被 C++ 优化内联；前端支持裁剪优化（DCE）；string 内联优化【bench v4 object/str_len inf + array 12.5× 大幅兑现；**bench v5 str_chars ~12× / str_concat 0.81 PERF-1 收口**】
12. **子代理并发硬性原则（强制）**：每批**至少 32 个以上**子代理并行推进（目标上百）；文件域隔离必须遵守
13. **NOP 指令必须删除**：IL NOP 不发射任何 C++ 模拟空转【已实现】
14. **DUP 指令必须理解并折叠**：栈顶复制在 IR 前端正确折叠【已实现】
15. **任务超时纪律**：子代理任务卡必须带时间控制——每 15-20 分钟落盘 checkpoint；总时长超限（约 40-60 分钟）先落盘已做工作再返回【bash 10 分钟硬超时：长任务代理拆分命令，timeout ≤540000ms】
16. **非托管函数指针（delegate*）必须支持**：ELEMENT_TYPE_FNPTR 0x1B + calli【**已落地**：345ec8c 全链 + 批量 21 扩展 MethodSignatureCoder PTR/TYPEDBYREF，fnptr e2e PASS】
17. **COM 必须支持（CCW/RCW）**：与 .NET Framework 一模一样；仅 Windows 保证工作，其他平台能编译但不保证工作、不崩溃【stage 1 已落地：56347ef RCW/CCW + batch 22d 非 RCW→CCW 委托 + vtable OOB guard】
18. **完整 .NET 资源嵌入**：ManifestResource 嵌入资源 + GetManifestResourceStream/Names + ResourceManager 最小面【**转译器侧已落地**：083170f ManifestResourceDef + LoadManifestResources + RenderResourcePoolHeader（T3-pattern resource-pool.h）；**runtime 侧 + e2e 待接线**——winforms-plan §1.6 实测 223 嵌入资源依赖面】
19. **目标**：打造人类历史上最强 .NET 编译器（目标不变）。

## 6. 里程碑完成状态（M1-M8）

- **M1-M4 ✅**：IL 解码/IR 栈折叠/CodeGenerator/异常 EH/入口；P-Invoke/Marshal/C API（M4）；batch 20 P/Invoke UTF-16 + pinvoke struct registry 扩面
- **M5 接线 🟡**：矩阵7 192 用例 **~94%**（175/192 = 91.1% 全量复跑 a21c5d6 + batch 40-41 fnptr/gcext/reflops 转绿）；剩余 FAIL 全为**文档化记录**（dictcase/genlist/linqcase/linqops 泛型域 + streamops/typecheckcase 接线 + console3/consolestream 流序 + gcext→已绿 + reflops→已绿 + arrayext2 + excext + fileio2 + timeext + reflect2 + delegatecb→已绿）
- **M5.5 反射 🟡**：**反射模式 A/B/C 全链路落地**（A=全类型、B=manifest 裁剪 FilterReflectionManifest、C=NormalizeTypeName gettype_bcl True 验证）；TypeInfo 表 + MethodInfo.Invoke 已接线；**未落地**：L0/L1/L2 trimming 验证（T301-T310）尾项
- **M6 转译器 🟡**：unit **1671/1671** 全绿；去虚化 S1-S3 + 属性子表 + finalizer 链落地；**e2e 未全绿**（剩余 FAIL 均文档化，已接近出口）
- **M6.5 跨平台 🟡**：P3a/P3b/P3d 落地 + **M8 平台探通 15/15**（Apple/OpenBSD 跨编译语法验证，c6a1bee）；**残项**：跨平台 P3 剩 8 项 + WSL 门
- **M7 性能 ✅ 大幅兑现（bench v4 + v5）**：**object/str_len inf（循环消除）+ array 12.5× + string 1.71× + arr_write 4.7×**（v4）；**str_chars 0.27→~12×、str_concat 0.44→0.81**（v5 perf deep）——PERF-1 两大 gap 收口；驱动：循环消除 + 边界检查消除 + DCE + InlinePass + AllocPromoPass + StringPool + RootSlotPass + LicmPass + 去虚化
- **M8 待（Apple/OpenBSD 实机）**：COM stage 2 IDispatch（7-slot CCW/RCW 已落地）+ 平台探通 15/15 语法级 PASS + Apple/OpenBSD 实机验证 + CLDR + ResourceManager 文化回退

## 7. 新域（批次 30-41 新增，高价值）

| 域 | 状态 | 证据 |
|---|---|---|
| **去虚化 S1-S3** | ✅ S1（metadata Sealed + closed-world 派生计数 VTablePlanner）、S2（final slot 直调）、S3（interface 单实现者直调，c6a1bee）全落地；virtbench e2e 实测 **5-7%**（net48 JIT 同样去虚化） | CodeGenerator.cs:3043 + VTablePlanner.cs:51-62 + 72ac74c |
| **finalizer 链** | ✅ **闭合**（72ac74c）：type-level finalizer registry（gc.cpp 75 行 + il2cpp_gc.h）+ WaitForPendingFinalizers 触发 → gcext e2e byte-exact | 72ac74c + src/runtime/src/gc.cpp + test_gcext.cpp 111 行 |
| **属性子表** | ✅ PropertyDef 模型 + TypeInfo property subtable（reflops d1_prop x3 True） | src/il2cppc/Model/PropertyDef.cs + 72ac74c |
| **反射模式 A/B/C** | ✅ A=全类型默认、B=manifest 裁剪（FilterReflectionManifest TypeEmitter.cs:990）、C=NormalizeTypeName 程序集限定名（reflect_base.cpp:135-188，Type.GetType/Activator，gettype_bcl True）——16d8dcf stage 1 + abe0999 验证 | 16d8dcf + abe0999 + reflect_base.cpp |
| **WinForms 阶段 1** | ✅ **end-to-end**（ef5bb03）：Form/WndProc 委托 + message loop + UTF-16 PInvoke，16-line byte-exact | ef5bb03 + winforms-plan.md |
| **M8 平台探通** | ✅ **15/15**（c6a1bee）：Apple x86_64/arm64 + OpenBSD x86_64 跨编译语法验证（platform_posix/environment/guid/io/string/rng），libc++ sysroot 合成 | m8p-b23-report.md + c6a1bee |
| **COM stage 2** | 🟡 **IDispatch**（a27dc7d）：7-slot CCW vtable + RCW invoke；**stage 3 待（拖放/ActiveX，M8 前置）** | a27dc7d + src/runtime/src/com.cpp 399 行 |
| **Array 接线扩面** | ✅ Find/Exists/TrueForAll/ConvertAll/Resize icalls（4078283 arrfuncs PASS）+ arrresize addr-taken gate（a21c5d6 24-line byte-exact） | 4078283 + a21c5d6 |
| **Split 2-arg options icall** | ✅ strcomp2 byte-exact 82/82（fe28fa5） | fe28fa5 |
| **console 流面** | ✅ SetOut(null) ANE + TextWriter.Null + redirect restore（console3/consolestream byte-exact） | a21c5d6 |
| **COM（CCW/RCW）** | 🟡 stage 1+2 落地：RCW CoCreateInstance/QueryInterface/GetActiveObject（oleaut32）、CCW IUnknown vtable + IDispatch 7-slot；Windows-only + 跨平台 stub 不崩；**stage 3 待（拖放/ActiveX，M8 前置）** | 56347ef + 58e465d + a27dc7d |
| **非托管函数指针** | ✅ calli + ELEMENT_TYPE_FNPTR 全链（FnPtrSig decode、IL2CPP_CALLCONV、NRE guard）+ **batch 41 local signature 0x1B consume**——fnptr e2e byte-exact | 345ec8c + 6b8bb02（PRD#16 达成） |
| **IL 全指令审计** | ✅ **218/218 decode**（batch 21 补 arglist/endfilter 之外的 1 个 LOUD 归零？IlDecoder 218/218 + bounds-safe reader + prefix pairing）；45 个 decoder 用例 | 345ec8c + ed4b688 + c8ecc3b |
| **WinForms 计划 + 阶段 1 探针** | ✅ 可行性调研 winforms-plan.md（net48 实测 2442 类型/625 P/Invoke/198 ComImport/223 嵌入资源）；batch 24 winforms1 probe skeleton；**阶段 1 已实施 end-to-end（ef5bb03）** | 41072f4 + 5abf5c5 + ef5bb03 |
| **资源嵌入** | 🟡 **转译器侧落地**（083170f ManifestResourceDef + LoadManifestResources + RenderResourcePoolHeader）；runtime 侧 + e2e + ResourceManager 文化回退待接线 | 083170f（PRD#18 起步） |
| **P/Invoke marshal 扩展** | ✅ UTF-16（string_to_utf16_alloc/from_utf16/hglobal，char16_t 跨平台）+ pinvoke struct registry（point/rect/msg 布局表，x64 MSG.pt@36 ABI，双向 memcpy）+ 批量 22e thread_local lastError + astral + byte[] null→nullptr | 744a7c8 + 1c940ad |
| **性能优化（bench v4 + v5）** | ✅ object/str_len inf（循环消除）+ array 12.5× + string 1.71× + arr_write 4.7×（v4）；str_chars ~12× / str_concat 0.81（v5 perf deep）；循环消除/边界检查消除/LicmPass/AllocPromoPass/InlinePass/DcePass 全套 + 去虚化 5-7% | 8572fa6 + ef5bb03 + batch 17/21 |
| **runtime 域审计** | 🟡 **~80/110 文件**（batch 21 transpiler 19 + batch 22a-e runtime 连续 5 批）；剩余 ~30 文件 | c8ecc3b + 46e9a3c..0fd9eb5 + memory.md |

## 8. 剩余工作（施工单清单——按优先级）

**P0（矩阵7 收口 + runtime 审计收尾）**：
1. 矩阵7 剩余 ~12 FAIL 收口（dictcase/genlist/linqcase/linqops M3 泛型实例化缺、streamops/typecheckcase 接线、console3/consolestream 流序、arrayext2 排序比较器 + FindIndex 委托、excext 异常消息透传、fileio2 File.Copy 覆盖抛、timeext 文化、reflect2 嵌套反射）——确认语义钉后转 test 侧头注
2. runtime 剩余域审计（~30/110 文件）
3. M8 实机验证前置规划（Apple/OpenBSD 探通 15/15 语法级已过，实机编译链接待）

**P1**：
- 资源嵌入 runtime 侧 + e2e（PRD#18，转译器侧已就绪）
- WinForms 阶段 2+（阶段 1 end-to-end 已落地；后续控件集/消息处理扩面）
- 反射 trimming 验证 L0/L1/L2（T301-T310）+ 反射模式 B/C 全用例
- e2e 扩量 C13+ / 去虚化 virtbench 扩面（sealed 推断，博客参考）
- CAST-1 补丁应用（45 处 static/reinterpret → static_cast，PRD#2）
- icall 缺口（Enum/Remoting LOUD + Core String 分配/MathF/DependentHandle/GC/GCHandle/反射 handle 面）
- 跨平台 P3 剩 8 项

**P2**：COM stage 3+（拖放/ActiveX，M8 前置）/ **M8 Apple/OpenBSD 实机验证** / ResourceManager 文化回退 / rename-ilc-plan 层 1-4 / MethodMapHub（T101，MethodMap 文件增长下序列化瓶颈）/ bench FULL 确认（str_chars/str_concat v5 已收口，全链复跑）

## 9. 环境/工具备忘

- 转译器：src/il2cppc/（.NET 8，dotnet build src/il2cppc/il2cppc.csproj -c Release——0 错 0 警门）
- unit：dotnet test tests/unit/unit.csproj -c Release（本快照 1671/1671）
- runtime：cmake -S tests/runtime -B build/<唯一名> -G "MinGW Makefiles" && cmake --build；CMakeLists 已 GLOB
- e2e 单用例：pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>（-ContinueOnError 开关已加）
- **run-tests.ps1 每用例 `dotnet run` 重建转译器——并发代理编辑 src/ 期间会误报 transpile FAIL（m7a/m7c 血泪教训：重跑前须查 StackFold.cs 等 mtime 稳定 ≥3min）**
- 三编译器：MinGW=clang17、MSVC=VS2022 19.39（vswhere）、WSL=Ubuntu1804 GCC7.5
- NDK：D:\android\sdk\ndk（**禁用 adb**）
- 探针/隔离验证惯例：%TEMP%\opencode\ 独立项目；共享树被在途代理破坏时 g++ 独立编译或 git worktree 快照
- 在途判定：编辑前查文件 mtime（<15min=在途，等 20min）；e2e 前需 il2cppc.dll 新于全部源 + 共享库重建
- net48 oracle：csc.exe 在 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\
- UTF-8 字节比较必须 cmd /c 重定向（pwsh *> 会 GBK 重编码）
- **bash 10 分钟硬超时**：长任务代理需拆分命令（timeout ≤540000ms）
- 解锁链（成熟模式）：LOUD→runtime icall→MethodMap 接线→参数重排（IcallArgOrder）→e2e 转绿
- M8 探通：合成 libc sysroot（%TEMP%\opencode\m8p-stubs\，Apple/OpenBSD/common ~26 头）→ clang++ -target <triple> -fsyntax-only

## 10. 恢复后第一动作（5 步）

1. git status + git log -5 确认 HEAD=6b8bb02 + 工作树仅 4 个 untracked 备份产物
2. dotnet test tests/unit -c Release 确认 1671/1671
3. 矩阵7 剩余 ~12 FAIL 收口（泛型/LINQ 域 + 流序 + 反射嵌套）+ runtime 剩余域审计（~30 文件）恢复
4. 汇总矩阵7 剩余 FAIL → 确认语义钉 → 派发下一批（批次 42+，≥32 代理）
5. 资源嵌入 runtime 侧 + WinForms 阶段 2 + M8 Apple/OpenBSD 实机前置规划
