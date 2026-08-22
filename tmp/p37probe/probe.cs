using System;
using System.Runtime.InteropServices;

public static class P37Probe
{
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
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
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("user32", EntryPoint = "GetWindowLongW")]
    public static extern IntPtr GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32", EntryPoint = "GetParent")]
    public static extern IntPtr GetParent(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd; public int message; public IntPtr wParam; public IntPtr lParam;
        public int time; public int ptX; public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- constants ----
    public const int WS_CHILD = 0x40000000;
    public const int WS_TABSTOP = 0x00010000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int LBS_NOTIFY = 0x0001;
    public const int LBS_NOINTEGRALHEIGHT = 0x0100;
    public const int WM_COMMAND = 0x0111;

    public const int LB_ADDSTRING = 0x0180;
    public const int LB_INSERTSTRING = 0x0181;
    public const int LB_SETCURSEL = 0x0186;
    public const int LB_DELETESTRING = 0x0182;
    public const int LB_RESETCONTENT = 0x0184;
    public const int LB_GETCURSEL = 0x0188;
    public const int LB_GETTEXT = 0x0189;
    public const int LB_GETTEXTLEN = 0x018A;
    public const int LB_GETCOUNT = 0x018B;
    public const int LB_GETTOPINDEX = 0x018E;
    public const int LB_FINDSTRING = 0x018F;
    public const int LB_SETTOPINDEX = 0x0197;
    public const int LB_GETITEMDATA = 0x0199;
    public const int LB_SETITEMDATA = 0x019A;
    public const int LBN_SELCHANGE = 1;
    public const int LBN_DBLCLK = 2;
    public const int LB_ERR = -1;

    public static int lastNotif = -99;
    public static int wmCmdCount = 0;
    public static WndProcDel procDel = new WndProcDel(WndProc);

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_COMMAND)
        {
            wmCmdCount++;
            lastNotif = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
        }
        return DefProc(hwnd, msg, wParam, lParam);
    }

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    public static IntPtr GetItemText(IntPtr h, int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        SendMessageW(h, LB_GETTEXT, new IntPtr(index), buf);
        return buf;
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        IntPtr pfn = Marshal.GetFunctionPointerForDelegate(procDel);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = 0;
        wc.lpfnWndProc = pfn;
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P37ProbeParent");
        ushort reg = RegisterClassExW(ref wc);
        Console.WriteLine("reg=" + (reg != 0));
        IntPtr hParent = CreateWindowExW(0, "P37ProbeParent", "P37Parent",
            WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("parent_created=" + (hParent != IntPtr.Zero));

        // Short-window listbox (200x60, LBS_NOINTEGRALHEIGHT -> non-integral height
        // allowed so the top index can really scroll).
        IntPtr hList = CreateWindowExW(0, "LISTBOX", "",
            WS_CHILD | LBS_NOTIFY | LBS_NOINTEGRALHEIGHT, 0, 0, 200, 60,
            hParent, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("list_created=" + (hList != IntPtr.Zero));
        Console.WriteLine("list_parent=" + (GetParent(hList) == hParent));

        // Add 7 items (Alpha..Golf) -- LB_ADDSTRING returns the index.
        string[] items = { "Alpha", "Beta", "Gamma", "Delta", "Echo", "Foxtrot", "Golf" };
        for (int i = 0; i < items.Length; i++)
        {
            IntPtr p = Marshal.StringToHGlobalUni(items[i]);
            IntPtr r = SendMessageW(hList, LB_ADDSTRING, IntPtr.Zero, p);
            Marshal.FreeHGlobal(p);
            Console.WriteLine("add" + i + "=" + (int)r.ToInt64());
        }
        Console.WriteLine("count7=" + (int)SendMessageW(hList, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // LB_INSERTSTRING at explicit index 2 -- returns the inserted index.
        IntPtr ins = Marshal.StringToHGlobalUni("Inserted");
        IntPtr ir = SendMessageW(hList, LB_INSERTSTRING, new IntPtr(2), ins);
        Marshal.FreeHGlobal(ins);
        Console.WriteLine("insert_ret=" + (int)ir.ToInt64());
        Console.WriteLine("count8=" + (int)SendMessageW(hList, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        IntPtr b0 = GetItemText(hList, 0);
        IntPtr b1 = GetItemText(hList, 1);
        IntPtr b2 = GetItemText(hList, 2);
        Console.WriteLine("text0=" + Marshal.PtrToStringUni(b0));
        Console.WriteLine("text1=" + Marshal.PtrToStringUni(b1));
        Console.WriteLine("text2=" + Marshal.PtrToStringUni(b2));
        Marshal.FreeHGlobal(b0); Marshal.FreeHGlobal(b1); Marshal.FreeHGlobal(b2);

        // LB_GETTEXTLEN: in-range length, out-of-range -1.
        Console.WriteLine("len0=" + (int)SendMessageW(hList, LB_GETTEXTLEN, new IntPtr(0), IntPtr.Zero).ToInt64());
        Console.WriteLine("len99=" + (int)SendMessageW(hList, LB_GETTEXTLEN, new IntPtr(99), IntPtr.Zero).ToInt64());

        // LB_SETITEMDATA returns the PREVIOUS item data (0 for unset).
        IntPtr s1 = SendMessageW(hList, LB_SETITEMDATA, new IntPtr(3), new IntPtr(42));
        Console.WriteLine("setdata_prev0=" + (int)s1.ToInt64());
        IntPtr g1 = SendMessageW(hList, LB_GETITEMDATA, new IntPtr(3), IntPtr.Zero);
        Console.WriteLine("getdata42=" + (int)g1.ToInt64());
        IntPtr s2 = SendMessageW(hList, LB_SETITEMDATA, new IntPtr(3), new IntPtr(7));
        Console.WriteLine("setdata_prev42=" + (int)s2.ToInt64());
        IntPtr g2 = SendMessageW(hList, LB_GETITEMDATA, new IntPtr(3), IntPtr.Zero);
        Console.WriteLine("getdata7=" + (int)g2.ToInt64());

        // LB_FINDSTRING: prefix match, search starts AFTER the given index and
        // wraps. Index -1 = start from 0.
        IntPtr f1 = Marshal.StringToHGlobalUni("Beta");
        IntPtr fb = SendMessageW(hList, LB_FINDSTRING, new IntPtr(-1), f1);
        Console.WriteLine("find_beta=" + (int)fb.ToInt64());
        Marshal.FreeHGlobal(f1);
        IntPtr f2 = Marshal.StringToHGlobalUni("Gamma");
        IntPtr fg = SendMessageW(hList, LB_FINDSTRING, new IntPtr(1), f2);
        Console.WriteLine("find_gamma_from1=" + (int)fg.ToInt64());
        Marshal.FreeHGlobal(f2);
        IntPtr f3 = Marshal.StringToHGlobalUni("Zzz");
        IntPtr fz = SendMessageW(hList, LB_FINDSTRING, new IntPtr(0), f3);
        Console.WriteLine("find_zzz=" + (int)fz.ToInt64());
        Marshal.FreeHGlobal(f3);
        // Prefix match: "Al" should match Alpha (index 0) even though the full
        // string is "Alpha".
        IntPtr f4 = Marshal.StringToHGlobalUni("Al");
        IntPtr fa = SendMessageW(hList, LB_FINDSTRING, new IntPtr(-1), f4);
        Console.WriteLine("find_prefix_al=" + (int)fa.ToInt64());
        Marshal.FreeHGlobal(f4);

        // TopIndex: initial 0, set 3 -> read 3, set 0 -> read 0.
        Console.WriteLine("top0=" + (int)SendMessageW(hList, LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(hList, LB_SETTOPINDEX, new IntPtr(3), IntPtr.Zero);
        Console.WriteLine("top3=" + (int)SendMessageW(hList, LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(hList, LB_SETTOPINDEX, new IntPtr(0), IntPtr.Zero);
        Console.WriteLine("top_reset=" + (int)SendMessageW(hList, LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // LB_DELETESTRING at 1 -> 7 remaining, text at 1 = Inserted (shifted up).
        IntPtr dr = SendMessageW(hList, LB_DELETESTRING, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("del_ret=" + (int)dr.ToInt64());
        Console.WriteLine("count_after_del=" + (int)SendMessageW(hList, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        IntPtr bd = GetItemText(hList, 1);
        Console.WriteLine("text1_after_del=" + Marshal.PtrToStringUni(bd));
        Marshal.FreeHGlobal(bd);

        // LB_RESETCONTENT -> count 0.
        SendMessageW(hList, LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("count_after_clear=" + (int)SendMessageW(hList, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // Re-add 3 + select 2 + post WM_COMMAND(id|(LBN_DBLCLK<<16)) + drain ->
        // the WndProc decodes notif = LBN_DBLCLK (2).
        string[] again = { "A", "B", "C" };
        for (int i = 0; i < again.Length; i++)
        {
            IntPtr p = Marshal.StringToHGlobalUni(again[i]);
            SendMessageW(hList, LB_ADDSTRING, IntPtr.Zero, p);
            Marshal.FreeHGlobal(p);
        }
        Console.WriteLine("count3=" + (int)SendMessageW(hList, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(hList, LB_SETCURSEL, new IntPtr(2), IntPtr.Zero);
        Console.WriteLine("cursel2=" + (int)SendMessageW(hList, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("inject=" + (PostMessageW(hParent, WM_COMMAND, new IntPtr(0x240 | (LBN_DBLCLK << 16)), hList) != 0));
        MSG msg;
        int d = 0;
        while (PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
            d++;
            if (d >= 200) break;
        }
        Console.WriteLine("wmcmd=" + wmCmdCount);
        Console.WriteLine("notif_dblclk=" + (lastNotif == LBN_DBLCLK));

        Console.WriteLine("destroy=" + (DestroyWindow(hList) != 0));
        Console.WriteLine("destroy_parent=" + (DestroyWindow(hParent) != 0));
    }
}
