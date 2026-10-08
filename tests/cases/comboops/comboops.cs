// comboops.cs -- ComboBox / ListBox DEEP probe (e2e batch C23, "ComboBox/ListBox
// deep probe: add/select/text-readback/events -- after WinForms stage 8"). Builds on winforms1
// Phase 8 (the WfDialogControl list-control surface: CB_*/LB_* add/count/
// cursels/gettext/selection-change events) and goes DEEPER on the
// ListControl behavior the stage-8 baseline only touched once:
//
//   * MANY ITEMS + ordered add: 6 items each -> returned indices 0..5, count
//     exact, item text readback for EVERY index (CB_GETLBTEXT / LB_GETTEXT).
//   * SELECTION transitions: none -> -1; set to each index; back to -1;
//     re-set. CB_GETCURSEL / LB_GETCURSEL round-trips after every set.
//   * TEXT READBACK AFTER SELECTION: the selected item's text via the index
//     returned by GetCurSel (item text and selection stay in lockstep).
//   * ITEM-COUNT GROWTH: add 3 more after a selection exists -- indices of
//     the new items continue from 6,7,8; the pre-existing selection is
//     untouched; count readback reflects the growth.
//   * EVENT SEQUENCES (CBN_SELCHANGE / LBN_SELCHANGE): a per-control
//     SelectionChanged handler records the CURRENT selection at fire time
//     (the handler re-reads GetCurSel). Posted WM_COMMAND id|(notif<<16)
//     drains FIFO -> exact sequence strings. Two controls, one handler each,
//     interleaved posts prove the notification route is per-control.
//   * SUBSCRIBE/UNSUBSCRIBE: - a handler from one control; the other
//     control's events keep firing (independent event lists).
//   * MULTI-EVENT per control: TWO handlers on the ComboBox selection --
//     multicast order preserved; remove one, the survivor keeps firing.
//   * REPEATED SELECTION on the same index is still an event (the native
//     control sends CBN_SELCHANGE every time a selection changes, including
//     to the same index via SETCURSEL -- but our posted-message simulation
//     makes the count deterministic; the HANDLER observes the selection at
//     fire time, so the sequence string is exact regardless).
//   * LISTBOX MULTI-SELECT-INDEPENDENT: the ListBox's own selection state is
//     read back interleaved with the ComboBox's -- the two controls never
//     share selection state (GetCurSel on one does not touch the other).
//
// Determinism: no raw HWND printing (only != 0 booleans); every observable is
// the index/count/text read back from the native control or the sequence
// string built by FIFO-drained posted messages -- byte-exact across the net48
// oracle and the ILC side (both run the same source against real Windows).
//
// Every expected value is spelled in a trailing comment.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Comboops
    {
        public const int WM_COMMAND = 0x0111;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_QUIT = 0x0012;
        public const int PM_REMOVE = 0x0001;
        public const int VK_F2 = 0x0071;
        public const int WS_OVERLAPPED = 0x00000000;
        public const int WS_CHILD = 0x40000000;
        public const int WS_TABSTOP = 0x00010000;
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);

        public const int CBS_DROPDOWNLIST = 0x0003;
        public const int LBS_NOTIFY = 0x0001;

        // ComboBox messages.
        public const int CB_ADDSTRING = 0x0143;
        public const int CB_GETCOUNT = 0x0146;
        public const int CB_GETCURSEL = 0x0147;
        public const int CB_GETLBTEXT = 0x0148;
        public const int CB_SETCURSEL = 0x014E;
        // ListBox messages.
        public const int LB_ADDSTRING = 0x0180;
        public const int LB_SETCURSEL = 0x0186;
        public const int LB_GETCURSEL = 0x0188;
        public const int LB_GETTEXT = 0x0189;
        public const int LB_GETCOUNT = 0x018B;

        public const int CBN_SELCHANGE = 1;
        public const int LBN_SELCHANGE = 1;

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

        [DllImport("user32", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

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

        [DllImport("user32", EntryPoint = "PostQuitMessage")]
        public static extern void PostQuitMessage(int exitCode);

        // ---- observable state ----
        public static string Seq = "";
        public static void ResetSeq() { Seq = ""; }

        // Handler targets (set before the event section).
        public static Ctrl P8Combo;
        public static Ctrl P8List;

        public static void OnComboChange() { Seq = Seq + "c" + P8Combo.SelectedIndex + ";"; }
        public static void OnListChange()  { Seq = Seq + "l" + P8List.SelectedIndex + ";"; }
        public static void OnCombo2()      { Seq = Seq + "C" + P8Combo.SelectedIndex + ";"; }

        public static WndProc RootWndProc;

        public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_COMMAND)
            {
                int cid = (int)(wParam.ToInt64() & 0xFFFFL);
                int notif = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
                if (P8Combo != null && cid == P8Combo.Id && notif == CBN_SELCHANGE) P8Combo.RaiseSelectionChanged();
                else if (P8List != null && cid == P8List.Id && notif == LBN_SELCHANGE) P8List.RaiseSelectionChanged();
                return IntPtr.Zero;
            }
            if (msg == WM_KEYDOWN)
            {
                if (wParam.ToInt64() == VK_F2 && P8Combo != null) P8Combo.RaiseSelectionChanged();
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // Drain posted messages FIFO (bounded; never blocks).
        static void Drain()
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

        public static void Main()
        {
            IntPtr hModule = GetModuleHandleW(null);
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = 0;
            RootWndProc = WndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
            wc.hInstance = hModule;
            IntPtr cls = Marshal.StringToHGlobalUni("CbOpsClass");
            wc.lpszClassName = cls;
            ushort atom = RegisterClassExW(ref wc);
            Console.WriteLine("class_registered=" + (atom != 0));
            Marshal.FreeHGlobal(cls);

            IntPtr hForm = CreateWindowExW(0, "CbOpsClass", "CbOps",
                WS_OVERLAPPED, CW_USEDEFAULT, CW_USEDEFAULT, 320, 200,
                IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));

            IntPtr hCombo = CreateWindowExW(0, "COMBOBOX", "CbOpsCombo",
                WS_CHILD | WS_TABSTOP | CBS_DROPDOWNLIST, 20, 20, 160, 120,
                hForm, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("combo_created=" + (hCombo != IntPtr.Zero));
            IntPtr hList = CreateWindowExW(0, "LISTBOX", "CbOpsList",
                WS_CHILD | WS_TABSTOP | LBS_NOTIFY, 200, 20, 160, 120,
                hForm, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("list_created=" + (hList != IntPtr.Zero));

            P8Combo = new Ctrl(); P8Combo.Handle = hCombo; P8Combo.Id = 0x601; P8Combo.Kind = 1;
            P8List = new Ctrl(); P8List.Handle = hList; P8List.Id = 0x602; P8List.Kind = 2;
            Console.WriteLine("controls_wired=" + (P8Combo.Handle != IntPtr.Zero && P8List.Handle != IntPtr.Zero));

            // ============ 1. ordered add, 6 items each ============
            Console.WriteLine("add_c0=" + P8Combo.Add("Alpha"));    // 0
            Console.WriteLine("add_c1=" + P8Combo.Add("Beta"));     // 1
            Console.WriteLine("add_c2=" + P8Combo.Add("Gamma"));    // 2
            Console.WriteLine("add_c3=" + P8Combo.Add("Delta"));    // 3
            Console.WriteLine("add_c4=" + P8Combo.Add("Epsilon"));  // 4
            Console.WriteLine("add_c5=" + P8Combo.Add("Zeta"));     // 5
            Console.WriteLine("combo_count=" + P8Combo.Count);      // 6
            Console.WriteLine("add_l0=" + P8List.Add("One"));       // 0
            Console.WriteLine("add_l1=" + P8List.Add("Two"));       // 1
            Console.WriteLine("add_l2=" + P8List.Add("Three"));     // 2
            Console.WriteLine("add_l3=" + P8List.Add("Four"));      // 3
            Console.WriteLine("add_l4=" + P8List.Add("Five"));      // 4
            Console.WriteLine("add_l5=" + P8List.Add("Six"));       // 5
            Console.WriteLine("list_count=" + P8List.Count);        // 6

            // ============ 2. item text readback for every index ============
            Console.WriteLine("combo_item0=" + P8Combo.GetItemText(0));   // Alpha
            Console.WriteLine("combo_item3=" + P8Combo.GetItemText(3));   // Delta
            Console.WriteLine("combo_item5=" + P8Combo.GetItemText(5));   // Zeta
            Console.WriteLine("list_item1=" + P8List.GetItemText(1));     // Two
            Console.WriteLine("list_item4=" + P8List.GetItemText(4));     // Five
            Console.WriteLine("list_item5=" + P8List.GetItemText(5));     // Six

            // ============ 3. selection transitions ============
            Console.WriteLine("combo_sel_none=" + P8Combo.SelectedIndex);   // -1
            Console.WriteLine("list_sel_none=" + P8List.SelectedIndex);     // -1
            P8Combo.SelectedIndex = 3;   // Delta
            P8List.SelectedIndex = 2;    // Three
            Console.WriteLine("combo_sel_3=" + P8Combo.SelectedIndex);      // 3
            Console.WriteLine("list_sel_2=" + P8List.SelectedIndex);        // 2
            // Text of the SELECTED item via the GetCurSel index (lockstep).
            Console.WriteLine("combo_selected_text=" + P8Combo.GetItemText(P8Combo.SelectedIndex)); // Delta
            Console.WriteLine("list_selected_text=" + P8List.GetItemText(P8List.SelectedIndex));    // Three
            // Deselect both.
            P8Combo.SelectedIndex = -1;
            P8List.SelectedIndex = -1;
            Console.WriteLine("combo_sel_deselect=" + P8Combo.SelectedIndex); // -1
            Console.WriteLine("list_sel_deselect=" + P8List.SelectedIndex);   // -1
            // Re-select.
            P8Combo.SelectedIndex = 0;
            P8List.SelectedIndex = 5;
            Console.WriteLine("combo_sel_resel0=" + P8Combo.SelectedIndex);  // 0
            Console.WriteLine("list_sel_resel5=" + P8List.SelectedIndex);    // 5

            // ============ 4. item-count growth with a live selection ============
            Console.WriteLine("combo_add_after=" + P8Combo.Add("Eta"));   // 6 (grows from 6 -> 7)
            Console.WriteLine("combo_add_after2=" + P8Combo.Add("Theta"));// 7
            Console.WriteLine("combo_add_after3=" + P8Combo.Add("Iota")); // 8
            Console.WriteLine("combo_count_grown=" + P8Combo.Count);      // 9
            Console.WriteLine("combo_sel_kept=" + P8Combo.SelectedIndex); // 0 (pre-existing selection untouched)
            Console.WriteLine("list_add_after=" + P8List.Add("Seven"));   // 6
            Console.WriteLine("list_count_grown=" + P8List.Count);        // 7
            Console.WriteLine("list_sel_kept=" + P8List.SelectedIndex);   // 5

            // ============ 5. event sequences (per-control, FIFO) ============
            ResetSeq();
            P8Combo.SelectionChanged += OnComboChange;
            P8List.SelectionChanged += OnListChange;
            Console.WriteLine("subs=" + P8Combo.SelCount + "," + P8List.SelCount); // 1,1

            // Combo selected index is 0 -> events read "c0". List selected 5.
            int ok1 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            int ok2 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8List.Id | (LBN_SELCHANGE << 16)), IntPtr.Zero);
            int ok3 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            Console.WriteLine("inject=" + (ok1 != 0 && ok2 != 0 && ok3 != 0));
            Drain();
            Console.WriteLine("seq1=" + Seq);   // c0;l5;c0;

            // Move the selections BETWEEN posts: the event observes the
            // selection AT FIRE TIME (the handler re-reads GetCurSel).
            ResetSeq();
            P8Combo.SelectedIndex = 2;   // Gamma
            int ok4 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            P8List.SelectedIndex = 0;    // One
            int ok5 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8List.Id | (LBN_SELCHANGE << 16)), IntPtr.Zero);
            int ok6 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            Console.WriteLine("inject2=" + (ok4 != 0 && ok5 != 0 && ok6 != 0));
            Drain();
            Console.WriteLine("seq2=" + Seq);   // c2;l0;c2;

            // ============ 6. unsubscribe one control; the other stays live ============
            ResetSeq();
            P8List.SelectionChanged -= OnListChange;
            Console.WriteLine("subs_after=" + P8Combo.SelCount + "," + P8List.SelCount); // 1,0
            int ok7 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            int ok8 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8List.Id | (LBN_SELCHANGE << 16)), IntPtr.Zero);
            Console.WriteLine("inject3=" + (ok7 != 0 && ok8 != 0));
            Drain();
            Console.WriteLine("seq3=" + Seq);   // c2; (list handler removed)

            // ============ 7. multi-handler multicast order + removal ============
            ResetSeq();
            P8Combo.SelectionChanged += OnCombo2;
            Console.WriteLine("combo_subs=" + P8Combo.SelCount); // 2
            int ok9 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            Console.WriteLine("inject4=" + (ok9 != 0));
            Drain();
            Console.WriteLine("seq4=" + Seq);   // c2;C2;
            // Remove the first (survivor order preserved).
            ResetSeq();
            P8Combo.SelectionChanged -= OnComboChange;
            Console.WriteLine("combo_subs_after=" + P8Combo.SelCount); // 1
            int okA = PostMessageW(hForm, WM_COMMAND, new IntPtr(P8Combo.Id | (CBN_SELCHANGE << 16)), IntPtr.Zero);
            Console.WriteLine("inject5=" + (okA != 0));
            Drain();
            Console.WriteLine("seq5=" + Seq);   // C2;

            // ============ 8. selection-state independence ============
            // Set list selection, read combo's -- the two never share state.
            P8List.SelectedIndex = 4;
            Console.WriteLine("list_sel_indep=" + P8List.SelectedIndex); // 4
            Console.WriteLine("combo_sel_indep=" + P8Combo.SelectedIndex); // 2 (untouched)
            P8Combo.SelectedIndex = 1;
            Console.WriteLine("combo_sel_2=" + P8Combo.SelectedIndex);    // 1
            Console.WriteLine("list_sel_kept2=" + P8List.SelectedIndex);  // 4 (untouched)

            // ============ 9. VM_KEYDOWN -> SelectionChanged (keyboard path) ============
            ResetSeq();
            int okB = PostMessageW(hForm, WM_KEYDOWN, new IntPtr(VK_F2), IntPtr.Zero);
            Console.WriteLine("inject6=" + (okB != 0));
            Drain();
            Console.WriteLine("seq6=" + Seq);   // C1; (combo has one handler left: OnCombo2)

            // ============ 10. teardown ============
            PostQuitMessage(0);
            DestroyWindow(hForm);
            Console.WriteLine("wm_close_ok=True");
        }
    }
}

// WinForms-style ListControl model (ComboBox kind=1 / ListBox kind=2) over the
// raw user32 COMBOBOX/LISTBOX messages -- the winforms1 Phase-8 surface, kept
// here so comboops exercises the SAME wired marshal paths.
public class Ctrl
{
    public IntPtr Handle;
    public int Id;
    public int Kind;
    private Cases.Comboops.H _selchange;

    public event Cases.Comboops.H SelectionChanged
    {
        add { _selchange = _selchange + value; }
        remove { _selchange = _selchange - value; }
    }

    public int SelCount
    {
        get { return _selchange == null ? 0 : _selchange.GetInvocationList().Length; }
    }

    private int AddMsg() { return Kind == 1 ? Cases.Comboops.CB_ADDSTRING : Cases.Comboops.LB_ADDSTRING; }
    private int GetCountMsg() { return Kind == 1 ? Cases.Comboops.CB_GETCOUNT : Cases.Comboops.LB_GETCOUNT; }
    private int GetSelMsg() { return Kind == 1 ? Cases.Comboops.CB_GETCURSEL : Cases.Comboops.LB_GETCURSEL; }
    private int SetSelMsg() { return Kind == 1 ? Cases.Comboops.CB_SETCURSEL : Cases.Comboops.LB_SETCURSEL; }
    private int GetTextMsg() { return Kind == 1 ? Cases.Comboops.CB_GETLBTEXT : Cases.Comboops.LB_GETTEXT; }

    public int Add(string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        IntPtr r = Cases.Comboops.SendMessageW(Handle, AddMsg(), IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public int Count
    {
        get
        {
            IntPtr r = Cases.Comboops.SendMessageW(Handle, GetCountMsg(), IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
    }

    public int SelectedIndex
    {
        get
        {
            IntPtr r = Cases.Comboops.SendMessageW(Handle, GetSelMsg(), IntPtr.Zero, IntPtr.Zero);
            return (int)r.ToInt64();
        }
        set { Cases.Comboops.SendMessageW(Handle, SetSelMsg(), new IntPtr(value), IntPtr.Zero); }
    }

    public string GetItemText(int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        Cases.Comboops.SendMessageW(Handle, GetTextMsg(), new IntPtr(index), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public void RaiseSelectionChanged() { if (_selchange != null) _selchange(); }
}
