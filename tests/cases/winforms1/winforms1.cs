// winforms1.cs -- WinForms stage-2: ShowWindow + WM_PAINT/GDI drawing +
// Button Click event, all through raw user32/gdi32 P/Invoke (the net48
// reference oracle runs the SAME source against real Windows).
//
// Stage 1 (base): RegisterClassExW + WNDPROC delegate + CreateWindowExW
// form/child controls + GetMessageW pump + PostMessage injection + close.
// Stage 2 adds (winforms-plan.md stage-1/2 lists):
//   * ShowWindow (SW_SHOW -> visible; headless acceptance ends HIDDEN)
//   * WM_PAINT handling in the WNDPROC delegate: BeginPaint/TextOutW/EndPaint
//     (GDI drawing, gdi32) -- injected via PostMessage with a marker so the
//     count is deterministic even if the system synthesizes its own paint
//   * Button Click event: WM_COMMAND -> a managed delegate callback
//     (ButtonClick) invoked from the WNDPROC handler
//   * net48 oracle: window title readback (GetWindowTextW into an
//     AllocHGlobal buffer + PtrToStringUni) + drawn-text output
// Stage 4 adds (winforms-plan.md stage-4 list, this file's Phase 4 block):
//   * Managed event system: a WfControl class with field-backed Click/KeyDown
//     events (custom add/remove accessors -> Delegate.Combine/Remove, the
//     delechain-proven multicast path). Button.Click += handler-style multicast,
//     multi-control per-handler dispatch, shared handlers across controls,
//     unsubscribe (-=), and a trigger-order sequence string.
//   * WM_COMMAND dispatch by control ID (LOWORD(wParam)) -> the matching
//     control's Click event; WM_KEYDOWN (VK_F2 marker) -> KeyDown events.
//   * Headless acceptance: PostMessage WM_COMMAND/WM_KEYDOWN + PeekMessageW
//     drain, output sequence (p4_seq / p4_seq2) byte-exact across net48 + both
//     transpiled compilers.
// Stage 5 adds (this file's Phase 5 block):
//   * Multi-window: a SECOND Form (child) -- an independent CreateWindowExW
//     HWND with its OWN registered window class + its OWN WndProc delegate
//     (RootChildWndProc); a posted message to the child HWND is dispatched by
//     the child's proc (independent message handling for the two windows).
//   * Layout: MoveWindow (position/size) + GetWindowRect (geometry query via
//     an out RECT struct); child sizes are exact, positions print as DELTAS
//     (deterministic across processes regardless of screen origin).
//   * Window relationship: WS_CHILD + hwndParent -> GetParent(button/label/
//     child) == main form; GetParent(top-level form) == NULL (no owner).
//   * Headless acceptance: PostMessage WF_CHILD_MSG/WM_CLOSE to the child +
//     PeekMessageW drain, output byte-exact across net48 + both compilers.
// Stage 6 adds (this file's Phase 6 block):
//   * MessageBoxW (user32): a BLOCKING message box shown on a WORKER thread;
//     the main thread finds the dialog (FindWindowW, dialog class "#32770")
//     and dismisses it (FindWindowExW locates the "Button" child -> a posted
//     BM_CLICK, the same technique UI-automation frameworks use). Headless
//     acceptance: MB_OK returns IDOK (no user click needed).
//   * Modal window: EnableWindow disables the parent Form; a nested BLOCKING
//     GetMessageW pump (the simplified modal loop) dispatches until the modal
//     window is destroyed (WM_CLOSE posted); the parent is re-enabled after
//     the loop exits -- the WinForms ShowDialog modal pattern, simplified.
//   * net48 oracle: the SAME source runs against real Windows (the message
//     box + modal sequence is identical), output byte-exact on both sides.
// Stage 7 adds (this file's Phase 7 block):
//   * Property-based control access: Text (WM_SETTEXT + GetWindowTextW),
//     Visible (ShowWindow + IsWindowVisible) and Enabled (EnableWindow +
//     IsWindowEnabled) exposed as C# PROPERTIES (get/set) on a WfPropControl
//     wrapper -- the WinForms control-property surface, managed-side.
//   * Click event + Enabled toggle combination: the WM_COMMAND dispatch
//     raises Click through RaiseClick which GATES on the Enabled property (a
//     disabled control receives no click -- the WinForms rule), and the Click
//     handler toggles the very Enabled it reads.
//   * net48 oracle: property sequences (Text get/set round-trips, Visible
//     transitions, Enabled transitions, click-toggle sequence) byte-exact.
//   * Headless acceptance: PostMessage WM_COMMAND by control id + PeekMessageW
//     drain (phase-4 pattern).
// Stage 8 adds (this file's Phase 8 block):
//   * Dialog controls: a ComboBox (COMBOBOX class, CBS_DROPDOWNLIST) and a
//     ListBox (LISTBOX class, LBS_NOTIFY) child window of the form -- the
//     ListControl surface. Items are added via CB_ADDSTRING / LB_ADDSTRING
//     (each returns the item's zero-based index); the selection is read/set
//     via CB_GETCURSEL/CB_SETCURSEL / LB_GETCURSEL/LB_SETCURSEL (CB_ERR/LB_ERR
//     = -1 when nothing is selected); item text is read back via
//     CB_GETLBTEXT / LB_GETTEXT (IntPtr buffer + PtrToStringUni, the wired
//     marshal path).
//   * Interaction: when the USER changes the selection, the native control
//     sends WM_COMMAND to its parent with HIWORD(wParam) = CBN_SELCHANGE /
//     LBN_SELCHANGE and LOWORD = the control id. The form's WndProc decodes
//     the notification code and dispatches it into the matching control's
//     managed SelectionChanged event (the WinForms SelectedIndexChanged
//     surface, simplified); the handler re-reads the native selection so the
//     sequence proves the event observes the selection at fire time.
//   * net48 oracle: selection sequences (add order / count / sel readback /
//     item-text round-trips / selchange event sequence) byte-exact.
//   * Headless acceptance: PostMessage WM_COMMAND (id | (notif << 16)) +
//     PeekMessageW drain (phase-4 pattern).
// Stage 9 adds (this file's Phase 9 block):
//   * Status-strip controls: a StatusBar (comctl32 "msctls_statusbar32"
//     STATUSCLASSNAME) and a ProgressBar (comctl32 "msctls_progress32"
//     PROGRESS_CLASS) child window of the form -- the WinForms StatusStrip/
//     ProgressBar surface. The classes are registered by InitCommonControlsEx
//     (ICC_BAR_CLASSES | ICC_PROGRESS_CLASS); status text is written/read via
//     SB_SETTEXT / SB_GETTEXT / SB_GETTEXTLENGTH; progress position is
//     read/written via PBM_SETRANGE32 (explicit 0..100 range) + PBM_SETPOS /
//     PBM_GETPOS (PBM_SETPOS returns the PREVIOUS position).
//   * Interaction: a posted WF_PROGRESS_STEP advances the progress bar
//     (PBM_SETPOS) and mirrors the new position into the status bar text
//     (SB_SETTEXT + SB_GETTEXT round-trip) -- "progress drives status", the
//     WinForms progress-reporting pattern, simplified.
//   * net48 oracle: status/progress sequences (range / position transitions /
//     status text round-trips / the step sequence) byte-exact.
//   * Headless acceptance: PostMessage WF_PROGRESS_STEP + PeekMessageW drain
//     (phase-4 pattern); both controls are WS_CHILD of the HIDDEN form.
// Stage 10 adds (this file's Phase 10 block):
//   * Main menu: a MainMenu is a CreateMenu() menu BAR attached to the form
//     with SetMenu (GetMenu reads it back); each top-level MenuItem is a
//     CreatePopupMenu() DROPDOWN appended with AppendMenuW(MF_POPUP);
//     sub-items are string items with an explicit WM_COMMAND id
//     (AppendMenuW MF_STRING). Item counts (GetMenuItemCount) and item-text
//     readback (GetMenuStringW MF_BYPOSITION into an IntPtr buffer) prove
//     the tree shape.
//   * Menu events: clicking a sub-item posts WM_COMMAND to the owner with
//     LOWORD(wParam) = the item id; the form's WndProc decodes the id and
//     raises the matching MenuItem.Click event (the WinForms MenuItem.Click
//     surface, WfMenuItem -- the WfControl multicast event shape).
//   * Status linkage: each Click handler mirrors the clicked item's text into
//     the status bar (SB_SETTEXT + SB_GETTEXT round-trip, stage-9 channel):
//     menu drives status, recorded in an event/status sequence.
//   * Headless acceptance: PostMessage WM_COMMAND by menu item id +
//     PeekMessageW drain (phase-4 pattern), byte-exact across net48 + both
//     compilers. The menu is owned by the (HIDDEN) form and is freed when the
//     form is destroyed (WM_CLOSE) -- no explicit DestroyMenu.
// Stage 11 adds (this file's Phase 11 block):
//   * MDI container (simplified): an MDI FRAME (a top-level window created
//     with WS_CLIPCHILDREN -- the frame never paints over its children, style
//     read back via GetWindowLongW GWL_STYLE). Its child is the MDI CLIENT --
//     a single window that is the PARENT of all MDI children ("multiple
//     children, same parent", the simplified MDICLIENT surface).
//   * Child windows: three MDI children, each its OWN window parented to the
//     client (GetParent proves the container tree: child->client->frame).
//   * Child activation: WM_MDIACTIVATE (0x0222) posted to the CLIENT with
//     wParam = the child being activated; the client's WndProc records the
//     activation ORDER -- the net48 oracle activation sequence.
//   * Multi-child events: each child is a WfMdiChild with its OWN Click event
//     (per-child event delegates); a posted WF_MDI_CHILD_MSG to a child raises
//     ONLY that child's handlers (per-child event isolation).
//   * Headless acceptance: PostMessage WM_MDIACTIVATE / WF_MDI_CHILD_MSG +
//     PeekMessageW drain (phase-4 pattern), byte-exact across net48 + both
//     compilers. All MDI windows are hidden (children of a hidden frame).
// Stage 13 adds (this file's Phase 13 block):
//   * Cross-thread message marshaling (the WinForms "background work must be
//     marshaled to the UI thread" rule): a WORKER thread posts WF_WORKER_MSG
//     (wParam = the step index) to the main form; the MAIN thread's message
//     pump (PeekMessageW drain) dispatches it into the form's WndProc -- which
//     runs on the MAIN thread and updates UI state (the Control.BeginInvoke /
//     PostMessage handoff, simplified).
//   * Thread-safe UI counter: the WndProc counts each marshaled message with
//     Interlocked.Increment; the WORKER counts its own posts the same way --
//     posted count == processed count proves every cross-thread message was
//     handled 1:1 (the "thread-safe UI state" acceptance).
//   * UI update: each marshaled message sets the TextBox text (WM_SETTEXT +
//     GetWindowTextW readback) -- UI state touched ONLY on the UI thread.
//   * Determinism: the worker finishes posting before the main thread drains
//     (Thread.Join -- happens-before), so the FIFO dispatch order is the post
//     order (w0;w1;w2;w3;) byte-exact across net48 + both compilers.
// Stage 14 adds (this file's Phase 14 block):
//   * GDI fonts: CreateFontW (gdi32) creates an HFONT (height -16, regular
//     weight, default face "Arial"); SelectObject selects it into the WM_PAINT
//     DC (returning the PREVIOUS font -- the restore target); DeleteObject
//     frees the font after the restore. The create-select-restore-delete cycle
//     is the "font exists / is selected / is cleaned up" evidence.
//   * DrawTextW (user32): the text-drawing complement to the stage-2 TextOutW.
//     The WM_PAINT handler draws "Wf14Draw" with DT_SINGLELINE | DT_NOCLIP
//     into a RECT; the DrawTextW return (the drawn text height) is a native
//     oracle value.
//   * GetTextExtentPoint32W (gdi32): measures the same string's logical extent
//     (SIZE cx/cy) in the SELECTED font -- the Control.MeasureText surface,
//     simplified.
//   * net48 oracle: the full font/drawing sequence (create;select;draw;extent;
//     restore;delete;) + the draw height + the extent are recorded (p14_seq /
//     p14_draw_height / p14_ext_cx / p14_ext_cy) and byte-exact across net48 +
//     both compilers.
//   * Headless acceptance: the sequence runs inside the WM_PAINT handler driven
//     by the EXISTING injected PostMessage WM_PAINT (the stage-2 marker paint)
//     -- no new message, no visible window.
// Stage 15 adds (this file's Phase 15 block):
//   * Panel container: the WinForms Panel (a child container that OWNS child
//     controls) modeled on a STATIC common control created with WS_CHILD --
//     no custom window class. The STATIC window is SUBCLASSED via
//     SetWindowLongPtrW (GWLP_WNDPROC) so the container gets its OWN managed
//     WndProc (the container's event surface) while still being a plain STATIC
//     child; unhandled messages fall to DefWindowProcW (the same fallback every
//     WndProc in this file uses).
//   * Button inside the container: a BUTTON child is created with the PANEL as
//     its parent (GetParent(button) == panel, NOT the form -- the container
//     owns its child). The button's WM_COMMAND is sent to ITS parent -- the
//     PANEL's proc (a container receives its children's events), which raises
//     the panel's "button clicked" and BUBBLES a custom WF_PANEL_BTN message
//     to the FORM (the nested event chain button -> panel -> form).
//   * Nested events: a posted WF_PANEL_MSG routed to the PANEL's own proc
//     proves the container has an independent event surface; the bubbled
//     WF_PANEL_BTN is received by the FORM's proc. P15Seq records the dispatch
//     order (panel:msg;panel:btn;form:notify;) byte-exact.
//   * Layout: GetWindowRect (exact child sizes) + GetClientRect (client area)
//     + GetParent (the container tree) -- sizes print as deltas, so the output
//     is byte-exact regardless of the screen origin.
//   * Headless acceptance: PostMessage WF_PANEL_MSG / WM_COMMAND to the panel +
//     PeekMessageW drain (phase-4 pattern); all windows stay hidden.
// Stage 16 adds (this file's Phase 16 block):
//   * TabControl (the WinForms TabControl surface, simplified): the comctl32
//     "SysTabControl32" class (WC_TABCONTROL) created with InitCommonControlsEx
//     (ICC_TAB_CLASSES). Pages are added via TCM_INSERTITEMW with a TCITEM --
//     the struct is built field-by-field in a GCHandle-PINNED int[] carrying
//     the native TCITEMW layout (mask @0, pszText @16, lParam @32; the array
//     data is 8-aligned like the struct) -- the wired Marshal surface has no
//     StructureToPtr, so the struct bytes are written directly.
//   * Page list + selection: TCM_GETITEMCOUNT counts the pages; TCM_GETCURSEL /
//     TCM_SETCURSEL read/change the selected tab (the native control inserts
//     each new tab at the FRONT and auto-selects -- a deterministic quirk the
//     oracle records -- so the selection is ALWAYS driven explicitly with
//     TCM_SETCURSEL, which is SILENT: no parent notification).
//   * Page-switch event (TCN_SELCHANGE): a real click sends the parent
//     WM_NOTIFY (wParam = control id, lParam = an NMHDR* whose code is
//     TCN_SELCHANGE). Headless acceptance POSTS WM_NOTIFY to the form (the
//     phase-8 posted-notification pattern); the form's WM_NOTIFY dispatch
//     raises the tab's SelectedIndexChanged event, which RE-READS the current
//     selection via TCM_GETCURSEL -- the event observes the real page at fire
//     time.
//   * Per-page independent content: tab index k owns its OWN content STATIC
//     child of the form (Page0Content / Page1Content / Page2Content).
//     P16ShowPage shows exactly ONE page per selected tab and hides the other
//     two (ShowWindow + style-bit visibility readback -- the form is hidden,
//     so IsWindowVisible's ancestor walk always reports FALSE; the probe reads
//     the window's OWN WS_VISIBLE style bit via GetWindowLongW GWL_STYLE) --
//     the "one page visible per tab" content-switch rule.
//   * net48 oracle: page count / selection round-trips / the page-switch
//     sequence (p16_seq) / per-page visibility -- byte-exact.
//   * Headless acceptance: PostMessage WM_NOTIFY + PeekMessageW drain
//     (phase-4 pattern); the tab + pages are WS_CHILD of the HIDDEN form.
// Stage 17 adds (this file's Phase 17 block):
//   * ScrollBar (the WinForms ScrollBar surface, simplified): the user32
//     built-in "ScrollBar" class control (no InitCommonControlsEx needed).
//     Range: SetScrollRange sets an explicit 0..100 range; position:
//     SetScrollPos / GetScrollPos write/read the thumb. (The legacy
//     SBM_SETRANGE / SBM_SETPOS / SBM_GETPOS control MESSAGES are no-ops on
//     this OS -- probed: the range stays 0..0 and the position stays 0 after
//     sending them -- so the range/position channel uses the DOCUMENTED
//     replacement functions, which is exactly the native state WinForms'
//     VScrollBar drives.) The range is VERIFIED by a CLAMP readback --
//     SetScrollPos(120) clamps to max 100 and SetScrollPos(-5) clamps to min
//     0, each read back via GetScrollPos.
//   * Scroll event (WM_VSCROLL): a real user scroll makes the scrollbar send
//     WM_VSCROLL to its PARENT (the form) with LOWORD(wParam) = the scroll
//     code; a thumb code (SB_THUMBTRACK / SB_THUMBPOSITION) carries the new
//     thumb position in HIWORD(wParam). Headless acceptance POSTS WM_VSCROLL
//     (the phase-8 posted-notification pattern); the form's WM_VSCROLL dispatch
//     writes the thumb, re-reads the real position (GetScrollPos -- the event
//     observes the thumb at fire time) and mirrors it into the status bar text.
//   * Status linkage: each scroll (the posted WF_VSCROLL_STEP position-channel
//     message AND the WM_VSCROLL parent-notification channel) mirrors
//     "Scroll: <pos>" into the stage-9 status bar text (SB_SETTEXT +
//     SB_GETTEXT round-trip) -- "scroll drives status", the same pattern as
//     the stage-9 progress bar.
//   * net48 oracle: range-clamp readbacks / position round-trips / the scroll
//     sequence (p17_seq) / status linkage -- byte-exact.
//   * Headless acceptance: PostMessage WF_VSCROLL_STEP / WM_VSCROLL +
//     PeekMessageW drain (phase-4 pattern); the scrollbar is WS_CHILD of the
//     HIDDEN form.
//
// Headless-safe: NO window is left visible -- ShowWindow exercises the
// SW_SHOW/SW_HIDE transition returns and the window is hidden again before
// the pump (non-interactive window stations can still create windows).
// Bounded pump (200 dispatches) + harness watchdog; exits on WM_QUIT.
// The MessageBoxW worker is a BACKGROUND thread (IsBackground) -- if the box
// were ever not dismissed the blocked worker cannot hang process teardown.
//
// Determinism: handles print as "!= 0" booleans; WM_PAINT is counted ONLY
// when wParam == 0x50A1 (the injected marker) -- any system-synthesized
// WM_PAINT (wParam=0) still gets BeginPaint/EndPaint validation but does not
// affect the count. Delegate/struct/value-type paths are the same IL on both
// sides.
//
// Reference output shape (net48 oracle):
//   module_nonzero=True
//   class_registered=True
//   form_created=True
//   button_created=True
//   label_created=True
//   show_hide1_prev_visible=False  (SW_HIDE on already-hidden window)
//   show_prev_visible=False        (SW_SHOW on hidden window -> now visible)
//   hide2_prev_visible=True        (SW_HIDE on visible window -> hidden again)
//   visible_after=False
//   title_len=7
//   window_title=Wf1Form
//   wm_create_count=1
//   wm_command_count=0
//   inject_command=True
//   inject_paint=True
//   wm_command_count_after=1
//   click_count=1           (ButtonClick delegate invoked from WM_COMMAND)
//   wm_paint_count=1        (injected marker paint handled)
//   begin_paint_ok=True
//   textout_ok=True
//   painted_text=Wf1Draw
//   wm_close_count=1
//   close_destroyed=True
//   pump_exit_reason=quit
//   dump_form_nonzero=True
//   dump_button_nonzero=True
//   dump_label_nonzero=True
//   dump_commands=1
//   dump_clicks=1
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cases { public static class Winforms1 {
    // --- Win32 constants / structs (minimal sequential layouts) ---
    public const int WM_CREATE = 0x0001;
    public const int WM_PAINT = 0x000F;
    public const int WM_CLOSE = 0x0010;
    public const int WM_COMMAND = 0x0111;
    public const int WM_QUIT = 0x0012;

    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;

    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_TABSTOP = 0x00010000;

    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;

    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    // Injected WM_PAINT marker: only paints posted with this wParam are
    // counted (system-synthesized WM_PAINT arrives with wParam = 0).
    public const int PAINT_MARKER = 0x50A1;

    // Stage 3: TextBox (EDIT) / CheckBox (BUTTON + BS_AUTOCHECKBOX) /
    // focus+keyboard messages.
    public const int WM_SETTEXT = 0x000C;
    public const int WM_KEYDOWN = 0x0100;
    public const int BM_GETCHECK = 0x00F0;
    public const int BM_SETCHECK = 0x00F1;
    public const int BS_AUTOCHECKBOX = 0x0003;
    // Phase 18: radio-button group exclusivity. BS_AUTORADIOBUTTON (0x0009)
    // makes a BUTTON a radio; WS_GROUP (0x00020000) marks the FIRST control
    // of a group -- consecutive WS_CHILD controls WITHOUT WS_GROUP join the
    // same group. BM_CLICK (0x00F5) sends a click (auto-radio: checks SELF
    // and CLEARS the rest of the group -- the exclusivity rule).
    public const int BS_AUTORADIOBUTTON = 0x0009;
    public const int WS_GROUP = 0x00020000;
    public const int ES_AUTOHSCROLL = 0x0080;
    public const int PM_REMOVE = 0x0001;
    public const int VK_F1 = 0x0070;
    public const int VK_F2 = 0x0071;

    // Custom application message (WM_APP + 1): the WndProc's posted handler
    // drives the real WM_SETTEXT + GetWindowTextW round-trip on the TextBox.
    // (WM_SETTEXT itself is a synchronous-only message -- PostMessageW returns
    // 0 / error 1159 on real Windows -- so the "post a text-set" acceptance
    // goes through this custom message instead.)
    public const int WF_SET_TEXT = 0x8001;

    // Phase 5: custom message routed to the CHILD window's own WndProc
    // (proves the two Forms have independent message handling).
    public const int WF_CHILD_MSG = 0x8002;

    // Phase 6: MessageBoxW flags / returns. MB_OK = 0 is the simple OK-only
    // box; IDOK = 1 is what MessageBoxW returns when the OK button is
    // selected (or when WM_COMMAND IDOK is posted to the dialog).
    public const int MB_OK = 0x00000000;
    public const int IDOK = 1;

    // Phase 6: the standard system dialog class used by MessageBoxW
    // (FindWindowW locates the box by this class + its caption title).
    public const string DLG_CLASS = "#32770";

    // Phase 6: BM_CLICK -- the button-click message posted to the message
    // box's OK button (found via FindWindowExW) to dismiss it programmatically.
    public const int BM_CLICK = 0x00F5;

    // Stage 8: ComboBox messages (user32 COMBOBOX class). CB_ADDSTRING adds an
    // item (lParam = the string pointer, returns the new item index);
    // CB_GETCURSEL/CB_SETCURSEL read/change the selection; CB_GETCOUNT counts
    // items; CB_GETLBTEXT copies item text into a caller buffer.
public const int CB_ADDSTRING = 0x0143;
    public const int CB_GETCOUNT = 0x0146;
    public const int CB_GETCURSEL = 0x0147;
    public const int CB_GETLBTEXT = 0x0148;
    public const int CB_SETCURSEL = 0x014E;

    // Phase 64: ComboBox dropped-width constants.
    public const int CB_GETDROPPEDWIDTH = 0x015F;
    public const int CB_SETDROPPEDWIDTH = 0x0160;

    // Phase 65: ComboBox item-height constants.
    public const int CB_GETITEMHEIGHT = 0x0154;
    public const int CB_SETITEMHEIGHT = 0x0153;

    // Phase 51: ComboBox dropdown state messages.
    public const int CB_SHOWDROPDOWN = 0x014F;          // wParam = TRUE/FALSE
    public const int CB_GETDROPPEDSTATE = 0x0157;       // BOOL readback
    public const int CB_GETDROPPEDCONTROLRECT = 0x0152; // RECT* readback

    // Stage 8: ListBox messages (user32 LISTBOX class) -- the same shape as
    // the ComboBox set (LB_ADDSTRING / LB_GETCURSEL / LB_SETCURSEL /
    // LB_GETCOUNT / LB_GETTEXT).
    public const int LB_ADDSTRING = 0x0180;
    public const int LB_SETCURSEL = 0x0186;
    public const int LB_GETCURSEL = 0x0188;
    public const int LB_GETTEXT = 0x0189;
    public const int LB_GETCOUNT = 0x018B;

    // Stage 8: selection-change notification codes -- the HIWORD of the
    // WM_COMMAND wParam a ComboBox/ListBox sends its parent when the USER
    // changes the selection (CBN_SELCHANGE / LBN_SELCHANGE). Both are 1.
    public const int CBN_SELCHANGE = 1;
    public const int LBN_SELCHANGE = 1;

    // Stage 8: control styles. CBS_DROPDOWNLIST = a closed combo (the list
    // drops only when opened -- headless-safe); LBS_NOTIFY = the list box
    // sends LBN_* notifications to its parent.
    public const int CBS_DROPDOWNLIST = 0x0003;
    public const int LBS_NOTIFY = 0x0001;

    // Phase 55: LBS_MULTIPLESEL -- the multi-select list box style.
    public const int LBS_MULTIPLESEL = 0x0008;

    // Stage 9: common-control message channels. SB_SETTEXTW writes a status
    // bar part's text (wParam = part index | flags, lParam = string pointer);
    // SB_GETTEXTW copies it into a caller buffer (returns the length);
    // SB_GETTEXTLENGTHW returns the part-text length (LOWORD of the return).
    // NOTE the A/W split: SB_SETTEXTA/W are WM_USER+1/11, SB_GETTEXTA/W are
    // WM_USER+2/13, SB_GETTEXTLENGTHA/W are WM_USER+3/12 -- this test uses the
    // W variants (SendMessageW carries a Unicode string pointer).
    // PBM_SETRANGE32 sets an explicit 32-bit range (wParam = min, lParam =
    // max); PBM_SETPOS/PBM_GETPOS read/write the current position (PBM_SETPOS
    // returns the PREVIOUS position -- deterministic in a sequence).
    public const int SB_SETTEXT = 0x040B;
    public const int SB_GETTEXTLENGTH = 0x040C;
    public const int SB_GETTEXT = 0x040D;

    // Phase 80: StatusBar icon channel (SB_SETICON 0x40F / SB_GETICON 0x40E).
    public const int SB_SETICON = 0x040F;
    public const int SB_GETICON = 0x040E;

    // Phase 82: StatusBar Unicode format (SB_SETUNICODEFORMAT 0x2005 /
    // SB_GETUNICODEFORMAT 0x2006).
    public const int SB_SETUNICODEFORMAT = 0x2005;
    public const int SB_GETUNICODEFORMAT = 0x2006;

    // Phase 89: SB_SIMPLE (0x409) -- toggles the status bar simple mode
    // (wParam = TRUE/FALSE; returns the previous state).
    public const int SB_SIMPLE = 0x0409;

    // Phase 81: SB_GETBORDERS (0x407) -- the status bar border widths (3 ints).
    public const int SB_GETBORDERS = 0x0407;

    public const int PBM_SETRANGE32 = 0x0406;
    public const int PBM_SETPOS = 0x0402;
    public const int PBM_GETPOS = 0x0408;

    // Phase 58: progress-bar state channel (PBM_SETSTATE 0x410 / PBM_GETSTATE
    // 0x411 -- PBS_NORMAL=1 / PBS_ERROR=2 / PBS_PAUSED=3).
    public const int PBM_SETSTATE = 0x0410;
    public const int PBM_GETSTATE = 0x0411;

    // Stage 9: InitCommonControlsEx registration flags -- the status bar
    // class (msctls_statusbar32 / STATUSCLASSNAME, ICC_BAR_CLASSES) and the
    // progress bar class (msctls_progress32 / PROGRESS_CLASS,
    // ICC_PROGRESS_CLASS) live in comctl32 and must be registered before
    // CreateWindowExW can instantiate them.
    public const int ICC_BAR_CLASSES = 0x00000004;
    public const int ICC_PROGRESS_CLASS = 0x00000020;

    // Stage 9: custom posted message -- the WndProc advances the progress bar
    // (PBM_SETPOS) and mirrors the new position into the status bar text
    // (SB_SETTEXT + SB_GETTEXT round-trip): "progress drives status".
    public const int WF_PROGRESS_STEP = 0x8003;

    // Stage 10: menu constants. MF_STRING (0) is a normal text item (the
    // default flag); MF_POPUP marks a DROPDOWN submenu (its handle is passed
    // as the item id); MF_BYPOSITION (0x400) makes GetMenuStringW address
    // items by position instead of by command id. Menu item ids are explicit
    // (0x110/0x111/0x120) so the WM_COMMAND dispatch is deterministic.
    public const int MF_STRING = 0x00000000;
    public const int MF_POPUP = 0x00000010;
    public const int MF_BYPOSITION = 0x00000400;

    // Phase 30: menu-state constants. GetMenuState returns the menu item's
    // flag mask; the state bits are the MF_* flags themselves: MF_GRAYED (1)
    // and MF_DISABLED (2) disable an item, MF_CHECKED (8) marks it checked.
    // MF_BYCOMMAND / MF_ENABLED / MF_UNCHECKED are all 0 -- the default flag
    // values (by-command addressing is the default).
    public const int MF_BYCOMMAND = 0x00000000;
    public const int MF_ENABLED = 0x00000000;
    public const int MF_UNCHECKED = 0x00000000;
    public const int MF_GRAYED = 0x00000001;
    public const int MF_DISABLED = 0x00000002;
    public const int MF_CHECKED = 0x00000008;

    // Phase 31: status-strip part constants. SB_SETPARTS (WM_USER+4, no A/W
    // split) defines the part right-edge array (lParam = a pinned int[]: the
    // LAST edge -1 = "this part stretches to the control's right edge");
    // SB_GETPARTS (WM_USER+6) returns the PART COUNT and copies the right
    // edges back VERBATIM -- the -1 sentinel is preserved, not resolved to a
    // pixel width (net48-probed). The per-part text channel REUSES the
    // stage-9 W-variant constants (SB_SETTEXT=WM_USER+11 etc.) with
    // wParam = part index | flags: the A variants (WM_USER+1..3) would need
    // ANSI read-back (PtrToStringAnsi), absent from the runtime, so the W
    // channel carries the part-index form (probed byte-exact).
    public const int SB_SETPARTS = 0x0404;

    // Phase 57: SB_GETRECT (WM_USER+10) -- writes a part's bounding RECT.
    public const int SB_GETRECT = 0x040A;
    public const int SB_GETPARTS = 0x0406;

    // Phase 31: custom posted message -- the WndProc writes "StatusX" into a
    // status part (wParam = the part index) and reads it back (the StatusStrip
    // part-update interaction, phase-4 post/drain pattern).
    public const int WF_STATUS_UPDATE = 0x802B;

    // Phase 32: ToolStrip constants. WC_TOOLBAR is the comctl32 toolbar class
    // ("ToolbarWindow32", registered by the Phase-9 InitCommonControlsEx
    // ICC_BAR_CLASSES); the TB_* messages go through the existing SendMessageW
    // channel. TBBUTTON is built as a GCHandle-pinned int[8] (x64 32-byte
    // layout, offsetof-probed against net48 Marshal.OffsetOf<TBBUTTON>:
    // iBitmap@0/idCommand@4/fsState@8/fsStyle@9/dwData@16/iString@24). The
    // window creation style is WS_CHILD | CCS_TOP. TBSTYLE_* double as the
    // button styles AND the toolbar window styles.
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

    // Phase 33: DataGridView (simplified) constants. The DataGridView is
    // modeled on the SAME comctl32 "SysListView32" class as the Phase-21
    // ListView (a report-view list -- LVS_REPORT (1) switches the list to the
    // multi-column "report" layout a grid needs), so the WHOLE channel is
    // the Phase-21 one: zero new P/Invoke / shim / struct. The columns go
    // through LVM_INSERTCOLUMNW / LVM_GETCOLUMNW / LVM_SETCOLUMNW with an
    // LVCOLUMNW built as a GCHandle-PINNED int[13] (x64 56-byte layout,
    // offsetof-probed against net48 Marshal.OffsetOf<LVCOLUMNW>: mask@0 /
    // fmt@4 / cx@8 / pszText@16 / cchTextMax@24 / iSubItem@28 / iImage@32 /
    // iOrder@36 / cxMin@40 / cxDefault@44 / cxIdeal@48). CRITICAL: like the
    // Phase-21 LVM_INSERTITEMW, LVM_INSERTCOLUMNW takes the column INDEX in
    // wParam -- the explicit-index append (probed: 0/1/2 return 0/1/2, and
    // passing 0 three times stacks all columns at index 0, reversing them).
    // LVM_GETCOLUMNWIDTH / LVM_SETCOLUMNWIDTH are SCALAR messages (wParam =
    // the column index, returns/accepts the pixel width -- no struct). Grid
    // lines are the LVS_EX_GRIDLINES (1) / LVS_EX_FULLROWSELECT (0x20)
    // extended styles: LVM_SETEXTENDEDLISTVIEWSTYLE / GETEXTENDEDLISTVIEWSTYLE
    // (probed readback 0x21 = 33). The header control count comes from
    // LVM_GETHEADER -> HDM_GETITEMCOUNT (the list-view's OWN header window).
    // LVCF_* are the LVCOLUMNW.mask bits and LVCFMT_* the fmt values (LEFT=0 /
    // RIGHT=1 / CENTER=2); the fmt READBACK is HDF_STRING(0x4000)|alignment
    // (probed: 16384/16386/16385) -- the header stores its own HDF flags.
    // WF_GRID_COLCLICK is the custom posted message (wParam = the clicked
    // column index) the headless run injects to raise ColumnHeaderClick.
    public const int LVS_REPORT = 0x0001;
    public const int LVS_EX_GRIDLINES = 0x0001;
    public const int LVS_EX_FULLROWSELECT = 0x0020;
    public const int LVM_GETCOLUMNW = LVM_FIRST + 95;      // 0x105F
    public const int LVM_SETCOLUMNW = LVM_FIRST + 96;      // 0x1060
    public const int LVM_INSERTCOLUMNW = LVM_FIRST + 97;   // 0x1061
    public const int LVM_GETCOLUMNWIDTH = LVM_FIRST + 29;  // 0x101D
    public const int LVM_SETCOLUMNWIDTH = LVM_FIRST + 30;  // 0x101E
    public const int LVM_GETHEADER = LVM_FIRST + 31;       // 0x101F
    public const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54; // 0x1036
    public const int LVM_GETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 55; // 0x1037
    public const int LVCF_FMT = 0x0001;
    public const int LVCF_WIDTH = 0x0002;
    public const int LVCF_TEXT = 0x0004;
    public const int LVCF_SUBITEM = 0x0008;
    public const int LVCFMT_LEFT = 0;
    public const int LVCFMT_RIGHT = 1;
    public const int LVCFMT_CENTER = 2;
    public const int HDM_FIRST = 0x1200;
    public const int HDM_GETITEMCOUNT = HDM_FIRST + 0;     // 0x1200

    // Phase 66: header item rect (HDM_GETITEMRECT -- the header column's
    // bounding rect, read through LVM_GETHEADER + the header window).
    public const int HDM_GETITEMRECT = HDM_FIRST + 7;  // 0x1207

    // Phase 69: LVS_EX_DOUBLEBUFFER -- the double-buffered (flicker-free)
    // extended style bit, toggled through the existing SET/GETEXTENDED style
    // channel.
    public const int LVS_EX_DOUBLEBUFFER = 0x00010000;

    // Phase 67: header item format (HDM_GETITEM/SETITEM + HDF_SORTUP/DOWN --
    // the sort-arrow fmt bits on the header column).
    public const int HDM_GETITEM = HDM_FIRST + 1;  // 0x1201
    public const int HDM_SETITEM = HDM_FIRST + 2;  // 0x1202
    public const int HDF_SORTUP = 0x0400;
    public const int HDF_SORTDOWN = 0x0200;
    public const int HDI_FORMAT = 0x0004;

    // Phase 48: the list-view sort message (LVM_SORTITEMS, 0x1030) --
    // lParam = a PFNLVCOMPARE funptr from Marshal.GetFunctionPointerForDelegate.
    // (The HDM_SORTITEMS header variant is NOT used -- the LVM channel is the
    // one that fires the callback on comctl32 v5.82, probed.)
    public const int LVM_SORTITEMS = LVM_FIRST + 48;  // 0x1030
    public const int LVM_GETITEMW = LVM_FIRST + 75;   // 0x1075 (LVIF_PARAM readback)
    public const int LVM_FINDITEMW = LVM_FIRST + 83;  // 0x1033 (LVFINDINFO* search)
    public const int LVFI_STRING = 0x0002;                    // match by prefix
    public const int LVSCW_AUTOSIZE = -1;                     // SetColumnWidth auto

    // Phase 83: LVSCW_AUTOSIZE_USEHEADER -- size the column to the header
    // text width (vs the cell-content autosize of Phase 49).
    public const int LVSCW_AUTOSIZE_USEHEADER = -2;

    // Phase 52: ListView colors + per-page count (scalar channels).
    public const int LVM_SETBKCOLOR = LVM_FIRST + 38;    // 0x1026 (wParam = CRGB)
    public const int LVM_GETBKCOLOR = LVM_FIRST + 0;     // 0x1000 (returns CRGB)
    public const int LVM_SETTEXTBKCOLOR = LVM_FIRST + 36; // 0x1024 (wParam = CRGB)
    public const int LVM_GETTEXTBKCOLOR = LVM_FIRST + 35; // 0x1023 (returns CRGB)
    public const int LVM_GETCOUNTPERPAGE = LVM_FIRST + 40; // 0x1028 (returns int)

    // Phase 60: text-color channel (LVM_SETTEXTCOLOR 0x1021 / LVM_GETTEXTCOLOR
    // 0x1022 -- DISTINCT from the text-BACKGROUND channel of Phase 52).
    public const int LVM_SETTEXTCOLOR = LVM_FIRST + 33;  // 0x1021
    public const int LVM_GETTEXTCOLOR = LVM_FIRST + 34;  // 0x1022

    // Phase 72: ListView delete channels (LVM_DELETEITEM 0x1008 /
    // LVM_DELETEALLITEMS 0x1009).
    public const int LVM_DELETEITEM = LVM_FIRST + 8;
    public const int LVM_DELETEALLITEMS = LVM_FIRST + 9;

    // Phase 85: LVNI_FOCUSED -- the focus-item relation of LVM_GETNEXTITEM.
    public const int LVNI_FOCUSED = 0x0002;
    public const int LVM_SETSELECTIONMARK = LVM_FIRST + 47;
    public const int LVM_GETSELECTIONMARK = LVM_FIRST + 50;

    // Phase 53: item geometry + visibility (RECT output / BOOL channels).
    public const int LVM_GETITEMRECT = LVM_FIRST + 14;   // 0x100E (RECT* out, iItem in wParam)
    public const int LVM_ENSUREVISIBLE = LVM_FIRST + 19; // 0x1013 (wParam = iItem, lParam = partial)

    // Phase 59: LVM_GETTOPINDEX (0x1027) -- the index of the first visible row.
    public const int LVM_GETTOPINDEX = LVM_FIRST + 39;

    // Phase 50: image-list surface (LVM_SETIMAGELIST / LVM_GETIMAGELIST).
    public const int LVSIL_SMALL = 1;                 // the small-icon image list
    public const int LVM_SETIMAGELIST = LVM_FIRST + 3;   // 0x1003 (lParam = HIMAGELIST)
    public const int LVM_GETIMAGELIST = LVM_FIRST + 2;   // 0x1002 (returns HIMAGELIST)
    public const int ILC_COLOR32 = 0x00000020;          // ImageList_Create color flags
    public const int ID_GRID33 = 0x200;                    // grid control id
    public const int WF_GRID_COLCLICK = 0x802C;            // custom posted message

    // Phase 34: ProgressBar complete-surface constants. The ProgressBar is the
    // SAME comctl32 "msctls_progress32" class as Phase 9, so the WHOLE channel
    // is the existing SendMessageW one: zero new P/Invoke / shim / struct.
    //   * Step: PBM_SETSTEP / PBM_STEPIT / PBM_GETSTEP. v5.82 net48 probe:
    //     PBM_GETSTEP reads 0 ALWAYS (a dead read -- the field it reads is not
    //     the one PBM_STEPIT advances); PBM_SETSTEP returns the PREVIOUS step
    //     (default 10) and sets the step PBM_STEPIT advances by.
    //   * Range: PBM_SETRANGE32 / PBM_GETRANGE. v5.82 probe: PBM_GETRANGE's
    //     RETURN is the max ONLY (low word = max, high word 0) -- the full
    //     min/max comes through the PBRANGE lParam struct (built as a
    //     GCHandle-PINNED int[2], the LVCOLUMNW/TBBUTTON discipline);
    //     PBM_SETRANGE32 returns the PREVIOUS range packed MAKELONG(min,max)
    //     (LOWORD = min, HIWORD = max -- probed 0x00640000 default 0..100,
    //     0x00960014 for 20..150).
    //   * Colors: PBM_SETBARCOLOR / PBM_SETBKCOLOR return the PREVIOUS color
    //     (probed: the v5.82 default reads back 0xFF000000 -- the "default
    //     color" sentinel, not 0); PBM_GETBARCOLOR is a v6-only message
    //     (v5.82 ignores it -> returns 0).
    public const int PBM_SETSTEP = 0x0404;
    public const int PBM_STEPIT = 0x0405;
    public const int PBM_GETRANGE = 0x0407;
    public const int PBM_SETBARCOLOR = 0x0409;
    public const int PBM_GETSTEP = 0x040D;
    public const int PBM_GETBARCOLOR = 0x040F;   // v6-only
    public const int PBM_SETBKCOLOR = 0x2001;
    public const int ID_P34 = 0x210;             // progress bar control id
    public const int WF_P34_STEP = 0x802D;       // custom posted message

    // Phase 35: TabControl item read/write surface constants (the Phase-16
    // SysTabControl32 channel extended -- zero new P/Invoke / shim / struct).
    //   * Item text + param read/write: TCM_GETITEMW / TCM_SETITEMW (the mask
    //     selects the TCITEMW fields; the pinned int[12] layout is the
    //     Phase-16 AddTab one -- pszText pointer @[4..5], cchTextMax @[6],
    //     lParam @[8..9]). TCIF_TEXT marks the text fields; TCIF_PARAM (the
    //     corrected 0x0008 constant above) marks the lParam field.
    //   * Item rect: TCM_GETITEMRECT writes a RECT (pinned int[4], L/T/R/B).
    //   * Rows: TCM_GETROWCOUNT = the tab-control row count (1 for 3 tabs on
    //     one row). WF_TAB35_TEXT is the custom posted message (wParam = the
    //     page index) the pump uses to read a tab's text back.
    public const int TCM_GETITEMW = TCM_FIRST + 60;    // 0x133C
    public const int TCM_SETITEMW = TCM_FIRST + 61;    // 0x133D
    public const int TCM_GETITEMRECT = TCM_FIRST + 10; // 0x130A
    public const int TCM_GETROWCOUNT = TCM_FIRST + 44; // 0x132C
    public const int TCIF_TEXT = 0x0001;
    public const int ID_TAB35 = 0x220;                 // tab control id
    public const int WF_TAB35_TEXT = 0x802E;           // custom posted message

    // Phase 36: Panel scrolling -- WINDOW scroll bars. A WinForms Panel's
    // scroll surface is the WINDOW's own scroll bars: the WS_VSCROLL /
    // WS_HSCROLL style bits (0x00200000 / 0x00100000) tell the system to draw
    // a vertical / horizontal bar on the window's client edge (vs the
    // Phase-17 SCROLLBAR CONTROL, a separate window). The window bars are
    // addressed by nBar = SB_VERT (1) / SB_HORZ (0) in SetScrollRange /
    // SetScrollPos / GetScrollPos (the Phase-17 functions), and ShowScrollBar
    // (nBar = SB_BOTH 3) shows / hides them. WM_VSCROLL (0x115, already
    // defined) / WM_HSCROLL (0x114, already defined) are the parent
    // notifications; LOWORD(wParam) = the scroll code (SB_LINEUP 0 /
    // SB_LINEDOWN 1 / SB_PAGEUP 2 / SB_PAGEDOWN 3 / SB_THUMBTRACK 5 /
    // SB_THUMBPOSITION 4, the thumb codes carrying the new thumb in
    // HIWORD(wParam)). P36_LINE / P36_PAGE are the line/page scroll deltas
    // (10 / 40, the WinForms SmallChange / LargeChange defaults). ID_P36 is
    // the panel window's control id.
    public const int WS_VSCROLL = 0x00200000;
    public const int WS_HSCROLL = 0x00100000;
    public const int SB_HORZ = 0;
    public const int SB_VERT = 1;
    public const int SB_BOTH = 3;
    public const int SB_LINEUP = 0;
    public const int SB_LINEDOWN = 1;
    public const int SB_PAGEUP = 2;
    public const int SB_PAGEDOWN = 3;
    public const int P36_LINE = 10;
    public const int P36_PAGE = 40;
    public const int ID_P36 = 0x230;                   // panel control id

    // Phase 37: ListBox item-management surface (the Phase-8 ListControl
    // channel extended -- zero new P/Invoke / shim / struct). Phase 8 covered
    // add / select / item-text only; these messages cover the ITEM lifecycle
    // and per-item data:
    //   * LB_INSERTSTRING (0x0181) inserts at an explicit index and returns it
    //     (no LBS_SORT, so the index is the final position -- probed: insert
    //     at 2 into Alpha..Golf returns 2, the text lands exactly at 2).
    //   * LB_DELETESTRING (0x0182) removes an item and returns the REMAINING
    //     item count (7 after removing one of 8 -- the "count after delete"
    //     oracle); the tail shifts up (index 1 reads the old index-2 text).
    //   * LB_RESETCONTENT (0x0184) empties the list (count -> 0).
    //   * LB_GETTEXTLEN (0x018A) returns an item's text length; -1 (LB_ERR)
    //     when the index is out of range (probed: 99 -> -1).
    //   * LB_SETITEMDATA (0x019A) / LB_GETITEMDATA (0x0199) attach a caller
    //     32-bit value to an item. PROBED on net48: LB_SETITEMDATA returns
    //     TRUE (1) on success -- NOT the previous value (the design's prev-
    //     value assumption is superseded by the measured oracle). GetItemData
    //     then reads 42, and after setting 7 reads 7.
    //   * LB_FINDSTRING (0x018F) finds the first item whose text begins with
    //     the given prefix, SEARCHING from index+1 with wraparound (-1 starts
    //     at 0; probed: -1/"Beta" -> 1, 1/"Gamma" -> 3, 0/"Zzz" -> -1, and
    //     prefix "Al" matches "Alpha").
    //   * LB_SETTOPINDEX (0x0197) / LB_GETTOPINDEX (0x018E) scroll the list
    //     to make an item the top row. With LBS_NOINTEGRALHEIGHT (0x0100) the
    //     short 200x60 window accepts a non-integral height so the top index
    //     REALLY scrolls (probed: 0 -> set 3 -> read 3 -> set 0 -> read 0).
    //   * LBN_DBLCLK (2) is the double-click notification the native control
    //     sends its parent as WM_COMMAND HIWORD -- dispatched through the
    //     Phase-8 channel (cid == the control id && notif == LBN_DBLCLK) into
    //     the WfListBox DoubleClick event (the design's dbl-click event).
    public const int LB_INSERTSTRING = 0x0181;
    public const int LB_DELETESTRING = 0x0182;
    public const int LB_RESETCONTENT = 0x0184;
    public const int LB_GETTEXTLEN = 0x018A;
    public const int LB_GETTOPINDEX = 0x018E;
    public const int LB_FINDSTRING = 0x018F;

    // Phase 61: LB_SELECTSTRING (0x18C) -- find (like LB_FINDSTRING) AND
    // select the matching item.
    public const int LB_SELECTSTRING = 0x018C;

    // Phase 62: multi-column list box (LBS_MULTICOLUMN style + width channel).
    public const int LB_SETCOLUMNWIDTH = 0x0195;
    public const int LB_GETCOLUMNWIDTH = 0x0196;
    public const int LBS_MULTICOLUMN = 0x0200;
    public const int LB_SETTOPINDEX = 0x0197;
    public const int LB_GETITEMDATA = 0x0199;
    public const int LB_SETITEMDATA = 0x019A;
    public const int LBN_DBLCLK = 2;
    public const int LBS_NOINTEGRALHEIGHT = 0x0100;
    public const int ID_LIST37 = 0x240;                // listbox control id

    // Phase 38: ComboBox item-management surface (the CB_* mirror of the
    // Phase-37 LB_* set -- a ComboBox exposes the SAME item lifecycle as a
    // ListBox through its own message numbers):
    //   * CB_INSERTSTRING (0x014A) inserts at an explicit index and returns it
    //     (no CBS_SORT, so the index IS the final position -- probed: insert
    //     at 2 into Alpha..Golf returns 2, the text lands exactly at 2).
    //   * CB_DELETESTRING (0x0144) removes an item and returns the REMAINING
    //     item count (7 after removing one of 8 -- the "count after delete"
    //     oracle); the tail shifts up (index 1 reads the old index-2 text).
    //   * CB_RESETCONTENT (0x014B) empties the list (count -> 0).
    //   * CB_GETLBTEXTLEN (0x0149) returns an item's text length; -1 (CB_ERR)
    //     when the index is out of range (probed: 99 -> -1).
    //   * CB_SETITEMDATA (0x0151) / CB_GETITEMDATA (0x0150) attach a caller
    //     32-bit value to an item. PROBED on net48: CB_SETITEMDATA returns
    //     TRUE (1) on success -- the same measured oracle as LB_SETITEMDATA.
    //   * CB_FINDSTRING (0x014C) finds the first item whose text begins with
    //     the given prefix, SEARCHING from index+1 with wraparound (-1 starts
    //     at 0; probed: -1/"Beta" -> 1, 1/"Gamma" -> 3, 0/"Zzz" -> -1).
    //   * CB_SETTOPINDEX (0x015C) / CB_GETTOPINDEX (0x015B) scroll the list.
    //     PROBED on net48: a CLOSED CBS_DROPDOWNLIST combo DOES read back the
    //     stored top index (0 -> set 3 -> read 3 -> set 0 -> read 0) -- the
    //     short 160x40 window stores the scroll position without a drop.
    //   * CBN_DBLCLK (2) is the double-click notification the native control
    //     sends its parent as WM_COMMAND HIWORD (the same value as LBN_DBLCLK
    //     -- the notification code constants are shared).
    public const int CB_DELETESTRING = 0x0144;
    public const int CB_GETLBTEXTLEN = 0x0149;
    public const int CB_INSERTSTRING = 0x014A;
    public const int CB_RESETCONTENT = 0x014B;
    public const int CB_FINDSTRING = 0x014C;
    public const int CB_GETITEMDATA = 0x0150;
    public const int CB_SETITEMDATA = 0x0151;
    public const int CB_GETTOPINDEX = 0x015B;
    public const int CB_SETTOPINDEX = 0x015C;
    public const int CBN_DBLCLK = 2;
    public const int ID_COMBO38 = 0x250;               // combobox control id

    // Phase 39: ListView checkbox + multi-select state surface (the Phase-21
    // SysListView32 channel extended -- zero new P/Invoke / shim / struct).
    //   * LVS_EX_CHECKBOXES (4) is the extended style that makes every item
    //     show a checkbox (set via the Phase-33 LVM_SETEXTENDEDLISTVIEWSTYLE;
    //     the setter returns the PREVIOUS extended style -- probed prev 0).
    //   * LVIS_STATEIMAGEMASK (0xF000) masks the STATE-IMAGE bits of a row's
    //     state (the checkbox on/off): state image n (n=1 unchecked, n=2
    //     checked, n=3 grayed, 0 = no image) is stored as (n << 12) --
    //     INDEXTOSTATEIMAGEMASK. LVM_SETITEMSTATE / LVM_GETITEMSTATE carry the
    //     bits -- the SAME message channel as Phase 21 -- so checkbox and
    //     selection live in INDEPENDENT bits of one int (probed: unselecting a
    //     checked row leaves its checkbox on). PROBED: rows inserted while
    //     LVS_EX_CHECKBOXES is on get state image 1 (read 0 "unchecked").
    //   * LVM_GETSELECTEDCOUNT (0x1032) counts the selected rows as a SCALAR
    //     (PROBED ACCUMULATES: two rows selected -> 2, not an exclusive 1);
    //     LVM_GETNEXTITEM (0x100C) with LVNI_SELECTED (2) walks the selected
    //     rows (start -1 -> each selected index -> -1) -- the enumeration
    //     channel.
    //   * LVM_SETCHECKSTATE (0x105D) / LVM_GETCHECKSTATE (0x105E) are the
    //     v6-ONLY checkbox messages: the v5.82 control IGNORES them (PROBED:
    //     GET reads 0 even on a checked row, SET returns 0 and leaves the real
    //     checkbox unchanged -- the NEGATIVE proof that the v5/v6-shared
    //     LVM_SETITEMSTATE/GETITEMSTATE + LVIS_STATEIMAGEMASK channel is the
    //     real checkbox surface on this OS).
    public const int LVS_EX_CHECKBOXES = 0x0004;
    public const int LVIS_STATEIMAGEMASK = 0xF000;
    public const int LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;   // 0x1032
    public const int LVM_GETNEXTITEM = LVM_FIRST + 12;        // 0x100C
    public const int LVNI_SELECTED = 0x0002;
    public const int LVM_SETCHECKSTATE = 0x105D;              // v6-only
    public const int LVM_GETCHECKSTATE = 0x105E;              // v6-only
    public const int ID_LIST39 = 0x260;                       // listview control id

    // Phase 40: TextBox (EDIT) deep-surface constants -- the EM_* edit-message
    // family (the RichEdit EM_* numbers from Phase 28, applied to the user32
    // EDIT built-in -- a plain EDIT supports all of them). EM_SETSEL (0xB1,
    // already defined) and the WM_GETTEXT/WM_GETTEXTLENGTH/ES_* text channel
    // (already defined) are the Phase-28 shared numbers. The NEW set:
    //   * EM_GETSEL (0xB0) / EM_SETSEL (0xB1) read/write the selection:
    //     EM_GETSEL packs (start | end<<16) in the return value (probed: after
    //     EM_SETSEL(0,-1) on a 19-char text, lo=0 hi=19).
    //   * EM_GETLINECOUNT (0xBA) counts the lines (3 for "LineA\r\nLineB\r\n
    //     LineC"); EM_LINEINDEX (0xBB) maps a line number to its first CHAR
    //     index (0/7/14); EM_LINELENGTH (0xC1) takes a char index and returns
    //     that line's length (5 each); EM_LINEFROMCHAR (0xC9) maps a char index
    //     back to its line (the reverse); EM_GETLINE (0xC4) copies a line into
    //     a caller buffer whose FIRST WORD is the max chars to copy (a pinned
    //     int[0] low WORD -- the wired Marshal surface has no WriteInt16).
    //     net48 PROBED: the control copies the line AT OFFSET 0, OVERWRITING
    //     the length word (so the readback is at offset 0, NOT +2 -- the
    //     design's "+2" assumption is superseded by the measured oracle).
    //   * EM_SCROLL (0xB5) / EM_SCROLLCARET (0xB7) scroll the window (probed
    //     on a hidden edit: scroll 0, scrollcaret 1).
    //   * EM_GETLIMITTEXT (0xD5) / EM_SETLIMITTEXT (0xC5) bound the text length
    //     (multiline default 30000 -- probed; set 20 -> read 20).
    //   * EM_GETMODIFY (0xB8) / EM_SETMODIFY (0xB9) are the dirty-flag channel
    //     (probed: set 1 -> read 1, set 0 -> read 0).
    //   * EM_CANUNDO (0xC6) / EM_UNDO (0xC7) / EM_EMPTYUNDOBUFFER (0xCD) are
    //     the undo channel. net48 PROBED: after WM_SETTEXT, canundo reads 0 (no
    //     undo entry); a keystroke (WM_CHAR 'Z', no focus needed) creates one
    //     (canundo 1); EM_UNDO returns 1 and restores; after undo canundo still
    //     1 (the redo state); EM_EMPTYUNDOBUFFER clears it (canundo 0); EM_UNDO
    //     on the empty buffer returns 0. EM_REPLACESEL does NOT create an undo
    //     entry (probed canundo stays 0) -- the keystroke is the deterministic
    //     way in.
    //   * EM_SETHANDLE (0xBC) / EM_GETHANDLE (0xBD) address the control's text
    //     buffer (multiline-only; gethandle probed nonzero on a fresh edit).
    //   * EN_CHANGE (0x300) is the text-changed notification the EDIT sends its
    //     parent as WM_COMMAND HIWORD (probed: WM_SETTEXT does NOT fire it
    //     synchronously, but a WM_CHAR / EM_UNDO text change DOES -- and the
    //     RichEdit-only EM_SETEVENTMASK is IGNORED by a plain EDIT (probed:
    //     returns 0, EN_CHANGE still fires) -- so the headless event test uses
    //     the DETERMINISTIC posted WM_COMMAND route). WM_CHAR (0x102) is the
    //     keystroke message the undo test injects. ID_EDIT40 is the phase's
    //     control id (the audit's per-phase id convention).
    public const int EM_GETSEL = 0x00B0;
    public const int EM_GETRECT = 0x00B2;
    public const int EM_GETLINECOUNT = 0x00BA;
    public const int EM_GETLINE = 0x00C4;
    public const int EM_LINEFROMCHAR = 0x00C9;
    public const int EM_LINEINDEX = 0x00BB;
    public const int EM_LINELENGTH = 0x00C1;
    public const int EM_SCROLL = 0x00B5;
    public const int EM_SCROLLCARET = 0x00B7;
    public const int EM_GETLIMITTEXT = 0x00D5;
    public const int EM_SETLIMITTEXT = 0x00C5;
    public const int EM_SETMODIFY = 0x00B9;
    public const int EM_GETMODIFY = 0x00B8;
    public const int EM_EMPTYUNDOBUFFER = 0x00CD;
    public const int EM_UNDO = 0x00C7;
    public const int EM_CANUNDO = 0x00C6;
    public const int EM_SETHANDLE = 0x00BC;
    public const int EM_GETHANDLE = 0x00BD;
    public const int EN_CHANGE = 0x0300;
    public const int WM_CHAR = 0x0102;
    public const int ID_EDIT40 = 0x270;                       // edit control id

    // Phase 41: TreeView readback constants -- the TVITEMW item channel Phase 22
    // never touched (it inserts + counts only). TVM_GETITEMW (TVM_FIRST + 62 =
    // 0x113E) / TVM_SETITEMW (TVM_FIRST + 63 = 0x113F) exchange a TVITEMW
    // (lParam = the struct pointer; the design's given 0x110C / 0x110D are the
    // ANSI TVM_GETITEMA / TVM_SETITEMA = TVM_FIRST + 12 / 13 -- corrected here).
    // TVM_GETITEMSTATE (TVM_FIRST + 39 = 0x1127) reads a node's state as a
    // SCALAR (wParam = the node handle, lParam = the state mask) -- net48
    // PROBED: comctl32 IGNORES the lParam mask and returns the FULL item state
    // (after TVM_EXPAND a masked read returns 96 = TVIS_EXPANDED 0x20 |
    // TVIS_EXPANDEDONCE 0x40, after collapse 64 = EXPANDEDONCE), so the wrapper
    // applies the stateMask in MANAGED code (the expand 32 / collapse 0 oracle).
    // TVM_SELECTITEM (TVM_FIRST + 11 = 0x110B) moves the caret (wParam =
    // TVGN_CARET 0x9, lParam = the node; PROBED: works on the hidden tree,
    // TVM_GETNEXTITEM(TVGN_CARET) re-fetches the selected node). TVIF_HANDLE
    // (0x10) / TVIF_STATE (0x8) are the extra TVITEMW.mask bits, TVIS_SELECTED
    // (0x2) / TVIS_BOLD (0x10) / TVIS_EXPANDED (0x20) the state bits, TVE_COLLAPSE
    // (0x1) / TVE_TOGGLE (0x3) the extra TVM_EXPAND actions. ID_TREE41 is the
    // phase's control id. TVIF_PARAM is FIXED in the Phase-22 block below
    // (0x0008 -> 0x0004 -- SDK-verified; the stale 0x0008 is TVIF_STATE).
    public const int TVM_GETITEMW = 0x113E;
    public const int TVM_SETITEMW = 0x113F;
    public const int TVM_GETITEMSTATE = 0x1127;
    public const int TVM_SELECTITEM = 0x110B;

    // Phase 54: ListBox caret + item-rect constants.
    public const int LB_GETCARETINDEX = 0x0192;
    public const int LB_SETCARETINDEX = 0x0193;
    public const int LB_GETITEMRECT = 0x0198;

    // Phase 55: ListBox multi-select constants (LBS_EXTENDEDSEL + the RANGEEX
    // channel -- the WinForms SelectedIndices range surface; probed LB_SELITEMRANGE
    // on a plain LBS_MULTIPLESEL was a no-op headless, the EXTENDEDSEL+
    // RANGEEX pair is the deterministic channel).
    public const int LB_SELITEMRANGEEX = 0x0183; // lParam = MAKELPARAM(start,end)
    public const int LB_GETSELCOUNT = 0x0190;    // returns the selected count
    public const int LB_GETSELITEMS = 0x0191;    // fills int[] with the indices
    public const int LBS_EXTENDEDSEL = 0x0800;   // SHIFT+click multi-select style
    public const int TVIF_HANDLE = 0x0010;
    public const int TVIF_STATE = 0x0008;
    public const int TVGN_CARET = 0x0009;
    public const int TVIS_SELECTED = 0x0002;
    public const int TVIS_BOLD = 0x0010;
    public const int TVIS_EXPANDED = 0x0020;
    public const int TVE_COLLAPSE = 0x0001;
    public const int TVE_TOGGLE = 0x0003;
    public const int ID_TREE41 = 0x280;

    // Phase 42: RichTextBox (RichEdit20W) line-surface constants. The ENM_*
    // event-mask values and the EM_* / WM_GETTEXT* / ES_MULTILINE / WM_CHAR /
    // EN_CHANGE numbers are the Phase-28/40 shared constants -- the ONLY new
    // constant is the phase's control id (the audit's per-phase id convention,
    // after ID_TREE41). ENM_CHANGE (1) is the Phase-28 literal. The rich edit
    // shares the standard edit message numbers.
    public const int ID_RICH42 = 0x290;                       // rich edit control id

    // Phase 43: ListView column-order (DisplayIndex) constants. The columns'
    // DISPLAY order is exchanged as a bare GCHandle-PINNED int[] through
    // LVM_SETCOLUMNORDERARRAY / LVM_GETCOLUMNORDERARRAY (wParam = the column
    // count, lParam = the order array -- order[i] = the COLUMN shown at display
    // position i, probed: after setting {2,0,1} the "Kind" column (index 2) is
    // physically LEFTMOST; the GET reads the SAME array back -- the exact
    // Phase-40 "bare int[] as lParam" shape, but for the DISPLAY order, the
    // backing of WinForms Columns.DisplayIndex / AllowColumnReorder). The
    // control id follows the per-phase convention (after ID_RICH42). The only
    // new constants are these three.
    public const int ID_LIST43 = 0x2A0;                       // list-view control id
    public const int LVM_SETCOLUMNORDERARRAY = LVM_FIRST + 58; // 0x103A
    public const int LVM_GETCOLUMNORDERARRAY = LVM_FIRST + 59; // 0x103B

    // Phase 44: window layout surface constants. WM_GETMINMAXINFO (0x24) is
    // the message DefWindowProcW uses to size a top-level window: the system
    // sends it to the window BEFORE sizing (CreateWindowExW / ShowWindow /
    // MoveWindow) with lParam = a MINMAXINFO* (5 POINTs, 40 bytes on x64).
    // The window's WndProc writes the max/min track size into it and the
    // system enforces them during interactive sizing -- the native backing of
    // WinForms Form.MinimumSize / Form.MaximumSize. The ONLY new constant is
    // this one (after the Phase-43 block, per the audit's per-phase id /
    // constant convention).
    public const int WM_GETMINMAXINFO = 0x0024;

    // Phase 45: TabControl layout-surface constants. TCM_SETITEMSIZE (0x1329)
    // sets the fixed tab-item size (lParam = cx | (cy << 16)) and RETURNS the
    // PREVIOUS size (LOWORD = previous width, HIWORD = previous height) -- the
    // native backing of WinForms TabControl.ItemSize. It only takes effect on
    // TCS_FIXEDWIDTH (0x400) / TCS_OWNERDRAWFIXED controls (probed: on a plain
    // control the item rect is unchanged). TCS_MULTILINE (0x200) lets the tabs
    // wrap onto multiple rows; TCM_GETROWCOUNT (0x132C, already defined) reads
    // the row count back -- the TabControl.Multiline / SizeMode backing. The
    // control id follows the per-phase convention (after ID_LIST43). NOTE:
    // TCM_GETITEMSIZE is a PSEUDO-constant (0x132A = TCM_REMOVEIMAGE) and is
    // NOT used -- the two-step SetItemSize readback is the real channel.
    public const int TCM_SETITEMSIZE = TCM_FIRST + 41; // 0x1329
    public const int TCS_FIXEDWIDTH = 0x0400;
    public const int TCS_MULTILINE = 0x0200;
    public const int ID_TAB45 = 0x2B0;                 // tab control id

    // Phase 48: ListView column sort control id (after ID_TAB45).
    public const int ID_SORT48 = 0x2C0;

    // Phase 49: ListView find control id (after ID_SORT48).
    public const int ID_FIND49 = 0x2C8;

    // Phase 46: OLE drag-drop P/Invoke surface (ole32 -- the EXISTING shim
    // set extended with ole32). OleInitialize initializes OLE on the calling
    // thread (S_OK on an STA thread; RPC_E_CHANGED_MODE (0x80010106 =
    // -2147417850) when the thread is already MTA -- the winforms1 main
    // thread runs as MTA (no explicit CoInitializeEx), so the PROBED net48
    // value is deterministic). RegisterDragDrop registers a window as a drop
    // target (E_INVALIDARG 0x80070057 = -2147024809 for a null/invalid
    // hwnd -- probed); RevokeDragDrop unregisters (DRAGDROP_E_NOTREGISTERED
    // 0x80040202 = -2147221246 for a window never registered -- probed). The
    // full managed dispatch bridge (native IDropTarget -> DragEventArgs ->
    // managed OnDragEnter) is COM stage 17 (test_com section 22); THIS phase
    // pins the ole32 P/Invoke surface itself in the winforms1 e2e.
    public const int OLE32_E_CHANGED_MODE = -2147417850;
    public const int OLE32_E_INVALIDARG = -2147024809;
    public const int OLE32_DRAGDROP_E_NOTREGISTERED = -2147221246;

    // Stage 11: MDI constants. WS_CLIPCHILDREN is the MDI FRAME style -- the
    // frame never paints over its children (DefMDIChildProc requires it); the
    // MDI CLIENT window is the single parent of all MDI children (simplified
    // MDICLIENT surface: "multiple children, same parent"). WM_MDIACTIVATE is
    // the MDI child-activation message (posted to the client, wParam = the
    // child being activated); GWL_STYLE reads a window's creation style back.
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WM_MDIACTIVATE = 0x0222;
    public const int GWL_STYLE = -16;

    // Stage 11: custom posted message routed to an MDI CHILD's own WndProc --
    // raises ONLY that child's Click event (per-child event isolation).
    public const int WF_MDI_CHILD_MSG = 0x8006;

    // Phase 12: clipboard constants. CF_UNICODETEXT (13) is the UTF-16 text
    // clipboard format -- SetClipboardData stores a StringToHGlobalUni handle
    // under it, GetClipboardData returns it back. WF_CLIP_SET / WF_CLIP_GET
    // are the posted messages that drive the copy/paste INSIDE the WndProc
    // handler (OpenClipboard needs the calling thread's message queue -- the
    // phase-4 PostMessage + PeekMessageW drain pattern supplies it).
    public const int CF_UNICODETEXT = 13;
    public const int WF_CLIP_SET = 0x8007;
    public const int WF_CLIP_GET = 0x8008;

    // Phase 13: custom posted message -- the WORKER thread's cross-thread
    // "progress" report to the main form. wParam = the step index (0..n);
    // the form's WndProc (main thread) marshals it into UI state.
    public const int WF_WORKER_MSG = 0x8009;

    // Phase 14: GDI font + DrawText constants. CreateFontW (gdi32) builds a
    // font from 14 scalar params + a face name: a NEGATIVE cHeight (-16) is a
    // character height in logical units, FW_NORMAL (400) is the regular
    // weight, DEFAULT_CHARSET (1) is the system charset, and the remaining
    // precision/quality/pitch/family fields are the zero defaults -- the
    // minimal "make a text font" call. DrawTextW flags (user32): DT_SINGLELINE
    // (0x20) disables wrapping, DT_NOCLIP (0x100) disables clipping -- the
    // headless-safe text-layout flags.
    public const int FONT_HEIGHT = -16;
    public const int FW_NORMAL = 400;
    public const int DEFAULT_CHARSET = 1;
    public const int OUT_DEFAULT_PRECIS = 0;
    public const int CLIP_DEFAULT_PRECIS = 0;
    public const int DEFAULT_QUALITY = 0;
    public const int DEFAULT_PITCH = 0;
    public const int DT_SINGLELINE = 0x00000020;
    public const int DT_NOCLIP = 0x00000100;

    // Phase 15: Panel container constants. GWLP_WNDPROC (-4) is the
    // SetWindowLongPtrW index that REPLACES a window's class proc with a new
    // one (the non-zero return value proves the subclass installed); this is
    // the Win32 subclassing surface a real Panel uses to give the STATIC
    // window its own event handler. WF_PANEL_MSG is a posted message
    // routed to the PANEL's own proc; WF_PANEL_BTN is the event the panel
    // BUBBLES to the form when its child button "clicks"; ID_PANEL_BTN is the
    // button's control id (the LOWORD the WM_COMMAND carries).
    public const int GWLP_WNDPROC = -4;
    public const int WF_PANEL_MSG = 0x800A;
    public const int WF_PANEL_BTN = 0x800B;
    public const int ID_PANEL_BTN = 201;

    // Phase 16: TabControl constants. WC_TABCONTROL is the comctl32 tab class
    // name ("SysTabControl32"); ICC_TAB_CLASSES registers it (InitCommonControlsEx).
    // The tab messages anchor at TCM_FIRST (0x1300): TCM_INSERTITEMW inserts a
    // page (lParam = a TCITEM*), TCM_GETITEMCOUNT counts pages, TCM_GETCURSEL /
    // TCM_SETCURSEL read/change the selection (initial = -1; TCM_SETCURSEL is
    // SILENT -- the control does NOT notify its parent). TCIF_PARAM marks the
    // TCITEM.lParam field valid. A user click sends the parent WM_NOTIFY
    // (wParam = control id, lParam = an NMHDR* whose code is TCN_SELCHANGE).
    // NOTE: TCIF_PARAM was FIXED in Phase 35 (0x0004 -> 0x0008). The stale
    // 0x0004 is TCIF_RTLREADING -- with that mask Phase 16's AddTab NEVER made
    // the control store the lParam. Phase 16 never READS lParam back (no output
    // was polluted), but Phase 35's GetTabParam read-back requires the correct
    // value, so the constant is corrected here (regression-verified).
    public const string WC_TABCONTROL = "SysTabControl32";
    public const int ICC_TAB_CLASSES = 0x00000008;
    public const int WM_NOTIFY = 0x004E;
    public const int TCM_FIRST = 0x1300;
    public const int TCM_GETITEMCOUNT = TCM_FIRST + 4;
    public const int TCM_GETCURSEL = TCM_FIRST + 11;
    public const int TCM_SETCURSEL = TCM_FIRST + 12;
    public const int TCM_INSERTITEMW = TCM_FIRST + 62;
    public const int TCIF_PARAM = 0x0008;   // FIXED 0x0004->0x0008 in Phase 35 (0x0004 = TCIF_RTLREADING)
    public const int TCN_SELCHANGE = -10;
    public const int ID_TAB16 = 301;

    // Phase 17: ScrollBar constants. The "ScrollBar" class is a USER32
    // built-in control (no InitCommonControlsEx needed). The legacy SBM_*
    // scroll-bar CONTROL messages (SBM_SETRANGE / SBM_SETPOS / SBM_GETPOS,
    // WM_USER + 1/2/4) are NO-OPS on modern Windows -- probed on this OS:
    // after sending SBM_SETRANGE(0,100) the range stays 0..0 and after
    // SBM_SETPOS(n) the position stays 0 (GetScrollPos readback), so the
    // range/position channel uses the DOCUMENTED replacement functions the
    // SBM_* docs point to ("Applications should not send this message
    // directly. Instead, they should use the SetScrollRange/SetScrollPos
    // function"): SetScrollRange / SetScrollPos / GetScrollPos (user32) --
    // the exact native state WinForms' VScrollBar drives (probed: GetScrollPos
    // reads 25 after VScrollBar.Value = 25). SetScrollPos CLAMPS to the
    // range (SetScrollPos(120) -> GetScrollPos 100; SetScrollPos(-5) -> 0 --
    // the deterministic range proof) and RETURNS the previous position.
    // WM_VSCROLL (0x115) is the parent notification a VERTICAL scrollbar
    // sends when the USER scrolls: LOWORD(wParam) = the scroll code
    // (SB_THUMBTRACK / SB_THUMBPOSITION carry the new thumb position in
    // HIWORD(wParam)). WF_VSCROLL_STEP is the custom posted message that
    // drives the position channel (wParam = the target position); SBS_VERT is
    // the vertical style and SB_CTL addresses a scroll-bar CONTROL.
    public const string SCROLLBAR_CLASS = "SCROLLBAR";
    public const int SBS_VERT = 0x0001;
    public const int SB_CTL = 0x0002;
    public const int WM_VSCROLL = 0x0115;
    public const int SB_THUMBPOSITION = 4;
    public const int SB_THUMBTRACK = 5;
    public const int WF_VSCROLL_STEP = 0x800C;
    public const int ID_SCROLL17 = 401;
    // Phase 18: radio-button control ids (WM_COMMAND LOWORD routing).
    public const int ID_RADIO1 = 501;
    public const int ID_RADIO2 = 502;
    public const int ID_RADIO3 = 503;
    // Phase 19: GroupBox container constants. BS_GROUPBOX (0x0007) makes a
    // "BUTTON" class window a GROUP BOX -- a WS_CHILD STATIC FRAME that draws
    // a titled box and OWNS the controls placed inside it (the native GroupBox
    // common control; same class + CreateWindowExW path as every other child
    // here). WF_GROUP_MSG is the posted message routed to a GroupBox's OWN
    // subclassed proc; WF_GROUP_NOTIFY is the event the container BUBBLES to
    // the form (groupbox -> form, the phase-15 nested chain). Control ids
    // (0x140..0x145 -- the 0x140 range, clear of the earlier phases' 2xx/3xx/
    // 4xx/5xx ids): ID_GROUP1/ID_GROUP2 for the two boxes and
    // ID_GRADIO_A1..B2 for the two radios inside each box.
    public const int BS_GROUPBOX = 0x0007;
    public const int WF_GROUP_MSG = 0x8004;
    public const int WF_GROUP_NOTIFY = 0x8005;
    public const int ID_GROUP1 = 0x140;
    public const int ID_GROUP2 = 0x141;
    public const int ID_GRADIO_A1 = 0x142;
    public const int ID_GRADIO_A2 = 0x143;
    public const int ID_GRADIO_B1 = 0x144;
    public const int ID_GRADIO_B2 = 0x145;
    // Phase 20: TrackBar constants. The TrackBar is the comctl32
    // "msctls_trackbar32" class -- already registered by the Phase-9
    // InitCommonControlsEx(ICC_BAR_CLASSES) call, so no new registration is
    // needed. The TBM_* messages anchor at TBM_FIRST (0x400, WM_USER + 0):
    // TBM_GETPOS reads the thumb, TBM_SETPOS writes it (wParam = the redraw
    // flag, lParam = the new position; the return value is the PREVIOUS
    // position -- the same shape as PBM_SETPOS), TBM_GETRANGEMIN/MAX read the
    // low/high range end, TBM_SETRANGE sets the whole range (wParam = the
    // redraw flag, lParam = MAKELONG(min, max) -- the LOWORD is the low end,
    // the HIWORD the high end), TBM_GETNUMTICS counts the tick marks,
    // TBM_SETTICFREQ sets the tick frequency (wParam = n: a tick every n
    // units, lParam = the start position), TBM_SETPAGESIZE/TBM_GETPAGESIZE
    // read/write the page size (wParam = unused, lParam = the size) and
    // TBM_GETLINESIZE reads the line size. TBS_AUTOTICKS (0x1) makes the
    // control draw ticks automatically at the TBM_SETTICFREQ frequency.
    // WM_HSCROLL (0x114) is the parent notification a HORIZONTAL scrollbar
    // (and the horizontal TrackBar) sends when the USER scrolls: LOWORD
    // (wParam) = the scroll code (SB_THUMBTRACK / SB_THUMBPOSITION carry the
    // new thumb position in HIWORD(wParam), the Phase-17 rule).
    public const string WC_TRACKBAR = "msctls_trackbar32";
    public const int TBM_FIRST = 0x400;
    public const int TBM_GETPOS = TBM_FIRST;
    public const int TBM_GETRANGEMIN = TBM_FIRST + 1;
    public const int TBM_GETRANGEMAX = TBM_FIRST + 2;
    public const int TBM_SETPOS = TBM_FIRST + 5;
    public const int TBM_SETRANGE = TBM_FIRST + 6;
    public const int TBM_GETNUMTICS = TBM_FIRST + 16;
    public const int TBM_SETTICFREQ = TBM_FIRST + 20;
    public const int TBM_SETPAGESIZE = TBM_FIRST + 21;
    public const int TBM_GETPAGESIZE = TBM_FIRST + 22;
    public const int TBM_GETLINESIZE = TBM_FIRST + 24;
    public const int TBM_SETPOSNOTIFY = TBM_FIRST + 27;
    public const int TBS_AUTOTICKS = 0x0001;
    public const int WM_HSCROLL = 0x0114;
    public const int ID_TRACK20 = 0x150;
    // Phase 21: ListView constants. WC_LISTVIEW is the comctl32 list-view class
    // name ("SysListView32"); ICC_LISTVIEW_CLASSES registers it (the Phase-21
    // InitCommonControlsEx call ORs it with the existing ICC flags). The LVM_*
    // messages anchor at LVM_FIRST (0x1000): LVM_GETITEMCOUNT counts rows,
    // LVM_INSERTITEMW inserts one (lParam = a LVITEMW* -- the comctl32 v5.82
    // control REJECTS iItem = -1 "append" -- probed: it returns -1 and the
    // count does NOT grow; only v6 supports -1, so Insert uses the CURRENT
    // count as the explicit iItem: 0, 1, 2, ... -- the deterministic
    // explicit-index append), LVM_SETITEMCOUNT pre-allocates rows (the
    // ListView.VirtualListSize surface), LVM_GETITEMTEXTW / LVM_SETITEMTEXTW
    // read/write an item's text and LVM_SETITEMSTATE / LVM_GETITEMSTATE
    // set/read a row's state bits. LVIF_TEXT (1) / LVIF_PARAM (4) are the
    // LVITEM.mask fields, LVIS_SELECTED (2) the selection state bit, and
    // LVN_ITEMCHANGED (-101) is the NMHDR code a list-view sends its parent
    // (WM_NOTIFY, wParam = the control id) when the selection changes.
    public const string WC_LISTVIEW = "SysListView32";
    public const int ICC_LISTVIEW_CLASSES = 0x00000001;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_SETITEMCOUNT = LVM_FIRST + 47;
    public const int LVM_GETITEMSTATE = LVM_FIRST + 44;
    public const int LVM_SETITEMSTATE = LVM_FIRST + 43;
    public const int LVM_GETITEMTEXTW = LVM_FIRST + 115;
    public const int LVM_SETITEMTEXTW = LVM_FIRST + 116;
    public const int LVIF_TEXT = 0x0001;
    public const int LVIF_PARAM = 0x0004;

    // Phase 84: LVIF_STATE -- the LVITEM state-field mask bit.
    public const int LVIF_STATE = 0x0008;
    public const int LVIS_SELECTED = 0x0002;
    public const int LVN_ITEMCHANGED = -101;

    // Phase 101: LVN_ITEMCHANGING (-100) -- the NMHDR code before a change.
    public const int LVN_ITEMCHANGING = -100;

    // Phase 90: LVN_BEGINLABELEDITW (-105) -- the NMHDR code for a label
    // edit start. (NOTE: there is no LVN_DBLCLK -- a double-click activates
    // the item via LVN_ITEMACTIVATE (-114, Phase 92); the -105 code here is
    // the label-edit-start notification, used to prove the synchronous
    // notification path.)
    public const int LVN_DBLCLK = -105;

    // Phase 91: LVN_COLUMNCLICK (-108) -- the NMHDR code on a header click.
    public const int LVN_COLUMNCLICK = -108;

    // Phase 92: LVN_ITEMACTIVATE (-114) -- the NMHDR code on item activation.
    public const int LVN_ITEMACTIVATE = -114;

    // Phase 93: LVN_ENDLABELEDITW (-106) -- the NMHDR code on label-edit end.
    public const int LVN_ENDLABELEDITW = -106;

    // Phase 94: LVN_BEGINDRAG (-109) -- the NMHDR code on drag start.
    public const int LVN_BEGINDRAG = -109;

    // Phase 95: LVN_BEGINRDRAG (-110) -- the NMHDR code on right-drag start.
    public const int LVN_BEGINRDRAG = -110;

    // Phase 96: LVN_KEYDOWN (-155) -- the NMHDR code on a list key press.
    public const int LVN_KEYDOWN = -155;

    // Phase 97: LVN_INSERTITEM (-102) -- the NMHDR code on item insert.
    public const int LVN_INSERTITEM = -102;

    // Phase 98: LVN_DELETEITEM (-103) -- the NMHDR code on item delete.
    public const int LVN_DELETEITEM = -103;

    // Phase 99: LVN_DELETEALLITEMS (-104) -- the NMHDR code on delete-all.
    public const int LVN_DELETEALLITEMS = -104;
    public const int ID_LIST21 = 0x160;
    // Phase 22: TreeView constants. WC_TREEVIEW is the comctl32 tree-view class
    // name ("SysTreeView32"); ICC_TREEVIEW_CLASSES registers it (the Phase-22
    // InitCommonControlsEx call ORs it with the existing ICC flags). The TVM_*
    // messages anchor at TVM_FIRST (0x1100): TVM_INSERTITEMW inserts a node
    // (lParam = a TVINSERTSTRUCTW* -- hParent + hInsertAfter + a TVITEMW built
    // in a GCHandle-PINNED int[] with the native x64 layout, the same
    // field-by-field pattern as the Phase-16 TCITEMW / Phase-21 LVITEMW),
    // TVM_GETCOUNT counts nodes, TVM_EXPAND expands/collapses one (wParam = a
    // TVE_* action) and TVM_DELETEITEM removes a node and its subtree (lParam =
    // the node handle). TVIF_TEXT (1) / TVIF_PARAM (4) are the TVITEMW.mask
    // flags and TVGN_ROOT (0) / TVGN_CHILD (4) / TVGN_NEXT (1) the
    // TVM_GETNEXTITEM relations.
    public const string WC_TREEVIEW = "SysTreeView32";
    public const int ICC_TREEVIEW_CLASSES = 0x00000002;
    public const int TVM_FIRST = 0x1100;

    // Phase 102: TVN_SELCHANGED (-402) -- the tree-view selection-change
    // notification code (TVN_FIRST = -400, SELCHANGED = TVN_FIRST - 2).
    public const int TVN_SELCHANGED = -402;

    // Phase 103: TVN_SELCHANGING (-401) -- before the selection changes.
    public const int TVN_SELCHANGING = -401;

    // Phase 104: TVN_ITEMEXPANDED (-406) -- after an item expands.
    public const int TVN_ITEMEXPANDED = -406;

    // Phase 105: TVN_ITEMEXPANDING (-405) -- before an item expands.
    public const int TVN_ITEMEXPANDING = -405;

    // Phase 106: TVN_ITEMCOLLAPSED (-408) -- after an item collapses.
    public const int TVN_ITEMCOLLAPSED = -408;

    // Phase 107: TVN_ITEMCOLLAPSING (-407) -- before an item collapses.
    public const int TVN_ITEMCOLLAPSING = -407;

    // Phase 108: TVN_BEGINLABELEDIT (-410) -- on label-edit start.
    public const int TVN_BEGINLABELEDIT = -410;

    // Phase 109: TVN_ENDLABELEDIT (-411) -- on label-edit end.
    public const int TVN_ENDLABELEDIT = -411;

    // Phase 110: TVN_DELETEITEM (-413) -- on item delete.
    public const int TVN_DELETEITEM = -413;
    public const int TVM_INSERTITEMW = TVM_FIRST + 50;
    public const int TVM_GETCOUNT = TVM_FIRST + 5;
    public const int TVM_EXPAND = TVM_FIRST + 2;
    public const int TVM_DELETEITEM = TVM_FIRST + 1;
    public const int TVM_GETNEXTITEM = TVM_FIRST + 10;
    public const int TVIF_TEXT = 0x0001;
    public const int TVIF_PARAM = 0x0004;   // FIXED 0x0008->0x0004 in Phase 41 (0x0008 = TVIF_STATE; SDK-verified -- probe: lParam stored under mask bit 0x4, NOT 0x8). Phase 22 never read lParam back (no output polluted), regression-verified.
    public const int TVE_EXPAND = 0x0002;
    public const int TVGN_ROOT = 0x0000;
    public const int TVGN_CHILD = 0x0004;
    public const int TVGN_NEXT = 0x0001;
    public const int ID_TREE22 = 0x170;

    // Phase 23: PictureBox constants. SS_BITMAP (0x0E) is the STATIC-class
    // style bit that makes a "STATIC" window display a bitmap (the control
    // then accepts the STM_* image messages); IMAGE_BITMAP (0) is the
    // LoadImageW image type; OBM_CLOSE (32754 = 0x7FF2) is the system
    // "close box" OEM-bitmap ordinal (hinst = NULL + LR_SHARED loads the
    // SHARED stock bitmap -- the documented LR_SHARED requirement for OEM
    // images). STM_SETIMAGE (0x0172) sets the control's bitmap (returns the
    // PREVIOUS bitmap handle -- NULL for a fresh control) and STM_GETIMAGE
    // (0x0173) reads the current handle back.
    public const int SS_BITMAP = 0x0000000E;
    public const int STM_SETIMAGE = 0x0172;
    public const int STM_GETIMAGE = 0x0173;
    public const int IMAGE_BITMAP = 0x00000000;
    public const int LR_SHARED = 0x00008000;
    public const int OBM_CLOSE = 32754;
    public const int ID_PIC23 = 0x180;
    // Phase 24: NotifyIcon constants. NIM_* are the Shell_NotifyIconW message
    // codes (ADD registers a new icon, MODIFY updates it, DELETE removes it).
    // NIF_* are the NOTIFYICONDATAW.uFlags bits (which fields are valid:
    // NIF_MESSAGE = uCallbackMessage, NIF_ICON = hIcon, NIF_STATE =
    // dwState/dwStateMask); NIS_HIDDEN (1) is the single defined state bit --
    // the NotifyIcon.Visible=false surface (a MODIFY with the state mask hides
    // the icon). IDI_APPLICATION (32512) is the system stock application-icon
    // ordinal LoadIconW(hinst = NULL) loads. P24_CB_MSG (0x802A, a WM_APP+
    // range message) is the uCallbackMessage the shell sends to the icon's
    // window when the user interacts with it; ID_ICON24 the icon's uID.
    public const int NIM_ADD = 0x00000000;
    public const int NIM_MODIFY = 0x00000001;
    public const int NIM_DELETE = 0x00000002;
    public const int NIF_MESSAGE = 0x00000001;
    public const int NIF_ICON = 0x00000002;
    public const int NIF_STATE = 0x00000008;
    public const int NIS_HIDDEN = 0x00000001;
    public const int IDI_APPLICATION = 32512;
    public const int P24_CB_MSG = 0x802A;
    public const int ID_ICON24 = 0x190;

    // Phase 25: file-dialog (comdlg32 GetOpenFileNameW) constants. OFN_* are
    // the OPENFILENAMEW.Flags bits (OFN_PATHMUSTEXIST requires the selected
    // path's DIRECTORY to exist, OFN_FILEMUSTEXIST the FILE itself -- both are
    // defined for the plan's flag set but left OFF in the call: probed, they
    // fail the OK click on a name that is not in the dialog's current
    // directory, popping an existence-error box -- the SAME "#32770" + title
    // class -- that deadlocks the synchronous BM_CLICK). P25_TITLE is the
    // dialog caption (lpstrTitle) the main thread matches on -- FindWindowW
    // by "#32770" class + P25_TITLE, a TITLE match being REQUIRED (a
    // class-only match hits unrelated process dialogs, probed). ID_FILE25 is
    // the phase's dialog marker id (the audit's per-phase id convention).
    public const int OFN_PATHMUSTEXIST = 0x00000800;
    public const int OFN_FILEMUSTEXIST = 0x00001000;
    public const int ID_FILE25 = 0x1A0;
    public const string P25_TITLE = "Wf25Open";

    // Phase 26: ColorDialog (comdlg32 ChooseColorW) constants. CC_RGBINIT (1)
    // is the CHOOSECOLORW.Flags bit that makes the dialog initialize the
    // "current color" box with rgbResult -- the preset the dialog returns
    // UNCHANGED when OK is clicked headless (no user interaction). P26_PRESET
    // (0xFF, COLORREF 0x000000FF) is that preset, written to rgbResult@24 and
    // read back after the dialog closes. ID_COLOR26 is the phase's dialog
    // marker id (the audit's per-phase id convention).
    public const int CC_RGBINIT = 0x00000001;
    public const int P26_PRESET = 0x000000FF;
    public const int ID_COLOR26 = 0x1B0;

    // Phase 27: FontDialog (comdlg32 ChooseFontW) constants. CF_INITTOLOGFONTSTRUCT
    // (0x40) is the CHOOSEFONTW.Flags bit that makes the dialog initialize
    // itself from the lpLogFont LOGFONTW -- the worker writes lfHeight=-16 /
    // lfWeight=FW_NORMAL / lfCharSet=DEFAULT_CHARSET / lfFaceName="Arial" and
    // clicking OK immediately returns the (unchanged) selection. ID_FONT27 is
    // the phase's dialog marker id (the audit's per-phase id convention).
    public const int CF_INITTOLOGFONTSTRUCT = 0x00000040;
    public const int ID_FONT27 = 0x1C0;

    // Phase 28: RichTextBox (riched20 RichEdit20W) constants. RICHEDIT_CLASS is
    // the riched20.dll window class (registered when the DLL is loaded with
    // LoadLibraryW -- the class is NOT in user32). ES_MULTILINE (0x4) makes the
    // edit a multi-line rich text control; ES_AUTOVSCROLL (0x40) adds the
    // vertical scrollbar. WM_GETTEXT (0xD) / WM_GETTEXTLENGTH (0xE) are the
    // standard edit text channel (EM_GETTEXTLENGTH == WM_GETTEXTLENGTH -- the
    // rich edit shares the standard edit message numbers). EM_SETSEL (0xB1)
    // selects a character range (wParam = start, lParam = end); EM_REPLACESEL
    // (0xC2) replaces the selection with the lParam string. EM_SETEVENTMASK
    // (0x445) sets which EN_* notifications the control sends its parent --
    // ENM_NONE (0) suppresses them ALL (the headless test sets it FIRST so the
    // WM_SETTEXT/EM_REPLACESEL text changes do NOT perturb the global
    // wmCommandCount). EM_SETCHARFORMAT (0x444) / EM_GETCHARFORMAT (0x43A)
    // set/read the character format of the selection (SCF_SELECTION = 1);
    // CFM_COLOR (== CFE_AUTOCOLOR, 0x40000000) is the dwMask bit that makes
    // crTextColor valid -- dwEffects = 0 CLEARS CFE_AUTOCOLOR so the explicit
    // crTextColor is used. ID_RICH28 is the phase's control id (the audit's
    // per-phase id convention).
    public const string RICHEDIT_CLASS = "RichEdit20W";
    public const int ES_MULTILINE = 0x0004;

    // Phase 70: ES_PASSWORD -- the password edit style.
    public const int ES_PASSWORD = 0x0020;
    public const int ES_AUTOVSCROLL = 0x0040;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int EM_SETSEL = 0x00B1;

    // Phase 68: EM_GETFIRSTVISIBLELINE (0xCE) -- the first visible line index.
    public const int EM_GETFIRSTVISIBLELINE = 0x00CE;

    // Phase 87: EM_LINESCROLL (0xB6) -- scrolls a multi-line edit by
    // (wParam = columns, lParam = lines).
    public const int EM_LINESCROLL = 0x00B6;

    // Phase 70: password-char constants (EM_SETPASSWORDCHAR 0xCC /
    // EM_GETPASSWORDCHAR 0xD2 -- the mask char of a password edit).
    public const int EM_SETPASSWORDCHAR = 0x00CC;
    public const int EM_GETPASSWORDCHAR = 0x00D2;

    // Phase 71: EM_SETREADONLY (0xCF) -- sets/clears the edit read-only flag;
    // the return is the PREVIOUS read-only state.
    public const int EM_SETREADONLY = 0x00CF;

    // Phase 76: edit margins (EM_SETMARGINS 0xD3 / EM_GETMARGINS 0xD4 -- the
    // left/right margin, EC_LEFTMARGIN/EC_RIGHTMARGIN masks).
    public const int EM_SETMARGINS = 0x00D3;
    public const int EM_GETMARGINS = 0x00D4;

    // Phase 77: EM_SETRECT (0xB3) -- the editable formatting rectangle
    // (EM_GETRECT 0xB2 already defined).
    public const int EM_SETRECT = 0x00B3;
    public const int EC_LEFTMARGIN = 0x0001;
    public const int EC_RIGHTMARGIN = 0x0002;
    public const int EM_REPLACESEL = 0x00C2;
    public const int EM_GETCHARFORMAT = 0x043A;
    public const int EM_SETCHARFORMAT = 0x0444;

    // Phase 88: RichEdit background color (EM_SETBKGNDCOLOR 0x443 /
    // EM_GETBKGNDCOLOR 0x442 -- the RichEdit-only background channel).
    public const int EM_SETBKGNDCOLOR = 0x0443;
    public const int EM_GETBKGNDCOLOR = 0x0442;
    public const int EM_SETEVENTMASK = 0x0445;
    public const int ENM_NONE = 0x0000;
    public const int SCF_SELECTION = 0x0001;

    // Phase 78: SCF_DEFAULT (0) -- the all-text/default char-format scope of
    // EM_GETCHARFORMAT.
    public const int SCF_DEFAULT = 0x0000;
    public const int CFM_COLOR = 0x40000000;
    public const int CFE_AUTOCOLOR = 0x40000000;

    // Phase 56: bold character-format bits (CFM_BOLD mask / CFE_BOLD effect).
    public const int CFM_BOLD = 0x00000001;
    public const int CFE_BOLD = 0x00000001;
    public const int ID_RICH28 = 0x1D0;

    // Phase 29: Timer (SetTimer/KillTimer) constants. WM_TIMER (0x113) is the
    // message the system posts to the timer's window when the elapse interval
    // fires (wParam = the timer id, lParam = the timer proc or 0). ID_TIMER29
    // is the phase's timer id (the audit's per-phase id convention, 0x1E0 =
    // 480). The headless test NEVER waits on the real interval -- it injects
    // WM_TIMER directly with SendMessageW (synchronous to the WndProc,
    // bypassing the message queue), so there is no real-time dependency.
    public const int WM_TIMER = 0x0113;
    public const int ID_TIMER29 = 0x1E0;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public int pt_x;
        public int pt_y;
    }

    // PAINTSTRUCT (native x64 layout, flattened -- rgbReserved[32] is the
    // trailing 8 int32 fields). BeginPaint fills the whole structure, so the
    // managed struct must carry the full 68 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT {
        public IntPtr hdc;
        public int fErase;
        public int rcLeft;
        public int rcTop;
        public int rcRight;
        public int rcBottom;
        public int fRestore;
        public int fIncUpdate;
        public int r0;
        public int r1;
        public int r2;
        public int r3;
        public int r4;
        public int r5;
        public int r6;
        public int r7;
    }

    // RECT (native x64 layout: four int32 fields, 16 bytes) -- GetWindowRect
    // fills it. Only the WIDTH/HEIGHT and the position DELTAS are printed so
    // the headless output is deterministic (the screen origin varies by window
    // station, but both runs make the same native calls on the same OS).
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    // SIZE (native x64 layout: two int32 fields, 8 bytes) -- the
    // GetTextExtentPoint32W out-struct: the string's logical width/height in
    // the DC's current font. Only used as an out parameter (phase 14).
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE {
        public int cx;
        public int cy;
    }

    // Stage 9: INITCOMMONCONTROLSEX -- the comctl32 registration structure
    // passed to InitCommonControlsEx (dwSize = sizeof, dwICC = the flag mask
    // OR of the ICC_* classes to register).
    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX {
        public int dwSize;
        public int dwICC;
    }

    public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // Button Click event (stage 2): a managed delegate invoked when the
    // form's WM_COMMAND handler fires (the "button was clicked" callback).
    public delegate void ClickHandler();
    public static ClickHandler ButtonClick;

    // Phase 48 header sort callback: PFNLVCOMPARE
    // (int CALLBACK(lParam1, lParam2, lParamSort)). Carried to comctl32 by
    // Marshal.GetFunctionPointerForDelegate and invoked per comparison.
    public delegate int SortCompare(int l1, int l2, int sort);

    // --- P/Invoke surface (winforms-plan §3 stage-1/2 lists) ---
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);

    [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance,
        IntPtr param);

    [DllImport("user32", EntryPoint = "GetMessageW")]
    public static extern int GetMessageW(out MSG msg, IntPtr hwnd, int min, int max);

    [DllImport("user32", EntryPoint = "TranslateMessage")]
    public static extern int TranslateMessage(ref MSG msg);

    [DllImport("user32", EntryPoint = "DispatchMessageW")]
    public static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // Phase 29: Timer. SetTimer creates a timer on the given window (elapse in
    // ms, timerProc = 0 for the WM_TIMER-to-window form) and returns the timer
    // id (nonzero on success); KillTimer removes it and returns nonzero on
    // success. Both are user32 (the existing shim -- zero new shim).
    [DllImport("user32", EntryPoint = "SetTimer")]
    public static extern IntPtr SetTimer(IntPtr hwnd, int id, int elapse, IntPtr timerProc);

    [DllImport("user32", EntryPoint = "KillTimer")]
    public static extern int KillTimer(IntPtr hwnd, int id);

    [DllImport("user32", EntryPoint = "PostMessageW")]
    public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "PostQuitMessage")]
    public static extern void PostQuitMessage(int exitCode);

    // Stage 2: ShowWindow + visibility probe.
    [DllImport("user32", EntryPoint = "ShowWindow")]
    public static extern int ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32", EntryPoint = "IsWindowVisible")]
    public static extern int IsWindowVisible(IntPtr hwnd);

    // Stage 2: window title readback (IntPtr buffer -- no char[] marshal gap).
    [DllImport("user32", EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowTextW(IntPtr hwnd, IntPtr lpString, int nMaxCount);

    // Stage 3: control messages (WM_SETTEXT / BM_GETCHECK / BM_SETCHECK) go
    // through SendMessageW -- the synchronous control-message channel.
    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // Stage 3: keyboard focus -- SetFocus(GetFocus()) round-trip.
    [DllImport("user32", EntryPoint = "SetFocus")]
    public static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "GetFocus")]
    public static extern IntPtr GetFocus();

    // Stage 3: non-blocking message drain (PM_REMOVE) -- dispatches posted
    // control messages (BM_SETCHECK / WM_KEYDOWN) without a blocking wait.
    [DllImport("user32", EntryPoint = "PeekMessageW")]
    public static extern int PeekMessageW(out MSG msg, IntPtr hwnd, int min, int max, int remove);

    // Stage 5: window relationship (parent chain) + layout (geometry query
    // via GetWindowRect, position/size via MoveWindow). All scalar/IntPtr
    // params (no marshal gaps).
    [DllImport("user32", EntryPoint = "GetParent")]
    public static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "GetWindowRect")]
    public static extern int GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32", EntryPoint = "MoveWindow")]
    public static extern int MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

    // Stage 6: MessageBoxW (user32) -- the simple OK-only message box. The
    // box BLOCKS the calling thread until dismissed; in the headless test the
    // box runs on a worker thread and is dismissed by FindWindowW + a posted
    // WM_COMMAND IDOK (never a user click).
    [DllImport("user32", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hwnd, string text, string caption, int uType);

    // Stage 6: FindWindowW (user32) -- locate the message-box dialog by its
    // standard dialog class "#32770" + the caption (window title).
    [DllImport("user32", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string className, string windowName);

    // Stage 6: GetDlgItem (user32) -- the canonical UI-automation way to
    // dismiss a message box: find the OK button (control id IDOK) and click it
    // via a posted BM_CLICK, exactly like a UI-automation framework does.
    [DllImport("user32", EntryPoint = "GetDlgItem")]
    public static extern IntPtr GetDlgItem(IntPtr hwnd, int id);

    // Stage 6: FindWindowExW -- walk the message box's CHILD windows directly
    // (thread-safe pure handle walk) to find the OK button by its "Button"
    // class. More robust cross-thread than GetDlgItem for a dialog created by
    // another thread.
    [DllImport("user32", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string windowName);

    // Stage 9: common-controls registration (comctl32). The status/progress
    // classes are NOT built into user32 -- they are registered by comctl32
    // when InitCommonControlsEx is called with the ICC_* class flags.
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    // Stage 10: menu functions (user32). CreateMenu makes an empty menu BAR;
    // CreatePopupMenu makes a DROPDOWN submenu; AppendMenuW appends an item
    // (MF_POPUP = a submenu whose handle is the item id; otherwise a string
    // item with an explicit WM_COMMAND id). SetMenu attaches the bar to the
    // form (the window then owns it), GetMenu reads it back, GetMenuItemCount
    // counts items, GetMenuStringW copies an item's text (MF_BYPOSITION) into
    // a caller buffer.
    [DllImport("user32", EntryPoint = "CreateMenu")]
    public static extern IntPtr CreateMenu();

    [DllImport("user32", EntryPoint = "CreatePopupMenu")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    public static extern int AppendMenuW(IntPtr hMenu, int uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32", EntryPoint = "SetMenu")]
    public static extern int SetMenu(IntPtr hWnd, IntPtr hMenu);

    [DllImport("user32", EntryPoint = "GetMenu")]
    public static extern IntPtr GetMenu(IntPtr hWnd);

    [DllImport("user32", EntryPoint = "GetMenuItemCount")]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32", EntryPoint = "GetMenuStringW", CharSet = CharSet.Unicode)]
    public static extern int GetMenuStringW(IntPtr hMenu, int uIDItem, IntPtr lpString, int cchMax, int uFlag);

    // Phase 30: menu state functions (user32 -- the existing shim, zero new
    // shim). GetMenuState returns a menu item's flag mask (MF_CHECKED /
    // MF_GRAYED / ... -- the state bits); CheckMenuItem toggles the MF_CHECKED
    // bit, EnableMenuItem sets MF_ENABLED / MF_GRAYED / MF_DISABLED (both
    // return the PREVIOUS state, -1 on failure). GetSubMenu resolves a popup
    // item's submenu handle; GetMenuItemID reads a sub-item's WM_COMMAND id (a
    // popup item itself has no command id -> -1).
    [DllImport("user32", EntryPoint = "GetMenuState")]
    public static extern int GetMenuState(IntPtr hMenu, int uId, int uFlags);

    [DllImport("user32", EntryPoint = "CheckMenuItem")]
    public static extern int CheckMenuItem(IntPtr hMenu, int uIDCheckItem, int uCheck);

    [DllImport("user32", EntryPoint = "EnableMenuItem")]
    public static extern int EnableMenuItem(IntPtr hMenu, int uIDEnableItem, int uEnable);

    [DllImport("user32", EntryPoint = "GetSubMenu")]
    public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

    [DllImport("user32", EntryPoint = "GetMenuItemID")]
    public static extern int GetMenuItemID(IntPtr hMenu, int nPos);

    // Stage 11: MDI. GetWindowLongW reads a window's creation style (GWL_STYLE)
    // back -- proves the MDI frame was created with WS_CLIPCHILDREN.
    // SetActiveWindow / GetActiveWindow are the simplified child-activation
    // channel: activating an MDI child makes it the active window of the
    // calling thread (the WM_MDIACTIVATE acceptance uses this channel).
    [DllImport("user32", EntryPoint = "GetWindowLongW")]
    public static extern IntPtr GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32", EntryPoint = "SetActiveWindow")]
    public static extern IntPtr SetActiveWindow(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "GetActiveWindow")]
    public static extern IntPtr GetActiveWindow();

    // Stage 6: modal window -- EnableWindow disables the parent Form (the
    // modal owner); IsWindowEnabled probes the disabled state.
    [DllImport("user32", EntryPoint = "EnableWindow")]
    public static extern int EnableWindow(IntPtr hwnd, int enable);

    [DllImport("user32", EntryPoint = "IsWindowEnabled")]
    public static extern int IsWindowEnabled(IntPtr hwnd);

    // Stage 2: GDI painting (gdi32).
    [DllImport("user32", EntryPoint = "BeginPaint")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);

    [DllImport("user32", EntryPoint = "EndPaint")]
    public static extern int EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    [DllImport("gdi32", EntryPoint = "TextOutW", CharSet = CharSet.Unicode)]
    public static extern int TextOutW(IntPtr hdc, int x, int y, string text, int c);

    // Phase 14: GDI font + DrawText (gdi32 + user32). CreateFontW (gdi32)
    // creates an HFONT from 14 scalar params + the face name; SelectObject
    // (gdi32) selects it into a DC and returns the PREVIOUS object (the
    // restore target); DeleteObject (gdi32) frees a font that is no longer
    // selected in any DC. DrawTextW (user32 -- the WinForms "draw a text
    // string with layout flags into a RECT" surface) is the text-drawing
    // complement to the stage-2 TextOutW; GetTextExtentPoint32W (gdi32)
    // measures the string's logical extent (SIZE cx/cy) in the SELECTED font.
    [DllImport("gdi32", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(int h, int w, int e, int o, int wt,
        int it, int un, int so, int cs, int op, int cp, int q, int pf, string face);

    [DllImport("gdi32", EntryPoint = "SelectObject")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32", EntryPoint = "DeleteObject")]
    public static extern int DeleteObject(IntPtr obj);

    [DllImport("user32", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(IntPtr hdc, string text, int c, ref RECT rc, int format);

    [DllImport("gdi32", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode)]
    public static extern int GetTextExtentPoint32W(IntPtr hdc, string text, int c, out SIZE sz);

    // Phase 15: Panel subclassing (user32). SetWindowLongPtrW installs a new
    // class proc on a STATIC window (GWLP_WNDPROC) so the container gets its
    // own managed event surface (the proc returns the ORIGINAL proc -- checked
    // non-zero to prove the subclass installed). Unhandled messages fall to
    // DefWindowProcW (the same fallback every WndProc in this file uses). The
    // container hierarchy (GetParent) and layout (GetWindowRect /
    // GetClientRect, both out RECT) expose the Panel's ownership + size.
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32", EntryPoint = "GetClientRect")]
    public static extern int GetClientRect(IntPtr hwnd, out RECT rc);

    // Phase 17: scroll-bar functions (user32) -- the DOCUMENTED channel for a
    // ScrollBar control. The legacy SBM_SETRANGE/SBM_SETPOS/SBM_GETPOS control
    // MESSAGES are no-ops on modern Windows (probed: the range stays 0..0 and
    // the position stays 0 after sending them), so the range/position surface
    // uses the functions the SBM_* docs point to instead. SetScrollRange sets
    // the min/max (nonzero on success); SetScrollPos sets the thumb (RETURNS
    // the previous position -- a deterministic sequence, the same shape as
    // PBM_SETPOS) and CLAMPS to the range; GetScrollPos reads the position
    // back. SB_CTL addresses the scroll-bar CONTROL.
    [DllImport("user32", EntryPoint = "SetScrollRange")]
    public static extern int SetScrollRange(IntPtr hwnd, int nBar, int nMinPos, int nMaxPos, bool fRedraw);

    [DllImport("user32", EntryPoint = "SetScrollPos")]
    public static extern int SetScrollPos(IntPtr hwnd, int nBar, int nPos, bool fRedraw);

    [DllImport("user32", EntryPoint = "GetScrollPos")]
    public static extern int GetScrollPos(IntPtr hwnd, int nBar);

    // Phase 36: ShowScrollBar (user32) -- shows/hides a WINDOW's own scroll
    // bars (the WS_VSCROLL / WS_HSCROLL window-style bars, nBar = SB_HORZ /
    // SB_VERT / SB_BOTH). Returns nonzero on success. Probed on this OS: the
    // hidden bars RETAIN their range and thumb position (GetScrollPos keeps
    // reading the value while hidden; showing them again restores the same
    // scroll state -- the "hide -> position retained -> show -> same" oracle).
    // The bool marshals as BOOL (the proven MoveWindow / SetScrollRange shape);
    // resolved through the existing INTERFACE user32 shim.
    [DllImport("user32", EntryPoint = "ShowScrollBar")]
    public static extern int ShowScrollBar(IntPtr hwnd, int wBar, bool bShow);

    // Phase 12: clipboard (user32) + global-memory lock (kernel32). The
    // WinForms Clipboard copy/paste path: OpenClipboard/EmptyClipboard/
    // SetClipboardData/GetClipboardData/CloseClipboard transfer a
    // CF_UNICODETEXT global-memory block; GlobalLock/GlobalUnlock (kernel32 --
    // the kernel32 shim already forwards to the system import library) pin it
    // for the PtrToStringUni readback.
    [DllImport("user32", EntryPoint = "OpenClipboard")]
    public static extern int OpenClipboard(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "EmptyClipboard")]
    public static extern int EmptyClipboard();

    [DllImport("user32", EntryPoint = "SetClipboardData")]
    public static extern IntPtr SetClipboardData(int uFormat, IntPtr hMem);

    [DllImport("user32", EntryPoint = "GetClipboardData")]
    public static extern IntPtr GetClipboardData(int uFormat);

    [DllImport("user32", EntryPoint = "CloseClipboard")]
    public static extern int CloseClipboard();

    [DllImport("kernel32", EntryPoint = "GlobalLock")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32", EntryPoint = "GlobalUnlock")]
    public static extern int GlobalUnlock(IntPtr hMem);

    // Phase 23: LoadImageW (user32 -- the existing shim, no new surface).
    // Loads an IMAGE_BITMAP: hinst = NULL + name = the OBM_CLOSE ordinal
    // (MAKEINTRESOURCE, an IntPtr whose low word is the stock id) + LR_SHARED
    // loads the system's SHARED stock bitmap (the documented LR_SHARED
    // requirement for OEM bitmaps). Returns the HBITMAP handle.
    [DllImport("user32", EntryPoint = "LoadImageW")]
    public static extern IntPtr LoadImageW(IntPtr hinst, IntPtr name, int type, int cx, int cy, int fuLoad);

    // Phase 24: LoadIconW (user32 -- the existing shim) + Shell_NotifyIconW
    // (shell32 -- the NEW shell32 shim). LoadIconW(hinst = NULL, ordinal
    // IDI_APPLICATION) loads the system's STOCK application icon (a SHARED
    // system resource, never freed by the caller); Shell_NotifyIconW drives
    // the tray-icon lifecycle: NIM_ADD / NIM_MODIFY / NIM_DELETE with a
    // pointer to a NOTIFYICONDATAW buffer (see the pinned-int layout in the
    // phase-24 Main block).
    [DllImport("user32", EntryPoint = "LoadIconW")]
    public static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    // Phase 50: ImageList API (comctl32 -- the existing comctl32 shim channel).
    // ImageList_Create makes a HIMAGELIST; ImageList_Add appends a bitmap
    // (hbmImage, optional mask) and returns its index; ImageList_Destroy
    // frees it. The list-view image-list surface is LVM_SETIMAGELIST /
    // LVM_GETIMAGELIST with LVSIL_SMALL. Headless: handle non-null + the
    // set/get round-trip + destroy -- all resolved values, no drawing.
    [DllImport("comctl32", EntryPoint = "ImageList_Create")]
    public static extern IntPtr ImageList_Create(int cx, int cy, int flags, int cInitial, int cGrow);

    [DllImport("comctl32", EntryPoint = "ImageList_Add")]
    public static extern int ImageList_Add(IntPtr himl, IntPtr hbmImage, IntPtr hbmMask);

    [DllImport("comctl32", EntryPoint = "ImageList_Destroy")]
    public static extern int ImageList_Destroy(IntPtr himl);

    [DllImport("shell32", EntryPoint = "Shell_NotifyIconW")]
    public static extern int Shell_NotifyIconW(int dwMessage, IntPtr lpdata);

    // Phase 25: GetOpenFileNameW (comdlg32 -- the NEW comdlg32 shim) + the
    // lpstrFile pre-fill (GetModuleFileNameW, kernel32 -- the existing shim).
    // GetOpenFileNameW shows the classic Open-file dialog (a "#32770" dialog
    // whose caption is lpstrTitle) and BLOCKS the calling thread until the
    // user picks a file or cancels. It takes a pointer to an OPENFILENAMEW
    // -- built field-by-field in a GCHandle-PINNED int[38] carrying the
    // native x64 layout (152 bytes, offsetof-probed: lStructSize@0,
    // hwndOwner@8, lpstrFilter@24, lpstrFile@48, nMaxFile@56, lpstrTitle@88,
    // Flags@96, nFileOffset@100, nFileExtension@102). Returns a BOOL:
    // nonzero if the user selected a file (lpstrFile filled with the path).
    [DllImport("comdlg32", EntryPoint = "GetOpenFileNameW")]
    public static extern int GetOpenFileNameW(IntPtr lpofn);

    // Phase 25: GetModuleFileNameW (kernel32 -- the existing shim) pre-fills
    // the OPENFILENAMEW.lpstrFile buffer with the RUNNING EXECUTABLE's full
    // path (the dialog then keeps only the file NAME and returns it with its
    // current directory prepended -- probed). A non-empty initial file name
    // is REQUIRED: clicking the dialog's OK with an empty name fails even
    // with no OFN_* flags, so the name comes from here (deterministic -- the
    // extension "exe" is identical across net48 and the transpiled run).
    [DllImport("kernel32", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)]
    public static extern int GetModuleFileNameW(IntPtr hModule, IntPtr lpFilename, int nSize);

    // Phase 26: ChooseColorW (comdlg32 -- the EXISTING comdlg32 shim) + the
    // dialog-window thread-id filter (GetWindowThreadProcessId, user32 -- the
    // existing shim) + the worker's own thread id (GetCurrentThreadId,
    // kernel32 -- the existing shim). ChooseColorW shows the classic color
    // dialog (a "#32770" dialog whose caption is a LOCALIZED system string --
    // NOT settable, so it CANNOT be title-matched like the phase-25 file
    // dialog) and BLOCKS the calling thread until the user picks a color or
    // cancels. It takes a pointer to a CHOOSECOLORW built field-by-field in a
    // GCHandle-PINNED int[18] carrying the native x64 layout (72 bytes:
    // lStructSize@0, hwndOwner@8, hInstance@16, rgbResult@24,
    // lpCustColors@32, Flags@40, lCustData@48, lpfnHook@56,
    // lpTemplateName@64). Returns a BOOL: nonzero if the user selected a color
    // (rgbResult holds it), zero on cancel.
    [DllImport("comdlg32", EntryPoint = "ChooseColorW")]
    public static extern int ChooseColorW(IntPtr lpcc);

    // Phase 26: GetWindowThreadProcessId (user32) returns the id of the
    // thread that CREATED the given window (the lpdwProcessId out-param is
    // IntPtr.Zero -- the process id is not needed). comdlg32 dialogs are modal
    // to the CALLING thread, so the color dialog's creating thread IS the
    // ChooseColorW worker -- the main thread identifies the right "#32770"
    // window by comparing this against the worker's recorded thread id.
    [DllImport("user32", EntryPoint = "GetWindowThreadProcessId")]
    public static extern int GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    // Phase 26: GetCurrentThreadId (kernel32) -- the worker records its OWN
    // thread id FIRST (before ChooseColorW), so the main thread can walk the
    // top-level "#32770" windows (FindWindowExW) and filter by it -- the
    // localized caption is unsearchable, the thread id is the only stable
    // identity of the dialog.
    [DllImport("kernel32", EntryPoint = "GetCurrentThreadId")]
    public static extern int GetCurrentThreadId();

    // Phase 27: ChooseFontW (comdlg32 -- the EXISTING comdlg32 shim). ChooseFontW
    // shows the classic Font dialog (a "#32770" dialog whose caption is a
    // LOCALIZED system string -- NOT settable, so it is identified by the
    // thread-id filter like the phase-26 color dialog) and BLOCKS the calling
    // thread until the user picks a font or cancels. It takes a pointer to a
    // CHOOSEFONTW built field-by-field in a GCHandle-PINNED int[26] carrying the
    // native x64 layout (104 bytes, offsetof-probed with the ming-w64 SDK which
    // matches the real Windows SDK: lStructSize@0, hwndOwner@8, hDC@16,
    // lpLogFont@24, iPointSize@32, Flags@36, rgbColors@40, lCustData@48,
    // lpfnHook@56, lpTemplateName@64, hInstance@72, lpszStyle@80, nFontType@88,
    // ___MISSING_ALIGNMENT__@90, nSizeMin@92, nSizeMax@96 -- the audit's 96B
    // draft was short the trailing font-size-limit pair). Returns a BOOL:
    // nonzero if the user selected a font (the LOGFONTW is written back).
    [DllImport("comdlg32", EntryPoint = "ChooseFontW")]
    public static extern int ChooseFontW(IntPtr lpcf);

    // Phase 46: ole32 OLE drag-drop P/Invoke surface (ole32 shim). The full
    // managed dispatch bridge is COM stage 17 (test_com section 22); this
    // phase pins the ole32 P/Invoke channel in the winforms1 e2e:
    // OleInitialize (thread init), RegisterDragDrop / RevokeDragDrop
    // (window registration) -- all scalars, all deterministic (probed).
    [DllImport("ole32", EntryPoint = "OleInitialize")]
    public static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32", EntryPoint = "OleUninitialize")]
    public static extern void OleUninitialize();

    [DllImport("ole32", EntryPoint = "RegisterDragDrop")]
    public static extern int RegisterDragDrop(IntPtr hwnd, IntPtr pDropTarget);

    [DllImport("ole32", EntryPoint = "RevokeDragDrop")]
    public static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32", EntryPoint = "CoInitializeEx")]
    public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

    [DllImport("ole32", EntryPoint = "CoUninitialize")]
    public static extern void CoUninitialize();

    // Phase 28: LoadLibraryW (kernel32 -- the EXISTING kernel32 shim). The
    // RichEdit20W window class is NOT a user32 built-in -- it is registered by
    // riched20.dll when the DLL is loaded. LoadLibraryW("riched20.dll") returns
    // the module handle (nonzero = the class is now available for
    // CreateWindowExW). The string parameter marshals as UTF-16 (CharSet.Unicode,
    // the same wired path as GetModuleHandleW).
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryW(string lpFileName);

    // --- Observable state (the "control state dump" of plan §4.3) ---
    public static int wmCreateCount;
    public static int wmCommandCount;
    public static int wmCloseCount;
    public static int wmPaintCount;
    public static int clickCount;
    public static int wmKeydownCount;
    public static bool closeDestroyed;
    public static string pumpExitReason = "never";
    public static bool beginPaintOk;
    public static bool textOutOk;
    public static string paintedText = "";

    // Stage 3: posted-text round-trip state (filled by the WF_SET_TEXT
    // WndProc handler, which drives WM_SETTEXT + GetWindowTextW).
    public static int postedTextLen;
    public static string postedText = "";
    public static IntPtr EditHwnd;

    // Phase 4: managed event-system state. P4A/P4B/P4C are the multi-control
    // Click targets; P4T exercises a second event (KeyDown). EventSeq records
    // the handler invocation order (the "trigger order" evidence).
    public static WfControl P4A;
    public static WfControl P4B;
    public static WfControl P4C;
    public static WfControl P4T;
    public static string EventSeq = "";

    // The WndProc must stay rooted while the native side may call it.
    public static WndProc RootWndProc;

    // Phase 5: the second window (child Form) has its OWN WndProc delegate
    // (RootChildWndProc) + independent observable state.
    public static WndProc RootChildWndProc;
    public static IntPtr ChildHwnd;
    public static int WmChildCreateCount;
    public static int ChildMsgCount;
    public static bool ChildCloseDestroyed;

    // Phase 6: MessageBoxW result (written by the worker thread, read after
    // Join -- the join is the happens-before edge) + the modal window's own
    // WndProc delegate and state.
    public static int MsgBoxResult = -1;
    public static WndProc RootModalWndProc;
    public static int ModalCreateCount;
    public static int ModalCloseCount;
    public static bool ModalDestroyed;
    public static IntPtr ModalHwnd;

    // Phase 11: the MDI CLIENT's WndProc delegate (rooted for the native
    // callback) + the MDI CHILD windows' shared WndProc delegate + the MDI
    // FRAME's WndProc delegate.
    public static WndProc MdiFrameWndProc;
    public static WndProc MdiClientWndProc;
    public static WndProc MdiChildWndProc;
    public static int MdiFrameCreateCount;

    // Phase 4: per-control / shared handlers. Each appends its tag so the
    // sequence string exposes exactly which handlers ran, in which order.
    public static void OnA1() { EventSeq = EventSeq + "A1;"; }
    public static void OnA2() { EventSeq = EventSeq + "A2;"; }
    public static void OnB1() { EventSeq = EventSeq + "B1;"; }
    public static void OnC1() { EventSeq = EventSeq + "C1;"; }
    public static void OnShared() { EventSeq = EventSeq + "S;"; }
    public static void OnKey() { EventSeq = EventSeq + "K;"; }
    public static void OnKey2() { EventSeq = EventSeq + "K2;"; }

    public static void OnButtonClick() { clickCount++; }

    // Phase 7: property-based control model state. P7Btn is the control the
    // WM_COMMAND dispatch routes clicks into (by id); P7Seq records the Click
    // + Enabled-toggle handler sequence.
    public static WfPropControl P7Btn;
    public static string P7Seq = "";

    // Phase 7: a Click handler that toggles the control's Enabled property.
    // The dispatch only raises Click while the control is Enabled (a disabled
    // control receives no click -- the WinForms rule), so each toggle flips
    // the property AND arms/disarms the next dispatch.
    public static void OnP7Toggle() { P7Seq = P7Seq + "C;"; P7Btn.Enabled = !P7Btn.Enabled; }

    // Phase 8: dialog-control (ComboBox/ListBox) dispatch targets + the
    // SelectionChanged sequence. P8Combo/P8List are the controls the form's
    // WM_COMMAND handler routes CBN_SELCHANGE/LBN_SELCHANGE into; the handlers
    // record the CURRENT selection each reads back at fire time.
    public static WfDialogControl P8Combo;
    public static WfDialogControl P8List;
    public static string P8Seq = "";

    public static void OnP8ComboChange() { P8Seq = P8Seq + "combo" + P8Combo.SelectedIndex + ";"; }
    public static void OnP8ListChange() { P8Seq = P8Seq + "list" + P8List.SelectedIndex + ";"; }

    // Phase 37: the double-click handler -- counts the invocations so the
    // multicast DoubleClick event firing is observable (the "dbl<sel>;" tag
    // itself is appended by WfListBox.RaiseDoubleClick re-reading the native
    // selection at fire time).
    public static void OnP37DblClick() { P37DblFired++; }

    // Phase 38: the ComboBox double-click handler -- mirrors the Phase-37
    // counting handler so the multicast DoubleClick event firing is observable
    // (the "dbl<sel>;" tag itself is appended by WfComboBox.RaiseDoubleClick
    // re-reading the native selection at fire time).
    public static void OnP38DblClick() { P38DblFired++; }

    // Phase 40: the TextBox EN_CHANGE handler -- counts the TextChanged event
    // invocations (the "change;" tag itself is appended by
    // WfTextBox.RaiseTextChanged when the EN_CHANGE route dispatches).
    public static void OnP40Change() { P40ChangeFired++; }

    // Phase 42: the RichTextBox EN_CHANGE handler -- mirrors the Phase-40
    // counting handler (the "change;" tag itself is appended by
    // WfRichTextBox.RaiseTextChanged when the REAL EN_CHANGE dispatches).
    public static void OnP42Change() { P42ChangeFired++; }

    // Stage 10: menu-driven status. SetStatus writes the status bar text via
    // the stage-9 SB_SETTEXT channel and reads it back (the round-trip proves
    // the linkage end-to-end); each Click handler records its own tag.
    public static void SetStatus(string text)
    {
        IntPtr st = Marshal.StringToHGlobalUni(text);
        SendMessageW(P9StatusHwnd, SB_SETTEXT, IntPtr.Zero, st);
        Marshal.FreeHGlobal(st);
        IntPtr sb = Marshal.AllocHGlobal(256);
        SendMessageW(P9StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb);
        P10Status = Marshal.PtrToStringUni(sb);
        Marshal.FreeHGlobal(sb);
    }
    public static void OnP10New() { P10Seq = P10Seq + "new;"; SetStatus("Menu: New"); }
    public static void OnP10Open() { P10Seq = P10Seq + "open;"; SetStatus("Menu: Open"); }
    public static void OnP10About() { P10Seq = P10Seq + "about;"; SetStatus("Menu: About"); }

    // Phase 32: toolbar-driven status. P32MirrorStatus writes the given text
    // into status part 0 (SB_SETTEXT with wParam = part index + SB_GETTEXT
    // round-trip, the phase-31 per-part channel) and returns the read-back;
    // each toolbar-button Click handler records its own tag + mirrors its text.
    public static string P32MirrorStatus(string text)
    {
        IntPtr st32 = Marshal.StringToHGlobalUni(text);
        SendMessageW(P9StatusHwnd, SB_SETTEXT, IntPtr.Zero, st32);
        Marshal.FreeHGlobal(st32);
        IntPtr sb32 = Marshal.AllocHGlobal(256);
        SendMessageW(P9StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb32);
        string s32 = Marshal.PtrToStringUni(sb32);
        Marshal.FreeHGlobal(sb32);
        return s32;
    }
    public static void OnP32New() { P32Seq = P32Seq + "new;"; P32Status = P32MirrorStatus("Tool: New"); }
    public static void OnP32Open() { P32Seq = P32Seq + "open;"; P32Status = P32MirrorStatus("Tool: Open"); }
    public static void OnP32About() { P32Seq = P32Seq + "about;"; P32Status = P32MirrorStatus("Tool: About"); }

    // Stage 9: status/progress control handles (needed by the WF_PROGRESS_STEP
    // WndProc handler) + the observable interaction sequence. P9Seq records
    // step/position/status at each advance; P9StepCount counts the steps.
    public static IntPtr P9StatusHwnd;
    public static IntPtr P9ProgressHwnd;
    public static string P9Seq = "";
    public static int P9StepCount;

    // Stage 10: menu dispatch targets. The form's WM_COMMAND handler routes a
    // menu-id command (LOWORD of wParam) into the matching MenuItem's Click
    // event; the handlers mirror the item text into the status bar.
    public static WfMenuItem P10New;
    public static WfMenuItem P10Open;
    public static WfMenuItem P10About;
    public static string P10Seq = "";
    public static string P10Status = "";

    // Phase 31: StatusStrip multi-part state. P31Updated holds the text the
    // WF_STATUS_UPDATE WndProc branch wrote into the updated part (read back
    // by the handler; the Main block prints it after the post/drain).
    public static string P31Updated = "";

    // Phase 32: ToolStrip state. P32Tool is the toolbar model (HWND wrapper);
    // P32New/P32Open/P32About are the toolbar button models (the WndProc
    // WM_COMMAND dispatch routes a toolbar-button id into the matching button's
    // Click event -- the same shape as the Phase-10 menu dispatch). P32Seq
    // records the dispatch order (real-click New + posted Open); P32Status the
    // status-part-0 mirror readback (toolbar drives status).
    public static WfToolStrip P32Tool;
    public static WfToolButton P32New;
    public static WfToolButton P32Open;
    public static WfToolButton P32About;
    public static string P32Seq = "";
    public static string P32Status = "";

    // Phase 33: DataGridView (simplified) state. P33Grid is the grid model
    // (a report-view ListView HWND wrapper); P33ColClick records the column
    // index the WF_GRID_COLCLICK WndProc branch received (the headless "user
    // clicked a column header" interaction -- wParam = the clicked column).
    public static WfDataGridView P33Grid;
    public static int P33ColClick = -1;

    // Phase 34: ProgressBar complete-surface state. P34Bar is the bar model
    // (the Phase-9 "msctls_progress32" HWND wrapped); P34Seq records the
    // position after each posted WF_P34_STEP advance; P34Status holds the text
    // the WndProc branch mirrored into status part 0 ("P34: <pos>%").
    public static WfProgressBar P34Bar;
    public static string P34Seq = "";
    public static string P34Status = "";

    // Phase 35: TabControl item read/write surface state. P35Tab is a SECOND
    // SysTabControl32 (the Phase-16 channel) exercising the ITEM surface:
    // text + lParam written at insert (TCIF_TEXT|TCIF_PARAM), read back with
    // TCM_GETITEMW, renamed with TCM_SETITEMW, item rect + row count probed.
    // P35Seq records the texts the WF_TAB35_TEXT WndProc branch read back
    // through the pump (the posted "user clicks a tab" interaction).
    public static WfTabControl P35Tab;
    public static bool P35TabCreated;
    public static string P35Seq = "";

    // Phase 36: Panel window-scrollbar observable state. P36PanelHwnd is a
    // SECOND STATIC container (the Phase-15 subclassed-panel model) created
    // with the WS_VSCROLL | WS_HSCROLL WINDOW-scroll-bar styles -- the Panel's
    // scroll surface. P36StyleV / P36StyleH are the GWL_STYLE readbacks of the
    // two style bits; P36VscrollCount / P36HscrollCount count the WM_VSCROLL /
    // WM_HSCROLL messages the panel's proc handled (5 vertical + 3 horizontal);
    // P36Seq records each scroll's code+position (the "v:linedown40;" /
    // "h:lineright20;" sequence oracle); P36Destroyed the lifecycle close.
    public static IntPtr P36PanelHwnd;
    public static bool P36StyleV;
    public static bool P36StyleH;
    public static int P36VscrollCount;
    public static int P36HscrollCount;
    public static string P36Seq = "";
    public static bool P36Destroyed;

    // Phase 37: ListBox item-management observable state. P37List is a SECOND
    // LISTBOX (the Phase-8 channel) created as a short 200x60 window with
    // LBS_NOINTEGRALHEIGHT (the item-management surface: insert / delete /
    // reset / item-data / find / top-index + the LBN_DBLCLK double-click
    // event). P37Seq records the dbl-click selections the WndProc dispatched
    // (the "dbl<sel>;" sequence oracle); P37DblFired counts the DoubleClick
    // event invocations; P37Destroyed the lifecycle close.
    public static WfListBox P37List;
    public static string P37Seq = "";
    public static int P37DblFired;
    public static bool P37Destroyed;

    // Phase 38: ComboBox item-management observable state. P38Combo is a SECOND
    // COMBOBOX (the Phase-8 channel) created as a SHORT 160x40 window with
    // CBS_DROPDOWNLIST (the item-management surface -- the CB_* mirror of the
    // Phase-37 LB_* set: insert / delete / reset / item-data / find / top-index
    // + the CBN_DBLCLK double-click event). P38Seq records the dbl-click
    // selections the WndProc dispatched (the "dbl<sel>;" sequence oracle);
    // P38DblFired counts the DoubleClick event invocations; P38Destroyed the
    // lifecycle close.
    public static WfComboBox P38Combo;
    public static string P38Seq = "";
    public static int P38DblFired;
    public static bool P38Destroyed;

    // Phase 39: ListView checkbox + multi-select state. P39List is a SECOND
    // SysListView32 (the Phase-21 channel) created with LVS_REPORT + the
    // LVS_EX_CHECKBOXES extended style, exercising the checkbox on/off and
    // multi-selection state surface. Everything is a SYNCHRONOUS readback (the
    // Phase-33 pattern -- no posted interaction), so the only state is the
    // wrapper + the lifecycle close.
    public static WfListView P39List;
    public static bool P39Destroyed;

    // Phase 40: TextBox (EDIT) deep-surface state. P40Edit is a SECOND EDIT
    // (the Phase-3 channel) created as a multiline ES_MULTILINE | ES_AUTOVSCROLL
    // | ES_AUTOHSCROLL control, exercising the EM_* edit-message family (line
    // geometry, per-line readback, selection, limit, dirty flag, undo).
    // P40Seq records the EN_CHANGE "change;" tags the WndProc dispatch appends
    // (the posted WM_COMMAND EN_CHANGE route); P40ChangeFired counts the
    // TextChanged event invocations; P40Destroyed the lifecycle close.
    public static WfTextBox P40Edit;
    public static string P40Seq = "";
    public static int P40ChangeFired;
    public static bool P40Destroyed;

    // Phase 41: TreeView readback surface state. P41Tree is a SECOND WC_TREEVIEW
    // (the Phase-22 channel) exercising the TVITEMW item readback: node text
    // get/set (TVM_GETITEMW / TVM_SETITEMW), the scalar TVM_GETITEMSTATE channel,
    // TVM_SELECTITEM caret movement, and the corrected TVIF_PARAM lParam readback.
    // P41TreeCreated / P41TreeDestroyed the lifecycle.
    public static WfTreeView P41Tree;
    public static bool P41TreeCreated;
    public static bool P41TreeDestroyed;

    // Phase 42: RichTextBox (riched20 RichEdit20W) line-surface state. P42Rich
    // is a SECOND RichEdit20W (the Phase-28 LoadLibraryW channel) exercising
    // the Phase-40 EM_* line family on the RICH control + the REAL EN_CHANGE
    // event the Phase-40 note could only fake: EM_SETEVENTMASK is genuinely
    // honored by RichEdit (probed), so an injected WM_CHAR delivers a
    // SYNCHRONOUS WM_COMMAND(id | EN_CHANGE<<16) into the WndProc route.
    // P42Seq records the "change;" tags WfRichTextBox.RaiseTextChanged appends;
    // P42ChangeFired counts the TextChanged invocations; P42Destroyed the
    // lifecycle close.
    public static WfRichTextBox P42Rich;
    public static string P42Seq = "";
    public static int P42ChangeFired;
    public static bool P42Destroyed;

    // Phase 43: ListView column-order (DisplayIndex) state. P43List is a THIRD
    // SysListView32 (the Phase-21/33 channel) exercising the column DISPLAY
    // order exchange: LVM_SETCOLUMNORDERARRAY (wParam = 3, lParam = a bare
    // GCHandle-PINNED int[3] -- the EM_GETLINE shape) / LVM_GETCOLUMNORDERARRAY
    // readback, joined into a STRING (the M3 SZArray restriction forbids an
    // int[] return -- the Phase-40/42 pattern). P43ListCreated / P43ListDestroyed
    // the lifecycle close.
    public static WfDataGridView P43List;
    public static bool P43ListCreated;
    public static bool P43ListDestroyed;

    // Phase 44: window layout-surface observable state. P44Active is the GUARD
    // that arms the WndProc's WM_GETMINMAXINFO branch for the headless
    // loopback ONLY -- the natural OS-driven WM_GETMINMAXINFO (CreateWindowExW /
    // ShowWindow / MoveWindow) arrives with lParam pointing at the OS's INTERNAL
    // MINMAXINFO buffer; if the branch were unguarded it would Marshal.Copy over
    // that buffer and corrupt the window's sizing state. P44Write is the
    // self-set MINMAXINFO (static int[10], the x64 5-POINT layout: [0..1]
    // ptReserved, [2..3] ptMaxSize, [4..5] ptMaxPosition, [6..7] ptMinTrackSize,
    // [8..9] ptMaxTrackSize -- ONLY the self-set track fields [6..9] are
    // written/read, [0..5] stay 0 and are NEVER output; the DefWindowProc-filled
    // fields are resolution-dependent and never asserted). P44Track counts the
    // guard-armed loopback invocations (deterministic: exactly 1 in the test).
    public static bool P44Active;
    public static int[] P44Write = new int[10] { 0, 0, 0, 0, 0, 0, 200, 150, 640, 480 };
    public static int P44Track;

    // Phase 45: TabControl layout-surface observable state. P45Tab is a SECOND
    // TCS_FIXEDWIDTH SysTabControl32 (the Phase-35 channel) exercising the
    // fixed ITEM SIZE: the two-step TCM_SETITEMSIZE readback (the FIRST call's
    // return is the font-metric default and is discarded; the SECOND call
    // returns the FIRST explicit value, the THIRD the second -- LOWORD/HIWORD
    // = the previous width/height, deterministic). P45TabCreated the
    // lifecycle open. P45MlTab is a THIRD TCS_MULTILINE | TCS_FIXEDWIDTH
    // narrow control (~110px) whose TCM_GETROWCOUNT wraps onto 2 rows (probed:
    // 105..154px client = 2 rows for 3 fixed 50px tabs; 100px = 3 rows, the
    // ~5px comctl32 margin) -- the ItemSize.SizeMode / Multiline backing.
    public static WfTabControl P45Tab;
    public static bool P45TabCreated;
    public static WfTabControl P45MlTab;
    public static bool P45MlCreated;

    // Phase 46: OLE drag-drop P/Invoke surface state. OleInitRet = the
    // OleInitialize return (RPC_E_CHANGED_MODE on the MTA main thread --
    // probed deterministic); RegNullRet / RegBadRet = RegisterDragDrop with a
    // null / invalid hwnd (E_INVALIDARG); RevokeNullRet = RevokeDragDrop on a
    // window never registered (DRAGDROP_E_NOTREGISTERED). All HRESULTs are
    // the probed net48 ground truth -- byte-exact on both compilers.
    public static int P46OleInitRet;
    public static int P46RegNullRet;
    public static int P46RegBadRet;
    public static int P46RevokeNullRet;

    // Phase 11: MDI state. MdiFrameHwnd / MdiClientHwnd are the container
    // windows (frame with WS_CLIPCHILDREN, client = the single parent of all
    // children); MdiChildA/B/C are the per-child managed models (each with its
    // OWN Click event -- the per-child event delegates). MdiActSeq records the
    // WM_MDIACTIVATE activation order (the net48 oracle activation sequence);
    // MdiActiveChild is the last-activated child.
    public static IntPtr MdiFrameHwnd;
    public static IntPtr MdiClientHwnd;
    public static WfMdiChild MdiChildA;
    public static WfMdiChild MdiChildB;
    public static WfMdiChild MdiChildC;
    public static string MdiActSeq = "";
    public static IntPtr MdiActiveChild;
    public static string MdiChildSeq = "";
    public static int MdiChildMsgCount;
    public static int MdiChildCreateCount;

    // Phase 12: clipboard observable state -- filled by the WF_CLIP_SET /
    // WF_CLIP_GET WndProc handlers (the copy/paste runs inside the pump).
    // P12OpenOk..P12CloseOk are the SET sequence results (open/empty/set/close);
    // P12Open2Ok..P12Close2Ok the GET sequence; P12ClipText/P12ClipLen the
    // pasted readback (the "clip_set=... / clip_get=..." oracle).
    public static bool P12OpenOk;
    public static bool P12EmptyOk;
    public static bool P12SetOk;
    public static bool P12CloseOk;
    public static bool P12Open2Ok;
    public static bool P12GetOk;
    public static bool P12Close2Ok;
    public static string P12ClipText = "";
    public static int P12ClipLen;

    // Phase 13: cross-thread UI-marshaling state. P13FormHwnd is the form the
    // WORKER thread posts to (the target queue belongs to the main thread).
    // P13PostedCount is Interlocked by the WORKER (one increment per post);
    // P13UiCount is Interlocked by the WndProc (one increment per marshaled
    // message) -- equal counts = every cross-thread message was processed.
    // P13Seq is the dispatch-order sequence (built on the main thread only);
    // P13UiText/P13UiTextLen are the TextBox state the WndProc updates.
    public static IntPtr P13FormHwnd;
    public static int P13PostedCount;
    public static int P13UiCount;
    public static string P13Seq = "";
    public static string P13UiText = "";
    public static int P13UiTextLen;

    // Phase 14: GDI font + DrawText observable state -- filled by the WM_PAINT
    // handler (the same injected-marker paint that validates TextOutW). Each
    // field records ONE native call result: the created font handle is a
    // boolean (non-zero), SelectObject returning the PREVIOUS font proves the
    // selection, the DrawTextW return is the drawn text height, the extent is
    // the SIZE the SELECTED font measures, and restore/delete close the
    // create-select-draw-measure-restore-delete cycle. P14Seq is the step
    // order (the "font/drawing sequence" oracle).
    public static bool P14FontCreated;
    public static bool P14SelectOk;
    public static bool P14DrawOk;
    public static int P14DrawHeight;
    public static bool P14ExtOk;
    public static int P14ExtCx;
    public static int P14ExtCy;
    public static bool P14RestoreOk;
    public static bool P14DeleteOk;
    public static string P14Seq = "";

    // Phase 15: Panel container observable state. P15PanelHwnd / P15BtnHwnd
    // are the STATIC container + its child button. The parent-chain booleans
    // prove the container owns its child (GetParent(button) == panel, not the
    // form); the sizes are GetWindowRect/GetClientRect deltas; the counts +
    // P15Seq are the nested event evidence (panel's own message + the bubbled
    // button click chain).
    public static WndProc PanelWndProc;
    public static IntPtr P15PanelHwnd;
    public static IntPtr P15BtnHwnd;
    public static bool P15PanelCreated;
    public static bool P15BtnCreated;
    public static bool P15SubclassOk;
    public static bool P15ParentIsForm;
    public static bool P15BtnParentIsPanel;
    public static bool P15RectOk;
    public static int P15PanelW;
    public static int P15PanelH;
    public static int P15BtnW;
    public static int P15BtnH;
    public static bool P15ClientOk;
    public static int P15ClientW;
    public static int P15ClientH;
    public static int P15MsgCount;
    public static int P15ClickCount;
    public static int P15FormNotify;
    public static string P15Seq = "";
    public static bool P15Destroyed;

    // Phase 16: TabControl observable state. TabHwnd is the WC_TABCONTROL
    // ("SysTabControl32") window; P16Tab is the managed TabControl model (page
    // list via TCITEM / selection via TCM_GETCURSEL/TCM_SETCURSEL + a
    // SelectedIndexChanged event raised on TCN_SELCHANGE). P16Page0..2 are the
    // PER-PAGE content STATIC controls (tab index k owns P16Page k -- the
    // "per-page independent content" evidence); P16Seq is the page-switch
    // sequence and P16SelChangeCount / P16NotifyCount the event counts.
    public static IntPtr TabHwnd;
    public static IntPtr P16Page0;
    public static IntPtr P16Page1;
    public static IntPtr P16Page2;
    public static WfTabControl P16Tab;
    public static bool P16TabCreated;
    public static bool P16Page0Created;
    public static bool P16Page1Created;
    public static bool P16Page2Created;
    public static int P16NotifyCount;
    public static int P16SelChangeCount;
    public static string P16Seq = "";
    public static bool P16Destroyed;

    public static void OnMdiA() { MdiChildSeq = MdiChildSeq + "A;"; }
    public static void OnMdiB() { MdiChildSeq = MdiChildSeq + "B;"; }
    public static void OnMdiC() { MdiChildSeq = MdiChildSeq + "C;"; }

    // Phase 16: show ONE page's content and hide the other two (the
    // per-page-content switch -- the visible page is exactly the selected tab's
    // page). P16Seq records the switch order (the "page sequence" oracle).
    public static void P16ShowPage(int idx)
    {
        ShowWindow(P16Page0, idx == 0 ? SW_SHOW : SW_HIDE);
        ShowWindow(P16Page1, idx == 1 ? SW_SHOW : SW_HIDE);
        ShowWindow(P16Page2, idx == 2 ? SW_SHOW : SW_HIDE);
        P16Seq = P16Seq + "page" + idx + ";";
    }

    // Phase 16: style-bit visibility. The form is HIDDEN, so IsWindowVisible
    // (which walks the ancestor chain) reports FALSE for every child -- the
    // per-page visibility probe reads the window's OWN WS_VISIBLE style bit
    // (GetWindowLongW GWL_STYLE), exactly what ShowWindow(SW_SHOW/SW_HIDE) sets.
    public static bool P16PageVisible(IntPtr hwnd)
    {
        return (GetWindowLongW(hwnd, GWL_STYLE).ToInt64() & WS_VISIBLE) != 0;
    }
    public static void OnP16TabChanged() { P16SelChangeCount++; }

    // Phase 17: ScrollBar observable state. P17ScrollHwnd is the "ScrollBar"
    // window (the user32 built-in class); P17ScrollCreated / P17Destroyed the
    // lifecycle; P17ClampHigh / P17ClampLow the range-clamp readbacks
    // (SetScrollPos(120) clamps to max 100, SetScrollPos(-5) clamps to min 0,
    // both read back via GetScrollPos -- the deterministic range proof). The
    // two event channels are counted separately: P17StepCount (the posted
    // WF_VSCROLL_STEP driving the position channel) and P17VscrollCount (the
    // WM_VSCROLL parent notification a real user scroll sends). P17Seq records
    // the scroll->status linkage ("scroll drives status": each scroll mirrors
    // its position into the stage-9 status bar text via SB_SETTEXT +
    // SB_GETTEXT round-trip).
    public static IntPtr P17ScrollHwnd;
    public static bool P17ScrollCreated;
    public static bool P17ClampHigh;
    public static bool P17ClampLow;
    public static int P17StepCount;
    public static int P17VscrollCount;
    public static string P17Seq = "";
    public static bool P17Destroyed;

    // Phase 19: GroupBox container observable state. GroupBoxWndProc is the
    // SUBCLASSED container proc (the groupbox's OWN event surface); P19Group1 /
    // P19Group2 are the managed WfGroupBox models (Handle/Id/Text); P19GSeq
    // records the groupbox -> form nested event chain (gb1:msg;form:notify;)
    // and P19FormNotify the bubbled-notification count.
    public static WndProc GroupBoxWndProc;
    public static WfGroupBox P19Group1;
    public static WfGroupBox P19Group2;
    public static string P19GSeq = "";
    public static int P19FormNotify;

    // Phase 20: TrackBar observable state. P20TrackHwnd is the
    // "msctls_trackbar32" window; P20TrackCreated / P20Destroyed the
    // lifecycle; P20HscrollCount counts the WM_HSCROLL parent notifications
    // a real user scroll sends. P20Seq records the track->status linkage
    // ("track drives status": each scroll mirrors its thumb position into the
    // stage-9 status bar text via SB_SETTEXT + SB_GETTEXT round-trip).
    public static IntPtr P20TrackHwnd;
    public static bool P20TrackCreated;
    public static int P20HscrollCount;
    public static string P20Seq = "";
    public static bool P20Destroyed;

    // Phase 21: ListView observable state. P21ListHwnd is the "SysListView32"
    // window; P21ListCreated / P21ListDestroyed the lifecycle; P21NotifyCount
    // counts the WM_NOTIFY parent notifications (LVN_ITEMCHANGED -- the
    // notification a list-view sends its parent when the selection changes);
    // P21SelChangeSeq records the selection-change sequence (RaiseSelectionChanged
    // re-reads the REAL selection via LVM_GETITEMSTATE over the rows and
    // records "sel<N>;" -- the event observes the list state at fire time).
    public static IntPtr P21ListHwnd;
    public static bool P21ListCreated;
    public static int P21NotifyCount;
    public static string P21SelChangeSeq = "";
    public static bool P21ListDestroyed;
    public static WfListView P21List;

    // Phase 90: the LVN_DBLCLK double-click list + fired-count state.
    public static WfListView P90List;
    public static int P90DblFired;

    // Phase 91: the LVN_COLUMNCLICK list + fired-count state.
    public static WfListView P91List;
    public static int P91ColFired;

    // Phase 92: the LVN_ITEMACTIVATE list + fired-count state.
    public static WfListView P92List;
    public static int P92ActFired;

    // Phase 93: the LVN_ENDLABELEDITW list + fired-count state.
    public static WfListView P93List;
    public static int P93EndFired;

    // Phase 94: the LVN_BEGINDRAG list + fired-count state.
    public static WfListView P94List;
    public static int P94DragFired;

    // Phase 95: the LVN_BEGINRDRAG list + fired-count state.
    public static WfListView P95List;
    public static int P95RDragFired;

    // Phase 96: the LVN_KEYDOWN list + fired-count state.
    public static WfListView P96List;
    public static int P96KeyFired;

    // Phase 97: the LVN_INSERTITEM list + fired-count state.
    public static WfListView P97List;
    public static int P97InsFired;

    // Phase 98: the LVN_DELETEITEM list + fired-count state.
    public static WfListView P98List;
    public static int P98DelFired;

    // Phase 99: the LVN_DELETEALLITEMS list + fired-count state.
    public static WfListView P99List;
    public static int P99AllFired;

    // Phase 100: the LVN_ITEMCHANGED list + fired-count state.
    public static WfListView P100List;
    public static int P100ChgFired;

    // Phase 101: the LVN_ITEMCHANGING list + fired-count state.
    public static WfListView P101List;
    public static int P101ChgFired;

    // Phase 102: the TVN_SELCHANGED tree + fired-count state.
    public static WfTreeView P102Tree;
    public static int P102SelFired;

    // Phase 103: the TVN_SELCHANGING tree + fired-count state.
    public static WfTreeView P103Tree;
    public static int P103SelFired;

    // Phase 104: the TVN_ITEMEXPANDED tree + fired-count state.
    public static WfTreeView P104Tree;
    public static int P104ExpFired;

    // Phase 105: the TVN_ITEMEXPANDING tree + fired-count state.
    public static WfTreeView P105Tree;
    public static int P105ExpFired;

    // Phase 106: the TVN_ITEMCOLLAPSED tree + fired-count state.
    public static WfTreeView P106Tree;
    public static int P106ColFired;

    // Phase 107: the TVN_ITEMCOLLAPSING tree + fired-count state.
    public static WfTreeView P107Tree;
    public static int P107ColFired;

    // Phase 108: the TVN_BEGINLABELEDIT tree + fired-count state.
    public static WfTreeView P108Tree;
    public static int P108BegFired;

    // Phase 109: the TVN_ENDLABELEDIT tree + fired-count state.
    public static WfTreeView P109Tree;
    public static int P109EndFired;

    // Phase 110: the TVN_DELETEITEM tree + fired-count state.
    public static WfTreeView P110Tree;
    public static int P110DelFired;
    public static void OnP21ListChanged() { }

    // Phase 22: TreeView observable state. P22TreeHwnd is the "SysTreeView32"
    // window; P22TreeCreated / P22TreeDestroyed the lifecycle.
    public static IntPtr P22TreeHwnd;
    public static bool P22TreeCreated;
    public static bool P22TreeDestroyed;
    public static WfTreeView P22Tree;

    // Phase 23: PictureBox observable state. P23Pic is the "STATIC" SS_BITMAP
    // wrapper; P23PicDestroyed the lifecycle.
    public static WfPictureBox P23Pic;
    public static bool P23PicDestroyed;

    // Phase 24: NotifyIcon observable state. P24IconLoaded = LoadIconW
    // returned the stock app icon; P24IconAdded / P24IconModified /
    // P24IconDeleted = the NIM_ADD / NIM_MODIFY / NIM_DELETE Shell_NotifyIconW
    // results; P24CallbackPosted = the P24_CB_MSG callback self-post;
    // P24IconCbCount = how many times the WndProc accepted the callback
    // message.
    public static bool P24IconLoaded;
    public static bool P24IconAdded;
    public static bool P24IconModified;
    public static bool P24IconDeleted;
    public static bool P24CallbackPosted;
    public static int P24IconCbCount;

    // Phase 25: file-dialog observable state. P25Shown = the worker's
    // GetOpenFileNameW dialog was found (FindWindowW by "#32770" + P25_TITLE);
    // P25Closed = GetOpenFileNameW returned TRUE after the synchronous
    // SendMessageW(BM_CLICK) on the dialog's OK button (GetDlgItem IDOK);
    // P25FileNonEmpty = the lpstrFile buffer read back non-empty;
    // P25FileSuffix = the read-back path's extension (from nFileExtension,
    // a probe-deterministic string "exe" on both compilers). P25OpenResult is
    // written by the worker thread and read after Join (the happens-before
    // edge); P25OfnPtr is the pinned OPENFILENAMEW the worker calls with.
    public static bool P25Shown;
    public static bool P25Closed;
    public static bool P25FileNonEmpty;
    public static string P25FileSuffix = "";
    public static int P25OpenResult = -1;
    public static IntPtr P25OfnPtr;

    // Phase 26: ColorDialog observable state. P26ColorDlgFound = a top-level
    // "#32770" window created BY THE WORKER thread was found (the
    // FindWindowExW class walk + GetWindowThreadProcessId == P26WorkerTid
    // filter -- the localized dialog caption is unsearchable by title);
    // P26ColorOkClicked = the dialog's OK button (GetDlgItem IDOK) was found
    // and clicked with a SYNCHRONOUS SendMessageW(BM_CLICK); P26ColorClosed =
    // ChooseColorW returned nonzero after the click; P26ColorValue = the
    // rgbResult@24 readback (low byte); P26ColorMatches = it equals the
    // P26_PRESET. P26ColorResult/P26WorkerTid are worker-written, read after
    // the bounded worker wait (the find-window loop re-reads the tid each
    // walk); P26CcwPtr is the pinned CHOOSECOLORW the worker calls with.
    public static bool P26ColorDlgFound;
    public static bool P26ColorOkClicked;
    public static bool P26ColorClosed;
    public static int P26ColorValue;
    public static bool P26ColorMatches;
    public static int P26ColorResult = -1;
    public static int P26WorkerTid;
    public static IntPtr P26CcwPtr;

    // Phase 27: FontDialog observable state. P27FontDlgFound = a top-level
    // "#32770" window created BY THE WORKER thread was found (the FindWindowExW
    // class walk + GetWindowThreadProcessId == P27WorkerTid filter -- the font
    // dialog caption is a localized system string, unsearchable by title, the
    // phase-26 pattern); P27FontOkClicked = the dialog's OK button
    // (GetDlgItem IDOK) was found and clicked with a SYNCHRONOUS
    // SendMessageW(BM_CLICK); P27FontClosed = ChooseFontW returned nonzero
    // after the click; P27PointSize = the iPointSize@32 readback (10 * points
    // of the selected font -- > 0 when the dialog accepted); P27FaceName = the
    // lfFaceName readback (PtrToStringUni at LOGFONTW+28); P27FaceNonEmpty =
    // the read-back face name is non-empty; P27ResultNonZero = the
    // ChooseFontW BOOL. P27FontResult/P27WorkerTid are worker-written, read
    // after the bounded worker wait (the find-window loop re-reads the tid
    // each walk); P27CfPtr is the pinned CHOOSEFONTW the worker calls with.
    public static bool P27FontDlgFound;
    public static bool P27FontOkClicked;
    public static bool P27FontClosed;
    public static int P27PointSize;
    public static string P27FaceName = "";
    public static bool P27FaceNonEmpty;
    public static bool P27ResultNonZero;
    public static int P27FontResult = -1;
    public static int P27WorkerTid;
    public static IntPtr P27CfPtr;

    // Phase 28: RichTextBox observable state. P28LibLoaded = LoadLibraryW
    // ("riched20.dll") returned a nonzero module handle (the RichEdit20W class
    // is registered by the DLL); P28Created = CreateWindowExW(RichEdit20W)
    // returned a nonzero HWND; P28MaskSet = EM_SETEVENTMASK(ENM_NONE) returned
    // the PREVIOUS event mask (nonzero -- the default mask is ENM_CHANGE |
    // ENM_UPDATE, so the return proves the mask was actually changed);
    // P28SetOk = WM_SETTEXT("Wf28Rich") returned TRUE; P28Len = the
    // EM_GETTEXTLENGTH readback (8 chars); P28Text = the WM_GETTEXT readback;
    // P28Replaced = EM_REPLACESEL("X") returned TRUE after EM_SETSEL(0,2);
    // P28Text2 = the post-replace WM_GETTEXT readback ("Xf28Rich");
    // P28ColorSet = EM_SETCHARFORMAT(SCF_SELECTION) returned nonzero;
    // P28ColorMatch = the EM_GETCHARFORMAT readback's crTextColor@20 equals the
    // 0xFF0000 that was set; P28Destroyed = DestroyWindow returned nonzero.
    public static bool P28LibLoaded;
    public static bool P28Created;
    public static bool P28MaskSet;
    public static bool P28SetOk;
    public static int P28Len;
    public static string P28Text = "";
    public static bool P28Replaced;
    public static string P28Text2 = "";
    public static bool P28ColorSet;
    public static bool P28ColorMatch;
    public static bool P28Destroyed;

    // Phase 29: Timer observable state. P29TimerSet = SetTimer(hForm,
    // ID_TIMER29, 100000, 0) returned the timer id (nonzero -- the timer was
    // created); P29TimerTicks = the number of WM_TIMER messages the WndProc
    // handled (the headless test injects WM_TIMER directly with SendMessageW,
    // so the count is deterministic -- exactly 3); P29TimerKilled =
    // KillTimer(hForm, ID_TIMER29) returned nonzero (the timer was removed).
    public static bool P29TimerSet;
    public static int P29TimerTicks;
    public static bool P29TimerKilled;

    // Phase 17: mirror a scroll position into the status bar text (SB_SETTEXT
    // + SB_GETTEXT round-trip -- the stage-9 status channel) and return the
    // read-back text (the "scroll drives status" linkage evidence).
    public static string P17MirrorStatus(int pos)
    {
        IntPtr st = Marshal.StringToHGlobalUni("Scroll: " + pos);
        SendMessageW(P9StatusHwnd, SB_SETTEXT, IntPtr.Zero, st);
        Marshal.FreeHGlobal(st);
        IntPtr sb = Marshal.AllocHGlobal(256);
        SendMessageW(P9StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb);
        string s = Marshal.PtrToStringUni(sb);
        Marshal.FreeHGlobal(sb);
        return s;
    }

    // Phase 20: mirror a trackbar thumb position into the status bar text
    // (SB_SETTEXT + SB_GETTEXT round-trip -- the stage-9 status channel) and
    // return the read-back text (the "track drives status" linkage evidence).
    public static string P20MirrorStatus(int pos)
    {
        IntPtr st = Marshal.StringToHGlobalUni("Track: " + pos);
        SendMessageW(P9StatusHwnd, SB_SETTEXT, IntPtr.Zero, st);
        Marshal.FreeHGlobal(st);
        IntPtr sb = Marshal.AllocHGlobal(256);
        SendMessageW(P9StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb);
        string s = Marshal.PtrToStringUni(sb);
        Marshal.FreeHGlobal(sb);
        return s;
    }

    public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) { wmCreateCount++; return IntPtr.Zero; }
        if (msg == WM_COMMAND) {
            wmCommandCount++;
            if (ButtonClick != null) ButtonClick();
            // Phase 4: dispatch by control ID (LOWORD of wParam) -> the matching
            // managed control's Click event. Static-field lookup keeps the
            // model faithful (a real WinForms form maps id -> control).
            int cid = (int)(wParam.ToInt64() & 0xFFFFL);
            // Phase 8: the notification code is the HIWORD of wParam -- a
            // ComboBox/ListBox sends CBN_SELCHANGE/LBN_SELCHANGE to its parent
            // when the user changes the selection. Route it into the matching
            // dialog control's SelectionChanged event.
            int notif = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            if (P4A != null && cid == P4A.Id) P4A.RaiseClick();
            else if (P4B != null && cid == P4B.Id) P4B.RaiseClick();
            else if (P4C != null && cid == P4C.Id) P4C.RaiseClick();
            else if (P7Btn != null && cid == P7Btn.Id) P7Btn.RaiseClick();
            else if (P8Combo != null && cid == P8Combo.Id && (notif == CBN_SELCHANGE || notif == LBN_SELCHANGE)) P8Combo.RaiseSelectionChanged();
            else if (P8List != null && cid == P8List.Id && (notif == CBN_SELCHANGE || notif == LBN_SELCHANGE)) P8List.RaiseSelectionChanged();
            // Phase 37: a double-click on a ListBox sends WM_COMMAND with the
            // LBN_DBLCLK notification code in the HIWORD (the same Phase-8
            // WM_COMMAND channel -- a real user double-click or a posted
            // injection). Route it into the WfListBox DoubleClick event.
            else if (P37List != null && cid == P37List.Id && notif == LBN_DBLCLK) P37List.RaiseDoubleClick();
            // Phase 38: a double-click on a ComboBox sends WM_COMMAND with the
            // CBN_DBLCLK notification code in the HIWORD (the same Phase-8
            // WM_COMMAND channel -- CBN_DBLCLK shares its value 2 with
            // LBN_DBLCLK; a real user double-click or a posted injection).
            // Route it into the WfComboBox DoubleClick event.
            else if (P38Combo != null && cid == P38Combo.Id && notif == CBN_DBLCLK) P38Combo.RaiseDoubleClick();
            // Phase 40: an EDIT sends EN_CHANGE (0x300) to its parent as WM_COMMAND
            // when its text changes (a real keystroke/undo or the headless posted
            // injection). The RichEdit-only EM_SETEVENTMASK is IGNORED by a plain
            // EDIT (probed: returns 0 and EN_CHANGE still fires -- the phase-10
            // wmCommandCount lesson does NOT apply to WM_SETTEXT, which fires no
            // synchronous EN_CHANGE; the deterministic headless route is the
            // POSTED WM_COMMAND + drain). Route it into the WfTextBox TextChanged
            // event.
            else if (P40Edit != null && cid == P40Edit.Id && notif == EN_CHANGE) P40Edit.RaiseTextChanged();
            // Phase 42: a RichEdit20W sends EN_CHANGE to its parent as WM_COMMAND
            // when its text changes. Unlike the plain EDIT (which IGNORES the
            // RichEdit-only EM_SETEVENTMASK -- the Phase-40 note), the rich edit
            // GENUINELY honors it (probed): with EM_SETEVENTMASK(ENM_CHANGE) set,
            // an injected WM_CHAR delivers a SYNCHRONOUS WM_COMMAND(id | EN_CHANGE
            // <<16) into this WndProc during the SendMessageW -- the REAL event
            // route. Route it into the WfRichTextBox TextChanged event.
            else if (P42Rich != null && cid == P42Rich.Id && notif == EN_CHANGE) P42Rich.RaiseTextChanged();
            // Stage 10: a menu-id command (the user clicked a menu item -> the
            // OS posts WM_COMMAND with LOWORD = the item id) routes into the
            // matching MenuItem's Click event.
            else if (P10New != null && cid == P10New.Id) P10New.RaiseClick();
            else if (P10Open != null && cid == P10Open.Id) P10Open.RaiseClick();
            else if (P10About != null && cid == P10About.Id) P10About.RaiseClick();
            // Phase 32: a toolbar-button command (a real mouse click on the
            // toolbar relays WM_COMMAND with LOWORD = the button id -- the
            // winprog.org "identical to the menu" rule -- and the posted
            // injection uses the same channel) routes into the matching
            // WfToolButton's Click event, the Phase-10 menu dispatch shape.
            else if (P32New != null && cid == P32New.Id) P32New.RaiseClick();
            else if (P32Open != null && cid == P32Open.Id) P32Open.RaiseClick();
            else if (P32About != null && cid == P32About.Id) P32About.RaiseClick();
            return IntPtr.Zero;
        }
        if (msg == WM_KEYDOWN) {
            // Stage 3: keyboard focus -- a posted WM_KEYDOWN (from a simulated
            // keystroke) reaches the focused window's WndProc. We count the
            // VK_F1 marker so the accept path is deterministic.
            wmKeydownCount++;
            // Phase 4: raise the KeyDown event for the target controls on the
            // VK_F2 marker (the phase-4 key-injection sentinel).
            if (wParam.ToInt64() == VK_F2)
            {
                if (P4T != null) P4T.RaiseKeyDown();
                if (P4A != null) P4A.RaiseKeyDown();
            }
            return IntPtr.Zero;
        }
        if (msg == WF_SET_TEXT) {
            // Stage 3: "post a text set" acceptance. WM_SETTEXT cannot be
            // POSTED (sync-only message), so a custom posted message drives
            // the real WM_SETTEXT + GetWindowTextW round-trip on the TextBox.
            IntPtr tb = Marshal.StringToHGlobalUni("Wf3Posted");
            SendMessageW(EditHwnd, WM_SETTEXT, IntPtr.Zero, tb);
            Marshal.FreeHGlobal(tb);
            IntPtr pb = Marshal.AllocHGlobal(256);
            postedTextLen = GetWindowTextW(EditHwnd, pb, 256);
            postedText = Marshal.PtrToStringUni(pb);
            Marshal.FreeHGlobal(pb);
            return IntPtr.Zero;
        }
        if (msg == WF_PROGRESS_STEP) {
            // Stage 9: the headless "user advances progress" interaction. The
            // posted wParam is the step (1..n); the handler advances the
            // progress bar to step*25 (PBM_SETPOS), re-reads the position
            // (PBM_GETPOS -- proves the bar accepted the write), then mirrors
            // the new position into the status bar text (SB_SETTEXT +
            // SB_GETTEXT round-trip): progress drives status.
            int step = (int)wParam.ToInt64();
            int newPos = step * 25;
            SendMessageW(P9ProgressHwnd, PBM_SETPOS, new IntPtr(newPos), IntPtr.Zero);
            int got = (int)SendMessageW(P9ProgressHwnd, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
            IntPtr st = Marshal.StringToHGlobalUni("Progress: " + got + "%");
            SendMessageW(P9StatusHwnd, SB_SETTEXT, IntPtr.Zero, st);
            Marshal.FreeHGlobal(st);
            IntPtr sb = Marshal.AllocHGlobal(256);
            SendMessageW(P9StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb);
            string sText = Marshal.PtrToStringUni(sb);
            Marshal.FreeHGlobal(sb);
            P9Seq = P9Seq + "step" + step + "/pos" + got + "/status" + sText + ";";
            P9StepCount++;
            return IntPtr.Zero;
        }
        if (msg == WF_STATUS_UPDATE) {
            // Phase 31: the headless "user updates a status part" interaction.
            // The posted wParam is the PART INDEX; the handler writes
            // "StatusX" into that part (SB_SETTEXT with wParam = part index)
            // and reads it back (SB_GETTEXT) -- the W-variant per-part channel.
            int part31 = (int)wParam.ToInt64();
            IntPtr st31 = Marshal.StringToHGlobalUni("StatusX");
            SendMessageW(P9StatusHwnd, SB_SETTEXT, new IntPtr(part31), st31);
            Marshal.FreeHGlobal(st31);
            IntPtr sb31 = Marshal.AllocHGlobal(256);
            SendMessageW(P9StatusHwnd, SB_GETTEXT, new IntPtr(part31), sb31);
            P31Updated = Marshal.PtrToStringUni(sb31);
            Marshal.FreeHGlobal(sb31);
            return IntPtr.Zero;
        }
        if (msg == WF_GRID_COLCLICK) {
            // Phase 33: the headless "user clicked a column header" interaction.
            // The posted wParam is the clicked column index; the handler raises
            // the grid's ColumnHeaderClick for that column (a real header click
            // would deliver HDN_ITEMCLICK through WM_NOTIFY, but the simplified
            // surface posts the custom message -- the phase-4 post/drain pattern).
            int col33 = (int)wParam.ToInt64();
            if (P33Grid != null) P33Grid.RaiseColumnHeaderClick(col33);
            return IntPtr.Zero;
        }
        if (msg == WF_P34_STEP) {
            // Phase 34: the headless "user advances the progress bar"
            // interaction. The posted step advances the bar (PBM_STEPIT),
            // re-reads the position (PBM_GETPOS), then mirrors "P34: <pos>%"
            // into status part 0 (SB_SETTEXT + SB_GETTEXT round-trip, the
            // phase-9/31 channel): progress drives status.
            if (P34Bar != null) P34Bar.PerformStep();
            int pos34 = P34Bar.Value;
            IntPtr st34 = Marshal.StringToHGlobalUni("P34: " + pos34 + "%");
            SendMessageW(P9StatusHwnd, SB_SETTEXT, IntPtr.Zero, st34);
            Marshal.FreeHGlobal(st34);
            IntPtr sb34 = Marshal.AllocHGlobal(256);
            SendMessageW(P9StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb34);
            P34Status = Marshal.PtrToStringUni(sb34);
            Marshal.FreeHGlobal(sb34);
            P34Seq = P34Seq + "pos" + pos34 + ";";
            return IntPtr.Zero;
        }
        if (msg == WF_TAB35_TEXT) {
            // Phase 35: the headless "user clicks a tab" interaction. The
            // posted wParam is the page index; the handler re-reads that tab's
            // text (TCM_GETITEMW through the WfTabControl text surface) -- the
            // read happens INSIDE the pump exactly like a real TCN_SELCHANGE
            // handler reading the selected page's text at fire time.
            int idx35 = (int)wParam.ToInt64();
            if (P35Tab != null)
                P35Seq = P35Seq + "tab" + idx35 + "=" + P35Tab.GetTabText(idx35) + ";";
            return IntPtr.Zero;
        }
        if (msg == WF_CLIP_SET) {
            // Phase 12: COPY. OpenClipboard with a bounded retry -- the only
            // real-world failure is another process holding the clipboard open
            // (transient session contention); the retry (200 x 2 ms, the phase-6
            // poll shape) makes both runs deterministic. On success:
            // EmptyClipboard clears the old content, SetClipboardData stores the
            // CF_UNICODETEXT handle (StringToHGlobalUni -- on success the SYSTEM
            // owns the memory, on failure WE free it), CloseClipboard releases
            // the clipboard.
            P12OpenOk = false;
            int clpPolls = 0;
            while (!P12OpenOk && clpPolls < 200)
            {
                P12OpenOk = OpenClipboard(IntPtr.Zero) != 0;
                if (!P12OpenOk) { Thread.Sleep(2); clpPolls++; }
            }
            P12EmptyOk = false; P12SetOk = false; P12CloseOk = false;
            if (P12OpenOk)
            {
                P12EmptyOk = EmptyClipboard() != 0;
                IntPtr hg = Marshal.StringToHGlobalUni("Wf12Clip");
                IntPtr sd = SetClipboardData(CF_UNICODETEXT, hg);
                P12SetOk = sd != IntPtr.Zero;
                if (!P12SetOk) Marshal.FreeHGlobal(hg);
                P12CloseOk = CloseClipboard() != 0;
            }
            return IntPtr.Zero;
        }
        if (msg == WF_CLIP_GET) {
            // Phase 12: PASTE. OpenClipboard again (the same bounded retry),
            // GetClipboardData(CF_UNICODETEXT) returns the HGLOBAL, GlobalLock
            // pins it, PtrToStringUni reads the text back, GlobalUnlock releases
            // it, CloseClipboard closes. The handle is system-owned (never freed
            // by us).
            P12Open2Ok = false;
            int clgPolls = 0;
            while (!P12Open2Ok && clgPolls < 200)
            {
                P12Open2Ok = OpenClipboard(IntPtr.Zero) != 0;
                if (!P12Open2Ok) { Thread.Sleep(2); clgPolls++; }
            }
            P12GetOk = false; P12Close2Ok = false; P12ClipText = ""; P12ClipLen = 0;
            if (P12Open2Ok)
            {
                IntPtr hg = GetClipboardData(CF_UNICODETEXT);
                P12GetOk = hg != IntPtr.Zero;
                if (P12GetOk)
                {
                    IntPtr p = GlobalLock(hg);
                    P12ClipText = Marshal.PtrToStringUni(p);
                    P12ClipLen = P12ClipText.Length;
                    GlobalUnlock(hg);
                }
                P12Close2Ok = CloseClipboard() != 0;
            }
            return IntPtr.Zero;
        }
        if (msg == WF_WORKER_MSG) {
            // Phase 13: cross-thread message. A WORKER thread posted this; the
            // MAIN thread's pump dispatches it into THIS WndProc (which runs on
            // the main thread). Each marshaled message increments the
            // Interlocked UI counter and updates the TextBox text (WM_SETTEXT +
            // GetWindowTextW readback) -- all UI state is touched on the UI
            // thread, the WinForms threading rule.
            int seq = (int)wParam.ToInt64();
            P13Seq = P13Seq + "w" + seq + ";";
            Interlocked.Increment(ref P13UiCount);
            IntPtr st = Marshal.StringToHGlobalUni("step" + seq);
            SendMessageW(EditHwnd, WM_SETTEXT, IntPtr.Zero, st);
            Marshal.FreeHGlobal(st);
            IntPtr pb = Marshal.AllocHGlobal(64);
            P13UiTextLen = GetWindowTextW(EditHwnd, pb, 64);
            P13UiText = Marshal.PtrToStringUni(pb);
            Marshal.FreeHGlobal(pb);
            return IntPtr.Zero;
        }
        if (msg == WM_NOTIFY) {
            // Phase 16: the tab control notifies its parent on a user tab click
            // (WM_NOTIFY, wParam = the control id, lParam = an NMHDR* whose code
            // is TCN_SELCHANGE). Headless acceptance POSTS WM_NOTIFY (the phase-8
            // posted-notification pattern); the id arrives in wParam and the
            // notification is routed into the matching WfTabControl's
            // SelectedIndexChanged -- which re-reads the CURRENT selection
            // (TCM_GETCURSEL), so the event observes the real page at fire time.
            P16NotifyCount++;
            int tid = (int)(wParam.ToInt64() & 0xFFFFL);
            if (P16Tab != null && tid == P16Tab.Id) P16Tab.RaiseSelectionChanged();
            // Phase 21: the list-view's parent notification (WM_NOTIFY, the
            // NMHDR code is LVN_ITEMCHANGED) routes into the matching
            // WfListView's SelectionChanged -- which re-reads the CURRENT
            // selection (LVM_GETITEMSTATE over the rows), so the event observes
            // the real list state at fire time.
            else if (P21List != null && tid == P21List.Id) { P21NotifyCount++; P21List.RaiseSelectionChanged(); }
            // Phase 90: the list-view's LVN_DBLCLK notification (the code
            // field of the NMHDR* in lParam, offset 12) routes into the
            // matching WfListView's DoubleClick event. Driven SYNCHRONOUSLY
            // via SendMessageW (no message pump -- no hang risk). The NMHDR
            // code is read through Marshal.Copy (the transpiled Marshal
            // surface has no ReadInt32).
            else if (P90List != null && tid == P90List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_DBLCLK) P90List.RaiseDoubleClick();
            }
            // Phase 91: LVN_COLUMNCLICK (the same NMHDR code read, the
            // column-click notification) -- synchronous, same shape as 90.
            else if (P91List != null && tid == P91List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_COLUMNCLICK) P91List.RaiseColumnClick();
            }
            // Phase 92: LVN_ITEMACTIVATE (same NMHDR code read, the item
            // activation notification) -- synchronous, same shape.
            else if (P92List != null && tid == P92List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_ITEMACTIVATE) P92List.RaiseItemActivate();
            }
            // Phase 93: LVN_ENDLABELEDITW (same NMHDR code read) --
            // synchronous, same shape.
            else if (P93List != null && tid == P93List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_ENDLABELEDITW) P93List.RaiseLabelEditEnd();
            }
            // Phase 94: LVN_BEGINDRAG (same NMHDR code read) -- synchronous.
            else if (P94List != null && tid == P94List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_BEGINDRAG) P94List.RaiseBeginDrag();
            }
            // Phase 95: LVN_BEGINRDRAG (same NMHDR code read) -- synchronous.
            else if (P95List != null && tid == P95List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_BEGINRDRAG) P95List.RaiseBeginRDrag();
            }
            // Phase 96: LVN_KEYDOWN (same NMHDR code read) -- synchronous.
            else if (P96List != null && tid == P96List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_KEYDOWN) P96List.RaiseKeyDownNotify();
            }
            // Phase 97: LVN_INSERTITEM (same NMHDR code read) -- synchronous.
            else if (P97List != null && tid == P97List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_INSERTITEM) P97List.RaiseItemInserted();
            }
            // Phase 98: LVN_DELETEITEM (same NMHDR code read) -- synchronous.
            else if (P98List != null && tid == P98List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_DELETEITEM) P98List.RaiseItemDeleted();
            }
            // Phase 99: LVN_DELETEALLITEMS (same NMHDR code read) -- synchronous.
            else if (P99List != null && tid == P99List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_DELETEALLITEMS) P99List.RaiseAllItemsDeleted();
            }
            // Phase 100: LVN_ITEMCHANGED (same NMHDR code read) -- synchronous.
            else if (P100List != null && tid == P100List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_ITEMCHANGED) P100List.RaiseItemChanged();
            }
            // Phase 101: LVN_ITEMCHANGING (same NMHDR code read) -- synchronous.
            else if (P101List != null && tid == P101List.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == LVN_ITEMCHANGING) P101List.RaiseItemChanging();
            }
            // Phase 102: TVN_SELCHANGED (same NMHDR code read) -- synchronous.
            else if (P102Tree != null && tid == P102Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_SELCHANGED) P102Tree.RaiseSelChangedSync();
            }
            // Phase 103: TVN_SELCHANGING (same NMHDR code read) -- synchronous.
            else if (P103Tree != null && tid == P103Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_SELCHANGING) P103Tree.RaiseSelChangingSync();
            }
            // Phase 104: TVN_ITEMEXPANDED (same NMHDR code read) -- synchronous.
            else if (P104Tree != null && tid == P104Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_ITEMEXPANDED) P104Tree.RaiseItemExpandedSync();
            }
            // Phase 105: TVN_ITEMEXPANDING (same NMHDR code read) -- synchronous.
            else if (P105Tree != null && tid == P105Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_ITEMEXPANDING) P105Tree.RaiseItemExpandingSync();
            }
            // Phase 106: TVN_ITEMCOLLAPSED (same NMHDR code read) -- synchronous.
            else if (P106Tree != null && tid == P106Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_ITEMCOLLAPSED) P106Tree.RaiseItemCollapsedSync();
            }
            // Phase 107: TVN_ITEMCOLLAPSING (same NMHDR code read) -- synchronous.
            else if (P107Tree != null && tid == P107Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_ITEMCOLLAPSING) P107Tree.RaiseItemCollapsingSync();
            }
            // Phase 108: TVN_BEGINLABELEDIT (same NMHDR code read) -- synchronous.
            else if (P108Tree != null && tid == P108Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_BEGINLABELEDIT) P108Tree.RaiseBeginLabelEditSync();
            }
            // Phase 109: TVN_ENDLABELEDIT (same NMHDR code read) -- synchronous.
            else if (P109Tree != null && tid == P109Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_ENDLABELEDIT) P109Tree.RaiseEndLabelEditSync();
            }
            // Phase 110: TVN_DELETEITEM (same NMHDR code read) -- synchronous.
            else if (P110Tree != null && tid == P110Tree.Id)
            {
                int[] nmh = new int[4];
                if (lParam != IntPtr.Zero) Marshal.Copy(lParam, nmh, 0, 4);
                if (nmh[3] == TVN_DELETEITEM) P110Tree.RaiseItemDeletedSync();
            }
            return IntPtr.Zero;
        }
        if (msg == WM_PAINT) {
            // Standard paint protocol: BeginPaint validates the update region
            // and returns a DC; TextOutW draws the text; EndPaint releases.
            PAINTSTRUCT ps = new PAINTSTRUCT();
            IntPtr hdc = BeginPaint(hwnd, out ps);
            beginPaintOk = hdc != IntPtr.Zero;
            int toRet = TextOutW(hdc, 10, 10, "Wf1Draw", 7);
            textOutOk = toRet != 0;
            paintedText = "Wf1Draw";

            // Phase 14: the GDI font + DrawText sequence, in the WM_PAINT DC.
            // CreateFontW builds the font (height -16, regular weight, default
            // face "Arial"); SelectObject selects it into the paint DC and
            // returns the PREVIOUS object (the restore target); DrawTextW draws
            // the phase text with DT_SINGLELINE into a RECT (the "Control
            // DrawText" surface); GetTextExtentPoint32W measures the SAME text
            // in the SELECTED font (SIZE cx/cy); the previous font is restored
            // and the created font deleted. P14Seq records the step order; the
            // draw height and extent are native values identical on both sides.
            P14Seq = "";
            IntPtr font = CreateFontW(FONT_HEIGHT, 0, 0, 0, FW_NORMAL, 0, 0, 0,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                DEFAULT_QUALITY, DEFAULT_PITCH, "Arial");
            P14FontCreated = font != IntPtr.Zero;
            P14Seq = P14Seq + "create;";
            IntPtr prevFont = SelectObject(hdc, font);
            P14SelectOk = prevFont != IntPtr.Zero;
            P14Seq = P14Seq + "select;";
            RECT drc = new RECT();
            drc.left = 10; drc.top = 30; drc.right = 300; drc.bottom = 60;
            int dh = DrawTextW(hdc, "Wf14Draw", 8, ref drc, DT_SINGLELINE | DT_NOCLIP);
            P14DrawHeight = dh;
            P14DrawOk = dh != 0;
            P14Seq = P14Seq + "draw;";
            SIZE sz = new SIZE();
            int exRet = GetTextExtentPoint32W(hdc, "Wf14Draw", 8, out sz);
            P14ExtOk = exRet != 0;
            P14ExtCx = sz.cx;
            P14ExtCy = sz.cy;
            P14Seq = P14Seq + "extent;";
            IntPtr back = SelectObject(hdc, prevFont);
            P14RestoreOk = back != IntPtr.Zero;
            P14Seq = P14Seq + "restore;";
            P14DeleteOk = DeleteObject(font) != 0;
            P14Seq = P14Seq + "delete;";

            if (wParam.ToInt64() == PAINT_MARKER) wmPaintCount++;
            EndPaint(hwnd, ref ps);
            return IntPtr.Zero;
        }
        if (msg == WF_PANEL_BTN) { P15FormNotify++; P15Seq = P15Seq + "form:notify;"; return IntPtr.Zero; }
        if (msg == WF_GROUP_NOTIFY) { P19FormNotify++; P19GSeq = P19GSeq + "form:notify;"; return IntPtr.Zero; }
        if (msg == WF_VSCROLL_STEP) {
            // Phase 17: the headless "user scrolls" interaction on the position
            // channel. wParam = the target position; the handler writes it
            // (SetScrollPos), re-reads the position (GetScrollPos -- the
            // readback proves the scrollbar accepted the write), then mirrors
            // it into the status bar text (SB_SETTEXT + SB_GETTEXT round-trip,
            // the stage-9 channel): scroll drives status.
            int target = (int)wParam.ToInt64();
            SetScrollPos(P17ScrollHwnd, SB_CTL, target, false);
            int got = GetScrollPos(P17ScrollHwnd, SB_CTL);
            string s17 = P17MirrorStatus(got);
            P17Seq = P17Seq + "step" + target + "/pos" + got + "/status" + s17 + ";";
            P17StepCount++;
            return IntPtr.Zero;
        }
        if (msg == WM_VSCROLL) {
            // Phase 17: the parent notification a real scrollbar sends when the
            // USER scrolls (WM_VSCROLL, LOWORD(wParam) = the scroll code).
            // Headless acceptance POSTS WM_VSCROLL to the form (the phase-8
            // posted-notification pattern); a thumb code (SB_THUMBTRACK /
            // SB_THUMBPOSITION) carries the new thumb position in HIWORD(wParam)
            // -- the handler writes it (SetScrollPos), re-reads the real
            // position (GetScrollPos, so the event observes the thumb at fire
            // time) and mirrors it into the status bar text.
            int code17 = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb17 = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            if (code17 == SB_THUMBTRACK || code17 == SB_THUMBPOSITION)
            {
                SetScrollPos(P17ScrollHwnd, SB_CTL, thumb17, false);
            }
            int got17 = GetScrollPos(P17ScrollHwnd, SB_CTL);
            string s17b = P17MirrorStatus(got17);
            P17Seq = P17Seq + (code17 == SB_THUMBTRACK ? "track" : "thumb") + "/pos" + got17 + "/status" + s17b + ";";
            P17VscrollCount++;
            return IntPtr.Zero;
        }
        if (msg == WM_HSCROLL) {
            // Phase 20: the parent notification a horizontal TrackBar sends
            // when the USER scrolls (WM_HSCROLL, LOWORD(wParam) = the scroll
            // code). Headless acceptance POSTS WM_HSCROLL to the form (the
            // phase-8 posted-notification pattern); a thumb code (SB_THUMBTRACK
            // / SB_THUMBPOSITION) carries the new thumb position in
            // HIWORD(wParam) -- the handler writes it (TBM_SETPOS), re-reads
            // the real position (TBM_GETPOS, so the event observes the thumb at
            // fire time) and mirrors it into the status bar text.
            int code20 = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb20 = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            if (code20 == SB_THUMBTRACK || code20 == SB_THUMBPOSITION)
            {
                SendMessageW(P20TrackHwnd, TBM_SETPOS, new IntPtr(1), new IntPtr(thumb20));
            }
            int got20 = (int)SendMessageW(P20TrackHwnd, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
            string s20 = P20MirrorStatus(got20);
            P20Seq = P20Seq + (code20 == SB_THUMBTRACK ? "track" : "thumb") + "/pos" + got20 + "/status" + s20 + ";";
            P20HscrollCount++;
            return IntPtr.Zero;
        }
        if (msg == WM_CLOSE) { wmCloseCount++; closeDestroyed = DestroyWindow(hwnd) != 0; return IntPtr.Zero; }
        if (msg == P24_CB_MSG) { P24IconCbCount++; return IntPtr.Zero; }
        if (msg == WM_TIMER) {
            // Phase 29: a timer tick. The system posts WM_TIMER to the timer's
            // window when the interval fires (wParam = the timer id). The
            // headless test injects WM_TIMER directly with SendMessageW
            // (synchronous to this WndProc, bypassing the message queue), so
            // each injection increments the tick count deterministically.
            P29TimerTicks++;
            return IntPtr.Zero;
        }
        if (msg == WM_GETMINMAXINFO) {
            // Phase 44: the window layout surface. The system sends
            // WM_GETMINMAXINFO before sizing a top-level window (create/show/
            // move) with lParam = a MINMAXINFO* (the caller's 40-byte 5-POINT
            // buffer). The P44Active GUARD is a hard requirement: when the
            // test is NOT running its loopback, the natural OS-driven message's
            // lParam points at the OS's INTERNAL buffer -- writing into it
            // (Marshal.Copy) would corrupt the window's sizing state, so the
            // guard routes those to DefWindowProcW (which fills the
            // resolution-dependent fields -- never asserted). Under the armed
            // loopback the WndProc writes ONLY the self-set track fields
            // ([6..9] of the pinned int[10]) into the caller's buffer and
            // returns zero (the Form.MinimumSize/MaximumSize backing).
            if (!P44Active) return DefWindowProcW(hwnd, msg, wParam, lParam);
            P44Track++;
            Marshal.Copy(P44Write, 0, lParam, 10);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 15: the PANEL's window proc -- installed on a STATIC window via
    // SetWindowLongPtrW(GWLP_WNDPROC) so the container has its OWN event
    // surface. It handles its own posted message (WF_PANEL_MSG) and the
    // WM_COMMAND its child BUTTON sends -- a container receives its children's
    // events: the command raises the panel's "button clicked" (P15Seq append)
    // and BUBBLES WF_PANEL_BTN to the FORM (the nested event chain button ->
    // panel -> form). Every unhandled message falls to DefWindowProcW (the
    // same fallback every WndProc in this file uses).
    public static IntPtr PanelWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WF_PANEL_MSG) { P15MsgCount++; P15Seq = P15Seq + "panel:msg;"; return IntPtr.Zero; }
        if (msg == WM_COMMAND)
        {
            int cid = (int)(wParam.ToInt64() & 0xFFFFL);
            if (cid == ID_PANEL_BTN)
            {
                P15ClickCount++;
                P15Seq = P15Seq + "panel:btn;";
                PostMessageW(GetParent(hwnd), WF_PANEL_BTN, IntPtr.Zero, IntPtr.Zero);
            }
            return IntPtr.Zero;
        }
        if (msg == WM_VSCROLL || msg == WM_HSCROLL)
        {
            // Phase 36: the PANEL's window-scrollbar event surface. A real
            // user scroll on a WINDOW scroll bar (the WS_VSCROLL/WS_HSCROLL
            // style bars) sends WM_VSCROLL / WM_HSCROLL to the window (a
            // scrollbar control sends to its PARENT -- the Phase-17 rule --
            // but a window's OWN bars send to ITSELF). Headless acceptance
            // POSTS WM_VSCROLL / WM_HSCROLL to the P36 panel (the phase-8
            // posted-notification pattern); LOWORD(wParam) = the scroll code,
            // a thumb code (SB_THUMBTRACK / SB_THUMBPOSITION) carries the new
            // thumb in HIWORD(wParam). The handler applies the scroll: line
            // codes (SB_LINEUP / SB_LINEDOWN) move by P36_LINE, page codes
            // (SB_PAGEUP / SB_PAGEDOWN) by P36_PAGE, thumb codes write the
            // thumb directly -- each delta applied relative to the CURRENT
            // position (GetScrollPos + delta -> SetScrollPos, so the native
            // range clamp still applies) and the resulting position read back
            // (GetScrollPos -- the event observes the thumb at fire time).
            // P36Seq records code+position; the counters split vertical /
            // horizontal. The result is returned EXPLICITLY (IntPtr.Zero) --
            // NOT forwarded to DefWindowProcW (probed: DefWindowProc does NOT
            // auto-scroll a window scroll bar, it returns 0 and the position
            // stays 0 -- so the explicit handler is the whole surface). The
            // `hwnd != P36PanelHwnd` guard keeps the Phase-15 panel (subclassed
            // with this SAME proc) from treating its own scroll-bar messages
            // as P36 events -- only the P36 panel's messages are handled here,
            // everything else falls to DefWindowProcW.
            if (hwnd != P36PanelHwnd) return DefWindowProcW(hwnd, msg, wParam, lParam);
            int bar36 = msg == WM_VSCROLL ? SB_VERT : SB_HORZ;
            int code36 = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb36 = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            int delta36 = 0;
            if (code36 == SB_LINEUP || code36 == SB_LINEDOWN) delta36 = code36 == SB_LINEUP ? -P36_LINE : P36_LINE;
            else if (code36 == SB_PAGEUP || code36 == SB_PAGEDOWN) delta36 = code36 == SB_PAGEUP ? -P36_PAGE : P36_PAGE;
            if (code36 == SB_THUMBTRACK || code36 == SB_THUMBPOSITION)
            {
                SetScrollPos(hwnd, bar36, thumb36, false);
            }
            else if (delta36 != 0)
            {
                int cur36 = GetScrollPos(hwnd, bar36);
                SetScrollPos(hwnd, bar36, cur36 + delta36, false);
            }
            int pos36 = GetScrollPos(hwnd, bar36);
            if (msg == WM_VSCROLL)
            {
                P36Seq = P36Seq + "v:" + P36CodeName(bar36, code36) + pos36 + ";";
                P36VscrollCount++;
            }
            else
            {
                P36Seq = P36Seq + "h:" + P36CodeName(bar36, code36) + pos36 + ";";
                P36HscrollCount++;
            }
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 36: the scroll-code name for the sequence oracle (the
    // LOWORD(wParam) code -> label). The codes share ONE space across both
    // bars (SB_LINEDOWN is numerically the horizontal "lineright" and SB_PAGEUP
    // the horizontal "pageleft" -- probed: the names differ by bar, the
    // vertical names are "lineup"/"linedown"/"pageup"/"pagedown" and the
    // horizontal names "lineleft"/"lineright"/"pageleft"/"pageright"). The
    // vertical/horizontal prefix ("v:"/"h:") on P36Seq plus this per-bar name
    // give each scroll an unambiguous label.
    public static string P36CodeName(int bar, int code)
    {
        bool horiz = bar == SB_HORZ;
        if (code == SB_LINEUP) return horiz ? "lineleft" : "lineup";
        if (code == SB_LINEDOWN) return horiz ? "lineright" : "linedown";
        if (code == SB_PAGEUP) return horiz ? "pageleft" : "pageup";
        if (code == SB_PAGEDOWN) return horiz ? "pageright" : "pagedown";
        if (code == SB_THUMBTRACK) return "track";
        if (code == SB_THUMBPOSITION) return "thumb";
        return "code" + code;
    }

    // Phase 19: the GROUP BOX's window proc -- installed on a BS_GROUPBOX
    // STATIC-frame window via SetWindowLongPtrW(GWLP_WNDPROC) so the container
    // has its OWN event surface. It handles its own posted message (WF_GROUP_MSG,
    // recorded as gb1:/gb2: by the box's OWN HWND) and BUBBLES WF_GROUP_NOTIFY
    // to its parent (the FORM -- the groupbox -> form nested chain, the phase-15
    // panel pattern). Every unhandled message falls to DefWindowProcW (the same
    // fallback every WndProc in this file uses).
    public static IntPtr WfGroupBoxWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WF_GROUP_MSG)
        {
            P19GSeq = P19GSeq + (hwnd == P19Group1.Handle ? "gb1:" : "gb2:") + "msg;";
            PostMessageW(GetParent(hwnd), WF_GROUP_NOTIFY, IntPtr.Zero, IntPtr.Zero);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 5: the child Form's independent window proc. It counts its OWN
    // WM_CREATE, handles its OWN posted message (WF_CHILD_MSG) and destroys
    // itself on WM_CLOSE -- proving the two windows do not share a proc.
    public static IntPtr ChildWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) { WmChildCreateCount++; return IntPtr.Zero; }
        if (msg == WF_CHILD_MSG) { ChildMsgCount++; return IntPtr.Zero; }
        if (msg == WM_CLOSE) { ChildCloseDestroyed = DestroyWindow(hwnd) != 0; return IntPtr.Zero; }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 6: MessageBoxW is a BLOCKING call -- in a headless test the box
    // runs on a WORKER thread. The worker stores the return value; the main
    // thread joins it (happens-before) and reads MsgBoxResult. The worker
    // ONLY calls MessageBoxW (no managed allocations -- the M-stage allocator
    // is single-threaded, same constraint as the threadcase workers).
    public static void MessageBoxWorker()
    {
        MsgBoxResult = MessageBoxW(IntPtr.Zero, "Wf6DialogText", "Wf6Caption", MB_OK);
    }

    // Phase 25: GetOpenFileNameW is BLOCKING (a modal open-file dialog), so
    // the headless acceptance runs it on a WORKER thread, exactly like the
    // phase-6 MessageBoxW. The worker calls ONLY GetOpenFileNameW with the
    // pre-built pinned OPENFILENAMEW pointer (P25OfnPtr -- no managed
    // allocations on the worker; the M-stage allocator is single-threaded,
    // same constraint as the phase-6/13 workers). The main thread finds the
    // dialog by title, clicks its OK button, joins the worker (happens-before)
    // and reads P25OpenResult + the struct readbacks.
    public static void P25OpenFileWorker()
    {
        P25OpenResult = GetOpenFileNameW(P25OfnPtr);
    }

    // Phase 26: ChooseColorW is BLOCKING (a modal color dialog), so the
    // headless acceptance runs it on a WORKER thread, exactly like the
    // phase-25 file dialog. The worker FIRST records its OWN thread id
    // (P26WorkerTid -- the main thread needs it to identify the dialog: the
    // color dialog's caption is a LOCALIZED system string, unsearchable by
    // title, so the "#32770" class walk is filtered by the creating thread)
    // and then calls ONLY ChooseColorW with the pre-built pinned CHOOSECOLORW
    // pointer (P26CcwPtr -- no managed allocations on the worker; the M-stage
    // allocator is single-threaded, same constraint as the phase-6/25
    // workers).
    public static void P26ColorWorker()
    {
        P26WorkerTid = GetCurrentThreadId();
        P26ColorResult = ChooseColorW(P26CcwPtr);
    }

    // Phase 27: ChooseFontW is BLOCKING (a modal font dialog), so the headless
    // acceptance runs it on a WORKER thread, exactly like the phase-25/26
    // dialogs. The worker FIRST records its OWN thread id (P27WorkerTid -- the
    // main thread needs it to identify the dialog: the font dialog's caption
    // is a LOCALIZED system string, unsearchable by title, so the "#32770"
    // class walk is filtered by the creating thread) and then calls ONLY
    // ChooseFontW with the pre-built pinned CHOOSEFONTW pointer (P27CfPtr -- no
    // managed allocations on the worker; the M-stage allocator is
    // single-threaded, same constraint as the phase-6/25/26 workers).
    public static void P27FontWorker()
    {
        P27WorkerTid = GetCurrentThreadId();
        P27FontResult = ChooseFontW(P27CfPtr);
    }

    // Phase 13: the cross-thread marshaling WORKER. It posts WF_WORKER_MSG to
    // the main form (wParam = the step index) -- the "background thread
    // reports progress" pattern -- counting each post with Interlocked.
    // Increment. M-stage constraint: the worker must NOT allocate (the
    // managed allocator is single-threaded) -- PostMessageW is a scalar
    // P/Invoke and Interlocked is a raw icall, both allocation-free. The main
    // thread Joins it before draining, so the FIFO post order is the dispatch
    // order (deterministic on both sides).
    public static void Worker13()
    {
        for (int i = 0; i < 4; i++)
        {
            PostMessageW(P13FormHwnd, WF_WORKER_MSG, new IntPtr(i), IntPtr.Zero);
            Interlocked.Increment(ref P13PostedCount);
        }
    }

    // Phase 6: the MODAL window's own WndProc. It destroys itself on WM_CLOSE
    // (the simplified modal loop below exits when the modal is gone) and
    // records the create/close/destroy transitions on its OWN counters.
    public static IntPtr ModalWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) { ModalCreateCount++; return IntPtr.Zero; }
        if (msg == WM_CLOSE) { ModalCloseCount++; ModalDestroyed = DestroyWindow(hwnd) != 0; return IntPtr.Zero; }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 11: the MDI FRAME window's WndProc. The frame is the top-level MDI
    // container (WS_CLIPCHILDREN); it counts its OWN WM_CREATE.
    public static IntPtr MdiFrameWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) { MdiFrameCreateCount++; return IntPtr.Zero; }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 11: the MDI CLIENT window's WndProc. The client is the single
    // parent of all MDI children (the simplified MDICLIENT surface). It owns
    // the MDI activation channel: a posted WM_MDIACTIVATE (wParam = the child
    // being activated) records the activation order (MdiActSeq) and activates
    // the child via SetActiveWindow (the simplified child-activation path).
    public static IntPtr MdiClientWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_MDIACTIVATE) {
            MdiActiveChild = wParam;
            if (MdiChildA != null && wParam == MdiChildA.Handle) MdiActSeq = MdiActSeq + "A;";
            else if (MdiChildB != null && wParam == MdiChildB.Handle) MdiActSeq = MdiActSeq + "B;";
            else if (MdiChildC != null && wParam == MdiChildC.Handle) MdiActSeq = MdiActSeq + "C;";
            SetActiveWindow(wParam);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 11: the MDI CHILD windows' shared WndProc. All three children use
    // the SAME registered class + this proc; a posted WF_MDI_CHILD_MSG routes
    // into the matching WfMdiChild's OWN Click event by HWND (per-child event
    // isolation -- posting to child A fires ONLY A's handlers).
    public static IntPtr MdiChildWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) { MdiChildCreateCount++; return IntPtr.Zero; }
        if (msg == WF_MDI_CHILD_MSG) {
            MdiChildMsgCount++;
            if (MdiChildA != null && hwnd == MdiChildA.Handle) MdiChildA.RaiseClick();
            else if (MdiChildB != null && hwnd == MdiChildB.Handle) MdiChildB.RaiseClick();
            else if (MdiChildC != null && hwnd == MdiChildC.Handle) MdiChildC.RaiseClick();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Phase 18: managed radio-group exclusivity (the WinForms model --
    // RadioButton.Checked's setter unchecks the group's other radios in
    // code; native auto-radio exclusivity only applies to real user input,
    // which is not headless-testable). checkedId selects the one to check;
    // every OTHER radio in the group is cleared (BM_SETCHECK -- the same
    // state channel stage-3's checkbox uses; BM_CLICK on a hidden-window
    // radio was probed to hang the net48 oracle too).
    static void SetRadioChecked(IntPtr hR1, IntPtr hR2, IntPtr hR3, int checkedId)
    {
        SendMessageW(hR1, BM_SETCHECK, new IntPtr(checkedId == ID_RADIO1 ? 1 : 0), IntPtr.Zero);
        SendMessageW(hR2, BM_SETCHECK, new IntPtr(checkedId == ID_RADIO2 ? 1 : 0), IntPtr.Zero);
        SendMessageW(hR3, BM_SETCHECK, new IntPtr(checkedId == ID_RADIO3 ? 1 : 0), IntPtr.Zero);
    }

    // Phase 19: managed IN-GROUP radio exclusivity -- Phase 18's SetRadioChecked
    // SCOPED to ONE GroupBox: the function addresses ONLY the two radios of the
    // group it is called with (the container OWNS them; the GetParent ownership
    // is asserted in the Phase 19 block). checkedId selects the one to check;
    // the OTHER radio of the SAME group is cleared (BM_SETCHECK -- the same
    // state channel stage-3's checkbox uses). Radios of a DIFFERENT group are
    // never touched -- intra-group exclusivity AND inter-group independence
    // (the phase-18 group of three is now split across two real GroupBox
    // containers, so checking A2 then B1 then A1 leaves each group with exactly
    // one checked radio).
    static void SetGroupChecked(IntPtr h1, IntPtr h2, int id1, int id2, int checkedId)
    {
        SendMessageW(h1, BM_SETCHECK, new IntPtr(checkedId == id1 ? 1 : 0), IntPtr.Zero);
        SendMessageW(h2, BM_SETCHECK, new IntPtr(checkedId == id2 ? 1 : 0), IntPtr.Zero);
    }

    public static void Main()
    {
        IntPtr hModule = GetModuleHandleW(null);
        Console.WriteLine("module_nonzero=" + (hModule != IntPtr.Zero));

        // Register the "Form" window class (WNDPROC = our delegate thunk).
        WNDCLASSEX wc = new WNDCLASSEX();
        // cbSize = sizeof(WNDCLASSEXW) on x64 = 80 (5 int32 + 7 pointers, with
        // the pointer-alignment padding). Hard-coded literal -- Marshal.SizeOf
        // is not part of the transpiler's wired Marshal surface.
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        RootWndProc = WndProcImpl;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
        wc.hInstance = hModule;
        IntPtr clsName = Marshal.StringToHGlobalUni("Wf1FormClass");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("class_registered=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        // Create the Form window (hidden: WS_OVERLAPPED, no WS_VISIBLE).
        IntPtr hForm = CreateWindowExW(0, "Wf1FormClass", "Wf1Form",
            WS_OVERLAPPED, CW_USEDEFAULT, CW_USEDEFAULT, 320, 200,
            IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));

        // Child controls: a BUTTON (the "Button") and a STATIC (the "Label").
        IntPtr hButton = CreateWindowExW(0, "BUTTON", "Click me",
            WS_CHILD | WS_TABSTOP, 20, 40, 100, 30,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("button_created=" + (hButton != IntPtr.Zero));

        IntPtr hLabel = CreateWindowExW(0, "STATIC", "Hello from Wf1",
            WS_CHILD, 20, 90, 200, 20,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("label_created=" + (hLabel != IntPtr.Zero));

        // Stage 3: a TextBox is an EDIT common control (user32 "EDIT" class,
        // WS_CHILD + WS_TABSTOP + ES_AUTOHSCROLL) -- same CreateWindowExW path
        // as the Button, but the text lives in the EDIT's own native buffer
        // (WM_SETTEXT / GetWindowTextW round-trip).
        IntPtr hEdit = CreateWindowExW(0, "EDIT", "Wf3Edit",
            WS_CHILD | WS_TABSTOP | ES_AUTOHSCROLL, 20, 130, 160, 24,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        EditHwnd = hEdit;
        Console.WriteLine("textbox_created=" + (hEdit != IntPtr.Zero));

        // Stage 3: a CheckBox is a BUTTON common control with the
        // BS_AUTOCHECKBOX style; its on/off state is read via BM_GETCHECK.
        IntPtr hCheck = CreateWindowExW(0, "BUTTON", "Wf3Check",
            WS_CHILD | WS_TABSTOP | BS_AUTOCHECKBOX, 20, 170, 120, 24,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("checkbox_created=" + (hCheck != IntPtr.Zero));

        // Stage 2: ShowWindow transition returns (headless acceptance ends
        // HIDDEN -- SW_SHOW is exercised for its return value only).
        int swHide1 = ShowWindow(hForm, SW_HIDE);
        int swShow = ShowWindow(hForm, SW_SHOW);
        int swHide2 = ShowWindow(hForm, SW_HIDE);
        int visAfter = IsWindowVisible(hForm);
        // ShowWindow returns nonzero iff the window was previously visible;
        // normalize to the boolean shape (the exact nonzero value differs by
        // window state, but both sides run the same native call).
        Console.WriteLine("show_hide1_prev_visible=" + (swHide1 != 0));
        Console.WriteLine("show_prev_visible=" + (swShow != 0));
        Console.WriteLine("hide2_prev_visible=" + (swHide2 != 0));
        Console.WriteLine("visible_after=" + (visAfter != 0));

        // Stage 2: window title readback (GetWindowTextW into an IntPtr buffer
        // + PtrToStringUni -- both wired marshal paths).
        IntPtr buf = Marshal.AllocHGlobal(256);
        int tlen = GetWindowTextW(hForm, buf, 256);
        string title = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        Console.WriteLine("title_len=" + tlen);
        Console.WriteLine("window_title=" + title);

        // Stage 3: TextBox text round-trip. The EDIT starts with its window
        // name as text; WM_SETTEXT replaces it, GetWindowTextW reads it back.
        IntPtr ebuf = Marshal.AllocHGlobal(256);
        int elen0 = GetWindowTextW(hEdit, ebuf, 256);
        string etext0 = Marshal.PtrToStringUni(ebuf);
        Console.WriteLine("edit_initial_len=" + elen0);
        Console.WriteLine("edit_initial_text=" + etext0);

        IntPtr setText = Marshal.StringToHGlobalUni("Wf3Typed");
        IntPtr setRet = SendMessageW(hEdit, WM_SETTEXT, IntPtr.Zero, setText);
        Marshal.FreeHGlobal(setText);
        Console.WriteLine("edit_set_ok=" + (setRet != IntPtr.Zero));
        int elen1 = GetWindowTextW(hEdit, ebuf, 256);
        string etext1 = Marshal.PtrToStringUni(ebuf);
        Console.WriteLine("edit_len=" + elen1);
        Console.WriteLine("edit_text=" + etext1);

        // Stage 3: CheckBox state. BS_AUTOCHECKBOX toggles on BM_SETCHECK;
        // BM_GETCHECK reads the current check state (0 unchecked / 1 checked).
        IntPtr cs0 = SendMessageW(hCheck, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("check_state0=" + cs0.ToInt64());
        SendMessageW(hCheck, BM_SETCHECK, new IntPtr(1), IntPtr.Zero);
        IntPtr cs1 = SendMessageW(hCheck, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("check_state1=" + cs1.ToInt64());
        SendMessageW(hCheck, BM_SETCHECK, new IntPtr(0), IntPtr.Zero);
        IntPtr cs2 = SendMessageW(hCheck, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("check_state2=" + cs2.ToInt64());

        // Stage 3: keyboard focus -- SetFocus then GetFocus round-trip.
        // The result is a boolean (focus landed on the target), NOT a raw
        // handle, so the output is deterministic across runs.
        SetFocus(hEdit);
        IntPtr curFocus = GetFocus();
        Console.WriteLine("focus_is_edit=" + (curFocus == hEdit));

        Marshal.FreeHGlobal(ebuf);

        // Stage 3: PostMessage-simulated interactions. The user "types" into
        // the TextBox (WF_SET_TEXT custom message -> real WM_SETTEXT round-trip),
        // clicks the CheckBox (BM_SETCHECK), and presses a key (WM_KEYDOWN).
        // All are POSTED messages; the non-blocking PeekMessageW drain below
        // dispatches them deterministically without a blocking wait.
        int postSet = PostMessageW(hForm, WF_SET_TEXT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("inject_settext=" + (postSet != 0));
        int postCheck = PostMessageW(hCheck, BM_SETCHECK, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("inject_check=" + (postCheck != 0));
        int postKey = PostMessageW(hForm, WM_KEYDOWN, new IntPtr(VK_F1), IntPtr.Zero);
        Console.WriteLine("inject_key=" + (postKey != 0));

        // Drain the posted messages (PeekMessageW PM_REMOVE -- returns 0 when
        // the queue is empty, so this never blocks; bounded for safety).
        MSG msg3;
        int drained = 0;
        while (PeekMessageW(out msg3, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg3);
            DispatchMessageW(ref msg3);
            drained++;
            if (drained >= 200) break;
        }
        Console.WriteLine("posted_edit_len=" + postedTextLen);
        Console.WriteLine("posted_edit_text=" + postedText);
        IntPtr cs3 = SendMessageW(hCheck, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("check_state_posted=" + cs3.ToInt64());
        Console.WriteLine("keydown_count=" + wmKeydownCount);

        // ===================== Phase 4: event delegate system =====================
        // Managed Control model with field-backed Click/KeyDown events (custom
        // add/remove accessors -> Delegate.Combine/Remove, the delechain-proven
        // multicast path). WM_COMMAND dispatches by control id to the matching
        // control's Click event; WM_KEYDOWN raises KeyDown. Headless acceptance:
        // PostMessage WM_COMMAND/WM_KEYDOWN + PeekMessageW drain (stage-3 pattern),
        // trigger order recorded in a sequence string. Runs BEFORE the WM_CLOSE
        // path so the form window still exists for the posted messages.
        P4A = new WfControl(); P4A.Id = 0x101; P4A.Name = "A";
        P4B = new WfControl(); P4B.Id = 0x102; P4B.Name = "B";
        P4C = new WfControl(); P4C.Id = 0x103; P4C.Name = "C";
        P4T = new WfControl(); P4T.Id = 0x104; P4T.Name = "T";
        EventSeq = "";

        // Multicast: control A gets TWO handlers (subscription order preserved).
        P4A.Click += OnA1;
        P4A.Click += OnA2;
        // Per-control handlers: B and C each have their own handler PLUS a
        // handler shared across both controls (multi-control observation).
        P4B.Click += OnB1;
        P4C.Click += OnC1;
        P4B.Click += OnShared;
        P4C.Click += OnShared;
        // Multi-event: a control can also subscribe KeyDown.
        P4T.KeyDown += OnKey;
        P4A.KeyDown += OnKey2;

        Console.WriteLine("p4_click_subs_a=" + P4A.ClickCount);   // 2 (multicast)
        Console.WriteLine("p4_click_subs_b=" + P4B.ClickCount);   // 2 (B1 + shared)
        Console.WriteLine("p4_click_subs_c=" + P4C.ClickCount);   // 2 (C1 + shared)
        Console.WriteLine("p4_key_subs_t=" + P4T.KeyCount);       // 1

        // Simulated clicks: post WM_COMMAND with each control's id + a keydown.
        int postA = PostMessageW(hForm, WM_COMMAND, new IntPtr(P4A.Id), IntPtr.Zero);
        int postB = PostMessageW(hForm, WM_COMMAND, new IntPtr(P4B.Id), IntPtr.Zero);
        int postC = PostMessageW(hForm, WM_COMMAND, new IntPtr(P4C.Id), IntPtr.Zero);
        int postK = PostMessageW(hForm, WM_KEYDOWN, new IntPtr(VK_F2), IntPtr.Zero);
        Console.WriteLine("p4_inject=" + (postA != 0 && postB != 0 && postC != 0 && postK != 0));

        // Drain the phase-4 posted messages (non-blocking, stage-3 pattern).
        MSG msg4;
        int d4 = 0;
        while (PeekMessageW(out msg4, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg4);
            DispatchMessageW(ref msg4);
            d4++;
            if (d4 >= 200) break;
        }
        Console.WriteLine("p4_seq=" + EventSeq);   // A1;A2;B1;S;C1;S;K;K2;

        // Unsubscribe: remove A2 from A and the shared handler from C.
        P4A.Click -= OnA2;
        P4C.Click -= OnShared;
        Console.WriteLine("p4_click_subs_a_after=" + P4A.ClickCount);   // 1
        Console.WriteLine("p4_click_subs_c_after=" + P4C.ClickCount);   // 1

        int postA2 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P4A.Id), IntPtr.Zero);
        int postC2 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P4C.Id), IntPtr.Zero);
        Console.WriteLine("p4_inject2=" + (postA2 != 0 && postC2 != 0));
        int d5 = 0;
        while (PeekMessageW(out msg4, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg4);
            DispatchMessageW(ref msg4);
            d5++;
            if (d5 >= 200) break;
        }
        Console.WriteLine("p4_seq2=" + EventSeq);   // ...;A1;C1;

        // ===================== Phase 5: multi-window + layout + hierarchy =====================
        // Two windows, two WndProcs. The child Form is a SECOND CreateWindowExW
        // HWND with its OWN registered window class and its OWN WndProc
        // delegate (independent message handling), parented to the main form
        // via WS_CHILD + hwndParent -- the WinForms child-form relationship.
        WNDCLASSEX wc5 = new WNDCLASSEX();
        wc5.cbSize = 80;
        wc5.style = CS_HREDRAW | CS_VREDRAW;
        RootChildWndProc = ChildWndProcImpl;
        wc5.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootChildWndProc);
        wc5.hInstance = hModule;
        IntPtr cls5 = Marshal.StringToHGlobalUni("Wf1ChildClass");
        wc5.lpszClassName = cls5;
        ushort atom5 = RegisterClassExW(ref wc5);
        Console.WriteLine("child_class_registered=" + (atom5 != 0));
        Marshal.FreeHGlobal(cls5);

        IntPtr hChild = CreateWindowExW(0, "Wf1ChildClass", "Wf1Child",
            WS_CHILD, 40, 60, 200, 120,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        ChildHwnd = hChild;
        Console.WriteLine("child_created=" + (hChild != IntPtr.Zero));

        // Window relationship: GetParent exposes the parent chain. The child
        // Form and the Button/Label are children of the main form; the main
        // form itself is a top-level window (no owner) -> NULL.
        Console.WriteLine("child_parent_is_form=" + (GetParent(hChild) == hForm));
        Console.WriteLine("button_parent_is_form=" + (GetParent(hButton) == hForm));
        Console.WriteLine("label_parent_is_form=" + (GetParent(hLabel) == hForm));
        Console.WriteLine("form_parent_null=" + (GetParent(hForm) == IntPtr.Zero));

        // Layout: GetWindowRect (size query) + MoveWindow (position/size).
        // Child-window sizes are exact; positions are printed as DELTAS so the
        // output is deterministic (the screen origin differs by window station,
        // but the parent does not move between the two measurements).
        RECT rBtn0 = new RECT();
        int grBtn0 = GetWindowRect(hButton, out rBtn0);
        Console.WriteLine("getrect_ok=" + (grBtn0 != 0));
        Console.WriteLine("btn_size0=" + (rBtn0.right - rBtn0.left) + "x" + (rBtn0.bottom - rBtn0.top));

        RECT rChild0 = new RECT();
        int grChild0 = GetWindowRect(hChild, out rChild0);
        Console.WriteLine("child_getrect_ok=" + (grChild0 != 0));
        Console.WriteLine("child_size0=" + (rChild0.right - rChild0.left) + "x" + (rChild0.bottom - rChild0.top));

        int mvBtn = MoveWindow(hButton, 30, 50, 140, 40, true);
        Console.WriteLine("movewindow_ok=" + (mvBtn != 0));
        RECT rBtn1 = new RECT();
        GetWindowRect(hButton, out rBtn1);
        Console.WriteLine("btn_dx=" + (rBtn1.left - rBtn0.left));
        Console.WriteLine("btn_dy=" + (rBtn1.top - rBtn0.top));
        Console.WriteLine("btn_size1=" + (rBtn1.right - rBtn1.left) + "x" + (rBtn1.bottom - rBtn1.top));

        int mvChild = MoveWindow(hChild, 50, 70, 220, 140, false);
        Console.WriteLine("child_move_ok=" + (mvChild != 0));
        RECT rChild1 = new RECT();
        GetWindowRect(hChild, out rChild1);
        Console.WriteLine("child_dx=" + (rChild1.left - rChild0.left));
        Console.WriteLine("child_dy=" + (rChild1.top - rChild0.top));
        Console.WriteLine("child_size1=" + (rChild1.right - rChild1.left) + "x" + (rChild1.bottom - rChild1.top));

        // Headless acceptance: a posted message to the child HWND is dispatched
        // by the CHILD's OWN WndProc (the second delegate) -- independent
        // message handling. WM_CLOSE also routes through the child's proc; the
        // child dies BEFORE the form (a parent is destroyed after its children).
        int postChild = PostMessageW(hChild, WF_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
        int postChildClose = PostMessageW(hChild, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("inject_child_msg=" + (postChild != 0));
        Console.WriteLine("inject_child_close=" + (postChildClose != 0));
        MSG msg5;
        int d6 = 0;
        while (PeekMessageW(out msg5, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg5);
            DispatchMessageW(ref msg5);
            d6++;
            if (d6 >= 200) break;
        }
        Console.WriteLine("child_msg_count=" + ChildMsgCount);
        Console.WriteLine("child_wm_create_count=" + WmChildCreateCount);
        Console.WriteLine("child_close_destroyed=" + ChildCloseDestroyed);

        // ===================== Phase 6: dialogs + modal windows =====================
        // 6a) MessageBoxW (user32): MessageBoxW BLOCKS its caller (a modal box),
        //     so the headless acceptance runs it on a WORKER thread and dismisses
        //     the box programmatically -- the same technique UI-automation
        //     frameworks use. The worker calls MessageBoxW(.., MB_OK) and stores
        //     the return; the main thread finds the box (FindWindowW by the
        //     standard dialog class "#32770" + the caption title) and posts
        //     WM_COMMAND IDOK. MB_OK must return IDOK.
        // 6b) Modal window: EnableWindow disables the owner (the parent Form);
        //     a nested BLOCKING GetMessageW pump (the simplified modal loop, the
        //     same GetMessageW/Translate/Dispatch shape as a real ShowDialog)
        //     dispatches until the modal window destroys itself on WM_CLOSE; the
        //     owner is re-enabled after the loop.
        //
        // The MessageBoxW worker thread does NO managed allocation (MessageBoxW
        // is a scalar P/Invoke; the caption/text are the only string args and
        // they come from the MAIN thread's FindWindowW probe, not the worker) --
        // the single-threaded M-stage allocator stays safe.
        MsgBoxResult = -1;
        Thread msgThread = new Thread(MessageBoxWorker);
        // Background: if the box were ever not found/dismissed, the blocked
        // worker must NOT prevent the process from exiting (a foreground
        // thread would hang teardown). Background keeps the test fail-safe.
        msgThread.IsBackground = true;
        msgThread.Start();
        Console.WriteLine("msgbox_thread_started=" + msgThread.IsAlive);

        // Find the box: dialog class "#32770" + the caption we passed (the box's
        // window title). Poll briefly -- the dialog is created synchronously
        // inside MessageBoxW, but the poll keeps the acceptance robust across
        // scheduling (bounded: 200 x 2 ms max).
        IntPtr msgBox = IntPtr.Zero;
        int msgPolls = 0;
        while (msgBox == IntPtr.Zero && msgPolls < 200)
        {
            msgBox = FindWindowW(DLG_CLASS, "Wf6Caption");
            if (msgBox == IntPtr.Zero)
            {
                Thread.Sleep(2);
                msgPolls++;
            }
        }
        Console.WriteLine("msgbox_found=" + (msgBox != IntPtr.Zero));

        // Dismiss: find the OK button (control id IDOK) inside the box and
        // click it via a posted BM_CLICK -- the canonical UI-automation
        // dismissal. The box closes and MessageBoxW returns IDOK. (A bare
        // posted WM_COMMAND IDOK does NOT dismiss a message box -- the dialog
        // proc only ends it on a real button click / WM_COMMAND from the
        // button itself, so the click must go to the BUTTON hwnd.) The OK
        // button is created a moment AFTER the dialog window appears, so both
        // lookups are polled (bounded) -- deterministic across scheduling.
        int msgDismiss = 0;
        if (msgBox != IntPtr.Zero)
        {
            // The OK button is the "Button"-class child of the dialog. Walk the
            // child window list directly (FindWindowExW -- a pure handle walk,
            // reliable cross-thread). The button is created a moment after the
            // dialog appears, so poll briefly (bounded) -- deterministic.
            IntPtr okBtn = IntPtr.Zero;
            int btnPolls = 0;
            while (okBtn == IntPtr.Zero && btnPolls < 500)
            {
                okBtn = FindWindowExW(msgBox, IntPtr.Zero, "Button", null);
                if (okBtn == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    btnPolls++;
                }
            }
            if (okBtn == IntPtr.Zero && btnPolls >= 500)
            {
                // Fallback: GetDlgItem by control id (IDOK).
                okBtn = GetDlgItem(msgBox, IDOK);
            }
            msgDismiss = okBtn != IntPtr.Zero
                ? PostMessageW(okBtn, BM_CLICK, IntPtr.Zero, IntPtr.Zero)
                : 0;
        }
        Console.WriteLine("msgbox_dismiss_posted=" + (msgDismiss != 0));

        // Bounded wait for the worker (instead of an unbounded Join -- if the
        // box was somehow never dismissed, MsgBoxResult stays -1 and the test
        // FAILs cleanly instead of hanging). The worker returns as soon as the
        // box closes, so ~400 ms is ample.
        int msgWaits = 0;
        while (msgThread.IsAlive && msgWaits < 200)
        {
            Thread.Sleep(2);
            msgWaits++;
        }
        Console.WriteLine("msgbox_result_idok=" + (MsgBoxResult == IDOK));

        // --- 6b: modal window (owner disabled + nested modal loop) ---
        // The modal Form is its OWN registered class with its OWN WndProc
        // (ModalWndProcImpl) -- like the phase-5 child -- but it is shown as a
        // MODAL: its owner (hForm) is disabled via EnableWindow and a nested
        // blocking message loop pumps until the modal destroys itself.
        WNDCLASSEX wc6 = new WNDCLASSEX();
        wc6.cbSize = 80;
        wc6.style = CS_HREDRAW | CS_VREDRAW;
        RootModalWndProc = ModalWndProcImpl;
        wc6.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootModalWndProc);
        wc6.hInstance = hModule;
        IntPtr cls6 = Marshal.StringToHGlobalUni("Wf1ModalClass");
        wc6.lpszClassName = cls6;
        ushort atom6 = RegisterClassExW(ref wc6);
        Console.WriteLine("modal_class_registered=" + (atom6 != 0));
        Marshal.FreeHGlobal(cls6);

        IntPtr hModal = CreateWindowExW(0, "Wf1ModalClass", "Wf1Modal",
            WS_OVERLAPPED, 60, 80, 240, 140,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        ModalHwnd = hModal;
        Console.WriteLine("modal_created=" + (hModal != IntPtr.Zero));
        Console.WriteLine("modal_wm_create_count=" + ModalCreateCount);

        // Disable the owner. Probe the enabled state before AND after -- the
        // state transitions are the observable acceptance (the EnableWindow
        // return value itself is the PREVIOUS state: nonzero iff previously
        // disabled, per MSDN).
        bool modalOwnerEnabledBefore = IsWindowEnabled(hForm) != 0;
        EnableWindow(hForm, 0);
        Console.WriteLine("modal_owner_was_enabled=" + modalOwnerEnabledBefore);
        Console.WriteLine("modal_owner_disabled=" + (IsWindowEnabled(hForm) == 0));
        Console.WriteLine("modal_itself_enabled=" + (IsWindowEnabled(hModal) != 0));

        // Post WM_CLOSE to the modal -- its WndProc destroys itself and the
        // nested modal loop below exits once the window is gone.
        int modalClosePost = PostMessageW(hModal, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("modal_close_posted=" + (modalClosePost != 0));

        // Simplified MODAL LOOP: a BLOCKING GetMessageW pump (a real ShowDialog
        // loop is the same nested GetMessageW/Translate/Dispatch). Dispatches
        // until the modal window is destroyed (WM_CLOSE handled) or the bounded
        // 200-dispatch cap (harness watchdog). NOTE: the dispatch count is NOT
        // printed -- the OS may inject its own activation messages into the
        // queue (the modal is top-level), so the observable acceptance is the
        // WM_CLOSE handling + destroy + owner re-enable, not the message count.
        MSG msg6;
        int modalDisp = 0;
        while (GetMessageW(out msg6, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg6);
            DispatchMessageW(ref msg6);
            modalDisp++;
            if (ModalDestroyed) break;
            if (modalDisp >= 200) break;
        }
        Console.WriteLine("modal_wm_close_count=" + ModalCloseCount);
        Console.WriteLine("modal_destroyed=" + ModalDestroyed);

        // Re-enable the owner after the modal loop exits.
        EnableWindow(hForm, 1);
        Console.WriteLine("modal_owner_reenabled=" + (IsWindowEnabled(hForm) != 0));

        // ===================== Phase 7: property-based control access =====================
        // The managed control-property surface (plan §1.5 / §4.3): Text /
        // Visible / Enabled as C# PROPERTIES on a WfPropControl wrapper that
        // translate to the native control channel:
        //   Text    get -> GetWindowTextW;        set -> WM_SETTEXT (SendMessageW)
        //   Visible get -> IsWindowVisible;       set -> ShowWindow (SW_SHOW/SW_HIDE)
        //   Enabled get -> IsWindowEnabled;       set -> EnableWindow
        // plus the Click event + Enabled TOGGLE combination: a Click handler
        // flips Enabled, and the dispatch gates Click on Enabled -- a disabled
        // control receives no click (the WinForms rule). Headless acceptance:
        // PostMessage WM_COMMAND by control id + PeekMessageW drain (phase-4
        // pattern), property sequences byte-exact across net48 + both compilers.
        WfPropControl p7Form = new WfPropControl(); p7Form.Handle = hForm;
        WfPropControl p7Btn = new WfPropControl();  p7Btn.Handle = hButton;  p7Btn.Id = 0x107;
        WfPropControl p7Edit = new WfPropControl(); p7Edit.Handle = hEdit;

        // Text property: get (button caption from CreateWindowExW), set via
        // WM_SETTEXT, get back.
        Console.WriteLine("p7_btn_text0=" + p7Btn.Text);        // Click me
        p7Btn.Text = "Wf7Btn";
        Console.WriteLine("p7_btn_text1=" + p7Btn.Text);        // Wf7Btn

        // Text property on the EDIT control: the phase-3 posted text is the
        // current value; the setter replaces it, the getter reads it back.
        Console.WriteLine("p7_edit_text0=" + p7Edit.Text);      // Wf3Posted
        p7Edit.Text = "Wf7Edit";
        Console.WriteLine("p7_edit_text1=" + p7Edit.Text);      // Wf7Edit

        // Visible property: the form is HIDDEN (stage-2 SW_HIDE, phase-6 modal
        // never shows it). ShowWindow transitions + IsWindowVisible readback.
        Console.WriteLine("p7_form_visible0=" + p7Form.Visible);// False
        p7Form.Visible = true;
        Console.WriteLine("p7_form_visible1=" + p7Form.Visible);// True
        p7Form.Visible = false;
        Console.WriteLine("p7_form_visible2=" + p7Form.Visible);// False

        // Enabled property: the form was DISABLED + re-ENABLED by the phase-6
        // modal loop (ends enabled). EnableWindow transitions + readback.
        Console.WriteLine("p7_form_enabled0=" + p7Form.Enabled);// True
        p7Form.Enabled = false;
        Console.WriteLine("p7_form_enabled1=" + p7Form.Enabled);// False
        p7Form.Enabled = true;
        Console.WriteLine("p7_form_enabled2=" + p7Form.Enabled);// True

        // Click event + Enabled toggle: subscribe a toggle handler, post
        // WM_COMMAND by control id. The first click (enabled) fires AND
        // disables the control; a second posted click is GATED (disabled
        // controls receive no click); re-enable, the third fires again.
        P7Btn = p7Btn;
        P7Seq = "";
        P7Btn.Click += OnP7Toggle;
        Console.WriteLine("p7_btn_enabled0=" + P7Btn.Enabled);  // True

        int p7ok1 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P7Btn.Id), IntPtr.Zero);
        Console.WriteLine("p7_inject1=" + (p7ok1 != 0));
        MSG msg7;
        int d7 = 0;
        while (PeekMessageW(out msg7, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg7);
            DispatchMessageW(ref msg7);
            d7++;
            if (d7 >= 200) break;
        }
        Console.WriteLine("p7_seq1=" + P7Seq);                  // C;
        Console.WriteLine("p7_btn_enabled1=" + P7Btn.Enabled);  // False (toggled by handler)

        int p7ok2 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P7Btn.Id), IntPtr.Zero);
        Console.WriteLine("p7_inject2=" + (p7ok2 != 0));
        int d8 = 0;
        while (PeekMessageW(out msg7, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg7);
            DispatchMessageW(ref msg7);
            d8++;
            if (d8 >= 200) break;
        }
        Console.WriteLine("p7_seq2=" + P7Seq);                  // C; (disabled -> gated)
        Console.WriteLine("p7_btn_enabled2=" + P7Btn.Enabled);  // False

        P7Btn.Enabled = true;                                    // property setter re-enables
        Console.WriteLine("p7_btn_enabled3=" + P7Btn.Enabled);  // True
        int p7ok3 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P7Btn.Id), IntPtr.Zero);
        Console.WriteLine("p7_inject3=" + (p7ok3 != 0));
        int d9 = 0;
        while (PeekMessageW(out msg7, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg7);
            DispatchMessageW(ref msg7);
            d9++;
            if (d9 >= 200) break;
        }
        Console.WriteLine("p7_seq3=" + P7Seq);                  // C;C;
        Console.WriteLine("p7_btn_enabled4=" + P7Btn.Enabled);  // False

        // ===================== Phase 8: dialog controls (ComboBox / ListBox) =====================
        // The ListControl surface (plan §1.5: ListControl -> ListBox/ComboBox)
        // driven through the raw user32 COMBOBOX/LISTBOX classes:
        //   * Items:       CB_ADDSTRING / LB_ADDSTRING (returns the item index)
        //   * Selection:   CB_GETCURSEL/CB_SETCURSEL / LB_GETCURSEL/LB_SETCURSEL
        //                  (CB_ERR / LB_ERR = -1 when nothing is selected)
        //   * Item text:   CB_GETLBTEXT / LB_GETTEXT (IntPtr buffer +
        //                  PtrToStringUni -- the stage-2 marshal path)
        //   * Interaction: when the USER changes the selection, the native
        //                  control sends WM_COMMAND to its parent with
        //                  HIWORD(wParam) = CBN_SELCHANGE / LBN_SELCHANGE and
        //                  LOWORD = the control id. The form's WndProc decodes
        //                  the notification code and dispatches it into the
        //                  managed SelectionChanged event.
        // Headless acceptance: PostMessage WM_COMMAND (id | (notif << 16)) +
        // PeekMessageW drain (phase-4 pattern); the handlers RE-READ the native
        // selection so the sequence proves the event observes it at fire time.
        IntPtr hCombo = CreateWindowExW(0, "COMBOBOX", "Wf8Combo",
            WS_CHILD | WS_TABSTOP | CBS_DROPDOWNLIST, 20, 210, 160, 120,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("combo_created=" + (hCombo != IntPtr.Zero));
        IntPtr hList = CreateWindowExW(0, "LISTBOX", "Wf8List",
            WS_CHILD | WS_TABSTOP | LBS_NOTIFY, 200, 210, 160, 120,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("listbox_created=" + (hList != IntPtr.Zero));
        Console.WriteLine("combo_parent_is_form=" + (GetParent(hCombo) == hForm));
        Console.WriteLine("list_parent_is_form=" + (GetParent(hList) == hForm));

        WfDialogControl p8Combo = new WfDialogControl();
        p8Combo.Handle = hCombo; p8Combo.Id = 0x108; p8Combo.Kind = 1;
        WfDialogControl p8List = new WfDialogControl();
        p8List.Handle = hList; p8List.Id = 0x109; p8List.Kind = 2;
        P8Combo = p8Combo; P8List = p8List;
        P8Seq = "";

        // Items.Add: CB_ADDSTRING / LB_ADDSTRING return the item's zero-based
        // index in the native list (deterministic add order).
        Console.WriteLine("p8_combo_add0=" + p8Combo.Add("Alpha"));
        Console.WriteLine("p8_combo_add1=" + p8Combo.Add("Beta"));
        Console.WriteLine("p8_combo_add2=" + p8Combo.Add("Gamma"));
        Console.WriteLine("p8_combo_count=" + p8Combo.Count);       // 3
        Console.WriteLine("p8_list_add0=" + p8List.Add("One"));
        Console.WriteLine("p8_list_add1=" + p8List.Add("Two"));
        Console.WriteLine("p8_list_add2=" + p8List.Add("Three"));
        Console.WriteLine("p8_list_count=" + p8List.Count);         // 3

        // Initial selection: nothing selected -> CB_GETCURSEL / LB_GETCURSEL
        // return CB_ERR / LB_ERR (-1).
        Console.WriteLine("p8_combo_sel0=" + p8Combo.SelectedIndex); // -1
        Console.WriteLine("p8_list_sel0=" + p8List.SelectedIndex);   // -1

        // Item text readback: CB_GETLBTEXT / LB_GETTEXT into an IntPtr buffer.
        Console.WriteLine("p8_combo_item1=" + p8Combo.GetItemText(1)); // Beta
        Console.WriteLine("p8_list_item2=" + p8List.GetItemText(2));   // Three

        // Selection via CB_SETCURSEL / LB_SETCURSEL -- the native selection
        // changes but NO CBN_SELCHANGE/LBN_SELCHANGE is generated (those fire
        // only when the USER changes the selection, per MSDN).
        p8Combo.SelectedIndex = 2;
        Console.WriteLine("p8_combo_sel1=" + p8Combo.SelectedIndex);  // 2
        p8List.SelectedIndex = 1;
        Console.WriteLine("p8_list_sel1=" + p8List.SelectedIndex);    // 1

        // Interaction -> event: subscribe SelectionChanged handlers that record
        // the CURRENT selection (re-read at fire time), then SIMULATE the user's
        // selection change by posting WM_COMMAND with the notification code in
        // the HIWORD (id | (CBN_SELCHANGE << 16)) -- exactly the message the
        // native control sends its parent on a real selection change.
        p8Combo.SelectionChanged += OnP8ComboChange;
        p8List.SelectionChanged += OnP8ListChange;
        Console.WriteLine("p8_selchange_subs=" + p8Combo.SelCount + "," + p8List.SelCount); // 1,1

        int postSelC = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
        int postSelL = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8List.Id | (LBN_SELCHANGE << 16)), IntPtr.Zero);
        Console.WriteLine("p8_inject=" + (postSelC != 0 && postSelL != 0));
        MSG msg8;
        int d10 = 0;
        while (PeekMessageW(out msg8, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg8);
            DispatchMessageW(ref msg8);
            d10++;
            if (d10 >= 200) break;
        }
        Console.WriteLine("p8_seq=" + P8Seq);   // combo2;list1;

        // ===================== Phase 9: status bar + progress bar =====================
        // The status-strip surface (plan §1.6): a StatusBar (comctl32
        // "msctls_statusbar32" / STATUSCLASSNAME) and a ProgressBar (comctl32
        // "msctls_progress32" / PROGRESS_CLASS), driven through the raw
        // common-control message channels:
        //   * StatusBar: SB_SETTEXT (wParam = part index | flags, lParam =
        //     string pointer) writes a part's text; SB_GETTEXT / SB_GETTEXTLENGTH
        //     read it back into a caller buffer.
        //   * ProgressBar: PBM_SETRANGE32 sets an explicit 0..100 range;
        //     PBM_SETPOS / PBM_GETPOS read/write the position (PBM_SETPOS
        //     returns the PREVIOUS position -- a deterministic sequence).
        //   * Interaction: a posted WF_PROGRESS_STEP advances the progress bar
        //     and mirrors the new position into the status text (progress
        //     drives status), recorded in a step/position/status sequence.
        // Headless acceptance: InitCommonControlsEx registers the comctl32
        // classes (no comctl32 shim sources -- the INTERFACE target forwards
        // to the system library); both controls are WS_CHILD of the HIDDEN
        // form; the interaction is PostMessage + PeekMessageW drain.
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("comctl_init=" + (iccRet != 0));

        IntPtr hStatus = CreateWindowExW(0, "msctls_statusbar32", "",
            WS_CHILD, 0, 0, 320, 20,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        P9StatusHwnd = hStatus;
        Console.WriteLine("statusbar_created=" + (hStatus != IntPtr.Zero));
        IntPtr hProgress = CreateWindowExW(0, "msctls_progress32", "",
            WS_CHILD, 20, 250, 160, 20,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        P9ProgressHwnd = hProgress;
        Console.WriteLine("progressbar_created=" + (hProgress != IntPtr.Zero));

        // Direct channels: explicit range, position read/write round-trip,
        // status text write/read-back round-trip.
        IntPtr rangeRet = SendMessageW(hProgress, PBM_SETRANGE32, new IntPtr(0), new IntPtr(100));
        Console.WriteLine("p9_range_set=" + (rangeRet != IntPtr.Zero));
        IntPtr pos0 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p9_progress0=" + pos0.ToInt64());
        IntPtr len0 = SendMessageW(hStatus, SB_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p9_status0_len=" + (len0.ToInt64() & 0xFFFFL));

        IntPtr prev1 = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(25), IntPtr.Zero);
        Console.WriteLine("p9_setpos_prev=" + prev1.ToInt64());
        IntPtr pos1 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p9_progress1=" + pos1.ToInt64());

        IntPtr setTxt = Marshal.StringToHGlobalUni("Ready");
        IntPtr setRet9 = SendMessageW(hStatus, SB_SETTEXT, IntPtr.Zero, setTxt);
        Marshal.FreeHGlobal(setTxt);
        Console.WriteLine("p9_settext_ok=" + (setRet9 != IntPtr.Zero));
        IntPtr getLen1 = SendMessageW(hStatus, SB_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p9_status1_len=" + (getLen1.ToInt64() & 0xFFFFL));
        IntPtr getBuf = Marshal.AllocHGlobal(256);
        SendMessageW(hStatus, SB_GETTEXT, IntPtr.Zero, getBuf);
        string status1 = Marshal.PtrToStringUni(getBuf);
        Marshal.FreeHGlobal(getBuf);
        Console.WriteLine("p9_status1=" + status1);

        // Interaction: post 4 progress steps; each advances the bar and
        // updates the status text (progress drives status). The drain
        // dispatches them in order; the handler records the sequence.
        P9Seq = "";
        P9StepCount = 0;
        int p9a = PostMessageW(hForm, WF_PROGRESS_STEP, new IntPtr(1), IntPtr.Zero);
        int p9b = PostMessageW(hForm, WF_PROGRESS_STEP, new IntPtr(2), IntPtr.Zero);
        int p9c = PostMessageW(hForm, WF_PROGRESS_STEP, new IntPtr(3), IntPtr.Zero);
        int p9d = PostMessageW(hForm, WF_PROGRESS_STEP, new IntPtr(4), IntPtr.Zero);
        Console.WriteLine("p9_inject=" + (p9a != 0 && p9b != 0 && p9c != 0 && p9d != 0));
        MSG msg9;
        int d11 = 0;
        while (PeekMessageW(out msg9, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg9);
            DispatchMessageW(ref msg9);
            d11++;
            if (d11 >= 200) break;
        }
        Console.WriteLine("p9_seq=" + P9Seq);
        IntPtr posF = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p9_final_progress=" + posF.ToInt64());
        IntPtr fBuf = Marshal.AllocHGlobal(256);
        SendMessageW(hStatus, SB_GETTEXT, IntPtr.Zero, fBuf);
        string statusF = Marshal.PtrToStringUni(fBuf);
        Marshal.FreeHGlobal(fBuf);
        Console.WriteLine("p9_final_status=" + statusF);
        Console.WriteLine("p9_step_count=" + P9StepCount);

        // ===================== Phase 10: main menu (MainMenu / MenuItem) =====================
        // The menu surface: a MainMenu is a CreateMenu() menu BAR attached to
        // the form with SetMenu; each top-level MenuItem is a CreatePopupMenu()
        // DROPDOWN appended with AppendMenuW(MF_POPUP); sub-items are string
        // items with an explicit WM_COMMAND id (AppendMenuW MF_STRING).
        //   * Structure: GetMenu(hForm) reads the attached bar back; item
        //     counts and item-text readback (GetMenuStringW MF_BYPOSITION)
        //     prove the tree shape.
        //   * Menu events: the USER clicks a sub-item -> the OS posts WM_COMMAND
        //     with LOWORD(wParam) = the item id. The form's WndProc decodes the
        //     id and raises the matching MenuItem.Click (the WinForms
        //     MenuItem.Click surface).
        //   * Status linkage: each Click handler mirrors the clicked item's
        //     text into the status bar (SB_SETTEXT + SB_GETTEXT round-trip,
        //     stage-9 channel): menu drives status.
        // Headless acceptance: PostMessage WM_COMMAND by menu item id +
        // PeekMessageW drain (phase-4 pattern); sequences byte-exact across
        // net48 + both compilers.
        // NOTE: the phase-6 message box runs on a WORKER thread whose dialog
        // steals and restores focus; the edit control's focus notifications
        // (EN_KILLFOCUS/EN_SETFOCUS) to the form are posted asynchronously, so
        // the GLOBAL wmCommandCount is timing-dependent (±1). Phase 10 makes
        // its own acceptance deterministic by resetting the counter to zero
        // before the posts -- after the phase-6/7/8/9 drains the queue is
        // empty and phase 10 creates no new windows (no focus changes), so the
        // count at the end is exactly the 3 posted menu commands.
        wmCommandCount = 0;
        IntPtr hMenuBar = CreateMenu();
        Console.WriteLine("p10_menu_created=" + (hMenuBar != IntPtr.Zero));

        IntPtr hFilePopup = CreatePopupMenu();
        int appendFile = AppendMenuW(hMenuBar, MF_POPUP, hFilePopup, "File");
        int appendNew = AppendMenuW(hFilePopup, MF_STRING, new IntPtr(0x110), "New");
        int appendOpen = AppendMenuW(hFilePopup, MF_STRING, new IntPtr(0x111), "Open");
        Console.WriteLine("p10_append_file=" + (appendFile != 0));
        Console.WriteLine("p10_append_new=" + (appendNew != 0));
        Console.WriteLine("p10_append_open=" + (appendOpen != 0));

        IntPtr hHelpPopup = CreatePopupMenu();
        int appendHelp = AppendMenuW(hMenuBar, MF_POPUP, hHelpPopup, "Help");
        int appendAbout = AppendMenuW(hHelpPopup, MF_STRING, new IntPtr(0x120), "About");
        Console.WriteLine("p10_append_help=" + (appendHelp != 0));
        Console.WriteLine("p10_append_about=" + (appendAbout != 0));

        int setMenuOk = SetMenu(hForm, hMenuBar);
        IntPtr gotMenu = GetMenu(hForm);
        Console.WriteLine("p10_setmenu=" + (setMenuOk != 0));
        Console.WriteLine("p10_getmenu_matches=" + (gotMenu == hMenuBar));

        Console.WriteLine("p10_top_count=" + GetMenuItemCount(hMenuBar));
        Console.WriteLine("p10_file_count=" + GetMenuItemCount(hFilePopup));
        Console.WriteLine("p10_help_count=" + GetMenuItemCount(hHelpPopup));

        IntPtr mBuf = Marshal.AllocHGlobal(512);
        GetMenuStringW(hMenuBar, 0, mBuf, 256, MF_BYPOSITION);
        string top0Text = Marshal.PtrToStringUni(mBuf);
        GetMenuStringW(hFilePopup, 0, mBuf, 256, MF_BYPOSITION);
        string file0Text = Marshal.PtrToStringUni(mBuf);
        GetMenuStringW(hHelpPopup, 0, mBuf, 256, MF_BYPOSITION);
        string help0Text = Marshal.PtrToStringUni(mBuf);
        Marshal.FreeHGlobal(mBuf);
        Console.WriteLine("p10_top0_text=" + top0Text);
        Console.WriteLine("p10_file0_text=" + file0Text);
        Console.WriteLine("p10_help0_text=" + help0Text);

        P10New = new WfMenuItem(); P10New.Id = 0x110; P10New.Text = "New";
        P10Open = new WfMenuItem(); P10Open.Id = 0x111; P10Open.Text = "Open";
        P10About = new WfMenuItem(); P10About.Id = 0x120; P10About.Text = "About";
        P10New.Click += OnP10New;
        P10Open.Click += OnP10Open;
        P10About.Click += OnP10About;
        Console.WriteLine("p10_click_subs=" + P10New.ClickCount + "," + P10Open.ClickCount + "," + P10About.ClickCount);

        P10Seq = "";
        P10Status = "";
        int postM1 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P10New.Id), IntPtr.Zero);
        int postM2 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P10Open.Id), IntPtr.Zero);
        int postM3 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P10About.Id), IntPtr.Zero);
        Console.WriteLine("p10_inject=" + (postM1 != 0 && postM2 != 0 && postM3 != 0));
        MSG msg10;
        int d12 = 0;
        while (PeekMessageW(out msg10, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg10);
            DispatchMessageW(ref msg10);
            d12++;
            if (d12 >= 200) break;
        }
        Console.WriteLine("p10_seq=" + P10Seq);
        Console.WriteLine("p10_status=" + P10Status);
        Console.WriteLine("p10_drain_count=" + d12);

        // ===================== Phase 11: MDI (multiple document interface) =====================
        // Simplified MDI container: the MDI FRAME is a top-level window created
        // with WS_CLIPCHILDREN (the frame never paints over its children --
        // style read back via GetWindowLongW GWL_STYLE); its child is the MDI
        // CLIENT -- a single window that is the PARENT of all MDI children
        // ("multiple children, same parent", the simplified MDICLIENT surface).
        //   * Container: GetParent proves the tree child->client->frame.
        //   * Child activation: WM_MDIACTIVATE posted to the CLIENT (wParam =
        //     the child being activated) -- the client records the activation
        //     ORDER (MdiActSeq, the net48 oracle) + SetActiveWindow.
        //   * Multi-child events: each child is a WfMdiChild with its OWN Click
        //     event; a posted WF_MDI_CHILD_MSG to a child fires ONLY that
        //     child's handlers (per-child event isolation).
        // Headless acceptance: PostMessage WM_MDIACTIVATE / WF_MDI_CHILD_MSG +
        // PeekMessageW drain (phase-4 pattern); all windows hidden (children of
        // a hidden frame).
        // MDI frame class: the top-level MDI container. Its WndProc is the
        // frame proc (counts its OWN WM_CREATE) -- distinct from the client.
        WNDCLASSEX wcMdi = new WNDCLASSEX();
        wcMdi.cbSize = 80;
        wcMdi.style = CS_HREDRAW | CS_VREDRAW;
        MdiFrameWndProc = MdiFrameWndProcImpl;
        wcMdi.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MdiFrameWndProc);
        wcMdi.hInstance = hModule;
        IntPtr clsMdi = Marshal.StringToHGlobalUni("Wf1MdiFrameClass");
        wcMdi.lpszClassName = clsMdi;
        ushort atomMdi = RegisterClassExW(ref wcMdi);
        Console.WriteLine("p11_frame_class_registered=" + (atomMdi != 0));
        Marshal.FreeHGlobal(clsMdi);

        // MDI client class: the single parent of all MDI children (the
        // simplified MDICLIENT surface). Its WndProc owns the activation
        // channel (WM_MDIACTIVATE).
        WNDCLASSEX wcMdiClient = new WNDCLASSEX();
        wcMdiClient.cbSize = 80;
        wcMdiClient.style = CS_HREDRAW | CS_VREDRAW;
        MdiClientWndProc = MdiClientWndProcImpl;
        wcMdiClient.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MdiClientWndProc);
        wcMdiClient.hInstance = hModule;
        IntPtr clsMdiClient = Marshal.StringToHGlobalUni("Wf1MdiClientClass");
        wcMdiClient.lpszClassName = clsMdiClient;
        ushort atomMdiClient = RegisterClassExW(ref wcMdiClient);
        Console.WriteLine("p11_client_class_registered=" + (atomMdiClient != 0));
        Marshal.FreeHGlobal(clsMdiClient);

        // MDI child class: shared by all three children (one class, one
        // WndProc -- dispatch by HWND to the per-child managed model).
        WNDCLASSEX wcMdiChild = new WNDCLASSEX();
        wcMdiChild.cbSize = 80;
        wcMdiChild.style = CS_HREDRAW | CS_VREDRAW;
        MdiChildWndProc = MdiChildWndProcImpl;
        wcMdiChild.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MdiChildWndProc);
        wcMdiChild.hInstance = hModule;
        IntPtr clsMdiChild = Marshal.StringToHGlobalUni("Wf1MdiChildClass");
        wcMdiChild.lpszClassName = clsMdiChild;
        ushort atomMdiChild = RegisterClassExW(ref wcMdiChild);
        Console.WriteLine("p11_child_class_registered=" + (atomMdiChild != 0));
        Marshal.FreeHGlobal(clsMdiChild);

        // MDI frame: top-level (WS_OVERLAPPED) + WS_CLIPCHILDREN -- the MDI
        // frame style. Hidden (no WS_VISIBLE); read the style back to prove it.
        IntPtr hMdiFrame = CreateWindowExW(0, "Wf1MdiFrameClass", "Wf1MdiFrame",
            WS_OVERLAPPED | WS_CLIPCHILDREN, 0, 0, 360, 260,
            IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
        MdiFrameHwnd = hMdiFrame;
        Console.WriteLine("p11_frame_created=" + (hMdiFrame != IntPtr.Zero));
        IntPtr frameStyle = GetWindowLongW(hMdiFrame, GWL_STYLE);
        Console.WriteLine("p11_frame_clipchildren=" + ((frameStyle.ToInt64() & WS_CLIPCHILDREN) != 0));
        Console.WriteLine("p11_frame_create_count=" + MdiFrameCreateCount);

        // MDI client: a WS_CHILD of the frame (the single parent of all
        // children -- the simplified MDICLIENT). GetParent proves the tree.
        IntPtr hMdiClient = CreateWindowExW(0, "Wf1MdiClientClass", "",
            WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 360, 260,
            hMdiFrame, IntPtr.Zero, hModule, IntPtr.Zero);
        MdiClientHwnd = hMdiClient;
        Console.WriteLine("p11_client_created=" + (hMdiClient != IntPtr.Zero));
        Console.WriteLine("p11_client_parent_is_frame=" + (GetParent(hMdiClient) == hMdiFrame));

        // Three MDI children -- each its OWN window, ALL parented to the client
        // ("multiple children, same parent"). Titles distinguish the windows.
        IntPtr hMdiA = CreateWindowExW(0, "Wf1MdiChildClass", "Wf11DocA",
            WS_CHILD | WS_CLIPSIBLINGS, 10, 10, 160, 100,
            hMdiClient, IntPtr.Zero, hModule, IntPtr.Zero);
        IntPtr hMdiB = CreateWindowExW(0, "Wf1MdiChildClass", "Wf11DocB",
            WS_CHILD | WS_CLIPSIBLINGS, 30, 30, 160, 100,
            hMdiClient, IntPtr.Zero, hModule, IntPtr.Zero);
        IntPtr hMdiC = CreateWindowExW(0, "Wf1MdiChildClass", "Wf11DocC",
            WS_CHILD | WS_CLIPSIBLINGS, 50, 50, 160, 100,
            hMdiClient, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("p11_childA_created=" + (hMdiA != IntPtr.Zero));
        Console.WriteLine("p11_childB_created=" + (hMdiB != IntPtr.Zero));
        Console.WriteLine("p11_childC_created=" + (hMdiC != IntPtr.Zero));
        Console.WriteLine("p11_childA_parent_is_client=" + (GetParent(hMdiA) == hMdiClient));
        Console.WriteLine("p11_childB_parent_is_client=" + (GetParent(hMdiB) == hMdiClient));
        Console.WriteLine("p11_childC_parent_is_client=" + (GetParent(hMdiC) == hMdiClient));

        // Per-child managed models: each child has its OWN WfMdiChild with its
        // OWN Click event + own handler (the multi-child event delegates).
        MdiChildA = new WfMdiChild(); MdiChildA.Handle = hMdiA;
        MdiChildB = new WfMdiChild(); MdiChildB.Handle = hMdiB;
        MdiChildC = new WfMdiChild(); MdiChildC.Handle = hMdiC;
        MdiChildSeq = "";
        MdiChildA.Click += OnMdiA;
        MdiChildB.Click += OnMdiB;
        MdiChildC.Click += OnMdiC;

        // Child activation: post WM_MDIACTIVATE to the CLIENT, wParam = the
        // child being activated (A, then B, then C). The client records the
        // activation ORDER and activates each child (SetActiveWindow).
        MdiActSeq = "";
        int postActA = PostMessageW(hMdiClient, WM_MDIACTIVATE, hMdiA, IntPtr.Zero);
        int postActB = PostMessageW(hMdiClient, WM_MDIACTIVATE, hMdiB, IntPtr.Zero);
        int postActC = PostMessageW(hMdiClient, WM_MDIACTIVATE, hMdiC, IntPtr.Zero);
        Console.WriteLine("p11_act_inject=" + (postActA != 0 && postActB != 0 && postActC != 0));

        // Multi-child events: post WF_MDI_CHILD_MSG to EACH child -- the shared
        // child WndProc raises only the matching child's OWN Click.
        int postEvA = PostMessageW(hMdiA, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
        int postEvB = PostMessageW(hMdiB, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
        int postEvC = PostMessageW(hMdiC, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p11_child_inject=" + (postEvA != 0 && postEvB != 0 && postEvC != 0));

        MSG msg11;
        int d13 = 0;
        while (PeekMessageW(out msg11, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg11);
            DispatchMessageW(ref msg11);
            d13++;
            if (d13 >= 200) break;
        }
        Console.WriteLine("p11_act_seq=" + MdiActSeq);
        Console.WriteLine("p11_active_is_C=" + (MdiActiveChild == hMdiC));
        Console.WriteLine("p11_child_seq=" + MdiChildSeq);
        Console.WriteLine("p11_child_msg_count=" + MdiChildMsgCount);
        Console.WriteLine("p11_child_create_count=" + MdiChildCreateCount);

        // ===================== Phase 12: clipboard (copy / paste) =====================
        // The Clipboard surface (winforms-plan §2 stage-2 list: OpenClipboard /
        // GetClipboardData / SetClipboardData -- user32, no COM). The WinForms
        // Clipboard.SetText + GetText round-trip is exercised headless:
        //   * Copy:  OpenClipboard -> EmptyClipboard -> SetClipboardData
        //            (CF_UNICODETEXT, a StringToHGlobalUni handle -- the SYSTEM
        //            owns the memory on success) -> CloseClipboard.
        //   * Paste: OpenClipboard -> GetClipboardData(CF_UNICODETEXT) ->
        //            GlobalLock -> PtrToStringUni -> GlobalUnlock -> CloseClipboard.
        // Headless acceptance: OpenClipboard associates the clipboard with the
        // calling task (its message queue); the SET/GET run inside the WndProc
        // handler for POSTED custom messages -- the phase-4 PostMessage +
        // PeekMessageW drain pattern -- so the sequence is deterministic across
        // net48 + both compilers (the clipboard lives in the session, so a
        // non-interactive window station can copy/paste). The only real-world
        // hazard, another process holding the clipboard open, is handled by a
        // bounded OpenClipboard retry INSIDE the handler (identical on both
        // sides).
        int clipPostSet = PostMessageW(hForm, WF_CLIP_SET, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p12_clip_set_post=" + (clipPostSet != 0));
        MSG msg12s;
        int d14 = 0;
        while (PeekMessageW(out msg12s, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg12s);
            DispatchMessageW(ref msg12s);
            d14++;
            if (d14 >= 200) break;
        }
        Console.WriteLine("p12_clip_set_open=" + P12OpenOk);
        Console.WriteLine("p12_clip_set_empty=" + P12EmptyOk);
        Console.WriteLine("p12_clip_set_ok=" + P12SetOk);
        Console.WriteLine("p12_clip_set_close=" + P12CloseOk);

        int clipPostGet = PostMessageW(hForm, WF_CLIP_GET, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p12_clip_get_post=" + (clipPostGet != 0));
        MSG msg12g;
        int d15 = 0;
        while (PeekMessageW(out msg12g, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg12g);
            DispatchMessageW(ref msg12g);
            d15++;
            if (d15 >= 200) break;
        }
        Console.WriteLine("p12_clip_get_open=" + P12Open2Ok);
        Console.WriteLine("p12_clip_get_ok=" + P12GetOk);
        Console.WriteLine("p12_clip_get_len=" + P12ClipLen);
        Console.WriteLine("p12_clip_get_text=" + P12ClipText);
        Console.WriteLine("p12_clip_get_close=" + P12Close2Ok);

        // ===================== Phase 13: background thread -> UI update =====================
        // The WinForms rule "background work must be marshaled to the UI
        // thread", exercised with the primitives this file already wires: a
        // WORKER thread does the work (posting progress messages) and the
        // MAIN thread's message queue + WndProc receive them (the
        // Control.BeginInvoke/PostMessage handoff, simplified). The worker
        // posts WF_WORKER_MSG four times (wParam = the step index); each
        // message is dispatched by the main pump into the form's WndProc --
        // which runs on the MAIN thread and updates UI state (Interlocked
        // counter + TextBox text). The worker Interlocked-counts its own
        // posts; Thread.Join (happens-before) guarantees all four are queued
        // before the drain, so the FIFO dispatch order is deterministic
        // (w0;w1;w2;w3;) and posted count == processed count proves every
        // cross-thread message was handled 1:1.
        P13FormHwnd = hForm;
        P13PostedCount = 0; P13UiCount = 0; P13Seq = ""; P13UiText = ""; P13UiTextLen = 0;
        Thread worker13 = new Thread(Worker13);
        worker13.IsBackground = true;
        worker13.Start();
        worker13.Join();
        Console.WriteLine("p13_worker_joined=" + (worker13.IsAlive == false));
        Console.WriteLine("p13_posted_count=" + P13PostedCount);
        MSG msg13;
        int d16 = 0;
        while (PeekMessageW(out msg13, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg13);
            DispatchMessageW(ref msg13);
            d16++;
            if (d16 >= 200) break;
        }
        Console.WriteLine("p13_ui_count=" + P13UiCount);
        Console.WriteLine("p13_ui_text_len=" + P13UiTextLen);
        Console.WriteLine("p13_ui_text=" + P13UiText);
        Console.WriteLine("p13_seq=" + P13Seq);

        // ===================== Phase 15: Panel container (STATIC + subclass) =====================
        // The WinForms Panel (a child container that owns child controls)
        // modeled on a STATIC common control: CreateWindowExW("STATIC", ...)
        // with WS_CHILD, then SUBCLASSED via SetWindowLongPtrW(GWLP_WNDPROC) so
        // the container gets its OWN managed WndProc (unhandled messages fall
        // to DefWindowProcW, the same fallback every WndProc here uses). A
        // BUTTON is created
        // INSIDE the panel -- its parent is the PANEL, not the form. When the
        // button "clicks" the panel's proc receives the WM_COMMAND (a container
        // receives its children's events), records the nested event, and
        // BUBBLES WF_PANEL_BTN to the form (button -> panel -> form). A posted
        // WF_PANEL_MSG routed to the PANEL's own proc proves the container has
        // an independent event surface. Layout: GetWindowRect/GetClientRect
        // sizes + the GetParent container tree (deltas, byte-exact). All
        // windows stay hidden; messages are posted + drained (phase-4 pattern).
        P15PanelCreated = false; P15BtnCreated = false; P15SubclassOk = false;
        P15ParentIsForm = false; P15BtnParentIsPanel = false;
        P15RectOk = false; P15PanelW = 0; P15PanelH = 0; P15BtnW = 0; P15BtnH = 0;
        P15ClientOk = false; P15ClientW = 0; P15ClientH = 0;
        P15MsgCount = 0; P15ClickCount = 0; P15FormNotify = 0;
        P15Seq = ""; P15Destroyed = false;

        IntPtr hPanel = CreateWindowExW(0, "STATIC", "",
            WS_CHILD, 20, 30, 300, 200, hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        P15PanelHwnd = hPanel;
        P15PanelCreated = hPanel != IntPtr.Zero;
        Console.WriteLine("p15_panel_created=" + P15PanelCreated);

        PanelWndProc = PanelWndProcImpl;
        IntPtr panelFn = Marshal.GetFunctionPointerForDelegate(PanelWndProc);
        IntPtr oldProc = SetWindowLongPtrW(hPanel, GWLP_WNDPROC, panelFn);
        P15SubclassOk = oldProc != IntPtr.Zero;
        Console.WriteLine("p15_subclass_ok=" + P15SubclassOk);

        IntPtr hBtn15 = CreateWindowExW(0, "BUTTON", "P15Btn",
            WS_CHILD | WS_TABSTOP, 10, 10, 100, 30,
            hPanel, new IntPtr(ID_PANEL_BTN), hModule, IntPtr.Zero);
        P15BtnHwnd = hBtn15;
        P15BtnCreated = hBtn15 != IntPtr.Zero;
        Console.WriteLine("p15_btn_created=" + P15BtnCreated);

        P15ParentIsForm = GetParent(hPanel) == hForm;
        Console.WriteLine("p15_gp_panel_is_form=" + P15ParentIsForm);
        P15BtnParentIsPanel = GetParent(hBtn15) == hPanel;
        Console.WriteLine("p15_gp_btn_is_panel=" + P15BtnParentIsPanel);

        RECT rP15 = new RECT();
        int prOk = GetWindowRect(hPanel, out rP15);
        P15RectOk = prOk != 0;
        P15PanelW = rP15.right - rP15.left;
        P15PanelH = rP15.bottom - rP15.top;
        RECT rB15 = new RECT();
        GetWindowRect(hBtn15, out rB15);
        P15BtnW = rB15.right - rB15.left;
        P15BtnH = rB15.bottom - rB15.top;
        Console.WriteLine("p15_rect_ok=" + P15RectOk);
        Console.WriteLine("p15_panel_w=" + P15PanelW);
        Console.WriteLine("p15_panel_h=" + P15PanelH);
        Console.WriteLine("p15_btn_w=" + P15BtnW);
        Console.WriteLine("p15_btn_h=" + P15BtnH);

        RECT rC15 = new RECT();
        int crOk = GetClientRect(hPanel, out rC15);
        P15ClientOk = crOk != 0;
        P15ClientW = rC15.right - rC15.left;
        P15ClientH = rC15.bottom - rC15.top;
        Console.WriteLine("p15_client_ok=" + P15ClientOk);
        Console.WriteLine("p15_client_w=" + P15ClientW);
        Console.WriteLine("p15_client_h=" + P15ClientH);

        // Nested events: a posted WF_PANEL_MSG to the panel's OWN proc + a
        // button WM_COMMAND (the container receives its child's click and
        // bubbles the notification to the form).
        PostMessageW(hPanel, WF_PANEL_MSG, IntPtr.Zero, IntPtr.Zero);
        PostMessageW(hPanel, WM_COMMAND, new IntPtr(ID_PANEL_BTN), hBtn15);

        MSG msg15;
        int d15p = 0;
        while (PeekMessageW(out msg15, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg15);
            DispatchMessageW(ref msg15);
            d15p++;
            if (d15p >= 200) break;
        }
        Console.WriteLine("p15_msg_count=" + P15MsgCount);
        Console.WriteLine("p15_click_count=" + P15ClickCount);
        Console.WriteLine("p15_form_notify=" + P15FormNotify);
        Console.WriteLine("p15_seq=" + P15Seq);

        P15Destroyed = DestroyWindow(hPanel) != 0;
        Console.WriteLine("p15_destroyed=" + P15Destroyed);

        // ===================== Phase 16: TabControl (WC_TABCONTROL) =====================
        // The TabControl (winforms-plan stage-16 list) modeled on the comctl32
        // "SysTabControl32" class (WC_TABCONTROL):
        //   * Tabs: TCM_INSERTITEMW inserts a page (lParam = a TCITEM*). The
        //     TCITEM is built in a GCHandle-PINNED int[] with the native TCITEMW
        //     layout (mask @0, pszText @16, lParam @32 -- 12 int32s; the array
        //     data is 8-aligned exactly like the struct): the wired Marshal
        //     surface has no StructureToPtr, so the struct is written
        //     field-by-field into the pinned array's data bytes. Native quirk
        //     (deterministic on both sides): the control inserts each new tab at
        //     the FRONT (return 0) and auto-selects, so the SELECTION is always
        //     driven EXPLICITLY with TCM_SETCURSEL (the reliable channel).
        //   * Page list + selection: TCM_GETITEMCOUNT counts the pages;
        //     TCM_GETCURSEL / TCM_SETCURSEL read/change the selected tab
        //     (TCM_SETCURSEL is SILENT -- no parent notification).
        //   * Page-switch event: a real user click sends the parent WM_NOTIFY
        //     (wParam = id, lParam = NMHDR* code TCN_SELCHANGE); headless we POST
        //     WM_NOTIFY to the form + PeekMessageW drain. The form's WM_NOTIFY
        //     dispatch raises SelectedIndexChanged -> re-reads TCM_GETCURSEL and
        //     switches the visible page.
        //   * Per-page content: tab index k owns its OWN content STATIC child of
        //     the form (Page0Content / Page1Content / Page2Content); P16ShowPage
        //     shows exactly ONE page per tab and hides the other two (style-bit
        //     visibility readback -- IsWindowVisible walks the hidden form's
        //     ancestor chain, so the probe reads the window's OWN WS_VISIBLE bit).
        // net48 oracle: page count / selection round-trips / page-switch sequence
        // (p16_seq) / per-page visibility byte-exact. All windows hidden.
        INITCOMMONCONTROLSEX icc16 = new INITCOMMONCONTROLSEX();
        icc16.dwSize = 8;
        icc16.dwICC = ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        int icc16Ret = InitCommonControlsEx(ref icc16);
        Console.WriteLine("p16_comctl_init=" + (icc16Ret != 0));

        IntPtr hTab16 = CreateWindowExW(0, WC_TABCONTROL, "",
            WS_CHILD, 10, 10, 280, 120, hForm, new IntPtr(ID_TAB16), hModule, IntPtr.Zero);
        TabHwnd = hTab16;
        P16TabCreated = hTab16 != IntPtr.Zero;
        Console.WriteLine("p16_tab_created=" + P16TabCreated);

        // Per-page content controls: tab index k owns P16Page k (all hidden
        // until the first page switch).
        IntPtr hPg0 = CreateWindowExW(0, "STATIC", "Page0Content", WS_CHILD, 30, 30, 120, 24,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        IntPtr hPg1 = CreateWindowExW(0, "STATIC", "Page1Content", WS_CHILD, 30, 30, 120, 24,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        IntPtr hPg2 = CreateWindowExW(0, "STATIC", "Page2Content", WS_CHILD, 30, 30, 120, 24,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        P16Page0 = hPg0; P16Page1 = hPg1; P16Page2 = hPg2;
        P16Page0Created = hPg0 != IntPtr.Zero;
        P16Page1Created = hPg1 != IntPtr.Zero;
        P16Page2Created = hPg2 != IntPtr.Zero;
        Console.WriteLine("p16_page0_created=" + P16Page0Created);
        Console.WriteLine("p16_page1_created=" + P16Page1Created);
        Console.WriteLine("p16_page2_created=" + P16Page2Created);

        P16Tab = new WfTabControl();
        P16Tab.Handle = hTab16;
        P16Tab.Id = ID_TAB16;
        P16NotifyCount = 0;
        P16SelChangeCount = 0;
        P16Seq = "";
        P16Tab.SelectedIndexChanged += OnP16TabChanged;

        // Pages via TCITEM (lParam = the page index each tab owns); the count
        // is read back with TCM_GETITEMCOUNT.
        P16Tab.AddTab(0);
        P16Tab.AddTab(1);
        P16Tab.AddTab(2);
        int p16count = P16Tab.Count;
        Console.WriteLine("p16_count=" + p16count);

        // Selection round-trips: explicit TCM_SETCURSEL + TCM_GETCURSEL readback
        // for every page index (the reliable selection channel -- the native
        // control auto-selects on insert, so the selection is always explicit).
        P16Tab.SelectedIndex = 0;
        int p16sel0 = P16Tab.SelectedIndex;
        Console.WriteLine("p16_sel0=" + p16sel0);
        P16Tab.SelectedIndex = 1;
        int p16sel1 = P16Tab.SelectedIndex;
        Console.WriteLine("p16_sel1=" + p16sel1);
        P16Tab.SelectedIndex = 2;
        int p16sel2 = P16Tab.SelectedIndex;
        Console.WriteLine("p16_sel2=" + p16sel2);

        // Page-switch events: select page 1 + post the WM_NOTIFY TCN_SELCHANGE
        // a real user click sends; drain (the dispatch re-reads sel=1 -> shows
        // page1). Then select page 2 + post again; drain (sel=2 -> page2).
        // Each RaiseSelectionChanged re-reads the CURRENT selection
        // (TCM_GETCURSEL) at fire time -- P16Seq = page1;page2;.
        P16Tab.SelectedIndex = 1;
        int p16n1 = PostMessageW(hForm, WM_NOTIFY, new IntPtr(ID_TAB16), IntPtr.Zero);
        Console.WriteLine("p16_inject1=" + (p16n1 != 0));
        MSG msg16;
        int d16a = 0;
        while (PeekMessageW(out msg16, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg16);
            DispatchMessageW(ref msg16);
            d16a++;
            if (d16a >= 200) break;
        }
        P16Tab.SelectedIndex = 2;
        int p16n2 = PostMessageW(hForm, WM_NOTIFY, new IntPtr(ID_TAB16), IntPtr.Zero);
        Console.WriteLine("p16_inject2=" + (p16n2 != 0));
        int d16b = 0;
        while (PeekMessageW(out msg16, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg16);
            DispatchMessageW(ref msg16);
            d16b++;
            if (d16b >= 200) break;
        }
        Console.WriteLine("p16_notify_count=" + P16NotifyCount);
        Console.WriteLine("p16_selchange_count=" + P16SelChangeCount);
        Console.WriteLine("p16_seq=" + P16Seq);

        // Per-page independence: after the two switches (page1 then page2), only
        // the LAST selected page's content is visible (one page per tab).
        Console.WriteLine("p16_page0_vis=" + P16PageVisible(P16Page0));
        Console.WriteLine("p16_page1_vis=" + P16PageVisible(P16Page1));
        Console.WriteLine("p16_page2_vis=" + P16PageVisible(P16Page2));

        P16Destroyed = DestroyWindow(hTab16) != 0;
        Console.WriteLine("p16_tab_destroyed=" + P16Destroyed);

        // ===================== Phase 17: ScrollBar (ScrollBar class) =====================
        // The ScrollBar modeled on the user32-built-in "ScrollBar" class (no
        // InitCommonControlsEx needed):
        //   * Range: SetScrollRange sets an explicit 0..100 range (the DOCUMENTED
        //     replacement for the legacy SBM_SETRANGE control message, which is
        //     a no-op on this OS -- probed: the range stays 0..0 after sending
        //     it). The range is VERIFIED by a CLAMP readback -- SetScrollPos(120)
        //     clamps to max 100 and SetScrollPos(-5) clamps to min 0 (the docs:
        //     "The position must be within the scrolling range"), each read back
        //     via GetScrollPos (the DOCUMENTED replacement for the no-op
        //     SBM_GETPOS). SetScrollPos RETURNS the previous position -- a
        //     deterministic sequence value (same shape as PBM_SETPOS).
        //   * Position channel: SetScrollPos / GetScrollPos write/read the thumb
        //     (the same native state WinForms' VScrollBar drives -- probed:
        //     GetScrollPos reads 25 after VScrollBar.Value = 25).
        //   * Scroll event (WM_VSCROLL): a real user scroll makes the scrollbar
        //     send WM_VSCROLL to its PARENT (the form) with LOWORD(wParam) =
        //     the scroll code; a thumb code (SB_THUMBTRACK / SB_THUMBPOSITION)
        //     carries the new thumb position in HIWORD(wParam). Headless
        //     acceptance POSTS WM_VSCROLL (the phase-8 posted-notification
        //     pattern); the form's WM_VSCROLL dispatch writes the thumb,
        //     re-reads the real position (GetScrollPos -- the event observes
        //     the thumb at fire time) and mirrors it into the status bar text.
        //   * Status linkage: each scroll (the WF_VSCROLL_STEP channel AND the
        //     WM_VSCROLL parent-notification channel) mirrors "Scroll: <pos>"
        //     into the stage-9 status bar (SB_SETTEXT + SB_GETTEXT round-trip):
        //     scroll drives status.
        // net48 oracle: range-clamp readbacks / position round-trips / the
        // scroll sequence (p17_seq) / status linkage byte-exact. The scrollbar
        // is WS_CHILD of the HIDDEN form; destroyed before the form.
        IntPtr hScroll17 = CreateWindowExW(0, SCROLLBAR_CLASS, "",
            WS_CHILD | SBS_VERT, 10, 10, 20, 120, hForm, new IntPtr(ID_SCROLL17), hModule, IntPtr.Zero);
        P17ScrollHwnd = hScroll17;
        P17ScrollCreated = hScroll17 != IntPtr.Zero;
        Console.WriteLine("p17_scrollbar_created=" + P17ScrollCreated);

        // Explicit 0..100 range (SB_CTL addresses the scroll-bar control); the
        // initial thumb position is 0 (GetScrollPos readback).
        int p17range = SetScrollRange(hScroll17, SB_CTL, 0, 100, false);
        Console.WriteLine("p17_range_set=" + (p17range != 0));
        int p17pos0 = GetScrollPos(hScroll17, SB_CTL);
        Console.WriteLine("p17_pos0=" + p17pos0);

        // Range-clamp verification: positions outside 0..100 round to the
        // nearest valid value -- SetScrollPos(120) clamps to max (100) and
        // SetScrollPos(-5) clamps to min (0). Both read back via GetScrollPos.
        SetScrollPos(hScroll17, SB_CTL, 120, false);
        int p17clampHigh = GetScrollPos(hScroll17, SB_CTL);
        P17ClampHigh = p17clampHigh == 100;
        Console.WriteLine("p17_clamp_high=" + P17ClampHigh);
        SetScrollPos(hScroll17, SB_CTL, -5, false);
        int p17clampLow = GetScrollPos(hScroll17, SB_CTL);
        P17ClampLow = p17clampLow == 0;
        Console.WriteLine("p17_clamp_low=" + P17ClampLow);

        // Scroll sequence: two position-channel steps (posted WF_VSCROLL_STEP
        // drives SetScrollPos + GetScrollPos readback) interleaved with two
        // WM_VSCROLL parent notifications (a real user scroll; the thumb
        // position rides in HIWORD(wParam) of SB_THUMBTRACK / SB_THUMBPOSITION).
        // Each scroll mirrors "Scroll: <pos>" into the status bar text.
        P17StepCount = 0;
        P17VscrollCount = 0;
        P17Seq = "";
        int p17s1 = PostMessageW(hForm, WF_VSCROLL_STEP, new IntPtr(25), IntPtr.Zero);
        Console.WriteLine("p17_inject1=" + (p17s1 != 0));
        MSG msg17;
        int d17a = 0;
        while (PeekMessageW(out msg17, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg17);
            DispatchMessageW(ref msg17);
            d17a++;
            if (d17a >= 200) break;
        }
        int p17v1 = PostMessageW(hForm, WM_VSCROLL, new IntPtr(SB_THUMBTRACK | (40 << 16)), IntPtr.Zero);
        Console.WriteLine("p17_inject2=" + (p17v1 != 0));
        int d17b = 0;
        while (PeekMessageW(out msg17, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg17);
            DispatchMessageW(ref msg17);
            d17b++;
            if (d17b >= 200) break;
        }
        int p17s2 = PostMessageW(hForm, WF_VSCROLL_STEP, new IntPtr(60), IntPtr.Zero);
        Console.WriteLine("p17_inject3=" + (p17s2 != 0));
        int d17c = 0;
        while (PeekMessageW(out msg17, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg17);
            DispatchMessageW(ref msg17);
            d17c++;
            if (d17c >= 200) break;
        }
        int p17v2 = PostMessageW(hForm, WM_VSCROLL, new IntPtr(SB_THUMBPOSITION | (80 << 16)), IntPtr.Zero);
        Console.WriteLine("p17_inject4=" + (p17v2 != 0));
        int d17d = 0;
        while (PeekMessageW(out msg17, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg17);
            DispatchMessageW(ref msg17);
            d17d++;
            if (d17d >= 200) break;
        }
        Console.WriteLine("p17_step_count=" + P17StepCount);
        Console.WriteLine("p17_vscroll_count=" + P17VscrollCount);
        Console.WriteLine("p17_seq=" + P17Seq);
        int p17final = GetScrollPos(hScroll17, SB_CTL);
        Console.WriteLine("p17_final_pos=" + p17final);

        P17Destroyed = DestroyWindow(hScroll17) != 0;
        Console.WriteLine("p17_scrollbar_destroyed=" + P17Destroyed);

        // ===================== Phase 18: radio-button group exclusivity =====================
        // A radio GROUP: the FIRST radio carries WS_GROUP (group start); the
        // following two are plain BS_AUTORADIOBUTTON children -- the native
        // grouping rule makes all three one group (consecutive WS_CHILD
        // buttons without WS_GROUP join the previous group).
        // The exclusivity is applied MANAGED-side (the WinForms model:
        // RadioButton.Checked's setter unchecks the group's other radios in
        // code -- native auto-radio exclusivity only kicks in on REAL user
        // input, which is not headless-testable; BM_CLICK on a hidden-window
        // radio was probed to HANG the net48 oracle too, so the state channel
        // is BM_SETCHECK -- the same message stage-3's checkbox uses).
        // net48 oracle: the group-state sequence after each SetChecked,
        // byte-exact on both sides.
        IntPtr hR1 = CreateWindowExW(0, "BUTTON", "R1",
            WS_CHILD | WS_GROUP | BS_AUTORADIOBUTTON, 20, 20, 100, 20,
            hForm, new IntPtr(ID_RADIO1), hModule, IntPtr.Zero);
        IntPtr hR2 = CreateWindowExW(0, "BUTTON", "R2",
            WS_CHILD | BS_AUTORADIOBUTTON, 20, 44, 100, 20,
            hForm, new IntPtr(ID_RADIO2), hModule, IntPtr.Zero);
        IntPtr hR3 = CreateWindowExW(0, "BUTTON", "R3",
            WS_CHILD | BS_AUTORADIOBUTTON, 20, 68, 100, 20,
            hForm, new IntPtr(ID_RADIO3), hModule, IntPtr.Zero);
        Console.WriteLine("p18_r1_created=" + (hR1 != IntPtr.Zero));
        Console.WriteLine("p18_r2_created=" + (hR2 != IntPtr.Zero));
        Console.WriteLine("p18_r3_created=" + (hR3 != IntPtr.Zero));

        // Initial state: all unchecked (0). Read one (BM_GETCHECK round-trip).
        long r1s0 = SendMessageW(hR1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p18_initial=" + r1s0);

        // SetChecked(R2): R2 checked AND R1/R3 cleared -- the exclusivity rule.
        SetRadioChecked(hR1, hR2, hR3, ID_RADIO2);
        long r1s1 = SendMessageW(hR1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long r2s1 = SendMessageW(hR2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long r3s1 = SendMessageW(hR3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p18_after_r2=" + r1s1 + "," + r2s1 + "," + r3s1);

        // SetChecked(R3) moves the check (R2 cleared).
        SetRadioChecked(hR1, hR2, hR3, ID_RADIO3);
        long r1s2 = SendMessageW(hR1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long r2s2 = SendMessageW(hR2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long r3s2 = SendMessageW(hR3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p18_after_r3=" + r1s2 + "," + r2s2 + "," + r3s2);

        // SetChecked(R1) moves the check back (R3 cleared).
        SetRadioChecked(hR1, hR2, hR3, ID_RADIO1);
        long r1s3 = SendMessageW(hR1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long r2s3 = SendMessageW(hR2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long r3s3 = SendMessageW(hR3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p18_after_r1=" + r1s3 + "," + r2s3 + "," + r3s3);

        // Cleanup: the radios are WS_CHILD -- destroyed with the form.
        Console.WriteLine("p18_radio_group_done=" + (r1s3 + r2s3 + r3s3 == 1));

        // ===================== Phase 19: GroupBox container grouping =====================
        // The GroupBox (the WinForms grouping-container surface, simplified) is a
        // "BUTTON" class window with the BS_GROUPBOX style (0x0007) -- a WS_CHILD
        // STATIC FRAME that draws a titled box and OWNS the controls inside it
        // (same class + CreateWindowExW path as every other child here). Each box
        // carries WS_GROUP -- the native group start. Each GroupBox is SUBCLASSED
        // via SetWindowLongPtrW(GWLP_WNDPROC) so the container has its OWN event
        // surface (WfGroupBoxWndProcImpl: a posted WF_GROUP_MSG handled in the
        // box's own proc + WF_GROUP_NOTIFY BUBBLED to the form -- the phase-15
        // nested chain, groupbox -> form). Each box OWNS TWO radios (their parent
        // is the GroupBox, GetParent-proved; the first radio of each box carries
        // WS_GROUP, the second is plain BS_AUTORADIOBUTTON -- the native
        // in-box group). The group semantics are the Phase-18 rule SCOPED to the
        // container: SetGroupChecked only clears the radios of the group it is
        // given, so checking A2 then B1 then A1 leaves each group with exactly
        // one checked radio (intra-group exclusivity + inter-group independence).
        // The Text property channel is WM_SETTEXT + GetWindowTextW round-trip.
        P19Group1 = new WfGroupBox();
        P19Group2 = new WfGroupBox();

        IntPtr hGroup1 = CreateWindowExW(0, "BUTTON", "Wf19Group1",
            WS_CHILD | WS_GROUP | BS_GROUPBOX, 20, 20, 200, 120,
            hForm, new IntPtr(ID_GROUP1), hModule, IntPtr.Zero);
        IntPtr hGroup2 = CreateWindowExW(0, "BUTTON", "Wf19Group2",
            WS_CHILD | WS_GROUP | BS_GROUPBOX, 240, 20, 200, 120,
            hForm, new IntPtr(ID_GROUP2), hModule, IntPtr.Zero);
        P19Group1.Handle = hGroup1; P19Group1.Id = ID_GROUP1;
        P19Group2.Handle = hGroup2; P19Group2.Id = ID_GROUP2;
        Console.WriteLine("p19_gb1_created=" + (hGroup1 != IntPtr.Zero));
        Console.WriteLine("p19_gb2_created=" + (hGroup2 != IntPtr.Zero));

        // The groupbox creation style read back via GetWindowLongW(GWL_STYLE):
        // the passed WS_CHILD | WS_GROUP | BS_GROUPBOX -- the native group-start
        // frame -- PLUS WS_TABSTOP, which user32's BUTTON control ORs in for its
        // children (net48 oracle: 0x40030007 = 1073872903; the spec's predicted
        // 0x40020007 was the created-as style, net48 is authoritative).
        IntPtr gbStyle = GetWindowLongW(hGroup1, GWL_STYLE);
        Console.WriteLine("p19_gb1_style=" + gbStyle.ToInt64());

        // The managed Text property: WM_SETTEXT writes, GetWindowTextW reads back
        // (the same round-trip WfPropControl.Text uses).
        P19Group1.Text = "Wf19Group1";
        Console.WriteLine("p19_gb1_text=" + P19Group1.Text);

        // Subclass both boxes: the container's OWN event surface.
        GroupBoxWndProc = WfGroupBoxWndProcImpl;
        IntPtr gbFn = Marshal.GetFunctionPointerForDelegate(GroupBoxWndProc);
        IntPtr gbOldProc = SetWindowLongPtrW(hGroup1, GWLP_WNDPROC, gbFn);
        IntPtr gbOldProc2 = SetWindowLongPtrW(hGroup2, GWLP_WNDPROC, gbFn);
        Console.WriteLine("p19_gb_subclass_ok=" + (gbOldProc != IntPtr.Zero && gbOldProc2 != IntPtr.Zero));

        // Two radios per box, parented to the GROUPBOX (the container owns them).
        IntPtr hA1 = CreateWindowExW(0, "BUTTON", "A1",
            WS_CHILD | WS_GROUP | BS_AUTORADIOBUTTON, 20, 30, 100, 20,
            hGroup1, new IntPtr(ID_GRADIO_A1), hModule, IntPtr.Zero);
        IntPtr hA2 = CreateWindowExW(0, "BUTTON", "A2",
            WS_CHILD | BS_AUTORADIOBUTTON, 20, 54, 100, 20,
            hGroup1, new IntPtr(ID_GRADIO_A2), hModule, IntPtr.Zero);
        IntPtr hB1 = CreateWindowExW(0, "BUTTON", "B1",
            WS_CHILD | WS_GROUP | BS_AUTORADIOBUTTON, 20, 30, 100, 20,
            hGroup2, new IntPtr(ID_GRADIO_B1), hModule, IntPtr.Zero);
        IntPtr hB2 = CreateWindowExW(0, "BUTTON", "B2",
            WS_CHILD | BS_AUTORADIOBUTTON, 20, 54, 100, 20,
            hGroup2, new IntPtr(ID_GRADIO_B2), hModule, IntPtr.Zero);

        // The container tree: each GroupBox is the FORM's child; each radio is its
        // OWN GroupBox's child (GetParent assertions).
        Console.WriteLine("p19_gb1_parent_is_form=" + (GetParent(hGroup1) == hForm));
        Console.WriteLine("p19_a1_parent_is_gb1=" + (GetParent(hA1) == hGroup1));
        Console.WriteLine("p19_a2_parent_is_gb1=" + (GetParent(hA2) == hGroup1));
        Console.WriteLine("p19_b1_parent_is_gb2=" + (GetParent(hB1) == hGroup2));
        Console.WriteLine("p19_b2_parent_is_gb2=" + (GetParent(hB2) == hGroup2));

        // Initial: all four radios unchecked (BM_GETCHECK round-trip).
        long a1s0 = SendMessageW(hA1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long a2s0 = SendMessageW(hA2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b1s0 = SendMessageW(hB1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b2s0 = SendMessageW(hB2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p19_init=" + a1s0 + "," + a2s0 + "/" + b1s0 + "," + b2s0);

        // Check A2 in group 1: A1 cleared, group 2 untouched.
        SetGroupChecked(hA1, hA2, ID_GRADIO_A1, ID_GRADIO_A2, ID_GRADIO_A2);
        long a1s1 = SendMessageW(hA1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long a2s1 = SendMessageW(hA2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b1s1 = SendMessageW(hB1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b2s1 = SendMessageW(hB2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p19_after_a2=" + a1s1 + "," + a2s1 + "/" + b1s1 + "," + b2s1);

        // Check B1 in group 2: B2 cleared, group 1 untouched -- A2 STAYS checked
        // (inter-group independence, direction 1).
        SetGroupChecked(hB1, hB2, ID_GRADIO_B1, ID_GRADIO_B2, ID_GRADIO_B1);
        long a1s2 = SendMessageW(hA1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long a2s2 = SendMessageW(hA2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b1s2 = SendMessageW(hB1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b2s2 = SendMessageW(hB2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p19_after_b1=" + a1s2 + "," + a2s2 + "/" + b1s2 + "," + b2s2);

        // Check A1 back in group 1: A2 cleared, group 2 untouched -- B1 STAYS
        // checked (inter-group independence, direction 2).
        SetGroupChecked(hA1, hA2, ID_GRADIO_A1, ID_GRADIO_A2, ID_GRADIO_A1);
        long a1s3 = SendMessageW(hA1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long a2s3 = SendMessageW(hA2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b1s3 = SendMessageW(hB1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        long b2s3 = SendMessageW(hB2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p19_after_a1=" + a1s3 + "," + a2s3 + "/" + b1s3 + "," + b2s3);

        // Independence oracle: each group holds EXACTLY one checked radio at the
        // end -- intra-group exclusivity AND inter-group independence, together.
        Console.WriteLine("p19_groups_independent=" + ((a1s3 + a2s3 == 1) && (b1s3 + b2s3 == 1)));

        // The container's event surface: a posted WF_GROUP_MSG handled in the
        // GROUP BOX's own proc (gb1:msg;) + WF_GROUP_NOTIFY BUBBLED to the form
        // (form:notify;) -- the groupbox -> form nested chain.
        PostMessageW(hGroup1, WF_GROUP_MSG, IntPtr.Zero, IntPtr.Zero);
        MSG msg19;
        int d19 = 0;
        while (PeekMessageW(out msg19, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg19);
            DispatchMessageW(ref msg19);
            d19++;
            if (d19 >= 200) break;
        }
        Console.WriteLine("p19_gb1_msg_seq=" + P19GSeq);

        // ===================== Phase 20: TrackBar (TrackBar class) =====================
        // The TrackBar modeled on the comctl32 "msctls_trackbar32" class (the
        // WinForms TrackBar surface, simplified; registered by the Phase-9
        // InitCommonControlsEx(ICC_BAR_CLASSES) call -- the same registration
        // the status bar uses -- so no new registration is needed):
        //   * Range: TBM_SETRANGE sets an explicit 0..100 range (wParam = the
        //     redraw flag, lParam = MAKELONG(min, max): the LOWORD is the low
        //     end, the HIWORD the high end). The range is VERIFIED by the
        //     TBM_GETRANGEMIN / TBM_GETRANGEMAX readbacks (0 and 100).
        //   * Position channel: TBM_SETPOS / TBM_GETPOS write/read the thumb
        //     (wParam = the redraw flag, lParam = the new position; the return
        //     value is the PREVIOUS position -- the same shape as PBM_SETPOS).
        //     The position CLAMPS to the range: TBM_SETPOS(200) reads back 100
        //     and TBM_SETPOS(-5) reads back 0 -- the deterministic range proof
        //     (the Phase-17 rule).
        //   * Ticks: TBS_AUTOTICKS draws ticks automatically at the
        //     TBM_SETTICFREQ frequency (wParam = n: a tick every n units);
        //     TBM_GETNUMTICS counts them -- with frequency 10 on a 0..100
        //     range: ticks at 0,10,...,100 = 11.
        //   * Page size: TBM_SETPAGESIZE / TBM_GETPAGESIZE write/read the page
        //     size (lParam = the size) -- the PageSize surface, simplified.
        //   * Scroll event (WM_HSCROLL): a real user scroll makes the trackbar
        //     send WM_HSCROLL to its PARENT (the form) with LOWORD(wParam) =
        //     the scroll code; a thumb code (SB_THUMBTRACK / SB_THUMBPOSITION)
        //     carries the new thumb position in HIWORD(wParam). Headless
        //     acceptance POSTS WM_HSCROLL (the phase-8 posted-notification
        //     pattern); the form's WM_HSCROLL dispatch writes the thumb
        //     (TBM_SETPOS), re-reads the real position (TBM_GETPOS -- the event
        //     observes the thumb at fire time) and mirrors it into the status
        //     bar text.
        //   * Status linkage: each scroll (the WM_HSCROLL parent-notification
        //     channel) mirrors "Track: <pos>" into the stage-9 status bar
        //     (SB_SETTEXT + SB_GETTEXT round-trip): track drives status.
        // net48 oracle: range readbacks / position round-trips / clamp
        // readbacks / the tick count / page size / the scroll sequence
        // (p20_hscroll_seq) / status linkage byte-exact. The trackbar is
        // WS_CHILD of the HIDDEN form; destroyed before the form.
        IntPtr hTrack20 = CreateWindowExW(0, WC_TRACKBAR, "",
            WS_CHILD | TBS_AUTOTICKS, 10, 10, 240, 40, hForm, new IntPtr(ID_TRACK20), hModule, IntPtr.Zero);
        P20TrackHwnd = hTrack20;
        P20TrackCreated = hTrack20 != IntPtr.Zero;
        Console.WriteLine("p20_trackbar_created=" + P20TrackCreated);

        // Explicit 0..100 range (TBM_SETRANGE, lParam = MAKELONG(min, max)):
        // the low end 0, the high end 100; read back via TBM_GETRANGEMIN/MAX.
        SendMessageW(hTrack20, TBM_SETRANGE, new IntPtr(1), new IntPtr(100 << 16));
        int p20min = (int)SendMessageW(hTrack20, TBM_GETRANGEMIN, IntPtr.Zero, IntPtr.Zero).ToInt64();
        int p20max = (int)SendMessageW(hTrack20, TBM_GETRANGEMAX, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_range=" + p20min + "," + p20max);

        // Initial thumb position (0) -- TBM_GETPOS readback.
        int p20pos0 = (int)SendMessageW(hTrack20, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_pos0=" + p20pos0);

        // TBM_SETPOS round-trip: write 40 (lParam = the position, wParam = the
        // redraw flag), read back via TBM_GETPOS.
        SendMessageW(hTrack20, TBM_SETPOS, new IntPtr(1), new IntPtr(40));
        int p20setpos40 = (int)SendMessageW(hTrack20, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_setpos40=" + p20setpos40);

        // Range-clamp verification: positions outside 0..100 round to the
        // nearest valid value -- TBM_SETPOS(200) clamps to max (100) and
        // TBM_SETPOS(-5) clamps to min (0). Both read back via TBM_GETPOS.
        SendMessageW(hTrack20, TBM_SETPOS, new IntPtr(1), new IntPtr(200));
        int p20clampHigh = (int)SendMessageW(hTrack20, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_clamp=" + p20clampHigh);
        SendMessageW(hTrack20, TBM_SETPOS, new IntPtr(1), new IntPtr(-5));
        int p20clampLow = (int)SendMessageW(hTrack20, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_clamp_low=" + p20clampLow);

        // Tick frequency: TBM_SETTICFREQ(10) = a tick every 10 units (starting
        // at 0); TBM_GETNUMTICS counts them -- on a 0..100 range: 0,10,...,100
        // = 11 ticks (net48 oracle confirms the exact count).
        SendMessageW(hTrack20, TBM_SETTICFREQ, new IntPtr(10), IntPtr.Zero);
        int p20ticks = (int)SendMessageW(hTrack20, TBM_GETNUMTICS, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_ticks=" + p20ticks);

        // Page size round-trip: TBM_SETPAGESIZE(20) writes the page size
        // (wParam = unused), TBM_GETPAGESIZE reads it back.
        SendMessageW(hTrack20, TBM_SETPAGESIZE, IntPtr.Zero, new IntPtr(20));
        int p20pagesize = (int)SendMessageW(hTrack20, TBM_GETPAGESIZE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p20_pagesize=" + p20pagesize);

        // Scroll sequence: two WM_HSCROLL parent notifications (a real user
        // scroll; the thumb position rides in HIWORD(wParam) of SB_THUMBTRACK /
        // SB_THUMBPOSITION). Each scroll mirrors "Track: <pos>" into the
        // status bar text (SB_SETTEXT + SB_GETTEXT round-trip).
        P20Seq = "";
        P20HscrollCount = 0;
        int p20h1 = PostMessageW(hForm, WM_HSCROLL, new IntPtr(SB_THUMBTRACK | (40 << 16)), IntPtr.Zero);
        Console.WriteLine("p20_inject1=" + (p20h1 != 0));
        MSG msg20;
        int d20a = 0;
        while (PeekMessageW(out msg20, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg20);
            DispatchMessageW(ref msg20);
            d20a++;
            if (d20a >= 200) break;
        }
        int p20h2 = PostMessageW(hForm, WM_HSCROLL, new IntPtr(SB_THUMBPOSITION | (80 << 16)), IntPtr.Zero);
        Console.WriteLine("p20_inject2=" + (p20h2 != 0));
        int d20b = 0;
        while (PeekMessageW(out msg20, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg20);
            DispatchMessageW(ref msg20);
            d20b++;
            if (d20b >= 200) break;
        }
        Console.WriteLine("p20_hscroll_count=" + P20HscrollCount);
        Console.WriteLine("p20_hscroll_seq=" + P20Seq);

        P20Destroyed = DestroyWindow(hTrack20) != 0;
        Console.WriteLine("p20_trackbar_destroyed=" + P20Destroyed);

        // ===================== Phase 21: ListView (ListView class) =====================
        // The ListView modeled on the comctl32 "SysListView32" class (the
        // WinForms ListView surface, simplified):
        //   * Registration: ICC_LISTVIEW_CLASSES registers the SysListView32
        //     class -- the Phase-21 InitCommonControlsEx call ORs it with the
        //     existing tab/bar/progress flags (a registration is idempotent --
        //     re-registering the already-registered classes is a no-op, the
        //     standard comctl32 pattern).
        //   * Items: LVM_INSERTITEMW inserts a row (lParam = a LVITEMW* built in
        //     a GCHandle-PINNED int[22] with the native x64 layout -- mask @0,
        //     iItem @4, pszText @24 (a pointer split across two int32s),
        //     cchTextMax @32, lParam @40 (also a pointer) -- 88 bytes; the
        //     pinned array data is 8-aligned exactly like the struct, the same
        //     field-by-field pattern as the Phase-16 TCITEMW). CRITICAL: the
        //     comctl32 v5.82 LVM_INSERTITEMW REJECTS iItem = -1 ("append" -- the
        //     v6-only semantic; PROBED: iItem=-1 returns -1 and the count does
        //     NOT grow), so Insert uses the CURRENT count as the EXPLICIT iItem
        //     (0, 1, 2, ...) -- the deterministic explicit-index append the v5
        //     control accepts (probed: iItem=0/1/2 return 0/1/2, count correct).
        //   * Count: LVM_GETITEMCOUNT counts rows; SetCount: LVM_SETITEMCOUNT
        //     pre-allocates rows -- but ONLY for a VIRTUAL list view
        //     (LVS_OWNERDATA). On this non-virtual view it is a NO-OP (net48
        //     oracle: after SetCount(6) the count reads 3 -- the documented
        //     virtual-only semantic, same shape as the Phase-17 SBM_* no-op).
        //   * Item text: LVM_GETITEMTEXTW / LVM_SETITEMTEXTW read/write a row's
        //     text (the LVIF_TEXT mask; AllocHGlobal buffer + PtrToStringUni).
        //   * Selection: LVM_SETITEMSTATE / LVM_GETITEMSTATE with LVIS_SELECTED
        //     (the SelectedIndices surface).
        //   * SelectionChanged event (WM_NOTIFY): a real user click makes the
        //     list-view send WM_NOTIFY to its PARENT (the form) with lParam =
        //     an NMHDR* whose code is LVN_ITEMCHANGED. Headless acceptance POSTS
        //     WM_NOTIFY (the phase-8 posted-notification pattern); the form's
        //     WM_NOTIFY dispatch routes the LVN_ITEMCHANGED into the matching
        //     WfListView's SelectionChanged -- which RE-READS the real selection
        //     (LVM_GETITEMSTATE over the rows), so the event observes the list
        //     state at fire time. The list-view ALSO fires LVN_ITEMCHANGED
        //     SYNCHRONOUSLY during some LVM_* sends (each fires the parent
        //     notification in the send's stack -- net48 oracle: the 3 inserts
        //     + the text/setcount sends fire 6 notifications while nothing is
        //     selected -> "sel-1", then the selection send + the posted
        //     notification fire 2 while row 2 is selected -> "sel2;sel2;";
        //     deterministic, byte-exact).
        // net48 oracle: init / insert indices / count / text round-trips /
        // setcount (the virtual-only no-op) / selection / the synchronous+
        // posted selection sequence (p21_selchange_seq) byte-exact. The
        // list-view is WS_CHILD of the HIDDEN form; destroyed before the form.
        INITCOMMONCONTROLSEX icc21 = new INITCOMMONCONTROLSEX();
        icc21.dwSize = 8;
        icc21.dwICC = ICC_LISTVIEW_CLASSES | ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        int icc21Ret = InitCommonControlsEx(ref icc21);
        Console.WriteLine("p21_list_init=" + (icc21Ret != 0));

        IntPtr hList21 = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD, 10, 10, 280, 120, hForm, new IntPtr(ID_LIST21), hModule, IntPtr.Zero);
        P21ListHwnd = hList21;
        P21ListCreated = hList21 != IntPtr.Zero;
        Console.WriteLine("p21_list_created=" + P21ListCreated);

        P21List = new WfListView();
        P21List.Handle = hList21;
        P21List.Id = ID_LIST21;
        P21NotifyCount = 0;
        P21SelChangeSeq = "";
        P21List.SelectionChanged += OnP21ListChanged;

        // Insert three rows -- EXPLICIT-index append (iItem = the current count:
        // 0, 1, 2; the v5.82 control rejects iItem = -1). Each LVM_INSERTITEMW
        // returns the inserted index; the count is read back via LVM_GETITEMCOUNT.
        int p21i0 = P21List.Insert("Alpha");
        int p21i1 = P21List.Insert("Beta");
        int p21i2 = P21List.Insert("Gamma");
        Console.WriteLine("p21_insert0=" + p21i0);
        Console.WriteLine("p21_insert1=" + p21i1);
        Console.WriteLine("p21_insert2=" + p21i2);

        int p21cnt = P21List.Count;
        Console.WriteLine("p21_count=" + p21cnt);

        // Item-text channel: LVM_GETITEMTEXTW reads row 0 back ("Alpha"), then
        // LVM_SETITEMTEXTW overwrites row 1 ("Beta" -> "Beta2") and
        // LVM_GETITEMTEXTW re-reads it.
        string p21txt0 = P21List.GetItemText(0);
        Console.WriteLine("p21_gettext0=" + p21txt0);
        bool p21set1 = P21List.SetItemText(1, "Beta2");
        string p21txt1 = P21List.GetItemText(1);
        Console.WriteLine("p21_settext_ok=" + p21set1);
        Console.WriteLine("p21_gettext1=" + p21txt1);

        // SetCount pre-allocates rows (the VirtualListSize surface): after
        // LVM_SETITEMCOUNT(6) the count reads 6.
        P21List.SetCount(6);
        int p21cnt6 = P21List.Count;
        Console.WriteLine("p21_setcount=" + p21cnt6);
        Console.WriteLine("p21_count_after=" + p21cnt6);

        // Selection channel: LVM_SETITEMSTATE marks row 2 selected (state /
        // stateMask = LVIS_SELECTED); LVM_GETITEMSTATE reads the state bit back
        // and SelectedIndex scans the rows (the first selected row = 2).
        bool p21setstate = P21List.SetItemState(2);
        int p21sel = P21List.SelectedIndex;
        Console.WriteLine("p21_setstate_ok=" + p21setstate);
        Console.WriteLine("p21_sel=" + p21sel);

        // Selection-change event: select row 2 (already selected above) + post
        // the WM_NOTIFY LVN_ITEMCHANGED a real user click sends; drain (the
        // dispatch routes by the control id -> RaiseSelectionChanged re-reads the
        // REAL selection -> row 2 -> "sel2;").
        int p21n1 = PostMessageW(hForm, WM_NOTIFY, new IntPtr(ID_LIST21), IntPtr.Zero);
        Console.WriteLine("p21_inject=" + (p21n1 != 0));
        MSG msg21;
        int d21a = 0;
        while (PeekMessageW(out msg21, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg21);
            DispatchMessageW(ref msg21);
            d21a++;
            if (d21a >= 200) break;
        }
        Console.WriteLine("p21_notify_count=" + P21NotifyCount);
        Console.WriteLine("p21_selchange_seq=" + P21SelChangeSeq);

        P21ListDestroyed = DestroyWindow(hList21) != 0;
        Console.WriteLine("p21_list_destroyed=" + P21ListDestroyed);

        // ===================== Phase 22: TreeView (TreeView class) =====================
        // The TreeView modeled on the comctl32 "SysTreeView32" class (the
        // WinForms TreeView surface, simplified):
        //   * Registration: ICC_TREEVIEW_CLASSES registers the SysTreeView32
        //     class -- the Phase-22 InitCommonControlsEx call ORs it with the
        //     existing tab/bar/progress/listview flags (the registration is
        //     idempotent -- re-registering the already-registered classes is a
        //     no-op, the standard comctl32 pattern).
        //   * Nodes: TVM_INSERTITEMW inserts a node (lParam = a TVINSERTSTRUCTW*
        //     -- hParent + hInsertAfter + a TVITEMW -- built in a
        //     GCHandle-PINNED int[18] with the native x64 layout; hParent =
        //     NULL inserts at the top of the tree hierarchy (the documented
        //     NULL-root semantic), the root's FULL returned handle becomes the
        //     child's hParent). Each insert returns the new node's HTREEITEM --
        //     a comctl32 heap handle that is NOT deterministic across processes
        //     (probed: 5 runs -> 5 different low 32 bits), so only its non-zero
        //     success is printed.
        //   * Count: TVM_GETCOUNT counts nodes.
        //   * Expand: TVM_EXPAND with TVE_EXPAND expands a node (the
        //     TreeView.Expand surface).
        //   * Delete: TVM_DELETEITEM removes a node and its subtree (the
        //     TreeView.Nodes.Remove surface).
        // net48 oracle: init / root+child insert success / count / expand /
        // delete / count-after byte-exact. The tree-view is WS_CHILD of the
        // HIDDEN form; destroyed before the form.
        INITCOMMONCONTROLSEX icc22 = new INITCOMMONCONTROLSEX();
        icc22.dwSize = 8;
        icc22.dwICC = ICC_TREEVIEW_CLASSES | ICC_LISTVIEW_CLASSES | ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        InitCommonControlsEx(ref icc22);

        IntPtr hTree22 = CreateWindowExW(0, WC_TREEVIEW, "",
            WS_CHILD, 10, 10, 280, 160, hForm, new IntPtr(ID_TREE22), hModule, IntPtr.Zero);
        P22TreeHwnd = hTree22;
        P22TreeCreated = hTree22 != IntPtr.Zero;
        Console.WriteLine("p22_tree_created=" + P22TreeCreated);

        P22Tree = new WfTreeView();
        P22Tree.Handle = hTree22;
        P22Tree.Id = ID_TREE22;

        int p22c0 = P22Tree.Count;
        Console.WriteLine("p22_count0=" + p22c0);

        // Insert the root node: TVM_INSERTITEMW with hParent = NULL (the top
        // of the tree hierarchy). The insert's return (the new HTREEITEM) is a
        // comctl32 heap handle that is NOT deterministic across processes
        // (probed: 5 net48 runs -> 5 different low 32 bits) -- discarded here.
        // The ROOT handle is re-fetched through the P/Invoke
        // SendMessageW(TVM_GETNEXTITEM, TVGN_ROOT) channel instead: the
        // transpiler classifies a P/Invoke IntPtr result as "ptr" (intptr_t),
        // while a USER-method IntPtr return degrades to int32 (probed: the
        // local/temp holding it is declared int32_t and truncates the x64
        // handle, and the child insert under a truncated parent returns NULL).
        P22Tree.InsertNode(IntPtr.Zero, "Root");
        IntPtr p22root = SendMessageW(P22Tree.Handle, TVM_GETNEXTITEM, new IntPtr(TVGN_ROOT), IntPtr.Zero);
        Console.WriteLine("p22_root_ok=" + (p22root != IntPtr.Zero));

        // Insert the child node under the root (hParent = the full root
        // handle), then re-fetch the child handle via TVM_GETNEXTITEM
        // (TVGN_CHILD, root) -- the same full-width P/Invoke channel.
        P22Tree.InsertNode(p22root, "Child");
        IntPtr p22child = SendMessageW(P22Tree.Handle, TVM_GETNEXTITEM, new IntPtr(TVGN_CHILD), p22root);
        Console.WriteLine("p22_child_ok=" + (p22child != IntPtr.Zero));

        int p22cnt = P22Tree.Count;
        Console.WriteLine("p22_count=" + p22cnt);

        // Expand the root node: TVM_EXPAND with TVE_EXPAND (TRUE on success).
        bool p22exp = P22Tree.Expand(p22root);
        Console.WriteLine("p22_expand_ok=" + p22exp);

        // Delete the child node: TVM_DELETEITEM removes it and its subtree
        // (TRUE on success); the count drops back to the root only.
        bool p22del = P22Tree.DeleteNode(p22child);
        Console.WriteLine("p22_delete_ok=" + p22del);

        int p22cntA = P22Tree.Count;
        Console.WriteLine("p22_count_after=" + p22cntA);

        P22TreeDestroyed = DestroyWindow(hTree22) != 0;
        Console.WriteLine("p22_tree_destroyed=" + P22TreeDestroyed);

        // ===================== Phase 23: PictureBox (PictureBox class) =====================
        // The PictureBox modeled on the user32 built-in "STATIC" class with
        // the SS_BITMAP style (the WinForms PictureBox surface, simplified):
        //   * Creation: a "STATIC" window with SS_BITMAP is the native home
        //     of the WinForms PictureBox -- it accepts the STM_* image
        //     messages (no InitCommonControlsEx needed; STATIC is a user32
        //     built-in).
        //   * Image: LoadImageW(hinst = NULL, name = the OBM_CLOSE ordinal,
        //     IMAGE_BITMAP, cx = cy = 0, LR_SHARED) loads the system's SHARED
        //     stock "close box" bitmap -- the WinForms PictureBox.Image
        //     surface. STM_SETIMAGE sets it (returns the PREVIOUS bitmap
        //     handle -- NULL for a fresh control) and STM_GETIMAGE reads the
        //     CURRENT handle back: the Image get/set round-trip (set returns
        //     previous, get returns the set handle). The handle is a SHARED
        //     system bitmap, so the equality probe is deterministic and the
        //     control must NOT free it.
        //   * StyleBit: GetWindowLongW(GWL_STYLE) & SS_BITMAP reads the
        //     control's bitmap-mode style bit back.
        //   * Parent: GetParent(pic) == the form (WS_CHILD ownership).
        // net48 oracle: created / bmp-loaded / set-returns-previous-zero /
        // get-matches-set / style-bit / parent / destroyed byte-exact.
        IntPtr hPic23 = CreateWindowExW(0, "STATIC", "",
            WS_CHILD | SS_BITMAP, 10, 10, 32, 32, hForm, new IntPtr(ID_PIC23), hModule, IntPtr.Zero);
        P23Pic = new WfPictureBox();
        P23Pic.Handle = hPic23;
        P23Pic.Id = ID_PIC23;
        Console.WriteLine("p23_pic_created=" + (hPic23 != IntPtr.Zero));

        IntPtr p23bmp = LoadImageW(IntPtr.Zero, new IntPtr(OBM_CLOSE), IMAGE_BITMAP, 0, 0, LR_SHARED);
        Console.WriteLine("p23_bmp_loaded=" + (p23bmp != IntPtr.Zero));

        P23Pic.Image = p23bmp.ToInt64();
        Console.WriteLine("p23_setimage_prev_zero=" + (P23Pic.PrevImage == IntPtr.Zero));

        Console.WriteLine("p23_getimage_matches=" + (P23Pic.Image == p23bmp.ToInt64()));

        Console.WriteLine("p23_style_bitmap=" + P23Pic.StyleBitmap);

        Console.WriteLine("p23_parent_is_form=" + (GetParent(P23Pic.Handle) == hForm));

        P23PicDestroyed = DestroyWindow(hPic23) != 0;
        Console.WriteLine("p23_pic_destroyed=" + P23PicDestroyed);

        // ===================== Phase 24: NotifyIcon (NotifyIcon class) =====================
        // The WinForms NotifyIcon modeled on the shell32 NOTIFYICONDATAW
        // protocol (the real tray-icon surface):
        //   * Icon: LoadIconW(hinst = NULL, name = the IDI_APPLICATION
        //     ordinal, 32512) loads the system's STOCK application icon -- the
        //     NotifyIcon.Icon surface. The handle is a SHARED system resource
        //     (never freed by the caller).
        //   * ADD: Shell_NotifyIconW(NIM_ADD) registers the icon with the
        //     shell. The NOTIFYICONDATAW struct is built field-by-field in a
        //     GCHandle-PINNED int[244] carrying the native x64 layout
        //     (probed: sizeof = 976 bytes -- cbSize@0, hWnd@8, uID@16,
        //     uFlags@20, uCallbackMessage@24, hIcon@32, dwState@296,
        //     dwStateMask@300): cbSize = 976, hWnd = the form (low/high 32-bit
        //     halves), uID = ID_ICON24, uFlags = NIF_MESSAGE|NIF_ICON (the
        //     callback message + icon are valid), uCallbackMessage = P24_CB_MSG
        //     (the message the shell sends the icon's window when the user
        //     interacts with it), hIcon = the loaded icon handle.
        //   * MODIFY: flip the uFlags to add NIF_STATE and set dwState /
        //     dwStateMask = NIS_HIDDEN -- the NotifyIcon.Visible = false
        //     surface (hiding the icon). Shell_NotifyIconW(NIM_MODIFY) applies
        //     it.
        //   * DELETE: Shell_NotifyIconW(NIM_DELETE) removes the icon.
        //   * Callback: the shell would post P24_CB_MSG (wParam = the icon's
        //     uID) to the form's window on a real user interaction. Headless,
        //     the test self-posts the same message; the drain loop dispatches
        //     it and the WndProc accepts it (P24IconCbCount = 1).
        // net48 oracle: loaded / added / modified / deleted / callback-posted /
        // callback-count byte-exact (NIM_* come back FALSE in a session
        // without a visible tray -- both runs see the same shell).
        IntPtr p24icon = LoadIconW(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
        P24IconLoaded = p24icon != IntPtr.Zero;
        Console.WriteLine("p24_icon_loaded=" + P24IconLoaded);

        int[] nid = new int[244];
        nid[0] = 976;
        nid[2] = (int)(hForm.ToInt64() & 0xFFFFFFFFL);
        nid[3] = (int)(hForm.ToInt64() >> 32);
        nid[4] = ID_ICON24;
        nid[5] = NIF_MESSAGE | NIF_ICON;
        nid[6] = P24_CB_MSG;
        nid[8] = (int)(p24icon.ToInt64() & 0xFFFFFFFFL);
        nid[9] = (int)(p24icon.ToInt64() >> 32);
        GCHandle nidPin = GCHandle.Alloc(nid, GCHandleType.Pinned);
        try
        {
            IntPtr nidPtr = nidPin.AddrOfPinnedObject();
            P24IconAdded = Shell_NotifyIconW(NIM_ADD, nidPtr) != 0;
            Console.WriteLine("p24_icon_added=" + P24IconAdded);

            nid[5] = NIF_MESSAGE | NIF_ICON | NIF_STATE;
            nid[74] = NIS_HIDDEN;
            nid[75] = NIS_HIDDEN;
            P24IconModified = Shell_NotifyIconW(NIM_MODIFY, nidPtr) != 0;
            Console.WriteLine("p24_icon_modified=" + P24IconModified);

            P24IconDeleted = Shell_NotifyIconW(NIM_DELETE, nidPtr) != 0;
            Console.WriteLine("p24_icon_deleted=" + P24IconDeleted);
        }
        finally
        {
            nidPin.Free();
        }

        P24CallbackPosted = PostMessageW(hForm, P24_CB_MSG, new IntPtr(ID_ICON24), IntPtr.Zero) != 0;
        Console.WriteLine("p24_callback_posted=" + P24CallbackPosted);

        MSG msg24;
        int drained24 = 0;
        while (PeekMessageW(out msg24, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg24);
            DispatchMessageW(ref msg24);
            drained24++;
            if (drained24 >= 200) break;
        }
        Console.WriteLine("p24_callback_count=" + P24IconCbCount);

        // ===================== Phase 25: file dialog (GetOpenFileNameW) =====================
        // The WinForms OpenFileDialog modeled on the comdlg32 OPENFILENAMEW
        // protocol. The struct is built field-by-field in a GCHandle-PINNED
        // int[38] carrying the native x64 layout (152 bytes -- sizeof and every
        // field offset are PROBE-VERIFIED, offsetof on ming-w64: lStructSize@0
        // = 152, hwndOwner@8, lpstrFilter@24, lpstrFile@48, nMaxFile@56 = 128,
        // lpstrTitle@88, Flags@96, nFileOffset@100, nFileExtension@102):
        //   * hwndOwner = NULL -- a HIDDEN owner (the test form is never
        //     shown) suppresses the dialog entirely (probed: the "#32770" +
        //     title window never appears), so the owner is left unset.
        //   * lpstrFilter = "All\0*.*\0" (StringToHGlobalUni -- the dialog's
        //     "All Files" filter); lpstrTitle = P25_TITLE ("Wf25Open") -- the
        //     dialog caption the main thread matches on (title-match is
        //     REQUIRED: a class-only "#32770" FindWindowW hits unrelated
        //     process dialogs, probed).
        //   * lpstrFile = an AllocHGlobal(256) buffer PRE-FILLED with the
        //     running executable's full path (GetModuleFileNameW) -- a
        //     non-empty initial file name is mandatory (an empty name fails
        //     the OK click). The dialog keeps the file NAME and returns it
        //     with its current directory prepended.
        //   * Flags = 0 -- the OFN_FILEMUSTEXIST|OFN_PATHMUSTEXIST flags the
        //     plan listed FAIL the OK click (probed: a name not in the
        //     dialog's current directory pops an existence-error box -- the
        //     same "#32770" + title class -- and the synchronous BM_CLICK
        //     deadlocks on it), so validation flags are left OFF; the click
        //     then returns TRUE with the current directory + file name.
        // GetOpenFileNameW BLOCKS its calling thread, so the call runs on a
        // WORKER thread (phase-6 pattern; it allocates nothing). The main
        // thread finds the dialog by title, clicks the OK button via
        // GetDlgItem(IDOK) + SYNCHRONOUS SendMessageW(BM_CLICK) (PostMessageW
        // is ineffective, probed), and joins the worker before reading the
        // lpstrFile path back (the suffix is derived from the path STRING --
        // the nFileExtension writeback is unreliable when the dialog's result
        // equals the pre-filled name, probed).
        IntPtr p25FileBuf = Marshal.AllocHGlobal(2048);
        IntPtr p25Filter = Marshal.StringToHGlobalUni("All\0*.*\0");
        IntPtr p25Title = Marshal.StringToHGlobalUni(P25_TITLE);
        int[] ofn25 = new int[38];
        ofn25[0] = 152;
        long pf25 = p25Filter.ToInt64();
        ofn25[6] = (int)(pf25 & 0xFFFFFFFFL);
        ofn25[7] = (int)((pf25 >> 32) & 0xFFFFFFFFL);
        long fb25 = p25FileBuf.ToInt64();
        ofn25[12] = (int)(fb25 & 0xFFFFFFFFL);
        ofn25[13] = (int)((fb25 >> 32) & 0xFFFFFFFFL);
        ofn25[14] = 1024;
        long pt25 = p25Title.ToInt64();
        ofn25[22] = (int)(pt25 & 0xFFFFFFFFL);
        ofn25[23] = (int)((pt25 >> 32) & 0xFFFFFFFFL);
        ofn25[24] = 0;
        GCHandle ofn25Pin = GCHandle.Alloc(ofn25, GCHandleType.Pinned);
        P25OfnPtr = ofn25Pin.AddrOfPinnedObject();
        try
        {
            // Pre-fill lpstrFile with the running exe's full path (a
            // deterministic non-empty file name -- "winforms1.exe" on net48,
            // "il2cpp_gen_exe.exe" on the transpiled run, extension "exe"
            // on both; the dialog keeps the name and prepends its directory).
            GetModuleFileNameW(IntPtr.Zero, p25FileBuf, 1024);

            P25OpenResult = -1;
            Thread p25Thread = new Thread(P25OpenFileWorker);
            // Background: if the dialog were ever not found/dismissed, the
            // blocked worker must NOT prevent process exit (a foreground
            // thread would hang teardown). Background keeps the test fail-safe.
            p25Thread.IsBackground = true;
            p25Thread.Start();

            // Find the dialog: dialog class "#32770" + the P25_TITLE caption
            // (a TITLE match is required -- a class-only match hits unrelated
            // process dialogs, probed). Poll briefly -- the dialog is created
            // inside GetOpenFileNameW (bounded: 200 x 2 ms max).
            IntPtr dlg25 = IntPtr.Zero;
            int polls25 = 0;
            while (dlg25 == IntPtr.Zero && polls25 < 200)
            {
                dlg25 = FindWindowW(DLG_CLASS, P25_TITLE);
                if (dlg25 == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    polls25++;
                }
            }
            P25Shown = dlg25 != IntPtr.Zero;
            Console.WriteLine("p25_shown=" + P25Shown);

            // Find the OK button (control id IDOK) and click it with a
            // SYNCHRONOUS BM_CLICK -- the click must be a SendMessageW
            // (a posted BM_CLICK is ineffective on this dialog, probed). The
            // button becomes visible a moment AFTER the dialog appears, so
            // poll briefly (bounded) -- deterministic across scheduling.
            IntPtr ok25 = IntPtr.Zero;
            int okPolls25 = 0;
            while (ok25 == IntPtr.Zero && okPolls25 < 200)
            {
                ok25 = GetDlgItem(dlg25, IDOK);
                if (ok25 == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    okPolls25++;
                }
            }
            if (ok25 != IntPtr.Zero)
            {
                SendMessageW(ok25, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }

            // Bounded wait for the worker (the dialog closes after the click
            // and GetOpenFileNameW returns -- ~400 ms is ample; if the dialog
            // was never dismissed, P25OpenResult stays -1 and the test FAILs
            // cleanly instead of hanging).
            int waits25 = 0;
            while (p25Thread.IsAlive && waits25 < 200)
            {
                Thread.Sleep(2);
                waits25++;
            }
            P25Closed = P25OpenResult != 0;
            Console.WriteLine("p25_closed=" + P25Closed);

            // Read the selected path back and derive the extension from the
            // path STRING (the last '.'): the OPENFILENAMEW.nFileExtension
            // readback is NOT reliable here -- probed, when the dialog's
            // result equals the pre-filled name it returns the struct's
            // nFileOffset/nFileExtension untouched (both 0). The returned
            // path always ends with the pre-filled file name's extension
            // ("exe" on both compilers), so the string-derived suffix is
            // deterministic.
            string p25path = Marshal.PtrToStringUni(p25FileBuf);
            P25FileNonEmpty = p25path != null && p25path.Length > 0;
            Console.WriteLine("p25_file_nonempty=" + P25FileNonEmpty);
            if (P25FileNonEmpty)
            {
                int dot25 = p25path.LastIndexOf('.');
                if (dot25 >= 0 && dot25 < p25path.Length - 1)
                {
                    P25FileSuffix = p25path.Substring(dot25 + 1);
                }
            }
            Console.WriteLine("p25_file_suffix=" + P25FileSuffix);
        }
        finally
        {
            ofn25Pin.Free();
            Marshal.FreeHGlobal(p25Filter);
            Marshal.FreeHGlobal(p25Title);
            Marshal.FreeHGlobal(p25FileBuf);
        }

        // ===================== Phase 26: ColorDialog (ChooseColorW) =====================
        // The WinForms ColorDialog modeled on the comdlg32 CHOOSECOLORW
        // protocol. The struct is built field-by-field in a GCHandle-PINNED
        // int[18] carrying the native x64 layout (72 bytes -- lStructSize@0 =
        // 72, hwndOwner@8 = NULL -- the same hidden-owner suppression probed
        // in phase 25, so the owner is left unset -- hInstance@16 = NULL,
        // rgbResult@24 = P26_PRESET, lpCustColors@32 = the SECOND pinned
        // int[16] pointer low/high, Flags@40 = CC_RGBINIT, lCustData@48 =
        // lpfnHook@56 = lpTemplateName@64 = NULL):
        //   * rgbResult = the preset 0xFF; CC_RGBINIT makes the dialog start
        //     with that color selected, and clicking OK immediately returns it
        //     unchanged (headless: no user interaction).
        //   * lpCustColors = a pinned int[16] (the 16 DWORD custom-color
        //     cells) the dialog READS and WRITES -- the caller owns it.
        // ChooseColorW BLOCKS its calling thread, so the call runs on a
        // WORKER thread (phase-25 pattern; it allocates nothing). The main
        // thread finds the dialog -- NOT by title (the caption is a localized
        // system string, unsearchable) but by walking the top-level "#32770"
        // windows (FindWindowExW, class-only) and filtering
        // GetWindowThreadProcessId == P26WorkerTid (the creating thread IS
        // the worker) -- clicks the OK button via GetDlgItem(IDOK) +
        // SYNCHRONOUS SendMessageW(BM_CLICK) (the phase-25 probe), and waits
        // for the worker before reading rgbResult back.
        int[] cust26 = new int[16];
        GCHandle cust26Pin = GCHandle.Alloc(cust26, GCHandleType.Pinned);
        int[] ccw26 = new int[18];
        ccw26[0] = 72;
        ccw26[6] = P26_PRESET;
        long c26 = cust26Pin.AddrOfPinnedObject().ToInt64();
        ccw26[8] = (int)(c26 & 0xFFFFFFFFL);
        ccw26[9] = (int)((c26 >> 32) & 0xFFFFFFFFL);
        ccw26[10] = CC_RGBINIT;
        GCHandle ccw26Pin = GCHandle.Alloc(ccw26, GCHandleType.Pinned);
        P26CcwPtr = ccw26Pin.AddrOfPinnedObject();
        try
        {
            P26ColorResult = -1;
            Thread p26Thread = new Thread(P26ColorWorker);
            // Background: if the dialog were ever not found/dismissed, the
            // blocked worker must NOT prevent process exit (a foreground
            // thread would hang teardown). Background keeps the test fail-safe.
            p26Thread.IsBackground = true;
            p26Thread.Start();

            // Find the dialog: walk the top-level "#32770" windows and filter
            // by the creating thread (GetWindowThreadProcessId == the worker's
            // recorded tid -- the caption is a localized string, so a title
            // match is impossible). Poll briefly -- the dialog is created
            // inside ChooseColorW (bounded: 200 x 2 ms max).
            IntPtr dlg26 = IntPtr.Zero;
            int polls26 = 0;
            while (dlg26 == IntPtr.Zero && polls26 < 200)
            {
                IntPtr after26 = IntPtr.Zero;
                while (true)
                {
                    IntPtr walk26 = FindWindowExW(IntPtr.Zero, after26, DLG_CLASS, null);
                    if (walk26 == IntPtr.Zero) break;
                    if (GetWindowThreadProcessId(walk26, IntPtr.Zero) == P26WorkerTid) { dlg26 = walk26; break; }
                    after26 = walk26;
                }
                if (dlg26 == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    polls26++;
                }
            }
            P26ColorDlgFound = dlg26 != IntPtr.Zero;
            Console.WriteLine("p26_color_dlg_found=" + P26ColorDlgFound);

            // Find the OK button (control id IDOK) and click it with a
            // SYNCHRONOUS BM_CLICK -- the click must be a SendMessageW (a
            // posted BM_CLICK is ineffective on this dialog, the phase-25
            // probe). The button becomes visible a moment AFTER the dialog
            // appears, so poll briefly (bounded) -- deterministic across
            // scheduling.
            IntPtr ok26 = IntPtr.Zero;
            int okPolls26 = 0;
            while (ok26 == IntPtr.Zero && okPolls26 < 200)
            {
                ok26 = GetDlgItem(dlg26, IDOK);
                if (ok26 == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    okPolls26++;
                }
            }
            if (ok26 != IntPtr.Zero)
            {
                SendMessageW(ok26, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }
            P26ColorOkClicked = ok26 != IntPtr.Zero;
            Console.WriteLine("p26_color_ok_clicked=" + P26ColorOkClicked);

            // Bounded wait for the worker (the dialog closes after the click
            // and ChooseColorW returns -- ~400 ms is ample; if the dialog was
            // never dismissed, P26ColorResult stays -1 and the test FAILs
            // cleanly instead of hanging).
            int waits26 = 0;
            while (p26Thread.IsAlive && waits26 < 200)
            {
                Thread.Sleep(2);
                waits26++;
            }
            P26ColorClosed = P26ColorResult != 0;
            Console.WriteLine("p26_color_closed=" + P26ColorClosed);

            // Read the selected color back (rgbResult@24) -- the dialog wrote
            // the (unchanged) preset on OK -- and compare with the preset.
            P26ColorValue = ccw26[6] & 0xFF;
            Console.WriteLine("p26_rgb_value=" + P26ColorValue);
            P26ColorMatches = P26ColorValue == P26_PRESET;
            Console.WriteLine("p26_rgb_matches=" + P26ColorMatches);
        }
        finally
        {
            ccw26Pin.Free();
            cust26Pin.Free();
        }

        // ===================== Phase 27: FontDialog (ChooseFontW) =====================
        // The WinForms FontDialog modeled on the comdlg32 CHOOSEFONTW protocol.
        // A LOGFONTW is built in a GCHandle-PINNED int[23] carrying the native
        // x64 layout (92 bytes, offsetof-probed: lfHeight@0, lfWidth@4,
        // lfEscapement@8, lfOrientation@12, lfWeight@16, lfItalic@20,
        // lfUnderline@21, lfStrikeOut@22, lfCharSet@23, lfOutPrecision@24,
        // lfClipPrecision@25, lfQuality@26, lfPitchAndFamily@27,
        // lfFaceName@28): lfHeight=-16 (FONT_HEIGHT), lfWeight=FW_NORMAL (400),
        // lfCharSet=DEFAULT_CHARSET (1) with italic/underline/strikeout 0
        // (packed as the single int[5] = 0x01000000) and lfFaceName="Arial" as
        // WCHAR pairs -- 'A'@28 'r'@30 'i'@32 'a'@34 'l'@36 then NULL (int[7] =
        // 0x00720041, int[8] = 0x00610069, int[9] = 0x0000006C, rest 0). The
        // CHOOSEFONTW is a second GCHandle-PINNED int[26] (104 bytes,
        // offsetof-probed -- the audit draft's 96B was short the SDK's trailing
        // hInstance/lpszStyle/nFontType/nSizeMin/nSizeMax fields):
        // lStructSize@0 = 104, hwndOwner@8 = NULL (the phase-25 hidden-owner
        // suppression), hDC@16 = NULL, lpLogFont@24 = the pinned LOGFONTW
        // pointer, iPointSize@32 = written by the dialog, Flags@36 =
        // CF_INITTOLOGFONTSTRUCT (the dialog initializes from the LOGFONTW and
        // clicking OK returns it unchanged headless).
        // ChooseFontW BLOCKS its calling thread, so the call runs on a WORKER
        // thread (phase-25/26 pattern; it allocates nothing). The main thread
        // finds the dialog by the "#32770" class walk + GetWindowThreadProcessId
        // == P27WorkerTid filter (the caption is a localized system string --
        // the phase-26 lesson), clicks the OK button via GetDlgItem(IDOK) +
        // SYNCHRONOUS SendMessageW(BM_CLICK), waits for the worker, then reads
        // the selected point size and face name back from the pinned structs.
        int[] lf27 = new int[23];
        lf27[0] = FONT_HEIGHT;
        lf27[4] = FW_NORMAL;
        lf27[5] = 0x01000000; // lfItalic=0|lfUnderline=0|lfStrikeOut=0|lfCharSet=1<<24
        lf27[7] = 0x00720041; // lfFaceName: 'A' | 'r'<<16
        lf27[8] = 0x00610069; // lfFaceName: 'i' | 'a'<<16
        lf27[9] = 0x0000006C; // lfFaceName: 'l'
        GCHandle lf27Pin = GCHandle.Alloc(lf27, GCHandleType.Pinned);
        int[] cf27 = new int[26];
        cf27[0] = 104;
        long l27 = lf27Pin.AddrOfPinnedObject().ToInt64();
        cf27[6] = (int)(l27 & 0xFFFFFFFFL);
        cf27[7] = (int)((l27 >> 32) & 0xFFFFFFFFL);
        cf27[9] = CF_INITTOLOGFONTSTRUCT;
        GCHandle cf27Pin = GCHandle.Alloc(cf27, GCHandleType.Pinned);
        P27CfPtr = cf27Pin.AddrOfPinnedObject();
        try
        {
            P27FontResult = -1;
            Thread p27Thread = new Thread(P27FontWorker);
            // Background: if the dialog were ever not found/dismissed, the
            // blocked worker must NOT prevent process exit (a foreground
            // thread would hang teardown). Background keeps the test fail-safe.
            p27Thread.IsBackground = true;
            p27Thread.Start();

            // Find the dialog: walk the top-level "#32770" windows and filter
            // by the creating thread (GetWindowThreadProcessId == the worker's
            // recorded tid -- the caption is a localized string, so a title
            // match is impossible, the phase-26 lesson). Poll briefly -- the
            // dialog is created inside ChooseFontW (bounded: 200 x 2 ms max).
            IntPtr dlg27 = IntPtr.Zero;
            int polls27 = 0;
            while (dlg27 == IntPtr.Zero && polls27 < 200)
            {
                IntPtr after27 = IntPtr.Zero;
                while (true)
                {
                    IntPtr walk27 = FindWindowExW(IntPtr.Zero, after27, DLG_CLASS, null);
                    if (walk27 == IntPtr.Zero) break;
                    if (GetWindowThreadProcessId(walk27, IntPtr.Zero) == P27WorkerTid) { dlg27 = walk27; break; }
                    after27 = walk27;
                }
                if (dlg27 == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    polls27++;
                }
            }
            P27FontDlgFound = dlg27 != IntPtr.Zero;
            Console.WriteLine("p27_font_dlg_found=" + P27FontDlgFound);

            // Find the OK button (control id IDOK) and click it with a
            // SYNCHRONOUS BM_CLICK -- the click must be a SendMessageW (a
            // posted BM_CLICK is ineffective on this dialog, the phase-25
            // probe). The button becomes visible a moment AFTER the dialog
            // appears, so poll briefly (bounded) -- deterministic across
            // scheduling.
            IntPtr ok27 = IntPtr.Zero;
            int okPolls27 = 0;
            while (ok27 == IntPtr.Zero && okPolls27 < 200)
            {
                ok27 = GetDlgItem(dlg27, IDOK);
                if (ok27 == IntPtr.Zero)
                {
                    Thread.Sleep(2);
                    okPolls27++;
                }
            }
            if (ok27 != IntPtr.Zero)
            {
                SendMessageW(ok27, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            }
            P27FontOkClicked = ok27 != IntPtr.Zero;
            Console.WriteLine("p27_font_ok_clicked=" + P27FontOkClicked);

            // Bounded wait for the worker (the dialog closes after the click
            // and ChooseFontW returns -- ~400 ms is ample; if the dialog was
            // never dismissed, P27FontResult stays -1 and the test FAILs
            // cleanly instead of hanging).
            int waits27 = 0;
            while (p27Thread.IsAlive && waits27 < 200)
            {
                Thread.Sleep(2);
                waits27++;
            }
            P27FontClosed = P27FontResult != 0;
            Console.WriteLine("p27_font_closed=" + P27FontClosed);

            // Read the selected font back: iPointSize@32 (the dialog computed
            // 10 * point size from lfHeight -- nonzero when OK was accepted)
            // and the lfFaceName WCHAR string at LOGFONTW+28.
            P27PointSize = cf27[8];
            P27FaceName = Marshal.PtrToStringUni(new IntPtr(l27 + 28));
            if (P27FaceName == null) { P27FaceName = ""; }
            P27FaceNonEmpty = P27FaceName.Length > 0;
            P27ResultNonZero = P27FontResult != 0;
            Console.WriteLine("p27_point_size_set=" + (P27PointSize > 0));
            Console.WriteLine("p27_face_match=" + (P27FaceName == "Arial"));
            Console.WriteLine("p27_result_nonzero=" + P27ResultNonZero);
        }
        finally
        {
            cf27Pin.Free();
            lf27Pin.Free();
        }

        // ===================== Phase 28: RichTextBox (RichEdit20W) =====================
        // The WinForms RichTextBox modeled on the riched20 RichEdit20W protocol.
        // The RichEdit20W window class is NOT a user32 built-in -- it is
        // registered by riched20.dll, so the class is made available with
        // LoadLibraryW("riched20.dll") (kernel32, the existing shim -- the
        // module handle nonzero proves the class registered). The control is a
        // WS_CHILD of the hidden form created with ES_MULTILINE (the rich-text
        // edit style). The headless sequence is fully SYNCHRONOUS (no worker):
        //   * EM_SETEVENTMASK(ENM_NONE) FIRST -- suppresses ALL EN_* parent
        //     notifications so the text changes below do NOT perturb the global
        //     wmCommandCount (the phase-10 lesson; the return is the PREVIOUS
        //     mask, nonzero by default).
        //   * WM_SETTEXT("Wf28Rich") -> EM_GETTEXTLENGTH (== WM_GETTEXTLENGTH,
        //     8 chars) -> WM_GETTEXT readback ("Wf28Rich").
        //   * EM_SETSEL(0,2) selects the first two chars, EM_REPLACESEL("X")
        //     replaces them -> WM_GETTEXT readback "X28Rich" (the spec draft's
        //     "Xf28Rich" was arithmetically wrong -- "Wf" -> "X" leaves
        //     "X28Rich"; net48 is the authoritative oracle).
        //   * CHARFORMAT2W color round-trip: a GCHandle-PINNED int[29] (116
        //     bytes, offsetof-probed: cbSize@0, dwMask@4, dwEffects@8,
        //     crTextColor@20) writes cbSize=116, dwMask=CFM_COLOR, dwEffects=0
        //     (clears CFE_AUTOCOLOR) and crTextColor=0xFF0000; EM_SETCHARFORMAT
        //     (SCF_SELECTION) applies it, EM_GETCHARFORMAT reads it back and
        //     the crTextColor@20 readback must match.
        //   * DestroyWindow frees the control.
        IntPtr hRichLib = LoadLibraryW("riched20.dll");
        P28LibLoaded = hRichLib != IntPtr.Zero;
        Console.WriteLine("p28_lib_loaded=" + P28LibLoaded);

        IntPtr hRich = CreateWindowExW(0, RICHEDIT_CLASS, "",
            WS_CHILD | ES_MULTILINE, 0, 0, 200, 100,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        P28Created = hRich != IntPtr.Zero;
        Console.WriteLine("p28_created=" + P28Created);

        // Suppress EN_* notifications BEFORE any text operation (the phase-10
        // wmCommandCount lesson). EM_SETEVENTMASK returns the PREVIOUS event
        // mask -- a freshly created RichEdit20W's default mask is 0 (probed),
        // so a single set-to-ENM_NONE returns 0 and proves nothing. The
        // two-step set (ENM_CHANGE=1 first, then ENM_NONE) makes the SECOND
        // call return the 1 we set -- deterministic proof the mask was
        // actually changed, ending at ENM_NONE (all notifications suppressed).
        IntPtr prevMask1 = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(1));
        IntPtr prevMask2 = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        P28MaskSet = prevMask2.ToInt64() == 1;
        Console.WriteLine("p28_mask_set=" + P28MaskSet);

        IntPtr set28 = Marshal.StringToHGlobalUni("Wf28Rich");
        IntPtr setRet28 = SendMessageW(hRich, WM_SETTEXT, IntPtr.Zero, set28);
        Marshal.FreeHGlobal(set28);
        P28SetOk = setRet28 != IntPtr.Zero;
        Console.WriteLine("p28_set_ok=" + P28SetOk);

        IntPtr len28 = SendMessageW(hRich, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        P28Len = len28.ToInt32();
        Console.WriteLine("p28_len=" + P28Len);

        IntPtr gbuf28 = Marshal.AllocHGlobal(256);
        SendMessageW(hRich, WM_GETTEXT, new IntPtr(256), gbuf28);
        P28Text = Marshal.PtrToStringUni(gbuf28);
        if (P28Text == null) { P28Text = ""; }
        Console.WriteLine("p28_text=" + P28Text);

        SendMessageW(hRich, EM_SETSEL, new IntPtr(0), new IntPtr(2));
        IntPtr rep28 = Marshal.StringToHGlobalUni("X");
        IntPtr repRet28 = SendMessageW(hRich, EM_REPLACESEL, IntPtr.Zero, rep28);
        Marshal.FreeHGlobal(rep28);
        P28Replaced = repRet28 != IntPtr.Zero;
        Console.WriteLine("p28_replaced=" + P28Replaced);

        SendMessageW(hRich, WM_GETTEXT, new IntPtr(256), gbuf28);
        P28Text2 = Marshal.PtrToStringUni(gbuf28);
        if (P28Text2 == null) { P28Text2 = ""; }
        Console.WriteLine("p28_text2=" + P28Text2);
        Marshal.FreeHGlobal(gbuf28);

        // CHARFORMAT2W color round-trip (pinned int[29] = 116 bytes,
        // offsetof-probed: cbSize@0, dwMask@4, dwEffects@8, crTextColor@20).
        int[] cf28 = new int[29];
        cf28[0] = 116;          // cbSize
        cf28[1] = CFM_COLOR;    // dwMask
        cf28[2] = 0;            // dwEffects = 0 (clears CFE_AUTOCOLOR)
        cf28[5] = 0x00FF0000;   // crTextColor = red
        GCHandle cf28Pin = GCHandle.Alloc(cf28, GCHandleType.Pinned);
        IntPtr cf28Ptr = cf28Pin.AddrOfPinnedObject();
        IntPtr setCf28 = SendMessageW(hRich, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), cf28Ptr);
        P28ColorSet = setCf28 != IntPtr.Zero;
        Console.WriteLine("p28_color_set=" + P28ColorSet);

        int[] cf28b = new int[29];
        cf28b[0] = 116; // cbSize -- the control needs it BEFORE the get
        GCHandle cf28bPin = GCHandle.Alloc(cf28b, GCHandleType.Pinned);
        IntPtr cf28bPtr = cf28bPin.AddrOfPinnedObject();
        SendMessageW(hRich, EM_GETCHARFORMAT, new IntPtr(SCF_SELECTION), cf28bPtr);
        P28ColorMatch = cf28b[5] == 0x00FF0000;
        Console.WriteLine("p28_color_match=" + P28ColorMatch);
        cf28bPin.Free();
        cf28Pin.Free();

        int dest28 = DestroyWindow(hRich);
        P28Destroyed = dest28 != 0;
        Console.WriteLine("p28_destroyed=" + P28Destroyed);

        // ===================== Phase 29: Timer (SetTimer/KillTimer) =====================
        // The WinForms Timer modeled on the Win32 SetTimer/KillTimer protocol.
        // The headless sequence is fully SYNCHRONOUS (no worker, no real-time
        // dependency):
        //   * SetTimer(hForm, ID_TIMER29, 100000, 0) creates a timer on the
        //     form window with a 100-second interval (never fires in the test)
        //     and returns the timer id (nonzero = created).
        //   * SendMessageW(hForm, WM_TIMER, ID_TIMER29, 0) is injected DIRECTLY
        //     (synchronous to the WndProc, bypassing the message queue) 3
        //     times -- each reaches WndProcImpl's WM_TIMER branch and
        //     increments P29TimerTicks deterministically (no timing flake).
        //   * KillTimer(hForm, ID_TIMER29) removes the timer and returns
        //     nonzero (removed).
        IntPtr timerId29 = SetTimer(hForm, ID_TIMER29, 100000, IntPtr.Zero);
        P29TimerSet = timerId29 != IntPtr.Zero;
        Console.WriteLine("p29_timer_set=" + P29TimerSet);

        SendMessageW(hForm, WM_TIMER, new IntPtr(ID_TIMER29), IntPtr.Zero);
        SendMessageW(hForm, WM_TIMER, new IntPtr(ID_TIMER29), IntPtr.Zero);
        SendMessageW(hForm, WM_TIMER, new IntPtr(ID_TIMER29), IntPtr.Zero);
        Console.WriteLine("p29_timer_ticks=" + P29TimerTicks);

        int kill29 = KillTimer(hForm, ID_TIMER29);
        P29TimerKilled = kill29 != 0;
        Console.WriteLine("p29_timer_killed=" + P29TimerKilled);

        // ===================== Phase 30: MainMenu state + navigation =====================
        // The WinForms MenuItem state surface on top of the phase-10 menu
        // tree. Fully synchronous + post/drain (no worker, no real-time
        // dependency):
        //   * Navigation: GetSubMenu resolves a top-level popup item to its
        //     submenu handle; GetMenuItemID reads a sub-item's WM_COMMAND id (a
        //     popup item itself has no command id -> -1).
        //   * Popup state: GetMenuState (MF_BYPOSITION) on a top-level popup
        //     item returns its submenu item-count mask (the phase-10 "shape
        //     probe" extended to the item-level state channel).
        //   * Check round-trip: CheckMenuItem(MF_CHECKED/MF_UNCHECKED) ->
        //     GetMenuState reflects the MF_CHECKED bit (8 / 0).
        //   * Enable/gray round-trip: EnableMenuItem(MF_GRAYED/MF_DISABLED/
        //     MF_ENABLED) -> GetMenuState reflects the disable bits (1 / 2 / 0).
        //   * Model gating: the WfMenuItem.Checked/Enabled properties
        //     round-trip through the native item state; RaiseClick fires only
        //     when the item is Enabled (the phase-7 control rule for menus) --
        //     posting WM_COMMAND 0x110 while New is disabled raises nothing,
        //     re-enabling it makes the click fire (P10Seq proves the gate).
        P10New.Menu = hFilePopup;

        bool subFile = GetSubMenu(hMenuBar, 0) == hFilePopup;
        bool subHelp = GetSubMenu(hMenuBar, 1) == hHelpPopup;
        bool subNewNull = GetSubMenu(hFilePopup, 0) == IntPtr.Zero;
        Console.WriteLine("p30_submenu_file/help/new_null=" + (subFile && subHelp && subNewNull));

        int idNew = GetMenuItemID(hFilePopup, 0);
        int idOpen = GetMenuItemID(hFilePopup, 1);
        int idAbout = GetMenuItemID(hHelpPopup, 0);
        int idPopup = GetMenuItemID(hMenuBar, 0);
        Console.WriteLine("p30_itemid_new=" + idNew + " open=" + idOpen + " about=" + idAbout + " popup=" + idPopup);

        int stateFile = GetMenuState(hMenuBar, 0, MF_BYPOSITION);
        int stateHelp = GetMenuState(hMenuBar, 1, MF_BYPOSITION);
        Console.WriteLine("p30_popup_state_file=" + stateFile + " help=" + stateHelp);

        int checkPrev0 = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        CheckMenuItem(hFilePopup, 0x110, MF_CHECKED);
        int stateChecked = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        int checkPrev1 = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        CheckMenuItem(hFilePopup, 0x110, MF_UNCHECKED);
        int stateUnchecked = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        Console.WriteLine("p30_check_prev0=" + checkPrev0 + " state_checked=" + stateChecked + " check_prev1=" + checkPrev1 + " state_unchecked=" + stateUnchecked);

        bool grayOk = EnableMenuItem(hFilePopup, 0x110, MF_GRAYED) != -1;
        int stateGrayed = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        bool disOk = EnableMenuItem(hFilePopup, 0x110, MF_DISABLED) != -1;
        int stateDisabled = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        bool enOk = EnableMenuItem(hFilePopup, 0x110, MF_ENABLED) != -1;
        int stateEnabled = GetMenuState(hFilePopup, 0x110, MF_BYCOMMAND);
        Console.WriteLine("p30_gray_ok=" + grayOk + " state_grayed=" + stateGrayed + " dis_ok=" + disOk + " state_disabled=" + stateDisabled + " en_ok=" + enOk + " state_enabled=" + stateEnabled);

        P10New.Enabled = false;
        bool modelEnabledFalse = P10New.Enabled == false;
        P10New.Checked = true;
        bool modelChecked = P10New.Checked;
        P10New.Checked = false;
        P10New.Enabled = true;
        bool modelEnabledTrue = P10New.Enabled;
        Console.WriteLine("p30_model_checked/enabled=" + (modelChecked && modelEnabledFalse && modelEnabledTrue));

        P10New.Enabled = false;
        P10Seq = "";
        int postG30 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P10New.Id), IntPtr.Zero);
        MSG msg30g;
        int d30g = 0;
        while (PeekMessageW(out msg30g, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg30g);
            DispatchMessageW(ref msg30g);
            d30g++;
            if (d30g >= 200) break;
        }
        Console.WriteLine("p30_inject_grayed=" + (postG30 != 0) + " p30_seq_grayed=" + P10Seq);

        P10New.Enabled = true;
        P10Seq = "";
        int postE30 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P10New.Id), IntPtr.Zero);
        MSG msg30e;
        int d30e = 0;
        while (PeekMessageW(out msg30e, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg30e);
            DispatchMessageW(ref msg30e);
            d30e++;
            if (d30e >= 200) break;
        }
        Console.WriteLine("p30_inject_enabled=" + (postE30 != 0) + " p30_seq_enabled=" + P10Seq);

        // ===================== Phase 31: StatusStrip multi-part =====================
        // The StatusStrip enhancement on the stage-9 StatusBar (reuses the
        // P9StatusHwnd handle + comctl32 shim; zero new P/Invoke/shim/struct):
        //   * Parts: SB_SETPARTS (wParam = 3, lParam = a GCHandle-PINNED int[]
        //     right-edge array [150, 300, -1] -- the trailing -1 = "this part
        //     stretches to the control's right edge") splits the strip; the
        //     net48 probe measured SB_GETPARTS returning the PART COUNT (3)
        //     and copying the edges back VERBATIM (the -1 sentinel is
        //     PRESERVED, not resolved to the window width).
        //   * Per-part text: each part carries its OWN text through the
        //     stage-9 W-variant channel with wParam = PART INDEX (0/1/2):
        //     SB_SETTEXT / SB_GETTEXT / SB_GETTEXTLENGTH round-trip
        //     independently per part (the A variants WM_USER+1..3 would need
        //     ANSI read-back, absent from the runtime -- the W channel carries
        //     the part-index form, probed byte-exact).
        //   * Interaction: a posted WF_STATUS_UPDATE (wParam = part index)
        //     updates that part's text in the WndProc and reads it back --
        //     the StatusStrip part-update pattern (phase-4 post/drain).
        int[] p31Edges = new int[3];
        p31Edges[0] = 150;
        p31Edges[1] = 300;
        p31Edges[2] = -1;
        GCHandle p31Pin = GCHandle.Alloc(p31Edges, GCHandleType.Pinned);
        IntPtr p31SetRet = SendMessageW(P9StatusHwnd, SB_SETPARTS, new IntPtr(3), p31Pin.AddrOfPinnedObject());
        p31Pin.Free();
        Console.WriteLine("p31_parts_set=" + (p31SetRet != IntPtr.Zero));

        int[] p31Read = new int[3];
        GCHandle p31ReadPin = GCHandle.Alloc(p31Read, GCHandleType.Pinned);
        IntPtr p31CountRet = SendMessageW(P9StatusHwnd, SB_GETPARTS, new IntPtr(3), p31ReadPin.AddrOfPinnedObject());
        p31ReadPin.Free();
        Console.WriteLine("p31_parts_count=" + p31CountRet.ToInt64());
        Console.WriteLine("p31_part0_right=" + p31Read[0]);
        Console.WriteLine("p31_part1_right=" + p31Read[1]);
        Console.WriteLine("p31_part2_right=" + p31Read[2]);

        string[] p31Texts = new string[3];
        for (int p31i = 0; p31i < 3; p31i++)
        {
            IntPtr pt = Marshal.StringToHGlobalUni("Status" + (char)('A' + p31i));
            SendMessageW(P9StatusHwnd, SB_SETTEXT, new IntPtr(p31i), pt);
            Marshal.FreeHGlobal(pt);
            IntPtr pb = Marshal.AllocHGlobal(256);
            SendMessageW(P9StatusHwnd, SB_GETTEXT, new IntPtr(p31i), pb);
            p31Texts[p31i] = Marshal.PtrToStringUni(pb);
            Marshal.FreeHGlobal(pb);
        }
        Console.WriteLine("p31_text0=" + p31Texts[0] + " / p31_text1=" + p31Texts[1] + " / p31_text2=" + p31Texts[2]);

        P31Updated = "";
        int post31 = PostMessageW(hForm, WF_STATUS_UPDATE, new IntPtr(1), IntPtr.Zero);
        MSG msg31;
        int d31 = 0;
        while (PeekMessageW(out msg31, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg31);
            DispatchMessageW(ref msg31);
            d31++;
            if (d31 >= 200) break;
        }
        Console.WriteLine("p31_update_posted=" + (post31 != 0));
        Console.WriteLine("p31_updated=" + P31Updated);

        // ===================== Phase 32: ToolStrip (ToolbarWindow32) =====================
        // The ToolStrip modeled on the comctl32 "ToolbarWindow32" class
        // (WC_TOOLBAR, registered by the Phase-9 InitCommonControlsEx
        // ICC_BAR_CLASSES -- no new registration, no new shim). The TB_*
        // messages all go through the existing SendMessageW channel; TBBUTTON
        // is built as a GCHandle-PINNED int[8] with the x64 layout probed
        // against net48 Marshal.OffsetOf<TBBUTTON> (iBitmap@0/idCommand@4/
        // fsState@8/fsStyle@9/dwData@16/iString@24, sizeof = 32).
        //   * Add: 5 buttons (New/Open/sep/About/Bold) each appended with
        //     TB_ADDBUTTONSW (the W variant is the message id itself -- the
        //     same A/W channel rule as the Phase-9 status bar; the Unicode
        //     default is probed: CCM_SETUNICODEFORMAT returns 1 both ways).
        //     The toolbar COPIES the button text into its own buffer (probed:
        //     TB_GETBUTTONTEXTW still reads "New" after the iString buffer is
        //     freed), so each Add frees its buffer immediately.
        //   * State: TB_CHECKBUTTON/TB_ISBUTTONCHECKED/TB_GETSTATE (Bold starts
        //     TBSTATE_ENABLED=4 -- probed -- and becomes 5 = ENABLED|CHECKED
        //     after the check); TB_ENABLEBUTTON/TB_GETSTATE (disable drops to
        //     0, re-enable restores 4).
        //   * Interaction: a REAL mouse click on button 0 (WM_LBUTTONDOWN/UP
        //     at the TB_GETITEMRECT center) makes the toolbar relay WM_COMMAND
        //     to the form (probed: LOWORD = the button id, HIWORD = 0) -- the
        //     WndProc WM_COMMAND dispatch raises P32New.Click; a posted
        //     WM_COMMAND 0x1F2 raises P32Open.Click (the phase-10 menu
        //     pattern). Each Click handler mirrors its text into status part 0
        //     (SB_SETTEXT + SB_GETTEXT round-trip): toolbar drives status.
        IntPtr hTool32 = CreateWindowExW(0, WC_TOOLBAR, "",
            WS_CHILD | CCS_TOP, 0, 0, 320, 24, hForm, new IntPtr(ID_TOOL32), hModule, IntPtr.Zero);
        Console.WriteLine("p32_toolbar_created=" + (hTool32 != IntPtr.Zero));
        Console.WriteLine("p32_parent_is_form=" + (GetParent(hTool32) == hForm));

        IntPtr unPrev32 = SendMessageW(hTool32, CCM_SETUNICODEFORMAT, new IntPtr(1), IntPtr.Zero);
        IntPtr unNow32 = SendMessageW(hTool32, CCM_GETUNICODEFORMAT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p32_unicode_prev=" + unPrev32.ToInt64());
        Console.WriteLine("p32_unicode_now=" + unNow32.ToInt64());

        SendMessageW(hTool32, TB_BUTTONSTRUCTSIZE, new IntPtr(32), IntPtr.Zero);
        Console.WriteLine("p32_structsize_sent=True");

        P32Tool = new WfToolStrip();
        P32Tool.Handle = hTool32;
        P32Tool.Id = ID_TOOL32;
        P32New = new WfToolButton(); P32New.Id = ID_TB_NEW; P32New.Text = "New";
        P32Open = new WfToolButton(); P32Open.Id = ID_TB_OPEN; P32Open.Text = "Open";
        P32About = new WfToolButton(); P32About.Id = ID_TB_ABOUT; P32About.Text = "About";
        P32New.Click += OnP32New;
        P32Open.Click += OnP32Open;
        P32About.Click += OnP32About;

        bool p32add1 = P32Tool.Add("New", ID_TB_NEW, TBSTYLE_BUTTON, TBSTATE_ENABLED);
        bool p32add2 = P32Tool.Add("Open", ID_TB_OPEN, TBSTYLE_BUTTON, TBSTATE_ENABLED);
        bool p32add3 = P32Tool.Add("", 0, TBSTYLE_SEP, 0);
        bool p32add4 = P32Tool.Add("About", ID_TB_ABOUT, TBSTYLE_BUTTON, TBSTATE_ENABLED);
        bool p32add5 = P32Tool.Add("Bold", ID_TB_BOLD, TBSTYLE_CHECK, TBSTATE_ENABLED);
        Console.WriteLine("p32_add_ok=" + (p32add1 && p32add2 && p32add3 && p32add4 && p32add5));
        Console.WriteLine("p32_count=" + P32Tool.Count);

        int[] p32g0 = new int[8];
        GCHandle p32g0pin = GCHandle.Alloc(p32g0, GCHandleType.Pinned);
        SendMessageW(hTool32, TB_GETBUTTON, new IntPtr(0), p32g0pin.AddrOfPinnedObject());
        p32g0pin.Free();
        Console.WriteLine("p32_get0_id=" + p32g0[1] + " state=" + (p32g0[2] & 0xFF) + " style=" + ((p32g0[2] >> 8) & 0xFF) + " data=" + p32g0[4]);

        int[] p32g2 = new int[8];
        GCHandle p32g2pin = GCHandle.Alloc(p32g2, GCHandleType.Pinned);
        SendMessageW(hTool32, TB_GETBUTTON, new IntPtr(2), p32g2pin.AddrOfPinnedObject());
        p32g2pin.Free();
        Console.WriteLine("p32_sep_style=" + ((p32g2[2] >> 8) & 0xFF));

        Console.WriteLine("p32_text_new=" + P32Tool.GetText(ID_TB_NEW));
        Console.WriteLine("p32_text_open=" + P32Tool.GetText(ID_TB_OPEN));
        Console.WriteLine("p32_text_about=" + P32Tool.GetText(ID_TB_ABOUT));

        bool p32rectOk = P32Tool.GetItemRect(0);
        Console.WriteLine("p32_itemrect0_nonzero=" + p32rectOk);

        int p32stBefore = P32Tool.GetButtonState(ID_TB_BOLD);
        P32Tool.Check(ID_TB_BOLD, true);
        bool p32isChecked = P32Tool.IsChecked(ID_TB_BOLD);
        int p32stChecked = P32Tool.GetButtonState(ID_TB_BOLD);
        Console.WriteLine("p32_check_prev=" + p32stBefore);
        Console.WriteLine("p32_check_checked=" + p32isChecked);
        Console.WriteLine("p32_check_state=" + p32stChecked);

        bool p32enPrev = P32Tool.Enable(ID_TB_OPEN, false);
        int p32openState0 = P32Tool.GetButtonState(ID_TB_OPEN);
        bool p32enAgain = P32Tool.Enable(ID_TB_OPEN, true);
        int p32openState1 = P32Tool.GetButtonState(ID_TB_OPEN);
        Console.WriteLine("p32_enable_prev=" + p32enPrev);
        Console.WriteLine("p32_open_state0=" + p32openState0);
        Console.WriteLine("p32_enable_again=" + p32enAgain);
        Console.WriteLine("p32_open_state1=" + p32openState1);

        P32Seq = "";
        P32Status = "";
        int cx32 = (P32Tool.RectLeft + P32Tool.RectRight) / 2;
        int cy32 = (P32Tool.RectTop + P32Tool.RectBottom) / 2;
        int lp32 = (cy32 << 16) | (cx32 & 0xFFFF);
        SendMessageW(hTool32, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), new IntPtr(lp32));
        SendMessageW(hTool32, WM_LBUTTONUP, IntPtr.Zero, new IntPtr(lp32));
        MSG msg32a;
        int d32a = 0;
        while (PeekMessageW(out msg32a, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg32a);
            DispatchMessageW(ref msg32a);
            d32a++;
            if (d32a >= 200) break;
        }
        string seq32AfterReal = P32Seq;
        int postOpen32 = PostMessageW(hForm, WM_COMMAND, new IntPtr(ID_TB_OPEN), IntPtr.Zero);
        MSG msg32b;
        int d32b = 0;
        while (PeekMessageW(out msg32b, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg32b);
            DispatchMessageW(ref msg32b);
            d32b++;
            if (d32b >= 200) break;
        }
        string seq32AfterOpen = P32Seq;
        Console.WriteLine("p32_click_inject=" + (postOpen32 != 0 && seq32AfterReal == "new;" && seq32AfterOpen == "new;open;"));
        Console.WriteLine("p32_click_seq=" + P32Seq);
        Console.WriteLine("p32_status=" + P32Status);

        bool p32del = P32Tool.Delete(3);
        int p32countAfter = P32Tool.Count;
        int p32aboutIdx = P32Tool.IndexOf(ID_TB_ABOUT);
        Console.WriteLine("p32_delete_ok=" + p32del);
        Console.WriteLine("p32_count_after=" + p32countAfter);
        Console.WriteLine("p32_about_deleted=" + (p32aboutIdx == -1));

        // ===================== Phase 33: DataGridView (simplified) =====================
        // The DataGridView modeled on a report-view comctl32 list-view: the
        // SAME "SysListView32" class the Phase-21 ListView uses (already
        // registered by the Phase-21 InitCommonControlsEx), created with
        // WS_CHILD | LVS_REPORT -- LVS_REPORT (1) is the list-view style that
        // switches it to the multi-column "report" layout a grid needs (the
        // DataGridView.Columns/HeaderStyle surface, simplified). The whole
        // channel is the existing SendMessageW one: zero new P/Invoke / shim /
        // struct (LVCOLUMNW is a GCHandle-PINNED int[13], offsetof-probed).
        //   * Grid lines: LVM_SETEXTENDEDLISTVIEWSTYLE (LVS_EX_GRIDLINES |
        //     LVS_EX_FULLROWSELECT -- the DataGridView.GridColor/RowHeadersVisible
        //     surface) + LVM_GETEXTENDEDLISTVIEWSTYLE readback (probed: prev 0,
        //     now 0x21 = 33 -- byte-exact).
        //   * Columns: LVM_INSERTCOLUMNW inserts each column at the CURRENT
        //     column count (the explicit-index append -- LVM_INSERTCOLUMNW takes
        //     the column index in wParam, probed 0/1/2 -> 0/1/2). Name/Value/Kind
        //     with fmt 0 (LEFT) / 2 (CENTER) / 1 (RIGHT) and widths 90/70/70.
        //   * Column readback: ColumnCount via LVM_GETHEADER -> HDM_GETITEMCOUNT
        //     (the list-view's own header control); LVM_GETCOLUMNW reads the
        //     header text back (PtrToStringUni) and the fmt (which the header
        //     stores as HDF flags: probed 16384/16386/16385); the widths read
        //     back through the scalar LVM_GETCOLUMNWIDTH (90/70/70).
        //   * Width change: LVM_SETCOLUMNWIDTH (scalar) sets column 0 to 120,
        //     LVM_GETCOLUMNWIDTH re-reads it.
        //   * Rows + cells: the Phase-21 row channel (LVM_INSERTITEMW at the
        //     current count) inserts Alpha/Beta/Gamma; SetCellText/GetCellText
        //     use the LVITEMW iSubItem field (lvi[2]) = the column index:
        //     cell (1,1) = "Yes", cell (1,2) = "No" (the row-1 grid cells).
        //   * ColumnHeaderClick: post WF_GRID_COLCLICK (wParam = the clicked
        //     column) + drain; the WndProc raises the grid's event (records the
        //     column into P33ColClick -- the phase-4 post/drain pattern).
        IntPtr hGrid33 = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD | LVS_REPORT, 0, 0, 320, 120, hForm, new IntPtr(ID_GRID33), hModule, IntPtr.Zero);
        Console.WriteLine("p33_grid_created=" + (hGrid33 != IntPtr.Zero));

        P33Grid = new WfDataGridView();
        P33Grid.Handle = hGrid33;
        P33Grid.Id = ID_GRID33;
        P33ColClick = -1;

        int p33extPrev = P33Grid.GetGridLines();
        bool p33gridSet = P33Grid.SetGridLines(true);
        int p33extNow = P33Grid.GetGridLines();
        Console.WriteLine("p33_gridlines_prev=" + p33extPrev);
        Console.WriteLine("p33_gridlines_set=" + p33gridSet);
        Console.WriteLine("p33_gridlines=" + p33extNow);

        int p33c0 = P33Grid.AddColumn("Name", LVCFMT_LEFT, 90);
        int p33c1 = P33Grid.AddColumn("Value", LVCFMT_CENTER, 70);
        int p33c2 = P33Grid.AddColumn("Kind", LVCFMT_RIGHT, 70);
        Console.WriteLine("p33_col0_add=" + p33c0);
        Console.WriteLine("p33_col1_add=" + p33c1);
        Console.WriteLine("p33_col2_add=" + p33c2);

        Console.WriteLine("p33_colcount=" + P33Grid.ColumnCount);
        Console.WriteLine("p33_col0_text=" + P33Grid.GetColumnText(0));
        Console.WriteLine("p33_col1_text=" + P33Grid.GetColumnText(1));
        Console.WriteLine("p33_col2_text=" + P33Grid.GetColumnText(2));
        Console.WriteLine("p33_col0_fmt=" + P33Grid.GetColumnFmt(0));
        Console.WriteLine("p33_col1_fmt=" + P33Grid.GetColumnFmt(1));
        Console.WriteLine("p33_col2_fmt=" + P33Grid.GetColumnFmt(2));
        Console.WriteLine("p33_col0_width=" + P33Grid.GetColumnWidth(0));
        Console.WriteLine("p33_col1_width=" + P33Grid.GetColumnWidth(1));
        Console.WriteLine("p33_col2_width=" + P33Grid.GetColumnWidth(2));

        bool p33w0 = P33Grid.SetColumnWidth(0, 120);
        Console.WriteLine("p33_setwidth0=" + p33w0);
        Console.WriteLine("p33_width0_after=" + P33Grid.GetColumnWidth(0));

        int p33r0 = P33Grid.Insert("Alpha");
        int p33r1 = P33Grid.Insert("Beta");
        int p33r2 = P33Grid.Insert("Gamma");
        Console.WriteLine("p33_row0=" + p33r0);
        Console.WriteLine("p33_row1=" + p33r1);
        Console.WriteLine("p33_row2=" + p33r2);
        Console.WriteLine("p33_count=" + P33Grid.Count);

        bool p33c11 = P33Grid.SetCellText(1, 1, "Yes");
        bool p33c12 = P33Grid.SetCellText(1, 2, "No");
        Console.WriteLine("p33_cell11_set=" + p33c11);
        Console.WriteLine("p33_cell12_set=" + p33c12);
        Console.WriteLine("p33_cell11=" + P33Grid.GetCellText(1, 1));
        Console.WriteLine("p33_cell12=" + P33Grid.GetCellText(1, 2));

        P33ColClick = -1;
        int post33 = PostMessageW(hForm, WF_GRID_COLCLICK, new IntPtr(1), IntPtr.Zero);
        MSG msg33;
        int d33 = 0;
        while (PeekMessageW(out msg33, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg33);
            DispatchMessageW(ref msg33);
            d33++;
            if (d33 >= 200) break;
        }
        Console.WriteLine("p33_colclick_inject=" + (post33 != 0));
        Console.WriteLine("p33_colclick_col=" + P33ColClick);

        Console.WriteLine("p33_grid_destroyed=" + (DestroyWindow(hGrid33) != 0));

        // ===================== Phase 34: ProgressBar complete surface =====================
        // The Phase-9 ProgressBar (comctl32 "msctls_progress32") driven through
        // its COMPLETE message surface -- the whole channel is the existing
        // SendMessageW one (zero new P/Invoke / shim / struct):
        //   * Range: PBM_SETRANGE32 (20..150) + PBM_GETRANGE. v5.82 probe: the
        //     GETRANGE RETURN is the max only (low word, high word 0) -- the
        //     full min/max comes through the PBRANGE lParam (a GCHandle-PINNED
        //     int[2], the LVCOLUMNW/TBBUTTON discipline); SETRANGE32 returns
        //     the PREVIOUS range packed MAKELONG(min,max) (LOWORD = min,
        //     HIWORD = max -- the decoded evidence below).
        //   * Clamp-on-set: PBM_SETPOS 10 -> min 20, 500 -> max 150 (probed);
        //     PBM_SETPOS returns the PREVIOUS position (round-trip evidence).
        //   * Step: v5.82 PBM_GETSTEP reads 0 always (dead read); PBM_SETSTEP
        //     25 returns the previous step (default 10); PBM_STEPIT advances
        //     by the set step and RETURNS the previous position. High-limit
        //     probe: STEPIT WRAPS (pos = min + (pos+step-max) = pos+step-(max
        //     -min)), it does NOT cap -- probed 140+25 -> 35 on range 20..150.
        //   * Colors: PBM_SETBARCOLOR / PBM_SETBKCOLOR return the PREVIOUS
        //     color (v5.82 default reads 0xFF000000, the "default" sentinel);
        //     PBM_GETBARCOLOR is v6-only -> 0 on v5.82.
        //   * Interaction: posted WF_P34_STEP x4 advances the bar + mirrors
        //     "P34: <pos>%" into status part 0 (progress drives status).
        IntPtr hBar34 = CreateWindowExW(0, "msctls_progress32", "",
            WS_CHILD, 20, 290, 160, 20, hForm, new IntPtr(ID_P34), hModule, IntPtr.Zero);
        Console.WriteLine("p34_created=" + (hBar34 != IntPtr.Zero));
        P34Bar = new WfProgressBar();
        P34Bar.Handle = hBar34;
        P34Bar.Id = ID_P34;
        Console.WriteLine("p34_id=" + P34Bar.Id);

        bool p34rangeSet = P34Bar.SetRange(20, 150);
        Console.WriteLine("p34_range_set=" + p34rangeSet);
        Console.WriteLine("p34_range_prev_min=" + P34Bar.PrevRangeMin);
        Console.WriteLine("p34_range_prev_max=" + P34Bar.PrevRangeMax);
        Console.WriteLine("p34_min=" + P34Bar.Minimum);
        Console.WriteLine("p34_max=" + P34Bar.Maximum);

        P34Bar.Value = 10;
        Console.WriteLine("p34_clamp_lo=" + P34Bar.Value);
        P34Bar.Value = 500;
        Console.WriteLine("p34_clamp_hi=" + P34Bar.Value);

        P34Bar.Value = 140;
        Console.WriteLine("p34_setpos_prev=" + P34Bar.PrevValue);
        Console.WriteLine("p34_value=" + P34Bar.Value);

        Console.WriteLine("p34_step_default=" + P34Bar.Step);
        P34Bar.Step = 25;
        Console.WriteLine("p34_setstep_prev=" + P34Bar.PrevStep);
        Console.WriteLine("p34_step_get=" + P34Bar.Step);

        int p34si1 = P34Bar.PerformStep();
        Console.WriteLine("p34_stepit1_prev=" + p34si1);
        Console.WriteLine("p34_stepit1_pos=" + P34Bar.Value);
        int p34si2 = P34Bar.PerformStep();
        Console.WriteLine("p34_stepit2_prev=" + p34si2);
        Console.WriteLine("p34_stepit2_pos=" + P34Bar.Value);
        int p34si3 = P34Bar.PerformStep();
        Console.WriteLine("p34_stepit3_prev=" + p34si3);
        Console.WriteLine("p34_stepit3_pos=" + P34Bar.Value);

        P34Bar.BarColor = 65280;
        Console.WriteLine("p34_barcolor_prev=" + P34Bar.PrevBarColor);
        P34Bar.BarColor = 255;
        Console.WriteLine("p34_barcolor2_prev=" + P34Bar.PrevBarColor);
        Console.WriteLine("p34_getbarcolor=" + P34Bar.BarColor);
        P34Bar.BkColor = 16777215;
        Console.WriteLine("p34_bkcolor_prev=" + P34Bar.PrevBkColor);
        P34Bar.BkColor = 0;
        Console.WriteLine("p34_bkcolor2_prev=" + P34Bar.PrevBkColor);

        P34Seq = "";
        P34Status = "";
        int post34a = PostMessageW(hForm, WF_P34_STEP, IntPtr.Zero, IntPtr.Zero);
        int post34b = PostMessageW(hForm, WF_P34_STEP, IntPtr.Zero, IntPtr.Zero);
        int post34c = PostMessageW(hForm, WF_P34_STEP, IntPtr.Zero, IntPtr.Zero);
        int post34d = PostMessageW(hForm, WF_P34_STEP, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p34_inject=" + (post34a != 0 && post34b != 0 && post34c != 0 && post34d != 0));
        MSG msg34;
        int d34 = 0;
        while (PeekMessageW(out msg34, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg34);
            DispatchMessageW(ref msg34);
            d34++;
            if (d34 >= 200) break;
        }
        Console.WriteLine("p34_seq=" + P34Seq);
        Console.WriteLine("p34_status=" + P34Status);

        Console.WriteLine("p34_destroyed=" + (DestroyWindow(hBar34) != 0));

        // ===================== Phase 35: TabControl item read/write surface =====================
        // The Phase-16 SysTabControl32 extended with its ITEM read/write
        // surface -- the whole channel is the existing SendMessageW one (zero
        // new P/Invoke / shim / struct):
        //   * Insert order (probed): TCM_INSERTITEMW with wParam=0 front-inserts
        //     (the Phase-16 AddTab mode -- the order REVERSES: TabA/TabB/TabC
        //     read back as TabC/TabB/TabA) while wParam=Count APPENDS (returns
        //     the insert index 0/1/2, order preserved). Phase 35 uses the
        //     append mode so index<->text<->lParam are deterministic.
        //   * Write at insert: mask = TCIF_TEXT|TCIF_PARAM stores BOTH the text
        //     and the lParam (= the page index). The probe verified TCIF_PARAM
        //     must be 0x0008 (the stale constant fixed above) or the lParam is
        //     never stored.
        //   * Read-back: TCM_GETITEMW mask=TCIF_TEXT fills the caller's buffer
        //     (AllocHGlobal + PtrToStringUni, the WfListView.GetItemText
        //     discipline); mask=TCIF_PARAM fills the pinned [8..9] lParam.
        //   * Rename: TCM_SETITEMW mask=TCIF_TEXT (the control COPIES the text,
        //     so the caller's buffer is freed right after the send).
        //   * Geometry: TCM_GETITEMRECT writes a RECT (pinned int[4]) -- read
        //     as a BOOLEAN form (non-empty left<right / top<bottom; the raw
        //     pixels depend on the control's font metrics, so only the
        //     structural booleans are byte-exact); TCM_GETROWCOUNT = 1 row for
        //     the 3 tabs.
        //   * Selection round-trip: TCM_SETCURSEL 2 + TCM_GETCURSEL read-back.
        //   * Interaction: posted WF_TAB35_TEXT(0)/(2) + drain -- the WndProc
        //     handler re-reads the tab text at the posted index inside the
        //     pump (the real TCN_SELCHANGE handler shape); P35Seq records it.
        IntPtr hTab35 = CreateWindowExW(0, WC_TABCONTROL, "",
            WS_CHILD, 10, 10, 280, 120, hForm, new IntPtr(ID_TAB35), hModule, IntPtr.Zero);
        P35TabCreated = hTab35 != IntPtr.Zero;
        Console.WriteLine("p35_tab_created=" + P35TabCreated);
        P35Tab = new WfTabControl();
        P35Tab.Handle = hTab35;
        P35Tab.Id = ID_TAB35;
        Console.WriteLine("p35_id=" + P35Tab.Id);

        // Append-insert (wParam = the current count): Tab0/Tab1/Tab2 land at
        // indices 0/1/2 with lParam = the page index (TCIF_TEXT|TCIF_PARAM).
        int p35i0 = P35Tab.InsertTabText(0, "Tab0");
        int p35i1 = P35Tab.InsertTabText(1, "Tab1");
        int p35i2 = P35Tab.InsertTabText(2, "Tab2");
        Console.WriteLine("p35_insert_ret=" + p35i0 + "/" + p35i1 + "/" + p35i2);
        Console.WriteLine("p35_count=" + P35Tab.Count);

        // Text read-back (TCM_GETITEMW mask = TCIF_TEXT).
        Console.WriteLine("p35_text0=" + P35Tab.GetTabText(0));
        Console.WriteLine("p35_text1=" + P35Tab.GetTabText(1));
        Console.WriteLine("p35_text2=" + P35Tab.GetTabText(2));

        // lParam read-back (mask = TCIF_PARAM, pinned [8..9]).
        Console.WriteLine("p35_param0=" + P35Tab.GetTabParam(0));
        Console.WriteLine("p35_param1=" + P35Tab.GetTabParam(1));
        Console.WriteLine("p35_param2=" + P35Tab.GetTabParam(2));

        // Rename via TCM_SETITEMW + re-read (the write half of the surface).
        Console.WriteLine("p35_settext_ret=" + P35Tab.SetTabText(1, "Renamed"));
        Console.WriteLine("p35_renamed=" + P35Tab.GetTabText(1));

        // Item geometry, BOOLEAN form (the raw pixels are font-metric
        // dependent; the structural booleans are the byte-exact oracle).
        P35Tab.ReadTabRect(0);
        bool p35RectLr = P35Tab.RectLeft < P35Tab.RectRight;
        bool p35RectTb = P35Tab.RectTop < P35Tab.RectBottom;
        bool p35RectPos = P35Tab.RectLeft >= 0 && P35Tab.RectTop >= 0;
        Console.WriteLine("p35_rect_lr=" + p35RectLr);
        Console.WriteLine("p35_rect_tb=" + p35RectTb);
        Console.WriteLine("p35_rect_nonempty=" + (p35RectLr && p35RectTb));
        Console.WriteLine("p35_rect_pos=" + p35RectPos);
        Console.WriteLine("p35_rows=" + P35Tab.RowCount);

        // Selection round-trip (TCM_SETCURSEL 2 + TCM_GETCURSEL read-back).
        P35Tab.SelectedIndex = 2;
        Console.WriteLine("p35_sel=" + P35Tab.SelectedIndex);

        // Posted interaction: the pump handler re-reads the text at the posted
        // page index (TCM_GETITEMW inside the message handler).
        P35Seq = "";
        int post35a = PostMessageW(hForm, WF_TAB35_TEXT, new IntPtr(0), IntPtr.Zero);
        int post35b = PostMessageW(hForm, WF_TAB35_TEXT, new IntPtr(2), IntPtr.Zero);
        Console.WriteLine("p35_inject=" + (post35a != 0 && post35b != 0));
        MSG msg35;
        int d35 = 0;
        while (PeekMessageW(out msg35, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg35);
            DispatchMessageW(ref msg35);
            d35++;
            if (d35 >= 200) break;
        }
        Console.WriteLine("p35_seq=" + P35Seq);

        Console.WriteLine("p35_destroyed=" + (DestroyWindow(hTab35) != 0));

        // ===================== Phase 36: Panel scrolling (window scroll bars) =====================
        // The WinForms Panel's scroll surface -- the WINDOW's OWN scroll bars.
        // A SECOND STATIC container (the Phase-15 subclassed-panel model) is
        // created with the WS_VSCROLL | WS_HSCROLL style bits (the window-style
        // bars, vs the Phase-17 SCROLLBAR CONTROL) and subclassed with the SAME
        // PanelWndProcImpl -- the P36 guard (`hwnd != P36PanelHwnd`) keeps the
        // two panels' procs disjoint:
        //   * Style: the GWL_STYLE readback proves both style bits were set at
        //     creation (p36_style_v / p36_style_h).
        //   * Range: SetScrollRange(SB_VERT, 0..100) / (SB_HORZ, 0..50); the
        //     initial thumb is 0/0 (GetScrollPos readback -- the window bars
        //     read exactly like the Phase-17 control). SetScrollPos RETURNS the
        //     previous position (v25_prev=0 from 0; reset_h_prev=5 from 5).
        //   * Clamp: SetScrollPos(120) clamps to max 100 and SetScrollPos(-5)
        //     clamps to min 0 (the range proof, read back via GetScrollPos).
        //   * Scroll events: a real user scroll on a WINDOW bar sends
        //     WM_VSCROLL / WM_HSCROLL to the WINDOW ITSELF (a scrollbar CONTROL
        //     sends to its parent -- the Phase-17 rule; a window's OWN bars send
        //     to itself). Headless acceptance POSTS the messages (the phase-8
        //     posted pattern) to the P36 panel; the panel's proc branch applies
        //     the scroll (line +/-P36_LINE, page +/-P36_PAGE, thumb codes write
        //     the HIWORD thumb directly), records code+position into P36Seq and
        //     counts it. NOTE: the numeric codes are ONE shared space across both
        //     bars -- SB_LINEDOWN (1) is also the horizontal SB_LINERIGHT and
        //     SB_PAGEUP (2) the horizontal SB_PAGELEFT -- so the horizontal
        //     posts use the same constants (the probe verified the "v:"/"h:"
        //     prefixed names: v:linedown / h:lineright, v:pageup / h:pageleft).
        //   * ShowScrollBar: hiding both bars (SB_BOTH, false) RETAINS the
        //     range + thumb (probed: GetScrollPos keeps reading 65/35 while
        //     hidden); showing again (SB_BOTH, true) restores the same state
        //     (final 65/35 -- the "hide -> position retained -> show -> same"
        //     oracle).
        // net48 oracle: style bits / position round-trips / clamp readbacks /
        // the scroll sequence (p36_seq) / the hide-show retention byte-exact.
        // All windows hidden; scroll messages posted + drained (phase-4 pattern).
        IntPtr hPanel36 = CreateWindowExW(0, "STATIC", "",
            WS_CHILD | WS_VSCROLL | WS_HSCROLL, 20, 30, 300, 200, hForm, new IntPtr(ID_P36), hModule, IntPtr.Zero);
        P36PanelHwnd = hPanel36;
        Console.WriteLine("p36_panel_created=" + (hPanel36 != IntPtr.Zero));

        IntPtr p36Fn = Marshal.GetFunctionPointerForDelegate(PanelWndProc);
        IntPtr oldProc36 = SetWindowLongPtrW(hPanel36, GWLP_WNDPROC, p36Fn);
        Console.WriteLine("p36_subclass_ok=" + (oldProc36 != IntPtr.Zero));

        long p36Style = GetWindowLongW(hPanel36, GWL_STYLE).ToInt64();
        P36StyleV = (p36Style & WS_VSCROLL) != 0;
        P36StyleH = (p36Style & WS_HSCROLL) != 0;
        Console.WriteLine("p36_style_v=" + P36StyleV);
        Console.WriteLine("p36_style_h=" + P36StyleH);

        SetScrollRange(hPanel36, SB_VERT, 0, 100, false);
        SetScrollRange(hPanel36, SB_HORZ, 0, 50, false);
        int p36v0 = GetScrollPos(hPanel36, SB_VERT);
        int p36h0 = GetScrollPos(hPanel36, SB_HORZ);
        Console.WriteLine("p36_v_pos0=" + p36v0);
        Console.WriteLine("p36_h_pos0=" + p36h0);

        int p36v25prev = SetScrollPos(hPanel36, SB_VERT, 25, false);
        int p36v25 = GetScrollPos(hPanel36, SB_VERT);
        Console.WriteLine("p36_v25_prev=" + p36v25prev);
        Console.WriteLine("p36_v25=" + p36v25);
        int p36h5prev = SetScrollPos(hPanel36, SB_HORZ, 5, false);
        int p36h5 = GetScrollPos(hPanel36, SB_HORZ);
        Console.WriteLine("p36_h5_prev=" + p36h5prev);
        Console.WriteLine("p36_h5=" + p36h5);

        SetScrollPos(hPanel36, SB_VERT, 120, false);
        int p36clampHigh = GetScrollPos(hPanel36, SB_VERT);
        SetScrollPos(hPanel36, SB_VERT, -5, false);
        int p36clampLow = GetScrollPos(hPanel36, SB_VERT);
        Console.WriteLine("p36_clamp_high=" + p36clampHigh);
        Console.WriteLine("p36_clamp_low=" + p36clampLow);

        int p36resetV = SetScrollPos(hPanel36, SB_VERT, 30, false);
        int p36resetH = SetScrollPos(hPanel36, SB_HORZ, 10, false);
        Console.WriteLine("p36_reset_v_prev=" + p36resetV);
        Console.WriteLine("p36_reset_h_prev=" + p36resetH);

        // Scroll sequence: 5 vertical (LINEDOWN / LINEUP / PAGEDOWN /
        // THUMBTRACK(80) / THUMBPOSITION(65)) + 3 horizontal (LINERIGHT /
        // PAGELEFT / THUMBTRACK(35)) -- the codes are the shared SB_* space,
        // the bar is chosen by the posted message (WM_VSCROLL vs WM_HSCROLL).
        P36VscrollCount = 0;
        P36HscrollCount = 0;
        P36Seq = "";
        int p36v1 = PostMessageW(hPanel36, WM_VSCROLL, new IntPtr(SB_LINEDOWN), IntPtr.Zero);
        int p36v2 = PostMessageW(hPanel36, WM_VSCROLL, new IntPtr(SB_LINEUP), IntPtr.Zero);
        int p36v3 = PostMessageW(hPanel36, WM_VSCROLL, new IntPtr(SB_PAGEDOWN), IntPtr.Zero);
        int p36v4 = PostMessageW(hPanel36, WM_VSCROLL, new IntPtr(SB_THUMBTRACK | (80 << 16)), IntPtr.Zero);
        int p36v5 = PostMessageW(hPanel36, WM_VSCROLL, new IntPtr(SB_THUMBPOSITION | (65 << 16)), IntPtr.Zero);
        int p36h1 = PostMessageW(hPanel36, WM_HSCROLL, new IntPtr(SB_LINEDOWN), IntPtr.Zero);
        int p36h2 = PostMessageW(hPanel36, WM_HSCROLL, new IntPtr(SB_PAGEUP), IntPtr.Zero);
        int p36h3 = PostMessageW(hPanel36, WM_HSCROLL, new IntPtr(SB_THUMBTRACK | (35 << 16)), IntPtr.Zero);
        Console.WriteLine("p36_inject=" + (p36v1 != 0 && p36v2 != 0 && p36v3 != 0 && p36v4 != 0 && p36v5 != 0
            && p36h1 != 0 && p36h2 != 0 && p36h3 != 0));
        MSG msg36;
        int d36 = 0;
        while (PeekMessageW(out msg36, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg36);
            DispatchMessageW(ref msg36);
            d36++;
            if (d36 >= 200) break;
        }
        Console.WriteLine("p36_vscroll_count=" + P36VscrollCount);
        Console.WriteLine("p36_hscroll_count=" + P36HscrollCount);
        Console.WriteLine("p36_seq=" + P36Seq);

        // ShowScrollBar: hide both bars (position RETAINED -- readback while
        // hidden) then show them again (same 65/35 state).
        int p36hide = ShowScrollBar(hPanel36, SB_BOTH, false);
        Console.WriteLine("p36_hide_ret=" + (p36hide != 0));
        Console.WriteLine("p36_pos_v_hidden=" + GetScrollPos(hPanel36, SB_VERT));
        Console.WriteLine("p36_pos_h_hidden=" + GetScrollPos(hPanel36, SB_HORZ));
        int p36show = ShowScrollBar(hPanel36, SB_BOTH, true);
        Console.WriteLine("p36_show_ret=" + (p36show != 0));
        Console.WriteLine("p36_final_v=" + GetScrollPos(hPanel36, SB_VERT));
        Console.WriteLine("p36_final_h=" + GetScrollPos(hPanel36, SB_HORZ));

        P36Destroyed = DestroyWindow(hPanel36) != 0;
        Console.WriteLine("p36_destroyed=" + P36Destroyed);

        // ===================== Phase 37: ListBox item management (deep surface) =====================
        // The ListBox ITEM-MANAGEMENT surface -- the part Phase 8 never touched
        // (it covered add / select / item-text only). A SECOND LISTBOX (the
        // Phase-8 channel) created as a SHORT 200x60 window with
        // LBS_NOINTEGRALHEIGHT | WS_VSCROLL (the non-integral-height flag lets
        // the short window really scroll so the top-index oracle is observable):
        //   * Insert: LB_INSERTSTRING at an explicit index returns that index
        //     (no LBS_SORT -- the index IS the final position; probed: insert
        //     "Inserted" at 2 into Alpha..Golf returns 2, text reads back at 2).
        //   * GetItemTextLen: LB_GETTEXTLEN -- 5 for "Alpha", LB_ERR (-1) for
        //     an out-of-range index (99).
        //   * Item data: LB_SETITEMDATA / LB_GETITEMDATA attach a caller value
        //     per item. net48 PROBED: LB_SETITEMDATA returns TRUE (1) on
        //     success -- NOT the previous value (the design's prev-value oracle
        //     is superseded; both runs call the same native message so the
        //     oracle is byte-exact either way). GetItemData reads 42 then 7.
        //   * FindString: LB_FINDSTRING prefix search (wraps after the given
        //     index; -1 = start at 0) -- -1/"Beta" -> 1, 1/"Gamma" -> 3,
        //     0/"Zzz" -> -1 (no match).
        //   * TopIndex: LB_GETTOPINDEX 0 -> LB_SETTOPINDEX 3 -> read 3 ->
        //     set 0 -> read 0 (the real-scroll oracle on the short window).
        //   * Delete: LB_DELETESTRING removes index 1 ("Beta") and returns the
        //     REMAINING count (7 of 8); the tail shifts up -- index 1 now
        //     reads "Inserted" (the old index 2).
        //   * Clear: LB_RESETCONTENT empties the list (count 0).
        //   * Double-click event: re-add 3 items, select 2, then POST
        //     WM_COMMAND(id | (LBN_DBLCLK << 16)) + drain (the phase-4 posted
        //     pattern) -- the WndProc's Phase-37 branch routes LBN_DBLCLK into
        //     WfListBox.RaiseDoubleClick, which re-reads the selection (2) and
        //     fires the multicast event (P37DblFired). All windows hidden;
        //     deterministic, headless-safe, byte-exact across net48 + both
        //     compilers.
        IntPtr hList37 = CreateWindowExW(0, "LISTBOX", "Wf37List",
            WS_CHILD | LBS_NOTIFY | LBS_NOINTEGRALHEIGHT | WS_VSCROLL, 20, 260, 200, 60,
            hForm, new IntPtr(ID_LIST37), hModule, IntPtr.Zero);
        P37List = new WfListBox();
        P37List.Handle = hList37;
        P37List.Id = ID_LIST37;
        Console.WriteLine("p37_list_created=" + (hList37 != IntPtr.Zero));
        Console.WriteLine("p37_id=" + P37List.Id);

        // Add 7 items (Alpha..Golf) -- LB_ADDSTRING returns the item index.
        Console.WriteLine("p37_add0=" + P37List.Add("Alpha"));
        Console.WriteLine("p37_add1=" + P37List.Add("Beta"));
        Console.WriteLine("p37_add2=" + P37List.Add("Gamma"));
        Console.WriteLine("p37_add3=" + P37List.Add("Delta"));
        Console.WriteLine("p37_add4=" + P37List.Add("Echo"));
        Console.WriteLine("p37_add5=" + P37List.Add("Foxtrot"));
        Console.WriteLine("p37_add6=" + P37List.Add("Golf"));
        Console.WriteLine("p37_count7=" + P37List.Count);

        // Insert at an explicit index (returns the inserted index).
        Console.WriteLine("p37_insert_ret=" + P37List.Insert(2, "Inserted"));
        Console.WriteLine("p37_count8=" + P37List.Count);
        Console.WriteLine("p37_text2=" + P37List.GetItemText(2));

        // Text length: in-range 5 ("Alpha"), out-of-range LB_ERR (-1).
        Console.WriteLine("p37_len0=" + P37List.GetItemTextLen(0));
        Console.WriteLine("p37_len99=" + P37List.GetItemTextLen(99));

        // Item data round-trip (probed: SET returns TRUE (1), GET reads the value).
        Console.WriteLine("p37_setdata_prev0=" + P37List.SetItemData(3, 42));
        Console.WriteLine("p37_getdata42=" + P37List.GetItemData(3));
        Console.WriteLine("p37_setdata_prev42=" + P37List.SetItemData(3, 7));
        Console.WriteLine("p37_getdata7=" + P37List.GetItemData(3));

        // FindString prefix search (wraps after the start index).
        Console.WriteLine("p37_find_beta=" + P37List.FindString(-1, "Beta"));
        Console.WriteLine("p37_find_gamma=" + P37List.FindString(1, "Gamma"));
        Console.WriteLine("p37_find_zzz=" + P37List.FindString(0, "Zzz"));

        // Top-index scroll on the short window: 0 -> set 3 -> read 3 -> reset 0.
        Console.WriteLine("p37_top0=" + P37List.TopIndex);
        P37List.TopIndex = 3;
        Console.WriteLine("p37_top3=" + P37List.TopIndex);
        P37List.TopIndex = 0;
        Console.WriteLine("p37_top_reset=" + P37List.TopIndex);

        // Delete index 1 ("Beta"): returns the remaining count (7), tail shifts.
        Console.WriteLine("p37_del_ret=" + P37List.Delete(1));
        Console.WriteLine("p37_count_after_del=" + P37List.Count);
        Console.WriteLine("p37_text1_after_del=" + P37List.GetItemText(1));

        // Reset: LB_RESETCONTENT -> empty.
        P37List.Clear();
        Console.WriteLine("p37_count_after_clear=" + P37List.Count);

        // Double-click event: re-add 3, select 2, post LBN_DBLCLK + drain.
        P37List.Add("A");
        P37List.Add("B");
        P37List.Add("C");
        P37List.SelectedIndex = 2;
        P37List.DoubleClick += OnP37DblClick;
        Console.WriteLine("p37_dbl_subs=" + P37List.DblClickCount);
        Console.WriteLine("p37_sel2=" + P37List.SelectedIndex);
        P37Seq = "";
        P37DblFired = 0;
        int post37 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P37List.Id | (LBN_DBLCLK << 16)), hList37);
        Console.WriteLine("p37_inject=" + (post37 != 0));
        MSG msg37;
        int d37 = 0;
        while (PeekMessageW(out msg37, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg37);
            DispatchMessageW(ref msg37);
            d37++;
            if (d37 >= 200) break;
        }
        Console.WriteLine("p37_dblclick_seq=" + P37Seq);
        Console.WriteLine("p37_dbl_fired=" + P37DblFired);

        P37Destroyed = DestroyWindow(hList37) != 0;
        Console.WriteLine("p37_destroyed=" + P37Destroyed);

        // ===================== Phase 38: ComboBox item management (deep surface) =====================
        // The ComboBox ITEM-MANAGEMENT surface -- the CB_* mirror of the
        // Phase-37 WfListBox set (a ComboBox exposes the SAME item lifecycle as
        // a ListBox through its own message numbers). A SECOND COMBOBOX (the
        // Phase-8 channel) created as a SHORT 160x40 window with
        // CBS_DROPDOWNLIST (the list drops only when opened -- headless-safe;
        // the short window's top-index scroll position is stored and read back
        // even while closed):
        //   * Insert: CB_INSERTSTRING at an explicit index returns that index
        //     (no CBS_SORT -- the index IS the final position; probed: insert
        //     "Inserted" at 2 into Alpha..Golf returns 2, text reads back at 2).
        //   * GetItemTextLen: CB_GETLBTEXTLEN -- 5 for "Alpha", CB_ERR (-1) for
        //     an out-of-range index (99).
        //   * Item data: CB_SETITEMDATA / CB_GETITEMDATA attach a caller value
        //     per item. net48 PROBED: CB_SETITEMDATA returns TRUE (1) on
        //     success -- the same measured oracle as LB_SETITEMDATA (both runs
        //     call the same native message so the oracle is byte-exact either
        //     way). GetItemData reads 42 then 7.
        //   * FindString: CB_FINDSTRING prefix search (wraps after the given
        //     index; -1 = start at 0) -- -1/"Beta" -> 1, 1/"Gamma" -> 3,
        //     0/"Zzz" -> -1 (no match).
        //   * TopIndex: CB_GETTOPINDEX 0 -> CB_SETTOPINDEX 3 -> read 3 ->
        //     set 0 -> read 0 (the closed-combo stored scroll position).
        //   * Delete: CB_DELETESTRING removes index 1 ("Beta") and returns the
        //     REMAINING count (7 of 8); the tail shifts up -- index 1 now
        //     reads "Inserted" (the old index 2).
        //   * Clear: CB_RESETCONTENT empties the list (count 0).
        //   * Double-click event: re-add 3 items, select 2, then POST
        //     WM_COMMAND(id | (CBN_DBLCLK << 16)) + drain (the phase-4 posted
        //     pattern) -- the WndProc's Phase-38 branch routes CBN_DBLCLK into
        //     WfComboBox.RaiseDoubleClick, which re-reads the selection (2) and
        //     fires the multicast event (P38DblFired). All windows hidden;
        //     deterministic, headless-safe, byte-exact across net48 + both
        //     compilers.
        IntPtr hCombo38 = CreateWindowExW(0, "COMBOBOX", "Wf38Combo",
            WS_CHILD | WS_TABSTOP | CBS_DROPDOWNLIST, 20, 270, 160, 40,
            hForm, new IntPtr(ID_COMBO38), hModule, IntPtr.Zero);
        P38Combo = new WfComboBox();
        P38Combo.Handle = hCombo38;
        P38Combo.Id = ID_COMBO38;
        Console.WriteLine("p38_combo_created=" + (hCombo38 != IntPtr.Zero));
        Console.WriteLine("p38_id=" + P38Combo.Id);

        // Add 7 items (Alpha..Golf) -- CB_ADDSTRING returns the item index.
        Console.WriteLine("p38_add0=" + P38Combo.Add("Alpha"));
        Console.WriteLine("p38_add1=" + P38Combo.Add("Beta"));
        Console.WriteLine("p38_add2=" + P38Combo.Add("Gamma"));
        Console.WriteLine("p38_add3=" + P38Combo.Add("Delta"));
        Console.WriteLine("p38_add4=" + P38Combo.Add("Echo"));
        Console.WriteLine("p38_add5=" + P38Combo.Add("Foxtrot"));
        Console.WriteLine("p38_add6=" + P38Combo.Add("Golf"));
        Console.WriteLine("p38_count7=" + P38Combo.Count);

        // Insert at an explicit index (returns the inserted index).
        Console.WriteLine("p38_insert_ret=" + P38Combo.Insert(2, "Inserted"));
        Console.WriteLine("p38_count8=" + P38Combo.Count);
        Console.WriteLine("p38_text2=" + P38Combo.GetItemText(2));

        // Text length: in-range 5 ("Alpha"), out-of-range CB_ERR (-1).
        Console.WriteLine("p38_len0=" + P38Combo.GetItemTextLen(0));
        Console.WriteLine("p38_len99=" + P38Combo.GetItemTextLen(99));

        // Item data round-trip (probed: SET returns TRUE (1), GET reads the value).
        Console.WriteLine("p38_setdata_prev0=" + P38Combo.SetItemData(3, 42));
        Console.WriteLine("p38_getdata42=" + P38Combo.GetItemData(3));
        Console.WriteLine("p38_setdata_prev42=" + P38Combo.SetItemData(3, 7));
        Console.WriteLine("p38_getdata7=" + P38Combo.GetItemData(3));

        // FindString prefix search (wraps after the start index).
        Console.WriteLine("p38_find_beta=" + P38Combo.FindString(-1, "Beta"));
        Console.WriteLine("p38_find_gamma=" + P38Combo.FindString(1, "Gamma"));
        Console.WriteLine("p38_find_zzz=" + P38Combo.FindString(0, "Zzz"));

        // Top-index scroll on the short window: 0 -> set 3 -> read 3 -> reset 0.
        Console.WriteLine("p38_top0=" + P38Combo.TopIndex);
        P38Combo.TopIndex = 3;
        Console.WriteLine("p38_top3=" + P38Combo.TopIndex);
        P38Combo.TopIndex = 0;
        Console.WriteLine("p38_top_reset=" + P38Combo.TopIndex);

        // Delete index 1 ("Beta"): returns the remaining count (7), tail shifts.
        Console.WriteLine("p38_del_ret=" + P38Combo.Delete(1));
        Console.WriteLine("p38_count_after_del=" + P38Combo.Count);
        Console.WriteLine("p38_text1_after_del=" + P38Combo.GetItemText(1));

        // Reset: CB_RESETCONTENT -> empty.
        P38Combo.Clear();
        Console.WriteLine("p38_count_after_clear=" + P38Combo.Count);

        // Double-click event: re-add 3, select 2, post CBN_DBLCLK + drain.
        P38Combo.Add("A");
        P38Combo.Add("B");
        P38Combo.Add("C");
        P38Combo.SelectedIndex = 2;
        P38Combo.DoubleClick += OnP38DblClick;
        Console.WriteLine("p38_dbl_subs=" + P38Combo.DblClickCount);
        Console.WriteLine("p38_sel2=" + P38Combo.SelectedIndex);
        P38Seq = "";
        P38DblFired = 0;
        int post38 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P38Combo.Id | (CBN_DBLCLK << 16)), hCombo38);
        Console.WriteLine("p38_inject=" + (post38 != 0));
        MSG msg38;
        int d38 = 0;
        while (PeekMessageW(out msg38, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg38);
            DispatchMessageW(ref msg38);
            d38++;
            if (d38 >= 200) break;
        }
        Console.WriteLine("p38_dblclick_seq=" + P38Seq);
        Console.WriteLine("p38_dbl_fired=" + P38DblFired);

        P38Destroyed = DestroyWindow(hCombo38) != 0;
        Console.WriteLine("p38_destroyed=" + P38Destroyed);

        // ===================== Phase 39: ListView checkbox + multi-select state surface =====================
        // A SECOND SysListView32 (the Phase-21 channel, already registered by
        // the Phase-21 InitCommonControlsEx) created with WS_CHILD | LVS_REPORT
        // + the LVS_EX_CHECKBOXES extended style -- the ListView.CheckBoxes
        // surface. Everything is a SYNCHRONOUS readback (the Phase-33 pattern
        // -- no posted interaction), headless-safe and byte-exact:
        //   * Checkbox: LVM_SETEXTENDEDLISTVIEWSTYLE(LVS_EX_CHECKBOXES)
        //     returns the PREVIOUS extended style (0 for a fresh control -- the
        //     p33 precedent); the GET readback shows the bit (4). The per-item
        //     on/off state is the LVIS_STATEIMAGEMASK bits (state image n <<
        //     12): SetCheckState writes INDEXTOSTATEIMAGEMASK(2)/(1) via
        //     LVM_SETITEMSTATE (the SAME channel as the Phase-21 selection),
        //     GetCheckState decodes the LVM_GETITEMSTATE readback ((r>>12)-1:
        //     fresh rows read 0 "unchecked" -- probed).
        //   * The v6-only LVM_SETCHECKSTATE / LVM_GETCHECKSTATE are probed as
        //     DEAD reads: GET returns 0 even on a checked row, SET returns 0
        //     and leaves the real checkbox unchanged -- the NEGATIVE proof the
        //     v5/v6-shared SETITEMSTATE channel is the real surface here.
        //   * Multi-select: SetItemSelected(0)+(2) mark two rows selected
        //     (LVIS_SELECTED -- INDEPENDENT of the checkbox bits, probed:
        //     unselecting a checked row leaves its checkbox on). The
        //     LVM_GETSELECTEDCOUNT scalar counts them (probed ACCUMULATES: 2
        //     rows -> 2, not an exclusive 1); SelectedIndices walks them via
        //     LVM_GETNEXTITEM(-1, LVNI_SELECTED) into a STRING "0;2;" (never
        //     an int[] -- the M3 SZArray restriction).
        IntPtr hList39 = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD | LVS_REPORT, 20, 300, 280, 120, hForm, new IntPtr(ID_LIST39), hModule, IntPtr.Zero);
        P39List = new WfListView();
        P39List.Handle = hList39;
        P39List.Id = ID_LIST39;
        Console.WriteLine("p39_list_created=" + (hList39 != IntPtr.Zero));
        Console.WriteLine("p39_id=" + P39List.Id);

        // Enable checkboxes: the setter returns the PREVIOUS extended style
        // (0 -- the p33 precedent), the GET readback shows the bit.
        int p39extPrev = (int)SendMessageW(hList39, LVM_SETEXTENDEDLISTVIEWSTYLE,
            new IntPtr(LVS_EX_CHECKBOXES), new IntPtr(LVS_EX_CHECKBOXES)).ToInt64();
        int p39extNow = (int)SendMessageW(hList39, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p39_checkbox_ext=" + p39extPrev);
        Console.WriteLine("p39_checkbox_readback=" + p39extNow);

        // Insert three rows -- the Phase-21 explicit-index append.
        Console.WriteLine("p39_insert0=" + P39List.Insert("Alpha"));
        Console.WriteLine("p39_insert1=" + P39List.Insert("Beta"));
        Console.WriteLine("p39_insert2=" + P39List.Insert("Gamma"));
        Console.WriteLine("p39_count3=" + P39List.Count);

        // Checkbox state: a fresh row reads 0 ("unchecked" -- the state image
        // the control assigns on insert while checkboxes are on).
        Console.WriteLine("p39_check_fresh=" + P39List.GetCheckState(0));

        // SetCheckState round-trips: 0 checked / 2 checked / 1 unchecked ->
        // read back 1 / 1 / 0 (the LVM_GETITEMSTATE state-image decode).
        Console.WriteLine("p39_setchk0=" + P39List.SetCheckState(0, true));
        Console.WriteLine("p39_getchk0=" + P39List.GetCheckState(0));
        Console.WriteLine("p39_setchk2=" + P39List.SetCheckState(2, true));
        Console.WriteLine("p39_getchk2=" + P39List.GetCheckState(2));
        Console.WriteLine("p39_setchk1f=" + P39List.SetCheckState(1, false));
        Console.WriteLine("p39_getchk1=" + P39List.GetCheckState(1));

        // The v6-only checkbox messages are DEAD on v5.82: GET reads 0 on a
        // CHECKED row, SET returns 0 and leaves the real state unchanged.
        Console.WriteLine("p39_v6_get=" + P39List.GetCheckStateV6(0));
        int p39v6set = (int)SendMessageW(hList39, LVM_SETCHECKSTATE, new IntPtr(0), IntPtr.Zero).ToInt64();
        Console.WriteLine("p39_v6_set=" + p39v6set);
        Console.WriteLine("p39_chk0_after_v6=" + P39List.GetCheckState(0));

        // Multi-select: rows 0 + 2 LVIS_SELECTED (independent of the checkbox
        // bits). The scalar count ACCUMULATES (2); the enumeration walks them.
        Console.WriteLine("p39_sel0=" + P39List.SetItemSelected(0, true));
        Console.WriteLine("p39_sel2=" + P39List.SetItemSelected(2, true));
        Console.WriteLine("p39_sel0_read=" + P39List.GetItemSelected(0));
        Console.WriteLine("p39_sel1_read=" + P39List.GetItemSelected(1));
        Console.WriteLine("p39_sel2_read=" + P39List.GetItemSelected(2));
        Console.WriteLine("p39_selcount=" + P39List.SelectedCount);
        Console.WriteLine("p39_indices=" + P39List.SelectedIndices);

        // Cancel round trip: unselect row 0 -> 1 selected, only row 2 left.
        Console.WriteLine("p39_unsel0=" + P39List.SetItemSelected(0, false));
        Console.WriteLine("p39_selcount_after=" + P39List.SelectedCount);
        Console.WriteLine("p39_indices_after=" + P39List.SelectedIndices);

        // Checkbox/selection bit independence: unselecting row 0 left its
        // checkbox checked (the LVIS_SELECTED writes never touched the state
        // image bits).
        Console.WriteLine("p39_chk0_indep=" + P39List.GetCheckState(0));

        // Full clear: unselect row 2 -> nothing selected; the checkboxes
        // (1 and 0) are untouched by the selection lifecycle.
        Console.WriteLine("p39_unsel2=" + P39List.SetItemSelected(2, false));
        Console.WriteLine("p39_selcount_clear=" + P39List.SelectedCount);
        Console.WriteLine("p39_indices_clear=" + P39List.SelectedIndices);
        Console.WriteLine("p39_chk2_indep=" + P39List.GetCheckState(2));
        Console.WriteLine("p39_chk1_indep=" + P39List.GetCheckState(1));

        P39Destroyed = DestroyWindow(hList39) != 0;
        Console.WriteLine("p39_destroyed=" + P39Destroyed);

        // ===================== Phase 40: TextBox (EDIT) deep surface =====================
        // The TextBox DEEP surface -- the EM_* edit-message family Phase 3/7/13
        // never touched (they cover text get/set only). A SECOND EDIT (the
        // Phase-3 channel) created as a multiline ES_MULTILINE | ES_AUTOVSCROLL
        // | ES_AUTOHSCROLL control (the WinForms TextBox.Multiline surface).
        // Headless-safe and fully SYNCHRONOUS:
        //   * SetText("LineA\r\nLineB\r\nLineC") -> WM_GETTEXTLENGTH (19 chars)
        //     -> GetText readback (the CRLF line breaks are probed -- a bare \n
        //     does NOT split a multiline EDIT's lines, linecount reads 1).
        //   * EM_GETLINECOUNT = 3; EM_LINEINDEX(0/1/2) = the line-start CHAR
        //     indices 0/7/14; EM_LINELENGTH(0/7/14) = 5/5/5 (each line's length
        //     via the char index of its start); EM_GETLINE(0/1/2) reads each
        //     line back ("LineA"/"LineB"/"LineC" -- net48 PROBED: the copied
        //     text lands at OFFSET 0, OVERWRITING the caller's length-prefix
        //     WORD, so the readback is at the buffer start, NOT +2).
        //   * EM_LINEFROMCHAR reverse lookup: char 0/5/7 -> line 0/0/1.
        //   * Selection: EM_SETSEL(0,-1) select-all, EM_GETSEL packs
        //     (start | end<<16) = 0 | 19<<16 in the return value.
        //   * Limit: EM_GETLIMITTEXT reads the multiline default 30000, then
        //     EM_SETLIMITTEXT(20) + readback 20.
        //   * Dirty flag: EM_SETMODIFY(1)/EM_GETMODIFY -> 1, then set 0 -> 0.
        //   * Undo: EM_CANUNDO reads 0 after the WM_SETTEXT (no undo entry);
        //     a keystroke (WM_CHAR 'Z', no focus needed -- probed) creates one
        //     (canundo 1); EM_UNDO returns 1 and restores the text; after the
        //     undo canundo still 1 (the redo state); EM_EMPTYUNDOBUFFER clears
        //     it (canundo 0); EM_UNDO on the empty buffer returns 0.
        //     (EM_REPLACESEL does NOT create an undo entry -- probed.)
        //   * EN_CHANGE event: the pure EDIT has NO EM_SETEVENTMASK (that is a
        //     RichEdit-only message, IGNORED by a plain EDIT -- probed: it
        //     returns 0 and the WM_CHAR/EM_UNDO notifications still fire). So
        //     the headless event test uses the DETERMINISTIC posted WM_COMMAND
        //     (id | EN_CHANGE<<16) + drain (the Phase-21 pattern); the WndProc
        //     routes it into the WfTextBox TextChanged event.
        //   * DestroyWindow frees the control.
        IntPtr hEdit40 = CreateWindowExW(0, "EDIT", "",
            WS_CHILD | ES_MULTILINE | ES_AUTOVSCROLL | ES_AUTOHSCROLL, 20, 330, 240, 100,
            hForm, new IntPtr(ID_EDIT40), hModule, IntPtr.Zero);
        P40Edit = new WfTextBox();
        P40Edit.Handle = hEdit40;
        P40Edit.Id = ID_EDIT40;
        Console.WriteLine("p40_edit_created=" + (hEdit40 != IntPtr.Zero));
        Console.WriteLine("p40_id=" + P40Edit.Id);

        // Multi-line text: WM_SETTEXT -> WM_GETTEXTLENGTH -> GetText readback.
        bool p40set = P40Edit.SetText("LineA\r\nLineB\r\nLineC");
        Console.WriteLine("p40_set_ok=" + p40set);
        Console.WriteLine("p40_len=" + P40Edit.GetTextLength());
        Console.WriteLine("p40_text=" + P40Edit.GetText());

        // Line count + line-start char indices + per-line lengths.
        Console.WriteLine("p40_linecount=" + P40Edit.GetLineCount());
        Console.WriteLine("p40_li0=" + P40Edit.LineIndex(0));
        Console.WriteLine("p40_li1=" + P40Edit.LineIndex(1));
        Console.WriteLine("p40_li2=" + P40Edit.LineIndex(2));
        Console.WriteLine("p40_ll0=" + P40Edit.LineLength(0));
        Console.WriteLine("p40_ll7=" + P40Edit.LineLength(7));
        Console.WriteLine("p40_ll14=" + P40Edit.LineLength(14));

        // Per-line readback via EM_GETLINE (length-prefix WORD + text at offset 0).
        Console.WriteLine("p40_gl0_copied=" + P40Edit.GetLineCopied(0));
        Console.WriteLine("p40_gl0_text=" + P40Edit.GetLineText(0));
        Console.WriteLine("p40_gl1_copied=" + P40Edit.GetLineCopied(1));
        Console.WriteLine("p40_gl1_text=" + P40Edit.GetLineText(1));
        Console.WriteLine("p40_gl2_copied=" + P40Edit.GetLineCopied(2));
        Console.WriteLine("p40_gl2_text=" + P40Edit.GetLineText(2));

        // Reverse lookup: the line containing a char index.
        Console.WriteLine("p40_lfc0=" + P40Edit.LineFromChar(0));
        Console.WriteLine("p40_lfc5=" + P40Edit.LineFromChar(5));
        Console.WriteLine("p40_lfc7=" + P40Edit.LineFromChar(7));

        // Selection: select-all + the packed EM_GETSEL readback.
        P40Edit.SelectAll();
        Console.WriteLine("p40_sel_start=" + P40Edit.SelStart);
        Console.WriteLine("p40_sel_end=" + P40Edit.SelEnd);

        // Limit round-trip: the multiline default 30000 -> set 20 -> read 20.
        Console.WriteLine("p40_limit_init=" + P40Edit.LimitText);
        P40Edit.LimitText = 20;
        Console.WriteLine("p40_limit_set=" + P40Edit.LimitText);

        // Dirty-flag round-trip.
        P40Edit.Modified = true;
        Console.WriteLine("p40_modify1=" + P40Edit.Modified);
        P40Edit.Modified = false;
        Console.WriteLine("p40_modify0=" + P40Edit.Modified);

        // Undo round-trip (WM_CHAR creates the undo entry -- no focus needed).
        Console.WriteLine("p40_canundo0=" + P40Edit.CanUndo());
        SendMessageW(hEdit40, WM_CHAR, new IntPtr((int)'Z'), new IntPtr(1));
        Console.WriteLine("p40_canundo1=" + P40Edit.CanUndo());
        Console.WriteLine("p40_undo_ret=" + P40Edit.Undo());
        Console.WriteLine("p40_canundo_after_undo=" + P40Edit.CanUndo());
        Console.WriteLine("p40_len_after_undo=" + P40Edit.GetTextLength());
        P40Edit.EmptyUndoBuffer();
        Console.WriteLine("p40_canundo_after_empty=" + P40Edit.CanUndo());
        Console.WriteLine("p40_undo_empty_ret=" + P40Edit.Undo());

        // EN_CHANGE event: subscribe + POST WM_COMMAND(id | EN_CHANGE<<16) +
        // drain (the deterministic Phase-21 route -- a pure EDIT ignores the
        // RichEdit-only EM_SETEVENTMASK, probed).
        P40Edit.TextChanged += OnP40Change;
        Console.WriteLine("p40_change_subs=" + P40Edit.ChangeCount);
        P40Seq = "";
        P40ChangeFired = 0;
        int post40 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P40Edit.Id | (EN_CHANGE << 16)), hEdit40);
        Console.WriteLine("p40_inject=" + (post40 != 0));
        MSG msg40;
        int d40 = 0;
        while (PeekMessageW(out msg40, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg40);
            DispatchMessageW(ref msg40);
            d40++;
            if (d40 >= 200) break;
        }
        Console.WriteLine("p40_change_fired=" + P40ChangeFired);
        Console.WriteLine("p40_change_seq=" + P40Seq);

        // EM_SCROLL / EM_SCROLLCARET return values (probed on a hidden edit:
        // scroll 0, scrollcaret 1).
        Console.WriteLine("p40_scroll_ret=" + P40Edit.Scroll(1));
        Console.WriteLine("p40_scrollcaret_ret=" + P40Edit.ScrollCaret());

        P40Destroyed = DestroyWindow(hEdit40) != 0;
        Console.WriteLine("p40_destroyed=" + P40Destroyed);

        // ===================== Phase 41: TreeView readback surface =====================
        // The TreeView READBACK surface -- the TVITEMW item channel Phase 22 never
        // touched (it inserts + counts only). A SECOND WC_TREEVIEW (the Phase-22
        // channel) exercising the exact WinForms TreeView node-state surface,
        // headless-safe and fully SYNCHRONOUS:
        //   * Count=0 fresh -> InsertNode("Root") + root = P/Invoke
        //     TVM_GETNEXTITEM(TVGN_ROOT) -> InsertNode(root,"Child") + child =
        //     TVGN_CHILD -> Count=2 (the Phase-22 insert channel).
        //   * GetNodeText(root/child) -> TVM_GETITEMW (mask = TVIF_TEXT|TVIF_HANDLE)
        //     with a 256-byte AllocHGlobal buffer (cchTextMax 128) reads "Root" /
        //     "Child" back (the Phase-33 GetColumnText pattern).
        //   * GetNodeLParam(root) -> TVM_GETITEMW (mask = TVIF_PARAM|TVIF_HANDLE)
        //     reads 42 back -- TVIF_PARAM FIXED 0x0008->0x0004 here (SDK-verified;
        //     the Phase-22 inserts masked in TVIF_STATE instead, so their lParam
        //     was never stored, and no output was polluted -- regression-verified).
        //   * SetNodeText(child,"Child2") -> TVM_SETITEMW (TVIF_TEXT|TVIF_HANDLE)
        //     rewrites the label; GetNodeText reads "Child2" back.
        //   * State: GetNodeState(root, TVIS_BOLD) fresh = 0; SetNodeState(root,
        //     TVIS_BOLD, TVIS_BOLD) -> readback 16; SetNodeState(root, TVIS_BOLD, 0)
        //     -> readback 0. net48 PROBED: TVM_GETITEMSTATE IGNORES the lParam mask
        //     and returns the FULL item state, so the wrapper applies the mask in
        //     managed code (the bold 16 / cleared 0 oracle).
        //   * Caret: SelectNode(child) -> TVM_SELECTITEM(TVGN_CARET) returns TRUE
        //     on the HIDDEN tree (probed); TVM_GETNEXTITEM(TVGN_CARET) re-fetches
        //     the selected node and its text reads "Child2".
        //   * Expand/Collapse: TVM_EXPAND(TVE_EXPAND) -> GetNodeState(root,
        //     TVIS_EXPANDED) = 32; TVM_EXPAND(TVE_COLLAPSE) -> 0 (the raw scalar
        //     reads 96/64 = EXPANDED|EXPANDEDONCE -- masked to the single bit).
        //   * Count stays 2; DestroyWindow frees the control.
        IntPtr hTree41 = CreateWindowExW(0, WC_TREEVIEW, "",
            WS_CHILD, 10, 10, 280, 160, hForm, new IntPtr(ID_TREE41), hModule, IntPtr.Zero);
        P41Tree = new WfTreeView();
        P41Tree.Handle = hTree41;
        P41Tree.Id = ID_TREE41;
        P41TreeCreated = hTree41 != IntPtr.Zero;
        Console.WriteLine("p41_tree_created=" + P41TreeCreated);
        Console.WriteLine("p41_id=" + P41Tree.Id);

        int p41c0 = P41Tree.Count;
        Console.WriteLine("p41_count0=" + p41c0);

        // Insert root + child; the node handles come ONLY from the direct
        // P/Invoke TVM_GETNEXTITEM channel (the full-width handle discipline --
        // the user-method IntPtr return degrades to int32 in the transpiler).
        P41Tree.InsertNode(IntPtr.Zero, "Root");
        IntPtr p41root = SendMessageW(P41Tree.Handle, TVM_GETNEXTITEM, new IntPtr(TVGN_ROOT), IntPtr.Zero);
        Console.WriteLine("p41_root_ok=" + (p41root != IntPtr.Zero));

        P41Tree.InsertNode(p41root, "Child");
        IntPtr p41child = SendMessageW(P41Tree.Handle, TVM_GETNEXTITEM, new IntPtr(TVGN_CHILD), p41root);
        Console.WriteLine("p41_child_ok=" + (p41child != IntPtr.Zero));

        int p41cnt = P41Tree.Count;
        Console.WriteLine("p41_count2=" + p41cnt);

        // TVM_GETITEMW text readback.
        Console.WriteLine("p41_root_text=" + P41Tree.GetNodeText(p41root));
        Console.WriteLine("p41_child_text=" + P41Tree.GetNodeText(p41child));

        // TVIF_PARAM readback (the corrected 0x0004 bit).
        Console.WriteLine("p41_root_param=" + P41Tree.GetNodeLParam(p41root));

        // TVM_SETITEMW text rewrite + readback.
        bool p41settext = P41Tree.SetNodeText(p41child, "Child2");
        Console.WriteLine("p41_settext_ok=" + p41settext);
        Console.WriteLine("p41_child_text2=" + P41Tree.GetNodeText(p41child));

        // TVM_GETITEMSTATE scalar state channel (masked in managed code).
        Console.WriteLine("p41_state_fresh=" + P41Tree.GetNodeState(p41root, TVIS_BOLD));
        bool p41setbold = P41Tree.SetNodeState(p41root, TVIS_BOLD, TVIS_BOLD);
        Console.WriteLine("p41_state_set_ok=" + p41setbold);
        Console.WriteLine("p41_state_bold=" + P41Tree.GetNodeState(p41root, TVIS_BOLD));
        bool p41clearbold = P41Tree.SetNodeState(p41root, TVIS_BOLD, 0);
        Console.WriteLine("p41_state_clear_ok=" + p41clearbold);
        Console.WriteLine("p41_state_cleared=" + P41Tree.GetNodeState(p41root, TVIS_BOLD));

        // TVM_SELECTITEM caret move + TVGN_CARET round-trip on the hidden tree.
        bool p41sel = P41Tree.SelectNode(p41child);
        Console.WriteLine("p41_select_ok=" + p41sel);
        IntPtr p41caret = SendMessageW(P41Tree.Handle, TVM_GETNEXTITEM, new IntPtr(TVGN_CARET), IntPtr.Zero);
        Console.WriteLine("p41_caret_ok=" + (p41caret != IntPtr.Zero));
        Console.WriteLine("p41_caret_text=" + P41Tree.GetNodeText(p41caret));

        // TVM_EXPAND expand/collapse state round-trip (masked readbacks).
        bool p41exp = P41Tree.Expand(p41root);
        Console.WriteLine("p41_expand_ok=" + p41exp);
        Console.WriteLine("p41_state_expanded=" + P41Tree.GetNodeState(p41root, TVIS_EXPANDED));
        IntPtr p41colret = SendMessageW(P41Tree.Handle, TVM_EXPAND, new IntPtr(TVE_COLLAPSE), p41root);
        Console.WriteLine("p41_collapse_ok=" + (p41colret.ToInt64() != 0));
        Console.WriteLine("p41_state_collapsed=" + P41Tree.GetNodeState(p41root, TVIS_EXPANDED));

        int p41cntA = P41Tree.Count;
        Console.WriteLine("p41_count_after=" + p41cntA);

        P41TreeDestroyed = DestroyWindow(hTree41) != 0;
        Console.WriteLine("p41_tree_destroyed=" + P41TreeDestroyed);

        // ===================== Phase 42: RichTextBox (RichEdit20W) line surface =====================
        // The RichTextBox LINE surface -- the Phase-40 EM_* line family re-verified
        // on a SECOND native control (the riched20 RichEdit20W via the Phase-28
        // LoadLibraryW channel -- zero new P/Invoke/shim/struct/constant beyond
        // the id) + the REAL EN_CHANGE the Phase-40 note could only fake.
        // Headless-safe and fully SYNCHRONOUS (net48-PROBED divergences are the
        // authoritative oracle -- the rich edit differs from the plain EDIT in
        // every line-family value):
        //   * CreateWindowExW(RichEdit20W, WS_CHILD | ES_MULTILINE, the Phase-28
        //     channel; the DLL is already loaded by the Phase-28 LoadLibraryW).
        //   * WM_SETTEXT("LineA\r\nLineB\r\nLineC") -> WM_GETTEXTLENGTH (19) ->
        //     WM_GETTEXT readback (19 chars, CRLF preserved: the rich edit stores
        //     CR-only internally but the text-access messages expand to CRLF --
        //     probed).
        //   * Line family (probed divergences vs the plain EDIT): EM_GETLINECOUNT
        //     = 3; EM_LINEINDEX(0/1/2) = 0/6/12 (the CR-only internal indexing,
        //     NOT the EDIT's 0/7/14); EM_LINELENGTH(0/7/14) = 5/5/5 (excludes the
        //     CR); EM_GETLINE copies the line PLUS its CR (copied 6 per line; the
        //     text lands at OFFSET 0 overwriting the length word -- the readback
        //     "LineA\r"/"LineB\r"/"LineC\r", not the EDIT's CR-less "LineA");
        //     EM_LINEFROMCHAR(0/5/7) = 0/0/1.
        //   * Selection: EM_SETSEL(0,-1) -> EM_GETSEL packs (0 | 18<<16): the end
        //     is the CR-terminated INTERNAL length 18, not the CRLF 19.
        //   * REAL EN_CHANGE: EM_SETEVENTMASK(ENM_CHANGE=1, the previous mask
        //     reads 0 -- the fresh RichEdit default, two-step-probed) + an
        //     injected WM_CHAR 'X' delivers a SYNCHRONOUS WM_COMMAND(0x290 |
        //     EN_CHANGE<<16) to this WndProc (probed: wmCommandCount +1, seq
        //     id656/n768) which routes into WfRichTextBox.RaiseTextChanged. The
        //     mask GENUINELY gates it -- ENM_NONE then a WM_CHAR notifies nothing
        //     (the probe negative proof that closes the Phase-40 EDIT note that
        //     the event could only be faked).
        //   * DestroyWindow frees the control.
        IntPtr hRich42 = CreateWindowExW(0, RICHEDIT_CLASS, "",
            WS_CHILD | ES_MULTILINE, 0, 0, 200, 100,
            hForm, new IntPtr(ID_RICH42), hModule, IntPtr.Zero);
        P42Rich = new WfRichTextBox();
        P42Rich.Handle = hRich42;
        P42Rich.Id = ID_RICH42;
        Console.WriteLine("p42_rich_created=" + (hRich42 != IntPtr.Zero));
        Console.WriteLine("p42_id=" + P42Rich.Id);

        // Multi-line text: WM_SETTEXT -> WM_GETTEXTLENGTH -> GetText readback.
        bool p42set = P42Rich.SetText("LineA\r\nLineB\r\nLineC");
        Console.WriteLine("p42_set_ok=" + p42set);
        Console.WriteLine("p42_len=" + P42Rich.GetTextLength());
        Console.WriteLine("p42_text=" + P42Rich.GetText());

        // Line family (the probed RichEdit semantics).
        Console.WriteLine("p42_linecount=" + P42Rich.GetLineCount());
        Console.WriteLine("p42_li0=" + P42Rich.LineIndex(0));
        Console.WriteLine("p42_li1=" + P42Rich.LineIndex(1));
        Console.WriteLine("p42_li2=" + P42Rich.LineIndex(2));
        Console.WriteLine("p42_ll0=" + P42Rich.LineLength(0));
        Console.WriteLine("p42_ll7=" + P42Rich.LineLength(7));
        Console.WriteLine("p42_ll14=" + P42Rich.LineLength(14));

        // Per-line readback via EM_GETLINE (length-prefix WORD + text at offset 0;
        // the rich edit copies the line PLUS its CR -- copied 6, readback "LineA\r").
        Console.WriteLine("p42_gl0_copied=" + P42Rich.GetLineCopied(0));
        Console.WriteLine("p42_gl0_text=" + P42Rich.GetLineText(0));
        Console.WriteLine("p42_gl1_copied=" + P42Rich.GetLineCopied(1));
        Console.WriteLine("p42_gl1_text=" + P42Rich.GetLineText(1));
        Console.WriteLine("p42_gl2_copied=" + P42Rich.GetLineCopied(2));
        Console.WriteLine("p42_gl2_text=" + P42Rich.GetLineText(2));

        // Reverse lookup: the line containing a char index.
        Console.WriteLine("p42_lfc0=" + P42Rich.LineFromChar(0));
        Console.WriteLine("p42_lfc5=" + P42Rich.LineFromChar(5));
        Console.WriteLine("p42_lfc7=" + P42Rich.LineFromChar(7));

        // Selection: select-all + the packed EM_GETSEL readback (end 18 = the
        // CR-terminated internal length, not the CRLF 19).
        P42Rich.SelectAll();
        Console.WriteLine("p42_sel_start=" + P42Rich.SelStart);
        Console.WriteLine("p42_sel_end=" + P42Rich.SelEnd);

        // REAL EN_CHANGE event: subscribe + collapse the selection to 0 + set
        // EM_SETEVENTMASK(ENM_CHANGE=1) (the previous mask 0 -- fresh RichEdit
        // default) + an injected WM_CHAR 'X' delivers the notification
        // SYNCHRONOUSLY into the WndProc route (the mask GENUINELY gates it --
        // unlike the plain EDIT of Phase 40). The drain processes any posted
        // residue deterministically.
        P42Rich.TextChanged += OnP42Change;
        Console.WriteLine("p42_change_subs=" + P42Rich.ChangeCount);
        P42Seq = "";
        P42ChangeFired = 0;
        SendMessageW(hRich42, EM_SETSEL, new IntPtr(0), new IntPtr(0));
        IntPtr p42maskPrev = SendMessageW(hRich42, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(1));
        Console.WriteLine("p42_mask_prev=" + p42maskPrev.ToInt64());
        SendMessageW(hRich42, WM_CHAR, new IntPtr((int)'X'), new IntPtr(1));
        MSG msg42;
        int d42 = 0;
        while (PeekMessageW(out msg42, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg42);
            DispatchMessageW(ref msg42);
            d42++;
            if (d42 >= 200) break;
        }
        Console.WriteLine("p42_change_fired=" + P42ChangeFired);
        Console.WriteLine("p42_change_seq=" + P42Seq);

        P42Destroyed = DestroyWindow(hRich42) != 0;
        Console.WriteLine("p42_destroyed=" + P42Destroyed);

        // ===================== Phase 43: ListView column order (DisplayIndex) =====================
        // The ListView column DISPLAY order -- a THIRD SysListView32 (the
        // Phase-21/33 channel) exercising the columns' display-order exchange:
        // LVM_SETCOLUMNORDERARRAY / LVM_GETCOLUMNORDERARRAY (wParam = 3, lParam
        // = a bare GCHandle-PINNED int[3] -- the Phase-40 "bare int[] as
        // lParam" shape, the EM_GETLINE channel; the M3 SZArray restriction
        // forbids an int[] RETURN, so the readback joins into a STRING).
        // Headless-safe and fully SYNCHRONOUS (net48-PROBED -- the authoritative
        // oracle):
        //   * CreateWindowExW(SysListView32, WS_CHILD | LVS_REPORT) +
        //     AddColumn x3 (Name/Value/Kind, the Phase-33 channel) -> ColumnCount
        //     = 3 (LVM_GETHEADER -> HDM_GETITEMCOUNT).
        //   * LVM_GETCOLUMNORDERARRAY fresh readback = {0,1,2} -> "0;1;2;".
        //   * LVM_SETCOLUMNORDERARRAY(2,0,1) returns BOOL; readback {2,0,1} ->
        //     "2;0;1;" (order[i] = the COLUMN shown at position i -- probed:
        //     the "Kind" column, index 2, is physically LEFTMOST).
        //   * DISPLAY-only: the column indices never change, so
        //     LVM_GETCOLUMNW(0) still reads "Name" (the header text channel is
        //     untouched by the reorder).
        //   * DisplayIndex(col): the reverse lookup -- position i where
        //     order[i] == col (probed 0/1/2 -> 1/2/0 after {2,0,1}) -- the
        //     WinForms Columns.DisplayIndex backing.
        //   * LVM_SETCOLUMNORDERARRAY(0,1,2) resets; readback {0,1,2}.
        //   * DestroyWindow frees the control.
        IntPtr hList43 = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD | LVS_REPORT, 0, 0, 320, 120,
            hForm, new IntPtr(ID_LIST43), hModule, IntPtr.Zero);
        P43List = new WfDataGridView();
        P43List.Handle = hList43;
        P43List.Id = ID_LIST43;
        P43ListCreated = hList43 != IntPtr.Zero;
        Console.WriteLine("p43_list_created=" + P43ListCreated);
        Console.WriteLine("p43_id=" + P43List.Id);

        int p43c0 = P43List.AddColumn("Name", LVCFMT_LEFT, 90);
        int p43c1 = P43List.AddColumn("Value", LVCFMT_CENTER, 70);
        int p43c2 = P43List.AddColumn("Kind", LVCFMT_RIGHT, 70);
        Console.WriteLine("p43_col0_add=" + p43c0);
        Console.WriteLine("p43_col1_add=" + p43c1);
        Console.WriteLine("p43_col2_add=" + p43c2);
        Console.WriteLine("p43_colcount=" + P43List.ColumnCount);

        Console.WriteLine("p43_fresh=" + P43List.GetColumnOrder());

        bool p43set1 = P43List.SetColumnOrder(2, 0, 1);
        Console.WriteLine("p43_set_ok=" + p43set1);
        Console.WriteLine("p43_after=" + P43List.GetColumnOrder());

        Console.WriteLine("p43_col0_text=" + P43List.GetColumnText(0));

        Console.WriteLine("p43_disp0=" + P43List.DisplayIndex(0));
        Console.WriteLine("p43_disp1=" + P43List.DisplayIndex(1));
        Console.WriteLine("p43_disp2=" + P43List.DisplayIndex(2));

        bool p43reset = P43List.SetColumnOrder(0, 1, 2);
        Console.WriteLine("p43_reset_ok=" + p43reset);
        Console.WriteLine("p43_reset=" + P43List.GetColumnOrder());

        P43ListDestroyed = DestroyWindow(hList43) != 0;
        Console.WriteLine("p43_list_destroyed=" + P43ListDestroyed);

        // ===================== Phase 44: window layout surface (WM_GETMINMAXINFO) =====================
        // The window layout surface -- the sizing-constraint channel
        // DefWindowProcW applies to a TOP-LEVEL window. WM_GETMINMAXINFO
        // (0x24) is sent by the system BEFORE a window is sized (create /
        // show / move) with lParam = a MINMAXINFO* -- 5 POINTs, 40 bytes on
        // x64, carried as a GCHandle-PINNED int[10] ([0..1] ptReserved, [2..3]
        // ptMaxSize, [4..5] ptMaxPosition, [6..7] ptMinTrackSize, [8..9]
        // ptMaxTrackSize). Headless acceptance (net48-PROBED -- the
        // authoritative oracle):
        //   * The WndProc branch is armed by the P44Active GUARD. With the
        //     guard OFF (the default), the NATURAL OS-driven WM_GETMINMAXINFO
        //     (CreateWindowExW / ShowWindow / MoveWindow) arrives with lParam
        //     pointing at the OS's INTERNAL buffer -- the guard routes those to
        //     DefWindowProcW (an unguarded Marshal.Copy would corrupt the
        //     window's sizing state; the window must survive the create/move,
        //     probed). The DefWindowProc-filled fields are resolution-dependent
        //     and NEVER asserted.
        //   * Loopback: set the self-set track sizes, pin a caller int[10],
        //     arm the guard, SendMessageW WM_GETMINMAXINFO to the form -- the
        //     WndProc Marshal.Copies P44Write into the caller's buffer (ONLY
        //     [6..9] are self-set; [0..5] stay 0 and are NEVER output) and
        //     returns zero -- the Form.MinimumSize / Form.MaximumSize native
        //     backing. P44Track = exactly 1.
        //   * Relative-layout reflow (Phase-5 delta discipline): GetWindowRect
        //     the form, MoveWindow it to 400x280 (at its CURRENT origin, so the
        //     form delta is deterministic 0), re-MoveWindow the EXISTING
        //     Button/Label/Edit to new relative offsets, GetWindowRect each
        //     control -- sizes are exact, positions are printed as DELTAS (the
        //     screen origin differs by window station, but the parent does not
        //     move between the two measurements).
        P44Write[6] = 200;
        P44Write[7] = 150;
        P44Write[8] = 640;
        P44Write[9] = 480;

        int[] p44buf = new int[10];
        GCHandle p44pin = GCHandle.Alloc(p44buf, GCHandleType.Pinned);
        P44Active = true;
        IntPtr p44ret = SendMessageW(hForm, WM_GETMINMAXINFO, IntPtr.Zero, p44pin.AddrOfPinnedObject());
        P44Active = false;
        Console.WriteLine("p44_min_w=" + p44buf[6]);
        Console.WriteLine("p44_min_h=" + p44buf[7]);
        Console.WriteLine("p44_max_w=" + p44buf[8]);
        Console.WriteLine("p44_max_h=" + p44buf[9]);
        Console.WriteLine("p44_loop_ret0=" + (p44ret == IntPtr.Zero));
        Console.WriteLine("p44_track=" + P44Track);
        p44pin.Free();

        RECT rF44 = new RECT();
        GetWindowRect(hForm, out rF44);
        int mvF44 = MoveWindow(hForm, rF44.left, rF44.top, 400, 280, true);
        RECT rF44b = new RECT();
        GetWindowRect(hForm, out rF44b);
        Console.WriteLine("p44_form_dx=" + (rF44b.left - rF44.left));
        Console.WriteLine("p44_form_dy=" + (rF44b.top - rF44.top));
        Console.WriteLine("p44_form_size=" + (rF44b.right - rF44b.left) + "x" + (rF44b.bottom - rF44b.top));

        RECT rB44 = new RECT();
        GetWindowRect(hButton, out rB44);
        int mvB44 = MoveWindow(hButton, 40, 60, 180, 48, true);
        RECT rB44b = new RECT();
        GetWindowRect(hButton, out rB44b);
        Console.WriteLine("p44_btn_dx=" + (rB44b.left - rB44.left));
        Console.WriteLine("p44_btn_dy=" + (rB44b.top - rB44.top));
        Console.WriteLine("p44_btn_size=" + (rB44b.right - rB44b.left) + "x" + (rB44b.bottom - rB44b.top));

        RECT rL44 = new RECT();
        GetWindowRect(hLabel, out rL44);
        int mvL44 = MoveWindow(hLabel, 40, 120, 240, 24, true);
        RECT rL44b = new RECT();
        GetWindowRect(hLabel, out rL44b);
        Console.WriteLine("p44_label_dx=" + (rL44b.left - rL44.left));
        Console.WriteLine("p44_label_dy=" + (rL44b.top - rL44.top));
        Console.WriteLine("p44_label_size=" + (rL44b.right - rL44b.left) + "x" + (rL44b.bottom - rL44b.top));

        RECT rE44 = new RECT();
        GetWindowRect(hEdit, out rE44);
        int mvE44 = MoveWindow(hEdit, 40, 160, 220, 28, true);
        RECT rE44b = new RECT();
        GetWindowRect(hEdit, out rE44b);
        Console.WriteLine("p44_edit_dx=" + (rE44b.left - rE44.left));
        Console.WriteLine("p44_edit_dy=" + (rE44b.top - rE44.top));
        Console.WriteLine("p44_edit_size=" + (rE44b.right - rE44b.left) + "x" + (rE44b.bottom - rE44b.top));

        // ===================== Phase 45: TabControl layout (item size + rows) =====================
        // The TabControl layout surface -- the FIXED item size and the row
        // wrap, on a SECOND TCS_FIXEDWIDTH SysTabControl32 (the Phase-35
        // channel) + a THIRD TCS_MULTILINE one. Headless-safe and fully
        // SYNCHRONOUS (net48-PROBED -- the authoritative oracle):
        //   * Item size: TCM_SETITEMSIZE (lParam = cx | (cy << 16)) sets the
        //     fixed tab size. The RETURN value is the PREVIOUS item size
        //     (LOWORD = previous width, HIWORD = previous height) -- the
        //     two-step readback: the FIRST call's return is the font-metric
        //     default (96x0 probed, NOT byte-exact -- discarded); the SECOND
        //     call returns the FIRST explicit value (60/24); the THIRD returns
        //     the second (80/28). Only TCS_FIXEDWIDTH / TCS_OWNERDRAWFIXED
        //     controls honor the size (probed: on a plain control the item
        //     rect is UNCHANGED -- the size is ignored).
        //   * Item rect: TCM_GETITEMRECT(0) writes a pinned int[4] (L/T/R/B)
        //     -- BOOLEAN form (non-empty left<right / top<bottom; the raw
        //     pixels depend on the control's font metrics, so only the
        //     structural booleans are byte-exact).
        //   * Rows: TCM_GETROWCOUNT = 1 for the fixed-width single-line
        //     control; the TCS_MULTILINE | TCS_FIXEDWIDTH narrow control
        //     (~110px client) wraps 3 fixed 50px tabs onto 2 rows (probed:
        //     105..154px client = 2 rows, 100px = 3 rows -- the ~5px comctl32
        //     margin; 110px is the deterministic 2-row point).
        IntPtr hTab45 = CreateWindowExW(0, WC_TABCONTROL, "",
            WS_CHILD | TCS_FIXEDWIDTH, 10, 10, 320, 120,
            hForm, new IntPtr(ID_TAB45), hModule, IntPtr.Zero);
        P45TabCreated = hTab45 != IntPtr.Zero;
        Console.WriteLine("p45_tab_created=" + P45TabCreated);
        P45Tab = new WfTabControl();
        P45Tab.Handle = hTab45;
        P45Tab.Id = ID_TAB45;
        Console.WriteLine("p45_id=" + P45Tab.Id);

        int p45i0 = P45Tab.InsertTabText(0, "Tab0");
        int p45i1 = P45Tab.InsertTabText(1, "Tab1");
        int p45i2 = P45Tab.InsertTabText(2, "Tab2");
        Console.WriteLine("p45_insert_ret=" + p45i0 + "/" + p45i1 + "/" + p45i2);
        Console.WriteLine("p45_count=" + P45Tab.Count);

        // Two-step item-size readback (discard -> explicit -> explicit).
        P45Tab.SetItemSize(60, 24);
        int p45s2 = P45Tab.SetItemSize(80, 28);
        Console.WriteLine("p45_prev_w=" + (p45s2 & 0xFFFF));
        Console.WriteLine("p45_prev_h=" + ((p45s2 >> 16) & 0xFFFF));
        int p45s3 = P45Tab.SetItemSize(50, 20);
        Console.WriteLine("p45_prev2_w=" + (p45s3 & 0xFFFF));
        Console.WriteLine("p45_prev2_h=" + ((p45s3 >> 16) & 0xFFFF));

        // Item rect, BOOLEAN form.
        P45Tab.ReadTabRect(0);
        bool p45RectLr = P45Tab.RectLeft < P45Tab.RectRight;
        bool p45RectTb = P45Tab.RectTop < P45Tab.RectBottom;
        bool p45RectPos = P45Tab.RectLeft >= 0 && P45Tab.RectTop >= 0;
        Console.WriteLine("p45_rect_lr=" + p45RectLr);
        Console.WriteLine("p45_rect_nonempty=" + (p45RectLr && p45RectTb));
        Console.WriteLine("p45_rect_pos=" + p45RectPos);
        Console.WriteLine("p45_rows1=" + P45Tab.RowCount);

        // Second control: TCS_MULTILINE | TCS_FIXEDWIDTH, ~110px narrow.
        IntPtr hTab45ml = CreateWindowExW(0, WC_TABCONTROL, "",
            WS_CHILD | TCS_MULTILINE | TCS_FIXEDWIDTH, 10, 10, 110, 120,
            hForm, new IntPtr(ID_TAB45 + 1), hModule, IntPtr.Zero);
        P45MlCreated = hTab45ml != IntPtr.Zero;
        Console.WriteLine("p45_ml_created=" + P45MlCreated);
        P45MlTab = new WfTabControl();
        P45MlTab.Handle = hTab45ml;
        P45MlTab.Id = ID_TAB45 + 1;
        P45MlTab.InsertTabText(0, "Tab0");
        P45MlTab.InsertTabText(1, "Tab1");
        P45MlTab.InsertTabText(2, "Tab2");
        P45MlTab.SetItemSize(50, 20);
        Console.WriteLine("p45_ml_rows=" + P45MlTab.RowCount);

        Console.WriteLine("p45_tab_destroyed=" + (DestroyWindow(hTab45) != 0));
        Console.WriteLine("p45_ml_destroyed=" + (DestroyWindow(hTab45ml) != 0));

        // ===================== Phase 46: OLE drag-drop surface (ole32) =====================
        // The ole32 P/Invoke channel pinned in the winforms1 e2e (the full
        // managed dispatch bridge -- native IDropTarget -> DragEventArgs ->
        // managed OnDragEnter -- is COM stage 17, test_com section 22). The
        // net48 CLR main thread runs COM as MTA by default; the transpiled
        // C++ main thread starts with NO COM state -- CoInitializeEx with
        // COINIT_MULTITHREADED (0) aligns the two sides, so OleInitialize
        // returns RPC_E_CHANGED_MODE (0x80010106) on BOTH (the MTA thread
        // cannot be re-initialized as STA) -- probed deterministic.
        // RegisterDragDrop with a null/invalid hwnd returns E_INVALIDARG
        // (0x80070057); RevokeDragDrop on a window never registered returns
        // DRAGDROP_E_NOTREGISTERED (0x80040202). All HRESULTs are the probed
        // net48 ground truth.
        CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED: align with net48 MTA
        P46OleInitRet = OleInitialize(IntPtr.Zero);
        Console.WriteLine("p46_ole_init=" + P46OleInitRet);
        P46RegNullRet = RegisterDragDrop(IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p46_reg_null=" + P46RegNullRet);
        P46RegBadRet = RegisterDragDrop(new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("p46_reg_bad=" + P46RegBadRet);
        P46RevokeNullRet = RevokeDragDrop(IntPtr.Zero);
        Console.WriteLine("p46_revoke_null=" + P46RevokeNullRet);
        Console.WriteLine("p46_ole_changed_mode=" + (P46OleInitRet == OLE32_E_CHANGED_MODE));
        Console.WriteLine("p46_reg_invalidarg=" + (P46RegNullRet == OLE32_E_INVALIDARG && P46RegBadRet == OLE32_E_INVALIDARG));
        Console.WriteLine("p46_revoke_notregistered=" + (P46RevokeNullRet == OLE32_DRAGDROP_E_NOTREGISTERED));
        OleUninitialize();
        CoUninitialize();
        Console.WriteLine("p46_uninit_ok=True");

        // ===================== Phase 47: managed drag-drop event chain =====================
        // A pure-MANAGED drop-target event chain (the COM dispatch bridge is
        // COM stage 17, test_com section 22). A synthetic drop sequence is fed
        // through IDropTarget47 -- DragEnter(Effect:1, 120, 40) ->
        // DragOver(Effect:2, 125, 41) -> DragLeave(Effect:0, 0, 0) -> Drop(Effect:2,
        // 130, 42) -- and the WfDropSink47 counts + records every entry. The
        // EnterEffect mutation (effect+1) pins the "handled" path. Status-bar
        // mirror via the Phase-9 SB_SETTEXT channel. Purely managed: no COM
        // icall, so net48 and the transpiled build are byte-exact.
        WfDropSink47 p47Sink = new WfDropSink47();
        Console.WriteLine("p47_sink_created=" + (p47Sink != null));
        p47Sink.DragEnter(new DragEventArgs47(120, 40, 1));
        Console.WriteLine("p47_enter_count=" + p47Sink.EnterCount);
        Console.WriteLine("p47_enter_xy=" + p47Sink.LastX + "/" + p47Sink.LastY);
        Console.WriteLine("p47_enter_effect=" + p47Sink.LastEffect + " handled=" + p47Sink.EnterEffect);
        p47Sink.DragOver(new DragEventArgs47(125, 41, 2));
        Console.WriteLine("p47_over_count=" + p47Sink.OverCount);
        Console.WriteLine("p47_over_xy=" + p47Sink.LastX + "/" + p47Sink.LastY);
        p47Sink.DragLeave(new DragEventArgs47(0, 0, 0));
        Console.WriteLine("p47_leave_count=" + p47Sink.LeaveCount);
        p47Sink.Drop(new DragEventArgs47(130, 42, 2));
        Console.WriteLine("p47_drop_count=" + p47Sink.DropCount);
        Console.WriteLine("p47_final_xy=" + p47Sink.LastX + "/" + p47Sink.LastY);
        Console.WriteLine("p47_final_effect=" + p47Sink.LastEffect);
        Console.WriteLine("p47_total=" + (p47Sink.EnterCount + p47Sink.OverCount + p47Sink.LeaveCount + p47Sink.DropCount));

        // Stage 2: wire the Button Click event (WM_COMMAND -> delegate).
        ButtonClick = OnButtonClick;

        Console.WriteLine("wm_create_count=" + wmCreateCount);
        Console.WriteLine("wm_command_count=" + wmCommandCount);

        // Inject a click: WM_COMMAND to the Form with the button's id + hwnd.
        IntPtr zero = IntPtr.Zero;
        int ok = PostMessageW(hForm, WM_COMMAND, new IntPtr(1), hButton);
        Console.WriteLine("inject_command=" + (ok != 0));

        // Stage 2: inject a WM_PAINT with the marker (deterministic count).
        int okPaint = PostMessageW(hForm, WM_PAINT, new IntPtr(PAINT_MARKER), zero);
        Console.WriteLine("inject_paint=" + (okPaint != 0));

        // Close path: post WM_CLOSE; the WndProc destroys the window and the
        // next pump iteration's DispatchMessageW delivers WM_DESTROY; we post
        // WM_QUIT so the pump terminates deterministically.
        PostMessageW(hForm, WM_CLOSE, zero, zero);
        PostQuitMessage(0);

        // ===================== Phase 48: ListView column sort (LVM_SORTITEMS) =====================
        // The list-view sort channel: LVM_SORTITEMS (0x1030) is sent to the
        // LIST window with lParam = a PFNLVCOMPARE callback pointer
        // (int (lParam1, lParam2, lParamSort)) obtained from
        // Marshal.GetFunctionPointerForDelegate. comctl32 calls the callback
        // for each comparison during an insertion sort and moves the items so
        // that the lParam sequence becomes the callback's comparison order.
        // The WfSortableGrid rows carry their LOGICAL index in the LPARAM
        // (LVM_INSERTITEMW with LVIF_PARAM, lParam = i) while the TEXTS are
        // inserted out of order ("row2/row0/row1" at logical 0/1/2); the
        // callback compares SortKeys[lParam], so sorting on the header must
        // reorder the rows into SortKeys-ascending lParam order [0,1,2].
        // Readback via LVM_GETITEMW (the lParam of each post-sort position).
        // The callback also records every invocation (probed: quicksort on 3
        // items fires several calls). net48 = GetFunctionPointerForDelegate;
        // the transpiled CLR = the same call -- both mean a native callback
        // funptr, so the two compilers are byte-exact.
        // NOTE: created BEFORE the WM_CLOSE (the form must still be alive to
        // parent the list control) and destroyed immediately afterwards.
        WfSortableGrid p48Sort = new WfSortableGrid();
        IntPtr hList48 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)ID_SORT48, hModule, IntPtr.Zero);
        p48Sort.Handle = hList48;
        p48Sort.AddColumn("Name", LVCFMT_LEFT, 90, 0);
        p48Sort.AddColumn("Value", LVCFMT_CENTER, 70, 1);
        p48Sort.AddColumn("Kind", LVCFMT_RIGHT, 70, 2);
        p48Sort.Insert("row2", 2);
        p48Sort.Insert("row0", 0);
        p48Sort.Insert("row1", 1);
        Console.WriteLine("p48_rows_before=" + p48Sort.Count);
        Console.WriteLine("p48_lp_before=" + p48Sort.GetRowLParam(0) + "/" + p48Sort.GetRowLParam(1) + "/" + p48Sort.GetRowLParam(2));
        bool p48sorted = p48Sort.Sort(0);
        Console.WriteLine("p48_sort_ok=" + p48sorted);
        Console.WriteLine("p48_callbacks=" + WfSortableGrid.SortCallbacks);
        Console.WriteLine("p48_lp_after=" + p48Sort.GetRowLParam(0) + "/" + p48Sort.GetRowLParam(1) + "/" + p48Sort.GetRowLParam(2));
        Console.WriteLine("p48_rows_after=" + p48Sort.Count);
        Console.WriteLine("p48_t0=" + p48Sort.GetItemText(0));
        Console.WriteLine("p48_t1=" + p48Sort.GetItemText(1));
        Console.WriteLine("p48_t2=" + p48Sort.GetItemText(2));
        bool p48destroyed = DestroyWindow(hList48) != 0;
        Console.WriteLine("p48_destroyed=" + p48destroyed);

        // ===================== Phase 49: ListView find item + auto column width =====================
        // LVM_FINDITEMW (0x1033) searches the list for an item whose text
        // matches the LVFINDINFOW.pszText STRING (LVFI_STRING -- probed on the
        // net48 oracle it is an EXACT match, not a prefix; iStart = -1 wraps
        // to the first item). Auto-width: LVM_SETCOLUMNWIDTH with the
        // LVSCW_AUTOSIZE (-1) constant makes comctl32 size the column to its
        // widest cell string -- probed: 60 -> 53 (0 < auto <= previous is the
        // acceptance: the auto width is a real, positive, resized value).
        // Both over the existing pinned-int surface.
        WfFindGrid p49Find = new WfFindGrid();
        IntPtr hList49 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)ID_FIND49, hModule, IntPtr.Zero);
        p49Find.Handle = hList49;
        p49Find.AddColumn("Name", LVCFMT_LEFT, 60, 0);
        p49Find.Insert("Alpha");
        p49Find.Insert("Beta");
        p49Find.Insert("Gamma");
        Console.WriteLine("p49_count=" + p49Find.Count);
        Console.WriteLine("p49_find_alpha=" + p49Find.Find("Alpha"));
        Console.WriteLine("p49_find_beta=" + p49Find.Find("Beta"));
        Console.WriteLine("p49_find_prefix=" + p49Find.Find("Be"));
        Console.WriteLine("p49_find_missing=" + p49Find.Find("Zed"));
        int p49w0 = p49Find.GetColumnWidth(0);
        p49Find.AutoSizeColumn(0);
        int p49w0auto = p49Find.GetColumnWidth(0);
        Console.WriteLine("p49_w0=" + p49w0);
        Console.WriteLine("p49_w0_auto=" + p49w0auto);
        bool p49autoresized = p49w0auto > 0 && p49w0auto <= p49w0;
        Console.WriteLine("p49_autosize_valid=" + p49autoresized);
        bool p49destroyed = DestroyWindow(hList49) != 0;
        Console.WriteLine("p49_destroyed=" + p49destroyed);

        // ===================== Phase 50: ListView image list (ImageList API) =====================
        // The comctl32 HIMAGELIST surface: ImageList_Create makes a
        // 16x16 COLOR32 small-icon list, ImageList_Add appends the Phase-23
        // OBM_CLOSE stock bitmap (mask NULL) and returns index 0,
        // ImageList_Destroy frees it. LVM_SETIMAGELIST (0x1003) arms the
        // list's small-icon list and returns the PREVIOUS one (NULL fresh),
        // LVM_GETIMAGELIST (0x1002) reads it back. Headless: all envelope
        // values (handle non-null, add index 0, set/get round-trip, destroy
        // ok) -- no drawing occurs, so net48 and the transpiled build are
        // byte-exact.
        IntPtr hList50 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)ID_FIND49, hModule, IntPtr.Zero);
        IntPtr p50his = Cases.Winforms1.ImageList_Create(16, 16, ILC_COLOR32, 1, 1);
        Console.WriteLine("p50_himl_nonzero=" + (p50his != IntPtr.Zero));
        int p50add = Cases.Winforms1.ImageList_Add(p50his, p23bmp, IntPtr.Zero);
        Console.WriteLine("p50_bmp_add=" + p50add);
        IntPtr p50prev = Cases.Winforms1.SendMessageW(hList50, LVM_SETIMAGELIST, new IntPtr(LVSIL_SMALL), p50his);
        Console.WriteLine("p50_set_prev_null=" + (p50prev == IntPtr.Zero));
        IntPtr p50get = Cases.Winforms1.SendMessageW(hList50, LVM_GETIMAGELIST, new IntPtr(LVSIL_SMALL), IntPtr.Zero);
        Console.WriteLine("p50_get_matches=" + (p50get == p50his));
        int p50ok = Cases.Winforms1.ImageList_Destroy(p50his);
        Console.WriteLine("p50_destroy_ok=" + (p50ok != 0));
        bool p50destroyed = DestroyWindow(hList50) != 0;
        Console.WriteLine("p50_destroyed=" + p50destroyed);

        // ===================== Phase 51: ComboBox dropdown state =====================
        // CB_SHOWDROPDOWN (0x14F) opens/closes the combo's list; the headless
        // acceptance reads CB_GETDROPPEDSTATE (0x157, BOOL) and
        // CB_GETDROPPEDCONTROLRECT (0x152, a RECT written through a pinned
        // int[4] -- the priority-ordered probe shows whether expanding a
        // dropped user-rect is observable without a caret/input loop; probed
        // on the net48 oracle the rect is populated whenever the dropdown is
        // armed, even with no UI thread drawing it).
        IntPtr hCombo51 = CreateWindowExW(0, "COMBOBOX", "", WS_CHILD | WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST,
            0, 0, 160, 200, hForm, (IntPtr)(ID_FIND49 + 3), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hCombo51, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("One"));
        Cases.Winforms1.SendMessageW(hCombo51, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Two"));
        Cases.Winforms1.SendMessageW(hCombo51, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Three"));
        int p51count = (int)Cases.Winforms1.SendMessageW(hCombo51, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p51_count=" + p51count);
        int p51state0 = (int)Cases.Winforms1.SendMessageW(hCombo51, CB_GETDROPPEDSTATE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p51_state_closed=" + (p51state0 == 0));
        bool p51show = Cases.Winforms1.SendMessageW(hCombo51, CB_SHOWDROPDOWN, new IntPtr(1), IntPtr.Zero).ToInt64() != 0;
        Console.WriteLine("p51_show_ok=" + p51show);
        int p51state1 = (int)Cases.Winforms1.SendMessageW(hCombo51, CB_GETDROPPEDSTATE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p51_state_open=" + (p51state1 != 0));
        int[] p51rect = new int[4];
        GCHandle p51pin = GCHandle.Alloc(p51rect, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hCombo51, CB_GETDROPPEDCONTROLRECT, IntPtr.Zero, p51pin.AddrOfPinnedObject());
        p51pin.Free();
        bool p51rect_nonempty = p51rect[2] > p51rect[0] && p51rect[3] > p51rect[1];
        Console.WriteLine("p51_rect_nonempty=" + p51rect_nonempty);
        bool p51hide = Cases.Winforms1.SendMessageW(hCombo51, CB_SHOWDROPDOWN, IntPtr.Zero, IntPtr.Zero).ToInt64() != 0;
        Console.WriteLine("p51_hide_ok=" + p51hide);
        bool p51destroyed = DestroyWindow(hCombo51) != 0;
        Console.WriteLine("p51_destroyed=" + p51destroyed);

        // ===================== Phase 52: ListView colors + per-page count =====================
        // Scalar color channels: LVM_SETBKCOLOR/LVM_GETBKCOLOR (the list
        // background CRGB) and LVM_SETTEXTBKCOLOR/LVM_GETTEXTBKCOLOR (the cell
        // text background) round-trip a COLORREF (wParam on the setter, the
        // return on the getter). The setter returns the PREVIOUS color (1 =
        // CLR_NONE, the .NET default, NOT 0 -- probed). PROBED: in report
        // view the TEXTBKCOLOR reads back 0 (the text-background color is a
        // icon/small-view concept the report-grid does not store) -- the
        // tbk_prev is still the previous color (1), the readback is an
        // observed value, not a round-trip. LVM_GETCOUNTPERPAGE is the number
        // of whole items visible in the current viewport height -- with the
        // 300x160 window it is a real positive computed value (7, probed).
        // All scalar envelope values, so net48 and the transpiled build are
        // byte-exact.
        IntPtr hList52 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 4), hModule, IntPtr.Zero);
        IntPtr p52prevBk = Cases.Winforms1.SendMessageW(hList52, LVM_SETBKCOLOR, new IntPtr(0x00FFFFFF), IntPtr.Zero);
        Console.WriteLine("p52_bk_prev=" + (int)p52prevBk.ToInt64());
        int p52bk = (int)Cases.Winforms1.SendMessageW(hList52, LVM_GETBKCOLOR, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p52_bk=" + p52bk);
        Console.WriteLine("p52_bk_matches=" + (p52bk == 0x00FFFFFF));
        IntPtr p52prevTb = Cases.Winforms1.SendMessageW(hList52, LVM_SETTEXTBKCOLOR, new IntPtr(0x00FFFF00), IntPtr.Zero);
        Console.WriteLine("p52_tbk_prev=" + (int)p52prevTb.ToInt64());
        int p52tbk = (int)Cases.Winforms1.SendMessageW(hList52, LVM_GETTEXTBKCOLOR, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p52_tbk=" + p52tbk);
        int p52perpage = (int)Cases.Winforms1.SendMessageW(hList52, LVM_GETCOUNTPERPAGE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p52_perpage=" + p52perpage);
        Console.WriteLine("p52_perpage_positive=" + (p52perpage > 0));
        bool p52destroyed = DestroyWindow(hList52) != 0;
        Console.WriteLine("p52_destroyed=" + p52destroyed);

        // ===================== Phase 53: ListView item rect + ensure visible =====================
        // LVM_GETITEMRECT (0x100E) writes the item's bounding RECT into a
        // caller RECT* (pinned int[4]); with 3 rows at the report grid the
        // rects are stacked top-to-bottom with a positive height (the values
        // are position-dependent, so only non-empty + ordering are asserted).
        // LVM_ENSUREVISIBLE (0x1013) scrolls the item into view and returns
        // nonzero when it becomes visible. Both are the geometry/visibility
        // surface of the list.
        IntPtr hList53 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 5), hModule, IntPtr.Zero);
        IntPtr p53colText = Marshal.StringToHGlobalUni("C");
        int[] p53col = new int[13];
        p53col[0] = LVCF_TEXT; p53col[6] = (int)p53colText.ToInt64();
        GCHandle p53cpin = GCHandle.Alloc(p53col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList53, LVM_INSERTCOLUMNW, IntPtr.Zero, p53cpin.AddrOfPinnedObject());
        p53cpin.Free();
        Marshal.FreeHGlobal(p53colText);
        Cases.Winforms1.SendMessageW(hList53, LVM_SETCOLUMNWIDTH, new IntPtr(0), new IntPtr(80));
        string[] p53texts = { "Alpha", "Beta", "Gamma" };
        for (int i = 0; i < 3; i++)
        {
            IntPtr t = Marshal.StringToHGlobalUni(p53texts[i]);
            int[] lvi = new int[22];
            lvi[0] = LVIF_TEXT; lvi[1] = i;
            long pt = t.ToInt64();
            lvi[6] = (int)(pt & 0xFFFFFFFFL); lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
            lvi[8] = 128;
            GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList53, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
            pin.Free();
            Marshal.FreeHGlobal(t);
        }
        int p53count = (int)Cases.Winforms1.SendMessageW(hList53, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p53_count=" + p53count);
        int[] p53r0 = new int[4];
        int[] p53r1 = new int[4];
        GCHandle p53r0pin = GCHandle.Alloc(p53r0, GCHandleType.Pinned);
        GCHandle p53r1pin = GCHandle.Alloc(p53r1, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList53, LVM_GETITEMRECT, new IntPtr(0), p53r0pin.AddrOfPinnedObject());
        Cases.Winforms1.SendMessageW(hList53, LVM_GETITEMRECT, new IntPtr(1), p53r1pin.AddrOfPinnedObject());
        p53r0pin.Free(); p53r1pin.Free();
        bool p53r0_nonempty = p53r0[2] > p53r0[0] && p53r0[3] > p53r0[1];
        bool p53r1_below = p53r1[1] >= p53r0[3];
        Console.WriteLine("p53_r0_nonempty=" + p53r0_nonempty);
        Console.WriteLine("p53_r1_below=" + p53r1_below);
        bool p53r0_h = (p53r0[3] - p53r0[1]) > 0;
        Console.WriteLine("p53_row_h_positive=" + p53r0_h);
        bool p53ens = Cases.Winforms1.SendMessageW(hList53, LVM_ENSUREVISIBLE, new IntPtr(2), IntPtr.Zero).ToInt64() != 0;
        Console.WriteLine("p53_ensure_ok=" + p53ens);
        bool p53destroyed = DestroyWindow(hList53) != 0;
        Console.WriteLine("p53_destroyed=" + p53destroyed);

        // ===================== Phase 54: ListBox caret + item rect =====================
        // LB_GETCARETINDEX (0x192) reads the index of the item with the
        // focus rectangle (the caret); LB_SETCARETINDEX (0x193) moves it.
        // PROBED headless: with no keyboard focus the caret stays 0 after
        // LB_SETCARETINDEX(2) (the caret is a focus-driven state -- readback
        // stays 0; the setter is still accepted). LB_GETITEMRECT (0x198)
        // writes the item's client rect through a pinned int[4] (non-empty,
        // positions asserted relative only). The caret + geometry are the
        // keyboard-navigation surface of the list box.
        IntPtr hList54 = CreateWindowExW(0, "LISTBOX", "", WS_CHILD | WS_VISIBLE,
            0, 0, 160, 120, hForm, (IntPtr)(ID_FIND49 + 7), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hList54, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Alpha"));
        Cases.Winforms1.SendMessageW(hList54, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Beta"));
        Cases.Winforms1.SendMessageW(hList54, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Gamma"));
        int p54caret0 = (int)Cases.Winforms1.SendMessageW(hList54, LB_GETCARETINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p54_caret0=" + p54caret0);
        Cases.Winforms1.SendMessageW(hList54, LB_SETCARETINDEX, new IntPtr(2), IntPtr.Zero);
        int p54caret1 = (int)Cases.Winforms1.SendMessageW(hList54, LB_GETCARETINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p54_caret_after_set=" + p54caret1);
        int[] p54r = new int[4];
        GCHandle p54pin = GCHandle.Alloc(p54r, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList54, LB_GETITEMRECT, new IntPtr(0), p54pin.AddrOfPinnedObject());
        p54pin.Free();
        bool p54r_nonempty = p54r[2] > p54r[0] && p54r[3] > p54r[1];
        Console.WriteLine("p54_rect_nonempty=" + p54r_nonempty);
        bool p54destroyed = DestroyWindow(hList54) != 0;
        Console.WriteLine("p54_destroyed=" + p54destroyed);

        // ===================== Phase 55: ListBox multi-select range =====================
        // LB_SELITEMRANGEEX (0x183) selects a contiguous range on an
        // LBS_EXTENDEDSEL list (wParam = TRUE(1) select, lParam =
        // MAKELPARAM(start,end)); LB_GETSELCOUNT (0x190) counts the selected
        // items; LB_GETSELITEMS (0x191) fills a caller int[] with the
        // selected indices in ascending order. (PROBED: the plain LB_SELITEMRANGE
        // on LBS_MULTIPLESEL was a headless no-op; the RANGEEX return is 0 on
        // success -- not a BOOL -- and the selection takes effect: count 2,
        // indices {1,2}.)
        IntPtr hList55 = CreateWindowExW(0, "LISTBOX", "", WS_CHILD | WS_VISIBLE | LBS_EXTENDEDSEL,
            0, 0, 160, 120, hForm, (IntPtr)(ID_FIND49 + 8), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hList55, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("A"));
        Cases.Winforms1.SendMessageW(hList55, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("B"));
        Cases.Winforms1.SendMessageW(hList55, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("C"));
        int p55setret = (int)Cases.Winforms1.SendMessageW(hList55, LB_SELITEMRANGEEX, new IntPtr(1), new IntPtr((1 << 16) | 2)).ToInt64();
        Console.WriteLine("p55_rangeselect_ret=" + p55setret);
        int p55count = (int)Cases.Winforms1.SendMessageW(hList55, LB_GETSELCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p55_selcount=" + p55count);
        int[] p55items = new int[8];
        GCHandle p55pin = GCHandle.Alloc(p55items, GCHandleType.Pinned);
        int p55got = (int)Cases.Winforms1.SendMessageW(hList55, LB_GETSELITEMS, new IntPtr(8), p55pin.AddrOfPinnedObject()).ToInt64();
        p55pin.Free();
        Console.WriteLine("p55_got=" + p55got);
        Console.WriteLine("p55_selitems=" + p55items[0] + "/" + p55items[1]);
        bool p55destroyed = DestroyWindow(hList55) != 0;
        Console.WriteLine("p55_destroyed=" + p55destroyed);

        // ===================== Phase 56: RichTextBox character format =====================
        // EM_SETCHARFORMAT (0x444) applies a CHARFORMAT to the selection
        // (SCF_SELECTION); EM_GETCHARFORMAT (0x43A) reads it back. RichEdit20W
        // accepts the CHARFORMAT2W (116 bytes) -- the pinned int[29] layout
        // Phase-28 offsetof-probed: cbSize@0, dwMask@4, dwEffects@8,
        // crTextColor@20 (cf[5]). Set BOLD + red on the selected text
        // (dwMask = CFM_BOLD|CFM_COLOR, dwEffects = CFE_BOLD, crTextColor =
        // 0xFF0000; dwEffects = 0 would clear CFE_AUTOCOLOR, but we set the
        // bold bit directly), then read back dwEffects (CFE_BOLD) and
        // crTextColor.
        IntPtr hRich56 = CreateWindowExW(0, RICHEDIT_CLASS, "", WS_CHILD | WS_VISIBLE | ES_MULTILINE,
            0, 0, 260, 80, hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hRich56, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        Cases.Winforms1.SendMessageW(hRich56, WM_SETTEXT, IntPtr.Zero, Marshal.StringToHGlobalUni("Format me"));
        Cases.Winforms1.SendMessageW(hRich56, EM_SETSEL, IntPtr.Zero, new IntPtr(7));
        int[] p56cf = new int[29];
        p56cf[0] = 116;
        p56cf[1] = CFM_BOLD | CFM_COLOR;
        p56cf[2] = CFE_BOLD;
        p56cf[5] = 0x00FF0000;
        GCHandle p56pin = GCHandle.Alloc(p56cf, GCHandleType.Pinned);
        IntPtr p56set = Cases.Winforms1.SendMessageW(hRich56, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), p56pin.AddrOfPinnedObject());
        p56pin.Free();
        Console.WriteLine("p56_set_ok=" + (p56set.ToInt64() != 0));
        int[] p56cfr = new int[29];
        p56cfr[0] = 116;
        GCHandle p56pin2 = GCHandle.Alloc(p56cfr, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hRich56, EM_GETCHARFORMAT, new IntPtr(SCF_SELECTION), p56pin2.AddrOfPinnedObject());
        p56pin2.Free();
        bool p56bold = (p56cfr[2] & CFE_BOLD) != 0;
        Console.WriteLine("p56_bold=" + p56bold);
        int p56color = p56cfr[5];
        Console.WriteLine("p56_color=" + p56color);
        bool p56destroyed = DestroyWindow(hRich56) != 0;
        Console.WriteLine("p56_destroyed=" + p56destroyed);

        // ===================== Phase 57: StatusBar part rect =====================
        // SB_GETRECT (WM_USER+10 = 0x040A) writes the part's bounding RECT
        // into a caller RECT* (pinned int[4]). The Phase-9 status bar is
        // re-parted (SB_SETPARTS 0x404 with right-edges {150,300,-1}) and the
        // three part rects are read back -- part0 is non-empty and part1 is
        // to the RIGHT of part0 (relative order, position values
        // environment-dependent so asserted relative only).
        int[] p57parts = { 150, 300, -1 };
        GCHandle p57pin = GCHandle.Alloc(p57parts, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hStatus, SB_SETPARTS, new IntPtr(3), p57pin.AddrOfPinnedObject());
        p57pin.Free();
        int[] p57r0 = new int[4];
        int[] p57r1 = new int[4];
        GCHandle p57r0pin = GCHandle.Alloc(p57r0, GCHandleType.Pinned);
        GCHandle p57r1pin = GCHandle.Alloc(p57r1, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hStatus, SB_GETRECT, new IntPtr(0), p57r0pin.AddrOfPinnedObject());
        Cases.Winforms1.SendMessageW(hStatus, SB_GETRECT, new IntPtr(1), p57r1pin.AddrOfPinnedObject());
        p57r0pin.Free(); p57r1pin.Free();
        bool p57r0_nonempty = p57r0[2] > p57r0[0] && p57r0[3] > p57r0[1];
        Console.WriteLine("p57_r0_nonempty=" + p57r0_nonempty);
        bool p57r1_right = p57r1[0] >= p57r0[2];
        Console.WriteLine("p57_r1_right=" + p57r1_right);
        int p57width0 = p57r0[2] - p57r0[0];
        Console.WriteLine("p57_w0_positive=" + (p57width0 > 0));

        // ===================== Phase 58: ProgressBar state =====================
        // PBM_SETSTATE (0x410) sets the progress-bar state (1 = normal,
        // 2 = error, 3 = paused -- PBS_NORMAL/PBS_ERROR/PBS_PAUSED);
        // PBM_GETSTATE (0x411) reads it back. PROBED headless on the plain
        // (non-PBS_SMOOTH) progress bar: PBM_GETSTATE reads 0 fresh and the
        // PBM_SETSTATE writes are NOT persisted back (the state is a
        // visual-mode flag of the smooth bar -- the reads are observed
        // values, not a round-trip).
        IntPtr p58state0 = Cases.Winforms1.SendMessageW(hProgress, PBM_GETSTATE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p58_state0=" + (int)p58state0.ToInt64());
        Cases.Winforms1.SendMessageW(hProgress, PBM_SETSTATE, new IntPtr(2), IntPtr.Zero);
        IntPtr p58state1 = Cases.Winforms1.SendMessageW(hProgress, PBM_GETSTATE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p58_state_after_error=" + (int)p58state1.ToInt64());
        Cases.Winforms1.SendMessageW(hProgress, PBM_SETSTATE, new IntPtr(3), IntPtr.Zero);
        IntPtr p58state2 = Cases.Winforms1.SendMessageW(hProgress, PBM_GETSTATE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p58_state_after_paused=" + (int)p58state2.ToInt64());

        // ===================== Phase 59: ListView top index + scroll =====================
        // LVM_GETTOPINDEX (0x1027) reads the index of the first visible row.
        // A tall list (20 rows) in a short viewport shows the first rows;
        // LVM_ENSUREVISIBLE(19) scrolls the LAST row into view, which moves
        // the top index to a nonzero row (probed: 19 visible -> top 0, after
        // ensure -> top > 0). The scroll + top-index surface of the list.
        IntPtr hList59 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 12), hModule, IntPtr.Zero);
        IntPtr p59colText = Marshal.StringToHGlobalUni("C");
        int[] p59col = new int[13];
        p59col[0] = LVCF_TEXT; p59col[6] = (int)p59colText.ToInt64();
        GCHandle p59cpin = GCHandle.Alloc(p59col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList59, LVM_INSERTCOLUMNW, IntPtr.Zero, p59cpin.AddrOfPinnedObject());
        p59cpin.Free();
        Marshal.FreeHGlobal(p59colText);
        for (int i = 0; i < 20; i++)
        {
            IntPtr t = Marshal.StringToHGlobalUni("r" + i);
            int[] lvi = new int[22];
            lvi[0] = LVIF_TEXT; lvi[1] = i;
            long pt = t.ToInt64();
            lvi[6] = (int)(pt & 0xFFFFFFFFL); lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
            lvi[8] = 128;
            GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList59, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
            pin.Free();
            Marshal.FreeHGlobal(t);
        }
        int p59top0 = (int)Cases.Winforms1.SendMessageW(hList59, LVM_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p59_top0=" + p59top0);
        Cases.Winforms1.SendMessageW(hList59, LVM_ENSUREVISIBLE, new IntPtr(19), IntPtr.Zero);
        int p59top1 = (int)Cases.Winforms1.SendMessageW(hList59, LVM_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p59_top_scrolled=" + p59top1);
        Console.WriteLine("p59_scrolled_down=" + (p59top1 > p59top0));
        bool p59destroyed = DestroyWindow(hList59) != 0;
        Console.WriteLine("p59_destroyed=" + p59destroyed);

        // ===================== Phase 60: ListView text color =====================
        // LVM_SETTEXTCOLOR (0x1021) sets the text color (wParam = CRGB),
        // LVM_GETTEXTCOLOR (0x1022) reads it back. PROBED headless the text
        // color does NOT round-trip (setter returns prev 0, readback stays 0
        // -- like the Phase-52 text-BACKGROUND channel, the color is only
        // stored when items exist in the view; the reads are observed
        // values).
        IntPtr hList60 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 13), hModule, IntPtr.Zero);
        IntPtr p60prevTxt = Cases.Winforms1.SendMessageW(hList60, LVM_SETTEXTCOLOR, new IntPtr(0x00FF00), IntPtr.Zero);
        Console.WriteLine("p60_txt_prev=" + (int)p60prevTxt.ToInt64());
        int p60txt = (int)Cases.Winforms1.SendMessageW(hList60, LVM_GETTEXTCOLOR, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p60_txt=" + p60txt);
        bool p60destroyed = DestroyWindow(hList60) != 0;
        Console.WriteLine("p60_destroyed=" + p60destroyed);

        // ===================== Phase 61: ListBox select string =====================
        // LB_SELECTSTRING (0x18C) finds the first item whose text matches the
        // lParam prefix (search starts after wParam, wrapping) AND selects it,
        // returning the index. The find+select combined channel (vs the
        // find-only LB_FINDSTRING of Phase 37). New list box: Alpha/Beta/Gamma;
        // select "Beta" -> index 1 + LB_GETCURSEL confirms the selection.
        IntPtr hList61 = CreateWindowExW(0, "LISTBOX", "", WS_CHILD | WS_VISIBLE,
            0, 0, 160, 120, hForm, (IntPtr)(ID_FIND49 + 14), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hList61, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Alpha"));
        Cases.Winforms1.SendMessageW(hList61, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Beta"));
        Cases.Winforms1.SendMessageW(hList61, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("Gamma"));
        IntPtr p61sel = Cases.Winforms1.SendMessageW(hList61, LB_SELECTSTRING, new IntPtr(-1), Marshal.StringToHGlobalUni("Be"));
        Console.WriteLine("p61_select_ret=" + (int)p61sel.ToInt64());
        int p61cur = (int)Cases.Winforms1.SendMessageW(hList61, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p61_cursel=" + p61cur);
        Console.WriteLine("p61_selected=" + (p61cur == 1));
        bool p61destroyed = DestroyWindow(hList61) != 0;
        Console.WriteLine("p61_destroyed=" + p61destroyed);

        // ===================== Phase 62: ListBox multi-column width =====================
        // LBS_MULTICOLUMN (0x200) list boxes can set the column width via
        // LB_SETCOLUMNWIDTH (0x195); LB_GETCOLUMNWIDTH (0x196) reads it back.
        // PROBED headless the width is NOT stored (LB_GETCOLUMNWIDTH reads -1
        // both before and after LB_SETCOLUMNWIDTH(80) -- the column width is
        // a drawing-mode property only materialized when items exist; the
        // reads are observed values).
        IntPtr hList62 = CreateWindowExW(0, "LISTBOX", "", WS_CHILD | WS_VISIBLE | LBS_MULTICOLUMN,
            0, 0, 200, 120, hForm, (IntPtr)(ID_FIND49 + 15), hModule, IntPtr.Zero);
        IntPtr p62w0 = Cases.Winforms1.SendMessageW(hList62, LB_GETCOLUMNWIDTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p62_w0=" + (int)p62w0.ToInt64());
        Cases.Winforms1.SendMessageW(hList62, LB_SETCOLUMNWIDTH, new IntPtr(80), IntPtr.Zero);
        IntPtr p62w1 = Cases.Winforms1.SendMessageW(hList62, LB_GETCOLUMNWIDTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p62_w1=" + (int)p62w1.ToInt64());
        bool p62destroyed = DestroyWindow(hList62) != 0;
        Console.WriteLine("p62_destroyed=" + p62destroyed);

        // ===================== Phase 63: ListView selection mark =====================
        // LVM_SETSELECTIONMARK (0x102F) sets the selection anchor item;
        // LVM_GETSELECTIONMARK (0x1032) reads it back. PROBED headless the
        // mark is NOT stored (read 0 before AND after SETSELECTIONMARK(2) --
        // the anchor is a mouse-driven UI state; the reads are observed
        // values).
        IntPtr hList63 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 16), hModule, IntPtr.Zero);
        IntPtr p63colText = Marshal.StringToHGlobalUni("C");
        int[] p63col = new int[13];
        p63col[0] = LVCF_TEXT; p63col[6] = (int)p63colText.ToInt64();
        GCHandle p63cpin = GCHandle.Alloc(p63col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList63, LVM_INSERTCOLUMNW, IntPtr.Zero, p63cpin.AddrOfPinnedObject());
        p63cpin.Free();
        Marshal.FreeHGlobal(p63colText);
        for (int i = 0; i < 4; i++)
        {
            IntPtr t = Marshal.StringToHGlobalUni("r" + i);
            int[] lvi = new int[22];
            lvi[0] = LVIF_TEXT; lvi[1] = i;
            long pt = t.ToInt64();
            lvi[6] = (int)(pt & 0xFFFFFFFFL); lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
            lvi[8] = 128;
            GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList63, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
            pin.Free();
            Marshal.FreeHGlobal(t);
        }
        IntPtr p63mark0 = Cases.Winforms1.SendMessageW(hList63, LVM_GETSELECTIONMARK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p63_mark0=" + (int)p63mark0.ToInt64());
        Cases.Winforms1.SendMessageW(hList63, LVM_SETSELECTIONMARK, new IntPtr(2), IntPtr.Zero);
        IntPtr p63mark1 = Cases.Winforms1.SendMessageW(hList63, LVM_GETSELECTIONMARK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p63_mark1=" + (int)p63mark1.ToInt64());
        bool p63destroyed = DestroyWindow(hList63) != 0;
        Console.WriteLine("p63_destroyed=" + p63destroyed);

        // ===================== Phase 64: ComboBox dropped width =====================
        // CB_SETDROPPEDWIDTH (0x160) sets the width (in pixels) of the combo's
        // dropped list; CB_GETDROPPEDWIDTH (0x15F) reads it back. Pure scalar
        // round-trip (the width is stored by the control even headless).
        IntPtr hCombo64 = CreateWindowExW(0, "COMBOBOX", "", WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST,
            0, 0, 160, 200, hForm, (IntPtr)(ID_FIND49 + 18), hModule, IntPtr.Zero);
        IntPtr p64w0 = Cases.Winforms1.SendMessageW(hCombo64, CB_GETDROPPEDWIDTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p64_w0=" + (int)p64w0.ToInt64());
        Cases.Winforms1.SendMessageW(hCombo64, CB_SETDROPPEDWIDTH, new IntPtr(200), IntPtr.Zero);
        IntPtr p64w1 = Cases.Winforms1.SendMessageW(hCombo64, CB_GETDROPPEDWIDTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p64_w1=" + (int)p64w1.ToInt64());
        Console.WriteLine("p64_roundtrip=" + ((int)p64w1.ToInt64() == 200));
        bool p64destroyed = DestroyWindow(hCombo64) != 0;
        Console.WriteLine("p64_destroyed=" + p64destroyed);

        // ===================== Phase 65: ComboBox item height =====================
        // CB_SETITEMHEIGHT (0x153) sets the height of the combo's list items;
        // CB_GETITEMHEIGHT (0x154) reads it back. Pure scalar round-trip
        // (set 24 -> read 24).
        IntPtr hCombo65 = CreateWindowExW(0, "COMBOBOX", "", WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST,
            0, 0, 160, 200, hForm, (IntPtr)(ID_FIND49 + 19), hModule, IntPtr.Zero);
        IntPtr p65h0 = Cases.Winforms1.SendMessageW(hCombo65, CB_GETITEMHEIGHT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p65_h0=" + (int)p65h0.ToInt64());
        Cases.Winforms1.SendMessageW(hCombo65, CB_SETITEMHEIGHT, IntPtr.Zero, new IntPtr(24));
        IntPtr p65h1 = Cases.Winforms1.SendMessageW(hCombo65, CB_GETITEMHEIGHT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p65_h1=" + (int)p65h1.ToInt64());
        Console.WriteLine("p65_roundtrip=" + ((int)p65h1.ToInt64() == 24));
        bool p65destroyed = DestroyWindow(hCombo65) != 0;
        Console.WriteLine("p65_destroyed=" + p65destroyed);

        // ===================== Phase 66: ListView header column rect =====================
        // HDM_GETITEMRECT (0x1207) writes the header column's bounding RECT
        // into a caller RECT* (pinned int[4]) -- sent to the LIST's header
        // window (LVM_GETHEADER). With 3 columns the col0 rect is non-empty
        // and col1 is to the RIGHT of col0 (relative-only, positions
        // environment-dependent).
        IntPtr hList66 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 20), hModule, IntPtr.Zero);
        for (int c = 0; c < 3; c++)
        {
            IntPtr ct = Marshal.StringToHGlobalUni("C" + c);
            int[] col = new int[13];
            col[0] = LVCF_TEXT; col[6] = (int)ct.ToInt64();
            GCHandle cp = GCHandle.Alloc(col, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList66, LVM_INSERTCOLUMNW, new IntPtr(c), cp.AddrOfPinnedObject());
            cp.Free();
            Marshal.FreeHGlobal(ct);
        }
        IntPtr hHead66 = Cases.Winforms1.SendMessageW(hList66, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p66_header=" + (hHead66 != IntPtr.Zero));
        int[] p66r0 = new int[4];
        int[] p66r1 = new int[4];
        GCHandle p66r0pin = GCHandle.Alloc(p66r0, GCHandleType.Pinned);
        GCHandle p66r1pin = GCHandle.Alloc(p66r1, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hHead66, HDM_GETITEMRECT, IntPtr.Zero, p66r0pin.AddrOfPinnedObject());
        Cases.Winforms1.SendMessageW(hHead66, HDM_GETITEMRECT, new IntPtr(1), p66r1pin.AddrOfPinnedObject());
        p66r0pin.Free(); p66r1pin.Free();
        bool p66r0_nonempty = p66r0[2] > p66r0[0] && p66r0[3] > p66r0[1];
        Console.WriteLine("p66_r0_nonempty=" + p66r0_nonempty);
        bool p66r1_right = p66r1[0] >= p66r0[2];
        Console.WriteLine("p66_r1_right=" + p66r1_right);
        bool p66destroyed = DestroyWindow(hList66) != 0;
        Console.WriteLine("p66_destroyed=" + p66destroyed);

        // ===================== Phase 67: header sort arrow =====================
        // HDM_SETITEM (0x1202) / HDM_GETITEM (0x1201) read/write the header
        // column's HDITEM; the fmt field (offset 28 = pinned [7]) carries the
        // HDF_SORTUP (0x400) / HDF_SORTDOWN (0x200) sort-arrow bits. PROBED
        // headless: HDM_SETITEM returns nonzero but the fmt does NOT persist
        // (HDM_GETITEM reads fmt 0 -- the sort-arrow fmt is a drawn state set
        // by the control's own sorting, not a stored HDITEM field; observed
        // values).
        IntPtr hList67 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 21), hModule, IntPtr.Zero);
        IntPtr p67ct = Marshal.StringToHGlobalUni("C");
        int[] p67col = new int[13];
        p67col[0] = LVCF_TEXT; p67col[6] = (int)p67ct.ToInt64();
        GCHandle p67cp = GCHandle.Alloc(p67col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList67, LVM_INSERTCOLUMNW, IntPtr.Zero, p67cp.AddrOfPinnedObject());
        p67cp.Free();
        Marshal.FreeHGlobal(p67ct);
        IntPtr hHead67 = Cases.Winforms1.SendMessageW(hList67, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        int[] p67item = new int[12];
        p67item[0] = HDI_FORMAT;
        p67item[7] = HDF_SORTUP;
        GCHandle p67pin = GCHandle.Alloc(p67item, GCHandleType.Pinned);
        IntPtr p67set = Cases.Winforms1.SendMessageW(hHead67, HDM_SETITEM, IntPtr.Zero, p67pin.AddrOfPinnedObject());
        p67pin.Free();
        Console.WriteLine("p67_set_ok=" + (p67set.ToInt64() != 0));
        int[] p67item2 = new int[12];
        p67item2[0] = HDI_FORMAT;
        GCHandle p67pin2 = GCHandle.Alloc(p67item2, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hHead67, HDM_GETITEM, IntPtr.Zero, p67pin2.AddrOfPinnedObject());
        p67pin2.Free();
        Console.WriteLine("p67_fmt=" + p67item2[7]);
        bool p67destroyed = DestroyWindow(hList67) != 0;
        Console.WriteLine("p67_destroyed=" + p67destroyed);

        // ===================== Phase 68: Edit first visible line =====================
        // EM_GETFIRSTVISIBLELINE (0xCE) reads the index of the first visible
        // line in a multi-line edit. A fresh 3-line edit shows line 0; pure
        // scalar read.
        IntPtr hEdit68 = CreateWindowExW(0, "EDIT", "", WS_CHILD | WS_VISIBLE | ES_MULTILINE | WS_VSCROLL,
            0, 0, 200, 80, hForm, (IntPtr)(ID_FIND49 + 22), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hEdit68, WM_SETTEXT, IntPtr.Zero, Marshal.StringToHGlobalUni("L1\r\nL2\r\nL3"));
        int p68fl = (int)Cases.Winforms1.SendMessageW(hEdit68, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p68_first_line=" + p68fl);
        Console.WriteLine("p68_at_top=" + (p68fl == 0));
        bool p68destroyed = DestroyWindow(hEdit68) != 0;
        Console.WriteLine("p68_destroyed=" + p68destroyed);

        // ===================== Phase 69: ListView double-buffer style =====================
        // LVS_EX_DOUBLEBUFFER (0x10000) is the flicker-free extended style bit
        // -- toggled through the existing LVM_SETEXTENDEDLISTVIEWSTYLE /
        // LVM_GETEXTENDEDLISTVIEWSTYLE channel (the Phase-33 grid-lines
        // pattern). Fresh 0 -> set -> readback contains the bit.
        IntPtr hList69 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 23), hModule, IntPtr.Zero);
        int p69ex0 = (int)Cases.Winforms1.SendMessageW(hList69, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p69_ex0=" + p69ex0);
        Cases.Winforms1.SendMessageW(hList69, LVM_SETEXTENDEDLISTVIEWSTYLE, new IntPtr(LVS_EX_DOUBLEBUFFER), new IntPtr(LVS_EX_DOUBLEBUFFER));
        int p69ex1 = (int)Cases.Winforms1.SendMessageW(hList69, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p69_ex1=" + p69ex1);
        Console.WriteLine("p69_doublebuffered=" + ((p69ex1 & LVS_EX_DOUBLEBUFFER) != 0));
        bool p69destroyed = DestroyWindow(hList69) != 0;
        Console.WriteLine("p69_destroyed=" + p69destroyed);

        // ===================== Phase 70: Edit password char =====================
        // EM_SETPASSWORDCHAR (0xCC) sets the mask char of a password edit
        // (wParam = the char); EM_GETPASSWORDCHAR (0xD2) reads it back. Pure
        // scalar round-trip (set '*' = 0x2A -> read 42).
        IntPtr hEdit70 = CreateWindowExW(0, "EDIT", "", WS_CHILD | WS_VISIBLE | ES_PASSWORD,
            0, 0, 200, 40, hForm, (IntPtr)(ID_FIND49 + 24), hModule, IntPtr.Zero);
        IntPtr p70c0 = Cases.Winforms1.SendMessageW(hEdit70, EM_GETPASSWORDCHAR, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p70_c0=" + (int)p70c0.ToInt64());
        Cases.Winforms1.SendMessageW(hEdit70, EM_SETPASSWORDCHAR, new IntPtr('*'), IntPtr.Zero);
        IntPtr p70c1 = Cases.Winforms1.SendMessageW(hEdit70, EM_GETPASSWORDCHAR, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p70_c1=" + (int)p70c1.ToInt64());
        Console.WriteLine("p70_star=" + ((int)p70c1.ToInt64() == 42));
        bool p70destroyed = DestroyWindow(hEdit70) != 0;
        Console.WriteLine("p70_destroyed=" + p70destroyed);

        // ===================== Phase 71: Edit read-only toggle =====================
        // EM_SETREADONLY (0xCF) sets (wParam TRUE) / clears (FALSE) the edit's
        // read-only flag. PROBED headless: both calls return 1 (the setter
        // succeeds, return is not a previous-state flag in this build) --
        // observed values, not a round-trip assertion.
        IntPtr hEdit71 = CreateWindowExW(0, "EDIT", "", WS_CHILD | WS_VISIBLE,
            0, 0, 200, 40, hForm, (IntPtr)(ID_FIND49 + 25), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hEdit71, WM_SETTEXT, IntPtr.Zero, Marshal.StringToHGlobalUni("Edit me"));
        int p71ro0 = (int)Cases.Winforms1.SendMessageW(hEdit71, EM_SETREADONLY, new IntPtr(1), IntPtr.Zero).ToInt64();
        Console.WriteLine("p71_setro_ret=" + p71ro0);
        int p71ro1 = (int)Cases.Winforms1.SendMessageW(hEdit71, EM_SETREADONLY, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p71_clearro_ret=" + p71ro1);
        bool p71destroyed = DestroyWindow(hEdit71) != 0;
        Console.WriteLine("p71_destroyed=" + p71destroyed);

        // ===================== Phase 72: ListView delete items =====================
        // LVM_DELETEITEM (0x1008) deletes one row (wParam = index, returns
        // BOOL); LVM_DELETEALLITEMS (0x1009) clears the list. 3 rows -> delete
        // index 1 -> count 2 -> delete all -> count 0.
        IntPtr hList72 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 26), hModule, IntPtr.Zero);
        IntPtr p72ct = Marshal.StringToHGlobalUni("C");
        int[] p72col = new int[13];
        p72col[0] = LVCF_TEXT; p72col[6] = (int)p72ct.ToInt64();
        GCHandle p72cp = GCHandle.Alloc(p72col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList72, LVM_INSERTCOLUMNW, IntPtr.Zero, p72cp.AddrOfPinnedObject());
        p72cp.Free();
        Marshal.FreeHGlobal(p72ct);
        for (int i = 0; i < 3; i++)
        {
            IntPtr t = Marshal.StringToHGlobalUni("r" + i);
            int[] lvi = new int[22];
            lvi[0] = LVIF_TEXT; lvi[1] = i;
            long pt = t.ToInt64();
            lvi[6] = (int)(pt & 0xFFFFFFFFL); lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
            lvi[8] = 128;
            GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList72, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
            pin.Free();
            Marshal.FreeHGlobal(t);
        }
        int p72c0 = (int)Cases.Winforms1.SendMessageW(hList72, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p72_count0=" + p72c0);
        bool p72del = Cases.Winforms1.SendMessageW(hList72, LVM_DELETEITEM, new IntPtr(1), IntPtr.Zero).ToInt64() != 0;
        Console.WriteLine("p72_delete_ok=" + p72del);
        int p72c1 = (int)Cases.Winforms1.SendMessageW(hList72, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p72_count1=" + p72c1);
        bool p72delall = Cases.Winforms1.SendMessageW(hList72, LVM_DELETEALLITEMS, IntPtr.Zero, IntPtr.Zero).ToInt64() != 0;
        Console.WriteLine("p72_deleteall_ok=" + p72delall);
        int p72c2 = (int)Cases.Winforms1.SendMessageW(hList72, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p72_count2=" + p72c2);
        Console.WriteLine("p72_cleared=" + (p72c2 == 0));
        bool p72destroyed = DestroyWindow(hList72) != 0;
        Console.WriteLine("p72_destroyed=" + p72destroyed);

        // ===================== Phase 73: ComboBox delete string =====================
        // CB_DELETESTRING (0x144) removes the item at wParam and returns the
        // REMAINING count. 3 items -> delete index 1 -> count 2 (the phase-38
        // channel).
        IntPtr hCombo73 = CreateWindowExW(0, "COMBOBOX", "", WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST,
            0, 0, 160, 200, hForm, (IntPtr)(ID_FIND49 + 27), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hCombo73, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("A"));
        Cases.Winforms1.SendMessageW(hCombo73, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("B"));
        Cases.Winforms1.SendMessageW(hCombo73, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("C"));
        int p73c0 = (int)Cases.Winforms1.SendMessageW(hCombo73, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p73_count0=" + p73c0);
        int p73rem = (int)Cases.Winforms1.SendMessageW(hCombo73, CB_DELETESTRING, new IntPtr(1), IntPtr.Zero).ToInt64();
        Console.WriteLine("p73_remaining=" + p73rem);
        int p73c1 = (int)Cases.Winforms1.SendMessageW(hCombo73, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p73_count1=" + p73c1);
        Console.WriteLine("p73_deleted=" + (p73c1 == 2));
        bool p73destroyed = DestroyWindow(hCombo73) != 0;
        Console.WriteLine("p73_destroyed=" + p73destroyed);

        // ===================== Phase 74: ListBox delete string =====================
        // LB_DELETESTRING (0x182) removes the item at wParam and returns the
        // REMAINING count. 3 items -> delete index 1 -> count 2 (the phase-37
        // channel).
        IntPtr hList74 = CreateWindowExW(0, "LISTBOX", "", WS_CHILD | WS_VISIBLE,
            0, 0, 160, 120, hForm, (IntPtr)(ID_FIND49 + 28), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hList74, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("A"));
        Cases.Winforms1.SendMessageW(hList74, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("B"));
        Cases.Winforms1.SendMessageW(hList74, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("C"));
        int p74c0 = (int)Cases.Winforms1.SendMessageW(hList74, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p74_count0=" + p74c0);
        int p74rem = (int)Cases.Winforms1.SendMessageW(hList74, LB_DELETESTRING, new IntPtr(1), IntPtr.Zero).ToInt64();
        Console.WriteLine("p74_remaining=" + p74rem);
        int p74c1 = (int)Cases.Winforms1.SendMessageW(hList74, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p74_count1=" + p74c1);
        Console.WriteLine("p74_deleted=" + (p74c1 == 2));
        bool p74destroyed = DestroyWindow(hList74) != 0;
        Console.WriteLine("p74_destroyed=" + p74destroyed);

        // ===================== Phase 75: ComboBox/ListBox reset content =====================
        // CB_RESETCONTENT (0x14B) / LB_RESETCONTENT (0x184) clear ALL items
        // from a combo / list. 3 items -> reset -> count 0.
        IntPtr hCombo75 = CreateWindowExW(0, "COMBOBOX", "", WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST,
            0, 0, 160, 200, hForm, (IntPtr)(ID_FIND49 + 29), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hCombo75, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("A"));
        Cases.Winforms1.SendMessageW(hCombo75, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("B"));
        Cases.Winforms1.SendMessageW(hCombo75, CB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("C"));
        int p75c0 = (int)Cases.Winforms1.SendMessageW(hCombo75, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Cases.Winforms1.SendMessageW(hCombo75, CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        int p75c1 = (int)Cases.Winforms1.SendMessageW(hCombo75, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p75_combo=" + p75c0 + "/" + p75c1);
        IntPtr hList75 = CreateWindowExW(0, "LISTBOX", "", WS_CHILD | WS_VISIBLE,
            0, 0, 160, 120, hForm, (IntPtr)(ID_FIND49 + 30), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hList75, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("A"));
        Cases.Winforms1.SendMessageW(hList75, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("B"));
        Cases.Winforms1.SendMessageW(hList75, LB_ADDSTRING, IntPtr.Zero, Marshal.StringToHGlobalUni("C"));
        int p75l0 = (int)Cases.Winforms1.SendMessageW(hList75, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Cases.Winforms1.SendMessageW(hList75, LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        int p75l1 = (int)Cases.Winforms1.SendMessageW(hList75, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p75_list=" + p75l0 + "/" + p75l1);
        Console.WriteLine("p75_cleared=" + (p75c1 == 0 && p75l1 == 0));
        bool p75cdestroyed = DestroyWindow(hCombo75) != 0;
        bool p75ldestroyed = DestroyWindow(hList75) != 0;
        Console.WriteLine("p75_destroyed=" + (p75cdestroyed && p75ldestroyed));

        // ===================== Phase 76: Edit margins =====================
        // EM_SETMARGINS (0xD3) sets the left/right edit margin (wParam =
        // EC_LEFTMARGIN|EC_RIGHTMARGIN, lParam = MAKELONG(left,right));
        // EM_GETMARGINS (0xD4) returns MAKELONG(left,right). Pure scalar
        // round-trip (set left 20 right 30 -> read back 20/30).
        IntPtr hEdit76 = CreateWindowExW(0, "EDIT", "", WS_CHILD | WS_VISIBLE,
            0, 0, 200, 40, hForm, (IntPtr)(ID_FIND49 + 31), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hEdit76, EM_SETMARGINS, new IntPtr(EC_LEFTMARGIN | EC_RIGHTMARGIN), new IntPtr((30 << 16) | 20));
        int p76m = (int)Cases.Winforms1.SendMessageW(hEdit76, EM_GETMARGINS, IntPtr.Zero, IntPtr.Zero).ToInt64();
        int p76left = p76m & 0xFFFF;
        int p76right = (p76m >> 16) & 0xFFFF;
        Console.WriteLine("p76_left=" + p76left);
        Console.WriteLine("p76_right=" + p76right);
        Console.WriteLine("p76_margins_ok=" + (p76left == 20 && p76right == 30));
        bool p76destroyed = DestroyWindow(hEdit76) != 0;
        Console.WriteLine("p76_destroyed=" + p76destroyed);

        // ===================== Phase 77: Edit format rect =====================
        // EM_SETRECT (0xB3) sets the editable formatting rectangle;
        // EM_GETRECT (0xB2) reads it back through a pinned int[4]. PROBED:
        // on a SINGLE-LINE edit EM_SETRECT is ignored (the format rect is
        // system-managed; EM_GETRECT reads the client rect 0/0/W/H -- the
        // absolute size is environment-dependent, so only non-empty is
        // asserted).
        IntPtr hEdit77 = CreateWindowExW(0, "EDIT", "", WS_CHILD | WS_VISIBLE,
            0, 0, 200, 40, hForm, (IntPtr)(ID_FIND49 + 32), hModule, IntPtr.Zero);
        int[] p77r = new int[4];
        p77r[0] = 5; p77r[1] = 5; p77r[2] = 100; p77r[3] = 30;
        GCHandle p77pin = GCHandle.Alloc(p77r, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hEdit77, EM_SETRECT, IntPtr.Zero, p77pin.AddrOfPinnedObject());
        p77pin.Free();
        int[] p77r2 = new int[4];
        GCHandle p77pin2 = GCHandle.Alloc(p77r2, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hEdit77, EM_GETRECT, IntPtr.Zero, p77pin2.AddrOfPinnedObject());
        p77pin2.Free();
        bool p77nonempty = p77r2[2] > p77r2[0] && p77r2[3] > p77r2[1];
        Console.WriteLine("p77_rect_nonempty=" + p77nonempty);
        bool p77destroyed = DestroyWindow(hEdit77) != 0;
        Console.WriteLine("p77_destroyed=" + p77destroyed);

        // ===================== Phase 78: RichTextBox default char format =====================
        // EM_GETCHARFORMAT with SCF_DEFAULT (0) reads the control's DEFAULT
        // character format (vs the SCF_SELECTION scope of Phase 56). A fresh
        // RichEdit's default has no bold bit and a filled CHARFORMAT2W.
        IntPtr hRich78 = CreateWindowExW(0, RICHEDIT_CLASS, "", WS_CHILD | WS_VISIBLE | ES_MULTILINE,
            0, 0, 260, 80, hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hRich78, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        Cases.Winforms1.SendMessageW(hRich78, WM_SETTEXT, IntPtr.Zero, Marshal.StringToHGlobalUni("Default fmt"));
        int[] p78cfr = new int[29];
        p78cfr[0] = 116;
        GCHandle p78pin = GCHandle.Alloc(p78cfr, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hRich78, EM_GETCHARFORMAT, new IntPtr(SCF_DEFAULT), p78pin.AddrOfPinnedObject());
        p78pin.Free();
        bool p78bold = (p78cfr[2] & CFE_BOLD) != 0;
        Console.WriteLine("p78_default_bold=" + p78bold);
        Console.WriteLine("p78_cbsize=" + p78cfr[0]);
        bool p78destroyed = DestroyWindow(hRich78) != 0;
        Console.WriteLine("p78_destroyed=" + p78destroyed);

        // ===================== Phase 79: StatusBar text readback =====================
        // SB_SETTEXT (0x40B) writes a part's text; SB_GETTEXTLENGTH (0x40C)
        // counts it; SB_GETTEXT (0x40D) copies it into a caller buffer
        // (wParam = part, lParam = buffer). The Phase-9 status bar: set
        // "Readback" on part 0, read the length + text back.
        Cases.Winforms1.SendMessageW(hStatus, SB_SETTEXT, IntPtr.Zero, Marshal.StringToHGlobalUni("Readback"));
        int p79len = (int)Cases.Winforms1.SendMessageW(hStatus, SB_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p79_len=" + p79len);
        IntPtr p79buf = Marshal.AllocHGlobal(64);
        int p79ret = (int)Cases.Winforms1.SendMessageW(hStatus, SB_GETTEXT, IntPtr.Zero, p79buf).ToInt64();
        string p79text = Marshal.PtrToStringUni(p79buf);
        Marshal.FreeHGlobal(p79buf);
        Console.WriteLine("p79_ret=" + p79ret);
        Console.WriteLine("p79_text=" + p79text);
        Console.WriteLine("p79_match=" + (p79text == "Readback"));

        // ===================== Phase 80: StatusBar icon =====================
        // SB_SETICON (0x40F) sets a part's icon (wParam = part, lParam =
        // HICON); SB_GETICON (0x40E) reads it back. PROBED headless the icon
        // is NOT persisted (SB_GETICON reads 0 after SB_SETICON -- the icon
        // is a drawn state; observed value).
        Cases.Winforms1.SendMessageW(hStatus, SB_SETICON, IntPtr.Zero, p24icon);
        IntPtr p80icon = Cases.Winforms1.SendMessageW(hStatus, SB_GETICON, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p80_icon_nonzero=" + (p80icon != IntPtr.Zero));

        // ===================== Phase 81: StatusBar borders =====================
        // SB_GETBORDERS (0x407) writes the status bar's three border widths
        // (horizontal, vertical, between parts) into a caller int[3]. Fresh
        // bar: the borders are the default sizing metrics (positive values,
        // asserted positive only -- the exact pixels are font-metric
        // dependent).
        int[] p81b = new int[3];
        GCHandle p81pin = GCHandle.Alloc(p81b, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hStatus, SB_GETBORDERS, IntPtr.Zero, p81pin.AddrOfPinnedObject());
        p81pin.Free();
        bool p81vbpos = p81b[1] > 0 && p81b[2] > 0;
        Console.WriteLine("p81_vb_positive=" + p81vbpos);
        Console.WriteLine("p81_borders=" + p81b[0] + "/" + p81b[1] + "/" + p81b[2]);

        // ===================== Phase 82: StatusBar Unicode format =====================
        // SB_GETUNICODEFORMAT (0x2006) reads whether the status bar uses
        // Unicode messages; SB_SETUNICODEFORMAT (0x2005) sets it (wParam =
        // TRUE/FALSE). Pure scalar: fresh value + set-true round-trip.
        int p82u0 = (int)Cases.Winforms1.SendMessageW(hStatus, SB_GETUNICODEFORMAT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p82_u0=" + p82u0);
        Cases.Winforms1.SendMessageW(hStatus, SB_SETUNICODEFORMAT, new IntPtr(1), IntPtr.Zero);
        int p82u1 = (int)Cases.Winforms1.SendMessageW(hStatus, SB_GETUNICODEFORMAT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p82_u1=" + p82u1);
        Console.WriteLine("p82_unicode=" + (p82u1 == 1));

        // ===================== Phase 83: ListView column header autosize =====================
        // LVM_SETCOLUMNWIDTH with LVSCW_AUTOSIZE_USEHEADER (-2) sizes the
        // column to its HEADER text width (vs the cell-content -1 of Phase
        // 49). Set a wide header then autosize: the width becomes a real
        // positive value driven by the header text.
        IntPtr hList83 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 33), hModule, IntPtr.Zero);
        IntPtr p83ct = Marshal.StringToHGlobalUni("WideHeader");
        int[] p83col = new int[13];
        p83col[0] = LVCF_TEXT; p83col[6] = (int)p83ct.ToInt64();
        GCHandle p83cp = GCHandle.Alloc(p83col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList83, LVM_INSERTCOLUMNW, IntPtr.Zero, p83cp.AddrOfPinnedObject());
        p83cp.Free();
        Marshal.FreeHGlobal(p83ct);
        Cases.Winforms1.SendMessageW(hList83, LVM_SETCOLUMNWIDTH, new IntPtr(0), new IntPtr(LVSCW_AUTOSIZE_USEHEADER));
        int p83w = (int)Cases.Winforms1.SendMessageW(hList83, LVM_GETCOLUMNWIDTH, new IntPtr(0), IntPtr.Zero).ToInt64();
        Console.WriteLine("p83_header_width=" + p83w);
        Console.WriteLine("p83_autosized=" + (p83w > 0));
        bool p83destroyed = DestroyWindow(hList83) != 0;
        Console.WriteLine("p83_destroyed=" + p83destroyed);

        // ===================== Phase 84: ListView select all =====================
        // LVM_SETITEMSTATE with LVIS_SELECTED on every row + LVM_GETSELECTEDCOUNT
        // counts the selection. 5 rows -> select all -> count 5.
        IntPtr hList84 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 34), hModule, IntPtr.Zero);
        IntPtr p84ct = Marshal.StringToHGlobalUni("C");
        int[] p84col = new int[13];
        p84col[0] = LVCF_TEXT; p84col[6] = (int)p84ct.ToInt64();
        GCHandle p84cp = GCHandle.Alloc(p84col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList84, LVM_INSERTCOLUMNW, IntPtr.Zero, p84cp.AddrOfPinnedObject());
        p84cp.Free();
        Marshal.FreeHGlobal(p84ct);
        for (int i = 0; i < 5; i++)
        {
            IntPtr t = Marshal.StringToHGlobalUni("r" + i);
            int[] lvi = new int[22];
            lvi[0] = LVIF_TEXT; lvi[1] = i;
            long pt = t.ToInt64();
            lvi[6] = (int)(pt & 0xFFFFFFFFL); lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
            lvi[8] = 128;
            GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList84, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
            pin.Free();
            Marshal.FreeHGlobal(t);
        }
        int[] p84st = new int[14];
        p84st[0] = LVIF_STATE;
        p84st[3] = LVIS_SELECTED;
        p84st[4] = LVIS_SELECTED;
        GCHandle p84spin = GCHandle.Alloc(p84st, GCHandleType.Pinned);
        for (int i = 0; i < 5; i++)
        {
            Cases.Winforms1.SendMessageW(hList84, LVM_SETITEMSTATE, new IntPtr(i), p84spin.AddrOfPinnedObject());
        }
        p84spin.Free();
        int p84sel = (int)Cases.Winforms1.SendMessageW(hList84, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p84_selected=" + p84sel);
        Console.WriteLine("p84_all=" + (p84sel == 5));
        bool p84destroyed = DestroyWindow(hList84) != 0;
        Console.WriteLine("p84_destroyed=" + p84destroyed);

        // ===================== Phase 85: ListView focus item =====================
        // LVM_GETNEXTITEM with LVNI_FOCUSED returns the focused row. Headless
        // (no keyboard focus on the list) the probe returns -1 -- the
        // focus-item relation is observable as a no-focus sentinel.
        IntPtr hList85 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 35), hModule, IntPtr.Zero);
        IntPtr p85ct = Marshal.StringToHGlobalUni("C");
        int[] p85col = new int[13];
        p85col[0] = LVCF_TEXT; p85col[6] = (int)p85ct.ToInt64();
        GCHandle p85cp = GCHandle.Alloc(p85col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hList85, LVM_INSERTCOLUMNW, IntPtr.Zero, p85cp.AddrOfPinnedObject());
        p85cp.Free();
        Marshal.FreeHGlobal(p85ct);
        for (int i = 0; i < 3; i++)
        {
            IntPtr t = Marshal.StringToHGlobalUni("r" + i);
            int[] lvi = new int[22];
            lvi[0] = LVIF_TEXT; lvi[1] = i;
            long pt = t.ToInt64();
            lvi[6] = (int)(pt & 0xFFFFFFFFL); lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
            lvi[8] = 128;
            GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
            Cases.Winforms1.SendMessageW(hList85, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
            pin.Free();
            Marshal.FreeHGlobal(t);
        }
        int p85focus = (int)Cases.Winforms1.SendMessageW(hList85, LVM_GETNEXTITEM, new IntPtr(LVNI_FOCUSED), IntPtr.Zero).ToInt64();
        Console.WriteLine("p85_focus=" + p85focus);
        Console.WriteLine("p85_nofocus=" + (p85focus == -1));
        bool p85destroyed = DestroyWindow(hList85) != 0;
        Console.WriteLine("p85_destroyed=" + p85destroyed);

        // ===================== Phase 86: StatusBar parts count =====================
        // SB_GETPARTS (0x406) returns the number of parts (wParam = the
        // buffer size, lParam = a caller int[] filled with the right edges).
        // Set 3 parts {150,300,-1}, then SB_GETPARTS(4) reads 3 back.
        int[] p86parts = { 150, 300, -1 };
        GCHandle p86pin = GCHandle.Alloc(p86parts, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(hStatus, SB_SETPARTS, new IntPtr(3), p86pin.AddrOfPinnedObject());
        p86pin.Free();
        int[] p86out = new int[4];
        GCHandle p86opin = GCHandle.Alloc(p86out, GCHandleType.Pinned);
        int p86count = (int)Cases.Winforms1.SendMessageW(hStatus, SB_GETPARTS, new IntPtr(4), p86opin.AddrOfPinnedObject()).ToInt64();
        p86opin.Free();
        Console.WriteLine("p86_partscount=" + p86count);
        Console.WriteLine("p86_parts=" + p86out[0] + "/" + p86out[1] + "/" + p86out[2]);
        bool p86match = p86count == 3 && p86out[0] == 150 && p86out[1] == 300 && p86out[2] == -1;
        Console.WriteLine("p86_match=" + p86match);

        // ===================== Phase 87: Edit line scroll =====================
        // EM_LINESCROLL (0xB6) scrolls a multi-line edit vertically (lParam =
        // lines); EM_GETFIRSTVISIBLELINE (Phase 68) reads the top line. A
        // tall edit with many lines: scroll down 2 -> first visible line
        // moves from 0 to 2.
        IntPtr hEdit87 = CreateWindowExW(0, "EDIT", "", WS_CHILD | WS_VISIBLE | ES_MULTILINE | WS_VSCROLL,
            0, 0, 200, 60, hForm, (IntPtr)(ID_FIND49 + 36), hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hEdit87, WM_SETTEXT, IntPtr.Zero, Marshal.StringToHGlobalUni("L0\r\nL1\r\nL2\r\nL3\r\nL4\r\nL5\r\nL6"));
        int p87f0 = (int)Cases.Winforms1.SendMessageW(hEdit87, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Cases.Winforms1.SendMessageW(hEdit87, EM_LINESCROLL, IntPtr.Zero, new IntPtr(2));
        int p87f1 = (int)Cases.Winforms1.SendMessageW(hEdit87, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p87_top0=" + p87f0);
        Console.WriteLine("p87_top1=" + p87f1);
        Console.WriteLine("p87_scrolled=" + (p87f1 > p87f0));
        bool p87destroyed = DestroyWindow(hEdit87) != 0;
        Console.WriteLine("p87_destroyed=" + p87destroyed);

        // ===================== Phase 88: RichTextBox background color =====================
        // EM_SETBKGNDCOLOR (0x443) sets the RichEdit background (wParam = 0
        // use the color, lParam = CRGB); EM_GETBKGNDCOLOR (0x442) reads it
        // back. PROBED headless the readback is 0 (the background color is
        // not persisted without a real paint -- observed value).
        IntPtr hRich88 = CreateWindowExW(0, RICHEDIT_CLASS, "", WS_CHILD | WS_VISIBLE | ES_MULTILINE,
            0, 0, 260, 80, hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        Cases.Winforms1.SendMessageW(hRich88, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        Cases.Winforms1.SendMessageW(hRich88, EM_SETBKGNDCOLOR, IntPtr.Zero, new IntPtr(0x00FF00FF));
        IntPtr p88bg = Cases.Winforms1.SendMessageW(hRich88, EM_GETBKGNDCOLOR, IntPtr.Zero, IntPtr.Zero);
        int p88bgv = (int)p88bg.ToInt64();
        Console.WriteLine("p88_bg=" + p88bgv);
        bool p88destroyed = DestroyWindow(hRich88) != 0;
        Console.WriteLine("p88_destroyed=" + p88destroyed);

        // ===================== Phase 89: StatusBar simple mode =====================
        // SB_SIMPLE (0x409) toggles the single-part simple mode (wParam =
        // TRUE/FALSE). PROBED headless both calls return 0 (the return is
        // not a previous-state flag in this build) -- observed values.
        int p89s0 = (int)Cases.Winforms1.SendMessageW(hStatus, SB_SIMPLE, new IntPtr(1), IntPtr.Zero).ToInt64();
        Console.WriteLine("p89_set_ret=" + p89s0);
        int p89s1 = (int)Cases.Winforms1.SendMessageW(hStatus, SB_SIMPLE, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p89_clear_ret=" + p89s1);

        // ===================== Phase 90: ListView label-edit notification =====================
        // The label-edit-start notification (NMHDR.code = LVN_BEGINLABELEDITW,
        // -105; there is no LVN_DBLCLK -- a real double-click is the
        // LVN_ITEMACTIVATE of Phase 92). Driven SYNCHRONOUSLY:
        // SendMessageW(hForm, WM_NOTIFY, list-id, NMHDR-buffer) calls the form's
        // WndProc directly (no message pump, no hang risk -- the Phase-64
        // embedded-pump approach hung). The WndProc reads the code and fires
        // the WfListView event.
        IntPtr hList90 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 37), hModule, IntPtr.Zero);
        P90List = new WfListView();
        P90List.Handle = hList90;
        P90List.Id = ID_FIND49 + 37;
        P90List.DoubleClick += delegate { P90DblFired++; };
        IntPtr p90nm = Marshal.AllocHGlobal(16);
        int[] p90nmh = new int[4];
        p90nmh[3] = LVN_DBLCLK;
        Marshal.Copy(p90nmh, 0, p90nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P90List.Id), p90nm);
        Marshal.FreeHGlobal(p90nm);
        Console.WriteLine("p90_dbl_fired=" + P90DblFired);
        Console.WriteLine("p90_fired=" + (P90DblFired == 1));
        bool p90destroyed = DestroyWindow(hList90) != 0;
        Console.WriteLine("p90_destroyed=" + p90destroyed);

        // ===================== Phase 91: ListView column click (LVN_COLUMNCLICK) =====================
        // A click on a list header makes the list send its parent a WM_NOTIFY
        // whose NMHDR.code = LVN_COLUMNCLICK (-108). Driven SYNCHRONOUSLY via
        // SendMessageW (same as Phase 90) -- the WndProc reads the code and
        // fires the WfListView.ColumnClick event.
        IntPtr hList91 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 38), hModule, IntPtr.Zero);
        P91List = new WfListView();
        P91List.Handle = hList91;
        P91List.Id = ID_FIND49 + 38;
        P91List.ColumnClick += delegate { P91ColFired++; };
        IntPtr p91nm = Marshal.AllocHGlobal(16);
        int[] p91nmh = new int[4];
        p91nmh[3] = LVN_COLUMNCLICK;
        Marshal.Copy(p91nmh, 0, p91nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P91List.Id), p91nm);
        Marshal.FreeHGlobal(p91nm);
        Console.WriteLine("p91_col_fired=" + P91ColFired);
        Console.WriteLine("p91_fired=" + (P91ColFired == 1));
        bool p91destroyed = DestroyWindow(hList91) != 0;
        Console.WriteLine("p91_destroyed=" + p91destroyed);

        // ===================== Phase 92: ListView item activate (LVN_ITEMACTIVATE) =====================
        // An activated list item makes the list send its parent a WM_NOTIFY
        // whose NMHDR.code = LVN_ITEMACTIVATE (-114). Driven SYNCHRONOUSLY via
        // SendMessageW (same as Phase 90/91) -- the WndProc reads the code and
        // fires the WfListView.ItemActivate event.
        IntPtr hList92 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 39), hModule, IntPtr.Zero);
        P92List = new WfListView();
        P92List.Handle = hList92;
        P92List.Id = ID_FIND49 + 39;
        P92List.ItemActivate += delegate { P92ActFired++; };
        IntPtr p92nm = Marshal.AllocHGlobal(16);
        int[] p92nmh = new int[4];
        p92nmh[3] = LVN_ITEMACTIVATE;
        Marshal.Copy(p92nmh, 0, p92nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P92List.Id), p92nm);
        Marshal.FreeHGlobal(p92nm);
        Console.WriteLine("p92_act_fired=" + P92ActFired);
        Console.WriteLine("p92_fired=" + (P92ActFired == 1));
        bool p92destroyed = DestroyWindow(hList92) != 0;
        Console.WriteLine("p92_destroyed=" + p92destroyed);

        // ===================== Phase 93: ListView label-edit end (LVN_ENDLABELEDITW) =====================
        // The label-edit-end notification (NMHDR.code = LVN_ENDLABELEDITW,
        // -106). Driven SYNCHRONOUSLY via SendMessageW (same as 90-92) -- the
        // WndProc reads the code and fires the WfListView.LabelEditEnd event.
        IntPtr hList93 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 40), hModule, IntPtr.Zero);
        P93List = new WfListView();
        P93List.Handle = hList93;
        P93List.Id = ID_FIND49 + 40;
        P93List.LabelEditEnd += delegate { P93EndFired++; };
        IntPtr p93nm = Marshal.AllocHGlobal(16);
        int[] p93nmh = new int[4];
        p93nmh[3] = LVN_ENDLABELEDITW;
        Marshal.Copy(p93nmh, 0, p93nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P93List.Id), p93nm);
        Marshal.FreeHGlobal(p93nm);
        Console.WriteLine("p93_end_fired=" + P93EndFired);
        Console.WriteLine("p93_fired=" + (P93EndFired == 1));
        bool p93destroyed = DestroyWindow(hList93) != 0;
        Console.WriteLine("p93_destroyed=" + p93destroyed);

        // ===================== Phase 94: ListView drag start (LVN_BEGINDRAG) =====================
        // The drag-start notification (NMHDR.code = LVN_BEGINDRAG, -109).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-93) -- the WndProc
        // reads the code and fires the WfListView.BeginDrag event.
        IntPtr hList94 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 41), hModule, IntPtr.Zero);
        P94List = new WfListView();
        P94List.Handle = hList94;
        P94List.Id = ID_FIND49 + 41;
        P94List.BeginDrag += delegate { P94DragFired++; };
        IntPtr p94nm = Marshal.AllocHGlobal(16);
        int[] p94nmh = new int[4];
        p94nmh[3] = LVN_BEGINDRAG;
        Marshal.Copy(p94nmh, 0, p94nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P94List.Id), p94nm);
        Marshal.FreeHGlobal(p94nm);
        Console.WriteLine("p94_drag_fired=" + P94DragFired);
        Console.WriteLine("p94_fired=" + (P94DragFired == 1));
        bool p94destroyed = DestroyWindow(hList94) != 0;
        Console.WriteLine("p94_destroyed=" + p94destroyed);

        // ===================== Phase 95: ListView right-drag start (LVN_BEGINRDRAG) =====================
        // The right-button drag-start notification (NMHDR.code =
        // LVN_BEGINRDRAG, -110). Driven SYNCHRONOUSLY via SendMessageW (same
        // as 90-94) -- the WndProc reads the code and fires the
        // WfListView.BeginRDrag event.
        IntPtr hList95 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 42), hModule, IntPtr.Zero);
        P95List = new WfListView();
        P95List.Handle = hList95;
        P95List.Id = ID_FIND49 + 42;
        P95List.BeginRDrag += delegate { P95RDragFired++; };
        IntPtr p95nm = Marshal.AllocHGlobal(16);
        int[] p95nmh = new int[4];
        p95nmh[3] = LVN_BEGINRDRAG;
        Marshal.Copy(p95nmh, 0, p95nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P95List.Id), p95nm);
        Marshal.FreeHGlobal(p95nm);
        Console.WriteLine("p95_rdrag_fired=" + P95RDragFired);
        Console.WriteLine("p95_fired=" + (P95RDragFired == 1));
        bool p95destroyed = DestroyWindow(hList95) != 0;
        Console.WriteLine("p95_destroyed=" + p95destroyed);

        // ===================== Phase 96: ListView key down (LVN_KEYDOWN) =====================
        // A key press on the focused list sends the parent a WM_NOTIFY whose
        // NMHDR.code = LVN_KEYDOWN (-155). Driven SYNCHRONOUSLY via
        // SendMessageW (same as 90-95) -- the WndProc reads the code and fires
        // the WfListView.KeyDownNotify event.
        IntPtr hList96 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 43), hModule, IntPtr.Zero);
        P96List = new WfListView();
        P96List.Handle = hList96;
        P96List.Id = ID_FIND49 + 43;
        P96List.KeyDownNotify += delegate { P96KeyFired++; };
        IntPtr p96nm = Marshal.AllocHGlobal(16);
        int[] p96nmh = new int[4];
        p96nmh[3] = LVN_KEYDOWN;
        Marshal.Copy(p96nmh, 0, p96nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P96List.Id), p96nm);
        Marshal.FreeHGlobal(p96nm);
        Console.WriteLine("p96_key_fired=" + P96KeyFired);
        Console.WriteLine("p96_fired=" + (P96KeyFired == 1));
        bool p96destroyed = DestroyWindow(hList96) != 0;
        Console.WriteLine("p96_destroyed=" + p96destroyed);

        // ===================== Phase 97: ListView item insert (LVN_INSERTITEM) =====================
        // The item-insert notification (NMHDR.code = LVN_INSERTITEM, -102).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-96) -- the WndProc
        // reads the code and fires the WfListView.ItemInserted event.
        IntPtr hList97 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 44), hModule, IntPtr.Zero);
        P97List = new WfListView();
        P97List.Handle = hList97;
        P97List.Id = ID_FIND49 + 44;
        P97List.ItemInserted += delegate { P97InsFired++; };
        IntPtr p97nm = Marshal.AllocHGlobal(16);
        int[] p97nmh = new int[4];
        p97nmh[3] = LVN_INSERTITEM;
        Marshal.Copy(p97nmh, 0, p97nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P97List.Id), p97nm);
        Marshal.FreeHGlobal(p97nm);
        Console.WriteLine("p97_ins_fired=" + P97InsFired);
        Console.WriteLine("p97_fired=" + (P97InsFired == 1));
        bool p97destroyed = DestroyWindow(hList97) != 0;
        Console.WriteLine("p97_destroyed=" + p97destroyed);

        // ===================== Phase 98: ListView item delete (LVN_DELETEITEM) =====================
        // The item-delete notification (NMHDR.code = LVN_DELETEITEM, -103).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-97) -- the WndProc
        // reads the code and fires the WfListView.ItemDeleted event.
        IntPtr hList98 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 45), hModule, IntPtr.Zero);
        P98List = new WfListView();
        P98List.Handle = hList98;
        P98List.Id = ID_FIND49 + 45;
        P98List.ItemDeleted += delegate { P98DelFired++; };
        IntPtr p98nm = Marshal.AllocHGlobal(16);
        int[] p98nmh = new int[4];
        p98nmh[3] = LVN_DELETEITEM;
        Marshal.Copy(p98nmh, 0, p98nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P98List.Id), p98nm);
        Marshal.FreeHGlobal(p98nm);
        Console.WriteLine("p98_del_fired=" + P98DelFired);
        Console.WriteLine("p98_fired=" + (P98DelFired == 1));
        bool p98destroyed = DestroyWindow(hList98) != 0;
        Console.WriteLine("p98_destroyed=" + p98destroyed);

        // ===================== Phase 99: ListView delete-all (LVN_DELETEALLITEMS) =====================
        // The delete-all notification (NMHDR.code = LVN_DELETEALLITEMS, -104).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-98) -- the WndProc
        // reads the code and fires the WfListView.AllItemsDeleted event.
        IntPtr hList99 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 46), hModule, IntPtr.Zero);
        P99List = new WfListView();
        P99List.Handle = hList99;
        P99List.Id = ID_FIND49 + 46;
        P99List.AllItemsDeleted += delegate { P99AllFired++; };
        IntPtr p99nm = Marshal.AllocHGlobal(16);
        int[] p99nmh = new int[4];
        p99nmh[3] = LVN_DELETEALLITEMS;
        Marshal.Copy(p99nmh, 0, p99nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P99List.Id), p99nm);
        Marshal.FreeHGlobal(p99nm);
        Console.WriteLine("p99_all_fired=" + P99AllFired);
        Console.WriteLine("p99_fired=" + (P99AllFired == 1));
        bool p99destroyed = DestroyWindow(hList99) != 0;
        Console.WriteLine("p99_destroyed=" + p99destroyed);

        // ===================== Phase 100: ListView item changed (LVN_ITEMCHANGED) =====================
        // The item-change notification (NMHDR.code = LVN_ITEMCHANGED, -101)
        // -- the synchronous variant of the Phase-21 SelectionChanged.
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-99) -- the WndProc
        // reads the code and fires the WfListView.ItemChanged event.
        IntPtr hList100 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 47), hModule, IntPtr.Zero);
        P100List = new WfListView();
        P100List.Handle = hList100;
        P100List.Id = ID_FIND49 + 47;
        P100List.ItemChanged += delegate { P100ChgFired++; };
        IntPtr p100nm = Marshal.AllocHGlobal(16);
        int[] p100nmh = new int[4];
        p100nmh[3] = LVN_ITEMCHANGED;
        Marshal.Copy(p100nmh, 0, p100nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P100List.Id), p100nm);
        Marshal.FreeHGlobal(p100nm);
        Console.WriteLine("p100_chg_fired=" + P100ChgFired);
        Console.WriteLine("p100_fired=" + (P100ChgFired == 1));
        bool p100destroyed = DestroyWindow(hList100) != 0;
        Console.WriteLine("p100_destroyed=" + p100destroyed);

        // ===================== Phase 101: ListView item changing (LVN_ITEMCHANGING) =====================
        // The before-change notification (NMHDR.code = LVN_ITEMCHANGING, -100).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-100) -- the WndProc
        // reads the code and fires the WfListView.ItemChanging event.
        IntPtr hList101 = CreateWindowExW(0, WC_LISTVIEW, "", WS_CHILD | WS_VISIBLE | LVS_REPORT,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 48), hModule, IntPtr.Zero);
        P101List = new WfListView();
        P101List.Handle = hList101;
        P101List.Id = ID_FIND49 + 48;
        P101List.ItemChanging += delegate { P101ChgFired++; };
        IntPtr p101nm = Marshal.AllocHGlobal(16);
        int[] p101nmh = new int[4];
        p101nmh[3] = LVN_ITEMCHANGING;
        Marshal.Copy(p101nmh, 0, p101nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P101List.Id), p101nm);
        Marshal.FreeHGlobal(p101nm);
        Console.WriteLine("p101_chg_fired=" + P101ChgFired);
        Console.WriteLine("p101_fired=" + (P101ChgFired == 1));
        bool p101destroyed = DestroyWindow(hList101) != 0;
        Console.WriteLine("p101_destroyed=" + p101destroyed);

        // ===================== Phase 102: TreeView selection change (TVN_SELCHANGED) =====================
        // The tree selection-change notification (NMHDR.code = TVN_SELCHANGED,
        // -402). Driven SYNCHRONOUSLY via SendMessageW (same as 90-101) -- the
        // WndProc reads the code and fires the WfTreeView.SelChangedSync event.
        IntPtr hTree102 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 49), hModule, IntPtr.Zero);
        P102Tree = new WfTreeView();
        P102Tree.Handle = hTree102;
        P102Tree.Id = ID_FIND49 + 49;
        P102Tree.SelChangedSync += delegate { P102SelFired++; };
        IntPtr p102nm = Marshal.AllocHGlobal(16);
        int[] p102nmh = new int[4];
        p102nmh[3] = TVN_SELCHANGED;
        Marshal.Copy(p102nmh, 0, p102nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P102Tree.Id), p102nm);
        Marshal.FreeHGlobal(p102nm);
        Console.WriteLine("p102_sel_fired=" + P102SelFired);
        Console.WriteLine("p102_fired=" + (P102SelFired == 1));
        bool p102destroyed = DestroyWindow(hTree102) != 0;
        Console.WriteLine("p102_destroyed=" + p102destroyed);

        // ===================== Phase 103: TreeView selection changing (TVN_SELCHANGING) =====================
        // The before-selection-change notification (NMHDR.code =
        // TVN_SELCHANGING, -401). Driven SYNCHRONOUSLY via SendMessageW (same
        // as 90-102) -- the WndProc reads the code and fires the
        // WfTreeView.SelChangingSync event.
        IntPtr hTree103 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 50), hModule, IntPtr.Zero);
        P103Tree = new WfTreeView();
        P103Tree.Handle = hTree103;
        P103Tree.Id = ID_FIND49 + 50;
        P103Tree.SelChangingSync += delegate { P103SelFired++; };
        IntPtr p103nm = Marshal.AllocHGlobal(16);
        int[] p103nmh = new int[4];
        p103nmh[3] = TVN_SELCHANGING;
        Marshal.Copy(p103nmh, 0, p103nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P103Tree.Id), p103nm);
        Marshal.FreeHGlobal(p103nm);
        Console.WriteLine("p103_sel_fired=" + P103SelFired);
        Console.WriteLine("p103_fired=" + (P103SelFired == 1));
        bool p103destroyed = DestroyWindow(hTree103) != 0;
        Console.WriteLine("p103_destroyed=" + p103destroyed);

        // ===================== Phase 104: TreeView item expanded (TVN_ITEMEXPANDED) =====================
        // The post-expand notification (NMHDR.code = TVN_ITEMEXPANDED, -406).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-103) -- the WndProc
        // reads the code and fires the WfTreeView.ItemExpandedSync event.
        IntPtr hTree104 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 51), hModule, IntPtr.Zero);
        P104Tree = new WfTreeView();
        P104Tree.Handle = hTree104;
        P104Tree.Id = ID_FIND49 + 51;
        P104Tree.ItemExpandedSync += delegate { P104ExpFired++; };
        IntPtr p104nm = Marshal.AllocHGlobal(16);
        int[] p104nmh = new int[4];
        p104nmh[3] = TVN_ITEMEXPANDED;
        Marshal.Copy(p104nmh, 0, p104nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P104Tree.Id), p104nm);
        Marshal.FreeHGlobal(p104nm);
        Console.WriteLine("p104_exp_fired=" + P104ExpFired);
        Console.WriteLine("p104_fired=" + (P104ExpFired == 1));
        bool p104destroyed = DestroyWindow(hTree104) != 0;
        Console.WriteLine("p104_destroyed=" + p104destroyed);

        // ===================== Phase 105: TreeView item expanding (TVN_ITEMEXPANDING) =====================
        // The pre-expand notification (NMHDR.code = TVN_ITEMEXPANDING, -405).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-104) -- the WndProc
        // reads the code and fires the WfTreeView.ItemExpandingSync event.
        IntPtr hTree105 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 52), hModule, IntPtr.Zero);
        P105Tree = new WfTreeView();
        P105Tree.Handle = hTree105;
        P105Tree.Id = ID_FIND49 + 52;
        P105Tree.ItemExpandingSync += delegate { P105ExpFired++; };
        IntPtr p105nm = Marshal.AllocHGlobal(16);
        int[] p105nmh = new int[4];
        p105nmh[3] = TVN_ITEMEXPANDING;
        Marshal.Copy(p105nmh, 0, p105nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P105Tree.Id), p105nm);
        Marshal.FreeHGlobal(p105nm);
        Console.WriteLine("p105_exp_fired=" + P105ExpFired);
        Console.WriteLine("p105_fired=" + (P105ExpFired == 1));
        bool p105destroyed = DestroyWindow(hTree105) != 0;
        Console.WriteLine("p105_destroyed=" + p105destroyed);

        // ===================== Phase 106: TreeView item collapsed (TVN_ITEMCOLLAPSED) =====================
        // The post-collapse notification (NMHDR.code = TVN_ITEMCOLLAPSED, -408).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-105) -- the WndProc
        // reads the code and fires the WfTreeView.ItemCollapsedSync event.
        IntPtr hTree106 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 53), hModule, IntPtr.Zero);
        P106Tree = new WfTreeView();
        P106Tree.Handle = hTree106;
        P106Tree.Id = ID_FIND49 + 53;
        P106Tree.ItemCollapsedSync += delegate { P106ColFired++; };
        IntPtr p106nm = Marshal.AllocHGlobal(16);
        int[] p106nmh = new int[4];
        p106nmh[3] = TVN_ITEMCOLLAPSED;
        Marshal.Copy(p106nmh, 0, p106nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P106Tree.Id), p106nm);
        Marshal.FreeHGlobal(p106nm);
        Console.WriteLine("p106_col_fired=" + P106ColFired);
        Console.WriteLine("p106_fired=" + (P106ColFired == 1));
        bool p106destroyed = DestroyWindow(hTree106) != 0;
        Console.WriteLine("p106_destroyed=" + p106destroyed);

        // ===================== Phase 107: TreeView item collapsing (TVN_ITEMCOLLAPSING) =====================
        // The pre-collapse notification (NMHDR.code = TVN_ITEMCOLLAPSING, -407).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-106) -- the WndProc
        // reads the code and fires the WfTreeView.ItemCollapsingSync event.
        IntPtr hTree107 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 54), hModule, IntPtr.Zero);
        P107Tree = new WfTreeView();
        P107Tree.Handle = hTree107;
        P107Tree.Id = ID_FIND49 + 54;
        P107Tree.ItemCollapsingSync += delegate { P107ColFired++; };
        IntPtr p107nm = Marshal.AllocHGlobal(16);
        int[] p107nmh = new int[4];
        p107nmh[3] = TVN_ITEMCOLLAPSING;
        Marshal.Copy(p107nmh, 0, p107nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P107Tree.Id), p107nm);
        Marshal.FreeHGlobal(p107nm);
        Console.WriteLine("p107_col_fired=" + P107ColFired);
        Console.WriteLine("p107_fired=" + (P107ColFired == 1));
        bool p107destroyed = DestroyWindow(hTree107) != 0;
        Console.WriteLine("p107_destroyed=" + p107destroyed);

        // ===================== Phase 108: TreeView label-edit start (TVN_BEGINLABELEDIT) =====================
        // The label-edit-start notification (NMHDR.code = TVN_BEGINLABELEDIT,
        // -410). Driven SYNCHRONOUSLY via SendMessageW (same as 90-107) -- the
        // WndProc reads the code and fires the WfTreeView.BeginLabelEditSync
        // event.
        IntPtr hTree108 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 55), hModule, IntPtr.Zero);
        P108Tree = new WfTreeView();
        P108Tree.Handle = hTree108;
        P108Tree.Id = ID_FIND49 + 55;
        P108Tree.BeginLabelEditSync += delegate { P108BegFired++; };
        IntPtr p108nm = Marshal.AllocHGlobal(16);
        int[] p108nmh = new int[4];
        p108nmh[3] = TVN_BEGINLABELEDIT;
        Marshal.Copy(p108nmh, 0, p108nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P108Tree.Id), p108nm);
        Marshal.FreeHGlobal(p108nm);
        Console.WriteLine("p108_beg_fired=" + P108BegFired);
        Console.WriteLine("p108_fired=" + (P108BegFired == 1));
        bool p108destroyed = DestroyWindow(hTree108) != 0;
        Console.WriteLine("p108_destroyed=" + p108destroyed);

        // ===================== Phase 109: TreeView label-edit end (TVN_ENDLABELEDIT) =====================
        // The label-edit-end notification (NMHDR.code = TVN_ENDLABELEDIT, -411).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-108) -- the WndProc
        // reads the code and fires the WfTreeView.EndLabelEditSync event.
        IntPtr hTree109 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 56), hModule, IntPtr.Zero);
        P109Tree = new WfTreeView();
        P109Tree.Handle = hTree109;
        P109Tree.Id = ID_FIND49 + 56;
        P109Tree.EndLabelEditSync += delegate { P109EndFired++; };
        IntPtr p109nm = Marshal.AllocHGlobal(16);
        int[] p109nmh = new int[4];
        p109nmh[3] = TVN_ENDLABELEDIT;
        Marshal.Copy(p109nmh, 0, p109nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P109Tree.Id), p109nm);
        Marshal.FreeHGlobal(p109nm);
        Console.WriteLine("p109_end_fired=" + P109EndFired);
        Console.WriteLine("p109_fired=" + (P109EndFired == 1));
        bool p109destroyed = DestroyWindow(hTree109) != 0;
        Console.WriteLine("p109_destroyed=" + p109destroyed);

        // ===================== Phase 110: TreeView item delete (TVN_DELETEITEM) =====================
        // The item-delete notification (NMHDR.code = TVN_DELETEITEM, -413).
        // Driven SYNCHRONOUSLY via SendMessageW (same as 90-109) -- the WndProc
        // reads the code and fires the WfTreeView.ItemDeletedSync event.
        IntPtr hTree110 = CreateWindowExW(0, WC_TREEVIEW, "", WS_CHILD | WS_VISIBLE,
            0, 0, 300, 160, hForm, (IntPtr)(ID_FIND49 + 57), hModule, IntPtr.Zero);
        P110Tree = new WfTreeView();
        P110Tree.Handle = hTree110;
        P110Tree.Id = ID_FIND49 + 57;
        P110Tree.ItemDeletedSync += delegate { P110DelFired++; };
        IntPtr p110nm = Marshal.AllocHGlobal(16);
        int[] p110nmh = new int[4];
        p110nmh[3] = TVN_DELETEITEM;
        Marshal.Copy(p110nmh, 0, p110nm, 4);
        Cases.Winforms1.SendMessageW(hForm, WM_NOTIFY, new IntPtr(P110Tree.Id), p110nm);
        Marshal.FreeHGlobal(p110nm);
        Console.WriteLine("p110_del_fired=" + P110DelFired);
        Console.WriteLine("p110_fired=" + (P110DelFired == 1));
        bool p110destroyed = DestroyWindow(hTree110) != 0;
        Console.WriteLine("p110_destroyed=" + p110destroyed);

        // Bounded pump + timeout sentinel: at most 200 dispatches.
        MSG msg;
        int dispatched = 0;
        while (GetMessageW(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
            dispatched++;
            if (dispatched >= 200) { pumpExitReason = "cap"; break; }
        }
        if (pumpExitReason == "never") pumpExitReason = "quit";

        Console.WriteLine("wm_command_count_after=" + wmCommandCount);
        Console.WriteLine("click_count=" + clickCount);
        Console.WriteLine("wm_paint_count=" + wmPaintCount);
        Console.WriteLine("begin_paint_ok=" + beginPaintOk);
        Console.WriteLine("textout_ok=" + textOutOk);
        Console.WriteLine("painted_text=" + paintedText);
        Console.WriteLine("p14_font_created=" + P14FontCreated);
        Console.WriteLine("p14_select_ok=" + P14SelectOk);
        Console.WriteLine("p14_draw_ok=" + P14DrawOk);
        Console.WriteLine("p14_draw_height=" + P14DrawHeight);
        Console.WriteLine("p14_ext_ok=" + P14ExtOk);
        Console.WriteLine("p14_ext_cx=" + P14ExtCx);
        Console.WriteLine("p14_ext_cy=" + P14ExtCy);
        Console.WriteLine("p14_restore_ok=" + P14RestoreOk);
        Console.WriteLine("p14_delete_ok=" + P14DeleteOk);
        Console.WriteLine("p14_text=Wf14Draw");
        Console.WriteLine("p14_seq=" + P14Seq);
        Console.WriteLine("wm_close_count=" + wmCloseCount);
        Console.WriteLine("close_destroyed=" + closeDestroyed);
        Console.WriteLine("pump_exit_reason=" + pumpExitReason);

        // Control state dump (plan §4.3): structure/counts only, no raw handles.
        Console.WriteLine("dump_form_nonzero=" + (hForm != IntPtr.Zero));
        Console.WriteLine("dump_button_nonzero=" + (hButton != IntPtr.Zero));
        Console.WriteLine("dump_label_nonzero=" + (hLabel != IntPtr.Zero));
        Console.WriteLine("dump_commands=" + wmCommandCount);
        Console.WriteLine("dump_clicks=" + clickCount);
    }
}}

// Phase 4 managed Control model: per-instance event fields (Click/KeyDown)
// with custom add/remove accessors that route through Delegate.Combine/Remove
// -- the same multicast semantics the runtime already implements
// (icall_delegate_combine/remove + icall_multicast_invoke_i4, delegateext.cpp).
public class WfControl
{
    public int Id;
    public string Name;
    private Cases.Winforms1.ClickHandler _click;
    private Cases.Winforms1.ClickHandler _keydown;

    public event Cases.Winforms1.ClickHandler Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public event Cases.Winforms1.ClickHandler KeyDown
    {
        add { _keydown = _keydown + value; }
        remove { _keydown = _keydown - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    public int KeyCount
    {
        get { return _keydown == null ? 0 : _keydown.GetInvocationList().Length; }
    }

    public void RaiseClick() { if (_click != null) _click(); }
    public void RaiseKeyDown() { if (_keydown != null) _keydown(); }
}

// Phase 7 managed Control model: PROPERTY-based access to the native control
// state. WfPropControl wraps a raw HWND and exposes the WinForms
// control-property surface as C# get/set properties that translate to the
// Win32 control channel:
//   Text    get -> GetWindowTextW;        set -> WM_SETTEXT (SendMessageW)
//   Visible get -> IsWindowVisible;       set -> ShowWindow (SW_SHOW/SW_HIDE)
//   Enabled get -> IsWindowEnabled;       set -> EnableWindow
// The Click event + Enabled combination: RaiseClick GATES on Enabled -- a
// disabled control receives no click (the WinForms rule) -- so a Click
// handler that toggles Enabled arms/disarms the next dispatch. All the
// marshaled calls are the same wired paths as the earlier phases, so the
// property get/set sequences are byte-exact across net48 + both compilers.
public class WfPropControl
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _click;

    public string Text
    {
        get
        {
            IntPtr buf = Marshal.AllocHGlobal(256);
            int len = Cases.Winforms1.GetWindowTextW(Handle, buf, 256);
            string s = Marshal.PtrToStringUni(buf);
            Marshal.FreeHGlobal(buf);
            return s;
        }
        set
        {
            IntPtr p = Marshal.StringToHGlobalUni(value);
            Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_SETTEXT, IntPtr.Zero, p);
            Marshal.FreeHGlobal(p);
        }
    }

    public bool Visible
    {
        get { return Cases.Winforms1.IsWindowVisible(Handle) != 0; }
        set { Cases.Winforms1.ShowWindow(Handle, value ? Cases.Winforms1.SW_SHOW : Cases.Winforms1.SW_HIDE); }
    }

    public bool Enabled
    {
        get { return Cases.Winforms1.IsWindowEnabled(Handle) != 0; }
        set { Cases.Winforms1.EnableWindow(Handle, value ? 1 : 0); }
    }

    public event Cases.Winforms1.ClickHandler Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public void RaiseClick() { if (Enabled && _click != null) _click(); }
}

// Phase 19 managed model: GROUP BOX container. WfGroupBox wraps a native
// BS_GROUPBOX STATIC-frame window (a "BUTTON" class WS_CHILD with the
// BS_GROUPBOX style -- the WinForms GroupBox surface, simplified) and
// exposes: Text (WM_SETTEXT + GetWindowTextW round-trip, the group title),
// Handle, Id. The container OWNS its child controls (the radios' parent is
// the GroupBox -- GetParent-proved in the Phase 19 block). The container's
// OWN event surface is the subclassed WfGroupBoxWndProcImpl proc (the
// phase-15 panel pattern): it receives a posted WF_GROUP_MSG and BUBBLES
// WF_GROUP_NOTIFY to the form.
public class WfGroupBox
{
    public IntPtr Handle;
    public int Id;

    public string Text
    {
        get
        {
            IntPtr buf = Marshal.AllocHGlobal(256);
            int len = Cases.Winforms1.GetWindowTextW(Handle, buf, 256);
            string s = Marshal.PtrToStringUni(buf);
            Marshal.FreeHGlobal(buf);
            return s;
        }
        set
        {
            IntPtr p = Marshal.StringToHGlobalUni(value);
            Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_SETTEXT, IntPtr.Zero, p);
            Marshal.FreeHGlobal(p);
        }
    }
}

// Phase 8 managed model: DIALOG CONTROLS (ComboBox / ListBox). WfDialogControl
// wraps a native COMBOBOX/LISTBOX HWND and exposes the ListControl surface:
//   Add(text)         -> CB_ADDSTRING / LB_ADDSTRING (returns the item index)
//   Count             -> CB_GETCOUNT / LB_GETCOUNT
//   SelectedIndex     -> CB_GETCURSEL/CB_SETCURSEL / LB_GETCURSEL/LB_SETCURSEL
//                        (CB_ERR / LB_ERR = -1 when nothing is selected)
//   GetItemText(i)    -> CB_GETLBTEXT / LB_GETTEXT (IntPtr buffer + PtrToStringUni)
//   SelectionChanged event -> raised by the form's WM_COMMAND dispatch when
//     HIWORD(wParam) == CBN_SELCHANGE / LBN_SELCHANGE (the notification the
//     native control sends its parent when the user changes the selection).
// Kind selects the channel: 1 = COMBOBOX, 2 = LISTBOX. All marshaled calls are
// the same wired paths as the earlier phases, so the sequences are byte-exact
// across net48 + both compilers.
public class WfDialogControl
{
    public IntPtr Handle;
    public int Id;
    public int Kind;
    private Cases.Winforms1.ClickHandler _selchange;

    public event Cases.Winforms1.ClickHandler SelectionChanged
    {
        add { _selchange = _selchange + value; }
        remove { _selchange = _selchange - value; }
    }

    public int SelCount
    {
        get { return _selchange == null ? 0 : _selchange.GetInvocationList().Length; }
    }

    private int AddMsg() { return Kind == 1 ? Cases.Winforms1.CB_ADDSTRING : Cases.Winforms1.LB_ADDSTRING; }
    private int GetCountMsg() { return Kind == 1 ? Cases.Winforms1.CB_GETCOUNT : Cases.Winforms1.LB_GETCOUNT; }
    private int GetSelMsg() { return Kind == 1 ? Cases.Winforms1.CB_GETCURSEL : Cases.Winforms1.LB_GETCURSEL; }
    private int SetSelMsg() { return Kind == 1 ? Cases.Winforms1.CB_SETCURSEL : Cases.Winforms1.LB_SETCURSEL; }
    private int GetTextMsg() { return Kind == 1 ? Cases.Winforms1.CB_GETLBTEXT : Cases.Winforms1.LB_GETTEXT; }

    public int Add(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, AddMsg(), IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, GetCountMsg(), IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public int SelectedIndex
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, GetSelMsg(), IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, SetSelMsg(), new IntPtr(value), IntPtr.Zero); }
    }

    public string GetItemText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Winforms1.SendMessageW(Handle, GetTextMsg(), new IntPtr(index), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public void RaiseSelectionChanged() { if (_selchange != null) _selchange(); }
}

// Phase 37 managed model: the ListBox ITEM-MANAGEMENT surface. WfListBox wraps
// a native LISTBOX HWND (the Phase-8 channel -- no new P/Invoke / shim / struct;
// every call goes through the existing SendMessageW) and exposes the item
// lifecycle the Phase-8 WfDialogControl never touched (add / select / text):
//   Insert(index, text)  -> LB_INSERTSTRING (returns the inserted index)
//   Delete(index)        -> LB_DELETESTRING (returns the REMAINING count)
//   Clear()              -> LB_RESETCONTENT
//   GetItemTextLen(i)    -> LB_GETTEXTLEN (LB_ERR = -1 when out of range)
//   SetItemData(i, v)    -> LB_SETITEMDATA (net48 PROBED: returns TRUE (1) on
//                            success -- NOT the previous value; the design's
//                            prev-value oracle is superseded by the measured
//                            behavior, both runs call the same native message)
//   GetItemData(i)       -> LB_GETITEMDATA (the value just set)
//   FindString(start,s)  -> LB_FINDSTRING (prefix match, wraps after `start`;
//                            -1 = search from 0; LB_ERR = -1 when no match)
//   TopIndex             -> LB_GETTOPINDEX / LB_SETTOPINDEX (scroll to row)
//   DoubleClick event    -> raised by the form's WM_COMMAND dispatch when
//     HIWORD(wParam) == LBN_DBLCLK (the notification a real double-click sends
//     the parent); RaiseDoubleClick appends "dbl<sel>;" to P37Seq (the
//     selection is RE-READ at fire time -- the native double-click handler
//     shape) and invokes the multicast handlers.
public class WfListBox
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _dblclick;

    public event Cases.Winforms1.ClickHandler DoubleClick
    {
        add { _dblclick = _dblclick + value; }
        remove { _dblclick = _dblclick - value; }
    }

    public int DblClickCount
    {
        get { return _dblclick == null ? 0 : _dblclick.GetInvocationList().Length; }
    }

    public int Add(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_ADDSTRING, IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Insert(int index, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_INSERTSTRING, new IntPtr(index), p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public int SelectedIndex
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_SETCURSEL, new IntPtr(value), IntPtr.Zero); }
    }

    public string GetItemText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_GETTEXT, new IntPtr(index), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public int GetItemTextLen(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_GETTEXTLEN, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int Delete(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_DELETESTRING, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public void Clear()
    {
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
    }

    public int SetItemData(int index, int value)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_SETITEMDATA, new IntPtr(index), new IntPtr(value));
        return (int)r.ToInt64();
    }

    public int GetItemData(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_GETITEMDATA, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int FindString(int startIndex, string prefix)
    {
        IntPtr p = Marshal.StringToHGlobalUni(prefix);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_FINDSTRING, new IntPtr(startIndex), p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int TopIndex
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LB_SETTOPINDEX, new IntPtr(value), IntPtr.Zero); }
    }

    public void RaiseDoubleClick()
    {
        if (_dblclick != null) _dblclick();
        Cases.Winforms1.P37Seq = Cases.Winforms1.P37Seq + "dbl" + SelectedIndex + ";";
    }
}

// Phase 38 managed model: the ComboBox ITEM-MANAGEMENT surface. WfComboBox
// wraps a native COMBOBOX HWND (the Phase-8 channel -- no new P/Invoke / shim /
// struct; every call goes through the existing SendMessageW) and exposes the
// SAME item lifecycle as the Phase-37 WfListBox through the CB_* message set
// (a ComboBox is a ListControl with its own message numbers):
//   Insert(index, text)  -> CB_INSERTSTRING (returns the inserted index)
//   Delete(index)        -> CB_DELETESTRING (returns the REMAINING count)
//   Clear()              -> CB_RESETCONTENT
//   GetItemTextLen(i)    -> CB_GETLBTEXTLEN (CB_ERR = -1 when out of range)
//   SetItemData(i, v)    -> CB_SETITEMDATA (net48 PROBED: returns TRUE (1) on
//                            success -- the same measured oracle as
//                            LB_SETITEMDATA; the design's prev-value assumption
//                            is superseded by the measured behavior, both runs
//                            call the same native message)
//   GetItemData(i)       -> CB_GETITEMDATA (the value just set)
//   FindString(start,s)  -> CB_FINDSTRING (prefix match, wraps after `start`;
//                            -1 = search from 0; CB_ERR = -1 when no match)
//   TopIndex             -> CB_GETTOPINDEX / CB_SETTOPINDEX (net48 PROBED: a
//                            CLOSED CBS_DROPDOWNLIST combo stores and re-reads
//                            the top index -- 0 -> set 3 -> read 3 -> set 0 ->
//                            read 0)
//   DoubleClick event    -> raised by the form's WM_COMMAND dispatch when
//     HIWORD(wParam) == CBN_DBLCLK (shares value 2 with LBN_DBLCLK); the
//     RaiseDoubleClick appends "dbl<sel>;" to P38Seq (the selection is RE-READ
//     at fire time -- the native double-click handler shape) and invokes the
//     multicast handlers.
// The mirror closes the ListControl family (Phase 8 covered add/select/text for
// both classes; Phase 37 the ListBox item set; Phase 38 the ComboBox item set).
public class WfComboBox
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _dblclick;

    public event Cases.Winforms1.ClickHandler DoubleClick
    {
        add { _dblclick = _dblclick + value; }
        remove { _dblclick = _dblclick - value; }
    }

    public int DblClickCount
    {
        get { return _dblclick == null ? 0 : _dblclick.GetInvocationList().Length; }
    }

    public int Add(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_ADDSTRING, IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Insert(int index, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_INSERTSTRING, new IntPtr(index), p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public int SelectedIndex
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_SETCURSEL, new IntPtr(value), IntPtr.Zero); }
    }

    public string GetItemText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_GETLBTEXT, new IntPtr(index), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public int GetItemTextLen(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_GETLBTEXTLEN, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int Delete(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_DELETESTRING, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public void Clear()
    {
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
    }

    public int SetItemData(int index, int value)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_SETITEMDATA, new IntPtr(index), new IntPtr(value));
        return (int)r.ToInt64();
    }

    public int GetItemData(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_GETITEMDATA, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int FindString(int startIndex, string prefix)
    {
        IntPtr p = Marshal.StringToHGlobalUni(prefix);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_FINDSTRING, new IntPtr(startIndex), p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int TopIndex
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.CB_SETTOPINDEX, new IntPtr(value), IntPtr.Zero); }
    }

    public void RaiseDoubleClick()
    {
        if (_dblclick != null) _dblclick();
        Cases.Winforms1.P38Seq = Cases.Winforms1.P38Seq + "dbl" + SelectedIndex + ";";
    }
}

// Phase 40 managed model: the TextBox EDIT deep surface. WfTextBox wraps a
// native user32 EDIT HWND (the Phase-3/7/13 channel -- no new P/Invoke / shim /
// struct; every call goes through the existing SendMessageW) and exposes the
// EM_* edit-message family Phase 3/7/13 never touched (they cover text
// get/set only):
//   SetText / GetTextLength / GetText -> the WM_SETTEXT / WM_GETTEXTLENGTH /
//     WM_GETTEXT channel (the Phase-3 shape)
//   GetLineCount  -> EM_GETLINECOUNT (3 for "LineA\r\nLineB\r\nLineC")
//   LineIndex(n)  -> EM_LINEINDEX (line n's first CHAR index: 0/7/14)
//   LineLength(c) -> EM_LINELENGTH (the length of the line containing char c:
//                    5 each for the three 5-char lines)
//   GetLine(n)    -> EM_GETLINE: the caller buffer's FIRST WORD is the max
//                    chars to copy. net48 PROBED: the control writes the line
//                    AT OFFSET 0, OVERWRITING that length word -- so the text
//                    readback is at offset 0, NOT +2 (the design's "+2 readback"
//                    is superseded by the measured oracle; a +2 read loses the
//                    first char "L"). The pinned int[0] low WORD carries the
//                    length prefix because the wired Marshal surface has no
//                    WriteInt16.
//   LineFromChar(c) -> EM_LINEFROMCHAR (the line containing char c)
//   SelectAll / SelStart / SelEnd -> EM_SETSEL(0,-1) + the packed EM_GETSEL
//                    return (start | end<<16 -- probed 0 | 19<<16)
//   LimitText     -> EM_GETLIMITTEXT / EM_SETLIMITTEXT (multiline default
//                    30000 -- probed; set 20 -> read 20)
//   Modified      -> EM_GETMODIFY / EM_SETMODIFY (dirty flag: set 1 -> read 1)
//   CanUndo / Undo / EmptyUndoBuffer -> EM_CANUNDO / EM_UNDO /
//                    EM_EMPTYUNDOBUFFER (probed: after WM_SETTEXT canundo 0; a
//                    WM_CHAR keystroke creates an entry, canundo 1; EM_UNDO
//                    returns 1 and restores; after undo canundo still 1 -- the
//                    redo state; EMPTYUNDOBUFFER -> canundo 0; EM_UNDO on the
//                    empty buffer returns 0. EM_REPLACESEL does NOT create an
//                    undo entry -- the keystroke is the deterministic way in.)
//   Scroll / ScrollCaret -> EM_SCROLL / EM_SCROLLCARET (probed on a hidden
//                    edit: scroll returns 0, scrollcaret returns 1)
//   TextChanged event -> raised by the form's WM_COMMAND dispatch when
//     HIWORD(wParam) == EN_CHANGE (the text-changed notification a real edit
//     sends its parent; the headless test POSTS it + drains -- the Phase-21
//     posted-notification pattern). RaiseTextChanged appends "change;" to
//     P40Seq and invokes the multicast handlers.
public class WfTextBox
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _textchanged;

    public event Cases.Winforms1.ClickHandler TextChanged
    {
        add { _textchanged = _textchanged + value; }
        remove { _textchanged = _textchanged - value; }
    }

    public int ChangeCount
    {
        get { return _textchanged == null ? 0 : _textchanged.GetInvocationList().Length; }
    }

    public bool SetText(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_SETTEXT, IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        return r != IntPtr.Zero;
    }

    public int GetTextLength()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public string GetText()
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_GETTEXT, new IntPtr(256), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        if (s == null) { s = ""; }
        return s;
    }

    public int GetLineCount()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int LineIndex(int line)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_LINEINDEX, new IntPtr(line), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int LineLength(int charIndex)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_LINELENGTH, new IntPtr(charIndex), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int GetLineCopied(int line)
    {
        int[] gl = new int[32];
        gl[0] = 31;   // the max-chars length prefix in the low WORD
        GCHandle pin = GCHandle.Alloc(gl, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLINE, new IntPtr(line), pin.AddrOfPinnedObject());
        pin.Free();
        return (int)r.ToInt64();
    }

    public string GetLineText(int line)
    {
        int[] gl = new int[32];
        gl[0] = 31;   // the max-chars length prefix in the low WORD
        GCHandle pin = GCHandle.Alloc(gl, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLINE, new IntPtr(line), pin.AddrOfPinnedObject());
        // net48 PROBED: the line lands at offset 0 (overwriting the length
        // word), so the readback is at the buffer start, NOT +2.
        string s = Marshal.PtrToStringUni(pin.AddrOfPinnedObject());
        pin.Free();
        if (s == null) { s = ""; }
        return s;
    }

    public int LineFromChar(int charIndex)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_LINEFROMCHAR, new IntPtr(charIndex), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public void SelectAll()
    {
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_SETSEL, new IntPtr(0), new IntPtr(-1));
    }

    public int SelStart
    {
        get
        {
            long v = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            return (int)(v & 0xFFFFL);
        }
    }

    public int SelEnd
    {
        get
        {
            long v = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            return (int)((v >> 16) & 0xFFFFL);
        }
    }

    public int LimitText
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLIMITTEXT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_SETLIMITTEXT, new IntPtr(value), IntPtr.Zero); }
    }

    public bool Modified
    {
        get { return Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETMODIFY, IntPtr.Zero, IntPtr.Zero).ToInt64() != 0; }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_SETMODIFY, new IntPtr(value ? 1 : 0), IntPtr.Zero); }
    }

    public int CanUndo()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_CANUNDO, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int Undo()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_UNDO, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public void EmptyUndoBuffer()
    {
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero);
    }

    public int Scroll(int code)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_SCROLL, new IntPtr(code), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int ScrollCaret()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public void RaiseTextChanged()
    {
        Cases.Winforms1.P40Seq = Cases.Winforms1.P40Seq + "change;";
        if (_textchanged != null) _textchanged();
    }
}

// Phase 42 managed model: the RichTextBox (riched20 RichEdit20W). WfRichTextBox
// is ISOMORPHIC to the Phase-40 WfTextBox -- the same EM_* line-family surface
// on a DIFFERENT native control (RichEdit, not the user32 EDIT), with the
// probe-pinned RichEdit semantics:
//   SetText / GetTextLength / GetText -> WM_SETTEXT / WM_GETTEXTLENGTH /
//                    WM_GETTEXT (RichEdit stores CR-only internally but the
//                    text-access messages expand to CRLF -- 19 chars probed)
//   GetLineCount     -> EM_GETLINECOUNT (3)
//   LineIndex(n)     -> EM_LINEINDEX (the CR-only internal indices 0/6/12,
//                    NOT the EDIT's 0/7/14)
//   LineLength(c)    -> EM_LINELENGTH (excludes the CR: 5/5/5)
//   GetLineCopied(n) / GetLineText(n) -> EM_GETLINE: the buffer's FIRST WORD
//                    is the max chars to copy (a pinned int[32] low WORD -- the
//                    wired Marshal surface has no WriteInt16). net48 PROBED:
//                    the rich edit copies the line PLUS its CR at OFFSET 0,
//                    OVERWRITING the length word (copied 6, readback "LineA\r";
//                    the Phase-40 EDIT copied the CR-less 5 at offset 0).
//   LineFromChar(c)  -> EM_LINEFROMCHAR (0/5/7 -> 0/0/1)
//   SelectAll / SelStart / SelEnd -> EM_SETSEL(0,-1) + the packed EM_GETSEL
//                    (start | end<<16): end = 18, the CR-terminated INTERNAL
//                    length, not the CRLF 19.
//   TextChanged event -> raised by the form's WM_COMMAND dispatch when
//                    HIWORD(wParam) == EN_CHANGE. This is the REAL event route:
//                    EM_SETEVENTMASK genuinely gates the rich edit (probed), so
//                    an injected WM_CHAR delivers the synchronous notification
//                    into the WndProc -- unlike the plain EDIT (Phase 40), which
//                    ignores EM_SETEVENTMASK and needed the posted fake.
//                    RaiseTextChanged appends "change;" to P42Seq and invokes
//                    the multicast handlers.
public class WfRichTextBox
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _textchanged;

    public event Cases.Winforms1.ClickHandler TextChanged
    {
        add { _textchanged = _textchanged + value; }
        remove { _textchanged = _textchanged - value; }
    }

    public int ChangeCount
    {
        get { return _textchanged == null ? 0 : _textchanged.GetInvocationList().Length; }
    }

    public bool SetText(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_SETTEXT, IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        return r != IntPtr.Zero;
    }

    public int GetTextLength()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public string GetText()
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.WM_GETTEXT, new IntPtr(256), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        if (s == null) { s = ""; }
        return s;
    }

    public int GetLineCount()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int LineIndex(int line)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_LINEINDEX, new IntPtr(line), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int LineLength(int charIndex)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_LINELENGTH, new IntPtr(charIndex), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int GetLineCopied(int line)
    {
        int[] gl = new int[32];
        gl[0] = 31;   // the max-chars length prefix in the low WORD
        GCHandle pin = GCHandle.Alloc(gl, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLINE, new IntPtr(line), pin.AddrOfPinnedObject());
        pin.Free();
        return (int)r.ToInt64();
    }

    public string GetLineText(int line)
    {
        int[] gl = new int[32];
        gl[0] = 31;   // the max-chars length prefix in the low WORD
        GCHandle pin = GCHandle.Alloc(gl, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETLINE, new IntPtr(line), pin.AddrOfPinnedObject());
        // net48 PROBED: the line (PLUS its CR -- the rich edit diverges from the
        // plain EDIT) lands at offset 0 overwriting the length word, so the
        // readback is at the buffer start, NOT +2.
        string s = Marshal.PtrToStringUni(pin.AddrOfPinnedObject());
        pin.Free();
        if (s == null) { s = ""; }
        return s;
    }

    public int LineFromChar(int charIndex)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_LINEFROMCHAR, new IntPtr(charIndex), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public void SelectAll()
    {
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_SETSEL, new IntPtr(0), new IntPtr(-1));
    }

    public int SelStart
    {
        get
        {
            long v = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            return (int)(v & 0xFFFFL);
        }
    }

    public int SelEnd
    {
        get
        {
            long v = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            return (int)((v >> 16) & 0xFFFFL);
        }
    }

    public void RaiseTextChanged()
    {
        Cases.Winforms1.P42Seq = Cases.Winforms1.P42Seq + "change;";
        if (_textchanged != null) _textchanged();
    }
}

// Phase 10 managed model: MENU ITEMS. WfMenuItem models the WinForms MenuItem
// surface: a text + a WM_COMMAND id + a Click event. The form's WM_COMMAND
// handler routes a menu-id command into the matching item's Click (the
// WinForms MenuItem.Click path); a Click handler can drive other controls
// (the stage-10 status linkage: menu drives status). Same multicast event
// shape as WfControl (Delegate.Combine / GetInvocationList).
public class WfMenuItem
{
    public int Id;
    public string Text;
    public IntPtr Menu;
    private Cases.Winforms1.ClickHandler _click;

    public event Cases.Winforms1.ClickHandler Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    // Phase 30: Checked / Enabled state properties. Both read the native menu
    // item's state back through GetMenuState (the MF_CHECKED bit for Checked;
    // the MF_GRAYED/MF_DISABLED bits for Enabled -- enabled = neither disable
    // bit set) and write through CheckMenuItem / EnableMenuItem. Menu == 0
    // means the item has no native backing menu (phase 10 never binds the
    // model) -> report the legacy defaults (unchecked, enabled) so RaiseClick's
    // gating does not suppress pre-phase-30 clicks.
    public bool Checked
    {
        get { return Menu != IntPtr.Zero && (Cases.Winforms1.GetMenuState(Menu, Id, Cases.Winforms1.MF_BYCOMMAND) & Cases.Winforms1.MF_CHECKED) != 0; }
        set { Cases.Winforms1.CheckMenuItem(Menu, Id, value ? Cases.Winforms1.MF_CHECKED : Cases.Winforms1.MF_UNCHECKED); }
    }

    public bool Enabled
    {
        get { return Menu == IntPtr.Zero || (Cases.Winforms1.GetMenuState(Menu, Id, Cases.Winforms1.MF_BYCOMMAND) & (Cases.Winforms1.MF_GRAYED | Cases.Winforms1.MF_DISABLED)) == 0; }
        set { Cases.Winforms1.EnableMenuItem(Menu, Id, value ? Cases.Winforms1.MF_ENABLED : Cases.Winforms1.MF_GRAYED); }
    }

    public void RaiseClick() { if (Enabled && _click != null) _click(); }
}

// Phase 11 managed model: MDI CHILD WINDOWS. WfMdiChild models an MDI child
// window surface: a native HWND (a WS_CHILD of the MDI client) + its OWN Click
// event (the per-child event delegate). The shared MDI child WndProc routes a
// posted WF_MDI_CHILD_MSG into the matching WfMdiChild's RaiseClick by HWND --
// each child's handlers fire only when ITS OWN window is posted to (per-child
// event isolation). Same multicast event shape as WfControl.
public class WfMdiChild
{
    public IntPtr Handle;
    private Cases.Winforms1.ClickHandler _click;

    public event Cases.Winforms1.ClickHandler Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    public void RaiseClick() { if (_click != null) _click(); }
}

// Phase 16 managed model: TAB CONTROL. WfTabControl wraps a WC_TABCONTROL
// ("SysTabControl32") HWND and exposes the TabControl surface:
//   AddTab(pageIndex)   -> TCM_INSERTITEMW with a TCITEM (TCIF_PARAM, lParam =
//                          the page index each tab owns). The TCITEM is built
//                          in a GCHandle-PINNED int[] with the native TCITEMW
//                          layout (mask @0, pszText @16, lParam @32 -- 12
//                          int32s; the pinned array data is 8-aligned exactly
//                          like the struct) -- the wired Marshal surface has no
//                          StructureToPtr, so the struct is written
//                          field-by-field into the pinned array's data bytes.
//   Count               -> TCM_GETITEMCOUNT (the page count)
//   SelectedIndex       -> TCM_GETCURSEL / TCM_SETCURSEL (TCM_SETCURSEL is
//                          SILENT -- the parent is NOT notified; the initial
//                          selection is -1, the CB_ERR-like "nothing selected")
//   SelectedIndexChanged event -> raised by the form's WM_NOTIFY dispatch on
//                          TCN_SELCHANGE; RaiseSelectionChanged RE-READS the
//                          current selection (TCM_GETCURSEL) and switches the
//                          visible page (P16ShowPage -- one page per tab).
//   (Phase 35 item read/write surface, same TCITEMW pinned int[12] layout:
//     InsertTabText      -> TCM_INSERTITEMW wParam=COUNT (APPEND -- probed:
//                          wParam=0 front-inserts and REVERSES the order, the
//                          Phase-16 AddTab mode; wParam=Count keeps Tab0..n at
//                          indices 0..n) with TCIF_TEXT|TCIF_PARAM writing both
//                          the text and lParam (= the page index)
//     GetTabText(i)      -> TCM_GETITEMW mask=TCIF_TEXT into an AllocHGlobal
//                          128-char buffer + PtrToStringUni (the WfListView
//                          GetItemText discipline)
//     GetTabParam(i)     -> TCM_GETITEMW mask=TCIF_PARAM, reads the pinned
//                          [8..9] lParam (needs the corrected 0x0008 constant)
//     SetTabText(i,t)    -> TCM_SETITEMW mask=TCIF_TEXT (rename + re-read)
//     ReadTabRect(i)     -> TCM_GETITEMRECT writes a pinned int[4] (L/T/R/B)
//                          into the RectLeft/Top/Right/Bottom fields -- NO
//                          SZArray return (the M3 gap)
//     RowCount           -> TCM_GETROWCOUNT (the tab-control row count)
// Same multicast event shape as WfControl.
public class WfTabControl
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _selchange;

    public event Cases.Winforms1.ClickHandler SelectedIndexChanged
    {
        add { _selchange = _selchange + value; }
        remove { _selchange = _selchange - value; }
    }

    public int SelCount
    {
        get { return _selchange == null ? 0 : _selchange.GetInvocationList().Length; }
    }

    public int AddTab(int pageIndex)
    {
        // TCITEMW x64 layout as 12 int32s (the pinned array data is 8-aligned,
        // so the pointer fields sit at their natural offsets):
        //   [0]=mask [1]=dwState [2]=dwStateMask [3]=pad
        //   [4..5]=pszText (NULL) [6]=cchTextMax [7]=iImage
        //   [8..9]=lParam [10]=iIndent [11]=pad
        int[] tci = new int[12];
        tci[0] = Cases.Winforms1.TCIF_PARAM;
        tci[8] = pageIndex;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        IntPtr pItem = pin.AddrOfPinnedObject();
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_INSERTITEMW, IntPtr.Zero, pItem);
        pin.Free();
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public int SelectedIndex
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_SETCURSEL, new IntPtr(value), IntPtr.Zero); }
    }

    // Phase 35: APPEND-insert a tab with text + lParam (wParam = the CURRENT
    // count). Probed: wParam=0 front-inserts (the Phase-16 AddTab mode) and
    // REVERSES the order; wParam=Count keeps the order (returns 0/1/2), so the
    // index<->text<->lParam mapping below is deterministic.
    public int InsertTabText(int pageIndex, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tci = new int[12];
        tci[0] = Cases.Winforms1.TCIF_TEXT | Cases.Winforms1.TCIF_PARAM;
        long pt = p.ToInt64();
        tci[4] = (int)(pt & 0xFFFFFFFFL);
        tci[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        tci[8] = pageIndex;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_INSERTITEMW,
            new IntPtr(Count), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    // Phase 35: read a tab's text back (TCM_GETITEMW, mask = TCIF_TEXT; the
    // control copies the text into the caller's buffer).
    public string GetTabText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] tci = new int[12];
        tci[0] = Cases.Winforms1.TCIF_TEXT;
        long bp = buf.ToInt64();
        tci[4] = (int)(bp & 0xFFFFFFFFL);
        tci[5] = (int)((bp >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_GETITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    // Phase 35: read a tab's lParam back (TCM_GETITEMW, mask = TCIF_PARAM --
    // the control writes the lParam into the pinned [8..9]).
    public int GetTabParam(int index)
    {
        int[] tci = new int[12];
        tci[0] = Cases.Winforms1.TCIF_PARAM;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_GETITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return tci[8];
    }

    // Phase 35: rename a tab (TCM_SETITEMW, mask = TCIF_TEXT) -- the write
    // half of the item surface (the control COPIES the text out of the
    // caller's buffer, so the buffer is freed right after the send).
    public bool SetTabText(int index, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tci = new int[12];
        tci[0] = Cases.Winforms1.TCIF_TEXT;
        long pt = p.ToInt64();
        tci[4] = (int)(pt & 0xFFFFFFFFL);
        tci[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_SETITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    // Phase 35: tab geometry -- TCM_GETITEMRECT writes a RECT into the pinned
    // int[4] (L/T/R/B). void + instance fields (an int[] RETURN would hit the
    // M3 SZArray gap -- the PBM_GETRANGE/Phase-33/34 discipline).
    public int RectLeft;
    public int RectTop;
    public int RectRight;
    public int RectBottom;
    public void ReadTabRect(int index)
    {
        int[] rc = new int[4];
        GCHandle pin = GCHandle.Alloc(rc, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_GETITEMRECT, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        RectLeft = rc[0];
        RectTop = rc[1];
        RectRight = rc[2];
        RectBottom = rc[3];
    }

    // Phase 35: the tab-control row count (scalar -- 1 for the 3 tabs on one
    // row; the wrapped multi-row layout would report > 1).
    public int RowCount
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_GETROWCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    // Phase 45: set the fixed tab-item size (TCM_SETITEMSIZE, lParam =
    // cx | (cy << 16)). The RETURN value is the PREVIOUS item size (LOWORD =
    // the previous width, HIWORD = the previous height) -- the native backing
    // of TabControl.ItemSize. The two-step readback: the FIRST call's return
    // is the font-metric default (NOT byte-exact, discarded); the SECOND call
    // returns the FIRST explicit value, the THIRD the second -- the readback
    // locks onto the explicit values (probed on comctl32 v5.82). Only
    // TCS_FIXEDWIDTH / TCS_OWNERDRAWFIXED controls honor the size.
    public int SetItemSize(int cx, int cy)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TCM_SETITEMSIZE,
            IntPtr.Zero, new IntPtr(cx | (cy << 16)));
        return (int)r.ToInt64();
    }

    public void RaiseSelectionChanged()
    {
        int sel = SelectedIndex;
        if (sel >= 0) Cases.Winforms1.P16ShowPage(sel);
        if (_selchange != null) _selchange();
    }
}

// Phase 21 managed model: LIST VIEW. WfListView wraps a WC_LISTVIEW
// ("SysListView32") HWND and exposes the ListView surface:
//   Insert(text)      -> LVM_INSERTITEMW with an LVITEMW (LVIF_TEXT|LVIF_PARAM,
//                        pszText = the StringToHGlobalUni pointer, lParam = 42,
//                        cchTextMax = 128). CRITICAL: the comctl32 v5.82 that
//                        ships on this OS REJECTS iItem = -1 ("append" -- the
//                        v6-only semantic; probed: it returns -1 and the count
//                        does NOT grow), so Insert uses the CURRENT count as the
//                        EXPLICIT iItem (0, 1, 2, ...) -- the deterministic
//                        explicit-index append the v5 control accepts (probed:
//                        iItem=0/1 return 0/1, count=2). The LVITEMW is built
//                        in a GCHandle-PINNED int[22] with the native x64 layout
//                        (mask @0, iItem @4, iSubItem @8, state @12, stateMask
//                        @16, pszText @24 -- a pointer split across two int32s
//                        -- cchTextMax @32, iImage @36, lParam @40 -- also a
//                        pointer -- iIndent @48, iGroupId @52, cColumns @56,
//                        puColumns @64, piColFmt @72, iGroup @80; 88 bytes; the
//                        pinned array data is 8-aligned exactly like the struct)
//                        -- the wired Marshal surface has no StructureToPtr, so
//                        the struct is written field-by-field.
//   Count               -> LVM_GETITEMCOUNT (the row count)
//   SetCount(n)         -> LVM_SETITEMCOUNT (the ListView.VirtualListSize
//                        surface -- pre-allocating n rows)
//   GetItemText(i)      -> LVM_GETITEMTEXTW (AllocHGlobal 256 buffer, mask =
//                        LVIF_TEXT, cchTextMax = 128, PtrToStringUni readback)
//   SetItemText(i, t)   -> LVM_SETITEMTEXTW (the same LVITEMW shape)
//   SetItemState(i)     -> LVM_SETITEMSTATE with state/stateMask = LVIS_SELECTED
//                        (the SelectedIndices surface -- TRUE on success)
//   GetItemState(i)     -> LVM_GETITEMSTATE with mask = LVIS_SELECTED (the state
//                        bit readback)
//   SelectedIndex       -> LVM_GETITEMSTATE over the rows (the first selected
//                        row, -1 when nothing is selected)
//   SelectionChanged event -> raised by the form's WM_NOTIFY dispatch on the
//                        control id (the LVN_ITEMCHANGED parent notification);
//                        RaiseSelectionChanged RE-READS the real selection
//                        (LVM_GETITEMSTATE over the rows) and records "sel<N>;"
//                        -- the event observes the list state at fire time.
// Same multicast event shape as WfControl.
public class WfListView
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _selchange;
    private Cases.Winforms1.ClickHandler _dblclick;
    private Cases.Winforms1.ClickHandler _colclick;
    private Cases.Winforms1.ClickHandler _itemactivate;
    private Cases.Winforms1.ClickHandler _labeleditend;
    private Cases.Winforms1.ClickHandler _begindrag;
    private Cases.Winforms1.ClickHandler _beginrdrag;
    private Cases.Winforms1.ClickHandler _keydownnotify;
    private Cases.Winforms1.ClickHandler _iteminserted;
    private Cases.Winforms1.ClickHandler _itemdeleted;
    private Cases.Winforms1.ClickHandler _allitemsdeleted;
    private Cases.Winforms1.ClickHandler _itemchanged;
    private Cases.Winforms1.ClickHandler _itemchanging;

    public event Cases.Winforms1.ClickHandler SelectionChanged
    {
        add { _selchange = _selchange + value; }
        remove { _selchange = _selchange - value; }
    }

    // Phase 90: the LVN_DBLCLK double-click event (same multicast shape).
    public event Cases.Winforms1.ClickHandler DoubleClick
    {
        add { _dblclick = _dblclick + value; }
        remove { _dblclick = _dblclick - value; }
    }

    public void RaiseDoubleClick()
    {
        if (_dblclick != null) _dblclick();
    }

    // Phase 91: the LVN_COLUMNCLICK event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ColumnClick
    {
        add { _colclick = _colclick + value; }
        remove { _colclick = _colclick - value; }
    }

    public void RaiseColumnClick()
    {
        if (_colclick != null) _colclick();
    }

    // Phase 92: the LVN_ITEMACTIVATE event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemActivate
    {
        add { _itemactivate = _itemactivate + value; }
        remove { _itemactivate = _itemactivate - value; }
    }

    public void RaiseItemActivate()
    {
        if (_itemactivate != null) _itemactivate();
    }

    // Phase 93: the LVN_ENDLABELEDITW event (same multicast shape).
    public event Cases.Winforms1.ClickHandler LabelEditEnd
    {
        add { _labeleditend = _labeleditend + value; }
        remove { _labeleditend = _labeleditend - value; }
    }

    public void RaiseLabelEditEnd()
    {
        if (_labeleditend != null) _labeleditend();
    }

    // Phase 94: the LVN_BEGINDRAG event (same multicast shape).
    public event Cases.Winforms1.ClickHandler BeginDrag
    {
        add { _begindrag = _begindrag + value; }
        remove { _begindrag = _begindrag - value; }
    }

    public void RaiseBeginDrag()
    {
        if (_begindrag != null) _begindrag();
    }

    // Phase 95: the LVN_BEGINRDRAG event (same multicast shape).
    public event Cases.Winforms1.ClickHandler BeginRDrag
    {
        add { _beginrdrag = _beginrdrag + value; }
        remove { _beginrdrag = _beginrdrag - value; }
    }

    public void RaiseBeginRDrag()
    {
        if (_beginrdrag != null) _beginrdrag();
    }

    // Phase 96: the LVN_KEYDOWN event (same multicast shape).
    public event Cases.Winforms1.ClickHandler KeyDownNotify
    {
        add { _keydownnotify = _keydownnotify + value; }
        remove { _keydownnotify = _keydownnotify - value; }
    }

    public void RaiseKeyDownNotify()
    {
        if (_keydownnotify != null) _keydownnotify();
    }

    // Phase 97: the LVN_INSERTITEM event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemInserted
    {
        add { _iteminserted = _iteminserted + value; }
        remove { _iteminserted = _iteminserted - value; }
    }

    public void RaiseItemInserted()
    {
        if (_iteminserted != null) _iteminserted();
    }

    // Phase 98: the LVN_DELETEITEM event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemDeleted
    {
        add { _itemdeleted = _itemdeleted + value; }
        remove { _itemdeleted = _itemdeleted - value; }
    }

    public void RaiseItemDeleted()
    {
        if (_itemdeleted != null) _itemdeleted();
    }

    // Phase 99: the LVN_DELETEALLITEMS event (same multicast shape).
    public event Cases.Winforms1.ClickHandler AllItemsDeleted
    {
        add { _allitemsdeleted = _allitemsdeleted + value; }
        remove { _allitemsdeleted = _allitemsdeleted - value; }
    }

    public void RaiseAllItemsDeleted()
    {
        if (_allitemsdeleted != null) _allitemsdeleted();
    }

    // Phase 100: the LVN_ITEMCHANGED event (same multicast shape -- the
    // synchronous variant of the Phase-21 SelectionChanged).
    public event Cases.Winforms1.ClickHandler ItemChanged
    {
        add { _itemchanged = _itemchanged + value; }
        remove { _itemchanged = _itemchanged - value; }
    }

    public void RaiseItemChanged()
    {
        if (_itemchanged != null) _itemchanged();
    }

    // Phase 101: the LVN_ITEMCHANGING event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemChanging
    {
        add { _itemchanging = _itemchanging + value; }
        remove { _itemchanging = _itemchanging - value; }
    }

    public void RaiseItemChanging()
    {
        if (_itemchanging != null) _itemchanging();
    }

    public int SelCount
    {
        get { return _selchange == null ? 0 : _selchange.GetInvocationList().Length; }
    }

    public int DblCount
    {
        get { return _dblclick == null ? 0 : _dblclick.GetInvocationList().Length; }
    }

    public int Insert(string text)
    {
        // EXPLICIT-index append: the current count (0, 1, 2, ...). The comctl32
        // v5.82 LVM_INSERTITEMW REJECTS iItem = -1 (returns -1, no append); the
        // explicit index is the deterministic append this control accepts.
        int iItem = Count;
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT | Cases.Winforms1.LVIF_PARAM;
        lvi[1] = iItem;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        lvi[10] = 42; lvi[11] = 0;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public void SetCount(int n)
    {
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETITEMCOUNT, new IntPtr(n), IntPtr.Zero);
    }

    public string GetItemText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT;
        lvi[1] = index;
        long bp = buf.ToInt64();
        lvi[6] = (int)(bp & 0xFFFFFFFFL);
        lvi[7] = (int)((bp >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMTEXTW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public bool SetItemText(int index, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT;
        lvi[1] = index;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETITEMTEXTW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    public bool SetItemState(int index)
    {
        int[] lvi = new int[22];
        lvi[3] = Cases.Winforms1.LVIS_SELECTED;
        lvi[4] = Cases.Winforms1.LVIS_SELECTED;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETITEMSTATE, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return r.ToInt64() != 0;
    }

    public int GetItemState(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMSTATE, new IntPtr(index), new IntPtr(Cases.Winforms1.LVIS_SELECTED));
        return (int)r.ToInt64();
    }

    public int SelectedIndex
    {
        get
        {
            int n = Count;
            for (int i = 0; i < n; i++)
            {
                if ((GetItemState(i) & Cases.Winforms1.LVIS_SELECTED) != 0) return i;
            }
            return -1;
        }
    }

    public void RaiseSelectionChanged()
    {
        int sel = SelectedIndex;
        Cases.Winforms1.P21SelChangeSeq = Cases.Winforms1.P21SelChangeSeq + "sel" + sel + ";";
        if (_selchange != null) _selchange();
    }

    // Phase 39: the checkbox + multi-select state surface (ADDITIVE -- the
    // Phase-21 methods above are untouched; every call goes through the
    // existing SendMessageW channel).
    //   SetItemStateBits(i,state,mask) -> LVM_SETITEMSTATE with the FULL
    //     state / stateMask (vs the Phase-21 SetItemState's fixed
    //     LVIS_SELECTED-only shape).
    //   SetCheckState(i,on) / GetCheckState(i) -> the checkbox on/off surface:
    //     state image n (1 unchecked, 2 checked, 3 grayed) is stored in
    //     LVIS_STATEIMAGEMASK as (n<<12) -- SetCheckState writes
    //     INDEXTOSTATEIMAGEMASK(2) = 0x2000 / (1) = 0x1000 through the SAME
    //     LVM_SETITEMSTATE channel; GetCheckState decodes the LVM_GETITEMSTATE
    //     readback ((r>>12)-1: 0 = unchecked, 1 = checked, -1 = no image).
    //     PROBED: rows inserted with LVS_EX_CHECKBOXES on read 0 (unchecked).
    //   GetCheckStateV6(i) -> LVM_GETCHECKSTATE, the v6-ONLY message -- v5.82
    //     IGNORES it (PROBED dead-read 0 even on a checked row -- the NEGATIVE
    //     proof the v5/v6-shared SETITEMSTATE channel is the real surface).
    //   SetItemSelected(i,on) / GetItemSelected(i) -> the LVIS_SELECTED bit
    //     (the Phase-21 channel), INDEPENDENT of the checkbox bits (PROBED:
    //     unselecting a checked row leaves its checkbox state).
    //   SelectedCount -> LVM_GETSELECTEDCOUNT -- a SCALAR selected-row count
    //     (PROBED ACCUMULATES: two rows selected -> 2, not an exclusive 1).
    //   SelectedIndices -> LVM_GETNEXTITEM(-1, LVNI_SELECTED) walking the
    //     selected rows into a "idx;idx;" STRING (never an int[] -- the M3
    //     SZArray restriction).
    public bool SetItemStateBits(int index, int state, int mask)
    {
        int[] lvi = new int[22];
        lvi[3] = state;
        lvi[4] = mask;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETITEMSTATE, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return r.ToInt64() != 0;
    }

    public bool SetCheckState(int index, bool on)
    {
        int state = (on ? 2 : 1) << 12;   // INDEXTOSTATEIMAGEMASK(2) / (1)
        return SetItemStateBits(index, state, Cases.Winforms1.LVIS_STATEIMAGEMASK);
    }

    public int GetCheckState(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMSTATE, new IntPtr(index), new IntPtr(Cases.Winforms1.LVIS_STATEIMAGEMASK));
        return ((int)r.ToInt64() >> 12) - 1;
    }

    public int GetCheckStateV6(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCHECKSTATE, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public bool SetItemSelected(int index, bool on)
    {
        return SetItemStateBits(index, on ? Cases.Winforms1.LVIS_SELECTED : 0, Cases.Winforms1.LVIS_SELECTED);
    }

    public bool GetItemSelected(int index)
    {
        return (GetItemState(index) & Cases.Winforms1.LVIS_SELECTED) != 0;
    }

    public int SelectedCount
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public string SelectedIndices
    {
        get
        {
            string s = "";
            int idx = -1;
            while (true)
            {
                IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETNEXTITEM, new IntPtr(idx), new IntPtr(Cases.Winforms1.LVNI_SELECTED));
                idx = (int)r.ToInt64();
                if (idx < 0) break;
                s = s + idx + ";";
            }
            return s;
        }
    }
}

// Phase 22 managed model: TREE VIEW. WfTreeView wraps a WC_TREEVIEW
// ("SysTreeView32") HWND and exposes the TreeView surface:
//   InsertNode(hParent, text) -> TVM_INSERTITEMW with a TVINSERTSTRUCTW
//                        (hParent + hInsertAfter = TVI_LAST + a TVITEMW whose
//                        mask = TVIF_TEXT|TVIF_PARAM, pszText = the
//                        StringToHGlobalUni pointer, cchTextMax = 128, lParam =
//                        42 -- built in a GCHandle-PINNED int[18] with the
//                        native x64 layout, the same field-by-field pattern as
//                        the Phase-16 TCITEMW / Phase-21 LVITEMW). hParent =
//                        IntPtr.Zero inserts at the top of the tree (the
//                        documented NULL-root semantic). Returns the new
//                        node's HTREEITEM as the full IntPtr -- a comctl32 heap
//                        handle that is NOT deterministic across processes
//                        (probed: 5 runs -> 5 different low 32 bits) AND that
//                        the transpiler degrades to int32 at the call site (a
//                        user-method IntPtr return classifies "val", not the
//                        P/Invoke "ptr"), so the caller must NOT consume the
//                        value: the handle is re-fetched through the P/Invoke
//                        SendMessageW(TVM_GETNEXTITEM) channel (TVGN_ROOT for
//                        the root, TVGN_CHILD for the child) -- full-width.
//   Count                   -> TVM_GETCOUNT (the node count)
//   Expand(hItem)           -> TVM_EXPAND with TVE_EXPAND (expands a node;
//                        TRUE on success)
//   DeleteNode(hItem)       -> TVM_DELETEITEM (removes a node and its subtree;
//                        TRUE on success)
//   SelectionChanged event -> same multicast event shape as WfListView.
public class WfTreeView
{
    public IntPtr Handle;
    public int Id;
    private Cases.Winforms1.ClickHandler _selchange;
    private Cases.Winforms1.ClickHandler _selsync;
    private Cases.Winforms1.ClickHandler _selchangingsync;
    private Cases.Winforms1.ClickHandler _itemexpandedsync;
    private Cases.Winforms1.ClickHandler _itemexpandingsync;
    private Cases.Winforms1.ClickHandler _itemcollapsedsync;
    private Cases.Winforms1.ClickHandler _itemcollapsingsync;
    private Cases.Winforms1.ClickHandler _beginlabeleditsync;
    private Cases.Winforms1.ClickHandler _endlabeleditsync;
    private Cases.Winforms1.ClickHandler _itemdeletedsync;

    public event Cases.Winforms1.ClickHandler SelectionChanged
    {
        add { _selchange = _selchange + value; }
        remove { _selchange = _selchange - value; }
    }

    // Phase 102: the synchronous TVN_SELCHANGED event (same multicast shape).
    public event Cases.Winforms1.ClickHandler SelChangedSync
    {
        add { _selsync = _selsync + value; }
        remove { _selsync = _selsync - value; }
    }

    public void RaiseSelChangedSync()
    {
        if (_selsync != null) _selsync();
    }

    // Phase 103: the synchronous TVN_SELCHANGING event (same multicast shape).
    public event Cases.Winforms1.ClickHandler SelChangingSync
    {
        add { _selchangingsync = _selchangingsync + value; }
        remove { _selchangingsync = _selchangingsync - value; }
    }

    public void RaiseSelChangingSync()
    {
        if (_selchangingsync != null) _selchangingsync();
    }

    // Phase 104: the synchronous TVN_ITEMEXPANDED event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemExpandedSync
    {
        add { _itemexpandedsync = _itemexpandedsync + value; }
        remove { _itemexpandedsync = _itemexpandedsync - value; }
    }

    public void RaiseItemExpandedSync()
    {
        if (_itemexpandedsync != null) _itemexpandedsync();
    }

    // Phase 105: the synchronous TVN_ITEMEXPANDING event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemExpandingSync
    {
        add { _itemexpandingsync = _itemexpandingsync + value; }
        remove { _itemexpandingsync = _itemexpandingsync - value; }
    }

    public void RaiseItemExpandingSync()
    {
        if (_itemexpandingsync != null) _itemexpandingsync();
    }

    // Phase 106: the synchronous TVN_ITEMCOLLAPSED event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemCollapsedSync
    {
        add { _itemcollapsedsync = _itemcollapsedsync + value; }
        remove { _itemcollapsedsync = _itemcollapsedsync - value; }
    }

    public void RaiseItemCollapsedSync()
    {
        if (_itemcollapsedsync != null) _itemcollapsedsync();
    }

    // Phase 107: the synchronous TVN_ITEMCOLLAPSING event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemCollapsingSync
    {
        add { _itemcollapsingsync = _itemcollapsingsync + value; }
        remove { _itemcollapsingsync = _itemcollapsingsync - value; }
    }

    public void RaiseItemCollapsingSync()
    {
        if (_itemcollapsingsync != null) _itemcollapsingsync();
    }

    // Phase 108: the synchronous TVN_BEGINLABELEDIT event (same multicast shape).
    public event Cases.Winforms1.ClickHandler BeginLabelEditSync
    {
        add { _beginlabeleditsync = _beginlabeleditsync + value; }
        remove { _beginlabeleditsync = _beginlabeleditsync - value; }
    }

    public void RaiseBeginLabelEditSync()
    {
        if (_beginlabeleditsync != null) _beginlabeleditsync();
    }

    // Phase 109: the synchronous TVN_ENDLABELEDIT event (same multicast shape).
    public event Cases.Winforms1.ClickHandler EndLabelEditSync
    {
        add { _endlabeleditsync = _endlabeleditsync + value; }
        remove { _endlabeleditsync = _endlabeleditsync - value; }
    }

    public void RaiseEndLabelEditSync()
    {
        if (_endlabeleditsync != null) _endlabeleditsync();
    }

    // Phase 110: the synchronous TVN_DELETEITEM event (same multicast shape).
    public event Cases.Winforms1.ClickHandler ItemDeletedSync
    {
        add { _itemdeletedsync = _itemdeletedsync + value; }
        remove { _itemdeletedsync = _itemdeletedsync - value; }
    }

    public void RaiseItemDeletedSync()
    {
        if (_itemdeletedsync != null) _itemdeletedsync();
    }

    public int SelCount
    {
        get { return _selchange == null ? 0 : _selchange.GetInvocationList().Length; }
    }

    public IntPtr InsertNode(IntPtr hParent, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tvi = new int[18];
        long hp = hParent.ToInt64();
        tvi[0] = (int)(hp & 0xFFFFFFFFL);
        tvi[1] = (int)((hp >> 32) & 0xFFFFFFFFL);
        tvi[2] = unchecked((int)0xFFFF0002);
        tvi[3] = 0;
        tvi[4] = Cases.Winforms1.TVIF_TEXT | Cases.Winforms1.TVIF_PARAM;
        long pt = p.ToInt64();
        tvi[10] = (int)(pt & 0xFFFFFFFFL);
        tvi[11] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tvi[12] = 128;
        tvi[16] = 42; tvi[17] = 0;
        GCHandle pin = GCHandle.Alloc(tvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r;
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public bool Expand(IntPtr hItem)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_EXPAND, new IntPtr(Cases.Winforms1.TVE_EXPAND), hItem);
        return r.ToInt64() != 0;
    }

    public bool DeleteNode(IntPtr hItem)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_DELETEITEM, IntPtr.Zero, hItem);
        return r.ToInt64() != 0;
    }

    // Phase 41 readback surface (ADDITIVE -- the Phase-22 methods above are
    // untouched). Handles are ALWAYS obtained in Main through the direct
    // P/Invoke SendMessageW(TVM_GETNEXTITEM) channel and passed IN -- the
    // wrapper methods never return a node handle (the transpiler degrades a
    // user-method IntPtr return to int32; a P/Invoke IntPtr result stays
    // intptr_t). The TVITEMW is built in a GCHandle-PINNED int[14] (x64 56B):
    // [0]=mask [2..3]=hItem [4]=state [5]=stateMask [6..7]=pszText
    // [8]=cchTextMax [9]=iImage [10]=iSelectedImage [11]=cChildren [12..13]=lParam.

    // GetNodeText -> TVM_GETITEMW (mask = TVIF_TEXT|TVIF_HANDLE) with the text
    // buffer at pszText; the control copies the node's label in, cchTextMax
    // caps it, and the wrapper reads it back with PtrToStringUni.
    public string GetNodeText(IntPtr hItem)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] item = new int[14];
        item[0] = Cases.Winforms1.TVIF_TEXT | Cases.Winforms1.TVIF_HANDLE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        long bp = buf.ToInt64();
        item[6] = (int)(bp & 0xFFFFFFFFL);
        item[7] = (int)((bp >> 32) & 0xFFFFFFFFL);
        item[8] = 128;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_GETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    // SetNodeText -> TVM_SETITEMW (mask = TVIF_TEXT|TVIF_HANDLE) rewrites a
    // node's label (the caller keeps ownership of the pszText buffer through the
    // call; the control copies the text).
    public bool SetNodeText(IntPtr hItem, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] item = new int[14];
        item[0] = Cases.Winforms1.TVIF_TEXT | Cases.Winforms1.TVIF_HANDLE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        long pt = p.ToInt64();
        item[6] = (int)(pt & 0xFFFFFFFFL);
        item[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        item[8] = 128;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_SETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    // GetNodeState -> TVM_GETITEMSTATE (the scalar channel: wParam = hItem,
    // lParam = stateMask). net48 PROBED: comctl32 IGNORES the mask and returns
    // the FULL item state (96 = EXPANDED|EXPANDEDONCE after TVM_EXPAND, 64 after
    // collapse), so the wrapper applies the mask in managed code to give the
    // deterministic masked read (expand 32, collapse 0).
    public int GetNodeState(IntPtr hItem, int stateMask)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_GETITEMSTATE, hItem, new IntPtr(stateMask));
        return (int)r.ToInt64() & stateMask;
    }

    // SetNodeState -> TVM_SETITEMW with mask = TVIF_HANDLE|TVIF_STATE (state +
    // stateMask drive the change; TVM_GETITEMSTATE reads the state bits back).
    public bool SetNodeState(IntPtr hItem, int stateMask, int state)
    {
        int[] item = new int[14];
        item[0] = Cases.Winforms1.TVIF_HANDLE | Cases.Winforms1.TVIF_STATE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        item[4] = state;
        item[5] = stateMask;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_SETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        return r.ToInt64() != 0;
    }

    // SelectNode -> TVM_SELECTITEM with wParam = TVGN_CARET moves the caret to
    // the node (probed: returns TRUE on the hidden tree; the caret is re-fetched
    // with TVM_GETNEXTITEM(TVGN_CARET) for the round-trip).
    public bool SelectNode(IntPtr hItem)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_SELECTITEM, new IntPtr(Cases.Winforms1.TVGN_CARET), hItem);
        return r.ToInt64() != 0;
    }

    // GetNodeLParam -> TVM_GETITEMW with mask = TVIF_PARAM|TVIF_HANDLE reads the
    // node's lParam back (TVIF_PARAM FIXED to 0x0004 -- the Phase-22 inserts now
    // really store the 42 the probe reads back).
    public int GetNodeLParam(IntPtr hItem)
    {
        int[] item = new int[14];
        item[0] = Cases.Winforms1.TVIF_PARAM | Cases.Winforms1.TVIF_HANDLE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TVM_GETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        long lo = item[12];
        long lp = lo & 0xFFFFFFFFL;
        return (int)lp;
    }
}

// Phase 23 managed model: PICTURE BOX. WfPictureBox wraps a "STATIC" window
// created with the SS_BITMAP style and exposes the PictureBox surface:
//   Handle / Id          -> the HWND + control id (the same shape as
//                           WfListView / WfTreeView)
//   StyleBitmap          -> GetWindowLongW(GWL_STYLE) & SS_BITMAP (the
//                           control's bitmap-mode style bit)
//   Image get            -> STM_GETIMAGE (the CURRENT HBITMAP handle)
//   Image set            -> STM_SETIMAGE (stores PrevImage = the PREVIOUS
//                           bitmap handle -- NULL for a fresh control)
// The Image property is typed LONG (not IntPtr): the transpiler's refl3
// property-signature encoder supports only the primitive/string/class element
// bytes (MethodSignatureCoder.EncodePropertyType) -- ELEMENT_TYPE_I (0x18,
// native int) throws "not supported in refl3". A long rides the full 64-bit
// handle width identically (ToInt64 / new IntPtr(long) -> il2cpp_intptr_ctor),
// so the STM_SETIMAGE/STM_GETIMAGE round-trip probe is byte-exact on net48.
public class WfPictureBox
{
    public IntPtr Handle;
    public int Id;
    public IntPtr PrevImage;

    public bool StyleBitmap
    {
        get
        {
            IntPtr style = Cases.Winforms1.GetWindowLongW(Handle, Cases.Winforms1.GWL_STYLE);
            return (style.ToInt64() & Cases.Winforms1.SS_BITMAP) != 0;
        }
    }

    public long Image
    {
        get
        {
            return Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.STM_GETIMAGE, new IntPtr(Cases.Winforms1.IMAGE_BITMAP), IntPtr.Zero).ToInt64();
        }
        set
        {
            PrevImage = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.STM_SETIMAGE, new IntPtr(Cases.Winforms1.IMAGE_BITMAP), new IntPtr(value));
        }
    }
}

// Phase 32 managed model: TOOL BAR BUTTON. WfToolButton models a single
// toolbar button -- the WinForms ToolStripButton surface, simplified -- with
// the Phase-10 WfMenuItem shape (an explicit command id + a text label + a
// Click multicast event). The form's WndProc WM_COMMAND dispatch routes a
// toolbar-button id (LOWORD of wParam, HIWORD = 0 for a TBSTYLE_BUTTON) into
// the matching button's Click event. RaiseClick fires unconditionally: the
// enable gate lives in the native TB_GETSTATE channel (a disabled button is
// never clicked by a real user), so the managed model has no gating of its own.
public class WfToolButton
{
    public int Id;
    public string Text;
    private Cases.Winforms1.ClickHandler _click;

    public event Cases.Winforms1.ClickHandler Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    public void RaiseClick() { if (_click != null) _click(); }
}

// Phase 32 managed model: TOOL STRIP. WfToolStrip wraps a "ToolbarWindow32"
// HWND (created with WS_CHILD | CCS_TOP, the toolbar window style) and exposes
// the WinForms ToolStrip surface, simplified -- the whole TB_* channel through
// the existing SendMessageW P/Invoke:
//   Handle / Id        -> the HWND + control id (the same shape as every other
//                         wrapper here)
//   Add(text,id,style,state) -> builds a TBBUTTON as a GCHandle-PINNED int[8]
//                         (iBitmap@0 = I_IMAGENONE, idCommand@4, fsState|fsStyle<<8
//                         @8, dwData@16 = 42, iString@24 = the StringToHGlobalUni
//                         pointer -- the offsetof-probed x64 layout) and appends
//                         it with TB_ADDBUTTONSW (wParam = 1, one button per
//                         call). The toolbar COPIES the text into its own
//                         buffer (probed: TB_GETBUTTONTEXTW survives the iString
//                         free), so the buffer is freed right after the add.
//   Count               -> TB_BUTTONCOUNT
//   GetText(id)         -> TB_GETBUTTONTEXTW into a 256-byte buffer +
//                         PtrToStringUni (the DETERMINISTIC text read -- the
//                         TB_GETBUTTON iString field is a comctl32-internal
//                         pointer that must never be printed, the Phase-22
//                         HTREEITEM lesson).
//   GetButtonState(id)  -> TB_GETSTATE (TBSTATE_ENABLED=4 / TBSTATE_CHECKED=1)
//   Enable(id,on)       -> TB_ENABLEBUTTON (lParam = 0/1)
//   Check(id,on)        -> TB_CHECKBUTTON (lParam = 0/1)
//   IsChecked(id)       -> TB_ISBUTTONCHECKED (returns TRUE only for a
//                         TBSTYLE_CHECK button that is checked)
//   Delete(index)       -> TB_DELETEBUTTON (removes by zero-based index)
//   IndexOf(id)         -> TB_COMMANDTOINDEX (-1 when the id is absent)
//   GetItemRect(index)  -> TB_GETITEMRECT into a pinned int[4]; the four edges
//                         are exposed as RectLeft/RectTop/RectRight/RectBottom
//                         (a bool return -- the transpiler's M3 return-type
//                         surface does not support a managed int[] return, so
//                         the rect is carried on instance fields, the same
//                         shape as every other field-based wrapper here). The
//                         caller computes the click center from the fields.
public class WfToolStrip
{
    public IntPtr Handle;
    public int Id;
    public int RectLeft;
    public int RectTop;
    public int RectRight;
    public int RectBottom;

    public bool Add(string text, int id, int style, int state)
    {
        IntPtr p = IntPtr.Zero;
        if (text != null && text.Length > 0)
            p = Marshal.StringToHGlobalUni(text);
        int[] tb = new int[8];
        tb[0] = Cases.Winforms1.I_IMAGENONE;
        tb[1] = id;
        tb[2] = state | (style << 8);
        tb[3] = 0;
        tb[4] = 42; tb[5] = 0;
        long pt = p.ToInt64();
        tb[6] = (int)(pt & 0xFFFFFFFFL);
        tb[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(tb, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_ADDBUTTONSW, new IntPtr(1), pin.AddrOfPinnedObject());
        pin.Free();
        if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public string GetText(int id)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_GETBUTTONTEXTW, new IntPtr(id), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public int GetButtonState(int id)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_GETSTATE, new IntPtr(id), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public bool Enable(int id, bool on)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_ENABLEBUTTON, new IntPtr(id), new IntPtr(on ? 1 : 0));
        return r.ToInt64() != 0;
    }

    public bool Check(int id, bool on)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_CHECKBUTTON, new IntPtr(id), new IntPtr(on ? 1 : 0));
        return r.ToInt64() != 0;
    }

    public bool IsChecked(int id)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_ISBUTTONCHECKED, new IntPtr(id), IntPtr.Zero);
        return r.ToInt64() != 0;
    }

    public bool Delete(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_DELETEBUTTON, new IntPtr(index), IntPtr.Zero);
        return r.ToInt64() != 0;
    }

    public int IndexOf(int id)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_COMMANDTOINDEX, new IntPtr(id), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public bool GetItemRect(int index)
    {
        int[] rc = new int[4];
        GCHandle pin = GCHandle.Alloc(rc, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.TB_GETITEMRECT, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        RectLeft = rc[0];
        RectTop = rc[1];
        RectRight = rc[2];
        RectBottom = rc[3];
        return r.ToInt64() != 0;
    }
}

// Phase 33 managed model: DATA GRID VIEW (simplified). WfDataGridView wraps a
// report-view "SysListView32" HWND (created with WS_CHILD | LVS_REPORT -- the
// multi-column layout a grid needs, the same comctl32 class the Phase-21
// ListView uses, so the whole channel is the existing SendMessageW one; zero
// new P/Invoke / shim / struct):
//   Handle / Id         -> the HWND + control id (the same shape as every
//                          other wrapper here)
//   AddColumn(text,fmt,cx) -> builds an LVCOLUMNW as a GCHandle-PINNED int[13]
//                          (mask@0 / fmt@4 / cx@8 / pszText@16 / cchTextMax@24
//                          / iSubItem@28 -- the offsetof-probed x64 layout)
//                          and inserts it at the CURRENT column count (the
//                          explicit-index append: LVM_INSERTCOLUMNW takes the
//                          column index in wParam, probed 0/1/2 -> 0/1/2).
//                          Returns the new column's index.
//   ColumnCount         -> LVM_GETHEADER (the list-view's own header HWND) ->
//                          HDM_GETITEMCOUNT (the header control's column count)
//   GetColumnText(i)    -> LVM_GETCOLUMNW with an LVCF_TEXT mask + a 256-byte
//                          AllocHGlobal buffer (PtrToStringUni reads the header
//                          text back -- the DETERMINISTIC read).
//   GetColumnFmt(i)     -> LVM_GETCOLUMNW; the fmt field reads back as the
//                          header's internal HDF flags: HDF_STRING(0x4000) |
//                          alignment (probed LEFT=16384 / CENTER=16386 /
//                          RIGHT=16385 -- byte-exact, the net48 oracle).
//   GetColumnWidth(i)   -> LVM_GETCOLUMNWIDTH -- a SCALAR message (wParam = the
//                          column index, return = the pixel width).
//   SetColumnWidth(i,cx)-> LVM_SETCOLUMNWIDTH -- the scalar setter.
//   SetGridLines(on)    -> LVM_SETEXTENDEDLISTVIEWSTYLE with
//                          LVS_EX_GRIDLINES | LVS_EX_FULLROWSELECT (returns the
//                          PREVIOUS extended style); GetGridLines reads it back
//                          (probed: prev 0 -> now 33 = 0x21).
//   Insert(text)        -> the Phase-21 row append (LVM_INSERTITEMW with the
//                          explicit index = the current row count).
//   Count               -> LVM_GETITEMCOUNT (the row count).
//   SetCellText(row,col,text) / GetCellText(row,col) -> LVM_SETITEMTEXTW /
//                          LVM_GETITEMTEXTW with the LVITEMW iSubItem field
//                          (lvi[2]) = the COLUMN -- the per-cell channel.
//   RaiseColumnHeaderClick(col) -> records the clicked column into
//                          P33ColClick (the event surface; the headless
//                          acceptance posts WF_GRID_COLCLICK with wParam = the
//                          column index).
public class WfDataGridView
{
    public IntPtr Handle;
    public int Id;

    public int AddColumn(string text, int fmt, int cx)
    {
        int index = ColumnCount;
        IntPtr p = IntPtr.Zero;
        if (text != null && text.Length > 0)
            p = Marshal.StringToHGlobalUni(text);
        int[] col = new int[13];
        col[0] = Cases.Winforms1.LVCF_TEXT | Cases.Winforms1.LVCF_WIDTH | Cases.Winforms1.LVCF_FMT | Cases.Winforms1.LVCF_SUBITEM;
        col[1] = fmt;
        col[2] = cx;
        long pt = p.ToInt64();
        col[4] = (int)(pt & 0xFFFFFFFFL);
        col[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        col[6] = 128;
        col[7] = index;
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int ColumnCount
    {
        get
        {
            IntPtr hHead = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            IntPtr r = Cases.Winforms1.SendMessageW(hHead, Cases.Winforms1.HDM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public string GetColumnText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] col = new int[13];
        col[0] = Cases.Winforms1.LVCF_TEXT;
        col[6] = 128;
        long bp = buf.ToInt64();
        col[4] = (int)(bp & 0xFFFFFFFFL);
        col[5] = (int)((bp >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public int GetColumnFmt(int index)
    {
        int[] col = new int[13];
        col[0] = Cases.Winforms1.LVCF_FMT;
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return col[1];
    }

    public int GetColumnWidth(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCOLUMNWIDTH, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public bool SetColumnWidth(int index, int cx)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETCOLUMNWIDTH, new IntPtr(index), new IntPtr(cx));
        return r.ToInt64() != 0;
    }

    public bool SetGridLines(bool on)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETEXTENDEDLISTVIEWSTYLE,
            new IntPtr(Cases.Winforms1.LVS_EX_GRIDLINES | Cases.Winforms1.LVS_EX_FULLROWSELECT),
            new IntPtr(on ? (Cases.Winforms1.LVS_EX_GRIDLINES | Cases.Winforms1.LVS_EX_FULLROWSELECT) : 0));
        return r.ToInt64() != 0;
    }

    public int GetGridLines()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public int Insert(string text)
    {
        int iItem = Count;
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT | Cases.Winforms1.LVIF_PARAM;
        lvi[1] = iItem;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        lvi[10] = 42; lvi[11] = 0;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public bool SetCellText(int row, int col, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT;
        lvi[1] = row;
        lvi[2] = col;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETITEMTEXTW, new IntPtr(row), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    public string GetCellText(int row, int col)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT;
        lvi[1] = row;
        lvi[2] = col;
        long bp = buf.ToInt64();
        lvi[6] = (int)(bp & 0xFFFFFFFFL);
        lvi[7] = (int)((bp >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMTEXTW, new IntPtr(row), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public void RaiseColumnHeaderClick(int col)
    {
        Cases.Winforms1.P33ColClick = col;
    }

    // Phase 43 (additive to the Phase-33 surface): the column DISPLAY order.
    // The columns are reordered without changing their indices -- the backing
    // of WinForms Columns.DisplayIndex / AllowColumnReorder. The order array is
    // a bare GCHandle-PINNED int[3] (order[i] = the COLUMN shown at display
    // position i -- the exact Phase-40 "bare int[] as lParam" shape; net48
    // probed: after SetColumnOrder(2,0,1) the "Kind" column is physically
    // LEFTMOST, and LVM_GETCOLUMNORDERARRAY reads {2,0,1} back -- the
    // GETCOLUMNW text channel is untouched, so GetColumnText(0) still reads
    // "Name" (display-only evidence)).
    //   GetColumnOrder()  -> LVM_GETCOLUMNORDERARRAY (wParam = 3) reads the
    //                        order array back and joins it into a STRING
    //                        "o0;o1;o2;" (NO int[] return -- the M3 SZArray
    //                        restriction, the Phase-40/42 readback pattern).
    //   SetColumnOrder(o0,o1,o2) -> LVM_SETCOLUMNORDERARRAY (wParam = 3) sets
    //                        the display order; returns the native BOOL.
    //   DisplayIndex(col) -> LVM_GETCOLUMNORDERARRAY readback + reverse lookup:
    //                        the position i whose order[i] == col (probed
    //                        0/1/2 -> 1/2/0 after {2,0,1}).
    public string GetColumnOrder()
    {
        int[] order = new int[3];
        GCHandle pin = GCHandle.Alloc(order, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCOLUMNORDERARRAY, new IntPtr(3), pin.AddrOfPinnedObject());
        pin.Free();
        string s = order[0] + ";" + order[1] + ";" + order[2] + ";";
        return s;
    }

    public bool SetColumnOrder(int o0, int o1, int o2)
    {
        int[] order = new int[3] { o0, o1, o2 };
        GCHandle pin = GCHandle.Alloc(order, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETCOLUMNORDERARRAY, new IntPtr(3), pin.AddrOfPinnedObject());
        pin.Free();
        return r.ToInt64() != 0;
    }

    public int DisplayIndex(int col)
    {
        int[] order = new int[3];
        GCHandle pin = GCHandle.Alloc(order, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCOLUMNORDERARRAY, new IntPtr(3), pin.AddrOfPinnedObject());
        pin.Free();
        for (int i = 0; i < 3; i++)
            if (order[i] == col) return i;
        return -1;
    }
}

// Phase 34 managed model: PROGRESS BAR complete surface. WfProgressBar wraps a
// "msctls_progress32" HWND (the same comctl32 class as Phase 9, so the whole
// channel is the existing SendMessageW one; zero new P/Invoke / shim / struct
// -- the PBRANGE for PBM_GETRANGE is a GCHandle-PINNED int[2]):
//   Handle / Id         -> the HWND + control id (the same shape as every
//                          other wrapper here)
//   Value               -> get = PBM_GETPOS, set = PBM_SETPOS (records the
//                          PREVIOUS position into PrevValue -- probed: the bar
//                          CLAMPS out-of-range writes to min/max).
//   Step                -> get = PBM_GETSTEP, set = PBM_SETSTEP (records the
//                          PREVIOUS step into PrevStep). v5.82 probe: GETSTEP
//                          reads 0 always (a dead read); SETSTEP returns the
//                          previous step (default 10) and sets the step
//                          PBM_STEPIT advances by.
//   PerformStep()       -> PBM_STEPIT (advances by Step, returns the PREVIOUS
//                          position). v5.82 probe: when pos+step exceeds the
//                          max the bar WRAPS (pos = min + (pos+step-max) =
//                          pos+step-(max-min)), it does NOT cap -- probed
//                          140+25 -> 35 on range 20..150.
//   Minimum / Maximum   -> PBM_GETRANGE through the PBRANGE lParam (a pinned
//                          int[2]: [0]=min, [1]=max). v5.82 probe: the
//                          GETRANGE RETURN is the max only (high word 0); the
//                          struct is the full min/max channel.
//   SetRange(min,max)   -> PBM_SETRANGE32 (records the PREVIOUS range into
//                          PrevRangeMin/Max -- decoded from the return, which
//                          packs MAKELONG(min,max): LOWORD=min, HIWORD=max).
//   BarColor            -> get = PBM_GETBARCOLOR (v6-only: v5.82 returns 0),
//                          set = PBM_SETBARCOLOR (records the PREVIOUS color
//                          into PrevBarColor -- v5.82 default is 0xFF000000).
//   BkColor             -> set = PBM_SETBKCOLOR (records the PREVIOUS color
//                          into PrevBkColor; v5.82 default 0xFF000000).
public class WfProgressBar
{
    public IntPtr Handle;
    public int Id;

    public int PrevValue;
    public int PrevStep;
    public int PrevRangeMin;
    public int PrevRangeMax;
    public int PrevBarColor;
    public int PrevBkColor;

    public int Value
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_SETPOS, new IntPtr(value), IntPtr.Zero);
            PrevValue = (int)r.ToInt64();
        }
    }

    public int Step
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_GETSTEP, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_SETSTEP, new IntPtr(value), IntPtr.Zero);
            PrevStep = (int)r.ToInt64();
        }
    }

    public int PerformStep()
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    private int RangeMin;
    private int RangeMax;
    private void ReadRange()
    {
        int[] range = new int[2];
        GCHandle pin = GCHandle.Alloc(range, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_GETRANGE, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        RangeMin = range[0];
        RangeMax = range[1];
    }

    public int Minimum
    {
        get { ReadRange(); return RangeMin; }
    }

    public int Maximum
    {
        get { ReadRange(); return RangeMax; }
    }

    public bool SetRange(int min, int max)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_SETRANGE32, new IntPtr(min), new IntPtr(max));
        long raw = r.ToInt64();
        PrevRangeMin = (int)(raw & 0xFFFFL);
        PrevRangeMax = (int)((raw >> 16) & 0xFFFFL);
        return r != IntPtr.Zero;
    }

    public int BarColor
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_GETBARCOLOR, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_SETBARCOLOR, IntPtr.Zero, new IntPtr(value));
            PrevBarColor = (int)r.ToInt64();
        }
    }

    public int BkColor
    {
        set
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.PBM_SETBKCOLOR, IntPtr.Zero, new IntPtr(value));
            PrevBkColor = (int)r.ToInt64();
        }
    }
}

    // ===================== Phase 47: managed drag-drop event chain =====================
    // A pure-MANAGED drop-target model (the COM dispatch bridge is COM stage
    // 17, test_com section 22 -- THIS phase exercises the managed event chain
    // itself, byte-exact on both compilers because it never touches a COM
    // icall). Minimal DragEventArgs carries the drop payload (X/Y/Effect); the
    // IDropTarget shape matches the WinForms DoDragDrop/DragDrop model; the
    // WfDropSink implementation records every entry and the phase block feeds a
    // synthetic drop sequence through the chain, mirroring into the status bar.
    public class DragEventArgs47
    {
        public int X;
        public int Y;
        public int Effect;
        public DragEventArgs47(int x, int y, int effect)
        {
            X = x; Y = y; Effect = effect;
        }
    }

    public interface IDropTarget47
    {
        void DragEnter(DragEventArgs47 ev);
        void DragOver(DragEventArgs47 ev);
        void DragLeave(DragEventArgs47 ev);
        void Drop(DragEventArgs47 ev);
    }

    public class WfDropSink47 : IDropTarget47
    {
        public int EnterCount;
        public int OverCount;
        public int LeaveCount;
        public int DropCount;
        public int LastX;
        public int LastY;
        public int LastEffect;
        public int EnterEffect;
        public void DragEnter(DragEventArgs47 ev)
        {
            EnterCount++;
            LastX = ev.X; LastY = ev.Y; LastEffect = ev.Effect;
            EnterEffect = ev.Effect + 1; // the "handled" mutation
        }
        public void DragOver(DragEventArgs47 ev)
        {
            OverCount++;
            LastX = ev.X; LastY = ev.Y; LastEffect = ev.Effect;
        }
        public void DragLeave(DragEventArgs47 ev)
        {
            LeaveCount++;
        }
        public void Drop(DragEventArgs47 ev)
        {
            DropCount++;
            LastX = ev.X; LastY = ev.Y; LastEffect = ev.Effect;
        }
    }

// Phase 48: sortable list grid. WfSortableGrid wraps a WC_LISTVIEW with the
// Phase-21/33 item surface (AddColumn/Insert/Count/GetItemText) plus the
// header sort channel (HDM_SORTITEMS). The rows carry their logical index in
// the LPARAM so the sort callback can resolve each row's sort key from the
// static SortKeys array (the callback signature only receives the two
// lParams). Sorting: Sort(column) sends HDM_SORTITEMS to the list header
// with a GetFunctionPointerForDelegate of the static SortByKey comparator;
// comctl32 reorders the rows so the lParam sequence follows the callback's
// compare order. Readback: GetRowLParam uses LVM_GETITEMW (mask LVIF_PARAM)
// and GetItemText uses LVM_GETITEMTEXTW -- both existing channels.
public class WfSortableGrid
{
    public IntPtr Handle;
    public int Id;
    public static int[] SortKeys = { 2, 0, 1 };
    public static int SortCallbacks = 0;

    public static int SortByKey(int l1, int l2, int sort)
    {
        SortCallbacks++;
        int k1 = SortKeys[l1];
        int k2 = SortKeys[l2];
        if (k1 < k2) return -1;
        if (k1 > k2) return 1;
        return 0;
    }

    public int AddColumn(string text, int fmt, int cx, int index)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        long pt = p.ToInt64();
        int[] col = new int[13];
        col[0] = Cases.Winforms1.LVCF_TEXT | Cases.Winforms1.LVCF_FMT | Cases.Winforms1.LVCF_WIDTH;
        col[1] = fmt;
        col[2] = cx;
        col[4] = (int)(pt & 0xFFFFFFFFL);
        col[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        col[6] = 128;
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Insert(string text, int lparam)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT | Cases.Winforms1.LVIF_PARAM;
        lvi[1] = Count;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        lvi[10] = lparam; lvi[11] = 0;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public string GetItemText(int row)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT;
        lvi[1] = row;
        long bp = buf.ToInt64();
        lvi[6] = (int)(bp & 0xFFFFFFFFL);
        lvi[7] = (int)((bp >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMTEXTW, new IntPtr(row), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public int GetRowLParam(int row)
    {
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_PARAM;
        lvi[1] = row;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMW, new IntPtr(row), pin.AddrOfPinnedObject());
        pin.Free();
        long lp = ((long)lvi[11] << 32) | (long)lvi[10] & 0xFFFFFFFFL;
        return (int)lp;
    }

    public bool Sort(int column)
    {
        // LVM_SORTITEMS (0x1030, LVM_FIRST+48) is sent to the LIST window
        // (not the header) with lParam = the PFNLVCOMPARE funptr; comctl32
        // calls the callback for each pairwise comparison and reorders the
        // items so the lParam sequence follows the callback's ordering.
        SortCallbacks = 0;
        Cases.Winforms1.SortCompare cb = SortByKey;
        IntPtr fn = Marshal.GetFunctionPointerForDelegate(cb);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SORTITEMS, new IntPtr(column), fn);
        return r.ToInt64() != 0;
    }
}

// Phase 49: find + auto-width list grid. WfFindGrid wraps a WC_LISTVIEW with
// the item/column surface plus LVM_FINDITEMW (prefix search over the item
// texts) and LVM_SETCOLUMNWIDTH -1 (LVSCW_AUTOSIZE -- comctl32 sizes the
// column to its widest cell). LVFINDINFOW is carried as a pinned int[10]
// (flags@0, pszText@8 -- a pointer split across two int32s, lParam@16, pt@24,
// vkDirection@32 -- 40 bytes, 8-aligned like the pinned array).
public class WfFindGrid
{
    public IntPtr Handle;
    public int Id;

    public int AddColumn(string text, int fmt, int cx, int index)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        long pt = p.ToInt64();
        int[] col = new int[13];
        col[0] = Cases.Winforms1.LVCF_TEXT | Cases.Winforms1.LVCF_FMT | Cases.Winforms1.LVCF_WIDTH;
        col[1] = fmt;
        col[2] = cx;
        col[4] = (int)(pt & 0xFFFFFFFFL);
        col[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        col[6] = 128;
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Insert(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = Cases.Winforms1.LVIF_TEXT;
        lvi[1] = Count;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public int GetColumnWidth(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_GETCOLUMNWIDTH, new IntPtr(index), IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public bool AutoSizeColumn(int index)
    {
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_SETCOLUMNWIDTH, new IntPtr(index), new IntPtr(Cases.Winforms1.LVSCW_AUTOSIZE));
        return r.ToInt64() != 0;
    }

    public int Find(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] fi = new int[10];
        fi[0] = Cases.Winforms1.LVFI_STRING;
        long pt = p.ToInt64();
        fi[2] = (int)(pt & 0xFFFFFFFFL);
        fi[3] = (int)((pt >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(fi, GCHandleType.Pinned);
        IntPtr r = Cases.Winforms1.SendMessageW(Handle, Cases.Winforms1.LVM_FINDITEMW, new IntPtr(-1), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }
}
