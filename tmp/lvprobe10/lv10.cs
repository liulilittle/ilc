using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe10 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    [StructLayout(LayoutKind.Sequential)]
    struct LVITEM {
        public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
        public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam;
        public int iIndent; public int iGroupId; public uint cColumns; public IntPtr puColumns;
        public IntPtr piColFmt; public int iGroup;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct LVCOLUMN {
        public uint mask; public int fmt; public int cx; public IntPtr pszText; public int cchTextMax; public int iSubItem; public int iImage; public int iOrder;
    }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_TAB_CLASSES = 8;
    public const int ICC_BAR_CLASSES = 4;
    public const int ICC_PROGRESS_CLASS = 32;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVM_SETCOLUMNW = LVM_FIRST + 96;
    public const int LVIF_TEXT = 1;
    public const int LVCF_FMT = 1; public const int LVCF_WIDTH = 2; public const int LVCF_TEXT = 4;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_OVERLAPPED = 0;
    public const int LVS_REPORT = 1;
    public static void Main() {
        Console.WriteLine("lvitem_size=" + Marshal.SizeOf(typeof(LVITEM)));
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS | ICC_LISTVIEW_CLASSES;
        InitCommonControlsEx(ref icc);
        IntPtr f = CreateWindowExW(0, "STATIC", "P", WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr h = CreateWindowExW(0, "SysListView32", "", WS_CHILD | LVS_REPORT, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        LVCOLUMN col = new LVCOLUMN();
        col.mask = LVCF_FMT | LVCF_WIDTH | LVCF_TEXT; col.fmt = 0; col.cx = 100;
        col.pszText = Marshal.StringToHGlobalUni("Col");
        GCHandle cpin = GCHandle.Alloc(col, GCHandleType.Pinned);
        int cr = (int)SendMessageW(h, LVM_SETCOLUMNW, new IntPtr(0), cpin.AddrOfPinnedObject()).ToInt64();
        cpin.Free();
        Console.WriteLine("col=" + cr);
        LVITEM item = new LVITEM();
        item.mask = LVIF_TEXT; item.iItem = -1; item.iSubItem = 0;
        item.pszText = Marshal.StringToHGlobalUni("Alpha"); item.cchTextMax = 128;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Console.WriteLine("insert=" + r.ToInt64());
        Console.WriteLine("count=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
