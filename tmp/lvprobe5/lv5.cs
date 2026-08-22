using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe5 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_TREEVIEW_CLASSES = 2;
    public const int ICC_BAR_CLASSES = 4;
    public const int ICC_TAB_CLASSES = 8;
    public const int ICC_PROGRESS_CLASS = 32;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public static void Main() {
        // The EXACT winforms1 Phase 9 style call (ICC_BAR_CLASSES|ICC_PROGRESS_CLASS)
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8; icc.dwICC = ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        Console.WriteLine("icc_bar|prog=" + InitCommonControlsEx(ref icc));
        // Then re-init with listview bit
        icc.dwICC = ICC_LISTVIEW_CLASSES | ICC_TREEVIEW_CLASSES | ICC_TAB_CLASSES;
        Console.WriteLine("icc_list|tree|tab=" + InitCommonControlsEx(ref icc));
        IntPtr h = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        Console.WriteLine("listview=" + (h != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
    }
}}
