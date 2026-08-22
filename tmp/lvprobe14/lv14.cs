using System;
using System.Runtime.InteropServices;
using System.Text;
namespace Cases { public static class LvProbe14 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_TAB_CLASSES = 8;
    public const int ICC_BAR_CLASSES = 4;
    public const int ICC_PROGRESS_CLASS = 32;
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
        IntPtr h = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        StringBuilder cn = new StringBuilder(64);
        GetClassNameW(h, cn, 64);
        Console.WriteLine("class=" + cn.ToString());
        Console.WriteLine("count=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        // Try LVM_GETITEMCOUNT via wParam=0 lParam=0 with a timeout message? No.
        // Also test if it responds to ANY message: WM_GETTEXT
        IntPtr b = Marshal.AllocHGlobal(64);
        IntPtr r = SendMessageW(h, 0x000D, new IntPtr(32), b); // WM_GETTEXT
        Console.WriteLine("wm_gettext_ret=" + r.ToInt64());
        Marshal.FreeHGlobal(b);
    }
}}
