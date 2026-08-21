# 剩余缺口记录（2026-08-13 批次 146，HEAD fc1ae82）— 施工单输入

> 本文档记录矩阵7 终版（195/195=100%）之后的**既有已知缺口**。全部为记录级已核实，
> 无 codegen FAIL、无回归。新增施工单按优先级排列。

## P0（M8 里程碑收口）

### 1. M8 S2 macOS/OpenBSD 实机编译链接验证（外部实机依赖）
- **状态**：本机仅有 MinGW/MSVC/WSL/NDK 交叉链路；macOS/OpenBSD 目前为 **-fsyntax-only 语法级 121×2 全 PASS**（合成 libc sysroot，0 错）。
- **缺口**：实机编译 + 链接 + 运行验证未做——**外部实机依赖**，本仓库无法自验。
- **证据**：docs/audit/cross-platform-m8.md + docs/plans/m8-real-machine-plan.md（S1 链接级 + S2 WSL mylib.so dlopen PASS 已过）。
- **前置**：m8-real-machine-plan.md 已入库。

### 2. 多维数组记录级（用户内存安全铁律约束）
- **状态**：3 道独立墙，均为**记录级**（转译 exit 2 LOUD，带完整堆栈），非实现级崩。
- **墙**：`src/il2cppc/Ir/StackFold.cs:2170`（TypeSpec 0x14 ARRAY → GenericInstantiationName throw，newobj 多维 ctor 路径）｜`src/il2cppc/Emit/MethodSignatureCoder.cs:103`（签名 0x14 EncodeType throw）｜`src/il2cppc/Il/MethodBodyReader.cs:193`（局部 0x14 LocalTypeKind throw）。
- **铁律**：用户内存安全铁律（2026-08-11）——仅显式 struct 字段、无 tail blocks（MD2-IMPb 已 revert，a52210a）。
- **证据**：docs/audit/multidim-probe.md（探针 B/C/D/E 完整堆栈 + 1D 对照 PASS）。

### 3. 记录级缺陷清单（矩阵7 之外剩余 FAIL 逐例——2026-08-13 批次 147 全量复核后更新）
| 用例 | 阶段 | 根因 | 状态（2026-08-13 复核） |
|---|---|---|---|
| virtbench | bench | bench timing 数字不同，checksum −166660532 一致 | 不计正确性（bench），复核仍 FAIL（预期） |
| decfmt2 | diff（原） | S3 自定义数字占位符（`00.00` 等 digit-placeholder）verbatim 回退 | **已解决**（批次 88-146 期间修复；复核 + 隔离重跑 PASS） |
| resmgmt | cmake build（原） | ResourceManager/CultureInfo/Type.Assembly 域未接线 | **已解决**（同批次链修复；复核 + 隔离重跑 PASS） |
| resops | cmake build（原） | 同上域未接线 | **已解决**（复核 + 隔离重跑 PASS） |
- **证据**：docs/audit/e2e-full-matrix8.md（全量复核 396/396：395 PASS / 1 virtbench）+ 隔离重跑 [BASH:run-tests.ps1 resmgmt/resops/decfmt2 → 3/3 PASS]。

> **2026-08-13 批次 147 已解决**：**stackqueue2**（GC 压缩收集下 icall 裸指针分裂，se_len/gq_fifo/gq_empty FAIL，
> 20000 enqueue 丢 1 次 ++count）——根因 + 修复见 docs/audit/e2e-full-matrix8.md §3：
> GcRoot 帧登记协议（新共享头 `src/runtime/include/internal/il2cpp_gcroot.h`）+ ensure 分支内联"分配后重读再写" +
> create 修复，覆盖 **stackqueue/arraylist/sortedlist/hashtable/versionuri**（含 listi4 已正确模式的对照确认）。
> 修复后验证：unit 1800/1800 + runtime 139/139 + frame_deep + e2e 全量复核 396/396（正确性 100%）+ msvc 15/15，回归 0。

## P1（持续施工项）
- e2e 扩量（批次 147+，C84 之后；批次 148-150 已加 enumparseic/mathround/interlockedobj/stringfind/refnullcase/binrw2/strreader2 7 个新用例）。
- WinForms 阶段 14 / COM stage 7（事件源/ActiveX 拖放）。
- 反射 trimming 验证 L0/L1/L2（T301-T310）+ 反射模式 B/C 全用例。
- CAST-1 补丁应用（45 处 static/reinterpret → static_cast，PRD#2；清单文件 %TEMP% 已清，需重建）。
- icall 缺口（**已大量闭合**：Enum 面 100%（批次 148）、Math.Round modes/BigMul（149/150）、Interlocked ref-object（149）、StringComparison 搜索（149）、BinaryIO 对象面（150）、StringIO 批量读/char 写（150）、GC.Collect 2 参（150）；Remoting LOUD + Core String 分配/MathF/DependentHandle/反射 handle 面待续）。
- 跨平台 P3 剩 8 项。

## 已闭合记录（批次 148-150，2026-08-14）
- **Enum 面 100% 接线**（Parse ic/TryParse/ToString format/Format/IsDefined/GetValues/ToObject/CompareTo + boxed ToString + H3 unbox）。
- **BinaryReader/Writer 对象面**（managed icalls 修正 + Stream ctor + binrw2 e2e）。
- **StringIO**（Read(char[])批量/Write(char/char[])/Dispose(bool) + Close/Dispose receiverType 修复——base/接口方法的 owner 判定陷阱）。
- **转译器**：Normalize known-non-null 跨调用失效修复（ref/out 写局部后 == null 正确）。
- 性能：bench 零回退确认（**须 clang 构建**——v8 CSV 基线即 clang；g++ 计时不可比）。

## P2（远期）
- M8 S2 macOS/OpenBSD 实机验证（实机依赖）。
- 完整 CLDR 父表。
- rename-ilc-plan 层 1-4（层 5 目录改名须等所有代理收工）。
- MethodMapHub（T101）。
- bench 基线入库 + 3/4 平台 CI routine。
- runtime 域审计 ~109/110 文件（剩余 ~1 文件）。

## 主代理约束（派发时必带）
- 文件域隔离 + 独立 build 目录 + 不 git commit（主代理统一提交）+ 每 15-20 分钟 checkpoint。
- bash 10 分钟硬超时（timeout ≤540000ms）。
- 在途判定：编辑前查文件 mtime（<15min=在途）。
