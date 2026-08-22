using System;
using System.Runtime.InteropServices;

public static class P36Probe
{
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32", EntryPoint = "GetWindowLongW")]
    public static extern IntPtr GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32", EntryPoint = "SetScrollRange")]
    public static extern int SetScrollRange(IntPtr hwnd, int nBar, int nMinPos, int nMaxPos, bool fRedraw);
    [DllImport("user32", EntryPoint = "SetScrollPos")]
    public static extern int SetScrollPos(IntPtr hwnd, int nBar, int nPos, bool fRedraw);
    [DllImport("user32", EntryPoint = "GetScrollPos")]
    public static extern int GetScrollPos(IntPtr hwnd, int nBar);
    [DllImport("user32", EntryPoint = "ShowScrollBar")]
    public static extern int ShowScrollBar(IntPtr hwnd, int wBar, bool bShow);
    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "PostMessageW")]
    public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "PeekMessageW")]
    public static extern int PeekMessageW(out MSG lpMsg, IntPtr hwnd, int wMsgFilterMin, int wMsgFilterMax, int wRemoveMsg);
    [DllImport("user32", EntryPoint = "TranslateMessage")]
    public static extern int TranslateMessage(ref MSG lpMsg);
    [DllImport("user32", EntryPoint = "DispatchMessageW")]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);
    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd; public int message; public IntPtr wParam; public IntPtr lParam;
        public int time; public int ptX; public int ptY;
    }

    public const int WS_CHILD = 0x40000000;
    public const int WS_VSCROLL = 0x00200000;
    public const int WS_HSCROLL = 0x00100000;
    public const int GWL_STYLE = -16;
    public const int GWLP_WNDPROC = -4;
    public const int SB_HORZ = 0;
    public const int SB_VERT = 1;
    public const int SB_BOTH = 3;
    public const int SB_LINEUP = 0;
    public const int SB_LINEDOWN = 1;
    public const int SB_PAGEUP = 2;
    public const int SB_PAGEDOWN = 3;
    public const int SB_THUMBPOSITION = 4;
    public const int SB_THUMBTRACK = 5;
    public const int WM_VSCROLL = 0x0115;
    public const int WM_HSCROLL = 0x0114;
    public const int PM_REMOVE = 0x0001;
    public const int P36_LINE = 10;
    public const int P36_PAGE = 40;

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel PanelProcDel;
    public static IntPtr P36PanelHwnd;
    public static string Seq = "";
    public static int VCount;
    public static int HCount;

    public static IntPtr PanelProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_VSCROLL || msg == WM_HSCROLL)
        {
            if (hwnd != P36PanelHwnd) return DefWindowProcW(hwnd, msg, wParam, lParam);
            int bar = msg == WM_VSCROLL ? SB_VERT : SB_HORZ;
            int code = (int)(wParam.ToInt64() & 0xFFFFL);
            int thumb = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            int delta = 0;
            if (code == SB_LINEUP || code == SB_LINEDOWN) delta = code == SB_LINEUP ? -P36_LINE : P36_LINE;
            else if (code == SB_PAGEUP || code == SB_PAGEDOWN) delta = code == SB_PAGEUP ? -P36_PAGE : P36_PAGE;
            if (code == SB_THUMBTRACK || code == SB_THUMBPOSITION)
            {
                SetScrollPos(hwnd, bar, thumb, false);
            }
            else if (delta != 0)
            {
                int cur = GetScrollPos(hwnd, bar);
                SetScrollPos(hwnd, bar, cur + delta, false);
            }
            int pos = GetScrollPos(hwnd, bar);
            string name;
            if (msg == WM_VSCROLL)
            {
                if (code == SB_LINEUP) name = "lineup";
                else if (code == SB_LINEDOWN) name = "linedown";
                else if (code == SB_PAGEUP) name = "pageup";
                else if (code == SB_PAGEDOWN) name = "pagedown";
                else if (code == SB_THUMBTRACK) name = "track";
                else if (code == SB_THUMBPOSITION) name = "thumb";
                else name = "code" + code;
                Seq = Seq + "v:" + name + pos + ";";
                VCount++;
            }
            else
            {
                if (code == SB_LINEUP) name = "lineleft";
                else if (code == SB_LINEDOWN) name = "lineright";
                else if (code == SB_PAGEUP) name = "pageleft";
                else if (code == SB_PAGEDOWN) name = "pageright";
                else if (code == SB_THUMBTRACK) name = "track";
                else if (code == SB_THUMBPOSITION) name = "thumb";
                else name = "code" + code;
                Seq = Seq + "h:" + name + pos + ";";
                HCount++;
            }
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public static void Drain(int cap)
    {
        MSG m;
        int d = 0;
        while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
            d++;
            if (d >= cap) break;
        }
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        IntPtr hParent = CreateWindowExW(0, "STATIC", "p36parent", 0, 0, 0, 320, 220, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        IntPtr hPanel = CreateWindowExW(0, "STATIC", "",
            WS_CHILD | WS_VSCROLL | WS_HSCROLL, 20, 30, 300, 200, hParent, IntPtr.Zero, hInst, IntPtr.Zero);
        P36PanelHwnd = hPanel;
        Console.WriteLine("p36_panel_created=" + (hPanel != IntPtr.Zero));

        PanelProcDel = PanelProc;
        IntPtr fn = Marshal.GetFunctionPointerForDelegate(PanelProcDel);
        IntPtr oldProc = SetWindowLongPtrW(hPanel, GWLP_WNDPROC, fn);
        Console.WriteLine("p36_subclass_ok=" + (oldProc != IntPtr.Zero));

        IntPtr style = GetWindowLongW(hPanel, GWL_STYLE);
        long sv = style.ToInt64();
        Console.WriteLine("p36_style_v=" + ((sv & WS_VSCROLL) != 0));
        Console.WriteLine("p36_style_h=" + ((sv & WS_HSCROLL) != 0));

        SetScrollRange(hPanel, SB_VERT, 0, 100, false);
        SetScrollRange(hPanel, SB_HORZ, 0, 50, false);
        int v0 = GetScrollPos(hPanel, SB_VERT);
        int h0 = GetScrollPos(hPanel, SB_HORZ);
        Console.WriteLine("p36_v_pos0=" + v0);
        Console.WriteLine("p36_h_pos0=" + h0);

        int v25prev = SetScrollPos(hPanel, SB_VERT, 25, false);
        int v25 = GetScrollPos(hPanel, SB_VERT);
        Console.WriteLine("p36_v25_prev=" + v25prev);
        Console.WriteLine("p36_v25=" + v25);
        int h5prev = SetScrollPos(hPanel, SB_HORZ, 5, false);
        int h5 = GetScrollPos(hPanel, SB_HORZ);
        Console.WriteLine("p36_h5_prev=" + h5prev);
        Console.WriteLine("p36_h5=" + h5);

        SetScrollPos(hPanel, SB_VERT, 120, false);
        int ch = GetScrollPos(hPanel, SB_VERT);
        SetScrollPos(hPanel, SB_VERT, -5, false);
        int cl = GetScrollPos(hPanel, SB_VERT);
        Console.WriteLine("p36_clamp_high=" + ch);
        Console.WriteLine("p36_clamp_low=" + cl);

        int rv = SetScrollPos(hPanel, SB_VERT, 30, false);
        int rh = SetScrollPos(hPanel, SB_HORZ, 10, false);
        Console.WriteLine("p36_reset_v_prev=" + rv);
        Console.WriteLine("p36_reset_h_prev=" + rh);

        Seq = ""; VCount = 0; HCount = 0;
        PostMessageW(hPanel, WM_VSCROLL, new IntPtr(SB_LINEDOWN), IntPtr.Zero);
        PostMessageW(hPanel, WM_VSCROLL, new IntPtr(SB_LINEUP), IntPtr.Zero);
        PostMessageW(hPanel, WM_VSCROLL, new IntPtr(SB_PAGEDOWN), IntPtr.Zero);
        PostMessageW(hPanel, WM_VSCROLL, new IntPtr(SB_THUMBTRACK | (80 << 16)), IntPtr.Zero);
        PostMessageW(hPanel, WM_VSCROLL, new IntPtr(SB_THUMBPOSITION | (65 << 16)), IntPtr.Zero);
        PostMessageW(hPanel, WM_HSCROLL, new IntPtr(SB_LINEDOWN), IntPtr.Zero);
        PostMessageW(hPanel, WM_HSCROLL, new IntPtr(SB_PAGEUP), IntPtr.Zero);
        PostMessageW(hPanel, WM_HSCROLL, new IntPtr(SB_THUMBTRACK | (35 << 16)), IntPtr.Zero);
        Drain(200);
        Console.WriteLine("p36_vscroll_count=" + VCount);
        Console.WriteLine("p36_hscroll_count=" + HCount);
        Console.WriteLine("p36_seq=" + Seq);

        int hideRet = ShowScrollBar(hPanel, SB_BOTH, false);
        int vh = GetScrollPos(hPanel, SB_VERT);
        int hh = GetScrollPos(hPanel, SB_HORZ);
        Console.WriteLine("p36_hide_ret=" + (hideRet != 0));
        Console.WriteLine("p36_pos_v_hidden=" + vh);
        Console.WriteLine("p36_pos_h_hidden=" + hh);
        int showRet = ShowScrollBar(hPanel, SB_BOTH, true);
        Console.WriteLine("p36_show_ret=" + (showRet != 0));
        int vf = GetScrollPos(hPanel, SB_VERT);
        int hf = GetScrollPos(hPanel, SB_HORZ);
        Console.WriteLine("p36_final_v=" + vf);
        Console.WriteLine("p36_final_h=" + hf);

        // P9: DefWindowProc default behavior for an UNSUBCLASSED window's scroll messages.
        IntPtr hPlain = CreateWindowExW(0, "STATIC", "",
            WS_CHILD | WS_VSCROLL | WS_HSCROLL, 0, 0, 100, 100, hParent, IntPtr.Zero, hInst, IntPtr.Zero);
        SetScrollRange(hPlain, SB_VERT, 0, 100, false);
        IntPtr dp = DefWindowProcW(hPlain, WM_VSCROLL, new IntPtr(SB_LINEDOWN), IntPtr.Zero);
        int dppos = GetScrollPos(hPlain, SB_VERT);
        Console.WriteLine("p36_defproc_ret=" + dp.ToInt64());
        Console.WriteLine("p36_defproc_pos=" + dppos);
        Console.WriteLine("p36_destroy_plain=" + (DestroyWindow(hPlain) != 0));

        Console.WriteLine("p36_destroyed=" + (DestroyWindow(hPanel) != 0));
        Console.WriteLine("p36_destroy_parent=" + (DestroyWindow(hParent) != 0));
    }
}
