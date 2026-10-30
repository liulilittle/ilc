# System.Private.CoreLib 内部调用现状盘点（2026-10-31）

> 审计席 B（只读审计）｜worktree `.worktrees/audit-corelib-icall`｜基线 c3a6b2e3
> 本文档由 `tools/audit/parse_icall_tables.py` 自动生成，所有数字均为脚本实测，
> 未手工抄录。复现：
>
> ```
> python tools/audit/parse_icall_tables.py \
>     --repo-root . --reference-root <checkout>/reference \
>     --out docs/audit/icall-corelib-current.md
> ```

**口径说明**：本仓 `reference/` 被 `.gitignore` 忽略（`.gitignore:47`），
因此解析脚本用 `--reference-root` 指向主 checkout 的 `reference/`；
worktree 内的 `src/` 才是被审计的 ILC 接线面。

## 0. 结论摘要

### 0.1 三张权威表实测规模

| 表 | 源文件 | 条目数 | 备注 |
|---|---|---|---|
| FCall | `reference/coreclr/src/coreclr/vm/ecalllist.h` | **225** | 29 个 FCClassElement 组 |
| QCall | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp` | **403** | `s_QCall[]` 的 DllImportEntry |
| RuntimeImport | `reference/corert/src/System.Private.CoreLib/src/System/Runtime/RuntimeImports.cs` | **173** 个属性 / **172** 个唯一导出名 | Rh*/Rhp* 118 条、libm 数学 55 条 |

### 0.2 接线状态（FCall / QCall 分开）

| 表 | 总数 | 已接 wired | 桩接 stub | 半接 half-wired | 缺 missing | 不可达 unreachable | no-decl |
|---|---|---|---|---|---|---|---|
| **FCall** | 225 | 84 | 0 | 1 | 129 | 11 | 0 |
| **QCall** | 403 | 23 | 1 | 2 | 333 | 36 | 8 |
| **RuntimeImport** | 173 | 1 (`RhBulkMoveWithWriteBarrier`) | 0 | 0 | 172 | 0 | 0 |

状态定义（本文档全篇统一口径）：

- **已接 wired**：owner 分发链上存在该方法名对应的 case 分支，且该分支指名了**运行时 icall 符号**；
  证据为该分支的 `file:line` 加符号名。
- **桩接 stub**：分支存在且指名了运行时符号，但该符号是 `*_unsupported` 的
  PNSE 桩（`src/runtime/src/stubs.cpp` 里直接 throw NotSupported）——
  链接得通，语义未实现。本序列入缺口。
- **半接 half-wired**：分支存在但解析到 fold/null/PNSE 抛异常（**不指名运行时符号**），
  或分支被签名形状守卫挡掉、对真实调用返回 null。
- **缺 missing**：没有任何分支命中该方法名 —— 调用点会发射一个未定义 extern。
  若该声明是 non-public 的内部助手（只能经 public 托管包装到达），行内会标注
  `non-public helper` 及其声明位置；**但接线本身仍然算缺**，不能因为“用户直接调不到”
  就当成已接（包装方法绕不开它）。
- **不可达 unreachable**：被预处理条件排除在 CoreCLR x64 桌面构建之外（记录守卫文本）。
- **no-decl**：QCall 表里有、但参考源码子集里找不到对应的 C# 声明，
  故托管侧在本仓参考树内无法调用（不等于运行时不需要）。

### 0.2.1 判定方法与已知局限

判定是**静态源码级**的，不是运行时验证：脚本从 `BclResolveCore` 的 owner 分发出发，
按函数体截取 `case` 标签与紧随的语句，从字符串字面量里取运行时符号名；
因此：

- `wired` 只证明“这条分支存在且指名了符号”，**不证明签名/参数计数正确**；
  同一 arm 守卫下的其它重载（例如 `String..ctor` 的 9 个签名共用一个 case 入口、
  `MethodMap.String.cs:15` 之后按 `ps` 分派）会共享一行证据。
- 互通插值符号（`icall_mathf_{...}`）只能定位到**符号族**，无法给出最终展开名。
- `missing` 是对**该 owner 的 dispatch 链**判定的；owner 未被任何 case 覆盖时，
  证据写作 `no wiring (owner not dispatched)`。
- QCall 的 owner 来自 C# 声明，而托管方法名 != EntryPoint；脚本用 EntryPoint 反查声明，
  再用声明名去查 map，这与真实调用路径一致（ILC 也按托管名解析）。

### 0.3 可达性分级（只统计 wired 之外的行）

| 级别 | FCall 缺/半 | QCall 缺/半 | 含义 |
|---|---|---|---|
| P0 | 29 | 60 | 用户代码直接调用 |
| P1 | 1 | 10 | 常见 BCL 间接可达 |
| P2 | 94 | 198 | 反射/序列化/线程/装配加载 |
| P3 | 6 | 68 | 仅运行时内部 |

**P0 缺口合计 89 条**（FCall 29 + QCall 60）。

### 0.4 关键判断

1. QCall 才是大头：s_QCall[] 403 条 vs FCall 225 条；
   而 ILC 的 QCall 接线只有 **23** 条（且其中多数是**顺带命中**——同一个托管方法名在 net48 版 MethodMap 里已存在，QCall 走同一条 map，例如 `Environment.GetProcessorCount`、`String.Intern`）。
2. FCall 侧 ILC 已接 84/225，主要是 String/Math/MathF/GC/Delegate/
   RuntimeHelpers 的 net48 形状；MathF 25/25 全接（float 数学面无缺口）。
3. RuntimeImport 173 条基本**整体缺失**：ILC 的运行时不是 Redhawk，
   `Rh*`/`Rhp*` 入口在 `src/runtime` 里只有 `RhBulkMoveWithWriteBarrier` 一个真正映射，
   libm 那 55 条数学入口由 `icall_math*` 直接调 libm，不需要 Redhawk 入口。
4. 计数口径警告：QCall 表的 403 条里，netcore 参考 CoreLib 子集的 46 条 QCall 声明中只有 37 条的 EntryPoint 出现在 `s_QCall[]`；
   其余靠 `reference/coreclr/src/coreclr/System.Private.CoreLib`（152 个 .cs，
   CoreCLR 侧 CoreLib）才能对上 395 条，剩下 8 条表项在**两份参考树里都没有声明**（另有 36 条被预处理排除）。

## 1. 三张权威表的实测结果（与任务书给定数字的差异）

| 项目 | 任务书给定 | 本次实测 | 差异说明 |
|---|---|---|---|
| ecalllist.h FCClassElement 组数 | 29 | **29** | 一致 |
| ecalllist.h FCall 条目 | 约 200 | **225** | 任务书是估算；实测 215 个 FCFuncElement + 10 个 FCDynamic/FCDynamicSig |
| s_QCall[] DllImportEntry | 403 | **403** | 一致 |
| RuntimeImports.cs RuntimeImport | 174 | **173 属性 / 172 唯一名** | `RhpLockCmpXchg32/64` 各出现两次（`ref long` 与 `ref IntPtr` 重载共用入口），且 `RuntimeImport("*", ...)` 两条早期写法未被任务书的 174 唯一计数拆开 |
| netcore CoreLib InternalCall | 26 | **26** | 一致 |
| netcore CoreLib QCall | 46 | **46** | 一致 |
| coreclr CoreLib InternalCall | 未给 | **202** | 新增实测 |
| coreclr CoreLib QCall | 未给 | **354** | 新增实测 |

预处理过滤（按 CoreCLR x64 桌面定义集求值，见脚本 `CORECLR_DEFINES`）：

- FCall 表 **11** 条被 `#ifdef` 排除（`TARGET_32BIT` 的 8 条 DivModInt、
  `FEATURE_INTERPRETER` 1 条、`FEATURE_JAVAMARSHAL` 1 条、`TARGET_WASM` 1 条）。
- QCall 表 **36** 条被排除，集中在 `FEATURE_COMWRAPPERS` /
  `TARGET_UNIX` / `TARGET_BROWSER` / `TARGET_WASI` / `FEATURE_JAVAMARSHAL` /
  `FEATURE_OBJCMARSHAL` / `FEATURE_EVENTSOURCE_XPLAT` / `PROFILING_SUPPORTED` / `_DEBUG`。

## 2. CoreLib C# 声明面与权威表的交叉核对

### 2.1 netcore 子集的 26 条 InternalCall

| # | 声明（owner.method） | 预处理守卫 | FCall 表内 | 判定 |
|---|---|---|---|---|
| 1 | `System.Math.DivInt32Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 2 | `System.Math.DivUInt32Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 3 | `System.Math.DivInt64Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 4 | `System.Math.DivUInt64Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 5 | `System.Math.ModInt32Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 6 | `System.Math.ModUInt32Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 7 | `System.Math.ModInt64Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 8 | `System.Math.ModUInt64Internal` | `-` | 是 | 表内存在但被 `TARGET_32BIT` 排除 -> x64 不可达 |
| 9 | `System.Runtime.GCFrameRegistration.RegisterForGCReporting` | `CORECLR` | 是 | 条件编译（CORECLR 等），需 runtime 面单独核对 |
| 10 | `System.Runtime.GCFrameRegistration.UnregisterForGCReporting` | `CORECLR` | 是 | 条件编译（CORECLR 等），需 runtime 面单独核对 |
| 11 | `System.Runtime.Intrinsics.X86.X86Base.CpuId` | `MONO` | 否 | MONO/NATIVEAOT 专属，CoreCLR 构建不编译 |
| 12 | `System.SpanHelpers.memmove` | `MONO` | 否 | MONO/NATIVEAOT 专属，CoreCLR 构建不编译 |
| 13 | `System.SpanHelpers.memset` | `MONO` | 否 | MONO/NATIVEAOT 专属，CoreCLR 构建不编译 |
| 14 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 15 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 16 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 17 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 18 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 19 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 20 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 21 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 22 | `System.String.ctor` | `-` | 是 | 对上（`System.String` 组，x64 可达） |
| 23 | `System.Threading.Thread.WarnAboutBlockingWait` | `FEATURE_WASM_MANAGED_THREADS` | 否 | WASM 专属，桌面构建不编译 |
| 24 | `System.Threading.Thread.CurrentThreadIsFinalizerThread` | `-` | 是 | 对上（`System.Threading.Thread` 组，x64 可达） |
| 25 | `System.Threading.ThreadPool.MainThreadScheduleBackgroundJob` | `MONO` | 否 | MONO/NATIVEAOT 专属，CoreCLR 构建不编译 |
| 26 | `System.Threading.TimerQueue.MainThreadScheduleTimer` | `MONO` | 否 | MONO/NATIVEAOT 专属，CoreCLR 构建不编译 |

要点：

- 9 条 `String` 声明在源码里是**构造函数**（`public extern String(...)`），
  FCall 表用 `COR_CTOR_METHOD_NAME` 表示，脚本已归一化为 `.ctor`，可与
  `gStringFuncs` 的 9 条 FCDynamicSig 一一对上。
- 8 条 `Math.DivModInt` 的 `Div*/Mod*Internal` 在 FCall 表中**存在**，但被
  `#ifdef TARGET_32BIT` 包住 —— 即 **x64 桌面构建不可达**，本仓 ILC 也无需接入。
- `System.Runtime.GCFrameRegistration.RegisterForGCReporting` / `UnregisterForGCReporting`
  用 `#if CORECLR` 声明、在 FCall 表里对应 `gGCFrameRegistration` 组，**能对上**。
- `System.SpanHelpers.memmove/memset` 是 `#if MONO` 的 InternalCall，在非 MONO 分支里
  走 `[LibraryImport(RuntimeHelpers.QCall, EntryPoint = "memmove")]` —— 同一语义两条声明路径。
- `System.Intrinsics.X86.X86Base.CpuId` 只出现在 QCall 路径（`X86Base_CpuId`），
  `#if MONO` 的 InternalCall 形式不进 CoreCLR 构建。

### 2.2 netcore 子集的 46 条 QCall

| # | 声明（owner.method） | EntryPoint | 在 s_QCall[] 中 |
|---|---|---|---|
| 1 | `System.Diagnostics.Tracing.EventPipeInternal.Enable` | `EventPipeInternal_Enable` | 是 |
| 2 | `System.Diagnostics.Tracing.EventPipeInternal.Disable` | `EventPipeInternal_Disable` | 是 |
| 3 | `System.Diagnostics.Tracing.EventPipeInternal.CreateProvider` | `EventPipeInternal_CreateProvider` | 是 |
| 4 | `System.Diagnostics.Tracing.EventPipeInternal.DefineEvent` | `EventPipeInternal_DefineEvent` | 是 |
| 5 | `System.Diagnostics.Tracing.EventPipeInternal.GetProvider` | `EventPipeInternal_GetProvider` | 是 |
| 6 | `System.Diagnostics.Tracing.EventPipeInternal.DeleteProvider` | `EventPipeInternal_DeleteProvider` | 是 |
| 7 | `System.Diagnostics.Tracing.EventPipeInternal.EventActivityIdControl` | `EventPipeInternal_EventActivityIdControl` | 是 |
| 8 | `System.Diagnostics.Tracing.EventPipeInternal.WriteEventData` | `EventPipeInternal_WriteEventData` | 是 |
| 9 | `System.Diagnostics.Tracing.EventPipeInternal.GetSessionInfo` | `EventPipeInternal_GetSessionInfo` | 是 |
| 10 | `System.Diagnostics.Tracing.EventPipeInternal.GetNextEvent` | `EventPipeInternal_GetNextEvent` | 是 |
| 11 | `System.Diagnostics.Tracing.EventPipeInternal.SignalSession` | `EventPipeInternal_SignalSession` | 是 |
| 12 | `System.Diagnostics.Tracing.EventPipeInternal.WaitForSessionSignal` | `EventPipeInternal_WaitForSessionSignal` | 是 |
| 13 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogExceptionThrown` | `NativeRuntimeEventSource_LogExceptionThrown` | **否** |
| 14 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogContentionLockCreated` | `NativeRuntimeEventSource_LogContentionLockCreated` | 是 |
| 15 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogContentionStart` | `NativeRuntimeEventSource_LogContentionStart` | 是 |
| 16 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogContentionStop` | `NativeRuntimeEventSource_LogContentionStop` | 是 |
| 17 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadStart` | `NativeRuntimeEventSource_LogThreadPoolWorkerThreadStart` | 是 |
| 18 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadStop` | `NativeRuntimeEventSource_LogThreadPoolWorkerThreadStop` | 是 |
| 19 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadWait` | `NativeRuntimeEventSource_LogThreadPoolWorkerThreadWait` | 是 |
| 20 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolMinMaxThreads` | `NativeRuntimeEventSource_LogThreadPoolMinMaxThreads` | 是 |
| 21 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadAdjustmentSample` | `NativeRuntimeEventSource_LogThreadPoolWorkerThreadAdjustmentSample` | 是 |
| 22 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadAdjustmentAdjustment` | `NativeRuntimeEventSource_LogThreadPoolWorkerThreadAdjustmentAdjustment` | 是 |
| 23 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadAdjustmentStats` | `NativeRuntimeEventSource_LogThreadPoolWorkerThreadAdjustmentStats` | 是 |
| 24 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolIOEnqueue` | `NativeRuntimeEventSource_LogThreadPoolIOEnqueue` | 是 |
| 25 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolIODequeue` | `NativeRuntimeEventSource_LogThreadPoolIODequeue` | 是 |
| 26 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkingThreadCount` | `NativeRuntimeEventSource_LogThreadPoolWorkingThreadCount` | 是 |
| 27 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolIOPack` | `NativeRuntimeEventSource_LogThreadPoolIOPack` | 是 |
| 28 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogWaitHandleWaitStart` | `NativeRuntimeEventSource_LogWaitHandleWaitStart` | 是 |
| 29 | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogWaitHandleWaitStop` | `NativeRuntimeEventSource_LogWaitHandleWaitStop` | 是 |
| 30 | `System.Diagnostics.Tracing.XplatEventLogger.EventSource_GetClrConfig` | `EventSource_GetClrConfig` | 是 |
| 31 | `System.Diagnostics.Tracing.XplatEventLogger.IsEventSourceLoggingEnabled` | `IsEventSourceLoggingEnabled` | 是 |
| 32 | `System.Diagnostics.Tracing.XplatEventLogger.LogEventSource` | `LogEventSource` | 是 |
| 33 | `System.Runtime.CompilerServices.AsyncHelpers.SystemJS_MarkAsyncMain` | `SystemJS_MarkAsyncMain` | 是 |
| 34 | `System.Runtime.CompilerServices.AsyncHelpers.SystemJS_RejectMainPromise` | `SystemJS_RejectMainPromise` | 是 |
| 35 | `System.Runtime.CompilerServices.AsyncHelpers.SystemJS_ResolveMainPromise` | `SystemJS_ResolveMainPromise` | 是 |
| 36 | `System.Runtime.InteropServices.TypeMapLazyDictionary.ProcessAttributes` | `TypeMapLazyDictionary_ProcessAttributes` | 是 |
| 37 | `System.Runtime.InteropServices.TypeMapLazyDictionary.FindPrecachedExternalTypeMapEntry` | `TypeMapLazyDictionary_FindPrecachedExternalTypeMapEntry` | 是 |
| 38 | `System.Runtime.InteropServices.TypeMapLazyDictionary.FindPrecachedProxyTypeMapEntry` | `TypeMapLazyDictionary_FindPrecachedProxyTypeMapEntry` | 是 |
| 39 | `System.Runtime.Intrinsics.X86.X86Base.CpuId` | `X86Base_CpuId` | 是 |
| 40 | `System.SpanHelpers.memmove` | `memmove` | 是 |
| 41 | `System.SpanHelpers.memset` | `memset` | 是 |
| 42 | `System.Threading.Interlocked._MemoryBarrierProcessWide` | `Interlocked_MemoryBarrierProcessWide` | 是 |
| 43 | `System.Threading.ThreadPool.SystemJS_ScheduleBackgroundJob` | `SystemJS_ScheduleBackgroundJob` | 是 |
| 44 | `System.Threading.TimerQueue.SystemJS_ScheduleTimer` | `SystemJS_ScheduleTimer` | 是 |
| 45 | `System.Threading.WasiFinalizerScheduler.TryClearPendingFinalization` | `WasiFinalizer_TryClearPending` | 是 |
| 46 | `System.Threading.WasiFinalizerScheduler.ExecuteFinalizationCallback` | `WasiFinalizer_RunWorker` | 是 |

**对不上（C# 声明有、s_QCall[] 无）**：

- `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogExceptionThrown`（`reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Exceptions.NativeSinks.Internal.cs:13`，EntryPoint = `NativeRuntimeEventSource_LogExceptionThrown`）

结论：这是 CoreCLR 侧重名的裁剪残留或入口在别的表（如 nativeaot 的
`runtimeeventinternal.cpp`）。不影响 ILC 判定，因为本仓接线面根本不含该域。

## 3. 已接清单

以下每行都是脚本从 `MethodRefResolver.BclResolveCore` 的 owner 分发出发，
在对应 `MethodMap.*.cs` 里找到同名 case 分支、且分支指名运行时符号后判定的。

### 3.1 FCall

| owner.method | 实现符号（Redhawk 侧） | 接线证据 |
|---|---|---|
| `System.Array.GetCorElementTypeOfElementType` | `ArrayNative::GetCorElementTypeOfElementType` | src/il2cppc/Emit/MethodMap.Array.cs:1189 -> `il2cpp::runtime::icall_array_get_cor_element_type_of_element_type` |
| `System.Runtime.Loader.AssemblyLoadContext.IsTracingEnabled` | `AssemblyNative::IsTracingEnabled` | src/il2cppc/Emit/MethodMap.GC.cs:130 -> `il2cpp::runtime::icall_assembly_load_context_is_tracing_enabled` |
| `System.Delegate.GetMulticastInvoke` | `COMDelegate::GetMulticastInvoke` | src/il2cppc/Emit/MethodMap.Delegate.cs:105 -> `il2cpp::runtime::icall_delegate_get_multicast_invoke` |
| `System.Delegate.GetInvokeMethod` | `COMDelegate::GetInvokeMethod` | src/il2cppc/Emit/MethodMap.Delegate.cs:106 -> `il2cpp::runtime::icall_delegate_get_invoke_method` |
| `System.Environment.set_ExitCode` | `EnvironmentNative::SetExitCode` | src/il2cppc/Emit/MethodMap.Environment.cs:118 -> `il2cpp::runtime::icall_environment_set_exit_code` |
| `System.Environment.get_ExitCode` | `EnvironmentNative::GetExitCode` | src/il2cppc/Emit/MethodMap.Environment.cs:117 -> `il2cpp::runtime::icall_environment_get_exit_code` |
| `System.Exception.GetExceptionCount` | `ExceptionNative::GetExceptionCount` | src/il2cppc/Emit/MethodMap.Exception.cs:262 -> `il2cpp::runtime::icall_exception_get_exception_count` |
| `System.GC.GetMemoryInfo` | `GCInterface::GetMemoryInfo` | src/il2cppc/Emit/MethodMap.GC.cs:297 -> `il2cpp::runtime::icall_gc_get_memory_info_5` |
| `System.GC.GetSegmentSize` | `GCInterface::GetSegmentSize` | src/il2cppc/Emit/MethodMap.GC.cs:91 -> `il2cpp::il2cpp_gc_get_segment_size` |
| `System.GC.GetLastGCPercentTimeInGC` | `GCInterface::GetLastGCPercentTimeInGC` | src/il2cppc/Emit/MethodMap.GC.cs:92 -> `il2cpp::il2cpp_gc_get_last_gc_percent_time_in_gc` |
| `System.GC.GetGenerationSize` | `GCInterface::GetGenerationSize` | src/il2cppc/Emit/MethodMap.GC.cs:93 -> `il2cpp::il2cpp_gc_get_generation_size` |
| `System.GC.GetMaxGeneration` | `GCInterface::GetMaxGeneration` | src/il2cppc/Emit/MethodMap.GC.cs:117 -> `il2cpp::il2cpp_gc_get_max_generation` |
| `System.GC.GetAllocatedBytesForCurrentThread` | `GCInterface::GetAllocatedBytesForCurrentThread` | src/il2cppc/Emit/MethodMap.GC.cs:118 -> `il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread` |
| `System.Runtime.GCFrameRegistration.RegisterForGCReporting` | `GCReporting::Register` | src/il2cppc/Emit/MethodMap.GC.cs:87 -> `il2cpp::runtime::icall_gc_register_for_gc_reporting` |
| `System.Runtime.GCFrameRegistration.UnregisterForGCReporting` | `GCReporting::Unregister` | src/il2cppc/Emit/MethodMap.GC.cs:88 -> `il2cpp::runtime::icall_gc_unregister_for_gc_reporting` |
| `System.Runtime.GCSettings.get_IsServerGC` | `GCInterface::IsServerGC` | src/il2cppc/Emit/MethodMap.GC.cs:85 -> `il2cpp::runtime::icall_gc_is_server_gc` |
| `System.Runtime.GCSettings.GetGCLatencyMode` | `GCInterface::GetGcLatencyMode` | src/il2cppc/Emit/MethodMap.GC.cs:79 -> `il2cpp::runtime::icall_gc_get_latency_mode` |
| `System.Runtime.GCSettings.GetLOHCompactionMode` | `GCInterface::GetLOHCompactionMode` | src/il2cppc/Emit/MethodMap.GC.cs:82 -> `il2cpp::runtime::icall_gc_get_loh_compaction_mode` |
| `System.Runtime.GCSettings.SetGCLatencyMode` | `GCInterface::SetGcLatencyMode` | src/il2cppc/Emit/MethodMap.GC.cs:81 -> `il2cpp::runtime::icall_gc_set_latency_mode` |
| `System.Runtime.GCSettings.SetLOHCompactionMode` | `GCInterface::SetLOHCompactionMode` | src/il2cppc/Emit/MethodMap.GC.cs:83 -> `il2cpp::runtime::icall_gc_set_loh_compaction_mode` |
| `System.Runtime.JitInfo.GetCompiledILBytes` | `GetCompiledILBytes` | src/il2cppc/Emit/MethodMap.GC.cs:127 -> `il2cpp::runtime::icall_jit_info_get_compiled_il_bytes` |
| `System.Runtime.JitInfo.GetCompiledMethodCount` | `GetCompiledMethodCount` | src/il2cppc/Emit/MethodMap.GC.cs:128 -> `il2cpp::runtime::icall_jit_info_get_compiled_method_count` |
| `System.Runtime.InteropServices.Marshal.GetExceptionCode` | `ExceptionNative::GetExceptionCode` | src/il2cppc/Emit/MethodMap.Marshal.cs:547 -> `il2cpp::runtime::icall_marshal_get_exception_code` |
| `System.Runtime.InteropServices.Marshal.GetExceptionPointers` | `ExceptionNative::GetExceptionPointers` | src/il2cppc/Emit/MethodMap.Marshal.cs:552 -> `il2cpp::runtime::icall_marshal_get_exception_pointers` |
| `System.Math.Acos` | `COMDouble::Acos` | src/il2cppc/Emit/MethodMap.Math.cs:140 -> `il2cpp::runtime::icall_math_acos_d` |
| `System.Math.Acosh` | `COMDouble::Acosh` | src/il2cppc/Emit/MethodMap.MathExtra.cs:59 -> `il2cpp::runtime::icall_math_acosh_d` |
| `System.Math.Asin` | `COMDouble::Asin` | src/il2cppc/Emit/MethodMap.Math.cs:139 -> `il2cpp::runtime::icall_math_asin_d` |
| `System.Math.Asinh` | `COMDouble::Asinh` | src/il2cppc/Emit/MethodMap.MathExtra.cs:63 -> `il2cpp::runtime::icall_math_asinh_d` |
| `System.Math.Atan` | `COMDouble::Atan` | src/il2cppc/Emit/MethodMap.Math.cs:141 -> `il2cpp::runtime::icall_math_atan_d` |
| `System.Math.Atanh` | `COMDouble::Atanh` | src/il2cppc/Emit/MethodMap.MathExtra.cs:67 -> `il2cpp::runtime::icall_math_atanh_d` |
| `System.Math.Atan2` | `COMDouble::Atan2` | src/il2cppc/Emit/MethodMap.Math.cs:142 -> `il2cpp::runtime::icall_math_atan2_d` |
| `System.Math.Cbrt` | `COMDouble::Cbrt` | src/il2cppc/Emit/MethodMap.MathExtra.cs:71 -> `il2cpp::runtime::icall_math_cbrt_d` |
| `System.Math.Ceiling` | `COMDouble::Ceil` | src/il2cppc/Emit/MethodMap.Math.cs:82 -> `il2cpp::runtime::icall_decimal_ceiling` |
| `System.Math.Cos` | `COMDouble::Cos` | src/il2cppc/Emit/MethodMap.Math.cs:137 -> `il2cpp::runtime::icall_math_cos_d` |
| `System.Math.Cosh` | `COMDouble::Cosh` | src/il2cppc/Emit/MethodMap.Math.cs:158 -> `il2cpp::runtime::icall_math_cosh_d` |
| `System.Math.Exp` | `COMDouble::Exp` | src/il2cppc/Emit/MethodMap.Math.cs:153 -> `il2cpp::runtime::icall_math_exp_d` |
| `System.Math.Floor` | `COMDouble::Floor` | src/il2cppc/Emit/MethodMap.Math.cs:78 -> `il2cpp::runtime::icall_decimal_floor` |
| `System.Math.FusedMultiplyAdd` | `COMDouble::FusedMultiplyAdd` | src/il2cppc/Emit/MethodMap.MathExtra.cs:79 -> `il2cpp::runtime::icall_math_fma_d` |
| `System.Math.Log` | `COMDouble::Log` | src/il2cppc/Emit/MethodMap.Math.cs:154 -> `il2cpp::runtime::icall_math_log_d` |
| `System.Math.Log2` | `COMDouble::Log2` | src/il2cppc/Emit/MethodMap.MathExtra.cs:75 -> `il2cpp::runtime::icall_math_log2_d` |
| `System.Math.Log10` | `COMDouble::Log10` | src/il2cppc/Emit/MethodMap.Math.cs:157 -> `il2cpp::runtime::icall_math_log10_d` |
| `System.Math.Pow` | `COMDouble::Pow` | src/il2cppc/Emit/MethodMap.Math.cs:77 -> `il2cpp::runtime::icall_math_pow_d` |
| `System.Math.Sin` | `COMDouble::Sin` | src/il2cppc/Emit/MethodMap.Math.cs:136 -> `il2cpp::runtime::icall_math_sin_d` |
| `System.Math.SinCos` | `COMDouble::SinCos` | src/il2cppc/Emit/MethodMap.Math.cs:148 -> `il2cpp::runtime::icall_math_sincos` |
| `System.Math.Sinh` | `COMDouble::Sinh` | src/il2cppc/Emit/MethodMap.Math.cs:159 -> `il2cpp::runtime::icall_math_sinh_d` |
| `System.Math.Sqrt` | `COMDouble::Sqrt` | src/il2cppc/Emit/MethodMap.Math.cs:76 -> `il2cpp::runtime::icall_math_sqrt_d` |
| `System.Math.Tan` | `COMDouble::Tan` | src/il2cppc/Emit/MethodMap.Math.cs:138 -> `il2cpp::runtime::icall_math_tan_d` |
| `System.Math.Tanh` | `COMDouble::Tanh` | src/il2cppc/Emit/MethodMap.Math.cs:160 -> `il2cpp::runtime::icall_math_tanh_d` |
| `System.MathF.Acos` | `COMSingle::Acos` | src/il2cppc/Emit/MethodMap.MathF.cs:49 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Acosh` | `COMSingle::Acosh` | src/il2cppc/Emit/MethodMap.MathF.cs:52 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Asin` | `COMSingle::Asin` | src/il2cppc/Emit/MethodMap.MathF.cs:49 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Asinh` | `COMSingle::Asinh` | src/il2cppc/Emit/MethodMap.MathF.cs:52 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Atan` | `COMSingle::Atan` | src/il2cppc/Emit/MethodMap.MathF.cs:49 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Atanh` | `COMSingle::Atanh` | src/il2cppc/Emit/MethodMap.MathF.cs:52 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Atan2` | `COMSingle::Atan2` | src/il2cppc/Emit/MethodMap.MathF.cs:57 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Cbrt` | `COMSingle::Cbrt` | src/il2cppc/Emit/MethodMap.MathF.cs:52 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Ceiling` | `COMSingle::Ceil` | src/il2cppc/Emit/MethodMap.MathF.cs:50 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Cos` | `COMSingle::Cos` | src/il2cppc/Emit/MethodMap.MathF.cs:48 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Cosh` | `COMSingle::Cosh` | src/il2cppc/Emit/MethodMap.MathF.cs:51 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Exp` | `COMSingle::Exp` | src/il2cppc/Emit/MethodMap.MathF.cs:49 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Floor` | `COMSingle::Floor` | src/il2cppc/Emit/MethodMap.MathF.cs:50 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.FusedMultiplyAdd` | `COMSingle::FusedMultiplyAdd` | src/il2cppc/Emit/MethodMap.MathF.cs:74 -> `il2cpp::runtime::icall_mathf_fma_f` |
| `System.MathF.Log` | `COMSingle::Log` | src/il2cppc/Emit/MethodMap.MathF.cs:61 -> `il2cpp::runtime::icall_mathf_log_f` |
| `System.MathF.Log2` | `COMSingle::Log2` | src/il2cppc/Emit/MethodMap.MathF.cs:50 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Log10` | `COMSingle::Log10` | src/il2cppc/Emit/MethodMap.MathF.cs:50 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.ModF` | `COMSingle::ModF` | src/il2cppc/Emit/MethodMap.MathF.cs:114 -> `il2cpp::runtime::icall_mathf_modf_f` |
| `System.MathF.Pow` | `COMSingle::Pow` | src/il2cppc/Emit/MethodMap.MathF.cs:57 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Sin` | `COMSingle::Sin` | src/il2cppc/Emit/MethodMap.MathF.cs:48 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.SinCos` | `COMSingle::SinCos` | src/il2cppc/Emit/MethodMap.MathF.cs:106 -> `il2cpp::runtime::icall_mathf_sincos_f` |
| `System.MathF.Sinh` | `COMSingle::Sinh` | src/il2cppc/Emit/MethodMap.MathF.cs:51 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Sqrt` | `COMSingle::Sqrt` | src/il2cppc/Emit/MethodMap.MathF.cs:48 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Tan` | `COMSingle::Tan` | src/il2cppc/Emit/MethodMap.MathF.cs:48 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.MathF.Tanh` | `COMSingle::Tanh` | src/il2cppc/Emit/MethodMap.MathF.cs:52 -> `il2cpp::runtime::icall_mathf_{...}` |
| `System.String.FastAllocateString` | `ECall::FastAllocateString` | src/il2cppc/Emit/MethodMap.String.cs:733 -> `il2cpp::runtime::icall_string_fast_allocate` |
| `System.String..ctor` | `&gsig_IM_ArrChar_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_ArrChar_Int_Int_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_PtrChar_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_PtrChar_Int_Int_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_Char_Int_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_ReadOnlySpanOfChar_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_PtrSByt_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_PtrSByt_Int_Int_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.String..ctor` | `&gsig_IM_PtrSByt_Int_Int_Encoding_RetVoid` | src/il2cppc/Emit/MethodMap.String.cs:15 -> `il2cpp::runtime::icall_string_ctor_repeat` |
| `System.Threading.Thread.get_OptimalMaxSpinWaitsPerSpinIteration` | `ThreadNative::GetOptimalMaxSpinWaitsPerSpinIteration` | src/il2cppc/Emit/MethodMap.Thread.cs:250 -> `il2cpp::runtime::icall_threadobj_get_optimal_max_spin_waits` |

### 3.2 QCall

| EntryPoint | 托管声明 | 接线证据 |
|---|---|---|
| `Delegate_AdjustTarget` | `System.Delegate.AdjustTarget` | src/il2cppc/Emit/MethodMap.Delegate.cs:109 -> `il2cpp::runtime::icall_delegate_adjust_target` |
| `Environment_Exit` | `System.Environment._Exit` | src/il2cppc/Emit/MethodMap.EnvironmentExtra.cs:78 -> `il2cpp::runtime::icall_environment_exit` |
| `Environment_FailFast` | `System.Environment.FailFast` | src/il2cppc/Emit/MethodMap.Environment.cs:126 -> `il2cpp::runtime::icall_environment_fail_fast` |
| `Environment_GetProcessorCount` | `System.Environment.GetProcessorCount` | src/il2cppc/Emit/MethodMap.Environment.cs:41 -> `il2cpp::runtime::icall_environment_get_processor_count` |
| `StackTrace_GetStackFramesInternal` | `System.Diagnostics.StackTrace.GetStackFramesInternal` | src/il2cppc/Emit/MethodMap.GC.cs:138 -> `il2cpp::runtime::icall_stack_trace_get_stack_frames_internal` |
| `AssemblyNative_GetLoadedAssemblies` | `System.Runtime.Loader.AssemblyLoadContext.GetLoadedAssemblies` | src/il2cppc/Emit/MethodMap.GC.cs:129 -> `il2cpp::runtime::icall_assembly_load_context_get_loaded_assemblies` |
| `String_Intern` | `System.String.Intern` | src/il2cppc/Emit/MethodMap.String.cs:726 -> `il2cpp::il2cpp_string_intern` |
| `String_IsInterned` | `System.String.IsInterned` | src/il2cppc/Emit/MethodMap.String.cs:730 -> `il2cpp::il2cpp_string_is_interned` |
| `ThreadNative_GetApartmentState` | `System.Threading.Thread.GetApartmentState` | src/il2cppc/Emit/MethodMap.Thread.cs:237 -> `il2cpp::runtime::icall_threadobj_get_apartment_state_native` |
| `ThreadNative_Interrupt` | `System.Threading.Thread.Interrupt` | src/il2cppc/Emit/MethodMap.Thread.cs:249 -> `il2cpp::runtime::icall_threadobj_interrupt` |
| `ThreadNative_DisableComObjectEagerCleanup` | `System.Threading.Thread.DisableComObjectEagerCleanup` | src/il2cppc/Emit/MethodMap.Thread.cs:111 -> `il2cpp::runtime::icall_thread_disable_com_object_eager_cleanup` |
| `GCInterface_AllocateNewArray` | `System.GC.AllocateNewArray` | src/il2cppc/Emit/MethodMap.GC.cs:122 -> `il2cpp::runtime::icall_gc_allocate_new_array` |
| `GCInterface_GetTotalMemory` | `System.GC.GetTotalMemory` | src/il2cppc/Emit/MethodMap.GC.cs:52 -> `il2cpp::runtime::icall_gc_get_total_memory` |
| `GCInterface_ReRegisterForFinalize` | `System.GC.ReRegisterForFinalize` | src/il2cppc/Emit/MethodMap.GC.cs:57 -> `il2cpp::runtime::icall_gc_re_register_for_finalize` |
| `MarshalNative_OffsetOf` | `System.Runtime.InteropServices.Marshal.OffsetOf` | src/il2cppc/Emit/MethodMap.Marshal.cs:293 -> `il2cpp::runtime::il2cpp_marshal_offset_of_struct_named` |
| `MarshalNative_GetHRForException` | `System.Runtime.InteropServices.Marshal.GetHRForException` | src/il2cppc/Emit/MethodMap.Marshal.cs:431 -> `il2cpp::runtime::icall_marshal_get_hr_for_exception` |
| `MarshalNative_GetIUnknownForObject` | `System.Runtime.InteropServices.Marshal.GetIUnknownForObject` | src/il2cppc/Emit/MethodMap.Marshal.cs:480 -> `il2cpp::runtime::icall_com_get_iunknown_for_object` |
| `MarshalNative_GetIDispatchForObject` | `System.Runtime.InteropServices.Marshal.GetIDispatchForObject` | src/il2cppc/Emit/MethodMap.Marshal.cs:489 -> `il2cpp::runtime::icall_com_get_idispatch_for_object` |
| `MarshalNative_GetObjectForIUnknown` | `System.Runtime.InteropServices.Marshal.GetObjectForIUnknown` | src/il2cppc/Emit/MethodMap.Marshal.cs:512 -> `il2cpp::runtime::icall_com_get_object_for_iunknown` |
| `MarshalNative_ReleaseComObject` | `System.Runtime.InteropServices.Marshal.ReleaseComObject` | src/il2cppc/Emit/MethodMap.Marshal.cs:521 -> `il2cpp::runtime::icall_com_release_com_object` |
| `MarshalNative_GetNativeVariantForObject` | `System.Runtime.InteropServices.Marshal.GetNativeVariantForObject` | src/il2cppc/Emit/MethodMap.Marshal.cs:447 -> `il2cpp::runtime::icall_marshal_get_native_variant_for_object` |
| `MarshalNative_GetObjectForNativeVariant` | `System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant` | src/il2cppc/Emit/MethodMap.Marshal.cs:442 -> `il2cpp::runtime::icall_marshal_get_object_for_native_variant` |
| `MarshalNative_GetObjectsForNativeVariants` | `System.Runtime.InteropServices.Marshal.GetObjectsForNativeVariants` | src/il2cppc/Emit/MethodMap.Marshal.cs:452 -> `il2cpp::runtime::icall_marshal_get_objects_for_native_variants` |

## 4. 缺口清单

排序：先按可达性 P0→P3，再按 owner。**FCall 与 QCall 分段统计**。

### 4.1 FCall 缺口（missing / half-wired / stub）

#### P0（29 条）

| owner.method | 状态 | Redhawk 实现 | 证据 |
|---|---|---|---|
| `System.Buffer.BulkMoveWithWriteBarrierInternal` | missing | `Buffer::BulkMoveWithWriteBarrier` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Buffer.CoreCLR.cs:14 |
| `System.Exception.IsImmutableAgileException` | missing | `ExceptionNative::IsImmutableAgileException` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:56 |
| `System.Exception.PrepareForForeignExceptionRaise` | missing | `ExceptionNative::PrepareForForeignExceptionRaise` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:131 |
| `System.GC.GetGenerationInternal` | missing | `GCInterface::GetGenerationInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:165 |
| `System.GC.GetMemoryLoad` | missing | `GCInterface::GetMemoryLoad` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:598 |
| `System.GC.GetTotalAllocatedBytesApproximate` | missing | `GCInterface::GetTotalAllocatedBytesApproximate` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:433 |
| `System.GC.SuppressFinalizeInternal` | missing | `GCInterface::SuppressFinalize` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:355 |
| `System.GC._CancelFullGCNotification` | missing | `GCInterface::CancelFullGCNotification` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:442 |
| `System.GC._CollectionCount` | missing | `GCInterface::CollectionCount` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:117 |
| `System.GC._GetTotalPauseDuration` | missing | `GCInterface::GetTotalPauseDuration` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:860 |
| `System.GC._RegisterForFullGCNotification` | missing | `GCInterface::RegisterForFullGCNotification` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:439 |
| `System.Math.ModF` | half-wired | `COMDouble::ModF` | src/il2cppc/Emit/MethodMap.MathExtra.cs:152 (arm without runtime symbol) |
| `System.Runtime.CompilerServices.RuntimeHelpers.ContentEquals` | missing | `ObjectNative::ContentEquals` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:290 |
| `System.Runtime.CompilerServices.RuntimeHelpers.GetTailCallArgBuffer` | missing | `TailCallHelp::GetTailCallArgBuffer` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:528 |
| `System.Runtime.CompilerServices.RuntimeHelpers.GetTailCallInfo` | missing | `TailCallHelp::GetTailCallInfo` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:567 |
| `System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack` | missing | `ReflectionInvocation::TryEnsureSufficientExecutionStack` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:329 |
| `System.Runtime.CompilerServices.RuntimeHelpers.TryGetHashCode` | missing | `ObjectNative::TryGetHashCode` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:240 |
| `System.Threading.Interlocked.CompareExchange32` | missing | `COMInterlocked::CompareExchange32` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:129 |
| `System.Threading.Interlocked.CompareExchange32Pointer` | missing | `COMInterlocked::CompareExchange32` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:149 |
| `System.Threading.Interlocked.CompareExchange64` | missing | `COMInterlocked::CompareExchange64` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:171 |
| `System.Threading.Interlocked.CompareExchangeObject` | missing | `COMInterlocked::CompareExchangeObject` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:191 |
| `System.Threading.Interlocked.Exchange32` | missing | `COMInterlocked::Exchange32` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:65 |
| `System.Threading.Interlocked.Exchange64` | missing | `COMInterlocked::Exchange64` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:86 |
| `System.Threading.Interlocked.ExchangeAdd32` | missing | `COMInterlocked::ExchangeAdd32` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:225 |
| `System.Threading.Interlocked.ExchangeAdd64` | missing | `COMInterlocked::ExchangeAdd64` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:241 |
| `System.Threading.Interlocked.ExchangeObject` | missing | `COMInterlocked::ExchangeObject` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Interlocked.CoreCLR.cs:105 |
| `System.Threading.Thread.CatchAtSafePoint` | missing | `ThreadNative::CatchAtSafePoint` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:547 |
| `System.Threading.Thread.CurrentThreadIsFinalizerThread` | missing | `ThreadNative::CurrentThreadIsFinalizerThread` | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.cs:793 |
| `System.Threading.Thread.InternalFinalize` | missing | `ThreadNative::Finalize` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:190 |

#### P1（1 条）

| owner.method | 状态 | Redhawk 实现 | 证据 |
|---|---|---|---|
| `System.Threading.ObjectHeader.GetLockHandleIfExists` | missing | `ObjectHeader_GetLockHandleIfExists` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/ObjectHeader.CoreCLR.cs:89 |

#### P2（94 条）

| owner.method | 状态 | Redhawk 实现 | 证据 |
|---|---|---|---|
| `System.Reflection.MetadataImport.GetClassLayout` | missing | `MetaDataImport::GetClassLayout` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:559 |
| `System.Reflection.MetadataImport.GetCustomAttributeProps` | missing | `MetaDataImport::GetCustomAttributeProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:545 |
| `System.Reflection.MetadataImport.GetDefaultValue` | missing | `MetaDataImport::GetDefaultValue` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:390 |
| `System.Reflection.MetadataImport.GetEventProps` | missing | `MetaDataImport::GetEventProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:444 |
| `System.Reflection.MetadataImport.GetFieldDefProps` | missing | `MetaDataImport::GetFieldDefProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:453 |
| `System.Reflection.MetadataImport.GetFieldMarshal` | missing | `MetaDataImport::GetFieldMarshal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:596 |
| `System.Reflection.MetadataImport.GetFieldOffset` | missing | `MetaDataImport::GetFieldOffset` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:570 |
| `System.Reflection.MetadataImport.GetGenericParamProps` | missing | `MetaDataImport::GetGenericParamProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:489 |
| `System.Reflection.MetadataImport.GetMemberRefProps` | missing | `MetaDataImport::GetMemberRefProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:536 |
| `System.Reflection.MetadataImport.GetMetadataImport` | missing | `MetaDataImport::GetMetadataImport` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:324 |
| `System.Reflection.MetadataImport.GetName` | missing | `MetaDataImport::GetName` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:426 |
| `System.Reflection.MetadataImport.GetNamespace` | missing | `MetaDataImport::GetNamespace` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:435 |
| `System.Reflection.MetadataImport.GetPInvokeMap` | missing | `MetaDataImport::GetPInvokeMap` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:606 |
| `System.Reflection.MetadataImport.GetParamDefProps` | missing | `MetaDataImport::GetParamDefProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:480 |
| `System.Reflection.MetadataImport.GetParentToken` | missing | `MetaDataImport::GetParentToken` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:471 |
| `System.Reflection.MetadataImport.GetPropertyProps` | missing | `MetaDataImport::GetPropertyProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:462 |
| `System.Reflection.MetadataImport.GetScopeProps` | missing | `MetaDataImport::GetScopeProps` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:500 |
| `System.Reflection.MetadataImport.GetSigOfFieldDef` | missing | `MetaDataImport::GetSigOfFieldDef` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:586 |
| `System.Reflection.MetadataImport.GetSigOfMethodDef` | missing | `MetaDataImport::GetSigOfMethodDef` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:516 |
| `System.Reflection.MetadataImport.GetSignatureFromToken` | missing | `MetaDataImport::GetSignatureFromToken` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:526 |
| `System.Reflection.MetadataImport.GetUserString` | missing | `MetaDataImport::GetUserString` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:414 |
| `System.Reflection.MetadataImport.IsValidToken` | missing | `MetaDataImport::IsValidToken` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:626 |
| `System.Reflection.RuntimeAssembly.GetIsCollectible` | missing | `AssemblyNative::GetIsCollectible` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:256 |
| `System.Reflection.RuntimeAssembly.GetIsDynamic` | missing | `AssemblyNative::GetIsDynamic` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:576 |
| `System.Reflection.RuntimeAssembly.GetManifestModule` | missing | `AssemblyHandle::GetManifestModule` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:713 |
| `System.Reflection.RuntimeAssembly.GetTokenInternal` | missing | `AssemblyHandle::GetTokenInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:719 |
| `System.Runtime.CompilerServices.MethodTable.GetLoaderAllocatorHandle` | missing | `MethodTableNative::GetLoaderAllocatorHandle` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1050 |
| `System.Runtime.CompilerServices.MethodTable.GetMethodTableMatchingParentClass` | missing | `MethodTableNative::GetMethodTableMatchingParentClass` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1026 |
| `System.Runtime.CompilerServices.MethodTable.GetNumInstanceFieldBytes` | missing | `MethodTableNative::GetNumInstanceFieldBytes` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1013 |
| `System.Runtime.CompilerServices.MethodTable.GetPrimitiveCorElementType` | missing | `MethodTableNative::GetPrimitiveCorElementType` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1020 |
| `System.Runtime.CompilerServices.MethodTable.InstantiationArg0` | missing | `MethodTableNative::InstantiationArg0` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1029 |
| `System.Runtime.DependentHandle.InternalAlloc` | missing | `DependentHandle::InternalAlloc` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:240 |
| `System.Runtime.DependentHandle.InternalFree` | missing | `DependentHandle::InternalFree` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:272 |
| `System.Runtime.DependentHandle.InternalGetDependent` | missing | `DependentHandle::InternalGetDependent` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:260 |
| `System.Runtime.DependentHandle.InternalGetTarget` | missing | `DependentHandle::InternalGetTarget` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:251 |
| `System.Runtime.DependentHandle.InternalGetTargetAndDependent` | missing | `DependentHandle::InternalGetTargetAndDependent` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:263 |
| `System.Runtime.DependentHandle.InternalSetDependent` | missing | `DependentHandle::InternalSetDependent` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:266 |
| `System.Runtime.DependentHandle.InternalSetTargetToNull` | missing | `DependentHandle::InternalSetTargetToNull` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:269 |
| `System.Runtime.InteropServices.GCHandle.InternalCompareExchange` | missing | `MarshalNative::GCHandleInternalCompareExchange` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:76 |
| `System.Runtime.InteropServices.GCHandle.InternalGet` | missing | `MarshalNative::GCHandleInternalGet` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:67 |
| `System.Runtime.InteropServices.GCHandle.InternalSet` | missing | `MarshalNative::GCHandleInternalSet` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:73 |
| `System.Runtime.InteropServices.GCHandle._InternalAlloc` | missing | `MarshalNative::GCHandleInternalAlloc` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:19 |
| `System.Runtime.InteropServices.GCHandle._InternalFree` | missing | `MarshalNative::GCHandleInternalFree` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:35 |
| `System.Runtime.InteropServices.Marshal.AreComObjectsAvailableForCleanup` | missing | `MarshalNative::AreComObjectsAvailableForCleanup` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:644 |
| `System.Runtime.InteropServices.Marshal.GetLastPInvokeError` | missing | `MarshalNative::GetLastPInvokeError` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:204 |
| `System.Runtime.InteropServices.Marshal.SetLastPInvokeError` | missing | `MarshalNative::SetLastPInvokeError` | no wiring; public member decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:211 |
| `System.Runtime.JitInfo.GetCompilationTimeInTicks` | missing | `GetCompilationTimeInTicks` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/JitInfo.CoreCLR.cs:31 |
| `System.RuntimeFieldHandle.AcquiresContextFromThis` | missing | `RuntimeFieldHandle::AcquiresContextFromThis` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1706 |
| `System.RuntimeFieldHandle.GetApproxDeclaringMethodTable` | missing | `RuntimeFieldHandle::GetApproxDeclaringMethodTable` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1553 |
| `System.RuntimeFieldHandle.GetAttributes` | missing | `RuntimeFieldHandle::GetAttributes` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1550 |
| `System.RuntimeFieldHandle.GetInstanceFieldOffset` | missing | `RuntimeFieldHandle::GetInstanceFieldOffset` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1575 |
| `System.RuntimeFieldHandle.GetLoaderAllocatorInternal` | missing | `RuntimeFieldHandle::GetLoaderAllocatorInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1709 |
| `System.RuntimeFieldHandle.GetStaticFieldAddress` | missing | `RuntimeFieldHandle::GetStaticFieldAddress` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1578 |
| `System.RuntimeFieldHandle.GetStaticFieldForGenericType` | missing | `RuntimeFieldHandle::GetStaticFieldForGenericType` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1696 |
| `System.RuntimeFieldHandle.GetToken` | missing | `RuntimeFieldHandle::GetToken` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1604 |
| `System.RuntimeFieldHandle.GetUtf8NameInternal` | missing | `RuntimeFieldHandle::GetUtf8Name` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1534 |
| `System.RuntimeFieldHandle.IsFastPathSupported` | missing | `RuntimeFieldHandle::IsFastPathSupported` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1572 |
| `System.RuntimeMethodHandle.GetAttributes` | missing | `RuntimeMethodHandle::GetAttributes` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1086 |
| `System.RuntimeMethodHandle.GetGenericParameterCount` | missing | `RuntimeMethodHandle::GetGenericParameterCount` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1332 |
| `System.RuntimeMethodHandle.GetImplAttributes` | missing | `RuntimeMethodHandle::GetImplAttributes` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1096 |
| `System.RuntimeMethodHandle.GetLoaderAllocatorInternal` | missing | `RuntimeMethodHandle::GetLoaderAllocatorInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1376 |
| `System.RuntimeMethodHandle.GetMethodDef` | missing | `RuntimeMethodHandle::GetMethodDef` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1140 |
| `System.RuntimeMethodHandle.GetMethodFromCanonical` | missing | `RuntimeMethodHandle::GetMethodFromCanonical` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1292 |
| `System.RuntimeMethodHandle.GetMethodTable` | missing | `RuntimeMethodHandle::GetMethodTable` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1111 |
| `System.RuntimeMethodHandle.GetResolver` | missing | `RuntimeMethodHandle::GetResolver` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1356 |
| `System.RuntimeMethodHandle.GetSlot` | missing | `RuntimeMethodHandle::GetSlot` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1128 |
| `System.RuntimeMethodHandle.GetStubIfNeededInternal` | missing | `RuntimeMethodHandle::GetStubIfNeededInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1270 |
| `System.RuntimeMethodHandle.GetUtf8NameInternal` | missing | `RuntimeMethodHandle::GetUtf8Name` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1162 |
| `System.RuntimeMethodHandle.HasMethodInstantiation` | missing | `RuntimeMethodHandle::HasMethodInstantiation` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1260 |
| `System.RuntimeMethodHandle.IsAsyncMethod` | missing | `RuntimeMethodHandle::IsAsyncMethod` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1373 |
| `System.RuntimeMethodHandle.IsCollectible` | missing | `RuntimeMethodHandle::IsCollectible` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1076 |
| `System.RuntimeMethodHandle.IsConstructor` | missing | `RuntimeMethodHandle::IsConstructor` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1370 |
| `System.RuntimeMethodHandle.IsDynamicMethod` | missing | `RuntimeMethodHandle::IsDynamicMethod` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1350 |
| `System.RuntimeMethodHandle.IsGenericMethodDefinition` | missing | `RuntimeMethodHandle::IsGenericMethodDefinition` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1305 |
| `System.RuntimeMethodHandle.IsTypicalMethodDefinition` | missing | `RuntimeMethodHandle::IsTypicalMethodDefinition` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1315 |
| `System.RuntimeTypeHandle.CompareCanonicalHandles` | missing | `RuntimeTypeHandle::CompareCanonicalHandles` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:452 |
| `System.RuntimeTypeHandle.ContainsGenericVariables` | missing | `RuntimeTypeHandle::ContainsGenericVariables` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:870 |
| `System.RuntimeTypeHandle.GetArrayRank` | missing | `RuntimeTypeHandle::GetArrayRank` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:455 |
| `System.RuntimeTypeHandle.GetAssemblyIfExists` | missing | `RuntimeTypeHandle::GetAssemblyIfExists` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:398 |
| `System.RuntimeTypeHandle.GetAttributes` | missing | `RuntimeTypeHandle::GetAttributes` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:433 |
| `System.RuntimeTypeHandle.GetElementTypeHandle` | missing | `RuntimeTypeHandle::GetElementTypeHandle` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:436 |
| `System.RuntimeTypeHandle.GetFirstIntroducedMethod` | missing | `RuntimeTypeHandle::GetFirstIntroducedMethod` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:533 |
| `System.RuntimeTypeHandle.GetGenericVariableIndex` | missing | `RuntimeTypeHandle::GetGenericVariableIndex` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:857 |
| `System.RuntimeTypeHandle.GetModuleIfExists` | missing | `RuntimeTypeHandle::GetModuleIfExists` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:417 |
| `System.RuntimeTypeHandle.GetNextIntroducedMethod` | missing | `RuntimeTypeHandle::GetNextIntroducedMethod` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:536 |
| `System.RuntimeTypeHandle.GetNumVirtuals` | missing | `RuntimeTypeHandle::GetNumVirtuals` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:610 |
| `System.RuntimeTypeHandle.GetToken` | missing | `RuntimeTypeHandle::GetToken` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:458 |
| `System.RuntimeTypeHandle.GetUtf8NameInternal` | missing | `RuntimeTypeHandle::GetUtf8Name` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:662 |
| `System.RuntimeTypeHandle.InternalAllocNoChecks_FastPath` | missing | `RuntimeTypeHandle::InternalAllocNoChecks_FastPath` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:321 |
| `System.RuntimeTypeHandle.IsGenericVariable` | missing | `RuntimeTypeHandle::IsGenericVariable` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:854 |
| `System.RuntimeTypeHandle.IsUnmanagedFunctionPointer` | missing | `RuntimeTypeHandle::IsUnmanagedFunctionPointer` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:491 |
| `System.Signature.GetCallingConventionFromFunctionPointerAtOffsetInternal` | missing | `SignatureNative::GetCallingConventionFromFunctionPointerAtOffsetInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2177 |
| `System.Signature.GetParameterOffsetInternal` | missing | `SignatureNative::GetParameterOffsetInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2147 |
| `System.Signature.GetTypeParameterOffsetInternal` | missing | `SignatureNative::GetTypeParameterOffsetInternal` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2159 |

#### P3（6 条）

| owner.method | 状态 | Redhawk 实现 | 证据 |
|---|---|---|---|
| `System.StubHelpers.StubHelpers.CalcVaListSize` | missing | `StubHelpers::CalcVaListSize` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2513 |
| `System.StubHelpers.StubHelpers.ClearLastError` | missing | `StubHelpers::ClearLastError` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2171 |
| `System.StubHelpers.StubHelpers.GetCOMIPFromRCW` | missing | `StubHelpers::GetCOMIPFromRCW` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2342 |
| `System.StubHelpers.StubHelpers.GetComInterfaceFromMethodDesc` | missing | `StubHelpers::GetComInterfaceFromMethodDesc` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2214 |
| `System.StubHelpers.StubHelpers.LogPinnedArgument` | missing | `StubHelpers::LogPinnedArgument` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2516 |
| `System.StubHelpers.StubHelpers.SetLastError` | missing | `StubHelpers::SetLastError` | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2174 |

### 4.2 QCall 缺口（missing / half-wired / stub）

#### P0（60 条）

| EntryPoint | 托管声明 | 状态 | 证据 |
|---|---|---|---|
| `Array_Ctor` | `System.Array.Ctor` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Array.CoreCLR.cs:39 |
| `Array_CreateInstance` | `System.Array.InternalCreate` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Array.CoreCLR.cs:19 |
| `Array_GetElementConstructorEntrypoint` | `System.Array.ArrayInitializeCache.GetElementConstructorEntrypoint` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Array.CoreCLR.cs:353 |
| `Delegate_BindToMethodInfo` | `System.Delegate.BindToMethodInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:572 |
| `Delegate_BindToMethodName` | `System.Delegate.BindToMethodName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:537 |
| `Delegate_Construct` | `System.Delegate.Construct` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:644 |
| `Delegate_CreateMethodInfo` | `System.Delegate.CreateMethodInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:685 |
| `Delegate_GetMethodDesc` | `System.Delegate.GetMethodDesc` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:695 |
| `Delegate_GetMulticastInvokeSlow` | `System.Delegate.GetMulticastInvokeSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:650 |
| `Delegate_InitializeVirtualCallStub` | `System.Delegate.InitializeVirtualCallStub` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs:900 |
| `ExceptionNative_GetFrozenStackTrace` | `System.Exception.GetFrozenStackTrace` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:240 |
| `ExceptionNative_GetMessageFromNativeResources` | `System.Exception.GetMessageFromNativeResources` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:217 |
| `ExceptionNative_GetMethodFromStackTrace` | `System.Exception.GetMethodFromStackTrace` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:59 |
| `GCInterface_GetNextFinalizableObject` | `System.GC.GetNextFinalizeableObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:312 |
| `GCInterface_GetTotalAllocatedBytesPrecise` | `System.GC.GetTotalAllocatedBytesPrecise` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:436 |
| `GCInterface_AddMemoryPressure` | `System.GC._AddMemoryPressure` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:129 |
| `GCInterface_Collect` | `System.GC._Collect` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:111 |
| `GCInterface_EnableNoGCRegionCallback` | `System.GC._EnableNoGCRegionCallback` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:765 |
| `GCInterface_EndNoGCRegion` | `System.GC._EndNoGCRegion` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:94 |
| `GCInterface_EnumerateConfigurationValues` | `System.GC._EnumerateConfigurationValues` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:938 |
| `GCInterface_GetGenerationBudget` | `System.GC._GetGenerationBudget` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:773 |
| `GCInterface_RefreshMemoryLimit` | `System.GC._RefreshMemoryLimit` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:1005 |
| `GCInterface_RegisterFrozenSegment` | `System.GC._RegisterFrozenSegment` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:417 |
| `GCInterface_RemoveMemoryPressure` | `System.GC._RemoveMemoryPressure` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:132 |
| `GCInterface_StartNoGCRegion` | `System.GC._StartNoGCRegion` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:91 |
| `GCInterface_UnregisterFrozenSegment` | `System.GC._UnregisterFrozenSegment` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:420 |
| `GCInterface_WaitForFullGCApproach` | `System.GC._WaitForFullGCApproach` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:445 |
| `GCInterface_WaitForFullGCComplete` | `System.GC._WaitForFullGCComplete` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:448 |
| `GCInterface_WaitForPendingFinalizers` | `System.GC._WaitForPendingFinalizers` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:344 |
| `TailCallHelp_AllocTailCallArgBufferInternal` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocTailCallArgBufferInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:531 |
| `RuntimeTypeHandle_AllocateTypeAssociatedMemory` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocateTypeAssociatedMemory` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:522 |
| `RuntimeTypeHandle_AllocateTypeAssociatedMemoryAligned` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocateTypeAssociatedMemoryAligned` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:525 |
| `ObjectNative_AllocateUninitializedClone` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocateUninitializedClone` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:350 |
| `MethodTable_AreTypesEquivalent` | `System.Runtime.CompilerServices.RuntimeHelpers.AreTypesEquivalent` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:475 |
| `ReflectionInvocation_CompileMethod` | `System.Runtime.CompilerServices.RuntimeHelpers.CompileMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:198 |
| `ObjectNative_GetHashCodeSlow` | `System.Runtime.CompilerServices.RuntimeHelpers.GetHashCodeSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:243 |
| `ReflectionInvocation_PrepareDelegate` | `System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate` | half-wired | src/il2cppc/Emit/MethodMap.RuntimeHelpers.cs:72 (arm without runtime symbol) |
| `ReflectionInvocation_PrepareMethod` | `System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:201 |
| `ReflectionInvocation_RunClassConstructor` | `System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:167 |
| `ReflectionInvocation_RunModuleConstructor` | `System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:187 |
| `ReflectionInvocation_SizeOf` | `System.Runtime.CompilerServices.RuntimeHelpers.SizeOf` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:627 |
| `Interlocked_MemoryBarrierProcessWide` | `System.Threading.Interlocked._MemoryBarrierProcessWide` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Threading/Interlocked.cs:872 |
| `ThreadNative_CheckForPendingInterrupt` | `System.Threading.Thread.CheckForPendingInterrupt` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:621 |
| `ThreadNative_ClearWaitSleepJoinState` | `System.Threading.Thread.ClearWaitSleepJoinState` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:340 |
| `ThreadNative_GetCurrentOSThreadId` | `System.Threading.Thread.GetCurrentOSThreadId` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:293 |
| `ThreadNative_GetCurrentThread` | `System.Threading.Thread.GetCurrentThread` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:175 |
| `ThreadNative_GetIsBackground` | `System.Threading.Thread.GetIsBackground` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:239 |
| `ThreadNative_GetOSHandle` | `System.Threading.Thread.GetOSHandle` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:449 |
| `ThreadNative_GetThreadState` | `System.Threading.Thread.GetThreadState` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:316 |
| `ThreadNative_InformThreadNameChange` | `System.Threading.Thread.InformThreadNameChange` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:199 |
| `ThreadNative_Initialize` | `System.Threading.Thread.Initialize` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:184 |
| `ThreadNative_PollGC` | `System.Threading.Thread.PollGCInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:550 |
| `ThreadNative_ReentrantWaitAny` | `System.Threading.Thread.ReentrantWaitAny` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:612 |
| `ThreadNative_SetApartmentState` | `System.Threading.Thread.SetApartmentState` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:351 |
| `ThreadNative_SetIsBackground` | `System.Threading.Thread.SetIsBackground` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:242 |
| `ThreadNative_SetPriority` | `System.Threading.Thread.SetPriority` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:271 |
| `ThreadNative_SetWaitSleepJoinState` | `System.Threading.Thread.SetWaitSleepJoinState` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:336 |
| `ThreadNative_SpinWait` | `System.Threading.Thread.SpinWaitInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:134 |
| `ThreadNative_Start` | `System.Threading.Thread.StartInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:112 |
| `ThreadNative_YieldThread` | `System.Threading.Thread.YieldInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs:162 |

#### P1（10 条）

| EntryPoint | 托管声明 | 状态 | 证据 |
|---|---|---|---|
| `DebugDebugger_Break` | `System.Diagnostics.Debugger.BreakInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs:15 |
| `DebugDebugger_CustomNotification` | `System.Diagnostics.Debugger.CustomNotification` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs:87 |
| `DebugDebugger_IsLoggingHelper` | `System.Diagnostics.Debugger.IsLoggingInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs:81 |
| `DebugDebugger_IsManagedDebuggerAttached` | `System.Diagnostics.Debugger.IsManagedDebuggerAttached` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs:66 |
| `DebugDebugger_Launch` | `System.Diagnostics.Debugger.LaunchInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs:59 |
| `DebugDebugger_Log` | `System.Diagnostics.Debugger.LogInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/Debugger.cs:74 |
| `StackFrame_GetMethodDescFromNativeIP` | `System.Diagnostics.StackFrame.GetMethodDescFromNativeIP` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Diagnostics/StackFrame.CoreCLR.cs:60 |
| `FileLoadException_GetMessageForHR` | `System.IO.FileLoadException.GetMessageForHR` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/IO/FileLoadException.CoreCLR.cs:39 |
| `Stream_HasOverriddenSlow` | `System.IO.Stream.HasOverriddenSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/IO/Stream.CoreCLR.cs:14 |
| `ObjectHeader_GetOrCreateLockObject` | `System.Threading.ObjectHeader.GetOrCreateLockObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Threading/ObjectHeader.CoreCLR.cs:92 |

#### P2（198 条）

| EntryPoint | 托管声明 | 状态 | 证据 |
|---|---|---|---|
| `ArgIterator_GetNextArg` | `System.ArgIterator.GetNextArg` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ArgIterator.cs:86 |
| `ArgIterator_GetNextArg2` | `System.ArgIterator.GetNextArg` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ArgIterator.cs:121 |
| `ArgIterator_GetNextArgType` | `System.ArgIterator.GetNextArgType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ArgIterator.cs:146 |
| `ArgIterator_Init` | `System.ArgIterator.Init` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ArgIterator.cs:44 |
| `ArgIterator_Init2` | `System.ArgIterator.Init` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ArgIterator.cs:60 |
| `ModuleHandle_GetDynamicMethod` | `System.ModuleHandle.GetDynamicMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1805 |
| `ModuleHandle_GetMDStreamVersion` | `System.ModuleHandle.GetMDStreamVersion` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2032 |
| `ModuleHandle_GetModuleType` | `System.ModuleHandle.GetModuleType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2009 |
| `ModuleHandle_GetPEKind` | `System.ModuleHandle.GetPEKind` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2019 |
| `ModuleHandle_GetToken` | `System.ModuleHandle.GetToken` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1828 |
| `ModuleHandle_ResolveField` | `System.ModuleHandle.ResolveField` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2000 |
| `ModuleHandle_ResolveMethod` | `System.ModuleHandle.ResolveMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1946 |
| `ModuleHandle_ResolveType` | `System.ModuleHandle.ResolveType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1894 |
| `AssemblyNative_GetAssemblyCount` | `System.Reflection.Assembly.GetAssemblyCount` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Assembly.CoreCLR.cs:100 |
| `AssemblyNative_GetEntryAssembly` | `System.Reflection.Assembly.GetEntryAssemblyNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Assembly.CoreCLR.cs:89 |
| `AssemblyNative_GetExecutingAssembly` | `System.Reflection.Assembly.GetExecutingAssemblyNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Assembly.CoreCLR.cs:58 |
| `AssemblyName_InitializeAssemblySpec` | `System.Reflection.AssemblyName.InitializeAssemblySpec` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/AssemblyName.CoreCLR.cs:193 |
| `CustomAttribute_CreateCustomAttributeInstance` | `System.Reflection.CustomAttribute.CreateCustomAttributeInstance` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeCustomAttributeData.cs:1862 |
| `CustomAttribute_CreatePropertyOrFieldData` | `System.Reflection.CustomAttribute.CreatePropertyOrFieldData` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeCustomAttributeData.cs:1891 |
| `CustomAttribute_ParseAttributeUsageAttribute` | `System.Reflection.CustomAttribute.ParseAttributeUsageAttribute` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeCustomAttributeData.cs:1838 |
| `AppDomain_CreateDynamicAssembly` | `System.Reflection.Emit.RuntimeAssemblyBuilder.CreateDynamicAssembly` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeAssemblyBuilder.cs:116 |
| `ModuleBuilder_GetArrayMethodToken` | `System.Reflection.Emit.RuntimeModuleBuilder.GetArrayMethodToken` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:168 |
| `ModuleBuilder_GetMemberRef` | `System.Reflection.Emit.RuntimeModuleBuilder.GetMemberRef` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:105 |
| `ModuleBuilder_GetMemberRefFromSignature` | `System.Reflection.Emit.RuntimeModuleBuilder.GetMemberRefFromSignature` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:116 |
| `ModuleBuilder_GetMemberRefOfFieldInfo` | `System.Reflection.Emit.RuntimeModuleBuilder.GetMemberRefOfFieldInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:148 |
| `ModuleBuilder_GetMemberRefOfMethodInfo` | `System.Reflection.Emit.RuntimeModuleBuilder.GetMemberRefOfMethodInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:125 |
| `ModuleBuilder_GetStringConstant` | `System.Reflection.Emit.RuntimeModuleBuilder.GetStringConstant` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:171 |
| `ModuleBuilder_GetTokenFromTypeSpec` | `System.Reflection.Emit.RuntimeModuleBuilder.GetTokenFromTypeSpec` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:159 |
| `ModuleBuilder_GetTypeRef` | `System.Reflection.Emit.RuntimeModuleBuilder.GetTypeRef` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:102 |
| `ModuleBuilder_SetFieldRVAContent` | `System.Reflection.Emit.RuntimeModuleBuilder.SetFieldRVAContent` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeModuleBuilder.cs:174 |
| `TypeBuilder_AddInterfaceImpl` | `System.Reflection.Emit.RuntimeTypeBuilder.AddInterfaceImpl` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:64 |
| `TypeBuilder_DefineCustomAttribute` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineCustomAttribute` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:88 |
| `TypeBuilder_DefineEvent` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineEvent` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:103 |
| `TypeBuilder_DefineField` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineField` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:76 |
| `TypeBuilder_DefineGenericParam` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineGenericParam` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:541 |
| `TypeBuilder_DefineMethod` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:69 |
| `TypeBuilder_DefineMethodImpl` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineMethodImpl` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:110 |
| `TypeBuilder_DefineMethodSemantics` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineMethodSemantics` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:106 |
| `TypeBuilder_DefineMethodSpec` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineMethodSpec` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:73 |
| `TypeBuilder_DefineProperty` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineProperty` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:99 |
| `TypeBuilder_DefineType` | `System.Reflection.Emit.RuntimeTypeBuilder.DefineType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:537 |
| `TypeBuilder_GetTokenFromSig` | `System.Reflection.Emit.RuntimeTypeBuilder.GetTokenFromSig` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:120 |
| `TypeBuilder_SetClassLayout` | `System.Reflection.Emit.RuntimeTypeBuilder.SetClassLayout` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:126 |
| `TypeBuilder_SetConstantValue` | `System.Reflection.Emit.RuntimeTypeBuilder.SetConstantValue` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:129 |
| `TypeBuilder_SetFieldLayoutOffset` | `System.Reflection.Emit.RuntimeTypeBuilder.SetFieldLayoutOffset` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:123 |
| `TypeBuilder_SetMethodIL` | `System.Reflection.Emit.RuntimeTypeBuilder.SetMethodIL` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:80 |
| `TypeBuilder_SetMethodImpl` | `System.Reflection.Emit.RuntimeTypeBuilder.SetMethodImpl` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:113 |
| `TypeBuilder_SetPInvokeData` | `System.Reflection.Emit.RuntimeTypeBuilder.SetPInvokeData` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:132 |
| `TypeBuilder_SetParamInfo` | `System.Reflection.Emit.RuntimeTypeBuilder.SetParamInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:116 |
| `TypeBuilder_SetParentType` | `System.Reflection.Emit.RuntimeTypeBuilder.SetParentType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:61 |
| `TypeBuilder_TermCreateClass` | `System.Reflection.Emit.RuntimeTypeBuilder.TermCreateClass` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/RuntimeTypeBuilder.cs:545 |
| `LoaderAllocator_Destroy` | `System.Reflection.LoaderAllocatorScout.Destroy` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/LoaderAllocator.cs:31 |
| `AssemblyNative_InternalTryGetRawMetadata` | `System.Reflection.Metadata.AssemblyExtensions.InternalTryGetRawMetadata` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Metadata/AssemblyExtensions.cs:14 |
| `AssemblyNative_ApplyUpdate` | `System.Reflection.Metadata.MetadataUpdater.ApplyUpdate` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Metadata/MetadataUpdater.cs:14 |
| `AssemblyNative_IsApplyUpdateSupported` | `System.Reflection.Metadata.MetadataUpdater.IsApplyUpdateSupported` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/Metadata/MetadataUpdater.cs:18 |
| `MetadataImport_Enum` | `System.Reflection.MetadataImport.Enum` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:337 |
| `MetadataImport_GetMarshalAs` | `System.Reflection.MetadataImport.GetMarshalAs` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MdImport.cs:233 |
| `MethodBase_GetCurrentMethod` | `System.Reflection.MethodBase.GetCurrentMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/MethodBase.CoreCLR.cs:40 |
| `AssemblyNative_GetCodeBase` | `System.Reflection.RuntimeAssembly.GetCodeBase` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:72 |
| `AssemblyNative_GetEntryPoint` | `System.Reflection.RuntimeAssembly.GetEntryPoint` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:162 |
| `AssemblyNative_GetExportedTypes` | `System.Reflection.RuntimeAssembly.GetExportedTypes` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:238 |
| `AssemblyNative_GetFlags` | `System.Reflection.RuntimeAssembly.GetFlags` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:609 |
| `AssemblyNative_GetForwardedType` | `System.Reflection.RuntimeAssembly.GetForwardedType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:804 |
| `AssemblyNative_GetFullName` | `System.Reflection.RuntimeAssembly.GetFullName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:142 |
| `AssemblyNative_GetHashAlgorithm` | `System.Reflection.RuntimeAssembly.GetHashAlgorithm` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:600 |
| `AssemblyNative_GetImageRuntimeVersion` | `System.Reflection.RuntimeAssembly.GetImageRuntimeVersion` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:527 |
| `AssemblyNative_GetLocale` | `System.Reflection.RuntimeAssembly.GetLocale` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:560 |
| `AssemblyNative_GetLocation` | `System.Reflection.RuntimeAssembly.GetLocation` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:511 |
| `AssemblyHandle_GetManifestModuleSlow` | `System.Reflection.RuntimeAssembly.GetManifestModuleSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:716 |
| `AssemblyNative_GetManifestResourceInfo` | `System.Reflection.RuntimeAssembly.GetManifestResourceInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:489 |
| `AssemblyNative_GetManifestResourceNames` | `System.Reflection.RuntimeAssembly.GetManifestResourceNames` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:464 |
| `AssemblyNative_GetModule` | `System.Reflection.RuntimeAssembly.GetModule` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:410 |
| `AssemblyNative_GetModules` | `System.Reflection.RuntimeAssembly.GetModules` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:674 |
| `AssemblyNative_GetPublicKey` | `System.Reflection.RuntimeAssembly.GetPublicKey` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:618 |
| `AssemblyNative_GetReferencedAssemblies` | `System.Reflection.RuntimeAssembly.GetReferencedAssemblies` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:477 |
| `AssemblyNative_GetResource` | `System.Reflection.RuntimeAssembly.GetResource` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:270 |
| `AssemblyNative_GetSimpleName` | `System.Reflection.RuntimeAssembly.GetSimpleName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:589 |
| `AssemblyNative_GetTypeCore` | `System.Reflection.RuntimeAssembly.GetTypeCore` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:181 |
| `AssemblyNative_GetTypeCoreIgnoreCase` | `System.Reflection.RuntimeAssembly.GetTypeCoreIgnoreCase` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:188 |
| `AssemblyNative_GetVersion` | `System.Reflection.RuntimeAssembly.GetVersion` | stub | src/il2cppc/Emit/MethodMap.RuntimeAssembly.cs:30 -> `il2cpp::runtime::icall_assembly_get_version_unsupported` (PNSE stub) |
| `AssemblyNative_InternalLoad` | `System.Reflection.RuntimeAssembly.InternalLoad` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeAssembly.cs:398 |
| `RuntimeModule_GetFullyQualifiedName` | `System.Reflection.RuntimeModule.GetFullyQualifiedName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeModule.cs:404 |
| `RuntimeModule_GetScopeName` | `System.Reflection.RuntimeModule.GetScopeName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeModule.cs:485 |
| `RuntimeModule_GetTypes` | `System.Reflection.RuntimeModule.GetTypes` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/RuntimeModule.cs:419 |
| `UnsafeAccessors_ResolveGenericParamToTypeHandle` | `System.Reflection.TypeNameResolver.ResolveGenericParamToTypeHandle` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Reflection/TypeNameResolver.CoreCLR.cs:201 |
| `AsyncHelpers_AddContinuationToExInternal` | `System.Runtime.CompilerServices.AsyncHelpers.AddContinuationToExInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncHelpers.CoreCLR.cs:320 |
| `ThrowInvalidCastException` | `System.Runtime.CompilerServices.CastHelpers.ThrowInvalidCastExceptionInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/CastHelpers.cs:18 |
| `TypeHandle_CanCastTo_NoCacheLookup` | `System.Runtime.CompilerServices.TypeHandle.CanCastTo_NoCacheLookup` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1321 |
| `TypeHandle_GetCorElementType` | `System.Runtime.CompilerServices.TypeHandle.GetCorElementType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:1325 |
| `ThreadNative_Abort` | `System.Runtime.ControlledExecution.AbortThread` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ControlledExecution.CoreCLR.cs:122 |
| `ThreadNative_ResetAbort` | `System.Runtime.ControlledExecution.ResetAbortThread` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ControlledExecution.CoreCLR.cs:126 |
| `DependentHandle_InternalAllocWithGCTransition` | `System.Runtime.DependentHandle._InternalAllocWithGCTransition` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:247 |
| `DependentHandle_InternalFreeWithGCTransition` | `System.Runtime.DependentHandle._InternalFreeWithGCTransition` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/DependentHandle.cs:279 |
| `AppendExceptionStackFrame` | `System.Runtime.ExceptionServices.InternalCalls.RhpAppendExceptionStackFrame` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ExceptionServices/InternalCalls.cs:39 |
| `CallFilterFunclet` | `System.Runtime.ExceptionServices.InternalCalls.RhpCallFilterFunclet` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ExceptionServices/InternalCalls.cs:33 |
| `EHEnumInitFromStackFrameIterator` | `System.Runtime.ExceptionServices.InternalCalls.RhpEHEnumInitFromStackFrameIterator` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ExceptionServices/InternalCalls.cs:46 |
| `EHEnumNext` | `System.Runtime.ExceptionServices.InternalCalls.RhpEHEnumNext` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ExceptionServices/InternalCalls.cs:52 |
| `SfiInit` | `System.Runtime.ExceptionServices.InternalCalls.RhpSfiInit` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ExceptionServices/InternalCalls.cs:21 |
| `SfiNext` | `System.Runtime.ExceptionServices.InternalCalls.RhpSfiNext` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/ExceptionServices/InternalCalls.cs:27 |
| `GCHandle_InternalAllocWithGCTransition` | `System.Runtime.InteropServices.GCHandle._InternalAllocWithGCTransition` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:26 |
| `GCHandle_InternalFreeWithGCTransition` | `System.Runtime.InteropServices.GCHandle._InternalFreeWithGCTransition` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/GCHandle.CoreCLR.cs:42 |
| `MarshalNative_ChangeWrapperHandleStrength` | `System.Runtime.InteropServices.Marshal.ChangeWrapperHandleStrength` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:1038 |
| `MarshalNative_CreateAggregatedObject` | `System.Runtime.InteropServices.Marshal.CreateAggregatedObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:624 |
| `MarshalNative_FinalReleaseComObject` | `System.Runtime.InteropServices.Marshal.FinalReleaseComObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:707 |
| `MarshalNative_GetComInterfaceForObject` | `System.Runtime.InteropServices.Marshal.GetComInterfaceForObject` | half-wired | src/il2cppc/Emit/MethodMap.Marshal.cs:504 (arm without runtime symbol) |
| `MarshalNative_GetDelegateForFunctionPointerInternal` | `System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointerInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:1049 |
| `MarshalNative_GetEndComSlot` | `System.Runtime.InteropServices.Marshal.GetEndComSlot` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:982 |
| `MarshalNative_GetExceptionForHR` | `System.Runtime.InteropServices.Marshal.GetExceptionForHRInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:442 |
| `MarshalNative_GetFunctionPointerForDelegateInternal` | `System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegateInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:1057 |
| `MarshalNative_GetHINSTANCE` | `System.Runtime.InteropServices.Marshal.GetHINSTANCE` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:430 |
| `MarshalNative_GetStartComSlot` | `System.Runtime.InteropServices.Marshal.GetStartComSlot` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:965 |
| `MarshalNative_GetTypeFromCLSID` | `System.Runtime.InteropServices.Marshal.GetTypeFromCLSID` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:487 |
| `MarshalNative_GetTypedObjectForIUnknown` | `System.Runtime.InteropServices.Marshal.GetTypedObjectForIUnknown` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:606 |
| `MarshalNative_GetUniqueObjectForIUnknown` | `System.Runtime.InteropServices.Marshal.GetUniqueObjectForIUnknown` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:585 |
| `MarshalNative_HasLayout` | `System.Runtime.InteropServices.Marshal.HasLayout` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:307 |
| `MarshalNative_CleanupUnusedObjectsInCurrentContext` | `System.Runtime.InteropServices.Marshal.InternalCleanupUnusedObjectsInCurrentContext` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:641 |
| `MarshalNative_InternalCreateWrapperOfType` | `System.Runtime.InteropServices.Marshal.InternalCreateWrapperOfType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:833 |
| `MarshalNative_Prelink` | `System.Runtime.InteropServices.Marshal.InternalPrelink` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:225 |
| `MarshalNative_IsBuiltInComSupported` | `System.Runtime.InteropServices.Marshal.IsBuiltInComSupportedInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:409 |
| `MarshalNative_IsTypeVisibleFromCom` | `System.Runtime.InteropServices.Marshal.IsTypeVisibleFromCom` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:850 |
| `MarshalNative_SizeOfHelper` | `System.Runtime.InteropServices.Marshal.SizeOfHelper` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.CoreCLR.cs:36 |
| `NativeLibrary_LoadByName` | `System.Runtime.InteropServices.NativeLibrary.LoadByName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/InteropServices/NativeLibrary.CoreCLR.cs:25 |
| `TypeMapLazyDictionary_FindPrecachedExternalTypeMapEntry` | `System.Runtime.InteropServices.TypeMapLazyDictionary.FindPrecachedExternalTypeMapEntry` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/TypeMapLazyDictionary.cs:92 |
| `TypeMapLazyDictionary_FindPrecachedProxyTypeMapEntry` | `System.Runtime.InteropServices.TypeMapLazyDictionary.FindPrecachedProxyTypeMapEntry` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/TypeMapLazyDictionary.cs:95 |
| `TypeMapLazyDictionary_ProcessAttributes` | `System.Runtime.InteropServices.TypeMapLazyDictionary.ProcessAttributes` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/TypeMapLazyDictionary.cs:82 |
| `X86Base_CpuId` | `System.Runtime.Intrinsics.X86.X86Base.CpuId` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/X86/X86Base.cs:129 |
| `AssemblyNative_GetLoadContextForAssembly` | `System.Runtime.Loader.AssemblyLoadContext.GetLoadContextForAssembly` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:146 |
| `AssemblyNative_InitializeAssemblyLoadContext` | `System.Runtime.Loader.AssemblyLoadContext.InitializeAssemblyLoadContext` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:15 |
| `MultiCoreJIT_InternalSetProfileRoot` | `System.Runtime.Loader.AssemblyLoadContext.InternalSetProfileRoot` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:25 |
| `MultiCoreJIT_InternalStartProfile` | `System.Runtime.Loader.AssemblyLoadContext.InternalStartProfile` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:28 |
| `AssemblyNative_LoadFromInMemoryModule` | `System.Runtime.Loader.AssemblyLoadContext.LoadFromInMemoryModuleInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:87 |
| `AssemblyNative_LoadFromPath` | `System.Runtime.Loader.AssemblyLoadContext.LoadFromPath` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:32 |
| `AssemblyNative_LoadFromStream` | `System.Runtime.Loader.AssemblyLoadContext.LoadFromStream` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:22 |
| `AssemblyNative_PrepareForAssemblyLoadContextRelease` | `System.Runtime.Loader.AssemblyLoadContext.PrepareForAssemblyLoadContextRelease` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:18 |
| `AssemblyNative_TraceAssemblyLoadFromResolveHandlerInvoked` | `System.Runtime.Loader.AssemblyLoadContext.TraceAssemblyLoadFromResolveHandlerInvoked` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:57 |
| `AssemblyNative_TraceAssemblyResolveHandlerInvoked` | `System.Runtime.Loader.AssemblyLoadContext.TraceAssemblyResolveHandlerInvoked` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:53 |
| `AssemblyNative_TraceResolvingHandlerInvoked` | `System.Runtime.Loader.AssemblyLoadContext.TraceResolvingHandlerInvoked` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:49 |
| `AssemblyNative_TraceSatelliteSubdirectoryPathProbed` | `System.Runtime.Loader.AssemblyLoadContext.TraceSatelliteSubdirectoryPathProbed` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyLoadContext.CoreCLR.cs:61 |
| `RuntimeFieldHandle_GetEnCFieldAddr` | `System.RuntimeFieldHandle.GetEnCFieldAddr` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1730 |
| `RuntimeFieldHandle_GetFieldDataReference` | `System.RuntimeFieldHandle.GetFieldDataReference` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1601 |
| `RuntimeFieldHandle_GetRVAFieldInfo` | `System.RuntimeFieldHandle.GetRVAFieldInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1582 |
| `RuntimeFieldHandle_GetValue` | `System.RuntimeFieldHandle.GetValue` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1614 |
| `RuntimeFieldHandle_GetValueDirect` | `System.RuntimeFieldHandle.GetValueDirect` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1636 |
| `RuntimeFieldHandle_SetValue` | `System.RuntimeFieldHandle.SetValue` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1657 |
| `RuntimeFieldHandle_SetValueDirect` | `System.RuntimeFieldHandle.SetValueDirect` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1677 |
| `RuntimeMethodHandle_ConstructInstantiation` | `System.RuntimeMethodHandle.ConstructInstantiation` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1099 |
| `RuntimeMethodHandle_Destroy` | `System.RuntimeMethodHandle.Destroy` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1353 |
| `RuntimeMethodHandle_GetFunctionPointer` | `System.RuntimeMethodHandle.GetFunctionPointer` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1066 |
| `RuntimeMethodHandle_GetMethodBody` | `System.RuntimeMethodHandle.GetMethodBody` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1359 |
| `RuntimeMethodHandle_GetMethodInstantiation` | `System.RuntimeMethodHandle.GetMethodInstantiation` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1234 |
| `RuntimeMethodHandle_GetNativeCode` | `System.RuntimeMethodHandle.GetNativeCode` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1295 |
| `RuntimeMethodHandle_GetStubIfNeededSlow` | `System.RuntimeMethodHandle.GetStubIfNeededSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1273 |
| `RuntimeMethodHandle_GetTypicalMethodDefinition` | `System.RuntimeMethodHandle.GetTypicalMethodDefinition` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1318 |
| `RuntimeMethodHandle_InvokeMethod` | `System.RuntimeMethodHandle.InvokeMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1180 |
| `RuntimeMethodHandle_IsCAVisibleFromDecoratedType` | `System.RuntimeMethodHandle.IsCAVisibleFromDecoratedType` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1079 |
| `RuntimeMethodHandle_StripMethodInstantiation` | `System.RuntimeMethodHandle.StripMethodInstantiation` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:1337 |
| `ReflectionInvocation_GetComObjectGuid` | `System.RuntimeType.GetComObjectGuid` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CoreCLR.cs:3430 |
| `ReflectionInvocation_GetGuid` | `System.RuntimeType.GetGuid` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CoreCLR.cs:3418 |
| `ReflectionInvocation_InvokeDispMethod` | `System.RuntimeType.InvokeDispMethod` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CoreCLR.cs:4140 |
| `ReflectionInvocation_GetBoxInfo` | `System.RuntimeType.BoxCache.GetBoxInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.BoxCache.cs:134 |
| `ReflectionSerialization_GetCreateUninitializedObjectInfo` | `System.RuntimeType.CreateUninitializedCache.GetCreateUninitializedInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CreateUninitializedCache.CoreCLR.cs:79 |
| `RuntimeTypeHandle_AllocateComObject` | `System.RuntimeTypeHandle.AllocateComObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:367 |
| `RuntimeTypeHandle_ConstructName` | `System.RuntimeTypeHandle.ConstructName` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:651 |
| `RuntimeTypeHandle_CreateInstanceForAnotherGenericParameter` | `System.RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:278 |
| `QCall_FreeGCHandleForTypeHandle` | `System.RuntimeTypeHandle.FreeGCHandle` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:601 |
| `RuntimeTypeHandle_GetActivationInfo` | `System.RuntimeTypeHandle.GetActivationInfo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:357 |
| `RuntimeTypeHandle_GetArgumentTypesFromFunctionPointer` | `System.RuntimeTypeHandle.GetArgumentTypesFromFunctionPointer` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:488 |
| `RuntimeTypeHandle_GetAssemblySlow` | `System.RuntimeTypeHandle.GetAssemblySlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:401 |
| `RuntimeTypeHandle_GetConstraints` | `System.RuntimeTypeHandle.GetConstraints` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:579 |
| `RuntimeTypeHandle_GetDeclaringMethodForGenericParameter` | `System.RuntimeTypeHandle.GetDeclaringMethodForGenericParameter` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:725 |
| `RuntimeTypeHandle_GetDeclaringTypeHandle` | `System.RuntimeTypeHandle.GetDeclaringTypeHandle` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:695 |
| `RuntimeTypeHandle_GetDeclaringTypeHandleForGenericVariable` | `System.RuntimeTypeHandle.GetDeclaringTypeHandleForGenericVariable` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:692 |
| `RuntimeTypeHandle_GetFields` | `System.RuntimeTypeHandle.GetFields` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:539 |
| `QCall_GetGCHandleForTypeHandle` | `System.RuntimeTypeHandle.GetGCHandle` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:592 |
| `RuntimeTypeHandle_GetGenericTypeDefinition` | `System.RuntimeTypeHandle.GetGenericTypeDefinition` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:851 |
| `RuntimeTypeHandle_GetInstantiation` | `System.RuntimeTypeHandle.GetInstantiation` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:737 |
| `RuntimeTypeHandle_GetInterfaceMethodImplementation` | `System.RuntimeTypeHandle.GetInterfaceMethodImplementation` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:632 |
| `RuntimeTypeHandle_GetInterfaces` | `System.RuntimeTypeHandle.GetInterfaces` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:560 |
| `RuntimeTypeHandle_GetMethodAt` | `System.RuntimeTypeHandle.GetMethodAt` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:461 |
| `RuntimeTypeHandle_GetModuleSlow` | `System.RuntimeTypeHandle.GetModuleSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:420 |
| `RuntimeTypeHandle_GetNumVirtualsAndStaticVirtuals` | `System.RuntimeTypeHandle.GetNumVirtualsAndStaticVirtuals` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:613 |
| `RuntimeTypeHandle_GetRuntimeTypeFromHandleSlow` | `System.RuntimeTypeHandle.GetRuntimeTypeFromHandleSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:37 |
| `RuntimeTypeHandle_Instantiate` | `System.RuntimeTypeHandle.Instantiate` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:756 |
| `RuntimeTypeHandle_InternalAlloc` | `System.RuntimeTypeHandle.InternalAlloc` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:300 |
| `RuntimeTypeHandle_InternalAllocNoChecks` | `System.RuntimeTypeHandle.InternalAllocNoChecks` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:318 |
| `RuntimeTypeHandle_IsEquivalentTo` | `System.RuntimeTypeHandle.IsEquivalentTo` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:905 |
| `RuntimeTypeHandle_MakeArray` | `System.RuntimeTypeHandle.MakeArray` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:784 |
| `RuntimeTypeHandle_MakeByRef` | `System.RuntimeTypeHandle.MakeByRef` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:806 |
| `RuntimeTypeHandle_MakeFunctionPointer` | `System.RuntimeTypeHandle.MakeFunctionPointer` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:817 |
| `RuntimeTypeHandle_MakePointer` | `System.RuntimeTypeHandle.MakePointer` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:840 |
| `RuntimeTypeHandle_MakeSZArray` | `System.RuntimeTypeHandle.MakeSZArray` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:795 |
| `RuntimeTypeHandle_RegisterCollectibleTypeDependency` | `System.RuntimeTypeHandle.RegisterCollectibleTypeDependency` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:889 |
| `RuntimeTypeHandle_SatisfiesConstraints` | `System.RuntimeTypeHandle.SatisfiesConstraints` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:878 |
| `RuntimeTypeHandle_VerifyInterfaceIsImplemented` | `System.RuntimeTypeHandle.VerifyInterfaceIsImplemented` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:622 |
| `RuntimeTypeHandle_IsVisible` | `System.RuntimeTypeHandle._IsVisible` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:643 |
| `Signature_AreEqual` | `System.Signature.AreEqual` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2135 |
| `Signature_GetCustomModifiersAtOffset` | `System.Signature.GetCustomModifiersAtOffset` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2198 |
| `Signature_Init` | `System.Signature.Init` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeHandles.cs:2062 |

#### P3（68 条）

| EntryPoint | 托管声明 | 状态 | 证据 |
|---|---|---|---|
| `ExceptionNative_ThrowAmbiguousResolutionException` | `Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowAmbiguousResolutionException` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/Internal/Runtime/CompilerHelpers/ThrowHelpers.cs:15 |
| `ExceptionNative_ThrowClassAccessException` | `Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowClassAccessExceptionInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/Internal/Runtime/CompilerHelpers/ThrowHelpers.cs:71 |
| `ExceptionNative_ThrowEntryPointNotFoundException` | `Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowEntryPointNotFoundException` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/Internal/Runtime/CompilerHelpers/ThrowHelpers.cs:29 |
| `ExceptionNative_ThrowFieldAccessException` | `Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowFieldAccessExceptionInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/Internal/Runtime/CompilerHelpers/ThrowHelpers.cs:57 |
| `ExceptionNative_ThrowMethodAccessException` | `Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowMethodAccessExceptionInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/Internal/Runtime/CompilerHelpers/ThrowHelpers.cs:43 |
| `VersionResilientHashCode_TypeHashCode` | `Internal.VersionResilientHashCode.TypeHashCode` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/Internal/VersionResilientHashCode.CoreCLR.cs:17 |
| `AppContext_SetFirstChanceExceptionHandler` | `System.AppContext.SetFirstChanceExceptionHandlerInternal` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/AppContext.CoreCLR.cs:67 |
| `AppContext_TryGetHostPropertyValue` | `System.AppContext.TryGetHostPropertyValue` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/AppContext.CoreCLR.cs:21 |
| `ClrConfig_GetConfigBoolValue` | `System.CLRConfig.GetConfigBoolValue` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/CLRConfig.cs:20 |
| `ComWeakRefToObject` | `System.ComAwareWeakReference.ComWeakRefToObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ComAwareWeakReference.CoreCLR.cs:13 |
| `ObjectToComWeakRef` | `System.ComAwareWeakReference.ObjectToComWeakRef` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ComAwareWeakReference.CoreCLR.cs:42 |
| `EventPipeInternal_CreateProvider` | `System.Diagnostics.Tracing.EventPipeInternal.CreateProvider` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:32 |
| `EventPipeInternal_DefineEvent` | `System.Diagnostics.Tracing.EventPipeInternal.DefineEvent` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:37 |
| `EventPipeInternal_DeleteProvider` | `System.Diagnostics.Tracing.EventPipeInternal.DeleteProvider` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:43 |
| `EventPipeInternal_Disable` | `System.Diagnostics.Tracing.EventPipeInternal.Disable` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:26 |
| `EventPipeInternal_Enable` | `System.Diagnostics.Tracing.EventPipeInternal.Enable` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:18 |
| `EventPipeInternal_EventActivityIdControl` | `System.Diagnostics.Tracing.EventPipeInternal.EventActivityIdControl` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:46 |
| `EventPipeInternal_GetNextEvent` | `System.Diagnostics.Tracing.EventPipeInternal.GetNextEvent` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:60 |
| `EventPipeInternal_GetProvider` | `System.Diagnostics.Tracing.EventPipeInternal.GetProvider` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:40 |
| `EventPipeInternal_GetSessionInfo` | `System.Diagnostics.Tracing.EventPipeInternal.GetSessionInfo` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:56 |
| `EventPipeInternal_SignalSession` | `System.Diagnostics.Tracing.EventPipeInternal.SignalSession` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:64 |
| `EventPipeInternal_WaitForSessionSignal` | `System.Diagnostics.Tracing.EventPipeInternal.WaitForSessionSignal` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:68 |
| `EventPipeInternal_WriteEventData` | `System.Diagnostics.Tracing.EventPipeInternal.WriteEventData` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipe.Internal.cs:49 |
| `NativeRuntimeEventSource_LogContentionLockCreated` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogContentionLockCreated` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:13 |
| `NativeRuntimeEventSource_LogContentionStart` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogContentionStart` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:17 |
| `NativeRuntimeEventSource_LogContentionStop` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogContentionStop` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:26 |
| `NativeRuntimeEventSource_LogThreadPoolIODequeue` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolIODequeue` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:80 |
| `NativeRuntimeEventSource_LogThreadPoolIOEnqueue` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolIOEnqueue` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:72 |
| `NativeRuntimeEventSource_LogThreadPoolIOPack` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolIOPack` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:93 |
| `NativeRuntimeEventSource_LogThreadPoolMinMaxThreads` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolMinMaxThreads` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:45 |
| `NativeRuntimeEventSource_LogThreadPoolWorkerThreadAdjustmentAdjustment` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadAdjustmentAdjustment` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:53 |
| `NativeRuntimeEventSource_LogThreadPoolWorkerThreadAdjustmentSample` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadAdjustmentSample` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:49 |
| `NativeRuntimeEventSource_LogThreadPoolWorkerThreadAdjustmentStats` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadAdjustmentStats` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:57 |
| `NativeRuntimeEventSource_LogThreadPoolWorkerThreadStart` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadStart` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:33 |
| `NativeRuntimeEventSource_LogThreadPoolWorkerThreadStop` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadStop` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:37 |
| `NativeRuntimeEventSource_LogThreadPoolWorkerThreadWait` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkerThreadWait` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:41 |
| `NativeRuntimeEventSource_LogThreadPoolWorkingThreadCount` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogThreadPoolWorkingThreadCount` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:87 |
| `NativeRuntimeEventSource_LogWaitHandleWaitStart` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogWaitHandleWaitStart` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:100 |
| `NativeRuntimeEventSource_LogWaitHandleWaitStop` | `System.Diagnostics.Tracing.NativeRuntimeEventSource.LogWaitHandleWaitStop` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.Threading.NativeSinks.Internal.cs:107 |
| `Enum_GetValuesAndNames` | `System.Enum.GetEnumValuesAndNames` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Enum.CoreCLR.cs:15 |
| `MdUtf8String_EqualsCaseInsensitive` | `System.MdUtf8String.EqualsCaseInsensitive` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CoreCLR.cs:4371 |
| `memmove` | `System.SpanHelpers.memmove` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.ByteMemOps.cs:265 |
| `memset` | `System.SpanHelpers.memset` | missing | no wiring; non-public helper decl reference/netcore/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.ByteMemOps.cs:488 |
| `InterfaceMarshaler_ConvertToManaged` | `System.StubHelpers.InterfaceMarshaler.ConvertToManaged` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:648 |
| `InterfaceMarshaler_ConvertToNative` | `System.StubHelpers.InterfaceMarshaler.ConvertToNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:635 |
| `InterfaceMarshaler_GetObjectForComCallableWrapperIUnknown` | `System.StubHelpers.InterfaceMarshaler.GetObjectForComCallableWrapperIUnknown` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:666 |
| `InterfaceMarshaler_ValidateComVisibilityForIUnknown` | `System.StubHelpers.InterfaceMarshaler.ValidateComVisibilityForIUnknown` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:669 |
| `MngdSafeArrayMarshaler_ClearNative` | `System.StubHelpers.MngdSafeArrayMarshaler.ClearNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:727 |
| `MngdSafeArrayMarshaler_ConvertContentsToManaged` | `System.StubHelpers.MngdSafeArrayMarshaler.ConvertContentsToManaged` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:717 |
| `MngdSafeArrayMarshaler_ConvertContentsToNative` | `System.StubHelpers.MngdSafeArrayMarshaler.ConvertContentsToNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:698 |
| `MngdSafeArrayMarshaler_ConvertSpaceToManaged` | `System.StubHelpers.MngdSafeArrayMarshaler.ConvertSpaceToManaged` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:708 |
| `MngdSafeArrayMarshaler_ConvertSpaceToNative` | `System.StubHelpers.MngdSafeArrayMarshaler.ConvertSpaceToNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:688 |
| `MngdSafeArrayMarshaler_CreateMarshaler` | `System.StubHelpers.MngdSafeArrayMarshaler.CreateMarshaler` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:679 |
| `ObjectMarshaler_ConvertToManaged` | `System.StubHelpers.ObjectMarshaler.ConvertToManaged` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:552 |
| `ObjectMarshaler_ConvertToNative` | `System.StubHelpers.ObjectMarshaler.ConvertToNative` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:542 |
| `StubHelpers_CreateCustomMarshaler` | `System.StubHelpers.StubHelpers.CreateCustomMarshaler` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2238 |
| `StubHelpers_CreateLayoutClassMarshalStubs` | `System.StubHelpers.StubHelpers.CreateLayoutClassMarshalStubs` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2394 |
| `StubHelpers_GetCOMIPFromRCWSlow` | `System.StubHelpers.StubHelpers.GetCOMIPFromRCWSlow` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2345 |
| `StubHelpers_MarshalToManagedVaList` | `System.StubHelpers.StubHelpers.MarshalToManagedVaList` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2507 |
| `StubHelpers_MarshalToUnmanagedVaList` | `System.StubHelpers.StubHelpers.MarshalToUnmanagedVaList` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2510 |
| `StubHelpers_MulticastDebuggerTraceHelper` | `System.StubHelpers.StubHelpers.MulticastDebuggerTraceHelperQCall` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2537 |
| `StubHelpers_ThrowInteropParamException` | `System.StubHelpers.StubHelpers.ThrowInteropParamException` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2177 |
| `StubHelpers_ValidateByref` | `System.StubHelpers.StubHelpers.ValidateByref` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2525 |
| `StubHelpers_ValidateObject` | `System.StubHelpers.StubHelpers.ValidateObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/StubHelpers.cs:2519 |
| `MethodTable_CanCompareBitsOrUseFastGetHashCode` | `System.ValueType.CanCompareBitsOrUseFastGetHashCodeHelper` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ValueType.cs:87 |
| `ValueType_GetHashCodeStrategy` | `System.ValueType.GetHashCodeStrategy` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/ValueType.cs:166 |
| `Variant_ConvertValueTypeToRecord` | `System.Variant.ConvertValueTypeToRecord` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Variant.cs:22 |
| `MarshalNative_GetIUnknownOrIDispatchForObject` | `System.Variant.GetIUnknownOrIDispatchForObject` | missing | no wiring; non-public helper decl reference/coreclr/src/coreclr/System.Private.CoreLib/src/System/Variant.cs:31 |

### 4.3 RuntimeImport 缺口（corert 契约面）

ILC 不是 Redhawk：`Rh*`/`Rhp*` 是 NativeAOT/Redhawk 的运行时 ABI，本仓 `src/runtime` 里
并不存在对应入口。下表按类别汇总，不逐条列 173 行（脚本 `--json` 可导出全表）。

| 类别 | 条数 | 已接 | 说明 |
|---|---|---|---|
| libm 数学（`sin`/`cos`/`pow`/`fma`/`modf`/`scalbn` …） | 55 | 语义已由 `icall_math*`/`icall_mathf*` 直接调 libm 覆盖 | 不需要 Redhawk 入口名 |
| `Rh*` GC/线程/异常运行时 ABI | 94 | 1（`RhBulkMoveWithWriteBarrier`） | 其余在 ILC 用自有 icall 表达 |
| `Rhp*` 平台原语 | 24 | 0 | Redhawk 专属汇编/平台桩，ILC 无对应物 |

已接的唯一一条证据：

- `RhBulkMoveWithWriteBarrier` -> `src/il2cppc/Emit/MethodMap.GC.cs:320` (`il2cpp::runtime::icall_rh_bulk_move_with_write_barrier`)

### 4.4 QCall no-decl（表有、参考树无声明）

| EntryPoint | 证据行 |
|---|---|
| `GetTypeLoadExceptionMessage` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:411` |
| `GetFileLoadExceptionMessage` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:412` |
| `InitClassHelper` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:531` |
| `ResolveVirtualFunctionPointer` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:532` |
| `GetThreadStaticsByMethodTable` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:533` |
| `GetThreadStaticsByIndex` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:534` |
| `GenericHandleWorker` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:535` |
| `IsInstanceOf_NoCacheLookup` | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:537` |

### 4.5 别名候选（入口名只差 `_` / `Internal` / `Native` / `Slow` 后缀）

这些 `missing` 行的托管名，在**同一 owner 的既有 case 表**里存在一个
归一化后同名的分支 —— 即接线成本很低（同一 case 加别名）。
表由脚本按 `strip(_)/strip(Internal|Native|Slow)` 归一化后比对得出，
只是候选，仍需逐个核对签名/语义，**不是已接**。

| EntryPoint | 托管名 | 既有分支 | 既有符号 |
|---|---|---|---|
| `AssemblyNative_GetEntryAssembly` | `GetEntryAssemblyNative` | `GetEntryAssembly` | fold / literal marker（非 icall，加别名只会变成 fold） |
| `AssemblyNative_GetExecutingAssembly` | `GetExecutingAssemblyNative` | `GetExecutingAssembly` | fold / literal marker（非 icall，加别名只会变成 fold） |
| `DebugDebugger_Break` | `BreakInternal` | `Break` | `il2cpp::runtime::il2cpp_debugger_break` |
| `DebugDebugger_IsLoggingHelper` | `IsLoggingInternal` | `IsLogging` | `il2cpp::runtime::il2cpp_debugger_is_logging` |
| `DebugDebugger_Log` | `LogInternal` | `Log` | `il2cpp::runtime::il2cpp_debugger_log` |
| `Delegate_GetMulticastInvokeSlow` | `GetMulticastInvokeSlow` | `GetMulticastInvoke` | `il2cpp::runtime::icall_delegate_get_multicast_invoke` |
| `GCInterface_AddMemoryPressure` | `_AddMemoryPressure` | `AddMemoryPressure` | `il2cpp::runtime::icall_gc_add_memory_pressure` |
| `GCInterface_Collect` | `_Collect` | `Collect` | `il2cpp::runtime::icall_gc_collect` |
| `GCInterface_RemoveMemoryPressure` | `_RemoveMemoryPressure` | `RemoveMemoryPressure` | `il2cpp::runtime::icall_gc_remove_memory_pressure` |
| `GCInterface_WaitForPendingFinalizers` | `_WaitForPendingFinalizers` | `WaitForPendingFinalizers` | `il2cpp::runtime::icall_gc_finalize_trigger` |
| `MarshalNative_GetDelegateForFunctionPointerInternal` | `GetDelegateForFunctionPointerInternal` | `GetDelegateForFunctionPointer` | `il2cpp::runtime::icall_delegate_from_fn_ptr` |
| `MarshalNative_GetFunctionPointerForDelegateInternal` | `GetFunctionPointerForDelegateInternal` | `GetFunctionPointerForDelegate` | `il2cpp::runtime::icall_delegate_get_fn_ptr` |
| `ObjectNative_GetHashCodeSlow` | `GetHashCodeSlow` | `GetHashCode` | `il2cpp::runtime::icall_object_get_hash_code` |
| `ReflectionInvocation_RunClassConstructor` | `RunClassConstructor` | `_RunClassConstructor` | fold / literal marker（非 icall，加别名只会变成 fold） |
| `ReflectionInvocation_RunModuleConstructor` | `RunModuleConstructor` | `_RunModuleConstructor` | fold / literal marker（非 icall，加别名只会变成 fold） |
| `ThreadNative_SetApartmentState` | `SetApartmentState` | `SetApartmentStateNative` | `il2cpp::runtime::icall_threadobj_set_apartment_state_native` |
| `ThreadNative_SpinWait` | `SpinWaitInternal` | `SpinWait` | `il2cpp::runtime::icall_threadobj_spin_wait` |
| `ThreadNative_Start` | `StartInternal` | `Start` | `il2cpp::runtime::icall_threadobj_start` |
| `ThreadNative_YieldThread` | `YieldInternal` | `Yield` | `il2cpp::runtime::icall_thread_yield` |

合计 **19** 条。（FCall 段的同类候选见 §6 第 1–3 条。）

## 5. 与 reference 子集裁剪情况的说明

**`reference/netcore/src/libraries/` 只有 4 个项目**：
`System.Private.CoreLib` / `System.Console` / `System.Private.Uri` /
`System.Text.RegularExpressions`。**这是裁剪子集，不是完整 upstream。**

- `System.Private.CoreLib` 有 **1718** 个 `.cs`。
- 作为对照，`reference/coreclr/src/coreclr/System.Private.CoreLib` 只有 **152** 个 `.cs`，
  但它是 **CoreCLR 侧**的 CoreLib 增量（`*.CoreCLR.cs`），不是完整实现。

### 5.1 本仓参考树里**根本没有源码**的域

按 QCall 表的 owner 统计（owner 取自 `reference/coreclr` 的 C# 声明；
`no-decl` 行取 EntryPoint 前缀）。最后一列的含义是：
**netcore 裁剪子集里是否存在该 owner 的 QCall 声明**（`否` = 该域的 QCall 面
只能从 coreclr 侧增量读到，或完全无源码）。注意“该类型在 netcore 子集里存在”
不等于“该类型有 QCall 声明”（如 `System.Threading.Thread` 在 netcore 里有
InternalCall 声明，但没有 QCall 声明）。

| owner | QCall 条数 | netcore 子集有该 owner 的 QCall 声明 |
|---|---|---|
| `System.RuntimeTypeHandle` | 34 | **否** |
| `System.Runtime.InteropServices.Marshal` | 29 | **否** |
| `System.Reflection.RuntimeAssembly` | 23 | **否** |
| `System.Reflection.Emit.RuntimeTypeBuilder` | 21 | **否** |
| `System.Threading.Thread` | 21 | **否** |
| `System.GC` | 19 | **否** |
| `System.Diagnostics.Tracing.NativeRuntimeEventSource` | 16 | 是 |
| `System.Runtime.Loader.AssemblyLoadContext` | 13 | **否** |
| `System.Runtime.CompilerServices.RuntimeHelpers` | 12 | **否** |
| `System.Diagnostics.Tracing.EventPipeInternal` | 12 | 是 |
| `System.RuntimeMethodHandle` | 11 | **否** |
| `System.Reflection.Emit.RuntimeModuleBuilder` | 9 | **否** |
| `System.StubHelpers.StubHelpers` | 9 | **否** |
| `System.Delegate` | 8 | **否** |
| `System.ModuleHandle` | 8 | **否** |
| `System.RuntimeFieldHandle` | 7 | **否** |
| `System.Diagnostics.Debugger` | 6 | **否** |
| `System.StubHelpers.MngdSafeArrayMarshaler` | 6 | **否** |
| `System.Private.CoreLib.ComWrappers` | 6 | **否** |
| `System.Runtime.ExceptionServices.InternalCalls` | 6 | **否** |
| `System.ArgIterator` | 5 | **否** |
| `Internal.Runtime.CompilerHelpers.ThrowHelpers` | 5 | **否** |
| `System.Private.CoreLib.SystemJS` | 5 | **否** |
| `System.Private.CoreLib.TrackerObjectManager` | 4 | **否** |

此外，下列整块域在本仓的**全部**参考树（netcore + coreclr 增量）里都找不到
InternalCall/QCall 的 C# 声明，只能从 s_QCall[] 表反推：

- `GetTypeLoadExceptionMessage`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:411`）
- `GetFileLoadExceptionMessage`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:412`）
- `InitClassHelper`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:531`）
- `ResolveVirtualFunctionPointer`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:532`）
- `GetThreadStaticsByMethodTable`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:533`）
- `GetThreadStaticsByIndex`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:534`）
- `GenericHandleWorker`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:535`）
- `IsInstanceOf_NoCacheLookup`（`reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp:537`）

### 5.2 裁剪带来的结论边界

1. §2 里“对上”的判定只说明**名字对得上权威表**；是否已接线仍以 §3/§4 的状态列为准。
   裁剪子集里缺的声明会让行落到 `no-decl`，不能当成“已接”。
2. FCall 表的 owner 是全量 upstream 的类名（`System.RuntimeTypeHandle` 等），
   这些类在 netcore 子集里是**类型转发/partial**，真实实现在别处；
   因此本表对 P2 段（反射/句柄/装配）的缺口数字是**上界**。
3. ILC 的接线面是按 **net48 形状**生长的（见 `docs/audit/icall-inventory-net48.md`），
   netcore 的 QCall 面天然大面积落在 `missing`。
4. `missing` 行的 `non-public helper` 注释只说明**可达路径**（用户代码要通过 public
   包装绕进来），不改变“无接线”的事实；包装方法本身也需要该 icall 才能工作。

## 6. 下一步建议（P0/P1 优先队列）

1. **GC FCall 面（P0，8 条）**：`GetGenerationInternal` / `GetMemoryLoad` / `GetTotalAllocatedBytesApproximate` / `SuppressFinalizeInternal` / `_CancelFullGCNotification` / `_CollectionCount` / `_GetTotalPauseDuration` / `_RegisterForFullGCNotification`。
   ILC 已有 `CollectionCount` / `SuppressFinalize` / `GetTotalAllocatedBytes` /
   `RegisterForFullGCNotification` / `CancelFullGCNotification` 的**别名**分支
   （`MethodMap.GC.cs:96/56/62/102/107`），把 netcore 的 `_`/`Internal` 名字加进
   同一 case 即可，属最小改动。
2. **Interlocked FCall 别名（P0，9 条）**：`Exchange32/64/Object`、
   `CompareExchange32/32Pointer/64/Object`、`ExchangeAdd32/64` 是 netcore 的**内部名**，
   ILC 只接了公开名 `Exchange`/`CompareExchange`/`ExchangeAdd`
   （`MethodMap.Interlocked.cs:57/63/101`）。加别名即可。
3. **RuntimeHelpers FCall（P0，5 条）**：`TryGetHashCode`/`ContentEquals`/
   `TryEnsureSufficientExecutionStack`/`GetTailCallArgBuffer`/`GetTailCallInfo` ——
   现有 `GetHashCode`/`Equals`/`EnsureSufficientExecutionStack` 分支
   （`MethodMap.RuntimeHelpers.cs:47/56/80`）是 net48 名字，需加 netcore 别名。
4. **Thread FCall（P0，3 条）**：`InternalFinalize`/`CatchAtSafePoint`/
   `CurrentThreadIsFinalizerThread` —— 后者在 netcore 子集里被 `System.Threading.Thread`
   直接声明为 InternalCall，是**用户可见 Thread 语义**的一部分。
5. **QCall P0 段（60 条）**：集中在 `System.Threading.Thread`(18) / `System.GC`(16) / `System.Runtime.CompilerServices.RuntimeHelpers`(12) / `System.Delegate`(7)，
   建议按 owner 成批接，而不是逐条接；先做 §4.5 的 19 条别名候选（同一 case 加别名即可）。
6. `half-wired` 的 2 条：`Math.ModF`（需 tuple/out 发射支持）与
   `Marshal.GetComInterfaceForObject`（现有 COM marker 路径）；属转译器能力缺口，
   应单独立项。

