# WinForms 可行性调研计划（ILC 域）

- 日期：2026-08-07
- 性质：只读调研（PowerShell 反射枚举 + 源码证据），无代码改动
- 目标：评估 ILC（IL→C++ AOT，git m1）转译/运行 .NET Framework 4.8 System.Windows.Forms 的可行性，给出依赖面、差距表、三阶段路线与验收方案
- 数据来源：
  - 反射枚举：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll`（net48 GAC，CLR 4.0.30319 下运行），原始输出 `%TEMP%\opencode\winforms_survey_net48_out.txt`（1360 行）
  - ILC 源码：`src/il2cppc/**`（转译器）、`src/runtime/include/internal/*.h`（运行时）
  - 状态基线：`docs/alignment-tracker-v3.md`（M6 快照）、`docs/audit/icall-gap-list.md`

---

## 1. 依赖面分析（net48 System.Windows.Forms.dll 实测）

### 1.1 总体

| 指标 | 数值 |
|---|---|
| 类型总数 | 2442（public 787 / 接口 255 / 枚举 424） |
| DllImport（P/Invoke）方法 | **625** |
| ComImport（COM 接口）类型 | 198 |
| 消息循环相关 P/Invoke 条目 | 149 |
| Control 派生类 | 99（直接子类 31） |
| 嵌入资源 | 223（.resx ×4 + 图标/位图 .bmp ~100 + 动画等） |
| 程序集引用 | System.Drawing、System.ComponentModel.*、Accessibility、System.Xml 等 |

### 1.2 P/Invoke 清单（625 个，按 DLL 分组）

| DLL | 数量 | 典型条目 |
|---|---|---|
| user32.dll | **281** | SendMessage(60 处)、SystemParametersInfo(8)、PostMessage、PeekMessageW/A、GetMessageW/A、DispatchMessageW/A、TranslateMessage、CreateWindowEx、RegisterClass、DefWindowProc、CallWindowProc、SetWindowPos、ShowWindow、InvalidateRect、GetDC/ReleaseDC、SetTimer/KillTimer、GetKeyboardState、SetWindowsHookEx |
| gdi32.dll | **117** | CreateSolidBrush/CreatePen/CreateFontIndirect、SelectObject/DeleteObject、TextOutW/DrawTextW/GetTextExtentPoint32W、MoveToEx/LineTo/Ellipse/Rectangle/Arc、SetBkColor/SetTextColor、CreateCompatibleDC/Bitmap、StretchDIBits、GetDeviceCaps |
| kernel32.dll | 60 | GetModuleHandle、GetProcAddress、LoadLibrary(Ex)/FreeLibrary、GetCurrentThreadId、GetUserDefaultLCID、LocalAlloc/GlobalAlloc、CreateFile/ReadFile/WriteFile |
| uxtheme.dll | 31 | OpenThemeData/CloseThemeData/DrawThemeBackground（视觉样式） |
| ole32.dll | 24 | OleInitialize/OleUninitialize、CoCreateInstance、RegisterDragDrop/RevokeDragDrop、DoDragDrop、CreateStreamOnHGlobal |
| comctl32.dll | 20 | 通用控件消息（ListView/TreeView/StatusBar 消息） |
| oleaut32.dll | 17 | SysAllocString/SysFreeString、VariantClear、SafeArray 家族 |
| clr.dll | 16 | CLR 宿主调用（CorBindToRuntimeEx 等，托管宿主用） |
| shell32.dll | 12 | SHGetFolderPath、ShellExecute 等 |
| imm32.dll | 10 | ImmGetContext/ImmReleaseContext（IME 输入法） |
| UIAutomationCore.dll | 9 | UIA 提供者 |
| comdlg32.dll | 9 | GetOpenFileName/GetSaveFileName（传统对话框） |
| 其它 | ≤5 | hhctrl.ocx(5, HtmlHelp)、crypt32(5)、shlwapi(2)、oleacc(2)、SHCore/ntdll/advapi32/Powrprof/wldp(各1) |

**CharSet 分布：Auto 479 / None 108 / Unicode 25 / Ansi 13。** net48 下 CharSet.Auto = W 版调用 → **全部字符串路径都走 UTF-16（WCHAR*）**。SetLastError=true 共 193 处。

### 1.3 COM 依赖类清单（198 ComImport + 223 Guid 接口）

- **System.Deployment.Internal.Isolation（~130 个）**：ClickOnce 域，WinForms 常规 UI 路径不可达（仅部署场景），可整体排除。
- **UnsafeNativeMethods OLE/ActiveX 接口（~60 个）**：IOleDropTarget/IOleDropSource（拖放）、IOleClientSite/IOleInPlaceSite/IOleObject（ActiveX 承载，WebBrowser/AxHost）、IWebBrowser2 + IHTML*（WebBrowser）、IDispatch/ITypeInfo/ITypeLib（自动化）、IStream/IStorage/IMalloc、IRichEditOleCallback（RichTextBox OLE）、ICorRuntimeHost、UIAutomation Provider 接口族（IRawElementProviderFragment 等）。
- **FileDialogNative（10 个）**：IFileDialog/IFileOpenDialog/IShellItem（Vista 文件对话框）。
- **NativeMethods/SafeNativeMethods（少量）**：IFontDisp、IProvideClassInfo 等。
- **公共 COM 接口**：System.Windows.Forms.IWin32Window（Guid 标记，非 ComImport）、Design.IUIService。

### 1.4 消息循环面

- `Application.Run()` ×4 重载（无参/Form/ApplicationContext/RunDialog）+ `DoEvents()` + `Exit()` + `ExitThread()` + 模态循环（BeginModalMessageLoop/EndModalMessageLoop）。
- 泵在内部 `ThreadContext`（私有类）：`GetMessageW`/`PeekMessageW` + `TranslateMessage` + `DispatchMessageW`（DispatchMessageW 是 user32 P/Invoke，Message 结构体 by-ref）。
- `NativeWindow`（CreateHandle/DestroyHandle/ReleaseHandle/DefWndProc/WndProc）——窗口类注册（RegisterClass）+ CreateWindowEx + WndProc 回调分发。
- 计时器：SetTimer/KillTimer（Timer 控件）；SendMessage 60 处为控件间通信主力。

### 1.5 控件树 / 绘图

- 层次：Control(31 直接子类) → ScrollableControl → ContainerControl → Form；ButtonBase→Button/CheckBox/RadioButton；TextBoxBase→TextBox/MaskedTextBox/RichTextBox；ListControl→ListBox/ComboBox/CheckedListBox；Panel→GroupBox/…；TabControl/TabPage/ListView/TreeView/DataGridView/DateTimePicker/MonthCalendar/PictureBox/ProgressBar/TrackBar/StatusBar/StatusStrip/MenuStrip/ToolStrip/Splitter/SplitContainer/DataGrid/AxHost/WebBrowserBase…
- 绘图：Paint 事件 → PaintEventArgs → Graphics（System.Drawing，GDI+ gdiplus.dll——**相邻依赖面，本调研未枚举，见 §5 风险**）；Control 绘制经 WM_PAINT → BeginPaint/EndPaint + GDI 句柄。
- 控件状态存于托管字段（Controls/Text/Location/Size/Visible/Enabled/Handle 等）→ 纯托管对象，可 dump。

### 1.6 资源依赖

- 嵌入资源 223 个：字符串表（System.Windows.Forms.resources / System.SR.resources）、对话框模板（MdiWindowDialog/PrintPreviewDialog .resources）、~100 个 .bmp 图标（控件 Toolbox 位图 + DataGridView 列图标 + BindingNavigator 按钮图标）、Animation.bmp。
- 运行时读取路径：ResourceManager → ManifestResource（System.Resources）。**ILC 转译器当前无嵌入资源处理**（grep `ManifestResource|EmbeddedResource|GetManifestResource` 于 src/il2cppc = 0 匹配）。

---

## 2. ILC 差距映射

对照面：MethodMap 域（`src/il2cppc/Emit/MethodMap.*.cs`，实测 71 文件 / v3 基线 66 已提交）、runtime 域（104 头 / 459 icall 声明 / 393 已接线）、P/Invoke 层（il2cpp_pinvoke.h + il2cpp_pinvoke_ext.h）、COM（M8 远期，`docs/architecture/路线图.md:37`）。

| # | WinForms 需求面 | ILC 现状（证据） | 等级 |
|---|---|---|---|
| 1 | 标量参数/返回值 P/Invoke（int32/int64/double） | ✅ `PinvokeParamList` 发射 int32_t（CodeGenerator.cs:2966-2971）；pinvoke/pinvokeext e2e PASS | **已覆盖** |
| 2 | SetLastError=true（193 处） | ✅ capture/clear/get_last_error（il2cpp_pinvoke.h:59-60、_ext.h:84；MethodRefResolver.cs:289 标志解码） | **已覆盖** |
| 3 | IntPtr/UIntPtr **返回值**（GetDC 等） | ✅ "ptr" result kind（MethodRefResolver.cs:300-303；PinvokeReturnTypeCpp:intptr_t 2958-2963） | **已覆盖** |
| 4 | UTF-8 (Ansi) string marshal | ✅ string_to_utf8(_alloc)/from_utf8（il2cpp_pinvoke.h:47-48、_ext.h:69-73） | **已覆盖** |
| 5 | P/Invoke 静态/动态绑定 | ✅ extern "C" 静态声明 + 链接（CodeGenerator.cs:379-388）+ resolve 动态路径（il2cpp_pinvoke.h:32）；CMake target_link_libraries（CodeGenerator.cs:3982） | **已覆盖** |
| 6 | **UTF-16 (WCHAR\*) string marshal**（CharSet.Auto 479 → 全部 W 版） | ❌ 仅 UTF-8；`MethodMap.Marshal.cs:110` 明确 "PtrToStringUni not wired"（无 UTF-16 解码）；无 to_utf16_alloc | **大缺口** |
| 7 | **结构体 by-value/by-ref**（MSG/RECT/POINT/WNDCLASS/CREATESTRUCT/…） | ❌ PinvokeParamList 只认 "int32_t"/"const char*"，其余一律 int32_t（CodeGenerator.cs:2969-2970）；PinvokeReturnTypeCpp 非 IntPtr 恒 int32_t | **大缺口** |
| 8 | **IntPtr 参数**（hWnd 全部 user32 API） | ❌ 参数侧无 "ptr" 处理（PinvokeParamList 无 IntPtr 分支，x64 截断）——返回侧已修（M4.5），参数侧未修 | **大缺口** |
| 9 | **bool/char/byte 数组 marshal**（GetKeyboardState(byte[256])、SendMessage wParam/lParam 位域、GetWindowText 缓冲） | ❌ 数组参数无 marshal（数组域只支持 int32[]/ref[]，`docs/design/linq-domain.md:40`）；无 buffer 引用参数语义 | **大缺口** |
| 10 | **委托 → 函数指针回调**（WindowProc/EnumWindows/钩子） | 🟡 icall_pinvoke_register_callback/find_callback 已存在（il2cpp_pinvoke_ext.h:77-80）；GetFunctionPointerForDelegate 施工单 #11（P1，`docs/alignment-tracker-v3.md:121`）；delegate 域已接线 | **可接线** |
| 11 | 事件（add_/remove_ accessor，Click/Paint 等） | 🟡 无专门事件机制，但事件字段=委托字段、accessor=普通方法（grep 仅发现 add_subdirectory 等，无事件专属处理）→ 委托链路已通，转译语义可行 | **可接线** |
| 12 | 虚方法/接口（Control.WndProc virtual、IWin32Window） | ✅ virtual 域、interface 域已接线（alignment-tracker-v3 §1） | **已覆盖** |
| 13 | 泛型（EventHandler\<T\>、List\<Control\>） | 🟡 generics e2e PASS 但受限（Matrix 5 泛型修复 b59c6d9）；泛型 string 展开 #20 在途 | **可接线**（风险） |
| 14 | 线程（Application.Run 主线程、Control.BeginInvoke 跨线程） | ✅ thread/threadobj/threadpool 域全接（alignment-tracker-v3:28,57）；跨线程 Invoke 需要消息泵投递（SendMessage/PostMessage 通路） | **可接线** |
| 15 | **嵌入资源 223 个**（字符串/位图/对话框模板） | ❌ 转译器无 ManifestResource 解码；无 ResourceManager icall | **大缺口** |
| 16 | System.ComponentModel（TypeDescriptor/PropertyDescriptor/ISite） | ❌ 反射/类型描述符域未建（reflect 域为 mode B 基础反射，`alignment-tracker-v3 §1.2`） | **大缺口**（阶段 2+ 遇阻点） |
| 17 | 反射（WinForms 大量 Type.GetProperty/Attribute 查询） | 🟡 reflect_base/typeinfo 部分接线（mode B 反射 CLI 落地 81d25b6）；Attribute 域已接 | **可接线**（浅层） |
| 18 | **COM 全量**（OLE 拖放/ActiveX/剪贴板/文件对话框/WebBrowser） | ❌ 无 COM 运行时（M8 远期，`docs/architecture/路线图.md:37`；spec 21 项） | **大缺口**（阶段 3） |
| 19 | uxtheme 视觉样式（31 个） | ❌ 无 | 大缺口（可禁用：AppContext/配置关主题） |
| 20 | GDI+（System.Drawing Graphics 域） | ⚠ 相邻依赖面：Color/Point/Size/Rectangle/Pen/Brush/Font 纯托管可转译；Graphics/Icon/Image → gdiplus.dll P/Invoke，未在本次枚举范围 | **待单独调研** |

**汇总：已覆盖 5 项（#1-5、12）｜可接线 6 项（#10-11、13-14、17）｜大缺口 7 项（#6-9、15-16、18-19）｜相邻 1 项（#20）。**

### 2.1 关键结论

1. **架构判断：WinForms 是纯托管 BCL 域**——消息泵、控件树、事件分发全是 C# IL（ThreadContext/Control/NativeWindow），ILC 只需转译 System.Windows.Forms.dll + System.Drawing.dll 源码并打通 P/Invoke。**不需要原生 UI 层**，这是最大利好。
2. **阶段 1 的 5 个大缺口集中在 P/Invoke marshal 面**（UTF-16、结构体、IntPtr 参数、数组参数、委托回调）——全部是 il2cpp_pinvoke_* 扩展层可解的问题，不触及 GC/对象模型。
3. 嵌入资源（#15）与 COM（#18）可分别后置：阶段 1 控件文本用代码赋值（不依赖资源表）即可；对话框/拖放/剪贴板属阶段 3。
4. 无头验收可行：ILC 产物是原生 Windows exe，可在无交互窗口站创建隐藏窗口 + PostMessage 驱动消息泵 + 托管状态 dump。

---

## 3. 阶段化路线

### 阶段 1：最小消息泵 + Form + Button + Label + 简单绘图

**目标**：`Application.Run(new Form())` + Button/Label + Click 事件 + WM_PAINT 矩形/文本绘制，双跑（net48 vs ILC）结构化断言一致。

**转译面**（System.Windows.Forms 子集，估计 ~60 类型）：Application、ThreadContext（消息泵）、NativeWindow、Control、Form、Button、ButtonBase、Label、Message/MessageBox（部分）、PaintEventArgs、ControlEventArgs、IWin32Window、委托/事件定义类型。

**P/Invoke 接线清单（ILC 运行时扩展，全部新增到 il2cpp_pinvoke 层）**：
- user32（~45）：RegisterClassW、CreateWindowExW、DefWindowProcW、GetMessageW、PeekMessageW、TranslateMessage、DispatchMessageW、SendMessageW、PostMessageW、SetWindowPos、ShowWindow、UpdateWindow、InvalidateRect、GetClientRect、GetDC、ReleaseDC、BeginPaint、EndPaint、LoadCursorW、LoadIconW、LoadImageW、GetSystemMetrics、SystemParametersInfoW、GetWindowLongW/GetWindowLongPtrW、SetWindowLongW/SetWindowLongPtrW、GetWindowTextW、SetWindowTextW、GetClassNameW、IsWindow、GetWindowRect、ClientToScreen、ScreenToClient、SetTimer、KillTimer、GetFocus、SetFocus、GetMessagePos、GetAsyncKeyState、GetKeyState、GetKeyboardState、SetCursor、GetStockObject、DestroyWindow、GetModuleHandleW、GetCurrentThreadId
- gdi32（~20）：CreateSolidBrush、CreatePen、CreateFontIndirectW、SelectObject、DeleteObject、TextOutW、DrawTextW、GetTextExtentPoint32W、SetBkColor、SetTextColor、SetBkMode、FillRect、MoveToEx、LineTo、Rectangle、Ellipse、GetDeviceCaps、CreateCompatibleDC、DeleteDC
- kernel32（~8）：GetModuleHandleW、GetProcAddress、LoadLibraryW、FreeLibrary、GetCurrentThreadId、GetUserDefaultLCID、LocalFree、GlobalFree

**ILC 运行时前置（本阶段必做，估计工作量排序）**：
1. 结构体 marshal：by-value/by-ref 参数 + 返回值（MSG/POINT/RECT/WNDCLASS/CREATESTRUCT/PAINTSTRUCT）——发射端 PinvokeParamList/Return 扩展 + 运行时 LayoutKind.Sequential 布局表
2. UTF-16 marshal：`il2cpp_pinvoke_string_to_utf16_alloc` / `from_utf16`（镜像 UTF-8 层）；PtrToStringUni 补线（MethodMap.Marshal.cs:110 缺口）
3. IntPtr 参数：参数侧 "ptr" result kind 扩展（PinvokeParamList IntPtr → intptr_t）
4. 委托回调：WindowProc thunk（icall_pinvoke_register_callback 通路 + 施工单 #11 GetFunctionPointerForDelegate 落地）
5. byte[] 引用参数（GetKeyboardState 256 字节缓冲）→ 数组 pin 或专用 icall
6. 消息泵入口：ThreadContext IL 转译即可（无需 icall）；如遇复杂 IL 形态（ref 结构 Message by-ref），需 ref/out 里程碑配合（P2，#27 TryParse ref/out 同源）

**阶段 1 验收**：见 §4。控件文本不走资源表（代码赋值），避开 #15。

### 阶段 2：常用控件

**目标**：TextBox/ComboBox/CheckBox/RadioButton/Panel/GroupBox/TabControl/ListView/TreeView/StatusStrip/MenuStrip/ToolStrip/ProgressBar/TrackBar/Timer/NotifyIcon/PictureBox + 文件对话框（comdlg32 路径）。

**新增依赖**：
- comctl32（20，ListView/TreeView/StatusBar 消息）、shell32（12）、comdlg32（9，GetOpenFileNameW/GetSaveFileNameW/ChooseColorW/ChooseFontW）、imm32（10，IME——可先忽略/禁用）、crypt32（5，ProtectedData 等，可后置）
- uxtheme（31）：**建议阶段 2 以禁用主题（FlatStyle/视觉样式关闭）或直通 DrawThemeBackground 双方案并进**；禁主题可先跑通功能面
- 剪贴板（user32 现有面：OpenClipboard/GetClipboardData/SetClipboardData + ole32 格式）
- System.ComponentModel 浅层（ISite/Component）：需要 ComponentModel 域基础——TypeDescriptor 全量放阶段 3

**ILC 前置**：控件状态 dump icall（验收基建，§4.3）；Clipboard 数据格式转换（byte[]/string 双 marshall）；若遇 PropertyDescriptor 依赖（DataBinding）→ 归阶段 3。

### 阶段 3：全量

**目标**：DataGridView/RichTextBox/MDI/拖放/剪贴板 COM 格式/打印/WebBrowser（可选）/设计时无关全控件。

**新增依赖**：
- ole32（24）+ oleaut32（17）+ 60 个 OLE/ActiveX COM 接口 → **COM 运行时里程碑（M8，`docs/architecture/路线图.md:37`：RCW/CCW/IDispatch/HRESULT↔异常）**——拖放（RegisterDragDrop/DoDragDrop + IOleDropTarget 回调）、RichTextBox OLE（IRichEditOleCallback）、WebBrowser/AxHost（IWebBrowser2）
- UIAutomationCore（9）+ Accessibility：可声明为"无障碍接口缺省 NotSupported"（loud）与 net48 差异白名单
- FileDialogNative（10）：IFileDialog（Vista 对话框）；或全程 comdlg32 旧路径兜底
- TypeDescriptor/PropertyDescriptor 反射面（DataBinding/DataSource）：需要反射增强里程碑
- 嵌入资源 223 个：ResourceManager icall + ManifestResource 解码（转译器 + 运行时双向）——控件默认图标/字符串对齐
- hhctrl.ocx（HtmlHelp）：可选，可 loud 拒绝

**阶段 3 验收**：控件全集 e2e 矩阵（无头）+ 真机截图基线 + net48 结构化 diff 白名单。

---

## 4. 验收方案

### 4.1 无头环境（CI/矩阵模式，主验收路径）

ILC 产物为原生 Windows exe，无桌面会话（服务/非交互窗口站）下仍可创建窗口与消息泵。用例模板：

1. 构造 Form + Button + Label（代码赋值文本，不依赖资源）
2. **不调用 Show**，仅创建句柄（Handle 触达）：`form.CreateControl()` / 显式 Handle 访问 → 验证 `Handle != 0`
3. 驱动消息泵：`Application.DoEvents()` 或后台线程 `Application.Run(form)` + 主线程 PostMessage 注入
4. 模拟交互：`button.PerformClick()` → 断言 Click 计数 == 1；`button.Text = ...` → 断言 label 联动
5. 绘图验证：`form.CreateGraphics()` + 简单绘制 + `PrintWindow` 或 BitBlt 到 DIB → **像素校验跳过**，改为：WM_PAINT 回调计数 + GDI 对象创建/删除计数对称（无泄漏）
6. 退出：`form.Close()` → 消息泵退出 → 进程正常返回 0
7. **控件状态 dump**（见 4.3）与 net48 参考运行输出做字段级白名单 diff

判定：转译 0 错 0 警 + 构建通过 + dump 断言全绿 + 无句柄泄漏（GetGuiResources 前后差）即 PASS。

### 4.2 Windows 真机（人工/脚本验收）

1. `ShowDialog` 模态运行 + 截图（窗口 DC → BMP）与 net48 参考截图像素 diff（容忍主题差异：强制 FlatStyle 基线）
2. 输入注入脚本：PostMessage/SendInput 注入 WM_LBUTTONDOWN/WM_KEYDOWN → 断言焦点/文本变化
3. 双跑对照：同一用例 exe（ILC 产物 vs net48 csc 产物）在真机并排运行，肉眼/截图对比

### 4.3 控件状态 dump（验收基建，阶段 1 就建）

新增运行时 icall（如 `il2cpp_wf_dump_control(Il2CppObject* control)`）或生成代码直接读托管字段，输出 JSON 行：
`{type, text, location, size, visible, enabled, handle, controlsCount, focused, parentChain}` —— 递归控件树。
net48 参考侧：同用例用 C# 反射打印等价 JSON。diff 白名单字段（Handle 值、坐标可含主题偏移，默认排除，仅比相对结构/文本/可见性/计数）。

### 4.4 风险与缓解

| 风险 | 缓解 |
|---|---|
| System.Drawing（gdiplus.dll）依赖未枚举 | 阶段 1 绘图用 GDI 直通（P/Invoke），Graphics 域作为阶段 1.5 专项调研；Color/Point/Size 纯托管无碍 |
| ref 结构体（Message by-ref、ref 参数）超出 P/Invoke 引用参数现状 | ref/out 里程碑（#27）与阶段 1 消息泵并行排期 |
| 转译体量（2442 类型）与泛型限制 | 按阶段裁剪转译子集；泛型失败 loud 拒绝；逐类型白名单 |
| 消息泵死循环/挂起 | 无头用例加超时哨兵（10s 无消息 → 强制退出 + FAIL） |
| 主题/DPI 差异 | 阶段 2 前禁用视觉样式 + DPI 感知关闭（SetProcessDPIAware 直通） |

---

## 5. 结论

- **可行**：WinForms 为纯托管域，阶段 1 可行性门槛 = 5 个 P/Invoke marshal 缺口（UTF-16、结构体、IntPtr 参数、byte[] 参数、委托回调），全部落在 il2cpp_pinvoke 扩展层，不涉 GC/对象模型；已覆盖基座（绑定/SetLastError/IntPtr 返回/UTF-8/委托/虚方法/线程）足够起步。
- **不可行项（远期）**：COM 全量（OLE/ActiveX/拖放/WebBrowser）依赖 M8 COM 里程碑；TypeDescriptor 反射面依赖反射增强；嵌入资源依赖 ResourceManager icall——均建议阶段 2+ 排期。
- 建议立即开工项：P/Invoke 结构体 + UTF-16 marshal 扩展（阶段 1 前置 1-2），可先行于 WinForms 域独立成单。

---

## 附：证据索引

- `%TEMP%\opencode\winforms_survey_net48_out.txt` — net48 反射枚举原始输出（625 P/Invoke 全清单、COM 198、消息循环 149、控件树、资源 223）
- `%TEMP%\opencode\winforms_survey.ps1` / `winforms_survey_net48.ps1` — 枚举脚本（可复跑）
- E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:379-388（P/Invoke 静态绑定）、2958-2971（返回/参数类型白名单）
- E:\il2cpp\src\il2cppc\Emit\MethodRefResolver.cs:285-304（P/Invoke 解析 + "ptr" result kind）
- E:\il2cpp\src\il2cppc\Emit\MethodMap.Marshal.cs:110（PtrToStringUni not wired）
- E:\il2cpp\src\runtime\include\internal\il2cpp_pinvoke.h / il2cpp_pinvoke_ext.h（现有 marshal 面）
- E:\il2cpp\docs\alignment-tracker-v3.md:22,33,81,121-127（Marshal/PInvoke/COM 状态）
- E:\il2cpp\docs\architecture\路线图.md:37（M8 COM 远期）
- E:\il2cpp\docs\design\linq-domain.md:40（newarr 数组族限制）
