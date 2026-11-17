# QCall 口径定案（P1-B）

> 目标程序集：`C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll`
> （13,174,608 B）。测量日期 2026-11-17，基线 HEAD `71f5a656`。

## 1. 问题的由来

历史审计出现三个互不相同的 QCall 数字：**21/32**、**36/44**、**0/0**。此前定性为
「三种匹配键 ⇒ 非三个事实」。本轮用两把独立的尺子把口径钉死。

## 2. 两把尺子的分工

| 尺子 | 来源 | 语义 |
|---|---|---|
| 参考源码 | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp` 的 `s_QCall[]` | **CoreCLR main 分支**声明了哪些 QCall |
| 发布程序集 | `tools/audit/QCallProbe`（读 `ImplMap.Module` == `QCall`） | **已发布 CoreLib** 实际发出哪些 QCall |

两者不是同一集合，且差得很多。

### 2.1 关键陷阱：方法名带 source-generator 后缀

.NET 7 起 `[LibraryImport]` 由源生成器展开，发布程序集里的方法名是
`<RealName>g____PInvoke|N_0`，**真正的 QCall 入口名在 `ImplMap.Entry`**：

```
System.GC | <_StartNoGCRegion>g____PInvoke|3_0 | GCInterface_StartNoGCRegion | public/assembly/static
System.Runtime.CompilerServices.RuntimeHelpers | <AreTypesEquivalent>g____PInvoke|34_0 | MethodTable_AreTypesEquivalent | public/assembly/static
```

实测 **192 条 QCall 中有 64 条（33%）**是这个形状。按 `(declaringType, methodName)` 直接求交
会把它们全部漏掉。

**口径定案：求交前必须先把 `<X>g____PInvoke|N_M` 归一化为 `X`**，否则结论系统性偏低。

## 3. 实测数字

```
reference-source QCall unique keys = 401 (403 rows, 2 dup keys)
  shipped as QCall (canonical name) = 170
  shipped as FCall                  =  67   (table migrated)
  in union (QCall|FCall)            = 237
  neither                           = 164

reference rows PRESENT in shipped union = 237 / 403 (59%)
```

- 参考源码 **403** 条（`parse_icall_tables.py` 口径）。
- 8.0.31 的 QCall ImplMap **192** 条（`QCallProbe` 直读元数据）。
- 按归一化名求交后命中 **237 条（59%）**；其中 170 仍走 QCall 表、67 已迁到 FCall 表。
- 参考源码 403 − 237 = **164 条（41%）在 8.0.31 根本不存在**。

**⇒ 164/403 的「QCall 缺口」是参考源码口径的虚数。** 按 403 条做覆盖率分母会系统性低估现状。

### 3.1 若不做名归一化（错误口径，仅作反例留存）

```
present in shipped 8.0.31 surface: 180 (45%)   <- 漏掉 57 条生成器方法
```

这 180 是**错的**，比正确值 237 低 57 条。本轮首次计算即踩此坑。

## 4. 已存在项的状态分布（这才是有效分母）

| 状态 | 条数 | 占比（/237） |
|---|---|---|
| wired | 32 | 13.5% |
| half-wired | 9 | 3.8% |
| stub | 1 | 0.4% |
| missing | **195** | **82.3%** |

对比全量口径（/403）会得到 wired 47 / missing 301 的假低分（11.7% / 74.7%），
两者结论方向一致但幅度差 1.4 倍。

## 5. P0 段（可达性最高）的现状

P0 QCall gap 42 条中，**12 条**在发布程序集存在（归一化后口径，见 `corelib-p0-recut.md` §3.1）：

- 5 条显式 LOUD throw
- 5 条由公共面替代
- **2 条未接线**（`GC._StartNoGCRegion` / `RuntimeHelpers.AreTypesEquivalent`），
  落 `default: return null` 幻影路径；两者均为 `public/assembly`，用户程序集不可直接调用

其余 30 条落空。

**⇒ P0 段无用户可见的静默缺口**，但有 2 条**内部幻影路径**待补显式 LOUD
（见 `corelib-p0-recut.md` §3.1.2，本轮只做定性，未改生产代码）。

## 6. 非 P0 的真实缺口（139 条 missing 的分布）

按 owner 聚类（前 15）：

| owner | 条数 | 性质 |
|---|---|---|
| `System.RuntimeTypeHandle` | 24 | 反射类型句柄面 |
| `System.Reflection.RuntimeAssembly` | 13 | 程序集反射 |
| `System.Diagnostics.Tracing.NativeRuntimeEventSource` | 13 | EventPipe/ETW 源 |
| `System.Reflection.Emit.RuntimeTypeBuilder` | 9 | 动态类型生成（AOT 不可支持） |
| `System.RuntimeMethodHandle` | 8 | 方法句柄反射 |
| `System.ModuleHandle` | 8 | 模块句柄反射 |
| `System.Runtime.InteropServices.Marshal` | 7 | COM/Interop |
| `System.StubHelpers.MngdSafeArrayMarshaler` | 6 | 互操作封送 |
| `System.Diagnostics.Tracing.EventPipeInternal` | 5 | 诊断管道 |
| `System.RuntimeFieldHandle` | 4 | 字段句柄反射 |
| `System.Runtime.Loader.AssemblyLoadContext` | 4 | 动态加载 |
| `System.Reflection.RuntimeModule` | 3 | 模块反射 |
| `System.Reflection.Emit.RuntimeModuleBuilder` | 3 | 动态模块生成 |
| `System.Reflection.Assembly` | 3 | 程序集信息 |
| `System.StubHelpers.StubHelpers` | 3 | 互操作校验 |

这些是 **P1-P2 面的真实缺口**，不属本轮 P0 范围。其中 `Reflection.Emit`（12 条）与
`AssemblyLoadContext`（4 条）在 AOT 模型下没有对应物，应按「显式 LOUD」处置而非实现。

## 7. 结论

1. **QCall 覆盖率必须用「发布程序集 ∩ 参考源码 = 180」作分母**，不能用 403。
2. **P0 段无静默缺口**（8/8 已封堵）。
3. P1-P2 段真实缺口集中在反射/互操作/诊断三类（139 条），是下一阶段的工作面。
4. `Reflection.Emit` / `AssemblyLoadContext` 类缺口应按 LOUD 白名单处置（AOT 无对应模型）。

## 8. 复现

```powershell
dotnet build tools/audit/QCallProbe/QCallProbe.csproj -c Release
tools/audit/QCallProbe/bin/Release/net9.0/QCallProbe.exe `
  "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.31\System.Private.CoreLib.dll" `
  <tmp>/corelib8-qcall.tsv
# 与 parse_icall_tables.py 的 qcall_rows 按 (decl_owner, decl_name) 求交
```
