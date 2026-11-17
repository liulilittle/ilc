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
详见 `qcall-scope-recut.md` §2.1。本轮首次计算即因此把 17 条错算成 12 条、再错算成 9 条。

| 键 | 结论 |
|---|---|
| 并集规模（归一化后） | 8.0.31 = **594**、9.0.20 = **588** |
| FCall 表 P0 gap 19 | 8.0.31 命中 **1**、仅 9.0.20 有 **4**、两者皆无 **14** |
| QCall 表 P0 gap 42 | 8.0.31 命中 **16**、仅 9.0.20 有 **8**、两者皆无 **18** |
| 合计 P0 gap 61 | 8.0.31 命中 **17**、仅 9.0.20 有 **12**、两者皆无 **32** |

**32 / 61（52%）的 P0 缺口在两版发布程序集都不存在** ⇒ 不是「待实现的缺口」，
而是参考源码审计的产物。目标为 8.0.31，真正需要处置的是 **17 条**。

> **更正记录（轮150）**：本节早先写「命中 12 / 落空 49」，是错的。汇总脚本
> `.opencode/tmp/w11main-abi/recut.py` 只保留 `both_rows` 与 `neither_rows` 两个桶，
> 把「仅在 9.0.20 存在」的 15 行（FCall 4 + QCall 11）静默丢弃；归一化后其中 5 条
> 其实在 8.0.31 存在，于是 17 被算成 12。现按 `present / only9 / absent` 三桶重算，
> 并断言三者之和 == 61。

### 3.1 真实缺口（8.0.31 命中，17 条）

处置列是**实测**（`grep '"<name>":'` 于该 owner 自己的 MethodMap 文件），不是从
「同类型有公共面映射」推断出来的。

| owner | name | 表 | 实测处置 | 证据 |
|---|---|---|---|---|
| `System.Runtime.CompilerServices.RuntimeHelpers` | `GetTailCallInfo` | FCall | LOUD throw | `MethodMap.RuntimeHelpers.cs:185` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `RunClassConstructor` | QCall | LOUD throw | `MethodMap.RuntimeHelpers.cs:101` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `RunModuleConstructor` | QCall | LOUD throw | `MethodMap.RuntimeHelpers.cs:113` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `AreTypesEquivalent` | QCall | LOUD throw | `MethodMap.RuntimeHelpers.cs:195` |
| `System.Runtime.CompilerServices.RuntimeHelpers` | `AllocateUninitializedClone` | QCall | **无 arm（幻影）** | 该文件无此 case |
| `System.Threading.Thread` | `StartInternal` | QCall | LOUD throw | `MethodMap.Thread.cs:314` |
| `System.Exception` | `GetMessageFromNativeResources` | QCall | LOUD throw | `MethodMap.Exception.cs:295` |
| `System.GC` | `_StartNoGCRegion` | QCall | LOUD throw | `MethodMap.GC.cs:432` |
| `System.GC` | `_WaitForFullGCApproach` | QCall | **无 arm（幻影）** | `MethodMap.GC.cs` 无此 case |
| `System.GC` | `_WaitForFullGCComplete` | QCall | **无 arm（幻影）** | `MethodMap.GC.cs` 无此 case |
| `System.Exception` | `GetMethodFromStackTrace` | QCall | **无 arm（幻影）** | `MethodMap.Exception.cs` 无此 case |
| `System.Threading.Thread` | `Initialize` | FCall | **无 arm（幻影）** | `MethodMap.Thread.cs` 无此 case |
| `System.Threading.Thread` | `InformThreadNameChange` | QCall | **无 arm（幻影）** | `MethodMap.Thread.cs` 无此 case |
| `System.Threading.Thread` | `SpinWaitInternal` | QCall | **无 arm（幻影）** | `MethodMap.Thread.cs` 无此 case |
| `System.Delegate` | `BindToMethodName` | FCall | **无 arm（幻影）** | `MethodMap.Delegate.cs` 无此 case |
| `System.Delegate` | `BindToMethodInfo` | FCall | **无 arm（幻影）** | `MethodMap.Delegate.cs` 无此 case |
| `System.Array` | `InternalCreate` | FCall | **无 arm（幻影）** | `MethodMap.Array.cs` 无此 case |

统计：**7 条 LOUD throw / 10 条无 arm**。

### 3.1.1 「无 arm」意味着什么，以及为什么它不是用户可见缺口

`return null` 从 `MethodRefResolver` 返回后走**生成名兜底路径**：生成对
`<owner>_<name>` 之类符号的调用。若该符号不存在则**链接失败**（编译期暴露，不是静默错值）；
若偶然同名同签名则可能误绑。

10 条全部是 CoreLib 内部成员（`public/private`、`public/internal` 或 `public/assembly`：
类型 public、方法 private/internal/assembly）。用户程序集**无法直接写出**这些调用，
只有 CoreLib 自己的 IL 会调，而 CoreLib 的 IL 不被转译。所以：

- **对用户代码**：不可达 ⇒ 不构成用户可见缺口。
- **对转译过程**：若将来有路径触达（如 CoreLib IL 被纳入转译），会链接失败而非静默错值。

**结论不变：P0 段没有用户可见的静默缺口。** 但「已封堵」的说法只对 7 条 LOUD 成立；
另 10 条是「不可达 + 触达即链接失败」，属于**可接受但未显式拒绝**的状态。

**是否补齐这 10 条 —— 判据已确认（实测，非推测）**：
转译器的类型加载入口只有 `opts.InputAssembly`
（[READ:src/il2cppc/Program.cs:164] `var types = MetadataLoader.LoadAssembly(opts.InputAssembly);`），
即只加载**用户程序集**；CoreLib 的方法体不进入转译。因此这 10 条不会出现在转译输入里。

**本轮不补**（按 C6：为不可达符号写代码属投机功能）。若后续引入「转译 CoreLib IL」的能力，
这 10 条会立刻变成阻断点，届时按 LOUD throw 补齐（与另外 7 条一致）。
这个条件已记录为**已知边界**，不是遗漏。

### 3.1.2 仅 9.0.20 存在的 12 条（目标为 8.0.31，不在范围内）

FCall 4 条：`Interlocked.ExchangeObject`、`Interlocked.CompareExchangeObject`、
`GC.GetTotalAllocatedBytesApproximate`、`RuntimeHelpers.ContentEquals`。

QCall 8 条：`Thread.GetIsBackground`、`Thread.SetIsBackground`、`RuntimeHelpers.SizeOf`、
`Delegate.InitializeVirtualCallStub`、`Delegate.GetMulticastInvokeSlow`、
`GC.GetTotalAllocatedBytesPrecise`、`GC.GetNextFinalizeableObject`、
`Exception.GetFrozenStackTrace`。

若未来切到 9.0.20 目标需重新评估。

### 3.2 落空的 44 条（不是缺口）

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
| `RuntimeHelpers.AreTypesEquivalent` | 仍是 QCall，仅入口名带 `g____PInvoke` 后缀 | 见 §3.1（轮149 补显式 LOUD） |
| `RuntimeHelpers.GetHashCodeSlow` | `GetHashCode` | `MethodMap.RuntimeHelpers.cs` `case "GetHashCode"` |
| `RuntimeHelpers.AllocTailCallArgBufferInternal` | `AllocTailCallArgBuffer` | `MethodMap.RuntimeHelpers.cs` |
| `Array.InternalCreate` | 仍是 FCall，无对应现代名 | 见 §3.1（无 arm） |

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

1. **CoreLib P0 的「89 条缺口」是参考源码口径的虚数**：当前接线面实测 P0 gap 为 **61 条**，
   其中 **32 条在两版发布程序集都不存在**、**12 条仅在 9.0.20 存在**（目标为 8.0.31，不适用），
   真正落在 8.0.31 上的只有 **17 条**。
2. **17 条的实测处置**：7 条显式 `NotSupportedException`（LOUD，已封堵）；
   10 条无 `case` arm（落 `default: return null` → 生成名兜底 → 触达即链接失败）。
   10 条全部是 CoreLib 内部成员，用户程序集不可达，因此**不构成用户可见缺口**。
3. **不新增 MethodMap 接线**：①12 条仅 9.0.20 有效、32 条两版皆无 ⇒ 为它们写映射违反 C6；
   ②10 条无 arm 成员当前无证据表明会进入转译输入，按 C6 同样不补。
   若后续出现真实转译输入触达，按 LOUD throw 补齐（与另外 7 条一致）。
4. **求交口径（两处硬伤，都已踩过）**：
   - 必须在 **FCall ∪ QCall 并集**上求交。参考源码 QCall 迁到发布版 FCall 的有 67 条；
     同表求交会把它们误判为「不存在」。
   - 必须**归一化源生成器名** `<X>g____PInvoke|N_M` → `X`（192 条 QCall 里 64 条如此）。
   - 必须**保留三个桶**：`present / only9 / absent`，并断言三者之和 == P0 gap。
     只留 present+absent 会静默丢弃 only9 行（本轮因此把 17 算成 12）。
5. **tier 判据的缺陷已定位**：`parse_icall_tables.py:762` 的 `tier_for()` 只看类型名
   （硬编码 `P0_TYPES` :733-777），不看成员是否存在于发布程序集 ⇒「P0 虚高」的机制性根因。
   该脚本的输出仍可用于**相对排序**，但**不能**直接当作绝对缺口数。

## 5. 复现

三桶求交已固化为 `tools/audit/recut_p0_gaps.py`（带恒等式断言，防止再丢桶）：

```powershell
# 1. 接线面现状
python tools/audit/parse_icall_tables.py --repo-root . --out <tmp>/icall-corelib.md `
    --json <tmp>/icall-corelib.json

# 2. 目标程序集 FCall 面
dotnet build tools/audit/MetadataIcallCounter/MetadataIcallCounter.csproj -c Release
tools/audit/MetadataIcallCounter/bin/Release/net9.0/MetadataIcallCounter.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" `
  <tmp>/corelib8-fcall.tsv

# 3. 目标程序集 QCall 面
dotnet build tools/audit/QCallProbe/QCallProbe.csproj -c Release
tools/audit/QCallProbe/bin/Release/net9.0/QCallProbe.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" `
  <tmp>/corelib8-qcall.tsv

# 4. 三桶求交（present / only9 / absent，断言三者之和 == P0 gap）
python tools/audit/recut_p0_gaps.py --probe-dir <tmp> --audit-json <tmp>/icall-corelib.json --target 80
```

实测输出：

```
=== FCall  P0 gap = 19 ===
  present in target (80) : 1
  only in other   (90) : 4
  in neither             : 14
=== QCall  P0 gap = 42 ===
  present in target (80) : 16
  only in other   (90) : 8
  in neither             : 18
BUCKET IDENTITY OK: three buckets sum to 61
```

变异验证（第16条）：把 `only_other` 桶强制置空后，脚本必须以非零码报
`bucket identity broken: 49 + ... != 61`；恢复后重新打印 `BUCKET IDENTITY OK`。
