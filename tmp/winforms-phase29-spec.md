# WinForms Phase 29 规格（审计子代理交付，纯研究）

- 日期：2026-08-16
- 性质：只读研究，无代码改动
- 基线：winforms1.cs Phase 1-28（372 行 byte-exact，HEAD 81d225e/6810a01）
- 已建 shim：comctl32 / gdi32 / kernel32 / user32 / shell32 / comdlg32（全部 INTERFACE 转发系统导入库）
- 目标：从 5 个候选（Timer 控件 / ProgressBar 增强 / ToolStrip 简化 / MainMenu 全量 / DataGridView）中推荐一个 Phase 29 目标，给出可实施规格

---

## 1. 候选评估表

| 候选 | 新 P/Invoke | 新 shim | struct | headless 确定性 | 转译器/runtime 缺口 | 工作量 | 风险 | 结论 |
|---|---|---|---|---|---|---|---|---|
| **a. Timer 控件** | SetTimer/KillTimer（user32，2 个标量） | 零 | 无 | **高**（SendMessageW 直投 WM_TIMER 规避真实时序） | 零 | 低 | 低 | **推荐** |
| **b. ProgressBar 增强** | 零（全 SendMessageW） | 零 | 无 | 高（全同步标量消息） | 零 | 低 | 极低 | 可选伴生 |
| **c. ToolStrip 简化** | 复用 MainMenu CreateMenu 面 | 零 | 无 | 中 | 语义偏离 | 中 | 中 | 不推荐 |
| **d. MainMenu 全量** | GetMenuState/CheckMenuItem/EnableMenuItem/GetSubMenu/GetMenuItemID（user32，5 个标量） | 零 | 无 | 高 | 零 | 中 | 低 | 次选 |
| **e. DataGridView 简化** | 无原生类（纯托管控件） | — | — | 低 | 大（大量托管网格代码） | 高 | 高 | 不推荐 |

---

## 2. 推荐 + 理由

**推荐 Phase 29 = Timer 控件（SetTimer/KillTimer + SendMessageW 直投 WM_TIMER）**

理由（按任务优先级"优先复用已建面、可 headless 确定性验证"逐条）：
1. **复用已建面**：SetTimer/KillTimer 都是 user32（现有 INTERFACE shim 自动转发，零新 shim）；WM_TIMER 分支复用既有 WndProcImpl 的 `if (msg == X)` 模式；SendMessageW 已大量使用。gen/CMakeLists.txt L4/L33 已含 user32。
2. **可 headless 确定性验证**：核心时序风险（真实 WM_TIMER 何时入队）用 **SendMessageW 直投** 完全规避 —— SendMessage 同步直达 WndProcImpl，绕过消息队列，无真实计时器触发。SetTimer 返回 nIDEvent（传 ID_TIMER29=480 返 480，确定）。
3. **全新控件**：Timer 是 winforms-plan §3 阶段 2 清单中唯一尚未覆盖的控件（Phase 1-28 无任何 SetTimer/WM_TIMER 面），是真正的"新控件"里程碑，而非既有阶段的增强。
4. **Phase 28 审计已标记为自然后续**（discover #232：Timer 确定性设计验证通过）。
5. **工作量最低、风险最低**：~5 处编辑（常量/P-Invoke/状态/WndProc 分支/Main 块）+ expected.txt，与 Phase 28 同量级。

---

## 3. 完整规格

### 3.1 新 P/Invoke（2 个，均 user32，现有 shim）

```csharp
// Phase 29: SetTimer (user32 -- the existing shim). Creates a timer for the
// window. nIDEvent = the timer id (nonzero -> the system returns it unchanged,
// deterministic); uElapse = the interval in ms (100000 = 100s, far beyond the
// test's <1s runtime so NO real WM_TIMER is ever posted to the queue);
// lpTimerFunc = NULL (the window-message form -- WM_TIMER is delivered to the
// window's WndProc, not a callback). Returns the timer id (UINT_PTR).
[DllImport("user32", EntryPoint = "SetTimer")]
public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, int uElapse, IntPtr lpTimerFunc);

// Phase 29: KillTimer (user32 -- the existing shim). Destroys the timer
// created by SetTimer (uIDEvent = the id). Returns a BOOL: nonzero if the
// timer was destroyed. Called immediately after the direct injections so no
// real WM_TIMER can ever fire.
[DllImport("user32", EntryPoint = "KillTimer")]
public static extern int KillTimer(IntPtr hWnd, IntPtr uIDEvent);
```

- 签名均为标量（IntPtr/IntPtr/int/IntPtr 与 IntPtr/IntPtr），转译器 PinvokeParamList 已支持（IntPtr 参数 + "ptr" 返回，Phase 23-28 同型）。
- 无 struct、无 delegate（lpTimerFunc = NULL，用窗口消息形式）。

### 3.2 新常量（2 个）

```csharp
// Phase 29: Timer constants. WM_TIMER (0x0113) is the message the system
// posts to a window when a SetTimer timer elapses -- the headless test
// INJECTS it directly with SendMessageW (synchronous, reaches the WndProc
// immediately, bypassing the queue) so the count is deterministic regardless
// of real timer timing. ID_TIMER29 (0x1E0) is the timer id passed to
// SetTimer -- the per-phase id convention (0x1D0 was Phase 28).
public const int WM_TIMER = 0x0113;
public const int ID_TIMER29 = 0x1E0;
```

### 3.3 可观察状态（5 个字段）

```csharp
// Phase 29: Timer observable state. P29TimerSet = SetTimer returned a nonzero
// id; P29TimerId = the returned id (== ID_TIMER29, deterministic); P29TimerFired
// = the WM_TIMER WndProc-branch counter (incremented by each direct
// SendMessageW injection); P29TimerIdSeen = the wParam id the WM_TIMER branch
// observed (== ID_TIMER29); P29TimerKilled = KillTimer returned nonzero.
public static bool P29TimerSet;
public static int P29TimerId;
public static int P29TimerFired;
public static int P29TimerIdSeen;
public static bool P29TimerKilled;
```

### 3.4 WndProcImpl 分支（1 处，加在 `if (msg == P24_CB_MSG)` 之后、`return DefWindowProcW(...)` 之前）

```csharp
if (msg == WM_TIMER) {
    // Phase 29: the timer-elapsed message. A real SetTimer timer posts WM_TIMER
    // to the window's queue when it elapses; the headless test INJECTS it
    // directly with SendMessageW (synchronous -- reaches this proc immediately,
    // bypassing the queue) so the count is deterministic. wParam = the timer id.
    P29TimerFired++;
    P29TimerIdSeen = (int)wParam.ToInt64();
    return IntPtr.Zero;
}
```

- WM_TIMER 不是 WM_COMMAND，不扰动全局 wmCommandCount（Phase 28 的 EM_SETEVENTMASK 抑制已保证文本操作不扰动；WM_TIMER 分支本身也不触碰 wmCommandCount）。
- 分支位置在 P24_CB_MSG 之后、DefWindowProcW 兜底之前（与既有分支一致）。

### 3.5 headless 方案（全同步，无 worker、无 pump）

插入点：Phase 28 块尾（`Console.WriteLine("p28_destroyed=" + P28Destroyed);`，L4666）之后、stage-2 ButtonClick 接线（L4668）之前。

```csharp
// ===================== Phase 29: Timer (SetTimer / WM_TIMER) =====================
// The WinForms Timer control modeled on the user32 SetTimer + WM_TIMER
// protocol. The headless sequence is fully SYNCHRONOUS (no worker, no pump):
//   * SetTimer(hForm, ID_TIMER29, 100000, NULL) -- the 100000ms (100s) elapse
//     is far beyond the test's <1s runtime, so NO real WM_TIMER is ever posted
//     to the queue. SetTimer returns the timer id (pass ID_TIMER29=480,
//     return 480 -- deterministic).
//   * SendMessageW(hForm, WM_TIMER, ID_TIMER29, 0) -- the DIRECT injection:
//     SendMessage is synchronous and reaches the WndProc immediately (bypassing
//     the queue), so the WM_TIMER branch fires deterministically. Two injections
//     prove the counter is repeatable (P29TimerFired == 2).
//   * KillTimer(hForm, ID_TIMER29) -- destroys the timer immediately after the
//     injections, so no real WM_TIMER can ever fire during the rest of the test.
IntPtr setTimer29 = SetTimer(hForm, new IntPtr(ID_TIMER29), 100000, IntPtr.Zero);
P29TimerSet = setTimer29 != IntPtr.Zero;
P29TimerId = setTimer29.ToInt32();
Console.WriteLine("p29_timer_set=" + P29TimerSet);
Console.WriteLine("p29_timer_id=" + P29TimerId);

SendMessageW(hForm, WM_TIMER, new IntPtr(ID_TIMER29), IntPtr.Zero);
SendMessageW(hForm, WM_TIMER, new IntPtr(ID_TIMER29), IntPtr.Zero);
Console.WriteLine("p29_timer_fired=" + P29TimerFired);
Console.WriteLine("p29_timer_id_seen=" + P29TimerIdSeen);

int killTimer29 = KillTimer(hForm, new IntPtr(ID_TIMER29));
P29TimerKilled = killTimer29 != 0;
Console.WriteLine("p29_timer_killed=" + P29TimerKilled);
```

### 3.6 预期输出行（net48 oracle 验证点，5 行）

```
p29_timer_set=True
p29_timer_id=480
p29_timer_fired=2
p29_timer_id_seen=480
p29_timer_killed=True
```

- `p29_timer_id=480`：SetTimer 传 ID_TIMER29=0x1E0=480，返回 nIDEvent=480（非零 nIDEvent 时 SetTimer 返回该值，确定性）。
- `p29_timer_fired=2`：两次 SendMessageW 直投，WM_TIMER 分支各 +1。
- `p29_timer_id_seen=480`：WM_TIMER 分支读 wParam=ID_TIMER29=480。
- 双跑（net48 vs 转译 C++）byte-exact，与 Phase 1-28 同流程。

### 3.7 转译器 / runtime 缺口

**零缺口**：
- SetTimer/KillTimer 均为标量 P/Invoke（IntPtr 参数 + "ptr"/int 返回），转译器 PinvokeParamList/PinvokeReturnTypeCpp 已支持（Phase 23-28 同型）。
- SendMessageW 已大量使用（Phase 9/16/17/20/28）。
- 无 struct、无 delegate、无数组、无 UTF-16 新面。
- user32 shim 已存在，gen/CMakeLists.txt 自动链接（L4 add_subdirectory + L33 target_link_libraries 已含 user32）。

### 3.8 工作量

- 5 处编辑：常量区（WM_TIMER/ID_TIMER29）、P/Invoke 区（SetTimer/KillTimer）、可观察状态区（P29* 5 字段）、WndProcImpl（WM_TIMER 分支）、Main 块（Phase 29 块）。
- expected.txt 追加 5 行。
- 双跑验证（net48 build + 转译 + cmake MinGW build + Compare-Object DIFF=0 + 确定性）。
- 与 Phase 28 同量级（~1 批次，1 commit）。

### 3.9 风险

| 风险 | 缓解 |
|---|---|
| **真实 WM_TIMER 时序**（SetTimer 的 100s elapse 若测试超时则可能入队） | elapse=100000ms 远超 <1s 运行；KillTimer 在直投后立即调用，测试结束前无真实 WM_TIMER 可入队；后续 phase 的 pump 不会 dispatch 到 WM_TIMER（无该消息在队列） |
| **SetTimer 返回 id 的确定性** | SetTimer 传非零 nIDEvent 时返回该值（Windows 文档）；用 ID_TIMER29=0x1E0=480，断言 `p29_timer_id=480` |
| **ID 值选择** | 0x1E0=480 是 per-phase 约定（0x1D0 是 Phase 28）；与既有 id（0x140-0x1D0）不冲突 |
| **WM_TIMER 分支扰动全局计数** | WM_TIMER 非 WM_COMMAND，不触碰 wmCommandCount；分支只增 P29TimerFired |
| **net48 oracle 一致性** | 同一源码跑真实 Windows，SetTimer/KillTimer/SendMessageW 行为一致，byte-exact |

---

## 4. 可选伴生：ProgressBar 增强（低风险，可并入同批次）

若想在同批次增加价值，可并入 ProgressBar 增强（复用 P9ProgressHwnd，全 SendMessageW，零新 P/Invoke）：
- 常量（commctrl.h 实测）：PBM_SETBARCOLOR=0x0409/GETBARCOLOR=0x040F、PBM_SETBKCOLOR=CCM_SETBKCOLOR=0x2001/GETBKCOLOR=0x040E、PBM_SETSTEP=0x0404/GETSTEP=0x040D/STEPIT=0x0405、PBM_DELTAPOS=0x0403、PBM_SETSTATE=0x0410/GETSTATE=0x0411、PBST_NORMAL=1/ERROR=2/PAUSED=3。
- headless：全同步标量消息，PBM_SETBARCOLOR(0xFF0000)+GETBARCOLOR 回读、PBM_SETSTEP(5)+STEPIT+GETPOS、PBM_SETSTATE(PBST_ERROR)+GETSTATE 回读。
- 零新 shim、零新 P/Invoke、零转译器缺口。
- 但它是"增强"非"新控件"，建议作为 Phase 29 的伴生（若批次预算允许），主目标仍是 Timer。

---

## 5. 证据索引

- [READ:winforms1.cs:742-774] Phase 28 常量区（RICHEDIT_CLASS/EM_* 模式）
- [READ:winforms1.cs:1202-1209] Phase 28 P/Invoke（LoadLibraryW kernel32 零新 shim 模式）
- [READ:winforms1.cs:1620-1643] Phase 28 可观察状态（P28* 模式）
- [READ:winforms1.cs:1675-1960] WndProcImpl（`if (msg == X)` 分支模式 + DefWindowProcW 兜底）
- [READ:winforms1.cs:4565-4666] Phase 28 Main 块（插入点 L4666 之后、L4668 之前）
- [READ:winforms1.cs:2803-2962] Phase 9 ProgressBar + Phase 10 MainMenu（复用面）
- [READ:comctl32/CMakeLists.txt] shim INTERFACE 模式
- [READ:gen/CMakeLists.txt:4-33] 6 shim 已自动链接（user32 在列）
- [BASH:commctrl.h] PBM_* 全值 + CCM_SETBKCOLOR=0x2001 + PBST_* 状态
- [BASH:winuser.h] WM_TIMER=0x0113、SetTimer(UINT_PTR)/KillTimer(BOOL) 签名、MF_*/GetMenuState/CheckMenuItem/EnableMenuItem/GetSubMenu/GetMenuItemID
- [READ:discover.md:232] Timer 确定性设计（elapse=100000 + SendMessageW 直投 + SetTimer 返 id）
- [READ:discover.md:234] ToolStrip 语义偏离（自绘 "WindowsForms10.toolstrip" 类，非 CreateMenu）
