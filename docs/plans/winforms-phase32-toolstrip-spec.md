# WinForms Phase 32 规格（ToolStrip 简化 —— ToolbarWindow32 + TB_* 通道）

- 日期：2026-08-16
- 性质：只读审计产物（ILC 域，`tests/cases/winforms1/winforms1.cs` 下一阶段），不修改任何既有文件
- 前置：Phase 31（StatusStrip 多部件）已落地，winforms1.cs 5666 行 / expected.txt 391 行，net48 byte-exact
- 计划定位：`docs/plans/winforms-plan.md` §3 阶段 2（ToolStrip），winforms1.cs 的 Phase 32（审计结论：Phase 31 → Phase 32 ToolStrip，DataGridView → Phase 33）

---

## 1. 可行性分析

### 1.1 结论：可行，且是本阶段最低复杂度的一类

Phase 32 与 Phase 31（StatusStrip，commit d51e23a）**完全同构**：在既有 comctl32 子窗口上，用既有的
`SendMessageW`（user32，已接线，winforms1.cs:959）发一条条全同步的控件消息，配合 GCHandle-pinned int[]
承载原生结构体字节（Phase 16/21/22/25/27/31 全部同一模式，`icall_gchandle_alloc/addr_of_pinned/free`
已接线，见 `tests/cases/winforms1/gen/src/WfListView.cpp:208-214` 的生成产物证据）。

关键论证：
1. **零新增 P/Invoke / 零新增 shim / 零新增 [StructLayout] struct**（与 Phase 31 同级的"零新增面"）：
   - 所有 TB_* 消息走既有的 `SendMessageW(IntPtr,int,IntPtr,IntPtr)`；
   - 控件创建走既有的 `CreateWindowExW`（winforms1.cs:910）；交互注入走既有的 `PostMessageW`（:937）；
   - 归属证明走既有的 `GetParent`（:977）；消息泵走既有的 `PeekMessageW`（:971）；
   - comctl32 shim 已存在（`tests/cases/winforms1/comctl32/CMakeLists.txt`，INTERFACE target → comctl32.lib），
     **不新增任何 shim**；工具栏类由 Phase 9 已注册的 `InitCommonControlsEx(ICC_BAR_CLASSES | ICC_PROGRESS_CLASS)`
     （winforms1.cs:2934）覆盖——**ICC_BAR_CLASSES = 0x4 注册 toolbar/statusbar/trackbar/tooltip**（mingw commctrl.h 文档）。
2. **headless 确定性**：TB_* 全部是同步 SendMessageW（无队列、无时序依赖）；"点击"交互与 Phase 10 菜单
   同款（PostMessage WM_COMMAND → PeekMessageW drain，winforms1.cs:3079-3094），可选增强为对工具栏
   直接 SendMessageW(WM_LBUTTONDOWN/UP) 的原生中继验证（见 §4.4，net48 探针门控）。
3. **net48 oracle 同源双跑**：同一 winforms1.cs 源码在 net48 与转译 C++ 上跑，两者调用的是**同一份 OS 的
   comctl32**——任何 comctl32 行为（包括本文档标注为"探针确认"的怪癖）在两侧逐字节一致，byte-exact 保证成立。

### 1.2 需要修正的任务前提：TBS_* 是 TrackBar 样式族，不是 Toolbar

`commctrl.h:1946-1965` 的 `TBS_AUTOTICKS/TBS_VERT/TBS_HORZ/...` 全部是 **TrackBar** 窗口样式
（Phase 20 已用 TBS_AUTOTICKS=0x1）。Toolbar 窗口样式是 **CCS_***（`commctrl.h:2185-2191`：CCS_TOP=0x1）
+ **TBSTYLE_***（TBSTYLE_FLAT=0x800 / TBSTYLE_LIST=0x1000 / TBSTYLE_TOOLTIPS=0x100 等同时兼作
按钮样式与工具栏窗口样式）。本规格按真实 API 命名，不引入"TBS_* 工具栏样式"。

### 1.3 结构性事实（mingw-w64 SDK 头文件证据，C:\Program Files\ming-w64\include\commctrl.h）

- TBBUTTON x64 布局（`:840-852`）：`iBitmap(int)@0 / idCommand(int)@4 / fsState(BYTE)@8 /
  fsStyle(BYTE)@9 / bReserved[6]@10-15 / dwData(DWORD_PTR)@16 / iString(INT_PTR)@24`，**sizeof = 32B =
  恰 8 个 int32**（审计给定的"TBUTTON int[8]"成立）。
- TB_* 消息号（`:937-1102`，WM_USER=0x400）：见 §3 常量表。
- TB_BUTTONSTRUCTSIZE **无返回值**（MS 文档："No return value"）——测试不得断言其返回，只保证在
  TB_ADDBUTTONS 之前发（CreateWindowEx 创建的工具栏必须先发，否则行为未定义）。
- TB_ADDBUTTONSW / TB_GETBUTTONTEXTW / TB_INSERTBUTTONW 是独立消息号（WM_USER+68 / +75 / +67），
  W 变体由消息号本身选择——与 Phase 9 StatusBar 的 W 变体通道（SB_SETTEXT=WM_USER+11，探针 byte-exact）
  同理。Unicode 进程（net48 与转译 C++ 都是）下 CreateWindowExW 创建即 Unicode；规格仍防御性发
  CCM_SETUNICODEFORMAT(TRUE)（0x2005，幂等）并回读 TB_GETUNICODEFORMAT（0x2006）。
- 工具栏按钮被点击 → 向父窗口发 **WM_COMMAND，wParam 低 16 位 = idCommand**（winprog.org 教程实证：
  "These buttons will generate WM_COMMAND messages identical to the menu"）——Phase 10 的 WndProc
  WM_COMMAND 按 id 分发链条（winforms1.cs:1765-1790）**直接复用**，这是"ToolStrip 按钮点击"事件面的
  天然落点。

---

## 2. 完整规格

### 2.1 新增面总览

| 面 | 新增量 |
|---|---|
| P/Invoke 声明 | **0**（全走既有 SendMessageW / CreateWindowExW / PostMessageW / GetParent / GetWindowLongW） |
| shim | **0**（comctl32 INTERFACE shim 已存在；工具栏类已由 Phase 9 注册） |
| [StructLayout] struct | **0**（TBUTTON 用 GCHandle-pinned int[8] 内联构建，同 TCITEMW/LVITEMW/TVINSERTSTRUCTW 模式） |
| 新常量 | ~35（§3） |
| 新托管类 | 2：`WfToolStrip`（HWND 包装 + TB_* 操作）、`WfToolButton`（id + text + Click 事件，Phase-10 WfMenuItem 形态） |
| 转译器 / runtime 缺口 | **0**（全部走已接线通道：pinned int[]、PtrToStringUni、StringToHGlobalUni、委托多播、字符串拼接） |

### 2.2 winforms1.cs 落点（行锚点）

| 内容 | 位置 |
|---|---|
| 常量块（"Phase 32: ToolStrip"注释） | 插在 Phase 31 常量之后：`winforms1.cs:472`（WF_STATUS_UPDATE）之后 |
| 可观察状态字段 | `winforms1.cs:1412`（P31Updated）之后 |
| WndProc WM_COMMAND 分发分支 | `winforms1.cs:1788`（P10About 分发）之后，追加 3 条 else-if |
| Main 块 Phase 32 代码 | `winforms1.cs:4955`（p31_updated 打印）之后、`4957`（Stage 2 接 Click 事件）之前 |
| 托管类 WfToolStrip / WfToolButton | 文件末尾（WfPictureBox 之后，`winforms1.cs:5666`） |
| expected.txt 行 | `expected.txt:362`（p31_updated）之后、`363`（wm_create_count）之前 |

### 2.3 托管模型

```csharp
// WfToolButton：Phase-10 WfMenuItem 形态（Id + Text + Click 多播事件）
public class WfToolButton {
    public int Id;
    public string Text;
    private Cases.Winforms1.ClickHandler _click;
    public event Cases.Winforms1.ClickHandler Click { add { _click = _click + value; } remove { _click = _click - value; } }
    public int ClickCount { get { return _click == null ? 0 : _click.GetInvocationList().Length; } }
    public void RaiseClick() { if (_click != null) _click(); }
}

// WfToolStrip：包装 "ToolbarWindow32" HWND 的 TB_* 面
public class WfToolStrip {
    public IntPtr Handle;
    public int Id;
    // Add(text, id, style, state) -> 构建 TBUTTON int[8]（§2.4）并 TB_ADDBUTTONSW
    // Count -> TB_BUTTONCOUNT
    // GetText(id) -> TB_GETBUTTONTEXTW（256B 缓冲 + PtrToStringUni）
    // GetButtonState(id) -> TB_GETSTATE / Enable(id,bool) -> TB_ENABLEBUTTON
    // Check(id,bool) -> TB_CHECKBUTTON / IsChecked(id) -> TB_ISBUTTONCHECKED
    // Delete(index) -> TB_DELETEBUTTON / IndexOf(id) -> TB_COMMANDTOINDEX
    // GetItemRect(index) -> TB_GETITEMRECT（pinned int[4]）
}
```

### 2.4 TBUTTON 的 GCHandle-pinned int[8] 布局（offsetof 探测纪律）

x64 原生 TBBUTTON（32B）→ pinned int[8]（数据 8 对齐，恰同结构体自然对齐）：

| 索引 | 偏移 | 字段 | 值 |
|---|---|---|---|
| [0] | 0 | iBitmap | `I_IMAGENONE`（-2，纯文本按钮无图像） |
| [1] | 4 | idCommand | 按钮命令 id（如 0x1F1） |
| [2] | 8 | fsState + fsStyle | `fsState | (fsStyle << 8)`（BYTE@8 / BYTE@9） |
| [3] | 12 | bReserved[高 4B] | 0 |
| [4] | 16 | dwData 低 32 | 自定标记（沿用各 Phase 的 lParam=42 惯例） |
| [5] | 20 | dwData 高 32 | 0 |
| [6] | 24 | iString 低 32 | StringToHGlobalUni 指针低半 |
| [7] | 28 | iString 高 32 | 指针高半 |

3 按钮 → `int[24]`（3×32=96B）。**offsetof 探针纪律（实现前必做）**：在 net48 探针里
`Marshal.OffsetOf<TBBUTTON>` 逐字段打印，必须得到 iBitmap@0 / idCommand@4 / fsState@8 / fsStyle@9 /
dwData@16 / iString@24 / sizeof=32（与 mingw 头文件 `:840-852` 及 MS SDK 一致），才落 int[8] 布局。

**读回纪律**（承 Phase-22 HTREEITEM 教训，winforms1.cs:5549-5557）：TB_GETBUTTON 读回的 iString 是
comctl32 内部缓冲指针，**跨进程不确定，绝不打印**；按钮文本一律走 TB_GETBUTTONTEXTW。dwData /
idCommand / fsState / fsStyle 是确定性字段，可打印。fsState = `[2] & 0xFF`，fsStyle = `([2] >> 8) & 0xFF`。

### 2.5 Main 块阶段逻辑（headless 全同步方案）

1. **创建**：`CreateWindowExW(0, WC_TOOLBAR, "", WS_CHILD | CCS_TOP, 0, 0, 320, 24, hForm, new IntPtr(ID_TOOL32), hModule, IntPtr.Zero)`
   → `p32_toolbar_created` + `p32_parent_is_form`（`GetParent(hTool) == hForm`）。隐藏表单的 WS_CHILD，
   与 Phase 9 StatusBar 完全同姿势。
2. **Unicode 格式**：`CCM_SETUNICODEFORMAT(TRUE)`（返回先前标志，打印）+ `TB_GETUNICODEFORMAT`（回读，打印）。
3. **结构体尺寸**：`TB_BUTTONSTRUCTSIZE`，wParam=32，lParam=0（**无返回值**，仅保证在 add 前发）。
4. **加按钮**：TBUTTON int[24]（New/Open/About/sep/Bold，见 §2.6）→ `TB_ADDBUTTONSW`（wParam=5，
   lParam=pinned 地址）→ `p32_add_ok` + `p32_count`（TB_BUTTONCOUNT=5）。
5. **结构读回**：`TB_GETBUTTON(2)`（分隔符）→ fsStyle==TBSTYLE_SEP（`p32_sep_style`）；
   `TB_GETBUTTON(0)` → id/state/style/data（`p32_get0_*`）；`TB_GETBUTTONTEXTW(0x1F1/0x1F2/0x1F3)`
   → `p32_text_new/open/about`。
6. **Check 态**：`TB_CHECKBUTTON(0x1F4, TRUE)` → `TB_ISBUTTONCHECKED` → `TB_GETSTATE(0x1F4)`
   （TBSTATE_ENABLED|TBSTATE_CHECKED=0x05）→ `p32_check_*`。
7. **Enable 态**：`TB_ENABLEBUTTON(0x1F2, FALSE)` → `TB_GETSTATE(0x1F2)`（无 TBSTATE_ENABLED=0）→
   `TB_ENABLEBUTTON(0x1F2, TRUE)` → `TB_GETSTATE`（=4）→ `p32_enable_*`（Phase-30 菜单态通道同款）。
8. **命中矩形**：`TB_GETITEMRECT(0)`（pinned int[4]，16B）→ `p32_itemrect0_nonzero`（供真实点击用）。
9. **交互 A（真实点击，探针门控 §4.4）**：对工具栏 `SendMessageW(WM_LBUTTONDOWN, MK_LBUTTON, center)`
   + `SendMessageW(WM_LBUTTONUP, 0, center)`（center = TB_GETITEMRECT 中心，lParam = x | y<<16）→
   工具栏同步中继 WM_COMMAND(0x1F1) 给表单 → WndProc 分发 → New.Click。随后一次 PeekMessageW drain
   （工具栏若用 PostMessage 中继则被 drain 消化）。
10. **交互 B（posted 命令，Phase-10 菜单模式，确定性主通道）**：`PostMessageW(hForm, WM_COMMAND, 0x1F2, 0)`
    + drain → Open.Click。
11. **状态联动**：每个 Click 处理器把按钮文本镜像进 StatusBar 部件 0（SB_SETTEXT wParam=0 +
    SB_GETTEXT 回读，Phase-31 通道）→ "toolbar drives status"（`p32_click_seq` + `p32_status`）。
12. **删除**：`TB_DELETEBUTTON(3)`（About）→ `TB_BUTTONCOUNT`=4 + `TB_COMMANDTOINDEX(0x1F3)`==-1
    → `p32_delete_ok` / `p32_count_after` / `p32_about_deleted`。
13. 字符串缓冲（StringToHGlobalUni×3）**保持存活到本阶段结束**再释放（规避"工具栏是否复制文本"的
    实现差异——探针确认该行为并写入注释，但测试设计不依赖它）。

### 2.6 按钮数组（id 用 Phase-32 专属 0x1F0 段，避开 0x110/0x120 菜单 id 与 0x101-0x1E0 既有 id）

| 索引 | idCommand | text | fsState | fsStyle |
|---|---|---|---|---|
| 0 | 0x1F1 | "New" | TBSTATE_ENABLED(4) | TBSTYLE_BUTTON(0) |
| 1 | 0x1F2 | "Open" | TBSTATE_ENABLED(4) | TBSTYLE_BUTTON(0) |
| 2 | 0 | (分隔符) | 0 | TBSTYLE_SEP(1) |
| 3 | 0x1F3 | "About" | TBSTATE_ENABLED(4) | TBSTYLE_BUTTON(0) |
| 4 | 0x1F4 | "Bold" | TBSTATE_ENABLED(4) | TBSTYLE_CHECK(2) |

真实点击只打在 TBSTYLE_BUTTON 上（WM_COMMAND HIWORD=0，Phase-10 确定性）；Check 按钮只探测状态通道
（避免 Check 按钮点击的 HIWORD=check-state 语义歧义）。

### 2.7 WndProc 分发

在 `winforms1.cs:1788` 之后追加（与 P10 菜单同款 else-if 链）：

```csharp
else if (P32New != null && cid == P32New.Id) P32New.RaiseClick();
else if (P32Open != null && cid == P32Open.Id) P32Open.RaiseClick();
else if (P32About != null && cid == P32About.Id) P32About.RaiseClick();
```

不需要新 WF_* 自定义消息——真实点击与 posted 注入都落到同一个 WM_COMMAND 分发分支。

---

## 3. 新常量（全部经 mingw commctrl.h 实测，winforms1.cs 常量区风格）

```csharp
// Phase 32: ToolStrip constants. WC_TOOLBAR is the comctl32 toolbar class
// ("ToolbarWindow32", registered by the Phase-9 InitCommonControlsEx
// ICC_BAR_CLASSES); the TB_* messages go through the existing SendMessageW
// channel. TBBUTTON is built as a GCHandle-pinned int[8] (x64 32-byte
// layout, offsetof-probed: iBitmap@0/idCommand@4/fsState@8/fsStyle@9/
// dwData@16/iString@24). TBSTYLE_* double as the button styles AND the
// toolbar window styles; the window creation style is WS_CHILD | CCS_TOP.
public const string WC_TOOLBAR = "ToolbarWindow32";
public const int CCS_TOP = 0x00000001;
public const int TB_BUTTONSTRUCTSIZE = 0x041E;   // WM_USER+30 (no return value)
public const int TB_ENABLEBUTTON = 0x0401;        // WM_USER+1
public const int TB_CHECKBUTTON = 0x0402;         // WM_USER+2
public const int TB_ISBUTTONENABLED = 0x0409;     // WM_USER+9
public const int TB_ISBUTTONCHECKED = 0x040A;     // WM_USER+10
public const int TB_GETSTATE = 0x0412;            // WM_USER+18
public const int TB_ADDBUTTONSW = 0x0444;         // WM_USER+68 (W variant)
public const int TB_DELETEBUTTON = 0x0416;        // WM_USER+22
public const int TB_GETBUTTON = 0x0417;           // WM_USER+23
public const int TB_BUTTONCOUNT = 0x0418;         // WM_USER+24
public const int TB_COMMANDTOINDEX = 0x0419;      // WM_USER+25
public const int TB_GETITEMRECT = 0x041D;         // WM_USER+29
public const int TB_GETBUTTONTEXTW = 0x044B;      // WM_USER+75 (W variant)
public const int CCM_SETUNICODEFORMAT = 0x2005;   // CCM_FIRST+5
public const int CCM_GETUNICODEFORMAT = 0x2006;   // CCM_FIRST+6
public const int TBSTATE_CHECKED = 0x0001;
public const int TBSTATE_ENABLED = 0x0004;
public const int TBSTATE_HIDDEN = 0x0008;
public const int TBSTYLE_BUTTON = 0x0000;
public const int TBSTYLE_SEP = 0x0001;
public const int TBSTYLE_CHECK = 0x0002;
public const int I_IMAGENONE = -2;
public const int WM_LBUTTONDOWN = 0x0201;
public const int WM_LBUTTONUP = 0x0202;
public const int MK_LBUTTON = 0x0001;
public const int ID_TOOL32 = 0x1F0;               // toolbar control id (hMenu param)
public const int ID_TB_NEW = 0x1F1;
public const int ID_TB_OPEN = 0x1F2;
public const int ID_TB_ABOUT = 0x1F3;
public const int ID_TB_BOLD = 0x1F4;
```

（可选探针附加：TB_CHECKBUTTON=0x0402、TB_ISBUTTONENABLED=0x0409、TB_ISBUTTONPRESSED=0x040B，
按探针需要裁剪；YAGNI 下正文只用上表所列。）

---

## 4. 预期输出行（net48 oracle 验证点，插在 expected.txt:362 之后）

```
p32_toolbar_created=True
p32_parent_is_form=True
p32_unicode_prev=1            // CCM_SETUNICODEFORMAT(TRUE) 返回的先前标志（Unicode 窗口=1，探针确认）
p32_unicode_now=1             // CCM_GETUNICODEFORMAT 回读
p32_structsize_sent=True      // TB_BUTTONSTRUCTSIZE 已发（无返回值，仅流程保证）
p32_add_ok=True
p32_count=5
p32_get0_id=497 state=4 style=0 data=42     // TB_GETBUTTON(0) 读回（0x1F1=497）
p32_sep_style=1               // TB_GETBUTTON(2) fsStyle == TBSTYLE_SEP
p32_text_new=New
p32_text_open=Open
p32_text_about=About
p32_itemrect0_nonzero=True
p32_check_prev=0              // TB_CHECKBUTTON(0x1F4, TRUE) 前 TB_GETSTATE
p32_check_checked=True        // TB_ISBUTTONCHECKED
p32_check_state=5             // TB_GETSTATE == TBSTATE_ENABLED|TBSTATE_CHECKED
p32_enable_prev=True          // TB_ENABLEBUTTON(0x1F2, FALSE) 返回
p32_open_state0=0             // disable 后 TB_GETSTATE 无 TBSTATE_ENABLED
p32_enable_again=True
p32_open_state1=4             // re-enable 后 TB_GETSTATE 恢复 TBSTATE_ENABLED
p32_click_inject=True         // 真实点击 + posted 命令都投递成功
p32_click_seq=...             // 例如 "new;open;"（真实点击 New + posted Open 的分发顺序）
p32_status=...                // StatusBar 部件0 回读（toolbar drives status）
p32_delete_ok=True
p32_count_after=4
p32_about_deleted=True        // TB_COMMANDTOINDEX(0x1F3) == -1
```

全部为布尔/可打印标量，无原始句柄、无指针值、无像素绝对值——跨机器确定（0x1F1=497 是 id 常量的十进制，
两侧一致）。

### 4.4 实现前 net48 探针清单（tmp/ 一次性探针，同 sbprobe/lvprobe 纪律，不提交）

1. `Marshal.OffsetOf<TBBUTTON>` 全字段 + sizeof → 验证 int[8] 布局（§2.4）。
2. `CCM_SETUNICODEFORMAT(TRUE)` 返回与 `TB_GETUNICODEFORMAT` → 定稿 p32_unicode_prev 期望值。
3. `TB_BUTTONSTRUCTSIZE` 原始返回 → 确认"无返回值/无害"。
4. `TB_ADDBUTTONSW` 直接返回 + `TB_BUTTONCOUNT` → 确认 W 变体在 v5.82 直通。
5. 释放 iString 缓冲后再 `TB_GETBUTTONTEXTW` → 确认工具栏是否复制文本（定稿测试是否需保活）。
6. `TB_GETITEMRECT` 是否非零 → 确认 headless 下矩形有效。
7. `WM_LBUTTONDOWN/UP` 中心点击是否触发父窗口 WM_COMMAND（**真实点击交互的门控**）：
   失败则降级为仅 posted WM_COMMAND（Phase-10 模式，主通道本来就确定）。
8. `TB_GETBUTTON` 读回 id/state/style/data + iString 原始值形态 → 确认"iString 不打印"纪律。

---

## 5. 转译器 / runtime 缺口

**0 缺口。** Phase 32 只使用下列全部已接线通道（每项均有既有 Phase 的生成产物/运行证据）：

- SendMessageW / CreateWindowExW / PostMessageW / GetParent（winforms1.cs:910-977，user32 shim）
- GCHandle pinned int[] + `icall_gchandle_addr_of_pinned`（`gen/src/WfListView.cpp:208-214`）
- Marshal.StringToHGlobalUni / PtrToStringUni / AllocHGlobal / FreeHGlobal（Phase 9-31 全链路）
- 委托多播事件 + GetInvocationList（Phase 4/10 全链路，`efa6741` 修过 void-0-arg multicast）
- PeekMessageW drain（Phase 4 模式）、字符串拼接 / int 打印

无新 icall、无新 marshal 类型、无新 CMake target（comctl32 shim 已存在且已链接）。

---

## 6. 工作量 + 风险

### 6.1 工作量：低（约 0.5–1 天，含探针）

| 项 | 估量 |
|---|---|
| net48 一次性探针（tmp/，~60 行） | 1–2 小时 |
| winforms1.cs 常量 + 状态 + WndProc 分支 + Main 块 + 2 托管类 | ~150–220 行，2–4 小时 |
| expected.txt +15 行；net48 双跑更新 | 0.5 小时 |
| 转译 + MSVC/MinGW 双编译器 + Compare-Object byte-exact | 沿用 batch 流程，1 小时 |

### 6.2 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| W 变体消息（TB_ADDBUTTONSW/TB_GETBUTTONTEXTW）在 v5.82 上的 Unicode 格式默认 | 低 | 探针 #2/#4 确认；规格防御性发 CCM_SETUNICODEFORMAT(TRUE) 并回读打印；Phase-9 W 通道先例（SB_SETTEXT=WM_USER+11 byte-exact） |
| iString 直接指针：工具栏复制 vs 引用 | 低 | 探针 #5 确认；测试设计保持缓冲存活到阶段末，不依赖复制语义 |
| TB_GETBUTTON 读回的 iString 是非确定内部指针 | 低 | 明文纪律：不打印 iString，文本一律 TB_GETBUTTONTEXTW（Phase-22 HTREEITEM 同款陷阱） |
| 真实鼠标点击（WM_LBUTTONDOWN/UP）中继 headless 不稳 | 中 | 探针 #7 门控；失败降级为仅 posted WM_COMMAND（Phase-10 模式，本身就是确定性主通道）；两侧同 OS 同 comctl32，net48 验证即 ILC 验证 |
| TB_GETITEMRECT 空矩形 | 中 | 探针 #6 确认；必要时先发 TB_AUTOSIZE 或 TB_SETBUTTONSIZE |
| TB_BUTTONSTRUCTSIZE 返回被误断言 | 低 | 文档明示无返回值，测试只记录"已发送" |
| fsState=0 默认禁用导致按钮不可点 | 低 | 显式设 TBSTATE_ENABLED + TB_GETSTATE 读回断言（§2.6） |
| 与既有控制 id / WM_COMMAND 分发冲突 | 低 | id 用 0x1F0 段（审计 id 惯例下一段，避开 0x101-0x1E0）；真实点击只打 TBSTYLE_BUTTON |
| wmCommandCount 时序敏感 | 低 | 阶段用自有 P32Seq/P32Status 序列（Phase-30 同款处置），不依赖全局计数 |

**净结论**：可行性成立，复杂度与 Phase 31 同级（纯消息 + pinned 结构体，零新增接线面）。唯一实质风险是
真实点击中继与 W 变体默认格式，两者都被"实现前 net48 探针 + 同 OS 双跑"完全兜底，且都有确定性降级路径。
