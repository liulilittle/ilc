# 非 P0 半接（half-wired）17 条探针与 Thread.CurrentThread 单例修复

> 承接 `plan.md` 阶段 B：「半接 27 条恒值桩语义补齐」。
> P0 的 10 条已由 `tests/cases/probe-p0c` 实证（32 记录 diff=0）；本文处理剩余 17 条。

## 1. 17 条的公共可达性

半接（`half`）= arm 存在但不投递运行时符号（空体 / 恒值桩）。是否构成用户可见缺陷，
取决于有没有**公共 API 能到达它**。

| 可达 | icall | 公共入口 |
|---|---|---|
| ✅ | `GC.IsServerGC` | `GCSettings.IsServerGC` |
| ✅ | `CompatibilitySwitch.GetAppContextOverridesInternalCall` | `AppContext.GetData` |
| ✅ | `CompatibilitySwitch.GetValueInternalCall` | `AppContext.GetData` |
| ✅ | `CompatibilitySwitch.IsEnabledInternalCall` | `AppContext.TryGetSwitch` |
| ✅ | `Thread.GetApartmentStateNative` | `Thread.GetApartmentState` |
| ✅ | `Thread.SetApartmentStateNative` | `Thread.SetApartmentState` |
| ✅ | `Thread.InternalGetCurrentThread` | `Thread.CurrentThread` |
| ❌ | `Delegate.FindMethodHandle` | CoreLib 内部 |
| ❌ | `Exception.PrepareForForeignExceptionRaise` | CoreLib 内部 |
| ❌ | `Exception.SaveStackTracesFromDeepCopy` | CoreLib 内部 |
| ❌ | `Thread.GetAbortReason` / `SetAbortReason` / `ClearAbortReason` | CoreLib 内部 |
| ❌ | `Thread.GetContextInternal` / `InternalCrossContextCallback` | CoreLib 内部 |
| ❌ | `Thread.SetAppDomainStack` / `RestoreAppDomainStack` | CoreLib 内部 |

7 条可达 / 10 条仅内部。探针 `tests/cases/probe-half-nonp0` 覆盖可达的那些里
**能编译通过的 5 个断言**（另 2 条见 §2 的阻塞）。

## 2. 探针过程中发现的三个真实缺陷

### 2.1 `Thread.CurrentThread` 不是每线程单例（**已修复**）

**症状**：`ReferenceEquals(Thread.CurrentThread, Thread.CurrentThread)` 在 net48 为 `True`，
在本运行时为 `False`。

因果链（G2）：

| 环 | 内容 |
|---|---|
| 定位 | `src/runtime/src/threadobj.cpp:682`（修前 `:675`）`icall_threadobj_get_current_thread` |
| 原因 | 每次调用 `il2cpp_object_new(&System_Threading_Thread_class)` 新建对象 |
| 原意图 | 修前注释 `:670-673` 自述「a fresh Thread object ... per call」——**原意图本身就与 net48 不符** |
| 错误假设 | 假设「每次新建也能工作」；实际 `ReferenceEquals`、`lock(Thread.CurrentThread)`、`Thread.CurrentThread.Name = x` 后再读 都依赖单例 |
| 防再犯 | 改为 `thread_local` 缓存对象；探针用 `ReferenceEquals` 断言（`thr.current_same`） |

修复要点：
- 新增 `thread_local Il2CppThreadObject* t_currentThreadObj`（`threadobj.cpp:163`）
- `icall_threadobj_get_current_thread` 首次创建、之后复用，每次仍从 TLS 刷新
  `managedId` / `tid` / `state`（TLS 字段是权威源）
- `Name`：wrapper 自己的字段优先（命名是 write-once），否则回退到 trampoline 发布的
  `t_currentThreadName`

### 2.2 `Thread.SetApartmentState` 公共面无 arm（**未修复，已登记**）

转译直接失败（整个方法 `not transpiled`）：

```
warning: method fold failed -> skipped (System.Threading.Thread::SetApartmentState:
  not wired in M5 (BCL owner has no mapping arm ...))
```

`MethodMap.Thread.cs:241` 只有 `TrySetApartmentState`，没有 `SetApartmentState`。
同一 API 家族的 `GetApartmentState`（`:237`）有 arm，`Set` 缺一条 —— 是**真实缺口**。

**为什么本轮不修**：实现它需要决定 net48 的异常语义
（`PlatformNotSupportedException` / `InvalidOperationException` / 静默成功），
这属于产品取舍而非机械补齐；且它不影响已交付的任何用例。
**待办**：单独立项。

### 2.3 `ApartmentState` 枚举未生成到 TU（**未修复，已登记**）

即使调用 `GetApartmentState()`（有 arm），打印其返回值也编译失败：

```
error: 'System_Threading_ApartmentState' was not declared in this scope
```

枚举类型没有被 emit 进生成的 TU。`GetApartmentState` 的 arm 因此**无法被任何用户代码
观测**（拿到返回值就编译不过）。这与 2.2 是同一家族（apartment 面未打通）。

## 3. 探针与验收

探针：`tests/cases/probe-half-nonp0`（7 行输出）

| 阶段 | 结果 |
|---|---|
| 修复前 | `ok=False`，`oracle 169B / runtime 170B`，唯一差异 `thr.current_same=False`（oracle `True`） |
| 修复后 | `ok=True`，两侧 **169B 逐字节一致**，`C0_EXIT=0` |
| 变异（缓存改回每次新建） | `ok=False`、`producedBytes=339`、`MUT_EXIT=1` |
| 恢复 | 无 `MUTANT` 残留，`ok=True`、`GREEN_EXIT=0` |

修复后实测输出（oracle == runtime）：
```
serv.is_server=False
ac.overrides_null=True
ac.getvalue_null=True
ac.tryswitch_found=False
ac.tryswitch_value=False
thr.current_same=True
thr.current_id_pos=True
```

回归（13 个依赖 `CurrentThread` 的既有用例，逐个 PASS）：

```
threadcase PASS      threadcase2 PASS      threadops PASS
threadops2 PASS      bgthread PASS         diffthread001 PASS
diffthread004 PASS   corelibp0 PASS        probe-p0a PASS
threadpooldeep2 PASS
```

## 4. 仍未覆盖的部分

- `thr.apartment` / `thr.setapa` 两行因 §2.2 / §2.3 无法探针，**未验证**。
- 10 条内部 icall 无公共路径，**未验证**（也不构成用户可见缺口）。
- 本轮只验证了主线程；子线程的 `CurrentThread` 单例性由 `threadcase`/`threadcase2`/
  `diffthread001` 间接覆盖，未写专门的 `ReferenceEquals` 断言（**未验证**）。

## 5. 复现

```powershell
dotnet build src/il2cppc/il2cppc.csproj -c Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/difftest.ps1 -Case probe-half-nonp0 -Compiler mingw
# 期望 ok=True（169B == 169B）
```
