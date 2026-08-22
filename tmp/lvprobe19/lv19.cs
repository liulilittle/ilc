using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe19 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassInfoW(IntPtr h, string cls, IntPtr wc);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    [StructLayout(LayoutKind.Sequential)] struct WNDCLASS { public int style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public IntPtr lpszMenuName; public IntPtr lpszClassName; }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_WIN95_CLASSES = 255;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_OVERLAPPED = 0;
    public static void Main() {
        IntPtr clsBuf = Marshal.AllocHGlobal(512);
        int before = GetClassInfoW(IntPtr.Zero, "SysListView32", clsBuf);
        Console.WriteLine("classinfo_before=" + before);
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8; icc.dwICC = ICC_LISTVIEW_CLASSES;
        InitCommonControlsEx(ref icc);
        int after = GetClassInfoW(IntPtr.Zero, "SysListView32", clsBuf);
        Console.WriteLine("classinfo_after_listview=" + after);
        icc.dwICC = ICC_WIN95_CLASSES;
        InitCommonControlsEx(ref icc);
        int after2 = GetClassInfoW(IntPtr.Zero, "SysListView32", clsBuf);
        Console.WriteLine("classinfo_after_win95=" + after2);
        Marshal.FreeHGlobal(clsBuf);
    }
}}
