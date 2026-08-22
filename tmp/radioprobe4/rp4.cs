using System;
using System.Runtime.InteropServices;
namespace Cases { public static class RadioProbe4 {
    [DllImport("user32.dll")] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct WNDCLASS { public int style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; }
    public delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
    public const int BS_AUTORADIOBUTTON = 9;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_GROUP = 0x00020000;
    public const int BM_SETCHECK = 0xF1;
    public const int BM_GETCHECK = 0xF0;
    static WndProc proc = new WndProc(H);
    static IntPtr H(IntPtr h, int m, IntPtr w, IntPtr l) { return IntPtr.Zero; }
    public static void Main() {
        WNDCLASS wc = new WNDCLASS(); wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc); wc.lpszClassName = "RP4"; wc.hInstance = GetModuleHandleW(null);
        ushort r = RegisterClassW(ref wc);
        Console.WriteLine("regRet=" + r);
        IntPtr f = CreateWindowExW(0, "RP4", "F", 0, 0, 0, 200, 200, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        Console.WriteLine("form=" + f.ToInt64());
        IntPtr r1 = CreateWindowExW(0, "BUTTON", "R1", WS_CHILD | WS_GROUP | BS_AUTORADIOBUTTON, 20, 20, 100, 20, f, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        Console.WriteLine("r1=" + r1.ToInt64());
        SendMessageW(r1, BM_SETCHECK, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("afterSet=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
