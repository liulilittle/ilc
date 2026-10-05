# icall 清单审计：.NET Core（netcore + corert）内部调用全景 vs ILC 接线

> 审计类型：只读源码扫描（无任何修改、无 commit）
> 日期：2026-08-06 · 分支：m1
> 审计代理：icall-core 扫描代理（net48 清单由另一代理并行产出，见第 7 节域级预分类）
> 关联文档：docs/audit/icall-gap-list.md（2026-08-03，ILC runtime<->转译器内部缺口，与本清单互补）
> 原则依据：memory.md 硬性原则 #7 —— Framework 4.8 + .NET Core **所有**内部调用必须全量接线。

---

## 0. 执行摘要（5 条关键结论）

1. **reference/netcore 是自洽裁剪子集，不是完整 upstream**：src/libraries 只有 4 个项目（System.Private.CoreLib / System.Console / System.Private.Uri / System.Text.RegularExpressions）；CoreLib 内 GC.cs 是 28 行 stub（0 icall）、RuntimeType.cs（1028 行）0 处 InternalCall（全部委托 RuntimeTypeHandle）、FastAllocateString 被调用 67 次但**全树无声明**（裁剪残留）。=> 任务预期"ecalllist.h 是巨大文件"不成立：本仓库 ecalllist.h 仅 427 行（29 组 FCall）；真正的第二张权威表 **qcallentrypoints.cpp 有 403 条 QCall 条目**（556 行）——QCall 才是现代 Core 内部调用的大头。

2. **Core 权威 icall 契约 = 两张表 + C# 声明**：
   - 表 1 src/coreclr/vm/ecalllist.h：FCall 表，29 个 FCClassElement 组、约 200 个函数（String 分配、Math/MathF、GC、GCHandle、Interlocked、RuntimeTypeHandle/RuntimeMethodHandle/RuntimeFieldHandle、MetadataImport、StubHelpers 等）。
   - 表 2 src/coreclr/vm/qcallentrypoints.cpp：s_QCall[] 共 **403 条 DllImportEntry**，约 45 个域（RuntimeTypeHandle 34、AssemblyNative 39、ThreadNative 25、TypeBuilder 21、GCInterface 19、MarshalNative 30+、ReflectionInvocation 10...）。
   - C# 侧（CoreLib）：**26 处** [MethodImpl(MethodImplOptions.InternalCall)]；QCall 路径统一为 [LibraryImport(RuntimeHelpers.QCall, EntryPoint=...)]；另有 48 处 [DllImport]（P/Invoke，已区分，不在 icall 范围）。

3. **reference/corert（CppWriter 侧）**：CppWriter.cs:642 对 [RuntimeImport] 方法直接 CompileExternMethod(GetRuntimeImportName())（发射为对原生函数的 extern 调用，链接到 Redhawk 运行时）。契约集中在 src/System.Private.CoreLib/src/System/Runtime/RuntimeImports.cs：**174 个唯一 RuntimeImport 条目**（libm 数学约 40 + Rh*/Rhp* Redhawk 运行时约 134）。另有 Runtime.Base/InternalCalls.cs（约 50 条）与 Test.CoreLib/RuntimeImports.cs（约 13 条）。

4. **ILC 现状**：src/runtime 已实现 **约 330 个 icall_* 函数**（net48 形状：console/string/array/datetime/decimal/math/convert/io/path/env/gc/crypto/random/thread/timespan/uri/version/weakref/type...）。对照 Core 两张表：**FCall 29 组中 0 组完整覆盖、约 8 组部分覆盖、约 14 组完全未接线；QCall 403 条与 corert 174 条全部未接线**（ILC 以自有 net48 形状替代品实现近似语义，如 icall_method_invoke 约等于 RuntimeMethodHandle_InvokeMethod、icall_object_get_type 约等于 TypeHandle_GetCorElementType 系）。

5. **高价值 Core 独有域（ILC 需要优先补）**：String 分配路径（FastAllocateString + 8 个 ctor）、MathF（24 条 float 数学）、Math 超函数（Acosh/Asinh/Atanh/Cbrt/FusedMultiplyAdd/ModF/SinCos/Log2）与 DivModInt（8 条 DivRem）、DependentHandle（7，ConditionalWeakTable 依赖）、GC 通知/统计（13 条）、GCSettings（5）、JitInfo（3）、GCFrameRegistration（2）、MethodTable（5）、ObjectHeader（1）、GCHandle 全量（5）、Environment.ExitCode（2）、Thread.CurrentThreadIsFinalizerThread。详见第 8 节逐域缺口表。

---

## 1. 扫描来源（绝对路径）

| 源 | 路径 | 内容 |
|---|---|---|
| CoreCLR FCall 表 | E:\il2cpp\reference\netcore\src\coreclr\vm\ecalllist.h（427 行） | 29 组 FCall（第 3 节全量） |
| CoreCLR QCall 表 | E:\il2cpp\reference\netcore\src\coreclr\vm\qcallentrypoints.cpp（556 行） | 403 条 QCall（第 4 节域级摘要） |
| CoreCLR 对照 | E:\il2cpp\reference\coreclr\src\coreclr\vm\ecalllist.h | 与 netcore 版仅差 1 行（同一张裁剪表） |
| Core C# 声明 | E:\il2cpp\reference\netcore\src\libraries\System.Private.CoreLib\src\System\ | 26 处 InternalCall + 48 处 DllImport（第 5 节） |
| corert 编译器侧 | E:\il2cpp\reference\corert\src\ILCompiler.CppCodeGen\src\CppCodeGen\CppWriter.cs | RuntimeImport -> CompileExternMethod（第 6.1 节） |
| corert 契约 | E:\il2cpp\reference\corert\src\System.Private.CoreLib\src\System\Runtime\RuntimeImports.cs | 174 条 RuntimeImport（第 6.2 节） |
| corert 其余 | Runtime.Base\src\System\Runtime\InternalCalls.cs、Test.CoreLib\...\RuntimeImports.cs、src\Native\Runtime\*.cpp | Rhp 原生实现（第 6.3 节） |
| ILC 能力面 | E:\il2cpp\src\runtime（il2cpp_*.h/cpp） | 约 330 个 icall_*（第 8 节对照基准） |
| ILC 接线面 | E:\il2cpp\src\il2cppc\Emit\MethodMap.*.cs（80+ 文件） | TryMap* 域分发（第 8 节对照基准） |

---

## 2. 机制说明（Core 三种内部调用形态）

1. **FCall（快速调用）**：C# [MethodImpl(MethodImplOptions.InternalCall)] -> JIT 按类/名查 ecalllist.h 表 -> 直接调 C++ 函数（如 String ctor、GC._CollectionCount）。
2. **QCall（慢速调用，现代主力）**：C# [LibraryImport(RuntimeHelpers.QCall, EntryPoint="X")] -> 运行时查 qcallentrypoints.cpp 的 s_QCall[]（QCallResolveDllImport）-> 调 C++ 函数（如 RuntimeTypeHandle_MakeArray、Environment_Exit）。**注意：同文件里 #if MONO 分支才是 InternalCall 属性，CoreCLR 分支是 LibraryImport**（见 5.2）。
3. **corert RuntimeImport**：[RuntimeImport(RuntimeLibrary, "RhpXxx")] -> AOT 编译器发射对原生符号的 extern 调用（CppWriter.cs:642-646）。

---

## 3. 表 1：FCall 表 ecalllist.h（29 组，全量）

文件：reference/netcore/src/coreclr/vm/ecalllist.h（427 行，FCClassElement 保持按 name:namespace 排序）。合计 29 组约 200 个函数：

| # | 组（gXxxFuncs） | 类（命名空间） | 数 | 函数清单 |
|---|---|---|---|---|
| 1 | gDependentHandleFuncs | **DependentHandle**（System.Runtime） | 7 | InternalAlloc, InternalGetTarget, InternalGetDependent, InternalGetTargetAndDependent, InternalSetTargetToNull, InternalSetDependent, InternalFree |
| 2 | gStringFuncs | **String**（System） | 9 | FCDynamic FastAllocateString；ctor x8：CtorCharArrayManaged, CtorCharArrayStartLengthManaged, CtorCharPtrManaged, CtorCharPtrStartLengthManaged, CtorSBytePtrManaged, CtorSBytePtrStartLengthManaged, CtorSBytePtrStartLengthEncodingManaged, CtorCharCountManaged, CtorReadOnlySpanOfCharManaged |
| 3 | gEnvironmentFuncs | **Environment**（System） | 2 | set_ExitCode, get_ExitCode |
| 4 | gExceptionFuncs | **Exception**（System） | 3 | IsImmutableAgileException, PrepareForForeignExceptionRaise, GetExceptionCount |
| 5 | gCOMTypeHandleFuncs | **RuntimeTypeHandle**（System） | 16 | GetFirstIntroducedMethod, GetNextIntroducedMethod, GetAssemblyIfExists, GetModuleIfExists, GetElementTypeHandle, GetArrayRank, GetToken, GetUtf8NameInternal, GetAttributes, GetNumVirtuals, GetGenericVariableIndex, IsGenericVariable, ContainsGenericVariables, IsUnmanagedFunctionPointer, CompareCanonicalHandles, InternalAllocNoChecks_FastPath |
| 6 | gMetaDataImport | **MetadataImport**（System.Reflection） | 22 | GetMetadataImport, GetDefaultValue, GetName, GetUserString, GetScopeProps, GetClassLayout, GetSignatureFromToken, GetNamespace, GetEventProps, GetFieldDefProps, GetPropertyProps, GetParentToken, GetParamDefProps, GetGenericParamProps, GetMemberRefProps, GetCustomAttributeProps, GetFieldOffset, GetSigOfFieldDef, GetSigOfMethodDef, GetFieldMarshal, GetPInvokeMap, IsValidToken |
| 7 | gSignatureNative | **Signature**（System） | 3 | GetParameterOffsetInternal, GetTypeParameterOffsetInternal, GetCallingConventionFromFunctionPointerAtOffsetInternal |
| 8 | gRuntimeMethodHandle | **RuntimeMethodHandle**（System） | 18 | GetImplAttributes, IsCollectible, GetAttributes, GetMethodTable, GetSlot, GetMethodDef, GetUtf8NameInternal, HasMethodInstantiation, IsGenericMethodDefinition, GetGenericParameterCount, IsTypicalMethodDefinition, GetStubIfNeededInternal, GetMethodFromCanonical, IsDynamicMethod, IsConstructor, IsAsyncMethod, GetResolver, GetLoaderAllocatorInternal |
| 9 | gAsyncHelpers | AsyncHelpers（System.Runtime.CompilerServices，仅 FEATURE_INTERPRETER） | 1 | ResumeInterpreterContinuation |
| 10 | gCOMFieldHandleNewFuncs | **RuntimeFieldHandle**（System） | 10 | GetUtf8NameInternal, GetAttributes, GetApproxDeclaringMethodTable, GetToken, GetStaticFieldForGenericType, AcquiresContextFromThis, GetLoaderAllocatorInternal, IsFastPathSupported, GetInstanceFieldOffset, GetStaticFieldAddress |
| 11 | gRuntimeAssemblyFuncs | **RuntimeAssembly**（System.Reflection） | 4 | GetIsDynamic, GetIsCollectible, GetManifestModule, GetTokenInternal |
| 12 | gAssemblyLoadContextFuncs | **AssemblyLoadContext**（System.Runtime.Loader） | 1 | IsTracingEnabled |
| 13 | gDelegateFuncs | **Delegate**（System） | 2 | GetMulticastInvoke, GetInvokeMethod |
| 14 | gMathFuncs | **Math**（System） | 25+8 | Acos, Acosh, Asin, Asinh, Atan, Atanh, Atan2, Cbrt, Ceiling, Cos, Cosh, Exp, Floor, FusedMultiplyAdd, Log, Log2, Log10, ModF, Pow, Sin, SinCos, Sinh, Sqrt, Tan, Tanh；TARGET_32BIT 另加 DivModInt x8：DivInt32Internal, DivUInt32Internal, DivInt64Internal, DivUInt64Internal, ModInt32Internal, ModUInt32Internal, ModInt64Internal, ModUInt64Internal |
| 15 | gMathFFuncs | **MathF**（System） | 24 | 单精度版同构：Acos, Acosh, Asin, Asinh, Atan, Atanh, Atan2, Cbrt, Ceiling, Cos, Cosh, Exp, Floor, FusedMultiplyAdd, Log, Log2, Log10, ModF, Pow, Sin, SinCos, Sinh, Sqrt, Tan, Tanh |
| 16 | gThreadFuncs | **Thread**（System.Threading） | 4+1 | InternalFinalize, CatchAtSafePoint, CurrentThreadIsFinalizerThread, get_OptimalMaxSpinWaitsPerSpinIteration；TARGET_WASM 另加 GetThreadStaticsBaseNative |
| 17 | gObjectHeaderFuncs | **ObjectHeader**（System.Threading） | 1 | GetLockHandleIfExists |
| 18 | gArrayFuncs | **Array**（System） | 1 | GetCorElementTypeOfElementType |
| 19 | gBufferFuncs | **Buffer**（System） | 1 | BulkMoveWithWriteBarrier |
| 20 | gGCFrameRegistration | **GCFrameRegistration**（System.Runtime） | 2 | RegisterForGCReporting, UnregisterForGCReporting |
| 21 | gGCInterfaceFuncs | **GC**（System） | 15 | _RegisterForFullGCNotification, _CancelFullGCNotification, _CollectionCount, GetMemoryInfo, _GetTotalPauseDuration, GetMemoryLoad, GetSegmentSize, GetLastGCPercentTimeInGC, GetGenerationSize, GetGenerationInternal, GetMaxGeneration, SuppressFinalizeInternal, GetAllocatedBytesForCurrentThread, GetTotalAllocatedBytesApproximate |
| 22 | gGCSettingsFuncs | **GCSettings**（System.Runtime） | 5 | get_IsServerGC, GetGCLatencyMode, GetLOHCompactionMode, SetGCLatencyMode, SetLOHCompactionMode |
| 23 | gInteropMarshalFuncs | **Marshal**（System.Runtime.InteropServices） | 4(+1) | GetLastPInvokeError, SetLastPInvokeError, GetExceptionCode, GetExceptionPointers；FEATURE_COMINTEROP 加 AreComObjectsAvailableForCleanup |
| 24 | gInterlockedFuncs | **Interlocked**（System.Threading） | 9 | Exchange32, Exchange64, ExchangeObject, CompareExchange32, CompareExchange32Pointer, CompareExchange64, CompareExchangeObject, ExchangeAdd32, ExchangeAdd64 |
| 25 | gJitInfoFuncs | **JitInfo**（System.Runtime） | 3 | GetCompiledILBytes, GetCompiledMethodCount, GetCompilationTimeInTicks |
| 26 | gRuntimeHelpers | **RuntimeHelpers**（System.Runtime.CompilerServices） | 5 | TryGetHashCode, ContentEquals, TryEnsureSufficientStackExecution, GetTailCallArgBuffer, GetTailCallInfo |
| 27 | gMethodTableFuncs | **MethodTable**（System.Runtime.CompilerServices） | 5 | GetNumInstanceFieldBytes, GetPrimitiveCorElementType, GetMethodTableMatchingParentClass, InstantiationArg0, GetLoaderAllocatorHandle |
| 28 | gStubHelperFuncs | **StubHelpers**（System.StubHelpers） | 6(+2) | GetDelegateTarget, SetLastError, ClearLastError, CalcVaListSize, LogPinnedArgument；FEATURE_COMINTEROP 加 GetComInterfaceFromMethodDesc, GetCOMIPFromRCW |
| 29 | gGCHandleFuncs | **GCHandle**（System.Runtime.InteropServices） | 5 | _InternalAlloc, _InternalFree, InternalGet, InternalSet, InternalCompareExchange（+FEATURE_JAVAMARSHAL InternalTryGetBridgeWait） |

> 注：本表**不含** upstream 完整表的 RuntimeType/Type/Monitor/StringBuilder/TypedReference/DynamicMethod/AppDomain/Remoting 等大组——本仓库是裁剪子集（参考 coreclr-src-fetch.md / netcore-src-fetch.md 的取源记录）。

---

## 4. 表 2：QCall 表 qcallentrypoints.cpp（403 条，域级摘要）

文件：reference/netcore/src/coreclr/vm/qcallentrypoints.cpp（556 行，static const Entry s_QCall[] + QCallResolveDllImport，实测 403 条 DllImportEntry）。按域（原生前缀）分组：

| 域（原生前缀） | 条数 | 代表条目 |
|---|---|---|
| ArgIterator | 5 | Init, Init2, GetNextArgType, GetNextArg, GetNextArg2 |
| CustomAttribute | 3 | ParseAttributeUsageAttribute, CreateCustomAttributeInstance, CreatePropertyOrFieldData |
| Enum | 1 | GetValuesAndNames |
| DebugDebugger | 6 | Break, Launch, Log, CustomNotification, IsLoggingHelper, IsManagedDebuggerAttached |
| Delegate | 8 | BindToMethodName/Info, InitializeVirtualCallStub, GetMulticastInvokeSlow, AdjustTarget, Construct, CreateMethodInfo, GetMethodDesc |
| Environment | 3 | Exit, FailFast, GetProcessorCount |
| ExceptionNative | 8 | GetFrozenStackTrace, GetMessageFromNativeResources, GetMethodFromStackTrace, Throw{AmbiguousResolution,EntryPointNotFound,MethodAccess,FieldAccess,ClassAccess}Exception |
| QCall（GCHandle-for-type） | 2 | GetGCHandleForTypeHandle, FreeGCHandleForTypeHandle |
| MethodTable | 2 | AreTypesEquivalent, CanCompareBitsOrUseFastGetHashCode |
| TypeHandle | 2 | CanCastTo_NoCacheLookup, GetCorElementType |
| ValueType | 1 | GetHashCodeStrategy |
| Stream | 1 | HasOverriddenSlow |
| **RuntimeTypeHandle** | 34 | MakePointer/ByRef/SZArray/Array/FunctionPointer, GetConstraints, GetArgumentTypesFromFunctionPointer, GetAssemblySlow, GetModuleSlow, GetNumVirtualsAndStaticVirtuals, GetMethodAt, GetFields, VerifyInterfaceIsImplemented, GetInterfaceMethodImplementation, GetDeclaringMethodForGenericParameter, GetDeclaringTypeHandleForGenericVariable, GetDeclaringTypeHandle, IsVisible, ConstructName, GetInterfaces, SatisfiesConstraints, GetInstantiation, Instantiate, GetGenericTypeDefinition, GetActivationInfo, AllocateComObject, GetRuntimeTypeFromHandleSlow, IsEquivalentTo, CreateInstanceForAnotherGenericParameter, InternalAlloc(NoChecks), AllocateTypeAssociatedMemory(Aligned), RegisterCollectibleTypeDependency |
| MethodBase | 1 | GetCurrentMethod |
| RuntimeMethodHandle | 11 | InvokeMethod, ConstructInstantiation, GetFunctionPointer, GetMethodInstantiation, GetTypicalMethodDefinition, StripMethodInstantiation, IsCAVisibleFromDecoratedType, Destroy, GetStubIfNeededSlow, GetNativeCode, GetMethodBody |
| RuntimeModule | 3 | GetScopeName, GetFullyQualifiedName, GetTypes |
| RuntimeFieldHandle | 7 | GetValue, SetValue, GetValueDirect, SetValueDirect, GetEnCFieldAddr, GetRVAFieldInfo, GetFieldDataReference |
| UnsafeAccessors | 1 | ResolveGenericParamToTypeHandle |
| StackTrace / StackFrame / AsyncHelpers | 3 | GetStackFramesInternal, GetMethodDescFromNativeIP, AddContinuationToExInternal |
| ModuleBuilder | 9 | GetStringConstant, GetTypeRef, GetTokenFromTypeSpec, GetMemberRef(OfMethodInfo/OfFieldInfo/FromSignature), GetArrayMethodToken, SetFieldRVAContent |
| ModuleHandle | 8 | GetMDStreamVersion, GetModuleType, GetToken, ResolveType/Method/Field, GetPEKind, GetDynamicMethod |
| AssemblyHandle | 1 | GetManifestModuleSlow |
| Signature | 3 | Init, AreEqual, GetCustomModifiersAtOffset |
| TypeBuilder | 21 | DefineGenericParam/Type, SetParentType, AddInterfaceImpl, DefineMethod/MethodSpec, SetMethodIL, TermCreateClass, DefineField/Property/Event, DefineMethodSemantics, SetMethodImpl, DefineMethodImpl, GetTokenFromSig, SetFieldLayoutOffset, SetClassLayout, SetParamInfo, SetPInvokeData, SetConstantValue, DefineCustomAttribute |
| MdUtf8String | 1 | EqualsCaseInsensitive |
| Array | 3 | CreateInstance, Ctor, GetElementConstructorEntrypoint |
| AssemblyName | 1 | InitializeAssemblySpec |
| **AssemblyNative** | 39 | GetFullName/Location/Resource/CodeBase/Flags/HashAlgorithm/Locale/PublicKey/SimpleName/Version, InternalLoad, GetTypeCore(+IgnoreCase), GetForwardedType, GetManifestResourceInfo/Names, GetReferencedAssemblies, GetModules/Module, GetExportedTypes, GetEntryPoint, GetImageRuntimeVersion, InternalTryGetRawMetadata, ApplyUpdate, IsApplyUpdateSupported, InitializeAssemblyLoadContext, PrepareForAssemblyLoadContextRelease, LoadFromPath/Stream/InMemoryModule, GetLoadContextForAssembly, Trace*（5 个诊断）, GetLoadedAssemblies, GetAssemblyCount, GetEntryAssembly, GetExecutingAssembly |
| TypeMapLazyDictionary | 3 | FindPrecached{External,Proxy}TypeMapEntry, ProcessAttributes |
| MultiCoreJIT | 2 | InternalSetProfileRoot, InternalStartProfile |
| LoaderAllocator | 1 | Destroy |
| String | 2 | **Intern, IsInterned** |
| AppDomain / AppContext | 3 | CreateDynamicAssembly, SetFirstChanceExceptionHandler, TryGetHostPropertyValue |
| **ThreadNative** | 25 | Start, SetPriority, GetCurrentThread, Get/SetIsBackground, InformThreadNameChange, YieldThread, GetCurrentOSThreadId, Initialize, GetThreadState, Set/ClearWaitSleepJoinState, ReentrantWaitAny, Get/SetApartmentState, Abort, ResetAbort, SpinWait, CheckForPendingInterrupt, Interrupt, GetOSHandle, PollGC, DisableComObjectEagerCleanup |
| ObjectHeader | 1 | GetOrCreateLockObject |
| ClrConfig | 1 | GetConfigBoolValue |
| 内存原语 | 2 | memset, memmove |
| DependentHandle | 2 | InternalAllocWithGCTransition, InternalFreeWithGCTransition |
| **GCInterface** | 19 | GetTotalAllocatedBytesPrecise, WaitForFullGCApproach/Complete, Start/EndNoGCRegion, AllocateNewArray, GetTotalMemory, Collect, ReRegisterForFinalize, GetNextFinalizableObject, WaitForPendingFinalizers, Add/RemoveMemoryPressure, Register/UnregisterFrozenSegment, EnumerateConfigurationValues, RefreshMemoryLimit, EnableNoGCRegionCallback, GetGenerationBudget |
| GCHandle | 2 | InternalAllocWithGCTransition, InternalFreeWithGCTransition（+InternalGetBridgeWait） |
| **MarshalNative** | 12+21 | OffsetOf, Prelink, IsBuiltInComSupported, HasLayout, SizeOfHelper, Get/GetFunctionPointerForDelegateInternal, GetExceptionForHR, GetHRForException, GetHINSTANCE, GetIsInCooperativeGCModeFunctionPointer；COM：GetTypeFromCLSID, GetIUnknownForObject, GetIDispatchForObject, GetComInterfaceForObject, GetObjectForIUnknown 系, CreateAggregatedObject, ReleaseComObject 系, GetNativeVariantForObject 系, GetStart/EndComSlot, ChangeWrapperHandleStrength... |
| MngdSafeArrayMarshaler / Variant | 7 | CreateMarshaler, ConvertSpace/ContentsToNative/Managed, ClearNative, ConvertValueTypeToRecord |
| NativeLibrary | 1 | LoadByName |
| TypeLoadException | 3 | GetTypeLoadExceptionMessage, GetFileLoadExceptionMessage, FileLoadException_GetMessageForHR |
| Interlocked | 1 | MemoryBarrierProcessWide |
| ObjectNative | 2 | GetHashCodeSlow, AllocateUninitializedClone |
| MetadataImport | 2 | Enum, GetMarshalAs |
| ReflectionInvocation | 10 | RunClassConstructor, RunModuleConstructor, CompileMethod, PrepareMethod, PrepareDelegate, InvokeDispMethod, GetComObjectGuid, GetGuid, SizeOf, GetBoxInfo |
| ReflectionSerialization | 1 | GetCreateUninitializedObjectInfo |
| ComWrappers / TrackerObjectManager | 10 | GetIUnknownImpl, GetUntrackedAddRefRelease, AllocateRefCountedHandle, GetIReferenceTrackerTargetVftbl, GetTaggedImpl, RegisterIsRootedCallback, HasReferenceTrackerManager, TryRegisterReferenceTrackerManager, RegisterNativeObjectWrapperCache, IsGlobalPeggingEnabled |
| ObjCMarshal | 3 | TrySetGlobalMessageSendCallback, TryInitializeReferenceTracker, AllocateReferenceTrackingHandle |
| JavaMarshal | 4 | Initialize, CreateReferenceTrackingHandle, FinishCrossReferenceProcessing, GetContext |
| EventSource / EventPipe | 31 | IsEventSourceLoggingEnabled, LogEventSource, EventSource_GetClrConfig；NativeRuntimeEventSource_Log*（16）；EventPipeInternal_*（12） |
| TARGET_UNIX 环境 | 5 | FreeEnvironmentStringsW, GetEnvironmentStringsW, GetEnvironmentVariableW, OutputDebugStringW, SetEnvironmentVariableW |
| X86Base | 1 | X86Base_CpuId |
| StubHelpers | 10 | CreateCustomMarshaler, CreateLayoutClassMarshalStubs, ThrowInteropParamException, MarshalToManaged/UnmanagedVaList, ValidateObject/Byref, MulticastDebuggerTraceHelper, ProfilerBegin/EndTransitionCallback, GetCOMIPFromRCWSlow |
| ObjectMarshaler / InterfaceMarshaler | 6 | ConvertToNative/Managed x2, GetObjectForComCallableWrapperIUnknown, ValidateComVisibilityForIUnknown |
| ComWeakRef | 2 | ComWeakRefToObject, ObjectToComWeakRef |
| Sfi / EH / 尾调用 | 15 | SfiInit, SfiNext, CallFilterFunclet, EHEnumInitFromStackFrameIterator, EHEnumNext, AppendExceptionStackFrame, InitClassHelper, ResolveVirtualFunctionPointer, GetThreadStaticsByMethodTable/Index, GenericHandleWorker, ThrowInvalidCastException, IsInstanceOf_NoCacheLookup, VersionResilientHashCode_TypeHashCode, TailCallHelp_AllocTailCallArgBufferInternal |
| TARGET_BROWSER | 5 | SystemJS_MarkAsyncMain, ResolveMainPromise, RejectMainPromise, ScheduleTimer, ScheduleBackgroundJob |
| TARGET_WASI | 2 | WasiFinalizer_RunWorker, TryClearPending |

---

## 5. C# 侧声明（netcore CoreLib：26 处 InternalCall + 48 处 DllImport 区分）

### 5.1 [MethodImpl(MethodImplOptions.InternalCall)] 全量 26 处

| 文件（System.Private.CoreLib\src\System\） | 行 | 方法 | 对应表 |
|---|---|---|---|
| String.cs | 70/91/118/144/178/196/252/283/305 | String(char[])、String(char[],int,int)、String(char*)、String(char*,int,int)、String(sbyte*)、String(sbyte*,int,int)、String(sbyte*,int,int,Encoding)、String(char,int)、String(ReadOnlySpan<char>)（9 个 ctor） | FCall gStringFuncs（8 条 sig + FastAllocateString） |
| Math.DivModInt.cs | 184-226 | DivInt32Internal/DivUInt32Internal/DivInt64Internal/DivUInt64Internal/ModInt32Internal/ModUInt32Internal/ModInt64Internal/ModUInt64Internal（8 个；#if NATIVEAOT 下同时挂 [RuntimeImport(RuntimeLibrary,"DivInt32Internal")]——**CoreCLR/NativeAOT 双模式样板**） | FCall gMathFuncs DivModInt x8 |
| SpanHelpers.ByteMemOps.cs | 259/482 | memmove、memset（**仅 #if MONO**；CoreCLR 分支为 QCall [LibraryImport(RuntimeHelpers.QCall, EntryPoint="memmove"/"memset")]） | QCall memset/memmove |
| Threading\Thread.cs | 747 | WarnAboutBlockingWait（仅 FEATURE_WASM_MANAGED_THREADS） | （WASM） |
| Threading\Thread.cs | 789 | CurrentThreadIsFinalizerThread（#if NATIVEAOT 挂 RuntimeImport "RhpCurrentThreadIsFinalizerThread"） | FCall gThreadFuncs |
| Runtime\Intrinsics\X86\X86Base.cs | 125 | CpuId(int*,int,int)（**仅 #if MONO**；CoreCLR 为 QCall X86Base_CpuId） | QCall X86Base_CpuId |
| Threading\ThreadPool.Browser.cs | 127 | 浏览器线程池通知（TARGET_BROWSER） | QCall SystemJS_* 系 |
| Threading\TimerQueue.Browser.cs | 35 | 浏览器计时器（TARGET_BROWSER） | QCall SystemJS_* 系 |
| Runtime\GCFrameRegistration.cs | 35/38 | RegisterForGCReporting、UnregisterForGCReporting（#if CORECLR） | FCall gGCFrameRegistration |

> **注意**：FastAllocateString(int) 全树（CoreLib）被调用 67 次（String.cs/String.Manipulation.cs/Number.Formatting.cs/Exception.cs/Enum.cs/Guid.cs/BitConverter.cs/StringBuilder.cs/Latin1Encoding.cs/Random.cs/Globalization/*.cs）但**本子集无声明**——9 个 ctor 全部经 FastAllocateString 实现，ILC 接线必须以 FCall 表 gStringFuncs 的 ECall::FastAllocateString 为准（FCDynamic 条目；upstream 的 C# 声明为 internal static extern string FastAllocateString(int length)）。

### 5.2 QCall LibraryImport 模式（与 InternalCall 的区分，接线必读）
- 同一方法两套声明：**#if MONO 用 [MethodImpl(InternalCall)]；#else 用 [LibraryImport(RuntimeHelpers.QCall, EntryPoint=...)]**（memset/memmove/X86Base_CpuId 三例实测）。**ILC 若以本仓库 C# 为契约源，必须识别 QCall 形态，否则漏接 memset/memmove 这类基础内存原语。**

### 5.3 DllImport 区分（P/Invoke，非 icall）
- 全 CoreLib 48 处 [DllImport]（Interop.Kernel32/User32/NtDll/BCrypt 等平台 P/Invoke）——**不在 icall 审计范围**，ILC 若跑 Core 语义需走既有 pinvoke 接线（icall_pinvoke_register_callback）。
- 另有非 QCall 的 [LibraryImport]（Interop.* 平台调用）若干，同属 P/Invoke。

### 5.4 本子集裁剪特征（影响审计结论的 3 个事实）
1. GC.cs 仅 28 行 stub（只有 2 个 TimeSpan 重载）——C# 侧无 GC icall 声明，但 FCall 表仍注册 15 条 GCInterface + 5 条 GCSettings，QCall 表另有 19 条 GCInterface。=> **以表为准，C# stub 不构成"无此 icall"的证据**。
2. RuntimeType.cs（1028 行）0 处 InternalCall——反射操作全部折叠到 RuntimeTypeHandle（FCall 16 + QCall 34）。=> RuntimeType 的 icall 面 = RuntimeTypeHandle 面。
3. RuntimeMethodHandle.InvokeMethod、RuntimeFieldHandle.GetValue 等反射调用在 QCall 表（403 条）里、不在 FCall 表——ILC 的 icall_method_invoke 只能算部分替代。

---

## 6. corert（CppWriter 侧）icall 面

### 6.1 机制
- CppWriter.cs:642-646：method.HasCustomAttribute("System.Runtime","RuntimeImportAttribute") -> CompileExternMethod(methodCodeNodeNeedingCode, ((EcmaMethod)method).GetRuntimeImportName())——发射对原生符号的直接 extern 调用（无 stub、无签名校验，链接期解析到 src/Native/Runtime 的 Rhp 函数）。CppWriter.cs:648-652：IsRawPInvoke 同路径（P/Invoke 名直出）。
- CppCodegenNodeFactory.cs:31：IsInternalCall 方法走独立 CreateMethodEntrypointNode 分支（ArrayAddress 等特例）。
- 编译器侧映射：ILCompiler.RyuJit 的 RyuJitNodeFactory.cs:32 对 RuntimeImport 建 RuntimeImportMethodNode。

### 6.2 RuntimeImports.cs（System.Private.CoreLib）——174 个唯一 RuntimeImport 条目（实测枚举）
文件：corert\src\System.Private.CoreLib\src\System\Runtime\RuntimeImports.cs（public static class RuntimeImports）。

- **libm 数学（约 54，小写名）**：acos, acosf, acosh, acoshf, asin, asinf, asinh, asinhf, atan, atan2, atan2f, atanf, atanh, atanhf, cbrt, cbrtf, ceil, ceilf, cos, cosf, cosh, coshf, exp, expf, fabs, floor, floorf, fma, fmaf, fmod, fmodf, ilogb, ilogbf, log, log10, log10f, log2, log2f, logf, modf, modff, pow, powf, scalbn, scalbnf, sin, sinf, sinh, sinhf, sqrt, sqrtf, tan, tanf, tanh, tanhf
- **Rh*/Rhp* Redhawk 运行时（约 120，按功能分）**：
  - 分配：RhNewObject, RhNewArray, RhNewString, RhBox, RhBoxAny, RhUnbox, RhAllocateNewArray（RhpNewFast/Finalizable/Array/Align8/Misalign 在 Runtime.Base）
  - 句柄：RhpHandleAlloc(+Dependent/Variable), RhHandleGet/Set/Free/GetDependent/SetDependentSecondary/CompareExchangeVariableType, RhpCheckedLockCmpXchg, RhpCheckedXchg
  - 内存：RhBulkMoveWithWriteBarrier, RhpArrayCopy, RhpArrayClear, RhCompareObjectContentsAndPadding, RhMemberwiseClone, memmove
  - 类型转换/调度：RhTypeCast_AreTypesAssignable/Equivalent/CheckArrayStore/IsInstanceOf(Class/Interface), RhResolveDispatch(OnType), RhpResolveInterfaceMethod, RhNewInterfaceDispatchCell, RhGetRuntimeHelperForType, RhGetTargetOfUnboxingAndInstantiatingStub, RhGetUniversalTransitionThunk, RhGetCodeTarget, RhGetCommonStubAddress
  - GC：RhCollect, RhGetGeneration, RhGetMaxGcGeneration, RhGetGcCollectionCount, RhGetGCNow, RhGetLastGCStartTime, RhGetLastGCDuration, RhGetCurrentObjSize, RhGetGcTotalMemory, RhGetGCSegmentSize, RhGetAllocatedBytesForCurrentThread, RhGetTotalAllocatedBytes, RhGetMemoryInfo, RhSuppressFinalize, RhReRegisterForFinalize, RhWaitForPendingFinalizers, RhRegisterForFullGCNotification, RhCancelFullGCNotification, RhWaitForFullGCApproach/Complete, RhStartNoGCRegion, RhEndNoGCRegion, RhGetGcLatencyMode, RhSetGcLatencyMode, RhGetLohCompactionMode, RhSetLohCompactionMode, RhIsServerGc, RhpRegisterFrozenSegment, RhpUnregisterFrozenSegment, RhIsPromoted, RhRegisterGcCallout, RhUnregisterGcCallout（GC.cs 31 处调用面，含 RhGetMemoryInfo/RhAllocateNewArray/RhGetTotalAllocatedBytesPrecise 等）
  - 线程/进程：RhCurrentNativeThreadId, RhCurrentOSThreadId, RhGetProcessCpuCount, RhSpinWait, RhpLockCmpXchg32/64, RhpMemoryBarrier, RhpInitiateThreadAbort, RhpCancelThreadAbort, RhpCurrentThreadIsFinalizerThread, RhSetThreadExitCallback
  - 模块/OS：RhpCreateTypeManager, RhpRegisterOsModule, RhpGetModuleSection, RhGetOSModuleForMrt, RhGetOSModuleFromEEType, RhGetOSModuleFromPointer, RhGetModuleFromEEType, RhGetModuleFileName, RhGetLoadedOSModules, RhFindBlob, RhConvertPublicKeyToPublicKeyToken
  - 诊断/异常：RhpFallbackFailFast, RhpEtwExceptionThrown, RhpETWShouldWalkCom, RhpETWLogLiveCom, RhpSendCustomEventToDebugger, RhSetErrorInfoBuffer, RhGetExceptionsForCurrentThread, RhGetCurrentThreadStackTrace, RhGetCurrentThreadStackBounds（RhpGetThreadAbortException 在 Runtime.Base）
  - 反射/泛型/EE：RhGetThreadLocalStorageForDynamicType, RhGetThreadStaticStorageForModule, RhSetThreadStaticStorageForModule, RhpSfiInit/SfiNext, RhpEHEnum*, RhpCallCatch/Finally/FilterFunclet, RhpValidateExInfoStack, RhpCopyContextFromExInfo, RhpGetClasslibFunction*, RhpGetEETypeRareFlags, RhpGetNullableEEType(+ValueOffset), RhpGetSealedVirtualSlot, RhpGetDispatchCellInfo, RhpSearchDispatchCellCache, RhpUpdateDispatchCellCache, RhpGetNextFinalizableObject, RhpGetRequestedExceptionEvents
  - Thunk/互操作：RhCreateThunksHeap, RhAllocateThunk, RhGetThunkSize/Data, RhSetThunkData, RhGetCurrentThunkContext, RhGetNumThunkBlocksPerMapping, RhGetNumThunksPerBlock, RhGetThunkDataBlockAddress, RhGetThunkStubsBlockAddress, RhGetThunkBlockSize, RhCallDescrWorker

### 6.3 corert 其余 icall 面
- Runtime.Base\src\System\Runtime\InternalCalls.cs：约 50 条 [RuntimeImport(Redhawk.BaseName, "Rhp...")]（RhpNewFast/Finalizable/Array/Align8/Misalign、RhpCopyObjectContents、RhpAssignRef、RhpInitMultibyte、RhpGetThreadAbortException、RhpGetNumThunk*、RhpGetThunk*、RhpInitializeGcStress、RhpHandleAlloc*、RhpGetEETypeRareFlags、RhpGetNullableEEType*、RhpCallCatchFunclet/Finally/Filter、RhpValidateExInfoStack、RhpCopyContextFromExInfo、RhpSetThreadDoNotTriggerGC、RhpGetRequestedExceptionEvents、RhpGetSealedVirtualSlot、RhpGetDispatchCellInfo、RhpSearchDispatchCellCache、RhpUpdateDispatchCellCache、RhpGetClasslibFunctionFromCodeAddress/EEType、RhpGetNextFinalizableObject、memmove）。
- Test.CoreLib\src\System\Runtime\RuntimeImports.cs：13 条（RhpHandleAlloc、RhpRegisterFrozenSegment、RhpGetModuleSection、RhpCreateTypeManager、RhpRegisterOsModule、RhNewObject、RhNewArray、RhpFallbackFailFast、RhpLockCmpXchg32、RhpMemoryBarrier、RhBulkMoveWithWriteBarrier）。
- CoreLib 各文件 icall 密度（InternalCall|RuntimeImport 命中数）：RuntimeImports.cs 351（含类名噪声）、RuntimeAugments.cs 65、GC.cs 31、Math.CoreRT.cs 29、MathF.CoreRT.cs 27、Array.CoreRT.cs 26、InteropExtensions.cs 19、MathHelpers.cs 16、Interlocked.cs 9、String.cs 9、InvokeUtils.cs 6、DependentHandle.cs 6、ThreadStatics.cs 5、GCSettings.CoreRT.cs 5、RuntimeExceptionHelpers.cs 5、UnsafeGCHandle.cs 4、GCHandle.CoreRT.cs 4、Environment.CoreRT.cs 4、RuntimeHelpers.CoreRT.cs 4、Delegate.cs 4、WaitSubsystem.HandleManager.Unix.cs 4、WeakReference.CoreRT.cs 4、EETypePtr.cs 4、Attributes.cs 3。
- **NATIVEAOT 双模式样板（Core 同源，corert 与 Core 可互相引用）**：Math.DivModInt.cs、Thread.cs:789 的 #if NATIVEAOT [RuntimeImport] 分支——icall 名（DivInt32Internal / RhpCurrentThreadIsFinalizerThread）与 FCall 表严格同名。

---

## 7. net48 对比（域级预分类；net48 方法级清单由另一代理产出，此处待其复核）

> net48 源码树不在本代理可访问范围（reference/ 下仅 coreclr/corert/netcore）。以下按 .NET Framework 4.8 mscorlib 已知 icall 面做**域级**预分类。

### 7.1 共有域（net48 也有同语义 icall，实现可对齐）
String（ctor/Intern）、Math（基础 25 条中大部分）、Environment（Exit 系）、GC（Collect/GetTotalMemory/SuppressFinalize 系）、Thread、Interlocked、Array、Buffer、Delegate、Marshal、GCHandle、Exception、RuntimeHelpers（部分）、RuntimeTypeHandle/RuntimeMethodHandle/RuntimeFieldHandle（net48 各有大量 icall）、Type/RuntimeType、MetadataImport、Signature、RuntimeAssembly、StubHelpers、AppDomain、ReflectionInvocation（MethodBase.Invoke 在 net48 走 icall）。

### 7.2 Core 独有域（net48 没有的类/机制——ILC 缺口重点）
| 域 | 说明 |
|---|---|
| DependentHandle | net48 无此类型（ConditionalWeakTable 在 net48 走内部对象，Core 改为显式 handle） |
| GCSettings（System.Runtime） | net48 无此类（GC.LatencyMode 在 net48 是属性+旧 icall，Core 独立成类） |
| JitInfo、MethodTable、GCFrameRegistration、ObjectHeader | 全新内部类型，net48 无 |
| AssemblyLoadContext | net48 无（AppDomain 模型替代） |
| MathF 全组（24） | net48 无 MathF 类 |
| Math.DivModInt x8 | net48 无（net48 的 Math.DivRem 走托管除法） |
| QCall 机制整体（403 条） | net48 对应物是旧式 mscorlib icall 表，QCall 条目名/分域是 Core 独有形态 |
| SpanHelpers memmove/memset、X86Base_CpuId | Core 独有（MONO/QCALL 双路径） |
| GCInterface 统计面（GetMemoryInfo/_CollectionCount/GetAllocatedBytesForCurrentThread...） | net48 无等价 public API 面 |
| Thread.CurrentThreadIsFinalizerThread | Core 独有 API（net48 有内部对应物但走不同 icall） |
| UnsafeAccessors、TypeMapLazyDictionary、VersionResilientHashCode | 内部实现细节，net48 无 |

### 7.3 共有但形态不同（接线时注意）
- RuntimeTypeHandle：net48 有 GetTypeFromHandle/GetToken 等 icall；Core 拆为 FCall 16 + QCall 34，且 RuntimeType 侧全部折叠到 handle（见 5.4）。
- RuntimeMethodHandle.InvokeMethod：net48 为 mscorlib icall；Core 为 QCall RuntimeMethodHandle_InvokeMethod。ILC 的 icall_method_invoke 需对照两者签名（net48 语义已对齐，见 memory.md 原则 #7）。

---

## 8. ILC 缺口清单（逐域：Core FCall 表 x ILC 现状）

ILC 能力面 = src/runtime 全部 icall_*（约 330 个，已实测枚举）+ il2cpp_interlocked_* 等 C++ 直调。接线面 = src/il2cppc/Emit/MethodMap.*.cs（80+ TryMap 域）。判定：**域内任一 FCall 条目无对应 icall/接线 => 缺口**。

### 8.1 部分接线（ILC 有近似对应，但缺条目）——约 8 组
| Core 域 | ILC 现状（已接） | 缺口条目 |
|---|---|---|
| Math（25 double） | icall_math_{abs,acos,asin,atan,atan2,ceiling,cos,cosh,exp,floor,ieee_remainder,log,log10,pow,round,round_digits,sin,sinh,sqrt,tan,tanh,truncate,max,min,sign,clamp}（26） | **缺 Acosh/Asinh/Atanh/Cbrt/FusedMultiplyAdd/ModF/SinCos/Log2（8 条）** |
| Math DivModInt x8 | 无 | **全部 8 条缺**（DivRem 高频；corert 有同名 C 参考） |
| GC（15） | icall_gc_{collect,get_total_memory,get_generation,keep_alive,suppress_finalize,re_register_for_finalize,register_finalizer,finalize_trigger,get_allocated_bytes,weak_ref_add/remove}（10） | **缺 _CollectionCount/GetMemoryInfo/_GetTotalPauseDuration/GetMemoryLoad/GetSegmentSize/GetLastGCPercentTimeInGC/GetGenerationSize/GetGenerationInternal/GetMaxGeneration/_RegisterForFullGCNotification/_CancelFullGCNotification/GetAllocatedBytesForCurrentThread/GetTotalAllocatedBytesApproximate（13 条）** |
| Environment（2） | icall_environment_*（10 条，无 ExitCode） | **缺 get_ExitCode/set_ExitCode（2 条）** |
| Thread（4） | icall_thread_*（sleep/yield/priority/current_id/critical_region）+ icall_threadobj_* | **缺 InternalFinalize/CatchAtSafePoint/CurrentThreadIsFinalizerThread/get_OptimalMaxSpinWaitsPerSpinIteration（4 条）** |
| Delegate（2） | icall_delegate_{combine,equals,get_fn_ptr,get_invocation_list,get_target,remove,to_callback}（7） | GetMulticastInvoke/GetInvokeMethod 无同名（功能有替代，可接受） |
| Interlocked（9） | il2cpp_interlocked_{increment,decrement,add,add64,exchange,exchange64,compare_exchange,compare_exchange64}（8） | ExchangeObject/CompareExchangeObject/CompareExchange32Pointer 被 MethodMap.Interlocked.cs 显式 LOUD 拒绝（void** 地址 cast 缺）——**3 条** |
| Marshal（4） | icall_marshal_*（10 条：alloc/free/sizeof/字段读写/拷贝） | **缺 GetLastPInvokeError/SetLastPInvokeError/GetExceptionCode/GetExceptionPointers（4 条）** |
| RuntimeHelpers（5） | icall_object_get_hash_code（约等于 TryGetHashCode） | **缺 ContentEquals/TryEnsureSufficientStackExecution/GetTailCallArgBuffer/GetTailCallInfo（4 条）** |
| Array（1）/GCHandle（5） | icall_array_*（11 条）/icall_gc_weak_ref_add/remove | 缺 GetCorElementTypeOfElementType（1 条）；GCHandle _InternalAlloc/_InternalFree/InternalGet/InternalSet/InternalCompareExchange 无同名（weak_ref 仅部分替代）——**5 条** |

### 8.2 完全未接线（0 对应）——14+ 组，Core 独有高危
| Core 域（FCall 组） | 条数 | 高价值理由 / 可抄参考 |
|---|---|---|
| **String 分配（gStringFuncs）** | 9 | **FastAllocateString + 8 ctor——任何 string 代码的分配根**；C# 侧 26 处 InternalCall 中 9 处在此；corert 参考 RhNewString |
| **MathF 全组（gMathFFuncs）** | 24 | float 数学；ILC 只有 double 版；corert 有 27 个 float RuntimeImport 参考（acosf...tanhf） |
| **DependentHandle（gDependentHandleFuncs）** | 7 | ConditionalWeakTable 依赖；corert DependentHandle.cs 6 处参考 |
| **GCSettings（gGCSettingsFuncs）** | 5 | ServerGC/LatencyMode/LOHCompaction；corert GCSettings.CoreRT.cs 5 处参考 |
| **JitInfo（gJitInfoFuncs）** | 3 | GetCompiledILBytes/Count/Ticks；纯诊断，可 stub |
| **GCFrameRegistration（gGCFrameRegistration）** | 2 | Register/UnregisterForGCReporting；C# 侧已声明（GCFrameRegistration.cs:35/38） |
| **MethodTable（gMethodTableFuncs）** | 5 | GetNumInstanceFieldBytes/PrimitiveCorElementType 等；与反射/类型布局相关 |
| **ObjectHeader（gObjectHeaderFuncs）** | 1 | GetLockHandleIfExists（锁对象头）；ILC 锁走 icall_thread_*，需评估 |
| **RuntimeTypeHandle（gCOMTypeHandleFuncs）** | 16 | GetElementTypeHandle/GetArrayRank/GetToken/GetAttributes/IsGenericVariable...；RuntimeType 反射全折叠到此；QCall 侧还有 34 条（MakeArray/GetFields/GetInterfaces...） |
| **RuntimeMethodHandle（gRuntimeMethodHandle）** | 18 | GetImplAttributes/GetSlot/GetMethodDef/HasMethodInstantiation...；QCall 侧 11 条（InvokeMethod/GetFunctionPointer/GetMethodBody...） |
| **RuntimeFieldHandle（gCOMFieldHandleNewFuncs）** | 10 | GetStaticFieldAddress/GetInstanceFieldOffset/GetToken...；QCall 侧 7 条（GetValue/SetValue...） |
| **MetadataImport（gMetaDataImport）** | 22 | 元数据查询全套；QCall 侧 2 条（Enum/GetMarshalAs） |
| **Signature（gSignatureNative）** | 3 | GetParameterOffsetInternal 等 |
| **RuntimeAssembly（gRuntimeAssemblyFuncs）** | 4 | GetIsDynamic/GetIsCollectible/GetManifestModule/GetTokenInternal；QCall 侧 AssemblyNative 39 条 |
| **AssemblyLoadContext（gAssemblyLoadContextFuncs）** | 1 | IsTracingEnabled |
| **StubHelpers（gStubHelperFuncs）** | 6 | GetDelegateTarget/SetLastError/ClearLastError/CalcVaListSize/LogPinnedArgument；QCall 侧 10 条 |
| **GCHandle（gGCHandleFuncs）** | 5 | _InternalAlloc/_InternalFree/InternalGet/InternalSet/InternalCompareExchange——弱引用/句柄核心；现有 weak_ref_add/remove 只是子集 |
| **Buffer（gBufferFuncs）** | 1 | BulkMoveWithWriteBarrier；QCall 侧另有 memset/memmove 基础原语 |

### 8.3 QCall 403 条与 corert 174 条：全部未接线（ILC 用 net48 形状替代，语义覆盖评估）
- ILC 已有近似替代的高频面：icall_method_invoke（约等于 RuntimeMethodHandle_InvokeMethod + ReflectionInvocation_PrepareMethod）、icall_object_get_type/icall_type_is_instance/icall_type_cast（约等于 TypeHandle_CanCastTo_NoCacheLookup/GetCorElementType 系）、icall_gc_weak_ref_*（约等于 GCHandle 系）、icall_string 系（String_Intern 已在 MethodMap.String.cs:220 接线）、icall_environment_*（约等于 Environment_Exit/FailFast/GetProcessorCount）。
- **无任何替代、ILC 完全空白的高价值 QCall 域**：RuntimeTypeHandle 34 条（MakeArray/Instantiate/GetGenericTypeDefinition/SatisfiesConstraints...）、AssemblyNative 39 条（GetExportedTypes/GetReferencedAssemblies/LoadFromPath...）、TypeBuilder 21 条、ModuleBuilder 9 条、RuntimeModule 3 条、AssemblyName 1 条、StackTrace/StackFrame 2 条、CustomAttribute 3 条、MetadataImport 2 条、UnsafeAccessors 1 条、AppContext 2 条、ClrConfig 1 条、NativeLibrary 1 条、String.Intern 系 2 条（Intern 已接，IsInterned 未接）。
- **corert 174 条参考价值**：与 ILC 现有 icall 语义一一对应的可抄实现——RhCollect/RhGetGcTotalMemory/RhSuppressFinalize（对应 icall_gc_*）、RhpArrayCopy/RhpArrayClear（对应 icall_array_*）、RhNewString/RhNewArray（对应分配）、RhTypeCast_*（对应 icall_type_cast/is_instance）、libm 全套（对应 icall_math_* 与 MathF 缺口）。

---

## 9. 结论与建议（供主代理派施工单）

1. **审计结论**：Core icall 契约（netcore 子集）= FCall 表 29 组约 200 条 + QCall 表 403 条 + C# 26 处声明；corert = 174 条 RuntimeImport。ILC 当前 icall 面（约 330 个）为 net48 形状，**FCall 缺口约 120 条（部分接线域 50 + 完全未接线 14 组 70+），QCall/corert 面未做同名接线**（功能替代评估见 8.3）。
2. **施工单候选（按优先级）**：
   - P0：String 分配（FastAllocateString + 8 ctor，9 条）——分配根，任何 Core 语义代码都依赖。
   - P0：Math 超函数 8 条 + DivModInt 8 条（corert libm + DivInt32Internal 可直接抄）。
   - P1：MathF 24 条（float 版，corert acosf...tanhf 参考）。
   - P1：GC 13 条 + GCSettings 5 条（corert GC.cs/Rh* 参考）。
   - P1：GCHandle 5 条、Environment.ExitCode 2 条、Thread 4 条、RuntimeHelpers 4 条、Marshal 4 条、DependentHandle 7 条。
   - P2：RuntimeTypeHandle 16 + QCall 34（反射核心，需与既有 icall_type_* 合并设计）、MetadataImport 22、RuntimeFieldHandle 10、RuntimeMethodHandle 18、RuntimeAssembly 4、AssemblyLoadContext 1、Signature 3、MethodTable 5、ObjectHeader 1、StubHelpers 6、JitInfo 3、GCFrameRegistration 2、Buffer 1。
   - P3：Interlocked object/pointer 3 条（需 CodeGenerator 加 void** 地址 cast 支持，见 MethodMap.Interlocked.cs 注释）。
3. **接线实现注意**（本审计实测发现）：
   - C# 侧 26 处 InternalCall 中，9 处（String ctor）无 NATIVEAOT 分支——纯 FCall 表；2 处（GCFrameRegistration）#if CORECLR；3 处（memset/memmove/CpuId）#if MONO + QCall 双路径——**按 FCall/QCall 表接，不按 C# 属性接**。
   - FastAllocateString 在子集无 C# 声明，但 FCall 表 FCDynamic 条目在——以表为准。
   - Math.DivModInt.cs 的 RuntimeImport 名与 FCall 表同名（DivInt32Internal 等），corert 侧实现可直接移植。
4. **跨平台提示**（原则 #8）：QCall 表含 TARGET_BROWSER/TARGET_WASI/TARGET_UNIX 专用条目（SystemJS_*、WasiFinalizer_*、GetEnvironmentStringsW 等）——ILC 只接平台无关面。

---

## 附录 A：核心文件路径索引

| 文件 | 绝对路径 |
|---|---|
| FCall 表（权威） | E:\il2cpp\reference\netcore\src\coreclr\vm\ecalllist.h |
| FCall 表（对照，差 1 行） | E:\il2cpp\reference\coreclr\src\coreclr\vm\ecalllist.h |
| QCall 表（权威，403 条） | E:\il2cpp\reference\netcore\src\coreclr\vm\qcallentrypoints.cpp |
| QCall 解析 | E:\il2cpp\reference\netcore\src\coreclr\vm\qcall.cpp / qcall.h / JitQCallHelpers.h |
| C# InternalCall 声明（9+8+2+2+1+1+2+1=26 处） | E:\il2cpp\reference\netcore\src\libraries\System.Private.CoreLib\src\System\String.cs / Math.DivModInt.cs / SpanHelpers.ByteMemOps.cs / Threading\Thread.cs / Runtime\Intrinsics\X86\X86Base.cs / Threading\ThreadPool.Browser.cs / Threading\TimerQueue.Browser.cs / Runtime\GCFrameRegistration.cs |
| corert 契约（174 条） | E:\il2cpp\reference\corert\src\System.Private.CoreLib\src\System\Runtime\RuntimeImports.cs |
| corert CppWriter 接线 | E:\il2cpp\reference\corert\src\ILCompiler.CppCodeGen\src\CppCodeGen\CppWriter.cs（642-652） |
| corert 原生实现 | E:\il2cpp\reference\corert\src\Native\Runtime\（HandleTableHelpers.cpp、thread.cpp 等） |
| ILC 能力面（约 330 icall） | E:\il2cpp\src\runtime\il2cpp_*.h / *.cpp |
| ILC 接线面（80+ 域） | E:\il2cpp\src\il2cppc\Emit\MethodMap.*.cs |
| ILC 既有内部缺口清单 | E:\il2cpp\docs\audit\icall-gap-list.md（2026-08-03） |

## 附录 B：方法级证据锚点（本审计实测，供复核）
- ecalllist.h：29 组全量列表见第 3 节（逐条读取该文件 47-418 行）。
- qcallentrypoints.cpp：403 条 DllImportEntry 实测计数（DllImportEntry 模式全文件统计），域分组见第 4 节。
- netcore CoreLib InternalCall 总数：grep "MethodImplOptions.InternalCall" 于 reference\netcore\src\libraries 全树 = 26 处（唯一命中文件即 5.1 表）。
- netcore CoreLib DllImport 总数：48 处。
- FastAllocateString：调用 67 处、声明 0 处（全树 grep "static.*FastAllocateString" / "extern string FastAllocateString" 均无命中）。
- corert RuntimeImport 唯一条目：174（RuntimeImports.cs 正则提取去重）。
- ILC icall_* 全量：src/runtime 递归 grep "icall_[a-z0-9_]+" 去重 = 约 330 个（第 8 节对照基准，未逐条列出；需要时以 il2cpp_*.h 为准）。
