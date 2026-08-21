// menuevt.cs -- MENU EVENT deep probe (e2e batch C25, "菜单事件深测: 多菜单项/
// 状态联动/退订 -- WinForms 阶段 10 后"). Builds on winforms1 Phase 10 (the
// MainMenu/MenuItem surface: CreateMenu + CreatePopupMenu + AppendMenuW +
// SetMenu, WM_COMMAND dispatch by item id -> WfMenuItem.Click, status linkage)
// and goes DEEPER on the menu event model:
//
//   * MANY ITEMS: a 4-top-level menu BAR (File/Edit/View/Help) with 9 sub-items
//     -- structure (GetMenuItemCount per popup) + item-text readback
//     (GetMenuStringW MF_BYPOSITION) prove the tree shape.
//   * FULL DISPATCH: post WM_COMMAND for every item id -> the matching
//     WfMenuItem.Click fires in post order (the WinForms MenuItem.Click path).
//   * MULTI-HANDLER + UNSUBSCRIBE: two handlers on one item, remove one,
//     remove ALL, re-add -- subscription counts and the dispatch sequence
//     prove the multicast add/remove round-trip.
//   * STATE LINKAGE (checked): a Click handler flips the item's managed
//     Checked property AND mirrors it to the native menu (CheckMenuItem);
//     GetMenuState readback proves native == managed at every step.
//   * STATE LINKAGE (enabled): Open's Click disables Cut (managed Enabled =
//     false + EnableMenuItem MF_GRAYED); the dispatch GATES on Enabled (a
//     disabled item receives no Click -- the WinForms rule); About re-enables.
//   * CROSS-MENU LINKAGE: File>New disables Edit>Paste; Edit>Copy disables
//     File>Exit; the sequence proves both directions and the gated items stay
//     silent.
//   * NATIVE STATE PROBES: CheckMenuItem/EnableMenuItem return the PREVIOUS
//     state and GetMenuState reflects MF_CHECKED/MF_GRAYED transitions.
//
// Headless acceptance: PostMessage WM_COMMAND by item id + PeekMessageW drain
// (phase-4 pattern); every sequence/count is byte-exact across net48 + both
// compilers. The window is HIDDEN; the menu bar is attached (SetMenu) so the
// native state calls operate on a real menu tree.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Menuevt
    {
        public const int WM_COMMAND = 0x0111;
        public const int PM_REMOVE = 0x0001;
        public const int WS_OVERLAPPED = 0x00000000;
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);

        // Menu flags (user32).
        public const int MF_POPUP = 0x0010;
        public const int MF_STRING = 0x0000;
        public const int MF_BYPOSITION = 0x0400;
        public const int MF_BYCOMMAND = 0x0000;
        public const int MF_CHECKED = 0x0008;
        public const int MF_UNCHECKED = 0x0000;
        public const int MF_ENABLED = 0x0000;
        public const int MF_GRAYED = 0x0001;

        // Item ids.
        public const int ID_NEW = 0x101;
        public const int ID_OPEN = 0x102;
        public const int ID_EXIT = 0x103;
        public const int ID_CUT = 0x201;
        public const int ID_COPY = 0x202;
        public const int ID_PASTE = 0x203;
        public const int ID_TOGGLE = 0x301;
        public const int ID_BOLD = 0x302;
        public const int ID_ABOUT = 0x401;

        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEX
        {
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
        public struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public int pt_x;
            public int pt_y;
        }

        public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        public delegate void H();

        [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance,
            IntPtr param);

        [DllImport("user32", EntryPoint = "PostMessageW")]
        public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "PeekMessageW")]
        public static extern int PeekMessageW(out MSG msg, IntPtr hwnd, int min, int max, int remove);

        [DllImport("user32", EntryPoint = "TranslateMessage")]
        public static extern int TranslateMessage(ref MSG msg);

        [DllImport("user32", EntryPoint = "DispatchMessageW")]
        public static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("user32", EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "DestroyWindow")]
        public static extern int DestroyWindow(IntPtr hwnd);

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

        [DllImport("user32", EntryPoint = "CheckMenuItem")]
        public static extern int CheckMenuItem(IntPtr hMenu, int uIDCheckItem, int uCheck);

        [DllImport("user32", EntryPoint = "EnableMenuItem")]
        public static extern int EnableMenuItem(IntPtr hMenu, int uIDEnableItem, int uEnable);

        [DllImport("user32", EntryPoint = "GetMenuState")]
        public static extern int GetMenuState(IntPtr hMenu, int uId, int uFlags);

        // ---- observable sequence + state ----
        public static string Seq = "";
        public static void ResetSeq() { Seq = ""; }

        public static IntPtr HForm;
        public static IntPtr HBar;
        public static IntPtr HFile;
        public static IntPtr HEdit;
        public static IntPtr HView;
        public static IntPtr HHelp;

        public static WfMenuItem ItemNew;
        public static WfMenuItem ItemOpen;
        public static WfMenuItem ItemExit;
        public static WfMenuItem ItemCut;
        public static WfMenuItem ItemCopy;
        public static WfMenuItem ItemPaste;
        public static WfMenuItem ItemToggle;
        public static WfMenuItem ItemBold;
        public static WfMenuItem ItemAbout;

        // Linkage gates: section C/D run with BOTH off so the full dispatch
        // fires every handler; the linkage sections enable one at a time.
        public static int LinkMode = 0;   // Open disables Cut / About re-enables
        public static int CrossMode = 0;  // New disables Paste / Copy disables Exit

        // Handlers (each appends its tag).
        public static void OnNew() { Seq = Seq + "new;"; if (CrossMode != 0) { SetEnabled(HEdit, ID_PASTE, false); ItemPaste.Enabled = 0; } }
        public static void OnNew2() { Seq = Seq + "new2;"; }
        public static void OnOpen() { Seq = Seq + "open;"; if (LinkMode != 0) { SetEnabled(HEdit, ID_CUT, false); ItemCut.Enabled = 0; } }
        public static void OnExit() { Seq = Seq + "exit;"; }
        public static void OnCut() { Seq = Seq + "cut;"; }
        public static void OnCopy() { Seq = Seq + "copy;"; if (CrossMode != 0) { SetEnabled(HFile, ID_EXIT, false); ItemExit.Enabled = 0; } }
        public static void OnPaste() { Seq = Seq + "paste;"; }
        public static void OnToggle()
        {
            int tchk = ItemToggle.Checked == 0 ? 1 : 0;
            ItemToggle.Checked = tchk;
            SetChecked(HView, ID_TOGGLE, tchk != 0);
            Seq = Seq + "toggle" + tchk + ";";
        }
        public static void OnBold() { ItemBold.Checked = 1; SetChecked(HView, ID_BOLD, true); Seq = Seq + "bold;"; }
        public static void OnAbout() { Seq = Seq + "about;"; if (LinkMode != 0) { SetEnabled(HEdit, ID_CUT, true); ItemCut.Enabled = 1; } }

        public static void SetChecked(IntPtr hMenu, int id, bool chk)
        {
            CheckMenuItem(hMenu, id, chk ? MF_CHECKED : MF_UNCHECKED);
        }

        public static void SetEnabled(IntPtr hMenu, int id, bool en)
        {
            EnableMenuItem(hMenu, id, en ? MF_ENABLED : MF_GRAYED);
        }

        public static void Drain()
        {
            MSG m;
            int d = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                d++;
                if (d >= 200) break;
            }
        }

        public static WndProc RootWndProc;

        public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_COMMAND)
            {
                int cid = (int)(wParam.ToInt64() & 0xFFFFL);
                if (ItemNew != null && cid == ID_NEW) ItemNew.RaiseClick();
                else if (ItemOpen != null && cid == ID_OPEN) ItemOpen.RaiseClick();
                else if (ItemExit != null && cid == ID_EXIT) ItemExit.RaiseClick();
                else if (ItemCut != null && cid == ID_CUT) ItemCut.RaiseClick();
                else if (ItemCopy != null && cid == ID_COPY) ItemCopy.RaiseClick();
                else if (ItemPaste != null && cid == ID_PASTE) ItemPaste.RaiseClick();
                else if (ItemToggle != null && cid == ID_TOGGLE) ItemToggle.RaiseClick();
                else if (ItemBold != null && cid == ID_BOLD) ItemBold.RaiseClick();
                else if (ItemAbout != null && cid == ID_ABOUT) ItemAbout.RaiseClick();
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public static void Post(int id) { PostMessageW(HForm, WM_COMMAND, new IntPtr(id), IntPtr.Zero); }

        public static void Main()
        {
            IntPtr hModule = GetModuleHandleW(null);
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = 0;
            RootWndProc = WndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
            wc.hInstance = hModule;
            IntPtr cls = Marshal.StringToHGlobalUni("MevClass");
            wc.lpszClassName = cls;
            ushort atom = RegisterClassExW(ref wc);
            Console.WriteLine("class_registered=" + (atom != 0));
            Marshal.FreeHGlobal(cls);

            HForm = CreateWindowExW(0, "MevClass", "Mev",
                WS_OVERLAPPED, CW_USEDEFAULT, CW_USEDEFAULT, 220, 140,
                IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("form_created=" + (HForm != IntPtr.Zero));

            // ---- build the 4-top-level menu bar with 9 items ----
            HBar = CreateMenu();
            HFile = CreatePopupMenu();
            HEdit = CreatePopupMenu();
            HView = CreatePopupMenu();
            HHelp = CreatePopupMenu();
            AppendMenuW(HBar, MF_POPUP, HFile, "File");
            AppendMenuW(HFile, MF_STRING, new IntPtr(ID_NEW), "New");
            AppendMenuW(HFile, MF_STRING, new IntPtr(ID_OPEN), "Open");
            AppendMenuW(HFile, MF_STRING, new IntPtr(ID_EXIT), "Exit");
            AppendMenuW(HBar, MF_POPUP, HEdit, "Edit");
            AppendMenuW(HEdit, MF_STRING, new IntPtr(ID_CUT), "Cut");
            AppendMenuW(HEdit, MF_STRING, new IntPtr(ID_COPY), "Copy");
            AppendMenuW(HEdit, MF_STRING, new IntPtr(ID_PASTE), "Paste");
            AppendMenuW(HBar, MF_POPUP, HView, "View");
            AppendMenuW(HView, MF_STRING, new IntPtr(ID_TOGGLE), "Toggle");
            AppendMenuW(HView, MF_STRING, new IntPtr(ID_BOLD), "Bold");
            AppendMenuW(HBar, MF_POPUP, HHelp, "Help");
            AppendMenuW(HHelp, MF_STRING, new IntPtr(ID_ABOUT), "About");
            SetMenu(HForm, HBar);
            Console.WriteLine("menu_attached=" + (GetMenu(HForm) == HBar));

            // ---- A. structure ----
            Console.WriteLine("a_top_count=" + GetMenuItemCount(HBar));    // 4
            Console.WriteLine("a_file_count=" + GetMenuItemCount(HFile));  // 3
            Console.WriteLine("a_edit_count=" + GetMenuItemCount(HEdit));  // 3
            Console.WriteLine("a_view_count=" + GetMenuItemCount(HView));  // 2
            Console.WriteLine("a_help_count=" + GetMenuItemCount(HHelp));  // 1
            IntPtr mb = Marshal.AllocHGlobal(256);
            GetMenuStringW(HBar, 0, mb, 128, MF_BYPOSITION);
            string top0 = Marshal.PtrToStringUni(mb);
            GetMenuStringW(HFile, 0, mb, 128, MF_BYPOSITION);
            string file0 = Marshal.PtrToStringUni(mb);
            GetMenuStringW(HEdit, 1, mb, 128, MF_BYPOSITION);
            string edit1 = Marshal.PtrToStringUni(mb);
            GetMenuStringW(HHelp, 0, mb, 128, MF_BYPOSITION);
            string help0 = Marshal.PtrToStringUni(mb);
            Marshal.FreeHGlobal(mb);
            Console.WriteLine("a_top0_text=" + top0);      // File
            Console.WriteLine("a_file0_text=" + file0);    // New
            Console.WriteLine("a_edit1_text=" + edit1);    // Copy
            Console.WriteLine("a_help0_text=" + help0);    // About

            // ---- managed items + subscriptions ----
            ItemNew = new WfMenuItem(); ItemNew.Id = ID_NEW; ItemNew.Text = "New";
            ItemOpen = new WfMenuItem(); ItemOpen.Id = ID_OPEN; ItemOpen.Text = "Open";
            ItemExit = new WfMenuItem(); ItemExit.Id = ID_EXIT; ItemExit.Text = "Exit";
            ItemCut = new WfMenuItem(); ItemCut.Id = ID_CUT; ItemCut.Text = "Cut";
            ItemCopy = new WfMenuItem(); ItemCopy.Id = ID_COPY; ItemCopy.Text = "Copy";
            ItemPaste = new WfMenuItem(); ItemPaste.Id = ID_PASTE; ItemPaste.Text = "Paste";
            ItemToggle = new WfMenuItem(); ItemToggle.Id = ID_TOGGLE; ItemToggle.Text = "Toggle";
            ItemBold = new WfMenuItem(); ItemBold.Id = ID_BOLD; ItemBold.Text = "Bold";
            ItemAbout = new WfMenuItem(); ItemAbout.Id = ID_ABOUT; ItemAbout.Text = "About";
            ItemNew.Click += OnNew;
            ItemOpen.Click += OnOpen;
            ItemExit.Click += OnExit;
            ItemCut.Click += OnCut;
            ItemCopy.Click += OnCopy;
            ItemPaste.Click += OnPaste;
            ItemToggle.Click += OnToggle;
            ItemBold.Click += OnBold;
            ItemAbout.Click += OnAbout;
            Console.WriteLine("b_subs=" + (ItemNew.ClickCount + ItemOpen.ClickCount
                + ItemExit.ClickCount + ItemCut.ClickCount + ItemCopy.ClickCount
                + ItemPaste.ClickCount + ItemToggle.ClickCount + ItemBold.ClickCount
                + ItemAbout.ClickCount));                                // 9

            // ---- C. full dispatch in post order (no linkage) ----
            ResetSeq();
            Post(ID_NEW); Post(ID_OPEN); Post(ID_EXIT); Post(ID_CUT);
            Post(ID_COPY); Post(ID_PASTE); Post(ID_TOGGLE); Post(ID_BOLD); Post(ID_ABOUT);
            Drain();
            Console.WriteLine("c_seq=" + Seq);   // new;open;exit;cut;copy;paste;toggle1;bold;about;

            // ---- D. multi-handler + unsubscribe round-trip ----
            ItemNew.Click += OnNew2;                                     // [OnNew, OnNew2]
            ItemNew.Click -= OnNew;                                      // [OnNew2]
            Console.WriteLine("d_subs=" + ItemNew.ClickCount);           // 1
            ResetSeq();
            Post(ID_NEW); Drain();
            Console.WriteLine("d_seq=" + Seq);                           // new2;
            ItemNew.Click -= OnNew2;                                     // []
            Console.WriteLine("d_subs2=" + ItemNew.ClickCount);          // 0
            ResetSeq();
            Post(ID_NEW); Drain();
            Console.WriteLine("d_seq2=" + Seq);                          // (empty)
            ItemNew.Click += OnNew;                                      // [OnNew]
            ResetSeq();
            Post(ID_NEW); Drain();
            Console.WriteLine("d_seq3=" + Seq);                          // new;

            // ---- E. checked-state linkage (managed <-> native) ----
            ResetSeq();
            Post(ID_TOGGLE); Drain();                              // E toggle #1 flips C's checked back -> unchecked
            Console.WriteLine("e_native=" + (GetMenuState(HView, ID_TOGGLE, MF_BYCOMMAND) & MF_CHECKED)); // 8
            int ec1 = ItemToggle.Checked;
            Console.WriteLine("e_managed=" + ec1); // 1
            ResetSeq();
            Post(ID_TOGGLE); Drain();                              // E toggle #2 -> checked again
            Console.WriteLine("e_native2=" + (GetMenuState(HView, ID_TOGGLE, MF_BYCOMMAND) & MF_CHECKED)); // 0
            int ec2 = ItemToggle.Checked;
            Console.WriteLine("e_managed2=" + ec2); // 0

            // ---- F. enabled-state linkage (Open disables Cut, About re-enables) ----
            LinkMode = 1;
            ResetSeq();
            Post(ID_OPEN); Drain();                                      // open; + disable Cut
            Console.WriteLine("f_seq=" + Seq);                           // open;
            int fc1 = ItemCut.Enabled;
            Console.WriteLine("f_cut_managed=" + fc1);   // 0
            Console.WriteLine("f_cut_native=" + (GetMenuState(HEdit, ID_CUT, MF_BYCOMMAND) & MF_GRAYED)); // 1
            ResetSeq();
            Post(ID_CUT); Drain();                                       // gated -> silent
            Console.WriteLine("f_after_cut=" + Seq);                     // (empty)
            ResetSeq();
            Post(ID_ABOUT); Drain();                                     // about; + re-enable Cut
            int fc2 = ItemCut.Enabled;
            Console.WriteLine("f_cut_managed2=" + fc2);  // 1
            Console.WriteLine("f_cut_native2=" + (GetMenuState(HEdit, ID_CUT, MF_BYCOMMAND) & MF_GRAYED)); // 0
            ResetSeq();
            Post(ID_CUT); Drain();                                       // cut; (fires again)
            Console.WriteLine("f_after_cut2=" + Seq);                    // cut;
            LinkMode = 0;

            // ---- G. native state probes (return previous state) ----
            Console.WriteLine("g_chk_prev=" + CheckMenuItem(HEdit, ID_COPY, MF_CHECKED));  // 0
            Console.WriteLine("g_chk_state=" + (GetMenuState(HEdit, ID_COPY, MF_BYCOMMAND) & MF_CHECKED)); // 8
            Console.WriteLine("g_chk_prev2=" + CheckMenuItem(HEdit, ID_COPY, MF_UNCHECKED)); // 8
            Console.WriteLine("g_chk_state2=" + (GetMenuState(HEdit, ID_COPY, MF_BYCOMMAND) & MF_CHECKED)); // 0
            Console.WriteLine("g_gry_prev=" + EnableMenuItem(HView, ID_TOGGLE, MF_GRAYED)); // 0
            Console.WriteLine("g_gry_state=" + (GetMenuState(HView, ID_TOGGLE, MF_BYCOMMAND) & MF_GRAYED)); // 1
            Console.WriteLine("g_gry_prev2=" + EnableMenuItem(HView, ID_TOGGLE, MF_ENABLED)); // 1
            Console.WriteLine("g_gry_state2=" + (GetMenuState(HView, ID_TOGGLE, MF_BYCOMMAND) & MF_GRAYED)); // 0

            // ---- H. cross-menu linkage (New disables Paste, Copy disables Exit) ----
            CrossMode = 1;
            ResetSeq();
            Post(ID_NEW); Post(ID_COPY); Post(ID_PASTE); Post(ID_EXIT); Drain();
            Console.WriteLine("h_seq=" + Seq);                           // new;copy;
            int hp1 = ItemPaste.Enabled;
            Console.WriteLine("h_paste_managed=" + hp1); // 0
            int he1 = ItemExit.Enabled;
            Console.WriteLine("h_exit_managed=" + he1);   // 0
            Console.WriteLine("h_paste_native=" + (GetMenuState(HEdit, ID_PASTE, MF_BYCOMMAND) & MF_GRAYED)); // 1
            Console.WriteLine("h_exit_native=" + (GetMenuState(HFile, ID_EXIT, MF_BYCOMMAND) & MF_GRAYED));  // 1
            CrossMode = 0;

            DestroyWindow(HForm);
            Console.WriteLine("done");
        }
    }
}

// WinForms-style managed menu item: a text + a WM_COMMAND id + a Click
// multicast event (Delegate.Combine/Remove) + managed Checked/Enabled state.
// RaiseClick GATES on Enabled -- a disabled item receives no click (the
// WinForms rule) -- and a Click handler can flip Checked/Enabled and mirror
// the change to the native menu (CheckMenuItem / EnableMenuItem).
public class WfMenuItem
{
    public int Id;
    public string Text;
    public int Checked;
    public int Enabled = 1;
    private Cases.Menuevt.H _click;

    public event Cases.Menuevt.H Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    public void RaiseClick() { if (Enabled != 0 && _click != null) _click(); }
}




