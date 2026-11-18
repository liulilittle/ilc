# CoreLib 已发布程序集 icall 重切（8.0.31，2026-11-18）

> 本文回答 `icall-gap-reachability.md` §5 留下的问题：
> 缺口清单基于 CoreCLR **主分支**参考源码，其中多少在**已发布程序集**里真实存在，
> 且**用户代码能调用**？全部数字由本机脚本产出。

## 0. 与前一版的关键差别

`icall-gap-reachability.md` 的 `NOT-IN-GRAPH 338` 无法区分两件事：
① 参考源码有、已发布程序集也有但非 InternalCall 的成员；
② 参考源码有、已发布程序集**根本没有**的成员（`recut_p0_gaps.py` 已证
403 条参考 QCall vs 192 条发布 QCall）。

本文直接从**已发布 DLL 的元数据表**出发，不再经过参考源码。因此
「声明数」是目标程序集的事实，而不是参考分支的期望。

## 1. 数据来源

| 数据 | 工具 | 结果 |
|---|---|---|
| 已发布 FCall 表 | `MetadataIcallCounter.exe` | **418** 条（带 `typeAccess/methodAccess`） |
| 已发布 QCall 表 | `QCallProbe.exe` | **192** 条（带访问修饰符） |
| 真实接线判定 | `IcallWiringProbe.exe` | 418 icall：`resolved=148 / loud=96 / unmapped=174` |
| 交叉 | `tools/audit/recut_shipped_icalls.py` | 本文表格 |

`IcallWiringProbe` 不是正则猜测：它加载转译器程序集并直接调用
`MethodRefResolver.BclResolve`（`[READ:IcallWiringProbe/Program.cs:1-12]`），
所以「resolved / loud / unmapped」就是发射路径自己的答案。

> **为什么不用参考表的 `wired` 计数**：参考表只覆盖 29 个类，
> `System.Object.GetType`、`Monitor.Enter`、`Interlocked.CompareExchange`
> 都实现了却不在表里。若按参考表判定，它们会被误报为缺口 ——
> 实测该误报规模：`USER-CALLABLE` 从 2 虚增到 69。

## 2. 可达性判定的两级筛

用户代码能命名一个成员，需要**同时**满足：

1. 声明类型 `public`（`typeAccess == public`）
2. 成员本身可见（`methodAccess ∈ {public, family, familyorassembly}`）

只判类型一项会大幅夸大缺口：在公开类型上的未接线成员里，
`private` 与 `internal/assembly` 合计占多数，它们无法出现在用户源码中。

## 3. 结果（8.0.31）

```
FCall: declared=418  unwired=174
  USER-CALLABLE    2   (public 类型 + public/protected 成员)
  INNER-ONLY      67   (public 类型，成员 private/internal)
  INTERNAL-TYPE  105   (声明类型本身不是 public)

QCall: declared=192  unwired=0  UNMEASURED=192
```

### 3.1 用户可调用的缺口：**仅 2 条**

```
System.Runtime.InteropServices.Marshal.GetLastPInvokeError   System.Int32 ()
System.Runtime.InteropServices.Marshal.SetLastPInvokeError   System.Void (System.Int32)
```

两条在 `IcallWiringProbe` 中均为 `unmapped`（交叉核对一致）。

### 3.2 QCall 为什么是 UNMEASURED 而不是缺口

`IcallWiringProbe` 枚举的是 `MethodImplAttributes.InternalCall` 方法；
QCall 经 `[LibraryImport]`/`DllImport` 进入运行时，**不带**该标志，
因此探针对 192 条 QCall 一条都看不见。

把「探针没看见」写成「未接线」正是本文要消除的那类错误 —— 上一级版本
正是这样把 319 条 QCall 误报成缺口。故此处标注为**未测量**。

要真正测量 QCall 的接线状态，需要一个基于 ImplMap 的接线探针，
而不是复用 InternalCall 探针。

## 4. 结论

1. 目标程序集 8.0.31 共声明 **418 FCall + 192 QCall = 610** 个 icall 入口。
2. 其中 **174** 条 FCall 未被转译器解析（`unmapped`）。
3. 但**用户代码可调用的未接线缺口只有 2 条**，均在 `Marshal` 的
   PInvoke 错误码查询面。
4. 其余 172 条（67 INNER-ONLY + 105 INTERNAL-TYPE）不是用户可命名的 API，
   修它们不会改变任何用户程序的可见行为。
5. QCall 的接线状态**未测量**，这是本文留下的明确缺口：
   需要 ImplMap 维度的探针。

## 5. 复现

```
MetadataIcallCounter.exe <corelib-8.0.31.dll>   <tmp>/fcall-8031.tsv
QCallProbe.exe            <corelib-8.0.31.dll>   <tmp>/qcall-8031.tsv
IcallWiringProbe.exe      <corelib-8.0.31.dll>   <tmp>/wiring-8031.tsv
python tools/audit/recut_shipped_icalls.py \
    --fcall-tsv <tmp>/fcall-8031.tsv --qcall-tsv <tmp>/qcall-8031.tsv \
    --wiring <tmp>/wiring-8031.tsv --json <tmp>/shipped-recut.json
```

### 变异验证

| 变异体 | 改动 | 结果 |
|---|---|---|
| A | 可达性只判类型可见性（忽略成员可见性） | `USER-CALLABLE 2 → 69`（夸大 34.5×） |
| B | 取消 QCall 的未测量保护 | `QCall unwired 0 → 192`（重新引入虚假缺口） |

两者恢复后回到 §3 数值，无残留。

## 6. 后续：两条 USER-CALLABLE 已接线（轮 34）

§4 结论已过时。两条缺口现已接线，重跑探针后 `USER-CALLABLE (live)` **2 → 0**：

| 成员 | 接线到 | 语义 |
|---|---|---|
| `Marshal.GetLastPInvokeError` | `il2cpp::il2cpp_pinvoke_get_last_error` | 读每线程捕获槽（与 `GetLastWin32Error` 同一槽） |
| `Marshal.SetLastPInvokeError` | `il2cpp::il2cpp_pinvoke_set_last_error`（新增） | 写捕获槽，**非** OS 错误码 |

`SetLastPInvokeError` 不能复用 `il2cpp_marshal_set_last_win32_error`：后者写的是
OS 错误（`SetLastError`/`errno`，`marshalext2.cpp:135`），而 net8 的 getter 读的是
**捕获**的值——写 OS 错误会写入 getter 永远看不到的位置，因此两者必须分开。
语义依据：net8 `Marshal.cs` 中 `GetLastWin32Error()` 直接 `return GetLastPInvokeError();`。

验证：`tests/cases/probe-pinvoke-error/`（net8.0，5 断言）与 net8 oracle 逐行一致；
变异测试删除 `SetLastPInvokeError` 臂 → 构建失败（未声明符号，RED），
恢复后 GREEN。残余 67 INNER-ONLY + 105 INTERNAL-TYPE 仍不是用户可命名 API。

## 7. QCall 表已测量（轮 36）

§5 把 QCall 的 192 条记为 **UNMEASURED**，理由是探针只枚举
`MethodImplAttributes.InternalCall`而 QCall 经 `[LibraryImport]`/`DllImport` 进入。
轮 36 给 `IcallWiringProbe` 加了第三种 kind：

```
isQcall = (m.Attributes & PInvokeImpl) != 0 && ImplMap.Module 的名字 == "QCall"
```

它与 `icall` / `publicapi` 一样走 `MethodRefResolver.BclResolve`，因此结果就是
发射路径自己的答案。量测结果：

| 状态 | 数量 | 含义 |
|---|---|---|
| resolved | 19 | 已映射到具体 `icall_*` 符号 |
| loud | 44 | 被明确拒绝（不会静默产生未定义符号） |
| unmapped | 129 | 无映射臂 |
| 合计 | **192** | 与 QCallProbe 的声明数一致 |

可达性分档（同 §3 的两级筛）：**USER-CALLABLE 0** / INNER-ONLY 45 / INTERNAL-TYPE 84。
因此 QCall 也没有用户可命名的未接线缺口。

### 变异验证

把 `load_wiring` 的 kind 过滤改回 `kind != "icall"`（即恢复轮 168 的旧口径），
QCall 立即从 `unwired=129` 变为 **`unwired=192`**，也就是把“没看见”重新算成“未接线”。
恢复后回到 129。这是本文唯一一处可靠的 QCall 结论来源。

### 探针口径的局限（必须知道）

探针把 CoreLib 本身当作 `MetadataReader`，因此
`IsExternalBclOwner` 对它恒为 false，phantom 守卫（`MethodRefResolver.cs:1603`）
**不会**触发。于是探针报的 `unmapped` 只意味着“没有映射臂”，
**不等于**“真实转译会静默产生未定义符号”。真实转译里 BCL 是外部程序集，
守卫会甩 LOUD。本轮正是靠这个差异发现了
`Marshal.GetTypeFromCLSID` 的真实缺陷（见 §8）。
