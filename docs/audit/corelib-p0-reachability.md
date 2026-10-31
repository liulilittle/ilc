# CoreLib P0 reachability against SHIPPED `System.Private.CoreLib.dll` (measured 2026-10-31)

## Why this file exists

`docs/audit/icall-corelib-current.md` derives its P0 list from the **reference sources**
(`reference/coreclr/src/coreclr/System.Private.CoreLib/src/...`), i.e. the CoreCLR main
branch. Several of those member names **do not exist in any released CoreLib**, so wiring
them would be writing mappings for symbols no shipped assembly ever emits (C6: no
speculative features). This file records what is actually reachable.

Method: read every `TypeDefinition` of the real DLL with `System.Reflection.Metadata`,
print each method whose `ImplAttributes` has `InternalCall` (`impl=0x1000`, an FCall with no
ImplMap) or whose `ImplMap` module is `QCall` (`IMPORT(QCall!<Entry>)`). Probe source and raw
dumps: `.opencode/tmp/corelib-probe/` (`Program.cs`, `dump8.txt`, `dump9.txt`, `all8.txt`,
`all9.txt`).

Assemblies measured (both installed on this machine, listed by `dotnet --list-runtimes`):

| version | path | bytes |
|---|---|---|
| 8.0.31 | `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll` | 13,174,608 |
| 9.0.20 | `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.20\System.Private.CoreLib.dll` | 15,263,528 |

## Summary

| list | rows | in 8.0.31 | in 9.0.20 | in BOTH | in NEITHER |
|---|---|---|---|---|---|
| FCall P0 (`icall-corelib-current.md` §4.1) | 29 | 11 | 15 | 11 | **14** |
| QCall P0 (`icall-corelib-current.md` §4.2) | 60 | 21 | 32 | 21 | **28** |

Interpretation:

- **42 of the 89 P0 entries are unreachable on every shipped runtime measured here.** They are
  not "gaps to implement"; they are artifacts of auditing main-branch sources. The CoreLib P0
  scope must be re-cut against a chosen target assembly before any implementation work.
- Nothing is 8-only: every name present in 8.0.31 is also present in 9.0.20. So 8.0.31 is a
  strict subset and **choosing 8.0.31 as the byte-exact target maximises overlap with 9.0.20**.
- The audit's `Interlocked.CompareExchange32 / CompareExchange32Pointer / CompareExchange64 /
  Exchange32 / Exchange64 / ExchangeAdd32 / ExchangeAdd64` naming does not exist in either
  release. .NET 8 spells the members `Exchange` / `CompareExchange` / `ExchangeAdd` (overloaded
  on int32 / int64 / object, all `INTERNALCALL`), and .NET 9 keeps only the object overloads as
  `ExchangeObject` / `CompareExchangeObject` — the numeric ones became plain managed code.

## Mechanism already present in the repo (no new machinery needed)

- **FCall** = `impl=0x1000` with no ImplMap. The transpiler already routes these through the
  MethodMap icall surface.
- **QCall** = `ImplMap` with `ModuleRef` name `QCall`. Two places already know this:
  - `src/il2cppc/Emit/CodeGenerator.cs:1508-1520` — a QCall whose owner/member the MethodMap
    covers gets its naked `extern` declaration **suppressed** (the resolver short-circuits every
    call site); unmapped QCalls keep the legacy declaration.
  - `src/il2cppc/Emit/CodeGenerator.cs:13960-13975` — `"QCall"` is explicitly excluded from the
    dynamic `il2cpp_pinvoke_resolve` path and stays on the static declaration path.
- So CoreLib work is **pure MethodMap + runtime implementation**, not new plumbing.

## Byte-exact verification is already possible for a net8/net9 oracle

`scripts/difftest.ps1` builds the oracle with `dotnet build <case>.csproj`, so a case whose
`TargetFramework` is `net8.0`/`net9.0` is compared against the real modern runtime. Measured:
`bcldeep` (csproj `net8.0`) → `ok=True`, oracle sha == runtime sha ==
`a2a8f56868c41ae3e752d1bd7ae17829a3efecf864f195f103b9100a1bdff985`.
The repo currently has 4 `net8.0` cases (`bcldeep`, `dhprobe`, `interp-reftype`,
`interp-valtype`) and 1 `net9.0` case; the other 1118 are `net48`.

## FCall P0 (29) — presence per assembly

`yes` = the type+method exists in that assembly.

| member | 8.0.31 | 9.0.20 |
|---|---|---|
| `System.Buffer.BulkMoveWithWriteBarrierInternal` | no | no |
| `System.Exception.IsImmutableAgileException` | yes | yes |
| `System.Exception.PrepareForForeignExceptionRaise` | yes | yes |
| `System.GC.GetGenerationInternal` | no | no |
| `System.GC.GetMemoryLoad` | no | no |
| `System.GC.GetTotalAllocatedBytesApproximate` | no | yes |
| `System.GC.SuppressFinalizeInternal` | no | no |
| `System.GC._CancelFullGCNotification` | yes | yes |
| `System.GC._CollectionCount` | yes | yes |
| `System.GC._GetTotalPauseDuration` | yes | yes |
| `System.GC._RegisterForFullGCNotification` | yes | yes |
| `System.Math.ModF` | yes | yes |
| `System.Runtime.CompilerServices.RuntimeHelpers.ContentEquals` | no | yes |
| `System.Runtime.CompilerServices.RuntimeHelpers.GetTailCallArgBuffer` | no | no |
| `System.Runtime.CompilerServices.RuntimeHelpers.GetTailCallInfo` | yes | yes |
| `System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack` | yes | yes |
| `System.Runtime.CompilerServices.RuntimeHelpers.TryGetHashCode` | yes | yes |
| `System.Threading.Interlocked.CompareExchange32` | no | no |
| `System.Threading.Interlocked.CompareExchange32Pointer` | no | no |
| `System.Threading.Interlocked.CompareExchange64` | no | no |
| `System.Threading.Interlocked.CompareExchangeObject` | no | yes |
| `System.Threading.Interlocked.Exchange32` | no | no |
| `System.Threading.Interlocked.Exchange64` | no | no |
| `System.Threading.Interlocked.ExchangeAdd32` | no | no |
| `System.Threading.Interlocked.ExchangeAdd64` | no | no |
| `System.Threading.Interlocked.ExchangeObject` | no | yes |
| `System.Threading.Thread.CatchAtSafePoint` | no | no |
| `System.Threading.Thread.CurrentThreadIsFinalizerThread` | no | no |
| `System.Threading.Thread.InternalFinalize` | yes | yes |

## QCall P0 (60) - presence per assembly

`yes` = the owner method exists with exactly that `IMPORT(QCall!<EntryPoint>)`.

| EntryPoint | owner | 8.0.31 | 9.0.20 |
|---|---|---|---|
| `Array_Ctor` | `System.Array.Ctor` | no | no |
| `Array_CreateInstance` | `System.Array.InternalCreate` | no | no |
| `Array_GetElementConstructorEntrypoint` | `System.Array.ArrayInitializeCache.GetElementConstructorEntrypoint` | no | no |
| `Delegate_BindToMethodInfo` | `System.Delegate.BindToMethodInfo` | no | no |
| `Delegate_BindToMethodName` | `System.Delegate.BindToMethodName` | no | no |
| `Delegate_Construct` | `System.Delegate.Construct` | no | no |
| `Delegate_CreateMethodInfo` | `System.Delegate.CreateMethodInfo` | no | no |
| `Delegate_GetMethodDesc` | `System.Delegate.GetMethodDesc` | no | no |
| `Delegate_GetMulticastInvokeSlow` | `System.Delegate.GetMulticastInvokeSlow` | no | yes |
| `Delegate_InitializeVirtualCallStub` | `System.Delegate.InitializeVirtualCallStub` | no | yes |
| `ExceptionNative_GetFrozenStackTrace` | `System.Exception.GetFrozenStackTrace` | no | no |
| `ExceptionNative_GetMessageFromNativeResources` | `System.Exception.GetMessageFromNativeResources` | yes | yes |
| `ExceptionNative_GetMethodFromStackTrace` | `System.Exception.GetMethodFromStackTrace` | no | yes |
| `GCInterface_GetNextFinalizableObject` | `System.GC.GetNextFinalizeableObject` | no | yes |
| `GCInterface_GetTotalAllocatedBytesPrecise` | `System.GC.GetTotalAllocatedBytesPrecise` | no | yes |
| `GCInterface_AddMemoryPressure` | `System.GC._AddMemoryPressure` | yes | yes |
| `GCInterface_Collect` | `System.GC._Collect` | yes | yes |
| `GCInterface_EnableNoGCRegionCallback` | `System.GC._EnableNoGCRegionCallback` | yes | yes |
| `GCInterface_EndNoGCRegion` | `System.GC._EndNoGCRegion` | yes | yes |
| `GCInterface_EnumerateConfigurationValues` | `System.GC._EnumerateConfigurationValues` | yes | yes |
| `GCInterface_GetGenerationBudget` | `System.GC._GetGenerationBudget` | yes | yes |
| `GCInterface_RefreshMemoryLimit` | `System.GC._RefreshMemoryLimit` | yes | yes |
| `GCInterface_RegisterFrozenSegment` | `System.GC._RegisterFrozenSegment` | yes | yes |
| `GCInterface_RemoveMemoryPressure` | `System.GC._RemoveMemoryPressure` | yes | yes |
| `GCInterface_StartNoGCRegion` | `System.GC._StartNoGCRegion` | no | no |
| `GCInterface_UnregisterFrozenSegment` | `System.GC._UnregisterFrozenSegment` | yes | yes |
| `GCInterface_WaitForFullGCApproach` | `System.GC._WaitForFullGCApproach` | no | yes |
| `GCInterface_WaitForFullGCComplete` | `System.GC._WaitForFullGCComplete` | no | yes |
| `GCInterface_WaitForPendingFinalizers` | `System.GC._WaitForPendingFinalizers` | yes | yes |
| `TailCallHelp_AllocTailCallArgBufferInternal` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocTailCallArgBufferInternal` | no | no |
| `RuntimeTypeHandle_AllocateTypeAssociatedMemory` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocateTypeAssociatedMemory` | yes | yes |
| `RuntimeTypeHandle_AllocateTypeAssociatedMemoryAligned` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocateTypeAssociatedMemoryAligned` | no | no |
| `ObjectNative_AllocateUninitializedClone` | `System.Runtime.CompilerServices.RuntimeHelpers.AllocateUninitializedClone` | no | yes |
| `MethodTable_AreTypesEquivalent` | `System.Runtime.CompilerServices.RuntimeHelpers.AreTypesEquivalent` | no | no |
| `ReflectionInvocation_CompileMethod` | `System.Runtime.CompilerServices.RuntimeHelpers.CompileMethod` | yes | yes |
| `ObjectNative_GetHashCodeSlow` | `System.Runtime.CompilerServices.RuntimeHelpers.GetHashCodeSlow` | no | no |
| `ReflectionInvocation_PrepareDelegate` | `System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate` | no | no |
| `ReflectionInvocation_PrepareMethod` | `System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod` | yes | yes |
| `ReflectionInvocation_RunClassConstructor` | `System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor` | yes | yes |
| `ReflectionInvocation_RunModuleConstructor` | `System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor` | yes | yes |
| `ReflectionInvocation_SizeOf` | `System.Runtime.CompilerServices.RuntimeHelpers.SizeOf` | no | yes |
| `Interlocked_MemoryBarrierProcessWide` | `System.Threading.Interlocked._MemoryBarrierProcessWide` | yes | yes |
| `ThreadNative_CheckForPendingInterrupt` | `System.Threading.Thread.CheckForPendingInterrupt` | no | no |
| `ThreadNative_ClearWaitSleepJoinState` | `System.Threading.Thread.ClearWaitSleepJoinState` | no | no |
| `ThreadNative_GetCurrentOSThreadId` | `System.Threading.Thread.GetCurrentOSThreadId` | yes | yes |
| `ThreadNative_GetCurrentThread` | `System.Threading.Thread.GetCurrentThread` | no | no |
| `ThreadNative_GetIsBackground` | `System.Threading.Thread.GetIsBackground` | no | no |
| `ThreadNative_GetOSHandle` | `System.Threading.Thread.GetOSHandle` | no | no |
| `ThreadNative_GetThreadState` | `System.Threading.Thread.GetThreadState` | no | no |
| `ThreadNative_InformThreadNameChange` | `System.Threading.Thread.InformThreadNameChange` | no | no |
| `ThreadNative_Initialize` | `System.Threading.Thread.Initialize` | no | no |
| `ThreadNative_PollGC` | `System.Threading.Thread.PollGCInternal` | no | no |
| `ThreadNative_ReentrantWaitAny` | `System.Threading.Thread.ReentrantWaitAny` | no | no |
| `ThreadNative_SetApartmentState` | `System.Threading.Thread.SetApartmentState` | no | no |
| `ThreadNative_SetIsBackground` | `System.Threading.Thread.SetIsBackground` | no | yes |
| `ThreadNative_SetPriority` | `System.Threading.Thread.SetPriority` | no | no |
| `ThreadNative_SetWaitSleepJoinState` | `System.Threading.Thread.SetWaitSleepJoinState` | no | no |
| `ThreadNative_SpinWait` | `System.Threading.Thread.SpinWaitInternal` | no | yes |
| `ThreadNative_Start` | `System.Threading.Thread.StartInternal` | yes | yes |
| `ThreadNative_YieldThread` | `System.Threading.Thread.YieldInternal` | yes | yes |

## Consequence for the P0 plan

1. **Re-cut the CoreLib P0 scope against 8.0.31** (the strict subset of 9.0.20 measured here).
   Reachable-and-unimplemented is the real backlog: FCall 11 + QCall 21 = **32 rows**, not 89.
2. The 42 unreachable rows must be moved to a separate "reference-source only" section of
   `icall-corelib-current.md` so no future seat implements them.
3. Every CoreLib e2e case must declare `net8.0` in its csproj so the oracle is the shipped
   assembly, and the shas recorded as anchors must come from that oracle.
4. The 8-only column is empty, so picking 8.0.31 loses nothing relative to 9.0.20; picking
   9.0.20 would additionally require 11 QCall rows that 8.0.31 never emits.

## Reproduce

    \C:\Python314\Scripts\;C:\Python314\;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0\;C:\WINDOWS\System32\OpenSSH\;C:\Program Files\dotnet\;C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\;C:\Program Files\Microsoft SQL Server\150\Tools\Binn\;C:\Program Files (x86)\Incredibuild;C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\;C:\Program Files\Git\cmd;C:\Program Files\nodejs\;C:\ProgramData\chocolatey\bin;D:\Program Files\opencode;C:\Program Files\GitHub CLI\;C:\Program Files\OpenSSL-Win64\bin;C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64;C:\Python314\Scripts\;C:\Python314\;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0\;C:\WINDOWS\System32\OpenSSH\;C:\Program Files\dotnet\;C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\;C:\Program Files\Microsoft SQL Server\150\Tools\Binn\;C:\Program Files (x86)\Incredibuild;C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\;C:\Program Files\Git\cmd;C:\Program Files\nodejs\;C:\ProgramData\chocolatey\bin;D:\Program Files\opencode;C:\Users\liulilte\AppData\Local\Microsoft\WindowsApps;C:\Users\liulilte\AppData\Local\Programs\Microsoft VS Code\bin;C:\Users\liulilte\.dotnet\tools;C:\Users\liulilte\AppData\Roaming\npm;C:\Users\liulilte\AppData\Local\Programs\Microsoft VS Code\bin\copilot-shim="C:\Program Files\dotnet"
    dotnet run --project F:\il2cpp\.opencode\tmp\corelib-probe -c Release -- 
        "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" all 
        > F:\il2cpp\.opencode\tmp\corelib-probe\all8.txt

The dump lines this doc was built from are `all8.txt` / `all9.txt` in that same directory.
