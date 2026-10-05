// scrollops.cs -- ScrollBar deep-operation case (winforms-plan stage-17
// domain, expanded). Exercises the user32 built-in "ScrollBar" class through
// the documented SetScrollRange / SetScrollPos / GetScrollPos channel (the
// legacy SBM_SETRANGE / SBM_SETPOS control messages are no-ops on this OS --
// probed in winforms1), plus the WM_VSCROLL / WM_HSCROLL parent
// notifications. Everything is headless: the scrollbars are WS_CHILD of a
// HIDDEN form; every scroll is driven by PostMessageW + a PeekMessageW drain
// (the winforms1 phase-4 acceptance pattern), so the sequences are byte-exact
// across net48 + both transpiled compilers.
//
// Subset exercised (the stage-17 scroll domain):
//   * RANGE: SetScrollRange (0..100, then a re-range 0..1000; the range is
//     verified by clamp readbacks -- SetScrollPos outside [min,max] rounds to
//     the nearest bound, read back via GetScrollPos).
//   * POSITION: SetScrollPos / GetScrollPos round-trips (0 / mid / max), the
//     SetScrollPos RETURN value (the PREVIOUS position -- a deterministic
//     sequence value), and the position persistence across a re-range.
//   * CLAMP: SetScrollPos(120) -> 100 (high clamp), SetScrollPos(-5) -> 0
//     (low clamp) on a 0..100 bar; SetScrollPos(1500) -> 1000 on the
//     re-ranged 0..1000 bar.
//   * EVENTS: WM_VSCROLL with the thumb codes SB_THUMBTRACK (5) and
//     SB_THUMBPOSITION (4) -- the new thumb position rides in HIWORD(wParam);
//     the form's dispatch writes the thumb (SetScrollPos), re-reads the real
//     position (GetScrollPos) and mirrors it into the stage-9 status bar text
//     (SB_SETTEXT + SB_GETTEXT round-trip): scroll drives status. Two
//     independent scrollbars (a vertical + a horizontal) prove the per-axis
//     event isolation (WM_VSCROLL only moves the vertical bar; WM_HSCROLL
//     only the horizontal).
//
// net48 oracle: every line is a range-clamp readback / position round-trip /
// scroll sequence / status linkage / per-axis isolation, all byte-exact.
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class Scrollops
{
    // --- window / message constants ---
    public const int WM_CREATE = 0x0001;
    public const int WM_VSCROLL = 0x0115;
    public const int WM_HSCROLL = 0x0114;
    public const int WM_CLOSE = 0x0010;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CHILD = 0x40000000;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const int PM_REMOVE = 0x0001;

    // --- ScrollBar constants (stage-17 domain) ---
    public const string SCROLLBAR_CLASS = "SCROLLBAR";
    public const int SBS_VERT = 0x0001;
    public const int SBS_HORZ = 0x0000;
    public const int SB_CTL = 0x0002;                 // a scroll-bar CONTROL
    public const int SB_THUMBPOSITION = 4;            // thumb codes carry the
    public const int SB_THUMBTRACK = 5;               //   new pos in HIWORD
    public const int SB_LINEUP = 0;
    public const int SB_LINEDOWN = 1;
    public const int SB_PAGEUP = 2;
    public const int SB_PAGEDOWN = 3;
    public const int SB_TOP = 6;
    public const int SB_BOTTOM = 7;
    public const int WF_VSCROLL_STEP = 0x800C;        // posted position channel
    public const int WF_HSCROLL_STEP = 0x800D;        // posted position channel
    public const int ID_VSCROLL = 501;
    public const int ID_HSCROLL = 502;

    // --- stage-9 status-bar constants (the scroll drives status linkage) ---
    public const int SB_SETTEXT = 0x040B;
    public const int SB_GETTEXT = 0x040D;
    public const int ICC_BAR_CLASSES = 0x00000004;

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

    [DllImport("user32", EntryPoint = "SetScrollRange")]
    public static extern int SetScrollRange(IntPtr hwnd, int nBar, int nMinPos, int nMaxPos, bool fRedraw);

    [DllImport("user32", EntryPoint = "SetScrollPos")]
    public static extern int SetScrollPos(IntPtr hwnd, int nBar, int nPos, bool fRedraw);

    [DllImport("user32", EntryPoint = "GetScrollPos")]
    public static extern int GetScrollPos(IntPtr hwnd, int nBar);

    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    // --- observable state ---
    public static IntPtr VScrollHwnd;
    public static IntPtr HScrollHwnd;
    public static IntPtr StatusHwnd;
    public static bool VScrollCreated;
    public static bool HScrollCreated;
    public static int VStepCount;
    public static int HStepCount;
    public static int VvscrollCount;
    public static int HvscrollCount;
    public static string VSeq = "";
    public static string HSeq = "";
    public static bool VDestroyed;
    public static bool HDestroyed;
    public static bool StatusDestroyed;

    public static void MirrorStatus(int pos)
    {
        IntPtr st = Marshal.StringToHGlobalUni("Scroll: " + pos);
        SendMessageW(StatusHwnd, SB_SETTEXT, IntPtr.Zero, st);
        Marshal.FreeHGlobal(st);
        IntPtr sb = Marshal.AllocHGlobal(256);
        SendMessageW(StatusHwnd, SB_GETTEXT, IntPtr.Zero, sb);
        string s = Marshal.PtrToStringUni(sb);
        Marshal.FreeHGlobal(sb);
        VLastStatus = s;
    }

    public static string VLastStatus = "";

    public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CREATE) return IntPtr.Zero;
        if (msg == WF_VSCROLL_STEP)
        {
            int target = (int)wParam.ToInt64();
            int prev = SetScrollPos(VScrollHwnd, SB_CTL, target, false);
            int got = GetScrollPos(VScrollHwnd, SB_CTL);
            MirrorStatus(got);
            VSeq = VSeq + "step" + target + "/prev" + prev + "/pos" + got + ";";
            VStepCount++;
            return IntPtr.Zero;
        }
        if (msg == WM_VSCROLL)
        {
            int code = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            int before = GetScrollPos(VScrollHwnd, SB_CTL);
            if (code == SB_THUMBTRACK || code == SB_THUMBPOSITION)
            {
                SetScrollPos(VScrollHwnd, SB_CTL, thumb, false);
            }
            int after = GetScrollPos(VScrollHwnd, SB_CTL);
            MirrorStatus(after);
            string cname = code == SB_THUMBTRACK ? "track" : (code == SB_THUMBPOSITION ? "thumb" : "code" + code);
            VSeq = VSeq + cname + "/before" + before + "/pos" + after + ";";
            VvscrollCount++;
            return IntPtr.Zero;
        }
        if (msg == WF_HSCROLL_STEP)
        {
            int target = (int)wParam.ToInt64();
            int prev = SetScrollPos(HScrollHwnd, SB_CTL, target, false);
            int got = GetScrollPos(HScrollHwnd, SB_CTL);
            HSeq = HSeq + "step" + target + "/prev" + prev + "/pos" + got + ";";
            HStepCount++;
            return IntPtr.Zero;
        }
        if (msg == WM_HSCROLL)
        {
            int code = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            int before = GetScrollPos(HScrollHwnd, SB_CTL);
            if (code == SB_THUMBTRACK || code == SB_THUMBPOSITION)
            {
                SetScrollPos(HScrollHwnd, SB_CTL, thumb, false);
            }
            int after = GetScrollPos(HScrollHwnd, SB_CTL);
            string cname = code == SB_THUMBTRACK ? "track" : (code == SB_THUMBPOSITION ? "thumb" : "code" + code);
            HSeq = HSeq + cname + "/before" + before + "/pos" + after + ";";
            HvscrollCount++;
            return IntPtr.Zero;
        }
        if (msg == WM_CLOSE) { return IntPtr.Zero; }
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
        IntPtr clsName = Marshal.StringToHGlobalUni("ScrollOpsFormClass");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("class_registered=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hForm = CreateWindowExW(0, "ScrollOpsFormClass", "ScrollOps",
            WS_OVERLAPPED, 0, 0, 400, 300,
            IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
        Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));

        // --- stage-9 status bar (the scroll drives status linkage) ---
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_BAR_CLASSES;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("comctl_init=" + (iccRet != 0));

        IntPtr hStatus = CreateWindowExW(0, "msctls_statusbar32", "",
            WS_CHILD, 0, 0, 400, 20,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        StatusHwnd = hStatus;
        Console.WriteLine("statusbar_created=" + (hStatus != IntPtr.Zero));

        // --- vertical scrollbar (SBS_VERT) ---
        IntPtr hScrollV = CreateWindowExW(0, SCROLLBAR_CLASS, "",
            WS_CHILD | SBS_VERT, 10, 10, 20, 200, hForm, new IntPtr(ID_VSCROLL), hModule, IntPtr.Zero);
        VScrollHwnd = hScrollV;
        VScrollCreated = hScrollV != IntPtr.Zero;
        Console.WriteLine("v_scrollbar_created=" + VScrollCreated);

        // Range 0..100; initial position readback 0.
        int rangeV1 = SetScrollRange(hScrollV, SB_CTL, 0, 100, false);
        Console.WriteLine("v_range_set=" + (rangeV1 != 0));
        Console.WriteLine("v_pos0=" + GetScrollPos(hScrollV, SB_CTL));

        // Position round-trips on 0..100.
        Console.WriteLine("v_prev40=" + SetScrollPos(hScrollV, SB_CTL, 40, false));
        Console.WriteLine("v_pos40=" + GetScrollPos(hScrollV, SB_CTL));
        Console.WriteLine("v_prev100=" + SetScrollPos(hScrollV, SB_CTL, 100, false));
        Console.WriteLine("v_pos100=" + GetScrollPos(hScrollV, SB_CTL));
        Console.WriteLine("v_prev0=" + SetScrollPos(hScrollV, SB_CTL, 0, false));
        Console.WriteLine("v_pos0b=" + GetScrollPos(hScrollV, SB_CTL));

        // Clamp: beyond max -> 100, below min -> 0.
        SetScrollPos(hScrollV, SB_CTL, 120, false);
        Console.WriteLine("v_clamp_high=" + (GetScrollPos(hScrollV, SB_CTL) == 100));
        SetScrollPos(hScrollV, SB_CTL, -5, false);
        Console.WriteLine("v_clamp_low=" + (GetScrollPos(hScrollV, SB_CTL) == 0));

        // Re-range to 0..1000; position 0 persists, then clamp to the new max.
        int rangeV2 = SetScrollRange(hScrollV, SB_CTL, 0, 1000, false);
        Console.WriteLine("v_rereange=" + (rangeV2 != 0));
        Console.WriteLine("v_pos_after_rereange=" + GetScrollPos(hScrollV, SB_CTL));
        Console.WriteLine("v_prev500=" + SetScrollPos(hScrollV, SB_CTL, 500, false));
        Console.WriteLine("v_pos500=" + GetScrollPos(hScrollV, SB_CTL));
        SetScrollPos(hScrollV, SB_CTL, 1500, false);
        Console.WriteLine("v_clamp_rereanged=" + (GetScrollPos(hScrollV, SB_CTL) == 1000));

        // --- horizontal scrollbar (SBS_HORZ), independent axis ---
        IntPtr hScrollH = CreateWindowExW(0, SCROLLBAR_CLASS, "",
            WS_CHILD | SBS_HORZ, 40, 10, 200, 20, hForm, new IntPtr(ID_HSCROLL), hModule, IntPtr.Zero);
        HScrollHwnd = hScrollH;
        HScrollCreated = hScrollH != IntPtr.Zero;
        Console.WriteLine("h_scrollbar_created=" + HScrollCreated);

        int rangeH1 = SetScrollRange(hScrollH, SB_CTL, 0, 50, false);
        Console.WriteLine("h_range_set=" + (rangeH1 != 0));
        Console.WriteLine("h_pos0=" + GetScrollPos(hScrollH, SB_CTL));
        Console.WriteLine("h_prev25=" + SetScrollPos(hScrollH, SB_CTL, 25, false));
        Console.WriteLine("h_pos25=" + GetScrollPos(hScrollH, SB_CTL));
        SetScrollPos(hScrollH, SB_CTL, 80, false);
        Console.WriteLine("h_clamp_high=" + (GetScrollPos(hScrollH, SB_CTL) == 50));

        // --- vertical scroll events (posted + drained) ---
        VStepCount = 0; VvscrollCount = 0; VSeq = "";
        int v1 = PostMessageW(hForm, WF_VSCROLL_STEP, new IntPtr(25), IntPtr.Zero);
        Console.WriteLine("v_inject1=" + (v1 != 0));
        MSG mv1;
        int d1 = 0;
        while (PeekMessageW(out mv1, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref mv1);
            DispatchMessageW(ref mv1);
            d1++;
            if (d1 >= 200) break;
        }
        int v2 = PostMessageW(hForm, WM_VSCROLL, new IntPtr(SB_THUMBTRACK | (40 << 16)), IntPtr.Zero);
        Console.WriteLine("v_inject2=" + (v2 != 0));
        int d2 = 0;
        while (PeekMessageW(out mv1, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref mv1);
            DispatchMessageW(ref mv1);
            d2++;
            if (d2 >= 200) break;
        }
        int v3 = PostMessageW(hForm, WF_VSCROLL_STEP, new IntPtr(60), IntPtr.Zero);
        Console.WriteLine("v_inject3=" + (v3 != 0));
        int d3 = 0;
        while (PeekMessageW(out mv1, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref mv1);
            DispatchMessageW(ref mv1);
            d3++;
            if (d3 >= 200) break;
        }
        int v4 = PostMessageW(hForm, WM_VSCROLL, new IntPtr(SB_THUMBPOSITION | (80 << 16)), IntPtr.Zero);
        Console.WriteLine("v_inject4=" + (v4 != 0));
        int d4 = 0;
        while (PeekMessageW(out mv1, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref mv1);
            DispatchMessageW(ref mv1);
            d4++;
            if (d4 >= 200) break;
        }
        Console.WriteLine("v_step_count=" + VStepCount);
        Console.WriteLine("v_vscroll_count=" + VvscrollCount);
        Console.WriteLine("v_seq=" + VSeq);
        Console.WriteLine("v_last_status=" + VLastStatus);
        Console.WriteLine("v_final_pos=" + GetScrollPos(hScrollV, SB_CTL));

        // --- horizontal scroll events (independent axis) ---
        HStepCount = 0; HvscrollCount = 0; HSeq = "";
        int h1 = PostMessageW(hForm, WF_HSCROLL_STEP, new IntPtr(10), IntPtr.Zero);
        Console.WriteLine("h_inject1=" + (h1 != 0));
        int dh1 = 0;
        while (PeekMessageW(out mv1, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref mv1);
            DispatchMessageW(ref mv1);
            dh1++;
            if (dh1 >= 200) break;
        }
        int h2 = PostMessageW(hForm, WM_HSCROLL, new IntPtr(SB_THUMBTRACK | (20 << 16)), IntPtr.Zero);
        Console.WriteLine("h_inject2=" + (h2 != 0));
        int dh2 = 0;
        while (PeekMessageW(out mv1, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref mv1);
            DispatchMessageW(ref mv1);
            dh2++;
            if (dh2 >= 200) break;
        }
        Console.WriteLine("h_step_count=" + HStepCount);
        Console.WriteLine("h_hscroll_count=" + HvscrollCount);
        Console.WriteLine("h_seq=" + HSeq);
        Console.WriteLine("h_final_pos=" + GetScrollPos(hScrollH, SB_CTL));

        // --- per-axis isolation: the vertical bar must be untouched by the
        // horizontal events (still at 80), the horizontal bar at 20. ---
        Console.WriteLine("v_isolation=" + (GetScrollPos(hScrollV, SB_CTL) == 80));
        Console.WriteLine("h_isolation=" + (GetScrollPos(hScrollH, SB_CTL) == 20));

        // --- teardown ---
        VDestroyed = DestroyWindow(hScrollV) != 0;
        HDestroyed = DestroyWindow(hScrollH) != 0;
        StatusDestroyed = DestroyWindow(hStatus) != 0;
        Console.WriteLine("v_destroyed=" + VDestroyed);
        Console.WriteLine("h_destroyed=" + HDestroyed);
        Console.WriteLine("status_destroyed=" + StatusDestroyed);
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
