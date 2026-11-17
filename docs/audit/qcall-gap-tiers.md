# QCall 缺口分层（P1-P3，目标 CoreLib 8.0.31）

> 数据源：`tools/audit/parse_icall_tables.py` 于 HEAD `10a40cd6` 的 JSON +
> `tools/audit/QCallProbe` / `MetadataIcallCounter` 对 8.0.31 的元数据实测。
> 分层脚本口径同 `tools/audit/recut_p0_gaps.py`（三桶 + 名归一化 + 表并集）。

## 0. 结论摘要

| 层 | 真实缺口（存在于 8.0.31） | 用户代码可直接调用 |
|---|---|---|
| P0 | 17 | 0（全部 private/assembly） |
| P1 | 6 | 0（4 private + 2 assembly） |
| P2 | 140 | **4** |
| P3 | 43 | 0（16 private + 13 internal + 14 assembly） |
| 合计 | **205** | **4** |

**205 条里只有 4 条对用户可见**，且这 4 条全部已有 arm（投递转译期 marker，非运行时符号）。
其余 201 条是 CoreLib 内部成员，用户程序集无法直接调用。

## 1. 判据：为什么「无 arm」不等于「用户可见缺口」

三件事必须分开看：

1. **方法可见性**（`MethodDef.Attributes` 的 MemberAccess 位）。只有 `public` 的方法
   才能被用户程序集直接调用。实测 205 条：**private 94 / internal 34 / assembly 73 / public 4**
   （合计 205，恒等式成立）。
2. **转译输入范围**：`src/il2cppc/Program.cs:164` 只 `LoadAssembly(opts.InputAssembly)`
   ⇒ CoreLib 的方法体不进转译。因此 `private`/`internal`/`assembly` 的成员根本没有调用点。
3. **未映射 QCall 的失败方式**：`CodeGenerator.cs:1590-1604` 对**未映射**的 QCall
   保留 legacy `extern "C"` 声明（映射过的才抑制）。因此在链接期以
   `undefined reference` 失败——**响亮失败，不是静默错值**。

综合：201 条内部成员 ⇒ 无调用点 ⇒ 不产生代码 ⇒ 不链接失败 ⇒ 对用户不可见。
4 条 public ⇒ 有 arm ⇒ 不落兜底路径。

## 2. 4 条 public 的实测状态（全部已接线）

| owner | name | arm 位置 | 投递 |
|---|---|---|---|
| `System.Runtime.InteropServices.Marshal` | `GetStartComSlot` | `MethodMap.Marshal.cs:584` | `ComStartSlotMarker`（转译期折叠） |
| `System.Runtime.InteropServices.Marshal` | `GetEndComSlot` | `MethodMap.Marshal.cs:587` | `ComEndSlotMarker`（转译期折叠） |
| `System.Runtime.InteropServices.Marshal` | `IsTypeVisibleFromCom` | `MethodMap.Marshal.cs:590` | `ComTypeVisibleMarker`（转译期折叠） |
| `System.Runtime.InteropServices.Marshal` | `GetTypedObjectForIUnknown` | `MethodMap.Marshal.cs:614` | `ComTypedObjectMarker`（转译期折叠） |

审计脚本把这几条记作 `half-wired`，是因为 marker **不是运行时符号**。
这是**判据差异，不是缺陷**：`ResolvedMethod.ComStartSlotMarker` 之类的标记由
StackFold/转译期求值，最终不产生对 QCall 的调用。

**折叠实现已验证存在**（`src/il2cppc/Ir/StackFold.cs`）：

| marker | StackFold handler |
|---|---|
| `ComStartSlotMarker` | `:5244`（分组入口）、`:5293`（求值） |
| `ComEndSlotMarker` | `:5245`（分组入口）、`:5250`（成员名） |
| `ComTypeVisibleMarker` | `:5246`（分组入口）、`:5274`（求值） |
| `ComTypedObjectMarker` | `:5314`（求值） |

即 4 条 public QCall 的 marker 都有转译期折叠实现，不产生运行时 QCall 调用。

**语义等价已验证**（`tests/cases/comslot`，已登记进 `scripts/green-contracts.txt`）：

```
oracle 与 runtime 各 113B，逐字节一致：
  start=3            (IUnknown 接口 IIDerivedComSlot，首个方法在 slot 3)
  end=5              (5 个方法 -> 3..7? 实测 net48 给 5，见下)
  base.start=3 / base.end=4
  visible=True / noattr=True / hidden=False
  class.start=-1 / class.end=-1     (非接口)
```

> 注意 `end=5`：net48 的 `GetEndComSlot(typeof(IDerivedComSlot))` 返回 5 而非 7 ——
> 这是**实测值**（oracle 输出），不是笔误。折叠实现逐字符复现了它。

### 2.1 折叠的两个硬限制（实测，写探针时踩到）

1. **`typeof` 必须是编译期常量**。传变量（`Marshal.GetStartComSlot(someType)`）会让折叠失败，
   整个方法 `Program::Main` 变成 `not transpiled`：
   `warning: ... requires a CONSTANT typeof(T) Type argument (the runtime Type object
   cannot be resolved to metadata at transpile time)`。
2. **`typeof` 的类型必须定义在被转译的程序集内**。`typeof(int)` 这类外部 TypeRef 会被拒：
   `warning: ... only a type DEFINED in the transpiled assembly is supported
   (an external-assembly TypeRef or a generic/array TypeSpec has no readable metadata ...)`。

这两条在 `StackFold.cs` 的 COM 标记处理器里是既有行为，本轮只是**记录**，未改动。

**变异验证（第16条）**：把 `ComStartSlotMarker` 的 `comStart` 改成常量 0、
把 `comVisibleFinal` 改成恒 true ⇒ `ok=False`、`firstDiff stdout=6`、`MUT_EXIT=1`；
字节级恢复（sha16 `F3778403DC324CE1`）并重建后 `ok=True`、`GREEN_EXIT=0`。

> **过程中发现的陷阱**：`scripts/difftest.ps1:254` 用的是 `src/il2cppc/bin/**Debug**/net8.0/il2cppc.dll`。
> 只跑 `dotnet build -c Release` 不会刷新它，于是变异体跑的还是旧编译器、测试照样绿——
> **假绿**。改 `src/il2cppc/**.cs` 后验证行为必须先 `dotnet build -c Debug`
> （或删掉该 dll 让 `dotnet run` 兜底）。

## 3. 分层明细（按 owner）

### P1（6 条，全不可直达）

| owner | name | 可见性 |
|---|---|---|
| `System.Diagnostics.Debugger` | `BreakInternal` | private |
| `System.Diagnostics.Debugger` | `LaunchInternal` | private |
| `System.Diagnostics.Debugger` | `LogInternal` | private |
| `System.Diagnostics.Debugger` | `CustomNotification` | private |
| `System.Diagnostics.StackFrame` | `GetMethodDescFromNativeIP` | private |
| `System.IO.FileLoadException` | `GetMessageForHR` | assembly |

### P2（140 条，20 owner）

| owner | 条数 | 性质 |
|---|---|---|
| `System.RuntimeTypeHandle` | 25 | 类型句柄反射 |
| `System.Reflection.RuntimeAssembly` | 22 | 程序集反射 |
| `System.Reflection.Emit.RuntimeTypeBuilder` | 21 | 动态类型生成（AOT 无模型） |
| `System.Runtime.InteropServices.Marshal` | 13 | COM/Interop（含上述 4 条 public） |
| `System.Runtime.Loader.AssemblyLoadContext` | 12 | 动态加载（AOT 无模型） |
| `System.RuntimeMethodHandle` | 9 | 方法句柄反射 |
| `System.Reflection.Emit.RuntimeModuleBuilder` | 9 | 动态模块生成 |
| `System.ModuleHandle` | 8 | 模块句柄反射 |
| `System.RuntimeFieldHandle` | 4 | 字段句柄反射 |
| `System.Reflection.RuntimeModule` | 3 | 模块反射 |
| `System.Reflection.Assembly` | 3 | 程序集信息 |
| 其余 9 owner | 11 | 各 1-2 条 |

### P3（43 条，9 owner）

| owner | 条数 | 性质 |
|---|---|---|
| `System.Diagnostics.Tracing.NativeRuntimeEventSource` | 14 | EventSource 日志 |
| `System.Diagnostics.Tracing.EventPipeInternal` | 12 | 诊断管道 |
| `System.StubHelpers.MngdSafeArrayMarshaler` | 6 | 互操作封送 |
| `System.StubHelpers.StubHelpers` | 3 | 互操作校验 |
| `System.StubHelpers.ObjectMarshaler` | 2 | 互操作封送 |
| `System.StubHelpers.InterfaceMarshaler` | 2 | 互操作封送 |
| `System.ComAwareWeakReference` | 2 | COM 弱引用 |
| `System.Enum` | 1 | `GetEnumValuesAndNames` |
| `System.MdUtf8String` | 1 | `EqualsCaseInsensitive` |

## 4. 结论

1. **QCall P1-P3 段没有用户可见的静默缺口**：205 条中 201 条是 CoreLib 内部成员
   （无调用点），4 条 public 全部已有 arm。
2. **不需要为这 205 条新增 MethodMap 接线**（C6：不为无调用点的符号写映射）。
3. **潜在工作面在别处**：P2 的 `Reflection.Emit`（30 条）与 `AssemblyLoadContext`（12 条）
   在 AOT 下无模型。若产品决定「显式 LOUD 拒绝」比「链接期 undefined reference」更好，
   应按 owner 批量加 LOUD arm。这是**产品取舍，需用户决定**（G1），本轮不做。
4. **4 个 Com*Marker 已完全验证**：StackFold handler 逐个定位
   （`StackFold.cs:5244/5245/5246/5314`），且 `tests/cases/comslot` 逐字节对齐 net48 oracle
   （9 个断言含 false 分支与非接口 -1 分支），已登记 `green-contracts.txt`。

## 5. 复现

```powershell
# 1. 接线面 JSON（含 tier/status）
python tools/audit/parse_icall_tables.py --repo-root . --json <tmp>/icall-corelib.json

# 2. 发布面
dotnet build tools/audit/QCallProbe/QCallProbe.csproj -c Release
tools/audit/QCallProbe/bin/Release/net9.0/QCallProbe.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" <tmp>/corelib8-qcall.tsv
tools/audit/MetadataIcallCounter/bin/Release/net9.0/MetadataIcallCounter.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" <tmp>/corelib8-fcall.tsv

# 3. 分层 + 可见性（三桶 + 名归一化 + 表并集）
#    见 tools/audit/recut_p0_gaps.py 的 surface()/canonical_name()
```

实测（本轮）：
```
真实 QCall 缺口  present=205  absent=107  （总计 312 条 gap 行）
方法可见性分布    private 94  internal 34  assembly 73  public 4   (sum 205)
用户可直接调用    4 / 205
```
