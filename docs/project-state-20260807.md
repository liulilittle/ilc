# ILC 项目完整进度快照（2026-08-07，HEAD efa74e3 / M6 batch 20 权威状态）

> 本文档是会话删除后的**唯一权威状态来源**。恢复时先读本文档，再读：
> docs/alignment-tracker-v3.md（域接线，含 08-07 追加）、docs/roadmap-status-v5.md（里程碑/e2e 趋势 snapshot 8）、
> docs/plans/e2e-sprint-worklist.md（剩余用例施工单）、docs/audit/e2e-full-matrix6.md（矩阵6）、
> docs/plans/winforms-plan.md（WinForms 可行性计划）、docs/audit/bench3（%TEMP%\opencode\bench3-b23-report.md）。
> 上一快照 = docs/project-state-20260806.md（HEAD f36f386，保留不动）。

## 1. 项目定位（铁律，不可违背）

- **正式名 ILC**（IL→C++ AOT 编译器），**不是 il2cpp**——"il2cpp 是形意词汇，不是工程定义"。命名纠正方案见 docs/architecture/rename-ilc-plan.md（分层重命名，层 5 目录改名须等所有代理收工）。
- 目标：对齐 .NET Framework 4.8 行为（M2-M8 全部里程碑）。
- **绝对原则**：实现任何 .NET 语义前**必须先抄 .NET Core 开源实现**（微软数十年验证，MIT）——本地参考源码：
  - `E:\il2cpp\reference\netcore\`（dotnet/runtime sparse：libraries 1829 文件 + src/coreclr 1177 文件（gc 151/jit 323/vm 703））
  - `E:\il2cpp\reference\corert\`（4412 文件 @c6af4cf——TypeSystem 296 文件 + CppWriter.cs 与 ILC 转译器思路最接近）
  - 映射表：docs/netcore-reference-guide.md（52 行/8 组）；conformance 审计 4 份在 docs/audit/*conformance*
- **跨平台铁律**：IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 接口；已落地 NDK/WINDOWS/LINUX（三编译器 MinGW/MSVC/WSL 零警告门——本快照 POSIX gate 0-warn / MSVC gate 122/122）；设计见 docs/design/cross-platform-design.md。
- **性能铁律**：bench v3（2026-08-07 18:28/18:42 实测）**6/10 项反超 net48**——fib 1.50×/array 6.00×/string 1.06×/arr_sum 8.00×/arr_write 4.20×/str_len 1.10×；唯一大差距 object 0.04×（待逃逸分析）。GC-Root-1（RootSlotPass 逃逸分析）+ DCE + InlinePass 深内联 + AllocPromoPass + 边界检查消除（ComputeLoopSafeAccesses）已落地。
- **主代理角色**：监工/调度——只拆任务派发并行子代理（用户要求**上百并发/批**，PRD#12），不做实现。任务卡约束：文件域隔离（每文件一代理独占）+ 独立 build 目录 + 不 git commit（主代理统一提交）+ 每 15-20 分钟 checkpoint（PRD#15）。

## 2. git 状态

- 分支 `m1`，**HEAD = efa74e3**（M6 batch 20，2026-08-07 22:25）；`git rev-list --count f36f386..HEAD` = **21 commits**（上一快照后全部落地并提交）
- 批次 15-20 提交链（新→旧）：efa74e3(batch20 test) → 744a7c8(batch20 runtime: P/Invoke UTF-16 + pinvoke struct registry) → ed4b688(batch20 feat: IL gap batch 2 + WIR-3) → 41072f4(batch19 test: FnPtr/IlDecoder/test_com/winforms-plan) → 56347ef(batch19 fix: COM stage1 RCW/CCW) → 345ec8c(batch19 feat: calli+FNPTR + IL 217/218 审计) → d704aed(batch18 test) → bf4e32e(batch18 feat: TypeInfo 表/reflecthandle/P3a-b-d) → bcdcee4(chore) → af34594(batch17 test) → 1066ad7(batch17 runtime: http/streamio/gchandle/dependhandle/gc) → 2b35d36(batch17 feat: DcePass/AllocPromoPass/InlinePass 深化 + wiring) → 6be1585(chore) → a7e1496(batch16 test+docs: icall-inventory×2/cross-platform-audit) → 68172df(batch16 runtime: String ctor 等 + P1/P2) → 252ab4b(batch16 feat: icall gap wiring batch 1 + FMT-2 + LANG-1) → 2cf02fa(chore) → 75215ca(batch15 docs: 上一快照文档波 + matrix6 全量 108/155) → ca4e677(batch15 test) → f6ac29f(batch15 runtime) → e829e98(batch15 transpiler: delegate 域/#44 HAS_THIS/multidim/cctor 守卫/Main argv)
- 工作树：**clean**（本快照实测 git status = 0 modified 0 untracked；矩阵7 验证代理 m7a-m7d 与多个 b23 代理均在飞，产物全部落 %TEMP%\opencode\，未污染仓库）

## 3. 当前数字（2026-08-07 快照，全部本快照实测）

| 指标 | 数值 | 证据 |
|---|---|---|
| 转译器 unit | **1369/1369（100%）** | dotnet test exit 0；[Fact]=1200 + [Theory]=32（理论行展开后总计 1369） |
| runtime 测试 | **127 test_*.cpp**；exe 运行 **123/123 PASS**（icallc 快照 19:25 实测）｜MSVC gate **122/122**（HEAD msg）｜POSIX gate **0-warn** | tests/runtime 目录计数 + icallc-b23-report + efa74e3 msg |
| e2e | **矩阵6 = 108/155（69.7%）**；131 交集 **97/131（74.0%，+17 vs matrix5 80/106 口径）**；**矩阵7 进行中**（batch1 32 PASS + batch2 39 PASS，batch3 被污染待重跑） | docs/audit/e2e-full-matrix6.md + m7a/m7b checkpoint |
| e2e 用例 | **169**（tests/cases/ 目录） | 目录计数 |
| 域接线 | **runtime 头 27**（批次 15-20 大幅合并：104→27）；icall 声明 **262**；转译器 icall 引用 **504** 唯一名，声明∩接线 **171** | 本快照正则实测（口径与 v3 追踪器 459/393 不可比——头文件架构已变） |
| MethodMap | **75 个文件**（v3: 66，全部 committed） | src/il2cppc/Emit 计数 |
| 性能 bench v3 | **6/10 反超 net48**：fib 1.50/loop 0.94/string 1.06/array 6.00/object 0.04/sort 0.60/hash 0.86；arr_sum 8.00/arr_write 4.20/str_len 1.10 | tests/bench/results/2026-08-07_1828 + bench-array/2026-08-07_1842 CSV |
| 三编译器 | MinGW ✅（bench/矩阵全链）｜MSVC 122/122（C4996×11 残留非新域）｜WSL POSIX 0-warn | efa74e3 msg |

## 4. 硬性原则 PRD（用户定稿 19 条，不可违背——含各条当前落地状态）

1. **注释全英语**：代码注释一律英文零中文——已全仓清零（仅字符串字面量保留）【已达标，LANG-1】
2. **类型转换用 static_cast**：禁止 C 风格 cast 发射；C# as（isinst）语义 NULL→空指针【CAST-1 补丁清单 %TEMP%\opencode\cast1-report.md 45 处待应用；matrix7 仍有 static_cast Node**→void** 报错面】
3. **禁止 dynamic_cast**：全仓 0 处，禁入规范【已达标】
4. **生成代码无死代码**：DCE 必做【已实施：DcePass 死存储消除+拷贝传播，2b35d36，批次 17】
5. **生成代码缩进严格对齐**【FMT-2 已修，批次 16】
6. 生成代码可内联 + RAII 帧（已实现）+ PDB 行号映射【SYMLN-1 #line 发射已实现，批次 22 Phase 1 并入】
7. **icall 接线全量覆盖**：net48 910 InternalCall 盘点（docs/audit/icall-inventory-net48.md）+ Core FCall/QCall/corert（icall-inventory-core.md，批次 16 入库）【盘点已建；接线按矩阵驱动推进中】
8. **跨平台通用性**（Boost 模式）：platform API 33；P1/P2 已修；P3a（Android tmpdir）/P3b（guid CSPRNG）/P3d（stopwatch/pathproc）批次 18 落地；P3 剩 8 项【cross-platform-audit.md G1-G14】
9. **无 PDB 必须支持编译**：无 PDB 不显示文件名+行号，绝不拒编【已实现：MetadataLoader.cs:256-267 回退契约 null→零 #line】
10. **研究本地 .NET 实现**（aot-blog-1/2）：CoreRT 依赖图/去虚拟化/形状削减 + CppCodeGen 教训（缺 GC/EH 不可产品化——已避开）。后续：去虚拟化（sealed 推断）、frozen segment≈g_strPool、内联+依赖协调
11. **性能是原则性问题（强制）**：代码必须可被 C++ 优化内联；前端支持裁剪优化（DCE）；string 内联优化；尽可能优化性能【bench v3 6/10 反超 net48 达成大推进；剩余 object 0.04 逃逸分析】
12. **子代理并发硬性原则（强制）**：每批**至少 32 个以上**子代理并行推进（目标上百）；文件域隔离必须遵守【本快照实测并发代理面 = 矩阵7 四代理 m7a-d + 30+ b23 域代理】
13. **NOP 指令必须删除**：IL NOP 不发射任何 C++ 模拟空转（前端直接跳过）【已实现】
14. **DUP 指令必须理解并折叠**：栈顶复制在 IR 前端正确折叠（引用共享/值复制语义正确，无冗余拷贝）【已实现】
15. **任务超时纪律**：子代理任务卡必须带时间控制——每 15-20 分钟落盘 checkpoint；总时长超限（约 40-60 分钟）先落盘已做工作再返回，禁止无限期卡死【本批次 30+ 代理全部执行此纪律】
16. **非托管函数指针（delegate*）必须支持**：ELEMENT_TYPE_FNPTR 0x1B + calli【**已落地**：345ec8c 全链（FnPtrSig decode + IL2CPP_CALLCONV + NRE guard），fnptr e2e PASS】
17. **COM 必须支持（CCW/RCW）**：与 .NET Framework 一模一样；仅 Windows 保证工作，其他平台能编译但不保证工作、不崩溃【**stage 1 已落地**：56347ef RCW CoCreateInstance/QueryInterface + CCW IUnknown vtable + GetActiveObject，Windows-only + 跨平台 stub 不崩；test_com 全量】
18. **完整 .NET 资源嵌入**：ManifestResource 嵌入资源 + GetManifestResourceStream/Names + ResourceManager 最小面【**计划中**：RES-1 施工单；winforms-plan.md §1.6 实测 223 嵌入资源依赖面已量化；转译器当前 ManifestResource 0 匹配】
19. **目标**：打造人类历史上最强 .NET 编译器（目标不变）。

## 5. 里程碑完成状态（M1-M8）

- **M1-M4 ✅**：IL 解码/IR 栈折叠/CodeGenerator/异常 EH/入口；P-Invoke/Marshal/C API（M4）
- **M5 接线 🟡**：头文件架构大幅合并（104→27）后口径重置（见 §3）；Matrix6 新增绿：http/streamio/gchandle/dependhandle/gc/stringalloc/xml/mathf/reflecthandle/encoding GetChars 等对象层域（批次 17-18 落地）
- **M5.5 反射 🟡**：TypeInfo 表发射修复（bf4e32e 非 const globals + HasClassDescriptor/IsDelegateType）→ reflectcase/refinvoke 用例转绿；MethodInfo.Invoke 已接线（345ec8c）；**未落地**：L0/L1/L2 trimming 验证（T301-T310）
- **M6 转译器 🟡**：unit 1369/1369 全绿；DcePass/AllocPromoPass/TempSlotPass/RootSlotPass 套件齐（批次 17）；IL 全指令审计 **217/218 decode**（仅 arglist LOUD，345ec8c）；multidim 0x14 解码；#44 HAS_THIS 分类；cctor 守卫；Main argv。**e2e 未全绿**（矩阵7 进行中）
- **M6.5 跨平台 🟡**：platform P3a/P3b/P3d 落地（Android tmpdir/guid CSPRNG/stopwatch 去重+pathproc plat_ 迁移）；POSIX gate 0-warn；MSVC 122/122（C4996×11 非新域）
- **M7 性能 🟡 → 大幅推进**：bench v3 **6/10 反超 net48**（array 6.00×/arr_sum 8.00×/arr_write 4.20×/string 1.06×）；GC-Root-1 RootSlotPass 逃逸分析、边界检查消除、DCE、AllocPromoPass、InlinePass 深内联全落地；**唯一剩余**：object 0.04×（真实分配 vs JIT 逃逸消除，需完整逃逸分析+分配下沉）
- **M8 未开始**（跨平台扩展验证 Apple/OpenBSD + COM 全量 + CLDR）

## 6. 新域（批次 15-20 新增，高价值）

| 域 | 状态 | 证据 |
|---|---|---|
| **COM（CCW/RCW）** | 🟡 stage 1 落地：RCW CoCreateInstance/QueryInterface/GetActiveObject（oleaut32）、CCW IUnknown vtable；Windows-only + 跨平台 stub 不崩；test_com 全平台构建/RCW win-条件 | 56347ef + 41072f4 |
| **非托管函数指针** | ✅ calli + ELEMENT_TYPE_FNPTR 全链（FnPtrSig decode、IL2CPP_CALLCONV、NRE guard）——fnptr e2e PASS | 345ec8c（PRD#16 达成） |
| **IL 全指令审计** | ✅ 45 个 decoder 用例：ldind/stind/cpobj 宽度 fold、ldobj/stobj/initobj/sizeof token、cpblk/initblk、localloc、ckfinite、jmp tailcall、checked ovf 家族+rem_un；arglist/endfilter/typedref LOUD；**217/218 decode** | 345ec8c + ed4b688（IL gap batch 2 +520 行） |
| **WinForms 计划** | ✅ 可行性调研 docs/plans/winforms-plan.md：net48 实测 2442 类型/625 P/Invoke（user32 281）/198 ComImport/223 嵌入资源；3 阶段路线 + 无头验收方案；结论=纯托管 BCL 域可行，阶段 1 前置 = 5 个 P/Invoke marshal 缺口 | 41072f4 + winforms-plan.md |
| **资源嵌入** | ⬜ 计划（PRD#18 RES-1 施工单，未实施）；winforms-plan §1.6 已量化依赖面 | — |
| **P/Invoke marshal 扩展** | ✅ batch 20：UTF-16（string_to_utf16_alloc/from_utf16/hglobal，char16_t 跨平台）+ pinvoke struct registry（point/rect/msg 布局表，x64 MSG.pt@36 ABI 验证，双向 memcpy）+ test_pinvoke_utf16 7 组/test_pinvokestruct 40 断言 | 744a7c8 |

## 7. 剩余工作（施工单清单——按优先级）

**P0（矩阵7 收口）**：
1. 矩阵7 batch3 重跑（m7c 首跑被 8 并发 dotnet build 污染——47 例全部 transpile 误报；m7a 14 例同因 BLOCKED）
2. 矩阵7 已知失败归因（m7d 汇总 13 例）：transpile 缺口 7（genlist/hashtable2/iocase/linqcase/parsecase 等）、build 8（System_*_class 未声明/metadata extern/static_cast Node**→void**）、diff 1（format2 对齐/精度/{0:x} 格式器）
3. 矩阵6 已修复但未入矩阵的用例复核（charstr/streamops/parseops/collections2/format2/linqops 等批次 18 记录）

**P1**：
- **逃逸分析 + 分配下沉**（object 0.04×——M7 唯一剩余大项；RootSlotPass 已有基础，需完整逃逸 + AllocPromoPass 深化）
- CAST-1 补丁应用（45 处 static/reinterpret → static_cast，PRD#2）
- MethodInfo.Invoke 端到端复核 / decimal 剩余（E 值取址/sf_Euler）
- 矩阵7 FAIL 47 归因逐卡：bitopcase Shl/Shr 掩码、mathdeep IsNaN 折叠、strcompare Equals(StringComparison)、Array.FindIndex、ArrayList.Remove、Exception.get_Message、WriteLine(object)、泛型实参展开、闭包、多维发射、numchain/delegateinst 预存 bug、newarr char、ifcase fixture
- icall 缺口批 2（RuntimeHelpers/Enum/Remoting LOUD + Core String 分配 9/MathF 24/DependentHandle/GC 13/GCHandle/反射 handle 面）
- 跨平台 P3 剩 8 项

**P2**：去虚拟化（sealed 推断）/ 资源嵌入 RES-1（PRD#18）/ COM stage 2+（拖放/ActiveX，M8 前置）/ Apple/OpenBSD 验证 / rename-ilc-plan 层 1-4 / 三编译器 C4996 清零 / bench FULL 恢复（arr_copy/str_chars/str_concat 三基准 wiring gap 仍未通）/ MethodMapHub（T101，75 文件下序列化瓶颈）

## 8. 环境/工具备忘

- 转译器：src/il2cppc/（.NET 8，dotnet build src/il2cppc/il2cppc.csproj -c Release——0 错 0 警门）
- unit：dotnet test tests/unit/unit.csproj -c Release（本快照实测 1369/1369）
- runtime：cmake -S tests/runtime -B build/<唯一名> -G "MinGW Makefiles" && cmake --build；CMakeLists 已 GLOB（新文件自动纳入）
- e2e 单用例：pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>（-ContinueOnError 开关已加）
- **run-tests.ps1 每用例 `dotnet run` 重建转译器——并发代理编辑 src/ 期间会误报 transpile FAIL（m7a/m7c 血泪教训：重跑前须查 StackFold.cs 等 mtime 稳定 ≥3min）**
- 三编译器：MinGW=clang17、MSVC=VS2022 19.39（vswhere）、WSL=Ubuntu1804 GCC7.5
- NDK：D:\android\sdk\ndk（**禁用 adb**）
- 探针/隔离验证惯例：%TEMP%\opencode\ 独立项目；共享树被在途代理破坏时 g++ 独立编译或 git worktree 快照（icallc 快照 123/123 先例）
- 在途判定：编辑前查文件 mtime（<15min=在途，等 20min）；e2e 前需 il2cppc.dll 新于全部源 + 共享库重建
- net48 oracle：csc.exe 在 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\
- UTF-8 字节比较必须 cmd /c 重定向（pwsh *> 会 GBK 重编码）

## 9. 恢复后第一动作（5 步）

1. git status + git log -5 确认 HEAD=efa74e3
2. dotnet test tests/unit -c Release 确认 1369/1369
3. 矩阵7 batch3 重跑（m7c 污染批，concurrency 2，先查 src mtime 稳定）
4. 汇总矩阵7 全量 → 归因剩余 FAIL → 派发下一批（批次 21+，≥32 代理）
5. 逃逸分析（object 0.04×）+ CAST-1 应用 + 矩阵7 归因逐卡 + 每批提交
