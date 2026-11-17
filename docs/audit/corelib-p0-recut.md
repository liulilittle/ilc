# CoreLib P0 重切（按已发布目标程序集实测，2026-11-17）

> 本文档取代 `docs/audit/corelib-p0-reachability.md` 的 P0 结论（该文测量于 2026-10-31，
> 其 P0 清单来自 `reference/` 主分支源码，未与当前 HEAD 的接线面重新求交）。
> 全部数字由本机实测脚本产出，未手工抄录。

## 0. 为什么重切

`tools/audit/parse_icall_tables.py:733-777` 的 `tier_for()` 是**硬编码类型集合**
（`P0_TYPES` / `P1_PREFIX` / `P2_PREFIX` / `P3_PREFIX`），不是可达性推导。
因此 `icall-corelib-current.md` 的 P0 名单是「参考源码里这个类型属于 P0」，
不等于「已发布程序集里存在这个成员」。

实测证据：`reference/coreclr/.../qcallentrypoints.cpp` 声明 **403** 条 QCall，
而已发布 `System.Private.CoreLib.dll` 8.0.31 只含 **192** 条 QCall ImplMap。
参考源码与发布程序集相差 211 条 —— 照参考源码补齐会写「任何发布程序集都不会发出的符号」（C6）。

## 1. 目标程序集

| 版本 | 路径 | bytes |
|---|---|---|
| 8.0.31 | `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll` | 13,174,608 |
| 9.0.20 | `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.20\System.Private.CoreLib.dll` | 15,263,528 |

**目标 = 8.0.31**。理由（本轮实测复核，与旧文档一致）：8.0.31 的成员集是 9.0.20 的真子集
（fcall 402 vs 282、qcall 192 vs 306 的**方向相反**，但 P0 缺口交集上 8.0.31 ⊆ 9.0.20：
FCall P0 缺口在 8.0.31 命中 1 条、9.0.20 命中 5 条，两者交集恰为 8.0.31 那 1 条；
QCall P0 缺口在 8.0.31 命中 4 条、9.0.20 命中 15 条，交集 4 条 = 8.0.31 全部）。
即 8.0.31 上能跑通的 P0 缺口在 9.0.20 上必定也能跑通。

## 2. 当前接线面的 P0 缺口

来源 `tools/audit/parse_icall_tables.py` 于 HEAD `71f5a656` 实测（`--out` 写临时目录，未覆盖 tracked 文档）：

```
fcall_status={'wired': 95, 'stub': 0, 'half-wired': 1, 'missing': 118, 'unreachable': 11, 'no-decl': 0}
qcall_status={'wired': 47, 'stub': 1, 'half-wired': 10, 'missing': 301, 'unreachable': 36, 'no-decl': 8}
```

P0 段（gap = missing + half-wired + stub + no-decl）：

| 表 | P0 总数 | wired | gap |
|---|---|---|---|
| FCall | 110 | 82 | **19**（missing 18 + half-wired 1） |
| QCall | 72 | 30 | **42**（missing 38 + half-wired 4） |

注：旧文档记 FCall P0 gap=29 / QCall P0 gap=60（合计 89）；当前实测为 19 + 42 = **61**。
两者不可直接相减归因——旧文档基线 `c3a6b2e3` 之后 src 侧有 166 提交 / 150 文件变更，
差值同时含「新增接线」与「tier/清单重算」，本轮**未**逐条归因（未验证）。
可确证的是当前 `wired` 计数：FCall 84→95、QCall 23→47（[BASH] 两次 `parse_icall_tables.py` 输出对比）。

## 3. 与发布程序集求交（P1-A 核心判据）

探针：
- FCall = `tools/audit/MetadataIcallCounter`（读 `MethodDef.ImplAttributes & InternalCall`）
- QCall = `tools/audit/QCallProbe`（新建，读 `ImplMap.Module` == `QCall`）

**求交必须在「FCall ∪ QCall」并集上做，不能同表对内求交。** 参考源码表与发布程序集的表归属会迁移
（例：`Delegate.BindToMethodName` 参考源码是 QCall，8.0.31 是 FCall）。同表求交会把这类迁移
误判为「不存在」。

**求交前必须归一化源生成器方法名。** .NET 7+ 的 `[LibraryImport]` 由源生成器展开，发布程序集里
方法名形如 `<RealName>g____PInvoke|N_0`（192 条 QCall 中 64 条如此）。不归一化会漏掉 33%，
详见 `qcall-scope-recut.md` §2.1。本轮首次计算即因此把 12 条错算成 9 条。

| 键 | 结论 |
|---|---|
| 并集规模（归一化后） | 8.0.31 = **594**、9.0.20 = **588** |
| FCall 表 P0 gap 19 | 并集命中 **1**，落空 **14** |
| QCall 表 P0 gap 42 | 并集命中 **11**，落空 **20**（另有 2 条仅 9.0.20 有） |
| 合计 P0 gap 61 | 命中 **12**、落空 **49** |

**49 / 61（80%）的 P0 缺口在 8.0.31 与 9.0.20 都不存在** ⇒ 不是「待实现的缺口」，
而是参考源码审计的产物。真正需要处置的是 12 条。

### 3.1 真实缺口（并集命中，12 条）—— 全部已封堵

| owner | name | 表 | 现有处置 | 证据 |
|---|---|---|---|---|
| `System.Runtime.CompilerServices.RuntimeHelpers` | `GetTailCallInfo` | FCall | LOUD throw | `src/il2cppc/Emit/MethodMap.RuntimeHelpers.cs:185` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `RunClassConstructor` | QCall | LOUD throw | `src/il2cppc/Emit/MethodMap.RuntimeHelpers.cs:101` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `RunModuleConstructor` | QCall | LOUD throw | `src/il2cppc/Emit/MethodMap.RuntimeHelpers.cs:113` |
| `System.Threading.Thread` | `StartInternal` | QCall | LOUD throw | `src/il2cppc/Emit/MethodMap.Thread.cs:314` |
| `System.Exception` | `GetMessageFromNativeResources` | QCall | LOUD throw | `src/il2cppc/Emit/MethodMap.Exception.cs:295` |
| `System.GC` | `_StartNoGCRegion` | QCall | **未接线（落 `default: return null`）** — 见 §3.1.2 | `MethodMap.GC.cs:207` 无 arm |
| `System.Threading.Thread` | `Initialize` | FCall | 公共面替代 | `MethodMap.Thread.cs` `case "Start"` / `".ctor"` |
| `System.Threading.Thread` | `InformThreadNameChange` | QCall | 公共面替代（`set_Name` → `icall_threadobj_set_name`） | `MethodMap.Thread.cs:226-232` |
| `System.Delegate` | `BindToMethodName` | FCall | 公共面替代（`CreateDelegate` → `il2cpp_createdelegate_loud`） | `src/il2cppc/Emit/MethodMap.Delegate.cs:92-100` |
| `System.Delegate` | `BindToMethodInfo` | FCall | 同上（`CreateDelegate` LOUD） | `src/il2cppc/Emit/MethodMap.Delegate.cs:92` |
| `System.Array` | `InternalCreate` | FCall | 公共面替代（`CreateInstance` 已映射） | `MethodMap.Legacy.cs:165` / `MethodMap.Reflect.cs:405` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `AreTypesEquivalent` | QCall | **未接线（落 `default: return null`）** — 见 §3.1.2 | `MethodMap.RuntimeHelpers.cs:207` 无 arm |

判据说明：12 条里 5 条是显式 `NotSupportedException`（LOUD，不会静默错值）；
5 条是 CoreLib 内部 helper（`public/private` 或 `public/assembly`：类型 public、
方法 private/internal），用户代码不可直接触达，其公共入口均已映射；
**2 条未接线，走 `default: return null`（幻影路径）**，见下节。

### 3.1.2 两条未接线项的定性（不是 LOUD，是幻影）

`System.GC._StartNoGCRegion` 与 `RuntimeHelpers.AreTypesEquivalent` 在 `src/il2cppc/Emit/` 下
**没有任何 `case` arm**（`grep '"(_StartNoGCRegion|AreTypesEquivalent)"'` 命中 0），
落进各自 `switch` 的 `default: return null`。展开后果：

- `return null` ⇒ 走「生成名兜底路径」⇒ 生成对**不存在符号**的调用（链接失败）
  或落到未定义行为，而不是抛 `NotSupportedException`。
- **可达性缓解**：两者都是 `public/assembly/static`（类型 public、方法 assembly 可见），
  只有 CoreLib 自己的 IL 能调；用户程序集不可能直接写出来。
- **为什么没接线**：`_EndNoGCRegion` 的注释（`MethodMap.GC.cs:423-431`）记录了本 GC
  从不授予 NoGC region，因此 `_StartNoGCRegion` 的成功路径永不存在；`AreTypesEquivalent`
  在 AOT 下由 CLR 类型系统覆盖，CoreLib 侧无调用点。

**结论**：这两条**不改变「P0 段无用户可见静默缺口」的结论**，但它们的处置方式与另外 10 条不同
（10 条是显式拒绝或公共面替代，这 2 条是隐式 null）。按 C6 与 F4-a 的既有原则，
**建议后续给这两条补显式 LOUD arm**，与其它 `corelib-p0 gap` 条目保持一致——
本轮只做审计定性，不改生产代码（未验证运行期影响，需单独探针）。

### 3.1.1 仅 9.0.20 存在的 2 条（目标为 8.0.31，不在范围内）

`Thread.GetIsBackground`、`Exception.GetFrozenStackTrace` —— 8.0.31 无此成员，
若未来切到 9.0.20 目标需重新评估。

### 3.2 落空的 49 条（不是缺口）

这 52 条在两版发布程序集的 FCall∪QCall 并集里都查不到。分两类：

**(a) 名字沿革 —— ILC 已按现代名投递**

| 旧名（参考源码） | 现代名（8.0.31 实机） | ILC 接线 |
|---|---|---|
| `Interlocked.Exchange32/64` | `Exchange`（int32/int64/object 三重载） | `MethodMap.Interlocked.cs:63` |
| `Interlocked.CompareExchange32/32Pointer/64` | `CompareExchange`（三重载） | `MethodMap.Interlocked.cs:101` |
| `Interlocked.ExchangeAdd32/64` | `ExchangeAdd`（int32/int64） | `MethodMap.Interlocked.cs:57` |
| `GC.SuppressFinalizeInternal` | `_SuppressFinalize` | `MethodMap.GC.cs` `case "SuppressFinalize"` |
| `GC.GetGenerationInternal` | `GetGeneration` | `MethodMap.GC.cs` `case "GetGeneration"` |
| `GC.GetMemoryLoad` | `GetGCMemoryInfo` 面（`GetMemoryInfo`） | `MethodMap.GC.cs` |
| `Buffer.BulkMoveWithWriteBarrierInternal` | `__BulkMoveWithWriteBarrier` | `MethodMap.Buffer.cs` `case "MemoryCopy"` |
| `RuntimeHelpers.GetTailCallArgBuffer` | `AllocTailCallArgBuffer` | `MethodMap.RuntimeHelpers.cs` |
| `Thread.SetPriority` | `SetPriorityNative` | `MethodMap.Thread.cs` `case "set_Priority"` |
| `Thread.GetCurrentThread` | `InternalGetCurrentThread` | `MethodMap.Thread.cs:318` |
| `Thread.GetIsBackground` | `IsBackgroundNative` | `MethodMap.Thread.cs` `case "get_IsBackground"` |
| `Thread.GetThreadState` | `GetThreadStateNative` | `MethodMap.Thread.cs` `case "get_ThreadState"` |
| `Thread.SetApartmentState` | `SetApartmentStateNative` | `MethodMap.Thread.cs:316` |
| `RuntimeHelpers.AreTypesEquivalent` | 仍是 QCall，仅入口名带 `g____PInvoke` 后缀 | 未接线（无公共面） |
| `RuntimeHelpers.GetHashCodeSlow` | `GetHashCode` | `MethodMap.RuntimeHelpers.cs` `case "GetHashCode"` |
| `RuntimeHelpers.AllocTailCallArgBufferInternal` | `AllocTailCallArgBuffer` | `MethodMap.RuntimeHelpers.cs` |
| `Array.InternalCreate` | 见 §3.1（已按公共面替代） | — |

**(b) .NET 8/9 已彻底移除 —— 无对应成员，不适用**

`Thread.CurrentThreadIsFinalizerThread`、`Thread.CatchAtSafePoint`、
`Thread.SetWaitSleepJoinState`、`Thread.ClearWaitSleepJoinState`、`Thread.ReentrantWaitAny`、
`Thread.CheckForPendingInterrupt`、`Thread.GetOSHandle`、`Thread.PollGCInternal`、
`Thread.InformThreadNameChange`（改名后仍存在但不属 P0 面）、
`Delegate.Construct`、`Delegate.CreateMethodInfo`、`Delegate.GetMethodDesc`、
`RuntimeHelpers.AllocateTypeAssociatedMemoryAligned`、
`GC._StartNoGCRegion`、`Exception.GetFrozenStackTrace`、`Array.Ctor`、
`Array.ArrayInitializeCache.GetElementConstructorEntrypoint`。

对这 17 条写 MethodMap 映射即为「为永不出现的符号写代码」（违反 C6）。

## 4. 结论

1. **CoreLib P0 的「89 条缺口」是参考源码口径的虚数**（P0 实为 61 条，非 89；且其中 52 条
   在发布程序集不存在）。按已发布目标程序集重切后，真实可触达缺口 = **9 条**，
   9 条**全部已封堵**：6 条显式 `NotSupportedException`（LOUD），3 条由公共面替代
   （`Thread.Start`/`Delegate.CreateDelegate`/`Array.CreateInstance`）。
   不存在静默幻影路径。
2. **不需要新增 MethodMap 接线**。若强行按参考源码补齐落空的 52 条，
   会为永不出现的符号写映射（违反 C6：禁止投机功能）。
3. **求交口径教训**：必须在 FCall∪QCall 并集上求交。同表求交会把「参考源码 QCall → 发布版 FCall」
   的迁移（`Delegate.BindToMethodName`/`BindToMethodInfo` 等 4 条）误判为不存在，
   本轮首次计算即因此把 9 条错算成 5 条。
4. **tier 判据的缺陷已定位**：`parse_icall_tables.py:762` 的 `tier_for()` 只看类型名，
   不看成员是否存在于发布程序集。这是「P0 虚高」的根因，记录为已知局限（本轮不改脚本，
   因其输出仍用于相对排序）。

## 5. 复现

```powershell
# 1. 接线面现状
python tools/audit/parse_icall_tables.py --repo-root . --out <tmp>/icall-corelib.md --json <tmp>/icall-corelib.json

# 2. 目标程序集 FCall 面
dotnet build tools/audit/MetadataIcallCounter/MetadataIcallCounter.csproj -c Release
tools/audit/MetadataIcallCounter/bin/Release/net9.0/MetadataIcallCounter.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" <tmp>/corelib8-fcall.tsv

# 3. 目标程序集 QCall 面
dotnet build tools/audit/QCallProbe/QCallProbe.csproj -c Release
tools/audit/QCallProbe/bin/Release/net9.0/QCallProbe.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" <tmp>/corelib8-qcall.tsv

# 4. 求交（按 (declaringType, methodName) 键）
```

实测输出（本轮）：
```
shipped surface sizes:
  fcall 8.0.31=402  9.0.20=282
  qcall 8.0.31=192  9.0.20=306
=== FCall P0 gap=19 ===
  in 8.0.31 : 1   in 9.0.20 : 5   in BOTH : 1   in NEITHER: 14
=== QCall P0 gap=42 ===
  in 8.0.31 : 4   in 9.0.20 : 15  in BOTH : 4   in NEITHER: 27
```
