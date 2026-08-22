using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe6 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
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
        // Phase 16 exact combo + listview bit
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS | ICC_LISTVIEW_CLASSES;
        Console.WriteLine("icc_combo=" + InitCommonControlsEx(ref icc));
        // Create a real parent (the winforms1 form shape)
        IntPtr f = CreateWindowExW(0, "STATIC", "P", WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        Console.WriteLine("form=" + (f != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
        IntPtr h = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        Console.WriteLine("listview=" + (h != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
        IntPtr t = CreateWindowExW(0, "SysTabControl32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        Console.WriteLine("tab=" + (t != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
    }
}}
