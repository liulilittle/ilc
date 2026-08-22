using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe24 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_TAB_CLASSES = 8;
    public const int ICC_BAR_CLASSES = 4;
    public const int ICC_PROGRESS_CLASS = 32;
    public const int TCM_FIRST = 0x1300;
    public const int TCM_INSERTITEMW = TCM_FIRST + 14;
    public const int TCM_GETITEMCOUNT = TCM_FIRST + 4;
    public const int TCIF_TEXT = 1;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVIF_TEXT = 1;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_OVERLAPPED = 0;
    public static void Main() {
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS | ICC_LISTVIEW_CLASSES;
        InitCommonControlsEx(ref icc);
        IntPtr f = CreateWindowExW(0, "STATIC", "P", WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr tab = CreateWindowExW(0, "SysTabControl32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr lv = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        // TCITEMW x64: mask@0 pszText@16(8) cchTextMax@24 lParam@32(8) = 12 int32s (48B)
        IntPtr tp = Marshal.StringToHGlobalUni("TabA");
        int[] tci = new int[12];
        long p = tp.ToInt64();
        tci[0] = TCIF_TEXT;
        tci[4] = (int)(p & 0xFFFFFFFFL); tci[5] = (int)((p >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        GCHandle tpin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        IntPtr tr = SendMessageW(tab, TCM_INSERTITEMW, IntPtr.Zero, tpin.AddrOfPinnedObject());
        tpin.Free();
        Console.WriteLine("tab_insert=" + tr.ToInt64() + " count=" + SendMessageW(tab, TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        // LVITEMW x64
        IntPtr lp = Marshal.StringToHGlobalUni("Alpha");
        int[] lvi = new int[22];
        long pp = lp.ToInt64();
        lvi[0] = LVIF_TEXT; lvi[1] = -1;
        lvi[6] = (int)(pp & 0xFFFFFFFFL); lvi[7] = (int)((pp >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle lpin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr lr = SendMessageW(lv, LVM_INSERTITEMW, IntPtr.Zero, lpin.AddrOfPinnedObject());
        lpin.Free();
        Console.WriteLine("lv_insert=" + lr.ToInt64() + " count=" + SendMessageW(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
