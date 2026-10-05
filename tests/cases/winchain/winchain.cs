// winchain.cs -- WinForms full-chain deep case: MULTI-CONTROL composition +
// EVENT CHAINS across the stages 1..17 surface. Instead of probing each
// control in isolation (winforms1 does that), this case wires FIVE controls
// into ONE form and drives EVENT CHAINS where one control's event triggers
// another control's state change, which in turn triggers a third -- the
// WinForms "controls talk to each other through events" composition model.
//
// The stage-17 scroll domain is the anchor; the chains reuse the winforms1
// wired channels (all proven byte-exact):
//
//   CONTROLS:
//     * Button  ("BUTTON" class, id ID_CHAIN_BTN)      -- the click source
//     * ScrollBar ("SCROLLBAR", SBS_VERT, id ID_CHAIN_SCROLL) -- the range/
//       position channel (stage-17) driven by SetScrollRange/SetScrollPos/
//       GetScrollPos
//     * StatusBar ("msctls_statusbar32", comctl32)     -- SB_SETTEXT /
//       SB_GETTEXT read-back channel
//     * ComboBox ("COMBOBOX", id ID_CHAIN_COMBO)       -- CB_ADDSTRING /
//       CB_GETCURSEL / CB_SETCURSEL; CBN_SELCHANGE event
//     * ListBox  ("LISTBOX", id ID_CHAIN_LIST)         -- LB_ADDSTRING /
//       LB_SETCURSEL / LB_GETCURSEL; LBN_SELCHANGE event
//     * TabControl ("SysTabControl32", id ID_CHAIN_TAB) -- TCM_* / WM_NOTIFY
//       TCN_SELCHANGE -> SelectedIndexChanged
//     * EditBox  ("EDIT", id ID_CHAIN_EDIT)            -- WM_SETTEXT +
//       GetWindowTextW (the text sink)
//
//   EVENT CHAINS (each chain is a deterministic posted-message sequence):
//     CHAIN A  click -> scroll -> status:
//         PostMessage WM_COMMAND(ID_CHAIN_BTN) -> the button's Click handler
//         advances the scrollbar (SetScrollPos), the scroll event re-reads
//         the real position (GetScrollPos) and mirrors "Scroll: <pos>" into
//         the status bar (SB_SETTEXT + SB_GETTEXT round-trip). Sequence:
//         click;scroll:pos;status:<text>;  -- the SAME observable state a
//         real user click + thumb drag produces.
//     CHAIN B  combo selection -> edit text:
//         ComboBox.Add(item) via CB_ADDSTRING, CB_SETCURSEL selects it; a
//         posted WM_COMMAND with CBN_SELCHANGE fires the SelectionChanged
//         event which writes "Selected: <item>" into the EDIT (WM_SETTEXT +
//         GetWindowTextW read-back). ListBox does the same on its own id
//         (LBN_SELCHANGE) -- the two list controls share the dispatch but
//         keep per-control state.
//     CHAIN C  tab switch -> status:
//         TCM_SETCURSEL selects a page; a posted WM_NOTIFY TCN_SELCHANGE
//         fires SelectedIndexChanged which re-reads the current selection
//         and mirrors "Tab: <index>" into the status bar.
//     CHAIN D  two-hop event chain (scroll drives BOTH status AND the edit):
//         a posted WF_CHAIN_SCROLL drives the scrollbar; the dispatch writes
//         the thumb, re-reads the position AND writes "Scroll: <pos>" into
//         BOTH the status bar and the edit text (one event, two sinks).
//
//   Each chain's observable output (counts + seq strings + read-back texts)
//   is deterministic: all windows are HIDDEN, every interaction is
//   PostMessageW + PeekMessageW drain, and the dispatch order is the posted
//   FIFO order. net48 oracle + both transpiled compilers byte-exact.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class WinChain
{
    // --- window / message constants ---
    public const int WM_CREATE = 0x0001;
    public const int WM_COMMAND = 0x0111;
    public const int WM_NOTIFY = 0x004E;
    public const int WM_VSCROLL = 0x0115;
    public const int WM_CLOSE = 0x0010;
    public const int WM_SETTEXT = 0x000C;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CHILD = 0x40000000;
    public const int WS_TABSTOP = 0x00010000;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const int PM_REMOVE = 0x0001;
    public const int SB_CTL = 0x0002;
    public const int SB_THUMBPOSITION = 4;
    public const int SB_THUMBTRACK = 5;

    // --- per-control ids ---
    public const int ID_CHAIN_BTN = 601;
    public const int ID_CHAIN_SCROLL = 602;
    public const int ID_CHAIN_COMBO = 603;
    public const int ID_CHAIN_LIST = 604;
    public const int ID_CHAIN_TAB = 605;
    public const int ID_CHAIN_EDIT = 606;

    // --- custom posted messages (drive the chains headless) ---
    public const int WF_CHAIN_SCROLL = 0x8010;   // wParam = target position
    public const int WF_CHAIN_COMBO = 0x8011;    // select combo item
    public const int WF_CHAIN_LIST = 0x8012;     // select list item
    public const int WF_CHAIN_TAB = 0x8013;      // select tab page

    // --- stage-9 status channel ---
    public const int SB_SETTEXT = 0x040B;
    public const int SB_GETTEXT = 0x040D;
    public const int ICC_BAR_CLASSES = 0x00000004;
    public const string STATUSCLASSNAME = "msctls_statusbar32";

    // --- stage-8 list controls ---
    public const int CB_ADDSTRING = 0x0143;
    public const int CB_GETCURSEL = 0x0147;
    public const int CB_SETCURSEL = 0x014E;
    public const int CB_GETLBTEXT = 0x0148;
    public const int LB_ADDSTRING = 0x0180;
    public const int LB_SETCURSEL = 0x0186;
    public const int LB_GETCURSEL = 0x0188;
    public const int LB_GETTEXT = 0x0189;
    public const int CBN_SELCHANGE = 1;
    public const int LBN_SELCHANGE = 1;
    public const int CBS_DROPDOWNLIST = 0x0003;
    public const int LBS_NOTIFY = 0x0001;

    // --- stage-16 tab channel ---
    public const string WC_TABCONTROL = "SysTabControl32";
    public const int ICC_TAB_CLASSES = 0x00000008;
    public const int TCM_FIRST = 0x1300;
    public const int TCM_GETITEMCOUNT = TCM_FIRST + 4;
    public const int TCM_GETCURSEL = TCM_FIRST + 11;
    public const int TCM_SETCURSEL = TCM_FIRST + 12;
    public const int TCM_INSERTITEMW = TCM_FIRST + 62;
    public const int TCIF_PARAM = 0x0004;
    public const int TCN_SELCHANGE = -10;

    // --- stage-17 scroll channel ---
    public const string SCROLLBAR_CLASS = "SCROLLBAR";
    public const int SBS_VERT = 0x0001;

    // --- structs (winforms1 layouts) ---
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

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX {
        public int dwSize;
        public int dwICC;
    }

    public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // --- P/Invoke surface ---
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

    [DllImport("user32", EntryPoint = "PostMessageW")]
    public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "PostQuitMessage")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32", EntryPoint = "PeekMessageW")]
    public static extern int PeekMessageW(out MSG msg, IntPtr hwnd, int min, int max, int remove);

    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowTextW(IntPtr hwnd, IntPtr lpString, int nMaxCount);

    [DllImport("user32", EntryPoint = "SetScrollRange")]
    public static extern int SetScrollRange(IntPtr hwnd, int nBar, int nMinPos, int nMaxPos, bool fRedraw);

    [DllImport("user32", EntryPoint = "SetScrollPos")]
    public static extern int SetScrollPos(IntPtr hwnd, int nBar, int nPos, bool fRedraw);

    [DllImport("user32", EntryPoint = "GetScrollPos")]
    public static extern int GetScrollPos(IntPtr hwnd, int nBar);

    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    // --- observable state ---
    public static IntPtr FormHwnd;
    public static IntPtr ScrollHwnd;
    public static IntPtr StatusHwnd;
    public static IntPtr ComboHwnd;
    public static IntPtr ListHwnd;
    public static IntPtr TabHwnd;
    public static IntPtr EditHwnd;

    public static int ClickCount;
    public static int ScrollCount;
    public static int ComboSelCount;
    public static int ListSelCount;
    public static int TabSelCount;
    public static string StatusText = "";
    public static string EditText = "";
    public static string ChainSeq = "";
    public static int ChainScrollCount;
    public static int ComboItems;
    public static int ListItems;

    public static void SetStatus(string text)
    {
        IntPtr st = Marshal.StringToHGlobalUni(text);
        SendMessageW(StatusHwnd, SB_SETTEXT, IntPtr.Zero, st);
        Marshal.FreeHGlobal(st);
        IntPtr sb = Marshal.AllocHGlobal(256);
        SendMessageW(StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb);
        StatusText = Marshal.PtrToStringUni(sb);
        Marshal.FreeHGlobal(sb);
    }

    public static void SetEdit(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        SendMessageW(EditHwnd, WM_SETTEXT, IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        IntPtr buf = Marshal.AllocHGlobal(256);
        GetWindowTextW(EditHwnd, buf, 256);
        EditText = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
    }

    public static void OnClick()
    {
        // CHAIN A: button click advances the scrollbar; the scroll then
        // mirrors into status. ChainSeq records click;scroll;status;.
        ClickCount++;
        SetScrollPos(ScrollHwnd, SB_CTL, ClickCount * 10, false);
        int pos = GetScrollPos(ScrollHwnd, SB_CTL);
        ScrollCount++;
        SetStatus("Scroll: " + pos);
        ChainSeq = ChainSeq + "click;scroll:" + pos + ";status:" + StatusText + ";";
    }

    public static void OnComboSel()
    {
        ComboSelCount++;
        int sel = (int)SendMessageW(ComboHwnd, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
        IntPtr buf = Marshal.AllocHGlobal(256);
        SendMessageW(ComboHwnd, CB_GETLBTEXT, new IntPtr(sel), buf);
        string item = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        SetEdit("Selected: " + item);
        ChainSeq = ChainSeq + "combo:" + sel + ";edit:" + EditText + ";";
    }

    public static void OnListSel()
    {
        ListSelCount++;
        int sel = (int)SendMessageW(ListHwnd, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
        IntPtr buf = Marshal.AllocHGlobal(256);
        SendMessageW(ListHwnd, LB_GETTEXT, new IntPtr(sel), buf);
        string item = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        SetEdit("List: " + item);
        ChainSeq = ChainSeq + "list:" + sel + ";edit:" + EditText + ";";
    }

    public static void OnTabSel()
    {
        // CHAIN C: tab switch re-reads the current selection and mirrors into
        // the status bar.
        TabSelCount++;
        int sel = (int)SendMessageW(TabHwnd, TCM_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
        SetStatus("Tab: " + sel);
        ChainSeq = ChainSeq + "tab:" + sel + ";status:" + StatusText + ";";
    }

    public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) return IntPtr.Zero;
        if (msg == WM_COMMAND)
        {
            int cid = (int)(wParam.ToInt64() & 0xFFFFL);
            int notif = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            if (cid == ID_CHAIN_BTN) OnClick();
            else if (cid == ID_CHAIN_COMBO && notif == CBN_SELCHANGE) OnComboSel();
            else if (cid == ID_CHAIN_LIST && notif == LBN_SELCHANGE) OnListSel();
            return IntPtr.Zero;
        }
        if (msg == WF_CHAIN_SCROLL)
        {
            // CHAIN D: one scroll event writes BOTH the status bar AND the edit.
            int target = (int)wParam.ToInt64();
            SetScrollPos(ScrollHwnd, SB_CTL, target, false);
            int pos = GetScrollPos(ScrollHwnd, SB_CTL);
            ChainScrollCount++;
            SetStatus("Scroll: " + pos);
            SetEdit("Scroll: " + pos);
            ChainSeq = ChainSeq + "wscroll:" + pos + ";status:" + StatusText + ";edit:" + EditText + ";";
            return IntPtr.Zero;
        }
        if (msg == WM_VSCROLL)
        {
            int code = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            if (code == SB_THUMBTRACK || code == SB_THUMBPOSITION)
            {
                SetScrollPos(ScrollHwnd, SB_CTL, thumb, false);
            }
            int pos = GetScrollPos(ScrollHwnd, SB_CTL);
            ScrollCount++;
            SetStatus("Scroll: " + pos);
            ChainSeq = ChainSeq + "vscroll:" + pos + ";status:" + StatusText + ";";
            return IntPtr.Zero;
        }
        if (msg == WF_CHAIN_COMBO)
        {
            int item = (int)wParam.ToInt64();
            SendMessageW(ComboHwnd, CB_SETCURSEL, new IntPtr(item), IntPtr.Zero);
            PostMessageW(hwnd, WM_COMMAND, new IntPtr(ID_CHAIN_COMBO | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (msg == WF_CHAIN_LIST)
        {
            int item = (int)wParam.ToInt64();
            SendMessageW(ListHwnd, LB_SETCURSEL, new IntPtr(item), IntPtr.Zero);
            PostMessageW(hwnd, WM_COMMAND, new IntPtr(ID_CHAIN_LIST | (LBN_SELCHANGE << 16)), IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (msg == WF_CHAIN_TAB)
        {
            int page = (int)wParam.ToInt64();
            SendMessageW(TabHwnd, TCM_SETCURSEL, new IntPtr(page), IntPtr.Zero);
            PostMessageW(hwnd, WM_NOTIFY, new IntPtr(ID_CHAIN_TAB), IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (msg == WM_NOTIFY)
        {
            int id = (int)(wParam.ToInt64() & 0xFFFFL);
            if (id == ID_CHAIN_TAB) OnTabSel();
            return IntPtr.Zero;
        }
        if (msg == WM_CLOSE) return IntPtr.Zero;
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public static void Main()
    {
        IntPtr hModule = GetModuleHandleW(null);
        Console.WriteLine("module_nonzero=" + (hModule != IntPtr.Zero));

        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        RootWndProc = WndProcImpl;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
        wc.hInstance = hModule;
        IntPtr clsName = Marshal.StringToHGlobalUni("WinChainFormClass");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("class_registered=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hForm = CreateWindowExW(0, "WinChainFormClass", "WinChain",
            WS_OVERLAPPED, 0, 0, 500, 400,
            IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
        FormHwnd = hForm;
        Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));

        // --- common controls (status bar + tab) ---
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_BAR_CLASSES | ICC_TAB_CLASSES;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("comctl_init=" + (iccRet != 0));

        // --- five controls on one form ---
        IntPtr hStatus = CreateWindowExW(0, STATUSCLASSNAME, "",
            WS_CHILD, 0, 0, 500, 20, hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        StatusHwnd = hStatus;
        Console.WriteLine("statusbar_created=" + (hStatus != IntPtr.Zero));

        IntPtr hBtn = CreateWindowExW(0, "BUTTON", "Chain",
            WS_CHILD | WS_TABSTOP, 20, 40, 100, 30,
            hForm, new IntPtr(ID_CHAIN_BTN), hModule, IntPtr.Zero);
        Console.WriteLine("button_created=" + (hBtn != IntPtr.Zero));

        IntPtr hScroll = CreateWindowExW(0, SCROLLBAR_CLASS, "",
            WS_CHILD | SBS_VERT, 20, 90, 20, 160, hForm, new IntPtr(ID_CHAIN_SCROLL), hModule, IntPtr.Zero);
        ScrollHwnd = hScroll;
        Console.WriteLine("scrollbar_created=" + (hScroll != IntPtr.Zero));

        IntPtr hCombo = CreateWindowExW(0, "COMBOBOX", "",
            WS_CHILD | WS_TABSTOP | CBS_DROPDOWNLIST, 60, 90, 140, 100,
            hForm, new IntPtr(ID_CHAIN_COMBO), hModule, IntPtr.Zero);
        ComboHwnd = hCombo;
        Console.WriteLine("combobox_created=" + (hCombo != IntPtr.Zero));

        IntPtr hList = CreateWindowExW(0, "LISTBOX", "",
            WS_CHILD | WS_TABSTOP | LBS_NOTIFY, 220, 90, 140, 100,
            hForm, new IntPtr(ID_CHAIN_LIST), hModule, IntPtr.Zero);
        ListHwnd = hList;
        Console.WriteLine("listbox_created=" + (hList != IntPtr.Zero));

        IntPtr hTab = CreateWindowExW(0, WC_TABCONTROL, "",
            WS_CHILD, 60, 200, 300, 120, hForm, new IntPtr(ID_CHAIN_TAB), hModule, IntPtr.Zero);
        TabHwnd = hTab;
        Console.WriteLine("tab_created=" + (hTab != IntPtr.Zero));

        IntPtr hEdit = CreateWindowExW(0, "EDIT", "",
            WS_CHILD | WS_TABSTOP, 60, 330, 200, 24,
            hForm, new IntPtr(ID_CHAIN_EDIT), hModule, IntPtr.Zero);
        EditHwnd = hEdit;
        Console.WriteLine("edit_created=" + (hEdit != IntPtr.Zero));

        // --- wire the scroll range + populate the list controls + tabs ---
        int range = SetScrollRange(hScroll, SB_CTL, 0, 100, false);
        Console.WriteLine("scroll_range_set=" + (range != 0));
        Console.WriteLine("scroll_pos0=" + GetScrollPos(hScroll, SB_CTL));

        IntPtr cs1 = Marshal.StringToHGlobalUni("Alpha");
        SendMessageW(hCombo, CB_ADDSTRING, IntPtr.Zero, cs1);
        Marshal.FreeHGlobal(cs1);
        IntPtr cs2 = Marshal.StringToHGlobalUni("Beta");
        SendMessageW(hCombo, CB_ADDSTRING, IntPtr.Zero, cs2);
        Marshal.FreeHGlobal(cs2);
        ComboItems = 2;
        Console.WriteLine("combo_items=" + ComboItems);

        IntPtr ls1 = Marshal.StringToHGlobalUni("Red");
        SendMessageW(hList, LB_ADDSTRING, IntPtr.Zero, ls1);
        Marshal.FreeHGlobal(ls1);
        IntPtr ls2 = Marshal.StringToHGlobalUni("Green");
        SendMessageW(hList, LB_ADDSTRING, IntPtr.Zero, ls2);
        Marshal.FreeHGlobal(ls2);
        IntPtr ls3 = Marshal.StringToHGlobalUni("Blue");
        SendMessageW(hList, LB_ADDSTRING, IntPtr.Zero, ls3);
        Marshal.FreeHGlobal(ls3);
        ListItems = 3;
        Console.WriteLine("list_items=" + ListItems);

        // Tabs via TCITEM (pinned int[] -- the winforms1 stage-16 layout).
        int[] t0 = new int[12]; t0[0] = TCIF_PARAM; t0[8] = 0;
        GCHandle g0 = GCHandle.Alloc(t0, GCHandleType.Pinned);
        SendMessageW(hTab, TCM_INSERTITEMW, IntPtr.Zero, g0.AddrOfPinnedObject());
        g0.Free();
        int[] t1 = new int[12]; t1[0] = TCIF_PARAM; t1[8] = 1;
        GCHandle g1 = GCHandle.Alloc(t1, GCHandleType.Pinned);
        SendMessageW(hTab, TCM_INSERTITEMW, IntPtr.Zero, g1.AddrOfPinnedObject());
        g1.Free();
        Console.WriteLine("tab_count=" + (int)SendMessageW(hTab, TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("tab_sel0=" + (int)SendMessageW(hTab, TCM_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // ---- CHAIN A: click -> scroll -> status ----
        ChainSeq = "";
        PostMessageW(hForm, WM_COMMAND, new IntPtr(ID_CHAIN_BTN), IntPtr.Zero);
        PostMessageW(hForm, WM_COMMAND, new IntPtr(ID_CHAIN_BTN), IntPtr.Zero);
        PostMessageW(hForm, WM_COMMAND, new IntPtr(ID_CHAIN_BTN), IntPtr.Zero);
        MSG msgA;
        int da = 0;
        while (PeekMessageW(out msgA, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msgA);
            DispatchMessageW(ref msgA);
            da++;
            if (da >= 200) break;
        }
        Console.WriteLine("chainA_click_count=" + ClickCount);
        Console.WriteLine("chainA_scroll_count=" + ScrollCount);
        Console.WriteLine("chainA_pos=" + GetScrollPos(hScroll, SB_CTL));
        Console.WriteLine("chainA_status=" + StatusText);
        Console.WriteLine("chainA_seq=" + ChainSeq);

        // ---- CHAIN B: combo selection -> edit text ----
        ChainSeq = "";
        PostMessageW(hForm, WF_CHAIN_COMBO, new IntPtr(1), IntPtr.Zero);
        MSG msgB;
        int db = 0;
        while (PeekMessageW(out msgB, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msgB);
            DispatchMessageW(ref msgB);
            db++;
            if (db >= 200) break;
        }
        Console.WriteLine("chainB_combo_sel=" + (int)SendMessageW(hCombo, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("chainB_selcount=" + ComboSelCount);
        Console.WriteLine("chainB_edit=" + EditText);
        Console.WriteLine("chainB_seq=" + ChainSeq);

        // ---- CHAIN B2: list selection -> edit text (second sink, same chain) ----
        ChainSeq = "";
        PostMessageW(hForm, WF_CHAIN_LIST, new IntPtr(2), IntPtr.Zero);
        MSG msgB2;
        int db2 = 0;
        while (PeekMessageW(out msgB2, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msgB2);
            DispatchMessageW(ref msgB2);
            db2++;
            if (db2 >= 200) break;
        }
        Console.WriteLine("chainB2_list_sel=" + (int)SendMessageW(hList, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("chainB2_selcount=" + ListSelCount);
        Console.WriteLine("chainB2_edit=" + EditText);
        Console.WriteLine("chainB2_seq=" + ChainSeq);

        // ---- CHAIN C: tab switch -> status ----
        ChainSeq = "";
        PostMessageW(hForm, WF_CHAIN_TAB, new IntPtr(1), IntPtr.Zero);
        MSG msgC;
        int dc = 0;
        while (PeekMessageW(out msgC, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msgC);
            DispatchMessageW(ref msgC);
            dc++;
            if (dc >= 200) break;
        }
        Console.WriteLine("chainC_tab_sel=" + (int)SendMessageW(hTab, TCM_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("chainC_selcount=" + TabSelCount);
        Console.WriteLine("chainC_status=" + StatusText);
        Console.WriteLine("chainC_seq=" + ChainSeq);

        // ---- CHAIN D: one scroll -> BOTH status and edit (two-hop chain) ----
        ChainSeq = "";
        PostMessageW(hForm, WF_CHAIN_SCROLL, new IntPtr(70), IntPtr.Zero);
        MSG msgD;
        int dd = 0;
        while (PeekMessageW(out msgD, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msgD);
            DispatchMessageW(ref msgD);
            dd++;
            if (dd >= 200) break;
        }
        Console.WriteLine("chainD_scroll_count=" + ChainScrollCount);
        Console.WriteLine("chainD_pos=" + GetScrollPos(hScroll, SB_CTL));
        Console.WriteLine("chainD_status=" + StatusText);
        Console.WriteLine("chainD_edit=" + EditText);
        Console.WriteLine("chainD_seq=" + ChainSeq);

        // ---- final state: status reflects the LAST sink write (tab switch) ----
        Console.WriteLine("final_status=" + StatusText);
        Console.WriteLine("final_edit=" + EditText);

        // ---- teardown ----
        Console.WriteLine("destroy_scroll=" + (DestroyWindow(hScroll) != 0));
        Console.WriteLine("destroy_combo=" + (DestroyWindow(hCombo) != 0));
        Console.WriteLine("destroy_list=" + (DestroyWindow(hList) != 0));
        Console.WriteLine("destroy_tab=" + (DestroyWindow(hTab) != 0));
        Console.WriteLine("destroy_edit=" + (DestroyWindow(hEdit) != 0));
        Console.WriteLine("destroy_status=" + (DestroyWindow(hStatus) != 0));
        PostQuitMessage(0);

        MSG msg;
        int dispatched = 0;
        while (GetMessageW(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
            dispatched++;
            if (dispatched >= 200) break;
        }
        Console.WriteLine("pump_dispatched=" + dispatched);
    }

    public static WndProc RootWndProc;
}}
