# mscorlib InternalCall 现状盘点（net48）

> 只读审计。目标程序集：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll`（net48）。
> 本版数字由 `tools/audit/run-audit.ps1` 于源码提交 `7b04e079b2e713f468107fd26026f2f10053d963`（`git rev-list -1 HEAD -- src`）重新生成；五类之和必须等于 **908**，脚本内 `assert sum == 908`，不成立则渲染器直接失败退出。

## 0. 结论摘要

- net48 mscorlib InternalCall 总数：**908**（反射通道 908 / 元数据通道 908，逐条 join key 一致）
- 判定分布（五类，部分影子单列）：已接 **194** + 半接 **30** + 缺 **594**（LOUD 拒绝 281 + 无接线 313）+ 影子 **74** + 部分影子 **16** = 194+30+594+74+16 = **908**，`assert sum == 908` 通过
- 可达性分级（仅统计非「已接」的 714 条）：P0 **42**、P1 **290**、P2 **98**、P3 **284**
- public API 面（类型 public 且方法 public）共 **141** 条，其中已接 **99**、半接 12、LOUD 25、无接线 5
- **「已接」不等于语义等价 net48**：30 条「半接」全部是运行时占位实现（30 条空体/恒值桩，见 §3.8），不是真实计算。

### 0.1 判定口径（逐条都在 §2/§3 给出 file:line）

| 判定 | 含义 | 证据形态 |
|---|---|---|
| 已接 | 转译器的 `MethodRefResolver.BclResolve` 对这条 icall 返回了 C++ 目标，且该目标在 `src/runtime` 有真实实现（不是抛异常桩，也不是恒值桩）；折叠标记（`::identity::` 等）视为已接，因为发射器直接内联 | `MethodMap.*.cs:<line>`（dispatcher 分支）+ 运行时定义 |
| 半接 | 有 dispatch 目标，但运行时实现不交付语义：是 `*_loud` 抛异常桩（调用必然抛），或空体 / 单一 `return <常量>;` 恒值桩（永远返回同一个字面量，不看参数） | `MethodMap.*.cs:<line>` + 桩定义 `src/runtime/src/<file>:<line>` |
| 缺（LOUD 拒绝）| 转译器对该成员显式抛 `NotSupportedException`，并在转译期把整个方法标记为未转译（不是静默错误） | `MethodMap.*.cs:<line>`（throw 点）+ 原始消息 |
| 缺（无接线）| 没有 owner 分支也没有方法分支，落到生成的幽灵函数名（`System_Xxx_<hash>`），**C++ 编译期**失败（见 §6.3） | `no wiring` |
| 影子 | 该 icall 自身无接线，但用户可见的入口方法已接线：mscorlib 方法体不会被转译，用户 IL 只发出对入口的那一次调用，所以这条 icall 不在生成的 C++ 走到的任何路径上。**必须看跳数**：1 跳才可写「由 X 直接承接」 | 承接入口 + 其 `MethodMap.*.cs:<line>` |
| 部分影子 | 影子结论只部分成立：同一最短距离上还有未接线的入口，或该入口有同名未映射的重载，用户写那种形状仍会落到幽灵符号 | 承接入口 + 其 `MethodMap.*.cs:<line>` |

可达性分级（对非「已接」行）：

| 级别 | 定义 |
|---|---|
| P0 | 类型 public 且方法 public —— 用户 IL 可直接 call |
| P1 | 非 public 面，但存在 public 入口到它的调用链；`publicDistance == 1` 一律为 P1（命名空间不得压低可及性） |
| P2 | 链路深度 ≥4，或深度 ≥2 且属主位于 reflection / remoting / interop / diagnostics / serialization / security / AppDomain / CompilerServices 命名空间（按**类型全名**带分隔符匹配） |
| P3 | mscorlib 内部无任何 public 入口可达（legacy / CLR 内部 / 已废弃） |

分类键是 `(声明类型全名, 方法名, 签名)`。同名重载的可达性与接线状态可以完全不同（`Interlocked::CompareExchange` 的数值重载已接，泛型 `!!0&` 重载未映射），折叠成 `(owner, name)` 会让一个重载的证据污染另一个。

## 1. 方法与证据（双通道一致性与差异）

两条**互相独立**的通道，join key 为 `(声明类型全名, 方法名, 签名, method|ctor)`：

| 通道 | 实现 | 结果 |
|---|---|---|
| 反射 | `tools/audit/enumerate-icall-reflection.ps1`（Windows PowerShell 5.1，`Assembly.LoadFrom` Framework mscorlib，枚举全部类型含 non-public 的 methods+ctors，过滤 `MethodImplAttributes.InternalCall`） | 908 |
| 元数据 | `tools/audit/MetadataIcallCounter`（.NET 9 + `System.Reflection.Metadata`，直接读 `MethodDef.ImplAttributes` 位） | 908 |

- **两通道数量相等：908 = 908**；join key 集合完全相同（`Compare-Object` 差集为 0）。
- 类型数反射 3354 / 元数据 3356，差 2 是 `<Module>` 等反射不可枚举行，不含 icall，不影响计数。
- 首轮曾出现 6 行 join 差异，全部是 `` !0 `` 泛型参数在反射通道被渲染成空串（`System.WeakReference`1::Create/get_Target/set_Target`）。已修正反射脚本的 `Format-ParamType`（泛型参数渲染为 `!n` / `!!n`），修正后差异归零 —— 这是**渲染差异，不是方法差异**。

方法学：判定不是用正则猜的。`tools/audit/IcallWiringProbe` 直接加载 `il2cppc` 程序集并对这 908 条 icall逐条调用真实入口 `MethodRefResolver.BclResolve`，因此「已接/LOUD/无接线」来自转译器自身的运行结果，而不是对源码的文本匹配。

可达性另有独立证据：`tools/audit/IcallCallGraph` 解码 mscorlib 全部托管方法体（29257 个方法，解码失败 0 个）建立调用图，从 public 面（12406 个可被用户直接调用的方法）做**前向** BFS，得到「该 icall 到最近 public 入口的距离 / 该入口是谁 / 中间经过哪几跳」。边按签名精确匹配，因此重载不会互相借距离；BFS 前沿按序展开，结果与 Python 哈希随机化无关，可重复。

## 2. 已接清单

共 **194** 条。证据列是 dispatcher 的 `MethodMap.*.cs:<line>`，即 `BclResolveCore` 的 owner 分支最终命中的 `TryMap*` 里那个方法名分支。

| # | 声明类型 | 方法 | 签名 | 可见性 | 运行时目标 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `System.AppDomain` | `GetDefaultDomain` | `System.AppDomain ()` | public/internal/static | `icall_appdomain_get_default_domain` | `MethodMap.Legacy.cs:169` |
| 2 | `System.AppDomain` | `GetFusionContext` | `System.IntPtr ()` | public/internal/instance | `icall_appdomain_get_fusion_context` | `MethodMap.Legacy.cs:175` |
| 3 | `System.AppDomain` | `GetId` | `System.Int32 ()` | public/internal/instance | `icall_appdomain_get_id` | `MethodMap.Legacy.cs:167` |
| 4 | `System.AppDomain` | `GetOrInternString` | `System.String (System.String)` | public/internal/instance | `icall_appdomain_get_or_intern_string` | `MethodMap.Legacy.cs:165` |
| 5 | `System.AppDomain` | `GetSecurityDescriptor` | `System.IntPtr ()` | public/internal/instance | `icall_appdomain_get_security_descriptor` | `MethodMap.Legacy.cs:177` |
| 6 | `System.AppDomain` | `IsDomainIdValid` | `System.Boolean (System.Int32)` | public/internal/static | `icall_appdomain_is_domain_id_valid` | `MethodMap.Legacy.cs:173` |
| 7 | `System.AppDomain` | `IsFinalizingForUnload` | `System.Boolean ()` | public/public/instance | `icall_appdomain_is_finalizing_for_unload` | `MethodMap.Legacy.cs:157` |
| 8 | `System.AppDomain` | `IsStringInterned` | `System.String (System.String)` | public/internal/instance | `icall_appdomain_is_string_interned` | `MethodMap.Legacy.cs:163` |
| 9 | `System.AppDomain` | `IsUnloadingForcedFinalize` | `System.Boolean ()` | public/internal/instance | `icall_appdomain_is_unloading_forced_finalize` | `MethodMap.Legacy.cs:171` |
| 10 | `System.AppDomain` | `PublishAnonymouslyHostedDynamicMethodsAssembly` | `System.Void (System.Reflection.RuntimeAssembly)` | public/internal/static | `icall_appdomain_publish_anonymously_hosted_dynamic_methods_assembly` | `MethodMap.Legacy.cs:159` |
| 11 | `System.AppDomain` | `UpdateLoaderOptimization` | `System.Void (System.LoaderOptimization)` | public/internal/instance | `icall_appdomain_update_loader_optimization` | `MethodMap.Legacy.cs:183` |
| 12 | `System.AppDomain` | `nCreateDomain` | `System.AppDomain (System.String, System.AppDomainSetup, System.Security.Policy.Evidence, System.Security.Policy.Evidence, System.IntPtr)` | public/internal/static | `icall_appdomain_n_create_domain` | `MethodMap.Legacy.cs:179` |
| 13 | `System.AppDomain` | `nCreateInstance` | `System.Runtime.Remoting.ObjRef (System.String, System.AppDomainSetup, System.Security.Policy.Evidence, System.Security.Policy.Evidence, System.IntPtr)` | public/internal/static | `icall_appdomain_n_create_instance` | `MethodMap.Legacy.cs:181` |
| 14 | `System.AppDomain` | `nUnload` | `System.Void (System.Int32)` | public/internal/static | `icall_appdomain_n_unload` | `MethodMap.Legacy.cs:185` |
| 15 | `System.Array` | `Clear` | `System.Void (System.Array, System.Int32, System.Int32)` | public/public/static | `icall_array_clear_int32` | `MethodMap.Array.cs:525` |
| 16 | `System.Array` | `Copy` | `System.Void (System.Array, System.Int32, System.Array, System.Int32, System.Int32, System.Boolean)` | public/internal/static | `icall_array_copy` | `MethodMap.Array.cs:607` |
| 17 | `System.Array` | `GetDataPtrOffsetInternal` | `System.Int32 ()` | public/internal/instance | `icall_array_get_data_ptr_offset_internal` | `MethodMap.Array.cs:1190` |
| 18 | `System.Array` | `GetLength` | `System.Int32 (System.Int32)` | public/public/instance | `icall_array_get_length` | `MethodMap.Array.cs:1083` |
| 19 | `System.Array` | `GetLowerBound` | `System.Int32 (System.Int32)` | public/public/instance | `icall_array_get_lower_bound` | `MethodMap.Array.cs:1114` |
| 20 | `System.Array` | `GetUpperBound` | `System.Int32 (System.Int32)` | public/public/instance | `icall_array_get_upper_bound` | `MethodMap.Array.cs:1085` |
| 21 | `System.Array` | `Initialize` | `System.Void ()` | public/public/instance | `icall_array_initialize` | `MethodMap.Array.cs:1118` |
| 22 | `System.Array` | `get_Length` | `System.Int32 ()` | public/public/instance | `il2cpp_array_get_length` | `MethodMap.Array.cs:1087` |
| 23 | `System.Array` | `get_LongLength` | `System.Int64 ()` | public/public/instance | `icall_array_get_long_length` | `MethodMap.Array.cs:1110` |
| 24 | `System.Array` | `get_Rank` | `System.Int32 ()` | public/public/instance | `icall_array_get_rank` | `MethodMap.Array.cs:1096` |
| 25 | `System.BaseConfigHandler` | `RunParser` | `System.Void (System.String)` | internal/internal/instance | `icall_base_config_handler_run_parser` | `MethodMap.GC.cs:235` |
| 26 | `System.Buffer` | `BlockCopy` | `System.Void (System.Array, System.Int32, System.Array, System.Int32, System.Int32)` | public/public/static | `il2cpp_buffer_block_copy` | `MethodMap.Buffer.cs:29` |
| 27 | `System.Buffer` | `InternalBlockCopy` | `System.Void (System.Array, System.Int32, System.Array, System.Int32, System.Int32)` | public/internal/static | `il2cpp_buffer_block_copy` | `MethodMap.Buffer.cs:66` |
| 28 | `System.CLRConfig` | `CheckLegacyManagedDeflateStream` | `System.Boolean ()` | internal/internal/static | `icall_clr_config_check_legacy_managed_deflate_stream` | `MethodMap.GC.cs:244` |
| 29 | `System.CLRConfig` | `CheckThrowUnobservedTaskExceptions` | `System.Boolean ()` | internal/internal/static | `icall_clr_config_check_throw_unobserved_task_exceptions` | `MethodMap.GC.cs:245` |
| 30 | `System.DateTime` | `GetSystemTimeAsFileTime` | `System.Int64 ()` | public/internal/static | `icall_datetime_get_system_time_as_file_time` | `MethodMap.DateTime.cs:220` |
| 31 | `System.DateTime` | `GetSystemTimeWithLeapSecondsHandling` | `System.Void (System.DateTime+FullSystemTime&)` | public/internal/static | `icall_datetime_get_system_time_with_leap_seconds_handling` | `MethodMap.DateTime.cs:222` |
| 32 | `System.DateTime` | `SystemFileTimeToSystemTime` | `System.Boolean (System.Int64, System.DateTime+FullSystemTime&)` | public/internal/static | `icall_datetime_system_file_time_to_system_time` | `MethodMap.DateTime.cs:223` |
| 33 | `System.DateTime` | `SystemTimeToSystemFileTime` | `System.Boolean (System.DateTime+FullSystemTime&, System.Int64&)` | public/internal/static | `icall_datetime_system_time_to_system_file_time` | `MethodMap.DateTime.cs:224` |
| 34 | `System.DateTime` | `ValidateSystemTime` | `System.Boolean (System.DateTime+FullSystemTime&, System.Boolean)` | public/internal/static | `icall_datetime_validate_system_time` | `MethodMap.DateTime.cs:221` |
| 35 | `System.Decimal` | `.ctor` | `System.Void (System.Double)` | public/public/instance | `icall_decimal_ctor_double` | `MethodMap.Decimal.cs:337` |
| 36 | `System.Decimal` | `.ctor` | `System.Void (System.Single)` | public/public/instance | `icall_decimal_ctor_float` | `MethodMap.Decimal.cs:337` |
| 37 | `System.Decimal` | `FCallAddSub` | `System.Void (System.Decimal&, System.Decimal&, System.Byte)` | public/private/static | `icall_decimal_fcall_addsub` | `MethodMap.Decimal.cs:393` |
| 38 | `System.Decimal` | `FCallAddSubOverflowed` | `System.Void (System.Decimal&, System.Decimal&, System.Byte, System.Boolean&)` | public/private/static | `icall_decimal_fcall_addsub` | `MethodMap.Decimal.cs:397` |
| 39 | `System.Decimal` | `FCallDivide` | `System.Void (System.Decimal&, System.Decimal&)` | public/private/static | `icall_decimal_fcall_divide` | `MethodMap.Decimal.cs:409` |
| 40 | `System.Decimal` | `FCallDivideOverflowed` | `System.Void (System.Decimal&, System.Decimal&, System.Boolean&)` | public/private/static | `icall_decimal_fcall_divide` | `MethodMap.Decimal.cs:413` |
| 41 | `System.Decimal` | `FCallMultiply` | `System.Void (System.Decimal&, System.Decimal&)` | public/private/static | `icall_decimal_fcall_multiply` | `MethodMap.Decimal.cs:401` |
| 42 | `System.Decimal` | `FCallMultiplyOverflowed` | `System.Void (System.Decimal&, System.Decimal&, System.Boolean&)` | public/private/static | `icall_decimal_fcall_multiply` | `MethodMap.Decimal.cs:405` |
| 43 | `System.Decimal` | `FCallToInt32` | `System.Int32 (System.Decimal)` | public/internal/static | `icall_decimal_fcall_to_int32` | `MethodMap.Decimal.cs:389` |
| 44 | `System.Decimal` | `GetHashCode` | `System.Int32 ()` | public/public/instance | `icall_decimal_get_hash_code` | `MethodMap.Decimal.cs:219` |
| 45 | `System.Decimal` | `ToDouble` | `System.Double (System.Decimal)` | public/public/static | `icall_decimal_to_double` | `MethodMap.Decimal.cs:378` |
| 46 | `System.Decimal` | `ToSingle` | `System.Single (System.Decimal)` | public/public/static | `icall_decimal_to_float` | `MethodMap.Decimal.cs:382` |
| 47 | `System.Delegate` | `AdjustTarget` | `System.IntPtr (System.Object, System.IntPtr)` | public/internal/instance | `icall_delegate_adjust_target` | `MethodMap.Delegate.cs:109` |
| 48 | `System.Delegate` | `CompareUnmanagedFunctionPtrs` | `System.Boolean (System.Delegate, System.Delegate)` | public/internal/static | `icall_delegate_compare_unmanaged_function_ptrs` | `MethodMap.Delegate.cs:113` |
| 49 | `System.Delegate` | `GetCallStub` | `System.IntPtr (System.IntPtr)` | public/internal/instance | `icall_delegate_get_call_stub` | `MethodMap.Delegate.cs:110` |
| 50 | `System.Delegate` | `GetInvokeMethod` | `System.IntPtr ()` | public/internal/instance | `icall_delegate_get_invoke_method` | `MethodMap.Delegate.cs:106` |
| 51 | `System.Delegate` | `GetMulticastInvoke` | `System.IntPtr ()` | public/internal/instance | `icall_delegate_get_multicast_invoke` | `MethodMap.Delegate.cs:105` |
| 52 | `System.Delegate` | `InternalAllocLike` | `System.MulticastDelegate (System.Delegate)` | public/internal/static | `icall_delegate_internal_alloc_like` | `MethodMap.Delegate.cs:104` |
| 53 | `System.Delegate` | `InternalEqualMethodHandles` | `System.Boolean (System.Delegate, System.Delegate)` | public/internal/static | `icall_delegate_internal_equal_method_handles` | `MethodMap.Delegate.cs:108` |
| 54 | `System.Delegate` | `InternalEqualTypes` | `System.Boolean (System.Object, System.Object)` | public/internal/static | `icall_delegate_internal_equal_types` | `MethodMap.Delegate.cs:112` |
| 55 | `System.Diagnostics.Assert` | `ShowDefaultAssertDialog` | `System.Int32 (System.String, System.String, System.String, System.String)` | internal/internal/static | `icall_assert_show_default_assert_dialog` | `MethodMap.GC.cs:264` |
| 56 | `System.Diagnostics.Debugger` | `Log` | `System.Void (System.Int32, System.String, System.String)` | public/public/static | `il2cpp_debugger_log` | `MethodMap.Debugger.cs:52` |
| 57 | `System.Diagnostics.Log` | `AddLogSwitch` | `System.Void (System.Diagnostics.LogSwitch)` | internal/internal/static | `icall_log_add_log_switch` | `MethodMap.GC.cs:265` |
| 58 | `System.Diagnostics.Log` | `ModifyLogSwitch` | `System.Void (System.Int32, System.String, System.String)` | internal/internal/static | `icall_log_modify_log_switch` | `MethodMap.GC.cs:266` |
| 59 | `System.Enum` | `Equals` | `System.Boolean (System.Object)` | public/public/instance | `il2cpp_enum_equals` | `MethodMap.Enum.cs:363` |
| 60 | `System.Enum` | `InternalGetUnderlyingType` | `System.RuntimeType (System.RuntimeType)` | public/internal/static | `icall_enum_internal_get_underlying_type` | `MethodMap.Enum.cs:425` |
| 61 | `System.Environment` | `FailFast` | `System.Void (System.String)` | public/public/static | `icall_environment_fail_fast` | `MethodMap.Environment.cs:126` |
| 62 | `System.Environment` | `FailFast` | `System.Void (System.String, System.Exception)` | public/public/static | `icall_environment_fail_fast` | `MethodMap.Environment.cs:126` |
| 63 | `System.Environment` | `FailFast` | `System.Void (System.String, System.UInt32)` | public/internal/static | `icall_environment_fail_fast` | `MethodMap.Environment.cs:126` |
| 64 | `System.Environment` | `GetCompatibilityFlag` | `System.Boolean (System.CompatibilityFlag)` | public/internal/static | `icall_environment_get_compatibility_flag` | `MethodMap.Environment.cs:155` |
| 65 | `System.Environment` | `GetResourceFromDefault` | `System.String (System.String)` | public/internal/static | `icall_environment_get_resource_from_default` | `MethodMap.Environment.cs:154` |
| 66 | `System.Environment` | `GetVersion` | `System.Boolean (Microsoft.Win32.Win32Native+OSVERSIONINFO)` | public/internal/static | `icall_environment_get_version` | `MethodMap.Environment.cs:152` |
| 67 | `System.Environment` | `get_ExitCode` | `System.Int32 ()` | public/public/static | `icall_environment_get_exit_code` | `MethodMap.Environment.cs:117` |
| 68 | `System.Environment` | `get_TickCount` | `System.Int32 ()` | public/public/static | `icall_environment_get_tick_count` | `MethodMap.Environment.cs:32` |
| 69 | `System.Environment` | `get_TickCount64` | `System.Int64 ()` | public/internal/static | `icall_environment_get_tick_count64` | `MethodMap.Environment.cs:39` |
| 70 | `System.Environment` | `nativeGetEnvironmentVariable` | `System.String (System.String)` | public/internal/static | `icall_environment_get_environment_variable_native` | `MethodMap.Environment.cs:139` |
| 71 | `System.Environment` | `set_ExitCode` | `System.Void (System.Int32)` | public/public/static | `icall_environment_set_exit_code` | `MethodMap.Environment.cs:118` |
| 72 | `System.GC` | `GetGCLatencyMode` | `System.Int32 ()` | public/internal/static | `icall_gc_get_latency_mode` | `MethodMap.GC.cs:79` |
| 73 | `System.GC` | `GetGeneration` | `System.Int32 (System.Object)` | public/public/static | `icall_gc_get_generation` | `MethodMap.GC.cs:54` |
| 74 | `System.GC` | `GetLOHCompactionMode` | `System.Int32 ()` | public/internal/static | `icall_gc_get_loh_compaction_mode` | `MethodMap.GC.cs:82` |
| 75 | `System.GC` | `GetMaxGeneration` | `System.Int32 ()` | public/private/static | `il2cpp_gc_get_max_generation` | `MethodMap.GC.cs:117` |
| 76 | `System.GC` | `SetGCLatencyMode` | `System.Int32 (System.Int32)` | public/internal/static | `icall_gc_set_latency_mode` | `MethodMap.GC.cs:81` |
| 77 | `System.GC` | `SetLOHCompactionMode` | `System.Void (System.Int32)` | public/internal/static | `icall_gc_set_loh_compaction_mode` | `MethodMap.GC.cs:83` |
| 78 | `System.Globalization.CultureInfo` | `nativeGetLocaleInfoEx` | `System.String (System.String, System.UInt32)` | public/internal/static | `icall_culture_info_native_get_locale_info_ex_int` | `MethodMap.CultureInfo.cs:96` |
| 79 | `System.Globalization.CultureInfo` | `nativeGetLocaleInfoExInt` | `System.Int32 (System.String, System.UInt32)` | public/internal/static | `icall_culture_info_native_get_locale_info_ex_int` | `MethodMap.CultureInfo.cs:97` |
| 80 | `System.Globalization.CultureInfo` | `nativeSetThreadLocale` | `System.Boolean (System.String)` | public/internal/static | `icall_culture_info_native_set_thread_locale` | `MethodMap.CultureInfo.cs:98` |
| 81 | `System.Math` | `Abs` | `System.Double (System.Double)` | public/public/static | `icall_math_abs_d` | `MethodMap.Math.cs:19` |
| 82 | `System.Math` | `Abs` | `System.Single (System.Single)` | public/public/static | `icall_math_abs_d` | `MethodMap.Math.cs:19` |
| 83 | `System.Math` | `Acos` | `System.Double (System.Double)` | public/public/static | `icall_math_acos_d` | `MethodMap.Math.cs:140` |
| 84 | `System.Math` | `Asin` | `System.Double (System.Double)` | public/public/static | `icall_math_asin_d` | `MethodMap.Math.cs:139` |
| 85 | `System.Math` | `Atan` | `System.Double (System.Double)` | public/public/static | `icall_math_atan_d` | `MethodMap.Math.cs:141` |
| 86 | `System.Math` | `Atan2` | `System.Double (System.Double, System.Double)` | public/public/static | `icall_math_atan2_d` | `MethodMap.Math.cs:142` |
| 87 | `System.Math` | `Ceiling` | `System.Double (System.Double)` | public/public/static | `icall_math_ceiling_d` | `MethodMap.Math.cs:82` |
| 88 | `System.Math` | `Cos` | `System.Double (System.Double)` | public/public/static | `icall_math_cos_d` | `MethodMap.Math.cs:137` |
| 89 | `System.Math` | `Cosh` | `System.Double (System.Double)` | public/public/static | `icall_math_cosh_d` | `MethodMap.Math.cs:158` |
| 90 | `System.Math` | `Exp` | `System.Double (System.Double)` | public/public/static | `icall_math_exp_d` | `MethodMap.Math.cs:153` |
| 91 | `System.Math` | `Floor` | `System.Double (System.Double)` | public/public/static | `icall_math_floor_d` | `MethodMap.Math.cs:78` |
| 92 | `System.Math` | `Log` | `System.Double (System.Double)` | public/public/static | `icall_math_log_d` | `MethodMap.Math.cs:154` |
| 93 | `System.Math` | `Log10` | `System.Double (System.Double)` | public/public/static | `icall_math_log10_d` | `MethodMap.Math.cs:157` |
| 94 | `System.Math` | `Pow` | `System.Double (System.Double, System.Double)` | public/public/static | `icall_math_pow_d` | `MethodMap.Math.cs:77` |
| 95 | `System.Math` | `Round` | `System.Double (System.Double)` | public/public/static | `icall_math_round_d` | `MethodMap.Math.cs:86` |
| 96 | `System.Math` | `Sin` | `System.Double (System.Double)` | public/public/static | `icall_math_sin_d` | `MethodMap.Math.cs:136` |
| 97 | `System.Math` | `Sinh` | `System.Double (System.Double)` | public/public/static | `icall_math_sinh_d` | `MethodMap.Math.cs:159` |
| 98 | `System.Math` | `Sqrt` | `System.Double (System.Double)` | public/public/static | `icall_math_sqrt_d` | `MethodMap.Math.cs:76` |
| 99 | `System.Math` | `Tan` | `System.Double (System.Double)` | public/public/static | `icall_math_tan_d` | `MethodMap.Math.cs:138` |
| 100 | `System.Math` | `Tanh` | `System.Double (System.Double)` | public/public/static | `icall_math_tanh_d` | `MethodMap.Math.cs:160` |
| 101 | `System.Object` | `GetType` | `System.Type ()` | public/public/instance | `icall_object_get_type` | `MethodMap.Object.cs:35` |
| 102 | `System.Object` | `MemberwiseClone` | `System.Object ()` | public/protected/instance | `icall_object_memberwise_clone` | `MethodMap.Object.cs:256` |
| 103 | `System.Runtime.CompilerServices.DependentHandle` | `nFree` | `System.Void (System.IntPtr)` | internal/private/static | `icall_dependenthandle_free` | `MethodMap.DependentHandle.cs:85` |
| 104 | `System.Runtime.CompilerServices.DependentHandle` | `nGetPrimary` | `System.Void (System.IntPtr, System.Object&)` | internal/private/static | `icall_dependenthandle_n_get_primary` | `MethodMap.DependentHandle.cs:73` |
| 105 | `System.Runtime.CompilerServices.DependentHandle` | `nGetPrimaryAndSecondary` | `System.Void (System.IntPtr, System.Object&, System.Object&)` | internal/private/static | `icall_dependenthandle_n_get_primary_and_secondary` | `MethodMap.DependentHandle.cs:79` |
| 106 | `System.Runtime.CompilerServices.DependentHandle` | `nInitialize` | `System.Void (System.Object, System.Object, System.IntPtr&)` | internal/private/static | `icall_dependenthandle_n_init` | `MethodMap.DependentHandle.cs:66` |
| 107 | `System.Runtime.CompilerServices.RuntimeHelpers` | `Equals` | `System.Boolean (System.Object, System.Object)` | public/public/static | `::refeq::` | `MethodMap.RuntimeHelpers.cs:56` |
| 108 | `System.Runtime.CompilerServices.RuntimeHelpers` | `GetHashCode` | `System.Int32 (System.Object)` | public/public/static | `icall_object_get_hash_code` | `MethodMap.RuntimeHelpers.cs:47` |
| 109 | `System.Runtime.CompilerServices.RuntimeHelpers` | `GetObjectValue` | `System.Object (System.Object)` | public/public/static | `::identity::` | `MethodMap.RuntimeHelpers.cs:65` |
| 110 | `System.Runtime.InteropServices.GCHandle` | `InternalGetHandleType` | `System.Runtime.InteropServices.GCHandleType (System.IntPtr)` | public/internal/static | `icall_gchandle_get_handle_type` | `MethodMap.GCHandle.cs:79` |
| 111 | `System.Runtime.InteropServices.Marshal` | `DestroyStructure` | `System.Void (System.IntPtr, System.Type)` | public/public/static | `icall_marshal_destroy_structure` | `MethodMap.Marshal.cs:196` |
| 112 | `System.Runtime.InteropServices.Marshal` | `GetHRForException` | `System.Int32 (System.Exception)` | public/public/static | `icall_marshal_get_hr_for_exception` | `MethodMap.Marshal.cs:431` |
| 113 | `System.Runtime.InteropServices.Marshal` | `GetLastWin32Error` | `System.Int32 ()` | public/public/static | `il2cpp_pinvoke_get_last_error` | `MethodMap.Marshal.cs:95` |
| 114 | `System.Runtime.InteropServices.Marshal` | `GetNativeVariantForObject` | `System.Void (System.Object, System.IntPtr)` | public/public/static | `icall_marshal_get_native_variant_for_object` | `MethodMap.Marshal.cs:447` |
| 115 | `System.Runtime.InteropServices.Marshal` | `GetObjectForIUnknown` | `System.Object (System.IntPtr)` | public/public/static | `icall_com_get_object_for_iunknown` | `MethodMap.Marshal.cs:512` |
| 116 | `System.Runtime.InteropServices.Marshal` | `GetObjectForNativeVariant` | `System.Object (System.IntPtr)` | public/public/static | `icall_marshal_get_object_for_native_variant` | `MethodMap.Marshal.cs:442` |
| 117 | `System.Runtime.InteropServices.Marshal` | `GetObjectsForNativeVariants` | `System.Object[] (System.IntPtr, System.Int32)` | public/public/static | `icall_marshal_get_objects_for_native_variants` | `MethodMap.Marshal.cs:452` |
| 118 | `System.Runtime.InteropServices.Marshal` | `IsComObject` | `System.Boolean (System.Object)` | public/public/static | `icall_marshal_is_com_object` | `MethodMap.Marshal.cs:457` |
| 119 | `System.Runtime.InteropServices.Marshal` | `SetLastWin32Error` | `System.Void (System.Int32)` | public/internal/static | `il2cpp_marshal_set_last_win32_error` | `MethodMap.Marshal.cs:100` |
| 120 | `System.Runtime.InteropServices.Marshal` | `StructureToPtr` | `System.Void (System.Object, System.IntPtr, System.Boolean)` | public/public/static | `il2cpp_marshal_struct_store` | `MethodMap.Marshal.cs:351` |
| 121 | `System.Runtime.InteropServices.Marshal` | `UnsafeAddrOfPinnedArrayElement` | `System.IntPtr (System.Array, System.Int32)` | public/public/static | `icall_marshal_unsafe_addr_of_pinned_array_element` | `MethodMap.Marshal.cs:421` |
| 122 | `System.Runtime.InteropServices.SafeHandle` | `DangerousAddRef` | `System.Void (System.Boolean&)` | public/public/instance | `icall_safehandle_dangerous_add_ref` | `MethodMap.SafeHandle.cs:40` |
| 123 | `System.Runtime.InteropServices.SafeHandle` | `DangerousRelease` | `System.Void ()` | public/public/instance | `icall_safehandle_dangerous_release` | `MethodMap.SafeHandle.cs:48` |
| 124 | `System.Runtime.InteropServices.SafeHandle` | `SetHandleAsInvalid` | `System.Void ()` | public/public/instance | `icall_safehandle_set_handle_as_invalid` | `MethodMap.SafeHandle.cs:52` |
| 125 | `System.Security.Principal.Win32` | `OpenThreadToken` | `System.Int32 (System.Security.Principal.TokenAccessLevels, System.Security.Principal.WinSecurityContext, Microsoft.Win32.SafeHandles.SafeAccessTokenHandle&)` | internal/internal/static | `icall_security_principal_win32_open_thread_token` | `MethodMap.Emit.cs:75` |
| 126 | `System.Security.SecurityContext` | `GetImpersonationFlowMode` | `System.Security.WindowsImpersonationFlowMode ()` | public/internal/static | `icall_security_context_get_impersonation_flow_mode` | `MethodMap.GC.cs:246` |
| 127 | `System.Security.SecurityContext` | `IsDefaultThreadSecurityInfo` | `System.Boolean ()` | public/internal/static | `icall_security_context_is_default_thread_security_info` | `MethodMap.GC.cs:247` |
| 128 | `System.Security.SecurityManager` | `_SetThreadSecurity` | `System.Boolean (System.Boolean)` | public/internal/static | `icall_security_manager_set_thread_security` | `MethodMap.GC.cs:248` |
| 129 | `System.Security.SecurityRuntime` | `GetSecurityObjectForFrame` | `System.Security.FrameSecurityDescriptor (System.Threading.StackCrawlMark&, System.Boolean)` | internal/internal/static | `icall_security_runtime_get_security_object_for_frame` | `MethodMap.GC.cs:249` |
| 130 | `System.String` | `.ctor` | `System.Void (System.Char*)` | public/public/instance | `icall_string_ctor_char_ptr` | `MethodMap.String.cs:15` |
| 131 | `System.String` | `.ctor` | `System.Void (System.Char*, System.Int32, System.Int32)` | public/public/instance | `icall_string_ctor_char_ptr_range` | `MethodMap.String.cs:15` |
| 132 | `System.String` | `.ctor` | `System.Void (System.Char, System.Int32)` | public/public/instance | `icall_string_ctor_repeat` | `MethodMap.String.cs:15` |
| 133 | `System.String` | `.ctor` | `System.Void (System.Char[])` | public/public/instance | `icall_string_ctor_char_array` | `MethodMap.String.cs:15` |
| 134 | `System.String` | `.ctor` | `System.Void (System.Char[], System.Int32, System.Int32)` | public/public/instance | `icall_string_ctor_char_array_range` | `MethodMap.String.cs:15` |
| 135 | `System.String` | `.ctor` | `System.Void (System.SByte*)` | public/public/instance | `icall_string_ctor_sbyte_ptr` | `MethodMap.String.cs:15` |
| 136 | `System.String` | `.ctor` | `System.Void (System.SByte*, System.Int32, System.Int32)` | public/public/instance | `icall_string_ctor_sbyte_ptr_range` | `MethodMap.String.cs:15` |
| 137 | `System.String` | `.ctor` | `System.Void (System.SByte*, System.Int32, System.Int32, System.Text.Encoding)` | public/public/instance | `icall_string_ctor_sbyte_ptr_range_encoding` | `MethodMap.String.cs:15` |
| 138 | `System.String` | `FastAllocateString` | `System.String (System.Int32)` | public/internal/static | `icall_string_fast_allocate` | `MethodMap.String.cs:733` |
| 139 | `System.String` | `IndexOf` | `System.Int32 (System.Char, System.Int32, System.Int32)` | public/public/instance | `icall_string_index_of_c_range` | `MethodMap.String.cs:82` |
| 140 | `System.String` | `IndexOfAny` | `System.Int32 (System.Char[], System.Int32, System.Int32)` | public/public/instance | `icall_string_index_of_any_range` | `MethodMap.String.cs:92` |
| 141 | `System.String` | `InternalMarvin32HashString` | `System.Int32 (System.String, System.Int32, System.Int64)` | public/internal/static | `icall_string_internal_marvin32_hash_string` | `MethodMap.String.cs:748` |
| 142 | `System.String` | `IsAscii` | `System.Boolean ()` | public/internal/instance | `icall_string_is_ascii` | `MethodMap.String.cs:750` |
| 143 | `System.String` | `IsFastSort` | `System.Boolean ()` | public/internal/instance | `icall_string_is_fast_sort` | `MethodMap.String.cs:749` |
| 144 | `System.String` | `LastIndexOf` | `System.Int32 (System.Char, System.Int32, System.Int32)` | public/public/instance | `icall_string_last_index_of_c_range` | `MethodMap.String.cs:132` |
| 145 | `System.String` | `LastIndexOfAny` | `System.Int32 (System.Char[], System.Int32, System.Int32)` | public/public/instance | `icall_string_last_index_of_any_range` | `MethodMap.String.cs:114` |
| 146 | `System.String` | `SetTrailByte` | `System.Void (System.Byte)` | public/internal/instance | `icall_string_set_trail_byte` | `MethodMap.String.cs:738` |
| 147 | `System.String` | `TryGetTrailByte` | `System.Boolean (System.Byte&)` | public/internal/instance | `icall_string_try_get_trail_byte` | `MethodMap.String.cs:742` |
| 148 | `System.String` | `get_Chars` | `System.Char (System.Int32)` | public/public/instance | `icall_string_get_chars` | `MethodMap.String.cs:723` |
| 149 | `System.String` | `get_Length` | `System.Int32 ()` | public/public/instance | `il2cpp_string_get_length` | `MethodMap.String.cs:74` |
| 150 | `System.String` | `nativeCompareOrdinalEx` | `System.Int32 (System.String, System.Int32, System.String, System.Int32, System.Int32)` | public/internal/static | `icall_string_native_compare_ordinal_ex` | `MethodMap.String.cs:746` |
| 151 | `System.String` | `nativeCompareOrdinalIgnoreCaseWC` | `System.Int32 (System.String, System.SByte*)` | public/internal/static | `icall_string_native_compare_ordinal_ignore_case_wc` | `MethodMap.String.cs:747` |
| 152 | `System.Text.StringBuilder` | `ReplaceBufferAnsiInternal` | `System.Void (System.SByte*, System.Int32)` | public/internal/instance | `icall_stringbuilder_replace_buffer_ansi_internal` | `MethodMap.StringBuilder.cs:384` |
| 153 | `System.Text.StringBuilder` | `ReplaceBufferInternal` | `System.Void (System.Char*, System.Int32)` | public/internal/instance | `icall_stringbuilder_replace_buffer_internal` | `MethodMap.StringBuilder.cs:383` |
| 154 | `System.Threading.HostExecutionContextManager` | `CloneHostSecurityContext` | `System.Int32 (System.Runtime.InteropServices.SafeHandle, System.Runtime.InteropServices.SafeHandle)` | public/internal/static | `icall_host_execution_context_manager_clone_host_security_context` | `MethodMap.Thread.cs:270` |
| 155 | `System.Threading.HostExecutionContextManager` | `ReleaseHostSecurityContext` | `System.Int32 (System.IntPtr)` | public/internal/static | `icall_host_execution_context_manager_release_host_security_context` | `MethodMap.Thread.cs:269` |
| 156 | `System.Threading.Interlocked` | `CompareExchange` | `System.Double (System.Double&, System.Double, System.Double)` | public/public/static | `il2cpp_interlocked_compare_exchange_double` | `MethodMap.Interlocked.cs:101` |
| 157 | `System.Threading.Interlocked` | `CompareExchange` | `System.Int32 (System.Int32&, System.Int32, System.Int32)` | public/public/static | `il2cpp_interlocked_compare_exchange` | `MethodMap.Interlocked.cs:101` |
| 158 | `System.Threading.Interlocked` | `CompareExchange` | `System.Int32 (System.Int32&, System.Int32, System.Int32, System.Boolean&)` | public/internal/static | `icall_interlocked_compare_exchange_found` | `MethodMap.Interlocked.cs:101` |
| 159 | `System.Threading.Interlocked` | `CompareExchange` | `System.Int64 (System.Int64&, System.Int64, System.Int64)` | public/public/static | `il2cpp_interlocked_compare_exchange64` | `MethodMap.Interlocked.cs:101` |
| 160 | `System.Threading.Interlocked` | `CompareExchange` | `System.IntPtr (System.IntPtr&, System.IntPtr, System.IntPtr)` | public/public/static | `il2cpp_interlocked_compare_exchange_pointer` | `MethodMap.Interlocked.cs:101` |
| 161 | `System.Threading.Interlocked` | `CompareExchange` | `System.Object (System.Object&, System.Object, System.Object)` | public/public/static | `il2cpp_interlocked_compare_exchange_pointer` | `MethodMap.Interlocked.cs:101` |
| 162 | `System.Threading.Interlocked` | `CompareExchange` | `System.Single (System.Single&, System.Single, System.Single)` | public/public/static | `il2cpp_interlocked_compare_exchange_double` | `MethodMap.Interlocked.cs:101` |
| 163 | `System.Threading.Interlocked` | `Exchange` | `System.Double (System.Double&, System.Double)` | public/public/static | `il2cpp_interlocked_exchange_double` | `MethodMap.Interlocked.cs:63` |
| 164 | `System.Threading.Interlocked` | `Exchange` | `System.Int32 (System.Int32&, System.Int32)` | public/public/static | `il2cpp_interlocked_exchange` | `MethodMap.Interlocked.cs:63` |
| 165 | `System.Threading.Interlocked` | `Exchange` | `System.Int64 (System.Int64&, System.Int64)` | public/public/static | `il2cpp_interlocked_exchange64` | `MethodMap.Interlocked.cs:63` |
| 166 | `System.Threading.Interlocked` | `Exchange` | `System.IntPtr (System.IntPtr&, System.IntPtr)` | public/public/static | `il2cpp_interlocked_exchange_pointer` | `MethodMap.Interlocked.cs:63` |
| 167 | `System.Threading.Interlocked` | `Exchange` | `System.Object (System.Object&, System.Object)` | public/public/static | `il2cpp_interlocked_exchange_pointer` | `MethodMap.Interlocked.cs:63` |
| 168 | `System.Threading.Interlocked` | `Exchange` | `System.Single (System.Single&, System.Single)` | public/public/static | `il2cpp_interlocked_exchange_double` | `MethodMap.Interlocked.cs:63` |
| 169 | `System.Threading.Interlocked` | `ExchangeAdd` | `System.Int32 (System.Int32&, System.Int32)` | public/internal/static | `il2cpp_interlocked_add` | `MethodMap.Interlocked.cs:57` |
| 170 | `System.Threading.Interlocked` | `ExchangeAdd` | `System.Int64 (System.Int64&, System.Int64)` | public/internal/static | `il2cpp_interlocked_add64` | `MethodMap.Interlocked.cs:57` |
| 171 | `System.Threading.Monitor` | `Enter` | `System.Void (System.Object)` | public/public/static | `il2cpp_monitor_enter` | `MethodMap.Monitor.cs:45` |
| 172 | `System.Threading.Monitor` | `Exit` | `System.Void (System.Object)` | public/public/static | `il2cpp_monitor_exit` | `MethodMap.Monitor.cs:59` |
| 173 | `System.Threading.SynchronizationContext` | `WaitHelper` | `System.Int32 (System.IntPtr[], System.Boolean, System.Int32)` | public/protected/static | `icall_synchronization_context_wait_helper` | `MethodMap.Thread.cs:261` |
| 174 | `System.Threading.Thread` | `GetCurrentProcessorNumber` | `System.Int32 ()` | public/private/static | `icall_thread_get_current_processor_number` | `MethodMap.Thread.cs:128` |
| 175 | `System.Threading.Thread` | `MemoryBarrier` | `System.Void ()` | public/public/static | `icall_thread_memory_barrier` | `MethodMap.Thread.cs:63` |
| 176 | `System.Threading.Thread` | `get_IsAlive` | `System.Boolean ()` | public/public/instance | `icall_threadobj_get_is_alive` | `MethodMap.Thread.cs:182` |
| 177 | `System.Threading.Thread` | `get_IsThreadPoolThread` | `System.Boolean ()` | public/public/instance | `icall_thread_get_is_thread_pool_thread` | `MethodMap.Thread.cs:105` |
| 178 | `System.Threading.Thread` | `get_ManagedThreadId` | `System.Int32 ()` | public/public/instance | `icall_threadobj_get_managed_thread_id` | `MethodMap.Thread.cs:186` |
| 179 | `System.Threading.ThreadPool` | `IsThreadPoolHosted` | `System.Boolean ()` | public/internal/static | `icall_thread_pool_is_thread_pool_hosted` | `MethodMap.ThreadPool.cs:68` |
| 180 | `System.Threading.ThreadPool` | `NotifyWorkItemComplete` | `System.Boolean ()` | public/internal/static | `icall_thread_pool_notify_work_item_complete` | `MethodMap.ThreadPool.cs:65` |
| 181 | `System.Threading.ThreadPool` | `NotifyWorkItemProgressNative` | `System.Void ()` | public/internal/static | `icall_thread_pool_notify_work_item_progress_native` | `MethodMap.ThreadPool.cs:67` |
| 182 | `System.Threading.ThreadPool` | `ReportThreadStatus` | `System.Void (System.Boolean)` | public/internal/static | `icall_thread_pool_report_thread_status` | `MethodMap.ThreadPool.cs:66` |
| 183 | `System.Type` | `GetTypeFromHandle` | `System.Type (System.RuntimeTypeHandle)` | public/public/static | `il2cpp_type_get_type_from_handle_value` | `MethodMap.Type.cs:73` |
| 184 | `System.Type` | `op_Equality` | `System.Boolean (System.Type, System.Type)` | public/public/static | `icall_string_equals_static` | `MethodMap.Type.cs:542` |
| 185 | `System.Type` | `op_Inequality` | `System.Boolean (System.Type, System.Type)` | public/public/static | `icall_type_op_inequality` | `MethodMap.Type.cs:549` |
| 186 | `System.ValueType` | `GetHashCode` | `System.Int32 ()` | public/public/instance | `icall_value_type_get_hash_code` | `MethodMap.ValueType.cs:495` |
| 187 | `System.ValueType` | `GetHashCodeOfPtr` | `System.Int32 (System.IntPtr)` | public/internal/static | `icall_value_type_get_hash_code_of_ptr` | `MethodMap.ValueType.cs:468` |
| 188 | `System.WeakReference` | `Finalize` | `System.Void ()` | public/protected/instance | `icall_weak_reference_finalize` | `MethodMap.WeakReference.cs:73` |
| 189 | `System.WeakReference` | `get_IsAlive` | `System.Boolean ()` | public/public/instance | `icall_weakref_get_is_alive` | `MethodMap.WeakReference.cs:59` |
| 190 | `System.WeakReference` | `get_Target` | `System.Object ()` | public/public/instance | `icall_weakref_get_target` | `MethodMap.WeakReference.cs:61` |
| 191 | `System.WeakReference` | `set_Target` | `System.Void (System.Object)` | public/public/instance | `icall_weakref_set_target` | `MethodMap.WeakReference.cs:67` |
| 192 | `System.WeakReference`1` | `Finalize` | `System.Void ()` | public/protected/instance | `icall_weak_reference_finalize` | `MethodMap.WeakReference.cs:73` |
| 193 | `System.WeakReference`1` | `get_Target` | `!0 ()` | public/private/instance | `icall_weakref_get_target` | `MethodMap.WeakReference.cs:61` |
| 194 | `System.WeakReference`1` | `set_Target` | `System.Void (!0)` | public/private/instance | `icall_weakref_set_target` | `MethodMap.WeakReference.cs:67` |

## 3. 缺口清单（按 P0→P3）

「缺口」= 非「已接」且非「影子」的全部行 = **640** 条（半接 / LOUD 拒绝 / 无接线 / 部分影子）。影子单列在 §3.6（结论是「无需接线」），部分影子单列在 §3.7（结论是「只部分覆盖」）。

**本节行数与摘要优先级分布的对应关系**（避免把两套口径误读为互相矛盾）：本节 P0/P1/P2/P3 共 42+219+95+284 = **640** 行，即上段的「缺口」全体；摘要里的优先级分布 42+290+98+284 = **714** = 908−194（已接），即**把 §3.6 的 74 条影子也按优先级计入**。两套口径的差只落在 P1/P2：P0（42=42）与 P3（284=284）完全一致，故 74 条影子全部落在 P1∪P2，据此得 P1 影子 290−219=**71**、P2 影子 98−95=**3**（71+3=74 自洽）。**已知缺陷**：§3.6 未设「优先级」列，上述 71/3 拆分是算术推定而非逐行可查，需脚本补出该列后才能逐条核对。

### P0（42 条）

类型与方法均为 public，用户 IL 可以直接 call，属于**必须处理**的面。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `System.ArgIterator` | `GetRemainingCount` | public/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetRemainingCount is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor:  | `MethodMap.Legacy.cs:232` |
| 2 | `System.Diagnostics.Debugger` | `IsLogging` | public/public/static | 半接 | 半接：dispatch 指向 `il2cpp_debugger_is_logging`，但运行时是空体/恒值桩（桩定义 `debuggerext.cpp:26`） | `debuggerext.cpp:26` |
| 3 | `System.Diagnostics.Debugger` | `get_IsAttached` | public/public/static | 半接 | 半接：dispatch 指向 `il2cpp_debugger_is_attached`，但运行时是空体/恒值桩（桩定义 `debuggerext.cpp:21`） | `debuggerext.cpp:21` |
| 4 | `System.Environment` | `get_HasShutdownStarted` | public/public/static | 半接 | 半接：dispatch 指向 `icall_environment_get_has_shutdown_started`，但运行时是空体/恒值桩（桩定义 `environment.cpp:531`） | `environment.cpp:531` |
| 5 | `System.Runtime.CompilerServices.RuntimeHelpers` | `EnsureSufficientExecutionStack` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.EnsureSufficientExecutionStack is a host stack-probe hint (the AOT runs on the host stack with no | `MethodMap.RuntimeHelpers.cs:81` |
| 6 | `System.Runtime.CompilerServices.RuntimeHelpers` | `ExecuteCodeWithGuaranteedCleanup` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup is a constrained-execution try/finally (TryCode/CleanupCode  | `MethodMap.RuntimeHelpers.cs:89` |
| 7 | `System.Runtime.CompilerServices.RuntimeHelpers` | `InitializeArray` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers.InitializeArray is a StackFold fold (ldtoken<RVA field> + newarr); the call reached the resol | `MethodMap.RuntimeHelpers.cs:108` |
| 8 | `System.Runtime.CompilerServices.RuntimeHelpers` | `PrepareContractedDelegate` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.PrepareContractedDelegate is a constrained-execution JIT-prepare hint (no JIT to prepare) | `MethodMap.RuntimeHelpers.cs:77` |
| 9 | `System.Runtime.CompilerServices.RuntimeHelpers` | `PrepareDelegate` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.PrepareDelegate is a JIT-prepare hint (this AOT has no JIT to prepare the delegate) | `MethodMap.RuntimeHelpers.cs:73` |
| 10 | `System.Runtime.CompilerServices.RuntimeHelpers` | `ProbeForSufficientStack` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.ProbeForSufficientStack is a host stack-probe hint (no managed stack-limit checks) | `MethodMap.RuntimeHelpers.cs:85` |
| 11 | `System.Runtime.InteropServices.ExtensibleClassFactory` | `RegisterObjectCreationCallback` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 12 | `System.Runtime.InteropServices.Marshal` | `AddRef` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): AddRef belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIANT conve | `MethodMap.Marshal.cs:86` |
| 13 | `System.Runtime.InteropServices.Marshal` | `AreComObjectsAvailableForCleanup` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): AreComObjectsAvailableForCleanup belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library | `MethodMap.Marshal.cs:86` |
| 14 | `System.Runtime.InteropServices.Marshal` | `ChangeWrapperHandleStrength` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): ChangeWrapperHandleStrength belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library mars | `MethodMap.Marshal.cs:86` |
| 15 | `System.Runtime.InteropServices.Marshal` | `CleanupUnusedObjectsInCurrentContext` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): CleanupUnusedObjectsInCurrentContext belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-lib | `MethodMap.Marshal.cs:86` |
| 16 | `System.Runtime.InteropServices.Marshal` | `CreateAggregatedObject` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): CreateAggregatedObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshalin | `MethodMap.Marshal.cs:86` |
| 17 | `System.Runtime.InteropServices.Marshal` | `GetEndComSlot` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetEndComSlot belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIAN | `MethodMap.Marshal.cs:86` |
| 18 | `System.Runtime.InteropServices.Marshal` | `GetExceptionCode` | public/public/static | 半接 | 半接：dispatch 指向 `icall_marshal_get_exception_code`，但运行时是空体/恒值桩（桩定义 `marshalext3.cpp:265`） | `marshalext3.cpp:265` |
| 19 | `System.Runtime.InteropServices.Marshal` | `GetExceptionPointers` | public/public/static | 半接 | 半接：dispatch 指向 `icall_marshal_get_exception_pointers`，但运行时是空体/恒值桩（桩定义 `marshalext3.cpp:275`） | `marshalext3.cpp:275` |
| 20 | `System.Runtime.InteropServices.Marshal` | `GetITypeInfoForType` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetITypeInfoForType belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling,  | `MethodMap.Marshal.cs:86` |
| 21 | `System.Runtime.InteropServices.Marshal` | `GetManagedThunkForUnmanagedMethodPtr` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 22 | `System.Runtime.InteropServices.Marshal` | `GetMethodInfoForComSlot` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetMethodInfoForComSlot belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshali | `MethodMap.Marshal.cs:86` |
| 23 | `System.Runtime.InteropServices.Marshal` | `GetStartComSlot` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetStartComSlot belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARI | `MethodMap.Marshal.cs:86` |
| 24 | `System.Runtime.InteropServices.Marshal` | `GetTypeLibLcid` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetTypeLibLcid belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIA | `MethodMap.Marshal.cs:86` |
| 25 | `System.Runtime.InteropServices.Marshal` | `GetTypedObjectForIUnknown` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetTypedObjectForIUnknown belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marsha | `MethodMap.Marshal.cs:86` |
| 26 | `System.Runtime.InteropServices.Marshal` | `GetUniqueObjectForIUnknown` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 27 | `System.Runtime.InteropServices.Marshal` | `GetUnmanagedThunkForManagedMethodPtr` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 28 | `System.Runtime.InteropServices.Marshal` | `IsTypeVisibleFromCom` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): IsTypeVisibleFromCom belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, | `MethodMap.Marshal.cs:86` |
| 29 | `System.Runtime.InteropServices.Marshal` | `QueryInterface` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 30 | `System.Runtime.InteropServices.Marshal` | `Release` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): Release belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIANT conv | `MethodMap.Marshal.cs:86` |
| 31 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetProxiedType` | public/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetProxiedType is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD r | `MethodMap.Legacy.cs:44` |
| 32 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetStubData` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：GetStubData is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reje | `MethodMap.Legacy.cs:44` |
| 33 | `System.Runtime.Remoting.Proxies.RealProxy` | `SetStubData` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：SetStubData is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reje | `MethodMap.Legacy.cs:44` |
| 34 | `System.Runtime.Remoting.RemotingServices` | `GetRealProxy` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：GetRealProxy is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD rej | `MethodMap.Legacy.cs:44` |
| 35 | `System.Runtime.Remoting.RemotingServices` | `IsTransparentProxy` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：IsTransparentProxy is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LO | `MethodMap.Legacy.cs:44` |
| 36 | `System.Threading.Interlocked` | `SpeculationBarrier` | public/public/static | 半接 | 半接：dispatch 指向 `il2cpp_interlocked_speculation_barrier`，但运行时是空体/恒值桩（桩定义 `interlocked.cpp:186`） | `interlocked.cpp:186` |
| 37 | `System.Threading.ReaderWriterLock` | `AnyWritersSince` | public/public/instance | 半接 | 半接：dispatch 指向 `icall_reader_writer_lock_any_writers_since`，但运行时是空体/恒值桩（桩定义 `stubs.cpp:1210`） | `stubs.cpp:1210` |
| 38 | `System.Threading.Thread` | `BeginCriticalRegion` | public/public/static | 半接 | 半接：dispatch 指向 `icall_thread_begin_critical_region`，但运行时是空体/恒值桩（桩定义 `thread.cpp:120`） | `thread.cpp:120` |
| 39 | `System.Threading.Thread` | `BeginThreadAffinity` | public/public/static | 半接 | 半接：dispatch 指向 `icall_thread_begin_thread_affinity`，但运行时是空体/恒值桩（桩定义 `thread.cpp:184`） | `thread.cpp:184` |
| 40 | `System.Threading.Thread` | `DisableComObjectEagerCleanup` | public/public/instance | 半接 | 半接：dispatch 指向 `icall_thread_disable_com_object_eager_cleanup`，但运行时是空体/恒值桩（桩定义 `thread.cpp:199`） | `thread.cpp:199` |
| 41 | `System.Threading.Thread` | `EndCriticalRegion` | public/public/static | 半接 | 半接：dispatch 指向 `icall_thread_end_critical_region`，但运行时是空体/恒值桩（桩定义 `thread.cpp:124`） | `thread.cpp:124` |
| 42 | `System.Threading.Thread` | `EndThreadAffinity` | public/public/static | 半接 | 半接：dispatch 指向 `icall_thread_end_thread_affinity`，但运行时是空体/恒值桩（桩定义 `thread.cpp:188`） | `thread.cpp:188` |

### P1（摘要 290 条 = 本节 219 条缺口 + §3.6 的 71 条影子）

非 public 面，但经普通 BCL 内部路径可达（集合/字符串/LINQ/IO/格式化/数值等），或距离 public 入口只有 1 跳。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `System.AppDomain` | `GetDynamicDir` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetDynamicDir is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT proces | `MethodMap.Legacy.cs:193` |
| 2 | `System.AppDomain` | `nApplyPolicy` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nApplyPolicy is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT process | `MethodMap.Legacy.cs:193` |
| 3 | `System.AppDomain` | `nChangeSecurityPolicy` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nChangeSecurityPolicy is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AO | `MethodMap.Legacy.cs:193` |
| 4 | `System.AppDomain` | `nEnableMonitoring` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nEnableMonitoring is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT pr | `MethodMap.Legacy.cs:193` |
| 5 | `System.AppDomain` | `nGetAssemblies` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetAssemblies is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT proce | `MethodMap.Legacy.cs:193` |
| 6 | `System.AppDomain` | `nGetLastSurvivedMemorySize` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetLastSurvivedMemorySize is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: t | `MethodMap.Legacy.cs:193` |
| 7 | `System.AppDomain` | `nGetLastSurvivedProcessMemorySize` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nGetLastSurvivedProcessMemorySize is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descri | `MethodMap.Legacy.cs:193` |
| 8 | `System.AppDomain` | `nGetTotalAllocatedMemorySize` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetTotalAllocatedMemorySize is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: | `MethodMap.Legacy.cs:193` |
| 9 | `System.AppDomain` | `nGetTotalProcessorTime` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetTotalProcessorTime is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the A | `MethodMap.Legacy.cs:193` |
| 10 | `System.AppDomain` | `nMonitoringIsEnabled` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nMonitoringIsEnabled is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT | `MethodMap.Legacy.cs:193` |
| 11 | `System.AppDomainSetup` | `UpdateContextProperty` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：UpdateContextProperty is part of System.AppDomainSetup -- only the probed ApplicationBase/ConfigurationFile rack-arms are wired in the adm1 closure: L | `MethodMap.Legacy.cs:216` |
| 12 | `System.ArgIterator` | `.ctor` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：.ctor is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __arglist is | `MethodMap.Legacy.cs:232` |
| 13 | `System.ArgIterator` | `.ctor` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：.ctor is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __arglist is | `MethodMap.Legacy.cs:232` |
| 14 | `System.ArgIterator` | `FCallGetNextArg` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：FCallGetNextArg is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __ | `MethodMap.Legacy.cs:232` |
| 15 | `System.ArgIterator` | `InternalGetNextArg` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：InternalGetNextArg is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: | `MethodMap.Legacy.cs:232` |
| 16 | `System.ArgIterator` | `_GetNextArgType` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：_GetNextArgType is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __ | `MethodMap.Legacy.cs:232` |
| 17 | `System.Array` | `InternalCreate` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 18 | `System.BCLDebug` | `GetRegistryLoggingValues` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 19 | `System.Currency` | `FCallToDecimal` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 20 | `System.CurrentSystemTimeZone` | `nativeGetTimeZoneMinuteOffset` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 21 | `System.Decimal` | `FCallCompare` | public/private/static | 部分影子 | 部分影子：由 `System.Decimal::Compare` 直接承接（1 跳） | `MethodMap.Decimal.cs:170` |
| 22 | `System.Decimal` | `FCallRound` | public/private/static | 部分影子 | 部分影子：由 `System.Decimal::Round` 直接承接（1 跳） | `MethodMap.Decimal.cs:290` |
| 23 | `System.Decimal` | `FCallToCurrency` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 24 | `System.Delegate` | `BindToMethodInfo` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 25 | `System.Delegate` | `BindToMethodName` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 26 | `System.Delegate` | `FindMethodHandle` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_delegate_find_method_handle`，但运行时是空体/恒值桩（桩定义 `delegateext.cpp:694`） | `delegateext.cpp:694` |
| 27 | `System.Delegate` | `InternalAlloc` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 28 | `System.Diagnostics.Debugger` | `BreakInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 29 | `System.Diagnostics.Debugger` | `LaunchInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 30 | `System.Enum` | `InternalGetCorElementType` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 31 | `System.Exception` | `CopyDynamicMethods` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 32 | `System.Exception` | `CopyStackTrace` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 33 | `System.Exception` | `GetMethodFromStackTrace` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 34 | `System.Exception` | `GetStackTracesDeepCopy` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 35 | `System.Exception` | `PrepareForForeignExceptionRaise` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 36 | `System.Exception` | `SaveStackTracesFromDeepCopy` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_exception_save_stack_traces_from_deep_copy`，但运行时是空体/恒值桩（桩定义 `stubs.cpp:47`） | `stubs.cpp:47` |
| 37 | `System.Exception` | `StripFileInfo` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 38 | `System.Exception` | `nIsTransient` | public/private/static | 部分影子 | 部分影子：**3 跳**，链路 `System.Exception::get_IsTransient → System.Resources.ResourceManager::SetAppXConfiguration → System.Resources.ResourceManager::.ctor`，根入口 `System.Resources.ResourceManager::.ctor`；不是由 `System.Resources.ResourceManager::.ctor` 直接承接 | `MethodMap.Resource.cs:65` |
| 39 | `System.GC` | `IsServerGC` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_gc_is_server_gc`，但运行时是空体/恒值桩（桩定义 `gcapi.cpp:167`） | `gcapi.cpp:167` |
| 40 | `System.GC` | `_WaitForFullGCApproach` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 41 | `System.GC` | `_WaitForFullGCComplete` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 42 | `System.Globalization.CalendarData` | `nativeGetCalendars` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 43 | `System.Globalization.CalendarData` | `nativeGetTwoDigitYearMax` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 44 | `System.Globalization.CultureData` | `LCIDToLocaleName` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 45 | `System.Globalization.CultureData` | `LocaleNameToLCID` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 46 | `System.Globalization.CultureData` | `nativeGetNumberFormatInfoValues` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 47 | `System.Globalization.CultureData` | `nativeInitCultureData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 48 | `System.Globalization.CultureInfo` | `InternalGetSystemDefaultUILanguage` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 49 | `System.Globalization.TextInfo` | `InternalGetCaseInsHash` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 50 | `System.MarshalByRefObject` | `GetComIUnknown` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 51 | `System.Math` | `SplitFractionDouble` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such overload): Math.SplitFractionDouble -- ModF/SplitFractionDouble is a .NET Core API (net48 has no public ModF member) | `MethodMap.Math.cs:128` |
| 52 | `System.MissingMemberException` | `FormatSignature` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 53 | `System.ModuleHandle` | `GetDynamicMethod` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 54 | `System.ModuleHandle` | `GetMDStreamVersion` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 55 | `System.ModuleHandle` | `_GetMetadataImport` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 56 | `System.Number` | `NumberBufferToDecimal` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 57 | `System.Number` | `NumberBufferToDouble` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 58 | `System.ParseNumbers` | `IntToString` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 59 | `System.ParseNumbers` | `StringToInt` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 60 | `System.ParseNumbers` | `StringToLong` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 61 | `System.Reflection.AssemblyName` | `EscapeCodeBase` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 62 | `System.Reflection.AssemblyName` | `ReferenceMatchesDefinitionInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 63 | `System.Reflection.AssemblyName` | `nGetFileInformation` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 64 | `System.Reflection.AssemblyName` | `nGetPublicKeyToken` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 65 | `System.Reflection.RuntimeAssembly` | `nLoadFile` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 66 | `System.Reflection.RuntimeAssembly` | `nLoadImage` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 67 | `System.Runtime.CompilerServices.RuntimeHelpers` | `_PrepareMethod` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 68 | `System.Runtime.CompilerServices.RuntimeHelpers` | `_RunClassConstructor` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers._RunClassConstructor is a runtime type-init driver; the transpiler drives type-initialization | `MethodMap.RuntimeHelpers.cs:94` |
| 69 | `System.Runtime.CompilerServices.RuntimeHelpers` | `_RunModuleConstructor` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers._RunModuleConstructor is a module-level constructor driver (module ctors are not in the M3 mo | `MethodMap.RuntimeHelpers.cs:99` |
| 70 | `System.Runtime.InteropServices.ArrayWithOffset` | `CalculateCount` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 71 | `System.Runtime.InteropServices.Marshal` | `FCallGenerateGuidForType` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGenerateGuidForType belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshal | `MethodMap.Marshal.cs:86` |
| 72 | `System.Runtime.InteropServices.Marshal` | `FCallGetTypeLibGuid` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGetTypeLibGuid belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling,  | `MethodMap.Marshal.cs:86` |
| 73 | `System.Runtime.InteropServices.Marshal` | `FCallGetTypeLibGuidForAssembly` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGetTypeLibGuidForAssembly belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library m | `MethodMap.Marshal.cs:86` |
| 74 | `System.Runtime.InteropServices.Marshal` | `GetComInterfaceForObjectNative` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetComInterfaceForObjectNative belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library m | `MethodMap.Marshal.cs:86` |
| 75 | `System.Runtime.InteropServices.Marshal` | `GetDelegateForFunctionPointerInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 76 | `System.Runtime.InteropServices.Marshal` | `GetExceptionForHRInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 77 | `System.Runtime.InteropServices.Marshal` | `GetFunctionPointerForDelegateInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 78 | `System.Runtime.InteropServices.Marshal` | `GetIDispatchForObjectNative` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetIDispatchForObjectNative belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library mars | `MethodMap.Marshal.cs:86` |
| 79 | `System.Runtime.InteropServices.Marshal` | `GetIUnknownForObjectNative` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetIUnknownForObjectNative belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marsh | `MethodMap.Marshal.cs:86` |
| 80 | `System.Runtime.InteropServices.Marshal` | `GetLoadedTypeForGUID` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetLoadedTypeForGUID belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, | `MethodMap.Marshal.cs:86` |
| 81 | `System.Runtime.InteropServices.Marshal` | `GetNativeActivationFactory` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 82 | `System.Runtime.InteropServices.Marshal` | `InternalCreateWrapperOfType` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalCreateWrapperOfType belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library mars | `MethodMap.Marshal.cs:86` |
| 83 | `System.Runtime.InteropServices.Marshal` | `InternalGetComSlotForMethodInfo` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalGetComSlotForMethodInfo belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library  | `MethodMap.Marshal.cs:86` |
| 84 | `System.Runtime.InteropServices.Marshal` | `InternalGetThreadFromFiberCookie` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 85 | `System.Runtime.InteropServices.Marshal` | `InternalSwitchCCW` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalSwitchCCW belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VA | `MethodMap.Marshal.cs:86` |
| 86 | `System.Runtime.InteropServices.Marshal` | `InternalWrapIUnknownWithComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalWrapIUnknownWithComObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-librar | `MethodMap.Marshal.cs:86` |
| 87 | `System.Runtime.InteropServices.Marshal` | `OffsetOfHelper` | public/private/static | 部分影子 | 部分影子：由 `System.Runtime.InteropServices.Marshal::OffsetOf` 直接承接（1 跳） | `MethodMap.Marshal.cs:293` |
| 88 | `System.Runtime.InteropServices.Marshal` | `PtrToStructureHelper` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 89 | `System.Runtime.InteropServices.Marshal` | `SizeOfHelper` | public/internal/static | 部分影子 | 部分影子：由 `System.Runtime.InteropServices.Marshal::SizeOf` 直接承接（1 跳） | `MethodMap.Marshal.cs:241` |
| 90 | `System.Runtime.InteropServices.Marshal` | `SizeOfType` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 91 | `System.Runtime.InteropServices.Marshal` | `ThrowExceptionForHRInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 92 | `System.Runtime.InteropServices.Marshal` | `_GetTypeLibVersionForAssembly` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): _GetTypeLibVersionForAssembly belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library ma | `MethodMap.Marshal.cs:86` |
| 93 | `System.Runtime.InteropServices.RegistrationServices` | `RegisterTypeForComClientsExNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 94 | `System.Runtime.InteropServices.RegistrationServices` | `RegisterTypeForComClientsNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 95 | `System.Runtime.InteropServices.RuntimeEnvironment` | `GetRuntimeDirectoryImpl` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 96 | `System.Runtime.InteropServices.TypeLibConverter` | `nConvertAssemblyToTypeLib` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 97 | `System.Runtime.InteropServices.TypeLibConverter` | `nConvertTypeLibToMetadata` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 98 | `System.Runtime.Remoting.Messaging.Message` | `OutToUnmanagedDebugger` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：OutToUnmanagedDebugger is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 99 | `System.Runtime.Remoting.RemotingServices` | `CallDefaultCtor` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CallDefaultCtor is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD  | `MethodMap.Legacy.cs:44` |
| 100 | `System.Runtime.Serialization.FormatterServices` | `nativeGetSafeUninitializedObject` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 101 | `System.Runtime.Serialization.FormatterServices` | `nativeGetUninitializedObject` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 102 | `System.Runtime.Versioning.CompatibilitySwitch` | `GetValueInternalCall` | public/private/static | 半接 | 半接：dispatch 指向 `icall_compatibility_switch_get_value`，但运行时是空体/恒值桩（桩定义 `appcontext.cpp:195`） | `appcontext.cpp:195` |
| 103 | `System.Runtime.Versioning.CompatibilitySwitch` | `IsEnabledInternalCall` | public/private/static | 半接 | 半接：dispatch 指向 `icall_compatibility_switch_is_enabled`，但运行时是空体/恒值桩（桩定义 `appcontext.cpp:201`） | `appcontext.cpp:201` |
| 104 | `System.Runtime.Versioning.VersioningHelper` | `GetRuntimeId` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 105 | `System.RuntimeFieldHandle` | `AcquiresContextFromThis` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::AcquiresContextFromThis not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 106 | `System.RuntimeFieldHandle` | `GetApproxDeclaringType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetApproxDeclaringType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 107 | `System.RuntimeFieldHandle` | `GetValue` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetValue not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 108 | `System.RuntimeFieldHandle` | `SetValue` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::SetValue not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 109 | `System.RuntimeMethodHandle` | `CheckLinktimeDemands` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::CheckLinktimeDemands not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on | `MethodMap.ReflectHandle.cs:55` |
| 110 | `System.RuntimeMethodHandle` | `GetAttributes` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetAttributes not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 111 | `System.RuntimeMethodHandle` | `GetDeclaringType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetDeclaringType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the | `MethodMap.ReflectHandle.cs:55` |
| 112 | `System.RuntimeMethodHandle` | `GetLoaderAllocator` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetLoaderAllocator not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on t | `MethodMap.ReflectHandle.cs:55` |
| 113 | `System.RuntimeMethodHandle` | `GetMethodFromCanonical` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetMethodFromCanonical not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 114 | `System.RuntimeMethodHandle` | `GetName` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetName not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 115 | `System.RuntimeMethodHandle` | `GetResolver` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetResolver not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type | `MethodMap.ReflectHandle.cs:55` |
| 116 | `System.RuntimeMethodHandle` | `GetStubIfNeeded` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetStubIfNeeded not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 117 | `System.RuntimeMethodHandle` | `HasMethodInstantiation` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::HasMethodInstantiation not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 118 | `System.RuntimeMethodHandle` | `InvokeMethod` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::InvokeMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 119 | `System.RuntimeMethodHandle` | `IsConstructor` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsConstructor not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 120 | `System.RuntimeMethodHandle` | `IsDynamicMethod` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsDynamicMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 121 | `System.RuntimeMethodHandle` | `IsGenericMethodDefinition` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsGenericMethodDefinition not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeTy | `MethodMap.ReflectHandle.cs:55` |
| 122 | `System.RuntimeMethodHandle` | `PerformSecurityCheck` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::PerformSecurityCheck not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on | `MethodMap.ReflectHandle.cs:55` |
| 123 | `System.RuntimeMethodHandle` | `_GetCurrentMethod` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::_GetCurrentMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on th | `MethodMap.ReflectHandle.cs:55` |
| 124 | `System.RuntimeType` | `AllocateValueType` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 125 | `System.RuntimeType` | `CanValueSpecialCast` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 126 | `System.RuntimeType` | `GetTypeFromCLSIDImpl` | internal/internal/static | 部分影子 | 部分影子：由 `System.Type::GetTypeFromCLSID` 直接承接（1 跳） | `MethodMap.Type.cs:787` |
| 127 | `System.RuntimeType` | `GetTypeFromProgIDImpl` | internal/internal/static | 部分影子 | 部分影子：由 `System.Type::GetTypeFromProgID` 直接承接（1 跳） | `MethodMap.Type.cs:782` |
| 128 | `System.RuntimeTypeHandle` | `Allocate` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::Allocate not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type hand | `MethodMap.ReflectHandle.cs:55` |
| 129 | `System.RuntimeTypeHandle` | `CompareCanonicalHandles` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CompareCanonicalHandles not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 130 | `System.RuntimeTypeHandle` | `CreateCaInstance` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CreateCaInstance not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 131 | `System.RuntimeTypeHandle` | `CreateInstance` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CreateInstance not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 132 | `System.RuntimeTypeHandle` | `GetAssembly` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetAssembly not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 133 | `System.RuntimeTypeHandle` | `GetBaseType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetBaseType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 134 | `System.RuntimeTypeHandle` | `GetCorElementType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetCorElementType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 135 | `System.RuntimeTypeHandle` | `GetElementType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetElementType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 136 | `System.RuntimeTypeHandle` | `GetModule` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetModule not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 137 | `System.RuntimeTypeHandle` | `HasInstantiation` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::HasInstantiation not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 138 | `System.RuntimeTypeHandle` | `IsComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsComObject not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 139 | `System.RuntimeTypeHandle` | `IsGenericTypeDefinition` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsGenericTypeDefinition not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 140 | `System.RuntimeTypeHandle` | `IsGenericVariable` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsGenericVariable not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 141 | `System.RuntimeTypeHandle` | `IsValueType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsValueType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 142 | `System.Security.CodeAccessSecurityEngine` | `QuickCheckForAllDemands` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 143 | `System.Security.Cryptography.Utils` | `_DecryptData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 144 | `System.Security.Cryptography.Utils` | `_EncryptData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 145 | `System.Security.Cryptography.Utils` | `_ExportKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 146 | `System.Security.Cryptography.Utils` | `_GetEnforceFipsPolicySetting` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 147 | `System.Security.Cryptography.Utils` | `_GetKeyParameter` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 148 | `System.Security.Cryptography.Utils` | `_GetProviderParameter` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 149 | `System.Security.Cryptography.Utils` | `_ImportKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 150 | `System.Security.Cryptography.Utils` | `_OpenCSP` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 151 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetIssuerName` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 152 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetPublicKeyOid` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 153 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetPublicKeyParameters` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 154 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetPublicKeyValue` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 155 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetSerialNumber` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 156 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetSubjectInfo` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 157 | `System.Signature` | `GetSignature` | internal/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 158 | `System.Threading.CompressedStack` | `DestroyDCSList` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 159 | `System.Threading.CompressedStack` | `GetDCSCount` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 160 | `System.Threading.CompressedStack` | `GetDelayedCompressedStack` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 161 | `System.Threading.CompressedStack` | `GetDomainCompressedStack` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 162 | `System.Threading.CompressedStack` | `GetHomogeneousPLS` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 163 | `System.Threading.CompressedStack` | `IsImmediateCompletionCandidate` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 164 | `System.Threading.HostExecutionContextManager` | `CaptureHostSecurityContext` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 165 | `System.Threading.HostExecutionContextManager` | `HostSecurityManagerPresent` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 166 | `System.Threading.HostExecutionContextManager` | `SetHostSecurityContext` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 167 | `System.Threading.Interlocked` | `_CompareExchange` | public/private/static | 部分影子 | 部分影子：由 `System.Threading.Interlocked::CompareExchange` 直接承接（1 跳） | `MethodMap.Interlocked.cs:101` |
| 168 | `System.Threading.Interlocked` | `_Exchange` | public/private/static | 部分影子 | 部分影子：由 `System.Threading.Interlocked::Exchange` 直接承接（1 跳） | `MethodMap.Interlocked.cs:63` |
| 169 | `System.Threading.Monitor` | `IsEnteredNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 170 | `System.Threading.Monitor` | `ObjWait` | public/private/static | 部分影子 | 部分影子：由 `System.Threading.Monitor::Wait` 直接承接（1 跳） | `MethodMap.Monitor.cs:98` |
| 171 | `System.Threading.Monitor` | `ReliableEnterTimeout` | public/private/static | 部分影子 | 部分影子：由 `System.Threading.Monitor::TryEnter` 直接承接（1 跳） | `MethodMap.Monitor.cs:63` |
| 172 | `System.Threading.OverlappedData` | `AllocateNativeOverlapped` | internal/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 173 | `System.Threading.OverlappedData` | `FreeNativeOverlapped` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 174 | `System.Threading.OverlappedData` | `GetOverlappedFromNative` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 175 | `System.Threading.ReaderWriterLock` | `AcquireReaderLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：AcquireReaderLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 176 | `System.Threading.ReaderWriterLock` | `AcquireWriterLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：AcquireWriterLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 177 | `System.Threading.ReaderWriterLock` | `DowngradeFromWriterLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：DowngradeFromWriterLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (r | `MethodMap.Legacy.cs:242` |
| 178 | `System.Threading.ReaderWriterLock` | `FCallReleaseLock` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：FCallReleaseLock is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descript | `MethodMap.Legacy.cs:242` |
| 179 | `System.Threading.ReaderWriterLock` | `FCallUpgradeToWriterLock` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：FCallUpgradeToWriterLock is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime  | `MethodMap.Legacy.cs:242` |
| 180 | `System.Threading.ReaderWriterLock` | `PrivateGetIsReaderLockHeld` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateGetIsReaderLockHeld is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtim | `MethodMap.Legacy.cs:242` |
| 181 | `System.Threading.ReaderWriterLock` | `PrivateGetIsWriterLockHeld` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateGetIsWriterLockHeld is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtim | `MethodMap.Legacy.cs:242` |
| 182 | `System.Threading.ReaderWriterLock` | `PrivateGetWriterSeqNum` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateGetWriterSeqNum is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime de | `MethodMap.Legacy.cs:242` |
| 183 | `System.Threading.ReaderWriterLock` | `PrivateInitialize` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateInitialize is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descrip | `MethodMap.Legacy.cs:242` |
| 184 | `System.Threading.ReaderWriterLock` | `ReleaseReaderLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：ReleaseReaderLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 185 | `System.Threading.ReaderWriterLock` | `ReleaseWriterLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：ReleaseWriterLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 186 | `System.Threading.ReaderWriterLock` | `RestoreLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：RestoreLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descr | `MethodMap.Legacy.cs:242` |
| 187 | `System.Threading.RegisteredWaitHandleSafe` | `UnregisterWaitNative` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 188 | `System.Threading.Thread` | `AbortInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 189 | `System.Threading.Thread` | `ClearAbortReason` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_clear_abort_reason`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:735`） | `threadobj.cpp:735` |
| 190 | `System.Threading.Thread` | `GetAbortReason` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_get_abort_reason`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:732`） | `threadobj.cpp:732` |
| 191 | `System.Threading.Thread` | `GetApartmentStateNative` | public/private/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_get_apartment_state_native`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:752`） | `threadobj.cpp:752` |
| 192 | `System.Threading.Thread` | `GetContextInternal` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_threadobj_get_context_internal`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:714`） | `threadobj.cpp:714` |
| 193 | `System.Threading.Thread` | `GetDomainInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 194 | `System.Threading.Thread` | `GetFastDomainInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 195 | `System.Threading.Thread` | `InternalCrossContextCallback` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_internal_cross_context_callback`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:720`） | `threadobj.cpp:720` |
| 196 | `System.Threading.Thread` | `InterruptInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 197 | `System.Threading.Thread` | `JoinInternal` | public/private/instance | 部分影子 | 部分影子：由 `System.Threading.Thread::Join` 直接承接（1 跳） | `MethodMap.Thread.cs:169` |
| 198 | `System.Threading.Thread` | `ResetAbortNative` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 199 | `System.Threading.Thread` | `ResumeInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 200 | `System.Threading.Thread` | `SetAbortReason` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_set_abort_reason`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:723`） | `threadobj.cpp:723` |
| 201 | `System.Threading.Thread` | `SetApartmentStateNative` | public/private/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_set_apartment_state_native`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:758`） | `threadobj.cpp:758` |
| 202 | `System.Threading.Thread` | `SuspendInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 203 | `System.Threading.Thread` | `nativeGetSafeCulture` | public/private/static | 部分影子 | 部分影子：**2 跳**，链路 `System.Threading.Thread::GetCurrentUICultureNoAppX → System.Resources.ResourceManager::GetString`，根入口 `System.Resources.ResourceManager::GetString`；不是由 `System.Resources.ResourceManager::GetString` 直接承接 | `MethodMap.Resources.cs:110` |
| 204 | `System.Threading.Thread` | `nativeSetThreadUILocale` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 205 | `System.Threading.ThreadPool` | `BindIOCompletionCallbackNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 206 | `System.Threading.ThreadPool` | `PostQueuedCompletionStatus` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 207 | `System.Threading.ThreadPool` | `RegisterWaitForSingleObjectNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 208 | `System.Threading.ThreadPool` | `SetMaxThreadsNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 209 | `System.Threading.ThreadPool` | `SetMinThreadsNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 210 | `System.Threading.WaitHandle` | `SignalAndWaitOne` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 211 | `System.Threading.WaitHandle` | `WaitMultiple` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 212 | `System.Threading.WaitHandle` | `WaitOneNative` | public/private/static | 部分影子 | 部分影子：**2 跳**，链路 `System.Threading.WaitHandle::WaitOneWithoutFAS → System.Threading.Thread::Sleep`，根入口 `System.Threading.Thread::Sleep`；不是由 `System.Threading.Thread::Sleep` 直接承接 | `MethodMap.Thread.cs:47` |
| 213 | `System.Type` | `GetTypeFromHandleUnsafe` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_type_get_type_from_handle_unsafe`，但运行时是空体/恒值桩（桩定义 `stubs.cpp:709`） | `stubs.cpp:709` |
| 214 | `System.TypedReference` | `InternalMakeTypedReference` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 215 | `System.TypedReference` | `InternalSetTypedReference` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 216 | `System.TypedReference` | `InternalToObject` | public/internal/static | 部分影子 | 部分影子：由 `System.Array::GetValue` 直接承接（1 跳） | `MethodMap.Array.cs:1129` |
| 217 | `System.ValueType` | `CanCompareBits` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 218 | `System.ValueType` | `FastEqualsCheck` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 219 | `System.WeakReference` | `IsTrackResurrection` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |

### P2（摘要 98 条 = 本节 95 条缺口 + §3.6 的 3 条影子）

仅经反射/序列化/调试器/COM 宿主/interop 路径可达，或链路深度 ≥4。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameGetPublicKey` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 2 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameFreeBuffer` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 3 | `System.AppDomain` | `_nExecuteAssembly` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：_nExecuteAssembly is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT pr | `MethodMap.Legacy.cs:193` |
| 4 | `System.AppDomain` | `nIsDefaultAppDomainForEvidence` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nIsDefaultAppDomainForEvidence is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descripto | `MethodMap.Legacy.cs:193` |
| 5 | `System.Diagnostics.Debugger` | `CustomNotification` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 6 | `System.Diagnostics.StackTrace` | `GetStackFramesInternal` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_stack_trace_get_stack_frames_internal`，但运行时是空体/恒值桩（桩定义 `stubs.cpp:757`） | `stubs.cpp:757` |
| 7 | `System.Globalization.CalendarData` | `nativeGetCalendarData` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 8 | `System.Globalization.CultureData` | `nativeEnumTimeFormats` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 9 | `System.Reflection.AssemblyName` | `nInit` | public/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 10 | `System.Reflection.CustomAttribute` | `_CreateCaObject` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 11 | `System.Reflection.CustomAttribute` | `_GetPropertyOrFieldData` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 12 | `System.Reflection.CustomAttribute` | `_ParseAttributeUsageAttribute` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 13 | `System.Reflection.Emit.AssemblyBuilder` | `GetInMemoryAssemblyModule` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 14 | `System.Reflection.Emit.AssemblyBuilder` | `GetOnDiskAssemblyModule` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 15 | `System.Reflection.Emit.AssemblyBuilder` | `nCreateDynamicAssembly` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 16 | `System.Reflection.Emit.ModuleBuilder` | `nCreateISymWriterForDynamicModule` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 17 | `System.Reflection.MetadataImport` | `_Enum` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 18 | `System.Reflection.MetadataImport` | `_GetCustomAttributeProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 19 | `System.Reflection.MetadataImport` | `_GetMemberRefProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 20 | `System.Reflection.MetadataImport` | `_GetParentToken` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 21 | `System.Reflection.MetadataImport` | `_GetPropertyProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 22 | `System.Reflection.MetadataImport` | `_GetSigOfMethodDef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 23 | `System.Reflection.MetadataImport` | `_IsValidToken` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 24 | `System.Reflection.PseudoCustomAttribute` | `_GetSecurityAttributes` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 25 | `System.Reflection.RuntimeAssembly` | `AptcaCheck` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 26 | `System.Reflection.RuntimeAssembly` | `IsFrameworkAssembly` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 27 | `System.Reflection.RuntimeAssembly` | `_nLoad` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 28 | `System.Reflection.SecurityContextFrame` | `Pop` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 29 | `System.Reflection.SecurityContextFrame` | `Push` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 30 | `System.Runtime.InteropServices.CriticalHandle` | `FireCustomerDebugProbe` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 31 | `System.Runtime.InteropServices.GCHandle` | `InternalAlloc` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalAlloc -- the runtime surface covers Alloc/Free/Target/Addr | `MethodMap.GCHandle.cs:101` |
| 32 | `System.Runtime.InteropServices.GCHandle` | `InternalCheckDomain` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalCheckDomain -- the runtime surface covers Alloc/Free/Targe | `MethodMap.GCHandle.cs:101` |
| 33 | `System.Runtime.InteropServices.GCHandle` | `InternalCompareExchange` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalCompareExchange -- the runtime surface covers Alloc/Free/T | `MethodMap.GCHandle.cs:101` |
| 34 | `System.Runtime.InteropServices.Marshal` | `FCallGetTypeInfoGuid` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGetTypeInfoGuid belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, | `MethodMap.Marshal.cs:86` |
| 35 | `System.Runtime.InteropServices.Marshal` | `GetHRForException_WinRT` | public/internal/static | 部分影子 | 部分影子：**3 跳**，链路 `System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal::ReportUnhandledError → System.Runtime.CompilerServices.AsyncMethodBuilderCore::ThrowAsync → System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1::AwaitOnCompleted`，根入口 `System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1::AwaitUnsafeOnCompleted`；不是由 `System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1::AwaitUnsafeOnCompleted` 直接承接 | `MethodMap.Task.cs:222` |
| 36 | `System.Runtime.InteropServices.Marshal` | `GetRawIUnknownForComObjectNoAddRef` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetRawIUnknownForComObjectNoAddRef belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-libra | `MethodMap.Marshal.cs:86` |
| 37 | `System.Runtime.InteropServices.Marshal` | `GetTypeLibVersion` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetTypeLibVersion belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VA | `MethodMap.Marshal.cs:86` |
| 38 | `System.Runtime.InteropServices.Marshal` | `InitializeManagedWinRTFactoryObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InitializeManagedWinRTFactoryObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-libr | `MethodMap.Marshal.cs:86` |
| 39 | `System.Runtime.InteropServices.Marshal` | `InternalFinalReleaseComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalFinalReleaseComObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library ma | `MethodMap.Marshal.cs:86` |
| 40 | `System.Runtime.InteropServices.Marshal` | `InternalReleaseComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalReleaseComObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshal | `MethodMap.Marshal.cs:86` |
| 41 | `System.Runtime.InteropServices.SafeHandle` | `InternalDispose` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 42 | `System.Runtime.InteropServices.SafeHandle` | `InternalFinalize` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 43 | `System.Runtime.Remoting.Contexts.Context` | `SetupInternalContext` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：SetupInternalContext is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested  | `MethodMap.Legacy.cs:44` |
| 44 | `System.Runtime.Remoting.Messaging.Message` | `Dispatch` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：Dispatch is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject' | `MethodMap.Legacy.cs:44` |
| 45 | `System.Runtime.Remoting.Messaging.Message` | `GetThisPtr` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetThisPtr is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD rejec | `MethodMap.Legacy.cs:44` |
| 46 | `System.Runtime.Remoting.Messaging.StackBuilderSink` | `_PrivateProcessMessage` | internal/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：_PrivateProcessMessage is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 47 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetStub` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetStub is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject') | `MethodMap.Legacy.cs:44` |
| 48 | `System.Runtime.Remoting.RemotingServices` | `AllocateUninitializedObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：AllocateUninitializedObject is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'sug | `MethodMap.Legacy.cs:44` |
| 49 | `System.Runtime.Remoting.RemotingServices` | `AlwaysUnwrap` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：AlwaysUnwrap is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD rej | `MethodMap.Legacy.cs:44` |
| 50 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerRemotingServerReceivingMessage` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerRemotingServerReceivingMessage is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-f | `MethodMap.Legacy.cs:44` |
| 51 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerRemotingServerSendingReply` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerRemotingServerSendingReply is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face  | `MethodMap.Legacy.cs:44` |
| 52 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerTrackRemoting` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerTrackRemoting is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'sugges | `MethodMap.Legacy.cs:44` |
| 53 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerTrackRemotingAsync` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerTrackRemotingAsync is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 's | `MethodMap.Legacy.cs:44` |
| 54 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerTrackRemotingCookie` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerTrackRemotingCookie is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, ' | `MethodMap.Legacy.cs:44` |
| 55 | `System.Runtime.Remoting.RemotingServices` | `CreateTransparentProxy` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CreateTransparentProxy is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 56 | `System.Runtime.Remoting.RemotingServices` | `nSetRemoteActivationConfigured` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nSetRemoteActivationConfigured is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, ' | `MethodMap.Legacy.cs:44` |
| 57 | `System.Runtime.Serialization.FormatterServices` | `GetEnableUnsafeTypeForwarders` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 58 | `System.Runtime.Versioning.CompatibilitySwitch` | `GetAppContextOverridesInternalCall` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_compatibility_switch_get_app_context_overrides`，但运行时是空体/恒值桩（桩定义 `appcontext.cpp:189`） | `appcontext.cpp:189` |
| 59 | `System.RuntimeMethodHandle` | `GetSlot` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetSlot not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 60 | `System.RuntimeMethodHandle` | `GetSpecialSecurityFlags` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetSpecialSecurityFlags not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType | `MethodMap.ReflectHandle.cs:55` |
| 61 | `System.RuntimeMethodHandle` | `IsTypicalMethodDefinition` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsTypicalMethodDefinition not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeTy | `MethodMap.ReflectHandle.cs:55` |
| 62 | `System.RuntimeMethodHandle` | `SerializationInvoke` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::SerializationInvoke not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on  | `MethodMap.ReflectHandle.cs:55` |
| 63 | `System.RuntimeTypeHandle` | `GetMethodAt` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetMethodAt not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 64 | `System.RuntimeTypeHandle` | `GetNumVirtuals` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetNumVirtuals not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 65 | `System.RuntimeTypeHandle` | `IsEquivalentType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsEquivalentType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 66 | `System.Security.CodeAccessSecurityEngine` | `AllDomainsHomogeneousWithNoStackModifiers` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 67 | `System.Security.CodeAccessSecurityEngine` | `Check` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 68 | `System.Security.CodeAccessSecurityEngine` | `CheckNReturnSO` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 69 | `System.Security.CodeAccessSecurityEngine` | `GetZoneAndOriginInternal` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 70 | `System.Security.CodeAccessSecurityEngine` | `SpecialDemand` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 71 | `System.Security.Cryptography.Utils` | `_AcquireCSP` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 72 | `System.Security.Cryptography.Utils` | `_CreateCSP` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 73 | `System.Security.Cryptography.Utils` | `_GenerateKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 74 | `System.Security.Cryptography.Utils` | `_GetKeySetSecurityInfo` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 75 | `System.Security.Cryptography.Utils` | `_GetUserKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 76 | `System.Security.Cryptography.Utils` | `_ImportBulkKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 77 | `System.Security.Cryptography.Utils` | `_ImportCspBlob` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 78 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_AddCertificateToStore` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 79 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_DuplicateCertContext` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 80 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_ExportCertificatesToBlob` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 81 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetCertRawData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 82 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetDateNotAfter` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 83 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetDateNotBefore` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 84 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetThumbprint` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 85 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_LoadCertFromBlob` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 86 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_LoadCertFromFile` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 87 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_OpenX509Store` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 88 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_QueryCertBlobType` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 89 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_QueryCertFileType` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 90 | `System.Security.FrameSecurityDescriptor` | `DecrementAssertCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 91 | `System.Security.FrameSecurityDescriptor` | `DecrementOverridesCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 92 | `System.Security.FrameSecurityDescriptor` | `IncrementAssertCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 93 | `System.Security.FrameSecurityDescriptor` | `IncrementOverridesCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 94 | `System.Threading.Thread` | `RestoreAppDomainStack` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_restore_app_domain_stack`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:700`） | `threadobj.cpp:700` |
| 95 | `System.Threading.Thread` | `SetAppDomainStack` | public/internal/instance | 半接 | 半接：dispatch 指向 `icall_threadobj_set_app_domain_stack`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:694`） | `threadobj.cpp:694` |

### P3（284 条）

mscorlib 内部没有任何 public 入口可达（legacy / CLR 内部）。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `Microsoft.Runtime.Hosting.IClrStrongName` | `GetHashFromAssemblyFile` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 2 | `Microsoft.Runtime.Hosting.IClrStrongName` | `GetHashFromAssemblyFileW` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 3 | `Microsoft.Runtime.Hosting.IClrStrongName` | `GetHashFromBlob` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 4 | `Microsoft.Runtime.Hosting.IClrStrongName` | `GetHashFromFile` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 5 | `Microsoft.Runtime.Hosting.IClrStrongName` | `GetHashFromFileW` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 6 | `Microsoft.Runtime.Hosting.IClrStrongName` | `GetHashFromHandle` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 7 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameCompareAssemblies` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 8 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameFreeBuffer` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 9 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameGetBlob` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 10 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameGetBlobFromImage` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 11 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameHashSize` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 12 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameKeyDelete` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 13 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameKeyGen` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 14 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameKeyGenEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 15 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameKeyInstall` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 16 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameSignatureGeneration` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 17 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameSignatureGenerationEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 18 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameSignatureSize` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 19 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameSignatureVerification` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 20 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameSignatureVerificationEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 21 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameSignatureVerificationFromImage` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 22 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameTokenFromAssembly` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 23 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameTokenFromAssemblyEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 24 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameTokenFromPublicKey` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 25 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `GetHashFromAssemblyFile` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 26 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `GetHashFromAssemblyFileW` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 27 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `GetHashFromBlob` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 28 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `GetHashFromFile` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 29 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `GetHashFromFileW` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 30 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `GetHashFromHandle` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 31 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameCompareAssemblies` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 32 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameGetBlob` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 33 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameGetBlobFromImage` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 34 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameGetPublicKey` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 35 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameHashSize` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 36 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameKeyDelete` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 37 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameKeyGen` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 38 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameKeyGenEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 39 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameKeyInstall` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 40 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameSignatureGeneration` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 41 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameSignatureGenerationEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 42 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameSignatureSize` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 43 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameSignatureVerification` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 44 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameSignatureVerificationEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 45 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameSignatureVerificationFromImage` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 46 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameTokenFromAssembly` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 47 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameTokenFromAssemblyEx` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 48 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameTokenFromPublicKey` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 49 | `Microsoft.Win32.OAVariantLib` | `ChangeTypeEx` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 50 | `System.AppDomain` | `nSetDisableInterfaceCache` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nSetDisableInterfaceCache is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: th | `MethodMap.Legacy.cs:193` |
| 51 | `System.AppDomain` | `nSetHostSecurityManagerFlags` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nSetHostSecurityManagerFlags is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: | `MethodMap.Legacy.cs:193` |
| 52 | `System.AppDomain` | `nSetupFriendlyName` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nSetupFriendlyName is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT p | `MethodMap.Legacy.cs:193` |
| 53 | `System.AppDomainManager` | `HasHost` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 54 | `System.CurrentSystemTimeZone` | `nativeGetDaylightChanges` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 55 | `System.CurrentSystemTimeZone` | `nativeGetDaylightName` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 56 | `System.CurrentSystemTimeZone` | `nativeGetStandardName` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 57 | `System.DefaultBinder` | `CanConvertPrimitive` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 58 | `System.DefaultBinder` | `CanConvertPrimitiveObjectToType` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 59 | `System.Delegate` | `DelegateConstruct` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 60 | `System.Globalization.EncodingTable` | `GetCodePageData` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 61 | `System.Globalization.EncodingTable` | `GetEncodingData` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 62 | `System.Globalization.EncodingTable` | `GetNumEncodingItems` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 63 | `System.Globalization.EncodingTable` | `nativeCreateOpenFileMapping` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 64 | `System.IO.__ConsoleStream` | `WaitForAvailableConsoleInput` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 65 | `System.Mda` | `IsInvalidGCHandleCookieProbeEnabled` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 66 | `System.Mda` | `MemberInfoCacheCreation` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 67 | `System.Mda` | `ReportErrorSafeHandleRelease` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 68 | `System.Mda` | `ReportStreamWriterBufferedDataLost` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 69 | `System.ModuleHandle` | `GetToken` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 70 | `System.Number` | `FormatNumberBuffer` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 71 | `System.Reflection.CustomAttributeEncodedArgument` | `ParseAttributeArguments` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 72 | `System.Reflection.MetadataImport` | `_GetClassLayout` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 73 | `System.Reflection.MetadataImport` | `_GetDefaultValue` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 74 | `System.Reflection.MetadataImport` | `_GetEventProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 75 | `System.Reflection.MetadataImport` | `_GetFieldDefProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 76 | `System.Reflection.MetadataImport` | `_GetFieldMarshal` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 77 | `System.Reflection.MetadataImport` | `_GetFieldOffset` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 78 | `System.Reflection.MetadataImport` | `_GetGenericParamProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 79 | `System.Reflection.MetadataImport` | `_GetMarshalAs` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 80 | `System.Reflection.MetadataImport` | `_GetName` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 81 | `System.Reflection.MetadataImport` | `_GetNamespace` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 82 | `System.Reflection.MetadataImport` | `_GetPInvokeMap` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 83 | `System.Reflection.MetadataImport` | `_GetParamDefProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 84 | `System.Reflection.MetadataImport` | `_GetScopeProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 85 | `System.Reflection.MetadataImport` | `_GetSigOfFieldDef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 86 | `System.Reflection.MetadataImport` | `_GetSignatureFromToken` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 87 | `System.Reflection.MetadataImport` | `_GetUserString` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 88 | `System.Reflection.RtFieldInfo` | `PerformVisibilityCheckOnField` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 89 | `System.Reflection.RuntimeAssembly` | `FCallIsDynamic` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 90 | `System.Reflection.RuntimeAssembly` | `GetManifestModule` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 91 | `System.Reflection.RuntimeAssembly` | `GetManifestResourceNames` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 92 | `System.Reflection.RuntimeAssembly` | `GetReferencedAssemblies` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 93 | `System.Reflection.RuntimeAssembly` | `GetToken` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 94 | `System.Reflection.RuntimeAssembly` | `IsGlobalAssemblyCache` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 95 | `System.Reflection.RuntimeAssembly` | `IsNewPortableAssembly` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 96 | `System.Reflection.RuntimeAssembly` | `IsReflectionOnly` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 97 | `System.Reflection.RuntimeModule` | `GetTypes` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 98 | `System.Reflection.RuntimeModule` | `IsResource` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 99 | `System.Runtime.CompilerServices.JitHelpers` | `UnsafeSetArrayElement` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 100 | `System.Runtime.InteropServices.Marshal` | `AlignedSizeOfType` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 101 | `System.Runtime.InteropServices.Marshal` | `GetSystemMaxDBCSCharSize` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 102 | `System.Runtime.InteropServices.Marshal` | `InitializeWrapperForWinRT` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InitializeWrapperForWinRT belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marsha | `MethodMap.Marshal.cs:86` |
| 103 | `System.Runtime.InteropServices.RuntimeEnvironment` | `GetDeveloperPath` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 104 | `System.Runtime.InteropServices.RuntimeEnvironment` | `GetHostBindingFile` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 105 | `System.Runtime.InteropServices.RuntimeEnvironment` | `GetModuleFileName` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 106 | `System.Runtime.InteropServices.SafeBuffer` | `PtrToStructureNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 107 | `System.Runtime.InteropServices.SafeBuffer` | `StructureToPtrNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 108 | `System.Runtime.InteropServices.WindowsRuntime.RuntimeClass` | `GetRedirectedEqualsMD` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 109 | `System.Runtime.InteropServices.WindowsRuntime.RuntimeClass` | `GetRedirectedGetHashCodeMD` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 110 | `System.Runtime.InteropServices.WindowsRuntime.RuntimeClass` | `GetRedirectedToStringMD` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 111 | `System.Runtime.InteropServices.WindowsRuntime.RuntimeClass` | `RedirectEquals` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 112 | `System.Runtime.InteropServices.WindowsRuntime.RuntimeClass` | `RedirectGetHashCode` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 113 | `System.Runtime.InteropServices.WindowsRuntime.RuntimeClass` | `RedirectToString` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 114 | `System.Runtime.MemoryFailPoint` | `GetMemorySettings` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 115 | `System.Runtime.Remoting.Channels.ChannelServices` | `GetPrivateContextsPerfCounters` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：GetPrivateContextsPerfCounters is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, ' | `MethodMap.Legacy.cs:44` |
| 116 | `System.Runtime.Remoting.Contexts.Context` | `CleanupInternalContext` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：CleanupInternalContext is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 117 | `System.Runtime.Remoting.Contexts.Context` | `ExecuteCallBackInEE` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：ExecuteCallBackInEE is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested L | `MethodMap.Legacy.cs:44` |
| 118 | `System.Runtime.Remoting.Messaging.Message` | `GetAsyncBeginInfo` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetAsyncBeginInfo is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOU | `MethodMap.Legacy.cs:44` |
| 119 | `System.Runtime.Remoting.Messaging.Message` | `GetAsyncResult` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetAsyncResult is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD r | `MethodMap.Legacy.cs:44` |
| 120 | `System.Runtime.Remoting.Messaging.Message` | `GetReturnValue` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetReturnValue is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD r | `MethodMap.Legacy.cs:44` |
| 121 | `System.Runtime.Remoting.Messaging.Message` | `InternalGetArg` | internal/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：InternalGetArg is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD r | `MethodMap.Legacy.cs:44` |
| 122 | `System.Runtime.Remoting.Messaging.Message` | `InternalGetArgCount` | internal/internal/instance | 缺（LOUD 拒绝） | LOUD 拒绝：InternalGetArgCount is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested L | `MethodMap.Legacy.cs:44` |
| 123 | `System.Runtime.Remoting.Messaging.Message` | `InternalGetArgs` | internal/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：InternalGetArgs is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD  | `MethodMap.Legacy.cs:44` |
| 124 | `System.Runtime.Remoting.Messaging.Message` | `InternalHasVarArgs` | internal/internal/instance | 缺（LOUD 拒绝） | LOUD 拒绝：InternalHasVarArgs is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LO | `MethodMap.Legacy.cs:44` |
| 125 | `System.Runtime.Remoting.Messaging.Message` | `PropagateOutParameters` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PropagateOutParameters is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 126 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetDefaultStub` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：GetDefaultStub is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD r | `MethodMap.Legacy.cs:44` |
| 127 | `System.Runtime.Remoting.RemotingServices` | `AllocateInitializedObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：AllocateInitializedObject is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'sugge | `MethodMap.Legacy.cs:44` |
| 128 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerRemotingClientReceivingReply` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerRemotingClientReceivingReply is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-fac | `MethodMap.Legacy.cs:44` |
| 129 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerRemotingClientSendingMessage` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerRemotingClientSendingMessage is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-fac | `MethodMap.Legacy.cs:44` |
| 130 | `System.Runtime.Remoting.RemotingServices` | `CheckCast` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CheckCast is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject | `MethodMap.Legacy.cs:44` |
| 131 | `System.Runtime.Remoting.RemotingServices` | `ResetInterfaceCache` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：ResetInterfaceCache is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested L | `MethodMap.Legacy.cs:44` |
| 132 | `System.Runtime.Remoting.RemotingServices` | `Unwrap` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：Unwrap is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject'): | `MethodMap.Legacy.cs:44` |
| 133 | `System.RuntimeFieldHandle` | `GetAttributes` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetAttributes not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 134 | `System.RuntimeFieldHandle` | `GetName` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetName not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type hand | `MethodMap.ReflectHandle.cs:55` |
| 135 | `System.RuntimeFieldHandle` | `GetStaticFieldForGenericType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetStaticFieldForGenericType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntime | `MethodMap.ReflectHandle.cs:55` |
| 136 | `System.RuntimeFieldHandle` | `GetToken` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetToken not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 137 | `System.RuntimeFieldHandle` | `GetValueDirect` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetValueDirect not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 138 | `System.RuntimeFieldHandle` | `MatchesNameHash` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::MatchesNameHash not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 139 | `System.RuntimeFieldHandle` | `SetValueDirect` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::SetValueDirect not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 140 | `System.RuntimeFieldHandle` | `_GetUtf8Name` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::_GetUtf8Name not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type | `MethodMap.ReflectHandle.cs:55` |
| 141 | `System.RuntimeMethodHandle` | `GetImplAttributes` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetImplAttributes not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on th | `MethodMap.ReflectHandle.cs:55` |
| 142 | `System.RuntimeMethodHandle` | `GetMethodBody` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetMethodBody not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 143 | `System.RuntimeMethodHandle` | `GetMethodDef` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetMethodDef not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 144 | `System.RuntimeMethodHandle` | `MatchesNameHash` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::MatchesNameHash not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 145 | `System.RuntimeMethodHandle` | `_GetUtf8Name` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::_GetUtf8Name not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 146 | `System.RuntimeMethodHandle` | `_IsTokenSecurityTransparent` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::_IsTokenSecurityTransparent not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntime | `MethodMap.ReflectHandle.cs:55` |
| 147 | `System.RuntimeType` | `GetGUID` | internal/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 148 | `System.RuntimeType` | `InvokeDispMethod` | internal/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 149 | `System.RuntimeType` | `IsTypeExportedToWindowsRuntime` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 150 | `System.RuntimeType` | `IsWindowsRuntimeObjectType` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 151 | `System.RuntimeType` | `_CreateEnum` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 152 | `System.RuntimeTypeHandle` | `CanCastTo` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CanCastTo not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 153 | `System.RuntimeTypeHandle` | `ContainsGenericVariables` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::ContainsGenericVariables not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 154 | `System.RuntimeTypeHandle` | `CreateInstanceForAnotherGenericParameter` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CreateInstanceForAnotherGenericParameter not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus | `MethodMap.ReflectHandle.cs:55` |
| 155 | `System.RuntimeTypeHandle` | `GetArrayRank` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetArrayRank not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type  | `MethodMap.ReflectHandle.cs:55` |
| 156 | `System.RuntimeTypeHandle` | `GetAttributes` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetAttributes not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type | `MethodMap.ReflectHandle.cs:55` |
| 157 | `System.RuntimeTypeHandle` | `GetDeclaringMethod` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetDeclaringMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the | `MethodMap.ReflectHandle.cs:55` |
| 158 | `System.RuntimeTypeHandle` | `GetDeclaringType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetDeclaringType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 159 | `System.RuntimeTypeHandle` | `GetFields` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetFields not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 160 | `System.RuntimeTypeHandle` | `GetFirstIntroducedMethod` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetFirstIntroducedMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 161 | `System.RuntimeTypeHandle` | `GetGenericVariableIndex` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetGenericVariableIndex not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 162 | `System.RuntimeTypeHandle` | `GetInterfaces` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetInterfaces not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type | `MethodMap.ReflectHandle.cs:55` |
| 163 | `System.RuntimeTypeHandle` | `GetNextIntroducedMethod` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetNextIntroducedMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 164 | `System.RuntimeTypeHandle` | `GetToken` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetToken not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type hand | `MethodMap.ReflectHandle.cs:55` |
| 165 | `System.RuntimeTypeHandle` | `GetValueInternal` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetValueInternal not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 166 | `System.RuntimeTypeHandle` | `HasProxyAttribute` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::HasProxyAttribute not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 167 | `System.RuntimeTypeHandle` | `IsContextful` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsContextful not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type  | `MethodMap.ReflectHandle.cs:55` |
| 168 | `System.RuntimeTypeHandle` | `IsDoNotForceOrderOfConstructorsSet` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsDoNotForceOrderOfConstructorsSet not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRu | `MethodMap.ReflectHandle.cs:55` |
| 169 | `System.RuntimeTypeHandle` | `IsEquivalentTo` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsEquivalentTo not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 170 | `System.RuntimeTypeHandle` | `IsInstanceOfType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsInstanceOfType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 171 | `System.RuntimeTypeHandle` | `IsZapped` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsZapped not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type hand | `MethodMap.ReflectHandle.cs:55` |
| 172 | `System.RuntimeTypeHandle` | `SatisfiesConstraints` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::SatisfiesConstraints not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on t | `MethodMap.ReflectHandle.cs:55` |
| 173 | `System.RuntimeTypeHandle` | `_GetMetadataImport` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::_GetMetadataImport not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the | `MethodMap.ReflectHandle.cs:55` |
| 174 | `System.RuntimeTypeHandle` | `_GetUtf8Name` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::_GetUtf8Name not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type  | `MethodMap.ReflectHandle.cs:55` |
| 175 | `System.Security.Cryptography.X509Certificates.SafeCertContextHandle` | `_FreePCertContext` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 176 | `System.Security.Cryptography.X509Certificates.SafeCertStoreHandle` | `_FreeCertStoreContext` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 177 | `System.Signature` | `CompareSig` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 178 | `System.Signature` | `GetCustomModifiers` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 179 | `System.SizedReference` | `CreateSizedRef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 180 | `System.SizedReference` | `FreeSizedRef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 181 | `System.SizedReference` | `GetApproximateSizeOfSizedRef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 182 | `System.SizedReference` | `GetTargetOfSizedRef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 183 | `System.StubHelpers.DateMarshaler` | `ConvertToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 184 | `System.StubHelpers.DateMarshaler` | `ConvertToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 185 | `System.StubHelpers.InterfaceMarshaler` | `ConvertToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 186 | `System.StubHelpers.InterfaceMarshaler` | `ConvertToManagedWithoutUnboxing` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 187 | `System.StubHelpers.InterfaceMarshaler` | `ConvertToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 188 | `System.StubHelpers.MngdHiddenLengthArrayMarshaler` | `ClearNativeContents` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 189 | `System.StubHelpers.MngdHiddenLengthArrayMarshaler` | `ConvertContentsToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 190 | `System.StubHelpers.MngdHiddenLengthArrayMarshaler` | `ConvertContentsToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 191 | `System.StubHelpers.MngdHiddenLengthArrayMarshaler` | `ConvertSpaceToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 192 | `System.StubHelpers.MngdHiddenLengthArrayMarshaler` | `ConvertSpaceToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 193 | `System.StubHelpers.MngdHiddenLengthArrayMarshaler` | `CreateMarshaler` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 194 | `System.StubHelpers.MngdNativeArrayMarshaler` | `ClearNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 195 | `System.StubHelpers.MngdNativeArrayMarshaler` | `ClearNativeContents` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 196 | `System.StubHelpers.MngdNativeArrayMarshaler` | `ConvertContentsToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 197 | `System.StubHelpers.MngdNativeArrayMarshaler` | `ConvertContentsToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 198 | `System.StubHelpers.MngdNativeArrayMarshaler` | `ConvertSpaceToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 199 | `System.StubHelpers.MngdNativeArrayMarshaler` | `ConvertSpaceToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 200 | `System.StubHelpers.MngdNativeArrayMarshaler` | `CreateMarshaler` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 201 | `System.StubHelpers.MngdRefCustomMarshaler` | `ClearManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 202 | `System.StubHelpers.MngdRefCustomMarshaler` | `ClearNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 203 | `System.StubHelpers.MngdRefCustomMarshaler` | `ConvertContentsToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 204 | `System.StubHelpers.MngdRefCustomMarshaler` | `ConvertContentsToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 205 | `System.StubHelpers.MngdRefCustomMarshaler` | `CreateMarshaler` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 206 | `System.StubHelpers.MngdSafeArrayMarshaler` | `ClearNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 207 | `System.StubHelpers.MngdSafeArrayMarshaler` | `ConvertContentsToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 208 | `System.StubHelpers.MngdSafeArrayMarshaler` | `ConvertContentsToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 209 | `System.StubHelpers.MngdSafeArrayMarshaler` | `ConvertSpaceToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 210 | `System.StubHelpers.MngdSafeArrayMarshaler` | `ConvertSpaceToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 211 | `System.StubHelpers.MngdSafeArrayMarshaler` | `CreateMarshaler` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 212 | `System.StubHelpers.ObjectMarshaler` | `ClearNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 213 | `System.StubHelpers.ObjectMarshaler` | `ConvertToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 214 | `System.StubHelpers.ObjectMarshaler` | `ConvertToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 215 | `System.StubHelpers.StubHelpers` | `AllocateInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 216 | `System.StubHelpers.StubHelpers` | `CalcVaListSize` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 217 | `System.StubHelpers.StubHelpers` | `CheckCollectedDelegateMDA` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 218 | `System.StubHelpers.StubHelpers` | `CreateCustomMarshalerHelper` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 219 | `System.StubHelpers.StubHelpers` | `DecimalCanonicalizeInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 220 | `System.StubHelpers.StubHelpers` | `DemandPermission` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 221 | `System.StubHelpers.StubHelpers` | `FmtClassUpdateCLRInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 222 | `System.StubHelpers.StubHelpers` | `FmtClassUpdateNativeInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 223 | `System.StubHelpers.StubHelpers` | `GetCOMIPFromRCW` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 224 | `System.StubHelpers.StubHelpers` | `GetCOMIPFromRCW_WinRT` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 225 | `System.StubHelpers.StubHelpers` | `GetCOMIPFromRCW_WinRTDelegate` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 226 | `System.StubHelpers.StubHelpers` | `GetCOMIPFromRCW_WinRTSharedGeneric` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 227 | `System.StubHelpers.StubHelpers` | `GetDelegateInvokeMethod` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 228 | `System.StubHelpers.StubHelpers` | `GetDelegateTarget` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 229 | `System.StubHelpers.StubHelpers` | `GetNDirectTarget` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 230 | `System.StubHelpers.StubHelpers` | `GetOuterInspectable` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 231 | `System.StubHelpers.StubHelpers` | `GetStubContext` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 232 | `System.StubHelpers.StubHelpers` | `GetStubContextAddr` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 233 | `System.StubHelpers.StubHelpers` | `GetTargetForAmbiguousVariantCall` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 234 | `System.StubHelpers.StubHelpers` | `GetWinRTFactoryObject` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 235 | `System.StubHelpers.StubHelpers` | `GetWinRTFactoryReturnValue` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 236 | `System.StubHelpers.StubHelpers` | `InitDeclaringType` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 237 | `System.StubHelpers.StubHelpers` | `InternalGetCOMHRExceptionObject` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 238 | `System.StubHelpers.StubHelpers` | `InternalGetHRExceptionObject` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 239 | `System.StubHelpers.StubHelpers` | `IsQCall` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 240 | `System.StubHelpers.StubHelpers` | `LayoutDestroyNativeInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 241 | `System.StubHelpers.StubHelpers` | `LogPinnedArgument` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 242 | `System.StubHelpers.StubHelpers` | `MarshalToManagedVaListInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 243 | `System.StubHelpers.StubHelpers` | `MarshalToUnmanagedVaListInternal` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 244 | `System.StubHelpers.StubHelpers` | `ProfilerBeginTransitionCallback` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 245 | `System.StubHelpers.StubHelpers` | `ProfilerEndTransitionCallback` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 246 | `System.StubHelpers.StubHelpers` | `SetLastError` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 247 | `System.StubHelpers.StubHelpers` | `ShouldCallWinRTInterface` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 248 | `System.StubHelpers.StubHelpers` | `StubRegisterRCW` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 249 | `System.StubHelpers.StubHelpers` | `StubUnregisterRCW` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 250 | `System.StubHelpers.StubHelpers` | `ThrowInteropParamException` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 251 | `System.StubHelpers.StubHelpers` | `TriggerExceptionSwallowedMDA` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 252 | `System.StubHelpers.StubHelpers` | `TriggerGCForMDA` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 253 | `System.StubHelpers.StubHelpers` | `ValidateByref` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 254 | `System.StubHelpers.StubHelpers` | `ValidateObject` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 255 | `System.StubHelpers.StubHelpers` | `strlen` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 256 | `System.StubHelpers.UriMarshaler` | `CreateNativeUriInstanceHelper` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 257 | `System.StubHelpers.UriMarshaler` | `GetRawUriFromNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 258 | `System.StubHelpers.ValueClassMarshaler` | `ClearNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 259 | `System.StubHelpers.ValueClassMarshaler` | `ConvertToManaged` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 260 | `System.StubHelpers.ValueClassMarshaler` | `ConvertToNative` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 261 | `System.StubHelpers.WinRTTypeNameConverter` | `ConvertToWinRTTypeName` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 262 | `System.StubHelpers.WinRTTypeNameConverter` | `GetTypeFromWinRTTypeName` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 263 | `System.Threading.CompressedStack` | `DestroyDelayedCompressedStack` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 264 | `System.Threading.DomainCompressedStack` | `GetDescCount` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 265 | `System.Threading.DomainCompressedStack` | `GetDescriptorInfo` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 266 | `System.Threading.DomainCompressedStack` | `GetDomainPermissionSets` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 267 | `System.Threading.DomainCompressedStack` | `IgnoreDomain` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 268 | `System.Threading.OverlappedData` | `CheckVMForIOPacket` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 269 | `System.Threading.ReaderWriterLock` | `PrivateDestruct` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateDestruct is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descripto | `MethodMap.Legacy.cs:242` |
| 270 | `System.Threading.RegisteredWaitHandleSafe` | `WaitHandleCleanupNative` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 271 | `System.Threading.Thread` | `InternalFinalize` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 272 | `System.Threading.Thread` | `InternalGetCurrentThread` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_threadobj_internal_get_current_thread`，但运行时是空体/恒值桩（桩定义 `threadobj.cpp:708`） | `threadobj.cpp:708` |
| 273 | `System.Utf8String` | `EqualsCaseSensitive` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 274 | `System.Variant` | `BoxEnum` | internal/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 275 | `System.Variant` | `GetR4FromVar` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 276 | `System.Variant` | `GetR8FromVar` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 277 | `System.Variant` | `SetFieldsObject` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 278 | `System.Variant` | `SetFieldsR4` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 279 | `System.Variant` | `SetFieldsR8` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 280 | `System.WeakReference`1` | `Create` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 281 | `System.WeakReference`1` | `IsTrackResurrection` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 282 | `Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs` | `.ctor` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such overload): WeakReference ctor arity 0 -- net48 has only WeakReference(object) and WeakReference(object, bool) | `MethodMap.WeakReference.cs:44` |
| 283 | `Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs` | `get_Enabled` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 284 | `Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs` | `get_TraceLevel` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |

### 3.6 影子清单

共 **74** 条。判定依据：icall 自身没有任何接线，但用户可见的入口方法已接线；mscorlib 的方法体不会被转译，用户 IL 只发出对入口的那一次调用，因此这条 icall 不在生成的 C++ 走到的路径上。

**跳数必须写清楚。** 文档旧版把 `System.Threading.Thread::StartInternal` 写成「由 `Start()` 直接承接」，实际链路是 public `Start()` → internal `Start(StackCrawlMark&)` → `StartInternal`，**2 跳**，中间那一跳的 mscorlib 方法体同样不会被转译，所以这个结论仍然成立，但「直接承接」的表述不成立。本表 `跳数` 列给出实测 IL 边数：1 跳才是直接承接；≥2 跳列出完整链路。（本版 65 条 1 跳、9 条 ≥2 跳。）

| # | 声明类型 | 方法 | 可见性 | 承接的 public 入口 | 跳数 | 完整链路 | 承接者接线证据 |
|---|---|---|---|---|---|---|---|
| 1 | `System.AppDomain` | `nGetFriendlyName` | public/private/instance | `System.AppDomain::ToString` | 1 | `（直接调用）` | `MethodMap.Legacy.cs:153` |
| 2 | `System.Array` | `InternalGetReference` | public/private/instance | `System.Array::GetValue` | 1 | `（直接调用）` | `MethodMap.Array.cs:1129` |
| 3 | `System.Array` | `InternalSetValue` | public/private/static | `System.Array::SetValue` | 1 | `（直接调用）` | `MethodMap.Array.cs:1158` |
| 4 | `System.Array` | `TrySZBinarySearch` | public/private/static | `System.Array::BinarySearch` | 1 | `（直接调用）` | `MethodMap.Array.cs:378` |
| 5 | `System.Array` | `TrySZIndexOf` | public/private/static | `System.Array::IndexOf` | 1 | `（直接调用）` | `MethodMap.Array.cs:428` |
| 6 | `System.Array` | `TrySZLastIndexOf` | public/private/static | `System.Array::LastIndexOf` | 1 | `（直接调用）` | `MethodMap.Array.cs:448` |
| 7 | `System.Array` | `TrySZReverse` | public/private/static | `System.Array::Reverse` | 1 | `（直接调用）` | `MethodMap.Array.cs:527` |
| 8 | `System.Array` | `TrySZSort` | public/private/static | `System.Array::Sort` | 1 | `（直接调用）` | `MethodMap.Array.cs:145` |
| 9 | `System.Buffer` | `IsPrimitiveTypeArray` | public/private/static | `System.Buffer::ByteLength` | 1 | `（直接调用）` | `MethodMap.Buffer.cs:38` |
| 10 | `System.Buffer` | `_ByteLength` | public/private/static | `System.Buffer::ByteLength` | 1 | `（直接调用）` | `MethodMap.Buffer.cs:38` |
| 11 | `System.Buffer` | `_GetByte` | public/private/static | `System.Buffer::GetByte` | 1 | `（直接调用）` | `MethodMap.Buffer.cs:71` |
| 12 | `System.Buffer` | `_SetByte` | public/private/static | `System.Buffer::SetByte` | 1 | `（直接调用）` | `MethodMap.Buffer.cs:81` |
| 13 | `System.Decimal` | `FCallFloor` | public/private/static | `System.Decimal::Floor` | 1 | `（直接调用）` | `MethodMap.Decimal.cs:305` |
| 14 | `System.Decimal` | `FCallTruncate` | public/private/static | `System.Decimal::ToInt32` | 1 | `（直接调用）` | `MethodMap.Decimal.cs:316` |
| 15 | `System.Enum` | `InternalBoxEnum` | public/private/static | `System.Enum::ToObject` | 1 | `（直接调用）` | `MethodMap.Enum.cs:398` |
| 16 | `System.Enum` | `InternalCompareTo` | public/private/static | `System.Enum::CompareTo` | 1 | `（直接调用）` | `MethodMap.Enum.cs:416` |
| 17 | `System.Enum` | `InternalHasFlag` | public/private/instance | `System.Enum::HasFlag` | 1 | `（直接调用）` | `MethodMap.Enum.cs:404` |
| 18 | `System.Environment` | `GetCommandLineArgsNative` | public/private/static | `System.Environment::GetCommandLineArgs` | 1 | `（直接调用）` | `MethodMap.Environment.cs:76` |
| 19 | `System.Environment` | `GetVersionEx` | public/internal/static | `System.Environment::get_OSVersion` | 1 | `（直接调用）` | `MethodMap.Environment.cs:48` |
| 20 | `System.Exception` | `IsImmutableAgileException` | public/private/static | `System.Exception::get_Data` | 1 | `（直接调用）` | `MethodMap.Exception.cs:215` |
| 21 | `System.GC` | `GetGenerationWR` | public/private/static | `System.GC::GetGeneration` | 1 | `（直接调用）` | `MethodMap.GC.cs:54` |
| 22 | `System.GC` | `_CancelFullGCNotification` | public/private/static | `System.GC::CancelFullGCNotification` | 1 | `（直接调用）` | `MethodMap.GC.cs:107` |
| 23 | `System.GC` | `_CollectionCount` | public/private/static | `System.GC::CollectionCount` | 1 | `（直接调用）` | `MethodMap.GC.cs:96` |
| 24 | `System.GC` | `_GetAllocatedBytesForCurrentThread` | public/private/static | `System.GC::GetAllocatedBytesForCurrentThread` | 1 | `（直接调用）` | `MethodMap.GC.cs:118` |
| 25 | `System.GC` | `_ReRegisterForFinalize` | public/private/static | `System.GC::ReRegisterForFinalize` | 1 | `（直接调用）` | `MethodMap.GC.cs:57` |
| 26 | `System.GC` | `_RegisterForFullGCNotification` | public/private/static | `System.GC::RegisterForFullGCNotification` | 1 | `（直接调用）` | `MethodMap.GC.cs:102` |
| 27 | `System.GC` | `_SuppressFinalize` | public/private/static | `System.GC::SuppressFinalize` | 1 | `（直接调用）` | `MethodMap.GC.cs:56` |
| 28 | `System.Globalization.TextInfo` | `InternalChangeCaseChar` | public/private/static | `System.Globalization.TextInfo::ToLower` | 1 | `（直接调用）` | `MethodMap.NumberFormat.cs:166` |
| 29 | `System.Globalization.TextInfo` | `InternalChangeCaseString` | public/private/static | `System.Globalization.TextInfo::ToLower` | 1 | `（直接调用）` | `MethodMap.NumberFormat.cs:166` |
| 30 | `System.Mda` | `DateTimeInvalidLocalFormat` | internal/internal/static | `System.DateTimeOffset::ToString` | 5 | `System.DateTimeFormat::InvalidFormatForUtc → System.DateTimeFormat::FormatCustomizedTimeZone → System.DateTimeFormat::FormatCustomized → System.DateTimeFormat::Format → System.DateTimeOffset::ToString` | `MethodMap.Dtoffset.cs:200` |
| 31 | `System.Mda` | `FireInvalidGCHandleCookieProbe` | internal/internal/static | `System.Runtime.InteropServices.GCHandle::FromIntPtr` | 1 | `（直接调用）` | `MethodMap.GCHandle.cs:83` |
| 32 | `System.Mda` | `IsStreamWriterBufferedDataLostCaptureAllocatedCallStack` | internal/internal/static | `System.IO.StreamWriter::.ctor` | 3 | `System.Mda+StreamWriterBufferedDataLost::get_CaptureAllocatedCallStack → System.IO.StreamWriter::Init → System.IO.StreamWriter::.ctor` | `MethodMap.Deflate.cs:45` |
| 33 | `System.Mda` | `IsStreamWriterBufferedDataLostEnabled` | internal/internal/static | `System.IO.StreamWriter::.ctor` | 3 | `System.Mda+StreamWriterBufferedDataLost::get_Enabled → System.IO.StreamWriter::Init → System.IO.StreamWriter::.ctor` | `MethodMap.Deflate.cs:45` |
| 34 | `System.Number` | `FormatDecimal` | internal/public/static | `System.Decimal::ToString` | 1 | `（直接调用）` | `MethodMap.Decimal.cs:227` |
| 35 | `System.Number` | `FormatDouble` | internal/public/static | `System.Double::ToString` | 1 | `（直接调用）` | `MethodMap.ValueType.cs:25` |
| 36 | `System.Number` | `FormatInt32` | internal/public/static | `System.Byte::ToString` | 1 | `（直接调用）` | `MethodMap.ValueType.cs:25` |
| 37 | `System.Number` | `FormatInt64` | internal/public/static | `System.Int64::ToString` | 1 | `（直接调用）` | `MethodMap.ValueType.cs:25` |
| 38 | `System.Number` | `FormatSingle` | internal/public/static | `System.Single::ToString` | 1 | `（直接调用）` | `MethodMap.ValueType.cs:25` |
| 39 | `System.Number` | `FormatUInt32` | internal/public/static | `System.UInt16::ToString` | 1 | `（直接调用）` | `MethodMap.ValueType.cs:25` |
| 40 | `System.Number` | `FormatUInt64` | internal/public/static | `System.UInt64::ToString` | 1 | `（直接调用）` | `MethodMap.ValueType.cs:25` |
| 41 | `System.ParseNumbers` | `LongToString` | internal/public/static | `System.Convert::ToString` | 1 | `（直接调用）` | `MethodMap.Convert.cs:237` |
| 42 | `System.Reflection.AssemblyName` | `nToString` | public/private/instance | `System.Reflection.AssemblyName::get_FullName` | 1 | `（直接调用）` | `MethodMap.Type.cs:985` |
| 43 | `System.Runtime.InteropServices.GCHandle` | `InternalAddrOfPinnedObject` | public/internal/static | `System.Runtime.InteropServices.GCHandle::AddrOfPinnedObject` | 1 | `（直接调用）` | `MethodMap.GCHandle.cs:65` |
| 44 | `System.Runtime.InteropServices.GCHandle` | `InternalFree` | public/internal/static | `System.Runtime.InteropServices.GCHandle::Free` | 1 | `（直接调用）` | `MethodMap.GCHandle.cs:53` |
| 45 | `System.Runtime.InteropServices.GCHandle` | `InternalGet` | public/internal/static | `System.Runtime.InteropServices.GCHandle::get_Target` | 1 | `（直接调用）` | `MethodMap.GCHandle.cs:57` |
| 46 | `System.Runtime.InteropServices.GCHandle` | `InternalSet` | public/internal/static | `System.Runtime.InteropServices.GCHandle::set_Target` | 1 | `（直接调用）` | `MethodMap.GCHandle.cs:61` |
| 47 | `System.Runtime.InteropServices.Marshal` | `CopyToManaged` | public/private/static | `System.Runtime.InteropServices.Marshal::Copy` | 1 | `（直接调用）` | `MethodMap.Marshal.cs:182` |
| 48 | `System.Runtime.InteropServices.Marshal` | `CopyToNative` | public/private/static | `System.Runtime.InteropServices.Marshal::Copy` | 1 | `（直接调用）` | `MethodMap.Marshal.cs:182` |
| 49 | `System.RuntimeTypeHandle` | `IsInterface` | public/internal/static | `System.Type::get_IsInterface` | 1 | `（直接调用）` | `MethodMap.Type.cs:719` |
| 50 | `System.Security.Cryptography.Utils` | `_ProduceLegacyHmacValues` | internal/internal/static | `System.Security.Cryptography.HMACSHA384::.ctor` | 1 | `（直接调用）` | `MethodMap.Crypto.cs:91` |
| 51 | `System.String` | `PadHelper` | public/private/instance | `System.String::PadLeft` | 1 | `（直接调用）` | `MethodMap.String.cs:243` |
| 52 | `System.String` | `ReplaceInternal` | public/private/instance | `System.String::Replace` | 1 | `（直接调用）` | `MethodMap.String.cs:137` |
| 53 | `System.String` | `ReplaceInternal` | public/private/instance | `System.String::Replace` | 1 | `（直接调用）` | `MethodMap.String.cs:137` |
| 54 | `System.Text.Normalization` | `nativeNormalizationIsNormalizedString` | internal/private/static | `System.String::IsNormalized` | 2 | `System.Text.Normalization::IsNormalized → System.String::IsNormalized` | `MethodMap.String.cs:306` |
| 55 | `System.Text.Normalization` | `nativeNormalizationNormalizeString` | internal/private/static | `System.String::Normalize` | 2 | `System.Text.Normalization::Normalize → System.String::Normalize` | `MethodMap.String.cs:298` |
| 56 | `System.Threading.Monitor` | `ObjPulse` | public/private/static | `System.Threading.Monitor::Pulse` | 1 | `（直接调用）` | `MethodMap.Monitor.cs:91` |
| 57 | `System.Threading.Monitor` | `ObjPulseAll` | public/private/static | `System.Threading.Monitor::PulseAll` | 1 | `（直接调用）` | `MethodMap.Monitor.cs:92` |
| 58 | `System.Threading.Monitor` | `ReliableEnter` | public/private/static | `System.Threading.Monitor::Enter` | 1 | `（直接调用）` | `MethodMap.Monitor.cs:45` |
| 59 | `System.Threading.Thread` | `GetCurrentThreadNative` | public/private/static | `System.Threading.Thread::get_CurrentThread` | 1 | `（直接调用）` | `MethodMap.Thread.cs:212` |
| 60 | `System.Threading.Thread` | `GetPriorityNative` | public/private/instance | `System.Threading.Thread::get_Priority` | 1 | `（直接调用）` | `MethodMap.Thread.cs:190` |
| 61 | `System.Threading.Thread` | `GetThreadStateNative` | public/private/instance | `System.Threading.Thread::get_ThreadState` | 1 | `（直接调用）` | `MethodMap.Thread.cs:217` |
| 62 | `System.Threading.Thread` | `IsBackgroundNative` | public/private/instance | `System.Threading.Thread::get_IsBackground` | 1 | `（直接调用）` | `MethodMap.Thread.cs:204` |
| 63 | `System.Threading.Thread` | `SetBackgroundNative` | public/private/instance | `System.Threading.Thread::set_IsBackground` | 1 | `（直接调用）` | `MethodMap.Thread.cs:208` |
| 64 | `System.Threading.Thread` | `SetPriorityNative` | public/private/instance | `System.Threading.Thread::set_Priority` | 1 | `（直接调用）` | `MethodMap.Thread.cs:194` |
| 65 | `System.Threading.Thread` | `SetStart` | public/private/instance | `System.Threading.Thread::.ctor` | 2 | `System.Threading.Thread::SetStartHelper → System.Threading.Thread::.ctor` | `MethodMap.Thread.cs:139` |
| 66 | `System.Threading.Thread` | `SleepInternal` | public/private/static | `System.Threading.Thread::Sleep` | 1 | `（直接调用）` | `MethodMap.Thread.cs:47` |
| 67 | `System.Threading.Thread` | `SpinWaitInternal` | public/private/static | `System.Threading.Thread::SpinWait` | 1 | `（直接调用）` | `MethodMap.Thread.cs:52` |
| 68 | `System.Threading.Thread` | `StartInternal` | public/private/instance | `System.Threading.Thread::Start` | 2 | `System.Threading.Thread::Start → System.Threading.Thread::Start` | `MethodMap.Thread.cs:152` |
| 69 | `System.Threading.Thread` | `StartupSetApartmentStateInternal` | public/private/instance | `System.Threading.Thread::Start` | 2 | `System.Threading.Thread::Start → System.Threading.Thread::Start` | `MethodMap.Thread.cs:152` |
| 70 | `System.Threading.ThreadPool` | `GetAvailableThreadsNative` | public/private/static | `System.Threading.ThreadPool::GetAvailableThreads` | 1 | `（直接调用）` | `MethodMap.ThreadPool.cs:51` |
| 71 | `System.Threading.ThreadPool` | `GetMaxThreadsNative` | public/private/static | `System.Threading.ThreadPool::GetMaxThreads` | 1 | `（直接调用）` | `MethodMap.ThreadPool.cs:50` |
| 72 | `System.Threading.ThreadPool` | `GetMinThreadsNative` | public/private/static | `System.Threading.ThreadPool::GetMinThreads` | 1 | `（直接调用）` | `MethodMap.ThreadPool.cs:49` |
| 73 | `System.TimeSpan` | `LegacyFormatMode` | public/private/static | `System.TimeSpan::ToString` | 3 | `System.TimeSpan::GetLegacyFormatMode → System.TimeSpan::get_LegacyMode → System.TimeSpan::ToString` | `MethodMap.TimeSpan.cs:98` |
| 74 | `System.WeakReference` | `Create` | public/private/instance | `System.WeakReference::.ctor` | 1 | `（直接调用）` | `MethodMap.WeakReference.cs:33` |

### 3.7 部分影子清单

共 **16** 条。这些不是「无需接线」也不是「完全没接线」，而是**只覆盖了一部分调用形状**：同一最短距离上存在未接线的入口，或该入口有同名但未映射的重载，用户写那种形状仍会落到幽灵符号。

典型是 `System.Threading.Interlocked::_CompareExchange`：它的数值重载 `CompareExchange(int&,int,int)` 等都已接线，但泛型重载 `CompareExchange(!!0&, !!0, !!0)` 在探针里是 `unmapped` —— 用户写 `ref T` 就走幽灵符号。所以 `_CompareExchange` / `_Exchange` 只能算**部分承接**，不能计入完整影子。

| # | 声明类型 | 方法 | 签名 | 承接的 public 入口 | 跳数 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `System.Decimal` | `FCallCompare` | `System.Int32 (System.Decimal&, System.Decimal&)` | `System.Decimal::Compare` | 1 | `MethodMap.Decimal.cs:170` |
| 2 | `System.Decimal` | `FCallRound` | `System.Void (System.Decimal&, System.Int32)` | `System.Decimal::Round` | 1 | `MethodMap.Decimal.cs:290` |
| 3 | `System.Exception` | `nIsTransient` | `System.Boolean (System.Int32)` | `System.Resources.ResourceManager::.ctor` | 3 | `MethodMap.Resource.cs:65` |
| 4 | `System.Runtime.InteropServices.Marshal` | `GetHRForException_WinRT` | `System.Int32 (System.Exception)` | `System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1::AwaitUnsafeOnCompleted` | 3 | `MethodMap.Task.cs:222` |
| 5 | `System.Runtime.InteropServices.Marshal` | `OffsetOfHelper` | `System.IntPtr (System.IRuntimeFieldInfo)` | `System.Runtime.InteropServices.Marshal::OffsetOf` | 1 | `MethodMap.Marshal.cs:293` |
| 6 | `System.Runtime.InteropServices.Marshal` | `SizeOfHelper` | `System.Int32 (System.Type, System.Boolean)` | `System.Runtime.InteropServices.Marshal::SizeOf` | 1 | `MethodMap.Marshal.cs:241` |
| 7 | `System.RuntimeType` | `GetTypeFromCLSIDImpl` | `System.Type (System.Guid, System.String, System.Boolean)` | `System.Type::GetTypeFromCLSID` | 1 | `MethodMap.Type.cs:787` |
| 8 | `System.RuntimeType` | `GetTypeFromProgIDImpl` | `System.Type (System.String, System.String, System.Boolean)` | `System.Type::GetTypeFromProgID` | 1 | `MethodMap.Type.cs:782` |
| 9 | `System.Threading.Interlocked` | `_CompareExchange` | `System.Void (System.TypedReference, System.TypedReference, System.Object)` | `System.Threading.Interlocked::CompareExchange` | 1 | `MethodMap.Interlocked.cs:101` |
| 10 | `System.Threading.Interlocked` | `_Exchange` | `System.Void (System.TypedReference, System.TypedReference)` | `System.Threading.Interlocked::Exchange` | 1 | `MethodMap.Interlocked.cs:63` |
| 11 | `System.Threading.Monitor` | `ObjWait` | `System.Boolean (System.Boolean, System.Int32, System.Object)` | `System.Threading.Monitor::Wait` | 1 | `MethodMap.Monitor.cs:98` |
| 12 | `System.Threading.Monitor` | `ReliableEnterTimeout` | `System.Void (System.Object, System.Int32, System.Boolean&)` | `System.Threading.Monitor::TryEnter` | 1 | `MethodMap.Monitor.cs:63` |
| 13 | `System.Threading.Thread` | `JoinInternal` | `System.Boolean (System.Int32)` | `System.Threading.Thread::Join` | 1 | `MethodMap.Thread.cs:169` |
| 14 | `System.Threading.Thread` | `nativeGetSafeCulture` | `System.Boolean (System.Threading.Thread, System.Int32, System.Boolean, System.Globalization.CultureInfo&)` | `System.Resources.ResourceManager::GetString` | 2 | `MethodMap.Resources.cs:110` |
| 15 | `System.Threading.WaitHandle` | `WaitOneNative` | `System.Int32 (System.Runtime.InteropServices.SafeHandle, System.UInt32, System.Boolean, System.Boolean)` | `System.Threading.Thread::Sleep` | 2 | `MethodMap.Thread.cs:47` |
| 16 | `System.TypedReference` | `InternalToObject` | `System.Object (System.Void*)` | `System.Array::GetValue` | 1 | `MethodMap.Array.cs:1129` |

### 3.8 已接 ≠ 语义等价 net48（恒值桩）

下面这些运行时实现是**空体或单一 `return <常量>;`**：它永远返回同一个字面量、完全不看参数，或者干脆什么都不做。它们不是「LOUD 拒绝」（不是明确抛异常），也不是「无接线」（能编译能链接），而是**能跑但答案恒定的静默偏差** —— 对 net48 byte-exact 来说等价于「编译通过、行为恒定」，不能算语义等价。

扫描器（`tools/audit/parse_runtime_syms.py` 的 `is_constant_stub`）在 `src/runtime/src` 全表命中**80** 个恒值/空体桩。其中 **30** 个正好是 908 条 icall 的 dispatch 目标，所以那 30 条 icall 从「已接」改判为「半接」；其余是没有 icall 引用的运行时辅助符号，不在本审计的 908 条范围内，但同样是净偏差（一并列出，便于后续排查）。

| # | 运行时实现 | 源码位置 | 实现体 | 被 N 条 icall 命中 |
|---|---|---|---|---|
| 1 | `icall_array_get_cor_element_type_of_element_type` | `src/runtime/src/stubs.cpp:31` | `return 4; // ELEMENT_TYPE_I4 (int32 default)` | 0 |
| 2 | `icall_assembly_load_context_get_loaded_assemblies` | `src/runtime/src/stubs.cpp:727` | `return nullptr; // empty array` | 0 |
| 3 | `icall_assembly_load_context_is_tracing_enabled` | `src/runtime/src/stubs.cpp:733` | `return 0; // false` | 0 |
| 4 | `icall_async_builder_set_state_machine` | `src/runtime/src/task.cpp:635` | `void icall_async_builder_set_state_machine(Il2CppObject* self)` | 0 |
| 5 | `icall_com_aware_weak_reference_has_interop_info` | `src/runtime/src/stubs.cpp:43` | `return 0; // false` | 0 |
| 6 | `icall_compatibility_switch_get_app_context_overrides` | `src/runtime/src/appcontext.cpp:189` | `return nullptr;` | 1 |
| 7 | `icall_compatibility_switch_get_value` | `src/runtime/src/appcontext.cpp:195` | `return nullptr;` | 1 |
| 8 | `icall_compatibility_switch_is_enabled` | `src/runtime/src/appcontext.cpp:201` | `return 0;` | 1 |
| 9 | `icall_datetime_get_type_code` | `src/runtime/src/time.cpp:1075` | `return 16; // TypeCode.DateTime (referencesource mscorlib/system/typecode.cs:44).` | 0 |
| 10 | `icall_delegate_find_method_handle` | `src/runtime/src/delegateext.cpp:694` | `return nullptr; // no method handle in the simplified model` | 1 |
| 11 | `icall_environment_get_has_shutdown_started` | `src/runtime/src/environment.cpp:531` | `return 0;` | 1 |
| 12 | `icall_environment_get_is_64bit_operating_system` | `src/runtime/src/stubs.cpp:963` | `return 1; // all supported targets are 64-bit operating systems` | 0 |
| 13 | `icall_environment_get_user_interactive` | `src/runtime/src/environment.cpp:1142` | `return 1;` | 0 |
| 14 | `icall_exception_get_exception_count` | `src/runtime/src/stubs.cpp:703` | `return 0;` | 0 |
| 15 | `icall_exception_get_null_ref` | `src/runtime/src/stubs.cpp:697` | `return nullptr;` | 0 |
| 16 | `icall_exception_save_stack_traces_from_deep_copy` | `src/runtime/src/stubs.cpp:47` | `void icall_exception_save_stack_traces_from_deep_copy(Il2CppObject*)` | 1 |
| 17 | `icall_gc_add_memory_pressure` | `src/runtime/src/gcapi.cpp:177` | `void icall_gc_add_memory_pressure(int64_t bytesAllocated)` | 0 |
| 18 | `icall_gc_allocate_new_array` | `src/runtime/src/stubs.cpp:37` | `return nullptr; // stub` | 0 |
| 19 | `icall_gc_is_server_gc` | `src/runtime/src/gcapi.cpp:167` | `return 0; // workstation collector model` | 1 |
| 20 | `icall_gc_keep_alive` | `src/runtime/src/gcapi.cpp:63` | `void icall_gc_keep_alive(Il2CppObject* obj)` | 0 |
| 21 | `icall_gc_register_for_gc_reporting` | `src/runtime/src/gcapi.cpp:189` | `void icall_gc_register_for_gc_reporting()` | 0 |
| 22 | `icall_gc_remove_memory_pressure` | `src/runtime/src/gcapi.cpp:182` | `void icall_gc_remove_memory_pressure(int64_t bytesAllocated)` | 0 |
| 23 | `icall_gc_unregister_for_gc_reporting` | `src/runtime/src/gcapi.cpp:194` | `void icall_gc_unregister_for_gc_reporting()` | 0 |
| 24 | `icall_jit_info_get_compiled_il_bytes` | `src/runtime/src/stubs.cpp:715` | `return 0;` | 0 |
| 25 | `icall_jit_info_get_compiled_method_count` | `src/runtime/src/stubs.cpp:721` | `return 0;` | 0 |
| 26 | `icall_marshal_get_exception_code` | `src/runtime/src/marshalext3.cpp:265` | `return 0;` | 1 |
| 27 | `icall_marshal_get_exception_pointers` | `src/runtime/src/marshalext3.cpp:275` | `return 0;` | 1 |
| 28 | `icall_reader_writer_lock_any_writers_since` | `src/runtime/src/stubs.cpp:1210` | `return 0;` | 1 |
| 29 | `icall_stack_trace_get_stack_frames_internal` | `src/runtime/src/stubs.cpp:757` | `void icall_stack_trace_get_stack_frames_internal(Il2CppObject*, int32_t, int32_t, Il2CppObject*)` | 1 |
| 30 | `icall_stacktrace_ctor` | `src/runtime/src/stubs.cpp:744` | `void icall_stacktrace_ctor(Il2CppObject*)` | 0 |
| 31 | `icall_thread_begin_critical_region` | `src/runtime/src/thread.cpp:120` | `void icall_thread_begin_critical_region()` | 1 |
| 32 | `icall_thread_begin_thread_affinity` | `src/runtime/src/thread.cpp:184` | `void icall_thread_begin_thread_affinity()` | 1 |
| 33 | `icall_thread_disable_com_object_eager_cleanup` | `src/runtime/src/thread.cpp:199` | `void icall_thread_disable_com_object_eager_cleanup(Il2CppObject* /*self*/)` | 1 |
| 34 | `icall_thread_end_critical_region` | `src/runtime/src/thread.cpp:124` | `void icall_thread_end_critical_region()` | 1 |
| 35 | `icall_thread_end_thread_affinity` | `src/runtime/src/thread.cpp:188` | `void icall_thread_end_thread_affinity()` | 1 |
| 36 | `icall_thread_get_priority` | `src/runtime/src/thread.cpp:114` | `return 0; // ThreadPriority.Normal (the single-threaded model)` | 0 |
| 37 | `icall_threadobj_clear_abort_reason` | `src/runtime/src/threadobj.cpp:735` | `void icall_threadobj_clear_abort_reason(Il2CppObject* self)` | 1 |
| 38 | `icall_threadobj_get_abort_reason` | `src/runtime/src/threadobj.cpp:732` | `return nullptr; // no abort reason in simplified model` | 1 |
| 39 | `icall_threadobj_get_apartment_state_native` | `src/runtime/src/threadobj.cpp:752` | `return 0; // MTA` | 1 |
| 40 | `icall_threadobj_get_context_internal` | `src/runtime/src/threadobj.cpp:714` | `return nullptr; // no context in simplified model` | 1 |
| 41 | `icall_threadobj_get_optimal_max_spin_waits` | `src/runtime/src/threadobj.cpp:770` | `return 1; // default spin count` | 0 |
| 42 | `icall_threadobj_internal_cross_context_callback` | `src/runtime/src/threadobj.cpp:720` | `return nullptr; // no cross-context callback in simplified model` | 1 |
| 43 | `icall_threadobj_internal_get_current_thread` | `src/runtime/src/threadobj.cpp:708` | `return nullptr; // no current thread in simplified model` | 1 |
| 44 | `icall_threadobj_interrupt` | `src/runtime/src/threadobj.cpp:761` | `void icall_threadobj_interrupt(Il2CppObject* self)` | 0 |
| 45 | `icall_threadobj_restore_app_domain_stack` | `src/runtime/src/threadobj.cpp:700` | `void icall_threadobj_restore_app_domain_stack(Il2CppObject* self)` | 1 |
| 46 | `icall_threadobj_set_abort_reason` | `src/runtime/src/threadobj.cpp:723` | `void icall_threadobj_set_abort_reason(Il2CppObject* self, Il2CppObject* reason)` | 1 |
| 47 | `icall_threadobj_set_apartment_state_native` | `src/runtime/src/threadobj.cpp:758` | `return true; // always succeeds` | 1 |
| 48 | `icall_threadobj_set_app_domain_stack` | `src/runtime/src/threadobj.cpp:694` | `void icall_threadobj_set_app_domain_stack(Il2CppObject* self, int32_t stackSize)` | 1 |
| 49 | `icall_threadobj_set_is_thread_pool_thread` | `src/runtime/src/threadobj.cpp:743` | `void icall_threadobj_set_is_thread_pool_thread(Il2CppObject* self, bool value)` | 0 |
| 50 | `icall_type_get_type_from_handle_unsafe` | `src/runtime/src/stubs.cpp:709` | `return nullptr; // stub` | 1 |
| 51 | `icall_web_client_ctor` | `src/runtime/src/http.cpp:637` | `void icall_web_client_ctor(Il2CppObject* self)` | 0 |
| 52 | `il2cpp_assembly_get_is_collectible` | `src/runtime/src/icallb.cpp:48` | `return 0; // AOT static assembly: never collectible` | 0 |
| 53 | `il2cpp_assembly_get_is_dynamic` | `src/runtime/src/icallb.cpp:43` | `return 0; // AOT static assembly: never dynamic (net48 Assembly.IsDynamic)` | 0 |
| 54 | `il2cpp_bitconverter_get_is_little_endian` | `src/runtime/src/bitconverter.cpp:377` | `return 1;` | 0 |
| 55 | `il2cpp_culture_get_invariant_lcid` | `src/runtime/src/cultureinfo.cpp:143` | `return 127;` | 0 |
| 56 | `il2cpp_debugger_break` | `src/runtime/src/debuggerext.cpp:29` | `void il2cpp_debugger_break()` | 0 |
| 57 | `il2cpp_debugger_is_attached` | `src/runtime/src/debuggerext.cpp:21` | `return 0;` | 1 |
| 58 | `il2cpp_debugger_is_logging` | `src/runtime/src/debuggerext.cpp:26` | `return 0;` | 1 |
| 59 | `il2cpp_dict_gen_enumerator_dispose` | `src/runtime/src/dictgen.cpp:894` | `void il2cpp_dict_gen_enumerator_dispose(Il2CppObject* self)` | 0 |
| 60 | `il2cpp_dict_si_enumerator_dispose` | `src/runtime/src/dictsi_obj.cpp:371` | `void il2cpp_dict_si_enumerator_dispose(Il2CppObject* self)` | 0 |
| 61 | `il2cpp_dict_si_keycollection_enumerator_dispose` | `src/runtime/src/dictsi_obj.cpp:433` | `void il2cpp_dict_si_keycollection_enumerator_dispose(Il2CppObject* self)` | 0 |
| 62 | `il2cpp_dict_si_valuecollection_enumerator_dispose` | `src/runtime/src/dictsi_obj.cpp:473` | `void il2cpp_dict_si_valuecollection_enumerator_dispose(Il2CppObject* self)` | 0 |
| 63 | `il2cpp_gc_get_is_server_gc` | `src/runtime/src/gc.cpp:2159` | `return false;` | 0 |
| 64 | `il2cpp_interlocked_speculation_barrier` | `src/runtime/src/interlocked.cpp:186` | `void il2cpp_interlocked_speculation_barrier()` | 1 |
| 65 | `il2cpp_list_i4_enumerator_dispose` | `src/runtime/src/listi4.cpp:1523` | `void il2cpp_list_i4_enumerator_dispose(Il2CppObject* self)` | 0 |
| 66 | `il2cpp_number_format_currency_decimal_digits` | `src/runtime/src/numberformat.cpp:1189` | `return 2;` | 0 |
| 67 | `il2cpp_number_format_currency_positive_pattern` | `src/runtime/src/numberformat.cpp:1194` | `return 0; // zh-CN && invariant: "¥n" (symbol precedes the value)` | 0 |
| 68 | `il2cpp_number_format_digit_substitution` | `src/runtime/src/numberformat.cpp:1262` | `return 1;` | 0 |
| 69 | `il2cpp_number_format_is_read_only` | `src/runtime/src/numberformat.cpp:1270` | `return true;` | 0 |
| 70 | `il2cpp_number_format_number_decimal_digits` | `src/runtime/src/numberformat.cpp:1184` | `return 2;` | 0 |
| 71 | `il2cpp_number_format_number_negative_pattern` | `src/runtime/src/numberformat.cpp:1179` | `return 1; // zh-CN && invariant: "-n" (leading minus)` | 0 |
| 72 | `il2cpp_number_format_percent_decimal_digits` | `src/runtime/src/numberformat.cpp:1217` | `return 2;` | 0 |
| 73 | `il2cpp_process_flush_stdin` | `src/runtime/src/pathproc.cpp:1247` | `void il2cpp_process_flush_stdin(int32_t pid)` | 0 |
| 74 | `il2cpp_process_get_stdin_autofush` | `src/runtime/src/pathproc.cpp:1257` | `return 1; // writes always flush (documented deviation from net48 default)` | 0 |
| 75 | `il2cpp_process_set_stdin_autofush` | `src/runtime/src/pathproc.cpp:1260` | `void il2cpp_process_set_stdin_autofush(int32_t pid, int32_t value)` | 0 |
| 76 | `il2cpp_sortedlist_enumerator_dispose` | `src/runtime/src/sortedlist.cpp:1037` | `void il2cpp_sortedlist_enumerator_dispose(Il2CppObject* self)` | 0 |
| 77 | `il2cpp_thread_join` | `src/runtime/src/threadext.cpp:26` | `void il2cpp_thread_join(int32_t thread_id)` | 0 |
| 78 | `il2cpp_thread_set_priority` | `src/runtime/src/threadext.cpp:34` | `void il2cpp_thread_set_priority(int32_t priority)` | 0 |
| 79 | `il2cpp_threadpool_set_max_threads` | `src/runtime/src/threadpool.cpp:312` | `void il2cpp_threadpool_set_max_threads(int32_t maxThreads)` | 0 |
| 80 | `il2cpp_typeinfo_is_contains_generic_params` | `src/runtime/src/typeinfo.cpp:2791` | `return 0;` | 0 |

逐条影响（命中 icall 的那些）：任何**依赖真值**的 net48 程序都会静默走错分支 ——
`Debugger.IsAttached`/`IsLogging` 恒 false、`GC.IsServerGC` 恒 false、
`Environment.HasShutdownStarted` 恒 false、`Marshal.GetExceptionPointers` 恒 0、
`StackTrace.GetStackFramesInternal` 填充 0 帧（栈回溯为空）、`Type.GetTypeFromHandleUnsafe` 恒 null、
`ReaderWriterLock.AnyWritersSince` 恒 false、`Thread.GetApartmentStateNative` 恒 0（MTA）。
处理原则见 §5.3 第 3 条：它们不排进 P0/P1 的「缺口」队列，但必须单独立项。

## 4. 与前次盘点（2026-08-06）的差异

旧文档 `docs/audit/icall-inventory-net48.md` 的核心结论：**910 条 InternalCall；public 面 121 个键；54 已接 / 67 缺口（42 域在方法缺 + 25 无域）**。以当前代码重扫后，这些数字**全部不再成立**。

### 4.1 总量

| 项目 | 旧结论（2026-08-06） | 本次实测 | 结论 |
|---|---|---|---|
| InternalCall 总数 | 910 | **908**（mscorlib 单程序集） | **推翻**：「910」是 7 个程序集的合计（mscorlib 908 + System.dll 2 个 COM 内部 ctor）。本审计的范围是 mscorlib，908 才是该程序集的真实总数。旧文档把一个跨程序集的合计当作 mscorlib 的数 |
| 双通道一致 | 910/910 | **908/908，join key 差异 0** | 改用精确 join key（类型+方法+签名+method/ctor）后一致 |
| 类型数 | 3356 | 反射 3354 / 元数据 3356 | 反射不可枚举 `<Module>` 与两个编译器行，均不含 icall |

### 4.2 接线结论（键级口径，与旧文档同粒度）

| 项目 | 旧结论 | 本次实测 | 结论 |
|---|---|---|---|
| public 面键数（owner+name 折叠，旧文档口径） | 121 | **121** | 保持（口径一致，可对齐比较：20 个键是同名重载折叠） |
| public 面键级已接（折叠口径） | 54 | **79** | **推翻**：增加 25 个键 |
| public 面键级缺口（折叠口径） | 67 | **42**（LOUD 25 + 半接 12 + 无接线 5） | **推翻**：减少 25 个键；但注意其中 12 个键是**恒值桩**（§3.8） |
| public 面行数（owner+name+签名，本版口径） | — | **141** | 同名重载各自成行，判定不再互相污染 |
| public 面行级已接（签名精确） | — | **99** | 与上面的键级数字不可直接相减 |
| public 面行级缺口（签名精确） | — | **42**（半接 12 + LOUD 25 + 无接线 5） | 剩余缺口的绝大多数是显式 LOUD 拒绝，不再是静默幽灵调用 |

### 4.3 被推翻的具体旧结论

**A. 旧文档 §5「最高优先级缺口（10 个）」—— 10 条全部被推翻**，当前代码均已接线：

| # | 旧文档认定 | 现状 | 证据 |
|---|---|---|---|
| 1 | `System.String..ctor` 缺 | 已接 — `new string(char, int)` → `icall_string_ctor_repeat` | `MethodMap.String.cs:15` |
| 2 | `System.String.IndexOfAny` 缺 | 已接 — → `icall_string_index_of_any_range` | `MethodMap.String.cs:92` |
| 3 | `System.String.LastIndexOfAny` 缺 | 已接 — → `icall_string_last_index_of_any_range` | `MethodMap.String.cs:114` |
| 4 | `System.Array.get_Rank` 缺 | 已接 — → `icall_array_get_rank` | `MethodMap.Array.cs:1096` |
| 5 | `System.ValueType.GetHashCode` 缺 | 已接 — → `icall_value_type_get_hash_code`（首字段分类） | `MethodMap.ValueType.cs:495` |
| 6 | `System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement` 缺 | 已接 — → `icall_marshal_unsafe_addr_of_pinned_array_element` | `MethodMap.Marshal.cs:421` |
| 7 | `System.Diagnostics.Debugger.get_IsAttached` 缺 | 半接 — → `il2cpp_debugger_is_attached`（**运行时是 `return 0;` 恒值桩，见 §3.8**） | `debuggerext.cpp:21` |
| 8 | `System.Runtime.InteropServices.Marshal.GetHRForException` 缺 | 已接 — → `icall_marshal_get_hr_for_exception` | `MethodMap.Marshal.cs:431` |
| 9 | `System.Runtime.InteropServices.Marshal.GetExceptionPointers` 缺 | 半接 — → `icall_marshal_get_exception_pointers`（**运行时是 `return 0;` 恒值桩，见 §3.8**） | `marshalext3.cpp:275` |
| 10 | `System.Runtime.InteropServices.SafeHandle.DangerousAddRef` 缺 | 已接 — SafeHandle 三项 → `icall_safehandle_dangerous_add_ref` 等 | `MethodMap.SafeHandle.cs:40` |

**B. 旧文档 §4 分域缺口表 —— 11 条具体条目被推翻**：

| 旧文档结论 | 现状 | 证据 |
|---|---|---|
| `System.Runtime.InteropServices.SafeHandle.DangerousAddRef` 称缺口 | 已接 | `MethodMap.SafeHandle.cs:40` |
| `System.Runtime.InteropServices.SafeHandle.DangerousRelease` 称缺口 | 已接 | `MethodMap.SafeHandle.cs:48` |
| `System.Runtime.InteropServices.SafeHandle.SetHandleAsInvalid` 称缺口 | 已接 | `MethodMap.SafeHandle.cs:52` |
| `System.Enum.Equals` 称缺口 | 已接 | `MethodMap.Enum.cs:363` |
| `System.Type.op_Inequality` 称缺口 | 已接 | `MethodMap.Type.cs:549` |
| `System.Threading.Interlocked.SpeculationBarrier` 称缺口 | 半接 | `interlocked.cpp:186` |
| `System.Threading.Thread.MemoryBarrier` 称缺口 | 已接 | `MethodMap.Thread.cs:63` |
| `System.Threading.Thread.get_IsThreadPoolThread` 称缺口 | 已接 | `MethodMap.Thread.cs:105` |
| `System.Array.get_LongLength` 称缺口 | 已接 | `MethodMap.Array.cs:1110` |
| `System.Array.GetLowerBound` 称缺口 | 已接 | `MethodMap.Array.cs:1114` |
| `System.Array.Initialize` 称缺口 | 已接 | `MethodMap.Array.cs:1118` |

**C. 旧文档 §6(b)「JIT/转译器已有语义，不算缺口」—— 2 条被推翻、4 条成立**：

- `Thread.MemoryBarrier` / `Interlocked.SpeculationBarrier`：旧文档称「空实现即可」——**推翻**，两者都已是真实运行时 icall（`icall_thread_memory_barrier`、`il2cpp_interlocked_speculation_barrier`）。但 `SpeculationBarrier` 的实现本身是空体，按本版口径是**半接**（§3.8），所以「不算缺口」这个结论仍然不成立。
- `RuntimeHelpers.InitializeArray`：旧文档称折叠即可、不算缺口 —— **部分推翻**。折叠路径存在（`StackFold.FoldInitializeArray`），但未折叠的形状现在被显式 LOUD 拒绝（`MethodMap.RuntimeHelpers.cs:108`）。
- `Type.GetTypeFromHandle`：旧文档称已接 —— **成立**（`MethodMap.Type.cs:73`）。
- `RuntimeHelpers.GetHashCode` / `Equals` / `GetObjectValue`：旧文档称可折叠 —— **成立**（`Equals`/`GetObjectValue` 走 `::refeq::` / `::identity::` 折叠标记，`GetHashCode` 走真实 icall）。

**D. 旧文档 §7「内部 icall 764 个键由 public 包装承接」—— 口径被推翻**：
严格按「icall 自身无接线、且最短 public 入口已接线且其重载组全部已接线」验证，只有 **74** 条成立（另有 16 条只部分成立），而不是按 owner 类型一概而论的 764。其余内部 icall 要么自身被接线，要么就是真缺口。

### 4.4 仍成立的旧结论（未被推翻）

- 「内部 icall 大多由 public 包装承接」这一**机制判断成立**（只是数量被高估，见 D）。
- `System.Runtime.Remoting.*` / `AppDomain` / `ArgIterator` / `ReaderWriterLock` 属 legacy：**成立**，当前全部为显式 LOUD 拒绝或恒值桩。
- Marshal 的 COM 面不建议实现：**成立**，当前是显式 LOUD 拒绝并给出「ILC 没有 COM 运行时」的理由。
- `System.Runtime.InteropServices.Marshal` 占缺口最大头：**成立**，当前仍是最大单一来源。

### 4.5 被推翻条数统计

| 类别 | 条数 |
|---|---|
| 总量/口径错误（910 当 mscorlib 总数、754 内部承接口径） | 2 |
| §5 最高优先级缺口被推翻 | 10 |
| §4 分域缺口条目被推翻 | 11 |
| §6(b)「已有语义」被推翻 | 2 |
| **合计** | **25** |

### 4.6 本轮修订（相对上一版本文档）

上一版本 doc 的数字（已接 224 / 半接 1 / LOUD 297 / 影子 65 / 无接线 321，共 908）在四处口径上是错的，本版逐条修正：

| 修订 | 旧值 | 新值 | 根因 |
|---|---|---|---|
| Q1 影子口径 | 影子 65（把 publicRoot 也塞进 public_faces，得 507 个假 public 面） | 影子 74 + 部分影子 16 | public face 必须只含真 public-face icall；影子必须标注跳数；泛型重载未映射的判部分影子 |
| M1 恒值桩 | 已接 224 | 已接 194 / 半接 30 | 空体与 `return <常量>;` 也是桩，全表重扫出 30 条（远超点名的 4 条） |
| M2 loud/half→shadow | LOUD 297 | LOUD 281 | 17 条 loud/half 的运行时分支实为死代码（depth-1 public 调用者全部检出真实实现） |
| Q3 P2 降级 | P2 173 | P2 98 | 命名空间不得压过 `publicDistance == 1`；前缀匹配必须按类型全名 |

## 5. 下一步建议（P0/P1 优先队列）

### 5.1 P0（42 条，用户可直接调用）

| 声明类型 | 方法 | 判定 | 建议 | 证据 |
|---|---|---|---|---|
| `System.ArgIterator` | `GetRemainingCount` | 缺（LOUD 拒绝） | vararg 语义，保持 LOUD | `MethodMap.Legacy.cs:232` |
| `System.Diagnostics.Debugger` | `IsLogging` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `debuggerext.cpp:26` |
| `System.Diagnostics.Debugger` | `get_IsAttached` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `debuggerext.cpp:21` |
| `System.Environment` | `get_HasShutdownStarted` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `environment.cpp:531` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `EnsureSufficientExecutionStack` | 缺（LOUD 拒绝） | AOT 无托管栈上限，保持 LOUD 即可；若要「可跑」可返回空实现 | `MethodMap.RuntimeHelpers.cs:81` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `ExecuteCodeWithGuaranteedCleanup` | 缺（LOUD 拒绝） | CER 无语义，保持 LOUD；或在 try/finally 上做等价降级 | `MethodMap.RuntimeHelpers.cs:89` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `InitializeArray` | 缺（LOUD 拒绝） | 折叠已覆盖常见形状；剩余形状保持 LOUD（不要改成幽灵调用） | `MethodMap.RuntimeHelpers.cs:108` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `PrepareContractedDelegate` | 缺（LOUD 拒绝） | JIT 提示，AOT 下空实现即可（可降级为 wired） | `MethodMap.RuntimeHelpers.cs:77` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `PrepareDelegate` | 缺（LOUD 拒绝） | 同上，JIT 提示，空实现即可 | `MethodMap.RuntimeHelpers.cs:73` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `ProbeForSufficientStack` | 缺（LOUD 拒绝） | 同上，空实现即可 | `MethodMap.RuntimeHelpers.cs:85` |
| `System.Runtime.InteropServices.ExtensibleClassFactory` | `RegisterObjectCreationCallback` | 缺（无接线） | 需单独评估 | `no wiring` |
| `System.Runtime.InteropServices.Marshal` | `AddRef` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `AreComObjectsAvailableForCleanup` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `ChangeWrapperHandleStrength` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `CleanupUnusedObjectsInCurrentContext` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `CreateAggregatedObject` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetEndComSlot` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetExceptionCode` | 半接 | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `marshalext3.cpp:265` |
| `System.Runtime.InteropServices.Marshal` | `GetExceptionPointers` | 半接 | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `marshalext3.cpp:275` |
| `System.Runtime.InteropServices.Marshal` | `GetITypeInfoForType` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetManagedThunkForUnmanagedMethodPtr` | 缺（无接线） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `no wiring` |
| `System.Runtime.InteropServices.Marshal` | `GetMethodInfoForComSlot` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetStartComSlot` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetTypeLibLcid` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetTypedObjectForIUnknown` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `GetUniqueObjectForIUnknown` | 缺（无接线） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `no wiring` |
| `System.Runtime.InteropServices.Marshal` | `GetUnmanagedThunkForManagedMethodPtr` | 缺（无接线） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `no wiring` |
| `System.Runtime.InteropServices.Marshal` | `IsTypeVisibleFromCom` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.InteropServices.Marshal` | `QueryInterface` | 缺（无接线） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `no wiring` |
| `System.Runtime.InteropServices.Marshal` | `Release` | 缺（LOUD 拒绝） | COM/interop 面，保持 LOUD（明确不支持优于幽灵调用） | `MethodMap.Marshal.cs:86` |
| `System.Runtime.Remoting.Proxies.RealProxy` | `GetProxiedType` | 缺（LOUD 拒绝） | legacy remoting，保持 LOUD | `MethodMap.Legacy.cs:44` |
| `System.Runtime.Remoting.Proxies.RealProxy` | `GetStubData` | 缺（LOUD 拒绝） | legacy remoting，保持 LOUD | `MethodMap.Legacy.cs:44` |
| `System.Runtime.Remoting.Proxies.RealProxy` | `SetStubData` | 缺（LOUD 拒绝） | legacy remoting，保持 LOUD | `MethodMap.Legacy.cs:44` |
| `System.Runtime.Remoting.RemotingServices` | `GetRealProxy` | 缺（LOUD 拒绝） | legacy remoting，保持 LOUD | `MethodMap.Legacy.cs:44` |
| `System.Runtime.Remoting.RemotingServices` | `IsTransparentProxy` | 缺（LOUD 拒绝） | legacy remoting，保持 LOUD | `MethodMap.Legacy.cs:44` |
| `System.Threading.Interlocked` | `SpeculationBarrier` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `interlocked.cpp:186` |
| `System.Threading.ReaderWriterLock` | `AnyWritersSince` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `stubs.cpp:1210` |
| `System.Threading.Thread` | `BeginCriticalRegion` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `thread.cpp:120` |
| `System.Threading.Thread` | `BeginThreadAffinity` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `thread.cpp:184` |
| `System.Threading.Thread` | `DisableComObjectEagerCleanup` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `thread.cpp:199` |
| `System.Threading.Thread` | `EndCriticalRegion` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `thread.cpp:124` |
| `System.Threading.Thread` | `EndThreadAffinity` | 半接 | 替换恒值桩为真实实现（当前静默返回常量） | `thread.cpp:188` |

### 5.2 P1（219 条，常见 BCL API 间接可达）

按属主聚合，优先处理「用户高频 API 会走到的内部 icall」：

| 属主 | 条数 | 说明 |
|---|---|---|
| `System.Runtime.InteropServices.Marshal` | 22 | FCallGenerateGuidForType, FCallGetTypeLibGuid, FCallGetTypeLibGuidForAssembly, GetComInterfaceForObjectNative … |
| `System.Threading.Thread` | 17 | AbortInternal, ClearAbortReason, GetAbortReason, GetApartmentStateNative … |
| `System.RuntimeMethodHandle` | 15 | CheckLinktimeDemands, GetAttributes, GetDeclaringType, GetLoaderAllocator … |
| `System.RuntimeTypeHandle` | 14 | Allocate, CompareCanonicalHandles, CreateCaInstance, CreateInstance … |
| `System.Threading.ReaderWriterLock` | 12 | AcquireReaderLockInternal, AcquireWriterLockInternal, DowngradeFromWriterLockInternal, FCallReleaseLock … |
| `System.AppDomain` | 10 | GetDynamicDir, nApplyPolicy, nChangeSecurityPolicy, nEnableMonitoring … |
| `System.Exception` | 8 | CopyDynamicMethods, CopyStackTrace, GetMethodFromStackTrace, GetStackTracesDeepCopy … |
| `System.Security.Cryptography.Utils` | 8 | _DecryptData, _EncryptData, _ExportKey, _GetEnforceFipsPolicySetting … |
| `System.Security.Cryptography.X509Certificates.X509Utils` | 6 | _GetIssuerName, _GetPublicKeyOid, _GetPublicKeyParameters, _GetPublicKeyValue … |
| `System.Threading.CompressedStack` | 6 | DestroyDCSList, GetDCSCount, GetDelayedCompressedStack, GetDomainCompressedStack … |
| `System.ArgIterator` | 5 | .ctor, .ctor, FCallGetNextArg, InternalGetNextArg … |
| `System.Threading.ThreadPool` | 5 | BindIOCompletionCallbackNative, PostQueuedCompletionStatus, RegisterWaitForSingleObjectNative, SetMaxThreadsNative … |
| `System.Delegate` | 4 | BindToMethodInfo, BindToMethodName, FindMethodHandle, InternalAlloc |
| `System.Globalization.CultureData` | 4 | LCIDToLocaleName, LocaleNameToLCID, nativeGetNumberFormatInfoValues, nativeInitCultureData |
| `System.Reflection.AssemblyName` | 4 | EscapeCodeBase, ReferenceMatchesDefinitionInternal, nGetFileInformation, nGetPublicKeyToken |
| `System.RuntimeFieldHandle` | 4 | AcquiresContextFromThis, GetApproxDeclaringType, GetValue, SetValue |
| `System.RuntimeType` | 4 | AllocateValueType, CanValueSpecialCast, GetTypeFromCLSIDImpl, GetTypeFromProgIDImpl |
| `System.Decimal` | 3 | FCallCompare, FCallRound, FCallToCurrency |
| `System.GC` | 3 | IsServerGC, _WaitForFullGCApproach, _WaitForFullGCComplete |
| `System.ModuleHandle` | 3 | GetDynamicMethod, GetMDStreamVersion, _GetMetadataImport |
| `System.ParseNumbers` | 3 | IntToString, StringToInt, StringToLong |
| `System.Runtime.CompilerServices.RuntimeHelpers` | 3 | _PrepareMethod, _RunClassConstructor, _RunModuleConstructor |
| `System.Threading.HostExecutionContextManager` | 3 | CaptureHostSecurityContext, HostSecurityManagerPresent, SetHostSecurityContext |
| `System.Threading.Monitor` | 3 | IsEnteredNative, ObjWait, ReliableEnterTimeout |
| `System.Threading.OverlappedData` | 3 | AllocateNativeOverlapped, FreeNativeOverlapped, GetOverlappedFromNative |
| `System.Threading.WaitHandle` | 3 | SignalAndWaitOne, WaitMultiple, WaitOneNative |
| `System.TypedReference` | 3 | InternalMakeTypedReference, InternalSetTypedReference, InternalToObject |
| `System.Diagnostics.Debugger` | 2 | BreakInternal, LaunchInternal |
| `System.Globalization.CalendarData` | 2 | nativeGetCalendars, nativeGetTwoDigitYearMax |
| `System.Number` | 2 | NumberBufferToDecimal, NumberBufferToDouble |
| `System.Reflection.RuntimeAssembly` | 2 | nLoadFile, nLoadImage |
| `System.Runtime.InteropServices.RegistrationServices` | 2 | RegisterTypeForComClientsExNative, RegisterTypeForComClientsNative |
| `System.Runtime.InteropServices.TypeLibConverter` | 2 | nConvertAssemblyToTypeLib, nConvertTypeLibToMetadata |
| `System.Runtime.Serialization.FormatterServices` | 2 | nativeGetSafeUninitializedObject, nativeGetUninitializedObject |
| `System.Runtime.Versioning.CompatibilitySwitch` | 2 | GetValueInternalCall, IsEnabledInternalCall |
| `System.Threading.Interlocked` | 2 | _CompareExchange, _Exchange |
| `System.ValueType` | 2 | CanCompareBits, FastEqualsCheck |
| `System.AppDomainSetup` | 1 | UpdateContextProperty |
| `System.Array` | 1 | InternalCreate |
| `System.BCLDebug` | 1 | GetRegistryLoggingValues |
| `System.Currency` | 1 | FCallToDecimal |
| `System.CurrentSystemTimeZone` | 1 | nativeGetTimeZoneMinuteOffset |
| `System.Enum` | 1 | InternalGetCorElementType |
| `System.Globalization.CultureInfo` | 1 | InternalGetSystemDefaultUILanguage |
| `System.Globalization.TextInfo` | 1 | InternalGetCaseInsHash |
| `System.MarshalByRefObject` | 1 | GetComIUnknown |
| `System.Math` | 1 | SplitFractionDouble |
| `System.MissingMemberException` | 1 | FormatSignature |
| `System.Runtime.InteropServices.ArrayWithOffset` | 1 | CalculateCount |
| `System.Runtime.InteropServices.RuntimeEnvironment` | 1 | GetRuntimeDirectoryImpl |
| `System.Runtime.Remoting.Messaging.Message` | 1 | OutToUnmanagedDebugger |
| `System.Runtime.Remoting.RemotingServices` | 1 | CallDefaultCtor |
| `System.Runtime.Versioning.VersioningHelper` | 1 | GetRuntimeId |
| `System.Security.CodeAccessSecurityEngine` | 1 | QuickCheckForAllDemands |
| `System.Signature` | 1 | GetSignature |
| `System.Threading.RegisteredWaitHandleSafe` | 1 | UnregisterWaitNative |
| `System.Type` | 1 | GetTypeFromHandleUnsafe |
| `System.WeakReference` | 1 | IsTrackResurrection |

### 5.3 处理原则

1. **P0 里的 LOUD 拒绝优先转成「明确的运行时语义」**：`RuntimeHelpers.PrepareDelegate` / `PrepareContractedDelegate` / `ProbeForSufficientStack` / `EnsureSufficientExecutionStack` 在 AOT 下没有实际工作要做，空实现即语义正确，比转译期直接拒绝更好。
2. **P0 里的 COM/remoting 保持 LOUD**：设计上就不支持，转译期失败远好于编译期幽灵符号。
3. **恒值桩单独列一类**（§3.8）：它们不是「缺口」也不是「已接」，而是静默偏差。按 net48 byte-exact 的要求，`Debugger.IsAttached` / `GC.IsServerGC` / `Marshal.GetExceptionPointers` 这类必须给出真实语义（或至少让偏差可观测），不能永远返回常量。
4. **P1 按属主成批推进**：同一 `MethodMap.*.cs` 域的分支集中加，避免反复触碰同一文件。
5. **`缺（无接线）` 的 313 条里 P3 占 202 条**，多数是 CLR 内部（`System.StubHelpers.*`、`System.Reflection.MetadataImport`、`System.Signature`）或 legacy（`System.Mda`、`System.Variant`），不建议排期。

## 6. 红证（可执行复现）

以下都是**实跑**结果，不是代码推断。

### 6.1 旧缺口确已转译（先红后绿）

构造一个 net48 程序，覆盖旧文档 §5 里点名“缺”的 6 条：

```csharp
new string('*', 5);                        // String..ctor(char,int)
"abcdef".IndexOfAny(stops, 0, 6);          // String.IndexOfAny
"abcdef".LastIndexOfAny(stops, 5, 6);      // String.LastIndexOfAny
grid.Rank;                                 // Array.get_Rank
grid.GetLowerBound(1);                     // Array.GetLowerBound
p.GetHashCode();                           // ValueType.GetHashCode
Marshal.UnsafeAddrOfPinnedArrayElement(buf, 0); // Marshal pin addr
```

il2cppc 转译后，gen/src/Probe.cpp 里实际生成的调用（摘录）：

```cpp
t0 = il2cpp::runtime::icall_string_ctor_repeat(t0, (char16_t)42, (int32_t)5);
il2cpp::runtime::icall_string_index_of_any_range(g_strPool[0], loc0, 0, 6)
il2cpp::runtime::icall_string_last_index_of_any_range(g_strPool[0], loc0, 5, 6)
il2cpp::runtime::icall_array_get_rank(t0)
il2cpp::runtime::icall_array_get_lower_bound(t0, (int32_t)1)
il2cpp::runtime::icall_int32_get_hash_code((*reinterpret_cast<int32_t*>(&loc1)))
il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element((Il2CppArray*)t0, 0)
```

stub-report 为空 —— 整个方法转译成功。**这就是旧结论被推翻的直接证据。**

### 6.2 LOUD 拒绝真的会拦下整个方法

把程序改成只调用 RuntimeHelpers.PrepareDelegate(null)，转译输出：

```
warning: method fold failed -> skipped (Probe::Main: System.Runtime.CompilerServices.RuntimeHelpers::PrepareDelegate:
  LOUD whitelist (no machine/JIT state): ... )
warning: 1 method(s) were not transpiled (see --stub-report)
```

stub-report 内容为 Probe::Main/0#0x06000001。也就是说 §3 里的「缺（LOUD 拒绝）」不是静默的：
转译期就失败并留下可核对的报告，而不是生成一个编译期才炸的幽灵符号。

### 6.3 「无接线」的失效时机：**编译期**，不是链接期

旧版文档说这类失效「只有到 C++ 链接期才会失败」。实测更早：转译照常成功、**没有任何 warning**、`--stub-report` 也没有条目，幽灵符号被写进生成的 C++，然后 `cmake --build` 在**编译单元**上以 **rc=2** 失败 —— 同样不会走到链接期。

复现用的 net48 程序（`csc.exe /target:exe`，Framework 4.8）：

```csharp
using System;
using System.Runtime.InteropServices;

internal static class Q2Marshal
{
    private static void Main()
    {
        var p = new IntPtr(0x1234);
        var o = Marshal.GetUniqueObjectForIUnknown(p);
        Console.WriteLine(o == null ? "null" : "obj");
    }
}
```

`il2cppc gen6_in gen6_out` 的完整转译输出（没有任何 warning，也没有 stub）：

```
symbols: no Portable PDB found -> no #line C# line directives
string pool: 2 distinct literal(s) from 2 site(s) across 1 method(s) ...
resources: 0 embedded manifest resource(s) -> include/internal/resource-pool.h
licm: 0 invariant length read(s) hoistable across 1 method(s)
inline: 0 simple call site(s) inlineable across 1 method(s)
tempslot: 4 temp slot(s) -> 4 with reuse across 1 method(s)
generated 1 type(s) -> gen6_out
```

生成的 `gen6_out/src/Q2Marshal.cpp` 第 77 行把调用直接写成幽灵符号（源码里没有这个函数）：

```cpp
loc1 = reinterpret_cast<Il2CppObject*>(System_Runtime_InteropServices_Marshal_GetUniqueObjectForIUnknown_6d094451fafe8f02(loc0)); // IL stloc.1 @0x0014 -- store call result into loc1
```

`cmake -S gen6_out -B build6 -G "MinGW Makefiles" -DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64.cmake` 配置成功，`cmake --build build6 --target il2cpp_gen_exe` 的**实际**输出（rc=2）：

```
C:\...\gen6_out\src\Q2Marshal.cpp: In function 'void Q2Marshal_Main_96da4d79b4e56284()':
C:\...\gen6_out\src\Q2Marshal.cpp:77:44: error:
  'System_Runtime_InteropServices_Marshal_GetUniqueObjectForIUnknown_6d094451fafe8f02'
  was not declared in this scope
   77 |     loc1 = reinterpret_cast<Il2CppObject*>(
      |                                            ^~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
mingw32-make[3]: *** [CMakeFiles\il2cpp_gen.dir\build.make:79: CMakeFiles\il2cpp_gen.dir/src/Q2Marshal.cpp.obj] Error 1
mingw32-make[2]: *** [CMakeFiles\Makefile2:114: CMakeFiles\il2cpp_gen.dir/all] Error 2
mingw32-make[1]: *** [CMakeFiles\Makefile2:186: CMakeFiles\il2cpp_gen.dir/rule] Error 2
mingw32-make: *** [Makefile:195: il2cpp_gen_exe] Error 2
```

同一条「无接线」还有第二种**更早**的失败形态：当调用形状能让折叠器拿到接收者类型提示时，M5 phantom guard（`src/il2cppc/Emit/MethodRefResolver.cs:1390-1408`）在**转译期**就把它变成 LOUD，连幽灵符号都不产出。把上面程序的 `var p = new IntPtr(0x1234);` 改成内联实参`Marshal.GetUniqueObjectForIUnknown(new IntPtr(0x1234))` 即触发：

```
warning: method fold failed -> skipped (Q2Probe::Main: System.Runtime.InteropServices.Marshal::GetUniqueObjectForIUnknown:
  ... not wired in M5 (BCL owner has no mapping arm and is not defined in the input assembly --
  a phantom generated-name call would never link; receiverType='System.IntPtr' arg0Type='System.IntPtr'))
warning: 1 method(s) were not transpiled (see --stub-report)
```

**结论**：§3 的 313 条「无接线」不是「最晚才炸」的一类，而是**最早**就炸的一类 —— 要么在转译期被 LOUD 拦下（带接收者提示的形状），要么在 C++ 编译期以 `was not declared in this scope` 失败（无提示的形状，实测 rc=2、转译零 warning）。两种形态都不会跑到链接期，更不会静默产出可执行文件。

## 7. 复现方式

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/run-audit.ps1
```

脚本按顺序执行 9 个阶段（反射通道 → 元数据通道 → join key 交叉校验 → 调用图 → 真实 BclResolve 探针 → 运行时符号索引 → dispatcher 分支解析 → 证据行定位 → 分类 → 渲染 → 证据自校验），产物写入 `-OutDir`（默认 `%TEMP%\opencode\icall-audit`），最终数据为 `classified.json`。

任一阶段失败（通道数量不等、五类之和 ≠ 908、文档里任何一处 `file:line` 对不上）脚本都会非零退出，不会发布一份数字不自洽的报告。

各阶段工具：

| 工具 | 作用 |
|---|---|
| `tools/audit/enumerate-icall-reflection.ps1` | 通道 1：运行时反射枚举 icall（PS 5.1） |
| `tools/audit/MetadataIcallCounter/` | 通道 2：ECMA-335 元数据位枚举 |
| `tools/audit/IcallCallGraph/` | IL 调用图（按签名精确成边）+ 从 public 面的可达距离/链路 |
| `tools/audit/IcallWiringProbe/` | 逐条调用真实 `MethodRefResolver.BclResolve` |
| `tools/audit/parse_runtime_syms.py` | `src/runtime` 的声明/定义/LOUD 桩/**恒值桩**索引 |
| `tools/audit/parse_wiring_surface.py` | `MethodMap.*.cs` 的 dispatcher 分支解析（含分支行号） |
| `tools/audit/locate_wiring_sites.py` | 每条判定对应的源码行 |
| `tools/audit/classify_icalls.py` | 最终判定（含影子跳数/部分影子）+ P0..P3 + `assert sum==908` |
| `tools/audit/render_report.py` | 本文档渲染（数字全部来自 `classified.json`） |
| `tools/audit/verify_report.py` | 文档内每一处 `file:line` 的自校验 |
| `tools/audit/shared/SignatureProvider.cs` | 两通道共用的签名渲染器（保证 join key 不漂移） |
