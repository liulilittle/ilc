using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe26 {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryW(string n);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetProcAddress(IntPtr h, string n);
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    [StructLayout(LayoutKind.Sequential)] struct DLLVERSIONINFO { public int cbSize; public int dwMajorVersion; public int dwMinorVersion; public int dwBuildNumber; public int dwPlatformID; }
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
    [DllImport("comctl32.dll")] static extern int DllGetVersion(ref DLLVERSIONINFO dvi);
    public static void Main() {
        DLLVERSIONINFO dvi = new DLLVERSIONINFO(); dvi.cbSize = Marshal.SizeOf(typeof(DLLVERSIONINFO));
        int r = DllGetVersion(ref dvi);
        Console.WriteLine("comctl32 v" + dvi.dwMajorVersion + "." + dvi.dwMinorVersion + "." + dvi.dwBuildNumber + " ret=" + r);
        // FindWindow-style class check with ICC
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS | ICC_LISTVIEW_CLASSES;
        InitCommonControlsEx(ref icc);
        IntPtr f = CreateWindowExW(0, "STATIC", "P", WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr h = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        // Check the window proc is real: IsWindow
        Console.WriteLine("iswindow=" + (h != IntPtr.Zero));
        // Try LVM_GETITEMCOUNT again after a tiny sleep (control may need paint)
        System.Threading.Thread.Sleep(50);
        Console.WriteLine("count=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
