using System;
using System.Runtime.InteropServices;
namespace Cases { public static class RadioProbe6 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint="RegisterClassExW", CharSet=CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("kernel32.dll", EntryPoint="GetModuleHandleW", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string n);
    [DllImport("user32.dll")] static extern int GetLastError();
    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEX {
        public int cbSize; public int style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
        public IntPtr lpszMenuName; public IntPtr lpszClassName; public IntPtr hIconSm;
    }
    public delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
    public const int BS_AUTORADIOBUTTON = 9;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_GROUP = 0x00020000;
    public const int BM_CLICK = 0xF5;
    public const int BM_GETCHECK = 0xF0;
    public const int WM_COMMAND = 0x111;
    static WndProc proc = new WndProc(H);
    static int cmdCount = 0;
    static IntPtr H(IntPtr h, int m, IntPtr w, IntPtr l) {
        if (m == WM_COMMAND) { cmdCount++; Console.WriteLine("WM_COMMAND id=" + (w.ToInt64() & 0xFFFF)); }
        return IntPtr.Zero;
    }
    public static void Main() {
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = 3;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc);
        wc.hInstance = GetModuleHandleW(null);
        IntPtr cn = Marshal.StringToHGlobalUni("RP6Class");
        wc.lpszClassName = cn;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("atom=" + atom);
        IntPtr f = CreateWindowExW(0, "RP6Class", "F", 0, 0, 0, 200, 200, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        Console.WriteLine("form=" + f.ToInt64() + " err=" + Marshal.GetLastWin32Error());
        IntPtr r1 = CreateWindowExW(0, "BUTTON", "R1", WS_CHILD | WS_GROUP | BS_AUTORADIOBUTTON, 20, 20, 100, 20, f, new IntPtr(501), wc.hInstance, IntPtr.Zero);
        IntPtr r2 = CreateWindowExW(0, "BUTTON", "R2", WS_CHILD | BS_AUTORADIOBUTTON, 20, 44, 100, 20, f, new IntPtr(502), wc.hInstance, IntPtr.Zero);
        IntPtr r3 = CreateWindowExW(0, "BUTTON", "R3", WS_CHILD | BS_AUTORADIOBUTTON, 20, 68, 100, 20, f, new IntPtr(503), wc.hInstance, IntPtr.Zero);
        Console.WriteLine("r=" + r1.ToInt64() + "," + r2.ToInt64() + "," + r3.ToInt64());
        Console.WriteLine("initial=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("click r2...");
        SendMessageW(r2, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("afterR2=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + " cmds=" + cmdCount);
    }
}}
