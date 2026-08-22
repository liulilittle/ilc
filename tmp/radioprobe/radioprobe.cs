using System;
using System.Runtime.InteropServices;
namespace Cases { public static class RadioProbe {
    [DllImport("user32.dll")] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr DefWindowProcW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct WNDCLASS { public int style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; }
    public delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
    public const int BS_AUTORADIOBUTTON = 9;
    public const int WS_CHILD = 0x40000000;
    public const int WS_GROUP = 0x00020000;
    public const int BM_CLICK = 0xF5;
    public const int BM_GETCHECK = 0xF0;
    public const int WM_COMMAND = 0x111;
    static WndProc proc = new WndProc(H);
    static IntPtr H(IntPtr h, int m, IntPtr w, IntPtr l) { if (m == WM_COMMAND) Console.WriteLine("got WM_COMMAND id=" + (w.ToInt64() & 0xFFFF)); return DefWindowProcW(h, m, w, l); }
    public static void Main() {
        Console.WriteLine("before reg");
        WNDCLASS wc = new WNDCLASS(); wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc); wc.lpszClassName = "RPC"; wc.hInstance = GetModuleHandleW(null);
        RegisterClassW(ref wc);
        Console.WriteLine("after reg");
        IntPtr f = CreateWindowExW(0, "RPC", "F", 0, 0, 0, 200, 200, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        IntPtr r2 = CreateWindowExW(0, "BUTTON", "R2", WS_CHILD | BS_AUTORADIOBUTTON, 20, 44, 100, 20, f, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        Console.WriteLine("created");
        Console.WriteLine("clicking r2...");
        SendMessageW(r2, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("click done, state=" + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
