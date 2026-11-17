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

### 2.2 `Thread.SetApartmentState` 公共面无 arm（**已修复**）

转译直接失败（整个方法 `not transpiled`）：

```
warning: method fold failed -> skipped (System.Threading.Thread::SetApartmentState:
  not wired in M5 (BCL owner has no mapping arm ...))
```

`MethodMap.Thread.cs` 原来只有 `TrySetApartmentState`，没有 `SetApartmentState`。
两者失败通道不同，必须分开：

**实测的 net48 契约**（`tests/cases/probe-apartment-fail`，逐字节对比）：

| 情形 | `TrySetApartmentState` | `SetApartmentState` |
|---|---|---|
| 已启动线程 | **抛 `ThreadStateException`** | 抛 `ThreadStateException` |
| 已终止线程 | （仅 `Set` 可测，同样抛） | 抛 `ThreadStateException` |
| 未启动 + 合法值 | 接受 / 拒绝 | 成功 / 抛 |
| 未启动 + 枚举外值 | 抛 `ArgumentOutOfRangeException` | 抛 `ArgumentOutOfRangeException` |

**关键纠正**：`Try*` 前缀容易让人以为它在失败时返回 `false`。实测**不是**——
已启动线程上它照样抛 `ThreadStateException`，枚举外值照样抛
`ArgumentOutOfRangeException`。它与 `SetApartmentState` 的唯一差别是
「良构但被拒绝的请求」返回 `false` 而不是抛异常。本运行时初版按「返回 false」
实现，被探针判红后改正。

修法：新增运行时入口 `icall_threadobj_set_apartment_state_public`（void，抛），
与 `..._native`（bool，仅对良构拒绝返回 false）并存；`MethodMap.Thread.cs` 的
`SetApartmentState` arm 指向新入口。

### 2.3 `ApartmentState` 枚举未生成到 TU（**已修复**）

即使调用 `GetApartmentState()`（有 arm），打印其返回值也编译失败：

```
error: 'System_Threading_ApartmentState' was not declared in this scope
```

根因：`CodeGenerator.KnownBclEnumNames` 白名单少了
`System.Threading.ApartmentState`（该白名单注释自述「grows on demand — one entry per
case that needs it」）。加入后枚举按 int32 carrier 处理，用户可 `(int)` 转换使用。

### 2.4 `GetApartmentState` 返回值语义错误（**已修复**）

修好 2.2/2.3 后探针立即暴露第三个缺陷：

| 场景 | net48 | 修复前运行时 |
|---|---|---|
| 未启动、未配置 | `Unknown(2)` | `0` |
| 当前线程 | `MTA(1)` | `0` |
| 未启动、已 `SetApartmentState(MTA)` | `MTA(1)` | `2` |

`ApartmentState` 的真实顺序是 **STA=0 / MTA=1 / Unknown=2**，而原实现
`return 0; // MTA` 既把 STA 当 MTA，也从未记录用户选择的公寓。

修法：
- `Il2CppThreadObject` 末尾追加 `int32_t apartmentState`（非 GC 引用，
  `refFieldBitmap` 不受影响），ctor 初始化为 `2`(Unknown)
- `get`：`state != 0`（已启动/CURRENT）⇒ `1`(MTA)；否则返回已记录值
- `native`(bool)：已启动 ⇒ `false`；请求非 MTA ⇒ `false`；否则记录并返回 `true`
- `public`(void)：已启动 ⇒ `ThrowThreadState`；非 MTA ⇒ `PlatformNotSupportedException`

## 3. 探针与验收

探针：`tests/cases/probe-half-nonp0`（7 行输出）

| 阶段 | 结果 |
|---|---|
| CurrentThread 修复前 | `ok=False`，唯一差异 `thr.current_same=False`（oracle `True`） |
| CurrentThread 修复后 | `ok=True`，169B 逐字节一致 |
| apartment 修复前 | `ok=False`，`thr.apartment=0` vs oracle `2`；`thr.apartment_after` 亦不同 |
| apartment 修复后 | `ok=True`，**264B 逐字节一致**，`C0_EXIT=0` |
| 变异（两处判定同时改坏） | `ok=False`、`MUT_EXIT=1` |
| 恢复 | 无 `MUTANT` 残留，`ok=True`、`GREEN_EXIT=0` |

修复后实测输出（oracle == runtime，264B）：
```
serv.is_server=False
ac.overrides_null=True
ac.getvalue_null=True
ac.tryswitch_found=False
ac.tryswitch_value=False
thr.apartment_unstarted=2
thr.apartment_current=1
thr.setapa_ok=True
thr.apartment_after=1
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

- 10 条内部 icall 无公共路径，**未验证**（也不构成用户可见缺口）。
- 子线程的 `CurrentThread` 单例性由 `threadcase`/`threadcase2`/`diffthread001` 间接覆盖，
  **未写**专门的子线程 `ReferenceEquals` 断言（**未验证**）。
- **STA 接受路径未验证**：net48 在本机（无 STA 消息泵）**接受**未启动线程的 STA 请求
  （`TrySetApartmentState(STA)` 返回 `True`，随后 `GetApartmentState()` 为 `STA(0)`），
  但接受之后该进程**再也无法格式化异常**（stderr: "由于 Exception.ToString() 失败，
  因此无法打印异常字符串"），oracle 因此不可信。本运行时对 STA 请求返回 `false` /
  抛 `PlatformNotSupportedException`。**这是一处已知的行为差异**，不是已对齐项。

### 4.1 新发现：catch 变量跨方法边界后 `GetType()` 退化为 `System.String`

与 apartment 无关，但探针过程中实证：

```
idx.inline=System.IndexOutOfRangeException     <- 正确
idx.param =System.String                        <- 错误
user.inline=System.InvalidOperationException    <- 正确
user.param =System.String                       <- 错误
apa.inline=System.Threading.ThreadStateException<- 正确
apa.param =System.String                        <- 错误
```

`[READ:src/il2cppc/Ir/StackFold.cs:328-335]` 记载 catch handler 压入的是
`ExceptionEntryExpr`——**异常短类型名的一个 interned 字符串**，不是异常对象：
「Push a synthetic entry (ExceptionEntryExpr -- the exception's SHORT type name as
an interned string)」。因此：

- 内联 `ex.GetType()` 命中的是 `MethodMap.Exception.cs:118` 的
  `receiverType == "System.Type"` identity arm，看起来正确
- 一旦把 catch 变量作为参数传给形参类型为 `System.Exception` 的方法，
  `receiverType` 变成 `System.Exception`，走 `:126` 的
  `icall_object_get_type`，读到的真实描述符是 `System.String`

影响面：**所有把 catch 变量传给辅助方法的代码**（常见写法，如统一的
`Log(Exception ex)`）。这不是 apartment 的问题，是 M5 异常 carrier 的表达力上限。
**未修复**，需单独立项（要么让 catch 变量成为真对象，要么让形参传递保留
「它是类型名载体」这一信息）。

## 5. 复现

```powershell
dotnet build src/il2cppc/il2cppc.csproj -c Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/difftest.ps1 -Case probe-half-nonp0 -Compiler mingw
# 期望 ok=True（169B == 169B）
```
