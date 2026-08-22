using System;
using System.Runtime.InteropServices;

public static class P38Probe
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
    [DllImport("user32", EntryPoint = "GetParent")]
    public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32", EntryPoint = "IsWindowVisible")]
    public static extern int IsWindowVisible(IntPtr hwnd);

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
    public const int WS_OVERLAPPED = 0x00000000;
    public const int CBS_DROPDOWNLIST = 0x0003;
    public const int WM_COMMAND = 0x0111;

    public const int CB_ADDSTRING = 0x0143;
    public const int CB_GETCOUNT = 0x0146;
    public const int CB_GETCURSEL = 0x0147;
    public const int CB_GETLBTEXT = 0x0148;
    public const int CB_SETCURSEL = 0x014E;
    public const int CB_GETLBTEXTLEN = 0x0149;
    public const int CB_INSERTSTRING = 0x014A;
    public const int CB_RESETCONTENT = 0x014B;
    public const int CB_FINDSTRING = 0x014C;
    public const int CB_DELETESTRING = 0x0144;
    public const int CB_GETITEMDATA = 0x0150;
    public const int CB_SETITEMDATA = 0x0151;
    public const int CB_GETTOPINDEX = 0x015B;
    public const int CB_SETTOPINDEX = 0x015C;
    public const int CBN_SELCHANGE = 1;
    public const int CBN_DBLCLK = 2;
    public const int CB_ERR = -1;

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

    public static string GetItemText(IntPtr h, int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        SendMessageW(h, CB_GETLBTEXT, new IntPtr(index), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
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
        wc.lpszClassName = Marshal.StringToHGlobalUni("P38ProbeParent");
        ushort reg = RegisterClassExW(ref wc);
        Console.WriteLine("reg=" + (reg != 0));
        IntPtr hParent = CreateWindowExW(0, "P38ProbeParent", "P38Parent",
            WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("parent_created=" + (hParent != IntPtr.Zero));

        // Short-window COMBOBOX (20,270,160,40) CBS_DROPDOWNLIST (list drops only
        // when opened; the combo itself is always a visible child).
        IntPtr hCombo = CreateWindowExW(0, "COMBOBOX", "",
            WS_CHILD | WS_TABSTOP | CBS_DROPDOWNLIST, 20, 270, 160, 40,
            hParent, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("combo_created=" + (hCombo != IntPtr.Zero));
        Console.WriteLine("combo_parent=" + (GetParent(hCombo) == hParent));
        Console.WriteLine("combo_visible=" + (IsWindowVisible(hCombo) != 0));

        // Add 7 items (Alpha..Golf) -- CB_ADDSTRING returns the index.
        string[] items = { "Alpha", "Beta", "Gamma", "Delta", "Echo", "Foxtrot", "Golf" };
        for (int i = 0; i < items.Length; i++)
        {
            IntPtr p = Marshal.StringToHGlobalUni(items[i]);
            IntPtr r = SendMessageW(hCombo, CB_ADDSTRING, IntPtr.Zero, p);
            Marshal.FreeHGlobal(p);
            Console.WriteLine("add" + i + "=" + (int)r.ToInt64());
        }
        Console.WriteLine("count7=" + (int)SendMessageW(hCombo, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // CB_INSERTSTRING at explicit index 2 -- returns the inserted index.
        IntPtr ins = Marshal.StringToHGlobalUni("Inserted");
        IntPtr ir = SendMessageW(hCombo, CB_INSERTSTRING, new IntPtr(2), ins);
        Marshal.FreeHGlobal(ins);
        Console.WriteLine("insert_ret=" + (int)ir.ToInt64());
        Console.WriteLine("count8=" + (int)SendMessageW(hCombo, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("text2=" + GetItemText(hCombo, 2));

        // CB_GETLBTEXTLEN: in-range length, out-of-range -1.
        Console.WriteLine("len0=" + (int)SendMessageW(hCombo, CB_GETLBTEXTLEN, new IntPtr(0), IntPtr.Zero).ToInt64());
        Console.WriteLine("len99=" + (int)SendMessageW(hCombo, CB_GETLBTEXTLEN, new IntPtr(99), IntPtr.Zero).ToInt64());

        // CB_SETITEMDATA return semantics -- probe whether it returns TRUE (1)
        // like LB_SETITEMDATA or the previous value.
        IntPtr s1 = SendMessageW(hCombo, CB_SETITEMDATA, new IntPtr(3), new IntPtr(42));
        Console.WriteLine("setdata_prev0=" + (int)s1.ToInt64());
        IntPtr g1 = SendMessageW(hCombo, CB_GETITEMDATA, new IntPtr(3), IntPtr.Zero);
        Console.WriteLine("getdata42=" + (int)g1.ToInt64());
        IntPtr s2 = SendMessageW(hCombo, CB_SETITEMDATA, new IntPtr(3), new IntPtr(7));
        Console.WriteLine("setdata_prev42=" + (int)s2.ToInt64());
        IntPtr g2 = SendMessageW(hCombo, CB_GETITEMDATA, new IntPtr(3), IntPtr.Zero);
        Console.WriteLine("getdata7=" + (int)g2.ToInt64());

        // CB_FINDSTRING: prefix match, search starts AFTER the given index and
        // wraps. Index -1 = start from 0.
        IntPtr f1 = Marshal.StringToHGlobalUni("Beta");
        IntPtr fb = SendMessageW(hCombo, CB_FINDSTRING, new IntPtr(-1), f1);
        Console.WriteLine("find_beta=" + (int)fb.ToInt64());
        Marshal.FreeHGlobal(f1);
        IntPtr f2 = Marshal.StringToHGlobalUni("Gamma");
        IntPtr fg = SendMessageW(hCombo, CB_FINDSTRING, new IntPtr(1), f2);
        Console.WriteLine("find_gamma_from1=" + (int)fg.ToInt64());
        Marshal.FreeHGlobal(f2);
        IntPtr f3 = Marshal.StringToHGlobalUni("Zzz");
        IntPtr fz = SendMessageW(hCombo, CB_FINDSTRING, new IntPtr(0), f3);
        Console.WriteLine("find_zzz=" + (int)fz.ToInt64());
        Marshal.FreeHGlobal(f3);

        // TopIndex on a CLOSED (not-dropped) combo: does CB_GETTOPINDEX read
        // back the CB_SETTOPINDEX stored value?
        Console.WriteLine("top0=" + (int)SendMessageW(hCombo, CB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(hCombo, CB_SETTOPINDEX, new IntPtr(3), IntPtr.Zero);
        Console.WriteLine("top_after_set3=" + (int)SendMessageW(hCombo, CB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(hCombo, CB_SETTOPINDEX, new IntPtr(0), IntPtr.Zero);
        Console.WriteLine("top_after_reset=" + (int)SendMessageW(hCombo, CB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // CB_DELETESTRING at 1 -> returns remaining count (7), text at 1 shifts.
        IntPtr dr = SendMessageW(hCombo, CB_DELETESTRING, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("del_ret=" + (int)dr.ToInt64());
        Console.WriteLine("count_after_del=" + (int)SendMessageW(hCombo, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("text1_after_del=" + GetItemText(hCombo, 1));

        // CB_RESETCONTENT -> count 0.
        SendMessageW(hCombo, CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("count_after_clear=" + (int)SendMessageW(hCombo, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // Re-add 3 + select 2 + post WM_COMMAND(id|(CBN_DBLCLK<<16)) + drain ->
        // the WndProc decodes notif = CBN_DBLCLK (2).
        string[] again = { "A", "B", "C" };
        for (int i = 0; i < again.Length; i++)
        {
            IntPtr p = Marshal.StringToHGlobalUni(again[i]);
            SendMessageW(hCombo, CB_ADDSTRING, IntPtr.Zero, p);
            Marshal.FreeHGlobal(p);
        }
        Console.WriteLine("count3=" + (int)SendMessageW(hCombo, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(hCombo, CB_SETCURSEL, new IntPtr(2), IntPtr.Zero);
        Console.WriteLine("cursel2=" + (int)SendMessageW(hCombo, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("inject=" + (PostMessageW(hParent, WM_COMMAND, new IntPtr(0x250 | (CBN_DBLCLK << 16)), hCombo) != 0));
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
        Console.WriteLine("notif_dblclk=" + (lastNotif == CBN_DBLCLK));

        Console.WriteLine("destroy=" + (DestroyWindow(hCombo) != 0));
        Console.WriteLine("destroy_parent=" + (DestroyWindow(hParent) != 0));
    }
}
