using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe15 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_TAB_CLASSES = 8;
    public const int ICC_BAR_CLASSES = 4;
    public const int ICC_PROGRESS_CLASS = 32;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_OVERLAPPED = 0;
    public static void Main() {
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS | ICC_LISTVIEW_CLASSES;
        InitCommonControlsEx(ref icc);
        IntPtr f = CreateWindowExW(0, "STATIC", "P", WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr lv = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr st = CreateWindowExW(0, "STATIC", "Hello", WS_CHILD, 0, 0, 100, 20, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr tab = CreateWindowExW(0, "SysTabControl32", "", WS_CHILD, 0, 0, 100, 20, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr b = Marshal.AllocHGlobal(64);
        IntPtr rlv = SendMessageW(lv, 0x000D, new IntPtr(32), b); // WM_GETTEXT on listview
        Console.WriteLine("lv_gettext=" + rlv.ToInt64());
        IntPtr rst = SendMessageW(st, 0x000D, new IntPtr(32), b); // WM_GETTEXT on static
        Console.WriteLine("static_gettext=" + rst.ToInt64());
        IntPtr rtab = SendMessageW(tab, 0x000D, new IntPtr(32), b); // WM_GETTEXT on tab
        Console.WriteLine("tab_gettext=" + rtab.ToInt64());
        Marshal.FreeHGlobal(b);
    }
}}
