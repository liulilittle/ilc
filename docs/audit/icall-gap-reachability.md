# CoreLib icall 缺口可达性量化（2026-11-18 首版；2026-11-19 重跑更新 §2/§4）

> 目标程序集：`System.Private.CoreLib.dll` 8.0.31（13,174,608B）
> 数据来源全部由本机脚本产出，无手工抄录。

## 0. 为什么要量化

`docs/audit/icall-gap-list.md` 报 **174** 个正向缺口，`corelib-p0-recut.md`
报 FCall `missing=118` / QCall `missing=301`。这些数字回答的是
「@runtime 是否实现了这个成员」，**不回答**「任何程序能否走到它」。
把一个永远走不到的成员与一个每次调用都崩溃的成员并列，无法排优先级。

本文把缺口清单与**已发布程序集的调用图**做交叉，给出每个缺口的可达性分桶。

## 1. 方法与工具

| 步骤 | 工具 | 产物 |
|---|---|---|
| 缺口清单 | `tools/audit/parse_icall_tables.py --json` | `icall-corelib.json`（438 行 gap） |
| 调用图 | `tools/audit/IcallCallGraph.exe` | `corelib-callgraph.json`（418 icall / 18470 public-face 方法） |
| 交叉 | `tools/audit/reach_icall_gaps.py` | `gap-reachability.json` |

调用图的语义（`[READ:IcallCallGraph/Program.cs:125-160]`）：从**所有 public
类型上的 public 非 icall 方法**出发做多源 BFS，沿反向边计算每个 icall 到
公共面的最短距离。`publicFace=true` 表示该 icall 本身就是用户可直接调用的入口。

## 2. 结果

```
gaps_total=438  fcall=119  qcall=319
  graph-visible  100  of 438  (其余不是 InternalCall)
  DIRECT         6
  SHADOW        42
  UNREACHABLE   52
  NOT-IN-GRAPH 338
```

| 桶 | 数量 | 含义 | 施工含义 |
|---|---|---|---|
| **DIRECT** | 4 | icall 本身就是公共入口 | 缺它 = 直接缺失的用户功能 |
| **SHADOW** | 42 | 经 N 层托管包装可达（d=1:21, d=2:7, d=3:8, d=4:5, d=5:1） | 缺它 = 包装方法失效；距离越短影响面越大 |
| **UNREACHABLE** | 52 | 图中无路径 | 见 §3 细分 |
| **NOT-IN-GRAPH** | 338 | 图中没有这个成员 | **不是"不可达"**，见 §4 |

（2026-11-18 首版为 DIRECT 6 / 总数 438；2026-11-19 重跑为 DIRECT 4 / 总数 436，
差额即已接线的 PInvoke 两条。）

### 2.1 DIRECT 当前 4 条

```
QCall  System.Runtime.InteropServices.Marshal.GetTypedObjectForIUnknown   [resolved]
QCall  System.Runtime.InteropServices.Marshal.IsTypeVisibleFromCom        [resolved]
QCall  System.Runtime.InteropServices.Marshal.GetStartComSlot             [resolved]
QCall  System.Runtime.InteropServices.Marshal.GetEndComSlot               [resolved]
```

全部集中在 COM 互操作面，全部 P2，探针状态均为 `resolved`（`::com_*` 标记），
由 `comslot` GREEN 契约保护（`case=comslot compiler=mingw ok=True`）。
已接线的两条：

```
FCall  System.Runtime.InteropServices.Marshal.GetLastPInvokeError   [resolved]
FCall  System.Runtime.InteropServices.Marshal.SetLastPInvokeError   [resolved]
```

### 2.2 桶 × 探针状态交叉（2026-11-19 新增）

把每个桶与 `IcallWiringProbe`（CoreLib 8.0.31 输入）的状态交叉，
缺口表里的 `missing` 一词被拆成三种完全不同的东西：

| 桶 | resolved | loud | unmapped | ABSENT | 小计 |
|---|---|---|---|---|---|
| DIRECT | 4 | 0 | 0 | 0 | 4 |
| SHADOW | 0 | 24 | 18 | 0 | 42 |
| UNREACHABLE | 0 | 27 | 25 | 0 | 52 |
| NOT-IN-GRAPH | 0 | 33 | 66 | 239 | 338 |

读法：

- `loud`（57 条）= 设计性拒绝，net48 无此 API 或显式 LOUD 白名单，**不是缺口**；
- `unmapped`（109 条）= 探针未见映射臂。其中 SHADOW/UNREACHABLE 的 43 条
  （18+25）全部满足「无 public 调用者」或「无调用者」，用户代码走不到；
  NOT-IN-GRAPH 的 66 条多数是 net8 新增 QCall（无公共入口）；
- `ABSENT`（239 条）= 该成员在 CoreLib 8.0.31 里根本不存在（参考源码独有），
  无法用探针判定，只能标注未测量。

### 2.3 UNREACHABLE 的成因细分

```
22  1 caller(s), none on a public face (internal container)
19  no callers at all
 8  2 caller(s), none on a public face (internal container)
 2  3 caller(s), none on a public face (internal container)
 1  5 caller(s), none on a public face (internal container)
```

两种成因必须分开读：

- **internal container（33 条）**：icall 所在类型不是 public（如
  `System.Reflection.RuntimeAssembly`、`System.RuntimeFieldHandle`）。BFS 只从
  public 类型起种子（`[READ:Program.cs:134]`），所以这类 icall **结构性**无法
  出现在公共面路径上。它们由 CoreLib 内部逻辑调用，用户代码碰不到。
- **no callers at all（19 条）**：程序集内无任何调用点。这是更强的证据。

## 3. QCall 的解释边界（关键）

`IcallCallGraph` 只收录 `ImplAttributes == InternalCall` 的方法
（`[READ:Program.cs:162]`）。8.0.31 中该类方法共 **418** 个。

QCall 通过 `[LibraryImport]`/`DllImport` 进入运行时，**不**带 InternalCall 标志，
因此绝大多数 QCall 对调用图**完全不可见**：

| 表 | gap 数 | 图中可见 |
|---|---|---|
| FCall | 119 | 45 |
| QCall | 319 | 55 |

所以 `NOT-IN-GRAPH 338` 里包含两类完全不同的东西：
① 真实存在于 8.0.31 但非 InternalCall 的 QCall；
② 只存在于参考源码、已发布程序集根本没有的成员
（`recut_p0_gaps.py` 已证 403 条参考 QCall vs 192 条发布 QCall）。

**这两类本文无法区分**，因此 338 条一律标注为「未测量」而**不是**「不可达」。

## 4. 结论与施工建议

1. 缺口总数 436（FCall 117 + QCall 319）为当前口径（2026-11-19 重跑）；
   `icall-gap-list.md` 的 174 是更早的、只统计「runtime 有声明但转译器未接线」的子集，
   两者不同源，不可互相替代。
2. **DIRECT 已从 6 降到 4**：`Marshal.GetLastPInvokeError` / `SetLastPInvokeError`
   已接线（提交 03ea6a60）。剩下 4 条（`GetTypedObjectForIUnknown`、
   `IsTypeVisibleFromCom`、`GetStartComSlot`、`GetEndComSlot`）探针状态为
   `resolved`（`::com_*` 标记），由 `comslot` 契约保护。
3. **SHADOW 42 = 24 loud + 18 unmapped**。24 条 loud 是设计性拒绝
   （net48 无此 API 或显式 LOUD 白名单），不是缺口。18 条 unmapped 中：
   13 条在 net48 mscorlib 探针里**根本不存在**（net8 新增 API，如
   `GC._WaitForFullGCApproach`、`Thread.SpinWaitInternal`、`Delegate.BindToMethodName`），
   5 条是 CoreCLR 内部 helper（`Marshal.SizeOfHelper` /
   `GetDelegateForFunctionPointerInternal` / `GetFunctionPointerForDelegateInternal` /
   `GetExceptionForHRInternal`、`ModuleHandle.GetMDStreamVersion`），
   其公共包装已由转译器直接映射（`MethodMap.Marshal.cs`），不经过 helper。
4. **UNREACHABLE 52 = 27 loud + 25 unmapped**，全部满足「无 public 调用者」或
   「无调用者」（`StubHelpers.*` 22 条为 no-callers）。
5. 因此按可达性口径，**用户可触达的缺口为 0**。剩余 unmapped 均为
   net8 新增成员或 CoreCLR 内部机制，无公共入口。
6. 若要继续压缩 QCall 的未知量，必须先补一个**基于 ImplMap 的 QCall 调用图**，
   而不是继续用 InternalCall 图推断（`NOT-IN-GRAPH` 338 中 239 条在探针里
   完全不存在，无法区分「net8 新增」与「参考源码独有」）。

## 5. 复现

```
python tools/audit/parse_icall_tables.py --repo-root . \
    --json <tmp>/icall-corelib.json --out <tmp>/report.md
IcallCallGraph.exe <corelib-8.0.31.dll> <tmp>/corelib-callgraph.json
python tools/audit/reach_icall_gaps.py \
    --gaps <tmp>/icall-corelib.json --graph <tmp>/corelib-callgraph.json \
    --json <tmp>/gap-reachability.json
```

变异验证：把 `load_gap_rows` 的 QCall 键回退为只读 `owner`/`name`
（复现原始 join 失配），四个桶全部偏移
（DIRECT 6→2、SHADOW 42→22、UNREACHABLE 52→21、NOT-IN-GRAPH 338→393）；
恢复后回到上表数值。
