using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe4 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    public const int ICC_WIN95_CLASSES = 255;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public static void Main() {
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8; icc.dwICC = ICC_WIN95_CLASSES;
        int r1 = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc_win95=" + r1);
        foreach (var cls in new[] { "SysListView32", "SysTabControl32", "msctls_statusbar32", "msctls_progress32", "msctls_trackbar32" }) {
            IntPtr h = CreateWindowExW(0, cls, "", WS_CHILD, 0, 0, 200, 100, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            Console.WriteLine(cls + "=" + (h != IntPtr.Zero) + " err=" + Marshal.GetLastWin32Error());
            if (h != IntPtr.Zero) Marshal.FreeHGlobal(h);
        }
    }
}}
