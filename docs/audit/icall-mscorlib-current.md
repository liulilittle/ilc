# mscorlib InternalCall 现状盘点（2026-10-31）

> 只读审计。以仓库 **当前** 代码（`c3a6b2e3`）为准重新生成，不引用任何历史数字。
> 目标程序集：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll`（net48）。

## 0. 结论摘要

- net48 mscorlib InternalCall 总数：**908**（反射通道 908 / 元数据通道 908，逐条 join key 一致）
- 判定分布：已接 **224**、半接 **1**、缺 **618**（LOUD 拒绝 297 + 无接线 321）、影子 **65**
- 可达性分级（仅统计非「已接」的 684 条）：P0 **30**、P1 **198**、P2 **173**、P3 **283**
- public API 面（类型 public 且方法 public）共 **141** 条，其中已接 **111**、LOUD 25、无接线 5

### 0.1 判定口径（逐条都在 §2/§3 给出 file:line）

| 判定 | 含义 | 证据形态 |
|---|---|---|
| 已接 | 转译器的 `MethodRefResolver.BclResolve` 对这条 icall 返回了 C++ 目标，且该目标在 `src/runtime` 有真实实现（非 `*_loud` 抛异常桩）；折叠标记（`::identity::` 等）视为已接，因为发射器直接内联 | `MethodMap.*.cs:<line>`（dispatcher 分支）+ 运行时定义 |
| 半接 | 有 dispatch 目标但运行时实现是 `*_loud` 抛异常桩（调用必然抛） | `MethodMap.*.cs:<line>` + 桩定义 |
| 缺（LOUD 拒绝）| 转译器对该成员显式抛 `NotSupportedException`，并在转译期把整个方法标记为未转译（不是静默错误） | `MethodMap.*.cs:<line>`（throw 点）+ 原始消息 |
| 缺（无接线）| 没有 owner 分支也没有方法分支，落到生成的幽灵函数名（`System_Xxx_<hash>`），C++ 链接期失败 | `no wiring` |
| 影子 | 该 icall 自身无接线，但它的**直接调用者**是 public 方法且该 public 方法已接线，用户代码只发出那一次调用，icall 体不会被转译 | 承接的 public 方法 + 其 `MethodMap.*.cs:<line>` |

可达性分级（对非「已接」行）：

| 级别 | 定义 |
|---|---|
| P0 | 类型 public 且方法 public —— 用户 IL 可直接 call |
| P1 | 非 public 面，但存在 public 入口到它的调用链（≤3 跳），且属主不在 P2 命名空间 |
| P2 | 只能经 reflection / remoting / interop / diagnostics / serialization / security / AppDomain / CompilerServices 到达，或链路深度 ≥4 |
| P3 | mscorlib 内部无任何 public 入口可达（legacy / CLR 内部 / 已废弃） |

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

可达性另有独立证据：`tools/audit/IcallCallGraph` 解码 mscorlib 全部托管方法体（29257 个方法，解码失败 0 个）建立调用图，从 public 面做前向 BFS，得到「该 icall 到最近 public 入口的距离 / 该入口是谁」。

## 2. 已接清单

共 **224** 条。证据列是 dispatcher 的 `MethodMap.*.cs:<line>`，即 `BclResolveCore` 的 owner 分支最终命中的 `TryMap*` 里那个方法名分支。

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
| 49 | `System.Delegate` | `FindMethodHandle` | `System.IRuntimeMethodInfo ()` | public/internal/instance | `icall_delegate_find_method_handle` | `MethodMap.Delegate.cs:107` |
| 50 | `System.Delegate` | `GetCallStub` | `System.IntPtr (System.IntPtr)` | public/internal/instance | `icall_delegate_get_call_stub` | `MethodMap.Delegate.cs:110` |
| 51 | `System.Delegate` | `GetInvokeMethod` | `System.IntPtr ()` | public/internal/instance | `icall_delegate_get_invoke_method` | `MethodMap.Delegate.cs:106` |
| 52 | `System.Delegate` | `GetMulticastInvoke` | `System.IntPtr ()` | public/internal/instance | `icall_delegate_get_multicast_invoke` | `MethodMap.Delegate.cs:105` |
| 53 | `System.Delegate` | `InternalAllocLike` | `System.MulticastDelegate (System.Delegate)` | public/internal/static | `icall_delegate_internal_alloc_like` | `MethodMap.Delegate.cs:104` |
| 54 | `System.Delegate` | `InternalEqualMethodHandles` | `System.Boolean (System.Delegate, System.Delegate)` | public/internal/static | `icall_delegate_internal_equal_method_handles` | `MethodMap.Delegate.cs:108` |
| 55 | `System.Delegate` | `InternalEqualTypes` | `System.Boolean (System.Object, System.Object)` | public/internal/static | `icall_delegate_internal_equal_types` | `MethodMap.Delegate.cs:112` |
| 56 | `System.Diagnostics.Assert` | `ShowDefaultAssertDialog` | `System.Int32 (System.String, System.String, System.String, System.String)` | internal/internal/static | `icall_assert_show_default_assert_dialog` | `MethodMap.GC.cs:264` |
| 57 | `System.Diagnostics.Debugger` | `IsLogging` | `System.Boolean ()` | public/public/static | `il2cpp_debugger_is_logging` | `MethodMap.Debugger.cs:44` |
| 58 | `System.Diagnostics.Debugger` | `Log` | `System.Void (System.Int32, System.String, System.String)` | public/public/static | `il2cpp_debugger_log` | `MethodMap.Debugger.cs:52` |
| 59 | `System.Diagnostics.Debugger` | `get_IsAttached` | `System.Boolean ()` | public/public/static | `il2cpp_debugger_is_attached` | `MethodMap.Debugger.cs:40` |
| 60 | `System.Diagnostics.Log` | `AddLogSwitch` | `System.Void (System.Diagnostics.LogSwitch)` | internal/internal/static | `icall_log_add_log_switch` | `MethodMap.GC.cs:265` |
| 61 | `System.Diagnostics.Log` | `ModifyLogSwitch` | `System.Void (System.Int32, System.String, System.String)` | internal/internal/static | `icall_log_modify_log_switch` | `MethodMap.GC.cs:266` |
| 62 | `System.Diagnostics.StackTrace` | `GetStackFramesInternal` | `System.Void (System.Diagnostics.StackFrameHelper, System.Int32, System.Boolean, System.Exception)` | public/internal/static | `icall_stack_trace_get_stack_frames_internal` | `MethodMap.GC.cs:138` |
| 63 | `System.Enum` | `Equals` | `System.Boolean (System.Object)` | public/public/instance | `il2cpp_enum_equals` | `MethodMap.Enum.cs:363` |
| 64 | `System.Enum` | `InternalGetUnderlyingType` | `System.RuntimeType (System.RuntimeType)` | public/internal/static | `icall_enum_internal_get_underlying_type` | `MethodMap.Enum.cs:425` |
| 65 | `System.Environment` | `FailFast` | `System.Void (System.String)` | public/public/static | `icall_environment_fail_fast` | `MethodMap.Environment.cs:126` |
| 66 | `System.Environment` | `FailFast` | `System.Void (System.String, System.Exception)` | public/public/static | `icall_environment_fail_fast` | `MethodMap.Environment.cs:126` |
| 67 | `System.Environment` | `FailFast` | `System.Void (System.String, System.UInt32)` | public/internal/static | `icall_environment_fail_fast` | `MethodMap.Environment.cs:126` |
| 68 | `System.Environment` | `GetCompatibilityFlag` | `System.Boolean (System.CompatibilityFlag)` | public/internal/static | `icall_environment_get_compatibility_flag` | `MethodMap.Environment.cs:155` |
| 69 | `System.Environment` | `GetResourceFromDefault` | `System.String (System.String)` | public/internal/static | `icall_environment_get_resource_from_default` | `MethodMap.Environment.cs:154` |
| 70 | `System.Environment` | `GetVersion` | `System.Boolean (Microsoft.Win32.Win32Native+OSVERSIONINFO)` | public/internal/static | `icall_environment_get_version` | `MethodMap.Environment.cs:152` |
| 71 | `System.Environment` | `get_ExitCode` | `System.Int32 ()` | public/public/static | `icall_environment_get_exit_code` | `MethodMap.Environment.cs:117` |
| 72 | `System.Environment` | `get_HasShutdownStarted` | `System.Boolean ()` | public/public/static | `icall_environment_get_has_shutdown_started` | `MethodMap.Environment.cs:36` |
| 73 | `System.Environment` | `get_TickCount` | `System.Int32 ()` | public/public/static | `icall_environment_get_tick_count` | `MethodMap.Environment.cs:32` |
| 74 | `System.Environment` | `get_TickCount64` | `System.Int64 ()` | public/internal/static | `icall_environment_get_tick_count64` | `MethodMap.Environment.cs:39` |
| 75 | `System.Environment` | `nativeGetEnvironmentVariable` | `System.String (System.String)` | public/internal/static | `icall_environment_get_environment_variable_native` | `MethodMap.Environment.cs:139` |
| 76 | `System.Environment` | `set_ExitCode` | `System.Void (System.Int32)` | public/public/static | `icall_environment_set_exit_code` | `MethodMap.Environment.cs:118` |
| 77 | `System.Exception` | `SaveStackTracesFromDeepCopy` | `System.Void (System.Exception, System.Object, System.Object)` | public/internal/static | `icall_exception_save_stack_traces_from_deep_copy` | `MethodMap.Exception.cs:261` |
| 78 | `System.GC` | `GetGCLatencyMode` | `System.Int32 ()` | public/internal/static | `icall_gc_get_latency_mode` | `MethodMap.GC.cs:79` |
| 79 | `System.GC` | `GetGeneration` | `System.Int32 (System.Object)` | public/public/static | `icall_gc_get_generation` | `MethodMap.GC.cs:54` |
| 80 | `System.GC` | `GetLOHCompactionMode` | `System.Int32 ()` | public/internal/static | `icall_gc_get_loh_compaction_mode` | `MethodMap.GC.cs:82` |
| 81 | `System.GC` | `GetMaxGeneration` | `System.Int32 ()` | public/private/static | `il2cpp_gc_get_max_generation` | `MethodMap.GC.cs:117` |
| 82 | `System.GC` | `IsServerGC` | `System.Boolean ()` | public/internal/static | `icall_gc_is_server_gc` | `MethodMap.GC.cs:84` |
| 83 | `System.GC` | `SetGCLatencyMode` | `System.Int32 (System.Int32)` | public/internal/static | `icall_gc_set_latency_mode` | `MethodMap.GC.cs:81` |
| 84 | `System.GC` | `SetLOHCompactionMode` | `System.Void (System.Int32)` | public/internal/static | `icall_gc_set_loh_compaction_mode` | `MethodMap.GC.cs:83` |
| 85 | `System.Globalization.CultureInfo` | `nativeGetLocaleInfoEx` | `System.String (System.String, System.UInt32)` | public/internal/static | `icall_culture_info_native_get_locale_info_ex_int` | `MethodMap.CultureInfo.cs:96` |
| 86 | `System.Globalization.CultureInfo` | `nativeGetLocaleInfoExInt` | `System.Int32 (System.String, System.UInt32)` | public/internal/static | `icall_culture_info_native_get_locale_info_ex_int` | `MethodMap.CultureInfo.cs:97` |
| 87 | `System.Globalization.CultureInfo` | `nativeSetThreadLocale` | `System.Boolean (System.String)` | public/internal/static | `icall_culture_info_native_set_thread_locale` | `MethodMap.CultureInfo.cs:98` |
| 88 | `System.Math` | `Abs` | `System.Double (System.Double)` | public/public/static | `icall_math_abs_d` | `MethodMap.Math.cs:19` |
| 89 | `System.Math` | `Abs` | `System.Single (System.Single)` | public/public/static | `icall_math_abs_d` | `MethodMap.Math.cs:19` |
| 90 | `System.Math` | `Acos` | `System.Double (System.Double)` | public/public/static | `icall_math_acos_d` | `MethodMap.Math.cs:140` |
| 91 | `System.Math` | `Asin` | `System.Double (System.Double)` | public/public/static | `icall_math_asin_d` | `MethodMap.Math.cs:139` |
| 92 | `System.Math` | `Atan` | `System.Double (System.Double)` | public/public/static | `icall_math_atan_d` | `MethodMap.Math.cs:141` |
| 93 | `System.Math` | `Atan2` | `System.Double (System.Double, System.Double)` | public/public/static | `icall_math_atan2_d` | `MethodMap.Math.cs:142` |
| 94 | `System.Math` | `Ceiling` | `System.Double (System.Double)` | public/public/static | `icall_math_ceiling_d` | `MethodMap.Math.cs:82` |
| 95 | `System.Math` | `Cos` | `System.Double (System.Double)` | public/public/static | `icall_math_cos_d` | `MethodMap.Math.cs:137` |
| 96 | `System.Math` | `Cosh` | `System.Double (System.Double)` | public/public/static | `icall_math_cosh_d` | `MethodMap.Math.cs:158` |
| 97 | `System.Math` | `Exp` | `System.Double (System.Double)` | public/public/static | `icall_math_exp_d` | `MethodMap.Math.cs:153` |
| 98 | `System.Math` | `Floor` | `System.Double (System.Double)` | public/public/static | `icall_math_floor_d` | `MethodMap.Math.cs:78` |
| 99 | `System.Math` | `Log` | `System.Double (System.Double)` | public/public/static | `icall_math_log_d` | `MethodMap.Math.cs:154` |
| 100 | `System.Math` | `Log10` | `System.Double (System.Double)` | public/public/static | `icall_math_log10_d` | `MethodMap.Math.cs:157` |
| 101 | `System.Math` | `Pow` | `System.Double (System.Double, System.Double)` | public/public/static | `icall_math_pow_d` | `MethodMap.Math.cs:77` |
| 102 | `System.Math` | `Round` | `System.Double (System.Double)` | public/public/static | `icall_math_round_d` | `MethodMap.Math.cs:86` |
| 103 | `System.Math` | `Sin` | `System.Double (System.Double)` | public/public/static | `icall_math_sin_d` | `MethodMap.Math.cs:136` |
| 104 | `System.Math` | `Sinh` | `System.Double (System.Double)` | public/public/static | `icall_math_sinh_d` | `MethodMap.Math.cs:159` |
| 105 | `System.Math` | `Sqrt` | `System.Double (System.Double)` | public/public/static | `icall_math_sqrt_d` | `MethodMap.Math.cs:76` |
| 106 | `System.Math` | `Tan` | `System.Double (System.Double)` | public/public/static | `icall_math_tan_d` | `MethodMap.Math.cs:138` |
| 107 | `System.Math` | `Tanh` | `System.Double (System.Double)` | public/public/static | `icall_math_tanh_d` | `MethodMap.Math.cs:160` |
| 108 | `System.Object` | `GetType` | `System.Type ()` | public/public/instance | `icall_object_get_type` | `MethodMap.Object.cs:35` |
| 109 | `System.Object` | `MemberwiseClone` | `System.Object ()` | public/protected/instance | `icall_object_memberwise_clone` | `MethodMap.Object.cs:256` |
| 110 | `System.Runtime.CompilerServices.DependentHandle` | `nFree` | `System.Void (System.IntPtr)` | internal/private/static | `icall_dependenthandle_free` | `MethodMap.DependentHandle.cs:85` |
| 111 | `System.Runtime.CompilerServices.DependentHandle` | `nGetPrimary` | `System.Void (System.IntPtr, System.Object&)` | internal/private/static | `icall_dependenthandle_n_get_primary` | `MethodMap.DependentHandle.cs:73` |
| 112 | `System.Runtime.CompilerServices.DependentHandle` | `nGetPrimaryAndSecondary` | `System.Void (System.IntPtr, System.Object&, System.Object&)` | internal/private/static | `icall_dependenthandle_n_get_primary_and_secondary` | `MethodMap.DependentHandle.cs:79` |
| 113 | `System.Runtime.CompilerServices.DependentHandle` | `nInitialize` | `System.Void (System.Object, System.Object, System.IntPtr&)` | internal/private/static | `icall_dependenthandle_n_init` | `MethodMap.DependentHandle.cs:66` |
| 114 | `System.Runtime.CompilerServices.RuntimeHelpers` | `Equals` | `System.Boolean (System.Object, System.Object)` | public/public/static | `::refeq::` | `MethodMap.RuntimeHelpers.cs:56` |
| 115 | `System.Runtime.CompilerServices.RuntimeHelpers` | `GetHashCode` | `System.Int32 (System.Object)` | public/public/static | `icall_object_get_hash_code` | `MethodMap.RuntimeHelpers.cs:47` |
| 116 | `System.Runtime.CompilerServices.RuntimeHelpers` | `GetObjectValue` | `System.Object (System.Object)` | public/public/static | `::identity::` | `MethodMap.RuntimeHelpers.cs:65` |
| 117 | `System.Runtime.InteropServices.GCHandle` | `InternalGetHandleType` | `System.Runtime.InteropServices.GCHandleType (System.IntPtr)` | public/internal/static | `icall_gchandle_get_handle_type` | `MethodMap.GCHandle.cs:79` |
| 118 | `System.Runtime.InteropServices.Marshal` | `DestroyStructure` | `System.Void (System.IntPtr, System.Type)` | public/public/static | `icall_marshal_destroy_structure` | `MethodMap.Marshal.cs:196` |
| 119 | `System.Runtime.InteropServices.Marshal` | `GetExceptionCode` | `System.Int32 ()` | public/public/static | `icall_marshal_get_exception_code` | `MethodMap.Marshal.cs:547` |
| 120 | `System.Runtime.InteropServices.Marshal` | `GetExceptionPointers` | `System.IntPtr ()` | public/public/static | `icall_marshal_get_exception_pointers` | `MethodMap.Marshal.cs:552` |
| 121 | `System.Runtime.InteropServices.Marshal` | `GetHRForException` | `System.Int32 (System.Exception)` | public/public/static | `icall_marshal_get_hr_for_exception` | `MethodMap.Marshal.cs:431` |
| 122 | `System.Runtime.InteropServices.Marshal` | `GetLastWin32Error` | `System.Int32 ()` | public/public/static | `il2cpp_pinvoke_get_last_error` | `MethodMap.Marshal.cs:95` |
| 123 | `System.Runtime.InteropServices.Marshal` | `GetNativeVariantForObject` | `System.Void (System.Object, System.IntPtr)` | public/public/static | `icall_marshal_get_native_variant_for_object` | `MethodMap.Marshal.cs:447` |
| 124 | `System.Runtime.InteropServices.Marshal` | `GetObjectForIUnknown` | `System.Object (System.IntPtr)` | public/public/static | `icall_com_get_object_for_iunknown` | `MethodMap.Marshal.cs:512` |
| 125 | `System.Runtime.InteropServices.Marshal` | `GetObjectForNativeVariant` | `System.Object (System.IntPtr)` | public/public/static | `icall_marshal_get_object_for_native_variant` | `MethodMap.Marshal.cs:442` |
| 126 | `System.Runtime.InteropServices.Marshal` | `GetObjectsForNativeVariants` | `System.Object[] (System.IntPtr, System.Int32)` | public/public/static | `icall_marshal_get_objects_for_native_variants` | `MethodMap.Marshal.cs:452` |
| 127 | `System.Runtime.InteropServices.Marshal` | `IsComObject` | `System.Boolean (System.Object)` | public/public/static | `icall_marshal_is_com_object` | `MethodMap.Marshal.cs:457` |
| 128 | `System.Runtime.InteropServices.Marshal` | `SetLastWin32Error` | `System.Void (System.Int32)` | public/internal/static | `il2cpp_marshal_set_last_win32_error` | `MethodMap.Marshal.cs:100` |
| 129 | `System.Runtime.InteropServices.Marshal` | `StructureToPtr` | `System.Void (System.Object, System.IntPtr, System.Boolean)` | public/public/static | `il2cpp_marshal_struct_store` | `MethodMap.Marshal.cs:351` |
| 130 | `System.Runtime.InteropServices.Marshal` | `UnsafeAddrOfPinnedArrayElement` | `System.IntPtr (System.Array, System.Int32)` | public/public/static | `icall_marshal_unsafe_addr_of_pinned_array_element` | `MethodMap.Marshal.cs:421` |
| 131 | `System.Runtime.InteropServices.SafeHandle` | `DangerousAddRef` | `System.Void (System.Boolean&)` | public/public/instance | `icall_safehandle_dangerous_add_ref` | `MethodMap.SafeHandle.cs:40` |
| 132 | `System.Runtime.InteropServices.SafeHandle` | `DangerousRelease` | `System.Void ()` | public/public/instance | `icall_safehandle_dangerous_release` | `MethodMap.SafeHandle.cs:48` |
| 133 | `System.Runtime.InteropServices.SafeHandle` | `SetHandleAsInvalid` | `System.Void ()` | public/public/instance | `icall_safehandle_set_handle_as_invalid` | `MethodMap.SafeHandle.cs:52` |
| 134 | `System.Runtime.Versioning.CompatibilitySwitch` | `GetAppContextOverridesInternalCall` | `System.String ()` | public/internal/static | `icall_compatibility_switch_get_app_context_overrides` | `MethodMap.AppContext.cs:81` |
| 135 | `System.Runtime.Versioning.CompatibilitySwitch` | `GetValueInternalCall` | `System.String (System.String, System.Boolean)` | public/private/static | `icall_compatibility_switch_get_value` | `MethodMap.AppContext.cs:83` |
| 136 | `System.Runtime.Versioning.CompatibilitySwitch` | `IsEnabledInternalCall` | `System.Boolean (System.String, System.Boolean)` | public/private/static | `icall_compatibility_switch_is_enabled` | `MethodMap.AppContext.cs:86` |
| 137 | `System.Security.Principal.Win32` | `OpenThreadToken` | `System.Int32 (System.Security.Principal.TokenAccessLevels, System.Security.Principal.WinSecurityContext, Microsoft.Win32.SafeHandles.SafeAccessTokenHandle&)` | internal/internal/static | `icall_security_principal_win32_open_thread_token` | `MethodMap.Emit.cs:75` |
| 138 | `System.Security.SecurityContext` | `GetImpersonationFlowMode` | `System.Security.WindowsImpersonationFlowMode ()` | public/internal/static | `icall_security_context_get_impersonation_flow_mode` | `MethodMap.GC.cs:246` |
| 139 | `System.Security.SecurityContext` | `IsDefaultThreadSecurityInfo` | `System.Boolean ()` | public/internal/static | `icall_security_context_is_default_thread_security_info` | `MethodMap.GC.cs:247` |
| 140 | `System.Security.SecurityManager` | `_SetThreadSecurity` | `System.Boolean (System.Boolean)` | public/internal/static | `icall_security_manager_set_thread_security` | `MethodMap.GC.cs:248` |
| 141 | `System.Security.SecurityRuntime` | `GetSecurityObjectForFrame` | `System.Security.FrameSecurityDescriptor (System.Threading.StackCrawlMark&, System.Boolean)` | internal/internal/static | `icall_security_runtime_get_security_object_for_frame` | `MethodMap.GC.cs:249` |
| 142 | `System.String` | `.ctor` | `System.Void (System.Char*)` | public/public/instance | `icall_string_ctor_char_ptr` | `MethodMap.String.cs:15` |
| 143 | `System.String` | `.ctor` | `System.Void (System.Char*, System.Int32, System.Int32)` | public/public/instance | `icall_string_ctor_char_ptr_range` | `MethodMap.String.cs:15` |
| 144 | `System.String` | `.ctor` | `System.Void (System.Char, System.Int32)` | public/public/instance | `icall_string_ctor_repeat` | `MethodMap.String.cs:15` |
| 145 | `System.String` | `.ctor` | `System.Void (System.Char[])` | public/public/instance | `icall_string_ctor_char_array` | `MethodMap.String.cs:15` |
| 146 | `System.String` | `.ctor` | `System.Void (System.Char[], System.Int32, System.Int32)` | public/public/instance | `icall_string_ctor_char_array_range` | `MethodMap.String.cs:15` |
| 147 | `System.String` | `.ctor` | `System.Void (System.SByte*)` | public/public/instance | `icall_string_ctor_sbyte_ptr` | `MethodMap.String.cs:15` |
| 148 | `System.String` | `.ctor` | `System.Void (System.SByte*, System.Int32, System.Int32)` | public/public/instance | `icall_string_ctor_sbyte_ptr_range` | `MethodMap.String.cs:15` |
| 149 | `System.String` | `.ctor` | `System.Void (System.SByte*, System.Int32, System.Int32, System.Text.Encoding)` | public/public/instance | `icall_string_ctor_sbyte_ptr_range_encoding` | `MethodMap.String.cs:15` |
| 150 | `System.String` | `FastAllocateString` | `System.String (System.Int32)` | public/internal/static | `icall_string_fast_allocate` | `MethodMap.String.cs:733` |
| 151 | `System.String` | `IndexOf` | `System.Int32 (System.Char, System.Int32, System.Int32)` | public/public/instance | `icall_string_index_of_c_range` | `MethodMap.String.cs:82` |
| 152 | `System.String` | `IndexOfAny` | `System.Int32 (System.Char[], System.Int32, System.Int32)` | public/public/instance | `icall_string_index_of_any_range` | `MethodMap.String.cs:92` |
| 153 | `System.String` | `InternalMarvin32HashString` | `System.Int32 (System.String, System.Int32, System.Int64)` | public/internal/static | `icall_string_internal_marvin32_hash_string` | `MethodMap.String.cs:748` |
| 154 | `System.String` | `IsAscii` | `System.Boolean ()` | public/internal/instance | `icall_string_is_ascii` | `MethodMap.String.cs:750` |
| 155 | `System.String` | `IsFastSort` | `System.Boolean ()` | public/internal/instance | `icall_string_is_fast_sort` | `MethodMap.String.cs:749` |
| 156 | `System.String` | `LastIndexOf` | `System.Int32 (System.Char, System.Int32, System.Int32)` | public/public/instance | `icall_string_last_index_of_c_range` | `MethodMap.String.cs:132` |
| 157 | `System.String` | `LastIndexOfAny` | `System.Int32 (System.Char[], System.Int32, System.Int32)` | public/public/instance | `icall_string_last_index_of_any_range` | `MethodMap.String.cs:114` |
| 158 | `System.String` | `SetTrailByte` | `System.Void (System.Byte)` | public/internal/instance | `icall_string_set_trail_byte` | `MethodMap.String.cs:738` |
| 159 | `System.String` | `TryGetTrailByte` | `System.Boolean (System.Byte&)` | public/internal/instance | `icall_string_try_get_trail_byte` | `MethodMap.String.cs:742` |
| 160 | `System.String` | `get_Chars` | `System.Char (System.Int32)` | public/public/instance | `icall_string_get_chars` | `MethodMap.String.cs:723` |
| 161 | `System.String` | `get_Length` | `System.Int32 ()` | public/public/instance | `il2cpp_string_get_length` | `MethodMap.String.cs:74` |
| 162 | `System.String` | `nativeCompareOrdinalEx` | `System.Int32 (System.String, System.Int32, System.String, System.Int32, System.Int32)` | public/internal/static | `icall_string_native_compare_ordinal_ex` | `MethodMap.String.cs:746` |
| 163 | `System.String` | `nativeCompareOrdinalIgnoreCaseWC` | `System.Int32 (System.String, System.SByte*)` | public/internal/static | `icall_string_native_compare_ordinal_ignore_case_wc` | `MethodMap.String.cs:747` |
| 164 | `System.Text.StringBuilder` | `ReplaceBufferAnsiInternal` | `System.Void (System.SByte*, System.Int32)` | public/internal/instance | `icall_stringbuilder_replace_buffer_ansi_internal` | `MethodMap.StringBuilder.cs:384` |
| 165 | `System.Text.StringBuilder` | `ReplaceBufferInternal` | `System.Void (System.Char*, System.Int32)` | public/internal/instance | `icall_stringbuilder_replace_buffer_internal` | `MethodMap.StringBuilder.cs:383` |
| 166 | `System.Threading.HostExecutionContextManager` | `CloneHostSecurityContext` | `System.Int32 (System.Runtime.InteropServices.SafeHandle, System.Runtime.InteropServices.SafeHandle)` | public/internal/static | `icall_host_execution_context_manager_clone_host_security_context` | `MethodMap.Thread.cs:270` |
| 167 | `System.Threading.HostExecutionContextManager` | `ReleaseHostSecurityContext` | `System.Int32 (System.IntPtr)` | public/internal/static | `icall_host_execution_context_manager_release_host_security_context` | `MethodMap.Thread.cs:269` |
| 168 | `System.Threading.Interlocked` | `CompareExchange` | `System.Double (System.Double&, System.Double, System.Double)` | public/public/static | `il2cpp_interlocked_compare_exchange_double` | `MethodMap.Interlocked.cs:101` |
| 169 | `System.Threading.Interlocked` | `CompareExchange` | `System.Int32 (System.Int32&, System.Int32, System.Int32)` | public/public/static | `il2cpp_interlocked_compare_exchange` | `MethodMap.Interlocked.cs:101` |
| 170 | `System.Threading.Interlocked` | `CompareExchange` | `System.Int32 (System.Int32&, System.Int32, System.Int32, System.Boolean&)` | public/internal/static | `icall_interlocked_compare_exchange_found` | `MethodMap.Interlocked.cs:101` |
| 171 | `System.Threading.Interlocked` | `CompareExchange` | `System.Int64 (System.Int64&, System.Int64, System.Int64)` | public/public/static | `il2cpp_interlocked_compare_exchange64` | `MethodMap.Interlocked.cs:101` |
| 172 | `System.Threading.Interlocked` | `CompareExchange` | `System.IntPtr (System.IntPtr&, System.IntPtr, System.IntPtr)` | public/public/static | `il2cpp_interlocked_compare_exchange_pointer` | `MethodMap.Interlocked.cs:101` |
| 173 | `System.Threading.Interlocked` | `CompareExchange` | `System.Object (System.Object&, System.Object, System.Object)` | public/public/static | `il2cpp_interlocked_compare_exchange_pointer` | `MethodMap.Interlocked.cs:101` |
| 174 | `System.Threading.Interlocked` | `CompareExchange` | `System.Single (System.Single&, System.Single, System.Single)` | public/public/static | `il2cpp_interlocked_compare_exchange_double` | `MethodMap.Interlocked.cs:101` |
| 175 | `System.Threading.Interlocked` | `Exchange` | `System.Double (System.Double&, System.Double)` | public/public/static | `il2cpp_interlocked_exchange_double` | `MethodMap.Interlocked.cs:63` |
| 176 | `System.Threading.Interlocked` | `Exchange` | `System.Int32 (System.Int32&, System.Int32)` | public/public/static | `il2cpp_interlocked_exchange` | `MethodMap.Interlocked.cs:63` |
| 177 | `System.Threading.Interlocked` | `Exchange` | `System.Int64 (System.Int64&, System.Int64)` | public/public/static | `il2cpp_interlocked_exchange64` | `MethodMap.Interlocked.cs:63` |
| 178 | `System.Threading.Interlocked` | `Exchange` | `System.IntPtr (System.IntPtr&, System.IntPtr)` | public/public/static | `il2cpp_interlocked_exchange_pointer` | `MethodMap.Interlocked.cs:63` |
| 179 | `System.Threading.Interlocked` | `Exchange` | `System.Object (System.Object&, System.Object)` | public/public/static | `il2cpp_interlocked_exchange_pointer` | `MethodMap.Interlocked.cs:63` |
| 180 | `System.Threading.Interlocked` | `Exchange` | `System.Single (System.Single&, System.Single)` | public/public/static | `il2cpp_interlocked_exchange_double` | `MethodMap.Interlocked.cs:63` |
| 181 | `System.Threading.Interlocked` | `ExchangeAdd` | `System.Int32 (System.Int32&, System.Int32)` | public/internal/static | `il2cpp_interlocked_add` | `MethodMap.Interlocked.cs:57` |
| 182 | `System.Threading.Interlocked` | `ExchangeAdd` | `System.Int64 (System.Int64&, System.Int64)` | public/internal/static | `il2cpp_interlocked_add64` | `MethodMap.Interlocked.cs:57` |
| 183 | `System.Threading.Interlocked` | `SpeculationBarrier` | `System.Void ()` | public/public/static | `il2cpp_interlocked_speculation_barrier` | `MethodMap.Interlocked.cs:146` |
| 184 | `System.Threading.Monitor` | `Enter` | `System.Void (System.Object)` | public/public/static | `il2cpp_monitor_enter` | `MethodMap.Monitor.cs:45` |
| 185 | `System.Threading.Monitor` | `Exit` | `System.Void (System.Object)` | public/public/static | `il2cpp_monitor_exit` | `MethodMap.Monitor.cs:59` |
| 186 | `System.Threading.ReaderWriterLock` | `AnyWritersSince` | `System.Boolean (System.Int32)` | public/public/instance | `icall_reader_writer_lock_any_writers_since` | `MethodMap.Legacy.cs:237` |
| 187 | `System.Threading.SynchronizationContext` | `WaitHelper` | `System.Int32 (System.IntPtr[], System.Boolean, System.Int32)` | public/protected/static | `icall_synchronization_context_wait_helper` | `MethodMap.Thread.cs:261` |
| 188 | `System.Threading.Thread` | `BeginCriticalRegion` | `System.Void ()` | public/public/static | `icall_thread_begin_critical_region` | `MethodMap.Thread.cs:56` |
| 189 | `System.Threading.Thread` | `BeginThreadAffinity` | `System.Void ()` | public/public/static | `icall_thread_begin_thread_affinity` | `MethodMap.Thread.cs:117` |
| 190 | `System.Threading.Thread` | `ClearAbortReason` | `System.Void ()` | public/internal/instance | `icall_threadobj_clear_abort_reason` | `MethodMap.Thread.cs:259` |
| 191 | `System.Threading.Thread` | `DisableComObjectEagerCleanup` | `System.Void ()` | public/public/instance | `icall_thread_disable_com_object_eager_cleanup` | `MethodMap.Thread.cs:111` |
| 192 | `System.Threading.Thread` | `EndCriticalRegion` | `System.Void ()` | public/public/static | `icall_thread_end_critical_region` | `MethodMap.Thread.cs:58` |
| 193 | `System.Threading.Thread` | `EndThreadAffinity` | `System.Void ()` | public/public/static | `icall_thread_end_thread_affinity` | `MethodMap.Thread.cs:121` |
| 194 | `System.Threading.Thread` | `GetAbortReason` | `System.Object ()` | public/internal/instance | `icall_threadobj_get_abort_reason` | `MethodMap.Thread.cs:258` |
| 195 | `System.Threading.Thread` | `GetApartmentStateNative` | `System.Int32 ()` | public/private/instance | `icall_threadobj_get_apartment_state_native` | `MethodMap.Thread.cs:247` |
| 196 | `System.Threading.Thread` | `GetContextInternal` | `System.Runtime.Remoting.Contexts.Context (System.IntPtr)` | public/internal/static | `icall_threadobj_get_context_internal` | `MethodMap.Thread.cs:255` |
| 197 | `System.Threading.Thread` | `GetCurrentProcessorNumber` | `System.Int32 ()` | public/private/static | `icall_thread_get_current_processor_number` | `MethodMap.Thread.cs:128` |
| 198 | `System.Threading.Thread` | `InternalCrossContextCallback` | `System.Object (System.Runtime.Remoting.Contexts.Context, System.IntPtr, System.Int32, System.Threading.InternalCrossContextDelegate, System.Object[])` | public/internal/instance | `icall_threadobj_internal_cross_context_callback` | `MethodMap.Thread.cs:256` |
| 199 | `System.Threading.Thread` | `InternalGetCurrentThread` | `System.IntPtr ()` | public/internal/static | `icall_threadobj_internal_get_current_thread` | `MethodMap.Thread.cs:254` |
| 200 | `System.Threading.Thread` | `MemoryBarrier` | `System.Void ()` | public/public/static | `icall_thread_memory_barrier` | `MethodMap.Thread.cs:63` |
| 201 | `System.Threading.Thread` | `RestoreAppDomainStack` | `System.Void (System.IntPtr)` | public/internal/instance | `icall_threadobj_restore_app_domain_stack` | `MethodMap.Thread.cs:253` |
| 202 | `System.Threading.Thread` | `SetAbortReason` | `System.Void (System.Object)` | public/internal/instance | `icall_threadobj_set_abort_reason` | `MethodMap.Thread.cs:257` |
| 203 | `System.Threading.Thread` | `SetApartmentStateNative` | `System.Int32 (System.Int32, System.Boolean)` | public/private/instance | `icall_threadobj_set_apartment_state_native` | `MethodMap.Thread.cs:248` |
| 204 | `System.Threading.Thread` | `SetAppDomainStack` | `System.IntPtr (System.Threading.SafeCompressedStackHandle)` | public/internal/instance | `icall_threadobj_set_app_domain_stack` | `MethodMap.Thread.cs:252` |
| 205 | `System.Threading.Thread` | `get_IsAlive` | `System.Boolean ()` | public/public/instance | `icall_threadobj_get_is_alive` | `MethodMap.Thread.cs:182` |
| 206 | `System.Threading.Thread` | `get_IsThreadPoolThread` | `System.Boolean ()` | public/public/instance | `icall_thread_get_is_thread_pool_thread` | `MethodMap.Thread.cs:105` |
| 207 | `System.Threading.Thread` | `get_ManagedThreadId` | `System.Int32 ()` | public/public/instance | `icall_threadobj_get_managed_thread_id` | `MethodMap.Thread.cs:186` |
| 208 | `System.Threading.ThreadPool` | `IsThreadPoolHosted` | `System.Boolean ()` | public/internal/static | `icall_thread_pool_is_thread_pool_hosted` | `MethodMap.ThreadPool.cs:68` |
| 209 | `System.Threading.ThreadPool` | `NotifyWorkItemComplete` | `System.Boolean ()` | public/internal/static | `icall_thread_pool_notify_work_item_complete` | `MethodMap.ThreadPool.cs:65` |
| 210 | `System.Threading.ThreadPool` | `NotifyWorkItemProgressNative` | `System.Void ()` | public/internal/static | `icall_thread_pool_notify_work_item_progress_native` | `MethodMap.ThreadPool.cs:67` |
| 211 | `System.Threading.ThreadPool` | `ReportThreadStatus` | `System.Void (System.Boolean)` | public/internal/static | `icall_thread_pool_report_thread_status` | `MethodMap.ThreadPool.cs:66` |
| 212 | `System.Type` | `GetTypeFromHandle` | `System.Type (System.RuntimeTypeHandle)` | public/public/static | `il2cpp_type_get_type_from_handle_value` | `MethodMap.Type.cs:73` |
| 213 | `System.Type` | `GetTypeFromHandleUnsafe` | `System.RuntimeType (System.IntPtr)` | public/internal/static | `icall_type_get_type_from_handle_unsafe` | `MethodMap.Type.cs:547` |
| 214 | `System.Type` | `op_Equality` | `System.Boolean (System.Type, System.Type)` | public/public/static | `icall_string_equals_static` | `MethodMap.Type.cs:542` |
| 215 | `System.Type` | `op_Inequality` | `System.Boolean (System.Type, System.Type)` | public/public/static | `icall_type_op_inequality` | `MethodMap.Type.cs:549` |
| 216 | `System.ValueType` | `GetHashCode` | `System.Int32 ()` | public/public/instance | `icall_value_type_get_hash_code` | `MethodMap.ValueType.cs:495` |
| 217 | `System.ValueType` | `GetHashCodeOfPtr` | `System.Int32 (System.IntPtr)` | public/internal/static | `icall_value_type_get_hash_code_of_ptr` | `MethodMap.ValueType.cs:468` |
| 218 | `System.WeakReference` | `Finalize` | `System.Void ()` | public/protected/instance | `icall_weak_reference_finalize` | `MethodMap.WeakReference.cs:73` |
| 219 | `System.WeakReference` | `get_IsAlive` | `System.Boolean ()` | public/public/instance | `icall_weakref_get_is_alive` | `MethodMap.WeakReference.cs:59` |
| 220 | `System.WeakReference` | `get_Target` | `System.Object ()` | public/public/instance | `icall_weakref_get_target` | `MethodMap.WeakReference.cs:61` |
| 221 | `System.WeakReference` | `set_Target` | `System.Void (System.Object)` | public/public/instance | `icall_weakref_set_target` | `MethodMap.WeakReference.cs:67` |
| 222 | `System.WeakReference`1` | `Finalize` | `System.Void ()` | public/protected/instance | `icall_weak_reference_finalize` | `MethodMap.WeakReference.cs:73` |
| 223 | `System.WeakReference`1` | `get_Target` | `!0 ()` | public/private/instance | `icall_weakref_get_target` | `MethodMap.WeakReference.cs:61` |
| 224 | `System.WeakReference`1` | `set_Target` | `System.Void (!0)` | public/private/instance | `icall_weakref_set_target` | `MethodMap.WeakReference.cs:67` |

## 3. 缺口清单（按 P0→P3）

「缺口」= 非「已接」的全部行（半接 / LOUD 拒绝 / 无接线）。影子单列在 §3.6，因为它的结论是「无需接线」。

### P0（30 条）

类型与方法均为 public，用户 IL 可以直接 call，属于**必须处理**的面。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `System.ArgIterator` | `GetRemainingCount` | public/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetRemainingCount is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor:  | `MethodMap.Legacy.cs:232` |
| 2 | `System.Runtime.CompilerServices.RuntimeHelpers` | `EnsureSufficientExecutionStack` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.EnsureSufficientExecutionStack is a host stack-probe hint (the AOT runs on the host stack with no | `MethodMap.RuntimeHelpers.cs:81` |
| 3 | `System.Runtime.CompilerServices.RuntimeHelpers` | `ExecuteCodeWithGuaranteedCleanup` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup is a constrained-execution try/finally (TryCode/CleanupCode  | `MethodMap.RuntimeHelpers.cs:89` |
| 4 | `System.Runtime.CompilerServices.RuntimeHelpers` | `InitializeArray` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers.InitializeArray is a StackFold fold (ldtoken<RVA field> + newarr); the call reached the resol | `MethodMap.RuntimeHelpers.cs:108` |
| 5 | `System.Runtime.CompilerServices.RuntimeHelpers` | `PrepareContractedDelegate` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.PrepareContractedDelegate is a constrained-execution JIT-prepare hint (no JIT to prepare) | `MethodMap.RuntimeHelpers.cs:77` |
| 6 | `System.Runtime.CompilerServices.RuntimeHelpers` | `PrepareDelegate` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.PrepareDelegate is a JIT-prepare hint (this AOT has no JIT to prepare the delegate) | `MethodMap.RuntimeHelpers.cs:73` |
| 7 | `System.Runtime.CompilerServices.RuntimeHelpers` | `ProbeForSufficientStack` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (① machine/JIT state): RuntimeHelpers.ProbeForSufficientStack is a host stack-probe hint (no managed stack-limit checks) | `MethodMap.RuntimeHelpers.cs:85` |
| 8 | `System.Runtime.InteropServices.ExtensibleClassFactory` | `RegisterObjectCreationCallback` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 9 | `System.Runtime.InteropServices.Marshal` | `AddRef` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): AddRef belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIANT conve | `MethodMap.Marshal.cs:86` |
| 10 | `System.Runtime.InteropServices.Marshal` | `AreComObjectsAvailableForCleanup` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): AreComObjectsAvailableForCleanup belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library | `MethodMap.Marshal.cs:86` |
| 11 | `System.Runtime.InteropServices.Marshal` | `ChangeWrapperHandleStrength` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): ChangeWrapperHandleStrength belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library mars | `MethodMap.Marshal.cs:86` |
| 12 | `System.Runtime.InteropServices.Marshal` | `CleanupUnusedObjectsInCurrentContext` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): CleanupUnusedObjectsInCurrentContext belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-lib | `MethodMap.Marshal.cs:86` |
| 13 | `System.Runtime.InteropServices.Marshal` | `CreateAggregatedObject` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): CreateAggregatedObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshalin | `MethodMap.Marshal.cs:86` |
| 14 | `System.Runtime.InteropServices.Marshal` | `GetEndComSlot` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetEndComSlot belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIAN | `MethodMap.Marshal.cs:86` |
| 15 | `System.Runtime.InteropServices.Marshal` | `GetITypeInfoForType` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetITypeInfoForType belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling,  | `MethodMap.Marshal.cs:86` |
| 16 | `System.Runtime.InteropServices.Marshal` | `GetManagedThunkForUnmanagedMethodPtr` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 17 | `System.Runtime.InteropServices.Marshal` | `GetMethodInfoForComSlot` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetMethodInfoForComSlot belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshali | `MethodMap.Marshal.cs:86` |
| 18 | `System.Runtime.InteropServices.Marshal` | `GetStartComSlot` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetStartComSlot belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARI | `MethodMap.Marshal.cs:86` |
| 19 | `System.Runtime.InteropServices.Marshal` | `GetTypeLibLcid` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetTypeLibLcid belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIA | `MethodMap.Marshal.cs:86` |
| 20 | `System.Runtime.InteropServices.Marshal` | `GetTypedObjectForIUnknown` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetTypedObjectForIUnknown belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marsha | `MethodMap.Marshal.cs:86` |
| 21 | `System.Runtime.InteropServices.Marshal` | `GetUniqueObjectForIUnknown` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 22 | `System.Runtime.InteropServices.Marshal` | `GetUnmanagedThunkForManagedMethodPtr` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 23 | `System.Runtime.InteropServices.Marshal` | `IsTypeVisibleFromCom` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): IsTypeVisibleFromCom belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, | `MethodMap.Marshal.cs:86` |
| 24 | `System.Runtime.InteropServices.Marshal` | `QueryInterface` | public/public/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 25 | `System.Runtime.InteropServices.Marshal` | `Release` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): Release belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIANT conv | `MethodMap.Marshal.cs:86` |
| 26 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetProxiedType` | public/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetProxiedType is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD r | `MethodMap.Legacy.cs:44` |
| 27 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetStubData` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：GetStubData is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reje | `MethodMap.Legacy.cs:44` |
| 28 | `System.Runtime.Remoting.Proxies.RealProxy` | `SetStubData` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：SetStubData is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reje | `MethodMap.Legacy.cs:44` |
| 29 | `System.Runtime.Remoting.RemotingServices` | `GetRealProxy` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：GetRealProxy is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD rej | `MethodMap.Legacy.cs:44` |
| 30 | `System.Runtime.Remoting.RemotingServices` | `IsTransparentProxy` | public/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：IsTransparentProxy is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LO | `MethodMap.Legacy.cs:44` |

### P1（198 条）

非 public 面，但经普通 BCL 内部路径可达（集合/字符串/LINQ/IO/格式化/数值等）。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `System.ArgIterator` | `.ctor` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：.ctor is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __arglist is | `MethodMap.Legacy.cs:232` |
| 2 | `System.ArgIterator` | `.ctor` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：.ctor is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __arglist is | `MethodMap.Legacy.cs:232` |
| 3 | `System.ArgIterator` | `FCallGetNextArg` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：FCallGetNextArg is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __ | `MethodMap.Legacy.cs:232` |
| 4 | `System.ArgIterator` | `InternalGetNextArg` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：InternalGetNextArg is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: | `MethodMap.Legacy.cs:232` |
| 5 | `System.ArgIterator` | `_GetNextArgType` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：_GetNextArgType is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __ | `MethodMap.Legacy.cs:232` |
| 6 | `System.Array` | `InternalCreate` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 7 | `System.Array` | `InternalGetReference` | public/private/instance | 影子 | 影子：由 `System.Array::GetValue` 承接（`il2cpp::il2cpp_array_get_value`） | `MethodMap.Array.cs:1129` |
| 8 | `System.Array` | `InternalSetValue` | public/private/static | 影子 | 影子：由 `System.Array::SetValue` 承接（`il2cpp::il2cpp_array_set_value`） | `MethodMap.Array.cs:1158` |
| 9 | `System.Array` | `TrySZBinarySearch` | public/private/static | 影子 | 影子：由 `System.Array::BinarySearch` 承接（`il2cpp::il2cpp_array_binary_search_any`） | `MethodMap.Array.cs:378` |
| 10 | `System.Array` | `TrySZIndexOf` | public/private/static | 影子 | 影子：由 `System.Array::IndexOf` 承接（`il2cpp::il2cpp_array_index_of_any_start`） | `MethodMap.Array.cs:428` |
| 11 | `System.Array` | `TrySZLastIndexOf` | public/private/static | 影子 | 影子：由 `System.Array::LastIndexOf` 承接（`il2cpp::il2cpp_array_last_index_of_any`） | `MethodMap.Array.cs:448` |
| 12 | `System.Array` | `TrySZReverse` | public/private/static | 影子 | 影子：由 `System.Array::Reverse` 承接（`il2cpp::runtime::icall_array_reverse`） | `MethodMap.Array.cs:527` |
| 13 | `System.Array` | `TrySZSort` | public/private/static | 影子 | 影子：由 `System.Array::Sort` 承接（`il2cpp::il2cpp_array_sort_any`） | `MethodMap.Array.cs:145` |
| 14 | `System.BCLDebug` | `GetRegistryLoggingValues` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 15 | `System.Buffer` | `IsPrimitiveTypeArray` | public/private/static | 影子 | 影子：由 `System.Buffer::GetByte` 承接（`il2cpp::runtime::icall_buffer_get_byte`） | `MethodMap.Buffer.cs:71` |
| 16 | `System.Buffer` | `_ByteLength` | public/private/static | 影子 | 影子：由 `System.Buffer::GetByte` 承接（`il2cpp::runtime::icall_buffer_get_byte`） | `MethodMap.Buffer.cs:71` |
| 17 | `System.Buffer` | `_GetByte` | public/private/static | 影子 | 影子：由 `System.Buffer::GetByte` 承接（`il2cpp::runtime::icall_buffer_get_byte`） | `MethodMap.Buffer.cs:71` |
| 18 | `System.Buffer` | `_SetByte` | public/private/static | 影子 | 影子：由 `System.Buffer::SetByte` 承接（`il2cpp::runtime::icall_buffer_set_byte`） | `MethodMap.Buffer.cs:81` |
| 19 | `System.Currency` | `FCallToDecimal` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 20 | `System.CurrentSystemTimeZone` | `nativeGetTimeZoneMinuteOffset` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 21 | `System.Decimal` | `FCallCompare` | public/private/static | 影子 | 影子：由 `System.Decimal::Compare` 承接（`il2cpp::runtime::icall_decimal_compare`） | `MethodMap.Decimal.cs:170` |
| 22 | `System.Decimal` | `FCallFloor` | public/private/static | 影子 | 影子：由 `System.Decimal::Floor` 承接（`il2cpp::runtime::icall_decimal_floor`） | `MethodMap.Decimal.cs:305` |
| 23 | `System.Decimal` | `FCallRound` | public/private/static | 影子 | 影子：由 `System.Decimal::Round` 承接（`il2cpp::runtime::icall_decimal_round`） | `MethodMap.Decimal.cs:290` |
| 24 | `System.Decimal` | `FCallToCurrency` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 25 | `System.Decimal` | `FCallTruncate` | public/private/static | 影子 | 影子：由 `System.Decimal::ToInt32` 承接（`il2cpp::runtime::icall_decimal_to_int32`） | `MethodMap.Decimal.cs:316` |
| 26 | `System.Delegate` | `BindToMethodInfo` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 27 | `System.Delegate` | `BindToMethodName` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 28 | `System.Delegate` | `InternalAlloc` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 29 | `System.Enum` | `InternalBoxEnum` | public/private/static | 影子 | 影子：由 `System.Enum::ToObject` 承接（`il2cpp::runtime::il2cpp_enum_to_object`） | `MethodMap.Enum.cs:398` |
| 30 | `System.Enum` | `InternalCompareTo` | public/private/static | 影子 | 影子：由 `System.Enum::CompareTo` 承接（`il2cpp::runtime::il2cpp_enum_compare_to`） | `MethodMap.Enum.cs:416` |
| 31 | `System.Enum` | `InternalGetCorElementType` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 32 | `System.Enum` | `InternalHasFlag` | public/private/instance | 影子 | 影子：由 `System.Enum::HasFlag` 承接（`il2cpp::runtime::il2cpp_enum_has_flag`） | `MethodMap.Enum.cs:404` |
| 33 | `System.Environment` | `GetCommandLineArgsNative` | public/private/static | 影子 | 影子：由 `System.Environment::GetCommandLineArgs` 承接（`il2cpp::runtime::icall_environment_get_command_line_args`） | `MethodMap.Environment.cs:76` |
| 34 | `System.Environment` | `GetVersionEx` | public/internal/static | 半接 | 半接：dispatch 指向 `icall_environment_get_version_ex`，但该运行时实现是抛异常的 LOUD 桩（桩定义 `stubs.cpp:1200`） | `stubs.cpp:1200` |
| 35 | `System.Exception` | `CopyDynamicMethods` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 36 | `System.Exception` | `CopyStackTrace` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 37 | `System.Exception` | `GetMethodFromStackTrace` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 38 | `System.Exception` | `GetStackTracesDeepCopy` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 39 | `System.Exception` | `IsImmutableAgileException` | public/private/static | 影子 | 影子：由 `System.Exception::get_Data` 承接（`il2cpp::runtime::icall_exception_get_data`） | `MethodMap.Exception.cs:215` |
| 40 | `System.Exception` | `PrepareForForeignExceptionRaise` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 41 | `System.Exception` | `StripFileInfo` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 42 | `System.Exception` | `nIsTransient` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 43 | `System.GC` | `GetGenerationWR` | public/private/static | 影子 | 影子：由 `System.GC::GetGeneration` 承接（`il2cpp::runtime::icall_gc_get_generation`） | `MethodMap.GC.cs:54` |
| 44 | `System.GC` | `_CancelFullGCNotification` | public/private/static | 影子 | 影子：由 `System.GC::CancelFullGCNotification` 承接（`il2cpp::il2cpp_gc_cancel_full_gc_notification`） | `MethodMap.GC.cs:107` |
| 45 | `System.GC` | `_CollectionCount` | public/private/static | 影子 | 影子：由 `System.GC::CollectionCount` 承接（`il2cpp::il2cpp_gc_get_collection_count`） | `MethodMap.GC.cs:96` |
| 46 | `System.GC` | `_GetAllocatedBytesForCurrentThread` | public/private/static | 影子 | 影子：由 `System.GC::GetAllocatedBytesForCurrentThread` 承接（`il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread`） | `MethodMap.GC.cs:118` |
| 47 | `System.GC` | `_ReRegisterForFinalize` | public/private/static | 影子 | 影子：由 `System.GC::ReRegisterForFinalize` 承接（`il2cpp::runtime::icall_gc_re_register_for_finalize`） | `MethodMap.GC.cs:57` |
| 48 | `System.GC` | `_RegisterForFullGCNotification` | public/private/static | 影子 | 影子：由 `System.GC::RegisterForFullGCNotification` 承接（`il2cpp::il2cpp_gc_register_for_full_gc_notification`） | `MethodMap.GC.cs:102` |
| 49 | `System.GC` | `_SuppressFinalize` | public/private/static | 影子 | 影子：由 `System.GC::SuppressFinalize` 承接（`il2cpp::runtime::icall_gc_suppress_finalize`） | `MethodMap.GC.cs:56` |
| 50 | `System.GC` | `_WaitForFullGCApproach` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 51 | `System.GC` | `_WaitForFullGCComplete` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 52 | `System.Globalization.CalendarData` | `nativeGetCalendars` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 53 | `System.Globalization.CalendarData` | `nativeGetTwoDigitYearMax` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 54 | `System.Globalization.CultureData` | `LCIDToLocaleName` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 55 | `System.Globalization.CultureData` | `LocaleNameToLCID` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 56 | `System.Globalization.CultureData` | `nativeGetNumberFormatInfoValues` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 57 | `System.Globalization.CultureData` | `nativeInitCultureData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 58 | `System.Globalization.CultureInfo` | `InternalGetSystemDefaultUILanguage` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 59 | `System.Globalization.TextInfo` | `InternalChangeCaseChar` | public/private/static | 影子 | 影子：由 `System.Globalization.TextInfo::ToLower` 承接（`il2cpp::runtime::icall_string_to_lower`） | `MethodMap.NumberFormat.cs:166` |
| 60 | `System.Globalization.TextInfo` | `InternalChangeCaseString` | public/private/static | 影子 | 影子：由 `System.Globalization.TextInfo::ToLower` 承接（`il2cpp::runtime::icall_string_to_lower`） | `MethodMap.NumberFormat.cs:166` |
| 61 | `System.Globalization.TextInfo` | `InternalGetCaseInsHash` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 62 | `System.MarshalByRefObject` | `GetComIUnknown` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 63 | `System.Math` | `SplitFractionDouble` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such overload): Math.SplitFractionDouble -- ModF/SplitFractionDouble is a .NET Core API (net48 has no public ModF member) | `MethodMap.Math.cs:128` |
| 64 | `System.MissingMemberException` | `FormatSignature` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 65 | `System.ModuleHandle` | `GetDynamicMethod` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 66 | `System.ModuleHandle` | `GetMDStreamVersion` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 67 | `System.ModuleHandle` | `_GetMetadataImport` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 68 | `System.Number` | `FormatDecimal` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 69 | `System.Number` | `FormatDouble` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 70 | `System.Number` | `FormatInt32` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 71 | `System.Number` | `FormatInt64` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 72 | `System.Number` | `FormatSingle` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 73 | `System.Number` | `FormatUInt32` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 74 | `System.Number` | `FormatUInt64` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 75 | `System.Number` | `NumberBufferToDecimal` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 76 | `System.Number` | `NumberBufferToDouble` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 77 | `System.ParseNumbers` | `IntToString` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 78 | `System.ParseNumbers` | `LongToString` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 79 | `System.ParseNumbers` | `StringToInt` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 80 | `System.ParseNumbers` | `StringToLong` | internal/public/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 81 | `System.Runtime.Versioning.VersioningHelper` | `GetRuntimeId` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 82 | `System.RuntimeFieldHandle` | `AcquiresContextFromThis` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::AcquiresContextFromThis not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 83 | `System.RuntimeFieldHandle` | `GetApproxDeclaringType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetApproxDeclaringType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 84 | `System.RuntimeFieldHandle` | `GetValue` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::GetValue not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 85 | `System.RuntimeFieldHandle` | `SetValue` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeFieldHandle::SetValue not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 86 | `System.RuntimeMethodHandle` | `CheckLinktimeDemands` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::CheckLinktimeDemands not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on | `MethodMap.ReflectHandle.cs:55` |
| 87 | `System.RuntimeMethodHandle` | `GetAttributes` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetAttributes not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 88 | `System.RuntimeMethodHandle` | `GetDeclaringType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetDeclaringType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the | `MethodMap.ReflectHandle.cs:55` |
| 89 | `System.RuntimeMethodHandle` | `GetLoaderAllocator` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetLoaderAllocator not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on t | `MethodMap.ReflectHandle.cs:55` |
| 90 | `System.RuntimeMethodHandle` | `GetMethodFromCanonical` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetMethodFromCanonical not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 91 | `System.RuntimeMethodHandle` | `GetName` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetName not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 92 | `System.RuntimeMethodHandle` | `GetResolver` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetResolver not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type | `MethodMap.ReflectHandle.cs:55` |
| 93 | `System.RuntimeMethodHandle` | `GetStubIfNeeded` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetStubIfNeeded not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 94 | `System.RuntimeMethodHandle` | `HasMethodInstantiation` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::HasMethodInstantiation not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType  | `MethodMap.ReflectHandle.cs:55` |
| 95 | `System.RuntimeMethodHandle` | `InvokeMethod` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::InvokeMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 96 | `System.RuntimeMethodHandle` | `IsConstructor` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsConstructor not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the ty | `MethodMap.ReflectHandle.cs:55` |
| 97 | `System.RuntimeMethodHandle` | `IsDynamicMethod` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsDynamicMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 98 | `System.RuntimeMethodHandle` | `IsGenericMethodDefinition` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsGenericMethodDefinition not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeTy | `MethodMap.ReflectHandle.cs:55` |
| 99 | `System.RuntimeMethodHandle` | `PerformSecurityCheck` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::PerformSecurityCheck not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on | `MethodMap.ReflectHandle.cs:55` |
| 100 | `System.RuntimeMethodHandle` | `_GetCurrentMethod` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::_GetCurrentMethod not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on th | `MethodMap.ReflectHandle.cs:55` |
| 101 | `System.RuntimeType` | `AllocateValueType` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 102 | `System.RuntimeType` | `CanValueSpecialCast` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 103 | `System.RuntimeType` | `GetTypeFromCLSIDImpl` | internal/internal/static | 影子 | 影子：由 `System.Type::GetTypeFromCLSID` 承接（`il2cpp::runtime::il2cpp_com_type_from_clsid`） | `MethodMap.Type.cs:787` |
| 104 | `System.RuntimeType` | `GetTypeFromProgIDImpl` | internal/internal/static | 影子 | 影子：由 `System.Type::GetTypeFromProgID` 承接（`il2cpp::runtime::il2cpp_com_type_from_progid`） | `MethodMap.Type.cs:782` |
| 105 | `System.RuntimeTypeHandle` | `Allocate` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::Allocate not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type hand | `MethodMap.ReflectHandle.cs:55` |
| 106 | `System.RuntimeTypeHandle` | `CompareCanonicalHandles` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CompareCanonicalHandles not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 107 | `System.RuntimeTypeHandle` | `CreateCaInstance` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CreateCaInstance not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 108 | `System.RuntimeTypeHandle` | `CreateInstance` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::CreateInstance not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 109 | `System.RuntimeTypeHandle` | `GetAssembly` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetAssembly not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 110 | `System.RuntimeTypeHandle` | `GetBaseType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetBaseType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 111 | `System.RuntimeTypeHandle` | `GetCorElementType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetCorElementType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 112 | `System.RuntimeTypeHandle` | `GetElementType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetElementType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 113 | `System.RuntimeTypeHandle` | `GetModule` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetModule not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 114 | `System.RuntimeTypeHandle` | `HasInstantiation` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::HasInstantiation not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 115 | `System.RuntimeTypeHandle` | `IsComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsComObject not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 116 | `System.RuntimeTypeHandle` | `IsGenericTypeDefinition` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsGenericTypeDefinition not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType o | `MethodMap.ReflectHandle.cs:55` |
| 117 | `System.RuntimeTypeHandle` | `IsGenericVariable` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsGenericVariable not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the  | `MethodMap.ReflectHandle.cs:55` |
| 118 | `System.RuntimeTypeHandle` | `IsInterface` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsInterface not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 119 | `System.RuntimeTypeHandle` | `IsValueType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsValueType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 120 | `System.Signature` | `GetSignature` | internal/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 121 | `System.String` | `PadHelper` | public/private/instance | 影子 | 影子：由 `System.String::PadLeft` 承接（`il2cpp::runtime::icall_string_pad_left`） | `MethodMap.String.cs:243` |
| 122 | `System.String` | `ReplaceInternal` | public/private/instance | 影子 | 影子：由 `System.String::Replace` 承接（`il2cpp::runtime::icall_string_replace`） | `MethodMap.String.cs:137` |
| 123 | `System.String` | `ReplaceInternal` | public/private/instance | 影子 | 影子：由 `System.String::Replace` 承接（`il2cpp::runtime::icall_string_replace`） | `MethodMap.String.cs:137` |
| 124 | `System.Text.Normalization` | `nativeNormalizationIsNormalizedString` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 125 | `System.Text.Normalization` | `nativeNormalizationNormalizeString` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 126 | `System.Threading.CompressedStack` | `DestroyDCSList` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 127 | `System.Threading.CompressedStack` | `GetDCSCount` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 128 | `System.Threading.CompressedStack` | `GetDelayedCompressedStack` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 129 | `System.Threading.CompressedStack` | `GetDomainCompressedStack` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 130 | `System.Threading.CompressedStack` | `GetHomogeneousPLS` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 131 | `System.Threading.CompressedStack` | `IsImmediateCompletionCandidate` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 132 | `System.Threading.HostExecutionContextManager` | `CaptureHostSecurityContext` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 133 | `System.Threading.HostExecutionContextManager` | `HostSecurityManagerPresent` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 134 | `System.Threading.HostExecutionContextManager` | `SetHostSecurityContext` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 135 | `System.Threading.Interlocked` | `_CompareExchange` | public/private/static | 影子 | 影子：由 `System.Threading.Interlocked::CompareExchange` 承接（`il2cpp::runtime::il2cpp_interlocked_compare_exchange_double`） | `MethodMap.Interlocked.cs:101` |
| 136 | `System.Threading.Interlocked` | `_Exchange` | public/private/static | 影子 | 影子：由 `System.Threading.Interlocked::Exchange` 承接（`il2cpp::runtime::il2cpp_interlocked_exchange_double`） | `MethodMap.Interlocked.cs:63` |
| 137 | `System.Threading.Monitor` | `IsEnteredNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 138 | `System.Threading.Monitor` | `ObjPulse` | public/private/static | 影子 | 影子：由 `System.Threading.Monitor::Pulse` 承接（`il2cpp::runtime::il2cpp_monitor_pulse`） | `MethodMap.Monitor.cs:91` |
| 139 | `System.Threading.Monitor` | `ObjPulseAll` | public/private/static | 影子 | 影子：由 `System.Threading.Monitor::PulseAll` 承接（`il2cpp::runtime::il2cpp_monitor_pulse`） | `MethodMap.Monitor.cs:92` |
| 140 | `System.Threading.Monitor` | `ObjWait` | public/private/static | 影子 | 影子：由 `System.Threading.Monitor::Wait` 承接（`il2cpp::runtime::il2cpp_monitor_wait`） | `MethodMap.Monitor.cs:98` |
| 141 | `System.Threading.Monitor` | `ReliableEnter` | public/private/static | 影子 | 影子：由 `System.Threading.Monitor::Enter` 承接（`il2cpp::runtime::il2cpp_monitor_enter`） | `MethodMap.Monitor.cs:45` |
| 142 | `System.Threading.Monitor` | `ReliableEnterTimeout` | public/private/static | 影子 | 影子：由 `System.Threading.Monitor::TryEnter` 承接（`il2cpp::runtime::il2cpp_monitor_try_enter`） | `MethodMap.Monitor.cs:63` |
| 143 | `System.Threading.OverlappedData` | `AllocateNativeOverlapped` | internal/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 144 | `System.Threading.OverlappedData` | `FreeNativeOverlapped` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 145 | `System.Threading.OverlappedData` | `GetOverlappedFromNative` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 146 | `System.Threading.ReaderWriterLock` | `AcquireReaderLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：AcquireReaderLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 147 | `System.Threading.ReaderWriterLock` | `AcquireWriterLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：AcquireWriterLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 148 | `System.Threading.ReaderWriterLock` | `DowngradeFromWriterLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：DowngradeFromWriterLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (r | `MethodMap.Legacy.cs:242` |
| 149 | `System.Threading.ReaderWriterLock` | `FCallReleaseLock` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：FCallReleaseLock is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descript | `MethodMap.Legacy.cs:242` |
| 150 | `System.Threading.ReaderWriterLock` | `FCallUpgradeToWriterLock` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：FCallUpgradeToWriterLock is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime  | `MethodMap.Legacy.cs:242` |
| 151 | `System.Threading.ReaderWriterLock` | `PrivateGetIsReaderLockHeld` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateGetIsReaderLockHeld is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtim | `MethodMap.Legacy.cs:242` |
| 152 | `System.Threading.ReaderWriterLock` | `PrivateGetIsWriterLockHeld` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateGetIsWriterLockHeld is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtim | `MethodMap.Legacy.cs:242` |
| 153 | `System.Threading.ReaderWriterLock` | `PrivateGetWriterSeqNum` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateGetWriterSeqNum is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime de | `MethodMap.Legacy.cs:242` |
| 154 | `System.Threading.ReaderWriterLock` | `PrivateInitialize` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：PrivateInitialize is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descrip | `MethodMap.Legacy.cs:242` |
| 155 | `System.Threading.ReaderWriterLock` | `ReleaseReaderLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：ReleaseReaderLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 156 | `System.Threading.ReaderWriterLock` | `ReleaseWriterLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：ReleaseWriterLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime | `MethodMap.Legacy.cs:242` |
| 157 | `System.Threading.ReaderWriterLock` | `RestoreLockInternal` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：RestoreLockInternal is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descr | `MethodMap.Legacy.cs:242` |
| 158 | `System.Threading.RegisteredWaitHandleSafe` | `UnregisterWaitNative` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 159 | `System.Threading.Thread` | `AbortInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 160 | `System.Threading.Thread` | `GetCurrentThreadNative` | public/private/static | 影子 | 影子：由 `System.Threading.Thread::get_CurrentThread` 承接（`il2cpp::runtime::icall_threadobj_get_current_thread`） | `MethodMap.Thread.cs:212` |
| 161 | `System.Threading.Thread` | `GetDomainInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 162 | `System.Threading.Thread` | `GetFastDomainInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 163 | `System.Threading.Thread` | `GetPriorityNative` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::get_Priority` 承接（`il2cpp::runtime::icall_threadobj_get_priority`） | `MethodMap.Thread.cs:190` |
| 164 | `System.Threading.Thread` | `GetThreadStateNative` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::get_ThreadState` 承接（`il2cpp::runtime::icall_threadobj_get_thread_state`） | `MethodMap.Thread.cs:217` |
| 165 | `System.Threading.Thread` | `InterruptInternal` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::Interrupt` 承接（`il2cpp::runtime::icall_threadobj_interrupt`） | `MethodMap.Thread.cs:249` |
| 166 | `System.Threading.Thread` | `IsBackgroundNative` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::get_IsBackground` 承接（`il2cpp::runtime::icall_threadobj_get_is_background`） | `MethodMap.Thread.cs:204` |
| 167 | `System.Threading.Thread` | `JoinInternal` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::Join` 承接（`il2cpp::runtime::icall_threadobj_join_timeout`） | `MethodMap.Thread.cs:169` |
| 168 | `System.Threading.Thread` | `ResetAbortNative` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 169 | `System.Threading.Thread` | `ResumeInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 170 | `System.Threading.Thread` | `SetBackgroundNative` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::set_IsBackground` 承接（`il2cpp::runtime::icall_threadobj_set_is_background`） | `MethodMap.Thread.cs:208` |
| 171 | `System.Threading.Thread` | `SetPriorityNative` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::set_Priority` 承接（`il2cpp::runtime::icall_threadobj_set_priority`） | `MethodMap.Thread.cs:194` |
| 172 | `System.Threading.Thread` | `SetStart` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 173 | `System.Threading.Thread` | `SleepInternal` | public/private/static | 影子 | 影子：由 `System.Threading.Thread::Sleep` 承接（`il2cpp::runtime::icall_thread_sleep`） | `MethodMap.Thread.cs:47` |
| 174 | `System.Threading.Thread` | `SpinWaitInternal` | public/private/static | 影子 | 影子：由 `System.Threading.Thread::SpinWait` 承接（`il2cpp::runtime::icall_threadobj_spin_wait`） | `MethodMap.Thread.cs:52` |
| 175 | `System.Threading.Thread` | `StartInternal` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::Start` 承接（`il2cpp::runtime::icall_threadobj_start`） | `MethodMap.Thread.cs:152` |
| 176 | `System.Threading.Thread` | `StartupSetApartmentStateInternal` | public/private/instance | 影子 | 影子：由 `System.Threading.Thread::Start` 承接（`il2cpp::runtime::icall_threadobj_start`） | `MethodMap.Thread.cs:152` |
| 177 | `System.Threading.Thread` | `SuspendInternal` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 178 | `System.Threading.Thread` | `nativeGetSafeCulture` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 179 | `System.Threading.Thread` | `nativeSetThreadUILocale` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 180 | `System.Threading.ThreadPool` | `BindIOCompletionCallbackNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 181 | `System.Threading.ThreadPool` | `GetAvailableThreadsNative` | public/private/static | 影子 | 影子：由 `System.Threading.ThreadPool::GetAvailableThreads` 承接（`il2cpp::runtime::icall_thread_pool_get_available_threads_out`） | `MethodMap.ThreadPool.cs:51` |
| 182 | `System.Threading.ThreadPool` | `GetMaxThreadsNative` | public/private/static | 影子 | 影子：由 `System.Threading.ThreadPool::GetMaxThreads` 承接（`il2cpp::runtime::icall_thread_pool_get_max_threads_out`） | `MethodMap.ThreadPool.cs:50` |
| 183 | `System.Threading.ThreadPool` | `GetMinThreadsNative` | public/private/static | 影子 | 影子：由 `System.Threading.ThreadPool::GetMinThreads` 承接（`il2cpp::runtime::icall_thread_pool_get_min_threads_out`） | `MethodMap.ThreadPool.cs:49` |
| 184 | `System.Threading.ThreadPool` | `PostQueuedCompletionStatus` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 185 | `System.Threading.ThreadPool` | `RegisterWaitForSingleObjectNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 186 | `System.Threading.ThreadPool` | `SetMaxThreadsNative` | public/private/static | 影子 | 影子：由 `System.Threading.ThreadPool::SetMaxThreads` 承接（`il2cpp::runtime::il2cpp_threadpool_set_max_threads`） | `MethodMap.ThreadPool.cs:42` |
| 187 | `System.Threading.ThreadPool` | `SetMinThreadsNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 188 | `System.Threading.WaitHandle` | `SignalAndWaitOne` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 189 | `System.Threading.WaitHandle` | `WaitMultiple` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 190 | `System.Threading.WaitHandle` | `WaitOneNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 191 | `System.TimeSpan` | `LegacyFormatMode` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 192 | `System.TypedReference` | `InternalMakeTypedReference` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 193 | `System.TypedReference` | `InternalSetTypedReference` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 194 | `System.TypedReference` | `InternalToObject` | public/internal/static | 影子 | 影子：由 `System.Array::GetValue` 承接（`il2cpp::il2cpp_array_get_value`） | `MethodMap.Array.cs:1129` |
| 195 | `System.ValueType` | `CanCompareBits` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 196 | `System.ValueType` | `FastEqualsCheck` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 197 | `System.WeakReference` | `Create` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 198 | `System.WeakReference` | `IsTrackResurrection` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |

### P2（173 条）

仅经反射/序列化/调试器/COM 宿主/interop 路径可达，或链路深度 ≥4。

| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |
|---|---|---|---|---|---|---|
| 1 | `Microsoft.Runtime.Hosting.IClrStrongName` | `StrongNameGetPublicKey` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 2 | `Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr` | `StrongNameFreeBuffer` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 3 | `System.AppDomain` | `GetDynamicDir` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetDynamicDir is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT proces | `MethodMap.Legacy.cs:193` |
| 4 | `System.AppDomain` | `_nExecuteAssembly` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：_nExecuteAssembly is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT pr | `MethodMap.Legacy.cs:193` |
| 5 | `System.AppDomain` | `nApplyPolicy` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nApplyPolicy is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT process | `MethodMap.Legacy.cs:193` |
| 6 | `System.AppDomain` | `nChangeSecurityPolicy` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nChangeSecurityPolicy is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AO | `MethodMap.Legacy.cs:193` |
| 7 | `System.AppDomain` | `nEnableMonitoring` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nEnableMonitoring is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT pr | `MethodMap.Legacy.cs:193` |
| 8 | `System.AppDomain` | `nGetAssemblies` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetAssemblies is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT proce | `MethodMap.Legacy.cs:193` |
| 9 | `System.AppDomain` | `nGetFriendlyName` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetFriendlyName is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT pro | `MethodMap.Legacy.cs:193` |
| 10 | `System.AppDomain` | `nGetLastSurvivedMemorySize` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetLastSurvivedMemorySize is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: t | `MethodMap.Legacy.cs:193` |
| 11 | `System.AppDomain` | `nGetLastSurvivedProcessMemorySize` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nGetLastSurvivedProcessMemorySize is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descri | `MethodMap.Legacy.cs:193` |
| 12 | `System.AppDomain` | `nGetTotalAllocatedMemorySize` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetTotalAllocatedMemorySize is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: | `MethodMap.Legacy.cs:193` |
| 13 | `System.AppDomain` | `nGetTotalProcessorTime` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nGetTotalProcessorTime is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the A | `MethodMap.Legacy.cs:193` |
| 14 | `System.AppDomain` | `nIsDefaultAppDomainForEvidence` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：nIsDefaultAppDomainForEvidence is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descripto | `MethodMap.Legacy.cs:193` |
| 15 | `System.AppDomain` | `nMonitoringIsEnabled` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nMonitoringIsEnabled is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT | `MethodMap.Legacy.cs:193` |
| 16 | `System.AppDomainSetup` | `UpdateContextProperty` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：UpdateContextProperty is part of System.AppDomainSetup -- only the probed ApplicationBase/ConfigurationFile rack-arms are wired in the adm1 closure: L | `MethodMap.Legacy.cs:216` |
| 17 | `System.Diagnostics.Debugger` | `BreakInternal` | public/private/static | 影子 | 影子：由 `System.Diagnostics.Debugger::Break` 承接（`il2cpp::runtime::il2cpp_debugger_break`） | `MethodMap.Debugger.cs:48` |
| 18 | `System.Diagnostics.Debugger` | `CustomNotification` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 19 | `System.Diagnostics.Debugger` | `LaunchInternal` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 20 | `System.Globalization.CalendarData` | `nativeGetCalendarData` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 21 | `System.Globalization.CultureData` | `nativeEnumTimeFormats` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 22 | `System.Mda` | `DateTimeInvalidLocalFormat` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 23 | `System.Mda` | `FireInvalidGCHandleCookieProbe` | internal/internal/static | 影子 | 影子：由 `System.Runtime.InteropServices.GCHandle::FromIntPtr` 承接（`::identity::`） | `MethodMap.GCHandle.cs:83` |
| 24 | `System.Mda` | `IsStreamWriterBufferedDataLostCaptureAllocatedCallStack` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 25 | `System.Mda` | `IsStreamWriterBufferedDataLostEnabled` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 26 | `System.Reflection.AssemblyName` | `EscapeCodeBase` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 27 | `System.Reflection.AssemblyName` | `ReferenceMatchesDefinitionInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 28 | `System.Reflection.AssemblyName` | `nGetFileInformation` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 29 | `System.Reflection.AssemblyName` | `nGetPublicKeyToken` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 30 | `System.Reflection.AssemblyName` | `nInit` | public/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 31 | `System.Reflection.AssemblyName` | `nToString` | public/private/instance | 影子 | 影子：由 `System.Reflection.AssemblyName::get_FullName` 承接（`il2cpp::il2cpp_assemblyname_get_full_name`） | `MethodMap.Type.cs:985` |
| 32 | `System.Reflection.CustomAttribute` | `_CreateCaObject` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 33 | `System.Reflection.CustomAttribute` | `_GetPropertyOrFieldData` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 34 | `System.Reflection.CustomAttribute` | `_ParseAttributeUsageAttribute` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 35 | `System.Reflection.Emit.AssemblyBuilder` | `GetInMemoryAssemblyModule` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 36 | `System.Reflection.Emit.AssemblyBuilder` | `GetOnDiskAssemblyModule` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 37 | `System.Reflection.Emit.AssemblyBuilder` | `nCreateDynamicAssembly` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 38 | `System.Reflection.Emit.ModuleBuilder` | `nCreateISymWriterForDynamicModule` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 39 | `System.Reflection.MetadataImport` | `_Enum` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 40 | `System.Reflection.MetadataImport` | `_GetCustomAttributeProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 41 | `System.Reflection.MetadataImport` | `_GetMemberRefProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 42 | `System.Reflection.MetadataImport` | `_GetParentToken` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 43 | `System.Reflection.MetadataImport` | `_GetPropertyProps` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 44 | `System.Reflection.MetadataImport` | `_GetSigOfMethodDef` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 45 | `System.Reflection.MetadataImport` | `_IsValidToken` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 46 | `System.Reflection.PseudoCustomAttribute` | `_GetSecurityAttributes` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 47 | `System.Reflection.RuntimeAssembly` | `AptcaCheck` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 48 | `System.Reflection.RuntimeAssembly` | `IsFrameworkAssembly` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 49 | `System.Reflection.RuntimeAssembly` | `_nLoad` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 50 | `System.Reflection.RuntimeAssembly` | `nLoadFile` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 51 | `System.Reflection.RuntimeAssembly` | `nLoadImage` | internal/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 52 | `System.Reflection.SecurityContextFrame` | `Pop` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 53 | `System.Reflection.SecurityContextFrame` | `Push` | internal/public/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 54 | `System.Runtime.CompilerServices.RuntimeHelpers` | `_PrepareMethod` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 55 | `System.Runtime.CompilerServices.RuntimeHelpers` | `_RunClassConstructor` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers._RunClassConstructor is a runtime type-init driver; the transpiler drives type-initialization | `MethodMap.RuntimeHelpers.cs:94` |
| 56 | `System.Runtime.CompilerServices.RuntimeHelpers` | `_RunModuleConstructor` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (③ no runtime descriptor): RuntimeHelpers._RunModuleConstructor is a module-level constructor driver (module ctors are not in the M3 mo | `MethodMap.RuntimeHelpers.cs:99` |
| 57 | `System.Runtime.InteropServices.ArrayWithOffset` | `CalculateCount` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 58 | `System.Runtime.InteropServices.CriticalHandle` | `FireCustomerDebugProbe` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 59 | `System.Runtime.InteropServices.GCHandle` | `InternalAddrOfPinnedObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalAddrOfPinnedObject -- the runtime surface covers Alloc/Fre | `MethodMap.GCHandle.cs:101` |
| 60 | `System.Runtime.InteropServices.GCHandle` | `InternalAlloc` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalAlloc -- the runtime surface covers Alloc/Free/Target/Addr | `MethodMap.GCHandle.cs:101` |
| 61 | `System.Runtime.InteropServices.GCHandle` | `InternalCheckDomain` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalCheckDomain -- the runtime surface covers Alloc/Free/Targe | `MethodMap.GCHandle.cs:101` |
| 62 | `System.Runtime.InteropServices.GCHandle` | `InternalCompareExchange` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalCompareExchange -- the runtime surface covers Alloc/Free/T | `MethodMap.GCHandle.cs:101` |
| 63 | `System.Runtime.InteropServices.GCHandle` | `InternalFree` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalFree -- the runtime surface covers Alloc/Free/Target/AddrO | `MethodMap.GCHandle.cs:101` |
| 64 | `System.Runtime.InteropServices.GCHandle` | `InternalGet` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalGet -- the runtime surface covers Alloc/Free/Target/AddrOf | `MethodMap.GCHandle.cs:101` |
| 65 | `System.Runtime.InteropServices.GCHandle` | `InternalSet` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such member): System.Runtime.InteropServices.GCHandle::InternalSet -- the runtime surface covers Alloc/Free/Target/AddrOf | `MethodMap.GCHandle.cs:101` |
| 66 | `System.Runtime.InteropServices.Marshal` | `CopyToManaged` | public/private/static | 影子 | 影子：由 `System.Runtime.InteropServices.Marshal::Copy` 承接（`il2cpp::runtime::icall_marshal_copy_to_unmanaged_elem`） | `MethodMap.Marshal.cs:182` |
| 67 | `System.Runtime.InteropServices.Marshal` | `CopyToNative` | public/private/static | 影子 | 影子：由 `System.Runtime.InteropServices.Marshal::Copy` 承接（`il2cpp::runtime::icall_marshal_copy_to_unmanaged_elem`） | `MethodMap.Marshal.cs:182` |
| 68 | `System.Runtime.InteropServices.Marshal` | `FCallGenerateGuidForType` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGenerateGuidForType belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshal | `MethodMap.Marshal.cs:86` |
| 69 | `System.Runtime.InteropServices.Marshal` | `FCallGetTypeInfoGuid` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGetTypeInfoGuid belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, | `MethodMap.Marshal.cs:86` |
| 70 | `System.Runtime.InteropServices.Marshal` | `FCallGetTypeLibGuid` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGetTypeLibGuid belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling,  | `MethodMap.Marshal.cs:86` |
| 71 | `System.Runtime.InteropServices.Marshal` | `FCallGetTypeLibGuidForAssembly` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): FCallGetTypeLibGuidForAssembly belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library m | `MethodMap.Marshal.cs:86` |
| 72 | `System.Runtime.InteropServices.Marshal` | `GetComInterfaceForObjectNative` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetComInterfaceForObjectNative belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library m | `MethodMap.Marshal.cs:86` |
| 73 | `System.Runtime.InteropServices.Marshal` | `GetDelegateForFunctionPointerInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 74 | `System.Runtime.InteropServices.Marshal` | `GetExceptionForHRInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 75 | `System.Runtime.InteropServices.Marshal` | `GetFunctionPointerForDelegateInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 76 | `System.Runtime.InteropServices.Marshal` | `GetHRForException_WinRT` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 77 | `System.Runtime.InteropServices.Marshal` | `GetIDispatchForObjectNative` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetIDispatchForObjectNative belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library mars | `MethodMap.Marshal.cs:86` |
| 78 | `System.Runtime.InteropServices.Marshal` | `GetIUnknownForObjectNative` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetIUnknownForObjectNative belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marsh | `MethodMap.Marshal.cs:86` |
| 79 | `System.Runtime.InteropServices.Marshal` | `GetLoadedTypeForGUID` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetLoadedTypeForGUID belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, | `MethodMap.Marshal.cs:86` |
| 80 | `System.Runtime.InteropServices.Marshal` | `GetNativeActivationFactory` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 81 | `System.Runtime.InteropServices.Marshal` | `GetRawIUnknownForComObjectNoAddRef` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetRawIUnknownForComObjectNoAddRef belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-libra | `MethodMap.Marshal.cs:86` |
| 82 | `System.Runtime.InteropServices.Marshal` | `GetTypeLibVersion` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): GetTypeLibVersion belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VA | `MethodMap.Marshal.cs:86` |
| 83 | `System.Runtime.InteropServices.Marshal` | `InitializeManagedWinRTFactoryObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InitializeManagedWinRTFactoryObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-libr | `MethodMap.Marshal.cs:86` |
| 84 | `System.Runtime.InteropServices.Marshal` | `InternalCreateWrapperOfType` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalCreateWrapperOfType belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library mars | `MethodMap.Marshal.cs:86` |
| 85 | `System.Runtime.InteropServices.Marshal` | `InternalFinalReleaseComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalFinalReleaseComObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library ma | `MethodMap.Marshal.cs:86` |
| 86 | `System.Runtime.InteropServices.Marshal` | `InternalGetComSlotForMethodInfo` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalGetComSlotForMethodInfo belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library  | `MethodMap.Marshal.cs:86` |
| 87 | `System.Runtime.InteropServices.Marshal` | `InternalGetThreadFromFiberCookie` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 88 | `System.Runtime.InteropServices.Marshal` | `InternalReleaseComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalReleaseComObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshal | `MethodMap.Marshal.cs:86` |
| 89 | `System.Runtime.InteropServices.Marshal` | `InternalSwitchCCW` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalSwitchCCW belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library marshaling, VA | `MethodMap.Marshal.cs:86` |
| 90 | `System.Runtime.InteropServices.Marshal` | `InternalWrapIUnknownWithComObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): InternalWrapIUnknownWithComObject belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-librar | `MethodMap.Marshal.cs:86` |
| 91 | `System.Runtime.InteropServices.Marshal` | `OffsetOfHelper` | public/private/static | 影子 | 影子：由 `System.Runtime.InteropServices.Marshal::OffsetOf` 承接（`il2cpp::runtime::il2cpp_marshal_offset_of_struct_named`） | `MethodMap.Marshal.cs:293` |
| 92 | `System.Runtime.InteropServices.Marshal` | `PtrToStructureHelper` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 93 | `System.Runtime.InteropServices.Marshal` | `SizeOfHelper` | public/internal/static | 影子 | 影子：由 `System.Runtime.InteropServices.Marshal::SizeOf` 承接（`il2cpp::runtime::icall_marshal_size_of_object`） | `MethodMap.Marshal.cs:241` |
| 94 | `System.Runtime.InteropServices.Marshal` | `SizeOfType` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 95 | `System.Runtime.InteropServices.Marshal` | `ThrowExceptionForHRInternal` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 96 | `System.Runtime.InteropServices.Marshal` | `_GetTypeLibVersionForAssembly` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (runtime-descriptor/COM): _GetTypeLibVersionForAssembly belongs to the COM interop domain (RCW/CCW, IUnknown/IDispatch, type-library ma | `MethodMap.Marshal.cs:86` |
| 97 | `System.Runtime.InteropServices.RegistrationServices` | `RegisterTypeForComClientsExNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 98 | `System.Runtime.InteropServices.RegistrationServices` | `RegisterTypeForComClientsNative` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 99 | `System.Runtime.InteropServices.RuntimeEnvironment` | `GetRuntimeDirectoryImpl` | public/internal/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 100 | `System.Runtime.InteropServices.SafeHandle` | `InternalDispose` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 101 | `System.Runtime.InteropServices.SafeHandle` | `InternalFinalize` | public/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 102 | `System.Runtime.InteropServices.TypeLibConverter` | `nConvertAssemblyToTypeLib` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 103 | `System.Runtime.InteropServices.TypeLibConverter` | `nConvertTypeLibToMetadata` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 104 | `System.Runtime.Remoting.Contexts.Context` | `SetupInternalContext` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：SetupInternalContext is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested  | `MethodMap.Legacy.cs:44` |
| 105 | `System.Runtime.Remoting.Messaging.Message` | `Dispatch` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：Dispatch is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject' | `MethodMap.Legacy.cs:44` |
| 106 | `System.Runtime.Remoting.Messaging.Message` | `GetThisPtr` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetThisPtr is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD rejec | `MethodMap.Legacy.cs:44` |
| 107 | `System.Runtime.Remoting.Messaging.Message` | `OutToUnmanagedDebugger` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：OutToUnmanagedDebugger is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 108 | `System.Runtime.Remoting.Messaging.StackBuilderSink` | `_PrivateProcessMessage` | internal/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：_PrivateProcessMessage is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 109 | `System.Runtime.Remoting.Proxies.RealProxy` | `GetStub` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：GetStub is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject') | `MethodMap.Legacy.cs:44` |
| 110 | `System.Runtime.Remoting.RemotingServices` | `AllocateUninitializedObject` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：AllocateUninitializedObject is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'sug | `MethodMap.Legacy.cs:44` |
| 111 | `System.Runtime.Remoting.RemotingServices` | `AlwaysUnwrap` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：AlwaysUnwrap is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD rej | `MethodMap.Legacy.cs:44` |
| 112 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerRemotingServerReceivingMessage` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerRemotingServerReceivingMessage is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-f | `MethodMap.Legacy.cs:44` |
| 113 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerRemotingServerSendingReply` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerRemotingServerSendingReply is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face  | `MethodMap.Legacy.cs:44` |
| 114 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerTrackRemoting` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerTrackRemoting is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'sugges | `MethodMap.Legacy.cs:44` |
| 115 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerTrackRemotingAsync` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerTrackRemotingAsync is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 's | `MethodMap.Legacy.cs:44` |
| 116 | `System.Runtime.Remoting.RemotingServices` | `CORProfilerTrackRemotingCookie` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CORProfilerTrackRemotingCookie is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, ' | `MethodMap.Legacy.cs:44` |
| 117 | `System.Runtime.Remoting.RemotingServices` | `CallDefaultCtor` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CallDefaultCtor is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD  | `MethodMap.Legacy.cs:44` |
| 118 | `System.Runtime.Remoting.RemotingServices` | `CreateTransparentProxy` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：CreateTransparentProxy is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggeste | `MethodMap.Legacy.cs:44` |
| 119 | `System.Runtime.Remoting.RemotingServices` | `nSetRemoteActivationConfigured` | public/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：nSetRemoteActivationConfigured is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, ' | `MethodMap.Legacy.cs:44` |
| 120 | `System.Runtime.Serialization.FormatterServices` | `GetEnableUnsafeTypeForwarders` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 121 | `System.Runtime.Serialization.FormatterServices` | `nativeGetSafeUninitializedObject` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 122 | `System.Runtime.Serialization.FormatterServices` | `nativeGetUninitializedObject` | public/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 123 | `System.RuntimeMethodHandle` | `GetSlot` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetSlot not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type han | `MethodMap.ReflectHandle.cs:55` |
| 124 | `System.RuntimeMethodHandle` | `GetSpecialSecurityFlags` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::GetSpecialSecurityFlags not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType | `MethodMap.ReflectHandle.cs:55` |
| 125 | `System.RuntimeMethodHandle` | `IsTypicalMethodDefinition` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::IsTypicalMethodDefinition not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeTy | `MethodMap.ReflectHandle.cs:55` |
| 126 | `System.RuntimeMethodHandle` | `SerializationInvoke` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeMethodHandle::SerializationInvoke not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on  | `MethodMap.ReflectHandle.cs:55` |
| 127 | `System.RuntimeTypeHandle` | `GetMethodAt` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetMethodAt not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the type h | `MethodMap.ReflectHandle.cs:55` |
| 128 | `System.RuntimeTypeHandle` | `GetNumVirtuals` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::GetNumVirtuals not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the typ | `MethodMap.ReflectHandle.cs:55` |
| 129 | `System.RuntimeTypeHandle` | `IsEquivalentType` | public/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：System.RuntimeTypeHandle::IsEquivalentType not wired: the reflect-handle surface covers get_Value / Equals / GetHashCode (plus GetRuntimeType on the t | `MethodMap.ReflectHandle.cs:55` |
| 130 | `System.Security.CodeAccessSecurityEngine` | `AllDomainsHomogeneousWithNoStackModifiers` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 131 | `System.Security.CodeAccessSecurityEngine` | `Check` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 132 | `System.Security.CodeAccessSecurityEngine` | `CheckNReturnSO` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 133 | `System.Security.CodeAccessSecurityEngine` | `GetZoneAndOriginInternal` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 134 | `System.Security.CodeAccessSecurityEngine` | `QuickCheckForAllDemands` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 135 | `System.Security.CodeAccessSecurityEngine` | `SpecialDemand` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 136 | `System.Security.Cryptography.Utils` | `_AcquireCSP` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 137 | `System.Security.Cryptography.Utils` | `_CreateCSP` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 138 | `System.Security.Cryptography.Utils` | `_DecryptData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 139 | `System.Security.Cryptography.Utils` | `_EncryptData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 140 | `System.Security.Cryptography.Utils` | `_ExportKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 141 | `System.Security.Cryptography.Utils` | `_GenerateKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 142 | `System.Security.Cryptography.Utils` | `_GetEnforceFipsPolicySetting` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 143 | `System.Security.Cryptography.Utils` | `_GetKeyParameter` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 144 | `System.Security.Cryptography.Utils` | `_GetKeySetSecurityInfo` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 145 | `System.Security.Cryptography.Utils` | `_GetProviderParameter` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 146 | `System.Security.Cryptography.Utils` | `_GetUserKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 147 | `System.Security.Cryptography.Utils` | `_ImportBulkKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 148 | `System.Security.Cryptography.Utils` | `_ImportCspBlob` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 149 | `System.Security.Cryptography.Utils` | `_ImportKey` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 150 | `System.Security.Cryptography.Utils` | `_OpenCSP` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 151 | `System.Security.Cryptography.Utils` | `_ProduceLegacyHmacValues` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 152 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_AddCertificateToStore` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 153 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_DuplicateCertContext` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 154 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_ExportCertificatesToBlob` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 155 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetCertRawData` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 156 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetDateNotAfter` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 157 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetDateNotBefore` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 158 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetIssuerName` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 159 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetPublicKeyOid` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 160 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetPublicKeyParameters` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 161 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetPublicKeyValue` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 162 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetSerialNumber` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 163 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetSubjectInfo` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 164 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_GetThumbprint` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 165 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_LoadCertFromBlob` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 166 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_LoadCertFromFile` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 167 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_OpenX509Store` | internal/private/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 168 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_QueryCertBlobType` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 169 | `System.Security.Cryptography.X509Certificates.X509Utils` | `_QueryCertFileType` | internal/internal/static | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 170 | `System.Security.FrameSecurityDescriptor` | `DecrementAssertCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 171 | `System.Security.FrameSecurityDescriptor` | `DecrementOverridesCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 172 | `System.Security.FrameSecurityDescriptor` | `IncrementAssertCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 173 | `System.Security.FrameSecurityDescriptor` | `IncrementOverridesCount` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |

### P3（283 条）

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
| 272 | `System.Utf8String` | `EqualsCaseSensitive` | internal/private/static | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 273 | `System.Variant` | `BoxEnum` | internal/private/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 274 | `System.Variant` | `GetR4FromVar` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 275 | `System.Variant` | `GetR8FromVar` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 276 | `System.Variant` | `SetFieldsObject` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 277 | `System.Variant` | `SetFieldsR4` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 278 | `System.Variant` | `SetFieldsR8` | internal/internal/instance | 缺（无接线） | 无 owner 分支 / 无方法分支 | `no wiring` |
| 279 | `System.WeakReference`1` | `Create` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 280 | `System.WeakReference`1` | `IsTrackResurrection` | public/private/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 281 | `Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs` | `.ctor` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (net48 has no such overload): WeakReference ctor arity 0 -- net48 has only WeakReference(object) and WeakReference(object, bool) | `MethodMap.WeakReference.cs:44` |
| 282 | `Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs` | `get_Enabled` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |
| 283 | `Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs` | `get_TraceLevel` | internal/public/instance | 缺（LOUD 拒绝） | LOUD 拒绝：LOUD whitelist (no runtime descriptor/model): System.WeakReference member not wired -- the il2cpp_gc_ext.h object icalls cover .ctor(object)/IsAlive/T | `MethodMap.WeakReference.cs:76` |

### 3.6 影子清单

共 **65** 条。判定依据：icall 的**直接调用者**是 public 面方法，且该 public 方法自身已接线；用户代码只发出那一次调用，因此这条 icall 不需要自己的接线。

| # | 声明类型 | 方法 | 可见性 | 承接的 public 方法 | 承接者的接线证据 |
|---|---|---|---|---|---|
| 1 | `System.Array` | `InternalGetReference` | public/private/instance | `System.Array::GetValue` | `MethodMap.Array.cs:1129` |
| 2 | `System.Array` | `InternalSetValue` | public/private/static | `System.Array::SetValue` | `MethodMap.Array.cs:1158` |
| 3 | `System.Array` | `TrySZBinarySearch` | public/private/static | `System.Array::BinarySearch` | `MethodMap.Array.cs:378` |
| 4 | `System.Array` | `TrySZIndexOf` | public/private/static | `System.Array::IndexOf` | `MethodMap.Array.cs:428` |
| 5 | `System.Array` | `TrySZLastIndexOf` | public/private/static | `System.Array::LastIndexOf` | `MethodMap.Array.cs:448` |
| 6 | `System.Array` | `TrySZReverse` | public/private/static | `System.Array::Reverse` | `MethodMap.Array.cs:527` |
| 7 | `System.Array` | `TrySZSort` | public/private/static | `System.Array::Sort` | `MethodMap.Array.cs:145` |
| 8 | `System.Buffer` | `IsPrimitiveTypeArray` | public/private/static | `System.Buffer::GetByte` | `MethodMap.Buffer.cs:71` |
| 9 | `System.Buffer` | `_ByteLength` | public/private/static | `System.Buffer::GetByte` | `MethodMap.Buffer.cs:71` |
| 10 | `System.Buffer` | `_GetByte` | public/private/static | `System.Buffer::GetByte` | `MethodMap.Buffer.cs:71` |
| 11 | `System.Buffer` | `_SetByte` | public/private/static | `System.Buffer::SetByte` | `MethodMap.Buffer.cs:81` |
| 12 | `System.Decimal` | `FCallCompare` | public/private/static | `System.Decimal::Compare` | `MethodMap.Decimal.cs:170` |
| 13 | `System.Decimal` | `FCallFloor` | public/private/static | `System.Decimal::Floor` | `MethodMap.Decimal.cs:305` |
| 14 | `System.Decimal` | `FCallRound` | public/private/static | `System.Decimal::Round` | `MethodMap.Decimal.cs:290` |
| 15 | `System.Decimal` | `FCallTruncate` | public/private/static | `System.Decimal::ToInt32` | `MethodMap.Decimal.cs:316` |
| 16 | `System.Diagnostics.Debugger` | `BreakInternal` | public/private/static | `System.Diagnostics.Debugger::Break` | `MethodMap.Debugger.cs:48` |
| 17 | `System.Enum` | `InternalBoxEnum` | public/private/static | `System.Enum::ToObject` | `MethodMap.Enum.cs:398` |
| 18 | `System.Enum` | `InternalCompareTo` | public/private/static | `System.Enum::CompareTo` | `MethodMap.Enum.cs:416` |
| 19 | `System.Enum` | `InternalHasFlag` | public/private/instance | `System.Enum::HasFlag` | `MethodMap.Enum.cs:404` |
| 20 | `System.Environment` | `GetCommandLineArgsNative` | public/private/static | `System.Environment::GetCommandLineArgs` | `MethodMap.Environment.cs:76` |
| 21 | `System.Exception` | `IsImmutableAgileException` | public/private/static | `System.Exception::get_Data` | `MethodMap.Exception.cs:215` |
| 22 | `System.GC` | `GetGenerationWR` | public/private/static | `System.GC::GetGeneration` | `MethodMap.GC.cs:54` |
| 23 | `System.GC` | `_CancelFullGCNotification` | public/private/static | `System.GC::CancelFullGCNotification` | `MethodMap.GC.cs:107` |
| 24 | `System.GC` | `_CollectionCount` | public/private/static | `System.GC::CollectionCount` | `MethodMap.GC.cs:96` |
| 25 | `System.GC` | `_GetAllocatedBytesForCurrentThread` | public/private/static | `System.GC::GetAllocatedBytesForCurrentThread` | `MethodMap.GC.cs:118` |
| 26 | `System.GC` | `_ReRegisterForFinalize` | public/private/static | `System.GC::ReRegisterForFinalize` | `MethodMap.GC.cs:57` |
| 27 | `System.GC` | `_RegisterForFullGCNotification` | public/private/static | `System.GC::RegisterForFullGCNotification` | `MethodMap.GC.cs:102` |
| 28 | `System.GC` | `_SuppressFinalize` | public/private/static | `System.GC::SuppressFinalize` | `MethodMap.GC.cs:56` |
| 29 | `System.Globalization.TextInfo` | `InternalChangeCaseChar` | public/private/static | `System.Globalization.TextInfo::ToLower` | `MethodMap.NumberFormat.cs:166` |
| 30 | `System.Globalization.TextInfo` | `InternalChangeCaseString` | public/private/static | `System.Globalization.TextInfo::ToLower` | `MethodMap.NumberFormat.cs:166` |
| 31 | `System.Mda` | `FireInvalidGCHandleCookieProbe` | internal/internal/static | `System.Runtime.InteropServices.GCHandle::FromIntPtr` | `MethodMap.GCHandle.cs:83` |
| 32 | `System.Reflection.AssemblyName` | `nToString` | public/private/instance | `System.Reflection.AssemblyName::get_FullName` | `MethodMap.Type.cs:985` |
| 33 | `System.Runtime.InteropServices.Marshal` | `CopyToManaged` | public/private/static | `System.Runtime.InteropServices.Marshal::Copy` | `MethodMap.Marshal.cs:182` |
| 34 | `System.Runtime.InteropServices.Marshal` | `CopyToNative` | public/private/static | `System.Runtime.InteropServices.Marshal::Copy` | `MethodMap.Marshal.cs:182` |
| 35 | `System.Runtime.InteropServices.Marshal` | `OffsetOfHelper` | public/private/static | `System.Runtime.InteropServices.Marshal::OffsetOf` | `MethodMap.Marshal.cs:293` |
| 36 | `System.Runtime.InteropServices.Marshal` | `SizeOfHelper` | public/internal/static | `System.Runtime.InteropServices.Marshal::SizeOf` | `MethodMap.Marshal.cs:241` |
| 37 | `System.RuntimeType` | `GetTypeFromCLSIDImpl` | internal/internal/static | `System.Type::GetTypeFromCLSID` | `MethodMap.Type.cs:787` |
| 38 | `System.RuntimeType` | `GetTypeFromProgIDImpl` | internal/internal/static | `System.Type::GetTypeFromProgID` | `MethodMap.Type.cs:782` |
| 39 | `System.String` | `PadHelper` | public/private/instance | `System.String::PadLeft` | `MethodMap.String.cs:243` |
| 40 | `System.String` | `ReplaceInternal` | public/private/instance | `System.String::Replace` | `MethodMap.String.cs:137` |
| 41 | `System.String` | `ReplaceInternal` | public/private/instance | `System.String::Replace` | `MethodMap.String.cs:137` |
| 42 | `System.Threading.Interlocked` | `_CompareExchange` | public/private/static | `System.Threading.Interlocked::CompareExchange` | `MethodMap.Interlocked.cs:101` |
| 43 | `System.Threading.Interlocked` | `_Exchange` | public/private/static | `System.Threading.Interlocked::Exchange` | `MethodMap.Interlocked.cs:63` |
| 44 | `System.Threading.Monitor` | `ObjPulse` | public/private/static | `System.Threading.Monitor::Pulse` | `MethodMap.Monitor.cs:91` |
| 45 | `System.Threading.Monitor` | `ObjPulseAll` | public/private/static | `System.Threading.Monitor::PulseAll` | `MethodMap.Monitor.cs:92` |
| 46 | `System.Threading.Monitor` | `ObjWait` | public/private/static | `System.Threading.Monitor::Wait` | `MethodMap.Monitor.cs:98` |
| 47 | `System.Threading.Monitor` | `ReliableEnter` | public/private/static | `System.Threading.Monitor::Enter` | `MethodMap.Monitor.cs:45` |
| 48 | `System.Threading.Monitor` | `ReliableEnterTimeout` | public/private/static | `System.Threading.Monitor::TryEnter` | `MethodMap.Monitor.cs:63` |
| 49 | `System.Threading.Thread` | `GetCurrentThreadNative` | public/private/static | `System.Threading.Thread::get_CurrentThread` | `MethodMap.Thread.cs:212` |
| 50 | `System.Threading.Thread` | `GetPriorityNative` | public/private/instance | `System.Threading.Thread::get_Priority` | `MethodMap.Thread.cs:190` |
| 51 | `System.Threading.Thread` | `GetThreadStateNative` | public/private/instance | `System.Threading.Thread::get_ThreadState` | `MethodMap.Thread.cs:217` |
| 52 | `System.Threading.Thread` | `InterruptInternal` | public/private/instance | `System.Threading.Thread::Interrupt` | `MethodMap.Thread.cs:249` |
| 53 | `System.Threading.Thread` | `IsBackgroundNative` | public/private/instance | `System.Threading.Thread::get_IsBackground` | `MethodMap.Thread.cs:204` |
| 54 | `System.Threading.Thread` | `JoinInternal` | public/private/instance | `System.Threading.Thread::Join` | `MethodMap.Thread.cs:169` |
| 55 | `System.Threading.Thread` | `SetBackgroundNative` | public/private/instance | `System.Threading.Thread::set_IsBackground` | `MethodMap.Thread.cs:208` |
| 56 | `System.Threading.Thread` | `SetPriorityNative` | public/private/instance | `System.Threading.Thread::set_Priority` | `MethodMap.Thread.cs:194` |
| 57 | `System.Threading.Thread` | `SleepInternal` | public/private/static | `System.Threading.Thread::Sleep` | `MethodMap.Thread.cs:47` |
| 58 | `System.Threading.Thread` | `SpinWaitInternal` | public/private/static | `System.Threading.Thread::SpinWait` | `MethodMap.Thread.cs:52` |
| 59 | `System.Threading.Thread` | `StartInternal` | public/private/instance | `System.Threading.Thread::Start` | `MethodMap.Thread.cs:152` |
| 60 | `System.Threading.Thread` | `StartupSetApartmentStateInternal` | public/private/instance | `System.Threading.Thread::Start` | `MethodMap.Thread.cs:152` |
| 61 | `System.Threading.ThreadPool` | `GetAvailableThreadsNative` | public/private/static | `System.Threading.ThreadPool::GetAvailableThreads` | `MethodMap.ThreadPool.cs:51` |
| 62 | `System.Threading.ThreadPool` | `GetMaxThreadsNative` | public/private/static | `System.Threading.ThreadPool::GetMaxThreads` | `MethodMap.ThreadPool.cs:50` |
| 63 | `System.Threading.ThreadPool` | `GetMinThreadsNative` | public/private/static | `System.Threading.ThreadPool::GetMinThreads` | `MethodMap.ThreadPool.cs:49` |
| 64 | `System.Threading.ThreadPool` | `SetMaxThreadsNative` | public/private/static | `System.Threading.ThreadPool::SetMaxThreads` | `MethodMap.ThreadPool.cs:42` |
| 65 | `System.TypedReference` | `InternalToObject` | public/internal/static | `System.Array::GetValue` | `MethodMap.Array.cs:1129` |

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
| public 面键数 | 121 | **121** | 保持（口径一致，可对齐比较） |
| public 面已接 | 54 | **91** | **推翻**：增加 37 个键 |
| public 面缺口 | 67 | **30**（LOUD 25 + 无接线 5） | **推翻**：减少 37 个键；且剩余缺口几乎全部是显式 LOUD 拒绝，不再是静默的幽灵调用 |

### 4.3 被推翻的具体旧结论

**A. 旧文档 §5「最高优先级缺口（10 个）」—— 10 条全部被推翻**，当前代码均已接线并有运行时实现：

| # | 旧文档认定 | 现状 | 证据 |
|---|---|---|---|
| 1 | `System.String..ctor` 缺 | 已接 — `new string(char, int)` → `icall_string_ctor_repeat` | `MethodMap.String.cs:15` |
| 2 | `System.String.IndexOfAny` 缺 | 已接 — → `icall_string_index_of_any_range` | `MethodMap.String.cs:92` |
| 3 | `System.String.LastIndexOfAny` 缺 | 已接 — → `icall_string_last_index_of_any_range` | `MethodMap.String.cs:114` |
| 4 | `System.Array.get_Rank` 缺 | 已接 — → `icall_array_get_rank` | `MethodMap.Array.cs:1096` |
| 5 | `System.ValueType.GetHashCode` 缺 | 已接 — → `icall_value_type_get_hash_code`（首字段分类） | `MethodMap.ValueType.cs:495` |
| 6 | `System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement` 缺 | 已接 — → `icall_marshal_unsafe_addr_of_pinned_array_element` | `MethodMap.Marshal.cs:421` |
| 7 | `System.Diagnostics.Debugger.get_IsAttached` 缺 | 已接 — → `il2cpp_debugger_is_attached`；Break 路径见下（BreakInternal 为影子） | `MethodMap.Debugger.cs:40` |
| 8 | `System.Runtime.InteropServices.Marshal.GetHRForException` 缺 | 已接 — → `icall_marshal_get_hr_for_exception` | `MethodMap.Marshal.cs:431` |
| 9 | `System.Runtime.InteropServices.Marshal.GetExceptionPointers` 缺 | 已接 — → `icall_marshal_get_exception_pointers` | `MethodMap.Marshal.cs:552` |
| 10 | `System.Runtime.InteropServices.SafeHandle.DangerousAddRef` 缺 | 已接 — SafeHandle 三项 → `icall_safehandle_dangerous_add_ref` 等 | `MethodMap.SafeHandle.cs:40` |

**B. 旧文档 §4 分域缺口表 —— 11 条具体条目被推翻**：

| 旧文档结论 | 现状 | 证据 |
|---|---|---|
| `System.Runtime.InteropServices.SafeHandle.DangerousAddRef` 称缺口 | 已接 | `MethodMap.SafeHandle.cs:40` |
| `System.Runtime.InteropServices.SafeHandle.DangerousRelease` 称缺口 | 已接 | `MethodMap.SafeHandle.cs:48` |
| `System.Runtime.InteropServices.SafeHandle.SetHandleAsInvalid` 称缺口 | 已接 | `MethodMap.SafeHandle.cs:52` |
| `System.Enum.Equals` 称缺口 | 已接 | `MethodMap.Enum.cs:363` |
| `System.Type.op_Inequality` 称缺口 | 已接 | `MethodMap.Type.cs:549` |
| `System.Threading.Interlocked.SpeculationBarrier` 称缺口 | 已接 | `MethodMap.Interlocked.cs:146` |
| `System.Threading.Thread.MemoryBarrier` 称缺口 | 已接 | `MethodMap.Thread.cs:63` |
| `System.Threading.Thread.get_IsThreadPoolThread` 称缺口 | 已接 | `MethodMap.Thread.cs:105` |
| `System.Array.get_LongLength` 称缺口 | 已接 | `MethodMap.Array.cs:1110` |
| `System.Array.GetLowerBound` 称缺口 | 已接 | `MethodMap.Array.cs:1114` |
| `System.Array.Initialize` 称缺口 | 已接 | `MethodMap.Array.cs:1118` |

**C. 旧文档 §6(b)「JIT/转译器已有语义，不算缺口」—— 2 条被推翻、4 条成立**：

- `Thread.MemoryBarrier` / `Interlocked.SpeculationBarrier`：旧文档称「空实现即可」——**推翻**，两者都已是真实运行时 icall（`icall_thread_memory_barrier`、`il2cpp_interlocked_speculation_barrier`），不再是空实现。
- `RuntimeHelpers.InitializeArray`：旧文档称折叠即可、不算缺口 —— **部分推翻**。折叠路径存在（`StackFold.FoldInitializeArray`），但未折叠的形状现在被显式 LOUD 拒绝（`MethodMap.RuntimeHelpers.cs:108`）。
- `Type.GetTypeFromHandle`：旧文档称已接 —— **成立**（`MethodMap.Type.cs:73`）。
- `RuntimeHelpers.GetHashCode` / `Equals` / `GetObjectValue`：旧文档称可折叠 —— **成立**（`Equals`/`GetObjectValue` 走 `::refeq::` / `::identity::` 折叠标记，`GetHashCode` 走真实 icall）。

**D. 旧文档 §7「内部 icall 764 个键由 public 包装承接」—— 口径被推翻**：严格按「直接调用者是已接线的 public 面」验证，只有 **65** 条成立，而不是按 owner 类型一概而论的 764。其余内部 icall 要么自身被接线，要么就是真缺口。

### 4.4 仍成立的旧结论（未被推翻）

- 「内部 icall 大多由 public 包装承接」这一**机制判断成立**（只是数量被高估，见 D）。
- `System.Runtime.Remoting.*` / `AppDomain` / `ArgIterator` / `ReaderWriterLock` 属 legacy：**成立**，当前全部为显式 LOUD 拒绝。
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

## 5. 下一步建议（P0/P1 优先队列）

### 5.1 P0（30 条，用户可直接调用）

| 声明类型 | 方法 | 判定 | 建议 | 证据 |
|---|---|---|---|---|
| `System.ArgIterator` | `GetRemainingCount` | 缺（LOUD 拒绝） | vararg 语义，保持 LOUD | `MethodMap.Legacy.cs:232` |
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

### 5.2 P1（198 条，常见 BCL API 间接可达）

按属主聚合，优先处理「用户高频 API 会走到的内部 icall」：

| 属主 | 条数 | 说明 |
|---|---|---|
| `System.Threading.Thread` | 21 | AbortInternal, GetCurrentThreadNative, GetDomainInternal, GetFastDomainInternal … |
| `System.RuntimeMethodHandle` | 15 | CheckLinktimeDemands, GetAttributes, GetDeclaringType, GetLoaderAllocator … |
| `System.RuntimeTypeHandle` | 15 | Allocate, CompareCanonicalHandles, CreateCaInstance, CreateInstance … |
| `System.Threading.ReaderWriterLock` | 12 | AcquireReaderLockInternal, AcquireWriterLockInternal, DowngradeFromWriterLockInternal, FCallReleaseLock … |
| `System.GC` | 9 | GetGenerationWR, _CancelFullGCNotification, _CollectionCount, _GetAllocatedBytesForCurrentThread … |
| `System.Number` | 9 | FormatDecimal, FormatDouble, FormatInt32, FormatInt64 … |
| `System.Array` | 8 | InternalCreate, InternalGetReference, InternalSetValue, TrySZBinarySearch … |
| `System.Exception` | 8 | CopyDynamicMethods, CopyStackTrace, GetMethodFromStackTrace, GetStackTracesDeepCopy … |
| `System.Threading.ThreadPool` | 8 | BindIOCompletionCallbackNative, GetAvailableThreadsNative, GetMaxThreadsNative, GetMinThreadsNative … |
| `System.Threading.CompressedStack` | 6 | DestroyDCSList, GetDCSCount, GetDelayedCompressedStack, GetDomainCompressedStack … |
| `System.Threading.Monitor` | 6 | IsEnteredNative, ObjPulse, ObjPulseAll, ObjWait … |
| `System.ArgIterator` | 5 | .ctor, .ctor, FCallGetNextArg, InternalGetNextArg … |
| `System.Decimal` | 5 | FCallCompare, FCallFloor, FCallRound, FCallToCurrency … |
| `System.Buffer` | 4 | IsPrimitiveTypeArray, _ByteLength, _GetByte, _SetByte |
| `System.Enum` | 4 | InternalBoxEnum, InternalCompareTo, InternalGetCorElementType, InternalHasFlag |
| `System.Globalization.CultureData` | 4 | LCIDToLocaleName, LocaleNameToLCID, nativeGetNumberFormatInfoValues, nativeInitCultureData |
| `System.ParseNumbers` | 4 | IntToString, LongToString, StringToInt, StringToLong |
| `System.RuntimeFieldHandle` | 4 | AcquiresContextFromThis, GetApproxDeclaringType, GetValue, SetValue |
| `System.RuntimeType` | 4 | AllocateValueType, CanValueSpecialCast, GetTypeFromCLSIDImpl, GetTypeFromProgIDImpl |
| `System.Delegate` | 3 | BindToMethodInfo, BindToMethodName, InternalAlloc |
| `System.Globalization.TextInfo` | 3 | InternalChangeCaseChar, InternalChangeCaseString, InternalGetCaseInsHash |
| `System.ModuleHandle` | 3 | GetDynamicMethod, GetMDStreamVersion, _GetMetadataImport |
| `System.String` | 3 | PadHelper, ReplaceInternal, ReplaceInternal |
| `System.Threading.HostExecutionContextManager` | 3 | CaptureHostSecurityContext, HostSecurityManagerPresent, SetHostSecurityContext |
| `System.Threading.OverlappedData` | 3 | AllocateNativeOverlapped, FreeNativeOverlapped, GetOverlappedFromNative |
| `System.Threading.WaitHandle` | 3 | SignalAndWaitOne, WaitMultiple, WaitOneNative |
| `System.TypedReference` | 3 | InternalMakeTypedReference, InternalSetTypedReference, InternalToObject |
| `System.Environment` | 2 | GetCommandLineArgsNative, GetVersionEx |
| `System.Globalization.CalendarData` | 2 | nativeGetCalendars, nativeGetTwoDigitYearMax |
| `System.Text.Normalization` | 2 | nativeNormalizationIsNormalizedString, nativeNormalizationNormalizeString |
| `System.Threading.Interlocked` | 2 | _CompareExchange, _Exchange |
| `System.ValueType` | 2 | CanCompareBits, FastEqualsCheck |
| `System.WeakReference` | 2 | Create, IsTrackResurrection |
| `System.BCLDebug` | 1 | GetRegistryLoggingValues |
| `System.Currency` | 1 | FCallToDecimal |
| `System.CurrentSystemTimeZone` | 1 | nativeGetTimeZoneMinuteOffset |
| `System.Globalization.CultureInfo` | 1 | InternalGetSystemDefaultUILanguage |
| `System.MarshalByRefObject` | 1 | GetComIUnknown |
| `System.Math` | 1 | SplitFractionDouble |
| `System.MissingMemberException` | 1 | FormatSignature |
| `System.Runtime.Versioning.VersioningHelper` | 1 | GetRuntimeId |
| `System.Signature` | 1 | GetSignature |
| `System.Threading.RegisteredWaitHandleSafe` | 1 | UnregisterWaitNative |
| `System.TimeSpan` | 1 | LegacyFormatMode |

### 5.3 处理原则

1. **P0 里的 LOUD 拒绝优先转成「明确的运行时语义」**：`RuntimeHelpers.PrepareDelegate` / `PrepareContractedDelegate` / `ProbeForSufficientStack` / `EnsureSufficientExecutionStack` 在 AOT 下没有实际工作要做，空实现即语义正确，比转译期直接拒绝更好。
2. **P0 里的 COM/remoting 保持 LOUD**：设计上就不支持，转译期失败远好于链接期幽灵符号。
3. **P1 按属主成批推进**：同一 `MethodMap.*.cs` 域的分支集中加，避免反复触碰同一文件。
4. **`缺（无接线）` 的 321 条里 P3 占 202 条**，多数是 CLR 内部（`System.StubHelpers.*`、`System.Reflection.MetadataImport`、`System.Signature`）或 legacy（`System.Mda`、`System.Variant`），不建议排期。

## 6. 红证（可执行复现）

以下三条都是**实跑**结果，不是代码推断。

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
`

stub-report 为空 —— 整个方法转译成功。**这就是旧结论被推翻的直接证据。**

### 6.2 LOUD 拒绝真的会拦下整个方法

把程序改成只调用 RuntimeHelpers.PrepareDelegate(null)，转译输出：

```
warning: method fold failed -> skipped (Probe::Main: System.Runtime.CompilerServices.RuntimeHelpers::PrepareDelegate:
  LOUD whitelist (no machine/JIT state): ... )
warning: 1 method(s) were not transpiled (see --stub-report)
`

stub-report 内容为 Probe::Main/0#0x06000001。也就是说 §3 里的「缺（LOUD 拒绝）」不是静默的：
转译期就失败并留下可核对的报告，而不是生成一个链接期才炸的幽灵符号。

### 6.3 「无接线」确实是幽灵调用（这是真问题）

Marshal.GetUniqueObjectForIUnknown 在 §3 里判为「缺（无接线）」。同样方式转译调用它的程序，
**没有** LOUD 警告、**没有** stub-report 条目，生成的是：

```cpp
t0 = reinterpret_cast<Il2CppObject*>(
        System_Runtime_InteropServices_Marshal_GetUniqueObjectForIUnknown_6d094451fafe8f02(System_IntPtr_sf_Zero));
`

这个 System_..._<hash> 符号在生成的 C++ 里不存在，只有到 C++ 链接期才会失败。
**所以 §3 的 321 条「无接线」是本审计里优先级最高的一类：它们的失败发生在最晚、最难定位的阶段。**

## 7. 复现方式

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/run-audit.ps1
```

脚本按顺序执行 9 个阶段（反射通道 → 元数据通道 → join key 交叉校验 → 调用图 → 真实 BclResolve 探针 → 运行时符号索引 → dispatcher 分支解析 → 证据行定位 → 分类），产物写入 `-OutDir`（默认 `%TEMP%\opencode\icall-audit`），最终数据为 `classified.json`。

各阶段工具：

| 工具 | 作用 |
|---|---|
| `tools/audit/enumerate-icall-reflection.ps1` | 通道 1：运行时反射枚举 icall（PS 5.1） |
| `tools/audit/MetadataIcallCounter/` | 通道 2：ECMA-335 元数据位枚举 |
| `tools/audit/IcallCallGraph/` | IL 调用图 + 从 public 面的可达距离 |
| `tools/audit/IcallWiringProbe/` | 逐条调用真实 `MethodRefResolver.BclResolve` |
| `tools/audit/parse_runtime_syms.py` | `src/runtime` 的声明/定义/LOUD 桩索引 |
| `tools/audit/parse_wiring_surface.py` | `MethodMap.*.cs` 的 dispatcher 分支解析 |
| `tools/audit/locate_wiring_sites.py` | 每条判定对应的源码行 |
| `tools/audit/classify_icalls.py` | 最终判定 + P0..P3 |
| `tools/audit/shared/SignatureProvider.cs` | 两通道共用的签名渲染器（保证 join key 不漂移） |
