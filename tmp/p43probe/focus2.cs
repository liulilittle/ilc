using System;
using System.Runtime.InteropServices;

public static class P43Focus2
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
    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX
    {
        public int dwSize;
        public int dwICC;
    }

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel procDel = new WndProcDel(WndProc);

    public const string WC_LISTVIEW = "SysListView32";
    public const int WS_CHILD = 0x40000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int LVS_REPORT = 0x0001;
    public const int ICC_LISTVIEW_CLASSES = 0x00000001;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_GETCOLUMNW = LVM_FIRST + 95;
    public const int LVM_INSERTCOLUMNW = LVM_FIRST + 97;
    public const int LVM_GETHEADER = LVM_FIRST + 31;
    public const int LVM_SETCOLUMNORDERARRAY = LVM_FIRST + 58;
    public const int LVM_GETCOLUMNORDERARRAY = LVM_FIRST + 59;
    public const int HDM_FIRST = 0x1200;
    public const int LVCF_TEXT = 0x0004;
    public const int LVCF_WIDTH = 0x0002;
    public const int LVCF_FMT = 0x0001;
    public const int LVCF_SUBITEM = 0x0008;
    public const int ID_LIST43 = 0x2A0;

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        return DefProc(hwnd, msg, wParam, lParam);
    }

    public static int AddColumn(IntPtr h, string text, int cx)
    {
        int index = ColumnCount(h);
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] col = new int[13];
        col[0] = LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM;
        col[1] = 0;
        col[2] = cx;
        long pt = p.ToInt64();
        col[4] = (int)(pt & 0xFFFFFFFFL);
        col[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        col[6] = 128;
        col[7] = index;
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_INSERTCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public static int ColumnCount(IntPtr h)
    {
        IntPtr hHead = SendMessageW(h, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        IntPtr r = SendMessageW(hHead, HDM_FIRST + 0, IntPtr.Zero, IntPtr.Zero);
        return (int)r.ToInt64();
    }

    public static string GetColText(IntPtr h, int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] col = new int[13];
        col[0] = LVCF_TEXT;
        col[6] = 128;
        long bp = buf.ToInt64();
        col[4] = (int)(bp & 0xFFFFFFFFL);
        col[5] = (int)((bp >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        SendMessageW(h, LVM_GETCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        if (s == null) { s = ""; }
        return s;
    }

    public static void SetOrder(IntPtr h, int a, int b, int c, string tag)
    {
        int[] order = new int[3] { a, b, c };
        GCHandle pin = GCHandle.Alloc(order, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_SETCOLUMNORDERARRAY, new IntPtr(3), pin.AddrOfPinnedObject());
        pin.Free();
        Console.WriteLine(tag + "_set_ret=" + (r.ToInt64() != 0));
    }

    public static void GetOrder(IntPtr h, string tag)
    {
        int[] back = new int[3] { 99, 99, 99 };
        GCHandle pin = GCHandle.Alloc(back, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_GETCOLUMNORDERARRAY, new IntPtr(3), pin.AddrOfPinnedObject());
        pin.Free();
        Console.WriteLine(tag + "_get_ret=" + (r.ToInt64() != 0) + ";o0=" + back[0] + ";o1=" + back[1] + ";o2=" + back[2]);
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procDel);
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P43Focus2F");
        RegisterClassExW(ref wc);
        IntPtr hParent = CreateWindowExW(0, "P43Focus2F", "P43FF",
            WS_OVERLAPPED, 0, 0, 400, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = Marshal.SizeOf(typeof(INITCOMMONCONTROLSEX));
        icc.dwICC = ICC_LISTVIEW_CLASSES;
        InitCommonControlsEx(ref icc);

        IntPtr hList = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD | LVS_REPORT, 0, 0, 320, 120, hParent, new IntPtr(ID_LIST43), hInst, IntPtr.Zero);
        AddColumn(hList, "Name", 90);
        AddColumn(hList, "Value", 70);
        AddColumn(hList, "Kind", 70);
        Console.WriteLine("p43f_colcount=" + ColumnCount(hList));

        // exact Phase-43 sequence
        GetOrder(hList, "p43f_fresh");
        SetOrder(hList, 2, 0, 1, "p43f_set1");
        GetOrder(hList, "p43f_after1");
        Console.WriteLine("p43f_col0_text=" + GetColText(hList, 0));
        SetOrder(hList, 0, 1, 2, "p43f_reset");
        GetOrder(hList, "p43f_afterreset");

        Console.WriteLine("p43f_destroyed=" + (DestroyWindow(hList) != 0));
        Console.WriteLine("p43f_parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}
